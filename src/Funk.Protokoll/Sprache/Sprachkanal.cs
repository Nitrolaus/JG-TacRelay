// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;

namespace Funk.Protokoll.Sprache;

/// <summary>Feste Größen und Grenzen des Sprachkanals (Kapitel 5).</summary>
public static class Sprachkanal
{
    /// <summary>Protokollversion im Kopf (5.2).</summary>
    public const byte Version = 0x01;

    public const int KopfLaenge = 16;
    public const int TagLaenge = 16;
    public const int SchluesselLaenge = 32;
    public const int IvLaenge = 12;

    /// <summary>Kopf und Tag zusammen: so viel ist ein Datagramm länger als sein Klartext.</summary>
    public const int Ueberhang = KopfLaenge + TagLaenge;

    /// <summary>Schritt 1 der Empfangsprüfung (5.9): kürzer oder länger wird verworfen.</summary>
    public const int MinDatagramm = 41;
    public const int MaxDatagramm = 1200;

    /// <summary>Längster Klartext, der Schritt 1 bestehen kann; so groß muss der Klartextpuffer der Empfangsprüfung sein.</summary>
    public const int MaxKlartext = MaxDatagramm - Ueberhang;

    /// <summary>Größe des Empfangspuffers (5.9 Schritt 1).</summary>
    public const int EmpfangspufferLaenge = 2048;

    /// <summary>Die Typen 0x01-0x05 sind immer genau so groß (5.3).</summary>
    public const int FestesDatagramm = 48;
    public const int FesterKlartext = FestesDatagramm - Ueberhang;

    public const int HochMin = 41;
    public const int HochMax = 569;
    public const int RunterMin = 45;
    public const int RunterMax = 477;

    /// <summary>Höchstlänge der Opus-Nutzlast (5.1, 5.4, 5.13).</summary>
    public const int OpusMax = 400;

    public const int NetzZieleMax = 16;
    public const int DurchsageZieleMax = 64;
    public const int RunterNetzeMax = 16;

    /// <summary>Netz-ID von Global (Begriffe, A23).</summary>
    public const ushort GlobalNetz = 1;

    /// <summary>Breite W des Replay-Fensters (5.9, 8.7).</summary>
    public const int Fensterbreite = 2048;

    /// <summary>Schritt 0 der Empfangsprüfung: Sprache, die länger gelegen hat, wird verworfen.</summary>
    public static readonly TimeSpan MaxEmpfangsalter = TimeSpan.FromMilliseconds(60);
}

/// <summary>Umrechnung von Dauern in Stopwatch-Zeitstempel. Alle Zeiten im Sprachkanal sind monoton.</summary>
internal static class Uhr
{
    public static long Ticks(TimeSpan dauer) =>
        (long)((Int128)dauer.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
}
