// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Client.Modelle;

/// <summary>
/// Die Kennungen der Klappbereiche "Erweitert:" in <see cref="AnzeigeEinstellungen.OffeneFeinheiten"/> (seit 0.10.0, E6): je Abschnitt der
/// Einstellungen eine. Bis 0.9.3 hießen sie nach dem Inhalt, "sprechtaste" und "klangfarbe"; die deutet das Normalisieren einmal um. Der
/// Schlüssel in der Datei behält seinen alten Namen (offeneFeinheiten), damit gemerkte Klappzustände über 0.11.1 hinweg gelten.
/// </summary>
public static class Abschnittskennung
{
    public const string Ich = "ich";
    public const string Tasten = "tasten";
    public const string Empfangen = "empfangen";
    public const string Senden = "senden";
    public const string Funkfeld = "funkfeld";

    /// <summary>Seit 0.11.9 zwischen Funkfeld und Darstellung und Fehlersuche.</summary>
    public const string Sprecheranzeige = "sprecheranzeige";

    /// <summary>Seit 0.12.0 nach Sprecheranzeige: die Meldungen der Leitung über dem Spiel.</summary>
    public const string Meldungen = "meldungen";

    public const string Fehlersuche = "fehlersuche";
}

/// <summary>
/// Ein Teil eines Klappbereichs Erweitert (seit 0.10.0, E6): ein oder mehrere Werte, die du selbst stellen kannst. Er sagt, was davon gerade selbst gestellt
/// ist (<see cref="SelbstGestellt"/>, etwa "Wärme +2 dB"), stellt es zurück auf ab Werk und meldet jede Änderung (<see cref="Geaendert"/>).
/// Gelerntes (Einmessen, gelernte Stimme) und Werte am Server oder am Konto gehören in keinen Teil: Sie zählen nicht als selbst gestellt und
/// bleiben beim Zurückstellen, wie sie sind.
/// </summary>
public abstract class Einstellteil : ObservableObject
{
    /// <summary>Was gerade von ab Werk abweicht, je Wert ein kurzer Text; leer heißt ab Werk.</summary>
    public abstract IReadOnlyList<string> SelbstGestellt { get; }

    /// <summary>Alles in diesem Teil zurück auf ab Werk.</summary>
    public abstract void Zuruecksetzen();

    /// <summary>Ein Wert hat sich geändert (aus dem Teil oder von außen).</summary>
    public event Action? Geaendert;

    /// <summary>Nach jeder Änderung eines Werts: Der Behälter zieht Kopfzeile, Satz und Knopf nach.</summary>
    protected void GeaendertMelden() => Geaendert?.Invoke();
}

/// <summary>
/// Ein Klappbereich "Erweitert:" in den Einstellungen (Sarevoc 11.10.2026; seit 0.10.0 ein Behälter je Abschnitt, E6; bis 0.11.0 hieß er in der
/// Oberfläche anders, der Klassenname blieb): Ein Standardmitglied braucht absolut einfache Bedienung mit Automatik, was selten jemand anfasst,
/// steht hier. Ab Werk zugeklappt; die Kopfzeile beginnt mit dem fetten Wort <see cref="Wort"/>, dann der Stand in Klammern und der Inhalt,
/// "Erweitert: (ab Werk) ..." oder "Erweitert: (1 selbst gestellt) ..." (Sarevoc 12.10.2026), damit der Stand auch im schmalen Fenster in der ersten
/// Zeile steht (aus Variante A). Unten neben "Zurück auf Werkseinstellung" sagt <see cref="StandSatz"/>, was selbst gestellt ist. Ob er offen
/// ist, merken die Einstellungen (<see cref="AnzeigeEinstellungen.OffeneFeinheiten"/>), unter der <see cref="Kennung"/> des Abschnitts.
/// Bis 0.9.3 je Inhalt eine Unterklasse (SprechtasteFeinheiten, KlangfarbeFeinheiten); seit 0.10.0 tragen <see cref="Einstellteil"/> die Werte.
/// Was offen über dem Klappbereich steht (seit 0.11.1 etwa die Schalter unter Senden), gehört in keinen Teil: Es zeigt seinen Stand selbst.
/// Ebenso wenig eine eigene Seite mit eigenen Standardwerten (die Seite Sprachaktivierung, Sarevoc 13.10.2026, Q117); der Hovertext am Knopf
/// sagt das über <see cref="ZurueckZusatz"/>.
/// </summary>
public sealed partial class Feinheiten : ObservableObject
{
    private readonly Einstellungsablage _ablage;
    private readonly Einstellteil[] _teile;

    /// <param name="kennung">Kennung des Abschnitts (<see cref="Abschnittskennung"/>).</param>
    /// <param name="abschnitt">Name des Abschnitts für den Satz, etwa "Senden".</param>
    /// <param name="inhalt">Was darin steht, für die Kopfzeile, etwa "Klangfarbe".</param>
    /// <param name="teile">Die Werte darin.</param>
    /// <param name="zurueckZusatz">Ein Satz mehr im Hovertext am Knopf "Zurück auf Werkseinstellung", etwa was im Abschnitt daneben unberührt bleibt; sonst <c>null</c>.</param>
    public Feinheiten(Einstellungsablage ablage, string kennung, string abschnitt, string inhalt, IEnumerable<Einstellteil> teile, string? zurueckZusatz = null)
    {
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(teile);
        _ablage = ablage;
        Kennung = kennung;
        Abschnitt = abschnitt;
        Inhalt = inhalt;
        ZurueckZusatz = zurueckZusatz;
        _teile = [.. teile];
        foreach (var t in _teile) t.Geaendert += StandGeaendert;
    }

    /// <summary>Kennung in <see cref="AnzeigeEinstellungen.OffeneFeinheiten"/>.</summary>
    public string Kennung { get; }

    /// <summary>Name des Abschnitts, etwa "Senden".</summary>
    public string Abschnitt { get; }

    /// <summary>Was darin steht, für die Kopfzeile.</summary>
    public string Inhalt { get; }

    /// <summary>Der Satz mehr in <see cref="ZurueckHilfe"/>, oder <c>null</c> (seit 0.11.1, Q117; unter Senden die Seite Sprachaktivierung).</summary>
    public string? ZurueckZusatz { get; }

    /// <summary>Die Teile mit den Werten, in der Reihenfolge der Ansicht.</summary>
    public IReadOnlyList<Einstellteil> Teile => _teile;

    /// <summary>Aufgeklappt; ab Werk zu, danach wie zuletzt.</summary>
    public bool Offen
    {
        get => _ablage.Aktuell.Anzeige.OffeneFeinheiten.Contains(Kennung, StringComparer.Ordinal);
        set
        {
            if (value == Offen) return;
            var liste = _ablage.Aktuell.Anzeige.OffeneFeinheiten;
            if (value) liste.Add(Kennung);
            else liste.RemoveAll(k => string.Equals(k, Kennung, StringComparison.Ordinal));
            _ablage.Vormerken();
            OnPropertyChanged();
        }
    }

    /// <summary>Was in allen Teilen selbst gestellt ist, in ihrer Reihenfolge.</summary>
    public IReadOnlyList<string> SelbstGestellt => [.. _teile.SelectMany(t => t.SelbstGestellt)];

    /// <summary>Wie viele Werte selbst gestellt sind.</summary>
    public int AnzahlSelbstGestellt => _teile.Sum(t => t.SelbstGestellt.Count);

    /// <summary>Etwas ist selbst gestellt; nur dann geht "Zurück auf Werkseinstellung".</summary>
    public bool EtwasSelbstGestellt => AnzahlSelbstGestellt > 0;

    /// <summary>Das erste Wort der Kopfzeile; die Ansicht setzt es fett (Sarevoc 12.10.2026: "wir nennen das nun Erweitert: (fett)").</summary>
    public const string Wort = "Erweitert:";

    /// <summary>
    /// Die Kopfzeile, auf- und zugeklappt dieselbe: <see cref="Wort"/>, dann <see cref="Kopfrest"/>, etwa "Erweitert: (1 selbst gestellt) Mithören,
    /// Klangfarbe". So lesen sie Bildschirmleser (Name der Kopfzeile) und die Tests.
    /// </summary>
    public string Kopfzeile => Wort + " " + Kopfrest;

    /// <summary>Was in der Kopfzeile nach dem fetten Wort steht: der Stand in Klammern, dann der Inhalt, etwa "(ab Werk) Klangfarbe".</summary>
    public string Kopfrest => "(" + Stand + ") " + Inhalt;

    /// <summary>
    /// Die <see cref="Kopfzeile"/>. Seit 0.11.1 ist der Behälter selbst der Header des Expanders, und WPF nennt den Klappbereich als Gruppe für
    /// Bildschirmleser nach dem Text seines Headers; ohne das hieß die Gruppe nach dem Typnamen, samt dem alten Wort (Befund der Prüfung 0.11.1).
    /// </summary>
    public override string ToString() => Kopfzeile;

    /// <summary>Der Stand vorn in der Kopfzeile: "ab Werk" oder "1 selbst gestellt".</summary>
    private string Stand => AnzahlSelbstGestellt is var n and > 0 ? n.ToString(CultureInfo.InvariantCulture) + " selbst gestellt" : "ab Werk";

    /// <summary>
    /// Der Satz unten neben dem Knopf: "1 selbst gestellt: Wärme +2 dB. Alles andere steht ab Werk." oder "Alles unter Erweitert steht auf der
    /// Werkseinstellung." Bis 0.11.0 hieß es "Alles in Senden ..."; seit unter Senden Schalter offen über dem Klappbereich stehen (0.11.1), die
    /// er nicht zählt, stimmte das nicht mehr.
    /// </summary>
    public string StandSatz
    {
        get
        {
            var selbst = SelbstGestellt;
            return selbst.Count == 0
                ? "Alles unter Erweitert steht auf der Werkseinstellung."
                : selbst.Count.ToString(CultureInfo.InvariantCulture) + " selbst gestellt: " + string.Join(", ", selbst) + ". Alles andere steht ab Werk.";
        }
    }

    /// <summary>Hilfetext an der Kopfzeile; "Erweitert: " wie die Zeile selbst, denn "für" bräuchte je Inhalt seinen Artikel.</summary>
    public string KopfHilfe => Wort + " " + Inhalt + ". Was selten jemand braucht; ab Werk passt es für fast alle. Aufklappen, um selbst etwas zu stellen. "
        + (EtwasSelbstGestellt ? "Gerade ist etwas selbst gestellt; was, steht aufgeklappt unten neben dem Knopf Zurück auf Werkseinstellung."
            : "Gerade gilt hier überall die Werkseinstellung.");

    /// <summary>
    /// Hilfetext am Knopf "Zurück auf Werkseinstellung", ausführlich und ohne Zahlen wie die übrigen. Seit 0.11.1 "die Automatiken darin" statt
    /// "alle Automatiken" (Befund der Prüfung): Unter Senden bleiben die offenen Schalter darüber stehen, etwa eine ausgeschaltete automatische
    /// Lautstärke oder eine Echounterdrückung von Hand. Dahinter <see cref="ZurueckZusatz"/>, wo es einen gibt.
    /// </summary>
    public string ZurueckHilfe => "Zurück auf Werkseinstellung: Stellt in " + Abschnitt + " alles zurück, was du unter Erweitert selbst gestellt hast. "
        + "Danach laufen die Automatiken darin wieder so, wie sie ab Werk laufen; was offen darüber steht, bleibt, wie es ist. "
        + (ZurueckZusatz is null ? "" : ZurueckZusatz + " ")
        + "Was JG TacRelay gelernt oder eingemessen hat, etwa deine Stimme, bleibt; "
        + "ebenso, was am Server oder an deinem Konto steht. Nimm es, wenn sich etwas verstellt anfühlt und du nicht mehr weißt, was. Gesperrt, "
        + "solange alles ab Werk steht.";

    /// <summary>"Zurück auf Werkseinstellung": alle Teile zurück; nur, wenn etwas selbst gestellt ist.</summary>
    [RelayCommand(CanExecute = nameof(EtwasSelbstGestellt))]
    private void Zurueck()
    {
        foreach (var t in _teile) t.Zuruecksetzen();
    }

    private void StandGeaendert()
    {
        OnPropertyChanged(nameof(SelbstGestellt));
        OnPropertyChanged(nameof(AnzahlSelbstGestellt));
        OnPropertyChanged(nameof(EtwasSelbstGestellt));
        OnPropertyChanged(nameof(Kopfzeile));
        OnPropertyChanged(nameof(Kopfrest));
        OnPropertyChanged(nameof(StandSatz));
        OnPropertyChanged(nameof(KopfHilfe));
        ZurueckCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// Nachlauf der Sprechtaste (Q66, seit 0.9.3; bis 0.9.3 "SprechtasteFeinheiten"): 0 bis 1000 ms in 10-ms-Schritten, ab Werk
/// <see cref="TastenEinstellungen.NachlaufAbWerkMs"/>. Jede Änderung geht sofort an die Klangmaschine und in die Einstellungen; beim Anlegen
/// bekommt die Maschine den gemerkten Wert. Ein Teil von Tasten > Erweitert.
/// </summary>
public sealed class NachlaufTeil : Einstellteil
{
    /// <summary>Kurz unter dem Regler.</summary>
    public const string NachlaufErklaerung = "Sendet nach dem Loslassen noch kurz weiter, damit dein letztes Wort ganz hinausgeht.";

    /// <summary>Hovertext am Regler, ausführlich wie bei den Klangmodulen und wie dort ohne Zahlen.</summary>
    public const string NachlaufHilfe = "Nachlauf der Sprechtaste: Nach dem Loslassen sendet JG TacRelay noch einen Augenblick weiter, als hieltest du "
        + "die Taste. Deine Stimme braucht durch Echounterdrückung, Rauschunterdrückung und Funkkodierung etwas Zeit; ohne Nachlauf fehlt den anderen "
        + "sonst die letzte Silbe, wenn du genau am Wortende loslässt. Mehr, wenn dir die anderen sagen, dass Wortenden fehlen, oder wenn du die Taste "
        + "gern schon beim letzten Wort loslässt. Weniger, wenn nach dem Loslassen noch Geräusche hinterhergehen, etwa ein Atmer oder das Klacken der "
        + "Taste. Drückst du im Nachlauf dieselbe Taste wieder, geht derselbe Spruch nahtlos weiter; Mikro stumm beendet ihn sofort. Der Klick beim "
        + "Loslassen kommt erst danach. Gilt auch für die Melde-Taste, und die Meldung bleibt dabei im Meldekanal. Drückst du im Nachlauf die andere "
        + "der beiden Tasten, endet der Spruch gleich, und der neue beginnt an seinem eigenen Ziel, mit Klick beim Loslassen und beim Drücken. Ab Werk "
        + "passt er für fast alle.";

    private readonly Einstellungsablage _ablage;
    private readonly IKlangMaschine? _maschine;

    /// <param name="maschine">Bekommt den Nachlauf; ohne Maschine (Tests, Entwurf) nur gemerkt.</param>
    public NachlaufTeil(Einstellungsablage ablage, IKlangMaschine? maschine)
    {
        ArgumentNullException.ThrowIfNull(ablage);
        _ablage = ablage;
        _maschine = maschine;
        if (_maschine is not null) _maschine.TastenNachlaufMs = NachlaufMs;
    }

    /// <summary>Nachlauf in ms, wie gemerkt (begrenzt).</summary>
    public int NachlaufMs
    {
        get => _ablage.Aktuell.Tasten.Nachlauf;
        set
        {
            var neu = IKlangMaschine.TastenNachlaufBegrenzen(value);
            if (neu == NachlaufMs) return;
            _ablage.Aktuell.Tasten.NachlaufMs = neu;
            if (_maschine is not null) _maschine.TastenNachlaufMs = neu;
            _ablage.Vormerken();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Nachlauf));
            OnPropertyChanged(nameof(NachlaufText));
            OnPropertyChanged(nameof(SelbstGestellt));
            GeaendertMelden();
        }
    }

    /// <summary>Für den Regler (WPF schiebt in double).</summary>
    public double Nachlauf
    {
        get => NachlaufMs;
        set
        {
            if (double.IsFinite(value)) NachlaufMs = (int)Math.Clamp(Math.Round(value), 0, IKlangMaschine.TastenNachlaufHoechstMs);
        }
    }

    /// <summary>Wert neben dem Regler, etwa "100 ms".</summary>
    public string NachlaufText => NachlaufMs.ToString(CultureInfo.CurrentCulture) + " ms";

    /// <summary>Etwa "Nachlauf 150 ms", wenn er nicht ab Werk steht.</summary>
    public override IReadOnlyList<string> SelbstGestellt =>
        NachlaufMs == TastenEinstellungen.NachlaufAbWerkMs ? [] : ["Nachlauf " + NachlaufMs.ToString(CultureInfo.InvariantCulture) + " ms"];

    public override void Zuruecksetzen() => NachlaufMs = TastenEinstellungen.NachlaufAbWerkMs;
}

/// <summary>
/// Klangfarbe der eigenen Stimme (Q70 bis Q72, seit 0.9.3; bis 0.9.3 "KlangfarbeFeinheiten"): Wärme, Klarheit und Schärfe je -6 bis +6 dB in
/// ganzen dB, ab Werk neutral (<see cref="KlangfarbeEinstellung.Neutral"/>). Jede Änderung geht sofort an die Klangmaschine und in die
/// Einstellungen; beim Anlegen bekommt die Maschine den gemerkten Stand. Ein Teil von Senden > Erweitert, mit eigenem "Zurück auf neutral".
/// </summary>
public sealed partial class KlangfarbeTeil : Einstellteil
{
    /// <summary>Kurz unter den Reglern.</summary>
    public const string Erklaerung = "Ändert nur den Klang deiner Stimme, nicht ihre Lautstärke. Die Hörprobe auf Start hört es mit.";

    /// <summary>Hovertext am Regler Wärme, ausführlich und ohne Zahlen wie bei den Klangmodulen.</summary>
    public const string WaermeHilfe = "Wärme: Hebt die tiefen Anteile deiner Stimme an oder senkt sie ab, den Körper, den eine Stimme im Brustton hat. "
        + "Mehr Wärme macht die Stimme voller und runder, aber nicht tiefer: Deine Stimmlage bleibt, wie sie ist. Gut, wenn du über ein Headset dünn "
        + "oder blechern klingst. Weniger, wenn du nah am Mikrofon dröhnst oder es bei den anderen wummert. Alle drei Regler ändern nur die Klangfarbe, "
        + "nicht die Lautstärke: JG TacRelay gleicht so aus, dass deine Stimme insgesamt so laut bleibt wie vorher. Die Hörprobe hört die Klangfarbe "
        + "mit; so hörst du gleich, wie du bei den anderen ankommst. Ab Werk steht alles in der Mitte, also neutral.";

    /// <summary>Hovertext am Regler Klarheit.</summary>
    public const string KlarheitHilfe = "Klarheit: Hebt die Mitlaute deiner Stimme an oder senkt sie ab, also das, was Wörter verständlich macht, etwa "
        + "t, k, p und f. Mehr Klarheit hilft, wenn dich die anderen im Gefecht neben Schüssen, Triebwerken und Funkverkehr schlecht verstehen. Zu viel "
        + "davon klingt hart und nasal. Weniger, wenn deine Stimme spitz oder anstrengend klingt. Alle drei Regler ändern nur die Klangfarbe, nicht die "
        + "Lautstärke; die Sprachaktivierung und die Pegelanzeige sehen deine Stimme wie vorher. Die Hörprobe hört die Klangfarbe mit.";

    /// <summary>Hovertext am Regler Schärfe.</summary>
    public const string SchaerfeHilfe = "Schärfe: Betont oder dämpft die Zischlaute deiner Stimme, also s, sch und z. Weniger Schärfe, wenn dein Mikrofon "
        + "zischt oder deine Zischlaute bei den anderen scharf in den Ohren klingen. Mehr, wenn deine Stimme dumpf klingt, etwa hinter einem dicken "
        + "Windschutz aus Schaumstoff. Alle drei Regler ändern nur die Klangfarbe, nicht die Lautstärke. Die Hörprobe hört die Klangfarbe mit; stell "
        + "immer nur einen Regler um und hör dann nach.";

    private readonly Einstellungsablage _ablage;
    private readonly IKlangMaschine? _maschine;

    /// <param name="maschine">Bekommt die Klangfarbe; ohne Maschine (Tests, Entwurf) nur gemerkt.</param>
    public KlangfarbeTeil(Einstellungsablage ablage, IKlangMaschine? maschine)
    {
        ArgumentNullException.ThrowIfNull(ablage);
        _ablage = ablage;
        _maschine = maschine;
        if (_maschine is not null) _maschine.Klangfarbe = Einstellung;
    }

    /// <summary>Die gemerkte Klangfarbe (begrenzt).</summary>
    public KlangfarbeEinstellung Einstellung
    {
        get => _ablage.Aktuell.Audio.Klangfarbe;
        set
        {
            var neu = value.Begrenzt();
            if (neu == Einstellung) return;
            _ablage.Aktuell.Audio.Klangfarbe = neu;
            if (_maschine is not null) _maschine.Klangfarbe = neu;
            _ablage.Vormerken();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Waerme));
            OnPropertyChanged(nameof(Klarheit));
            OnPropertyChanged(nameof(Schaerfe));
            OnPropertyChanged(nameof(WaermeText));
            OnPropertyChanged(nameof(KlarheitText));
            OnPropertyChanged(nameof(SchaerfeText));
            OnPropertyChanged(nameof(Neutral));
            OnPropertyChanged(nameof(SelbstGestellt));
            ZurueckCommand.NotifyCanExecuteChanged();
            GeaendertMelden();
        }
    }

    /// <summary>Wärme in dB für den Regler (WPF schiebt in double, gerundet auf ganze dB).</summary>
    public double Waerme
    {
        get => Einstellung.WaermeDb;
        set
        {
            if (double.IsFinite(value)) Einstellung = Einstellung with { WaermeDb = Ganz(value) };
        }
    }

    /// <summary>Klarheit in dB für den Regler.</summary>
    public double Klarheit
    {
        get => Einstellung.KlarheitDb;
        set
        {
            if (double.IsFinite(value)) Einstellung = Einstellung with { KlarheitDb = Ganz(value) };
        }
    }

    /// <summary>Schärfe in dB für den Regler.</summary>
    public double Schaerfe
    {
        get => Einstellung.SchaerfeDb;
        set
        {
            if (double.IsFinite(value)) Einstellung = Einstellung with { SchaerfeDb = Ganz(value) };
        }
    }

    /// <summary>Wert neben dem Regler, etwa "+3 dB", "0 dB" oder "-2 dB".</summary>
    public string WaermeText => Text(Einstellung.WaermeDb);

    /// <inheritdoc cref="WaermeText"/>
    public string KlarheitText => Text(Einstellung.KlarheitDb);

    /// <inheritdoc cref="WaermeText"/>
    public string SchaerfeText => Text(Einstellung.SchaerfeDb);

    /// <summary>Alle drei Regler in der Mitte.</summary>
    public bool Neutral => Einstellung.IstNeutral;

    /// <summary>Was verstellt ist, etwa "Wärme +2 dB" und "Schärfe -1 dB".</summary>
    public override IReadOnlyList<string> SelbstGestellt
    {
        get
        {
            var e = Einstellung;
            var teile = new List<string>(3);
            if (e.WaermeDb != 0) teile.Add("Wärme " + Text(e.WaermeDb));
            if (e.KlarheitDb != 0) teile.Add("Klarheit " + Text(e.KlarheitDb));
            if (e.SchaerfeDb != 0) teile.Add("Schärfe " + Text(e.SchaerfeDb));
            return teile;
        }
    }

    /// <summary>Hilfetext am Knopf "Zurück auf neutral".</summary>
    public const string ZurueckHilfe = "Stellt Wärme, Klarheit und Schärfe zurück auf neutral, die Werkseinstellung. Deine Stimme klingt dann wieder so, "
        + "wie sie aus Mikrofon und Rauschfilter kommt.";

    public override void Zuruecksetzen() => Einstellung = KlangfarbeEinstellung.Neutral;

    /// <summary>"Zurück auf neutral": nur, wenn etwas verstellt ist.</summary>
    [RelayCommand(CanExecute = nameof(NichtNeutral))]
    private void Zurueck() => Zuruecksetzen();

    private bool NichtNeutral() => !Neutral;

    private static int Ganz(double db) => (int)Math.Round(Math.Clamp(db, KlangfarbeEinstellung.MinDb, KlangfarbeEinstellung.MaxDb), MidpointRounding.AwayFromZero);

    /// <summary>Mit Vorzeichen, nur Ziffern und "+" oder "-" (unabhängig von der Kultur, die sonst ein anderes Minuszeichen setzen könnte).</summary>
    private static string Zahl(int db) => (db > 0 ? "+" : "") + db.ToString(CultureInfo.InvariantCulture);

    private static string Text(int db) => Zahl(db) + " dB";
}
