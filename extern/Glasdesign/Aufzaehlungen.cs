// Glasdesign - Maintainer: Sarevoc
namespace Glasdesign;

/// <summary>Umriss eines Glaskörpers. Die Rundung folgt immer der Höhe.</summary>
public enum Glasform
{
    /// <summary>Pille: Radius = halbe Höhe. Bei gleicher Breite und Höhe ein Kreis (Glas.Knopf.Rund).</summary>
    Pille,

    /// <summary>Abgerundete Ecken: Radius = <see cref="Glasgeometrie.Rundungsanteil"/> mal Höhe.</summary>
    Eckig,
}

/// <summary>Machart eines <see cref="Farbglaskoerper"/>.</summary>
public enum Farbglasart
{
    /// <summary>Gel: dunklerer Farbrand, Glanzband oben, Licht von unten, Funkeln, Spiegelung darunter.</summary>
    Gel,

    /// <summary>Kissen: satt und weich gewölbt, Glanzstrich oben rechts, weicher Schatten.</summary>
    Kissen,

    /// <summary>Metall (Flüssigmetall, Stilfamilie 3): Chrom mit harter Horizontkante unter der Schrift, kühler Glanz.</summary>
    Metall,

    /// <summary>Clay (Stilfamilie 6): satte Pastellfarbe, weich gewölbt, Licht oben links, Schatten in der eigenen Farbe.</summary>
    Clay,

    /// <summary>Neo-Brutal (Stilfamilie 7): flache Farbe, dicker Rand, harter Schatten ohne Unschärfe; gedrückt rutscht er hinein.</summary>
    Brutal,
}

/// <summary>Welcher Körper: bestimmt, welche Pinsel der Farbstellung ein <see cref="Glaskoerper"/> nimmt.</summary>
public enum Glasart
{
    /// <summary>Anthrazit-Glas (Glas.Koerper*).</summary>
    Normal,

    /// <summary>Hauptaktion im Akzent (Glas.Haupt*): Türkis in Standard, Weinrot in der Farbstellung Weinrot.</summary>
    Haupt,

    /// <summary>Auswahl, etwa ein gewählter Reiter (Glas.Auswahl*): Blau in Standard.</summary>
    Auswahl,

    /// <summary>Dunkler Körper für "aus" bei Haken und Schalter (Glas.Dunkel*).</summary>
    Dunkel,
}

/// <summary>Zustand, den ein Glaskörper zeichnet. Die Vorlagen setzen ihn über Trigger.</summary>
public enum Glaszustand
{
    Normal,
    Ueberfahren,
    Gedrueckt,
    Gesperrt,
}

/// <summary>Wie viel Schatten gezeichnet wird (angehängte Eigenschaft <see cref="Glas.SchattenProperty"/>).</summary>
public enum Schattenstufe
{
    /// <summary>Umgebungsschatten (DropShadowEffect) und Kontaktschatten, wie die Vorlage.</summary>
    Voll,

    /// <summary>Nur der Kontaktschatten (eine Ellipse, kein Effekt): deutlich billiger.</summary>
    Einfach,

    /// <summary>Kein Schatten.</summary>
    Aus,
}

/// <summary>
/// Erzwingt einen Zustand für Musterblatt und Tests (<see cref="Glas.ZustandVorschauProperty"/>). Kein Nutzerfeature:
/// Im Programm kommen die Zustände von Maus und Tastatur.
/// </summary>
public enum Zustandvorschau
{
    Keine,
    Ueberfahren,
    Gedrueckt,
    Fokus,

    /// <summary>Es läuft etwas: die Lichtkante, ohne dass der Host <see cref="Glas.LaeuftProperty"/> setzt.</summary>
    Laeuft,
}

/// <summary>Zeichen, die <see cref="Zeichner"/> pixelgenau mittig zeichnet.</summary>
public enum Glaszeichen
{
    Keins,
    Haken,
    Kreuz,
    Strich,
    Plus,
    Minus,
    PfeilHoch,
    PfeilRunter,
    PfeilLinks,
    PfeilRechts,

    /// <summary>Ein Quadrat als Umriss (Fensterknopf Maximieren).</summary>
    Maximieren,

    /// <summary>Zwei versetzte Quadrate, das hintere nur mit Ober- und rechter Kante (Fensterknopf Wiederherstellen).</summary>
    Wiederherstellen,

    /// <summary>Lupe: Kreis oben links, Griff nach unten rechts (Suchfelder, seit 0.5.0-beta.6).</summary>
    Lupe,

    /// <summary>Winkel nach unten ohne Schaft (Auswahllisten, seit 0.5.0-beta.6).</summary>
    Winkel,

    // Seit 0.5.0-beta.8 (Stilfamilien 2, Stufe 2): die Zeichen aller neuen Elementarten (Zeichensatz.cs).

    /// <summary>Winkel nach rechts ohne Schaft (Brotkrumen, Listeneintrag, Seitenwahl weiter).</summary>
    WinkelRechts,

    /// <summary>Winkel nach links ohne Schaft (Seitenwahl zurück).</summary>
    WinkelLinks,

    /// <summary>Winkel nach oben ohne Schaft.</summary>
    WinkelHoch,

    /// <summary>Zahnrad mit acht Zähnen und Loch (Einstellungen, Schrittfolge).</summary>
    Zahnrad,

    /// <summary>Person: Kopf und Schultern.</summary>
    Person,

    /// <summary>Bild: Rahmen, Berg und Sonne (Bildplatzhalter).</summary>
    Bild,

    /// <summary>Raute (Dialog im Glutglas).</summary>
    Raute,

    /// <summary>Warndreieck mit Ausrufezeichen (Hinweisbanner Warnung).</summary>
    Warnung,

    /// <summary>Kreis mit i (Hinweisbanner Hinweis).</summary>
    Info,

    /// <summary>Getrennte Kette: zwei Glieder, dazwischen der Bruch (Leerzustand).</summary>
    Kette,

    /// <summary>Uhr: Kreis mit zwei Zeigern.</summary>
    Uhr,

    /// <summary>Liste: drei Punkte mit Zeilen.</summary>
    Liste,

    /// <summary>Korb mit Henkel.</summary>
    Korb,

    /// <summary>Dokument mit umgeknickter Ecke und zwei Zeilen.</summary>
    Dokument,

    /// <summary>Etikett mit Loch, schräg nach oben links.</summary>
    Etikett,

    /// <summary>Zwei Winkel nach rechts.</summary>
    Doppelwinkel,

    /// <summary>Sortieren: kleiner Winkel hoch und kleiner Winkel runter übereinander.</summary>
    Sortieren,

    /// <summary>Filter: ein Trichter.</summary>
    Filter,
}

/// <summary>Die Familien, die den gemeinsamen <see cref="Chromrand"/> tragen; jede mit eigenen Werten im <see cref="Stilprofil"/>.</summary>
public enum Chromfamilie
{
    /// <summary>Breiter Ring um Kuppel und Kugel (Stilfamilie Glanzkugel).</summary>
    Glanzkugel,

    /// <summary>Dünner, steiler Rand um farbiges Glas (Stilfamilie Horizontglas).</summary>
    Horizontglas,

    /// <summary>Breiter, flacher Rahmen mit Lippe unten (Stilfamilie Chromrahmen).</summary>
    Chromrahmen,
}

/// <summary>Was die Glanzkugel im Chromrand trägt (<see cref="Kugelkoerper"/>).</summary>
public enum Kugelart
{
    /// <summary>Dunkle Glaskuppel mit Kuppelglanz, Fensterbögen und Leuchtkern.</summary>
    Kuppel,

    /// <summary>Satte Kugel in der Farbe des Knopfs, mit Kuppelglanz und Fensterreflex.</summary>
    Kugel,

    /// <summary>Dunkle Fläche mit Wellenglanz, ohne Ring (abgerundetes Quadrat oder beliebiger Umriss).</summary>
    Welle,

    /// <summary>Farbige Fläche mit Glanz oben und Glut in der Mitte, im Chromrahmen.</summary>
    Rahmen,

    /// <summary>Dunkle Bahn eines Schiebers: oben grau, an einer harten Kante unten schwarz.</summary>
    Bahn,
}

/// <summary>Form der Grenze des Himmelsglanzes im <see cref="Horizontglaskoerper"/>.</summary>
public enum Horizontglanz
{
    Gerade,
    Ellipse,
    Welle,
    Bogen,
    Schraeg,
}

/// <summary>Füllung eines Knopfs im Chromrahmen (<see cref="Chromrahmenfuellung"/>).</summary>
public enum Fuellung
{
    Hell,
    Silber,
    Schwarz,
    RauchHell,
    Rauch,
    RauchDunkel,
}

/// <summary>Ebene einer Fläche der Stilfamilie Taktil (<see cref="Taktilkoerper"/>).</summary>
public enum Taktilebene
{
    /// <summary>Gehoben: oben heller mit Lichtkante, weicher Schatten nach unten.</summary>
    Gehoben,

    /// <summary>Karte: eine Stufe höher als gehoben, Schatten weiter.</summary>
    Karte,

    /// <summary>Eingelassen: dunkler als der Grund, oben innen Schatten, unten eine feine Lichtkante.</summary>
    Eingelassen,

    /// <summary>Hauptaktion: Kupfer in einer Bronzefassung.</summary>
    Haupt,

    /// <summary>Kupfer ohne Fassung (gewählter Reiter, Haken an, Füllung des Fortschritts).</summary>
    Kupfer,

    /// <summary>Kupfer eingelassen (Spur des Schalters, an).</summary>
    KupferEingelassen,

    /// <summary>Gebürstetes Metall mit konzentrischen Ringen (Knauf).</summary>
    Metall,
}

/// <summary>Was eine <see cref="Glutscheibe"/> zeichnet: die Scheibe, die Spur eines Schalters oder seinen Knauf.</summary>
public enum Glutteil
{
    /// <summary>Rauchglasscheibe mit Schrägglanz und Haarlinie.</summary>
    Scheibe,

    /// <summary>Spur eines Schalters: an satt in der Glutfarbe mit Schein, aus grau.</summary>
    Spur,

    /// <summary>Knauf eines Schalters: eine helle Glaskugel, aus etwas dunkler.</summary>
    Knauf,
}

/// <summary>Kern einer Dickglasplatte (<see cref="Dickglaskoerper"/>).</summary>
public enum Dickkern
{
    /// <summary>Fast schwarz.</summary>
    Dunkel,

    /// <summary>Getönt in den Farben des Lichts.</summary>
    Getoent,

    /// <summary>Klar wie die Unterlage, nur leicht getönt.</summary>
    Klar,
}

/// <summary>Machart des Fortschritts (Stil Glas.Fortschritt, <see cref="Glas.FortschrittartProperty"/>).</summary>
public enum Fortschrittart
{
    /// <summary>Vorgabe: eingelassene Rinne mit Haarlinie, die Füllung ein Glaskörper im Akzent mit gleichem Abstand rundum.</summary>
    Glasfuellung,

    /// <summary>Schmale dunkle Spur, darauf eine leuchtende Linie mit weichem Schein und hellem Kopf.</summary>
    Lichtlinie,

    /// <summary>Reihe kleiner Glaskörper gleicher Breite mit gleichen Fugen; das angefangene Segment leuchtet anteilig.</summary>
    Segmente,
}

/// <summary>Machart des Fensterrahmens (Stile Glas.Fenster und Glas.Fensterrahmen, <see cref="Glas.RahmenartProperty"/>).</summary>
public enum Rahmenart
{
    /// <summary>Vorgabe: die Titelleiste ist ein Glaskörper (gewölbter Verlauf, Kantenspiegel, Schein), aktiv wie inaktiv flacher.</summary>
    Glasleiste,

    /// <summary>Titelleiste flach im Grund, nur durch eine Haarlinie vom Inhalt getrennt; die Knöpfe bleiben aus Glas.</summary>
    Schlicht,
}
