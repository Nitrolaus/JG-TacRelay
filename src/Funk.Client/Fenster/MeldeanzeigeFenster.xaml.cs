// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Funk.Client.Ablage;
using Funk.Client.Modelle;

namespace Funk.Client.Fenster;

/// <summary>
/// Das Fenster der Meldungen über dem Spiel (seit 0.12.0, Sarevoc 11.10. und 16.10.2026), nach dem Muster der Sprecheranzeige
/// (<see cref="SprecheranzeigeFenster"/>): <c>ShowActivated=false</c>, WS_EX_NOACTIVATE, WS_EX_TRANSPARENT (durchklickbar), WS_EX_TOOLWINDOW (nicht in
/// Alt+Tab und Taskleiste), WS_EX_LAYERED, ohne WS_EX_APPWINDOW (<see cref="Meldezeilenlage.Stil"/>), kein Owner. Es zeigt sich nur, solange
/// <see cref="Meldeanzeige.Sichtbar"/>, sonst ist es verborgen (Hide statt leer gezeigt). Beim Sichtbarwerden setzt es sich jedes Mal per
/// SetWindowPos mit HWND_TOPMOST und SWP_NOACTIVATE ganz nach oben. Nie Activate, Focus oder SetForegroundWindow, kein Prozess-Handle auf Star
/// Citizen, kein Speicherlesen, keine Eingaben ans Spiel. Über dem Spiel nur im randlosen Fenstermodus sichtbar.
/// <para>
/// Platz: zwei Regler wie bei der Sprecheranzeige, waagerecht und senkrecht je 0 bis 100 % des ganzen Monitorrechtecks, aber für den ganzen Block
/// (<see cref="Ecke"/>): bei 0 % am linken oder oberen Rand, bei 100 % am rechten oder unteren, bei 50 % mittig; ab Werk 50 und 0, oben Mitte. Der
/// Block ragt nie aus dem Monitor, und vor jedem Zeigen bekommt die Anzeige den Platz auf dem Monitor als Grenze (<see cref="Meldeanzeige.Grenze"/>),
/// damit keine Meldung größer wird als er. Monitor: der, auf dem das Spiel vorn läuft (ab Werk Star Citizen, <see cref="Spielprofil"/>, <see cref="Spielfenster.MonitorImVordergrund(string)"/>), sonst der
/// zuletzt bekannte, sonst der Hauptmonitor; eine eigene Wahl gibt es nicht.
/// </para>
/// </summary>
public partial class MeldeanzeigeFenster : Window
{
    /// <summary>Abstand zum Rand des Monitors in Einheiten (wie die Sprecheranzeige, mit der Skala des Monitors).</summary>
    public const double Rand = 24;

    private const int GwlExstyle = -20;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private static readonly nint HwndTopmost = -1;

    private readonly Meldeanzeige _anzeige;
    private readonly IBildschirme _bildschirme;
    private readonly Func<Sprecherlage> _wahl;
    private readonly Func<Bildschirm?> _spiel;

    /// <summary>Der zuletzt gesehene Monitor des Spiels, damit die Anzeige nicht springt, wenn gerade ein anderes Programm vorn ist.</summary>
    private Bildschirm? _letzterSpielmonitor;

    /// <param name="bildschirme">Monitore; ohne Angabe die echten (nur gelesen).</param>
    /// <param name="wahl">Lage aus den Einstellungen; ohne Angabe ab Werk (50 und 0, oben Mitte).</param>
    /// <param name="spiel">Der Monitor des Spiels; ohne Angabe keiner, im Betrieb <see cref="Spielfenster.MonitorImVordergrund"/>.</param>
    public MeldeanzeigeFenster(Meldeanzeige anzeige, IBildschirme? bildschirme = null, Func<Sprecherlage>? wahl = null, Func<Bildschirm?>? spiel = null)
    {
        ArgumentNullException.ThrowIfNull(anzeige);
        InitializeComponent();
        _anzeige = anzeige;
        _bildschirme = bildschirme ?? WindowsBildschirme.Instanz;
        _wahl = wahl ?? (() => new Sprecherlage(MeldungenEinstellungen.WaagerechtAbWerk, MeldungenEinstellungen.SenkrechtAbWerk, null));
        _spiel = spiel ?? (() => null);
        DataContext = anzeige;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            var stil = GetWindowLongW(h, GwlExstyle);
            _ = SetWindowLongW(h, GwlExstyle, Meldezeilenlage.Stil(stil));
        };
        SizeChanged += (_, _) => LageSetzen(nachOben: false);
    }

    /// <summary>
    /// Zeigen, solange es etwas zu zeigen gibt (ohne Fokus, an der gewählten Stelle, beim Sichtbarwerden ganz oben), sonst verbergen. Vorher bekommt
    /// die Anzeige den Platz auf dem Monitor als Grenze. Auch nach jedem Schieben an der Lage (zieht eine stehende Anzeige um).
    /// </summary>
    public void Abgleichen()
    {
        if (_anzeige.Sichtbar)
        {
            if (MonitorJetzt() is { } m) _anzeige.Grenze = Grenze(m);
            var neu = !IsVisible;
            if (neu) Show();
            LageSetzen(nachOben: neu);
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    /// <summary>Der Platz auf <paramref name="monitor"/> in Einheiten, ohne den Rand ringsum: so groß darf die Anzeige höchstens werden.</summary>
    public static Size Grenze(Bildschirm monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var skala = monitor.Skala > 0 && double.IsFinite(monitor.Skala) ? monitor.Skala : 1;
        return new Size(Math.Max(1, (monitor.Bereich.Breite / skala) - (2 * Rand)), Math.Max(1, (monitor.Bereich.Hoehe / skala) - (2 * Rand)));
    }

    /// <summary>
    /// Der Monitor der Anzeige: mit dem Spiel vorn dessen, sonst der zuletzt gesehene (wenn noch angeschlossen), sonst der Hauptmonitor; <c>null</c>
    /// ohne Monitor. Ohne Fensterhandle aufrufbar (Tests).
    /// </summary>
    internal Bildschirm? MonitorJetzt()
    {
        var alle = _bildschirme.Alle();
        if (alle.Count == 0) return null;
        var spiel = _spiel();
        if (spiel is not null) _letzterSpielmonitor = spiel;
        else spiel = _letzterSpielmonitor;
        if (spiel is not null && !alle.Any(b => b.Bereich == spiel.Bereich)) spiel = null;
        return Meldezeilenlage.Monitor(alle, null, spiel);
    }

    /// <summary>Wohin ein Block von <paramref name="breite"/> mal <paramref name="hoehe"/> Bildpunkten kommt; <c>null</c> ohne Monitor (Tests).</summary>
    internal (int X, int Y, Bildschirm Monitor)? ZielFuer(int breite, int hoehe)
    {
        if (MonitorJetzt() is not { } monitor) return null;
        var wahl = _wahl();
        var (x, y) = Ecke(wahl.Waagerecht, wahl.Senkrecht, monitor, breite, hoehe);
        return (x, y, monitor);
    }

    /// <summary>
    /// Linke obere Ecke in Bildpunkten für einen Block von <paramref name="breite"/> mal <paramref name="hoehe"/> Bildpunkten: Der Block wandert mit dem
    /// Prozentwert durch den Platz zwischen den Rändern (<see cref="Rand"/> Einheiten mit der Skala des Monitors), bei 0 % am linken oder oberen, bei
    /// 100 % am rechten oder unteren Rand, bei 50 % mittig. Ist er größer als der Platz, steht er am linken oder oberen Rand (ist er größer als der
    /// Monitor, an dessen Kante). Reine Rechnung.
    /// </summary>
    public static (int X, int Y) Ecke(int waagerecht, int senkrecht, Bildschirm monitor, int breite, int hoehe)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var b = monitor.Bereich;
        var rand = Platzierung.Pixel(Rand, monitor.Skala > 0 && double.IsFinite(monitor.Skala) ? monitor.Skala : 1);
        static int Achse(int anfang, int laenge, int rand, int prozent, int block)
        {
            var p = Math.Clamp(prozent, 0, MeldungenEinstellungen.LageMax);
            var frei = laenge - (2 * rand) - block;
            if (frei < 0) return anfang + Math.Min(rand, Math.Max(0, laenge - block));
            return anfang + rand + (int)Math.Round(frei * p / (double)MeldungenEinstellungen.LageMax, MidpointRounding.AwayFromZero);
        }
        return (Achse(b.Links, b.Breite, rand, waagerecht, breite), Achse(b.Oben, b.Hoehe, rand, senkrecht, hoehe));
    }

    /// <summary>
    /// An die gewählte Stelle des Monitors, per SetWindowPos ohne Aktivieren. Mit <paramref name="nachOben"/> (beim Sichtbarwerden) auch an derselben
    /// Stelle, damit die Anzeige wieder über allem steht; sonst nur, wenn sie sich bewegt.
    /// </summary>
    private void LageSetzen(bool nachOben)
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == 0 || !IsVisible || WindowsBildschirme.Rahmen(this) is not { } ich) return;
        if (ZielFuer(ich.Breite, ich.Hoehe) is not { } ziel) return;
        if (!nachOben && ziel.X == ich.Links && ziel.Y == ich.Oben) return;
        _ = SetWindowPos(h, HwndTopmost, ziel.X, ziel.Y, 0, 0, SwpNosize | SwpNoactivate);
    }

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint fenster, int index);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(nint fenster, int index, int wert);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint fenster, nint danach, int x, int y, int breite, int hoehe, uint flags);
}
