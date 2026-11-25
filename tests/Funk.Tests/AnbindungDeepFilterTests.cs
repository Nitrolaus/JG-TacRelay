// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;
using Funk.Tests.Maschine;

namespace Funk.Tests;

/// <summary>DeepFilterNet3-Anbindung (Klangstufe B) gegen die echte deepfilternet.dll.</summary>
internal static class AnbindungDeepFilterTests
{
    private const int R = DeepFilterProzessor.RahmenSamples;

    private static float[] Durchlauf(DeepFilterProzessor d, float[] signal)
    {
        var aus = new float[signal.Length];
        for (int f = 0; f + R <= signal.Length; f += R)
            Pruefe.Wahr(d.Verarbeiten(signal.AsSpan(f, R), aus.AsSpan(f, R)), $"Rahmen {f / R} verarbeitet");
        Pruefe.Wahr(aus.All(float.IsFinite), "Ausgabe endlich");
        return aus;
    }

    [Test]
    public static void DeepFilter_VersionRahmenUndVerzoegerung()
    {
        Pruefe.Wahr(DeepFilterProzessor.Version.StartsWith("JG TacRelay deepfilternet 1.0.0 (DeepFilterNet 0.5.6, Modell DeepFilterNet3", StringComparison.Ordinal),
            "Version: " + DeepFilterProzessor.Version);
        using var d = new DeepFilterProzessor();
        Pruefe.Gleich(480, d.NativeRahmenSamples, "Rahmen der DLL");
        Pruefe.Gleich(DeepFilterProzessor.VerzoegerungSamples, d.NativeVerzoegerungSamples, "Verzögerung der DLL");
        Pruefe.Gleich(1440, DeepFilterProzessor.VerzoegerungSamples, "Verzögerung 30 ms");
    }

    [Test]
    public static void DeepFilter_RauschenGedaempft_SpracheBleibt()
    {
        using var d = new DeepFilterProzessor();
        var rauschen = AnbindungSignale.Rauschen(AnbindungSignale.Fs * 3, -40, 21);
        var aus = Durchlauf(d, rauschen);
        double rein = AnbindungSignale.PegelDb(rauschen.AsSpan(48000)), raus = AnbindungSignale.PegelDb(aus.AsSpan(48000));
        Pruefe.Wahr(rein - raus > 20, $"weißes Rauschen -40 dBFS um {rein - raus:0.0} dB gedämpft (mehr als 20)");

        // Die Formant-Synthese ist keine echte Stimme: DeepFilterNet3 nimmt ihr gemessen 6,4 dB (RNNoise im Test daneben bis zu
        // 12 dB Spielraum). Geprüft wird nur, dass sie bleibt und richtig skaliert ist; echte Sprache misst MessungKlangB.
        using var s = new DeepFilterProzessor();
        var sprache = AnbindungSignale.Sprache(AnbindungSignale.Fs * 5, 3);
        var ausSprache = Durchlauf(s, sprache);
        double pegel = AnbindungSignale.PegelDb(ausSprache.AsSpan(48000 + DeepFilterProzessor.VerzoegerungSamples)) - AnbindungSignale.PegelDb(sprache.AsSpan(48000));
        Pruefe.Wahr(pegel is > -10 and < 1, $"synthetische Sprache ohne Rauschen höchstens 10 dB leiser ({pegel:+0.0;-0.0} dB)");
    }

    [Test]
    public static void DeepFilter_Verzoegerung_1440Samples()
    {
        using var d = new DeepFilterProzessor();
        var signal = AnbindungSignale.Sprache(AnbindungSignale.Fs * 3, 6);
        var rauschen = AnbindungSignale.Rauschen(signal.Length, -55, 7);
        for (int i = 0; i < signal.Length; i++) signal[i] += rauschen[i];
        var aus = Durchlauf(d, signal);
        double beiSoll = AnbindungSignale.Korrelation(signal.AsSpan(9600, 96000), aus.AsSpan(9600 + 1440, 96000));
        foreach (int anders in new[] { 0, 480, 960, 1920 })
        {
            double k = AnbindungSignale.Korrelation(signal.AsSpan(9600, 96000), aus.AsSpan(9600 + anders, 96000));
            Pruefe.Wahr(beiSoll > k + 0.3, $"Korrelation bei 1440 ({beiSoll:0.000}) klar über {anders} ({k:0.000})");
        }
    }

    /// <summary>
    /// Digitale Stille bleibt still. Seit der Gegenprüfung rechnet DeepFilterNet3 auch sie (mit unterlegtem Rauschen von -64 dBFS, das
    /// es selbst wieder herausnimmt); vorher gab libDF ohne zu rechnen Nullen aus.
    /// </summary>
    [Test]
    public static void DeepFilter_Stille_BleibtStill()
    {
        using var d = new DeepFilterProzessor();
        var aus = new float[R * 100];
        aus.AsSpan().Fill(1f);
        for (int i = 0; i < 100; i++) Pruefe.Wahr(d.Verarbeiten(new float[R], aus.AsSpan(i * R, R)), "Stille verarbeitet");
        double pegel = AnbindungSignale.PegelDb(aus);
        double hoechster = aus.Max(MathF.Abs);
        Pruefe.Wahr(pegel < -85 && hoechster < 1e-3, $"Stille bleibt still: {pegel:0.0} dBFS, höchster Wert {hoechster:0.00000}");
        Pruefe.Gleich(100L, d.UntermalteRahmen, "jeder stille Rahmen untermalt");
    }

    [Test]
    public static void DeepFilter_NaNUndUnendlich_WerdenErsetzt()
    {
        using var d = new DeepFilterProzessor();
        var signal = AnbindungSignale.Sprache(R * 40, 9);
        var rahmen = new float[R];
        var aus = new float[R];
        for (int f = 0; f < 40; f++)
        {
            signal.AsSpan(f * R, R).CopyTo(rahmen);
            if (f == 10) { rahmen[3] = float.NaN; rahmen[7] = float.PositiveInfinity; rahmen[9] = 1e30f; }
            Pruefe.Wahr(d.Verarbeiten(rahmen, aus), $"Rahmen {f}");
            Pruefe.Wahr(aus.All(float.IsFinite), $"Rahmen {f}: Ausgabe endlich");
        }
        Pruefe.Gleich(2L, d.ErsetzteWerte, "NaN und Unendlich ersetzt (1e30 nur begrenzt)");
        Pruefe.Falsch(d.Gesperrt, "nicht gesperrt");
    }

    [Test]
    public static void DeepFilter_Laengen_WerfenVorDemNativenAufruf()
    {
        var d = new DeepFilterProzessor();
        Pruefe.Wirft<ArgumentException>(() => d.Verarbeiten(new float[479], new float[R]), "Eingang 479");
        Pruefe.Wirft<ArgumentException>(() => d.Verarbeiten(new float[R], new float[481]), "Ausgang 481");
        Pruefe.Gleich(0L, d.VerarbeiteteRahmen, "nie nativ aufgerufen");
        d.Dispose();
        Pruefe.Wirft<ObjectDisposedException>(() => d.Verarbeiten(new float[R], new float[R]), "nach Dispose");
    }

    [Test]
    public static void DeepFilter_ZweiInstanzen_BitgleichAuchInPlace()
    {
        var signal = AnbindungSignale.Sprache(R * 60, 8);
        var rauschen = AnbindungSignale.Rauschen(signal.Length, -50, 4);
        for (int i = 0; i < signal.Length; i++) signal[i] += rauschen[i];
        using var a = new DeepFilterProzessor();
        using var b = new DeepFilterProzessor();
        var getrennt = new float[R];
        var inPlace = (float[])signal.Clone();
        for (int f = 0; f < 60; f++)
        {
            Pruefe.Wahr(a.Verarbeiten(signal.AsSpan(f * R, R), getrennt), "a");
            Pruefe.Wahr(b.Verarbeiten(inPlace.AsSpan(f * R, R), inPlace.AsSpan(f * R, R)), "b");
            Pruefe.Wahr(getrennt.AsSpan().SequenceEqual(inPlace.AsSpan(f * R, R)), $"Rahmen {f} bitgleich");
        }
        Pruefe.Gleich(60L, a.VerarbeiteteRahmen, "Rahmen gezählt");
    }

    /// <summary>
    /// Gegenprüfung Klangstufe B: libDF rechnet einen Rahmen mit mittlerer Leistung unter 1e-7 (etwa -70 dBFS) nicht und hält dabei
    /// seinen Zustand an. Die letzten 30 ms vor einer Stille fehlten dann am Satzende und kamen erst kurz vor dem nächsten Anfang
    /// heraus (gemessen vor dem Fix: vor jedem Satz bis -43 dBFS bei Grundrauschen -80 dBFS, bis -26 dBFS bei hartem Gate im
    /// Mikrofon-Treiber). Klangkorpus, erste sechs Sätze, durch den Hochpass wie in der Kette.
    /// </summary>
    [Test]
    public static void DeepFilter_LeisesMikrofon_KeinRestDesVorigenSatzesVorDemNaechsten()
    {
        var a = Klangkorpus.Aeusserungen;
        int ende = a[6].Start / R * R;
        var sauber = MessungKlangB.Hochpass(Klangkorpus.Signal[..ende]);
        int versatz = AnbindungSignale.BesterVerzug(Klangkorpus.Signal, sauber, a[0].Start, 2 * 48000, 2400).Verzug;
        int v = DeepFilterProzessor.VerzoegerungSamples;
        float[] Filtern(float[] ein, out long untermalt)
        {
            using var d = new DeepFilterProzessor(schwellen: DeepFilterSchwellen.Voll);
            var aus = Durchlauf(d, MessungKlangB.Hochpass(ein[..ende]));
            untermalt = d.UntermalteRahmen;
            return aus;
        }
        double Geist(float[] aus) => Enumerable.Range(1, 5).Max(i => AnbindungSignale.PegelDb(aus.AsSpan(a[i].Start + versatz - 2880 + v, 2400)));
        double Ende(float[] aus, int i) => AnbindungSignale.PegelDb(aus.AsSpan(a[i].Ende + versatz - 7200 + v, 7200));

        var bezug = Filtern(Pumpmessung.Eingang(0f), out _);
        var leise = Filtern(Pumpmessung.Eingang(0f, rauschenDbfs: -80f), out var untermaltLeise);
        var gate = Filtern(MessungGegenpruefungKlangB.HartesGate(150), out var untermaltGate);
        Pruefe.Wahr(Geist(bezug) < -70, $"Voraussetzung: bei -65 dBFS kein Rest vor den Sätzen ({Geist(bezug):0.0} dBFS)");
        Pruefe.Wahr(Geist(leise) < -70, $"Grundrauschen -80 dBFS: vor den Sätzen kein Rest des vorigen ({Geist(leise):0.0} dBFS)");
        Pruefe.Wahr(Geist(gate) < -70, $"hartes Gate: vor den Sätzen kein Rest des vorigen ({Geist(gate):0.0} dBFS)");
        for (int i = 0; i < 6; i++)
            Pruefe.Wahr(Ende(leise, i) > Ende(bezug, i) - 6, $"Satz {i}: letzte 150 ms bei -80 dBFS {Ende(leise, i):0.0} gegen {Ende(bezug, i):0.0} dBFS bei -65");
        Pruefe.Wahr(untermaltLeise > 0 && untermaltGate > 0, $"leise Rahmen untermalt ({untermaltLeise}, {untermaltGate})");
    }

    /// <summary>Die Hülle allokiert im Rechenpfad nichts auf dem verwalteten Heap (tract rechnet nativ mit eigenem Speicher).</summary>
    [Test]
    public static void DeepFilter_Verarbeiten_AllokiertNichtVerwaltet()
    {
        using var d = new DeepFilterProzessor();
        var signal = AnbindungSignale.Sprache(R * 120, 10);
        var aus = new float[R];
        for (int f = 0; f < 20; f++) d.Verarbeiten(signal.AsSpan(f * R, R), aus);
        long vor = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 20; f < 120; f++) d.Verarbeiten(signal.AsSpan(f * R, R), aus);
        Pruefe.Gleich(0L, GC.GetAllocatedBytesForCurrentThread() - vor, "Bytes auf dem verwalteten Heap in 100 Rahmen");
    }
}
