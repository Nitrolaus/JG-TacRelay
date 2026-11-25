// Glasdesign - Maintainer: Sarevoc
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Das Glanzprofil: alle Einstellwerte der Spiegelungen, unveränderlich und zur Laufzeit tauschbar (Glas 4, Grundlage für
/// Glint). Jede Farbstellung trägt ihr eigenes Profil unter <see cref="Schluessel.Glanzprofil"/>; die Elemente holen es per
/// SetResourceReference wie ihre Pinsel, ein Wechsel wirkt sofort. Fehlt der Schlüssel, gilt <see cref="Standard"/>.
/// Die Vorgaben sind Sarevocs Werte aus dem Einstellpult (Fassung 0.3.1). Deckungen von 0 bis 1, Lagen in Grad (gegen den
/// Uhrzeigersinn ab rechts: 90 = oben, 180 = links), Längen als Anteil des Eckradius, Breiten in Gerätepixeln. Haarlinie
/// und Durchlicht stehen als Pinsel in der Farbstellung (Glas.Kante*, Glas.Lichtfleck*). Das Einstellpult
/// (export\einstellpult.html) liest diese Werte und rechnet wie <see cref="Glanzbild"/>.
/// Seit Glint Abschnitt 5 kommen zehn abschaltbare Glanzschichten dazu (Glanzkappe bis Glanzwischer, jede mit Stärke 0
/// als Vorgabe) und fertige Mischungen (<see cref="Glanzmischung"/>, <see cref="Mischen"/>); das Einstellpult zeichnet die
/// neuen Schichten noch nicht, trägt ihre Werte aber mit.
/// </summary>
public sealed record Glanzprofil
{
    /// <summary>Das Profil mit den Vorgaben, wenn eine Farbstellung keines mitbringt.</summary>
    public static Glanzprofil Standard { get; } = new();

    public double Kantenspiegel { get; init; } = 0.51;
    public double Randlicht { get; init; } = 1.55;
    public double Schein { get; init; } = 0.45;

    /// <summary>Der Randglanz: ein weißes Glanzlicht direkt auf der Haarlinie, an der Rundung oben links.</summary>
    public double Randglanz { get; init; } = 1.00;

    /// <summary>Seine Lage auf der Rundung in Grad (131: in der Ecke, etwas über der Mitte des Bogens).</summary>
    public double RandglanzLage { get; init; } = 131;

    /// <summary>Seine halbe Länge entlang der Kante, als Anteil des Eckradius (mindestens drei Gerätepixel).</summary>
    public double RandglanzLaenge { get; init; } = 0.48;

    /// <summary>Wie weit er von außen nach innen reicht, in Gerätepixeln: die Haarlinie und eine Reihe darunter.</summary>
    public double RandglanzBreite { get; init; } = 2;

    /// <summary>Der weiche Schein um den Randglanz, in den äußeren vier Pixelreihen, als Anteil seiner Stärke.</summary>
    public double RandglanzHof { get; init; } = 0.68;

    /// <summary>Der Gegenglanz auf der Haarlinie an der Rundung unten rechts, kürzer und schmaler als der Randglanz.</summary>
    public double Gegenglanz { get; init; } = 1.00;

    /// <summary>Seine Lage auf der unteren rechten Rundung in Grad (-90 = unten, 0 = rechts).</summary>
    public double GegenglanzLage { get; init; } = -39;

    /// <summary>Deckung des Umgebungsschattens.</summary>
    public double Schatten { get; init; } = 0.85;

    /// <summary>Stärke der Spiegelungen beim Überfahren (heller).</summary>
    public double Ueberfahren { get; init; } = 1.25;

    /// <summary>Stärke der Spiegelungen gedrückt (kaum gedämpft).</summary>
    public double Gedrueckt { get; init; } = 0.95;

    /// <summary>Dunkles Glas (aus, Dunkel) spiegelt schwächer.</summary>
    public double Dunkel { get; init; } = 0.85;

    // Leuchtsaum (Glas 4, KONZEPT.md Abschnitt 4): Startwerte gemessen an Sarevocs Discord-Vorbild, endgültig stellt Sarevoc sie ein.

    /// <summary>Deckung des Leuchtsaums an der Haarlinie beim Überfahren (in Ruhe kein Saum).</summary>
    public double LeuchtsaumUeberfahren { get; init; } = 0.12;

    /// <summary>Deckung beim Tastaturfokus (nur bei Tastaturbedienung, nicht nach einem Mausklick).</summary>
    public double LeuchtsaumFokus { get; init; } = 0.12;

    /// <summary>Deckung eingerastet (gewählter Reiter); dann nimmt auch die Haarlinie den Leuchtton an.</summary>
    public double LeuchtsaumEingerastet { get; init; } = 0.19;

    /// <summary>Wie weit der Saum nach außen reicht, in DIP; am Ende ist er genau null.</summary>
    public double LeuchtsaumReichweite { get; init; } = 14;

    /// <summary>Abfall nach außen als Hochzahl: Deckung mal (1 - x) hoch Abfall.</summary>
    public double LeuchtsaumAbfall { get; init; } = 1.5;

    /// <summary>Stärke oben und unten im Verhältnis zur Seite (1): das Licht fällt nach unten.</summary>
    public double LeuchtsaumOben { get; init; } = 0.45;

    public double LeuchtsaumUnten { get; init; } = 1.4;

    /// <summary>Leuchten nach innen, unter der Haarlinie (Vorgabe aus: es machte den ersten Entwurf milchig).</summary>
    public double LeuchtsaumInnen { get; init; }

    /// <summary>Ein- und Ausblenden in Millisekunden.</summary>
    public double LeuchtsaumEinblenden { get; init; } = 150;

    // Spotlicht (Stilfamilie 1, KONZEPT.md Abschnitt 5a): die Haarlinie leuchtet dort auf, wo die Maus ist.

    /// <summary>Deckung des Lichts auf der Haarlinie an der Mausstelle; 0 schaltet das Spotlicht ab.</summary>
    public double SpotlichtRand { get; init; } = 0.55;

    /// <summary>Deckung des Hauchs innen an der Mausstelle.</summary>
    public double SpotlichtInnen { get; init; } = 0.05;

    /// <summary>Radius des Lichtkegels in DIP; bis dorthin fällt er auf null.</summary>
    public double SpotlichtRadius { get; init; } = 80;

    /// <summary>Ein- und Ausblenden in Millisekunden, wenn die Maus kommt oder geht.</summary>
    public double SpotlichtEinblenden { get; init; } = 120;

    // Lichtkante (Stilfamilie 2): Farbverlauf rundum, solange etwas läuft (Glas.Laeuft).

    /// <summary>Breite der Kante in DIP (mindestens ein Gerätepixel).</summary>
    public double LichtkanteBreite { get; init; } = 1.5;

    /// <summary>Deckung des weichen Scheins außen an der Kante; 0 ohne Schein.</summary>
    public double LichtkanteSchein { get; init; } = 0.35;

    /// <summary>Wie weit der Schein reicht, in DIP.</summary>
    public double LichtkanteReichweite { get; init; } = 10;

    /// <summary>Wie stark der helle Komet aus dem Verlauf heraussticht (0 gleichmäßig, 1 nur der Komet).</summary>
    public double LichtkanteKomet { get; init; } = 0.45;

    /// <summary>Dauer eines Umlaufs in Millisekunden; 0 steht still.</summary>
    public double LichtkanteUmlauf { get; init; } = 2400;

    // Neue Glanzschichten (Glint Abschnitt 5, "Mehr Glanz"): Jede Stärke 0 heißt aus und ist die Vorgabe; dann wird die
    // Schicht gar nicht gebaut, und jedes Element zeichnet Pixel für Pixel wie vorher ("Satin"). Die übrigen Werte einer
    // Schicht sind Startwerte, die erst wirken, wenn ihre Stärke über 0 liegt. Fertige Zusammenstellungen: Glanzmischung.

    /// <summary>Glanzkappe: Deckung des breiten, weichen Lichtscheins über der oberen Hälfte (Licht auf einer Kuppel).</summary>
    public double GlanzkappeStaerke { get; init; }

    /// <summary>Wo die Unterkante der Glanzkappe in der Mitte liegt, als Anteil der Höhe; zu den Seiten steigt sie leicht an.</summary>
    public double GlanzkappeHoehe { get; init; } = 0.46;

    /// <summary>Abstand der Glanzkappe von der Haarlinie in DIP, auf ganze Gerätepixel gerundet.</summary>
    public double GlanzkappeEinzug { get; init; } = 1;

    /// <summary>Wie klar die Unterkante ist: 0 läuft weich aus, 1 endet mit halber Deckung als klare Kante.</summary>
    public double GlanzkappeSchaerfe { get; init; } = 0.6;

    /// <summary>Horizontband: Deckung des schmalen hellen Streifens quer über das Glas (Spiegelung eines Fensters).</summary>
    public double HorizontbandStaerke { get; init; }

    /// <summary>Lage der Mitte des Streifens als Anteil der Höhe (ein Drittel: etwas über der Mitte).</summary>
    public double HorizontbandLage { get; init; } = 0.33;

    /// <summary>
    /// Breite des Streifens in DIP, mindestens ein Gerätepixel; darüber und darunter ein weicher Hof, der je eine
    /// Streifenbreite weit auf null ausläuft.
    /// </summary>
    public double HorizontbandBreite { get; init; } = 1.5;

    /// <summary>Auslauf an den Enden als Anteil der Länge (0 bis 0,5): so weit wird der Streifen links und rechts schwächer.</summary>
    public double HorizontbandAuslauf { get; init; } = 0.3;

    /// <summary>Bodenleuchten: Deckung des hellen Bogens an der Unterkante, als träte Licht unten durch das Glas aus.</summary>
    public double BodenleuchtenStaerke { get; init; }

    /// <summary>Breite des Bogens innen an der Unterkante in DIP; darüber ein weicher Schein.</summary>
    public double BodenleuchtenBreite { get; init; } = 3;

    /// <summary>Tönung des Bodenleuchtens: 0 im Ton der Spiegelungen (weiß), 1 ganz in der Akzentfarbe (Glas.Akzent).</summary>
    public double BodenleuchtenToenung { get; init; } = 0.6;

    /// <summary>
    /// Sternglanz: Deckung des winzigen Funkens mit vier auslaufenden Strahlen am Randglanz (geschliffenes Glas). Die Strahlen
    /// reichen höchstens ein Fünftel ihrer Länge über die Kante und nie über das Element hinaus; an einer scharfen Ecke rückt
    /// der Funke schräg nach innen.
    /// </summary>
    public double SternglanzStaerke { get; init; }

    /// <summary>Halbmesser des hellen Kerns in DIP.</summary>
    public double SternglanzGroesse { get; init; } = 1.2;

    /// <summary>Länge der Strahlen von der Mitte bis zur Spitze in DIP.</summary>
    public double SternglanzStrahlen { get; init; } = 5;

    /// <summary>Wie viele Funken (1 bis 3): auf dem Randglanz, dazu auf dem Gegenglanz und oben rechts, je kleiner.</summary>
    public double SternglanzAnzahl { get; init; } = 1;

    /// <summary>Zweitglanz: Deckung des kleinen zweiten Glanzlichts auf der Haarlinie neben dem Randglanz (zwei Lichtquellen).</summary>
    public double ZweitglanzStaerke { get; init; }

    /// <summary>
    /// Wie weit er vom Randglanz entfernt liegt, entlang der Kante zur linken Seite hin (dort ist die Haarlinie schwach), als
    /// Anteil des Eckradius wie die Länge des Randglanzes; ohne Rundung oben links (Fase, Pfeil, Sechseck) als Anteil der
    /// halben kurzen Seite. Mindestens so weit, dass zwischen beiden eine dunkle Lücke bleibt, höchstens bis zum unteren
    /// Ende der linken Seite (bei der Pille die Mitte unten): größere Werte bleiben dort stehen.
    /// </summary>
    public double ZweitglanzAbstand { get; init; } = 0.8;

    /// <summary>Seine halbe Länge entlang der Kante in DIP.</summary>
    public double ZweitglanzGroesse { get; init; } = 2.5;

    /// <summary>
    /// Fresnel-Rand: Deckung des Lichts auf der Haarlinie und der Reihe darunter an den Rundungen. Gilt auf jeder Form, wo
    /// sich ihre Kontur biegt (<see cref="Fresnelrand"/>): Bögen jeder Größe und Fasen auf ihrer ganzen Länge, ein Kreis
    /// oder rundes Loch ringsum, scharfe Ecken (Pfeilspitze, rechter Winkel, die Stellen, an denen sich die Teile eines
    /// Verbunds im Knick schneiden) als kurzes Licht; dahinter läuft es kurz aus. Schneiden sich zwei Bögen flach, läuft ihr
    /// Licht ohne Kerbe durch. Nirgends leuchtet es stärker als an einer Biegung.
    /// </summary>
    public double FresnelStaerke { get; init; }

    /// <summary>Wie viel stärker die Rundungen leuchten als die geraden Stücke (3: dreimal so stark, 1: rundum gleich).</summary>
    public double FresnelVerhaeltnis { get; init; } = 3;

    /// <summary>Tiefenlinie: Deckung der feinen dunklen Linie innen unter der oberen Haarlinie (das Glas wirkt dicker).</summary>
    public double TiefenlinieStaerke { get; init; }

    /// <summary>Abstand der Tiefenlinie unter der Haarlinie in Gerätepixeln (0: direkt darunter).</summary>
    public double TiefenlinieAbstand { get; init; } = 1;

    /// <summary>Prismensaum: Deckung des Farbsaums an der Haarlinie, oben kühl, unten warm (Lichtbrechung an dickem Glas).</summary>
    public double PrismensaumStaerke { get; init; }

    /// <summary>Lage des Saums in Gerätepixeln von außen: 0 auf der Haarlinie, 1 die Reihe darunter.</summary>
    public double PrismensaumVersatz { get; init; } = 1;

    /// <summary>Druckleuchten: Deckung des Lichts, das sich gedrückt in der Mitte des Glases sammelt (nur im Zustand gedrückt).</summary>
    public double DruckleuchtenStaerke { get; init; }

    /// <summary>
    /// Größe des Lichts als Anteil des Körpers: Bei 1 ist es so breit wie das Glas und 1,3-mal so hoch, um eine Mitte etwas
    /// unter der halben Höhe; was über den Körper hinausreicht, fällt weg.
    /// </summary>
    public double DruckleuchtenGroesse { get; init; } = 0.6;

    /// <summary>Tönung des Druckleuchtens: 0 im Ton der Spiegelungen (weiß), 1 ganz in der Akzentfarbe.</summary>
    public double DruckleuchtenToenung { get; init; } = 0.5;

    /// <summary>Glanzwischer: Deckung des schrägen Lichtstreifens, der beim Überfahren einmal über das Glas läuft.</summary>
    public double GlanzwischerStaerke { get; init; }

    /// <summary>Neigung des Streifens gegen die Senkrechte in Grad (positiv: oben nach rechts geneigt).</summary>
    public double GlanzwischerWinkel { get; init; } = 20;

    /// <summary>Breite des Streifens in DIP, quer zu seiner Richtung gemessen.</summary>
    public double GlanzwischerBreite { get; init; } = 14;

    /// <summary>Dauer eines Laufs von links nach rechts in Millisekunden; 0 läuft nicht.</summary>
    public double GlanzwischerDauer { get; init; } = 650;

    /// <summary>Alle Werte mit Namen, in der Reihenfolge der Deklaration (für Export, Einstellpult und Glint).</summary>
    public IReadOnlyList<(string Name, double Wert)> Werte() => Wertesatz.Lesen(this);

    /// <summary>
    /// Die Werte, die eine Mischung festlegt: die Glanzwerte (Kantenspiegel bis Gegenglanz) und alle Glanzschichten.
    /// Schatten, Zustandsstärken, Leuchtsaum, Spotlicht und Lichtkante gehören nicht dazu.
    /// </summary>
    private static readonly PropertyInfo[] Mischwerte = [.. typeof(Glanzprofil).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(e => e.PropertyType == typeof(double) && !Bleibt(e.Name))
        .OrderBy(e => e.MetadataToken)];

    private static bool Bleibt(string name) =>
        name is nameof(Schatten) or nameof(Ueberfahren) or nameof(Gedrueckt) or nameof(Dunkel)
        || name.StartsWith("Leuchtsaum", StringComparison.Ordinal)
        || name.StartsWith("Spotlicht", StringComparison.Ordinal)
        || name.StartsWith("Lichtkante", StringComparison.Ordinal);

    /// <summary>Die Mischung Satin: das Standardprofil, alle neuen Glanzschichten aus.</summary>
    public static Glanzprofil Satin { get; } = Standard.Mischen(Glanzmischung.Satin);

    /// <summary>Die Mischung Hochglanz: Glanzkappe, Sternglanz und Zweitglanz auf dem Standardprofil.</summary>
    public static Glanzprofil Hochglanz { get; } = Standard.Mischen(Glanzmischung.Hochglanz);

    /// <summary>Die Mischung Gel: kräftige Glanzkappe und Bodenleuchten auf dem Standardprofil.</summary>
    public static Glanzprofil Gel { get; } = Standard.Mischen(Glanzmischung.Gel);

    /// <summary>Die Mischung Kristall: Sternglanz, Prismensaum und Tiefenlinie auf dem Standardprofil.</summary>
    public static Glanzprofil Kristall { get; } = Standard.Mischen(Glanzmischung.Kristall);

    /// <summary>Die Mischung Matt: fast keine Spiegelung, auf dem Standardprofil.</summary>
    public static Glanzprofil Matt { get; } = Standard.Mischen(Glanzmischung.Matt);

    /// <summary>
    /// Legt eine Mischung auf dieses Profil: Die Glanzwerte und alle Glanzschichten kommen aus der Mischung, Leuchtsaum,
    /// Spotlicht, Lichtkante, Schatten und Zustandsstärken bleiben, wie sie sind. So behält eine Farbstellung mit eigenem
    /// Leuchtsaum ihn auch in der Mischung Hochglanz.
    /// </summary>
    public Glanzprofil Mischen(Glanzmischung mischung)
    {
        var vorlage = Vorlage(mischung);
        var neu = this with { };
        foreach (var e in Mischwerte) e.SetValue(neu, e.GetValue(vorlage));
        return neu;
    }

    /// <summary>Die Werte einer Mischung auf dem Standardprofil; nur die Glanzwerte und Glanzschichten zählen davon.</summary>
    private static Glanzprofil Vorlage(Glanzmischung mischung)
    {
        var s = new Glanzprofil();
        return mischung switch
        {
            Glanzmischung.Satin => s,
            Glanzmischung.Hochglanz => s with
            {
                Kantenspiegel = 0.56,
                GlanzkappeStaerke = 0.13, GlanzkappeHoehe = 0.44, GlanzkappeEinzug = 1.5, GlanzkappeSchaerfe = 0.55,
                SternglanzStaerke = 0.9, SternglanzGroesse = 1.2, SternglanzStrahlen = 6, SternglanzAnzahl = 2,
                ZweitglanzStaerke = 0.7, ZweitglanzAbstand = 0.8, ZweitglanzGroesse = 2.5,
            },
            Glanzmischung.Gel => s with
            {
                Schein = 0.3, Kantenspiegel = 0.45,
                GlanzkappeStaerke = 0.24, GlanzkappeHoehe = 0.47, GlanzkappeEinzug = 1.5, GlanzkappeSchaerfe = 0.7,
                BodenleuchtenStaerke = 0.45, BodenleuchtenBreite = 3.5, BodenleuchtenToenung = 0.75,
            },
            // Kristall lebt von feinen Linien; bei 100 Prozent sah es mit Prismensaum 0,35 eine Reihe unter der Haarlinie fast
            // wie Satin aus. Der Saum liegt deshalb auf der Haarlinie selbst, Tiefenlinie und Funken sind etwas kräftiger.
            Glanzmischung.Kristall => s with
            {
                Kantenspiegel = 0.6, RandglanzLaenge = 0.42,
                SternglanzStaerke = 1, SternglanzGroesse = 1.3, SternglanzStrahlen = 7, SternglanzAnzahl = 3,
                PrismensaumStaerke = 0.5, PrismensaumVersatz = 0,
                TiefenlinieStaerke = 0.55, TiefenlinieAbstand = 1,
            },
            Glanzmischung.Matt => s with
            {
                Kantenspiegel = 0.12, Randlicht = 0.35, Schein = 0.12, Randglanz = 0.18, RandglanzHof = 0.3, Gegenglanz = 0.15,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mischung), mischung, "unbekannte Glanzmischung"),
        };
    }
}

/// <summary>
/// Fertige Zusammenstellungen der Glanzschichten (Glint Abschnitt 5). <see cref="Glanzprofil.Mischen"/> legt eine davon auf
/// ein Profil; fertig gemischt auf dem Standardprofil gibt es sie als <see cref="Glanzprofil.Hochglanz"/> und so weiter.
/// </summary>
public enum Glanzmischung
{
    /// <summary>Der Stand von Glas 3 und 4: alle neuen Glanzschichten aus (das Standardprofil).</summary>
    Satin,

    /// <summary>Glanzkappe, Sternglanz und Zweitglanz: poliertes Glas mit zwei Lichtquellen.</summary>
    Hochglanz,

    /// <summary>Kräftige Glanzkappe und Bodenleuchten im Akzentton: dickes, gegossenes Gel.</summary>
    Gel,

    /// <summary>Sternglanz, Prismensaum und Tiefenlinie: geschliffenes, dickes Kristallglas.</summary>
    Kristall,

    /// <summary>Fast keine Spiegelung: Kantenspiegel, Randlicht, Schein, Randglanz und Gegenglanz sehr klein.</summary>
    Matt,
}

/// <summary>
/// Die Spiegelungen von Glas 3 für eine Glasfläche: Durchlicht unten, Schein oben, Randlicht (vier Pixelringe, nach innen
/// schwächer), Kantenspiegel (erste Pixelreihe innen) und zwei punktuelle Glanzlichter auf der Haarlinie selbst: der
/// Randglanz mit Hof an der Rundung oben links, der Gegenglanz an der Rundung unten rechts. Weil sie auf der Haarlinie
/// liegen, zeichnen die Elemente das Glanzbild nach ihr. Geometrie und Pinsel entstehen je Fläche einmal und sind
/// eingefroren. Ausgeschnitten wird über die gefüllte Form und Pinsel mit fester Lage, nicht über PushClip oder PushOpacity.
/// <para>
/// Darüber die neuen Glanzschichten der Glint Abschnitt 5, von unten nach oben: Bodenleuchten, Glanzkappe, Horizontband,
/// Tiefenlinie, Fresnel-Rand und Prismensaum unter den punktuellen Glanzlichtern, Zweitglanz und Sternglanz zuoberst. Jede
/// entsteht nur, wenn ihre Stärke im Profil über 0 liegt; mit dem Standardprofil bleibt der Stapel genau der von Glas 3.
/// Die Formen von Zweitglanz und Sternglanz suchen auf einem Umriss erst die Kante; sie sind deshalb je Fläche und
/// Lagewerten gemerkt und werden von allen gleich großen Elementen geteilt, nur die Pinsel entstehen je Glanzbild. Ebenso
/// die Abschnitte des Fresnel-Rands (<see cref="Fresnelrand"/>, je Fläche).
/// Das Druckleuchten zeichnet der <see cref="Glaskoerper"/> (nur gedrückt), den Glanzwischer das Element
/// <see cref="Glanzwischer"/>.
/// </para>
/// </summary>
internal sealed class Glanzbild
{
    private readonly List<(Geometry Form, Brush Pinsel)> _schichten = [];

    /// <summary>Ton des Prismensaums oben (kühl) und unten (warm).</summary>
    private static readonly Color Kuehl = Color.FromRgb(0x6E, 0xC8, 0xFF), Warm = Color.FromRgb(0xFF, 0xB0, 0x5E);

    private Glanzbild(Glasflaeche f, Glanzprofil p, Color ton, double staerke, Brush? durchlicht, Color akzent)
    {
        Flaeche = f;
        Profil = p;
        Ton = ton;
        Staerke = staerke;
        Durchlicht = durchlicht;
        Akzent = akzent;
        Haar = f.Ring(0, Glasgeometrie.Haarlinie);
        Koerper = f.Form(Glasgeometrie.Haarlinie);
        // Ohne Stärke wäre jede Schicht durchsichtig (die Rinne des Leuchtschalters braucht nur Haarlinie und Körper).
        if (f.Breite < 6 || f.Hoehe < 6 || staerke <= 0) return;

        var st = staerke;
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        // Jede Spiegelung füllt nur ihren eigenen Ausschnitt (einmal beim Aufbau geschnitten), nicht den ganzen Körper: Ein
        // radialer Verlauf über die volle Fläche kostete bei 60 Leuchtschaltern rund 30 ms mehr (Software, 175 %).
        if (durchlicht is RadialGradientBrush rund)
        {
            var (m, rx, ry) = (f.Dip(b / 2, h * 1.22), b * 0.46 / s, h * 0.55 / s);
            Hinzu(KoerperSchnitt(f, Koerper, m, rx, ry), Pinselwerk.Gelegt(rund, m, rx, ry, Math.Min(1, st)));
        }
        {
            var (m, rx, ry) = (f.Dip(b / 2, -h * 0.32), b * 0.62 / s, h * 0.8 / s);
            Hinzu(KoerperSchnitt(f, Koerper, m, rx, ry), Pinselwerk.Strahl(m, rx, ry, Pinselwerk.Alpha(ton, p.Schein * st)));
        }

        double[] rand = [0.19, 0.10, 0.055, 0.024];
        for (var i = 0; i < rand.Length; i++)
        {
            var a = rand[i] * p.Randlicht * st;
            Hinzu(f.Ring(2 + i, 3 + i), Pinselwerk.Senkrecht((Pinselwerk.Alpha(ton, a), 0), (Pinselwerk.Alpha(ton, a * 0.2), 0.5), (Pinselwerk.Alpha(ton, a * 0.7), 1)));
        }
        Hinzu(f.Ring(1, 2), Pinselwerk.Senkrecht(
            (Pinselwerk.Alpha(ton, p.Kantenspiegel * st), 0), (Pinselwerk.Alpha(ton, 0), 0.3),
            (Pinselwerk.Alpha(ton, 0), 0.72), (Pinselwerk.Alpha(ton, p.Kantenspiegel * 0.48 * st), 1)));

        // Neue Glanzschichten unter den punktuellen Glanzlichtern (Glint Abschnitt 5); jede nur, wenn ihre Stärke über 0 liegt.
        Bodenleuchten(f, p, ton, akzent, st);
        Glanzkappe(f, p, ton, st);
        Horizontband(f, p, ton, st);
        Tiefenlinie(f, p);
        FresnelRand(f, p, ton, st);
        Prismensaum(f, p, st);

        // Randglanz und Gegenglanz: Mitte auf dem Bogen durch die Mitte der Haarlinie (Eckmittelpunkt r, r), längs der Kante
        // gestreckt (Drehung 90 - Lage), ausgeschnitten auf die äußeren Pixelreihen. So liegt das Licht auf der Kante selbst.
        var r = f.Radius;
        var rb = Math.Max(0.5, r - 0.5);
        var laenge = Math.Max(3, p.RandglanzLaenge * r);
        {
            var w = p.RandglanzLage * Math.PI / 180;
            var m = f.Dip(r + rb * Math.Cos(w), r - rb * Math.Sin(w));
            var winkel = 90 - p.RandglanzLage;
            Glanzlicht(f, m, laenge * 1.5, laenge * 0.55, winkel, 4, p.RandglanzHof * p.Randglanz * st);
            Glanzlicht(f, m, laenge, laenge * 0.5, winkel, p.RandglanzBreite, p.Randglanz * st);
        }
        {
            var w = p.GegenglanzLage * Math.PI / 180;
            var m = f.Dip(b - r + rb * Math.Cos(w), h - r - rb * Math.Sin(w));
            Glanzlicht(f, m, laenge * 0.7, laenge * 0.35, 90 - p.GegenglanzLage, Math.Max(1, p.RandglanzBreite - 0.5), p.Gegenglanz * st);
        }

        // Zuoberst die neuen punktuellen Lichter: Zweitglanz neben dem Randglanz, Funken darauf.
        Zweitglanz(f, p, st);
        Sternglanz(f, p, st);
    }

    /// <summary>
    /// Ein Glanzlicht auf der Kante: eine längs der Kante gestreckte Ellipse um <paramref name="m"/> (Halbachsen in
    /// Gerätepixeln, gedreht um <paramref name="winkel"/> Grad), ausgeschnitten auf die äußeren <paramref name="ring"/> Pixelreihen.
    /// </summary>
    private void Glanzlicht(Glasflaeche f, Point m, double rx, double ry, double winkel, double ring, double deckung)
    {
        var s = f.Skala;
        Hinzu(Glanzlichtform(f, m, rx, ry, winkel, ring), Pinselwerk.Strahl(m, rx / s, ry / s, Pinselwerk.Alpha(Colors.White, deckung), winkel));
    }

    /// <summary>
    /// Die Form eines Glanzlichts (wie <see cref="Glanzlicht"/>) ohne Pinsel, eingefroren; je Fläche und Lage gemerkt
    /// (0.5.0-beta.5): Ruhe, Überfahren und Drücken haben dieselben Formen, nur andere Pinsel.
    /// </summary>
    private static PathGeometry Glanzlichtform(Glasflaeche f, Point m, double rx, double ry, double winkel, double ring) =>
        Gemerkt(Schnittvorrat, (f, ring, m.X, m.Y, rx, ry, winkel), () => Schnitt(f.Ring(0, ring), m, rx / f.Skala, ry / f.Skala, winkel));

    /// <summary>
    /// Der Teil des Körpers (innerhalb der Haarlinie) in einer Ellipse, je Fläche und Lage gemerkt (0.5.0-beta.5) wie
    /// <see cref="Glanzlichtform"/>. Der Körper hängt nur an der Fläche; im Schlüssel steht er als Ring -1.
    /// </summary>
    private static PathGeometry KoerperSchnitt(Glasflaeche f, Geometry koerper, Point m, double rx, double ry) =>
        Gemerkt(Schnittvorrat, (f, -1, m.X, m.Y, rx, ry, 0), () => Schnitt(koerper, m, rx, ry));

    private static readonly Dictionary<(Glasflaeche, double, double, double, double, double, double), PathGeometry> Schnittvorrat = [];

    /// <summary>
    /// Bodenleuchten: Licht tritt unten durch das Glas aus. Ein heller Bogen innen an der Unterkante (so breit wie
    /// <see cref="Glanzprofil.BodenleuchtenBreite"/>) und ein weicher Schein darüber, beide zur Mitte unten am hellsten und im
    /// Akzentton getönt.
    /// </summary>
    private void Bodenleuchten(Glasflaeche f, Glanzprofil p, Color ton, Color akzent, double st)
    {
        if (p.BodenleuchtenStaerke <= 0) return;
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var farbe = Farbton.Mischen(Color.FromRgb(ton.R, ton.G, ton.B), Color.FromRgb(akzent.R, akzent.G, akzent.B), p.BodenleuchtenToenung);
        var a = p.BodenleuchtenStaerke * st;
        var m = f.Dip(b / 2, h);
        var (sx, sy) = (b * 0.5 / s, h * 0.55 / s);
        Hinzu(Schnitt(Koerper, m, sx, sy), Pinselwerk.Strahl(m, sx, sy, Pinselwerk.Alpha(farbe, a * 0.4)));
        var breite = Glasgeometrie.Linie(p.BodenleuchtenBreite, s);
        var (bx, by) = (b * 0.56 / s, h * 0.8 / s);
        Hinzu(Schnitt(f.Ring(Glasgeometrie.Haarlinie, Glasgeometrie.Haarlinie + breite), m, bx, by), Pinselwerk.Strahl(m, bx, by, Pinselwerk.Alpha(farbe, a)));
    }

    /// <summary>
    /// Glanzkappe: ein breiter, weicher Lichtschein über der oberen Hälfte, um den Einzug von der Haarlinie abgesetzt, oben
    /// am hellsten. Die Unterkante ist ein flacher Bogen (Licht auf einer Kuppel); wie klar sie endet, sagt die Schärfe.
    /// </summary>
    private void Glanzkappe(Glasflaeche f, Glanzprofil p, Color ton, double st)
    {
        if (p.GlanzkappeStaerke <= 0) return;
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var einzug = Glasgeometrie.Haarlinie + Math.Max(0, Glasgeometrie.Raster(p.GlanzkappeEinzug, s));
        var unten = Math.Round(Math.Clamp(p.GlanzkappeHoehe, 0, 1) * h);
        if (unten < einzug + 2) return;
        // Die Unterkante: unterster Punkt einer flachen Ellipse, deren Mitte weit über dem Glas liegt.
        var ry = unten * 2;
        var rx = b * 0.95;
        var m = f.Dip(b / 2, unten - ry);
        var a = p.GlanzkappeStaerke * st;
        var kante = a * 0.6 * Math.Clamp(p.GlanzkappeSchaerfe, 0, 1);
        Hinzu(Schnitt(f.Form(einzug), m, rx / s, ry / s), Pinselwerk.Senkrecht(
            (Pinselwerk.Alpha(ton, a), 0), (Pinselwerk.Alpha(ton, a * 0.62), 0.5), (Pinselwerk.Alpha(ton, Math.Max(kante, a * 0.12)), 0.92), (Pinselwerk.Alpha(ton, kante), 1)));
    }

    /// <summary>
    /// Horizontband: ein schmaler heller Streifen quer über den Körper bei <see cref="Glanzprofil.HorizontbandLage"/>, zu den
    /// Enden über den Auslauf auf null auslaufend. Darüber und darunter ein weicher Hof: ein flacher runder Verlauf um die
    /// Mitte des Streifens, der am Rand des Streifens knapp ein Drittel deckt und je eine Streifenbreite darüber und darunter
    /// auf null fällt, zu den Enden hin ebenso. Senkrecht nimmt er so von Pixelreihe zu Pixelreihe ab, ohne Stufe.
    /// </summary>
    private void Horizontband(Glasflaeche f, Glanzprofil p, Color ton, double st)
    {
        if (p.HorizontbandStaerke <= 0) return;
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var w = Glasgeometrie.Linie(p.HorizontbandBreite, s);
        var oben = Math.Round(Math.Clamp(p.HorizontbandLage, 0, 1) * h - w / 2);
        var auslauf = Math.Clamp(p.HorizontbandAuslauf, 0, 0.5);
        var a = p.HorizontbandStaerke * st;
        var m = f.Dip(b / 2, oben + w / 2);
        Hinzu(Rechteckschnitt(Koerper, new Rect(f.Dip(0, oben - w), f.Dip(b, oben + 2 * w))),
            Pinselwerk.Rund(m, b / 2 / s, 1.5 * w / s, (Pinselwerk.Alpha(ton, a * 0.45), 0), (Pinselwerk.Alpha(ton, 0), 1)));
        Hinzu(Rechteckschnitt(Koerper, new Rect(f.Dip(0, oben), f.Dip(b, oben + w))), Pinselwerk.Waagerecht(
            (Pinselwerk.Alpha(ton, 0), 0), (Pinselwerk.Alpha(ton, a), auslauf), (Pinselwerk.Alpha(ton, a), 1 - auslauf), (Pinselwerk.Alpha(ton, 0), 1)));
    }

    /// <summary>
    /// Tiefenlinie: eine Reihe Gerätepixel, um den Abstand unter der Haarlinie, oben dunkel und zu den Seiten auslaufend.
    /// Eine Kante im Glas, keine Spiegelung: Sie hängt nicht an der Zustandsstärke.
    /// </summary>
    private void Tiefenlinie(Glasflaeche f, Glanzprofil p)
    {
        if (p.TiefenlinieStaerke <= 0) return;
        var a = Glasgeometrie.Haarlinie + Math.Max(0, Math.Round(p.TiefenlinieAbstand));
        var schwarz = Colors.Black;
        Hinzu(f.Ring(a, a + 1), Pinselwerk.Senkrecht((Pinselwerk.Alpha(schwarz, p.TiefenlinieStaerke), 0), (Pinselwerk.Alpha(schwarz, 0), 0.35)));
    }

    /// <summary>
    /// Fresnel-Rand: Haarlinie und die Reihe darunter leuchten überall ein wenig (Stärke durch Verhältnis) und an jeder
    /// Biegung der Kontur voll (die Stärke). Wo, rechnet <see cref="Fresnelrand"/> einmal je Fläche an der Kontur selbst,
    /// mit und ohne Umriss gleich: Bögen jeder Größe und Fasen auf ihrer ganzen Länge, scharfe Ecken als kurzes Licht,
    /// dahinter ein kurzer Auslauf, nie doppelt. Hier entstehen nur die Pinsel; Überfahren und Drücken bauen keine Geometrie.
    /// </summary>
    private void FresnelRand(Glasflaeche f, Glanzprofil p, Color ton, double st)
    {
        if (p.FresnelStaerke <= 0) return;
        var rand = Fresnel = Fresnelrand.Holen(f);
        var a = p.FresnelStaerke * st;
        var v = Math.Max(1, p.FresnelVerhaeltnis);
        Hinzu(rand.Ring, Pinselwerk.Flaeche(Pinselwerk.Alpha(ton, a / v)));
        if (v <= 1) return;
        // Darüber der Rest der Stärke, an jeder Stelle mal ihrer Deckung längs der Kante. Beide Schichten liegen auf
        // denselben Rändern, so wird ein halb gedecktes Pixel am Rand nie heller als ein ganzes an der Biegung.
        var voll = a * (1 - 1 / v);
        if (rand.Licht is { } licht) Hinzu(licht, Pinselwerk.Flaeche(Pinselwerk.Alpha(ton, voll)));
        foreach (var l in rand.Auslaeufe)
            Hinzu(l.Form, Pinselwerk.Verlauf(l.Von, l.Bis, Pinselwerk.Alpha(ton, voll * l.DeckungVon), Pinselwerk.Alpha(ton, voll * l.DeckungBis)));
    }

    /// <summary>Prismensaum: eine Reihe Gerätepixel um den Versatz von außen, oben im kühlen, unten im warmen Ton.</summary>
    private void Prismensaum(Glasflaeche f, Glanzprofil p, double st)
    {
        if (p.PrismensaumStaerke <= 0) return;
        var v = Math.Max(0, Math.Round(p.PrismensaumVersatz));
        var a = p.PrismensaumStaerke * st;
        Hinzu(f.Ring(v, v + 1), Pinselwerk.Senkrecht(
            (Pinselwerk.Alpha(Kuehl, a), 0), (Pinselwerk.Alpha(Kuehl, 0), 0.4), (Pinselwerk.Alpha(Warm, 0), 0.6), (Pinselwerk.Alpha(Warm, a), 1)));
    }

    /// <summary>Ein gebauter Zweitglanz: Hof und Kern auf der Kante (eingefroren), Mitte und halbe Länge in DIP, Drehung in Grad.</summary>
    private sealed record Zweitlage(Geometry Hof, Geometry Kern, Point Mitte, double Laenge, double Winkel);

    /// <summary>Ein gebauter Funke: Strahlen und Kern (eingefroren), Mitte, Halbmesser des Kerns und Länge der Strahlen in DIP.</summary>
    private sealed record Funke(Geometry Stern, Geometry Punkt, Point Mitte, double Kern, double Strahl);

    private static readonly Dictionary<(Glasflaeche, double, double, double, double, double), Zweitlage> Zweitvorrat = [];
    private static readonly Dictionary<(Glasflaeche, double, double, int, double, double), Funke[]> Funkenvorrat = [];

    /// <summary>Wie weit die Strahlen eines Funkens über die Kante des Glases reichen dürfen, als Anteil ihrer Länge.</summary>
    internal const double Funkenueberstand = 0.2;

    /// <summary>Gemerkt je Schlüssel, für alle gleich großen Elemente; begrenzt, damit ein Regler nicht alles behält.</summary>
    private static TWert Gemerkt<TSchluessel, TWert>(Dictionary<TSchluessel, TWert> vorrat, TSchluessel schluessel, Func<TWert> bauen)
        where TSchluessel : notnull
    {
        lock (vorrat)
        {
            if (vorrat.TryGetValue(schluessel, out var fertig)) return fertig;
            var neu = bauen();
            if (vorrat.Count > 256) vorrat.Clear();
            vorrat[schluessel] = neu;
            return neu;
        }
    }

    /// <summary>
    /// Zweitglanz: ein kleines Glanzlicht wie der Randglanz, um den Abstand entlang der Kante zur linken Seite hin versetzt,
    /// längs der Kante gestreckt, mit einem Hauch Hof. Die Form ist je Fläche und Lagewerten gemerkt.
    /// </summary>
    private void Zweitglanz(Glasflaeche f, Glanzprofil p, double st)
    {
        if (p.ZweitglanzStaerke <= 0) return;
        var z = Gemerkt(Zweitvorrat, (f, p.ZweitglanzAbstand, p.ZweitglanzGroesse, p.RandglanzLage, p.RandglanzLaenge, p.RandglanzBreite), () => ZweitlageBauen(f, p));
        var a = p.ZweitglanzStaerke * st;
        var weiss = Colors.White;
        Hinzu(z.Hof, Pinselwerk.Strahl(z.Mitte, z.Laenge * 1.5, z.Laenge * 0.55, Pinselwerk.Alpha(weiss, a * 0.45), z.Winkel));
        Hinzu(z.Kern, Pinselwerk.Strahl(z.Mitte, z.Laenge, z.Laenge * 0.45, Pinselwerk.Alpha(weiss, a), z.Winkel));
    }

    /// <summary>
    /// Wo der Zweitglanz liegt: um den Abstand entlang der Kante vom Randglanz entfernt, gemessen am Eckradius oder, ohne
    /// Rundung oben links, an der halben kurzen Seite; mindestens so weit, dass zwischen beiden eine dunkle Lücke bleibt.
    /// </summary>
    private static Zweitlage ZweitlageBauen(Glasflaeche f, Glanzprofil p)
    {
        var s = f.Skala;
        var l = Math.Max(2, p.ZweitglanzGroesse * s);
        var randglanz = Math.Max(3, p.RandglanzLaenge * f.Radius);
        var bezug = f.Radius > 0 ? f.Radius : Math.Min(f.Breite, f.Hoehe) / 2;
        var weg = Math.Max(Math.Max(0, p.ZweitglanzAbstand) * bezug, randglanz + 2 * l);
        var (x, y, winkel) = Kantenweg(f, p.RandglanzLage, weg);
        var q = AufkantePx(f, x, y);
        var m = f.Dip(q.X, q.Y);
        return new Zweitlage(Glanzlichtform(f, m, l * 1.5, l * 0.55, winkel, 3), Glanzlichtform(f, m, l, l * 0.45, winkel, Math.Max(1, p.RandglanzBreite)), m, l / s, winkel);
    }

    /// <summary>
    /// Sternglanz: ein winziger Funke mit vier Strahlen (oben, unten, links, rechts), die nach außen dünner und schwächer
    /// werden, mit hellem Kern. Der erste sitzt am Randglanz, der zweite am Gegenglanz, der dritte oben rechts. Die Formen
    /// sind je Fläche und Lagewerten gemerkt (<see cref="FunkenBauen"/>), nur die Pinsel hängen an der Stärke.
    /// </summary>
    private void Sternglanz(Glasflaeche f, Glanzprofil p, double st)
    {
        if (p.SternglanzStaerke <= 0 || p.SternglanzStrahlen <= 0) return;
        var anzahl = (int)Math.Clamp(Math.Round(p.SternglanzAnzahl), 1, 3);
        var funken = Gemerkt(Funkenvorrat, (f, p.SternglanzGroesse, p.SternglanzStrahlen, anzahl, p.RandglanzLage, p.GegenglanzLage), () => FunkenBauen(f, p, anzahl));
        var weiss = Colors.White;
        for (var i = 0; i < funken.Length; i++)
        {
            var (stern, punkt, m, kern, strahl) = funken[i];
            var deckung = p.SternglanzStaerke * st * (i == 0 ? 1 : 0.85);
            Hinzu(stern, Pinselwerk.Rund(m, strahl, strahl,
                (Pinselwerk.Alpha(weiss, deckung * 0.95), 0), (Pinselwerk.Alpha(weiss, deckung * 0.75), kern / strahl),
                (Pinselwerk.Alpha(weiss, deckung * 0.35), 0.5 + kern / strahl / 2), (Pinselwerk.Alpha(weiss, 0), 1)));
            Hinzu(punkt, Pinselwerk.Strahl(m, kern, kern, Pinselwerk.Alpha(weiss, deckung)));
        }
    }

    /// <summary>
    /// Die Funken einer Fläche. Jeder beginnt auf der Mitte der Haarlinie (auf einem Umriss auf die Kante gezogen). Seine
    /// Strahlen reichen höchstens um <see cref="Funkenueberstand"/> ihrer Länge über die Kante und nie über die Fläche
    /// hinaus; sie enden dann früher, spitz wie sonst. Hätten die beiden nach außen zeigenden Strahlen so kaum Platz (an
    /// einer scharfen Ecke gar keinen), rückt der Funke schräg nach innen, bis beide gut drei Fünftel ihrer Länge haben,
    /// höchstens um drei Fünftel der Strahllänge.
    /// </summary>
    private static Funke[] FunkenBauen(Glasflaeche f, Glanzprofil p, int anzahl)
    {
        double b = f.Breite, h = f.Hoehe, r = f.Radius, s = f.Skala;
        var rb = Math.Max(0.5, r - 0.5);
        var w1 = p.RandglanzLage * Math.PI / 180;
        var w2 = p.GegenglanzLage * Math.PI / 180;
        var w3 = (180 - p.RandglanzLage) * Math.PI / 180;
        var orte = new (double X, double Y, double Anteil)[]
        {
            (r + rb * Math.Cos(w1), r - rb * Math.Sin(w1), 1),
            (b - r + rb * Math.Cos(w2), h - r - rb * Math.Sin(w2), 0.75),
            (b - r + rb * Math.Cos(w3), r - rb * Math.Sin(w3), 0.6),
        };
        var funken = new Funke[anzahl];
        for (var i = 0; i < anzahl; i++)
        {
            var (ox, oy, anteil) = orte[i];
            var q = AufkantePx(f, ox, oy);
            var kern = Math.Max(0.3, p.SternglanzGroesse) * anteil * s;
            var strahl = Math.Max(p.SternglanzStrahlen * anteil * s, kern * 1.5);
            var ueber = strahl * Funkenueberstand;
            // Nach außen zeigen die Strahlen zur nächsten Ecke der Fläche.
            var (ax, ay) = (q.X < b / 2 ? -1 : 1, q.Y < h / 2 ? -1 : 1);
            double Raum(double x, double y, int dx, int dy)
            {
                var rand = dx < 0 ? x : dx > 0 ? b - x : dy < 0 ? y : h - y;
                return Math.Clamp(Math.Min(rand, Austritt(f, x, y, dx, dy, strahl) + ueber), 0, strahl);
            }
            var (cx, cy, bester) = (q.X, q.Y, -1.0);
            foreach (var t in new[] { 0, 0.2, 0.4, 0.6 })
            {
                var (x, y) = (q.X - ax * t * strahl, q.Y - ay * t * strahl);
                if (!Innen(f, x, y)) break;
                var aussen = Math.Min(Raum(x, y, ax, 0), Raum(x, y, 0, ay));
                if (aussen > bester) (cx, cy, bester) = (x, y, aussen);
                if (aussen >= 0.6 * strahl) break;
            }
            var d = kern * 0.6;
            Point P(double x, double y) => f.Dip(Math.Clamp(x, 0, b), Math.Clamp(y, 0, h));
            var stern = new StreamGeometry();
            using (var z = stern.Open())
            {
                z.BeginFigure(P(cx, cy - Math.Max(d, Raum(cx, cy, 0, -1))), true, true);
                z.LineTo(P(cx + d, cy - d), false, false);
                z.LineTo(P(cx + Math.Max(d, Raum(cx, cy, 1, 0)), cy), false, false);
                z.LineTo(P(cx + d, cy + d), false, false);
                z.LineTo(P(cx, cy + Math.Max(d, Raum(cx, cy, 0, 1))), false, false);
                z.LineTo(P(cx - d, cy + d), false, false);
                z.LineTo(P(cx - Math.Max(d, Raum(cx, cy, -1, 0)), cy), false, false);
                z.LineTo(P(cx - d, cy - d), false, false);
            }
            stern.Freeze();
            // Der Kern bleibt in der Fläche.
            var k = Math.Max(0.3, Math.Min(kern, Math.Min(Math.Min(cx, b - cx), Math.Min(cy, h - cy))));
            var m = f.Dip(cx, cy);
            var punkt = new EllipseGeometry(m, k / s, k / s);
            punkt.Freeze();
            funken[i] = new Funke(stern, punkt, m, k / s, strahl / s);
        }
        return funken;
    }

    /// <summary>
    /// Ein Punkt in Pixeln, der ein Stück <paramref name="weg"/> (Pixel) gegen den Uhrzeigersinn von der Lage
    /// <paramref name="lage"/> (Grad, auf dem Bogen oben links) entlang der Mitte der Haarlinie liegt: den Bogen hinunter zur
    /// linken Seite, die Gerade links und den Bogen unten links. Dazu die Drehung einer längs der Kante gestreckten Ellipse.
    /// Zur Seite hin, weil die Haarlinie dort schwach ist: Oben ginge ein zweites Glanzlicht im hellen Rand unter.
    /// </summary>
    internal static (double X, double Y, double Winkel) Kantenweg(Glasflaeche f, double lage, double weg)
    {
        double h = f.Hoehe, r = f.Radius;
        var rb = Math.Max(0.5, r - 0.5);
        var w = Math.Clamp(lage, 90, 180) * Math.PI / 180;
        var bogen = rb * (Math.PI - w);
        if (weg <= bogen)
        {
            var u = w + weg / rb;
            return (r + rb * Math.Cos(u), r - rb * Math.Sin(u), 90 - u * 180 / Math.PI);
        }
        weg -= bogen;
        var gerade = Math.Max(0, h - 2 * r);
        if (weg <= gerade) return (r - rb, r + weg, -90);
        var v = Math.PI + Math.Min(Math.PI / 2, (weg - gerade) / rb);
        return (r + rb * Math.Cos(v), h - r - rb * Math.Sin(v), 90 - v * 180 / Math.PI);
    }

    /// <summary>
    /// Ein Punkt in Pixeln der Fläche, auf die Kante gezogen. Mit <see cref="Glasflaeche.Umriss"/> liegt die gerechnete
    /// Ecke nicht immer auf der Form (eine Fase hat keinen Bogen); dann wird der Punkt vom Mittelpunkt aus auf die Kontur
    /// gezogen, einen halben Pixel nach innen, also auf die Mitte der Haarlinie. Gesucht wird durch Halbieren; die Aufrufer
    /// merken sich das Ergebnis je Fläche.
    /// </summary>
    private static Point AufkantePx(Glasflaeche f, double x, double y)
    {
        if (f.Umriss is null) return new Point(x, y);
        double mx = f.Breite / 2, my = f.Hoehe / 2;
        double dx = x - mx, dy = y - my;
        var laenge = Math.Sqrt(dx * dx + dy * dy);
        if (laenge < 1e-6 || !Innen(f, mx, my)) return new Point(x, y);
        (dx, dy) = (dx / laenge, dy / laenge);
        double innen = 0, aussen = Math.Sqrt(f.Breite * f.Breite + f.Hoehe * f.Hoehe);
        for (var i = 0; i < 20; i++)
        {
            var t = (innen + aussen) / 2;
            if (Innen(f, mx + dx * t, my + dy * t)) innen = t;
            else aussen = t;
        }
        var k = Math.Max(0, innen - 0.5);
        return new Point(mx + dx * k, my + dy * k);
    }

    /// <summary>Ob ein Punkt (Pixel der Fläche) in der Form samt Haarlinie liegt; ohne Umriss gerechnet, mit Umriss über die Kontur.</summary>
    private static bool Innen(Glasflaeche f, double x, double y)
    {
        if (f.Umriss is not null) return f.Form(0).FillContains(f.Dip(x, y));
        if (x < 0 || y < 0 || x > f.Breite || y > f.Hoehe) return false;
        var r = Math.Min(f.Radius, Math.Min(f.Breite, f.Hoehe) / 2);
        var (dx, dy) = (x - Math.Clamp(x, r, f.Breite - r), y - Math.Clamp(y, r, f.Hoehe - r));
        return dx * dx + dy * dy <= r * r;
    }

    /// <summary>Wie weit man von (x, y) (Pixel, in der Form) in Richtung (dx, dy) gehen kann, bis die Form endet, höchstens <paramref name="bis"/>.</summary>
    private static double Austritt(Glasflaeche f, double x, double y, int dx, int dy, double bis)
    {
        if (Innen(f, x + dx * bis, y + dy * bis)) return bis;
        double innen = 0, aussen = bis;
        for (var i = 0; i < 12; i++)
        {
            var t = (innen + aussen) / 2;
            if (Innen(f, x + dx * t, y + dy * t)) innen = t;
            else aussen = t;
        }
        return innen;
    }

    /// <summary>Der Teil von <paramref name="form"/> innerhalb der (um ihre Mitte gedrehten) Ellipse, einmal berechnet und eingefroren.</summary>
    private static PathGeometry Schnitt(Geometry form, Point mitte, double rx, double ry, double winkel = 0)
    {
        var ellipse = new EllipseGeometry(mitte, rx, ry);
        if (winkel != 0) ellipse.Transform = new RotateTransform(winkel, mitte.X, mitte.Y);
        var g = Geometry.Combine(form, ellipse, GeometryCombineMode.Intersect, null);
        g.Freeze();
        return g;
    }

    /// <summary>Der Teil von <paramref name="form"/> innerhalb eines Rechtecks (DIP), einmal berechnet und eingefroren.</summary>
    private static PathGeometry Rechteckschnitt(Geometry form, Rect rechteck)
    {
        var g = Geometry.Combine(form, new RectangleGeometry(rechteck), GeometryCombineMode.Intersect, null);
        g.Freeze();
        return g;
    }

    public Glasflaeche Flaeche { get; }
    public Glanzprofil Profil { get; }
    public Color Ton { get; }
    public double Staerke { get; }
    public Brush? Durchlicht { get; }

    /// <summary>Die Akzentfarbe für das Bodenleuchten (Glas.Akzent); ohne Angabe der Ton der Spiegelungen.</summary>
    public Color Akzent { get; }

    /// <summary>Der Ring der Haarlinie (ein Gerätepixel).</summary>
    public Geometry Haar { get; }

    /// <summary>Der Körper innerhalb der Haarlinie.</summary>
    public Geometry Koerper { get; }

    /// <summary>Die Geometrie des Fresnel-Rands, wenn er gezeichnet wird; je Fläche dieselbe, gleich in welchem Zustand.</summary>
    public Fresnelrand? Fresnel { get; private set; }

    /// <summary>
    /// Die zuletzt gebauten Bilder, geteilt von allen Elementen mit gleicher Fläche, gleichem Profil, Ton, gleicher Stärke,
    /// gleichem Durchlicht und Akzent (0.5.0-beta.5). Ein Glanzbild ist nach dem Bau unveränderlich (Formen und Pinsel
    /// eingefroren); gleiche Werte bauen genau dasselbe. Ändert sich eine Form, bauten bis beta.4 alle gleichen Knöpfe ihr
    /// Bild je für sich (16 Formknöpfe in Glint: je Schritt eines Zugs 16 statt rund 8 Bilder, je 0,5 bis 1 ms).
    /// Je Faden, weil WPF-Elemente an ihrem Faden hängen.
    /// </summary>
    [ThreadStatic]
    private static List<Glanzbild>? _geteilt;

    /// <summary>So viele Bilder bleiben geteilt (die ältesten fallen heraus).</summary>
    private const int GeteiltHoechstens = 32;

    /// <summary>Wie viele Glanzbilder neu gebaut wurden, je Faden (für die Tests in Glint, per Reflection gelesen).</summary>
    [ThreadStatic]
    private static int _gebaut;

    internal static int Gebaut => _gebaut;

    private bool Passt(Glasflaeche f, Glanzprofil p, Color ton, double staerke, Brush? durchlicht, Color akzent) =>
        Flaeche == f && Profil == p && Ton == ton && Staerke.Equals(staerke) && ReferenceEquals(Durchlicht, durchlicht) && Akzent == akzent;

    /// <summary>
    /// Das zuletzt gebaute Bild, wenn es passt (auch das Profil, verglichen als Wert), sonst ein passendes geteiltes, sonst
    /// ein neues.
    /// </summary>
    public static Glanzbild Holen(Glanzbild? alt, Glasflaeche f, Glanzprofil p, Color ton, double staerke, Brush? durchlicht, Color? akzent = null)
    {
        var a = akzent ?? ton;
        if (alt is not null && alt.Passt(f, p, ton, staerke, durchlicht, a)) return alt;
        var geteilt = _geteilt ??= [];
        for (var i = geteilt.Count - 1; i >= 0; i--)
            if (geteilt[i].Passt(f, p, ton, staerke, durchlicht, a)) return geteilt[i];
        var neu = new Glanzbild(f, p, ton, staerke, durchlicht, a);
        _gebaut++;
        if (geteilt.Count >= GeteiltHoechstens) geteilt.RemoveAt(0);
        geteilt.Add(neu);
        return neu;
    }

    /// <summary>Alle Spiegelungen, in der Reihenfolge von unten nach oben.</summary>
    public void Zeichnen(DrawingContext dc)
    {
        foreach (var (form, pinsel) in _schichten) dc.DrawGeometry(pinsel, null, form);
    }

    private void Hinzu(Geometry form, Brush pinsel) => _schichten.Add((form, pinsel));

    /// <summary>Die Formen aller Spiegelungen, von unten nach oben (für die Tests).</summary>
    internal IEnumerable<Geometry> Formen => _schichten.Select(s => s.Form);
}

/// <summary>Kleine Pinselhelfer: eingefroren, Deckung im Pinsel statt PushOpacity, Lage statt Ausschnitt.</summary>
internal static class Pinselwerk
{
    private static readonly ConditionalWeakTable<Brush, Dictionary<double, Brush>> GedaempftVorrat = new();
    private static readonly ConditionalWeakTable<Brush, Dictionary<double, Brush>> ObenVorrat = new();

    public static Color Alpha(Color c, double deckung) => Color.FromArgb((byte)Math.Round(Math.Clamp(deckung, 0, 1) * 255), c.R, c.G, c.B);

    /// <summary>Radialer Verlauf mit fester Lage, Größe (DIP) und Drehung (Grad, um die Mitte), von <paramref name="innen"/> zu durchsichtig.</summary>
    public static RadialGradientBrush Strahl(Point mitte, double rx, double ry, Color innen, double winkel = 0)
    {
        var b = new RadialGradientBrush(innen, Color.FromArgb(0, innen.R, innen.G, innen.B))
        {
            MappingMode = BrushMappingMode.Absolute, Center = mitte, GradientOrigin = mitte, RadiusX = rx, RadiusY = ry,
        };
        if (winkel != 0) b.Transform = new RotateTransform(winkel, mitte.X, mitte.Y);
        b.Freeze();
        return b;
    }

    /// <summary>Die Stopps eines radialen Pinsels der Farbstellung, an eine feste Lage gelegt.</summary>
    public static RadialGradientBrush Gelegt(RadialGradientBrush quelle, Point mitte, double rx, double ry, double deckung)
    {
        var b = new RadialGradientBrush(quelle.GradientStops.Clone())
        {
            MappingMode = BrushMappingMode.Absolute, Center = mitte, GradientOrigin = mitte, RadiusX = rx, RadiusY = ry,
            Opacity = quelle.Opacity * deckung,
        };
        b.Freeze();
        return b;
    }

    public static LinearGradientBrush Senkrecht(params (Color Farbe, double Stelle)[] stopps)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        foreach (var (f, o) in stopps) b.GradientStops.Add(new GradientStop(f, o));
        b.Freeze();
        return b;
    }

    /// <summary>Waagerechter Verlauf über die Breite der gefüllten Form, von links nach rechts.</summary>
    public static LinearGradientBrush Waagerecht(params (Color Farbe, double Stelle)[] stopps)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        foreach (var (f, o) in stopps) b.GradientStops.Add(new GradientStop(f, o));
        b.Freeze();
        return b;
    }

    /// <summary>Eine einfarbige, eingefrorene Fläche.</summary>
    public static SolidColorBrush Flaeche(Color farbe)
    {
        var b = new SolidColorBrush(farbe);
        b.Freeze();
        return b;
    }

    /// <summary>Radialer Verlauf mit fester Lage und Halbachsen (DIP) und beliebigen Stopps von der Mitte nach außen.</summary>
    public static RadialGradientBrush Rund(Point mitte, double rx, double ry, params (Color Farbe, double Stelle)[] stopps)
    {
        var b = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, Center = mitte, GradientOrigin = mitte, RadiusX = rx, RadiusY = ry,
        };
        foreach (var (f, o) in stopps) b.GradientStops.Add(new GradientStop(f, o));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Linearer Verlauf mit fester Lage (DIP) von <paramref name="anfang"/> bei <paramref name="von"/> zu
    /// <paramref name="ende"/> bei <paramref name="bis"/>; davor und dahinter die Endfarben.
    /// </summary>
    public static LinearGradientBrush Verlauf(Point von, Point bis, Color anfang, Color ende)
    {
        var b = new LinearGradientBrush(anfang, ende, von, bis) { MappingMode = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }

    /// <summary>Die Farbe eines Pinsels der Farbstellung (einfarbig, sonst der erste Stopp eines Verlaufs), sonst <paramref name="ersatz"/>.</summary>
    public static Color Ton(Brush? pinsel, Color ersatz) => pinsel switch
    {
        SolidColorBrush s => s.Color,
        GradientBrush { GradientStops.Count: > 0 } g => g.GradientStops[0].Color,
        _ => ersatz,
    };

    /// <summary>
    /// Eingefrorene Kopie mit geringerer Deckung, je Quellpinsel und Deckung einmal. Ersetzt PushOpacity: Das legte je
    /// Element eine Zwischenebene an und kostete im Software-Rendern (60 Schalter, 175 %) rund 200 ms statt wenigen.
    /// </summary>
    public static Brush Gedaempft(Brush quelle, double deckung) => Vorrat(GedaempftVorrat, quelle, deckung, q =>
    {
        var kopie = q.CloneCurrentValue();
        kopie.Opacity = q.Opacity * deckung;
        return kopie;
    });

    /// <summary>Ein senkrechter Verlauf der Farbstellung, auf den oberen <paramref name="anteil"/> der Fläche gestaucht.</summary>
    public static Brush Oben(Brush quelle, double anteil) => Vorrat(ObenVorrat, quelle, anteil, q =>
    {
        if (q.CloneCurrentValue() is not LinearGradientBrush l) return q.CloneCurrentValue();
        l.StartPoint = new Point(0.5, 0);
        l.EndPoint = new Point(0.5, anteil);
        return l;
    });

    private static Brush Vorrat(ConditionalWeakTable<Brush, Dictionary<double, Brush>> tabelle, Brush quelle, double wert, Func<Brush, Brush> machen)
    {
        var je = tabelle.GetValue(quelle, _ => []);
        lock (je)
        {
            if (je.TryGetValue(wert, out var fertig)) return fertig;
            var neu = machen(quelle);
            neu.Freeze();
            je[wert] = neu;
            return neu;
        }
    }
}
