// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>Wo das Einmessen der Mindest-Funklautstärke steht.</summary>
public enum MindestEinmessSchritt
{
    Bereit,

    /// <summary>Satz wie am Funk sprechen.</summary>
    Normal,

    /// <summary>Kurze Pause, um umzuschalten.</summary>
    Umschalten,

    /// <summary>Derselbe Satz leise, wie zur Familie.</summary>
    Leise,

    Fertig,

    /// <summary>In einem der Sätze kam zu wenig Sprache an.</summary>
    ZuWenigSprache,

    /// <summary>Der leise Satz war nicht deutlich leiser als der normale (unter <see cref="Mindesteinmessung.MindestUnterschiedDb"/>).</summary>
    ZuWenigUnterschied,
}

/// <summary>
/// Stand des Einmessens. Gilt <see cref="Schritt"/> = <see cref="MindestEinmessSchritt.Fertig"/>, sind <paramref name="VerstaerkungDb"/>
/// (Grundverstärkung der Pegelstufe aus dem normalen Satz) und <paramref name="MindestAbstandDb"/> (Untergrenze so weit unter der Stimme)
/// das Ergebnis; <paramref name="NormalDbfs"/> und <paramref name="LeiseDbfs"/> sind die Pegel der beiden Sätze vor der Pegelstufe (auch bei
/// <see cref="MindestEinmessSchritt.ZuWenigUnterschied"/>).
/// </summary>
public readonly record struct MindestEinmessStand(MindestEinmessSchritt Schritt, int RestMs, float VerstaerkungDb, float MindestAbstandDb, float NormalDbfs,
    float LeiseDbfs);

/// <summary>
/// "Mindestlautstärke einmessen" (Klangstufe C1, Q12): erst ein Satz wie am Funk, dann derselbe Satz leise wie zur Familie. Je Satz zählt
/// die mittlere Leistung der Sprachrahmen (wie der Grundpegel der Halteregel) vor der Pegelstufe. Daraus:
/// <list type="bullet">
/// <item>die Grundverstärkung: <see cref="Pegelstufe.ZielDbfs"/> minus Pegel des normalen Satzes, höchstens so viel, dass dessen Spitzen
/// bei <see cref="Pegelstufe.EinmessSpitzeDbfs"/> bleiben (wie das Einmessen der Pegelstufe);</item>
/// <item>die Mindest-Funklautstärke an der leisen Seite, sogar etwas darunter: 3 dB unter dem leisen Satz, also Unterschied plus 3 dB unter
/// der Stimme (<see cref="MindestEinmessStand.MindestAbstandDb"/>, <see cref="AbstandFuer"/>). Milde Einstellung (klang-c1b): Ein
/// verlorener Befehl wiegt schwerer als ein durchgerutschter leiser Satz.</item>
/// </list>
/// Unter <see cref="MindestUnterschiedDb"/> Unterschied ist das Ergebnis nicht zu gebrauchen (freundlich wiederholen lassen).
/// <para>
/// Threads: <see cref="Zufuehren"/> nur im Aufnahme-Thread (ohne Allokation); <see cref="Starten"/> und <see cref="Abbrechen"/> aus jedem
/// Thread, wirksam ab dem nächsten Rahmen; <see cref="Stand"/> ist eine Momentaufnahme, ein noch nicht angenommener Befehl zählt schon.
/// </para>
/// </summary>
public sealed class Mindesteinmessung
{
    /// <summary>5 s je Satz.</summary>
    public const int SatzRahmen = 500;

    /// <summary>1,5 s zum Umschalten zwischen den Sätzen.</summary>
    public const int UmschaltRahmen = 150;

    /// <summary>Ab dieser Wahrscheinlichkeit (RNNoise) zählt ein Rahmen als Sprache; leise Sprache erreicht 0,9 seltener.</summary>
    public const float SprachSchwelle = 0.6f;

    /// <summary>So viele Sprachrahmen braucht jeder Satz (0,8 s).</summary>
    public const int MindestSprachRahmen = 80;

    /// <summary>So viel leiser muss der leise Satz sein.</summary>
    public const float MindestUnterschiedDb = 8f;

    /// <summary>
    /// Linie der Mindest-Funklautstärke über dem leisen Satz als Anteil des Unterschieds (<see cref="AbstandFuer"/>); ab Werk 0. Gewählt in
    /// klang-c1b (MessungKlangC1.Messung_KlangC1_Raster, 9 Kombinationen mit <see cref="SprachGate.FreigabeZuschlagDb"/>): Ein verlorener Befehl
    /// wiegt schwerer als ein durchgerutschter leiser Satz. Bei leise plus ein Drittel des Unterschieds und 8 dB Zuschlag (erster Stand von C1)
    /// verloren normale Sprüche 69 von 492 Wortanfängen, jetzt 9.
    /// </summary>
    public const double LinieUeberLeiseAnteil = 0.0;

    /// <summary>Versatz der Linie in dB zusätzlich zum Anteil; negativ liegt sie unter dem leisen Satz. Ab Werk -3 dB (leise minus 3 dB).</summary>
    public const double LinieVersatzDb = -3.0;

    private const int BefehlStarten = 1;
    private const int BefehlAbbrechen = 2;
    private const float EimerUntergrenzeDb = -150f;
    private const int EimerAnzahl = 1600; // -150 bis +10 dB in 0,1-dB-Schritten

    private readonly float[] _spitzen = new float[SatzRahmen];
    private readonly int[] _eimer = new int[EimerAnzahl];
    private int _befehl;
    private volatile int _schritt;
    private volatile int _restMs;
    private volatile float _verstaerkung;
    private volatile float _abstand;
    private volatile float _normal = float.NaN;
    private volatile float _leise = float.NaN;

    // Nur Aufnahme-Thread:
    private int _rahmen;
    private double _summe;
    private int _anzahl;

    /// <summary>Stand für die Oberfläche; ein noch nicht angenommener Befehl zählt schon.</summary>
    public MindestEinmessStand Stand
    {
        get
        {
            switch (Volatile.Read(ref _befehl))
            {
                case BefehlStarten: return new MindestEinmessStand(MindestEinmessSchritt.Normal, SatzRahmen * 10, 0f, 0f, float.NaN, float.NaN);
                case BefehlAbbrechen: return new MindestEinmessStand(MindestEinmessSchritt.Bereit, 0, 0f, 0f, float.NaN, float.NaN);
            }
            var schritt = (MindestEinmessSchritt)_schritt;
            return new MindestEinmessStand(schritt, _restMs, _verstaerkung, _abstand, _normal, _leise);
        }
    }

    /// <summary>Einer der Sätze oder die Pause dazwischen läuft (oder der Start ist angefordert).</summary>
    public bool Laeuft => Stand.Schritt is MindestEinmessSchritt.Normal or MindestEinmessSchritt.Umschalten or MindestEinmessSchritt.Leise;

    public void Starten() => Volatile.Write(ref _befehl, BefehlStarten);

    public void Abbrechen() => Volatile.Write(ref _befehl, BefehlAbbrechen);

    /// <summary>
    /// Aufnahme-Thread: ein Rahmen mit seinem Pegel vor der Pegelstufe (mittlere Leistung in dBFS), seiner Spitze (dBFS) und der
    /// Sprachwahrscheinlichkeit.
    /// </summary>
    public void Zufuehren(float pegelDbfs, float spitzeDbfs, float sprachwahrscheinlichkeit)
    {
        BefehlAnnehmen();
        switch ((MindestEinmessSchritt)_schritt)
        {
            case MindestEinmessSchritt.Normal:
                Sammeln(pegelDbfs, spitzeDbfs, sprachwahrscheinlichkeit);
                if (++_rahmen >= SatzRahmen) NormalAbschliessen();
                break;
            case MindestEinmessSchritt.Umschalten:
                if (++_rahmen >= UmschaltRahmen) Weiter(MindestEinmessSchritt.Leise);
                break;
            case MindestEinmessSchritt.Leise:
                Sammeln(pegelDbfs, spitzeDbfs, sprachwahrscheinlichkeit);
                if (++_rahmen >= SatzRahmen) LeiseAbschliessen();
                break;
            default:
                return;
        }
        var laenge = (MindestEinmessSchritt)_schritt == MindestEinmessSchritt.Umschalten ? UmschaltRahmen : SatzRahmen;
        _restMs = Laeuft ? Math.Max(0, laenge - _rahmen) * 10 : 0;
    }

    private void BefehlAnnehmen()
    {
        var befehl = Volatile.Read(ref _befehl);
        if (befehl == 0) return;
        if (befehl == BefehlStarten)
        {
            _normal = float.NaN;
            _leise = float.NaN;
            _verstaerkung = 0f;
            _abstand = 0f;
            Weiter(MindestEinmessSchritt.Normal);
        }
        else
        {
            Weiter(MindestEinmessSchritt.Bereit);
        }
        // Erst nach dem Zustand löschen, damit ein Leser nie einen Zwischenstand sieht; ein neuerer Befehl bleibt stehen.
        _ = Interlocked.CompareExchange(ref _befehl, 0, befehl);
    }

    private void Weiter(MindestEinmessSchritt schritt)
    {
        _rahmen = 0;
        _summe = 0;
        _anzahl = 0;
        _schritt = (int)schritt;
    }

    private void Sammeln(float pegelDbfs, float spitzeDbfs, float p)
    {
        if (!(p >= SprachSchwelle) || !float.IsFinite(pegelDbfs) || pegelDbfs <= Pegel.MinDbfs) return;
        _summe += Math.Pow(10.0, pegelDbfs / 10.0);
        if (_anzahl < _spitzen.Length) _spitzen[_anzahl] = float.IsFinite(spitzeDbfs) ? spitzeDbfs : pegelDbfs;
        _anzahl++;
    }

    private double Mittel() => 10.0 * Math.Log10(_summe / _anzahl);

    private void NormalAbschliessen()
    {
        if (_anzahl < MindestSprachRahmen)
        {
            Weiter(MindestEinmessSchritt.ZuWenigSprache);
            return;
        }
        var normal = Mittel();
        var spitze = Quantil(_spitzen.AsSpan(0, Math.Min(_anzahl, _spitzen.Length)), 0.99);
        var verstaerkung = Math.Min(Pegelstufe.ZielDbfs - normal, Pegelstufe.EinmessSpitzeDbfs - spitze);
        _verstaerkung = (float)Math.Round(Math.Clamp(verstaerkung, Pegelstufe.MinDb, Pegelstufe.MaxDb), 1);
        _normal = (float)normal;
        Weiter(MindestEinmessSchritt.Umschalten);
    }

    private void LeiseAbschliessen()
    {
        if (_anzahl < MindestSprachRahmen)
        {
            Weiter(MindestEinmessSchritt.ZuWenigSprache);
            return;
        }
        var leise = Mittel();
        _leise = (float)leise;
        var unterschied = _normal - leise;
        if (unterschied < MindestUnterschiedDb)
        {
            Weiter(MindestEinmessSchritt.ZuWenigUnterschied);
            return;
        }
        _abstand = AbstandFuer(unterschied);
        Weiter(MindestEinmessSchritt.Fertig);
    }

    /// <summary>
    /// Abstand der Mindest-Funklautstärke unter der Stimme für einen Unterschied zwischen normalem und leisem Satz: Die Linie liegt bei
    /// leise plus <paramref name="anteilUeberLeise"/> mal Unterschied plus <paramref name="versatzDb"/> (negativ: unter dem leisen Satz),
    /// auf 0,5 dB gerundet und nie näher an der Stimme, als der Korridor zur Obergrenze verlangt (<see cref="Maschine.GateVorgaben"/>: 6 dB
    /// plus 3 dB Breite), nie weiter als <see cref="Maschine.GateVorgaben.MaxMindestAbstandDb"/>. Ohne Angabe die Werte des Programms
    /// (<see cref="LinieUeberLeiseAnteil"/>, <see cref="LinieVersatzDb"/>); Messungen setzen die Lage der Linie selbst.
    /// </summary>
    internal static float AbstandFuer(double unterschiedDb, double anteilUeberLeise = LinieUeberLeiseAnteil, double versatzDb = LinieVersatzDb)
    {
        var abstand = Math.Round((unterschiedDb * (1.0 - anteilUeberLeise) - versatzDb) * 2.0, MidpointRounding.AwayFromZero) / 2.0;
        return (float)Math.Clamp(abstand, Maschine.GateVorgaben.MinObergrenzeAbstandDb + Maschine.GateVorgaben.KorridorMindestBreiteDb,
            Maschine.GateVorgaben.MaxMindestAbstandDb);
    }

    /// <summary>Quantil auf 0,1 dB über vorab angelegte Eimer (wie in der Pegelstufe, ohne Sortieren im Audio-Thread): Oberkante des Eimers.</summary>
    private float Quantil(ReadOnlySpan<float> werte, double q)
    {
        Array.Clear(_eimer);
        foreach (var db in werte) _eimer[Math.Clamp((int)MathF.Floor((db - EimerUntergrenzeDb) * 10f), 0, EimerAnzahl - 1)]++;
        var ziel = Math.Max(1, (int)Math.Ceiling(q * werte.Length));
        var summe = 0;
        for (var i = 0; i < _eimer.Length; i++)
        {
            summe += _eimer[i];
            if (summe >= ziel) return EimerUntergrenzeDb + (i + 1) * 0.1f;
        }
        return EimerUntergrenzeDb + _eimer.Length * 0.1f;
    }
}
