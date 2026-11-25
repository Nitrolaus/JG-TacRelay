// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using System.Text;
using Funk.Protokoll.Identitaet;

namespace Funk.Echo;

/// <summary>
/// Geräteschlüssel des Echo-Funkers als PKCS#8-PEM-Datei (ECDSA P-256) für den Betrieb neben dem Server: Fehlt die Datei,
/// wird sie angelegt, unter Linux nur für den Besitzer lesbar (0600). Eine vorhandene Datei wird nie überschrieben. Der
/// Schlüssel verlässt den Rechner nicht; beim Server steht nur sein Fingerabdruck.
/// </summary>
internal static class Schluesseldatei
{
    /// <summary>Lädt den Schlüssel oder legt ihn an. <c>Neu</c>: gerade angelegt, das Gerät muss erst freigegeben werden.</summary>
    public static (ECDsa Schluessel, bool Neu) LadenOderAnlegen(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        if (File.Exists(pfad))
        {
            var k = ECDsa.Create();
            try
            {
                k.ImportFromPem(File.ReadAllText(pfad));
                if (!P256Spki.IstKanonisch(k.ExportSubjectPublicKeyInfo())) throw new InvalidDataException("Der Schlüssel ist kein ECDSA P-256.");
                return (k, false);
            }
            catch
            {
                k.Dispose();
                throw;
            }
        }

        var neu = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = Encoding.ASCII.GetBytes(neu.ExportPkcs8PrivateKeyPem());
        try
        {
            var optionen = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) optionen.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var datei = new FileStream(pfad, optionen);
            datei.Write(pem);
            datei.Flush(flushToDisk: true);
        }
        catch
        {
            neu.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pem);
        }
        return (neu, true);
    }
}
