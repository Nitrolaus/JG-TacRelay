// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Funk.Client.Ablage;
using Funk.Client.Modelle;
using Funk.ClientKern;

namespace Funk.Client.Fenster;

/// <summary>
/// Hauptfenster (A25): minimierbar, frei skalierbar, eigene Titelleiste, Tastaturbedienung. Die Navigation links zeigt ihre
/// Namen immer (Q7, seit 0.6.0; bis 0.5.x schrumpfte sie unter 820 Einheiten auf Symbole). Unten sitzt seit 0.10.0 die Statuszeile
/// "Mikrofon" (Q87), die eine Stelle für Pegel und SENDET; bis 0.9.3 stand dort die Leiste der Sprachaktivierung, deren Bereich darunter
/// aufklappte. Die Sprachaktivierung ist seit 0.10.0 nur noch eine Seite (Unterpunkt unter Einstellungen), ihr Bereich steht fest dort.
/// In schmalen Fenstern zeigt die Titelleiste weniger (E23, <see cref="TitelleisteSetzen"/>). Die Spalte "Wer ist da" verbreitert seit Q93 a das
/// Fenster in Klein oder liegt dort als Schublade über Start (<see cref="SpalteAnordnen"/>).
/// </summary>
public partial class Hauptfenster : Window
{
    /// <summary>Unter dieser Fensterbreite zeigen "Wer ist da" und "Funkfeld" in der Titelleiste nur Zeichen und Zahl (E23).</summary>
    public const double TitelKnoepfeKurzUnter = 900;

    /// <summary>Unter dieser Fensterbreite fällt "spricht: ..." weg, und der Pegel der Statuszeile wird kürzer (E23).</summary>
    public const double SehrSchmalUnter = 700;

    /// <summary>So viel darf die gemessene Breite unter einer Grenze liegen und gilt doch als genau dort (Rundung des Layouts, E23).</summary>
    private const double RundungsSpiel = Fenstergroessen.RundungsSpiel;

    private readonly HauptViewModel _modell;
    private readonly Einstellungsablage _ablage;
    private readonly IBildschirme _bildschirme;
    private readonly Fenstergroessen _groessen;

    /// <summary>
    /// Musste ein Klassenwechsel das Fenster nach oben oder links schieben, die Ecke davor und die danach (Bildpunkte): Steht
    /// es beim nächsten Wechsel noch an der geschobenen Stelle, geht es von der alten aus, kommt also nach dem Wechsel zurück auf
    /// die kleinere Klasse wieder dorthin zurück. Verschiebst du es selbst, gilt deine Stelle.
    /// </summary>
    private (int Links, int Oben)? _anker;

    private (int Links, int Oben)? _geschoben;

    /// <summary>Die Klasse wechselte, während das Fenster maximiert oder minimiert war; ihre Größe kommt beim Wiederherstellen.</summary>
    private bool _wechselOffen;

    /// <summary>
    /// Q93 a: Das Fenster ist für die offene Spalte "Wer ist da" breiter geworden; der Platz davor und danach, dazu <see cref="_anker"/> und
    /// <see cref="_geschoben"/> von davor. Weg, sobald die Spalte zugeht oder der Bereich wechselt.
    /// </summary>
    private Verbreiterung? _verbreitert;

    private sealed record Verbreiterung(Platz Vorher, Platz Nachher, (int Links, int Oben)? Anker, (int Links, int Oben)? Geschoben);

    /// <param name="bildschirme">Monitore; ohne Angabe die echten (nur gelesen). Tests geben eine feste Liste.</param>
    /// <param name="bisher">
    /// Beim Neuaufbau für eine Farbstellung (A31) der Platz des alten Fensters, genau so, auch wenn er nicht gemerkt ist
    /// (etwa per Win+Pfeil eingerastet). Gemerkt wird er dadurch nicht.
    /// </param>
    public Hauptfenster(HauptViewModel modell, Einstellungsablage ablage, IBildschirme? bildschirme = null, FensterOrt? bisher = null)
    {
        ArgumentNullException.ThrowIfNull(modell);
        ArgumentNullException.ThrowIfNull(ablage);
        InitializeComponent();
        _modell = modell;
        _ablage = ablage;
        _bildschirme = bildschirme ?? WindowsBildschirme.Instanz;
        DataContext = modell;
        // Fassung aus der Assembly (Directory.Build.props): in Taskleiste und Alt+Tab, und klein in der eigenen Titelleiste.
        Title = "JG TacRelay " + FunkClientOptionen.Fassung;
        FassungText.Text = FunkClientOptionen.Fassung;
        Fensterrahmen.Einrichten(this, minimierbar: true);
        // A32: Lage, Größe und Maximiert, so wie du das Fenster zuletzt eingestellt hast; die Größe je Klasse (Fenstergroessen).
        Platzfuehrung = new Platzfuehrung(this, Gemerkt, mitZustand: true);
        var alle = _bildschirme.Alle();
        var einstellungen = ablage.Aktuell;
        var gemerkt = alle.Count == 0 ? einstellungen.HauptfensterOrt : einstellungen.HauptfensterFuer(Platzierung.Haupt(alle).Skala);
        // Umstieg aus einer Datei vor 0.6.0 (keine Größen je Klasse): Die damalige Größe des Hauptfensters wird einmal die der
        // Seiten. Das Fenster selbst öffnet auf der Startseite in deren Größe und übernimmt nur die Lage (Stufe 2b; bis Stufe 2 kam
        // mit dem gemerkten Bereich, etwa Netze, die alte große Größe mit, so in Sarevocs erstem Test der Vorab-Fassung).
        var umstieg = einstellungen.HauptfensterGroessen is null && gemerkt is not null;
        var seiteAlt = umstieg && gemerkt is { MitGroesse: true } g ? new Size(g.Breite!.Value, g.Hoehe!.Value) : (Size?)null;
        einstellungen.HauptfensterGroessen ??= new Dictionary<string, Fenstergroesse>(StringComparer.Ordinal);
        _groessen = new Fenstergroessen(einstellungen.HauptfensterGroessen, seiteAlt);
        if (umstieg) modell.AktiverBereich = modell.Bereiche[0];
        Klasse = Fenstergroessen.KlasseFuer(modell.AktiverBereich?.Kennung);
        PlatzAnwenden(alle, bisher, umstieg ? new FensterOrt(gemerkt!.Links, gemerkt.Oben) : gemerkt, umstieg);
        // Q93 a: Der Schatten der Schublade steht links neben der Spalte "Wer ist da".
        WerIstDaSchatten.Margin = new Thickness(0, 0, Fenstergroessen.WerIstDaBreite, 0);
        // Die Breite des Inhalts, nicht die des Fensters: Im Bildlauf der Tests ordnet nur der Inhalt an, das Fenster selbst bleibt ohne Größe.
        Wurzelraster.SizeChanged += (_, e) =>
        {
            var breite = e.NewSize.Width + BorderThickness.Left + BorderThickness.Right;
            TitelleisteSetzen(breite);
            SpalteAnordnen(breite);
        };
        StateChanged += (_, _) => ZustandAnwenden();
        // Zurück aus einem anderen Fenster (etwa nach dem Einstecken des Sticks): Serverprofil neu prüfen. Seit 0.10.0 wartet der Schritt
        // Sprechtaste beim ersten Start nur, solange das Fenster vorn ist (E8).
        Activated += (_, _) => _modell.FensterAktiviert();
        Deactivated += (_, _) => _modell.FensterDeaktiviert();
        Loaded += (_, _) =>
        {
            MaxKnopfSetzen();
            FokusSetzen();
        };
        _modell.PropertyChanged += ModellGeaendert;
        _modell.SchwelleFokussieren += GrafikFokussieren;
        _modell.WerIstDa.PropertyChanged += WerIstDaGeaendert;
        Closed += (_, _) =>
        {
            _modell.PropertyChanged -= ModellGeaendert;
            _modell.SchwelleFokussieren -= GrafikFokussieren;
            _modell.WerIstDa.PropertyChanged -= WerIstDaGeaendert;
        };
        // Ist die Spalte beim Aufbau schon offen (Neuaufbau für eine Farbstellung, Tests), wird nichts verbreitert; in Klein liegt sie dann als
        // Schublade über Start, bis das Fenster breit genug ist.
        SpalteAnordnen(Width);
    }

    /// <summary>Lage und Größe (A32).</summary>
    internal Platzfuehrung Platzfuehrung { get; }

    /// <summary>Die Größenklasse jetzt (nach dem Bereich, <see cref="Fenstergroessen.KlasseFuer"/>).</summary>
    internal Groessenklasse Klasse { get; private set; }

    /// <summary>Die Titelleiste steht gerade schmal: "Wer ist da" und "Funkfeld" nur mit Zeichen und Zahl (Tests).</summary>
    internal bool TitelKurz { get; private set; }

    /// <summary>"spricht: ..." ist gerade weggefallen, und der Pegel der Statuszeile ist kürzer (Tests).</summary>
    internal bool SehrSchmal { get; private set; }

    /// <summary>Die Spalte "Wer ist da" liegt gerade als Schublade über Start (Q93 a, Tests).</summary>
    internal bool Schublade { get; private set; }

    /// <summary>Das Fenster ist gerade für die Spalte "Wer ist da" verbreitert und geht beim Schließen zurück, wenn es so steht (Q93 a, Tests).</summary>
    internal bool Verbreitert => _verbreitert is not null;

    /// <summary>
    /// Platz beim Aufbau: der des alten Fensters beim Neuaufbau samt Größe, sonst die gemerkte Lage mit der Größe der Klasse,
    /// auf einem vorhandenen Monitor und höchstens so groß wie dessen Arbeitsbereich (<see cref="Platzierung.Wiederherstellen"/>),
    /// sonst mittig auf dem Hauptmonitor. Beim <paramref name="umstieg"/> von 0.5.x bleibt die linke obere Ecke, und das Fenster
    /// rückt ganz in den Arbeitsbereich (<see cref="Fenstergroessen.Wechsel"/>); maximiert wird es dabei nicht.
    /// </summary>
    private void PlatzAnwenden(IReadOnlyList<Bildschirm> alle, FensterOrt? bisher, FensterOrt? gemerkt, bool umstieg)
    {
        var groesse = _groessen.Groesse(Klasse);
        if (alle.Count == 0)
        {
            MindestSetzen(null);
            (Width, Height) = (groesse.Width, groesse.Height);
            return;
        }
        var mindest = Fenstergroessen.Mindest(Klasse);
        var platz = FensterOrt.OderNull(bisher) is { } b
            ? Platzierung.Wiederherstellen(b, groesse, alle, mindest)
            : gemerkt is { } ort
                ? umstieg
                    ? Fenstergroessen.Wechsel(ort, groesse, alle, mindest)
                    : Platzierung.Wiederherstellen(ort with { Breite = groesse.Width, Hoehe = groesse.Height }, groesse, alle, mindest)
                : Platzierung.Mittig(groesse, Platzierung.Haupt(alle), mindest);
        MindestSetzen(platz.Bildschirm);
        Platzfuehrung.Setzen(platz);
    }

    /// <summary>
    /// Was du selbst tust (Platzfuehrung): Lage und Maximiert für jede Klasse, eine am Rand gezogene Größe für die Klasse jetzt.
    /// Hast du das Fenster verschoben, gilt deine Stelle, nicht mehr die vor einem Schieben beim Klassenwechsel. Steht die Spalte "Wer ist da"
    /// in einem für sie verbreiterten Fenster, wird der Platz ohne die Spalte gemerkt (Q93 a, <see cref="Fenstergroessen.OhneSpalte"/>): Der
    /// nächste Start beginnt nicht breit.
    /// </summary>
    private void Gemerkt(FensterOrt ort, bool groesseGeaendert)
    {
        var merken = _verbreitert is { } v ? Fenstergroessen.OhneSpalte(ort, v.Vorher, v.Nachher) : ort;
        _ablage.Aktuell.HauptfensterMerken(merken);
        if (groesseGeaendert && !merken.Maximiert && merken.MitGroesse) _groessen.Merken(Klasse, merken.Breite!.Value, merken.Hoehe!.Value);
        if (_geschoben != (ort.Links, ort.Oben)) (_anker, _geschoben) = (null, null);
        _ablage.Vormerken();
    }

    /// <summary>Mindestgröße der Klasse, höchstens der Arbeitsbereich von <paramref name="b"/> (<see cref="Fenstergroessen.Mindest(Groessenklasse, Bildschirm?)"/>).</summary>
    private void MindestSetzen(Bildschirm? b)
    {
        var m = Fenstergroessen.Mindest(Klasse, b);
        (MinWidth, MinHeight) = (m.Width, m.Height);
    }

    /// <summary>Nach einem Wechsel des Bereichs: neue Klasse, neue Mindestgröße, ihre Größe.</summary>
    private void KlasseAnwenden()
    {
        var neu = Fenstergroessen.KlasseFuer(_modell.AktiverBereich?.Kennung);
        if (neu == Klasse) return;
        Klasse = neu;
        GroesseAnwenden();
    }

    /// <summary>
    /// Setzt die Größe der Klasse, mit der linken oberen Ecke von jetzt (oder der vor dem letzten Schieben) und so weit nach
    /// links und oben gerückt wie nötig (<see cref="Fenstergroessen.Wechsel"/>). Maximiert oder minimiert bleibt die Größe in
    /// Ruhe (RestoreBounds); sie kommt beim Wiederherstellen. Kommt Klein mit offener Spalte "Wer ist da", kommt es gleich verbreitert
    /// (Q93 a), in einem Schritt; eine Verbreiterung von vorher gilt nach dem Wechsel nicht mehr.
    /// </summary>
    private void GroesseAnwenden()
    {
        _verbreitert = null;
        var alle = _bildschirme.Alle();
        var jetzt = Platzfuehrung.Aktuell();
        var start = _anker is { } a && _geschoben == (jetzt.Links, jetzt.Oben) ? new FensterOrt(a.Links, a.Oben) : jetzt;
        // Die Mindestgröße zuerst, auf dem Monitor, auf dem der Wechsel rechnet: Eine größere MinHeight von vorher hielte das
        // Fenster sonst über dem Arbeitsbereich. Auch maximiert, denn auch dort zählt sie.
        MindestSetzen(alle.Count == 0 ? null : Platzierung.Am(start.Links, start.Oben, alle));
        _wechselOffen = WindowState != WindowState.Normal;
        if (_wechselOffen)
        {
            SpalteAnordnen(Fensterbreite());
            return;
        }
        var groesse = _groessen.Groesse(Klasse);
        if (alle.Count == 0)
        {
            (Width, Height) = (groesse.Width, groesse.Height);
            SpalteAnordnen(Width);
            return;
        }
        var p = Fenstergroessen.Wechsel(start, groesse, alle, Fenstergroessen.Mindest(Klasse));
        var geschoben = p.Links != start.Links || p.Oben != start.Oben;
        _anker = geschoben ? (start.Links, start.Oben) : null;
        _geschoben = geschoben ? (p.Links, p.Oben) : null;
        if (Klasse == Groessenklasse.Klein && _modell.WerIstDa.InSpalte
            && Fenstergroessen.SpalteOeffnen(new FensterOrt(p.Links, p.Oben, p.Breite, p.Hoehe), alle, p.Bildschirm.Skala).Breiter is { } breiter)
            Verbreitern(p, breiter);
        else
            Platzfuehrung.Setzen(p);
        SpalteAnordnen(Width);
    }

    /// <summary>
    /// Nach Maximieren, Minimieren oder Wiederherstellen (StateChanged; Tests rufen es direkt, ohne Fensterhandle kommt das
    /// Ereignis nicht): Knopf in der Titelleiste, und wechselte die Klasse in der Zwischenzeit, jetzt ihre Größe. Maximiert steht die
    /// Spalte "Wer ist da" wie bisher neben dem Inhalt, nie als Schublade (Q93 a).
    /// </summary>
    internal void ZustandAnwenden()
    {
        MaxKnopfSetzen();
        if (_wechselOffen && WindowState == WindowState.Normal) GroesseAnwenden();
        else SpalteAnordnen(Fensterbreite());
    }

    /// <summary>Q93 a: Die Spalte "Wer ist da" geht auf oder zu (Knopf, Strg+6, ihr eigenes Schließen).</summary>
    private void WerIstDaGeaendert(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(WerIstDaViewModel.Offen) or nameof(WerIstDaViewModel.Geloest))) return;
        if (_modell.WerIstDa.InSpalte) SpalteOeffnen();
        else SpalteSchliessen();
        LoesungAnwenden();
    }

    private WerIstDaFenster? _werIstDaFenster;

    /// <summary>
    /// Wer ist da als eigenes Fenster (seit 0.11.5, Sarevoc 14.10.2026: "wenn möglich auch als Popup und eigenes Fenster ablösbar"): offen und gelöst
    /// zeigt es <see cref="WerIstDaFenster"/>, sonst schließt es; wer das Fenster schließt, legt die Liste zurück in die Spalte.
    /// </summary>
    private void LoesungAnwenden()
    {
        var w = _modell.WerIstDa;
        if (w.Offen && w.Geloest)
        {
            if (_werIstDaFenster is not null) return;
            var f = new WerIstDaFenster(w, this);
            f.Closed += (_, _) =>
            {
                _werIstDaFenster = null;
                w.Geloest = false;
            };
            _werIstDaFenster = f;
            f.Show();
        }
        else
        {
            _werIstDaFenster?.Close();
        }
    }

    /// <summary>
    /// Q93 a (Sarevoc 12.10.2026): Geht die Spalte in Klein auf und ist das Fenster schmaler als <see cref="Fenstergroessen.WerIstDaDanebenAb"/>,
    /// wird es um die Spalte breiter, nach rechts oder nach links ausweichend (<see cref="Fenstergroessen.SpalteOeffnen"/>, auf dem Monitor, auf
    /// dem es überwiegend steht, mit seiner Skala), in einem Schritt; Start behält seine Breite. Ohne Platz auf dem Monitor bleibt es, und die
    /// Spalte liegt als Schublade über Start. Maximiert wird der Platz des Normalzustands verbreitert, ohne dass man etwas sieht: Das
    /// Wiederherstellen bringt dann Klein mit der Spalte daneben in einem Schritt (Befund der Prüfung; bis dahin kam Klein 900 mit der Schublade,
    /// obwohl Platz war). Wechselte die Klasse, während das Fenster maximiert war, bringt das Wiederherstellen ihre Größe samt Verbreitern
    /// (<see cref="GroesseAnwenden"/>). Minimiert (dort lässt sich die Spalte nicht bedienen) und in den übrigen Bereichen wie bisher. Das Fenster
    /// bekommt nur Lage und Größe, es wird nicht aktiviert und nimmt keinen Fokus.
    /// </summary>
    private void SpalteOeffnen()
    {
        _verbreitert = null;
        var alle = _bildschirme.Alle();
        if (Klasse == Groessenklasse.Klein && WindowState != WindowState.Minimized && !_wechselOffen && alle.Count > 0)
        {
            var jetzt = Platzfuehrung.Aktuell();
            if (Fenstergroessen.SpalteOeffnen(jetzt, alle, Platzfuehrung.Skala).Breiter is { } breiter)
                Verbreitern(new Platz(jetzt.Links, jetzt.Oben, jetzt.Breite!.Value, jetzt.Hoehe!.Value, breiter.Bildschirm), breiter);
        }
        SpalteAnordnen(WindowState == WindowState.Normal && _verbreitert is { } v ? v.Nachher.Breite : Fensterbreite());
    }

    /// <summary>
    /// Setzt den verbreiterten Platz in einem Schritt (<see cref="Platzfuehrung.Setzen"/>) und merkt den davor samt Anker. Weicht das Fenster
    /// dabei nach links aus, gilt wie beim Schieben eines Klassenwechsels die Ecke davor als Anker: Wechselt der Bereich, geht es von dort aus weiter.
    /// </summary>
    private void Verbreitern(Platz vorher, Platz breiter)
    {
        _verbreitert = new Verbreiterung(vorher, breiter, _anker, _geschoben);
        Platzfuehrung.Setzen(breiter, einSchritt: true);
        if (breiter.Links == vorher.Links && breiter.Oben == vorher.Oben) return;
        _anker = _anker is { } a && _geschoben == (vorher.Links, vorher.Oben) ? a : (vorher.Links, vorher.Oben);
        _geschoben = (breiter.Links, breiter.Oben);
    }

    /// <summary>
    /// Q93 a: Geht die Spalte zu und hat das Öffnen das Fenster verbreitert, kommt es auf Breite und Lage davor zurück, in einem Schritt; nur
    /// wenn es im Normalzustand noch genau so steht (<see cref="Fenstergroessen.Unveraendert"/>). Hast du es in der Zwischenzeit verschoben oder
    /// gezogen, bleibt es, wie du es hast. Maximiert geht der Platz des Normalzustands zurück, ohne dass man etwas sieht; das Wiederherstellen
    /// bringt dann Klein von vorher in einem Schritt (Befund der Prüfung: bis dahin blieb das Fenster danach breit).
    /// </summary>
    private void SpalteSchliessen()
    {
        if (_verbreitert is { } v)
        {
            _verbreitert = null;
            if (WindowState != WindowState.Minimized && Fenstergroessen.Unveraendert(Platzfuehrung.Aktuell(), v.Nachher))
            {
                Platzfuehrung.Setzen(v.Vorher, einSchritt: true);
                (_anker, _geschoben) = (v.Anker, v.Geschoben);
            }
        }
        SpalteAnordnen(Fensterbreite());
    }

    /// <summary>
    /// Q93 a: Die Spalte "Wer ist da" steht neben dem Inhalt (Spalte 2 des Rasters, wie bisher) oder liegt als Schublade über Start: in Klein,
    /// nicht maximiert, wenn das Fenster schmaler ist als <see cref="Fenstergroessen.WerIstDaDanebenAb"/> (kein Platz zum Verbreitern, oder du
    /// hast es danach schmaler gezogen). Dann steht sie rechts in Spalte 1 über dem Inhalt, mit dem Schatten <c>WerIstDaSchatten</c> an ihrer
    /// linken Kante, und Start behält die ganze Breite; Spalte 2 bleibt leer. Der Platz der Seiten bekommt dann <see cref="UnterSchubladeProperty"/>:
    /// Die verdeckte Spalte Hören von Start geht aus Tab-Kreis und Pfeilnavigation, und ein Fokus darin wandert in die Schublade
    /// (<see cref="FokusAusVerdecktem"/>). Gerufen beim Öffnen und Schließen, beim Wechsel von Bereich oder Zustand und bei jeder neuen Breite;
    /// umgesetzt nur bei einem Wechsel.
    /// </summary>
    private void SpalteAnordnen(double breite)
    {
        var schublade = Klasse == Groessenklasse.Klein && WindowState == WindowState.Normal && _modell.WerIstDa.InSpalte
            && double.IsFinite(breite) && breite > 0 && breite + RundungsSpiel < Fenstergroessen.WerIstDaDanebenAb;
        if (schublade == Schublade) return;
        Schublade = schublade;
        Grid.SetColumn(WerIstDaSpalte, schublade ? 1 : 2);
        WerIstDaSpalte.HorizontalAlignment = schublade ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        Zeigen(WerIstDaSchatten, schublade);
        if (schublade) SetUnterSchublade(SeitenPlatz, true);
        else SeitenPlatz.ClearValue(UnterSchubladeProperty);
        if (schublade) FokusAusVerdecktem();
    }

    /// <summary>
    /// Q93 a (Befund der Prüfung): wahr auf dem Platz der Seiten (<c>SeitenPlatz</c>) und, geerbt, auf allem darin, solange die Spalte
    /// "Wer ist da" als Schublade über Start liegt. Die Spalte Hören von Start (<c>HoerenSpalte</c> in Bereiche.xaml), die dann unter der Schublade
    /// liegt, nimmt sich damit aus Tab-Kreis und Pfeilnavigation; sonst ließen sich etwa die Funk-Lautstärke oder die Ausgabe blind ändern.
    /// </summary>
    public static readonly DependencyProperty UnterSchubladeProperty = DependencyProperty.RegisterAttached("UnterSchublade", typeof(bool), typeof(Hauptfenster),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetUnterSchublade(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(UnterSchubladeProperty);
    }

    public static void SetUnterSchublade(DependencyObject element, bool wert)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(UnterSchubladeProperty, wert);
    }

    /// <summary>
    /// Q93 a (Befund der Prüfung): Geht die Schublade auf, während der Fokus in der Spalte Hören von Start liegt, die sie verdeckt, wandert er in die
    /// Schublade, auf ihr Suchfeld (oder, solange es noch nicht aufgebaut ist, auf den Knopf "Wer ist da"); sonst änderten die Pfeiltasten etwa
    /// die Funk-Lautstärke unsichtbar. Hatte die Spalte Hören den Tastaturfokus, ist das Fenster schon vorn, und das Ziel bekommt ihn; sonst nur
    /// den logischen Fokus, den WPF beim nächsten Aktivieren übernimmt. Aktiviert wird nichts. Erst nach dem Aufbau der Schublade (Priorität
    /// Loaded), denn ihr Inhalt entsteht beim ersten Öffnen erst im nächsten Layout.
    /// </summary>
    private void FokusAusVerdecktem()
    {
        if (Nachfahren<StackPanel>(SeitenPlatz).FirstOrDefault(p => p.Name == "HoerenSpalte") is not { } hoeren || !FokusIn(hoeren)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!Schublade || !FokusIn(hoeren)) return;
            var tastatur = hoeren.IsKeyboardFocusWithin;
            IInputElement ziel = Nachfahren<TextBox>(WerIstDaSpalte).FirstOrDefault(t => t.Name == "WerIstDaSuche") ?? (IInputElement)WerIstDaKnopf;
            FocusManager.SetFocusedElement(this, ziel);
            // Nur mit dem Tastaturfokus im Fenster: Es ist dann schon vorn, Focus() aktiviert nichts.
            if (tastatur) ziel.Focus();
        }));
    }

    /// <summary>Der Tastaturfokus oder der logische Fokus des Fensters liegt in <paramref name="teil"/>.</summary>
    private bool FokusIn(UIElement teil) => teil.IsKeyboardFocusWithin || Enthaelt(teil, FocusManager.GetFocusedElement(this) as DependencyObject);

    /// <summary>Liegt <paramref name="d"/> in <paramref name="wurzel"/> (sichtbarer Baum)?</summary>
    private static bool Enthaelt(DependencyObject wurzel, DependencyObject? d)
    {
        for (; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, wurzel)) return true;
        return false;
    }

    /// <summary>Die Breite des Fensters: gemessen (Inhalt samt Rand), vor dem ersten Messen die gesetzte.</summary>
    private double Fensterbreite() =>
        Wurzelraster.ActualWidth > 0 ? Wurzelraster.ActualWidth + BorderThickness.Left + BorderThickness.Right : Width;

    /// <summary>
    /// Die Titelleiste nach der Fensterbreite (seit 0.10.0, E23, Prüfbericht Mischung Punkt 3): Unter <see cref="TitelKnoepfeKurzUnter"/> zeigen
    /// "Wer ist da" und "Funkfeld" nur Zeichen und Zahl, damit "spricht: ..." daneben Platz hat; unter <see cref="SehrSchmalUnter"/> fällt
    /// "spricht: ..." weg, und der Pegel der Statuszeile ist 120 statt 160 breit, damit jeder Hinweissatz ganz bleibt. Umgeschaltet hier statt
    /// über einen Wandler: Er bräuchte eine Ressource in App.xaml und im Bildlauf der Tests.
    /// </summary>
    internal void TitelleisteSetzen(double breite)
    {
        if (!double.IsFinite(breite) || breite <= 0) return;
        // Einen Punkt Spiel für die Rundung des Layouts: In Klein ab Werk (900 breit) maß der Inhalt 897,7 statt 898, und die Titelleiste stand
        // kurz, anders als in der abgenommenen Vorlage m-7c (gefunden beim Bildvergleich in AP4, 12.10.2026).
        breite += RundungsSpiel;
        TitelKurz = breite < TitelKnoepfeKurzUnter;
        SehrSchmal = breite < SehrSchmalUnter;
        // Kurz steht neben dem Zeichen nur die Zahl (WerIstDaKurzPlatz); ist niemand online, nur das Zeichen.
        Zeigen(WerIstDaText, !TitelKurz);
        Zeigen(WerIstDaKurzPlatz, TitelKurz);
        Zeigen(FunkfeldText, !TitelKurz);
        Zeigen(SprechendPlatz, !SehrSchmal);
        var pegel = SehrSchmal ? 120.0 : 160.0;
        if (StatusPegel.Width != pegel) StatusPegel.Width = pegel;
    }

    /// <summary>Setzt die Sichtbarkeit nur bei einem Wechsel (sonst misst WPF bei jedem Größenschritt neu).</summary>
    private static void Zeigen(UIElement e, bool sichtbar)
    {
        var soll = sichtbar ? Visibility.Visible : Visibility.Collapsed;
        if (e.Visibility != soll) e.Visibility = soll;
    }

    /// <summary>Der Bereich der Sprachaktivierung auf ihrer Seite (Tests, Fokus).</summary>
    public Schwellenbereich Schwellenbereich => Schwellen;

    private void ModellGeaendert(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HauptViewModel.AktiverBereich)) return;
        KlasseAnwenden();
        FokusPruefen();
    }

    /// <summary>
    /// Liegt der Fokus in der Sprachaktivierung und ist ihre Seite nicht mehr vorn (Strg+1 oder Strg+2, bis 0.9.3 bis Strg+4; N5 der Gegenprüfung 2c), fiele er ins
    /// Leere; dann nimmt ihn die Navigation. Bis 0.9.3 setzte das Fenster den Bereich hier zwischen unten und der Seite um.
    /// </summary>
    private void FokusPruefen()
    {
        if (_modell.IstSprachaktivierungsseite || !FokusInSchwellen()) return;
        FokusSetzen();
    }

    /// <summary>
    /// Fokus nach dem Aufbau und wenn er ins Leere fiele: auf die Navigation. Bis 19.10.2026 ging er nach dem Neuaufbau für eine
    /// andere Farbstellung (A31) auf die gewählte Farbwahl; seitdem gibt es nur Anthrazit und keine Wahl. Das Element wird auch
    /// logischer Fokus des Fensters und bekommt den Tastaturfokus beim Aktivieren, falls das Fenster noch nicht aktiv ist. Liefert
    /// das Element (Tests).
    /// </summary>
    internal IInputElement FokusSetzen()
    {
        FrameworkElement element = Navi;
        FocusManager.SetFocusedElement(this, element);
        element.Focus();
        return element;
    }

    private static IEnumerable<T> Nachfahren<T>(DependencyObject wurzel) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(wurzel); i++)
        {
            var kind = VisualTreeHelper.GetChild(wurzel, i);
            if (kind is T t) yield return t;
            foreach (var n in Nachfahren<T>(kind)) yield return n;
        }
    }

    /// <summary>
    /// Der Fokus liegt in der Sprachaktivierung: auf der Tastatur oder, wo es kein Fensterhandle gibt (Tests im Speicher), im logischen
    /// Fokus des Fensters.
    /// </summary>
    private bool FokusInSchwellen()
    {
        if (Schwellen.IsKeyboardFocusWithin) return true;
        var d = FocusManager.GetFocusedElement(this) as DependencyObject;
        while (d is not null)
        {
            if (ReferenceEquals(d, Schwellen)) return true;
            d = (d is Visual) || (d is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    /// <summary>Fokus auf die Grafik, sobald die Seite sichtbar und gemessen ist.</summary>
    private void GrafikFokussieren() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Schwellen.Verlaufsgrafik.Focus()));

    private void MaxKnopf_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void MaxKnopfSetzen()
    {
        var max = WindowState == WindowState.Maximized;
        MaxKnopf.Content = max ? "" : "";
        MaxKnopf.ToolTip = max ? "Wiederherstellen" : "Maximieren";
        System.Windows.Automation.AutomationProperties.SetName(MaxKnopf, max ? "Wiederherstellen" : "Maximieren");
    }
}
