// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Übergang von 0.4.0 auf 0.5.0: Der Server lädt ein Zustandsverzeichnis, das die Programmteile von 0.4.0 selbst geschrieben haben
/// (tests\Funk.Tests\Daten\zustand-0.4.0, Herkunft dort), ohne Verlust: Jede Datei wird von 0.5.0 gelesen und wieder geschrieben,
/// und jedes Feld von damals steht mit demselben Wert wieder da; neu dazu kommen nur <c>alsFunkerBis</c> je Konto und die leere
/// Liste der Phasen. Danach gelten die neuen Regeln auf diesem Stand: Phasen anlegen und schalten (mit dem Komfortzustand von
/// damals), der Funker-Modus für Sarevocs ADMIN-Konto, und beides übersteht Speichern und Laden; die Sicherung lässt den Funker-Modus weg.
/// </summary>
internal static class Uebergang040Tests
{
    private const uint TestKonto = 1;
    private const uint Otto = 2;
    private const uint Fritz = 3;
    private const uint Gina = 4;
    private const uint Hans = 5;
    private const uint Anker = 6;
    private const uint Neuling = 7;

    private static string Daten()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new PruefungException("Repository nicht gefunden"), "tests", "Funk.Tests", "Daten", "zustand-0.4.0");
    }

    /// <summary>Kopie der Testdaten in ein frisches Verzeichnis unter der Testwurzel.</summary>
    private static string Kopie(string name)
    {
        var ziel = Path.Combine(Testserver.Wurzel(), name + "-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(ziel);
        foreach (var datei in Directory.GetFiles(Daten(), "*.json")) File.Copy(datei, Path.Combine(ziel, Path.GetFileName(datei)));
        return ziel;
    }

    /// <summary>Lage-Zeit kurz nach dem Erzeugen der Testdaten: die jüngste Platzzeit plus eine Minute. So gelten Reservierungen noch.</summary>
    private static long KurzDanach(L.Schnappschuss z) => z.Konten.Values.Where(k => k.Platz is not null).Max(k => k.Platz!.Value.Gesetzt) + 60_000;

    [Test]
    public static void Zustand040_JedeDateiVerlustfrei_GelesenUndGeschrieben()
    {
        var dateien = Directory.GetFiles(Daten(), "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Pruefe.Gleich("admins.json,journal.json,komfort.json,konten.json,lage.json,mitglieder.json,vorrangspur.json", string.Join(',', dateien), "alle Zustandsdateien von 0.4.0");
        var lesen = Kopie("u040-lesen");
        var schreiben = Path.Combine(Testserver.Wurzel(), "u040-schreiben-" + Guid.NewGuid().ToString("N")[..12]);
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
            Uebergang020Tests.Vergleichen("konten.json", Alt("konten.json"), Neu("konten.json"));
            Uebergang020Tests.Vergleichen("lage.json", Alt("lage.json"), Neu("lage.json"));
            Uebergang020Tests.Vergleichen("admins.json", Alt("admins.json"), Neu("admins.json"));
            Uebergang020Tests.Vergleichen("mitglieder.json", Alt("mitglieder.json"), Neu("mitglieder.json"));
            var komfort = new Komfortablage();
            komfort.Laden(Alt("komfort.json"));
            Uebergang020Tests.Vergleichen("komfort.json", Alt("komfort.json"), komfort.AlsJson());
            var journal = new Journal(Path.Combine(lesen, "journal.json"));
            var vorher = Alt("journal.json");
            journal.Speichern();
            Uebergang020Tests.Vergleichen("journal.json", vorher, Alt("journal.json"));

            // Neu dazu: alsFunkerBis 0 je Konto, die leere Liste der Phasen; die laufende Phase fehlt (Grundstellung).
            var neueLage = JsonNode.Parse(Neu("lage.json"))!;
            Pruefe.Wahr(neueLage["konten"]!.AsArray().All(k => k!["alsFunkerBis"]!.GetValue<long>() == 0), "alsFunkerBis 0: niemand spielt als Funker");
            Pruefe.Wahr(neueLage["phasen"] is JsonArray { Count: 0 }, "keine Phasen");
            Pruefe.Wahr(neueLage["nebenstand"]!["phase"] is null, "Grundstellung");
            var alteLage = JsonNode.Parse(Alt("lage.json"))!;
            Pruefe.Wahr(alteLage["konten"]!.AsArray().Any(k => k!["beauftragung"] is JsonObject) && alteLage["einstellungen"]!["nachzuegler"]!.GetValue<bool>() == false,
                "die Testdaten tragen Aufträge und eine Sperre ganz (sonst prüfte der Vergleich zu wenig)");
        }
        finally
        {
            Directory.Delete(lesen, recursive: true);
            if (Directory.Exists(schreiben)) Directory.Delete(schreiben, recursive: true);
        }
    }

    [Test]
    public static void Zustand040_PhasenUndFunkerModus_AufDemAltenStand_UeberstehenSpeichernUndLaden()
    {
        var verzeichnis = Kopie("u040-lage");
        try
        {
            var ablage = new Zustandsablage(verzeichnis);
            var (lage, neben) = ablage.LageLaden();
            var jetzt = KurzDanach(lage);
            Pruefe.Wahr(lage.Phasen.IsEmpty && neben.Phase is null && lage.Konten.Values.All(k => k.AlsFunkerBis == 0), "0.4.0: keine Phasen, kein Funker-Modus");
            Pruefe.Wahr(lage.Konten[Otto].Beauftragung is { Grund: "Op Nachtfalke" } b && b.GiltZu(jetzt), "Ottos Auftrag gilt zur Zeit der Daten");
            Pruefe.Wahr(lage.Schiffe[20] is { Art: L.Einheitsart.Trupp, Typ: "Infanterie" } && lage.Schiffe[3].Plaetze.Single(p => p.RolleId == 10).Mindest == 1, "Einheiten mit Art, Typ, Mindest");
            Pruefe.Wahr(lage.Einstellungen is { Selbstwahl: false, SelbstwahlDurch: Otto, Nachzuegler: false }, "Sperre ganz, Urheber Otto");
            Pruefe.Wahr(neben.Aufstellungen.Values.Single() is { Name: "Entern", Ersteller: Otto }, "Aufstellung mit Ersteller");
            // A62 (0.5.0): Nur neue Netze stehen ab Werk auf Sprachaktivierung; gespeicherte behalten ihren Auslöser.
            Pruefe.Wahr(lage.Netze[2].AusloeserStandard == L.Ausloeser.Taste && lage.Netze[50].AusloeserStandard == L.Ausloeser.Taste
                && lage.Netze[8] is { NurTaste: true, AusloeserStandard: L.Ausloeser.Taste } && lage.Netze[7].AusloeserStandard == L.Ausloeser.Va
                && lage.Netze[90].AusloeserStandard == L.Ausloeser.Va && lage.Netze[1] is { NurTaste: true, AusloeserStandard: L.Ausloeser.Taste },
                "gespeicherte Auslöser bleiben: Kommando, Brücke und Trupp Alpha Taste, Crew und Echo VA, Global Taste");

            var schreiber = new L.Zustandsschreiber(lage, neben, ablage);
            var komfort = new Komfortablage();
            komfort.Laden(File.ReadAllBytes(Path.Combine(verzeichnis, "komfort.json")));
            foreach (var (konto, a) in komfort.Auswahlen) schreiber.SendeauswahlSetzen(konto, a.Felder, a.Global, jetzt);

            L.RollenFeld Rf(byte feld, ushort netz, bool hoeren, bool senden, bool standard = false) => new(feld, netz, null, hoeren, senden, standard, false);
            L.RollenFeld Kennung(byte feld, string k, bool hoeren, bool senden, bool standard = false) => new(feld, 0, k, hoeren, senden, standard, false);
            var entern = new L.Einsatzphase(2, "Entern", "ENTERN")
            {
                Varianten =
                [
                    new L.PhasenVariante(13) { Felder = [Kennung(0, "CREW", true, false), Rf(1, 2, true, false), Rf(2, 50, true, true, standard: true)] },
                    new L.PhasenVariante(10) { Felder = [Rf(0, 2, true, true, standard: true), Kennung(1, "CREW", true, true), Kennung(2, "BRUECKE", true, true), Rf(3, 50, true, false)] },
                ],
            };
            // Sarevoc meldet sich an (sein selbst gewählter Platz gilt nur mit Sitzung; nach dem Neustart des Erzeugers hatte er keine).
            Pruefe.Wahr(schreiber.Ausfuehren(L.Auftrag.SitzungAktivieren(TestKonto, 4711, 10), jetzt).Erfolg && schreiber.Aktuell.PlatzZustandVon(TestKonto, jetzt) == L.PlatzZustand.Belegt,
                "Test online, Platz CO belegt");
            // Angelegt von Sarevoc (ADMIN, NETZE_VERWALTEN); die Admin-Sitzung prüft die Zentrale, nicht die Lage.
            Pruefe.Wahr(schreiber.Ausfuehren(L.Auftrag.PhaseSetzen(TestKonto, new L.Einsatzphase(1, "Anflug", "ANFLUG"), schreiber.Aktuell.NetzListeVersion), jetzt).Erfolg
                && schreiber.Ausfuehren(L.Auftrag.PhaseSetzen(TestKonto, entern, schreiber.Aktuell.NetzListeVersion), jetzt).Erfolg, "Phasen auf dem Stand von 0.4.0 angelegt");

            var r = schreiber.Ausfuehren(L.Auftrag.PhaseSchalten(Otto, 2, 1), jetzt);
            Pruefe.Wahr(r.Erfolg && r.Phasenwechsel is { Nr: 2 } pw && pw.Ausgelassen.SequenceEqual([TestKonto]), "Otto schaltet, Sarevoc (ADMIN) ausgelassen: " + r);
            var z = schreiber.Aktuell;
            Pruefe.Wahr(z.Konten[Gina].FeldMitNetz(50) is { Senden: true, Feld: 3 } && z.Konten[Gina].FeldMitNetz(90) is { Fest: true, Feld: 2 }, "Gina: Trupp Alpha auf 3, Echo bleibt fest auf 2");
            Pruefe.Wahr(z.Konten[Gina].Sendeauswahl.Felder.SequenceEqual([(byte)2]), "Ginas eigene Wahl von damals (Echo, komfort.json) bleibt");
            Pruefe.Wahr(z.Konten[Anker].Platz is { SchiffId: 20 } && z.Konten[Anker].FeldMitNetz(50) is { Senden: true }, "Anker (Trupp Alpha, reserviert) nachgeführt");
            Pruefe.Wahr(z.Konten[Fritz].FeldMitNetz(50) is null, "Fritz (Pilot) unberührt");

            var bis = jetzt + 6 * 3600_000L;
            Pruefe.Wahr(schreiber.Ausfuehren(L.Auftrag.FunkermodusSetzen(TestKonto, true, bis), jetzt).Erfolg, "Test heute als Funker");
            Pruefe.Wahr(schreiber.Ausfuehren(L.Auftrag.PhaseSchalten(Otto, 1, 2), jetzt).Erfolg && schreiber.Ausfuehren(L.Auftrag.PhaseSchalten(Otto, 2, 1), jetzt) is { Erfolg: true } wieder
                && wieder.Phasenwechsel!.Umgestellt.Contains(TestKonto), "jetzt stellt Otto auch Test um");
            Pruefe.Wahr(schreiber.Aktuell.Konten[TestKonto].FeldMitNetz(50) is { Hoeren: true, Senden: false }, "Sarevoc (CO) hört Trupp Alpha");

            // Speichern und Laden: Phasen, laufende Phase und Funker-Modus bleiben; die Sicherung lässt den Funker-Modus weg.
            var (lage2, neben2) = new Zustandsablage(verzeichnis).LageLaden();
            Pruefe.Wahr(lage2.Phasen.Count == 2 && lage2.Phasen[2].Varianten.Length == 2 && neben2.Phase is { Nr: 2, Durch: Otto }, "Phasen und laufende Phase geladen");
            Pruefe.Gleich(bis, lage2.Konten[TestKonto].AlsFunkerBis, "Funker-Modus geladen");
            var gesichert = JsonNode.Parse(Zustandsablage.OhneAuftraege(File.ReadAllBytes(Path.Combine(verzeichnis, Zustandsablage.LageDatei))))!;
            Pruefe.Wahr(gesichert["konten"]!.AsArray().All(k => k!["alsFunkerBis"] is null && k["beauftragung"] is null), "die Sicherung trägt weder Aufträge noch Funker-Modus");
            Pruefe.Wahr(gesichert["phasen"] is JsonArray { Count: 2 } && gesichert["nebenstand"]!["phase"] is JsonObject, "aber Phasen und laufende Phase (passend zu den Feldern)");
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Test]
    public static async Task Zustand040_Server050_LaedtAlles_UeberstehtNeustart()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "u040-" + Guid.NewGuid().ToString("N")[..12]);
        var zustand = Path.Combine(verzeichnis, "zustand");
        Directory.CreateDirectory(zustand);
        foreach (var datei in Directory.GetFiles(Daten(), "*.json")) File.Copy(datei, Path.Combine(zustand, Path.GetFileName(datei)));
        var uhr = new Uebergang020Tests.Rueckuhr(new DateTimeOffset(2026, 10, 1, 17, 30, 0, TimeSpan.Zero));
        Testserver? t1 = null;
        Testserver? t2 = null;
        try
        {
            t1 = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr);
            var z = t1.Dienst.Zentrale;
            var k = z.Konten.Stand;
            Pruefe.Gleich(7, k.Konten.Count, "sieben Konten mit dem offenen Antrag");
            Pruefe.Wahr(k.Konten[TestKonto] is { Stufe: I.Stufe.Admin, Eigentuemer: true } && z.Admins.IstEingerichtet(TestKonto), "Test: Eigentümer mit Admin-Nachweis");
            Pruefe.Wahr(k.Konten[Hans].Status == I.KontoStatus.Gesperrt && k.Konten[Neuling].IstAntrag, "Hans gesperrt, Antrag offen");
            var lage = z.Schreiber.Aktuell;
            Pruefe.Wahr(lage.Schiffe.Count == 2 && lage.Rollen.Count == 4 && lage.Phasen.IsEmpty, "Flotte da, keine Phasen");
            Pruefe.Wahr(lage.Konten.Values.All(x => x.AlsFunkerBis == 0), "niemand als Funker");
            Pruefe.Gleich((byte)1, await z.Ausfuehren(() => z.Schreiber.Neben.LaufendeNr()), "Grundstellung");
            Pruefe.Wahr(lage.Konten[TestKonto].Platz is { RolleId: 10 } && lage.Konten[Gina].FeldMitNetz(90) is { Fest: true }, "Plätze und Zusatznetz");
            Pruefe.Wahr(z.Mitglieder.Stand.Vormerkungen.Values.Single() is { ScName: "Eule" }, "Vormerkung");
            var netzliste = await t1.AdminOk("netzliste");
            Pruefe.Falsch(netzliste.Daten!.AsObject().ContainsKey("phasen"), "NetzListe ohne Phasen: das Feld fehlt, wie bei 0.4.0");

            await t1.DienstBeendenAsync();
            t2 = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr);
            var z2 = t2.Dienst.Zentrale;
            Pruefe.Wahr(z2.Konten.Stand.Konten.Count == 7 && z2.Schreiber.Aktuell.Rollen.Count == 4 && z2.Admins.IstEingerichtet(TestKonto), "nichts verloren");
            var journal = await t2.AdminOk("journal", new JsonObject { ["anzahl"] = 200 });
            Pruefe.Wahr(journal.Text.Contains("AUFTRAG_ERTEILT", StringComparison.Ordinal) && journal.Text.Contains("ADMIN_ERNANNT", StringComparison.Ordinal), "Journal von 0.4.0");
        }
        finally
        {
            if (t2 is not null) await t2.DisposeAsync();
            if (t1 is not null && t2 is null) await t1.DisposeAsync();
            if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, recursive: true);
        }
    }
}
