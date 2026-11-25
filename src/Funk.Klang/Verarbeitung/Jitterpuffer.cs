// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Klang.Kern;

namespace Funk.Klang.Verarbeitung;

/// <summary>Einstellungen des Jitterpuffers; Zahlen aus codec-ausgabe.md (B19, Empfehlung 4) und FUNKPROTOKOLL-v1 (C6).</summary>
public sealed class JitterpufferEinstellungen
{
    /// <summary>Zielverzögerung, solange es noch keine Statistik gibt (NetEQ kStartDelayMs).</summary>
    public int StartMs { get; init; } = 80;

    public int MinMs { get; init; } = 20;

    /// <summary>Untergrenze, wenn FEC erwartet wird: Das Folgepaket muss beim Abspielen schon da sein.</summary>
    public int MinMitFecMs { get; init; } = 40;

    public int MaxMs { get; init; } = 200;

    public bool FecErwartet { get; init; } = true;

    public double Quantil { get; init; } = 0.95;

    public double Vergessensfaktor { get; init; } = 0.983;

    public double StartVergessensgewicht { get; init; } = 2;

    /// <summary>Intervall, dessen Höchstwert ins Histogramm geht (NetEQ resample_interval_ms). 0 = jedes Paket.</summary>
    public int AuffrischIntervallMs { get; init; } = 500;

    /// <summary>Verlustverdeckung höchstens so lange am Stück, die letzten 20 ms davon ausgeblendet (C6).</summary>
    public int MaxVerdeckungMs { get; init; } = 60;

    /// <summary>Phase endet so lange nach dem Leerlaufen, wenn kein ENDE kam (C6).</summary>
    public int EndeNachLeerMs { get; init; } = 300;

    /// <summary>Beginnt binnen dieser Zeit nach einem ENDE eine neue Phase, entfallen Ende- und Beginn-Klick (C4, C6).</summary>
    public int FortsetzungMs { get; init; } = 100;

    /// <summary>Rahmen unter diesem Pegel gelten als leise und dürfen frei gekürzt oder gedehnt werden.</summary>
    public float LeiseDbfs { get; init; } = -45f;

    /// <summary>Plätze im Übergabering Netz-Thread zu Audio-Thread (Zweierpotenz).</summary>
    public int EingangPlaetze { get; init; } = 64;
}

/// <summary>Momentaufnahme der Zähler. Von anderen Threads lesbar, ohne Gleichzeitigkeitsgarantie zwischen den Feldern.</summary>
public readonly record struct JitterpufferStatistik(
    long Empfangen,
    long SpaetVerworfen,
    long AltVerworfen,
    long Doppelt,
    long Ueberlauf,
    long EingangVoll,
    long Normal,
    long Fec,
    long Plc,
    long Unterlaeufe,
    long UnterlaufSamples,
    long StilleSamples,
    long GekuerztSamples,
    long GedehntSamples,
    long Phasen,
    double ZielMs,
    double ZielBerechnetMs,
    double FuellstandMs,
    double TiefeMs,
    double JitterMs,
    uint ZuletztGespielterFrame,
    long ErzeugteFrames);

/// <summary>
/// Adaptiver Jitterpuffer je Sprecher nach dem Prinzip von WebRTC NetEQ.
/// <para>
/// Zielverzögerung: 20 ms plus 95-%-Quantil der relativen Verzögerung (<see cref="Laufzeitstatistik"/>),
/// begrenzt auf Minimum (20 ms, mit FEC 40 ms) und Maximum (200 ms). Übernommen wird das Ziel am Anfang
/// jeder Sprechphase; während der Phase nur, wenn es um mehr als 20 ms steigt.
/// </para>
/// <para>
/// Verlustkette je 20-ms-Rahmen: Paket da, dann dekodieren; fehlt es, FEC aus dem Folgepaket, falls vorhanden;
/// sonst Verdecken (Deep PLC). Ist der Puffer ganz leer, wird in 10-ms-Schritten verdeckt, ohne den
/// Abspielpunkt vorzurücken (Unterlauf, die Verzögerung wächst). Verdeckung höchstens 60 ms am Stück,
/// die letzten 20 ms davon ausgeblendet, danach Stille bis zum nächsten echten Rahmen (eingeblendet).
/// Jeder hörbare Abschnitt setzt an der Verstärkung an, mit der der vorige endete (auch FEC oder PLC nach
/// Stille und ein echter Rahmen mitten in der Ausblendung). Pakete hinter dem Abspielpunkt werden verworfen,
/// Sprachpakete ohne Opus beginnen keine Phase. Liefert der Dekodierer eine unmögliche Länge (0, negativ,
/// größer als der Puffer), gilt das als Fehler und wird verdeckt, statt im Audio-Thread zu werfen.
/// </para>
/// <para>
/// Geregelt wird die Puffertiefe: Abholzeitpunkt minus erwartete Ankunft des nächsten Frames bei schnellster
/// Laufzeit der letzten 2 s, plus 20 ms für den Frame selbst. Das ist dieselbe Größe, aus deren Verteilung das
/// Ziel entsteht; der bloße Füllstand (Frames bis zum höchsten empfangenen) wäre bei Jitter zu klein, weil der
/// am weitesten vorauslaufende Frame meist noch unterwegs ist. Die Wiedergabe beginnt, sobald die Tiefe das
/// Ziel erreicht. Liegt sie mehr als 15 ms darüber (Pre-roll-Burst, nach einem Unterlauf), wird um jeweils
/// eine Grundperiode gekürzt, leise Rahmen bis zur Hälfte, bis sie höchstens 5 ms über dem Ziel liegt; liegt
/// sie mehr als 15 ms darunter, wird entsprechend gedehnt. Das Phasenende (ENDE) wird über 5 ms ausgeblendet.
/// </para>
/// <para>
/// Threads: <see cref="Einreihen"/> gehört genau einem Netz-Thread, <see cref="Lesen"/> genau einem
/// Audio-Thread. Übergabe über einen Ring ohne Sperre; nach dem Konstruktor wird nichts allokiert.
/// Ankunftsstempel und Uhr müssen dieselbe monotone Zeitbasis haben (Standard <see cref="MonotonUs"/>).
/// Der Dekodierer gehört dem Aufrufer und wird nur aus <see cref="Lesen"/> heraus benutzt.
/// </para>
/// </summary>
public sealed class Jitterpuffer : IKlangquelle
{
    private const int Plaetze = 64;
    private const int MaxPaketBytes = 1275;
    private const int FrameMs = 20;
    private const int FrameSamples = Klangformat.Samples20ms;
    private const int UnterlaufSamplesJeSchritt = Klangformat.Samples10ms;
    private const int Einblendlaenge = 120; // 2,5 ms
    private const int Ausblendlaenge = 240; // 5 ms
    private const int MaxFrameSprung = 3000; // 60 s, FUNKPROTOKOLL-v1 7.3
    private const double KuerzenAbMs = 15;
    private const double KuerzenBisMs = 5;

    private enum Zustand
    {
        Leer,
        Fuellen,
        Spielen,
    }

    private enum Art
    {
        Normal,
        Fec,
        Plc,
        Unterlauf,
    }

    private enum Endegrund
    {
        Ende,
        Zeitablauf,
        Abgeloest,
    }

    private readonly IDekodierer _dekodierer;
    private readonly JitterpufferEinstellungen _e;
    private readonly PaketRing _eingang;
    private readonly Laufzeitstatistik _statistik;
    private readonly Func<long> _uhrUs;
    private long _jetztUs;

    private readonly byte[][] _platzDaten = new byte[Plaetze][];
    private readonly int[] _platzLaenge = new int[Plaetze];
    private readonly uint[] _platzFrame = new uint[Plaetze];
    private readonly bool[] _platzBelegt = new bool[Plaetze];

    private readonly float[] _roh = new float[Klangformat.MaxSamplesJePaket];
    private readonly float[] _ausgabe = new float[Klangformat.MaxSamplesJePaket + Zeitdehnung.MaxVersatz];
    private int _ausgabeLese;
    private int _ausgabeAnzahl;

    private Zustand _zustand;
    private ushort _phase;
    private bool _phaseVa;
    private bool _beginnKlick;
    private bool _fortsetzung;
    private bool _naechsteIstFortsetzung;
    private uint _abspielFrame;
    private uint _maxFrame;
    private bool _hatPakete;
    private bool _endeBekannt;
    private uint _endeFrame;
    private int _gewartetSamples;
    private int _verdecktInFolge;
    private bool _imUnterlauf;
    private int _leerSamples;
    private float _endVerstaerkung; // Verstärkung am Ende des zuletzt erzeugten Abschnitts, 0 = Stille
    private bool _kuerzen;
    private bool _dehnen;
    private int _dehnPause;
    private double _zielAngewendet;
    private volatile int _maxZielMs;

    private bool _hatLetzte;
    private ushort _letztePhase;
    private bool _letzteDurchEnde;
    private bool _letzteVa;
    private int _endeKlickRest;

    private long _empfangen;
    private long _spaet;
    private long _alt;
    private long _doppelt;
    private long _ueberlauf;
    private long _eingangVoll;
    private long _normal;
    private long _fec;
    private long _plc;
    private long _unterlaeufe;
    private long _unterlaufSamples;
    private long _stilleSamples;
    private long _gekuerzt;
    private long _gedehnt;
    private long _phasen;
    private uint _zuletztGespielt;
    private long _erzeugteFrames;

    /// <param name="dekodierer">Opus-Dekodierer dieses Sprechers; gehört dem Aufrufer.</param>
    /// <param name="einstellungen">Grenzen und Zeiten, sonst die Standardwerte.</param>
    /// <param name="uhrUs">Monotone Uhr in Mikrosekunden, dieselbe wie für die Ankunftsstempel. Standard <see cref="MonotonUs"/>.</param>
    public Jitterpuffer(IDekodierer dekodierer, JitterpufferEinstellungen? einstellungen = null, Func<long>? uhrUs = null)
    {
        ArgumentNullException.ThrowIfNull(dekodierer);
        _dekodierer = dekodierer;
        _uhrUs = uhrUs ?? MonotonUs;
        _e = einstellungen ?? new JitterpufferEinstellungen();
        if (_e.MinMs < FrameMs || _e.MinMitFecMs < _e.MinMs || _e.MaxMs < _e.MinMitFecMs)
            throw new ArgumentException("Grenzen müssen 20 <= Min <= MinMitFec <= Max erfüllen.", nameof(einstellungen));
        // Negiert formuliert, damit NaN durchfällt.
        if (!(_e.Quantil is > 0 and <= 1) || !(_e.Vergessensfaktor is >= 0 and < 1) || !(_e.StartVergessensgewicht >= 0)
            || double.IsInfinity(_e.StartVergessensgewicht) || _e.AuffrischIntervallMs < 0 || _e.MaxVerdeckungMs < 0
            || _e.EndeNachLeerMs < 1 || _e.FortsetzungMs < 0 || _e.StartMs < 0 || float.IsNaN(_e.LeiseDbfs))
            throw new ArgumentException("Quantil (0, 1], Vergessensfaktor [0, 1), Zeiten nicht negativ, Leise-Schwelle eine Zahl.", nameof(einstellungen));
        _eingang = new PaketRing(_e.EingangPlaetze, MaxPaketBytes);
        _statistik = new Laufzeitstatistik(_e.Quantil, _e.Vergessensfaktor, _e.StartVergessensgewicht, _e.AuffrischIntervallMs);
        for (var i = 0; i < Plaetze; i++) _platzDaten[i] = new byte[MaxPaketBytes];
        _maxZielMs = _e.MaxMs;
        _zielAngewendet = ZielBerechnet();
    }

    /// <summary>Obergrenze der Zielverzögerung, zur Laufzeit änderbar (etwa gedeckelt im TCP-Tunnel).</summary>
    public int MaxZielMs
    {
        get => _maxZielMs;
        set => _maxZielMs = Math.Max(value, _e.FecErwartet ? _e.MinMitFecMs : _e.MinMs);
    }

    public JitterpufferStatistik Statistik => new(
        _empfangen, _spaet, _alt, _doppelt, _ueberlauf, Volatile.Read(ref _eingangVoll), _normal, _fec, _plc,
        _unterlaeufe, _unterlaufSamples, _stilleSamples, _gekuerzt, _gedehnt, _phasen,
        _zielAngewendet, ZielBerechnet(), Fuellstand(), TiefeMs(), _statistik.JitterMs, _zuletztGespielt, _erzeugteFrames);

    /// <summary>Monotone Zeit in Mikrosekunden (Stopwatch). Mit ihr stempelt der Netz-Thread die Ankunft.</summary>
    public static long MonotonUs() => (long)(Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency));

    /// <summary>
    /// Netz-Thread: ein Sprachpaket übergeben. <paramref name="ankunftUs"/> ist der Empfangsstempel derselben
    /// monotonen Uhr in Mikrosekunden, die der Puffer benutzt (Standard <see cref="MonotonUs"/>). ENDE-Pakete
    /// kommen ohne Opus. Falsch, wenn der Übergabering voll ist oder das Paket zu groß.
    /// </summary>
    public bool Einreihen(long ankunftUs, ushort sprechphase, uint frameNr, Sprachkennzeichen kennzeichen, ReadOnlySpan<byte> opus)
    {
        if (_eingang.Schreiben(ankunftUs, sprechphase, frameNr, kennzeichen, opus)) return true;
        Interlocked.Increment(ref _eingangVoll);
        return false;
    }

    /// <summary>Audio-Thread: den nächsten Block holen. Füllt <paramref name="ziel"/> ganz, Stille als 0.</summary>
    public QuellenEreignis Lesen(Span<float> ziel)
    {
        var ereignis = QuellenEreignis.Keine;
        _jetztUs = _uhrUs();
        EingangAbarbeiten(ref ereignis);

        var geschrieben = 0;
        while (geschrieben < ziel.Length)
        {
            if (_ausgabeAnzahl == 0 && !RahmenErzeugen(ref ereignis)) break;
            var n = Math.Min(_ausgabeAnzahl, ziel.Length - geschrieben);
            _ausgabe.AsSpan(_ausgabeLese, n).CopyTo(ziel[geschrieben..]);
            _ausgabeLese += n;
            _ausgabeAnzahl -= n;
            geschrieben += n;
        }
        if (geschrieben < ziel.Length)
        {
            ziel[geschrieben..].Clear();
            if (_zustand == Zustand.Fuellen) _gewartetSamples += ziel.Length - geschrieben;
        }

        if (_endeKlickRest > 0)
        {
            _endeKlickRest -= ziel.Length;
            if (_endeKlickRest <= 0)
            {
                _endeKlickRest = 0;
                ereignis |= QuellenEreignis.KlickLoslassen;
                if (_letzteVa) ereignis |= QuellenEreignis.Sprachaktivierung;
            }
        }
        if (_zustand != Zustand.Leer) ereignis |= QuellenEreignis.Aktiv;
        return ereignis;
    }

    /// <summary>Audio-Thread: alles verwerfen (etwa beim Wechsel des Ausgabegeräts). Statistik bleibt.</summary>
    public void Zuruecksetzen()
    {
        _eingang.Leeren();
        PlaetzeLeeren();
        _ausgabeAnzahl = 0;
        _zustand = Zustand.Leer;
        _endeKlickRest = 0;
        _hatLetzte = false;
        _statistik.NeuePhase();
    }

    private void EingangAbarbeiten(ref QuellenEreignis ereignis)
    {
        while (_eingang.Spaehen(out var i))
        {
            var phase = _eingang.Phase(i);
            var kennzeichen = _eingang.Kennzeichen(i);
            if (_zustand == Zustand.Leer)
            {
                if (_hatLetzte)
                {
                    var abstand = (short)(phase - _letztePhase);
                    if (abstand < 0 || (abstand == 0 && _letzteDurchEnde))
                    {
                        _alt++;
                        _eingang.Freigeben();
                        continue;
                    }
                }
                if ((kennzeichen & Sprachkennzeichen.Ende) != 0)
                {
                    // Einzelnes ENDE einer unbekannten Phase: nur merken, damit Nachzügler verworfen werden.
                    _hatLetzte = true;
                    _letztePhase = phase;
                    _letzteDurchEnde = true;
                    _eingang.Freigeben();
                    continue;
                }
                if (_eingang.Daten(i).IsEmpty)
                {
                    // Sprachpaket ohne Opus: beginnt keine Phase. Sonst bliebe der Puffer ohne abspielbares Paket
                    // für immer im Füllen und meldete Aktiv (etwa eine Durchsage-Absenkung ohne Ende).
                    _alt++;
                    _eingang.Freigeben();
                    continue;
                }
                PhaseBeginnen(phase, _eingang.Frame(i), kennzeichen, _hatLetzte && phase == _letztePhase);
            }
            else if (phase != _phase)
            {
                if ((short)(phase - _phase) < 0)
                {
                    _alt++;
                    _eingang.Freigeben();
                    continue;
                }
                if (HatNochDaten()) break; // neue Phase wartet, bis die laufende ausgespielt ist
                var grund = _endeBekannt ? Endegrund.Ende
                    : _imUnterlauf && _leerSamples > _e.FortsetzungMs * Klangformat.SamplesJeMs ? Endegrund.Zeitablauf
                    : Endegrund.Abgeloest;
                PhaseBeenden(grund, ref ereignis);
                continue; // dasselbe Paket beginnt jetzt die neue Phase
            }
            else if ((int)(_eingang.Frame(i) - _abspielFrame) > MaxFrameSprung)
            {
                // Sprung um mehr als 60 s innerhalb der Phase (FUNKPROTOKOLL-v1 7.3): neue Zeitlinie derselben
                // Phase, ohne Klick. Wie bei einer neuen Phase läuft die alte vorher aus.
                if (HatNochDaten()) break;
                PhaseBeenden(Endegrund.Abgeloest, ref ereignis);
                _letzteDurchEnde = false;
                continue;
            }
            PaketAufnehmen(i);
            _eingang.Freigeben();
        }
    }

    private void PaketAufnehmen(int i)
    {
        var frame = _eingang.Frame(i);
        var kennzeichen = _eingang.Kennzeichen(i);
        _empfangen++;
        if ((kennzeichen & Sprachkennzeichen.Ende) != 0)
        {
            // ENDE kommt dreifach mit fortlaufender Frame-Nr; das früheste zählt, auch wenn es später eintrifft.
            if (!_endeBekannt || (int)(frame - _endeFrame) < 0)
            {
                _endeBekannt = true;
                _endeFrame = frame;
            }
            return;
        }
        var daten = _eingang.Daten(i);
        if (daten.IsEmpty || (_endeBekannt && (int)(frame - _endeFrame) >= 0))
        {
            _alt++;
            return;
        }

        var abstand = (int)(frame - _abspielFrame);
        if (_zustand == Zustand.Fuellen && abstand < 0 && (int)(_maxFrame - frame) < Plaetze)
        {
            _abspielFrame = frame; // Umordnung vor dem Start: früherer Frame beginnt die Zeitlinie
            abstand = 0;
        }
        if (abstand < 0)
        {
            _spaet++;
            return;
        }
        if (abstand >= Plaetze)
        {
            _ueberlauf++;
            return;
        }

        var p = (int)(frame % Plaetze);
        if (_platzBelegt[p] && _platzFrame[p] == frame)
        {
            _doppelt++;
            return;
        }
        daten.CopyTo(_platzDaten[p]);
        _platzLaenge[p] = daten.Length;
        _platzFrame[p] = frame;
        _platzBelegt[p] = true;
        if (!_hatPakete || (int)(frame - _maxFrame) > 0) _maxFrame = frame;
        _hatPakete = true;
        if (_zustand == Zustand.Fuellen && (kennzeichen & Sprachkennzeichen.Beginn) != 0 && !_fortsetzung) _beginnKlick = true;
        if ((kennzeichen & Sprachkennzeichen.Preroll) == 0) _statistik.Paket(_eingang.AnkunftUs(i), frame);
    }

    private void PhaseBeginnen(ushort phase, uint frame, Sprachkennzeichen kennzeichen, bool fortsetzung)
    {
        if (_endeKlickRest > 0 || _naechsteIstFortsetzung)
        {
            fortsetzung = true; // C4: Ende- und Beginn-Klick entfallen
            _endeKlickRest = 0;
        }
        _naechsteIstFortsetzung = false;
        _statistik.NeuePhase();
        PlaetzeLeeren();
        _zustand = Zustand.Fuellen;
        _phase = phase;
        _phaseVa = (kennzeichen & Sprachkennzeichen.Sprachaktivierung) != 0;
        _fortsetzung = fortsetzung;
        _beginnKlick = false;
        _abspielFrame = frame;
        _maxFrame = frame;
        _hatPakete = false;
        _endeBekannt = false;
        _gewartetSamples = 0;
        _verdecktInFolge = 0;
        _imUnterlauf = false;
        _leerSamples = 0;
        _endVerstaerkung = 0f;
        _kuerzen = false;
        _dehnen = false;
        _dehnPause = 0;
        _zielAngewendet = ZielBerechnet();
        _phasen++;
    }

    private void PhaseBeenden(Endegrund grund, ref QuellenEreignis ereignis)
    {
        _statistik.NeuePhase();
        PlaetzeLeeren();
        _zustand = Zustand.Leer;
        _hatLetzte = true;
        _letztePhase = _phase;
        _letzteDurchEnde = grund == Endegrund.Ende;
        _letzteVa = _phaseVa;
        ereignis |= QuellenEreignis.Beendet;
        switch (grund)
        {
            case Endegrund.Ende:
                _endeKlickRest = Math.Max(1, _e.FortsetzungMs * Klangformat.SamplesJeMs);
                break;
            case Endegrund.Zeitablauf:
                ereignis |= QuellenEreignis.KlickLoslassen;
                if (_phaseVa) ereignis |= QuellenEreignis.Sprachaktivierung;
                break;
            case Endegrund.Abgeloest:
                _naechsteIstFortsetzung = true;
                break;
        }
    }

    private bool RahmenErzeugen(ref QuellenEreignis ereignis)
    {
        if (_zustand == Zustand.Fuellen)
        {
            if (!Startbereit()) return false;
            _zustand = Zustand.Spielen;
            _endVerstaerkung = 0f;
            ereignis |= QuellenEreignis.Begonnen;
            if (_beginnKlick)
            {
                ereignis |= QuellenEreignis.KlickDruck;
                if (_phaseVa) ereignis |= QuellenEreignis.Sprachaktivierung;
            }
        }
        if (_zustand != Zustand.Spielen) return false;

        var berechnet = ZielBerechnet();
        if (berechnet > _zielAngewendet + FrameMs) _zielAngewendet = berechnet;

        var n = _abspielFrame;
        if (_endeBekannt && (int)(n - _endeFrame) >= 0)
        {
            PhaseBeenden(Endegrund.Ende, ref ereignis);
            return false;
        }

        if (Fuellstand() > _maxZielMs + 200)
        {
            // Notbremse: weit über dem Maximum, auf das Ziel springen und neu einblenden.
            var ziel = (int)Math.Ceiling(_zielAngewendet / FrameMs);
            while ((int)(_maxFrame - _abspielFrame) + 1 > ziel)
            {
                if (PlatzHat(_abspielFrame, out var alt)) _platzBelegt[alt] = false;
                _abspielFrame++;
                _ueberlauf++;
            }
            n = _abspielFrame;
            _endVerstaerkung = 0f;
        }

        var tiefe = TiefeMs();
        var letzter = _endeBekannt && n + 1 == _endeFrame;
        var vorher = _endVerstaerkung; // Verstärkung am Ende des vorigen Abschnitts, 0 = Stille
        Art art;
        var anzahl = 0;
        var stille = false;
        var verstaerkungAnfang = 1f;
        var verstaerkungEnde = 1f;
        if (PlatzHat(n, out var platz))
        {
            anzahl = _dekodierer.Dekodieren(_platzDaten[platz].AsSpan(0, _platzLaenge[platz]), _roh);
            _platzBelegt[platz] = false;
            // Mehr Samples als der Puffer fasst kann ein vertragstreuer Dekodierer nicht liefern; dann wie ein
            // Fehler behandeln statt im Audio-Thread zu werfen.
            art = anzahl > 0 && anzahl <= _roh.Length ? Art.Normal : Art.Plc;
        }
        else if (!letzter && PlatzHat(n + 1, out _))
        {
            art = Art.Fec;
        }
        else if (_endeBekannt || (_hatPakete && (int)(_maxFrame - n) > 0))
        {
            art = Art.Plc;
        }
        else
        {
            art = Art.Unterlauf;
        }

        if (art == Art.Normal)
        {
            _verdecktInFolge = 0;
            _normal++;
        }
        else
        {
            anzahl = Verdecken(art, n, out stille, out verstaerkungAnfang, out verstaerkungEnde);
        }

        if (art == Art.Unterlauf)
        {
            if (!_imUnterlauf) _unterlaeufe++;
            _imUnterlauf = true;
            _leerSamples += anzahl;
            _unterlaufSamples += anzahl;
        }
        else
        {
            _imUnterlauf = false;
            _leerSamples = 0;
            _abspielFrame = n + 1;
            _zuletztGespielt = n;
            _erzeugteFrames++;
        }

        // Stetig ansetzen: Endete der vorige Abschnitt leiser, als dieser beginnt (Start, Notbremse, nach einer
        // ganz oder teilweise ausgeblendeten oder gescheiterten Verdeckung), wird über 2,5 ms von dort auf den
        // eigenen Pegel geführt. Das gilt für jeden hörbaren Abschnitt, auch für FEC und PLC nach Stille; eine
        // laufende Ausblendung setzt sich dagegen ohne Einblenden fort.
        if (stille)
        {
            _endVerstaerkung = 0f;
        }
        else
        {
            if (vorher < verstaerkungAnfang && verstaerkungAnfang > 0f) Einblenden(_roh.AsSpan(0, anzahl), vorher / verstaerkungAnfang);
            _endVerstaerkung = verstaerkungEnde;
        }

        var laenge = anzahl;
        if (!letzter && anzahl == FrameSamples && art is Art.Normal or Art.Fec)
            laenge = Nachregeln(tiefe, anzahl);
        else
            _roh.AsSpan(0, anzahl).CopyTo(_ausgabe);
        if (letzter)
        {
            Ausblenden(_ausgabe.AsSpan(0, laenge));
            _endVerstaerkung = 0f;
        }

        _ausgabeLese = 0;
        _ausgabeAnzahl = laenge;

        if (art == Art.Unterlauf && _leerSamples >= _e.EndeNachLeerMs * Klangformat.SamplesJeMs)
            PhaseBeenden(Endegrund.Zeitablauf, ref ereignis);
        return true;
    }

    /// <summary>
    /// FEC, PLC oder Unterlauf; mit Ausblendung nach der Höchstdauer. Liefert die erzeugte Länge in _roh;
    /// <paramref name="stille"/> ist wahr, wenn statt Verdeckung Stille eingesetzt wurde. Die beiden Verstärkungen
    /// sind die der Verdeckungskurve am ersten und letzten Sample.
    /// </summary>
    private int Verdecken(Art art, uint n, out bool stille, out float verstaerkungAnfang, out float verstaerkungEnde)
    {
        var soll = art == Art.Unterlauf ? UnterlaufSamplesJeSchritt : FrameSamples;
        var maxVerdeckung = _e.MaxVerdeckungMs * Klangformat.SamplesJeMs;
        var anzahl = 0;
        stille = false;
        if (_verdecktInFolge < maxVerdeckung)
        {
            if (art == Art.Fec && PlatzHat(n + 1, out var folge))
            {
                anzahl = _dekodierer.DekodierenFec(_platzDaten[folge].AsSpan(0, _platzLaenge[folge]), _roh, soll);
                if (anzahl > 0) _fec++;
            }
            if (anzahl <= 0)
            {
                anzahl = _dekodierer.Verdecken(_roh, soll);
                if (art != Art.Unterlauf) _plc++;
            }
        }
        if (anzahl <= 0 || anzahl > _roh.Length)
        {
            anzahl = soll;
            _roh.AsSpan(0, anzahl).Clear();
            _stilleSamples += anzahl;
            stille = true;
        }

        // Verstärkung über die Verdeckungsdauer: voll bis 20 ms vor der Höchstdauer, dann Kosinus auf 0.
        var ausblendStart = Math.Max(0, maxVerdeckung - FrameSamples);
        var bereich = Math.Max(1, maxVerdeckung - ausblendStart);
        verstaerkungAnfang = VerdeckungsVerstaerkung(_verdecktInFolge, ausblendStart, maxVerdeckung, bereich);
        verstaerkungEnde = VerdeckungsVerstaerkung(_verdecktInFolge + anzahl - 1, ausblendStart, maxVerdeckung, bereich);
        for (var i = 0; i < anzahl; i++)
        {
            var pos = _verdecktInFolge + i;
            if (pos < ausblendStart) continue;
            _roh[i] *= VerdeckungsVerstaerkung(pos, ausblendStart, maxVerdeckung, bereich);
        }
        _verdecktInFolge += anzahl;
        return anzahl;
    }

    private static float VerdeckungsVerstaerkung(int pos, int ausblendStart, int maxVerdeckung, int bereich) =>
        pos < ausblendStart ? 1f
        : pos >= maxVerdeckung ? 0f
        : 0.5f * (1f + MathF.Cos(MathF.PI * (pos - ausblendStart) / bereich));

    /// <summary>Kürzen oder Dehnen nach der Puffertiefe; schreibt nach _ausgabe und liefert die Länge.</summary>
    private int Nachregeln(double tiefe, int anzahl)
    {
        var ein = _roh.AsSpan(0, anzahl);
        if (double.IsNaN(tiefe))
        {
            // Ohne Bezugspaket (nur Vorlauf empfangen) gibt es keine Tiefe; dann nicht eingreifen.
            ein.CopyTo(_ausgabe);
            return anzahl;
        }
        var ziel = _zielAngewendet;
        if (!_kuerzen && tiefe > ziel + KuerzenAbMs) _kuerzen = true;
        if (_kuerzen && tiefe <= ziel + KuerzenBisMs) _kuerzen = false;
        if (!_dehnen && tiefe < ziel - KuerzenAbMs) _dehnen = true;
        if (_dehnen && tiefe >= ziel - KuerzenBisMs) _dehnen = false;

        if (_dehnPause > 0 || (!_kuerzen && !_dehnen))
        {
            if (_dehnPause > 0) _dehnPause--;
            ein.CopyTo(_ausgabe);
            return anzahl;
        }
        var leise = Pegel.Dbfs(ein) < _e.LeiseDbfs;
        int laenge;
        if (_kuerzen)
        {
            laenge = Zeitdehnung.Kuerzen(ein, _ausgabe, leise);
            _gekuerzt += anzahl - laenge;
        }
        else
        {
            laenge = Zeitdehnung.Dehnen(ein, _ausgabe, leise);
            _gedehnt += laenge - anzahl;
        }
        if (laenge != anzahl && !leise) _dehnPause = 1; // hörbare Rahmen höchstens jeden zweiten
        return laenge;
    }

    private bool Startbereit()
    {
        if (_endeBekannt) return true;
        if (!_hatPakete) return false;
        var tiefe = TiefeMs();
        if (!double.IsNaN(tiefe)) return tiefe >= _zielAngewendet;
        // Nur Vorlauf-Pakete bisher: nach Füllstand oder Wartezeit.
        return Fuellstand() >= _zielAngewendet
            || _gewartetSamples >= (_zielAngewendet - FrameMs) * Klangformat.SamplesJeMs;
    }

    /// <summary>
    /// Puffertiefe in ms für den nächsten Frame: Abholzeitpunkt minus seine erwartete Ankunft bei der schnellsten
    /// Laufzeit der letzten 2 s, plus 20 ms. NaN, solange die Phase kein Bezugspaket hat.
    /// </summary>
    private double TiefeMs()
    {
        if (_zustand == Zustand.Leer || !_statistik.HatBezug) return double.NaN;
        var erwartetUs = _statistik.MinLaufzeitUs + (int)(_abspielFrame - _statistik.BezugFrame) * 20_000L;
        return (_jetztUs - erwartetUs) / 1000.0 + FrameMs;
    }
    private bool HatNochDaten() =>
        _ausgabeAnzahl > 0 || (_hatPakete && (int)(_maxFrame - _abspielFrame) >= 0);

    /// <summary>Gepufferte Dauer in ms: offene Ausgabe plus Frames vom Abspielpunkt bis zum höchsten empfangenen.</summary>
    private double Fuellstand()
    {
        double ms = (double)_ausgabeAnzahl / Klangformat.SamplesJeMs;
        if (_zustand != Zustand.Leer && _hatPakete && (int)(_maxFrame - _abspielFrame) >= 0)
            ms += ((int)(_maxFrame - _abspielFrame) + 1) * FrameMs;
        return ms;
    }

    private double ZielBerechnet()
    {
        var min = _e.FecErwartet ? _e.MinMitFecMs : _e.MinMs;
        var max = Math.Max(min, _maxZielMs);
        var roh = _statistik.Eintraege == 0 ? _e.StartMs : FrameMs + _statistik.QuantilMs();
        return Math.Clamp(roh, min, max);
    }

    private bool PlatzHat(uint frame, out int platz)
    {
        platz = (int)(frame % Plaetze);
        return _platzBelegt[platz] && _platzFrame[platz] == frame;
    }

    private void PlaetzeLeeren() => Array.Clear(_platzBelegt);

    /// <summary>Anfang von <paramref name="von"/> (relativ, 0 = Stille) auf 1 führen, halbe Kosinusflanke über 2,5 ms.</summary>
    private static void Einblenden(Span<float> x, float von)
    {
        var n = Math.Min(Einblendlaenge, x.Length);
        for (var i = 0; i < n; i++) x[i] *= von + (1f - von) * 0.5f * (1f - MathF.Cos(MathF.PI * i / n));
    }

    private static void Ausblenden(Span<float> x)
    {
        var n = Math.Min(Ausblendlaenge, x.Length);
        var start = x.Length - n;
        for (var i = 0; i < n; i++) x[start + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / n));
    }
}
