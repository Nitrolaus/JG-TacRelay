// Glasdesign - Maintainer: Sarevoc
using System.Reflection;

namespace Glasdesign;

/// <summary>
/// Der Bauplan: die Maße der Elemente, die bis 0.3.1 als Konstanten im Code standen (Glas 4, Grundlage für Glint).
/// Unveränderlich, als Ressource <see cref="Schluessel.Bauplan"/> in jeder Farbstellung, per SetResourceReference in
/// Schalterbahn, Leuchtschalter, Glaskoerper und Kontaktschatten; ein Wechsel wirkt sofort. Fehlt der Schlüssel, gilt
/// <see cref="Standard"/>. Die Vorgaben sind genau die bisherigen Werte. Sarevocs Pixelregeln bleiben in der Geometrie: Die
/// Werte bestimmen Maße, gerechnet wird weiter auf ganzen Gerätepixeln (gleicher Abstand im Schalter, Zeichen mittig).
/// Maße in DIP, Anteile von 0 bis 1 (bezogen auf Höhe, Breite oder kürzere Seite).
/// </summary>
public sealed record Bauplan
{
    /// <summary>Der Bauplan mit den Vorgaben, wenn eine Farbstellung keinen mitbringt.</summary>
    public static Bauplan Standard { get; } = new();

    /// <summary>Höhe der Schalterspur ohne gesetzte Größe (DIP).</summary>
    public double SchalterHoehe { get; init; } = Schaltergeometrie.Hoehe;

    /// <summary>Breite der Schalterspur als Vielfaches ihrer Höhe.</summary>
    public double SchalterSeitenverhaeltnis { get; init; } = Schaltergeometrie.Seitenverhaeltnis;

    /// <summary>Luft zwischen Haarlinie der Spur und Knauf (DIP), oben, unten und am Ende gleich.</summary>
    public double SchalterAbstand { get; init; } = Schaltergeometrie.Abstand;

    /// <summary>Wie viel breiter als hoch der Knauf ist, je Seite als Anteil der Höhe.</summary>
    public double SchalterKnaufZugabe { get; init; } = Schaltergeometrie.KnaufZugabe;

    /// <summary>Feldbreite des Leuchtschalters (DIP, JG-Original 64).</summary>
    public double LeuchtBreite { get; init; } = Leuchtgeometrie.Breite;

    /// <summary>Feldhöhe des Leuchtschalters (DIP, JG-Original 34).</summary>
    public double LeuchtHoehe { get; init; } = Leuchtgeometrie.Hoehe;

    /// <summary>Durchmesser des Knaufs (DIP, JG-Original 28,6).</summary>
    public double LeuchtKnauf { get; init; } = Leuchtgeometrie.Knauf;

    /// <summary>Knauf geteilt durch Spurhöhe (JG-Original 1,14).</summary>
    public double LeuchtSpurVerhaeltnis { get; init; } = 1.14;

    /// <summary>Breite der Spur (DIP, JG-Original 56).</summary>
    public double LeuchtSpurBreite { get; init; } = Leuchtgeometrie.SpurBreite;

    /// <summary>Höchstbreite des Zeichens im Knauf (DIP, JG-Original 21,4).</summary>
    public double LeuchtZeichenBreite { get; init; } = Leuchtgeometrie.ZeichenBreite;

    /// <summary>Tiefe des Umgebungsschattens als Anteil der Höhe.</summary>
    public double SchattenTiefe { get; init; } = 0.07;

    /// <summary>Unschärfe des Umgebungsschattens als Anteil der Höhe.</summary>
    public double SchattenUnschaerfe { get; init; } = 0.5;

    public double SchattenTiefeGedrueckt { get; init; } = 0.03;
    public double SchattenUnschaerfeGedrueckt { get; init; } = 0.25;

    /// <summary>Kontaktschatten: Mitte als Anteil der Höhe, Breite und Höhe der Ellipse als Anteile, Deckung.</summary>
    public double KontaktLage { get; init; } = 0.9;

    public double KontaktBreite { get; init; } = 0.72;
    public double KontaktHoehe { get; init; } = 0.4;
    public double KontaktDeckung { get; init; } = 0.55;
    public double KontaktLageGedrueckt { get; init; } = 0.86;
    public double KontaktBreiteGedrueckt { get; init; } = 0.6;
    public double KontaktHoeheGedrueckt { get; init; } = 0.3;
    public double KontaktDeckungGedrueckt { get; init; } = 0.4;

    /// <summary>Größe der Zeichen im Glaskörper als Anteil der kürzeren Seite: Haken, Strich und Minus, alle übrigen.</summary>
    public double ZeichenHaken { get; init; } = 0.56;

    public double ZeichenStrich { get; init; } = 0.5;
    public double ZeichenUebrige { get; init; } = 0.44;

    /// <summary>Strichstärke der Zeichen im Glaskörper als Anteil der kürzeren Seite.</summary>
    public double ZeichenStrichstaerke { get; init; } = 0.11;

    /// <summary>Alle Werte mit Namen, in der Reihenfolge der Deklaration (für Export und Glint).</summary>
    public IReadOnlyList<(string Name, double Wert)> Werte() => Wertesatz.Lesen(this);
}

/// <summary>Liest die Zahlen eines Wertesatzes (<see cref="Glanzprofil"/>, <see cref="Bauplan"/>) per Reflection.</summary>
internal static class Wertesatz
{
    public static IReadOnlyList<(string Name, double Wert)> Lesen(object satz) =>
        [.. satz.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(double))
            .OrderBy(p => p.MetadataToken)
            .Select(p => (p.Name, (double)p.GetValue(satz)!))];

    /// <summary>Die Zahlen, falls <paramref name="wert"/> ein Wertesatz ist, sonst null.</summary>
    public static IReadOnlyList<(string Name, double Wert)>? Von(object? wert) => wert switch
    {
        Glanzprofil g => g.Werte(),
        Bauplan b => b.Werte(),
        Farbglasprofil f => f.Werte(),
        Mattprofil m => m.Werte(),
        Stilprofil s => s.Werte(),
        _ => null,
    };
}
