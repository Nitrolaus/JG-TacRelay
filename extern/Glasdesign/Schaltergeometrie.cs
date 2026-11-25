// Glasdesign - Maintainer: Sarevoc
using System.Windows;

namespace Glasdesign;

/// <summary>Maße des Schiebeschalters in ganzen Gerätepixeln (Spur und Knauf).</summary>
public sealed record Schaltermass(int SpurBreite, int SpurHoehe, int Rand, int Abstand, int KnaufX, int KnaufY, int KnaufBreite, int KnaufHoehe)
{
    /// <summary>Rechteck des Knaufs in DIP, bezogen auf die Spur.</summary>
    public Rect KnaufDip(double skala) => new(KnaufX / skala, KnaufY / skala, KnaufBreite / skala, KnaufHoehe / skala);

    /// <summary>Gesamtabstand des Knaufs von der Außenkante der Spur (Haarlinie plus Abstand), oben, unten und am Ende gleich.</summary>
    public int Luft => Rand + Abstand;
}

/// <summary>
/// Sarevocs Vorgabe vom 02.10.2026: Der Knauf hat oben, unten und zur jeweiligen Endseite pixelgenau denselben Abstand zur
/// Außenkante der Spur, in beiden Stellungen und bei jeder Skalierung. Der Abstand ist EIN Wert: die Haarlinie der Spur
/// (ein Gerätepixel, Glas 3) plus <see cref="Abstand"/> auf ganze Pixel gerundet; daraus folgen Knaufhöhe und beide
/// Knaufstellungen.
/// </summary>
public static class Schaltergeometrie
{
    /// <summary>Höhe der Spur in DIP, wenn nichts anderes gesetzt ist.</summary>
    public const double Hoehe = 26;

    /// <summary>Breite der Spur als Vielfaches ihrer Höhe (Vorlage: 150 zu 58, gerundet).</summary>
    public const double Seitenverhaeltnis = 2.3;

    /// <summary>Luft zwischen Haarlinie der Spur und Knauf, in DIP.</summary>
    public const double Abstand = 2;

    /// <summary>Wie viel breiter als hoch der Knauf ist, je Seite als Anteil der Höhe (Vorlage: 80 zu 52).</summary>
    public const double KnaufZugabe = 0.27;

    public static Schaltermass Berechnen(Size spur, double skala, bool an, double abstand = Abstand, double knaufZugabe = KnaufZugabe)
    {
        var b = (int)Glasgeometrie.Raster(spur.Width, skala);
        var h = (int)Glasgeometrie.Raster(spur.Height, skala);
        var r = Glasgeometrie.Haarlinie;
        var a = (int)Glasgeometrie.Linie(abstand, skala);
        var luft = r + a;
        var kh = Math.Max(1, h - 2 * luft);
        // Breite minus Höhe gerade: Dann sitzt ein Zeichen im Knauf waagerecht und senkrecht auf demselben Raster.
        var kb = Math.Min(Math.Max(kh, b - 2 * luft), kh + 2 * (int)Math.Round(kh * knaufZugabe, MidpointRounding.AwayFromZero));
        if ((kb - kh) % 2 != 0) kb--;
        var x = an ? b - luft - kb : luft;
        return new Schaltermass(b, h, r, a, x, luft, kb, kh);
    }
}
