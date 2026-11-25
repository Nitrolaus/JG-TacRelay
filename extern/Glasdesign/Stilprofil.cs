// Glasdesign - Maintainer: Sarevoc
namespace Glasdesign;

/// <summary>
/// Das Stilprofil: die Einstellwerte der Stilfamilien 2 (KONZEPT.md Abschnitt 5b, Sarevocs Vorbilder vom 15.10.2026): Taktil,
/// Glutglas, Chromrahmen, Horizontglas, Dickglas, Glanzkugel und je Chromfamilie die Werte des gemeinsamen
/// <see cref="Chromrand"/>. Jede Farbstellung trägt eines unter <see cref="Schluessel.Stilprofil"/>; fehlt es, gilt
/// <see cref="Standard"/>. Die Namen beginnen mit der Familie (Glint ordnet danach), Deckungen und Anteile von 0 bis 1.
/// </summary>
public sealed record Stilprofil
{
    /// <summary>Das Profil mit den Vorgaben, wenn eine Farbstellung keines mitbringt.</summary>
    public static Stilprofil Standard { get; } = new();

    /// <summary>Alle Familien: überfahren so viel heller.</summary>
    public double FamilieUeberfahren { get; init; } = 0.07;

    /// <summary>Alle Familien: gedrückt so viel dunkler.</summary>
    public double FamilieGedrueckt { get; init; } = 0.12;

    /// <summary>Taktil: wie hoch die gehobene Ebene liegt (Schatten weiter und weicher), 0,5 bis 1,6 in der Studie.</summary>
    public double TaktilStufe { get; init; } = 1.0;

    /// <summary>Taktil: Deckung des Schattens unter gehobenen Flächen.</summary>
    public double TaktilSchatten { get; init; } = 0.63;

    /// <summary>Taktil: Lichtkante oben auf gehobenen Flächen und Glanz auf dem Kupfer.</summary>
    public double TaktilLicht { get; init; } = 0.24;

    /// <summary>Taktil: Innenschatten eingelassener Flächen (oben und links).</summary>
    public double TaktilTiefe { get; init; } = 0.6;

    /// <summary>Glutglas: Stärke des roten Glühens unter wichtigen Elementen und in ihrem Glas.</summary>
    public double GlutglasGlut { get; init; } = 1.0;

    /// <summary>Glutglas: Stärke des Leuchtsaums um Hauptknopf und Symbolknopf.</summary>
    public double GlutglasSaum { get; init; } = 0.85;

    /// <summary>Glutglas: Reichweite des Leuchtsaums in DIP.</summary>
    public double GlutglasReichweite { get; init; } = 26;

    /// <summary>Glutglas: Stärke der Kantenglut (heller roter Strich an der Unterkante).</summary>
    public double GlutglasKantenglut { get; init; } = 1.0;

    /// <summary>Glutglas: Deckung der Rauchglasscheibe.</summary>
    public double GlutglasScheibe { get; init; } = 0.9;

    /// <summary>Glutglas: Stärke des schrägen Glanzes, der an einer Diagonale abbricht.</summary>
    public double GlutglasGlanz { get; init; } = 0.12;

    /// <summary>Dickglas: Glanzstreif oben und Glanzlichter auf dem Wulst.</summary>
    public double DickglasGlanz { get; init; } = 1.0;

    /// <summary>Dickglas: wie stark die steile Außenkante die Umgebung spiegelt.</summary>
    public double DickglasFresnel { get; init; } = 0.6;

    /// <summary>Dickglas: Stärke des farbigen Lichts im Glas und auf der Unterlage.</summary>
    public double DickglasLicht { get; init; } = 0.75;

    /// <summary>Dickglas: Deckung des Schlagschattens nach unten links.</summary>
    public double DickglasSchatten { get; init; } = 0.6;

    /// <summary>Glanzkugel: Kuppelglanz oben auf Kuppel, Kugel und Flächen.</summary>
    public double GlanzkugelKuppelglanz { get; init; } = 0.75;

    /// <summary>Glanzkugel: Stärke des Leuchtkerns in der dunklen Kuppel.</summary>
    public double GlanzkugelLeuchtkern { get; init; } = 1.0;

    /// <summary>Glanzkugel: Fensterspiegelung (Bögen in der Kuppel, vier Scheiben auf der Kugel).</summary>
    public double GlanzkugelFenster { get; init; } = 0.9;

    /// <summary>Glanzkugel: Deckung des Bodenschattens.</summary>
    public double GlanzkugelSchatten { get; init; } = 0.7;

    /// <summary>Horizontglas: Deckung des Himmelsglanzes oben bis zur Horizontlinie.</summary>
    public double HorizontglasHimmelsglanz { get; init; } = 0.55;

    /// <summary>Horizontglas: Lage der Horizontlinie als Anteil der Innenhöhe.</summary>
    public double HorizontglasHorizontlage { get; init; } = 0.5;

    /// <summary>Horizontglas: Licht, das unten durch das Glas scheint.</summary>
    public double HorizontglasBodenlicht { get; init; } = 0.35;

    /// <summary>Horizontglas: der Funke oben links auf dem Glas.</summary>
    public double HorizontglasFunke { get; init; } = 0.9;

    /// <summary>Horizontglas: Deckung des sehr schwachen Schattens.</summary>
    public double HorizontglasSchatten { get; init; } = 0.18;

    /// <summary>Chromrahmen: Glanzstreif oben auf der Füllung.</summary>
    public double ChromrahmenFuellglanz { get; init; } = 0.9;

    /// <summary>Chromrahmen: Tiefe der Rille zwischen Rahmen und Füllung.</summary>
    public double ChromrahmenRille { get; init; } = 0.7;

    /// <summary>Chromrahmen: Deckung der Rauchgläser als Faktor (1 wie in der Studie).</summary>
    public double ChromrahmenRauch { get; init; } = 1.0;

    /// <summary>Chromrahmen: der Funke am linken Rand beim Überfahren.</summary>
    public double ChromrahmenFunke { get; init; } = 0.95;

    /// <summary>Chromrahmen: Deckung des Schattens unter dem Rahmen.</summary>
    public double ChromrahmenSchatten { get; init; } = 0.55;

    /// <summary>Chromrand der Glanzkugel: Helligkeit des gespiegelten Himmels.</summary>
    public double GlanzkugelChromHimmel { get; init; } = 0.93;

    /// <summary>Chromrand der Glanzkugel: Helligkeit des dunklen Horizonts.</summary>
    public double GlanzkugelChromHorizont { get; init; } = 0.42;

    /// <summary>Chromrand der Glanzkugel: Helligkeit des Bodens unter dem Horizont.</summary>
    public double GlanzkugelChromBoden { get; init; } = 0.62;

    /// <summary>Chromrand der Glanzkugel: Glanzpunkt von oben links.</summary>
    public double GlanzkugelChromGlanz { get; init; } = 0.5;

    /// <summary>Chromrand der Glanzkugel: wie stark flache Stücke als gewölbt gelten.</summary>
    public double GlanzkugelChromWoelbung { get; init; } = 0.9;

    /// <summary>Chromrand der Glanzkugel: Steilheit des Profils (Neigung als Tangens).</summary>
    public double GlanzkugelChromSteil { get; init; } = 2.2;

    // Chromrand des Horizontglases, Werte wie bei der Glanzkugel. Einen Glanzpunkt hat er nicht: Bei seinem schmalen, flachen
    // Profil trifft der Glanzpunkt von oben links kein Pixel (gemessen bis 300 Prozent); den Funken zeichnet das Glas selbst.
    public double HorizontglasChromHimmel { get; init; } = 1.0;
    public double HorizontglasChromHorizont { get; init; } = 0.10;
    public double HorizontglasChromBoden { get; init; } = 0.66;
    public double HorizontglasChromWoelbung { get; init; } = 0.3;
    public double HorizontglasChromSteil { get; init; } = 2.8;

    // Chromrand des Chromrahmens, Werte wie bei der Glanzkugel.
    public double ChromrahmenChromHimmel { get; init; } = 0.92;
    public double ChromrahmenChromHorizont { get; init; } = 0.24;
    public double ChromrahmenChromBoden { get; init; } = 0.55;
    public double ChromrahmenChromGlanz { get; init; } = 0.45;
    public double ChromrahmenChromWoelbung { get; init; } = 0.7;
    public double ChromrahmenChromSteil { get; init; } = 2.0;

    /// <summary>Die Werte des Chromrands einer Familie, mit ihrem festen Profil (Anteile außen und innen, Lage des Horizonts).</summary>
    public Chromart Chrom(Chromfamilie familie) => familie switch
    {
        Chromfamilie.Glanzkugel => new Chromart(0.22, 0.18, GlanzkugelChromSteil, GlanzkugelChromHimmel, GlanzkugelChromHorizont, GlanzkugelChromBoden, 0.25, GlanzkugelChromWoelbung, GlanzkugelChromGlanz),
        Chromfamilie.Horizontglas => new Chromart(0.40, 0.40, HorizontglasChromSteil, HorizontglasChromHimmel, HorizontglasChromHorizont, HorizontglasChromBoden, 0.12, HorizontglasChromWoelbung, 0),
        _ => new Chromart(0.38, 0.34, ChromrahmenChromSteil, ChromrahmenChromHimmel, ChromrahmenChromHorizont, ChromrahmenChromBoden, 0.05, ChromrahmenChromWoelbung, ChromrahmenChromGlanz),
    };

    /// <summary>Alle Werte mit Namen, in der Reihenfolge der Deklaration (für Export und Glint).</summary>
    public IReadOnlyList<(string Name, double Wert)> Werte() => Wertesatz.Lesen(this);
}

/// <summary>
/// Die Werte eines <see cref="Chromrand"/>: Profil über die Breite (außen steigt die Fläche auf dem Anteil
/// <paramref name="Aussen"/>, innen fällt sie auf <paramref name="Innen"/>, dazwischen flach, Steilheit
/// <paramref name="Steil"/>) und die gespiegelte Umgebung (Himmel, dunkler Horizont ab <paramref name="HorizontLage"/>,
/// Boden), dazu flache Stücke als leicht gewölbt (<paramref name="Woelbung"/>) und ein Glanzpunkt von oben links.
/// </summary>
public readonly record struct Chromart(double Aussen, double Innen, double Steil, double Himmel, double Horizont, double Boden,
    double HorizontLage, double Woelbung, double Glanz)
{
    /// <summary>Gesperrt: weniger Kontrast, kein Glanzpunkt, etwas dunkler.</summary>
    public Chromart Gesperrt() => this with
    {
        Himmel = 0.55 + (Himmel - 0.55) * 0.4, Horizont = 0.45 + (Horizont - 0.45) * 0.4, Boden = 0.5 + (Boden - 0.5) * 0.4, Glanz = 0,
    };
}
