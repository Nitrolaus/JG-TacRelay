// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using Funk.Protokoll.Schutz;
using Funk.Protokoll.Sprache;

namespace Funk.Tests;

/// <summary>Testschlüssel und Datagramme aus 8.3, dazu eine Empfangsumgebung für die Prüfkette 5.9.</summary>
internal static class SprachTestwerte
{
    public const uint SitzungA = 0x1a2b3c4d;
    public const uint SitzungB = 0x5e6f7081;

    public static readonly byte[] KHoch0 = Hex("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
    public static readonly byte[] IvHoch0 = Hex("a0a1a2a3a4a5a6a7a8a9aaab");
    public static readonly byte[] KRunter0 = Hex("202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f");
    public static readonly byte[] IvRunter0 = Hex("b0b1b2b3b4b5b6b7b8b9babb");
    public static readonly byte[] KHoch1 = Hex("404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f");
    public static readonly byte[] IvHoch1 = Hex("c0c1c2c3c4c5c6c7c8c9cacb");

    public static readonly byte[] OpusAttrappe = Hex("f801020304050607");

    public static readonly byte[] TvA = Hex("011000001a2b3c4d0000000000000005cefa04952a75d72b9f0fa81e8f9a6f33fadd803ce6f3a15ea103cbea77c18b354dd540186d");
    public static readonly byte[] TvB = Hex("011100005e6f7081000000000000002a9c7690b94867debba4d41624c65308a12e168a1c633470cb5c50af7c7cbb3338fd7167f180cdb9");
    public static readonly byte[] TvC = Hex("010400001a2b3c4d0000000000000006f8e1ac92fb8f3bc066323499a1d6928aceaf234f5024fa1affe3d9e53ec31d96");
    public static readonly byte[] TvD = Hex("011001001a2b3c4d0000000100000000dc393148d56cede37bc70711f32fd891159aae37b5ee596eacdb60");
    public static readonly byte[] TvE = Hex("010200005e6f7081000000000000002b333211e4a53a9e0037b91c1f79252e3b83c9ba39a8c66b1c2a3da09f88176547");
    public static readonly byte[] TvF = Hex("010100001a2b3c4d0000000000000000e6187c2d45cb02bf626587d3077ac0deea6b42ff94f0cf209c1998c8323032fa");

    /// <summary>VoiceKeys-Anfrage aus 8.4; der Body ab Offset 2 ist das Material der Epoche 0.</summary>
    public static readonly byte[] VoiceKeysRahmen = Hex(
        "0000006201008002800000010000000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f" +
        "a0a1a2a3a4a5a6a7a8a9aaab202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3fb0b1b2b3b4b5b6b7b8b9babb");

    public static byte[] Hex(string text) => Convert.FromHexString(text.Replace(" ", "", StringComparison.Ordinal));

    public static RichtungsSchluessel Hoch0() => new(0, KHoch0, IvHoch0);
    public static RichtungsSchluessel Runter0() => new(0, KRunter0, IvRunter0);
    public static RichtungsSchluessel Hoch1() => new(1, KHoch1, IvHoch1);

    public static byte[] Kopie(byte[] quelle, Action<byte[]> aenderung)
    {
        var d = (byte[])quelle.Clone();
        aenderung(d);
        return d;
    }

    public static Empfangsergebnis Server(TestUmgebung umgebung, ReadOnlySpan<byte> datagramm, Span<byte> klartext,
        long jetzt = 0, Weg weg = Weg.Udp, uint tragende = 0) =>
        Empfangspruefung.Pruefen(umgebung, datagramm, new Empfangsdaten(Richtung.Hoch, weg, jetzt, false, tragende),
            jetzt, klartext, out _, out _);

    public static Empfangsergebnis Client(TestUmgebung umgebung, ReadOnlySpan<byte> datagramm, Span<byte> klartext, long jetzt = 0) =>
        Empfangspruefung.Pruefen(umgebung, datagramm, new Empfangsdaten(Richtung.Runter, Weg.Udp, jetzt), jetzt, klartext, out _, out _);
}

/// <summary>Sitzungstabelle, Budget und Kopierprüfung zum Einstellen im Test.</summary>
internal sealed class TestUmgebung(params EmpfangsKanal[] kanaele) : IEmpfangsUmgebung
{
    public bool BudgetFrei { get; set; } = true;
    public HashSet<ulong> Kopierzaehler { get; } = [];

    public EmpfangsKanal? KanalSuchen(uint sitzungsId)
    {
        foreach (var k in kanaele)
            if (k.SitzungsId == sitzungsId) return k;
        return null;
    }
    public bool BudgetNehmen(EmpfangsKanal kanal) => BudgetFrei;
    public bool DuplikatEntschluesseln(EmpfangsKanal kanal, ulong zaehler) => Kopierzaehler.Contains(zaehler);
}

/// <summary>Prüfwerte 8.1, 8.2 und 8.3 (P-03, P-04).</summary>
internal static class SpracheVektorTests
{
    private static byte[] Hex(string text) => SprachTestwerte.Hex(text);

    // ---------------------------------------------------------------- 8.1 Werkzeugprüfung

    [Test]
    public static void Werkzeug_8_1_Gcm_Tc13_Tc14()
    {
        var schluessel = new byte[32];
        var nonce = new byte[12];
        using var aes = new AesGcm(schluessel, 16);
        var tag = new byte[16];
        aes.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag);
        Pruefe.BytesGleich(Hex("530f8afbc74536b9a963b4f1c4cb738b"), tag, "TC13 Tag");

        var chiffrat = new byte[16];
        aes.Encrypt(nonce, new byte[16], chiffrat, tag);
        Pruefe.BytesGleich(Hex("cea7403d4d606b6e074ec5d3baf39d18"), chiffrat, "TC14 Chiffrat");
        Pruefe.BytesGleich(Hex("d0d1c8a799996bf0265b98b5d48ab919"), tag, "TC14 Tag");
    }

    [Test]
    public static void Werkzeug_8_1_UeberRichtungsSchluessel()
    {
        // IV 0 und Zähler 0 ergeben Nonce 0; ohne AAD sind das TC13 und TC14.
        using var k = new RichtungsSchluessel(0, new byte[32], new byte[12]);
        var tag = new byte[16];
        k.Versiegeln(0, [], [], [], tag);
        Pruefe.BytesGleich(Hex("530f8afbc74536b9a963b4f1c4cb738b"), tag, "TC13 Tag");

        var chiffrat = new byte[16];
        k.Versiegeln(0, [], new byte[16], chiffrat, tag);
        Pruefe.BytesGleich(Hex("cea7403d4d606b6e074ec5d3baf39d18"), chiffrat, "TC14 Chiffrat");
        Pruefe.BytesGleich(Hex("d0d1c8a799996bf0265b98b5d48ab919"), tag, "TC14 Tag");

        var zurueck = new byte[16];
        Pruefe.Wahr(k.Oeffnen(0, [], chiffrat, tag, zurueck), "TC14 öffnen");
        Pruefe.BytesGleich(new byte[16], zurueck, "TC14 Klartext");
    }

    // ---------------------------------------------------------------- 8.2 Nonce-Bildung

    private static readonly (string Iv, ulong Zaehler, string Nonce)[] Tabelle82 =
    [
        ("000102030405060708090a0b", 1, "000102030405060708090a0a"),
        ("000102030405060708090a0b", 0x0102030405060708, "00010203050705030d0f0d03"),
        ("a0a1a2a3a4a5a6a7a8a9aaab", 0, "a0a1a2a3a4a5a6a7a8a9aaab"),
        ("a0a1a2a3a4a5a6a7a8a9aaab", 5, "a0a1a2a3a4a5a6a7a8a9aaae"),
        ("b0b1b2b3b4b5b6b7b8b9babb", 42, "b0b1b2b3b4b5b6b7b8b9ba91"),
        ("c0c1c2c3c4c5c6c7c8c9cacb", 1UL << 32, "c0c1c2c3c4c5c6c6c8c9cacb"),
    ];

    [Test]
    public static void Nonce_8_2_Tabelle()
    {
        var nonce = new byte[Nonce.Laenge];
        foreach (var (iv, zaehler, erwartet) in Tabelle82)
        {
            Nonce.Bilden(Hex(iv), zaehler, nonce);
            Pruefe.BytesGleich(Hex(erwartet), nonce, $"IV {iv}, Zähler {zaehler}");
        }
    }

    [Test]
    public static void Nonce_P03_Zaehler2hoch63()
    {
        var nonce = new byte[Nonce.Laenge];
        Nonce.Bilden(SprachTestwerte.IvHoch0, 1UL << 63, nonce);
        Pruefe.BytesGleich(Hex("a0a1a2a324a5a6a7a8a9aaab"), nonce, "Zähler 2^63 kippt das oberste Bit von Byte 4");
        Nonce.Bilden(SprachTestwerte.IvHoch0, ulong.MaxValue, nonce);
        Pruefe.BytesGleich(Hex("a0a1a2a35b5a595857565554"), nonce, "Zähler 2^64-1");
    }

    // ---------------------------------------------------------------- 8.3 Datagramme bytegenau

    private static void Vergleichen(RichtungsSchluessel k, SprachKopf kopf, ReadOnlySpan<byte> klartext,
        string kopfHex, string nonceHex, string chiffratHex, string tagHex, byte[] datagramm, string name)
    {
        var iv = kopf.Typ is PaketTyp.SpracheRunter or PaketTyp.PfadPruefung or PaketTyp.KeepaliveEcho
            ? SprachTestwerte.IvRunter0
            : kopf.Epoche == 1 ? SprachTestwerte.IvHoch1 : SprachTestwerte.IvHoch0;
        var nonce = new byte[Nonce.Laenge];
        Nonce.Bilden(iv, kopf.Zaehler, nonce);
        Pruefe.BytesGleich(Hex(nonceHex), nonce, name + " Nonce");

        var ziel = new byte[Sprachkanal.MaxDatagramm];
        var n = Datagramm.Versiegeln(k, kopf, klartext, ziel);
        Pruefe.Gleich(datagramm.Length, n, name + " Länge");
        Pruefe.BytesGleich(Hex(kopfHex), ziel.AsSpan(0, 16), name + " Kopf");
        Pruefe.BytesGleich(Hex(chiffratHex), ziel.AsSpan(16, klartext.Length), name + " Chiffrat");
        Pruefe.BytesGleich(Hex(tagHex), ziel.AsSpan(16 + klartext.Length, 16), name + " Tag");
        Pruefe.BytesGleich(datagramm, ziel.AsSpan(0, n), name + " Datagramm");

        var zurueck = new byte[klartext.Length];
        Pruefe.Wahr(Datagramm.Oeffnen(k, datagramm, zurueck), name + " Rückweg");
        Pruefe.BytesGleich(klartext, zurueck, name + " Klartext zurück");
    }

    private static byte[] KlartextA()
    {
        var p = new byte[64];
        var n = SpracheHoch.Schreiben(p, 100, 3, Kennzeichen.Beginn, Zielart.Netz, [2, 7], SprachTestwerte.OpusAttrappe);
        return p[..n];
    }

    [Test]
    public static void TvA_SpracheHoch()
    {
        var klartext = KlartextA();
        Pruefe.BytesGleich(Hex("00000064 0003 01 01 02 0002 0007 f801020304050607"), klartext, "TV-A Klartext (21 B)");
        using var k = SprachTestwerte.Hoch0();
        Vergleichen(k, new SprachKopf(PaketTyp.SpracheHoch, 0, SprachTestwerte.SitzungA, 5), klartext,
            "0110 0000 1a2b3c4d 0000000000000005", "a0a1a2a3a4a5a6a7a8a9aaae",
            "cefa04952a75d72b9f0fa81e8f9a6f33fadd803ce6", "f3a15ea103cbea77c18b354dd540186d", SprachTestwerte.TvA, "TV-A");
        Pruefe.Gleich(53, SprachTestwerte.TvA.Length, "TV-A 53 B");
    }

    [Test]
    public static void TvB_SpracheRunter_GestempeltAusTvA()
    {
        var hochKlartext = KlartextA();
        Pruefe.Gleich(Formfehler.Keiner, SpracheHoch.Lesen(hochKlartext, out var hoch), "TV-A lesen");
        var p = new byte[64];
        // Absender 1001, der Empfänger hört nur Netz 7, erstes Paket der Phase an ihn: BEGINN vom Server.
        var n = SpracheRunter.Stempeln(p, 1001, hoch, beginn: true, Spruchart.Netz, [7]);
        var klartext = p[..n];
        Pruefe.BytesGleich(Hex("000003e9 00000064 0003 01 01 01 0007 f801020304050607"), klartext, "TV-B Klartext (23 B)");
        using var k = SprachTestwerte.Runter0();
        Vergleichen(k, new SprachKopf(PaketTyp.SpracheRunter, 0, SprachTestwerte.SitzungB, 42), klartext,
            "0111 0000 5e6f7081 000000000000002a", "b0b1b2b3b4b5b6b7b8b9ba91",
            "9c7690b94867debba4d41624c65308a12e168a1c633470", "cb5c50af7c7cbb3338fd7167f180cdb9", SprachTestwerte.TvB, "TV-B");
        Pruefe.Gleich(55, SprachTestwerte.TvB.Length, "TV-B 55 B");
    }

    [Test]
    public static void TvC_Keepalive()
    {
        var klartext = new byte[16];
        new Keepalive(1, 0x00001234).Schreiben(klartext);
        Pruefe.BytesGleich(Hex("00000001 00001234 0000000000000000"), klartext, "TV-C Klartext");
        using var k = SprachTestwerte.Hoch0();
        Vergleichen(k, new SprachKopf(PaketTyp.Keepalive, 0, SprachTestwerte.SitzungA, 6), klartext,
            "0104 0000 1a2b3c4d 0000000000000006", "a0a1a2a3a4a5a6a7a8a9aaad",
            "f8e1ac92fb8f3bc066323499a1d6928a", "ceaf234f5024fa1affe3d9e53ec31d96", SprachTestwerte.TvC, "TV-C");
    }

    [Test]
    public static void TvD_SpracheHochEnde_Epoche1_Zaehler2hoch32()
    {
        var p = new byte[16];
        var n = SpracheHoch.Schreiben(p, 120, 3, Kennzeichen.Ende, Zielart.Netz, [2], []);
        var klartext = p[..n];
        Pruefe.BytesGleich(Hex("00000078 0003 02 01 01 0002"), klartext, "TV-D Klartext (11 B)");
        using var k = SprachTestwerte.Hoch1();
        Vergleichen(k, new SprachKopf(PaketTyp.SpracheHoch, 1, SprachTestwerte.SitzungA, 1UL << 32), klartext,
            "0110 0100 1a2b3c4d 0000000100000000", "c0c1c2c3c4c5c6c6c8c9cacb",
            "dc393148d56cede37bc707", "11f32fd891159aae37b5ee596eacdb60", SprachTestwerte.TvD, "TV-D");
        Pruefe.Gleich(43, SprachTestwerte.TvD.Length, "TV-D 43 B");
    }

    [Test]
    public static void TvE_PfadPruefung()
    {
        var klartext = Hex("0f0e0d0c0b0a09080706050403020100");
        using var k = SprachTestwerte.Runter0();
        Vergleichen(k, new SprachKopf(PaketTyp.PfadPruefung, 0, SprachTestwerte.SitzungB, 43), klartext,
            "0102 0000 5e6f7081 000000000000002b", "b0b1b2b3b4b5b6b7b8b9ba90",
            "333211e4a53a9e0037b91c1f79252e3b", "83c9ba39a8c66b1c2a3da09f88176547", SprachTestwerte.TvE, "TV-E");
    }

    [Test]
    public static void TvF_Anmeldung()
    {
        var klartext = new byte[16];
        Steuerpakete.AnmeldungSchreiben(klartext);
        using var k = SprachTestwerte.Hoch0();
        Vergleichen(k, new SprachKopf(PaketTyp.Anmeldung, 0, SprachTestwerte.SitzungA, 0), klartext,
            "0101 0000 1a2b3c4d 0000000000000000", "a0a1a2a3a4a5a6a7a8a9aaab",
            "e6187c2d45cb02bf626587d3077ac0de", "ea6b42ff94f0cf209c1998c8323032fa", SprachTestwerte.TvF, "TV-F");
    }

    [Test]
    public static void Material_VoiceKeys84_PasstZu83()
    {
        // Querprobe: Das Material aus dem VoiceKeys-Rahmen (8.4, Body ab Offset 2) ergibt die Schlüssel aus 8.3.
        var body = SprachTestwerte.VoiceKeysRahmen.AsSpan(12);
        Pruefe.Gleich(90, body.Length, "VoiceKeys-Body");
        Pruefe.Gleich(0, (int)body[0], "Epoche 0");
        Pruefe.Gleich(0, (int)body[1], "Grund START");
        var material = body[2..];
        Pruefe.Gleich(Schluesselmaterial.Laenge, material.Length, "88 Byte Material");

        using var hoch = Schluesselmaterial.Hoch(0, material);
        using var runter = Schluesselmaterial.Runter(0, material);
        var ziel = new byte[64];
        var n = Datagramm.Versiegeln(hoch, new SprachKopf(PaketTyp.SpracheHoch, 0, SprachTestwerte.SitzungA, 5), KlartextA(), ziel);
        Pruefe.BytesGleich(SprachTestwerte.TvA, ziel.AsSpan(0, n), "TV-A aus VoiceKeys-Material");
        var klartext = new byte[64];
        Pruefe.Wahr(Datagramm.Oeffnen(runter, SprachTestwerte.TvB, klartext), "TV-B mit K_runter aus VoiceKeys-Material");
    }

    // ---------------------------------------------------------------- 8.3 durch die vollständige Empfangsprüfung (P-04)

    [Test]
    public static void Empfang_Server_TvF_TvA_TvC_TvD_Angenommen()
    {
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        kanal.Ankuendigen(SprachTestwerte.Hoch1(), 0); // Epoche 1 angekündigt, damit TV-D angenommen werden darf
        var u = new TestUmgebung(kanal);
        var k = new byte[Sprachkanal.EmpfangspufferLaenge];

        Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Server(u, SprachTestwerte.TvF, k), "TV-F");
        Pruefe.Wahr(Steuerpakete.AnmeldungGueltig(k.AsSpan(0, 16)), "TV-F 16 x 0x00");

        Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Server(u, SprachTestwerte.TvA, k), "TV-A");
        Pruefe.Gleich(Formfehler.Keiner, SpracheHoch.Lesen(k.AsSpan(0, 21), out var a), "TV-A Form");
        Pruefe.Gleich(100u, a.FrameNr, "TV-A Frame-Nr");
        Pruefe.Gleich((ushort)3, a.Sprechphase, "TV-A Sprechphase");
        Pruefe.Gleich(Kennzeichen.Beginn, a.Kennzeichen, "TV-A Kennzeichen");
        Pruefe.Gleich(Zielart.Netz, a.Zielart, "TV-A Zielart");
        Pruefe.Gleich(2, a.ZielAnzahl, "TV-A n");
        Pruefe.Gleich((ushort)2, a.Ziel(0), "TV-A Ziel 0");
        Pruefe.Gleich((ushort)7, a.Ziel(1), "TV-A Ziel 1");
        Pruefe.BytesGleich(SprachTestwerte.OpusAttrappe, a.Opus, "TV-A Opus");

        Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Server(u, SprachTestwerte.TvC, k), "TV-C");
        Pruefe.Wahr(Keepalive.TryLesen(k.AsSpan(0, 16), out var ka), "TV-C Form");
        Pruefe.Gleich(new Keepalive(1, 0x1234), ka, "TV-C Inhalt");

        Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Server(u, SprachTestwerte.TvD, k), "TV-D (Positivfall, 43 B)");
        Pruefe.Gleich(Formfehler.Keiner, SpracheHoch.Lesen(k.AsSpan(0, 11), out var d), "TV-D Form");
        Pruefe.Wahr(d.IstEnde, "TV-D ENDE");
        Pruefe.Gleich(120u, d.FrameNr, "TV-D Frame-Nr");
        Pruefe.Gleich(0, d.Opus.Length, "TV-D ohne Opus");

        Pruefe.Gleich(1UL << 32, kanal.Hoechster, "Höchstwert nach TV-D");
        Pruefe.Gleich(4L, kanal.Entschluesselungen, "vier Entschlüsselungen");
    }

    [Test]
    public static void Empfang_Client_TvB_TvE_Angenommen()
    {
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungB, Richtung.Runter, SprachTestwerte.Runter0());
        var u = new TestUmgebung(kanal);
        var k = new byte[Sprachkanal.EmpfangspufferLaenge];

        Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Client(u, SprachTestwerte.TvB, k), "TV-B");
        Pruefe.Gleich(Formfehler.Keiner, SpracheRunter.Lesen(k.AsSpan(0, 23), out var b), "TV-B Form");
        Pruefe.Gleich(1001u, b.Absender, "TV-B Absender");
        Pruefe.Gleich(100u, b.FrameNr, "TV-B Frame-Nr");
        Pruefe.Gleich((ushort)3, b.Sprechphase, "TV-B Sprechphase");
        Pruefe.Gleich(Kennzeichen.Beginn, b.Kennzeichen, "TV-B BEGINN");
        Pruefe.Gleich(Spruchart.Netz, b.Art, "TV-B Art");
        Pruefe.Gleich(1, b.NetzAnzahl, "TV-B k");
        Pruefe.Gleich((ushort)7, b.Netz(0), "TV-B Netz");
        Pruefe.BytesGleich(SprachTestwerte.OpusAttrappe, b.Opus, "TV-B Opus");

        Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Client(u, SprachTestwerte.TvE, k), "TV-E");
        Pruefe.Wahr(Steuerpakete.ChallengeGleich(k.AsSpan(0, 16), Hex("0f0e0d0c0b0a09080706050403020100")), "TV-E Challenge");
    }

    // ---------------------------------------------------------------- 8.3 Negativfälle

    private static (EmpfangsKanal Kanal, TestUmgebung Umgebung) ServerKanal()
    {
        var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        return (kanal, new TestUmgebung(kanal));
    }

    [Test]
    public static void Negativ_TvA_Zaehlerbyte15Geaendert_TagUngueltig()
    {
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var d = SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[15] = 0x04);
        var k = new byte[Sprachkanal.MaxKlartext];
        var e = SprachTestwerte.Server(u, d, k);
        Pruefe.Gleich(Empfangsergebnis.TagUngueltig, e, "Kopf-Byte 15 = 0x04");
        Pruefe.Gleich(1L, kanal.FehlschlaegeInEpoche, "gezählt");
        Pruefe.Wahr(kanal.IstNeu(4) && kanal.IstNeu(5), "Fenster unverändert");
        Pruefe.BytesGleich(new byte[21], k.AsSpan(0, 21), "kein Klartext bei falschem Tag");
    }

    [Test]
    public static void Negativ_TvA_GekipptesBit_TagUngueltig()
    {
        var k = new byte[Sprachkanal.MaxKlartext];
        foreach (var stelle in new[] { 16, 26, 36, 37, 44, 52 }) // Chiffrat Anfang, Mitte, Ende; Tag Anfang, Mitte, Ende
        {
            var (kanal, u) = ServerKanal();
            using var aufraeumen = kanal;
            var d = SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[stelle] ^= 0x01);
            Pruefe.Gleich(Empfangsergebnis.TagUngueltig, SprachTestwerte.Server(u, d, k), $"Bit gekippt an Byte {stelle}");
        }
    }

    [Test]
    public static void Negativ_TvA_MitKRunter_TagUngueltig()
    {
        var k = new byte[Sprachkanal.MaxKlartext];
        using var runterMitIvHoch = new RichtungsSchluessel(0, SprachTestwerte.KRunter0, SprachTestwerte.IvHoch0);
        using var runter = SprachTestwerte.Runter0();
        Pruefe.Falsch(Datagramm.Oeffnen(runterMitIvHoch, SprachTestwerte.TvA, k), "K_runter mit IV_hoch");
        Pruefe.Falsch(Datagramm.Oeffnen(runter, SprachTestwerte.TvA, k), "K_runter mit IV_runter");
    }

    [Test]
    public static void Negativ_TvA_Typ0x11_BeimServer_VorDemEntschluesseln()
    {
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var d = SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[1] = 0x11);
        var e = SprachTestwerte.Server(u, d, new byte[Sprachkanal.MaxKlartext]);
        Pruefe.Gleich(Empfangsergebnis.FalscheRichtung, e, "Typ 0x11 hoch");
        Pruefe.Wahr(e.Schritt() < 7, "vor dem Entschlüsseln");
        Pruefe.Gleich(0L, kanal.Entschluesselungen, "nichts entschlüsselt");
    }

    [Test]
    public static void Negativ_TvA_Reserviert1_VorDemEntschluesseln()
    {
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var d = SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[3] = 0x01);
        Pruefe.Gleich(Empfangsergebnis.ReserviertGesetzt, SprachTestwerte.Server(u, d, new byte[Sprachkanal.MaxKlartext]), "reserviert = 1");
        Pruefe.Gleich(0L, kanal.Entschluesselungen, "nichts entschlüsselt");
    }

    [Test]
    public static void Negativ_TvA_Zweimal_ReplayFenster()
    {
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var k = new byte[Sprachkanal.MaxKlartext];
        Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Server(u, SprachTestwerte.TvA, k), "erstes Mal");
        var e = SprachTestwerte.Server(u, SprachTestwerte.TvA, k);
        Pruefe.Gleich(Empfangsergebnis.Replay, e, "zweites Mal");
        Pruefe.Gleich(6, e.Schritt(), "in Schritt 6, vor dem Entschlüsseln");
        Pruefe.Gleich(1L, kanal.Entschluesselungen, "nur einmal entschlüsselt");
    }

    [Test]
    public static void Negativ_TvA_Epoche1NichtAngekuendigt_VorDemEntschluesseln()
    {
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var d = SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[2] = 0x01);
        Pruefe.Gleich(Empfangsergebnis.EpocheNichtAngenommen, SprachTestwerte.Server(u, d, new byte[Sprachkanal.MaxKlartext]), "Epoche 1");
        Pruefe.Gleich(0L, kanal.Entschluesselungen, "nichts entschlüsselt");
    }

    [Test]
    public static void Negativ_Laengen_40Hoch_44RunterBeimClient()
    {
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var k = new byte[Sprachkanal.MaxKlartext];
        var e = SprachTestwerte.Server(u, SprachTestwerte.TvA.AsSpan(0, 40), k);
        Pruefe.Gleich(Empfangsergebnis.LaengeUngueltig, e, "40 Byte");
        Pruefe.Gleich(1, e.Schritt(), "in Schritt 1");

        using var client = new EmpfangsKanal(SprachTestwerte.SitzungB, Richtung.Runter, SprachTestwerte.Runter0());
        var uc = new TestUmgebung(client);
        var e44 = SprachTestwerte.Client(uc, SprachTestwerte.TvB.AsSpan(0, 44), k);
        Pruefe.Gleich(Empfangsergebnis.LaengePasstNichtZumTyp, e44, "SPRACHE_RUNTER mit 44 Byte");
        Pruefe.Gleich(0L, client.Entschluesselungen, "nichts entschlüsselt");
    }

    [Test]
    public static void Negativ_TvA_TunnelFremderSitzung_Protokoll()
    {
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var e = SprachTestwerte.Server(u, SprachTestwerte.TvA, new byte[Sprachkanal.MaxKlartext], weg: Weg.Tunnel, tragende: SprachTestwerte.SitzungB);
        Pruefe.Gleich(Empfangsergebnis.TunnelFremdeSitzung, e, "TV-A im Tunnel einer anderen Sitzung");
        Pruefe.Gleich(Empfangsergebnis.Angenommen,
            SprachTestwerte.Server(u, SprachTestwerte.TvA, new byte[Sprachkanal.MaxKlartext], weg: Weg.Tunnel, tragende: SprachTestwerte.SitzungA),
            "im Tunnel der eigenen Sitzung angenommen");
    }

    [Test]
    public static void Grenzen_P04_41Hoch_45Runter_ErreichenDenTag()
    {
        var k = new byte[Sprachkanal.MaxKlartext];
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var d41 = SprachTestwerte.TvA.AsSpan(0, 41).ToArray();
        Pruefe.Gleich(Empfangsergebnis.TagUngueltig, SprachTestwerte.Server(u, d41, k), "41 Byte hoch besteht Schritt 1 und 2");

        using var client = new EmpfangsKanal(SprachTestwerte.SitzungB, Richtung.Runter, SprachTestwerte.Runter0());
        var uc = new TestUmgebung(client);
        var d45 = SprachTestwerte.TvB.AsSpan(0, 45).ToArray();
        Pruefe.Gleich(Empfangsergebnis.TagUngueltig, SprachTestwerte.Client(uc, d45, k), "45 Byte runter besteht Schritt 1 und 2");
    }

    // ---------------------------------------------------------------- weitere Schritte aus 5.9

    [Test]
    public static void Schritte_0_bis_4_JeEinVerwurf()
    {
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var k = new byte[Sprachkanal.MaxKlartext];
        long s = System.Diagnostics.Stopwatch.Frequency;

        Pruefe.Gleich(Empfangsergebnis.ZuAlt, Empfangspruefung.Pruefen(u, SprachTestwerte.TvA,
            new Empfangsdaten(Richtung.Hoch, Weg.Udp, 0), s * 61 / 1000, k, out _, out _), "Sprache 61 ms alt");
        Pruefe.Gleich(Empfangsergebnis.TagUngueltig, Empfangspruefung.Pruefen(u, SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[52] ^= 1),
            new Empfangsdaten(Richtung.Hoch, Weg.Udp, 0), s * 60 / 1000, k, out _, out _), "Sprache genau 60 ms alt läuft weiter");
        Pruefe.Gleich(Empfangsergebnis.Angenommen, Empfangspruefung.Pruefen(u, SprachTestwerte.TvC,
            new Empfangsdaten(Richtung.Hoch, Weg.Udp, 0), s, k, out _, out _), "KEEPALIVE altert nicht (nur Sprache)");

        Pruefe.Gleich(Empfangsergebnis.LaengeUngueltig, Empfangspruefung.Pruefen(u, SprachTestwerte.TvA,
            new Empfangsdaten(Richtung.Hoch, Weg.Udp, 0, Abgeschnitten: true), 0, k, out _, out _), "abgeschnitten");
        Pruefe.Gleich(Empfangsergebnis.LaengeUngueltig, SprachTestwerte.Server(u, new byte[1201], k), "1201 Byte");
        Pruefe.Gleich(Empfangsergebnis.VersionFalsch, SprachTestwerte.Server(u, SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[0] = 2), k), "Version 2");
        Pruefe.Gleich(Empfangsergebnis.TypUnbekannt, SprachTestwerte.Server(u, SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[1] = 0x06), k), "Typ 0x06");
        Pruefe.Gleich(Empfangsergebnis.LaengePasstNichtZumTyp, SprachTestwerte.Server(u, SprachTestwerte.TvC.AsSpan(0, 47), k), "KEEPALIVE 47 Byte");
        var zuLang = new byte[570];
        SprachTestwerte.TvA.AsSpan(0, 16).CopyTo(zuLang);
        Pruefe.Gleich(Empfangsergebnis.LaengePasstNichtZumTyp, SprachTestwerte.Server(u, zuLang, k), "SPRACHE_HOCH 570 Byte");

        Pruefe.Gleich(Empfangsergebnis.SitzungUnbekannt,
            SprachTestwerte.Server(u, SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[7] ^= 1), k), "fremde Sitzungs-ID");
        u.BudgetFrei = false;
        Pruefe.Gleich(Empfangsergebnis.BudgetErschoepft, SprachTestwerte.Server(u, SprachTestwerte.TvA, k), "Budget");
        Pruefe.Gleich(2L, kanal.Entschluesselungen, "nur die zwei Pakete, die Schritt 4 bestanden haben");
    }

    [Test]
    public static void Kopierpruefung_AuthentischesDuplikat_FensterUnveraendert()
    {
        var (kanal, u) = ServerKanal();
        using var aufraeumen = kanal;
        var k = new byte[Sprachkanal.MaxKlartext];
        Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Server(u, SprachTestwerte.TvA, k), "Original");
        u.Kopierzaehler.Add(5);
        Pruefe.Gleich(Empfangsergebnis.AuthentischesDuplikat, SprachTestwerte.Server(u, SprachTestwerte.TvA, k), "echte Kopie");
        Pruefe.Gleich(Empfangsergebnis.TagUngueltig,
            SprachTestwerte.Server(u, SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[52] ^= 1), k), "gefälschte Kopie");
        Pruefe.Gleich(5UL, kanal.Hoechster, "Fenster unverändert");
        Pruefe.Falsch(kanal.IstNeu(5), "5 bleibt gesehen");
    }
}
