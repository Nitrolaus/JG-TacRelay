// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>
/// Eine Verbindung des Clients vom Hello bis zum Ende (4.6): Leseschleife mit <see cref="ClientKanal"/> (Automat,
/// Anfrage-Nummern, Body-Prüfung), Schreibschleife mit zwei Warteschlangen (Steuerrahmen vor Tunnel-Sprache, 60 ms,
/// 3 je Absender; ein Write je Rahmen), Takt alle 50 ms (Ping alle 2 s ab Hello, Totfrist 6 s, Bindungsfrist 3 s,
/// Sprachweg, EmpfangsBericht, C5, C8). Registrierung, Rückfrage, VoiceKeys, Sync, UDP-Bindung und Tunnel.
/// Der Kanal wird nur unter <see cref="_sperre"/> benutzt.
/// </summary>
internal sealed class Sitzung : IAsyncDisposable
{
    private const long TaktMs = 50;
    private const long C8WartenMs = 500;
    private const long ZustandAbstandMs = 5000;

    private readonly FunkClient _client;
    private readonly FunkClientOptionen _optionen;
    private readonly Steuerverbindung _verbindung;
    private readonly ClientKanal _kanal;
    private readonly Lock _sperre = new();
    private readonly Sendewarteschlange<Ausgehend> _warte = new(static a => a.Freigeben());
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly SemaphoreSlim _anfrageFrei = new(0, int.MaxValue);
    private readonly Dictionary<uint, TaskCompletionSource<Antwort>> _offen = [];
    private readonly CancellationTokenSource _ende = new();
    private readonly TaskCompletionSource<Sitzungsende> _beendet = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<ushort, long> _c8 = new();
    private Sprachweg? _sprache;
    private volatile ClientZustand _zustand;
    private bool _geschlossen;
    private bool _helloGesendet;
    private Sitzungsende? _schliessGrund;
    private long _aktivSeitMs = -1;
    private int _codes;
    private long _codeGueltigBis;
    private uint _pingNr;

    /// <summary>Sendezeit (monotone ms) je offener Ping-Nummer, für den Uhrabgleich (<see cref="FunkClient.Uhr"/>).</summary>
    private readonly ConcurrentDictionary<uint, long> _pingGesendet = new();
    private long _letzterBerichtMs;
    private long _letzteZustandsAnfrageMs = Monouhr.Nie;
    private long _zustandAngefordertFuer = -1;
    private bool _tunnelVomNutzer;

    public Sitzung(FunkClient client, FunkClientOptionen optionen, Steuerverbindung verbindung, ClientKanal kanal, Messwerte messwerte)
    {
        _client = client;
        _optionen = optionen;
        _verbindung = verbindung;
        _kanal = kanal;
        Messwerte = messwerte;
    }

    public Messwerte Messwerte { get; }

    public ClientZustand Zustand => _zustand;

    public Sprachweg? Sprache => _sprache;

    public bool Tunnel => _sprache?.Tunnel == true;

    /// <summary>Unerwartete Ausnahme aus einem Thread der Sitzung an <see cref="FunkClient.HintergrundFehler"/>.</summary>
    public void FehlerMelden(string wo, Exception ex) => _client.FehlerMelden(wo, ex);

    // ---- Lebenslauf ----

    /// <summary>Hello senden und die Verbindung bedienen, bis sie endet.</summary>
    public async Task<Sitzungsende> LaufenAsync(bool vomNutzer)
    {
        lock (_sperre)
        {
            var u = _kanal.Verbinden(Monouhr.Ms(), vomNutzer);
            ZustandVeroeffentlichen("Verbinden");
        }
        var schreiben = Task.Run(SchreibschleifeAsync);
        var lesen = Task.Run(LeseschleifeAsync);
        var takt = Task.Run(TaktschleifeAsync);
        lock (_sperre)
        {
            JsonEinreihen(Nachrichtentyp.Hello, 0, 0, new Hello
            {
                Minor = Versionen.Nebenversion,
                Client = new ClientAngabe { Name = _optionen.ClientName, Version = _optionen.ClientVersion },
                SchluesselArt = _optionen.Identitaet.Art,
                Opus = _optionen.OpusVersion,
                Faehigkeiten = ["tunnel"],
            });
            _helloGesendet = true;
        }
        Wecken();

        var ende = await _beendet.Task.ConfigureAwait(false);
        await _ende.CancelAsync().ConfigureAwait(false);
        Wecken();
        await Leise(schreiben).ConfigureAwait(false);
        await Leise(lesen).ConfigureAwait(false);
        await Leise(takt).ConfigureAwait(false);
        Aufraeumen();
        return ende;
    }

    private static async Task Leise(Task t)
    {
        try
        {
            await t.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or AuthenticationException or TimeoutException)
        {
        }
    }

    private void Aufraeumen()
    {
        _client.Sprechen.SitzungEndet();
        _sprache?.Dispose();
        List<TaskCompletionSource<Antwort>> offen;
        lock (_sperre)
        {
            _geschlossen = true;
            offen = [.. _offen.Values];
            _offen.Clear();
            if (_kanal.Automat.Zustand != ClientZustand.Getrennt) _kanal.Automat.Verbindungsverlust(Monouhr.Ms());
            ZustandVeroeffentlichen(_beendet.Task.Result.Grund);
            while (_warte.VersucheEntnehmen(long.MaxValue, out var a)) a.Freigeben();
        }
        var grund = new VerbindungGetrenntException();
        foreach (var t in offen) t.TrySetException(grund);
        try
        {
            _anfrageFrei.Release(64);
        }
        catch (SemaphoreFullException)
        {
        }
    }

    /// <summary>Beendet die Sitzung sofort mit <paramref name="ende"/>.</summary>
    private void Beenden(Sitzungsende ende)
    {
        if (!_beendet.TrySetResult(ende)) return;
        try
        {
            _ende.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        Wecken();
    }

    /// <summary>Sendet optional <c>Fehler</c> und <c>Bye</c>, dann TLS close_notify und Trennung.</summary>
    private void Schliessen(Sitzungsende ende, Fehlercode? fehler = null, ByeGrund? bye = null, string? text = null)
    {
        lock (_sperre)
        {
            if (_geschlossen) return;
            _geschlossen = true;
            _schliessGrund = ende;
            if (fehler is { } f) JsonEinreihen(Nachrichtentyp.Fehler, 0, 0, new Fehler { Code = f, Text = text });
            if (bye is { } b) JsonEinreihen(Nachrichtentyp.Bye, 0, 0, new Bye { Grund = b });
            _warte.SteuerEinreihen(new Ausgehend { Schliessen = true });
        }
        Wecken();
        // Hängt die Gegenseite, begrenzt die Frist das Warten auf den Abschied.
        _ = Task.Delay(2000).ContinueWith(_ => Beenden(ende), TaskScheduler.Default);
    }

    /// <summary>Der Nutzer beendet: Bye{BEENDET}.</summary>
    public void NutzerBeendet() => Schliessen(new Sitzungsende("Beendet", ByeGrund.Beendet, null, null, AktivDauer(), null), bye: ByeGrund.Beendet);

    /// <summary>Neue Sitzung nötig (5.8: harte Grenze länger als 5 s): Bye{BEENDET}, dann Wiederverbindung.</summary>
    public void NeueSitzung(string grund) => Schliessen(new Sitzungsende(grund, null, null, null, AktivDauer(), null), bye: ByeGrund.Beendet);

    private long? AktivDauer() => _aktivSeitMs < 0 ? null : Monouhr.Ms() - _aktivSeitMs;

    public async ValueTask DisposeAsync()
    {
        Beenden(new Sitzungsende("Verworfen", null, null, null, AktivDauer(), null));
        await _verbindung.DisposeAsync().ConfigureAwait(false);
        _ende.Dispose();
        _signal.Dispose();
        _anfrageFrei.Dispose();
    }

    /// <summary>Nur unter <see cref="_sperre"/>: macht den Zustand des Automaten sichtbar (Quelle ist immer der Automat).</summary>
    private void ZustandVeroeffentlichen(string? grund)
    {
        var alt = _zustand;
        var neu = _kanal.Automat.Zustand;
        _zustand = neu;
        if (alt != neu) _client.ZustandMelden(alt, neu, grund);
    }

    // ---- Schreiben ----

    private sealed class Ausgehend
    {
        public Func<RahmenSchreiber, CancellationToken, ValueTask>? Json;
        public byte[]? Roh;
        public int Laenge;
        public ushort Typ;
        public byte Flags;
        public uint Nr;
        public bool Schliessen;

        public ValueTask SchreibenAsync(RahmenSchreiber w, CancellationToken ct) =>
            Json is not null ? Json(w, ct) : w.SchreibenAsync(Typ, Flags, Nr, Roh.AsMemory(0, Laenge), ct);

        /// <summary>Tunnelrahmen tragen nur Chiffrat; Schlüssel gehen vom Client nie hinaus, also nichts zu nullen.</summary>
        public void Freigeben() => Roh = null;
    }

    /// <summary>Nur unter <see cref="_sperre"/>.</summary>
    private void JsonEinreihen<T>(Nachrichtentyp typ, byte flags, uint nr, T body) where T : class =>
        _warte.SteuerEinreihen(new Ausgehend { Json = (w, ct) => w.SchreibenAsync(typ, flags, nr, body, ct), Typ = (ushort)typ });

    private void Wecken()
    {
        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task SchreibschleifeAsync()
    {
        using var schreiber = new RahmenSchreiber(_verbindung.Strom);
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_ende.Token).ConfigureAwait(false);
                while (true)
                {
                    Ausgehend? a;
                    lock (_sperre)
                    {
                        if (!_warte.VersucheEntnehmen(Monouhr.Ms(), out a)) break;
                    }
                    if (a.Schliessen)
                    {
                        await AbschliessenAsync().ConfigureAwait(false);
                        Beenden(_schliessGrund ?? new Sitzungsende("Geschlossen", null, null, null, AktivDauer(), null));
                        return;
                    }
                    try
                    {
                        await a.SchreibenAsync(schreiber, _ende.Token).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                        // Rahmen über 1 MiB, vom Schreiber verweigert; nichts ging hinaus.
                    }
                    finally
                    {
                        a.Freigeben();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or AuthenticationException or NotSupportedException)
        {
            Beenden(new Sitzungsende("Schreibfehler: " + ex.GetType().Name, null, null, null, AktivDauer(), null));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _client.FehlerMelden("Schreibschleife", ex);
            Beenden(new Sitzungsende("Interner Fehler: " + ex.GetType().Name, null, null, null, AktivDauer(), null));
        }
    }

    private async Task AbschliessenAsync()
    {
        if (_verbindung.Strom is SslStream ssl)
        {
            try
            {
                using var frist = new CancellationTokenSource(1000);
                await ssl.ShutdownAsync().WaitAsync(frist.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or TimeoutException or InvalidOperationException)
            {
            }
        }
        try
        {
            await _verbindung.Strom.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
        {
        }
    }

    // ---- Anfragen ----

    /// <summary>Stellt eine Anfrage und wartet auf ihre Antwort (derselbe Typ oder Fehler). Höchstens 32 offen (4.2).</summary>
    public async Task<Antwort> AnfrageAsync<T>(Nachrichtentyp typ, T body, CancellationToken abbruch = default) where T : class
    {
        TaskCompletionSource<Antwort> tcs;
        while (true)
        {
            lock (_sperre)
            {
                if (_geschlossen || _beendet.Task.IsCompleted) throw new VerbindungGetrenntException();
                if (_kanal.VersucheAnfrageStellen(typ, Monouhr.Ms(), out var nr))
                {
                    tcs = new TaskCompletionSource<Antwort>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _offen[nr] = tcs;
                    JsonEinreihen(typ, 0, nr, body);
                    break;
                }
            }
            await _anfrageFrei.WaitAsync(abbruch).ConfigureAwait(false);
        }
        Wecken();
        return await tcs.Task.WaitAsync(abbruch).ConfigureAwait(false);
    }

    /// <summary>Push vom Client (etwa EmpfangsBericht).</summary>
    public void Push<T>(Nachrichtentyp typ, T body) where T : class
    {
        lock (_sperre)
        {
            if (_geschlossen) return;
            JsonEinreihen(typ, 0, 0, body);
        }
        Wecken();
    }

    private void Antworten<T>(Nachrichtentyp typ, uint nr, T body) where T : class
    {
        lock (_sperre)
        {
            if (_geschlossen) return;
            JsonEinreihen(typ, Rahmen.FlagAntwort, nr, body);
            _kanal.AntwortGesendet(nr);
        }
        Wecken();
    }

    /// <summary>SpracheTunnel hoch (5.12): höchstens 3 in der Warteschlange, älter als 60 ms wird verworfen.</summary>
    public void TunnelHoch(byte[] datagramm)
    {
        lock (_sperre)
        {
            if (_geschlossen) return;
            _warte.TunnelEinreihen(0, new Ausgehend { Roh = datagramm, Laenge = datagramm.Length, Typ = (ushort)Nachrichtentyp.SpracheTunnel }, Monouhr.Ms());
        }
        Wecken();
    }

    // ---- Lesen ----

    private async Task LeseschleifeAsync()
    {
        using var leser = new RahmenLeser(_verbindung.Strom);
        try
        {
            while (!_ende.IsCancellationRequested)
            {
                var r = await leser.LesenAsync(ClientKanal.Rahmengrenze, _ende.Token).ConfigureAwait(false);
                if (r.Status is LeseStatus.Ende or LeseStatus.Abgebrochen)
                {
                    Beenden(_schliessGrund ?? new Sitzungsende("Verbindung geschlossen", null, null, null, AktivDauer(), null));
                    return;
                }
                if (r.Status == LeseStatus.Fehler)
                {
                    Schliessen(new Sitzungsende("Rahmenfehler", null, null, r.Fehler, AktivDauer(), null), r.Fehler);
                    return;
                }
                RahmenVerarbeiten(r.Kopf, r.Body.Span, leser);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or AuthenticationException)
        {
            Beenden(_schliessGrund ?? new Sitzungsende("Verbindungsverlust", null, null, null, AktivDauer(), null));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ohne diesen Fang endete die Leseschleife still: niemand läse mehr, die Sitzung hinge bis zur Totfrist.
            // So endet sie sofort, und die Wiederverbindung (7.1) greift.
            _client.FehlerMelden("Leseschleife", ex);
            Schliessen(new Sitzungsende("Interner Fehler: " + ex.GetType().Name, null, null, null, AktivDauer(), null));
        }
    }

    private void RahmenVerarbeiten(RahmenKopf kopf, ReadOnlySpan<byte> body, RahmenLeser leser)
    {
        var jetzt = Monouhr.Ms();
        Eingang e;
        ClientUebergang? u;
        lock (_sperre)
        {
            if (_geschlossen) return;
            e = _kanal.Empfangen(kopf, body, jetzt);
            u = _kanal.LetzterUebergang;
        }
        if (e.Trennen)
        {
            Schliessen(new Sitzungsende("Protokollfehler vom Server", null, null, e.Fehler, AktivDauer(), null), e.Fehler, text: e.Feld);
            return;
        }
        switch (e.Status)
        {
            case EingangsStatus.Uebergehen:
                return;
            case EingangsStatus.Fehler:
                // UNGUELTIG ohne Trennung (4.3): eine Anfrage bekommt Fehler, eine ungültige Antwort erledigt die eigene Anfrage.
                if (kopf.Art == Nachrichtenart.Anfrage) Antworten(Nachrichtentyp.Fehler, kopf.AnfrageNr, new Fehler { Code = e.Fehler, BezugTyp = kopf.Typ, Text = e.Feld });
                else if (kopf.Art == Nachrichtenart.Antwort) Aufloesen(kopf.AnfrageNr, new Antwort((Nachrichtentyp)kopf.Typ, null, new Fehler { Code = e.Fehler, Text = "Antwort ungültig" }));
                return;
        }

        if (kopf.Art == Nachrichtenart.Antwort)
        {
            if (e.Body is PingAntwort p)
            {
                _sprache?.PingAntwort(p.UdpGesendet, p.UdpEmpfangen);
                // Uhrabgleich (0.12.0): Empfangszeit gleich beim Lesen des Rahmens, nicht erst nach dem Sprung in die wartende Task.
                if (_pingGesendet.TryRemove(p.Nr, out var gesendet)) _client.Uhr.Probe(gesendet, jetzt, p.ServerZeit);
            }
            Aufloesen(kopf.AnfrageNr, new Antwort((Nachrichtentyp)kopf.Typ, e.Body, e.Body as Fehler));
        }
        else
        {
            Annehmen(kopf, e, body, leser);
        }
        if (u is { } ue) Uebergang(ue, e.Body);
    }

    private void Aufloesen(uint nr, Antwort a)
    {
        TaskCompletionSource<Antwort>? tcs;
        lock (_sperre) _offen.Remove(nr, out tcs);
        tcs?.TrySetResult(a);
        try
        {
            _anfrageFrei.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Annehmen(RahmenKopf kopf, Eingang e, ReadOnlySpan<byte> body, RahmenLeser leser)
    {
        var lage = _client.Lage;
        switch ((Nachrichtentyp)kopf.Typ)
        {
            case Nachrichtentyp.VoiceKeys:
                VoiceKeysAnnehmen(kopf.AnfrageNr, body, leser);
                return;
            case Nachrichtentyp.SpracheTunnel:
                _sprache?.TunnelEingang(body);
                return;
            case Nachrichtentyp.VerknuepfungBestaetigen:
                RueckfrageBeantworten(kopf.AnfrageNr, ((VerknuepfungBestaetigenAnfrage)e.Body!).Konto);
                return;
        }
        switch (e.Body)
        {
            case Welcome w:
                if (w.Status == WelcomeStatus.Freigegeben)
                {
                    lage.WelcomeSetzen(w);
                    if (w.ServerZeit is { } serverZeit) _client.Uhr.Grob(serverZeit, Monouhr.Ms());
                }
                break;
            case NetzListe n:
                lage.NetzListeSetzen(n);
                break;
            case Mitgliedschaften m:
                var rolleVorher = lage.Mitgliedschaften?.Rolle;
                lage.MitgliedschaftenSetzen(m);
                // 6.9: Nach einer Umstellung (Standard, Rolle, Verschieben, Aufstellung, Wiederherstellen, Satz) gilt die
                // Sendeauswahl des Servers; niemand muss sein Sendenetz suchen. Nach eigenen Änderungen (SELBST) bleibt die
                // lokale Auswahl, sonst überholte ein älterer Stand des Servers einen gerade gewählten (C1). Ein Phasenwechsel
                // (6.14 Regel 7, phase gesetzt) lässt die Quelle stehen, stellt aber Felder und Sendeauswahl um: Dann gilt die des
                // Servers auch bei SELBST, sonst zeigte die lokale Auswahl auf ein Feld, das in der Phase nicht mehr senden darf.
                // Ebenso eine neu gewählte Rolle (RolleWaehlen, 6.16, seit 0.6.0): Quelle SELBST, aber alle Felder neu, und der
                // Sendevorschlag der Rolle gilt.
                var rolleNeu = m.Rolle is { } gewaehlteRolle && gewaehlteRolle != rolleVorher;
                if (m.Quelle != MitgliedschaftsQuelle.Selbst || m.Phase is not null || rolleNeu) _client.Sprechen.AuswahlSetzen(m.Sendeauswahl.Felder, m.Sendeauswahl.Global);
                _client.Sprechen.MitgliedschaftenGeaendert(m, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                break;
            case Praesenz p:
                lage.PraesenzAnwenden(p);
                break;
            case PegelVoreinstellungen pv:
                lage.PegelSetzen(pv);
                break;
            case Belegung b:
                lage.BelegungSetzen(b);
                break;
            case PhaseGeschaltet ph:
                lage.PhaseSetzen(ph);
                break;
            case Meldestand ms:
                lage.MeldestandSetzen(ms);
                break;
            case BereitStand bs:
                lage.BereitStandSetzen(bs);
                break;
            case ZustandsVersionBody z:
                lage.Vollstaendigkeit(z.ZustandsVersion);
                break;
            case UdpStatus { Zustand: UdpZustand.Gebunden }:
                _sprache?.GebundenGemeldet();
                lock (_sperre)
                {
                    var u = _kanal.Automat.UdpGebunden(Monouhr.Ms());
                    ZustandVeroeffentlichen("UDP gebunden");
                }
                break;
            case VerlustHinweis vh:
                Messwerte.VerlustHinweisSetzen(vh.Prozent);
                break;
        }
        if (e.Body is not null) _client.PushMelden(new PushEreignis((Nachrichtentyp)kopf.Typ, e.Body));
    }

    /// <summary>
    /// VoiceKeys (4.5, 5.8): START nur in SYNC mit Epoche 0, ROTATION nur in AKTIV mit der nächsten Epoche. Schlüssel
    /// übernehmen, den Empfangspuffer sofort nullen, dann mit der installierten Epoche antworten.
    /// </summary>
    private void VoiceKeysAnnehmen(uint nr, ReadOnlySpan<byte> body, RahmenLeser leser)
    {
        var ansicht = VoiceKeys.Lesen(body);
        var epoche = ansicht.Epoche;
        var grund = ansicht.Grund;
        var ok = false;
        try
        {
            var zustand = _zustand;
            if (grund == VoiceKeysGrund.Start && zustand == ClientZustand.Sync && _sprache is null && _client.Lage.Welcome is { Udp: { } udp } && _client.Lage.SitzungsId != 0)
            {
                var weg = new Sprachweg(this, _optionen, Messwerte, _client.Lage.SitzungsId, new IPEndPoint(_verbindung.ServerIp, udp.Port), _verbindung.LokaleIp);
                ok = weg.SchluesselInstallieren(epoche, grund, body.Slice(2, Schluesselmaterial.Laenge));
                if (ok)
                {
                    weg.UdpSperre = _client.UdpSperre;
                    _sprache = weg;
                    _client.Sprechen.SitzungBeginnt(weg);
                }
                else weg.Dispose();
            }
            else if (grund == VoiceKeysGrund.Rotation && zustand is ClientZustand.AktivUdpBinden or ClientZustand.AktivUdp or ClientZustand.AktivTunnel && _sprache is not null)
            {
                ok = _sprache.SchluesselInstallieren(epoche, grund, body.Slice(2, Schluesselmaterial.Laenge));
            }
        }
        finally
        {
            leser.BodyVerwerfen();
        }
        if (!ok)
        {
            Schliessen(new Sitzungsende("VoiceKeys unzulässig", null, null, Fehlercode.Protokoll, AktivDauer(), null), Fehlercode.Protokoll);
            return;
        }
        lock (_sperre)
        {
            if (_geschlossen) return;
            _warte.SteuerEinreihen(new Ausgehend { Roh = [epoche], Laenge = 1, Typ = (ushort)Nachrichtentyp.VoiceKeys, Flags = Rahmen.FlagAntwort, Nr = nr });
            _kanal.AntwortGesendet(nr);
        }
        Wecken();
    }

    private void RueckfrageBeantworten(uint nr, VerknuepfungKonto konto) => _ = Task.Run(async () =>
    {
        var ja = false;
        try
        {
            using var frist = CancellationTokenSource.CreateLinkedTokenSource(_ende.Token);
            frist.CancelAfter(TimeSpan.FromSeconds(115));
            if (_optionen.Rueckfrage is { } f) ja = await f(konto, frist.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ja = false;
        }
        Antworten(Nachrichtentyp.VerknuepfungBestaetigen, nr, new VerknuepfungBestaetigenAntwort { Ja = ja });
    });

    // ---- Übergänge ----

    private void Uebergang(ClientUebergang u, object? body)
    {
        // Den Weg der Sprache umstellen, bevor der neue Zustand sichtbar wird: Wer AKTIV/TUNNEL liest, sieht auch den Tunnel.
        if (u.TunnelAn is { } weg) _sprache?.TunnelSetzen(weg, u.TunnelGrund ?? TunnelGrund.Nutzer);
        lock (_sperre) ZustandVeroeffentlichen(body?.GetType().Name);
        if (u.NeuenCodeAnbieten) _ = RegistrierenAsync();
        if (!u.Geaendert && u.TunnelAn is null) return;
        switch (u.Nach)
        {
            case ClientZustand.Getrennt:
                var bye = body as Bye;
                var fehler = body as Fehler;
                Schliessen(new Sitzungsende(bye is not null ? "Bye " + bye.Grund : fehler is not null ? "Fehler " + Fehlercodes.Drahtname(fehler.Code) : "Getrennt",
                    bye?.Grund, bye?.WiederInMs, fehler?.Code, AktivDauer(), bye?.Durch));
                return;
            case ClientZustand.Registrierung when u.Von == ClientZustand.Verbinden:
                if (_optionen.AutomatischRegistrieren) _ = RegistrierenAsync();
                return;
            case ClientZustand.AktivUdpBinden:
                _aktivSeitMs = Monouhr.Ms();
                if (_sprache is null)
                {
                    Schliessen(new Sitzungsende("AKTIV ohne VoiceKeys", null, null, Fehlercode.Protokoll, null, null), Fehlercode.Protokoll);
                    return;
                }
                _client.AktivErreicht();
                try
                {
                    _sprache.Starten();
                }
                catch (System.Net.Sockets.SocketException)
                {
                    // Kein UDP möglich: die Bindungsfrist (3 s) schaltet den Tunnel ein.
                }
                return;
        }
        if (u.TunnelAn is { } an) _ = TunnelModusAsync(an, u.TunnelGrund ?? TunnelGrund.Nutzer);
    }

    private async Task RegistrierenAsync()
    {
        if (Interlocked.Increment(ref _codes) > 3) return;
        try
        {
            var n = _optionen.Anmeldung;
            if (n is null && _optionen.AnmeldungFragen is { } fragen)
            {
                // Erst die Namen bestätigen lassen (A42); ohne Antwort kein Antrag, dann läuft die REGISTRIERUNG ab (4.6).
                using var frist = CancellationTokenSource.CreateLinkedTokenSource(_ende.Token);
                frist.CancelAfter(_optionen.AnmeldungFrist);
                try
                {
                    n = await fragen(frist.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Frist, Trennung oder ein Fehler in der Oberfläche: kein Antrag; nur der Fehler wird gemeldet.
                    if (ex is not OperationCanceledException) _client.FehlerMelden("AnmeldungFragen", ex);
                    n = null;
                }
                if (n is null) return;
            }
            var a = await AnfrageAsync(Nachrichtentyp.Registrierung, new RegistrierungAnfrage
            {
                Geraetename = _optionen.Geraetename,
                ScName = n?.ScName,
                DiscordName = n?.DiscordName,
            }).ConfigureAwait(false);
            // Beim Antrag (3.4, P3) gibt es keinen Code; GeraetStatus{WARTET} folgt und führt in den WARTERAUM.
            if (a.Body is RegistrierungAntwort { Code: { } code, GueltigBis: { } bis, Befehl: { } befehl })
            {
                Interlocked.Exchange(ref _codeGueltigBis, bis);
                _client.CodeMelden(new Registrierungscode(code, bis, befehl));
            }
        }
        catch (Exception ex) when (ex is VerbindungGetrenntException or OperationCanceledException)
        {
        }
    }

    // ---- Tunnel (5.12) ----

    /// <summary>
    /// Schaltet den Tunnel (nur der Client schaltet, 5.12): erst den Weg der Sprache, dann den Zustand sichtbar machen, dann
    /// TunnelModus melden. <paramref name="uebergang"/> ist die Aktion am Automaten.
    /// </summary>
    private void TunnelSchalten(Func<long, ClientUebergang> uebergang, TunnelGrund grund, string anlass)
    {
        ClientUebergang u;
        lock (_sperre) u = uebergang(Monouhr.Ms());
        if (u.TunnelAn is { } an) _sprache?.TunnelSetzen(an, u.TunnelGrund ?? grund);
        lock (_sperre) ZustandVeroeffentlichen(anlass);
        if (u.TunnelAn is { } an2) _ = TunnelModusAsync(an2, u.TunnelGrund ?? grund);
    }

    private async Task TunnelModusAsync(bool an, TunnelGrund grund)
    {
        try
        {
            await AnfrageAsync(Nachrichtentyp.TunnelModus, new TunnelModusAnfrage { An = an, Grund = grund }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is VerbindungGetrenntException or OperationCanceledException)
        {
        }
    }

    /// <summary>Vom Sprachweg: UDP-Ausfall erkannt.</summary>
    public void UdpAusfall() => TunnelSchalten(_kanal.Automat.UdpAusfall, TunnelGrund.UdpAusfall, "UDP-Ausfall");

    /// <summary>Vom Sprachweg: die UDP-Probe war 10 s sauber.</summary>
    public void ProbeSauber() => TunnelSchalten(_kanal.Automat.TunnelProbeSauber, TunnelGrund.UdpZurueck, "UDP zurück");

    /// <summary>Der Nutzer schaltet den Tunnel ein oder aus (Grund NUTZER).</summary>
    public void TunnelVomNutzer(bool an)
    {
        _tunnelVomNutzer = an;
        // Aus dem Tunnel zurück geht es wie nach einer sauberen Probe, nur mit Grund NUTZER.
        TunnelSchalten(jetzt => an ? _kanal.Automat.TunnelVomNutzer(jetzt) : _kanal.Automat.TunnelProbeSauber(jetzt) with { TunnelGrund = TunnelGrund.Nutzer },
            TunnelGrund.Nutzer, "Tunnel vom Nutzer");
    }

    public bool TunnelNutzer => _tunnelVomNutzer;

    public void KeyRotationAnfordern(byte epoche) => _ = RotationAsync(epoche);

    private async Task RotationAsync(byte epoche)
    {
        try
        {
            await AnfrageAsync(Nachrichtentyp.KeyRotation, new KeyRotationAnfrage { Epoche = epoche, Grund = RotationsGrund.Zaehler }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is VerbindungGetrenntException or OperationCanceledException)
        {
        }
    }

    // ---- C8: Netze im Stempel ----

    /// <summary>Vom Empfang: ein gestempeltes Netz. Unbekannt oder in keinem eigenen Feld: später Zustand anfordern (C8).</summary>
    public void NetzImStempel(ushort netz, Spruchart art)
    {
        if (netz == Sprachkanal.GlobalNetz || art == Spruchart.Global && netz == Sprachkanal.GlobalNetz) return;
        var lage = _client.Lage;
        if (lage.NetzVon(netz) is not null && lage.FeldMitNetz(netz) is not null) return;
        _c8.TryAdd(netz, Monouhr.Ms());
    }

    private void C8Takt(long jetzt)
    {
        if (_c8.IsEmpty) return;
        var lage = _client.Lage;
        foreach (var (netz, seit) in _c8)
        {
            if (lage.FeldMitNetz(netz) is not null)
            {
                _c8.TryRemove(netz, out _);
                continue;
            }
            // Unbekannte Netz-ID sofort, sonst bis zu 500 ms auf den Mitgliedschaften-Push warten (C8).
            if (lage.NetzVon(netz) is null || jetzt - seit >= C8WartenMs)
            {
                _c8.TryRemove(netz, out _);
                ZustandAnfordern(jetzt);
            }
        }
    }

    /// <summary>Zustand anfordern, höchstens einmal je Version und höchstens alle 5 s (4.5, C8).</summary>
    public bool ZustandAnfordern(long jetzt)
    {
        var v = _client.Lage.ZustandsVersion;
        lock (_sperre)
        {
            if (v == _zustandAngefordertFuer || jetzt - _letzteZustandsAnfrageMs < ZustandAbstandMs) return false;
            _zustandAngefordertFuer = v;
            _letzteZustandsAnfrageMs = jetzt;
        }
        _ = ZustandAsync();
        return true;
    }

    private async Task ZustandAsync()
    {
        try
        {
            await AnfrageAsync(Nachrichtentyp.Zustand, LeererBody.Wert).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is VerbindungGetrenntException or OperationCanceledException)
        {
        }
    }

    // ---- Takt ----

    private async Task TaktschleifeAsync()
    {
        using var takt = new PeriodicTimer(TimeSpan.FromMilliseconds(TaktMs));
        try
        {
            while (await takt.WaitForNextTickAsync(_ende.Token).ConfigureAwait(false))
            {
                try
                {
                    Takt(Monouhr.Ms());
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _client.FehlerMelden("Takt", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Takt(long jetzt)
    {
        bool ping;
        ClientUebergang u;
        lock (_sperre)
        {
            if (_geschlossen) return;
            ping = _helloGesendet && _kanal.Automat.PingFaellig(jetzt);
            if (ping) _kanal.Automat.PingGesendet(jetzt);
            u = _kanal.Automat.Ticken(jetzt);
        }
        if (ping) _ = PingAsync();
        if (u.Geaendert)
        {
            if (u.Nach == ClientZustand.Getrennt)
            {
                lock (_sperre) ZustandVeroeffentlichen("6 s ohne Ping-Antwort");
                Schliessen(new Sitzungsende("6 s ohne Ping-Antwort", null, null, null, AktivDauer(), null));
                return;
            }
            Uebergang(u, null);
        }
        _sprache?.Takt(jetzt);
        _client.Melder.Takt();
        C8Takt(jetzt);

        var zustand = _zustand;
        if (zustand == ClientZustand.Registrierung && _optionen.AutomatischRegistrieren)
        {
            var bis = Interlocked.Read(ref _codeGueltigBis);
            if (bis > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > bis)
            {
                Interlocked.Exchange(ref _codeGueltigBis, 0);
                _ = RegistrierenAsync();
            }
        }
        if (zustand is ClientZustand.AktivUdp or ClientZustand.AktivTunnel or ClientZustand.AktivUdpBinden)
        {
            if (_letzterBerichtMs == 0) _letzterBerichtMs = jetzt;
            var abstand = (long)_optionen.BerichtAbstand.TotalMilliseconds;
            if (jetzt - _letzterBerichtMs >= abstand)
            {
                var zeitraum = (ushort)Math.Min(jetzt - _letzterBerichtMs, ushort.MaxValue);
                _letzterBerichtMs = jetzt;
                var k = _optionen.Kennzahlen;
                Push(Nachrichtentyp.EmpfangsBericht, Messwerte.Bericht(zeitraum, Tunnel ? Verbindungsweg.Tunnel : Verbindungsweg.Udp,
                    k?.ZielVerzoegerungMs ?? 0, k?.SpaetVerworfenAbholen() ?? 0, k?.UnterlaeufeAbholen() ?? 0));
            }
        }
    }

    private async Task PingAsync()
    {
        try
        {
            var nr = Interlocked.Increment(ref _pingNr);
            // Höchstens eine Handvoll offen (Ping alle 2 s, tot nach 6 s); was nie beantwortet wurde, fällt hier heraus.
            if (_pingGesendet.Count > 16) _pingGesendet.Clear();
            _pingGesendet[nr] = Monouhr.Ms();
            await AnfrageAsync(Nachrichtentyp.Ping, new PingAnfrage { Nr = nr }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is VerbindungGetrenntException or OperationCanceledException)
        {
        }
    }
}
