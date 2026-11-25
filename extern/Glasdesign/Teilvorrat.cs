// Glasdesign - Maintainer: Sarevoc
namespace Glasdesign;

/// <summary>
/// Ein Vorrat zum Teilen (seit 0.5.0-beta.13, Teilschritt A7): die zuletzt abgelegten Werte je Schlüssel, damit gleiche
/// Elemente einen Bau oder ein Bild nur einmal rechnen (Familienkörper, Schatten und Zwischenbild der Sprechblase), wie
/// Glanzbild und Saumbild seit beta.5. Begrenzt auf <c>hoechstens</c> Werte und <c>hoechstensBytes</c> (gemessen mit
/// <c>groesse</c>); die zuletzt gebrauchten bleiben, die ältesten fallen heraus. Ein Element behält einen herausgefallenen
/// Wert, nur teilen kann ihn danach keiner mehr. Nicht fadensicher: je Faden ein Vorrat ([ThreadStatic]), weil WPF-Elemente
/// an ihrem Faden hängen. Die Werte sind nach dem Ablegen unveränderlich (eingefroren).
/// </summary>
internal sealed class Teilvorrat<T>(int hoechstens, long hoechstensBytes = long.MaxValue, Func<T, long>? groesse = null) where T : class
{
    private sealed class Eintrag(object schluessel, T wert)
    {
        public object Schluessel { get; } = schluessel;
        public T Wert { get; } = wert;
        public long Bytes { get; set; }
    }

    private readonly Dictionary<object, LinkedListNode<Eintrag>> _nach = [];
    private readonly LinkedList<Eintrag> _reihe = new();
    private long _bytes;

    /// <summary>Wie viele Werte bereitliegen.</summary>
    public int Zahl => _reihe.Count;

    /// <summary>Wie viele Bytes die bereitliegenden Werte belegen.</summary>
    public long Bytes => _bytes;

    /// <summary>Wie oft ein Wert geteilt wurde (für die Tests).</summary>
    public int Treffer { get; private set; }

    /// <summary>Der abgelegte Wert zum <paramref name="schluessel"/> (er rückt nach hinten, zu den zuletzt gebrauchten), sonst null.</summary>
    public T? Holen(object schluessel)
    {
        if (!_nach.TryGetValue(schluessel, out var knoten)) return null;
        _reihe.Remove(knoten);
        _reihe.AddLast(knoten);
        Treffer++;
        return knoten.Value.Wert;
    }

    /// <summary>Legt <paramref name="wert"/> unter <paramref name="schluessel"/> ab (ein schon abgelegter bleibt).</summary>
    public void Ablegen(object schluessel, T wert)
    {
        if (_nach.ContainsKey(schluessel)) return;
        var eintrag = new Eintrag(schluessel, wert) { Bytes = groesse?.Invoke(wert) ?? 0 };
        _nach[schluessel] = _reihe.AddLast(eintrag);
        _bytes += eintrag.Bytes;
        Kuerzen();
    }

    /// <summary>Der abgelegte <paramref name="wert"/> ist gewachsen (etwa ein Bau, der sein Bild bekommen hat): neu bemessen.</summary>
    public void Bemessen(object schluessel, T wert)
    {
        if (groesse is null || !_nach.TryGetValue(schluessel, out var knoten) || !ReferenceEquals(knoten.Value.Wert, wert)) return;
        var neu = groesse(wert);
        _bytes += neu - knoten.Value.Bytes;
        knoten.Value.Bytes = neu;
        Kuerzen();
    }

    /// <summary>Alles herausnehmen (für die Tests).</summary>
    public void Leeren()
    {
        _nach.Clear();
        _reihe.Clear();
        _bytes = 0;
    }

    private void Kuerzen()
    {
        while (_reihe.Count > hoechstens || (_bytes > hoechstensBytes && _reihe.Count > 1))
        {
            var alt = _reihe.First!.Value;
            _reihe.RemoveFirst();
            _nach.Remove(alt.Schluessel);
            _bytes -= alt.Bytes;
        }
    }
}
