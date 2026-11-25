// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Die Namensregeln der Anmeldung (A42, 3.3), die Client, Setup und Server teilen. Der Discord-Name steht seit Antwort 3 für
/// alle sichtbar in "Wer ist da"; darum darf er nichts enthalten, was einen anderen Namen vortäuscht: keine unsichtbaren oder
/// richtungsändernden Zeichen, keine anderen Leerzeichen als das gewöhnliche, keine doppelten Leerzeichen, nichts aus dem
/// privaten Bereich. Der eindeutige SC-Name steht zur Sicherheit daneben.
/// </summary>
internal static class NamensregelnTests
{
    [Test]
    public static void Namensregeln_DiscordNameOhneTaeuschendeZeichen()
    {
        Pruefe.Gleich("Sarevoc/Otto", Namensregeln.DiscordName(" Sarevoc/Otto "), "Schrägstrich trennt nichts, der Rand fällt weg");
        Pruefe.Gleich("Brandy \"Lea\"", Namensregeln.DiscordName("Brandy \"Lea\""), "Anführungszeichen gehen");
        Pruefe.Gleich("Otto \U0001F642", Namensregeln.DiscordName("Otto \U0001F642"), "ein Emoji ist kein Trick");
        Pruefe.Gleich("Jörg Müller", Namensregeln.DiscordName("Jörg Müller"), "Umlaute");
        foreach (var (text, was) in new (string, string)[]
                 {
                     ("Sare\u200Bvoc/Otto", "Leerzeichen ohne Breite"),
                     ("Sarevoc\u202E/Otto", "Richtungsumkehr"),
                     ("Sarevoc\u2066/Otto", "Richtungsisolation"),
                     ("Sarevoc\u2060", "Wortverbinder"),
                     ("Sare\uFEFFvoc", "Byte-Reihenfolge-Marke"),
                     ("Sare\u00ADvoc", "weiches Trennzeichen"),
                     ("Sarevoc\u00A0Otto", "geschütztes Leerzeichen"),
                     ("Sarevoc\u3000Otto", "breites Leerzeichen"),
                     ("Sarevoc\u2009Otto", "schmales Leerzeichen"),
                     ("Sarevoc  Otto", "doppeltes Leerzeichen"),
                     ("Sarevoc\uE000", "privater Bereich"),
                     ("Sarevoc\u2028Otto", "Zeilentrenner"),
                     ("Sarevoc\nOtto", "Zeilenumbruch"),
                     ("Sarevoc\tOtto", "Tabulator"),
                     ("S", "zu kurz"),
                     (new string('a', 33), "zu lang"),
                     ("", "leer"),
                 })
        {
            Pruefe.Gleich(null, Namensregeln.DiscordName(text), was);
        }

        // SC-Name: nur Buchstaben, Ziffern, Leerzeichen, _ - und Punkt; Vergleich ohne Rücksicht auf Groß und klein.
        Pruefe.Gleich("Sarevoc_2.0", Namensregeln.ScName(" Sarevoc_2.0 "), "SC-Name");
        Pruefe.Gleich(null, Namensregeln.ScName("Sarevoc/Otto"), "Schrägstrich nur im Discord-Namen");
        Pruefe.Gleich(null, Namensregeln.ScName("Sare\u200Bvoc"), "unsichtbar im SC-Namen");
        Pruefe.Gleich(Namensregeln.Vergleichsschluessel("sarevoc"), Namensregeln.Vergleichsschluessel("SAREVOC"), "gleich ohne Groß und klein");
        Pruefe.Wahr(Namensregeln.Min == 2 && Namensregeln.Max == 32, "2 bis 32 Zeichen, wie das Setup prüft");
    }

    /// <summary>
    /// Gegenprüfung des Nachtbaus (A53): Unsichtbare Zeichen gibt es nicht nur als Formatzeichen (Cf). Der Verbinder U+034F, die
    /// Variantenwähler und die mongolischen Freiformwähler sind Kombinationszeichen (Mn), die Hangul-Füller sind sogar Buchstaben
    /// (Lo) und damit auch im SC-Namen erlaubt. Alle stehen in Unicode als Default_Ignorable_Code_Point. Mit einem davon sähe
    /// "Fritz" aus wie "Fritz", hätte aber einen anderen Namensschlüssel: ein zweiter "Fritz" in "Wer ist da", an der Eindeutigkeit
    /// vorbei. Das leere Braillemuster U+2800 zeigt sich als Leerraum und zählt wie ein anderes Leerzeichen. Die Zeichen stehen
    /// hier als Zahlen, nicht als Escape im Quelltext (CODEMAP, Falle mit dem Edit-Werkzeug).
    /// </summary>
    [Test]
    public static void Namensregeln_UnsichtbareZeichenAuchAusserhalbDerFormatzeichen()
    {
        int[] unsichtbar = [0x034F, 0x115F, 0x1160, 0x17B4, 0x17B5, 0x180B, 0x180C, 0x180D, 0x180F, 0x2800, 0x3164, 0xFE00, 0xFE0E, 0xFE0F, 0xFFA0, 0xE0100, 0xE01EF];
        foreach (var c in unsichtbar)
        {
            var z = char.ConvertFromUtf32(c);
            var was = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"U+{c:X4}");
            Pruefe.Gleich(null, Namensregeln.DiscordName("Fritz/DC" + z), was + " am Ende des Discord-Namens");
            Pruefe.Gleich(null, Namensregeln.DiscordName(z + "Fritz/DC"), was + " am Anfang des Discord-Namens");
            Pruefe.Gleich(null, Namensregeln.DiscordName("Fri" + z + "tz"), was + " mitten im Discord-Namen");
            Pruefe.Gleich(null, Namensregeln.ScName("Fritz" + z), was + " im SC-Namen");
        }
        // Sichtbares bleibt erlaubt: ein Symbol ohne Variantenwähler, Umlaute, das koreanische Silbenzeichen.
        Pruefe.Gleich("Otto " + char.ConvertFromUtf32(0x2764), Namensregeln.DiscordName("Otto " + char.ConvertFromUtf32(0x2764)), "Herz ohne Variantenwähler");
        Pruefe.Gleich("Jörg", Namensregeln.ScName("Jörg"), "Umlaut im SC-Namen");
        Pruefe.Gleich(char.ConvertFromUtf32(0xD55C) + "Otto", Namensregeln.DiscordName(char.ConvertFromUtf32(0xD55C) + "Otto"), "Hangul-Silbe");
    }

    /// <summary>
    /// Gegenprüfung des Nachtbaus: Der Vergleich "ohne Groß und klein" hatte genau eine Lücke. <c>ToUpperInvariant</c> lässt
    /// das kleine ß stehen, das große ẞ (U+1E9E) auch; "groß" und "GROẞ" bekamen verschiedene Schlüssel. Gemessen über alle
    /// Zeichen bis U+1FFFF: Sonst stimmt der Schlüssel eines Zeichens mit dem seiner Kleinschreibung überein.
    /// </summary>
    [Test]
    public static void Namensregeln_GrossesEszettGleichKleinem()
    {
        var gross = char.ConvertFromUtf32(0x1E9E);
        var klein = char.ConvertFromUtf32(0x00DF);
        Pruefe.Gleich(Namensregeln.Vergleichsschluessel("gro" + klein + "/dc"), Namensregeln.Vergleichsschluessel("GRO" + gross + "/DC"), "Discord-Name mit ẞ und ß");
        Pruefe.Gleich(Namensregeln.Vergleichsschluessel("Ma" + klein), Namensregeln.Vergleichsschluessel("MA" + gross), "SC-Name mit ẞ und ß");
        Pruefe.Gleich(Namensregeln.Vergleichsschluessel("Strasse"), Namensregeln.Vergleichsschluessel("STRASSE"), "ss bleibt ss");
    }
}
