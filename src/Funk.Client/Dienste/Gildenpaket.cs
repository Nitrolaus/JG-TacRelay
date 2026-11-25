// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Funk.Client.Dienste;

/// <summary>
/// Der Gildenbezug der Oberfläche (Sarevoc, Vorgabe e: austauschbar halten). In Stufe 2 nur das Logo, und zwar fest der Platzhalter
/// "Hier könnte dein Logo sein" (Eigenbau, Vorlage grafik\gildenlogo), eingebettet in TacRelay.dll. Stufe 4 bringt das unterschriebene Gildenpaket,
/// das der Server verteilt (Q6, Q24): Dann tauscht nur die Quelle, die Oberfläche fragt weiter hier nach dem Logo.
/// </summary>
/// <param name="logo">Öffnet die PNG-Datei des Logos (quadratisch, bis 512 x 512).</param>
public sealed class Gildenpaket(Func<Stream> logo)
{
    private const string Ressource = "Gildenlogo-Platzhalter.png";

    /// <summary>Breite, auf die das Logo beim Laden verkleinert wird: zweimal 96, scharf bis 200 % Anzeigeskalierung.</summary>
    public const int LogoPixel = 192;

    private ImageSource? _logo;

    /// <summary>Der Platzhalter aus TacRelay.dll.</summary>
    public static Gildenpaket Eingebaut { get; } = new(() => typeof(Gildenpaket).Assembly.GetManifestResourceStream(Ressource)
        ?? throw new InvalidOperationException("Das eingebaute Gildenlogo fehlt in TacRelay.dll."));

    /// <summary>Das Logo, einmal geladen und eingefroren (nutzbar aus jedem Thread).</summary>
    public ImageSource Logo => _logo ??= Laden();

    private BitmapImage Laden()
    {
        using var strom = logo();
        var bild = new BitmapImage();
        bild.BeginInit();
        bild.CacheOption = BitmapCacheOption.OnLoad;
        bild.DecodePixelWidth = LogoPixel;
        bild.StreamSource = strom;
        bild.EndInit();
        bild.Freeze();
        return bild;
    }
}
