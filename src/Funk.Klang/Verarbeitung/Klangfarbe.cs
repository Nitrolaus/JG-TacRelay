// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;

namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Einstellung der <see cref="Klangfarbe"/> (Q70 bis Q72, seit 0.9.3): Wärme, Klarheit und Schärfe in ganzen dB von <see cref="MinDb"/> bis
/// <see cref="MaxDb"/>; ab Werk alle 0, also neutral.
/// </summary>
public readonly record struct KlangfarbeEinstellung(int WaermeDb, int KlarheitDb, int SchaerfeDb)
{
    public const int MinDb = -6;
    public const int MaxDb = 6;

    /// <summary>Alle drei 0, die Werkseinstellung: Die Stufe ist überbrückt.</summary>
    public static KlangfarbeEinstellung Neutral => default;

    public bool IstNeutral => WaermeDb == 0 && KlarheitDb == 0 && SchaerfeDb == 0;

    /// <summary>Jeder Wert auf <see cref="MinDb"/> bis <see cref="MaxDb"/> begrenzt.</summary>
    public KlangfarbeEinstellung Begrenzt() => new(Begrenzen(WaermeDb), Begrenzen(KlarheitDb), Begrenzen(SchaerfeDb));

    /// <summary>Ein Wert auf <see cref="MinDb"/> bis <see cref="MaxDb"/>.</summary>
    public static int Begrenzen(int db) => Math.Clamp(db, MinDb, MaxDb);
}

/// <summary>
/// Klangfarbe der eigenen Stimme (Q70 bis Q72, seit 0.9.3): drei Glocken (Peaking EQ nach dem Audio-EQ-Kochbuch, <see cref="Biquad.Glocke"/>)
/// hintereinander, Wärme um <see cref="WaermeHz"/> (Q <see cref="WaermeQ"/>), Klarheit um <see cref="KlarheitHz"/> (Q <see cref="KlarheitQ"/>)
/// und Schärfe um <see cref="SchaerfeHz"/> (Q <see cref="SchaerfeQ"/>), je <see cref="KlangfarbeEinstellung.MinDb"/> bis
/// <see cref="KlangfarbeEinstellung.MaxDb"/> dB. Sie sitzt in der Sendekette hinter dem Abgriff der Stimmprobe und vor Hörprobe und Gate.
/// <para>
/// Lautheitsneutral: Ein fester Ausgleich g = -10 log10(Summe w_b |H(f_b)|^2) bringt die Leistung einer Normstimme wieder auf den Stand ohne
/// Klangfarbe (<see cref="AusgleichDb"/>). H ist der Frequenzgang der drei Glocken, f_b sind die Mitten der Terzbänder 160 bis 8000 Hz, w_b
/// ihre auf die Summe 1 gebrachten Leistungsanteile in der Norm-Sprachkurve (<see cref="BandDb"/>).
/// </para>
/// <para>
/// Neutral (alle drei 0) ist die Stufe ganz überbrückt: Der Rahmen bleibt Bit für Bit, wie er war, auch der Begrenzer dahinter rechnet dann
/// nicht. Eine neue Einstellung blendet über <see cref="BlendeWerte"/> (20 ms, Halbkosinus) vom alten Satz Glocken auf den neuen über; der neue
/// übernimmt dazu den Zustand des alten (aus der Überbrückung die letzten Eingangswerte), damit er ohne Einschwingen beginnt. Kommt während
/// einer Blende schon die nächste Einstellung, folgt sie gleich danach. So knackt nichts, auch wenn jemand mitten im Spruch am Regler zieht.
/// </para>
/// <para>
/// Hinter den Glocken ein <see cref="Begrenzer"/> mit den Werten des Sicherheitsbegrenzers der Pegelstufe, denn eine Anhebung kann Spitzen
/// über 0 dBFS heben. Er läuft, solange die Klangfarbe wirkt oder überblendet, und nach dem Zurückstellen auf neutral noch, bis er ganz offen
/// ist; sonst sprünge die Lautstärke.
/// </para>
/// <para>
/// Zustand in double; nach jedem Rahmen werden sehr kleine Zustandswerte zu 0 (<see cref="Biquad.KleineNullen"/>) und subnormale Ausgangswerte
/// ebenso (<see cref="Endlichkeit.SubnormaleNullen"/>); ein nicht endlicher Zustand (NaN im Eingang) beginnt bei null. Nur ein Thread ruft
/// <see cref="Verarbeiten"/> (der Aufnahme-Thread); <see cref="Einstellung"/> kommt aus jedem Thread als eine einzige Zahl. Ohne Allokation und
/// ohne Sperre.
/// </para>
/// </summary>
public sealed class Klangfarbe
{
    public const double WaermeHz = 160;
    public const double WaermeQ = 0.8;
    public const double KlarheitHz = 3000;
    public const double KlarheitQ = 0.8;
    public const double SchaerfeHz = 6000;
    public const double SchaerfeQ = 1.0;

    /// <summary>Länge der Überblendung beim Wechsel der Einstellung: 960 Werte, 20 ms, zwei Rahmen.</summary>
    public const int BlendeWerte = 2 * Klangformat.Samples10ms;

    /// <summary>Mitten der Terzbänder für den Ausgleich, 160 bis 8000 Hz.</summary>
    internal static readonly double[] BandHz = [160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000];

    /// <summary>
    /// Bandpegel der Norm-Sprachkurve in dB zu <see cref="BandHz"/>. Herkunft: ANSI S3.5-1997 (Speech Intelligibility Index), Terzband-Verfahren,
    /// Standard-Sprachspektrum bei normaler Sprechweise; dort als Spektrumpegel (dB je Hz) angegeben, hier plus 10 log10 der Bandbreite des
    /// jeweiligen Terzbands (Bandbreitenkorrektur), also der Pegel des ganzen Bands. Nur die Verhältnisse zählen, der Bezug fällt beim Normieren weg.
    /// </summary>
    internal static readonly double[] BandDb = [48.06, 51.13, 52.40, 52.63, 54.24, 54.92, 53.71, 50.95, 48.66, 47.65, 45.80, 43.97, 40.83, 40.20, 38.98, 35.96, 34.24, 33.78];

    /// <summary>Leistungsanteile der Bänder, Summe 1.</summary>
    private static readonly double[] Gewichte = Normieren(BandDb);

    /// <summary>Gewicht des neuen Satzes je Wert der Blende, Halbkosinus von knapp 0 bis 1.</summary>
    private static readonly float[] Rampe = RampeBauen();

    /// <summary>Ein Satz Glocken mit Ausgleich; neutral heißt: durchreichen.</summary>
    private sealed class Satz
    {
        public Biquad Waerme;
        public Biquad Klarheit;
        public Biquad Schaerfe;
        public double Faktor = 1;
        public bool Neutral = true;
        public int Gepackt;

        public double Schritt(double x) => Neutral ? x : Faktor * Schaerfe.Schritt(Klarheit.Schritt(Waerme.Schritt(x)));

        /// <summary>Nach einem Rahmen: nicht endlicher Zustand auf null, sehr kleine Werte auf 0.</summary>
        public void Pflegen()
        {
            if (Neutral) return;
            if (!Waerme.Endlich || !Klarheit.Endlich || !Schaerfe.Endlich)
            {
                Waerme.ZustandSetzen(0, 0, 0, 0);
                Klarheit.ZustandSetzen(0, 0, 0, 0);
                Schaerfe.ZustandSetzen(0, 0, 0, 0);
                return;
            }
            Waerme.KleineNullen();
            Klarheit.KleineNullen();
            Schaerfe.KleineNullen();
        }
    }

    private Satz _alt = new();
    private Satz _neu = new();
    private readonly Begrenzer _begrenzer = new();
    private volatile int _wunsch;
    private volatile int _wirksam;
    private volatile bool _ueberbrueckt = true;
    private int _blende = -1;
    private bool _begrenzerLaeuft;
    private double _x1;
    private double _x2;

    /// <summary>Die gewünschte Einstellung; aus jedem Thread, begrenzt, gilt ab dem nächsten Rahmen (mit Überblendung).</summary>
    public KlangfarbeEinstellung Einstellung
    {
        get => Entpacken(_wunsch);
        set => _wunsch = Packen(value.Begrenzt());
    }

    /// <summary>Die Einstellung, die nach der letzten Blende ganz wirkt (Tests, Diagnose).</summary>
    public KlangfarbeEinstellung Wirksam => Entpacken(_wirksam);

    /// <summary>Der letzte Rahmen blieb unberührt: neutral, keine Blende, Begrenzer offen.</summary>
    public bool Ueberbrueckt => _ueberbrueckt;

    /// <summary>Nur zum Vergleich in Tests: falsch schaltet hart um, mit leerem Zustand und ohne Blende (so knackt es).</summary>
    internal bool Ueberblenden { get; set; } = true;

    /// <summary>Aufnahme-Thread: einen 10-ms-Rahmen in-place färben (und begrenzen); neutral bleibt er unberührt.</summary>
    public void Verarbeiten(Span<float> rahmen)
    {
        if (rahmen.Length != Klangformat.Samples10ms) throw new ArgumentException("Rahmen muss 480 Werte haben.", nameof(rahmen));
        var wunsch = _wunsch;
        if (_blende < 0 && wunsch != _alt.Gepackt)
        {
            if (Ueberblenden) BlendeBeginnen(wunsch);
            else HartUmschalten(wunsch);
        }
        if (_blende < 0 && _alt.Neutral)
        {
            // Neutral: durchreichen; nur die letzten Eingangswerte merken, damit eine spätere Blende ohne Einschwingen beginnt.
            _x2 = Endlich(rahmen[^2]);
            _x1 = Endlich(rahmen[^1]);
            if (!_begrenzerLaeuft)
            {
                _ueberbrueckt = true;
                return;
            }
            // Zurück auf neutral, aber der Begrenzer greift noch: weiter begrenzen, bis er ganz offen ist, sonst spränge die Lautstärke.
            _ueberbrueckt = false;
            Begrenzen(rahmen);
            return;
        }

        _ueberbrueckt = false;
        var alt = _alt;
        var neu = _neu;
        var blende = _blende;
        double x1 = _x1, x2 = _x2;
        for (var i = 0; i < rahmen.Length; i++)
        {
            double x = rahmen[i];
            var y = alt.Schritt(x);
            if (blende >= 0)
            {
                y += Rampe[blende] * (neu.Schritt(x) - y);
                blende++;
            }
            rahmen[i] = (float)y;
            x2 = x1;
            x1 = x;
        }
        _x1 = Endlich(x1);
        _x2 = Endlich(x2);
        alt.Pflegen();
        if (blende >= 0) neu.Pflegen();
        if (blende >= BlendeWerte)
        {
            // Blende fertig: Der neue Satz ist jetzt der wirkende.
            (_alt, _neu) = (neu, alt);
            blende = -1;
            _wirksam = _alt.Gepackt;
        }
        _blende = blende;
        Endlichkeit.SubnormaleNullen(rahmen);
        Begrenzen(rahmen);
    }

    /// <summary>
    /// Der feste Ausgleich in dB für eine Einstellung: g = -10 log10(Summe w_b |H(f_b)|^2) über die Terzbänder der Norm-Sprachkurve; neutral 0.
    /// </summary>
    public static double AusgleichDb(KlangfarbeEinstellung e)
    {
        if (e.Begrenzt().IstNeutral) return 0;
        var (w, k, s) = Glocken(e.Begrenzt());
        return 20 * Math.Log10(Ausgleich(w, k, s));
    }

    /// <summary>Frequenzgang der Stufe in dB bei <paramref name="hz"/>, mit Ausgleich (Tests, Handbuch); neutral 0.</summary>
    internal static double FrequenzgangDb(KlangfarbeEinstellung e, double hz)
    {
        if (e.Begrenzt().IstNeutral) return 0;
        var (w, k, s) = Glocken(e.Begrenzt());
        return (10 * Math.Log10(w.Betrag2(hz) * k.Betrag2(hz) * s.Betrag2(hz))) + AusgleichDb(e);
    }

    private void Begrenzen(Span<float> rahmen)
    {
        _begrenzer.Verarbeiten(rahmen, Span<float>.Empty);
        // Ganz offen und nichts zu tun: Neutral darf die Stufe wieder überbrückt werden.
        if (_alt.Neutral && _blende < 0 && _begrenzer.KleinsterFaktor >= 1f && _begrenzer.LetzterFaktor >= 1f) _begrenzerLaeuft = false;
    }

    /// <summary>Den neuen Satz für <paramref name="gepackt"/> bauen und die Blende beginnen.</summary>
    private void BlendeBeginnen(int gepackt)
    {
        SatzBauen(_neu, gepackt);
        if (!_neu.Neutral)
        {
            if (_alt.Neutral)
            {
                // Aus der Überbrückung: Jede Glocke hat zuletzt den Eingang gesehen und durchgereicht.
                _neu.Waerme.ZustandSetzen(_x1, _x2, _x1, _x2);
                _neu.Klarheit.ZustandSetzen(_x1, _x2, _x1, _x2);
                _neu.Schaerfe.ZustandSetzen(_x1, _x2, _x1, _x2);
            }
            else
            {
                _neu.Waerme.ZustandVon(_alt.Waerme);
                _neu.Klarheit.ZustandVon(_alt.Klarheit);
                _neu.Schaerfe.ZustandVon(_alt.Schaerfe);
            }
        }
        _blende = 0;
        if (!_begrenzerLaeuft)
        {
            _begrenzer.Zuruecksetzen();
            _begrenzerLaeuft = true;
        }
    }

    /// <summary>Nur für den Vergleich in Tests: sofort der neue Satz, Zustand null, keine Blende.</summary>
    private void HartUmschalten(int gepackt)
    {
        SatzBauen(_neu, gepackt);
        (_alt, _neu) = (_neu, _alt);
        _wirksam = gepackt;
        if (!_begrenzerLaeuft && !_alt.Neutral)
        {
            _begrenzer.Zuruecksetzen();
            _begrenzerLaeuft = true;
        }
    }

    private static void SatzBauen(Satz s, int gepackt)
    {
        var e = Entpacken(gepackt);
        s.Gepackt = gepackt;
        s.Neutral = e.IstNeutral;
        if (s.Neutral) return;
        (s.Waerme, s.Klarheit, s.Schaerfe) = Glocken(e);
        s.Faktor = Ausgleich(s.Waerme, s.Klarheit, s.Schaerfe);
    }

    private static (Biquad Waerme, Biquad Klarheit, Biquad Schaerfe) Glocken(KlangfarbeEinstellung e) =>
        (Biquad.Glocke(WaermeHz, WaermeQ, e.WaermeDb), Biquad.Glocke(KlarheitHz, KlarheitQ, e.KlarheitDb), Biquad.Glocke(SchaerfeHz, SchaerfeQ, e.SchaerfeDb));

    /// <summary>Linearer Ausgleich: 1 / Wurzel(Summe w_b |H(f_b)|^2).</summary>
    private static double Ausgleich(in Biquad w, in Biquad k, in Biquad s)
    {
        var summe = 0.0;
        for (var b = 0; b < BandHz.Length; b++)
        {
            var f = BandHz[b];
            summe += Gewichte[b] * w.Betrag2(f) * k.Betrag2(f) * s.Betrag2(f);
        }
        return 1 / Math.Sqrt(summe);
    }

    private static double Endlich(double x) => double.IsFinite(x) ? x : 0;

    /// <summary>Drei Werte zu je einem Byte (Zweierkomplement) in einer Zahl; neutral ist 0.</summary>
    private static int Packen(KlangfarbeEinstellung e) => (e.WaermeDb & 0xFF) | ((e.KlarheitDb & 0xFF) << 8) | ((e.SchaerfeDb & 0xFF) << 16);

    private static KlangfarbeEinstellung Entpacken(int p) => new((sbyte)(p & 0xFF), (sbyte)((p >> 8) & 0xFF), (sbyte)((p >> 16) & 0xFF));

    private static double[] Normieren(double[] pegelDb)
    {
        var leistung = pegelDb.Select(db => Math.Pow(10, db / 10)).ToArray();
        var summe = leistung.Sum();
        return [.. leistung.Select(p => p / summe)];
    }

    private static float[] RampeBauen()
    {
        var r = new float[BlendeWerte];
        for (var j = 0; j < BlendeWerte; j++) r[j] = (float)(0.5 - (0.5 * Math.Cos(Math.PI * (j + 1) / BlendeWerte)));
        return r;
    }
}
