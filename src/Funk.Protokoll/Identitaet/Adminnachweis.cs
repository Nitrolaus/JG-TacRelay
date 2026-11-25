// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Funk.Protokoll.Identitaet;

/// <summary>Wofür eine Unterschrift des Admin-Schlüssels gilt (3.9). Der Zweck steht in den unterschriebenen Bytes.</summary>
public enum AdminZweck
{
    /// <summary>Antrag auf Eintragung des Admin-Schlüssels (Besitznachweis).</summary>
    Antrag,

    /// <summary>TOTP-Geheimnis abholen.</summary>
    Geheimnis,

    /// <summary>Ersten Code der Einrichtung bestätigen.</summary>
    Einrichtung,

    /// <summary>Admin-Sitzung öffnen.</summary>
    Anmeldung,
}

/// <summary>
/// Unterschrift des Admin-Schlüssels nach 3.9: ECDSA P-256 mit SHA-256 im Format IEEE P1363 (64 Byte) über
/// <c>"tacrelay admin v1"</c> || 0x00 || Sitzungs-ID (u32) || Fingerabdruck des Geräts (32 Byte) || Nonce des Servers
/// (32 Byte) || Zweck (ASCII, etwa <c>ANMELDUNG</c>). So gilt sie weder in einer anderen Sitzung, noch auf einem anderen
/// Gerät, noch später, noch für einen anderen Zweck (Vektor 8.14).
/// </summary>
public static class Adminnachweis
{
    /// <summary>Länge der Nonce des Servers.</summary>
    public const int NonceLaenge = 32;

    /// <summary>Länge der Unterschrift (P1363, r und s je 32 Byte).</summary>
    public const int UnterschriftLaenge = 64;

    /// <summary>So lange gilt eine Nonce, und nur einmal.</summary>
    public static readonly TimeSpan NonceGueltigkeit = TimeSpan.FromSeconds(60);

    /// <summary>ASCII <c>"tacrelay admin v1"</c> und ein Nullbyte.</summary>
    public static ReadOnlySpan<byte> Praefix => "tacrelay admin v1\0"u8;

    /// <summary>Drahtname und unterschriebene Form des Zwecks.</summary>
    public static string Drahtname(this AdminZweck zweck) => zweck switch
    {
        AdminZweck.Antrag => "ANTRAG",
        AdminZweck.Geheimnis => "GEHEIMNIS",
        AdminZweck.Einrichtung => "EINRICHTUNG",
        AdminZweck.Anmeldung => "ANMELDUNG",
        _ => throw new ArgumentOutOfRangeException(nameof(zweck)),
    };

    /// <summary>Die unterschriebenen Bytes (ohne Hash).</summary>
    public static byte[] Daten(uint sitzungsId, Fingerabdruck geraet, ReadOnlySpan<byte> nonce, AdminZweck zweck)
    {
        if (nonce.Length != NonceLaenge) throw new ArgumentException($"Die Nonce hat {NonceLaenge} Byte.", nameof(nonce));
        var name = Drahtname(zweck);
        var daten = new byte[Praefix.Length + 4 + Fingerabdruck.Laenge + NonceLaenge + name.Length];
        var pos = 0;
        Praefix.CopyTo(daten);
        pos += Praefix.Length;
        BinaryPrimitives.WriteUInt32BigEndian(daten.AsSpan(pos), sitzungsId);
        pos += 4;
        geraet.Schreiben(daten.AsSpan(pos));
        pos += Fingerabdruck.Laenge;
        nonce.CopyTo(daten.AsSpan(pos));
        pos += NonceLaenge;
        foreach (var c in name) daten[pos++] = (byte)c;
        return daten;
    }

    /// <summary>Unterschreibt mit dem Admin-Schlüssel (Client, Tests; der Schlüssel liegt im TPM des Admins).</summary>
    public static byte[] Unterschreiben(ECDsa adminSchluessel, uint sitzungsId, Fingerabdruck geraet, ReadOnlySpan<byte> nonce, AdminZweck zweck)
    {
        ArgumentNullException.ThrowIfNull(adminSchluessel);
        return adminSchluessel.SignData(Daten(sitzungsId, geraet, nonce, zweck), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>
    /// Prüft eine Unterschrift gegen einen Admin-Schlüssel in kanonischer SPKI-Form. Ein Schlüssel, der sich nicht laden
    /// lässt, oder eine Unterschrift falscher Länge ergibt <c>false</c>; es wird nie geworfen.
    /// </summary>
    public static bool Pruefen(ReadOnlySpan<byte> spki, ReadOnlySpan<byte> unterschrift, uint sitzungsId, Fingerabdruck geraet,
        ReadOnlySpan<byte> nonce, AdminZweck zweck)
    {
        if (unterschrift.Length != UnterschriftLaenge || nonce.Length != NonceLaenge || !P256Spki.IstKanonisch(spki)) return false;
        try
        {
            using var schluessel = ECDsa.Create();
            schluessel.ImportSubjectPublicKeyInfo(spki, out _);
            return schluessel.VerifyData(Daten(sitzungsId, geraet, nonce, zweck), unterschrift, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
