// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Protokoll.Sprache;

namespace Funk.Tests;

/// <summary>Zähler, Schlüssel, Epochen und Rotation nach 5.7 und 5.8 mit simulierter Uhr (P-06).</summary>
internal static class SpracheSchluesselTests
{
    private static readonly long Sekunde = Stopwatch.Frequency;

    private static long Ms(long ms) => ms * Stopwatch.Frequency / 1000;

    private static byte[] Material(byte fuell)
    {
        var m = new byte[Schluesselmaterial.Laenge];
        for (var i = 0; i < m.Length; i++) m[i] = (byte)(fuell + i);
        return m;
    }

    private static readonly byte[] M0 = SprachTestwerte.VoiceKeysRahmen[14..];
    private static readonly byte[] M1 = Material(0x40);
    private static readonly byte[] M2 = Material(0x90);

    private static byte[] KeepaliveKlartext()
    {
        var k = new byte[16];
        new Keepalive(1, 2).Schreiben(k);
        return k;
    }

    private static byte[] EchoKlartext()
    {
        var k = new byte[16];
        new KeepaliveEcho(1, 2, 3).Schreiben(k);
        return k;
    }

    /// <summary>Versiegelt ein Paket mit festem Schlüssel und Zähler, an allen Kanälen vorbei.</summary>
    private static byte[] Paket(RichtungsSchluessel k, PaketTyp typ, uint sitzung, ulong zaehler)
    {
        var d = new byte[Sprachkanal.FestesDatagramm];
        var klartext = PaketTypen.RichtungVon(typ) == Richtung.Hoch ? KeepaliveKlartext() : EchoKlartext();
        Datagramm.Versiegeln(k, new SprachKopf(typ, k.Epoche, sitzung, zaehler), klartext, d);
        return d;
    }

    private static byte[] Senden(SendeKanal kanal, long jetzt)
    {
        var d = new byte[Sprachkanal.FestesDatagramm];
        var typ = kanal.Richtung == Richtung.Hoch ? PaketTyp.Keepalive : PaketTyp.KeepaliveEcho;
        var klartext = kanal.Richtung == Richtung.Hoch ? KeepaliveKlartext() : EchoKlartext();
        Pruefe.Wahr(kanal.Versiegeln(typ, klartext, d, jetzt, out var n), "senden");
        Pruefe.Gleich(d.Length, n, "48 Byte");
        return d;
    }

    private static Empfangsergebnis Empfangen(EmpfangsKanal kanal, byte[] datagramm, long jetzt) =>
        kanal.Oeffnen(datagramm, new byte[64], jetzt);

    // ---------------------------------------------------------------- 5.7 Zähler

    [Test]
    public static void Zaehler_5_7_AbNullUeberEpochenOhneLuecke()
    {
        using var hoch = new SendeKanal(SprachTestwerte.SitzungA, Richtung.Hoch, Schluesselmaterial.Hoch(0, M0), 0);
        Pruefe.Gleich(0UL, hoch.NaechsterZaehler, "beginnt bei 0");
        var anmeldung = new byte[16];
        var d = new byte[48];
        Pruefe.Wahr(hoch.Versiegeln(PaketTyp.Anmeldung, anmeldung, d, 0, out _), "ANMELDUNG verbraucht einen Wert");
        Pruefe.BytesGleich(SprachTestwerte.TvF, d, "erste ANMELDUNG ist TV-F");
        Senden(hoch, 0);
        Pruefe.Gleich(2UL, hoch.NaechsterZaehler, "jedes Paket jedes Typs zählt");

        hoch.Umschalten(Schluesselmaterial.Hoch(1, M1), Sekunde);
        var nachRotation = Senden(hoch, Sekunde);
        var kopf = SprachKopf.Lesen(nachRotation);
        Pruefe.Gleich((byte)1, kopf.Epoche, "neue Epoche im Kopf");
        Pruefe.Gleich(2UL, kopf.Zaehler, "Zähler läuft über die Epoche weiter");
        Pruefe.Gleich(1L, hoch.GesendetInEpoche, "Zählung je Epoche neu");
    }

    [Test]
    public static void Zaehler_FalscheRichtungOderLaenge_Wirft()
    {
        using var hoch = new SendeKanal(SprachTestwerte.SitzungA, Richtung.Hoch, Schluesselmaterial.Hoch(0, M0), 0);
        var d = new byte[600];
        Pruefe.Wirft<ArgumentException>(() => hoch.Versiegeln(PaketTyp.SpracheRunter, new byte[20], d, 0, out _), "runter-Typ hoch");
        Pruefe.Wirft<ArgumentException>(() => hoch.Versiegeln(PaketTyp.Keepalive, new byte[15], d, 0, out _), "KEEPALIVE 15 Byte");
        Pruefe.Wirft<ArgumentException>(() => hoch.Versiegeln(PaketTyp.SpracheHoch, new byte[8], d, 0, out _), "SPRACHE_HOCH 8 Byte");
        Pruefe.Wirft<ArgumentException>(() => hoch.Versiegeln((PaketTyp)0x06, new byte[16], d, 0, out _), "unbekannter Typ");
        Pruefe.Wirft<ArgumentException>(() => hoch.Umschalten(Schluesselmaterial.Hoch(2, M1), 0), "Epoche übersprungen");
        Pruefe.Gleich(0UL, hoch.NaechsterZaehler, "kein Wert verbraucht");
    }

    [Test]
    public static void Grenzen_5_8_Standardwerte()
    {
        var g = Rotationsgrenzen.Standard;
        Pruefe.Gleich(TimeSpan.FromMinutes(60), g.EpochenDauer, "60 min");
        Pruefe.Gleich(1L << 20, g.PaketeBisRotation, "2^20");
        Pruefe.Gleich(1L << 21, g.PaketeBisAnforderung, "2^21");
        Pruefe.Gleich(1L << 22, g.HarteGrenze, "2^22");
        Pruefe.Gleich(1L << 16, g.FehlschlaegeBisVerdacht, "2^16");
        Pruefe.Gleich(TimeSpan.FromSeconds(10), g.AntwortFrist, "Antwort 10 s");
        Pruefe.Gleich(TimeSpan.FromSeconds(10), g.VorigeNachUmschalten, "Server: vorige 10 s");
        Pruefe.Gleich(TimeSpan.FromSeconds(10), g.VorigeNachErstemPaket, "Client: 10 s nach erstem Paket");
        Pruefe.Gleich(TimeSpan.FromSeconds(20), g.VorigeSpaetestens, "Client: spätestens 20 s");
        Pruefe.Gleich(TimeSpan.FromSeconds(5), g.SperreBisNeueSitzung, "neue Sitzung nach 5 s");
    }

    [Test]
    public static void Grenzen_5_8_AnforderungUndHarteGrenze()
    {
        var g = new Rotationsgrenzen { PaketeBisAnforderung = 2, HarteGrenze = 3 };
        using var hoch = new SendeKanal(SprachTestwerte.SitzungA, Richtung.Hoch, Schluesselmaterial.Hoch(0, M0), 0, g);
        Senden(hoch, 0);
        Pruefe.Falsch(hoch.RotationAnfordern, "nach 1 Paket noch nicht");
        Senden(hoch, 0);
        Pruefe.Wahr(hoch.RotationAnfordern, "ab 2^21 (hier 2) anfordern");
        Senden(hoch, Sekunde);
        Pruefe.Wahr(hoch.Gesperrt, "harte Grenze erreicht");
        var d = new byte[48];
        Pruefe.Falsch(hoch.Versiegeln(PaketTyp.Keepalive, KeepaliveKlartext(), d, 2 * Sekunde, out var n), "darüber wird nichts gesendet");
        Pruefe.Gleich(0, n, "keine Länge");
        Pruefe.Gleich(3UL, hoch.NaechsterZaehler, "verworfen verbraucht keinen Zähler");
        Pruefe.Falsch(hoch.NeueSitzungNoetig(6 * Sekunde), "genau 5 s: noch nicht");
        Pruefe.Wahr(hoch.NeueSitzungNoetig(6 * Sekunde + 1), "länger als 5 s: neue Sitzung");

        hoch.Umschalten(Schluesselmaterial.Hoch(1, M1), 7 * Sekunde);
        Pruefe.Falsch(hoch.Gesperrt, "neue Epoche hebt die Sperre auf");
        Senden(hoch, 7 * Sekunde);
    }

    // ---------------------------------------------------------------- 5.8 Server: Auslöser, Ablauf, Fristen

    [Test]
    public static void Server_5_8_StartAntwortUndFrist()
    {
        using var bund = new ServerSchluesselbund(SprachTestwerte.SitzungA, M0, 0);
        Pruefe.Wahr(bund.AntwortAusstehend, "START wartet auf Antwort");
        Pruefe.Falsch(bund.AntwortErhalten(1, Ms(10)), "falsche Epoche ist PROTOKOLL");
        Pruefe.Falsch(bund.MussTrennen(10 * Sekunde - 1), "vor Fristablauf");
        Pruefe.Wahr(bund.AntwortErhalten(0, Ms(20)), "Antwort Epoche 0");
        Pruefe.Falsch(bund.AntwortErhalten(0, Ms(30)), "keine zweite Antwort");
        Pruefe.Falsch(bund.MussTrennen(100 * Sekunde), "nach der Antwort kein Trennen");

        using var ohneAntwort = new ServerSchluesselbund(SprachTestwerte.SitzungA, M0, 0);
        Pruefe.Wahr(ohneAntwort.MussTrennen(10 * Sekunde), "10 s ohne Antwort: trennen");
        Pruefe.Falsch(ohneAntwort.AntwortErhalten(0, 10 * Sekunde), "zu spät");
    }

    [Test]
    public static void Server_5_8_Ausloeser()
    {
        var g = new Rotationsgrenzen { PaketeBisRotation = 4, FehlschlaegeBisVerdacht = 2 };

        using (var bund = new ServerSchluesselbund(SprachTestwerte.SitzungA, M0, 0))
        {
            Pruefe.Gleich(Rotationsgrund.Keiner, bund.RotationFaellig(61 * 60 * Sekunde), "nicht solange START offen ist");
            bund.AntwortErhalten(0, 0);
            Pruefe.Gleich(Rotationsgrund.Keiner, bund.RotationFaellig(60 * 60 * Sekunde - 1), "vor 60 min");
            Pruefe.Gleich(Rotationsgrund.Zeit, bund.RotationFaellig(60 * 60 * Sekunde), "60 min");
        }

        using (var bund = new ServerSchluesselbund(SprachTestwerte.SitzungA, M0, 0, g))
        {
            bund.AntwortErhalten(0, 0);
            for (var i = 0; i < 3; i++) Senden(bund.Runter, 0);
            Pruefe.Gleich(Rotationsgrund.Keiner, bund.RotationFaellig(0), "3 runter");
            Senden(bund.Runter, 0);
            Pruefe.Gleich(Rotationsgrund.PaketeRunter, bund.RotationFaellig(0), "2^20 (hier 4) runter");
        }

        using (var bund = new ServerSchluesselbund(SprachTestwerte.SitzungA, M0, 0, g))
        using (var client = new SendeKanal(SprachTestwerte.SitzungA, Richtung.Hoch, Schluesselmaterial.Hoch(0, M0), 0))
        {
            bund.AntwortErhalten(0, 0);
            for (var i = 0; i < 4; i++) Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangen(bund.Hoch, Senden(client, 0), 0), $"hoch {i}");
            Pruefe.Gleich(Rotationsgrund.PaketeHoch, bund.RotationFaellig(0), "2^20 (hier 4) hoch");
        }

        using (var bund = new ServerSchluesselbund(SprachTestwerte.SitzungA, M0, 0, g))
        {
            bund.AntwortErhalten(0, 0);
            using var falsch = Schluesselmaterial.Hoch(0, M1);
            for (ulong z = 0; z < 2; z++) Empfangen(bund.Hoch, Paket(falsch, PaketTyp.Keepalive, SprachTestwerte.SitzungA, z), 0);
            Pruefe.Gleich(Rotationsgrund.Keiner, bund.RotationFaellig(0), "genau 2^16 (hier 2) Fehlschläge: noch nicht");
            Empfangen(bund.Hoch, Paket(falsch, PaketTyp.Keepalive, SprachTestwerte.SitzungA, 2), 0);
            Pruefe.Wahr(bund.Hoch.AngriffsVerdacht, "mehr als 2^16: Angriffsverdacht");
            Pruefe.Gleich(Rotationsgrund.Angriffsverdacht, bund.RotationFaellig(0), "erzwungene Rotation");
        }
    }

    [Test]
    public static void Rotation_P06_AnnahmefensterMitSimulierterUhr()
    {
        const uint S = SprachTestwerte.SitzungA;
        long t = 0;
        using var bund = new ServerSchluesselbund(S, M0, t);
        var hoch0 = Schluesselmaterial.Hoch(0, M0);
        var runter0 = Schluesselmaterial.Runter(0, M0);
        using var clientHoch = new SendeKanal(S, Richtung.Hoch, hoch0, t);
        using var clientRunter = new EmpfangsKanal(S, Richtung.Runter, runter0);
        Pruefe.Wahr(bund.AntwortErhalten(0, t), "START beantwortet");

        // Pakete der Epoche 0 zum späteren Nachreichen (umsortiert, verspätet)
        using var alterHoch = Schluesselmaterial.Hoch(0, M0);
        using var alterRunter = Schluesselmaterial.Runter(0, M0);

        t = 60 * 60 * Sekunde;
        Pruefe.Gleich(Rotationsgrund.Zeit, bund.RotationFaellig(t), "Rotation fällig");

        // Schritt 1: Server kündigt e+1 an und nimmt hoch e und e+1 an.
        Pruefe.Gleich((byte)1, bund.RotationBeginnen(M1, t), "e+1");
        Pruefe.Wahr(bund.RotationLaeuft, "Rotation läuft");
        Pruefe.Gleich(Rotationsgrund.Keiner, bund.RotationFaellig(t), "höchstens eine Rotation zugleich");
        Pruefe.Wirft<InvalidOperationException>(() => bund.RotationBeginnen(M2, t), "keine zweite Rotation");
        Pruefe.Gleich((byte)0, bund.Epoche, "runter noch in e");
        Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangen(bund.Hoch, Senden(clientHoch, t), t), "hoch e weiter angenommen");
        using var hoch2 = Schluesselmaterial.Hoch(2, M2);
        Pruefe.Gleich(Empfangsergebnis.EpocheNichtAngenommen, Empfangen(bund.Hoch, Paket(hoch2, PaketTyp.Keepalive, S, 900), t), "e+2 nie");

        // Schritt 2: Client installiert e+1, sendet hoch sofort in e+1, nimmt runter e und e+1 an.
        var tAntwort = t + Ms(15);
        clientHoch.Umschalten(Schluesselmaterial.Hoch(1, M1), tAntwort);
        clientRunter.Umschalten(Schluesselmaterial.Runter(1, M1), tAntwort);
        var hochE1 = Senden(clientHoch, tAntwort);
        Pruefe.Gleich(1UL, SprachKopf.Lesen(hochE1).Zaehler, "Zähler läuft über die Epoche");
        Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangen(bund.Hoch, hochE1, tAntwort + Ms(5)), "hoch e+1 vor der Antwort angenommen");
        Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangen(clientRunter, Senden(bund.Runter, tAntwort), tAntwort + Ms(5)), "runter e beim Client");

        // Schritt 3: Mit der Antwort sendet der Server runter in e+1; e bleibt 10 s.
        var tServer = t + Ms(30);
        Pruefe.Wahr(bund.AntwortErhalten(1, tServer), "Antwort e+1");
        Pruefe.Gleich((byte)1, bund.Epoche, "runter jetzt in e+1");
        Pruefe.Gleich<byte?>(0, bund.Hoch.VorigeEpoche, "hoch e als vorige");

        // Schritt 4: erstes runter-Paket in e+1 beim Client; e bleibt dort noch 10 s danach.
        var tErstes = t + Ms(50);
        Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangen(clientRunter, Senden(bund.Runter, tServer), tErstes), "erstes runter in e+1");

        Pruefe.Gleich(Empfangsergebnis.Angenommen,
            Empfangen(clientRunter, Paket(alterRunter, PaketTyp.KeepaliveEcho, S, 500), tErstes + 10 * Sekunde - 1), "Client: e knapp vor der Frist");
        Pruefe.Gleich(Empfangsergebnis.Angenommen,
            Empfangen(bund.Hoch, Paket(alterHoch, PaketTyp.Keepalive, S, 500), tServer + 10 * Sekunde - 1), "Server: e knapp vor der Frist");

        Pruefe.Gleich(Empfangsergebnis.EpocheNichtAngenommen,
            Empfangen(clientRunter, Paket(alterRunter, PaketTyp.KeepaliveEcho, S, 501), tErstes + 10 * Sekunde), "Client: e nach 10 s verworfen");
        Pruefe.Wahr(runter0.Verworfen, "Client: Dispose beim Verwerfen");
        Pruefe.Gleich(Empfangsergebnis.EpocheNichtAngenommen,
            Empfangen(bund.Hoch, Paket(alterHoch, PaketTyp.Keepalive, S, 501), tServer + 10 * Sekunde), "Server: e nach 10 s verworfen");
        Pruefe.Wahr(hoch0.Verworfen, "Client-Sendeschlüssel e seit dem Umschalten frei");
        Pruefe.Falsch(bund.RotationLaeuft, "Rotation abgeschlossen");
        Pruefe.Gleich(Rotationsgrund.Keiner, bund.RotationFaellig(tServer + 10 * Sekunde), "neue Epoche beginnt bei 0 min");
        Pruefe.Gleich(Rotationsgrund.Zeit, bund.RotationFaellig(tServer + 60 * 60 * Sekunde), "60 min nach dem Umschalten");
    }

    [Test]
    public static void Rotation_P06_ClientSpaetestens20s()
    {
        // Kommt runter nichts in e+1 an, verwirft der Client e spätestens 20 s nach seiner Antwort.
        var r0 = Schluesselmaterial.Runter(0, M0);
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Runter, r0);
        using var alt = Schluesselmaterial.Runter(0, M0);
        kanal.Umschalten(Schluesselmaterial.Runter(1, M1), 0);
        Pruefe.Gleich(Empfangsergebnis.Angenommen,
            Empfangen(kanal, Paket(alt, PaketTyp.KeepaliveEcho, SprachTestwerte.SitzungA, 1), 20 * Sekunde - 1), "e bis knapp 20 s");
        kanal.Takt(20 * Sekunde);
        Pruefe.Wahr(r0.Verworfen, "Takt verwirft nach 20 s");
        Pruefe.Gleich(Empfangsergebnis.EpocheNichtAngenommen,
            Empfangen(kanal, Paket(alt, PaketTyp.KeepaliveEcho, SprachTestwerte.SitzungA, 2), 20 * Sekunde), "danach nicht mehr");
    }

    [Test]
    public static void Rotation_P06_OhneAntwortTrennen()
    {
        using var bund = new ServerSchluesselbund(SprachTestwerte.SitzungA, M0, 0);
        bund.AntwortErhalten(0, 0);
        var material = new byte[Schluesselmaterial.Laenge];
        var t = Sekunde;
        Pruefe.Gleich((byte)1, bund.RotationBeginnen(t, material), "Rotation mit Zufallsmaterial");
        Pruefe.Falsch(material.AsSpan().SequenceEqual(new byte[Schluesselmaterial.Laenge]), "Material für VoiceKeys erzeugt");
        Pruefe.Falsch(bund.MussTrennen(t + 10 * Sekunde - 1), "vor 10 s");
        Pruefe.Wahr(bund.MussTrennen(t + 10 * Sekunde), "binnen 10 s keine Antwort: trennen");
        Pruefe.Falsch(bund.AntwortErhalten(1, t + 10 * Sekunde), "späte Antwort gilt nicht");
    }

    [Test]
    public static void Rotation_P06_EpochenMod256()
    {
        var r255 = new RichtungsSchluessel(255, SprachTestwerte.KRunter0, SprachTestwerte.IvRunter0);
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Runter, r255);
        using var alt = new RichtungsSchluessel(255, SprachTestwerte.KRunter0, SprachTestwerte.IvRunter0);
        using var e1 = new RichtungsSchluessel(1, SprachTestwerte.KRunter0, SprachTestwerte.IvRunter0);
        Pruefe.Wirft<ArgumentException>(() => kanal.Umschalten(e1, 0), "255 -> 1 übersprungen");
        using var e0 = new RichtungsSchluessel(0, SprachTestwerte.KHoch0, SprachTestwerte.IvHoch0);
        kanal.Umschalten(new RichtungsSchluessel(0, SprachTestwerte.KHoch0, SprachTestwerte.IvHoch0), 0);
        Pruefe.Gleich((byte)0, kanal.Epoche, "255 + 1 = 0");
        Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangen(kanal, Paket(alt, PaketTyp.KeepaliveEcho, SprachTestwerte.SitzungA, 1), 0), "vorige 255");
        Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangen(kanal, Paket(e0, PaketTyp.KeepaliveEcho, SprachTestwerte.SitzungA, 2), 0), "aktuelle 0");
        Pruefe.Gleich(Empfangsergebnis.EpocheNichtAngenommen, Empfangen(kanal, Paket(e1, PaketTyp.KeepaliveEcho, SprachTestwerte.SitzungA, 3), 0), "1 nicht angekündigt");
    }

    [Test]
    public static void Rotation_FensterGemeinsamUeberEpochen()
    {
        // Ein Zählerwert, der in e schon angenommen war, gilt auch in e+1 als Duplikat (5.9: ein Fenster für alle Epochen).
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, Schluesselmaterial.Hoch(0, M0));
        using var e0 = Schluesselmaterial.Hoch(0, M0);
        using var e1 = Schluesselmaterial.Hoch(1, M1);
        Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangen(kanal, Paket(e0, PaketTyp.Keepalive, SprachTestwerte.SitzungA, 7), 0), "7 in e");
        kanal.Ankuendigen(Schluesselmaterial.Hoch(1, M1), 0);
        Pruefe.Gleich(Empfangsergebnis.Replay, Empfangen(kanal, Paket(e1, PaketTyp.Keepalive, SprachTestwerte.SitzungA, 7), 0), "7 in e+1");
        Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangen(kanal, Paket(e1, PaketTyp.Keepalive, SprachTestwerte.SitzungA, 8), 0), "8 in e+1");
    }
}
