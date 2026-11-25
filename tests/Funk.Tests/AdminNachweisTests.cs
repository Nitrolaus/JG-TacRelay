// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using System.Text;
using Funk.Protokoll.Identitaet;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Admin-Nachweis ohne Server (3.9, 8.13 bis 8.15): TOTP nach RFC 6238/4226 mit den Vektoren der RFCs, Base32 nach
/// RFC 4648, otpauth-URI, Verschlüsselung der Geheimnisse mit AAD, Unterschrift des Admin-Schlüssels mit ihren
/// Bindungen, und das Adminbuch mit Einrichtung, Anmeldung, Fenster, Einmalverwendung, Notfallcodes und Sperren. Dazu die
/// Angriffsfälle aus der Sicherheitsprüfung: Code wiederholen, raten, fremde Unterschrift, Aussperren durch Fremde.
/// </summary>
internal static class AdminNachweisTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Geheimnis der RFC-Anhänge: ASCII "12345678901234567890".</summary>
    private static readonly byte[] RfcGeheimnis = Encoding.ASCII.GetBytes("12345678901234567890");

    private static Fingerabdruck Fp(int n) => new(Enumerable.Repeat((byte)n, 32).ToArray());

    // ---------------------------------------------------------------- 8.13 TOTP, HOTP, Base32

    [Test]
    public static void Hotp_Rfc4226AnhangD()
    {
        int[] erwartet = [755224, 287082, 359152, 969429, 338314, 254676, 287922, 162583, 399871, 520489];
        for (var i = 0; i < erwartet.Length; i++)
            Pruefe.Gleich(erwartet[i], Totp.Hotp(RfcGeheimnis, i), $"HOTP Zähler {i}");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => Totp.Hotp(RfcGeheimnis, -1), "negativer Zähler");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => Totp.Hotp(RfcGeheimnis, 0, 5), "5 Ziffern");
    }

    [Test]
    public static void Totp_Rfc6238AnhangB()
    {
        (long Zeit, int Code)[] faelle =
        [
            (59, 94287082), (1111111109, 07081804), (1111111111, 14050471),
            (1234567890, 89005924), (2000000000, 69279037), (20000000000, 65353130),
        ];
        foreach (var (zeit, code) in faelle)
        {
            var t = Totp.Zeitschritt(DateTimeOffset.FromUnixTimeSeconds(zeit));
            Pruefe.Gleich(zeit / 30, t, $"T bei {zeit}");
            Pruefe.Gleich(code, Totp.Hotp(RfcGeheimnis, t, 8), $"TOTP SHA-1 8 Ziffern bei {zeit}");
            // Die App zeigt 6 Ziffern: dieselbe Kürzung, nur modulo 10^6.
            Pruefe.Gleich(code % 1_000_000, Totp.Code(RfcGeheimnis, t), $"6 Ziffern bei {zeit}");
        }
        Pruefe.Gleich("287082", Totp.CodeText(RfcGeheimnis, 1), "Anzeigeform");
        Pruefe.Gleich("081804", Totp.CodeText(RfcGeheimnis, 1111111109 / 30), "führende Null bleibt");
        Pruefe.Gleich(-1L, Totp.Zeitschritt(DateTimeOffset.FromUnixTimeSeconds(-5)), "vor 1970 kein Schritt");
        Pruefe.Falsch(Totp.Passt(RfcGeheimnis, -1, 755224), "negativer Schritt passt nie");
        Pruefe.Wahr(Totp.Passt(RfcGeheimnis, 0, 755224), "Passt");
        Pruefe.Falsch(Totp.Passt(RfcGeheimnis, 0, 755225), "eine Ziffer daneben");
    }

    [Test]
    public static void Totp_EingabeLesen()
    {
        Pruefe.Wahr(Totp.TryLesen("123456", out var c) && c == 123456, "sechs Ziffern");
        Pruefe.Wahr(Totp.TryLesen("123 456", out c) && c == 123456, "mit Leerzeichen, wie die App gruppiert");
        Pruefe.Wahr(Totp.TryLesen("012-345", out c) && c == 12345, "mit Bindestrich und führender Null");
        foreach (var falsch in new[] { "", "12345", "1234567", "12345a", "١٢٣٤٥٦", "１２３４５６", new string('1', 40) })
            Pruefe.Falsch(Totp.TryLesen(falsch, out _), "ungültig: " + falsch);
    }

    [Test]
    public static void Base32_Rfc4648UndStrengeForm()
    {
        (string Klar, string Kodiert)[] faelle = [("", ""), ("f", "MY"), ("fo", "MZXQ"), ("foo", "MZXW6"), ("foob", "MZXW6YQ"), ("fooba", "MZXW6YTB"), ("foobar", "MZXW6YTBOI")];
        foreach (var (klar, kodiert) in faelle)
        {
            Pruefe.Gleich(kodiert, Base32.Kodieren(Encoding.ASCII.GetBytes(klar)), "kodieren " + klar);
            Pruefe.Wahr(Base32.TryDekodieren(kodiert, out var d) && Encoding.ASCII.GetString(d) == klar, "dekodieren " + kodiert);
        }
        Pruefe.Gleich("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Base32.Kodieren(RfcGeheimnis), "Geheimnis der RFC");
        Pruefe.Wahr(Base32.TryDekodieren("mzxw6ytboi", out var klein) && Encoding.ASCII.GetString(klein) == "foobar", "Kleinbuchstaben");
        Pruefe.Wahr(Base32.TryDekodieren("MZXW 6YTB OI======", out var gruppiert) && Encoding.ASCII.GetString(gruppiert) == "foobar", "Gruppen und Auffüllung");
        Pruefe.Falsch(Base32.TryDekodieren("MZ", out _), "Restbits ungleich 0");
        Pruefe.Falsch(Base32.TryDekodieren("MY=A", out _), "Zeichen nach der Auffüllung");
        Pruefe.Falsch(Base32.TryDekodieren("MY1", out _), "1 gehört nicht zum Alphabet");
        var zufall = Totp.NeuesGeheimnis();
        Pruefe.Gleich(20, zufall.Length, "160 Bit");
        Pruefe.Wahr(Base32.TryDekodieren(Totp.Gruppiert(zufall), out var zurueck) && zurueck.AsSpan().SequenceEqual(zufall), "Gruppiert zurück");
    }

    [Test]
    public static void Otpauth_UriFuerJedeAuthenticatorApp()
    {
        var uri = Totp.OtpauthUri("Mustermann", RfcGeheimnis);
        Pruefe.Gleich("otpauth://totp/JG%20TacRelay%3AMustermann?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=JG%20TacRelay&algorithm=SHA1&digits=6&period=30",
            uri, "Key Uri Format");
        var mitLeer = Totp.OtpauthUri("Neo Sar & Co", RfcGeheimnis);
        Pruefe.Wahr(mitLeer.Contains("JG%20TacRelay%3ANeo%20Sar%20%26%20Co?", StringComparison.Ordinal), "Name prozentkodiert: " + mitLeer);
        Pruefe.Gleich("GEZD GNBV GY3T QOJQ GEZD GNBV GY3T QOJQ", Totp.Gruppiert(RfcGeheimnis), "zum Abtippen");
    }

    [Test]
    public static void Otpauth_UriPasstAuchMitLangemNamenAufDenDraht()
    {
        // Ungünstigster Name: 32 Skalarwerte mit je 4 UTF-8-Byte (ein zulässiger SC-Name aus Buchstaben). Vorher galten 300
        // Zeichen: Der Server hätte das Geheimnis als abgeholt vermerkt, der Client die Antwort aber mit ZU_GROSS verworfen.
        // Seit JG TacRelay steht der Herausgeber zweimal prozentkodiert darin ("JG%20TacRelay"): 510 statt 508 Zeichen, die
        // Grenze von 512 hält.
        var name = string.Concat(Enumerable.Repeat("\U0001D400", 32));
        Pruefe.Gleich(name, Zeichenregeln.ScNameBereinigen(name), "zulässiger SC-Name");
        var uri = Totp.OtpauthUri(name, Totp.NeuesGeheimnis());
        Pruefe.Wahr(uri.Length is > 300 and <= 510, "Länge im ungünstigsten Fall: " + uri.Length);
        var antwort = new S.AdminEinrichtungAntwort { Geheimnis = Base32.Kodieren(new byte[20]), Uri = uri, GueltigBis = 1 };
        var gelesen = S.SteuerJson.Lesen(S.SteuerJson.Serialisieren(antwort), S.SteuerJson.Info<S.AdminEinrichtungAntwort>());
        Pruefe.Wahr(gelesen.Body is S.AdminEinrichtungAntwort a && a.Uri == uri, "Client nimmt die Antwort an: " + gelesen.Fehler + " " + gelesen.Feld);
        // Gegenprobe: über der Grenze weist der Client die Antwort ab.
        var zuLang = antwort with { Uri = uri + new string('x', S.Grenzen.OtpauthUriMax - uri.Length + 1) };
        var abgewiesen = S.SteuerJson.Lesen(S.SteuerJson.Serialisieren(zuLang), S.SteuerJson.Info<S.AdminEinrichtungAntwort>());
        Pruefe.Gleich(S.Fehlercode.ZuGross, abgewiesen.Fehler, "über 512 Zeichen");
    }

    // ---------------------------------------------------------------- 8.15 Verschlüsselung der Geheimnisse

    [Test]
    public static void Tresor_Vektor815UndBindungAnKontoUndGeneration()
    {
        // 8.15: Schlüssel 00..1f, Nonce a0..ab, Konto 7, Generation 1; die AAD stehen hier von Hand, nicht über den Code.
        var schluessel = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var aad = Convert.FromHexString("74616372656c617920746f7470207631000000000700000001");
        Pruefe.BytesGleich(aad, TotpTresor.Zusatzdaten(7, 1), "AAD nach 3.9");
        var chiffrat = Convert.FromHexString("a0a1a2a3a4a5a6a7a8a9aaabd72a4f1970fd35875b55b6e1344ef5e847946020254dc67285b0568b9dd68299f48e4b65");
        using var tresor = new TotpTresor(schluessel);
        var klar = tresor.Entschluesseln(chiffrat, 7, 1);
        Pruefe.Wahr(klar is not null && klar.AsSpan().SequenceEqual(RfcGeheimnis), "Vektor 8.15 entschlüsselt");
        Pruefe.Wahr(tresor.Entschluesseln(chiffrat, 8, 1) is null, "anderes Konto: kein Umhängen");
        Pruefe.Wahr(tresor.Entschluesseln(chiffrat, 7, 0) is null, "alte Generation: kein Zurückspielen");
        var kaputt = chiffrat.ToArray();
        kaputt[20] ^= 1;
        Pruefe.Wahr(tresor.Entschluesseln(kaputt, 7, 1) is null, "verändertes Chiffrat");
        Pruefe.Wahr(tresor.Entschluesseln(chiffrat.AsSpan(0, 20), 7, 1) is null, "zu kurz");

        var a = tresor.Verschluesseln(RfcGeheimnis, 7, 1);
        var b = tresor.Verschluesseln(RfcGeheimnis, 7, 1);
        Pruefe.Falsch(a.AsSpan().SequenceEqual(b), "je Verschlüsselung eine neue Nonce");
        Pruefe.Gleich(12 + 20 + 16, a.Length, "Nonce || Chiffrat || Tag");
        using var fremd = TotpTresor.Zufaellig();
        Pruefe.Wahr(fremd.Entschluesseln(a, 7, 1) is null, "anderer Serverschlüssel");
        Pruefe.Wirft<ArgumentException>(() => new TotpTresor(new byte[16]).Dispose(), "Schlüssel mit 16 Byte");

        var p1 = tresor.NotfallPruefwert("ABCD1234EFGH", 7);
        Pruefe.BytesGleich(p1, tresor.NotfallPruefwert("ABCD1234EFGH", 7), "Prüfwert stabil");
        Pruefe.Falsch(p1.AsSpan().SequenceEqual(tresor.NotfallPruefwert("ABCD1234EFGH", 8)), "Prüfwert an das Konto gebunden");
        Pruefe.Falsch(p1.AsSpan().SequenceEqual(fremd.NotfallPruefwert("ABCD1234EFGH", 7)), "Prüfwert am Serverschlüssel");
    }

    // ---------------------------------------------------------------- 8.14 Unterschrift des Admin-Schlüssels

    private static ECDsa SchluesselEins() => ECDsa.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        D = Convert.FromHexString("0000000000000000000000000000000000000000000000000000000000000001"),
        Q = new ECPoint
        {
            X = Convert.FromHexString("6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296"),
            Y = Convert.FromHexString("4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5"),
        },
    });

    [Test]
    public static void Adminnachweis_Vektor814UndBindungen()
    {
        var nonce = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var daten = Adminnachweis.Daten(0x1a2b3c4d, Fp(0x11), nonce, AdminZweck.Anmeldung);
        Pruefe.Gleich("74616372656c61792061646d696e207631001a2b3c4d"
            + "1111111111111111111111111111111111111111111111111111111111111111"
            + "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f414e4d454c44554e47",
            Convert.ToHexString(daten).ToLowerInvariant(), "unterschriebene Bytes 8.14");
        using var eins = SchluesselEins();
        var spki = eins.ExportSubjectPublicKeyInfo();
        var vektor = Convert.FromHexString("37331cc28433ccb0605e334453cc3dbd7291e50e66dee8b375ea58e1ec7ec974ed36f90621b124f439344a9d58d24b5e06a3c4ae6067b4117e6f85f0612e1e62");
        Pruefe.Wahr(Adminnachweis.Pruefen(spki, vektor, 0x1a2b3c4d, Fp(0x11), nonce, AdminZweck.Anmeldung), "Vektor 8.14 gilt");

        // Jede Bindung einzeln: andere Sitzung, anderes Gerät, andere Nonce, anderer Zweck.
        Pruefe.Falsch(Adminnachweis.Pruefen(spki, vektor, 0x1a2b3c4e, Fp(0x11), nonce, AdminZweck.Anmeldung), "andere Sitzung");
        Pruefe.Falsch(Adminnachweis.Pruefen(spki, vektor, 0x1a2b3c4d, Fp(0x12), nonce, AdminZweck.Anmeldung), "anderes Gerät");
        var andereNonce = nonce.ToArray();
        andereNonce[31] ^= 1;
        Pruefe.Falsch(Adminnachweis.Pruefen(spki, vektor, 0x1a2b3c4d, Fp(0x11), andereNonce, AdminZweck.Anmeldung), "andere Nonce");
        foreach (var zweck in new[] { AdminZweck.Antrag, AdminZweck.Geheimnis, AdminZweck.Einrichtung })
            Pruefe.Falsch(Adminnachweis.Pruefen(spki, vektor, 0x1a2b3c4d, Fp(0x11), nonce, zweck), "anderer Zweck " + zweck.Drahtname());

        using var anderer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Pruefe.Falsch(Adminnachweis.Pruefen(anderer.ExportSubjectPublicKeyInfo(), vektor, 0x1a2b3c4d, Fp(0x11), nonce, AdminZweck.Anmeldung), "anderer Schlüssel");
        var der = eins.SignData(daten, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Pruefe.Falsch(Adminnachweis.Pruefen(spki, der, 0x1a2b3c4d, Fp(0x11), nonce, AdminZweck.Anmeldung), "DER statt P1363");
        Pruefe.Falsch(Adminnachweis.Pruefen(spki, new byte[64], 0x1a2b3c4d, Fp(0x11), nonce, AdminZweck.Anmeldung), "Nullen");
        Pruefe.Falsch(Adminnachweis.Pruefen([1, 2, 3], vektor, 0x1a2b3c4d, Fp(0x11), nonce, AdminZweck.Anmeldung), "Müll als Schlüssel wirft nicht");
        Pruefe.Falsch(Adminnachweis.Pruefen(spki, vektor, 0x1a2b3c4d, Fp(0x11), nonce.AsSpan(0, 31), AdminZweck.Anmeldung), "Nonce 31 Byte");

        var frisch = Adminnachweis.Unterschreiben(eins, 5, Fp(3), nonce, AdminZweck.Geheimnis);
        Pruefe.Gleich(64, frisch.Length, "P1363 hat 64 Byte");
        Pruefe.Wahr(Adminnachweis.Pruefen(spki, frisch, 5, Fp(3), nonce, AdminZweck.Geheimnis), "eigene Unterschrift");
        Pruefe.Gleich("ANTRAG,GEHEIMNIS,EINRICHTUNG,ANMELDUNG", string.Join(",", Enum.GetValues<AdminZweck>().Select(z => z.Drahtname())), "Zwecke");
    }

    // ---------------------------------------------------------------- Adminbuch

    /// <summary>Ein Adminbuch mit Uhr, Tresor und einem Admin-Schlüssel je Konto.</summary>
    private sealed class Buchwelt : IDisposable
    {
        private readonly Dictionary<uint, ECDsa> _schluessel = [];

        public Buchwelt()
        {
            Buch = new Adminbuch(Uhr, Tresor);
        }

        public IdentitaetsUhr Uhr { get; } = new(Start);
        public TotpTresor Tresor { get; } = TotpTresor.Zufaellig();
        public Adminbuch Buch { get; }

        public ECDsa Schluessel(uint konto)
        {
            if (!_schluessel.TryGetValue(konto, out var s)) _schluessel[konto] = s = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return s;
        }

        public byte[] Spki(uint konto) => Schluessel(konto).ExportSubjectPublicKeyInfo();

        /// <summary>Nachweis wie vom Client: frische Nonce, Unterschrift für Sitzung 5 auf Gerät 9.</summary>
        public Nachweis N(uint konto, AdminZweck zweck, ECDsa? mit = null, uint sitzung = 5, Fingerabdruck? geraet = null)
        {
            var nonce = RandomNumberGenerator.GetBytes(32);
            var g = geraet ?? Fp(9);
            return new Nachweis(sitzung, g, nonce, Adminnachweis.Unterschreiben(mit ?? Schluessel(konto), sitzung, g, nonce, zweck));
        }

        public long Schritt => Totp.Zeitschritt(Uhr.GetUtcNow());

        public string Code(byte[] geheimnis, int versatz = 0) => Totp.CodeText(geheimnis, Schritt + versatz);

        /// <summary>Weiter in den nächsten Zeitschritt (jeder Code gilt einmal).</summary>
        public void Weiter() => Uhr.Vor(TimeSpan.FromSeconds(30));

        /// <summary>Vorsehen, Antrag, Ernennen, Geheimnis abholen, erster Code: der ganze Weg aus 3.9.</summary>
        public (byte[] Geheimnis, IReadOnlyList<string> Notfallcodes) Einrichten(uint konto)
        {
            Pruefe.Wahr(Buch.Vorsehen(konto).Ok, "vorsehen");
            var antrag = Buch.AntragStellen(konto, Spki(konto), N(konto, AdminZweck.Antrag));
            Pruefe.Wahr(antrag.Ok, "Antrag: " + antrag.Fehler);
            Pruefe.Wahr(Buch.Ernennen(konto, antrag.Kennung!).Ok, "ernennen");
            var abholen = Buch.GeheimnisAbholen(konto, N(konto, AdminZweck.Geheimnis));
            Pruefe.Wahr(abholen.Ok && abholen.Geheimnis is { Length: 20 }, "Geheimnis abgeholt");
            var g = abholen.Geheimnis!;
            var bestaetigt = Buch.EinrichtungBestaetigen(konto, N(konto, AdminZweck.Einrichtung), Code(g));
            Pruefe.Wahr(bestaetigt.Ok, "erster Code: " + bestaetigt.Fehler);
            return (g, bestaetigt.Notfallcodes!);
        }

        public Adminergebnis Anmelden(uint konto, string? code, string? notfall = null, uint geraetId = 1) =>
            Buch.Anmelden(konto, geraetId, N(konto, AdminZweck.Anmeldung), code, notfall);

        public void Dispose()
        {
            foreach (var s in _schluessel.Values) s.Dispose();
            Tresor.Dispose();
        }
    }

    [Test]
    public static void Adminbuch_EinrichtungSchrittFuerSchritt()
    {
        using var w = new Buchwelt();
        var b = w.Buch;
        Pruefe.Gleich(Fehlercode.KeinRecht, b.AntragStellen(7, w.Spki(7), w.N(7, AdminZweck.Antrag)).Fehler, "ohne admin-vorsehen kein Antrag");
        Pruefe.Wahr(b.Vorsehen(7).Ok, "vorsehen");
        Pruefe.Gleich(AdminZustand.Vorgesehen, b.Eintrag(7)!.Zustand, "VORGESEHEN");
        Pruefe.Gleich(Fehlercode.Ungueltig, b.AntragStellen(7, w.Spki(7), w.N(7, AdminZweck.Anmeldung)).Fehler, "Antrag mit Unterschrift für einen anderen Zweck");
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Pruefe.Gleich(Fehlercode.Ungueltig, b.AntragStellen(7, w.Spki(7), w.N(7, AdminZweck.Antrag, fremd)).Fehler, "ohne Besitz des beantragten Schlüssels");
        Pruefe.Gleich(Fehlercode.Ungueltig, b.AntragStellen(7, [1, 2, 3], w.N(7, AdminZweck.Antrag)).Fehler, "kein P-256-Schlüssel");

        var antrag = b.AntragStellen(7, w.Spki(7), w.N(7, AdminZweck.Antrag));
        Pruefe.Wahr(antrag.Ok, "Antrag");
        Pruefe.Gleich(Fingerabdruck.AusSpki(w.Spki(7)).Anzeige, antrag.Kennung, "Kennung = Anzeigeform des Admin-Schlüssels");
        var vorher = b.Stand;
        Pruefe.Gleich(Fehlercode.Ungueltig, b.Ernennen(7, "0000-0000-0000-0000").Fehler, "falsch vorgelesene Kennung");
        Pruefe.Wahr(ReferenceEquals(vorher, b.Stand), "ohne Seiteneffekt");
        Pruefe.Gleich(Fehlercode.Unbekannt, b.Ernennen(8, antrag.Kennung!).Fehler, "anderes Konto hat keinen Antrag");
        var ernannt = b.Ernennen(7, antrag.Kennung!.ToLowerInvariant());
        Pruefe.Wahr(ernannt.Ok && ernannt.EinrichtungBegonnen, "ernannt, Einrichtung beginnt (Kennung ohne Rücksicht auf Groß und klein)");
        Pruefe.Gleich(Start + Adminbuch.AbholFrist, ernannt.AbholbarBis, "abholbar 10 min");
        Pruefe.Gleich(AdminZustand.Einrichtung, b.Eintrag(7)!.Zustand, "EINRICHTUNG");
        Pruefe.Falsch(b.IstEingerichtet(7), "noch nicht eingerichtet");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Anmelden(7, "123456").Fehler, "vor der Einrichtung keine Anmeldung");

        Pruefe.Gleich(Fehlercode.KeinRecht, b.GeheimnisAbholen(7, w.N(7, AdminZweck.Anmeldung)).Fehler, "Abholen mit falschem Zweck");
        Pruefe.Gleich(Fehlercode.KeinRecht, b.GeheimnisAbholen(7, w.N(7, AdminZweck.Geheimnis, fremd)).Fehler, "Abholen mit fremdem Schlüssel");
        var abholen = b.GeheimnisAbholen(7, w.N(7, AdminZweck.Geheimnis));
        Pruefe.Wahr(abholen.Ok && abholen.Geheimnis is { Length: 20 }, "Geheimnis");
        var geheimnis = abholen.Geheimnis!;
        Pruefe.Gleich(Fehlercode.Ungueltig, b.GeheimnisAbholen(7, w.N(7, AdminZweck.Geheimnis)).Fehler, "genau einmal");

        // Ruhend nur verschlüsselt: weder roh noch als Base32 im Stand.
        var e = b.Eintrag(7)!;
        Pruefe.Falsch(e.Geheimnis!.Contains(Convert.ToBase64String(geheimnis), StringComparison.Ordinal), "nicht roh im Stand");
        Pruefe.Wahr(w.Tresor.Entschluesseln(Convert.FromBase64String(e.Geheimnis), 7, e.Generation) is { } klar && klar.AsSpan().SequenceEqual(geheimnis), "Chiffrat passt");
        Pruefe.Falsch(abholen.ToString().Contains(Base32.Kodieren(geheimnis), StringComparison.Ordinal), "ToString verrät das Geheimnis nicht");

        var falsch = b.EinrichtungBestaetigen(7, w.N(7, AdminZweck.Einrichtung), FalscherCode(w, geheimnis));
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, falsch.Fehler, "erster Code falsch");
        Pruefe.Gleich(Fehlercode.KeinRecht, b.EinrichtungBestaetigen(7, w.N(7, AdminZweck.Anmeldung), w.Code(geheimnis)).Fehler, "Bestätigung mit falschem Zweck");
        var ok = b.EinrichtungBestaetigen(7, w.N(7, AdminZweck.Einrichtung), w.Code(geheimnis));
        Pruefe.Wahr(ok.Ok, "eingerichtet");
        Pruefe.Gleich(Adminbuch.NotfallcodesAnzahl, ok.Notfallcodes!.Count, "10 Notfallcodes");
        Pruefe.Gleich(10, ok.Notfallcodes.Distinct(StringComparer.Ordinal).Count(), "verschieden");
        Pruefe.Wahr(ok.Notfallcodes.All(c => c.Length == 14 && c[4] == '-' && c[9] == '-'), "Form XXXX-XXXX-XXXX");
        Pruefe.Falsch(ok.ToString().Contains(ok.Notfallcodes[0], StringComparison.Ordinal), "ToString verrät keinen Notfallcode");
        Pruefe.Wahr(b.IstEingerichtet(7), "AKTIV");
        Pruefe.Falsch(b.Eintrag(7)!.Notfallcodes.Any(p => ok.Notfallcodes.Any(c => p.Contains(c.Replace("-", "", StringComparison.Ordinal), StringComparison.Ordinal))), "Notfallcodes nur als Prüfwerte");
        Pruefe.Gleich(Fehlercode.KeinRecht, b.AntragStellen(7, w.Spki(7), w.N(7, AdminZweck.Antrag)).Fehler, "nach der Ernennung kein weiterer Antrag ohne admin-vorsehen");
    }

    [Test]
    public static void Adminbuch_JederZeitschrittGiltEinmal()
    {
        using var w = new Buchwelt();
        var (g, _) = w.Einrichten(7);
        // Angriffsfall "Code wiederholen": der Code der Einrichtung gilt nicht noch einmal für die Anmeldung.
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Anmelden(7, w.Code(g)).Fehler, "derselbe Schritt wie die Einrichtung");
        w.Weiter();
        var a = w.Anmelden(7, w.Code(g), geraetId: 11);
        Pruefe.Wahr(a.Ok && a.NeuesGeraet, "Anmeldung, neues Gerät");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Anmelden(7, w.Code(g), geraetId: 11).Fehler, "Code mitgelesen und sofort wiederholt");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Buch.Bestaetigen(7, w.Code(g), null).Fehler, "auch nicht als Bestätigung");
        w.Weiter();
        var zweite = w.Anmelden(7, w.Code(g), geraetId: 11);
        Pruefe.Wahr(zweite.Ok && !zweite.NeuesGeraet, "nächster Schritt, bekanntes Gerät");

        // Fenster: ein Schritt zurück oder vor gilt, zwei nicht; nach einem späteren Schritt nie mehr ein früherer. Vorher
        // 16 min warten, damit die drei Fehlversuche oben mit den folgenden keine Pause ergeben (5 in 15 min).
        w.Uhr.Vor(TimeSpan.FromMinutes(16));
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Buch.Bestaetigen(7, w.Code(g, -2), null).Fehler, "zwei Schritte zurück");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Buch.Bestaetigen(7, w.Code(g, 2), null).Fehler, "zwei Schritte voraus");
        Pruefe.Wahr(w.Buch.Bestaetigen(7, w.Code(g, 1), null).Ok, "ein Schritt voraus (Telefon geht vor)");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Buch.Bestaetigen(7, w.Code(g), null).Fehler, "danach gilt der aktuelle Schritt nicht mehr");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Buch.Bestaetigen(7, w.Code(g, -1), null).Fehler, "und der vorige erst recht nicht");
        Pruefe.Gleich(w.Schritt + 1, w.Buch.Eintrag(7)!.LetzterZeitschritt, "zuletzt benutzter Schritt");

        // Drei Nachbarschritte in Folge: Hinweis auf die Uhr des Telefons.
        var hinweis = false;
        for (var i = 0; i < 3; i++)
        {
            w.Weiter();
            w.Weiter();
            w.Weiter();
            var r = w.Buch.Bestaetigen(7, w.Code(g, -1), null);
            Pruefe.Wahr(r.Ok, "Nachbarschritt " + i);
            hinweis = r.UhrHinweis;
        }
        Pruefe.Wahr(hinweis, "Uhr des Telefons prüfen");
        w.Weiter();
        Pruefe.Falsch(w.Buch.Bestaetigen(7, w.Code(g), null).UhrHinweis, "passender Schritt setzt den Hinweis zurück");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.Bestaetigen(7, null, null).Fehler, "weder Code noch Notfallcode");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.Bestaetigen(7, "123456", "ABCD-EFGH-JKMN").Fehler, "beides zugleich");
    }

    [Test]
    public static void Adminbuch_RatenFuehrtZuPauseUndSperre()
    {
        using var w = new Buchwelt();
        var (g, _) = w.Einrichten(7);
        var falsch = FalscherCode(w, g);
        for (var i = 1; i < Adminbuch.FehlversuchePause; i++)
        {
            var r = w.Anmelden(7, falsch);
            Pruefe.Wahr(r.Fehler == Fehlercode.ZweitfaktorFalsch && r.Fehlversuch && !r.Gesperrt, "Fehlversuch " + i);
        }
        var pause = w.Anmelden(7, falsch);
        Pruefe.Gleich(Fehlercode.ZweitfaktorGesperrt, pause.Fehler, "fünfter Fehlversuch");
        Pruefe.Wahr(pause.Gesperrt && pause.Bis == w.Uhr.GetUtcNow() + Adminbuch.PauseFenster, "15 min Pause, Meldung an alle Admins");
        w.Weiter();
        var waehrend = w.Anmelden(7, w.Code(g));
        Pruefe.Wahr(waehrend.Fehler == Fehlercode.ZweitfaktorGesperrt && !waehrend.Fehlversuch, "in der Pause hilft auch der richtige Code nicht, zählt aber nicht");
        Pruefe.Gleich(5, w.Buch.Eintrag(7)!.Fehlversuche.Length, "Pause zählt nicht weiter");

        w.Uhr.Vor(Adminbuch.PauseFenster);
        Pruefe.Wahr(w.Anmelden(7, w.Code(g)).Ok, "nach der Pause geht es");
        // 10 in 24 h sperren bis zur Konsole; ein Erfolg setzt den Zähler nicht zurück.
        w.Weiter();
        for (var i = 6; i < Adminbuch.FehlversucheSperre; i++)
            Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Anmelden(7, FalscherCode(w, g)).Fehler, "Fehlversuch " + i);
        var sperre = w.Anmelden(7, FalscherCode(w, g));
        Pruefe.Wahr(sperre.Fehler == Fehlercode.ZweitfaktorGesperrt && sperre.Gesperrt && sperre.Bis is null, "zehnter: gesperrt bis zur Konsole");
        Pruefe.Gleich(AdminZustand.Gesperrt, w.Buch.Eintrag(7)!.Zustand, "GESPERRT");
        Pruefe.Gleich("FEHLVERSUCHE", w.Buch.Eintrag(7)!.Sperrgrund, "Grund");
        Pruefe.Falsch(w.Buch.IstEingerichtet(7), "keine Admin-Sitzung mehr möglich");
        w.Uhr.Vor(TimeSpan.FromHours(25));
        Pruefe.Gleich(Fehlercode.ZweitfaktorGesperrt, w.Anmelden(7, w.Code(g)).Fehler, "auch nach 24 h: nur die Konsole hebt auf");
        Pruefe.Wahr(w.Buch.Entsperren(7).Ok, "Konsole entsperrt");
        Pruefe.Wahr(w.Buch.IstEingerichtet(7) && w.Buch.Eintrag(7)!.Fehlversuche.IsEmpty, "wieder AKTIV, Fehlversuche fort");
        w.Weiter();
        Pruefe.Wahr(w.Anmelden(7, w.Code(g)).Ok, "Anmeldung nach dem Entsperren");
    }

    /// <summary>Ein Code, der in keinem der drei Schritte des Fensters passt.</summary>
    private static string FalscherCode(Buchwelt w, byte[] g)
    {
        var belegt = new[] { w.Code(g, -1), w.Code(g), w.Code(g, 1) };
        for (var i = 0; ; i++)
        {
            var kandidat = ((123456 + i * 7919) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            if (!belegt.Contains(kandidat)) return kandidat;
        }
    }

    [Test]
    public static void Adminbuch_OhneGueltigeUnterschriftZaehltNichts()
    {
        using var w = new Buchwelt();
        var (g, _) = w.Einrichten(7);
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // Angriffsfall "Aussperren durch Fremde": ohne den Admin-Schlüssel kein Fehlversuch, keine Pause, keine Sperre.
        for (var i = 0; i < 25; i++)
        {
            var r = w.Buch.Anmelden(7, 1, w.N(7, AdminZweck.Anmeldung, fremd), "000000", null);
            Pruefe.Wahr(r.Fehler == Fehlercode.KeinRecht && !r.Fehlversuch, "fremde Unterschrift " + i);
        }
        var andereSitzung = w.N(7, AdminZweck.Anmeldung);
        var umgehaengt = andereSitzung with { SitzungsId = andereSitzung.SitzungsId + 1 };
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.Anmelden(7, 1, umgehaengt, w.Code(g), null).Fehler, "Unterschrift aus einer anderen Sitzung");
        var anderesGeraet = w.N(7, AdminZweck.Anmeldung) with { Geraet = Fp(10) };
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.Anmelden(7, 1, anderesGeraet, w.Code(g), null).Fehler, "Unterschrift von einem anderen Gerät");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.Anmelden(7, 1, new Nachweis(5, Fp(9), null!, null!), w.Code(g), null).Fehler, "ohne Nonce und Unterschrift");
        Pruefe.Wahr(w.Buch.Eintrag(7)!.Fehlversuche.IsEmpty, "kein einziger Fehlversuch gezählt");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Anmelden(8, w.Code(g)).Fehler, "Konto ohne Admin-Nachweis");
        w.Weiter();
        Pruefe.Wahr(w.Anmelden(7, w.Code(g)).Ok, "der echte Admin kommt weiter hinein");
    }

    [Test]
    public static void Adminbuch_NotfallcodesEinmalUndAnDasKontoGebunden()
    {
        using var w = new Buchwelt();
        var (_, codes7) = w.Einrichten(7);
        var (_, codes8) = w.Einrichten(8);
        var r = w.Anmelden(7, null, codes7[0]);
        Pruefe.Wahr(r.Ok && r.NotfallcodeBenutzt && r.NotfallcodesUebrig == 9, "Notfallcode, 9 übrig");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Anmelden(7, null, codes7[0]).Fehler, "derselbe Notfallcode ein zweites Mal");
        var klein = codes7[1].Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant();
        Pruefe.Wahr(w.Anmelden(7, null, klein).Ok, "klein und mit Leerzeichen getippt");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Anmelden(7, null, codes8[0]).Fehler, "Notfallcode eines anderen Admins");
        Pruefe.Wahr(w.Buch.Bestaetigen(7, null, codes7[2]).NotfallcodeBenutzt, "auch als Bestätigung");
        Pruefe.Gleich(7, w.Buch.Eintrag(7)!.Notfallcodes.Length, "drei verbraucht");

        var neu = w.Buch.NotfallcodesErzeugen(7);
        Pruefe.Wahr(neu.Ok && neu.Notfallcodes!.Count == 10, "Konsole: 10 neue");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, w.Anmelden(7, null, codes7[3]).Fehler, "alte gelten nicht mehr");
        Pruefe.Wahr(w.Anmelden(7, null, neu.Notfallcodes![0]).Ok, "neue gelten");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.NotfallcodesErzeugen(9).Fehler, "nur für eingerichtete Admins");
    }

    [Test]
    public static void Adminbuch_FristenUndNeueEinrichtung()
    {
        using var w = new Buchwelt();
        var b = w.Buch;
        b.Vorsehen(7);
        var antrag = b.AntragStellen(7, w.Spki(7), w.N(7, AdminZweck.Antrag));
        b.Ernennen(7, antrag.Kennung!);
        w.Uhr.Vor(Adminbuch.AbholFrist + TimeSpan.FromSeconds(1));
        Pruefe.Gleich(Fehlercode.Frist, b.GeheimnisAbholen(7, w.N(7, AdminZweck.Geheimnis)).Fehler, "nach 10 min nicht mehr abholbar");

        var neu = b.EinrichtungNeu(7, anzeigen: false);
        Pruefe.Wahr(neu.Ok && neu.Geheimnis is null && neu.EinrichtungBegonnen, "Konsole: neues Geheimnis, abholbar");
        Pruefe.Gleich(2u, b.Eintrag(7)!.Generation, "Generation zählt");
        var g = b.GeheimnisAbholen(7, w.N(7, AdminZweck.Geheimnis)).Geheimnis!;
        w.Uhr.Vor(Adminbuch.EinrichtungsFrist + TimeSpan.FromSeconds(1));
        Pruefe.Gleich(Fehlercode.Frist, b.EinrichtungBestaetigen(7, w.N(7, AdminZweck.Einrichtung), w.Code(g)).Fehler, "erster Code nach 10 min zu spät");

        // Mit --anzeigen zeigt die Konsole das Geheimnis selbst; der Client holt es dann nicht mehr ab.
        var konsole = b.EinrichtungNeu(7, anzeigen: true);
        Pruefe.Wahr(konsole.Ok && konsole.Geheimnis is { Length: 20 }, "Konsole zeigt das Geheimnis");
        Pruefe.Gleich(Fehlercode.Ungueltig, b.GeheimnisAbholen(7, w.N(7, AdminZweck.Geheimnis)).Fehler, "schon gezeigt, nicht noch einmal");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, b.EinrichtungBestaetigenKonsole(7, w.Code(g)).Fehler, "Code des alten Geheimnisses");
        var fertig = b.EinrichtungBestaetigenKonsole(7, w.Code(konsole.Geheimnis!));
        Pruefe.Wahr(fertig.Ok && fertig.Notfallcodes!.Count == 10, "an der Konsole eingerichtet");

        // Ein altes Chiffrat lässt sich nach einer neuen Einrichtung nicht zurückspielen (AAD mit Generation).
        var e = b.Eintrag(7)!;
        Pruefe.Wahr(w.Tresor.Entschluesseln(Convert.FromBase64String(e.Geheimnis!), 7, e.Generation - 1) is null, "alte Generation");
        Pruefe.Gleich(Fehlercode.Unbekannt, b.EinrichtungNeu(9, false).Fehler, "Konto ohne Nachweis");
        b.Vorsehen(10);
        Pruefe.Gleich(Fehlercode.Ungueltig, b.EinrichtungNeu(10, false).Fehler, "ohne Admin-Schlüssel keine Einrichtung");
    }

    [Test]
    public static void Adminbuch_KonsoleSperrtEntziehtUndSetztZurueck()
    {
        using var w = new Buchwelt();
        var (g, _) = w.Einrichten(7);
        Pruefe.Wahr(w.Buch.Sperren(7).Ok, "Konsole sperrt");
        Pruefe.Gleich("KONSOLE", w.Buch.Eintrag(7)!.Sperrgrund, "Grund KONSOLE");
        w.Weiter();
        Pruefe.Gleich(Fehlercode.ZweitfaktorGesperrt, w.Anmelden(7, w.Code(g)).Fehler, "gesperrt");
        Pruefe.Wahr(w.Buch.Entsperren(7).Ok && w.Buch.IstEingerichtet(7), "entsperrt");

        // Zweiter Schlüssel (neuer PC): vorsehen erhält den Zustand, der Antrag ergänzt nur.
        using var laptop = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        w.Buch.Vorsehen(7);
        Pruefe.Wahr(w.Buch.IstEingerichtet(7), "vorsehen lässt einen eingerichteten Admin eingerichtet");
        var antrag = w.Buch.AntragStellen(7, laptop.ExportSubjectPublicKeyInfo(), w.N(7, AdminZweck.Antrag, laptop));
        var zweiter = w.Buch.Ernennen(7, antrag.Kennung!);
        Pruefe.Wahr(zweiter.Ok && !zweiter.EinrichtungBegonnen, "zweiter Schlüssel ohne neues Geheimnis");
        w.Weiter();
        Pruefe.Wahr(w.Buch.Anmelden(7, 2, w.N(7, AdminZweck.Anmeldung, laptop), w.Code(g), null).Ok, "Anmeldung mit dem zweiten Schlüssel");

        // Derselbe Schlüssel gehört nie zwei Konten.
        w.Buch.Vorsehen(8);
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.AntragStellen(8, laptop.ExportSubjectPublicKeyInfo(), w.N(8, AdminZweck.Antrag, laptop)).Fehler, "Schlüssel eines anderen Admins");

        Pruefe.Wahr(w.Buch.SchluesselEntfernen(7, antrag.Kennung!).Ok, "Laptop verloren: Schlüssel entfernt");
        w.Weiter();
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.Anmelden(7, 2, w.N(7, AdminZweck.Anmeldung, laptop), w.Code(g), null).Fehler, "entfernter Schlüssel gilt nicht mehr");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.SchluesselEntfernen(7, antrag.Kennung!).Fehler, "nicht zweimal");

        Pruefe.Wahr(w.Buch.Zuruecksetzen(7).Ok, "zurückgesetzt");
        var e = w.Buch.Eintrag(7)!;
        Pruefe.Wahr(e.Zustand == AdminZustand.Vorgesehen && e.Schluessel.IsEmpty && e.Geheimnis is null && e.Notfallcodes.IsEmpty && e.AntragErlaubt,
            "Schlüssel, Geheimnis und Notfallcodes fort, wieder vorgesehen");
        Pruefe.Wahr(w.Buch.Entziehen(7).Ok && w.Buch.Eintrag(7) is null, "entzogen");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.Sperren(7).Fehler, "ohne Eintrag nichts zu sperren");
    }

    [Test]
    public static void Adminbuch_HoechstensDreiSchluessel()
    {
        using var w = new Buchwelt();
        w.Einrichten(7);
        var schluessel = new List<ECDsa>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var s = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                schluessel.Add(s);
                w.Buch.Vorsehen(7);
                var antrag = w.Buch.AntragStellen(7, s.ExportSubjectPublicKeyInfo(), w.N(7, AdminZweck.Antrag, s));
                var r = w.Buch.Ernennen(7, antrag.Kennung!);
                Pruefe.Gleich(i < 2 ? Fehlercode.Keiner : Fehlercode.Grenze, r.Fehler, "Schlüssel " + (i + 2));
            }
        }
        finally
        {
            foreach (var s in schluessel) s.Dispose();
        }
        Pruefe.Gleich(Adminbuch.MaxSchluessel, w.Buch.Eintrag(7)!.Schluessel.Length, "drei");
    }

    [Test]
    public static void Adminbuch_TresorDefektIstKeinRateversuch()
    {
        using var w = new Buchwelt();
        var (g, _) = w.Einrichten(7);
        using var andererTresor = TotpTresor.Zufaellig();
        // Der Schlüssel funk-totp-schluessel wurde getauscht: Betriebsfehler, kein Fehlversuch, keine Sperre.
        var nachTausch = new Adminbuch(w.Uhr, andererTresor, w.Buch.Stand);
        w.Weiter();
        for (var i = 0; i < 12; i++)
        {
            var r = nachTausch.Anmelden(7, 1, w.N(7, AdminZweck.Anmeldung), w.Code(g), null);
            Pruefe.Wahr(r.TresorDefekt && !r.Fehlversuch, "Tresor defekt " + i);
        }
        Pruefe.Wahr(nachTausch.Eintrag(7)!.Fehlversuche.IsEmpty && nachTausch.IstEingerichtet(7), "nichts gezählt, nichts gesperrt");
    }

    // ---------------------------------------------------------------- Draht: Geheimnisse nie im Text, Regeln der Felder

    [Test]
    public static void Draht_GeheimnisseNieImText()
    {
        var anmeldung = new S.AdminAnmeldungAnfrage { Unterschrift = Convert.ToBase64String(new byte[64]), Code = "493817" };
        Pruefe.Falsch(anmeldung.ToString().Contains("493817", StringComparison.Ordinal), "Code nicht im Text: " + anmeldung);
        var notfall = new S.AdminBestaetigenAnfrage { Notfallcode = "K7M4-Q9TX-2HWD" };
        Pruefe.Falsch(notfall.ToString().Contains("K7M4", StringComparison.Ordinal), "Notfallcode nicht im Text");
        var einrichtung = new S.AdminEinrichtungAnfrage { Op = S.AdminEinrichtungOperation.Bestaetigen, Unterschrift = "x", Code = "493817" };
        Pruefe.Falsch(einrichtung.ToString().Contains("493817", StringComparison.Ordinal), "erster Code nicht im Text");
        var antwort = new S.AdminEinrichtungAntwort { Geheimnis = "GEZDGNBVGY3TQOJQ", Uri = "otpauth://totp/x?secret=GEZDGNBVGY3TQOJQ", Notfallcodes = ["K7M4-Q9TX-2HWD"] };
        var text = antwort.ToString();
        Pruefe.Falsch(text.Contains("GEZD", StringComparison.Ordinal) || text.Contains("K7M4", StringComparison.Ordinal), "Antwort verrät nichts: " + text);
    }

    [Test]
    public static void Draht_FelderDerAdminNachrichten()
    {
        var sig = Convert.ToBase64String(new byte[64]);
        Pruefe.Gleich(S.EingangsStatus.Annehmen, Eingang(S.Nachrichtentyp.AdminAnmeldung, new S.AdminAnmeldungAnfrage { Unterschrift = sig, Code = "123456" }).Status, "gültig");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, Eingang(S.Nachrichtentyp.AdminAnmeldung, new S.AdminAnmeldungAnfrage { Unterschrift = sig }).Fehler, "weder Code noch Notfallcode");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, Eingang(S.Nachrichtentyp.AdminAnmeldung, new S.AdminAnmeldungAnfrage { Unterschrift = sig, Code = "1", Notfallcode = "2" }).Fehler, "beides");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, Eingang(S.Nachrichtentyp.AdminAnmeldung, new S.AdminAnmeldungAnfrage { Unterschrift = Convert.ToBase64String(new byte[70]), Code = "123456" }).Fehler, "Unterschrift nicht 64 Byte");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, Eingang(S.Nachrichtentyp.AdminEinrichtung, new S.AdminEinrichtungAnfrage { Op = S.AdminEinrichtungOperation.Antrag, Unterschrift = sig }).Fehler, "ANTRAG ohne Schlüssel");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, Eingang(S.Nachrichtentyp.AdminEinrichtung, new S.AdminEinrichtungAnfrage { Op = S.AdminEinrichtungOperation.Bestaetigen, Unterschrift = sig }).Fehler, "BESTAETIGEN ohne Code");
        using var s = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Pruefe.Gleich(S.EingangsStatus.Annehmen, Eingang(S.Nachrichtentyp.AdminEinrichtung, new S.AdminEinrichtungAnfrage
        {
            Op = S.AdminEinrichtungOperation.Antrag,
            Unterschrift = sig,
            Schluessel = Convert.ToBase64String(s.ExportSubjectPublicKeyInfo()),
        }).Status, "ANTRAG mit SPKI (91 Byte)");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, Eingang(S.Nachrichtentyp.AdminBestaetigen, new S.AdminBestaetigenAnfrage()).Fehler, "Bestätigen ohne alles");
        // Registrierung: SC-Name und Discord-Name nur zusammen (Antrag ohne Code) oder gar nicht (Code-Weg).
        var reg = SteuerHilfe.ServerIn(S.ServerZustand.Registrierung);
        Pruefe.Gleich(S.Fehlercode.Ungueltig, SteuerHilfe.AnServer(reg, SteuerHilfe.Json(S.Nachrichtentyp.Registrierung,
            new S.RegistrierungAnfrage { Geraetename = "PC", ScName = "Sarevoc" }, 1)).Fehler, "nur SC-Name");
        var reg2 = SteuerHilfe.ServerIn(S.ServerZustand.Registrierung);
        Pruefe.Gleich(S.EingangsStatus.Annehmen, SteuerHilfe.AnServer(reg2, SteuerHilfe.Json(S.Nachrichtentyp.Registrierung,
            new S.RegistrierungAnfrage { Geraetename = "PC", ScName = "Sarevoc", DiscordName = "sarevoc" }, 1)).Status, "beide Namen");
    }

    private static S.Eingang Eingang<T>(S.Nachrichtentyp typ, T body)
    {
        var k = SteuerHilfe.ServerIn(S.ServerZustand.Aktiv);
        return SteuerHilfe.AnServer(k, SteuerHilfe.Json(typ, body, 5));
    }
}
