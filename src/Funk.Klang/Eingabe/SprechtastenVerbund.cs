// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Eingabe;

/// <summary>
/// Mehrere Tastenquellen hinter einer: Raw Input für Tastatur und Maus, nach dem Merge DirectInput für Joysticks. Eine
/// weitere Quelle ist eine Zeile (<see cref="Hinzufuegen"/> oder ein Argument mehr im Konstruktor).
/// <para>
/// Verteilen: Jede Zuordnung geht an die erste Quelle, die ihre Art kann. Eine <see cref="ITastenQuelle"/> bekommt alle
/// ihre Zuordnungen, eine einfache <see cref="ISprechtasteQuelle"/> nur die erste Sprechtaste ihrer Art; was keine Quelle
/// bedienen kann (etwa ein Joystick ohne DirectInput-Quelle), steht in <see cref="NichtBedient"/> und bleibt gespeichert.
/// </para>
/// <para>
/// Ereignisse: Die Sprechtaste gilt als gedrückt, solange sie an irgendeiner Quelle gedrückt ist (Tastatur und Joystick
/// zugleich schließen das Mikrofon erst, wenn beide los sind). Neue Zuordnungen und das Freigeben lassen eine gehaltene
/// Sprechtaste los. Netztasten kommen unverändert aus den Quellen durch. Alles aus den Threads der Quellen; Belegen
/// fragt alle Quellen gleichzeitig und nimmt die erste Taste.
/// </para>
/// </summary>
public sealed class SprechtastenVerbund : ITastenQuelle
{
    private sealed record Anschluss(ISprechtasteQuelle Quelle, Action Gedrueckt, Action Losgelassen, Action<Tastenfunktion> Ausgeloest,
        Action<Tastenfunktion, bool> Gehalten);

    private readonly Lock _sperre = new();
    private readonly List<Anschluss> _quellen = [];
    private readonly HashSet<ISprechtasteQuelle> _gedrueckt = [];

    /// <summary>Je gehaltener Funktion außer Sprechen (Melden) die Quellen, an denen sie gerade gedrückt ist.</summary>
    private readonly Dictionary<Tastenfunktion, HashSet<ISprechtasteQuelle>> _gehalten = [];
    private Tastenzuordnung[] _zuordnungen = [];
    private Tastenzuordnung[] _nichtBedient = [];

    public SprechtastenVerbund(params ISprechtasteQuelle[] quellen)
    {
        ArgumentNullException.ThrowIfNull(quellen);
        if (quellen.Length == 0) throw new ArgumentException("Mindestens eine Quelle.", nameof(quellen));
        foreach (var q in quellen) Hinzufuegen(q);
    }

    public event Action? Gedrueckt;

    public event Action? Losgelassen;

    public event Action<Tastenfunktion>? Ausgeloest;

    /// <summary>Melden als ODER über alle Quellen: gedrückt mit der ersten, losgelassen mit der letzten.</summary>
    public event Action<Tastenfunktion, bool>? Gehalten;

    public bool IstGedrueckt
    {
        get
        {
            lock (_sperre) return _gedrueckt.Count > 0;
        }
    }

    /// <summary>Zuordnungen, die gerade keine Quelle bedienen kann.</summary>
    public IReadOnlyList<Tastenzuordnung> NichtBedient
    {
        get
        {
            lock (_sperre) return _nichtBedient;
        }
    }

    /// <summary>Die erste Sprechtaste. Setzen ersetzt alle Sprechtasten; eine Art, die keine Quelle kann, wird abgelehnt.</summary>
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
            if (value is { } b && !Unterstuetzt(b.Art)) throw new NotSupportedException($"Keine Quelle kann {b}.");
            List<Tastenzuordnung> neu;
            lock (_sperre) neu = [.. _zuordnungen.Where(z => z.Funktion != Tastenfunktion.Sprechen)];
            if (value is { } t) neu.Add(new Tastenzuordnung(Tastenfunktion.Sprechen, t));
            Zuordnungen = neu;
        }
    }

    /// <summary>Alle Zuordnungen; Setzen verteilt sie neu auf die Quellen und lehnt nichts ab (siehe <see cref="NichtBedient"/>).</summary>
    public IReadOnlyList<Tastenzuordnung> Zuordnungen
    {
        get
        {
            lock (_sperre) return _zuordnungen;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_sperre) _zuordnungen = [.. value];
            Verteilen();
        }
    }

    public bool Unterstuetzt(EingabeArt art)
    {
        lock (_sperre) return _quellen.Any(a => a.Quelle.Unterstuetzt(art));
    }

    /// <summary>Hängt eine weitere Quelle an (etwa die DirectInput-Sprechtaste) und verteilt die Zuordnungen neu.</summary>
    public void Hinzufuegen(ISprechtasteQuelle quelle)
    {
        ArgumentNullException.ThrowIfNull(quelle);
        var a = new Anschluss(quelle, () => Wechsel(quelle, true), () => Wechsel(quelle, false), f => Ausgeloest?.Invoke(f),
            (f, unten) => GehaltenWechsel(quelle, f, unten));
        quelle.Gedrueckt += a.Gedrueckt;
        quelle.Losgelassen += a.Losgelassen;
        if (quelle is ITastenQuelle t)
        {
            t.Ausgeloest += a.Ausgeloest;
            t.Gehalten += a.Gehalten;
        }
        lock (_sperre) _quellen.Add(a);
        Verteilen();
    }

    public Task<Tastenbelegung> NaechsteTasteAsync(CancellationToken ct) => NaechsteTasteAsync(false, ct);

    /// <summary>Fragt alle Quellen, jede mit <paramref name="vorsichtig"/> (<see cref="ISprechtasteQuelle.NaechsteTasteAsync(bool, CancellationToken)"/>).</summary>
    public async Task<Tastenbelegung> NaechsteTasteAsync(bool vorsichtig, CancellationToken ct)
    {
        List<ISprechtasteQuelle> quellen;
        lock (_sperre) quellen = [.. _quellen.Select(a => a.Quelle)];
        using var abbruch = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var aufgaben = quellen.Select(q => q.NaechsteTasteAsync(vorsichtig, abbruch.Token)).ToList();
        try
        {
            while (aufgaben.Count > 0)
            {
                var fertig = await Task.WhenAny(aufgaben).ConfigureAwait(false);
                if (fertig.IsCompletedSuccessfully) return fertig.Result;
                aufgaben.Remove(fertig);
            }
            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException("Keine Quelle lieferte eine Taste.");
        }
        finally
        {
            await abbruch.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Gibt die Quellen frei; war die Sprechtaste gedrückt, kommt zuletzt ein <see cref="Losgelassen"/>.</summary>
    public void Dispose()
    {
        List<Anschluss> quellen;
        lock (_sperre)
        {
            quellen = [.. _quellen];
            _quellen.Clear();
        }
        foreach (var a in quellen)
        {
            a.Quelle.Gedrueckt -= a.Gedrueckt;
            a.Quelle.Losgelassen -= a.Losgelassen;
            if (a.Quelle is ITastenQuelle t)
            {
                t.Ausgeloest -= a.Ausgeloest;
                t.Gehalten -= a.Gehalten;
            }
            a.Quelle.Dispose();
        }
        // Das Loslassen, das eine Quelle beim Freigeben meldet, kommt nach dem Abmelden nicht mehr an; ohne diese Meldung
        // bliebe das Mikrofon der Maschine offen.
        Loslassen();
    }

    /// <summary>Vergisst alle gedrückten Quellen und meldet das Loslassen von Sprechtaste und gehaltenen Funktionen.</summary>
    private void Loslassen()
    {
        bool sprechen;
        List<Tastenfunktion> gehalten;
        lock (_sperre)
        {
            sprechen = _gedrueckt.Count > 0;
            _gedrueckt.Clear();
            gehalten = [.. _gehalten.Where(p => p.Value.Count > 0).Select(p => p.Key)];
            _gehalten.Clear();
        }
        if (sprechen) Losgelassen?.Invoke();
        foreach (var f in gehalten) Gehalten?.Invoke(f, false);
    }

    /// <summary>
    /// Verteilt die Zuordnungen neu. Wie bei <see cref="ITastenQuelle.Zuordnungen"/> gelten gehaltene Tasten danach als
    /// losgelassen: Eine einfache Quelle, deren Belegung wechselt, meldet ihr Loslassen sonst womöglich nie, und das
    /// Mikrofon bliebe offen. Ein späteres Loslassen der Quelle zählt dann nicht noch einmal.
    /// </summary>
    private void Verteilen()
    {
        Loslassen();
        List<Anschluss> quellen;
        Tastenzuordnung[] alle;
        lock (_sperre)
        {
            quellen = [.. _quellen];
            alle = _zuordnungen;
        }
        var bedient = new bool[alle.Length];
        foreach (var a in quellen)
        {
            var eigene = new List<int>();
            for (var i = 0; i < alle.Length; i++)
            {
                var zustaendig = quellen.FirstOrDefault(x => x.Quelle.Unterstuetzt(alle[i].Taste.Art));
                if (ReferenceEquals(zustaendig, a)) eigene.Add(i);
            }
            if (a.Quelle is ITastenQuelle t)
            {
                t.Zuordnungen = [.. eigene.Select(i => alle[i])];
                foreach (var i in eigene) bedient[i] = true;
            }
            else
            {
                var sprechen = eigene.FindIndex(i => alle[i].Funktion == Tastenfunktion.Sprechen);
                a.Quelle.Belegung = sprechen >= 0 ? alle[eigene[sprechen]].Taste : null;
                if (sprechen >= 0) bedient[eigene[sprechen]] = true;
            }
        }
        lock (_sperre) _nichtBedient = [.. alle.Where((_, i) => !bedient[i])];
    }

    private void Wechsel(ISprechtasteQuelle quelle, bool gedrueckt)
    {
        bool melden;
        lock (_sperre)
        {
            if (gedrueckt) melden = _gedrueckt.Add(quelle) && _gedrueckt.Count == 1;
            else melden = _gedrueckt.Remove(quelle) && _gedrueckt.Count == 0;
        }
        if (!melden) return;
        if (gedrueckt) Gedrueckt?.Invoke();
        else Losgelassen?.Invoke();
    }

    private void GehaltenWechsel(ISprechtasteQuelle quelle, Tastenfunktion funktion, bool unten)
    {
        bool melden;
        lock (_sperre)
        {
            if (!_gehalten.TryGetValue(funktion, out var quellen)) _gehalten[funktion] = quellen = [];
            if (unten) melden = quellen.Add(quelle) && quellen.Count == 1;
            else melden = quellen.Remove(quelle) && quellen.Count == 0;
        }
        if (melden) Gehalten?.Invoke(funktion, unten);
    }
}
