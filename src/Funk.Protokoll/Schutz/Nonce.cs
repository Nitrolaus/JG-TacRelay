// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;

namespace Funk.Protokoll.Schutz;

/// <summary>
/// Nonce für AES-GCM nach RFC 8446 Abschnitt 5.3: der 64-Bit-Zähler in Netzwerk-Byte-Reihenfolge,
/// links mit Nullen auf 12 Byte aufgefüllt und mit dem festen IV der Richtung verknüpft (XOR).
/// Solange der Zähler je Schlüssel nie wiederkehrt, wiederholt sich auch die Nonce nie.
/// </summary>
public static class Nonce
{
    public const int Laenge = 12;

    public static void Bilden(ReadOnlySpan<byte> iv, ulong zaehler, Span<byte> ziel)
    {
        if (iv.Length != Laenge) throw new ArgumentException($"IV muss {Laenge} Byte lang sein.", nameof(iv));
        if (ziel.Length != Laenge) throw new ArgumentException($"Ziel muss {Laenge} Byte lang sein.", nameof(ziel));

        iv.CopyTo(ziel);
        var hinten = ziel[4..];
        var alt = BinaryPrimitives.ReadUInt64BigEndian(hinten);
        BinaryPrimitives.WriteUInt64BigEndian(hinten, alt ^ zaehler);
    }
}
