// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Geraete;
using Funk.Klang.Maschine;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>Geräteformate (Rate, Kanäle) zu 48 kHz mono in 10-ms-Rahmen und zurück; Journal und Ringe.</summary>
internal static class WandlerTests
{
    [Test]
    public static void Eingang_441StereoZu48Mono_RahmenUndPegel()
    {
        var rahmen = new List<float[]>();
        using var w = new Eingangswandler(new StromFormat(44100, 2), r => rahmen.Add(r.ToArray()));
        // 1 kHz, links 0,4 und rechts 0,2 Amplitude: Mittel 0,3; in ungeraden Blöcken wie ein Gerät.
        var zufall = new Random(3);
        var gesamt = 0;
        while (gesamt < 44100)
        {
            var n = Math.Min(44100 - gesamt, 100 + zufall.Next(900));
            var block = new float[n * 2];
            for (var i = 0; i < n; i++)
            {
                var v = MathF.Sin(2f * MathF.PI * 1000f * (gesamt + i) / 44100f);
                block[2 * i] = 0.4f * v;
                block[2 * i + 1] = 0.2f * v;
            }
            w.Einspeisen(block);
            gesamt += n;
        }
        PruefeWert.Nahe(100, rahmen.Count, 1, "1 s ergibt 100 Rahmen zu 480 (bis auf die Latenz)");
        var mitte = rahmen.Skip(10).Take(80).SelectMany(r => r).ToArray();
        PruefeWert.Nahe(0.3, mitte.Max(), 0.01, "Amplitude = Mittel der Kanäle");
        var nulldurchgaenge = 0;
        for (var i = 1; i < mitte.Length; i++) if (mitte[i - 1] < 0 && mitte[i] >= 0) nulldurchgaenge++;
        PruefeWert.Nahe(800, nulldurchgaenge, 2, "1 kHz bleibt 1 kHz (80 Rahmen = 0,8 s)");
    }

    [Test]
    public static void Eingang_48Mono_BitgleichDurchgereicht()
    {
        var rahmen = new List<float[]>();
        using var w = new Eingangswandler(new StromFormat(48000, 1), r => rahmen.Add(r.ToArray()));
        var x = AnbindungSignale.Rauschen(48000, -20, 5);
        w.Einspeisen(x.AsSpan(0, 1000));
        w.Einspeisen(x.AsSpan(1000));
        Pruefe.Gleich(100, rahmen.Count, "100 Rahmen");
        Pruefe.Wahr(rahmen.SelectMany(r => r).SequenceEqual(x), "ohne Umrechnung bitgleich");
    }

    [Test]
    public static void Ausgabe_Stereo48_Direkt_Und_Kanaele()
    {
        var aufrufe = 0;
        using var w = new Ausgabewandler(new StromFormat(48000, 6), (ziel, k) =>
        {
            aufrufe++;
            Pruefe.Gleich(6, k, "Quelle schreibt direkt ins 6-Kanal-Ziel");
            for (var i = 0; i < ziel.Length / k; i++)
            {
                ziel[i * k] = 0.5f;
                ziel[i * k + 1] = -0.5f;
                for (var c = 2; c < k; c++) ziel[i * k + c] = 0f;
            }
        });
        var puffer = new float[137 * 6];
        w.Fuellen(puffer);
        Pruefe.Gleich(1, aufrufe, "ein Aufruf, keine Zwischenpuffer");
        Pruefe.Wahr(puffer.Where((_, i) => i % 6 == 0).All(v => v == 0.5f), "links");
    }

    [Test]
    public static void Ausgabe_Zu441UndMono_ResampleUndAbmischung()
    {
        var phase = 0L;
        using var w = new Ausgabewandler(new StromFormat(44100, 1), (ziel, k) =>
        {
            Pruefe.Gleich(2, k, "Zwischenstufe stereo");
            for (var i = 0; i < ziel.Length / 2; i++)
            {
                var v = 0.5f * MathF.Sin(2f * MathF.PI * 500f * phase++ / 48000f);
                ziel[2 * i] = v * 0.70710677f;
                ziel[2 * i + 1] = v * 0.70710677f;
            }
        });
        var aus = new List<float>();
        var puffer = new float[441];
        for (var t = 0; t < 100; t++)
        {
            w.Fuellen(puffer);
            aus.AddRange(puffer);
        }
        var mitte = aus.Skip(4410).Take(44100 - 8820).ToArray();
        PruefeWert.Nahe(0.5, mitte.Max(), 0.01, "Mitte gemischt wieder volle Amplitude");
        var nulldurchgaenge = 0;
        for (var i = 1; i < mitte.Length; i++) if (mitte[i - 1] < 0 && mitte[i] >= 0) nulldurchgaenge++;
        PruefeWert.Nahe(400, nulldurchgaenge, 2, "500 Hz bleiben 500 Hz (0,8 s)");
    }

    [Test]
    public static void Journal_LeserVerwirftUeberschriebenes_UnterLast()
    {
        var j = new Rahmenjournal();
        const long Gesamt = 2_000_000;
        var schreiber = new Thread(() =>
        {
            for (long n = 0; n < Gesamt; n++)
                j.Schreiben(new RahmenEintrag(n, n, -n, n * 0.5f, n % 7, RahmenMerkmal.Sendet));
        });
        schreiber.Start();
        var puffer = new RahmenEintrag[800];
        long geprueft = 0;
        while (schreiber.IsAlive || geprueft == 0)
        {
            var von = Math.Max(0, j.Anzahl - 900);
            var n = j.Lesen(von, puffer, out var erste);
            for (var i = 0; i < n; i++)
            {
                var e = puffer[i];
                var nr = erste + i;
                // Jeder angenommene Eintrag muss vollständig zu seiner Nummer gehören (nie halb überschrieben).
                if (e.ZeitMs != nr || e.PegelDbfs != nr || e.RauschbodenDb != -nr || e.LinieDb != nr * 0.5f || e.Sprachwahrscheinlichkeit != nr % 7)
                    throw new PruefungException($"Eintrag {nr} zerrissen: {e}");
            }
            geprueft += n;
        }
        schreiber.Join();
        Console.WriteLine($"  Journal: {geprueft} Einträge unter Last geprüft");
        PruefeWert.Mindestens(1000, geprueft, "genug gelesen");
        var rest = j.Lesen(0, puffer, out var ab);
        Pruefe.Gleich(Gesamt - Rahmenjournal.Kapazitaet + 1, ab, "Älteres als die Kapazität ist weg");
        Pruefe.Gleich(Math.Min(800, Rahmenjournal.Kapazitaet - 1), rest, "Rest bis zur Puffergröße");
    }

    [Test]
    public static void RahmenRing_VollVerwirft_KuerzenBehaeltReserve()
    {
        var ring = new RahmenRing(4);
        var r = new float[480];
        for (var i = 0; i < 6; i++)
        {
            r[0] = i;
            _ = ring.Schreiben(r);
        }
        Pruefe.Gleich(2L, ring.Verworfen, "voll: zwei verworfen");
        Pruefe.Gleich(3, ring.Kuerzen(1), "bis auf einen gekürzt");
        Pruefe.Wahr(ring.Lesen(r) && r[0] == 3f, "der jüngste bleibt");
        Pruefe.Falsch(ring.Lesen(r), "dann leer");
    }
}
