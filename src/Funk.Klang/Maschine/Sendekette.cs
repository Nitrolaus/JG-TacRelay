// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>
/// Die Aufnahmekette je 10-ms-Rahmen im Aufnahme-Thread: Echoreferenz (mit eigener RNNoise-Sprachschätzung für den
/// <see cref="Maschine.Echoschutz"/>) und <see cref="EchoProzessor"/> (AEC3, Hochpass; bis 0.6.0 Stufe 2c auch AGC2),
/// seit Klangstufe A <see cref="Verarbeitung.Pegelstufe"/> und Sicherheitsbegrenzer, <see cref="RnnoiseProzessor"/> (Rauschen und
/// Sprachwahrscheinlichkeit), dann <see cref="GateFuehrung"/> mit <see cref="SprachGate"/> und <see cref="Sendeaufbereitung"/> mit
/// Opus. Die fertigen Pakete gehen in den Sende-Ring (abgeholt vom Melde-Thread) und, bei eingeschaltetem Mithören
/// (<see cref="HoerprobeAn"/>), in den eigenen Jitterpuffer, der wie ein Empfänger dekodiert und abspielt. Jeder Rahmen landet
/// mit Pegel, Boden, Linie und Wahrscheinlichkeit im <see cref="Rahmenjournal"/> und mit den Werten und Laufzeiten jeder Stufe im
/// <see cref="Telemetriejournal"/>.
/// <para>
/// Reihenfolge (Klangstufe A, Recherche Abschnitt 1): Die Pegelstufe sitzt dort, wo bis 0.6.0 Stufe 2c die AGC2 im APM saß,
/// hinter AEC3 und Hochpass, vor RNNoise und dem Gate. Ihre Pegelschätzung und die Sprachwahrscheinlichkeit sehen also kein vom
/// Gate geschnittenes Signal. Hinter RNNoise wäre die Recherche-Reihenfolge gewesen; gemessen (<c>MessungKlangA.Messung_Reihenfolge</c>,
/// Klangkorpus, gleicher Pegel am Gate) schnitt das Gate dort mehr Wortanfänge ab, je leiser das Mikrofon: 9 statt 5 von 190 bei
/// 15 dB, 20 statt 5 bei 25 dB Verstärkung. RNNoise erkennt schwache Anlaute schlechter, wenn die Stimme leise ankommt. Die
/// Pegelstufe nimmt die Sprachwahrscheinlichkeit des vorigen Rahmens, weil RNNoise erst danach rechnet; der Begrenzer hält die
/// Spitzen unter 0 dBFS, bevor RNNoise auf [-1, 1] beschneidet. Das Gate sieht den Pegel hinter RNNoise, dieselbe Skala wie die
/// Pegelanzeige und die Schwellen.
/// </para>
/// <para>
/// Klangfarbe (Q70 bis Q72, 0.9.3, <see cref="Verarbeitung.Klangfarbe"/>): hinter dem Abgriff der Stimmprobe, vor Hörprobe und Gate, mit eigenem
/// Begrenzer. Ihren Pegel misst die Kette davor und gibt ihn dem Gate mit (<see cref="SprachGate.Verarbeiten"/>, <c>pegelDbfs</c>); Gate,
/// Führung, Mindest-Funklautstärke, Korridor, Pegelanzeige, Journal, Telemetrie und Wächter sehen so genau dieselben Werte wie ohne
/// Klangfarbe. Was hinausgeht, die Hörprobe und das Mithören haben sie. Davor wäre falsch: Ein angehobener Rauschfilter-Eingang änderte die
/// Sprachwahrscheinlichkeit und damit Gate, Lernen und Wächter; die Stimmproben bleiben ohne Klangfarbe.
/// </para>
/// <para>
/// Hörprobe als Aufnahme (<see cref="ProbeStarten"/>): Das Signal hinter APM und RNNoise, vor dem Gate, geht durch einen eigenen
/// Opus-Kodierer in einen Speicher für <see cref="IKlangMaschine.ProbeDauerMs"/>; danach speist die Kette die Pakete im 20-ms-Takt
/// in denselben Jitterpuffer. Aufnahme und Wiedergabe laufen nie zugleich, das Mithören ruht so lange. Ab dem Start bis
/// <see cref="ProbeNachlaufRahmen"/> nach dem Ende verwirft die Kette jede neue Phase der Sprachaktivierung ganz (BEGINN bis
/// ENDE), damit weder die Probe noch das Echo ihrer Wiedergabe hinausgeht; eine schon laufende Phase und die Sprechtaste
/// bleiben unberührt. Das Gate selbst rechnet weiter wie immer, seine Statistik sieht also keine verfälschten Rahmen.
/// </para>
/// <para>
/// Nachlauf der Sprechtaste (Q66, 0.9.3, <see cref="TastenNachlaufMs"/>): Nach dem Loslassen sendet die Kette noch so lange wie mit gedrückter
/// Taste, denn APM, Rauschfilter und Opus verzögern die Stimme; ohne Nachlauf fehlte das Ende der letzten Silbe (gemessen in
/// <c>MessungNachlauf</c>, Ergebnis im Modulbericht Klang). Ein neuer Druck derselben Taste im Nachlauf setzt dieselbe Phase fort, einer der anderen
/// Taste (anderes Ziel) beendet sie und beginnt eine neue, "Mikro stumm" beendet ihn sofort. Erst an seinem Ende meldet die Kette
/// <see cref="TasteBeendet"/>, und die Maschine klickt.
/// </para>
/// <para>
/// Ohne Allokation und ohne Sperre. Einstellungen aus anderen Threads kommen als unveränderliches Fach
/// (<see cref="GateSetzen"/>) oder als einzelne volatile Werte.
/// </para>
/// <para>
/// RNNoise verzögert sein Ausgangssignal um 20 ms, die Wahrscheinlichkeit gilt aber für den gerade gelesenen Rahmen.
/// Geht das entrauschte Signal ins Gate, wird die Wahrscheinlichkeit deshalb um zwei Rahmen verzögert.
/// </para>
/// <para>
/// Rauschfilter (Klangstufe B): genau einer wirkt, Standard (RNNoise), Stark (DeepFilterNet3) oder Aus; die
/// <see cref="Rauschfilterwahl"/> legt die Wege zeitrichtig übereinander und blendet um. RNNoise rechnet immer (warmer leichter Weg
/// und Sprachwahrscheinlichkeit), DeepFilterNet3 nur, solange es gebraucht wird. Der <see cref="DeepFilterProzessor"/> wird
/// außerhalb des Audio-Threads angelegt und mit <see cref="StarkUebergeben"/> hereingereicht; einen nicht mehr gebrauchten legt die
/// Kette zum Freigeben ab (<see cref="AusgedientenHolen"/>). Die Wahrscheinlichkeit fürs Gate und die Wirkung des Filters in der
/// Telemetrie folgen dem Verzug der Kette (0, 2 oder 3 Rahmen). Rückfall auf Standard: wenn DeepFilterNet3 einen Fehler meldet, und
/// wenn die Kette mit DeepFilterNet3 nach dessen Anlauf in einer Sekunde <see cref="RueckfallFristen"/>-mal länger als 10 ms rechnete.
/// </para>
/// </summary>
internal sealed class Sendekette
{
    private const int RahmenWerte = Klangformat.Samples10ms;
    private const int HoechsterVerzug = DeepFilterProzessor.VerzoegerungSamples / RahmenWerte;

    /// <summary>So viele verpasste Fristen (Kette länger als 10 ms) mit DeepFilterNet3 binnen <see cref="RueckfallFensterRahmen"/> führen zum Rückfall.</summary>
    public const int RueckfallFristen = 3;

    /// <summary>Fenster für <see cref="RueckfallFristen"/>: 100 Rahmen, eine Sekunde.</summary>
    public const int RueckfallFensterRahmen = 100;

    /// <summary>Mehr Echoreferenz als so viele Rahmen wird verworfen, damit die Referenz nicht nachhängt.</summary>
    public const int ReferenzHoechstRahmen = 8;

    /// <summary>Nach dem Kürzen bleiben so viele Rahmen Reserve gegen das Zittern der beiden Geräte.</summary>
    public const int ReferenzReserveRahmen = 2;

    /// <summary>
    /// So viele Rahmen nach dem Ende einer Wiedergabe der Probe öffnet die Sprachaktivierung noch nicht: Jitterpuffer, Gerät,
    /// Laufzeit und Nachhall der letzten Silben (wie <see cref="Echoschutz.HalteRahmen"/>, dazu 100 ms).
    /// </summary>
    public const int ProbeNachlaufRahmen = Echoschutz.HalteRahmen + 10;

    /// <summary>Pakete zu 20 ms in einer Probe.</summary>
    public const int ProbePakete = IKlangMaschine.ProbeDauerMs / 20;

    private const int ProbeBefehlStarten = 1;
    private const int ProbeBefehlAbbrechen = 2;

    private sealed class GateFach(GateEinstellung wert)
    {
        public GateEinstellung Wert { get; } = wert;
    }

    private readonly EchoProzessor? _echo;
    private readonly RnnoiseProzessor? _rnnoise;
    private readonly RnnoiseProzessor? _referenzVad;
    private readonly Pegelstufe? _pegelstufe;
    private readonly Begrenzer? _begrenzer;
    private readonly Klangfarbe _klangfarbe = new();
    private readonly Telemetriejournal _telemetrie;
    private readonly float[] _eingangVerlauf = new float[HoechsterVerzug + 1];
    private int _eingangIndex;
    private readonly Rauschfilterwahl _wahl = new();
    private readonly float[] _stark = new float[RahmenWerte];
    private readonly float[] _gewaehlt = new float[RahmenWerte];
    private DeepFilterProzessor? _df;
    private DeepFilterProzessor? _dfNeu;
    private DeepFilterProzessor? _dfAlt;
    private volatile int _rauschfilterWunsch = (int)Rauschfilter.Standard;
    private volatile int _rauschfilterWirksam = (int)Rauschfilter.Standard;
    private volatile int _rueckfall;
    private long _rueckfallFensterAb;
    private int _starkRahmen;
    private int _rueckfallZaehler;
    private float _pVorher;
    private bool _sendetVorher;
    private readonly ISprachwahrscheinlichkeit? _schaetzer;
    private readonly GateFuehrung _fuehrung = new();
    private readonly Echoschutz _echoschutz = new();
    private readonly Sendeaufbereitung _sende;
    private readonly PaketRing _ausgang;
    private readonly Rahmenjournal _journal;
    private readonly RahmenRing? _referenz;
    private readonly Jitterpuffer? _hoerprobe;
    private readonly Func<long> _uhrUs;
    private readonly IKodierer? _probeKodierer;
    private readonly byte[][] _probeDaten = [];
    private readonly int[] _probeLaengen = [];
    private readonly float[] _probePaar = new float[Klangformat.Samples20ms];

    private readonly float[] _entrauscht = new float[RahmenWerte];
    private readonly float[] _referenzRahmen = new float[RahmenWerte];
    private readonly float[] _referenzEntrauscht = new float[RahmenWerte];
    private readonly float[] _pVerlauf = new float[HoechsterVerzug + 1];
    private int _pIndex;

    private GateFach _gateFach = new(GateVorgaben.Standard);
    private GateFach? _gateGesehen;
    private volatile bool _echoAn;
    private volatile bool _hoerprobeAn;

    // Sprechtaste mit Nachlauf (Q66, 0.9.3): Maschine und Kette teilen den Zustand _tastenZug (Ruhe, gedrückt, losgelassen). Drücken und Loslassen
    // setzen ihn aus jedem Thread, die Kette nimmt das Loslassen erst nach dem Nachlauf zurück auf Ruhe. Beides mit Interlocked: Entweder
    // nimmt ein neuer Druck den laufenden Nachlauf auf (die Phase läuft nahtlos weiter, oder sie endet für ein anderes Ziel mit _tastenSchnitt),
    // oder die Kette hat ihn schon beendet (dann neue Phase).
    private int _tastenZug;
    private volatile int _nachlaufMs;
    private volatile bool _imNachlauf;
    private int _nachlaufRest = -1;

    /// <summary>
    /// 1: Die Taste kam im Nachlauf wieder, aber für ein anderes Ziel (<see cref="TasteDruecken"/> ohne Fortsetzen). Der nächste Rahmen beendet die
    /// laufende Phase mit ENDE, der übernächste beginnt die neue. Setzt jeder Thread, abgeräumt im Aufnahme-Thread.
    /// </summary>
    private int _tastenSchnitt;

    /// <summary>Nur Aufnahme-Thread: Der Rahmen gehört zur Sprechtaste (gedrückt oder im Nachlauf); für die Pegelstufe.</summary>
    private bool _tasteImRahmen;

    private bool _hpOffen;
    private ushort _hpPhase;
    private uint _hpFrame;
    private long _rahmen;
    private long _pakete;
    private long _referenzFehlt;
    private long _referenzVerworfen;
    private long _referenzAusgeblendet;
    private long _klickAbRahmen = long.MinValue;
    private readonly long[] _referenzFuellstaende = new long[ReferenzHoechstRahmen + 2];
    private long _sendeVerworfen;
    private int _funkGespielt;

    // Hörprobe als Aufnahme. Befehle kommen aus jedem Thread (_probeBefehl), alles andere gehört dem Aufnahme-Thread; Zustand
    // und Zähler liest die Oberfläche (volatile).
    private int _probeBefehl;
    private volatile int _probeZustand;
    private volatile int _probeAufgenommen;
    private volatile int _probeGespielt;
    private bool _probeHalb;
    private bool _probeTakt;
    private bool _probeBegonnen;
    private int _probeNachlauf;
    private bool _vaVerworfen;
    private long _vaVerworfenePakete;

    /// <param name="kodierer">Opus-Kodierer (oder Attrappe); gehört danach der Kette.</param>
    /// <param name="echo">APM oder null (ohne Echo und Hochpass).</param>
    /// <param name="rnnoise">RNNoise oder null (dann ohne Rauschunterdrückung).</param>
    /// <param name="schaetzer">Ersatz für die RNNoise-Wahrscheinlichkeit (Tests, Rückfall); sonst null.</param>
    /// <param name="referenz">Echoreferenz aus dem Loopback, oder null.</param>
    /// <param name="hoerprobe">Jitterpuffer der Hörprobe (Schreiber ist diese Kette), oder null.</param>
    /// <param name="referenzVad">
    /// Eigene RNNoise-Instanz für die Echoreferenz: Sie sagt dem <see cref="Maschine.Echoschutz"/>, ob die Wiedergabe
    /// sprachartig ist. Null: jede Wiedergabe gilt als sprachartig.
    /// </param>
    /// <param name="probeKodierer">
    /// Zweiter Kodierer nur für die Hörprobe als Aufnahme (die Kette ruft ihn nur im Aufnahme-Thread), oder null: dann gibt es
    /// keine Probe. Braucht <paramref name="hoerprobe"/>.
    /// </param>
    /// <param name="pegelstufe">Pegelstufe vor RNNoise, dazu ein Sicherheitsbegrenzer; null: keine (alte Kette).</param>
    /// <param name="telemetrie">Telemetrie der Kette; null: eine eigene, die niemand liest.</param>
    public Sendekette(
        IKodierer kodierer,
        EchoProzessor? echo,
        RnnoiseProzessor? rnnoise,
        ISprachwahrscheinlichkeit? schaetzer,
        PaketRing ausgang,
        Rahmenjournal journal,
        RahmenRing? referenz,
        Jitterpuffer? hoerprobe,
        Func<long> uhrUs,
        RnnoiseProzessor? referenzVad = null,
        IKodierer? probeKodierer = null,
        Pegelstufe? pegelstufe = null,
        Telemetriejournal? telemetrie = null,
        FunkSpur? funkSpur = null,
        IEchoWiedergabe? wiedergabe = null)
    {
        ArgumentNullException.ThrowIfNull(kodierer);
        _funkSpur = funkSpur;
        Waechter = new EigenerWaechter(wiedergabe: wiedergabe);
        ArgumentNullException.ThrowIfNull(ausgang);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(uhrUs);
        _echo = echo;
        _rnnoise = rnnoise;
        _referenzVad = referenzVad;
        _pegelstufe = pegelstufe;
        if (pegelstufe is not null) _begrenzer = new Begrenzer();
        _telemetrie = telemetrie ?? new Telemetriejournal();
        Array.Fill(_eingangVerlauf, Pegel.MinDbfs);
        _schaetzer = schaetzer;
        _sende = new Sendeaufbereitung(_fuehrung.Gate, kodierer);
        _ausgang = ausgang;
        _journal = journal;
        _referenz = referenz;
        _hoerprobe = hoerprobe;
        _uhrUs = uhrUs;
        if (probeKodierer is not null && hoerprobe is not null)
        {
            // 250 Pakete zu höchstens 400 Byte, einmal hier statt im Audio-Thread.
            _probeKodierer = probeKodierer;
            _probeDaten = new byte[ProbePakete][];
            for (var i = 0; i < ProbePakete; i++) _probeDaten[i] = new byte[Sendeaufbereitung.MaxPaketBytes];
            _probeLaengen = new int[ProbePakete];
        }
    }

    public GateFuehrung Fuehrung => _fuehrung;

    /// <summary>Der Wächter der eigenen Seite (Klangstufe C2a); nur der Aufnahme-Thread ruft ihn, andere lesen nur.</summary>
    internal EigenerWaechter Waechter { get; }

    /// <summary>Rahmen, die das APM als Aufnahme verarbeitet hat (Tests: Mikro stumm hält die Echounterdrückung am Laufen).</summary>
    internal long EchoRahmen => _echo?.AufnahmeRahmen ?? 0;

    private sealed class DaempfungFach(DaempfungEinstellung wert)
    {
        public DaempfungEinstellung Wert { get; } = wert;
    }

    private readonly FunkSpur? _funkSpur;
    private volatile DaempfungFach _daempfung = new(DaempfungEinstellung.AbWerk);
    private volatile bool _stumm;
    private volatile int _urteil;
    private bool _tasteGesperrt;
    private bool _funkspurSchutz;
    private int _kopplungGesehen;
    private float _funkDb = Pegel.MinDbfs;
    private bool _funkAnfang;
    private int _funkPlatz = -1;
    private float _referenzDb = float.NaN;

    /// <summary>Schalter "Auto Dauersend-Dämpfung" und Stufe (Klangstufe C2a); aus jedem Thread, gilt ab dem nächsten Rahmen.</summary>
    public DaempfungEinstellung Daempfung
    {
        get => _daempfung.Wert;
        set => _daempfung = new DaempfungFach(value);
    }

    /// <summary>
    /// Mikro stumm (Klangstufe C2a, Q31, Q36): Jeder Mikrofonrahmen wird nach der Echounterdrückung verworfen; eine offene Phase endet sofort mit
    /// ENDE, auch im Nachlauf der Sprechtaste, danach geht nichts hinaus, auch nicht mit der Sprechtaste. Pegelstufe, Gate, Wächter und Einmessen
    /// sehen nichts. Aus jedem Thread.
    /// </summary>
    public bool Stumm
    {
        get => _stumm;
        set => _stumm = value;
    }

    /// <summary>Das eigene Urteil des Wächters nach dem letzten Rahmen (aus jedem Thread).</summary>
    public DauersendUrteil Urteil => (DauersendUrteil)_urteil;

    /// <summary>Die Sprechtaste ist wegen eines Klemmens gesperrt, bis sie losgelassen wird (Tests).</summary>
    internal bool TasteGesperrt => _tasteGesperrt;

    /// <summary>Aus jedem Thread: den Wächter samt Funkspur-Schutz vergessen (anderes Mikrofon), greift im nächsten Rahmen.</summary>
    internal void WaechterZuruecksetzen() => Interlocked.Exchange(ref _waechterNeu, 1);

    private int _waechterNeu;
    private int _tasteStille;

    /// <summary>So lange Pause (Sprachwahrscheinlichkeit unter 0,9) wartet die Abschaltung der klemmenden Taste: 500 ms, länger als Pausen im Satz.</summary>
    internal const int TastePauseRahmen = 50;

    /// <summary>Nach einem Echo-Urteil ohne Echoreferenz: Der Echoschutz vergleicht mit der Funk-Wiedergabe des eigenen Mischers (Tests).</summary>
    internal bool FunkspurSchutz => _funkspurSchutz;

    /// <summary>Die Pegelstufe der Kette oder null (alte Kette); für Tests und die Maschine.</summary>
    internal Pegelstufe? Pegelstufe => _pegelstufe;

    /// <summary>
    /// Klangfarbe der eigenen Stimme (Q70 bis Q72, seit 0.9.3, <see cref="Verarbeitung.Klangfarbe"/>): aus jedem Thread, begrenzt; gilt ab dem
    /// nächsten Rahmen mit 20 ms Überblendung. Ab Werk neutral, dann ist die Stufe überbrückt.
    /// </summary>
    public KlangfarbeEinstellung Klangfarbe
    {
        get => _klangfarbe.Einstellung;
        set => _klangfarbe.Einstellung = value;
    }

    /// <summary>Die Stufe selbst (Tests: wirksame Einstellung, Überbrückung).</summary>
    internal Klangfarbe KlangfarbeStufe => _klangfarbe;

    /// <summary>Einmessen der Mindest-Funklautstärke (Klangstufe C1); die Kette füttert es je Rahmen.</summary>
    public Mindesteinmessung Mindesteinmessung { get; } = new();

    /// <summary>Der Korridor des letzten Rahmens; aus jedem Thread lesbar.</summary>
    public KorridorStand Korridor => new(_korridorStimme, _korridorUnten, _korridorOben, _korridorBezug, _korridorAnker);

    public Echoschutz Echoschutz => _echoschutz;

    private volatile bool _echoschutzAn = true;

    /// <summary>
    /// Klangmodul "Echoschutz" (seit 0.9.0 schaltbar, Sarevoc 06.10.2026, Q49): Das Echo der Lautsprecher öffnet die Sprachaktivierung nicht. Ab Werk an;
    /// aus nur mit Headset sinnvoll, sonst sendet die Sprachaktivierung die anderen zurück. Aus jedem Thread setzbar.
    /// </summary>
    public bool EchoschutzAn
    {
        get => _echoschutzAn;
        set => _echoschutzAn = value;
    }

    /// <summary>
    /// Voller Echoschutz nur bei sprachartiger Wiedergabe, bei Geräusch der kleine Abstand (Voreinstellung); falsch heißt:
    /// jede Wiedergabe zählt als Sprache, wie vor dem Modul aec3 (nur zum Vergleich in Messungen).
    /// </summary>
    public bool EchoschutzNurSprache { get; set; } = true;

    /// <summary>
    /// Voller Echoschutz auch dann, wenn der eigene Mischer gerade hörbar spielt (Voreinstellung); falsch heißt: nur die
    /// Sprachschätzung auf der Referenz zählt, wie im Modul aec3 vor der Gegenprüfung (nur zum Vergleich in Tests).
    /// </summary>
    public bool EchoschutzMitFunk { get; set; } = true;

    public Sendeaufbereitung Sendeaufbereitung => _sende;

    /// <summary>
    /// Pegelstufe und Begrenzer hinter RNNoise statt davor, wie die Recherche es vorschlägt (nur zum Vergleich in
    /// <c>MessungKlangA.Messung_Reihenfolge</c>; vor dem ersten Rahmen setzen). Die Pegelstufe bekommt dann die
    /// Sprachwahrscheinlichkeit des Rahmens selbst.
    /// </summary>
    public bool PegelstufeHinterRnnoise { get; set; }

    /// <summary>
    /// Wiedergabe-Thread: Der eigene Mischer hat gerade hörbar gespielt (Stimmen der anderen, Hörprobe, Klick). Das ist
    /// Sprache oder ihr Rand; der <see cref="Maschine.Echoschutz"/> verlangt dann den vollen Abstand, auch wenn RNNoise die
    /// Wiedergabe im Loopback neben Musik nicht als sprachartig erkennt. Gemessen (Gegenprüfung aec3, Funk neben Musik
    /// -42 bis -27 dBFS über die Lautsprecher, 75 Szenen zu 40 s): nur mit der Sprachschätzung 1 Auslösung durch das Echo,
    /// mit dieser Meldung keine (bei 3 dB Abstand für Geräusch 24 gegen 7). Ohne Sperre und ohne Allokation.
    /// </summary>
    public void FunkWiedergabeMelden() => Volatile.Write(ref _funkGespielt, 1);

    /// <summary>
    /// So viele Rahmen des Loopbacks ab dem Druck bleibt die Echoreferenz still: 30 ms Klick plus die Laufzeit vom Mischen bis in
    /// den Loopback (Wiedergabepuffer 30 ms, Mischtakt und Loopback-Paket je 10 ms), mit Reserve.
    /// </summary>
    public const int KlickAusblendenRahmen = 12;

    /// <summary>
    /// Wiedergabe-Thread: Der eigene Druck-Klick hat gerade begonnen (Sprechtaste gedrückt). Mit Lautsprechern käme er über den
    /// Loopback in die Echoreferenz, und AEC3 dämpfte den Anlaut, der mit dem Tastendruck zusammenfällt (Gegenprüfung 2c: erster
    /// Spruch -18,7 bis -22,6 statt -13,5 dBFS). Die Kette nimmt deshalb <see cref="KlickAusblendenRahmen"/> Rahmen der Referenz aus
    /// dem Spiel (Stille statt Loopback), gezählt ab dem nächsten Rahmen, den der Loopback ablegt: Der Klick ist gerade erst gemischt
    /// und kann nur in späteren Rahmen stehen. Das Fenster hängt so am Strom des Loopbacks, nicht an der Uhr: Liegen wegen zweier
    /// Geräteuhren mehrere Rahmen im Ring, verschiebt es sich mit (Gegenprüfung Klangstufe A: an der Uhr lief es bei 50 ms Verzug und
    /// 7 Rahmen Rückstand am Klick vorbei, Anlaut -42,5 statt -23,7 dBFS). Gemessen in <c>MessungKlangA.Messung_KlickImLautsprecher</c>.
    /// Ohne Sperre und ohne Allokation.
    /// </summary>
    public void EigenenKlickMelden()
    {
        if (EigenenKlickAusblenden && _referenz is not null) Volatile.Write(ref _klickAbRahmen, _referenz.Geschrieben);
    }

    /// <summary>Den eigenen Druck-Klick aus der Echoreferenz nehmen (Voreinstellung); falsch nur zum Vergleich in Messungen.</summary>
    public bool EigenenKlickAusblenden { get; set; } = true;

    /// <summary>Referenzrahmen, die wegen des eigenen Druck-Klicks still blieben (Diagnose, Tests).</summary>
    public long ReferenzAusgeblendet => Interlocked.Read(ref _referenzAusgeblendet);

    /// <summary>Aus jedem Thread; greift ab dem nächsten Rahmen. Der Wert muss bereinigt sein.</summary>
    public void GateSetzen(GateEinstellung e) => Volatile.Write(ref _gateFach, new GateFach(e));

    /// <summary>
    /// Aus jedem Thread: gewünschter Rauschfilter, greift ab dem nächsten Rahmen. Stark wirkt erst, wenn ein
    /// <see cref="DeepFilterProzessor"/> übergeben ist (<see cref="StarkUebergeben"/>) und kein Rückfall gilt; bis dahin Standard.
    /// </summary>
    public Rauschfilter Rauschfilter
    {
        get => (Rauschfilter)_rauschfilterWunsch;
        set => _rauschfilterWunsch = (int)value;
    }

    /// <summary>Der Filter, der gerade wirkt (in einer Blende der neue).</summary>
    public Rauschfilter RauschfilterWirksam => (Rauschfilter)_rauschfilterWirksam;

    /// <summary>Warum Stark nicht wirkt, obwohl gewünscht (0 = kein Rückfall); siehe <see cref="RueckfallRechenzeit"/>.</summary>
    public int Rueckfall => _rueckfall;

    /// <summary>Rückfall: die Kette brauchte mit DeepFilterNet3 zu oft länger als 10 ms.</summary>
    public const int RueckfallRechenzeit = 1;

    /// <summary>Rückfall: DeepFilterNet3 meldete einen Fehler (Zustand gesperrt).</summary>
    public const int RueckfallFehler = 2;

    /// <summary>Rückfall: DeepFilterNet3 ließ sich nicht anlegen (gemeldet von außen, <see cref="RueckfallSetzen"/>).</summary>
    public const int RueckfallNichtVerfuegbar = 3;

    /// <summary>Aus jedem Thread: Rückfall setzen (etwa DLL fehlt) oder mit 0 aufheben, damit Stark wieder wirken darf.</summary>
    public void RueckfallSetzen(int grund) => _rueckfall = grund;

    /// <summary>
    /// Aus jedem Thread außer dem Aufnahme-Thread: einen fertig angelegten DeepFilterNet3 hereinreichen; die Kette übernimmt ihn im
    /// nächsten Rahmen und gehört ihr danach. Rückgabe: ein noch nicht übernommener früherer, den der Aufrufer freigibt.
    /// </summary>
    public DeepFilterProzessor? StarkUebergeben(DeepFilterProzessor neu) => Interlocked.Exchange(ref _dfNeu, neu);

    /// <summary>Wahr, solange die Kette einen DeepFilterNet3 hat oder einer zur Übernahme bereitliegt.</summary>
    public bool HatStark => Volatile.Read(ref _df) is not null || Volatile.Read(ref _dfNeu) is not null;

    /// <summary>Außerhalb des Aufnahme-Threads: einen nicht mehr gebrauchten DeepFilterNet3 abholen, um ihn freizugeben.</summary>
    public DeepFilterProzessor? AusgedientenHolen() => Interlocked.Exchange(ref _dfAlt, null);

    /// <summary>Nur ohne Aufnahme-Thread (Maschine entsorgt): alle DeepFilterNet3 der Kette freigeben.</summary>
    public void StarkFreigeben()
    {
        Interlocked.Exchange(ref _dfNeu, null)?.Dispose();
        Interlocked.Exchange(ref _dfAlt, null)?.Dispose();
        Interlocked.Exchange(ref _df, null)?.Dispose();
    }

    /// <summary>Echoreferenz verarbeiten (AEC3 an). Das Einschalten im APM selbst geschieht beim Konfigurieren.</summary>
    public bool EchoAn
    {
        get => _echoAn;
        set => _echoAn = value;
    }

    private const int ZugRuhe = 0;
    private const int ZugGedrueckt = 1;
    private const int ZugLosgelassen = 2;

    /// <summary>Die Sprechtaste ist gerade gedrückt (ohne Nachlauf); aus jedem Thread.</summary>
    public bool Sprechtaste => Volatile.Read(ref _tastenZug) == ZugGedrueckt;

    /// <summary>Die Taste ist los, die Kette sendet aber noch ihren Nachlauf (<see cref="TastenNachlaufMs"/>); aus jedem Thread.</summary>
    public bool ImNachlauf => _imNachlauf;

    /// <summary>
    /// Aus jedem Thread (die Maschine ruft es unter ihrer Tastensperre): Taste gedrückt, greift ab dem nächsten Rahmen. Kommt sie im Nachlauf wieder,
    /// bevor die Kette ihn beendet hat, läuft mit <paramref name="fortsetzen"/> dieselbe Phase nahtlos weiter, ohne ENDE und ohne neuen Beginn
    /// (<see cref="TastenDruck.Fortgesetzt"/>). Ohne <paramref name="fortsetzen"/> (die andere Taste, also ein anderes Ziel) endet die Phase im
    /// nächsten Rahmen mit ENDE und Ausblendung, und der Rahmen danach beginnt eine neue (<see cref="TastenDruck.Geschnitten"/>): So bleibt der Rest
    /// des alten Spruchs an seinem Ziel, und die Brücke beginnt die neue Phase am neuen.
    /// </summary>
    public TastenDruck TasteDruecken(bool fortsetzen = true)
    {
        if (fortsetzen) return Interlocked.Exchange(ref _tastenZug, ZugGedrueckt) == ZugLosgelassen ? TastenDruck.Fortgesetzt : TastenDruck.Neu;
        // Vor dem Zustand setzen: Sähe der Aufnahme-Thread "gedrückt" ohne den Schnitt, ginge ein Rahmen des neuen Drucks noch in die alte Phase.
        // War kein Nachlauf offen, ist die Phase der Taste schon zu, und der Schnitt bleibt im nächsten Rahmen ohne Wirkung.
        Volatile.Write(ref _tastenSchnitt, 1);
        return Interlocked.Exchange(ref _tastenZug, ZugGedrueckt) == ZugLosgelassen ? TastenDruck.Geschnitten : TastenDruck.Neu;
    }

    /// <summary>
    /// Aus jedem Thread: Taste losgelassen. Die Kette sendet noch <see cref="TastenNachlaufMs"/> lang wie mit gedrückter Taste und meldet das
    /// Ende über <see cref="TasteBeendet"/> (dort kommt der Klick beim Loslassen); "Mikro stumm" beendet den Nachlauf sofort und ohne Meldung.
    /// </summary>
    public void TasteLoslassen() => Interlocked.CompareExchange(ref _tastenZug, ZugLosgelassen, ZugGedrueckt);

    /// <summary>
    /// Aus jedem Thread, wenn kein Mikrofon Rahmen liefert (keins offen, Audio gestoppt, Strom abgebrochen oder gewechselt): ein offenes Loslassen
    /// gleich abschließen, samt Nachlauf. Wahr, wenn eins offen war; dann klickt der Aufrufer selbst. Den Zähler des Nachlaufs räumt die Kette im
    /// nächsten Rahmen selbst ab; die Phase der Taste endet dort mit ENDE, auch wenn bis dahin wieder gedrückt wird (dann beginnt eine neue).
    /// </summary>
    public bool TasteAbschliessen()
    {
        if (Interlocked.CompareExchange(ref _tastenZug, ZugRuhe, ZugLosgelassen) != ZugLosgelassen) return false;
        _imNachlauf = false;
        Volatile.Write(ref _tastenSchnitt, 1);
        return true;
    }

    /// <summary>
    /// Nachlauf der Sprechtaste in ms (Q66, 0.9.3): So lange sendet die Kette nach dem Loslassen weiter wie mit gedrückter Taste, in ganzen
    /// Rahmen (abgerundet auf 10 ms). Aus jedem Thread; gilt ab dem nächsten Loslassen. Ab Werk 0, wie bis 0.9.2.
    /// </summary>
    public int TastenNachlaufMs
    {
        get => _nachlaufMs;
        set => _nachlaufMs = Math.Max(0, value);
    }

    /// <summary>
    /// Aufnahme-Thread: Die Kette hat das Senden nach dem Loslassen beendet, nach dem Nachlauf (oder gleich, wenn keiner lief). Ohne Allokation
    /// und ohne Sperre; die Maschine fordert hier den Klick beim Loslassen an. Nie bei "Mikro stumm".
    /// </summary>
    public Action? TasteBeendet { get; set; }

    /// <summary>
    /// Aufnahme-Thread, je Rahmen: Ist die Taste gedrückt (<paramref name="nachlauf"/> falsch) oder läuft ihr Nachlauf (dann gibt es falsch zurück
    /// und <paramref name="nachlauf"/> wahr)? Der Nachlauf beginnt nur, wenn die Taste im vorigen Rahmen wirklich sendete (nicht gesperrt).
    /// </summary>
    private bool TasteLesen(out bool nachlauf)
    {
        nachlauf = false;
        var zug = Volatile.Read(ref _tastenZug);
        if (zug == ZugLosgelassen)
        {
            if (_nachlaufRest < 0) _nachlaufRest = _fuehrung.Gate.TasteGedrueckt ? _nachlaufMs / 10 : 0;
            if (_nachlaufRest > 0)
            {
                _nachlaufRest--;
                _imNachlauf = true;
                nachlauf = true;
                return false;
            }
            // Im letzten Augenblick wieder gedrückt: Die Phase läuft weiter (außer bei einem Schnitt, unten).
            zug = Interlocked.CompareExchange(ref _tastenZug, ZugRuhe, ZugLosgelassen);
            if (zug == ZugLosgelassen)
            {
                _nachlaufRest = -1;
                _imNachlauf = false;
                TasteBeendet?.Invoke();
                return false;
            }
        }
        _nachlaufRest = -1;
        _imNachlauf = false;
        // Schnitt (im Nachlauf für ein anderes Ziel gedrückt, oder ohne Rahmen abgeschlossen): Läuft die Phase der Taste noch, endet sie in diesem
        // Rahmen mit ENDE und Ausblendung, und ein Druck beginnt im nächsten eine neue. Sonst hat er keine Wirkung.
        var schnitt = Volatile.Read(ref _tastenSchnitt) != 0 && Interlocked.Exchange(ref _tastenSchnitt, 0) != 0;
        return zug == ZugGedrueckt && !(schnitt && _fuehrung.Gate.TasteGedrueckt);
    }

    /// <summary>Mikro stumm: ein laufender Nachlauf endet sofort und ohne <see cref="TasteBeendet"/> (wie bisher kein Klick, wenn stumm).</summary>
    private void NachlaufVerwerfen()
    {
        _ = Interlocked.CompareExchange(ref _tastenZug, ZugRuhe, ZugLosgelassen);
        _nachlaufRest = -1;
        _imNachlauf = false;
    }

    public bool Sprachaktivierung
    {
        get => _fuehrung.Gate.SprachaktivierungAn;
        set => _fuehrung.Gate.SprachaktivierungAn = value;
    }

    public bool HoerprobeAn
    {
        get => _hoerprobeAn;
        set => _hoerprobeAn = value;
    }

    /// <summary>Aus jedem Thread: Probe (neu) beginnen; greift ab dem nächsten Rahmen. Ohne Probe-Kodierer wirkungslos.</summary>
    public void ProbeStarten()
    {
        if (_probeKodierer is not null) Volatile.Write(ref _probeBefehl, ProbeBefehlStarten);
    }

    /// <summary>Aus jedem Thread: laufende Probe abbrechen; greift ab dem nächsten Rahmen.</summary>
    public void ProbeAbbrechen()
    {
        if (_probeKodierer is not null) Volatile.Write(ref _probeBefehl, ProbeBefehlAbbrechen);
    }

    /// <summary>
    /// Stand der Probe für die Oberfläche. Ein noch nicht angenommener Befehl zählt schon: Nach dem Starten heißt es sofort
    /// Aufnahme, nach dem Abbrechen sofort bereit.
    /// </summary>
    public ProbeStand Probe
    {
        get
        {
            switch (Volatile.Read(ref _probeBefehl))
            {
                case ProbeBefehlStarten: return new ProbeStand(ProbeZustand.Aufnahme, IKlangMaschine.ProbeDauerMs);
                case ProbeBefehlAbbrechen: return new ProbeStand(ProbeZustand.Bereit, 0);
            }
            return (ProbeZustand)_probeZustand switch
            {
                ProbeZustand.Aufnahme => new ProbeStand(ProbeZustand.Aufnahme, (ProbePakete - _probeAufgenommen) * 20),
                ProbeZustand.Wiedergabe => new ProbeStand(ProbeZustand.Wiedergabe, Math.Max(0, _probeAufgenommen - _probeGespielt) * 20),
                _ => new ProbeStand(ProbeZustand.Bereit, 0),
            };
        }
    }

    /// <summary>
    /// Nur, wenn kein Aufnahme-Thread mehr in der Kette ist (Audio gestoppt): Probe ohne Wiedergabe beenden. Eine verworfene
    /// Phase der Sprachaktivierung bleibt verworfen, bis ihr ENDE kommt; sonst ginge ihr Rest ohne BEGINN hinaus.
    /// </summary>
    public void ProbeZuruecksetzen()
    {
        Volatile.Write(ref _probeBefehl, 0);
        _probeZustand = (int)ProbeZustand.Bereit;
        _probeBegonnen = false;
        _probeNachlauf = 0;
    }

    // ---- Stimmprobe für Klangmessungen (Q60): hinter Echo- und Rauschfilter, vor Klangfarbe und Gate ----

    /// <summary>Eine laufende Stimmprobe; der Speicher entsteht außerhalb des Aufnahme-Threads (keine Allokation im Takt).</summary>
    private sealed class StimmprobeFach(int rahmen)
    {
        public readonly float[] Daten = new float[rahmen * RahmenWerte];
        public int Rahmen;
        public volatile bool Fertig;
    }

    private StimmprobeFach? _stimmprobe;

    /// <summary>Aus jedem Thread: eine Stimmprobe von <paramref name="ms"/> beginnen (höchstens 60 s); greift ab dem nächsten Rahmen.</summary>
    public void StimmprobeStarten(int ms) => Volatile.Write(ref _stimmprobe, new StimmprobeFach(Math.Clamp(ms / 10, 1, 6000)));

    public StimmprobeStand Stimmprobe => Volatile.Read(ref _stimmprobe) is { } s
        ? new StimmprobeStand(!s.Fertig, s.Fertig, Volatile.Read(ref s.Rahmen) * 10, s.Daten.Length / RahmenWerte * 10)
        : default;

    /// <summary>Die fertige Stimmprobe oder <c>null</c>; danach ist sie weg.</summary>
    public float[]? StimmprobeHolen()
    {
        var s = Volatile.Read(ref _stimmprobe);
        if (s is not { Fertig: true }) return null;
        Interlocked.CompareExchange(ref _stimmprobe, null, s);
        return s.Daten;
    }

    /// <summary>Aufnahme-Thread: einen Rahmen in die laufende Stimmprobe legen.</summary>
    private void StimmprobeAufnehmen(ReadOnlySpan<float> signal)
    {
        var s = Volatile.Read(ref _stimmprobe);
        if (s is null || s.Fertig) return;
        signal.CopyTo(s.Daten.AsSpan(s.Rahmen * RahmenWerte, RahmenWerte));
        Volatile.Write(ref s.Rahmen, s.Rahmen + 1);
        if (s.Rahmen * RahmenWerte >= s.Daten.Length) s.Fertig = true;
    }

    /// <summary>Pakete der Sprachaktivierung, die während einer Probe nicht hinausgingen (Diagnose, Tests).</summary>
    public long VaVerworfenePakete => Interlocked.Read(ref _vaVerworfenePakete);

    public long VerarbeiteteRahmen => Interlocked.Read(ref _rahmen);

    public long Pakete => Interlocked.Read(ref _pakete);

    /// <summary>Rahmen, für die keine Echoreferenz bereitlag (Stille statt Referenz).</summary>
    public long ReferenzFehlt => Interlocked.Read(ref _referenzFehlt);

    /// <summary>Referenzrahmen, die wegen Nachhängens verworfen wurden.</summary>
    public long ReferenzVerworfen => Interlocked.Read(ref _referenzVerworfen);

    /// <summary>
    /// Wie oft beim Lesen der Echoreferenz <paramref name="rahmen"/> Rahmen bereitlagen (0 bis
    /// <see cref="ReferenzHoechstRahmen"/>, der letzte Platz zählt alles darüber). Jeder wartende Rahmen verzögert die
    /// Referenz um 10 ms gegenüber dem Mikrofon; AEC3 braucht die Referenz vor dem Echo (Diagnose, Rauchtest).
    /// </summary>
    public long ReferenzFuellstand(int rahmen) => Interlocked.Read(ref _referenzFuellstaende[Math.Clamp(rahmen, 0, ReferenzHoechstRahmen + 1)]);

    /// <summary>Pakete, die der Melde-Thread nicht rechtzeitig abholte (Sende-Ring voll).</summary>
    public long SendeVerworfen => Interlocked.Read(ref _sendeVerworfen);

    /// <summary>Aufnahme-Thread: einen Rahmen verarbeiten (480 Werte, wird in-place verändert).</summary>
    public void Verarbeiten(Span<float> rahmen)
    {
        if (rahmen.Length != RahmenWerte) throw new ArgumentException("Rahmen muss 480 Werte haben.", nameof(rahmen));
        var t0 = Stopwatch.GetTimestamp();
        var fach = Volatile.Read(ref _gateFach);
        if (!ReferenceEquals(fach, _gateGesehen))
        {
            _fuehrung.Einstellung = fach.Wert;
            _gateGesehen = fach;
        }
        var jetztUs = _uhrUs();
        if (_probeKodierer is not null) ProbeBefehlAnnehmen(jetztUs);
        FunkLesen();

        // Pegel vor dem APM: nur so ist er mit dem Wiedergabepegel vergleichbar (der Echoschutz).
        var eingangDb = Pegel.Dbfs(rahmen);
        if (_stumm)
        {
            // Mikro stumm beendet auch einen Nachlauf der Sprechtaste sofort.
            NachlaufVerwerfen();
            if (!Sprechtaste) _tasteGesperrt = false;
            StummVerarbeiten(rahmen, jetztUs, t0, eingangDb);
            return;
        }
        // Sprechtaste mit Nachlauf (Q66): Nach dem Loslassen sendet die Kette noch eine Weile wie mit gedrückter Taste; für Gate, Lernen und
        // Wächter ist das Tastenzeit. Klemmende Taste (Klangstufe C2a): Senden aus, bis die Taste losgelassen und neu gedrückt wird.
        var tasteGedrueckt = TasteLesen(out var nachlauf);
        if (!tasteGedrueckt) _tasteGesperrt = false;
        var taste = (tasteGedrueckt && !_tasteGesperrt) || nachlauf;
        _tasteImRahmen = tasteGedrueckt || nachlauf;
        var echoVerdacht = false;
        var mitReferenz = false;
        float referenzUs = float.NaN, echoUs = float.NaN, rauschfilterUs = float.NaN, pegelstufeUs = float.NaN, begrenzerUs = float.NaN;
        if (_echo is not null)
        {
            var t = Stopwatch.GetTimestamp();
            if (_echoAn && _referenz is not null)
            {
                ReferenzEinspeisen();
                mitReferenz = true;
                _echoschutz.SprachAbstandDb = Echoschutz.SicherheitDb;
                _echoschutz.VerzugRahmen = -1;
                echoVerdacht = EchoschutzAn && _echoschutz.Verdacht(eingangDb, _fuehrung.Offen || _fuehrung.Gate.Zustand == GateZustand.Zurueckgehalten, taste);
                referenzUs = Mikrosekunden(t, out t);
            }
            _ = _echo.VerarbeiteAufnahme(rahmen);
            echoUs = Mikrosekunden(t, out _);
        }
        _fuehrung.EchoOhneStatistik = !mitReferenz && _funkspurSchutz;
        if (!mitReferenz && _funkspurSchutz)
        {
            // Nach einem Echo-Urteil ohne Echoreferenz (Echounterdrückung aus, Lautsprecher oder offene Kopfhörer): Der Echoschutz vergleicht mit
            // der Funk-Wiedergabe des eigenen Mischers, mit der vom Wächter gemessenen Kopplung.
            _echoschutz.SprachAbstandDb = Echoschutz.FunkspurSicherheitDb;
            _echoschutz.Wiedergabe(_funkDb, sprachartig: true);
            echoVerdacht = EchoschutzAn && _echoschutz.Verdacht(eingangDb, _fuehrung.Offen || _fuehrung.Gate.Zustand == GateZustand.Zurueckgehalten, taste);
        }
        var echoDb = Pegel.Dbfs(rahmen);
        // Einmessen der Mindest-Funklautstärke (Klangstufe C1): Pegel und Spitze vor der Pegelstufe.
        var einmessSpitze = Mindesteinmessung.Laeuft ? Pegel.LinearZuDb(Pegel.Spitze(rahmen)) : float.NaN;
        // Lernt die Pegelstufe aus diesem Rahmen? Gesendet und Sprache wie bisher aus dem vorigen Rahmen, Echo aus diesem; die Mindest-
        // Funklautstärke prüft sie je Äußerung; der Wächter sperrt während eines Urteils und in deutlich leisen Phasen (Klangstufe C2a), nur
        // mit Schalter an (aus heißt: urteilen, aber nichts stellen).
        var freigegeben = Lernfreigabe.Pruefen(new LernfreigabeLage(taste || _sendetVorher, _pVorher, echoVerdacht, !_daempfung.Wert.An || Waechter.LernenErlaubt));
        // Lernen nur aus Funkniveau (Klangstufe C2a, Q38): Mit der Auto Dauersend-Dämpfung zählt eine Äußerung ab 9 dB unter dem Anker nicht als
        // eigene Funkstimme (Sprechtaste ausgenommen), sonst zöge leises Reden im Raum nach drei Sätzen die Halteregel und den Wächter mit.
        if (_pegelstufe is not null)
        {
            var mindest = _fuehrung.Einstellung.MindestAbstandDb;
            _pegelstufe.MindestAbstandDb = _daempfung.Wert.An ? MathF.Min(mindest, EigenerWaechter.RaumUnterStimmeDb) : mindest;
        }

        // Pegelstufe und Begrenzer vor RNNoise, mit der Sprachwahrscheinlichkeit des vorigen Rahmens (RNNoise rechnet sie erst
        // danach); der Begrenzer hält die Spitzen unter 0 dBFS, bevor RNNoise auf [-1, 1] beschneidet.
        var verstaerkung = 0f;
        var begrenzerDb = 0f;
        var hinter = PegelstufeHinterRnnoise;
        if (_pegelstufe is not null && !hinter) Verstaerken(rahmen, _pVorher, taste || _sendetVorher, freigegeben, ref verstaerkung, ref begrenzerDb, ref pegelstufeUs, ref begrenzerUs);
        var verstaerktDb = _pegelstufe is null || hinter ? echoDb : Pegel.Dbfs(rahmen);

        Span<float> signal = rahmen;
        var p = 0f;
        var verzug = 0;
        var starkGerechnet = false;
        if (_rnnoise is not null)
        {
            var t = Stopwatch.GetTimestamp();
            p = _rnnoise.Verarbeiten(rahmen, _entrauscht);
            _pVorher = p;
            WunschUebernehmen();
            // Pause: Gate zu und keine Taste, keine Probe; nur dort darf die Wahl Rahmen doppeln oder auslassen.
            var pause = !taste && _fuehrung.Gate.Zustand == GateZustand.Leise && _probeZustand == (int)ProbeZustand.Bereit;
            if (_wahl.StarkGebraucht && _df is not null)
            {
                starkGerechnet = _df.Verarbeiten(rahmen, _stark);
                if (!starkGerechnet) _rueckfall = RueckfallFehler;
            }
            _wahl.Verarbeiten(rahmen, _entrauscht, starkGerechnet ? _stark : [], pause, _gewaehlt);
            _rauschfilterWirksam = (int)_wahl.Aktiv;
            rauschfilterUs = Mikrosekunden(t, out _);
            signal = _gewaehlt;
            verzug = _wahl.Verzug;
            p = WahrscheinlichkeitVerzoegern(p, verzug);
        }
        if (_schaetzer is not null)
        {
            p = _schaetzer.Schaetzen(signal);
            _pVorher = p;
        }
        var rauschfilterDb = Pegel.Dbfs(signal);
        StimmprobeAufnehmen(signal);
        // Die Kette verzögert um verzug Rahmen: Die Wirkung des Filters ist der Pegel dahinter gegen den Pegel davor von damals.
        var vorher = EingangVerlauf(verstaerktDb, verzug);
        var rauschfilterWirkung = verzug > 0 && vorher > Pegel.MinDbfs ? rauschfilterDb - vorher : 0f;
        if (_pegelstufe is not null && hinter) Verstaerken(signal, p, taste || _sendetVorher, freigegeben, ref verstaerkung, ref begrenzerDb, ref pegelstufeUs, ref begrenzerUs);
        Mindesteinmessung.Zufuehren(echoDb, einmessSpitze, _pVorher);

        // Der Pegel für Gate, Führung, Korridor, Pegelanzeige und Journal: vor der Klangfarbe gemessen, damit sie dieselben Werte sehen wie ohne.
        var pegel = Pegel.Dbfs(signal);
        // Klangfarbe (Q70 bis Q72, 0.9.3) mit eigenem Begrenzer, hinter dem Abgriff der Stimmprobe; neutral überbrückt (Rahmen bitgleich).
        _klangfarbe.Verarbeiten(signal);

        // Die Probe nimmt auf, was gesendet würde, aber vor dem Gate: unabhängig von Sprechtaste, Schwelle und Auslösung; mit Klangfarbe.
        if (_probeZustand == (int)ProbeZustand.Aufnahme) ProbeAufnehmen(signal);

        var tGate = Stopwatch.GetTimestamp();
        _fuehrung.StimmeDb = StimmeAmGate();
        _fuehrung.BezugDb = _pegelstufe is { StimmeBekannt: true } ps && float.IsFinite(ps.AnkerDbfs) ? ps.AnkerDbfs + ps.VerstaerkungDb : float.NaN;
        var pGate = _fuehrung.Vorbereiten(pegel, p, taste, echoVerdacht);
        // Funkniveau während der Raum-Anhebung am Pegel vor dem Rauschfilter (RNNoise dämpft Wortanfänge nach leiser Rede, SprachGate.VorfilterDbfs).
        _fuehrung.Gate.VorfilterDbfs = vorher;
        KorridorMelden(_fuehrung.Korridor);
        // Das Gate nimmt den Rahmen mit Klangfarbe, aber den Pegel von davor (gegeben statt gemessen, also genau derselbe Wert).
        var n = _sende.Verarbeiten(signal, pGate, 0f, taste, pegel);
        var gateTakte = Stopwatch.GetTimestamp() - tGate - _sende.LetzteKodierTakte;
        var probeLaeuft = _probeZustand != (int)ProbeZustand.Bereit;
        // Während des Einmessens beginnt die Sprachaktivierung nichts Neues (wie bei der Hörprobe); die Sprechtaste sendet.
        if (n > 0) PaketeAusgeben(n, jetztUs, probeLaeuft || _probeNachlauf > 0 || Mindesteinmessung.Laeuft);
        if (_hpOffen && !_hoerprobeAn) HoerprobeSchliessen(jetztUs);
        if (_probeZustand == (int)ProbeZustand.Wiedergabe) ProbeAbspielen(jetztUs);
        else if (_probeNachlauf > 0) _probeNachlauf--;

        var gate = _fuehrung.Gate;
        var merkmale = RahmenMerkmal.Keine;
        if (gate.TasteGedrueckt) merkmale |= RahmenMerkmal.Sendet | RahmenMerkmal.Sprechtaste;
        else if (gate.Zustand is GateZustand.Senden or GateZustand.Nachlauf && !_vaVerworfen) merkmale |= RahmenMerkmal.Sendet | RahmenMerkmal.Sprachaktivierung;
        // 0.10.0 (E4a): Eine bestätigte Phase unter der Mindest-Funklautstärke; die Statuszeile sagt es in Wörtern. Mit Taste gibt es das nicht.
        else if (gate.Zustand == GateZustand.Zurueckgehalten) merkmale |= RahmenMerkmal.Zurueckgehalten;
        _journal.Schreiben(new RahmenEintrag(jetztUs / 1000, pegel, gate.RauschbodenDbfs, _fuehrung.AnzeigeLinieDb, p, merkmale));
        var opusUs = _sende.LetzteKodierungen > 0 ? Takte(_sende.LetzteKodierTakte) : float.NaN;
        var gesamtUs = Takte(Stopwatch.GetTimestamp() - t0);
        _starkRahmen = starkGerechnet ? _starkRahmen + 1 : 0;
        // Im Anlauf nicht zählen: Die ersten Rahmen kalt (Seiten der DLL, Speicher von tract, JIT) dauern einmalig länger.
        if (_starkRahmen > Rauschfilterwahl.AnlaufRahmen) RechenzeitPruefen(gesamtUs);
        var zeiten = new Stufenzeiten(referenzUs, echoUs, rauschfilterUs, pegelstufeUs, begrenzerUs, Takte(gateTakte), opusUs, gesamtUs);
        _telemetrie.Schreiben(new TelemetrieEintrag(_rahmen, jetztUs / 1000, eingangDb, echoDb, verstaerktDb, rauschfilterDb, rauschfilterWirkung, verstaerkung,
            begrenzerDb, pegel, p, gate.Zustand, (merkmale & RahmenMerkmal.Sendet) != 0, zeiten));
        // Für die Pegelstufe im nächsten Rahmen: Sie lernt nur, was hinausgeht, und schreitet nur ohne Sendung.
        _sendetVorher = (merkmale & RahmenMerkmal.Sendet) != 0;
        Waechten(gate, echoDb, eingangDb, mitReferenz, tasteGedrueckt, nachlauf, echoVerdacht);
        _rahmen++;
    }

    /// <summary>Neue Blöcke der Funkspur (Wiedergabe-Thread) zu diesem Rahmen zusammenfassen; ohne neue gilt der vorige Pegel, ein Anfang nur einmal.</summary>
    private void FunkLesen()
    {
        _funkAnfang = false;
        if (_funkSpur is null || !_funkSpur.Lesen(out var db, out var anfang, out var platz)) return;
        _funkDb = db;
        _funkAnfang = anfang;
        _funkPlatz = platz;
    }

    /// <summary>
    /// Der Wächter der eigenen Seite (Klangstufe C2a) urteilt auf dem Pegel vor der Pegelstufe; die vorhandenen Regler handeln, solange die Auto
    /// Dauersend-Dämpfung an ist: Echoschutz (Kopplung, Abstand bei Spielton), GateFuehrung (Wunsch bei Raumgeräusch), Sprechtaste (klemmt). Die
    /// Linie steigt nur bei geschlossenem Gate (an Phasengrenzen, keine laufende Phase endet deswegen), sie sinkt sofort. Für den Wächter ist der
    /// Nachlauf der Sprechtaste (<paramref name="nachlauf"/>) Tastenzeit; die Regel der klemmenden Taste zählt nur, solange sie wirklich gedrückt ist.
    /// </summary>
    private void Waechten(SprachGate gate, float echoDb, float eingangDb, bool mitReferenz, bool tasteGedrueckt, bool nachlauf, bool echoGesperrt)
    {
        var w = Waechter;
        if (Interlocked.Exchange(ref _waechterNeu, 0) != 0)
        {
            // Gegenprüfung C2a, M4: anderes Mikrofon, alles vergessen, auch Kopplung und Funkspur-Schutz.
            w.Zuruecksetzen();
            _echoschutz.Zuruecksetzen();
            _funkspurSchutz = false;
            _echoschutz.VerzugRahmen = -1;
            _kopplungGesehen = w.KopplungFassung;
            _tasteGesperrt = false;
            _tasteStille = 0;
            _fuehrung.ZusatzWunschDb = float.NegativeInfinity;
        }
        var an = _daempfung.Wert.An;
        w.Handeln = an;
        w.RaumAn = an && _daempfung.Wert.LeisesRedenSperren;
        // Die Stimme für den Wächter: der feste Anker (Grundpegel, als die Stimme bekannt wurde), nicht der wandernde Grundpegel, damit leises Reden
        // seinen eigenen Bezug nicht mitzieht (Q38); ohne Anker der Grundpegel.
        var stimme = _pegelstufe is { StimmeBekannt: true } ps && float.IsFinite(ps.GrundpegelDbfs) ? (float.IsFinite(ps.AnkerDbfs) ? ps.AnkerDbfs : ps.GrundpegelDbfs) : float.NaN;
        var offen = gate.Zustand is GateZustand.Senden or GateZustand.Nachlauf;
        w.Rahmen(new WaechterRahmen
        {
            EigenDb = echoDb,
            EingangDb = eingangDb,
            WiedergabeDb = mitReferenz || _funkspurSchutz ? _echoschutz.WiedergabeDb : float.NaN,
            P = _pVorher,
            Gate = gate.Zustand,
            VaSendet = offen && !gate.TasteGedrueckt && !_vaVerworfen,
            Taste = tasteGedrueckt || nachlauf,
            FunkDb = _funkDb,
            FunkAnfang = _funkAnfang,
            FunkPlatz = _funkPlatz,
            SpielDb = mitReferenz ? _referenzDb : float.NaN,
            SpielSprachartig = mitReferenz && _echoschutz.Sprachartig,
            SpielLaeuft = mitReferenz,
            StimmeDb = stimme,
            EchoGesperrt = echoGesperrt,
        });
        _urteil = (int)w.Urteil;
        if (!an)
        {
            // Aus heißt: urteilen, aber nichts stellen, auch kein Funkspur-Schutz mehr (Gegenprüfung C2a, M4). Wird der Schalter wieder an, gilt
            // die zuletzt gemessene Kopplung wieder.
            _echoschutz.WaechterAbstandDb = 0f;
            _fuehrung.ZusatzWunschDb = float.NegativeInfinity;
            if (_funkspurSchutz)
            {
                _funkspurSchutz = false;
                _echoschutz.VerzugRahmen = -1;
            }
            _kopplungGesehen = -1;
            _tasteStille = 0;
            return;
        }
        if (w.KopplungFassung != _kopplungGesehen)
        {
            _kopplungGesehen = w.KopplungFassung;
            if (mitReferenz)
            {
                _echoschutz.VerzugRahmen = -1;
                _echoschutz.KopplungKorrigieren(w.KopplungDb);
            }
            else if (w.EchoVerzugRahmen >= 0 && float.IsFinite(w.KopplungVerzugDb))
            {
                // Ohne Echoreferenz: Schutz mit der Funk-Wiedergabe, im Takt des Echos und mit der Kopplung in diesem Takt.
                _funkspurSchutz = true;
                _echoschutz.VerzugRahmen = w.EchoVerzugRahmen;
                _echoschutz.KopplungKorrigieren(w.KopplungVerzugDb);
            }
        }
        // Mehr Abstand bei Spielton nur für Rahmen ohne eigene Sprache: Sonst sperrt er Anlaute der eigenen Stimme mit (SpieltonTests, Lautsprecher
        // 12 dB: 30 statt 19 von 65 verpasst). Der Rest des Spieltons hinter AEC3 hat kaum je Sprachwahrscheinlichkeit ab 0,9.
        _echoschutz.WaechterAbstandDb = _pVorher >= Pegelstufe.SprachSchwelle ? 0f : w.GeraeuschZusatzDb;
        var wunsch = float.IsFinite(w.RaumWunschVorStufeDb) ? w.RaumWunschVorStufeDb + (_pegelstufe?.VerstaerkungDb ?? 0f) : float.NegativeInfinity;
        if (wunsch < _fuehrung.ZusatzWunschDb || gate.Zustand == GateZustand.Leise) _fuehrung.ZusatzWunschDb = wunsch;
        // Klemmende Taste: abschalten erst in der nächsten Pause (500 ms ohne Sprache), nie mitten im Satz (Gegenprüfung C2a, M1). Nur mit
        // gedrückter Taste: Im Nachlauf gezählt, könnte die Sperre dort greifen und einen neuen Druck still schlucken (Prüfung 0.9.3).
        if (w.TasteKlemmt && tasteGedrueckt && !_tasteGesperrt)
        {
            _tasteStille = _pVorher < Pegelstufe.SprachSchwelle ? _tasteStille + 1 : 0;
            if (_tasteStille >= TastePauseRahmen) _tasteGesperrt = true;
        }
        else
        {
            _tasteStille = 0;
        }
    }

    /// <summary>
    /// Mikro stumm: Echoreferenz und APM rechnen weiter (die Echounterdrückung bleibt eingeschwungen), dann wird der Rahmen verworfen. Eine offene
    /// Phase endet sofort mit ENDE (ihr Rest wird nicht gesendet). Journal und Telemetrie bekommen einen Eintrag "stumm".
    /// </summary>
    private void StummVerarbeiten(Span<float> rahmen, long jetztUs, long t0, float eingangDb)
    {
        if (_echo is not null)
        {
            if (_echoAn && _referenz is not null) ReferenzEinspeisen();
            _ = _echo.VerarbeiteAufnahme(rahmen);
        }
        var n = _sende.Abbrechen();
        if (n > 0) PaketeAusgeben(n, jetztUs, false);
        var gate = _fuehrung.Gate;
        // Gegenprüfung C2a, M2: Nach dem Lösen darf der Vorlauf nichts von vor dem Stummschalten enthalten.
        gate.VorpufferLeeren();
        _journal.Schreiben(new RahmenEintrag(jetztUs / 1000, Pegel.MinDbfs, gate.RauschbodenDbfs, _fuehrung.AnzeigeLinieDb, 0f, RahmenMerkmal.Stumm));
        var zeiten = new Stufenzeiten(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, Takte(Stopwatch.GetTimestamp() - t0));
        _telemetrie.Schreiben(new TelemetrieEintrag(_rahmen, jetztUs / 1000, eingangDb, Pegel.MinDbfs, Pegel.MinDbfs, Pegel.MinDbfs, 0f,
            _pegelstufe?.VerstaerkungDb ?? 0f, 0f, Pegel.MinDbfs, 0f, gate.Zustand, false, zeiten));
        _sendetVorher = false;
        _urteil = (int)DauersendUrteil.Keins;
        _rahmen++;
    }

    /// <summary>
    /// Die gelernte Funkstimme am Gate: Grundpegel der Halteregel plus jetzige Verstärkung; NaN ohne Pegelstufe und solange die Stimme
    /// nicht als gelernt gilt (<see cref="Verarbeitung.Pegelstufe.StimmeBekannt"/>).
    /// </summary>
    private float StimmeAmGate() =>
        _pegelstufe is { StimmeBekannt: true } s && float.IsFinite(s.GrundpegelDbfs) ? s.GrundpegelDbfs + s.VerstaerkungDb : float.NaN;

    private volatile float _korridorStimme = float.NaN;
    private volatile float _korridorUnten = float.NaN;
    private volatile float _korridorOben = float.NaN;
    private volatile float _korridorBezug = float.NaN;
    private volatile float _korridorAnker = float.NaN;

    /// <summary>Den Korridor dieses Rahmens für andere Threads ablegen (drei einzelne Werte, ohne Allokation).</summary>
    private void KorridorMelden(KorridorStand k)
    {
        _korridorStimme = k.StimmeDb;
        _korridorUnten = k.UntergrenzeDb;
        _korridorOben = k.ObergrenzeDb;
        _korridorBezug = k.BezugDb;
        _korridorAnker = _pegelstufe is { StimmeBekannt: true } p ? p.AnkerDbfs : float.NaN;
    }

    /// <summary>Pegelstufe und Begrenzer auf <paramref name="x"/>, mit Laufzeiten.</summary>
    private void Verstaerken(Span<float> x, float p, bool gesendet, bool freigegeben, ref float verstaerkung, ref float begrenzerDb, ref float pegelstufeUs,
        ref float begrenzerUs)
    {
        var t = Stopwatch.GetTimestamp();
        verstaerkung = _pegelstufe!.Verarbeiten(x, p, gesendet, freigegeben, _tasteImRahmen);
        pegelstufeUs = Mikrosekunden(t, out t);
        _begrenzer!.Verarbeiten(x, Span<float>.Empty);
        begrenzerDb = MathF.Min(0f, Pegel.LinearZuDb(_begrenzer.KleinsterFaktor));
        begrenzerUs = Mikrosekunden(t, out _);
    }

    /// <summary>Mikrosekunden seit <paramref name="start"/>; <paramref name="jetzt"/> ist der neue Startpunkt.</summary>
    private static float Mikrosekunden(long start, out long jetzt)
    {
        jetzt = Stopwatch.GetTimestamp();
        return Takte(jetzt - start);
    }

    private static float Takte(long takte) => (float)(takte * 1_000_000.0 / Stopwatch.Frequency);

    /// <summary>Merkt den Pegel vor dem Rauschfilter und liefert den von vor <paramref name="verzug"/> Rahmen.</summary>
    private float EingangVerlauf(float db, int verzug)
    {
        _eingangIndex = (_eingangIndex + 1) % _eingangVerlauf.Length;
        _eingangVerlauf[_eingangIndex] = db;
        return _eingangVerlauf[(_eingangIndex - verzug + _eingangVerlauf.Length) % _eingangVerlauf.Length];
    }

    /// <summary>
    /// Wunsch und DeepFilterNet3 übernehmen (Aufnahme-Thread, ohne Sperre und Allokation): ein hereingereichter Filter wird der eigene;
    /// Stark gilt nur mit Filter und ohne Rückfall; ein Filter, der nicht mehr gebraucht wird, geht zum Freigeben in das Fach
    /// <see cref="AusgedientenHolen"/> (ist das noch belegt, wartet er einen Rahmen).
    /// </summary>
    private void WunschUebernehmen()
    {
        if (Volatile.Read(ref _dfNeu) is not null && (_df is null || Ablegen(_df)))
        {
            Volatile.Write(ref _df, Interlocked.Exchange(ref _dfNeu, null));
        }
        var wunsch = (Rauschfilter)_rauschfilterWunsch;
        if (wunsch == Rauschfilter.Stark && (_df is null || _rueckfall != 0 || _df.Gesperrt)) wunsch = Rauschfilter.Standard;
        _wahl.Ziel = wunsch;
        if (_df is not null && !_wahl.StarkGebraucht && (_rueckfall != 0 || (Rauschfilter)_rauschfilterWunsch != Rauschfilter.Stark) && Ablegen(_df))
        {
            Volatile.Write(ref _df, null);
        }
    }

    /// <summary>Legt einen DeepFilterNet3 zum Freigeben ab; falsch, wenn das Fach noch belegt ist.</summary>
    private bool Ablegen(DeepFilterProzessor df) => Interlocked.CompareExchange(ref _dfAlt, df, null) is null;

    /// <summary>Rückfall auf Standard, wenn die Kette mit DeepFilterNet3 binnen einer Sekunde zu oft länger als 10 ms rechnete.</summary>
    private void RechenzeitPruefen(float gesamtUs)
    {
        if (gesamtUs <= Telemetriejournal.BudgetUs) return;
        if (_rahmen - _rueckfallFensterAb >= RueckfallFensterRahmen)
        {
            _rueckfallFensterAb = _rahmen;
            _rueckfallZaehler = 0;
        }
        if (++_rueckfallZaehler >= RueckfallFristen) _rueckfall = RueckfallRechenzeit;
    }

    private void ReferenzEinspeisen()
    {
        var referenz = _referenz!;
        _referenzFuellstaende[Math.Clamp(referenz.Fuellstand, 0, ReferenzHoechstRahmen + 1)]++;
        if (referenz.Fuellstand > ReferenzHoechstRahmen)
            _referenzVerworfen += referenz.Kuerzen(ReferenzReserveRahmen);
        var nummer = referenz.NaechsterLesen;
        if (!referenz.Lesen(_referenzRahmen))
        {
            // Der Lautsprecher spielt gerade nichts (oder der Loopback hängt): Stille als Referenz hält Aufnahme und
            // Wiedergabe im APM im Gleichschritt.
            Array.Clear(_referenzRahmen);
            _referenzFehlt++;
        }
        else if (Volatile.Read(ref _klickAbRahmen) is var ab && ab != long.MinValue
            && unchecked(nummer - (int)ab) is >= 0 and < KlickAusblendenRahmen)
        {
            // Der eigene Druck-Klick: nicht als Echo erwarten, sonst dämpft AEC3 den Anlaut (EigenenKlickMelden).
            Array.Clear(_referenzRahmen);
            _referenzAusgeblendet++;
        }
        // Die Wahrscheinlichkeit von RNNoise gilt für den gerade gelesenen Rahmen; das entrauschte Signal wird nicht gebraucht.
        // RNNoise läuft jeden Rahmen, damit sein Zustand lückenlos bleibt.
        var p = _referenzVad?.Verarbeiten(_referenzRahmen, _referenzEntrauscht) ?? 1f;
        var funk = Interlocked.Exchange(ref _funkGespielt, 0) != 0 && EchoschutzMitFunk;
        var sprachartig = !EchoschutzNurSprache || funk || p >= Echoschutz.SprachSchwelle;
        _referenzDb = Pegel.Dbfs(_referenzRahmen);
        _echoschutz.Wiedergabe(_referenzDb, sprachartig);
        _ = _echo!.VerarbeiteWiedergabe(_referenzRahmen);
    }

    /// <summary>Merkt die Sprachwahrscheinlichkeit dieses Rahmens und liefert die von vor <paramref name="verzug"/> Rahmen.</summary>
    private float WahrscheinlichkeitVerzoegern(float p, int verzug)
    {
        _pIndex = (_pIndex + 1) % _pVerlauf.Length;
        _pVerlauf[_pIndex] = p;
        return _pVerlauf[(_pIndex - verzug + _pVerlauf.Length) % _pVerlauf.Length];
    }

    /// <param name="vaGesperrt">Probe läuft oder klingt aus: Eine jetzt beginnende Phase der Sprachaktivierung geht nicht hinaus.</param>
    private void PaketeAusgeben(int n, long jetztUs, bool vaGesperrt)
    {
        // Aufnahmezeit: Das letzte Audiopaket endet mit diesem Rahmen, jedes frühere liegt 20 ms davor.
        var audio = 0;
        for (var i = 0; i < n; i++)
        {
            if (_sende.Paket(i).Length > 0) audio++;
        }
        var takte = Stopwatch.GetTimestamp() - Stopwatch.Frequency / 50;
        var j = 0;
        // Das Mithören ruht während der Probe; beide teilen sich den Jitterpuffer der Hörprobe.
        var mithoeren = _hoerprobe is not null && _probeZustand == (int)ProbeZustand.Bereit;
        for (var i = 0; i < n; i++)
        {
            var k = _sende.Kennzeichen(i);
            var daten = _sende.Paket(i);
            var paketTakte = daten.IsEmpty ? takte : takte - (audio - 1 - j++) * Stopwatch.Frequency / 50;
            // Ganz oder gar nicht: Wird die Phase beim BEGINN verworfen, dann bis zu ihrem ENDE, auch über das Ende der Probe
            // hinaus. Sonst ginge ein Rest ohne BEGINN hinaus. Die Sprechtaste beginnt immer eine eigene Phase.
            if ((k & Sprachkennzeichen.Beginn) != 0) _vaVerworfen = vaGesperrt && (k & Sprachkennzeichen.Sprachaktivierung) != 0;
            if (_vaVerworfen)
            {
                _vaVerworfenePakete++;
                if ((k & Sprachkennzeichen.Ende) != 0) _vaVerworfen = false;
                continue;
            }
            if (!_ausgang.Schreiben(paketTakte, 0, 0, k, daten)) _sendeVerworfen++;
            if (mithoeren) HoerprobeEinreihen(k, daten, jetztUs);
            _pakete++;
        }
    }

    // ---------------------------------------------------------------- Hörprobe als Aufnahme (nur Aufnahme-Thread)

    private void ProbeBefehlAnnehmen(long jetztUs)
    {
        var befehl = Volatile.Read(ref _probeBefehl);
        if (befehl == 0) return;
        // Eine laufende Wiedergabe endet mit ENDE, damit der Puffer sauber ausblendet, auch vor einem Neustart.
        if (_probeZustand == (int)ProbeZustand.Wiedergabe) WiedergabeBeenden(jetztUs);
        if (befehl == ProbeBefehlStarten)
        {
            // Das Mithören ruht während der Probe; eine offene Phase dort endet hier.
            if (_hpOffen) HoerprobeSchliessen(jetztUs);
            _probeAufgenommen = 0;
            _probeGespielt = 0;
            _probeHalb = false;
            _probeZustand = (int)ProbeZustand.Aufnahme;
        }
        else
        {
            _probeZustand = (int)ProbeZustand.Bereit;
        }
        // Erst nach dem Zustand löschen, damit die Oberfläche nie einen Zwischenstand sieht. Kam inzwischen ein anderer
        // Befehl, bleibt er für den nächsten Rahmen stehen.
        _ = Interlocked.CompareExchange(ref _probeBefehl, 0, befehl);
    }

    /// <summary>Zwei 10-ms-Rahmen zu einem 20-ms-Paket kodieren, wie die Sendeaufbereitung, nur ohne Gate.</summary>
    private void ProbeAufnehmen(ReadOnlySpan<float> signal)
    {
        if (!_probeHalb)
        {
            signal.CopyTo(_probePaar);
            _probeHalb = true;
            return;
        }
        signal.CopyTo(_probePaar.AsSpan(RahmenWerte));
        _probeHalb = false;
        var i = _probeAufgenommen;
        int laenge;
        try
        {
            laenge = _probeKodierer!.Kodieren(_probePaar, _probeDaten[i]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            laenge = 0;
        }
        // Ein Kodierfehler fehlt in der Wiedergabe als Lücke, die der Puffer verdeckt, wie beim Empfänger.
        _probeLaengen[i] = laenge is > 0 and <= Sendeaufbereitung.MaxPaketBytes ? laenge : 0;
        _probeAufgenommen = i + 1;
        if (i + 1 < ProbePakete) return;
        _probeGespielt = 0;
        _probeTakt = false;
        _probeBegonnen = false;
        _hpPhase++;
        _probeZustand = (int)ProbeZustand.Wiedergabe;
    }

    /// <summary>Ein Paket je 20 ms (jeder zweite Rahmen) in den Jitterpuffer der Hörprobe; der stempelt es wie beim Empfang.</summary>
    private void ProbeAbspielen(long jetztUs)
    {
        _probeTakt = !_probeTakt;
        if (!_probeTakt) return;
        var i = _probeGespielt;
        if (i < _probeAufgenommen)
        {
            var laenge = _probeLaengen[i];
            if (laenge > 0)
            {
                var k = _probeBegonnen ? Sprachkennzeichen.Keine : Sprachkennzeichen.Beginn;
                _ = _hoerprobe!.Einreihen(jetztUs, _hpPhase, _hpFrame, k, _probeDaten[i].AsSpan(0, laenge));
                _probeBegonnen = true;
            }
            _hpFrame++;
            _probeGespielt = i + 1;
        }
        if (_probeGespielt >= _probeAufgenommen) WiedergabeBeenden(jetztUs);
    }

    private void WiedergabeBeenden(long jetztUs)
    {
        if (_probeBegonnen) _ = _hoerprobe!.Einreihen(jetztUs, _hpPhase, _hpFrame++, Sprachkennzeichen.Ende, ReadOnlySpan<byte>.Empty);
        _probeBegonnen = false;
        _probeZustand = (int)ProbeZustand.Bereit;
        _probeNachlauf = ProbeNachlaufRahmen;
    }

    private void HoerprobeEinreihen(Sprachkennzeichen k, ReadOnlySpan<byte> daten, long jetztUs)
    {
        if (!_hpOffen)
        {
            // Die Hörprobe setzt erst mit einer neuen Phase ein, nie mitten in einem Satz.
            if (!_hoerprobeAn || (k & Sprachkennzeichen.Beginn) == 0) return;
            _hpOffen = true;
            _hpPhase++;
        }
        _ = _hoerprobe!.Einreihen(jetztUs, _hpPhase, _hpFrame++, k, daten);
        if ((k & Sprachkennzeichen.Ende) != 0) _hpOffen = false;
    }

    /// <summary>Hörprobe mitten in einer Phase abgeschaltet: mit ENDE schließen, damit der Puffer sauber ausblendet.</summary>
    private void HoerprobeSchliessen(long jetztUs)
    {
        _ = _hoerprobe!.Einreihen(jetztUs, _hpPhase, _hpFrame++, Sprachkennzeichen.Ende, ReadOnlySpan<byte>.Empty);
        _hpOffen = false;
    }
}

/// <summary>Was ein Druck der Sprechtaste mit der laufenden Phase macht (<see cref="Sendekette.TasteDruecken"/>, Q66, Prüfung 0.9.3).</summary>
internal enum TastenDruck
{
    /// <summary>Kein Nachlauf offen: eine neue Phase, mit Klick beim Drücken.</summary>
    Neu,

    /// <summary>Im Nachlauf für dasselbe Ziel wieder gedrückt: dieselbe Phase läuft weiter, ohne Klick.</summary>
    Fortgesetzt,

    /// <summary>Im Nachlauf für ein anderes Ziel gedrückt: Die alte Phase endet, eine neue beginnt; Klick beim Loslassen und beim Drücken.</summary>
    Geschnitten,
}
