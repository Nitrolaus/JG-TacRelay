// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Nativ;

namespace Funk.Klang.Kern;

/// <summary>
/// RNNoise-Rauschunterdrückung, 48 kHz mono, fest 480 Samples (10 ms) je Rahmen, eingebautes
/// Standardmodell. Liefert je Rahmen das entrauschte Signal und die Sprachwahrscheinlichkeit.
/// </summary>
/// <remarks>
/// RNNoise rechnet im int16-Wertebereich (±32768), die Kette in float [-1, 1]. Die Hülle skaliert in
/// eigene, vorab angelegte Puffer um; der native Code sieht nie die Spans des Aufrufers. Eingang und
/// Ausgang dürfen deshalb derselbe Puffer sein. Keine Allokation, keine Sperre. Nur aus einem Thread.
/// <para>
/// Numerischer Schutz (Gegenprüfung): Ein einziges NaN, ±Unendlich oder ein riesiger Wert (1e30)
/// vergiftet den Hochpass- und GRU-Zustand von RNNoise dauerhaft; danach liefert es nur noch NaN.
/// Die Hülle ersetzt deshalb NaN und ±Unendlich durch 0 (gezählt in <see cref="ErsetzteWerte"/>) und
/// begrenzt alle Werte auf [-1, 1], also auf den int16-Bereich, in dem das Modell trainiert ist.
/// Das APM davor begrenzt ohnehin auf denselben Bereich. Subnormale Ausgabewerte (abklingender
/// Zustand in digitaler Stille) werden zu 0.
/// </para>
/// <para>
/// Ein stationärer Sinuston wird vollständig entfernt (BAUBERICHT). Ruf- und Quittungstöne gehören
/// deshalb hinter die Rauschunterdrückung.
/// </para>
/// <para>
/// Verzögerung: gemessen 960 Samples (20 ms), nicht 480. Der verwendete Stand (rnnoise main mit
/// neuem Modell) hält in <c>rnnoise_process_frame</c> einen Rahmen zurück (<c>delayed_X</c>) und
/// legt ihn auf die 10 ms der Überlappung obendrauf. Wer das Original zumischt, muss es um
/// <see cref="VerzoegerungSamples"/> verzögern.
/// </para>
/// </remarks>
public sealed unsafe class RnnoiseProzessor : IDisposable
{
    public const int RahmenSamples = 480;
    /// <summary>Gemessene Verzögerung Ausgang gegen Eingang (Test Rnnoise_Verzoegerung_960Samples).</summary>
    public const int VerzoegerungSamples = 960;
    /// <summary>Faktor zwischen float [-1, 1] und dem int16-Wertebereich, den RNNoise erwartet.</summary>
    public const float Int16Skala = 32768f;

    private const float Rueckskala = 1f / Int16Skala;

    private readonly RnnoiseHandle _handle;
    private readonly float[] _ein = new float[RahmenSamples];
    private readonly float[] _aus = new float[RahmenSamples];
    private long _rahmen;
    private long _ersetzt;

    public RnnoiseProzessor()
    {
        int groesse = RnnoiseNativ.RahmenGroesse();
        if (groesse != RahmenSamples)
            throw new InvalidOperationException($"rnnoise_get_frame_size liefert {groesse}, erwartet {RahmenSamples}.");
        _handle = RnnoiseNativ.Erstellen(0);
        if (_handle.IsInvalid)
        {
            _handle.Dispose();
            throw new InvalidOperationException("rnnoise_create lieferte NULL.");
        }
    }

    /// <summary>Anzahl verarbeiteter Rahmen (zählt nur echte native Aufrufe).</summary>
    public long VerarbeiteteRahmen => Volatile.Read(ref _rahmen);

    /// <summary>Eingangswerte, die als NaN oder ±Unendlich ankamen und durch 0 ersetzt wurden.</summary>
    public long ErsetzteWerte => Volatile.Read(ref _ersetzt);

    /// <summary>
    /// Entrauscht genau 480 Werte (float [-1, 1]) von <paramref name="eingang"/> nach
    /// <paramref name="ausgang"/>. Rückgabe: Sprachwahrscheinlichkeit in [0, 1]; bei digitaler Stille 0.
    /// NaN und ±Unendlich werden zu 0, Werte außerhalb [-1, 1] begrenzt (siehe Klassenhinweis).
    /// </summary>
    public float Verarbeiten(ReadOnlySpan<float> eingang, Span<float> ausgang)
    {
        LaengenPruefung.Genau(eingang.Length, RahmenSamples, nameof(eingang));
        LaengenPruefung.Genau(ausgang.Length, RahmenSamples, nameof(ausgang));
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        Span<float> ein = _ein;
        int ersetzt = 0;
        for (int i = 0; i < ein.Length; i++)
        {
            float v = eingang[i];
            if (!float.IsFinite(v))
            {
                v = 0f;
                ersetzt++;
            }
            ein[i] = Math.Clamp(v, -1f, 1f) * Int16Skala;
        }
        if (ersetzt != 0) _ersetzt += ersetzt;

        float wahrscheinlichkeit;
        fixed (float* e = _ein)
        fixed (float* a = _aus)
        {
            wahrscheinlichkeit = RnnoiseNativ.Verarbeiten(_handle, a, e);
        }
        _rahmen++;

        ReadOnlySpan<float> aus = _aus;
        for (int i = 0; i < aus.Length; i++) ausgang[i] = aus[i] * Rueckskala;
        Endlichkeit.SubnormaleNullen(ausgang);
        return wahrscheinlichkeit;
    }

    /// <summary>Entrauscht einen Rahmen in-place. Rückgabe wie <see cref="Verarbeiten(ReadOnlySpan{float}, Span{float})"/>.</summary>
    public float Verarbeiten(Span<float> rahmen) => Verarbeiten(rahmen, rahmen);

    /// <summary>
    /// Vergisst allen Verlauf (rnnoise_init mit eingebautem Modell), etwa nach einem Gerätewechsel.
    /// Danach verhält sich der Prozessor wie frisch angelegt. Allokiert nicht.
    /// </summary>
    public void Zuruecksetzen()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (RnnoiseNativ.Initialisieren(_handle, 0) != 0)
            throw new InvalidOperationException("rnnoise_init meldet einen Fehler.");
    }

    public void Dispose() => _handle.Dispose();
}
