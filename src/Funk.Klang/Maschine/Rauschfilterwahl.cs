// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Maschine;

/// <summary>Rauschfilter der Sendekette (Klangstufe B): genau einer wirkt (Recherche Abschnitt 1 und 2).</summary>
public enum Rauschfilter
{
    /// <summary>RNNoise, leicht, 20 ms Verzögerung (ab Werk).</summary>
    Standard,

    /// <summary>DeepFilterNet3, treuer zur Stimme, etwa sechsmal so viel Rechenzeit wie RNNoise, 30 ms Verzögerung.</summary>
    Stark,

    /// <summary>Ohne Rauschfilter; die Sprachwahrscheinlichkeit für Gate und Pegelstufe kommt weiter von RNNoise.</summary>
    Aus,
}

/// <summary>
/// Wahl und Wechsel des Rauschfilters je 10-ms-Rahmen im Aufnahme-Thread (Klangstufe B, Recherche Abschnitt 3), ohne native
/// Filter: Die <see cref="Sendekette"/> rechnet RNNoise immer (leichter Weg warm, dazu die Sprachwahrscheinlichkeit) und
/// DeepFilterNet3, solange <see cref="StarkGebraucht"/>; diese Klasse legt die Wege zeitrichtig übereinander und blendet um.
/// <para>
/// Verzögerung: Die Kette läuft mit <see cref="Verzug"/> Rahmen (Aus 0, Standard 2, Stark 3). Jeder Weg wird auf diesen Verzug
/// nachgezogen (Eingang und RNNoise in kleinen Ringen), so liegen beim Überblenden beide Wege zeitrichtig. Mehr Verzug kommt nur
/// in einer Pause hinzu (kein Gate offen, keine Taste), weniger fällt nur in einer Pause weg; dort darf ein Rahmen doppelt kommen
/// oder fehlen, weil nichts hinausgeht. Ein Wechsel, der mehr Verzug braucht (zu Stark, von Aus weg), wartet deshalb auf die
/// nächste Pause; einer, der weniger braucht (zurück auf Standard, auch beim Rückfall), blendet sofort und gibt den Verzug in der
/// nächsten Pause ab.
/// </para>
/// <para>
/// Überblenden linear über <see cref="BlendeRahmen"/> Rahmen (50 ms, Recherche 30 bis 100 ms); bei gleichphasigen Wegen hebt eine
/// lineare Blende den Pegel in der Mitte nicht an. DeepFilterNet3 rechnet vor dem Einblenden <see cref="AnlaufRahmen"/> Rahmen
/// warm (500 ms; seine Normierung hat eine Zeitkonstante von 1 s). Liefert DeepFilterNet3 einen Rahmen nicht (Fehler der DLL),
/// gilt sofort Standard, um einen Rahmen nachgezogen, also ohne Sprung in der Zeit.
/// </para>
/// <para>Die Pegelstufe liegt vor beiden Filtern und ist ihnen gemeinsam (Klangstufe A, Abschnitt 5). Ohne Allokation, ohne Sperre.</para>
/// </summary>
internal sealed class Rauschfilterwahl
{
    public const int BlendeRahmen = 5;
    public const int AnlaufRahmen = 50;

    private const int R = 480;
    private const int HoechsterVerzug = 3;

    private readonly float[][] _roh = [new float[R], new float[R], new float[R], new float[R]];
    private readonly float[][] _standard = [new float[R], new float[R]];
    private int _rohNeu;
    private int _standardNeu;
    private Rauschfilter _von;
    private int _blende;
    private int _starkAnlauf;

    /// <summary>Gewünschter Filter; setzt nur der Aufnahme-Thread (die Kette übernimmt ihn aus ihrem volatilen Feld).</summary>
    public Rauschfilter Ziel { get; set; } = Rauschfilter.Standard;

    /// <summary>Der Weg, der wirkt (in einer Blende der, zu dem hin geblendet wird).</summary>
    public Rauschfilter Aktiv { get; private set; } = Rauschfilter.Standard;

    /// <summary>Verzögerung der Kette in Rahmen; die Sprachwahrscheinlichkeit für das Gate wird ebenso weit verzögert.</summary>
    public int Verzug { get; private set; } = VerzugRahmen(Rauschfilter.Standard);

    /// <summary>DeepFilterNet3 muss in diesem Rahmen rechnen (gewünscht, wirkt, oder wird gerade ausgeblendet).</summary>
    public bool StarkGebraucht => Ziel == Rauschfilter.Stark || Aktiv == Rauschfilter.Stark || (_blende > 0 && _von == Rauschfilter.Stark);

    /// <summary>Eigene Verzögerung eines Wegs in Rahmen (RNNoise 960, DeepFilterNet3 1440 Samples).</summary>
    public static int VerzugRahmen(Rauschfilter f) => f switch
    {
        Rauschfilter.Standard => 2,
        Rauschfilter.Stark => HoechsterVerzug,
        _ => 0,
    };

    /// <summary>
    /// Ein Rahmen: <paramref name="roh"/> vor den Filtern, <paramref name="standard"/> hinter RNNoise, <paramref name="stark"/>
    /// hinter DeepFilterNet3 oder leer, wenn es nicht rechnete. <paramref name="pause"/>: Gate geschlossen und keine Taste. Alle
    /// Spans haben 480 Werte; <paramref name="aus"/> darf keiner der Eingänge sein.
    /// </summary>
    public void Verarbeiten(ReadOnlySpan<float> roh, ReadOnlySpan<float> standard, ReadOnlySpan<float> stark, bool pause, Span<float> aus)
    {
        _rohNeu = (_rohNeu + 1) % _roh.Length;
        roh.CopyTo(_roh[_rohNeu]);
        _standardNeu = (_standardNeu + 1) % _standard.Length;
        standard.CopyTo(_standard[_standardNeu]);

        if (stark.IsEmpty)
        {
            _starkAnlauf = 0;
            if (Aktiv == Rauschfilter.Stark)
            {
                Aktiv = _blende > 0 ? _von : Rauschfilter.Standard;
                _blende = 0;
            }
            else if (_blende > 0 && _von == Rauschfilter.Stark)
            {
                _blende = 0;
            }
        }
        else if (_starkAnlauf < AnlaufRahmen)
        {
            _starkAnlauf++;
        }

        if (_blende == 0 && Ziel != Aktiv)
        {
            var noetig = Math.Max(VerzugRahmen(Aktiv), VerzugRahmen(Ziel));
            if (Verzug < noetig && pause) Verzug = noetig;
            var bereit = Ziel != Rauschfilter.Stark || _starkAnlauf >= AnlaufRahmen;
            if (bereit && Verzug >= noetig)
            {
                _von = Aktiv;
                Aktiv = Ziel;
                _blende = 1;
            }
        }
        else if (_blende == 0 && pause && Verzug > VerzugRahmen(Aktiv))
        {
            Verzug = VerzugRahmen(Aktiv);
        }

        var ziel = Weg(Aktiv, stark);
        if (_blende == 0)
        {
            ziel.CopyTo(aus);
            return;
        }
        var von = Weg(_von, stark);
        var basis = (_blende - 1) * R;
        const float Schritt = 1f / (BlendeRahmen * R);
        for (var i = 0; i < R; i++)
        {
            var a = (basis + i + 1) * Schritt;
            aus[i] = ((1f - a) * von[i]) + (a * ziel[i]);
        }
        if (++_blende > BlendeRahmen) _blende = 0;
    }

    /// <summary>Ein Weg, nachgezogen auf <see cref="Verzug"/>.</summary>
    private ReadOnlySpan<float> Weg(Rauschfilter f, ReadOnlySpan<float> stark) => f switch
    {
        Rauschfilter.Standard => _standard[(_standardNeu - (Verzug - VerzugRahmen(Rauschfilter.Standard)) + _standard.Length) % _standard.Length],
        Rauschfilter.Stark => stark,
        _ => _roh[(_rohNeu - Verzug + _roh.Length) % _roh.Length],
    };
}
