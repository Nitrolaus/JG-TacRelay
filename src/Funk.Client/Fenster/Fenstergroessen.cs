// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using Funk.Client.Ablage;
using Funk.Client.Modelle;

namespace Funk.Client.Fenster;

/// <summary>
/// Größenklassen des Hauptfensters (Sarevoc, 02.10.2026, Q19 und Vorgabe j): je Klasse eine eigene, getrennt gemerkte Größe. Wird nicht gespeichert,
/// nur ihr <see cref="Fenstergroessen.Schluessel"/>; bis 0.9.3 gab es dazu die Klassen mit aufgeklappter Sprachaktivierung.
/// </summary>
public enum Groessenklasse
{
    /// <summary>Die Startseite: so klein wie möglich (Zweitmonitor mit Discord und Twitch), seit 0.10.0 mindestens 900 x 900 (Q89).</summary>
    Klein,

    /// <summary>Das Brett der Einsatzleitung.</summary>
    Brett,

    /// <summary>Der Verwaltungsteil (nur Admin-Setup).</summary>
    Verwaltung,

    /// <summary>
    /// Die übrigen Seiten, etwa Einstellungen (seit Stufe 2): eine eigene, gemerkte Größe (Q19: Größen je Bereich), ab Werk die des Hauptfensters
    /// bis Stufe 1; so bleibt die Startseite klein, auch wenn diese Seiten größer gezogen werden.
    /// </summary>
    Seite,

    /// <summary>
    /// Die Seite Sprachaktivierung (ui-fix 0.6.2): eigene Größe, weil die Schwellengrafik dort fest und groß ist (Pegelfläche 600 x 260); in ihrer
    /// Mindestgröße rollt die Seite nur senkrecht, die Grafik wird nie kleiner. Bis 0.6.1 eine <see cref="Seite"/>.
    /// </summary>
    Sprachaktivierung,
}

/// <summary>Was das Öffnen der Spalte "Wer ist da" in Klein mit dem Fenster macht (Q93 a, <see cref="Fenstergroessen.SpalteOeffnen"/>).</summary>
public enum Spaltenplatz
{
    /// <summary>Das Fenster ist schon breit genug: Die Spalte steht wie bisher neben Start, nichts ändert sich.</summary>
    Daneben,

    /// <summary>Das Fenster wird um die Breite der Spalte breiter, nach rechts oder, wo rechts kein Platz ist, nach links ausweichend.</summary>
    Breiter,

    /// <summary>Kein Platz auf dem Monitor: Das Fenster bleibt, und die Spalte legt sich wie eine Schublade über Start.</summary>
    Schublade,
}

/// <summary>
/// Größen des Hauptfensters je <see cref="Groessenklasse"/> (Weg 1, Sarevoc 02.10.2026): Brett und Verwaltung öffnen in ihrer
/// zuletzt benutzten Größe, die übrigen Bereiche wieder klein. Was du am Rand ziehst, gilt für die Klasse, in der du es tust.
/// Reine Rechnung ohne Fenster; das Hauptfenster wendet sie an.
/// <para>
/// Seit 0.10.0 (Q87, Q89 Weg a) ist Klein mindestens 900 x 900: Start mit Sprechen und Hören nebeneinander und der erste Start passen ohne
/// Rollbalken hinein. Unten steht die Statuszeile; die Sprachaktivierung klappt nicht mehr auf, sie ist nur noch eine Seite. Die Klassen mit
/// aufgeklappter Sprachaktivierung (Stufe 2b bis 0.9.3) entfallen; ihre Schlüssel <c>start-aufgeklappt</c> und <c>seite-aufgeklappt</c> bleiben
/// in der Datei stehen, für ältere Clients, und werden hier nicht mehr gelesen.
/// </para>
/// <para>
/// Ohne gemerkte Größe öffnet Klein genau so klein wie <see cref="KleinMindest"/>; eine kleinere gemerkte Größe (etwa 760 x 660 aus 0.9.3) hebt
/// <see cref="Groesse"/> auf die Mindestgröße. Die Schlüssel <c>klein</c> und <c>sprachaktivierung</c> aus Stufe 1 und 2 gelten nicht mehr
/// (<see cref="Veraltet"/>): Stufe 1 legte dort die alte, große Größe von 0.5.x ab (Befund N5 der Gegenprüfung Stufe 2), und die
/// Sprachaktivierung war drei Spalten breit.
/// </para>
/// <para>
/// Seit Q93 a (Sarevoc 12.10.2026) rechnet die Klasse auch die Spalte "Wer ist da" in Klein: Ist das Fenster schmaler als
/// <see cref="WerIstDaDanebenAb"/>, wird es beim Öffnen um die Spalte breiter (<see cref="SpalteOeffnen"/>), geht beim Schließen zurück, wenn es
/// unverändert steht (<see cref="Unveraendert"/>), und gemerkt wird die Größe ohne die Spalte (<see cref="OhneSpalte"/>).
/// </para>
/// </summary>
/// <param name="gemerkt">Die gemerkten Größen (<see cref="ClientEinstellungen.HauptfensterGroessen"/>), wird hier geschrieben.</param>
/// <param name="seiteAlt">
/// Aus einer Datei vor 0.6.0 die Größe, die das Hauptfenster damals hatte; sie wird einmal als <see cref="Groessenklasse.Seite"/>
/// übernommen, wenn dafür noch nichts gemerkt ist. Die Startseite bleibt klein.
/// </param>
public sealed class Fenstergroessen(IDictionary<string, Fenstergroesse> gemerkt, Size? seiteAlt = null)
{
    private readonly IDictionary<string, Fenstergroesse> _gemerkt = Uebernehmen(gemerkt, seiteAlt);

    /// <summary>Schlüssel aus Stufe 1 und 2, die beim Aufbau wegfallen (siehe Klasse).</summary>
    public static IReadOnlyList<string> Veraltet { get; } = ["klein", "sprachaktivierung"];

    /// <summary>Klein ohne gemerkte Größe: so klein wie möglich (Vorgabe j), also die Mindestgröße.</summary>
    public static Size KleinVoreinstellung => KleinMindest;

    /// <summary>Einstellungen ohne gemerkte Größe: wie das Hauptfenster bis Stufe 1 ab Werk.</summary>
    public static Size SeiteVoreinstellung { get; } = new(960, 700);

    /// <summary>Mindestgröße der Seiten (dort darf es rollen, Q12 und Q89): wie bis Stufe 1.</summary>
    public static Size SeiteMindest { get; } = new(640, 480);

    /// <summary>
    /// Mindestgröße der Seite Sprachaktivierung (ui-fix 0.6.2): so breit, dass die feste Grafik (<see cref="Schwellenbereich.SeitenGrafik"/>) neben
    /// der Navigation samt Rändern und senkrechtem Rollbalken ganz zu sehen ist; die Einstellungen stehen dann unter ihr, die Seite rollt.
    /// </summary>
    public static Size SprachaktivierungMindest { get; } = new(960, 480);

    /// <summary>Die Seite Sprachaktivierung ohne gemerkte Größe: breit genug für die Einstellungen rechts neben der Grafik.</summary>
    public static Size SprachaktivierungVoreinstellung { get; } = new(1280, 900);

    /// <summary>Brett und Verwaltung ohne gemerkte Größe: die Größe des Hauptfensters bis 0.5.x.</summary>
    public static Size GrossVoreinstellung { get; } = new(1120, 780);

    /// <summary>
    /// Mindestgröße von Klein seit 0.10.0 (Q89, Weg a, Sarevoc 11.10.2026): 900 x 900. Bei 900 Breite stehen auf Start "Sprechen" und "Hören"
    /// nebeneinander, auch der erste Start (m-7c) passt ohne Rollbalken; bis 0.9.3 waren es 760 x 660 (die Startseite ohne Rollbalken in jedem
    /// Zustand nach der Anmeldung). Ein Monitor mit kleinerem Arbeitsbereich kürzt sie (<see cref="Mindest(Groessenklasse, Bildschirm?)"/>);
    /// dann rollt Start dort, etwa bei Full HD mit 125 % (Arbeitsbereich etwa 826 hoch).
    /// </summary>
    public static Size KleinMindest { get; } = new(900, 900);

    /// <summary>Mindestgröße von Brett und Verwaltung: die des Hauptfensters bis 0.5.x.</summary>
    public static Size GrossMindest { get; } = new(560, 440);

    /// <summary>Breite der Spalte "Wer ist da" (A48) in Einheiten; <c>Hauptfenster.xaml</c> nimmt sie von hier (Q93 a).</summary>
    public const double WerIstDaBreite = 340;

    /// <summary>
    /// Die Seiten mit Bildlauf (Start, Einstellungen) sind höchstens 820 breit plus 24 Rand je Seite. Steht "Wer ist da" daneben, wird ihr Platz
    /// nicht breiter, damit die Spalte ohne Lücke am Inhalt steht (Sarevoc 14.10.2026: "der freie Bereich muss weg").
    /// </summary>
    public const double SeiteMitBildlaufBreite = 868;

    /// <summary>
    /// So viel darf eine gemessene Breite unter einer Grenze liegen und gilt doch als genau dort (Rundung des Layouts: In Klein 900 maß der Inhalt
    /// im Bildlauf 897,7 statt 898, AP4). Gilt für die Grenzen der Titelleiste und für <see cref="WerIstDaDanebenAb"/>.
    /// </summary>
    public const double RundungsSpiel = 1;

    /// <summary>
    /// Q93 a (Sarevoc 12.10.2026): Ab dieser Fensterbreite bleibt Start in Klein neben der offenen Spalte "Wer ist da" so breit wie Klein ab Werk
    /// (<see cref="KleinMindest"/>: zwei Spalten Sprechen und Hören, kein Rollen), also 900 plus 340. Schmaler wird das Fenster beim Öffnen breiter
    /// (<see cref="SpalteOeffnen"/>), oder die Spalte liegt als Schublade über Start.
    /// </summary>
    public static double WerIstDaDanebenAb => KleinMindest.Width + WerIstDaBreite;

    /// <summary>
    /// Die Klasse eines Bereichs (Kennung aus <see cref="HauptViewModel"/>). Die Seite Sprachaktivierung (Stufe 2c) hat seit 0.6.2 ihre eigene
    /// Klasse; alle übrigen Seiten teilen sich <see cref="Groessenklasse.Seite"/>.
    /// </summary>
    public static Groessenklasse KlasseFuer(string? kennung) => kennung switch
    {
        HauptViewModel.RaidKennung => Groessenklasse.Brett,
        HauptViewModel.VerwaltungKennung => Groessenklasse.Verwaltung,
        HauptViewModel.SprachaktivierungKennung => Groessenklasse.Sprachaktivierung,
        null or HauptViewModel.StartKennung => Groessenklasse.Klein,
        _ => Groessenklasse.Seite,
    };

    /// <summary>Schlüssel in der Einstellungsdatei; die Startseite heißt seit Stufe 2b <c>start</c> (bis Stufe 2 <c>klein</c>, <see cref="Veraltet"/>).</summary>
    public static string Schluessel(Groessenklasse klasse) => klasse switch
    {
        Groessenklasse.Brett => "brett",
        Groessenklasse.Verwaltung => "verwaltung",
        Groessenklasse.Seite => "seite",
        Groessenklasse.Sprachaktivierung => "sprachaktivierung-seite",
        _ => "start",
    };

    /// <summary>Mindestgröße einer Klasse.</summary>
    public static Size Mindest(Groessenklasse klasse) => klasse switch
    {
        Groessenklasse.Klein => KleinMindest,
        Groessenklasse.Seite => SeiteMindest,
        Groessenklasse.Sprachaktivierung => SprachaktivierungMindest,
        _ => GrossMindest,
    };

    /// <summary>
    /// Mindestgröße einer Klasse auf dem Monitor <paramref name="b"/>: höchstens dessen Arbeitsbereich. WPF hält MinWidth und
    /// MinHeight auch gegen eine kleinere Größe; ohne die Grenze ragte das Fenster auf einem Laptop mit 150 % (688 hoch) unter die
    /// Taskleiste. Ohne Monitor die Mindestgröße der Klasse.
    /// </summary>
    public static Size Mindest(Groessenklasse klasse, Bildschirm? b)
    {
        var m = Mindest(klasse);
        return b is null ? m : new Size(Math.Min(m.Width, b.Arbeit.Breite / b.Skala), Math.Min(m.Height, b.Arbeit.Hoehe / b.Skala));
    }

    /// <summary>
    /// Die Größe einer Klasse: die gemerkte, sonst die Voreinstellung; nie kleiner als ihre Mindestgröße (seit 0.10.0: Ein Klein von 760 x 660
    /// aus 0.9.3 wird 900 x 900).
    /// </summary>
    public Size Groesse(Groessenklasse klasse)
    {
        var m = Mindest(klasse);
        if (_gemerkt.TryGetValue(Schluessel(klasse), out var g) && Fenstergroesse.OderNull(g) is { } gut)
            return new Size(Math.Max(gut.Breite, m.Width), Math.Max(gut.Hoehe, m.Height));
        return klasse switch
        {
            Groessenklasse.Klein => KleinVoreinstellung,
            Groessenklasse.Seite => SeiteVoreinstellung,
            Groessenklasse.Sprachaktivierung => SprachaktivierungVoreinstellung,
            _ => GrossVoreinstellung,
        };
    }

    /// <summary>Merkt eine am Rand gezogene Größe für <paramref name="klasse"/>, auf zwei Stellen gerundet; Unbrauchbares nicht.</summary>
    public void Merken(Groessenklasse klasse, double breite, double hoehe)
    {
        if (Fenstergroesse.OderNull(new Fenstergroesse(Math.Round(breite, 2), Math.Round(hoehe, 2))) is { } g) _gemerkt[Schluessel(klasse)] = g;
    }

    /// <summary>
    /// Platz nach einem Klassenwechsel: Die linke obere Ecke von <paramref name="jetzt"/> bleibt, das Fenster bekommt
    /// <paramref name="groesse"/>, höchstens den Arbeitsbereich des Monitors unter der Ecke und mindestens
    /// <paramref name="mindest"/>. Passt es dann nicht, rückt es nur so weit nach links und oben wie nötig.
    /// </summary>
    public static Platz Wechsel(FensterOrt jetzt, Size groesse, IReadOnlyList<Bildschirm> bildschirme, Size mindest)
    {
        ArgumentNullException.ThrowIfNull(jetzt);
        var b = Platzierung.Am(jetzt.Links, jetzt.Oben, bildschirme);
        return Platzierung.Einpassen(jetzt.Links, jetzt.Oben, Platzierung.Begrenzen(groesse, b, mindest), b, false);
    }

    /// <summary>
    /// Q93 a (Sarevoc 12.10.2026): Wohin das Fenster kommt, wenn in Klein die Spalte "Wer ist da" aufgeht. <paramref name="jetzt"/> ist sein Platz im
    /// Normalzustand (Ecke in Bildpunkten, Größe in Einheiten), <paramref name="skala"/> die Skala, die das Fenster gerade hat. Ist es schon
    /// <see cref="WerIstDaDanebenAb"/> breit, bleibt alles (<see cref="Spaltenplatz.Daneben"/>). Sonst wird es um <see cref="WerIstDaBreite"/>
    /// breiter: nach rechts, solange der Arbeitsbereich seines Monitors reicht, sonst so weit nach links wie nötig, ohne diesen Arbeitsbereich zu
    /// verlassen; Höhe und obere Kante bleiben. Reicht auch das nicht, bleibt das Fenster, und die Spalte wird eine Schublade. Sein Monitor ist der,
    /// auf dem es überwiegend steht (<see cref="Platzierung.Ueberwiegend"/>, wie Windows ihn für die Skala nimmt), nicht der unter der linken oberen
    /// Ecke: Ein Fenster, das zu einem Teil auf den Nachbarmonitor ragt, springt so nicht hinüber (Befund der Prüfung). Gerechnet in Bildpunkten mit
    /// der Skala dieses Monitors (100, 125, 150 oder 175 %); auf einen anderen Monitor rückt das Fenster dabei nie.
    /// </summary>
    public static (Spaltenplatz Art, Platz? Breiter) SpalteOeffnen(FensterOrt jetzt, IReadOnlyList<Bildschirm> bildschirme, double skala)
    {
        ArgumentNullException.ThrowIfNull(jetzt);
        ArgumentNullException.ThrowIfNull(bildschirme);
        if (jetzt.Breite is not { } breite || jetzt.Hoehe is not { } hoehe || bildschirme.Count == 0) return (Spaltenplatz.Schublade, null);
        if (breite + RundungsSpiel >= WerIstDaDanebenAb) return (Spaltenplatz.Daneben, null);
        var s = double.IsFinite(skala) && skala > 0 ? skala : 1;
        var b = Platzierung.Ueberwiegend(new Pixelrechteck(jetzt.Links, jetzt.Oben, Platzierung.Pixel(breite, s), Platzierung.Pixel(hoehe, s)), bildschirme);
        var neu = breite + WerIstDaBreite;
        var pixel = Platzierung.Pixel(neu, b.Skala);
        if (jetzt.Links + pixel <= b.Arbeit.Rechts) return (Spaltenplatz.Breiter, new Platz(jetzt.Links, jetzt.Oben, neu, hoehe, b));
        var links = b.Arbeit.Rechts - pixel;
        return links >= b.Arbeit.Links ? (Spaltenplatz.Breiter, new Platz(links, jetzt.Oben, neu, hoehe, b)) : (Spaltenplatz.Schublade, null);
    }

    /// <summary>
    /// Q93 a: Steht das Fenster im Normalzustand noch so, wie das Öffnen der Spalte es verbreitert hat (<paramref name="nachher"/>; Lage auf
    /// einen Bildpunkt, Größe auf eine Einheit genau)? Nur dann geht es beim Schließen zurück. Ob es gerade maximiert ist, zählt hier nicht: Nach
    /// Maximieren und Wiederherstellen steht es wieder genau so.
    /// </summary>
    public static bool Unveraendert(FensterOrt jetzt, Platz nachher)
    {
        ArgumentNullException.ThrowIfNull(jetzt);
        ArgumentNullException.ThrowIfNull(nachher);
        return Math.Abs((long)jetzt.Links - nachher.Links) <= 1 && Math.Abs((long)jetzt.Oben - nachher.Oben) <= 1
            && jetzt.Breite is { } breite && Math.Abs(breite - nachher.Breite) <= 1 && jetzt.Hoehe is { } hoehe && Math.Abs(hoehe - nachher.Hoehe) <= 1;
    }

    /// <summary>
    /// Q93 a: Der Platz zum Merken, solange die Spalte "Wer ist da" in einem für sie verbreiterten Fenster steht: ohne die Spalte, damit der nächste
    /// Start nicht breit beginnt. Steht das Fenster noch so, wie es verbreitert wurde (<see cref="Unveraendert"/>), ist es der Platz davor, auch
    /// maximiert. Hat der Nutzer es danach verschoben oder gezogen, gilt seine Lage mit der Breite ohne die Spalte; ist es dabei schmaler als
    /// <see cref="WerIstDaDanebenAb"/> geworden, liegt die Spalte als Schublade über Start und nimmt keine Breite, dann gilt die Breite, wie sie ist.
    /// </summary>
    public static FensterOrt OhneSpalte(FensterOrt ort, Platz vorher, Platz nachher)
    {
        ArgumentNullException.ThrowIfNull(ort);
        ArgumentNullException.ThrowIfNull(vorher);
        if (Unveraendert(ort, nachher)) return ort with { Links = vorher.Links, Oben = vorher.Oben, Breite = vorher.Breite, Hoehe = vorher.Hoehe };
        return ort.Breite is { } breite && breite + RundungsSpiel >= WerIstDaDanebenAb ? ort with { Breite = Math.Round(breite - WerIstDaBreite, 2) } : ort;
    }

    private static IDictionary<string, Fenstergroesse> Uebernehmen(IDictionary<string, Fenstergroesse> gemerkt, Size? alt)
    {
        ArgumentNullException.ThrowIfNull(gemerkt);
        foreach (var veraltet in Veraltet) gemerkt.Remove(veraltet);
        var schluessel = Schluessel(Groessenklasse.Seite);
        if (alt is { } a && !gemerkt.ContainsKey(schluessel) && Fenstergroesse.OderNull(new Fenstergroesse(a.Width, a.Height)) is { } g) gemerkt[schluessel] = g;
        return gemerkt;
    }
}
