// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Funk.Protokoll.Identitaet;

namespace Funk.Verwaltung;

/// <summary>
/// Wo der Admin-Schlüssel liegt (ANMELDUNG-SICHERHEIT, "Der Admin-Schlüssel"). Im Betrieb <see cref="TpmAdminSchluessel"/>,
/// in Tests ein flüchtiger Schlüssel im Speicher.
/// </summary>
public interface IAdminSchluesselSpeicher
{
    /// <summary>Den vorhandenen Admin-Schlüssel öffnen, oder <c>null</c>, wenn es auf diesem PC keinen gibt.</summary>
    ECDsa? Laden();

    /// <summary>Einen neuen Admin-Schlüssel anlegen; wirft <see cref="InvalidOperationException"/> mit lesbarem Satz, wenn es nicht geht.</summary>
    ECDsa Erzeugen();
}

/// <summary>
/// Admin-Schlüssel im TPM (3.9): ECDSA P-256 über den Microsoft Platform Crypto Provider, Name <c>JG-TacRelay-Admin</c>,
/// nicht exportierbar, im Benutzerkontext. Anders als der Geräteschlüssel (3.2) OHNE Rückfall auf den Software-Speicher:
/// Ohne nutzbares TPM gibt es auf diesem PC keine Verwaltung. Ein kopierbarer Admin-Schlüssel wäre genau das, was der
/// eigene Schlüssel neben dem Geräteschlüssel verhindern soll.
/// UNGEPRÜFT im Testlauf: Ein persistierter TPM-Schlüssel landet im TPM und im Profil des Benutzers; die Tests prüfen nur die
/// Regel ohne Rückfall (<see cref="Adminschluesselwahl"/>) und die Parameter an einem flüchtigen Software-Schlüssel.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TpmAdminSchluessel : IAdminSchluesselSpeicher
{
    /// <summary>Name im Schlüsselspeicher des Benutzers, getrennt vom Geräteschlüssel <c>JG-TacRelay-Geraet</c>.</summary>
    public const string Name = "JG-TacRelay-Admin";

    public ECDsa? Laden()
    {
        try
        {
            if (!CngKey.Exists(Name, CngProvider.MicrosoftPlatformCryptoProvider)) return null;
            using var key = CngKey.Open(Name, CngProvider.MicrosoftPlatformCryptoProvider);
            return new ECDsaCng(key);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public ECDsa Erzeugen() => Adminschluesselwahl.NurTpm(() => Parameter.Erzeugen(CngProvider.MicrosoftPlatformCryptoProvider, Name));

    /// <summary>Erzeugungsparameter, gemeinsam mit dem Test am flüchtigen Schlüssel.</summary>
    internal static class Parameter
    {
        /// <summary>P-256, nicht exportierbar, nur Signieren; <paramref name="name"/> <c>null</c> ist flüchtig (Tests).</summary>
        public static ECDsaCng Erzeugen(CngProvider anbieter, string? name)
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
    }
}

/// <summary>Regel für den Admin-Schlüssel: nur TPM. Scheitert es, gibt es einen Satz für die Oberfläche, nie einen Rückfall.</summary>
public static class Adminschluesselwahl
{
    /// <summary>
    /// Sarevocs Entscheidung vom 01.10.2026: technisch Pflicht ist nur TPM 2.0. Eine Windows-PIN braucht das TPM für diesen
    /// Schlüssel nicht (er entsteht ohne UI-Richtlinie und ohne Kennwort am Schlüssel), sie ist keine Bedingung.
    /// </summary>
    public const string OhneTpm =
        "Auf diesem PC lässt sich kein Admin-Schlüssel im TPM anlegen. Technisch Pflicht ist TPM 2.0; einen Admin-Schlüssel als Datei "
        + "gibt es mit Absicht nicht. Eine Windows-PIN braucht das TPM dafür nicht. Frag den Eigentümer, ob die Verwaltung auf einem "
        + "anderen PC eingerichtet werden kann.";

    public static ECDsa NurTpm(Func<ECDsa> tpm)
    {
        ArgumentNullException.ThrowIfNull(tpm);
        try
        {
            return tpm();
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException(OhneTpm + WindowsCode(ex), ex);
        }
    }

    /// <summary>
    /// Der Code, den Windows beim Anlegen meldete (etwa NTE_DEVICE_NOT_READY), damit sich ein Fehlschlag am echten PC einordnen
    /// lässt; leer, wenn die Ausnahme nur den allgemeinen Code von .NET trägt (0x8013xxxx).
    /// </summary>
    private static string WindowsCode(Exception ex) =>
        ex.HResult != 0 && (ex.HResult & unchecked((int)0xFFFF0000)) != unchecked((int)0x80130000)
            ? " (Windows meldet " + Tpmdeutung.Code(ex.HResult) + ".)"
            : "";

    /// <summary>Die Admin-Kennung zum Vorlesen am Telefon (Anzeigeform des Fingerabdrucks, wie bei Geräten).</summary>
    public static string Kennung(ECDsa schluessel)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        return Fingerabdruck.AusSpki(schluessel.ExportSubjectPublicKeyInfo()).Anzeige;
    }
}
