// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

internal static class SendeaufbereitungTests
{
    private static float[] Rahmen(float amplitude, int nr, int seed = 0)
    {
        var x = new float[480];
        var r = new Random(seed * 7919 + nr);
        for (var i = 0; i < 480; i++)
            x[i] = amplitude * MathF.Sin(2f * MathF.PI * 150f * (nr * 480 + i) / 48000f) + 0.0008f * (float)(r.NextDouble() * 2 - 1);
        return x;
    }

    private static List<(Sprachkennzeichen K, int Laenge)> Lauf(Sendeaufbereitung s, IEnumerable<(float Amplitude, float P, bool Taste)> folge)
    {
        var pakete = new List<(Sprachkennzeichen, int)>();
        var nr = 0;
        foreach (var (a, p, taste) in folge)
        {
            var n = s.Verarbeiten(Rahmen(a, nr++), p, 0f, taste);
            for (var i = 0; i < n; i++) pakete.Add((s.Kennzeichen(i), s.Paket(i).Length));
        }
        return pakete;
    }

    private static IEnumerable<(float, float, bool)> Wiederhole(int anzahl, float a, float p, bool taste = false) =>
        Enumerable.Repeat((a, p, taste), anzahl);

    [Test]
    public static void Sprachaktivierung_BurstMitPreroll_UndEnde()
    {
        var kod = new AttrappenKodierer();
        var s = new Sendeaufbereitung(new SprachGate(), kod);
        var folge = Wiederhole(150, 0f, 0f).Concat(Wiederhole(21, 0.1f, 0.95f)).Concat(Wiederhole(40, 0f, 0f));
        var pakete = Lauf(s, folge);

        const Sprachkennzeichen va = Sprachkennzeichen.Sprachaktivierung;
        Pruefe.Gleich(va | Sprachkennzeichen.Beginn | Sprachkennzeichen.Preroll, pakete[0].K, "erstes Paket");
        Pruefe.Gleich(va | Sprachkennzeichen.Preroll, pakete[1].K, "zweites Paket Vorlauf");
        Pruefe.Gleich(va | Sprachkennzeichen.Preroll, pakete[2].K, "drittes Paket Vorlauf");
        Pruefe.Gleich(va, pakete[3].K, "danach Echtzeit");
        Pruefe.Gleich(3, pakete.Count(p => (p.K & Sprachkennzeichen.Preroll) != 0), "Vorlauf-Pakete");
        Pruefe.Gleich(va | Sprachkennzeichen.Ende, pakete[^1].K, "ENDE zum Schluss");
        Pruefe.Gleich(0, pakete[^1].Laenge, "ENDE ohne Opus");
        Pruefe.Gleich(1, pakete.Count(p => (p.K & Sprachkennzeichen.Beginn) != 0), "ein Beginn");
        // 147..170 Sprache plus 30 Rahmen Nachlauf: 54 Rahmen = 27 Pakete.
        Pruefe.Gleich(27, pakete.Count - 1, "Audio-Pakete");
        Pruefe.Wahr(pakete.Take(27).All(p => p.Laenge == 6), "Paketlänge vom Kodierer");
        Pruefe.Wahr(kod.Eingaben.All(e => e.Length == 960), "Kodierer bekommt 20 ms");
    }

    [Test]
    public static void Sprechtaste_OhnePreroll_UngeradeAufgefuellt()
    {
        var kod = new AttrappenKodierer();
        var s = new Sendeaufbereitung(new SprachGate(), kod);
        var folge = Wiederhole(5, 0f, 0f).Concat(Wiederhole(10, 0.1f, 0f, taste: true)).Concat(Wiederhole(5, 0f, 0f));
        var pakete = Lauf(s, folge);

        Pruefe.Gleich(Sprachkennzeichen.Beginn, pakete[0].K, "Beginn ohne VA und Preroll");
        Pruefe.Gleich(0, pakete.Count(p => (p.K & (Sprachkennzeichen.Preroll | Sprachkennzeichen.Sprachaktivierung)) != 0), "kein Preroll, keine VA");
        // 10 Rahmen mit Taste plus der ausgeblendete Loslass-Rahmen = 11 Rahmen = 6 Pakete (letztes aufgefüllt).
        Pruefe.Gleich(7, pakete.Count, "6 Audio-Pakete und ENDE");
        Pruefe.Gleich(Sprachkennzeichen.Ende, pakete[^1].K, "ENDE");
        var letztes = kod.Eingaben[^1];
        Pruefe.Wahr(letztes.AsSpan(480).ToArray().All(v => v == 0f), "zweite Hälfte mit Stille aufgefüllt");
        PruefeWert.Hoechstens(1e-6, MathF.Abs(letztes[479]), "Loslass-Rahmen ausgeblendet");
    }

    [Test]
    public static void TasteWaehrendSprachaktivierung_SchliesstAltePhase()
    {
        var kod = new AttrappenKodierer();
        var s = new Sendeaufbereitung(new SprachGate(), kod);
        var folge = Wiederhole(150, 0f, 0f).Concat(Wiederhole(20, 0.1f, 0.95f)).Concat(Wiederhole(6, 0.1f, 0.95f, taste: true)).Concat(Wiederhole(3, 0f, 0f));
        var pakete = Lauf(s, folge);

        var ende = pakete.FindIndex(p => (p.K & Sprachkennzeichen.Ende) != 0);
        Pruefe.Gleich(Sprachkennzeichen.Sprachaktivierung | Sprachkennzeichen.Ende, pakete[ende].K, "VA-Phase mit ENDE geschlossen");
        Pruefe.Gleich(Sprachkennzeichen.Beginn, pakete[ende + 1].K, "danach Tastenphase ohne VA");
        Pruefe.Gleich(2, pakete.Count(p => (p.K & Sprachkennzeichen.Beginn) != 0), "zwei Phasen");
        Pruefe.Gleich(2, pakete.Count(p => (p.K & Sprachkennzeichen.Ende) != 0), "zwei ENDE");
    }

    [Test]
    public static void Kodierfehler_UndVerlusthinweis()
    {
        var kod = new AttrappenKodierer { Scheitern = true };
        var s = new Sendeaufbereitung(new SprachGate(), kod);
        var pakete = Lauf(s, Wiederhole(4, 0.1f, 0f, taste: true).Concat(Wiederhole(1, 0f, 0f)));
        Pruefe.Gleich(3L, s.Kodierfehler, "drei Pakete nicht kodiert");
        Pruefe.Gleich(1, pakete.Count, "nur ENDE bleibt");
        s.SetzeVerlustHinweis(150);
        Pruefe.Gleich(100, kod.VerlustHinweis, "Verlusthinweis begrenzt");
        s.SetzeVerlustHinweis(7);
        Pruefe.Gleich(7, kod.VerlustHinweis, "Verlusthinweis durchgereicht");
    }

    [Test]
    public static void Echtzeit_Sendeaufbereitung_AllokiertNicht()
    {
        var kod = new AttrappenKodierer { Mitschreiben = false };
        var s = new Sendeaufbereitung(new SprachGate(), kod);
        var schaetzer = new Stimmhaftigkeit();
        var (signal, _) = Testszenen.Sprache(20, 13);
        var pos = 0;
        void Schritt()
        {
            var rahmen = signal.AsSpan(pos, 480);
            var n = s.Verarbeiten(rahmen, schaetzer.Schaetzen(rahmen), 0f, pos / 480 % 900 > 850);
            for (var i = 0; i < n; i++) _ = s.Paket(i).Length + (int)s.Kennzeichen(i);
            pos = (pos + 480) % (signal.Length - 480);
        }
        for (var i = 0; i < 300; i++) Schritt();
        var bytes = PruefeWert.Allokiert(() =>
        {
            for (var i = 0; i < 1800; i++) Schritt();
        });
        Pruefe.Gleich(0L, bytes, "allokierte Bytes in Sendeaufbereitung.Verarbeiten");
        PruefeWert.Mindestens(50, kod.Zaehler, "es wurde kodiert");
    }

    [Test]
    public static void EndeZuEnde_GateKodiererJitterpufferMischer_NieUeberEins()
    {
        // Senden: Szene mit Sprache durch Schätzer, Gate und Kodierer-Attrappe.
        var kod = new AttrappenKodierer { Mitschreiben = false };
        var sende = new Sendeaufbereitung(new SprachGate(), kod);
        var schaetzer = new Stimmhaftigkeit();
        var (signal, marken) = Testszenen.Sprache(30, 21);

        // Transport: Frame-Nr an der Sitzungsuhr (20-ms-Raster), Sprechphase je BEGINN, ENDE dreifach, Jitter 0-40 ms.
        var zufall = new Random(4);
        var pakete = new List<SimPaket>();
        ushort phase = 100;
        uint frame = 0;
        var offen = false;
        for (var r = 0; r < signal.Length / 480; r++)
        {
            var n = sende.Verarbeiten(signal.AsSpan(r * 480, 480), schaetzer.Schaetzen(signal.AsSpan(r * 480, 480)));
            var jetztUs = (r + 1) * 10_000L;
            for (var i = 0; i < n; i++)
            {
                var k = sende.Kennzeichen(i);
                if ((k & Sprachkennzeichen.Beginn) != 0)
                {
                    phase++;
                    frame = Math.Max(frame + 1, (uint)(jetztUs / 20_000) - 8);
                    offen = true;
                }
                if (!offen) continue;
                var ankunft = jetztUs + 20_000 + (long)(zufall.NextDouble() * 40_000);
                if ((k & Sprachkennzeichen.Ende) != 0)
                {
                    for (var j = 0; j < 3; j++) pakete.Add(new SimPaket(ankunft + j * 20_000, phase, frame + (uint)j, k, []));
                    frame += 3;
                    offen = false;
                    continue;
                }
                pakete.Add(new SimPaket(ankunft, phase, frame++, k, sende.Paket(i).ToArray()));
            }
        }
        Pruefe.Wahr(pakete.Count > 100, "es wurde gesendet");

        // Empfang: Jitterpuffer und Mischer (Stereo), dazu eine laute zweite Quelle.
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var mischer = new Mischer();
        mischer.QuelleHinzufuegen(jp, 2, funkerPegelDb: 10f);
        mischer.QuelleHinzufuegen(new TestQuelle(0.8f, 97f), 3, funkerPegelDb: 6f);
        var sortiert = pakete.OrderBy(p => p.AnkunftUs).ToList();
        var index = 0;
        var aus = new float[480 * 2];
        var spitze = 0f;
        for (var t = 3_000L; t < 32_000_000; t += 10_000)
        {
            while (index < sortiert.Count && sortiert[index].AnkunftUs <= t)
            {
                var p = sortiert[index++];
                jp.Einreihen(p.AnkunftUs, p.Phase, p.Frame, p.Kennzeichen, p.Daten);
            }
            uhr.Us = t;
            mischer.Mischen(aus, 2);
            foreach (var v in aus)
            {
                Pruefe.Wahr(float.IsFinite(v), "endlich");
                spitze = MathF.Max(spitze, MathF.Abs(v));
            }
        }
        var st = jp.Statistik;
        PruefeWert.Hoechstens(1.0, spitze, "Ausgabe nie über 1,0");
        PruefeWert.Mindestens(0.9, spitze, "laut genug, dass der Begrenzer arbeitet");
        Pruefe.Gleich((long)marken.Count, st.Phasen, "je Äußerung eine Sprechphase");
        // Jitter wirkt hier auf jedes Paket einzeln, auch innerhalb eines Vorlauf-Bursts; ein Burst-Paket kann
        // dadurch hinter den Wiedergabestart fallen. Mindestens 99 % müssen normal dekodiert werden.
        var audio = pakete.Count(p => p.Daten.Length > 0);
        PruefeWert.Mindestens(0.99 * audio, dek.Normal, "normal dekodierte Audio-Pakete");
        Pruefe.Gleich((long)audio, dek.Normal + st.SpaetVerworfen, "jedes Audio-Paket gespielt oder als verspätet gezählt");
    }
}
