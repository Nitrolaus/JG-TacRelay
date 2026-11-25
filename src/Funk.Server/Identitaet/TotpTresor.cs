// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Funk.Protokoll.Identitaet;

// Liegt im Server (funk-server), nicht in der Client-Bibliothek Funk.Protokoll: Das normale Setup bringt keinen
// Verwaltungscode mit (A45, Gegenprüfung Stufe 1). Der Namensraum bleibt, damit Spezifikation, Server und Tests dieselben
// Namen tragen.

/// <summary>
/// Verschlüsselung der TOTP-Geheimnisse und Prüfwerte der Notfallcodes am Server (3.9): AES-256-GCM mit dem Schlüssel
/// <c>funk-totp-schluessel</c> (32 Byte, per systemd <c>LoadCredential</c>), je Verschlüsselung eine neue zufällige
/// Nonce (96 Bit), zusätzliche Daten <c>"tacrelay totp v1"</c> || 0x00 || Konto-ID (u32) || Generation (u32), damit
/// sich kein Chiffrat auf ein anderes Konto oder einen alten Stand umhängen lässt. Notfallcodes stehen nur als
/// HMAC-SHA256 mit einem abgeleiteten Schlüssel da. Entschlüsselte Geheimnisse gibt es nur für die Dauer einer Prüfung.
/// </summary>
public sealed class TotpTresor : IDisposable
{
    /// <summary>Länge des Schlüssels.</summary>
    public const int SchluesselLaenge = 32;

    private const int NonceLaenge = 12;
    private const int TagLaenge = 16;

    private readonly byte[] _schluessel;
    private readonly byte[] _notfallSchluessel;

    public TotpTresor(ReadOnlySpan<byte> schluessel)
    {
        if (schluessel.Length != SchluesselLaenge) throw new ArgumentException($"Der TOTP-Schlüssel hat {SchluesselLaenge} Byte.", nameof(schluessel));
        _schluessel = schluessel.ToArray();
        _notfallSchluessel = HKDF.DeriveKey(HashAlgorithmName.SHA256, _schluessel, 32, salt: "tacrelay server"u8.ToArray(),
            info: "tacrelay notfallcode v1"u8.ToArray());
    }

    /// <summary>Nur für Tests: zufälliger Schlüssel im Speicher.</summary>
    public static TotpTresor Zufaellig() => new(RandomNumberGenerator.GetBytes(SchluesselLaenge));

    /// <summary>AAD nach 3.9.</summary>
    internal static byte[] Zusatzdaten(uint kontoId, uint generation)
    {
        var praefix = "tacrelay totp v1\0"u8;
        var aad = new byte[praefix.Length + 8];
        praefix.CopyTo(aad);
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(praefix.Length), kontoId);
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(praefix.Length + 4), generation);
        return aad;
    }

    /// <summary>Verschlüsselt ein Geheimnis: Nonce (12) || Chiffrat || Tag (16).</summary>
    public byte[] Verschluesseln(ReadOnlySpan<byte> geheimnis, uint kontoId, uint generation)
    {
        var ergebnis = new byte[NonceLaenge + geheimnis.Length + TagLaenge];
        var nonce = ergebnis.AsSpan(0, NonceLaenge);
        RandomNumberGenerator.Fill(nonce);
        using var gcm = new AesGcm(_schluessel, TagLaenge);
        gcm.Encrypt(nonce, geheimnis, ergebnis.AsSpan(NonceLaenge, geheimnis.Length), ergebnis.AsSpan(NonceLaenge + geheimnis.Length),
            Zusatzdaten(kontoId, generation));
        return ergebnis;
    }

    /// <summary>
    /// Entschlüsselt in einen neuen Puffer, den der Aufrufer nach Gebrauch mit <see cref="CryptographicOperations.ZeroMemory"/>
    /// nullt. Falsches Konto, falsche Generation oder verändertes Chiffrat ergeben <c>null</c>.
    /// </summary>
    public byte[]? Entschluesseln(ReadOnlySpan<byte> chiffrat, uint kontoId, uint generation)
    {
        if (chiffrat.Length < NonceLaenge + TagLaenge + 1) return null;
        var laenge = chiffrat.Length - NonceLaenge - TagLaenge;
        var klar = new byte[laenge];
        try
        {
            using var gcm = new AesGcm(_schluessel, TagLaenge);
            gcm.Decrypt(chiffrat[..NonceLaenge], chiffrat.Slice(NonceLaenge, laenge), chiffrat[^TagLaenge..], klar, Zusatzdaten(kontoId, generation));
            return klar;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(klar);
            return null;
        }
    }

    /// <summary>Prüfwert eines Notfallcodes: HMAC-SHA256 über die normalisierte Form (ASCII), gebunden an das Konto.</summary>
    public byte[] NotfallPruefwert(ReadOnlySpan<char> normalisiert, uint kontoId)
    {
        var daten = new byte[4 + normalisiert.Length];
        BinaryPrimitives.WriteUInt32BigEndian(daten, kontoId);
        for (var i = 0; i < normalisiert.Length; i++)
        {
            if (normalisiert[i] > 0x7F) throw new ArgumentException("Nur ASCII.", nameof(normalisiert));
            daten[4 + i] = (byte)normalisiert[i];
        }
        return HMACSHA256.HashData(_notfallSchluessel, daten);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_schluessel);
        CryptographicOperations.ZeroMemory(_notfallSchluessel);
    }
}
