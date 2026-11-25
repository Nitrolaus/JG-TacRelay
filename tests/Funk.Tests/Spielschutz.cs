// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Funk.Tests;

/// <summary>
/// Sperre für Tests, die echte Geräte, Eingabegeräte oder Fenster anfassen (C:\Claude\CLAUDE.md, Abschnitt Bildschirm):
/// Sie laufen nur mit der ausdrücklichen Freigabe <c>FUNK_RAUCHTEST=1</c> und nur, solange kein Spiel und keine andere
/// Vollbildanwendung läuft. Ein Spiel erkennt die Prüfung am Prozessnamen (<see cref="Spiele"/>, auch am Launcher) und
/// am Benachrichtigungszustand von Windows (Vollbild, Direct3D-Vollbild, Präsentationsmodus). Randlose Fenstermodi
/// meldet Windows dort nicht sicher, deshalb beides. Nur lesend.
/// </summary>
internal static partial class Spielschutz
{
    /// <summary>Anfang der Prozessnamen, bei denen nichts gestartet wird (StarCitizen, StarCitizen_Launcher).</summary>
    public static readonly string[] Spiele = ["StarCitizen"];

    // QUERY_USER_NOTIFICATION_STATE: QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN, QUNS_PRESENTATION_MODE.
    private const int QunsVollbild = 2;
    private const int QunsD3dVollbild = 3;
    private const int QunsPraesentation = 4;

    /// <summary>Wie oft <see cref="WartenAsync"/> nachsieht, ob inzwischen ein Spiel läuft.</summary>
    public static readonly TimeSpan Pruefabstand = TimeSpan.FromMilliseconds(250);

    /// <summary>Warum gerade nichts gestartet werden darf (Spiel oder Vollbild), oder null. <paramref name="spiele"/>: nur für die Selbstprüfung.</summary>
    public static string? Grund(IReadOnlyList<string>? spiele = null)
    {
        spiele ??= Spiele;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                string name;
                try
                {
                    name = p.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                foreach (var spiel in spiele)
                {
                    if (name.StartsWith(spiel, StringComparison.OrdinalIgnoreCase)) return name + " läuft";
                }
            }
        }
        if (SHQueryUserNotificationState(out var zustand) == 0 && zustand is QunsVollbild or QunsD3dVollbild or QunsPraesentation)
            return "eine Vollbildanwendung läuft (Benachrichtigungszustand " + zustand + ")";
        return null;
    }

    /// <summary>
    /// Wahr, wenn <paramref name="was"/> laufen darf: <c>FUNK_RAUCHTEST=1</c> und kein Spiel, keine Vollbildanwendung.
    /// Sonst schreibt es "übersprungen" mit dem Grund und liefert falsch.
    /// </summary>
    public static bool RauchtestFreigegeben(string was)
    {
        if (Environment.GetEnvironmentVariable("FUNK_RAUCHTEST") != "1")
        {
            Console.WriteLine($"HINWEIS {was} übersprungen (FUNK_RAUCHTEST=1 setzen).");
            return false;
        }
        if (Grund() is { } grund)
        {
            Console.WriteLine($"HINWEIS {was} übersprungen: {grund}.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Wartet, bis <paramref name="fertig"/> wahr ist oder <paramref name="hoechstens"/> vergangen ist, und sieht dabei
    /// alle <see cref="Pruefabstand"/> nach, ob inzwischen ein Spiel läuft. Falsch heißt: Spiel erkannt, der Test gibt
    /// sofort frei (Rückkehr durch die using-Blöcke) und endet ohne Fehler.
    /// </summary>
    public static async Task<bool> WartenAsync(Func<bool> fertig, TimeSpan hoechstens, string was)
    {
        ArgumentNullException.ThrowIfNull(fertig);
        var uhr = Stopwatch.StartNew();
        var naechstePruefung = TimeSpan.Zero;
        while (!fertig() && uhr.Elapsed < hoechstens)
        {
            if (uhr.Elapsed >= naechstePruefung)
            {
                if (Grund() is { } grund)
                {
                    Console.WriteLine($"HINWEIS {was} abgebrochen: {grund}; die Geräte werden freigegeben.");
                    return false;
                }
                naechstePruefung = uhr.Elapsed + Pruefabstand;
            }
            await Task.Delay(5);
        }
        return true;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int zustand);
}
