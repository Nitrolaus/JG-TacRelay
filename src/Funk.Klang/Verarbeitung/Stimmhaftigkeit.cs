// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Liefert je 10-ms-Rahmen (480 Samples, 48 kHz) eine Sprachwahrscheinlichkeit 0..1.
/// Im Produkt kommt sie von RNNoise (vad_prob) oder Silero; hier ist die Naht für Messung und Tests.
/// </summary>
public interface ISprachwahrscheinlichkeit
{
    float Schaetzen(ReadOnlySpan<float> rahmen10ms);

    void Zuruecksetzen();
}

/// <summary>
/// Einfacher, eigener Schätzer der Stimmhaftigkeit: zweifacher Tiefpass bei 1 kHz, Dezimation auf 12 kHz,
/// normierte Autokorrelation über die letzten 30 ms für Grundperioden von 2,5 bis 15 ms (67 bis 400 Hz).
/// Das Maximum wird auf 0..1 abgebildet (0,4 und darunter = 0, 0,85 und darüber = 1). Passt die gefundene
/// Periode nicht zu der des vorigen Rahmens (Abweichung über 10 %, auch nicht als Oktave), zählt der Wert nur
/// halb: Sprache hält ihre Grundperiode über mehrere Rahmen, Knackimpulse täuschen nur zufällige Perioden vor.
/// Er erkennt stimmhafte Laute und weist breitbandige Klicks und Knistern ab, stimmlose Laute (f, s, sch)
/// erkennt er nicht. Gedacht als Rückfall ohne RNNoise und als deterministische Sprachwahrscheinlichkeit
/// für die Gate-Messung an synthetischen Szenen. Allokiert nach dem Konstruktor nichts.
/// </summary>
public sealed class Stimmhaftigkeit : ISprachwahrscheinlichkeit
{
    private const int Faktor = 4;
    private const int Laenge = 360; // 30 ms bei 12 kHz
    private const int MinVersatz = 30;
    private const int MaxVersatz = 180;
    private const float Tiefpass = 0.1227f; // 1 - exp(-2 pi 1000 / 48000)
    private const float Untergrenze = 0.4f;
    private const float Obergrenze = 0.85f;

    private const float MinZustand = 1e-20f; // darunter sind die Tiefpasszustände 0 (keine Denormalen nach Stille)

    private readonly float[] _verlauf = new float[Laenge];
    private readonly float[] _arbeit = new float[Laenge];
    private readonly double[] _energieKumuliert = new double[Laenge + 1];
    private float _tp1;
    private float _tp2;
    private int _phase;
    private int _letzterVersatz;

    /// <summary>Zuletzt gefundene höchste normierte Autokorrelation.</summary>
    public float LetzteKorrelation { get; private set; }

    public float Schaetzen(ReadOnlySpan<float> rahmen10ms)
    {
        var neu = 0;
        // Platz für die neuen dezimierten Werte schaffen: höchstens rahmen/Faktor + 1.
        var erwartet = (rahmen10ms.Length + _phase) / Faktor;
        erwartet = Math.Min(erwartet, Laenge);
        Array.Copy(_verlauf, erwartet, _verlauf, 0, Laenge - erwartet);
        var schreib = Laenge - erwartet;
        foreach (var v in rahmen10ms)
        {
            var x = float.IsFinite(v) ? v : 0f;
            _tp1 += Tiefpass * (x - _tp1);
            _tp2 += Tiefpass * (_tp1 - _tp2);
            if (++_phase < Faktor) continue;
            _phase = 0;
            if (neu < erwartet) _verlauf[schreib + neu] = _tp2;
            neu++;
        }
        // Nach digitaler Stille blieben die Tiefpässe sonst für immer auf einem Denormal stehen (der Schritt
        // Tiefpass * x rundet dort auf 0), und jedes Sample kostete Denormal-Arithmetik.
        if (MathF.Abs(_tp1) < MinZustand) _tp1 = 0f;
        if (MathF.Abs(_tp2) < MinZustand) _tp2 = 0f;

        float mittel = 0;
        foreach (var v in _verlauf) mittel += v;
        mittel /= Laenge;
        // Kumulierte Energie in double: Die Differenz gesamt - kumuliert[versatz] löscht sonst bei Klicks am
        // Fensteranfang aus und täuscht hohe Korrelation vor.
        _energieKumuliert[0] = 0;
        for (var i = 0; i < Laenge; i++)
        {
            var y = _verlauf[i] - mittel;
            _arbeit[i] = y;
            _energieKumuliert[i + 1] = _energieKumuliert[i] + (double)y * y;
        }
        var gesamt = _energieKumuliert[Laenge];
        if (!(gesamt > 1e-9) || double.IsInfinity(gesamt))
        {
            LetzteKorrelation = 0;
            _letzterVersatz = 0;
            return 0f;
        }

        var beste = 0f;
        var besterVersatz = 0;
        for (var versatz = MinVersatz; versatz <= MaxVersatz; versatz++)
        {
            var n = Laenge - versatz;
            var e0 = _energieKumuliert[n];
            var e1 = gesamt - _energieKumuliert[versatz];
            if (e0 <= 1e-12 || e1 <= 1e-12) continue;
            var r = (float)(Zeitdehnung.Skalarprodukt(_arbeit.AsSpan(0, n), _arbeit.AsSpan(versatz, n)) / Math.Sqrt(e0 * e1));
            if (r > beste)
            {
                beste = r;
                besterVersatz = versatz;
            }
        }
        LetzteKorrelation = beste;
        var p = Math.Clamp((beste - Untergrenze) / (Obergrenze - Untergrenze), 0f, 1f);
        if (!PeriodePasst(besterVersatz, _letzterVersatz)) p *= 0.5f;
        _letzterVersatz = beste > Untergrenze ? besterVersatz : 0;
        return p;
    }

    /// <summary>Gleiche Periode wie im vorigen Rahmen, auch als Oktave (halb oder doppelt), auf 10 % genau.</summary>
    private static bool PeriodePasst(int versatz, int vorher)
    {
        if (versatz <= 0 || vorher <= 0) return false;
        var toleranz = 0.1f * versatz;
        return MathF.Abs(versatz - vorher) <= toleranz
            || MathF.Abs(versatz - 2f * vorher) <= toleranz
            || MathF.Abs(2f * versatz - vorher) <= 2f * toleranz;
    }

    /// <summary>Betrag der Tiefpasszustände, für Tests.</summary>
    internal float Filterzustand => MathF.Max(MathF.Abs(_tp1), MathF.Abs(_tp2));

    public void Zuruecksetzen()
    {
        Array.Clear(_verlauf);
        _letzterVersatz = 0;
        _tp1 = 0;
        _tp2 = 0;
        _phase = 0;
    }
}
