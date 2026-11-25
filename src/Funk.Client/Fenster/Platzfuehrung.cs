// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Funk.Client.Ablage;

namespace Funk.Client.Fenster;

/// <summary>
/// Setzt Lage und Größe eines Fensters aus einem <see cref="Platz"/> und merkt, was du daraus machst (A32, Sarevoc: "so wie
/// sich jemand ein Fenster einstellt, soll es sein, auch später").
/// <para>
/// Setzen: vor dem Zeigen mit der Skala des Zielmonitors (WPF rechnet die Lage in Einheiten mit der Skala des Fensters in
/// Bildpunkte um), danach mit der des Fensters. Nach dem ersten Zeichnen sieht <see cref="Nachfuehren"/> nach, ob das
/// Fenster wirklich dort steht, und rückt es sonst einmal zurecht: Auf Monitoren mit verschiedener DPI setzt WPF ein neues
/// Fenster womöglich mit der Skala des Hauptmonitors, bevor es auf seinen Monitor kommt.
/// </para>
/// <para>
/// Merken: nur, was du selbst tust, und je Handgriff einmal, nicht bei jedem Bildpunkt. Windows klammert jedes Ziehen am
/// Rand, jedes Verschieben an der Titelzeile (auch DragMove) und das Verschieben per Tastatur in WM_ENTERSIZEMOVE und
/// WM_EXITSIZEMOVE; gemerkt wird am Ende, wenn sich Lage oder Größe geändert haben (<see cref="BewegungEndet"/>). Beim
/// Hauptfenster zählt dazu Maximieren und Wiederherstellen. Was Windows selbst tut (ein Monitor fällt weg, das Fenster
/// rückt auf einen anderen) und was das Programm setzt, wird nicht gemerkt: Kommt der Monitor wieder, steht das Fenster
/// beim nächsten Start wieder dort, wo du es hattest. Geschrieben wird die Datei gebündelt von der Einstellungsablage.
/// </para>
/// </summary>
internal sealed partial class Platzfuehrung
{
    private const int WmEntersizemove = 0x0231;
    private const int WmExitsizemove = 0x0232;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNoownerzorder = 0x0200;

    private readonly Window _fenster;
    private readonly Action<FensterOrt, bool> _merken;
    private FensterOrt? _vorher;
    private bool _setzt;
    private bool _nachgesehen;

    /// <param name="merken">
    /// Merkt einen Platz; wahr, wenn du die Größe am Rand gezogen hast (sonst wurde nur verschoben, maximiert oder
    /// wiederhergestellt: Dabei bleibt die Größe des Normalzustands, wie sie war).
    /// </param>
    /// <param name="mitZustand">Auch Maximieren und Wiederherstellen merken (Hauptfenster).</param>
    public Platzfuehrung(Window fenster, Action<FensterOrt, bool> merken, bool mitZustand)
    {
        ArgumentNullException.ThrowIfNull(fenster);
        ArgumentNullException.ThrowIfNull(merken);
        _fenster = fenster;
        _merken = merken;
        fenster.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(fenster).Handle)?.AddHook(Nachricht);
        fenster.ContentRendered += (_, _) => Nachfuehren();
        if (mitZustand) fenster.StateChanged += (_, _) => ZustandGewechselt();
    }

    /// <summary>Der zuletzt gesetzte Platz, oder <c>null</c>.</summary>
    public Platz? Gesetzt { get; private set; }

    /// <summary>Wo das Fenster nach dem ersten Zeichnen tatsächlich stand, in Bildpunkten (Messwerkzeug).</summary>
    public Pixelrechteck? BeimZeigen { get; private set; }

    /// <summary>Das Fenster stand nach dem ersten Zeichnen nicht am Platz und wurde zurechtgerückt (Messwerkzeug).</summary>
    public bool Nachgefuehrt { get; private set; }

    /// <summary>Wie oft ein Platz gemerkt wurde (Tests: gebündelt, einmal je Handgriff).</summary>
    public int Gemerkt { get; private set; }

    /// <summary>Wie oft das Programm einen Platz gesetzt hat (Tests, Q93 a: ein Setzen je Schritt, nicht mehrere).</summary>
    public int Gesetzte { get; private set; }

    /// <summary>
    /// Wie viele davon in einem Schritt verlangt waren (Tests, Q93 a): mit Fensterhandle im Normalzustand ein einziges SetWindowPos für Lage und
    /// Größe zusammen.
    /// </summary>
    public int InEinemSchritt { get; private set; }

    private bool HatHandle => new WindowInteropHelper(_fenster).Handle != IntPtr.Zero;

    /// <summary>
    /// Skala, mit der WPF die Lage des Fensters umrechnet: mit Fensterhandle die des Fensters (bei PerMonitorV2 die seines
    /// Monitors), vorher die des Monitors aus dem zuletzt gesetzten Platz.
    /// </summary>
    public double Skala => HatHandle ? VisualTreeHelper.GetDpi(_fenster).DpiScaleX : Gesetzt?.Bildschirm.Skala ?? 1;

    /// <summary>
    /// Setzt Lage (Bildpunkte, umgerechnet), Größe (Einheiten) und, wenn verlangt, Maximiert. Über die Eigenschaften von WPF wird jede Zuweisung
    /// mit Fensterhandle sofort ein eigenes SetWindowPos: erst die Lage mit der alten Größe, dann die Größe. Mit <paramref name="einSchritt"/>
    /// (Q93 a, Spalte "Wer ist da": Ausweichen nach links und Zurück) geschieht beides im Normalzustand in einem SetWindowPos, ohne Zwischenstand
    /// auf dem Bildschirm (<see cref="Umsetzen"/>). Maximiert oder minimiert gehen Lage und Größe über die Eigenschaften in den Platz des
    /// Normalzustands (WPF schreibt sie mit SetWindowPlacement in die Wiederherstellungsgrenzen); zu sehen ist davon nichts.
    /// </summary>
    public void Setzen(Platz platz, bool einSchritt = false)
    {
        ArgumentNullException.ThrowIfNull(platz);
        Gesetzte++;
        if (einSchritt) InEinemSchritt++;
        _setzt = true;
        try
        {
            var griff = new WindowInteropHelper(_fenster).Handle;
            var s = griff != IntPtr.Zero ? VisualTreeHelper.GetDpi(_fenster).DpiScaleX : platz.Bildschirm.Skala;
            _fenster.WindowStartupLocation = WindowStartupLocation.Manual;
            if (!einSchritt || griff == IntPtr.Zero || _fenster.WindowState != WindowState.Normal || !Umsetzen(griff, platz, s))
            {
                _fenster.Left = platz.Links / s;
                _fenster.Top = platz.Oben / s;
                _fenster.Width = platz.Breite;
                _fenster.Height = platz.Hoehe;
            }
            if (platz.Maximiert && _fenster.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip) _fenster.WindowState = WindowState.Maximized;
            Gesetzt = platz;
        }
        finally
        {
            _setzt = false;
        }
    }

    /// <summary>
    /// Lage und Größe in einem SetWindowPos, in Bildpunkten mit der Skala <paramref name="s"/> des Fensters; ohne Z-Reihenfolge und ohne
    /// Aktivieren (SWP_NOZORDER, SWP_NOOWNERZORDER, SWP_NOACTIVATE). WPF zieht Left, Top, Width und Height dabei selbst aus WM_MOVE und WM_SIZE
    /// nach; weicht eine davon danach noch um mehr als einen halben Bildpunkt ab, wird sie gesetzt. Das bewegt nichts mehr, denn das Fenster
    /// steht schon dort. Falsch, wenn Windows das Setzen ablehnt; dann setzt der Aufrufer die Eigenschaften wie bisher.
    /// </summary>
    private bool Umsetzen(IntPtr griff, Platz platz, double s)
    {
        if (!SetWindowPos(griff, IntPtr.Zero, platz.Links, platz.Oben, Platzierung.Pixel(platz.Breite, s), Platzierung.Pixel(platz.Hoehe, s),
                SwpNozorder | SwpNoownerzorder | SwpNoactivate))
            return false;
        if (Daneben(_fenster.Left * s, platz.Links)) _fenster.Left = platz.Links / s;
        if (Daneben(_fenster.Top * s, platz.Oben)) _fenster.Top = platz.Oben / s;
        if (Daneben(_fenster.Width * s, platz.Breite * s)) _fenster.Width = platz.Breite;
        if (Daneben(_fenster.Height * s, platz.Hoehe * s)) _fenster.Height = platz.Hoehe;
        return true;
    }

    /// <summary>Mehr als einen halben Bildpunkt auseinander, oder nicht gesetzt (NaN).</summary>
    private static bool Daneben(double ist, double soll) => !(Math.Abs(ist - soll) <= 0.5);

    /// <summary>
    /// Der Platz des Fensters jetzt: Lage in Bildpunkten (mit Handle aus GetWindowRect, sonst aus Left und Top mit der
    /// Skala), Größe in Einheiten, bei Maximiert die des Normalzustands.
    /// </summary>
    public FensterOrt Aktuell()
    {
        // Maximiert zählt der Normalzustand. Vor dem ersten Zeigen hat WPF keine RestoreBounds; dann sind es Left, Top, Width, Height.
        var wiederher = _fenster.WindowState == WindowState.Normal ? Rect.Empty : _fenster.RestoreBounds;
        var normal = wiederher.IsEmpty || !double.IsFinite(wiederher.Width) || wiederher.Width <= 0
            ? new Rect(_fenster.Left, _fenster.Top, Mass(_fenster.ActualWidth, _fenster.Width), Mass(_fenster.ActualHeight, _fenster.Height))
            : wiederher;
        var s = Skala;
        var (links, oben) = _fenster.WindowState == WindowState.Normal && WindowsBildschirme.Rahmen(_fenster) is { } r
            ? (r.Links, r.Oben)
            : (Platzierung.Pixel(normal.Left, s), Platzierung.Pixel(normal.Top, s));
        return new FensterOrt(links, oben, Math.Round(normal.Width, 2), Math.Round(normal.Height, 2), _fenster.WindowState == WindowState.Maximized);
    }

    /// <summary>Beginn eines Handgriffs (WM_ENTERSIZEMOVE): der Platz davor.</summary>
    internal void BewegungBeginnt() => _vorher = Aktuell();

    /// <summary>Ende eines Handgriffs (WM_EXITSIZEMOVE): einmal merken, wenn sich Lage oder Größe geändert haben.</summary>
    internal void BewegungEndet()
    {
        if (_vorher is not { } vorher) return;
        _vorher = null;
        var jetzt = Aktuell();
        if (jetzt == vorher) return;
        Merken(jetzt, jetzt.Breite != vorher.Breite || jetzt.Hoehe != vorher.Hoehe);
    }

    /// <summary>
    /// Nach dem ersten Zeichnen: Steht das Fenster nicht dort, wo es hin sollte (mehr als ein Bildpunkt daneben), einmal
    /// mit der Skala des Fensters zurechtrücken. Maximiert entscheidet Windows über den Platz.
    /// </summary>
    internal void Nachfuehren()
    {
        if (_nachgesehen) return;
        _nachgesehen = true;
        BeimZeigen = WindowsBildschirme.Rahmen(_fenster);
        if (Gesetzt is not { Maximiert: false } p || BeimZeigen is not { } ist) return;
        if (Math.Abs(ist.Links - p.Links) <= 1 && Math.Abs(ist.Oben - p.Oben) <= 1) return;
        Setzen(p);
        Nachgefuehrt = true;
    }

    private void ZustandGewechselt()
    {
        if (_setzt || Gesetzt is null || _fenster.WindowState == WindowState.Minimized) return;
        Merken(Aktuell(), false);
    }

    private void Merken(FensterOrt ort, bool groesseGeaendert)
    {
        Gemerkt++;
        _merken(ort, groesseGeaendert);
    }

    private IntPtr Nachricht(IntPtr fenster, int nachricht, IntPtr w, IntPtr l, ref bool behandelt)
    {
        if (nachricht == WmEntersizemove) BewegungBeginnt();
        else if (nachricht == WmExitsizemove) BewegungEndet();
        return IntPtr.Zero;
    }

    /// <summary>Ohne Fensterhandle ist ActualWidth 0; dann gilt die gesetzte Größe.</summary>
    private static double Mass(double tatsaechlich, double gesetzt) => tatsaechlich > 0 ? tatsaechlich : gesetzt;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr fenster, IntPtr danach, int x, int y, int breite, int hoehe, uint flags);
}
