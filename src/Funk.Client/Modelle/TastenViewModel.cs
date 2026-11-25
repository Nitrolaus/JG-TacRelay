// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Klang.Eingabe;

namespace Funk.Client.Modelle;

/// <summary>
/// Globale Tasten in den Einstellungen (A3, A5): je Funktion (Sprechen, Sendenetz vor und zurück, seit 0.5.0 Melden und Funkfeld
/// ablegen/holen, seit Klangstufe C2b Dauersender stumm und Mikro stumm, seit 0.12.0 Bereit, im Abschnitt Einsatzleitung Phase vor und zurück) beliebig viele Tasten. "Belegen" wartet auf die nächste Taste
/// der Quelle (Tastatur, Maus 1-5, Joystick, wenn eine Quelle ihn kann), Esc bricht ab, nach <see cref="Frist"/> ohne Taste ebenso.
/// Eine Taste gehört nur einer Funktion; wer sie woanders belegt, verschiebt sie. Jede Änderung geht sofort an die Quelle und in
/// die Einstellungen. Darunter die Wahl, in welches Netz die Melde-Taste spricht (RAID-S2-ENTWURF Abschnitt 4). Seit 0.9.3 der Nachlauf der
/// Sprechtaste (<see cref="NachlaufTeil"/>, Q66). Seit 0.10.0 steht das alles unter Einstellungen > Tasten > Erweitert, in Gruppen
/// (<see cref="Sprechen"/>, <see cref="KanalTasten"/>, <see cref="WeitereTasten"/>, die Einsatzleitung nur mit Recht, <see cref="LeitungSichtbar"/>,
/// E18); den Klappbereich baut das Modell der Einstellungen (Kennung "tasten"). Seit 0.10.0 (AP2) belegt auch die Startseite die Sprechtaste über
/// dieses Modell: beim ersten Start ohne Frist (E8), "Ändern" ersetzt alle bisherigen (E17), und <see cref="WartenBeenden"/> beendet ein Warten
/// still, wenn Start nicht mehr vorn ist. Eine neu belegte Sprechtaste erledigt den Schritt des ersten Starts
/// (<see cref="TastenEinstellungen.ErststartSprechtaste"/>).
/// </summary>
public sealed partial class TastenViewModel : ObservableObject, IDisposable
{
    /// <summary>So lange wartet "Belegen" auf eine Taste.</summary>
    public static readonly TimeSpan Frist = TimeSpan.FromSeconds(10);

    private const int Escape = 0x0001;

    private readonly Einstellungsablage _ablage;
    private readonly ITastenQuelle? _quelle;
    private readonly Func<Tastenbelegung, string> _namen;
    private readonly TimeSpan _frist;
    private readonly IFunkDienst? _dienst;
    private readonly IOberflaechentakt? _takt;
    private CancellationTokenSource? _belegen;
    private bool _wahlIntern;

    /// <param name="quelle">Die globalen Tasten (im Betrieb der Verbund mit Raw Input); null, wenn es keine gibt.</param>
    /// <param name="namen">Anzeigename einer Taste (im Betrieb aus dem Tastaturlayout).</param>
    /// <param name="frist">Wartezeit beim Belegen (Tests kürzer).</param>
    /// <param name="dienst">Der Funkbetrieb, für die Netze der Melde-Taste; ohne ihn bleibt die Wahl leer.</param>
    /// <param name="takt">Bringt Änderungen der Lage auf den Oberflächen-Thread.</param>
    /// <param name="maschine">Die Klangmaschine für den Nachlauf der Sprechtaste (Q66); ohne sie wird der Wert nur gemerkt.</param>
    public TastenViewModel(Einstellungsablage ablage, ITastenQuelle? quelle, Func<Tastenbelegung, string>? namen = null, TimeSpan? frist = null,
        IFunkDienst? dienst = null, IOberflaechentakt? takt = null, Funk.Klang.Maschine.IKlangMaschine? maschine = null)
    {
        ArgumentNullException.ThrowIfNull(ablage);
        _ablage = ablage;
        _quelle = quelle;
        _namen = namen ?? Tastennamen.Kurz;
        _frist = frist ?? Frist;
        _dienst = dienst;
        _takt = takt;
        // Q66 (0.9.3): der Nachlauf gibt der Maschine gleich den gemerkten Wert; seit 0.10.0 ein Teil von Tasten > Erweitert.
        Nachlauf = new NachlaufTeil(ablage, maschine);
        Funktionen =
        [
            new TastenFunktionZeile(this, Tastenfunktion.Sprechen, "Sprechtaste", "Solange gedrückt, sendest du in deinen Sendekanal."),
            new TastenFunktionZeile(this, Tastenfunktion.NetzVor, "Sendekanal vor", "Wie + im Funkfeld: nächster eigener Kanal."),
            new TastenFunktionZeile(this, Tastenfunktion.NetzZurueck, "Sendekanal zurück", "Wie - im Funkfeld: voriger eigener Kanal."),
            new TastenFunktionZeile(this, Tastenfunktion.Melden, "Melde-Taste",
                "Solange gedrückt, sprichst du in den Meldekanal (unten), etwa an Kommando. Dein Sendekanal bleibt."),
            new TastenFunktionZeile(this, Tastenfunktion.FunkfeldUmschalten, "Funkfeld ablegen/holen",
                "Ein Druck legt das Funkfeld in den Infobereich oder holt es zurück, ohne Star Citizen den Fokus zu nehmen."),
            // Klangstufe C2b (Q8, Q32): ab Werk unbelegt.
            new TastenFunktionZeile(this, Tastenfunktion.DauersenderStumm, "Dauersender stumm",
                "Ein Druck schaltet den Funker stumm, der zuletzt als Dauersender leiser gestellt wurde; ein zweiter Druck macht ihn wieder normal."),
            new TastenFunktionZeile(this, Tastenfunktion.MikroStumm, "Mikro stumm",
                "Ein Druck schaltet dein Mikrofon in JG TacRelay stumm, auch für die Sprechtaste; ein zweiter wieder an. Windows bleibt unberührt."),
            // Meldesystem (0.12.0): ab Werk unbelegt.
            new TastenFunktionZeile(this, Tastenfunktion.Bereit, "Bereit",
                "Fragt die Leitung, wer bereit ist, meldet dich ein Druck bereit, auch wenn das Spiel vorn ist. Ohne offene Abfrage passiert nichts."),
        ];
        Leitung =
        [
            new TastenFunktionZeile(this, Tastenfunktion.PhaseVor, "Phase vor",
                "Einmal drücken macht die nächste Phase scharf, noch einmal (bis 5 s) schaltet sie."),
            new TastenFunktionZeile(this, Tastenfunktion.PhaseZurueck, "Phase zurück",
                "Einmal drücken macht die vorige Phase scharf, noch einmal (bis 5 s) schaltet sie."),
        ];
        Sprechen = Funktionen[0];
        KanalTasten = [.. Funktionen.Where(f => f.Funktion is Tastenfunktion.NetzVor or Tastenfunktion.NetzZurueck or Tastenfunktion.Melden)];
        WeitereTasten = [.. Funktionen.Where(f => f.Funktion is Tastenfunktion.FunkfeldUmschalten or Tastenfunktion.DauersenderStumm or Tastenfunktion.MikroStumm
            or Tastenfunktion.Bereit)];
        QuelleSetzen();
        Aufbauen();
        if (_dienst is not null) _dienst.LageGeaendert += LageGeaendert;
        MeldenetzeAbgleichen();
    }

    /// <summary>Die Zuordnungen haben sich geändert (etwa für das Funkfeld, das + und - dann nicht selbst schaltet).</summary>
    public event Action? Geaendert;

    /// <summary>Funktionen für jeden: Sprechen, Sendenetz, Melden, Funkfeld.</summary>
    public IReadOnlyList<TastenFunktionZeile> Funktionen { get; }

    /// <summary>Die Sprechtaste (Gruppe "Sprechtaste" unter Einstellungen > Tasten > Erweitert, seit 0.10.0 mit dem Nachlauf darunter).</summary>
    public TastenFunktionZeile Sprechen { get; }

    /// <summary>Gruppe "Kanal und Melde-Taste": Sendekanal vor und zurück, Melde-Taste (darunter "Melde-Taste spricht in").</summary>
    public IReadOnlyList<TastenFunktionZeile> KanalTasten { get; }

    /// <summary>Gruppe "Weitere Tasten": Funkfeld ablegen/holen, Dauersender stumm, Mikro stumm, seit 0.12.0 Bereit.</summary>
    public IReadOnlyList<TastenFunktionZeile> WeitereTasten { get; }

    /// <summary>Nachlauf der Sprechtaste (Q66), ein Teil von Tasten > Erweitert.</summary>
    public NachlaufTeil Nachlauf { get; }

    /// <summary>Abschnitt Einsatzleitung: Phase vor und zurück; auch ohne Auftrag vorab belegbar, zu sehen aber nur mit Recht (<see cref="LeitungSichtbar"/>).</summary>
    public IReadOnlyList<TastenFunktionZeile> Leitung { get; }

    /// <summary>
    /// Die Gruppe Einsatzleitung (Phase vor und zurück) steht nur da, solange du die Einsatzleitung hast (seit 0.10.0, E18, Nebenbefund 1 der Inventur):
    /// dieselbe Bedingung wie das Brett (<see cref="BrettViewModel.Gilt"/>). Belegte Tasten bleiben gemerkt, auch wenn die Gruppe nicht zu sehen ist.
    /// </summary>
    [ObservableProperty]
    public partial bool LeitungSichtbar { get; private set; }

    /// <summary>Hinweis unter den Tasten: Was das Programm liest, sieht Star Citizen auch.</summary>
    public static string SpielHinweis { get; } =
        "Knöpfe und Tasten, die du hier belegst, sieht Star Citizen genauso; JG TacRelay fängt nichts ab. Nimm Knöpfe, die im Spiel frei sind, "
        + "etwa eine Shift-Ebene der Joystick-Software.";

    /// <summary>"Melde-Taste spricht in": ab Werk Kommando (wenn die Flotte eins hat), sonst jedes Netz der Flotte.</summary>
    public ObservableCollection<MeldenetzWahl> Meldenetze { get; } = [];

    [ObservableProperty]
    public partial MeldenetzWahl? GewaehltesMeldenetz { get; set; }

    partial void OnGewaehltesMeldenetzChanged(MeldenetzWahl? value)
    {
        if (_wahlIntern || value is null) return;
        var tasten = _ablage.Aktuell.Tasten;
        if (tasten.Meldenetz == value.NetzId) return;
        tasten.Meldenetz = value.NetzId;
        _ablage.Vormerken();
    }

    private void LageGeaendert()
    {
        if (_takt is null) MeldenetzeAbgleichen();
        else _takt.Einreihen(MeldenetzeAbgleichen);
    }

    /// <summary>Die Wahl nur ersetzen, wenn sich die Netze geändert haben (sonst ginge eine offene Auswahl zu); dazu, ob die Einsatzleitung zu sehen ist.</summary>
    private void MeldenetzeAbgleichen()
    {
        var lage = _dienst?.Lage ?? FunkLage.Leer;
        LeitungSichtbar = BrettViewModel.Gilt(lage);
        var soll = Meldenetzwahl.Wahlen(lage, _ablage.Aktuell.Tasten.Meldenetz);
        _wahlIntern = true;
        try
        {
            if (!Meldenetze.SequenceEqual(soll))
            {
                Meldenetze.Clear();
                foreach (var w in soll) Meldenetze.Add(w);
            }
            var gewaehlt = Meldenetze.FirstOrDefault(w => w.NetzId == _ablage.Aktuell.Tasten.Meldenetz) ?? Meldenetze.FirstOrDefault();
            if (!Equals(GewaehltesMeldenetz, gewaehlt)) GewaehltesMeldenetz = gewaehlt;
        }
        finally
        {
            _wahlIntern = false;
        }
    }

    /// <summary>Globale Tasten sind verfügbar (Raw Input läuft).</summary>
    public bool Verfuegbar => _quelle is not null;

    public bool JoystickMoeglich => _quelle?.Unterstuetzt(EingabeArt.Joystick) == true;

    /// <summary>Was sich belegen lässt, für den Hinweistext.</summary>
    public string ArtenText => JoystickMoeglich ? "Tastatur, Maustasten 1 bis 5 und Joystick" : "Tastatur und Maustasten 1 bis 5";

    /// <summary>Eine Netztaste (Sendenetz vor oder zurück) ist belegt und wird bedient.</summary>
    public bool NetztastenBelegt => _quelle is not null
        && Funktionen.Where(f => f.Funktion is Tastenfunktion.NetzVor or Tastenfunktion.NetzZurueck).Any(f => f.Belegungen.Any(b => b.Verfuegbar));

    /// <summary>Es gibt keine Sprechtaste; dann geht bei Netzen auf Taste nichts hinaus.</summary>
    public bool OhneSprechtaste => Funktionen[0].Belegungen.Count == 0;

    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    public void Dispose()
    {
        if (_dienst is not null) _dienst.LageGeaendert -= LageGeaendert;
        _belegen?.Cancel();
        _belegen?.Dispose();
        _belegen = null;
    }

    /// <summary>Gesperrt-Grund von Belegen und Ändern, wenn es keine globalen Tasten gibt (etwa in der Vorführung).</summary>
    public const string OhneQuelleGrund = "Globale Tasten gibt es hier nicht, etwa in der Vorführung; ohne sie lässt sich keine Taste belegen.";

    /// <summary>
    /// Wartet auf die nächste Taste der Quelle und belegt damit <paramref name="zeile"/>; ein zweiter Aufruf, solange sie wartet, bricht ab.
    /// <paramref name="frist"/>: wie lange, ohne Angabe <see cref="Frist"/>; <see cref="Timeout.InfiniteTimeSpan"/> wartet ohne Frist (der Schritt
    /// Sprechtaste beim ersten Start, E8). <paramref name="ersetzen"/>: Die neue Taste ersetzt alle bisherigen derselben Funktion ("Ändern" auf
    /// Start, E17); Esc lässt alles stehen. <paramref name="vorsichtig"/>: Tasten, mit denen man das Fenster bedient, zählen nicht, Strg, Alt,
    /// Umschalt und Windows nur allein (der Schritt beim ersten Start, <see cref="ISprechtasteQuelle.NaechsteTasteAsync(bool, CancellationToken)"/>).
    /// Liefert, wie das Warten endete.
    /// </summary>
    internal async Task<Belegergebnis> BelegenAsync(TastenFunktionZeile zeile, TimeSpan? frist = null, bool ersetzen = false, bool vorsichtig = false)
    {
        if (zeile.Wartet)
        {
            _vomNutzer = true;
            _belegen?.Cancel();
            return Belegergebnis.Abgebrochen;
        }
        if (_quelle is null)
        {
            Hinweis = "Globale Tasten sind hier nicht verfügbar.";
            return Belegergebnis.OhneQuelle;
        }
        _belegen?.Cancel();
        foreach (var f in Alle) f.Wartet = false;
        zeile.Wartet = true;
        using var abbruch = new CancellationTokenSource(frist ?? _frist);
        _belegen = abbruch;
        _vomNutzer = false;
        Hinweis = "Drücke jetzt die Taste für \"" + zeile.Titel + "\" (" + ArtenText + "). Esc bricht ab. Linke, rechte und mittlere Maustaste "
            + "zählen nur außerhalb dieses Programms; ein Klick hier bedient die Oberfläche.";
        try
        {
            var taste = await _quelle.NaechsteTasteAsync(vorsichtig, abbruch.Token).ConfigureAwait(true);
            if (taste is { Art: EingabeArt.Tastatur, Code: Escape })
            {
                Hinweis = "Abgebrochen.";
                return Belegergebnis.Abgebrochen;
            }
            if (ersetzen) Ersetzen(zeile.Funktion, taste);
            else Hinzufuegen(zeile.Funktion, taste);
            return Belegergebnis.Belegt;
        }
        catch (OperationCanceledException)
        {
            // Ein neues Belegen hat dieses abgelöst, oder es wurde still beendet: Der Hinweis bleibt, wie er ist.
            if (!ReferenceEquals(_belegen, abbruch)) return Belegergebnis.Beendet;
            Hinweis = _vomNutzer ? "Abgebrochen." : "Keine Taste erkannt, nichts geändert.";
            return _vomNutzer ? Belegergebnis.Abgebrochen : Belegergebnis.Frist;
        }
        finally
        {
            // Nur das Warten, das noch gilt: Ein abgelöstes käme hier erst nach dem neuen an und nähme ihm sonst sein "wartet".
            if (ReferenceEquals(_belegen, abbruch))
            {
                zeile.Wartet = false;
                _belegen = null;
            }
        }
    }

    /// <summary>
    /// Beendet ein laufendes Warten still, ohne Hinweis und nicht als Abbruch des Nutzers (seit 0.10.0): Die Startseite wartet beim ersten Start
    /// nur, solange sie vorn ist, und nie im Hintergrund (E8).
    /// </summary>
    /// <remarks>
    /// Sofort und nicht erst, wenn das Warten nach dem Abbruch ankommt: So kann die Startseite gleich danach wieder warten (zurück auf Start,
    /// Fenster wieder vorn), ohne dass ihr Aufruf als zweiter Druck auf "Abbrechen" gilt.
    /// </remarks>
    internal void WartenBeenden()
    {
        if (_belegen is not { } laufend) return;
        _belegen = null;
        foreach (var f in Alle) f.Wartet = false;
        Hinweis = null;
        laufend.Cancel();
    }

    private bool _vomNutzer;

    /// <summary>Alle Zeilen, auch die der Einsatzleitung.</summary>
    private IEnumerable<TastenFunktionZeile> Alle => Funktionen.Concat(Leitung);

    /// <summary>
    /// Belegt <paramref name="taste"/> für <paramref name="funktion"/>; war sie anderswo belegt, wandert sie. Die Funktionen ab 0.5.0
    /// landen in der eigenen Liste (<see cref="TastenEinstellungen.Weitere"/>), damit ein Client 0.4.0 die Datei weiter liest.
    /// </summary>
    internal void Hinzufuegen(Tastenfunktion funktion, Tastenbelegung taste)
    {
        var tasten = _ablage.Aktuell.Tasten;
        var bisher = tasten.FunktionVon(taste);
        if (bisher == funktion)
        {
            Hinweis = _namen(taste) + " ist hier schon belegt.";
            return;
        }
        if (bisher is { } alt)
        {
            Hinweis = _namen(taste) + " war \"" + Titel(alt) + "\" und ist jetzt \"" + Titel(funktion) + "\"." + SpielZusatz(taste);
            _ = tasten.Entfernen(taste);
        }
        else if (tasten.Anzahl(funktion) >= TastenEinstellungen.MaxEintraege)
        {
            Hinweis = "Höchstens " + TastenEinstellungen.MaxEintraege.ToString(CultureInfo.CurrentCulture) + " Tasten.";
            return;
        }
        else
        {
            Hinweis = _namen(taste) + ": " + Titel(funktion) + "." + SpielZusatz(taste);
        }
        tasten.Hinzufuegen(new Tastenzuordnung(funktion, taste));
        _ = SchrittErledigen(funktion);
        Anwenden();
    }

    /// <summary>
    /// "Ändern" auf Start (seit 0.10.0, E17): <paramref name="taste"/> wird die einzige Taste von <paramref name="funktion"/>; die bisherigen
    /// fallen weg. War sie anderswo belegt, wandert sie wie beim Belegen.
    /// </summary>
    internal void Ersetzen(Tastenfunktion funktion, Tastenbelegung taste)
    {
        var tasten = _ablage.Aktuell.Tasten;
        var bisher = tasten.Zuordnungen().Where(z => z.Funktion == funktion && !z.Taste.GleicheTaste(taste)).ToList();
        foreach (var z in bisher) _ = tasten.Entfernen(z.Taste, funktion);
        if (tasten.FunktionVon(taste) != funktion)
        {
            Hinzufuegen(funktion, taste);
            return;
        }
        Hinweis = _namen(taste) + ": " + Titel(funktion) + ".";
        var erledigt = SchrittErledigen(funktion);
        if (erledigt || bisher.Count > 0) Anwenden();
    }

    /// <summary>Eine belegte Sprechtaste erledigt den Schritt Sprechtaste des ersten Starts (E8), wenn er offen ist; wahr, wenn sie das tat.</summary>
    private bool SchrittErledigen(Tastenfunktion funktion)
    {
        var tasten = _ablage.Aktuell.Tasten;
        if (funktion != Tastenfunktion.Sprechen || tasten.ErststartSprechtaste != false) return false;
        tasten.ErststartSprechtaste = true;
        return true;
    }

    /// <summary>Beim Joystick der Hinweis, dass Star Citizen denselben Knopf sieht (RAID-S2-ENTWURF, Fehlbedienung).</summary>
    private static string SpielZusatz(Tastenbelegung taste) =>
        taste.Art == EingabeArt.Joystick ? " Ist der Knopf in Star Citizen belegt, löst er dort die Spielaktion mit aus." : "";

    internal void Loeschen(Tastenzuordnung zuordnung)
    {
        var n = _ablage.Aktuell.Tasten.Entfernen(zuordnung.Taste, zuordnung.Funktion);
        if (n == 0) return;
        Hinweis = null;
        Anwenden();
    }

    private string Titel(Tastenfunktion f) => Alle.First(z => z.Funktion == f).Titel;

    private void Anwenden()
    {
        _ablage.Vormerken();
        QuelleSetzen();
        Aufbauen();
        Geaendert?.Invoke();
    }

    /// <summary>Alle gespeicherten Zuordnungen an die Quelle; was keine Quelle kann, bleibt gespeichert und grau.</summary>
    private void QuelleSetzen()
    {
        if (_quelle is null) return;
        var alle = _ablage.Aktuell.Tasten.Zuordnungen();
        _quelle.Zuordnungen = _quelle is SprechtastenVerbund ? alle : [.. alle.Where(z => _quelle.Unterstuetzt(z.Taste.Art))];
    }

    private void Aufbauen()
    {
        var nichtBedient = _quelle is SprechtastenVerbund v ? v.NichtBedient : [];
        var alle = _ablage.Aktuell.Tasten.Zuordnungen();
        foreach (var f in Alle)
        {
            f.Belegungen.Clear();
            foreach (var z in alle.Where(z => z.Funktion == f.Funktion))
            {
                var bedient = _quelle is not null && _quelle.Unterstuetzt(z.Taste.Art) && !nichtBedient.Contains(z);
                f.Belegungen.Add(new BelegungZeile(this, z, _namen(z.Taste), bedient));
            }
        }
        OnPropertyChanged(nameof(NetztastenBelegt));
        OnPropertyChanged(nameof(OhneSprechtaste));
    }
}

/// <summary>Wie ein Warten auf eine Taste endete (seit 0.10.0, für den Schritt Sprechtaste auf Start); nur im Speicher, nie in der Datei.</summary>
public enum Belegergebnis
{
    /// <summary>Eine Taste kam und ist belegt (oder war es schon).</summary>
    Belegt,

    /// <summary>Esc oder ein zweiter Druck auf "Abbrechen": Der Nutzer wollte nicht.</summary>
    Abgebrochen,

    /// <summary>In der Frist kam keine Taste.</summary>
    Frist,

    /// <summary>Still beendet (<see cref="TastenViewModel.WartenBeenden"/>) oder von einem neuen Warten abgelöst.</summary>
    Beendet,

    /// <summary>Es gibt keine globalen Tasten.</summary>
    OhneQuelle,
}

/// <summary>Eine Funktion mit ihren Tasten und dem Knopf "Belegen" (während des Wartens "Abbrechen").</summary>
public sealed partial class TastenFunktionZeile : ObservableObject
{
    private readonly TastenViewModel _tasten;

    internal TastenFunktionZeile(TastenViewModel tasten, Tastenfunktion funktion, string titel, string erklaerung)
    {
        _tasten = tasten;
        Funktion = funktion;
        Titel = titel;
        Erklaerung = erklaerung;
    }

    public Tastenfunktion Funktion { get; }

    public string Titel { get; }

    public string Erklaerung { get; }

    public ObservableCollection<BelegungZeile> Belegungen { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BelegenText))]
    public partial bool Wartet { get; set; }

    public string BelegenText => Wartet ? "Abbrechen" : "Belegen";

    [RelayCommand]
    private async Task BelegenAsync() => await _tasten.BelegenAsync(this).ConfigureAwait(true);
}

/// <summary>Eine belegte Taste: Name, ob eine Quelle sie gerade bedient, und Löschen.</summary>
public sealed partial class BelegungZeile : ObservableObject
{
    private readonly TastenViewModel _tasten;

    internal BelegungZeile(TastenViewModel tasten, Tastenzuordnung zuordnung, string name, bool verfuegbar)
    {
        _tasten = tasten;
        Zuordnung = zuordnung;
        Name = name;
        Verfuegbar = verfuegbar;
    }

    public Tastenzuordnung Zuordnung { get; }

    public string Name { get; }

    /// <summary>Keine Quelle kann diese Taste gerade (etwa ein Joystick ohne DirectInput); sie bleibt gespeichert.</summary>
    public bool Verfuegbar { get; }

    public string Beschreibung => Verfuegbar ? Name : Name + " (nicht verfügbar)";

    [RelayCommand]
    private void Loeschen() => _tasten.Loeschen(Zuordnung);
}

/// <summary>Anzeigenamen für Tasten. Die Tastatur benennt das Layout des Systems (Raw Input), Maus und Joystick benennen wir.</summary>
public static class Tastennamen
{
    public static string Kurz(Tastenbelegung taste) => taste.Art switch
    {
        EingabeArt.Maus => taste.Code switch
        {
            Tastenbelegung.MausLinks => "Linke Maustaste",
            Tastenbelegung.MausRechts => "Rechte Maustaste",
            Tastenbelegung.MausMitte => "Mittlere Maustaste",
            Tastenbelegung.MausX1 => "Maustaste 4 (zurück)",
            Tastenbelegung.MausX2 => "Maustaste 5 (vor)",
            _ => "Maustaste " + taste.Code.ToString(CultureInfo.CurrentCulture),
        },
        // Name aus der Belegung ("... - Knopf 5", "... - Hut 1 oben"); fehlt er, sagt die Hersteller- und Produktkennung, welcher.
        EingabeArt.Joystick => Joystickeingabe.Anzeigename(taste)
            + (Geraetekennung.Lesen(taste.Geraet).Name.Trim().Length == 0 ? Geraet(taste.Geraet) : ""),
        _ => taste.Code switch
        {
            Tastenbelegung.NumpadPlus => "Num +",
            Tastenbelegung.NumpadMinus => "Num -",
            _ => taste.ToString(),
        },
    };

    /// <summary>Wie <see cref="Kurz"/>, die Tastatur aber mit dem Namen aus dem Layout des Systems.</summary>
    public static string MitLayout(Tastenbelegung taste)
    {
        if (taste.Art != EingabeArt.Tastatur || taste.Code is Tastenbelegung.NumpadPlus or Tastenbelegung.NumpadMinus) return Kurz(taste);
        var name = RawInputSprechtaste.Tastenname(taste);
        return string.IsNullOrWhiteSpace(name) ? Kurz(taste) : name;
    }

    /// <summary>Kurzform eines Gerätepfads: die Hersteller- und Produktkennung, sonst nichts.</summary>
    private static string Geraet(string? pfad)
    {
        if (string.IsNullOrEmpty(pfad)) return "";
        var i = pfad.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        return i >= 0 && pfad.Length >= i + 17 ? " (" + pfad.Substring(i, 17).ToUpperInvariant() + ")" : "";
    }
}
