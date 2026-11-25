// JG TacRelay - Maintainer: Sarevoc
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Funk.Protokoll.Identitaet;

namespace Funk.Server.Betrieb;

/// <summary>
/// Identität des Servers (3.1): langlebiger ECDSA-P-256-Schlüssel mit selbstsigniertem Zertifikat. Die Clients prüfen
/// allein den SPKI-Pin, nicht Name, Kette oder Gültigkeit. Der Schlüssel kommt aus einer Datei (unter systemd aus
/// <c>$CREDENTIALS_DIRECTORY</c>) und wird nie ausgegeben.
/// <para>
/// Windows: Schannel arbeitet in LSASS und kann keine flüchtigen Schlüssel benutzen ("platform does not support
/// ephemeral keys", gemessen unter .NET 10.0.12). Dort wird der Schlüssel deshalb für die Laufzeit des Prozesses in
/// den Schlüsselspeicher des Benutzers importiert (PKCS#12 ohne PersistKeySet) und beim Beenden wieder gelöscht.
/// Unter Linux (OpenSSL) bleibt er im Prozessspeicher.
/// </para>
/// </summary>
public sealed class ServerIdentitaet : IDisposable
{
    private readonly X509Certificate2 _zertifikat;

    private ServerIdentitaet(X509Certificate2 zertifikat)
    {
        _zertifikat = zertifikat;
        Pin = Fingerabdruck.AusZertifikat(zertifikat);
        Kontext = SslStreamCertificateContext.Create(zertifikat, additionalCertificates: null, offline: true);
    }

    /// <summary>SHA-256 über das SPKI des Zertifikats; gehört in <c>pins</c> des Serverprofils.</summary>
    public Fingerabdruck Pin { get; }

    public SslStreamCertificateContext Kontext { get; }

    /// <summary>Lädt Zertifikat und Schlüssel (PEM). Nur ECDSA P-256 wird angenommen.</summary>
    public static ServerIdentitaet Laden(string zertifikatPfad, string schluesselPfad)
    {
        ArgumentNullException.ThrowIfNull(zertifikatPfad);
        ArgumentNullException.ThrowIfNull(schluesselPfad);
        using var geladen = X509Certificate2.CreateFromPemFile(zertifikatPfad, schluesselPfad);
        return AusZertifikat(geladen);
    }

    /// <summary>
    /// Übernimmt ein Zertifikat mit privatem Schlüssel (Tests, Werkzeuge); der Aufrufer behält seines.
    /// <paramref name="schannelTauglich"/> = <c>false</c> lässt unter Windows den Import in den Schlüsselspeicher weg:
    /// dann schreibt nichts ins Benutzerprofil, aber ein TLS-Handshake scheitert (nur für Tests ohne TLS).
    /// </summary>
    public static ServerIdentitaet AusZertifikat(X509Certificate2 mitSchluessel, bool schannelTauglich = true)
    {
        ArgumentNullException.ThrowIfNull(mitSchluessel);
        using (var ec = mitSchluessel.GetECDsaPrivateKey())
        {
            if (ec is null || !P256Spki.IstKanonisch(mitSchluessel.PublicKey.ExportSubjectPublicKeyInfo()))
                throw new InvalidDataException("Die Serveridentität muss ein ECDSA-P-256-Schlüssel sein (3.1).");
        }
        return new ServerIdentitaet(schannelTauglich || !OperatingSystem.IsWindows()
            ? FuerTls(mitSchluessel)
            : X509CertificateLoader.LoadPkcs12(mitSchluessel.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.EphemeralKeySet));
    }

    /// <summary>
    /// Macht ein Zertifikat mit Schlüssel für <c>SslStream</c> brauchbar. Unter Windows über einen PKCS#12-Umweg in den
    /// Schlüsselspeicher des Benutzers (siehe Klassenkommentar), sonst eine Kopie.
    /// </summary>
    public static X509Certificate2 FuerTls(X509Certificate2 mitSchluessel)
    {
        ArgumentNullException.ThrowIfNull(mitSchluessel);
        if (!OperatingSystem.IsWindows())
            return X509CertificateLoader.LoadPkcs12(mitSchluessel.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.EphemeralKeySet);
        var kennwort = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var pfx = mitSchluessel.Export(X509ContentType.Pkcs12, kennwort);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, kennwort, X509KeyStorageFlags.UserKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }

    /// <summary>
    /// Admin-Werkzeug: erzeugt Schlüssel und selbstsigniertes Zertifikat (20 Jahre) und schreibt beide als PEM. Der
    /// Schlüssel ist unter Linux nur für den Besitzer lesbar. Liefert den Pin. Gibt es eine der beiden Dateien schon, bleibt
    /// ohne <paramref name="ersetzen"/> alles, wie es ist (<see cref="SchonDaException"/>): Ein neuer Schlüssel hat einen neuen
    /// Pin, und die Clients nehmen ihn erst mit einem neuen Profil an.
    /// </summary>
    public static Fingerabdruck Erzeugen(string zertifikatPfad, string schluesselPfad, string name = "Funk-Server", bool ersetzen = false)
    {
        ArgumentNullException.ThrowIfNull(zertifikatPfad);
        ArgumentNullException.ThrowIfNull(schluesselPfad);
        if (!ersetzen)
        {
            foreach (var pfad in new[] { schluesselPfad, zertifikatPfad })
                if (File.Exists(pfad)) throw new SchonDaException(Path.GetFullPath(pfad));
        }
        using var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var anfrage = new CertificateRequest("CN=" + name, schluessel, HashAlgorithmName.SHA256);
        anfrage.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        anfrage.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        var jetzt = DateTimeOffset.UtcNow;
        using var zert = anfrage.CreateSelfSigned(jetzt.AddHours(-1), jetzt.AddYears(20));
        var pem = schluessel.ExportPkcs8PrivateKeyPem();
        var bytes = Encoding.ASCII.GetBytes(pem);
        try
        {
            Dateiablage.AtomarSchreiben(schluesselPfad, bytes, nurBesitzer: true, ersetzen);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
        Dateiablage.AtomarSchreiben(zertifikatPfad, Encoding.ASCII.GetBytes(zert.ExportCertificatePem()), nurBesitzer: false, ersetzen);
        return Fingerabdruck.AusZertifikat(zert);
    }

    public void Dispose() => _zertifikat.Dispose();
}
