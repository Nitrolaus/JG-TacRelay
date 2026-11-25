// JG TacRelay - Maintainer: Sarevoc
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
/// Stufe 3, Schritt 3.4 gegen den echten Server im Prozess (ohne TLS, ohne Fenster, ohne Geräte): Das Funkfeld wählt über die
/// Rollenliste eine Rolle mit den gemerkten Ch., wechselt einen Ch. mit FeldSetzen und stellt die Rolle nach der
/// Wiederverbindungsfrist (10 min, Server setzt STANDARD) selbst wieder her. Der Sprechkanal des ClientKerns nimmt nach einem
/// Rollenwechsel den Sendevorschlag des Servers und sendet nie in eine Ruhe oder einen gesperrten Kanal (C1, C4).
/// </summary>
internal static class Stufe34DrahtTests
{
    private const string DiscordTest = "980000000000000001";
    private const string DiscordOtto = "980000000000000002";
    private const string DiscordFritz = "980000000000000003";
    private const string DiscordLeitung = "980000000000000005";

    private static ushort K(ushort netz, byte ch) => Kanalrechnung.Id(netz, ch);

    /// <summary>Brücke (100, Ruhe und Schiff 1 bis 5), Kommando (101, Ruhe, Kommando 1 und 2), Rolle 12 "3 Kapitän".</summary>
    private static async Task KanaeleAsync(Testserver t)
    {
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse(Stufe3VersionstorTests.KanalBrueckeJson) });
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":101,"name":"Kommando","typ":"NORMAL","reihenfolge":11,"nurTaste":true,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false,"kanaele":["Ruhe","Kommando 1","Kommando 2"]}"""),
        });
        await t.AdminOk("rolle-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":12,"name":"3 Kapitän","reihenfolge":12,"selbstWaehlbar":true,"brauchtSchiff":false,"felder":[{"feld":0,"netzId":101,"senden":true,"hoeren":true,"sendenStandard":true,"fest":false},{"feld":1,"netzId":100,"senden":true,"hoeren":true,"sendenStandard":false,"fest":false}]}"""),
        });
    }

    [Test]
    public static async Task Draht_RollenlisteChWaehlerUndRolleNachDerWiederverbindungsfrist()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        await KanaeleAsync(t);
        var halter = new ServerHalter { Server = t };
        using var identitaet = Geraeteidentitaet.Fluechtig();
        var client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = halter.Aufbau(identitaet, Lokal(241)),
            Identitaet = identitaet,
            Geraetename = "PC Otto",
            ClientName = "Kerntest",
            ClientVersion = "1.0.0",
            Rueckfrage = (_, _) => Task.FromResult(true),
            Empfaenger = new Sammler(),
            Zufall = () => 0.0,
        });
        await using var d = new FunkClientDienst(() => new FunkAnbindung(client, null, null, null, null));
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null), "Registrierungscode");
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = d.Lage.Registrierung!.Code, ["discord"] = DiscordOtto, ["benutzername"] = "otto", ["stufe"] = "FUNKER" });
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Warteraum), "Warteraum");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = identitaet.Fingerabdruck.Anzeige });
        Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv && d.Lage.Abgeglichen && d.Lage.ServerKanaele), "aktiv, abgeglichen, Server kennt Kanäle");
        var otto = d.Lage.Konto!.Id;
        Task<Funk.Server.Lage.Konto> Stand() => t.Dienst.Zentrale.Ausfuehren(() => t.Dienst.Zentrale.Schreiber.Aktuell.Konten[otto]);

        // Das Funkfeld lebt auf einem eigenen Thread mit Kontext wie auf dem Oberflächen-Thread.
        using var faden = new EinFaden();
        var takt = new FadenTakt(faden);
        long ms = 0;
        using var ordner = new Testordner();
        var speicher = new EinstellungsSpeicher(ordner.Datei("einstellungen.json"));
        using var einstellungen = new Einstellungsablage(speicher, speicher.Laden(), takt, TimeSpan.FromHours(1));
        var maschine = new KlangMaschinenAttrappe();
        var f = await faden.Ausfuehren(() => new FunkfeldViewModel(d, maschine, new PegelVerlauf(), einstellungen, takt, () => ms));
        // Bis 0.8.0 schickte der Client die gemerkten Ch. als Wünsche mit; seit 0.8.1 (Q46) beginnt jeder Ch. auf Ruhe.
        await faden.Ausfuehren(() => einstellungen.Aktuell.NetzFuer(100).Ch = 3);

        // Rolle aus der Liste, mit der Maus (Liste klappt zu): RolleWaehlen ohne Wünsche, alles auf Ruhe.
        Pruefe.Wahr(await faden.Warten(() => f.Rollen.Any(r => r.Text == "3 Kapitän")), "Rollenliste mit 3 Kapitän");
        await faden.Ausfuehren(() =>
        {
            f.Rolle = f.Rollen.Single(r => r.Text == "3 Kapitän");
            f.VormerkungenAbschicken();
            return true;
        });
        Pruefe.Wahr(await Warten(() => Stand().Result is { GewaehlteRolle: 12 } k && k.Felder.Any(x => x.Feld == 0 && x.NetzId == K(101, 0))
            && k.Felder.Any(x => x.Feld == 1 && x.NetzId == K(100, 0))), "Server: Rolle 12, Kommando und Brücke auf Ruhe, der alte Merkwert gilt nicht");
        Pruefe.Wahr(await faden.Warten(() => f.Rolle?.Text == "3 Kapitän" && f.Kacheln[0].ChText == "Ruhe" && f.Kacheln[1].ChText == "Ruhe"),
            "das Funkfeld zeigt Rolle und Ruhe");
        Pruefe.Wahr(await Warten(() => d.Lage.SendeFelder.Count == 0), "Kommando auf Ruhe sendet nicht: keine Sendeauswahl: " + string.Join(",", d.Lage.SendeFelder));

        // Ch.-Wechsel am Ch.-Wähler: nach 400 ms Ruhe ein FeldSetzen mit der Kanal-ID.
        await faden.Ausfuehren(() =>
        {
            f.Kacheln[0].ChWahl = f.Kacheln[0].ChListe[2];
            ms += FunkfeldViewModel.RuheMs;
            f.Takt();
            return true;
        });
        Pruefe.Wahr(await Warten(() => Stand().Result.Felder.Any(x => x.Feld == 0 && x.NetzId == K(101, 2))), "Server: Kommando 2");
        Pruefe.Wahr(await faden.Ausfuehren(() => einstellungen.Aktuell.NetzOderNull(101)?.Ch is null), "seit 0.8.1 merkt sich der Client den Ch. nicht");

        // Verbindung weg und innerhalb der Frist von 10 min zurück: Rolle und Felder sind wieder da wie vor der Trennung; der Client schickt dabei
        // keine gemerkten Ch. Die Testuhr stellt die Lage nicht (sie rechnet mit Zeit.LageMs); bis 0.11.0 stand hier "11 min später" mit einem
        // wirkungslosen Uhrschritt. Nach der Frist prüft der Teil von 0.11.0 unten (NeuVerbindenAsync).
        halter.Server = null;
        halter.HartTrennen();
        Pruefe.Wahr(await Warten(() => !d.Lage.IstAktiv), "getrennt");
        Pruefe.Wahr(await Warten(() => Stand().Result.SitzungsEnde > 0), "der Server hat das Ende der Sitzung");
        halter.Server = t;
        // Wie "Verbinden" bei laufender Schleife: sofort neu, ohne auf den nächsten Versuch zu warten (derselbe Client, dasselbe Gerät).
        client.NeuVerbinden();
        Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv && d.Lage.Abgeglichen), "wieder verbunden");
        Pruefe.Wahr(await Warten(() => Stand().Result is { GewaehlteRolle: 12 } k && k.Felder.Any(x => x.Feld == 0 && x.NetzId == K(101, 2))
            && k.Felder.Any(x => x.Feld == 1 && x.NetzId == K(100, 0))), "Rolle 12 wieder da, mit dem Stand vor der Trennung (Kommando 2, Brücke auf Ruhe): " + string.Join(",", Stand().Result.Felder.Select(x => x.NetzId)));
        Pruefe.Wahr(await faden.Warten(() => f.Rolle?.Text == "3 Kapitän"), "oben links wieder die Rolle");

        // Seit 0.11.0 (Zuordnung, Punkt 4): Teilt der Admin Otto ein, vergisst der Client die gemerkte Rolle nicht mehr. Innerhalb der
        // 12 h kommt Otto eingeteilt zurück (VERSCHIEBEN mit durch), danach ohne Einteilung auf seiner Rolle.
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(242));
        var testId = test.KontoId;
        var einteilen = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Verschieben,
            new VerschiebenAnfrage { Aenderungen = [new Verschiebung { KontoId = otto, RolleId = 12, SchiffId = 0 }] }));
        Pruefe.Wahr(einteilen.Ok, "Test teilt Otto auf den Platz 12 ein: " + einteilen.Fehler);
        Pruefe.Wahr(await Warten(() => d.Lage.Quelle == MitgliedschaftsQuelle.Verschieben && d.Lage.Rolle is null), "Otto eingeteilt, ohne gewählte Rolle");
        await Task.Delay(200);
        Pruefe.Wahr(await faden.Ausfuehren(() => true), "das Funkfeld hat die Lage gesehen");
        Pruefe.Gleich((int?)12, await faden.Ausfuehren(() => einstellungen.Aktuell.Funkfeld.Rolle), "die gemerkte Rolle bleibt");

        // Die Lage läuft auf der monotonen Serveruhr (Zeit.LageMs), nicht auf der Testuhr: Die Zeit ohne Verbindung wird deshalb
        // gestellt, indem Sitzungsende und Reservierung um sie zurückgesetzt werden (wie in RaidleitungGegenpruefungTests).
        async Task NeuVerbindenAsync(TimeSpan spaeter)
        {
            var endeVorher = (await Stand()).SitzungsEnde;
            halter.Server = null;
            halter.HartTrennen();
            Pruefe.Wahr(await Warten(() => !d.Lage.IstAktiv), "getrennt");
            Pruefe.Wahr(await Warten(() => Stand().Result.SitzungsEnde > endeVorher), "der Server hat das Ende dieser Sitzung");
            var z = t.Dienst.Zentrale;
            var ms = (long)spaeter.TotalMilliseconds;
            var gestellt = await z.Ausfuehren(() => z.Schreiber.Ausfuehren(new Funk.Server.Lage.Auftrag("Test: Zeit ohne Verbindung", true, w =>
            {
                var k = w.Konto(otto)!;
                w.KontoSetzen(k with { SitzungsEnde = k.SitzungsEnde - ms, Reservierung = k.Reservierung is { } r ? r with { Gesetzt = r.Gesetzt - ms } : null },
                    relevant: false);
                return new Funk.Server.Lage.Ergebnis();
            }), Funk.Server.Betrieb.Zeit.LageMs()));
            Pruefe.Wahr(gestellt.Erfolg, "Zeit gestellt");
            halter.Server = t;
            client.NeuVerbinden();
            Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv && d.Lage.Abgeglichen), "wieder verbunden");
        }

        await NeuVerbindenAsync(TimeSpan.FromMinutes(11));
        var eingeteilt = await Stand();
        Pruefe.Wahr(eingeteilt is { Quelle: Funk.Server.Lage.Quelle.Verschieben, GewaehlteRolle: null, Platz.RolleId: 12 } && eingeteilt.QuelleDurch == testId,
            "nach 11 min, innerhalb der 12 h: eingeteilt zurück, VERSCHIEBEN durch Test: " + eingeteilt.Quelle + " durch " + eingeteilt.QuelleDurch);
        await Task.Delay(300);
        Pruefe.Gleich(null, (await Stand()).GewaehlteRolle, "der Client schickt keine Rolle über die Einteilung");
        Pruefe.Gleich((int?)12, await faden.Ausfuehren(() => einstellungen.Aktuell.Funkfeld.Rolle), "und merkt sie weiter");

        await NeuVerbindenAsync(TimeSpan.FromHours(12));
        Pruefe.Wahr(await Warten(() => Stand().Result is { GewaehlteRolle: 12, Platz: null, Quelle: Funk.Server.Lage.Quelle.Selbst }),
            "nach 12 h 11 min ohne Einteilung: der Client stellt die gemerkte Rolle 12 wieder her: " + Stand().Result.Quelle);
        Pruefe.Wahr(await faden.Warten(() => f.Rolle?.Text == "3 Kapitän"), "oben links die Rolle");
        await faden.Ausfuehren(() =>
        {
            f.Dispose();
            return true;
        });
    }

    /// <summary>
    /// Zuordnung Punkt 3 (0.11.0) gegen den echten Server: Fritz ist offline, die Einsatzleitung teilt ihn auf den Platz 12 ein; beim Login
    /// steht in seiner Meldezeile die Ansage "Eingeteilt von ...: Platz" (bis 0.11.0 "Für heute eingeteilt von ..."), und oben links im
    /// Funkfeld die Platzrolle statt "Rolle wählen". Das
    /// prüft die echte Reihenfolge von Welcome, Mitgliedschaften und Abgleich.
    /// </summary>
    [Test]
    public static async Task Draht_AnsageBeimLogin_NachVorabEinteilung()
    {
        await using var t = await Testserver.StartenAsync();
        await KanaeleAsync(t);
        var halterFritz = new ServerHalter { Server = t };
        var halterLeitung = new ServerHalter { Server = t };
        using var identitaet = Geraeteidentitaet.Fluechtig();
        var client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = halterFritz.Aufbau(identitaet, Lokal(247)),
            Identitaet = identitaet,
            Geraetename = "PC Fritz",
            ClientName = "Kerntest",
            ClientVersion = "1.0.0",
            Rueckfrage = (_, _) => Task.FromResult(true),
            Empfaenger = new Sammler(),
            Zufall = () => 0.0,
        });
        await using var d = new FunkClientDienst(() => new FunkAnbindung(client, null, null, null, null));
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null), "Registrierungscode");
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = d.Lage.Registrierung!.Code, ["discord"] = DiscordFritz, ["benutzername"] = "fritz", ["stufe"] = "FUNKER" });
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Warteraum), "Warteraum");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = identitaet.Fingerabdruck.Anzeige });
        Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv && d.Lage.Abgeglichen && d.Lage.ServerKanaele), "aktiv, abgeglichen, Server kennt Kanäle");
        var fritz = d.Lage.Konto!.Id;
        Task<Funk.Server.Lage.Konto> Stand() => t.Dienst.Zentrale.Ausfuehren(() => t.Dienst.Zentrale.Schreiber.Aktuell.Konten[fritz]);
        await using var leitung = await KernFunker.AktivAsync(halterLeitung, DiscordLeitung, "nordlicht", "ADMIN", Lokal(248));

        using var faden = new EinFaden();
        var takt = new FadenTakt(faden);
        long ms = 0;
        using var ordner = new Testordner();
        var speicher = new EinstellungsSpeicher(ordner.Datei("einstellungen.json"));
        using var einstellungen = new Einstellungsablage(speicher, speicher.Laden(), takt, TimeSpan.FromHours(1));
        var zeile = await faden.Ausfuehren(() => new Meldezeile(() => ms));
        using var teilnehmer = await faden.Ausfuehren(() => new Teilnehmermeldungen(d, zeile, takt));
        var f = await faden.Ausfuehren(() => new FunkfeldViewModel(d, new KlangMaschinenAttrappe(), new PegelVerlauf(), einstellungen, takt, () => ms));
        Pruefe.Wahr(await faden.Warten(() => f.RolleName == "Rolle wählen"), "ohne Platz: Rolle wählen");

        // Fritz geht offline; die Leitung teilt ihn vorab ein.
        halterFritz.Server = null;
        halterFritz.HartTrennen();
        Pruefe.Wahr(await Warten(() => !d.Lage.IstAktiv), "getrennt");
        Pruefe.Wahr(await Warten(() => Stand().Result.SitzungsEnde > 0), "der Server hat das Ende der Sitzung");
        var einteilen = await Gedrosselt(() => leitung.Client.AnfrageAsync(Nachrichtentyp.Verschieben,
            new VerschiebenAnfrage { Aenderungen = [new Verschiebung { KontoId = fritz, RolleId = 12, SchiffId = 0 }] }));
        Pruefe.Wahr(einteilen.Ok, "die Leitung teilt Fritz offline auf den Platz 12 ein: " + einteilen.Fehler);
        Pruefe.Wahr(await faden.Ausfuehren(() => !zeile.Sichtbar), "offline: noch nichts");

        halterFritz.Server = t;
        client.NeuVerbinden();
        Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv && d.Lage.Abgeglichen), "wieder verbunden");
        Pruefe.Wahr(await Warten(() => d.Lage.Quelle == MitgliedschaftsQuelle.Verschieben && d.Lage.QuelleDurch == leitung.KontoId), "VERSCHIEBEN mit durch");
        // Seit der einzeiligen Meldezeile (0.11.0) beginnen Ansage und Umstellung gleich ("Eingeteilt von ..."); die Ansage erkennt der
        // Test an ihrem Schlüssel (bis dahin am "Für heute" vorn).
        Pruefe.Wahr(await faden.Warten(() => zeile.Zeigt(Teilnehmermeldungen.AnsageSchluessel) && zeile.Text.StartsWith("Eingeteilt von ", StringComparison.Ordinal)),
            "die Ansage beim Login: " + await faden.Ausfuehren(() => zeile.Text));
        var text = await faden.Ausfuehren(() => zeile.Text);
        Pruefe.Wahr(text.EndsWith("3 Kapitän", StringComparison.Ordinal) && !text.Contains('\n', StringComparison.Ordinal)
            && !await faden.Ausfuehren(() => zeile.Zeigt(Teilnehmermeldungen.UmstellungSchluessel)), "mit dem Platz, eine Zeile, keine Umstellung: " + text);
        Pruefe.Wahr(await faden.Warten(() => f.RolleName == "3 Kapitän" && f.Rolle is null), "oben links die Platzrolle: " + await faden.Ausfuehren(() => f.RolleName));
        await faden.Ausfuehren(() =>
        {
            f.Dispose();
            return true;
        });
    }

    [Test]
    public static async Task Draht_Sprechkanal_NieInRuheOderGesperrtenKanal()
    {
        await using var t = await Testserver.StartenAsync();
        await KanaeleAsync(t);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(251));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(252));
        Pruefe.Wahr((await Gedrosselt(() => otto.Client.RolleWaehlenAsync(12))).Ok, "Otto wählt Rolle 12 ohne Wünsche");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften is { Rolle: 12 } m && m.Felder.All(x => Kanalrechnung.IstRuhe(x.NetzId))), "beide Felder auf Ruhe");
        await otto.Client.SendeauswahlSetzenAsync([0, 1], false);
        Pruefe.Gleich(null, otto.Client.Sprechen.ZielAusAuswahl(), "Ruhe sendet nie: kein Ziel");

        Pruefe.Wahr((await Gedrosselt(() => otto.Client.FeldSetzenAsync(1, K(100, 3)))).Ok, "Schiff 3 in Feld 1");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.FeldVon(1)?.NetzId == K(100, 3)), "Feld 1 auf Schiff 3");
        await otto.Client.SendeauswahlSetzenAsync([0, 1], false);
        Pruefe.Wahr(otto.Client.Sprechen.ZielAusAuswahl() is { } z && z.Netze.SequenceEqual([K(100, 3)]), "nur Schiff 3, nicht die Ruhe von Kommando");
        Pruefe.Wahr(otto.Client.Sprechen.Beginnen(Sendeziel.Netz(K(100, 3))), "Otto spricht in Schiff 3");

        var sperre = await Gedrosselt(() => test.Client.KanalSperreAsync(KanalSperreOperation.Setzen, otto.KontoId, K(100, 3)));
        Pruefe.Wahr(sperre.Ok, "Test sperrt Schiff 3 für Otto: " + sperre.Fehler);
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften?.Gesperrt is { Count: > 0 }), "Otto bekommt die Sperre");
        Pruefe.Wahr(await Warten(() => !otto.Client.Sprechen.Aktiv), "die laufende Phase endet (C4)");
        await otto.Client.SendeauswahlSetzenAsync([1], false);
        Pruefe.Gleich(null, otto.Client.Sprechen.ZielAusAuswahl(), "ein gesperrter Kanal ist nie Ziel");
    }
}
