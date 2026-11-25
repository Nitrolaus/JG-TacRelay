// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;

namespace Funk.Protokoll.Sprache;

/// <summary>
/// Klartext SPRACHE_RUNTER (5.5), vom Server gestempelt, als Sicht auf den Puffer ohne Kopie.
/// Aufbau: Absender u32, Frame-Nr u32, Sprechphase u16, Kennzeichen u8, Art u8, k u8, Netz-IDs u16[k], Opus (Rest).
/// </summary>
public readonly ref struct SpracheRunter
{
    /// <summary>Länge bis vor die Netz-IDs.</summary>
    public const int FesterTeil = 13;

    private readonly ReadOnlySpan<byte> _netze;

    private SpracheRunter(uint absender, uint frameNr, ushort sprechphase, Kennzeichen kennzeichen, Spruchart art,
        ReadOnlySpan<byte> netze, ReadOnlySpan<byte> opus)
    {
        Absender = absender;
        FrameNr = frameNr;
        Sprechphase = sprechphase;
        Kennzeichen = kennzeichen;
        Art = art;
        _netze = netze;
        Opus = opus;
    }

    /// <summary>Konto-ID des Sprechers, allein vom Server bestimmt.</summary>
    public uint Absender { get; }
    public uint FrameNr { get; }
    public ushort Sprechphase { get; }
    public Kennzeichen Kennzeichen { get; }
    public Spruchart Art { get; }
    public ReadOnlySpan<byte> Opus { get; }

    public int NetzAnzahl => _netze.Length / 2;

    /// <summary>Netze in der Reihenfolge aus 6.4: zuerst Global, dann nach Feldnummer des Empfängers.</summary>
    public ushort Netz(int index) => BinaryPrimitives.ReadUInt16BigEndian(_netze.Slice(2 * index, 2));
    public bool IstEnde => (Kennzeichen & Kennzeichen.Ende) != 0;

    /// <summary>
    /// Liest und prüft die Form: Art 1-3, k = 0..16, ENDE- und Opus-Regeln wie hoch (m &lt;= 400).
    /// Den Opus-Inhalt prüft der Client mit libopus (5.13); <see cref="OpusPruefung"/> steht zusätzlich bereit.
    /// </summary>
    public static Formfehler Lesen(ReadOnlySpan<byte> klartext, out SpracheRunter paket)
    {
        paket = default;
        if (klartext.Length < FesterTeil) return Formfehler.ZuKurz;

        var art = (Spruchart)klartext[11];
        int k = klartext[12];
        if (art is not (Spruchart.Netz or Spruchart.Durchsage or Spruchart.Global)) return Formfehler.ArtUnbekannt;
        if (k > Sprachkanal.RunterNetzeMax) return Formfehler.NetzAnzahlUngueltig;
        if (klartext.Length < FesterTeil + 2 * k) return Formfehler.ZuKurz;

        var kennzeichen = (Kennzeichen)klartext[10];
        var opus = klartext[(FesterTeil + 2 * k)..];
        var fehler = SpracheHoch.OpusRegeln(kennzeichen, opus, mitToc: false);
        if (fehler != Formfehler.Keiner) return fehler;

        paket = new SpracheRunter(
            BinaryPrimitives.ReadUInt32BigEndian(klartext),
            BinaryPrimitives.ReadUInt32BigEndian(klartext[4..]),
            BinaryPrimitives.ReadUInt16BigEndian(klartext[8..]),
            kennzeichen, art, klartext.Slice(FesterTeil, 2 * k), opus);
        return Formfehler.Keiner;
    }

    /// <summary>Schreibt den Klartext und gibt seine Länge zurück. Ein Formverstoß wirft.</summary>
    public static int Schreiben(Span<byte> ziel, uint absender, uint frameNr, ushort sprechphase, Kennzeichen kennzeichen,
        Spruchart art, ReadOnlySpan<ushort> netze, ReadOnlySpan<byte> opus)
    {
        if (art is not (Spruchart.Netz or Spruchart.Durchsage or Spruchart.Global))
            throw new ArgumentOutOfRangeException(nameof(art), art, "Unbekannte Art.");
        ArgumentOutOfRangeException.ThrowIfGreaterThan(netze.Length, Sprachkanal.RunterNetzeMax, nameof(netze));
        if (((byte)kennzeichen & KennzeichenMasken.Reserviert) != 0)
            throw new ArgumentException("Reservierte Kennzeichen-Bits müssen 0 sein.", nameof(kennzeichen));
        if (SpracheHoch.OpusRegeln(kennzeichen, opus, mitToc: false) is var fo and not Formfehler.Keiner)
            throw new ArgumentException($"Opus verletzt 5.5: {fo}.", nameof(opus));

        var laenge = FesterTeil + 2 * netze.Length + opus.Length;
        ArgumentOutOfRangeException.ThrowIfLessThan(ziel.Length, laenge, nameof(ziel));

        BinaryPrimitives.WriteUInt32BigEndian(ziel, absender);
        BinaryPrimitives.WriteUInt32BigEndian(ziel[4..], frameNr);
        BinaryPrimitives.WriteUInt16BigEndian(ziel[8..], sprechphase);
        ziel[10] = (byte)kennzeichen;
        ziel[11] = (byte)art;
        ziel[12] = (byte)netze.Length;
        for (var i = 0; i < netze.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(ziel[(FesterTeil + 2 * i)..], netze[i]);
        opus.CopyTo(ziel[(FesterTeil + 2 * netze.Length)..]);
        return laenge;
    }

    /// <summary>
    /// Stempel nach 6.4: Absender, Art und Netze vom Server; Frame-Nr, Sprechphase und Opus vom Sender übernommen;
    /// vom Kennzeichen nur ENDE, PREROLL und VA, BEGINN setzt der Server je Empfänger.
    /// </summary>
    public static int Stempeln(Span<byte> ziel, uint absender, SpracheHoch hoch, bool beginn, Spruchart art,
        ReadOnlySpan<ushort> netze)
    {
        var kennzeichen = (Kennzeichen)((byte)hoch.Kennzeichen & KennzeichenMasken.Uebernommen);
        if (beginn) kennzeichen |= Kennzeichen.Beginn;
        return Schreiben(ziel, absender, hoch.FrameNr, hoch.Sprechphase, kennzeichen, art, netze, hoch.Opus);
    }
}
