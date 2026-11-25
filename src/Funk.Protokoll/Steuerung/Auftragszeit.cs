// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;

namespace Funk.Protokoll.Steuerung;

/// <summary>
/// Zeiten eines Auftrags (6.12) in der Ortszeit der Gilde: Voreinstellung für das Ende ist der nächste Zeitpunkt 04:00 Uhr nach
/// dem Beginn, höchstens 24 h danach. Gerechnet wird in Europe/Berlin (die Gruppe spielt nach deutscher Zeit); fehlt die Zeitzonendatenbank,
/// in der Ortszeit des Rechners. Server, Konsole und Verwaltungsteil rechnen dasselbe.
/// </summary>
public static class Auftragszeit
{
    /// <summary>Stunde, zu der ein Auftrag ohne <c>bis</c> endet.</summary>
    public const int Morgenstunde = 4;

    /// <summary>Die Zeitzone der Gilde.</summary>
    public static TimeZoneInfo Zone { get; } = ZoneFinden();

    private static TimeZoneInfo ZoneFinden()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>Der nächste Zeitpunkt <see cref="Morgenstunde"/>:00 Ortszeit nach <paramref name="vonUnixMs"/>, als Unix-ms.</summary>
    public static long NaechsterMorgen(long vonUnixMs, TimeZoneInfo? zone = null)
    {
        zone ??= Zone;
        var lokal = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(vonUnixMs), zone);
        var kandidat = new DateTime(lokal.Year, lokal.Month, lokal.Day, Morgenstunde, 0, 0, DateTimeKind.Unspecified);
        if (kandidat <= lokal.DateTime) kandidat = kandidat.AddDays(1);
        return AlsUnixMs(kandidat, zone);
    }

    /// <summary>
    /// Liest eine Zeitangabe der Konsole: <c>HH:mm</c> (der nächste solche Zeitpunkt nach <paramref name="nachUnixMs"/>),
    /// <c>yyyy-MM-dd HH:mm</c> oder <c>yyyy-MM-ddTHH:mm</c> in Ortszeit. <c>null</c>, wenn nichts davon passt.
    /// </summary>
    public static long? Lesen(string text, long nachUnixMs, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        zone ??= Zone;
        var t = text.Trim();
        if (TimeOnly.TryParseExact(t, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var uhrzeit))
        {
            var lokal = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nachUnixMs), zone);
            var kandidat = lokal.Date + uhrzeit.ToTimeSpan();
            if (kandidat <= lokal.DateTime) kandidat = kandidat.AddDays(1);
            return AlsUnixMs(DateTime.SpecifyKind(kandidat, DateTimeKind.Unspecified), zone);
        }
        if (DateTime.TryParseExact(t, ["yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var datum))
            return AlsUnixMs(DateTime.SpecifyKind(datum, DateTimeKind.Unspecified), zone);
        return null;
    }

    /// <summary>"02.10. 04:00" in Ortszeit, für Journal, Konsole und Anzeige.</summary>
    public static string Anzeigen(long unixMs, TimeZoneInfo? zone = null) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(unixMs), zone ?? Zone).ToString("dd.MM. HH:mm", CultureInfo.InvariantCulture);

    private static long AlsUnixMs(DateTime lokal, TimeZoneInfo zone)
    {
        // Eine Uhrzeit in der Lücke der Sommerzeitumstellung gibt es nicht; dann gilt die Stunde danach.
        if (zone.IsInvalidTime(lokal)) lokal = lokal.AddHours(1);
        return new DateTimeOffset(lokal, zone.GetUtcOffset(lokal)).ToUnixTimeMilliseconds();
    }
}
