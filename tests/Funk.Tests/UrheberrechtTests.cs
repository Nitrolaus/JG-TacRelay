// JG TacRelay - Maintainer: Sarevoc
using System.Text.RegularExpressions;

namespace Funk.Tests;

/// <summary>
/// Urheberrecht (C:\Claude\CLAUDE.md: jeder Fremdbestandteil steht mit Lizenz in THIRD-PARTY-NOTICES, sonst gilt er als
/// ungeklärt). Befund der Prüfung der Pakete 0.4.0 (mittel): <c>WeichesClipping</c> in Funk.Klang.dll ist eine Übertragung von
/// <c>opus_pcm_soft_clip_impl</c> aus libopus 1.6.1 (src/opus.c), Struktur, Sonderfall und Konstante wie dort. Die Notices
/// nannten nur opus.dll und zählten Funk.* ganz zum eigenen Code. Hier: die Übertragung mit Herkunft, Copyright aus dem Kopf
/// von src/opus.c und Lizenztext; und jede Quelldatei, die sich selbst als Übertragung oder Nachbau ausweist, steht mit
/// ihrem Namen in THIRD-PARTY-NOTICES.
/// </summary>
internal static class UrheberrechtTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

    private static string Notices() => File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "THIRD-PARTY-NOTICES.txt"));

    /// <summary>
    /// Die Copyright-Zeilen aus dem Kopf von native\quellen\opus-1.6.1\src\opus.c (Quellarchiv, SHA-256 in
    /// native\BAUBERICHT.md). Der Ordner native\quellen liegt nicht im Git; deshalb stehen sie hier als Text.
    /// </summary>
    private static readonly string[] OpusKopf =
    [
        "Copyright (c) 2011 Xiph.Org Foundation, Skype Limited",
        "Copyright (c) 2024 Arm Limited",
        "Written by Jean-Marc Valin and Koen Vos",
    ];

    /// <summary>
    /// Gegenprüfung Klangstufe B (mittel): deepfilternet.dll enthält neben den 79 Crates die statisch gelinkte Rust-Standardbibliothek,
    /// und die Crate realfft bringt keine Lizenzdatei mit. Für die Weitergabe braucht jede gelinkte Crate einen Lizenztext im Ordner
    /// lizenzen\native\deepfilternet\crates\ (MIT verlangt den Vermerk in jeder Kopie), dazu die Lizenzen der Standardbibliothek
    /// (COPYRIGHT-library.html der Rust-Fassung) und ihre Nennung in THIRD-PARTY-NOTICES.
    /// </summary>
    [Test]
    public static void DeepFilterNet_JedeGelinkteCrateUndDieStandardbibliothekMitLizenztext()
    {
        var ordner = new[]
        {
            Path.Combine(EinrichtungTests.Repo(), "native", "out", "lizenzen", "deepfilternet"),
            @"C:\Claude\work\JG-TacRelay\native\out\lizenzen\deepfilternet",
        }.FirstOrDefault(Directory.Exists);
        Pruefe.Wahr(ordner is not null, @"native\out\lizenzen\deepfilternet vorhanden (native\skripte\deepfilternet.ps1)");
        var zeilen = File.ReadAllLines(Path.Combine(ordner!, "CRATES.txt")).Skip(4).Where(z => z.Trim().Length > 0).ToList();
        Pruefe.Gleich(79, zeilen.Count, "gelinkte Crates in CRATES.txt");
        foreach (var z in zeilen)
        {
            var teile = z.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var crate = Path.Combine(ordner!, "crates", teile[0] + "-" + teile[1]);
            Pruefe.Wahr(Directory.Exists(crate) && Directory.GetFiles(crate).Length > 0, $"Lizenztext für {teile[0]} {teile[1]}");
        }
        var std = Path.Combine(ordner!, "rust-std", "COPYRIGHT-library.html");
        Pruefe.Wahr(File.Exists(std) && File.ReadAllText(std).Contains("Rust Standard Library", StringComparison.Ordinal),
            @"rust-std\COPYRIGHT-library.html mit den Lizenzen der Standardbibliothek");
        var notices = Notices();
        Pruefe.Wahr(notices.Contains("Rust-Standardbibliothek", StringComparison.Ordinal) && notices.Contains("COPYRIGHT-library.html", StringComparison.Ordinal),
            "THIRD-PARTY-NOTICES nennt die Rust-Standardbibliothek in deepfilternet.dll");
    }

    [Test]
    public static void WeichesClipping_UebertragungAusLibopusMitHerkunftUndLizenz()
    {
        var notices = Notices();
        foreach (var teil in new[] { "opus_pcm_soft_clip", "libopus 1.6.1", "src/opus.c", @"src\Funk.Klang\Verarbeitung\WeichesClipping.cs", "Funk.Klang.dll" })
            Pruefe.Wahr(notices.Contains(teil, StringComparison.Ordinal), "THIRD-PARTY-NOTICES nennt " + teil);
        foreach (var zeile in OpusKopf)
            Pruefe.Wahr(notices.Contains(zeile, StringComparison.Ordinal), "THIRD-PARTY-NOTICES mit der Zeile aus dem Kopf von opus.c: " + zeile);
        // Der Lizenztext aus dem Dateikopf (zwei Bedingungen und der Haftungsausschluss), nicht nur ein Verweis.
        Pruefe.Wahr(notices.Contains("Redistributions in binary form must reproduce the above copyright", StringComparison.Ordinal)
            && notices.Contains("SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.", StringComparison.Ordinal), "Lizenztext des Dateikopfs");

        // Der Satz über den eigenen Code nimmt die Übertragungen aus.
        var eigen = Regex.Match(notices, @"Eigener Code \(Funk\.\*[^\n]*\n[^\n]*", RegexOptions.None, Frist);
        Pruefe.Wahr(eigen.Success && eigen.Value.Contains("außer", StringComparison.Ordinal), "'Eigener Code (Funk.*' nennt die Ausnahme: " + eigen.Value);

        var quelle = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Klang", "Verarbeitung", "WeichesClipping.cs"));
        Pruefe.Wahr(quelle.Contains("Skype Limited", StringComparison.Ordinal) && quelle.Contains("THIRD-PARTY-NOTICES", StringComparison.Ordinal),
            "WeichesClipping.cs nennt die Rechteinhaber und den Eintrag");
    }

    /// <summary>
    /// Jede Quelldatei unter src, die sich selbst als Übertragung oder Nachbau einer fremden Vorlage ausweist oder einen
    /// fremden Copyright-Vermerk trägt, steht mit ihrem Dateinamen in THIRD-PARTY-NOTICES. Die Suche muss mindestens die
    /// beiden bekannten Fälle finden (QR-Code nach Project Nayuki, weiches Clipping nach libopus), sonst prüft sie nichts.
    /// </summary>
    [Test]
    public static void JedeUebertragungInSrc_StehtInDenNotices()
    {
        var notices = Notices();
        var src = Path.Combine(EinrichtungTests.Repo(), "src");
        var merkmal = new Regex(@"in C# nachgebaut|übertragen nach C#|Übertragung (der|von|aus) |wie im Original|Copyright \(c\)", RegexOptions.None, Frist);
        var gefunden = new List<string>();
        foreach (var datei in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (datei.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || datei.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (!merkmal.IsMatch(File.ReadAllText(datei))) continue;
            var relativ = Path.GetRelativePath(EinrichtungTests.Repo(), datei);
            gefunden.Add(relativ);
            Pruefe.Wahr(notices.Contains(relativ, StringComparison.Ordinal), relativ + " weist sich als Übertragung aus und steht in THIRD-PARTY-NOTICES");
        }
        Pruefe.Wahr(gefunden.Any(d => d.EndsWith("QrCode.cs", StringComparison.Ordinal)) && gefunden.Any(d => d.EndsWith("WeichesClipping.cs", StringComparison.Ordinal)),
            "die Suche findet die bekannten Übertragungen: " + string.Join(", ", gefunden));
    }
}
