// JG TacRelay - Maintainer: Sarevoc
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Funk.Admin;

namespace Funk.Tests;

/// <summary>
/// Was sich an funk-einrichten.sh, "Profil erneuern.cmd", den Units und den Bauskripten ohne Linux prüfen lässt: Zeilenenden,
/// die Falle mit pipefail, dieselben Optionen wie funk-admin einrichten, dieselben Pfade wie <see cref="Einrichtorte.Linux"/>,
/// die Fassung im Format, das das Skript aus funk-server.deps.json liest, und dass beide Pakete Skript, Doppelklick und
/// Anleitungen mitnehmen. Ob das Skript auf Debian und Ubuntu wirklich durchläuft, zeigt nur der Probelauf im Wegwerf-Container.
/// </summary>
internal static class EinrichtenSkriptTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

    private static byte[] Bytes(params string[] teile) => File.ReadAllBytes(Path.Combine([EinrichtungTests.Repo(), .. teile]));

    private static string Text(params string[] teile) => Encoding.UTF8.GetString(Bytes(teile));

    [Test]
    public static void Skript_LfShebangPipefailUndKeinGrepQ()
    {
        var b = Bytes("konfig", "funk-einrichten.sh");
        Pruefe.Falsch(b.Contains((byte)'\r'), "nur LF (bash liest ein CR als Teil des Befehls)");
        Pruefe.Falsch(b.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]), "keine BOM vor #!");
        var t = Encoding.UTF8.GetString(b);
        Pruefe.Wahr(t.StartsWith("#!/usr/bin/env bash\n", StringComparison.Ordinal), "Shebang");
        Pruefe.Wahr(Regex.IsMatch(t, @"^set -euo pipefail$", RegexOptions.Multiline, Frist), "set -euo pipefail");
        // Code ohne Kommentarzeilen: Mit pipefail scheitert "x | grep -q" zufällig, wenn x nach dem ersten Treffer SIGPIPE bekommt.
        var code = string.Join("\n", t.Split('\n').Where(z => !z.TrimStart().StartsWith('#')));
        Pruefe.Falsch(Regex.IsMatch(code, @"\|\s*grep\s+(-\w*q|--quiet)", RegexOptions.None, Frist), "kein | grep -q");
        Pruefe.Falsch(Regex.IsMatch(code, @"\|\s*head\b", RegexOptions.None, Frist), "kein | head (gleiche Falle)");
        Pruefe.Wahr(code.Contains("exec /usr/bin/dotnet \"$ziel/funk-admin.dll\" einrichten ${weiter[@]+\"${weiter[@]}\"}", StringComparison.Ordinal), "übergibt an funk-admin einrichten");
        Pruefe.Wahr(code.Contains("ziel=" + Einrichtorte.Linux.Programme + "\n", StringComparison.Ordinal), "Ziel wie Einrichtorte.Linux.Programme");
        Pruefe.Wahr(code.Contains("chmod -R go-w \"$ziel\"", StringComparison.Ordinal) && code.Contains("chown -R root:root \"$ziel\"", StringComparison.Ordinal), "Programme gehören root, für andere nicht beschreibbar");
    }

    /// <summary>
    /// Gegenprüfung 01.10.2026: Das Skript verglich und kopierte den ganzen Ordner linux. Wer nach EINRICHTEN.md profil.json oder
    /// nach UMZUG.md umzug.tru dort hineinlegt, bekam bei laufendem Dienst die Frage "Für die Programme kurz anhalten?" (die
    /// Eingabetaste heißt ja), der Dienst stand für ein bloßes neues Profil, und mit n kam das Profil gar nicht an. umzug.tru landete in
    /// /opt/funk, und aus einer Kopie des Stickordners wären profil.key und reserve.key mitgewandert. Jetzt zählt nur, was zum
    /// Programmpaket gehört. Die Muster stehen im Skript; hier wird geprüft, dass sie jede Datei des Pakets treffen und keine,
    /// die jemand danebenlegt. Gemessen mit Git Bash an beiden Probepaketen: alle 24 Dateien von linux in der Liste.
    /// </summary>
    [Test]
    public static void Skript_VergleichtUndKopiertNurProgrammdateien()
    {
        var t = Text("konfig", "funk-einrichten.sh");
        var code = string.Join("\n", t.Split('\n').Where(z => !z.TrimStart().StartsWith('#')));
        Pruefe.Falsch(code.Contains("cp -R", StringComparison.Ordinal) || code.Contains("diff -rq", StringComparison.Ordinal), "kein Kopieren und kein Vergleich des ganzen Ordners");
        var start = t.IndexOf("programmdateien() (", StringComparison.Ordinal);
        Pruefe.Wahr(start >= 0, "Funktion programmdateien");
        var schleife = Regex.Match(t[start..], @"for f in (.+); do", RegexOptions.None, Frist);
        Pruefe.Wahr(schleife.Success, "die Muster in programmdateien");
        var muster = Regex.Matches(schleife.Groups[1].Value, "\"[^\"]*\"|\\S+", RegexOptions.None, Frist)
            .Select(m => new Regex("^" + Regex.Escape(m.Value.Trim('"')).Replace(@"\*", "[^/]*", StringComparison.Ordinal) + "$", RegexOptions.None, Frist)).ToList();
        bool Programm(string pfad) => muster.Any(m => m.IsMatch(pfad));

        // Was die Bauskripte nach linux legen (MANIFEST des Serverpakets 20261001_1133), seit 0.3.0 dazu HEIMSERVER-HAERTUNG.md und die
        // fail2ban-Beispiele in einem Unterordner von konfig (Serverpaket 20261001_1316). Bis zur Zusammenführung 0.4.0 traf "konfig/*"
        // den Unterordner nicht, und /opt/funk hätte nach einrichten ohne die Beispiele dagestanden, nach dem Weg von Hand (tar) mit.
        foreach (var d in new[]
        {
            "BETRIEB.md", "EINRICHTEN.md", "HEIMSERVER-PROXMOX.md", "HEIMSERVER-HAERTUNG.md", "UMZUG.md", "funk-einrichten.sh", "Profil erneuern.cmd",
            "konfig/fail2ban/filter.d/tacrelay.conf", "konfig/fail2ban/jail.d/tacrelay.conf",
            "funk-admin.dll", "funk-admin.deps.json", "funk-admin.runtimeconfig.json", "funk-echo.dll", "funk-echo.deps.json", "funk-echo.runtimeconfig.json",
            "funk-server.dll", "funk-server.deps.json", "funk-server.runtimeconfig.json", "funk-testclient.dll", "funk-testclient.deps.json",
            "funk-testclient.runtimeconfig.json", "Funk.ClientKern.dll", "Funk.Protokoll.dll",
            "konfig/funk-echo.service", "konfig/funk-server.service", "konfig/profilschluessel.txt", "konfig/server.beispiel.json",
        })
            Pruefe.Wahr(Programm(d), "gehört zum Paket: " + d);
        // Was daneben liegen kann: Profil und Umzugspaket nach den Anleitungen, auf dem Stick die privaten Schlüssel.
        foreach (var d in new[]
        {
            "profil.json", "profil-v1.json", "daten.json", "umzug.tru", "zurueck.tru", "profil.key", "reserve.key", "reserve.crt", "profilanfrage.txt",
            "zum-verschicken/profil.json", "alt/funk-server.dll",
        })
            Pruefe.Falsch(Programm(d), "bleibt draußen: " + d);
        Pruefe.Wahr(code.Contains("gleich \"$paket\" \"$ziel\"", StringComparison.Ordinal) && code.Contains("<<< \"$(programmdateien \"$paket\")\"", StringComparison.Ordinal),
            "Vergleich und Kopie über dieselbe Liste");
        // Dateien in Unterordnern (konfig/fail2ban/...): cp legt den Ordner nicht selbst an.
        Pruefe.Wahr(code.Contains("install -d -m 0755 \"$ziel/$(dirname -- \"$f\")\"", StringComparison.Ordinal), "Unterordner vor dem Kopieren anlegen");
    }

    /// <summary>
    /// Gegenprüfung 01.10.2026: Gab es /usr/bin/dotnet schon, aber ohne Laufzeit 10, installierte das Skript dotnet-runtime-10.0 über
    /// apt daneben, sobald apt eine kannte; das kann /usr/bin/dotnet eines anderen Dienstes umbiegen. Jetzt hält es an und sagt, was
    /// zu tun ist. Den Verweis auf /usr/share/dotnet legt es nur an, wenn dort die Laufzeit 10 liegt.
    /// </summary>
    [Test]
    public static void Skript_FremdesDotnetBleibtUnangetastet()
    {
        var t = Text("konfig", "funk-einrichten.sh");
        var abschnitt = t[t.IndexOf("# ---- .NET-Laufzeit 10", StringComparison.Ordinal)..t.IndexOf("# ---- Programme nach", StringComparison.Ordinal)];
        var fremd = abschnitt.IndexOf("elif [ -e /usr/bin/dotnet ]; then", StringComparison.Ordinal);
        var apt = abschnitt.IndexOf("elif [ \"$apt\" = ja ]; then", StringComparison.Ordinal);
        Pruefe.Wahr(fremd > 0 && apt > fremd, "erst die fremde Installation, dann apt");
        Pruefe.Wahr(abschnitt[fremd..apt].Contains("fehlt ", StringComparison.Ordinal), "dort hält es an");
        var verweis = abschnitt.IndexOf("ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet", StringComparison.Ordinal);
        Pruefe.Wahr(verweis > 0 && abschnitt[..verweis].Contains("*\"Microsoft.NETCore.App 10.\"*", StringComparison.Ordinal), "Verweis nur mit Laufzeit 10 in /usr/share/dotnet");
    }

    [Test]
    public static void Skript_OptionenWieFunkAdminEinrichten()
    {
        var t = Text("konfig", "funk-einrichten.sh");
        var schleife = t[t.IndexOf("while [ $# -gt 0 ]", StringComparison.Ordinal)..t.IndexOf("done\n", StringComparison.Ordinal)];
        var optionen = Regex.Matches(schleife, @"--([a-z-]+)\s*[|)]", RegexOptions.None, Frist).Select(m => m.Groups[1].Value).Where(o => o != "hilfe").Order(StringComparer.Ordinal);
        var tabelle = Befehlsoptionen.Befehle["einrichten"].Optionen.Order(StringComparer.Ordinal);
        Pruefe.Gleich(string.Join(" ", tabelle), string.Join(" ", optionen), "das Skript nimmt genau die Optionen von funk-admin einrichten an");
        // Was einrichten im Paket braucht, prüft das Skript, bevor es etwas kopiert.
        foreach (var d in new[] { "konfig/funk-server.service", "konfig/funk-echo.service", "konfig/profilschluessel.txt", "funk-server.deps.json" })
            Pruefe.Wahr(Regex.IsMatch(t, @"^for d in .*\b" + Regex.Escape(d), RegexOptions.Multiline, Frist), "Skript prüft " + d);
    }

    [Test]
    public static void Skript_FassungAusDepsJson()
    {
        // Das Skript liest die Fassung so: sed '/^ *"funk-server\/[0-9]/ ...' in funk-server.deps.json. Dieselbe Form steht in jeder
        // deps.json, die funk-server als Projekt nennt, hier die der Tests.
        var deps = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Funk.Tests.deps.json"));
        var m = Regex.Match(deps, @"^ *""funk-server/([0-9][0-9.]*)""", RegexOptions.Multiline, Frist);
        Pruefe.Wahr(m.Success, "\"funk-server/<fassung>\" am Zeilenanfang in der deps.json");
        var fassung = typeof(Funk.Server.Kern.Zentrale).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Pruefe.Gleich(fassung, m.Groups[1].Value, "dieselbe Fassung wie das Programm");
        Pruefe.Wahr(Text("konfig", "funk-einrichten.sh").Contains("""sed -n '/^ *"funk-server\/[0-9]/{s/^ *"funk-server\/\([0-9][0-9.]*\)".*/\1/p;q;}'""", StringComparison.Ordinal),
            "der sed-Ausdruck im Skript ist dieser (Backslashes heil angekommen)");
    }

    [Test]
    public static void Doppelklick_DreiZeilenCrlfUndBekannteOptionen()
    {
        var b = Bytes("tools", "paket", "Profil erneuern.cmd");
        Pruefe.Wahr(b.All(x => x is >= 0x20 and < 0x7F or (byte)'\r' or (byte)'\n'), "nur ASCII (cmd liest in der Codepage der Konsole)");
        var t = Encoding.ASCII.GetString(b);
        Pruefe.Gleich(t.Count(c => c == '\n'), t.Split("\r\n").Length - 1, "jede Zeile endet mit CRLF");
        var zeilen = t.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Pruefe.Gleich(3, zeilen.Length, "drei Zeilen");
        Pruefe.Gleich("@cd /d \"%~dp0\"", zeilen[0], "in den Ordner der Datei (funk-offline auf dem Stick)");
        Pruefe.Gleich("@pause", zeilen[2], "Fenster bleibt offen");
        var aufruf = zeilen[1].TrimStart('@').Split(' ');
        Pruefe.Gleich("dotnet funk-admin.dll", aufruf[0] + " " + aufruf[1], "funk-admin neben der Datei");
        Pruefe.Gleich(null, Befehlsoptionen.Pruefen(new Befehlszeile(aufruf[2..], Befehlsoptionen.Schalter)), "funk-admin nimmt den Aufruf an");
        Pruefe.Gleich("profil-erneuern --bisher profil.json --schluessel profil.key", string.Join(" ", aufruf[2..]), "wie im Entwurf");
    }

    [Test]
    public static void Units_PassenZuEinrichtorte()
    {
        var o = Einrichtorte.Linux;
        var server = Text("konfig", "funk-server.service");
        foreach (var name in new[] { "server.crt", "server.key", "funk-geheimnis", "funk-totp-schluessel" })
            Pruefe.Wahr(server.Contains("LoadCredential=" + name + ":" + o.Credentials + "/" + name, StringComparison.Ordinal), "LoadCredential " + name);
        Pruefe.Wahr(server.Contains("ExecStart=" + Einrichtorte.Dotnet + " " + o.Programme + "/funk-server.dll --konfig " + o.Konfig, StringComparison.Ordinal), "ExecStart funk-server");
        Pruefe.Wahr(server.Contains("StateDirectory=" + Path.GetFileName(o.Zustand), StringComparison.Ordinal) && o.Zustand == "/var/lib/funk", "StateDirectory");
        Pruefe.Wahr(Regex.IsMatch(server, @"^CapabilityBoundingSet=\s*$", RegexOptions.Multiline, Frist), "ohne Drop-in keine Fähigkeit");
        var echo = Text("konfig", "funk-echo.service");
        Pruefe.Wahr(echo.Contains("EnvironmentFile=-" + o.EchoEnv, StringComparison.Ordinal), "Adresse aus echo.env");
        Pruefe.Wahr(echo.Contains("ExecStart=" + Einrichtorte.Dotnet + " " + o.Programme + "/funk-echo.dll --profil " + o.Profil + " --identitaet " + o.EchoIdentitaet
            + " --adresse ${FUNK_ECHO_ADRESSE}", StringComparison.Ordinal), "ExecStart funk-echo mit ${FUNK_ECHO_ADRESSE}");
        // Environment= vor EnvironmentFile=: Die Datei gewinnt, ohne Datei gilt der Wert aus der Unit.
        Pruefe.Wahr(echo.IndexOf("Environment=FUNK_ECHO_ADRESSE=", StringComparison.Ordinal) is > 0 and var e && e < echo.IndexOf("EnvironmentFile=", StringComparison.Ordinal), "Voreinstellung in der Unit");
        Pruefe.Wahr(echo.Contains("StateDirectory=" + Path.GetFileName(o.EchoZustand), StringComparison.Ordinal), "StateDirectory funk-echo");
    }

    [Test]
    public static void Pakete_NehmenSkriptDoppelklickUndAnleitungenMit()
    {
        foreach (var skript in new[] { "paket-bauen.ps1", "serverpaket-bauen.ps1" })
        {
            var t = Text("tools", skript);
            foreach (var d in new[] { "funk-einrichten.sh", "Profil erneuern.cmd", "EINRICHTEN.md", "UMZUG.md", "profilschluessel.txt" })
                Pruefe.Wahr(t.Contains(d, StringComparison.Ordinal), skript + " nimmt " + d + " mit");
        }
        foreach (var d in new[] { "EINRICHTEN.md", "UMZUG.md" })
            Pruefe.Wahr(File.Exists(Path.Combine(EinrichtungTests.Repo(), "docs", d)), "docs/" + d);
        Pruefe.Wahr(Text("docs", "HEIMSERVER-PROXMOX.md").Contains("EINRICHTEN.md", StringComparison.Ordinal), "HEIMSERVER-PROXMOX.md verweist auf EINRICHTEN.md");
    }
}
