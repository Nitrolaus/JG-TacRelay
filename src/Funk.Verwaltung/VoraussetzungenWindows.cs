// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Funk.Verwaltung;

/// <summary>
/// TPM-Prüfung am echten PC, nur lesend: den Microsoft Platform Crypto Provider öffnen (NCryptOpenStorageProvider) und die
/// Eigenschaft PCP_PLATFORM_TYPE des Anbieters lesen (NCryptGetProperty). Kein Schlüssel wird geöffnet, angelegt oder
/// gelöscht. Derselbe Anbieter, über den <see cref="TpmAdminSchluessel"/> den Admin-Schlüssel anlegt.
/// Läuft in keinem Test (Sperre in SicherungTests); gemessen wird von Hand.
/// </summary>
public sealed unsafe partial class PcpTpmPruefung : ITpmPruefung
{
    /// <summary>NCRYPT_PCP_PLATFORM_TYPE_PROPERTY (ncrypt.h).</summary>
    public const string Eigenschaft = "PCP_PLATFORM_TYPE";

    /// <summary>Mehr meldet keine bekannte Fassung; schützt vor einem unsinnig großen Puffer.</summary>
    private const uint HoechstensBytes = 4096;

    public TpmStand Pruefen()
    {
        var status = NCryptOpenStorageProvider(out var anbieter, CngProvider.MicrosoftPlatformCryptoProvider.Provider, 0);
        if (status != 0) return Tpmdeutung.BeimOeffnen(status);
        try
        {
            var (lesen, text) = TextLesen(anbieter, Eigenschaft);
            return lesen == 0 ? Tpmdeutung.AusPlattform(text) : Tpmdeutung.BeimLesen(lesen);
        }
        finally
        {
            _ = NCryptFreeObject(anbieter);
        }
    }

    /// <summary>Eine Eigenschaft als UTF-16-Text (mit Null am Ende) lesen: erst die Größe, dann den Inhalt.</summary>
    private static (int Status, string? Text) TextLesen(nint objekt, string eigenschaft)
    {
        uint groesse = 0;
        var status = NCryptGetProperty(objekt, eigenschaft, null, 0, &groesse, 0);
        if (status != 0) return (status, null);
        if (groesse == 0 || groesse > HoechstensBytes) return (0, null);
        var puffer = new byte[groesse];
        fixed (byte* p = puffer)
        {
            status = NCryptGetProperty(objekt, eigenschaft, p, groesse, &groesse, 0);
        }
        return status != 0 ? (status, null) : (0, Encoding.Unicode.GetString(puffer, 0, (int)Math.Min(groesse, (uint)puffer.Length)).TrimEnd('\0'));
    }

    [LibraryImport("ncrypt.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NCryptOpenStorageProvider(out nint anbieter, string name, uint flags);

    [LibraryImport("ncrypt.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NCryptGetProperty(nint objekt, string eigenschaft, byte* ausgabe, uint groesse, uint* ergebnis, uint flags);

    [LibraryImport("ncrypt.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NCryptFreeObject(nint objekt);
}
