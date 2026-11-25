// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Identitaet;

/// <summary>
/// Die Namensregeln des Servers für die Anmeldung (A42, 3.3) nach außen, damit Client und Setup dasselbe prüfen wie der
/// Server: SC-Name ("Mustermann") und Discord-Name ("Mustermann/Max"), je 2 bis 32 Zeichen. Leerzeichen vorn und hinten fallen
/// weg; <c>null</c> heißt ungültig.
/// </summary>
public static class Namensregeln
{
    /// <summary>Kürzester Name.</summary>
    public const int Min = Zeichenregeln.ScNameMin;

    /// <summary>Längster Name, in Unicode-Zeichen.</summary>
    public const int Max = Zeichenregeln.ScNameMax;

    /// <summary>SC-Name: Buchstaben (auch Umlaute), Ziffern, Leerzeichen, <c>_</c>, <c>-</c> und <c>.</c>.</summary>
    public static string? ScName(string? text) => Zeichenregeln.ScNameBereinigen(text);

    /// <summary>Discord-Name: freier, auch <c>/</c>, aber ohne unsichtbare oder täuschende Zeichen.</summary>
    public static string? DiscordName(string? text) => Zeichenregeln.DiscordNameBereinigen(text);

    /// <summary>Schlüssel für den Vergleich zweier Namen (NFKC, Großschreibung), wie der Server Namen eindeutig hält.</summary>
    public static string Vergleichsschluessel(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Zeichenregeln.Namensschluessel(name);
    }
}
