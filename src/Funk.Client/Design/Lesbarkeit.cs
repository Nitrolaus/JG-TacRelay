// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Windows.Media;
using Funk.Client.Ablage;

namespace Funk.Client.Design;

/// <summary>Ein Vordergrund mit seinem Mindestkontrast nach WCAG 2.1: Text 4,5:1, Grenzen, Symbole und Fokus 3:1.</summary>
public sealed record Paar(string Was, Color Vorn, double Verlangt);

/// <summary>
/// Rechnung zur Lesbarkeit über hellem Spielbild. Gemischt wird wie in WPF und in der Fensterverwaltung von Windows: je
/// Kanal mit den sRGB-Werten (nicht linear), Alpha in 8 Bit. Der schlimmste Fall hinter dem Fenster ist reines Weiß, denn
/// alle Schrift und alle Grenzen im Funkfeld sind heller als ihre Fläche: Je heller das Spiel durchscheint, desto kleiner
/// wird der Abstand.
/// </summary>
public static class Lesbarkeit
{
    /// <summary>
    /// Reserve auf jedes verlangte Verhältnis beim Rechnen. Im Bild runden Mischung und Kantenglättung je Kanal um eine
    /// Stufe; bei den Werten hier kostet das bis zu 2 % des Verhältnisses.
    /// </summary>
    public const double Reserve = 0.1;

    /// <summary><paramref name="vorn"/> mit <paramref name="deckkraft"/> über <paramref name="hinten"/>, auf 8 Bit gerundet.</summary>
    public static Color Mischen(Color vorn, Color hinten, double deckkraft)
    {
        var a = Math.Clamp(deckkraft, 0, 1);
        byte K(byte v, byte h) => (byte)Math.Round((v * a) + (h * (1 - a)));
        return Color.FromRgb(K(vorn.R, hinten.R), K(vorn.G, hinten.G), K(vorn.B, hinten.B));
    }

    /// <summary>Eine Fläche mit <paramref name="alpha"/> über reinem Weiß.</summary>
    public static Color UeberWeiss(Color flaeche, byte alpha) => Mischen(flaeche, Colors.White, alpha / 255.0);

    /// <summary>Hält jedes Paar, mit <paramref name="inhalt"/> gedimmt, auf <paramref name="grund"/> sein Verhältnis plus <paramref name="reserve"/>?</summary>
    public static bool Haelt(Color grund, IEnumerable<Paar> paare, double inhalt = 1, double reserve = 0)
    {
        ArgumentNullException.ThrowIfNull(paare);
        return paare.All(p => Kontrast.Verhaeltnis(Mischen(p.Vorn, grund, inhalt), grund) >= p.Verlangt + reserve);
    }

    /// <summary>
    /// Kleinstes Alpha, mit dem die Fläche über reinem Weiß alle Paare mit <see cref="Reserve"/> hält. 255, wenn es nicht
    /// einmal deckend reicht; das fällt im Test auf (Lesbarkeit der Paare auf der deckenden Fläche).
    /// </summary>
    public static byte MindestAlpha(Color flaeche, IReadOnlyList<Paar> paare)
    {
        for (var a = 0; a < 255; a++)
        {
            if (Haelt(UeberWeiss(flaeche, (byte)a), paare, 1, Reserve)) return (byte)a;
        }
        return 255;
    }

    /// <summary>
    /// Stärkste Dimmung des Inhalts (kleinste Deckkraft, nicht unter <paramref name="mindestens"/>, in Hundertsteln) auf
    /// einer Fläche mit <paramref name="alpha"/> über Weiß, bei der alle Paare mit <see cref="Reserve"/> halten; 1, wenn
    /// keine Dimmung mehr geht.
    /// </summary>
    public static double Dimmung(Color flaeche, byte alpha, IReadOnlyList<Paar> paare, double mindestens)
    {
        var grund = UeberWeiss(flaeche, alpha);
        // Erst runden: 0,55 * 100 ist in double 55,000000000000007, und Ceiling machte daraus 56.
        for (var h = (int)Math.Ceiling(Math.Round(Math.Clamp(mindestens, 0, 1) * 100, 6)); h < 100; h++)
        {
            if (Haelt(grund, paare, h / 100.0, Reserve)) return h / 100.0;
        }
        return 1;
    }
}

/// <summary>
/// Kleinste Deckkraft der Flächen, die ihren Inhalt tragen, je Farbsatz (<see cref="FunkfeldDeckung.Mindest"/>), und die
/// Deckkraft des Inhalts leerer Felder, wenn alles deckend ist (<see cref="Leer"/>).
/// </summary>
public sealed record Mindestdeckung(byte Kachel, byte Global, byte Unterlage, double Leer);

/// <summary>
/// Deckkraft (Alpha 0-255) der Flächen des Funkfelds bei einer <see cref="Durchsicht"/>, und die Deckkraft des ganzen
/// Fensters (<see cref="Fenster"/>).
/// <para>
/// Hauptweg (Sarevoc, 30.09.2026: "nicht nur den Hintergrund"): Alle Flächen bleiben deckend, und es wirkt nur die Deckkraft
/// des Fensters, bis 15 %. Wie gut man dann über hellem Spielbild noch liest, ist Sarevocs Wahl; hier wird nichts begrenzt.
/// </para>
/// <para>
/// Mit der Option "Schrift und Symbole deckend halten" gelten die Lesbarkeitsregeln (Befund der Gegenprüfung: Bei 70 % über
/// der Sonne lag die Kachel bei #929292, die Zweitschrift hatte 1,9:1, Weiß 3,1:1, und die Knopfränder verschwanden): Die
/// Lücken zwischen den Kacheln und die Titelzeile folgen dem Regler bis Alpha 0,2 (<see cref="Luecken"/>). Kacheln, Global
/// samt der Marke oben links und die dunklen Unterlagen in der Titelzeile (Sendenetze, Knöpfe, Schiebeschalter) bleiben
/// mindestens so deckend, dass jedes Paar ihres Inhalts über reinem Weiß hält (<see cref="Mindest"/>). Im Zustand AUS wird
/// der Inhalt einer Kachel nur so weit gedimmt, wie es die Fläche dann noch zulässt (<see cref="AusDeckkraft"/>), ebenso der
/// Inhalt leerer Felder auf Kachel und Global (<see cref="LeerDeckkraft"/>).
/// </para>
/// </summary>
public sealed record FunkfeldDeckung(byte Luecken, byte Kachel, byte Global, byte Unterlage, double AusDeckkraft, double LeerDeckkraft, double Fenster)
{
    private static readonly ConcurrentDictionary<Farbsatz, Mindestdeckung> Vorrat = new();

    /// <summary>Alles deckend, leere Felder mit der Untergrenze <see cref="Palette.LeerDeckkraft"/>; je Farbsatz: <see cref="DeckendFuer"/>.</summary>
    public static FunkfeldDeckung Deckend { get; } = new(255, 255, 255, 255, Palette.AusDeckkraft, Palette.LeerDeckkraft, 1);

    /// <summary>Alles deckend; leere Felder so blass, wie es Kachel und Global dieses Farbsatzes zulassen.</summary>
    public static FunkfeldDeckung DeckendFuer(Farbsatz farben) => Deckend with { LeerDeckkraft = Mindest(farben).Leer };

    /// <summary>
    /// Was auf einer Kachel steht (Fläche #2F2F2F) und genauso auf Global (Leitfarbe). Nicht darunter: die Akzentspur des
    /// Lautstärkereglers (sie zeigt den Wert, die Grenze des Reglers sind Knauf und Spur), gesperrte Knöpfe (WCAG nimmt
    /// inaktive Elemente aus) und leere Felder, die absichtlich blass sind.
    /// </summary>
    public static IReadOnlyList<Paar> KachelPaare { get; } =
    [
        new("Name und Sprecher", Palette.Text, 4.5),
        new("Taste, Ruhe, AUS, VA/Taste, Symbole", Palette.Text2, 4.5),
        new("Du sprichst", Palette.Senden, 4.5),
        new("Knopfrand, Reglerspur, Rand der Marke AUS", Palette.Grenze, 3),
        new("Rand HÖRT", Palette.Hoeren, 3),
        new("Rand SENDET", Palette.Senden, 3),
        new("Fokusrahmen und Reglerknauf", Palette.Text, 3),
    ];

    /// <summary>Was der Zustand AUS dimmt: Name, Zweitschrift und der Rand der Marke.</summary>
    public static IReadOnlyList<Paar> AusPaare { get; } =
    [
        new("Name gedimmt", Palette.Text, 4.5),
        new("Zweitschrift gedimmt", Palette.Text2, 4.5),
        new("Rand der Marke AUS gedimmt", Palette.Grenze, 3),
    ];

    /// <summary>
    /// Was ein leeres Feld zeigen muss, auf der Kachel und auf Global (nicht verbunden): den Namen ("frei", "Global") und
    /// die Marke "FREI", beide weiß und mit der Deckkraft des Stils Kachel.Blass gedimmt. Taste und "Ruhe" bleiben
    /// zweitrangig und blasser; ein leeres Feld ist nicht bedienbar.
    /// </summary>
    public static IReadOnlyList<Paar> LeerPaare { get; } =
    [
        new("Name im leeren Feld", Palette.Text, 4.5),
        new("Marke FREI", Palette.Text, 4.5),
    ];

    /// <summary>
    /// Was auf den dunklen Unterlagen der Titelzeile steht (Fläche #1E1E1E): Sendenetze, der Regler der Durchsichtigkeit
    /// mit seinem Wert, die Knöpfe und der Schiebeschalter 5/10.
    /// </summary>
    public static IReadOnlyList<Paar> UnterlagePaare(Farbsatz farben)
    {
        ArgumentNullException.ThrowIfNull(farben);
        return
        [
            new("Sendekanäle, Wert des Reglers, nicht gewählte Feldzahl", Palette.Text2, 4.5),
            new("Ablehnung in roter Schrift", farben.RotSchrift, 4.5),
            new("Schließen, gewählte Ansicht und Feldzahl, Reglerknauf", Palette.Text, 4.5),
            new("Rand der gewählten Ansicht, Reglerspur, Spur des Schiebeschalters", Palette.Grenze, 3),
        ];
    }

    /// <summary>Kleinste Deckkraft der tragenden Flächen für einen Farbsatz; einmal gerechnet, dann gemerkt.</summary>
    public static Mindestdeckung Mindest(Farbsatz farben)
    {
        ArgumentNullException.ThrowIfNull(farben);
        return Vorrat.GetOrAdd(farben, f => new Mindestdeckung(
            Lesbarkeit.MindestAlpha(Palette.Flaeche, KachelPaare),
            Lesbarkeit.MindestAlpha(f.Leitfarbe, KachelPaare),
            Lesbarkeit.MindestAlpha(Palette.FlaecheTief, UnterlagePaare(f)),
            Leer(f, 255, 255, Palette.LeerDeckkraft)));
    }

    /// <summary>Deckkraft aller Flächen für diese Durchsicht und diesen Farbsatz.</summary>
    public static FunkfeldDeckung Berechnen(Durchsicht durchsicht, Farbsatz farben)
    {
        ArgumentNullException.ThrowIfNull(farben);
        var luecken = Alpha(durchsicht.Hintergrund);
        if (luecken == 255) return DeckendFuer(farben) with { Fenster = Math.Clamp(durchsicht.Fenster, 0, 1) };
        var m = Mindest(farben);
        var kachel = Math.Max(m.Kachel, luecken);
        var global = Math.Max(m.Global, luecken);
        return new FunkfeldDeckung(luecken, kachel, global, Math.Max(m.Unterlage, luecken),
            Lesbarkeit.Dimmung(Palette.Flaeche, kachel, AusPaare, Palette.AusDeckkraft), Leer(farben, kachel, global, m.Leer),
            Math.Clamp(durchsicht.Fenster, 0, 1));
    }

    /// <summary>
    /// Stärkste Dimmung leerer Felder, nicht unter <paramref name="mindestens"/>, bei der ihr Name auf der Kachel und auf
    /// Global (beide mit ihrer Deckkraft über Weiß) hält. Global zählt mit: Ist das Funkfeld getrennt, ist auch Global leer.
    /// </summary>
    private static double Leer(Farbsatz farben, byte kachel, byte global, double mindestens) => Math.Max(
        Lesbarkeit.Dimmung(Palette.Flaeche, kachel, LeerPaare, mindestens),
        Lesbarkeit.Dimmung(farben.Leitfarbe, global, LeerPaare, mindestens));

    /// <summary>Durchsichtigkeit einer Fläche in ganzen Prozent, für die Anzeige.</summary>
    public static int Prozent(byte alpha) => (int)Math.Round(100 - (alpha * 100.0 / 255));

    private static byte Alpha(double deckkraft) => (byte)Math.Round(Math.Clamp(deckkraft, 0, 1) * 255);
}
