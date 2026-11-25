// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Funk.Protokoll.Identitaet;

/// <summary>Wo der private Geräteschlüssel liegt, laut Client (3.2). Der Server kann das nicht prüfen.</summary>
public enum SchluesselArt
{
    /// <summary>Microsoft Software Key Storage Provider, Drahtname <c>SOFTWARE</c>.</summary>
    Software,

    /// <summary>TPM 2.0 über den Microsoft Platform Crypto Provider, Drahtname <c>TPM</c>.</summary>
    Tpm,
}

/// <summary>Geräteschlüssel ECDSA P-256 samt Art und Fingerabdruck. Besitzt den Schlüssel.</summary>
public sealed class Geraeteschluessel : IDisposable
{
    /// <summary>Übernimmt <paramref name="schluessel"/>; er muss P-256 sein.</summary>
    public Geraeteschluessel(ECDsa schluessel, SchluesselArt art)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        var spki = schluessel.ExportSubjectPublicKeyInfo();
        if (!P256Spki.IstKanonisch(spki)) throw new ArgumentException("Der Geräteschlüssel muss ECDSA P-256 sein.", nameof(schluessel));
        Schluessel = schluessel;
        Art = art;
        Fingerabdruck = Fingerabdruck.AusSpki(spki);
    }

    public ECDsa Schluessel { get; }

    public SchluesselArt Art { get; }

    public Fingerabdruck Fingerabdruck { get; }

    public void Dispose() => Schluessel.Dispose();
}

/// <summary>
/// Ablage des einen Geräteschlüssels eines Clients (3.2). <see cref="Erzeugen"/> ersetzt einen vorhandenen
/// Schlüssel; das ist der Weg nach ABGELEHNT oder ENTZOGEN.
/// </summary>
public interface IGeraeteschluesselSpeicher
{
    /// <summary>Der gespeicherte Schlüssel oder <c>null</c>. Der Aufrufer gibt ihn frei.</summary>
    Geraeteschluessel? Laden();

    /// <summary>Löscht einen vorhandenen Schlüssel und erzeugt einen neuen.</summary>
    Geraeteschluessel Erzeugen();

    /// <summary>Löscht den gespeicherten Schlüssel, falls vorhanden.</summary>
    void Loeschen();
}

/// <summary>
/// Schlüssel nur im Prozessspeicher, für Tests und Probeläufe. Meldet <see cref="SchluesselArt.Software"/>.
/// Nichts wird auf die Platte geschrieben; nach Prozessende ist der Schlüssel fort.
/// </summary>
public sealed class FluechtigerSchluesselspeicher : IGeraeteschluesselSpeicher
{
    private byte[]? _pkcs8;

    public Geraeteschluessel? Laden()
    {
        if (_pkcs8 is null) return null;
        var schluessel = ECDsa.Create();
        schluessel.ImportPkcs8PrivateKey(_pkcs8, out _);
        return new Geraeteschluessel(schluessel, SchluesselArt.Software);
    }

    public Geraeteschluessel Erzeugen()
    {
        Loeschen();
        var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _pkcs8 = schluessel.ExportPkcs8PrivateKey();
        return new Geraeteschluessel(schluessel, SchluesselArt.Software);
    }

    public void Loeschen()
    {
        if (_pkcs8 is null) return;
        CryptographicOperations.ZeroMemory(_pkcs8);
        _pkcs8 = null;
    }
}

/// <summary>
/// Persistierter Geräteschlüssel über CNG (3.2): zuerst TPM 2.0 (Microsoft Platform Crypto Provider), sonst der
/// Software-KSP, beide mit <c>ExportPolicy = None</c> im Benutzerkontext.
/// UNGEPRÜFT: Die Tests fassen diesen Weg nicht an, denn ein persistierter Schlüssel landet im Benutzerprofil
/// bzw. im TPM. Ob er als Client-Zertifikat in <c>SslStream</c> taugt, klärt der Spike aus Offener Punkt 1.
/// Geprüft sind nur die Rückfallregel und die Erzeugungsparameter an einem flüchtigen Software-Schlüssel.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CngSchluesselspeicher : IGeraeteschluesselSpeicher
{
    private readonly string _name;
    private readonly bool _tpmVersuchen;

    /// <param name="schluesselName">Name des Schlüssels im Schlüsselspeicher des Benutzers.</param>
    /// <param name="tpmVersuchen">false überspringt das TPM, etwa nach einem bekannten Fehlschlag.</param>
    public CngSchluesselspeicher(string schluesselName, bool tpmVersuchen = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schluesselName);
        _name = schluesselName;
        _tpmVersuchen = tpmVersuchen;
    }

    public Geraeteschluessel? Laden()
    {
        foreach (var (anbieter, art) in Anbieter())
        {
            if (!Existiert(anbieter)) continue;
            using var key = CngKey.Open(_name, anbieter);
            return new Geraeteschluessel(new ECDsaCng(key), art);
        }
        return null;
    }

    public Geraeteschluessel Erzeugen()
    {
        Loeschen();
        var (schluessel, art) = Schluesselwahl.MitRueckfall(
            _tpmVersuchen,
            () => Erzeugen(CngProvider.MicrosoftPlatformCryptoProvider, _name),
            () => Erzeugen(CngProvider.MicrosoftSoftwareKeyStorageProvider, _name));
        return new Geraeteschluessel(schluessel, art);
    }

    public void Loeschen()
    {
        foreach (var (anbieter, _) in Anbieter())
        {
            if (!Existiert(anbieter)) continue;
            using var key = CngKey.Open(_name, anbieter);
            key.Delete();
        }
    }

    /// <summary>
    /// Erzeugt einen P-256-Schlüssel, nicht exportierbar, im Benutzerkontext. <paramref name="name"/> = null
    /// ergibt einen flüchtigen Schlüssel, der nie auf die Platte kommt (so prüfen es die Tests).
    /// </summary>
    internal static ECDsaCng Erzeugen(CngProvider anbieter, string? name)
    {
        var parameter = new CngKeyCreationParameters
        {
            Provider = anbieter,
            ExportPolicy = CngExportPolicies.None,
            KeyCreationOptions = CngKeyCreationOptions.None,
            KeyUsage = CngKeyUsages.Signing,
        };
        using var key = CngKey.Create(CngAlgorithm.ECDsaP256, name, parameter);
        return new ECDsaCng(key);
    }

    private IEnumerable<(CngProvider, SchluesselArt)> Anbieter()
    {
        if (_tpmVersuchen) yield return (CngProvider.MicrosoftPlatformCryptoProvider, SchluesselArt.Tpm);
        yield return (CngProvider.MicrosoftSoftwareKeyStorageProvider, SchluesselArt.Software);
    }

    private bool Existiert(CngProvider anbieter)
    {
        try
        {
            return CngKey.Exists(_name, anbieter);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

/// <summary>Rückfallregel aus 3.2: TPM zuerst; scheitert es, der Software-Speicher mit Art SOFTWARE.</summary>
internal static class Schluesselwahl
{
    public static (ECDsa Schluessel, SchluesselArt Art) MitRueckfall(bool tpmVersuchen, Func<ECDsa> tpm, Func<ECDsa> software)
    {
        if (tpmVersuchen)
        {
            try
            {
                return (tpm(), SchluesselArt.Tpm);
            }
            catch (CryptographicException)
            {
                // kein TPM, gesperrt oder Anbieter fehlt: Rückfall
            }
            catch (PlatformNotSupportedException)
            {
                // kein CNG: Rückfall
            }
        }
        return (software(), SchluesselArt.Software);
    }
}
