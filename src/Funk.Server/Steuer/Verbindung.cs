// JG TacRelay - Maintainer: Sarevoc
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading.Channels;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using Funk.Server.Sprachpfad;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Server.Steuer;

/// <summary>Warum eine Verbindung wegen eines Verstoßes endete (für die Sperrliste, A51).</summary>
public enum Verstossart
{
    /// <summary>Rahmen, JSON oder Zustand verletzt, oder kein Hello in der Frist.</summary>
    Protokoll,

    /// <summary>Mehr als 50 Verstöße gegen die Ratengrenzen in 60 s (4.8).</summary>
    Ratengrenze,
}

/// <summary>
/// Eine Verbindung im Steuerkanal nach dem TLS-Handshake (4.2, 4.6): Leseschleife mit Zustandsautomat, Anfrage-Nummern,
/// Body-Prüfung und Ratengrenzen (4.8); Schreibschleife mit zwei Warteschlangen (Steuerrahmen vor Tunnel-Sprache, 60 ms,
/// 3 je Absender) und genau einem Write je Rahmen; dazu Befehle von Zentrale und Relais. Der Zustand der Verbindung wird
/// nur unter einer Sperre verändert, sodass Rahmen, Takt und Befehle nacheinander laufen.
/// </summary>
public sealed class Verbindung : IDisposable
{
    /// <summary>Nur für Tests: so lange wartet die Verbindung nach dem Antrag, bevor sie antwortet.</summary>
    internal static int AntragZeitlupeMs { get; set; }

    /// <summary>Rückstaugrenze der Steuer-Warteschlange; darüber liest der Client offenbar nicht und wird getrennt.</summary>
    public const int SteuerRueckstauMax = 20_000;

    private static long _naechsteId;

    private readonly Stream _strom;
    private readonly Socket? _socket;
    private readonly Zentrale _zentrale;
    private readonly Relais? _relais;
    private readonly Logbuch _log;
    private readonly SemaphoreSlim _sperre = new(1, 1);
    private readonly Channel<Func<Task>> _befehle = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock _warteSperre = new();
    private readonly Sendewarteschlange<Ausgehend> _warte = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _ende = new();
    private readonly List<Ausgehend> _nachhol = [];
    private readonly int _standardSendepuffer;
    private ServerKanal _kanal = null!;
    private Steuerbremse _bremse = null!;
    private Zusammenfasser<object> _zusammen = null!;
    private bool _syncOffen;
    private bool _schliessenEingereiht;
    private int _steuerAnzahl;
    private volatile bool _endet;
    private volatile bool _auslaufen;

    internal Verbindung(Stream strom, Socket? socket, Adresse quelle, I.Fingerabdruck geraet, bool haeltUnbekannt,
        Zentrale zentrale, Relais? relais, Logbuch log)
    {
        _strom = strom;
        _socket = socket;
        Quelle = quelle;
        Geraet = geraet;
        HaeltUnbekannt = haeltUnbekannt;
        _zentrale = zentrale;
        _relais = relais;
        _log = log;
        _standardSendepuffer = socket?.SendBufferSize ?? 0;
        Id = Interlocked.Increment(ref _naechsteId);
    }

    public long Id { get; }
    public Adresse Quelle { get; }
    public I.Fingerabdruck Geraet { get; }
    public uint KontoId { get; internal set; }
    public uint GeraetId { get; internal set; }
    public uint SitzungsId { get; internal set; }
    public ushort Minor { get; private set; }

    /// <summary><c>Hello.client.version</c>, sobald Hello da ist; das Versionstor misst daran (4.7, 6.16).</summary>
    public string? ClientVersion { get; private set; }
    public I.SchluesselArt SchluesselArt { get; private set; }
    public bool WarAktiv { get; private set; }
    public bool SprechanzeigeAbo { get; internal set; }
    public SitzungsZaehler Zaehler { get; } = new();

    /// <summary>Hält einen Platz der unfreigegebenen Verbindungen (4.6, 4.8).</summary>
    public bool HaeltUnbekannt { get; private set; }

    /// <summary>Endete die Verbindung wegen eines Verstoßes (Protokoll, Ratengrenze, kein Hello in der Frist)? Für die Sperrliste (A51).</summary>
    public Verstossart? Verstoss { get; private set; }

    /// <summary>Die Verbindung wird beendet; es geht nichts mehr hinaus außer dem Abschied.</summary>
    public bool Endet => _endet;

    /// <summary>Aktueller Zustand des Automaten (für Anzeige und Tests).</summary>
    public ServerZustand Zustand { get; private set; } = ServerZustand.Tls;

    // ---- Lebenslauf ----

    /// <summary>Läuft, bis die Verbindung endet. Danach meldet sie sich bei der Zentrale ab.</summary>
    public async Task LaufenAsync()
    {
        var jetzt = Zeit.MonoMs();
        _kanal = new ServerKanal(jetzt);
        // Handshake, ALPN und P-256-Zertifikat hat die Annahme schon geprüft (4.1).
        _kanal.Automat.TlsFertig(alpnPasst: true, p256Zertifikat: true, jetzt);
        Zustand = _kanal.Automat.Zustand;
        _bremse = new Steuerbremse(jetzt);
        _zusammen = new Zusammenfasser<object>(_bremse);
        _zentrale.VerbindungAnmelden(this);

        var schreiben = Task.Run(SchreibschleifeAsync);
        var befehle = Task.Run(BefehlsschleifeAsync);
        var takt = Task.Run(TaktschleifeAsync);
        try
        {
            await LeseschleifeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or AuthenticationException)
        {
            // Verbindung weg oder beendet.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Fehler("Verbindung", ex);
        }
        finally
        {
            // Erst den Abschied (Fehler, Bye) hinausschreiben lassen, dann alles beenden; hängt der Client, begrenzt
            // die Frist das Warten.
            Beenden(null, null);
            _befehle.Writer.TryComplete();
            try
            {
                await schreiben.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or IOException or ObjectDisposedException or AuthenticationException)
            {
            }
            await _ende.CancelAsync().ConfigureAwait(false);
            Wecken();
            await Leise(schreiben).ConfigureAwait(false);
            await Leise(befehle).ConfigureAwait(false);
            await Leise(takt).ConfigureAwait(false);
            if (_auslaufen && _socket is not null) _ = AuslaufenAsync(_strom, _socket);
            else await _strom.DisposeAsync().ConfigureAwait(false);
            if (HaeltUnbekannt)
            {
                HaeltUnbekannt = false;
                _zentrale.Zulassung.UnbekanntFreigeben(Quelle);
            }
            _zentrale.VerbindungGeschlossen(this);
        }
    }

    private static async Task Leise(Task t)
    {
        try
        {
            await t.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or AuthenticationException)
        {
        }
    }

    private async Task LeseschleifeAsync()
    {
        using var leser = new RahmenLeser(_strom);
        var abbruch = _ende.Token;
        while (!_endet)
        {
            int grenze;
            await _sperre.WaitAsync(abbruch).ConfigureAwait(false);
            try
            {
                grenze = _kanal.Rahmengrenze;
            }
            finally
            {
                _sperre.Release();
            }
            var r = await leser.LesenAsync(grenze, abbruch).ConfigureAwait(false);
            if (r.Status is LeseStatus.Ende or LeseStatus.Abgebrochen) return;
            await _sperre.WaitAsync(abbruch).ConfigureAwait(false);
            try
            {
                if (_endet) return;
                if (r.Status == LeseStatus.Fehler)
                {
                    Verstoss ??= Verstossart.Protokoll;
                    Beenden(r.Fehler, null);
                    return;
                }
                await RahmenAsync(r.Kopf, r.Body).ConfigureAwait(false);
            }
            finally
            {
                _sperre.Release();
            }
        }
    }

    private async Task BefehlsschleifeAsync()
    {
        var leser = _befehle.Reader;
        while (await leser.WaitToReadAsync(_ende.Token).ConfigureAwait(false))
        {
            while (leser.TryRead(out var befehl))
            {
                await _sperre.WaitAsync(_ende.Token).ConfigureAwait(false);
                try
                {
                    if (!_endet) await befehl().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _log.Fehler("Verbindung, Befehl", ex);
                }
                finally
                {
                    _sperre.Release();
                }
            }
        }
    }

    private async Task TaktschleifeAsync()
    {
        using var takt = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (await takt.WaitForNextTickAsync(_ende.Token).ConfigureAwait(false))
            Befehl(TaktAsync);
    }

    /// <summary>Führt <paramref name="befehl"/> unter der Sperre der Verbindung aus (threadsicher).</summary>
    public void Befehl(Func<Task> befehl) => _befehle.Writer.TryWrite(befehl);

    // ---- Eingang ----

    private async Task RahmenAsync(RahmenKopf kopf, ReadOnlyMemory<byte> body)
    {
        var jetzt = Zeit.MonoMs();
        var e = _kanal.Empfangen(kopf, body.Span, jetzt);
        if (e.Trennen)
        {
            Verstoss ??= Verstossart.Protokoll;
            Beenden(e.Fehler, null, e.Feld);
            return;
        }
        if (kopf.Typ != (ushort)Nachrichtentyp.SpracheTunnel)
        {
            var b = _bremse.Pruefen(kopf, jetzt);
            switch (b.Urteil)
            {
                case Bremsurteil.Trennen:
                    // Mehr als 50 Verstöße in 60 s: trennen und das Gerät 60 s sperren, nicht die IP (4.8).
                    _zentrale.Zulassung.GeraetSperren(Geraet, jetzt, Ratengrenzen.GeraetesperreMs);
                    Verstoss = Verstossart.Ratengrenze;
                    _log.Warnung("Zu viele Verstöße gegen die Ratengrenzen, Gerät 60 s gesperrt: " + _log.Schwaerzung.Konto(KontoId));
                    Beenden(Fehlercode.Grenze, null);
                    return;
                case Bremsurteil.Grenze:
                    if (kopf.Art == Nachrichtenart.Anfrage) AntwortFehler(kopf.AnfrageNr, kopf.Typ, Fehlercode.Grenze);
                    else if (b.GrenzeMelden) Push(Nachrichtentyp.Fehler, new Fehler { Code = Fehlercode.Grenze, BezugTyp = kopf.Typ });
                    return;
                case Bremsurteil.Zusammenfassen when e.Status == EingangsStatus.Annehmen && e.Body is { } zustand:
                    if (_zusammen.Einreihen(kopf.Typ, kopf.AnfrageNr, zustand) is { } ersetzt)
                        AntwortFehler(ersetzt.Nr, kopf.Typ, Fehlercode.Ueberholt);
                    return;
            }
        }
        switch (e.Status)
        {
            case EingangsStatus.Uebergehen:
                return;
            case EingangsStatus.Fehler:
                if (kopf.Art == Nachrichtenart.Anfrage) AntwortFehler(kopf.AnfrageNr, kopf.Typ, e.Fehler, e.Feld);
                else if (kopf.Art == Nachrichtenart.Push) Push(Nachrichtentyp.Fehler, new Fehler { Code = e.Fehler, BezugTyp = kopf.Typ, Text = e.Feld });
                // Eine ungültige Antwort auf VerknuepfungBestaetigen ist schon ausgebucht und gilt als nein.
                else if (e.Bezug is { Typ: Nachrichtentyp.VerknuepfungBestaetigen }) await VerknuepfungAntwortAsync(false).ConfigureAwait(false);
                return;
        }

        await AnnehmenAsync(kopf, e, body).ConfigureAwait(false);
        if (kopf.Typ == (ushort)Nachrichtentyp.SpracheTunnel) return;
        if (_kanal.LetzterUebergang is { Trennen: true })
        {
            // Bye vom Client oder Fehler-Antwort auf VoiceKeys: ohne Abschied schließen.
            Beenden(null, null);
        }
    }

    private async Task AnnehmenAsync(RahmenKopf kopf, Eingang e, ReadOnlyMemory<byte> body)
    {
        switch ((Nachrichtentyp)kopf.Typ)
        {
            case Nachrichtentyp.Hello:
                await HelloAsync((Hello)e.Body!).ConfigureAwait(false);
                return;
            case Nachrichtentyp.Ping:
                Antworten(kopf.AnfrageNr, kopf.Typ, Antwort.Mit(new PingAntwort
                {
                    Nr = ((PingAnfrage)e.Body!).Nr,
                    UdpEmpfangen = Zaehler.UdpEmpfangen,
                    UdpGesendet = Zaehler.UdpGesendet,
                    ServerZeit = Zeit.UnixMs(),
                }));
                return;
            case Nachrichtentyp.Bye:
                return;
            case Nachrichtentyp.Fehler:
                if (e.Bezug is { Typ: Nachrichtentyp.VerknuepfungBestaetigen }) await VerknuepfungAntwortAsync(false).ConfigureAwait(false);
                return;
            case Nachrichtentyp.Registrierung:
            {
                var r = (RegistrierungAnfrage)e.Body!;
                if (r.ScName is { } scName && r.DiscordName is { } discordName)
                {
                    // Antrag ohne Code (3.4, P3): erst die Antwort, dann GeraetStatus{WARTET}, dann in den WARTERAUM.
                    var (antrag, antragFehler, wartet) = await _zentrale.AntragStellenAsync(this, r.Geraetename, scName, discordName).ConfigureAwait(false);
                    if (AntragZeitlupeMs > 0) await Task.Delay(AntragZeitlupeMs).ConfigureAwait(false);
                    Antworten(kopf.AnfrageNr, kopf.Typ, antrag is not null ? Antwort.Mit(antrag) : Antwort.Fehl(antragFehler ?? Fehlercode.Intern));
                    if (antrag is null || wartet is null || _kanal.Automat.Zustand != ServerZustand.Registrierung) return;
                    Push(Nachrichtentyp.GeraetStatus, wartet);
                    _kanal.Automat.VerknuepfungBestaetigt(Zeit.MonoMs());
                    Zustand = _kanal.Automat.Zustand;
                    return;
                }
                var (antwort, fehler) = await _zentrale.RegistrierenAsync(this, r.Geraetename).ConfigureAwait(false);
                Antworten(kopf.AnfrageNr, kopf.Typ, antwort is not null ? Antwort.Mit(antwort) : Antwort.Fehl(fehler ?? Fehlercode.Intern));
                return;
            }
            case Nachrichtentyp.VerknuepfungBestaetigen:
                await VerknuepfungAntwortAsync(((VerknuepfungBestaetigenAntwort)e.Body!).Ja).ConfigureAwait(false);
                return;
            case Nachrichtentyp.VoiceKeys:
                _relais?.VoiceKeysBeantwortet(SitzungsId, body.Span[0]);
                if (_kanal.LetzterUebergang is { Von: ServerZustand.AktivSync, Nach: ServerZustand.Aktiv })
                {
                    Zustand = ServerZustand.Aktiv;
                    WarAktiv = true;
                    _zentrale.AktivErreicht(this);
                }
                return;
            case Nachrichtentyp.SpracheTunnel:
                _relais?.TunnelEingang(SitzungsId, body.Span);
                return;
            case Nachrichtentyp.EmpfangsBericht:
                _relais?.EmpfangsBericht(SitzungsId, (EmpfangsBericht)e.Body!);
                return;
        }
        if (kopf.Art != Nachrichtenart.Anfrage || e.Body is null) return;
        var a = await _zentrale.AnfrageAsync(this, (Nachrichtentyp)kopf.Typ, e.Body).ConfigureAwait(false);
        Antworten(kopf.AnfrageNr, kopf.Typ, a);
    }

    private async Task HelloAsync(Hello hello)
    {
        Minor = Versionen.NebenversionVereinbaren(hello.Minor, Versionen.Nebenversion);
        SchluesselArt = Abbildung.SchluesselArtIdentitaet(hello.SchluesselArt);
        // Vor dem Bescheid gesetzt: Legt ein Commit danach das erste Kanalnetz an, trennt ihn das Versionstor (6.16).
        ClientVersion = hello.Client.Version;
        var bescheid = await _zentrale.HelloAsync(this).ConfigureAwait(false);
        var mindest = bescheid.Mindest;
        var zuAlt = Versionen.ClientZuAlt(hello.Client.Version, mindest);
        var einstufung = bescheid.Art switch
        {
            I.EinstufungsArt.Wartet => Einstufung.Wartet,
            I.EinstufungsArt.Freigegeben => Einstufung.Freigegeben,
            I.EinstufungsArt.GeraetGesperrt => Einstufung.Abgelehnt,
            I.EinstufungsArt.KontoGesperrt => Einstufung.KontoGesperrt,
            _ => Einstufung.Unbekannt,
        };
        var u = _kanal.Automat.HelloVerarbeitet(einstufung, zuAlt, Zeit.MonoMs());
        Zustand = u.Nach;
        if (u.Fehler is { } f)
        {
            if (f == Fehlercode.VersionZuAlt) SchliessenMit(Versionen.VersionZuAltFehler(mindest));
            else Beenden(f, null);
            return;
        }
        switch (u.Welcome)
        {
            case WelcomeStatus.Unbekannt:
                // P-44: keine Serverversion, keine Namen, keine Zahlen über Konten oder Netze.
                Push(Nachrichtentyp.Welcome, Welcome.Unbekannt(Minor));
                break;
            case WelcomeStatus.Wartet:
                Push(Nachrichtentyp.Welcome, bescheid.Welcome!);
                break;
            case WelcomeStatus.Freigegeben:
                await SyncStartenAsync().ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// AKTIV_SYNC (4.6): Sitzung in der Zentrale starten, Schlüssel der Epoche 0 erzeugen und dem Relais übergeben, dann in
    /// fester Folge Welcome{FREIGEGEBEN}, VoiceKeys(START, 0), NetzListe, Mitgliedschaften, Praesenz, PegelVoreinstellungen
    /// und Zustand einreihen. Pushes, die die Zentrale in der Zwischenzeit schickt, folgen danach.
    /// </summary>
    private async Task SyncStartenAsync()
    {
        if (HaeltUnbekannt)
        {
            HaeltUnbekannt = false;
            _zentrale.Zulassung.UnbekanntFreigeben(Quelle);
        }
        var start = await _zentrale.SitzungStartenAsync(this).ConfigureAwait(false);
        if (start.Fehler is { } f)
        {
            Beenden(f, null);
            return;
        }
        _kanal.SitzungsId = start.SitzungsId;
        var material = new byte[Schluesselmaterial.Laenge];
        Schluesselmaterial.Erzeugen(material);
        _relais?.SitzungAnlegen(start.SitzungsId, KontoId, Quelle, material.ToArray(), Zaehler);
        lock (_warteSperre)
        {
            if (_schliessenEingereiht)
            {
                CryptographicOperations.ZeroMemory(material);
                return;
            }
            JsonEinreihen(Nachrichtentyp.Welcome, 0, 0, start.Welcome!);
            VoiceKeysEinreihen(0, VoiceKeysGrund.Start, material);
            JsonEinreihen(Nachrichtentyp.NetzListe, 0, 0, start.NetzListe!);
            JsonEinreihen(Nachrichtentyp.Mitgliedschaften, 0, 0, start.Mitgliedschaften!);
            JsonEinreihen(Nachrichtentyp.Praesenz, 0, 0, start.Praesenz!);
            JsonEinreihen(Nachrichtentyp.PegelVoreinstellungen, 0, 0, start.Pegel!);
            JsonEinreihen(Nachrichtentyp.Zustand, 0, 0, new ZustandsVersionBody { ZustandsVersion = start.ZustandsVersion });
            foreach (var n in _nachhol) SteuerEinreihen(n);
            _nachhol.Clear();
            _syncOffen = false;
        }
        Wecken();
    }

    private async Task VerknuepfungAntwortAsync(bool ja)
    {
        var ok = await _zentrale.VerknuepfungBeantwortetAsync(this, ja).ConfigureAwait(false);
        if (ok && _kanal.Automat.Zustand == ServerZustand.Registrierung)
        {
            _kanal.Automat.VerknuepfungBestaetigt(Zeit.MonoMs());
            Zustand = _kanal.Automat.Zustand;
        }
    }

    private async Task TaktAsync()
    {
        var jetzt = Zeit.MonoMs();
        var u = _kanal.Ticken(jetzt, out var abgelaufen);
        if (abgelaufen is { Typ: Nachrichtentyp.VerknuepfungBestaetigen }) await VerknuepfungAntwortAsync(false).ConfigureAwait(false);
        if (u.Trennen)
        {
            // Kein Hello in der Frist nach dem Handshake: zählt wie Protokollmüll (hält sonst einen Platz für Unfreigegebene).
            if (u.Von == ServerZustand.WarteHello) Verstoss ??= Verstossart.Protokoll;
            Zustand = ServerZustand.Ende;
            Beenden(null, u.Bye, null, null, u.WiederInMs);
            return;
        }
        foreach (var typ in _zusammen.WartendeArten.ToList())
        {
            if (!_zusammen.VersucheEntnehmen(typ, jetzt, out var nr, out var wert) || wert is null) continue;
            var a = await _zentrale.AnfrageAsync(this, (Nachrichtentyp)typ, wert).ConfigureAwait(false);
            Antworten(nr, typ, a);
        }
    }

    // ---- Befehle von Zentrale und Relais (threadsicher) ----

    /// <summary>Die Zentrale beginnt die Sitzung: bis die Sync-Folge eingereiht ist, werden Pushes gepuffert.</summary>
    internal void SyncBeginnt()
    {
        lock (_warteSperre) _syncOffen = true;
    }

    /// <summary>
    /// Freigabe im WARTERAUM (3.5): GeraetStatus{FREIGEGEBEN}, dann Welcome und AKTIV_SYNC. Läuft wie jeder Befehl unter der
    /// Sperre der Verbindung, also nie mitten in der Bearbeitung des Antrags (Antwort, GeraetStatus{WARTET}, WARTERAUM).
    /// </summary>
    internal void FreigabeAnwenden(GeraetStatus status) => Befehl(async () =>
    {
        Push(Nachrichtentyp.GeraetStatus, status);
        if (_kanal.Automat.Zustand != ServerZustand.Warteraum) return;
        var u = _kanal.Automat.Freigegeben(Zeit.MonoMs());
        Zustand = u.Nach;
        await SyncStartenAsync().ConfigureAwait(false);
    });

    /// <summary>Rückfrage VerknuepfungBestaetigen (3.4 Schritt 5) als Server-Anfrage mit Frist 120 s.</summary>
    internal void VerknuepfungFragen(VerknuepfungKonto konto) => Befehl(async () =>
    {
        if (_kanal.Automat.Zustand != ServerZustand.Registrierung)
        {
            await _zentrale.VerknuepfungBeantwortetAsync(this, false).ConfigureAwait(false);
            return;
        }
        var nr = _kanal.VerknuepfungStellen(Zeit.MonoMs());
        lock (_warteSperre) JsonEinreihen(Nachrichtentyp.VerknuepfungBestaetigen, 0, nr, new VerknuepfungBestaetigenAnfrage { Konto = konto });
        Wecken();
    });

    /// <summary>VoiceKeys ROTATION (5.8) vom Relais; das Material wird nach dem Schreiben genullt.</summary>
    internal void VoiceKeysSenden(byte epoche, byte[] material) => Befehl(() =>
    {
        if (_kanal.Automat.Zustand != ServerZustand.Aktiv)
        {
            CryptographicOperations.ZeroMemory(material);
            return Task.CompletedTask;
        }
        lock (_warteSperre)
        {
            if (_schliessenEingereiht)
            {
                CryptographicOperations.ZeroMemory(material);
                return Task.CompletedTask;
            }
            VoiceKeysEinreihen(epoche, VoiceKeysGrund.Rotation, material);
        }
        Wecken();
        return Task.CompletedTask;
    });

    /// <summary>Tunnel an: kleiner Sendepuffer, damit sich keine Sekunden veralteter Sprache im Kernel stauen (4.1, 5.12).</summary>
    internal void TunnelGeaendert(bool an)
    {
        if (_socket is null) return;
        try
        {
            _socket.SendBufferSize = an ? 64 * 1024 : _standardSendepuffer;
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>JSON-Push; während der Sync-Folge gepuffert, nach dem Abschied verworfen.</summary>
    public void Push<T>(Nachrichtentyp typ, T body) where T : class
    {
        lock (_warteSperre)
        {
            if (_schliessenEingereiht) return;
            var a = new Ausgehend { Json = (w, ct) => w.SchreibenAsync(typ, 0, 0, body, ct), Typ = (ushort)typ };
            if (_syncOffen) _nachhol.Add(a);
            else SteuerEinreihen(a);
        }
        Wecken();
    }

    /// <summary>SpracheTunnel runter (5.12): höchstens 3 je Absender, älter als 60 ms wird verworfen (4.2).</summary>
    public void TunnelSenden(uint absender, byte[] datagramm)
    {
        ArgumentNullException.ThrowIfNull(datagramm);
        lock (_warteSperre)
        {
            if (_schliessenEingereiht || _syncOffen) return;
            _warte.TunnelEinreihen(absender, new Ausgehend { Roh = datagramm, Laenge = datagramm.Length, Typ = (ushort)Nachrichtentyp.SpracheTunnel },
                Zeit.MonoMs());
        }
        Wecken();
    }

    /// <summary>
    /// Beendet die Verbindung: optional <c>Fehler</c> und/oder <c>Bye</c>, danach TLS close_notify und Trennung
    /// (4.5 Bye). Threadsicher; weitere Pushes werden danach verworfen.
    /// </summary>
    public void Beenden(Fehlercode? fehler, ByeGrund? bye, ByeDurch? durch = null) => Beenden(fehler, bye, durch, null, null);

    private void Beenden(Fehlercode? fehler, ByeGrund? bye, ByeDurch? durch, string? text, uint? wiederInMs)
    {
        lock (_warteSperre)
        {
            if (_schliessenEingereiht) return;
            _schliessenEingereiht = true;
            _syncOffen = false;
            _nachhol.Clear();
            if (fehler is { } f) JsonEinreihen(Nachrichtentyp.Fehler, 0, 0, new Fehler { Code = f, Text = text });
            if (bye is { } b) JsonEinreihen(Nachrichtentyp.Bye, 0, 0, new Bye { Grund = b, Durch = durch, WiederInMs = wiederInMs });
            SteuerEinreihen(new Ausgehend { Schliessen = true });
        }
        _endet = true;
        Wecken();
        if (fehler is not null || bye is not null)
            _log.Debug("Verbindung endet mit " + (fehler is { } fc ? "Fehler " + Fehlercodes.Drahtname(fc) : "") + (bye is { } bg ? " Bye " + bg : "") + ": " + _log.Schwaerzung.Konto(KontoId));
        try
        {
            _ende.CancelAfter(2000);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Beenden(Fehlercode? fehler, ByeGrund? bye, string? text) => Beenden(fehler, bye, null, text, null);

    /// <summary>Geordneter Neustart (7.2): <c>Bye{WARTUNG, wiederInMs}</c>.</summary>
    public void Wartung(uint wiederInMs) => Beenden(null, ByeGrund.Wartung, null, null, wiederInMs);

    /// <summary>Versionstor (4.7, 6.16): <c>Fehler{VERSION_ZU_ALT, details: {mindest}}</c> und Trennung; threadsicher.</summary>
    internal void VersionZuAlt(string mindest) => SchliessenMit(Versionen.VersionZuAltFehler(mindest));

    private void SchliessenMit(Fehler fehler)
    {
        lock (_warteSperre)
        {
            if (_schliessenEingereiht) return;
            JsonEinreihen(Nachrichtentyp.Fehler, 0, 0, fehler);
        }
        Beenden(null, null);
    }

    // ---- Ausgang ----

    private void Antworten(uint nr, ushort typ, Antwort antwort)
    {
        lock (_warteSperre)
        {
            if (_schliessenEingereiht) return;
            SteuerEinreihen(new Ausgehend { Json = (w, ct) => antwort.SchreibenAsync(w, (Nachrichtentyp)typ, nr, ct), Typ = typ });
        }
        _kanal.AntwortGesendet(nr);
        Wecken();
    }

    private void AntwortFehler(uint nr, ushort typ, Fehlercode code, string? text = null) => Antworten(nr, typ, Antwort.Fehl(code, text));

    /// <summary>Nur unter <see cref="_warteSperre"/>.</summary>
    private void JsonEinreihen<T>(Nachrichtentyp typ, byte flags, uint nr, T body) where T : class =>
        SteuerEinreihen(new Ausgehend { Json = (w, ct) => w.SchreibenAsync(typ, flags, nr, body, ct), Typ = (ushort)typ });

    /// <summary>Nur unter <see cref="_warteSperre"/>: VoiceKeys als Server-Anfrage, 90 Byte binär (4.5).</summary>
    private void VoiceKeysEinreihen(byte epoche, VoiceKeysGrund grund, byte[] material)
    {
        var body = new byte[VoiceKeys.BodyLaenge];
        try
        {
            VoiceKeys.Schreiben(body, epoche, grund, material.AsSpan(0, 32), material.AsSpan(32, 12), material.AsSpan(44, 32), material.AsSpan(76, 12));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
        var nr = _kanal.VoiceKeysStellen(epoche, Zeit.MonoMs());
        SteuerEinreihen(new Ausgehend { Roh = body, Laenge = body.Length, Typ = (ushort)Nachrichtentyp.VoiceKeys, Nr = nr, Nullen = true });
    }

    /// <summary>Nur unter <see cref="_warteSperre"/>.</summary>
    private void SteuerEinreihen(Ausgehend a)
    {
        if (++_steuerAnzahl > SteuerRueckstauMax && !a.Schliessen)
        {
            // Der Client liest nicht: kein unbegrenzter Rückstau, die Verbindung endet.
            _schliessenEingereiht = true;
            _endet = true;
            _warte.SteuerEinreihen(new Ausgehend { Schliessen = true });
            return;
        }
        _warte.SteuerEinreihen(a);
    }

    private bool Entnehmen(out Ausgehend a)
    {
        lock (_warteSperre)
        {
            if (!_warte.VersucheEntnehmen(Zeit.MonoMs(), out a)) return false;
            if (a.Typ != (ushort)Nachrichtentyp.SpracheTunnel) _steuerAnzahl--;
            return true;
        }
    }

    private async Task SchreibschleifeAsync()
    {
        using var schreiber = new RahmenSchreiber(_strom);
        var abbruch = _ende.Token;
        try
        {
            while (true)
            {
                await _signal.WaitAsync(abbruch).ConfigureAwait(false);
                while (Entnehmen(out var a))
                {
                    if (a.Schliessen)
                    {
                        await AbschliessenAsync().ConfigureAwait(false);
                        return;
                    }
                    try
                    {
                        await a.SchreibenAsync(schreiber, abbruch).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                        // Rahmen größer als 1 MiB (RahmenSchreiber verweigert ihn); nichts ging hinaus.
                        _log.Warnung("Rahmen über der Grenze nicht gesendet: " + ((Nachrichtentyp)a.Typ).ToString());
                    }
                    finally
                    {
                        a.Freigeben();
                    }
                }
            }
        }
        finally
        {
            try
            {
                await _ende.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task AbschliessenAsync()
    {
        _endet = true;
        if (_strom is SslStream ssl)
        {
            try
            {
                using var frist = new CancellationTokenSource(1000);
                await ssl.ShutdownAsync().WaitAsync(frist.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or TimeoutException)
            {
            }
        }
        // Halb schließen (FIN) statt sofort zu schließen: Lägen vom Client noch ungelesene Rahmen im Puffer (etwa beim
        // Trennen wegen GRENZE mitten in einer Flut), schickte TCP beim Schließen ein RST, und der Client verwürfe den
        // schon zugestellten Abschied (Fehler, Bye). Den Rest verwirft AuslaufenAsync, nachdem die Verbindung abgemeldet ist.
        if (_socket is not null)
        {
            try
            {
                _socket.Shutdown(SocketShutdown.Send);
                _auslaufen = true;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
            }
        }
        await _ende.CancelAsync().ConfigureAwait(false);
        if (!_auslaufen) await _strom.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Höchstens so lange und so viel liest das Auslaufen nach dem Abschied noch, um es zu verwerfen.</summary>
    internal const int AuslaufMs = 500;

    internal const int AuslaufMaxBytes = 1024 * 1024;

    /// <summary>
    /// Nach dem halben Schließen: restliche Daten des Clients lesen und verwerfen, bis er selbst schließt (höchstens
    /// <see cref="AuslaufMs"/> und <see cref="AuslaufMaxBytes"/>), dann den Strom schließen. Läuft im Hintergrund, damit
    /// Sitzungsende, Schlüssel und Zählungen nicht darauf warten.
    /// </summary>
    private static async Task AuslaufenAsync(Stream strom, Socket socket)
    {
        var puffer = System.Buffers.ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var frist = new CancellationTokenSource(AuslaufMs);
            var gelesen = 0;
            while (gelesen < AuslaufMaxBytes)
            {
                var n = await socket.ReceiveAsync(puffer, SocketFlags.None, frist.Token).ConfigureAwait(false);
                if (n == 0) break;
                gelesen += n;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(puffer, clearArray: true);
            try
            {
                await strom.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
            }
        }
    }

    /// <summary>Weckt die Schreibschleife; nach dem Ende der Verbindung ohne Wirkung.</summary>
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

    /// <summary>Gibt Sperren und Signale frei; ruft die Annahme nach <see cref="LaufenAsync"/> auf.</summary>
    public void Dispose()
    {
        _sperre.Dispose();
        _signal.Dispose();
        _ende.Dispose();
    }

    /// <summary>Ein ausgehender Rahmen: JSON über einen Schreiber mit statischem Typ oder fertiger Binärbody.</summary>
    private sealed class Ausgehend
    {
        public Func<RahmenSchreiber, CancellationToken, ValueTask>? Json;
        public byte[]? Roh;
        public int Laenge;
        public ushort Typ;
        public uint Nr;
        public bool Nullen;
        public bool Schliessen;

        public ValueTask SchreibenAsync(RahmenSchreiber w, CancellationToken ct) =>
            Json is not null ? Json(w, ct) : w.SchreibenAsync(Typ, 0, Nr, Roh.AsMemory(0, Laenge), ct);

        public void Freigeben()
        {
            if (Nullen && Roh is not null) CryptographicOperations.ZeroMemory(Roh);
        }
    }
}
