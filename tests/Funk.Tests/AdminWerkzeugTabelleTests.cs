// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Funk.Admin;

namespace Funk.Tests;

/// <summary>
/// Die Optionstabelle von funk-admin (<see cref="Befehlsoptionen"/>) gegen den Code, der die Optionen liest, und gegen alle
/// Stellen, an denen Sarevoc einen Befehl abschreibt: HEIMSERVER-PROXMOX.md, BETRIEB.md, EINRICHTEN.md, UMZUG.md, die
/// LIESMICH-Vorlagen, funk-einrichten.sh, "Profil erneuern.cmd" und den Hilfetext. Seit funk-admin unbekannte Optionen ablehnt (01.10.2026), würde eine Option, die in der Anleitung steht und in
/// der Tabelle fehlt, Sarevoc mit Exit 2 aufhalten; eine Option, die der Code liest und die Tabelle nicht kennt, ließe sich nie
/// mehr angeben.
/// </summary>
internal static class AdminWerkzeugTabelleTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden.");
    }

    private static string Lesen(params string[] teile) => File.ReadAllText(Path.Combine([Wurzel(), .. teile]));

    /// <summary>Befehl und gelesene Optionen je <c>case</c> eines Quelltextausschnitts.</summary>
    private static Dictionary<string, (HashSet<string> Optionen, bool MitGeraet)> Gelesen(string ausschnitt)
    {
        var ergebnis = new Dictionary<string, (HashSet<string>, bool)>(StringComparer.Ordinal);
        var faelle = Regex.Matches(ausschnitt, @"case\s+(""[^""]+""(?:\s+or\s+""[^""]+"")*)\s*:", RegexOptions.None, Frist);
        for (var i = 0; i < faelle.Count; i++)
        {
            var ende = i + 1 < faelle.Count ? faelle[i + 1].Index : ausschnitt.Length;
            var rumpf = ausschnitt[(faelle[i].Index + faelle[i].Length)..ende];
            var optionen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(rumpf, @"(?:\bText|\bZahl|z\.Wert|z\.Zahl|z\.Pflicht|z\.Alle|z\.Schalter)\(""([a-z-]+)""", RegexOptions.None, Frist))
                optionen.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(rumpf, @"ListeOderLeeren\(""([a-z-]+)""", RegexOptions.None, Frist))
            {
                optionen.Add(m.Groups[1].Value);
                optionen.Add("leeren");
            }
            var mitGeraet = rumpf.Contains("Geraet()", StringComparison.Ordinal);
            if (mitGeraet) optionen.Add("geraet");
            foreach (Match b in Regex.Matches(faelle[i].Groups[1].Value, @"""([^""]+)""", RegexOptions.None, Frist))
                ergebnis.Add(b.Groups[1].Value, (optionen, mitGeraet));
        }
        return ergebnis;
    }

    [Test]
    public static void AdminWerkzeug_Tabelle_PasstZumCode()
    {
        var programm = Lesen("src", "Funk.Admin", "Program.cs");
        var von = programm.IndexOf("// ---- offline ----", StringComparison.Ordinal);
        var bis = programm.IndexOf("// ---- über den Server ----", StringComparison.Ordinal);
        Pruefe.Wahr(von > 0 && bis > von, "Program.cs hat die Abschnitte offline und über den Server");
        var offline = Gelesen(programm[von..bis]);
        var server = Gelesen(Lesen("src", "Funk.Admin", "Uebersetzung.cs"));
        Pruefe.Wahr(offline.Count >= 7 && server.Count >= 40, $"Fälle gefunden: offline {offline.Count}, Server {server.Count}");

        foreach (var (quelle, faelle, istOffline) in new[] { ("Program.cs", offline, true), ("Uebersetzung.cs", server, false) })
        {
            foreach (var (befehl, (optionen, mitGeraet)) in faelle)
            {
                Pruefe.Wahr(Befehlsoptionen.Befehle.TryGetValue(befehl, out var regel), $"{befehl} ({quelle}) steht in der Tabelle");
                Pruefe.Gleich(istOffline, regel!.Offline, $"{befehl}: offline wie im Code");
                Pruefe.Gleich(mitGeraet, regel.MitGeraet, $"{befehl}: Gerät ohne Option wie im Code");
                var tabelle = string.Join(" ", regel.Optionen.Order(StringComparer.Ordinal));
                var code = string.Join(" ", optionen.Order(StringComparer.Ordinal));
                Pruefe.Gleich(code, tabelle, $"{befehl}: Optionen der Tabelle = gelesene Optionen in {quelle}");
            }
        }
        var imCode = offline.Keys.Concat(server.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (var befehl in Befehlsoptionen.Befehle.Keys)
            Pruefe.Wahr(imCode.Contains(befehl), $"{befehl} aus der Tabelle hat einen Fall im Code");
        foreach (var s in Befehlsoptionen.Schalter)
            Pruefe.Wahr(Befehlsoptionen.UeberDenServer.Contains(s) || Befehlsoptionen.Befehle.Values.Any(r => r.Optionen.Contains(s, StringComparer.Ordinal)), $"Schalter --{s} gehört zu einem Befehl");
    }

    [Test]
    public static void AdminWerkzeug_Pruefen_Meldungen()
    {
        static string? P(params string[] a) => Befehlsoptionen.Pruefen(new Befehlszeile(a, Befehlsoptionen.Schalter));
        Pruefe.Gleich(null, P("scnamen", "--konto", "1", "--namen", "Sarevoc Test", "--konfig", "/etc/funk/server.json"), "richtig");
        Pruefe.Gleich(null, P("scnamen", "--konto", "1", "--leeren", "--socket", "/run/funk/v.sock", "--json-aus"), "--leeren, Socket, JSON");
        Pruefe.Gleich(null, P("freigeben", "ABCD-EFGH", "--vormerkung", "3", "--konfig", "x"), "Gerät vorn");
        Pruefe.Gleich(null, P("profil-schreiben", "--host", "a.example.org", "--host", "b.example.org/v6", "--pin", "x", "--pin", "y", "--aus", "d.json"), "mehrfach erlaubt");
        Pruefe.Gleich(null, P("hilfe", "--was-auch-immer"), "hilfe prüft nichts");
        Pruefe.Gleich(null, P(), "ohne Befehl: hilfe");

        Pruefe.Gleich("scnamen kennt --name nicht. Erlaubt: --konto, --namen, --leeren (dazu --konfig oder --socket, --json-aus). Nichts geändert.",
            P("scnamen", "--konto", "1", "--name", "Sarevoc Test"), "der Befund");
        Pruefe.Gleich("--namen=Sarevoc: zwischen Option und Wert gehört ein Leerzeichen, kein =, etwa --namen Sarevoc. Nichts geändert.",
            P("scnamen", "--konto", "1", "--namen=Sarevoc"), "= statt Leerzeichen");
        Pruefe.Gleich("\"Test\" gehört zu keiner Option. Werte mit Leerzeichen gehören in Anführungszeichen, etwa --namen \"Max Mustermann\". Nichts geändert.",
            P("scnamen", "--konto", "1", "--namen", "Sarevoc", "Test"), "ohne Anführungszeichen");
        Pruefe.Gleich("--namen darf nur einmal dastehen, hier steht es 2-mal. Nichts geändert.",
            P("scnamen", "--konto", "1", "--namen", "A", "--namen", "B"), "zweimal");
        Pruefe.Gleich("--name braucht einen Wert, dahinter steht aber --leeren. Nichts geändert.",
            P("discordname", "--konto", "1", "--name", "--leeren"), "Wert vergessen");
        Pruefe.Gleich("--name braucht einen Wert. Nichts geändert.", P("discordname", "--konto", "1", "--name"), "Wert am Ende fehlt");
        Pruefe.Gleich("geraete kennt --wartnd nicht. Erlaubt: --konto, --wartend, --nicht-seit (dazu --konfig oder --socket, --json-aus). Nichts geändert.",
            P("geraete", "--wartnd"), "vertippter Schalter am Ende");
        Pruefe.Gleich("status braucht keine Optionen (dazu --konfig oder --socket, --json-aus).",
            P("status", "--konto", "1")?.Split(". ", 2)[1].Replace(" Nichts geändert.", "", StringComparison.Ordinal), "Befehl ohne Optionen");
        Pruefe.Gleich("totp-schluessel-erzeugen kennt --json-aus nicht. Erlaubt: --datei, --ersetzen. Nichts geändert.",
            P("totp-schluessel-erzeugen", "--datei", "t.bin", "--json-aus"), "offline ohne JSON");
        Pruefe.Gleich("freigeben nimmt nur ein Gerät, \"EFGH\" ist zu viel. Nichts geändert.", P("freigeben", "ABCD", "EFGH"), "zwei Geräte");
        Pruefe.Gleich("freigeben: Das Gerät steht zweimal da, vorn und mit --geraet. Nichts geändert.", P("freigeben", "ABCD", "--geraet", "EFGH"), "Gerät doppelt");
        Pruefe.Gleich("Unbekannter Befehl: scname. funk-admin hilfe zeigt alle.", P("scname", "--konto", "1"), "vertippter Befehl");
    }

    [Test]
    public static void AdminWerkzeug_Uebersetzen_LeerenNurAusdruecklich()
    {
        static (string, JsonObject) U(params string[] a) => Uebersetzung.Uebersetzen(new Befehlszeile(a, Befehlsoptionen.Schalter));
        var (_, a) = U("scnamen", "--konto", "4", "--namen", " Sarevoc , Otto ,");
        Pruefe.Gleich("{\"konto\":4,\"namen\":[\"Sarevoc\",\"Otto\"]}", a.ToJsonString(), "Namen getrimmt, leere Teile fort");
        (_, a) = U("scnamen", "--konto", "4", "--leeren");
        Pruefe.Gleich("{\"konto\":4,\"leeren\":true,\"namen\":[]}", a.ToJsonString(), "leeren nur ausdrücklich");
        (_, a) = U("rechte", "--konto", "4", "--rechte", "ZUWEISEN");
        Pruefe.Gleich("{\"konto\":4,\"rechte\":[\"ZUWEISEN\"]}", a.ToJsonString(), "rechte");
        foreach (var (was, args, text) in new[]
        {
            ("ohne --namen", new[] { "scnamen", "--konto", "4" }, "--namen fehlt, etwa --namen \"Mustermann,Max\". Alle SC-Namen wegnehmen geht nur mit --leeren. Nichts geändert."),
            ("--namen leer", ["scnamen", "--konto", "4", "--namen", " , "], "--namen ist leer. Alle SC-Namen wegnehmen geht nur mit --leeren. Nichts geändert."),
            ("beides", ["scnamen", "--konto", "4", "--namen", "A", "--leeren"], "--namen und --leeren zusammen geht nicht. Nichts geändert."),
            ("ohne --rechte", ["rechte", "--konto", "4"], "--rechte fehlt, etwa --rechte ZUWEISEN,GLOBAL_SENDEN. Alle Rechte wegnehmen geht nur mit --leeren. Nichts geändert."),
        })
        {
            var e = Pruefe.Wirft<ArgumentException>(() => U(args), was);
            Pruefe.Gleich(text, e.Message, was);
        }
    }

    /// <summary>
    /// A62 und A64 (Sarevoc, 01.10.2026): Ab 0.5.0 legt <c>netz-setzen</c> ohne Schalter ein Netz mit Sprachaktivierung an, Taste nur
    /// ausdrücklich mit <c>--taste</c> (oder <c>--nur-taste</c>); <c>--va</c> bleibt erlaubt und wirkt wie ohne Schalter, damit die
    /// Befehle aus den Anleitungen für 0.4.x weiter stimmen. Global (Netz 1) bleibt immer Taste. Was nicht zusammenpasst, lehnt
    /// funk-admin ab, bevor der Server etwas hört.
    /// </summary>
    [Test]
    public static void AdminWerkzeug_NetzSetzen_SprachaktivierungIstStandard()
    {
        static JsonObject Netz(params string[] a) =>
            (JsonObject)Uebersetzung.Uebersetzen(new Befehlszeile(["netz-setzen", .. a], Befehlsoptionen.Schalter)).Item2["objekt"]!;
        static string Ausloeser(JsonObject n) => (string)n["ausloeserStandard"]!;
        static bool NurTaste(JsonObject n) => (bool)n["nurTaste"]!;

        var ohne = Netz("--id", "7", "--name", "Crew 3", "--offen", "HOEREN_SENDEN");
        Pruefe.Wahr(Ausloeser(ohne) == "VA" && !NurTaste(ohne), "ohne Schalter: Sprachaktivierung (" + ohne.ToJsonString() + ")");
        var va = Netz("--id", "90", "--name", "Echo", "--offen", "HOEREN_SENDEN", "--va");
        Pruefe.Wahr(Ausloeser(va) == "VA" && !NurTaste(va), "--va wie ohne Schalter (Anleitungen bis 0.4.x)");
        var taste = Netz("--id", "2", "--name", "Kommando", "--offen", "HOEREN_SENDEN", "--standard", "--taste");
        Pruefe.Wahr(Ausloeser(taste) == "TASTE" && !NurTaste(taste) && (bool)taste["standard"]!, "--taste: Taste ab Werk, im Client umstellbar");
        var nurTaste = Netz("--id", "4", "--name", "Durchsagen", "--offen", "HOEREN", "--nur-taste");
        Pruefe.Wahr(Ausloeser(nurTaste) == "TASTE" && NurTaste(nurTaste), "--nur-taste: nur Taste, also auch ab Werk Taste");
        var global = Netz("--id", "1", "--name", "Global");
        Pruefe.Wahr(Ausloeser(global) == "TASTE" && NurTaste(global) && (string)global["typ"]! == "GLOBAL", "Global bleibt immer Taste");

        foreach (var (was, args, text) in new[]
        {
            ("beide", new[] { "--id", "2", "--name", "Kommando", "--taste", "--va" }, "--taste und --va zusammen geht nicht: ohne Schalter steht ein Kanal auf Sprachaktivierung, mit --taste auf Taste. Nichts geändert."),
            ("va und nur-taste", ["--id", "4", "--name", "Durchsagen", "--va", "--nur-taste"], "--va und --nur-taste zusammen geht nicht: ein Kanal nur mit Taste steht auch ab Werk auf Taste. Nichts geändert."),
            ("Global mit va", ["--id", "1", "--name", "Global", "--va"], "Global (Kanal 1) bleibt immer auf Taste, --va geht dort nicht. Nichts geändert."),
        })
        {
            var e = Pruefe.Wirft<ArgumentException>(() => Netz(args), was);
            Pruefe.Gleich(text, e.Message, was);
        }
        Pruefe.Gleich(null, Befehlsoptionen.Pruefen(new Befehlszeile(["netz-setzen", "--id", "2", "--name", "Kommando", "--taste"], Befehlsoptionen.Schalter)),
            "--taste steht in der Tabelle");

        // Der Hilfetext nennt --taste und sagt, was ohne Schalter gilt; Kommando steht dort und in den Anleitungen mit --taste.
        var hilfe = Lesen("src", "Funk.Admin", "Hilfe.cs");
        Pruefe.Wahr(hilfe.Contains("[--taste]", StringComparison.Ordinal) && hilfe.Contains("ohne Schalter Sprachaktivierung", StringComparison.Ordinal),
            "Hilfe nennt --taste und die Voreinstellung");
        var command = new List<string>();
        foreach (var datei in new[] { Path.Combine("docs", "HEIMSERVER-PROXMOX.md"), Path.Combine("docs", "BETRIEB.md"), Path.Combine("docs", "EINRICHTEN.md") })
            command.AddRange(FundeInMarkdown(datei).Where(f => f.Text.StartsWith("netz-setzen", StringComparison.Ordinal) && f.Text.Contains("--name Kommando", StringComparison.Ordinal))
                .Select(f => f.Quelle + ": " + f.Text));
        command.AddRange(FundeInDerHilfe().Where(f => f.Text.StartsWith("netz-setzen", StringComparison.Ordinal) && f.Text.Contains("--name Kommando", StringComparison.Ordinal))
            .Select(f => f.Quelle + ": " + f.Text));
        Pruefe.Wahr(command.Count >= 4, "Kommando steht in HEIMSERVER-PROXMOX.md, BETRIEB.md, EINRICHTEN.md und der Hilfe: " + string.Join(" | ", command));
        Pruefe.Gleich("", string.Join("\n", command.Where(c => !c.Contains("--taste", StringComparison.Ordinal))), "Kommando überall mit --taste");
    }

    /// <summary>Eine Stelle, an der ein funk-admin-Befehl steht; <c>Vollstaendig</c> heißt: so zum Abschreiben gedacht.</summary>
    private sealed record Fund(string Quelle, string Text, bool Vollstaendig, bool MitPraefix);

    private static readonly Regex Praefix = new(@"funk-admin(?:\.dll)?\s+", RegexOptions.None, Frist);

    private static List<Fund> FundeInMarkdown(string datei)
    {
        var zeilen = File.ReadAllLines(Path.Combine(Wurzel(), datei));
        var imBlock = false;
        var absatz = new StringBuilder();
        var absatzZeile = 0;
        var funde = new List<Fund>();

        void Spannen(string text, string quelle, bool ersteZelle)
        {
            string? vorige = null;
            foreach (Match m in Regex.Matches(text, "`([^`]+)`", RegexOptions.None, Frist))
            {
                var s = m.Groups[1].Value.Trim();
                // In der Befehlstabelle gehört "oder `--json`" zum Befehl davor.
                if (ersteZelle && s.StartsWith("--", StringComparison.Ordinal) && vorige is not null)
                {
                    funde.Add(new Fund(quelle, vorige + " " + s, false, false));
                    continue;
                }
                var p = Praefix.Match(s);
                if (p.Success) funde.Add(new Fund(quelle, s[(p.Index + p.Length)..], false, true));
                else funde.Add(new Fund(quelle, s, false, false));
                vorige = p.Success ? s[(p.Index + p.Length)..] : s;
            }
        }

        void AbsatzFertig()
        {
            if (absatz.Length > 0) Spannen(absatz.ToString(), $"{datei}:{absatzZeile}", false);
            absatz.Clear();
        }

        for (var i = 0; i < zeilen.Length; i++)
        {
            var zeile = zeilen[i];
            if (zeile.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                AbsatzFertig();
                imBlock = !imBlock;
                continue;
            }
            if (imBlock)
            {
                var start = i;
                var text = zeile;
                while (text.TrimEnd().EndsWith('\\') && i + 1 < zeilen.Length) text = text.TrimEnd()[..^1] + " " + zeilen[++i].Trim();
                var p = Praefix.Match(text);
                var raute = text.IndexOf('#', StringComparison.Ordinal);
                if (p.Success && (raute < 0 || raute > p.Index))
                {
                    var befehl = text[(p.Index + p.Length)..];
                    var kommentar = Regex.Match(befehl, @"\s#\s", RegexOptions.None, Frist);
                    if (kommentar.Success) befehl = befehl[..kommentar.Index];
                    funde.Add(new Fund($"{datei}:{start + 1}", befehl.Trim(), true, true));
                }
                continue;
            }
            if (zeile.TrimStart().StartsWith('|'))
            {
                AbsatzFertig();
                var zellen = Regex.Split(zeile.Trim().Trim('|'), @"(?<!\\)\|", RegexOptions.None, Frist);
                for (var z = 0; z < zellen.Length; z++) Spannen(zellen[z], $"{datei}:{i + 1}", z == 0);
                continue;
            }
            if (string.IsNullOrWhiteSpace(zeile))
            {
                AbsatzFertig();
                continue;
            }
            if (absatz.Length == 0) absatzZeile = i + 1;
            absatz.Append(' ').Append(zeile.Trim());
        }
        AbsatzFertig();
        return funde;
    }

    private static IEnumerable<Fund> FundeImText(string datei)
    {
        var zeilen = File.ReadAllLines(Path.Combine(Wurzel(), datei));
        for (var i = 0; i < zeilen.Length; i++)
        {
            foreach (Match p in Praefix.Matches(zeilen[i]))
                yield return new Fund($"{datei}:{i + 1}", zeilen[i][(p.Index + p.Length)..], false, true);
        }
    }

    /// <summary>Der Hilfetext: Zeilen mit mehreren Befehlen, durch drei und mehr Leerzeichen getrennt; Fortsetzungen mit "--".</summary>
    private static List<Fund> FundeInDerHilfe()
    {
        var quelle = Lesen("src", "Funk.Admin", "Hilfe.cs");
        var von = quelle.IndexOf("\"\"\"", StringComparison.Ordinal) + 3;
        var bis = quelle.IndexOf("\"\"\"", von, StringComparison.Ordinal);
        var funde = new List<Fund>();
        string? vorige = null;
        foreach (var zeile in quelle[von..bis].Split('\n'))
        {
            foreach (var teil in Regex.Split(zeile.Trim(), @"\s{3,}", RegexOptions.None, Frist))
            {
                if (teil.Length == 0 || teil.StartsWith('(') || teil.StartsWith("funk-admin", StringComparison.Ordinal)) continue;
                if (teil.StartsWith("--", StringComparison.Ordinal) && vorige is not null)
                {
                    funde.Add(new Fund("Hilfe.cs", vorige + " " + teil, false, true));
                    continue;
                }
                var erstes = teil.Split(' ')[0];
                if (erstes.Split('|').All(Befehlsoptionen.Befehle.ContainsKey))
                {
                    funde.Add(new Fund("Hilfe.cs", teil, false, true));
                    vorige = erstes;
                }
            }
        }
        return funde;
    }

    private static List<string> Woerter(string text) =>
        [.. Regex.Matches(text, @"(?:<[^>]*>|""[^""]*""|'[^']*'|[^\s<""'])+", RegexOptions.None, Frist).Select(m => m.Value)];

    /// <summary>Prüft einen Fund; <c>null</c>, wenn er passt oder kein funk-admin-Befehl ist, sonst was nicht passt.</summary>
    private static string? Urteil(Fund f, out bool geprueft)
    {
        geprueft = false;
        var woerter = Woerter(f.Text);
        // Was hinter einer Pipe, einer Umleitung oder einem ";" steht, gehört der Shell, nicht funk-admin (Abschnitt 18: netzliste | awk,
        // for ... do funk-admin ...; done).
        for (var i = 0; i < woerter.Count; i++)
        {
            if (woerter[i] is "|" or ">" or ">>")
            {
                woerter.RemoveRange(i, woerter.Count - i);
                break;
            }
            if (woerter[i].Length > 1 && woerter[i].EndsWith(';'))
            {
                woerter[i] = woerter[i][..^1];
                woerter.RemoveRange(i + 1, woerter.Count - i - 1);
                break;
            }
        }
        if (woerter.Count == 0) return null;
        // Eine zitierte Ablehnung ("scnamen kennt --name nicht. ...") ist eine Antwort, kein Befehl.
        if (f.Text.Contains(" kennt --", StringComparison.Ordinal)) return null;
        var erstes = woerter[0].Trim('[', ']', '(', ')', '.', ',', ';', ':');
        if (erstes.StartsWith('<') || erstes is "hilfe" or "<befehl>") return null;
        var befehle = erstes.Split('|');
        // Im Fließtext kann auf "funk-admin" auch ein gewöhnliches Wort folgen; zum Abschreiben muss es ein Befehl sein.
        if (!befehle.All(Befehlsoptionen.Befehle.ContainsKey)) return f.Vollstaendig ? $"{f.Quelle}: unbekannter Befehl {erstes}" : null;
        if (!f.MitPraefix && !woerter.Any(w => w.TrimStart('[').StartsWith("--", StringComparison.Ordinal))) return null;
        geprueft = true;

        if (f.Vollstaendig)
        {
            // So, wie Sarevoc die Zeile abschreibt: Anführungszeichen nimmt die Shell weg, ein Platzhalter ist ein Wert.
            var args = woerter.Select(w => w.Length >= 2 && (w[0] == '"' || w[0] == '\'') && w[^1] == w[0] ? w[1..^1] : w).ToList();
            var falsch = Befehlsoptionen.Pruefen(new Befehlszeile(args, Befehlsoptionen.Schalter));
            return falsch is null ? null : $"{f.Quelle}: {f.Text} -> {falsch}";
        }
        foreach (var w in woerter.Skip(1))
        {
            var o = w.Trim('[', ']');
            if (!o.StartsWith("--", StringComparison.Ordinal)) continue;
            var name = o[2..].TrimEnd('.', ',', ';', ':', ')', '`');
            if (name.Length == 0) continue;
            if (name.Contains('=', StringComparison.Ordinal)) return $"{f.Quelle}: {f.Text} -> = in --{name}";
            if (!befehle.All(b => Befehlsoptionen.Befehle[b].Kennt(name))) return $"{f.Quelle}: {f.Text} -> {erstes} kennt --{name} nicht";
        }
        return null;
    }

    [Test]
    public static void AdminWerkzeug_AnleitungenNutzenNurBekannteOptionen()
    {
        var funde = new List<Fund>();
        funde.AddRange(FundeInMarkdown(Path.Combine("docs", "HEIMSERVER-PROXMOX.md")));
        funde.AddRange(FundeInMarkdown(Path.Combine("docs", "BETRIEB.md")));
        // Die Checkliste der Härtung nennt sperrliste und entsperren zum Abschreiben (seit 0.3.0).
        funde.AddRange(FundeInMarkdown(Path.Combine("docs", "HEIMSERVER-HAERTUNG.md")));
        // Einrichten und Umzug (A55): Der Server-Admin und Sarevoc schreiben dort ab, das Skript ruft funk-admin auf.
        funde.AddRange(FundeInMarkdown(Path.Combine("docs", "EINRICHTEN.md")));
        funde.AddRange(FundeInMarkdown(Path.Combine("docs", "UMZUG.md")));
        // Das Handbuch (docs\handbuch, seit 0.5.0 dabei): Geprüft werden die Befehle zum Abschreiben in den Codeblöcken. Nicht dazu gehören die
        // Syntaxzeilen mit Klammern ("[--offen NEIN|HOEREN]"), Bilder des Aufbaus, die Kommentarzeile einer erzeugten server.json
        // ("<JJJJ-MM-TT ...>") und Tabellen und Fließtext, die ein Befehlswort nur erwähnen.
        foreach (var d in Directory.EnumerateFiles(Path.Combine(Wurzel(), "docs", "handbuch"), "*.md").Order(StringComparer.Ordinal))
            funde.AddRange(FundeInMarkdown(Path.Combine("docs", "handbuch", Path.GetFileName(d))).Where(f =>
                f.Vollstaendig && !f.Text.Contains('[') && !f.Text.Contains("<JJJJ", StringComparison.Ordinal)
                && Befehlsoptionen.Befehle.ContainsKey(f.Text.Split(' ')[0])));
        funde.AddRange(FundeImText(Path.Combine("konfig", "funk-einrichten.sh")));
        funde.AddRange(FundeImText(Path.Combine("tools", "paket", "Profil erneuern.cmd")));
        funde.AddRange(FundeImText(Path.Combine("tools", "paket", "LIESMICH-Server.txt")));
        funde.AddRange(FundeImText(Path.Combine("tools", "paket", "LIESMICH.txt")));
        var hilfe = FundeInDerHilfe().ToList();
        funde.AddRange(hilfe);

        var fehler = new List<string>();
        var geprueft = 0;
        var vollstaendig = 0;
        foreach (var f in funde)
        {
            if (Urteil(f, out var g) is { } falsch) fehler.Add(falsch);
            if (!g) continue;
            geprueft++;
            if (f.Vollstaendig) vollstaendig++;
        }
        Pruefe.Gleich("", string.Join("\n", fehler), "Befehle in den Anleitungen, die funk-admin ablehnen würde");

        // Zitierte Ablehnungen stehen so da, wie funk-admin sie schreibt.
        var zitate = 0;
        foreach (var f in funde)
        {
            var m = Regex.Match(f.Text, @"^(\S+) kennt --(\S+) nicht\.", RegexOptions.None, Frist);
            if (!m.Success) continue;
            zitate++;
            var echt = Befehlsoptionen.Pruefen(new Befehlszeile([m.Groups[1].Value, "--" + m.Groups[2].Value, "x"], Befehlsoptionen.Schalter));
            Pruefe.Gleich(echt, f.Text, f.Quelle + ": Zitat wie die Ausgabe");
        }
        Pruefe.Wahr(zitate >= 2, "Zitate in HEIMSERVER-PROXMOX.md und BETRIEB.md geprüft: " + zitate);
        Pruefe.Wahr(vollstaendig >= 30 && geprueft >= 90, $"genug geprüft: {vollstaendig} zum Abschreiben, {geprueft} insgesamt");
        Pruefe.Wahr(funde.Any(f => f.Vollstaendig && f.Text.StartsWith("scnamen --konto <deine id> --namen", StringComparison.Ordinal)),
            "Abschnitt 14, Schritt 10 (scnamen) ist dabei");
        Pruefe.Wahr(funde.Any(f => f.Vollstaendig && f.Quelle.StartsWith(Path.Combine("docs", "HEIMSERVER-PROXMOX.md"), StringComparison.Ordinal)
            && f.Text.StartsWith("auftrag --konto <id>", StringComparison.Ordinal)), "Abschnitt 15, Schritt 11 (auftrag) ist dabei");
        Pruefe.Wahr(funde.Any(f => f.Vollstaendig && f.Quelle.StartsWith(Path.Combine("docs", "HEIMSERVER-PROXMOX.md"), StringComparison.Ordinal)
            && f.Text.StartsWith("pruefen --vollstaendig --konfig", StringComparison.Ordinal)), "Abschnitt 16, Schritt 7 (pruefen --vollstaendig) ist dabei");
        Pruefe.Wahr(funde.Any(f => f.Vollstaendig && f.Quelle.StartsWith(Path.Combine("docs", "HEIMSERVER-HAERTUNG.md"), StringComparison.Ordinal)
            && f.Text.StartsWith("entsperren", StringComparison.Ordinal)), "HEIMSERVER-HAERTUNG.md (entsperren) ist dabei");

        // Der Hilfetext nennt jeden Befehl der Tabelle.
        var genannt = hilfe.SelectMany(f => Woerter(f.Text)[0].Split('|')).ToHashSet(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(Lesen("src", "Funk.Admin", "Hilfe.cs"), @"\|\s*([a-z-]+)", RegexOptions.None, Frist)) genannt.Add(m.Groups[1].Value);
        foreach (var befehl in Befehlsoptionen.Befehle.Keys)
            Pruefe.Wahr(genannt.Contains(befehl), "Hilfe nennt " + befehl);
    }
}
