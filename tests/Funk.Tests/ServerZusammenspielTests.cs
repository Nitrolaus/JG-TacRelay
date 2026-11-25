// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Zusammenspiel des Dienstes auf Loopback: Anmeldung bis AKTIV, Ablehnung und Sperre, Verdrängung, UDP-Weg eines
/// Pakets von A über den Server zu B mit Stempel, Keepalive, Tunnel, Rotation, Ratengrenzen, Neustart. Die
/// Verbindungen gehen am TLS-Handshake vorbei über denselben Weg wie danach; echtes TLS prüft <see cref="ServerTlsTests"/>.
/// </summary>
internal static class ServerZusammenspielTests
{
    private const string DiscordA = "111111111111111111";
    private const string DiscordB = "222222222222222222";
    private const string DiscordC = "333333333333333333";

    /// <summary>Opus-TOC 0xF8: CELT, config 31 (20 ms), mono, ein Frame (5.13).</summary>
    private static readonly byte[] Opus = [0xF8, 0x11, 0x22, 0x33, 0x44];

    private static JsonObject Netz(ushort id, string name) => new()
    {
        ["objekt"] = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["typ"] = "NORMAL",
            ["reihenfolge"] = id,
            ["nurTaste"] = false,
            ["ausloeserStandard"] = "TASTE",
            ["offen"] = "HOEREN_SENDEN",
            ["standard"] = true,
        },
    };

    [Test]
    public static async Task Anmeldung_UnbekanntBisAktiv_UndZustandGespeichert()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2, "Kommando"));
        await using var f = await t.VerbindenAsync();
        var w = await f.HelloAsync();
        Pruefe.Gleich(WelcomeStatus.Unbekannt, w.Status, "Welcome UNBEKANNT");
        // P-44: keine Serverversion, keine Sitzung, keine Namen oder Zahlen.
        Pruefe.Wahr(w.Server is null && w.SitzungsId is null && w.Konto is null && w.Profil is null && w.ZustandsVersion is null, "Welcome UNBEKANNT verrät nichts");
        Pruefe.Gleich((byte)12, w.Registrierung!.CodeLaenge, "Codelänge");

        var code = await f.RegistrierenAsync("Mein PC");
        Pruefe.Gleich(14, code.Length, "Code in Anzeigeform XXXX-XXXX-XXXX");
        var verknuepfen = t.Admin("verknuepfen", new JsonObject { ["code"] = code, ["discord"] = DiscordA, ["benutzername"] = "anna", ["stufe"] = "ADMIN" });
        var rueckfrage = await f.ErwarteAsync(Nachrichtentyp.VerknuepfungBestaetigen);
        var konto = rueckfrage.Json<VerknuepfungBestaetigenAnfrage>().Konto;
        Pruefe.Wahr(konto.Neu && konto.Benutzername == "anna", "Rückfrage nennt das neue Konto");
        await f.AntwortAsync(Nachrichtentyp.VerknuepfungBestaetigen, rueckfrage.Kopf.AnfrageNr, new VerknuepfungBestaetigenAntwort { Ja = true });
        var v = await verknuepfen;
        Pruefe.Wahr(v.Ok, "verknuepfen: " + v.Text);
        Pruefe.Gleich(GeraeteStatus.Wartet, (await f.ErwarteAsync(Nachrichtentyp.GeraetStatus)).Json<GeraetStatus>().Status, "WARTET");

        // Kein Weg vom Code zu FREIGEGEBEN: erst die Freigabe per Anzeigeform setzt fort.
        Pruefe.Falsch(f.Hat(r => r.Typ == Nachrichtentyp.Welcome), "vor der Freigabe kein zweites Welcome");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = f.Fingerabdruck.Anzeige });
        await f.SyncErwartenAsync();
        Pruefe.Wahr(f.SitzungsId != 0 && f.KontoId != 0, "Sitzung und Konto aus Welcome");
        Pruefe.Gleich(1, f.VoiceKeysErhalten, "VoiceKeys START beantwortet");

        var ping = await f.AnfrageAsync(Nachrichtentyp.Ping, new PingAnfrage { Nr = 77 });
        Pruefe.Gleich(77u, ping.Json<PingAntwort>().Nr, "Ping-Antwort");

        var zustand = await f.AnfrageAsync(Nachrichtentyp.Zustand, LeererBody.Wert);
        Pruefe.Gleich(Nachrichtentyp.Zustand, zustand.Typ, "Antwort auf Zustand");
        var m = (await f.ErwarteAsync(Nachrichtentyp.Mitgliedschaften)).Json<Mitgliedschaften>();
        Pruefe.Wahr(m.Felder.Any(x => x.NetzId == 2 && x.Hoeren && x.Senden), "STANDARD-Feld Kommando");

        var zustandsdatei = Path.Combine(t.Verzeichnis, "zustand", "konten.json");
        for (var i = 0; i < 50 && !File.Exists(zustandsdatei); i++) await Task.Delay(20);
        Pruefe.Wahr(File.Exists(zustandsdatei), "konten.json geschrieben");
        Pruefe.Wahr(File.Exists(Path.Combine(t.Verzeichnis, "zustand", "lage.json")), "lage.json geschrieben");

        // P-46 (Anteil Server): kein Einmalcode und keine Klartext-IP im Logbuch.
        Pruefe.Falsch(t.Logbuch.Any(z => z.Contains(code, StringComparison.Ordinal) || z.Contains(code.Replace("-", "", StringComparison.Ordinal), StringComparison.Ordinal)), "kein Code im Logbuch");
        Pruefe.Falsch(t.Logbuch.Any(z => z.Contains("127.0.0.1", StringComparison.Ordinal)), "keine Klartext-IP im Logbuch");
    }

    [Test]
    public static async Task Anmeldung_VorHelloUndInRegistrierungNurErlaubtes()
    {
        await using var t = await Testserver.StartenAsync();
        await using var f = await t.VerbindenAsync();
        // In WARTE_HELLO ist nur Hello erlaubt (4.6): alles andere ergibt PROTOKOLL und ENDE.
        await f.SendenAsync(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, f.NaechsteNr());
        var fehler = (await f.ErwarteAsync(Nachrichtentyp.Fehler)).Json<Fehler>();
        Pruefe.Gleich(Fehlercode.Protokoll, fehler.Code, "PROTOKOLL vor Hello");
        await f.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));

        await using var g = await t.VerbindenAsync();
        await g.HelloAsync();
        // REGISTRIERUNG: keine Anfragen aus AKTIV (P-44, 4.6).
        await g.SendenAsync(Nachrichtentyp.HoerenSetzen, new HoerenSetzenAnfrage { NetzId = 2, Hoeren = true }, g.NaechsteNr());
        Pruefe.Gleich(Fehlercode.Protokoll, (await g.ErwarteAsync(Nachrichtentyp.Fehler)).Json<Fehler>().Code, "PROTOKOLL in REGISTRIERUNG");
        await g.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));
    }

    [Test]
    public static async Task Anmeldung_VersionZuAlt()
    {
        await using var t = await Testserver.StartenAsync();
        await using var f = await t.VerbindenAsync();
        await f.SendenAsync(Nachrichtentyp.Hello, new Hello
        {
            Minor = 0,
            Client = new ClientAngabe { Name = "alt", Version = "0.0.9" },
            SchluesselArt = SchluesselArt.Software,
            Opus = "1.6.1",
            Faehigkeiten = [],
        });
        var fehler = (await f.ErwarteAsync(Nachrichtentyp.Fehler)).Json<Fehler>();
        Pruefe.Gleich(Fehlercode.VersionZuAlt, fehler.Code, "VERSION_ZU_ALT statt Welcome");
        await f.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));
    }

    [Test]
    public static async Task Ablehnung_LoeschtAntragOhneSperre()
    {
        await using var t = await Testserver.StartenAsync();
        // Abgelehnt im WARTERAUM: GeraetStatus{ABGELEHNT}, dann Bye{ENTFERNT} und Trennung (3.5). Seit 0.7.0 löscht Ablehnen das Gerät,
        // gesperrt wird nichts (Sarevoc 04.10.2026); das Bye hält den Client davon ab, gleich von selbst neu zu beantragen.
        var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameter = schluessel.ExportParameters(includePrivateParameters: true);
        var f = await t.VerbindenAsync(schluessel);
        await f.HelloAsync();
        var code = await f.RegistrierenAsync("PC");
        var verknuepfen = t.Admin("verknuepfen", new JsonObject { ["code"] = code, ["discord"] = DiscordC, ["benutzername"] = "carl" });
        var r = await f.ErwarteAsync(Nachrichtentyp.VerknuepfungBestaetigen);
        await f.AntwortAsync(Nachrichtentyp.VerknuepfungBestaetigen, r.Kopf.AnfrageNr, new VerknuepfungBestaetigenAntwort { Ja = true });
        Pruefe.Wahr((await verknuepfen).Ok, "verknuepft");
        await f.ErwarteAsync(Nachrichtentyp.GeraetStatus);
        await t.AdminOk("ablehnen", new JsonObject { ["geraet"] = f.Fingerabdruck.AlsBase64() });
        Pruefe.Gleich(GeraeteStatus.Abgelehnt, (await f.ErwarteAsync(Nachrichtentyp.GeraetStatus)).Json<GeraetStatus>().Status, "ABGELEHNT");
        Pruefe.Gleich(ByeGrund.Entfernt, (await f.ErwarteAsync(Nachrichtentyp.Bye)).Json<Bye>().Grund, "Bye ENTFERNT: kein stiller Neuantrag");
        await f.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));
        await f.DisposeAsync();

        // Derselbe Schlüssel kommt wieder: unbekannt, er darf einen neuen Antrag stellen (4.6 WARTE_HELLO). Bis 0.6.x: GERAET_GESPERRT.
        await using var wieder = await t.VerbindenAsync(ECDsa.Create(parameter));
        Pruefe.Gleich(WelcomeStatus.Unbekannt, (await wieder.HelloAsync()).Status, "nicht gesperrt: Welcome UNBEKANNT");
    }


    /// <summary>
    /// Sperre aufheben an der Konsole (seit 0.8.0): Nach "sperren" bleibt der PC draußen, nach "sperre-aufheben" mit der Anzeigeform
    /// ist er unbekannt und darf neu beantragen; freigegeben ist er nicht. Das Journal nennt es, ein zweites Mal ist nichts mehr gesperrt.
    /// </summary>
    [Test]
    public static async Task Sperre_KonsoleHebtSieAuf()
    {
        await using var t = await Testserver.StartenAsync();
        var a = await t.AktivAsync(DiscordA, "anna");
        var parameter = a.Schluessel.ExportParameters(includePrivateParameters: true);
        var anzeige = a.Fingerabdruck.Anzeige;
        await t.AdminOk("sperren", new JsonObject { ["geraet"] = a.Fingerabdruck.AlsBase64(), ["grund"] = "Test" });
        Pruefe.Gleich(ByeGrund.Entzogen, (await a.ErwarteAsync(Nachrichtentyp.Bye)).Json<Bye>().Grund, "Bye ENTZOGEN");
        await a.WartenAufTrennungAsync(TimeSpan.FromSeconds(1));
        await a.DisposeAsync();

        var auf = await t.AdminOk("sperre-aufheben", new JsonObject { ["geraet"] = anzeige });
        Pruefe.Wahr(auf.Text.Contains("nicht mehr gesperrt", StringComparison.Ordinal), auf.Text);
        var nochmal = await t.Admin("sperre-aufheben", new JsonObject { ["geraet"] = anzeige });
        Pruefe.Wahr(!nochmal.Ok && nochmal.Text.Contains("nicht gesperrt", StringComparison.Ordinal), "ein zweites Mal: " + nochmal.Text);
        var journal = await t.AdminOk("journal");
        Pruefe.Wahr(journal.Text.Contains("SPERRE_AUFGEHOBEN", StringComparison.Ordinal), "Journal vermerkt das Aufheben");

        await using var wieder = await t.VerbindenAsync(ECDsa.Create(parameter));
        Pruefe.Gleich(WelcomeStatus.Unbekannt, (await wieder.HelloAsync()).Status, "nach dem Aufheben: unbekannt, neuer Antrag möglich");
    }

    [Test]
    public static async Task Sperre_EntzugUndKontosperreTrennenSofort()
    {
        await using var t = await Testserver.StartenAsync();
        await using var a = await t.AktivAsync(DiscordA, "anna");
        await using var b = await t.AktivAsync(DiscordB, "bert");

        // Gerät dauerhaft sperren (ENTZOGEN): Bye{ENTZOGEN} und Trennung (3.6).
        await t.AdminOk("sperren", new JsonObject { ["geraet"] = a.Fingerabdruck.AlsBase64(), ["grund"] = "Test" });
        Pruefe.Gleich(ByeGrund.Entzogen, (await a.ErwarteAsync(Nachrichtentyp.Bye)).Json<Bye>().Grund, "Bye ENTZOGEN");
        await a.WartenAufTrennungAsync(TimeSpan.FromSeconds(1));

        // Konto sperren: Fehler{KONTO_GESPERRT} binnen 1 s (P-12).
        await t.AdminOk("konto-sperren", new JsonObject { ["konto"] = b.KontoId });
        Pruefe.Gleich(Fehlercode.KontoGesperrt, (await b.ErwarteAsync(Nachrichtentyp.Fehler, TimeSpan.FromSeconds(1))).Json<Fehler>().Code, "KONTO_GESPERRT");
        await b.WartenAufTrennungAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public static async Task Verdraengung_ZweitesGeraetUndErsetzt()
    {
        await using var t = await Testserver.StartenAsync();
        await using var erstes = await t.AktivAsync(DiscordA, "anna");

        // Zweites Gerät desselben Kontos: nach Freigabe verdrängt es die ältere Sitzung (4.6).
        await using var zweites = await t.VerbindenAsync();
        await zweites.HelloAsync();
        var code = await zweites.RegistrierenAsync("Laptop");
        var verknuepfen = t.Admin("verknuepfen", new JsonObject { ["code"] = code, ["discord"] = DiscordA, ["benutzername"] = "anna" });
        var r = await zweites.ErwarteAsync(Nachrichtentyp.VerknuepfungBestaetigen);
        Pruefe.Falsch(r.Json<VerknuepfungBestaetigenAnfrage>().Konto.Neu, "vorhandenes Konto");
        await zweites.AntwortAsync(Nachrichtentyp.VerknuepfungBestaetigen, r.Kopf.AnfrageNr, new VerknuepfungBestaetigenAntwort { Ja = true });
        Pruefe.Wahr((await verknuepfen).Ok, "verknuepft");
        await zweites.ErwarteAsync(Nachrichtentyp.GeraetStatus);
        // Das freigegebene Erstgerät sieht die Anfrage (3.4 Schritt 7).
        Pruefe.Gleich(GeraeteStatus.Wartet, (await erstes.ErwarteAsync(Nachrichtentyp.GeraetStatus)).Json<GeraetStatus>().Status, "Inhaber sieht WARTET");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = zweites.Fingerabdruck.AlsBase64() });
        await zweites.SyncErwartenAsync();
        var bye = (await erstes.ErwarteAsync(Nachrichtentyp.Bye)).Json<Bye>();
        Pruefe.Gleich(ByeGrund.Verdraengt, bye.Grund, "Bye VERDRAENGT");
        Pruefe.Gleich("Laptop", bye.Durch!.GeraetName, "durch nennt das neue Gerät");
        Pruefe.Gleich(zweites.Fingerabdruck.Anzeige, bye.Durch.Anzeige, "durch.anzeige");
        await erstes.WartenAufTrennungAsync(TimeSpan.FromSeconds(2));

        // Dasselbe Gerät verbindet neu: die alte Sitzung endet mit ERSETZT und zählt nicht als Verdrängung.
        var nochmal = await t.VerbindenAsync(ECDsa.Create(zweites.Schluessel.ExportParameters(true)));
        await using (nochmal)
        {
            var w = await nochmal.HelloAsync();
            Pruefe.Gleich(WelcomeStatus.Freigegeben, w.Status, "freigegebenes Gerät geht direkt nach AKTIV_SYNC");
            Pruefe.Gleich(ByeGrund.Ersetzt, (await zweites.ErwarteAsync(Nachrichtentyp.Bye)).Json<Bye>().Grund, "Bye ERSETZT");
        }
    }

    [Test]
    public static async Task Udp_PaketVonAUeberServerZuBMitStempel()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2, "Kommando"));
        await using var a = await t.AktivAsync(DiscordA, "anna");
        await using var b = await t.AktivAsync(DiscordB, "bert");
        a.UdpVerbinden();
        b.UdpVerbinden();
        await a.UdpBindenAsync();
        await b.UdpBindenAsync();

        a.SpracheSenden([2], Opus, frame: 1000, phase: 7, kz: Kennzeichen.Beginn);
        var erstes = await b.UdpEmpfangenAsync(TimeSpan.FromSeconds(2));
        Pruefe.Wahr(erstes is not null, "B empfängt das Paket");
        var (kopf, klartext) = erstes!.Value;
        Pruefe.Gleich(PaketTyp.SpracheRunter, kopf.Typ, "SPRACHE_RUNTER");
        Pruefe.Gleich(b.SitzungsId, kopf.SitzungsId, "Sitzungs-ID des Empfängers im Kopf");
        Pruefe.Gleich(Formfehler.Keiner, SpracheRunter.Lesen(klartext, out var runter), "Form");
        Pruefe.Gleich(a.KontoId, runter.Absender, "Absender vom Server gestempelt");
        Pruefe.Gleich(1000u, runter.FrameNr, "Frame-Nr übernommen");
        Pruefe.Gleich((ushort)7, runter.Sprechphase, "Sprechphase übernommen");
        Pruefe.Wahr((runter.Kennzeichen & Kennzeichen.Beginn) != 0, "BEGINN beim ersten Paket an diesen Empfänger");
        Pruefe.Gleich(Spruchart.Netz, runter.Art, "Art NETZ");
        Pruefe.Gleich(1, runter.NetzAnzahl, "ein Netz");
        Pruefe.Gleich((ushort)2, runter.Netz(0), "Netz Kommando");
        Pruefe.BytesGleich(Opus, runter.Opus, "Opus unverändert");

        a.SpracheSenden([2], Opus, frame: 1001, phase: 7);
        var zweites = await b.UdpEmpfangenAsync(TimeSpan.FromSeconds(2));
        Pruefe.Wahr(zweites is not null, "zweites Paket");
        SpracheRunter.Lesen(zweites!.Value.Klartext, out var runter2);
        Pruefe.Gleich(Kennzeichen.None, runter2.Kennzeichen & Kennzeichen.Beginn, "kein zweites BEGINN");
        Pruefe.Wahr(await a.UdpEmpfangenAsync(TimeSpan.FromMilliseconds(300)) is null, "der Absender bekommt seinen Spruch nie zurück");

        // Ping zählt die UDP-Datagramme je Richtung (5.12).
        var ping = (await b.AnfrageAsync(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 })).Json<PingAntwort>();
        Pruefe.Wahr(ping.UdpGesendet >= 3, "udpGesendet zählt PFAD_PRUEFUNG und Sprache an B");
    }

    [Test]
    public static async Task Udp_KeepaliveEchoNurAnGebundeneAdresse()
    {
        await using var t = await Testserver.StartenAsync();
        await using var a = await t.AktivAsync(DiscordA, "anna");
        a.UdpVerbinden();
        await a.UdpBindenAsync();
        var klartext = new byte[Steuerpakete.Laenge];
        new Keepalive(5, 1234).Schreiben(klartext);
        a.UdpSenden(PaketTyp.Keepalive, klartext);
        var echo = await a.UdpEmpfangenAsync(TimeSpan.FromSeconds(2));
        Pruefe.Wahr(echo is not null && echo.Value.Kopf.Typ == PaketTyp.KeepaliveEcho, "KEEPALIVE_ECHO");
        Pruefe.Wahr(KeepaliveEcho.TryLesen(echo!.Value.Klartext, out var e), "Echo lesbar");
        Pruefe.Gleich(5u, e.Nr, "nr gespiegelt");
        Pruefe.Gleich(1234u, e.ZeitMs, "zeitMs gespiegelt");

        // Ungültiges (falscher Tag) bleibt ohne jede Antwort (P-40).
        a.Versiegeln(PaketTyp.Keepalive, klartext, out var d);
        d[^1] ^= 0xFF;
        a.RohUdpSenden(d);
        Pruefe.Wahr(await a.UdpEmpfangenAsync(TimeSpan.FromMilliseconds(300)) is null, "keine Antwort auf ein ungültiges Datagramm");
    }

    [Test]
    public static async Task Udp_FremdquelleMitMuellStoertGebundeneSitzungNicht()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2, "Kommando"));
        await using var a = await t.AktivAsync(DiscordA, "anna");
        await using var b = await t.AktivAsync(DiscordB, "bert");
        a.UdpVerbinden();
        b.UdpVerbinden();
        await a.UdpBindenAsync();
        await b.UdpBindenAsync();

        using var angreifer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows()) angreifer.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        angreifer.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        angreifer.Connect(t.Dienst.UdpEndpunkte[0]);
        var muell = new byte[100];
        for (var i = 0; i < 2000; i++)
        {
            RandomNumberGenerator.Fill(muell);
            muell[0] = 1;
            muell[1] = 0x10;
            muell[2] = 0;
            muell[3] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(muell.AsSpan(4), a.SitzungsId);
            BinaryPrimitives.WriteUInt64BigEndian(muell.AsSpan(8), (ulong)(100_000 + i));
            angreifer.Send(muell);
        }
        a.SpracheSenden([2], Opus, frame: 1, phase: 1);
        Pruefe.Wahr(await b.UdpEmpfangenAsync(TimeSpan.FromSeconds(2)) is not null, "echte Pakete von B kommen trotz Müll durch (Budget gebunden getrennt)");
        using var abbruch = new CancellationTokenSource(300);
        var antwort = false;
        try
        {
            await angreifer.ReceiveAsync(new byte[2048], SocketFlags.None, abbruch.Token);
            antwort = true;
        }
        catch (OperationCanceledException)
        {
        }
        Pruefe.Falsch(antwort, "der Server antwortet nie auf Unauthentisiertes (Verhältnis 0)");
    }

    [Test]
    public static async Task Tunnel_RunterUndHochUeberDenSteuerkanal()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2, "Kommando"));
        await using var a = await t.AktivAsync(DiscordA, "anna");
        await using var b = await t.AktivAsync(DiscordB, "bert");
        a.UdpVerbinden();
        await a.UdpBindenAsync();

        // B hat keine UDP-Bindung und schaltet den Tunnel ein (5.12: nur der Client schaltet).
        var an = await b.AnfrageAsync(Nachrichtentyp.TunnelModus, new TunnelModusAnfrage { An = true, Grund = TunnelGrund.KeineBindung });
        Pruefe.Wahr(an.Json<AnAus>().An, "TunnelModus an bestätigt");
        a.SpracheSenden([2], Opus, frame: 50, phase: 3, kz: Kennzeichen.Beginn);
        var rahmen = await b.ErwarteAsync(Nachrichtentyp.SpracheTunnel);
        var geoeffnet = b.Oeffnen(rahmen.Body);
        Pruefe.Wahr(geoeffnet is not null, "Tunnelrahmen trägt ein gültiges runter-Datagramm");
        SpracheRunter.Lesen(geoeffnet!.Value.Klartext, out var runter);
        Pruefe.Gleich(a.KontoId, runter.Absender, "Stempel im Tunnel");
        Pruefe.BytesGleich(Opus, runter.Opus, "Opus im Tunnel");

        // Hoch im Tunnel: B spricht, A hört über UDP.
        var klartext = new byte[Sprachkanal.MaxKlartext];
        var n = SpracheHoch.Schreiben(klartext, 9, 4, Kennzeichen.Beginn, Zielart.Netz, [2], Opus);
        await b.TunnelSendenAsync(PaketTyp.SpracheHoch, klartext[..n]);
        var beiA = await a.UdpEmpfangenAsync(TimeSpan.FromSeconds(2));
        Pruefe.Wahr(beiA is not null, "Sprache aus dem Tunnel geht per UDP weiter");
        SpracheRunter.Lesen(beiA!.Value.Klartext, out var runterA);
        Pruefe.Gleich(b.KontoId, runterA.Absender, "Absender B");
    }

    [Test]
    public static async Task Rotation_NeueEpocheOhneUnterbrechung()
    {
        await using var t = await Testserver.StartenAsync(rotationSekunden: 1);
        await t.AdminOk("netz-setzen", Netz(2, "Kommando"));
        await using var a = await t.AktivAsync(DiscordA, "anna");
        await using var b = await t.AktivAsync(DiscordB, "bert");
        a.UdpVerbinden();
        b.UdpVerbinden();
        await a.UdpBindenAsync();
        await b.UdpBindenAsync();
        var bis = DateTime.UtcNow.AddSeconds(8);
        while ((a.Epoche == 0 || b.Epoche == 0) && DateTime.UtcNow < bis) await Task.Delay(50);
        Pruefe.Wahr(a.Epoche >= 1 && b.Epoche >= 1, "VoiceKeys ROTATION kam an beide (5.8)");
        await Task.Delay(100);
        a.SpracheSenden([2], Opus, frame: 5, phase: 9, kz: Kennzeichen.Beginn);
        var r = await b.UdpEmpfangenAsync(TimeSpan.FromSeconds(2));
        if (r is null) foreach (var z in t.Logbuch) Console.WriteLine("  LOG " + z);
        Pruefe.Wahr(r is not null, "Sprache nach der Rotation");
        Pruefe.Wahr(r!.Value.Kopf.Epoche >= 1, "runter in der neuen Epoche");
    }

    [Test]
    public static async Task Ratengrenze_GrenzeUndTrennungMitGeraetesperre()
    {
        await using var t = await Testserver.StartenAsync();
        await using var a = await t.AktivAsync(DiscordA, "anna");
        // FeldSetzen: 5/s, Stoß 10 (4.8). Jede Anfrage bekommt genau eine Antwort.
        var nummern = new List<uint>();
        for (var i = 0; i < 16; i++)
        {
            var nr = a.NaechsteNr();
            nummern.Add(nr);
            await a.SendenAsync(Nachrichtentyp.FeldSetzen, new FeldSetzenAnfrage { Feld = 9 }, nr);
        }
        var grenze = 0;
        foreach (var nr in nummern)
        {
            var antwort = await a.ErwarteAsync(r => r.Kopf.IstAntwort && r.Kopf.AnfrageNr == nr);
            if (antwort.Typ == Nachrichtentyp.Fehler && antwort.Json<Fehler>().Code == Fehlercode.Grenze) grenze++;
        }
        Pruefe.Wahr(grenze >= 5, "über dem Stoß kommt GRENZE (" + grenze + ")");

        // Mehr als 50 Verstöße in 60 s: Trennung, Gerät 60 s gesperrt (nicht die IP).
        try
        {
            for (var i = 0; i < 60 && !a.Getrennt; i++)
                await a.SendenAsync(Nachrichtentyp.FeldSetzen, new FeldSetzenAnfrage { Feld = 9 }, a.NaechsteNr());
        }
        catch (IOException)
        {
            // Der Server hat schon getrennt.
        }
        await a.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));
        Pruefe.Wahr(a.Hat(r => r.Typ == Nachrichtentyp.Fehler && !r.Kopf.IstAntwort && r.Json<Fehler>().Code == Fehlercode.Grenze),
            "Abschied mit Fehler{GRENZE} vor der Trennung");
        await using var wieder = await t.VerbindenAsync(ECDsa.Create(a.Schluessel.ExportParameters(true)));
        await wieder.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));
        Pruefe.Falsch(wieder.Hat(r => r.Typ == Nachrichtentyp.Welcome), "gesperrtes Gerät bekommt kein Welcome");
        await using var anderer = await t.VerbindenAsync();
        Pruefe.Gleich(WelcomeStatus.Unbekannt, (await anderer.HelloAsync()).Status, "ein anderes Gerät derselben IP ist nicht betroffen");
    }

    [Test]
    public static async Task Zusammenfassen_ZustandsmeldungenOhneVerstoss()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2, "Kommando"));
        await using var a = await t.AktivAsync(DiscordA, "anna");
        var nummern = new List<uint>();
        for (var i = 0; i < 20; i++)
        {
            var nr = a.NaechsteNr();
            nummern.Add(nr);
            await a.SendenAsync(Nachrichtentyp.HoerenSetzen, new HoerenSetzenAnfrage { NetzId = 2, Hoeren = i % 2 == 0 }, nr);
        }
        var ueberholt = 0;
        foreach (var nr in nummern)
        {
            var antwort = await a.ErwarteAsync(r => r.Kopf.IstAntwort && r.Kopf.AnfrageNr == nr, TimeSpan.FromSeconds(10));
            if (antwort.Typ == Nachrichtentyp.Fehler)
            {
                var code = antwort.Json<Fehler>().Code;
                Pruefe.Gleich(Fehlercode.Ueberholt, code, "nur UEBERHOLT, nie GRENZE (4.8)");
                ueberholt++;
            }
        }
        Pruefe.Wahr(ueberholt > 0, "Überschuss wurde zusammengefasst");
        Pruefe.Falsch(a.Getrennt, "kein Verstoß");
    }

    [Test]
    public static async Task Neustart_KontoGeraetUndNetzeBleiben()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), Guid.NewGuid().ToString("N")[..16]);
        var t1 = await Testserver.StartenAsync(verzeichnis: verzeichnis);
        await t1.AdminOk("netz-setzen", Netz(2, "Kommando"));
        var a = await t1.AktivAsync(DiscordA, "anna");
        var parameter = a.Schluessel.ExportParameters(true);
        await a.DisposeAsync();
        await t1.DienstBeendenAsync();

        await using var t2 = await Testserver.StartenAsync(verzeichnis: verzeichnis);
        var konten = await t2.AdminOk("konten");
        Pruefe.Wahr(konten.Text.Contains("anna", StringComparison.Ordinal), "Konto nach dem Neustart vorhanden");
        await using var wieder = await t2.VerbindenAsync(ECDsa.Create(parameter));
        var w = await wieder.HelloAsync();
        Pruefe.Gleich(WelcomeStatus.Freigegeben, w.Status, "freigegebenes Gerät nach dem Neustart direkt AKTIV_SYNC");
        var m = (await wieder.ErwarteAsync(Nachrichtentyp.Mitgliedschaften)).Json<Mitgliedschaften>();
        Pruefe.Wahr(m.Felder.Any(f => f.NetzId == 2), "Mitgliedschaften nach dem Neustart");
        Pruefe.Gleich(MitgliedschaftsQuelle.Wiederhergestellt, m.Quelle, "WIEDERHERGESTELLT innerhalb der Frist (6.9)");
        await t1.DisposeAsync();
    }

    [Test]
    public static async Task Verwaltung_BefehleUndStatus()
    {
        await using var t = await Testserver.StartenAsync();
        var status = await t.AdminOk("status");
        Pruefe.Wahr(status.Text.Contains("0 Sitzungen", StringComparison.Ordinal), "status");
        var anlegen = await t.AdminOk("konto-anlegen", new JsonObject { ["discord"] = DiscordC, ["benutzername"] = "carl", ["stufe"] = "RAIDLEITER" });
        var id = anlegen.Daten!["kontoId"]!.GetValue<uint>();
        await t.AdminOk("rechte", new JsonObject { ["konto"] = id, ["rechte"] = new JsonArray("ZUWEISEN", "GLOBAL_SENDEN") });
        await t.AdminOk("netz-setzen", Netz(5, "Crew"));
        await t.AdminOk("rolle-setzen", new JsonObject
        {
            ["objekt"] = new JsonObject
            {
                ["id"] = 1,
                ["name"] = "Pilot",
                ["reihenfolge"] = 0,
                ["selbstWaehlbar"] = true,
                ["brauchtSchiff"] = false,
                ["felder"] = new JsonArray(new JsonObject { ["feld"] = 0, ["netzId"] = 5, ["senden"] = true, ["hoeren"] = true, ["sendenStandard"] = true, ["fest"] = false }),
            },
        });
        var liste = await t.AdminOk("netzliste");
        Pruefe.Wahr(liste.Text.Contains("Crew", StringComparison.Ordinal) && liste.Text.Contains("Pilot", StringComparison.Ordinal), "Netzliste zeigt Netz und Rolle");
        var falsch = await t.Admin("netz-setzen", Netz(1, "Global neu"));
        Pruefe.Falsch(falsch.Ok, "Global lässt sich nicht umtypisieren (4.5 Verwaltung)");
        var unbekannt = await t.Admin("gibtsnicht");
        Pruefe.Falsch(unbekannt.Ok, "unbekannter Befehl");
        var code = await t.Admin("verknuepfen", new JsonObject { ["code"] = "0000-0000-0000", ["discord"] = DiscordA, ["benutzername"] = "anna" });
        Pruefe.Gleich("CODE_UNGUELTIG", code.Text, "falscher Code einheitlich CODE_UNGUELTIG");
    }
}
