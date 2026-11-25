// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Nativ;

namespace Funk.Klang.Kern;

/// <summary>
/// Opus-Dekodierer für 48 kHz mono, eingestellt nach ENTWURF: Decoder-Komplexität 7 (Deep PLC ab 5,
/// NoLACE ab 7) und OSCE-Bandbreitenerweiterung an.
/// </summary>
/// <remarks>
/// Reihenfolge bei Verlust (codec-ausgabe, Empfehlung 4): fehlt Paket n und liegt n+1 schon vor,
/// erst <see cref="DekodierenFec"/> mit Paket n+1 für die fehlende Dauer, danach Paket n+1 ganz
/// normal mit <see cref="Dekodieren"/>. Liegt nichts vor, <see cref="Verdecken"/>.
/// <para>
/// Im Normalfall wirft keine der drei Methoden: Ein leeres oder kaputtes Paket wird für 20 ms
/// verdeckt und in <see cref="UngueltigePakete"/> gezählt. Es werfen nur falsche Längen, und zwar vor
/// dem nativen Aufruf. Keine Allokation, keine Sperre. Nur aus einem Thread benutzen.
/// </para>
/// </remarks>
public sealed unsafe class OpusDekodierer : IDekodierer
{
    public const int RahmenSamples = 960;
    /// <summary>Längstes Opus-Paket: 120 ms bei 48 kHz. Auch Obergrenze je PLC- bzw. FEC-Aufruf.</summary>
    public const int MaxSamples = 5760;
    /// <summary>FEC und PLC verlangen Vielfache von 2,5 ms.</summary>
    public const int RasterSamples = 120;
    public const int StandardKomplexitaet = 7;

    private readonly OpusDekodiererHandle _handle;
    private long _dekodiert;
    private long _fecVersuche;
    private long _fecMitLbrr;
    private long _verdeckt;
    private long _ungueltig;
    private int _letzterFehler;

    /// <param name="komplexitaet">0 bis 10; 5 schaltet Deep PLC ein, 6 LACE, 7 NoLACE.</param>
    /// <param name="bandbreitenErweiterung">OPUS_SET_OSCE_BWE; wirkt nur auf SILK-Wideband.</param>
    public OpusDekodierer(int komplexitaet = StandardKomplexitaet, bool bandbreitenErweiterung = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(komplexitaet);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(komplexitaet, 10);

        _handle = OpusNativ.DekodiererErstellen(OpusKodierer.Abtastrate, 1, out int fehler);
        if (_handle.IsInvalid || fehler != OpusNativ.Ok)
        {
            _handle.Dispose();
            throw new InvalidOperationException("opus_decoder_create: " + OpusNativ.Text(fehler));
        }
        try
        {
            int rc = OpusNativ.DekodiererSetzen(_handle, OpusNativ.SetComplexity, komplexitaet);
            if (rc != OpusNativ.Ok)
                throw new InvalidOperationException("OPUS_SET_COMPLEXITY (Dekodierer): " + OpusNativ.Text(rc));
            Komplexitaet = Lesen(OpusNativ.GetComplexity);

            if (bandbreitenErweiterung)
            {
                rc = OpusNativ.DekodiererSetzen(_handle, OpusNativ.SetOsceBwe, 1);
                if (rc == OpusNativ.Ok)
                    BweAktiv = Lesen(OpusNativ.GetOsceBwe) == 1;
                else if (rc != OpusNativ.Unimplemented)
                    throw new InvalidOperationException("OPUS_SET_OSCE_BWE: " + OpusNativ.Text(rc));
                // UNIMPLEMENTED: DLL ohne ENABLE_OSCE_BWE gebaut; dann läuft es ohne, BweAktiv bleibt false.
            }
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    public int Abtastrate => OpusKodierer.Abtastrate;

    /// <summary>Beim Dekodierer zurückgelesene Komplexität.</summary>
    public int Komplexitaet { get; }

    /// <summary>OSCE-Bandbreitenerweiterung laut Rücklesen aktiv.</summary>
    public bool BweAktiv { get; }

    /// <summary>Normal dekodierte Pakete.</summary>
    public long DekodiertePakete => Volatile.Read(ref _dekodiert);
    /// <summary>Aufrufe von <see cref="DekodierenFec"/>.</summary>
    public long FecVersuche => Volatile.Read(ref _fecVersuche);
    /// <summary>Davon mit LBRR-Daten im Folgepaket (sonst hat libopus selbst verdeckt).</summary>
    public long FecMitLbrr => Volatile.Read(ref _fecMitLbrr);
    /// <summary>Verdeckungen ohne Paket, einschließlich Ersatz für ungültige Pakete.</summary>
    public long Verdeckungen => Volatile.Read(ref _verdeckt);
    /// <summary>Leere oder von libopus abgelehnte Pakete.</summary>
    public long UngueltigePakete => Volatile.Read(ref _ungueltig);
    /// <summary>Letzter negativer Opus-Fehlercode, 0 wenn noch keiner auftrat.</summary>
    public int LetzterFehler => Volatile.Read(ref _letzterFehler);

    /// <summary>
    /// Dekodiert ein Paket nach <paramref name="ziel"/> (mindestens 960 Werte; für Pakete über 20 ms
    /// entsprechend mehr, höchstens <see cref="MaxSamples"/> werden genutzt). Rückgabe: Anzahl Samples.
    /// Leeres oder kaputtes Paket: 20 ms Verdeckung, gezählt in <see cref="UngueltigePakete"/>.
    /// </summary>
    public int Dekodieren(ReadOnlySpan<byte> paket, Span<float> ziel)
    {
        LaengenPruefung.Mindestens(ziel.Length, RahmenSamples, nameof(ziel));
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        if (paket.IsEmpty)
        {
            _ungueltig++;
            _letzterFehler = OpusNativ.BadArg;
            return VerdeckenIntern(ziel, RahmenSamples);
        }
        int n;
        fixed (byte* p = paket)
        fixed (float* z = ziel)
        {
            n = OpusNativ.DekodiereFloat(_handle, p, paket.Length, z, Math.Min(ziel.Length, MaxSamples), 0);
        }
        if (n > 0)
        {
            _dekodiert++;
            return n;
        }
        _ungueltig++;
        _letzterFehler = n;
        return VerdeckenIntern(ziel, RahmenSamples);
    }

    /// <summary>
    /// Stellt die fehlende Dauer vor <paramref name="folgepaket"/> aus dessen LBRR-Daten wieder her.
    /// <paramref name="fehlendeSamples"/> muss genau der Lücke entsprechen (Vielfaches von 120, höchstens
    /// 5760, höchstens <c>ziel.Length</c>). Ohne LBRR im Folgepaket (CELT, FEC aus, Verlusthinweis 0)
    /// verdeckt libopus selbst; das zeigt <see cref="FecMitLbrr"/>. Rückgabe: Anzahl Samples.
    /// </summary>
    public int DekodierenFec(ReadOnlySpan<byte> folgepaket, Span<float> ziel, int fehlendeSamples)
    {
        PruefeFehlend(ziel, fehlendeSamples);
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        _fecVersuche++;
        if (folgepaket.IsEmpty) return VerdeckenIntern(ziel, fehlendeSamples);
        int n;
        fixed (byte* p = folgepaket)
        fixed (float* z = ziel)
        {
            if (OpusNativ.PaketHatLbrr(p, folgepaket.Length) > 0) _fecMitLbrr++;
            n = OpusNativ.DekodiereFloat(_handle, p, folgepaket.Length, z, fehlendeSamples, 1);
        }
        if (n > 0) return n;
        _ungueltig++;
        _letzterFehler = n;
        return VerdeckenIntern(ziel, fehlendeSamples);
    }

    /// <summary>
    /// Verdeckt eine Lücke ohne Paket (PLC, ab Komplexität 5 Deep PLC). Bedingungen an
    /// <paramref name="fehlendeSamples"/> wie bei <see cref="DekodierenFec"/>. Rückgabe: Anzahl Samples.
    /// </summary>
    public int Verdecken(Span<float> ziel, int fehlendeSamples)
    {
        PruefeFehlend(ziel, fehlendeSamples);
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        return VerdeckenIntern(ziel, fehlendeSamples);
    }

    /// <summary>Setzt den Dekodierer zurück (OPUS_RESET_STATE), etwa für einen neuen Sprecher.</summary>
    public void Zuruecksetzen()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        int rc = OpusNativ.DekodiererAnfrage(_handle, OpusNativ.ResetState);
        if (rc != OpusNativ.Ok) throw new InvalidOperationException("OPUS_RESET_STATE: " + OpusNativ.Text(rc));
    }

    /// <summary>Dauer des zuletzt dekodierten oder verdeckten Abschnitts in Samples.</summary>
    public int LetzteDauer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            return Lesen(OpusNativ.GetLastPacketDuration);
        }
    }

    public void Dispose() => _handle.Dispose();

    private int VerdeckenIntern(Span<float> ziel, int fehlend)
    {
        int n;
        fixed (float* z = ziel)
        {
            n = OpusNativ.DekodiereFloat(_handle, null, 0, z, fehlend, 0);
        }
        _verdeckt++;
        if (n > 0) return n;
        // Nach den Prüfungen nicht zu erwarten; dann lieber Stille als ein Loch im Strom.
        _letzterFehler = n;
        ziel[..fehlend].Clear();
        return fehlend;
    }

    private static void PruefeFehlend(Span<float> ziel, int fehlendeSamples)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fehlendeSamples);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fehlendeSamples, MaxSamples);
        LaengenPruefung.Vielfaches(fehlendeSamples, RasterSamples, nameof(fehlendeSamples));
        LaengenPruefung.Mindestens(ziel.Length, fehlendeSamples, nameof(ziel));
    }

    private int Lesen(int anfrage)
    {
        int wert;
        int rc = OpusNativ.DekodiererLesen(_handle, anfrage, &wert);
        if (rc != OpusNativ.Ok)
            throw new InvalidOperationException($"opus_decoder_ctl({anfrage}): {OpusNativ.Text(rc)}");
        return wert;
    }
}
