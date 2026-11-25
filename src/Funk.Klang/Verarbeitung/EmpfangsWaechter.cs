// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Der Wächter der Empfängerseite im <see cref="Mischer"/> (Klangstufe C2a, Entwurf "Was Dauersenden ist", letzte Zeile, und Q29): je
/// ankommendem Funker dieselbe Formel wie auf der eigenen Seite, seine Hüllkurve gegen die der anderen Funker (Max echot Moritz), dazu Dauersenden
/// ohne Echo (60 s fast durchgehend, weniger als ein Fünftel Sprache). Ein solcher Funker wird bei mir schrittweise leiser, je Schritt 2 dB, höchstens
/// bis zur gewählten Stufe; nach der Entwarnung langsam zurück (1 dB je 3 s). Sein Senden fasst er nie an. Dazu die langsame Angleichung der
/// Grundlautstärke je Funker (Q29): nach jeder Äußerung, in der Pause danach, höchstens 1 dB, insgesamt höchstens 10 dB und nur abwärts (zu laute Sender), ein
/// einzelner Ausreißer (ein Schrei) zählt nicht.
/// <para>
/// Anders als auf der eigenen Seite zählen unbeantwortete Anfänge hier nicht: Ob ein echoender Funker auf einen Anfang mit einer Phase antwortet,
/// entscheidet sein Gate, nicht sein Lautsprecher, und wer nur einen von vielen echot, sammelte sonst bei jedem Anfang der anderen Gegenpunkte
/// (Prüfstand, 1. Abnahme: bei 10 Sprechern erkannt und wieder entwarnt). Der Bezug des Gleichlaufs ist der Funker, der kurz vorher begann.
/// </para>
/// <para>
/// Läuft im Audio-Thread, ohne Allokation und ohne Sperre; was er stellt (Zieldämpfung, Angleichung), liest der Mischer im nächsten Block und
/// rampt es. Den Sprachanteil schätzt er nur für Funker, die seit 20 s fast durchgehend senden (höchstens zwei zugleich, RNNoise je etwa 0,1 ms
/// je Block); ohne Schätzer gibt es kein Urteil Dauersenden.
/// </para>
/// </summary>
internal sealed class EmpfangsWaechter
{
    /// <summary>Andere Funker gelten als spielend ab diesem Pegel (gehalten 400 ms).</summary>
    public const float AktivDb = -55f;

    /// <summary>Dauersenden: in 60 s mindestens so viel aktiv.</summary>
    public const float DauerAktivAnteil = 0.95f;

    /// <summary>Ab so viel Aktivität (mindestens 20 s gemessen) wird der Sprachanteil geschätzt.</summary>
    public const float KandidatAnteil = 0.8f;

    public const int KandidatAbRahmen = 2000;

    /// <summary>Mindestens 30 s geschätzter Sprachanteil, bevor Dauersenden gilt.</summary>
    public const int SpracheMindestRahmen = 3000;

    public const float SchrittDb = 2f;

    /// <summary>Zwischen zwei Schritten mindestens 1 s; ohne Pause spätestens nach 3 s (sonst nur an Phasengrenzen und in Pausen).</summary>
    public const int SchrittAbstand = 100;

    public const int SchrittSpaetestens = 300;

    /// <summary>Freigabe 1 dB je 3 s, in Pausen: langsamer als der Eingriff.</summary>
    public const int FreigabeAbstand = 300;

    public const float FreigabeSchrittDb = 1f;

    /// <summary>Angleichung auf das Ziel der Pegelstufe der Sender.</summary>
    public const float AngleichungZielDbfs = Pegelstufe.ZielDbfs;

    public const float AngleichungHoechstDb = 10f;

    /// <summary>Wer bis 3 dB am Ziel liegt (die Pegelstufe der anderen hält das), wird nicht angeglichen.</summary>
    public const float AngleichungTotzoneDb = 3f;

    public const float AngleichungSchrittDb = 1f;

    /// <summary>Eine Äußerung zählt ab 500 ms; ein Ausreißer über 10 dB erst als dritter in Folge.</summary>
    public const int AeusserungMindestRahmen = 50;

    public const float AusreisserDb = 10f;

    private sealed class Funker
    {
        public readonly Referenzspur Andere = new();
        public readonly Referenzspur Selbst = new();
        public long LetzterAnfang = long.MinValue / 2;
        public int Bezug = -1;
        public readonly Funklage Lage = new();
        public readonly Punktekonto Konto = new();
        public readonly Phasenbeobachter Beob = new();
        public readonly long[] Anfaenge = new long[16];
        public int AnfangPos;
        public bool Da;
        public bool Durchsage;
        public QuellenEreignis Ereignis;
        public float Db = Referenzspur.BodenDb;
        public float P = float.NaN;
        public int Halte;
        public bool Phase;
        public bool ErsterAbschnitt;
        public int Schaetzer = -1;
        public int KandRahmen;
        public int KandSprache;
        public bool Dauer;
        public float Ziel;
        public long LetzterSchritt = long.MinValue / 2;
        public long Gedaempft;
        public volatile int Urteil;
        public double UttLeistung;
        public int UttRahmen;
        public float Lang = float.NaN;
        public int Ausreisser;
        public float Angleichung;
        public bool Faellig;
        public bool NichtDaempfen;

        public void Zuruecksetzen()
        {
            Andere.Zuruecksetzen();
            Selbst.Zuruecksetzen();
            LetzterAnfang = long.MinValue / 2;
            Bezug = -1;
            Lage.Zuruecksetzen();
            Konto.Zuruecksetzen();
            Array.Fill(Anfaenge, long.MinValue / 2);
            Db = Referenzspur.BodenDb;
            P = float.NaN;
            Halte = 0;
            Phase = false;
            KandRahmen = KandSprache = 0;
            Dauer = false;
            Ziel = 0f;
            LetzterSchritt = long.MinValue / 2;
            Gedaempft = 0;
            Urteil = 0;
            UttLeistung = 0;
            UttRahmen = 0;
            Lang = float.NaN;
            Ausreisser = 0;
            Angleichung = 0f;
            Faellig = false;
            NichtDaempfen = false;
        }
    }

    private readonly WaechterKalibrierung _k;
    private readonly Funker[] _f;
    private readonly double[] _leistung;
    private readonly ISprachwahrscheinlichkeit[] _schaetzer;
    private readonly int[] _schaetzerPlatz;
    private long _nr;
    private long _folge;

    public EmpfangsWaechter(int plaetze, Func<ISprachwahrscheinlichkeit>? sprache = null, WaechterKalibrierung? kalibrierung = null, int sprachPlaetze = 2)
    {
        _k = kalibrierung ?? WaechterKalibrierung.AbWerk;
        _f = new Funker[plaetze];
        for (var i = 0; i < plaetze; i++)
        {
            _f[i] = new Funker();
            _f[i].Zuruecksetzen();
        }
        _leistung = new double[plaetze];
        _schaetzer = sprache is null ? [] : [.. Enumerable.Range(0, sprachPlaetze).Select(_ => sprache())];
        _schaetzerPlatz = new int[_schaetzer.Length];
        Array.Fill(_schaetzerPlatz, -1);
    }

    /// <summary>Schalter und Stufe; der Mischer setzt sie vor jedem Block.</summary>
    public DaempfungEinstellung Einstellung { get; set; } = DaempfungEinstellung.AbWerk;

    private volatile bool _angleichungAn = true;

    /// <summary>
    /// Klangmodul "Pegelausgleich" (Q29; seit 0.9.0 ein eigener Schalter, Sarevoc 06.10.2026, Q48): Angleichung zu lauter Funker an, ab Werk an. Bis 0.8.x
    /// hing sie am Schalter der Auto Dauersend-Dämpfung; jetzt wirkt sie auch ohne diese. Aus jedem Thread setzbar.
    /// </summary>
    public bool AngleichungAn
    {
        get => _angleichungAn;
        set => _angleichungAn = value;
    }

    /// <summary>Für das Einmessen im Prüfstand: jede beurteilte Phase eines Funkers (Platz, Beobachtung, Punkte).</summary>
    public Action<int, Beobachtung, float>? Beobachtet { get; set; }

    public float ZielDb(int platz) => _f[platz].Ziel;

    public float AngleichungDb(int platz) => _f[platz].Angleichung;

    public DauersendUrteil Urteil(int platz) => (DauersendUrteil)_f[platz].Urteil;

    public long GedaempftFolge(int platz) => Volatile.Read(ref _f[platz].Gedaempft);

    /// <summary>
    /// Audio-Thread, je Quelle im Block: <paramref name="neu"/> heißt ein neuer Funker auf dem Platz (alles vergessen), <paramref name="zuruecksetzen"/>
    /// die Wahl "wieder normal" (Urteil und Dämpfung weg), <paramref name="nichtDaempfen"/> die Wahl "für diesen Funker nicht mehr dämpfen".
    /// </summary>
    public void Quelle(int platz, bool neu, bool zuruecksetzen, bool nichtDaempfen, ReadOnlySpan<float> mono, QuellenEreignis ereignis, bool durchsage)
    {
        var f = _f[platz];
        if (neu)
        {
            SchaetzerLoesen(platz);
            f.Zuruecksetzen();
        }
        if (zuruecksetzen)
        {
            f.Konto.Zuruecksetzen();
            f.Dauer = false;
            f.KandRahmen = f.KandSprache = 0;
            f.Ziel = 0f;
            f.Urteil = 0;
        }
        f.NichtDaempfen = nichtDaempfen;
        f.Da = true;
        f.Durchsage = durchsage;
        f.Ereignis = ereignis;
        var leistung = Pegel.Leistung(mono);
        _leistung[platz] = float.IsFinite(leistung) ? leistung : 0;
        f.Db = Pegel.LeistungZuDb(leistung);
        f.P = float.NaN;
        for (var i = 0; i < _schaetzer.Length; i++)
        {
            if (_schaetzerPlatz[i] == platz) f.P = _schaetzer[i].Schaetzen(mono);
        }
    }

    /// <summary>Audio-Thread, nach allen Quellen eines Blocks: Bezug der anderen, Beobachtungen, Urteile, Dämpfung und Angleichung.</summary>
    public void BlockEnde()
    {
        _nr++;
        double gesamt = 0;
        var anfaenge = 0;
        for (var p = 0; p < _f.Length; p++)
        {
            var f = _f[p];
            f.Selbst.Hinzufuegen(f.Da ? f.Db : Referenzspur.BodenDb);
            if (!f.Da) continue;
            gesamt += _leistung[p];
            if ((f.Ereignis & QuellenEreignis.Begonnen) == 0) continue;
            anfaenge++;
            if (!f.Durchsage) f.LetzterAnfang = _nr;
        }
        for (var p = 0; p < _f.Length; p++)
        {
            var f = _f[p];
            if (!f.Da) continue;
            f.Da = false;
            Fuehren(p, f, gesamt, anfaenge);
        }
    }

    private void Fuehren(int platz, Funker f, double gesamt, int anfaenge)
    {
        var e = f.Ereignis;
        var begonnen = (e & QuellenEreignis.Begonnen) != 0;
        var aktiv = (e & QuellenEreignis.Aktiv) != 0;
        var beendet = (e & QuellenEreignis.Beendet) != 0;
        var andere = Math.Max(gesamt - _leistung[platz], 1e-12);
        var andereDb = (float)(10.0 * Math.Log10(andere));
        f.Andere.Hinzufuegen(andereDb);
        if (andereDb >= AktivDb) f.Halte = EigenerWaechter.HalteRahmen;
        else if (f.Halte > 0) f.Halte--;
        var laeuft = f.Halte > 0;
        var andererAnfang = anfaenge - (begonnen ? 1 : 0) > 0;
        if (andererAnfang)
        {
            f.AnfangPos = (f.AnfangPos + 1) % f.Anfaenge.Length;
            f.Anfaenge[f.AnfangPos] = _nr;
        }

        var eigenerBeginn = false;
        if (begonnen && !f.Durchsage)
        {
            if (f.Phase) Auswerten(platz, f);
            f.Phase = true;
            f.ErsterAbschnitt = true;
            f.Beob.Beginnen(_nr, AnlaufPruefen(f));
            f.Bezug = BezugWaehlen(platz);
            eigenerBeginn = true;
        }
        if (f.Phase)
        {
            f.Beob.Rahmen(f.Db, f.Db, float.NaN, float.IsNaN(f.P) ? 1f : f.P, laeuft, f.Bezug >= 0 ? _f[f.Bezug].Selbst : f.Andere, -1, false);
            if (beendet || !aktiv)
            {
                Auswerten(platz, f);
                f.Phase = false;
            }
            else if (f.Beob.Anzahl >= EigenerWaechter.AbschnittRahmen)
            {
                Auswerten(platz, f);
                f.ErsterAbschnitt = false;
                f.Beob.Beginnen(_nr, false);
            }
        }
        f.Lage.Rahmen(laeuft, andererAnfang, eigenerBeginn, aktiv);
        f.Konto.Takt(_nr);
        Dauersenden(platz, f, aktiv);
        Angleichen(f, aktiv, beendet);
        Daempfen(f, aktiv, begonnen || beendet);
    }

    /// <summary>
    /// Der Bezug für den Gleichlauf einer beginnenden Phase: der Funker, der 50 bis 400 ms vorher begonnen hat (bei mehreren der lauteste), sonst die
    /// Summe der anderen. Bei vielen Sprechern zugleich verwischt die Summe die Hüllkurve dessen, den er echot (Prüfstand, 1. Abnahme: bei 25 Sprechern
    /// kein Urteil).
    /// </summary>
    private int BezugWaehlen(int platz)
    {
        var bester = -1;
        var besterDb = float.NegativeInfinity;
        for (var q = 0; q < _f.Length; q++)
        {
            if (q == platz) continue;
            var d = _nr - _f[q].LetzterAnfang;
            if (d is < Waechterformel.AnlaufVonRahmen or > Waechterformel.AnlaufBisRahmen) continue;
            var db = _f[q].Selbst.Hoechster(Waechterformel.AnlaufVonRahmen);
            if (db <= besterDb) continue;
            besterDb = db;
            bester = q;
        }
        return bester;
    }

    private bool AnlaufPruefen(Funker f)
    {
        var ja = false;
        for (var i = 0; i < f.Anfaenge.Length; i++)
        {
            var d = _nr - f.Anfaenge[i];
            if (d is < Waechterformel.AnlaufVonRahmen or > Waechterformel.AnlaufBisRahmen) continue;
            ja = true;
        }
        return ja;
    }

    private void Auswerten(int platz, Funker f)
    {
        var b = f.Beob.Ergebnis();
        var pO = Waechterformel.Ueberlappung(b.Ueberlappung, f.Lage.Anteil, _k);
        var pA = f.ErsterAbschnitt ? Waechterformel.Anlauf(b.Anlauf, f.Lage.AnfaengeJeS, _k) : 0f;
        var pG = b.Ueberlappung ? _k.Gleichlauf(b.Gleichlauf) : 0f;
        f.Konto.Buchen(pO + pA + pG, _nr);
        Beobachtet?.Invoke(platz, b, pO + pA + pG);
    }

    /// <summary>Dauersenden ohne Echo: 60 s fast durchgehend aktiv und weniger als ein Fünftel Sprache (geschätzt, sobald er Kandidat ist).</summary>
    private void Dauersenden(int platz, Funker f, bool aktiv)
    {
        var anteil = f.Lage.EigenAktivAnteil;
        if (f.Schaetzer < 0 && !f.Durchsage && f.Lage.Gefuellt >= KandidatAbRahmen && anteil >= KandidatAnteil) SchaetzerBinden(platz, f);
        if (f.Schaetzer >= 0 && aktiv && !float.IsNaN(f.P))
        {
            f.KandRahmen++;
            if (f.P >= Pegelstufe.SprachSchwelle) f.KandSprache++;
        }
        if (!f.Dauer && !f.Durchsage && f.Lage.Gefuellt >= Funklage.Fenster && anteil >= DauerAktivAnteil && f.KandRahmen >= SpracheMindestRahmen
            && f.KandSprache < EigenerWaechter.KaumSpracheAnteil * f.KandRahmen)
        {
            f.Dauer = true;
        }
        if (anteil < 0.5f)
        {
            f.Dauer = false;
            if (f.Schaetzer >= 0) SchaetzerLoesen(platz);
        }
    }

    private void SchaetzerBinden(int platz, Funker f)
    {
        for (var i = 0; i < _schaetzer.Length; i++)
        {
            if (_schaetzerPlatz[i] >= 0) continue;
            _schaetzerPlatz[i] = platz;
            _schaetzer[i].Zuruecksetzen();
            f.Schaetzer = i;
            f.KandRahmen = f.KandSprache = 0;
            return;
        }
    }

    private void SchaetzerLoesen(int platz)
    {
        var f = _f[platz];
        if (f.Schaetzer >= 0 && f.Schaetzer < _schaetzerPlatz.Length && _schaetzerPlatz[f.Schaetzer] == platz) _schaetzerPlatz[f.Schaetzer] = -1;
        f.Schaetzer = -1;
    }

    /// <summary>
    /// Q29: nach jeder Äußerung (mittlere Leistung der Rahmen über -60 dBFS) der Grundpegel des Funkers, langsam (30 % je Äußerung, Ausreißer erst als
    /// dritter in Folge); in der Pause danach höchstens 1 dB Richtung Ziel, nie mehr als 10 dB, nur abwärts, innerhalb von 3 dB am Ziel nichts.
    /// </summary>
    private void Angleichen(Funker f, bool aktiv, bool beendet)
    {
        if (aktiv && f.Db > -60f)
        {
            f.UttLeistung += Math.Pow(10.0, f.Db / 10.0);
            f.UttRahmen++;
        }
        if (beendet)
        {
            if (f.UttRahmen >= AeusserungMindestRahmen)
            {
                var u = (float)(10.0 * Math.Log10(f.UttLeistung / f.UttRahmen));
                if (!float.IsFinite(f.Lang))
                {
                    f.Lang = u;
                }
                else if (MathF.Abs(u - f.Lang) > AusreisserDb)
                {
                    if (++f.Ausreisser >= 3)
                    {
                        f.Lang = u;
                        f.Ausreisser = 0;
                    }
                }
                else
                {
                    f.Lang += 0.3f * (u - f.Lang);
                    f.Ausreisser = 0;
                }
                f.Faellig = true;
            }
            f.UttLeistung = 0;
            f.UttRahmen = 0;
        }
        if (!AngleichungAn)
        {
            // Gegenprüfung C2a, N1: gerampt zurück (0,05 dB je Block wie der Mischer aufwärts), nicht in einem Block bis 10 dB lauter.
            f.Angleichung = MathF.Min(0f, f.Angleichung + 0.05f);
            f.Faellig = false;
            return;
        }
        if (!f.Faellig || aktiv) return;
        f.Faellig = false;
        // Nur abwärts (Koordination 04.10.2026, Sarevocs Wunsch "sehr laute Sprecher regeln sich zurück"): Einen leisen Sender anzuheben machte über
        // Lautsprecher mehr Echo und kostete eigene Anlaute (Prüfstand, Gefecht 25: 276 statt 186 angeschnitten).
        var ziel = AngleichungZielDbfs - f.Lang;
        ziel = ziel >= -AngleichungTotzoneDb ? 0f : MathF.Max(ziel, -AngleichungHoechstDb);
        f.Angleichung += Math.Clamp(ziel - f.Angleichung, -AngleichungSchrittDb, AngleichungSchrittDb);
    }

    /// <summary>Dämpfung je Funker: bei Urteil schrittweise bis zur Stufe (an Phasengrenzen, in Pausen, sonst spätestens alle 3 s), danach langsam zurück.</summary>
    private void Daempfen(Funker f, bool aktiv, bool grenze)
    {
        var urteil = f.Durchsage ? DauersendUrteil.Keins : f.Konto.Erkannt ? DauersendUrteil.Echo : f.Dauer ? DauersendUrteil.Dauersenden : DauersendUrteil.Keins;
        f.Urteil = (int)urteil;
        var e = Einstellung;
        if (!e.An || f.NichtDaempfen || f.Durchsage)
        {
            f.Ziel = 0f;
            return;
        }
        var stufe = e.StufeDb;
        if (f.Ziel < stufe) f.Ziel = stufe;
        var seit = _nr - f.LetzterSchritt;
        if (urteil != DauersendUrteil.Keins)
        {
            if (f.Ziel > stufe + 0.01f && seit >= SchrittAbstand && (!aktiv || grenze || seit >= SchrittSpaetestens))
            {
                f.Ziel = MathF.Max(stufe, f.Ziel - SchrittDb);
                f.LetzterSchritt = _nr;
                Volatile.Write(ref f.Gedaempft, ++_folge);
            }
        }
        else if (f.Ziel < 0f && seit >= FreigabeAbstand && !aktiv)
        {
            f.Ziel = MathF.Min(0f, f.Ziel + FreigabeSchrittDb);
            f.LetzterSchritt = _nr;
        }
    }

    /// <summary>Nur ohne Audio-Thread: die Schätzer freigeben (RNNoise).</summary>
    public void SchaetzerFreigeben()
    {
        foreach (var s in _schaetzer) (s as IDisposable)?.Dispose();
    }
}
