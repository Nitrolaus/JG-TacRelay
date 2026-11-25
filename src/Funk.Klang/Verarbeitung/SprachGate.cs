// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

public enum GateZustand
{
    Leise,
    Anlauf,
    Senden,
    Nachlauf,

    /// <summary>
    /// Sprache bestätigt, aber noch nicht laut genug für die Mindest-Funklautstärke (<see cref="SprachGate.MindestDbfs"/>, Klangstufe C1):
    /// nichts geht hinaus; wird sie im Fenster laut genug, öffnet das Gate mit dem Vorlauf, endet sie vorher, war es nichts.
    /// </summary>
    Zurueckgehalten,
}

/// <summary>Kennzeichen eines Rahmens, den das Gate zum Senden herausgibt.</summary>
[Flags]
public enum SenderahmenArt
{
    Keine = 0,

    /// <summary>Erster Rahmen einer Sprechphase.</summary>
    Beginn = 0x01,

    /// <summary>Rahmen aus dem Vorlauf (liegt vor dem Rahmen, in dem das Gate öffnete).</summary>
    Vorlauf = 0x02,

    /// <summary>Letzter Rahmen einer Sprechphase (ausgeblendet).</summary>
    Letzter = 0x04,

    /// <summary>Die Phase kommt aus der Sprachaktivierung, nicht aus der Sprechtaste.</summary>
    Sprachaktivierung = 0x08,
}

/// <summary>Startwerte und Grenzen des Gates; Zahlen aus audio-dsp.md, Empfehlung 6.</summary>
public sealed class GateEinstellungen
{
    public float AbstandStartDb { get; init; } = 9f;
    public float AbstandMinDb { get; init; } = 6f;
    public float AbstandMaxDb { get; init; } = 20f;

    /// <summary>Aufschlag auf Median plus dreifache Streuung von E minus N in Nicht-Sprache.</summary>
    public float AbstandReserveDb { get; init; } = 3f;

    /// <summary>Nicht-Sprache zählt erst ab diesem Abstand zum Boden für die Wahrscheinlichkeitsschwelle (hörbare Geräusche).</summary>
    public float SchwelleStatistikAbDb { get; init; } = 3f;

    /// <summary>So viel weniger Abstand reicht, um offen zu bleiben.</summary>
    public float AbstandHystereseDb { get; init; } = 3f;

    public float SchwelleStart { get; init; } = 0.5f;
    public float SchwelleMin { get; init; } = 0.4f;
    public float SchwelleMax { get; init; } = 0.8f;

    /// <summary>Aufschlag auf das 95-%-Quantil der Sprachwahrscheinlichkeit in Nicht-Sprache (theta_on).</summary>
    public float SchwelleOffset { get; init; } = 0.15f;

    /// <summary>theta_off = theta_on minus diesen Wert (Silero-Hysterese).</summary>
    public float SchwelleHysterese { get; init; } = 0.15f;

    /// <summary>So viele Rahmen müssen innerhalb des Fensters die Anlaufbedingung erfüllen (erster eingeschlossen).</summary>
    public int BestaetigungRahmen { get; init; } = 4;

    public int BestaetigungFensterRahmen { get; init; } = 10;

    /// <summary>Vorlauf beim Anlegen; danach über <see cref="SprachGate.VorlaufMs"/> einstellbar (Klangstufe C1).</summary>
    public int VorlaufMs { get; init; } = 30;

    /// <summary>
    /// Höchstens so viele schon vergangene Rahmen gehen beim Öffnen als Burst hinaus (Vorlauf plus Anlauf). Bei 15 sind das 7 volle
    /// Pakete aus der Vergangenheit, die das Kennzeichen PREROLL tragen, und ein halbes im ersten Paket, das mit dem laufenden Rahmen
    /// schließt und damit in Echtzeit hinausgeht (<see cref="Sendeaufbereitung"/>). FUNKPROTOKOLL-v1 5.6 erlaubt höchstens 7 PREROLL-Pakete
    /// (140 ms) je Phase; der Sender wirft beim achten (<c>SprechphasenGeber.Audio</c>). Ältere Rahmen fallen vorn weg.
    /// </summary>
    public int BurstHoechstRahmen { get; init; } = 15;

    /// <summary>
    /// Fenster der Freigabe (Klangstufe C1): Mit einer Mindest-Funklautstärke öffnet die Sprachaktivierung erst, wenn die mittlere Leistung
    /// der Sprachrahmen in den letzten so vielen Rahmen (ab dem Anlauf) über ihr liegt. 15 Rahmen sind 150 ms: Bis dahin reicht der Burst
    /// noch an den Anfang des Anlaufs zurück.
    /// </summary>
    public int FreigabeFensterRahmen { get; init; } = 15;

    /// <summary>
    /// Beginnt Sprache so bald nach dem letzten gesendeten Rahmen (300 ms nach dem Nachlauf, also bis 600 ms Pause), ist sie die Fortsetzung
    /// desselben Spruchs nach einer Pause (Komma,
    /// Atem): Sie geht ohne die Prüfung gegen die Mindest-Funklautstärke hinaus. Gemessen (MessungKlangC1): ohne sie verlor ein Spruch nach
    /// einer Pause über 300 ms (Nachlauf) leise Wortanfänge mitten im Satz.
    /// </summary>
    public int FortsetzungRahmen { get; init; } = 30;

    public int RingMs { get; init; } = 300;
    public int NachlaufMs { get; init; } = 300;
    public int RauschbodenFensterMs { get; init; } = 2000;

    /// <summary>Gedächtnis der Nicht-Sprach-Statistik (Zeitkonstante).</summary>
    public int StatistikMs { get; init; } = 60000;

    /// <summary>So viele Rahmen Nicht-Sprache braucht die Selbstjustierung, bevor sie die Startwerte ablöst.</summary>
    public int StatistikMindestRahmen { get; init; } = 100;

    /// <summary>
    /// So lange ab dem ersten Rahmen lernt die Selbstjustierung nichts. RNNoise schwingt am Anfang ein und hält das Rauschen, das
    /// es dabei durchlässt, für halbe Sprache (in der Kette gemessen: p 0,5 bis 0,68 in den Rahmen 8 bis 24 bei -65 dBFS Rauschen
    /// und 6 bis 8 dB Verstärkung davor). Weil hörbare Nicht-Sprache selten ist, hob schon das die Schwelle für die ganze Sitzung.
    /// </summary>
    public int EinschwingMs { get; init; } = 500;

    /// <summary>Wirkung des Empfindlichkeitsreglers (-1 .. +1) auf den Abstand je Stufe.</summary>
    public float EmpfindlichkeitDb { get; init; } = 6f;

    /// <summary>Wirkung des Empfindlichkeitsreglers auf die Wahrscheinlichkeitsschwelle je Stufe.</summary>
    public float EmpfindlichkeitSchwelle { get; init; } = 0.1f;
}

/// <summary>
/// Sprach-Gate als Zustandsmaschine LEISE, ANLAUF, SENDEN, NACHLAUF, je 10-ms-Rahmen.
/// <list type="bullet">
/// <item>Pegel E in dBFS, um die AGC-Verstärkung bereinigt (Mumble); Rauschboden N nach Minimum-Statistik
/// über 2 s, nachgeführt nur außerhalb von SENDEN.</item>
/// <item>ANLAUF, wenn E über N plus Abstand liegt und die Sprachwahrscheinlichkeit die Schwelle erreicht.
/// SENDEN erst, wenn das in 4 von höchstens 10 Rahmen gilt; sonst zählt der Anlauf als abgewiesen
/// (Klicks, Tüte). Beim Öffnen geht der Vorlauf (<see cref="VorlaufMs"/>, über die Führung ab Werk 120 ms) vor dem Anlauf aus dem
/// 300-ms-Ring als Burst hinaus, höchstens <see cref="GateEinstellungen.BurstHoechstRahmen"/> vergangene Rahmen.</item>
/// <item>NACHLAUF 300 ms; kehrt die Sprache zurück, geht es ohne neuen Vorlauf zurück nach SENDEN.
/// Der letzte Rahmen wird über 10 ms ausgeblendet.</item>
/// <item>Selbstjustierung aus der Nicht-Sprache der letzten 60 s (Pausen und abgewiesene Anläufe, ohne die ersten 500 ms):
/// Schwelle = 95-%-Quantil der Wahrscheinlichkeit in hörbarer Nicht-Sprache (mindestens 3 dB über dem Boden)
/// plus 0,15, begrenzt auf 0,4 bis 0,8. Abstand aus der Streuung von E minus N: Median plus dreimal
/// (Median minus 16-%-Quantil) plus 3 dB, begrenzt auf 6 bis 20 dB. Die untere Streuung ist unempfindlich gegen
/// Klicks; die stillen Rahmen verdünnen die Schwelle nicht. Der Empfindlichkeitsregler verschiebt nur die Offsets.</item>
/// <item>Die Sprechtaste umgeht das Gate: senden, solange sie gedrückt ist, ohne Vorlauf.</item>
/// </list>
/// <see cref="Verarbeiten"/> läuft im Aufnahme-Thread und allokiert nicht. Nach jedem Aufruf die Rahmen mit
/// <see cref="Holen"/> abholen; beim nächsten Aufruf sind nicht abgeholte verloren.
/// </summary>
public sealed class SprachGate
{
    private const int Rahmen = Klangformat.Samples10ms;
    private const int Einblendlaenge = 120;
    private const int MaxAusgabe = 32;

    private readonly GateEinstellungen _e;
    private readonly float[] _ring;
    private readonly int _ringRahmen;
    private int _vorlaufRahmen;
    private int _freigabeFenster;
    private readonly int _hoechsterVorlaufRahmen;
    private readonly int _nachlaufRahmen;
    private readonly long _einschwingRahmen;
    private readonly Rauschboden _boden;
    private readonly Verteilung _pStatistik;
    private readonly Verteilung _abstandStatistik;
    private readonly float[] _anlaufP;
    private readonly float[] _anlaufAbstand;
    private readonly float[] _pegelJeRahmen;
    private readonly float[] _vorPegelJeRahmen;
    private readonly float[] _pJeRahmen;

    private readonly int[] _ausSlot = new int[MaxAusgabe];
    private readonly long[] _ausNummer = new long[MaxAusgabe];
    private readonly SenderahmenArt[] _ausArt = new SenderahmenArt[MaxAusgabe];
    private int _ausAnzahl;
    private int _ausLese;

    private long _nummer;
    private long _letzteAusgabe = -1;
    private GateZustand _zustand;
    private bool _taste;
    private long _anlaufStart;
    private int _anlaufRahmen;
    private int _bestaetigt;
    private int _nachlaufRest;
    private volatile float _empfindlichkeit;
    private volatile bool _sprachaktivierungAn = true;

    public SprachGate(GateEinstellungen? einstellungen = null)
    {
        _e = einstellungen ?? new GateEinstellungen();
        _ringRahmen = Math.Max(1, _e.RingMs / 10);
        _vorlaufRahmen = Math.Max(0, _e.VorlaufMs / 10);
        _nachlaufRahmen = Math.Max(1, _e.NachlaufMs / 10);
        _einschwingRahmen = Math.Max(0, _e.EinschwingMs / 10);
        if (_vorlaufRahmen + _e.BestaetigungFensterRahmen + 1 > _ringRahmen || _e.BestaetigungFensterRahmen + _vorlaufRahmen + 1 > MaxAusgabe)
            throw new ArgumentException("Ring zu kurz für Vorlauf plus Bestätigungsfenster.", nameof(einstellungen));
        if (_e.BestaetigungRahmen < 1 || _e.BestaetigungRahmen > _e.BestaetigungFensterRahmen)
            throw new ArgumentException("1 <= Bestätigung <= Fenster.", nameof(einstellungen));
        if (_e.BurstHoechstRahmen < 1) throw new ArgumentException("Der Burst braucht mindestens einen Rahmen.", nameof(einstellungen));
        _hoechsterVorlaufRahmen = Math.Min(_ringRahmen, MaxAusgabe) - _e.BestaetigungFensterRahmen - 1;
        _ring = new float[_ringRahmen * Rahmen];
        _boden = new Rauschboden(_e.RauschbodenFensterMs / 10);
        var vergessen = 1.0 - 1.0 / Math.Max(1, _e.StatistikMs / 10);
        _pStatistik = new Verteilung(0f, 1f, 50, vergessen);
        _abstandStatistik = new Verteilung(-10f, 40f, 100, vergessen);
        _anlaufP = new float[_e.BestaetigungFensterRahmen];
        _anlaufAbstand = new float[_e.BestaetigungFensterRahmen];
        _pegelJeRahmen = new float[_ringRahmen];
        _vorPegelJeRahmen = new float[_ringRahmen];
        _pJeRahmen = new float[_ringRahmen];
        _freigabeFenster = Math.Clamp(_e.FreigabeFensterRahmen, 1, _ringRahmen);
    }

    public GateZustand Zustand => _zustand;

    /// <summary>
    /// Vorlauf in ms (Klangstufe C1): so viel vor dem ersten Rahmen des Anlaufs geht beim Öffnen mit hinaus. Nur im Aufnahme-Thread
    /// setzen; wird auf ganze 10 ms und auf das begrenzt, was Ring und Ausgabe fassen. Greift beim nächsten Öffnen.
    /// </summary>
    public int VorlaufMs
    {
        get => _vorlaufRahmen * 10;
        set => _vorlaufRahmen = Math.Clamp(value / 10, 0, _hoechsterVorlaufRahmen);
    }

    public bool TasteGedrueckt => _taste;

    /// <summary>
    /// Mindest-Funklautstärke in dBFS (Klangstufe C1), minus unendlich heißt keine. Eine Phase der Sprachaktivierung öffnet erst, wenn die
    /// mittlere Leistung der Sprachrahmen in den letzten <see cref="GateEinstellungen.FreigabeFensterRahmen"/> Rahmen seit dem Anlauf darüber
    /// liegt; bis dahin hält das Gate sie zurück (<see cref="GateZustand.Zurueckgehalten"/>), danach geht der Vorlauf mit hinaus. Eine offene
    /// Phase bleibt offen, solange Sprache über der Linie liegt, auch wenn sie leiser wird (kein Wort wird mitten im Satz abgeschnitten).
    /// Die Sprechtaste kennt keine Mindest-Funklautstärke. Nur im Aufnahme-Thread setzen.
    /// </summary>
    public float MindestDbfs { get; set; } = float.NegativeInfinity;

    /// <summary>
    /// So weit muss die mittlere Leistung im Fenster über der Mindest-Funklautstärke liegen (Klangstufe C1, ab Werk 4 dB). Die
    /// Mindest-Funklautstärke ist wie beim Einmessen ein mittlerer Pegel ganzer Sätze; die lautesten 150 ms eines Satzes liegen einige dB
    /// darüber. Gemessen (MessungKlangC1, Nachweis Einmessen: normal und 12 dB leiser eingemessen, Korpus normal und 12 dB leiser, drei
    /// Rauschmuster), Wortanfänge normaler Sätze angeschnitten / Wortanfänge der leisen Sprache, die hinausgingen, je von 492: Mit der Linie
    /// des ersten Stands (leise plus ein Drittel des Unterschieds, 9 dB unter der Stimme) Zuschlag 4 dB 45 / 129, 6 dB 63 / 33, 8 dB 69 / 0
    /// (Fenster 150 ms); kürzere Fenster (50, 80 ms) trennten schlechter. Milde Einstellung (klang-c1b, Raster Messung_KlangC1_Raster, Linie mal
    /// Zuschlag 8 / 4 / 0 dB): Linie leise minus 3 dB (15 dB unter der Stimme) mit 4 dB Zuschlag schneidet 9 normale Anfänge an (vorher 69) und
    /// lässt 408 der 492 leisen hinaus; unter allen Kombinationen mit höchstens 10 angeschnittenen die mit den wenigsten hinausgehenden leisen.
    /// Nur im Aufnahme-Thread.
    /// </summary>
    public float FreigabeZuschlagDb { get; set; } = 4f;

    /// <summary>Fenster der Freigabe in Rahmen (<see cref="GateEinstellungen.FreigabeFensterRahmen"/>); nur im Aufnahme-Thread ändern.</summary>
    public int FreigabeFensterRahmen
    {
        get => _freigabeFenster;
        set => _freigabeFenster = Math.Clamp(value, 1, _ringRahmen);
    }

    /// <summary>Phasen, die nie laut genug für die Mindest-Funklautstärke wurden und darum nicht hinausgingen.</summary>
    public long ZurueckgehalteneAnlaeufe { get; private set; }

    /// <summary>Nutzerregler -1 (unempfindlich) bis +1 (empfindlich); verschiebt nur die Offsets. NaN gilt als 0.</summary>
    public float Empfindlichkeit
    {
        get => _empfindlichkeit;
        set => _empfindlichkeit = float.IsNaN(value) ? 0f : Math.Clamp(value, -1f, 1f);
    }

    /// <summary>
    /// Aus: Nur die Sprechtaste sendet (Netz steht auf Taste). Eine offene Phase der Sprachaktivierung endet
    /// beim nächsten Rahmen mit einem ausgeblendeten letzten Rahmen.
    /// </summary>
    public bool SprachaktivierungAn
    {
        get => _sprachaktivierungAn;
        set => _sprachaktivierungAn = value;
    }

    public float RauschbodenDbfs => _boden.WertDb;

    public float LetzterPegelDbfs { get; private set; } = Pegel.MinDbfs;

    /// <summary>Abstand aus der Selbstjustierung, ohne Empfindlichkeit.</summary>
    public float AbstandAutoDb
    {
        get
        {
            if (_abstandStatistik.Anzahl < _e.StatistikMindestRahmen) return _e.AbstandStartDb;
            var median = _abstandStatistik.Quantil(0.5);
            var streuung = median - _abstandStatistik.Quantil(0.16);
            return Math.Clamp(median + 3f * streuung + _e.AbstandReserveDb, _e.AbstandMinDb, _e.AbstandMaxDb);
        }
    }

    /// <summary>Wahrscheinlichkeitsschwelle aus der Selbstjustierung, ohne Empfindlichkeit.</summary>
    public float SchwelleAuto =>
        _pStatistik.Anzahl < _e.StatistikMindestRahmen
            ? _e.SchwelleStart
            : Math.Clamp(_pStatistik.Quantil(0.95) + _e.SchwelleOffset, _e.SchwelleMin, _e.SchwelleMax);

    public float AbstandWirksamDb => AbstandAutoDb - _empfindlichkeit * _e.EmpfindlichkeitDb;

    public float SchwelleWirksam => Math.Clamp(SchwelleAuto - _empfindlichkeit * _e.EmpfindlichkeitSchwelle, 0.05f, 0.95f);

    /// <summary>Anläufe, die nicht bestätigt wurden (Klicks, Tüte, Klopfen).</summary>
    public long AbgewieseneAnlaeufe { get; private set; }

    /// <summary>Öffnungen durch die Sprachaktivierung.</summary>
    public long Ausloesungen { get; private set; }

    /// <summary>Laufende Nummer des nächsten Rahmens (0 = erster Rahmen überhaupt).</summary>
    public long Rahmennummer => _nummer;

    /// <summary>
    /// Einen 10-ms-Rahmen verarbeiten. <paramref name="sprachwahrscheinlichkeit"/> von RNNoise/Silero (0..1),
    /// <paramref name="agcVerstaerkungDb"/> die aktuelle AGC-Verstärkung. <paramref name="pegelDbfs"/>: der Pegel des Rahmens, gemessen vor einer
    /// Stufe, die den Rahmen danach noch verändert hat (seit 0.9.3 die Klangfarbe); ohne Angabe misst das Gate ihn selbst aus dem Rahmen.
    /// Gesendet wird in jedem Fall der Rahmen selbst. Rückgabe: Zahl der Rahmen zum Senden.
    /// </summary>
    public int Verarbeiten(ReadOnlySpan<float> rahmen10ms, float sprachwahrscheinlichkeit, float agcVerstaerkungDb = 0f, bool sprechtaste = false,
        float? pegelDbfs = null)
    {
        if (rahmen10ms.Length != Rahmen) throw new ArgumentException("Rahmen muss 480 Samples haben.", nameof(rahmen10ms));
        _ausAnzahl = 0;
        _ausLese = 0;
        var nr = _nummer++;
        rahmen10ms.CopyTo(_ring.AsSpan(Slot(nr) * Rahmen, Rahmen));

        var pegel = (pegelDbfs ?? Pegel.Dbfs(rahmen10ms)) - agcVerstaerkungDb;
        // Ein Rahmen mit NaN oder Unendlich (oder ein solcher AGC-Wert) ist kaputt: Er zählt nie als Sprache
        // und geht in keine Statistik; leistung ist dann nicht endlich und wird vom Rauschboden übergangen.
        var leistung = MathF.Pow(10f, pegel / 10f);
        var p = float.IsNaN(sprachwahrscheinlichkeit) ? 0f : Math.Clamp(sprachwahrscheinlichkeit, 0f, 1f);
        if (!_boden.HatWert) _boden.Nachfuehren(leistung);
        var ueber = float.IsFinite(pegel) ? pegel - _boden.WertDb : float.NaN;
        LetzterPegelDbfs = pegel;
        _pegelJeRahmen[Slot(nr)] = pegel;
        _pJeRahmen[Slot(nr)] = p;
        _vorPegelJeRahmen[Slot(nr)] = float.IsFinite(VorfilterDbfs) ? MathF.Max(VorfilterDbfs, pegel) : pegel;

        if (sprechtaste || _taste)
        {
            TasteVerarbeiten(sprechtaste, nr);
            return _ausAnzahl;
        }
        if (!_sprachaktivierungAn)
        {
            // Abgeschaltet, während eine Phase offen ist: mit ausgeblendetem letzten Rahmen schließen, damit
            // die Sendeseite sofort ENDE schickt und kein halbes Paket bis zur nächsten Phase liegen bleibt.
            if (_zustand is GateZustand.Senden or GateZustand.Nachlauf)
                Ausgeben(nr, SenderahmenArt.Sprachaktivierung | SenderahmenArt.Letzter);
            _zustand = GateZustand.Leise;
            _boden.Nachfuehren(leistung);
            return _ausAnzahl;
        }

        var abstandEin = AbstandWirksamDb;
        var abstandAus = abstandEin - _e.AbstandHystereseDb;
        var schwelleEin = SchwelleWirksam;
        var schwelleAus = MathF.Max(0.01f, schwelleEin - _e.SchwelleHysterese);
        var aktiv = ueber > abstandAus && p >= schwelleAus;

        // Klangstufe C2a, Nacharbeit: Während der Raum-Anhebung öffnet ein Sprachrahmen auf Funkniveau sofort, mit dem ganzen Vorpuffer (Vorlauf
        // bis zur Burst-Grenze, auch vor den Anlauf zurück, den die angehobene Linie verzögert hätte). Die Anhebung sperrt nur leises Niveau.
        // Erreicht eine schon offene Phase Funkniveau, gilt die Anhebung für ihren Rest ebenso nicht.
        if (_zustand == GateZustand.Leise) FunkniveauPhase = false;
        // Nicht an der Linie gemessen: Die angehobene Linie liegt über dem, was RNNoise vom Wortanfang übrig lässt.
        if (p >= schwelleEin && AufFunkniveau(nr, schwelleAus))
        {
            FunkniveauPhase = true;
            if (_zustand is GateZustand.Leise or GateZustand.Anlauf or GateZustand.Zurueckgehalten)
            {
                if (_zustand == GateZustand.Leise) _anlaufStart = nr;
                _anlaufStart = Math.Min(_anlaufStart, nr - _e.BurstHoechstRahmen + _vorlaufRahmen);
                Oeffnen(nr);
                return _ausAnzahl;
            }
        }

        switch (_zustand)
        {
            case GateZustand.Leise:
                _boden.Nachfuehren(leistung);
                if (ueber > abstandEin && p >= schwelleEin)
                {
                    _zustand = GateZustand.Anlauf;
                    _anlaufStart = nr;
                    _anlaufRahmen = 0;
                    _bestaetigt = 0;
                    AnlaufRahmen(p, ueber, true);
                }
                else
                {
                    NichtSprache(p, ueber, nr);
                }
                break;

            case GateZustand.Anlauf:
                _boden.Nachfuehren(leistung);
                AnlaufRahmen(p, ueber, ueber > abstandAus && p >= schwelleEin);
                if (_bestaetigt >= _e.BestaetigungRahmen)
                {
                    if (LautGenug(nr, schwelleAus))
                    {
                        Oeffnen(nr);
                    }
                    else
                    {
                        // Sprache, aber zu leise für die Mindest-Funklautstärke: zurückhalten, solange sie anhält.
                        _zustand = GateZustand.Zurueckgehalten;
                        _nachlaufRest = _nachlaufRahmen;
                    }
                }
                else if (_anlaufRahmen >= _e.BestaetigungFensterRahmen)
                {
                    _zustand = GateZustand.Leise;
                    AbgewieseneAnlaeufe++;
                    for (var i = 0; i < _anlaufRahmen; i++) NichtSprache(_anlaufP[i], _anlaufAbstand[i], _anlaufStart + i);
                }
                break;

            case GateZustand.Senden:
                if (aktiv)
                {
                    Ausgeben(nr, SenderahmenArt.Sprachaktivierung);
                }
                else
                {
                    _zustand = GateZustand.Nachlauf;
                    _nachlaufRest = _nachlaufRahmen - 1; // dieser Rahmen ist der erste des Nachlaufs
                    Ausgeben(nr, SenderahmenArt.Sprachaktivierung);
                }
                break;

            case GateZustand.Nachlauf:
                _boden.Nachfuehren(leistung);
                if (aktiv)
                {
                    _zustand = GateZustand.Senden;
                    Ausgeben(nr, SenderahmenArt.Sprachaktivierung);
                }
                else if (--_nachlaufRest <= 0)
                {
                    _zustand = GateZustand.Leise;
                    Ausgeben(nr, SenderahmenArt.Sprachaktivierung | SenderahmenArt.Letzter);
                }
                else
                {
                    Ausgeben(nr, SenderahmenArt.Sprachaktivierung);
                }
                break;

            case GateZustand.Zurueckgehalten:
                // Der Boden bleibt stehen (es ist Sprache); die Selbstjustierung sieht diese Rahmen nicht.
                if (aktiv)
                {
                    _nachlaufRest = _nachlaufRahmen;
                    if (LautGenug(nr, schwelleAus)) Oeffnen(nr);
                }
                else if (--_nachlaufRest <= 0)
                {
                    _zustand = GateZustand.Leise;
                    ZurueckgehalteneAnlaeufe++;
                }
                break;
        }
        return _ausAnzahl;
    }

    /// <summary>
    /// Ist die Phase laut genug für die Mindest-Funklautstärke? Mittlere Leistung der Sprachrahmen (Wahrscheinlichkeit ab
    /// <paramref name="schwelle"/>) in den letzten <see cref="GateEinstellungen.FreigabeFensterRahmen"/> Rahmen, nicht vor dem Anlauf. Ohne
    /// Mindest-Funklautstärke immer.
    /// </summary>
    private bool LautGenug(long nr, float schwelle)
    {
        if (!(MindestDbfs > float.NegativeInfinity)) return true;
        if (_letzteAusgabe >= 0 && _anlaufStart - _letzteAusgabe <= _e.FortsetzungRahmen) return true;
        var von = Math.Max(_anlaufStart, Math.Max(nr - FreigabeFensterRahmen + 1, nr - _ringRahmen + 1));
        double summe = 0;
        var anzahl = 0;
        for (var r = von; r <= nr; r++)
        {
            var i = Slot(r);
            if (!(_pJeRahmen[i] >= schwelle) || !float.IsFinite(_pegelJeRahmen[i])) continue;
            summe += Math.Pow(10.0, _pegelJeRahmen[i] / 10.0);
            anzahl++;
        }
        return anzahl > 0 && 10.0 * Math.Log10(summe / anzahl) >= MindestDbfs + FreigabeZuschlagDb;
    }

    /// <summary>
    /// Mikro stumm (Klangstufe C2a): Anlauf, Phase und Taste vergessen, ohne einen Rahmen herauszugeben; Rauschboden und Selbstjustierung bleiben.
    /// Die Sendeaufbereitung schließt ihre offene Phase selbst (<see cref="Sendeaufbereitung.Abbrechen"/>).
    /// </summary>
    public void Schliessen()
    {
        _zustand = GateZustand.Leise;
        _taste = false;
        _ausAnzahl = 0;
        _ausLese = 0;
    }

    /// <summary>Nächsten Senderahmen kopieren (480 Samples). Falsch, wenn keiner mehr ansteht.</summary>
    public bool Holen(Span<float> ziel, out SenderahmenArt art, out long nummer)
    {
        if (_ausLese >= _ausAnzahl)
        {
            art = SenderahmenArt.Keine;
            nummer = -1;
            return false;
        }
        var i = _ausLese++;
        art = _ausArt[i];
        nummer = _ausNummer[i];
        var x = ziel[..Rahmen];
        _ring.AsSpan(_ausSlot[i] * Rahmen, Rahmen).CopyTo(x);
        if ((art & SenderahmenArt.Beginn) != 0)
        {
            for (var j = 0; j < Einblendlaenge; j++) x[j] *= 0.5f * (1f - MathF.Cos(MathF.PI * j / Einblendlaenge));
        }
        if ((art & SenderahmenArt.Letzter) != 0)
        {
            for (var j = 0; j < Rahmen; j++) x[j] *= 0.5f * (1f + MathF.Cos(MathF.PI * (j + 1) / Rahmen));
        }
        return true;
    }

    private void TasteVerarbeiten(bool gedrueckt, long nr)
    {
        if (gedrueckt && !_taste)
        {
            // Taste schlägt Sprachaktivierung: ein laufender Anlauf verfällt, eine laufende VA-Phase
            // endet mit dem Beginn der Tastenphase.
            _taste = true;
            _zustand = GateZustand.Senden;
            Ausgeben(nr, SenderahmenArt.Beginn);
        }
        else if (gedrueckt)
        {
            Ausgeben(nr, SenderahmenArt.Keine);
        }
        else
        {
            _taste = false;
            _zustand = GateZustand.Leise;
            Ausgeben(nr, SenderahmenArt.Letzter);
        }
    }

    private void AnlaufRahmen(float p, float ueber, bool erfuellt)
    {
        _anlaufP[_anlaufRahmen] = p;
        _anlaufAbstand[_anlaufRahmen] = ueber;
        _anlaufRahmen++;
        if (erfuellt) _bestaetigt++;
    }

    private void Oeffnen(long nr)
    {
        _zustand = GateZustand.Senden;
        Ausloesungen++;
        // Der Vorlauf greift nie hinter den zuletzt gesendeten Rahmen zurück: Beginnt die Sprache direkt nach
        // dem Ende einer Phase (Nachlauf aus, Taste losgelassen), würden sonst bis zu 30 ms doppelt gesendet.
        // Und höchstens BurstHoechstRahmen vergangene Rahmen (Protokoll: höchstens 7 PREROLL-Pakete); bei langsamer
        // Bestätigung fällt dafür der Anfang des Vorlaufs weg.
        var erster = Math.Max(Math.Max(0, _letzteAusgabe + 1), Math.Max(_anlaufStart - _vorlaufRahmen, nr - _ringRahmen + 1));
        erster = Math.Max(Math.Max(erster, nr - _e.BurstHoechstRahmen), _vorlaufAb);
        for (var r = erster; r <= nr; r++)
        {
            var art = SenderahmenArt.Sprachaktivierung;
            if (r == erster) art |= SenderahmenArt.Beginn;
            if (r < nr) art |= SenderahmenArt.Vorlauf;
            Ausgeben(r, art);
        }
    }

    /// <summary>
    /// Dieser Rahmen geht nicht in die Selbstjustierung von Abstand und Sprachschwelle (Klangstufe C2a): ohne Echoreferenz erkanntes Echo, das der
    /// Echoschutz gerade sperrt; sonst höbe es den Abstand bis 20 dB über den Boden, und kurze eigene Sprüche fänden die Linie spät. Nur Aufnahme-Thread.
    /// </summary>
    public bool OhneStatistik { get; set; }

    /// <summary>
    /// Funkniveau in dBFS (Klangstufe C2a, Nacharbeit 04.10.2026): Ein Sprachrahmen ab diesem Pegel öffnet sofort, ohne Bestätigung und ohne
    /// Mindest-Funklautstärke, mit dem ganzen Vorpuffer. Plus unendlich heißt keine Regel. Die GateFuehrung setzt es nur, solange die Raum-Anhebung
    /// des Wächters gilt: Sie darf leises Niveau sperren, nie den Anlauf eines Funkspruchs verzögern. Nur Aufnahme-Thread.
    /// </summary>
    public float FunkniveauDbfs { get; set; } = float.PositiveInfinity;

    /// <summary>
    /// Mikro stumm gelöst (Gegenprüfung C2a, M2): Ring, Pegel und Wahrscheinlichkeiten vergessen, der Vorlauf greift nie vor diesen Rahmen zurück,
    /// und die erste Phase danach ist keine Fortsetzung. Sonst gingen beim nächsten Öffnen bis zu 150 ms von vor dem Stummschalten hinaus.
    /// Nur Aufnahme-Thread.
    /// </summary>
    public void VorpufferLeeren()
    {
        Array.Clear(_ring);
        Array.Clear(_pJeRahmen);
        Array.Fill(_pegelJeRahmen, float.NegativeInfinity);
        Array.Fill(_vorPegelJeRahmen, float.NegativeInfinity);
        _letzteAusgabe = -1;
        _vorlaufAb = _nummer;
    }

    private long _vorlaufAb;

    /// <summary>Die offene Phase hat ein Rahmen auf Funkniveau geöffnet; für sie gilt die Raum-Anhebung nicht (GateFuehrung).</summary>
    public bool FunkniveauPhase { get; private set; }

    /// <summary>
    /// Funkniveau zählt über die letzten 50 ms (mittlere Leistung der Sprachrahmen, mindestens drei): Einzelne Rahmen leiser Rede erreichen es
    /// an betonten Silben (Prüfstand: 4 leise Phasen hinaus statt 0), 50 ms kaum; der Vorpuffer reicht 150 ms zurück, das Warten kostet nichts.
    /// </summary>
    public const int FunkniveauFensterRahmen = 5;

    /// <summary>
    /// Pegel desselben Rahmens vor dem Rauschfilter in dBFS (die Sendekette richtet ihn auf die Verzögerung des Filters aus); NaN: der Pegel des
    /// Rahmens. Gilt nur für das Funkniveau: Nach leiser Rede dämpft RNNoise den Anfang eines Wortes 300 ms lang um 15 bis 28 dB, obwohl es ihn
    /// als Sprache erkennt (Prüfstand, "Rechts!" während der Raum-Anhebung: davor -19 dBFS, dahinter -33 bis -49). Nur Aufnahme-Thread.
    /// </summary>
    public float VorfilterDbfs { get; set; } = float.NaN;

    private bool AufFunkniveau(long nr, float schwelle)
    {
        if (!(FunkniveauDbfs < float.PositiveInfinity)) return false;
        double summe = 0;
        var anzahl = 0;
        for (var r = Math.Max(0, nr - FunkniveauFensterRahmen + 1); r <= nr; r++)
        {
            var i = Slot(r);
            if (!(_pJeRahmen[i] >= schwelle) || !float.IsFinite(_vorPegelJeRahmen[i])) continue;
            summe += Math.Pow(10.0, _vorPegelJeRahmen[i] / 10.0);
            anzahl++;
        }
        return anzahl >= 3 && 10.0 * Math.Log10(summe / anzahl) >= FunkniveauDbfs;
    }

    /// <summary>Rahmen <paramref name="nr"/> war keine Sprache: in die Selbstjustierung, außer beim Einschwingen.</summary>
    private void NichtSprache(float p, float ueber, long nr)
    {
        if (!float.IsFinite(ueber) || nr < _einschwingRahmen || OhneStatistik) return;
        _abstandStatistik.Hinzufuegen(ueber);
        if (ueber > _e.SchwelleStatistikAbDb) _pStatistik.Hinzufuegen(p);
    }

    private void Ausgeben(long nr, SenderahmenArt art)
    {
        if (_ausAnzahl >= MaxAusgabe) return;
        _ausSlot[_ausAnzahl] = Slot(nr);
        _ausNummer[_ausAnzahl] = nr;
        _ausArt[_ausAnzahl] = art;
        _ausAnzahl++;
        _letzteAusgabe = nr;
    }

    private int Slot(long nr) => (int)(nr % _ringRahmen);
}
