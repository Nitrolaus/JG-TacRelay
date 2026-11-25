// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;

namespace Funk.Klang.Eingabe;

/// <summary>Woher eine Sprechtaste kommt.</summary>
public enum EingabeArt
{
    Tastatur,
    Maus,

    /// <summary>Joystick, Pedale, Throttle und andere Spielgeräte über DirectInput (<see cref="DirectInputSprechtaste"/>).</summary>
    Joystick,
}

/// <summary>
/// Eine frei belegte Sprechtaste. <see cref="Code"/> ist bei der Tastatur der Scancode mit Präfix (0xE0nn bzw. 0xE1nn),
/// unabhängig vom Tastaturlayout und mit Unterscheidung links/rechts (rechte Strg ist 0xE01D, linke 0x001D). Bei der
/// Maus die Knopfnummer 1 bis 5 (links, rechts, Mitte, X1, X2). Beim Joystick der Knopf ab 1 oder eine Hutrichtung
/// (<see cref="Joystickeingabe"/>) und in <see cref="Geraet"/> die Gerätekennung mit Pfad, VID/PID und Produktname
/// (<see cref="Geraetekennung"/>), damit baugleiche Sticks unterscheidbar bleiben.
/// </summary>
public readonly record struct Tastenbelegung(EingabeArt Art, int Code, string? Geraet = null)
{
    public const int MausLinks = 1;
    public const int MausRechts = 2;
    public const int MausMitte = 3;
    public const int MausX1 = 4;
    public const int MausX2 = 5;

    /// <summary>Numpad + (Scancode 0x4E, ohne Präfix): bei Sarevoc Sendenetz vor (A7).</summary>
    public const int NumpadPlus = 0x004E;

    /// <summary>Numpad - (Scancode 0x4A): bei Sarevoc Sendenetz zurück (A7).</summary>
    public const int NumpadMinus = 0x004A;

    public static Tastenbelegung Taste(int scancodeMitPraefix) => new(EingabeArt.Tastatur, scancodeMitPraefix);

    /// <summary>Gleiche Taste: Art, Code und (beim Joystick) Gerät, ohne Unterschied der Groß- und Kleinschreibung im Pfad.</summary>
    public bool GleicheTaste(Tastenbelegung andere) =>
        Art == andere.Art && Code == andere.Code && string.Equals(Geraet ?? "", andere.Geraet ?? "", StringComparison.OrdinalIgnoreCase);

    public static Tastenbelegung Maus(int knopf) => new(EingabeArt.Maus, knopf);

    /// <summary>Kurze, sprachneutrale Beschreibung für Protokolle; die Oberfläche benennt Tasten selbst.</summary>
    public override string ToString() => Art switch
    {
        EingabeArt.Tastatur => string.Create(CultureInfo.InvariantCulture, $"Taste 0x{Code:X4}"),
        EingabeArt.Maus => string.Create(CultureInfo.InvariantCulture, $"Maus {Code}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"Joystick {Code} ({Geraet})"),
    };
}

/// <summary>
/// Quelle einer Sprechtaste. Ereignisse kommen aus dem Thread der Quelle, nie aus dem Audio-Thread, und sollen kurz
/// bleiben (etwa <see cref="Maschine.IKlangMaschine.SprechtasteGedrueckt()"/> aufrufen). Eine Quelle meldet nur Wechsel:
/// Tastenwiederholung beim Halten erzeugt kein zweites <see cref="Gedrueckt"/>.
/// </summary>
public interface ISprechtasteQuelle : IDisposable
{
    /// <summary>Die überwachte Taste, oder null (keine). Eine Belegung, die die Quelle nicht kann, wird abgelehnt.</summary>
    Tastenbelegung? Belegung { get; set; }

    bool IstGedrueckt { get; }

    event Action? Gedrueckt;

    event Action? Losgelassen;

    bool Unterstuetzt(EingabeArt art);

    /// <summary>Belegen: liefert die nächste gedrückte Taste bzw. den nächsten Knopf, den diese Quelle sieht.</summary>
    Task<Tastenbelegung> NaechsteTasteAsync(CancellationToken ct);

    /// <summary>
    /// Belegen wie <see cref="NaechsteTasteAsync(CancellationToken)"/>; mit <paramref name="vorsichtig"/> (seit 0.10.0, der Schritt Sprechtaste beim
    /// ersten Start) nimmt eine Tastaturquelle keine Taste, mit der man das eigene Fenster bedient, und Strg, Alt, Umschalt und Windows nur allein
    /// (<see cref="SprechtastenZustand.Vorsichtig"/>). Eine Quelle ohne Tastatur (Joystick) wartet wie sonst.
    /// </summary>
    Task<Tastenbelegung> NaechsteTasteAsync(bool vorsichtig, CancellationToken ct) => NaechsteTasteAsync(ct);
}

/// <summary>
/// Was eine globale Taste auslöst: Sprechen (solange gehalten), das Sendenetz weiterschalten (A5, A7) und seit 0.5.0 (Ausbaustufe
/// S2, RAID-S2-ENTWURF Abschnitt 6) Melden (solange gehalten), das Funkfeld ablegen oder holen und die Phasen der Einsatzleitung; seit
/// Klangstufe C2b "Dauersender stumm" und "Mikro stumm" (beide als Flanke).
/// <para>
/// Der Name steht so in der Einstellungsdatei. Die ersten drei stehen seit 0.1.0 in <c>tasten.eintraege</c>; ein Client 0.4.0 legt
/// die ganze Datei beiseite, wenn er dort einen Namen nicht kennt. Die Funktionen ab 0.5.0 stehen deshalb in einer eigenen Liste
/// (<see cref="Tastenfunktionen.IstAlt"/>), die ein älterer Client als unbekannte Eigenschaft übergeht.
/// </para>
/// </summary>
public enum Tastenfunktion
{
    Sprechen,

    /// <summary>Sendenetz vor, wie + im Funkfeld (bei Sarevoc Numpad +).</summary>
    NetzVor,

    /// <summary>Sendenetz zurück, wie - im Funkfeld (bei Sarevoc Numpad -).</summary>
    NetzZurueck,

    /// <summary>Funkfeld in den Infobereich ablegen oder zurückholen (ein Druck schaltet um).</summary>
    FunkfeldUmschalten,

    /// <summary>Melde-Taste: Solange gehalten, spricht man ins Meldenetz (etwa Kommando), ohne die Sendeauswahl zu ändern.</summary>
    Melden,

    /// <summary>Einsatzleitung: die nächste Phase scharf machen, ein zweiter Druck schaltet.</summary>
    PhaseVor,

    /// <summary>Einsatzleitung: die vorige Phase scharf machen, ein zweiter Druck schaltet.</summary>
    PhaseZurueck,

    /// <summary>Klangstufe C2b (Q8): der zuletzt gedämpfte Dauersender stumm, ein zweiter Druck macht ihn wieder normal.</summary>
    DauersenderStumm,

    /// <summary>Klangstufe C2b (Q32): Mikro stumm an oder aus (nichts geht hinaus, auch nicht mit der Sprechtaste).</summary>
    MikroStumm,

    /// <summary>Meldesystem (0.12.0): "Bereit" auf die offene Bereit-Abfrage der Leitung; ohne offene Abfrage nichts.</summary>
    Bereit,
}

/// <summary>Eigenschaften der <see cref="Tastenfunktion"/>en.</summary>
public static class Tastenfunktionen
{
    /// <summary>Gehalten (gedrückt und losgelassen zählen): Sprechen und Melden. Alle übrigen melden nur die Flanke nach unten.</summary>
    public static bool IstGehalten(Tastenfunktion f) => f is Tastenfunktion.Sprechen or Tastenfunktion.Melden;

    /// <summary>Seit 0.1.0 bekannt (Sprechen, Sendenetz vor und zurück): Nur diese gehören in die Liste, die ein Client 0.4.0 liest.</summary>
    public static bool IstAlt(Tastenfunktion f) => f is Tastenfunktion.Sprechen or Tastenfunktion.NetzVor or Tastenfunktion.NetzZurueck;
}

/// <summary>Eine Taste mit ihrer Funktion. Eine Funktion darf mehrere Tasten haben (etwa Strg-Rechts und Maus 4 zum Sprechen).</summary>
public readonly record struct Tastenzuordnung(Tastenfunktion Funktion, Tastenbelegung Taste);

/// <summary>
/// Quelle, die mehrere Belegungen und alle Funktionen kann: Raw Input und seit 0.5.0 auch DirectInput. Eine einfache
/// <see cref="ISprechtasteQuelle"/> bekommt im <see cref="SprechtastenVerbund"/> nur ihre erste Sprechtaste.
/// </summary>
public interface ITastenQuelle : ISprechtasteQuelle
{
    /// <summary>
    /// Alle Zuordnungen dieser Quelle; ersetzt die bisherigen. Gehaltene Tasten gelten danach als losgelassen. Eine
    /// einzelne Quelle lehnt Arten ab, die sie nicht kann (<see cref="NotSupportedException"/>); der
    /// <see cref="SprechtastenVerbund"/> nimmt alles und führt Unbedientes unter <see cref="SprechtastenVerbund.NichtBedient"/>.
    /// </summary>
    IReadOnlyList<Tastenzuordnung> Zuordnungen { get; set; }

    /// <summary>
    /// Eine Funktion mit Flanke (alle außer Sprechen und Melden, <see cref="Tastenfunktionen.IstGehalten"/>) ging hinunter: nur die
    /// Flanke, keine Tastenwiederholung; aus dem Thread der Quelle.
    /// </summary>
    event Action<Tastenfunktion>? Ausgeloest;

    /// <summary>
    /// Eine gehaltene Funktion außer Sprechen (Melden) ging hinunter (<c>true</c>) oder hinauf (<c>false</c>); aus dem Thread der
    /// Quelle. Wie bei der Sprechtaste: Neue Zuordnungen und das Freigeben melden ein Loslassen.
    /// </summary>
    event Action<Tastenfunktion, bool>? Gehalten;
}
