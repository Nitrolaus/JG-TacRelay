// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Funk.ClientKern;
using Funk.ClientKern.Echo;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Offene Befunde aus PROJEKT.md, Nächste Schritte 0, 6 und 7 (Fassung 0.3.0, Strang Härtung): Rennen Antrag/WARTERAUM,
/// Uhrprüfung beim Start, Journal im eigenen Faden, Konsole (admins, antraege mit passendem Konto, geraet-umhaengen und der
/// Reparaturweg nach Sarevocs Antrag ins falsche Konto), Adressmeldung ohne Netlink, funk-echo nach Bye{VERDRAENGT}, UTF-8 auf der
/// Windows-Konsole, Setup und neutrale Baupfade. Dazu Anleitungsblöcke, so abgearbeitet, wie Sarevoc sie abschreibt (Weg A,
/// Zeile in server.json, Reihenfolge in HEIMSERVER-HAERTUNG.md; Befunde der Prüfung vom 01.10.2026).
/// </summary>
internal static class HaertungTests
{
    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new PruefungException("Repository nicht gefunden");
    }

    private static IPAddress Lokal(int n) => IPAddress.Parse("127.0.3." + n.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static async Task WartenBisAsync(Func<bool> bedingung, TimeSpan frist, string was)
    {
        var bis = DateTime.UtcNow + frist;
        while (!bedingung() && DateTime.UtcNow < bis) await Task.Delay(25);
        Pruefe.Wahr(bedingung(), was);
    }

    // ---------------------------------------------------------------- Rennen Antrag/WARTERAUM

    /// <summary>
    /// Ein automatischer Admin-Client gibt binnen Millisekunden frei, noch bevor die Verbindung des Antragstellers ihre Antwort
    /// und GeraetStatus{WARTET} hinausgeschickt hat und im WARTERAUM steht. Die Zeitlupe dehnt genau dieses Fenster. Im Draht muss
    /// trotzdem stehen: Antwort, WARTET, FREIGEGEBEN, Welcome{FREIGEGEBEN}.
    /// </summary>
    [Test]
    public static async Task Antrag_SchnelleFreigabe_ReihenfolgeImDraht()
    {
        Verbindung.AntragZeitlupeMs = 1500;
        try
        {
            await using var t = await Testserver.StartenAsync();
            await using var f = await t.VerbindenAsync(null, Lokal(1));
            await f.HelloAsync();
            var nr = f.NaechsteNr();
            await f.SendenAsync(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = "PC Eilig", ScName = "Eilig", DiscordName = "Eilig/Test" }, nr);
            await WartenBisAsync(() => t.Dienst.Zentrale.Konten.Stand.GeraetZuFingerabdruck(f.Fingerabdruck) is not null, TimeSpan.FromSeconds(5), "Antrag im Kontenbuch");
            await t.AdminOk("freigeben", new JsonObject { ["geraet"] = f.Fingerabdruck.AlsBase64() });
            await f.ErwarteAsync(r => r.Typ == Nachrichtentyp.Welcome && r.Json<Welcome>().Status == WelcomeStatus.Freigegeben);
            var v = f.Verlauf;
            var antwort = v.ToList().FindIndex(r => r.Kopf.IstAntwort && r.Kopf.AnfrageNr == nr);
            var wartet = v.ToList().FindIndex(r => r.Typ == Nachrichtentyp.GeraetStatus && r.Json<GeraetStatus>().Status == GeraeteStatus.Wartet);
            var frei = v.ToList().FindIndex(r => r.Typ == Nachrichtentyp.GeraetStatus && r.Json<GeraetStatus>().Status == GeraeteStatus.Freigegeben);
            var welcome = v.ToList().FindIndex(r => r.Typ == Nachrichtentyp.Welcome && r.Json<Welcome>().Status == WelcomeStatus.Freigegeben);
            var folge = string.Join(", ", v.Select(r => r.Typ + (r.Kopf.IstAntwort ? "(Antwort)" : "")));
            Pruefe.Wahr(antwort >= 0 && antwort < wartet && wartet < frei && frei < welcome, "Antwort < WARTET < FREIGEGEBEN < Welcome: " + folge);
        }
        finally
        {
            Verbindung.AntragZeitlupeMs = 0;
        }
    }

    /// <summary>
    /// Ursache der Wackler in den Reparaturwegen unter Last (01.10. und 04.10.2026, "Verbindung getrennt, erwarteter Rahmen kam
    /// nicht. Vorhanden: Fehler ... UNBEKANNT"): Die Freigabe setzt die wartende Verbindung fort. Landete deren Sitzungsstart noch im
    /// selben Durchlauf der Zentrale, stand das neue Konto nicht im Schnappschuss (gespiegelt wurde erst nach dem Commit),
    /// SitzungAktivieren fand es nicht, und der Server trennte mit UNBEKANNT. Die Zeitlupe hält die Arbeitsschleife nach der Freigabe
    /// an, so landet der Start sicher im selben Durchlauf. Seit 0.8.0 spiegelt der Start vorher.
    /// </summary>
    [Test]
    public static async Task Freigabe_SitzungsstartImSelbenDurchlauf_KeinUnbekannt()
    {
        Zentrale.FreigabeZeitlupeMs = 300;
        try
        {
            await using var t = await Testserver.StartenAsync();
            await using var f = await t.VerbindenAsync(null, Lokal(14));
            await f.HelloAsync();
            var a = await f.AnfrageAsync(Nachrichtentyp.Registrierung,
                new RegistrierungAnfrage { Geraetename = "PC Gleichzeitig", ScName = "Gleichzeitig", DiscordName = "Gleichzeitig/Test" });
            Pruefe.Gleich(Nachrichtentyp.Registrierung, a.Typ, "Antrag gestellt");
            await f.ErwarteAsync(Nachrichtentyp.GeraetStatus);
            await t.AdminOk("freigeben", new JsonObject { ["geraet"] = f.Fingerabdruck.AlsBase64() });
            await f.SyncErwartenAsync();
            Pruefe.Falsch(f.Getrennt, "die Verbindung bleibt");
            Pruefe.Wahr(t.Dienst.Zentrale.Schreiber.Aktuell.Konten.ContainsKey(f.KontoId), "das neue Konto steht im Schnappschuss");
        }
        finally
        {
            Zentrale.FreigabeZeitlupeMs = 0;
        }
    }

    // ---------------------------------------------------------------- Uhrprüfung

    private sealed class FesteUhr(DateTimeOffset jetzt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => jetzt;
    }

    [Test]
    public static void Uhr_WarnungenNurBeiOffensichtlichFalscherUhr()
    {
        var jetzt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        Pruefe.Gleich(0, Uhrpruefung.Warnungen(jetzt, new Kernuhr(0, 20_000), jetzt.AddMinutes(-3)).Count, "synchron, Journal älter");
        Pruefe.Gleich(0, Uhrpruefung.Warnungen(jetzt, null, jetzt.AddMinutes(4)).Count, "Journal 4 min voraus: im Spielraum");
        var vergangen = Uhrpruefung.Warnungen(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null, null);
        Pruefe.Wahr(vergangen.Count == 1 && vergangen[0].Contains("offensichtlich falsch", StringComparison.Ordinal) && vergangen[0].Contains("2020-01-01", StringComparison.Ordinal),
            "Uhr vor dieser Fassung: " + string.Join(" | ", vergangen));
        var unsync = Uhrpruefung.Warnungen(jetzt, new Kernuhr(Uhrpruefung.TimeError, 16_000_000), null);
        Pruefe.Wahr(unsync.Count == 1 && unsync[0].Contains("nicht synchronisiert", StringComparison.Ordinal) && unsync[0].Contains("TOTP", StringComparison.Ordinal),
            "Kernel meldet TIME_ERROR: " + string.Join(" | ", unsync));
        var nach = Uhrpruefung.Warnungen(jetzt, null, jetzt.AddHours(2));
        Pruefe.Wahr(nach.Count == 1 && nach[0].Contains("geht nach", StringComparison.Ordinal), "jüngster Journaleintrag 2 h voraus: " + string.Join(" | ", nach));
        if (!OperatingSystem.IsLinux()) Pruefe.Gleich(null, Uhrpruefung.KernLesen(), "außerhalb von Linux kein adjtimex");
        Pruefe.Wahr(Uhrpruefung.Hinweis(null).Contains("nicht lesbar", StringComparison.Ordinal), "Hinweis ohne Kernel: " + Uhrpruefung.Hinweis(null));
        Pruefe.Wahr(Uhrpruefung.Hinweis(new Kernuhr(0, 12_000)).Contains("synchronisiert", StringComparison.Ordinal), "Hinweis synchron");
    }

    [Test]
    public static async Task Uhr_StartWarntBeiFalscherUhr()
    {
        await using var t = await Testserver.StartenAsync(zeit: new FesteUhr(new DateTimeOffset(2021, 3, 4, 5, 6, 7, TimeSpan.Zero)));
        Pruefe.Wahr(t.Logbuch.Any(z => z.Contains("WARNUNG", StringComparison.Ordinal) && z.Contains("Uhr", StringComparison.Ordinal) && z.Contains("2021-03-04", StringComparison.Ordinal)),
            "Startwarnung: " + string.Join("\n", t.Logbuch.Where(z => z.Contains("Uhr", StringComparison.Ordinal))));
    }

    // ---------------------------------------------------------------- Journal im eigenen Faden

    [Test]
    public static async Task Journal_SpeichernImEigenenFaden_UndInDerRichtigenReihenfolge()
    {
        var ordner = Path.Combine(Testserver.Wurzel(), "journal-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(ordner);
        try
        {
            var pfad = Path.Combine(ordner, Journal.Datei);
            using var halten = new ManualResetEventSlim(false);
            var schreibfaeden = new ConcurrentQueue<int>();
            var j = new Journal(pfad, (p, b) =>
            {
                schreibfaeden.Enqueue(Environment.CurrentManagedThreadId);
                halten.Wait(TimeSpan.FromSeconds(20));
                Dateiablage.AtomarSchreiben(p, b);
            });
            for (var i = 0; i < 2000; i++) j.Eintragen(1_790_000_000_000 + i, "TEST", 0, null, "Eintrag " + i);
            var faden = Environment.CurrentManagedThreadId;
            var aufruf = Task.Run(() => { _ = j.SpeichernImHintergrund(); });
            Pruefe.Wahr(aufruf.Wait(TimeSpan.FromSeconds(5)), "kehrt zurück, während das Schreiben noch hängt (Arbeitsschleife blockiert nicht)");
            Pruefe.Falsch(j.Geaendert, "Stand übergeben");
            j.Eintragen(1_790_000_010_000, "TEST", 0, null, "danach");
            Pruefe.Wahr(j.Geaendert, "neuer Eintrag: wieder geändert");
            // Sicherung oder Beenden schreiben sofort; sie warten auf den laufenden Hintergrund und überschreiben nie Neueres mit Älterem.
            var sofort = Task.Run(j.Speichern);
            await Task.Delay(200);
            halten.Set();
            await sofort.WaitAsync(TimeSpan.FromSeconds(10));
            await j.WartenAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Pruefe.Falsch(schreibfaeden.Contains(faden), "geschrieben wird nicht im aufrufenden Faden");
            var wieder = new Journal(pfad);
            Pruefe.Gleich(2001, wieder.Anzahl, "der neueste Stand liegt auf der Platte, nicht der ältere aus dem Hintergrund");
            Pruefe.Gleich(DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_010_000), wieder.Juengster, "jüngster Eintrag");
        }
        finally
        {
            Directory.Delete(ordner, recursive: true);
        }
    }

    /// <summary>
    /// Was HEIMSERVER-PROXMOX.md, Abschnitt 14, Schritt 6 über die Meldung sagt: Ein Syntaxfehler nennt die Zeile, ein vertippter
    /// Feldname nur den Namen (Befund der unabhängigen Prüfung vom 01.10.2026).
    /// </summary>
    [Test]
    public static void Konfig_MeldungNenntZeileOderNurDenNamen()
    {
        var ordner = Path.Combine(Testserver.Wurzel(), "konfig-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(ordner);
        try
        {
            string Meldung(string json)
            {
                var pfad = Path.Combine(ordner, "server.json");
                File.WriteAllText(pfad, json);
                return Pruefe.Wirft<InvalidDataException>(() => ServerKonfiguration.Laden(pfad), "Laden").Message;
            }
            var komma = Meldung("{\n  \"binden\": [\"127.0.0.1\"]\n  \"profil\": \"p.json\"\n}");
            // Gezählt ab 0: Der Fehler steht in der dritten Zeile (nach dem fehlenden Komma), die Meldung sagt 2.
            Pruefe.Wahr(komma.Contains("LineNumber: 2", StringComparison.Ordinal), "fehlendes Komma: Zeile genannt, ab 0 gezählt: " + komma);
            var name = Meldung("{\n  \"binden\": [\"127.0.0.1\"],\n  \"totpSchlussel\": \"t.bin\"\n}");
            Pruefe.Wahr(name.Contains("'totpSchlussel' could not be mapped", StringComparison.Ordinal) && !name.Contains("LineNumber", StringComparison.Ordinal),
                "vertippter Name: nur der Name: " + name);
        }
        finally
        {
            Directory.Delete(ordner, recursive: true);
        }
    }

    // ---------------------------------------------------------------- Konsole

    [Test]
    public static void Konsole_AdminsZeigtFehlversucheUndPause()
    {
        var jetzt = new DateTimeOffset(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);
        var e = new I.AdminEintrag
        {
            KontoId = 7,
            Zustand = I.AdminZustand.Aktiv,
            Fehlversuche = [jetzt.AddMinutes(-3), jetzt.AddMinutes(-2), jetzt.AddMinutes(-1)],
            PauseBis = jetzt.AddMinutes(14),
        };
        var zeile = Zentrale.AdminZeile("Sarevoc (Konto 7)", e, jetzt);
        Pruefe.Wahr(zeile.Contains("Fehlversuche 3", StringComparison.Ordinal) && zeile.Contains("Pause bis 18:14 UTC", StringComparison.Ordinal), zeile);
        var ruhig = Zentrale.AdminZeile("Sarevoc (Konto 7)", e with { Fehlversuche = [], PauseBis = jetzt.AddMinutes(-1) }, jetzt);
        Pruefe.Falsch(ruhig.Contains("Fehlversuche", StringComparison.Ordinal) || ruhig.Contains("Pause", StringComparison.Ordinal), "ohne Fehlversuche und abgelaufene Pause: " + ruhig);
    }

    [Test]
    public static void Kontovorschlag_PasstUeberNamenUndBenutzernamen()
    {
        Pruefe.Wahr(I.Kontovorschlag.Passt(["Sarevoc", "Sarevoc/Otto"], ["Max", "sarevoc", null]), "Konto aus 0.1.0: Benutzername sarevoc");
        Pruefe.Wahr(I.Kontovorschlag.Passt(["Sarevoc", "Sarevoc/Otto"], ["Sarevoc", null, null]), "Anzeigename");
        Pruefe.Wahr(I.Kontovorschlag.Passt(["Brandy", "Brandy/Lea"], ["Brandy/Lea"]), "Discord-Name");
        Pruefe.Wahr(I.Kontovorschlag.Passt(["ＳＡＲＥＶＯＣ", "x/y"], ["sarevoc"]), "NFKC und Groß/Klein wie die Namensschlüssel");
        Pruefe.Falsch(I.Kontovorschlag.Passt(["Brandy", "Brandy/Lea"], ["Falke", "Falke/Tom"]), "andere Namen");
        Pruefe.Falsch(I.Kontovorschlag.Passt(["Otto", "Otto"], ["Sarevoc/Otto"]), "der Rufname hinter dem Schrägstrich allein reicht nicht");
        Pruefe.Falsch(I.Kontovorschlag.Passt(["/", " "], ["/"]), "leere Teile passen nie");
    }

    /// <summary>Was Sarevoc am 01.10.2026 früh passierte: Konto aus 0.1.0 mit ADMIN, Antrag mit "Sarevoc/Otto", freigegeben als neues Konto.</summary>
    private static async Task<(Testfunker Geraet, uint Alt, uint Neu)> FallSarevocAsync(Testserver t, int lokal, bool eigentuemer = true)
    {
        var alt = (uint)(await t.AdminOk("konto-anlegen", new JsonObject { ["discord"] = "930000000000000001", ["benutzername"] = "sarevoc", ["name"] = "Sarevoc", ["stufe"] = "ADMIN" }))
            .Daten!["kontoId"]!.GetValue<uint>();
        // Ob Sarevoc in Abschnitt 14, Schritt 10 schon "eigentuemer" gesetzt hat, ist nicht bekannt; Weg A muss in beiden Fällen halten.
        if (eigentuemer) await t.AdminOk("eigentuemer", new JsonObject { ["konto"] = alt });
        var f = await t.VerbindenAsync(null, Lokal(lokal));
        await f.HelloAsync();
        var a = await f.AnfrageAsync(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = "Sarevocs PC", ScName = "Sarevoc", DiscordName = "Sarevoc/Otto" });
        Pruefe.Gleich(Nachrichtentyp.Registrierung, a.Typ, "Antrag gestellt");
        await f.ErwarteAsync(Nachrichtentyp.GeraetStatus);
        var antraege = await t.AdminOk("antraege");
        Pruefe.Wahr(antraege.Text.Contains("BESTEHENDES KONTO?", StringComparison.Ordinal) && antraege.Text.Contains("Konto " + alt, StringComparison.Ordinal)
            && antraege.Text.Contains("ADMIN", StringComparison.Ordinal) && antraege.Text.Contains("--an-konto " + alt, StringComparison.Ordinal),
            "antraege bietet das bestehende Konto an: " + antraege.Text);
        // Gegenprüfung: Die Namen eines Antrags wählt der Antragsteller selbst; der Hinweis erscheint also gerade bei einem
        // Nachahmer. Er verlangt deshalb den Vergleich der Kennung mit dem Bildschirm des Geräts, bevor angehängt wird.
        Pruefe.Wahr(antraege.Text.Contains("nur wenn sein neues Gerät dieselbe Kennung zeigt", StringComparison.Ordinal),
            "Hinweis verlangt den Kennungsvergleich: " + antraege.Text);
        Pruefe.Wahr(antraege.Daten is JsonArray { Count: 1 } liste && liste[0]!["konten"] is JsonArray { Count: 1 } k && k[0]!.GetValue<uint>() == alt, "JSON nennt das Konto");
        // So geschah es: freigegeben ohne --an-konto.
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = f.Fingerabdruck.AlsBase64() });
        await f.SyncErwartenAsync();
        var neu = f.KontoId;
        Pruefe.Wahr(neu != alt && t.Dienst.Zentrale.Konten.Stand.Konten[neu].Stufe == I.Stufe.Funker, "neues Konto als FUNKER");
        return (f, alt, neu);
    }

    [Test]
    public static async Task Konsole_ReparaturwegA_GeraetAnsAlteKontoHaengen()
    {
        await using var t = await Testserver.StartenAsync();
        var (f, alt, neu) = await FallSarevocAsync(t, 10);
        var parameter = f.Schluessel.ExportParameters(true);
        await using (f)
        {
            var anzeige = f.Fingerabdruck.Anzeige;
            var falsch = await t.Admin("geraet-umhaengen", new JsonObject { ["geraet"] = anzeige, ["anKonto"] = 99999 });
            Pruefe.Falsch(falsch.Ok, "unbekanntes Zielkonto: " + falsch.Text);
            var ok = await t.AdminOk("geraet-umhaengen", new JsonObject { ["geraet"] = anzeige, ["anKonto"] = alt });
            Pruefe.Wahr(ok.Text.Contains("konto-loeschen --konto " + neu, StringComparison.Ordinal), "nennt den nächsten Schritt: " + ok.Text);
            await f.WartenAufTrennungAsync(TimeSpan.FromSeconds(5));
            var stand = t.Dienst.Zentrale.Konten.Stand;
            Pruefe.Gleich(alt, stand.GeraetZuFingerabdruck(f.Fingerabdruck)!.KontoId, "Gerät gehört dem alten Konto");
            Pruefe.Gleich(I.GeraetStatus.Freigegeben, stand.GeraetZuFingerabdruck(f.Fingerabdruck)!.Status, "bleibt freigegeben");
            var journal = await t.AdminOk("journal");
            Pruefe.Wahr(journal.Text.Contains("GERAET_UMGEHAENGT", StringComparison.Ordinal), "im Journal");
        }
        // Wiederverbinden mit demselben Schlüssel: jetzt das ADMIN-Konto.
        var schluessel = System.Security.Cryptography.ECDsa.Create(parameter);
        await using var wieder = await t.VerbindenAsync(schluessel, Lokal(10));
        var w = await wieder.HelloAsync();
        Pruefe.Gleich(WelcomeStatus.Freigegeben, w.Status, "Welcome FREIGEGEBEN");
        Pruefe.Gleich(alt, w.Konto!.Id, "angemeldet im alten Konto");
        await t.AdminOk("konto-loeschen", new JsonObject { ["konto"] = neu });
        await t.AdminOk("discordname", new JsonObject { ["konto"] = alt, ["name"] = "Sarevoc/Otto" });
        await t.AdminOk("scnamen", new JsonObject { ["konto"] = alt, ["namen"] = new JsonArray("Sarevoc") });
        var k = t.Dienst.Zentrale.Konten.Stand.Konten[alt];
        Pruefe.Wahr(k.Stufe == I.Stufe.Admin && k.Eigentuemer && k.DiscordName == "Sarevoc/Otto" && k.ScNamen.Contains("Sarevoc"), "altes Konto vollständig");

        // Nur die Konsole hängt um.
        var ergebnis = t.Dienst.Zentrale.Konten.GeraetUmhaengen(I.Akteur.Verwaltend(alt, 1, bestaetigt: true), 1, alt);
        Pruefe.Gleich(I.Fehlercode.NurAdminWerkzeug, ergebnis.Fehler, "nicht über den Client");
    }

    [Test]
    public static async Task Konsole_ReparaturwegB_NeuesKontoZumEigentuemer()
    {
        await using var t = await Testserver.StartenAsync();
        var (f, alt, neu) = await FallSarevocAsync(t, 11);
        await using (f)
        {
            await t.AdminOk("stufe", new JsonObject { ["konto"] = neu, ["stufe"] = "ADMIN" });
            var vorher = await t.Admin("konto-loeschen", new JsonObject { ["konto"] = alt });
            Pruefe.Falsch(vorher.Ok, "der Eigentümer lässt sich nicht löschen: " + vorher.Text);
            await t.AdminOk("eigentuemer", new JsonObject { ["konto"] = neu });
            await t.AdminOk("konto-loeschen", new JsonObject { ["konto"] = alt });
            var k = t.Dienst.Zentrale.Konten.Stand.Konten[neu];
            Pruefe.Wahr(k.Stufe == I.Stufe.Admin && k.Eigentuemer && !t.Dienst.Zentrale.Konten.Stand.Konten.ContainsKey(alt), "neues Konto ist ADMIN und Eigentümer, das alte fort");
            await t.AdminOk("admin-vorsehen", new JsonObject { ["konto"] = neu });
        }
    }

    // ---------------------------------------------------------------- Anleitung, so wie Sarevoc sie abschreibt

    private static readonly TimeSpan Regexfrist = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Die funk-admin-Zeilen aus den Codeblöcken eines Stücks Markdown, in Reihenfolge, als Wörter wie nach der Shell
    /// (Anführungszeichen weg, Platzhalter wie <c>&lt;alt&gt;</c> bleiben ein Wort).
    /// </summary>
    private static List<List<string>> AnleitungsBefehle(string text)
    {
        var befehle = new List<List<string>>();
        var imBlock = false;
        foreach (var roh in text.Split('\n'))
        {
            var zeile = roh.TrimEnd('\r');
            if (zeile.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                imBlock = !imBlock;
                continue;
            }
            const string programm = "funk-admin.dll ";
            var p = zeile.IndexOf(programm, StringComparison.Ordinal);
            if (!imBlock || p < 0) continue;
            befehle.Add([.. Regex.Matches(zeile[(p + programm.Length)..], @"(?:<[^>]*>|'[^']*'|""[^""]*""|[^\s<'""])+", RegexOptions.None, Regexfrist)
                .Select(m => m.Value.Length >= 2 && (m.Value[0] == '\'' || m.Value[0] == '"') && m.Value[^1] == m.Value[0] ? m.Value[1..^1] : m.Value)]);
        }
        return befehle;
    }

    /// <summary>
    /// Befund der Prüfung vom 01.10.2026 (mittel): In Weg A stand <c>konto-loeschen --konto &lt;neu&gt;</c> vor
    /// <c>eigentuemer --konto &lt;alt&gt;</c>, und das nur als Satz danach. konto-loeschen fragt an der Konsole nicht nach und
    /// lehnt nur den Eigentümer ab; war Sarevocs altes ADMIN-Konto noch nicht Eigentümer, löschte ein vertauschtes --konto das
    /// ADMIN-Konto samt dem gerade umgehängten Gerät. Hier laufen die Befehle genau so, wie sie unter "Weg A" in
    /// HEIMSERVER-PROXMOX.md stehen, mit und ohne vorher gesetzten Eigentümer. Vor jedem konto-loeschen wird die vertauschte
    /// Zeile versucht, und sie muss scheitern. Dazu: Abschnitt 15, Schritt 7 nennt die Warnung, die der Server ohne Eigentümer
    /// beim Start schreibt, wörtlich.
    /// </summary>
    [Test]
    public static async Task Anleitung_ReparaturwegA_WieGeschriebenUndVertauscht()
    {
        var anleitung = File.ReadAllText(Path.Combine(Wurzel(), "docs", "HEIMSERVER-PROXMOX.md"));
        var von = anleitung.IndexOf("**Weg A, Gerät ans alte Konto", StringComparison.Ordinal);
        var bis = anleitung.IndexOf("**Weg B, das neue Konto übernimmt:**", StringComparison.Ordinal);
        Pruefe.Wahr(von > 0 && bis > von, "Weg A in HEIMSERVER-PROXMOX.md gefunden");
        var befehle = AnleitungsBefehle(anleitung[von..bis]);
        Pruefe.Wahr(befehle.Any(b => b[0] == "konto-loeschen") && befehle.Any(b => b[0] == "geraet-umhaengen"),
            "Weg A hängt um und löscht: " + string.Join(" | ", befehle.Select(b => string.Join(' ', b))));

        foreach (var (mitEigentuemer, lokal) in new[] { (false, 12), (true, 13) })
        {
            var fall = mitEigentuemer ? "Eigentümer schon gesetzt" : "ohne Eigentümer";
            await using var t = await Testserver.StartenAsync();
            var (f, alt, neu) = await FallSarevocAsync(t, lokal, mitEigentuemer);
            string Einsetzen(string w) => w.Replace("<alt>", alt.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("<neu>", neu.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("<gerätekennung>", f.Fingerabdruck.Anzeige, StringComparison.Ordinal)
                // Seit A65 stehen Sarevocs Namen in Weg A als Platzhalter; eingesetzt werden die Namen des Falls (FallSarevocAsync).
                .Replace("<dein Discord-Name>", "Sarevoc/Otto", StringComparison.Ordinal).Replace("<dein SC-Name>", "Sarevoc", StringComparison.Ordinal);
            await using (f)
            {
                foreach (var woerter in befehle)
                {
                    var args = woerter.Select(Einsetzen).ToList();
                    var text = string.Join(' ', args);
                    Pruefe.Falsch(args.Any(w => w.Contains('<', StringComparison.Ordinal)), "kein Platzhalter übrig: " + text);
                    var zeile = new Funk.Admin.Befehlszeile(args, Funk.Admin.Befehlsoptionen.Schalter);
                    Pruefe.Gleich(null, Funk.Admin.Befehlsoptionen.Pruefen(zeile), "funk-admin nimmt die Zeile an: " + text);
                    var (befehl, json) = Funk.Admin.Uebersetzung.Uebersetzen(zeile);
                    if (befehl == "konto-loeschen")
                    {
                        var vertauscht = await t.Admin("konto-loeschen", new JsonObject { ["konto"] = alt });
                        Pruefe.Falsch(vertauscht.Ok, $"{fall}: vertauschtes konto-loeschen --konto {alt} (das ADMIN-Konto) scheitert: {vertauscht.Text}");
                        Pruefe.Wahr(t.Dienst.Zentrale.Konten.Stand.Konten.ContainsKey(alt), fall + ": das ADMIN-Konto ist noch da");
                    }
                    var antwort = await t.AdminOk(befehl, json);
                    if (befehl == "eigentuemer") Pruefe.Gleich($"Eigentümer: Sarevoc (Konto {alt})", antwort.Text, fall + ": Antwort wie in der Anleitung");
                    if (befehl == "geraet-umhaengen") await f.WartenAufTrennungAsync(TimeSpan.FromSeconds(5));
                    if (befehl == "geraete" && args.Contains(neu.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                        Pruefe.Gleich("", antwort.Text, fall + ": geraete --konto <neu> zeigt vor dem Löschen kein Gerät mehr");
                }
            }
            var stand = t.Dienst.Zentrale.Konten.Stand;
            var k = stand.Konten[alt];
            Pruefe.Wahr(k.Stufe == I.Stufe.Admin && k.Eigentuemer && k.DiscordName == "Sarevoc/Otto" && k.ScNamen.Contains("Sarevoc")
                && !stand.Konten.ContainsKey(neu), fall + ": altes Konto ADMIN, Eigentümer, mit Namen; das neue fort");
            Pruefe.Gleich(alt, stand.GeraetZuFingerabdruck(f.Fingerabdruck)!.KontoId, fall + ": Gerät beim alten Konto");

            if (!mitEigentuemer)
            {
                // Ohne Eigentümer warnt der Start; Abschnitt 15, Schritt 7 zitiert die Zeile, damit Sarevoc sie dort erwartet.
                var zitat = Regex.Match(anleitung[anleitung.IndexOf("## 15.", StringComparison.Ordinal)..], "`WARNUNG (Kein Eigentümer gesetzt[^`]*)`",
                    RegexOptions.None, Regexfrist);
                Pruefe.Wahr(zitat.Success, "Abschnitt 15 nennt die Warnung 'Kein Eigentümer gesetzt'");
                // Im Markdown darf das Zitat umbrechen; im Journal ist es eine Zeile.
                var zitiert = Regex.Replace(zitat.Groups[1].Value, @"\s+", " ", RegexOptions.None, Regexfrist);
                Pruefe.Wahr(t.Logbuch.Any(z => z.Contains("WARNUNG " + zitiert, StringComparison.Ordinal)),
                    "der Server schreibt die Warnung so, wie Abschnitt 15 sie zitiert: " + zitiert);
            }
        }
    }

    /// <summary>
    /// Befund der Prüfung vom 01.10.2026 (hoch): HEIMSERVER-HAERTUNG.md 7.2 sagte "mit Komma davor", die gedruckte Zeile endete
    /// selbst mit einem Komma, und wohin sie gehört, stand nirgends. Am Ende eingefügt oder mit zusätzlichem Komma wird
    /// server.json ungültig (Exit 2, gemessen), und der Block startete den Server danach trotzdem neu. Hier kommt die Zeile genau
    /// dorthin, wo die Anleitung sie hinschickt, in eine server.json in Sarevocs Form aus 0.2.0; und kein Codeblock der Anleitungen
    /// startet den Server nach <c>pruefen</c> unbedingt neu.
    /// </summary>
    [Test]
    public static void Anleitung_SperrlistenZeile_AnDerGenanntenStelleGueltig()
    {
        var haertung = File.ReadAllText(Path.Combine(Wurzel(), "docs", "HEIMSERVER-HAERTUNG.md"));
        var von = haertung.IndexOf("### 7.2", StringComparison.Ordinal);
        var bis = haertung.IndexOf("### 7.3", StringComparison.Ordinal);
        Pruefe.Wahr(von > 0 && bis > von, "7.2 gefunden");
        var abschnitt = haertung[von..bis];
        var zeilen = abschnitt.Split('\n').Select(z => z.TrimEnd('\r')).ToList();
        var eintrag = zeilen.Single(z => z.TrimStart().StartsWith("\"sperrliste\":", StringComparison.Ordinal));
        Pruefe.Wahr(abschnitt.Contains("direkt über die Zeile `\"log\": {`", StringComparison.Ordinal), "7.2 nennt die Stelle in server.json");

        var vorlage = File.ReadAllLines(Path.Combine(Wurzel(), "tests", "Funk.Tests", "Daten", "server-0.2.0-heim.json"));
        var log = Array.FindIndex(vorlage, z => z.TrimStart().StartsWith("\"log\": {", StringComparison.Ordinal));
        Pruefe.Wahr(log > 0, "Sarevocs server.json aus 0.2.0 hat \"log\"");
        var ordner = Path.Combine(Testserver.Wurzel(), "konfig-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(ordner);
        try
        {
            var pfad = Path.Combine(ordner, "server.json");
            File.WriteAllLines(pfad, vorlage.Take(log).Append(eintrag).Concat(vorlage.Skip(log)));
            var k = ServerKonfiguration.Laden(pfad);
            Pruefe.Gleich("/var/log/funk/sperren.log", k.Sperrliste.KlartextDatei, "Klartextdatei aus der eingefügten Zeile");
            Pruefe.Wahr(k.Sperrliste.Aktiv, "Sperrliste bleibt an");
        }
        finally
        {
            Directory.Delete(ordner, recursive: true);
        }

        Pruefe.Wahr(zeilen.Any(z => z.Trim() == "dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen && systemctl restart funk-server"),
            "7.2 startet nur nach bestandenem pruefen neu");
        Pruefe.Wahr(abschnitt.Contains("systemctl reset-failed funk-server", StringComparison.Ordinal), "7.2 nennt den Rückweg");
        // Seit 0.4.0 auch die Anleitungen zu Einrichten und Umzug: UMZUG.md, Schritt 1 hatte genau dieses Paar (pruefen, darunter restart).
        foreach (var datei in new[] { "HEIMSERVER-HAERTUNG.md", "HEIMSERVER-PROXMOX.md", "BETRIEB.md", "EINRICHTEN.md", "UMZUG.md" })
        {
            var alle = File.ReadAllLines(Path.Combine(Wurzel(), "docs", datei));
            for (var i = 0; i + 1 < alle.Length; i++)
            {
                if (!alle[i].TrimEnd().EndsWith(" pruefen", StringComparison.Ordinal)) continue;
                Pruefe.Falsch(Regex.IsMatch(alle[i + 1], @"^\s*systemctl (re)?start funk-server", RegexOptions.None, Regexfrist),
                    $"{datei}:{i + 2}: Neustart unbedingt nach pruefen");
            }
        }
    }

    /// <summary>
    /// Befunde der Prüfung vom 01.10.2026 (mittel) zu HEIMSERVER-HAERTUNG.md: limit_req mit dem Jail nginx-limit-req konnte
    /// Spieler nach Strg+F5 eine Stunde aus Foundry sperren, bevor die Anleitung ein größeres burst anbot; fail2ban startet mit
    /// einem Jail ohne Logdatei gar nicht (auch nicht für sshd), und die Pfade im Wurzelverzeichnis der Container gibt es erst,
    /// wenn die Container laufen. Die Reihenfolge steht jetzt fest: erst ein Probelauf ohne Sperre, dann scharf, dann das Jail;
    /// fail2ban startet nach den Containern, und Sarevoc sieht nach, ob es läuft.
    /// </summary>
    [Test]
    public static void Anleitung_Haertung_ErstProbelaufDannSperren()
    {
        var haertung = File.ReadAllText(Path.Combine(Wurzel(), "docs", "HEIMSERVER-HAERTUNG.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        string Abschnitt(string von, string bis)
        {
            var a = haertung.IndexOf(von, StringComparison.Ordinal);
            var b = haertung.IndexOf(bis, a + 1, StringComparison.Ordinal);
            Pruefe.Wahr(a > 0 && b > a, "Abschnitt " + von);
            return haertung[a..b];
        }
        var nginx = Abschnitt("## 5. nginx", "## 6.");
        Pruefe.Wahr(nginx.Contains("limit_req_dry_run on;", StringComparison.Ordinal), "Punkt 5: erst Probelauf (limit_req_dry_run)");
        Pruefe.Wahr(nginx.IndexOf("limit_req_dry_run on;", StringComparison.Ordinal) < nginx.IndexOf("limiting requests", StringComparison.Ordinal),
            "Punkt 5: Probelauf vor dem Nachsehen im error.log");
        var jails = Abschnitt("### 7.1", "### 7.2");
        var limitReq = jails[jails.IndexOf("[nginx-limit-req]", StringComparison.Ordinal)..];
        var ende = limitReq.IndexOf("\n\n", StringComparison.Ordinal);
        if (ende > 0) limitReq = limitReq[..ende];
        Pruefe.Wahr(Regex.IsMatch(limitReq, @"^enabled\s*=\s*false\s*$", RegexOptions.Multiline, Regexfrist), "7.1: nginx-limit-req erst aus: " + limitReq);
        var punkt7 = Abschnitt("## 7. fail2ban", "### 7.1");
        Pruefe.Wahr(punkt7.Contains("After=pve-guests.service", StringComparison.Ordinal), "7: fail2ban startet nach den Containern");
        Pruefe.Wahr(punkt7.Contains("fail2ban-client status", StringComparison.Ordinal) && punkt7.Contains("systemctl is-active fail2ban", StringComparison.Ordinal),
            "7: Sarevoc sieht nach, ob fail2ban läuft");
        Pruefe.Wahr(punkt7.Contains("backend = systemd", StringComparison.Ordinal), "7: sshd ohne auth.log");
    }

    // ---------------------------------------------------------------- Client-Kern und Echo

    [Test]
    public static void Sprachweg_OhneNetlinkLaeuftOhneAdressmeldungWeiter()
    {
        var aufgerufen = 0;
        Pruefe.Falsch(Sprachweg.AdressmeldungAbonnieren((_, _) => { }, _ =>
        {
            aufgerufen++;
            throw new NetworkInformationException(97);
        }), "Netlink fehlt (RestrictAddressFamilies ohne AF_NETLINK): ohne Adressmeldung weiter, keine Ausnahme");
        Pruefe.Gleich(1, aufgerufen, "einmal versucht");
        Pruefe.Wahr(Sprachweg.AdressmeldungAbonnieren((_, _) => { }, _ => { }), "sonst abonniert");
    }

    [Test]
    public static async Task Echo_VerdraengtEndetMitCode3OhneSelbstNeuverbinden()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        var id = Geraeteidentitaet.Fluechtig();
        var meldungen = new ConcurrentQueue<string>();
        await using var echo = new EchoFunker(halter.Aufbau(id, Lokal(20)), id, new EchoOptionen(), meldungen.Enqueue);
        var abschied = new TaskCompletionSource<EchoAbschied>(TaskCreationOptions.RunContinuationsAsynchronously);
        echo.Abgewiesen += (_, a) => abschied.TrySetResult(a);
        var code = new TaskCompletionSource<Registrierungscode>(TaskCreationOptions.RunContinuationsAsynchronously);
        echo.Client.CodeErhalten += (_, c) => code.TrySetResult(c);
        echo.Starten();
        var c = await code.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = c.Code, ["discord"] = "940000000000000001", ["benutzername"] = "echo-funker", ["name"] = "Echo" });
        Pruefe.Wahr(await echo.Client.WartenAufAsync(z => z == ClientZustand.Warteraum, TimeSpan.FromSeconds(5)), "Echo im Warteraum");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = id.Fingerabdruck.Anzeige });
        Pruefe.Wahr(await echo.Client.WartenAufAsync(z => z is ClientZustand.AktivUdp or ClientZustand.AktivTunnel, TimeSpan.FromSeconds(10)), "Echo aktiv");

        // Die Echo-Probe mit der Discord-Zahl des Echo-Funkers: ein zweites Gerät desselben Kontos verdrängt ihn.
        await using var probe = await KernFunker.AktivAsync(halter, "940000000000000001", "echo-funker", "FUNKER", Lokal(21));
        var a = await abschied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Pruefe.Gleich(EchoAbschied.CodeVerdraengt, a.Code, "Code 3");
        Pruefe.Wahr(a.Text.Contains("Verdrängt", StringComparison.Ordinal) && a.Text.Contains("PC echo-funker", StringComparison.Ordinal)
            && a.Text.Contains("systemd", StringComparison.Ordinal), "eindeutige Meldung: " + a.Text);
        await Task.Delay(3000);
        Pruefe.Gleich(ClientZustand.Getrennt, echo.Client.Zustand, "verbindet nicht selbst neu");
        Pruefe.Wahr(probe.Client.Zustand is ClientZustand.AktivUdp or ClientZustand.AktivTunnel, "die Probe bleibt verbunden (kein Ping-Pong)");
        Pruefe.Wahr(EchoOptionen.RuheNachAbweisung >= TimeSpan.FromMinutes(5), "Ruhezeit vor dem Ende: " + EchoOptionen.RuheNachAbweisung);
    }

    [Test]
    public static void Echo_DrucktDieGanzenAdminBefehle()
    {
        var zeilen = EchoFunker.Adminbefehle("ABCD-EFGH-JKMN", "1A2B-3C4D-5E6F-7A8B");
        Pruefe.Wahr(zeilen.Count == 2 && zeilen.All(z => z.StartsWith("dotnet /opt/funk/funk-admin.dll ", StringComparison.Ordinal) && z.EndsWith(" --konfig /etc/funk/server.json", StringComparison.Ordinal)),
            string.Join(" | ", zeilen));
        Pruefe.Wahr(zeilen[0].Contains("verknuepfen --code ABCD-EFGH-JKMN --discord <", StringComparison.Ordinal) && zeilen[1].Contains("freigeben 1A2B-3C4D-5E6F-7A8B", StringComparison.Ordinal),
            string.Join(" | ", zeilen));
        foreach (var z in zeilen)
        {
            var args = Regex.Matches(z["dotnet /opt/funk/funk-admin.dll ".Length..], @"<[^>]*>|\S+", RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Value).ToList();
            Pruefe.Gleich(null, Funk.Admin.Befehlsoptionen.Pruefen(new Funk.Admin.Befehlszeile(args, Funk.Admin.Befehlsoptionen.Schalter)), "funk-admin nimmt die Zeile an: " + z);
        }
    }

    [Test]
    public static void Konsolen_SetzenUtf8AufWindows()
    {
        foreach (var datei in new[] { Path.Combine("tests", "Funk.Testclient", "Program.cs"), Path.Combine("src", "Funk.Admin", "Program.cs") })
        {
            var text = File.ReadAllText(Path.Combine(Wurzel(), datei));
            var setzen = text.IndexOf("Console.OutputEncoding = System.Text.Encoding.UTF8;", StringComparison.Ordinal);
            if (setzen < 0) setzen = text.IndexOf("Console.OutputEncoding = Encoding.UTF8;", StringComparison.Ordinal);
            var erste = Regex.Match(text, @"Console\.(Error\.)?Write(Line)?\(", RegexOptions.None, TimeSpan.FromSeconds(2)).Index;
            Pruefe.Wahr(setzen >= 0 && setzen < erste, datei + ": UTF-8 vor der ersten Ausgabe (Umlaute auf der Windows-Konsole)");
        }
    }

    // ---------------------------------------------------------------- Client, Setup, Bau

    [Test]
    public static void Ich_HinweisUnterMeinDiscordName()
    {
        var text = Funk.Client.Modelle.MeinProfilViewModel.DiscordNameHinweis;
        Pruefe.Wahr(text.StartsWith("Den Discord-Namen gibt ein Admin frei, und nur ein Admin ändert ihn.", StringComparison.Ordinal), text);
        Pruefe.Falsch(text.Contains("trägt ein Admin ein", StringComparison.Ordinal), "nicht mehr: trägt ein Admin ein");
    }

    [Test]
    public static void SetupBau_PrueftProfilGegenEingebautenSchluessel()
    {
        var skript = File.ReadAllText(Path.Combine(Wurzel(), "tools", "setup-bauen.ps1"));
        Pruefe.Wahr(skript.Contains("profil-pruefen", StringComparison.Ordinal) && skript.Contains("profilschluessel.txt", StringComparison.Ordinal),
            "setup-bauen.ps1 prüft konfig\\profil.json mit funk-admin profil-pruefen gegen konfig\\profilschluessel.txt");
        Pruefe.Wahr(Regex.IsMatch(skript, @"throw\s+""[^""]*Profil[^""]*passt nicht", RegexOptions.None, TimeSpan.FromSeconds(2)), "bricht ab, wenn die Signatur nicht passt");
        Pruefe.Wahr(skript.Contains("IndexOf($spki", StringComparison.Ordinal), "und prüft, dass TacRelay.dll genau diesen Schlüssel trägt");
    }

    [Test]
    public static void Bau_NeutralePfadeInDenProgrammen()
    {
        var wurzel = Wurzel().TrimEnd(Path.DirectorySeparatorChar);
        foreach (var typ in new[] { typeof(Funk.Server.Dienst), typeof(FunkClient), typeof(I.Kontenbuch), typeof(Funk.Admin.Befehlsoptionen) })
        {
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(typ.Assembly.Location));
            Pruefe.Falsch(text.Contains(wurzel, StringComparison.OrdinalIgnoreCase), Path.GetFileName(typ.Assembly.Location) + " trägt den Baupfad " + wurzel);
        }
    }
}
