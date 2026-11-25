// JG TacRelay - Maintainer: Sarevoc
using System.Text.RegularExpressions;

namespace Funk.Tests;

/// <summary>
/// A65 (Sarevoc, 01.10.2026): Beispiele in Programm, Setup, Hilfe und Anleitungen heißen Max Mustermann ("Mustermann/Max"), nicht wie
/// Sarevoc; "Sarevoc" steht nur, wo der Maintainer gemeint ist, und Programmausgaben sagen "der Eigentümer" statt Sarevoc. Rudi steht nirgends
/// mehr, auch nicht in den Testdaten. Das Handbuch (docs\handbuch) gehört seit der Fassung 0.5.0 dazu: Rudi kommt dort nirgends vor, und
/// "Sarevoc" steht nicht als Beispieleingabe (Anmeldename, Option, Beispielliste); als Person (Maintainer, Eigentümer) darf der Name
/// im Fließtext stehen.
/// </summary>
internal static class BeispielnamenTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

    /// <summary>Textdateien eines Ordners, ohne Bauordner und ohne das Handbuch.</summary>
    private static IEnumerable<string> Dateien(string ordner, params string[] muster) =>
        muster.SelectMany(m => Directory.EnumerateFiles(Path.Combine(EinrichtungTests.Repo(), ordner), m, SearchOption.AllDirectories))
            .Where(d => !Regex.IsMatch(d, @"\\(bin|obj|handbuch)\\", RegexOptions.None, Frist));

    /// <summary>Die Kapiteldateien des Handbuchs (Markdown, direkt in docs\handbuch; sie hängen an keinem anderen Ordner).</summary>
    private static IEnumerable<string> Handbuch() =>
        Directory.EnumerateFiles(Path.Combine(EinrichtungTests.Repo(), "docs", "handbuch"), "*.md", SearchOption.TopDirectoryOnly);

    private static string Relativ(string d) => Path.GetRelativePath(EinrichtungTests.Repo(), d);

    [Test]
    public static void Rudi_Nirgends()
    {
        var funde = new List<string>();
        foreach (var ordner in new[] { "src", "tests", "installer", "konfig", "tools", "docs" })
        {
            foreach (var d in Dateien(ordner, "*.cs", "*.xaml", "*.iss", "*.md", "*.txt", "*.json", "*.sh", "*.ps1", "*.cmd", "*.py"))
            {
                if (Path.GetFileName(d) == nameof(BeispielnamenTests) + ".cs") continue;
                var zeilen = File.ReadAllLines(d);
                for (var i = 0; i < zeilen.Length; i++)
                    if (zeilen[i].Contains("rudi", StringComparison.OrdinalIgnoreCase)) funde.Add($"{Relativ(d)}:{i + 1}");
            }
        }
        // Das Handbuch: Es war bis 0.4.1 voller Beispiele mit Sarevoc/Rudi (Anf. A65).
        foreach (var d in Handbuch())
        {
            var zeilen = File.ReadAllLines(d);
            for (var i = 0; i < zeilen.Length; i++)
                if (zeilen[i].Contains("rudi", StringComparison.OrdinalIgnoreCase)) funde.Add($"{Relativ(d)}:{i + 1}");
        }
        Pruefe.Gleich("", string.Join(", ", funde), "Rudi");
    }

    [Test]
    public static void Sarevoc_NichtAlsBeispiel()
    {
        // Der Maintainer heißt Sarevoc (A59). Seit 0.13.1 (öffentliche Fassung) nennen auch Kommentare und Anleitungen ihn so, wo vorher
        // der Rufname stand: als Person, deren Entscheidung etwas war. Als Beispieleingabe darf er nirgends stehen: nicht als Anmeldename
        // ("Sarevoc/..."), nicht als Wert einer Option ("--name Sarevoc", "--namen \"Sarevoc,..."), nicht als Beispielname in
        // Anführungszeichen mit Zusatz ("Sarevoc Test").
        var beispiel = new Regex(@"Sarevoc/|/Sarevoc|--[a-z-]+ ""?Sarevoc|""Sarevoc[ ,]|Sarevoc,\w|Sarevoc Test", RegexOptions.None, Frist);
        var funde = new List<string>();
        foreach (var (ordner, muster) in new[]
                 {
                     ("src", new[] { "*.cs", "*.xaml" }), ("installer", ["*.iss"]), ("konfig", ["*"]), (Path.Combine("tools", "paket"), ["*"]),
                     ("docs", ["*.md"]),
                 })
        {
            foreach (var d in Dateien(ordner, muster))
            {
                var zeilen = File.ReadAllLines(d);
                for (var i = 0; i < zeilen.Length; i++)
                    if (beispiel.IsMatch(zeilen[i])) funde.Add($"{Relativ(d)}:{i + 1}: {zeilen[i].Trim()}");
            }
        }
        Pruefe.Gleich("", string.Join("\n", funde.Where(f => !f.Contains("Maintainer", StringComparison.Ordinal))), "Sarevoc als Beispiel");
        // Das Handbuch nennt Sarevoc als Person (Maintainer, Eigentümer, Heimserver) in fast jedem Kapitel, auch ohne das Wort Maintainer
        // in der Zeile; dort gilt dasselbe Muster.
        var imHandbuch = new List<string>();
        foreach (var d in Handbuch())
        {
            var zeilen = File.ReadAllLines(d);
            for (var i = 0; i < zeilen.Length; i++)
                if (beispiel.IsMatch(zeilen[i]) && !zeilen[i].Contains("Maintainer", StringComparison.Ordinal)) imHandbuch.Add($"{Relativ(d)}:{i + 1}: {zeilen[i].Trim()}");
        }
        Pruefe.Gleich("", string.Join("\n", imHandbuch), "Sarevoc als Beispiel im Handbuch");
        var setup = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "installer", "TacRelay.iss"));
        Pruefe.Wahr(setup.Contains("etwa Mustermann/Max", StringComparison.Ordinal), "Seite Deine Namen: etwa Mustermann/Max");
    }

    /// <summary>Zeichenketten in C# und Texte in XAML nennen Sarevoc nicht; Kommentare dürfen sagen, wessen Entscheidung etwas war.</summary>
    [Test]
    public static void Ausgaben_NennenDenEigentuemer_NichtJu()
    {
        var funde = new List<string>();
        var ju = new Regex(@"\bJus?\b", RegexOptions.None, Frist);
        foreach (var d in Dateien("src", "*.cs"))
        {
            var zeilen = File.ReadAllLines(d);
            for (var i = 0; i < zeilen.Length; i++)
            {
                var z = zeilen[i].TrimStart();
                if (z.StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (Match s in Regex.Matches(z, @"""(?:[^""\\]|\\.)*""", RegexOptions.None, Frist))
                    if (ju.IsMatch(s.Value)) funde.Add($"{Relativ(d)}:{i + 1}: {s.Value}");
            }
        }
        foreach (var d in Dateien("src", "*.xaml"))
        {
            var ohneKommentare = Regex.Replace(File.ReadAllText(d), "<!--.*?-->", "", RegexOptions.Singleline, Frist);
            foreach (Match a in Regex.Matches(ohneKommentare, @"(?:Text|ToolTip|Content|Header|AutomationProperties\.Name)=""([^""]*)""", RegexOptions.None, Frist))
                if (ju.IsMatch(a.Groups[1].Value)) funde.Add($"{Relativ(d)}: {a.Value}");
        }
        foreach (var d in Dateien("installer", "*.iss").Concat(Dateien("konfig", "*.sh")).Concat(Dateien(Path.Combine("tools", "paket"), "*")))
        {
            var zeilen = File.ReadAllLines(d);
            for (var i = 0; i < zeilen.Length; i++)
                if (ju.IsMatch(zeilen[i])) funde.Add($"{Relativ(d)}:{i + 1}: {zeilen[i].Trim()}");
        }
        // Ausnahme mit Absicht: Die Messung am echten Funkfeld gibt Sarevoc selbst frei, im Gespräch mit Claude (Werkzeug für die
        // Entwicklung, kein Text für Mitglieder).
        var rest = funde.Where(f => !f.Contains("FunkfeldMessauftrag.cs", StringComparison.Ordinal)).ToList();
        Pruefe.Gleich("", string.Join("\n", rest), "Sarevoc in Ausgaben");
    }
}
