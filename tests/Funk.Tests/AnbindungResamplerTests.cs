// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;

namespace Funk.Tests;

/// <summary>Resampler-Anbindung gegen die echte speexdsp_resampler.dll.</summary>
internal static class AnbindungResamplerTests
{
    /// <summary>Schiebt ein Signal in 10-ms-Blöcken durch, danach so viele Nullen wie die Eingangslatenz.</summary>
    private static float[] Durchschieben(Resampler r, float[] ein, int block, out int ohneNachlauf)
    {
        var aus = new List<float>();
        var puffer = new float[block * 4 + 64];
        int pos = 0;
        while (pos < ein.Length)
        {
            int n = Math.Min(block, ein.Length - pos);
            r.Verarbeiten(ein.AsSpan(pos, n), puffer, out int verbraucht, out int erzeugt);
            Pruefe.Wahr(verbraucht > 0, "Fortschritt");
            aus.AddRange(puffer.AsSpan(0, erzeugt * r.Kanaele).ToArray());
            pos += verbraucht * r.Kanaele;
        }
        ohneNachlauf = aus.Count;
        var nullen = new float[r.EingangsLatenz * r.Kanaele];
        pos = 0;
        while (pos < nullen.Length)
        {
            r.Verarbeiten(nullen.AsSpan(pos), puffer, out int verbraucht, out int erzeugt);
            Pruefe.Wahr(verbraucht > 0, "Fortschritt im Nachlauf");
            aus.AddRange(puffer.AsSpan(0, erzeugt * r.Kanaele).ToArray());
            pos += verbraucht * r.Kanaele;
        }
        return aus.ToArray();
    }

    private static int Spitze(float[] x)
    {
        int best = 0;
        for (int i = 1; i < x.Length; i++) if (Math.Abs(x[i]) > Math.Abs(x[best])) best = i;
        return best;
    }

    [Test]
    public static void Resampler_48zu16_LaengeUndLatenz()
    {
        using var r = new Resampler(48000, 16000, 5);
        Pruefe.Gleich(120, r.EingangsLatenz, "Eingangslatenz");
        Pruefe.Gleich(40, r.AusgangsLatenz, "Ausgangslatenz (2,5 ms)");
        Pruefe.Gleich(5, r.Qualitaet, "Qualität");
        Pruefe.Gleich((48000, 16000), r.Raten, "Raten");
        Pruefe.Gleich((3u, 1u), r.Verhaeltnis, "gekürztes Verhältnis");

        var aus = Durchschieben(r, AnbindungSignale.Sinus(48000, 1000, 0.5), 480, out int signal);
        Pruefe.Wahr(Math.Abs(signal - 16000) <= 1, $"1 s ergibt {signal} Werte");
        Pruefe.Gleich(16000 + 40, aus.Length, "mit Nachlauf");
        double pegel = AnbindungSignale.PegelDb(aus.AsSpan(200, 15000));
        Pruefe.Wahr(Math.Abs(pegel - AnbindungSignale.PegelDb(AnbindungSignale.Sinus(48000, 1000, 0.5))) < 0.1, $"Pegel {pegel:0.00} dBFS");

        r.Zuruecksetzen();
        var impuls = new float[4800];
        impuls[480] = 1f;
        Pruefe.Gleich(160 + 40, Spitze(Durchschieben(r, impuls, 480, out _)), "Impuls nach Zurücksetzen: Ort + Ausgangslatenz");
        r.Zuruecksetzen();
        r.UeberspringeNullen();
        Pruefe.Gleich(160, Spitze(Durchschieben(r, impuls, 480, out _)), "Impuls nach UeberspringeNullen: am Ort");
    }

    [Test]
    public static void Resampler_441zu48_LaengeUndLatenz()
    {
        using var r = new Resampler(44100, 48000, 5);
        Pruefe.Gleich(40, r.EingangsLatenz, "Eingangslatenz");
        Pruefe.Gleich(44, r.AusgangsLatenz, "Ausgangslatenz");
        var aus = Durchschieben(r, AnbindungSignale.Sinus(44100, 1000, 0.5), 441, out int signal);
        Pruefe.Wahr(Math.Abs(signal - 48000) <= 1, $"1 s ergibt {signal} Werte");
        Pruefe.Gleich(48000 + 44, aus.Length, "mit Nachlauf");

        r.Zuruecksetzen();
        var impuls = new float[4410];
        impuls[441] = 1f;
        Pruefe.Gleich(480 + 44, Spitze(Durchschieben(r, impuls, 441, out _)), "Impuls: Ort + Ausgangslatenz");

        r.SetzeRate(48000, 16000);
        Pruefe.Gleich((48000, 16000), r.Raten, "umgestellt");
        Pruefe.Gleich(40, r.AusgangsLatenz, "Latenz folgt der Rate");
    }

    [Test]
    public static void Resampler_Fraktional_Drift100ppm()
    {
        // 100 ppm schneller ausgeben: 48000 -> 48004,8 Hz
        using var r = Resampler.Fraktional(480000, 480048, 48000, 48005, 5);
        Pruefe.Gleich((10000u, 10001u), r.Verhaeltnis, "gekürztes Verhältnis");
        var ein = AnbindungSignale.Sinus(480000, 440, 0.5);
        Durchschieben(r, ein, 480, out int signal);
        Pruefe.Wahr(Math.Abs(signal - 480048) <= 2, $"10 s ergeben {signal} Werte, Soll 480048");

        using var fest = new Resampler(48000, 48000, 5);
        fest.SetzeRateFraktional(480048, 480000, 48005, 48000);
        Pruefe.Gleich((10001u, 10000u), fest.Verhaeltnis, "nachträglich fraktional");
        Durchschieben(fest, ein, 480, out int signal2);
        Pruefe.Wahr(Math.Abs(signal2 - 479952) <= 2, $"langsamer: {signal2} Werte, Soll 479952");
    }

    [Test]
    public static void Resampler_Stereo_Verschraenkt()
    {
        using var r = new Resampler(48000, 16000, 10, kanaele: 2);
        var links = AnbindungSignale.Sinus(48000, 1000, 0.5);
        var rechts = AnbindungSignale.Sinus(48000, 440, 0.25);
        var ein = new float[96000];
        for (int i = 0; i < 48000; i++) { ein[2 * i] = links[i]; ein[2 * i + 1] = rechts[i]; }
        var aus = Durchschieben(r, ein, 960, out int signal);
        Pruefe.Wahr(Math.Abs(signal - 32000) <= 2, $"verschränkt {signal} Werte, Soll 32000");
        Pruefe.Gleich(0, aus.Length % 2, "ganze Rahmen");
        var l = new float[15000];
        var re = new float[15000];
        for (int i = 0; i < 15000; i++) { l[i] = aus[2 * (i + 500)]; re[i] = aus[2 * (i + 500) + 1]; }
        Pruefe.Wahr(Math.Abs(AnbindungSignale.PegelDb(l) - AnbindungSignale.PegelDb(links)) < 0.1, "linker Kanal Pegel");
        Pruefe.Wahr(Math.Abs(AnbindungSignale.PegelDb(re) - AnbindungSignale.PegelDb(rechts)) < 0.1, "rechter Kanal Pegel");
    }

    [Test]
    public static void Resampler_Laengen_WerfenVorDemNativenAufruf()
    {
        using var r = new Resampler(48000, 16000, kanaele: 2);
        var aus = new float[400];
        Pruefe.Wirft<ArgumentException>(() => r.Verarbeiten(new float[3], aus, out _, out _), "Eingang ungerade bei stereo");
        Pruefe.Wirft<ArgumentException>(() => r.Verarbeiten(new float[4], new float[5], out _, out _), "Ausgang ungerade bei stereo");
        var gemeinsam = new float[1000];
        Pruefe.Wirft<ArgumentException>(() => r.Verarbeiten(gemeinsam.AsSpan(0, 600), gemeinsam.AsSpan(400, 600), out _, out _), "Überlappung");
        Pruefe.Gleich(0L, r.Aufrufe, "nie nativ aufgerufen");

        r.Verarbeiten([], aus, out int v, out int e);
        Pruefe.Gleich((0, 0), (v, e), "leer: nichts zu tun");
        Pruefe.Gleich(0L, r.Aufrufe, "leer: kein nativer Aufruf");

        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new Resampler(48000, 16000, 11).Dispose(), "Qualität 11");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new Resampler(0, 16000).Dispose(), "Rate 0");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new Resampler(48000, 16000, kanaele: 9).Dispose(), "9 Kanäle");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => Resampler.Fraktional(0, 1, 48000, 48000).Dispose(), "Zähler 0");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => r.SetzeRateFraktional(1, 0, 48000, 48000), "Nenner 0");
    }
}
