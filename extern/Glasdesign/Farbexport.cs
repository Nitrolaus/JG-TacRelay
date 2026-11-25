// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Farbwerte einer Farbstellung als neutrale Dateien für andere Anwendungen (Foundry-Module, Webseiten): JSON und CSS-
/// Variablen. Erzeugt aus der XAML-Farbstellung, also aus derselben Quelle; die Dateien in export\ schreibt
/// tools\musterblatt (Modus farben), und ein Test prüft, dass sie zur XAML passen.
/// Farben stehen als #rrggbb oder, mit Deckung, #rrggbbaa (CSS-Reihenfolge: Alpha zuletzt, anders als in XAML).
/// </summary>
public static class Farbexport
{
    /// <summary>Dateiname für eine Farbstellung, etwa farben-standard.json.</summary>
    public static string Dateiname(string name, string endung) =>
        "farben-" + name.ToLowerInvariant() + "." + endung;

    /// <summary>JSON mit Name und allen Werten in der Reihenfolge von <see cref="Schluessel.Alle"/>.</summary>
    public static string Json(ResourceDictionary farbstellung)
    {
        ArgumentNullException.ThrowIfNull(farbstellung);
        var puffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(puffer, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("name", Name(farbstellung));
            w.WriteString("erzeugt", "Glasdesign, aus der XAML-Farbstellung; nicht von Hand ändern");
            w.WriteString("farbformat", "#rrggbb oder #rrggbbaa (Alpha zuletzt)");
            w.WriteStartObject("werte");
            foreach (var k in Werte())
            {
                w.WritePropertyName(k);
                Schreiben(w, farbstellung[k]);
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(puffer.ToArray()).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>CSS mit einer Variable je Schlüssel (Glas.Koerper.Ueber wird --glas-koerper-ueber) in :root.</summary>
    public static string Css(ResourceDictionary farbstellung)
    {
        ArgumentNullException.ThrowIfNull(farbstellung);
        var name = Name(farbstellung);
        var sb = new StringBuilder();
        sb.Append("/* Glasdesign - Maintainer: Sarevoc */\n");
        sb.Append("/* Farbstellung ").Append(name).Append(", erzeugt aus der XAML-Farbstellung; nicht von Hand ändern. */\n");
        sb.Append(":root {\n");
        foreach (var k in Werte())
        {
            sb.Append("  --").Append(Variable(k)).Append(": ").Append(CssWert(farbstellung[k])).Append(";\n");
            // Glanzprofil und Bauplan haben keinen CSS-Wert; ihre Zahlen stehen einzeln darunter.
            if (Wertesatz.Von(farbstellung[k]) is { } zahlen)
                foreach (var (wert, zahl) in zahlen)
                    sb.Append("  --").Append(Variable(k)).Append('-').Append(wert.ToLowerInvariant()).Append(": ").Append(Zahl(zahl)).Append(";\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>CSS-Name eines Schlüssels: klein, Punkte als Bindestriche.</summary>
    public static string Variable(string schluessel)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        return schluessel.ToLowerInvariant().Replace('.', '-');
    }

    private static IEnumerable<string> Werte() => Schluessel.Alle.Where(k => k != Schluessel.Name);

    private static string Name(ResourceDictionary d) => d[Schluessel.Name] as string ?? "";

    private static string Hex(Color c) =>
        c.A == 255
            ? string.Create(CultureInfo.InvariantCulture, $"#{c.R:x2}{c.G:x2}{c.B:x2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{c.R:x2}{c.G:x2}{c.B:x2}{c.A:x2}");

    private static double Winkel(LinearGradientBrush b)
    {
        var dx = b.EndPoint.X - b.StartPoint.X;
        var dy = b.EndPoint.Y - b.StartPoint.Y;
        var grad = Math.Atan2(dx, -dy) * 180 / Math.PI;
        return Math.Round((grad + 360) % 360, 2);
    }

    private static string Zahl(double wert) => wert.ToString("0.####", CultureInfo.InvariantCulture);

    private static void Schreiben(Utf8JsonWriter w, object? wert)
    {
        w.WriteStartObject();
        switch (wert)
        {
            case Color c:
                w.WriteString("art", "farbe");
                w.WriteString("farbe", Hex(c));
                break;
            case SolidColorBrush s:
                w.WriteString("art", "farbe");
                w.WriteString("farbe", Hex(s.Color));
                if (s.Opacity < 1) w.WriteNumber("deckung", s.Opacity);
                break;
            case LinearGradientBrush l:
                w.WriteString("art", "linear");
                w.WriteNumber("winkel", Winkel(l));
                Stopps(w, l);
                break;
            case RadialGradientBrush r:
                w.WriteString("art", "radial");
                Stopps(w, r);
                break;
            case double zahl:
                w.WriteString("art", "zahl");
                w.WriteNumber("wert", zahl);
                break;
            case Thickness t:
                w.WriteString("art", "abstand");
                w.WriteNumber("links", t.Left);
                w.WriteNumber("oben", t.Top);
                w.WriteNumber("rechts", t.Right);
                w.WriteNumber("unten", t.Bottom);
                break;
            case Glanzprofil or Bauplan or Farbglasprofil or Mattprofil or Stilprofil:
                w.WriteString("art", wert switch { Bauplan => "bauplan", Farbglasprofil => "farbglasprofil", Mattprofil => "mattprofil", Stilprofil => "stilprofil", _ => "glanzprofil" });
                w.WriteStartObject("werte");
                foreach (var (name, zahl) in Wertesatz.Von(wert)!) w.WriteNumber(name, zahl);
                w.WriteEndObject();
                break;
            default:
                w.WriteString("art", "unbekannt");
                break;
        }
        w.WriteEndObject();
    }

    private static void Stopps(Utf8JsonWriter w, GradientBrush g)
    {
        w.WriteStartArray("stopps");
        foreach (var s in g.GradientStops.OrderBy(s => s.Offset))
        {
            w.WriteStartObject();
            w.WriteString("farbe", Hex(s.Color));
            w.WriteNumber("position", Math.Round(s.Offset, 4));
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static string StoppListe(GradientBrush g) =>
        string.Join(", ", g.GradientStops.OrderBy(s => s.Offset).Select(s => Hex(s.Color) + " " + Zahl(s.Offset * 100) + "%"));

    private static string CssWert(object? wert) => wert switch
    {
        Color c => Hex(c),
        SolidColorBrush s => Hex(s.Color),
        LinearGradientBrush l => "linear-gradient(" + Zahl(Winkel(l)) + "deg, " + StoppListe(l) + ")",
        RadialGradientBrush r => "radial-gradient(ellipse at center, " + StoppListe(r) + ")",
        double zahl => Zahl(zahl) + "px",
        Thickness t => Zahl(t.Top) + "px " + Zahl(t.Right) + "px " + Zahl(t.Bottom) + "px " + Zahl(t.Left) + "px",
        _ => "initial",
    };
}
