// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>
/// Geräteschlüssel (ECDSA P-256) mit selbstsigniertem Gerätezertifikat nach 3.2. Der Fingerabdruck (SHA-256 über das SPKI)
/// ist die Identität des Geräts beim Server. Die Klasse besitzt Schlüssel und Zertifikat und gibt beide frei.
/// </summary>
public sealed class Geraeteidentitaet : IDisposable
{
    private X509Certificate2? _schannel;

    private Geraeteidentitaet(ECDsa schluessel, X509Certificate2 zertifikat, Protokoll.Steuerung.SchluesselArt art, bool persistiert)
    {
        Schluessel = schluessel;
        Zertifikat = zertifikat;
        Art = art;
        Persistiert = persistiert;
        Fingerabdruck = Fingerabdruck.AusSchluessel(schluessel);
    }

    /// <summary>
    /// Ob der Schlüssel mit Namen in einem Schlüsselspeicher von Windows liegt (TPM über den Platform Crypto Provider oder
    /// der Software-KSP als Rückfall, <see cref="CngSchluesselspeicher"/>). Dann verweist das Zertifikat auf ihn, und
    /// Schannel öffnet ihn selbst.
    /// </summary>
    public bool Persistiert { get; }

    /// <summary>Privater Geräteschlüssel; im Produkt aus TPM bzw. KSP, in Tests nur im Speicher.</summary>
    public ECDsa Schluessel { get; }

    /// <summary>Gerätezertifikat mit privatem Schlüssel (3.2).</summary>
    public X509Certificate2 Zertifikat { get; }

    /// <summary>Schlüsselart laut Client, geht in Hello (ungeprüft beim Server).</summary>
    public Protokoll.Steuerung.SchluesselArt Art { get; }

    /// <summary>SHA-256 über das SPKI, Anzeigeform über <see cref="Fingerabdruck.Anzeige"/>.</summary>
    public Fingerabdruck Fingerabdruck { get; }

    /// <summary>Neuer Schlüssel nur im Speicher (Tests, Lasttest): nichts wird persistiert.</summary>
    public static Geraeteidentitaet Fluechtig(TimeProvider? zeit = null)
    {
        var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            return Aus(schluessel, Protokoll.Steuerung.SchluesselArt.Software, zeit);
        }
        catch
        {
            schluessel.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Übernimmt einen vorhandenen Schlüssel (etwa aus <see cref="IGeraeteschluesselSpeicher"/>) und stellt das Zertifikat
    /// dazu aus. Ein CNG-Schlüssel muss persistiert sein, sonst lässt sich der private Schlüssel nicht anhängen.
    /// </summary>
    public static Geraeteidentitaet Aus(ECDsa schluessel, Protokoll.Steuerung.SchluesselArt art, TimeProvider? zeit = null)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        var zertifikat = Geraetezertifikat.Erzeugen(schluessel, zeit ?? TimeProvider.System);
        return new Geraeteidentitaet(schluessel, zertifikat, art, art == Protokoll.Steuerung.SchluesselArt.Tpm || IstPersistiert(schluessel));
    }

    /// <summary>Ein CNG-Schlüssel mit Namen (nicht flüchtig), wie ihn <see cref="CngSchluesselspeicher"/> anlegt oder öffnet.</summary>
    private static bool IstPersistiert(ECDsa schluessel) =>
        OperatingSystem.IsWindows() && schluessel is ECDsaCng cng && !cng.Key.IsEphemeral;

    /// <summary>Wie <see cref="FuerTls"/> das Zertifikat an Schannel gibt.</summary>
    internal enum TlsWeg
    {
        /// <summary>Das Zertifikat selbst: Es verweist auf den persistierten Schlüssel, oder das System ist nicht Windows.</summary>
        Direkt,

        /// <summary>Flüchtiger Schlüssel unter Windows: für die Laufzeit als PKCS#12 in den Speicher des Benutzers.</summary>
        Pkcs12Import,
    }

    /// <summary>
    /// Welcher Weg für welchen Schlüssel. Befund der Durchsicht (29.09.2026): Früher nahm jeder Software-Schlüssel unter
    /// Windows den PKCS#12-Umweg, auch der persistierte Rückfall ohne TPM. Der ist mit <c>ExportPolicy = None</c> angelegt und
    /// lässt sich nicht exportieren; das erste echte Verbinden wäre an einem PC ohne nutzbares TPM gescheitert.
    /// </summary>
    internal static TlsWeg WegFuer(bool windows, bool persistiert) => !windows || persistiert ? TlsWeg.Direkt : TlsWeg.Pkcs12Import;

    /// <summary>
    /// Zertifikat für <c>SslStream</c>. Unter Windows kann Schannel keinen flüchtigen Schlüssel benutzen; dort wird der
    /// Schlüssel für die Laufzeit in den Schlüsselspeicher des Benutzers importiert (PKCS#12 ohne PersistKeySet,
    /// schreibt ins Benutzerprofil) und mit <see cref="Dispose"/> wieder gelöscht. Unter Linux bleibt er im Speicher.
    /// Ein Schlüssel aus TPM oder KSP (Produkt) ist bereits persistiert und braucht den Umweg nicht; er ließe sich auch gar
    /// nicht exportieren (<see cref="WegFuer"/>).
    /// </summary>
    public X509Certificate2 FuerTls()
    {
        if (_schannel is not null) return _schannel;
        if (WegFuer(OperatingSystem.IsWindows(), Persistiert) == TlsWeg.Direkt)
        {
            _schannel = Zertifikat;
            return _schannel;
        }
        var kennwort = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var pfx = Zertifikat.Export(X509ContentType.Pkcs12, kennwort);
        try
        {
            _schannel = X509CertificateLoader.LoadPkcs12(pfx, kennwort, X509KeyStorageFlags.UserKeySet);
            return _schannel;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }

    public void Dispose()
    {
        if (_schannel is not null && !ReferenceEquals(_schannel, Zertifikat)) _schannel.Dispose();
        _schannel = null;
        Zertifikat.Dispose();
        Schluessel.Dispose();
    }
}
