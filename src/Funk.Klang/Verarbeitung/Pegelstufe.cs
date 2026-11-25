// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Lautstärke der eigenen Stimme vor dem Senden. <paramref name="Automatisch"/>: Halteregel der <see cref="Pegelstufe"/>;
/// sonst gilt fest <paramref name="FestDb"/> (eingemessen). Im Automatikbetrieb ist <paramref name="FestDb"/> der Startwert.
/// </summary>
/// <param name="Gelernt">
/// <paramref name="FestDb"/> ist ein gelernter oder eingemessener Wert (aus einer früheren Sitzung): Die Halteregel setzt dort fort,
/// als hätte sie ihn gerade gelernt, ohne die Anfangsschritte bis 12 dB. Falsch: ein bloßer Startwert, die Halteregel lernt neu.
/// </param>
/// <param name="Eingang">
/// Gerät (Kennung), für das <paramref name="FestDb"/> gelernt wurde; null heißt unbekannt. Öffnet die Klangmaschine ein anderes
/// Mikrofon, lernt die Halteregel neu (<see cref="Pegelstufe.NeuLernen"/>).
/// </param>
/// <param name="Eingemessen">
/// <paramref name="FestDb"/> kommt gerade aus "Mindestlautstärke einmessen" (Klangstufe C1): Er gilt sofort, auch wenn die Halteregel schon
/// gelernt hat (sonst bliebe sie beim jetzigen Wert), und die Halteregel setzt von dort fort.
/// </param>
/// <param name="StimmeDbfs">
/// Die gemessene Funkstimme vor der Pegelstufe in dBFS (Einmessen oder Lernen, mit dem Wert gemerkt): Grundpegel und Anker der
/// Mindest-Funklautstärke. NaN: aus der Verstärkung zurückgerechnet (Ziel minus Verstärkung), aber nur, wenn die Verstärkung nicht an ihrer
/// Grenze steht; sonst gilt die Stimme als unbekannt und wird neu gelernt (Gegenprüfung C1, H1: bei begrenzter Verstärkung lag der Anker sonst
/// um die Begrenzung über der Stimme).
/// </param>
public readonly record struct LautstaerkeEinstellung(bool Automatisch, float FestDb, bool Gelernt = false, string? Eingang = null, bool Eingemessen = false,
    float StimmeDbfs = float.NaN)
{
    /// <summary>NaN wird <see cref="Pegelstufe.StartDb"/>, sonst begrenzt auf <see cref="Pegelstufe.MinDb"/> bis <see cref="Pegelstufe.MaxDb"/>; eine Stimme außerhalb -120 bis 0 dBFS gilt als unbekannt.</summary>
    public LautstaerkeEinstellung Bereinigt() =>
        this with
        {
            FestDb = float.IsNaN(FestDb) ? Pegelstufe.StartDb : Math.Clamp(FestDb, Pegelstufe.MinDb, Pegelstufe.MaxDb),
            StimmeDbfs = StimmeDbfs is >= -120f and <= 0f ? StimmeDbfs : float.NaN,
        };
}

public enum EinmessZustand { Bereit, Laeuft, Fertig, ZuWenigSprache }

/// <summary>Stand des Einmessens; <paramref name="ErgebnisDb"/> gilt nur bei <see cref="EinmessZustand.Fertig"/>.</summary>
public readonly record struct EinmessStand(EinmessZustand Zustand, int RestMs, float ErgebnisDb);

/// <summary>
/// Pegelstufe der Sendekette (Klangstufe A): bringt die eigene Stimme auf eine gleichbleibende Grundlautstärke, ohne sie innerhalb
/// eines Satzes nachzuregeln. Sie sitzt hinter dem APM, vor dem Sicherheitsbegrenzer, RNNoise und dem Gate (Recherche Abschnitt 1);
/// was sie misst, hat das Gate also noch nicht geschnitten. Je 10-ms-Rahmen, ohne Allokation und ohne Sperre.
/// <list type="bullet">
/// <item><b>Fest:</b> eine konstante Verstärkung (<see cref="LautstaerkeEinstellung.FestDb"/>), einmal eingemessen.</item>
/// <item><b>Halteregel (automatisch):</b> Rahmen mit einer Sprachwahrscheinlichkeit ab <see cref="SprachSchwelle"/>, die gesendet
/// werden (Sprechtaste oder offene Sprachaktivierung), zählen zur laufenden Äußerung, ihr Pegel wird über die ganze Äußerung
/// gemittelt (mittlere Leistung). Was nicht hinausgeht, lernt sie nicht: Wer schweigt, während andere im Raum sprechen, verschiebt
/// nichts (Gegenprüfung Klangstufe A: ohne diese Regel stieg die Verstärkung in 220 s um 19 dB, die eigene Stimme kam danach mit
/// -11 dBFS und bis 19 dB Begrenzung). Erst nach <see cref="PauseRahmen"/> Rahmen Pause (Wahrscheinlichkeit unter
/// <see cref="PausenSchwelle"/> und Pegel höchstens <see cref="PausenSpielraumDb"/> über dem leisesten Rahmen der Pause, 600 ms)
/// ist die Äußerung zu Ende; ihr Pegel geht in den Grundpegel ein (laufendes Mittel über bis zu <see cref="GedaechtnisRahmen"/>
/// Rahmen Sprache, also 20 s), und die Verstärkung bewegt sich in der Pause um höchstens einen Schritt auf <see cref="ZielDbfs"/>
/// minus Grundpegel zu: in den ersten <see cref="AnfangRahmen"/> Rahmen Sprache (10 s) um bis zu <see cref="AnfangsSchrittDb"/>,
/// danach um höchstens <see cref="SchrittDb"/>. Der Schritt geschieht linear über einen Rahmen, frühestens im letzten Rahmen der
/// 600 ms und nur, solange nichts gesendet wird: Hält jemand die Sprechtaste über mehrere Sätze, wartet der Schritt bis nach dem
/// Loslassen (sonst hörten die anderen ihn mitten in der Sendung). Eine Rampe über mehrere Rahmen lief im Klangkorpus in den
/// leisen Anlaut der nächsten Äußerung hinein, weil RNNoise einen schwachen Anlaut noch als Pause sieht. Innerhalb einer Äußerung
/// und einer Sendung ändert sich die Verstärkung also nie.</item>
/// <item><b>Abweichler:</b> Nach der Anfangszeit zählt eine Äußerung, die um mehr als <see cref="AbweichungDb"/> vom Grundpegel
/// abweicht, nicht (ein Ruf, ein Lachen, ein anderer im Raum). Erst <see cref="AbweichlerFolge"/> in Folge in dieselbe Richtung
/// gelten als neue Lage (Mikrofon verstellt) und zählen wieder. Eine leisere, deren Abstand zum Rauschen um mehr als
/// <see cref="FremdAbstandDb"/> kleiner ist als der der eigenen Stimme, ist eine fremde Stimme und zählt nie, auch nicht zur Folge:
/// Wird das Mikrofon leiser gestellt, sinken Stimme und Rauschen gemeinsam, eine fremde Stimme liegt dagegen näher am Rauschen.</item>
/// <item><b>Start:</b> Ein gelernter Wert (<see cref="LautstaerkeEinstellung.Gelernt"/>) setzt fort, als wäre die Anfangszeit
/// vorbei; ein bloßer Startwert lernt mit den Anfangsschritten. <see cref="NeuLernen"/> (anderes Mikrofon) beginnt die Anfangszeit
/// von der jetzigen Verstärkung aus neu.</item>
/// <item><b>Einmessen:</b> <see cref="EinmessRahmen"/> Rahmen (8 s) lang die Sprachrahmen sammeln, in denen man normal und einmal
/// deutlich lauter spricht. Normaler Pegel: mittlere Leistung der leiseren 60 % der Sprachrahmen; Spitze: 99-%-Quantil der
/// Rahmenspitzen. Ergebnis: <see cref="ZielDbfs"/> minus normaler Pegel, aber höchstens so viel, dass die Spitze bei
/// <see cref="EinmessSpitzeDbfs"/> ankommt, damit der Begrenzer nur Netz bleibt (Recherche Abschnitt 4, A). Während des Einmessens
/// bleibt die Verstärkung stehen; das Ergebnis gilt erst, wenn es gesetzt wird.</item>
/// </list>
/// Ein Wechsel der festen Verstärkung oder auf Fest greift ab dem nächsten Rahmen und läuft innerhalb dieses Rahmens linear,
/// ohne Sprung. Nicht endliche Werte (Rahmen, Wahrscheinlichkeit, Einstellung) vergiften den Zustand nicht.
/// <para>
/// Threads: <see cref="Verarbeiten(Span{float}, float, bool)"/> nur im Aufnahme-Thread; <see cref="Setzen"/>,
/// <see cref="EinmessenStarten"/>, <see cref="EinmessenAbbrechen"/> und <see cref="NeuLernen"/> aus jedem Thread, wirksam ab dem
/// nächsten Rahmen. Die Stände sind Momentaufnahmen.
/// </para>
/// </summary>
public sealed class Pegelstufe
{
    /// <summary>Grundlautstärke der Sprache, die die Halteregel anstrebt (mittlere Leistung der Sprachrahmen).</summary>
    public const float ZielDbfs = -23f;

    public const float MinDb = -12f;
    public const float MaxDb = 30f;

    /// <summary>Startwert, solange nichts eingemessen ist.</summary>
    public const float StartDb = 6f;

    public const float SprachSchwelle = 0.9f;
    public const float PausenSchwelle = 0.5f;

    /// <summary>
    /// 600 ms Pause beenden eine Äußerung (Recherche: 400 bis 600 ms). Gemessen im Klangkorpus: Mit 500 ms änderte sich die
    /// Verstärkung bei leisem Mikrofon in bis zu sieben von 22 Sätzen in einer langen Kommapause (P_Gain bis 0,8 dB, beim Kaltstart
    /// einmal 9,8 dB), mit 600 ms in keinem.
    /// </summary>
    public const int PauseRahmen = 60;

    /// <summary>Eine Äußerung zählt erst ab 200 ms Sprache.</summary>
    public const int MindestRahmen = 20;

    /// <summary>Ein Pausenrahmen liegt höchstens so weit über dem leisesten Rahmen der Pause; lauter ist er ein Anlaut.</summary>
    public const float PausenSpielraumDb = 10f;

    public const float SchrittDb = 1f;
    public const float AnfangsSchrittDb = 12f;
    public const int AnfangRahmen = 1000;
    public const int GedaechtnisRahmen = 2000;
    public const int EinmessRahmen = 800;
    public const int EinmessMindestRahmen = 150;
    public const float EinmessSpitzeDbfs = -1f;

    /// <summary>Nach der Anfangszeit zählt eine Äußerung, die weiter als so viel vom Grundpegel abweicht, erst in einer Folge.</summary>
    public const float AbweichungDb = 10f;

    /// <summary>So viele Abweichler in Folge (dieselbe Richtung) gelten als neue Lage und zählen.</summary>
    public const int AbweichlerFolge = 3;

    /// <summary>Ein leiserer Abweichler mit so viel weniger Abstand zum Rauschen als die eigene Stimme ist eine fremde Stimme.</summary>
    public const float FremdAbstandDb = 6f;

    /// <summary>Weicht der gelernte Grundpegel mehr als so viel vom Anker ab, folgt der Anker (Gegenprüfung C1, M1).</summary>
    public const float AnkerNachfuehrenDb = 6f;

    /// <summary>
    /// Nach unten folgt der Anker schon ab dieser Abweichung (Nacharbeit M1, Koordination 04.10.2026): Wird die gesendete Funkstimme anhaltend leiser
    /// (der Grundpegel ist ein gleitendes Mittel über die Äußerungen, ein einzelner leiser Satz bewegt ihn kaum), rückt die Mindest-Funklautstärke mit.
    /// </summary>
    public const float AnkerNachUntenDb = 3f;

    private const int Rahmen = Klangformat.Samples10ms;
    private const int BefehlStarten = 1;
    private const int BefehlAbbrechen = 2;

    private sealed class Fach(LautstaerkeEinstellung wert)
    {
        public LautstaerkeEinstellung Wert { get; } = wert;
    }

    private const float EimerUntergrenzeDb = -150f;
    private const int EimerAnzahl = 1600; // -150 bis +10 dB in 0,1-dB-Schritten

    private readonly float[] _einmessPegel = new float[EinmessRahmen];
    private readonly float[] _einmessSpitze = new float[EinmessRahmen];
    private readonly int[] _eimer = new int[EimerAnzahl];

    private Fach _fach;
    private Fach? _gesehen;
    private int _befehl;
    private int _neuLernen;
    private volatile int _einmessZustand;
    private volatile int _einmessRestMs;
    private volatile float _einmessErgebnis;
    private volatile float _verstaerkungAussen;
    private volatile float _grundpegelAussen = float.NaN;
    private volatile bool _stimmeBekannt;
    private volatile float _ankerAussen = float.NaN;
    private volatile float _mindestAbstand = float.PositiveInfinity;
    private bool _aeusserungMitTaste;

    // Nur Aufnahme-Thread:
    private bool _begonnen;
    private float _gain;
    private bool _automatisch;
    private bool _gelernt;
    private double _grundDb;
    private double _abstandDb = double.NaN;
    private long _sprachRahmen;
    private double _aeusserungLeistung;
    private int _aeusserungRahmen;
    private int _pause;
    private float _pauseLeisesterDb;
    private bool _schrittOffen;
    private bool _schrittGross;
    private float _schrittZiel;
    private int _abweichler;
    private bool _einmessen;
    private int _einmessGesehen;
    private int _einmessAnzahl;

    public Pegelstufe(LautstaerkeEinstellung? einstellung = null)
    {
        var e = (einstellung ?? new LautstaerkeEinstellung(true, StartDb)).Bereinigt();
        _fach = new Fach(e);
        _gesehen = _fach;
        _gain = e.FestDb;
        _automatisch = e.Automatisch;
        _verstaerkungAussen = _gain;
        StartwertUebernehmen(e);
    }

    /// <summary>Die zuletzt gesetzte Einstellung (bereinigt).</summary>
    public LautstaerkeEinstellung Einstellung => Volatile.Read(ref _fach).Wert;

    /// <summary>Aus jedem Thread; greift ab dem nächsten Rahmen. Eine feste Verstärkung NaN gilt als die bisherige.</summary>
    public void Setzen(LautstaerkeEinstellung e)
    {
        if (float.IsNaN(e.FestDb)) e = e with { FestDb = Einstellung.FestDb };
        Volatile.Write(ref _fach, new Fach(e.Bereinigt()));
    }

    /// <summary>
    /// Aus jedem Thread: Das Gelernte vergessen (anderes Mikrofon), ab dem nächsten Rahmen wieder mit den Anfangsschritten lernen,
    /// von der jetzigen Verstärkung aus.
    /// </summary>
    public void NeuLernen() => Volatile.Write(ref _neuLernen, 1);

    /// <summary>Verstärkung am Ende des letzten Rahmens in dB.</summary>
    public float VerstaerkungDb => _verstaerkungAussen;

    /// <summary>Grundpegel der Sprache, wie ihn die Halteregel gelernt hat (dBFS, vor der Verstärkung); NaN vor der ersten Äußerung.</summary>
    public float GrundpegelDbfs => _grundpegelAussen;

    /// <summary>
    /// Die Funkstimme gilt als gelernt (Klangstufe C1): erst dann gibt es eine Mindest-Funklautstärke aus ihr. Wahr nach einem gelernten
    /// oder eingemessenen Startwert und, ohne ihn, sobald die Halteregel ihre Anfangszeit hinter sich hat (<see cref="AnfangRahmen"/>
    /// Rahmen gesendeter Sprache).
    /// </summary>
    public bool StimmeBekannt => _stimmeBekannt;

    /// <summary>
    /// Bezug der Mindest-Funklautstärke vor der Pegelstufe in dBFS (Klangstufe C1): der Grundpegel in dem Moment, in dem die Stimme bekannt
    /// wurde (eingemessen, gelernter Startwert oder Ende der Anfangszeit); NaN solange sie unbekannt ist. Er wandert nicht mit der Halteregel:
    /// Spricht jemand lange leise, folgte ein beweglicher Bezug der leisen Stimme, und die Mindest-Funklautstärke sänke mit (gemessen: +8 dB
    /// Verstärkung in zwei Minuten leiser Sprache, MessungKlangC1). Neu gesetzt durch Einmessen, neues Mikrofon oder einen neuen Startwert.
    /// </summary>
    public float AnkerDbfs => _ankerAussen;

    public void EinmessenStarten() => Volatile.Write(ref _befehl, BefehlStarten);

    public void EinmessenAbbrechen() => Volatile.Write(ref _befehl, BefehlAbbrechen);

    /// <summary>Stand des Einmessens; ein noch nicht angenommener Befehl zählt schon.</summary>
    public EinmessStand Einmessen
    {
        get
        {
            switch (Volatile.Read(ref _befehl))
            {
                case BefehlStarten: return new EinmessStand(EinmessZustand.Laeuft, EinmessRahmen * 10, 0f);
                case BefehlAbbrechen: return new EinmessStand(EinmessZustand.Bereit, 0, 0f);
            }
            var zustand = (EinmessZustand)_einmessZustand;
            return new EinmessStand(zustand, zustand == EinmessZustand.Laeuft ? _einmessRestMs : 0, zustand == EinmessZustand.Fertig ? _einmessErgebnis : 0f);
        }
    }

    /// <summary>
    /// Ohne Kette (Tests, Werkzeuge): wie eine Sprachaktivierung, die bei jeder möglichen Sprache offen ist; Sprache und unsichere
    /// Rahmen gelten als gesendet, Pausen nicht. Siehe <see cref="Verarbeiten(Span{float}, float, bool)"/>.
    /// </summary>
    public float Verarbeiten(Span<float> rahmen, float sprachwahrscheinlichkeit) =>
        Verarbeiten(rahmen, sprachwahrscheinlichkeit, sprachwahrscheinlichkeit >= PausenSchwelle);

    /// <summary>
    /// Aufnahme-Thread: einen Rahmen (480 Werte) in-place verstärken. <paramref name="sprachwahrscheinlichkeit"/> gehört zu genau
    /// diesem Signal. <paramref name="gesendet"/>: Die Sendung läuft gerade (Sprechtaste gedrückt oder Sprachaktivierung offen);
    /// nur dann lernt die Halteregel, und nur ohne Sendung macht sie einen Schritt. Rückgabe: Verstärkung am Ende des Rahmens in dB.
    /// </summary>
    public float Verarbeiten(Span<float> rahmen, float sprachwahrscheinlichkeit, bool gesendet) =>
        Verarbeiten(rahmen, sprachwahrscheinlichkeit, gesendet, gesendet);

    /// <summary>
    /// Mindest-Funklautstärke als Abstand unter dem gelernten Grundpegel (Klangstufe C1), plus unendlich heißt keine. Ist die Stimme bekannt,
    /// zählt eine Äußerung, deren mittlerer Pegel weiter darunter liegt, nie (wie eine fremde Stimme), auch nicht in einer Folge; es sei denn,
    /// sie kam über die Sprechtaste. Sonst lernte die Halteregel leises Sprechen zur Familie nach drei Äußerungen als neue Lage, drehte auf,
    /// und die Mindest-Funklautstärke wanderte mit der Stimme nach unten (MessungKlangC1, Nachweis Einmessen). Aus jedem Thread.
    /// </summary>
    public float MindestAbstandDb
    {
        get => _mindestAbstand;
        set => _mindestAbstand = float.IsNaN(value) ? float.PositiveInfinity : value;
    }

    /// <summary>
    /// Wie <see cref="Verarbeiten(Span{float}, float, bool)"/>, aber gelernt wird nur aus freigegebenen Rahmen (Klangstufe C1,
    /// <see cref="Maschine.Lernfreigabe"/>): <paramref name="freigegeben"/> heißt, dieser Rahmen ist eigene Stimme. Ein gesendeter, nicht
    /// freigegebener Rahmen zählt nicht zur Äußerung, hält aber wie jede Sendung den Schritt zurück. <paramref name="sprechtaste"/>: Der
    /// Rahmen kam über die Sprechtaste; eine solche Äußerung gilt auch unter der Mindest-Funklautstärke (<see cref="MindestAbstandDb"/>).
    /// </summary>
    public float Verarbeiten(Span<float> rahmen, float sprachwahrscheinlichkeit, bool gesendet, bool freigegeben, bool sprechtaste = false)
    {
        if (gesendet && freigegeben && sprechtaste) _aeusserungMitTaste = true;
        if (rahmen.Length != Rahmen) throw new ArgumentException("Rahmen muss 480 Werte haben.", nameof(rahmen));
        var alt = _gain;
        EinstellungUebernehmen();
        // Was vor dem ersten Rahmen gesetzt wurde, gilt ohne Rampe (sonst liefe der erste Rahmen vom Startwert her).
        if (!_begonnen) alt = _gain;
        _begonnen = true;
        if (Interlocked.Exchange(ref _neuLernen, 0) != 0) Vergessen();
        BefehlAnnehmen();
        var p = float.IsNaN(sprachwahrscheinlichkeit) ? 0f : sprachwahrscheinlichkeit;
        var leistung = Pegel.Leistung(rahmen);
        var endlich = float.IsFinite(leistung);
        if (_einmessen) Sammeln(rahmen, leistung, endlich, p);
        if (_automatisch && !_einmessen) Halteregel(leistung, endlich, p, gesendet, freigegeben);
        Anwenden(rahmen, alt, _gain);
        _verstaerkungAussen = _gain;
        return _gain;
    }

    private void EinstellungUebernehmen()
    {
        var fach = Volatile.Read(ref _fach);
        if (ReferenceEquals(fach, _gesehen)) return;
        _gesehen = fach;
        _automatisch = fach.Wert.Automatisch;
        // Automatik: Der Wert ist der Startwert und gilt nur, solange noch nichts gelernt ist; sonst weiter vom jetzigen Wert. Ein
        // eingemessener Wert (Klangstufe C1) gilt immer.
        if (_automatisch && _gelernt && !fach.Wert.Eingemessen) return;
        _gain = fach.Wert.FestDb;
        StartwertUebernehmen(fach.Wert);
    }

    /// <summary>
    /// Automatik: ein gelernter oder eingemessener Startwert setzt fort. Fest: Ist der Wert eingemessen oder gelernt, kommt die Stimme mit
    /// ihm am Ziel an und gilt als bekannt; sonst nicht (Klangstufe C1).
    /// </summary>
    private void StartwertUebernehmen(LautstaerkeEinstellung e)
    {
        var stimme = StimmeAus(e);
        var bekannt = (e.Gelernt || e.Eingemessen) && double.IsFinite(stimme);
        if (_automatisch)
        {
            if (bekannt) Fortsetzen(stimme);
            return;
        }
        if (bekannt)
        {
            _grundDb = stimme;
            _grundpegelAussen = (float)_grundDb;
            _ankerAussen = (float)_grundDb;
        }
        _stimmeBekannt = bekannt;
        if (!bekannt) _ankerAussen = float.NaN;
    }

    /// <summary>
    /// Die Funkstimme vor der Pegelstufe zu einem Startwert: gemessen mitgegeben, sonst Ziel minus Verstärkung, aber nicht aus einer Verstärkung an
    /// ihrer Grenze (dann fehlte die Begrenzung im Anker, Gegenprüfung C1, H1); NaN heißt unbekannt.
    /// </summary>
    private double StimmeAus(LautstaerkeEinstellung e)
    {
        if (float.IsFinite(e.StimmeDbfs)) return e.StimmeDbfs;
        return _gain > MinDb + 0.05f && _gain < MaxDb - 0.05f ? ZielDbfs - _gain : double.NaN;
    }

    /// <summary>
    /// Ein gelernter Startwert: Grundpegel aus der gemessenen Stimme (sonst so, dass er gerade die jetzige Verstärkung ergibt), Anfangszeit vorbei,
    /// die Stimme gilt als bekannt. Eine angefangene Äußerung und ein ausstehender Schritt verfallen (sonst holte er einen eingemessenen Wert gleich
    /// wieder zurück).
    /// </summary>
    private void Fortsetzen(double stimme)
    {
        _gelernt = true;
        _grundDb = stimme;
        _sprachRahmen = AnfangRahmen + 1;
        _abstandDb = double.NaN;
        _abweichler = 0;
        _schrittGross = false;
        _schrittOffen = false;
        _aeusserungLeistung = 0;
        _aeusserungRahmen = 0;
        _grundpegelAussen = (float)_grundDb;
        _ankerAussen = (float)_grundDb;
        _stimmeBekannt = true;
    }

    private void Vergessen()
    {
        _stimmeBekannt = false;
        _ankerAussen = float.NaN;
        _gelernt = false;
        _sprachRahmen = 0;
        _abstandDb = double.NaN;
        _abweichler = 0;
        _aeusserungLeistung = 0;
        _aeusserungRahmen = 0;
        _schrittOffen = false;
        _schrittGross = false;
        _grundpegelAussen = float.NaN;
    }

    private void BefehlAnnehmen()
    {
        var befehl = Volatile.Read(ref _befehl);
        if (befehl == 0) return;
        _einmessen = befehl == BefehlStarten;
        _einmessGesehen = 0;
        _einmessAnzahl = 0;
        if (_einmessen) _einmessRestMs = EinmessRahmen * 10;
        _einmessZustand = (int)(_einmessen ? EinmessZustand.Laeuft : EinmessZustand.Bereit);
        // Erst nach dem Zustand löschen, damit ein Leser nie einen Zwischenstand sieht; ein neuerer Befehl bleibt stehen.
        _ = Interlocked.CompareExchange(ref _befehl, 0, befehl);
    }

    private void Halteregel(float leistung, bool endlich, float p, bool gesendet, bool freigegeben)
    {
        if (!endlich)
        {
            _pause = 0;
            return;
        }
        var db = Pegel.LeistungZuDb(leistung);
        if (p >= PausenSchwelle && p < SprachSchwelle)
        {
            // Unsicher: weder Sprache noch Pause. In einer Sendung, oder so laut wie die eigene Stimme, unterbricht es die Pause;
            // sonst (andere im Raum, Taste los) hält es sie nur an, damit die Regel neben Gesprächen im Raum überhaupt schreitet.
            if (gesendet || EigeneStimme(db)) _pause = 0;
            return;
        }
        if (p >= SprachSchwelle)
        {
            // Nur was hinausgeht, zählt zur Äußerung. Sprache ohne Sendung unterbricht die Pause nur, wenn sie so laut ist wie die
            // eigene Stimme (etwa ein Satzteil, bei dem die Sprachaktivierung zu war); leiser hält sie die Pause nur an. Ein
            // Schritt fällt nie in einen solchen Rahmen, nur in leise Pausenrahmen.
            if (!gesendet)
            {
                if (EigeneStimme(db)) _pause = 0;
                return;
            }
            // Gesendet, aber nicht eigene Stimme (Echo, unter der Mindest-Funklautstärke; Klangstufe C1): zählt nicht, ist aber Sendung.
            if (!freigegeben)
            {
                _pause = 0;
                return;
            }
            _aeusserungLeistung += leistung;
            _aeusserungRahmen++;
            _pause = 0;
            return;
        }
        // Ein Rahmen deutlich über dem leisesten der Pause ist ein Anlaut, auch wenn die Wahrscheinlichkeit (einen Rahmen alt)
        // noch Pause sagt.
        if (_pause == 0 || db < _pauseLeisesterDb) _pauseLeisesterDb = db;
        if (db > _pauseLeisesterDb + PausenSpielraumDb)
        {
            _pause = 0;
            return;
        }
        if (_pause < PauseRahmen && ++_pause == PauseRahmen) AeusserungAbschliessen();
        if (_pause >= PauseRahmen && _schrittOffen && !gesendet) Schritt();
    }

    /// <summary>Ein Rahmen so laut wie die eigene Stimme (höchstens <see cref="AbweichungDb"/> unter dem Grundpegel); vor dem ersten Lernen jeder.</summary>
    private bool EigeneStimme(float db) => !_gelernt || db > _grundDb - AbweichungDb;

    private void AeusserungAbschliessen()
    {
        var n = _aeusserungRahmen;
        var summe = _aeusserungLeistung;
        var mitTaste = _aeusserungMitTaste;
        _aeusserungRahmen = 0;
        _aeusserungLeistung = 0;
        _aeusserungMitTaste = false;
        if (n < MindestRahmen || !(summe > 0)) return;
        var pegel = 10.0 * Math.Log10(summe / n);
        // Klangstufe C1: Leiser als die Mindest-Funklautstärke ist nicht die Funkstimme (Sprechtaste ausgenommen).
        if (_stimmeBekannt && _gelernt && !mitTaste && pegel < _ankerAussen - _mindestAbstand) return;
        // Abstand zum Rauschen: der leiseste Rahmen dieser Pause (Stille aus dem Gerät gibt keinen Abstand).
        var abstand = _pauseLeisesterDb > Pegel.MinDbfs ? pegel - _pauseLeisesterDb : double.NaN;
        if (!_gelernt)
        {
            _grundDb = pegel;
            _abstandDb = abstand;
            _gelernt = true;
        }
        else
        {
            if (_sprachRahmen > AnfangRahmen && !Zaehlt(pegel, abstand)) return;
            var gewicht = _sprachRahmen < GedaechtnisRahmen ? (double)n / (_sprachRahmen + n) : Math.Min(1.0, (double)n / GedaechtnisRahmen);
            _grundDb += gewicht * (pegel - _grundDb);
            if (double.IsNaN(_abstandDb)) _abstandDb = abstand;
            else if (!double.IsNaN(abstand)) _abstandDb += gewicht * (abstand - _abstandDb);
        }
        _sprachRahmen += n;
        _grundpegelAussen = (float)_grundDb;
        if (_sprachRahmen >= AnfangRahmen && !_stimmeBekannt)
        {
            _ankerAussen = (float)_grundDb;
            _stimmeBekannt = true;
        }
        // Gegenprüfung C1, M1: Hat die Halteregel eine deutlich andere Lage gelernt (Mikrofon weiter weg, Pegel in Windows), folgt der Anker, sonst
        // liefe die Mindest-Funklautstärke an der Stimme vorbei. Leises Reden (ab dem Abstand unter dem Anker) lernt sie ohnehin nicht.
        else if (_stimmeBekannt && (_grundDb > _ankerAussen + AnkerNachfuehrenDb || _grundDb < _ankerAussen - AnkerNachUntenDb))
        {
            _ankerAussen = (float)_grundDb;
        }
        _schrittZiel = (float)Math.Clamp(ZielDbfs - _grundDb, MinDb, MaxDb);
        _schrittOffen = true;
        // Fiel eine Äußerung in die Anfangszeit, darf der nächste Schritt groß sein, auch wenn er bis nach einer langen Sendung
        // wartet und die Anfangszeit inzwischen vorbei ist.
        _schrittGross |= _sprachRahmen <= AnfangRahmen;
    }

    /// <summary>Nach der Anfangszeit: Zählt diese Äußerung (keine Abweichung, oder die dritte in Folge, aber keine fremde Stimme)?</summary>
    private bool Zaehlt(double pegel, double abstand)
    {
        var abweichung = pegel - _grundDb;
        if (Math.Abs(abweichung) <= AbweichungDb)
        {
            _abweichler = 0;
            return true;
        }
        // Leiser und viel näher am Rauschen als die eigene Stimme: eine fremde Stimme; sie zählt weder, noch unterbricht sie eine Folge.
        if (abweichung < 0 && !double.IsNaN(abstand) && !double.IsNaN(_abstandDb) && abstand < _abstandDb - FremdAbstandDb) return false;
        var richtung = abweichung > 0 ? 1 : -1;
        _abweichler = Math.Sign(_abweichler) == richtung ? _abweichler + richtung : richtung;
        return Math.Abs(_abweichler) >= AbweichlerFolge;
    }

    private void Schritt()
    {
        _schrittOffen = false;
        var schritt = _schrittGross ? AnfangsSchrittDb : SchrittDb;
        _schrittGross = false;
        _gain += Math.Clamp(_schrittZiel - _gain, -schritt, schritt);
    }

    private void Sammeln(ReadOnlySpan<float> rahmen, float leistung, bool endlich, float p)
    {
        if (endlich && p >= SprachSchwelle && leistung > 0f)
        {
            _einmessPegel[_einmessAnzahl] = Pegel.LeistungZuDb(leistung);
            _einmessSpitze[_einmessAnzahl] = Pegel.LinearZuDb(Pegel.Spitze(rahmen));
            _einmessAnzahl++;
        }
        _einmessGesehen++;
        _einmessRestMs = (EinmessRahmen - _einmessGesehen) * 10;
        if (_einmessGesehen < EinmessRahmen) return;
        _einmessen = false;
        if (_einmessAnzahl < EinmessMindestRahmen)
        {
            _einmessZustand = (int)EinmessZustand.ZuWenigSprache;
            return;
        }
        var pegel = _einmessPegel.AsSpan(0, _einmessAnzahl);
        var grenze = Quantil(pegel, 0.6);
        double summe = 0;
        var anzahl = 0;
        foreach (var db in pegel)
        {
            if (db > grenze) continue;
            summe += Math.Pow(10.0, db / 10.0);
            anzahl++;
        }
        var normalDb = 10.0 * Math.Log10(summe / anzahl);
        var spitzeDb = Quantil(_einmessSpitze.AsSpan(0, _einmessAnzahl), 0.99);
        var ergebnis = Math.Min(ZielDbfs - normalDb, EinmessSpitzeDbfs - spitzeDb);
        _einmessErgebnis = (float)Math.Round(Math.Clamp(ergebnis, MinDb, MaxDb), 1);
        _einmessZustand = (int)EinmessZustand.Fertig;
    }

    /// <summary>
    /// Quantil von Pegeln in dB auf 0,1 dB genau, über die vorab angelegten Eimer (ohne Sortieren: Das Sortieren eines Spans legt
    /// beim ersten Mal einen Vergleicher an, und das geschähe im Audio-Thread). Die Oberkante des Eimers, der das Quantil enthält.
    /// </summary>
    private float Quantil(ReadOnlySpan<float> werte, double q)
    {
        Array.Clear(_eimer);
        foreach (var db in werte) _eimer[Eimer(db)]++;
        var ziel = Math.Max(1, (int)Math.Ceiling(q * werte.Length));
        var summe = 0;
        for (var i = 0; i < _eimer.Length; i++)
        {
            summe += _eimer[i];
            if (summe >= ziel) return EimerUntergrenzeDb + (i + 1) * 0.1f;
        }
        return EimerUntergrenzeDb + _eimer.Length * 0.1f;
    }

    private static int Eimer(float db) => Math.Clamp((int)MathF.Floor((db - EimerUntergrenzeDb) * 10f), 0, EimerAnzahl - 1);

    /// <summary>Verstärkung anwenden, bei einer Änderung linear über den Rahmen.</summary>
    private static void Anwenden(Span<float> rahmen, float vonDb, float nachDb)
    {
        var nach = Pegel.DbZuLinear(nachDb);
        if (vonDb == nachDb)
        {
            for (var i = 0; i < rahmen.Length; i++) rahmen[i] *= nach;
            return;
        }
        var von = Pegel.DbZuLinear(vonDb);
        for (var i = 0; i < rahmen.Length; i++) rahmen[i] *= von + (nach - von) * (i + 1) / rahmen.Length;
    }
}
