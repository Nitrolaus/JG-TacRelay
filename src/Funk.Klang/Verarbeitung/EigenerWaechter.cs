// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>Was der Wächter der eigenen Seite je Aufnahmerahmen (10 ms) sieht.</summary>
internal struct WaechterRahmen
{
    /// <summary>Pegel vor der Pegelstufe (nach dem APM): Der Wächter urteilt auf dem Pegel vor der Pegelstufe (Entwurf, Ineinandergreifen 2).</summary>
    public float EigenDb;

    /// <summary>Pegel vor dem APM, wie ihn der Echoschutz vergleicht.</summary>
    public float EingangDb;

    /// <summary>Höchste Wiedergabe der Echoreferenz der letzten 400 ms (Echoschutz), oder NaN ohne Echoreferenz.</summary>
    public float WiedergabeDb;

    /// <summary>Sprachwahrscheinlichkeit dieses Rahmens.</summary>
    public float P;

    /// <summary>Zustand des Gates nach dem Rahmen.</summary>
    public GateZustand Gate;

    /// <summary>Der Echoschutz hat diesen Rahmen als Echo gesperrt (Gegenprüfung C2a, M4: ein gesperrtes Echo beantwortet den Funk-Anfang).</summary>
    public bool EchoGesperrt;

    /// <summary>Eine Phase der Sprachaktivierung geht gerade hinaus (nicht verworfen).</summary>
    public bool VaSendet;

    /// <summary>Die Sprechtaste ist gedrückt (auch wenn der Wächter ihr Senden gesperrt hat).</summary>
    public bool Taste;

    /// <summary>Pegel der Funk-Wiedergabe des eigenen Mischers (ohne Klick und Hörprobe) in diesem Rahmen.</summary>
    public float FunkDb;

    /// <summary>In diesem Rahmen begann ein Funker hörbar zu spielen.</summary>
    public bool FunkAnfang;

    /// <summary>Platz des lautesten Funkers im Mischer, oder -1.</summary>
    public int FunkPlatz;

    /// <summary>Pegel der Echoreferenz (Loopback: Funk und Spiel), NaN ohne Echoreferenz.</summary>
    public float SpielDb;

    /// <summary>Die Echoreferenz war in den letzten 400 ms sprachartig (Echoschutz).</summary>
    public bool SpielSprachartig;

    /// <summary>Es gibt eine Echoreferenz.</summary>
    public bool SpielLaeuft;

    /// <summary>Gelernte Funkstimme vor der Pegelstufe (Grundpegel der Halteregel), NaN solange unbekannt.</summary>
    public float StimmeDb;
}

/// <summary>Die Funk-Wiedergabe der eigenen Seite, die der Wächter bei Echo leiser stellt (der Mischer): je Funker und als Ganzes, in dB (0 bis -6).</summary>
internal interface IEchoWiedergabe
{
    /// <summary>Die ganze Funk-Wiedergabe in JG TacRelay (nie Windows, nie das Spiel).</summary>
    float GesamtDb { get; set; }

    float StimmeDb(int platz);

    void StimmeSetzen(int platz, float db);
}

/// <summary>
/// Der Wächter der eigenen Seite (Klangstufe C2a): urteilt je Sprechphase der Sprachaktivierung nach der Formel (<see cref="Waechterformel"/>) und
/// meldet Echo, Spielton, Raumgeräusch und klemmende Taste; er stellt selbst fast nichts, sondern sagt den vorhandenen Reglern, was sie tun sollen
/// (Entwurf, "Ineinandergreifen"). Nur im Aufnahme-Thread, ohne Allokation.
/// <list type="bullet">
/// <item>Echo (Punktekonto ab ln(1000)): die gemessene Kopplung für den Echoschutz (<see cref="KopplungDb"/>, er korrigiert seine gelernte),
/// je weiterer Echo-Phase die durchkommende Stimme 2 dB leiser bis 6 dB, danach die ganze Funk-Wiedergabe bis 6 dB (<see cref="IEchoWiedergabe"/>);
/// nach 30 s unter 0 Entwarnung, dann alle 10 s 1 dB zurück, aber nicht über die Grenzlautstärke des Geräts (<see cref="GrenzeDb"/>, gesetzt,
/// wenn das Echo nach einer Freigabe wiederkam).</item>
/// <item>Spielton: Phasen mit nicht sprachartigem Spielton in den Lautsprechern und kaum Sprache; der Echoschutz verlangt mehr Abstand
/// (<see cref="GeraeuschZusatzDb"/>), bis 30 s ohne Verdacht.</item>
/// <item>Raumgeräusch (Q38): Phasen mit kaum Sprache oder mit Sprache deutlich (ab <see cref="RaumUnterStimmeDb"/>) unter der gelernten Funkstimme,
/// auch die eigene leise Stimme; nach <see cref="RaumPhasen"/> solchen Phasen die Linie gerade über dieses Niveau (<see cref="RaumWunschVorStufeDb"/>,
/// nie über Stimme minus 6 dB). Sprache auf Funkniveau nimmt sie schon im Anlauf zurück.</item>
/// <item>Klemmende Taste: 60 s gedrückt, weniger als ein Fünftel Sprache: Senden aus bis zum Neudruck (<see cref="TasteKlemmt"/>).</item>
/// </list>
/// Lange Phasen werden in Abschnitten zu 5 s beurteilt, damit auch eine nie schließende Sendung urteilt.
/// </summary>
internal sealed class EigenerWaechter
{
    /// <summary>Funk gilt als spielend ab diesem Pegel der Wiedergabe (gehalten 400 ms).</summary>
    public const float FunkAktivDb = -55f;

    /// <summary>Spielton aus den Lautsprechern zählt ab diesem Pegel der Echoreferenz (wie der Echoschutz, <c>Echoschutz.AktivAbDb</c>).</summary>
    public const float SpielAktivDb = -60f;

    /// <summary>Haltezeit des Funks: Laufzeit, Nachhall, Kette (wie der Echoschutz).</summary>
    public const int HalteRahmen = 40;

    /// <summary>Lange Phasen in Abschnitten von 5 s.</summary>
    public const int AbschnittRahmen = 500;

    /// <summary>Sprache, die im Mittel so weit unter der gelernten Funkstimme liegt, gilt als nicht für den Funk bestimmt (Q38: "etwa 10 dB").</summary>
    public const float RaumUnterStimmeDb = 9f;

    /// <summary>Sprache ab hier (Stimme minus 6 dB, die Obergrenze) ist Funkniveau.</summary>
    public const float FunkniveauUnterStimmeDb = 6f;

    /// <summary>So viele Raum-Phasen in 30 s bis zum Urteil ("nach wenigen Phasen").</summary>
    public const int RaumPhasen = 2;

    /// <summary>So viel über das 80. Perzentil der leisen Phasen hebt der Wächter die Linie.</summary>
    public const float RaumZuschlagDb = 10f;

    /// <summary>Der Wunsch geht höchstens bis Stimme minus 5 dB: So viel muss eine Phase in ihren lautesten 150 ms erreichen; Sprache auf Funkniveau schafft das.</summary>
    public const float RaumHoechstUnterStimmeDb = 2f;

    /// <summary>Unter diesem Sprachanteil hat eine Phase "kaum Sprache"; ebenso für die klemmende Taste.</summary>
    public const float KaumSpracheAnteil = 0.2f;

    /// <summary>Eine Phase zählt als Spielton nur unter diesem Sprachanteil (5 %).</summary>
    public const float SpielSpracheAnteil = 0.05f;

    /// <summary>Klemmende Taste: nach 60 s.</summary>
    public const int TasteRahmen = 6000;

    /// <summary>Schritt der Wiedergabe bei Echo und höchste Absenkung ("etwas leiser").</summary>
    public const float EchoSchrittDb = 2f;

    public const float EchoTiefstDb = Daempfungsworte.EtwasLeiserDb;

    /// <summary>Freigabe: alle 10 s 1 dB.</summary>
    public const int FreigabeAbstandRahmen = 1000;

    public const float FreigabeSchrittDb = 1f;

    private readonly WaechterKalibrierung _k;
    private readonly IEchoWiedergabe? _wg;
    private readonly Referenzspur _spur = new();
    private readonly Funklage _lage = new();
    private readonly Punktekonto _konto = new();
    private readonly Phasenbeobachter _beob = new();
    private readonly long[] _anfaenge = new long[32];
    private readonly bool[] _anfangOffen = new bool[32];
    private readonly int[] _plaetze = new int[16];
    private readonly long[] _raumZeiten = new long[8];
    private readonly float[] _raumNiveau = new float[8];
    private readonly int[] _raumDauer = new int[8];
    private int _anfangPos;
    private int _platzPos;
    private int _raumPos;
    private long _nr;
    private int _halte;

    // Phase
    private bool _phase;
    private bool _gesendet;
    private bool _verworfen;
    private bool _zurueck;
    private bool _ersterAbschnitt;
    private bool _eigenerBeginn;

    // Taste
    private int _tasteRahmen;
    private int _tasteSprache;

    // Echo
    private bool _echoVorher;
    private long _freigabeAb = -1;
    private bool _freigegebenSeitEcho;
    private float _kopplungKandidat = float.NaN;
    private float _verzugKandidat = float.NaN;
    private int _verzugRahmenKandidat = -1;
    private long _kopplungKandidatAb;

    // Spielton und Raum
    private long _letzteSpielPhase = long.MinValue / 2;
    private int _spielPhasen;
    private long _letzteRaumPhase = long.MinValue / 2;
    private long _raumAufgehobenAb = long.MinValue / 2;

    // Rauschboden vor der Pegelstufe und Abstand der Funkstimme dazu (aus Phasen auf Funkniveau dieser Sitzung)
    private float _bodenDb = float.NaN;
    private long _letzteEchoSperre = long.MinValue / 2;
    private float _stimmeAbstandDb = float.NaN;

    // Äußerung: Sprachrahmen ohne Pause von 500 ms, unabhängig vom Gate
    private double _aeusserungLeistung;
    private int _aeusserungRahmen;
    private int _seitSprache = int.MaxValue / 2;

    public EigenerWaechter(WaechterKalibrierung? kalibrierung = null, IEchoWiedergabe? wiedergabe = null)
    {
        _k = kalibrierung ?? WaechterKalibrierung.AbWerk;
        _wg = wiedergabe;
        Array.Fill(_anfaenge, long.MinValue / 2);
        Array.Fill(_raumNiveau, float.NegativeInfinity);
    }

    public WaechterKalibrierung Kalibrierung => _k;

    /// <summary>Schalter "Auto Dauersend-Dämpfung": aus heißt urteilen, aber nichts stellen (keine Kopplung, keine Wiedergabe, keine Linie, kein Abstand).</summary>
    public bool Handeln { get; set; } = true;

    /// <summary>Das wichtigste wirkende Urteil: klemmende Taste vor Echo vor Spielton vor Raumgeräusch.</summary>
    public DauersendUrteil Urteil =>
        TasteKlemmt ? DauersendUrteil.KlemmendeTaste
        : EchoErkannt ? DauersendUrteil.Echo
        : SpieltonErkannt ? DauersendUrteil.Spielton
        : RaumErkannt ? DauersendUrteil.Raumgeraeusch
        : DauersendUrteil.Keins;

    public bool EchoErkannt => _konto.Erkannt;

    public float EchoKonto => _konto.Stand;

    /// <summary>
    /// Die Pegelstufe darf lernen: kein Urteil, und die laufende Phase ist nicht deutlich leiser als die Funkstimme (Entwurf, Ineinandergreifen 1).
    /// Spielton sperrt das Lernen nicht: Gelernt wird ohnehin nur aus Rahmen mit Sprachwahrscheinlichkeit ab 0,9 ohne Echoverdacht, und der Rest
    /// eines Spieltons erreicht sie kaum je; gesperrt verpasste die eigene Stimme mit Spielton über Lautsprecher mehr Anlaute (SpieltonTests).
    /// </summary>
    public bool LernenErlaubt { get; private set; } = true;

    /// <summary>Gemessene Kopplung der Echo-Phasen für den Echoschutz (Mikrofon minus höchste Wiedergabe), NaN solange keine.</summary>
    public float KopplungDb { get; private set; } = float.NaN;

    /// <summary>Verschiebung des Echos gegen die Funk-Wiedergabe in Rahmen (bester Gleichlauf der Echo-Phasen), -1 solange keine.</summary>
    public int EchoVerzugRahmen { get; private set; } = -1;

    /// <summary>Kopplung in dieser Verschiebung (Mikrofon minus Funk zur selben Zeit des Echos), NaN solange keine; für den Echoschutz ohne Echoreferenz.</summary>
    public float KopplungVerzugDb { get; private set; } = float.NaN;

    /// <summary>Zählt jede neue Messung von <see cref="KopplungDb"/>; die Sendekette übernimmt sie einmal.</summary>
    public int KopplungFassung { get; private set; }

    public bool RaumErkannt { get; private set; }

    /// <summary>Wunsch an die Linie bei Raumgeräusch in dBFS vor der Pegelstufe; minus unendlich heißt keiner.</summary>
    public float RaumWunschVorStufeDb { get; private set; } = float.NegativeInfinity;

    public bool SpieltonErkannt { get; private set; }

    /// <summary>So viel mehr Abstand verlangt der Echoschutz bei Spielton (0 bis 6 dB).</summary>
    public float GeraeuschZusatzDb { get; private set; }

    /// <summary>Die Sprechtaste klemmt: Senden aus, bis sie losgelassen und neu gedrückt wird.</summary>
    public bool TasteKlemmt { get; private set; }

    /// <summary>Grenzlautstärke des Ausgabegeräts: Die Freigabe stellt die ganze Funk-Wiedergabe nicht über diesen Wert (0 bis -6 dB).</summary>
    public float GrenzeDb { get; set; }

    public long Rahmennummer => _nr;

    /// <summary>Unbeantwortete und beantwortete Funk-Anfänge (Einmessen).</summary>
    public long Unbeantwortet { get; private set; }

    public long Beantwortet { get; private set; }

    /// <summary>Wie oft der Wunsch bei Raumgeräusch zurückging: im Anlauf auf Funkniveau, nach einer Phase auf Funkniveau, nach 30 s Ruhe (Diagnose).</summary>
    public long[] RaumAufgehoben { get; } = new long[3];

    /// <summary>Diagnose im Prüfstand (nur mit FUNK_C2_VERLAUF).</summary>
    public Action<string>? Diagnose { get; set; }

    /// <summary>Für das Einmessen im Prüfstand: jede beurteilte Phase mit den Punkten für Überlappung, Anlauf und Gleichlauf.</summary>
    public Action<Beobachtung, float, float, float>? Beobachtet { get; set; }

    public void Rahmen(in WaechterRahmen r)
    {
        _nr++;
        var funk = float.IsFinite(r.FunkDb) ? r.FunkDb : Referenzspur.BodenDb;
        _spur.Hinzufuegen(funk);
        if (funk >= FunkAktivDb) _halte = HalteRahmen;
        else if (_halte > 0) _halte--;
        var laeuft = _halte > 0;
        _platzPos = (_platzPos + 1) % _plaetze.Length;
        _plaetze[_platzPos] = funk >= FunkAktivDb ? r.FunkPlatz : -1;
        var gateZu = r.Gate == GateZustand.Leise;
        if (r.FunkAnfang && funk > Referenzspur.BodenDb)
        {
            _anfangPos = (_anfangPos + 1) % _anfaenge.Length;
            _anfaenge[_anfangPos] = _nr;
            _anfangOffen[_anfangPos] = gateZu && !r.Taste;
        }

        if (r.EchoGesperrt) _letzteEchoSperre = _nr;
        TasteFuehren(r);
        BodenFuehren(r);
        AeusserungFuehren(r);
        _eigenerBeginn = false;
        PhaseFuehren(r, laeuft);
        Unbeantwortete();
        _lage.Rahmen(laeuft, r.FunkAnfang, _eigenerBeginn, !gateZu || r.Taste);
        _konto.Takt(_nr);
        EchoFuehren();
        Fristen(r);
        LernenErlaubt = Urteil is DauersendUrteil.Keins or DauersendUrteil.Spielton && !(_phase && LaufendLeise(r.StimmeDb));
    }

    /// <summary>So viel weniger Abstand zum Rauschen als die Funkstimme hat leise Sprache im Raum mindestens (Abstand der Funkstimme minus 6 dB).</summary>
    public const float RaumAbstandVerlustDb = 6f;

    /// <summary>Rauschboden vor der Pegelstufe (dBFS): folgt Rahmen ohne Sprache rasch nach unten, langsam nach oben (Zeitkonstante 5 s). NaN vor dem ersten.</summary>
    public float BodenDb => _bodenDb;

    /// <summary>Abstand der Funkstimme zum Rauschboden (dB), gemessen an Phasen auf Funkniveau dieser Sitzung; NaN, solange keine war.</summary>
    public float StimmeAbstandDb => _stimmeAbstandDb;

    private void BodenFuehren(in WaechterRahmen r)
    {
        if (!(r.P < 0.5f) || float.IsNaN(r.EigenDb)) return;
        // Digitale Stille zählt als -80 dBFS (sonst gäbe es ohne Rauschen keinen Abstand).
        var x = MathF.Max(r.EigenDb, Referenzspur.BodenDb);
        if (float.IsNaN(_bodenDb)) _bodenDb = x;
        else _bodenDb += (x < _bodenDb ? 0.1f : 0.002f) * (x - _bodenDb);
    }

    /// <summary>
    /// Eine Äußerung endet nach so vielen Rahmen ohne Sprache (300 ms); kürzere Pausen gehören zum Satz. Mit 500 ms verschmolz ein leiser Satz mit
    /// dem nächsten normalen (Korpus: 350 ms Pause), der Wunsch ging erst spät zurück, und Wortanfänge fehlten (MessungKlangC1: 15 statt 9 von 492).
    /// </summary>
    public const int AeusserungPauseRahmen = 30;

    /// <summary>
    /// Die laufende Äußerung (Sprachrahmen ab Wahrscheinlichkeit 0,9, Pausen unter 500 ms), unabhängig davon, was das Gate daraus macht: Ihr
    /// mittlerer Pegel entscheidet, ob Sprache auf Funkniveau ist. Eine Phase allein reicht dafür nicht: Gibt das Gate von einem leisen Satz nur
    /// das lauteste Stück frei, liegt diese Phase fast auf Funkniveau, der Satz aber nicht (Prüfstand: leise Sätze nahmen so den Wunsch zurück, und
    /// die nächsten gingen wieder hinaus).
    /// </summary>
    private void AeusserungFuehren(in WaechterRahmen r)
    {
        var sprache = r.P >= Pegelstufe.SprachSchwelle && float.IsFinite(r.EigenDb) && r.EigenDb > Referenzspur.BodenDb;
        if (!sprache)
        {
            if (_seitSprache < int.MaxValue / 2) _seitSprache++;
            return;
        }
        // Die vorige Äußerung gilt bis zur nächsten Sprache (das Phasenende nach dem Nachlauf urteilt noch über sie).
        if (_seitSprache >= AeusserungPauseRahmen)
        {
            _aeusserungLeistung = 0;
            _aeusserungRahmen = 0;
        }
        _seitSprache = 0;
        _aeusserungLeistung += Math.Pow(10.0, r.EigenDb / 10.0);
        _aeusserungRahmen++;
    }

    /// <summary>
    /// So viele Sprachrahmen der Äußerung (200 ms Sprache) entscheiden, ob sie auf Funkniveau ist und den Wunsch zurücknimmt; gezählt ab ihrem Anfang,
    /// nicht ab dem Anfang der Phase (gibt das Gate von einem leisen Satz nur ein lautes Stück frei, zählt der leise Rest davor mit). Länger zu
    /// warten kostet Wortanfänge: Hält das Gate einen normalen Satz nach leisem Reden zurück, geht beim Zurücknehmen nur der Vorlauf (150 ms) mit
    /// hinaus (500 ms: 15 statt 9 von 492 Anfängen angeschnitten, MessungKlangC1).
    /// </summary>
    public const int AeusserungFunkniveauRahmen = 20;

    /// <summary>Mittlerer Pegel der laufenden Äußerung (Leistung), NaN ohne Sprache.</summary>
    public float AeusserungPegelDb => _aeusserungRahmen == 0 ? float.NaN : (float)(10.0 * Math.Log10(_aeusserungLeistung / _aeusserungRahmen));

    public int AeusserungSprachRahmen => _aeusserungRahmen;

    private void TasteFuehren(in WaechterRahmen r)
    {
        if (!r.Taste)
        {
            _tasteRahmen = 0;
            _tasteSprache = 0;
            TasteKlemmt = false;
            return;
        }
        _tasteRahmen++;
        if (r.P >= Pegelstufe.SprachSchwelle) _tasteSprache++;
        if (!TasteKlemmt && _tasteRahmen >= TasteRahmen && _tasteSprache < KaumSpracheAnteil * _tasteRahmen) TasteKlemmt = true;
    }

    private void PhaseFuehren(in WaechterRahmen r, bool laeuft)
    {
        var aktiv = !r.Taste && r.Gate != GateZustand.Leise;
        if (!_phase)
        {
            if (!aktiv) return;
            _phase = true;
            _gesendet = false;
            _verworfen = false;
            _zurueck = false;
            _ersterAbschnitt = true;
            _beob.Beginnen(_nr, AnlaufPruefen());
        }
        else if (!aktiv)
        {
            if (_gesendet && !_verworfen) Auswerten(r.StimmeDb);
            else if (_zurueck && RaumErkannt) ZurueckgehalteneBeurteilen(r.StimmeDb);
            _phase = false;
            return;
        }
        if (r.Gate == GateZustand.Zurueckgehalten) _zurueck = true;
        if (r.Gate is GateZustand.Senden or GateZustand.Nachlauf)
        {
            if (r.VaSendet)
            {
                if (!_gesendet) _eigenerBeginn = true;
                _gesendet = true;
            }
            else
            {
                // Hörprobe, Einmessen: Diese Phase geht nicht hinaus und wird nicht beurteilt.
                _verworfen = true;
            }
        }
        var spielNichtSprache = r.SpielLaeuft && !r.SpielSprachartig && r.SpielDb >= SpielAktivDb;
        _beob.Rahmen(r.EigenDb, r.EingangDb, r.WiedergabeDb, r.P, laeuft, _spur, _plaetze[(_platzPos + 6) % _plaetze.Length], spielNichtSprache,
            r.Gate == GateZustand.Nachlauf);
        RaumZuruecknehmen(r);
        if (_gesendet && !_verworfen && _beob.Anzahl >= AbschnittRahmen)
        {
            Auswerten(r.StimmeDb);
            _ersterAbschnitt = false;
            _beob.Beginnen(_nr, false);
        }
    }

    /// <summary>Beginnt die Phase 50 bis 400 ms nach einem Funk-Anfang? Solche Anfänge gelten als beantwortet.</summary>
    private bool AnlaufPruefen()
    {
        var ja = false;
        for (var i = 0; i < _anfaenge.Length; i++)
        {
            var d = _nr - _anfaenge[i];
            if (d is < Waechterformel.AnlaufVonRahmen or > Waechterformel.AnlaufBisRahmen) continue;
            ja = true;
            if (_anfangOffen[i])
            {
                _anfangOffen[i] = false;
                Beantwortet++;
            }
        }
        return ja;
    }

    /// <summary>Funk-Anfänge bei geschlossenem Gate, auf die in 400 ms keine Phase folgte.</summary>
    private void Unbeantwortete()
    {
        for (var i = 0; i < _anfaenge.Length; i++)
        {
            if (!_anfangOffen[i] || _nr - _anfaenge[i] <= Waechterformel.AnlaufBisRahmen) continue;
            _anfangOffen[i] = false;
            // Hat der Echoschutz in diesem Fenster Echo gesperrt, war das Echo da: kein Beleg gegen Echo (sonst entwarnte das Urteil nach 1 bis 2 min,
            // weil der Funkspur-Schutz das Echo sperrt und der Wächter keine Echo-Phasen mehr sieht; Gegenprüfung C2a, M4).
            if (_nr - _letzteEchoSperre <= Waechterformel.AnlaufBisRahmen)
            {
                Beantwortet++;
                continue;
            }
            Unbeantwortet++;
            _konto.Buchen(Waechterformel.Unbeantwortet(_lage.EigenePhasenJeS, _k), _nr);
        }
    }

    private void Auswerten(float stimmeDb)
    {
        var b = _beob.Ergebnis();
        var pO = Waechterformel.Ueberlappung(b.Ueberlappung, _lage.Anteil, _k);
        var pA = _ersterAbschnitt ? Waechterformel.Anlauf(b.Anlauf, _lage.AnfaengeJeS, _k) : 0f;
        var pG = b.Ueberlappung ? _k.Gleichlauf(b.Gleichlauf) : 0f;
        var summe = pO + pA + pG;
        _konto.Buchen(summe, _nr);
        Beobachtet?.Invoke(b, pO, pA, pG);
        var echoArtig = summe > 0f && b.Ueberlappung;
        // Die Kopplung nur aus Phasen, die klar nach Echo aussehen (Gleichlauf spricht dafür): Eine eigene Phase über Funk ergäbe sonst die
        // eigene Stimme minus Funk als "Kopplung" und sperrte danach eigene Sprache.
        if (echoArtig && pG > 0f && summe > 1f && float.IsFinite(b.KopplungDb))
        {
            // Die Kopplung der Phasen, die zum Urteil führen: die höchste der letzten 30 s.
            if (!float.IsFinite(_kopplungKandidat) || _nr - _kopplungKandidatAb > Waechterformel.EntwarnungRahmen)
            {
                _kopplungKandidat = b.KopplungDb;
                _verzugKandidat = float.NaN;
            }
            else
            {
                _kopplungKandidat = MathF.Max(_kopplungKandidat, b.KopplungDb);
            }
            if (float.IsFinite(b.KopplungVerzugDb))
            {
                _verzugKandidat = float.IsFinite(_verzugKandidat) ? MathF.Max(_verzugKandidat, b.KopplungVerzugDb) : b.KopplungVerzugDb;
                _verzugRahmenKandidat = b.BesteVerschiebung;
            }
            _kopplungKandidatAb = _nr;
        }
        if (_konto.Erkannt) EchoHandeln(b, echoArtig);

        // Spielton nur aus Phasen fast ganz ohne Sprache (unter 5 %): Spricht der Funker in der Phase, ist sie seine, auch wenn Spielton mitläuft
        // (SpieltonTests, Lautsprecher 12 dB, 60 s Rede: sonst urteilte er Spielton und sperrte mit dem Abstand eigene Anlaute).
        var spielPhase = b.SpielNichtSpracheAnteil >= 0.5f && b.SprachAnteil < SpielSpracheAnteil;
        if (spielPhase) SpielPhase();
        if (float.IsFinite(stimmeDb)) RaumBeurteilen(b, stimmeDb, spielPhase, echoArtig);
    }

    private void EchoHandeln(in Beobachtung b, bool echoArtig)
    {
        if (!_echoVorher)
        {
            _echoVorher = true;
            _freigabeAb = -1;
            // Kam das Echo nach einer Freigabe wieder, war diese Lautstärke zu viel: die Grenze des Geräts liegt 2 dB darunter.
            if (_freigegebenSeitEcho && _wg is not null)
            {
                GrenzeDb = MathF.Max(EchoTiefstDb, MathF.Min(GrenzeDb, _wg.GesamtDb - EchoSchrittDb));
                if (Handeln) _wg.GesamtDb = MathF.Min(_wg.GesamtDb, GrenzeDb);
            }
            if (Handeln && float.IsFinite(_kopplungKandidat)) KopplungSetzen(_kopplungKandidat, _verzugRahmenKandidat, _verzugKandidat);
        }
        if (!echoArtig || !Handeln) return;
        if (float.IsFinite(b.KopplungDb) && _k.Gleichlauf(b.Gleichlauf) > 0f) KopplungSetzen(b.KopplungDb, b.BesteVerschiebung, b.KopplungVerzugDb);
        if (_wg is null) return;
        // Zuerst die durchkommende Stimme, dann die ganze Funk-Wiedergabe; je Phase ein Schritt.
        var platz = b.FunkPlatz;
        if (platz >= 0 && _wg.StimmeDb(platz) > EchoTiefstDb + 0.01f) _wg.StimmeSetzen(platz, MathF.Max(EchoTiefstDb, _wg.StimmeDb(platz) - EchoSchrittDb));
        else if (_wg.GesamtDb > EchoTiefstDb + 0.01f) _wg.GesamtDb = MathF.Max(EchoTiefstDb, _wg.GesamtDb - EchoSchrittDb);
    }

    private void KopplungSetzen(float db, int verzug, float verzugDb)
    {
        var neu = false;
        if (!float.IsFinite(KopplungDb) || db > KopplungDb)
        {
            KopplungDb = db;
            neu = true;
        }
        if (float.IsFinite(verzugDb) && (!float.IsFinite(KopplungVerzugDb) || verzugDb > KopplungVerzugDb || verzug != EchoVerzugRahmen))
        {
            KopplungVerzugDb = float.IsFinite(KopplungVerzugDb) ? MathF.Max(KopplungVerzugDb, verzugDb) : verzugDb;
            EchoVerzugRahmen = verzug;
            neu = true;
        }
        if (neu) KopplungFassung++;
    }

    /// <summary>Nach der Entwarnung: alle 10 s 1 dB zurück, die ganze Wiedergabe nicht über die Grenze des Geräts.</summary>
    private void EchoFuehren()
    {
        if (_konto.Erkannt) return;
        if (_echoVorher)
        {
            _echoVorher = false;
            _freigabeAb = _nr;
            _freigegebenSeitEcho = true;
        }
        if (_freigabeAb < 0 || _wg is null || !Handeln) return;
        if ((_nr - _freigabeAb) % FreigabeAbstandRahmen != 0 || _nr == _freigabeAb) return;
        var noch = false;
        for (var p = 0; p < 64; p++)
        {
            var s = _wg.StimmeDb(p);
            if (s >= 0f) continue;
            _wg.StimmeSetzen(p, MathF.Min(0f, s + FreigabeSchrittDb));
            noch = true;
        }
        var g = _wg.GesamtDb;
        if (g < GrenzeDb)
        {
            _wg.GesamtDb = MathF.Min(GrenzeDb, g + FreigabeSchrittDb);
            noch = true;
        }
        if (!noch) _freigabeAb = -1;
    }

    private void SpielPhase()
    {
        _spielPhasen = _nr - _letzteSpielPhase > Waechterformel.EntwarnungRahmen ? 1 : _spielPhasen + 1;
        _letzteSpielPhase = _nr;
        if (_spielPhasen < 2) return;
        if (!SpieltonErkannt)
        {
            SpieltonErkannt = true;
            GeraeuschZusatzDb = Handeln ? 2f : 0f;
        }
        else if (Handeln)
        {
            GeraeuschZusatzDb = MathF.Min(6f, GeraeuschZusatzDb + 2f);
        }
    }

    /// <summary>
    /// Raumgeräusch (Q38): Sprache deutlich unter der Funkstimme, auch die eigene leise, oder eine Phase mit kaum Sprache, die weder Echo noch Spielton
    /// ist. Nach <see cref="RaumPhasen"/> solchen Phasen in 30 s wünscht der Wächter eine Linie gerade über ihr Niveau (mittlerer Pegel der Sprache
    /// plus 3 dB, bei kaum Sprache das 80. Perzentil plus 3 dB), nie über die Obergrenze; die GateFuehrung macht daraus Linie und Freigabe. Eine Phase
    /// auf Funkniveau nimmt sie zurück.
    /// </summary>
    /// <summary>Schalter "Leises Reden im Raum aussperren" (<see cref="DaempfungEinstellung.LeisesRedenSperren"/>): ohne ihn kein Raumgeräusch.</summary>
    public bool RaumAn { get; set; }

    private void RaumBeurteilen(in Beobachtung b, float stimmeDb, bool spielPhase, bool echoArtig)
    {
        if (!RaumAn)
        {
            if (RaumErkannt) RaumAufheben();
            return;
        }
        var abstand = b.PegelDb - _bodenDb;
        // Leise heißt: deutlich unter der Funkstimme UND mit deutlich weniger Abstand zum Rauschen als sie. Ist das Mikrofon nur leiser gestellt
        // (Pegel in Windows, anderes Gerät mit derselben Kennung), sinkt das Rauschen mit, der Abstand bleibt, und das ist die Funkstimme
        // (Prüfstand: Mikrofon 15 dB leiser, ohne diese Prüfung gingen nur 3 bis 18 % der Rahmen hinaus, Raumgeräusch ohne Ende). Ohne eine
        // Phase auf Funkniveau in dieser Sitzung ist der Abstand der Stimme unbekannt; dann gilt leise Sprache nicht als Raumgeräusch.
        var leise = b.SprachRahmen >= 10 && b.PegelDb <= stimmeDb - RaumUnterStimmeDb
            && float.IsFinite(_stimmeAbstandDb) && float.IsFinite(abstand) && abstand <= _stimmeAbstandDb - RaumAbstandVerlustDb;
        var kaumSprache = b.SprachAnteil < KaumSpracheAnteil && !spielPhase && !echoArtig;
        var funkniveau = b.SprachRahmen >= 10 && b.PegelDb >= stimmeDb - FunkniveauUnterStimmeDb && b.SprachAnteil >= 0.3f;
        if (funkniveau && !echoArtig && float.IsFinite(abstand))
            _stimmeAbstandDb = float.IsNaN(_stimmeAbstandDb) ? abstand : _stimmeAbstandDb + (0.2f * (abstand - _stimmeAbstandDb));
        if (funkniveau && _aeusserungRahmen >= AeusserungFunkniveauRahmen && AeusserungPegelDb >= stimmeDb - FunkniveauUnterStimmeDb)
        {
            if (RaumErkannt) RaumAufgehoben[1]++;
            RaumAufheben();
            return;
        }
        if (!(leise || kaumSprache) || echoArtig) return;
        _letzteRaumPhase = _nr;
        _raumPos = (_raumPos + 1) % _raumZeiten.Length;
        _raumZeiten[_raumPos] = _nr;
        _raumNiveau[_raumPos] = leise ? b.PegelDb : b.PerzentilDb;
        _raumDauer[_raumPos] = leise ? b.SprachRahmen : b.Rahmen;
        var anzahl = 0;
        var dauer = 0;
        var niveau = float.NegativeInfinity;
        for (var i = 0; i < _raumZeiten.Length; i++)
        {
            if (_nr - _raumZeiten[i] > Waechterformel.EntwarnungRahmen || _raumZeiten[i] == 0) continue;
            anzahl++;
            dauer += _raumDauer[i];
            if (float.IsFinite(_raumNiveau[i])) niveau = MathF.Max(niveau, _raumNiveau[i]);
        }
        // "Anhaltend" (Q38): wenige Phasen mit zusammen mindestens 2,5 s leiser Sprache, ohne Funkniveau dazwischen (das setzt zurück). Einzelne leise
        // Stücke eigener Sätze (anschwellender Anfang, verklingendes Ende) urteilten sonst im Gespräch (Abnahme, 1. Lauf: einmal nach 0,5 s und 1,2 s).
        // Wer gerade erst aufgehoben hat und wieder leise hört (Kinder reden weiter), urteilt schon nach einer Phase von 1 s.
        var gedaechtnis = _nr - _raumAufgehobenAb < 2 * Waechterformel.EntwarnungRahmen;
        if (gedaechtnis ? dauer < RaumMindestRahmen / 2 : anzahl < RaumPhasen || dauer < RaumMindestRahmen) return;
        RaumErkannt = true;
        RaumWunschVorStufeDb = Handeln && float.IsFinite(niveau)
            ? MathF.Min(stimmeDb - RaumHoechstUnterStimmeDb, niveau + RaumZuschlagDb)
            : float.NegativeInfinity;
    }

    /// <summary>
    /// Sprache auf Funkniveau in einer beginnenden oder laufenden Phase nimmt den Wunsch zügig zurück (Q38): sobald die ersten
    /// <see cref="FunkniveauRahmen"/> Sprachrahmen der Phase im Mittel ab Stimme minus 6 dB liegen, also nach etwa 100 ms, noch im Anlauf. Einzelne
    /// laute Silben leiser Sprache reichen dafür nicht (ein Mittel, kein Rahmen).
    /// </summary>
    private void RaumZuruecknehmen(in WaechterRahmen r)
    {
        if (!RaumErkannt || !float.IsFinite(r.StimmeDb)) return;
        if (_beob.LaufendSprachRahmen < FunkniveauRahmen || _beob.LaufendPegelDb < r.StimmeDb - FunkniveauAnlaufUnterStimmeDb) return;
        if (_aeusserungRahmen < AeusserungFunkniveauRahmen || AeusserungPegelDb < r.StimmeDb - FunkniveauAnlaufUnterStimmeDb) return;
        RaumAufgehoben[0]++;
        Diagnose?.Invoke(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Anlauf {_nr} mittel {_beob.LaufendPegelDb:0.0} aus {_beob.LaufendSprachRahmen}, Äußerung {AeusserungPegelDb:0.0} aus {_aeusserungRahmen}, stimme {r.StimmeDb:0.0} gate {r.Gate} eigen {r.EigenDb:0.0}"));
        RaumAufheben();
    }

    /// <summary>So viele Sprachrahmen einer Phase entscheiden, ob sie auf Funkniveau ist.</summary>
    public const int FunkniveauRahmen = 20;

    /// <summary>
    /// Im Anlauf gilt eine Phase erst ab Stimme minus 3 dB als Funkniveau: Bei angehobener Linie beginnt auch leise Sprache nur mit ihren lautesten
    /// Silben, die ersten Sprachrahmen liegen also an der Linie (Stimme minus 6 dB); mit 6 dB nahm der Wächter den Wunsch gleich wieder zurück
    /// (Prüfstand, leise zum Sohn: 5 bis 10 Mal je Szene).
    /// </summary>
    public const float FunkniveauAnlaufUnterStimmeDb = 4f;

    /// <summary>Zusammen so viel leise Sprache (Sprachrahmen) bis zum Urteil Raumgeräusch: 2,5 s.</summary>
    public const int RaumMindestRahmen = 250;

    private void RaumAufheben()
    {
        if (RaumErkannt) _raumAufgehobenAb = _nr;
        RaumErkannt = false;
        RaumWunschVorStufeDb = float.NegativeInfinity;
        Array.Clear(_raumZeiten);
        Array.Clear(_raumDauer);
        Array.Fill(_raumNiveau, float.NegativeInfinity);
    }

    /// <summary>
    /// Eine Phase, die das Gate wegen des Wunsches zurückhielt (sie ging nicht hinaus): Ist sie leise oder kaum Sprache, hält sie das Urteil am Leben
    /// (sonst endete es nach 30 s, obwohl im Raum weiter leise gesprochen wird, und zwei leise Phasen gingen wieder hinaus).
    /// </summary>
    private void ZurueckgehalteneBeurteilen(float stimmeDb)
    {
        if (!float.IsFinite(stimmeDb)) return;
        var b = _beob.Ergebnis();
        var leise = b.SprachRahmen >= 10 && b.PegelDb <= stimmeDb - RaumUnterStimmeDb;
        if (leise || b.SprachAnteil < KaumSpracheAnteil) _letzteRaumPhase = _nr;
    }

    private bool LaufendLeise(float stimmeDb) =>
        float.IsFinite(stimmeDb) && _beob.LaufendSprachRahmen >= 20 && _beob.LaufendPegelDb <= stimmeDb - RaumUnterStimmeDb;

    /// <summary>30 s ohne Verdacht: Spielton und Raumgeräusch vorbei.</summary>
    private void Fristen(in WaechterRahmen r)
    {
        if (SpieltonErkannt && _nr - _letzteSpielPhase > Waechterformel.EntwarnungRahmen)
        {
            SpieltonErkannt = false;
            GeraeuschZusatzDb = 0f;
            _spielPhasen = 0;
        }
        if (!Handeln) GeraeuschZusatzDb = 0f;
        if (RaumErkannt && (_nr - _letzteRaumPhase > Waechterformel.EntwarnungRahmen || !float.IsFinite(r.StimmeDb)))
        {
            RaumAufgehoben[2]++;
            RaumAufheben();
        }
        if (!Handeln && RaumErkannt) RaumWunschVorStufeDb = float.NegativeInfinity;
    }

    /// <summary>Alles vergessen (Mikrofon- oder Gerätewechsel, "wieder normal"); die Grenze des Geräts bleibt.</summary>
    public void Zuruecksetzen()
    {
        _konto.Zuruecksetzen();
        _lage.Zuruecksetzen();
        _spur.Zuruecksetzen();
        _phase = false;
        _echoVorher = false;
        _freigabeAb = -1;
        SpieltonErkannt = false;
        GeraeuschZusatzDb = 0f;
        RaumAufheben();
        TasteKlemmt = false;
        _tasteRahmen = _tasteSprache = 0;
        Array.Fill(_anfaenge, long.MinValue / 2);
        Array.Clear(_anfangOffen);
        _kopplungKandidat = float.NaN;
        _verzugKandidat = float.NaN;
        _verzugRahmenKandidat = -1;
        EchoVerzugRahmen = -1;
        KopplungVerzugDb = float.NaN;
        _bodenDb = float.NaN;
        _stimmeAbstandDb = float.NaN;
        _letzteEchoSperre = long.MinValue / 2;
        _aeusserungLeistung = 0;
        _aeusserungRahmen = 0;
    }
}
