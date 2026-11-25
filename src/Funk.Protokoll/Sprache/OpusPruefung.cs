// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Sprache;

/// <summary>Befund der Opus-Prüfung (5.13).</summary>
public enum Opusbefund : byte
{
    Gueltig = 0,
    Leer,

    /// <summary>m &gt; 400, geprüft vor dem TOC.</summary>
    ZuLang,
    Stereo,

    /// <summary>c = 3 verlangt ein zweites Byte mit der Frame-Anzahl.</summary>
    AnzahlFehlt,

    /// <summary>Dauer mal Anzahl ist nicht genau 20 ms.</summary>
    FalscheDauer,
}

/// <summary>
/// Serverprüfung der Opus-Nutzlast ohne libopus, nur aus dem TOC-Byte (5.13, RFC 6716 Abschnitt 3.1).
/// Angenommen wird nur, was genau 20 ms mono ergibt. Der Client prüft zusätzlich mit libopus.
/// </summary>
public static class OpusPruefung
{
    /// <summary>20 ms bei 48 kHz.</summary>
    public const int SamplesJeFrame = 960;

    public static Opusbefund Pruefen(ReadOnlySpan<byte> opus)
    {
        if (opus.Length > Sprachkanal.OpusMax) return Opusbefund.ZuLang;
        if (opus.IsEmpty) return Opusbefund.Leer;

        var toc = opus[0];
        var config = toc >> 3;
        var stereo = (toc >> 2) & 1;
        var c = toc & 3;
        if (stereo != 0) return Opusbefund.Stereo;

        int anzahl;
        if (c == 0) anzahl = 1;
        else if (c is 1 or 2) anzahl = 2;
        else if (opus.Length >= 2) anzahl = opus[1] & 0x3F;
        else return Opusbefund.AnzahlFehlt;

        return FrameSamples(config) * anzahl == SamplesJeFrame ? Opusbefund.Gueltig : Opusbefund.FalscheDauer;
    }

    /// <summary>Framedauer je config in Samples bei 48 kHz: SILK 10/20/40/60 ms, Hybrid 10/20 ms, CELT 2,5/5/10/20 ms.</summary>
    public static int FrameSamples(int config) => config switch
    {
        < 0 or > 31 => throw new ArgumentOutOfRangeException(nameof(config)),
        < 12 => (config & 3) switch { 0 => 480, 1 => 960, 2 => 1920, _ => 2880 },
        < 16 => (config & 1) == 0 ? 480 : 960,
        _ => (config & 3) switch { 0 => 120, 1 => 240, 2 => 480, _ => 960 },
    };
}
