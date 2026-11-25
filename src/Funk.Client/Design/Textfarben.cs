// JG TacRelay - Maintainer: Sarevoc
using System.Windows.Media;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Design;

/// <summary>Eine Farbe der Textmeldung: Kennung auf dem Draht (<see cref="Textmeldungen"/>), Name für Menschen und der Farbton.</summary>
public sealed class Textfarbe
{
    internal Textfarbe(string kennung, string name, Color farbe)
    {
        Kennung = kennung;
        Name = name;
        Farbe = farbe;
        var pinsel = new SolidColorBrush(farbe);
        pinsel.Freeze();
        Pinsel = pinsel;
    }

    /// <summary>"orange" (<see cref="Textmeldungen.Orange"/>).</summary>
    public string Kennung { get; }

    /// <summary>"Orange".</summary>
    public string Name { get; }

    public Color Farbe { get; }

    /// <summary>Eingefroren, für die Schrift über dem Spiel und die Farbknöpfe der Leitung.</summary>
    public SolidColorBrush Pinsel { get; }
}

/// <summary>
/// Die festen Farben der Textmeldung (M24, Sarevoc 16.10.2026 abends): Weiß, Gelb, Orange, Rot, Grün, Blau. Die Töne sind hell gewählt, weil die Schrift
/// ohne Fläche über dem Spiel steht, nur mit dem schwarzen Schatten der Sprecheranzeige: Ein reines Blau (#0000FF) oder Rot (#FF0000) wäre vor dem
/// dunklen All kaum zu lesen. Jede Farbe hat gegen Schwarz (den Schatten) mindestens 7:1. Eine unbekannte Kennung (neuere Gegenstelle) zeigt Weiß.
/// </summary>
public static class Textfarben
{
    /// <summary>In der Folge von <see cref="Textmeldungen.Farben"/>, Weiß zuerst (ab Werk).</summary>
    public static IReadOnlyList<Textfarbe> Alle { get; } =
    [
        new(Textmeldungen.Weiss, "Weiß", Colors.White),
        new(Textmeldungen.Gelb, "Gelb", Color.FromRgb(0xFF, 0xE1, 0x4D)),
        new(Textmeldungen.Orange, "Orange", Color.FromRgb(0xFF, 0xA2, 0x3A)),
        new(Textmeldungen.Rot, "Rot", Color.FromRgb(0xFF, 0x63, 0x59)),
        new(Textmeldungen.Gruen, "Grün", Color.FromRgb(0x5C, 0xE6, 0x7A)),
        new(Textmeldungen.Blau, "Blau", Color.FromRgb(0x5E, 0xB4, 0xFF)),
    ];

    /// <summary>Die Farbe zur Kennung, sonst Weiß.</summary>
    public static Textfarbe Von(string? kennung) => Alle.FirstOrDefault(f => string.Equals(f.Kennung, kennung, StringComparison.Ordinal)) ?? Alle[0];
}
