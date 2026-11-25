// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Runtime.InteropServices;

namespace Funk.Server.Betrieb;

/// <summary>Was der Kernel über die Uhr sagt (adjtimex): Zustand (5 = TIME_ERROR) und höchste Abweichung in Mikrosekunden.</summary>
public readonly record struct Kernuhr(int Zustand, long MaxFehlerUs);

/// <summary>
/// Uhrprüfung beim Start und bei <c>pruefen</c> (Gegenprüfung Stufe 1): TOTP verträgt nur einen Zeitschritt (30 s) Abweichung,
/// mit falscher Uhr passt kein Code der Admins. Im unprivilegierten LXC läuft keine eigene Zeitsynchronisierung; die Uhr ist
/// die des Proxmox-Hosts. Was sich prüfen lässt:
/// <list type="bullet">
/// <item>Der Kernel (geteilt mit dem Host) meldet über <c>adjtimex</c> (nur lesen, <c>modes = 0</c>), ob ein Zeitdienst die Uhr
/// führt; <c>TIME_ERROR</c> heißt nicht synchronisiert. Unter systemd mit <c>ProtectClock=true</c> sperrt der Systemaufruffilter
/// das auch zum Lesen (EPERM); dann bleibt es unbekannt, und <c>funk-server pruefen</c> aus der Shell zeigt es.</item>
/// <item>Offensichtlich falsch: eine Uhr vor dem Erscheinen dieser Fassung, oder ein Journaleintrag, der mehr als 5 Minuten
/// in der Zukunft liegt (die Uhr ging einmal vor oder geht jetzt nach).</item>
/// </list>
/// </summary>
public static partial class Uhrpruefung
{
    /// <summary><c>TIME_ERROR</c> aus <c>adjtimex</c>: Die Uhr ist nicht synchronisiert.</summary>
    public const int TimeError = 5;

    /// <summary>Früher kann die Uhr nicht stehen: Diese Fassung ist jünger.</summary>
    public static readonly DateTimeOffset Fruehestens = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>So weit darf der jüngste Journaleintrag voraus sein, ohne dass es auffällt.</summary>
    public static readonly TimeSpan Spielraum = TimeSpan.FromMinutes(5);

    private const string Abhilfe = "Auf dem Proxmox-Host die Zeitsynchronisierung prüfen (timedatectl, chrony); der Container nimmt die Uhr des Hosts.";

    /// <summary>Liest den Zustand der Kernuhr (nur Linux, nur lesend). <c>null</c>: anderes System oder vom Filter gesperrt.</summary>
    public static Kernuhr? KernLesen()
    {
        if (!OperatingSystem.IsLinux()) return null;
        // struct timex ist auf 64-Bit-Linux 208 Byte groß; modes (Offset 0) bleibt 0, also nur lesen. maxerror steht bei 24.
        var puffer = Marshal.AllocHGlobal(256);
        try
        {
            for (var i = 0; i < 256; i += 8) Marshal.WriteInt64(puffer, i, 0);
            var zustand = adjtimex(puffer);
            if (zustand < 0) return null;
            return new Kernuhr(zustand, Marshal.ReadInt64(puffer, 24));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(puffer);
        }
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int adjtimex(nint timex);

    /// <summary>Warnungen für Logbuch und <c>pruefen</c>; leer, wenn nichts auffällt.</summary>
    public static IReadOnlyList<string> Warnungen(DateTimeOffset jetzt, Kernuhr? kern, DateTimeOffset? juengsterVermerk)
    {
        var w = new List<string>(2);
        var zeit = jetzt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        if (jetzt < Fruehestens)
            w.Add("Die Uhr des Servers steht auf " + zeit + ", vor dem Erscheinen dieser Fassung: offensichtlich falsch. TOTP-Codes der Admins passen so nicht. " + Abhilfe);
        else if (juengsterVermerk is { } j && j - jetzt > Spielraum)
            w.Add("Die Uhr des Servers geht nach: Sie steht auf " + zeit + ", der jüngste Journaleintrag ist "
                + string.Create(CultureInfo.InvariantCulture, $"{(j - jetzt).TotalMinutes:0}") + " min jünger. TOTP-Codes der Admins passen so nicht. " + Abhilfe);
        if (kern is { Zustand: TimeError })
            w.Add("Der Kernel meldet die Uhr als nicht synchronisiert (adjtimex TIME_ERROR): Sie kann schon abweichen, TOTP verträgt nur 30 s. " + Abhilfe);
        return w;
    }

    /// <summary>Eine Zeile für <c>pruefen</c>: was über die Synchronisierung bekannt ist.</summary>
    public static string Hinweis(Kernuhr? kern) => kern switch
    {
        null => "Uhr: Synchronisierung hier nicht lesbar (anderes System oder ProtectClock); geprüft wird nur, ob die Uhr offensichtlich falsch geht.",
        { Zustand: TimeError } => "Uhr: vom Kernel als nicht synchronisiert gemeldet (siehe Warnung).",
        { } k => string.Create(CultureInfo.InvariantCulture, $"Uhr: synchronisiert (Kernel, höchstens {k.MaxFehlerUs / 1000.0:0.#} ms Abweichung)."),
    };
}
