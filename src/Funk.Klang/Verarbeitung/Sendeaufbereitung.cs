// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Klang.Kern;

namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Sendeseite zwischen Gate und Transport: nimmt 10-ms-Rahmen, lässt sie durch das <see cref="SprachGate"/>,
/// fasst je zwei zu 20 ms zusammen, kodiert über <see cref="IKodierer"/> und liefert Pakete mit den
/// Kennzeichen aus FUNKPROTOKOLL-v1 5.6 (BEGINN, PREROLL, VA, ENDE). Frame-Nr, Sprechphase und das dreifache
/// ENDE im 20-ms-Abstand sind Sache des Transports. Endet eine Phase nach einer ungeraden Zahl von Rahmen,
/// wird der letzte 20-ms-Rahmen mit Stille aufgefüllt. Beginnt eine neue Phase, während eine offen ist
/// (Taste schlägt Sprachaktivierung), wird die alte vorher mit ENDE geschlossen.
/// Läuft im Aufnahme-Thread; allokiert nach dem Konstruktor nichts. Der Kodierer gehört dem Aufrufer.
/// </summary>
public sealed class Sendeaufbereitung
{
    /// <summary>Höchstlänge eines Opus-Pakets nach FUNKPROTOKOLL-v1 5.6.</summary>
    public const int MaxPaketBytes = 400;
    private const int MaxPakete = 16;
    private const int Rahmen = Klangformat.Samples10ms;

    private readonly SprachGate _gate;
    private readonly IKodierer _kodierer;
    private readonly float[] _rahmen = new float[Rahmen];
    private readonly float[] _paar = new float[Klangformat.Samples20ms];
    private readonly byte[][] _pakete = new byte[MaxPakete][];
    private readonly int[] _laengen = new int[MaxPakete];
    private readonly Sprachkennzeichen[] _kennzeichen = new Sprachkennzeichen[MaxPakete];
    private int _anzahl;
    private bool _halbVoll;
    private bool _halbVorlauf;
    private bool _phaseOffen;
    private bool _ersterDerPhase;
    private bool _phaseVa;

    public Sendeaufbereitung(SprachGate gate, IKodierer kodierer)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(kodierer);
        _gate = gate;
        _kodierer = kodierer;
        for (var i = 0; i < MaxPakete; i++) _pakete[i] = new byte[MaxPaketBytes];
    }

    public SprachGate Gate => _gate;

    /// <summary>Fehlgeschlagene Kodierungen (Kodierer lieferte keine gültige Länge oder meldete einen Fehler).</summary>
    public long Kodierfehler { get; private set; }

    public int AnzahlPakete => _anzahl;

    /// <summary>Stopwatch-Takte, die der Kodierer im letzten <see cref="Verarbeiten"/> brauchte (Telemetrie der Sendekette).</summary>
    public long LetzteKodierTakte { get; private set; }

    /// <summary>Kodieraufrufe im letzten <see cref="Verarbeiten"/>.</summary>
    public int LetzteKodierungen { get; private set; }

    public ReadOnlySpan<byte> Paket(int index) => _pakete[index].AsSpan(0, _laengen[index]);

    public Sprachkennzeichen Kennzeichen(int index) => _kennzeichen[index];

    /// <summary>
    /// Verlusthinweis aus den Empfängerberichten an den Kodierer weitergeben. Aus einem anderen Thread als dem
    /// Aufnahme-Thread nur, wenn der Kodierer das zulässt: <c>OpusKodierer</c> merkt den Wert nur vor und
    /// übernimmt ihn vor dem nächsten Rahmen; ein Kodierer, der sofort nativ einstellt, darf nicht parallel
    /// zu <see cref="Verarbeiten"/> gerufen werden.
    /// </summary>
    public void SetzeVerlustHinweis(int prozent) => _kodierer.SetzeVerlustHinweis(Math.Clamp(prozent, 0, 100));

    /// <summary>
    /// Einen 10-ms-Rahmen verarbeiten; Rückgabe: Zahl der fertigen Pakete (gültig bis zum nächsten Aufruf). <paramref name="pegelDbfs"/> geht an das
    /// Gate (<see cref="SprachGate.Verarbeiten"/>): der Pegel vor der Klangfarbe, ohne Angabe misst das Gate selbst.
    /// </summary>
    public int Verarbeiten(ReadOnlySpan<float> rahmen10ms, float sprachwahrscheinlichkeit, float agcVerstaerkungDb = 0f, bool sprechtaste = false,
        float? pegelDbfs = null)
    {
        _anzahl = 0;
        LetzteKodierTakte = 0;
        LetzteKodierungen = 0;
        _gate.Verarbeiten(rahmen10ms, sprachwahrscheinlichkeit, agcVerstaerkungDb, sprechtaste, pegelDbfs);
        while (_gate.Holen(_rahmen, out var art, out _))
        {
            if ((art & SenderahmenArt.Beginn) != 0)
            {
                if (_phaseOffen) PhaseSchliessen();
                _phaseOffen = true;
                _ersterDerPhase = true;
                _phaseVa = (art & SenderahmenArt.Sprachaktivierung) != 0;
            }
            if (!_phaseOffen) continue;
            var vorlauf = (art & SenderahmenArt.Vorlauf) != 0;
            if (!_halbVoll)
            {
                _rahmen.CopyTo(_paar, 0);
                _halbVoll = true;
                _halbVorlauf = vorlauf;
            }
            else
            {
                _rahmen.CopyTo(_paar, Rahmen);
                _halbVoll = false;
                // PREROLL nur, wenn auch die zweite Hälfte aus dem Vorlauf stammt (Klangstufe C1): Ein Paket, das mit dem laufenden Rahmen
                // schließt, geht in Echtzeit hinaus und gehört in die Laufzeitstatistik des Empfängers. So bleiben es bei 120 ms Vorlauf
                // höchstens 7 PREROLL-Pakete (FUNKPROTOKOLL-v1 5.6; SprachGate begrenzt den Burst passend).
                Kodieren(vorlauf);
            }
            if ((art & SenderahmenArt.Letzter) != 0) PhaseSchliessen();
        }
        return _anzahl;
    }

    /// <summary>
    /// Mikro stumm (Klangstufe C2a): Eine offene Phase endet sofort, ohne weiteren Rahmen; ein halbes Paket wird mit Stille aufgefüllt, dann ENDE.
    /// Das Gate vergisst Anlauf, Phase und Taste (<see cref="SprachGate.Schliessen"/>), behält aber Boden und Statistik. Rückgabe: Zahl der Pakete.
    /// </summary>
    public int Abbrechen()
    {
        _anzahl = 0;
        LetzteKodierTakte = 0;
        LetzteKodierungen = 0;
        _gate.Schliessen();
        if (_phaseOffen) PhaseSchliessen();
        return _anzahl;
    }

    private void PhaseSchliessen()
    {
        if (_halbVoll)
        {
            Array.Clear(_paar, Rahmen, Rahmen);
            _halbVoll = false;
            Kodieren(_halbVorlauf);
        }
        Hinzufuegen(Sprachkennzeichen.Ende | Grundkennzeichen(), 0);
        _phaseOffen = false;
    }

    private void Kodieren(bool vorlauf)
    {
        if (_anzahl >= MaxPakete) return;
        int laenge;
        var start = Stopwatch.GetTimestamp();
        try
        {
            // Das Ziel hat MaxPaketBytes; der Kodierer hält die Länge ein (OpusKodierer: max_data_bytes).
            laenge = _kodierer.Kodieren(_paar, _pakete[_anzahl]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Der Kodierer meldet einen Fehler (libopus): Das Paket fällt aus, die Aufnahme läuft weiter.
            laenge = -1;
        }
        LetzteKodierTakte += Stopwatch.GetTimestamp() - start;
        LetzteKodierungen++;
        if (laenge <= 0 || laenge > MaxPaketBytes)
        {
            Kodierfehler++;
            return;
        }
        var k = Grundkennzeichen();
        if (_ersterDerPhase) k |= Sprachkennzeichen.Beginn;
        if (vorlauf) k |= Sprachkennzeichen.Preroll;
        _ersterDerPhase = false;
        Hinzufuegen(k, laenge);
    }

    private Sprachkennzeichen Grundkennzeichen() => _phaseVa ? Sprachkennzeichen.Sprachaktivierung : Sprachkennzeichen.Keine;

    private void Hinzufuegen(Sprachkennzeichen kennzeichen, int laenge)
    {
        if (_anzahl >= MaxPakete) return;
        _laengen[_anzahl] = laenge;
        _kennzeichen[_anzahl] = kennzeichen;
        _anzahl++;
    }
}
