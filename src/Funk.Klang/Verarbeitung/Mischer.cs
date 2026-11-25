// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

public sealed class MischerEinstellungen
{
    /// <summary>1 = Mono (Panorama wirkungslos), 2 = Stereo. Weitere Gerätekanäle werden mit 0 gefüllt.</summary>
    public int Kanaele { get; init; } = 2;

    public int MaxQuellen { get; init; } = 64;

    public int MaxNetze { get; init; } = 64;

    /// <summary>Absenkung der NETZ-Ströme während einer Durchsage (C9: Standard -12 dB, 0 bis -30 dB).</summary>
    public float AbsenkungDb { get; init; } = -12f;

    /// <summary>Zeit, in der die Absenkung voll greift (C9: 50 ms).</summary>
    public float AbsenkungAnstiegMs { get; init; } = 50f;

    /// <summary>So lange nach dem Ende der Durchsage bleibt abgesenkt (C9: 300 ms).</summary>
    public float AbsenkungHalteMs { get; init; } = 300f;

    /// <summary>Zeit, in der die Absenkung danach wieder aufgehoben wird.</summary>
    public float AbsenkungFreigabeMs { get; init; } = 250f;

    /// <summary>Untergrenze für Funker- plus Netzpegel bei Durchsagen (C7).</summary>
    public float DurchsageMindestpegelDb { get; init; } = -20f;

    public BegrenzerEinstellungen? Begrenzer { get; init; }

    /// <summary>
    /// Sprachwahrscheinlichkeit für Funker, die lange fast durchgehend senden (Klangstufe C2a, Urteil Dauersenden auf der Empfängerseite); der
    /// Mischer legt zwei an. Null: kein Urteil Dauersenden. Die Klangmaschine gibt RNNoise.
    /// </summary>
    public Func<ISprachwahrscheinlichkeit>? Sprachschaetzer { get; init; }

    /// <summary>Dieselben Einstellungen mit einem Sprachschätzer.</summary>
    public MischerEinstellungen MitSprachschaetzer(Func<ISprachwahrscheinlichkeit> schaetzer) => new()
    {
        Kanaele = Kanaele,
        MaxQuellen = MaxQuellen,
        MaxNetze = MaxNetze,
        AbsenkungDb = AbsenkungDb,
        AbsenkungAnstiegMs = AbsenkungAnstiegMs,
        AbsenkungHalteMs = AbsenkungHalteMs,
        AbsenkungFreigabeMs = AbsenkungFreigabeMs,
        DurchsageMindestpegelDb = DurchsageMindestpegelDb,
        Begrenzer = Begrenzer,
        Sprachschaetzer = schaetzer,
    };
}

/// <summary>
/// Stand eines Mischer-Platzes aus Sicht der Auto Dauersend-Dämpfung (Klangstufe C2a): Urteil des Wächters der Empfängerseite, wirkende Dämpfung in
/// dB (minus unendlich: stumm) und in Wörtern, Angleichung (Q29), Echo-Dämpfung der eigenen Seite, Wahl am gelben Zeichen und eine laufende Nummer
/// des letzten Dämpfungsschritts (für die Taste "Dauersender stumm", Q8).
/// </summary>
public readonly record struct FunkerZustand(DauersendUrteil Urteil, float DaempfungDb, string Wort, float AngleichungDb, float EchoDaempfungDb, bool Stumm,
    bool NichtDaempfen, long GedaempftFolge);

/// <summary>
/// Mischer für genau ein Ausgabegerät, float32 mit 48 kHz. Je Gerät eine eigene Instanz; jede Quelle
/// (Jitterpuffer eines Sprechers) gehört genau einem Mischer.
/// <para>
/// Kette je 10-ms-Block: Quelle lesen, Pegel je Funker (dB) plus Netzlautstärke (dB) plus Absenkung,
/// Panorama nach Constant-Power (Mitte je Kanal -3 dB), Verstärkungsänderungen linear über den Block
/// gerampt; dazu der Funk-Klick; danach <see cref="Begrenzer"/> (Stereo gekoppelt) und
/// <see cref="WeichesClipping"/> je Kanal. Hart geclippt wird nie; die Ausgabe liegt immer in [-1, 1].
/// </para>
/// <para>
/// Threads: Die Setz-Methoden, <see cref="QuelleHinzufuegen"/> und <see cref="QuelleEntfernen"/> dürfen aus
/// jedem Thread kommen, auch gleichzeitig (Plätze werden per Compare-and-Swap reserviert, sonst einzelne
/// 32-Bit-Werte, volatile). <see cref="Mischen"/> gehört dem Audio-Thread und allokiert nicht. Eine gerade
/// entfernte Quelle kann noch bis zum Ende des laufenden Blocks gelesen werden; freigeben (etwa ihren
/// Dekodierer) darf der Aufrufer sie erst, wenn <see cref="QuelleFreigebbar"/> mit der Marke aus
/// <see cref="QuelleEntfernen"/> wahr ist oder der Audio-Thread sicher steht.
/// </para>
/// <para>
/// Dezibel-Werte aus Einstellungen werden bereinigt: NaN zählt als 0 dB, mehr als
/// <see cref="MaxVerstaerkungDb"/> wird begrenzt, minus unendlich bleibt Stummschaltung. Ein NaN im Pegel
/// oder im Panorama würde sonst die Summe und damit alle Quellen des Geräts stummschalten.
/// </para>
/// </summary>
public sealed class Mischer : IEchoWiedergabe
{
    public const int Blocklaenge = Klangformat.Samples10ms;

    /// <summary>Höchste Verstärkung je Einstellung (Funker, Netz); deckt die Voreinstellungen -60 bis +12 dB mit Reserve.</summary>
    public const float MaxVerstaerkungDb = 24f;

    private const float Wurzel2Halbe = 0.70710677f;

    private sealed class Platz
    {
        public int Belegt; // 0 frei, 1 reserviert oder belegt; nur per Interlocked
        public volatile IKlangquelle? Quelle;
        public volatile float FunkerPegelDb;
        public volatile int Netz;
        public volatile bool Durchsage;
        public volatile int Generation; // steigt bei jeder Belegung, vor dem Veröffentlichen der Quelle

        // Klangstufe C2a (aus anderen Threads gesetzt):
        public volatile bool Funk = true; // falsch für die Hörprobe: kein Wächter, nicht in der Funkspur
        public volatile int Vorgabe; // Wahl am gelben Zeichen: VorgabeStumm, VorgabeNichtDaempfen
        public volatile int Zuruecksetzen; // Zähler der Wahl "wieder normal"
        public volatile float EchoZielDb; // eigene Seite: durchkommende Stimme bei Echo, 0 bis -6 dB

        // Nur Audio-Thread:
        public bool Initialisiert;
        public int GenerationGesehen;
        public float GainLinks;
        public float GainRechts;
        public int ZuruecksetzenGesehen;
        public float DaempfungAktuell;
        public float EchoAktuell;

        // Anzeige (schreibt der Audio-Thread):
        public volatile float AnzeigeDaempfung;
        public volatile float AnzeigeAngleichung;
        public volatile float AnzeigeEcho;
    }

    private const int VorgabeStumm = 1;
    private const int VorgabeNichtDaempfen = 2;

    /// <summary>Rampe einer Dämpfung je 10-ms-Block: nach unten 10 dB/s, zurück 5 dB/s (wie die Durchsage-Absenkung kein Sprung, kein Pumpen).</summary>
    private const float RampeAbDb = 0.1f;

    private const float RampeAufDb = 0.05f;

    private sealed class DaempfungFach(DaempfungEinstellung wert)
    {
        public DaempfungEinstellung Wert { get; } = wert;
    }

    private readonly MischerEinstellungen _e;
    private readonly Platz[] _plaetze;
    private readonly float[] _netzDb;
    private readonly float[] _netzPanorama;
    private readonly float[] _mono = new float[Blocklaenge];
    private readonly float[] _links = new float[Blocklaenge];
    private readonly float[] _rechts = new float[Blocklaenge];
    private readonly Begrenzer _begrenzer;
    private readonly bool _stereo;
    private volatile FunkKlick? _klick;
    private readonly Signalton _ton = new();
    private volatile float _absenkungDb;
    private float _absenkungAktuellDb;
    private int _halteRest;
    private bool _durchsageLetzterBlock;
    private float _clipLinks;
    private float _clipRechts;
    private int _blockLese = Blocklaenge;
    private long _bloecke;
    private float _spitzeVorBegrenzer;
    private readonly EmpfangsWaechter _waechter;
    private volatile DaempfungFach _daempfung = new(DaempfungEinstellung.AbWerk);
    private volatile float _funkGesamtZiel;
    private float _funkGesamtAktuell;
    private volatile float _funkLautstaerkeZiel = 1f;
    private float _funkLautstaerkeAktuell = 1f;
    private bool _funkLautstaerkeBegonnen;

    /// <summary>
    /// So weit ändert sich die <see cref="FunkLautstaerke"/> je 10-ms-Block höchstens (Faktor): von ganz laut auf stumm in 200 ms, ohne Knacken,
    /// und ein Zug am Regler (Schritte zu 5 %) kommt in ein bis zwei Blöcken an.
    /// </summary>
    public const float FunkLautstaerkeSchritt = 0.05f;

    public Mischer(MischerEinstellungen? einstellungen = null)
    {
        _e = einstellungen ?? new MischerEinstellungen();
        if (_e.Kanaele is < 1 or > 2)
            throw new ArgumentException("Kanäle 1 oder 2; mehr Gerätekanäle beim Mischen angeben.", nameof(einstellungen));
        ArgumentOutOfRangeException.ThrowIfLessThan(_e.MaxQuellen, 1, nameof(einstellungen));
        ArgumentOutOfRangeException.ThrowIfLessThan(_e.MaxNetze, 1, nameof(einstellungen));
        if (!(_e.AbsenkungAnstiegMs >= 0) || !(_e.AbsenkungHalteMs >= 0) || !(_e.AbsenkungFreigabeMs >= 0)
            || !float.IsFinite(_e.AbsenkungAnstiegMs) || !float.IsFinite(_e.AbsenkungHalteMs) || !float.IsFinite(_e.AbsenkungFreigabeMs))
            throw new ArgumentException("Absenkungszeiten müssen endlich und nicht negativ sein.", nameof(einstellungen));
        if (!float.IsFinite(_e.DurchsageMindestpegelDb) || float.IsNaN(_e.AbsenkungDb))
            throw new ArgumentException("Durchsage-Mindestpegel und Absenkung müssen Zahlen sein.", nameof(einstellungen));
        _stereo = _e.Kanaele == 2;
        _plaetze = new Platz[_e.MaxQuellen];
        for (var i = 0; i < _plaetze.Length; i++) _plaetze[i] = new Platz();
        _netzDb = new float[_e.MaxNetze];
        _netzPanorama = new float[_e.MaxNetze];
        _begrenzer = new Begrenzer(_e.Begrenzer);
        _klick = new FunkKlick();
        AbsenkungDb = _e.AbsenkungDb;
        _waechter = new EmpfangsWaechter(_e.MaxQuellen, _e.Sprachschaetzer);
    }

    public int Kanaele => _e.Kanaele;

    /// <summary>Absenkung anderer Netze während einer Durchsage, 0 bis -30 dB. NaN wird übergangen.</summary>
    public float AbsenkungDb
    {
        get => _absenkungDb;
        set
        {
            if (!float.IsNaN(value)) _absenkungDb = Math.Clamp(value, -30f, 0f);
        }
    }

    /// <summary>Aktuell wirksame Absenkung in dB (0 = keine).</summary>
    public float AbsenkungAktuellDb => _absenkungAktuellDb;

    /// <summary>
    /// Funk-Klick dieses Geräts; null schaltet ihn ab. Austausch aus jedem Thread. Eine Instanz gehört genau
    /// einem Mischer, weil <see cref="FunkKlick.Mischen"/> nur aus einem Audio-Thread laufen darf.
    /// </summary>
    public FunkKlick? Klick
    {
        get => _klick;
        set => _klick = value;
    }

    /// <summary>
    /// Signalton des Meldesystems (seit 0.12.0): ein Befehl oder eine der letzten zehn Sekunden eines Countdowns, angefordert aus jedem
    /// Thread, gemischt wie der Klick nach dem Funk und vor der Funk-Lautstärke.
    /// </summary>
    public Signalton Ton => _ton;

    /// <summary>Spitze der Summe vor dem Begrenzer im letzten Block (Anzeige, Test).</summary>
    public float SpitzeVorBegrenzer => _spitzeVorBegrenzer;

    /// <summary>Zahl der fertig berechneten 10-ms-Blöcke.</summary>
    public long Bloecke => Interlocked.Read(ref _bloecke);

    /// <summary>
    /// Gemeinsame Untergrenze der automatischen Absenkungen (Durchsage, Dauersend-Dämpfung, Echo-Dämpfung, Angleichung nach unten): Zusammen gehen
    /// sie nie tiefer als die tiefste Durchsage-Absenkung. Was der Funker selbst stellt (Pegel je Funker, stumm), gilt darüber hinaus.
    /// </summary>
    public const float GemeinsameUntergrenzeDb = -30f;

    /// <summary>Schalter "Auto Dauersend-Dämpfung" und Stufe (Klangstufe C2a); aus jedem Thread, gilt ab dem nächsten Block.</summary>
    public DaempfungEinstellung Daempfung
    {
        get => _daempfung.Wert;
        set => _daempfung = new DaempfungFach(value);
    }

    /// <summary>
    /// Die ganze Funk-Wiedergabe dieses Mischers (eigene Seite bei Echo, zweite Stufe, 0 bis -6 dB; nie Windows, nie das Spiel). Aus jedem Thread,
    /// gerampt; Klick und Hörprobe bleiben unberührt.
    /// </summary>
    public float FunkGesamtDb
    {
        get => _funkGesamtZiel;
        set => _funkGesamtZiel = float.IsFinite(value) ? Math.Clamp(value, Daempfungsworte.EtwasLeiserDb, 0f) : 0f;
    }

    /// <summary>
    /// Funk-Lautstärke (seit 0.10.0, Q74, E7): Faktor von 0 (stumm) bis 1 (ab Werk, unverändert) auf die ganze Summe vor dem Begrenzer, also auf
    /// Funk, Klick und Hörprobe gemeinsam; Windows und das Spiel bleiben, wie sie sind. Aus jedem Thread; gerampt um höchstens
    /// <see cref="FunkLautstaerkeSchritt"/> je Block, nur der allererste Block beginnt gleich beim Wert. Was keine Zahl ist, gilt als 1: Ein Fehler
    /// macht den Funk nie stumm. Die <see cref="Spur"/> rechnet den Faktor mit, sonst schätzte der Wächter das Echo zu laut.
    /// </summary>
    public float FunkLautstaerke
    {
        get => _funkLautstaerkeZiel;
        set => _funkLautstaerkeZiel = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 1f;
    }

    /// <summary>Für die eigene Seite (Wächter im Aufnahme-Thread): Was der Mischer an Funk spielt, je Block. Vor dem ersten Block setzen.</summary>
    internal FunkSpur? Spur { get; set; }

    /// <summary>Der Wächter der Empfängerseite (Tests, Einmessen).</summary>
    internal EmpfangsWaechter Waechter => _waechter;

    /// <summary>Stand eines Platzes aus Sicht der Auto Dauersend-Dämpfung (für die Oberfläche über die Klangmaschine, aus jedem Thread).</summary>
    public FunkerZustand Zustand(int platz)
    {
        var p = _plaetze[platz];
        var vorgabe = p.Vorgabe;
        var stumm = (vorgabe & VorgabeStumm) != 0;
        var d = stumm ? float.NegativeInfinity : p.AnzeigeDaempfung;
        return new FunkerZustand(_waechter.Urteil(platz), d, Daempfungsworte.Wort(d), p.AnzeigeAngleichung, p.AnzeigeEcho, stumm,
            (vorgabe & VorgabeNichtDaempfen) != 0, _waechter.GedaempftFolge(platz));
    }

    /// <summary>Wahl am gelben Zeichen eines Platzes: stumm, nicht mehr dämpfen; aus jedem Thread.</summary>
    public void SetzeWaechterVorgabe(int platz, bool stumm, bool nichtDaempfen) =>
        _plaetze[platz].Vorgabe = (stumm ? VorgabeStumm : 0) | (nichtDaempfen ? VorgabeNichtDaempfen : 0);

    /// <summary>"Wieder normal": Urteil und Dämpfung dieses Platzes vergessen (im nächsten Block); aus jedem Thread.</summary>
    public void WaechterZuruecksetzen(int platz) => Interlocked.Increment(ref _plaetze[platz].Zuruecksetzen);

    /// <summary>
    /// Zusammenführung je Funker (Klangstufe C2a, Entwurf "Ineinandergreifen" 4, Q29): Absenkungen (Pegel je Funker unter 0, Durchsage, Dauersend-
    /// Dämpfung, Echo-Dämpfung, Angleichung nach unten) werden nie addiert, die stärkste zählt; die automatischen zusammen nie unter
    /// <see cref="GemeinsameUntergrenzeDb"/>. Anhebungen (Pegel je Funker über 0, Angleichung eines leisen Senders nach oben) ebenso nicht addiert, die
    /// größte zählt; von ihr aus wird abgesenkt (ein angehobener leiser Funker sinkt bei einer Durchsage wie alle um 12 dB). Stumm bleibt stumm, NaN
    /// zählt als 0. Die Netzlautstärke (A9) kommt wie bisher dazu.
    /// </summary>
    public static float Zusammenfuehren(float manuellDb, float durchsageDb, float daempfungDb, float echoDb, float angleichungDb)
    {
        static float N(float x) => float.IsNaN(x) ? 0f : x;
        var m = N(manuellDb);
        if (float.IsNegativeInfinity(m)) return float.NegativeInfinity;
        var angleich = N(angleichungDb);
        var auto = MathF.Min(MathF.Min(0f, N(durchsageDb)), MathF.Min(MathF.Min(N(daempfungDb), N(echoDb)), MathF.Min(0f, angleich)));
        auto = MathF.Max(auto, GemeinsameUntergrenzeDb);
        var absenkung = MathF.Min(MathF.Min(0f, m), auto);
        var anhebung = MathF.Max(0f, MathF.Max(m, angleich));
        return anhebung + absenkung;
    }

    /// <summary>Nur ohne Audio-Thread: die Sprachschätzer der Empfängerseite freigeben (RNNoise).</summary>
    public void SchaetzerFreigeben() => _waechter.SchaetzerFreigeben();

    /// <summary>
    /// Quelle anmelden, aus jedem Thread. Rückgabe: Platznummer oder -1, wenn alle belegt sind. <paramref name="funk"/> falsch (Hörprobe): kein
    /// Wächter, keine Dämpfung, nicht in der Funkspur.
    /// </summary>
    public int QuelleHinzufuegen(IKlangquelle quelle, int netz, float funkerPegelDb = 0f, bool durchsage = false, bool funk = true)
    {
        ArgumentNullException.ThrowIfNull(quelle);
        PruefeNetz(netz);
        for (var i = 0; i < _plaetze.Length; i++)
        {
            var p = _plaetze[i];
            if (Volatile.Read(ref p.Belegt) != 0 || Interlocked.CompareExchange(ref p.Belegt, 1, 0) != 0) continue;
            p.FunkerPegelDb = DbBereinigen(funkerPegelDb);
            p.Netz = netz;
            p.Durchsage = durchsage;
            p.Funk = funk;
            p.Vorgabe = 0;
            p.EchoZielDb = 0f;
            p.Generation++; // nur der Reservierende schreibt hier
            p.Quelle = quelle; // zuletzt: erst jetzt sieht der Audio-Thread den Platz
            return i;
        }
        return -1;
    }

    /// <summary>
    /// Quelle abmelden, aus jedem Thread. Rückgabe: Marke für <see cref="QuelleFreigebbar"/>. Der Platz ist
    /// sofort wieder frei; die Quelle selbst kann der laufende Block noch lesen.
    /// </summary>
    public long QuelleEntfernen(int platz)
    {
        var p = _plaetze[platz];
        // Exchange ist ein voller Speicherzaun; zusammen mit Interlocked.Increment am Blockende im Audio-Thread
        // sieht jeder Block, der nach der Marke beginnt, den leeren Platz.
        if (Interlocked.Exchange(ref p.Quelle, null) is not null) Volatile.Write(ref p.Belegt, 0);
        return Interlocked.Read(ref _bloecke);
    }

    /// <summary>
    /// Wahr, sobald der Audio-Thread eine mit <paramref name="marke"/> entfernte Quelle nicht mehr liest,
    /// also mindestens ein Block nach dem Entfernen fertig wurde. Steht der Audio-Thread, bleibt es falsch;
    /// dann darf der Aufrufer die Quelle ohnehin freigeben.
    /// </summary>
    public bool QuelleFreigebbar(long marke) => Interlocked.Read(ref _bloecke) > marke;

    public void SetzeFunkerPegel(int platz, float db) => _plaetze[platz].FunkerPegelDb = DbBereinigen(db);

    public void SetzeNetz(int platz, int netz)
    {
        PruefeNetz(netz);
        _plaetze[platz].Netz = netz;
    }

    /// <summary>Art GLOBAL oder DURCHSAGE: senkt die übrigen Ströme ab und wird selbst nie abgesenkt.</summary>
    public void SetzeDurchsage(int platz, bool durchsage) => _plaetze[platz].Durchsage = durchsage;

    public void SetzeNetzLautstaerke(int netz, float db)
    {
        PruefeNetz(netz);
        Volatile.Write(ref _netzDb[netz], DbBereinigen(db));
    }

    /// <summary>Panorama -1 (links) bis +1 (rechts), 0 = beide. NaN gilt als Mitte.</summary>
    public void SetzeNetzPanorama(int netz, float panorama)
    {
        PruefeNetz(netz);
        Volatile.Write(ref _netzPanorama[netz], PanoramaBereinigen(panorama));
    }

    /// <summary>Constant-Power-Panorama: L = cos(theta), R = sin(theta), theta = (p + 1) * pi / 4.</summary>
    public static (float Links, float Rechts) PanoramaFaktoren(float panorama)
    {
        var theta = (PanoramaBereinigen(panorama) + 1f) * MathF.PI / 4f;
        return (MathF.Cos(theta), MathF.Sin(theta));
    }

    /// <summary>NaN wird 0 dB, nach oben begrenzt auf <see cref="MaxVerstaerkungDb"/>; minus unendlich bleibt stumm.</summary>
    private static float DbBereinigen(float db) => float.IsNaN(db) ? 0f : MathF.Min(db, MaxVerstaerkungDb);

    private static float PanoramaBereinigen(float p) => float.IsNaN(p) ? 0f : Math.Clamp(p, -1f, 1f);

    /// <summary>
    /// Audio-Thread: <paramref name="ausgabe"/> verschachtelt mit <paramref name="geraeteKanaele"/> Kanälen füllen.
    /// Kanal 0 und 1 bekommen links und rechts (Mono: nur Kanal 0), weitere Kanäle 0.
    /// </summary>
    public void Mischen(Span<float> ausgabe, int geraeteKanaele)
    {
        if (geraeteKanaele < _e.Kanaele || ausgabe.Length % geraeteKanaele != 0)
            throw new ArgumentException("Kanalzahl passt nicht zur Ausgabe.", nameof(geraeteKanaele));
        var frames = ausgabe.Length / geraeteKanaele;
        var ziel = 0;
        while (ziel < frames)
        {
            if (_blockLese == Blocklaenge)
            {
                BlockBerechnen();
                _blockLese = 0;
            }
            var n = Math.Min(Blocklaenge - _blockLese, frames - ziel);
            for (var i = 0; i < n; i++)
            {
                var basis = (ziel + i) * geraeteKanaele;
                ausgabe[basis] = _links[_blockLese + i];
                if (_stereo) ausgabe[basis + 1] = _rechts[_blockLese + i];
                for (var k = _e.Kanaele; k < geraeteKanaele; k++) ausgabe[basis + k] = 0f;
            }
            _blockLese += n;
            ziel += n;
        }
    }

    private void BlockBerechnen()
    {
        Array.Clear(_links);
        if (_stereo) Array.Clear(_rechts);

        // Absenkung aus dem Stand des vorigen Blocks nachführen (10 ms Verzug).
        var soll = _durchsageLetzterBlock || _halteRest > 0 ? _absenkungDb : 0f;
        if (_durchsageLetzterBlock) _halteRest = (int)(_e.AbsenkungHalteMs * Klangformat.SamplesJeMs);
        else if (_halteRest > 0) _halteRest = Math.Max(0, _halteRest - Blocklaenge);
        var blockMs = (float)Blocklaenge / Klangformat.SamplesJeMs;
        var tiefe = MathF.Max(1f, MathF.Abs(_absenkungDb));
        if (soll < _absenkungAktuellDb)
            _absenkungAktuellDb = MathF.Max(soll, _absenkungAktuellDb - tiefe * blockMs / MathF.Max(blockMs, _e.AbsenkungAnstiegMs));
        else if (soll > _absenkungAktuellDb)
            _absenkungAktuellDb = MathF.Min(soll, _absenkungAktuellDb + tiefe * blockMs / MathF.Max(blockMs, _e.AbsenkungFreigabeMs));

        var klick = _klick;
        var durchsage = false;
        var einstellung = _daempfung.Wert;
        _waechter.Einstellung = einstellung;
        _funkGesamtAktuell = Rampe(_funkGesamtAktuell, _funkGesamtZiel);
        // Funk-Lautstärke (E7): von wo der Block beginnt und wo er endet; der erste Block beginnt gleich beim Wert.
        var lautZiel = _funkLautstaerkeZiel;
        var lautAnfang = _funkLautstaerkeBegonnen ? _funkLautstaerkeAktuell : lautZiel;
        _funkLautstaerkeBegonnen = true;
        var lautEnde = lautZiel < lautAnfang
            ? MathF.Max(lautZiel, lautAnfang - FunkLautstaerkeSchritt)
            : MathF.Min(lautZiel, lautAnfang + FunkLautstaerkeSchritt);
        _funkLautstaerkeAktuell = lautEnde;
        var lautDb = lautEnde >= 1f ? 0f : lautEnde > 0f ? 20f * MathF.Log10(lautEnde) : float.NegativeInfinity;
        double funkLeistung = 0;
        var funkAnfang = false;
        var lautester = -1;
        double lautesteLeistung = 0;
        for (var i = 0; i < _plaetze.Length; i++)
        {
            var p = _plaetze[i];
            var quelle = p.Quelle;
            if (quelle is null)
            {
                p.Initialisiert = false;
                continue;
            }
            var ereignis = quelle.Lesen(_mono);
            var istDurchsage = p.Durchsage;
            if (istDurchsage && (ereignis & QuellenEreignis.Aktiv) != 0) durchsage = true;
            var neu = !p.Initialisiert || p.GenerationGesehen != p.Generation;
            var funk = p.Funk;

            var netz = p.Netz;
            float db;
            if (funk)
            {
                // Klangstufe C2a: Wächter der Empfängerseite, dann die Zusammenführung (stärkste Absenkung zählt, nichts addiert).
                var zurueck = p.Zuruecksetzen;
                var vorgabe = p.Vorgabe;
                if (neu)
                {
                    p.DaempfungAktuell = 0f;
                    p.EchoAktuell = 0f;
                    p.ZuruecksetzenGesehen = zurueck;
                }
                // "Wieder normal" ist eine Wahl des Funkers: sofort (die Verstärkung rampt wie immer über den Block), nicht langsam wie die Freigabe.
                if (zurueck != p.ZuruecksetzenGesehen) p.DaempfungAktuell = 0f;
                _waechter.Quelle(i, neu, zurueck != p.ZuruecksetzenGesehen, (vorgabe & VorgabeNichtDaempfen) != 0, _mono, ereignis, istDurchsage);
                p.ZuruecksetzenGesehen = zurueck;
                p.DaempfungAktuell = Rampe(p.DaempfungAktuell, _waechter.ZielDb(i));
                p.EchoAktuell = Rampe(p.EchoAktuell, einstellung.An ? p.EchoZielDb : 0f);
                var angleich = _waechter.AngleichungDb(i);
                p.AnzeigeDaempfung = p.DaempfungAktuell;
                p.AnzeigeAngleichung = angleich;
                p.AnzeigeEcho = p.EchoAktuell;
                var stumm = (vorgabe & VorgabeStumm) != 0;
                if (istDurchsage)
                {
                    // Gegenprüfung C2a, M3: Die Gesamtabsenkung des Wächters ist eine Absenkung wie die anderen (stärkste zählt, nicht addiert).
                    db = MathF.Max(Zusammenfuehren(p.FunkerPegelDb, 0f, 0f, MathF.Min(p.EchoAktuell, _funkGesamtAktuell), angleich)
                        + Volatile.Read(ref _netzDb[netz]), _e.DurchsageMindestpegelDb);
                }
                else
                {
                    db = Zusammenfuehren(p.FunkerPegelDb, _absenkungAktuellDb, p.DaempfungAktuell, MathF.Min(p.EchoAktuell, _funkGesamtAktuell), angleich)
                        + Volatile.Read(ref _netzDb[netz]);
                }
                if (stumm) db = float.NegativeInfinity;
            }
            else
            {
                db = p.FunkerPegelDb + Volatile.Read(ref _netzDb[netz]);
                if (istDurchsage) db = MathF.Max(db, _e.DurchsageMindestpegelDb);
                else db += _absenkungAktuellDb;
            }
            var gain = Pegel.DbZuLinear(db);
            if (funk && float.IsFinite(db))
            {
                double l = Pegel.Leistung(_mono) * gain * gain;
                if (float.IsFinite((float)l))
                {
                    funkLeistung += l;
                    if (l > lautesteLeistung)
                    {
                        lautesteLeistung = l;
                        lautester = i;
                    }
                }
                if ((ereignis & QuellenEreignis.Begonnen) != 0 && db + lautDb > -40f) funkAnfang = true;
            }
            float zielL, zielR;
            float panL = Wurzel2Halbe, panR = Wurzel2Halbe; // Mono: Klick in voller Höhe
            if (_stereo)
            {
                (panL, panR) = PanoramaFaktoren(Volatile.Read(ref _netzPanorama[netz]));
                zielL = gain * panL;
                zielR = gain * panR;
            }
            else
            {
                zielL = gain;
                zielR = 0f;
            }
            var generation = p.Generation;
            if (!p.Initialisiert || p.GenerationGesehen != generation)
            {
                // Neue Quelle auf dem Platz (auch wenn der Platz zwischen zwei Blöcken frei und wieder belegt
                // wurde): mit dem eigenen Pegel beginnen, nicht mit der Rampe des Vorgängers.
                p.GainLinks = zielL;
                p.GainRechts = zielR;
                p.Initialisiert = true;
                p.GenerationGesehen = generation;
            }

            var gl = p.GainLinks;
            var gr = p.GainRechts;
            var schrittL = (zielL - gl) / Blocklaenge;
            var schrittR = (zielR - gr) / Blocklaenge;
            for (var k = 0; k < Blocklaenge; k++)
            {
                gl += schrittL;
                gr += schrittR;
                var v = _mono[k];
                _links[k] += v * gl;
                if (_stereo) _rechts[k] += v * gr;
            }
            p.GainLinks = zielL;
            p.GainRechts = zielR;

            if (klick is not null)
            {
                var va = (ereignis & QuellenEreignis.Sprachaktivierung) != 0;
                if ((ereignis & QuellenEreignis.KlickDruck) != 0) klick.Ausloesen(KlickArt.Druck, va, panL, panR);
                if ((ereignis & QuellenEreignis.KlickLoslassen) != 0) klick.Ausloesen(KlickArt.Loslassen, va, panL, panR);
            }
        }
        _durchsageLetzterBlock = durchsage;
        _waechter.BlockEnde();
        // Die Spur meldet, was wirklich spielt: mit der Funk-Lautstärke (bei 1 bitgleich wie ohne).
        funkLeistung *= (double)lautEnde * lautEnde;
        Spur?.Schreiben(funkLeistung > 0 ? (float)(10.0 * Math.Log10(funkLeistung)) : Pegel.MinDbfs, funkAnfang, lautester);

        klick?.Mischen(_links, _stereo ? _rechts : Span<float>.Empty);
        _ton.Mischen(_links, _stereo ? _rechts : Span<float>.Empty);
        // Funk-Lautstärke auf die ganze Summe (Funk, Klick, Signalton, Hörprobe), je Sample gerampt; bei 1 wird nichts angefasst.
        if (lautAnfang != 1f || lautEnde != 1f)
        {
            var schritt = (lautEnde - lautAnfang) / Blocklaenge;
            var g = lautAnfang;
            for (var k = 0; k < Blocklaenge; k++)
            {
                g += schritt;
                _links[k] *= g;
                if (_stereo) _rechts[k] *= g;
            }
        }

        _spitzeVorBegrenzer = _stereo ? MathF.Max(Pegel.Spitze(_links), Pegel.Spitze(_rechts)) : Pegel.Spitze(_links);
        _begrenzer.Verarbeiten(_links, _stereo ? _rechts : Span<float>.Empty);
        WeichesClipping.Anwenden(_links, ref _clipLinks);
        if (_stereo) WeichesClipping.Anwenden(_rechts, ref _clipRechts);
        Interlocked.Increment(ref _bloecke); // voller Zaun, Gegenstück zu QuelleEntfernen
    }

    /// <summary>Eine Dämpfung je Block zum Ziel führen: nach unten höchstens 0,1 dB, nach oben höchstens 0,05 dB.</summary>
    private static float Rampe(float aktuell, float ziel)
    {
        if (!float.IsFinite(ziel)) ziel = 0f;
        return ziel < aktuell ? MathF.Max(ziel, aktuell - RampeAbDb) : MathF.Min(ziel, aktuell + RampeAufDb);
    }

    float IEchoWiedergabe.GesamtDb
    {
        get => FunkGesamtDb;
        set => FunkGesamtDb = value;
    }

    float IEchoWiedergabe.StimmeDb(int platz) => platz >= 0 && platz < _plaetze.Length ? _plaetze[platz].EchoZielDb : 0f;

    void IEchoWiedergabe.StimmeSetzen(int platz, float db)
    {
        if (platz < 0 || platz >= _plaetze.Length || !float.IsFinite(db)) return;
        _plaetze[platz].EchoZielDb = Math.Clamp(db, Daempfungsworte.EtwasLeiserDb, 0f);
    }

    private void PruefeNetz(int netz) => ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)netz, (uint)_netzDb.Length, nameof(netz));
}
