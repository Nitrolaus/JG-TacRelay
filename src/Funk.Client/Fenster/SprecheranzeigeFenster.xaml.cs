// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Funk.Client.Ablage;
using Funk.Client.Modelle;

namespace Funk.Client.Fenster;

/// <summary>
/// Das Fenster der Sprecheranzeige über dem Spiel (seit 0.11.9, Sarevoc 11.10. und 15.10.2026), nach dem Muster der Meldezeile
/// (<see cref="MeldezeileFenster"/>): <c>ShowActivated=false</c>, WS_EX_NOACTIVATE, WS_EX_TRANSPARENT (durchklickbar), WS_EX_TOOLWINDOW (nicht in
/// Alt+Tab und Taskleiste), WS_EX_LAYERED, ohne WS_EX_APPWINDOW (<see cref="Meldezeilenlage.Stil"/>), kein Owner (sonst ginge es mit dem
/// Hauptfenster in die Taskleiste). Es zeigt sich nur, solange <see cref="Sprecheranzeige.Sichtbar"/>, sonst ist es verborgen (Hide statt leer
/// gezeigt: ein gezeigtes Fenster über dem Spiel kostet den direkten Weg zum Bildschirm). Beim Sichtbarwerden setzt es sich jedes Mal per
/// SetWindowPos mit HWND_TOPMOST und SWP_NOACTIVATE ganz nach oben. Nie Activate, Focus oder SetForegroundWindow, kein Prozess-Handle auf Star
/// Citizen, kein Speicherlesen, keine Eingaben ans Spiel. Über dem Spiel nur im randlosen Fenstermodus sichtbar.
/// <para>
/// Platz: die linke obere Ecke folgt den zwei Reglern der Einstellungen, waagerecht und senkrecht je 0 bis 100 % des ganzen Monitorrechtecks
/// (<see cref="Ecke"/>; Sarevoc 15.10.2026: "die Position mit Schieberegler einstellen, nicht fest vorgeschlagen", bis dahin neun feste Stellen), ab Werk
/// 0 und 0 ganz oben links mit <see cref="Rand"/> Einheiten Abstand; der Block ragt nie aus dem Monitor. Monitor Automatisch: der, auf dem das Spiel
/// vorn läuft (ab Werk Star Citizen, <see cref="Spielfenster.MonitorImVordergrund(string)"/>), sonst der zuletzt bekannte, sonst der Hauptmonitor; wahlweise ein fester.
/// </para>
/// </summary>
public partial class SprecheranzeigeFenster : Window
{
    /// <summary>Abstand zum Rand des Monitors in Einheiten bei 0 % (Vorgabe: etwa 24 Bildpunkte, mit der Skala des Monitors), ebenso bei 100 %.</summary>
    public const double Rand = 24;

    private const int GwlExstyle = -20;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private static readonly nint HwndTopmost = -1;

    private readonly Sprecheranzeige _anzeige;
    private readonly IBildschirme _bildschirme;
    private readonly Func<Sprecherlage> _wahl;
    private readonly Func<Bildschirm?> _spiel;

    /// <summary>Der zuletzt gesehene Monitor des Spiels (Automatisch), damit die Anzeige nicht springt, wenn gerade ein anderes Programm vorn ist.</summary>
    private Bildschirm? _letzterSpielmonitor;

    /// <param name="bildschirme">Monitore; ohne Angabe die echten (nur gelesen).</param>
    /// <param name="wahl">Lage und Monitor aus den Einstellungen; ohne Angabe 0 und 0 (oben links), Automatisch.</param>
    /// <param name="spiel">Der Monitor des Spiels für Automatisch; ohne Angabe keiner, im Betrieb <see cref="Spielfenster.MonitorImVordergrund"/>.</param>
    public SprecheranzeigeFenster(Sprecheranzeige anzeige, IBildschirme? bildschirme = null, Func<Sprecherlage>? wahl = null, Func<Bildschirm?>? spiel = null)
    {
        ArgumentNullException.ThrowIfNull(anzeige);
        InitializeComponent();
        _anzeige = anzeige;
        _bildschirme = bildschirme ?? WindowsBildschirme.Instanz;
        _wahl = wahl ?? (() => new Sprecherlage(SprecheranzeigeEinstellungen.LageAbWerk, SprecheranzeigeEinstellungen.LageAbWerk, null));
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
    /// Zeigen, solange es etwas zu zeigen gibt (ohne Fokus, an der gewählten Stelle, beim Sichtbarwerden ganz oben), sonst verbergen. Auch nach
    /// jedem Schieben an der Lage und einer neuen Wahl des Monitors (zieht eine stehende Anzeige um).
    /// </summary>
    public void Abgleichen()
    {
        if (_anzeige.Sichtbar)
        {
            var neu = !IsVisible;
            if (neu) Show();
            LageSetzen(nachOben: neu);
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    /// <summary>
    /// Wohin eine Anzeige von <paramref name="breite"/> mal <paramref name="hoehe"/> Bildpunkten kommt: der Monitor aus der Wahl (bei Automatisch
    /// der des Spiels, sonst der zuletzt gesehene, sonst der Hauptmonitor) und die Ecke nach den Reglern (<see cref="Ecke"/>); <c>null</c> ohne Monitor. Ohne
    /// Fensterhandle aufrufbar (Tests).
    /// </summary>
    internal (int X, int Y, Bildschirm Monitor)? ZielFuer(int breite, int hoehe)
    {
        var alle = _bildschirme.Alle();
        if (alle.Count == 0) return null;
        var wahl = _wahl();
        var spiel = wahl.Monitor is null ? _spiel() : null;
        if (spiel is not null) _letzterSpielmonitor = spiel;
        else if (wahl.Monitor is null) spiel = _letzterSpielmonitor;
        // Ein zuletzt gesehener Monitor, der nicht mehr angeschlossen ist, zählt nicht.
        if (spiel is not null && !alle.Any(b => b.Bereich == spiel.Bereich)) spiel = null;
        var monitor = Meldezeilenlage.Monitor(alle, wahl.Monitor, spiel);
        var (x, y) = Ecke(wahl.Waagerecht, wahl.Senkrecht, monitor, breite, hoehe);
        return (x, y, monitor);
    }

    /// <summary>
    /// Linke obere Ecke in Bildpunkten für einen Block von <paramref name="breite"/> mal <paramref name="hoehe"/> Bildpunkten bei
    /// <paramref name="waagerecht"/> und <paramref name="senkrecht"/> Prozent auf <paramref name="monitor"/>: Die Ecke steht bei 0 % <see cref="Rand"/>
    /// Einheiten mit der Skala des Monitors vom linken und oberen Rand des ganzen Monitorrechtecks (auch über der Taskleiste) und wandert mit dem
    /// Prozentwert über den Monitor bis ebenso weit vom rechten und unteren Rand. Ragte der Block dort hinaus, rückt er hinein, bis er den Rand von
    /// rechts oder unten wahrt; ein Block, der breiter oder höher ist als der Monitor, bleibt am linken oder oberen Rand. Reine Rechnung.
    /// </summary>
    public static (int X, int Y) Ecke(int waagerecht, int senkrecht, Bildschirm monitor, int breite, int hoehe)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var b = monitor.Bereich;
        var rand = Platzierung.Pixel(Rand, monitor.Skala > 0 && double.IsFinite(monitor.Skala) ? monitor.Skala : 1);
        static int Achse(int anfang, int laenge, int rand, int prozent, int block)
        {
            var p = Math.Clamp(prozent, 0, SprecheranzeigeEinstellungen.LageMax);
            var spanne = Math.Max(0, laenge - (2 * rand));
            var wunsch = anfang + rand + (int)Math.Round(spanne * p / (double)SprecheranzeigeEinstellungen.LageMax, MidpointRounding.AwayFromZero);
            var hoechstens = anfang + laenge - rand - block;
            var mindestens = anfang + Math.Min(rand, Math.Max(0, laenge - block));
            return Math.Max(mindestens, Math.Min(wunsch, hoechstens));
        }
        return (Achse(b.Links, b.Breite, rand, waagerecht, breite), Achse(b.Oben, b.Hoehe, rand, senkrecht, hoehe));
    }

    /// <summary>
    /// An die gewählte Stelle des gewählten Monitors, per SetWindowPos ohne Aktivieren. Mit <paramref name="nachOben"/> (beim Sichtbarwerden) auch
    /// an derselben Stelle, damit die Anzeige wieder über allem steht; sonst nur, wenn sie sich bewegt.
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
