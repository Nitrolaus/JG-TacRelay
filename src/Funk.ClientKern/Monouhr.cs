// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;

namespace Funk.ClientKern;

/// <summary>Monotone Uhr des Clients (Stopwatch), in Takten und Millisekunden.</summary>
public static class Monouhr
{
    /// <summary>Zeitpunkt "noch nie": weit in der Vergangenheit, aber ohne Überlauf bei <c>jetzt - Nie</c>.</summary>
    public const long Nie = long.MinValue / 4;

    /// <summary>Aktueller Zeitstempel in Stopwatch-Takten.</summary>
    public static long Takte() => Stopwatch.GetTimestamp();

    /// <summary>Monotone Millisekunden seit einem beliebigen Anfang.</summary>
    public static long Ms() => MsAus(Stopwatch.GetTimestamp());

    /// <summary>Takte in ganze Millisekunden.</summary>
    public static long MsAus(long takte) => (long)((Int128)takte * 1000 / Stopwatch.Frequency);

    /// <summary>Takte in Millisekunden mit Nachkommastellen.</summary>
    public static double MsGenau(long takte) => takte * 1000.0 / Stopwatch.Frequency;

    /// <summary>Millisekunden in Takte.</summary>
    public static long TakteAus(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);
}
