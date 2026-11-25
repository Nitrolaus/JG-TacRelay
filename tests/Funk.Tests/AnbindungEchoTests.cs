// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;

namespace Funk.Tests;

/// <summary>APM-Anbindung (AEC3, HPF, NS, AGC2) gegen die echte webrtc_apm.dll.</summary>
internal static class AnbindungEchoTests
{
    private const int R = 480;
    private const int EchoVerzugMs = 40;

    /// <summary>Aufnahme = Wiedergabe um 40 ms verzögert und um 6 dB gedämpft, plus Nahsprecher und Grundrauschen.</summary>
    private static float[] Aufnahme(float[] fern, float[]? nah, ulong seed)
    {
        var grund = AnbindungSignale.Rauschen(fern.Length, -70, seed);
        int verzug = AnbindungSignale.Fs / 1000 * EchoVerzugMs;
        var auf = new float[fern.Length];
        for (int i = 0; i < auf.Length; i++)
            auf[i] = (i >= verzug ? 0.5f * fern[i - verzug] : 0f) + (nah?[i] ?? 0f) + grund[i];
        return auf;
    }

    [Test]
    public static void Echo_SynthetischesEcho_ErleSteigtNachKonvergenz()
    {
        int n = AnbindungSignale.Fs * 8;
        var fern = AnbindungSignale.Sprache(n, 22, -18);
        var aufnahme = Aufnahme(fern, null, 23);
        var roh = (float[])aufnahme.Clone();

        using var apm = new EchoProzessor();
        apm.Konfigurieren(new ApmEinstellungen(true, true, ApmRauschStufe.Aus, false, 0f));
        Pruefe.Gleich(EchoVerzugMs, apm.SetzeVerzoegerung(EchoVerzugMs), "Startwert Verzögerung");

        EchoStatistik? ersteGueltige = null;
        EchoStatistik nachEinerSekunde = default;
        for (int f = 0; f < n / R; f++)
        {
            Pruefe.Gleich(0, apm.VerarbeiteWiedergabe(fern.AsSpan(f * R, R)), "Wiedergabe-Rückgabe");
            Pruefe.Gleich(0, apm.VerarbeiteAufnahme(aufnahme.AsSpan(f * R, R)), "Aufnahme-Rückgabe");
            var s = apm.Statistik();
            if (f == 99) nachEinerSekunde = s;
            if (s.Gueltig && ersteGueltige is null) ersteGueltige = s;
        }
        var ende = apm.Statistik();

        Pruefe.Falsch(nachEinerSekunde.Gueltig, "nach 1 s noch Anlaufphase von AEC3");
        Pruefe.Wahr(ersteGueltige is not null, "Kennwerte werden gültig");
        Pruefe.Wahr(ende.Gueltig, "am Ende gültig");
        Pruefe.Wahr(ende.Erle > ersteGueltige!.Value.Erle + 10, $"ERLE steigt: erste gültige {ersteGueltige.Value.Erle:0.0} dB, nach 8 s {ende.Erle:0.0} dB");
        Pruefe.Wahr(ende.Erle > 25, $"ERLE nach Konvergenz {ende.Erle:0.0} dB");
        Pruefe.Wahr(ende.Erl is > 3 and < 25, $"ERL {ende.Erl:0.0} dB (Pfad 6 dB, Schätzer nach Minimumstatistik)");
        Pruefe.Wahr(Math.Abs(ende.VerzoegerungMs - EchoVerzugMs) <= 12, $"geschätzte Verzögerung {ende.VerzoegerungMs} ms");

        // Eigene Messung über die letzten 2 s: das Echo ist tatsächlich weg.
        int von = n - 2 * AnbindungSignale.Fs;
        double daempfung = AnbindungSignale.PegelDb(roh.AsSpan(von)) - AnbindungSignale.PegelDb(aufnahme.AsSpan(von));
        Pruefe.Wahr(daempfung > 20, $"Echo um {daempfung:0.0} dB gedämpft");
        Pruefe.Gleich(800L, apm.AufnahmeRahmen, "Aufnahmerahmen");
        Pruefe.Gleich(800L, apm.WiedergabeRahmen, "Wiedergaberahmen");
    }

    [Test]
    public static void Echo_Nahsprecher_BleibtErhalten()
    {
        int n = AnbindungSignale.Fs * 9, fernEnde = AnbindungSignale.Fs * 6, nahStart = fernEnde + AnbindungSignale.Fs / 2;
        var fern = AnbindungSignale.Sprache(n, 41, -18);
        fern.AsSpan(fernEnde).Clear();
        var nah = AnbindungSignale.Sprache(n, 42, -24);
        nah.AsSpan(0, nahStart).Clear();
        var aufnahme = Aufnahme(fern, nah, 43);
        var roh = (float[])aufnahme.Clone();

        using var apm = new EchoProzessor();
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk with { Agc2Adaptiv = false });
        apm.SetzeVerzoegerung(EchoVerzugMs);
        for (int f = 0; f < n / R; f++)
        {
            apm.VerarbeiteWiedergabe(fern.AsSpan(f * R, R));
            apm.VerarbeiteAufnahme(aufnahme.AsSpan(f * R, R));
        }
        double echo = AnbindungSignale.PegelDb(roh.AsSpan(4 * AnbindungSignale.Fs, 2 * AnbindungSignale.Fs)) - AnbindungSignale.PegelDb(aufnahme.AsSpan(4 * AnbindungSignale.Fs, 2 * AnbindungSignale.Fs));
        double nahVerlust = AnbindungSignale.PegelDb(roh.AsSpan(nahStart)) - AnbindungSignale.PegelDb(aufnahme.AsSpan(nahStart));
        Pruefe.Wahr(echo > 20, $"Echo 4-6 s um {echo:0.0} dB gedämpft");
        Pruefe.Wahr(Math.Abs(nahVerlust) < 3, $"Nahsprecher verändert um {nahVerlust:0.0} dB");
    }

    [Test]
    public static void Echo_Laengen_WerfenVorDemNativenAufruf()
    {
        using var mono = new EchoProzessor();
        Pruefe.Gleich(480, mono.RahmenWerte, "48 kHz mono");
        Pruefe.Wirft<ArgumentException>(() => mono.VerarbeiteAufnahme(new float[479]), "Aufnahme 479");
        Pruefe.Wirft<ArgumentException>(() => mono.VerarbeiteAufnahme(new float[481]), "Aufnahme 481");
        Pruefe.Wirft<ArgumentException>(() => mono.VerarbeiteWiedergabe(new float[479]), "Wiedergabe 479");
        Pruefe.Wirft<ArgumentException>(() => mono.VerarbeiteWiedergabe(new float[960]), "Wiedergabe 960");
        Pruefe.Gleich(0L, mono.AufnahmeRahmen + mono.WiedergabeRahmen, "mono: nie nativ aufgerufen");

        using var stereo = new EchoProzessor(48000, 2);
        Pruefe.Gleich(960, stereo.RahmenWerte, "48 kHz stereo planar");
        Pruefe.Wirft<ArgumentException>(() => stereo.VerarbeiteAufnahme(new float[480]), "stereo mit Mono-Länge (Befund BAUBERICHT)");
        Pruefe.Wirft<ArgumentException>(() => stereo.VerarbeiteWiedergabe(new float[480]), "stereo Wiedergabe mit Mono-Länge");
        Pruefe.Gleich(0L, stereo.AufnahmeRahmen + stereo.WiedergabeRahmen, "stereo: nie nativ aufgerufen");

        using var cd = new EchoProzessor(44100);
        Pruefe.Gleich(441, cd.RahmenWerte, "44,1 kHz");
        Pruefe.Wirft<ArgumentException>(() => cd.VerarbeiteAufnahme(new float[480]), "44,1 kHz mit 480 statt 441 (Befund BAUBERICHT)");
        Pruefe.Gleich(0L, cd.AufnahmeRahmen, "44,1 kHz: nie nativ aufgerufen");

        Pruefe.Gleich(0, stereo.VerarbeiteAufnahme(new float[960]), "stereo richtig");
        Pruefe.Gleich(0, cd.VerarbeiteWiedergabe(new float[441]), "44,1 kHz richtig");
        Pruefe.Gleich(1L, stereo.AufnahmeRahmen, "gezählt");

        stereo.Dispose();
        Pruefe.Wirft<ObjectDisposedException>(() => stereo.VerarbeiteAufnahme(new float[960]), "nach Dispose");
    }

    [Test]
    public static void Echo_Parameter_WerdenVorherGeprueft()
    {
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new EchoProzessor(22050).Dispose(), "22,05 kHz nicht durch 100 teilbar");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new EchoProzessor(7900).Dispose(), "unter 8 kHz");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new EchoProzessor(48000, 3).Dispose(), "3 Kanäle");

        using var apm = new EchoProzessor();
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => apm.Konfigurieren(ApmEinstellungen.Sprechfunk with { Agc2MaxGainDb = 0 }), "AGC2 0 dB");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => apm.Konfigurieren(ApmEinstellungen.Sprechfunk with { Agc2MaxGainDb = float.NaN }), "AGC2 NaN");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => apm.Konfigurieren(ApmEinstellungen.Sprechfunk with { Agc2MaxGainDb = 91 }), "AGC2 91 dB");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => apm.Konfigurieren(ApmEinstellungen.Aus with { Rauschen = (ApmRauschStufe)4 }), "NS-Stufe 4");
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk with { Agc2Adaptiv = false, Agc2MaxGainDb = 0 });
        apm.Konfigurieren(ApmEinstellungen.Aus with { Rauschen = ApmRauschStufe.SehrHoch });

        Pruefe.Gleich(500, apm.SetzeVerzoegerung(900), "oben begrenzt");
        Pruefe.Gleich(0, apm.SetzeVerzoegerung(-5), "unten begrenzt");
    }

    [Test]
    public static void Echo_OhneAec_StatistikUngueltig()
    {
        using var apm = new EchoProzessor();
        apm.Konfigurieren(ApmEinstellungen.Aus with { Hochpass = true });
        var rahmen = AnbindungSignale.Rauschen(R, -30, 9);
        apm.VerarbeiteWiedergabe(rahmen);
        apm.VerarbeiteAufnahme(rahmen);
        var s = apm.Statistik();
        Pruefe.Falsch(s.Gueltig, "ohne AEC ungültig");
        Pruefe.Wahr(float.IsNaN(s.Erle) && float.IsNaN(s.Erl), "NaN ohne AEC");
        Pruefe.Gleich(-1, s.VerzoegerungMs, "Verzögerung -1 ohne AEC");
    }
}
