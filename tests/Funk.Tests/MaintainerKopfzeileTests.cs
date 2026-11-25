// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Funk.Tests;

/// <summary>
/// A58 und A59 (Sarevoc, 01.10.2026): In jeder eigenen Quelldatei steht, dass Sarevoc Maintainer ist, im Kommentarstil der Sprache,
/// ohne Copyright- und ohne Lizenzzeile (die Lizenz ist noch nicht entschieden) und ohne bürgerlichen Namen. Nicht darunter fallen
/// Übertragungen fremden Codes (sie tragen den Vermerk ihrer Rechteinhaber), Testdaten, Bauordner und die gebauten Bibliotheken.
/// Der Test meldet jede eigene Datei, in der die Zeile fehlt oder an der falschen Stelle steht.
/// </summary>
internal static class MaintainerKopfzeileTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

    internal const string Kopf = "JG TacRelay - Maintainer: Sarevoc";

    /// <summary>
    /// Übertragungen fremden Codes in eigenen Quelldateien (THIRD-PARTY-NOTICES Nr. 11 und 12). Sie bekommen keine Zeile, weil dort
    /// der Vermerk der Rechteinhaber an erster Stelle steht und der Maintainer des Projekts nichts über sie sagt.
    /// </summary>
    private static readonly string[] Uebertragungen =
    [
        "src/Funk.Verwaltung/QrCode.cs",
        "src/Funk.Klang/Verarbeitung/WeichesClipping.cs",
    ];

    /// <summary>Eigene Quellen je Ordner und Dateiendung. Fremdes (native\quellen, native\bau, native\out) und .cmd (Paketdateien) fehlen mit Absicht.</summary>
    private static readonly (string Ordner, string[] Endungen)[] Bereiche =
    [
        ("src", [".cs", ".xaml"]),
        ("tests", [".cs", ".xaml", ".ps1"]),
        ("tools", [".ps1", ".py"]),
        (Path.Combine("native", "eigen"), [".c", ".h", ".cpp", ".rs", ".ps1"]),
        (Path.Combine("native", "skripte"), [".ps1"]),
        ("konfig", [".sh", ".service"]),
        ("installer", [".iss"]),
    ];

    private static string Relativ(string d) => Path.GetRelativePath(EinrichtungTests.Repo(), d).Replace('\\', '/');

    private static bool InBauordner(string d) =>
        Regex.IsMatch(d, @"[\\/](bin|obj)[\\/]", RegexOptions.None, Frist);

    /// <summary>Die Zeile im Kommentarstil der Sprache; null, wenn für die Endung keine vorgesehen ist.</summary>
    internal static string? ZeileFuer(string datei) => Path.GetExtension(datei).ToLowerInvariant() switch
    {
        ".cs" or ".c" or ".h" or ".cpp" or ".rs" => "// " + Kopf,
        ".xaml" => "<!-- " + Kopf + " -->",
        ".ps1" or ".py" or ".sh" or ".service" => "# " + Kopf,
        ".iss" => "; " + Kopf,
        _ => null,
    };

    internal static IEnumerable<string> EigeneQuellen()
    {
        foreach (var (ordner, endungen) in Bereiche)
        {
            var wurzel = Path.Combine(EinrichtungTests.Repo(), ordner);
            if (!Directory.Exists(wurzel)) continue;
            foreach (var d in Directory.EnumerateFiles(wurzel, "*", SearchOption.AllDirectories))
            {
                if (InBauordner(d) || !endungen.Contains(Path.GetExtension(d).ToLowerInvariant())) continue;
                if (Uebertragungen.Contains(Relativ(d), StringComparer.Ordinal)) continue;
                yield return d;
            }
        }
    }

    /// <summary>Die ersten Zeilen ohne Byte-Reihenfolge-Marke; die Marke bleibt in jeder Datei, wo sie war.</summary>
    private static List<string> Anfang(string datei, int zahl)
    {
        using var leser = new StreamReader(datei, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var zeilen = new List<string>();
        while (zeilen.Count < zahl && leser.ReadLine() is { } z) zeilen.Add(z);
        return zeilen;
    }

    /// <summary>Erste Zeile mit Pflichtform: Shebang, Kodierungszeile in Python, XML-Deklaration. Die Kopfzeile folgt dann in Zeile 2.</summary>
    private static bool HatPflichtform(string datei, string erste) =>
        erste.StartsWith("#!", StringComparison.Ordinal)
        || erste.StartsWith("<?xml", StringComparison.Ordinal)
        || (datei.EndsWith(".py", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(erste, @"coding[:=]", RegexOptions.None, Frist));

    [Test]
    public static void Kopfzeile_StehtInJederEigenenQuelldatei()
    {
        var fehlen = new List<string>();
        var zaehler = 0;
        foreach (var d in EigeneQuellen())
        {
            zaehler++;
            var zeile = ZeileFuer(d)!;
            var anfang = Anfang(d, 3);
            var erwartetIn = anfang.Count > 0 && HatPflichtform(d, anfang[0]) ? 1 : 0;
            if (anfang.Count <= erwartetIn || !string.Equals(anfang[erwartetIn], zeile, StringComparison.Ordinal))
            {
                fehlen.Add(Relativ(d));
                continue;
            }
            // PowerShell erkennt einen Hilfeblock (<# .SYNOPSIS ... #>) nicht mehr, wenn eine Zeilenkommentar-Zeile unmittelbar davor
            // steht (gemessen am 02.10.2026 mit Get-Help an opus.ps1): Zwischen Kopfzeile und <# gehört eine Leerzeile.
            if (d.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) && anfang.Count > erwartetIn + 1
                && anfang[erwartetIn + 1].StartsWith("<#", StringComparison.Ordinal))
                fehlen.Add(Relativ(d) + " (Leerzeile vor dem Hilfeblock fehlt)");
        }
        Pruefe.Wahr(zaehler > 500, "die Suche findet die eigenen Quelldateien (" + zaehler + ")");
        Pruefe.Gleich("", string.Join("\n", fehlen), "Quelldateien ohne Maintainer-Kopfzeile");
    }

    [Test]
    public static void Kopfzeile_NurEineZeile_OhneCopyrightUndLizenz()
    {
        var falsch = new List<string>();
        foreach (var d in EigeneQuellen())
        {
            var anfang = Anfang(d, 6);
            var treffer = anfang.Count(z => z.Contains("Maintainer: Sarevoc", StringComparison.Ordinal));
            if (treffer > 1) falsch.Add(Relativ(d) + ": die Zeile steht mehrfach");
            // Direkt neben der Kopfzeile darf nichts von Copyright, Lizenz oder Rechten stehen, das sie ergänzen soll.
            var nach = anfang.FindIndex(z => z.Contains("Maintainer: Sarevoc", StringComparison.Ordinal));
            if (nach >= 0 && Regex.IsMatch(anfang[nach], @"copyright|\(c\)|©|lizenz|licen[sc]e|all rights", RegexOptions.IgnoreCase, Frist))
                falsch.Add(Relativ(d) + ": Rechtsbehauptung in der Kopfzeile");
        }
        Pruefe.Gleich("", string.Join("\n", falsch), "Kopfzeilen");
    }

    [Test]
    public static void Uebertragungen_SindAusgenommen_UndTragenDenVermerkIhrerRechteinhaber()
    {
        var notices = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "THIRD-PARTY-NOTICES.txt"));
        foreach (var u in Uebertragungen)
        {
            var pfad = Path.Combine(EinrichtungTests.Repo(), u.Replace('/', Path.DirectorySeparatorChar));
            Pruefe.Wahr(File.Exists(pfad), u + " gibt es");
            var text = File.ReadAllText(pfad);
            Pruefe.Wahr(text.Contains("Copyright (c)", StringComparison.Ordinal), u + " trägt den fremden Copyright-Vermerk, sonst gehörte sie nicht auf die Liste");
            Pruefe.Wahr(notices.Contains(u.Replace('/', '\\'), StringComparison.Ordinal) || notices.Contains(Path.GetFileName(u), StringComparison.Ordinal),
                u + " steht in THIRD-PARTY-NOTICES");
            Pruefe.Falsch(Anfang(pfad, 2).Any(z => z.Contains("Maintainer: Sarevoc", StringComparison.Ordinal)), u + " bekommt keine Maintainer-Zeile");
        }
    }

    /// <summary>
    /// Feste Kopien eigener Bibliotheken aus anderen Projekten (seit 0.6.0, Stufe 2: extern\Glasdesign). Sie bleiben Byte für Byte
    /// wie bei der Übernahme (HERKUNFT.md) und tragen deshalb ihre eigene Kopfzeile, nicht die von JG TacRelay; der Ordner extern
    /// gehört nicht zu den eigenen Quellen (<see cref="Bereiche"/>).
    /// </summary>
    private static readonly (string Ordner, string Kopf)[] FesteKopien = [("extern/Glasdesign", "Glasdesign - Maintainer: Sarevoc")];

    [Test]
    public static void FesteKopien_BehaltenIhreEigeneKopfzeile()
    {
        var eigene = EigeneQuellen().Select(Relativ).ToList();
        foreach (var (ordner, kopf) in FesteKopien)
        {
            Pruefe.Falsch(eigene.Any(d => d.StartsWith(ordner + "/", StringComparison.Ordinal)), ordner + " zählt nicht zu den eigenen Quellen");
            var dateien = Directory.EnumerateFiles(Path.Combine(EinrichtungTests.Repo(), ordner.Replace('/', Path.DirectorySeparatorChar)), "*", SearchOption.AllDirectories)
                .Where(d => !InBauordner(d) && Path.GetExtension(d) is ".cs" or ".xaml").ToList();
            Pruefe.Wahr(dateien.Count >= 10, ordner + ": Quellen gefunden (" + dateien.Count + ")");
            foreach (var d in dateien)
            {
                var erste = Anfang(d, 1).FirstOrDefault() ?? "";
                Pruefe.Wahr(erste.Contains(kopf, StringComparison.Ordinal) && !erste.Contains(Kopf, StringComparison.Ordinal), Relativ(d) + ": eigene Kopfzeile der Kopie");
            }
        }
    }

    /// <summary>Company, Authors und Copyright der Programme, Herausgeber im Setup: Sarevoc als Maintainer, ohne Jahr und ohne Umfang.</summary>
    [Test]
    public static void Metadaten_NennenSarevocAlsMaintainer_OhneRechtsbehauptung()
    {
        var repo = EinrichtungTests.Repo();
        var props = XDocument.Load(Path.Combine(repo, "Directory.Build.props")).Descendants("PropertyGroup").First();
        string? Wert(string name) => props.Element(name)?.Value;
        Pruefe.Gleich("Sarevoc", Wert("Company"), "Company");
        Pruefe.Gleich("Sarevoc", Wert("Authors"), "Authors");
        Pruefe.Gleich("Maintainer: Sarevoc", Wert("Copyright"), "Copyright ohne Jahr und ohne Umfang");

        var iss = File.ReadAllText(Path.Combine(repo, "installer", "TacRelay.iss"));
        string? Anweisung(string name)
        {
            var m = Regex.Match(iss, @"^" + name + @"=(.*?)\s*$", RegexOptions.Multiline, Frist);
            return m.Success ? m.Groups[1].Value : null;
        }
        Pruefe.Wahr(Regex.IsMatch(iss, @"^#define Maintainer\s+""Sarevoc""\s*$", RegexOptions.Multiline, Frist), "Setup: #define Maintainer \"Sarevoc\"");
        Pruefe.Gleich("{#Maintainer}", Anweisung("AppPublisher"), "Herausgeber im Setup");
        Pruefe.Gleich("Maintainer: {#Maintainer}", Anweisung("AppCopyright"), "AppCopyright im Setup");
        Pruefe.Gleich("{#Maintainer}", Anweisung("VersionInfoCompany"), "Firma in den Dateieigenschaften des Setups");
        Pruefe.Gleich("Maintainer: {#Maintainer}", Anweisung("VersionInfoCopyright"), "Copyright in den Dateieigenschaften des Setups");

        var notices = File.ReadAllText(Path.Combine(repo, "THIRD-PARTY-NOTICES.txt"));
        Pruefe.Wahr(notices.Contains("Maintainer: Sarevoc", StringComparison.Ordinal), "THIRD-PARTY-NOTICES nennt den Maintainer");
        // Dass der eigene Code keiner Gilde mehr zugeschrieben ist, prüft seit dem Umbau der Namenswächter (NamenswaechterTests).
    }

    /// <summary>A59: Der bürgerliche Name steht nirgends, nur wenn es rechtlich nötig wäre; bis dahin ist das Pseudonym der Name.</summary>
    [Test]
    public static void BuergerlicherName_StehtNirgends()
    {
        var gesucht = new[] { "Per" + "zl" };
        var funde = new List<string>();
        var repo = EinrichtungTests.Repo();
        var dateien = new[] { "src", "tests", "tools", "installer", "konfig", "docs", Path.Combine("native", "eigen") }
            .SelectMany(o => Directory.EnumerateFiles(Path.Combine(repo, o), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(repo, "*", SearchOption.TopDirectoryOnly));
        foreach (var d in dateien)
        {
            var rel = Relativ(d);
            if (InBauordner(d)) continue;
            var endung = Path.GetExtension(d).ToLowerInvariant();
            if (endung is not (".cs" or ".xaml" or ".ps1" or ".py" or ".sh" or ".service" or ".iss" or ".md" or ".txt" or ".json" or ".props" or ".csproj" or ".cmd" or ".c" or ".h" or ".cpp" or ".rs" or ".toml"))
                continue;
            var text = File.ReadAllText(d);
            foreach (var g in gesucht)
                if (text.Contains(g, StringComparison.OrdinalIgnoreCase)) funde.Add(rel);
        }
        Pruefe.Gleich("", string.Join(", ", funde.Distinct()), "bürgerlicher Name im Repository");
    }
}
