// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Design;

/// <summary>
/// Ein Meldungsbild, auf seine sichtbaren Bildpunkte zugeschnitten: <see cref="Bild"/> (eingefroren), dazu wo der Ausschnitt im ganzen Bild
/// lag (<see cref="Ausschnitt"/>, in Bildpunkten des Originals mit längster Seite 600) und wie groß das ganze Bild war. Die Countdown-Felder
/// sind im ganzen Bild gemessen und rechnen über den Ausschnitt in den Zuschnitt um.
/// </summary>
public sealed record Meldungsgrafik(BitmapSource Bild, Int32Rect Ausschnitt, int GanzBreite, int GanzHoehe)
{
    /// <summary>Breite durch Höhe des Zuschnitts.</summary>
    public double Seitenverhaeltnis => Ausschnitt.Height > 0 ? Ausschnitt.Width / (double)Ausschnitt.Height : 1;
}

/// <summary>
/// Die Meldungsbilder aus den eingebetteten Ressourcen (seit 0.12.0, <c>Meldungen.&lt;ausfuehrung&gt;.&lt;bild&gt;.png</c>, Namen aus
/// <see cref="Meldungskatalog.Ressource"/>). Jedes Bild wird auf seine sichtbaren Bildpunkte zugeschnitten (Deckkraft über
/// <see cref="Schwelle"/>): Die Bilder haben verschieden breite durchsichtige Ränder (die Countdowns oben und unten fast ein Drittel, Nur Schrift
/// zwei Drittel), und ohne Zuschnitt stünden Zeile und Nachbarn unterschiedlich weit weg. Kacheln der Leitung werden klein dekodiert
/// (<see cref="KachelPixel"/>) und gemerkt; die großen über dem Spiel nur so lange, wie sie gebraucht werden (höchstens
/// <see cref="GrossGemerkt"/> zugleich). Ein fehlendes Bild ergibt <c>null</c>, kein Fehler.
/// </summary>
public static class Meldungsbilder
{
    /// <summary>Kacheln dekodiert mit dieser längsten Seite (52 Einheiten Kachel, bei 200 % gut doppelt).</summary>
    public const int KachelPixel = 112;

    /// <summary>Ab dieser Deckkraft (0 bis 255) zählt ein Bildpunkt als sichtbar.</summary>
    public const byte Schwelle = 16;

    /// <summary>So viele große Bilder bleiben gemerkt (die Plätze über dem Spiel und eine Probe).</summary>
    public const int GrossGemerkt = 8;

    private static readonly Dictionary<string, Meldungsgrafik?> Kacheln = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Meldungsgrafik?> Gross = new(StringComparer.Ordinal);
    private static readonly Lock Sperre = new();

    /// <summary>Das Bild einer Kachel (klein dekodiert, zugeschnitten); <c>null</c>, wenn es die Ressource nicht gibt.</summary>
    public static Meldungsgrafik? Kachel(string? ressource)
    {
        if (ressource is null) return null;
        lock (Sperre)
        {
            if (Kacheln.TryGetValue(ressource, out var k)) return k;
            var neu = Laden(ressource, KachelPixel);
            Kacheln[ressource] = neu;
            return neu;
        }
    }

    /// <summary>Das Bild in voller Größe (längste Seite 600, zugeschnitten); <c>null</c>, wenn es die Ressource nicht gibt.</summary>
    public static Meldungsgrafik? Ganz(string? ressource)
    {
        if (ressource is null) return null;
        lock (Sperre)
        {
            if (Gross.TryGetValue(ressource, out var g)) return g;
            if (Gross.Count >= GrossGemerkt) Gross.Clear();
            var neu = Laden(ressource, null);
            Gross[ressource] = neu;
            return neu;
        }
    }

    /// <summary>Der Ausschnitt der sichtbaren Bildpunkte in einem Bild aus Bgra32-Zeilen; ohne sichtbare das ganze Bild.</summary>
    public static Int32Rect Sichtbar(byte[] bgra, int breite, int hoehe, int schritt)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        int links = breite, rechts = -1, oben = hoehe, unten = -1;
        for (var y = 0; y < hoehe; y++)
        {
            var zeile = y * schritt;
            for (var x = 0; x < breite; x++)
            {
                if (bgra[zeile + (x * 4) + 3] <= Schwelle) continue;
                if (x < links) links = x;
                if (x > rechts) rechts = x;
                if (y < oben) oben = y;
                if (y > unten) unten = y;
            }
        }
        return rechts < 0 ? new Int32Rect(0, 0, breite, hoehe) : new Int32Rect(links, oben, rechts - links + 1, unten - oben + 1);
    }

    private static Meldungsgrafik? Laden(string ressource, int? laengsteSeite)
    {
        using var strom = typeof(Meldungsbilder).Assembly.GetManifestResourceStream(ressource);
        if (strom is null) return null;
        var bild = new BitmapImage();
        bild.BeginInit();
        bild.CacheOption = BitmapCacheOption.OnLoad;
        bild.StreamSource = strom;
        bild.EndInit();
        bild.Freeze();
        BitmapSource quelle = bild;
        if (laengsteSeite is { } l && Math.Max(bild.PixelWidth, bild.PixelHeight) > l)
        {
            var faktor = l / (double)Math.Max(bild.PixelWidth, bild.PixelHeight);
            var klein = new TransformedBitmap(bild, new ScaleTransform(faktor, faktor));
            klein.Freeze();
            quelle = klein;
        }
        var bgra = new FormatConvertedBitmap(quelle, PixelFormats.Bgra32, null, 0);
        var schritt = bgra.PixelWidth * 4;
        var punkte = new byte[schritt * bgra.PixelHeight];
        bgra.CopyPixels(punkte, schritt, 0);
        var ausschnitt = Sichtbar(punkte, bgra.PixelWidth, bgra.PixelHeight, schritt);
        var zugeschnitten = new CroppedBitmap(quelle, ausschnitt);
        zugeschnitten.Freeze();
        // Der Ausschnitt in Bildpunkten des Originals (die Countdown-Felder sind dort gemessen).
        var f = bild.PixelWidth / (double)quelle.PixelWidth;
        var imOriginal = new Int32Rect((int)Math.Round(ausschnitt.X * f), (int)Math.Round(ausschnitt.Y * f), (int)Math.Round(ausschnitt.Width * f),
            (int)Math.Round(ausschnitt.Height * f));
        return new Meldungsgrafik(zugeschnitten, imOriginal, bild.PixelWidth, bild.PixelHeight);
    }
}
