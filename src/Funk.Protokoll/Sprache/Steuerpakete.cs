// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Funk.Protokoll.Sprache;

/// <summary>
/// Klartexte der festen Typen 0x01-0x03 (5.3), je 16 Byte. Füllbytes sind 0; die Formate sind kanonisch (Z8),
/// deshalb gilt ein Klartext mit anderen Füllbytes als ungültig.
/// </summary>
public static class Steuerpakete
{
    public const int Laenge = Sprachkanal.FesterKlartext;

    /// <summary>ANMELDUNG: 16 Byte 0x00.</summary>
    public static void AnmeldungSchreiben(Span<byte> ziel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ziel.Length, Laenge, nameof(ziel));
        ziel[..Laenge].Clear();
    }

    public static bool AnmeldungGueltig(ReadOnlySpan<byte> klartext) =>
        klartext.Length == Laenge && !klartext.ContainsAnyExcept((byte)0);

    /// <summary>Challenge für PFAD_PRUEFUNG: 16 Zufallsbytes (5.10).</summary>
    public static void ChallengeErzeugen(Span<byte> ziel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ziel.Length, Laenge, nameof(ziel));
        RandomNumberGenerator.Fill(ziel[..Laenge]);
    }

    /// <summary>Vergleicht eine PFAD_ANTWORT mit der gesendeten Challenge in fester Zeit (5.10).</summary>
    public static bool ChallengeGleich(ReadOnlySpan<byte> klartext, ReadOnlySpan<byte> challenge) =>
        klartext.Length == Laenge && challenge.Length == Laenge && CryptographicOperations.FixedTimeEquals(klartext, challenge);
}

/// <summary>KEEPALIVE (0x04): nr u32, zeitMs u32 (Client-Uhr, monoton, mod 2^32), 8 Byte 0x00.</summary>
public readonly record struct Keepalive(uint Nr, uint ZeitMs)
{
    public void Schreiben(Span<byte> ziel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ziel.Length, Steuerpakete.Laenge, nameof(ziel));
        BinaryPrimitives.WriteUInt32BigEndian(ziel, Nr);
        BinaryPrimitives.WriteUInt32BigEndian(ziel[4..], ZeitMs);
        ziel[8..Steuerpakete.Laenge].Clear();
    }

    public static bool TryLesen(ReadOnlySpan<byte> klartext, out Keepalive paket)
    {
        paket = default;
        if (klartext.Length != Steuerpakete.Laenge || klartext[8..].ContainsAnyExcept((byte)0)) return false;
        paket = new Keepalive(BinaryPrimitives.ReadUInt32BigEndian(klartext), BinaryPrimitives.ReadUInt32BigEndian(klartext[4..]));
        return true;
    }
}

/// <summary>KEEPALIVE_ECHO (0x05): nr u32, zeitMs u32 (Echo), serverZeitMs u32 (monoton, mod 2^32), 4 Byte 0x00.</summary>
public readonly record struct KeepaliveEcho(uint Nr, uint ZeitMs, uint ServerZeitMs)
{
    public void Schreiben(Span<byte> ziel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ziel.Length, Steuerpakete.Laenge, nameof(ziel));
        BinaryPrimitives.WriteUInt32BigEndian(ziel, Nr);
        BinaryPrimitives.WriteUInt32BigEndian(ziel[4..], ZeitMs);
        BinaryPrimitives.WriteUInt32BigEndian(ziel[8..], ServerZeitMs);
        ziel[12..Steuerpakete.Laenge].Clear();
    }

    public static bool TryLesen(ReadOnlySpan<byte> klartext, out KeepaliveEcho paket)
    {
        paket = default;
        if (klartext.Length != Steuerpakete.Laenge || klartext[12..].ContainsAnyExcept((byte)0)) return false;
        paket = new KeepaliveEcho(
            BinaryPrimitives.ReadUInt32BigEndian(klartext),
            BinaryPrimitives.ReadUInt32BigEndian(klartext[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(klartext[8..]));
        return true;
    }
}
