// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Geraete;

/// <summary>
/// Macht aus den Daten eines Aufnahmegeräts (beliebige Rate und Kanalzahl, verschränkt) 10-ms-Rahmen mit 48 kHz mono:
/// Kanäle gemittelt, bei anderer Rate mit dem speexdsp-Resampler umgerechnet (Qualität 5), in Rahmen zu 480 Werten
/// gesammelt. Jeder volle Rahmen geht sofort an den Rückruf, der ihn in-place verändern darf. Allokiert nach dem
/// Konstruktor nichts; nur aus dem einen Audio-Thread des Geräts.
/// </summary>
internal sealed class Eingangswandler : IDisposable
{
    public delegate void RahmenRueckruf(Span<float> rahmen);

    private const int Rahmen = Klangformat.Samples10ms;
    private const int Block = 4800;

    private readonly int _kanaele;
    private readonly Resampler? _resampler;
    private readonly RahmenRueckruf _ziel;
    private readonly float[] _mono = new float[Block];
    private readonly float[] _umgerechnet;
    private readonly float[] _rahmen = new float[Rahmen];
    private int _fuellung;

    public Eingangswandler(StromFormat format, RahmenRueckruf ziel)
    {
        ArgumentNullException.ThrowIfNull(ziel);
        ArgumentOutOfRangeException.ThrowIfLessThan(format.Kanaele, 1, nameof(format));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(format.Kanaele, 32, nameof(format));
        _kanaele = format.Kanaele;
        _ziel = ziel;
        if (format.Abtastrate != Klangformat.Abtastrate)
        {
            _resampler = new Resampler(format.Abtastrate, Klangformat.Abtastrate);
            _umgerechnet = new float[(int)((long)Block * Klangformat.Abtastrate / format.Abtastrate) + 256];
        }
        else
        {
            _umgerechnet = [];
        }
        Format = format;
    }

    public StromFormat Format { get; }

    /// <summary>Verschränkte Gerätedaten einspeisen; Reste unter einem Frame werden verworfen.</summary>
    public void Einspeisen(ReadOnlySpan<float> werte)
    {
        var frames = werte.Length / _kanaele;
        var faktor = 1f / _kanaele;
        var pos = 0;
        while (pos < frames)
        {
            var n = Math.Min(Block, frames - pos);
            var quelle = werte.Slice(pos * _kanaele, n * _kanaele);
            if (_kanaele == 1)
            {
                quelle.CopyTo(_mono);
            }
            else
            {
                for (var i = 0; i < n; i++)
                {
                    var summe = 0f;
                    var basis = i * _kanaele;
                    for (var k = 0; k < _kanaele; k++) summe += quelle[basis + k];
                    _mono[i] = summe * faktor;
                }
            }
            if (_resampler is null) Sammeln(_mono.AsSpan(0, n));
            else Umrechnen(_mono.AsSpan(0, n));
            pos += n;
        }
    }

    public void Dispose() => _resampler?.Dispose();

    private void Umrechnen(ReadOnlySpan<float> ein)
    {
        while (!ein.IsEmpty)
        {
            _resampler!.Verarbeiten(ein, _umgerechnet, out var verbraucht, out var erzeugt);
            Sammeln(_umgerechnet.AsSpan(0, erzeugt));
            if (verbraucht == 0 && erzeugt == 0) return; // kann speexdsp nicht, schützt aber vor einer Endlosschleife
            ein = ein[verbraucht..];
        }
    }

    private void Sammeln(ReadOnlySpan<float> x)
    {
        while (!x.IsEmpty)
        {
            var n = Math.Min(Rahmen - _fuellung, x.Length);
            x[..n].CopyTo(_rahmen.AsSpan(_fuellung));
            _fuellung += n;
            x = x[n..];
            if (_fuellung < Rahmen) continue;
            _fuellung = 0;
            _ziel(_rahmen);
        }
    }
}
