// JG TacRelay - Maintainer: Sarevoc
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Funk.Client;
using Funk.Klang.Maschine;

namespace Funk.Tests;

/// <summary>
/// Regeln für Test- und Werkzeugläufe (C:\Claude\CLAUDE.md), mechanisch geprüft statt per Vorsatz: Geräte, Eingabegeräte
/// und Fenster nur hinter <see cref="Spielschutz.RauchtestFreigegeben"/>, Testdaten nie im Temp-Verzeichnis des Benutzers,
/// dotnet nur mit der Umgebung aus tools\dotnet-umgebung.ps1, Netz und Programmstart nur mit ausdrücklichem Schalter.
/// Anlass: Am 29.09.2026 startete der WPF-Rauchtest unbemerkt das ganze Programm, während Star Citizen lief. Alle Prüfungen
/// hier lesen nur Quelltext oder rufen reine Funktionen; keine fasst ein Gerät oder ein Fenster an.
/// </summary>
internal static partial class SicherungTests
{
    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden.");
    }

    private static IEnumerable<string> Dateien(string ordner, string muster) =>
        Directory.EnumerateFiles(Path.Combine(Wurzel(), ordner), muster, SearchOption.AllDirectories)
            .Where(p => !p.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) && !p.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase));

    /// <summary>Was ein Gerät, ein Eingabegerät, ein Fenster oder einen Prozess anfasst.</summary>
    private static readonly (string Name, Regex Muster)[] Gefaehrlich =
    [
        ("Raw Input", RawInputMuster()),
        ("DirectInput", DirectInputMuster()),
        ("WASAPI", WasapiMuster()),
        ("sichtbares Fenster", FensterMuster()),
        ("Programmstart", ProzessMuster()),
        // 0.4.1: TPM und Schlüsselspeicher am echten PC (Vorprüfung, Admin-Schlüssel); Tests nehmen Attrappen.
        ("TPM am echten PC", EchterPcMuster()),
    ];

    [Test]
    public static void Quelltext_GeraeteUndFensterNurHinterFreigabe()
    {
        var befunde = new List<string>();
        foreach (var datei in Dateien(@"tests\Funk.Tests", "*.cs"))
        {
            // Diese Datei nennt die Muster selbst.
            if (Path.GetFileName(datei) == "SicherungTests.cs") continue;
            var text = File.ReadAllText(datei);
            // Je Test ein Block von [Test] bis zum nächsten; Hilfen vor dem ersten Test bilden einen eigenen Block.
            var grenzen = TestMarke().Matches(text).Select(m => m.Index).Prepend(0).Append(text.Length).ToList();
            for (var i = 0; i + 1 < grenzen.Count; i++)
            {
                var block = text[grenzen[i]..grenzen[i + 1]];
                var erste = ErsteGefahr(block);
                if (erste is null) continue;
                var schutz = block.IndexOf("Spielschutz.RauchtestFreigegeben(", StringComparison.Ordinal);
                if (schutz < 0 || schutz > erste.Value.Index)
                {
                    var zeile = text[..(grenzen[i] + erste.Value.Index)].Count(c => c == '\n') + 1;
                    befunde.Add($"{Path.GetFileName(datei)}:{zeile} {erste.Value.Name} ohne Spielschutz.RauchtestFreigegeben davor");
                }
            }
        }
        Pruefe.Gleich(0, befunde.Count, "Ungeschützte Stellen: " + string.Join(" | ", befunde));
    }

    private static (string Name, int Index)? ErsteGefahr(string block)
    {
        (string, int)? erste = null;
        foreach (var (name, muster) in Gefaehrlich)
        {
            var m = muster.Match(block);
            if (m.Success && (erste is null || m.Index < erste.Value.Item2)) erste = (name, m.Index);
        }
        // Die Klangmaschine ohne Attrappe öffnet WASAPI: bis zum Ende der Anweisung muss AudioSystem stehen.
        foreach (Match m in KlangMaschineMuster().Matches(block))
        {
            var ende = block.IndexOf(';', m.Index);
            var anweisung = ende < 0 ? block[m.Index..] : block[m.Index..ende];
            if (anweisung.Contains("AudioSystem", StringComparison.Ordinal)) continue;
            if (erste is null || m.Index < erste.Value.Item2) erste = ("KlangMaschine ohne AudioSystem", m.Index);
            break;
        }
        return erste;
    }

    [Test]
    public static void Quelltext_Selbstpruefung_ErkenntUngeschuetztUndGeschuetzt()
    {
        // Gegenprobe: Ohne diese Treffer bewiese "keine Befunde" oben nichts.
        Pruefe.Gleich("Raw Input", ErsteGefahr("using var t = new RawInputSprechtaste();")?.Name, "Raw Input erkannt");
        Pruefe.Gleich("DirectInput", ErsteGefahr("using var j = new DirectInputSprechtaste( );")?.Name, "DirectInput erkannt");
        Pruefe.Gleich(null, ErsteGefahr("new DirectInputSprechtaste(() => schicht, () => schicht)")?.Name, "DirectInput mit Attrappe ist erlaubt");
        Pruefe.Gleich("KlangMaschine ohne AudioSystem", ErsteGefahr("var m = new KlangMaschine(new KlangMaschinenOptionen { AusgabeStumm = true });")?.Name,
            "KlangMaschine ohne Attrappe erkannt");
        Pruefe.Gleich(null, ErsteGefahr("var m = new KlangMaschine(new KlangMaschinenOptionen\n{\n  AudioSystem = s,\n  EigeneThreads = false,\n});")?.Name,
            "KlangMaschine mit Attrappe ist erlaubt");
        Pruefe.Gleich("sichtbares Fenster", ErsteGefahr("fenster.Show();")?.Name, "Show erkannt");
        Pruefe.Gleich("Programmstart", ErsteGefahr("Process.Start(si);")?.Name, "Programmstart erkannt");
        Pruefe.Gleich("WASAPI", ErsteGefahr("using var s = new WasapiSystem();")?.Name, "WASAPI erkannt");
        Pruefe.Gleich("TPM am echten PC", ErsteGefahr("var p = new PcpTpmPruefung( );")?.Name, "PCP erkannt");
        Pruefe.Gleich("TPM am echten PC", ErsteGefahr("await Adminvoraussetzungen.AmPc().PruefenAsync();")?.Name, "echte Vorprüfung erkannt");
        Pruefe.Gleich("TPM am echten PC", ErsteGefahr("using var s = new TpmAdminSchluessel();")?.Name, "Admin-Schlüssel im TPM erkannt");
        Pruefe.Gleich(null, ErsteGefahr("Pruefe.Wahr(text.Contains(\"Adminvoraussetzungen.AmPc()\", StringComparison.Ordinal));")?.Name, "als Text im Quelltextvergleich erlaubt");
    }

    [Test]
    public static void Spielschutz_ErkenntSpielAmProzessnamen()
    {
        // Gegenprobe ohne Spiel: Der eigene Prozess gilt als Spiel, dann muss die Prüfung ihn finden.
        using var ich = System.Diagnostics.Process.GetCurrentProcess();
        var grund = Spielschutz.Grund([ich.ProcessName]);
        Pruefe.Wahr(grund is not null && grund.Contains(ich.ProcessName, StringComparison.Ordinal), "eigener Prozess als Spiel erkannt: " + grund);
        Pruefe.Wahr(Spielschutz.Spiele.Contains("StarCitizen"), "Star Citizen steht in der Liste");
        Console.WriteLine("  Spielschutz jetzt: " + (Spielschutz.Grund() ?? "kein Spiel, kein Vollbild"));
    }

    [Test]
    public static void Quelltext_KeinAusweichenInsTempDesBenutzers()
    {
        // Path.GetTempPath() nur unter Linux oder als Rückfall auf einem PC ohne C:\Claude; die Zeile nennt dann
        // OperatingSystem.IsWindows(). Alles andere legt Testdaten, Schlüssel oder Sockets ins Benutzerprofil.
        var befunde = new List<string>();
        foreach (var ordner in new[] { @"tests\Funk.Tests", @"tests\Funk.Testclient", @"tests\Funk.Testserver", @"src\Funk.Echo" })
        {
            foreach (var datei in Dateien(ordner, "*.cs"))
            {
                if (Path.GetFileName(datei) == "SicherungTests.cs") continue;
                var zeilen = File.ReadAllLines(datei);
                for (var i = 0; i < zeilen.Length; i++)
                {
                    if (zeilen[i].Contains("GetTempPath", StringComparison.Ordinal) && !zeilen[i].Contains("IsWindows", StringComparison.Ordinal))
                        befunde.Add($"{ordner}\\{Path.GetFileName(datei)}:{i + 1}");
                }
            }
        }
        Pruefe.Gleich(0, befunde.Count, "Temp-Verzeichnis ohne Plattformprüfung: " + string.Join(" | ", befunde));

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FUNK_TESTVERZEICHNIS")))
            Pruefe.Wahr(Testserver.Wurzel().StartsWith(@"C:\Claude\tmp\", StringComparison.OrdinalIgnoreCase), "Testverzeichnis in der Zone: " + Testserver.Wurzel());
        Pruefe.Wahr(Funk.Client.Ablage.EinstellungsSpeicher.StandardPfad().StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FUNK_TESTVERZEICHNIS")),
            "Client-Einstellungen im Testlauf nicht im Profil: " + Funk.Client.Ablage.EinstellungsSpeicher.StandardPfad());
    }

    [Test]
    public static void Werkzeuge_DotnetNurMitZonenUmgebung()
    {
        // Ohne tools\dotnet-umgebung.ps1 nimmt das Restore den NuGet-Ordner im Profil, die CLI sendet Telemetrie, und
        // MSBuild, NuGet und der Compiler legen ihre Zwischenstände im Temp-Verzeichnis des Benutzers ab.
        var befunde = new List<string>();
        var geprueft = 0;
        foreach (var datei in Dateien("tools", "*.ps1").Concat(Dateien("tests", "*.ps1")).Concat(Dateien("native", "*.ps1")))
        {
            if (Path.GetFileName(datei) == "dotnet-umgebung.ps1") continue;
            var code = OhneKommentare(File.ReadAllText(datei));
            var aufruf = DotnetAufruf().Match(code);
            if (!aufruf.Success) continue;
            geprueft++;
            var umgebung = UmgebungEinbinden().Match(code);
            if (!umgebung.Success || umgebung.Index > aufruf.Index) befunde.Add(Path.GetFileName(datei));
        }
        Pruefe.Wahr(geprueft >= 2, "paket-bauen.ps1 und testlauf.ps1 rufen dotnet auf (gefunden: " + geprueft + ")");
        Pruefe.Gleich(0, befunde.Count, "dotnet ohne tools\\dotnet-umgebung.ps1 davor: " + string.Join(", ", befunde));

        var umgebungText = File.ReadAllText(Path.Combine(Wurzel(), "tools", "dotnet-umgebung.ps1"));
        foreach (var name in new[] { "dev-umgebung.ps1", "$env:TEMP", "$env:TMP", "NUGET_SCRATCH", "DOTNET_CLI_HOME", "DOTNET_GENERATE_ASPNET_CERTIFICATE",
            "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "MSBUILDDISABLENODEREUSE" })
            Pruefe.Wahr(umgebungText.Contains(name, StringComparison.Ordinal), "dotnet-umgebung.ps1 setzt " + name);
    }

    [Test]
    public static void Werkzeuge_NetzUndProgrammstartNurMitSchalter()
    {
        var befunde = new List<string>();
        foreach (var datei in Dateien("tools", "*.ps1").Concat(Dateien("tests", "*.ps1")).Concat(Dateien("native", "*.ps1")))
        {
            var code = OhneKommentare(File.ReadAllText(datei));
            var name = Path.GetFileName(datei);
            var netz = NetzAufruf().Match(code);
            if (netz.Success)
            {
                var sperre = code.IndexOf("-not $NetzFreigegeben", StringComparison.Ordinal);
                if (sperre < 0 || sperre > netz.Index) befunde.Add(name + ": Netz ohne -NetzFreigegeben davor");
            }
            var start = StartAufruf().Match(code);
            if (start.Success)
            {
                var sperre = code.IndexOf("-not $SichtbarFreigegeben", StringComparison.Ordinal);
                if (sperre < 0 || sperre > start.Index) befunde.Add(name + ": Programmstart ohne -SichtbarFreigegeben davor");
                if (!code.Contains("'--vorfuehrung' -notin $Argumente", StringComparison.Ordinal)) befunde.Add(name + ": --vorfuehrung nicht erzwungen");
                if (!code.Contains("FUNK_RAUCHTEST", StringComparison.Ordinal) || !code.Contains("'--stumm'", StringComparison.Ordinal))
                    befunde.Add(name + ": echter Client ohne FUNK_RAUCHTEST und --stumm");
                var versuch = TryBlock().Matches(code).Any(m => m.Index < start.Index);
                var aufraeumen = FinallyBlock().Matches(code).Any(m => m.Index > start.Index);
                if (!versuch || !aufraeumen) befunde.Add(name + ": Start nicht in try/finally (das Programm bliebe bei einem Abbruch offen)");
            }
        }
        Pruefe.Gleich(0, befunde.Count, string.Join(" | ", befunde));
    }

    [Test]
    public static void Werkzeuge_TestlaufMeldetErgebnisUndCode()
    {
        // Befund: Neben einem Spiel läuft der Testlauf über "cmd /c start "" /belownormal /wait /b pwsh ...", und dort kommt
        // der Exitcode nicht an (gemessen am 29.09.2026: immer 0). Das Skript reicht den Code durch und schreibt dazu die
        // Ergebnisdatei; hier am Quelltext geprüft, denn ein Test startet keine Programme.
        var text = File.ReadAllText(Path.Combine(Wurzel(), "tests", "testlauf.ps1"));
        var hilfe = BlockKommentar().Match(text).Value;
        var code = OhneKommentare(text);
        Pruefe.Wahr(hilfe.Contains(@"C:\Claude\tmp\testlauf-ergebnis.txt", StringComparison.Ordinal) && hilfe.Contains("start /wait /b", StringComparison.Ordinal)
            && hilfe.Contains("Summenzeile", StringComparison.Ordinal), "die Hilfe nennt Ergebnisdatei, Summenzeile und warum");
        Pruefe.Wahr(code.Contains(@"$Ergebnisdatei = 'C:\Claude\tmp\testlauf-ergebnis.txt'", StringComparison.Ordinal), "Standardort der Ergebnisdatei");
        Pruefe.Wahr(code.Contains(@"StartsWith('C:\Claude\'", StringComparison.Ordinal), "die Ergebnisdatei bleibt in der Zone");

        // Das Muster des Skripts erkennt die Summenzeile, wie der Testlauf sie schreibt, auch mit Fehlschlägen.
        var muster = SummenMuster().Match(code);
        Pruefe.Wahr(muster.Success, "Muster für die Summenzeile im Skript");
        var summe = new Regex(muster.Groups[1].Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Pruefe.Wahr(summe.IsMatch(Testlauf.Summenzeile(844, 1, 845, 263_839)) && summe.IsMatch(Testlauf.Summenzeile(0, 0, 0, 5)),
            "Muster passt zur Summenzeile: " + muster.Groups[1].Value);
        Pruefe.Falsch(summe.IsMatch("FEHLER Klasse.Test: 3 bestanden, 1 fehlgeschlagen"), "nur am Zeilenanfang");

        // Jeder Ausgang geht über Beenden: erst die Ergebnisdatei, dann exit mit dem Code. Kein exit daran vorbei.
        var exits = Regex.Matches(code, @"(?m)\bexit\b").Select(m => m.Index).ToList();
        var datei = code.IndexOf("Set-Content -LiteralPath $ergebnisPfad", StringComparison.Ordinal);
        Pruefe.Wahr(exits.Count == 1 && code.IndexOf("exit $Code", StringComparison.Ordinal) == exits[0] && datei >= 0 && datei < exits[0],
            "genau ein exit, hinter dem Schreiben der Ergebnisdatei (" + exits.Count + ")");
        var bauen = code.IndexOf("& dotnet build", StringComparison.Ordinal);
        var lauf = code.IndexOf("& dotnet run", StringComparison.Ordinal);
        Pruefe.Wahr(bauen >= 0 && code.IndexOf("Beenden \"Bauen fehlgeschlagen\" $LASTEXITCODE", bauen, StringComparison.Ordinal) > bauen, "Baufehler: Datei und Code");
        Pruefe.Wahr(lauf > bauen && code.IndexOf("$code = $LASTEXITCODE", lauf, StringComparison.Ordinal) > lauf, "Code des Testlaufs gemerkt");
        Pruefe.Wahr(code.Contains("if ($fehlgeschlagen -gt 0 -and $code -eq 0) { $code = 1 }", StringComparison.Ordinal), "Fehlschläge ergeben nie 0");
        Pruefe.Wahr(code.Contains("($code -ne 0 ? $code : 1)", StringComparison.Ordinal), "ohne Summenzeile nie 0");
    }

    [Test]
    public static void NuGetConfig_PaketordnerInDerZone()
    {
        // Gilt für jedes Restore aus dem Repository, auch aus einer Shell ohne dev-umgebung.ps1 (NUGET_PACKAGES leer).
        var xml = XDocument.Load(Path.Combine(Wurzel(), "NuGet.config"));
        var ordner = xml.Root?.Element("config")?.Elements("add").FirstOrDefault(e => (string?)e.Attribute("key") == "globalPackagesFolder");
        var wert = (string?)ordner?.Attribute("value");
        Pruefe.Wahr(wert is not null && wert.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase), "globalPackagesFolder in C:\\Claude: " + wert);
        var quellen = xml.Root?.Element("packageSources")?.Elements("add").Select(e => (string?)e.Attribute("value")).ToList() ?? [];
        Pruefe.Wahr(quellen.Count > 0 && quellen.All(q => q is not null && q.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)),
            "nur Paketquellen aus der Zone: " + string.Join(", ", quellen));
    }

    [Test]
    public static void App_Vorfuehrung_OhneGlobaleTastenUndJoysticks()
    {
        var hinweise = new List<string>();
        var erzeugt = false;
        var tasten = App.TastenFuerStart(vorfuehrung: true, hinweise, _ =>
        {
            erzeugt = true;
            return null;
        });
        Pruefe.Gleich(null, tasten, "Vorführung: keine Tastenquelle");
        Pruefe.Falsch(erzeugt, "Vorführung: Raw Input und DirectInput nicht angelegt");
        Pruefe.Wahr(hinweise.Contains("Tasten: aus (Vorführung)"), "Hinweis in der Fußzeile");

        var imBetrieb = new List<string>();
        _ = App.TastenFuerStart(vorfuehrung: false, imBetrieb, h =>
        {
            erzeugt = true;
            return null;
        });
        Pruefe.Wahr(erzeugt, "im Betrieb werden die Tasten angelegt");
        Pruefe.Gleich(0, imBetrieb.Count, "im Betrieb kein Vorführungshinweis");
    }

    [Test]
    public static void KlangMaschine_EchteGeraeteNurImProgrammOderFreigegeben()
    {
        Pruefe.Falsch(KlangMaschine.ImProgramm, "der Testprozess ist nicht JG TacRelay");
        if (Environment.GetEnvironmentVariable("FUNK_RAUCHTEST") == "1")
        {
            Console.WriteLine("  KlangMaschine-Sperre: FUNK_RAUCHTEST=1 gesetzt, echte Geräte sind freigegeben; nichts weiter geprüft.");
            return;
        }
        Pruefe.Wahr(KlangMaschine.EchteGeraeteSperre() is not null, "ohne FUNK_RAUCHTEST gesperrt");
        // Die Sperre greift vor dem ersten Zugriff auf WASAPI (erste Anweisung nach den Optionen im Konstruktor).
        var e = Pruefe.Wirft<InvalidOperationException>(
            () => new KlangMaschine(new KlangMaschinenOptionen { EigeneThreads = false, MitApm = false, MitRnnoise = false }).DisposeAsync().AsTask().GetAwaiter().GetResult(),
            "KlangMaschine ohne AudioSystem im Testprozess");
        Pruefe.Wahr(e.Message.Contains("FUNK_RAUCHTEST", StringComparison.Ordinal), "Meldung nennt die Freigabe: " + e.Message);
    }

    /// <summary>Entfernt Kommentarblöcke &lt;# #&gt; und Zeilenkommentare, damit Beispiele in der Hilfe nicht zählen.</summary>
    private static string OhneKommentare(string code) => ZeilenKommentar().Replace(BlockKommentar().Replace(code, m => new string('\n', m.Value.Count(c => c == '\n'))), "");

    [GeneratedRegex(@"\[Test\]")]
    private static partial Regex TestMarke();

    [GeneratedRegex(@"new\s+RawInputSprechtaste\s*\(\s*\)")]
    private static partial Regex RawInputMuster();

    [GeneratedRegex(@"new\s+DirectInputSprechtaste\s*\(\s*\)|new\s+DirectInputGeraete\s*\(|new\s+DirectInputSuche\s*\(")]
    private static partial Regex DirectInputMuster();

    [GeneratedRegex(@"new\s+WasapiSystem\s*\(")]
    private static partial Regex WasapiMuster();

    [GeneratedRegex(@"new\s+KlangMaschine\s*\(")]
    private static partial Regex KlangMaschineMuster();

    [GeneratedRegex(@"\.Show\s*\(\s*\)|\.ShowDialog\s*\(|\.Activate\s*\(\s*\)|EnsureHandle\s*\(|new\s+HwndSource\s*\(")]
    private static partial Regex FensterMuster();

    [GeneratedRegex(@"Process\.Start\s*\(|new\s+Process\s*\{|new\s+Process\s*\(")]
    private static partial Regex ProzessMuster();

    [GeneratedRegex(@"new\s+PcpTpmPruefung\s*\(|(?<!"")Adminvoraussetzungen\.AmPc\s*\(|new\s+TpmAdminSchluessel\s*\(")]
    private static partial Regex EchterPcMuster();

    [GeneratedRegex(@"<#.*?#>", RegexOptions.Singleline)]
    private static partial Regex BlockKommentar();

    [GeneratedRegex(@"(?m)^\s*#.*$")]
    private static partial Regex ZeilenKommentar();

    [GeneratedRegex(@"(?m)(&\s*\$?dotnet\b)|((^|[;{|])\s*dotnet\s)")]
    private static partial Regex DotnetAufruf();

    [GeneratedRegex(@"(?m)^\s*\.\s+.*dotnet-umgebung\.ps1")]
    private static partial Regex UmgebungEinbinden();

    [GeneratedRegex(@"Invoke-WebRequest|Invoke-RestMethod|Start-BitsTransfer|\bcurl(\.exe)?\s|\bwget\s")]
    private static partial Regex NetzAufruf();

    [GeneratedRegex(@"Start-Process\b")]
    private static partial Regex StartAufruf();

    [GeneratedRegex(@"(?m)^\s*try\s*\{")]
    private static partial Regex TryBlock();

    [GeneratedRegex(@"(?m)^\s*\}?\s*finally\s*\{")]
    private static partial Regex FinallyBlock();

    [GeneratedRegex(@"-match\s+'([^']*bestanden[^']*)'")]
    private static partial Regex SummenMuster();
}
