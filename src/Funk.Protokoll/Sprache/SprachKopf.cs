// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;

namespace Funk.Protokoll.Sprache;

/// <summary>
/// Gemeinsamer Kopf aller Sprachdatagramme (5.2), 16 Byte, vollständig AAD.
/// Die Sitzungs-ID ist öffentlich und dient nur der Zuordnung, nie der Authentisierung.
/// </summary>
public readonly record struct SprachKopf(byte Version, PaketTyp Typ, byte Epoche, byte Reserviert, uint SitzungsId, ulong Zaehler)
{
    /// <summary>Kopf zum Senden: Version 1, reserviert 0.</summary>
    public SprachKopf(PaketTyp typ, byte epoche, uint sitzungsId, ulong zaehler)
        : this(Sprachkanal.Version, typ, epoche, 0, sitzungsId, zaehler)
    {
    }

    public void Schreiben(Span<byte> ziel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ziel.Length, Sprachkanal.KopfLaenge, nameof(ziel));
        ziel[0] = Version;
        ziel[1] = (byte)Typ;
        ziel[2] = Epoche;
        ziel[3] = Reserviert;
        BinaryPrimitives.WriteUInt32BigEndian(ziel[4..], SitzungsId);
        BinaryPrimitives.WriteUInt64BigEndian(ziel[8..], Zaehler);
    }

    /// <summary>Liest die 16 Kopfbytes ohne jede Prüfung; geprüft wird in <see cref="Empfangspruefung"/>.</summary>
    public static SprachKopf Lesen(ReadOnlySpan<byte> quelle)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quelle.Length, Sprachkanal.KopfLaenge, nameof(quelle));
        return new SprachKopf(
            quelle[0],
            (PaketTyp)quelle[1],
            quelle[2],
            quelle[3],
            BinaryPrimitives.ReadUInt32BigEndian(quelle[4..]),
            BinaryPrimitives.ReadUInt64BigEndian(quelle[8..]));
    }
}
