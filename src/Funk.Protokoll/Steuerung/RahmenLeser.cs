// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace Funk.Protokoll.Steuerung;

/// <summary>Ausgang eines Leseversuchs.</summary>
public enum LeseStatus : byte
{
    /// <summary>Ein gültiger Rahmen liegt vor.</summary>
    Rahmen,
    /// <summary>Die Gegenseite hat sauber zwischen zwei Rahmen geschlossen.</summary>
    Ende,
    /// <summary>Die Verbindung brach mitten im Rahmen ab.</summary>
    Abgebrochen,
    /// <summary>Verstoß gegen 4.2 (<c>PROTOKOLL</c> oder <c>ZU_GROSS</c>): Fehler senden und trennen.</summary>
    Fehler,
}

/// <summary>Ergebnis von <see cref="RahmenLeser.LesenAsync"/>. <see cref="Body"/> gilt bis zum nächsten Lesen.</summary>
public readonly record struct Leseergebnis(LeseStatus Status, RahmenKopf Kopf, ReadOnlyMemory<byte> Body, Fehlercode Fehler);

/// <summary>
/// Liest Steuerrahmen aus einem <see cref="Stream"/> (etwa <c>SslStream</c>). Reihenfolge nach 4.2: erst die
/// 4 Byte Länge lesen und gegen die Grenze prüfen, dann die übrigen Kopfbytes, erst danach den Body-Puffer aus
/// dem Pool anfordern. Der Puffer des vorigen Rahmens wird beim nächsten Lesen genullt und zurückgegeben
/// (VoiceKeys trägt Schlüssel). Nicht threadsicher: ein Leser je Verbindung.
/// </summary>
public sealed class RahmenLeser : IDisposable
{
    private readonly Stream _strom;
    private readonly ArrayPool<byte> _pool;
    private readonly byte[] _kopf = new byte[Rahmen.KopfLaenge];
    private byte[]? _body;

    public RahmenLeser(Stream strom, ArrayPool<byte>? pool = null)
    {
        ArgumentNullException.ThrowIfNull(strom);
        _strom = strom;
        _pool = pool ?? ArrayPool<byte>.Shared;
    }

    /// <summary>Liest den nächsten Rahmen mit höchstens <paramref name="maxLaenge"/> im Längenfeld.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Leseergebnis> LesenAsync(int maxLaenge, CancellationToken abbruch = default)
    {
        Freigeben();

        var gelesen = await _strom.ReadAtLeastAsync(_kopf.AsMemory(0, 4), 4, throwOnEndOfStream: false, abbruch).ConfigureAwait(false);
        if (gelesen == 0) return new Leseergebnis(LeseStatus.Ende, default, default, default);
        if (gelesen < 4) return new Leseergebnis(LeseStatus.Abgebrochen, default, default, default);

        var laenge = BinaryPrimitives.ReadUInt32BigEndian(_kopf);
        if (Rahmen.LaengePruefen(laenge, maxLaenge) is { } f1) return new Leseergebnis(LeseStatus.Fehler, default, default, f1);

        if (!await VollAsync(_kopf.AsMemory(4, 8), abbruch).ConfigureAwait(false))
            return new Leseergebnis(LeseStatus.Abgebrochen, default, default, default);
        var f2 = Rahmen.KopfLesen(_kopf, maxLaenge, out var kopf);
        if (f2 is { } f) return new Leseergebnis(LeseStatus.Fehler, kopf, default, f);

        var n = kopf.BodyLaenge;
        if (n == 0) return new Leseergebnis(LeseStatus.Rahmen, kopf, ReadOnlyMemory<byte>.Empty, default);
        _body = _pool.Rent(n);
        var body = _body.AsMemory(0, n);
        if (!await VollAsync(body, abbruch).ConfigureAwait(false))
            return new Leseergebnis(LeseStatus.Abgebrochen, kopf, default, default);
        return new Leseergebnis(LeseStatus.Rahmen, kopf, body, default);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> VollAsync(Memory<byte> ziel, CancellationToken abbruch) =>
        await _strom.ReadAtLeastAsync(ziel, ziel.Length, throwOnEndOfStream: false, abbruch).ConfigureAwait(false) == ziel.Length;

    /// <summary>
    /// Nullt den Body des zuletzt gelesenen Rahmens sofort und gibt ihn zurück, statt erst beim nächsten Lesen.
    /// Für VoiceKeys (4.5): Schlüssel übernehmen, dann aufrufen, damit sie nicht bis zum nächsten Rahmen im
    /// Poolpuffer liegen. Danach gilt <see cref="Leseergebnis.Body"/> nicht mehr.
    /// </summary>
    public void BodyVerwerfen() => Freigeben();

    private void Freigeben()
    {
        if (_body is null) return;
        _pool.Return(_body, clearArray: true);
        _body = null;
    }

    public void Dispose() => Freigeben();
}

/// <summary>
/// Liest Steuerrahmen aus einem <see cref="PipeReader"/>. Die Grenzprüfung greift, sobald die Kopfbytes da sind;
/// auf einen zu großen Body wird nicht gewartet. <see cref="PipeLeseergebnis.Body"/> gilt bis zum nächsten Lesen.
/// </summary>
public sealed class PipeRahmenLeser
{
    private readonly PipeReader _leser;
    private SequencePosition _bis;
    private bool _offen;

    public PipeRahmenLeser(PipeReader leser)
    {
        ArgumentNullException.ThrowIfNull(leser);
        _leser = leser;
    }

    /// <summary>Liest den nächsten Rahmen mit höchstens <paramref name="maxLaenge"/> im Längenfeld.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<PipeLeseergebnis> LesenAsync(int maxLaenge, CancellationToken abbruch = default)
    {
        if (_offen)
        {
            _leser.AdvanceTo(_bis);
            _offen = false;
        }
        while (true)
        {
            var ergebnis = await _leser.ReadAsync(abbruch).ConfigureAwait(false);
            var puffer = ergebnis.Buffer;
            var status = Rahmen.VersucheLesen(ref puffer, maxLaenge, out var kopf, out var body, out var fehler);
            switch (status)
            {
                case RahmenStatus.Fertig:
                    _bis = puffer.Start;
                    _offen = true;
                    return new PipeLeseergebnis(LeseStatus.Rahmen, kopf, body, default);
                case RahmenStatus.Fehler:
                    _leser.AdvanceTo(ergebnis.Buffer.Start);
                    return new PipeLeseergebnis(LeseStatus.Fehler, default, default, fehler);
                default:
                    _leser.AdvanceTo(ergebnis.Buffer.Start, ergebnis.Buffer.End);
                    if (ergebnis.IsCompleted || ergebnis.IsCanceled)
                    {
                        var status2 = ergebnis.Buffer.IsEmpty ? LeseStatus.Ende : LeseStatus.Abgebrochen;
                        return new PipeLeseergebnis(status2, default, default, default);
                    }
                    break;
            }
        }
    }
}

/// <summary>Ergebnis von <see cref="PipeRahmenLeser.LesenAsync"/>.</summary>
public readonly record struct PipeLeseergebnis(LeseStatus Status, RahmenKopf Kopf, ReadOnlySequence<byte> Body, Fehlercode Fehler);
