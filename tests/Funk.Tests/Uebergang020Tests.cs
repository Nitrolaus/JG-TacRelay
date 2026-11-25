// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Nodes;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Übergang von 0.2.0 (Sarevocs Heimserver seit dem 01.10.2026) auf 0.3.0: Der Server lädt ein Zustandsverzeichnis, das die
/// Programmteile von 0.2.0 selbst geschrieben haben (tests\Funk.Tests\Daten\zustand-0.2.0, Herkunft dort), ohne Verlust. Jede
/// Datei wird von 0.3.0 gelesen und wieder geschrieben, und jedes Feld von damals steht mit demselben Wert wieder da (neue
/// Felder dürfen dazukommen). Danach gelten die neuen Regeln: RAIDLEITER teilt nicht mehr ein, eine alte Aufstellung gehört
/// dem, der sie gespeichert hat, die Sperre der Selbstwahl von damals bleibt bestehen, bis jemand sie aufhebt.
/// </summary>
internal static class Uebergang020Tests
{
    // Konten der Testdaten, in der Reihenfolge der Verknüpfung (HERKUNFT.txt).
    private const uint TestKonto = 1;
    private const uint Otto = 2;
    private const uint Fritz = 3;
    private const uint Gina = 4;
    private const uint Hans = 5;
    private const uint Neuling = 6;

    /// <summary>Kurz nach der Zeit der Testdaten, damit keine Fristen (Antrag 24 h, Reservierung 12 h) den Test zum Fristtest machen.</summary>
    private static readonly DateTimeOffset KurzDanach = new(2026, 10, 1, 6, 30, 0, TimeSpan.Zero);

    private static string Daten()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new PruefungException("Repository nicht gefunden"), "tests", "Funk.Tests", "Daten", "zustand-0.2.0");
    }

    /// <summary>Uhr ab einem festen Zeitpunkt, die mit der echten Zeit weiterläuft (wie in Uebergang010Tests).</summary>
    internal sealed class Rueckuhr(DateTimeOffset start) : TimeProvider
    {
        private readonly long _t0 = TimeProvider.System.GetTimestamp();

        public override DateTimeOffset GetUtcNow() => start + TimeProvider.System.GetElapsedTime(_t0);
    }

    /// <summary>
    /// Jedes Feld aus <paramref name="alt"/> steht mit demselben Wert in <paramref name="neu"/>; neue Felder sind erlaubt.
    /// <paramref name="ohne"/>: berechnete Felder, die beim Laden nicht gelesen werden (etwa die Rechte aus der Stufe).
    /// </summary>
    internal static void Teilmenge(JsonNode? alt, JsonNode? neu, string pfad, List<string> fehler, IReadOnlySet<string> ohne)
    {
        switch (alt)
        {
            case JsonObject a:
                if (neu is not JsonObject n)
                {
                    fehler.Add(pfad + ": Objekt fehlt");
                    return;
                }
                foreach (var (name, wert) in a)
                {
                    if (ohne.Contains(name)) continue;
                    if (!n.TryGetPropertyValue(name, out var nw)) fehler.Add($"{pfad}.{name}: fehlt");
                    else Teilmenge(wert, nw, pfad + "." + name, fehler, ohne);
                }
                return;
            case JsonArray liste:
                if (neu is not JsonArray nl || nl.Count != liste.Count)
                {
                    fehler.Add($"{pfad}: Liste mit {liste.Count} statt {(neu as JsonArray)?.Count}");
                    return;
                }
                for (var i = 0; i < liste.Count; i++) Teilmenge(liste[i], nl[i], $"{pfad}[{i}]", fehler, ohne);
                return;
            default:
                if (!JsonNode.DeepEquals(alt, neu)) fehler.Add($"{pfad}: {alt?.ToJsonString()} statt {neu?.ToJsonString()}");
                return;
        }
    }

    internal static void Vergleichen(string datei, byte[] alt, byte[] neu, params string[] ohne)
    {
        var fehler = new List<string>();
        Teilmenge(JsonNode.Parse(alt), JsonNode.Parse(neu), datei, fehler, new HashSet<string>(ohne, StringComparer.Ordinal));
        Pruefe.Gleich("", string.Join("\n", fehler.Take(20)), datei + ": verlustfrei geladen und geschrieben");
    }

    /// <summary>
    /// Seit 0.11.8 (Untergilden entfallen): <c>untergilden</c> aus einer alten <c>mitglieder.json</c> wird geladen und wieder
    /// geschrieben, damit eine ältere Serverfassung den Stand wieder laden kann (ohne das Feld scheiterte sie beim Laden). Wird eine
    /// Vormerkung aus der Untergilde übernommen und ein Konto aus ihr gelöscht, fallen nur deren Nummern heraus (Datenschutz,
    /// 15.10.2026); das übernehmende Konto kommt nicht hinein, sonst bleibt jedes Feld.
    /// </summary>
    [Test]
    public static void Zustand020_Untergilden_NurGeloeschteNummernFallenHeraus()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "u020-ug-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(verzeichnis);
        var quelle = Path.Combine(Daten(), Zustandsablage.MitgliederDatei);
        File.Copy(quelle, Path.Combine(verzeichnis, Zustandsablage.MitgliederDatei));
        try
        {
            var ablage = new Zustandsablage(verzeichnis);
            var buch = new I.Mitgliederbuch(TimeProvider.System, ablage.MitgliederLaden());
            Pruefe.Wahr(buch.VormerkungUebernehmen(1, Hans).Ok, "Vormerkung Eule (in BS) an Hans übernommen");
            buch.KontoEntfernt(Fritz);
            Pruefe.Wahr(buch.Stand.Vormerkungen.IsEmpty && !buch.Stand.Mitglieder.ContainsKey(Fritz), "beide Änderungen gelten");
            ablage.MitgliederSpeichern(buch.Stand);
            var erwartet = JsonNode.Parse(File.ReadAllBytes(quelle))!["untergilden"]!;
            Pruefe.Wahr(erwartet[0]!["konten"]!.ToJsonString() == $"[{Fritz},{Gina}]" && erwartet[0]!["vormerkungen"]!.ToJsonString() == "[1]",
                "Ausgangslage: Fritz, Gina und Vormerkung 1 in der Untergilde");
            erwartet[0]!["konten"] = new JsonArray(Gina);
            erwartet[0]!["vormerkungen"] = new JsonArray();
            var neu = JsonNode.Parse(File.ReadAllBytes(Path.Combine(verzeichnis, Zustandsablage.MitgliederDatei)))!["untergilden"];
            Pruefe.Wahr(JsonNode.DeepEquals(erwartet, neu), "untergilden ohne Fritz und Vormerkung 1, sonst unverändert zurückgeschrieben: " + neu?.ToJsonString());
            Pruefe.Wahr(new Zustandsablage(verzeichnis).MitgliederLaden().Untergilden.Count == 1, "und wieder ladbar");
        }
        finally
        {
            if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Test]
    public static void Zustand020_JedeDateiVerlustfrei_GelesenUndGeschrieben()
    {
        var quelle = Daten();
        var dateien = Directory.GetFiles(quelle, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Pruefe.Gleich("admins.json,journal.json,komfort.json,konten.json,lage.json,mitglieder.json,vorrangspur.json", string.Join(',', dateien), "alle Zustandsdateien von 0.2.0");
        var lesen = Path.Combine(Testserver.Wurzel(), "u020-lesen-" + Guid.NewGuid().ToString("N")[..12]);
        var schreiben = Path.Combine(Testserver.Wurzel(), "u020-schreiben-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(lesen);
        foreach (var d in dateien) File.Copy(Path.Combine(quelle, d!), Path.Combine(lesen, d!));
        try
        {
            var a = new Zustandsablage(lesen);
            var b = new Zustandsablage(schreiben);
            b.KontenSpeichern(a.KontenLaden());
            var (lage, neben) = a.LageLaden();
            b.Speichern(lage, neben);
            b.AdminsSpeichern(a.AdminsLaden());
            b.MitgliederSpeichern(a.MitgliederLaden());
            byte[] Alt(string d) => File.ReadAllBytes(Path.Combine(lesen, d));
            byte[] Neu(string d) => File.ReadAllBytes(Path.Combine(schreiben, d));
            // rechte, privilegiert und istPrivilegiert berechnet der Server aus Stufe und Abweichungen; gelesen werden sie nicht. Für
            // RAIDLEITER sind sie in 0.3.0 andere (kein ZUWEISEN), gewollt; geprüft unten über Stufe, plus und minus.
            Vergleichen("konten.json", Alt("konten.json"), Neu("konten.json"), "rechte", "privilegiert");
            Vergleichen("lage.json", Alt("lage.json"), Neu("lage.json"), "rechte", "istPrivilegiert");
            var ottoAlt = JsonNode.Parse(Alt("konten.json"))!["konten"]!.AsArray().Single(x => (uint)x!["kontoId"]! == Otto)!;
            var ottoNeu = JsonNode.Parse(Neu("konten.json"))!["konten"]!.AsArray().Single(x => (uint)x!["kontoId"]! == Otto)!;
            Pruefe.Gleich(63, (int)ottoAlt["rechte"]!, "RAIDLEITER in 0.2.0: mit ZUWEISEN");
            Pruefe.Gleich(63 & ~(int)I.Rechte.Zuweisen, (int)ottoNeu["rechte"]!, "RAIDLEITER in 0.3.0: Führung ohne ZUWEISEN, sonst gleich");
            Vergleichen("admins.json", Alt("admins.json"), Neu("admins.json"));
            Vergleichen("mitglieder.json", Alt("mitglieder.json"), Neu("mitglieder.json"));
            var komfort = new Komfortablage();
            komfort.Laden(Alt("komfort.json"));
            Vergleichen("komfort.json", Alt("komfort.json"), komfort.AlsJson());
            var journal = new Journal(Path.Combine(lesen, "journal.json"));
            var vorher = Alt("journal.json");
            journal.Speichern();
            Vergleichen("journal.json", vorher, Alt("journal.json"));

            var neueLage = File.ReadAllText(Path.Combine(schreiben, "lage.json"));
            Pruefe.Wahr(neueLage.Contains("\"ersteller\":2", StringComparison.Ordinal), "die Aufstellung von 0.2.0 gehört Otto, der sie gespeichert hat (F10)");
        }
        finally
        {
            Directory.Delete(lesen, recursive: true);
            if (Directory.Exists(schreiben)) Directory.Delete(schreiben, recursive: true);
        }
    }

    [Test]
    public static async Task Zustand020_Server030_LaedtAlles_NeueRegelnGelten_UeberstehtNeustart()
    {
        var uhr = new Rueckuhr(KurzDanach);
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "u020-" + Guid.NewGuid().ToString("N")[..12]);
        var zustand = Path.Combine(verzeichnis, "zustand");
        Directory.CreateDirectory(zustand);
        foreach (var datei in Directory.GetFiles(Daten(), "*.json")) File.Copy(datei, Path.Combine(zustand, Path.GetFileName(datei)));
        Testserver? t1 = null;
        Testserver? t2 = null;
        try
        {
            t1 = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr);
            var z = t1.Dienst.Zentrale;
            var k = z.Konten.Stand;
            Pruefe.Gleich(6, k.Konten.Count, "sechs Konten mit dem offenen Antrag");
            Pruefe.Wahr(k.Konten[TestKonto] is { Stufe: I.Stufe.Admin, Eigentuemer: true, DiscordName: "Sarevoc/Otto" } j && j.ScNamen.SequenceEqual(["Sarevoc"]), "Test: Eigentümer mit Namen");
            Pruefe.Wahr(k.Konten[Otto] is { Stufe: I.Stufe.Raidleiter } r && r.ScNamen.SequenceEqual(["Ferox"]), "Otto: RAIDLEITER");
            Pruefe.Wahr(k.Konten[Fritz] is { DiscordName: "Fritz/DC" } f && f.ScNamen.SequenceEqual(["Fritz", "Fritz Zwei"]), "Fritz: Namen");
            Pruefe.Wahr(k.Konten[Hans].Status == I.KontoStatus.Gesperrt && k.Konten[Neuling].IstAntrag && k.Konten[Neuling].Antrag!.ScName == "Neuling", "Hans gesperrt, Antrag offen");
            Pruefe.Wahr(z.Admins.IstEingerichtet(TestKonto), "Admin-Nachweis von Test");
            var m = z.Mitglieder.Stand;
            Pruefe.Wahr(m.Katalog.Count == 2 && m.Vormerkungen.Values.Single() is { ScName: "Eule", DiscordName: "Eule/BS" }, "Katalog und Vormerkung");
            Pruefe.Wahr(m.DatenVon(Fritz).Rollen.SequenceEqual([(ushort)1]), "Rollen");
            // Seit 0.11.8 gibt es keine Untergilden mehr: Die gespeicherte bleibt im Stand (Rückweg auf eine ältere Fassung), aber weder
            // die eigenen Angaben noch die Praesenz tragen sie.
            Pruefe.Wahr(m.Untergilden.Values.Single() is { Kuerzel: "BS" } u && u.Konten.SequenceEqual([Fritz, Gina]) && u.Vormerkungen.SequenceEqual([1u]),
                "Untergilde BS bleibt gespeichert");
            Pruefe.Wahr(z.KontoStandVon(Fritz) is { Untergilde: null } && z.KontoStandVon(Gina) is { Untergilde: null }, "KontoStand ohne Untergilde");
            foreach (var details in new[] { true, false })
            {
                var eintrag = Funk.Server.Steuer.Abbildung.PraesenzEintrag(z.Schreiber.Aktuell, z.Schreiber.Komfort, z.Schreiber.Aktuell.Konten[Fritz], k.Konten[Fritz],
                    details, null, null, Zeit.LageMs(), z.PraesenzZusatz(k.Konten[Fritz]));
                Pruefe.Wahr(eintrag is { KontoId: Fritz, DiscordName: "Fritz/DC", Untergilde: null }, "Praesenz von Fritz ohne Untergilde, Details " + details);
            }

            var lage = z.Schreiber.Aktuell;
            Pruefe.Wahr(lage.Netze.ContainsKey(90) && lage.Schiffe[3].Art == L.Einheitsart.Schiff && lage.Rollen.Count == 2, "Netze, Schiff 3 (Art SCHIFF), Rollen");
            Pruefe.Wahr(lage.Konten[Fritz].Platz is { SchiffId: 3, RolleId: 11 } && lage.Konten[Fritz].Stumm.Any(s => s.NetzId == 7 && s.Durch == Otto), "Fritz: Platz und Stummschaltung durch Otto");
            Pruefe.Wahr(lage.Konten[Gina].Platz is { RolleId: 10 } && lage.Konten[Gina].Reservierung is { Durch: Otto }, "Gina: von Otto gesetzt, reserviert");
            Pruefe.Wahr(lage.Konten[Hans].LetzterStand?.Platz is { RolleId: 11 }, "Hans: letzter Stand");
            Pruefe.Wahr(lage.Konten.Values.All(x => x.Beauftragung is null), "0.2.0 kennt keine Aufträge");
            var auf = z.Schreiber.Neben.Aufstellungen.Values.Single();
            Pruefe.Wahr(auf is { Name: "Phase 2", Durch: Otto, Ersteller: Otto } && z.Schreiber.Neben.Letzte is { Anwender: Otto }, "Aufstellung: Ersteller aus durch");
            Pruefe.Wahr(lage.Einstellungen is { Selbstwahl: false, SelbstwahlDurch: 0, SelbstwahlBis: 0, Nachzuegler: true }, "Sperre von 0.2.0: ohne Urheber und Frist");

            // Die neuen Regeln: Otto (Führung) behält Global, teilt aber nicht mehr ein; Sarevoc teilt ein, auch ohne Admin-Sitzung.
            var jetzt = Zeit.LageMs();
            Pruefe.Wahr(lage.RechteVon(Otto, jetzt).HasFlag(L.Recht.GlobalSenden) && !lage.RechteVon(Otto, jetzt).HasFlag(L.Recht.Zuweisen), "Otto: Global ja, ZUWEISEN nein");
            Pruefe.Wahr(lage.RechteVon(TestKonto, jetzt).HasFlag(L.Recht.Zuweisen), "Test: ZUWEISEN");
            // Der Auftrag an der Konsole gibt Otto die Einsatzleitung zurück, für einen Abend.
            await t1.AdminOk("auftrag", new JsonObject { ["konto"] = Otto, ["grund"] = "nach dem Update" });
            Pruefe.Wahr(z.Schreiber.Aktuell.RechteVon(Otto, Zeit.LageMs()).HasFlag(L.Recht.Zuweisen), "Otto im Auftrag");
            // Die alte Sperre wirkt weiter, bis jemand sie aufhebt (an der Konsole oder ein Einsatzleiter).
            await t1.AdminOk("einstellung", new JsonObject { ["selbstwahl"] = true });
            Pruefe.Wahr(z.Schreiber.Aktuell.Einstellungen.Selbstwahl, "aufgehoben");

            await t1.DienstBeendenAsync();
            t2 = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr);
            var z2 = t2.Dienst.Zentrale;
            Pruefe.Wahr(z2.Schreiber.Aktuell.Konten[Otto].Beauftragung is { Grund: "nach dem Update" }, "Auftrag nach dem Neustart");
            Pruefe.Wahr(z2.Konten.Stand.Konten.Count == 6 && z2.Mitglieder.Stand.Vormerkungen.Count == 1 && z2.Admins.IstEingerichtet(TestKonto), "nichts verloren");
            var journal = await t2.AdminOk("journal", new JsonObject { ["anzahl"] = 200 });
            Pruefe.Wahr(journal.Text.Contains("AUFTRAG_ERTEILT", StringComparison.Ordinal) && journal.Text.Contains("ADMIN_ERNANNT", StringComparison.Ordinal), "Journal von 0.2.0 und von jetzt");
        }
        finally
        {
            if (t2 is not null) await t2.DisposeAsync();
            if (t1 is not null && t2 is null) await t1.DisposeAsync();
            if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, recursive: true);
        }
    }
}
