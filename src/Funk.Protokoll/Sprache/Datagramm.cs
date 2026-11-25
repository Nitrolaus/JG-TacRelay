// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;

namespace Funk.Protokoll.Sprache;

/// <summary>
/// Versiegeln und Öffnen eines Datagramms (5.2, 5.7): Kopf (16, AAD) || Chiffrat (n) || Tag (16).
/// Reine Kryptoschicht ohne Zustand; Epochen, Zähler und Replay-Fenster liegen in den Kanälen.
/// </summary>
public static class Datagramm
{
    public static int Laenge(int klartextLaenge) => klartextLaenge + Sprachkanal.Ueberhang;

    /// <summary>Schreibt Kopf, Chiffrat und Tag nach <paramref name="ziel"/> und gibt die Datagrammlänge zurück.</summary>
    public static int Versiegeln(RichtungsSchluessel schluessel, in SprachKopf kopf, ReadOnlySpan<byte> klartext, Span<byte> ziel)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        if (kopf.Epoche != schluessel.Epoche)
            throw new ArgumentException($"Kopf nennt Epoche {kopf.Epoche}, der Schlüssel gehört zu {schluessel.Epoche}.", nameof(kopf));
        var laenge = Laenge(klartext.Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(ziel.Length, laenge, nameof(ziel));

        kopf.Schreiben(ziel);
        schluessel.Versiegeln(kopf.Zaehler, ziel[..Sprachkanal.KopfLaenge], klartext,
            ziel.Slice(Sprachkanal.KopfLaenge, klartext.Length),
            ziel.Slice(Sprachkanal.KopfLaenge + klartext.Length, Sprachkanal.TagLaenge));
        return laenge;
    }

    /// <summary>
    /// Entschlüsselt mit dem Zähler aus dem Kopf und dem Kopf als AAD. Prüft nichts außer dem Tag;
    /// die Reihenfolge der Empfangsprüfung regelt <see cref="Empfangspruefung"/>.
    /// </summary>
    public static bool Oeffnen(RichtungsSchluessel schluessel, ReadOnlySpan<byte> datagramm, Span<byte> klartext)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        if (datagramm.Length < Sprachkanal.Ueberhang) return false;
        var n = datagramm.Length - Sprachkanal.Ueberhang;
        ArgumentOutOfRangeException.ThrowIfLessThan(klartext.Length, n, nameof(klartext));

        var zaehler = BinaryPrimitives.ReadUInt64BigEndian(datagramm[8..]);
        return schluessel.Oeffnen(zaehler, datagramm[..Sprachkanal.KopfLaenge],
            datagramm.Slice(Sprachkanal.KopfLaenge, n), datagramm[^Sprachkanal.TagLaenge..], klartext[..n]);
    }
}
