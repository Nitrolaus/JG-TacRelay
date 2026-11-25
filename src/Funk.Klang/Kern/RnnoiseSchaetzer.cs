// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Kern;

/// <summary>
/// Sprachwahrscheinlichkeit aus RNNoise als <see cref="ISprachwahrscheinlichkeit"/> (Klangstufe C2a: Sprachanteil eines Funkers, der lange
/// sendet, auf der Empfängerseite). Eine eigene RNNoise-Instanz; das entrauschte Signal wird verworfen. Allokiert nach dem Konstruktor nichts.
/// </summary>
public sealed class RnnoiseSchaetzer : ISprachwahrscheinlichkeit, IDisposable
{
    private readonly RnnoiseProzessor _rnnoise = new();
    private readonly float[] _aus = new float[RnnoiseProzessor.RahmenSamples];

    public float Schaetzen(ReadOnlySpan<float> rahmen10ms) => _rnnoise.Verarbeiten(rahmen10ms, _aus);

    public void Zuruecksetzen() => _rnnoise.Zuruecksetzen();

    public void Dispose() => _rnnoise.Dispose();
}
