// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Funk.Protokoll.Steuerung;

/// <summary>
/// Schreibt Steuerrahmen auf einen <see cref="Stream"/>: Kopf und Body liegen in einem Puffer aus dem Pool und
/// gehen mit genau einem Write-Aufruf hinaus (4.2). Der Puffer wird danach genullt (VoiceKeys trägt Schlüssel).
/// Nicht threadsicher: gedacht für die eine Schreibschleife je Sitzung.
/// </summary>
public sealed class RahmenSchreiber : IDisposable
{
    private readonly Stream _strom;
    private readonly int _maxLaenge;
    private readonly PoolPuffer _puffer;
    private readonly Utf8JsonWriter _writer;

    /// <param name="strom">Ziel, etwa ein <c>SslStream</c>.</param>
    /// <param name="maxLaenge">Größtes Längenfeld, das dieser Schreiber erzeugt (Standard 1 MiB).</param>
    /// <param name="pool">Pufferpool.</param>
    public RahmenSchreiber(Stream strom, int maxLaenge = Rahmengrenzen.Aktiv, ArrayPool<byte>? pool = null)
    {
        ArgumentNullException.ThrowIfNull(strom);
        _strom = strom;
        _maxLaenge = maxLaenge;
        _puffer = new PoolPuffer(pool ?? ArrayPool<byte>.Shared);
        _writer = SteuerJson.NeuerWriter(_puffer);
    }

    /// <summary>Schreibt einen Rahmen mit fertigem Body (binär oder bereits kodiertes JSON).</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask SchreibenAsync(ushort typ, byte flags, uint anfrageNr, ReadOnlyMemory<byte> body, CancellationToken abbruch = default)
    {
        try
        {
            _puffer.Beginnen(Rahmen.KopfLaenge);
            body.Span.CopyTo(_puffer.GetSpan(body.Length));
            _puffer.Advance(body.Length);
        }
        catch
        {
            _puffer.AllesNullen();
            throw;
        }
        await AbschliessenAsync(typ, flags, anfrageNr, abbruch).ConfigureAwait(false);
    }

    /// <summary>Kodiert <paramref name="body"/> als JSON (4.3) und schreibt den Rahmen.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask SchreibenAsync<T>(Nachrichtentyp typ, byte flags, uint anfrageNr, T body, CancellationToken abbruch = default)
    {
        try
        {
            _puffer.Beginnen(Rahmen.KopfLaenge);
            SteuerJson.Schreiben(_writer, _puffer, body);
        }
        catch
        {
            // Ein halb geschriebener Body bleibt nicht im Poolpuffer liegen; der Writer kann schon über die
            // gebuchte Länge hinaus geschrieben haben, darum wird der ganze Puffer genullt.
            _puffer.AllesNullen();
            throw;
        }
        await AbschliessenAsync((ushort)typ, flags, anfrageNr, abbruch).ConfigureAwait(false);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask AbschliessenAsync(ushort typ, byte flags, uint anfrageNr, CancellationToken abbruch)
    {
        try
        {
            var bodyLaenge = _puffer.Geschrieben - Rahmen.KopfLaenge;
            if (bodyLaenge + Rahmen.LaengeOhneBody > _maxLaenge)
                throw new InvalidOperationException($"Rahmen zu groß: Body {bodyLaenge} Byte, Grenze {_maxLaenge}.");
            Rahmen.KopfSchreiben(_puffer.Anfang(Rahmen.KopfLaenge), typ, flags, anfrageNr, bodyLaenge);
            await _strom.WriteAsync(_puffer.Inhalt, abbruch).ConfigureAwait(false);
        }
        finally
        {
            _puffer.Leeren();
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
        _puffer.Dispose();
    }
}

/// <summary>Wachsender Schreibpuffer aus dem Pool; gibt beim Wachsen und Leeren genullt zurück.</summary>
internal sealed class PoolPuffer : IBufferWriter<byte>, IDisposable
{
    private readonly ArrayPool<byte> _pool;
    private byte[] _feld;
    private int _pos;

    public PoolPuffer(ArrayPool<byte> pool, int anfang = 4096)
    {
        _pool = pool;
        _feld = pool.Rent(anfang);
    }

    public int Geschrieben => _pos;

    public ReadOnlyMemory<byte> Inhalt => _feld.AsMemory(0, _pos);

    public Span<byte> Anfang(int laenge) => _feld.AsSpan(0, laenge);

    /// <summary>Beginnt einen neuen Rahmen und reserviert vorn Platz für den Kopf.</summary>
    public void Beginnen(int reserviert)
    {
        _pos = 0;
        GetSpan(reserviert);
        _pos = reserviert;
    }

    public void Leeren()
    {
        _feld.AsSpan(0, _pos).Clear();
        _pos = 0;
    }

    /// <summary>Nullt den ganzen Puffer, auch hinter der gebuchten Länge (nach einem Abbruch mitten im Schreiben).</summary>
    public void AllesNullen()
    {
        _feld.AsSpan().Clear();
        _pos = 0;
    }

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_pos + count > _feld.Length) throw new InvalidOperationException("Über das Pufferende hinaus.");
        _pos += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Sichern(sizeHint);
        return _feld.AsMemory(_pos);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Sichern(sizeHint);
        return _feld.AsSpan(_pos);
    }

    private void Sichern(int sizeHint)
    {
        var noetig = _pos + Math.Max(sizeHint, 1);
        if (noetig <= _feld.Length) return;
        var neu = _pool.Rent(Math.Max(noetig, _feld.Length * 2));
        _feld.AsSpan(0, _pos).CopyTo(neu);
        _pool.Return(_feld, clearArray: true);
        _feld = neu;
    }

    public void Dispose()
    {
        _pool.Return(_feld, clearArray: true);
        _feld = [];
        _pos = 0;
    }
}
