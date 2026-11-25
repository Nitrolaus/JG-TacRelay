// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Tests.Oberflaeche;
using static Funk.Tests.RaidleitungServerTests;

namespace Funk.Tests;

/// <summary>
/// Das Brett gegen den echten Server im Prozess (0.11.1 AP1, Sarevocs Befunde vom 12.10.2026 am echten PC; ohne TLS, ohne Geräte, ohne
/// Fenster): Die Leitung (Admin) zieht sich selbst und andere auf eine Rolle, auf einen Einheitskopf, auf eine Rolle ohne Einheit,
/// tauscht mit einer Karte und räumt einen Platz (seit 0.11.2 tut ein Name in der Liste links nichts); jemand Offline bekommt eine Reservierung. Dazu die Kanalliste rechts:
/// Zusatzkanal setzen, umziehen (hört zu hört und sendet, anderer Ch.), entfernen; die Anzeige folgt dem Server.
/// </summary>
internal static class ZuordnungBrettDrahtTests
{
    private const string DiscordTest = "991000000000000001";
    private const string DiscordFritz = "991000000000000002";
    private const string DiscordGina = "991000000000000003";

    private const ushort Kommando = 2;
    private const ushort Orga = 2000;
    private const ushort KanalBruecke = 100;
    private const ushort Co = 10;
    private const ushort Marine = 13;
    private const ushort Funker = 20;
    private const ushort Schiff3 = 3;
    private const ushort Schiff4 = 4;

    private static IPAddress Lokal(int n) => IPAddress.Parse("127.0.11." + n.ToString(CultureInfo.InvariantCulture));

    private static ushort K(byte ch) => Kanalrechnung.Id(KanalBruecke, ch);

    private static JsonNode Objekt(string json) => JsonNode.Parse(json)!;

    /// <summary>
    /// Schiff 3 (CO 1, Marine 6) und Schiff 4 (CO 1, Marine 2), Kommando (2, offen), Crew je Schiff (7, 9), Orga (2000, offen), das Kanalnetz
    /// Brücke (100, Ruhe und Schiff 1 bis 20, offen); Rollen Kapitän (10) und Marine (13) mit Einheit, Funker (20) ohne.
    /// </summary>
    private static async Task FlotteAsync(Testserver t)
    {
        await t.AdminOk("schiff-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":3,"name":"Schiff 3","reihenfolge":3,"netze":{},"plaetze":[]}""") });
        await t.AdminOk("schiff-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":4,"name":"Schiff 4","reihenfolge":4,"netze":{},"plaetze":[]}""") });
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":2,"name":"Kommando","typ":"NORMAL","reihenfolge":1,"nurTaste":false,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false}""") });
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":7,"name":"Crew 3","typ":"NORMAL","reihenfolge":2,"nurTaste":false,"ausloeserStandard":"VA","offen":"NEIN","standard":false,"schiffId":3,"schiffsnetz":"CREW"}""") });
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":9,"name":"Crew 4","typ":"NORMAL","reihenfolge":3,"nurTaste":false,"ausloeserStandard":"VA","offen":"NEIN","standard":false,"schiffId":4,"schiffsnetz":"CREW"}""") });
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":2000,"name":"Orga","typ":"NORMAL","reihenfolge":9,"nurTaste":false,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false}""") });
        var kanaele = "\"Ruhe\"," + string.Join(",", Enumerable.Range(1, 20).Select(i => "\"Schiff " + i.ToString(CultureInfo.InvariantCulture) + "\""));
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":100,"name":"Brücke","typ":"NORMAL","reihenfolge":10,"nurTaste":false,"ausloeserStandard":"VA","offen":"HOEREN_SENDEN","standard":false,"gruppe":"Schiffe","kanaele":[""" + kanaele + "]}") });
        await t.AdminOk("rolle-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":10,"name":"Kapitän","reihenfolge":12,"selbstWaehlbar":true,"brauchtSchiff":true,"felder":[{"feld":0,"netzId":2,"senden":true,"hoeren":true,"sendenStandard":true,"fest":false},{"feld":1,"schiffsnetz":"CREW","senden":true,"hoeren":true,"sendenStandard":false,"fest":false}]}""") });
        await t.AdminOk("rolle-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":13,"name":"Marine","reihenfolge":15,"selbstWaehlbar":true,"brauchtSchiff":true,"felder":[{"feld":0,"schiffsnetz":"CREW","senden":true,"hoeren":true,"sendenStandard":true,"fest":false},{"feld":1,"netzId":2,"senden":false,"hoeren":true,"sendenStandard":false,"fest":false}]}""") });
        await t.AdminOk("rolle-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":20,"name":"Funker","reihenfolge":20,"selbstWaehlbar":true,"brauchtSchiff":false,"felder":[{"feld":0,"netzId":2000,"senden":true,"hoeren":true,"sendenStandard":true,"fest":false}]}""") });
        await t.AdminOk("schiff-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":3,"name":"Schiff 3","reihenfolge":3,"netze":{"CREW":7},"plaetze":[{"rolleId":10,"kapazitaet":1},{"rolleId":13,"kapazitaet":6}]}""") });
        await t.AdminOk("schiff-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":4,"name":"Schiff 4","reihenfolge":4,"netze":{"CREW":9},"plaetze":[{"rolleId":10,"kapazitaet":1},{"rolleId":13,"kapazitaet":2}]}""") });
    }

    /// <summary>Ein Konto wie im WPF-Client: FunkClientDienst über dem FunkClient, verknüpft und freigegeben.</summary>
    private sealed class Leitung : IAsyncDisposable
    {
        public required Geraeteidentitaet Identitaet { get; init; }
        public required FunkClient Client { get; init; }
        public required FunkClientDienst Dienst { get; init; }

        public uint Id => Dienst.Lage.Konto!.Id;

        public static async Task<Leitung> VerbindenAsync(Testserver t, ServerHalter halter, string discord, string name, string stufe, IPAddress lokal)
        {
            var identitaet = Geraeteidentitaet.Fluechtig();
            var client = new FunkClient(new FunkClientOptionen
            {
                Aufbau = halter.Aufbau(identitaet, lokal),
                Identitaet = identitaet,
                Geraetename = "PC " + name,
                ClientName = "Kerntest",
                ClientVersion = "1.0.0",
                Rueckfrage = (_, _) => Task.FromResult(true),
                Empfaenger = new Sammler(),
                Zufall = () => 0.0,
            });
            var d = new FunkClientDienst(() => new FunkAnbindung(client, null, null, null, null));
            await d.VerbindenAsync();
            Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null), name + ": Registrierungscode");
            await t.AdminOk("verknuepfen", new JsonObject { ["code"] = d.Lage.Registrierung!.Code, ["discord"] = discord, ["benutzername"] = name, ["stufe"] = stufe });
            Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Warteraum), name + ": Warteraum");
            await t.AdminOk("freigeben", new JsonObject { ["geraet"] = identitaet.Fingerabdruck.Anzeige });
            Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv && d.Lage.Abgeglichen && d.Lage.ServerKanaele), name + ": aktiv und abgeglichen");
            return new Leitung { Identitaet = identitaet, Client = client, Dienst = d };
        }

        public async ValueTask DisposeAsync()
        {
            await Dienst.DisposeAsync();
            Identitaet.Dispose();
        }
    }

    /// <summary>Das Brett (und Sarevocs Funkfeld) auf einem eigenen Thread mit Kontext wie der Oberflächen-Thread.</summary>
    private sealed class Pult : IDisposable
    {
        private readonly Testordner _ordner = new();
        private readonly Einstellungsablage _einstellungen;

        public Pult(FunkClientDienst dienst)
        {
            Takt = new FadenTakt(Faden);
            var speicher = new EinstellungsSpeicher(_ordner.Datei("einstellungen.json"));
            _einstellungen = new Einstellungsablage(speicher, speicher.Laden(), Takt, TimeSpan.FromHours(1));
            Funkfeld = Faden.Ausfuehren(() => new FunkfeldViewModel(dienst, new KlangMaschinenAttrappe(), new PegelVerlauf(), _einstellungen, Takt, () => 0L)).GetAwaiter().GetResult();
            Brett = Faden.Ausfuehren(() => new BrettViewModel(dienst, Takt, null, TimeSpan.Zero)).GetAwaiter().GetResult();
        }

        public EinFaden Faden { get; } = new();
        public FadenTakt Takt { get; }
        public FunkfeldViewModel Funkfeld { get; }
        public BrettViewModel Brett { get; }

        public Task<T> Am<T>(Func<T> f) => Faden.Ausfuehren(f);

        public Task<bool> Bis(Func<bool> bedingung, int ms = 8000) => Faden.Warten(bedingung, ms);

        public Task<bool> Ablegen(uint konto, IAblageziel ziel) => Faden.AusfuehrenAsync(() => Brett.AblegenAsync(Brett.Karte(konto)!, ziel));

        public Task<string?> Meldung() => Am(() => Brett.Meldung);

        public RollenBlock Block(ushort? schiff, ushort rolle) =>
            Brett.Gruppen.SelectMany(g => g.Spalten).Where(s => s.SchiffId == schiff).SelectMany(s => s.Bloecke).Single(b => b.RolleId == rolle);

        public EinheitSpalte Spalte(ushort schiff) => Brett.Gruppen.SelectMany(g => g.Spalten).Single(s => s.SchiffId == schiff);

        public NetzZiel Netz(ushort id) => Brett.Sondernetze.Single(n => n.NetzId == id);

        public void Dispose()
        {
            Faden.Ausfuehren(() =>
            {
                Funkfeld.Dispose();
                return true;
            }).GetAwaiter().GetResult();
            Faden.Dispose();
            _einstellungen.Dispose();
            _ordner.Dispose();
        }
    }

    private static Task<Funk.Server.Lage.Konto> Stand(Testserver t, uint id) => t.Dienst.Zentrale.Ausfuehren(() => t.Dienst.Zentrale.Schreiber.Aktuell.Konten[id]);

    private static string Felder(Funk.Server.Lage.Konto k) => string.Join(" ", k.Felder.Select(m =>
        m.Feld + ":" + (Kanalrechnung.Zerlegen(m.NetzId, out var n, out var c) ? n + "/" + c : m.NetzId.ToString(CultureInfo.InvariantCulture))
        + (m.Hoeren ? "h" : "") + (m.Senden ? "s" : "") + (m.Fest ? "f" : "")));

    [Test]
    public static async Task Brett_Draht_AdminZiehtSichUndAndere_RolleEinheitskopfOhneEinheitTauschListe()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        await FlotteAsync(t);
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(12));
        // Gina war einmal da und ist offline: Sie steht im Kontenverzeichnis unter "Alle Mitglieder".
        var gina = await KernFunker.AktivAsync(halter, DiscordGina, "gina", "FUNKER", Lokal(13));
        var ginaId = gina.KontoId;
        await gina.DisposeAsync();
        Pruefe.Wahr(await Warten(() => !t.Dienst.Zentrale.Schreiber.Aktuell.IstOnline(ginaId)), "Gina offline");

        await using var test = await Leitung.VerbindenAsync(t, halter, DiscordTest, "test", "ADMIN", Lokal(11));
        var testId = test.Id;
        using var p = new Pult(test.Dienst);
        var b = p.Brett;
        Pruefe.Wahr(await p.Bis(() => b.Sichtbar), "als Admin hat Test das Brett");
        Pruefe.Wahr(await p.Bis(() => b.Karte(fritz.KontoId) is { Online: true } && b.Karte(testId) is { Online: true }), "Fritz und Test als Karten");
        Pruefe.Wahr(await p.Bis(() => b.Karte(ginaId) is { Online: false, RolleId: null }), "Gina offline aus dem Verzeichnis");
        Pruefe.Wahr(await p.Bis(() => b.Gruppen.SelectMany(g => g.Spalten).Count() == 3), "Schiff 3, Schiff 4 und die Rollen ohne Einheit");

        // Fritz wählt sich selbst Marine auf Schiff 3.
        Pruefe.Wahr((await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.EinsteigenRolleSchiff, new EinsteigenRolleSchiffAnfrage { RolleId = Marine, SchiffId = Schiff3 }))).Ok,
            "Fritz Marine auf Schiff 3");
        Pruefe.Wahr(await p.Bis(() => b.Karte(fritz.KontoId) is { RolleId: Marine, SchiffId: Schiff3, Marke: "selbst" }), "Brett: Fritz in Marine 3, selbst");

        // (a) Sarevoc zieht sich selbst auf Kapitän, Schiff 3 (Sarevocs Befund: landete auf Standard).
        var co3 = await p.Am(() => p.Block(Schiff3, Co));
        var bewertung = await p.Am(() => b.Bewerten(b.Karte(testId)!, co3));
        Pruefe.Gleich(Ablagewirkung.Passt, bewertung.Wirkung, "grün vor dem Loslassen: " + bewertung.Text);
        Pruefe.Wahr(await p.Ablegen(testId, co3), "Test auf CO 3 abgelegt: " + await p.Meldung());
        Pruefe.Wahr(await Warten(() => test.Dienst.Lage is { RolleId: Co, SchiffId: Schiff3, Quelle: MitgliedschaftsQuelle.Verschieben }),
            "Sarevocs Lage: CO auf Schiff 3, eingeteilt; war " + test.Dienst.Lage.RolleId + "/" + test.Dienst.Lage.SchiffId + " " + test.Dienst.Lage.Quelle);
        Pruefe.Wahr((await Stand(t, testId)).Platz is { SchiffId: Schiff3, RolleId: Co }, "Server: Test sitzt auf CO 3");
        Pruefe.Wahr(await p.Bis(() => b.Karte(testId) is { RolleId: Co, SchiffId: Schiff3, Marke: "von dir", Wartet: false } && co3.Eintraege.Contains(b.Karte(testId)!)),
            "Brett: Sarevocs Karte im Block CO 3 mit \"von dir\"; Meldung: " + await p.Meldung());
        // ... und sie bleibt dort, auch nachdem das Funkfeld und alle Pushes verarbeitet sind.
        await Task.Delay(1500);
        Pruefe.Wahr(await p.Am(() => b.Karte(testId) is { RolleId: Co, SchiffId: Schiff3 }) && (await Stand(t, testId)).Platz is { RolleId: Co }, "Test bleibt auf CO 3");
        Pruefe.Wahr(await p.Bis(() => p.Funkfeld.RolleName == "12 Kapitän"), "Funkfeld von Test: oben links die Platzrolle, war " + await p.Am(() => p.Funkfeld.RolleName));
        Pruefe.Gleich("0:2hs 1:7hs", Felder(await Stand(t, testId)), "Sarevocs Felder aus der Rolle: Kommando und Crew 3");

        // Einheitskopf: Schiff 4, bester Platz ist die bisherige Rolle CO (dort frei).
        var s4 = await p.Am(() => p.Spalte(Schiff4));
        bewertung = await p.Am(() => b.Bewerten(b.Karte(testId)!, s4));
        Pruefe.Gleich(Ablagewirkung.Passt, bewertung.Wirkung, "Kopf von Schiff 4: " + bewertung.Text);
        Pruefe.Wahr(await p.Ablegen(testId, s4), "Test auf den Kopf von Schiff 4: " + await p.Meldung());
        Pruefe.Wahr(await Warten(() => test.Dienst.Lage is { RolleId: Co, SchiffId: Schiff4 }), "Test jetzt CO auf Schiff 4");
        Pruefe.Wahr(await p.Bis(() => b.Karte(testId) is { RolleId: Co, SchiffId: Schiff4 } && p.Block(Schiff4, Co).Eintraege.Contains(b.Karte(testId)!)), "Brett: Test im Block CO 4");

        // Rolle ohne Einheit: Fritz auf Funker.
        var funker = await p.Am(() => p.Block(null, Funker));
        Pruefe.Wahr(await p.Ablegen(fritz.KontoId, funker), "Fritz auf Funker (ohne Einheit): " + await p.Meldung());
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften is { RolleId: Funker, SchiffId: null, Quelle: MitgliedschaftsQuelle.Verschieben }), "Fritz: Rolle Funker ohne Einheit");
        Pruefe.Wahr(await p.Bis(() => b.Karte(fritz.KontoId) is { RolleId: Funker, SchiffId: null } && funker.Eintraege.Contains(b.Karte(fritz.KontoId)!)), "Brett: Fritz im Block Funker");
        Pruefe.Gleich("0:2000hs", Felder(await Stand(t, fritz.KontoId)), "Fritz' Feld aus der Rolle Funker");

        // Offline vorab: Gina auf Marine 4, reserviert.
        var marine4 = await p.Am(() => p.Block(Schiff4, Marine));
        bewertung = await p.Am(() => b.Bewerten(b.Karte(ginaId)!, marine4));
        Pruefe.Wahr(bewertung.Wirkung == Ablagewirkung.Passt && bewertung.Text.EndsWith("(reserviert, offline)", StringComparison.Ordinal), "offline: " + bewertung.Text);
        Pruefe.Wahr(await p.Ablegen(ginaId, marine4), "Gina (offline) auf Marine 4: " + await p.Meldung());
        Pruefe.Wahr(await p.Bis(() => b.Karte(ginaId) is { RolleId: Marine, SchiffId: Schiff4, Online: false, Marke: "reserviert" } && marine4.Eintraege.Contains(b.Karte(ginaId)!)),
            "Brett: Gina reserviert im Block Marine 4");
        Pruefe.Wahr((await Stand(t, ginaId)).Platz is { SchiffId: Schiff4, RolleId: Marine }, "Server: Gina reserviert auf Marine 4");

        // Tausch: Sarevoc (CO 4) auf Fritz' Karte (Funker).
        bewertung = await p.Am(() => b.Bewerten(b.Karte(testId)!, b.Karte(fritz.KontoId)!));
        Pruefe.Gleich(Ablagewirkung.Tausch, bewertung.Wirkung, "gelb: " + bewertung.Text);
        Pruefe.Wahr(await p.Faden.AusfuehrenAsync(() => b.AblegenAsync(b.Karte(testId)!, b.Karte(fritz.KontoId)!)), "Tausch: " + await p.Meldung());
        Pruefe.Wahr(await Warten(() => test.Dienst.Lage is { RolleId: Funker, SchiffId: null } && fritz.Client.Lage.Mitgliedschaften is { RolleId: Co, SchiffId: Schiff4 }),
            "nach dem Tausch: Test Funker, Fritz CO 4");
        Pruefe.Wahr(await p.Bis(() => funker.Eintraege.Contains(b.Karte(testId)!) && p.Block(Schiff4, Co).Eintraege.Contains(b.Karte(fritz.KontoId)!)), "Brett nach dem Tausch");

        // Liste links: Seit 0.11.2 tut ein Name, nach links gezogen, nichts (Sarevoc 13.10.2026; bis 0.11.1 "auf Standard"); Fritz bleibt auf CO 4.
        bewertung = await p.Am(() => b.Bewerten(b.Karte(fritz.KontoId)!, b.Teilnehmerziel));
        Pruefe.Gleich(Ablagebewertung.Nichts, bewertung, "Liste links: nichts");
        Pruefe.Falsch(await p.Ablegen(fritz.KontoId, b.Teilnehmerziel), "Fritz in die Liste links: nicht abgelegt");
        Pruefe.Wahr((await Stand(t, fritz.KontoId)).Platz is { SchiffId: Schiff4, RolleId: Co }, "Server: Fritz bleibt CO 4");
        // Zurück auf Standard über "Platz räumen".
        await p.Faden.AusfuehrenAsync(async () =>
        {
            b.Auswahl = b.Karte(fritz.KontoId);
            await b.PlatzRaeumenCommand.ExecuteAsync(null);
            return true;
        });
        // "Zurück auf Standard" trägt am Server weiter VERSCHIEBEN mit durch (keine Reservierung, Zuweisen.Anwenden); erst der nächste Login nach
        // der Frist macht STANDARD daraus (ZuordnungServerTests).
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften is { RolleId: null, SchiffId: null }), "Fritz ohne Platz; " + await p.Meldung());
        Pruefe.Wahr(await p.Bis(() => b.Karte(fritz.KontoId) is { RolleId: null } && b.OhnePlatz.Contains(b.Karte(fritz.KontoId)!)), "Brett: Fritz links ohne Platz");
        var verlauf = await p.Am(() => b.Verlauf.ToList());
        Pruefe.Wahr(verlauf.Count(z => z.Contains("du: ", StringComparison.Ordinal)) >= 7 && verlauf.Any(z => z.EndsWith("fritz steigt ein: Schiff 3, Marine", StringComparison.Ordinal)),
            "Verlauf: " + string.Join(" | ", verlauf));
    }

    [Test]
    public static async Task Brett_Draht_Zusatzkanal_SetzenUmziehenEntfernen_AnzeigeFolgtDemServer()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        await FlotteAsync(t);
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(22));
        await using var test = await Leitung.VerbindenAsync(t, halter, DiscordTest, "test", "ADMIN", Lokal(21));
        var testId = test.Id;
        using var p = new Pult(test.Dienst);
        var b = p.Brett;
        Pruefe.Wahr(await p.Bis(() => b.Sichtbar && b.Karte(fritz.KontoId) is { Online: true } && b.Karte(testId) is { Online: true }), "Brett mit Fritz und Test");
        var orga = await p.Am(() => p.Netz(Orga));
        var bridge = await p.Am(() => p.Netz(KanalBruecke));
        Pruefe.Wahr(!orga.IstKanalnetz && bridge.IstKanalnetz, "Orga ist ein Netz, Brücke ein Kanalnetz");

        // Fritz hört und sendet in Orga: ein Zusatzkanal ohne Platz.
        Pruefe.Wahr(await p.Ablegen(fritz.KontoId, orga.HoerenSenden), "Fritz auf Orga (hört + sendet): " + await p.Meldung());
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften?.Felder.Any(f => f.NetzId == Orga && f.Senden) == true), "Fritz hat Orga");
        Pruefe.Gleich("0:2000hsf", Felder(await Stand(t, fritz.KontoId)), "Server: Orga fest in Feld 1");
        Pruefe.Wahr(await p.Bis(() => orga.Mitglieder == "1 drin"), "Kanalliste: Orga 1 drin, war " + await p.Am(() => orga.Mitglieder));

        // Brücke, nur hören, Schiff 3: erst das Ch.-Raster, dann der Zusatzkanal.
        Pruefe.Falsch(await p.Ablegen(fritz.KontoId, bridge.Hoeren), "erst der Ch.");
        Pruefe.Wahr(await p.Am(() => b.HatChWahl && b.ChKnoepfe.Count == 20), "Ch.-Raster mit Schiff 1 bis 20");
        await p.Faden.AusfuehrenAsync(async () =>
        {
            await b.ChWaehlenCommand.ExecuteAsync(b.ChKnoepfe.Single(k => k.Ch == 3));
            return true;
        });
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften?.Felder.Any(f => f.NetzId == K(3) && !f.Senden) == true), "Fritz hört Brücke (Schiff 3)");
        Pruefe.Gleich("0:2000hsf 1:100/3hf", Felder(await Stand(t, fritz.KontoId)), "Server: Schiff 3 fest in Feld 2, nur hören");
        Pruefe.Wahr(await p.Bis(() => bridge.Mitglieder == "1 drin"), "Kanalliste: Brücke 1 drin");

        // Umziehen 1: derselbe Ch., jetzt hört + sendet. Das Raster muss Schiff 3 anbieten, und es bleibt EIN Feld.
        Pruefe.Falsch(await p.Ablegen(fritz.KontoId, bridge.HoerenSenden), "wieder erst der Ch.");
        var drei = await p.Am(() => b.ChKnoepfe.Single(k => k.Ch == 3));
        Pruefe.Wahr(drei.Waehlbar, "Schiff 3 lässt sich für hört + sendet wählen (war: " + drei.Hilfe + ")");
        await p.Faden.AusfuehrenAsync(async () =>
        {
            await b.ChWaehlenCommand.ExecuteAsync(b.ChKnoepfe.Single(k => k.Ch == 3));
            return true;
        });
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften?.Felder.Any(f => f.NetzId == K(3) && f.Senden) == true), "Fritz sendet jetzt in Schiff 3: " + await p.Meldung());
        Pruefe.Gleich("0:2000hsf 1:100/3hsf", Felder(await Stand(t, fritz.KontoId)), "Server: dasselbe Feld, jetzt hört und sendet");

        // Die Kanalliste zeigt Fritz unter Brücke und Orga als Mini-Kachel (0.11.1), je mit seinem Feld.
        Pruefe.Wahr(await p.Bis(() => bridge.Personen.Count == 1 && bridge.Personen[0] is { Feld: 1, Senden: true, Zusatz: true } x && x.Karte.KontoId == fritz.KontoId
            && orga.Personen.Count == 1 && orga.Personen[0] is { Feld: 0, Zusatz: true }), "Mini-Kacheln unter Brücke (Feld 2) und Orga (Feld 1)");
        Pruefe.Gleich("fritz (Schiff 3)", await p.Am(() => bridge.Personen[0].Text), "Text der Mini-Kachel");

        // Umziehen 2 (Sarevocs Befund: eine Person blieb kleben): die Mini-Kachel auf hört + sendet, Schiff 5: das Feld wandert, kein zweites Feld.
        var fritzInBridge = await p.Am(() => bridge.Personen[0]);
        Pruefe.Falsch(await p.Faden.AusfuehrenAsync(() => b.AblegenAsync(fritzInBridge, bridge.HoerenSenden)), "erst der Ch.");
        Pruefe.Wahr(await p.Am(() => b.ChFrage!.StartsWith("fritz von Brücke (Schiff 3) auf Brücke", StringComparison.Ordinal)), "Frage: " + await p.Am(() => b.ChFrage));
        await p.Faden.AusfuehrenAsync(async () =>
        {
            await b.ChWaehlenCommand.ExecuteAsync(b.ChKnoepfe.Single(k => k.Ch == 5));
            return true;
        });
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften?.Felder.Any(f => f.NetzId == K(5)) == true), "Fritz auf Schiff 5: " + await p.Meldung());
        Pruefe.Gleich("0:2000hsf 1:100/5hsf", Felder(await Stand(t, fritz.KontoId)), "Server: Brücke nur einmal, in Feld 2, Schiff 5");
        Pruefe.Wahr(await p.Bis(() => bridge.Mitglieder == "1 drin" && bridge.Personen.Count == 1 && bridge.Personen[0].Text == "fritz (Schiff 5)"), "Kanalliste folgt: Schiff 5, weiter 1 drin");

        // Herausziehen in die Liste links: der Zusatzkanal Orga ist weg (ein felderPatch mit leeren); der Server leert das Feld, die Liste folgt.
        // Der Satz heißt seit 0.11.2 "aus ... heraus" (bis 0.11.1 "Zusatzkanal ... weg").
        var fritzInOrga = await p.Am(() => orga.Personen[0]);
        Pruefe.Gleich("fritz aus Orga heraus", (await p.Am(() => b.Bewerten(fritzInOrga, b.Teilnehmerziel))).Text, "Satz an der Maus");
        Pruefe.Wahr(await p.Faden.AusfuehrenAsync(() => b.AblegenAsync(fritzInOrga, b.Teilnehmerziel)), "Mini-Kachel in die Liste links: " + await p.Meldung());
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften?.Felder.All(f => f.NetzId != Orga) == true), "Fritz hat Orga nicht mehr");
        Pruefe.Gleich("1:100/5hsf", Felder(await Stand(t, fritz.KontoId)), "Server: nur noch Brücke");
        Pruefe.Wahr(await p.Bis(() => orga.Mitglieder == "0 drin" && orga.Personen.Count == 0), "Kanalliste: Orga leer");

        // Das Kreuz an der Mini-Kachel: Brücke weg.
        var fritzBridge5 = await p.Am(() => bridge.Personen[0]);
        await p.Faden.AusfuehrenAsync(async () =>
        {
            await b.KanalPersonEntfernenCommand.ExecuteAsync(fritzBridge5);
            return true;
        });
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften?.Felder.Count == 0), "Fritz hat kein Feld mehr: " + await p.Meldung());
        Pruefe.Gleich("", Felder(await Stand(t, fritz.KontoId)), "Server: keine Felder");
        Pruefe.Wahr(await p.Bis(() => bridge.Mitglieder == "0 drin" && bridge.Personen.Count == 0), "Kanalliste: Brücke leer");

        // Sarevoc selbst auf Brücke (Schiff 7), dann heraus: Funkfeld und Kanalliste folgen. Seit 0.11.2 tut der Name links nichts mehr (bis 0.11.1
        // nahm Standard den Zusatzkanal weg); heraus nimmt die Person im Kanal, nach links gezogen.
        Pruefe.Falsch(await p.Ablegen(testId, bridge.HoerenSenden), "Test: erst der Ch.");
        await p.Faden.AusfuehrenAsync(async () =>
        {
            await b.ChWaehlenCommand.ExecuteAsync(b.ChKnoepfe.Single(k => k.Ch == 7));
            return true;
        });
        Pruefe.Wahr(await Warten(() => test.Dienst.Lage.Felder.Any(f => f.NetzId == K(7))), "Test hat Schiff 7: " + await p.Meldung());
        Pruefe.Wahr(await p.Bis(() => bridge.Mitglieder == "1 drin" && bridge.Personen.Count == 1 && bridge.Personen[0].Karte.KontoId == testId
            && p.Funkfeld.Kacheln.Any(k => k.ChText == "Schiff 7")), "Kanalliste 1 drin mit Test, Sarevocs Funkfeld zeigt Schiff 7");
        Pruefe.Gleich(Ablagebewertung.Nichts, await p.Am(() => b.Bewerten(b.Karte(testId)!, b.Teilnehmerziel)), "Sarevocs Name nach links: nichts");
        var testInBridge = await p.Am(() => bridge.Personen.Single());
        Pruefe.Gleich("test aus Brücke (Schiff 7) heraus", (await p.Am(() => b.Bewerten(testInBridge, b.Teilnehmerziel))).Text, "Test im Kanal nach links: heraus");
        Pruefe.Wahr(await p.Faden.AusfuehrenAsync(() => b.AblegenAsync(testInBridge, b.Teilnehmerziel)), "Test aus Brücke heraus: " + await p.Meldung());
        Pruefe.Wahr(await Warten(() => test.Dienst.Lage.Felder.Count == 0), "Test hat keine Felder mehr: " + await p.Meldung());
        Pruefe.Wahr(await p.Bis(() => bridge.Mitglieder == "0 drin" && bridge.Personen.Count == 0 && !p.Funkfeld.Kacheln.Any(k => k.ChText == "Schiff 7")), "Kanalliste 0 drin, Kachel weg");
    }

    /// <summary>Eine Flotte nur aus der Vorlage Star Citizen (zehn Kanäle mit Ch., 14 Rollen ohne Einheit und ohne Kapazität), wie sie Sarevoc fährt.</summary>
    [Test]
    public static async Task Brett_Draht_StarCitizen_AdminZiehtSichAufRolleOhneEinheit_AuchPerSetzen()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        await t.AdminOk("flotte-vorlage", new JsonObject { ["name"] = "starcitizen" });
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(32));
        await using var test = await Leitung.VerbindenAsync(t, halter, DiscordTest, "test", "ADMIN", Lokal(31));
        var testId = test.Id;
        using var p = new Pult(test.Dienst);
        var b = p.Brett;
        Pruefe.Wahr(await p.Bis(() => b.Sichtbar && b.Karte(fritz.KontoId) is { Online: true } && b.Karte(testId) is { Online: true }), "Brett mit Fritz und Test");
        var co = await p.Am(() => b.Gruppen.SelectMany(g => g.Spalten).Where(s => s.SchiffId is null).SelectMany(s => s.Bloecke).Single(x => x.Titel == "Kapitän"));
        Pruefe.Wahr(await p.Am(() => b.Gruppen.Single().Spalten.Single().Bloecke.Count == 14), "14 Rollen ohne Einheit");

        // Ziehen: Sarevoc auf Kapitän.
        var bewertung = await p.Am(() => b.Bewerten(b.Karte(testId)!, co));
        Pruefe.Gleich(Ablagewirkung.Passt, bewertung.Wirkung, "grün: " + bewertung.Text);
        Pruefe.Wahr(await p.Ablegen(testId, co), "Test auf Kapitän: " + await p.Meldung());
        Pruefe.Wahr(await Warten(() => test.Dienst.Lage is { RolleId: not null, SchiffId: null, Quelle: MitgliedschaftsQuelle.Verschieben } l && l.RolleVon(l.RolleId.Value)?.Name == "Kapitän"),
            "Sarevocs Lage: Kapitän ohne Einheit; war " + test.Dienst.Lage.RolleId + " " + test.Dienst.Lage.Quelle);
        Pruefe.Wahr(await p.Bis(() => b.Karte(testId) is { RolleId: not null, Marke: "von dir", Wartet: false } && co.Eintraege.Contains(b.Karte(testId)!)), "Brett: Test im Block, von dir; " + await p.Meldung());
        await Task.Delay(1500);
        Pruefe.Wahr(await p.Am(() => co.Eintraege.Contains(b.Karte(testId)!)) && (await Stand(t, testId)).Platz is { RolleId: not 0 }, "Test bleibt dort");
        var k = await Stand(t, testId);
        Pruefe.Gleich(6, k.Felder.Length, "sechs Felder aus der Rolle: " + Felder(k));
        Pruefe.Wahr(k.Felder.Count(f => Kanalrechnung.IstRuhe(f.NetzId)) == 2, "Brücke und Crew auf Ruhe (Schiff n wählt der Funker oder die Leitung), die übrigen auf ihrem ersten Ch.");
        Pruefe.Wahr(await p.Bis(() => p.Funkfeld.RolleName == "3 Kapitän" && p.Funkfeld.Kacheln.Count(x => !x.IstGlobal && !x.IstLeer) == 6),
            "Funkfeld von Test: Platzrolle und sechs belegte Kacheln; war " + await p.Am(() => p.Funkfeld.RolleName + ", "
                + p.Funkfeld.Kacheln.Count(x => !x.IstGlobal && !x.IstLeer)));

        // "Setzen" im Detail (derselbe Weg wie das Ziehen): Fritz auf Gunner.
        var crew = await p.Am(() => b.Ziele.Single(z => z.Block.Titel == "Gunner"));
        await p.Am(() =>
        {
            b.Auswahl = b.Karte(fritz.KontoId);
            return true;
        });
        await p.Faden.AusfuehrenAsync(async () =>
        {
            await b.SetzenCommand.ExecuteAsync(crew);
            return true;
        });
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften is { RolleId: not null, Quelle: MitgliedschaftsQuelle.Verschieben }), "Fritz per Setzen auf Gunner: " + await p.Meldung());
        Pruefe.Wahr(await p.Bis(() => crew.Block.Eintraege.Contains(b.Karte(fritz.KontoId)!)), "Brett: Fritz im Block");

        // Und Sarevoc zurück auf Standard, dann wieder auf die Rolle: beides hält. Seit 0.11.2 tut der Name in der Liste links nichts mehr (bis 0.11.1
        // ging es so auf Standard); auf Standard setzt "Platz räumen".
        Pruefe.Falsch(await p.Ablegen(testId, b.Teilnehmerziel), "der Name in die Liste: nichts");
        await p.Faden.AusfuehrenAsync(async () =>
        {
            b.Auswahl = b.Karte(testId);
            await b.PlatzRaeumenCommand.ExecuteAsync(null);
            return true;
        });
        Pruefe.Wahr(await Warten(() => test.Dienst.Lage is { RolleId: null }), "Test ohne Platz: " + await p.Meldung());
        Pruefe.Wahr(await p.Bis(() => b.OhnePlatz.Contains(b.Karte(testId)!)), "Brett: Test links");
        Pruefe.Wahr(await p.Ablegen(testId, co), "Test wieder auf die Rolle: " + await p.Meldung());
        Pruefe.Wahr(await Warten(() => test.Dienst.Lage is { RolleId: not null, Quelle: MitgliedschaftsQuelle.Verschieben }), "Test wieder eingeteilt");
        Pruefe.Wahr(await p.Bis(() => co.Eintraege.Contains(b.Karte(testId)!)), "Brett: Test wieder im Block");
    }
}
