// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Security.Cryptography;
using Funk.Klang.Geraete;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Die Klangmaschine Ende zu Ende mit Geräte-Attrappen und echter Kette (APM, RNNoise, Opus), simulierter Uhr und ohne
/// eigene Threads: Kette deterministisch, Sprechtaste gegen Gate, Hörprobe mit Latenz, Pegelmeldungen, Echo, Geräte.
/// Keine echten Audiogeräte, keine fremden Klänge.
/// </summary>
internal static class KlangMaschineTests
{
    /// <summary>Szene: synthetische Sprache mit Tastaturanschlägen darunter (48 kHz mono) und die Äußerungen.</summary>
    private static (float[] Signal, List<Aeusserung> Marken) SpracheMitTastatur(double sekunden, int seed)
    {
        var (sprache, marken) = Testszenen.Sprache(sekunden, seed);
        return (Testszenen.Summe(sprache, Testszenen.Tastatur(sekunden, seed + 1)), marken);
    }

    /// <summary>Phasen aus den gesendeten Paketen: Takt des Beginns (abzüglich Vorlauf) und des Endes.</summary>
    private static List<(long Beginn, long Ende, bool Va, int Pakete)> Phasen(List<Gesendet> pakete)
    {
        var phasen = new List<(long, long, bool, int)>();
        long beginn = -1;
        var va = false;
        var anzahl = 0;
        foreach (var p in pakete)
        {
            if ((p.Kennzeichen & Sprachkennzeichen.Beginn) != 0)
            {
                // Der Vorlauf (je Paket 20 ms) liegt vor dem Takt, in dem das Gate öffnete.
                var vorlauf = pakete.Count(q => q.Takt == p.Takt && (q.Kennzeichen & Sprachkennzeichen.Preroll) != 0);
                beginn = p.Takt - 2 * vorlauf;
                va = (p.Kennzeichen & Sprachkennzeichen.Sprachaktivierung) != 0;
                anzahl = 0;
            }
            if (p.Opus.Length > 0) anzahl++;
            if ((p.Kennzeichen & Sprachkennzeichen.Ende) != 0 && beginn >= 0)
            {
                phasen.Add((beginn, p.Takt, va, anzahl));
                beginn = -1;
            }
        }
        return phasen;
    }

    [Test]
    public static async Task Vertrag_Voreinstellungen_UndGeraete()
    {
        await using var s = new Pruefstand();
        var m = AlsVertrag(s.Maschine);
        Pruefe.Gleich(GateVorgaben.Standard, m.Gate, "Voreinstellung des Gates");
        Pruefe.Wahr(m.Sprachaktivierung, "Sprachaktivierung an");
        Pruefe.Gleich(Rauschfilter.Standard, m.Rauschfilter, "Rauschfilter ab Werk Standard (RNNoise)");
        Pruefe.Wahr(m.Echounterdrueckung, "Echounterdrückung an (A14)");
        Pruefe.Falsch(m.Hoerprobe, "Hörprobe aus");
        Pruefe.Gleich(2, m.Eingaenge().Count, "Eingänge der Attrappe");
        Pruefe.Wahr(m.Ausgaenge().Single(g => g.IstStandard).Id == "spk-1", "Standardausgang");

        m.Gate = new GateEinstellung(GateModus.Lernend, float.NaN, 3f, -30f, -60f, 1f, 5f, 0.7f);
        Pruefe.Gleich(-60f, m.Gate.KorridorVonDb, "Gate wird beim Setzen bereinigt");
        Pruefe.Gleich(GateVorgaben.Standard.SchwelleDb, m.Gate.SchwelleDb, "NaN ersetzt");

        await m.StartenAsync(null, null, CancellationToken.None);
        Pruefe.Gleich("mic-1", s.System.Aufnahme!.GeraetId, "Standardmikrofon");
        Pruefe.Gleich("spk-1", s.System.Wiedergabe!.GeraetId, "Standardausgang");
        Pruefe.Gleich("spk-1", s.System.Loopback!.GeraetId, "Echoreferenz vom Ausgang");
        await m.StoppenAsync();
        Pruefe.Wahr(s.System.Aufnahme.Freigegeben && s.System.Wiedergabe.Freigegeben && s.System.Loopback.Freigegeben, "Stoppen schließt alle Ströme");
    }

    [Test]
    public static async Task Kette_EndeZuEnde_SpracheUndTastatur_Deterministisch()
    {
        var (signal, marken) = SpracheMitTastatur(40, 41);
        var (pakete1, statistik) = await KetteLaufen(signal);
        var (pakete2, _) = await KetteLaufen(signal);

        var phasen = Phasen(pakete1);
        Console.WriteLine($"  Kette: {phasen.Count} Phasen, {pakete1.Count(p => p.Opus.Length > 0)} Audiopakete für {marken.Count} Äußerungen, " +
            $"{statistik.Kodierfehler} Kodierfehler, {statistik.SendeVerworfen} verworfen");

        // Jede Äußerung liegt in einer Phase der Sprachaktivierung, keine Phase ohne Äußerung (Tastatur allein).
        var ohneSprache = phasen.Count(ph => !marken.Any(a => ph.Beginn * 480 < a.Ende + 24000 && (ph.Ende + 1) * 480 > a.Start));
        var verpasst = marken.Count(a => !phasen.Any(ph => ph.Beginn * 480 <= a.Start + 4800 && (ph.Ende + 1) * 480 >= a.Start));
        Pruefe.Gleich(0, ohneSprache, "Phasen nur aus Tastaturgeräusch");
        Pruefe.Gleich(0, verpasst, "Äußerungen ohne Phase");
        Pruefe.Wahr(phasen.All(ph => ph.Va), "alle Phasen aus der Sprachaktivierung");
        Pruefe.Gleich(0L, statistik.Kodierfehler + statistik.SendeVerworfen, "keine Kodierfehler, nichts verworfen");

        // Echte Opus-Pakete: je 20 ms, höchstens 400 Byte, dekodierbar.
        using var dek = new OpusDekodierer();
        var pcm = new float[5760];
        foreach (var p in pakete1.Where(p => p.Opus.Length > 0))
        {
            PruefeWert.Hoechstens(400, p.Opus.Length, "Paketlänge nach 5.6");
            Pruefe.Gleich(960, dek.Dekodieren(p.Opus, pcm), "ein Paket sind 20 ms");
        }

        // Zweimal dieselbe Szene: bitgleiche Pakete und Kennzeichen.
        Pruefe.Gleich(Hash(pakete1), Hash(pakete2), "deterministisch (SHA-256 aller Pakete)");
    }

    /// <summary>Nur über den gemeinsamen Vertrag, wie die Oberfläche die Maschine sieht (deshalb bewusst die Schnittstelle).</summary>
#pragma warning disable CA1859
    private static IKlangMaschine AlsVertrag(KlangMaschine maschine) => maschine;
#pragma warning restore CA1859

    private static async Task<(List<Gesendet> Pakete, KlangStatistik Statistik)> KetteLaufen(float[] signal)
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Abspielen(signal);
        return (s.Pakete, s.Maschine.Statistik);
    }

    private static string Hash(List<Gesendet> pakete)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var p in pakete)
        {
            sha.AppendData(BitConverter.GetBytes(p.Takt));
            sha.AppendData([(byte)p.Kennzeichen]);
            sha.AppendData(p.Opus);
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    [Test]
    public static async Task Sprechtaste_UebersteuertGate()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        // Linie ganz oben: Die Sprachaktivierung öffnet nie.
        s.Maschine.Gate = GateVorgaben.Standard with { Modus = GateModus.Fest, SchwelleDb = 0f };
        var (signal, _) = SpracheMitTastatur(12, 51);
        s.Abspielen(signal.AsSpan(0, 48000 * 12), takt =>
        {
            if (takt == 300) s.Maschine.SprechtasteGedrueckt();
            if (takt == 301) s.Maschine.SprechtasteGedrueckt(); // Wiederholung beim Halten: kein zweiter Beginn
            if (takt == 700) s.Maschine.SprechtasteLosgelassen();
        });
        var phasen = Phasen(s.Pakete);
        Pruefe.Gleich(1, phasen.Count, "genau eine Phase, nur durch die Taste");
        var (beginn, ende, va, anzahl) = phasen[0];
        Pruefe.Falsch(va, "Phase der Sprechtaste trägt kein VA");
        Pruefe.Wahr(beginn is >= 300 and <= 302, "beginnt mit dem Drücken (ohne Vorlauf)");
        Pruefe.Wahr(ende is >= 700 and <= 703, "endet mit dem Loslassen");
        PruefeWert.Nahe(200, anzahl, 2, "4 s Sprechtaste = 200 Pakete zu 20 ms");
        Pruefe.Falsch(s.Pakete.Any(p => (p.Kennzeichen & Sprachkennzeichen.Preroll) != 0), "kein Vorlauf bei der Taste");
    }

    [Test]
    public static async Task Sprechtaste_BeendetLaufendePhaseDerSprachaktivierung()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Gate = GateVorgaben.Standard with { Modus = GateModus.Fest, SchwelleDb = -60f, Sprachsicherheit = 0f };
        s.Stille(100);
        var (sprache, _) = Testszenen.Sprache(20, 52);
        var laut = sprache.Select(v => v * 4f).ToArray(); // dauernd weit über -60 dBFS
        var mitten = -1L;
        s.Abspielen(laut.AsSpan(0, 48000 * 6), takt =>
        {
            if (mitten < 0 && s.Pakete.Count > 20)
            {
                mitten = takt;
                s.Maschine.SprechtasteGedrueckt();
            }
        });
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(60);
        var ende = s.Pakete.FindIndex(p => (p.Kennzeichen & Sprachkennzeichen.Ende) != 0);
        Pruefe.Wahr(ende > 0, "die VA-Phase wurde geschlossen");
        Pruefe.Gleich(Sprachkennzeichen.Sprachaktivierung | Sprachkennzeichen.Ende, s.Pakete[ende].Kennzeichen, "ENDE der VA-Phase");
        Pruefe.Gleich(Sprachkennzeichen.Beginn, s.Pakete[ende + 1].Kennzeichen, "sofort danach die Tastenphase ohne VA");
        Pruefe.Wahr(s.Pakete[ende + 1].Takt <= mitten + 2, "ENDE und neuer Beginn im Takt der Taste bzw. im nächsten");
    }

    [Test]
    public static async Task Sprechtaste_LokalerKlick_UndOhneSprachaktivierungNurTaste()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Sprachaktivierung = false; // nur Sprechtaste
        s.Stille(50);
        s.Maschine.SprechtasteGedrueckt();
        s.Stille(1);
        var klick = Pegel.Spitze(s.LetzteWiedergabe);
        s.Stille(20);
        var danach = Pegel.Spitze(s.LetzteWiedergabe);
        s.Maschine.SprechtasteLosgelassen();
        var (sprache, _) = Testszenen.Sprache(8, 53);
        s.Abspielen(sprache);
        Console.WriteLine($"  Klick beim Drücken: Spitze {Pegel.LinearZuDb(klick):F1} dBFS");
        PruefeWert.Mindestens(0.01, klick, "Funk-Klick der eigenen Taste hörbar (A11)");
        PruefeWert.Hoechstens(Pegel.DbZuLinear(-10f), klick, "und dezent: Spitze unter -10 dBFS (A63, seit 0.5.0 lauter als die -24 dBFS bis 0.4.1)");
        Pruefe.Gleich(0f, danach, "danach Ruhe");
        Pruefe.Gleich(1, Phasen(s.Pakete).Count, "ohne Sprachaktivierung sendet nur die Taste");
    }

    [Test]
    public static async Task Hoerprobe_MundZuOhr_Latenz()
    {
        foreach (var rauschunterdrueckung in new[] { true, false })
        {
            await using var s = new Pruefstand { Mitschreiben = true };
            await s.StartenAsync();
            s.Maschine.Rauschfilter = rauschunterdrueckung ? Rauschfilter.Standard : Rauschfilter.Aus;
            s.Maschine.Hoerprobe = true;
            s.Stille(100);
            s.Maschine.SprechtasteGedrueckt();
            s.Stille(30);
            // Harter Einsatz eines Vokals (ohne Anstieg) nach 30 Takten mit gedrückter Taste; Klick liegt davor.
            var vokal = Vokal(48000, 0.3f);
            var einsatz = s.Ausgabe.Count / 2;
            s.Abspielen(vokal);
            s.Maschine.SprechtasteLosgelassen();
            s.Stille(40);

            var links = s.Ausgabe.Where((_, i) => i % 2 == 0).ToArray();
            var aus = Array.FindIndex(links, einsatz, v => MathF.Abs(v) > 0.08f);
            Pruefe.Wahr(aus > 0, "die eigene Stimme kommt in der Wiedergabe an");
            var latenzMs = (aus - einsatz) / 48.0;
            var statistik = s.Maschine.Statistik.Hoerprobe;
            Console.WriteLine($"  Hörprobe (Rauschunterdrückung {(rauschunterdrueckung ? "an" : "aus")}): Mund-zu-Ohr {latenzMs:F0} ms, " +
                $"Jitterpuffer-Ziel {statistik.ZielMs:F0} ms, {statistik.Normal} Pakete dekodiert, {statistik.Plc} verdeckt");
            PruefeWert.Mindestens(40, latenzMs, "Hörprobe läuft durch Kodierer und Jitterpuffer (nicht direkt)");
            PruefeWert.Hoechstens(200, latenzMs, "Mund-zu-Ohr in der Attrappe");
            Pruefe.Gleich(0L, statistik.Plc, "keine Verdeckung ohne Verlust");
        }
    }

    [Test]
    public static async Task Hoerprobe_Aus_KeineEigeneStimme()
    {
        await using var s = new Pruefstand { Mitschreiben = true };
        await s.StartenAsync();
        s.Maschine.Sprachaktivierung = false;
        s.Stille(50);
        s.Maschine.SprechtasteGedrueckt();
        s.Stille(20);
        var start = s.Ausgabe.Count;
        s.Abspielen(Vokal(48000, 0.3f));
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(30);
        // Seit 0.5.0 (A63) ist der Klick lauter, seit Stufe 2b ein Rauschen ohne feste Spitze. Erlaubt ist genau seine Energie: über
        // 200 ms der Pegel "eigene Taste" unter der Sprache, mittig auf zwei Kanäle verteilt; die Stimme (Vokal mit 0,3) käme dazu.
        double energie = 0;
        foreach (var v in s.Ausgabe.Skip(start)) energie += (double)v * v;
        var klick = Pegel.DbZuLinear(FunkKlickEinstellungen.SprachpegelDbfs + s.Maschine.Klick.EigeneDb);
        PruefeWert.Hoechstens(klick * klick * 9600 * 1.01, energie, "ohne Hörprobe nur der Loslass-Klick in der Wiedergabe");
        Pruefe.Wahr(s.Pakete.Count > 40, "gesendet wurde trotzdem");
    }

    [Test]
    public static async Task Pegelmeldungen_50JeSekunde_MitLinieUndBoden()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Gate = GateVorgaben.Standard with { Modus = GateModus.Fest, SchwelleDb = -42f };
        var (sprache, _) = Testszenen.Sprache(4, 54);
        s.Abspielen(sprache);
        Pruefe.Gleich((int)(s.Takte / 2), s.Meldungen.Count, "eine Meldung je 20 ms");
        Pruefe.Wahr(s.Meldungen.Zip(s.Meldungen.Skip(1)).All(p => p.Second.ZeitMs - p.First.ZeitMs == 20), "Zeit im 20-ms-Raster");
        Pruefe.Wahr(s.Meldungen.All(m => m.SchwelleDb == -42f), "Linie der festen Schwelle");
        Pruefe.Wahr(s.Meldungen.Any(m => m.Sendet) && s.Meldungen.Any(m => !m.Sendet), "Sendeabschnitte markiert");
        Pruefe.Wahr(s.Meldungen.Any(m => m.Sprachwahrscheinlichkeit > 0.9f), "Sprachwahrscheinlichkeit von RNNoise");
        var boden = s.Meldungen[^1].RauschbodenDb;
        PruefeWert.Hoechstens(-50, boden, "Rauschboden unter der Sprache");
        Pruefe.Wahr(s.Meldungen.All(m => float.IsFinite(m.PegelDbfs)), "Pegel endlich");
    }

    /// <summary>Echopfad Lautsprecher zu Mikrofon je Wert (40 ms Laufzeit kommen dazu).</summary>
    internal static float Kopplung3Db(float x) => 0.7071f * x;

    internal static float Kopplung12Db(float x) => 0.25f * x;

    /// <summary>Billiger, übersteuerter Lautsprecher: kaum Dämpfung und nichtlinear, das kann AEC3 nicht linear nachbilden.</summary>
    internal static float KopplungVerzerrt(float x) => 0.8f * MathF.Tanh(4f * x);

    [Test]
    public static async Task Echounterdrueckung_OhneHeadset_KeineAusloesungDurchEcho()
    {
        // A14: Ein anderer Funker spricht, seine Stimme kommt aus den Lautsprechern und 40 ms später ins Mikrofon. Der
        // Nutzer selbst schweigt. Ohne AEC öffnet die Sprachaktivierung auf das Echo; mit AEC3 und Echoschutz nach dem
        // Einschwingen (5 s) nie, auch nicht bei starker Kopplung und verzerrendem Lautsprecher.
        foreach (var (name, pfad) in new (string, Func<float, float>)[] { ("3 dB", Kopplung3Db), ("12 dB", Kopplung12Db), ("verzerrt", KopplungVerzerrt) })
        {
            // Die Gegenprobe ohne AEC läuft ohne die Auto Dauersend-Dämpfung (Klangstufe C2a): Mit ihr erkennt der Wächter das Echo nach
            // wenigen Phasen und sperrt es (gemessen 1 statt mindestens 5), dann misste die Szene nichts mehr.
            var ohne = await EchoLauf(pfad, echoAn: false, schutz: true, nahsprecher: false,
                vorbereiten: p => p.Maschine.Daempfung = new DaempfungEinstellung(false, Daempfungsstufe.DeutlichLeiser));
            var ohneMitWaechter = await EchoLauf(pfad, echoAn: false, schutz: true, nahsprecher: false);
            var mit = await EchoLauf(pfad, echoAn: true, schutz: true, nahsprecher: false);
            Console.WriteLine($"  Echo {name}: ohne AEC {ohne.Fehlphasen} Phasen (mit Wächter {ohneMitWaechter.Fehlphasen}), mit AEC und Echoschutz {mit.Fehlphasen}");
            PruefeWert.Hoechstens(ohne.Fehlphasen, ohneMitWaechter.Fehlphasen, $"{name}: der Wächter lässt nicht mehr Echo hinaus");
            PruefeWert.Mindestens(5, ohne.Fehlphasen, $"{name}: ohne Echounterdrückung löst das Echo aus (die Szene misst etwas)");
            Pruefe.Gleich(0, mit.Fehlphasen, $"{name}: mit Echounterdrückung keine Auslösung durch das Echo");
        }
    }

    [Test]
    public static async Task Echoschutz_FaengtRestechoNachAec3()
    {
        // Der verzerrte Lautsprecher lässt hinter AEC3 einen sprachähnlichen Rest; ohne Echoschutz öffnet das Gate darauf.
        var ohneSchutz = await EchoLauf(KopplungVerzerrt, echoAn: true, schutz: false, nahsprecher: false);
        var mitSchutz = await EchoLauf(KopplungVerzerrt, echoAn: true, schutz: true, nahsprecher: false);
        Console.WriteLine($"  Restecho verzerrt: ohne Echoschutz {ohneSchutz.Fehlphasen} Phasen, mit {mitSchutz.Fehlphasen}");
        PruefeWert.Mindestens(1, ohneSchutz.Fehlphasen, "ohne Echoschutz öffnet der Rest");
        Pruefe.Gleich(0, mitSchutz.Fehlphasen, "mit Echoschutz nicht");
    }

    [Test]
    public static async Task Echounterdrueckung_Doppelsprechen_EigeneStimmeKommtDurch()
    {
        // Der Nutzer spricht selbst (-36 bis -18 dBFS), der andere Funker auch, Kopplung 12 dB. Alles, was der Nutzer sagt,
        // während die Gegenseite schweigt, muss gesendet werden. Fällt es in die Rede der Gegenseite, dämpft AEC3 die eigene
        // Stimme mit (halbduplex beim Öffnen); diese Quote wird nur berichtet, sie gehört in den Hörtest mit Lautsprechern.
        var e = await EchoLauf(Kopplung12Db, echoAn: true, schutz: true, nahsprecher: true);
        Console.WriteLine($"  Doppelsprechen 12 dB: ohne Gegenrede {e.FreiGesendet}/{e.Frei} gesendet, in Gegenrede {e.UeberlapptGesendet}/{e.Ueberlappt}");
        PruefeWert.Mindestens(4, e.Frei, "genug Äußerungen ohne Gegenrede");
        Pruefe.Gleich(e.Frei, e.FreiGesendet, "ohne Gegenrede kommt jede eigene Äußerung durch");
    }

    internal sealed record EchoErgebnis(int Fehlphasen, int Frei, int FreiGesendet, int Ueberlappt, int UeberlapptGesendet);

    /// <summary>
    /// Echo-Prüfstand, 40 s: ein anderer Funker (synthetische Sprache, echt Opus-kodiert, 30 ms Laufzeit) spielt über die
    /// Wiedergabe; die Wiedergabe kommt über <paramref name="pfad"/> und 40 ms später ins Mikrofon, dazu Grundrauschen oder
    /// die eigene Stimme. Gezählt ab 5 s: Phasen ohne eigene Äußerung und die eigenen Äußerungen mit bzw. ohne Gegenrede.
    /// <paramref name="aec3"/>: abweichende AEC3-Konfiguration (Messung der Kandidaten), sonst die Voreinstellung der DLL.
    /// <paramref name="spielton"/>: zusätzlich Ton eines anderen Programms (48 kHz mono) auf demselben Ausgang, also im
    /// Loopback und im Echo. <paramref name="nurSprache"/> und <paramref name="mitFunk"/> stellen den Echoschutz wie
    /// <see cref="Sendekette.EchoschutzNurSprache"/> und <see cref="Sendekette.EchoschutzMitFunk"/> (Vergleich).
    /// </summary>
    internal static async Task<EchoErgebnis> EchoLauf(Func<float, float> pfad, bool echoAn, bool schutz, bool nahsprecher, string? aec3 = null,
        float[]? spielton = null, bool nurSprache = true, bool mitFunk = true, FunkKlickEinstellungen? klick = null, Action<Pruefstand>? vorbereiten = null)
    {
        const double Sekunden = 40;
        await using var s = new Pruefstand(aec3: aec3);
        vorbereiten?.Invoke(s);
        s.Maschine.Echounterdrueckung = echoAn;
        if (klick is not null) s.Maschine.Klick = klick;
        await s.StartenAsync();
        s.Maschine.Kette.EchoschutzAn = schutz;
        s.Maschine.Kette.EchoschutzNurSprache = nurSprache;
        s.Maschine.Kette.EchoschutzMitFunk = mitFunk;
        if (spielton is not null)
        {
            s.Fremdton = (takt, w) =>
            {
                var basis = (int)(takt - 1) * 480;
                for (var i = 0; i < 480 && basis + i < spielton.Length; i++)
                {
                    w[2 * i] += spielton[basis + i];
                    w[2 * i + 1] += spielton[basis + i];
                }
            };
        }
        Pruefe.Gleich(echoAn, s.System.Loopback is { Freigegeben: false }, "Loopback nur mit Echounterdrückung");

        // Fernsprecher: synthetische Sprache, echt kodiert, als Pakete mit 30 ms Laufzeit.
        var (fern, fernMarken) = Testszenen.Sprache(Sekunden, 61);
        using var kodierer = new OpusKodierer();
        var pakete = new List<(long Takt, uint Frame, byte[] Daten)>();
        var puffer = new byte[OpusKodierer.MaxPaketBytes];
        for (var i = 0; i + 960 <= fern.Length; i += 960)
        {
            var n = kodierer.Kodieren(fern.AsSpan(i, 960), puffer);
            pakete.Add((i / 480 + 3, (uint)(i / 960), puffer[..n]));
        }
        var echoLinie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
        s.Echo = (wiedergabe, mikrofon) =>
        {
            for (var i = 0; i < mikrofon.Length; i++)
            {
                echoLinie.Enqueue(pfad(0.5f * (wiedergabe[2 * i] + wiedergabe[2 * i + 1])));
                mikrofon[i] += echoLinie.Dequeue();
            }
        };
        var (nah, marken) = nahsprecher ? Testszenen.Sprache(Sekunden, 64) : (AnbindungSignale.Rauschen(fern.Length, -65, 62), []);
        var naechstes = 0;
        s.Abspielen(nah, takt =>
        {
            while (naechstes < pakete.Count && pakete[naechstes].Takt <= takt)
            {
                var p = pakete[naechstes++];
                var k = p.Frame == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine;
                _ = s.Maschine.SpracheEmpfangen(77, 0, 5, p.Frame, k, p.Daten, s.Uhr.Us);
            }
        });

        const int AbTakt = 500;
        var phasen = Phasen(s.Pakete);
        var fehl = phasen.Count(ph => ph.Beginn >= AbTakt && !marken.Any(a => ph.Beginn * 480 < a.Ende + 24000 && (ph.Ende + 1) * 480 > a.Start));
        // Gegenrede: Die fremde Äußerung (um Laufzeit und Puffer versetzt, 150 ms) liegt bis 0,5 s um den eigenen Anlaut.
        bool Ueberlappt(Aeusserung a) => fernMarken.Any(f => f.Start + 7200 - 24000 < a.Start && f.Ende + 7200 + 24000 > a.Start);
        bool Gesendet(Aeusserung a) => phasen.Any(ph => ph.Beginn * 480 <= a.Start + 9600 && (ph.Ende + 1) * 480 >= a.Start);
        var eigene = marken.Where(a => a.Start > AbTakt * 480).ToList();
        var frei = eigene.Where(a => !Ueberlappt(a)).ToList();
        var ueberlappt = eigene.Where(Ueberlappt).ToList();
        return new EchoErgebnis(fehl, frei.Count, frei.Count(Gesendet), ueberlappt.Count, ueberlappt.Count(Gesendet));
    }

    [Test]
    public static async Task Empfang_PegelFuerFunker_UndPanorama()
    {
        await using var s = new Pruefstand { Mitschreiben = true };
        await s.StartenAsync();
        s.Maschine.PegelFuerFunker(9, -120f); // praktisch stumm
        s.Maschine.SetzeNetzPanorama(4, -1f); // Netz 4 ganz links
        using var kodierer = new OpusKodierer();
        var puffer = new byte[OpusKodierer.MaxPaketBytes];
        var ton = Vokal(48000 * 2, 0.3f);
        for (var f = 0; f < 100; f++)
        {
            var n = kodierer.Kodieren(ton.AsSpan(f * 960, 960), puffer);
            _ = s.Maschine.SpracheEmpfangen(9, 4, 1, (uint)f, f == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine, puffer.AsSpan(0, n), s.Uhr.Us);
            _ = s.Maschine.SpracheEmpfangen(10, 4, 1, (uint)f, f == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine, puffer.AsSpan(0, n), s.Uhr.Us);
            s.Stille(2);
        }
        s.Stille(30);
        Pruefe.Gleich(2, s.Maschine.Statistik.Sprecher, "zwei Sprecher");
        var links = s.Ausgabe.Where((_, i) => i % 2 == 0).Max(MathF.Abs);
        var rechts = s.Ausgabe.Where((_, i) => i % 2 == 1).Max(MathF.Abs);
        PruefeWert.Mindestens(0.1, links, "Sprecher 10 links hörbar");
        PruefeWert.Hoechstens(1e-3, rechts, "rechts still (Panorama links, Sprecher 9 stumm)");
    }

    [Test]
    public static async Task Geraete_FolgenStandard_UndFallenZurueck()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        Pruefe.Gleich("mic-1", s.System.Aufnahme!.GeraetId, "Start auf Standard");
        s.System.StandardSetzen("mic-2", "spk-2");
        Pruefe.Gleich("mic-2", s.System.Aufnahme!.GeraetId, "neues Standardmikrofon übernommen");
        Pruefe.Gleich("spk-2", s.System.Wiedergabe!.GeraetId, "neuer Standardausgang übernommen");
        Pruefe.Gleich("spk-2", s.System.Loopback!.GeraetId, "Echoreferenz folgt dem Ausgang");

        await s.Maschine.StartenAsync("mic-1", "spk-1", CancellationToken.None);
        Pruefe.Gleich("mic-1", s.System.Aufnahme!.GeraetId, "gewähltes Mikrofon");
        s.System.Entfernen("mic-1");
        Pruefe.Gleich("mic-2", s.System.Aufnahme!.GeraetId, "gewähltes Gerät weg: Rückfall auf Standard");

        var alt = s.System.Wiedergabe!;
        alt.Abbrechen();
        Pruefe.Wahr(alt.Freigegeben, "abgebrochener Strom freigegeben");
        Pruefe.Wahr(s.System.Wiedergabe is { Freigegeben: false, GeraetId: "spk-1" }, "und neu geöffnet");
        Pruefe.Wahr(s.Fehler.Count >= 1, "Abbruch als Fehler gemeldet");
    }

    [Test]
    public static async Task EigeneThreads_SteuerUndMelder_OhneVerklemmung()
    {
        // Wie im Produkt: Steuer-Thread für Geräte, Melde-Thread für Pegel und Sende-Ausgang. Der Test spielt den
        // Audio-Thread; Geräteereignisse kommen aus einem dritten Thread.
        var system = new AttrappenSystem();
        var meldungen = 0;
        var pakete = 0;
        var fehler = new List<Exception>();
        var maschine = new KlangMaschine(new KlangMaschinenOptionen { AudioSystem = system });
        maschine.Pegel += _ => Interlocked.Increment(ref meldungen);
        maschine.SprachpaketBereit += (in SendeRahmen _) => Interlocked.Increment(ref pakete);
        maschine.Fehler += (_, e) =>
        {
            lock (fehler) fehler.Add(e);
        };
        Pruefe.Gleich(2, maschine.Eingaenge().Count, "Geräteliste über den Steuer-Thread");
        await maschine.StartenAsync(null, null, CancellationToken.None);
        maschine.SprechtasteGedrueckt();
        var (sprache, _) = Testszenen.Sprache(2, 81);
        var aufnahme = system.Aufnahme!;
        var wiedergabe = new float[960];
        for (var t = 0; t < 200; t++)
        {
            system.Wiedergabe!.Ziehen(wiedergabe);
            aufnahme.Liefern(sprache.AsSpan(t * 480, 480));
        }
        maschine.SprechtasteLosgelassen();
        aufnahme.Liefern(new float[480]);
        var wechsel = Task.Run(() => system.StandardSetzen("mic-2", "spk-2"));
        await wechsel;
        var frist = Stopwatch.StartNew();
        while ((system.Aufnahme!.GeraetId != "mic-2" || Volatile.Read(ref meldungen) < 100 || Volatile.Read(ref pakete) < 100) && frist.ElapsedMilliseconds < 5000)
            await Task.Delay(20);
        Pruefe.Gleich("mic-2", system.Aufnahme!.GeraetId, "Gerätewechsel im Steuer-Thread übernommen");
        PruefeWert.Mindestens(100, Volatile.Read(ref meldungen), "Pegelmeldungen aus dem Melde-Thread");
        PruefeWert.Mindestens(100, Volatile.Read(ref pakete), "Sende-Ausgang aus dem Melde-Thread");
        await maschine.StoppenAsync();
        Pruefe.Wahr(system.Aufnahme!.Freigegeben && system.Wiedergabe!.Freigegeben, "gestoppt");
        await maschine.DisposeAsync();
        await maschine.DisposeAsync(); // zweimal schadet nicht
        Pruefe.Gleich(0, fehler.Count, "keine Hintergrundfehler");
        _ = Pruefe.Wirft<ObjectDisposedException>(() => _ = maschine.StartenAsync(null, null, CancellationToken.None), "Starten nach Dispose");
    }

    [Test]
    public static async Task Geraet_441kHzStereo_UmrechnungInBeideRichtungen()
    {
        var format = new StromFormat(44100, 2);
        await using var s = new Pruefstand(aufnahme: format, wiedergabe: format) { Mitschreiben = true };
        await s.StartenAsync();
        s.Maschine.Hoerprobe = true;
        s.Maschine.SprechtasteGedrueckt();
        // 2 s Vokal, auf 44,1 kHz stereo umgerechnet (einfach, nur für den Test).
        var vokal = Vokal(48000 * 2, 0.3f);
        var geraet = new float[441 * 2];
        for (var t = 0; t < 200; t++)
        {
            for (var i = 0; i < 441; i++)
            {
                var v = vokal[Math.Min(vokal.Length - 1, (int)((t * 441 + i) * 48000L / 44100))];
                geraet[2 * i] = v;
                geraet[2 * i + 1] = v;
            }
            s.Takt(geraet);
        }
        s.Maschine.SprechtasteLosgelassen();
        for (var t = 0; t < 30; t++) s.Takt(new float[441 * 2]);
        PruefeWert.Nahe(100, s.Pakete.Count(p => p.Opus.Length > 0), 2, "2 s = 100 Pakete");
        PruefeWert.Mindestens(0.1, s.Ausgabe.Max(MathF.Abs), "Hörprobe kommt in 44,1 kHz an");
        Pruefe.Gleich(230 * 441 * 2, s.Ausgabe.Count, "Wiedergabe liefert genau die angeforderten Werte");
        PruefeWert.Nahe(230, s.Maschine.Statistik.Rahmen, 1, "230 Rahmen zu 10 ms aus 44,1 kHz (bis auf die Latenz des Resamplers)");
    }

    [Test]
    public static async Task Echtzeit_AudioPfade_AllokierenNicht()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Hoerprobe = true;
        var (signal, _) = SpracheMitTastatur(20, 71);
        using var kodierer = new OpusKodierer();
        var puffer = new byte[OpusKodierer.MaxPaketBytes];
        var fernFrame = 0u;
        var wiedergabe = new float[960];
        void Schritt(int t)
        {
            s.Uhr.Us += 10_000;
            s.System.Wiedergabe!.Ziehen(wiedergabe);
            s.System.Loopback!.Liefern(wiedergabe);
            s.System.Aufnahme!.Liefern(signal.AsSpan(t % 1900 * 480, 480));
            if (t % 2 == 0)
            {
                var n = kodierer.Kodieren(signal.AsSpan(t % 900 * 960, 960), puffer);
                _ = s.Maschine.SpracheEmpfangen(5, 1, 1, fernFrame++, Sprachkennzeichen.Keine, puffer.AsSpan(0, n), s.Uhr.Us);
            }
            if (t % 300 == 150) s.Maschine.SprechtasteGedrueckt();
            if (t % 300 == 250) s.Maschine.SprechtasteLosgelassen();
        }
        for (var t = 0; t < 600; t++)
        {
            Schritt(t);
            s.Maschine.Zustellen();
        }
        var bytes = PruefeWert.Allokiert(() =>
        {
            for (var t = 600; t < 1800; t++) Schritt(t);
        });
        s.Maschine.Zustellen();
        Console.WriteLine($"  Audiopfade: {bytes} Byte in 12 s (Aufnahme, Loopback, Wiedergabe, Empfang, Hörprobe, Taste)");
        Pruefe.Gleich(0L, bytes, "allokierte Bytes in den Audio-Pfaden");
        Pruefe.Wahr(s.Maschine.Statistik.Pakete > 100, "es wurde gesendet");
    }

    /// <summary>Stimmhafter Vokal (a) mit hartem Einsatz, 150 Hz, feste Amplitude; RNNoise lässt ihn durch.</summary>
    internal static float[] Vokal(int laenge, float amplitude)
    {
        var x = new double[laenge];
        var phase = 0.0;
        for (var i = 0; i < laenge; i++)
        {
            phase += 2 * Math.PI * 150 * (1 + 0.02 * Math.Sin(2 * Math.PI * 5 * i / 48000.0)) / 48000;
            double s = 0;
            for (var k = 1; k * 150 < 4000; k++) s += Math.Sin(k * phase) / k;
            x[i] = s;
        }
        var y = new double[laenge];
        foreach (var (f, b) in new[] { (730.0, 80.0), (1090.0, 120.0), (2440.0, 160.0) })
        {
            var c = -Math.Exp(-2 * Math.PI * b / 48000);
            var bb = 2 * Math.Exp(-Math.PI * b / 48000) * Math.Cos(2 * Math.PI * f / 48000);
            var a = 1 - bb - c;
            double y1 = 0, y2 = 0;
            for (var i = 0; i < laenge; i++)
            {
                var v = a * x[i] + bb * y1 + c * y2;
                y2 = y1;
                y1 = v;
                y[i] += v;
            }
        }
        var spitze = y.Max(Math.Abs);
        return [.. y.Select(v => (float)(amplitude * v / spitze))];
    }
}
