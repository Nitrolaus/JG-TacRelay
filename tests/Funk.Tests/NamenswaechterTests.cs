// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using System.Text.RegularExpressions;

namespace Funk.Tests;

/// <summary>
/// Umbau JG TacRelay (Sarevoc, 18./19.10.2026: alles mit dem alten Gildennamen, der alten Kanalvorlage und den beiden Personen raus "bis in die letzte
/// Zeile", Q22): Kein Datei- oder Ordnername und keine Text- oder Codedatei des Repositorys enthält eines der vier Wörter, groß oder klein, auch
/// nicht als Teil eines Worts (wie im alten Programmnamen). Geprüft wird alles außer den Bauordnern (bin, obj, native\bau), den fremden Quellen
/// und Bauergebnissen (native\quellen, native\out) und Binärdateien (eine Null im Anfang der Datei). Die Wörter stehen hier zerlegt, damit dieser
/// Test sich nicht selbst findet. Was noch darf, steht in <see cref="Ausnahmen"/>, je mit Grund; eine Ausnahme, die nichts mehr findet, meldet
/// der Test, damit die Liste kurz bleibt. Eine Ausnahme gilt nur für den Inhalt; Datei- und Ordnernamen prüft der Test überall. Hier aufgegangen
/// sind die beiden früheren Einzelwächter (THIRD-PARTY-NOTICES und die Farbstellung der Gilde in der Glasdesign-Kopie).
/// </summary>
internal static class NamenswaechterTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(5);

    /// <summary>Die vier Wörter, zerlegt geschrieben.</summary>
    private static readonly string[] Woerter = ["Ker" + "beros", "Ker" + "Com", "Ni" + "ko", "Ara" + "sil"];

    private static readonly Regex Muster = new(string.Join("|", Woerter.Select(Regex.Escape)), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Frist);

    /// <summary>Ordner, die nie geprüft werden: Bauordner und fremde Quellen und Bauergebnisse.</summary>
    private static readonly string[] Nie = ["native/quellen/", "native/bau/", "native/out/"];

    /// <summary>Endungen von Dateien, die sicher keine Texte sind (Bilder, Klang, Programme, Archive, Schriften).</summary>
    private static readonly HashSet<string> Binaer = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".ico", ".jpg", ".jpeg", ".gif", ".bmp", ".wav", ".dll", ".exe", ".pdb", ".zip", ".7z", ".pdf", ".ttf", ".otf", ".woff", ".woff2", ".lib", ".obj",
    };

    /// <summary>
    /// Was die Wörter noch enthalten darf, und warum (Pfad relativ zum Repository, mit "/" am Ende für einen ganzen Ordner). Seit Umbau-Schritt 5
    /// leer: Doku und PROJEKT.md sind neu gefasst, und die Glasdesign-Kopie (0fd0663) kommt ohne den alten Namen aus. Ihre einzige Zeile damit, der
    /// Eintrag der Tabelle früherer Namen in Thema.cs, fehlt dort als bewusste Abweichung, vermerkt in extern\Glasdesign\HERKUNFT.md.
    /// </summary>
    internal static readonly (string Pfad, string Grund)[] Ausnahmen = [];

    private static string Relativ(string pfad) => Path.GetRelativePath(EinrichtungTests.Repo(), pfad).Replace('\\', '/');

    private static bool Ausgenommen(string relativ, IEnumerable<(string Pfad, string Grund)> liste) =>
        liste.Any(a => a.Pfad.EndsWith('/') ? relativ.StartsWith(a.Pfad, StringComparison.Ordinal) : relativ == a.Pfad);

    private static bool NieGeprueft(string relativ) =>
        Nie.Any(n => relativ.StartsWith(n, StringComparison.Ordinal))
        || relativ.Split('/').Any(teil => teil is "bin" or "obj" or ".git" or ".vs");

    /// <summary>Alle Dateien des Repositorys, die geprüft werden (ohne Bauordner und Fremdes), relativ mit "/".</summary>
    internal static IEnumerable<string> Dateien() =>
        Directory.EnumerateFiles(EinrichtungTests.Repo(), "*", SearchOption.AllDirectories).Select(Relativ).Where(r => !NieGeprueft(r)).Order(StringComparer.Ordinal);

    /// <summary>Ein Fund im Datei- oder Ordnernamen, sonst <c>null</c>.</summary>
    private static string? ImNamen(string relativ) => Muster.IsMatch(relativ) ? relativ + ": im Datei- oder Ordnernamen" : null;

    /// <summary>Die Funde im Inhalt einer Datei, je "Pfad:Zeile: Text" (gekürzt); eine Binärdatei hat keine.</summary>
    private static IEnumerable<string> ImInhalt(string relativ)
    {
        if (Binaer.Contains(Path.GetExtension(relativ))) yield break;
        var bytes = File.ReadAllBytes(Path.Combine(EinrichtungTests.Repo(), relativ));
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8192)).Contains((byte)0)) yield break;
        var zeilen = Encoding.UTF8.GetString(bytes).Split('\n');
        for (var i = 0; i < zeilen.Length; i++)
        {
            if (!Muster.IsMatch(zeilen[i])) continue;
            var z = zeilen[i].Trim();
            yield return relativ + ":" + (i + 1) + ": " + (z.Length > 120 ? z[..120] + "..." : z);
        }
    }

    [Test]
    public static void AlteNamen_StehenNirgends_AusserInDenAusnahmen()
    {
        var funde = new List<string>();
        var genutzt = new HashSet<string>(StringComparer.Ordinal);
        var gezaehlt = 0;
        foreach (var r in Dateien())
        {
            gezaehlt++;
            if (ImNamen(r) is { } name) funde.Add(name);
            var hier = ImInhalt(r).ToList();
            if (hier.Count == 0) continue;
            var ausnahme = Ausnahmen.FirstOrDefault(a => a.Pfad.EndsWith('/') ? r.StartsWith(a.Pfad, StringComparison.Ordinal) : r == a.Pfad);
            if (ausnahme.Pfad is not null) genutzt.Add(ausnahme.Pfad);
            else funde.AddRange(hier);
        }
        Pruefe.Wahr(gezaehlt > 1000, "das ganze Repository geprüft: " + gezaehlt + " Dateien");
        Pruefe.Gleich("", string.Join("\n", funde.Take(40)) + (funde.Count > 40 ? "\n... und " + (funde.Count - 40) + " weitere" : ""), funde.Count + " Funde");
        var leer = Ausnahmen.Where(a => !genutzt.Contains(a.Pfad)).Select(a => a.Pfad).ToList();
        Pruefe.Gleich("", string.Join(", ", leer), "Ausnahmen, die nichts mehr finden, gehören aus der Liste");
        Pruefe.Wahr(Ausnahmen.All(a => a.Grund.Length > 40 && a.Grund.EndsWith('.')), "jede Ausnahme mit Grund");
    }

    /// <summary>Der Wächter selbst: Er findet die Wörter in jeder Schreibweise, auch als Teil eines Worts, und lässt Fremdes und Bauordner aus.</summary>
    [Test]
    public static void Waechter_FindetAlleSchreibweisen_UndLaesstFremdesAus()
    {
        foreach (var w in Woerter)
        {
            foreach (var probe in new[] { w, w.ToUpperInvariant(), w.ToLowerInvariant(), w + "Funk", "Alt" + w.ToLowerInvariant() + "21", "x " + w + "-Aufteilung" })
                Pruefe.Wahr(Muster.IsMatch(probe), "gefunden: " + probe);
        }
        foreach (var probe in new[] { "Kerbe", "Komma", "Nicht", "Arsenal", "Silber", "JG TacRelay" })
            Pruefe.Falsch(Muster.IsMatch(probe), "nicht gefunden: " + probe);
        Pruefe.Wahr(NieGeprueft("native/quellen/opus-1.6.1/README") && NieGeprueft("src/Funk.Client/bin/Debug/x.dll") && NieGeprueft("native/out/lizenzen/x.txt"),
            "Fremdes und Bauordner bleiben draußen");
        Pruefe.Falsch(NieGeprueft("src/Funk.Client/App.xaml.cs") || NieGeprueft("tests/Funk.Tests/Daten/zustand-0.2.0/lage.json"), "eigene Dateien und Testdaten werden geprüft");
        // Die Liste ist leer; geprüft wird der Abgleich an einer Probeliste mit einem Ordner und einer Datei.
        (string Pfad, string Grund)[] liste = [("extern/Glasdesign/", "Probe Ordner."), ("PROJEKT.md", "Probe Datei.")];
        Pruefe.Wahr(Ausgenommen("extern/Glasdesign/Glas.xaml", liste) && Ausgenommen("PROJEKT.md", liste) && !Ausgenommen("CODEMAP.md", liste)
            && !Ausgenommen("docs/PROJEKT.md", liste) && !Ausgenommen("extern/Glasdesign", liste), "nur die Ausnahmen der Liste");
        // Der Name zählt auch in einer Ausnahme: Eine Farbstellung mit dem alten Namen in der Glasdesign-Kopie fiele auf (früher in OberflaecheGlasTests).
        Pruefe.Wahr(ImNamen("extern/Glasdesign/Farbstellungen/" + Woerter[0] + ".xaml") is not null, "Dateinamen zählen auch dort, wo der Inhalt ausgenommen wäre");
    }
}
