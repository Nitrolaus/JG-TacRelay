// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;

namespace Funk.Protokoll.Sprache;

/// <summary>
/// Klartext SPRACHE_HOCH (5.4) als Sicht auf den Puffer, ohne Kopie.
/// Aufbau: Frame-Nr u32, Sprechphase u16, Kennzeichen u8, Zielart u8, n u8, Netz-IDs u16[n], Opus (Rest).
/// </summary>
public readonly ref struct SpracheHoch
{
    /// <summary>Länge bis vor die Netz-IDs.</summary>
    public const int FesterTeil = 9;

    private readonly ReadOnlySpan<byte> _ziele;

    private SpracheHoch(uint frameNr, ushort sprechphase, Kennzeichen kennzeichen, Zielart zielart,
        ReadOnlySpan<byte> ziele, ReadOnlySpan<byte> opus)
    {
        FrameNr = frameNr;
        Sprechphase = sprechphase;
        Kennzeichen = kennzeichen;
        Zielart = zielart;
        _ziele = ziele;
        Opus = opus;
    }

    public uint FrameNr { get; }
    public ushort Sprechphase { get; }

    /// <summary>Wie gesendet, einschließlich reservierter Bits; der Server maskiert beim Stempeln.</summary>
    public Kennzeichen Kennzeichen { get; }
    public Zielart Zielart { get; }
    public ReadOnlySpan<byte> Opus { get; }

    public int ZielAnzahl => _ziele.Length / 2;
    public ushort Ziel(int index) => BinaryPrimitives.ReadUInt16BigEndian(_ziele.Slice(2 * index, 2));
    public bool IstEnde => (Kennzeichen & Kennzeichen.Ende) != 0;

    /// <summary>
    /// Liest und prüft die Form in der Reihenfolge aus 6.3: Zielart, n und Sortierung, Global kein Durchsageziel,
    /// ENDE- und Opus-Regeln, m &lt;= 400, TOC (5.13).
    /// </summary>
    public static Formfehler Lesen(ReadOnlySpan<byte> klartext, out SpracheHoch paket)
    {
        paket = default;
        if (klartext.Length < FesterTeil) return Formfehler.ZuKurz;

        var zielart = (Zielart)klartext[7];
        int n = klartext[8];
        if (zielart is not (Zielart.Netz or Zielart.Durchsage)) return Formfehler.ZielartUnbekannt;
        if (!ZielAnzahlGueltig(zielart, n)) return Formfehler.ZielAnzahlUngueltig;
        if (klartext.Length < FesterTeil + 2 * n) return Formfehler.ZuKurz;

        var ziele = klartext.Slice(FesterTeil, 2 * n);
        var fehler = ZielePruefen(zielart, ziele);
        if (fehler != Formfehler.Keiner) return fehler;

        var kennzeichen = (Kennzeichen)klartext[6];
        var opus = klartext[(FesterTeil + 2 * n)..];
        fehler = OpusRegeln(kennzeichen, opus, mitToc: true);
        if (fehler != Formfehler.Keiner) return fehler;

        paket = new SpracheHoch(
            BinaryPrimitives.ReadUInt32BigEndian(klartext),
            BinaryPrimitives.ReadUInt16BigEndian(klartext[4..]),
            kennzeichen, zielart, ziele, opus);
        return Formfehler.Keiner;
    }

    /// <summary>
    /// Schreibt den Klartext und gibt seine Länge zurück. Prüft die Form wie <see cref="Lesen"/>, außer dem TOC;
    /// ein Verstoß ist ein Programmierfehler des Senders und wirft.
    /// </summary>
    public static int Schreiben(Span<byte> ziel, uint frameNr, ushort sprechphase, Kennzeichen kennzeichen,
        Zielart zielart, ReadOnlySpan<ushort> ziele, ReadOnlySpan<byte> opus)
    {
        if (zielart is not (Zielart.Netz or Zielart.Durchsage))
            throw new ArgumentOutOfRangeException(nameof(zielart), zielart, "Unbekannte Zielart.");
        if (!ZielAnzahlGueltig(zielart, ziele.Length))
            throw new ArgumentException($"Unzulässige Zielanzahl {ziele.Length} für {zielart}.", nameof(ziele));
        if (((byte)kennzeichen & KennzeichenMasken.Reserviert) != 0)
            throw new ArgumentException("Reservierte Kennzeichen-Bits müssen 0 sein.", nameof(kennzeichen));

        var laenge = FesterTeil + 2 * ziele.Length + opus.Length;
        ArgumentOutOfRangeException.ThrowIfLessThan(ziel.Length, laenge, nameof(ziel));

        var zielBytes = ziel.Slice(FesterTeil, 2 * ziele.Length);
        for (var i = 0; i < ziele.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(zielBytes[(2 * i)..], ziele[i]);
        if (ZielePruefen(zielart, zielBytes) is var fz and not Formfehler.Keiner)
            throw new ArgumentException($"Ziele verletzen 5.4: {fz}.", nameof(ziele));
        if (OpusRegeln(kennzeichen, opus, mitToc: false) is var fo and not Formfehler.Keiner)
            throw new ArgumentException($"Opus verletzt 5.4: {fo}.", nameof(opus));

        BinaryPrimitives.WriteUInt32BigEndian(ziel, frameNr);
        BinaryPrimitives.WriteUInt16BigEndian(ziel[4..], sprechphase);
        ziel[6] = (byte)kennzeichen;
        ziel[7] = (byte)zielart;
        ziel[8] = (byte)ziele.Length;
        opus.CopyTo(ziel[(FesterTeil + 2 * ziele.Length)..]);
        return laenge;
    }

    /// <summary>NETZ: n = 1..16. DURCHSAGE: n = 0 (an alle) oder 1..64.</summary>
    private static bool ZielAnzahlGueltig(Zielart zielart, int n) => zielart == Zielart.Netz
        ? n is >= 1 and <= Sprachkanal.NetzZieleMax
        : n is >= 0 and <= Sprachkanal.DurchsageZieleMax;

    /// <summary>Streng aufsteigend; Global (1) ist als Durchsageziel verboten.</summary>
    private static Formfehler ZielePruefen(Zielart zielart, ReadOnlySpan<byte> ziele)
    {
        var vorige = -1;
        for (var i = 0; i < ziele.Length; i += 2)
        {
            int netz = BinaryPrimitives.ReadUInt16BigEndian(ziele[i..]);
            if (netz <= vorige) return Formfehler.ZieleNichtAufsteigend;
            if (zielart == Zielart.Durchsage && netz == Sprachkanal.GlobalNetz) return Formfehler.GlobalAlsDurchsageziel;
            vorige = netz;
        }
        return Formfehler.Keiner;
    }

    /// <summary>ENDE: m = 0 und kein PREROLL. Sonst m = 1..400 und, beim Lesen, TOC nach 5.13. Gilt hoch wie runter.</summary>
    internal static Formfehler OpusRegeln(Kennzeichen kennzeichen, ReadOnlySpan<byte> opus, bool mitToc)
    {
        if ((kennzeichen & Kennzeichen.Ende) != 0)
        {
            if (!opus.IsEmpty) return Formfehler.EndeMitOpus;
            if ((kennzeichen & Kennzeichen.Preroll) != 0) return Formfehler.EndeMitPreroll;
            return Formfehler.Keiner;
        }
        if (opus.IsEmpty) return Formfehler.OpusFehlt;
        if (opus.Length > Sprachkanal.OpusMax) return Formfehler.OpusZuLang;
        if (mitToc && OpusPruefung.Pruefen(opus) != Opusbefund.Gueltig) return Formfehler.OpusUngueltig;
        return Formfehler.Keiner;
    }
}
