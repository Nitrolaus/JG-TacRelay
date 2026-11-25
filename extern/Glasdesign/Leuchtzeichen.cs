// Glasdesign - Maintainer: Sarevoc
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Zeichen für den Knauf des Leuchtschalters (angehängte Eigenschaft <c>Glas.Leuchtzeichen</c>). Ein Zeichen ist eine
/// Geometrie in einem Kasten von 250 x 83 Einheiten (<see cref="Leuchtgeometrie.ZeichenEinheitenBreite"/>); dessen Mitte
/// liegt auf der Knaufmitte, seine Breite wird zu <see cref="Leuchtmass.Zeichen"/> Pixeln. Eigene Zeichen halten sich an
/// diesen Kasten. Ohne Zeichen (null) bleibt der Knauf eine leere Glaslinse.
/// </summary>
public static class Leuchtzeichen
{
    /// <summary>
    /// "JG" von Joystick Guardian: die kantigen Umrisse aus dem JG-Original (GuardianSwitchVisual), Vektoren, keine Schrift
    /// und kein Bild. Füllt den Kasten genau.
    /// </summary>
    public static Geometry JG { get; } = Erzeugen(
        "M 48,0 L 120,0 L 120,65 L 103,83 L 0,83 L 18,65 L 91,65 L 91,18 L 66,18 Z " +
        "M 151,0 L 250,0 L 232,18 L 167,18 L 167,65 L 221,65 L 221,48 L 193,48 L 193,34 L 250,34 L 250,66 L 233,83 L 151,83 L 135,66 L 135,17 Z");

    private static Geometry Erzeugen(string daten)
    {
        var g = Geometry.Parse(daten);
        g.Freeze();
        return g;
    }
}
