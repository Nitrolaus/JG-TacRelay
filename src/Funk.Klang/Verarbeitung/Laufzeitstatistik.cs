// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Laufzeitstatistik eines Senders nach dem Prinzip von WebRTC NetEQ (DelayManager, UnderrunOptimizer):
/// <list type="bullet">
/// <item>Relative Verzögerung eines Pakets = seine Laufzeit minus die kleinste Laufzeit der letzten 2 s
/// (Laufzeit = Ankunft minus Sollzeit aus der Frame-Nr). Sie ist nie negativ.</item>
/// <item>Je Auffrischintervall (Standard 500 ms) geht das Maximum der relativen Verzögerung in ein
/// Histogramm (5-ms-Eimer) mit Vergessensfaktor 0,983; die ersten Werte zählen stärker
/// (Startgewicht 2, wie start_forget_weight).</item>
/// <item>Ergebnis ist das Quantil (Standard 0,95) dieses Histogramms.</item>
/// </list>
/// Eigene Umsetzung; Zahlen aus codec-ausgabe.md B19. Läuft im Audio-Thread, allokiert nach dem Konstruktor nichts.
/// </summary>
internal sealed class Laufzeitstatistik
{
    public const int EimerMs = 5;
    private const int EimerAnzahl = 100; // bis 500 ms, der letzte Eimer sammelt alles darüber
    private const int FensterPlaetze = 128;
    private const long FensterUs = 2_000_000;
    private const long FrameUs = 20_000;

    private readonly double[] _eimer = new double[EimerAnzahl];
    private readonly long[] _fensterAnkunft = new long[FensterPlaetze];
    private readonly long[] _fensterLaufzeit = new long[FensterPlaetze];
    private readonly double _vergessen;
    private readonly double _startGewicht;
    private readonly double _quantil;
    private readonly long _intervallUs;

    private long _eintraege;
    private int _fensterKopf;
    private int _fensterAnzahl;
    private bool _hatBezug;
    private uint _bezugFrame;

    private bool _intervallLaeuft;
    private long _intervallStartUs;
    private double _intervallMaxMs;
    private int _intervallPakete;

    private bool _hatVorgaenger;
    private long _vorgaengerAnkunft;
    private uint _vorgaengerFrame;

    public Laufzeitstatistik(double quantil, double vergessen, double startGewicht, int auffrischIntervallMs)
    {
        _quantil = quantil;
        _vergessen = vergessen;
        _startGewicht = startGewicht;
        _intervallUs = auffrischIntervallMs * 1000L;
    }

    /// <summary>Zahl der Histogramm-Einträge; 0 heißt: noch keine Schätzung.</summary>
    public long Eintraege => _eintraege;

    /// <summary>Zwischenankunfts-Jitter nach RFC 3550 6.4.1 in ms (nur aufeinanderfolgende Pakete einer Phase).</summary>
    public double JitterMs { get; private set; }

    /// <summary>Zuletzt berechnete relative Verzögerung in ms.</summary>
    public double LetzteRelativeMs { get; private set; }

    /// <summary>Ob die laufende Phase schon ein Paket für die Laufzeitrechnung hatte.</summary>
    public bool HatBezug => _hatBezug;

    /// <summary>Frame-Nr, auf die sich die Laufzeiten der laufenden Phase beziehen.</summary>
    public uint BezugFrame => _bezugFrame;

    /// <summary>Kleinste Laufzeit im 2-s-Fenster (Ankunft minus Sollabstand zum Bezugsframe) in Mikrosekunden.</summary>
    public long MinLaufzeitUs { get; private set; }

    /// <summary>Neue Zeitlinie: Bezug und Fenster zurücksetzen, angefangenes Intervall übernehmen. Das Histogramm bleibt.</summary>
    public void NeuePhase()
    {
        if (_intervallLaeuft && _intervallPakete >= 5) Eintragen(_intervallMaxMs);
        _intervallLaeuft = false;
        _intervallPakete = 0;
        _fensterAnzahl = 0;
        _hatBezug = false;
        _hatVorgaenger = false;
    }

    /// <summary>Ein Paket (ohne PREROLL) mit Ankunftszeit in Mikrosekunden.</summary>
    public void Paket(long ankunftUs, uint frame)
    {
        if (!_hatBezug)
        {
            _bezugFrame = frame;
            _hatBezug = true;
        }
        var laufzeit = ankunftUs - (int)(frame - _bezugFrame) * FrameUs;

        // Fenster: älteste Einträge außerhalb von 2 s fallen heraus.
        while (_fensterAnzahl > 0 && ankunftUs - _fensterAnkunft[Index(0)] > FensterUs) Entfernen();
        if (_fensterAnzahl == FensterPlaetze) Entfernen();
        var neu = Index(_fensterAnzahl);
        _fensterAnkunft[neu] = ankunftUs;
        _fensterLaufzeit[neu] = laufzeit;
        _fensterAnzahl++;

        var minimum = long.MaxValue;
        for (var i = 0; i < _fensterAnzahl; i++) minimum = Math.Min(minimum, _fensterLaufzeit[Index(i)]);
        MinLaufzeitUs = minimum;
        var relativMs = (laufzeit - minimum) / 1000.0;
        LetzteRelativeMs = relativMs;

        if (_hatVorgaenger && frame - _vorgaengerFrame == 1)
        {
            var d = (ankunftUs - _vorgaengerAnkunft - FrameUs) / 1000.0;
            JitterMs += (Math.Abs(d) - JitterMs) / 16.0;
        }
        if (!_hatVorgaenger || (int)(frame - _vorgaengerFrame) > 0)
        {
            _vorgaengerAnkunft = ankunftUs;
            _vorgaengerFrame = frame;
            _hatVorgaenger = true;
        }

        if (_intervallUs <= 0)
        {
            Eintragen(relativMs);
            return;
        }
        if (!_intervallLaeuft)
        {
            _intervallLaeuft = true;
            _intervallStartUs = ankunftUs;
            _intervallMaxMs = relativMs;
            _intervallPakete = 1;
            return;
        }
        if (ankunftUs - _intervallStartUs >= _intervallUs)
        {
            Eintragen(_intervallMaxMs);
            _intervallStartUs = ankunftUs;
            _intervallMaxMs = relativMs;
            _intervallPakete = 1;
            return;
        }
        _intervallMaxMs = Math.Max(_intervallMaxMs, relativMs);
        _intervallPakete++;
    }

    /// <summary>Oberkante des Quantil-Eimers in ms; 0 ohne Schätzung.</summary>
    public double QuantilMs()
    {
        if (_eintraege == 0) return 0;
        double gesamt = 0;
        foreach (var e in _eimer) gesamt += e;
        var ziel = _quantil * gesamt;
        double kumuliert = 0;
        for (var i = 0; i < EimerAnzahl; i++)
        {
            kumuliert += _eimer[i];
            if (kumuliert >= ziel) return (i + 1) * EimerMs;
        }
        return EimerAnzahl * EimerMs;
    }

    private void Eintragen(double wertMs)
    {
        var eimer = Math.Clamp((int)(wertMs / EimerMs), 0, EimerAnzahl - 1);
        var faktor = Math.Clamp(1.0 - _startGewicht / (_eintraege + 1), 0.0, _vergessen);
        for (var i = 0; i < EimerAnzahl; i++)
        {
            // Selten getroffene Eimer würden nach Stunden zu Denormalen; darunter zählen sie als leer.
            var e = _eimer[i] * faktor;
            _eimer[i] = e < 1e-30 ? 0 : e;
        }
        _eimer[eimer] += 1.0 - faktor;
        _eintraege++;
    }

    private int Index(int versatz) => (_fensterKopf + versatz) % FensterPlaetze;

    private void Entfernen()
    {
        _fensterKopf = (_fensterKopf + 1) % FensterPlaetze;
        _fensterAnzahl--;
    }
}
