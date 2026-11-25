// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Eingabe;

namespace Funk.Tests.Maschine;

/// <summary>
/// Attrappe der Geräteschicht und der Suche unter der Joystick-Sprechtaste: angesteckte Geräte mit Stand und Puffer,
/// von Hand gedrückt. Threadsicher, weil die Tests der Sprechtaste von außen drücken, während Lese- und Suchthread
/// darauf zugreifen; beide geben sie am Ende frei.
/// </summary>
internal sealed class GeraeteschichtAttrappe : ISpielgeraeteSchicht, ISpielgeraeteSuche
{
    public const string Name = "VPC Stick MT-50CM3";

    private readonly Lock _sperre = new();
    private readonly List<GeraetAttrappe> _angesteckt = [];
    private bool _geaendert;
    private int _aufzaehlungen;
    private int _entsorgungen;

    public int Aufzaehlungen
    {
        get
        {
            lock (_sperre) return _aufzaehlungen;
        }
    }

    /// <summary>Wie oft freigegeben: 2, wenn Lese- und Suchthread fertig sind.</summary>
    public int Entsorgungen => Volatile.Read(ref _entsorgungen);

    public static string Pfad(string kennung) => $@"\\?\hid#vid_3344&pid_4130#{kennung}#{{4d1e55b2-f16f-11cf-88cb-001111000030}}";

    public GeraetAttrappe Anstecken(string pfad, ushort vid = 0x3344, ushort pid = 0x4130, string name = Name, Guid? instanz = null,
        bool mussAbfragen = false)
    {
        var info = new SpielgeraetInfo(instanz ?? Guid.NewGuid(), new Geraetekennung(pfad, vid, pid, name), 32, 2, mussAbfragen);
        var g = new GeraetAttrappe(info);
        lock (_sperre)
        {
            _angesteckt.Add(g);
            _geaendert = true;
        }
        return g;
    }

    public GeraetAttrappe Geraet(string pfad)
    {
        lock (_sperre) return _angesteckt.First(g => g.Info.Kennung.Pfad == pfad);
    }

    /// <summary>Wie ein echtes Abstecken: Das Gerät verschwindet aus der Aufzählung, Lesen scheitert, das Ereignis kommt.</summary>
    public void Abstecken(GeraetAttrappe g, bool windowsMeldet = true)
    {
        lock (_sperre)
        {
            _angesteckt.Remove(g);
            if (windowsMeldet) _geaendert = true;
        }
        g.Wegnehmen();
    }

    /// <summary>So lange braucht eine Suche (DirectInputs EnumDevices: gemessen rund 200 ms).</summary>
    public int SucheDauertMs { get; set; }

    public bool Aufzaehlen(List<SpielgeraetInfo> ziel)
    {
        if (SucheDauertMs > 0) Thread.Sleep(SucheDauertMs);
        lock (_sperre)
        {
            _aufzaehlungen++;
            foreach (var g in _angesteckt) ziel.Add(g.Info);
        }
        return true;
    }

    public IOffenesSpielgeraet? Oeffnen(SpielgeraetInfo geraet, out string? fehler)
    {
        lock (_sperre)
        {
            var g = _angesteckt.Find(x => x.Info.Instanz == geraet.Instanz);
            if (g is null)
            {
                fehler = "nicht angesteckt";
                return null;
            }
            fehler = null;
            return g.Oeffnen();
        }
    }

    public bool GeraeteGeaendert()
    {
        lock (_sperre)
        {
            var g = _geaendert;
            _geaendert = false;
            return g;
        }
    }

    public void Dispose() => Interlocked.Increment(ref _entsorgungen);
}

/// <summary>Ein angestecktes Gerät der Attrappe. Knöpfe zählen ab 1, Hüte ab 1, wie in den Belegungen.</summary>
internal sealed class GeraetAttrappe(SpielgeraetInfo info)
{
    // Monitor statt System.Threading.Lock: Lock legt bei der ersten Konkurrenz ein Warteobjekt auf dem verwalteten Heap an,
    // unter Last auch im Lesethread, und der Latenztest schriebe diese Bytes der Sprechtaste zu (gemessen 200 bis 216).
    private readonly object _sperre = new();
    private readonly Queue<Eingabewechsel> _puffer = new();
    private readonly List<AutoResetEvent> _ereignisse = [];
    private Eingabestand _stand;
    private SpielgeraetInfo _info = info;
    private bool _weg;
    private bool _unvollstaendig;

    public SpielgeraetInfo Info
    {
        get
        {
            lock (_sperre) return _info;
        }
    }

    /// <summary>DirectInput vergibt dem Gerät eine andere Instanz-GUID; das Gerät selbst bleibt dasselbe.</summary>
    public void NeueInstanz(Guid instanz)
    {
        lock (_sperre) _info = _info with { Instanz = instanz };
    }

    public int Offen { get; private set; }

    /// <summary>Das Freigeben eines Zugangs wirft, nachdem es geschlossen hat (wie ein Fehler beim Freigeben über COM).</summary>
    public bool SchliessenWirft { get; set; }

    public Eingabestand Stand
    {
        get
        {
            lock (_sperre) return _stand;
        }
    }

    public void Knopf(int knopf, bool unten) => Wechseln(Eingabewechsel.Knopf(knopf - 1, unten), mitPuffer: true);

    public void Hut(int hut, Hutrichtung? richtung) => Wechseln(Eingabewechsel.Hut(hut - 1, richtung is { } r ? (int)r : -1), mitPuffer: true);

    /// <summary>Ein Wechsel, der im Puffer fehlt und kein Ereignis auslöst (verlorenes Loslassen).</summary>
    public void KnopfStill(int knopf, bool unten) => Wechseln(Eingabewechsel.Knopf(knopf - 1, unten), mitPuffer: false, signal: false);

    /// <summary>Ein Wechsel nach einem Pufferüberlauf: Nur der Stand kennt ihn.</summary>
    public void KnopfUeberlauf(int knopf, bool unten)
    {
        lock (_sperre) _unvollstaendig = true;
        Wechseln(Eingabewechsel.Knopf(knopf - 1, unten), mitPuffer: false);
    }

    public void Wegnehmen()
    {
        lock (_sperre)
        {
            _weg = true;
            foreach (var e in _ereignisse) e.Set();
        }
    }

    public OffenAttrappe Oeffnen()
    {
        lock (_sperre)
        {
            Offen++;
            _puffer.Clear();
            var ereignis = _info.MussAbfragen ? null : new AutoResetEvent(false);
            if (ereignis is not null) _ereignisse.Add(ereignis);
            return new OffenAttrappe(this, ereignis);
        }
    }

    public void Schliessen(AutoResetEvent? ereignis)
    {
        lock (_sperre)
        {
            Offen--;
            if (ereignis is not null) _ereignisse.Remove(ereignis);
        }
        ereignis?.Dispose();
    }

    public int WechselLesen(Span<Eingabewechsel> ziel, out bool unvollstaendig)
    {
        lock (_sperre)
        {
            unvollstaendig = _unvollstaendig;
            _unvollstaendig = false;
            if (_weg) return -1;
            var n = 0;
            while (_puffer.Count > 0)
            {
                var w = _puffer.Dequeue();
                if (n < ziel.Length) ziel[n++] = w;
                else unvollstaendig = true;
            }
            return n;
        }
    }

    public bool StandLesen(out Eingabestand stand)
    {
        lock (_sperre)
        {
            stand = _stand;
            return !_weg;
        }
    }

    private void Wechseln(Eingabewechsel w, bool mitPuffer, bool signal = true)
    {
        lock (_sperre)
        {
            _stand = _stand.Mit(w);
            if (mitPuffer) _puffer.Enqueue(w);
            if (!signal) return;
            foreach (var e in _ereignisse) e.Set();
        }
    }
}

/// <summary>Ein geöffneter Zugang zur Attrappe; mehrere Zugänge teilen sich den Puffer, das genügt für die Tests.</summary>
internal sealed class OffenAttrappe(GeraetAttrappe geraet, AutoResetEvent? ereignis) : IOffenesSpielgeraet
{
    private readonly nint _griff = ereignis?.SafeWaitHandle.DangerousGetHandle() ?? 0;
    private bool _zu;

    public nint Ereignis => _griff;

    public int WechselLesen(Span<Eingabewechsel> ziel, out bool unvollstaendig) => geraet.WechselLesen(ziel, out unvollstaendig);

    public bool StandLesen(out Eingabestand stand) => geraet.StandLesen(out stand);

    public void Dispose()
    {
        if (_zu) return;
        _zu = true;
        geraet.Schliessen(ereignis);
        if (geraet.SchliessenWirft) Werfen();
    }

    private static void Werfen() => throw new InvalidOperationException("Freigeben scheiterte (Attrappe).");
}
