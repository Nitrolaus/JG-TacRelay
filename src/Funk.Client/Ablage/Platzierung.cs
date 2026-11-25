// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Serialization;
using System.Windows;

namespace Funk.Client.Ablage;

/// <summary>Rechteck in Bildpunkten des virtuellen Bildschirms (physische Pixel, so wie Windows sie zählt).</summary>
public readonly record struct Pixelrechteck(int Links, int Oben, int Breite, int Hoehe)
{
    public int Rechts => Links + Breite;

    public int Unten => Oben + Hoehe;

    /// <summary>Fläche des Schnitts mit <paramref name="r"/>, 0 ohne Schnitt.</summary>
    public long Schnitt(Pixelrechteck r)
    {
        var b = Math.Min(Rechts, r.Rechts) - Math.Max(Links, r.Links);
        var h = Math.Min(Unten, r.Unten) - Math.Max(Oben, r.Oben);
        return b <= 0 || h <= 0 ? 0 : (long)b * h;
    }

    /// <summary>Der Schnitt mit <paramref name="r"/> als Rechteck (leer: Breite oder Höhe 0).</summary>
    public Pixelrechteck Schnittrechteck(Pixelrechteck r)
    {
        var l = Math.Max(Links, r.Links);
        var o = Math.Max(Oben, r.Oben);
        return new Pixelrechteck(l, o, Math.Max(0, Math.Min(Rechts, r.Rechts) - l), Math.Max(0, Math.Min(Unten, r.Unten) - o));
    }

    /// <summary>Kürzester Abstand zweier Rechtecke in Bildpunkten, 0 wenn sie sich berühren oder schneiden.</summary>
    public double Abstand(Pixelrechteck r)
    {
        var dx = Math.Max(0, Math.Max(r.Links - Rechts, Links - r.Rechts));
        var dy = Math.Max(0, Math.Max(r.Oben - Unten, Oben - r.Unten));
        return Math.Sqrt(((double)dx * dx) + ((double)dy * dy));
    }

    public bool Enthaelt(int x, int y) => x >= Links && x < Rechts && y >= Oben && y < Unten;
}

/// <summary>
/// Ein Monitor, wie Windows ihn meldet: ganzer Bereich und Arbeitsbereich (ohne Taskleiste) in Bildpunkten, dazu seine
/// Skala (DPI / 96; 1,75 bei 175 %).
/// </summary>
public sealed record Bildschirm(Pixelrechteck Bereich, Pixelrechteck Arbeit, double Skala, bool Haupt);

/// <summary>
/// Wohin ein Fenster kommt: die linke obere Ecke in Bildpunkten, die Größe in Einheiten, und auf welchen Monitor. In
/// Einheiten für WPF: Links / Skala des Fensters, Breite unverändert.
/// </summary>
public sealed record Platz(int Links, int Oben, double Breite, double Hoehe, Bildschirm Bildschirm, bool Maximiert = false)
{
    /// <summary>Das Fenster in Bildpunkten auf seinem Monitor.</summary>
    public Pixelrechteck Rahmen => new(Links, Oben, Platzierung.Pixel(Breite, Bildschirm.Skala), Platzierung.Pixel(Hoehe, Bildschirm.Skala));
}

/// <summary>
/// Wo ein Fenster steht, so wie du es hingeschoben und gezogen hast (A32, Sarevoc: "so wie sich jemand ein Fenster einstellt,
/// soll es sein, auch später"). Die linke obere Ecke zählt in Bildpunkten des virtuellen Bildschirms, die Größe in Einheiten
/// (1/96 Zoll).
/// <para>
/// Warum gemischt: Einheiten sind in WPF je Fenster auf dessen Monitor bezogen. Stehen zwei Monitore mit verschiedener
/// DPI nebeneinander, bezeichnet dieselbe Lage in Einheiten zwei verschiedene Stellen, je nachdem, auf welchem Monitor
/// das Fenster gerade ist; nur Bildpunkte sind über alle Monitore eindeutig. Die Größe dagegen gehört zum Inhalt, und der
/// ist in Einheiten gebaut: Wandert das Fenster auf einen Monitor mit anderer DPI, passt derselbe Inhalt in dieselbe Zahl
/// von Einheiten, in Bildpunkten gemerkt wäre er dort zu groß oder zu klein.
/// </para>
/// <para>
/// Ohne Größe (<see cref="MitGroesse"/> falsch) gilt die optimale Größe der Ansicht (Funkfeld) oder die Voreinstellung
/// des Fensters; so bleibt das Funkfeld "gerade so groß, dass alles angezeigt wird", bis du es selbst am Rand ziehst.
/// </para>
/// </summary>
public sealed record FensterOrt(int Links, int Oben, double? Breite = null, double? Hoehe = null, bool Maximiert = false)
{
    /// <summary>Weiter außen liegt kein Monitor (Windows erlaubt ±32767 Bildpunkte).</summary>
    public const int Grenze = 1_000_000;

    /// <summary>Eine eigene, von dir gezogene Größe ist gemerkt.</summary>
    [JsonIgnore]
    public bool MitGroesse => Breite is not null && Hoehe is not null;

    /// <summary><c>null</c>, wenn die Lage unbrauchbar ist; eine unbrauchbare Größe fällt weg, die Lage bleibt.</summary>
    public static FensterOrt? OderNull(FensterOrt? o)
    {
        if (o is null || Math.Abs((long)o.Links) > Grenze || Math.Abs((long)o.Oben) > Grenze) return null;
        static bool Gut(double? w) => w is { } x && double.IsFinite(x) && x >= 1 && x <= 100_000;
        if (o.Breite is null && o.Hoehe is null) return o;
        return Gut(o.Breite) && Gut(o.Hoehe) ? o : o with { Breite = null, Hoehe = null };
    }

    /// <summary>
    /// Übernimmt einen Stand aus der Zeit vor dem 30.09.2026 (Lage in Einheiten). WPF setzte diese Lage beim Start mit der
    /// Skala des Hauptmonitors (<paramref name="skala"/>) in Bildpunkte um; so kommt das Fenster dorthin, wo es zuletzt stand.
    /// </summary>
    public static FensterOrt? AusAltemStand(FensterStand? alt, double skala, bool mitGroesse)
    {
        if (FensterStand.OderNull(alt) is not { } s || !double.IsFinite(skala) || skala <= 0) return null;
        return OderNull(new FensterOrt(Platzierung.Pixel(s.Links, skala), Platzierung.Pixel(s.Oben, skala),
            mitGroesse ? s.Breite : null, mitGroesse ? s.Hoehe : null, s.Maximiert));
    }
}

/// <summary>
/// Gemerkte Größe des Hauptfensters für eine Größenklasse (Einheiten), ohne Lage: Beim Wechsel der Klasse bleibt die linke
/// obere Ecke, nur die Größe wechselt (<c>Fenster\Fenstergroessen.cs</c>).
/// </summary>
public sealed record Fenstergroesse(double Breite, double Hoehe)
{
    /// <summary><c>null</c>, wenn die Größe unbrauchbar ist (nicht endlich, unter 1 oder über 100000 Einheiten).</summary>
    public static Fenstergroesse? OderNull(Fenstergroesse? g) => g is not null && Gut(g.Breite) && Gut(g.Hoehe) ? g : null;

    private static bool Gut(double w) => double.IsFinite(w) && w >= 1 && w <= 100_000;
}

/// <summary>
/// Rechnet, wohin ein Fenster kommt und wie groß es wird, als reine Funktion über den Monitoren; ohne Fenster testbar.
/// Grundsätze (A32): Ein gemerkter Platz wird genau so wiederhergestellt, solange er auf einem vorhandenen Monitor greifbar
/// ist. Fehlt sein Monitor (abgesteckt), rückt das Fenster auf den nächsten; ist es größer als dessen Arbeitsbereich, wird
/// es begrenzt und ganz hineingeschoben. Die optimale Größe des Funkfelds geht auf ganze Bildpunkte und nie über den
/// Arbeitsbereich.
/// </summary>
public static class Platzierung
{
    /// <summary>So viel vom Fenster muss auf seinem Monitor sichtbar sein (Einheiten), damit man es noch greifen kann.</summary>
    public const double MindestSichtbar = 48;

    /// <summary>
    /// Obergrenze der optimalen Größe als Vielfaches der gemessenen Wunschgröße. Die Rundung auf ganze Bildpunkte gibt
    /// höchstens einen Bildpunkt dazu; die Grenze fängt ab, dass ein Fenster je größer wird als sein Inhalt.
    /// </summary>
    public const double HoechstensFaktor = 1.2;

    /// <summary>Einheiten in ganze Bildpunkte bei <paramref name="skala"/>.</summary>
    public static int Pixel(double einheiten, double skala)
    {
        var p = einheiten * skala;
        return double.IsFinite(p) ? (int)Math.Clamp(Math.Round(p, MidpointRounding.AwayFromZero), -FensterOrt.Grenze, FensterOrt.Grenze) : 0;
    }

    /// <summary>
    /// Optimale Größe für einen Inhalt mit der Wunschgröße <paramref name="wunsch"/> (Einheiten, frei gemessen) auf
    /// <paramref name="b"/>: aufgerundet auf ganze Bildpunkte, damit nichts abgeschnitten wird, höchstens
    /// <see cref="HoechstensFaktor"/> mal die Wunschgröße und höchstens der Arbeitsbereich.
    /// </summary>
    public static Size Optimal(Size wunsch, Bildschirm b)
    {
        ArgumentNullException.ThrowIfNull(b);
        var s = b.Skala;
        double Runden(double e, int arbeit)
        {
            if (!double.IsFinite(e) || e <= 0) return arbeit / s;
            // Erst runden: 776 * 1,75 ist in double nicht genau 1358, Ceiling machte sonst 1359 daraus.
            var auf = Math.Ceiling(Math.Round(e * s, 6));
            var grenze = Math.Floor(Math.Round(e * HoechstensFaktor * s, 6));
            return Math.Min(Math.Min(auf, grenze), arbeit) / s;
        }
        return new Size(Runden(wunsch.Width, b.Arbeit.Breite), Runden(wunsch.Height, b.Arbeit.Hoehe));
    }

    /// <summary>Größe innerhalb von <paramref name="mindest"/> und dem Arbeitsbereich von <paramref name="b"/>; der Arbeitsbereich gewinnt.</summary>
    public static Size Begrenzen(Size groesse, Bildschirm b, Size mindest)
    {
        ArgumentNullException.ThrowIfNull(b);
        double Eine(double w, double min, int arbeit)
        {
            var max = arbeit / b.Skala;
            var soll = double.IsFinite(w) ? Math.Max(w, min) : min;
            return Math.Min(soll, max);
        }
        return new Size(Eine(groesse.Width, mindest.Width, b.Arbeit.Breite), Eine(groesse.Height, mindest.Height, b.Arbeit.Hoehe));
    }

    /// <summary>Der Hauptmonitor, sonst der erste.</summary>
    public static Bildschirm Haupt(IReadOnlyList<Bildschirm> bildschirme)
    {
        ArgumentNullException.ThrowIfNull(bildschirme);
        if (bildschirme.Count == 0) throw new ArgumentException("Kein Monitor bekannt.", nameof(bildschirme));
        return bildschirme.FirstOrDefault(b => b.Haupt) ?? bildschirme[0];
    }

    /// <summary>Der Monitor an einem Punkt (Bildpunkte), sonst der nächste.</summary>
    public static Bildschirm Am(int x, int y, IReadOnlyList<Bildschirm> bildschirme)
    {
        ArgumentNullException.ThrowIfNull(bildschirme);
        if (bildschirme.Count == 0) throw new ArgumentException("Kein Monitor bekannt.", nameof(bildschirme));
        var punkt = new Pixelrechteck(x, y, 1, 1);
        return bildschirme.FirstOrDefault(b => b.Bereich.Enthaelt(x, y)) ?? bildschirme.MinBy(b => b.Bereich.Abstand(punkt))!;
    }

    /// <summary>
    /// Der Monitor, auf dem ein Fenster überwiegend steht, so wie Windows ihn bestimmt (MonitorFromWindow): der mit dem größten Stück von
    /// <paramref name="fenster"/> (Bildpunkte, mit der Skala, die das Fenster gerade hat). Nach ihm wählt Windows bei PerMonitorV2 auch die
    /// Skala des Fensters. Liegt es auf keinem, der unter der linken oberen Ecke oder der nächste (<see cref="Am"/>).
    /// </summary>
    public static Bildschirm Ueberwiegend(Pixelrechteck fenster, IReadOnlyList<Bildschirm> bildschirme)
    {
        ArgumentNullException.ThrowIfNull(bildschirme);
        if (bildschirme.Count == 0) throw new ArgumentException("Kein Monitor bekannt.", nameof(bildschirme));
        Bildschirm? bester = null;
        var groesster = 0L;
        foreach (var b in bildschirme)
        {
            var schnitt = fenster.Schnitt(b.Bereich);
            if (schnitt <= groesster) continue;
            (groesster, bester) = (schnitt, b);
        }
        return bester ?? Am(fenster.Links, fenster.Oben, bildschirme);
    }

    /// <summary>
    /// Ein Fenster ohne gemerkten Platz oben mittig auf <paramref name="b"/>, <paramref name="abstand"/> Einheiten unter dem
    /// oberen Rand des Arbeitsbereichs, in der optimalen Größe (Funkfeld beim ersten Start).
    /// </summary>
    public static Platz ObenMittig(Size wunsch, Bildschirm b, double abstand, Size mindest)
    {
        ArgumentNullException.ThrowIfNull(b);
        var groesse = Begrenzen(Optimal(wunsch, b), b, mindest);
        var breite = Pixel(groesse.Width, b.Skala);
        return Einpassen(b.Arbeit.Links + ((b.Arbeit.Breite - breite) / 2), b.Arbeit.Oben + Pixel(abstand, b.Skala), groesse, b, false);
    }

    /// <summary>Ein Fenster ohne gemerkten Platz mittig im Arbeitsbereich von <paramref name="b"/> (Hauptfenster beim ersten Start).</summary>
    public static Platz Mittig(Size groesse, Bildschirm b, Size mindest)
    {
        ArgumentNullException.ThrowIfNull(b);
        var g = Begrenzen(groesse, b, mindest);
        return Einpassen(b.Arbeit.Links + ((b.Arbeit.Breite - Pixel(g.Width, b.Skala)) / 2),
            b.Arbeit.Oben + ((b.Arbeit.Hoehe - Pixel(g.Height, b.Skala)) / 2), g, b, false);
    }

    /// <summary>
    /// Stellt <paramref name="ort"/> wieder her. Ohne gemerkte Größe gilt die optimale Größe zu <paramref name="wunsch"/> auf
    /// dem Zielmonitor. Ziel ist der Monitor unter der linken oberen Ecke, wo die Titelzeile beginnt; liegt sie auf keinem,
    /// der mit dem größten Stück des Fensters, und liegt es auf keinem (Monitor abgesteckt), der nächste. Genau so bleibt der
    /// Platz, wenn er dort greifbar ist (<see cref="MindestSichtbar"/>, obere Kante auf dem Monitor) und nicht begrenzt werden
    /// musste; sonst kommt das Fenster ganz in den Arbeitsbereich.
    /// </summary>
    public static Platz Wiederherstellen(FensterOrt ort, Size wunsch, IReadOnlyList<Bildschirm> bildschirme, Size mindest)
    {
        ArgumentNullException.ThrowIfNull(ort);
        ArgumentNullException.ThrowIfNull(bildschirme);
        if (bildschirme.Count == 0) throw new ArgumentException("Kein Monitor bekannt.", nameof(bildschirme));
        Size Groesse(Bildschirm b) => ort.MitGroesse ? new Size(ort.Breite!.Value, ort.Hoehe!.Value) : Optimal(wunsch, b);
        Pixelrechteck Rahmen(Bildschirm b, Size g) => new(ort.Links, ort.Oben, Pixel(g.Width, b.Skala), Pixel(g.Height, b.Skala));

        var ziel = bildschirme.FirstOrDefault(b => b.Bereich.Enthaelt(ort.Links, ort.Oben));
        var bester = 0L;
        foreach (var b in ziel is null ? bildschirme : [])
        {
            var schnitt = Rahmen(b, Groesse(b)).Schnitt(b.Bereich);
            if (schnitt <= bester) continue;
            bester = schnitt;
            ziel = b;
        }
        var aufEinem = ziel is not null;
        ziel ??= bildschirme.MinBy(b => Rahmen(b, Groesse(b)).Abstand(b.Bereich))!;
        var gewollt = Groesse(ziel);
        var soll = Begrenzen(gewollt, ziel, mindest);
        var begrenzt = Math.Abs(soll.Width - gewollt.Width) > 1e-6 || Math.Abs(soll.Height - gewollt.Height) > 1e-6;
        if (aufEinem && !begrenzt && Greifbar(Rahmen(ziel, soll), ziel)) return new Platz(ort.Links, ort.Oben, soll.Width, soll.Height, ziel, ort.Maximiert);
        return Einpassen(ort.Links, ort.Oben, soll, ziel, ort.Maximiert);
    }

    /// <summary>
    /// Greifbar: Vom Fenster sind auf dem Monitor mindestens <see cref="MindestSichtbar"/> Einheiten in Breite und Höhe zu
    /// sehen (oder das ganze Fenster, wenn es kleiner ist), und seine obere Kante mit der Titelzeile liegt auf dem Monitor.
    /// </summary>
    public static bool Greifbar(Pixelrechteck fenster, Bildschirm b)
    {
        ArgumentNullException.ThrowIfNull(b);
        var sichtbar = fenster.Schnittrechteck(b.Bereich);
        var min = Pixel(MindestSichtbar, b.Skala);
        return sichtbar.Breite >= Math.Min(min, fenster.Breite) && sichtbar.Hoehe >= Math.Min(min, fenster.Hoehe)
            && fenster.Oben >= b.Bereich.Oben && fenster.Oben <= b.Bereich.Unten - Math.Min(min, fenster.Hoehe);
    }

    /// <summary>Schiebt ein Fenster der Größe <paramref name="groesse"/> (Einheiten) so wenig wie möglich ganz in den Arbeitsbereich.</summary>
    public static Platz Einpassen(int links, int oben, Size groesse, Bildschirm b, bool maximiert)
    {
        ArgumentNullException.ThrowIfNull(b);
        var a = b.Arbeit;
        var breite = Pixel(groesse.Width, b.Skala);
        var hoehe = Pixel(groesse.Height, b.Skala);
        var x = Math.Clamp(links, a.Links, Math.Max(a.Links, a.Rechts - breite));
        var y = Math.Clamp(oben, a.Oben, Math.Max(a.Oben, a.Unten - hoehe));
        return new Platz(x, y, groesse.Width, groesse.Height, b, maximiert);
    }
}
