// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Simulierte Uhr für die Identitätstests; springt nur, wenn der Test sie vorstellt. Wanduhr und monotone Uhr
/// laufen mit <see cref="Vor"/> gemeinsam; <see cref="WanduhrSpringt"/> verstellt nur die Wanduhr (NTP, Handeingriff).
/// </summary>
internal sealed class IdentitaetsUhr(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _jetzt = start;
    private long _ticks;

    public override DateTimeOffset GetUtcNow() => _jetzt;

    public override long GetTimestamp() => _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Vor(TimeSpan dauer)
    {
        _jetzt += dauer;
        _ticks += dauer.Ticks;
    }

    public void WanduhrSpringt(TimeSpan sprung) => _jetzt += sprung;
}

/// <summary>Prüfwerte 8.5 (SPKI), 8.6 (Einmalcode), 8.11 (Profilsignatur) und P-11, P-14 ohne Kontenbuch.</summary>
internal static class IdentitaetTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    // 8.5: Q = G der Kurve P-256, privater Schlüssel 1, nur für Tests.
    private const string GX = "6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296";
    private const string GY = "4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5";
    private const string Spki85 = "3059301306072a8648ce3d020106082a8648ce3d03010703420004" + GX + GY;
    private const string Sha85 = "5cd252fb0ce8932436faf8ccd1040981b89ee4ad6b9fe9e2a2b7e71aacb27cd3";
    private const string Base6485 = "XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=";
    private const string Anzeige85 = "5CD2-52FB-0CE8-9324";

    // 8.11
    private const string Daten811 =
        "{\"name\":\"Test\",\"profilVersion\":2,\"ausgestellt\":1790000000000,\"gueltigBis\":1805000000000,"
        + "\"hosts\":[{\"name\":\"funk.example\",\"familien\":[\"IPv4\"]}],\"tcpPort\":443,"
        + "\"pins\":[\"XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=\",\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"]}";
    private const string Praefix811 = "74616372656c61792070726f66696c20763100";
    private const string Hash811 = "1988cee397416db14408d00f19b55bf6bc27cca89aac8b949ee66ce1031d8026";
    private const string Signatur811 =
        "8ae61a79115c7b38f827d9f107aa84d31eb8d677ab3118fa7f20b6a84d48adfe"
        + "41af3313051f3c4137391f314f0a5c80325bb4a8afb3768ceb349f700404206b";
    private const string Signatur811Base64 = "iuYaeRFcezj4J9nxB6qE0x641nerMRj6fyC2qE1Irf5BrzMTBR88QTc5HzFPClyAMlu0qK+zdozrNJ9wBAQgaw==";

    // 8.6
    private const string Hash86 = "d30125c87bc69a4ec95a4fcbf0d928c1df3f6b53e119a8cd057518efc9635790";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static ECParameters TestParameter(bool mitPrivat) => new()
    {
        Curve = ECCurve.NamedCurves.nistP256,
        D = mitPrivat ? Hex("0000000000000000000000000000000000000000000000000000000000000001") : null,
        Q = new ECPoint { X = Hex(GX), Y = Hex(GY) },
    };

    private static Profilschluessel TestProfilschluessel() => Profilschluessel.AusSpki(Hex(Spki85));

    private static SigniertesProfil Profil811() => new(Encoding.ASCII.GetBytes(Daten811), Hex(Signatur811));

    // ---------------------------------------------------------------- 8.5 SPKI-Fingerabdruck

    [Test]
    public static void Spki85_FingerabdruckBase64Anzeige()
    {
        var fp = Fingerabdruck.AusSpki(Hex(Spki85));
        Pruefe.BytesGleich(Hex(Sha85), fp.AlsBytes(), "SHA-256 über das SPKI");
        Pruefe.Gleich(Base6485, fp.AlsBase64(), "Base64 (Pin)");
        Pruefe.Gleich(Anzeige85, fp.Anzeige, "Anzeigeform");
        Pruefe.Gleich(Anzeige85, fp.ToString(), "ToString ist die Anzeigeform");
        Pruefe.Gleich(91, Hex(Spki85).Length, "SPKI-Länge");
        Pruefe.Wahr(P256Spki.IstGueltig(Hex(Spki85)), "kanonisches P-256 mit Punkt auf der Kurve");
    }

    [Test]
    public static void Spki85_ExportDurchDotnetGleichVektor()
    {
        using var k = ECDsa.Create(TestParameter(mitPrivat: false));
        Pruefe.BytesGleich(Hex(Spki85), k.ExportSubjectPublicKeyInfo(), "ExportSubjectPublicKeyInfo mit Q = G");
        Pruefe.BytesGleich(Hex(Sha85), Fingerabdruck.AusSchluessel(k).AlsBytes(), "Fingerabdruck aus dem Schlüssel");

        using var privat = ECDsa.Create(TestParameter(mitPrivat: true));
        Pruefe.BytesGleich(Hex(Spki85), privat.ExportSubjectPublicKeyInfo(), "privater Schlüssel 1 ergibt Q = G");
    }

    [Test]
    public static void Spki85_UeberGeraetezertifikatUndPin()
    {
        using var k = ECDsa.Create(TestParameter(mitPrivat: true));
        using var zert = Geraetezertifikat.Erzeugen(k, new IdentitaetsUhr(Start));
        Pruefe.Wahr(Geraetezertifikat.TryAnnehmen(zert, out var fp), "Server nimmt P-256-Zertifikat an");
        Pruefe.Gleich(Anzeige85, fp.Anzeige, "Geräte-Identität aus dem Zertifikat");
        Pruefe.Gleich(Base6485, Fingerabdruck.AusZertifikat(zert).AlsBase64(), "Pin aus dem Blattzertifikat");

        Pruefe.Wahr(Serverprofil.TryLesen(Encoding.ASCII.GetBytes(Daten811), out var profil), "Profil 8.11 lesbar");
        Pruefe.Wahr(profil!.ZertifikatPasst(zert), "richtiger Pin");
        Pruefe.Falsch(profil.ZertifikatPasst(null), "kein Zertifikat");
    }

    [Test]
    public static void Spki_Base64StrengUndKanonisch()
    {
        Pruefe.Wahr(Fingerabdruck.TryAusBase64(Base6485, out var fp), "Vektor");
        Pruefe.Gleich(Anzeige85, fp.Anzeige, "Vektor gelesen");
        Pruefe.Falsch(Fingerabdruck.TryAusBase64("XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNN=", out _), "nicht kanonisch (unbenutzte Bits)");
        Pruefe.Falsch(Fingerabdruck.TryAusBase64("XNJS wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=", out _), "Leerzeichen");
        Pruefe.Falsch(Fingerabdruck.TryAusBase64("XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM", out _), "ohne Auffüllung");
        Pruefe.Falsch(Fingerabdruck.TryAusBase64("XNJS-wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=", out _), "URL-Alphabet");
        Pruefe.Falsch(Fingerabdruck.TryAusBase64("AAAA", out _), "zu kurz");
    }

    [Test]
    public static void Spki_AnzeigePasstFuerAdminWerkzeug()
    {
        var fp = Fingerabdruck.AusSpki(Hex(Spki85));
        Pruefe.Wahr(fp.AnzeigePasst("5CD2-52FB-0CE8-9324"), "genau");
        Pruefe.Wahr(fp.AnzeigePasst("5cd252fb0ce89324"), "klein ohne Bindestriche");
        Pruefe.Wahr(fp.AnzeigePasst(" 5CD2 52FB 0CE8 9324 "), "mit Leerzeichen");
        Pruefe.Falsch(fp.AnzeigePasst("5CD2-52FB-0CE8-932"), "zu kurz");
        Pruefe.Falsch(fp.AnzeigePasst("5CD2-52FB-0CE8-93245"), "zu lang");
        Pruefe.Falsch(fp.AnzeigePasst("5CD2-52FB-0CE8-9325"), "falsch");
    }

    [Test]
    public static void Spki_NurKanonischesP256()
    {
        using var rsa = RSA.Create(2048);
        Pruefe.Falsch(P256Spki.IstKanonisch(rsa.ExportSubjectPublicKeyInfo()), "RSA");
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Pruefe.Falsch(P256Spki.IstKanonisch(p384.ExportSubjectPublicKeyInfo()), "P-384");
        var neben = Hex(Spki85);
        neben[^1] ^= 0x01;
        Pruefe.Wahr(P256Spki.IstKanonisch(neben), "Form stimmt noch");
        Pruefe.Falsch(P256Spki.IstGueltig(neben), "Punkt nicht auf der Kurve");
        Pruefe.Wirft<ArgumentException>(() => new Geraeteschluessel(ECDsa.Create(ECCurve.NamedCurves.nistP384), SchluesselArt.Software).Dispose(), "P-384 als Geräteschlüssel");
    }

    [Test]
    public static void Geraetezertifikat_ServerAnnahmeNurP256()
    {
        Pruefe.Falsch(Geraetezertifikat.TryAnnehmen(null, out _), "ohne Zertifikat");
        using var rsa = RSA.Create(2048);
        var rsaAnfrage = new CertificateRequest("CN=Funk-Geraet", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var rsaZert = rsaAnfrage.CreateSelfSigned(Start.AddHours(-1), Start.AddYears(1));
        Pruefe.Falsch(Geraetezertifikat.TryAnnehmen(rsaZert, out _), "RSA-Zertifikat");
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var p384Zert = new CertificateRequest("CN=Funk-Geraet", p384, HashAlgorithmName.SHA256).CreateSelfSigned(Start, Start.AddYears(1));
        Pruefe.Falsch(Geraetezertifikat.TryAnnehmen(p384Zert, out _), "P-384-Zertifikat");

        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var abgelaufen = new CertificateRequest("CN=egal", p256, HashAlgorithmName.SHA256)
            .CreateSelfSigned(new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2002, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Pruefe.Wahr(Geraetezertifikat.TryAnnehmen(abgelaufen, out var fp), "Gültigkeit und Subject zählen nicht");
        Pruefe.Gleich(Fingerabdruck.AusSchluessel(p256), fp, "Fingerabdruck des Schlüssels");
    }

    // ---------------------------------------------------------------- 3.2 Gerätezertifikat und Schlüssel

    [Test]
    public static void Geraetezertifikat_FelderNach32()
    {
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var z = Geraetezertifikat.Erzeugen(k, new IdentitaetsUhr(Start));
        Pruefe.Gleich("CN=Funk-Geraet", z.Subject, "Subject");
        Pruefe.Gleich(z.Subject, z.Issuer, "selbstsigniert");
        Pruefe.Wahr(z.HasPrivateKey, "mit privatem Schlüssel");
        Pruefe.Gleich(Start.AddHours(-1).UtcDateTime, z.NotBefore.ToUniversalTime(), "NotBefore = jetzt - 1 h");
        Pruefe.Gleich(Start.AddYears(20).UtcDateTime, z.NotAfter.ToUniversalTime(), "NotAfter = jetzt + 20 Jahre");
        Pruefe.Gleich("1.2.840.10045.4.3.2", z.SignatureAlgorithm.Value, "ECDSA mit SHA-256");
        var serie = z.SerialNumberBytes.Span;
        Pruefe.Wahr(serie.Length is >= 1 and <= 17 && (serie.Length < 17 || serie[0] == 0), "16 Zufallsbytes, vorzeichenlos kodiert");

        var ku = z.Extensions.OfType<X509KeyUsageExtension>().Single();
        Pruefe.Wahr(ku.Critical, "KeyUsage kritisch");
        Pruefe.Gleich(X509KeyUsageFlags.DigitalSignature, ku.KeyUsages, "nur digitalSignature");
        var eku = z.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        Pruefe.Gleich("1.3.6.1.5.5.7.3.2", eku.EnhancedKeyUsages.Cast<Oid>().Single().Value, "EKU clientAuth");
        Pruefe.Wahr(Geraetezertifikat.TryAnnehmen(z, out var fp), "Server nimmt es an");
        Pruefe.Gleich(Fingerabdruck.AusSchluessel(k), fp, "Identität ist der Schlüssel");

        using var zweites = Geraetezertifikat.Erzeugen(k, new IdentitaetsUhr(Start));
        Pruefe.Falsch(z.SerialNumberBytes.Span.SequenceEqual(zweites.SerialNumberBytes.Span), "Seriennummer zufällig");
        Pruefe.Wirft<ArgumentException>(() => Geraetezertifikat.Erzeugen(ECDsa.Create(ECCurve.NamedCurves.nistP384), new IdentitaetsUhr(Start)), "nur P-256");
    }

    [Test]
    public static void Geraeteschluessel_FluechtigerSpeicher()
    {
        var speicher = new FluechtigerSchluesselspeicher();
        Pruefe.Wahr(speicher.Laden() is null, "anfangs leer");
        using var erster = speicher.Erzeugen();
        Pruefe.Gleich(SchluesselArt.Software, erster.Art, "meldet SOFTWARE");
        using (var geladen = speicher.Laden())
            Pruefe.Gleich(erster.Fingerabdruck, geladen!.Fingerabdruck, "Laden liefert denselben Schlüssel");
        using var zweiter = speicher.Erzeugen();
        Pruefe.Falsch(erster.Fingerabdruck == zweiter.Fingerabdruck, "Erzeugen ersetzt den Schlüssel (Weg nach GERAET_GESPERRT)");
        speicher.Loeschen();
        Pruefe.Wahr(speicher.Laden() is null, "nach Löschen leer");
    }

    [Test]
    public static void Geraeteschluessel_RueckfallTpmAufSoftware()
    {
        var (a, artA) = Schluesselwahl.MitRueckfall(true, () => throw new CryptographicException("kein TPM"), () => ECDsa.Create(ECCurve.NamedCurves.nistP256));
        using (a) Pruefe.Gleich(SchluesselArt.Software, artA, "TPM scheitert: SOFTWARE");
        var (b, artB) = Schluesselwahl.MitRueckfall(true, () => throw new PlatformNotSupportedException(), () => ECDsa.Create(ECCurve.NamedCurves.nistP256));
        using (b) Pruefe.Gleich(SchluesselArt.Software, artB, "ohne CNG: SOFTWARE");
        var (c, artC) = Schluesselwahl.MitRueckfall(true, () => ECDsa.Create(ECCurve.NamedCurves.nistP256), () => throw new InvalidOperationException("nicht erreicht"));
        using (c) Pruefe.Gleich(SchluesselArt.Tpm, artC, "TPM gelingt: TPM");
        var tpmAufgerufen = false;
        var (d, artD) = Schluesselwahl.MitRueckfall(false, () => { tpmAufgerufen = true; return ECDsa.Create(); }, () => ECDsa.Create(ECCurve.NamedCurves.nistP256));
        using (d) Pruefe.Gleich(SchluesselArt.Software, artD, "TPM abgeschaltet");
        Pruefe.Falsch(tpmAufgerufen, "TPM nicht versucht");
    }

    [Test]
    public static void Geraeteschluessel_SoftwareKspFluechtigNichtExportierbar()
    {
        if (!OperatingSystem.IsWindows()) return; // CNG gibt es nur unter Windows; der Server braucht es nicht
        // name = null: flüchtiger Schlüssel, nichts landet im Benutzerprofil.
        using var k = CngSchluesselspeicher.Erzeugen(CngProvider.MicrosoftSoftwareKeyStorageProvider, null);
        Pruefe.Wahr(k.Key.IsEphemeral, "flüchtig");
        Pruefe.Gleich(CngExportPolicies.None, k.Key.ExportPolicy, "ExportPolicy = None");
        Pruefe.Wirft<CryptographicException>(() => k.ExportParameters(includePrivateParameters: true), "privater Teil nicht exportierbar");
        Pruefe.Wahr(P256Spki.IstGueltig(k.ExportSubjectPublicKeyInfo()), "öffentlicher Teil als P-256-SPKI");
        using var z = Geraetezertifikat.ErzeugenOhnePrivatschluessel(k, new IdentitaetsUhr(Start));
        Pruefe.Wahr(Geraetezertifikat.TryAnnehmen(z, out var fp), "Zertifikat mit KSP-Schlüssel signiert");
        Pruefe.Gleich(Fingerabdruck.AusSchluessel(k), fp, "Identität");
    }

    // ---------------------------------------------------------------- 8.11 Profilsignatur

    [Test]
    public static void Profil811_PraefixDatenHash()
    {
        Pruefe.BytesGleich(Hex(Praefix811), SigniertesProfil.Praefix, "Präfix");
        Pruefe.Gleich(19, SigniertesProfil.Praefix.Length, "Präfix 19 Byte");
        Pruefe.Gleich(259, Encoding.ASCII.GetByteCount(Daten811), "daten 259 Byte");
        Span<byte> hash = stackalloc byte[32];
        SigniertesProfil.HashBilden(Encoding.ASCII.GetBytes(Daten811), hash);
        Pruefe.BytesGleich(Hex(Hash811), hash, "SHA-256(Präfix || daten)");
        Pruefe.BytesGleich(Hex(Signatur811), Convert.FromBase64String(Signatur811Base64), "Signatur hex = Base64");
    }

    [Test]
    public static void Profil811_SignaturGiltUndScheitertBeiAenderung()
    {
        using var schluessel = TestProfilschluessel();
        Pruefe.Gleich(Anzeige85, schluessel.Fingerabdruck.Anzeige, "Profilschlüssel ist der Testschlüssel aus 8.5");
        Pruefe.Wahr(schluessel.Prueft(Profil811()), "Signatur gilt");

        var daten = Encoding.ASCII.GetBytes(Daten811);
        daten[10] ^= 0x01;
        Pruefe.Falsch(schluessel.Prueft(new SigniertesProfil(daten, Hex(Signatur811))), "gekipptes Bit in daten");

        var signatur = Hex(Signatur811);
        signatur[40] ^= 0x01;
        Pruefe.Falsch(schluessel.Prueft(new SigniertesProfil(Encoding.ASCII.GetBytes(Daten811), signatur)), "gekipptes Bit in der Signatur");

        using var k = ECDsa.Create(TestParameter(mitPrivat: false));
        Pruefe.Falsch(k.VerifyData(Encoding.ASCII.GetBytes(Daten811), Hex(Signatur811), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation), "ohne Präfix");
    }

    [Test]
    public static void Profil811_InhaltGelesenUndKanonischGeschrieben()
    {
        Pruefe.Wahr(Serverprofil.TryLesen(Encoding.ASCII.GetBytes(Daten811), out var p), "lesbar");
        Pruefe.Gleich("Test", p!.Name, "name");
        Pruefe.Gleich(2u, p.ProfilVersion, "profilVersion");
        Pruefe.Gleich(1790000000000L, p.Ausgestellt, "ausgestellt");
        Pruefe.Gleich(1805000000000L, p.GueltigBis, "gueltigBis");
        Pruefe.Gleich(1, p.Hosts.Count, "ein Host");
        Pruefe.Gleich(new ProfilHost("funk.example", true, false), p.Hosts[0], "Host und Familie");
        Pruefe.Gleich((ushort)443, p.TcpPort, "tcpPort");
        Pruefe.Gleich(2, p.Pins.Count, "zwei Pins");
        Pruefe.Gleich(Base6485, p.Pins[0].AlsBase64(), "aktueller Pin");
        Pruefe.Gleich("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", p.Pins[1].AlsBase64(), "Reserve-Pin");
        Pruefe.Gleich(0, p.Nameserver.Count, "keine Nameserver");
        Pruefe.BytesGleich(Encoding.ASCII.GetBytes(Daten811), p.AlsJson(), "Schreiber erzeugt genau die 259 Byte");
    }

    [Test]
    public static void Profil811_VersionsregelUndAblauf()
    {
        using var schluessel = TestProfilschluessel();
        Profilschluessel[] eingebaut = [schluessel];
        var vorAblauf = DateTimeOffset.FromUnixTimeMilliseconds(1800000000000);
        Pruefe.Gleich(ProfilUrteil.Uebernehmen, ProfilAnnahme.Pruefen(Profil811(), eingebaut, null, vorAblauf).Urteil, "noch kein Profil");
        Pruefe.Gleich(ProfilUrteil.Uebernehmen, ProfilAnnahme.Pruefen(Profil811(), eingebaut, 1, vorAblauf).Urteil, "gespeichert 1 < 2");
        Pruefe.Gleich(ProfilUrteil.NichtNeuer, ProfilAnnahme.Pruefen(Profil811(), eingebaut, 2, vorAblauf).Urteil, "gleich: still übergehen");
        Pruefe.Gleich(ProfilUrteil.NichtNeuer, ProfilAnnahme.Pruefen(Profil811(), eingebaut, 3, vorAblauf).Urteil, "älter: still übergehen");
        Pruefe.Falsch(ProfilAnnahme.Pruefen(Profil811(), eingebaut, null, vorAblauf).Abgelaufen, "vor gueltigBis");
        var danach = ProfilAnnahme.Pruefen(Profil811(), eingebaut, null, DateTimeOffset.FromUnixTimeMilliseconds(1805000000001));
        Pruefe.Gleich(ProfilUrteil.Uebernehmen, danach.Urteil, "nach gueltigBis weiter in Gebrauch");
        Pruefe.Wahr(danach.Abgelaufen, "mit Warnung");
    }

    [Test]
    public static void Profil_P14_PinsAktuellReserveFalsch()
    {
        Pruefe.Wahr(Serverprofil.TryLesen(Encoding.ASCII.GetBytes(Daten811), out var p), "lesbar");
        Pruefe.Wahr(p!.PinPasst(Fingerabdruck.AusSpki(Hex(Spki85))), "richtiger Pin");
        Pruefe.Wahr(p.PinPasst(new Fingerabdruck(new byte[32])), "Reserve-Pin");
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Pruefe.Falsch(p.PinPasst(Fingerabdruck.AusSchluessel(fremd)), "falscher Pin");
    }

    [Test]
    public static void Profil_P14_UnsigniertOderFremdWirdVerworfen()
    {
        using var schluessel = TestProfilschluessel();
        Profilschluessel[] eingebaut = [schluessel];
        var jetzt = DateTimeOffset.FromUnixTimeMilliseconds(1800000000000);

        var neuerHost = Daten811.Replace("funk.example", "boese.example", StringComparison.Ordinal);
        var mitHost = new SigniertesProfil(Encoding.ASCII.GetBytes(neuerHost), Hex(Signatur811));
        Pruefe.Gleich(ProfilUrteil.SignaturUngueltig, ProfilAnnahme.Pruefen(mitHost, eingebaut, null, jetzt).Urteil, "neuer Host ohne Signatur");

        var neuerPin = Daten811.Replace("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBA=", StringComparison.Ordinal);
        var mitPin = new SigniertesProfil(Encoding.ASCII.GetBytes(neuerPin), Hex(Signatur811));
        Pruefe.Gleich(ProfilUrteil.SignaturUngueltig, ProfilAnnahme.Pruefen(mitPin, eingebaut, null, jetzt).Urteil, "neuer Pin ohne Signatur");

        using var angreifer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fremdSigniert = SigniertesProfil.Signieren(Encoding.ASCII.GetBytes(neuerPin), angreifer);
        Pruefe.Gleich(ProfilUrteil.SignaturUngueltig, ProfilAnnahme.Pruefen(fremdSigniert, eingebaut, null, jetzt).Urteil, "Signatur mit fremdem Schlüssel");

        var ohneSignatur = Encoding.UTF8.GetBytes("{\"daten\":\"" + Convert.ToBase64String(Encoding.ASCII.GetBytes(Daten811)) + "\"}");
        Pruefe.Falsch(SigniertesProfil.TryLesen(ohneSignatur, out _), "Objekt ohne Signatur");
        Pruefe.Gleich(ProfilUrteil.SignaturUngueltig, ProfilAnnahme.Pruefen(Profil811(), [], null, jetzt).Urteil, "kein eingebauter Schlüssel");
    }

    [Test]
    public static void Profil_P14_ReserveProfilschluessel()
    {
        using var aktuell = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var reserve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var a = Profilschluessel.AusSpki(aktuell.ExportSubjectPublicKeyInfo());
        using var r = Profilschluessel.AusSpki(reserve.ExportSubjectPublicKeyInfo());
        var daten = Encoding.ASCII.GetBytes(Daten811.Replace("\"profilVersion\":2", "\"profilVersion\":3", StringComparison.Ordinal));
        var signiert = SigniertesProfil.Signieren(daten, reserve);
        var urteil = ProfilAnnahme.Pruefen(signiert, [a, r], 2, DateTimeOffset.FromUnixTimeMilliseconds(1800000000000));
        Pruefe.Gleich(ProfilUrteil.Uebernehmen, urteil.Urteil, "mit dem Reserve-Profilschlüssel signiert");
        Pruefe.Gleich(3u, urteil.Profil!.ProfilVersion, "neue Version");

        var kaputt = SigniertesProfil.Signieren("{\"name\":\"x\"}"u8, aktuell);
        Pruefe.Gleich(ProfilUrteil.InhaltUngueltig, ProfilAnnahme.Pruefen(kaputt, [a, r], null, DateTimeOffset.UnixEpoch).Urteil, "signiert, aber Schema verletzt");
    }

    [Test]
    public static void Profil_UmschlagJsonHinUndZurueck()
    {
        var umschlag = Encoding.UTF8.GetBytes("{\"daten\":\"" + Convert.ToBase64String(Encoding.ASCII.GetBytes(Daten811))
            + "\",\"signatur\":\"" + Signatur811Base64 + "\"}");
        Pruefe.Wahr(SigniertesProfil.TryLesen(umschlag, out var s), "Umschlag lesbar");
        Pruefe.BytesGleich(Encoding.ASCII.GetBytes(Daten811), s!.Daten, "daten unverändert");
        Pruefe.BytesGleich(Hex(Signatur811), s.Signatur, "signatur");
        Pruefe.BytesGleich(umschlag, s.AlsJson(), "Schreiber erzeugt denselben Umschlag");
        using var schluessel = TestProfilschluessel();
        Pruefe.Wahr(schluessel.Prueft(s), "Signatur gilt nach dem Lesen");

        Pruefe.Falsch(SigniertesProfil.TryLesen("{\"daten\":\"AA==\",\"signatur\":\"AA==\"}"u8, out _), "Signatur zu kurz");
        Pruefe.Falsch(SigniertesProfil.TryLesen("{\"daten\":\"AA==\",\"daten\":\"AA==\",\"signatur\":\"AA==\"}"u8, out _), "doppelter Schlüssel");
        Pruefe.Falsch(SigniertesProfil.TryLesen("[]"u8, out _), "kein Objekt");
    }

    [Test]
    public static void Profil_StrengesLesen()
    {
        static bool Liest(string json) => Serverprofil.TryLesen(Encoding.UTF8.GetBytes(json), out _);
        static string Mit(string alt, string neu) => Daten811.Replace(alt, neu, StringComparison.Ordinal);
        static string Vorne(string feld) => "{" + feld + "," + Daten811[1..];

        Pruefe.Wahr(Liest(Daten811), "Grundfall");
        Pruefe.Wahr(Liest(Vorne("\"neu\":{\"a\":[1,true,null,\"x\"]}")), "unbekanntes Feld übergangen");
        Pruefe.Wahr(Liest(Mit("\"tcpPort\":443", "\"tcpPort\":443,\"nameserver\":null")), "null wie fehlend");
        Pruefe.Wahr(Liest(Mit("\"tcpPort\":443", "\"tcpPort\":443,\"nameserver\":[\"ns1.example\",\"ns2.example\"]")), "Nameserver");
        Pruefe.Wahr(Liest(Mit("[\"IPv4\"]", "[\"IPv6\",\"IPv4\"]")), "beide Familien");
        Pruefe.Wahr(Liest(Mit("\"Test\"", "\"\\u0054est\"")), "maskierte Zeichen");
        // Das Wurzelobjekt zählt als Ebene 1, jede weitere Klammer als eine Ebene mehr.
        Pruefe.Wahr(Liest(Vorne("\"t\":[[[[[[[1]]]]]]]")), "Tiefe 8");

        Pruefe.Falsch(Liest(Vorne("\"t\":[[[[[[[[1]]]]]]]]")), "Tiefe 9");
        Pruefe.Falsch(Liest(Mit("{\"name\":\"Test\"", "{\"name\":\"Test\",\"name\":\"Test\"")), "doppelter Schlüssel");
        Pruefe.Falsch(Liest(Vorne("\"neu\":{\"a\":1,\"a\":2}")), "doppelter Schlüssel in der Tiefe");
        Pruefe.Falsch(Liest(Mit("\"tcpPort\":443", "\"tcpPort\":443,")), "abschließendes Komma");
        Pruefe.Falsch(Liest("{/*x*/" + Daten811[1..]), "Kommentar");
        Pruefe.Falsch(Liest(Daten811 + " {}"), "etwas nach dem Objekt");
        Pruefe.Falsch(Liest(Mit("\"name\":\"Test\",", "\"name\":null,")), "Pflichtfeld null");
        Pruefe.Falsch(Liest(Mit("\"tcpPort\":443", "\"tcpPort\":0")), "Port 0");
        Pruefe.Falsch(Liest(Mit("\"tcpPort\":443", "\"tcpPort\":65536")), "Port zu groß");
        Pruefe.Falsch(Liest(Mit("\"profilVersion\":2", "\"profilVersion\":2.0")), "Nachkommastelle");
        Pruefe.Falsch(Liest(Mit("\"profilVersion\":2", "\"profilVersion\":\"2\"")), "Zahl als Zeichenkette");
        Pruefe.Falsch(Liest(Mit("1805000000000", "1790000000000")), "gueltigBis nicht nach ausgestellt");
        Pruefe.Falsch(Liest(Mit("1805000000000", "9007199254740992")), "über 2^53-1");
        Pruefe.Falsch(Liest(Mit("\"Test\"", "\"Te\\u0007st\"")), "Steuerzeichen");
        Pruefe.Falsch(Liest(Mit("\"Test\"", "\"Te\u202Est\"")), "Bidi-Zeichen");
        Pruefe.Falsch(Liest(Mit("\"Test\"", "\"" + new string('x', 33) + "\"")), "Name über 32");
        Pruefe.Falsch(Liest(Mit("\"Test\"", "\"\"")), "leerer Name");
        Pruefe.Falsch(Liest(Mit("[\"IPv4\"]", "[\"IPv5\"]")), "unbekannte Familie");
        Pruefe.Falsch(Liest(Mit("[\"IPv4\"]", "[\"IPv4\",\"IPv4\"]")), "Familie doppelt");
        Pruefe.Falsch(Liest(Mit("[\"IPv4\"]", "[]")), "keine Familie");
        Pruefe.Falsch(Liest(Mit("funk.example", "funk_example")), "kein Hostname");
        Pruefe.Falsch(Liest(Mit(",\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"", "")), "nur ein Pin");
        Pruefe.Falsch(Liest(Mit("\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"", "\"XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=\"")), "Pin doppelt");
        Pruefe.Falsch(Liest(Mit("\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"",
            "\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\",\"AQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\",\"AgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"")), "vier Pins");
        Pruefe.Falsch(Liest(Mit("\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"", "\"AAAA\"")), "Pin zu kurz");
        var fuenfHosts = string.Join(',', Enumerable.Repeat("{\"name\":\"a.example\",\"familien\":[\"IPv4\"]}", 5));
        Pruefe.Falsch(Liest(Mit("{\"name\":\"funk.example\",\"familien\":[\"IPv4\"]}", fuenfHosts)), "fünf Hosts");
        Pruefe.Falsch(Liest(Mit("[{\"name\":\"funk.example\",\"familien\":[\"IPv4\"]}]", "[]")), "keine Hosts");
        Pruefe.Falsch(Serverprofil.TryLesen(new byte[] { 0x7B, 0x22, 0xFF, 0x22, 0x3A, 0x31, 0x7D }, out _), "ungültiges UTF-8");
        Pruefe.Falsch(Serverprofil.TryLesen(new byte[Serverprofil.MaxDatenBytes + 1], out _), "zu groß");
    }

    [Test]
    public static void Profil_MutationenWerfenNie()
    {
        // Kleiner Vorläufer von P-42 (dort 10^7 Fälle): kein Fall darf eine Ausnahme werfen oder angenommen werden.
        using var schluessel = TestProfilschluessel();
        Profilschluessel[] eingebaut = [schluessel];
        var umschlag = Profil811().AlsJson();
        var daten = Encoding.ASCII.GetBytes(Daten811);
        var zufall = new Random(811);
        for (var i = 0; i < 20_000; i++)
        {
            var u = (byte[])umschlag.Clone();
            var d = (byte[])daten.Clone();
            for (var j = zufall.Next(1, 4); j > 0; j--)
            {
                u[zufall.Next(u.Length)] = (byte)zufall.Next(256);
                d[zufall.Next(d.Length)] = (byte)zufall.Next(256);
            }
            if (SigniertesProfil.TryLesen(u, out var s))
                Pruefe.Falsch(ProfilAnnahme.Pruefen(s, eingebaut, null, DateTimeOffset.UnixEpoch).Urteil == ProfilUrteil.Uebernehmen
                    && !s.Daten.SequenceEqual(daten), $"Mutation {i} darf nicht übernommen werden");
            Serverprofil.TryLesen(d, out _);
            Serverprofil.TryLesen(d.AsSpan(0, zufall.Next(d.Length)), out _);
        }
    }

    [Test]
    public static void PinWaechter_ZweiMinutenBisAlarm()
    {
        var uhr = new IdentitaetsUhr(Start);
        var w = new PinWaechter(uhr);
        Pruefe.Falsch(w.PinFehler("192.0.2.1:443"), "erster Fehler: falscher Host, kein Alarm");
        Pruefe.Wahr(w.IstGemieden("192.0.2.1:443"), "Adresse gemieden");
        Pruefe.Falsch(w.IstGemieden("192.0.2.2:443"), "andere Adresse nicht");
        uhr.Vor(TimeSpan.FromSeconds(59));
        Pruefe.Wahr(w.IstGemieden("192.0.2.1:443"), "nach 59 s noch gemieden");
        uhr.Vor(TimeSpan.FromSeconds(1));
        Pruefe.Falsch(w.IstGemieden("192.0.2.1:443"), "nach 60 s wieder erlaubt");
        uhr.Vor(TimeSpan.FromSeconds(59));
        Pruefe.Falsch(w.PinFehler("192.0.2.2:443"), "nach 1:59 noch kein Alarm");
        uhr.Vor(TimeSpan.FromSeconds(1));
        Pruefe.Wahr(w.PinFehler("192.0.2.1:443"), "nach 2 min ohne gültigen Pin: Alarm");
        w.PinGueltig();
        Pruefe.Falsch(w.Alarm, "gültiger Pin setzt zurück");
        Pruefe.Falsch(w.IstGemieden("192.0.2.1:443"), "Meidung aufgehoben");
    }

    // ---------------------------------------------------------------- 8.6 Einmalcode

    [Test]
    public static void Einmalcode86_Normalisierung()
    {
        Span<char> n = stackalloc char[Einmalcode.Zeichen];
        Span<byte> h = stackalloc byte[32];

        Pruefe.Wahr(Einmalcode.Normalisieren(" k7m4 q9tx-2hwd ", n), "Zeile 1 gültig");
        Pruefe.Gleich("K7M4Q9TX2HWD", n.ToString(), "Zeile 1 normalisiert");
        Einmalcode.Hash(n, h);
        Pruefe.BytesGleich(Hex(Hash86), h, "Zeile 1 SHA-256 (ASCII)");

        Pruefe.Wahr(Einmalcode.Normalisieren("K7M4-Q9TX-2HWO", n), "Zeile 2 gültig");
        Pruefe.Gleich("K7M4Q9TX2HW0", n.ToString(), "Zeile 2: O -> 0");
        Span<byte> h2 = stackalloc byte[32];
        Einmalcode.Hash(n, h2);
        Pruefe.Falsch(h.SequenceEqual(h2), "Zeile 2 ist ein anderer Code");

        Pruefe.Falsch(Einmalcode.Normalisieren("K7M4-Q9TX-2HW", n), "Zeile 3: 11 Zeichen");
        Pruefe.Falsch(Einmalcode.Normalisieren("K7M4-Q9TX-2HWU", n), "Zeile 4: U nicht im Alphabet");

        Pruefe.Wahr(Einmalcode.Normalisieren("iLo0-1111-1111", n), "I, L, O klein");
        Pruefe.Gleich("110011111111", n.ToString(), "I -> 1, L -> 1, O -> 0");
        Pruefe.Falsch(Einmalcode.Normalisieren("K7M4-Q9TX-2HWDX", n), "13 Zeichen");
        Pruefe.Falsch(Einmalcode.Normalisieren("K7M4\u2013Q9TX\u20132HWD", n), "Gedankenstrich ist kein -");
        Pruefe.Falsch(Einmalcode.Normalisieren("K7M4-Q9TX-2HW\u0131", n), "Nicht-ASCII (dotless i)");
        Pruefe.Falsch(Einmalcode.Normalisieren(new string(' ', 60) + "K7M4Q9TX2HWD", n), "über 64 Zeichen Eingabe");
    }

    [Test]
    public static void Einmalcode_ErzeugenFormUndZufall()
    {
        var gesehen = new HashSet<string>(StringComparer.Ordinal);
        Span<char> n = stackalloc char[Einmalcode.Zeichen];
        for (var i = 0; i < 200; i++)
        {
            var c = Einmalcode.Erzeugen();
            Pruefe.Gleich(14, c.Length, "Länge der Anzeigeform");
            Pruefe.Wahr(c[4] == '-' && c[9] == '-', "Bindestriche an 4 und 9");
            Pruefe.Wahr(Einmalcode.Normalisieren(c, n), "eigener Code ist gültig");
            Pruefe.Gleich(c.Replace("-", "", StringComparison.Ordinal), n.ToString(), "Normalform ohne Bindestriche");
            gesehen.Add(c);
        }
        Pruefe.Gleich(200, gesehen.Count, "keine Wiederholung in 200 Codes");
        Pruefe.Gleich("K7M4-Q9TX-2HWD", Einmalcode.Erzeugen(CodeQuelle("K7M4Q9TX2HWD")), "60 bit in 12 Zeichen zu 5 bit");
        Pruefe.Gleich("0000-0000-0000", Einmalcode.Erzeugen(z => { z.Clear(); z[0] = 0xF0; }), "die oberen 4 Bit zählen nicht");
        Pruefe.Gleich("ZZZZ-ZZZZ-ZZZZ", Einmalcode.Erzeugen(z => z.Fill(0xFF)), "alle 60 Bit gesetzt");
    }

    /// <summary>Zufallsquelle, die nacheinander die gegebenen Codes (Normalform) liefert.</summary>
    internal static Zufallsquelle CodeQuelle(params string[] codes)
    {
        var i = 0;
        return ziel =>
        {
            var code = codes[i++ % codes.Length];
            ulong wert = 0;
            for (var k = 0; k < Einmalcode.Zeichen; k++)
                wert |= (ulong)Einmalcode.Alphabet.IndexOf(code[k], StringComparison.Ordinal) << (55 - 5 * k);
            BinaryPrimitives.WriteUInt64BigEndian(ziel, wert | 0xF000_0000_0000_0000UL);
        };
    }

    // ---------------------------------------------------------------- P-11 Einmalcode-Register

    private static readonly Fingerabdruck GeraetA = new(Enumerable.Repeat((byte)0xA1, 32).ToArray());

    [Test]
    public static void Register86_EingabeVariantenTreffen()
    {
        var uhr = new IdentitaetsUhr(Start);
        var r = new EinmalcodeRegister(uhr, CodeQuelle("K7M4Q9TX2HWD", "K7M4Q9TX2HW0"));
        Pruefe.Gleich("K7M4-Q9TX-2HWD", r.Ausstellen(1, GeraetA, "Rechner", SchluesselArt.Tpm, "p1").Code, "Code 1");
        Pruefe.Gleich("K7M4-Q9TX-2HW0", r.Ausstellen(2, GeraetA, "Rechner", SchluesselArt.Tpm, "p1").Code, "Code 2");

        var e1 = r.Einloesen(" k7m4 q9tx-2hwd ", "1001");
        Pruefe.Wahr(e1.Ok, "Zeile 1 trifft Code 1");
        Pruefe.Gleich(1L, e1.Bindung!.Verbindung, "gebunden an Verbindung 1");
        Pruefe.Gleich(GeraetA, e1.Bindung.Geraet, "gebunden an den Fingerabdruck");
        Pruefe.Gleich(SchluesselArt.Tpm, e1.Bindung.SchluesselArt, "Schlüsselart laut Client");

        var e2 = r.Einloesen("K7M4-Q9TX-2HWO", "1002");
        Pruefe.Wahr(e2.Ok, "Zeile 2 trifft den Code mit 0 (O -> 0)");
        Pruefe.Gleich(2L, e2.Bindung!.Verbindung, "Verbindung 2");

        Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("K7M4-Q9TX-2HW", "1003").Fehler, "Zeile 3: Fehlversuch");
        Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("K7M4-Q9TX-2HWU", "1003").Fehler, "Zeile 4: Fehlversuch");
        for (var i = 0; i < 3; i++) r.Einloesen("0000-0000-0000", "1003");
        Pruefe.Gleich(Fehlercode.Grenze, r.Einloesen("K7M4-Q9TX-2HWD", "1003").Fehler, "Zeilen 3 und 4 zählten als Fehlversuche (5 erreicht)");
    }

    [Test]
    public static void Register_EinheitlichCodeUngueltig()
    {
        var uhr = new IdentitaetsUhr(Start);
        var r = new EinmalcodeRegister(uhr, CodeQuelle("AAAABBBBCCCC", "DDDDEEEEFFFF", "GGGGHHHHJJJJ"));
        r.Ausstellen(1, GeraetA, "Rechner", SchluesselArt.Software, "p");
        Pruefe.Wahr(r.Einloesen("AAAA-BBBB-CCCC", "2001").Ok, "Treffer");
        Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("AAAA-BBBB-CCCC", "2001").Fehler, "verbraucht");

        r.Ausstellen(2, GeraetA, "Rechner", SchluesselArt.Software, "p");
        uhr.Vor(TimeSpan.FromMinutes(5));
        Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("DDDD-EEEE-FFFF", "2002").Fehler, "abgelaufen nach genau 5 min");
        Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("ZZZZ-ZZZZ-ZZZZ", "2003").Fehler, "falsch");

        var a = r.Ausstellen(3, GeraetA, "Rechner", SchluesselArt.Software, "p");
        Pruefe.Gleich(uhr.GetUtcNow() + TimeSpan.FromMinutes(5), a.GueltigBis, "gueltigBis = jetzt + 5 min");
        uhr.Vor(TimeSpan.FromSeconds(299));
        Pruefe.Wahr(r.Einloesen("GGGG-HHHH-JJJJ", "2004").Ok, "nach 4:59 noch gültig");
    }

    [Test]
    public static void Register_GebundenAnVerbindung()
    {
        var uhr = new IdentitaetsUhr(Start);
        var r = new EinmalcodeRegister(uhr, CodeQuelle("AAAABBBBCCCC", "DDDDEEEEFFFF", "GGGGHHHHJJJJ"));
        r.Ausstellen(1, GeraetA, "Rechner", SchluesselArt.Software, "p");
        r.Ausstellen(1, GeraetA, "Rechner", SchluesselArt.Software, "p");
        Pruefe.Gleich(1, r.OffeneCodes, "neue Anfrage ersetzt den offenen Code");
        Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("AAAA-BBBB-CCCC", "3001").Fehler, "ersetzter Code gilt nicht mehr");
        r.VerbindungBeendet(1);
        Pruefe.Gleich(0, r.OffeneCodes, "Verbindung zu: Code fort");
        Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("DDDD-EEEE-FFFF", "3001").Fehler, "Code der geschlossenen Verbindung");
    }

    [Test]
    public static void Register_GrenzenJeVerbindungUndPraefix()
    {
        var uhr = new IdentitaetsUhr(Start);
        var r = new EinmalcodeRegister(uhr);
        for (var i = 0; i < 3; i++) Pruefe.Wahr(r.Ausstellen(1, GeraetA, "Rechner", SchluesselArt.Software, "p").Ok, $"Code {i + 1} der Verbindung");
        Pruefe.Gleich(Fehlercode.Grenze, r.Ausstellen(1, GeraetA, "Rechner", SchluesselArt.Software, "p").Fehler, "vierter Code je Verbindung");
        for (var v = 2; v <= 8; v++) Pruefe.Wahr(r.Ausstellen(v, GeraetA, "Rechner", SchluesselArt.Software, "p").Ok, $"Präfix Code {v + 2}");
        Pruefe.Gleich(Fehlercode.Grenze, r.Ausstellen(9, GeraetA, "Rechner", SchluesselArt.Software, "p").Fehler, "11. Code je Präfix und Stunde");
        Pruefe.Wahr(r.Ausstellen(9, GeraetA, "Rechner", SchluesselArt.Software, "q").Ok, "anderes Präfix unberührt");
        uhr.Vor(TimeSpan.FromHours(1));
        Pruefe.Wahr(r.Ausstellen(10, GeraetA, "Rechner", SchluesselArt.Software, "p").Ok, "nach einer Stunde wieder");
        Pruefe.Gleich(Fehlercode.Ungueltig, r.Ausstellen(11, GeraetA, "Rech\u0000ner", SchluesselArt.Software, "p").Fehler, "Gerätename mit Steuerzeichen");
        Pruefe.Gleich(Fehlercode.Ungueltig, r.Ausstellen(11, GeraetA, new string('x', 41), SchluesselArt.Software, "p").Fehler, "Gerätename über 40");
    }

    [Test]
    public static void Register_GlobalerAlarmNurAlarm()
    {
        var r = new EinmalcodeRegister(new IdentitaetsUhr(Start));
        for (var i = 0; i < 100; i++)
        {
            var a = r.Ausstellen(i, GeraetA, "Rechner", SchluesselArt.Software, $"p{i / 10}");
            Pruefe.Wahr(a.Ok && a.Wirkungen.Count == 0, $"Code {i + 1} ohne Alarm");
        }
        var hundertEins = r.Ausstellen(100, GeraetA, "Rechner", SchluesselArt.Software, "p10");
        Pruefe.Wahr(hundertEins.Ok, "101. Code wird trotzdem ausgestellt (keine globale Sperre)");
        Pruefe.Wahr(hundertEins.Wirkungen.OfType<Alarm>().Any(), "aber mit Alarm");
    }

    [Test]
    public static void Register_GrenzenJeDiscordId()
    {
        var uhr = new IdentitaetsUhr(Start);
        var r = new EinmalcodeRegister(uhr);
        string Code(long v) => r.Ausstellen(v, GeraetA, "Rechner", SchluesselArt.Software, $"p{v}").Code!;

        for (var i = 0; i < 5; i++) Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("ZZZZ-ZZZZ-ZZZZ", "4001").Fehler, $"Fehlversuch {i + 1}");
        var gueltig = Code(1);
        var grenze = r.Einloesen(gueltig, "4001");
        Pruefe.Gleich(Fehlercode.Grenze, grenze.Fehler, "6. Versuch: GRENZE, auch mit gültigem Code");
        Pruefe.Wahr(grenze.Wirkungen.OfType<Alarm>().Any(), "mit Alarm");
        Pruefe.Wahr(r.Einloesen(gueltig, "4002").Ok, "Code blieb unberührt");

        for (var i = 0; i < 3; i++) Pruefe.Wahr(r.Einloesen(Code(10 + i), "4003").Ok, $"Erfolg {i + 1}");
        Pruefe.Gleich(Fehlercode.Grenze, r.Einloesen(Code(20), "4003").Fehler, "4. Erfolg je Discord-ID und Stunde");
        uhr.Vor(TimeSpan.FromHours(1));
        Pruefe.Wahr(r.Einloesen(Code(21), "4003").Ok, "nach einer Stunde wieder");
        Pruefe.Wahr(r.EinloesenUeberAdminWerkzeug(Code(22)).Ok, "Admin-Werkzeug ohne Grenzen je Discord-ID");
        Pruefe.Gleich(Fehlercode.Ungueltig, r.Einloesen(Code(23), "04003").Fehler, "Discord-ID nicht kanonisch");
    }

    [Test]
    public static void Register_GrenzeErfolgeGlobal()
    {
        var r = new EinmalcodeRegister(new IdentitaetsUhr(Start));
        for (var i = 0; i < 30; i++)
        {
            var code = r.Ausstellen(i, GeraetA, "Rechner", SchluesselArt.Software, $"p{i}").Code!;
            Pruefe.Wahr(r.Einloesen(code, (5000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)).Ok, $"Erfolg {i + 1}");
        }
        var letzter = r.Ausstellen(30, GeraetA, "Rechner", SchluesselArt.Software, "p30").Code!;
        Pruefe.Gleich(Fehlercode.Grenze, r.Einloesen(letzter, "5999").Fehler, "31. Erfolg je Stunde global");
    }
}
