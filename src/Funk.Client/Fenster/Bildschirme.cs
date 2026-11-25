// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Funk.Client.Ablage;

namespace Funk.Client.Fenster;

/// <summary>Die Monitore, wie Windows sie gerade meldet. Im Betrieb <see cref="WindowsBildschirme"/>, in Tests eine feste Liste.</summary>
public interface IBildschirme
{
    IReadOnlyList<Bildschirm> Alle();
}

/// <summary>Eine feste Liste von Monitoren, für Tests (abgesteckter Monitor, andere DPI) ohne echte Anzeige.</summary>
public sealed class FesteBildschirme(IReadOnlyList<Bildschirm> alle) : IBildschirme
{
    public IReadOnlyList<Bildschirm> Alle() => alle;
}

/// <summary>
/// Liest die Monitore über EnumDisplayMonitors, GetMonitorInfo und GetDpiForMonitor, dazu das Rechteck eines eigenen
/// Fensters über GetWindowRect: alles nur lesend, kein Fenster wird bewegt, nichts vom Bildschirm kopiert. Das Programm
/// läuft mit PerMonitorV2 (app.manifest), die Rechtecke sind also physische Bildpunkte.
/// </summary>
public sealed partial class WindowsBildschirme : IBildschirme
{
    private WindowsBildschirme()
    {
    }

    public static WindowsBildschirme Instanz { get; } = new();

    public IReadOnlyList<Bildschirm> Alle()
    {
        var griffe = new List<nint>();
        var halter = GCHandle.Alloc(griffe);
        try
        {
            unsafe
            {
                _ = EnumDisplayMonitors(0, 0, &Gefunden, GCHandle.ToIntPtr(halter));
            }
        }
        finally
        {
            halter.Free();
        }
        var alle = new List<Bildschirm>();
        foreach (var g in griffe)
        {
            if (Lesen(g) is { } b) alle.Add(b);
        }
        return alle;
    }

    /// <summary>Rechteck eines Fensters in Bildpunkten, oder <c>null</c> ohne Fensterhandle.</summary>
    public static Pixelrechteck? Rahmen(Window fenster)
    {
        ArgumentNullException.ThrowIfNull(fenster);
        var h = new WindowInteropHelper(fenster).Handle;
        if (h == IntPtr.Zero || !GetWindowRect(h, out var r)) return null;
        return Rechteck(r);
    }

    /// <summary>Der Monitor, auf dem ein Fenster steht (größter Anteil), als Rohdaten; <c>null</c> ohne Fensterhandle.</summary>
    public static Bildschirm? Von(Window fenster)
    {
        ArgumentNullException.ThrowIfNull(fenster);
        return MonitorVon(new WindowInteropHelper(fenster).Handle);
    }

    /// <summary>
    /// Der Monitor, auf dem ein fremdes Fenster überwiegend steht (MonitorFromWindow, der nächste, wenn es auf keinem liegt), nur gelesen;
    /// <c>null</c> ohne Handle. Seit 0.11.1 für das Fenster des Spiels (<see cref="Spielfenster"/>).
    /// </summary>
    public static Bildschirm? MonitorVon(nint fenster) => fenster == 0 ? null : Lesen(MonitorFromWindow(fenster, MonitorDefaultToNearest));

    private static Bildschirm? Lesen(nint monitor)
    {
        if (monitor == 0) return null;
        var info = new MonitorInfo { Groesse = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) return null;
        var skala = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        return new Bildschirm(Rechteck(info.Monitor), Rechteck(info.Arbeit), skala, (info.Flags & MonitorinfofPrimary) != 0);
    }

    private static Pixelrechteck Rechteck(Rect32 r) => new(r.Links, r.Oben, r.Rechts - r.Links, r.Unten - r.Oben);

    [UnmanagedCallersOnly]
    private static int Gefunden(nint monitor, nint dc, nint rechteck, nint daten)
    {
        if (GCHandle.FromIntPtr(daten).Target is List<nint> griffe) griffe.Add(monitor);
        return 1;
    }

    private const uint MonitorinfofPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Links;
        public int Oben;
        public int Rechts;
        public int Unten;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Groesse;
        public Rect32 Monitor;
        public Rect32 Arbeit;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumDisplayMonitors(nint dc, nint ausschnitt, delegate* unmanaged<nint, nint, nint, nint, int> rueckruf, nint daten);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint fenster, uint art);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint fenster, out Rect32 rechteck);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int art, out uint dpiX, out uint dpiY);
}
