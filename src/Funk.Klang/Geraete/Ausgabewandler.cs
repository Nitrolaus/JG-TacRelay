// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Geraete;

/// <summary>
/// Füllt den Puffer eines Wiedergabegeräts (beliebige Rate und Kanalzahl, verschränkt) aus einer Stereoquelle mit
/// 48 kHz, etwa dem <see cref="Mischer"/>. Bei 48 kHz und mindestens zwei Kanälen schreibt die Quelle direkt ins Ziel
/// (weitere Kanäle 0). Sonst wird in 10-ms-Blöcken gemischt, mit dem speexdsp-Resampler auf die Geräterate umgerechnet
/// (Qualität 5) und über einen kleinen Vorrat verteilt; ein Monogerät bekommt (L + R) mal 0,707. Allokiert nach dem
/// Konstruktor nichts; nur aus dem einen Audio-Thread des Geräts.
/// </summary>
internal sealed class Ausgabewandler : IDisposable
{
    /// <summary>Füllt <paramref name="ziel"/> verschränkt mit <paramref name="kanaele"/> Kanälen, 48 kHz (wie <see cref="Mischer.Mischen"/>).</summary>
    public delegate void Quelle(Span<float> ziel, int kanaele);

    private const int Block = Klangformat.Samples10ms;
    private const int VorratFrames = 4096;
    private const float Wurzel2Halbe = 0.70710677f;

    private readonly int _kanaele;
    private readonly Quelle _quelle;
    private readonly Resampler? _resampler;
    private readonly bool _direkt;
    private readonly float[] _block = new float[Block * 2];
    private readonly float[] _vorrat = new float[VorratFrames * 2];
    private int _vorratLese;
    private int _vorratAnzahl;

    public Ausgabewandler(StromFormat format, Quelle quelle)
    {
        ArgumentNullException.ThrowIfNull(quelle);
        ArgumentOutOfRangeException.ThrowIfLessThan(format.Kanaele, 1, nameof(format));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(format.Kanaele, 32, nameof(format));
        _kanaele = format.Kanaele;
        _quelle = quelle;
        if (format.Abtastrate != Klangformat.Abtastrate)
            _resampler = new Resampler(Klangformat.Abtastrate, format.Abtastrate, Resampler.StandardQualitaet, 2);
        _direkt = _resampler is null && _kanaele >= 2;
        Format = format;
    }

    public StromFormat Format { get; }

    public void Fuellen(Span<float> ziel)
    {
        if (_direkt)
        {
            _quelle(ziel, _kanaele);
            return;
        }
        var frames = ziel.Length / _kanaele;
        var geschrieben = 0;
        while (geschrieben < frames)
        {
            if (_vorratAnzahl == 0) Nachfuellen();
            var n = Math.Min(_vorratAnzahl, frames - geschrieben);
            for (var i = 0; i < n; i++)
            {
                var l = _vorrat[(_vorratLese + i) * 2];
                var r = _vorrat[(_vorratLese + i) * 2 + 1];
                var basis = (geschrieben + i) * _kanaele;
                if (_kanaele == 1)
                {
                    ziel[basis] = (l + r) * Wurzel2Halbe;
                    continue;
                }
                ziel[basis] = l;
                ziel[basis + 1] = r;
                for (var k = 2; k < _kanaele; k++) ziel[basis + k] = 0f;
            }
            _vorratLese += n;
            _vorratAnzahl -= n;
            geschrieben += n;
        }
        ziel[(frames * _kanaele)..].Clear();
    }

    public void Dispose() => _resampler?.Dispose();

    private void Nachfuellen()
    {
        _vorratLese = 0;
        _vorratAnzahl = 0;
        // Ein Resampler kann aus einem Block anfangs weniger (Latenz) liefern; so lange nachmischen, bis etwas da ist.
        while (_vorratAnzahl == 0)
        {
            _quelle(_block, 2);
            if (_resampler is null)
            {
                _block.CopyTo(_vorrat, 0);
                _vorratAnzahl = Block;
                return;
            }
            ReadOnlySpan<float> ein = _block;
            while (!ein.IsEmpty)
            {
                _resampler.Verarbeiten(ein, _vorrat.AsSpan(_vorratAnzahl * 2), out var verbraucht, out var erzeugt);
                _vorratAnzahl += erzeugt;
                if (verbraucht == 0 && erzeugt == 0) break;
                ein = ein[(verbraucht * 2)..];
            }
        }
    }
}
