// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Buffers.Binary;

namespace Funk.Protokoll.Steuerung;

/// <summary>
/// Kopf eines Steuerrahmens (4.2): Länge u32 (8 + Body), Rahmenversion u8, Flags u8, Typ u16, Anfrage-Nr u32,
/// alles big-endian.
/// </summary>
public readonly record struct RahmenKopf(uint Laenge, byte Version, byte Flags, ushort Typ, uint AnfrageNr)
{
    /// <summary>Länge des Bodys.</summary>
    public int BodyLaenge => (int)(Laenge - Rahmen.LaengeOhneBody);

    /// <summary>Länge des ganzen Rahmens samt Längenfeld.</summary>
    public int GesamtLaenge => (int)Laenge + 4;

    /// <summary>Flag ANTWORT gesetzt.</summary>
    public bool IstAntwort => (Flags & Rahmen.FlagAntwort) != 0;

    /// <summary>Art nach 4.2: ANTWORT, sonst Anfrage-Nr 0 = Push, sonst Anfrage.</summary>
    public Nachrichtenart Art => IstAntwort ? Nachrichtenart.Antwort : AnfrageNr == 0 ? Nachrichtenart.Push : Nachrichtenart.Anfrage;

    /// <summary>Typ als Aufzählung (auch für unbekannte Nummern).</summary>
    public Nachrichtentyp Nachricht => (Nachrichtentyp)Typ;
}

/// <summary>Ergebnis der Kopfprüfung.</summary>
public enum RahmenStatus : byte
{
    /// <summary>Vollständiger, gültiger Rahmen.</summary>
    Fertig,
    /// <summary>Es fehlen noch Bytes; nichts verbraucht.</summary>
    Unvollstaendig,
    /// <summary>Verstoß; Fehlercode steht daneben, die Verbindung wird getrennt.</summary>
    Fehler,
}

/// <summary>Framing des Steuerkanals nach 4.2: Konstanten, Kopf lesen und prüfen, Rahmen schreiben.</summary>
public static class Rahmen
{
    /// <summary>Kopf samt Längenfeld.</summary>
    public const int KopfLaenge = 12;
    /// <summary>Das Längenfeld zählt Version, Flags, Typ und Anfrage-Nr mit.</summary>
    public const int LaengeOhneBody = 8;
    /// <summary>Rahmenversion.</summary>
    public const byte Version = 1;
    /// <summary>Bit 0: ANTWORT.</summary>
    public const byte FlagAntwort = 0x01;
    /// <summary>Bits 1..7 müssen 0 sein.</summary>
    public const byte ReservierteFlags = 0xFE;
    /// <summary>Anfragen des Servers haben Bit 31 gesetzt.</summary>
    public const uint ServerAnfrageBit = 0x8000_0000;
    /// <summary>Höchste Anfrage-Nr des Clients.</summary>
    public const uint ClientAnfrageMax = 0x7FFF_FFFF;

    /// <summary>
    /// Prüft das Längenfeld (erste 4 Byte), bevor irgendetwas vom Body gelesen oder Speicher angefordert wird.
    /// Unter 8 ergibt <c>PROTOKOLL</c>, über <paramref name="maxLaenge"/> <c>ZU_GROSS</c>.
    /// </summary>
    public static Fehlercode? LaengePruefen(uint laenge, int maxLaenge)
    {
        if (laenge < LaengeOhneBody) return Fehlercode.Protokoll;
        if (laenge > (uint)maxLaenge) return Fehlercode.ZuGross;
        return null;
    }

    /// <summary>Prüft Rahmenversion und reservierte Flag-Bits.</summary>
    public static Fehlercode? VersionUndFlagsPruefen(byte version, byte flags)
    {
        if (version != Version) return Fehlercode.Protokoll;
        if ((flags & ReservierteFlags) != 0) return Fehlercode.Protokoll;
        return null;
    }

    /// <summary>Liest und prüft einen vollständigen 12-Byte-Kopf.</summary>
    public static Fehlercode? KopfLesen(ReadOnlySpan<byte> kopf, int maxLaenge, out RahmenKopf ergebnis)
    {
        if (kopf.Length < KopfLaenge) throw new ArgumentException("Kopf zu kurz.", nameof(kopf));
        ergebnis = new RahmenKopf(
            BinaryPrimitives.ReadUInt32BigEndian(kopf),
            kopf[4],
            kopf[5],
            BinaryPrimitives.ReadUInt16BigEndian(kopf[6..]),
            BinaryPrimitives.ReadUInt32BigEndian(kopf[8..]));
        return LaengePruefen(ergebnis.Laenge, maxLaenge) ?? VersionUndFlagsPruefen(ergebnis.Version, ergebnis.Flags);
    }

    /// <summary>Schreibt den 12-Byte-Kopf für einen Body der Länge <paramref name="bodyLaenge"/>.</summary>
    public static void KopfSchreiben(Span<byte> ziel, ushort typ, byte flags, uint anfrageNr, int bodyLaenge)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bodyLaenge);
        BinaryPrimitives.WriteUInt32BigEndian(ziel, checked((uint)(bodyLaenge + LaengeOhneBody)));
        ziel[4] = Version;
        ziel[5] = flags;
        BinaryPrimitives.WriteUInt16BigEndian(ziel[6..], typ);
        BinaryPrimitives.WriteUInt32BigEndian(ziel[8..], anfrageNr);
    }

    /// <summary>Baut einen vollständigen Rahmen in ein neues Byte-Feld. Für Tests und Werkzeuge.</summary>
    public static byte[] Bauen(ushort typ, byte flags, uint anfrageNr, ReadOnlySpan<byte> body)
    {
        var rahmen = new byte[KopfLaenge + body.Length];
        KopfSchreiben(rahmen, typ, flags, anfrageNr, body.Length);
        body.CopyTo(rahmen.AsSpan(KopfLaenge));
        return rahmen;
    }

    /// <summary>Baut einen JSON-Rahmen in ein neues Byte-Feld.</summary>
    public static byte[] BauenJson<T>(Nachrichtentyp typ, byte flags, uint anfrageNr, T body) =>
        Bauen((ushort)typ, flags, anfrageNr, SteuerJson.Serialisieren(body));

    /// <summary>
    /// Versucht, einen Rahmen vom Anfang von <paramref name="puffer"/> zu lesen (PipeReader-artige Puffer).
    /// Die Grenzen werden geprüft, sobald die jeweiligen Kopfbytes da sind, also bevor auf den Body gewartet wird.
    /// Bei <see cref="RahmenStatus.Fertig"/> zeigt <paramref name="puffer"/> hinter den Rahmen und
    /// <paramref name="body"/> auf den Body (gültig, solange der Puffer gilt).
    /// </summary>
    public static RahmenStatus VersucheLesen(ref ReadOnlySequence<byte> puffer, int maxLaenge,
        out RahmenKopf kopf, out ReadOnlySequence<byte> body, out Fehlercode fehler)
    {
        kopf = default;
        body = default;
        fehler = default;
        Span<byte> k = stackalloc byte[KopfLaenge];
        var vorhanden = (int)Math.Min(puffer.Length, KopfLaenge);
        puffer.Slice(0, vorhanden).CopyTo(k);

        if (vorhanden >= 4 && LaengePruefen(BinaryPrimitives.ReadUInt32BigEndian(k), maxLaenge) is { } f1)
        {
            fehler = f1;
            return RahmenStatus.Fehler;
        }
        if ((vorhanden >= 5 && k[4] != Version) || (vorhanden >= 6 && (k[5] & ReservierteFlags) != 0))
        {
            fehler = Fehlercode.Protokoll;
            return RahmenStatus.Fehler;
        }
        if (vorhanden < KopfLaenge) return RahmenStatus.Unvollstaendig;

        KopfLesen(k, maxLaenge, out kopf);
        if (puffer.Length < kopf.GesamtLaenge) return RahmenStatus.Unvollstaendig;
        body = puffer.Slice(KopfLaenge, kopf.BodyLaenge);
        puffer = puffer.Slice(kopf.GesamtLaenge);
        return RahmenStatus.Fertig;
    }
}
