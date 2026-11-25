// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.SteuerHilfe;

namespace Funk.Tests;

/// <summary>
/// Randfälle und Befunde der Gegenprüfung des Steuerkanals: Antwortpflicht bei GRENZE (4.2, 4.8), begrenzte
/// Tunnel-Warteschlange (4.2), Hello als erster Rahmen (4.5, 4.6), keine Geheimnisse in ToString (P-46),
/// Nummernraum, Epochenwechsel, leere und übergroße Eingaben.
/// </summary>
internal static class SteuerGegenpruefungTests
{
    private static RahmenKopf BremsKopf(Nachrichtentyp typ, Nachrichtenart art, int body = 20) =>
        new((uint)(body + 8), 1, art == Nachrichtenart.Antwort ? Rahmen.FlagAntwort : (byte)0, (ushort)typ, art == Nachrichtenart.Push ? 0u : 1u);

    private static Hello HelloMit(string name = "Funk", string version = "0.1.0") => new()
    {
        Minor = 0,
        Client = new ClientAngabe { Name = name, Version = version },
        SchluesselArt = SchluesselArt.Software,
        Opus = "1.6.1",
        Faehigkeiten = ["tunnel"],
    };

    // ---------------------------------------------------------------- 4.2 / 4.8: jede Anfrage bekommt eine Antwort

    [Test]
    public static void Bremse_JedeAnfrageUeberDerGrenzeBekommtEineAntwort()
    {
        // Verwaltung und Geräte-Nachrichten: 1/s, Stoß 5. Ohne Antwort bliebe jede verworfene Anfrage beim Client
        // offen; nach 32 solchen könnte er keine Anfrage mehr stellen (4.2).
        var b = new Steuerbremse(0);
        for (var i = 0; i < 5; i++)
        {
            Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(BremsKopf(Nachrichtentyp.GeraeteListe, Nachrichtenart.Anfrage), 0).Urteil, $"Stoß {i}");
        }
        for (var i = 0; i < 10; i++)
        {
            var e = b.Pruefen(BremsKopf(Nachrichtentyp.GeraeteListe, Nachrichtenart.Anfrage), 10 + i);
            Pruefe.Gleich(Bremsurteil.Grenze, e.Urteil, $"Verstoß {i}");
            Pruefe.Wahr(e.GrenzeMelden, $"Anfrage {i}: Fehler{{GRENZE}} als Antwort");
        }

        // Verworfene Pushes: höchstens eine Meldung je Sekunde (4.8).
        var c = new Steuerbremse(0);
        c.Pruefen(BremsKopf(Nachrichtentyp.EmpfangsBericht, Nachrichtenart.Push), 0);
        var m1 = c.Pruefen(BremsKopf(Nachrichtentyp.EmpfangsBericht, Nachrichtenart.Push), 100);
        var m2 = c.Pruefen(BremsKopf(Nachrichtentyp.EmpfangsBericht, Nachrichtenart.Push), 200);
        var m3 = c.Pruefen(BremsKopf(Nachrichtentyp.EmpfangsBericht, Nachrichtenart.Push), 1100);
        Pruefe.Wahr(m1.Urteil == Bremsurteil.Grenze && m1.GrenzeMelden, "erster Push-Verstoß gemeldet");
        Pruefe.Wahr(m2.Urteil == Bremsurteil.Grenze && !m2.GrenzeMelden, "zweiter binnen 1 s nicht");
        Pruefe.Wahr(m3.Urteil == Bremsurteil.Grenze && m3.GrenzeMelden, "nach 1 s wieder");

        // Auch mit Antwortpflicht trennt der 51. Verstoß in 60 s.
        var d = new Steuerbremse(0);
        for (var i = 0; i < 5; i++) d.Pruefen(BremsKopf(Nachrichtentyp.Verwaltung, Nachrichtenart.Anfrage), 0);
        for (var i = 0; i < 50; i++) Pruefe.Gleich(Bremsurteil.Grenze, d.Pruefen(BremsKopf(Nachrichtentyp.Verwaltung, Nachrichtenart.Anfrage), 0).Urteil, $"Verstoß {i + 1}");
        Pruefe.Gleich(Bremsurteil.Trennen, d.Pruefen(BremsKopf(Nachrichtentyp.Verwaltung, Nachrichtenart.Anfrage), 0).Urteil, "51. Verstoß");
    }

    [Test]
    public static void Bremse_Ping2JeSekunde()
    {
        Pruefe.Gleich(new Rate(2, 2), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.Ping)), "Ping 2/s");
        var b = new Steuerbremse(0);
        Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(BremsKopf(Nachrichtentyp.Ping, Nachrichtenart.Anfrage), 0).Urteil, "1");
        Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(BremsKopf(Nachrichtentyp.Ping, Nachrichtenart.Anfrage), 0).Urteil, "2");
        Pruefe.Gleich(Bremsurteil.Grenze, b.Pruefen(BremsKopf(Nachrichtentyp.Ping, Nachrichtenart.Anfrage), 0).Urteil, "3 im selben Augenblick");
        Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(BremsKopf(Nachrichtentyp.Ping, Nachrichtenart.Anfrage), 500).Urteil, "nach 500 ms");
        for (long t = 2000; t < 60_000; t += 2000)
        {
            Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(BremsKopf(Nachrichtentyp.Ping, Nachrichtenart.Anfrage), t).Urteil, $"Takt alle 2 s: {t}");
        }
    }

    // ---------------------------------------------------------------- 4.2: Tunnel-Warteschlange bleibt begrenzt

    [Test]
    public static void Senden_StehendeSchreibschleife_SpeicherBleibtBegrenzt()
    {
        var frei = 0;
        var q = new Sendewarteschlange<int>(_ => frei++);
        // 20 Absender; die Schreibschleife steht, weil der Client nicht liest.
        for (var i = 0; i < 10_000; i++) q.TunnelEinreihen((uint)(i % 20), i, i);
        var vorher = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 10_000; i < 200_000; i++) q.TunnelEinreihen((uint)(i % 20), i, i);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - vorher;
        Pruefe.Gleich(60, q.Tunnelrahmen, "20 Absender zu je 3");
        Pruefe.Gleich(0L, bytes, "kein Wachstum, solange je Absender nur 3 anliegen");
        Pruefe.Gleich(200_000 - 60, frei, "jeder verdrängte Rahmen freigegeben");

        // Das Verdichten hält die Reihenfolge: die jüngsten drei je Absender, wie eingereiht.
        var folge = new List<int>();
        while (q.VersucheEntnehmen(200_000, out var r)) folge.Add(r);
        Pruefe.Gleich(string.Join(',', Enumerable.Range(200_000 - 60, 60)), string.Join(',', folge), "Reihenfolge");
        Pruefe.Gleich(0, q.Tunnelrahmen, "leer");
    }

    [Test]
    public static void Senden_VerdichtenMitVielenAbsendern_Wachsen()
    {
        // Mehr aktive Einträge als der Anfangsring fasst: Verdichten allein reicht nicht, der Ring wächst.
        var q = new Sendewarteschlange<int>();
        for (var i = 0; i < 300; i++) q.TunnelEinreihen((uint)i, i, 0);
        Pruefe.Gleich(300, q.Tunnelrahmen, "300 Absender zu je 1");
        for (var i = 0; i < 300; i++) q.TunnelEinreihen((uint)i, 1000 + i, 0);
        var folge = new List<int>();
        while (q.VersucheEntnehmen(0, out var r)) folge.Add(r);
        Pruefe.Gleich(600, folge.Count, "alle da");
        Pruefe.Wahr(folge.SequenceEqual(folge.Order()), "Reihenfolge erhalten");
    }

    // ---------------------------------------------------------------- 4.5 / 4.6: Hello ist der erste Rahmen

    [Test]
    public static void P10_VorHello_NurEinGueltigesHello()
    {
        (string Was, byte[] Rahmen, Fehlercode Code)[] faelle =
        [
            ("optionaler JSON-Typ", Rahmen.Bauen(0x4FFF, 0, 0, "{}"u8), Fehlercode.Protokoll),
            ("optionaler Binärtyp", Rahmen.Bauen(0xC001, 0, 0, [1]), Fehlercode.Protokoll),
            ("Hello ohne Pflichtfelder", Rahmen.Bauen((ushort)Nachrichtentyp.Hello, 0, 0, "{\"minor\":0}"u8), Fehlercode.Protokoll),
            ("Hello mit Nicht-SemVer", Json(Nachrichtentyp.Hello, HelloMit(version: "1.0")), Fehlercode.Protokoll),
            ("Hello nicht wohlgeformt", Rahmen.Bauen((ushort)Nachrichtentyp.Hello, 0, 0, "{"u8), Fehlercode.Protokoll),
            ("Hello mit leerem Body", Rahmen.Bauen((ushort)Nachrichtentyp.Hello, 0, 0, []), Fehlercode.Protokoll),
            ("Hello mit 33 Zeichen Name", Json(Nachrichtentyp.Hello, HelloMit(name: new string('x', 33))), Fehlercode.ZuGross),
            ("Hello als Anfrage", Json(Nachrichtentyp.Hello, HelloMit(), nr: 1), Fehlercode.Protokoll),
            ("Hello als Antwort", Json(Nachrichtentyp.Hello, HelloMit(), flags: Rahmen.FlagAntwort), Fehlercode.Protokoll),
        ];
        foreach (var (was, rahmen, code) in faelle)
        {
            var k = ServerIn(ServerZustand.WarteHello);
            var e = AnServer(k, rahmen);
            Pruefe.Gleich(code, e.Fehler, was);
            Pruefe.Wahr(e.Trennen, $"{was}: Trennung");
            Pruefe.Gleich(ServerZustand.Ende, k.Automat.Zustand, $"{was}: ENDE statt Warten bis zur Hello-Frist");
        }

        var gut = ServerIn(ServerZustand.WarteHello);
        var eg = AnServer(gut, Json(Nachrichtentyp.Hello, HelloMit()));
        Pruefe.Gleich(EingangsStatus.Annehmen, eg.Status, "gültiges Hello");
        Pruefe.Gleich(ServerZustand.WarteHello, gut.Automat.Zustand, "die Einstufung folgt über HelloVerarbeitet");
    }

    [Test]
    public static void Client_VorWelcome_NurWelcomeFehlerOderBye()
    {
        (string Was, byte[] Rahmen)[] faelle =
        [
            ("Welcome ohne registrierung", Rahmen.Bauen((ushort)Nachrichtentyp.Welcome, 0, 0, "{\"minor\":0,\"status\":\"UNBEKANNT\"}"u8)),
            ("Welcome mit unbekanntem Status", Rahmen.Bauen((ushort)Nachrichtentyp.Welcome, 0, 0, "{\"minor\":0,\"status\":\"NEU\"}"u8)),
            ("Fehler mit unbekanntem Code", Rahmen.Bauen((ushort)Nachrichtentyp.Fehler, 0, 0, "{\"code\":\"NEU\"}"u8)),
            ("optionaler Typ", Rahmen.Bauen(0x4FFF, 0, 0, "{}"u8)),
            ("Ping-Antwort", Json(Nachrichtentyp.Ping, new PingAntwort { Nr = 1, UdpEmpfangen = 0, UdpGesendet = 0, ServerZeit = 0 }, nr: 1, flags: Rahmen.FlagAntwort)),
            ("Welcome als Anfrage", Json(Nachrichtentyp.Welcome, Welcome.Unbekannt(0), nr: 0x80000001)),
        ];
        foreach (var (was, rahmen) in faelle)
        {
            var c = ClientIn(ClientZustand.Verbinden);
            var e = AnClient(c, rahmen);
            Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, was);
            Pruefe.Gleich(ClientZustand.Getrennt, c.Automat.Zustand, $"{was}: getrennt statt in VERBINDEN hängen");
        }

        var b = ClientIn(ClientZustand.Verbinden);
        AnClient(b, Json(Nachrichtentyp.Bye, new Bye { Grund = ByeGrund.Wartung, WiederInMs = 30_000 }));
        Pruefe.Gleich(ClientZustand.Getrennt, b.Automat.Zustand, "Bye WARTUNG vor Welcome");
        Pruefe.Gleich(30_000u, b.LetzterUebergang!.Value.NeuverbindenNachMs, "wiederInMs");

        // Nach Welcome gilt wieder 4.3: UNGUELTIG ohne Trennung.
        var a = ClientIn(ClientZustand.AktivUdp);
        var ea = AnClient(a, Rahmen.Bauen((ushort)Nachrichtentyp.VerlustHinweis, 0, 0, "{\"prozent\":101}"u8));
        Pruefe.Gleich(Fehlercode.Ungueltig, ea.Fehler, "in AKTIV");
        Pruefe.Gleich(ClientZustand.AktivUdp, a.Automat.Zustand, "keine Trennung");
    }

    // ---------------------------------------------------------------- P-46: nichts Geheimes in ToString

    [Test]
    public static void P46_KeinEinmalcodeUndKeineAdresseInToString()
    {
        var r = new RegistrierungAntwort { Code = "7K3M-Q9ZX-4TPA", GueltigBis = 1_790_000_300_000, Befehl = "/funk verknuepfen" };
        var s = r.ToString();
        Pruefe.Falsch(s.Contains("7K3M", StringComparison.Ordinal), "Code im Text: " + s);
        Pruefe.Wahr(s.Contains("1790000300000", StringComparison.Ordinal) && s.Contains("/funk verknuepfen", StringComparison.Ordinal), "übrige Felder: " + s);
        Pruefe.Wahr(Encoding.UTF8.GetString(SteuerJson.Serialisieren(r)).Contains("\"code\":\"7K3M-Q9ZX-4TPA\"", StringComparison.Ordinal), "auf dem Draht unverändert");

        var u = new UdpStatus { Zustand = UdpZustand.Gebunden, Adresse = "203.0.113.7:50123", GleicheIp = true };
        Pruefe.Falsch(u.ToString().Contains("203.0.113", StringComparison.Ordinal), "Adresse im Text: " + u);
        var c = ClientIn(ClientZustand.AktivUdp);
        var e = AnClient(c, Json(Nachrichtentyp.UdpStatus, u));
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "UdpStatus angenommen");
        Pruefe.Falsch(e.ToString().Contains("203.0.113", StringComparison.Ordinal), "auch nicht im Eingang: " + e);
    }

    // ---------------------------------------------------------------- Feldangabe bleibt kurz

    [Test]
    public static void Json_FeldangabeWirdGekuerzt_OhneSurrogatZuZerteilen()
    {
        for (var vorlauf = 80; vorlauf <= 110; vorlauf++)
        {
            var schluessel = new string('A', vorlauf) + string.Concat(Enumerable.Repeat("\U0001F600", 2000));
            var json = "{\"op\":\"SCHIFF_SETZEN\",\"version\":1,\"objekt\":{\"id\":1,\"name\":\"S\",\"reihenfolge\":1,\"netze\":{\""
                + schluessel + "\":\"x\"},\"plaetze\":[]}}";
            var v = (VerwaltungAnfrage)SteuerJson.Lesen(Encoding.UTF8.GetBytes(json), SteuerJson.Info<VerwaltungAnfrage>()).Body!;
            var e = v.ObjektLesen<Schiff>();
            Pruefe.Gleich(Fehlercode.Ungueltig, e.Fehler, $"Typfehler im Wert ({vorlauf})");
            Pruefe.Wahr(e.Feld is { Length: > 0 and <= SteuerJson.FeldMax }, $"Feld gekürzt: {e.Feld?.Length}");
            Pruefe.Wahr(Textregeln.Pruefen(e.Feld!), $"gültiges UTF-16 ({vorlauf})");
            // Die Feldangabe lässt sich als Fehler.text (str(200)) senden und wieder lesen.
            var f = SteuerJson.Lesen(SteuerJson.Serialisieren(new Fehler { Code = Fehlercode.Ungueltig, Text = e.Feld }), SteuerJson.Info<Fehler>());
            Pruefe.Gleich(null, f.Fehler, $"Fehler.text ({vorlauf})");
        }
    }

    // ---------------------------------------------------------------- Puffer mit Schlüsseln und Resten

    [Test]
    public static async Task Leser_BodyVerwerfen_NulltSofort()
    {
        var body = new byte[VoiceKeys.BodyLaenge];
        VoiceKeys.Schreiben(body, 0, VoiceKeysGrund.Start, KHoch, IvHoch, KRunter, IvRunter);
        var rahmen = Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, 0, 0x80000001, body);
        using var leser = new RahmenLeser(new MemoryStream(rahmen), new ZaehlPool());
        var r = await leser.LesenAsync(Rahmengrenzen.Aktiv);
        var sicht = r.Body;
        Pruefe.BytesGleich(body, sicht.Span, "gelesen");
        leser.BodyVerwerfen();
        Pruefe.Wahr(sicht.Span.IndexOfAnyExcept((byte)0) < 0, "sofort genullt, nicht erst beim nächsten Rahmen");
        leser.BodyVerwerfen();
        Pruefe.Gleich(LeseStatus.Ende, (await leser.LesenAsync(Rahmengrenzen.Aktiv)).Status, "danach weiter lesbar");
    }

    [Test]
    public static async Task Schreiber_AbbruchBeimSerialisieren_PufferGenullt()
    {
        var pool = new MerkPool();
        using var strom = new ZaehlStrom();
        using var s = new RahmenSchreiber(strom, pool: pool);
        var kaputt = new Fehler { Code = Fehlercode.Intern, Text = new string('G', 3000) + "\ud800x" };
        var geworfen = false;
        try
        {
            await s.SchreibenAsync(Nachrichtentyp.Fehler, 0, 0u, kaputt);
        }
        catch (System.Text.Json.JsonException)
        {
            geworfen = true;
        }
        Pruefe.Wahr(geworfen, "einsames Surrogat wird abgewiesen");
        Pruefe.Gleich(0, strom.Schreibaufrufe, "nichts geschrieben");
        Pruefe.Wahr(pool.Ausgegeben.TrueForAll(a => a.AsSpan().IndexOfAnyExcept((byte)0) < 0), "kein Rest im Poolpuffer");
        await s.SchreibenAsync(Nachrichtentyp.Bye, 0, 0u, new Bye { Grund = ByeGrund.Beendet });
        Pruefe.BytesGleich(Json(Nachrichtentyp.Bye, new Bye { Grund = ByeGrund.Beendet }), strom.Inhalt, "danach wieder ein sauberer Rahmen");
    }

    private sealed class MerkPool : ArrayPool<byte>
    {
        public List<byte[]> Ausgegeben { get; } = [];

        public override byte[] Rent(int minimumLength)
        {
            var feld = new byte[minimumLength];
            Ausgegeben.Add(feld);
            return feld;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            if (clearArray) Array.Clear(array);
        }
    }

    // ---------------------------------------------------------------- Nummernraum, Epochen, leere Eingaben

    [Test]
    public static void Anfragen_NummernraumBisZumEnde()
    {
        var feld = typeof(EigeneAnfragen).GetField("_letzte", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var c = new EigeneAnfragen(Seite.Client);
        feld.SetValue(c, 0x7FFF_FFFEu);
        Pruefe.Wahr(c.VersucheStellen(Nachrichtentyp.Ping, 0, 0, 0, out var nr), "letzte Nummer");
        Pruefe.Gleich(0x7FFF_FFFFu, nr, "0x7FFFFFFF");
        Pruefe.Falsch(c.VersucheStellen(Nachrichtentyp.Ping, 0, 0, 0, out _), "erschöpft: neu verbinden, nie Bit 31");

        var s = new EigeneAnfragen(Seite.Server);
        feld.SetValue(s, 0xFFFF_FFFEu);
        Pruefe.Wahr(s.VersucheStellen(Nachrichtentyp.VoiceKeys, 0, 10_000, 1, out var sn) && sn == 0xFFFF_FFFF, "Server: 0xFFFFFFFF");
        Pruefe.Falsch(s.VersucheStellen(Nachrichtentyp.VoiceKeys, 0, 10_000, 2, out _), "kein Überlauf auf 0");

        var f = new FremdeAnfragen(Seite.Client);
        Pruefe.Gleich(null, f.Annehmen(0x7FFF_FFFF), "Client 0x7FFFFFFF");
        Pruefe.Gleich(Fehlercode.Protokoll, new FremdeAnfragen(Seite.Client).Annehmen(0x8000_0000), "0x80000000 vom Client");
        Pruefe.Gleich(Fehlercode.Protokoll, new FremdeAnfragen(Seite.Client).Annehmen(0), "0 ist keine Anfrage");
        var fs = new FremdeAnfragen(Seite.Server);
        Pruefe.Gleich(null, fs.Annehmen(0xFFFF_FFFF), "Server 0xFFFFFFFF");
        Pruefe.Gleich(Fehlercode.Protokoll, fs.Annehmen(0xFFFF_FFFF), "danach keine weitere");
    }

    [Test]
    public static void VoiceKeys_EpochenwechselUeber255()
    {
        // Rotation von Epoche 255 auf 0 (5.8: Epochen mod 256).
        var k = ServerIn(ServerZustand.Aktiv);
        var nr = k.VoiceKeysStellen(0, 0);
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k, Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, nr, [255])).Fehler, "alte Epoche 255");

        var k2 = ServerIn(ServerZustand.Aktiv);
        var nr2 = k2.VoiceKeysStellen(0, 0);
        var e = AnServer(k2, Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, nr2, [0]));
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Epoche 0 nach 255");
        Pruefe.Gleich((byte)0, e.Bezug!.Value.Kontext, "Bezug");
        Pruefe.Gleich(ServerZustand.Aktiv, k2.Automat.Zustand, "bleibt AKTIV");

        var k3 = ServerIn(ServerZustand.Aktiv);
        var nr3 = k3.VoiceKeysStellen(1, 0);
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k3, Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, nr3, [])).Fehler, "leere Antwort");
    }

    [Test]
    public static void LeereUndFalscheBodies()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k, Rahmen.Bauen((ushort)Nachrichtentyp.Ping, 0, 1, [])).Fehler, "Ping mit leerem Body");
        var c = ClientIn(ClientZustand.Sync);
        Pruefe.Gleich(Fehlercode.Protokoll, AnClient(c, Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, 0, 0x80000001, [])).Fehler, "VoiceKeys leer");
        // Doppelter Schlüssel, einmal roh und einmal maskiert geschrieben.
        Pruefe.Gleich(Fehlercode.Protokoll, SteuerJson.Lesen(Encoding.UTF8.GetBytes("{\"nr\":1,\"\\u00e4\":1,\"\u00e4\":2}"), SteuerJson.Info<PingAnfrage>()).Fehler, "ä roh und maskiert");
    }

    [Test]
    public static void SpracheTunnel_Randfaelle()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k, Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 5, TvA)).Fehler, "mit Anfrage-Nr (4.5: Anfrage-Nr 0)");
        var k2 = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k2, Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, Rahmen.FlagAntwort, 0, TvA)).Fehler, "als Antwort");

        // 5.9 Schritte 1 und 2: SPRACHE_HOCH 41-569, Typen 0x01, 0x03, 0x04 genau 48, Datagramm höchstens 1200.
        Pruefe.Gleich(TunnelPruefung.Weiter, SpracheTunnel.Pruefen(Hoch(41), SitzungA, hoch: true), "41");
        Pruefe.Gleich(TunnelPruefung.Weiter, SpracheTunnel.Pruefen(Hoch(569), SitzungA, hoch: true), "569");
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(Hoch(570), SitzungA, hoch: true), "570");
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(Hoch(1201), SitzungA, hoch: true), "1201");
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen([], SitzungA, hoch: true), "leer");
        Pruefe.Gleich(TunnelPruefung.Weiter, SpracheTunnel.Pruefen(Hoch(48, 0x04), SitzungA, hoch: true), "KEEPALIVE 48");
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(Hoch(49, 0x04), SitzungA, hoch: true), "KEEPALIVE 49");
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(Hoch(48, 0x05), SitzungA, hoch: true), "KEEPALIVE_ECHO hoch");
        Pruefe.Gleich(TunnelPruefung.Weiter, SpracheTunnel.Pruefen(Hoch(48, 0x05), SitzungA, hoch: false), "KEEPALIVE_ECHO runter");
        Pruefe.Gleich(TunnelPruefung.Weiter, SpracheTunnel.Pruefen(Hoch(477, 0x11), SitzungA, hoch: false), "SPRACHE_RUNTER 477");
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(Hoch(478, 0x11), SitzungA, hoch: false), "SPRACHE_RUNTER 478");
        var fremd = Hoch(41);
        fremd[0] = 2;
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(fremd, SitzungA, hoch: true), "Version 2");

        var k3 = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(EingangsStatus.Uebergehen, AnServer(k3, Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, [])).Status, "leerer Tunnelrahmen still verworfen");
    }

    private static byte[] Hoch(int laenge, byte typ = 0x10)
    {
        var d = new byte[laenge];
        if (laenge < 16) return d;
        d[0] = 1;
        d[1] = typ;
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(4), SitzungA);
        return d;
    }

    // ---------------------------------------------------------------- Verdrängungsbuch ist serverweit

    [Test]
    public static void Verdraengungsbuch_Nebenlaeufig()
    {
        var buch = new Verdraengungsbuch();
        Parallel.For(0, 8000, i => buch.NeueSitzung((uint)(i % 1000), true, false, i % 3));
        for (uint konto = 0; konto < 1000; konto++)
        {
            Pruefe.Wahr(buch.IstGesperrt(konto, 10), $"Konto {konto}: nach 8 Verdrängungen gesperrt");
        }
    }

    // ---------------------------------------------------------------- Befund zu 4.10 (Spezifikation)

    /// <summary>
    /// 4.5 begrenzt <c>stumm</c> nicht. Je Konto sind 129 Einträge möglich (netzId 0 und jedes der 128 Netze).
    /// Mit Details für 100 Sitzungen überschreitet Praesenz voll dann die Rahmengrenze (Bericht). Geprüft wird hier,
    /// dass der Sender das nicht als kaputten Rahmen hinausschickt.
    /// </summary>
    [Test]
    public static async Task P02b_Befund_PraesenzMitVollerStummListe()
    {
        var name = string.Concat(Enumerable.Repeat("\U0001F600", Grenzen.NameMax));
        var stumm = Enumerable.Range(0, Grenzen.NetzeMax + 1)
            .Select(_ => new Stummschaltung { NetzId = 65534, Bis = JsonVorpruefung.MaxGanzzahl, Durch = int.MaxValue }).ToArray();
        var felder = Enumerable.Range(0, Grenzen.FelderMax)
            .Select(f => new Mitgliedschaft { Feld = (byte)f, NetzId = (ushort)(65534 - f), Hoeren = false, Senden = false, Fest = false }).ToArray();
        var mitDetails = Enumerable.Range(0, Grenzen.SitzungenMax).Select(_ => new PraesenzEintrag
        {
            KontoId = int.MaxValue, Name = name, Benutzername = name, Online = false, SchiffId = 65535, RolleId = 65535, Platz = PlatzStatus.Reserviert,
            Felder = felder, SendetIn = [.. Enumerable.Repeat((ushort)65534, Grenzen.FelderMax + 1)], Stumm = stumm,
            Verbindung = Verbindungsweg.Tunnel, Guete = Guete.Schlecht,
        });
        var ohneSitzung = Enumerable.Range(0, Grenzen.KontenMax - Grenzen.SitzungenMax).Select(_ => new PraesenzEintrag
        {
            KontoId = int.MaxValue, Name = name, Benutzername = name, Online = false, SchiffId = 65535, RolleId = 65535, Platz = PlatzStatus.Reserviert,
        });
        var praesenz = new Praesenz { Voll = true, ZustandsVersion = JsonVorpruefung.MaxGanzzahl, Eintraege = [.. mitDetails, .. ohneSitzung], Entfernt = [] };
        var bytes = SteuerJson.Serialisieren(praesenz);
        Console.WriteLine($"BEFUND 4.10: Praesenz voll mit {stumm.Length} Stumm-Einträgen je Sitzung: {bytes.Length} Byte Body, "
            + $"Rahmengrenze {Rahmengrenzen.Aktiv - Rahmen.LaengeOhneBody} Byte (Bericht)");
        if (bytes.Length + Rahmen.LaengeOhneBody <= Rahmengrenzen.Aktiv) return;

        using var strom = new ZaehlStrom();
        using var s = new RahmenSchreiber(strom);
        var geworfen = false;
        try
        {
            await s.SchreibenAsync(Nachrichtentyp.Praesenz, 0, 0u, praesenz);
        }
        catch (InvalidOperationException)
        {
            geworfen = true;
        }
        Pruefe.Wahr(geworfen && strom.Schreibaufrufe == 0, "der Schreiber verweigert den übergroßen Rahmen");
    }
}
