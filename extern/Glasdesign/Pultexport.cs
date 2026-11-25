// Glasdesign - Maintainer: Sarevoc
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Glasdesign;

/// <summary>
/// Das Einstellpult mit den Werten der Bibliothek: Vorlage tools\einstellpult\einstellpult.vorlage.html, darin an der Stelle
/// <see cref="Platzhalter"/> das <see cref="Glanzprofil"/> und alle eingebauten Farbstellungen (Pinsel im Format von
/// <see cref="Farbexport"/>). Daraus entstehen export\einstellpult-widget.html (Bruchstück für die Live-Anzeige im Chat) und
/// export\einstellpult.html (eigene Seite für den Browser). Beide werden erzeugt, nie von Hand gepflegt; ein Test vergleicht
/// sie mit Vorlage und Bibliothek. Die Zeichnung im Pult rechnet wie <see cref="Glanzbild"/>, <see cref="Glaskoerper"/>,
/// <see cref="Schalterbahn"/> und <see cref="Leuchtschalter"/>: Wer dort die Rechnung ändert, ändert die Vorlage mit.
/// </summary>
public static class Pultexport
{
    /// <summary>Die Stelle in der Vorlage, an der die Daten als JSON eingesetzt werden.</summary>
    public const string Platzhalter = "__GLAS_DATEN__";

    /// <summary>Fassung, Glanzwerte (das <see cref="Glanzprofil"/> der Farbstellung Standard) und die Pinsel jeder Farbstellung, kompakt.</summary>
    public static string Daten()
    {
        using var puffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(puffer))
        {
            w.WriteStartObject();
            w.WriteString("fassung", Fassung());
            // Die Regler des Pults stehen auf dem Profil der Farbstellung Standard (Weinrot trägt seins in den Farbstellungen).
            var profil = Thema.Laden("Standard")[Schluessel.Glanzprofil] as Glanzprofil ?? Glanzprofil.Standard;
            w.WriteStartObject("glanzwerte");
            foreach (var (name, zahl) in profil.Werte()) w.WriteNumber(name, zahl);
            w.WriteEndObject();
            w.WriteStartObject("farbstellungen");
            foreach (var name in Thema.Eingebaut)
            {
                using var doc = JsonDocument.Parse(Farbexport.Json(Thema.Laden(name)));
                w.WritePropertyName(name);
                doc.RootElement.GetProperty("werte").WriteTo(w);
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(puffer.ToArray());
    }

    /// <summary>Das Bruchstück für die Live-Anzeige im Chat: die Vorlage mit eingesetzten Daten, Zeilenenden \n.</summary>
    public static string Widget(string vorlage)
    {
        ArgumentNullException.ThrowIfNull(vorlage);
        if (!vorlage.Contains(Platzhalter, StringComparison.Ordinal)) throw new ArgumentException("Vorlage ohne " + Platzhalter, nameof(vorlage));
        return vorlage.ReplaceLineEndings("\n").Replace(Platzhalter, Daten(), StringComparison.Ordinal);
    }

    /// <summary>Die eigene Seite für den Browser: dunkler Rahmen mit den Farbwerten, die im Chat der Gastgeber liefert.</summary>
    public static string Seite(string vorlage) => (Kopf.Replace("{fassung}", Fassung(), StringComparison.Ordinal) + Widget(vorlage) + Fuss).ReplaceLineEndings("\n");

    private static string Fassung() => Thema.Fassung;

    private const string Kopf = """
        <!doctype html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Glas-Einstellpult</title>
        <style>
        :root{color-scheme:dark;--text-primary:#e4ecee;--text-secondary:#9aa4a8;--text-accent:#8fe3ea;--text-danger:#ff8a80;--border:rgba(255,255,255,.16);--font-sans:"Segoe UI",system-ui,sans-serif;--font-mono:Consolas,ui-monospace,monospace;--radius:8px}
        body{margin:0;background:#1f2326;color:var(--text-primary);font-family:var(--font-sans)}
        main{max-width:720px;margin:0 auto;padding:16px}
        h1{font-size:18px;font-weight:500;margin:0 0 4px}
        .sr-only{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}
        button,select,textarea{font:inherit;color:inherit;background:#2b2f33;border:1px solid var(--border);border-radius:var(--radius)}
        button{padding:6px 12px;cursor:pointer}
        button:hover{background:#353b40}
        select{padding:3px 6px}
        textarea{padding:8px}
        input[type=range]{accent-color:#8fe3ea}
        </style>
        </head>
        <body>
        <main>
        <h1>Glas-Einstellpult</h1>
        <p style="color:var(--text-secondary);margin:0 0 8px">Glasdesign {fassung}. Werte mit den Reglern einstellen, dann "Kopieren" und den Werteblock an den Glasdesign-Chat geben.</p>

        """;

    private const string Fuss = """

        </main>
        </body>
        </html>

        """;
}
