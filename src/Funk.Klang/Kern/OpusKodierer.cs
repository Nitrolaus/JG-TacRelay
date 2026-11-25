// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Nativ;

namespace Funk.Klang.Kern;

/// <summary>Opus-Anwendungsmodus. Welcher für die Org besser klingt, entscheidet der Hörtest (ENTWURF, Codec).</summary>
public enum OpusAnwendung
{
    /// <summary>OPUS_APPLICATION_VOIP: Sprachverständlichkeit zuerst.</summary>
    Voip = 2048,
    /// <summary>OPUS_APPLICATION_AUDIO: Treue zum Eingangssignal zuerst.</summary>
    Audio = 2049,
}

/// <summary>Beim Kodierer zurückgelesene Einstellungen (Diagnose und Tests).</summary>
/// <param name="Anwendung">OPUS_APPLICATION_*.</param>
/// <param name="Bitrate">Zielbitrate in bit/s.</param>
/// <param name="Vbr">Variable Bitrate an.</param>
/// <param name="VbrBeschraenkt">Constrained VBR an.</param>
/// <param name="Komplexitaet">0 bis 10.</param>
/// <param name="MaxBandbreite">OPUS_BANDWIDTH_*.</param>
/// <param name="InbandFec">OPUS_SET_INBAND_FEC-Wert (0, 1 oder 2).</param>
/// <param name="VerlustHinweis">Erwarteter Paketverlust in Prozent.</param>
/// <param name="Dtx">Discontinuous Transmission an.</param>
/// <param name="Signal">OPUS_SIGNAL_*.</param>
/// <param name="RahmenDauer">OPUS_FRAMESIZE_*.</param>
/// <param name="Lookahead">Vorlauf des Kodierers in Samples bei 48 kHz.</param>
public readonly record struct OpusKodiererEinstellungen(
    int Anwendung, int Bitrate, bool Vbr, bool VbrBeschraenkt, int Komplexitaet, int MaxBandbreite,
    int InbandFec, int VerlustHinweis, bool Dtx, int Signal, int RahmenDauer, int Lookahead);

/// <summary>
/// Opus-Kodierer für 48 kHz mono in 20-ms-Rahmen, eingestellt nach ENTWURF (Codec und Übertragung):
/// VBR (constrained) mit 48 kbit/s, Komplexität 10, höchstens Fullband, In-band-FEC an, DTX aus,
/// Signal VOICE. Der Verlusthinweis startet bei 0 und kommt aus den Empfängerberichten.
/// </summary>
/// <remarks>
/// Threads: <see cref="Kodieren"/> immer aus demselben Thread. <see cref="SetzeVerlustHinweis"/> und
/// <see cref="SetzeBitrate"/> dürfen aus jedem Thread kommen; sie merken den Wert nur vor, der
/// Kodier-Thread übernimmt ihn vor dem nächsten Rahmen. So greift nie ein zweiter Thread in den
/// nativen Zustand, und es braucht keine Sperre. <see cref="EinstellungenLesen"/> nicht gleichzeitig
/// mit <see cref="Kodieren"/> aufrufen.
/// </remarks>
public sealed unsafe class OpusKodierer : IKodierer
{
    public const int Abtastrate = 48000;
    public const int RahmenSamples = 960;
    /// <summary>Größtes Paket mit einem Rahmen (TOC plus 1275 Byte); ein Ziel ab dieser Länge begrenzt nie.</summary>
    public const int MaxPaketBytes = 1276;
    public const int StandardBitrate = 48000;
    public const int MinBitrate = 6000;
    public const int MaxBitrate = 510000;
    public const int Komplexitaet = 10;

    private readonly OpusKodiererHandle _handle;
    private volatile int _verlustSoll;
    private int _verlustIst;
    private volatile int _bitrateSoll;
    private int _bitrateIst;
    private long _kodierteRahmen;

    public OpusKodierer(OpusAnwendung anwendung = OpusAnwendung.Voip, int bitrate = StandardBitrate)
    {
        if (anwendung is not (OpusAnwendung.Voip or OpusAnwendung.Audio))
            throw new ArgumentOutOfRangeException(nameof(anwendung), anwendung, "Nur Voip oder Audio.");
        ArgumentOutOfRangeException.ThrowIfLessThan(bitrate, MinBitrate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitrate, MaxBitrate);

        _handle = OpusNativ.KodiererErstellen(Abtastrate, 1, (int)anwendung, out int fehler);
        if (_handle.IsInvalid || fehler != OpusNativ.Ok)
        {
            _handle.Dispose();
            throw new InvalidOperationException("opus_encoder_create: " + OpusNativ.Text(fehler));
        }
        try
        {
            Setzen(OpusNativ.SetBitrate, bitrate);
            Setzen(OpusNativ.SetVbr, 1);
            Setzen(OpusNativ.SetVbrConstraint, 1);
            Setzen(OpusNativ.SetComplexity, Komplexitaet);
            Setzen(OpusNativ.SetMaxBandwidth, OpusNativ.BandwidthFullband);
            Setzen(OpusNativ.SetExpertFrameDuration, OpusNativ.Framesize20Ms);
            Setzen(OpusNativ.SetInbandFec, 1);
            Setzen(OpusNativ.SetPacketLossPerc, 0);
            Setzen(OpusNativ.SetDtx, 0);
            Setzen(OpusNativ.SetSignal, OpusNativ.SignalVoice);
            Lookahead = Lesen(OpusNativ.GetLookahead);
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
        Anwendung = anwendung;
        _bitrateSoll = _bitrateIst = bitrate;
    }

    public OpusAnwendung Anwendung { get; }

    /// <summary>Vorlauf des Kodierers in Samples: um so viel ist die dekodierte Ausgabe später dran.</summary>
    public int Lookahead { get; }

    /// <summary>Anzahl erfolgreich kodierter Rahmen (zählt nur echte native Aufrufe).</summary>
    public long KodierteRahmen => Volatile.Read(ref _kodierteRahmen);

    /// <summary>
    /// Kodiert genau 960 Samples (20 ms, mono, float in [-1, 1]) in <paramref name="ziel"/>. Die Länge des Ziels
    /// geht als max_data_bytes an libopus (höchstens <see cref="MaxPaketBytes"/>): Braucht ein Rahmen mehr, senkt
    /// libopus die Bitrate dieses einen Rahmens, bis er passt; das Paket bleibt gültig. So gibt die
    /// Sendeaufbereitung die 400 Byte aus FUNKPROTOKOLL-v1 5.6 vor, bei 48 kbit/s sind es ohnehin 120 bis 160.
    /// Rückgabe: Paketlänge in Byte (ab 1). Allokiert nicht und sperrt nicht. Wirft nur bei falschen Längen (vor dem
    /// nativen Aufruf, ein leeres Ziel eingeschlossen) oder wenn libopus selbst einen Fehler meldet.
    /// Ein Rahmen mit NaN, ±Unendlich oder absurder Energie (Summe der Quadrate ab 1e9) wird von
    /// libopus selbst als Stille kodiert, der Zustand bleibt heil (Test Opus_NichtEndlicherRahmen_WirdStill).
    /// </summary>
    public int Kodieren(ReadOnlySpan<float> pcm20ms, Span<byte> ziel)
    {
        LaengenPruefung.Genau(pcm20ms.Length, RahmenSamples, nameof(pcm20ms));
        LaengenPruefung.Mindestens(ziel.Length, 1, nameof(ziel));
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        VorgemerktesUebernehmen();
        int n;
        fixed (float* p = pcm20ms)
        fixed (byte* z = ziel)
        {
            n = OpusNativ.KodiereFloat(_handle, p, RahmenSamples, z, Math.Min(ziel.Length, MaxPaketBytes));
        }
        if (n <= 0) throw new InvalidOperationException("opus_encode_float: " + OpusNativ.Text(n));
        _kodierteRahmen++;
        return n;
    }

    /// <summary>
    /// Merkt den erwarteten Paketverlust in Prozent vor (0 bis 100, außerhalb wird begrenzt). Über 0
    /// legt Opus LBRR-Daten für den Vorgängerrahmen ins Paket. Wirkt ab dem nächsten Rahmen.
    /// </summary>
    public void SetzeVerlustHinweis(int prozent) => _verlustSoll = Math.Clamp(prozent, 0, 100);

    /// <summary>Merkt eine neue Zielbitrate vor (ENTWURF: 32 000 bis 64 000). Wirkt ab dem nächsten Rahmen.</summary>
    public void SetzeBitrate(int bitrate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bitrate, MinBitrate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitrate, MaxBitrate);
        _bitrateSoll = bitrate;
    }

    /// <summary>Liest die Einstellungen beim Kodierer zurück. Nicht aus dem Audio-Thread und nicht gleichzeitig mit Kodieren.</summary>
    public OpusKodiererEinstellungen EinstellungenLesen()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        return new OpusKodiererEinstellungen(
            Anwendung: Lesen(OpusNativ.GetApplication),
            Bitrate: Lesen(OpusNativ.GetBitrate),
            Vbr: Lesen(OpusNativ.GetVbr) != 0,
            VbrBeschraenkt: Lesen(OpusNativ.GetVbrConstraint) != 0,
            Komplexitaet: Lesen(OpusNativ.GetComplexity),
            MaxBandbreite: Lesen(OpusNativ.GetMaxBandwidth),
            InbandFec: Lesen(OpusNativ.GetInbandFec),
            VerlustHinweis: Lesen(OpusNativ.GetPacketLossPerc),
            Dtx: Lesen(OpusNativ.GetDtx) != 0,
            Signal: Lesen(OpusNativ.GetSignal),
            RahmenDauer: Lesen(OpusNativ.GetExpertFrameDuration),
            Lookahead: Lesen(OpusNativ.GetLookahead));
    }

    public void Dispose() => _handle.Dispose();

    private void VorgemerktesUebernehmen()
    {
        int verlust = _verlustSoll;
        if (verlust != _verlustIst && OpusNativ.KodiererSetzen(_handle, OpusNativ.SetPacketLossPerc, verlust) == OpusNativ.Ok)
            _verlustIst = verlust;
        int bitrate = _bitrateSoll;
        if (bitrate != _bitrateIst && OpusNativ.KodiererSetzen(_handle, OpusNativ.SetBitrate, bitrate) == OpusNativ.Ok)
            _bitrateIst = bitrate;
    }

    private void Setzen(int anfrage, int wert)
    {
        int rc = OpusNativ.KodiererSetzen(_handle, anfrage, wert);
        if (rc != OpusNativ.Ok)
            throw new InvalidOperationException($"opus_encoder_ctl({anfrage}, {wert}): {OpusNativ.Text(rc)}");
    }

    private int Lesen(int anfrage)
    {
        int wert;
        int rc = OpusNativ.KodiererLesen(_handle, anfrage, &wert);
        if (rc != OpusNativ.Ok)
            throw new InvalidOperationException($"opus_encoder_ctl({anfrage}): {OpusNativ.Text(rc)}");
        return wert;
    }
}
