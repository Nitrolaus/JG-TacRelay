// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>Welcher Signalton des Meldesystems (seit 0.12.0).</summary>
public enum Signaltonart
{
    /// <summary>Ein Befehl der Leitung kam an: zwei kurze Töne, tief und höher.</summary>
    Befehl,

    /// <summary>Eine Sekunde der letzten zehn eines Countdowns: ein kurzer Ton.</summary>
    Sekunde,
}

/// <summary>
/// Signalton des Meldesystems (seit 0.12.0, Sarevoc 16.10.2026, M3-M14: "kurzer Ton bei Befehlen und in den letzten 10 s des Countdowns,
/// abschaltbar"), zur Laufzeit erzeugt und lokal gemischt wie der <see cref="FunkKlick"/>: keine Tondatei, nichts geht durch den Codec
/// oder hinaus. Ein Sinus mit weichem Anstieg und Abfall (je <see cref="FlankeMs"/>, Sinusflanke), damit nichts knackt:
/// <list type="bullet">
/// <item>Befehl: <see cref="BefehlTiefHz"/> und gleich danach <see cref="BefehlHochHz"/>, je <see cref="BefehlTonMs"/>, dazwischen
/// <see cref="BefehlPauseMs"/> Stille.</item>
/// <item>Sekunde: <see cref="SekundeHz"/> für <see cref="SekundeMs"/>.</item>
/// </list>
/// Die Lautstärke liegt mit <see cref="PegelDb"/> unter der Sprache (Bezug wie beim Klick, <see cref="FunkKlickEinstellungen.SprachpegelDbfs"/>,
/// als Effektivwert), also deutlich hörbar, aber nicht lauter als ein Funkspruch; die Funk-Lautstärke wirkt mit. Die Klänge entstehen im
/// Konstruktor; <see cref="Mischen"/> allokiert nichts und gehört einem Audio-Thread, <see cref="Anfordern"/> ist aus jedem Thread erlaubt. Ein
/// neuer Ton ersetzt einen laufenden.
/// </summary>
public sealed class Signalton
{
    /// <summary>Unter der Sprache (Effektivwert), ein Sinus hat 3 dB mehr Spitze.</summary>
    public const float PegelDb = -6f;

    public const float BefehlTiefHz = 660f;

    public const float BefehlHochHz = 880f;

    public const float BefehlTonMs = 90f;

    public const float BefehlPauseMs = 40f;

    public const float SekundeHz = 1000f;

    public const float SekundeMs = 60f;

    /// <summary>Anstieg und Abfall jedes Tons.</summary>
    public const float FlankeMs = 8f;

    private readonly float[] _befehl;
    private readonly float[] _sekunde;
    private int _anfrage = -1;
    private float[]? _laufend;
    private int _pos;
    private long _gespielt;

    public Signalton()
    {
        var spitze = Pegel.DbZuLinear(FunkKlickEinstellungen.SprachpegelDbfs + PegelDb) * MathF.Sqrt(2f);
        var ton = Samples(BefehlTonMs);
        var pause = Samples(BefehlPauseMs);
        _befehl = new float[(2 * ton) + pause];
        Sinus(_befehl.AsSpan(0, ton), BefehlTiefHz, spitze);
        Sinus(_befehl.AsSpan(ton + pause, ton), BefehlHochHz, spitze);
        _sekunde = new float[Samples(SekundeMs)];
        Sinus(_sekunde, SekundeHz, spitze);
    }

    /// <summary>Wie viele Töne der Mischer begonnen hat (Tests, Diagnose).</summary>
    public long Gespielt => Interlocked.Read(ref _gespielt);

    /// <summary>Länge eines Tons in Samples (48 kHz), samt Pause beim Befehl.</summary>
    public static int Laenge(Signaltonart art) => art == Signaltonart.Befehl ? (2 * Samples(BefehlTonMs)) + Samples(BefehlPauseMs) : Samples(SekundeMs);

    /// <summary>Aus jedem Thread: den Ton beim nächsten Mischen beginnen (ein laufender bricht ab). Immer wahr.</summary>
    public bool Anfordern(Signaltonart art)
    {
        Interlocked.Exchange(ref _anfrage, (int)art);
        return true;
    }

    /// <summary>
    /// Schreibt einen Ton bei voller Lautstärke nach <paramref name="ziel"/> (Tests: Länge, Spitze, weiche Flanken); liefert die Länge.
    /// </summary>
    public static int Erzeugen(Signaltonart art, Span<float> ziel)
    {
        var t = new Signalton();
        var quelle = art == Signaltonart.Befehl ? t._befehl : t._sekunde;
        if (ziel.Length < quelle.Length) throw new ArgumentException("Ziel zu kurz für den Ton.", nameof(ziel));
        quelle.CopyTo(ziel);
        return quelle.Length;
    }

    /// <summary>Nur im Audio-Thread: den laufenden Ton auf die Kanäle addieren (mittig). <paramref name="rechts"/> leer für Mono.</summary>
    public void Mischen(Span<float> links, Span<float> rechts)
    {
        var anfrage = Interlocked.Exchange(ref _anfrage, -1);
        if (anfrage >= 0)
        {
            _laufend = anfrage == (int)Signaltonart.Befehl ? _befehl : _sekunde;
            _pos = 0;
            Interlocked.Increment(ref _gespielt);
        }
        if (_laufend is not { } klang) return;
        var mono = rechts.IsEmpty;
        // Mittig wie der Klick: je Kanal -3 dB, in Mono die volle Höhe.
        var g = mono ? 1f : 0.70710677f;
        var n = Math.Min(links.Length, klang.Length - _pos);
        for (var i = 0; i < n; i++)
        {
            var v = klang[_pos + i] * g;
            links[i] += v;
            if (!mono) rechts[i] += v;
        }
        _pos += n;
        if (_pos >= klang.Length) _laufend = null;
    }

    private static int Samples(float ms) => (int)MathF.Round(ms * Klangformat.SamplesJeMs);

    private static void Sinus(Span<float> ziel, float hz, float spitze)
    {
        var flanke = Math.Max(1, Samples(FlankeMs));
        for (var i = 0; i < ziel.Length; i++)
        {
            var huelle = i < flanke ? Math.Sin(Math.PI / 2 * i / flanke)
                : ziel.Length - 1 - i < flanke ? Math.Sin(Math.PI / 2 * (ziel.Length - 1 - i) / flanke)
                : 1.0;
            ziel[i] = (float)(spitze * huelle * Math.Sin(2 * Math.PI * hz * i / Klangformat.Abtastrate));
        }
    }
}
