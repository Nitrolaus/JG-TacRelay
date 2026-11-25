// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace Funk.Client.Design;

/// <summary>
/// Welche Farbstellung die Oberfläche trägt. Seit dem 19.10.2026 gibt es genau eine (Sarevoc, Q5, Q12, Q17): Anthrazit mit Petrol-Akzent.
/// Die drei Weinrot-Stellungen P1 bis P3 sind entfallen; ein alter gespeicherter Wert wird beim Lesen still zu <see cref="Anthrazit"/>
/// (<c>NachsichtigeFarbstellung</c>, <c>ClientEinstellungen.Normalisieren</c>).
/// </summary>
public enum Farbstellung
{
    /// <summary>Anthrazit und Grau, neutral und ruhig, gedämpfter Akzent Petrol #2F8A8C, hell #4FB3B4.</summary>
    Anthrazit,
}

/// <summary>
/// Die Farben, die eine Stellung ändert; alles andere (Grautöne, Grün, Orange, Gelb) bleibt. <see cref="Leitfarbe"/> und
/// <see cref="LeitfarbeGedrueckt"/> sind die großen Flächen (Titelleiste, Global, gewählte Umschalter, Hauptaktion),
/// <see cref="AkzentFlaeche"/> die gefüllten Akzente (Schalter an), <see cref="Akzent"/> Rahmen, Linien, Fokus und Reglerspur,
/// <see cref="RotSchrift"/> Warnungen und Fehler, <see cref="SchliessenFlaeche"/> der Knopf Schließen unter der Maus. Der braucht eine
/// eigene Rolle: Er sitzt auf der Titelleiste in der Leitfarbe und muss sich unter der Maus deutlich von ihr abheben.
/// </summary>
public sealed record Farbsatz(Farbstellung Stellung, string Name, Color Leitfarbe, Color LeitfarbeGedrueckt, Color AkzentFlaeche, Color Akzent, Color RotSchrift,
    Color SchliessenFlaeche)
{
    /// <summary>
    /// Das Glühen um den Strich des offenen Reiters auf der Raid-Seite (0.11.3, Sarevoc 13.10.2026: "rein mit Glow"): hell und satt, damit es auf
    /// der dunklen Fläche sichtbar leuchtet; in Anthrazit Petrol hell #4FB3B4.
    /// </summary>
    public Color Glut { get; init; } = Palette.Glut;

    /// <summary>Korridor der Schwellengrafik: der Akzent mit 15 % Deckkraft.</summary>
    public Color KorridorFlaeche => Color.FromArgb(0x26, Akzent.R, Akzent.G, Akzent.B);
}

/// <summary>
/// Die Farbstellung der Oberfläche, seit dem 19.10.2026 genau eine: <see cref="Anthrazit"/> (Sarevoc, Q5, Q12, Q17); eine Wahl unter
/// Einstellungen gibt es nicht mehr. Sie ist ein Wörterbuch, das hinter <c>Design\Farben.xaml</c> gelegt wird und nur die Rollen aus
/// <see cref="Farbsatz"/> ersetzt (<see cref="Anwenden(ResourceDictionary, Farbstellung)"/>, in <c>App.OnStartup</c>). Ihre Werte sind die
/// von Farben.xaml und <see cref="Palette"/>, damit Code, der selbst zeichnet, mit dem Ersatzwert dasselbe zeigt. Kontraste gemessen nach
/// WCAG 2.1 gegen Anthrazit #252525, die Kachelfläche #2F2F2F und die Unterlage #1E1E1E (Test OberflaecheFarbvorschlagTests):
/// <list type="bullet">
/// <item>Leitfarbe #0D3536 (dunkles Petrol): Weiß 13,29:1, Zweitschrift 8,28:1, Grün 6,31:1, Orange 6,47:1, Grenze 5,08:1; gedrückt #082627
/// mit Weiß 15,95:1. Auf Anthrazit nur 1,15:1, also nie Schrift oder Linie. So dunkel, damit leere Felder bei 0,55 und weiße Namen über
/// hellem Spielbild bei 48 % Durchsichtigkeit noch 3:1 halten (mit #1A4647 waren es 0,58 und 2,80:1, OberflaecheFunkfeldDurchsichtTests).</item>
/// <item>Akzent Petrol #2F8A8C: 3,75:1, 3,27:1 und 4,08:1; Weiß auf der Akzentfläche (gleiche Farbe) 4,09:1.</item>
/// <item>Rote Schrift #F07878: 5,59:1, 4,89:1 und 6,08:1.</item>
/// <item>Schließen unter der Maus #C8504E: Weiß darauf 4,46:1, auf der Titelzeile des Funkfelds 3,74:1, gegen die Titelleiste 2,98:1.</item>
/// <item>Glühen Petrol hell #4FB3B4: 6,70:1 auf der Unterlage.</item>
/// </list>
/// </summary>
public static class Farbvorschlaege
{
    /// <summary>Schlüssel, unter dem ein eingesetztes Wörterbuch seine Stellung trägt; so findet <see cref="Anwenden(ResourceDictionary, Farbsatz)"/> es wieder.</summary>
    public const string Kennung = "Farbvorschlag.Stellung";

    /// <summary>Die Farbstellung, auch für jeden alten gespeicherten Wert: Anthrazit.</summary>
    public const Farbstellung Voreinstellung = Farbstellung.Anthrazit;

    /// <summary>Anthrazit mit Petrol-Akzent, Werte wie in Farben.xaml und <see cref="Palette"/>.</summary>
    public static Farbsatz Anthrazit { get; } = new(Farbstellung.Anthrazit, "Anthrazit", Palette.Leitfarbe, Palette.LeitfarbeGedrueckt, Palette.AkzentFlaeche,
        Palette.Akzent, Palette.RotSchrift, Palette.SchliessenFlaeche);

    public static IReadOnlyList<Farbsatz> Alle { get; } = [Anthrazit];

    /// <summary>Der Farbsatz einer Stellung; ein unbekannter Wert bekommt den der <see cref="Voreinstellung"/>.</summary>
    public static Farbsatz Von(Farbstellung stellung) => Alle.FirstOrDefault(s => s.Stellung == stellung) ?? Anthrazit;

    /// <summary>Wörterbuch mit Farben (<c>Farbe.*</c>) und eingefrorenen Pinseln (<c>Pinsel.*</c>) eines Satzes.</summary>
    public static ResourceDictionary Woerterbuch(Farbsatz satz)
    {
        ArgumentNullException.ThrowIfNull(satz);
        var w = new ResourceDictionary { [Kennung] = satz.Stellung };
        void Setzen(string rolle, Color farbe)
        {
            w["Farbe." + rolle] = farbe;
            var pinsel = new SolidColorBrush(farbe);
            pinsel.Freeze();
            w["Pinsel." + rolle] = pinsel;
        }
        Setzen("Leitfarbe", satz.Leitfarbe);
        Setzen("LeitfarbeGedrueckt", satz.LeitfarbeGedrueckt);
        Setzen("AkzentFlaeche", satz.AkzentFlaeche);
        Setzen("Akzent", satz.Akzent);
        Setzen("RotSchrift", satz.RotSchrift);
        Setzen("SchliessenFlaeche", satz.SchliessenFlaeche);
        Setzen("KorridorFlaeche", satz.KorridorFlaeche);
        Setzen("Glut", satz.Glut);
        return w;
    }

    /// <summary>
    /// Legt die Stellung in <paramref name="ziel"/> (im Programm <c>Application.Resources</c>) direkt hinter Farben.xaml und
    /// nimmt eine früher gesetzte heraus. Die Wörterbücher dahinter (Symbole, Stile) werden frisch geladen: WPF löst StaticResource in
    /// Stile.xaml schon beim Laden auf, wenn es den Schlüssel dann in den Ressourcen der Anwendung findet, und die Stile behielten sonst
    /// die Farben von vorher. Vor dem ersten Fenster aufrufen; was schon steht, behält seine Farben.
    /// </summary>
    public static void Anwenden(ResourceDictionary ziel, Farbstellung stellung) => Anwenden(ziel, Von(stellung));

    /// <summary>
    /// Wie <see cref="Anwenden(ResourceDictionary, Farbstellung)"/>, mit einem beliebigen Satz; so zeichnet ein Test auch einen Satz, der
    /// keine Stellung ist.
    /// </summary>
    public static void Anwenden(ResourceDictionary ziel, Farbsatz satz)
    {
        ArgumentNullException.ThrowIfNull(ziel);
        ArgumentNullException.ThrowIfNull(satz);
        var liste = ziel.MergedDictionaries;
        var alt = liste.FirstOrDefault(d => d.Contains(Kennung));
        if (alt is not null) liste.Remove(alt);
        var farben = -1;
        for (var i = 0; i < liste.Count; i++)
        {
            if (liste[i].Source?.OriginalString.EndsWith("Farben.xaml", StringComparison.OrdinalIgnoreCase) == true) farben = i;
        }
        var ab = farben >= 0 ? farben + 1 : liste.Count;
        liste.Insert(ab++, Woerterbuch(satz));
        for (var i = ab; i < liste.Count; i++)
        {
            if (liste[i].Source is not { } quelle) continue;
            var neu = new ResourceDictionary();
            ((System.Windows.Markup.IUriContext)neu).BaseUri = ((System.Windows.Markup.IUriContext)liste[i]).BaseUri;
            neu.Source = quelle;
            liste[i] = neu;
        }
    }
}

/// <summary>
/// Pinsel und Stifte der Akzente für Code, der selbst zeichnet (Schwellengrafik, Pegelbalken). Die Farben kommen aus den
/// Ressourcen am Element, damit eine Farbstellung auch dort wirkt; je Farbsatz einmal gebaut und eingefroren, also ohne
/// Allokation im Bildtakt.
/// </summary>
internal sealed class Akzente
{
    private static readonly ConcurrentDictionary<(Color Akzent, Color Leitfarbe, Color Schrift, Color Senden, Color WuerdeSenden), Akzente> Vorrat = new();

    private Akzente(Color akzent, Color leitfarbe, Color schrift, Color senden, Color wuerdeSenden)
    {
        Senden = Frieren(new SolidColorBrush(senden));
        SendenFlaeche = Frieren(new SolidColorBrush(Color.FromArgb(0x40, senden.R, senden.G, senden.B)));
        SendenStift = Frieren(new Pen(Senden, PegelLinienbreite) { LineJoin = PenLineJoin.Round });
        WuerdeSenden = Frieren(new SolidColorBrush(wuerdeSenden));
        WuerdeSendenFlaeche = Frieren(new SolidColorBrush(WuerdeSendenFlaecheFarbe(wuerdeSenden)));
        WuerdeSendenStift = Frieren(new Pen(WuerdeSenden, PegelLinienbreite) { LineJoin = PenLineJoin.Round });
        Akzent = Frieren(new SolidColorBrush(akzent));
        KorridorFlaeche = Frieren(new SolidColorBrush(Color.FromArgb(0x26, akzent.R, akzent.G, akzent.B)));
        Leitfarbe = Frieren(new SolidColorBrush(leitfarbe));
        SchwellenStift = Frieren(new Pen(Akzent, 2.5) { LineJoin = PenLineJoin.Round });
        KorridorStift = Frieren(new Pen(Akzent, 1) { DashStyle = new DashStyle([4, 3], 0) });
        SicherheitStift = Frieren(new Pen(new SolidColorBrush(schrift), 1.5) { DashStyle = new DashStyle([4, 3], 0) });
        AutomatikStift = Frieren(new Pen(new SolidColorBrush(AutomatikFarbe(schrift)), AutomatikLinienbreite) { LineJoin = PenLineJoin.Round });
        ZoneFlaeche = Frieren(new SolidColorBrush(ZoneFarbe));
        SchildRand = Frieren(new Pen(Akzent, 1.5));
        Stift2 = Frieren(new Pen(Akzent, 2));
    }

    /// <summary>
    /// Das Grün beim Senden aus der Farbstellung (Ressource <c>Farbe.Senden</c>; Q41, ui-fix 0.6.2): Pegelbalken, Pegelkurve und Sendeabschnitte der
    /// Schwellengrafik nehmen dasselbe Grün wie Kacheln und Meldezeile, kein festes.
    /// </summary>
    public Brush Senden { get; }

    /// <summary>Die Hinterlegung gesendeter Abschnitte in der Schwellengrafik: das Grün mit 25 % Deckkraft.</summary>
    public Brush SendenFlaeche { get; }

    /// <summary>Die Pegelkurve der Schwellengrafik, solange gesendet wird (Q41).</summary>
    public Pen SendenStift { get; }

    /// <summary>
    /// Q43 (0.8.0): Das Gelb, wenn die Sprachaktivierung auslösen würde, aber nichts hinausgeht, aus der Farbstellung (Ressource
    /// <c>Farbe.WuerdeSenden</c>), kein festes: Pegelbalken, Pegelkurve und Hinterlegung der Schwellengrafik.
    /// </summary>
    public Brush WuerdeSenden { get; }

    /// <summary>Die Hinterlegung der Abschnitte, die gesendet hätten: das Gelb mit <see cref="WuerdeSendenDeckkraft"/>.</summary>
    public Brush WuerdeSendenFlaeche { get; }

    /// <summary>Die Pegelkurve der Schwellengrafik, solange sie gesendet hätte (Q43).</summary>
    public Pen WuerdeSendenStift { get; }

    /// <summary>
    /// Deckkraft der gelben Hinterlegung (0x34, rund 20 %): Das Gelb ist heller als das Grün, mit 25 % wäre der Abschnitt heller als ein gesendeter
    /// und die Linien darauf verlören Kontrast (gemessen mit dem früheren hellen Rot: die Linie der Automatik mit Zone 3,21 statt 3,52:1). Mit 0x34 ist er höchstens so hell
    /// wie der grüne (relative Leuchtdichte 0,0534 gegen 0,0547 auf #1E1E1E); jede Linie hält darauf, was sie im gesendeten Abschnitt hält.
    /// </summary>
    public const byte WuerdeSendenDeckkraft = 0x34;

    /// <summary>Die gelbe Hinterlegung zu <paramref name="gelb"/> (Stufe35OberflaecheTests misst sie wie die grüne).</summary>
    public static Color WuerdeSendenFlaecheFarbe(Color gelb) => Color.FromArgb(WuerdeSendenDeckkraft, gelb.R, gelb.G, gelb.B);

    /// <summary>Breite der Pegelkurve, wie <c>SchwellenGrafik.PegelLinienbreite</c>.</summary>
    public const double PegelLinienbreite = 1.0;

    /// <summary>Akzent als Fläche (runder Griff der Schwellenlinie).</summary>
    public Brush Akzent { get; }

    public Brush KorridorFlaeche { get; }

    /// <summary>Die Leitfarbe als Fläche mit weißer Schrift (beschriftete Griffe).</summary>
    public Brush Leitfarbe { get; }

    public Pen SchwellenStift { get; }

    public Pen KorridorStift { get; }

    public Pen SicherheitStift { get; }

    /// <summary>Breite der roten Linie der Automatik in der Schwellengrafik (Einheiten): dünn, sie zeigt nur an.</summary>
    public const double AutomatikLinienbreite = 1.0;

    /// <summary>
    /// Die Schwelle der Automatik in der Schwellengrafik (Stufe 2c): dünn im Rot für Schrift auf dunklem Grund, also in jeder
    /// Farbstellung rot (der Akzent ist Petrol), seit Schritt 3.5 um ein Viertel zum Weiß aufgehellt (<see cref="AutomatikFarbe"/>).
    /// Verlangt und gemessen (Stufe35OberflaecheTests): mindestens 5:1 auf der Grafikfläche und auf der Zone und mindestens 3:1 im
    /// gesendeten Abschnitt auch auf der Zone. Bis 3.5 hielt sie auf der Zone nur 4,64 bis 4,70:1 (Gegenprüfung 2c, N2).
    /// </summary>
    public Pen AutomatikStift { get; }

    /// <summary>Anteil, um den die rote Linie der Automatik zum Weiß aufgehellt wird: genug für 5:1 auf der Zone, wenig genug, dass sie rot bleibt.</summary>
    public const double AutomatikAufhellung = 0.25;

    /// <summary>Die Farbe der Linie der Automatik: die rote Schrift der Farbstellung, <see cref="AutomatikAufhellung"/> zum Weiß hin.</summary>
    public static Color AutomatikFarbe(Color schrift) => Color.FromRgb(
        (byte)Math.Round(schrift.R + ((255 - schrift.R) * AutomatikAufhellung)), (byte)Math.Round(schrift.G + ((255 - schrift.G) * AutomatikAufhellung)),
        (byte)Math.Round(schrift.B + ((255 - schrift.B) * AutomatikAufhellung)));

    /// <summary>
    /// Die Zone, in der sich die rote Linie der Automatik bewegt: <see cref="Palette.Grenzlinie"/> (blau) mit 10 % Deckkraft (wie
    /// <c>Farbe.GrenzlinieFlaeche</c> in Farben.xaml). Bis Klangstufe C1 über der Grenzlinie aus Stufe 2c, seitdem der Korridor zwischen
    /// Mindest-Funklautstärke und Obergrenze am Pegelbalken und in der Schwellengrafik; die Linien des Korridors stehen im Akzent. Blau statt
    /// Akzent, weil die rote Linie nur so auf der Zone 5:1 hält (Stufe35OberflaecheTests).
    /// </summary>
    internal static Color ZoneFarbe { get; } = Color.FromArgb(0x1A, Palette.Grenzlinie.R, Palette.Grenzlinie.G, Palette.Grenzlinie.B);

    public Brush ZoneFlaeche { get; }

    public Pen SchildRand { get; }

    /// <summary>2 Einheiten breit: Fokus der Grafik, Schwellenmarke des Pegelbalkens, Linien des Korridors (Klangstufe C1).</summary>
    public Pen Stift2 { get; }

    public static Akzente Fuer(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var akzent = element.TryFindResource("Farbe.Akzent") is Color a ? a : Palette.Akzent;
        var leitfarbe = element.TryFindResource("Farbe.Leitfarbe") is Color w ? w : Palette.Leitfarbe;
        var schrift = element.TryFindResource("Farbe.RotSchrift") is Color s ? s : Palette.RotSchrift;
        var senden = element.TryFindResource("Farbe.Senden") is Color g ? g : Palette.Senden;
        var wuerdeSenden = element.TryFindResource("Farbe.WuerdeSenden") is Color y ? y : Palette.WuerdeSenden;
        return Vorrat.GetOrAdd((akzent, leitfarbe, schrift, senden, wuerdeSenden), k => new Akzente(k.Akzent, k.Leitfarbe, k.Schrift, k.Senden, k.WuerdeSenden));
    }

    private static T Frieren<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
