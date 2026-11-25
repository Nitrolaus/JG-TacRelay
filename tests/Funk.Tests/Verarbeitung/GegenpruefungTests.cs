// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

/// <summary>
/// Tests aus der Gegenprüfung: nicht endliche Werte, Denormale, Vertragsbrüche an der Dekodierer-Naht,
/// Nebenläufigkeit der Steuermethoden und Randfälle von Gate und Jitterpuffer. Alle Signale synthetisch,
/// alle Zufallswerte mit festem Seed, alle Zeiten simuliert.
/// </summary>
internal static class GegenpruefungTests
{
    private const long Sekunde = 1_000_000;

    // ---------------------------------------------------------------- Begrenzer und weiches Clipping

    [Test]
    public static void Begrenzer_NichtEndlicheSamples_VerstummtNichtDauerhaft()
    {
        var b = new Begrenzer();
        var x = new float[480];
        x[100] = float.PositiveInfinity;
        x[200] = float.NaN;
        x[300] = float.NegativeInfinity;
        b.Verarbeiten(x, Span<float>.Empty);
        foreach (var v in x) Pruefe.Wahr(float.IsFinite(v), "nach dem Begrenzer endlich");

        var leise = new float[480];
        var erwartet = new float[480];
        for (var k = 0; k < 100; k++)
        {
            for (var i = 0; i < 480; i++) erwartet[i] = leise[i] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * (k * 480 + i) / 48000f);
            b.Verarbeiten(leise, Span<float>.Empty);
        }
        Pruefe.Gleich(1f, b.LetzterFaktor, "nach 1 s wieder frei");
        Pruefe.Wahr(erwartet.AsSpan().SequenceEqual(leise), "leises Signal danach bitgenau unberührt");
    }

    [Test]
    public static void Begrenzer_UngueltigeEinstellungen_Abgelehnt()
    {
        Pruefe.Wirft<ArgumentException>(() => _ = new Begrenzer(new BegrenzerEinstellungen { KniebreiteDb = float.NaN }), "Kniebreite NaN");
        Pruefe.Wirft<ArgumentException>(() => _ = new Begrenzer(new BegrenzerEinstellungen { Verhaeltnis = float.NaN }), "Verhältnis NaN");
        Pruefe.Wirft<ArgumentException>(() => _ = new Begrenzer(new BegrenzerEinstellungen { SchwelleDbfs = float.NaN }), "Schwelle NaN");
        Pruefe.Wirft<ArgumentException>(() => _ = new Begrenzer(new BegrenzerEinstellungen { Abklingfaktor = 1.5f }), "Abklingfaktor über 1");
    }

    [Test]
    public static void WeichesClipping_Unendlich_WirdVollaussteuerung()
    {
        var x = new[] { 0.1f, float.PositiveInfinity, 0.1f, -0.2f, float.NegativeInfinity, -0.1f, 0.3f };
        var mem = 0f;
        WeichesClipping.Anwenden(x, ref mem);
        foreach (var v in x)
        {
            Pruefe.Wahr(float.IsFinite(v), "endlich");
            PruefeWert.Hoechstens(1.0, MathF.Abs(v), "innerhalb [-1, 1]");
        }
    }

    // ---------------------------------------------------------------- Mischer

    [Test]
    public static void Mischer_NichtEndlicheEinstellungen_StoerenAndereQuellenNicht()
    {
        var m = new Mischer { Klick = null };
        var a = new TestQuelle(0.1f) { Gleichwert = true };
        var b = new TestQuelle(0.1f) { Gleichwert = true };
        var pa = m.QuelleHinzufuegen(a, 0);
        m.QuelleHinzufuegen(b, 1);
        var aus = new float[960];
        var beide = (float)(0.2 * Math.Sqrt(0.5));

        void Bloecke(int n)
        {
            for (var i = 0; i < n; i++)
            {
                m.Mischen(aus, 2);
                foreach (var v in aus) Pruefe.Wahr(float.IsFinite(v), "Ausgabe endlich");
            }
        }

        // NaN als Funker-Pegel (etwa aus einer beschädigten Voreinstellung): gilt als 0 dB, b bleibt hörbar.
        m.SetzeFunkerPegel(pa, float.NaN);
        Bloecke(3);
        PruefeWert.Nahe(beide, aus[^1], 1e-5, "NaN-Pegel wie 0 dB, beide Quellen hörbar");

        // +Unendlich: auf die Höchstverstärkung begrenzt; danach zurück auf 0 dB, der Begrenzer gibt wieder frei.
        m.SetzeFunkerPegel(pa, float.PositiveInfinity);
        Bloecke(3);
        PruefeWert.Mindestens(0.5, aus[^1], "laut, aber endlich");
        m.SetzeFunkerPegel(pa, 0f);
        Bloecke(150);
        PruefeWert.Nahe(beide, aus[^1], 1e-5, "nach +Unendlich wieder normal");

        // -Unendlich ist Stummschaltung.
        m.SetzeFunkerPegel(pa, float.NegativeInfinity);
        Bloecke(3);
        PruefeWert.Nahe(beide / 2, aus[^1], 1e-5, "minus unendlich schaltet nur a stumm");
        m.SetzeFunkerPegel(pa, 0f);

        // NaN bei Panorama und Netzlautstärke: Mitte bzw. 0 dB. NaN als Absenkung wird übergangen.
        m.SetzeNetzPanorama(0, float.NaN);
        m.SetzeNetzLautstaerke(1, float.NaN);
        m.AbsenkungDb = float.NaN;
        Bloecke(3);
        PruefeWert.Nahe(beide, aus[^2], 1e-5, "links");
        PruefeWert.Nahe(beide, aus[^1], 1e-5, "rechts");
        Pruefe.Gleich(-12f, m.AbsenkungDb, "Absenkung unverändert");
        var (l, r) = Mischer.PanoramaFaktoren(float.NaN);
        PruefeWert.Nahe(Math.Sqrt(0.5), l, 1e-6, "Panorama NaN links");
        PruefeWert.Nahe(Math.Sqrt(0.5), r, 1e-6, "Panorama NaN rechts");
    }

    /// <summary>Quelle, die nicht endliche Werte liefert (etwa ein fehlerhafter Dekodierer).</summary>
    private sealed class KaputteQuelle : IKlangquelle
    {
        public QuellenEreignis Lesen(Span<float> ziel)
        {
            for (var i = 0; i < ziel.Length; i++) ziel[i] = (i % 3) switch { 0 => float.NaN, 1 => float.PositiveInfinity, _ => float.NegativeInfinity };
            return QuellenEreignis.Aktiv;
        }
    }

    [Test]
    public static void Mischer_KaputteQuelle_UebrigeBleibenHoerbar()
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null };
        var kaputt = m.QuelleHinzufuegen(new KaputteQuelle(), 0);
        m.QuelleHinzufuegen(new TestQuelle(0.25f) { Gleichwert = true }, 1);
        var aus = new float[480];
        for (var i = 0; i < 5; i++) m.Mischen(aus, 1);
        foreach (var v in aus) Pruefe.Wahr(float.IsFinite(v), "endlich");
        m.QuelleEntfernen(kaputt);
        for (var i = 0; i < 100; i++) m.Mischen(aus, 1);
        PruefeWert.Nahe(0.25, aus[^1], 1e-6, "nach Entfernen der kaputten Quelle unverfälscht");
    }

    [Test]
    public static void Mischer_UngueltigeEinstellungen_Abgelehnt()
    {
        Pruefe.Wirft<ArgumentException>(() => _ = new Mischer(new MischerEinstellungen { AbsenkungAnstiegMs = float.NaN }), "Anstieg NaN");
        Pruefe.Wirft<ArgumentException>(() => _ = new Mischer(new MischerEinstellungen { AbsenkungHalteMs = -1f }), "Haltezeit negativ");
        Pruefe.Wirft<ArgumentException>(() => _ = new Mischer(new MischerEinstellungen { DurchsageMindestpegelDb = float.NaN }), "Mindestpegel NaN");
    }

    /// <summary>Zählt die Lesevorgänge (für den Freigabe-Nachweis).</summary>
    private sealed class ZaehlQuelle : IKlangquelle
    {
        private int _gelesen;

        public int Gelesen => Volatile.Read(ref _gelesen);

        public QuellenEreignis Lesen(Span<float> ziel)
        {
            ziel.Fill(0.01f);
            Interlocked.Increment(ref _gelesen);
            return QuellenEreignis.Aktiv;
        }
    }

    [Test]
    public static void Mischer_QuelleHinzufuegen_AusVielenThreads_JederPlatzGenauEinmal()
    {
        for (var runde = 0; runde < 20; runde++)
        {
            var m = new Mischer(new MischerEinstellungen { MaxQuellen = 64 }) { Klick = null };
            var quellen = Enumerable.Range(0, 64).Select(_ => new ZaehlQuelle()).ToArray();
            var plaetze = new int[64];
            using var start = new Barrier(8);
            var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
            {
                start.SignalAndWait();
                for (var k = 0; k < 8; k++) plaetze[t * 8 + k] = m.QuelleHinzufuegen(quellen[t * 8 + k], 0);
            })).ToArray();
            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join();

            Pruefe.Falsch(plaetze.Contains(-1), "jede Quelle bekam einen Platz");
            Pruefe.Gleich(64, plaetze.Distinct().Count(), "kein Platz doppelt vergeben");
            Pruefe.Gleich(-1, m.QuelleHinzufuegen(new ZaehlQuelle(), 0), "danach voll");
            m.Mischen(new float[960], 2);
            Pruefe.Wahr(quellen.All(q => q.Gelesen == 1), "jede Quelle wird gemischt");
        }
    }

    [Test]
    public static void Mischer_EntfernteQuelle_FreigabeErstNachDerMarke()
    {
        var m = new Mischer { Klick = null };
        var q = new ZaehlQuelle();
        var platz = m.QuelleHinzufuegen(q, 0);
        var aus = new float[960];
        m.Mischen(aus, 2);
        var marke = m.QuelleEntfernen(platz);
        Pruefe.Falsch(m.QuelleFreigebbar(marke), "vor dem nächsten Block noch nicht freigebbar");
        m.Mischen(aus, 2);
        Pruefe.Wahr(m.QuelleFreigebbar(marke), "nach einem Block freigebbar");
        var gelesen = q.Gelesen;
        for (var i = 0; i < 5; i++) m.Mischen(aus, 2);
        Pruefe.Gleich(gelesen, q.Gelesen, "nach der Freigabe nicht mehr gelesen");
        m.QuelleEntfernen(platz); // doppelt entfernen schadet nicht
        Pruefe.Wahr(m.QuelleHinzufuegen(new ZaehlQuelle(), 0) >= 0, "Platz wieder frei");
    }

    [Test]
    public static void Mischer_PlatzWiederbelegt_BeginntOhneRampeVomVorgaenger()
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null };
        var laut = new TestQuelle(0.5f) { Gleichwert = true };
        var platz = m.QuelleHinzufuegen(laut, 0, funkerPegelDb: 0f);
        var aus = new float[480];
        m.Mischen(aus, 1);
        // Entfernen und sofort neu belegen, ohne dass der Audio-Thread den leeren Platz gesehen hat.
        m.QuelleEntfernen(platz);
        var leise = new TestQuelle(0.5f) { Gleichwert = true };
        Pruefe.Gleich(platz, m.QuelleHinzufuegen(leise, 0, funkerPegelDb: -20f), "gleicher Platz");
        m.Mischen(aus, 1);
        PruefeWert.Nahe(0.05, aus[0], 1e-6, "neue Quelle sofort mit eigenem Pegel, ohne Rampe vom Vorgänger");
    }

    // ---------------------------------------------------------------- Funk-Klick

    [Test]
    public static void Klick_UngueltigeEinstellungen_Abgelehnt()
    {
        // Dauer, Hülle und Band sind seit Stufe 2b fest (Rauschen nach Variante A); einstellbar bleiben die Pegel.
        Pruefe.Wirft<ArgumentException>(() => _ = new FunkKlick(new FunkKlickEinstellungen { EigeneDb = float.NaN }), "Pegel NaN");
        Pruefe.Wirft<ArgumentException>(() => _ = new FunkKlick(new FunkKlickEinstellungen { SprachaktivierungDb = float.NaN }), "Absenkung NaN");
    }

    // ---------------------------------------------------------------- Sprach-Gate

    private static float[] Rauschen(float dbfs, int seed)
    {
        var r = new Random(seed);
        var x = new float[480];
        var a = MathF.Pow(10f, dbfs / 20f) * MathF.Sqrt(3f);
        for (var i = 0; i < x.Length; i++) x[i] = a * (float)(r.NextDouble() * 2 - 1);
        return x;
    }

    private static float[] Ton(float dbfs, int nr)
    {
        var x = Rauschen(-65f, 77);
        var a = MathF.Pow(10f, dbfs / 20f) * MathF.Sqrt(2f);
        for (var i = 0; i < x.Length; i++) x[i] += a * MathF.Sin(2f * MathF.PI * 150f * (nr * 480 + i) / 48000f);
        return x;
    }

    private static SprachGate Eingeschwungen()
    {
        var g = new SprachGate();
        for (var i = 0; i < 150; i++) g.Verarbeiten(Rauschen(-65f, 100 + i), 0f);
        return g;
    }

    [Test]
    public static void Gate_NichtEndlicherRahmen_VergiftetRauschbodenNicht()
    {
        var g = Eingeschwungen();
        var nan = Rauschen(-65f, 1);
        nan[10] = float.NaN;
        var inf = Rauschen(-65f, 2);
        inf[20] = float.PositiveInfinity;
        Pruefe.Gleich(0, g.Verarbeiten(nan, float.NaN), "NaN-Rahmen öffnet nicht");
        g.Verarbeiten(inf, 0f);
        g.Verarbeiten(Rauschen(-65f, 3), 0f, agcVerstaerkungDb: float.NaN);
        g.Verarbeiten(Rauschen(-65f, 4), 0f, agcVerstaerkungDb: float.NegativeInfinity);
        for (var i = 0; i < 300; i++) g.Verarbeiten(Rauschen(-65f, 500 + i), 0f);
        Pruefe.Wahr(float.IsFinite(g.RauschbodenDbfs), "Rauschboden endlich");
        PruefeWert.Nahe(-65, g.RauschbodenDbfs, 1.5, "Rauschboden wieder am Grundrauschen");
        Pruefe.Wahr(float.IsFinite(g.AbstandAutoDb) && float.IsFinite(g.SchwelleAuto), "Selbstjustierung endlich");
        for (var i = 0; i < 4; i++) g.Verarbeiten(Ton(-20f, i), 0.95f);
        Pruefe.Gleich(GateZustand.Senden, g.Zustand, "Sprachaktivierung funktioniert weiter");
    }

    [Test]
    public static void Gate_EmpfindlichkeitNaN_WirdNull()
    {
        var g = Eingeschwungen();
        g.Empfindlichkeit = float.NaN;
        Pruefe.Gleich(0f, g.Empfindlichkeit, "NaN wird 0");
        for (var i = 0; i < 4; i++) g.Verarbeiten(Ton(-20f, i), 0.95f);
        Pruefe.Gleich(GateZustand.Senden, g.Zustand, "öffnet weiter");
    }

    private static List<(SenderahmenArt Art, long Nummer)> Holen(SprachGate g)
    {
        var liste = new List<(SenderahmenArt, long)>();
        var puffer = new float[480];
        while (g.Holen(puffer, out var art, out var nr)) liste.Add((art, nr));
        return liste;
    }

    [Test]
    public static void Gate_NeuerAnlaufDirektNachDemEnde_SendetKeinenRahmenDoppelt()
    {
        var g = Eingeschwungen();
        var alle = new List<(SenderahmenArt Art, long Nummer)>();
        var nr = 0;
        for (var i = 0; i < 20; i++)
        {
            g.Verarbeiten(Ton(-20f, nr++), 0.95f);
            alle.AddRange(Holen(g));
        }
        // Nachlauf läuft ganz aus (30 Rahmen), der letzte ist ausgeblendet.
        for (var i = 0; i < 30; i++)
        {
            g.Verarbeiten(Rauschen(-65f, 900 + i), 0f);
            alle.AddRange(Holen(g));
        }
        Pruefe.Gleich(GateZustand.Leise, g.Zustand, "Phase beendet");
        Pruefe.Wahr((alle[^1].Art & SenderahmenArt.Letzter) != 0, "letzter Rahmen markiert");
        var letzterDerErsten = alle[^1].Nummer;

        // Sofort wieder Sprache: der Vorlauf darf nicht in die schon gesendete Phase zurückgreifen.
        for (var i = 0; i < 6; i++)
        {
            g.Verarbeiten(Ton(-20f, nr++), 0.95f);
            alle.AddRange(Holen(g));
        }
        Pruefe.Gleich(GateZustand.Senden, g.Zustand, "zweite Phase offen");
        Pruefe.Gleich(alle.Count, alle.Select(a => a.Nummer).Distinct().Count(), "kein Rahmen doppelt gesendet");
        var beginn = alle.FindLastIndex(a => (a.Art & SenderahmenArt.Beginn) != 0);
        Pruefe.Wahr(alle[beginn].Nummer > letzterDerErsten, "zweite Phase beginnt nach dem letzten Rahmen der ersten");
    }

    [Test]
    public static void Gate_TasteLosgelassenDannSprache_SendetKeinenRahmenDoppelt()
    {
        var g = Eingeschwungen();
        var alle = new List<(SenderahmenArt Art, long Nummer)>();
        for (var i = 0; i < 10; i++)
        {
            g.Verarbeiten(Ton(-20f, i), 0.95f, sprechtaste: true);
            alle.AddRange(Holen(g));
        }
        for (var i = 10; i < 16; i++)
        {
            g.Verarbeiten(Ton(-20f, i), 0.95f);
            alle.AddRange(Holen(g));
        }
        Pruefe.Gleich(GateZustand.Senden, g.Zustand, "Sprachaktivierung übernimmt");
        Pruefe.Gleich(alle.Count, alle.Select(a => a.Nummer).Distinct().Count(), "kein Rahmen doppelt gesendet");
    }

    [Test]
    public static void Gate_SprachaktivierungAbgeschaltetWaehrendSenden_SchliesstPhaseMitEnde()
    {
        var kod = new AttrappenKodierer();
        var gate = new SprachGate();
        var s = new Sendeaufbereitung(gate, kod);
        for (var i = 0; i < 150; i++) s.Verarbeiten(Rauschen(-65f, 100 + i), 0f);
        var offen = 0;
        for (var i = 0; i < 21; i++) offen += s.Verarbeiten(Ton(-20f, i), 0.95f);
        Pruefe.Wahr(offen > 0, "Phase offen");
        gate.SprachaktivierungAn = false;
        var n = s.Verarbeiten(Ton(-20f, 21), 0.95f);
        var ende = Enumerable.Range(0, n).Any(i => (s.Kennzeichen(i) & Sprachkennzeichen.Ende) != 0);
        Pruefe.Wahr(ende, "ENDE sofort beim Abschalten");
        Pruefe.Gleich(GateZustand.Leise, gate.Zustand, "Gate leise");
        Pruefe.Gleich(0, s.Verarbeiten(Ton(-20f, 22), 0.95f), "danach nichts mehr");
        // Beim nächsten Tastendruck kommt kein Rest der alten Phase mit.
        n = s.Verarbeiten(Ton(-20f, 23), 0f, sprechtaste: true);
        Pruefe.Gleich(0, n, "halbes Paket wartet auf den zweiten Rahmen");
        n = s.Verarbeiten(Ton(-20f, 24), 0f, sprechtaste: true);
        Pruefe.Gleich(1, n, "ein Paket");
        Pruefe.Gleich(Sprachkennzeichen.Beginn, s.Kennzeichen(0), "neue Tastenphase ohne Altlast");
    }

    // ---------------------------------------------------------------- Denormale

    [Test]
    public static void Denormale_NachStille_FilterzustaendeUndHuelleExaktNull()
    {
        // Nachweis des Mechanismus: Der Tiefpassschritt rundet bei kleinen Denormalen auf 0 und bleibt stehen.
        var t = 1f;
        for (var i = 0; i < 100_000; i++) t += 0.1227f * (0f - t);
        Pruefe.Wahr(float.IsSubnormal(t), "ohne Rückstellung bliebe ein Tiefpass auf einem Denormal stehen");

        var s = new Stimmhaftigkeit();
        for (var i = 0; i < 10; i++) s.Schaetzen(Ton(-10f, i));
        var stille = new float[480];
        for (var i = 0; i < 100; i++) s.Schaetzen(stille);
        Pruefe.Gleich(0f, s.Filterzustand, "Tiefpässe der Stimmhaftigkeit nach 1 s digitaler Stille exakt 0");

        var b = new Begrenzer();
        var laut = new float[480];
        Array.Fill(laut, 0.9f);
        b.Verarbeiten(laut, Span<float>.Empty);
        for (var i = 0; i < 2000; i++)
        {
            Array.Clear(stille);
            b.Verarbeiten(stille, Span<float>.Empty);
        }
        Pruefe.Gleich(0f, b.Huellpegel, "Hüllkurve des Begrenzers nach 20 s Stille exakt 0");
    }

    // ---------------------------------------------------------------- Übergabering Netz-Thread zu Audio-Thread

    [Test]
    public static void PaketRing_EinSchreiberEinLeser_ReihenfolgeUndInhaltUnverfaelscht()
    {
        const int Anzahl = 200_000;
        var ring = new PaketRing(64, 400);
        var fehler = 0;
        var leser = new Thread(() =>
        {
            var erwartet = 0;
            while (erwartet < Anzahl)
            {
                if (!ring.Spaehen(out var i))
                {
                    Thread.SpinWait(8);
                    continue;
                }
                var d = ring.Daten(i);
                if (ring.Frame(i) != (uint)erwartet || d.Length != 1 + erwartet % 400 || ring.AnkunftUs(i) != erwartet * 3L
                    || ring.Phase(i) != (ushort)erwartet || ring.Kennzeichen(i) != (Sprachkennzeichen)(erwartet & 0x0F))
                {
                    fehler++;
                }
                else
                {
                    for (var k = 0; k < d.Length; k++)
                    {
                        if (d[k] == (byte)(erwartet + k)) continue;
                        fehler++;
                        break;
                    }
                }
                ring.Freigeben();
                erwartet++;
            }
        });
        leser.Start();
        var puffer = new byte[400];
        for (var n = 0; n < Anzahl; n++)
        {
            var laenge = 1 + n % 400;
            for (var k = 0; k < laenge; k++) puffer[k] = (byte)(n + k);
            while (!ring.Schreiben(n * 3L, (ushort)n, (uint)n, (Sprachkennzeichen)(n & 0x0F), puffer.AsSpan(0, laenge))) Thread.SpinWait(8);
        }
        leser.Join();
        Pruefe.Gleich(0, fehler, "jedes Paket unverfälscht und in Reihenfolge");
        Pruefe.Falsch(ring.Schreiben(0, 0, 0, Sprachkennzeichen.Keine, new byte[401]), "zu großes Paket abgelehnt");
        Pruefe.Falsch(ring.Spaehen(out _), "Ring leer");
    }

    [Test]
    public static void PaketRing_Voll_LehntAb_UndLeerenGibtFrei()
    {
        var ring = new PaketRing(4, 8);
        for (var i = 0; i < 4; i++) Pruefe.Wahr(ring.Schreiben(i, 1, (uint)i, Sprachkennzeichen.Keine, [1, 2]), "Platz frei");
        Pruefe.Falsch(ring.Schreiben(9, 1, 9, Sprachkennzeichen.Keine, [1]), "voll");
        ring.Leeren();
        Pruefe.Falsch(ring.Spaehen(out _), "geleert");
        Pruefe.Wahr(ring.Schreiben(10, 1, 10, Sprachkennzeichen.Keine, []), "wieder frei, auch leeres Paket");
        Pruefe.Wahr(ring.Spaehen(out var j) && ring.Daten(j).IsEmpty && ring.Frame(j) == 10, "leeres Paket kommt an");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => _ = new PaketRing(3, 8), "keine Zweierpotenz");
    }

    [Test]
    public static void PaketRing_ZaehlerUeberlauf_NachZweiHoch31Paketen()
    {
        // Bei 50 Paketen je Sekunde läuft der int-Zähler nach etwa 500 Tagen über; der Ring rechnet modular.
        var ring = new PaketRing(8, 4);
        var art = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(PaketRing).GetField("_schreib", art)!.SetValue(ring, int.MaxValue - 5);
        typeof(PaketRing).GetField("_lese", art)!.SetValue(ring, int.MaxValue - 5);
        for (var runde = 0; runde < 4; runde++)
        {
            for (var i = 0; i < 8; i++) Pruefe.Wahr(ring.Schreiben(i, 0, (uint)(runde * 8 + i), Sprachkennzeichen.Keine, [(byte)i]), "Platz frei");
            Pruefe.Falsch(ring.Schreiben(0, 0, 0, Sprachkennzeichen.Keine, [0]), "voll erkannt");
            for (var i = 0; i < 8; i++)
            {
                Pruefe.Wahr(ring.Spaehen(out var j), "Paket da");
                Pruefe.Gleich((uint)(runde * 8 + i), ring.Frame(j), "Reihenfolge über den Überlauf");
                ring.Freigeben();
            }
            Pruefe.Falsch(ring.Spaehen(out _), "leer erkannt");
        }
    }

    // ---------------------------------------------------------------- Stimmhaftigkeit, Zeitdehnung

    [Test]
    public static void Zeitdehnung_KuerzenUndDehnen_UmEinePeriode_Stetig()
    {
        // Periodisch mit 384 Samples (125 Hz und 250 Hz).
        var ein = new float[960];
        for (var i = 0; i < ein.Length; i++)
            ein[i] = 0.3f * MathF.Sin(2f * MathF.PI * 125f * i / 48000f) + 0.1f * MathF.Sin(2f * MathF.PI * 250f * i / 48000f + 0.3f);
        var eingangsSchritt = 0f;
        for (var i = 1; i < ein.Length; i++) eingangsSchritt = MathF.Max(eingangsSchritt, MathF.Abs(ein[i] - ein[i - 1]));
        var aus = new float[960 + Zeitdehnung.MaxVersatz];

        void Stetig(int n, string was)
        {
            Pruefe.Gleich(ein[0], aus[0], was + ": Anfang schließt an");
            PruefeWert.Nahe(ein[^1], aus[n - 1], 1e-6, was + ": Ende schließt an");
            for (var i = 1; i < n; i++) PruefeWert.Hoechstens(eingangsSchritt * 1.01 + 1e-6, MathF.Abs(aus[i] - aus[i - 1]), was + ": kein Sprung");
        }

        Pruefe.Gleich(960 - 384, Zeitdehnung.Kuerzen(ein, aus, leise: false), "um eine Periode gekürzt");
        Stetig(960 - 384, "Kürzen");
        Pruefe.Gleich(960 + 384, Zeitdehnung.Dehnen(ein, aus, leise: false), "um eine Periode gedehnt");
        Stetig(960 + 384, "Dehnen");
        Pruefe.Gleich(480, Zeitdehnung.Kuerzen(ein, aus, leise: true), "leise bis zur Hälfte");

        var rauschen = new float[960];
        var r = new Random(3);
        for (var i = 0; i < rauschen.Length; i++) rauschen[i] = 0.3f * (float)(r.NextDouble() * 2 - 1);
        Pruefe.Gleich(960, Zeitdehnung.Kuerzen(rauschen, aus, leise: false), "Rauschen wird nicht gekürzt");
        var mitNaN = (float[])ein.Clone();
        mitNaN[100] = float.NaN;
        Pruefe.Gleich(960, Zeitdehnung.Dehnen(mitNaN, aus, leise: false), "NaN verhindert das Dehnen, ohne zu werfen");
    }

    [Test]
    public static void Stimmhaftigkeit_NichtEndlich_UndExtrem_Endlich()
    {
        var s = new Stimmhaftigkeit();
        var x = Ton(-20f, 0);
        x[5] = float.NaN;
        x[6] = float.PositiveInfinity;
        var p = s.Schaetzen(x);
        Pruefe.Wahr(float.IsFinite(p) && p is >= 0f and <= 1f, "Wahrscheinlichkeit in [0, 1]");
        var laut = new float[480];
        Array.Fill(laut, 1e30f);
        p = s.Schaetzen(laut);
        Pruefe.Wahr(float.IsFinite(p) && p is >= 0f and <= 1f, "auch bei 1e30");
        for (var i = 0; i < 20; i++) p = s.Schaetzen(Ton(-30f, i));
        PruefeWert.Mindestens(0.9, p, "erholt sich danach");
    }

    // ---------------------------------------------------------------- Jitterpuffer an der Dekodierer-Naht

    /// <summary>
    /// Dekodierer, der den Vertrag verletzen oder scheitern kann: zu große Längen, negative Längen, Stille.
    /// Normal liefert er denselben Klang wie <see cref="AttrappenDekodierer"/>.
    /// </summary>
    private sealed class StoerDekodierer : IDekodierer
    {
        public HashSet<uint> Scheitert { get; } = [];
        public int ZuLang { get; set; }
        public bool VerdeckenScheitert { get; set; }
        public float FecGleichwert { get; set; } = float.NaN;

        /// <summary>Wenn gesetzt: Dekodieren, FEC und Verdecken liefern diesen Gleichwert (Hüllkurve direkt sichtbar).</summary>
        public float Gleichwert { get; set; } = float.NaN;

        public int Aufrufe { get; private set; }

        public int Abtastrate => 48000;

        public int Dekodieren(ReadOnlySpan<byte> paket, Span<float> ziel)
        {
            Aufrufe++;
            var frame = BinaryPrimitives.ReadUInt32LittleEndian(paket);
            if (Scheitert.Contains(frame)) return -4;
            var n = Math.Min(960, ziel.Length);
            for (var i = 0; i < n; i++) ziel[i] = float.IsNaN(Gleichwert) ? AttrappenDekodierer.Wert(frame * 960L + i) : Gleichwert;
            return ZuLang > 0 ? ZuLang : 960;
        }

        public int DekodierenFec(ReadOnlySpan<byte> folgepaket, Span<float> ziel, int fehlendeSamples)
        {
            Aufrufe++;
            var start = BinaryPrimitives.ReadUInt32LittleEndian(folgepaket) * 960L - fehlendeSamples;
            for (var i = 0; i < fehlendeSamples; i++)
                ziel[i] = !float.IsNaN(FecGleichwert) ? FecGleichwert : !float.IsNaN(Gleichwert) ? Gleichwert : AttrappenDekodierer.Wert(start + i);
            return ZuLang > 0 ? ZuLang : fehlendeSamples;
        }

        public int Verdecken(Span<float> ziel, int fehlendeSamples)
        {
            Aufrufe++;
            if (VerdeckenScheitert) return 0;
            ziel[..fehlendeSamples].Fill(float.IsNaN(Gleichwert) ? 0f : Gleichwert);
            return ZuLang > 0 ? ZuLang : fehlendeSamples;
        }

        public void Dispose()
        {
        }
    }

    [Test]
    public static void Jitterpuffer_DekodiererMitZuGrosserLaenge_WirftNicht()
    {
        var dek = new StoerDekodierer { ZuLang = 100_000 };
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var netz = Netzsimulation.Verlust(Netzsimulation.Konstant(30_000), f => f % 7 == 3);
        var e = Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 100, netz), 3 * Sekunde);
        Pruefe.Falsch(e.NichtEndlich, "endlich");
        // 86 Pakete dekodiert (14 verloren), dazu 3 Verdeckungen bis zur Höchstdauer; danach Stille ohne Aufruf.
        PruefeWert.Mindestens(86, dek.Aufrufe, "Dekodierer wurde benutzt");
        Pruefe.Gleich(1L, jp.Statistik.Phasen, "Phase normal zu Ende");
    }

    [Test]
    public static void Jitterpuffer_DekodiererScheitert_StilleDannFecEingeblendet()
    {
        // Frame 50: Dekodieren und Verdecken scheitern (Stille). Frame 51 fehlt, FEC aus 52 liefert einen Gleichwert.
        var dek = new StoerDekodierer { VerdeckenScheitert = true, FecGleichwert = 0.25f };
        dek.Scheitert.Add(50);
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var netz = Netzsimulation.Verlust(Netzsimulation.Konstant(30_000), f => f == 51);
        var e = Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 100, netz), 3 * Sekunde, ausgabeMitschreiben: true);
        var a = e.Ausgabe;

        // Konstantes Netz: nichts wird gekürzt oder gedehnt, jeder Frame füllt zwei 10-ms-Blöcke.
        Pruefe.Gleich(0L, jp.Statistik.GekuerztSamples + jp.Statistik.GedehntSamples, "keine Zeitdehnung");
        var stille = -1;
        for (var b = 0; b + 1 < a.Count / 480 && stille < 0; b++)
        {
            if (Enumerable.Range(b * 480, 960).All(i => a[i] == 0f) && a.Take(b * 480).Any(v => v != 0f)) stille = b;
        }
        Pruefe.Wahr(stille > 0, "stiller Frame gefunden");
        var fec = (stille + 2) * 480;
        PruefeWert.Hoechstens(0.01, MathF.Abs(a[fec]), "FEC nach Stille beginnt eingeblendet");
        PruefeWert.Nahe(0.25, a[fec + 480], 1e-6, "danach voller Pegel");
        var normal = (stille + 4) * 480;
        PruefeWert.Nahe(AttrappenDekodierer.Wert(52 * 960L + 60), a[normal + 60], 1e-6, "Frame 52 danach nicht noch einmal eingeblendet");
    }

    [Test]
    public static void Jitterpuffer_NachTeilweiseAusgeblendeterVerdeckung_StetigerUebergang()
    {
        // Das Netz hängt ab Frame 100 verschieden lange: Was während des Hängers gesendet wird, kommt gebündelt an
        // dessen Ende an. Je nach Dauer endet die Verdeckung voll, teilweise ausgeblendet oder in Stille. Mit
        // Gleichwerten ist die Ausgabe die Hüllkurve: Sie darf nirgends springen, auch nicht, wenn der nächste
        // echte Frame mitten in der Ausblendung kommt.
        var teilweise = 0;
        for (var l = 60; l <= 160; l += 5)
        {
            var dek = new StoerDekodierer { Gleichwert = 0.3f };
            var uhr = new SimUhr();
            var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
            var hangEnde = 100 * 20_000L + 30_000 + l * 1000L;
            Func<uint, long?> netz = f => f >= 100 && f * 20_000L + 30_000 < hangEnde ? hangEnde - f * 20_000L : 30_000;
            var e = Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 200, netz), 6 * Sekunde, ausgabeMitschreiben: true);
            var a = e.Ausgabe;
            var start = a.FindIndex(v => v != 0f);
            var ende = a.FindLastIndex(v => v != 0f);
            var schritt = 0f;
            var tiefster = 1f;
            for (var i = start + 1; i <= ende; i++)
            {
                schritt = MathF.Max(schritt, MathF.Abs(a[i] - a[i - 1]));
                if (a[i] != 0f && i > start + 200 && i < ende - 300) tiefster = MathF.Min(tiefster, a[i]);
            }
            PruefeWert.Hoechstens(0.01, schritt, $"größter Sprung bei {l} ms Hänger");
            if (tiefster is > 0.01f and < 0.29f) teilweise++;
        }
        PruefeWert.Mindestens(1, teilweise, "mindestens ein Fall mit teilweiser Ausblendung");
    }

    [Test]
    public static void Jitterpuffer_UngueltigeEinstellungen_Abgelehnt()
    {
        var dek = new AttrappenDekodierer();
        Pruefe.Wirft<ArgumentException>(() => _ = new Jitterpuffer(dek, new JitterpufferEinstellungen { Quantil = double.NaN }), "Quantil NaN");
        Pruefe.Wirft<ArgumentException>(() => _ = new Jitterpuffer(dek, new JitterpufferEinstellungen { Vergessensfaktor = 1.0 }), "Vergessensfaktor 1");
        Pruefe.Wirft<ArgumentException>(() => _ = new Jitterpuffer(dek, new JitterpufferEinstellungen { MaxVerdeckungMs = -1 }), "Verdeckung negativ");
        Pruefe.Wirft<ArgumentException>(() => _ = new Jitterpuffer(dek, new JitterpufferEinstellungen { LeiseDbfs = float.NaN }), "Leise NaN");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => _ = new Jitterpuffer(dek, new JitterpufferEinstellungen { EingangPlaetze = 48 }), "Ring keine Zweierpotenz");
        _ = new Jitterpuffer(dek, new JitterpufferEinstellungen { AuffrischIntervallMs = 0, FecErwartet = false });
    }

    [Test]
    public static void Jitterpuffer_LeeresPaketOhneEnde_BeginntKeinePhase()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        Pruefe.Wahr(jp.Einreihen(10_000, 7, 0, Sprachkennzeichen.Beginn, []), "eingereiht");
        var block = new float[480];
        var aktiv = 0;
        for (var t = 13_000L; t < 2 * Sekunde; t += 10_000)
        {
            uhr.Us = t;
            if ((jp.Lesen(block) & QuellenEreignis.Aktiv) != 0) aktiv++;
        }
        Pruefe.Gleich(0, aktiv, "nie aktiv (sonst bliebe etwa eine Durchsage-Absenkung stehen)");
        Pruefe.Gleich(0L, jp.Statistik.Phasen, "keine Phase");

        // Danach beginnt dieselbe Phase normal mit echtem Paket.
        var e = Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(7, 150, 20, Netzsimulation.Konstant(30_000)), 5 * Sekunde, startUs: 2 * Sekunde + 3_000);
        Pruefe.Gleich(20, dek.Normal, "Phase gespielt");
        Pruefe.Gleich(1, e.Ereignisse.Count(x => (x.Ereignis & QuellenEreignis.KlickDruck) != 0), "mit Beginn-Klick");
    }

    [Test]
    public static void Jitterpuffer_Zuruecksetzen_MittenInDerPhase_UndDanachWeiter()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var pakete = Netzsimulation.Phase(3, 0, 100, Netzsimulation.Konstant(30_000));
        Netzsimulation.Abspielen(jp, uhr, pakete.Where(p => p.Frame < 40), 1 * Sekunde);
        jp.Zuruecksetzen();
        var e = Netzsimulation.Abspielen(jp, uhr, pakete.Where(p => p.Frame >= 40), 4 * Sekunde, startUs: 1 * Sekunde + 3_000);
        Pruefe.Falsch(e.NichtEndlich, "endlich");
        PruefeWert.Mindestens(95, dek.Normal, "nach dem Zurücksetzen geht es mit derselben Phase weiter");
    }

    [Test]
    public static void Jitterpuffer_Echtzeit_Sonderpfade_AllokierenNicht()
    {
        // Burst weit über dem Maximum (Notbremse), Frame-Sprung, Zurücksetzen, Statistik lesen, Verlust.
        var dek = new StoerDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var pakete = new List<SimPaket>();
        for (var runde = 0; runde < 3; runde++)
        {
            var basis = (uint)(runde * 400);
            var phase = (ushort)(20 + runde);
            // 30 Frames kommen gleichzeitig (600 ms Stau), danach Echtzeit mit Verlusten; ohne ENDE, weil die
            // Phase nach einem Frame-Sprung über 3000 weitergeht (neue Zeitlinie derselben Phase).
            pakete.AddRange(Netzsimulation.Phase(phase, basis, 150,
                f => f < basis + 30 ? (long)(basis + 30 - f) * 20_000 + 30_000 : f % 11 == 0 ? (long?)null : 30_000, mitEnde: false));
            pakete.AddRange(Netzsimulation.Phase(phase, basis + 3150, 20, f => 30_000 - 3000L * 20_000, mitBeginn: false));
        }
        var sortiert = pakete.OrderBy(p => p.AnkunftUs).ToArray();
        var block = new float[480];
        var index = 0;
        var t = 3_000L;
        long summe = 0;
        void Schritt()
        {
            while (index < sortiert.Length && sortiert[index].AnkunftUs <= t)
            {
                var p = sortiert[index++];
                jp.Einreihen(p.AnkunftUs, p.Phase, p.Frame, p.Kennzeichen, p.Daten);
            }
            uhr.Us = t;
            jp.Lesen(block);
            var s = jp.Statistik;
            summe += s.Ueberlauf + s.Plc + (long)s.ZielMs;
            if (t % 3_000_000 == 3_000) jp.Zuruecksetzen();
            t += 10_000;
        }
        for (var i = 0; i < 100; i++) Schritt();
        var bytes = PruefeWert.Allokiert(() =>
        {
            for (var i = 0; i < 2300; i++) Schritt();
        });
        Pruefe.Gleich(0L, bytes, "allokierte Bytes in Einreihen/Lesen/Statistik/Zuruecksetzen");
        PruefeWert.Mindestens(1, jp.Statistik.Ueberlauf, "Notbremse wurde durchlaufen");
        Pruefe.Wahr(summe > 0, "Statistik gelesen");
    }
}
