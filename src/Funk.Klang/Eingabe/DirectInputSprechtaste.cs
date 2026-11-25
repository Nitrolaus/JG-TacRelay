// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Funk.Klang.Eingabe;

/// <summary>
/// Sprechtaste auf Joystick, Throttle, Pedalen und jedem anderen Spielgerät (A3) über DirectInput 8, gelesen wie ein
/// Spiel: nicht-exklusiv und im Hintergrund, also neben Star Citizen, dem JG Signal Monitor und jedem anderen Leser,
/// auch wenn ein anderes Fenster vorn ist. Nur Spielgeräte (DI8DEVCLASS_GAMECTRL), nur lesen: kein Force Feedback, keine
/// Geräteeinstellung außer dem Puffer des eigenen Geräteobjekts, kein HidHide, keine Treiber. Der eigene Code schreibt
/// nichts in die Registry; DirectInput selbst pflegt beim Aufzählen und Öffnen aber Werte unter HKCU (Kalibrier- und
/// Instanzdaten je Gerät). Wer das Programm oder den Joystick-Rauchtest startet, löst das mit aus.
/// <para>
/// Zwei eigene Threads: Der Lesethread wartet auf die Ereignisse der Geräte (SetEventNotification) und liest gepuffert;
/// ein Druck ist damit nach deutlich unter einer Millisekunde gemeldet, im Takt wird nichts allokiert. Geräte, die
/// abgefragt werden müssen, liest ein Takt von 4 ms. Der Suchthread zählt die Geräte auf, was DirectInput einige hundert
/// Millisekunden kostet; der Lesethread liest währenddessen ungebremst weiter.
/// </para>
/// <para>
/// Ab- und Anstecken im Betrieb: Windows meldet es dem versteckten Fenster der Geräteschicht, dazu fällt ein
/// abgestecktes Gerät beim Lesen auf. Eine gehaltene Taste gilt beim Abstecken als losgelassen; die Belegung findet ihr
/// Gerät danach über Pfad, VID/PID und Produktname wieder (<see cref="Geraetekennung"/>).
/// </para>
/// <para>
/// Mehrere Belegungen (<see cref="Belegungen"/>) wirken wie eine Taste: gedrückt, solange eine davon gedrückt ist.
/// <see cref="Belegung"/> aus <see cref="ISprechtasteQuelle"/> ist die erste davon. <see cref="Gedrueckt"/>,
/// <see cref="Losgelassen"/> und <see cref="GeraeteGeaendert"/> kommen aus dem Lesethread, <see cref="Fehler"/> aus
/// einem der beiden Threads. Alle Methoden dürfen aus jedem Thread gerufen werden, auch aus den Ereignissen heraus.
/// </para>
/// </summary>
public sealed unsafe class DirectInputSprechtaste : ITastenQuelle
{
    /// <summary>Eine Funktion mit Flanke ging am Joystick hinunter (Sendenetz, Funkfeld, Phasen); aus dem Lesethread.</summary>
    public event Action<Tastenfunktion>? Ausgeloest;

    /// <summary>Melden am Joystick gedrückt oder losgelassen; aus dem Lesethread.</summary>
    public event Action<Tastenfunktion, bool>? Gehalten;

    /// <summary>
    /// Alle Joystick-Zuordnungen dieser Quelle mit ihren Funktionen (seit 0.5.0, RAID-S2-ENTWURF Abschnitt 6); eine Liste gleichen
    /// Inhalts ändert nichts. Tastatur und Maus lehnt sie ab.
    /// </summary>
    public IReadOnlyList<Tastenzuordnung> Zuordnungen
    {
        get
        {
            lock (_sperre) return _zuordnungen;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var neu = value.ToArray();
            foreach (var z in neu) Pruefen(z.Taste, nameof(value));
            lock (_sperre)
            {
                if (_zuordnungen.AsSpan().SequenceEqual(neu)) return;
                _zuordnungen = neu;
                _zuordnungenNeu = true;
            }
            Wecken();
        }
    }

    private static void Pruefen(Tastenbelegung b, string name)
    {
        if (b.Art != EingabeArt.Joystick)
            throw new NotSupportedException("DirectInput kennt nur Spielgeräte; Tastatur und Maus kommen über Raw Input.");
        if (!Joystickeingabe.IstGueltig(b.Code))
            throw new ArgumentException($"Kein Knopf und keine Hutrichtung: {b}", name);
    }

    /// <summary>Takt für Geräte ohne Ereignis, wie im JG Signal Monitor.</summary>
    public const int AbfrageMs = 4;

    /// <summary>So viele Fehler hintereinander verkraftet der Lesethread, bevor er aufgibt (wie im JG Signal Monitor).</summary>
    private const int FehlerHoechstens = 32;

    private readonly Func<ISpielgeraeteSchicht> _schichtFabrik;
    private readonly Func<ISpielgeraeteSuche> _sucheFabrik;
    private readonly Thread _leser;
    private readonly Thread _sucher;
    private readonly ManualResetEventSlim _bereit = new();
    private readonly AutoResetEvent _weck = new(false);
    private readonly AutoResetEvent _suchauftrag = new(false);
    private readonly Lock _sperre = new();
    private Tastenzuordnung[] _zuordnungen = [];
    private bool _zuordnungenNeu;
    private bool _suchen;
    private int _probe;
    private TaskCompletionSource<Tastenbelegung>? _lernen;
    private List<SpielgeraetInfo>? _suchergebnis;
    private bool _sucheFertig;
    private SpielgeraetInfo[] _geraete = [];
    private Exception? _startfehler;
    private volatile bool _gedrueckt;
    private volatile bool _stop;
    private int _beendet;
    private long _ereignisse;
    private long _allokiertImTakt;
    private long _fehler;
    private long _letzteSucheMikros;
    private Joystickauswertung? _auswertung;

    // Nur im Lesethread.
    private bool _sucheLaeuft;
    private bool _sucheNochmal;

    public DirectInputSprechtaste()
        : this(static () => new DirectInputGeraete(), static () => new DirectInputSuche())
    {
    }

    /// <summary>Mit eigener Geräteschicht (Tests). Die Fabriken laufen im Lese- bzw. im Suchthread.</summary>
    internal DirectInputSprechtaste(Func<ISpielgeraeteSchicht> schichtFabrik, Func<ISpielgeraeteSuche> sucheFabrik)
    {
        ArgumentNullException.ThrowIfNull(schichtFabrik);
        ArgumentNullException.ThrowIfNull(sucheFabrik);
        _schichtFabrik = schichtFabrik;
        _sucheFabrik = sucheFabrik;
        _leser = new Thread(Leseschleife)
        {
            IsBackground = true,
            Name = "Funk Sprechtaste (DirectInput)",
            // Der Thread tut fast nichts, soll aber auch neben einem Spiel, das alle Kerne belegt, sofort dran sein.
            Priority = ThreadPriority.AboveNormal,
        };
        _leser.SetApartmentState(ApartmentState.MTA);
        _sucher = new Thread(Suchschleife) { IsBackground = true, Name = "Funk Sprechtaste (Gerätesuche)" };
        _sucher.SetApartmentState(ApartmentState.MTA);
        _leser.Start();
        _bereit.Wait();
        if (_startfehler is not null) throw new InvalidOperationException("DirectInput ließ sich nicht einrichten.", _startfehler);
        _sucher.Start();
    }

    public event Action? Gedrueckt;

    public event Action? Losgelassen;

    /// <summary>Die Liste der angeschlossenen Spielgeräte hat sich geändert (<see cref="Geraete"/>); aus dem Lesethread.</summary>
    public event Action? GeraeteGeaendert;

    /// <summary>Ein Fehler im Lese- oder Suchthread; nach zu vielen hintereinander endet der Lesethread, gedrückt ist dann nichts mehr.</summary>
    public event EventHandler<Exception>? Fehler;

    public bool IstGedrueckt => _gedrueckt;

    /// <summary>Die angeschlossenen und geöffneten Spielgeräte; leer bis zum ersten Aufzählen kurz nach dem Start.</summary>
    public IReadOnlyList<SpielgeraetInfo> Geraete => Volatile.Read(ref _geraete);

    /// <summary>Die erste Sprechtaste; Setzen ersetzt alle Sprechtasten und lässt die übrigen Funktionen stehen.</summary>
    public Tastenbelegung? Belegung
    {
        get
        {
            var b = Belegungen;
            return b.Count > 0 ? b[0] : null;
        }
        set => Belegungen = value is { } b ? [b] : [];
    }

    /// <summary>Die Sprechtasten dieser Quelle; Setzen ersetzt sie und lässt die übrigen Funktionen stehen.</summary>
    public IReadOnlyList<Tastenbelegung> Belegungen
    {
        get
        {
            lock (_sperre) return [.. _zuordnungen.Where(z => z.Funktion == Tastenfunktion.Sprechen).Select(z => z.Taste)];
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var b in value) Pruefen(b, nameof(value));
            Tastenzuordnung[] uebrige;
            lock (_sperre) uebrige = [.. _zuordnungen.Where(z => z.Funktion != Tastenfunktion.Sprechen)];
            Zuordnungen = [.. value.Select(b => new Tastenzuordnung(Tastenfunktion.Sprechen, b)), .. uebrige];
        }
    }

    public bool Unterstuetzt(EingabeArt art) => art == EingabeArt.Joystick;

    /// <summary>
    /// Belegen: der nächste frisch gedrückte Knopf oder die nächste Hutrichtung irgendeines Spielgeräts. Ein schon
    /// gehaltener Knopf zählt erst nach dem Loslassen; ein eben angestecktes Gerät erst nach einer Ruhezeit von
    /// <see cref="Joystickauswertung.RuheMs"/> ms. Zu Beginn werden die Geräte neu aufgezählt.
    /// </summary>
    public Task<Tastenbelegung> NaechsteTasteAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<Tastenbelegung>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Tastenbelegung>? alt;
        bool beendet;
        lock (_sperre)
        {
            alt = _lernen;
            // Unter der Sperre prüfen: Ein Dispose, das zwischen einer Prüfung davor und hier lief, hätte _lernen schon
            // geleert, und dieses Belegen bliebe für immer offen.
            beendet = Volatile.Read(ref _beendet) != 0;
            _lernen = beendet ? null : tcs;
        }
        alt?.TrySetCanceled(CancellationToken.None);
        if (beendet)
        {
            tcs.SetCanceled(CancellationToken.None);
            return tcs.Task;
        }
        Wecken();
        if (ct.CanBeCanceled)
        {
            var anmeldung = ct.Register(() =>
            {
                lock (_sperre)
                {
                    if (ReferenceEquals(_lernen, tcs)) _lernen = null;
                }
                tcs.TrySetCanceled(ct);
                Wecken();
            });
            _ = tcs.Task.ContinueWith(_ => anmeldung.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return tcs.Task;
    }

    /// <summary>
    /// Zählt die Spielgeräte neu auf. Nötig etwa, wenn Joystick Guardian beendet wird: HidHide gibt Geräte frei, ohne dass
    /// Windows das meldet. Ohne Aufruf geschieht es nur nach Windows-Meldungen, beim Belegen und, solange eine Belegung
    /// ihr Gerät nicht findet, anfangs alle 3 s, dann seltener bis alle 30 s (<see cref="Joystickauswertung.SuchabstandMs"/>).
    /// </summary>
    public void GeraeteNeuSuchen()
    {
        lock (_sperre) _suchen = true;
        Wecken();
    }

    /// <summary>
    /// Rauchtest: jedes offene Gerät <paramref name="runden"/> Mal lesen (Puffer und Stand), wie nach einem Ereignis, und
    /// die Allokation dabei mitzählen. Beweist am echten Gerät, dass der heiße Pfad über COM nichts allokiert.
    /// </summary>
    internal void Probelesen(int runden)
    {
        lock (_sperre) _probe += runden;
        Wecken();
    }

    /// <summary>Ob das Gerät einer Belegung gerade angeschlossen ist (nach denselben Regeln wie beim Lesen).</summary>
    public bool IstAngeschlossen(Tastenbelegung belegung)
    {
        if (belegung.Art != EingabeArt.Joystick) return false;
        var geraete = Geraete;
        var kennungen = new Geraetekennung[geraete.Count];
        for (var i = 0; i < kennungen.Length; i++) kennungen[i] = geraete[i].Kennung;
        var alle = Zuordnungen.Select(z => z.Taste).ToList();
        var eigener = Geraetekennung.Lesen(belegung.Geraet).Pfad;
        return Joystickauswertung.Zuordnen(Geraetekennung.Lesen(belegung.Geraet), kennungen,
            pfad => !string.Equals(pfad, eigener, StringComparison.OrdinalIgnoreCase) && Joystickauswertung.Beansprucht(alle, pfad)) >= 0;
    }

    /// <summary>Name einer Belegung für die Oberfläche, etwa "VPC Stick MT-50CM3 - Knopf 5" (<see cref="Joystickeingabe.Anzeigename"/>).</summary>
    public static string Anzeigename(Tastenbelegung belegung) => Joystickeingabe.Anzeigename(belegung);

    /// <summary>Zähler für Tests und den Rauchtest; aus einem anderen Thread gelesen nur ungefähr.</summary>
    internal JoystickStatistik Statistik
    {
        get
        {
            var a = _auswertung;
            return new JoystickStatistik(
                a?.Aufzaehlungen ?? 0,
                a?.Oeffnungsfehler ?? 0,
                a?.Verloren ?? 0,
                Interlocked.Read(ref _ereignisse),
                a?.Wechselzahl ?? 0,
                Interlocked.Read(ref _allokiertImTakt),
                Interlocked.Read(ref _fehler),
                Interlocked.Read(ref _letzteSucheMikros),
                a?.LetzterFehler);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _beendet, 1) != 0) return;
        _stop = true;
        Wecken();
        Beauftragen();
        // Aus einem Ereignis heraus nicht auf den eigenen Thread warten: Die Schleifen enden nach dem Ereignis von selbst.
        var hier = Thread.CurrentThread;
        if (hier != _leser && hier != _sucher)
        {
            // Der Suchthread kann mitten in EnumDevices stecken (einige hundert ms).
            var leserFertig = _leser.Join(TimeSpan.FromSeconds(2));
            var sucherFertig = !_sucher.IsAlive || _sucher.Join(TimeSpan.FromSeconds(2));
            if (leserFertig && sucherFertig)
            {
                _weck.Dispose();
                _suchauftrag.Dispose();
                _bereit.Dispose();
            }
        }
        TaskCompletionSource<Tastenbelegung>? tcs;
        lock (_sperre)
        {
            tcs = _lernen;
            _lernen = null;
        }
        tcs?.TrySetCanceled(CancellationToken.None);
    }

    private void Wecken()
    {
        try
        {
            _ = _weck.Set();
        }
        catch (ObjectDisposedException)
        {
            // Schon beendet.
        }
    }

    private void Beauftragen()
    {
        try
        {
            _ = _suchauftrag.Set();
        }
        catch (ObjectDisposedException)
        {
            // Schon beendet.
        }
    }

    private static long Jetzt => Environment.TickCount64;

    // ---------------------------------------------------------------- Suchthread

    private void Suchschleife()
    {
        var com = DirectInputNativ.ComStarten(null, 0);
        ISpielgeraeteSuche? suche = null;
        try
        {
            try
            {
                suche = _sucheFabrik();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Ohne Suche liefert jede Runde "gescheitert"; der Lesethread versucht es in Abständen wieder.
                FehlerMelden(ex);
            }
            while (true)
            {
                try
                {
                    _ = _suchauftrag.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                if (_stop) break;
                List<SpielgeraetInfo>? ergebnis = [];
                var start = Stopwatch.GetTimestamp();
                try
                {
                    if (suche is null || !suche.Aufzaehlen(ergebnis)) ergebnis = null;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    FehlerMelden(ex);
                    ergebnis = null;
                }
                Interlocked.Exchange(ref _letzteSucheMikros, (long)Stopwatch.GetElapsedTime(start).TotalMicroseconds);
                lock (_sperre)
                {
                    _suchergebnis = ergebnis;
                    _sucheFertig = true;
                }
                Wecken();
            }
        }
        finally
        {
            suche?.Dispose();
            if (com >= 0) DirectInputNativ.ComBeenden();
        }
    }

    // ---------------------------------------------------------------- Lesethread

    private void Leseschleife()
    {
        var com = DirectInputNativ.ComStarten(null, 0);
        ISpielgeraeteSchicht schicht;
        Abfragetakt takt;
        try
        {
            takt = new Abfragetakt();
            try
            {
                schicht = _schichtFabrik();
            }
            catch
            {
                takt.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _startfehler = ex;
            _bereit.Set();
            if (com >= 0) DirectInputNativ.ComBeenden();
            return;
        }
        var auswertung = new Joystickauswertung(schicht) { Wechsel = Melden, Gelernt = Gelernt, Gehalten = Halten, Flanke = Ausloesen };
        auswertung.GeraeteGeaendert = () => GeraeteMelden(auswertung);
        _auswertung = auswertung;
        _bereit.Set();
        try
        {
            Laufen(auswertung, schicht, takt);
        }
        finally
        {
            try
            {
                auswertung.Schliessen(Jetzt);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                FehlerMelden(ex);
            }
            takt.Dispose();
            schicht.Dispose();
            Volatile.Write(ref _geraete, []);
            if (com >= 0) DirectInputNativ.ComBeenden();
        }
    }

    private void Laufen(Joystickauswertung auswertung, ISpielgeraeteSchicht schicht, Abfragetakt takt)
    {
        var griffe = Array.Empty<nint>();
        var zuordnung = Array.Empty<int>();
        var version = -1;
        var fehlerFolge = 0;
        SucheAnstossen();
        while (!_stop)
        {
            try
            {
                if (auswertung.Version != version)
                {
                    version = auswertung.Version;
                    (griffe, zuordnung) = GriffeBauen(auswertung, takt);
                }
                var frist = auswertung.NaechsteFrist(Jetzt);
                uint ergebnis;
                fixed (nint* p = griffe)
                {
                    ergebnis = DirectInputNativ.Warten((uint)griffe.Length, p, frist < 0 ? DirectInputNativ.Unendlich : (uint)Math.Min(frist, int.MaxValue),
                        DirectInputNativ.QsAlles, DirectInputNativ.MwmoEingabeDa);
                }
                var jetzt = Jetzt;
                if (ergebnis == 0)
                {
                    Auftraege(auswertung, jetzt);
                }
                else if (ergebnis == 1)
                {
                    auswertung.Abfragen(jetzt);
                }
                else if (ergebnis < griffe.Length)
                {
                    GeraetLesen(auswertung, zuordnung[ergebnis - 2], jetzt, mitStand: false);
                }
                else if (ergebnis == griffe.Length)
                {
                    while (DirectInputNativ.NachrichtNehmen(out var n, 0, 0, 0, DirectInputNativ.PmEntfernen)) _ = RawInputNativ.NachrichtVerteilen(in n);
                    if (schicht.GeraeteGeaendert()) auswertung.Geraeteaenderung(jetzt);
                }
                else if (ergebnis == DirectInputNativ.WartenFehlgeschlagen)
                {
                    FehlerMelden(new Win32Exception(Marshal.GetLastPInvokeError()));
                    return;
                }
                if (auswertung.Faelliges(Jetzt)) SucheAnstossen();
                fehlerFolge = 0;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Eine Ausnahme darf nicht den ganzen Funk beenden; erst eine lange Kette davon beendet den Thread.
                FehlerMelden(ex);
                if (++fehlerFolge >= FehlerHoechstens) return;
            }
        }
    }

    /// <summary>Eine Suche läuft immer nur einmal; ein Auftrag währenddessen wird danach nachgeholt.</summary>
    private void SucheAnstossen()
    {
        if (_sucheLaeuft)
        {
            _sucheNochmal = true;
            return;
        }
        _sucheLaeuft = true;
        Beauftragen();
    }

    private void GeraetLesen(Joystickauswertung auswertung, int index, long jetzt, bool mitStand)
    {
        var version = auswertung.Version;
        var lernte = auswertung.Lernt;
        var vorher = GC.GetAllocatedBytesForCurrentThread();
        if (mitStand) auswertung.LesenMitStand(index, jetzt);
        else auswertung.Lesen(index, jetzt);
        var allokiert = GC.GetAllocatedBytesForCurrentThread() - vorher;
        // Gemessen wird nur der Takt selbst; Abstecken und Belegen dürfen allokieren.
        if (auswertung.Version == version && auswertung.Lernt == lernte) Interlocked.Add(ref _allokiertImTakt, allokiert);
        Interlocked.Increment(ref _ereignisse);
    }

    /// <summary>Warteliste: 0 Wecken, 1 Takt, dann die Ereignisse der Geräte. Stellt den Takt, falls ein Gerät ohne Ereignis dabei ist.</summary>
    private (nint[] Griffe, int[] Zuordnung) GriffeBauen(Joystickauswertung auswertung, Abfragetakt takt)
    {
        var griffe = new List<nint> { _weck.SafeWaitHandle.DangerousGetHandle(), takt.Griff };
        var zuordnung = new List<int>();
        var abfragen = false;
        for (var i = 0; i < auswertung.Anzahl; i++)
        {
            var ereignis = auswertung.Ereignis(i);
            if (ereignis == 0)
            {
                abfragen = true;
                continue;
            }
            griffe.Add(ereignis);
            zuordnung.Add(i);
        }
        takt.Stellen(abfragen);
        return (griffe.ToArray(), zuordnung.ToArray());
    }

    private void Auftraege(Joystickauswertung auswertung, long jetzt)
    {
        Tastenzuordnung[]? zuordnungen = null;
        List<SpielgeraetInfo>? ergebnis = null;
        bool lernen, suchen, fertig;
        int probe;
        lock (_sperre)
        {
            if (_zuordnungenNeu)
            {
                zuordnungen = _zuordnungen;
                _zuordnungenNeu = false;
            }
            lernen = _lernen is not null;
            suchen = _suchen;
            _suchen = false;
            probe = _probe;
            _probe = 0;
            fertig = _sucheFertig;
            if (fertig)
            {
                ergebnis = _suchergebnis;
                _suchergebnis = null;
                _sucheFertig = false;
            }
        }
        if (fertig)
        {
            _sucheLaeuft = false;
            auswertung.Abgleichen(ergebnis, jetzt);
            if (_sucheNochmal)
            {
                _sucheNochmal = false;
                SucheAnstossen();
            }
        }
        if (zuordnungen is not null) auswertung.ZuordnungenSetzen(zuordnungen, jetzt);
        if (lernen && !auswertung.Lernt) auswertung.LernenStarten(jetzt);
        else if (!lernen && auswertung.Lernt) auswertung.LernenBeenden();
        if (suchen) SucheAnstossen();
        for (var runde = 0; runde < probe; runde++)
        {
            for (var i = auswertung.Anzahl - 1; i >= 0; i--)
            {
                if (i < auswertung.Anzahl) GeraetLesen(auswertung, i, jetzt, mitStand: true);
            }
        }
    }

    private void Gelernt(Tastenbelegung belegung)
    {
        TaskCompletionSource<Tastenbelegung>? tcs;
        lock (_sperre)
        {
            tcs = _lernen;
            _lernen = null;
        }
        tcs?.TrySetResult(belegung);
    }

    private void GeraeteMelden(Joystickauswertung auswertung)
    {
        Volatile.Write(ref _geraete, auswertung.Liste);
        try
        {
            GeraeteGeaendert?.Invoke();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ein Fehler der Anwendung darf den Lesethread nicht beenden.
        }
    }

    private void Melden(bool gedrueckt)
    {
        _gedrueckt = gedrueckt;
        try
        {
            if (gedrueckt) Gedrueckt?.Invoke();
            else Losgelassen?.Invoke();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ein Fehler der Anwendung darf den Lesethread nicht beenden.
        }
    }

    private void Halten(Tastenfunktion funktion, bool unten)
    {
        try
        {
            Gehalten?.Invoke(funktion, unten);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ein Fehler der Anwendung darf den Lesethread nicht beenden.
        }
    }

    private void Ausloesen(Tastenfunktion funktion)
    {
        try
        {
            Ausgeloest?.Invoke(funktion);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Wie oben.
        }
    }

    private void FehlerMelden(Exception ex)
    {
        Interlocked.Increment(ref _fehler);
        try
        {
            Fehler?.Invoke(this, ex);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Wie oben.
        }
    }

    /// <summary>Hochauflösender Wartezeitgeber für Geräte, die abgefragt werden müssen; nur gestellt, wenn es solche gibt.</summary>
    private sealed class Abfragetakt : IDisposable
    {
        private bool _laeuft;

        public Abfragetakt()
        {
            Griff = DirectInputNativ.ZeitgeberErzeugen(0, null, DirectInputNativ.ZeitgeberHochaufloesend, DirectInputNativ.ZeitgeberVollzugriff);
            if (Griff == 0) Griff = DirectInputNativ.ZeitgeberErzeugen(0, null, 0, DirectInputNativ.ZeitgeberVollzugriff);
            if (Griff == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        public nint Griff { get; }

        public void Stellen(bool an)
        {
            if (an == _laeuft) return;
            if (an)
            {
                var faellig = -10_000L * AbfrageMs;
                _laeuft = DirectInputNativ.ZeitgeberStellen(Griff, &faellig, AbfrageMs, 0, 0, false);
            }
            else
            {
                _ = DirectInputNativ.ZeitgeberAnhalten(Griff);
                _laeuft = false;
            }
        }

        public void Dispose()
        {
            if (_laeuft) _ = DirectInputNativ.ZeitgeberAnhalten(Griff);
            _ = DirectInputNativ.GriffSchliessen(Griff);
        }
    }
}

/// <summary>
/// Zähler der Joystick-Sprechtaste; AllokiertImTakt misst die Bytes, die das Lesen nach einem Ereignis allokierte,
/// LetzteSucheMikros die Dauer der letzten Aufzählung im Suchthread.
/// </summary>
internal readonly record struct JoystickStatistik(long Aufzaehlungen, long Oeffnungsfehler, long Verloren, long Ereignisse, long Wechsel,
    long AllokiertImTakt, long Fehler, long LetzteSucheMikros, string? LetzterFehler);
