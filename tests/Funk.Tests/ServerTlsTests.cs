// JG TacRelay - Maintainer: Sarevoc
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;

namespace Funk.Tests;

/// <summary>
/// Echtes TLS auf Loopback (4.1, P-43): Client-Zertifikat Pflicht und nur P-256, ALPN <c>tacrelay/1</c>, TLS 1.3
/// oder 1.2. Unter Windows verlangt Schannel für jeden Handshake einen persistierten Schlüssel im Benutzerprofil
/// (<c>%APPDATA%\Microsoft\Crypto\Keys</c>); die Tests laufen dort nur mit <c>FUNK_TLS_IM_PROFIL=1</c>. Unter Linux
/// laufen sie immer.
/// </summary>
internal static class ServerTlsTests
{
    private static bool Uebersprungen(string name)
    {
        if (Testserver.TlsErlaubt) return false;
        Console.WriteLine($"HINWEIS ServerTlsTests.{name} übersprungen: Windows-Schannel braucht persistierte Schlüssel im Benutzerprofil (FUNK_TLS_IM_PROFIL=1 setzt die Freigabe voraus).");
        return true;
    }

    /// <summary>Gerätezertifikat nach 3.2, für Schannel in den Schlüsselspeicher übernommen (nur mit Freigabe).</summary>
    private static X509Certificate2 Geraet(ECDsa schluessel)
    {
        using var roh = Geraetezertifikat.Erzeugen(schluessel, TimeProvider.System);
        return ServerIdentitaet.FuerTls(roh);
    }

    [Test]
    public static async Task Tls_AnmeldungMitP256ClientZertifikat()
    {
        if (Uebersprungen(nameof(Tls_AnmeldungMitP256ClientZertifikat))) return;
        await using var t = await Testserver.StartenAsync(tls: true);
        using var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var zert = Geraet(schluessel);
        var (funker, fehler) = await t.TlsFunkerAsync(ECDsa.Create(schluessel.ExportParameters(true)), zert);
        Pruefe.Wahr(funker is not null, "Handshake mit P-256-Zertifikat: " + fehler?.Message);
        var f = funker!;
        await using (f)
        {
            var w = await f.HelloAsync();
            Pruefe.Gleich(WelcomeStatus.Unbekannt, w.Status, "unbekanntes Gerät nach echtem TLS");
            Pruefe.Wahr(t.Logbuch.Any(z => z.Contains("Pin", StringComparison.Ordinal)), "Start meldet den Pin");
        }
    }

    [Test]
    public static async Task Tls_OhneClientZertifikatAbgebrochen()
    {
        if (Uebersprungen(nameof(Tls_OhneClientZertifikatAbgebrochen))) return;
        await using var t = await Testserver.StartenAsync(tls: true);
        var (ssl, _) = await t.TlsVerbindenAsync(null);
        if (ssl is null) return;
        // Unter TLS 1.3 kann der Client den Abbruch erst beim Lesen sehen.
        await using (ssl)
        {
            var puffer = new byte[16];
            var gelesen = -1;
            try
            {
                gelesen = await ssl.ReadAsync(puffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception e) when (e is IOException or AuthenticationException)
            {
                gelesen = 0;
            }
            Pruefe.Gleich(0, gelesen, "ohne Client-Zertifikat keine Verbindung");
        }
    }

    [Test]
    public static async Task Tls_FalschesAlpnGetrennt()
    {
        if (Uebersprungen(nameof(Tls_FalschesAlpnGetrennt))) return;
        await using var t = await Testserver.StartenAsync(tls: true);
        using var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var zert = Geraet(schluessel);
        var (funker, _) = await t.TlsFunkerAsync(ECDsa.Create(schluessel.ExportParameters(true)), zert, "http/1.1");
        if (funker is null) return;
        await using (funker)
        {
            await funker.WartenAufTrennungAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public static async Task Tls_RsaZertifikatAbgelehnt()
    {
        if (Uebersprungen(nameof(Tls_RsaZertifikatAbgelehnt))) return;
        await using var t = await Testserver.StartenAsync(tls: true);
        using var rsa = RSA.Create(2048);
        var anfrage = new CertificateRequest("CN=Funk-Geraet", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var roh = anfrage.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var zert = ServerIdentitaet.FuerTls(roh);
        var (ssl, _) = await t.TlsVerbindenAsync(zert);
        if (ssl is null) return;
        await using (ssl)
        {
            var gelesen = -1;
            try
            {
                gelesen = await ssl.ReadAsync(new byte[16]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception e) when (e is IOException or AuthenticationException)
            {
                gelesen = 0;
            }
            Pruefe.Gleich(0, gelesen, "RSA-Zertifikat wird nicht angenommen (nur P-256, 3.2)");
        }
    }
}
