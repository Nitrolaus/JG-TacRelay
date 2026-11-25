// JG TacRelay - Maintainer: Sarevoc
using System.Threading.Channels;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>
/// Client für den Protokollteil des Funks, ohne Oberfläche und ohne Audio. Baut die Steuerverbindung auf (Aufbau nach
/// Wahl: TLS mit Gerätezertifikat und Pin, Testzugang, prozessintern), durchläuft Anmeldung und Zustand nach 4.6,
/// hält das <see cref="Lagebild"/>, stellt Anfragen, sendet und empfängt Sprache (<see cref="Sprechen"/>,
/// <see cref="FunkClientOptionen.Empfaenger"/>) und verbindet nach 7.1 neu. Die WPF-Oberfläche und
/// <c>funk-testclient</c> bauen darauf.
/// </summary>
public sealed class FunkClient : IAsyncDisposable
{
    private readonly FunkClientOptionen _optionen;
    private readonly ClientKanal _kanal = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _nutzer = new(0, 1);
    private volatile Sitzung? _sitzung;
    private volatile ClientZustand _zustand;
    private Task? _schleife;
    private volatile bool _udpSperre;
    private volatile bool _aufbauGesperrt;
    private int _aktivierungen;
    private bool _vomNutzer;
    private readonly Channel<Zustandswechsel> _wechsel = Channel.CreateUnbounded<Zustandswechsel>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _melden;

    public FunkClient(FunkClientOptionen optionen)
    {
        ArgumentNullException.ThrowIfNull(optionen);
        _optionen = optionen;
        Sprechen = new Sprechkanal(this);
        Messwerte = new Messwerte();
        _melden = Task.Run(MeldeschleifeAsync);
    }

    /// <summary>Zustandswechsel in ihrer Reihenfolge, aber nie unter einer Sperre der Sitzung an die Abonnenten.</summary>
    private async Task MeldeschleifeAsync()
    {
        await foreach (var w in _wechsel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                ZustandGeaendert?.Invoke(this, w);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                FehlerMelden("ZustandGeaendert", ex);
            }
        }
    }

    /// <summary>Stand des Servers aus Sicht des Clients.</summary>
    public Lagebild Lage { get; } = new();

    /// <summary>
    /// Die Uhr des Servers, laufend über Ping abgeglichen (Meldesystem 0.12.0: Countdown in Serverzeit). Je neue Verbindung beginnt
    /// der Abgleich von vorn, grob aus Welcome, nach der ersten Ping-Antwort mit Laufzeitausgleich.
    /// </summary>
    public Uhrabgleich Uhr { get; } = new();

    /// <summary>Fingerabdruck des eigenen Geräts (steht in jeder Unterschrift des Admin-Schlüssels, 3.9).</summary>
    public Protokoll.Identitaet.Fingerabdruck Geraet => _optionen.Identitaet.Fingerabdruck;

    /// <summary>Senden (C1, C2, C4, 5.6).</summary>
    public Sprechkanal Sprechen { get; }

    /// <summary>Messwerte der laufenden Sitzung (je Sitzung neu, denn der runter-Zähler beginnt je Sitzung bei 0).</summary>
    public Messwerte Messwerte { get; private set; }

    internal Zustandsmelder Melder { get; } = new();

    /// <summary>Zustand nach 4.6.</summary>
    public ClientZustand Zustand => _zustand;

    /// <summary>Ob Sprache hoch gerade über den Tunnel geht.</summary>
    public bool Tunnel => _sitzung?.Tunnel == true;

    /// <summary>
    /// Kein automatischer Neuversuch (KONTO_GESPERRT, GERAET_GESPERRT, VERSION_ZU_ALT, VERDRAENGEN_GESPERRT, Bye{VERDRAENGT}, seit 0.6.3 Bye{ENTFERNT}),
    /// dazu nach "Server nicht vertrauenswürdig" bei der Erstverbindung (4.1: Abbruch mit Anzeige). Erst der Nutzer verbindet neu.
    /// </summary>
    public bool NeuversuchGesperrt => _kanal.Automat.NeuversuchGesperrt || _aufbauGesperrt;

    /// <summary>Wie die letzte Sitzung endete.</summary>
    public Sitzungsende? LetztesEnde { get; private set; }

    /// <summary>Wie oft eine Sitzung AKTIV wurde.</summary>
    public int Aktivierungen => Volatile.Read(ref _aktivierungen);

    /// <summary>Epoche der Sprachschlüssel der laufenden Sitzung.</summary>
    public byte Epoche => _sitzung?.Sprache?.Epoche ?? 0;

    public event EventHandler<Zustandswechsel>? ZustandGeaendert;

    /// <summary>Einmalcode zum Anzeigen (REGISTRIERUNG).</summary>
    public event EventHandler<Registrierungscode>? CodeErhalten;

    /// <summary>Pushes des Servers (SendeFehler, DurchsageStart/-Ende, Sprechanzeige, Meldung, GeraetStatus, ...).</summary>
    public event EventHandler<PushEreignis>? PushErhalten;

    public event EventHandler<Sitzungsende>? SitzungBeendet;

    /// <summary>Fehler im Hintergrund (für Log und Anzeige).</summary>
    public event EventHandler<Exception>? HintergrundFehler;

    /// <summary>
    /// Test: UDP gesperrt, wie hinter einer Firewall, die UDP verwirft. Nichts geht per UDP hinaus, nichts wird per UDP
    /// angenommen; die Ausfallerkennung (5.12) schaltet dann den Tunnel ein.
    /// </summary>
    internal bool UdpSperre
    {
        get => _udpSperre;
        set
        {
            _udpSperre = value;
            if (_sitzung?.Sprache is { } s) s.UdpSperre = value;
        }
    }

    /// <summary>Startet die Verbindungsschleife (Erstverbindung sofort, danach 7.1).</summary>
    public void Starten()
    {
        if (_schleife is not null) throw new InvalidOperationException("Schon gestartet.");
        _schleife = Task.Run(SchleifeAsync);
    }

    /// <summary>Neu verbinden auf Wunsch des Nutzers, auch nach gesperrtem Neuversuch ("zurückholen").</summary>
    public void NeuVerbinden()
    {
        _vomNutzer = true;
        try
        {
            _nutzer.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        _sitzung?.NeueSitzung("Nutzer verbindet neu");
    }

    /// <summary>Wartet, bis <paramref name="bedingung"/> für den Zustand gilt. <c>false</c> nach der Frist.</summary>
    public async Task<bool> WartenAufAsync(Func<ClientZustand, bool> bedingung, TimeSpan frist)
    {
        ArgumentNullException.ThrowIfNull(bedingung);
        var bis = Monouhr.Ms() + (long)frist.TotalMilliseconds;
        while (!bedingung(_zustand))
        {
            if (Monouhr.Ms() >= bis) return false;
            await Task.Delay(10).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>Wartet auf AKTIV/UDP oder AKTIV/TUNNEL.</summary>
    public Task<bool> AktivAsync(TimeSpan frist) => WartenAufAsync(static z => z is ClientZustand.AktivUdp or ClientZustand.AktivTunnel, frist);

    // ---- Anfragen ----

    /// <summary>
    /// Stellt eine Anfrage in der laufenden Sitzung. Einen Pflichttyp, den der Server nach seiner Fassung in Welcome noch nicht
    /// kennt (<see cref="Versionen.ServerKennt"/>), schickt der Client nicht: Ein Server 0.2.0 trennt bei 0x0017 und 0x0038 mit
    /// PROTOKOLL (4.7). Stattdessen kommt sofort eine Antwort <c>VERSION</c> mit einem Satz.
    /// </summary>
    public Task<Antwort> AnfrageAsync<T>(Nachrichtentyp typ, T body, CancellationToken abbruch = default) where T : class
    {
        if (Lage.Welcome?.Server?.Version is { } fassung && !Versionen.ServerKennt(fassung, typ))
        {
            return Task.FromResult(new Antwort(typ, null, new Fehler
            {
                Code = Fehlercode.Version,
                Text = "Der Server hat noch Fassung " + fassung + " und kennt das nicht. Erst den Server aktualisieren, dann geht es.",
            }));
        }
        return (_sitzung ?? throw new VerbindungGetrenntException("Keine Sitzung")).AnfrageAsync(typ, body, abbruch);
    }

    /// <summary>HoerenSetzen (4.5), zusammengefasst nach C5 je Netz.</summary>
    public Task<Antwort> HoerenSetzenAsync(ushort netzId, bool hoeren) =>
        Melder.Melden(Nachrichtentyp.HoerenSetzen, netzId, () => AnfrageAsync(Nachrichtentyp.HoerenSetzen, new HoerenSetzenAnfrage { NetzId = netzId, Hoeren = hoeren }));

    /// <summary>
    /// SendeauswahlSetzen (4.5, C1): die Auswahl als Feldnummern lokal halten und dem Server melden (Anzeige für den
    /// Raidleiter, "wie letztes Mal"), zusammengefasst nach C5.
    /// </summary>
    public Task<Antwort> SendeauswahlSetzenAsync(IReadOnlyList<byte> felder, bool global)
    {
        ArgumentNullException.ThrowIfNull(felder);
        Sprechen.AuswahlSetzen(felder, global);
        var (f, g) = Sprechen.Auswahl;
        return Melder.Melden(Nachrichtentyp.SendeauswahlSetzen, 0, () => AnfrageAsync(Nachrichtentyp.SendeauswahlSetzen, new SendeauswahlSetzenAnfrage { Felder = [.. f], Global = g }));
    }

    /// <summary>PegelSetzen (4.5), zusammengefasst nach C5 je Konto und Netz.</summary>
    public Task<Antwort> PegelSetzenAsync(uint kontoId, ushort netzId, double? db) =>
        Melder.Melden(Nachrichtentyp.PegelSetzen, ((long)kontoId << 16) | netzId,
            () => AnfrageAsync(Nachrichtentyp.PegelSetzen, new PegelSetzenAnfrage { KontoId = kontoId, NetzId = netzId, Db = db }));

    /// <summary>SprechanzeigeAbo (4.5), zusammengefasst nach C5.</summary>
    public Task<Antwort> SprechanzeigeAboAsync(bool an) =>
        Melder.Melden(Nachrichtentyp.SprechanzeigeAbo, 0, () => AnfrageAsync(Nachrichtentyp.SprechanzeigeAbo, new AnAus { An = an }));

    /// <summary>FeldSetzen (4.5): offenes Netz in ein Feld, ohne <paramref name="netzId"/> leeren.</summary>
    public Task<Antwort> FeldSetzenAsync(byte feld, ushort? netzId) =>
        AnfrageAsync(Nachrichtentyp.FeldSetzen, new FeldSetzenAnfrage { Feld = feld, NetzId = netzId });

    /// <summary>
    /// RolleWaehlen (0x0039, 6.16, seit 0.6.0): die Felder der Rolle anlegen, der Platz bleibt. <paramref name="ch"/>: Wünsche je
    /// Rollenfeld, etwa der zuletzt benutzte Ch. je Kanalnetz. Einem Server vor 0.6.0 schickt der Client sie nicht (4.7).
    /// </summary>
    public Task<Antwort> RolleWaehlenAsync(ushort rolleId, IReadOnlyList<ChWunsch>? ch = null) =>
        AnfrageAsync(Nachrichtentyp.RolleWaehlen, new RolleWaehlenAnfrage { RolleId = rolleId, Ch = ch });

    /// <summary>
    /// KanalSperre (0x003A, 6.17, seit 0.6.0), nur mit <c>ZUWEISEN</c>: <paramref name="kontoId"/> 0 heißt für alle ohne
    /// <c>ZUWEISEN</c>; <paramref name="bis"/> fehlt: bis 04:00. Die Antwort nennt alle laufenden Sperren. Einem Server vor 0.6.0 schickt
    /// der Client sie nicht (4.7).
    /// </summary>
    public Task<Antwort> KanalSperreAsync(KanalSperreOperation op, uint? kontoId = null, ushort? netzId = null, long? bis = null) =>
        AnfrageAsync(Nachrichtentyp.KanalSperre, new KanalSperreAnfrage { Op = op, KontoId = kontoId, NetzId = netzId, Bis = bis });

    /// <summary>Den vollständigen Stand anfordern (höchstens einmal je Version und alle 5 s). <c>false</c>: gedrosselt.</summary>
    public bool ZustandAnfordern() => _sitzung?.ZustandAnfordern(Monouhr.Ms()) == true;

    /// <summary>Tunnel auf Wunsch des Nutzers (Grund NUTZER).</summary>
    public void TunnelNutzer(bool an) => _sitzung?.TunnelVomNutzer(an);

    // ---- Rückmeldungen der Sitzung ----

    internal void ZustandMelden(ClientZustand alt, ClientZustand neu, string? grund)
    {
        _zustand = neu;
        _wechsel.Writer.TryWrite(new Zustandswechsel(alt, neu, grund));
    }

    // Die Ereignisse laufen auf der Leseschleife der Sitzung. Eine Ausnahme im Code der Anwendung darf sie nicht beenden:
    // sonst läse niemand mehr, und die Sitzung hinge bis zur Totfrist (6 s) und liefe danach in dieselbe Falle.

    internal void CodeMelden(Registrierungscode code) => Sicher(CodeErhalten, code, "CodeErhalten");

    internal void PushMelden(PushEreignis p) => Sicher(PushErhalten, p, "PushErhalten");

    internal void FehlerMelden(string wo, Exception ex)
    {
        try
        {
            HintergrundFehler?.Invoke(this, new InvalidOperationException(wo + ": " + ex.Message, ex));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Auch ein Fehler im Fehlerbericht darf keinen Thread beenden.
        }
    }

    /// <summary>Ruft einen Ereignisbehandler auf; seine Ausnahme geht an <see cref="HintergrundFehler"/>.</summary>
    internal void Sicher<T>(EventHandler<T>? behandler, T wert, string wo)
    {
        if (behandler is null) return;
        try
        {
            behandler(this, wert);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            FehlerMelden(wo, ex);
        }
    }

    internal void AktivErreicht() => Interlocked.Increment(ref _aktivierungen);

    // ---- Verbindungsschleife (7.1) ----

    private async Task SchleifeAsync()
    {
        var plan = new Wiederverbindungsplan(_optionen.Zufall);
        var wiederverbindung = false;
        long naechster = 0;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var warten = naechster - Monouhr.Ms();
                if (warten > 0) await WartenAsync(warten).ConfigureAwait(false);
                if (_stop.IsCancellationRequested) break;
                var vomNutzer = _vomNutzer;
                _vomNutzer = false;
                if (NeuversuchGesperrt && !vomNutzer)
                {
                    // Kein automatischer Neuversuch; nur der Nutzer verbindet neu (4.6).
                    await _nutzer.WaitAsync(_stop.Token).ConfigureAwait(false);
                    naechster = 0;
                    continue;
                }
                _aufbauGesperrt = false;

                Steuerverbindung v;
                try
                {
                    v = await _optionen.Aufbau.AufbauenAsync(wiederverbindung, _stop.Token).ConfigureAwait(false);
                }
                catch (AufbauException e)
                {
                    // 4.1: Ein Pin-Fehler bei der Erstverbindung heißt "Server nicht vertrauenswürdig", ohne Ausnahmeknopf und
                    // ohne stilles Weiterprobieren. Beim Wiederverbinden (7.1) versucht der Client es dagegen weiter.
                    if (e.Art == AufbauFehler.PinAlarm && !wiederverbindung) _aufbauGesperrt = true;
                    Ende(new Sitzungsende("Aufbau: " + e.Art, null, null, null, null, null, e.Art, e.Message, wiederverbindung));
                    naechster = plan.Fehlschlag(Monouhr.Ms());
                    continue;
                }

                Messwerte = new Messwerte();
                Uhr.Zuruecksetzen();
                var s = new Sitzung(this, _optionen, v, _kanal, Messwerte);
                Sitzungsende ende;
                await using (s.ConfigureAwait(false))
                {
                    _sitzung = s;
                    var aktivVorher = Aktivierungen;
                    var lauf = s.LaufenAsync(vomNutzer);
                    ende = await lauf.ConfigureAwait(false);
                    if (Aktivierungen > aktivVorher) plan.Erfolg();
                    _sitzung = null;
                }
                Melder.Abbrechen(new VerbindungGetrenntException());
                Lage.Zuruecksetzen();
                Ende(ende);
                wiederverbindung = true;
                if (!_optionen.Wiederverbinden) break;
                naechster = ende.Bye switch
                {
                    ByeGrund.Wartung => plan.Verloren(Monouhr.Ms(), ende.AktivDauerMs, wartungInMs: ende.WiederInMs ?? Protokoll.Steuerung.Fristen.WartezeitWiederInMs),
                    ByeGrund.Wartezeit => plan.Verloren(Monouhr.Ms(), null, wartezeitInMs: ende.WiederInMs ?? Protokoll.Steuerung.Fristen.WartezeitWiederInMs),
                    _ => ende.AktivDauerMs is not null ? plan.Verloren(Monouhr.Ms(), ende.AktivDauerMs) : plan.Fehlschlag(Monouhr.Ms()),
                };
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                FehlerMelden("Verbindungsschleife", ex);
                naechster = plan.Fehlschlag(Monouhr.Ms());
            }
        }
        if (_zustand != ClientZustand.Getrennt) ZustandMelden(_zustand, ClientZustand.Getrennt, "Beendet");
    }

    private async Task WartenAsync(long ms)
    {
        using var unterbrechen = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var nutzer = _nutzer.WaitAsync(unterbrechen.Token);
        var zeit = Task.Delay(TimeSpan.FromMilliseconds(ms), unterbrechen.Token);
        await Task.WhenAny(nutzer, zeit).ConfigureAwait(false);
        await unterbrechen.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(nutzer, zeit).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Ende(Sitzungsende ende)
    {
        LetztesEnde = ende;
        // Sonst verschöbe eine Ausnahme der Anwendung den Plan (Fehlschlag statt WARTUNG oder Schnellphase).
        Sicher(SitzungBeendet, ende, "SitzungBeendet");
    }

    /// <summary>Geordnet beenden: Bye{BEENDET}, keine Wiederverbindung.</summary>
    public async Task BeendenAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _sitzung?.NutzerBeendet();
        if (_schleife is not null)
        {
            try
            {
                await _schleife.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await BeendenAsync().ConfigureAwait(false);
        _wechsel.Writer.TryComplete();
        await _melden.ConfigureAwait(false);
        _stop.Dispose();
        _nutzer.Dispose();
    }
}
