// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Globalization;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern.Echo;

/// <summary>
/// Ein Teilnehmer, der im Echo-Netz mithört, jede Sprechphase aufnimmt (Opus-Pakete, ohne sie zu dekodieren) und sie nach
/// dem Ende mit einer Pause als eigene Phase ins selbe Netz zurücksendet. So hört ein Sprecher sich selbst über den ganzen
/// Weg: Server, Jitterpuffer, Mischer. Die Logik steckt im <see cref="Echoband"/>; hier liegen Netz, Uhr und Takt.
/// <para>
/// Empfang auf dem UDP-Faden: nur kopieren und einreihen. Ein eigener Arbeitsfaden führt das Band, prüft einmal je Sekunde
/// die Mitgliedschaft (ist das Echo-Netz offen und fehlt das Feld, trägt er sich per <c>FeldSetzen</c> ein) und gibt eine
/// fällige Phase im 20-ms-Raster wieder, genau wie der Taktgeber des Lasttests (Schlafen nur bei mehr als 17 ms Rest).
/// Die <see cref="Ratenbremse"/> hält die Grenzen des Servers je Sitzung ein.
/// </para>
/// Die übergebene Geräteidentität gehört danach dem Funker und wird mit ihm freigegeben.
/// </summary>
public sealed class EchoFunker : ISprachEmpfaenger, IAsyncDisposable
{
    /// <summary>Mehr wartende Pakete nimmt der Empfang nicht an (etwa 20 s Sprache); der Rest wird gezählt und verworfen.</summary>
    public const int MaxWartend = 1024;

    private readonly EchoOptionen _optionen;
    private readonly Echoband _band;
    private readonly Geraeteidentitaet _identitaet;
    private readonly Action<string>? _log;
    private readonly ConcurrentQueue<EchoPaket> _eingang = new();
    private readonly AutoResetEvent _wecker = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<string> _gemeldet = new(StringComparer.Ordinal);
    private Thread? _arbeiter;
    private volatile ushort _netz;
    private int _wartend;
    private long _ueberlauf;
    private long _letztePruefungMs = Monouhr.Nie;
    private long _letzterFeldversuchMs = Monouhr.Nie;
    private Ratenbremse? _bremse;

    /// <param name="aufbau">Verbindungsaufbau: TLS mit Pin im Betrieb, Testzugang auf Loopback in der Probe.</param>
    /// <param name="identitaet">Geräteschlüssel des Funkers; gehört danach ihm.</param>
    /// <param name="log">Meldungen (nie Codes oder Schlüssel).</param>
    public EchoFunker(IVerbindungsaufbau aufbau, Geraeteidentitaet identitaet, EchoOptionen? optionen = null, Action<string>? log = null,
        string geraetename = "Echo-Funker")
    {
        ArgumentNullException.ThrowIfNull(aufbau);
        ArgumentNullException.ThrowIfNull(identitaet);
        _optionen = optionen ?? new EchoOptionen();
        _band = new Echoband(_optionen, log);
        _identitaet = identitaet;
        _log = log;
        Client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = aufbau,
            Identitaet = identitaet,
            ClientName = "funk-echo",
            Geraetename = geraetename,
            // Die Verknüpfung stößt der Admin mit dem angezeigten Code an; der Funker bestätigt sie selbst.
            Rueckfrage = (konto, _) =>
            {
                Melden("Verknüpfung mit Konto " + konto.Name + " (" + konto.Benutzername + ") bestätigt; jetzt freigeben");
                return Task.FromResult(true);
            },
            Empfaenger = this,
        });
        Client.ZustandGeaendert += (_, w) =>
        {
            Melden("Zustand " + w.Nach + (w.Grund is { } g ? " (" + g + ")" : ""));
            if (w.Nach is not (ClientZustand.AktivUdp or ClientZustand.AktivTunnel)) return;
            // Gleich nach AKTIV die Mitgliedschaft prüfen, nicht erst im Sekundentakt.
            Interlocked.Exchange(ref _letztePruefungMs, Monouhr.Nie);
            _wecker.Set();
        };
        Client.HintergrundFehler += (_, e) => Melden("Fehler im Hintergrund: " + e.Message);
        // Scheitert schon der Aufbau (DNS, TCP, TLS, Pin), wechselt kein Zustand; ohne diese Zeile bliebe das Journal stumm.
        Client.SitzungBeendet += (_, e) =>
        {
            if (e.Aufbau is { } art) Melden("Aufbau gescheitert (" + art + "): " + e.Einzelheit);
            if (Abschied(e, Client.NeuversuchGesperrt) is { } abschied) Abgewiesen?.Invoke(this, abschied);
        };
    }

    /// <summary>
    /// Der Server lässt den Funker nicht mehr herein, und er versucht es nicht von selbst neu: <c>Bye{VERDRAENGT}</c> (ein
    /// anderes Gerät desselben Kontos) oder ein Fehler, nach dem nur ein Mensch neu verbindet (4.6). funk-echo endet darauf nach
    /// einer Ruhezeit mit dem Code aus <see cref="EchoAbschied"/>, und systemd startet ihn neu.
    /// </summary>
    public event EventHandler<EchoAbschied>? Abgewiesen;

    /// <summary>Die beiden Befehle, die der Admin auf dem Server abschreibt, ganz ausgeschrieben wie bei der echo-probe.</summary>
    public static IReadOnlyList<string> Adminbefehle(string code, string anzeige) =>
    [
        "dotnet /opt/funk/funk-admin.dll verknuepfen --code " + code + " --discord <eindeutige Zahl> --benutzername echo-funker --name Echo --konfig /etc/funk/server.json",
        "dotnet /opt/funk/funk-admin.dll freigeben " + anzeige + " --konfig /etc/funk/server.json",
    ];

    /// <summary>Was ein Sitzungsende für den Echo-Funker heißt; <c>null</c>, wenn der Client es selbst wieder versucht.</summary>
    public static EchoAbschied? Abschied(Sitzungsende ende, bool neuversuchGesperrt)
    {
        ArgumentNullException.ThrowIfNull(ende);
        if (ende.Bye == ByeGrund.Verdraengt)
        {
            var durch = ende.Durch is { } d ? d.GeraetName + " (" + d.Anzeige + ")" : "ein anderes Gerät";
            return new EchoAbschied(EchoAbschied.CodeVerdraengt, "Verdrängt durch " + durch + ": Ein anderes Gerät hat sich mit dem Konto des "
                + "Echo-Funkers angemeldet, etwa die Echo-Probe mit seiner Discord-Zahl (eine Sitzung je Konto). funk-echo verbindet nicht "
                + "selbst neu, endet nach der Ruhezeit mit Code 3, und systemd startet ihn danach neu.");
        }
        if (!neuversuchGesperrt) return null;
        var grund = ende.Fehler is { } f ? Fehlercodes.Drahtname(f) : ende.Aufbau is { } a ? a.ToString() : ende.Grund;
        return new EchoAbschied(EchoAbschied.CodeAbgewiesen, "Der Server lässt den Echo-Funker nicht herein (" + grund + "): Konto oder Gerät "
            + "gesperrt, Fassung zu alt, Verdrängen gesperrt (funk-admin verdraengung-freigeben) oder ein falscher Pin. funk-echo endet nach "
            + "der Ruhezeit mit Code 4, und systemd versucht es danach neu.");
    }

    /// <summary>Der Protokoll-Client des Funkers (Code, Zustand, Lagebild).</summary>
    public FunkClient Client { get; }

    public EchoOptionen Optionen => _optionen;

    /// <summary>Netz-ID des Echo-Netzes, sobald die NetzListe da ist; 0 = unbekannt.</summary>
    public ushort NetzId => _netz;

    public EchoZustand Zustand => _band.Zustand;

    public int Aufnahmen => _band.Aufnahmen;

    public int Wiedergaben => _band.Wiedergaben;

    public int Schleifen => _band.Schleifen;

    public int Gekuerzt => _band.Gekuerzt;

    /// <summary>Wegen voller Warteschlange verworfene Pakete.</summary>
    public long Ueberlauf => Interlocked.Read(ref _ueberlauf);

    /// <summary>Verbindet und startet den Arbeitsfaden.</summary>
    public void Starten()
    {
        if (_arbeiter is not null) throw new InvalidOperationException("Schon gestartet.");
        _arbeiter = new Thread(Arbeiten) { IsBackground = true, Name = "Echo-Funker", Priority = ThreadPriority.AboveNormal };
        _arbeiter.Start();
        Client.Starten();
    }

    // ---- Empfang (UDP-Faden bzw. Leseschleife): nur kopieren ----

    public void Empfangen(in SprachEmpfang empfang)
    {
        var netz = _netz;
        var p = empfang.Paket;
        if (netz == 0 || p.Art != Spruchart.Netz) return;
        var treffer = false;
        for (var i = 0; i < p.NetzAnzahl && !treffer; i++) treffer = p.Netz(i) == netz;
        if (!treffer) return;
        if (Interlocked.Increment(ref _wartend) > MaxWartend)
        {
            Interlocked.Decrement(ref _wartend);
            Interlocked.Increment(ref _ueberlauf);
            return;
        }
        _eingang.Enqueue(new EchoPaket(p.Absender, p.Sprechphase, p.FrameNr, p.Kennzeichen, p.Art, netz, p.Opus.ToArray(),
            Monouhr.MsAus(empfang.EmpfangenTakte)));
        _wecker.Set();
    }

    public void ZeitlinieBeendet(uint absender, ushort sprechphase)
    {
        // Das Band erkennt den Leerlauf selbst; der Arbeitsfaden soll nur zeitnah nachsehen.
        _wecker.Set();
    }

    // ---- Arbeitsfaden ----

    private void Arbeiten()
    {
        _bremse = new Ratenbremse(_optionen, Monouhr.Takte());
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                Abarbeiten();
                var jetzt = Monouhr.Ms();
                if (jetzt - Interlocked.Read(ref _letztePruefungMs) >= 1000)
                {
                    Interlocked.Exchange(ref _letztePruefungMs, jetzt);
                    MitgliedschaftPruefen(jetzt);
                }
                if (_band.Takt(jetzt) is { } phase) Wiedergeben(phase);
                _wecker.WaitOne(10);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Der Funker soll weiterlaufen; eine hängende Wiedergabe gibt das Band frei.
                Melden("Fehler im Arbeitsfaden: " + ex.GetType().Name + ": " + ex.Message);
                if (_band.Zustand == EchoZustand.Wiedergabe) _band.WiedergabeBeendet(Monouhr.Ms(), vollstaendig: false);
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Alle wartenden Pakete ins Band; während Pause und Wiedergabe verwirft das Band sie.</summary>
    private void Abarbeiten()
    {
        while (_eingang.TryDequeue(out var p))
        {
            Interlocked.Decrement(ref _wartend);
            if (_band.Paket(p, Monouhr.Ms()) == EchoUrteil.Begonnen)
                Melden("Aufnahme beginnt: Konto " + p.Absender.ToString(CultureInfo.InvariantCulture) + ", Phase " + p.Sprechphase.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void MitgliedschaftPruefen(long jetzt)
    {
        if (Client.Zustand is not (ClientZustand.AktivUdp or ClientZustand.AktivTunnel)) return;
        var lage = Client.Lage;
        _band.EigenesKonto = lage.KontoId;
        if (lage.NetzListe is not { } liste) return;
        var netz = NetzAufloesen(liste, _optionen.Netz);
        if (netz is null)
        {
            _netz = 0;
            EinmalMelden("netz", "Den Kanal '" + _optionen.Netz + "' gibt es auf diesem Server nicht; der Funker wartet.");
            return;
        }
        _netz = netz.Id;
        var feld = lage.FeldMitNetz(netz.Id);
        if (feld is null)
        {
            if (netz.Offen != NetzOffen.HoerenSenden)
            {
                EinmalMelden("offen", "Kein Feld mit '" + netz.Name + "' und der Kanal ist nicht offen: die Einsatzleitung oder ein Admin muss den Funker hineinsetzen (hören und senden).");
                return;
            }
            if (jetzt - _letzterFeldversuchMs < 10_000 || lage.Mitgliedschaften is not { } m) return;
            _letzterFeldversuchMs = jetzt;
            var frei = Enumerable.Range(0, 16).Select(i => (byte)i).FirstOrDefault(i => m.Felder.All(f => f.Feld != i), (byte)255);
            if (frei == 255)
            {
                EinmalMelden("voll", "Alle 16 Felder sind belegt; für '" + netz.Name + "' ist kein Platz.");
                return;
            }
            Melden("Trage mich in '" + netz.Name + "' ein (Feld " + frei.ToString(CultureInfo.InvariantCulture) + ")");
            _ = Client.FeldSetzenAsync(frei, netz.Id).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && !t.Result.Ok) Melden("FeldSetzen abgelehnt: " + t.Result.Code);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }
        if (!feld.Hoeren) EinmalMelden("hoeren", "Das Feld mit '" + netz.Name + "' hört nicht; der Funker nimmt nichts auf.");
        if (!feld.Senden) EinmalMelden("senden", "Das Feld mit '" + netz.Name + "' darf nicht senden; der Funker kann nichts zurückschicken.");
        if (feld.Hoeren && feld.Senden) EinmalMelden("bereit", "Bereit im Kanal '" + netz.Name + "' (ID " + netz.Id.ToString(CultureInfo.InvariantCulture) + ")");
    }

    /// <summary>Netz nach Name (ohne Rücksicht auf Groß- und Kleinschreibung) oder nach ID; Global nie.</summary>
    public static Netz? NetzAufloesen(NetzListe liste, string netz)
    {
        ArgumentNullException.ThrowIfNull(liste);
        ArgumentNullException.ThrowIfNull(netz);
        foreach (var n in liste.Netze)
        {
            if (n.Typ == NetzTyp.Global) continue;
            if (string.Equals(n.Name, netz, StringComparison.OrdinalIgnoreCase)) return n;
        }
        if (ushort.TryParse(netz, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return liste.Netze.FirstOrDefault(n => n.Id == id && n.Typ != NetzTyp.Global);
        return null;
    }

    /// <summary>Gibt eine Phase im Raster ihrer Frame-Nr wieder, dann ENDE (dreimal, 5.6); danach ist das Band wieder bereit.</summary>
    private void Wiedergeben(Echophase phase)
    {
        var vollstaendig = false;
        try
        {
            if (Client.Lage.FeldMitNetz(phase.Netz) is not { Senden: true })
            {
                Melden("Wiedergabe entfällt: kein Senderecht im Kanal " + phase.Netz.ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (!Client.Sprechen.Beginnen(Sendeziel.Netz(phase.Netz), phase.Va))
            {
                Melden("Wiedergabe entfällt: keine Sitzung");
                return;
            }
            var start = Monouhr.Takte();
            long verschoben = 0;
            var gesendet = 0;
            foreach (var f in phase.Frames)
            {
                WartenBis(start + Monouhr.TakteAus(f.VersatzMs) + verschoben);
                verschoben += Bremsen(f.Opus.Length, 1);
                // Beim Beenden bricht die Wiedergabe ab, statt den Rest ohne Takt hinauszuschicken.
                if (_stop.IsCancellationRequested || !Senden(f)) break;
                gesendet++;
            }
            Bremsen(0, SprechphasenGeber.EndeWiederholungen);
            Client.Sprechen.Beenden();
            vollstaendig = gesendet == phase.Frames.Count;
            Melden("Wiedergabe " + (vollstaendig ? "fertig" : "abgebrochen") + ": " + gesendet.ToString(CultureInfo.InvariantCulture) + " von "
                + phase.Frames.Count.ToString(CultureInfo.InvariantCulture) + " Frames"
                + (verschoben > 0 ? string.Create(CultureInfo.InvariantCulture, $", Ratenbremse {Monouhr.MsGenau(verschoben):F0} ms") : ""));
        }
        finally
        {
            // Pakete, die während der Wiedergabe ankamen, verwirft das Band (Ankunft vor "bereit").
            Abarbeiten();
            _band.WiedergabeBeendet(Monouhr.Ms(), vollstaendig);
        }
    }

    private bool Senden(EchoFrame f)
    {
        try
        {
            return Client.Sprechen.Senden(f.Opus, f.Preroll);
        }
        catch (InvalidOperationException)
        {
            // Pre-roll passt nicht (etwa nach einem Zielwechsel): als gewöhnlicher Frame.
            return Client.Sprechen.Senden(f.Opus);
        }
    }

    /// <summary>Wartet, bis die Ratenbremse das Paket durchlässt; liefert die dabei gewartete Zeit in Takten.</summary>
    private long Bremsen(int bytes, int pakete)
    {
        long gewartet = 0;
        while (!_stop.IsCancellationRequested)
        {
            var jetzt = Monouhr.Takte();
            var warte = _bremse!.Nehmen(jetzt, bytes, pakete);
            if (warte == 0) return gewartet;
            gewartet += warte;
            WartenBis(jetzt + warte);
        }
        return gewartet;
    }

    /// <summary>Wie der Taktgeber des Lasttests: schlafen nur, solange mehr als 17 ms fehlen, den Rest aktiv; dabei den Empfang leeren.</summary>
    private void WartenBis(long soll)
    {
        var frequenz = System.Diagnostics.Stopwatch.Frequency;
        while (true)
        {
            var rest = soll - Monouhr.Takte();
            if (rest <= 0 || _stop.IsCancellationRequested) return;
            if (rest > 17 * frequenz / 1000)
            {
                Abarbeiten();
                Thread.Sleep(1);
            }
            else if (rest > frequenz / 2000) Thread.Yield();
            else Thread.SpinWait(20);
        }
    }

    private void EinmalMelden(string schluessel, string text)
    {
        lock (_gemeldet)
        {
            if (!_gemeldet.Add(schluessel)) return;
        }
        Melden(text);
    }

    private void Melden(string text)
    {
        try
        {
            _log?.Invoke(text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ein Fehler im Log darf den Funker nicht anhalten.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _wecker.Set();
        _arbeiter?.Join(TimeSpan.FromSeconds(5));
        await Client.DisposeAsync().ConfigureAwait(false);
        _identitaet.Dispose();
        _wecker.Dispose();
        _stop.Dispose();
    }
}
