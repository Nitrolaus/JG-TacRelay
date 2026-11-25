// Glasdesign - Maintainer: Sarevoc
using System.Windows;

namespace Glasdesign;

/// <summary>
/// Maße des Leuchtschalters in ganzen Gerätepixeln, bezogen auf seine linke obere Ecke. <see cref="Skala"/> rechnet in DIP
/// zurück. Der Knauf überragt die Spur oben, unten und an der Endseite um denselben <see cref="Ueberstand"/>.
/// </summary>
public readonly record struct Leuchtmass(
    double Skala, int Breite, int Hoehe, int Knauf, int Spur, int Ueberstand,
    int SpurLinks, int SpurOben, int SpurBreite, int KnaufOben, int Haarlinie, int Zeichen)
{
    /// <summary>Der Rand je Seite: in Glas 3 nur die Haarlinie (ein Gerätepixel).</summary>
    public int Ringe => Haarlinie;

    /// <summary>Durchmesser des Glaskörpers im Knauf (innerhalb der Ringe).</summary>
    public int Koerper => Knauf - 2 * Ringe;

    public int KnaufLinksAus => SpurLinks - Ueberstand;

    public int KnaufLinksAn => SpurLinks + SpurBreite + Ueberstand - Knauf;

    /// <summary>Linke Kante des Knaufs in Pixeln; ganzzahlig in beiden Endstellungen, dazwischen gleitend.</summary>
    public double KnaufLinks(double stellung) => KnaufLinksAus + Math.Clamp(stellung, 0, 1) * (KnaufLinksAn - KnaufLinksAus);

    /// <summary>Knauf in DIP bei <paramref name="stellung"/> (0 = aus, 1 = an).</summary>
    public Rect KnaufDip(double stellung) => new(KnaufLinks(stellung) / Skala, KnaufOben / Skala, Knauf / Skala, Knauf / Skala);

    /// <summary>Spur in DIP.</summary>
    public Rect SpurDip => new(SpurLinks / Skala, SpurOben / Skala, SpurBreite / Skala, Spur / Skala);
}

/// <summary>
/// Geometrie des Leuchtschalters (JG-Schalter im Glaslook). Die Sollmaße stammen unverändert aus dem JG-Original
/// (GuardianSwitchVisual, Build 5.19.86.284): Feld 64 x 34 DIP, Knauf 28,6 DIP, Spur 28,6 / 1,14 DIP hoch und 56 DIP
/// breit, Zeichen 21,4 DIP breit. Neu ist nur, dass alles auf ganze Gerätepixel fällt, damit die Haarlinie scharf bleibt
/// (wie <see cref="Glasgeometrie"/>):
/// <list type="bullet">
/// <item>Knauf und Spur haben gleiche Parität, der Überstand ist also eine ganze Zahl; gewählt wird das Paar aus ab- und
/// aufgerundeten Werten mit der kleinsten Summe der Abweichungen (höchstens etwa ein Pixel).</item>
/// <item>Der Rand ist in Glas 3 nur die Haarlinie, genau ein Gerätepixel bei jeder Skalierung (wie die Haarlinien des
/// Originals). So hat das JG-Zeichen im Glaskörper reichlich Platz.</item>
/// <item>Das Zeichen ist höchstens <see cref="ZeichenBreite"/> DIP breit und hat die Parität des Knaufs: Es sitzt auf ganzen
/// Pixeln mittig.</item>
/// </list>
/// Gerundet wird wie die Layoutrundung von WPF (<see cref="Math.Round(double)"/>, bei ,5 zur geraden Zahl).
/// </summary>
public static class Leuchtgeometrie
{
    public const double Breite = 64;
    public const double Hoehe = 34;
    public const double Knauf = 28.6;
    public const double Spur = Knauf / 1.14;
    public const double SpurBreite = 56;
    public const double ZeichenBreite = 21.4;

    /// <summary>Das Zeichen wird in einem Kasten von 250 x 83 Einheiten gezeichnet, dessen Mitte auf der Knaufmitte liegt.</summary>
    public const double ZeichenEinheitenBreite = 250;
    public const double ZeichenEinheitenHoehe = 83;

    public static Leuchtmass Berechnen(double skala) => Berechnen(skala, Bauplan.Standard);

    /// <summary>Maße nach einem <see cref="Bauplan"/> (Feld, Knauf, Spur, Zeichen), gerundet wie oben beschrieben.</summary>
    public static Leuchtmass Berechnen(double skala, Bauplan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!(skala > 0) || double.IsInfinity(skala)) skala = 1;
        var breite = (int)Math.Round(plan.LeuchtBreite * skala);
        var hoehe = (int)Math.Round(plan.LeuchtHoehe * skala);
        double dSoll = plan.LeuchtKnauf * skala, tSoll = plan.LeuchtKnauf / plan.LeuchtSpurVerhaeltnis * skala;
        int knauf = 0, spur = 0;
        var fehler = double.MaxValue;
        foreach (var d in new[] { (int)Math.Floor(dSoll), (int)Math.Ceiling(dSoll) })
            foreach (var t in new[] { (int)Math.Floor(tSoll), (int)Math.Ceiling(tSoll) })
            {
                if ((d - t) % 2 != 0 || d <= t) continue;
                var f = Math.Abs(d - dSoll) + Math.Abs(t - tSoll);
                if (f < fehler) (fehler, knauf, spur) = (f, d, t);
            }
        var spurBreite = (int)Math.Round(plan.LeuchtSpurBreite * skala);
        var spurLinks = (int)Math.Round((breite - spurBreite) / 2.0);
        var knaufOben = (int)Math.Round((hoehe - knauf) / 2.0);
        var ueberstand = (knauf - spur) / 2;
        var zeichen = (int)Math.Floor(plan.LeuchtZeichenBreite * skala + 1e-9);
        if ((zeichen - knauf) % 2 != 0) zeichen--;
        return new Leuchtmass(skala, breite, hoehe, knauf, spur, ueberstand, spurLinks, knaufOben + ueberstand, spurBreite, knaufOben,
            Glasgeometrie.Haarlinie, zeichen);
    }
}
