// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.ClientKern;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Klang;

/// <summary>
/// Was die <see cref="KlangBruecke"/> vom Sprechkanal des Funkbetriebs braucht (5.6, C1, C2). Im Betrieb der
/// <see cref="Sprechkanal"/> eines <see cref="FunkClient"/> (<see cref="SprechkanalWeg"/>), in Tests eine Attrappe.
/// Alle Methoden sind threadsicher und blockieren nicht auf das Netz.
/// </summary>
public interface IFunkweg
{
    /// <summary>C1: die Sendeauswahl als Ziel, oder null ohne sendbares Feld.</summary>
    Sendeziel? ZielAusAuswahl();

    bool Beginnen(Sendeziel ziel, bool sprachaktivierung, long? aufnahmeTakte);

    bool Senden(ReadOnlySpan<byte> opus, bool preroll);

    void Beenden();

    /// <summary>C2: Die Auswahl wechselt mitten in einer Phase.</summary>
    void Umschalten(Sendeziel neu);

    /// <summary>Eine Phase ist offen oder wartet auf die stabile Auswahl.</summary>
    bool Aktiv { get; }
}

/// <summary>Der <see cref="Sprechkanal"/> eines <see cref="FunkClient"/> als <see cref="IFunkweg"/>.</summary>
public sealed class SprechkanalWeg(Sprechkanal kanal) : IFunkweg
{
    public Sendeziel? ZielAusAuswahl() => kanal.ZielAusAuswahl();

    public bool Beginnen(Sendeziel ziel, bool sprachaktivierung, long? aufnahmeTakte) => kanal.Beginnen(ziel, sprachaktivierung, aufnahmeTakte);

    public bool Senden(ReadOnlySpan<byte> opus, bool preroll) => kanal.Senden(opus, preroll);

    public void Beenden() => kanal.Beenden();

    public void Umschalten(Sendeziel neu) => kanal.Umschalten(neu);

    public bool Aktiv => kanal.Aktiv;
}

/// <summary>
/// Ein Sprecher, der gerade zu hören ist (Sprecheranzeige, seit 0.11.9): das Konto aus dem Stempel seines letzten Pakets, die Art (Netz,
/// Durchsage, Global) und die Netze dieses Empfängers in der Reihenfolge des Stempels (zuerst Global, dann nach Feldnummer); bei einer
/// Durchsage an alle keine. Ob er zu hören ist, sagt die Empfangsanzeige der Maschine ("spricht", A18).
/// </summary>
public sealed record GehoerterSprecher(uint KontoId, Spruchart Art, IReadOnlyList<ushort> Netze);

/// <summary>Lautstärke und Panorama je Funknetz (A9) und der Empfangspegel je Funknetz (A18), für die Oberfläche.</summary>
public interface INetzKlang : IEmpfangsPegelQuelle
{
    /// <summary>
    /// Lautstärke in dB und Panorama (-1 links, 0 beide Ohren, +1 rechts) eines Funknetzes; gilt sofort und für alle
    /// späteren Sprecher in diesem Netz. Aus dem Oberflächen-Thread.
    /// </summary>
    void NetzEinstellen(ushort netzId, float db, float panorama);
}

/// <summary>
/// Verbindet die Klangmaschine mit dem Funkbetrieb (Funk.ClientKern).
/// <list type="bullet">
/// <item>Senden: <see cref="IKlangMaschine.SprachpaketBereit"/> kommt im Melde-Thread der Maschine an, nie im Audio-Thread.
/// BEGINN beginnt eine Phase an die aktuelle Sendeauswahl (C1) mit VA und Aufnahmezeit aus dem Paket, jedes Paket geht
/// mit Vorlauf-Kennzeichen hinaus, ENDE beendet die Phase (ENDE dreifach macht der Sprechkanal). Der Audio-Thread schreibt
/// nur in den Ring der Maschine; Sperren gibt es erst hier, im Melde-Thread und im Sprechkanal.</item>
/// <item>Wechselt die Sendeauswahl mitten im Spruch (Numpad + und -), schaltet <see cref="AuswahlGeaendert"/> nach C2 um.</item>
/// <item>Empfangen: <see cref="ISprachEmpfaenger"/> auf dem UDP-Thread bzw. der Leseschleife. Funknetze (u16) werden auf
/// Mischer-Netze 0 bis 62 abgebildet; hat ein Paket mehrere Treffer-Netze, gilt nach C7 das lauteste, bei Gleichstand
/// das erste. GLOBAL und DURCHSAGE gehen als Durchsage in den Mischer (C9: sie senken die anderen Netze ab). Die
/// Ankunft stempelt die Uhr der Maschine (<see cref="IKlangMaschine.JetztUs"/>).</item>
/// <item>Anzeige: Die Empfangsanzeige der Maschine (je Sprecher) wird zu <see cref="NetzPegel"/> je Funknetz, Höchstwert
/// über die Sprecher, und zwar für jedes Netz, in dem der Sprecher zuletzt gestempelt war.</item>
/// <item>Sprecheranzeige (seit 0.11.9): Aus derselben Empfangsanzeige wird je Sprecher, der gerade spricht, Konto, Art und Netze seines letzten
/// Stempels (<see cref="SprecherGehoert"/>); dazu das Ziel der eigenen offenen Phase (<see cref="EigeneSendung"/>).</item>
/// <item>VerlustHinweis des Servers (4.5) geht an den Kodierer (In-band-FEC).</item>
/// </list>
/// Ein Funknetz bekommt sein Mischer-Netz, sobald die Oberfläche es einstellt (<see cref="NetzEinstellen"/>) oder das
/// erste Paket kommt. Sind alle 63 vergeben, wird das am längsten unbenutzte neu vergeben; bei 5 oder 10 Feldern plus
/// Global kommt das nur nach vielen Umstellungen vor.
/// </summary>
public sealed class KlangBruecke : IKlangAnschluss, INetzKlang, IMeldeziel, IDisposable
{
    /// <summary>Ziel der Melde-Taste (A4), solange sie gehalten ist; sonst <c>null</c> (es gilt die Sendeauswahl).</summary>
    private Sendeziel? _meldeZiel;

    /// <summary>1: Der nächste Rahmen beginnt eine neue Phase am aktuellen Ziel (nach einem Wechsel des Meldeziels mitten im Spruch).</summary>
    private int _neuBeginnen;

    /// <summary>
    /// 1: Die Melde-Taste ist los und das Mikrofon schließt, aber die Phase der Meldung hat ihren letzten Rahmen noch nicht gesehen;
    /// bis dahin gehören die Rahmen zur Meldung. Ein Beginn oder Ende räumt ihn ab (auch das ENDE, mit dem die Maschine ihn beim Druck der
    /// Sprechtaste beendet); ein Wechsel mit Umschalten beendet ihn sofort.
    /// </summary>
    private int _meldeRest;

    /// <summary>Das Netz, an dem der Rest einer Meldung läuft (gilt nur, solange <see cref="_meldeRest"/> 1 ist).</summary>
    private Sendeziel? _restZiel;

    /// <summary>
    /// Melde-Taste (RAID-S2-ENTWURF Abschnitt 4): Solange <paramref name="netzId"/> gesetzt ist, beginnt jede Sprechphase an diesem
    /// Netz statt an der Sendeauswahl, und ein Wechsel der Sendeauswahl (Numpad + und -) schaltet sie nicht um. Läuft beim Wechsel
    /// eine Phase, endet sie; der nächste Rahmen beginnt am neuen Ziel, ohne auf ein neues Drücken zu warten. Ohne
    /// <paramref name="umschalten"/> beginnt die Maschine selbst eine neue Phase (Melde-Taste los und Mikrofon zu, oder eine Taste öffnet das
    /// Mikrofon neu, auch im Nachlauf der anderen): Die laufende Phase läuft bis zu ihrem letzten Rahmen an ihrem Ziel weiter. Der Melde-Thread
    /// holt Rahmen von vor dem Wechsel erst danach ab, und seit 0.9.3 sendet die Maschine noch den Nachlauf der Sprechtaste (Q66); die gingen
    /// sonst als neue Phase ans andere Ziel. Den Rest einer Meldung merkt sich die Brücke, damit auch die Sendeauswahl ihn nicht umschaltet;
    /// kommt die Melde-Taste in diesem Rest wieder, läuft die Meldung am selben Netz nahtlos weiter.
    /// </summary>
    public void MeldezielSetzen(ushort? netzId, bool umschalten = true)
    {
        var neu = netzId is { } id ? Sendeziel.Netz(id) : null;
        var alt = Interlocked.Exchange(ref _meldeZiel, neu);
        var rest = Interlocked.Exchange(ref _meldeRest, 0) == 1;
        var restZiel = Interlocked.Exchange(ref _restZiel, null);
        if (_weg is not { } weg || !weg.Aktiv) return;
        if (!umschalten)
        {
            if (alt is not null || rest)
            {
                Volatile.Write(ref _restZiel, alt ?? restZiel);
                Volatile.Write(ref _meldeRest, 1);
            }
            return;
        }
        // Wieder gedrückt, solange der Rest der Meldung läuft (Nachlauf, Q66): dasselbe Netz, dieselbe Phase.
        if (rest && neu is not null && Equals(restZiel, neu)) return;
        if (Equals(alt, neu) && !rest) return;
        weg.Beenden();
        Volatile.Write(ref _sendung, null);
        Volatile.Write(ref _neuBeginnen, 1);
    }

    /// <summary>Schlüssel für Pakete ohne Netz (Durchsage an alle).</summary>
    private const ushort OhneNetz = 0;

    private sealed class SprecherNetze
    {
        public readonly ushort[] Netze = new ushort[Sprachkanal.RunterNetzeMax];
        public int Anzahl;

        /// <summary>Die Art aus dem letzten Stempel (seit 0.11.9 für die Sprecheranzeige).</summary>
        public Spruchart Art;
    }

    private readonly IKlangMaschine _maschine;
    private readonly Lock _sperre = new();
    private readonly Dictionary<ushort, int> _mischerNetz = [];
    private readonly ushort[] _funknetzJePlatz = new ushort[IKlangMaschine.FunkNetze];
    private readonly long[] _zuletztJePlatz = new long[IKlangMaschine.FunkNetze];
    private readonly Dictionary<ushort, (float Db, float Panorama)> _netzKlang = [];
    private readonly Dictionary<uint, SprecherNetze> _sprecherNetze = [];
    private readonly Dictionary<ushort, float> _anzeige = [];
    private int _belegt;
    private long _benutzung;
    private volatile IFunkweg? _weg;
    private FunkClient? _client;
    private long _empfangen;
    private long _abgewiesen;
    private long _gesendet;

    /// <summary>Das Ziel der offenen eigenen Phase (seit 0.11.9 für "Du -> Kanal" der Sprecheranzeige), sonst <c>null</c>.</summary>
    private Sendeziel? _sendung;

    /// <summary>Die letzte Meldung an <see cref="SprecherGehoert"/> nannte jemanden (dann geht beim Verstummen einmal eine leere).</summary>
    private bool _gehoertZuletzt;

    public KlangBruecke(IKlangMaschine maschine)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        _maschine = maschine;
        _maschine.SprachpaketBereit += Senden;
        _maschine.EmpfangsPegel += EmpfangMelden;
        // Die Attrappe der Vorführung liefert Pegel schon je Funknetz; sie gehen unverändert durch.
        if (_maschine is IEmpfangsPegelQuelle direkt) direkt.NetzPegel += Weiterreichen;
    }

    public event Action<ushort, float>? NetzPegel;

    /// <summary>
    /// Sprecheranzeige (seit 0.11.9): wer gerade zu hören ist, aus der Empfangsanzeige der Maschine (je Sprecher "spricht") und dem Stempel
    /// seines letzten Pakets (<see cref="GehoerterSprecher"/>). Aus dem Melde-Thread der Maschine, etwa 25 je Sekunde, solange jemand spricht;
    /// verstummen alle, einmal mit leerer Liste, danach nichts. Ohne Server-Änderung und ohne Recht: Der Stempel trägt nur die eigenen Netze
    /// (bei einer Durchsage die getroffenen Felder); gefiltert wird in der Oberfläche (<c>Modelle.Sprecheranzeige</c>).
    /// </summary>
    public event Action<IReadOnlyList<GehoerterSprecher>>? SprecherGehoert;

    /// <summary>
    /// Wohin die eigene Stimme gerade geht (seit 0.11.9, Zeile "Du -> Kanal" der Sprecheranzeige): das Ziel der offenen Phase am Sprechkanal,
    /// <c>null</c> ohne offene Phase. Aus jedem Thread.
    /// </summary>
    public Sendeziel? EigeneSendung => _weg is { Aktiv: true } ? Volatile.Read(ref _sendung) : null;

    /// <summary>Angenommene Sprachpakete (Diagnose, Tests).</summary>
    public long EmpfangenePakete => Interlocked.Read(ref _empfangen);

    /// <summary>Pakete, die die Maschine nicht annahm (Mischer voll, Ring voll).</summary>
    public long AbgewiesenePakete => Interlocked.Read(ref _abgewiesen);

    /// <summary>Pakete, die der Sprechkanal hinausließ.</summary>
    public long GesendetePakete => Interlocked.Read(ref _gesendet);

    /// <summary>Den Sprechkanal eines neuen Clients anbinden (vor dessen Start); VerlustHinweis-Pushes gehen an die Maschine.</summary>
    public void Anbinden(FunkClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        LoesenIntern();
        lock (_sperre) _client = client;
        client.PushErhalten += PushErhalten;
        _weg = new SprechkanalWeg(client.Sprechen);
    }

    /// <summary>Einen Weg ohne Client anbinden (Tests).</summary>
    public void Anbinden(IFunkweg weg)
    {
        ArgumentNullException.ThrowIfNull(weg);
        LoesenIntern();
        _weg = weg;
    }

    /// <summary>Beim Trennen: offene Phase beenden, Pushes abmelden.</summary>
    public void Loesen(FunkClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_sperre)
        {
            if (!ReferenceEquals(_client, client)) return;
        }
        LoesenIntern();
    }

    /// <summary>C2: Die Sendeauswahl ist gerade lokal gewechselt. Läuft eine Phase, endet sie und setzt am neuen Ziel fort.</summary>
    public void AuswahlGeaendert()
    {
        var weg = _weg;
        // Während der Melde-Taste (und ihrem Rest bis zum letzten Rahmen) spricht man ins Meldenetz; die neue Auswahl gilt danach.
        if (weg is null || !weg.Aktiv || Volatile.Read(ref _meldeZiel) is not null || Volatile.Read(ref _meldeRest) == 1) return;
        var ziel = weg.ZielAusAuswahl();
        if (ziel is not null) weg.Umschalten(ziel);
        else weg.Beenden();
        Volatile.Write(ref _sendung, ziel);
    }

    public void NetzEinstellen(ushort netzId, float db, float panorama)
    {
        if (netzId == OhneNetz) return;
        lock (_sperre)
        {
            _netzKlang[netzId] = (db, panorama);
            if (_mischerNetz.TryGetValue(netzId, out var platz)) Anwenden(platz, db, panorama);
            else _ = Platz(netzId);
        }
    }

    /// <summary>Mischer-Netz eines Funknetzes, vergeben beim ersten Paket; -1, wenn noch keins vergeben ist (Tests).</summary>
    public int MischerNetzVon(ushort netzId)
    {
        lock (_sperre) return _mischerNetz.TryGetValue(netzId, out var platz) ? platz : -1;
    }

    // ---------------------------------------------------------------- Empfang (UDP-Thread, Leseschleife)

    public void Empfangen(in SprachEmpfang empfang)
    {
        var p = empfang.Paket;
        int netz;
        lock (_sperre)
        {
            netz = TrefferNetz(p);
            SprecherMerken(p);
        }
        var durchsage = p.Art is Spruchart.Durchsage or Spruchart.Global;
        if (_maschine.SpracheEmpfangen(p.Absender, netz, p.Sprechphase, p.FrameNr, (Sprachkennzeichen)(byte)p.Kennzeichen, p.Opus,
            _maschine.JetztUs, durchsage))
            Interlocked.Increment(ref _empfangen);
        else
            Interlocked.Increment(ref _abgewiesen);
    }

    /// <summary>C6: Nach 300 ms ohne Paket endet die Phase im Jitterpuffer der Maschine von selbst; hier ist nichts zu tun.</summary>
    public void ZeitlinieBeendet(uint absender, ushort sprechphase)
    {
    }

    /// <summary>5.13 mit libopus: Samples bei 48 kHz oder -1.</summary>
    public int Samples(ReadOnlySpan<byte> opus)
    {
        var n = OpusPaket.AnzahlSamples(opus);
        return n > 0 ? n : -1;
    }

    public void Dispose()
    {
        _maschine.SprachpaketBereit -= Senden;
        _maschine.EmpfangsPegel -= EmpfangMelden;
        if (_maschine is IEmpfangsPegelQuelle direkt) direkt.NetzPegel -= Weiterreichen;
        LoesenIntern();
    }

    private void Weiterreichen(ushort netz, float db) => NetzPegel?.Invoke(netz, db);

    // ---------------------------------------------------------------- Senden (Melde-Thread der Maschine)

    private void Senden(in SendeRahmen rahmen)
    {
        var weg = _weg;
        if (weg is null) return;
        var neu = Interlocked.Exchange(ref _neuBeginnen, 0) == 1 && !rahmen.Ende;
        // Ein Beginn oder ein Ende schließt den Rest einer Meldung ab: Danach gilt wieder die Sendeauswahl.
        if (rahmen.Beginn || rahmen.Ende) Volatile.Write(ref _meldeRest, 0);
        if ((rahmen.Beginn || neu) && (Volatile.Read(ref _meldeZiel) ?? weg.ZielAusAuswahl()) is { } ziel
            && weg.Beginnen(ziel, rahmen.Sprachaktivierung, rahmen.AufnahmeTakte))
            Volatile.Write(ref _sendung, ziel);
        if (!rahmen.Opus.IsEmpty && weg.Senden(rahmen.Opus, rahmen.Vorlauf)) Interlocked.Increment(ref _gesendet);
        if (rahmen.Ende)
        {
            weg.Beenden();
            Volatile.Write(ref _sendung, null);
        }
    }

    // ---------------------------------------------------------------- Anzeige (Melde-Thread der Maschine)

    private void EmpfangMelden(ReadOnlySpan<SprecherPegel> sprecher)
    {
        var melden = NetzPegel;
        var gehoert = SprecherGehoert;
        if (melden is null && gehoert is null) return;
        List<GehoerterSprecher>? liste = null;
        lock (_sperre)
        {
            _anzeige.Clear();
            foreach (var s in sprecher)
            {
                if (!s.Spricht) continue;
                var bekannt = _sprecherNetze.TryGetValue(s.KontoId, out var n);
                if (bekannt && n!.Anzahl > 0)
                {
                    for (var i = 0; i < n.Anzahl; i++) Hoechstwert(n.Netze[i], s.PegelDbfs);
                }
                else if (s.Netz >= 0 && s.Netz < _funknetzJePlatz.Length && _funknetzJePlatz[s.Netz] != OhneNetz)
                {
                    Hoechstwert(_funknetzJePlatz[s.Netz], s.PegelDbfs);
                }
                // Sprecheranzeige (0.11.9): Konto, Art und Netze aus dem letzten Stempel; ohne Stempel (nie ein Paket) kein Eintrag.
                if (gehoert is not null && bekannt) (liste ??= []).Add(new GehoerterSprecher(s.KontoId, n!.Art, n.Netze.AsSpan(0, n.Anzahl).ToArray()));
            }
        }
        if (melden is not null)
        {
            foreach (var (netz, db) in _anzeige) melden(netz, db);
        }
        if (gehoert is null || (liste is null && !_gehoertZuletzt)) return;
        _gehoertZuletzt = liste is not null;
        gehoert(liste ?? (IReadOnlyList<GehoerterSprecher>)[]);
    }

    private void Hoechstwert(ushort netz, float db)
    {
        if (netz == OhneNetz) return;
        if (!_anzeige.TryGetValue(netz, out var alt) || db > alt) _anzeige[netz] = db;
    }

    // ---------------------------------------------------------------- Hilfen (unter der Sperre)

    /// <summary>C7: unter den Treffer-Netzen das mit der höchsten Lautstärke, bei Gleichstand das erste.</summary>
    private int TrefferNetz(in SpracheRunter p)
    {
        if (p.NetzAnzahl == 0) return Platz(OhneNetz);
        var bestes = p.Netz(0);
        var bestesDb = Lautstaerke(bestes);
        for (var i = 1; i < p.NetzAnzahl; i++)
        {
            var n = p.Netz(i);
            var db = Lautstaerke(n);
            if (db > bestesDb)
            {
                bestes = n;
                bestesDb = db;
            }
        }
        return Platz(bestes);
    }

    private float Lautstaerke(ushort netz) => _netzKlang.TryGetValue(netz, out var k) ? k.Db : 0f;

    private int Platz(ushort netz)
    {
        if (_mischerNetz.TryGetValue(netz, out var platz))
        {
            _zuletztJePlatz[platz] = ++_benutzung;
            return platz;
        }
        if (_belegt < _funknetzJePlatz.Length)
        {
            platz = _belegt++;
        }
        else
        {
            // Alle vergeben: das am längsten unbenutzte Mischer-Netz übernimmt das neue Funknetz.
            platz = 0;
            for (var i = 1; i < _zuletztJePlatz.Length; i++)
                if (_zuletztJePlatz[i] < _zuletztJePlatz[platz]) platz = i;
            _mischerNetz.Remove(_funknetzJePlatz[platz]);
        }
        _mischerNetz[netz] = platz;
        _funknetzJePlatz[platz] = netz;
        _zuletztJePlatz[platz] = ++_benutzung;
        var (db, panorama) = _netzKlang.TryGetValue(netz, out var k) ? k : (0f, 0f);
        Anwenden(platz, db, panorama);
        return platz;
    }

    private void Anwenden(int platz, float db, float panorama)
    {
        _maschine.SetzeNetzLautstaerke(platz, db);
        _maschine.SetzeNetzPanorama(platz, panorama);
    }

    private void SprecherMerken(in SpracheRunter p)
    {
        if (!_sprecherNetze.TryGetValue(p.Absender, out var s))
        {
            s = new SprecherNetze();
            _sprecherNetze[p.Absender] = s;
        }
        var n = Math.Min(p.NetzAnzahl, s.Netze.Length);
        for (var i = 0; i < n; i++) s.Netze[i] = p.Netz(i);
        s.Anzahl = n;
        s.Art = p.Art;
    }

    private void PushErhalten(object? sender, PushEreignis p)
    {
        if (p.Body is VerlustHinweis v) _maschine.SetzeVerlustHinweis(v.Prozent);
    }

    private void LoesenIntern()
    {
        var weg = _weg;
        _weg = null;
        weg?.Beenden();
        Volatile.Write(ref _sendung, null);
        FunkClient? alt;
        lock (_sperre)
        {
            alt = _client;
            _client = null;
        }
        if (alt is not null) alt.PushErhalten -= PushErhalten;
    }
}
