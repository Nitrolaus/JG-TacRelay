// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using Funk.Klang.Nativ;

namespace Funk.Klang.Kern;

/// <summary>
/// Schwellen der Rechenstufen von libDF nach dem lokalen SNR eines Rahmens: unter <paramref name="MinDb"/> gilt er als reines
/// Rauschen und wird stumm, über <paramref name="MaxErbDb"/> bleibt er unbearbeitet, über <paramref name="MaxDfDb"/> entfällt
/// die zweite Stufe (Deep Filtering).
/// </summary>
public readonly record struct DeepFilterSchwellen(float MinDb, float MaxErbDb, float MaxDfDb)
{
    /// <summary>Voreinstellung von libDF (RuntimeParams): -10 / 30 / 20 dB.</summary>
    public static DeepFilterSchwellen LibDf => new(-10f, 30f, 20f);

    /// <summary>Voreinstellung des LADSPA-Plugins von DeepFilterNet: -15 / 35 / 35 dB.</summary>
    public static DeepFilterSchwellen Ladspa => new(-15f, 35f, 35f);

    /// <summary>Jeder Rahmen voll gerechnet, wie DeepFilterNet in Python: -100 / 100 / 100 dB.</summary>
    public static DeepFilterSchwellen Voll => new(-100f, 100f, 100f);
}

/// <summary>
/// DeepFilterNet3-Rauschunterdrückung (Klangstufe B, Rauschfilter "Stark"), 48 kHz mono, fest 480 Samples (10 ms) je Rahmen,
/// Modell fest in deepfilternet.dll. Liefert je Rahmen das entrauschte Signal; die Sprachwahrscheinlichkeit für Gate und
/// Pegelstufe kommt weiter von RNNoise, das daneben warm läuft.
/// </summary>
/// <remarks>
/// Die Hülle kopiert in eigene, vorab angelegte Puffer (der native Code sieht nie die Spans des Aufrufers, Eingang und Ausgang
/// dürfen derselbe Puffer sein), ersetzt NaN und ±Unendlich durch 0 (gezählt in <see cref="ErsetzteWerte"/>) und begrenzt auf
/// [-1, 1] wie bei RNNoise. Auf dem verwalteten Heap allokiert der Rechenpfad nichts; tract legt nativ je Rahmen Speicher an
/// (gemessen in docs\MODULBERICHT-klang.md, Klangstufe B). Keine Sperre. Nur aus einem Thread zur Zeit; anlegen (einige hundert
/// Millisekunden) nie im Audio-Thread.
/// <para>
/// Meldet die DLL einen Fehler (gefangene Panik oder Rechenfehler), ist der Zustand gesperrt: <see cref="Verarbeiten"/> liefert
/// falsch und Stille, <see cref="Gesperrt"/> wird wahr. Der Aufrufer fällt dann auf RNNoise zurück.
/// </para>
/// <para>
/// Verzögerung 1440 Samples (30 ms): 10 ms Überlappung der STFT und 2 Rahmen Vorgriff des Modells, also 10 ms mehr als RNNoise
/// (960). Die Autoren nennen 40 ms algorithmische Latenz; darin steckt zusätzlich das Sammeln des Rahmens, das jede Kette hat.
/// </para>
/// <para>
/// Eigenheit von libDF: Einen Rahmen mit mittlerer Leistung unter 1e-7 (etwa -70 dBFS) rechnet es nicht, gibt Nullen aus und hält
/// seinen Zustand an. Die letzten 30 ms vor einer Stille fehlten dann am Satzende und kamen erst kurz vor dem nächsten Anfang
/// heraus (Gegenprüfung: bei Grundrauschen -75 dBFS bis -43 dBFS vor jedem Satz, bei hartem Gate im Mikrofon bis -18 dBFS). Die
/// Hülle legt einem so leisen Rahmen deshalb ein festes, leises Rauschen unter (<see cref="UntermalteRahmen"/>, -64 dBFS), damit
/// jeder Rahmen gerechnet wird; DeepFilterNet3 nimmt es als Rauschen wieder heraus (Test
/// DeepFilter_NachStille_SatzendeBleibtUndKeinRestVorDemNaechstenAnfang).
/// </para>
/// </remarks>
public sealed unsafe class DeepFilterProzessor : IDisposable
{
    public const int RahmenSamples = 480;

    /// <summary>Verzögerung Ausgang gegen Eingang (Test DeepFilter_Verzoegerung_1440Samples).</summary>
    public const int VerzoegerungSamples = 1440;

    /// <summary>
    /// Mittlere Leistung, unter der die Hülle Rauschen unterlegt: das Doppelte der Grenze von libDF (1e-7, in float gerechnet), damit
    /// kein Rahmen knapp darunter liegen bleibt.
    /// </summary>
    internal const double UntermalenUnter = 2e-7;

    /// <summary>Spitze des unterlegten Rauschens: gleichverteilt in ±1,1e-3, mittlere Leistung 4e-7 (-64 dBFS).</summary>
    internal const float UntermalungSpitze = 1.1e-3f;

    private readonly DeepFilterHandle _handle;
    private readonly float[] _ein = new float[RahmenSamples];
    private readonly float[] _aus = new float[RahmenSamples];
    private long _rahmen;
    private long _ersetzt;
    private long _untermalt;
    private uint _zufall = 0x2545F491;
    private volatile int _fehler;

    /// <param name="hoechsteAbsenkungDb">Höchste Absenkung in dB; 100 und mehr heißt unbegrenzt (Voreinstellung von libDF).</param>
    /// <param name="schwellen">Schwellen der Rechenstufen; null heißt die von libDF (<see cref="DeepFilterSchwellen.LibDf"/>).</param>
    /// <exception cref="InvalidOperationException">Die DLL kann keinen Filter anlegen (Code im Text).</exception>
    public DeepFilterProzessor(float hoechsteAbsenkungDb = 100f, DeepFilterSchwellen? schwellen = null)
    {
        int code;
        _handle = DeepFilterNativ.Erstellen(hoechsteAbsenkungDb, 0f, &code);
        if (_handle.IsInvalid || code != DeepFilterNativ.Ok)
        {
            _handle.Dispose();
            throw new InvalidOperationException($"dfn_create: {FehlerText(code)}.");
        }
        if (schwellen is { } s && DeepFilterNativ.SchwellenSetzen(_handle, s.MinDb, s.MaxErbDb, s.MaxDfDb) is var c && c != DeepFilterNativ.Ok)
        {
            _handle.Dispose();
            throw new ArgumentException($"dfn_set_thresholds: {FehlerText(c)}.", nameof(schwellen));
        }
        int rahmen = DeepFilterNativ.RahmenGroesse(_handle);
        int verzug = DeepFilterNativ.Verzoegerung(_handle);
        if (rahmen != RahmenSamples || verzug != VerzoegerungSamples)
        {
            _handle.Dispose();
            throw new InvalidOperationException($"deepfilternet.dll meldet {rahmen} Werte und {verzug} Verzögerung, erwartet {RahmenSamples} und {VerzoegerungSamples}.");
        }
        NativeRahmenSamples = rahmen;
        NativeVerzoegerungSamples = verzug;
    }

    /// <summary>Versionszeichenkette von deepfilternet.dll (eigene C-Schicht, DeepFilterNet- und tract-Fassung).</summary>
    public static string Version => Marshal.PtrToStringUTF8(DeepFilterNativ.VersionLesen()) ?? string.Empty;

    /// <summary>Werte je Rahmen laut DLL (geprüft beim Anlegen).</summary>
    public int NativeRahmenSamples { get; }

    /// <summary>Verzögerung laut DLL (geprüft beim Anlegen).</summary>
    public int NativeVerzoegerungSamples { get; }

    /// <summary>Anzahl verarbeiteter Rahmen (nur echte native Aufrufe mit Erfolg).</summary>
    public long VerarbeiteteRahmen => Volatile.Read(ref _rahmen);

    /// <summary>Eingangswerte, die als NaN oder ±Unendlich ankamen und durch 0 ersetzt wurden.</summary>
    public long ErsetzteWerte => Volatile.Read(ref _ersetzt);

    /// <summary>Rahmen unter <see cref="UntermalenUnter"/>, denen leises Rauschen unterlegt wurde (sonst rechnete libDF sie nicht).</summary>
    public long UntermalteRahmen => Volatile.Read(ref _untermalt);

    /// <summary>Die DLL hat einen Fehler gemeldet; der Filter rechnet nicht mehr (Rückfall auf RNNoise).</summary>
    public bool Gesperrt => _fehler != 0;

    /// <summary>Fehlercode der DLL, der den Filter gesperrt hat (0 = keiner), als Text.</summary>
    public string Sperrgrund => _fehler == 0 ? string.Empty : FehlerText(_fehler);

    /// <summary>
    /// Entrauscht genau 480 Werte (float [-1, 1]) von <paramref name="eingang"/> nach <paramref name="ausgang"/> (dürfen derselbe
    /// Puffer sein). Rückgabe falsch, wenn die DLL einen Fehler meldet oder schon gesperrt ist; <paramref name="ausgang"/> ist dann
    /// Stille.
    /// </summary>
    public bool Verarbeiten(ReadOnlySpan<float> eingang, Span<float> ausgang)
    {
        LaengenPruefung.Genau(eingang.Length, RahmenSamples, nameof(eingang));
        LaengenPruefung.Genau(ausgang.Length, RahmenSamples, nameof(ausgang));
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (_fehler != 0)
        {
            ausgang.Clear();
            return false;
        }

        Span<float> ein = _ein;
        int ersetzt = 0;
        double leistung = 0;
        for (int i = 0; i < ein.Length; i++)
        {
            float v = eingang[i];
            if (!float.IsFinite(v))
            {
                v = 0f;
                ersetzt++;
            }
            v = Math.Clamp(v, -1f, 1f);
            ein[i] = v;
            leistung += (double)v * v;
        }
        if (ersetzt != 0) _ersetzt += ersetzt;
        if (leistung < UntermalenUnter * RahmenSamples) Untermalen(ein);

        int code;
        fixed (float* e = _ein)
        fixed (float* a = _aus)
        {
            code = DeepFilterNativ.Verarbeiten(_handle, e, a, RahmenSamples, null);
        }
        if (code != DeepFilterNativ.Ok)
        {
            _fehler = code;
            ausgang.Clear();
            return false;
        }
        _rahmen++;
        _aus.AsSpan().CopyTo(ausgang);
        Endlichkeit.SubnormaleNullen(ausgang);
        return true;
    }

    /// <summary>Festes, leises Rauschen (xorshift32, gleichverteilt) unter einen zu leisen Rahmen legen; ohne Allokation.</summary>
    private void Untermalen(Span<float> x)
    {
        var z = _zufall;
        for (int i = 0; i < x.Length; i++)
        {
            z ^= z << 13;
            z ^= z >> 17;
            z ^= z << 5;
            x[i] += ((2f * z / uint.MaxValue) - 1f) * UntermalungSpitze;
        }
        _zufall = z;
        _untermalt++;
    }

    public void Dispose() => _handle.Dispose();

    private static string FehlerText(int code) => code switch
    {
        DeepFilterNativ.Ok => "kein Fehler",
        DeepFilterNativ.FehlerZeiger => "Zeiger fehlt",
        DeepFilterNativ.FehlerLaenge => "falsche Länge",
        DeepFilterNativ.FehlerWert => "ungültiger Wert",
        DeepFilterNativ.FehlerModell => "Modell nicht ladbar",
        DeepFilterNativ.FehlerRechnen => "Rechenfehler in tract",
        DeepFilterNativ.FehlerPanik => "Panik in libDF oder tract",
        DeepFilterNativ.FehlerGesperrt => "Zustand gesperrt",
        _ => "unbekannter Code " + code.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
