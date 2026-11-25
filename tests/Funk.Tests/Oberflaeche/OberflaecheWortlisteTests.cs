// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Wächter für die Wörter der Oberfläche (Sarevoc, 02.10.2026, Q13 und Stufe 3): Was ein Mitglied oder der Admin liest, spricht von
/// <b>Kanal</b>, nie von "Netz" (auch nicht Sendenetz, Meldenetz, Einheitsnetz), und nennt keine Anforderungskürzel wie "(A9)" oder
/// "A23". Die feste Wortliste: Sprachaktivierung, Sprechtaste, Kanal, Feld, Rolle, Platz, Brett, Einsatzleitung; Ch. steht am Kanal.
/// Geprüft werden die sichtbaren Texte von Client und Verwaltungsteil: in XAML Text, Content, ToolTip, Header, Title, die Hilfetexte
/// (<c>gd:Hilfe.Text</c>, <c>gd:Hilfe.Gesperrt</c>), <c>AutomationProperties.Name</c>, <c>StringFormat</c> und Textinhalte; in C#
/// jede Zeichenkette (Kommentare nicht, die Ausdrücke einer interpolierten Zeichenkette als Code). Bezeichner, Bindungspfade und
/// Schlüssel in Kleinbuchstaben ("netze" in der Einstellungsdatei) sind keine Texte. "Netzwerk" meint die Verbindung und bleibt.
/// Die Ausnahmeliste ist leer.
/// </summary>
internal static class OberflaecheWortlisteTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

    /// <summary>"Netz" und seine Zusammensetzungen (Sendenetz, Netze, Orga-Netz), nicht aber "Netzwerk".</summary>
    private static readonly Regex NetzWort = new(@"netz(?!werk)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Frist);

    /// <summary>"(A9)", "(A23)" und ein allein stehendes "A9" (Anforderungskürzel).</summary>
    private static readonly Regex Kuerzel = new(@"\(A\d{1,3}\)|(?<![\w-])A\d{1,3}(?![\w-])", RegexOptions.CultureInvariant, Frist);

    /// <summary>Wörter, die die feste Wortliste ersetzt: Preset (Rolle), Push-to-Talk und PTT (Sprechtaste).</summary>
    private static readonly Regex Fremdwort = new(@"\bPreset|Push-to-Talk|\bPTT\b|Sendetaste", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Frist);

    /// <summary>Was an einem Text zu beanstanden ist; leer, wenn nichts.</summary>
    internal static List<string> Beanstandungen(string text)
    {
        var funde = new List<string>();
        if (text.Length == 0) return funde;
        foreach (Match m in NetzWort.Matches(text)) funde.Add("Netz: " + m.Value);
        foreach (Match m in Kuerzel.Matches(text)) funde.Add("Kürzel: " + m.Value);
        foreach (Match m in Fremdwort.Matches(text)) funde.Add("Wort: " + m.Value);
        return funde;
    }

    private static IEnumerable<string> Dateien(string projekt, string muster) =>
        Directory.EnumerateFiles(Path.Combine(EinrichtungTests.Repo(), "src", projekt), muster, SearchOption.AllDirectories)
            .Where(d => !Regex.IsMatch(d, @"\\(bin|obj)\\", RegexOptions.None, Frist));

    private static string Relativ(string d) => Path.GetRelativePath(EinrichtungTests.Repo(), d);

    // ------------------------------------------------------------------ XAML

    private static readonly HashSet<string> TextAttribute = new(StringComparer.Ordinal)
    {
        "Text", "Content", "ToolTip", "Header", "Title", "Hilfe.Text", "Hilfe.Gesperrt", "Hilfe.Taste", "AutomationProperties.Name", "AutomationProperties.HelpText",
    };

    /// <summary>
    /// Die sichtbaren Texte einer XAML-Datei. Ein Wert, der mit <c>{</c> beginnt (außer <c>{}</c>), ist eine Bindung oder ein Verweis:
    /// Dort zählt nur sein <c>StringFormat</c>. Kommentare liest <see cref="XDocument"/> nicht als Elemente.
    /// </summary>
    internal static List<string> XamlTexte(string xaml)
    {
        var texte = new List<string>();
        var dok = XDocument.Parse(xaml);
        foreach (var e in dok.Descendants())
        {
            foreach (var a in e.Attributes())
            {
                if (!TextAttribute.Contains(a.Name.LocalName)) continue;
                var wert = a.Value;
                foreach (Match f in Regex.Matches(wert, @"StringFormat\s*=\s*(?:'([^']*)'|([^,}]*))", RegexOptions.None, Frist))
                    texte.Add(AusKlammernOhne(f.Groups[1].Success ? f.Groups[1].Value : f.Groups[2].Value));
                if (wert.StartsWith('{') && !wert.StartsWith("{}", StringComparison.Ordinal)) continue;
                texte.Add(AusKlammernOhne(wert));
            }
            foreach (var n in e.Nodes().OfType<XText>())
            {
                var t = n.Value.Trim();
                if (t.Length > 0) texte.Add(t);
            }
        }
        return texte;
    }

    /// <summary>Das XAML-Maskierungszeichen <c>{}</c> am Anfang entfällt; <c>{0}</c> bleibt harmlos.</summary>
    private static string AusKlammernOhne(string wert) => wert.StartsWith("{}", StringComparison.Ordinal) ? wert[2..] : wert;

    // ------------------------------------------------------------------ C#

    /// <summary>
    /// Alle Zeichenketten eines C#-Quelltexts mit ihrer Zeile (1-basiert), ohne Kommentare und Zeichenliterale. Bei einer
    /// interpolierten Zeichenkette gilt nur der feste Text; ihre Ausdrücke werden selbst als Code gelesen.
    /// </summary>
    internal static List<(int Zeile, string Text)> Zeichenketten(string code)
    {
        var ziel = new List<(int, string)>();
        var zeilenanfang = new List<int> { 0 };
        for (var i = 0; i < code.Length; i++)
            if (code[i] == '\n') zeilenanfang.Add(i + 1);
        Lesen(code, 0, code.Length, zeilenanfang, ziel);
        return ziel;
    }

    private static int ZeileVon(List<int> anfang, int index)
    {
        var l = anfang.BinarySearch(index);
        return (l >= 0 ? l : ~l - 1) + 1;
    }

    private static void Lesen(string c, int von, int bis, List<int> zeilen, List<(int, string)> ziel)
    {
        var i = von;
        while (i < bis)
        {
            var z = c[i];
            if (z == '/' && i + 1 < bis && c[i + 1] == '/')
            {
                while (i < bis && c[i] != '\n') i++;
                continue;
            }
            if (z == '/' && i + 1 < bis && c[i + 1] == '*')
            {
                var ende = c.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = ende < 0 || ende >= bis ? bis : ende + 2;
                continue;
            }
            if (z == '\'')
            {
                i++;
                if (i < bis && c[i] == '\\') i++;
                i++;
                while (i < bis && c[i] != '\'') i++;
                i++;
                continue;
            }
            // Präfix einer Zeichenkette: $, @ in beliebiger Folge, dann " (auch ein Rohtext mit drei oder mehr ")
            var p = i;
            var dollar = 0;
            var klammeraffe = false;
            while (p < bis && (c[p] == '$' || c[p] == '@'))
            {
                if (c[p] == '$') dollar++;
                else klammeraffe = true;
                p++;
            }
            if (p >= bis || c[p] != '"' || (p > i && i > 0 && (char.IsLetterOrDigit(c[i - 1]) || c[i - 1] == '_')))
            {
                i++;
                continue;
            }
            var start = i;
            var quotes = 0;
            while (p + quotes < bis && c[p + quotes] == '"') quotes++;
            var text = new StringBuilder();
            if (quotes >= 3)
            {
                var abschluss = new string('"', quotes);
                var ende = c.IndexOf(abschluss, p + quotes, StringComparison.Ordinal);
                if (ende < 0 || ende >= bis) ende = bis;
                var inhalt = c.Substring(p + quotes, ende - (p + quotes));
                ziel.Add((ZeileVon(zeilen, start), dollar == 0 ? inhalt : Regex.Replace(inhalt, @"\{[^{}]*\}", " ")));
                i = Math.Min(bis, ende + quotes);
                continue;
            }
            i = p + 1;
            while (i < bis)
            {
                var k = c[i];
                if (k == '"')
                {
                    if (klammeraffe && i + 1 < bis && c[i + 1] == '"')
                    {
                        text.Append('"');
                        i += 2;
                        continue;
                    }
                    i++;
                    break;
                }
                if (k == '\\' && !klammeraffe)
                {
                    text.Append(i + 1 < bis ? c[i + 1] : ' ');
                    i += 2;
                    continue;
                }
                if (dollar > 0 && k == '{')
                {
                    if (i + 1 < bis && c[i + 1] == '{')
                    {
                        text.Append('{');
                        i += 2;
                        continue;
                    }
                    var ausdruckEnde = AusdruckEnde(c, i + 1, bis);
                    Lesen(c, i + 1, ausdruckEnde, zeilen, ziel);
                    text.Append(' ');
                    i = Math.Min(bis, ausdruckEnde + 1);
                    continue;
                }
                if (dollar > 0 && k == '}' && i + 1 < bis && c[i + 1] == '}')
                {
                    text.Append('}');
                    i += 2;
                    continue;
                }
                text.Append(k);
                i++;
            }
            ziel.Add((ZeileVon(zeilen, start), text.ToString()));
        }
    }

    /// <summary>Das Ende (die schließende Klammer) eines Ausdrucks in einer interpolierten Zeichenkette; Zeichenketten darin zählen nicht mit.</summary>
    private static int AusdruckEnde(string c, int von, int bis)
    {
        var tiefe = 1;
        for (var i = von; i < bis; i++)
        {
            switch (c[i])
            {
                case '"':
                    i++;
                    while (i < bis && c[i] != '"')
                    {
                        if (c[i] == '\\') i++;
                        i++;
                    }
                    break;
                case '{':
                    tiefe++;
                    break;
                case '}':
                    if (--tiefe == 0) return i;
                    break;
            }
        }
        return bis;
    }

    /// <summary>Eine Zeichenkette ist ein Schlüssel oder Bezeichner, kein Text: ein Wort aus Kleinbuchstaben und Zeichen ohne Leerzeichen.</summary>
    private static bool IstSchluessel(string text) => Regex.IsMatch(text, @"^[a-zäöüß][A-Za-z0-9_.:/#-]*$", RegexOptions.CultureInvariant, Frist);

    /// <summary>Alle Beanstandungen der Quellen von Client und Verwaltungsteil, je als "Datei:Zeile: Fund in Text".</summary>
    private static List<string> Befund(Func<string, bool> gesucht)
    {
        var funde = new List<string>();
        foreach (var projekt in new[] { "Funk.Client", "Funk.Verwaltung" })
        {
            foreach (var d in Dateien(projekt, "*.xaml"))
                foreach (var t in XamlTexte(File.ReadAllText(d)))
                    foreach (var b in Beanstandungen(t).Where(gesucht))
                        funde.Add(Relativ(d) + ": " + b + " in \"" + Kurz(t) + "\"");
            foreach (var d in Dateien(projekt, "*.cs"))
                foreach (var (zeile, t) in Zeichenketten(File.ReadAllText(d)))
                {
                    if (IstSchluessel(t)) continue;
                    foreach (var b in Beanstandungen(t).Where(gesucht))
                        funde.Add(Relativ(d) + ":" + zeile + ": " + b + " in \"" + Kurz(t) + "\"");
                }
        }
        return funde;
    }

    private static string Kurz(string t)
    {
        var einzeilig = t.ReplaceLineEndings(" ");
        return einzeilig.Length <= 90 ? einzeilig : einzeilig[..90] + "...";
    }

    // ------------------------------------------------------------------ Tests

    [Test]
    public static void Kanal_StattNetz_InAllenTextenVonClientUndVerwaltung()
    {
        var funde = Befund(b => b.StartsWith("Netz:", StringComparison.Ordinal));
        Pruefe.Gleich("", string.Join("\n", funde), "Netz in Texten");
    }

    [Test]
    public static void Kuerzel_NichtInNutzertexten()
    {
        var funde = Befund(b => b.StartsWith("Kürzel:", StringComparison.Ordinal));
        Pruefe.Gleich("", string.Join("\n", funde), "Anforderungskürzel in Texten");
    }

    [Test]
    public static void Fremdwoerter_NichtInNutzertexten()
    {
        var funde = Befund(b => b.StartsWith("Wort:", StringComparison.Ordinal));
        Pruefe.Gleich("", string.Join("\n", funde), "Preset, Push-to-Talk, PTT");
    }

    /// <summary>
    /// Mutationsprobe: Der Wächter schlägt an, wenn ein Text "Netz" oder ein Kürzel trägt, in XAML (Attribut, StringFormat, Textinhalt)
    /// und in C# (einfach, interpoliert, wörtlich, Rohtext), und bleibt ruhig bei Kanal, Bindungspfaden, Kommentaren, Schlüsseln und
    /// "Netzwerk".
    /// </summary>
    [Test]
    public static void Waechter_FindetNetzUndKuerzel_Mutationsprobe()
    {
        var xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:gd="urn:gd" xmlns:x="urn:x">
              <!-- Netz im Kommentar (A9) zählt nicht -->
              <TextBlock Text="Meine Netze" />
              <Button Content="Weiter" gd:Hilfe.Text="Dieses Netz hören (A23)." />
              <TextBlock Text="{Binding Netz, StringFormat='Netze: {0}'}" />
              <TextBlock Text="{Binding Netz}" x:Name="Netz" />
              <TextBlock>Sendenetz</TextBlock>
              <TextBlock Text="Netzwerk getrennt" />
            </Grid>
            """;
        var gefunden = XamlTexte(xaml).SelectMany(Beanstandungen).ToList();
        Pruefe.Wahr(gefunden.Contains("Netz: Netz") && gefunden.Count(g => g.StartsWith("Netz:", StringComparison.Ordinal)) == 4,
            "XAML: Text, Hilfe.Text, StringFormat, Textinhalt (Netze, Netz, Netze, Sendenetz): " + string.Join("|", gefunden));
        Pruefe.Wahr(gefunden.Contains("Kürzel: (A23)") && gefunden.Count(g => g.StartsWith("Kürzel:", StringComparison.Ordinal)) == 1, "XAML: (A23) im Hilfetext, nicht im Kommentar");
        Pruefe.Gleich(0, XamlTexte("""<Grid><TextBlock Text="Meine Kanäle" gd:Hilfe.Text="Kanal hören" xmlns:gd="urn:gd" /></Grid>""").SelectMany(Beanstandungen).Count(), "sauberes XAML");

        var code = "var a = \"Dein Netz\"; // Netz (A9)\n/* Netz */ var b = $\"Kein {(x ? \"Sendenetz\" : \"Kanal\")} da, {Netz.Name} {{Netz}}\";\n"
            + "var c = @\"Meldenetz \"\"A9\"\" \";\nvar d = \"\"\"\n Raw Netz (A23)\n \"\"\"; var e = \"netze\"; var f = \"Netzwerkfehler\"; var g = 'N';";
        var teile = Zeichenketten(code);
        var kaputt = teile.Where(t => !IstSchluessel(t.Text)).SelectMany(t => Beanstandungen(t.Text).Select(b => t.Zeile + ":" + b)).ToList();
        Pruefe.Wahr(kaputt.Contains("1:Netz: Netz"), "C#: einfache Zeichenkette in Zeile 1: " + string.Join("|", kaputt));
        Pruefe.Wahr(kaputt.Contains("2:Netz: netz") && kaputt.Contains("2:Netz: Netz"), "C#: Zeichenkette im Ausdruck und {{Netz}} als Text: " + string.Join("|", kaputt));
        Pruefe.Wahr(kaputt.Contains("2:Netz: Netz") && !kaputt.Any(k => k.Contains("Name", StringComparison.Ordinal)), "C#: der Bindungspfad {Netz.Name} ist Code");
        Pruefe.Wahr(kaputt.Contains("3:Netz: netz") && kaputt.Contains("3:Kürzel: A9"), "C#: wörtliche Zeichenkette mit Kürzel: " + string.Join("|", kaputt));
        Pruefe.Wahr(kaputt.Any(k => k.StartsWith("4:", StringComparison.Ordinal) && k.Contains("Netz", StringComparison.Ordinal)) && kaputt.Contains("4:Kürzel: (A23)"),
            "C#: Rohtext: " + string.Join("|", kaputt));
        Pruefe.Wahr(!kaputt.Any(k => k.Contains("Netzwerk", StringComparison.Ordinal)) && teile.All(t => t.Text != "N"), "C#: Netzwerk, Zeichenliteral und Kommentar zählen nicht");
        Pruefe.Wahr(IstSchluessel("netze") && IstSchluessel("sendenetz") && !IstSchluessel("kein Meldenetz") && !IstSchluessel("Netze"), "Schlüssel gegen Text");
        Pruefe.Wahr(Beanstandungen("Preset wählen").Count == 1 && Beanstandungen("Push-to-Talk").Count == 1 && Beanstandungen("Sprechtaste, Sprachaktivierung, Kanal, Feld, Rolle, Platz, Brett, Einsatzleitung, Ch. 3").Count == 0,
            "Fremdwörter gegen die feste Wortliste");
    }
}
