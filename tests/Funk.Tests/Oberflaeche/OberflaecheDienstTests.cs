// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Net;
using System.Text.Json.Nodes;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Anbindung an Funk.ClientKern: Abbildung des Protokollstands in die Lage der Oberfläche, Fehlertexte, Verhalten ohne
/// Serverprofil, und ein Zusammenspiel mit dem echten Dienst auf Loopback (prozessintern, ohne TLS).
/// </summary>
internal static class OberflaecheDienstTests
{
    private static Netz N(ushort id, string name, ushort reihenfolge, NetzTyp typ = NetzTyp.Normal, bool nurTaste = false, Ausloeser a = Ausloeser.Va) => new()
    {
        Id = id, Name = name, Typ = typ, Reihenfolge = reihenfolge, NurTaste = nurTaste, AusloeserStandard = a, Offen = NetzOffen.HoerenSenden, Standard = false,
    };

    private static PraesenzEintrag P(uint konto, string name, bool online, ushort? schiff, ushort? rolle) => new()
    {
        KontoId = konto, Name = name, Benutzername = name.ToLowerInvariant(), Online = online, SchiffId = schiff, RolleId = rolle,
    };

    [Test]
    public static void Abbildung_UebersetztDenProtokollstand()
    {
        var netzListe = new NetzListe
        {
            Version = 3,
            Einstellungen = new NetzEinstellungen { DurchsageNurTaste = true, Selbstwahl = true },
            Netze = [N(1, "Global", 0, NetzTyp.Global, nurTaste: true, a: Ausloeser.Taste), N(7, "Crew", 5), N(2, "Kommando", 1, a: Ausloeser.Taste)],
            Schiffe =
            [
                new Schiff { Id = 3, Name = "Schiff 2", Reihenfolge = 2, Netze = new Dictionary<string, ushort> { ["crew"] = 7 },
                    Plaetze = [new SchiffsPlatz { RolleId = 4, Kapazitaet = 3 }] },
                new Schiff { Id = 1, Name = "Schiff 1", Reihenfolge = 1, Netze = new Dictionary<string, ushort>(),
                    Plaetze = [new SchiffsPlatz { RolleId = 4, Kapazitaet = 3 }, new SchiffsPlatz { RolleId = 5, Kapazitaet = 1 }] },
            ],
            Rollen =
            [
                new Rolle { Id = 4, Name = "Gunner", Reihenfolge = 2, SelbstWaehlbar = true, BrauchtSchiff = true,
                    Felder = [new RollenFeld { Feld = 1, Schiffsnetz = "crew", Senden = true, Hoeren = true, SendenStandard = true, Fest = false },
                              new RollenFeld { Feld = 0, NetzId = 2, Senden = false, Hoeren = true, SendenStandard = false, Fest = true }] },
                new Rolle { Id = 5, Name = "CO", Reihenfolge = 1, SelbstWaehlbar = true, BrauchtSchiff = true, Felder = [] },
            ],
        };
        var m = new Mitgliedschaften
        {
            ZustandsVersion = 9, KontoVersion = 1, SchiffId = 1, RolleId = 4,
            Felder = [new Mitgliedschaft { Feld = 1, NetzId = 7, Hoeren = true, Senden = true, Fest = false },
                      new Mitgliedschaft { Feld = 0, NetzId = 2, Hoeren = true, Senden = false, Fest = true }],
            Global = new GlobalAngabe { Senden = false },
            Sendeauswahl = new Sendeauswahl { Felder = [1], Global = false },
            Stumm = [],
            Quelle = MitgliedschaftsQuelle.Rolle,
        };
        var praesenz = new Dictionary<uint, PraesenzEintrag>
        {
            [10] = P(10, "Test", true, 1, 4),
            [11] = P(11, "Falke", true, 1, 4),
            [12] = P(12, "Eule", false, 1, 4),
            [13] = P(13, "Kobold", true, 1, 5),
        };
        var welcome = new Welcome
        {
            Minor = 0, Status = WelcomeStatus.Freigegeben,
            Konto = new KontoAngabe { Id = 10, Name = "Test", Benutzername = "test", Stufe = Stufe.Raidleiter, Rechte = [Recht.MehrfachSenden] },
        };
        var sprech = new[]
        {
            new SprechanzeigeEintrag { KontoId = 11, Art = SprechArt.Netz, Netze = [7] },
            new SprechanzeigeEintrag { KontoId = 13, Art = SprechArt.Global, Netze = [] },
            new SprechanzeigeEintrag { KontoId = 99, Art = SprechArt.Durchsage, Netze = [] },
        };
        var pegel = new PegelVoreinstellungen
        {
            Eintraege = [new PegelEintrag { KontoId = 11, NetzId = 0, Db = -6 }, new PegelEintrag { KontoId = 13, NetzId = 7, Db = 3 }],
        };

        var l = LageAbbildung.Abbilden(new LageEingabe(ClientZustand.AktivUdp, welcome, netzListe, m, praesenz, [1], false, sprech, pegel));

        Pruefe.Gleich(Verbindungsstatus.Aktiv, l.Status, "AKTIV/UDP");
        Pruefe.Wahr(l.Netze.Select(n => n.Name).SequenceEqual(["Kommando", "Crew", "Global"]), "Reihenfolge des Servers, Global zuletzt: " + string.Join(",", l.Netze.Select(n => n.Name)));
        Pruefe.Wahr(l.NetzVon(1)!.IstGlobal && l.NetzVon(1)!.NurTaste, "Global");
        Pruefe.Falsch(l.NetzVon(2)!.SprachaktivierungStandard, "Kommando: Taste");
        Pruefe.Wahr(l.NetzVon(7)!.SprachaktivierungStandard, "Crew: VA");
        Pruefe.Wahr(l.Felder.Select(f => f.Feld).SequenceEqual(new byte[] { 0, 1 }), "Felder nach Nummer");
        Pruefe.Wahr(l.FeldVon(0) is { NetzId: 2, Fest: true, Senden: false }, "Feld 0");
        Pruefe.Wahr(l.SendeFelder.SequenceEqual(new byte[] { 1 }), "Sendeauswahl");
        Pruefe.Wahr(l.Schiffe.Select(s => s.Name).SequenceEqual(["Schiff 1", "Schiff 2"]), "Schiffe nach Reihenfolge");
        var s1 = l.Schiffe[0];
        Pruefe.Gleich(2, s1.Plaetze.Single(p => p.RolleId == 4).Belegt, "zwei Gunner online auf Schiff 1 (Eule offline zählt nicht)");
        Pruefe.Gleich(1, s1.Plaetze.Single(p => p.RolleId == 5).Belegt, "CO belegt");
        Pruefe.Gleich(4, s1.Kapazitaet, "Kapazität");
        Pruefe.Wahr(l.Rollen.Select(r => r.Name).SequenceEqual(["CO", "Gunner"]), "Rollen nach Reihenfolge");
        Pruefe.Wahr(l.Rollen[1].Netze.SequenceEqual(["Kommando", "Crew (Schiff)"]), "Vorschau der Netze (A22): " + string.Join(",", l.Rollen[1].Netze));
        Pruefe.Wahr(l.Selbstwahl, "Selbstwahl");
        Pruefe.Gleich((ushort?)1, l.SchiffId, "eigenes Schiff");
        Pruefe.Gleich("Test", l.Konto?.Name, "Konto");
        Pruefe.Wahr(l.Konto!.Hat(Recht.MehrfachSenden), "Rechte");
        Pruefe.Gleich(3, l.Sprecher.Count, "drei Sprecher");
        Pruefe.Wahr(l.Sprecher[0] is { Name: "Falke", Durchsage: false } && l.Sprecher[0].Netze.SequenceEqual(new ushort[] { 7 }), "Falke in Crew");
        Pruefe.Wahr(l.Sprecher[1].Netze.SequenceEqual(new ushort[] { 1 }), "Global-Spruch landet auf Global");
        Pruefe.Wahr(l.Sprecher[2].Durchsage && l.Sprecher[2].Netze.SequenceEqual(new ushort[] { 2, 7 }), "Durchsage an alle: alle Netze außer Global");
        Pruefe.Gleich("Funker 99", l.Sprecher[2].Name, "unbekannter Sprecher");
        Pruefe.Gleich(1, l.FunkerPegel.Count, "nur Pegel für alle Netze (Netz 0)");
        Pruefe.Gleich(-6f, l.FunkerPegel[11], "Pegel für Falke");

        Pruefe.Gleich(Verbindungsstatus.Abgleich, LageAbbildung.Status(ClientZustand.Sync), "SYNC");
        Pruefe.Gleich(Verbindungsstatus.Aktiv, LageAbbildung.Status(ClientZustand.AktivUdpBinden), "AKTIV/UDP_BINDEN");
        Pruefe.Gleich(Verbindungsstatus.AktivTunnel, LageAbbildung.Status(ClientZustand.AktivTunnel), "Tunnel");
        var leer = LageAbbildung.Abbilden(new LageEingabe(ClientZustand.Verbinden, null, null, null, new Dictionary<uint, PraesenzEintrag>(), [], false, []));
        Pruefe.Wahr(leer.Netze.Count == 0 && leer.Felder.Count == 0 && leer.Konto is null && leer.Status == Verbindungsstatus.Verbinden, "ohne Stand leer");
    }

    [Test]
    public static void Dienst_Fehlertexte()
    {
        Pruefe.Gleich("Dafür fehlt das Recht.", FunkClientDienst.Fehlertext(Fehlercode.KeinRecht), "KEIN_RECHT");
        Pruefe.Gleich("Global lässt sich nicht abschalten.", FunkClientDienst.Fehlertext(Fehlercode.GlobalPflicht), "GLOBAL_PFLICHT");
        Pruefe.Wahr(FunkClientDienst.Fehlertext(Fehlercode.Intern).Contains("INTERN", StringComparison.Ordinal), "sonst Drahtname");
        Pruefe.Gleich("Unbekannter Fehler.", FunkClientDienst.Fehlertext(null), "ohne Code");
    }

    [Test]
    public static async Task Dienst_Lage_AelterBauUeberschreibtNieNeueren()
    {
        // Zwei Threads bauen die Lage (Ereignis des Clients, Anfrage der Oberfläche). Der mit der höheren Nummer hat
        // später gelesen; kommt der ältere Bau danach an, darf er den neueren nicht überschreiben.
        await using var d = new FunkClientDienst(() => throw new InvalidOperationException("nicht gebraucht"));
        var alt = d.BauNummerZiehen();
        var neu = d.BauNummerZiehen();
        var lageNeu = FunkLage.Leer with { Hinweis = "neu" };
        Pruefe.Wahr(d.Uebernehmen(neu, lageNeu), "neuerer Bau übernommen");
        Pruefe.Falsch(d.Uebernehmen(alt, FunkLage.Leer with { Hinweis = "alt" }), "älterer Bau verworfen");
        Pruefe.Gleich("neu", d.Lage.Hinweis, "es gilt der neuere Stand");

        // Unter Last: viele Bauten aus vielen Threads, am Ende steht der mit der höchsten Nummer.
        var hoechste = 0L;
        var sperre = new object();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                var n = d.BauNummerZiehen();
                d.Uebernehmen(n, FunkLage.Leer with { Hinweis = n.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                lock (sperre) hoechste = Math.Max(hoechste, n);
            }
        })));
        Pruefe.Gleich(hoechste.ToString(System.Globalization.CultureInfo.InvariantCulture), d.Lage.Hinweis, "höchste Nummer gewinnt");
    }

    [Test]
    public static async Task Dienst_OhneProfil_ZeigtHinweisStattAbsturz()
    {
        var v = new VerbindungsEinstellungen();
        // Diese Fassung (Testlauf) bringt weder Profil noch Profilschlüssel mit: Die Fabrik sagt es, statt abzustürzen.
        var ohne = Pruefe.Wirft<InvalidOperationException>(() => Verbindungsfabrik.Bauen(v, null), "ohne eingebautes Profil");
        Pruefe.Wahr(ohne.Message.Contains("Probefunk", StringComparison.Ordinal), "sagt, was geht: " + ohne.Message);
        v.Testzugang = "10.1.2.3:4000";
        Pruefe.Wirft<InvalidOperationException>(() => Verbindungsfabrik.Bauen(v, null), "Testzugang nur auf Loopback");
        v.Testzugang = null;
        using var o = new Testordner();
        using var k = new OberflaecheProfilTests.Schluessel();
        var datei = o.Datei("profil.json");
        File.WriteAllText(datei, "{\"daten\":\"AA==\"}");
        var e = Pruefe.Wirft<InvalidOperationException>(() => Verbindungsfabrik.Bauen(v, null, profil: new Profilquelle(datei, [k.Spki])), "kein signiertes Profil");
        Pruefe.Wahr(e.Message.Contains("signiertes Serverprofil", StringComparison.Ordinal), e.Message);

        var geaendert = 0;
        await using var d = new FunkClientDienst(() => throw new InvalidOperationException("Kein Serverprofil gewählt."));
        d.LageGeaendert += () => geaendert++;
        await d.VerbindenAsync();
        Pruefe.Gleich(Verbindungsstatus.Getrennt, d.Lage.Status, "getrennt");
        // Eine unerwartete Ausnahme der Fabrik: der Satz mit Fehlernummer, ihr Text nur als Einzelheit (Q5).
        Pruefe.Wahr(d.Lage.Hinweis is { } h && h.EndsWith("(Fehler " + Fehlernummern.InternVorbereitung + ")", StringComparison.Ordinal), "Hinweis für die Anzeige: " + d.Lage.Hinweis);
        Pruefe.Gleich("Kein Serverprofil gewählt.", d.Lage.HinweisEinzelheit, "Ausnahmetext als Einzelheit");
        Pruefe.Wahr(geaendert >= 1, "Änderung gemeldet");
        Pruefe.Gleich("Nicht verbunden.", await d.HoerenSetzenAsync(2, true), "Anfragen ohne Verbindung scheitern mit Text");
    }

    [Test]
    public static async Task Verbindung_Rueckfrage_UndRegistrierungscode()
    {
        var takt = new WarteschlangenTakt();
        using var a = new Aufbau(FunkLage.Leer with
        {
            Status = Verbindungsstatus.Registrierung,
            Registrierung = new Registrierungsangabe("ABCD-EFGH-JKMN", "/verknuepfen ABCD-EFGH-JKMN", DateTimeOffset.UtcNow.AddMinutes(5)),
        }, takt: takt);
        var vm = new VerbindungViewModel(a.Dienst, a.Ablage, takt, a.Fenster);
        Pruefe.Wahr(vm.IstRegistrierung, "Registrierung sichtbar");
        Pruefe.Gleich("/verknuepfen ABCD-EFGH-JKMN", vm.CodeBefehl, "Befehl zum Eintippen");
        Pruefe.Gleich("Gerät anmelden", VerbindungViewModel.Text(vm.Status), "Status");
        Pruefe.Falsch(a.Dienst.Lage.Registrierung!.ToString().Contains("ABCD", StringComparison.Ordinal), "Code nie in ToString");

        var frage = Task.Run(() => vm.RueckfrageAsync(new VerknuepfungKonto { Name = "Test", Benutzername = "test", Neu = false }, CancellationToken.None));
        var bis = DateTime.UtcNow.AddSeconds(5);
        while (takt.Anzahl == 0 && DateTime.UtcNow < bis) await Task.Delay(5);
        takt.Abarbeiten();
        Pruefe.Wahr(vm.HatRueckfrage && vm.Rueckfrage!.Contains("Test", StringComparison.Ordinal), "Frage steht da: " + vm.Rueckfrage);
        vm.RueckfrageJaCommand.Execute(null);
        Pruefe.Wahr(await frage.WaitAsync(TimeSpan.FromSeconds(5)), "Ja kommt beim Client an");
        Pruefe.Falsch(vm.HatRueckfrage, "Frage weg");

        using var abbruch = new CancellationTokenSource();
        var zweite = vm.RueckfrageAsync(new VerknuepfungKonto { Name = "X", Benutzername = "x", Neu = true }, abbruch.Token);
        takt.Abarbeiten();
        await abbruch.CancelAsync();
        Pruefe.Falsch(await zweite.WaitAsync(TimeSpan.FromSeconds(5)), "Frist oder Abbruch gilt als Nein");

        // Den Gerätenamen gibt es seit 0.6.0 nur noch als Zeile in der Antragskarte (Q4). Seit 0.6.3 entsteht er aus der
        // Gerätekennung, nie aus dem Windows-PC (Sarevoc 04.10.2026); ein alter Name in der Datei verschwindet beim Lesen.
        a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
        a.Ablage.Aktuell.Verbindung.Normalisieren();
        Pruefe.Gleich(null, a.Ablage.Aktuell.Verbindung.Geraetename, "alter Windows-Name aus der Datei weg");
        Pruefe.Gleich(null, new VerbindungsEinstellungen().Geraetename, "ab Werk keiner");
        Pruefe.Wahr(vm.GeraetenameSatz.StartsWith("Im Antrag heißt dieses Gerät PC- ", StringComparison.Ordinal)
            && !vm.GeraetenameSatz.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase), "ohne Kennung: wie der Name entsteht: " + vm.GeraetenameSatz);
        Pruefe.Gleich("PC-7Q2K", VerbindungsEinstellungen.NameAusKennung("7Q2K-9M4X-D3FA-81C0"), "aus der Kennung");
        Pruefe.Gleich("PC-5CD2", VerbindungsEinstellungen.NameAusKennung(" 5cd2-52fb-0ce8-9324"), "groß, ohne Leerzeichen");
        Pruefe.Gleich("PC", VerbindungsEinstellungen.NameAusKennung("-"), "ohne Zeichen nur PC");
    }

    /// <summary>
    /// Zusammenspiel: FunkClientDienst über den echten FunkClient gegen den Dienst auf Loopback (am TLS vorbei wie in den
    /// Kern-Tests). Registrierungscode erscheint in der Lage, nach Verknüpfen und Freigabe wird sie AKTIV mit Netzen,
    /// Feldern und Konto; Sendeauswahl und Hören gehen durch; Trennen führt zurück.
    /// </summary>
    [Test]
    public static async Task Dienst_Zusammenspiel_MitDemEchtenDienst()
    {
        var halter = new ServerHalter();
        await using var t = await Testserver.StartenAsync();
        halter.Server = t;
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = new JsonObject
            {
                ["id"] = 2, ["name"] = "Kommando", ["typ"] = "NORMAL", ["reihenfolge"] = 2, ["nurTaste"] = false,
                ["ausloeserStandard"] = "TASTE", ["offen"] = "HOEREN_SENDEN", ["standard"] = true,
            },
        });
        var identitaet = Geraeteidentitaet.Fluechtig();
        await using var d = new FunkClientDienst(() => new FunkAnbindung(new FunkClient(new FunkClientOptionen
        {
            Aufbau = halter.Aufbau(identitaet, new IPAddress([127, 0, 8, 21])),
            Identitaet = identitaet,
            Geraetename = "PC Oberflaeche",
            ClientName = "Oberflaechentest",
            ClientVersion = "1.0.0",
            Rueckfrage = (_, _) => Task.FromResult(true),
            Zufall = () => 0.0,
        }), identitaet, "Testserver", identitaet.Fingerabdruck.Anzeige, "flüchtig"));

        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null, 10_000), "Code in der Lage, Status " + d.Lage.Status);
        Pruefe.Gleich(Verbindungsstatus.Registrierung, d.Lage.Status, "REGISTRIERUNG");
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = d.Lage.Registrierung!.Code, ["discord"] = "200000000000000077", ["benutzername"] = "test", ["stufe"] = "RAIDLEITER" });
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Warteraum, 5_000), "WARTERAUM, war " + d.Lage.Status);
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = identitaet.Fingerabdruck.Anzeige });
        Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv && d.Lage.Felder.Count > 0 && d.Lage.Netze.Count >= 2, 10_000), "AKTIV mit Feldern, war " + d.Lage.Status);

        var l = d.Lage;
        Pruefe.Gleich("Testserver", l.Server, "Server aus der Anbindung");
        Pruefe.Gleich(null, l.Registrierung, "Code weg");
        Pruefe.Wahr(l.Netze.Any(n => n is { Name: "Kommando", IstGlobal: false }) && l.Netze.Any(n => n.IstGlobal), "Kommando und Global");
        Pruefe.Wahr(l.Konto is { Stufe: Stufe.Raidleiter }, "Konto mit Stufe");
        var feld = l.Felder.Single(f => f.NetzId == 2);

        using var a = new Aufbau();
        var funkfeld = new FunkfeldViewModel(d, a.Maschine, a.Verlauf, a.Ablage, Funk.Client.Dienste.SofortTakt.Instanz);
        var kachel = funkfeld.Kacheln.Single(k => k.Platz == feld.Feld);
        Pruefe.Gleich("Kommando", kachel.Name, "Kachel zeigt das Netz vom Server");
        await kachel.SendenCommand.ExecuteAsync(null);
        Pruefe.Gleich(null, funkfeld.Hinweis, "Sendeauswahl angenommen");
        Pruefe.Wahr(d.Lage.SendeFelder.SequenceEqual(new[] { feld.Feld }), "Sendeauswahl lokal (C1)");
        Pruefe.Gleich(KachelZustand.Senden, kachel.Zustand, "Kachel sendet");
        Pruefe.Gleich(null, await d.HoerenSetzenAsync(2, false), "Hören aus angenommen");
        Pruefe.Wahr(await Warten(() => d.Lage.Felder.Single(f => f.NetzId == 2).Hoeren == false, 5_000), "Mitgliedschaften zurückgemeldet");
        funkfeld.Dispose();

        await d.TrennenAsync();
        Pruefe.Gleich(Verbindungsstatus.Getrennt, d.Lage.Status, "getrennt");
        Pruefe.Gleich("Getrennt", d.Lage.Hinweis, "Hinweis");
    }

    /// <summary>
    /// Seit 0.6.3 (Sarevoc, 04.10.2026): Entfernt der Admin das Gerät, verbindet der Client nicht von selbst neu und stellt keinen
    /// stillen neuen Antrag; die Lage sagt "entfernt" (FunkLage.Entfernt) mit dem Satz "vom Funk abgemeldet", bis der Server
    /// wieder antwortet. Vorher verband er gleich neu, und statt des Grunds stand etwa "Name schon vergeben" da.
    /// </summary>
    [Test]
    public static async Task Dienst_Entfernt_KeinStillerNeuantrag()
    {
        var halter = new ServerHalter();
        await using var t = await Testserver.StartenAsync();
        halter.Server = t;
        var identitaet = Geraeteidentitaet.Fluechtig();
        FunkClient? client = null;
        await using var d = new FunkClientDienst(() =>
        {
            client = new FunkClient(new FunkClientOptionen
            {
                Aufbau = halter.Aufbau(identitaet, new IPAddress([127, 0, 8, 22])),
                Identitaet = identitaet,
                Geraetename = "PC-AB12",
                ClientName = "Oberflaechentest",
                ClientVersion = "1.0.0",
                Rueckfrage = (_, _) => Task.FromResult(true),
                Zufall = () => 0.0,
            });
            return new FunkAnbindung(client, identitaet, "Testserver", identitaet.Fingerabdruck.Anzeige, "flüchtig");
        });

        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null, 10_000), "Code in der Lage, Status " + d.Lage.Status);
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = d.Lage.Registrierung!.Code, ["discord"] = "200000000000000078", ["benutzername"] = "otto", ["stufe"] = "RAIDLEITER" });
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Warteraum, 5_000), "WARTERAUM, war " + d.Lage.Status);
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = identitaet.Fingerabdruck.Anzeige });
        Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv, 10_000), "AKTIV, war " + d.Lage.Status);
        Pruefe.Falsch(d.Lage.Entfernt, "aktiv: nicht entfernt");

        await t.AdminOk("entfernen", new JsonObject { ["geraet"] = identitaet.Fingerabdruck.Anzeige });
        Pruefe.Wahr(await Warten(() => d.Lage.Entfernt && d.Lage.Status == Verbindungsstatus.Getrennt, 5_000),
            "entfernt und getrennt, war " + d.Lage.Status + ", entfernt " + d.Lage.Entfernt);
        Pruefe.Wahr(client!.NeuversuchGesperrt, "kein automatischer Neuversuch, also kein stiller neuer Antrag");
        Pruefe.Wahr(d.Lage.Hinweis is { } h && h.StartsWith("Dieses Gerät wurde vom Funk abgemeldet", StringComparison.Ordinal)
            && h.EndsWith("(Fehler " + Fehlernummern.GeraetEntfernt + ")", StringComparison.Ordinal), "Hinweis: " + d.Lage.Hinweis);

        // Erst der Nutzer verbindet neu. Antwortet der Server (hier mit einem neuen Code, das Gerät ist ihm unbekannt), gilt
        // "entfernt" nicht mehr.
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null && !d.Lage.Entfernt, 10_000),
            "neu angemeldet, Status " + d.Lage.Status + ", entfernt " + d.Lage.Entfernt);
        await d.TrennenAsync();
    }

    private static async Task<bool> Warten(Func<bool> bedingung, int ms)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(10);
        }
        return true;
    }
}
