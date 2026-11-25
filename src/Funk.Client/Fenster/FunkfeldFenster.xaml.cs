// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Modelle;

namespace Funk.Client.Fenster;

/// <summary>
/// Funkfeld (A24): eigenes Fenster, immer im Vordergrund, schließbar, nicht minimierbar und nicht maximierbar (ein Overlay),
/// verschiebbar und größenveränderlich. Es nimmt beim Öffnen keinen Fokus (ShowActivated=false), damit es ein laufendes
/// Spiel nicht unterbricht. Tasten: 1-9 und 0 wählen das Sendenetz, + und - schalten weiter (nur ohne belegte globale
/// Netztasten, die das schon tun). Sarevoc, 29.09.2026: kompakt, weil es als Overlay läuft; zwei Ansichten (A Raster,
/// C Liste; A29) je mit 5 oder 10 Feldern. Die Titelzeile zieht das Fenster, ebenso jede freie Fläche (Ränder, Lücken
/// zwischen den Kacheln).
/// <para>
/// Größe und Lage (A32, Sarevoc 30.09.2026: "öffnet sich beim Starten absurd groß über 2 Bildschirme", "in optimaler Größe,
/// gerade so, dass alles angezeigt wird", "so wie sich jemand ein Fenster einstellt, soll es sein, auch später"): Das
/// Fenster misst sich nicht mehr selbst (kein SizeToContent; das gab dem Inhalt den ganzen virtuellen Bildschirm als
/// Angebot, siehe <see cref="Steuerelemente.Kachelraster"/>). <see cref="Wunschgroesse"/> misst einmal den Inhalt der
/// gewählten Ansicht mit unbegrenzter Breite, <see cref="Platzierung"/> rundet das auf ganze Bildpunkte des Zielmonitors und
/// begrenzt es auf dessen Arbeitsbereich, und die Größe wird fest gesetzt. Beim ersten Start steht das Funkfeld oben mittig
/// auf dem Monitor des Hauptfensters. Lage und Größe je Ansicht und Feldzahl merkt <see cref="Platzfuehrung"/>, sobald du es
/// verschiebst oder am Rand ziehst, und beim nächsten Start kommen sie so wieder; fehlt der Monitor, rückt es auf den
/// nächsten.
/// </para>
/// <para>
/// Durchsichtigkeit (A30, A33): Das Fenster ist immer geschichtet (AllowsTransparency), der Regler wirkt beim Ziehen sofort
/// und stufenlos, und nichts öffnet sich neu. Der Hauptweg macht das ganze Fenster durchsichtig
/// (<see cref="UIElement.Opacity"/>), die Option "Schrift und Symbole deckend halten" nur Lücken und Titelzeile: Die Flächen
/// nehmen ihre Pinsel aus <c>Window.Resources</c>, die <see cref="DurchsichtAnwenden"/> nach <see cref="FunkfeldDeckung"/>
/// setzt. Ein geschichtetes Fenster zeichnet WPF in Software und überträgt bei jeder Änderung sein ganzes Bild; darum bleibt
/// der Pegel auf den Kacheln gedrosselt (<see cref="FunkfeldViewModel.PegelAbstandMs"/>).
/// </para>
/// </summary>
public partial class FunkfeldFenster : Window
{
    /// <summary>Abstand vom oberen Rand des Arbeitsbereichs beim ersten Start, in Einheiten.</summary>
    public const double AbstandOben = 24;

    private readonly FunkfeldViewModel _modell;
    private readonly IBildschirme _bildschirme;
    private readonly Func<(int X, int Y)?>? _bezug;
    private readonly FensterOrt? _bisher;
    private bool _platzVorgemerkt;
    private bool _geschlossen;

    /// <param name="bildschirme">Monitore; ohne Angabe die echten (nur gelesen). Tests geben eine feste Liste.</param>
    /// <param name="bezug">Mitte des Hauptfensters in Bildpunkten: Beim ersten Start kommt das Funkfeld auf dessen Monitor.</param>
    /// <param name="bisher">
    /// Wo das vorige Funkfeld dieser Sitzung stand (Neuaufbau für eine Farbstellung, wieder geöffnet nach dem Schließen): die
    /// Lage gilt, solange für die Ansicht nichts gemerkt ist, in der optimalen Größe. Gemerkt wird sie dadurch nicht.
    /// </param>
    public FunkfeldFenster(FunkfeldViewModel modell, Einstellungsablage ablage, IBildschirme? bildschirme = null, Func<(int X, int Y)?>? bezug = null,
        FensterOrt? bisher = null)
    {
        ArgumentNullException.ThrowIfNull(modell);
        ArgumentNullException.ThrowIfNull(ablage);
        InitializeComponent();
        _modell = modell;
        _bildschirme = bildschirme ?? WindowsBildschirme.Instanz;
        _bezug = bezug;
        _bisher = bisher is null ? null : FensterOrt.OderNull(bisher with { Breite = null, Hoehe = null, Maximiert = false });
        DataContext = modell;
        TitelRegler.PreviewKeyDown += ReglerTaste;
        ReglerSymbol.Click += (_, _) =>
        {
            if (ReglerSymbol.IsChecked == true) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => TitelRegler.Focus()));
        };
        Fensterrahmen.Einrichten(this, minimierbar: false, maximierbar: false);
        Platzfuehrung = new Platzfuehrung(this, (ort, gezogen) =>
        {
            _modell.Einstellungen.Funkfeld.Merken(_modell.Darstellung, _modell.Ansicht, ort, gezogen);
            ablage.Vormerken();
        }, mitZustand: false);
        SkalaAnwenden();
        DarstellungAnwenden();
        PlatzAnwenden();
        DurchsichtAnwenden();
        PreviewKeyDown += TasteGedrueckt;
        MouseLeftButtonDown += FlaecheGezogen;
        _modell.PropertyChanged += ModellGeaendert;
        _modell.PlatzNeu += PlatzVormerken;
        Closed += (_, _) =>
        {
            _geschlossen = true;
            _modell.PropertyChanged -= ModellGeaendert;
            _modell.PlatzNeu -= PlatzVormerken;
        };
    }

    /// <summary>Lage und Größe (A32): setzen, nach dem ersten Zeigen nachsehen, merken, was du daraus machst.</summary>
    internal Platzfuehrung Platzfuehrung { get; }

    /// <summary>Die zuletzt gemessene Wunschgröße des Inhalts in Einheiten (Messwerkzeug, Tests).</summary>
    internal Size LetzteWunschgroesse { get; private set; }

    /// <summary>
    /// Setzt die Flächen und die Dimmung im Zustand AUS nach <see cref="FunkfeldViewModel.Deckung"/> und die Deckkraft des
    /// ganzen Fensters, sofort. Die Farben kommen aus den Ressourcen, damit eine Farbstellung (Farbvorschlaege) auch hier gilt.
    /// </summary>
    internal void DurchsichtAnwenden()
    {
        var d = _modell.Deckung;
        Resources["Funkfeld.Grund"] = Flaeche("Farbe.Anthrazit", Palette.Anthrazit, d.Luecken);
        Resources["Funkfeld.Kopf"] = Flaeche("Farbe.FlaecheTief", Palette.FlaecheTief, d.Luecken);
        Resources["Funkfeld.Unterlage"] = Flaeche("Farbe.FlaecheTief", Palette.FlaecheTief, d.Unterlage);
        Resources["Funkfeld.Kachel"] = Flaeche("Farbe.Flaeche", Palette.Flaeche, d.Kachel);
        Resources["Funkfeld.Global"] = Flaeche("Farbe.Leitfarbe", Palette.Leitfarbe, d.Global);
        Resources["Funkfeld.AusDeckkraft"] = d.AusDeckkraft;
        Resources["Funkfeld.LeerDeckkraft"] = d.LeerDeckkraft;
        Opacity = d.Fenster;
    }

    /// <summary>
    /// Kacheln, Vorlage, Fläche und Rand für die gewählte Ansicht. Im Code statt über Bindung und Trigger in der XAML, damit
    /// sie sicher stehen, bevor <see cref="PlatzAnwenden"/> die neue Ansicht misst: Die Bindung löst WPF erst auf, wenn der
    /// Dispatcher läuft, und im Konstruktor käme sonst nur die Titelzeile heraus (gemessen: 198 x 34).
    /// </summary>
    internal void DarstellungAnwenden()
    {
        var liste = _modell.Darstellung == FunkfeldDarstellung.Liste;
        // Rollenliste und "Mehrere senden": im Raster in der Titelzeile, in der Liste in einer zweiten Kopfzeile (Stufe 3).
        RolleUnterlage.Visibility = liste ? Visibility.Collapsed : Visibility.Visible;
        Kopf2.Visibility = liste ? Visibility.Visible : Visibility.Collapsed;
        var vorlage = (DataTemplate)FindResource(liste ? "ListeVorlage" : "RasterVorlage");
        if (ReferenceEquals(Felder.ItemTemplate, vorlage)) return;
        // Erst die Kacheln ab, dann Vorlage und Fläche: Sonst baut WPF die Kacheln mit der neuen Vorlage in der alten Fläche
        // und wirft sie gleich wieder weg, und deren Symbole melden Bindungsfehler (FindAncestor ohne Vorfahren).
        Felder.ItemsSource = null;
        Felder.Margin = new Thickness(liste ? 3 : 4);
        Felder.ItemTemplate = vorlage;
        Felder.ItemsPanel = (ItemsPanelTemplate)FindResource(liste ? "ListeFlaeche" : "RasterFlaeche");
        Felder.ItemsSource = _modell.Kacheln;
    }

    /// <summary>
    /// Platz für die aktuelle Ansicht und Feldzahl: der gemerkte, sonst die Lage der anderen Feldzahl derselben Ansicht oder
    /// aus einer älteren Datei, jeweils in der gemerkten oder der optimalen Größe (<see cref="Platzierung.Wiederherstellen"/>).
    /// Ohne jeden Anhalt bleibt ein offenes Fenster mit seiner linken oberen Ecke stehen und nimmt die optimale Größe der neuen
    /// Ansicht; ein neues kommt an die Stelle des vorigen Funkfelds dieser Sitzung, sonst oben mittig auf den Monitor des
    /// Hauptfensters (sonst den Hauptmonitor).
    /// </summary>
    internal void PlatzAnwenden()
    {
        var alle = _bildschirme.Alle();
        if (alle.Count == 0) return;
        // Feste Mindestgröße (Sarevoc Q12): gerade so groß, dass alles zu sehen ist, je Ansicht, Feldzahl und Skalierung. Kleiner lässt
        // es sich nicht ziehen; eine früher kleiner gezogene Größe wird angehoben. Nur ein Arbeitsbereich, der selbst kleiner ist,
        // begrenzt das Fenster darunter (Platzierung.Begrenzen), sonst ragte es über den Monitor.
        MinWidth = 0;
        MinHeight = 0;
        var wunsch = Wunschgroesse();
        var ort = _modell.Einstellungen.Funkfeld.OrtFuer(_modell.Darstellung, _modell.Ansicht, Platzierung.Haupt(alle).Skala);
        if (ort is null && Platzfuehrung.Gesetzt is not null) ort = Platzfuehrung.Aktuell() with { Breite = null, Hoehe = null, Maximiert = false };
        ort ??= _bisher;
        var platz = ort is not null
            ? Platzierung.Wiederherstellen(ort, wunsch, alle, wunsch)
            : Platzierung.ObenMittig(wunsch, Bezugsmonitor(alle), AbstandOben, wunsch);
        MinWidth = Math.Min(wunsch.Width, platz.Breite);
        MinHeight = Math.Min(wunsch.Height, platz.Hoehe);
        Platzfuehrung.Setzen(platz);
    }

    /// <summary>
    /// Skalierung des Funkfelds (Sarevoc Q12, -30 bis +30 %): der ganze Inhalt als LayoutTransform, die Titelzeile zum Ziehen mit. Danach
    /// misst <see cref="PlatzAnwenden"/> die neue Mindestgröße.
    /// </summary>
    internal void SkalaAnwenden()
    {
        var s = _modell.Skala;
        Inhalt.LayoutTransform = Math.Abs(s - 1) < 1e-9 ? Transform.Identity : new ScaleTransform(s, s);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 24 * s,
            ResizeBorderThickness = new Thickness(5),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
    }

    /// <summary>
    /// Wunschgröße des Inhalts der gewählten Ansicht in Einheiten, frei gemessen (unbegrenzt in beiden Richtungen): die
    /// Kacheln in ihrer natürlichen Breite, Titelzeile und Rand dazu. Jedes Element wird neu gemessen: Ein direkter Aufruf
    /// von Measure misst Kinder, deren Angebot gleich blieb, nicht nach, auch wenn sich ihre Kacheln geändert haben (sonst
    /// kam nach dem Wechsel von 10 auf 5 Felder die Höhe des 10ers heraus). Zweimal, denn beim ersten Messen entstehen die
    /// Kacheln einer neuen Ansicht erst.
    /// </summary>
    internal Size Wunschgroesse()
    {
        ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(this) == 0 || VisualTreeHelper.GetChild(this, 0) is not UIElement wurzel) return new Size(MinWidth, MinHeight);
        var frei = new Size(double.PositiveInfinity, double.PositiveInfinity);
        for (var durchgang = 0; durchgang < 2; durchgang++)
        {
            NeuMessen(wurzel);
            wurzel.Measure(frei);
        }
        LetzteWunschgroesse = wurzel.DesiredSize;
        return wurzel.DesiredSize;

        static void NeuMessen(DependencyObject d)
        {
            if (d is UIElement u) u.InvalidateMeasure();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) NeuMessen(VisualTreeHelper.GetChild(d, i));
        }
    }

    private Bildschirm Bezugsmonitor(IReadOnlyList<Bildschirm> alle) =>
        _bezug?.Invoke() is { } p ? Platzierung.Am(p.X, p.Y, alle) : Platzierung.Haupt(alle);

    /// <summary>Escape im aufgeklappten Regler der Liste klappt ihn zu; der Fokus geht auf sein Symbol.</summary>
    private void ReglerTaste(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_modell.ReglerBreit) return;
        _modell.ReglerOffen = false;
        ReglerSymbol.Focus();
        e.Handled = true;
    }

    private SolidColorBrush Flaeche(string schluessel, Color ersatz, byte alpha)
    {
        var farbe = TryFindResource(schluessel) is Color c ? c : ersatz;
        var pinsel = new SolidColorBrush(Color.FromArgb(alpha, farbe.R, farbe.G, farbe.B));
        pinsel.Freeze();
        return pinsel;
    }

    private void ModellGeaendert(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FunkfeldViewModel.Deckung):
                DurchsichtAnwenden();
                break;
            case nameof(FunkfeldViewModel.Darstellung) or nameof(FunkfeldViewModel.Ansicht):
                DarstellungAnwenden();
                PlatzVormerken();
                break;
            case nameof(FunkfeldViewModel.Skala):
                SkalaAnwenden();
                PlatzVormerken();
                break;
        }
    }

    /// <summary>
    /// Nach einem Wechsel von Ansicht oder Feldzahl den Platz gleich anwenden, aber nicht mitten in der Änderung: vor dem
    /// nächsten Layout und Zeichnen (Priorität Normal, Layout läuft mit Render), also ohne Bild in der alten Größe. Mehrere
    /// Wechsel hintereinander messen so nur einmal, und es entstehen keine Kacheln, die gleich wieder wegfallen (deren Symbole
    /// meldeten sonst Bindungsfehler, gemessen: 5 beim Wechsel 5, 10 ohne Dispatcher dazwischen).
    /// </summary>
    private void PlatzVormerken()
    {
        if (_platzVorgemerkt) return;
        _platzVorgemerkt = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            _platzVorgemerkt = false;
            if (!_geschlossen) PlatzAnwenden();
        }));
    }

    /// <summary>Freie Flächen ziehen das Fenster; Knöpfe, Regler und die wählbaren Kacheln behandeln ihren Klick selbst.</summary>
    private void FlaecheGezogen(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ButtonState != MouseButtonState.Pressed) return;
        e.Handled = true;
        DragMove();
    }

    private void Kachelkopf_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: KachelViewModel k }) return;
        var dazu = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _ = dazu ? k.SendenHinzufuegenCommand.ExecuteAsync(null) : k.SendenCommand.ExecuteAsync(null);
    }

    private void TasteGedrueckt(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers is not ModifierKeys.None) return;
        int? ziffer = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            _ => null,
        };
        if (ziffer is { } z)
        {
            _ = _modell.TasteAsync(z);
            e.Handled = true;
            return;
        }
        var richtung = e.Key switch
        {
            Key.Add or Key.OemPlus => 1,
            Key.Subtract or Key.OemMinus => -1,
            _ => 0,
        };
        // Mit belegten globalen Netztasten schaltet Raw Input schon; ein zweites Mal hier wäre doppelt.
        if (richtung == 0 || _modell.NetztastenGlobal) return;
        _ = _modell.WeiterschaltenAsync(richtung);
        e.Handled = true;
    }
}
