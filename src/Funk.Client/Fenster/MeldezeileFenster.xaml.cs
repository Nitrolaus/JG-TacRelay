// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Funk.Client.Ablage;
using Funk.Client.Modelle;

namespace Funk.Client.Fenster;

/// <summary>
/// Das Fenster der Meldezeile (RAID-S2-ENTWURF Abschnitt 3). Es zeigt sich nur, solange <see cref="Meldezeile.Sichtbar"/>, und nie mit
/// Fokus: <c>ShowActivated=false</c>, dazu WS_EX_NOACTIVATE, WS_EX_TRANSPARENT (durchklickbar), WS_EX_TOOLWINDOW (nicht in Alt+Tab
/// und Taskleiste) und WS_EX_LAYERED (<see cref="Meldezeilenlage.Stil"/>). Seit 0.11.1 steht es an einem festen Platz (Sarevoc 12.10.2026: "an
/// einem festen Platz am Bildschirm, nicht da, wo das Funkfeld ist"): an einer von neun Stellen auf dem gewählten Monitor, ab Werk oben
/// Mitte auf dem Monitor, auf dem das Spiel läuft (ab Werk Star Citizen), sonst auf dem Hauptmonitor (<see cref="Meldezeilenlage.Fest"/>,
/// <see cref="Meldezeilenlage.Monitor"/>, <see cref="Spielfenster"/>); bis 0.11.0 über dem Funkfeld. Über dem Spiel nur im randlosen
/// Fenstermodus sichtbar, wie das Funkfeld. Die Breite ist fest 640 Einheiten bei 0 % und skaliert mit dem Funkfeld (Sarevoc Q111,
/// <see cref="GroesseSetzen"/>), eine Meldung in einer Zeile (Sarevoc Q112).
/// </summary>
public partial class MeldezeileFenster : Window
{
    private const int GwlExstyle = -20;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private static readonly nint HwndTopmost = -1;

    private readonly Meldezeile _zeile;
    private readonly Func<double> _skalierung;
    private readonly IBildschirme _bildschirme;
    private readonly Func<Meldezeilenwahl> _wahl;
    private readonly Func<Bildschirm?> _spiel;

    /// <param name="bildschirme">Monitore; ohne Angabe die echten (nur gelesen).</param>
    /// <param name="skalierung">Faktor der Skalierung des Funkfelds (<c>FunkfeldViewModel.Skala</c>, 0,7 bis 1,3); ohne Angabe 1.</param>
    /// <param name="wahl">Stelle und Monitor aus den Einstellungen; ohne Angabe oben Mitte, Automatisch.</param>
    /// <param name="spiel">Der Monitor des Spiels für Automatisch; ohne Angabe keiner (Hauptmonitor), im Betrieb <see cref="Spielfenster.Monitor"/>.</param>
    public MeldezeileFenster(Meldezeile zeile, IBildschirme? bildschirme = null, Func<double>? skalierung = null, Func<Meldezeilenwahl>? wahl = null,
        Func<Bildschirm?>? spiel = null)
    {
        ArgumentNullException.ThrowIfNull(zeile);
        InitializeComponent();
        _zeile = zeile;
        _skalierung = skalierung ?? (() => 1);
        _bildschirme = bildschirme ?? WindowsBildschirme.Instanz;
        _wahl = wahl ?? (() => new Meldezeilenwahl(FunkfeldEinstellungen.MeldezeileStelleAbWerk, null));
        _spiel = spiel ?? (() => null);
        DataContext = zeile;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            var stil = GetWindowLongW(h, GwlExstyle);
            _ = SetWindowLongW(h, GwlExstyle, Meldezeilenlage.Stil(stil));
        };
        SizeChanged += (_, _) => LageSetzen();
    }

    /// <summary>Zeigen, solange die Meldezeile Text hat (ohne Fokus, oben), sonst verbergen; Größe und Lage aus Skalierung und Wahl.</summary>
    public void Abgleichen()
    {
        if (_zeile.Sichtbar)
        {
            // Erst Breite und Skalierung, dann zeigen: Das erste Bild hat schon eine Zeile in der Breite und im Maß des Funkfelds.
            GroesseSetzen();
            if (!IsVisible) Show();
            LageSetzen();
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    /// <summary>
    /// Breite und Maß: fest 640 Einheiten bei 0 % (<see cref="Meldezeilenlage.Hoechstbreite"/>; bis 0.11.0 höchstens so breit wie das
    /// Funkfeld), die Meldung bleibt eine Zeile und kürzt mit "...". Der ganze Rahmen skaliert mit dem Funkfeld (Sarevoc Q111): Schrift,
    /// Innenrand und Höhe als LayoutTransform wie beim Funkfeld (<c>FunkfeldFenster.SkalaAnwenden</c>), die Höchstbreite davor geteilt;
    /// so passt bei -30 und +30 % ganz, was bei 0 % ganz passt. Ohne Fensterhandle aufrufbar (Bildlauf der Tests).
    /// </summary>
    internal void GroesseSetzen()
    {
        var s = Meldezeilenlage.Skalierung(_skalierung());
        var breite = Meldezeilenlage.Hoechstbreite(s) / s;
        // 0.11.2 (Sarevoc 13.10.2026: "nicht immer an einem anderen Platz, die muss an einem Platz sein, der festgelegt wird"): fest breit, nicht
        // nach dem Satz; sonst wanderte der Rahmen in der Mitte und rechts mit jeder Meldung.
        if (Math.Abs(Rahmen.MaxWidth - breite) > 0.001) Rahmen.MaxWidth = breite;
        if (Math.Abs(Rahmen.Width - breite) > 0.001) Rahmen.Width = breite;
        var bisher = Rahmen.LayoutTransform is ScaleTransform t ? t.ScaleX : 1;
        if (Math.Abs(bisher - s) > 1e-9) Rahmen.LayoutTransform = Math.Abs(s - 1) < 1e-9 ? Transform.Identity : new ScaleTransform(s, s);
    }

    /// <summary>
    /// Wohin eine Meldezeile von <paramref name="breite"/> mal <paramref name="hoehe"/> Bildpunkten kommt: der Monitor aus der Wahl (bei
    /// Automatisch der des Spiels, sonst der Hauptmonitor) und die Ecke an der gewählten Stelle; <c>null</c> ohne Monitor. Ohne
    /// Fensterhandle aufrufbar (Tests).
    /// </summary>
    internal (int X, int Y, Bildschirm Monitor)? ZielFuer(int breite, int hoehe)
    {
        var alle = _bildschirme.Alle();
        if (alle.Count == 0) return null;
        var wahl = _wahl();
        // Automatisch: der Monitor des Spiels; verliert das Programm das Spiel kurz aus den Augen, bleibt es beim zuletzt gesehenen (0.11.2,
        // sonst sprang die Meldezeile auf den Hauptmonitor und zurück).
        var spiel = wahl.Monitor is null ? _spiel() : null;
        if (spiel is not null) _letzterSpielmonitor = spiel;
        else if (wahl.Monitor is null) spiel = _letzterSpielmonitor;
        var monitor = Meldezeilenlage.Monitor(alle, wahl.Monitor, spiel);
        var (x, y) = Meldezeilenlage.Fest(wahl.Stelle, monitor, breite, hoehe);
        return (x, y, monitor);
    }

    /// <summary>Der zuletzt gesehene Monitor des Spiels (Automatisch), damit die Meldezeile nicht springt.</summary>
    private Bildschirm? _letzterSpielmonitor;

    /// <summary>An die gewählte Stelle des gewählten Monitors; gesetzt per SetWindowPos ohne Aktivieren.</summary>
    private void LageSetzen()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == 0 || !IsVisible || WindowsBildschirme.Rahmen(this) is not { } ich) return;
        if (ZielFuer(ich.Breite, ich.Hoehe) is not { } ziel) return;
        if (ziel.X == ich.Links && ziel.Y == ich.Oben) return;
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
