// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Funk.Client.Ablage;

namespace Funk.Client.Messung;

/// <summary>
/// Was <c>TacRelay.exe --funkfeld-messen &lt;bericht.json&gt; [--ansicht raster|liste] [--felder 5|10] [--sekunden 3]
/// --sichtbar-freigegeben</c> tun soll (A32): nur das Funkfeld mit der Vorführungsszene öffnen und festhalten, wie groß es
/// am echten Fenster wirklich wird. Das Fenster ist sichtbar; deshalb verweigert das Programm den Start ohne
/// <see cref="Freigabe"/> (die gibt Sarevoc im Gespräch), wenn ein Spiel läuft (<see cref="Spielsperre"/>) und wenn der Bericht
/// nicht unter <see cref="Zone"/> liegt. Bild und Einstellungen kommen neben den Bericht.
/// </summary>
public sealed record FunkfeldMessauftrag(string Bericht, FunkfeldDarstellung Darstellung, int Felder, int Sekunden)
{
    public const string Schalter = "--funkfeld-messen";
    public const string Freigabe = "--sichtbar-freigegeben";

    /// <summary>Nur hier darf der Bericht liegen (C:\Claude\CLAUDE.md: geschrieben wird nur in der Zone).</summary>
    public const string Zone = @"C:\Claude\";

    public const int StandardSekunden = 3;
    public const int HoechstensSekunden = 30;

    /// <summary>Exitcodes, wie tests\oberflaeche-aufnehmen.ps1: 2 Argumente, 3 Fehler im Lauf, 4 Spiel, 5 ohne Freigabe, 6 außerhalb der Zone.</summary>
    public const int CodeArgumente = 2;
    public const int CodeFehler = 3;
    public const int CodeSpiel = 4;
    public const int CodeNichtFreigegeben = 5;
    public const int CodeAusserhalbZone = 6;

    private static readonly string[] Mitwert = ["--ansicht", "--felder", "--sekunden", Schalter];
    private static readonly string[] Ohnewert = [Freigabe, "--vorfuehrung"];

    /// <summary>Das Bild per PrintWindow, neben dem Bericht.</summary>
    public string Bild => Path.Combine(Path.GetDirectoryName(Bericht)!, Path.GetFileNameWithoutExtension(Bericht) + ".png");

    /// <summary>Die Wegwerf-Einstellungen des Laufs, neben dem Bericht; das Profil und TACRELAY_EINSTELLUNGEN bleiben unberührt.</summary>
    public string Einstellungen => Path.Combine(Path.GetDirectoryName(Bericht)!, Path.GetFileNameWithoutExtension(Bericht) + "-einstellungen.json");

    /// <summary>
    /// Liest die Schalter. Geprüft in dieser Folge: Argumente (Code 2), Freigabe (5), Ablageort unter <see cref="Zone"/> (6).
    /// Ob ein Spiel läuft, prüft der Aufrufer danach (<see cref="Spielsperre"/>, Code 4). <c>Bericht</c> ist der geprüfte
    /// Pfad, sobald er in der Zone liegt, auch bei einer Ablehnung (dorthin darf sie geschrieben werden), sonst <c>null</c>.
    /// </summary>
    public static (FunkfeldMessauftrag? Auftrag, int Code, string? Grund, string? Bericht) Lesen(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var werte = new Dictionary<string, string>(StringComparer.Ordinal);
        var schalter = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (Ohnewert.Contains(a))
            {
                schalter.Add(a);
                continue;
            }
            if (!Mitwert.Contains(a)) return (null, CodeArgumente, $"Unbekanntes Argument \"{a}\".", null);
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal)) return (null, CodeArgumente, a + " braucht einen Wert.", null);
            if (!werte.TryAdd(a, args[++i])) return (null, CodeArgumente, a + " steht doppelt.", null);
        }
        if (!werte.TryGetValue(Schalter, out var pfad)) return (null, CodeArgumente, Schalter + " braucht den Pfad des Berichts.", null);
        var darstellung = werte.GetValueOrDefault("--ansicht", "raster").ToUpperInvariant() switch
        {
            "RASTER" => (FunkfeldDarstellung?)FunkfeldDarstellung.Raster,
            "LISTE" => FunkfeldDarstellung.Liste,
            _ => null,
        };
        if (darstellung is null) return (null, CodeArgumente, "--ansicht raster oder liste.", null);
        var felder = werte.GetValueOrDefault("--felder", "10") switch
        {
            "5" => 5,
            "10" => 10,
            _ => 0,
        };
        if (felder == 0) return (null, CodeArgumente, "--felder 5 oder 10.", null);
        if (!int.TryParse(werte.GetValueOrDefault("--sekunden", StandardSekunden.ToString(CultureInfo.InvariantCulture)), NumberStyles.None,
                CultureInfo.InvariantCulture, out var sekunden) || sekunden is < 1 or > HoechstensSekunden)
            return (null, CodeArgumente, $"--sekunden 1 bis {HoechstensSekunden}.", null);

        var bericht = InDerZone(pfad);
        if (!schalter.Contains(Freigabe))
            return (null, CodeNichtFreigegeben, "Die Messung öffnet ein sichtbares Fenster; nur mit " + Freigabe + ", und den gibt Sarevoc im Gespräch.", bericht);
        if (bericht is null) return (null, CodeAusserhalbZone, @"Der Bericht muss eine .json-Datei unter C:\Claude sein: " + pfad, null);
        return (new FunkfeldMessauftrag(bericht, darstellung.Value, felder, sekunden), 0, null, bericht);
    }

    /// <summary>Der volle Pfad, wenn er eine .json-Datei unter <see cref="Zone"/> ist, sonst <c>null</c>.</summary>
    public static string? InDerZone(string? pfad)
    {
        if (string.IsNullOrWhiteSpace(pfad) || !Path.IsPathFullyQualified(pfad)) return null;
        string voll;
        try
        {
            voll = Path.GetFullPath(pfad);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        return voll.StartsWith(Zone, StringComparison.OrdinalIgnoreCase) && voll.Length > Zone.Length
            && string.Equals(Path.GetExtension(voll), ".json", StringComparison.OrdinalIgnoreCase)
            ? voll
            : null;
    }
}

/// <summary>
/// Kein sichtbares Fenster neben einem Spiel (C:\Claude\CLAUDE.md, Abschnitt Bildschirm), nachgebaut nach
/// tests\Funk.Tests\Spielschutz.cs: Ein Spiel erkennt die Prüfung am Prozessnamen (auch am Launcher) und am
/// Benachrichtigungszustand von Windows (Vollbild, Direct3D-Vollbild, Präsentationsmodus); randlose Fenstermodi meldet
/// Windows dort nicht sicher, deshalb beides. Nur lesend.
/// </summary>
public static partial class Spielsperre
{
    /// <summary>Anfang der Prozessnamen, bei denen nichts gestartet wird (StarCitizen, StarCitizen_Launcher).</summary>
    public static readonly IReadOnlyList<string> Spiele = ["StarCitizen"];

    /// <summary>Warum nichts geöffnet werden darf, oder <c>null</c>; reine Rechnung über Prozessnamen und Zustand (QUNS).</summary>
    public static string? Grund(IEnumerable<string> prozesse, int? benachrichtigung)
    {
        ArgumentNullException.ThrowIfNull(prozesse);
        foreach (var name in prozesse)
        {
            if (Spiele.Any(s => name.StartsWith(s, StringComparison.OrdinalIgnoreCase))) return name + " läuft";
        }
        // QUERY_USER_NOTIFICATION_STATE: 2 QUNS_BUSY, 3 QUNS_RUNNING_D3D_FULL_SCREEN, 4 QUNS_PRESENTATION_MODE.
        return benachrichtigung is 2 or 3 or 4 ? "eine Vollbildanwendung läuft (Benachrichtigungszustand " + benachrichtigung.Value.ToString(CultureInfo.InvariantCulture) + ")" : null;
    }

    /// <summary>Der Grund jetzt, aus der Prozessliste und dem Benachrichtigungszustand.</summary>
    public static string? Jetzt()
    {
        var namen = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    namen.Add(p.ProcessName);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        return Grund(namen, SHQueryUserNotificationState(out var zustand) == 0 ? zustand : null);
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int zustand);
}
