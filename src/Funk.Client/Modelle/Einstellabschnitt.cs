// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using System.Globalization;
using Funk.Client.Ablage;

namespace Funk.Client.Modelle;

/// <summary>
/// Ein Abschnitt der Seite Einstellungen (seit 0.10.0, Mischung Q87, Bilder m-4 und m-5): Name mit Zeichen, der Kasten "Läuft von selbst"
/// (<see cref="Automatik"/>, aus Variante B; nur gesagt, kein Bedienelement) oder bei Fehlersuche ein Satz (<see cref="Satz"/>), darunter der
/// Klappbereich "Erweitert:" (<see cref="Feinheiten"/>). Was zum Funken gebraucht wird, steht auf Start; unter Senden stehen seit 0.11.1 die
/// Klangmodule und die Funkschwelle offen über dem Klappbereich (Sarevoc 12.10.2026), sonst steht hier nur, was selten jemand anfasst. Seit 0.11.9
/// kann der Kasten statt "Läuft von selbst" einen Hinweis tragen (<see cref="Hinweis"/>, Sprecheranzeige im exklusiven Vollbild).
/// </summary>
public sealed partial class Einstellabschnitt : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <param name="name">Name des Abschnitts, etwa "Senden".</param>
    /// <param name="symbol">Schlüssel des Zeichens in den Symbolen (Design\Symbole.xaml), etwa "Symbol.Senden".</param>
    /// <param name="automatik">Der Satz im Kasten "Läuft von selbst: ..." oder <c>null</c> (Ich, Fehlersuche).</param>
    /// <param name="feinheiten">Der Klappbereich des Abschnitts.</param>
    /// <param name="satz">Statt des Kastens ein schlichter Satz (Fehlersuche), sonst <c>null</c>.</param>
    public Einstellabschnitt(string name, string symbol, string? automatik, Feinheiten feinheiten, string? satz = null)
    {
        ArgumentNullException.ThrowIfNull(feinheiten);
        Name = name;
        Symbol = symbol;
        Automatik = automatik;
        Feinheiten = feinheiten;
        Satz = satz;
    }

    public string Name { get; }

    /// <summary>Schlüssel des Zeichens vor dem Namen; die Ansicht holt es über den Wandler "Ressource" wie die Navigation.</summary>
    public string Symbol { get; }

    /// <summary>Der Satz im Kasten "Läuft von selbst"; <c>null</c>, wenn nichts von selbst läuft.</summary>
    public string? Automatik { get; }

    /// <summary>Ein Satz statt des Kastens (Fehlersuche: "Nur nötig, wenn etwas nicht geht."), sonst <c>null</c>.</summary>
    public string? Satz { get; }

    /// <summary>
    /// Ein Hinweis, der im Kasten an der Stelle von <see cref="Automatik"/> steht, mit dem Zeichen Hinweis statt des Hakens (seit 0.11.9: "Star
    /// Citizen läuft im Vollbild; die Anzeige erscheint nur bei Randlos" unter Sprecheranzeige); <c>null</c> ohne.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(KastenText), nameof(HatHinweis))]
    public partial string? Hinweis { get; set; }

    /// <summary>Der Kasten trägt gerade einen <see cref="Hinweis"/> (dann mit dem Zeichen Hinweis).</summary>
    public bool HatHinweis => Hinweis is not null;

    /// <summary>Was im Kasten steht: der Hinweis, wenn es einen gibt, sonst <see cref="Automatik"/>.</summary>
    public string? KastenText => Hinweis ?? Automatik;

    public Feinheiten Feinheiten { get; }

    /// <summary>Kennung des Abschnitts (die seines Klappbereichs, <see cref="Abschnittskennung"/>).</summary>
    public string Kennung => Feinheiten.Kennung;
}

/// <summary>
/// Ein einzelner Wert als Teil eines Klappbereichs Erweitert (seit 0.10.0, E6): ein Schalter, eine Auswahl oder ein Regler, der woanders gehalten wird (meist in
/// einem Modell der Ansicht). Er liest und schreibt über die mitgegebenen Wege und weiß, was ab Werk gilt; "selbst gestellt" heißt: weicht von ab
/// Werk ab. Geändert meldet er, wenn die Quelle eine ihrer <paramref name="eigenschaften"/> meldet (eine leere Meldung zählt immer) und sich sein Text
/// dadurch geändert hat; so löst das häufige Melden der Livewerte nichts aus.
/// </summary>
public sealed class WertTeil<T> : Einstellteil
{
    private readonly Func<T> _lesen;
    private readonly Action<T> _schreiben;
    private readonly Func<T> _abWerk;
    private readonly Func<T, string> _text;
    private string? _zuletzt;

    /// <param name="lesen">Der Wert, wie er gerade gilt.</param>
    /// <param name="schreiben">Setzt den Wert (mit allem, was dazugehört: Maschine, Einstellungen, Meldungen).</param>
    /// <param name="abWerk">Was ab Werk gilt; als Weg, weil es sich ändern kann (etwa "Beim Start verbinden" nach der Freigabe).</param>
    /// <param name="text">Kurzer Text, wenn er selbst gestellt ist, etwa "Echoschutz aus".</param>
    /// <param name="quelle">Wer Änderungen meldet; ohne Quelle meldet nur <see cref="Pruefen"/>.</param>
    /// <param name="eigenschaften">Die Eigenschaften der Quelle, die diesen Wert betreffen.</param>
    public WertTeil(Func<T> lesen, Action<T> schreiben, Func<T> abWerk, Func<T, string> text, INotifyPropertyChanged? quelle, params string[] eigenschaften)
    {
        ArgumentNullException.ThrowIfNull(lesen);
        ArgumentNullException.ThrowIfNull(schreiben);
        ArgumentNullException.ThrowIfNull(abWerk);
        ArgumentNullException.ThrowIfNull(text);
        _lesen = lesen;
        _schreiben = schreiben;
        _abWerk = abWerk;
        _text = text;
        _zuletzt = Stand();
        if (quelle is not null)
        {
            quelle.PropertyChanged += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.PropertyName) || Array.IndexOf(eigenschaften, e.PropertyName) >= 0) Pruefen();
            };
        }
    }

    /// <summary>Wie <see cref="WertTeil{T}(Func{T}, Action{T}, Func{T}, Func{T, string}, INotifyPropertyChanged?, string[])"/> mit festem Wert ab Werk.</summary>
    public WertTeil(Func<T> lesen, Action<T> schreiben, T abWerk, Func<T, string> text, INotifyPropertyChanged? quelle, params string[] eigenschaften)
        : this(lesen, schreiben, () => abWerk, text, quelle, eigenschaften)
    {
    }

    /// <summary>Der Wert, wie er gerade gilt.</summary>
    public T Wert => _lesen();

    /// <summary>Weicht der Wert von ab Werk ab?</summary>
    public bool Abweichend => !EqualityComparer<T>.Default.Equals(_lesen(), _abWerk());

    public override IReadOnlyList<string> SelbstGestellt => Stand() is { } s ? [s] : [];

    public override void Zuruecksetzen()
    {
        if (Abweichend) _schreiben(_abWerk());
        Pruefen();
    }

    /// <summary>Liest neu und meldet, wenn sich der Stand geändert hat (auch von außen, ohne Meldung der Quelle).</summary>
    public void Pruefen()
    {
        var jetzt = Stand();
        if (string.Equals(jetzt, _zuletzt, StringComparison.Ordinal)) return;
        _zuletzt = jetzt;
        GeaendertMelden();
    }

    private string? Stand() => Abweichend ? _text(_lesen()) : null;
}

/// <summary>Kurze Texte für "selbst gestellt" (<see cref="WertTeil{T}"/>).</summary>
public static class Teiltext
{
    /// <summary>Text eines Schalters, etwa "Echoschutz aus" oder "Mithören an".</summary>
    public static Func<bool, string> Schalter(string name) => an => name + (an ? " an" : " aus");
}

/// <summary>
/// Lautstärke und Seite je Kanal (seit 0.10.0, Teil von Empfangen > Erweitert, E6, E16): Selbst gestellt ist, was in einem Kanal von 0 dB
/// beziehungsweise "beide" abweicht; gezählt wird je Art, wie viele Kanäle. Beides steht nur auf diesem PC (je Kanalnetz in den Einstellungen).
/// "Hören" je Kanal und der Pegel je Person stehen am Server und zählen nicht; ebenso VA oder Taste, das gehört ins Funkfeld. Zurück stellt alle
/// Kanäle auf 0 dB und beide Seiten, über das Funkfeld und die Kanäle, damit Kacheln und Klang gleich folgen.
/// </summary>
public sealed class KanalTeil : Einstellteil
{
    private readonly Einstellungsablage _ablage;
    private readonly FunkfeldViewModel _funkfeld;
    private readonly NetzeViewModel _netze;
    private string _zuletzt;

    public KanalTeil(Einstellungsablage ablage, FunkfeldViewModel funkfeld, NetzeViewModel netze)
    {
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(funkfeld);
        ArgumentNullException.ThrowIfNull(netze);
        _ablage = ablage;
        _funkfeld = funkfeld;
        _netze = netze;
        _zuletzt = string.Join("|", SelbstGestellt);
        funkfeld.NetzGeaendert += _ => Pruefen();
    }

    /// <summary>Kanäle mit eigener Lautstärke.</summary>
    public int MitLautstaerke => Netze().Count(n => Math.Abs(n.Value.LautstaerkeDb) >= 0.01f);

    /// <summary>Kanäle nur links oder nur rechts.</summary>
    public int MitSeite => Netze().Count(n => Math.Abs(n.Value.Panorama) >= 0.01f);

    public override IReadOnlyList<string> SelbstGestellt
    {
        get
        {
            var liste = new List<string>(2);
            if (MitLautstaerke is var l and > 0) liste.Add("Lautstärke in " + Kanaele(l));
            if (MitSeite is var s and > 0) liste.Add("links oder rechts in " + Kanaele(s));
            return liste;
        }
    }

    public override void Zuruecksetzen()
    {
        foreach (var (id, n) in Netze().ToList())
        {
            if (Math.Abs(n.LautstaerkeDb) >= 0.01f) _funkfeld.LautstaerkeSetzen(id, 0);
            if (Math.Abs(n.Panorama) >= 0.01f) _netze.PanoramaSetzen(id, 0);
        }
        Pruefen();
    }

    private void Pruefen()
    {
        var jetzt = string.Join("|", SelbstGestellt);
        if (string.Equals(jetzt, _zuletzt, StringComparison.Ordinal)) return;
        _zuletzt = jetzt;
        GeaendertMelden();
    }

    private IEnumerable<KeyValuePair<ushort, NetzEinstellung>> Netze()
    {
        foreach (var (schluessel, n) in _ablage.Aktuell.Netze)
        {
            if (n is not null && ushort.TryParse(schluessel, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) yield return new(id, n);
        }
    }

    private static string Kanaele(int n) => n == 1 ? "1 Kanal" : n.ToString(CultureInfo.InvariantCulture) + " Kanälen";
}
