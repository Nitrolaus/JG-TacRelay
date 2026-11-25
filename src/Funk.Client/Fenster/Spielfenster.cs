// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Runtime.InteropServices;
using Funk.Client.Ablage;

namespace Funk.Client.Fenster;

/// <summary>
/// Auf welchem Monitor das Spiel läuft, für den Platz der Meldezeile ab Werk (seit 0.11.1, Sarevoc 12.10.2026). Nur lesend, ohne
/// Prozess-Handle und ohne Aktivieren: Die Prozesse mit dem Namen des Spiels (genau, nicht der Launcher; ab Werk <see cref="Prozess"/>, seit
/// 19.10.2026 unter Einstellungen > Sprecheranzeige > Erweitert wählbar, <see cref="Spielprofil"/>) kommen aus der
/// Prozessliste von Windows, ihre sichtbaren Fenster aus EnumWindows mit GetWindowThreadProcessId; das größte davon
/// (<see cref="Groesstes"/>) bestimmt über MonitorFromWindow den Monitor. Läuft kein Spiel oder hat es kein sichtbares Fenster, gibt es
/// keinen (<c>null</c>), und die Meldezeile nimmt den Hauptmonitor. Der Bildschirm wird dabei weder kopiert noch ein Fenster bewegt
/// (C:\Claude\CLAUDE.md, Abschnitt Bildschirm). Seit 0.11.9 für die Sprecheranzeige auch über das Vordergrundfenster
/// (<see cref="MonitorImVordergrund"/>) und das exklusive Vollbild (<see cref="ExklusivesVollbild"/>).
/// </summary>
public static partial class Spielfenster
{
    /// <summary>Der Prozessname des Spiels ab Werk, genau (StarCitizen_Launcher zählt nicht).</summary>
    public const string Prozess = Spielprofil.StarCitizenProzess;

    /// <summary>Der Monitor von Star Citizen jetzt (ab Werk), oder <c>null</c>.</summary>
    public static Bildschirm? Monitor() => Monitor(Prozess);

    /// <summary>Der Monitor des Spiels mit dem Prozessnamen <paramref name="prozess"/> jetzt, oder <c>null</c> ohne laufendes Spiel mit sichtbarem Fenster.</summary>
    public static Bildschirm? Monitor(string prozess)
    {
        var pids = Prozesse(prozess);
        if (pids.Count == 0) return null;
        var fenster = Fenster();
        var h = Groesstes(fenster, pids);
        return h == 0 ? null : WindowsBildschirme.MonitorVon(h);
    }

    /// <summary>
    /// Der Monitor des Spiels für die Sprecheranzeige (seit 0.11.9, Vorgabe vom 15.10.2026): das Vordergrundfenster (GetForegroundWindow), seine
    /// Prozessnummer (GetWindowThreadProcessId) gegen die Prozesse mit dem Namen <paramref name="prozess"/>, dann MonitorFromWindow. Ist nicht das
    /// Spiel vorn, <c>null</c>; das Fenster nimmt dann den zuletzt bekannten, sonst den Hauptmonitor. Nur gelesen: kein Prozess-Handle, nichts
    /// aktiviert, kein Fenster bewegt.
    /// </summary>
    public static Bildschirm? MonitorImVordergrund(string prozess)
    {
        var vorn = GetForegroundWindow();
        if (vorn == 0) return null;
        _ = GetWindowThreadProcessId(vorn, out var pid);
        return ImVordergrund(pid, Prozesse(prozess)) ? WindowsBildschirme.MonitorVon(vorn) : null;
    }

    /// <summary>Wie <see cref="MonitorImVordergrund(string)"/> für Star Citizen (ab Werk).</summary>
    public static Bildschirm? MonitorImVordergrund() => MonitorImVordergrund(Prozess);

    /// <summary>Gehört das Vordergrundfenster (Prozessnummer <paramref name="pidVorn"/>) dem Spiel? Reine Rechnung, ohne Spiel prüfbar.</summary>
    public static bool ImVordergrund(uint pidVorn, IReadOnlySet<uint> pids)
    {
        ArgumentNullException.ThrowIfNull(pids);
        return pidVorn != 0 && pids.Contains(pidVorn);
    }

    /// <summary>QUNS_RUNNING_D3D_FULL_SCREEN: Ein Programm läuft im exklusiven Direct3D-Vollbild.</summary>
    public const int ExklusivesVollbildZustand = 3;

    /// <summary>
    /// Läuft gerade etwas im exklusiven Vollbild (SHQueryUserNotificationState meldet <see cref="ExklusivesVollbildZustand"/>, seit 0.11.9)?
    /// Dann zeigt Windows kein anderes Fenster darüber, auch die Sprecheranzeige nicht; Randlos geht. Nur gelesen; scheitert die Abfrage, nein.
    /// </summary>
    public static bool ExklusivesVollbild() => IstExklusivesVollbild(SHQueryUserNotificationState(out var zustand) == 0 ? zustand : null);

    /// <summary>Der Benachrichtigungszustand <paramref name="zustand"/> ist das exklusive Vollbild; reine Rechnung.</summary>
    public static bool IstExklusivesVollbild(int? zustand) => zustand == ExklusivesVollbildZustand;

    /// <summary>
    /// Das größte (nach Fläche in Bildpunkten) sichtbare Fenster eines Prozesses aus <paramref name="pids"/>, als Handle; 0 ohne. Reine
    /// Rechnung über einer Liste, damit die Wahl ohne Spiel prüfbar ist.
    /// </summary>
    public static nint Groesstes(IEnumerable<(nint Handle, uint Pid, Pixelrechteck Rahmen)> fenster, IReadOnlySet<uint> pids)
    {
        ArgumentNullException.ThrowIfNull(fenster);
        ArgumentNullException.ThrowIfNull(pids);
        nint bestes = 0;
        long groesste = 0;
        foreach (var (h, pid, r) in fenster)
        {
            if (!pids.Contains(pid)) continue;
            var flaeche = (long)Math.Max(0, r.Breite) * Math.Max(0, r.Hoehe);
            if (flaeche <= groesste) continue;
            (groesste, bestes) = (flaeche, h);
        }
        return bestes;
    }

    /// <summary>
    /// Die Prozessnummern aller Prozesse mit dem Namen <paramref name="prozess"/>; Windows liefert sie aus der Prozessliste, ohne Handle. Lässt sich
    /// die Liste nicht lesen (NtQuerySystemInformation, selten), ist das Ergebnis leer, und die Meldezeile nimmt den Hauptmonitor; ein Fehler
    /// einer Komfortfunktion darf den Client nicht beenden (Befund der Prüfung 0.11.1: der Fang saß um <c>Id</c>, das aus dem Schnappschuss nie
    /// wirft, nicht um den Aufruf).
    /// </summary>
    private static HashSet<uint> Prozesse(string prozess)
    {
        var pids = new HashSet<uint>();
        if (string.IsNullOrWhiteSpace(prozess)) return pids;
        Process[] prozesse;
        try
        {
            prozesse = Process.GetProcessesByName(prozess);
        }
        catch (InvalidOperationException)
        {
            return pids;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return pids;
        }
        foreach (var p in prozesse)
        {
            using (p) pids.Add((uint)p.Id);
        }
        return pids;
    }

    /// <summary>Alle sichtbaren Hauptfenster mit Prozess und Rechteck in Bildpunkten (EnumWindows, nur gelesen).</summary>
    private static List<(nint Handle, uint Pid, Pixelrechteck Rahmen)> Fenster()
    {
        var liste = new List<(nint, uint, Pixelrechteck)>();
        var halter = GCHandle.Alloc(liste);
        try
        {
            unsafe
            {
                _ = EnumWindows(&Gefunden, GCHandle.ToIntPtr(halter));
            }
        }
        finally
        {
            halter.Free();
        }
        return liste;
    }

    [UnmanagedCallersOnly]
    private static int Gefunden(nint fenster, nint daten)
    {
        if (GCHandle.FromIntPtr(daten).Target is not List<(nint, uint, Pixelrechteck)> liste) return 0;
        if (!IsWindowVisible(fenster) || !GetWindowRect(fenster, out var r)) return 1;
        _ = GetWindowThreadProcessId(fenster, out var pid);
        liste.Add((fenster, pid, new Pixelrechteck(r.Links, r.Oben, r.Rechts - r.Links, r.Unten - r.Oben)));
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Links;
        public int Oben;
        public int Rechts;
        public int Unten;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> rueckruf, nint daten);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint fenster);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint fenster, out uint pid);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint fenster, out Rect32 rechteck);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int zustand);
}
