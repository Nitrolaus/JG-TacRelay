// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using Funk.Protokoll.Schutz;

namespace Funk.Protokoll.Sprache;

/// <summary>
/// Schlüssel und statischer IV einer Richtung in einer Epoche (5.7, 5.8): AEAD_AES_256_GCM mit 32-Byte-Schlüssel,
/// 12-Byte-Nonce nach RFC 8446 und 16-Byte-Tag. Besitzt genau eine <see cref="AesGcm"/>-Instanz, die nur der
/// besitzende Thread benutzt (5.14). <see cref="Dispose"/> beim Verwerfen der Epoche.
/// </summary>
public sealed class RichtungsSchluessel : IDisposable
{
    private readonly AesGcm _aes;
    private readonly byte[] _iv = new byte[Sprachkanal.IvLaenge];

    public RichtungsSchluessel(byte epoche, ReadOnlySpan<byte> schluessel, ReadOnlySpan<byte> iv)
    {
        if (schluessel.Length != Sprachkanal.SchluesselLaenge)
            throw new ArgumentException($"Schlüssel muss {Sprachkanal.SchluesselLaenge} Byte lang sein.", nameof(schluessel));
        if (iv.Length != Sprachkanal.IvLaenge)
            throw new ArgumentException($"IV muss {Sprachkanal.IvLaenge} Byte lang sein.", nameof(iv));
        Epoche = epoche;
        _aes = new AesGcm(schluessel, Sprachkanal.TagLaenge);
        iv.CopyTo(_iv);
    }

    public byte Epoche { get; }

    public bool Verworfen { get; private set; }

    /// <summary>Verschlüsselt mit der Nonce aus IV und Zähler; <paramref name="aad"/> ist im Sprachkanal der Kopf.</summary>
    public void Versiegeln(ulong zaehler, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> klartext, Span<byte> chiffrat, Span<byte> tag)
    {
        ObjectDisposedException.ThrowIf(Verworfen, this);
        Span<byte> nonce = stackalloc byte[Nonce.Laenge];
        Nonce.Bilden(_iv, zaehler, nonce);
        _aes.Encrypt(nonce, klartext, chiffrat, tag, aad);
    }

    /// <summary>
    /// Entschlüsselt und prüft den Tag. Bei falschem Tag false; <paramref name="klartext"/> ist dann genullt.
    /// <see cref="AesGcm"/> kennt kein Entschlüsseln ohne Ausnahme; nur dieser Fehlerfall allokiert (Budget begrenzt ihn, 5.9 Schritt 4).
    /// </summary>
    public bool Oeffnen(ulong zaehler, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> chiffrat, ReadOnlySpan<byte> tag, Span<byte> klartext)
    {
        ObjectDisposedException.ThrowIf(Verworfen, this);
        Span<byte> nonce = stackalloc byte[Nonce.Laenge];
        Nonce.Bilden(_iv, zaehler, nonce);
        try
        {
            _aes.Decrypt(nonce, chiffrat, tag, klartext, aad);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Verworfen) return;
        Verworfen = true;
        _aes.Dispose();
        CryptographicOperations.ZeroMemory(_iv);
    }
}

/// <summary>
/// Schlüsselmaterial einer Epoche (5.8), 88 Byte in der Reihenfolge des VoiceKeys-Bodys ab Offset 2 (4.5):
/// K_hoch 32, IV_hoch 12, K_runter 32, IV_runter 12. Der Aufrufer nullt seine Puffer nach Gebrauch.
/// </summary>
public static class Schluesselmaterial
{
    public const int Laenge = 2 * (Sprachkanal.SchluesselLaenge + Sprachkanal.IvLaenge);

    private const int KHoch = 0;
    private const int IvHoch = KHoch + Sprachkanal.SchluesselLaenge;
    private const int KRunter = IvHoch + Sprachkanal.IvLaenge;
    private const int IvRunter = KRunter + Sprachkanal.SchluesselLaenge;

    /// <summary>Neues Material aus <see cref="RandomNumberGenerator"/> (nur im Server).</summary>
    public static void Erzeugen(Span<byte> ziel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ziel.Length, Laenge, nameof(ziel));
        RandomNumberGenerator.Fill(ziel[..Laenge]);
    }

    public static RichtungsSchluessel Hoch(byte epoche, ReadOnlySpan<byte> material) =>
        new(epoche, Pruefen(material).Slice(KHoch, Sprachkanal.SchluesselLaenge), material.Slice(IvHoch, Sprachkanal.IvLaenge));

    public static RichtungsSchluessel Runter(byte epoche, ReadOnlySpan<byte> material) =>
        new(epoche, Pruefen(material).Slice(KRunter, Sprachkanal.SchluesselLaenge), material.Slice(IvRunter, Sprachkanal.IvLaenge));

    public static RichtungsSchluessel Fuer(Richtung richtung, byte epoche, ReadOnlySpan<byte> material) =>
        richtung == Richtung.Hoch ? Hoch(epoche, material) : Runter(epoche, material);

    private static ReadOnlySpan<byte> Pruefen(ReadOnlySpan<byte> material) => material.Length == Laenge
        ? material
        : throw new ArgumentException($"Schlüsselmaterial muss {Laenge} Byte lang sein.", nameof(material));
}
