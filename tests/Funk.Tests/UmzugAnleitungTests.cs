// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Funk.Protokoll.Identitaet;
using Funk.Server.Betrieb;
using Schluessel = Funk.Tests.Oberflaeche.OberflaecheProfilTests.Schluessel;

namespace Funk.Tests;

/// <summary>
/// UMZUG.md so, wie Sarevoc und der Server-Admin es abschreiben. Befunde der Prüfung der Pakete 0.4.0 (mittel): Schritt 6 übersah mit dem Tag
/// von Schritt 4 genau die Geräte, die beim Laden von Version 2 verbunden waren (<c>geraete --nicht-seit</c> zählt auch das
/// Ende einer Sitzung, und das Profil kommt nur mit Welcome beim Sitzungsbeginn); Schritt 4 nannte <c>pruefen</c> erst nach
/// dem Codeblock und nur wegen des Pins, nicht wegen des Ports; Schritt 3 riet, nur <c>Profil erneuern.cmd</c> dazuzulegen,
/// obwohl ein funk-admin vor 0.4.0 <c>profil-erneuern</c> nicht kennt.
/// </summary>
internal static class UmzugAnleitungTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);
    private static readonly Fingerabdruck Fremd = Fingerabdruck.AusSpki([7]);
    private static readonly Fingerabdruck Reserve = Fingerabdruck.AusSpki([8]);

    private static string Lesen(params string[] teile) =>
        File.ReadAllText(Path.Combine([EinrichtungTests.Repo(), .. teile])).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Umzug() => Lesen("docs", "UMZUG.md");

    /// <summary>Leerraum zusammengefasst, wie Markdown umbricht.</summary>
    private static string Normal(string text) => Regex.Replace(text, @"\s+", " ", RegexOptions.None, Frist);

    /// <summary>Schritt <paramref name="n"/> von "**n. " bis zum nächsten Schritt oder zur nächsten Überschrift.</summary>
    private static string Schritt(string text, int n)
    {
        var von = text.IndexOf("\n**" + n.ToString(CultureInfo.InvariantCulture) + ". ", StringComparison.Ordinal);
        Pruefe.Wahr(von >= 0, "Schritt " + n.ToString(CultureInfo.InvariantCulture) + " gefunden");
        var naechster = text.IndexOf("\n**" + (n + 1).ToString(CultureInfo.InvariantCulture) + ". ", von + 1, StringComparison.Ordinal);
        var ueberschrift = text.IndexOf("\n## ", von + 1, StringComparison.Ordinal);
        var bis = new[] { naechster, ueberschrift, text.Length }.Where(i => i > von).Min();
        return text[von..bis];
    }

    /// <summary>Die Zeilen aller Codeblöcke, in Reihenfolge.</summary>
    private static List<string> Codezeilen(string text)
    {
        var zeilen = new List<string>();
        var imBlock = false;
        foreach (var zeile in text.Split('\n'))
        {
            if (zeile.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                imBlock = !imBlock;
                continue;
            }
            if (imBlock && zeile.Trim().Length > 0) zeilen.Add(zeile.Trim());
        }
        return zeilen;
    }

    /// <summary>Die Wörter nach <c>funk-admin.dll</c> wie nach der Shell, Platzhalter wie <c>&lt;JJJJ-MM-TT&gt;</c> als ein Wort.</summary>
    private static List<string>? Funkadmin(string zeile)
    {
        const string programm = "funk-admin.dll ";
        var p = zeile.IndexOf(programm, StringComparison.Ordinal);
        if (p < 0) return null;
        return [.. Regex.Matches(zeile[(p + programm.Length)..], @"(?:<[^>]*>|'[^']*'|""[^""]*""|[^\s<'""])+", RegexOptions.None, Frist)
            .Select(m => m.Value.Length >= 2 && (m.Value[0] == '\'' || m.Value[0] == '"') && m.Value[^1] == m.Value[0] ? m.Value[1..^1] : m.Value)];
    }

    private static async Task<bool> Warten(Func<bool> bedingung, int ms = 10_000)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(20);
        }
        return true;
    }

    private static uint GeraetId(Testserver t, KernFunker f) => t.Dienst.Zentrale.Konten.Stand.GeraetZuFingerabdruck(f.Identitaet.Fingerabdruck)!.GeraetId;

    /// <summary>Die Version des Profils, das der Client mit dem letzten Welcome bekam.</summary>
    private static uint? ProfilVersion(KernFunker f) =>
        f.Client.Lage.Welcome?.Profil is { } p && Serverprofil.TryLesen(Convert.FromBase64String(p.Daten), out var inhalt) ? inhalt.ProfilVersion : null;

    /// <summary>
    /// Schritt 4 und 6 wie geschrieben, gegen einen Server mit verstellbarer Uhr. Anna ist vor Schritt 4 gegangen und hat nur
    /// Version 1; Dora war beim Laden verbunden und geht am selben Abend. Wer in Schritt 6 nicht in der Liste steht, muss das
    /// neue Profil haben, sonst fände er den Server nach dem Umzug nie. Mit dem alten Text (kein Neustart, Tag von Schritt 4)
    /// fehlten beide in der Liste, obwohl keine von ihnen Version 7 hatte.
    /// </summary>
    [Test]
    public static async Task Umzug_Nachzuegler_WieGeschrieben()
    {
        var umzug = Umzug();
        var schritt4 = Codezeilen(Schritt(umzug, 4));
        var text6 = Schritt(umzug, 6);
        var zeile6 = Codezeilen(text6).Single(z => z.Contains("geraete --nicht-seit", StringComparison.Ordinal));
        var tagDanach = Normal(text6).Contains("Tag nach Schritt 4", StringComparison.Ordinal);

        using var k = new Schluessel();
        var uhr = new Testuhr();
        var verzeichnis = Path.Combine(Testserver.Wurzel(), Guid.NewGuid().ToString("N")[..16]);
        var halter = new ServerHalter();
        var t = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr);
        halter.Server = t;
        Testserver? neu = null;
        KernFunker? anna = null;
        KernFunker? dora = null;
        try
        {
            anna = await KernFunker.AktivAsync(halter, "961000000000000001", "anna", "FUNKER", IPAddress.Parse("127.0.0.93"));
            dora = await KernFunker.AktivAsync(halter, "961000000000000002", "dora", "FUNKER", IPAddress.Parse("127.0.0.94"));
            var annaKennung = anna.Identitaet.Fingerabdruck.Anzeige;
            var doraKennung = dora.Identitaet.Fingerabdruck.Anzeige;

            // Anna geht vor Schritt 4 und kommt bis zum Umzug nicht wieder.
            uhr.Vor(TimeSpan.FromMinutes(30));
            var annaId = GeraetId(t, anna);
            await anna.DisposeAsync();
            anna = null;
            Pruefe.Wahr(await Warten(() => !t.Dienst.Zentrale.Konten.Stand.GeraeteMitSitzung.Contains(annaId)), "Annas Sitzung ist zu");

            // Schritt 4: Version 7 mit dem Pin dieses Servers liegt in profil.json; die Befehle laufen, wie sie dastehen.
            uhr.Vor(TimeSpan.FromMinutes(30));
            var tag4 = DateOnly.FromDateTime(uhr.GetUtcNow().UtcDateTime);
            File.WriteAllBytes(Path.Combine(verzeichnis, "profil.json"),
                ProfiluebernahmeTests.Signiert(k, 7, ["heim.example.org", "funk.example.org"], [t.Dienst.Pin, Fremd, Reserve]));
            var geladen = false;
            foreach (var zeile in schritt4)
            {
                if (Funkadmin(zeile) is { } woerter)
                {
                    var z = new Funk.Admin.Befehlszeile(woerter, Funk.Admin.Befehlsoptionen.Schalter);
                    Pruefe.Gleich(null, Funk.Admin.Befehlsoptionen.Pruefen(z), "funk-admin nimmt die Zeile an: " + zeile);
                    // pruefen liest die Dateien des echten Servers; hier zählen nur die Optionen.
                    if (woerter[0] != "profil-neu-laden") continue;
                    var (befehl, json) = Funk.Admin.Uebersetzung.Uebersetzen(z);
                    Pruefe.Gleich("Profil Version 7 geladen.", (await t.AdminOk(befehl, json)).Text, "profil-neu-laden wie in Schritt 4");
                    geladen = true;
                }
                else if (zeile == "systemctl restart funk-server")
                {
                    Pruefe.Wahr(geladen, "neu gestartet wird erst nach profil-neu-laden");
                    halter.Server = null;
                    await t.DienstBeendenAsync();
                    halter.HartTrennen();
                    neu = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr, profilBehalten: true);
                    halter.Server = neu;
                    KernFunker d = dora!;
                    Pruefe.Wahr(await Warten(() => d.Client.Aktivierungen >= 2 && d.Client.Zustand == Funk.Protokoll.Steuerung.ClientZustand.AktivUdp, 15_000),
                        "Dora verbindet sich nach dem Neustart von selbst wieder");
                    Pruefe.Gleich((uint?)7, ProfilVersion(d), "nach dem Neustart bringt Welcome Version 7");
                }
            }
            Pruefe.Wahr(geladen, "Schritt 4 lädt das Profil");
            var server = neu ?? t;

            // Dora geht am selben Abend.
            uhr.Vor(TimeSpan.FromHours(2.5));
            var doraVersion = ProfilVersion(dora);
            var doraId = GeraetId(server, dora);
            await dora.DisposeAsync();
            dora = null;
            Pruefe.Wahr(await Warten(() => !server.Dienst.Zentrale.Konten.Stand.GeraeteMitSitzung.Contains(doraId)), "Doras Sitzung ist zu");

            // Schritt 6, ein paar Tage später, mit dem Tag, den die Anleitung nennt.
            uhr.Vor(TimeSpan.FromDays(3));
            var tag = (tagDanach ? tag4.AddDays(1) : tag4).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var woerter6 = Funkadmin(zeile6)!.Select(w => w == "<JJJJ-MM-TT>" ? tag : w).ToList();
            var z6 = new Funk.Admin.Befehlszeile(woerter6, Funk.Admin.Befehlsoptionen.Schalter);
            Pruefe.Gleich(null, Funk.Admin.Befehlsoptionen.Pruefen(z6), "funk-admin nimmt Schritt 6 an: " + string.Join(' ', woerter6));
            var (befehl6, json6) = Funk.Admin.Uebersetzung.Uebersetzen(z6);
            var liste = (await server.AdminOk(befehl6, json6)).Text;

            Pruefe.Wahr(liste.Contains(annaKennung, StringComparison.Ordinal), $"Anna hat nur Version 1 und steht in der Liste (--nicht-seit {tag}):\n{liste}");
            Pruefe.Wahr(liste.Contains(doraKennung, StringComparison.Ordinal) || doraVersion == 7,
                $"Dora fehlt in der Liste (--nicht-seit {tag}), hat aber Version {doraVersion}, nicht 7:\n{liste}");
        }
        finally
        {
            if (anna is not null) await anna.DisposeAsync();
            if (dora is not null) await dora.DisposeAsync();
            halter.Server = null;
            if (neu is not null) await neu.DisposeAsync();
            await t.DisposeAsync();
        }
    }

    /// <summary>
    /// Schritt 4: Erst <c>pruefen --vollstaendig</c>, dann <c>profil-neu-laden</c>, und die Zeilen, auf die Sarevoc achten soll, gibt es
    /// so: die Portwarnung aus <see cref="Profilabgleich.PortWarnung"/> und die Pin-Zeile aus der vollständigen Prüfung. Schritt 3
    /// sagt, was bei einem anderen Port geschieht.
    /// </summary>
    [Test]
    public static void Umzug_Schritt4_ErstPruefenDannLaden()
    {
        var umzug = Umzug();
        var text4 = Schritt(umzug, 4);
        var befehle = Codezeilen(text4).Select(Funkadmin).OfType<List<string>>().ToList();
        Pruefe.Wahr(befehle.Count >= 2 && befehle[0] is ["pruefen", "--vollstaendig", ..] && befehle.FindIndex(b => b[0] == "profil-neu-laden") > 0,
            "Schritt 4: zuerst pruefen --vollstaendig, danach profil-neu-laden: " + string.Join(" | ", befehle.Select(b => string.Join(' ', b))));

        var warnung = Profilabgleich.PortWarnung(new ServerKonfiguration { TcpPort = 8443 }, 443)!;
        var kopf = "WARNUNG " + warnung[..warnung.IndexOf(" 443", StringComparison.Ordinal)];
        Pruefe.Wahr(Normal(text4).Contains("`" + kopf, StringComparison.Ordinal), "Schritt 4 zitiert die Portwarnung so, wie sie kommt: " + kopf);
        var pruefung = Lesen("src", "Funk.Admin", "Serverpruefung.cs");
        Pruefe.Wahr(pruefung.Contains("Ok(\"Pin des Serverschlüssels \" + p.Anzeige + \" steht im Profil.\")", StringComparison.Ordinal)
            && Regex.IsMatch(Normal(text4), "`OK +Pin des Serverschlüssels XXXX-XXXX-XXXX-XXXX steht im Profil\\.`", RegexOptions.None, Frist),
            "Schritt 4 zitiert die Pin-Zeile von pruefen --vollstaendig");
        Pruefe.Wahr(Normal(text4).Contains("Profil Version 2 geladen.", StringComparison.Ordinal), "Schritt 4 nennt die Antwort von profil-neu-laden");

        Pruefe.Wahr(Normal(Schritt(umzug, 3)).Contains("TCP-Port 8443 -> 443", StringComparison.Ordinal), "Schritt 3 nennt die Ablehnung eines anderen Ports");
    }

    /// <summary>
    /// Der Stick braucht vor einem Umzug die Programme 0.4.0: Ein funk-admin vor 0.4.0 kennt <c>profil-erneuern</c> nicht
    /// (gemessen mit funk-admin 0.2.0 aus dem Serverpaket 20261001_0248: <c>Unbekannter Befehl: profil-erneuern. funk-admin hilfe
    /// zeigt alle.</c>, Exit 2). UMZUG.md sagt das vorweg und in Schritt 3, HEIMSERVER-PROXMOX.md Abschnitt 16 Schritt 11 ist
    /// vor einem Umzug Pflicht, die LIESMICH des Serverpakets nennt ihn beim Umziehen.
    /// </summary>
    [Test]
    public static async Task Umzug_StickMitDenProgrammen040()
    {
        var umzug = Umzug();
        var vorweg = umzug[umzug.IndexOf("## Vorweg", StringComparison.Ordinal)..umzug.IndexOf("## Vorlauf", StringComparison.Ordinal)];
        Pruefe.Wahr(Normal(vorweg).Contains("funk-offline", StringComparison.Ordinal) && Normal(vorweg).Contains("Abschnitt 16, Schritt 11", StringComparison.Ordinal),
            "Vorweg: funk-offline mit den Programmen 0.4.0");
        var text3 = Normal(Schritt(umzug, 3));
        Pruefe.Falsch(text3.Contains("Sonst die Datei aus dem neuen Paket dazulegen", StringComparison.Ordinal), "Schritt 3 rät nicht mehr, nur die cmd-Datei dazuzulegen");

        // Die zitierte Ablehnung hat die Form, die funk-admin bei einem unbekannten Befehl druckt.
        var (code, text) = await AdminEinrichtungTests.Werkzeug("gibt-es-nicht");
        Pruefe.Gleich(2, code, "unbekannter Befehl: " + text);
        var form = text.Trim().Replace("gibt-es-nicht", "profil-erneuern", StringComparison.Ordinal);
        var zitat = Regex.Match(text3, "`(Unbekannter Befehl: profil-erneuern[^`]*)`", RegexOptions.None, Frist);
        Pruefe.Wahr(zitat.Success && form.StartsWith(zitat.Groups[1].Value, StringComparison.Ordinal), "Schritt 3 zitiert die Ablehnung wie gedruckt: " + form);
        (code, text) = await AdminEinrichtungTests.Werkzeug("hilfe");
        Pruefe.Wahr(code == 0 && text.Contains("profil-erneuern", StringComparison.Ordinal), "das funk-admin dieser Fassung kennt profil-erneuern");

        var heim = Lesen("docs", "HEIMSERVER-PROXMOX.md");
        var schritt11 = heim[heim.IndexOf("**Schritt 11", heim.IndexOf("## 16.", StringComparison.Ordinal), StringComparison.Ordinal)..];
        schritt11 = schritt11[..schritt11.IndexOf("\n\n", StringComparison.Ordinal)];
        Pruefe.Falsch(schritt11.Contains("wenn du magst", StringComparison.Ordinal), "Abschnitt 16, Schritt 11 ist vor einem Umzug Pflicht: " + schritt11);
        Pruefe.Wahr(schritt11.Contains("Umzug", StringComparison.Ordinal), "Abschnitt 16, Schritt 11 nennt den Umzug");

        var liesmich = Lesen("tools", "paket", "LIESMICH-Server.txt");
        var umziehen = liesmich[liesmich.IndexOf("Umziehen?", StringComparison.Ordinal)..liesmich.IndexOf("\nAnleitungen\n", StringComparison.Ordinal)];
        Pruefe.Wahr(Normal(umziehen).Contains("Abschnitt 16, Schritt 11", StringComparison.Ordinal), "LIESMICH-Server, Umziehen: erst den Stick auf 0.4.0: " + umziehen);
    }
}
