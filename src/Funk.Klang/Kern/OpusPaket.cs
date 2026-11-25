// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Nativ;

namespace Funk.Klang.Kern;

/// <summary>Kodiermodus eines Opus-Rahmens laut TOC-Byte (RFC 6716, Abschnitt 3.1).</summary>
public enum OpusModus
{
    /// <summary>Nur SILK (LPC), Konfigurationen 0 bis 11.</summary>
    Silk,
    /// <summary>SILK unten und CELT oben, Konfigurationen 12 bis 15.</summary>
    Hybrid,
    /// <summary>Nur CELT (MDCT), Konfigurationen 16 bis 31.</summary>
    Celt,
}

/// <summary>Audiobandbreite laut TOC-Byte; die Werte entsprechen OPUS_BANDWIDTH_*.</summary>
public enum OpusBandbreite
{
    /// <summary>4 kHz.</summary>
    Schmal = 1101,
    /// <summary>6 kHz.</summary>
    Mittel = 1102,
    /// <summary>8 kHz.</summary>
    Breit = 1103,
    /// <summary>12 kHz.</summary>
    SuperBreit = 1104,
    /// <summary>20 kHz.</summary>
    Voll = 1105,
}

/// <summary>
/// Das erste Byte eines Opus-Pakets (TOC), rein verwaltet ausgewertet nach RFC 6716, Tabelle 2.
/// </summary>
public readonly record struct OpusToc(byte Wert)
{
    /// <summary>Konfigurationsnummer 0 bis 31 (obere fünf Bit).</summary>
    public int Konfiguration => Wert >> 3;

    /// <summary>Stereo-Bit.</summary>
    public bool Stereo => (Wert & 0x04) != 0;

    /// <summary>Rahmenanzahl-Code c: 0 ein Rahmen, 1 zwei gleich lange, 2 zwei verschieden lange, 3 beliebig viele.</summary>
    public int RahmenCode => Wert & 0x03;

    public OpusModus Modus => Konfiguration switch
    {
        < 12 => OpusModus.Silk,
        < 16 => OpusModus.Hybrid,
        _ => OpusModus.Celt,
    };

    public OpusBandbreite Bandbreite => Konfiguration switch
    {
        < 4 => OpusBandbreite.Schmal,
        < 8 => OpusBandbreite.Mittel,
        < 12 => OpusBandbreite.Breit,
        < 14 => OpusBandbreite.SuperBreit,
        < 16 => OpusBandbreite.Voll,
        < 20 => OpusBandbreite.Schmal,
        < 24 => OpusBandbreite.Breit,
        < 28 => OpusBandbreite.SuperBreit,
        _ => OpusBandbreite.Voll,
    };

    /// <summary>Dauer eines Rahmens in Samples bei 48 kHz.</summary>
    public int SamplesJeRahmen => Modus switch
    {
        OpusModus.Silk => (Konfiguration & 3) switch { 0 => 480, 1 => 960, 2 => 1920, _ => 2880 },
        OpusModus.Hybrid => (Konfiguration & 1) == 0 ? 480 : 960,
        _ => 120 << (Konfiguration & 3),
    };
}

/// <summary>Auskünfte über Opus-Pakete, ohne sie zu dekodieren.</summary>
public static unsafe class OpusPaket
{
    /// <summary>Liest das TOC-Byte. Wirft bei leerem Paket.</summary>
    public static OpusToc Toc(ReadOnlySpan<byte> paket)
    {
        LaengenPruefung.Mindestens(paket.Length, 1, nameof(paket));
        return new OpusToc(paket[0]);
    }

    /// <summary>
    /// Anzahl Samples bei 48 kHz über alle Rahmen (120 bis 5760), oder ein negativer Opus-Fehlercode.
    /// libopus meldet für Code 3 mit null Rahmen 0; das gilt hier als OPUS_INVALID_PACKET.
    /// </summary>
    public static int AnzahlSamples(ReadOnlySpan<byte> paket)
    {
        if (paket.IsEmpty) return OpusNativ.BadArg;
        int samples;
        fixed (byte* p = paket) samples = OpusNativ.PaketSamples(p, paket.Length, OpusKodierer.Abtastrate);
        return samples == 0 ? OpusNativ.InvalidPacket : samples;
    }

    /// <summary>
    /// Anzahl Rahmen im Paket (1 bis 48), oder ein negativer Opus-Fehlercode. libopus liest die Zahl bei
    /// Code 3 ungeprüft aus dem zweiten Byte (0 bis 63); hier gilt sie nur, wenn das Paket höchstens
    /// 120 ms lang ist und mindestens einen Rahmen hat (RFC 6716, 3.2.5). Eine Rahmenzahl von hier
    /// darf also zum Bemessen von Puffern dienen.
    /// </summary>
    public static int AnzahlRahmen(ReadOnlySpan<byte> paket)
    {
        int samples = AnzahlSamples(paket);
        if (samples < 0) return samples;
        fixed (byte* p = paket) return OpusNativ.PaketRahmen(p, paket.Length);
    }

    /// <summary>Bandbreite laut libopus (Gegenprobe zu <see cref="OpusToc.Bandbreite"/>).</summary>
    public static OpusBandbreite BandbreiteNativ(ReadOnlySpan<byte> paket)
    {
        LaengenPruefung.Mindestens(paket.Length, 1, nameof(paket));
        fixed (byte* p = paket) return (OpusBandbreite)OpusNativ.PaketBandbreite(p);
    }

    /// <summary>Samples je Rahmen bei 48 kHz laut libopus (Gegenprobe zu <see cref="OpusToc.SamplesJeRahmen"/>).</summary>
    public static int SamplesJeRahmenNativ(ReadOnlySpan<byte> paket)
    {
        LaengenPruefung.Mindestens(paket.Length, 1, nameof(paket));
        fixed (byte* p = paket) return OpusNativ.PaketSamplesJeRahmen(p, OpusKodierer.Abtastrate);
    }

    /// <summary>
    /// Trägt das Paket In-band-FEC (LBRR) für den Rahmen davor? Nur SILK und Hybrid können das.
    /// Leeres oder ungültiges Paket: false.
    /// </summary>
    public static bool HatLbrr(ReadOnlySpan<byte> paket)
    {
        if (paket.IsEmpty) return false;
        fixed (byte* p = paket) return OpusNativ.PaketHatLbrr(p, paket.Length) > 0;
    }
}
