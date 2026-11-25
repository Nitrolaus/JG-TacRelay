// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Rauschboden nach dem Prinzip der Minimum-Statistik (Martin 2001), vereinfacht auf den Gesamtpegel:
/// leicht geglättete Rahmenleistung, Minimum über 8 Teilfenster zu je einem Achtel der Fensterlänge
/// (Standard 2 s), mit fester Bias-Korrektur (+1 dB), weil das Minimum den Mittelwert des Rauschens
/// unterschätzt. Die Rahmenleistung über 480 Samples streut bei stationärem Rauschen nur um etwa 0,3 dB,
/// darum reicht eine schwache Glättung; eine starke würde Klickenergie in die Folgerahmen tragen und den
/// Boden bei schnellem Tippen anheben. Allokiert nach dem Konstruktor nichts.
/// </summary>
internal sealed class Rauschboden
{
    private const int Teilfenster = 8;
    private const float Glaettung = 0.3f;
    private const float Bias = 1.26f;
    private const float MinLeistung = 1e-10f;

    private readonly float[] _teilMinimum = new float[Teilfenster];
    private readonly int _rahmenJeTeil;
    private float _geglaettet;
    private bool _hatWert;
    private float _minimumAktuell = float.MaxValue;
    private int _zaehler;
    private int _teilIndex;

    public Rauschboden(int fensterRahmen)
    {
        _rahmenJeTeil = Math.Max(1, fensterRahmen / Teilfenster);
        Array.Fill(_teilMinimum, float.MaxValue);
    }

    public bool HatWert => _hatWert;

    public float WertDb
    {
        get
        {
            if (!_hatWert) return Pegel.MinDbfs;
            var minimum = _minimumAktuell;
            foreach (var m in _teilMinimum) minimum = MathF.Min(minimum, m);
            return Pegel.LeistungZuDb(MathF.Max(minimum, MinLeistung) * Bias);
        }
    }

    /// <summary>Eine Rahmenleistung nachführen. Nicht endliche Werte werden übergangen: Die Glättung trüge ein
    /// NaN oder Unendlich sonst für immer weiter, und die Sprachaktivierung öffnete nie wieder.</summary>
    public void Nachfuehren(float leistung)
    {
        if (!float.IsFinite(leistung)) return;
        leistung = MathF.Max(leistung, MinLeistung);
        _geglaettet = _hatWert ? Glaettung * _geglaettet + (1f - Glaettung) * leistung : leistung;
        _hatWert = true;
        _minimumAktuell = MathF.Min(_minimumAktuell, _geglaettet);
        if (++_zaehler < _rahmenJeTeil) return;
        _teilMinimum[_teilIndex] = _minimumAktuell;
        _teilIndex = (_teilIndex + 1) % Teilfenster;
        _minimumAktuell = float.MaxValue;
        _zaehler = 0;
    }
}

/// <summary>
/// Häufigkeitsverteilung mit exponentiellem Vergessen und Quantil. Allokiert nach dem Konstruktor nichts.
/// NaN wird übergangen; Eimer unter <see cref="MinGewicht"/> werden 0, damit selten getroffene Eimer nach
/// Stunden nicht in Denormalen landen.
/// </summary>
internal sealed class Verteilung
{
    private const double MinGewicht = 1e-30;
    private readonly double[] _eimer;
    private readonly float _untergrenze;
    private readonly float _breite;
    private readonly double _vergessen;
    private double _gesamt;

    public Verteilung(float untergrenze, float obergrenze, int eimer, double vergessen)
    {
        _eimer = new double[eimer];
        _untergrenze = untergrenze;
        _breite = (obergrenze - untergrenze) / eimer;
        _vergessen = vergessen;
    }

    public long Anzahl { get; private set; }

    public void Hinzufuegen(float wert)
    {
        if (float.IsNaN(wert)) return;
        var i = Math.Clamp((int)((wert - _untergrenze) / _breite), 0, _eimer.Length - 1);
        for (var j = 0; j < _eimer.Length; j++)
        {
            var e = _eimer[j] * _vergessen;
            _eimer[j] = e < MinGewicht ? 0 : e;
        }
        _eimer[i] += 1;
        _gesamt = _gesamt * _vergessen + 1;
        Anzahl++;
    }

    /// <summary>Oberkante des Eimers, in dem das Quantil liegt.</summary>
    public float Quantil(double q)
    {
        var ziel = q * _gesamt;
        double kumuliert = 0;
        for (var i = 0; i < _eimer.Length; i++)
        {
            kumuliert += _eimer[i];
            if (kumuliert >= ziel) return _untergrenze + (i + 1) * _breite;
        }
        return _untergrenze + _eimer.Length * _breite;
    }
}
