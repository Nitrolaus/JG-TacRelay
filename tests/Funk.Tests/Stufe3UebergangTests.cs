// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Nodes;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using L = Funk.Server.Lage;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Tests;

/// <summary>
/// Übergang von 0.5.0 auf 0.6.0 (FUNKPROTOKOLL 4.7, 6.16, P-62): Der Server lädt ein Zustandsverzeichnis, das Server und Protokoll im
/// Stand 0.5.0 geschrieben haben (tests\Funk.Tests\Daten\zustand-0.5.0, Herkunft dort), ohne Verlust; neu kommen nur die leeren Felder
/// der Stufe 3. Auf dem alten Stand mit laufender Phase und Funker-Modus gehen Kanalnetze, gewählte Rolle und Ch.-Wechsel; der Rückweg
/// (alle Kanalnetze löschen) lässt keine Kanal-ID im Zustand, so dass ein Server 0.5.0 ihn wieder lädt.
/// </summary>
internal static class Stufe3UebergangTests
{
    private const uint TestKonto = 1;
    private const uint Otto = 2;
    private const uint Gina = 4;

    private static string Daten()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new PruefungException("Repository nicht gefunden"), "tests", "Funk.Tests", "Daten", "zustand-0.5.0");
    }

    private static string Kopie(string name)
    {
        var ziel = Path.Combine(Testserver.Wurzel(), name + "-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(ziel);
        foreach (var datei in Directory.GetFiles(Daten(), "*.json")) File.Copy(datei, Path.Combine(ziel, Path.GetFileName(datei)));
        return ziel;
    }

    [Test]
    public static void Zustand050_JedeDateiVerlustfrei_NeuNurDieLeerenFelderDerStufe3()
    {
        var lesen = Kopie("u050-lesen");
        var schreiben = Path.Combine(Testserver.Wurzel(), "u050-schreiben-" + Guid.NewGuid().ToString("N")[..12]);
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
            foreach (var d in new[] { "konten.json", "lage.json", "admins.json", "mitglieder.json" }) Uebergang020Tests.Vergleichen(d, Alt(d), Neu(d));
            var komfort = new Komfortablage();
            komfort.Laden(Alt("komfort.json"));
            Uebergang020Tests.Vergleichen("komfort.json", Alt("komfort.json"), komfort.AlsJson());

            var alt = JsonNode.Parse(Alt("lage.json"))!;
            var neu = JsonNode.Parse(Neu("lage.json"))!;
            Pruefe.Wahr(alt["netze"]!.AsArray().All(n => n!["kanaele"] is null) && neu["netze"]!.AsArray().All(n => n!["kanaele"] is JsonArray { Count: 0 }),
                "neu: kanaele leer je Netz");
            Pruefe.Wahr(neu["konten"]!.AsArray().All(k => k!["gewaehlteRolle"] is null), "neu: keine gewählte Rolle");
            Pruefe.Wahr(neu["rollen"]!.AsArray().All(r => r!["felder"]!.AsArray().All(f => f!["ch"] is null)), "neu: kein fester Ch.");
            Pruefe.Wahr(lage.Phasen.Count == 2 && neben.Phase is { Nr: 2, Durch: Otto } && lage.Konten[TestKonto].AlsFunkerBis > 0, "Phase 2 und Funker-Modus von 0.5.0 geladen");
            Pruefe.Wahr(lage.Konten[Otto].FeldMitNetz(90) is { Feld: 1 }, "Ottos selbst gelegtes Echo");
        }
        finally
        {
            Directory.Delete(lesen, recursive: true);
            if (Directory.Exists(schreiben)) Directory.Delete(schreiben, recursive: true);
        }
    }

    [Test]
    public static void Zustand050_KanaeleDarauf_UndRueckwegOhneKanalId()
    {
        var verzeichnis = Kopie("u050-kanaele");
        try
        {
            var ablage = new Zustandsablage(verzeichnis);
            var (lage, neben) = ablage.LageLaden();
            var jetzt = neben.Phase!.Seit + 60_000;
            var schreiber = new L.Zustandsschreiber(lage, neben, ablage);
            L.Ergebnis Tun(L.Auftrag a, string was)
            {
                var r = schreiber.Ausfuehren(a, jetzt);
                Pruefe.Wahr(r.Erfolg, was + ": " + r);
                return r;
            }
            ushort K(ushort n, byte c) => Kanalrechnung.Id(n, c);
            Tun(L.InterneAuftraege.Verwaltung("netz-setzen", z => z.NetzSetzen(new L.Netz(100, "Brücke")
            {
                Kanaele = ["Ruhe", .. Enumerable.Range(1, 20).Select(i => "Schiff " + i)], Offen = L.Offenheit.HoerenSenden, Reihenfolge = 20,
            })), "Kanalnetz auf dem alten Stand");
            Tun(L.InterneAuftraege.Verwaltung("rolle-setzen", z => z.RolleSetzen(new L.Rolle(30, "Kapitän")
            {
                SelbstWaehlbar = true,
                Felder = [new L.RollenFeld(0, 2, null, true, true, true, false), new L.RollenFeld(1, 100, null, true, true, false, false)],
            })), "Rolle mit Kanalnetz");
            Tun(L.Auftrag.RolleWaehlen(Gina, 30), "Gina wählt die Rolle (Platz Marine auf Schiff 3 bleibt)");
            // Seit 0.8.1 (Q46) ohne Wunsch: Brücke beginnt auf Ruhe, Gina wechselt selbst.
            Tun(L.Auftrag.FeldSetzen(Gina, 1, K(100, 3)), "Gina auf Schiff 3");
            Pruefe.Wahr(schreiber.Aktuell.Konten[Gina] is { GewaehlteRolle: 30, Platz: { SchiffId: 3, RolleId: 13 } } g && g.FeldMitNetz(K(100, 3)) is not null,
                "Gina: Brücke Schiff 3, Platz bleibt");
            Tun(L.Auftrag.FeldSetzen(Otto, 2, K(100, 7)), "Otto legt Schiff 7 dazu");
            Tun(L.Auftrag.PhaseSchalten(Otto, 1, 2), "Otto schaltet zurück auf Anflug");
            var (geladen, _) = new Zustandsablage(verzeichnis).LageLaden();
            Pruefe.Gleich("", string.Join("; ", geladen.Pruefen()), "gespeichert und geladen stimmig");

            // Rückweg: das Kanalnetz löschen räumt jede Kanal-ID, die gewählte Rolle bleibt (ihre Rolle gibt es noch).
            Tun(L.InterneAuftraege.Verwaltung("netz-loeschen", z => z.NetzLoeschen(100)), "Kanalnetz gelöscht");
            var json = JsonNode.Parse(File.ReadAllBytes(Path.Combine(verzeichnis, Zustandsablage.LageDatei)))!;
            var ids = json["konten"]!.AsArray().SelectMany(k => k!["felder"]!.AsArray().Select(f => (int)f!["netzId"]!)
                    .Concat(k["stumm"]!.AsArray().Select(s => (int)s!["netzId"]!)))
                .Concat(json["schiffe"]!.AsArray().SelectMany(s => s!["netze"]!.AsObject().Select(p => (int)p.Value!)))
                .ToList();
            Pruefe.Falsch(ids.Any(id => id >= Kanalrechnung.Basis), "keine Kanal-ID mehr in Feldern, Stummschaltungen und Einheiten");
            Pruefe.Wahr(json["netze"]!.AsArray().All(n => n!["kanaele"] is JsonArray { Count: 0 }), "kein Kanalnetz mehr: das Tor fällt, 0.5.0 lädt den Stand");
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Test]
    public static async Task Zustand050_ServerStartetDarauf_ClientsUnter060BleibenBisZumKanalnetz()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "u050-dienst-" + Guid.NewGuid().ToString("N")[..12]);
        var zustand = Path.Combine(verzeichnis, "zustand");
        Directory.CreateDirectory(zustand);
        foreach (var datei in Directory.GetFiles(Daten(), "*.json")) File.Copy(datei, Path.Combine(zustand, Path.GetFileName(datei)));
        await using var t = await Testserver.StartenAsync(verzeichnis: verzeichnis);
        var z = await t.Dienst.Zentrale.Ausfuehren(() => t.Dienst.Zentrale.Schreiber.Aktuell);
        Pruefe.Wahr(z.Phasen.Count == 2 && z.Netze.Values.All(n => n.Kanaele.IsEmpty), "Server 0.6.0 läuft auf dem Stand von 0.5.0, ohne Kanalnetz");
        await using var f = await t.VerbindenAsync();
        Pruefe.Gleich(Funk.Protokoll.Steuerung.WelcomeStatus.Unbekannt, (await f.HelloAsync("0.5.1")).Status, "ohne Kanalnetz kommt ein Client 0.5.1 durch");
    }
}
