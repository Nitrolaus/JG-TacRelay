// JG TacRelay - Maintainer: Sarevoc
using System.Windows.Media;

namespace Funk.Client.Design;

/// <summary>
/// Die Farben aus <c>Design\Farben.xaml</c> für Code, der selbst zeichnet (Pegelanzeige, Schwellengrafik): die Farbstellung
/// Anthrazit mit Petrol-Akzent (Sarevoc, 18./19.10.2026). Der Test <c>OberflaecheKontrastTests</c> hält beide Stellen gleich. Die
/// Farbstellung legt ihre Rollen darüber (<see cref="Farbvorschlaege"/>), und wer selbst zeichnet, holt die Rollen deshalb aus den
/// Ressourcen (<c>Akzente.Fuer</c>) und nimmt diese Werte nur als Ersatz.
/// </summary>
public static class Palette
{
    public static Color Anthrazit { get; } = Color.FromRgb(0x25, 0x25, 0x25);
    public static Color Flaeche { get; } = Color.FromRgb(0x2F, 0x2F, 0x2F);
    public static Color FlaecheTief { get; } = Color.FromRgb(0x1E, 0x1E, 0x1E);
    public static Color Linie { get; } = Color.FromRgb(0x45, 0x45, 0x45);

    /// <summary>Rolle für die großen Flächen (Titelleiste, Global, Gewähltes, Hauptaktion): dunkles Petrol, nur mit weißer Schrift.</summary>
    public static Color Leitfarbe { get; } = Color.FromRgb(0x0D, 0x35, 0x36);

    /// <summary>Die Leitfarbe gedrückt und gewählt (Umschalter, Kacheln), dunkler.</summary>
    public static Color LeitfarbeGedrueckt { get; } = Color.FromRgb(0x08, 0x26, 0x27);

    /// <summary>Rot für Warnungen und Fehler in Schrift.</summary>
    public static Color RotSchrift { get; } = Color.FromRgb(0xF0, 0x78, 0x78);

    /// <summary>Rolle für Rahmen, Linien, Fokus und Reglerspur: Petrol #2F8A8C (<see cref="Farbvorschlaege"/>).</summary>
    public static Color Akzent { get; } = Color.FromRgb(0x2F, 0x8A, 0x8C);

    /// <summary>Rolle für gefüllte Akzente (Schalter an): Petrol wie der Akzent, Weiß darauf 4,09:1.</summary>
    public static Color AkzentFlaeche { get; } = Color.FromRgb(0x2F, 0x8A, 0x8C);

    /// <summary>
    /// Rolle für den Knopf Schließen unter der Maus, auf der Titelleiste in der Leitfarbe: gedecktes Rot. Eigene Rolle, weil Schließen
    /// sich unter der Maus deutlich von der Titelleiste abheben muss.
    /// </summary>
    public static Color SchliessenFlaeche { get; } = Color.FromRgb(0xC8, 0x50, 0x4E);

    /// <summary>Glühen um den Strich des offenen Reiters (Raid-Seite): Petrol hell #4FB3B4.</summary>
    public static Color Glut { get; } = Color.FromRgb(0x4F, 0xB3, 0xB4);
    public static Color Text { get; } = Color.FromRgb(0xFF, 0xFF, 0xFF);
    public static Color Text2 { get; } = Color.FromRgb(0xCC, 0xCC, 0xCC);
    public static Color TextAus { get; } = Color.FromRgb(0x8D, 0x8D, 0x8D);

    /// <summary>
    /// Die Grenzlinie der Automatik in der Schwellengrafik und ihre Zone (Stufe 3, Schritt 3.5, N1 aus der Gegenprüfung 2c): ein klares Blau,
    /// damit sie sich von der roten Linie der Automatik unterscheidet (die Farbe der Linie kommt aus der Farbstellung, ihr Rot und das Blau
    /// trennen sich in jeder). 6,8:1 auf der Grafikfläche #1E1E1E, in jeder Farbstellung gleich.
    /// </summary>
    public static Color Grenzlinie { get; } = Color.FromRgb(0x5E, 0xA9, 0xFF);

    /// <summary>
    /// Grenze kleiner Bedienelemente im Funkfeld (Knopfrand, Reglerspur, Rand der Marke AUS): 5,1:1 auf der Kachelfläche,
    /// 5,1:1 auf Global. <see cref="TextAus"/> war dafür zu schwach und ließ der Durchsichtigkeit keinen Spielraum
    /// (<see cref="FunkfeldDeckung"/>).
    /// </summary>
    public static Color Grenze { get; } = Color.FromRgb(0xA0, 0xA0, 0xA0);
    public static Color Senden { get; } = Color.FromRgb(0x33, 0xCC, 0x66);
    public static Color Hoeren { get; } = Color.FromRgb(0xFF, 0x9F, 0x1A);

    /// <summary>Sprache über den Tunnel (Steuerkanal statt UDP), auf der Startseite: Gelb, 8,5:1 auf der Kartenfläche, nie allein.</summary>
    public static Color Tunnel { get; } = Color.FromRgb(0xF2, 0xC9, 0x4C);

    /// <summary>
    /// Q43 (0.8.0): Gelb der Pegelanzeigen, wenn die Sprachaktivierung auslösen würde, aber nichts hinausgeht (Ruhe, kein Kanal, nicht verbunden,
    /// stumm durch die Einsatzleitung). Dasselbe Gelb wie <see cref="Tunnel"/> und das "!" der Dauersend-Dämpfung, eine eigene Rolle wie
    /// <see cref="Senden"/>; 10,5:1 auf #1E1E1E, nie allein (der Satz in Wörtern steht dabei).
    /// </summary>
    public static Color WuerdeSenden { get; } = Color.FromRgb(0xF2, 0xC9, 0x4C);
    public static Color AufFarbe { get; } = Color.FromRgb(0x14, 0x14, 0x14);

    /// <summary>
    /// Deckkraft einer Kachel im Zustand AUS (A17). Gedimmt, aber lesbar: Die zweitrangige Schrift erreicht damit auf der
    /// Kachelfläche noch 4,9:1. Bei 0,5 waren es 3,3:1, zu wenig für "AUS" und "Ruhe" (Test OberflaecheKontrastTests).
    /// </summary>
    public const double AusDeckkraft = 0.7;

    /// <summary>
    /// Deckkraft des Inhalts eines leeren Felds (Stil Kachel.Blass), deckend auf der Kachelfläche: Der Name "frei" und die
    /// Marke "FREI" (im leeren Feld weiß statt zweitrangig) haben damit 5,2:1 (#A1A1A1 auf #2F2F2F). Bei 0,45 waren es 4,03:1
    /// und für "FREI" in zweitrangiger Schrift 2,95:1. Auf Global rechnet <see cref="FunkfeldDeckung"/> je Farbstellung nach;
    /// dies ist die Untergrenze.
    /// </summary>
    public const double LeerDeckkraft = 0.55;
}

/// <summary>Kontrastverhältnis nach WCAG 2.1 (relative Leuchtdichte, sRGB). Alpha wird nicht berücksichtigt.</summary>
public static class Kontrast
{
    /// <summary>Verhältnis zwischen 1 und 21; die Reihenfolge der Farben spielt keine Rolle.</summary>
    public static double Verhaeltnis(Color a, Color b)
    {
        var la = Leuchtdichte(a);
        var lb = Leuchtdichte(b);
        var hell = Math.Max(la, lb);
        var dunkel = Math.Min(la, lb);
        return (hell + 0.05) / (dunkel + 0.05);
    }

    /// <summary>Relative Leuchtdichte 0..1.</summary>
    public static double Leuchtdichte(Color c) =>
        (0.2126 * Kanal(c.R)) + (0.7152 * Kanal(c.G)) + (0.0722 * Kanal(c.B));

    private static double Kanal(byte wert)
    {
        var s = wert / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
