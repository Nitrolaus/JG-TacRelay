// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.IO.Pipelines;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.SteuerHilfe;

namespace Funk.Tests;

/// <summary>Prüfwerte 8.4 (Steuerrahmen) und Prüfplan P-01 (Rahmen, Längengrenzen, Typbereiche, ein Write je Rahmen).</summary>
internal static class SteuerRahmenTests
{
    private const string PingAnfrageHex = "0000001001000004000000077b226e72223a317d";
    private const string PingAntwortHex = "0000005101010004000000077b226e72223a312c22756470456d7066616e67656e223a3235302c22756470476573656e646574223a313235302c227365727665725a656974223a313739303030303030303030307d";
    private const string FehlerAntwortHex = "0000002b01010006000000087b22636f6465223a224b45494e5f5245434854222c2262657a7567547970223a35317d";
    private const string VoiceKeysAnfrageHex = "0000006201008002800000010000000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1fa0a1a2a3a4a5a6a7a8a9aaab202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3fb0b1b2b3b4b5b6b7b8b9babb";
    private const string VoiceKeysAntwort0Hex = "00000009010180028000000100";
    private const string VoiceKeysAntwort1Hex = "00000009010180028000000201";

    private static readonly PingAntwort PingAntwortWert = new() { Nr = 1, UdpEmpfangen = 250, UdpGesendet = 1250, ServerZeit = 1_790_000_000_000 };

    // ---------------------------------------------------------------- 8.4 Positivfälle, bytegenau

    [Test]
    public static void V84_PingAnfrage_Bytegenau()
    {
        var rahmen = Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 7);
        Pruefe.BytesGleich(Convert.FromHexString(PingAnfrageHex), rahmen, "Ping-Anfrage");
        Pruefe.Gleich(20, rahmen.Length, "Länge");
    }

    [Test]
    public static void V84_PingAnfrage_ServerLiest()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        for (uint i = 1; i < 7; i++) k.Fremde.Annehmen(i);
        var e = AnServer(k, Convert.FromHexString(PingAnfrageHex));
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Status");
        Pruefe.Gleich(7u, e.Kopf.AnfrageNr, "Anfrage-Nr");
        Pruefe.Gleich(1u, ((PingAnfrage)e.Body!).Nr, "nr");
    }

    [Test]
    public static void V84_PingAntwort_Bytegenau()
    {
        var rahmen = Json(Nachrichtentyp.Ping, PingAntwortWert, nr: 7, flags: Rahmen.FlagAntwort);
        Pruefe.BytesGleich(Convert.FromHexString(PingAntwortHex), rahmen, "Ping-Antwort");
        Pruefe.Gleich(85, rahmen.Length, "Länge");
    }

    [Test]
    public static void V84_PingAntwort_ClientLiest()
    {
        var k = ClientIn(ClientZustand.AktivUdp);
        for (var i = 0; i < 7; i++) Pruefe.Wahr(k.VersucheAnfrageStellen(Nachrichtentyp.Ping, 0, out _), "Anfrage stellen");
        var e = AnClient(k, Convert.FromHexString(PingAntwortHex));
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Status");
        Pruefe.Gleich(PingAntwortWert, (PingAntwort)e.Body!, "Body");
        Pruefe.Gleich(7u, e.Bezug!.Value.Nr, "Bezug");
    }

    [Test]
    public static void V84_FehlerAntwortAufVerschieben_Bytegenau()
    {
        var fehler = new Fehler { Code = Fehlercode.KeinRecht, BezugTyp = (ushort)Nachrichtentyp.Verschieben };
        var rahmen = Json(Nachrichtentyp.Fehler, fehler, nr: 8, flags: Rahmen.FlagAntwort);
        Pruefe.BytesGleich(Convert.FromHexString(FehlerAntwortHex), rahmen, "Fehler-Antwort");
        Pruefe.Gleich(47, rahmen.Length, "Länge");
        Pruefe.Gleich(51, (int)Nachrichtentyp.Verschieben, "Verschieben = 51");
    }

    [Test]
    public static void V84_FehlerAntwort_ClientOrdnetVerschiebenZu()
    {
        var k = ClientIn(ClientZustand.AktivUdp);
        for (var i = 0; i < 7; i++) k.VersucheAnfrageStellen(Nachrichtentyp.Ping, 0, out _);
        k.VersucheAnfrageStellen(Nachrichtentyp.Verschieben, 0, out var nr);
        Pruefe.Gleich(8u, nr, "Anfrage-Nr");
        var e = AnClient(k, Convert.FromHexString(FehlerAntwortHex));
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Status");
        Pruefe.Gleich(Nachrichtentyp.Verschieben, e.Bezug!.Value.Typ, "Bezug");
        Pruefe.Gleich(Fehlercode.KeinRecht, ((Fehler)e.Body!).Code, "Code");
        Pruefe.Gleich(ClientZustand.AktivUdp, k.Automat.Zustand, "KEIN_RECHT trennt nicht");
    }

    [Test]
    public static void V84_VoiceKeysAnfrage_Bytegenau()
    {
        var k = ServerIn(ServerZustand.WarteHello);
        k.Automat.HelloVerarbeitet(Einstufung.Freigegeben, false, 0);
        var nr = k.VoiceKeysStellen(0, 0);
        Pruefe.Gleich(0x80000001u, nr, "erste Server-Anfrage");
        var body = new byte[VoiceKeys.BodyLaenge];
        VoiceKeys.Schreiben(body, 0, VoiceKeysGrund.Start, KHoch, IvHoch, KRunter, IvRunter);
        var rahmen = Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, 0, nr, body);
        Pruefe.BytesGleich(Convert.FromHexString(VoiceKeysAnfrageHex), rahmen, "VoiceKeys-Anfrage");
        Pruefe.Gleich(102, rahmen.Length, "Länge");
    }

    [Test]
    public static void V84_VoiceKeysAnfrage_ClientLiest()
    {
        var k = ClientIn(ClientZustand.Sync);
        var rahmen = Convert.FromHexString(VoiceKeysAnfrageHex);
        var e = AnClient(k, rahmen);
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Status");
        var v = VoiceKeys.Lesen(rahmen.AsSpan(Rahmen.KopfLaenge));
        Pruefe.Gleich((byte)0, v.Epoche, "Epoche");
        Pruefe.Gleich(VoiceKeysGrund.Start, v.Grund, "Grund");
        Pruefe.BytesGleich(KHoch, v.KHoch, "K_hoch");
        Pruefe.BytesGleich(IvHoch, v.IvHoch, "IV_hoch");
        Pruefe.BytesGleich(KRunter, v.KRunter, "K_runter");
        Pruefe.BytesGleich(IvRunter, v.IvRunter, "IV_runter");
    }

    [Test]
    public static void V84_VoiceKeysAntwortEpoche0_Bytegenau_UndServerNachAktiv()
    {
        var rahmen = Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, 0x80000001, [0]);
        Pruefe.BytesGleich(Convert.FromHexString(VoiceKeysAntwort0Hex), rahmen, "Antwort Epoche 0");
        Pruefe.Gleich(13, rahmen.Length, "Länge");

        var k = ServerIn(ServerZustand.AktivSync);
        var e = AnServer(k, rahmen);
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Status");
        Pruefe.Gleich(ServerZustand.Aktiv, k.Automat.Zustand, "AKTIV_SYNC -> AKTIV");
    }

    [Test]
    public static void V84_VoiceKeysAntwortRotation_Bytegenau_UndServerNimmtAn()
    {
        var rahmen = Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, 0x80000002, [1]);
        Pruefe.BytesGleich(Convert.FromHexString(VoiceKeysAntwort1Hex), rahmen, "Antwort Epoche 1");

        var k = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(0x80000002u, k.VoiceKeysStellen(1, 0), "zweite Server-Anfrage");
        var e = AnServer(k, rahmen);
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Status");
        Pruefe.Gleich((byte)1, e.Bezug!.Value.Kontext, "Epoche im Bezug");
    }

    [Test]
    public static async Task V84_AlleVektoren_UeberSchreiberUndLeser()
    {
        using var strom = new ZaehlStrom();
        using (var s = new RahmenSchreiber(strom))
        {
            await s.SchreibenAsync(Nachrichtentyp.Ping, 0, 7u, new PingAnfrage { Nr = 1 });
            await s.SchreibenAsync(Nachrichtentyp.Ping, Rahmen.FlagAntwort, 7u, PingAntwortWert);
            await s.SchreibenAsync(Nachrichtentyp.Fehler, Rahmen.FlagAntwort, 8u, new Fehler { Code = Fehlercode.KeinRecht, BezugTyp = 51 });
            await s.SchreibenAsync((ushort)Nachrichtentyp.VoiceKeys, 0, 0x80000001u, Convert.FromHexString(VoiceKeysAnfrageHex).AsMemory(Rahmen.KopfLaenge));
            await s.SchreibenAsync((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, 0x80000001u, new byte[] { 0 });
            await s.SchreibenAsync((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, 0x80000002u, new byte[] { 1 });
        }
        var erwartet = Convert.FromHexString(PingAnfrageHex + PingAntwortHex + FehlerAntwortHex + VoiceKeysAnfrageHex + VoiceKeysAntwort0Hex + VoiceKeysAntwort1Hex);
        Pruefe.BytesGleich(erwartet, strom.Inhalt, "Strom");
        Pruefe.Gleich(6, strom.Schreibaufrufe, "ein Write je Rahmen");

        using var leser = new RahmenLeser(new MemoryStream(erwartet));
        var laengen = new List<int>();
        while (true)
        {
            var r = await leser.LesenAsync(Rahmengrenzen.Aktiv);
            if (r.Status == LeseStatus.Ende) break;
            Pruefe.Gleich(LeseStatus.Rahmen, r.Status, "Lesestatus");
            laengen.Add(r.Kopf.GesamtLaenge);
        }
        Pruefe.Gleich("20,85,47,102,13,13", string.Join(',', laengen), "Rahmenlängen");
    }

    // ---------------------------------------------------------------- 8.4 Negativfälle

    [Test]
    public static async Task V84_Laenge7_Protokoll()
    {
        var rahmen = Convert.FromHexString("0000000701000004000000");
        var r = await new RahmenLeser(new MemoryStream(rahmen)).LesenAsync(Rahmengrenzen.VorHello);
        Pruefe.Gleich(LeseStatus.Fehler, r.Status, "Status");
        Pruefe.Gleich(Fehlercode.Protokoll, r.Fehler, "Code");
        Pruefe.Wahr(Fehlercodes.Trennt(r.Fehler), "trennt");
        var seq = new ReadOnlySequence<byte>(rahmen);
        Pruefe.Gleich(RahmenStatus.Fehler, Rahmen.VersucheLesen(ref seq, Rahmengrenzen.VorHello, out _, out _, out var f), "Parser");
        Pruefe.Gleich(Fehlercode.Protokoll, f, "Parser-Code");
    }

    [Test]
    public static async Task V84_VorHello4097_ZuGross()
    {
        var k = ServerIn(ServerZustand.WarteHello);
        Pruefe.Gleich(4096, k.Rahmengrenze, "Grenze vor Hello");
        await LaengeGrenzePruefen(k.Rahmengrenze, 4096);
    }

    [Test]
    public static async Task V84_Warteraum1033_ZuGross()
    {
        var k = ServerIn(ServerZustand.Warteraum);
        Pruefe.Gleich(1032, k.Rahmengrenze, "Grenze in WARTERAUM");
        Pruefe.Gleich(1032, ServerIn(ServerZustand.Registrierung).Rahmengrenze, "Grenze in REGISTRIERUNG");
        await LaengeGrenzePruefen(k.Rahmengrenze, 1032);
    }

    [Test]
    public static async Task V84_Aktiv1048577_ZuGross()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(1_048_576, k.Rahmengrenze, "Grenze in AKTIV");
        Pruefe.Gleich(1_048_576, ServerIn(ServerZustand.AktivSync).Rahmengrenze, "Grenze in AKTIV_SYNC");
        await LaengeGrenzePruefen(k.Rahmengrenze, 1_048_576);
    }

    /// <summary>Länge = Grenze wird angenommen, Grenze + 1 ergibt ZU_GROSS, und zwar bevor ein Body-Puffer angefordert wird.</summary>
    private static async Task LaengeGrenzePruefen(int grenze, uint erwarteteGrenze)
    {
        Pruefe.Gleich((int)erwarteteGrenze, grenze, "Grenze");
        var body = new byte[grenze - 8];
        var passt = Rahmen.Bauen((ushort)Nachrichtentyp.Ping, 0, 1, body);
        var pool = new ZaehlPool();
        var r1 = await new RahmenLeser(new MemoryStream(passt), pool).LesenAsync(grenze);
        Pruefe.Gleich(LeseStatus.Rahmen, r1.Status, "Länge = Grenze");

        var zuGross = new byte[Rahmen.KopfLaenge];
        Rahmen.KopfSchreiben(zuGross, (ushort)Nachrichtentyp.Ping, 0, 1, grenze - 8 + 1);
        var pool2 = new ZaehlPool();
        var r2 = await new RahmenLeser(new MemoryStream(zuGross), pool2).LesenAsync(grenze);
        Pruefe.Gleich(LeseStatus.Fehler, r2.Status, "Grenze + 1");
        Pruefe.Gleich(Fehlercode.ZuGross, r2.Fehler, "Code");
        Pruefe.Gleich(0, pool2.Anforderungen.Count, "kein Puffer vor der Längenprüfung");

        // PipeReader-Weg: schon die ersten 4 Byte genügen für das Urteil.
        var seq = new ReadOnlySequence<byte>(zuGross, 0, 4);
        Pruefe.Gleich(RahmenStatus.Fehler, Rahmen.VersucheLesen(ref seq, grenze, out _, out _, out var f), "Parser nach 4 Byte");
        Pruefe.Gleich(Fehlercode.ZuGross, f, "Parser-Code");
    }

    [Test]
    public static async Task V84_Rahmenversion2_Protokoll()
    {
        var rahmen = Convert.FromHexString(PingAnfrageHex);
        rahmen[4] = 2;
        var r = await new RahmenLeser(new MemoryStream(rahmen)).LesenAsync(Rahmengrenzen.Aktiv);
        Pruefe.Gleich(LeseStatus.Fehler, r.Status, "Status");
        Pruefe.Gleich(Fehlercode.Protokoll, r.Fehler, "Code");
        var seq = new ReadOnlySequence<byte>(rahmen, 0, 5);
        Pruefe.Gleich(RahmenStatus.Fehler, Rahmen.VersucheLesen(ref seq, Rahmengrenzen.Aktiv, out _, out _, out _), "Parser nach 5 Byte");
    }

    [Test]
    public static async Task V84_Flags02_Protokoll()
    {
        var rahmen = Convert.FromHexString(PingAnfrageHex);
        rahmen[5] = 0x02;
        var r = await new RahmenLeser(new MemoryStream(rahmen)).LesenAsync(Rahmengrenzen.Aktiv);
        Pruefe.Gleich(LeseStatus.Fehler, r.Status, "Status");
        Pruefe.Gleich(Fehlercode.Protokoll, r.Fehler, "Code");
        for (var bit = 1; bit < 8; bit++)
        {
            Pruefe.Gleich(Fehlercode.Protokoll, Rahmen.VersionUndFlagsPruefen(1, (byte)(1 << bit)), $"Flag-Bit {bit}");
        }
        Pruefe.Gleich(null, Rahmen.VersionUndFlagsPruefen(1, Rahmen.FlagAntwort), "ANTWORT erlaubt");
    }

    [Test]
    public static void V84_UnbekannterTyp3FFF_ProtokollUndTrennung()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        var e = AnServer(k, Rahmen.Bauen(0x3FFF, 0, 0, "{}"u8));
        Pruefe.Gleich(EingangsStatus.Fehler, e.Status, "Status");
        Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, "Code");
        Pruefe.Wahr(e.Trennen, "Trennung");
        Pruefe.Gleich(ServerZustand.Ende, k.Automat.Zustand, "ENDE");
    }

    [Test]
    public static void V84_UnbekannterTyp4FFF_Uebergangen()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        var e = AnServer(k, Rahmen.Bauen(0x4FFF, 0, 0, "{}"u8));
        Pruefe.Gleich(EingangsStatus.Uebergehen, e.Status, "Status");
        Pruefe.Gleich(ServerZustand.Aktiv, k.Automat.Zustand, "bleibt AKTIV");
        // Mit beliebigem Body: der optionale Typ wird ohne Blick in den Body übergangen.
        var k1 = ServerIn(ServerZustand.Warteraum);
        Pruefe.Gleich(EingangsStatus.Uebergehen, AnServer(k1, Rahmen.Bauen(0x4FFF, 0, 0, [0xFF, 0x00])).Status, "WARTERAUM, Body kein JSON");
        // Vor Hello dagegen nicht: Hello ist der erste Rahmen nach TLS (4.5), "etwas anderes -> ENDE" (4.6).
        var k2 = ServerIn(ServerZustand.WarteHello);
        var e2 = AnServer(k2, Rahmen.Bauen(0x4FFF, 0, 0, [0xFF, 0x00]));
        Pruefe.Gleich(Fehlercode.Protokoll, e2.Fehler, "vor Hello");
        Pruefe.Gleich(ServerZustand.Ende, k2.Automat.Zustand, "vor Hello: ENDE");
    }

    [Test]
    public static void V84_AntwortMitUnbekannterAnfrageNr_ProtokollUndTrennung()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        var e = AnServer(k, Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, 0x80000009, [0]));
        Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, "Code");
        Pruefe.Wahr(e.Trennen, "Trennung");

        var c = ClientIn(ClientZustand.AktivUdp);
        var e2 = AnClient(c, Json(Nachrichtentyp.Ping, PingAntwortWert, nr: 3, flags: Rahmen.FlagAntwort));
        Pruefe.Gleich(Fehlercode.Protokoll, e2.Fehler, "Client: Code");
        Pruefe.Gleich(ClientZustand.Getrennt, c.Automat.Zustand, "Client getrennt");
    }

    [Test]
    public static void V84_DoppelterSchluessel_Protokoll()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        var e = AnServer(k, Rahmen.Bauen((ushort)Nachrichtentyp.Ping, 0, 1, "{\"nr\":1,\"nr\":2}"u8));
        Pruefe.Gleich(EingangsStatus.Fehler, e.Status, "Status");
        Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, "Code");
        Pruefe.Wahr(e.Trennen, "Trennung");
    }

    [Test]
    public static void V84_VoiceKeysAnfrage89Byte_Protokoll()
    {
        var k = ClientIn(ClientZustand.Sync);
        var rahmen = Convert.FromHexString(VoiceKeysAnfrageHex);
        var kurz = Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, 0, 0x80000001, rahmen.AsSpan(Rahmen.KopfLaenge, 89));
        var e = AnClient(k, kurz);
        Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, "Code");
        Pruefe.Wahr(e.Trennen, "Trennung");
        Pruefe.Gleich(Fehlercode.Protokoll, VoiceKeys.AnfragePruefen(new byte[91]), "91 Byte");
        var grund2 = rahmen.AsSpan(Rahmen.KopfLaenge).ToArray();
        grund2[1] = 2;
        Pruefe.Gleich(Fehlercode.Protokoll, VoiceKeys.AnfragePruefen(grund2), "Grund 2");
    }

    [Test]
    public static void V84_VoiceKeysAntwortFalscheEpoche_Protokoll()
    {
        var k = ServerIn(ServerZustand.AktivSync);
        var e = AnServer(k, Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, 0x80000001, [1]));
        Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, "Code");
        Pruefe.Wahr(e.Trennen, "Trennung");
        Pruefe.Gleich(ServerZustand.Ende, k.Automat.Zustand, "ENDE");

        var k2 = ServerIn(ServerZustand.AktivSync);
        var e2 = AnServer(k2, Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, 0x80000001, [0, 0]));
        Pruefe.Gleich(Fehlercode.Protokoll, e2.Fehler, "zwei Byte");
    }

    // ---------------------------------------------------------------- P-01 weitere Fälle

    [Test]
    public static void P01_Typbereiche()
    {
        Pruefe.Gleich(Typbereich.Ungueltig, Typbereiche.Von(0x0000), "0x0000");
        Pruefe.Gleich(Typbereich.JsonPflicht, Typbereiche.Von(0x0001), "0x0001");
        Pruefe.Gleich(Typbereich.JsonPflicht, Typbereiche.Von(0x3FFF), "0x3FFF");
        Pruefe.Gleich(Typbereich.JsonOptional, Typbereiche.Von(0x4000), "0x4000");
        Pruefe.Gleich(Typbereich.JsonOptional, Typbereiche.Von(0x7FFF), "0x7FFF");
        Pruefe.Gleich(Typbereich.BinaerPflicht, Typbereiche.Von(0x8000), "0x8000");
        Pruefe.Gleich(Typbereich.BinaerPflicht, Typbereiche.Von(0xBFFF), "0xBFFF");
        Pruefe.Gleich(Typbereich.BinaerOptional, Typbereiche.Von(0xC000), "0xC000");
        Pruefe.Gleich(Typbereich.BinaerOptional, Typbereiche.Von(0xFFFF), "0xFFFF");

        foreach (var (typ, erwartet) in new (ushort, EingangsStatus)[]
                 { (0x0000, EingangsStatus.Fehler), (0x0070, EingangsStatus.Fehler), (0x8FFF, EingangsStatus.Fehler), (0xC123, EingangsStatus.Uebergehen), (0x7FFF, EingangsStatus.Uebergehen) })
        {
            var k = ServerIn(ServerZustand.Aktiv);
            var e = AnServer(k, Rahmen.Bauen(typ, 0, 0, "{}"u8));
            Pruefe.Gleich(erwartet, e.Status, $"Typ 0x{typ:X4}");
        }
    }

    [Test]
    public static void P01_BekannterOptionalerTypFalscheRichtung_Uebergangen()
    {
        // ServerLast (0x4001) sendet nur der Server; vom Client wird er als optionaler Typ übergangen.
        var k = ServerIn(ServerZustand.Aktiv);
        var e = AnServer(k, Json(Nachrichtentyp.ServerLast, new ServerLast { Stufe = 0, Verworfen = 0, AusgangMbit = 1 }));
        Pruefe.Gleich(EingangsStatus.Uebergehen, e.Status, "ServerLast vom Client");
        // NetzListe (Pflicht, nur S->C) vom Client ist ein Protokollfehler.
        var e2 = AnServer(k, Rahmen.Bauen((ushort)Nachrichtentyp.NetzListe, 0, 0, "{}"u8));
        Pruefe.Gleich(Fehlercode.Protokoll, e2.Fehler, "NetzListe vom Client");
    }

    [Test]
    public static void P01_KopfSchreibenUndLesen_RundUm()
    {
        var kopf = new byte[Rahmen.KopfLaenge];
        Rahmen.KopfSchreiben(kopf, 0xBEEF, Rahmen.FlagAntwort, 0x80000005, 1000);
        Pruefe.Gleich(null, Rahmen.KopfLesen(kopf, Rahmengrenzen.Aktiv, out var k), "gültig");
        Pruefe.Gleich(new RahmenKopf(1008, 1, 1, 0xBEEF, 0x80000005), k, "Kopf");
        Pruefe.Gleich(1000, k.BodyLaenge, "Body");
        Pruefe.Gleich(1012, k.GesamtLaenge, "gesamt: 12 Byte Kopf + 1000 Byte Body");
        Pruefe.Gleich(Nachrichtenart.Antwort, k.Art, "Art");
        Pruefe.Gleich(Nachrichtenart.Push, (k with { Flags = 0, AnfrageNr = 0 }).Art, "Push");
        Pruefe.Gleich(Nachrichtenart.Anfrage, (k with { Flags = 0 }).Art, "Anfrage");
    }

    [Test]
    public static async Task P01_EinWriteJeRahmen_AuchGross()
    {
        using var strom = new ZaehlStrom();
        using var s = new RahmenSchreiber(strom);
        var gross = new byte[900_000];
        new Random(7).NextBytes(gross);
        await s.SchreibenAsync(0xC001, 0, 0, gross);
        await s.SchreibenAsync(Nachrichtentyp.PegelVoreinstellungen, 0, 0u,
            new PegelVoreinstellungen { Eintraege = [.. Enumerable.Range(0, 2000).Select(i => new PegelEintrag { KontoId = (uint)i, NetzId = 7, Db = -3.5 })] });
        await s.SchreibenAsync(Nachrichtentyp.Bye, 0, 0u, new Bye { Grund = ByeGrund.Beendet });
        Pruefe.Gleich(3, strom.Schreibaufrufe, "Writes");

        using var leser = new RahmenLeser(new MemoryStream(strom.Inhalt));
        var r = await leser.LesenAsync(Rahmengrenzen.Aktiv);
        Pruefe.BytesGleich(gross, r.Body.Span, "großer Body");
    }

    [Test]
    public static async Task P01_SchreiberWeigertZuGrossenRahmen()
    {
        using var strom = new ZaehlStrom();
        using var s = new RahmenSchreiber(strom, maxLaenge: 1032);
        await s.SchreibenAsync(0xC001, 0, 0, new byte[1024]);
        var fehler = false;
        try
        {
            await s.SchreibenAsync(0xC001, 0, 0, new byte[1025]);
        }
        catch (InvalidOperationException)
        {
            fehler = true;
        }
        Pruefe.Wahr(fehler, "1025 Byte Body abgelehnt");
        Pruefe.Gleich(1, strom.Schreibaufrufe, "nichts geschrieben");
    }

    [Test]
    public static async Task P01_Leser_EndeUndAbbruch()
    {
        var leer = await new RahmenLeser(new MemoryStream([])).LesenAsync(4096);
        Pruefe.Gleich(LeseStatus.Ende, leer.Status, "leerer Strom");
        var halb = Convert.FromHexString(PingAnfrageHex)[..15];
        var r = await new RahmenLeser(new MemoryStream(halb)).LesenAsync(4096);
        Pruefe.Gleich(LeseStatus.Abgebrochen, r.Status, "mitten im Body");
        var r2 = await new RahmenLeser(new MemoryStream(halb[..2])).LesenAsync(4096);
        Pruefe.Gleich(LeseStatus.Abgebrochen, r2.Status, "mitten im Längenfeld");
    }

    [Test]
    public static void P01_Parser_Stueckweise()
    {
        var alles = Convert.FromHexString(PingAnfrageHex + PingAntwortHex + FehlerAntwortHex);
        // Jede Teilung in bis zu drei Segmente ergibt dieselben drei Rahmen.
        for (var a = 1; a < alles.Length; a += 3)
        {
            for (var b = a; b < alles.Length; b += 7)
            {
                var seq = Segmente(alles, a, b);
                var laengen = new List<int>();
                while (Rahmen.VersucheLesen(ref seq, Rahmengrenzen.Aktiv, out var kopf, out var body, out _) == RahmenStatus.Fertig)
                {
                    Pruefe.Gleich((long)kopf.BodyLaenge, body.Length, "Body-Länge");
                    laengen.Add(kopf.GesamtLaenge);
                }
                Pruefe.Gleich("20,85,47", string.Join(',', laengen), $"Teilung {a}/{b}");
                Pruefe.Gleich(0L, seq.Length, "Rest");
            }
        }
        var teil = new ReadOnlySequence<byte>(alles, 0, 19);
        Pruefe.Gleich(RahmenStatus.Unvollstaendig, Rahmen.VersucheLesen(ref teil, Rahmengrenzen.Aktiv, out _, out _, out _), "unvollständig");
        Pruefe.Gleich(19L, teil.Length, "nichts verbraucht");
    }

    [Test]
    public static async Task P01_PipeLeser_TeilstueckeUndGrenze()
    {
        var pipe = new Pipe();
        var leser = new PipeRahmenLeser(pipe.Reader);
        var alles = Convert.FromHexString(PingAnfrageHex + PingAntwortHex);
        var schreiben = Task.Run(async () =>
        {
            foreach (var b in alles)
            {
                await pipe.Writer.WriteAsync(new[] { b });
            }
            await pipe.Writer.CompleteAsync();
        });
        var r1 = await leser.LesenAsync(Rahmengrenzen.Aktiv);
        Pruefe.Gleich(LeseStatus.Rahmen, r1.Status, "Rahmen 1");
        Pruefe.Gleich(20, r1.Kopf.GesamtLaenge, "Länge 1");
        var r2 = await leser.LesenAsync(Rahmengrenzen.Aktiv);
        Pruefe.Gleich(85, r2.Kopf.GesamtLaenge, "Länge 2");
        Pruefe.Gleich(LeseStatus.Ende, (await leser.LesenAsync(Rahmengrenzen.Aktiv)).Status, "Ende");
        await schreiben;

        // Zu großer Kopf: Urteil ohne auf den Body zu warten.
        var pipe2 = new Pipe();
        var kopf = new byte[Rahmen.KopfLaenge];
        Rahmen.KopfSchreiben(kopf, 1, 0, 0, 5000);
        await pipe2.Writer.WriteAsync(kopf);
        var r3 = await new PipeRahmenLeser(pipe2.Reader).LesenAsync(Rahmengrenzen.VorHello);
        Pruefe.Gleich(LeseStatus.Fehler, r3.Status, "zu groß");
        Pruefe.Gleich(Fehlercode.ZuGross, r3.Fehler, "Code");
    }

    [Test]
    public static async Task P01_LeserNulltVorigenPuffer()
    {
        var pool = new ZaehlPool();
        var vk = Convert.FromHexString(VoiceKeysAnfrageHex);
        using var leser = new RahmenLeser(new MemoryStream([.. vk, .. vk]), pool);
        var r1 = await leser.LesenAsync(Rahmengrenzen.Aktiv);
        var sicht = r1.Body;
        Pruefe.BytesGleich(vk.AsSpan(Rahmen.KopfLaenge), sicht.Span, "Schlüssel gelesen");
        await leser.LesenAsync(Rahmengrenzen.Aktiv);
        Pruefe.Wahr(sicht.Span.IndexOfAnyExcept((byte)0) < 0, "voriger Puffer genullt");
    }

    // ---------------------------------------------------------------- 4.2 Senden: zwei Warteschlangen

    [Test]
    public static void Senden_SteuerrahmenGehenVor()
    {
        var q = new Sendewarteschlange<string>();
        q.TunnelEinreihen(1001, "T1", 0);
        q.SteuerEinreihen("S1");
        q.TunnelEinreihen(1002, "T2", 0);
        q.SteuerEinreihen("S2");
        var folge = new List<string>();
        while (q.VersucheEntnehmen(10, out var r)) folge.Add(r);
        Pruefe.Gleich("S1,S2,T1,T2", string.Join(',', folge), "Reihenfolge");
    }

    [Test]
    public static void Senden_TunnelAelterAls60ms_Verworfen()
    {
        var frei = new List<string>();
        var q = new Sendewarteschlange<string>(frei.Add);
        q.TunnelEinreihen(1001, "alt", 0);
        q.TunnelEinreihen(1001, "grenze", 40);
        q.TunnelEinreihen(1002, "frisch", 90);
        Pruefe.Wahr(q.VersucheEntnehmen(100, out var r), "etwas steht an");
        Pruefe.Gleich("grenze", r, "60 ms alt: noch gültig");
        Pruefe.Gleich("alt", string.Join(',', frei), "100 ms alt: verworfen und freigegeben");
        Pruefe.Wahr(q.VersucheEntnehmen(100, out var r2) && r2 == "frisch", "frisch");
        Pruefe.Falsch(q.VersucheEntnehmen(100, out _), "leer");
        Pruefe.Gleich(1L, q.Verworfen, "verworfen");
    }

    [Test]
    public static void Senden_HoechstensDreiJeAbsender_AeltesterDesselbenFaellt()
    {
        var frei = new List<string>();
        var q = new Sendewarteschlange<string>(frei.Add);
        q.TunnelEinreihen(1001, "A1", 0);
        q.TunnelEinreihen(1002, "B1", 0);
        q.TunnelEinreihen(1001, "A2", 1);
        q.TunnelEinreihen(1001, "A3", 2);
        q.TunnelEinreihen(1002, "B2", 2);
        q.TunnelEinreihen(1001, "A4", 3);
        Pruefe.Gleich("A1", string.Join(',', frei), "vierter von A verdrängt A1, nie B");
        q.TunnelEinreihen(1001, "A5", 4);
        Pruefe.Gleich("A1,A2", string.Join(',', frei), "fünfter von A verdrängt A2");
        Pruefe.Gleich(5, q.Tunnelrahmen, "wartend");
        var folge = new List<string>();
        while (q.VersucheEntnehmen(5, out var r)) folge.Add(r);
        Pruefe.Gleich("B1,A3,B2,A4,A5", string.Join(',', folge), "Reihenfolge bleibt");
        for (var i = 0; i < 500; i++) q.TunnelEinreihen((uint)(i % 7), "x", 10);
        Pruefe.Gleich(21, q.Tunnelrahmen, "7 Absender zu je 3");
    }

    [Test]
    public static async Task Heisspfad_TunnelRahmen_OhneAllokation()
    {
        var rahmen = Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, TvA);
        var alles = new byte[rahmen.Length * 1200];
        for (var i = 0; i < 1200; i++) rahmen.CopyTo(alles, i * rahmen.Length);
        var k = ServerIn(ServerZustand.Aktiv);
        using var leser = new RahmenLeser(new MemoryStream(alles));
        for (var i = 0; i < 200; i++) await EinenAsync(leser, k);

        var vorher = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) await EinenAsync(leser, k);
        var leserBytes = GC.GetAllocatedBytesForCurrentThread() - vorher;
        var seq = new ReadOnlySequence<byte>(alles);
        vorher = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            Rahmen.VersucheLesen(ref seq, Rahmengrenzen.Aktiv, out var kopf, out var body, out _);
            k.Empfangen(kopf, body, 0);
        }
        var parserBytes = GC.GetAllocatedBytesForCurrentThread() - vorher;
        vorher = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) k.Empfangen(Kopf(rahmen), rahmen.AsSpan(Rahmen.KopfLaenge), 0);
        var kanalBytes = GC.GetAllocatedBytesForCurrentThread() - vorher;
        Pruefe.Gleich("0/0", $"{parserBytes}/{kanalBytes}", "Allokationen je 1000 Tunnelrahmen: Parser samt Kanal / Kanal");

        // async-Zustandsautomaten sind im Debug-Build Klassen; ohne Optimierung allokiert jeder Aufruf.
        var debug = typeof(RahmenLeser).Assembly.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false)
            .OfType<System.Diagnostics.DebuggableAttribute>().Any(a => a.IsJITOptimizerDisabled);
        if (debug) Console.WriteLine($"HINWEIS: Leser im Debug-Build {leserBytes / 1000} Byte je Rahmen (Zustandsautomat); im Release-Build geprüft.");
        else Pruefe.Gleich(0L, leserBytes, "Allokationen je 1000 Tunnelrahmen: Leser");
    }

    private static async ValueTask EinenAsync(RahmenLeser leser, ServerKanal k)
    {
        var r = await leser.LesenAsync(Rahmengrenzen.Aktiv);
        var e = k.Empfangen(r.Kopf, r.Body.Span, 0);
        if (e.Status != EingangsStatus.Annehmen) throw new PruefungException("Tunnelrahmen abgelehnt: " + e.Fehler);
    }

    private static ReadOnlySequence<byte> Segmente(byte[] daten, int a, int b)
    {
        var s1 = new Segment(daten.AsMemory(0, a));
        var s2 = s1.Anhaengen(daten.AsMemory(a, b - a));
        var s3 = s2.Anhaengen(daten.AsMemory(b));
        return new ReadOnlySequence<byte>(s1, 0, s3, s3.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> speicher) => Memory = speicher;

        public Segment Anhaengen(ReadOnlyMemory<byte> speicher)
        {
            var s = new Segment(speicher) { RunningIndex = RunningIndex + Memory.Length };
            Next = s;
            return s;
        }
    }
}
