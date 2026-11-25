// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Funk.Protokoll.Identitaet;

/// <summary>
/// Selbstsigniertes Gerätezertifikat nach 3.2 und die Annahmeprüfung des Servers. Der Server prüft weder
/// Gültigkeit noch Kette, nur den P-256-Schlüssel; den Besitz beweist der TLS-Handshake.
/// </summary>
public static class Geraetezertifikat
{
    /// <summary>Subject ohne persönliche Angaben, denn bei TLS 1.2 geht das Zertifikat im Klartext.</summary>
    public const string Subject = "CN=Funk-Geraet";

    /// <summary>OID der erweiterten Schlüsselverwendung clientAuth.</summary>
    public const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";

    /// <summary>Länge der Seriennummer in Zufallsbytes.</summary>
    public const int SeriennummerBytes = 16;

    /// <summary>NotBefore liegt so weit vor jetzt.</summary>
    public static readonly TimeSpan Vorlauf = TimeSpan.FromHours(1);

    /// <summary>Laufzeit in Jahren ab jetzt.</summary>
    public const int LaufzeitJahre = 20;

    /// <summary>
    /// Erzeugt das Zertifikat samt privatem Schlüssel. Bei einem CNG-Schlüssel muss der Schlüssel persistiert
    /// sein; ein flüchtiger, nicht exportierbarer Schlüssel lässt sich nicht anhängen (.NET 10, geprüft).
    /// </summary>
    public static X509Certificate2 Erzeugen(ECDsa schluessel, TimeProvider zeit)
    {
        using var ohneSchluessel = ErzeugenOhnePrivatschluessel(schluessel, zeit);
        return ohneSchluessel.CopyWithPrivateKey(schluessel);
    }

    /// <summary>Wie <see cref="Erzeugen"/>, aber nur das öffentliche Zertifikat.</summary>
    public static X509Certificate2 ErzeugenOhnePrivatschluessel(ECDsa schluessel, TimeProvider zeit)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        ArgumentNullException.ThrowIfNull(zeit);
        if (!P256Spki.IstKanonisch(schluessel.ExportSubjectPublicKeyInfo()))
            throw new ArgumentException("Der Geräteschlüssel muss ECDSA P-256 sein.", nameof(schluessel));

        var anfrage = new CertificateRequest(Subject, schluessel, HashAlgorithmName.SHA256);
        anfrage.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        anfrage.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ClientAuthOid)], critical: false));

        Span<byte> seriennummer = stackalloc byte[SeriennummerBytes];
        RandomNumberGenerator.Fill(seriennummer);
        var jetzt = zeit.GetUtcNow();
        return anfrage.Create(
            anfrage.SubjectName,
            X509SignatureGenerator.CreateForECDsa(schluessel),
            jetzt - Vorlauf,
            jetzt.AddYears(LaufzeitJahre),
            seriennummer);
    }

    /// <summary>
    /// Annahme durch den Server (3.2, 4.1): ein Blattzertifikat mit kanonischem P-256-Schlüssel, dessen Punkt auf
    /// der Kurve liegt. Liefert den Fingerabdruck als Geräte-Identität. Gültigkeit und Kette zählen nicht.
    /// </summary>
    public static bool TryAnnehmen(X509Certificate2? blatt, out Fingerabdruck fingerabdruck)
    {
        fingerabdruck = default;
        if (blatt is null) return false;
        Span<byte> spki = stackalloc byte[P256Spki.Laenge];
        if (!blatt.PublicKey.TryExportSubjectPublicKeyInfo(spki, out var n)) return false;
        spki = spki[..n];
        if (!P256Spki.IstGueltig(spki)) return false;
        fingerabdruck = Fingerabdruck.AusSpki(spki);
        return true;
    }
}
