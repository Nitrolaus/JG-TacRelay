// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;

namespace Funk.Server.Betrieb;

/// <summary>
/// Die drei Zeitbasen des Servers. Alle Grenzen und Fristen laufen auf der monotonen Uhr (4.8); die Wanduhr
/// dient nur Zeitstempeln im Steuerkanal (<c>serverZeit</c>, <c>gueltigBis</c>).
/// <list type="bullet">
/// <item><see cref="Takte"/>: Stopwatch-Zeitstempel für den Sprachkanal (Empfangsprüfung, Schlüsselbund, Budgets).</item>
/// <item><see cref="MonoMs"/>: monotone Millisekunden für Steuerkanal, Zustandsautomat und Ratenlimits.</item>
/// <item><see cref="LageMs"/>: monotone Millisekunden, einmal beim Start an die Unix-Zeit angelegt. So verlangt es der
/// Weiterleiter (Stummschaltung.Bis und Phasenfristen auf einer Basis); ein Rücksprung der Systemuhr kommt dort nie an.</item>
/// </list>
/// </summary>
public static class Zeit
{
    private static readonly long Anlage = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - MonoMs();

    /// <summary>Stopwatch-Zeitstempel.</summary>
    public static long Takte() => Stopwatch.GetTimestamp();

    /// <summary>Monotone Millisekunden seit einem beliebigen Anfang.</summary>
    public static long MonoMs() => MsAusTakten(Stopwatch.GetTimestamp());

    /// <summary>Monotone Millisekunden, angelegt an die Unix-Zeit beim Prozessstart.</summary>
    public static long LageMs() => Anlage + MonoMs();

    /// <summary>Unix-Millisekunden der Wanduhr.</summary>
    public static long UnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Rechnet Stopwatch-Takte in Millisekunden um, ohne Überlauf.</summary>
    public static long MsAusTakten(long takte) =>
        takte / Stopwatch.Frequency * 1000 + takte % Stopwatch.Frequency * 1000 / Stopwatch.Frequency;

    /// <summary>Rechnet Millisekunden in Stopwatch-Takte um, ohne Überlauf.</summary>
    public static long TakteAusMs(long ms) =>
        ms / 1000 * Stopwatch.Frequency + ms % 1000 * Stopwatch.Frequency / 1000;
}
