// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Funk.Klang.Eingabe;

/// <summary>
/// Globale Tasten über Raw Input für Tastatur und Maus (A3, A5): Sprechtaste mit beliebig vielen Belegungen und die
/// Netztasten (Sendenetz vor und zurück, bei Sarevoc Numpad + und -). RIDEV_INPUTSINK meldet Tasten auch, wenn ein anderes
/// Programm (Star Citizen) vorn ist. Das Programm fängt nichts ab; Star Citizen bekommt dieselbe Taste weiterhin.
/// Eigener Thread mit einem unsichtbaren Nachrichtenfenster (Klasse "Message", Elternfenster HWND_MESSAGE): kein Fokus,
/// keine Anzeige, kein Hook. Die Maus wird nur angemeldet, solange eine Maustaste belegt ist oder gerade belegt wird,
/// damit nicht jede Mausbewegung eine Nachricht erzeugt.
/// <para>
/// Grenze von Raw Input: Je Prozess und Gerätekategorie gilt nur die letzte Anmeldung. Darum gibt es im Prozess genau
/// eine Instanz für alle Funktionen; meldet ein anderer Teil desselben Prozesses die Tastatur oder Maus mit eigenem
/// Fenster an, bekommt diese Quelle nichts mehr.
/// </para>
/// <para>
/// Hängeschutz: Hinter dem sicheren Desktop (UAC, Strg+Alt+Entf, Sperrbildschirm) kommt kein WM_INPUT, auch kein
/// Loslassen. Solange eine zugeordnete Taste unten ist, fragt ein Zeitgeber am Nachrichtenfenster deshalb alle
/// <see cref="HaengerAbstandMs"/> ms den physischen Zustand ab (GetAsyncKeyState mit dem virtuellen Code, den Windows dem
/// Drücken zugeordnet hat; nur lesend, kein Hook). Ist die Taste zweimal hintereinander oben, gilt sie als losgelassen.
/// Eine weiter gehaltene Tastaturtaste meldet sich über die Tastenwiederholung von selbst wieder.
/// </para>
/// </summary>
public sealed unsafe class RawInputSprechtaste : ITastenQuelle
{
    private const uint MeldungBelegung = RawInputNativ.WmApp + 1;
    private const uint MeldungBeenden = RawInputNativ.WmApp + 2;
    private const int PufferBytes = 128;

    /// <summary>Abstand der Prüfungen des Hängeschutzes; nach zwei Prüfungen (etwa 0,5 s) gilt eine hängende Taste als los.</summary>
    public const int HaengerAbstandMs = 250;

    private const nuint WaechterKennung = 1;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _bereit = new();
    private readonly Lock _sperre = new();
    private readonly SprechtastenZustand _zustand = new();
    private Tastenzuordnung[] _zuordnungen = [];
    private TaskCompletionSource<Tastenbelegung>? _lernen;
    private bool _lernenVorsichtig;
    private Exception? _startfehler;
    private nint _fenster;
    private bool _mausAngemeldet;
    private bool _waechterLaeuft;
    private volatile bool _gedrueckt;
    private int _beendet;

    public RawInputSprechtaste()
    {
        _zustand.EigenesFensterVorn = EigenesFensterVorn;
        _zustand.ModifikatorUnten = ModifikatorUnten;
        _thread = new Thread(Schleife) { IsBackground = true, Name = "Funk Sprechtaste (Raw Input)" };
        _thread.Start();
        _bereit.Wait();
        if (_startfehler is not null) throw new InvalidOperationException("Raw Input ließ sich nicht einrichten.", _startfehler);
    }

    public event Action? Gedrueckt;

    public event Action? Losgelassen;

    public event Action<Tastenfunktion>? Ausgeloest;

    public event Action<Tastenfunktion, bool>? Gehalten;

    public bool IstGedrueckt => _gedrueckt;

    /// <summary>Die erste Sprechtaste; Setzen ersetzt alle Sprechtasten und lässt die Netztasten stehen.</summary>
    public Tastenbelegung? Belegung
    {
        get
        {
            lock (_sperre)
            {
                foreach (var z in _zuordnungen)
                    if (z.Funktion == Tastenfunktion.Sprechen) return z.Taste;
                return null;
            }
        }
        set
        {
            List<Tastenzuordnung> neu;
            lock (_sperre) neu = [.. _zuordnungen.Where(z => z.Funktion != Tastenfunktion.Sprechen)];
            if (value is { } b) neu.Add(new Tastenzuordnung(Tastenfunktion.Sprechen, b));
            Zuordnungen = neu;
        }
    }

    public IReadOnlyList<Tastenzuordnung> Zuordnungen
    {
        get
        {
            lock (_sperre) return _zuordnungen;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var z in value)
            {
                if (!Unterstuetzt(z.Taste.Art))
                    throw new NotSupportedException("Raw Input kennt nur Tastatur und Maus; Joystick kommt über DirectInput.");
            }
            lock (_sperre) _zuordnungen = [.. value];
            _ = RawInputNativ.NachrichtSenden(_fenster, MeldungBelegung, 0, 0);
        }
    }

    public bool Unterstuetzt(EingabeArt art) => art is EingabeArt.Tastatur or EingabeArt.Maus;

    public Task<Tastenbelegung> NaechsteTasteAsync(CancellationToken ct) => NaechsteTasteAsync(false, ct);

    /// <summary>Belegen; <paramref name="vorsichtig"/> siehe <see cref="SprechtastenZustand.Vorsichtig"/>.</summary>
    public Task<Tastenbelegung> NaechsteTasteAsync(bool vorsichtig, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<Tastenbelegung>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sperre)
        {
            _lernen?.TrySetCanceled(CancellationToken.None);
            _lernen = tcs;
            _lernenVorsichtig = vorsichtig;
        }
        _ = RawInputNativ.NachrichtSenden(_fenster, MeldungBelegung, 0, 0);
        if (ct.CanBeCanceled)
        {
            var anmeldung = ct.Register(() =>
            {
                lock (_sperre)
                {
                    if (ReferenceEquals(_lernen, tcs)) _lernen = null;
                }
                tcs.TrySetCanceled(ct);
                _ = RawInputNativ.NachrichtSenden(_fenster, MeldungBelegung, 0, 0);
            });
            _ = tcs.Task.ContinueWith(_ => anmeldung.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return tcs.Task;
    }

    /// <summary>Name einer Taste im Tastaturlayout des Systems (etwa "Strg-Rechts"), sonst die Kurzform.</summary>
    public static string Tastenname(Tastenbelegung taste)
    {
        if (taste.Art != EingabeArt.Tastatur) return taste.ToString();
        var scancode = taste.Code & 0xFF;
        var erweitert = (taste.Code & 0xFF00) == 0xE000 ? 1 << 24 : 0;
        var puffer = stackalloc char[64];
        var n = RawInputNativ.TastennameLesen((scancode << 16) | erweitert, puffer, 64);
        return n > 0 ? new string(puffer, 0, n) : taste.ToString();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _beendet, 1) != 0) return;
        if (_fenster != 0) _ = RawInputNativ.NachrichtSenden(_fenster, MeldungBeenden, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _bereit.Dispose();
        lock (_sperre) _lernen?.TrySetCanceled(CancellationToken.None);
    }

    private void Schleife()
    {
        try
        {
            _fenster = RawInputNativ.FensterErzeugen(0, "Message", null, 0, 0, 0, 0, 0, RawInputNativ.NurNachrichten, 0, 0, 0);
            if (_fenster == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            Anmelden(RawInputNativ.NutzungTastatur, RawInputNativ.RidevInputSink, _fenster);
        }
        catch (Win32Exception ex)
        {
            _startfehler = ex;
            _bereit.Set();
            return;
        }
        _bereit.Set();

        var puffer = new byte[PufferBytes];
        var ereignisse = new Tastenereignis[5];
        while (RawInputNativ.NachrichtHolen(out var n, 0, 0, 0) > 0)
        {
            if (n.Meldung == MeldungBeenden && n.Fenster == _fenster) break;
            if (n.Meldung == MeldungBelegung && n.Fenster == _fenster)
            {
                BelegungUebernehmen();
                continue;
            }
            if (n.Meldung == RawInputNativ.WmTimer && n.Fenster == _fenster)
            {
                HaengerPruefen();
                continue;
            }
            if (n.Meldung == RawInputNativ.WmInput) Eingabe(n.LParam, puffer, ereignisse);
            // WM_INPUT muss durch DefWindowProc (hier die Fensterprozedur der Klasse "Message"), damit Windows aufräumt.
            _ = RawInputNativ.NachrichtVerteilen(in n);
        }
        Abmelden();
    }

    private void Eingabe(nint rawInput, byte[] puffer, Tastenereignis[] ereignisse)
    {
        uint groesse = PufferBytes;
        uint gelesen;
        fixed (byte* p = puffer)
        {
            gelesen = RawInputNativ.DatenLesen(rawInput, RawInputNativ.RidInput, p, ref groesse, (uint)RawInputAuswertung.KopfBytes);
        }
        if (gelesen == uint.MaxValue || gelesen == 0) return;
        var anzahl = RawInputAuswertung.Auswerten(puffer.AsSpan(0, (int)gelesen), ereignisse);
        for (var i = 0; i < anzahl; i++)
        {
            var e = ereignisse[i];
            var virtuell = e.Taste.Art == EingabeArt.Maus
                ? RawInputAuswertung.VirtuelleTaste(e.Taste.Code)
                : RawInputAuswertung.VirtuelleTaste(puffer.AsSpan(0, (int)gelesen));
            var meldenVorher = _zustand.MeldenGedrueckt;
            _zustand.Verarbeiten(e, virtuell, out var wechsel, out var ausgeloest, out var gelernt);
            if (gelernt is { } taste)
            {
                TaskCompletionSource<Tastenbelegung>? tcs;
                lock (_sperre)
                {
                    tcs = _lernen;
                    _lernen = null;
                }
                tcs?.TrySetResult(taste);
                BelegungUebernehmen();
            }
            if (_zustand.EineUnten) WaechterStarten();
            else WaechterStoppen();
            if (wechsel != 0) Melden(wechsel > 0);
            if (_zustand.MeldenGedrueckt != meldenVorher) Halten(Tastenfunktion.Melden, _zustand.MeldenGedrueckt);
            if (ausgeloest is { } funktion) Ausloesen(funktion);
        }
    }

    private void WaechterStarten()
    {
        if (_waechterLaeuft) return;
        _waechterLaeuft = RawInputNativ.ZeitgeberSetzen(_fenster, WaechterKennung, HaengerAbstandMs, 0) != 0;
    }

    private void WaechterStoppen()
    {
        if (!_waechterLaeuft) return;
        _ = RawInputNativ.ZeitgeberLoeschen(_fenster, WaechterKennung);
        _waechterLaeuft = false;
    }

    private void HaengerPruefen()
    {
        if (!_waechterLaeuft) return;
        // WM_TIMER kommt erst, wenn keine anderen Nachrichten warten: Ein echtes Loslassen ist dann schon verarbeitet.
        // Ohne bekannten Code (0) gibt es nichts abzufragen; dann bleibt es beim Loslassen über Raw Input.
        var meldenVorher = _zustand.MeldenGedrueckt;
        var los = _zustand.HaengerPruefen(v => v == 0 || (RawInputNativ.TastenzustandLesen(v) & 0x8000) != 0);
        if (!_zustand.EineUnten) WaechterStoppen();
        if (los) Melden(false);
        if (meldenVorher && !_zustand.MeldenGedrueckt) Halten(Tastenfunktion.Melden, false);
    }

    private void BelegungUebernehmen()
    {
        Tastenzuordnung[] zuordnungen;
        bool lernt;
        bool vorsichtig;
        lock (_sperre)
        {
            zuordnungen = _zuordnungen;
            lernt = _lernen is not null;
            vorsichtig = _lernenVorsichtig;
        }
        _zustand.Vorsichtig = vorsichtig;
        _zustand.Lernt = lernt;
        var meldenVorher = _zustand.MeldenGedrueckt;
        if (_zustand.Zuordnen(zuordnungen)) Melden(false);
        if (meldenVorher) Halten(Tastenfunktion.Melden, false);
        WaechterStoppen();
        var mausNoetig = lernt || zuordnungen.Any(z => z.Taste.Art == EingabeArt.Maus);
        if (mausNoetig == _mausAngemeldet) return;
        try
        {
            if (mausNoetig) Anmelden(RawInputNativ.NutzungMaus, RawInputNativ.RidevInputSink, _fenster);
            else Anmelden(RawInputNativ.NutzungMaus, RawInputNativ.RidevRemove, 0);
            _mausAngemeldet = mausNoetig;
        }
        catch (Win32Exception)
        {
            // Die Maus bleibt, wie sie war; die Tastatur arbeitet weiter.
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
            // Ein Fehler der Anwendung darf die Tastenschleife nicht beenden.
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
            // Wie bei der Sprechtaste: Ein Fehler der Anwendung darf die Tastenschleife nicht beenden.
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
            // Wie oben: die Schleife läuft weiter.
        }
    }

    private void Abmelden()
    {
        try
        {
            Anmelden(RawInputNativ.NutzungTastatur, RawInputNativ.RidevRemove, 0);
            if (_mausAngemeldet) Anmelden(RawInputNativ.NutzungMaus, RawInputNativ.RidevRemove, 0);
        }
        catch (Win32Exception)
        {
            // Beim Beenden ohne Belang.
        }
        WaechterStoppen();
        if (_zustand.IstGedrueckt) Melden(false);
        if (_zustand.MeldenGedrueckt) Halten(Tastenfunktion.Melden, false);
        _ = RawInputNativ.FensterZerstoeren(_fenster);
    }

    /// <summary>Beim Belegen: Ein Fenster dieses Programms ist vorn (nur lesend, kein Handle auf fremde Prozesse).</summary>
    private static bool EigenesFensterVorn()
    {
        var vorn = RawInputNativ.VordergrundFenster();
        return vorn != 0 && RawInputNativ.FensterProzess(vorn, out var prozess) != 0 && prozess == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// Beim vorsichtigen Belegen: Strg, Alt, Umschalt oder eine Windows-Taste ist physisch unten (GetAsyncKeyState mit VK_SHIFT, VK_CONTROL,
    /// VK_MENU, VK_LWIN, VK_RWIN; nur lesend, kein Hook).
    /// </summary>
    private static bool ModifikatorUnten()
    {
        foreach (var vk in (ReadOnlySpan<int>)[0x10, 0x11, 0x12, 0x5B, 0x5C])
            if ((RawInputNativ.TastenzustandLesen(vk) & 0x8000) != 0) return true;
        return false;
    }

    private static void Anmelden(ushort nutzung, uint flags, nint ziel)
    {
        var geraet = new RawInputNativ.RawInputGeraet
        {
            Seite = RawInputNativ.SeiteGenerisch,
            Nutzung = nutzung,
            Flags = flags,
            Ziel = ziel,
        };
        if (!RawInputNativ.GeraeteAnmelden(&geraet, 1, (uint)sizeof(RawInputNativ.RawInputGeraet)))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
