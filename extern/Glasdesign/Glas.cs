// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Angehängte Eigenschaften der Glasstile.
/// <list type="bullet">
/// <item><c>Glas.Schatten</c>: Voll, Einfach oder Aus; vererbt sich, also einmal am Fenster setzen.</item>
/// <item><c>Glas.ZustandVorschau</c>: erzwingt Überfahren, Gedrückt oder Fokus (Musterblatt und Tests, kein Nutzerfeature).</item>
/// <item><c>Glas.Form</c> und <c>Glas.Art</c>: setzen die Stile (Glas.Knopf.Eckig, Glas.Knopf.Haupt); die Vorlagen reichen sie an
/// den <see cref="Glaskoerper"/> weiter. Selbst setzen nur, wenn man einen Stil abwandelt.</item>
/// <item><c>Glas.Leuchtfarbe</c>, <c>Glas.Leuchtet</c>, <c>Glas.Wartet</c>, <c>Glas.Leuchtzeichen</c>: Angaben des Hosts für den
/// <see cref="Leuchtschalter"/> (Stil Glas.Leuchtschalter), unabhängig von der Stellung IsChecked. Wie im JG-Original
/// (GuardianSwitch.AccentColor, IsIlluminated, IsPending).</item>
/// </list>
/// Seit 0.5.0-beta.11 partiell: die Eigenschaften der Eingabe (Platzhalter, Hinweistext, Fehlertext, Fehlerbalken,
/// Muldensymbol) stehen in Eingabe.cs.
/// </summary>
public static partial class Glas
{
    public static readonly DependencyProperty SchattenProperty = DependencyProperty.RegisterAttached(
        "Schatten", typeof(Schattenstufe), typeof(Glas),
        new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ZustandVorschauProperty = DependencyProperty.RegisterAttached(
        "ZustandVorschau", typeof(Zustandvorschau), typeof(Glas), new FrameworkPropertyMetadata(Zustandvorschau.Keine));

    public static readonly DependencyProperty FormProperty = DependencyProperty.RegisterAttached(
        "Form", typeof(Glasform), typeof(Glas), new FrameworkPropertyMetadata(Glasform.Pille));

    /// <summary>
    /// Eine beliebige Form für Glas (Abschnitt 6, <see cref="Glasdesign.Umriss"/>): gesetzt, zeichnen Körper, Haarlinie,
    /// Glanz, Leuchtsaum, Spotlicht und Lichtkante auf ihr statt auf <see cref="FormProperty"/>. In XAML als Text:
    /// <c>gd:Glas.Umriss="Fase 6"</c>.
    /// </summary>
    public static readonly DependencyProperty UmrissProperty = DependencyProperty.RegisterAttached(
        "Umriss", typeof(Umriss), typeof(Glas), new FrameworkPropertyMetadata(null));

    public static Umriss? GetUmriss(DependencyObject element) => (Umriss?)(element ?? throw new ArgumentNullException(nameof(element))).GetValue(UmrissProperty);

    public static void SetUmriss(DependencyObject element, Umriss? wert) => (element ?? throw new ArgumentNullException(nameof(element))).SetValue(UmrissProperty, wert);

    public static readonly DependencyProperty ArtProperty = DependencyProperty.RegisterAttached(
        "Art", typeof(Glasart), typeof(Glas), new FrameworkPropertyMetadata(Glasart.Normal));

    /// <summary>Farbe des Leuchtens; der aufrufende Bereich wählt sie (Stile Glas.Leuchtschalter.Gruen, .Rot ...).</summary>
    public static readonly DependencyProperty LeuchtfarbeProperty = DependencyProperty.RegisterAttached(
        "Leuchtfarbe", typeof(Color), typeof(Glas), new FrameworkPropertyMetadata(Color.FromRgb(0x17, 0xEE, 0xFF)));

    /// <summary>Bestätigtes Leuchten: false unterdrückt es auch bei IsChecked, null folgt IsChecked.</summary>
    public static readonly DependencyProperty LeuchtetProperty = DependencyProperty.RegisterAttached(
        "Leuchtet", typeof(bool?), typeof(Glas), new FrameworkPropertyMetadata(null));

    /// <summary>Änderung wartet auf Bestätigung: gelber Rand, kein Leuchten.</summary>
    public static readonly DependencyProperty WartetProperty = DependencyProperty.RegisterAttached(
        "Wartet", typeof(bool), typeof(Glas), new FrameworkPropertyMetadata(false));

    /// <summary>Zeichen im Knauf (<see cref="Glasdesign.Leuchtzeichen"/>), null = leere Glaslinse.</summary>
    public static readonly DependencyProperty LeuchtzeichenProperty = DependencyProperty.RegisterAttached(
        "Leuchtzeichen", typeof(Geometry), typeof(Glas), new FrameworkPropertyMetadata(null));

    /// <summary>Eckradius der Farbglasknöpfe als Anteil der Höhe: 0 eckig, 0,5 Pille oder Kreis (Stile Glas.Gelknopf, Glas.Kissenknopf).</summary>
    public static readonly DependencyProperty RundungProperty = DependencyProperty.RegisterAttached(
        "Rundung", typeof(double), typeof(Glas), new FrameworkPropertyMetadata(0.5));

    /// <summary>Machart der Farbglasknöpfe (<see cref="Glasdesign.Farbglasart"/>); setzen die Stile.</summary>
    public static readonly DependencyProperty FarbglasartProperty = DependencyProperty.RegisterAttached(
        "Farbglasart", typeof(Farbglasart), typeof(Glas), new FrameworkPropertyMetadata(Glasdesign.Farbglasart.Gel));

    /// <summary>Farbe der Akzentleiste eines Felds (Stil Glas.Feld); null heißt keine Leiste.</summary>
    public static readonly DependencyProperty AkzentfarbeProperty = DependencyProperty.RegisterAttached(
        "Akzentfarbe", typeof(Brush), typeof(Glas), new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Tastaturfokus, der über die Tastatur kam (Tab, Pfeile), nicht nach einem Mausklick; ein Mausklick nimmt ihn weg. Setzt
    /// die Bibliothek selbst an jedem Steuerelement, die Vorlagen schalten damit den Leuchtsaum. So zeigt WPF auch seinen
    /// FocusVisualStyle; IsKeyboardFocused dagegen bleibt nach jedem Klick stehen (der weiße Rand in Joystick Guardian).
    /// </summary>
    public static readonly DependencyProperty TastaturfokusProperty = DependencyProperty.RegisterAttached(
        "Tastaturfokus", typeof(bool), typeof(Glas), new FrameworkPropertyMetadata(false));

    static Glas()
    {
        EventManager.RegisterClassHandler(typeof(Control), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(FokusErhalten), true);
        EventManager.RegisterClassHandler(typeof(Control), Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(FokusVerloren), true);
        EventManager.RegisterClassHandler(typeof(Control), Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Maus), true);
    }

    private static void FokusErhalten(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.NewFocus) && sender is DependencyObject d)
            d.SetValue(TastaturfokusProperty, InputManager.Current.MostRecentInputDevice is KeyboardDevice);
    }

    private static void FokusVerloren(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OldFocus) && sender is DependencyObject d && GetTastaturfokus(d)) d.ClearValue(TastaturfokusProperty);
    }

    private static void Maus(object sender, MouseButtonEventArgs e)
    {
        if (sender is DependencyObject d && GetTastaturfokus(d)) d.ClearValue(TastaturfokusProperty);
    }

    public static bool GetTastaturfokus(DependencyObject element) => (bool)Pruefen(element).GetValue(TastaturfokusProperty);

    public static void SetTastaturfokus(DependencyObject element, bool wert) => Pruefen(element).SetValue(TastaturfokusProperty, wert);

    /// <summary>
    /// Etwas läuft (Aufnahme, Senden, Verbinden): Knöpfe zeigen dann die kreisende <see cref="Lichtkante"/>. Der Host setzt es
    /// und nimmt es wieder weg; gesperrt zeigt der Knopf nichts.
    /// </summary>
    public static readonly DependencyProperty LaeuftProperty = DependencyProperty.RegisterAttached(
        "Laeuft", typeof(bool), typeof(Glas), new FrameworkPropertyMetadata(false));

    public static bool GetLaeuft(DependencyObject element) => (bool)Pruefen(element).GetValue(LaeuftProperty);

    public static void SetLaeuft(DependencyObject element, bool wert) => Pruefen(element).SetValue(LaeuftProperty, wert);

    /// <summary>Das Element hinter einer Milchglasplatte (Stil Glas.Milchglas), das unscharf durchscheint; ein Geschwister, kein Elternteil.</summary>
    public static readonly DependencyProperty MilchglasQuelleProperty = DependencyProperty.RegisterAttached(
        "MilchglasQuelle", typeof(Visual), typeof(Glas), new FrameworkPropertyMetadata(null));

    public static Visual? GetMilchglasQuelle(DependencyObject element) => (Visual?)Pruefen(element).GetValue(MilchglasQuelleProperty);

    public static void SetMilchglasQuelle(DependencyObject element, Visual? wert) => Pruefen(element).SetValue(MilchglasQuelleProperty, wert);

    /// <summary>Das Element hinter einem Flüssigglas (Stil Glas.Fluessigglas), das am Rand gebrochen durchscheint; ein Geschwister.</summary>
    public static readonly DependencyProperty FluessigglasQuelleProperty = DependencyProperty.RegisterAttached(
        "FluessigglasQuelle", typeof(Visual), typeof(Glas), new FrameworkPropertyMetadata(null));

    public static Visual? GetFluessigglasQuelle(DependencyObject element) => (Visual?)Pruefen(element).GetValue(FluessigglasQuelleProperty);

    public static void SetFluessigglasQuelle(DependencyObject element, Visual? wert) => Pruefen(element).SetValue(FluessigglasQuelleProperty, wert);

    /// <summary>
    /// Machart des Fortschritts (Stil Glas.Fortschritt, <see cref="Glasdesign.Fortschrittart"/>): Glasfuellung (Vorgabe),
    /// Lichtlinie oder Segmente. Am ProgressBar setzen.
    /// </summary>
    public static readonly DependencyProperty FortschrittartProperty = DependencyProperty.RegisterAttached(
        "Fortschrittart", typeof(Fortschrittart), typeof(Glas), new FrameworkPropertyMetadata(Glasdesign.Fortschrittart.Glasfuellung));

    public static Fortschrittart GetFortschrittart(DependencyObject element) => (Fortschrittart)Pruefen(element).GetValue(FortschrittartProperty);

    public static void SetFortschrittart(DependencyObject element, Fortschrittart wert) => Pruefen(element).SetValue(FortschrittartProperty, wert);

    /// <summary>
    /// Machart des Fensterrahmens (Stile Glas.Fenster und Glas.Fensterrahmen, <see cref="Glasdesign.Rahmenart"/>): Glasleiste
    /// (Vorgabe) oder Schlicht. Am Window setzen, der Stil reicht sie an den <see cref="Fensterrahmen"/> weiter.
    /// </summary>
    public static readonly DependencyProperty RahmenartProperty = DependencyProperty.RegisterAttached(
        "Rahmenart", typeof(Rahmenart), typeof(Glas), new FrameworkPropertyMetadata(Glasdesign.Rahmenart.Glasleiste));

    public static Rahmenart GetRahmenart(DependencyObject element) => (Rahmenart)Pruefen(element).GetValue(RahmenartProperty);

    public static void SetRahmenart(DependencyObject element, Rahmenart wert) => Pruefen(element).SetValue(RahmenartProperty, wert);

    /// <summary>Machart der matten Knöpfe (<see cref="Glasdesign.Mattart"/>); setzen die Stile Glas.Matt.*.</summary>
    public static readonly DependencyProperty MattartProperty = DependencyProperty.RegisterAttached(
        "Mattart", typeof(Mattart), typeof(Glas), new FrameworkPropertyMetadata(Glasdesign.Mattart.Flaeche));

    public static Mattart GetMattart(DependencyObject element) => (Mattart)Pruefen(element).GetValue(MattartProperty);

    public static void SetMattart(DependencyObject element, Mattart wert) => Pruefen(element).SetValue(MattartProperty, wert);

    /// <summary>
    /// Form der Grenze des Himmelsglanzes (Stile Glas.Horizontglas.*, <see cref="Glasdesign.Horizontglanz"/>): Gerade
    /// (Vorgabe), Ellipse, Welle, Bogen oder Schraeg. Am Knopf setzen.
    /// </summary>
    public static readonly DependencyProperty HorizontglanzProperty = DependencyProperty.RegisterAttached(
        "Horizontglanz", typeof(Horizontglanz), typeof(Glas), new FrameworkPropertyMetadata(Glasdesign.Horizontglanz.Gerade));

    public static Horizontglanz GetHorizontglanz(DependencyObject element) => (Horizontglanz)Pruefen(element).GetValue(HorizontglanzProperty);

    public static void SetHorizontglanz(DependencyObject element, Horizontglanz wert) => Pruefen(element).SetValue(HorizontglanzProperty, wert);

    /// <summary>
    /// Füllung eines Knopfs im Chromrahmen (Stil Glas.Chromrahmen.Knopf, <see cref="Glasdesign.Fuellung"/>): Hell (Vorgabe),
    /// Silber, Schwarz, RauchHell, Rauch, RauchDunkel. Rauchglas lässt den Grund durchscheinen.
    /// </summary>
    public static readonly DependencyProperty FuellungProperty = DependencyProperty.RegisterAttached(
        "Fuellung", typeof(Fuellung), typeof(Glas), new FrameworkPropertyMetadata(Glasdesign.Fuellung.Hell));

    public static Fuellung GetFuellung(DependencyObject element) => (Fuellung)Pruefen(element).GetValue(FuellungProperty);

    public static void SetFuellung(DependencyObject element, Fuellung wert) => Pruefen(element).SetValue(FuellungProperty, wert);

    /// <summary>
    /// Ein Zeichen rechts im Feld (Stil Glas.Dickglas.Textfeld), etwa <c>Lupe</c> für ein Suchfeld; Keins (Vorgabe) zeigt
    /// keines. Das Zeichen ist Schmuck, kein Knopf.
    /// </summary>
    public static readonly DependencyProperty FeldsymbolProperty = DependencyProperty.RegisterAttached(
        "Feldsymbol", typeof(Glaszeichen), typeof(Glas), new FrameworkPropertyMetadata(Glaszeichen.Keins));

    public static Glaszeichen GetFeldsymbol(DependencyObject element) => (Glaszeichen)Pruefen(element).GetValue(FeldsymbolProperty);

    public static void SetFeldsymbol(DependencyObject element, Glaszeichen wert) => Pruefen(element).SetValue(FeldsymbolProperty, wert);

    /// <summary>Ebene der Fläche eines Taktil-Knopfs (<see cref="Glasdesign.Taktilebene"/>); setzen die Stile Glas.Taktil.*.</summary>
    public static readonly DependencyProperty TaktilebeneProperty = DependencyProperty.RegisterAttached(
        "Taktilebene", typeof(Taktilebene), typeof(Glas), new FrameworkPropertyMetadata(Glasdesign.Taktilebene.Gehoben));

    public static Taktilebene GetTaktilebene(DependencyObject element) => (Taktilebene)Pruefen(element).GetValue(TaktilebeneProperty);

    public static void SetTaktilebene(DependencyObject element, Taktilebene wert) => Pruefen(element).SetValue(TaktilebeneProperty, wert);

    /// <summary>Eckradius in DIP für die Stile der Stilfamilien 2 (etwa Glas.Dickglas.Knopf als Rechteck); NaN (Vorgabe) nimmt <see cref="RundungProperty"/>.</summary>
    public static readonly DependencyProperty EckeProperty = DependencyProperty.RegisterAttached(
        "Ecke", typeof(double), typeof(Glas), new FrameworkPropertyMetadata(double.NaN));

    public static double GetEcke(DependencyObject element) => (double)Pruefen(element).GetValue(EckeProperty);

    public static void SetEcke(DependencyObject element, double wert) => Pruefen(element).SetValue(EckeProperty, wert);

    /// <summary>Was ein Knopf der Stilfamilie Glanzkugel trägt (<see cref="Glasdesign.Kugelart"/>); setzen die Stile Glas.Glanzkugel.*.</summary>
    public static readonly DependencyProperty KugelartProperty = DependencyProperty.RegisterAttached(
        "Kugelart", typeof(Kugelart), typeof(Glas), new FrameworkPropertyMetadata(Glasdesign.Kugelart.Kuppel));

    public static Kugelart GetKugelart(DependencyObject element) => (Kugelart)Pruefen(element).GetValue(KugelartProperty);

    public static void SetKugelart(DependencyObject element, Kugelart wert) => Pruefen(element).SetValue(KugelartProperty, wert);

    /// <summary>Breite des Chromrings als Anteil der kürzeren Seite (Stile Glas.Glanzkugel.*); 0 heißt ohne Ring.</summary>
    public static readonly DependencyProperty RinganteilProperty = DependencyProperty.RegisterAttached(
        "Ringanteil", typeof(double), typeof(Glas), new FrameworkPropertyMetadata(0d));

    public static double GetRinganteil(DependencyObject element) => (double)Pruefen(element).GetValue(RinganteilProperty);

    public static void SetRinganteil(DependencyObject element, double wert) => Pruefen(element).SetValue(RinganteilProperty, wert);

    /// <summary>
    /// Die Schrift, die ein Körper mit eigener Schriftwahl (Horizontglas, Glanzkugel, Chromrahmen; seit 0.5.0-beta.7 jeder
    /// Familienkörper mit <see cref="Familienkoerper.SchriftAnSteuerelement"/>) nach Kontrast gewählt hat; er setzt sie an den
    /// Knopf, dessen Vorlage ihn trägt. Die Stile binden Foreground daran: So bekommen auch Zeichen als
    /// Inhalt (<see cref="Glassymbol"/>), die ihre Farbe über den Knopf erben und nicht über den ContentPresenter, die
    /// gewählte Schrift.
    /// </summary>
    public static readonly DependencyProperty KontrastschriftProperty = DependencyProperty.RegisterAttached(
        "Kontrastschrift", typeof(Brush), typeof(Glas), new FrameworkPropertyMetadata(null));

    public static Brush? GetKontrastschrift(DependencyObject element) => (Brush?)Pruefen(element).GetValue(KontrastschriftProperty);

    public static void SetKontrastschrift(DependencyObject element, Brush? wert) => Pruefen(element).SetValue(KontrastschriftProperty, wert);

    /// <summary>
    /// Schriftwahl (0.5.0-beta.7): die bevorzugte Schrift eines Steuerelements oder Zeichens, in den Stilen der Stilfamilien
    /// die Schrift der Farbstellung. Der Körper der Vorlage prüft sie auf der Fläche, auf der sie steht, und gibt sie als
    /// <see cref="KontrastschriftProperty"/> weiter, oder die Kontrastschrift, wenn sie dort keine 4,5 zu 1 (gesperrt 3 zu 1)
    /// hält (<see cref="Schriftwahl"/>). Die Stile setzen deshalb Schriftwunsch statt Foreground; ein eigenes Foreground am
    /// Steuerelement gilt weiter und schaltet die Wahl ab.
    /// </summary>
    public static readonly DependencyProperty SchriftwunschProperty = DependencyProperty.RegisterAttached(
        "Schriftwunsch", typeof(Brush), typeof(Glas), new FrameworkPropertyMetadata(null));

    public static Brush? GetSchriftwunsch(DependencyObject element) => (Brush?)Pruefen(element).GetValue(SchriftwunschProperty);

    public static void SetSchriftwunsch(DependencyObject element, Brush? wert) => Pruefen(element).SetValue(SchriftwunschProperty, wert);

    /// <summary>
    /// Quelle einer abgeleiteten Farbe oder eines abgeleiteten Maßes (0.5.0-beta.7): ein Wert der Farbstellung
    /// (DynamicResource: Color, Pinsel oder Zahl) an einem Element der Vorlage, aus dem eine Bindung mit
    /// <see cref="Farbableitung"/> oder <see cref="Glasumrechnung.Kartenecke"/> die Farbe oder das Maß eines Teils macht. So
    /// folgt das Teil der Familienfarbe, ohne eigenen Schlüssel.
    /// </summary>
    public static readonly DependencyProperty AbleitungsquelleProperty = DependencyProperty.RegisterAttached(
        "Ableitungsquelle", typeof(object), typeof(Glas), new FrameworkPropertyMetadata(null));

    public static object? GetAbleitungsquelle(DependencyObject element) => Pruefen(element).GetValue(AbleitungsquelleProperty);

    public static void SetAbleitungsquelle(DependencyObject element, object? wert) => Pruefen(element).SetValue(AbleitungsquelleProperty, wert);

    public static Brush? GetAkzentfarbe(DependencyObject element) => (Brush?)Pruefen(element).GetValue(AkzentfarbeProperty);

    public static void SetAkzentfarbe(DependencyObject element, Brush? wert) => Pruefen(element).SetValue(AkzentfarbeProperty, wert);

    public static double GetRundung(DependencyObject element) => (double)Pruefen(element).GetValue(RundungProperty);

    public static void SetRundung(DependencyObject element, double wert) => Pruefen(element).SetValue(RundungProperty, wert);

    public static Farbglasart GetFarbglasart(DependencyObject element) => (Farbglasart)Pruefen(element).GetValue(FarbglasartProperty);

    public static void SetFarbglasart(DependencyObject element, Farbglasart wert) => Pruefen(element).SetValue(FarbglasartProperty, wert);

    public static Schattenstufe GetSchatten(DependencyObject element) => (Schattenstufe)Pruefen(element).GetValue(SchattenProperty);

    public static void SetSchatten(DependencyObject element, Schattenstufe wert) => Pruefen(element).SetValue(SchattenProperty, wert);

    public static Zustandvorschau GetZustandVorschau(DependencyObject element) => (Zustandvorschau)Pruefen(element).GetValue(ZustandVorschauProperty);

    public static void SetZustandVorschau(DependencyObject element, Zustandvorschau wert) => Pruefen(element).SetValue(ZustandVorschauProperty, wert);

    public static Glasform GetForm(DependencyObject element) => (Glasform)Pruefen(element).GetValue(FormProperty);

    public static void SetForm(DependencyObject element, Glasform wert) => Pruefen(element).SetValue(FormProperty, wert);

    public static Glasart GetArt(DependencyObject element) => (Glasart)Pruefen(element).GetValue(ArtProperty);

    public static void SetArt(DependencyObject element, Glasart wert) => Pruefen(element).SetValue(ArtProperty, wert);

    public static Color GetLeuchtfarbe(DependencyObject element) => (Color)Pruefen(element).GetValue(LeuchtfarbeProperty);

    public static void SetLeuchtfarbe(DependencyObject element, Color wert) => Pruefen(element).SetValue(LeuchtfarbeProperty, wert);

    public static bool? GetLeuchtet(DependencyObject element) => (bool?)Pruefen(element).GetValue(LeuchtetProperty);

    public static void SetLeuchtet(DependencyObject element, bool? wert) => Pruefen(element).SetValue(LeuchtetProperty, wert);

    public static bool GetWartet(DependencyObject element) => (bool)Pruefen(element).GetValue(WartetProperty);

    public static void SetWartet(DependencyObject element, bool wert) => Pruefen(element).SetValue(WartetProperty, wert);

    public static Geometry? GetLeuchtzeichen(DependencyObject element) => (Geometry?)Pruefen(element).GetValue(LeuchtzeichenProperty);

    public static void SetLeuchtzeichen(DependencyObject element, Geometry? wert) => Pruefen(element).SetValue(LeuchtzeichenProperty, wert);

    private static DependencyObject Pruefen(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element;
    }
}
