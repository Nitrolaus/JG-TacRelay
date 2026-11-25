// JG TacRelay - Maintainer: Sarevoc
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Dienste;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Client.Modelle;

/// <summary>
/// Das gelbe "!" der Auto Dauersend-Dämpfung (Klangstufe C2b, KLANGSTUFE-C-ENTWURF.md, Q1, Q2, Q8, Q13): das eigene Urteil des Wächters
/// an der eigenen Sendeanzeige im Funkfeld und in der Meldezeile, das Zeichen an anderen Funkern dort, wo ihr Name erscheint (Kachel im Funkfeld,
/// "Wer ist da"), die Auswahl daran (stumm, wieder normal, für diesen Funker nicht mehr dämpfen) und die Taste "Dauersender stumm". Für Funker Wörter
/// statt dB. Die Ereignisse der Maschine kommen aus ihrem Melde-Thread und gehen über den Oberflächentakt; alles Übrige auf dem Oberflächen-Thread.
/// Ein Exemplar je Programm, gehalten vom Funkfeld (<see cref="FunkfeldViewModel.Dauersend"/>).
/// </summary>
public sealed partial class DauersendViewModel : ObservableObject, IDisposable
{
    /// <summary>Schlüssel der Meldezeile für das eigene Urteil.</summary>
    public const string UrteilSchluessel = "urteil";

    /// <summary>Schlüssel der Meldezeile für die Taste "Dauersender stumm".</summary>
    public const string TasteSchluessel = "dauersender";

    /// <summary>Am Ende jedes Tooltips an einem anderen Funker: was ein Klick tut.</summary>
    public const string AuswahlHinweis = "Klick: stumm, wieder normal oder für diesen Funker nicht mehr dämpfen.";

    /// <summary>Hinter dem eigenen Urteil, wenn der Schalter aus ist: Der Wächter urteilt, stellt aber nichts.</summary>
    public const string NurAngezeigt = "Die Auto Dauersend-Dämpfung ist aus: Es wird nur angezeigt, nichts geregelt.";

    /// <summary>Taste "Dauersender stumm", wenn bisher keiner gedämpft wurde (bis 0.11.0 "Dauersender stumm: Bisher wurde keiner leiser gestellt.").</summary>
    public const string KeinerGedaempft = "Dauersender stumm: keiner leiser gestellt";

    private readonly IKlangMaschine _maschine;
    private readonly IFunkDienst _dienst;
    private readonly IOberflaechentakt _takt;
    private Dictionary<uint, FunkerDaempfung> _funker = [];

    public DauersendViewModel(IKlangMaschine maschine, IFunkDienst dienst, IOberflaechentakt takt)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(takt);
        _maschine = maschine;
        _dienst = dienst;
        _takt = takt;
        _maschine.EigenesUrteilGeaendert += UrteilGemeldet;
        _maschine.DauersenderGeaendert += DauersenderGemeldet;
        EigenesLesen(_maschine.EigenesUrteil, melden: false);
        FunkerLesen();
    }

    /// <summary>Die Meldezeile (App.OnStartup); ohne sie nur das Zeichen im Funkfeld.</summary>
    public Meldezeile? Meldezeile { get; set; }

    /// <summary>Die Funker mit Zeichen haben sich geändert (Kacheln und "Wer ist da" ziehen nach); auf dem Oberflächen-Thread.</summary>
    public event Action? Geaendert;

    // ---- das eigene Urteil ----

    /// <summary>Das eigene Urteil des Wächters (Echo, Spielton, Raumgeräusch, klemmende Taste) oder keins.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatEigenesUrteil))]
    public partial WaechterStand EigenesUrteil { get; private set; } = WaechterStand.Keins;

    /// <summary>Das gelbe "!" an der eigenen Sendeanzeige steht da.</summary>
    public bool HatEigenesUrteil => EigenesUrteil.Urteil != DauersendUrteil.Keins;

    /// <summary>
    /// Tooltip des eigenen "!": Ursache und was du tun kannst, in den Wörtern des Klangkerns (<see cref="Daempfungsworte.Ursache"/>, bei Echo ohne
    /// Echounterdrückung dessen eigener Satz). Ist die Auto Dauersend-Dämpfung aus, dazu <see cref="NurAngezeigt"/>.
    /// </summary>
    [ObservableProperty]
    public partial string EigeneHilfe { get; private set; } = "";

    private void UrteilGemeldet(WaechterStand stand) => _takt.Einreihen(() => EigenesLesen(stand, melden: true));

    private void EigenesLesen(WaechterStand stand, bool melden)
    {
        var vorher = EigenesUrteil;
        var ursache = stand.Urteil == DauersendUrteil.Keins ? ""
            : string.IsNullOrWhiteSpace(stand.Ursache) ? Daempfungsworte.Ursache(stand.Urteil) : stand.Ursache;
        var hilfe = ursache.Length == 0 ? "" : _maschine.Daempfung.An ? ursache : ursache + " " + NurAngezeigt;
        EigenesUrteil = stand;
        EigeneHilfe = hilfe;
        if (!melden || Meldezeile is not { } zeile) return;
        if (stand.Urteil == DauersendUrteil.Keins) zeile.Loesen(UrteilSchluessel);
        else if (stand != vorher) zeile.Zeigen(UrteilSchluessel, Meldetext(stand.Urteil, _maschine.Daempfung.An), Meldeart.Hinweis, Meldezeiten.UrteilMs, ausrufe: true);
    }

    /// <summary>
    /// Das eigene Urteil als eine Zeile für die Meldezeile (seit 0.11.0, Sarevoc 12.10.2026: "maximal einzeilig"); bis dahin stand dort der ganze
    /// Satz aus Ursache und Hinweis. Der steht weiter am gelben "!" im Funkfeld (<see cref="EigeneHilfe"/>), auch ob die Automatik nur anzeigt.
    /// Ist die Auto Dauersend-Dämpfung aus (<paramref name="an"/> falsch), stellt der Wächter nichts (Sendekette, Schalter aus): Dann sagt die
    /// Zeile nicht "Senden aus" oder "Schwelle angehoben", sondern was wirklich gilt (Befund der Prüfung: bis dahin stand dort sonst eine
    /// Handlung, die nicht geschah, ohne das "nur angezeigt" des langen Satzes).
    /// </summary>
    public static string Meldetext(DauersendUrteil urteil, bool an = true) => urteil switch
    {
        DauersendUrteil.Echo => "Echo: Ton kommt ins Mikrofon zurück",
        DauersendUrteil.Spielton => "Spielton kommt ins Mikrofon",
        DauersendUrteil.Raumgeraeusch => an ? "Raumgeräusch: Schwelle angehoben" : "Raumgeräusch: nur angezeigt",
        DauersendUrteil.KlemmendeTaste => an ? "Sprechtaste klemmt: Senden aus" : "Sprechtaste klemmt: Senden bleibt an",
        DauersendUrteil.Dauersenden => "Dauersenden fast ohne Sprache",
        _ => "",
    };

    // ---- andere Funker ----

    private void DauersenderGemeldet() => _takt.Einreihen(FunkerLesen);

    private void FunkerLesen()
    {
        var neu = _maschine.Dauersender.Where(Zeigen).ToDictionary(f => f.KontoId);
        if (neu.Count == _funker.Count && neu.All(p => _funker.TryGetValue(p.Key, out var alt) && alt == p.Value)) return;
        _funker = neu;
        Geaendert?.Invoke();
    }

    /// <summary>Ein Funker bekommt das Zeichen, solange er ein Urteil hat, gedämpft ist oder eine Wahl am Zeichen gilt.</summary>
    private static bool Zeigen(FunkerDaempfung f) =>
        f.KontoId != 0 && (f.Urteil != DauersendUrteil.Keins || f.DaempfungDb < -0.5f || f.Stumm || f.NichtDaempfen);

    /// <summary>Das Zeichen an einem Funker, mit seinem Namen aus der Lage; <c>null</c>, wenn er keins hat.</summary>
    public DauersendZeichen? ZeichenVon(uint kontoId) =>
        _funker.TryGetValue(kontoId, out var f) ? new DauersendZeichen(this, f, _dienst.Lage.HauptnameVon(kontoId), _maschine.Daempfung.An) : null;

    /// <summary>Du hast diesen Funker am gelben Zeichen stumm geschaltet ("stumm", deine Wahl): Er ist bei dir nicht zu hören (seit 0.11.9 für die Sprecheranzeige).</summary>
    public bool IstStumm(uint kontoId) => _funker.TryGetValue(kontoId, out var f) && f.Stumm;

    /// <summary>Eine Wahl am Zeichen eines Funkers: an den Klangkern, dann den Stand neu lesen.</summary>
    public void Handeln(uint kontoId, FunkerAktion aktion)
    {
        _maschine.FunkerHandeln(kontoId, aktion);
        FunkerLesen();
    }

    /// <summary>
    /// Taste "Dauersender stumm" (Q8): Der erste Druck schaltet den zuletzt gedämpften Funker stumm, der zweite macht ihn wieder normal; die Meldezeile
    /// sagt, wen (im Vollbild klickt keiner). Auf dem Oberflächen-Thread.
    /// </summary>
    public void StummTaste()
    {
        var f = _maschine.ZuletztGedaempftenUmschalten();
        FunkerLesen();
        // Seit 0.11.0 eine Zeile (bis dahin "Kobold stumm. Noch einmal drücken: wieder normal." und "Kobold wieder normal.").
        var text = f is not { } d ? KeinerGedaempft
            : d.Stumm ? _dienst.Lage.HauptnameVon(d.KontoId) + " stumm, nochmal drücken: wieder normal"
            : _dienst.Lage.HauptnameVon(d.KontoId) + " wieder normal";
        // Eine Taste ohne Bild: bei jedem Druck, auch "keiner leiser gestellt" zweimal hintereinander.
        Meldezeile?.Zeigen(TasteSchluessel, text, Meldeart.Hinweis, Meldezeiten.DauersenderTasteMs, immer: true);
    }

    public void Dispose()
    {
        _maschine.EigenesUrteilGeaendert -= UrteilGemeldet;
        _maschine.DauersenderGeaendert -= DauersenderGemeldet;
    }

    /// <summary>Der Grund eines Urteils der Empfängerseite in Wörtern, für den Satz am Zeichen; <c>null</c> ohne Urteil.</summary>
    internal static string? Grund(DauersendUrteil urteil) => urteil switch
    {
        DauersendUrteil.Echo => "sendet im Takt der anderen",
        DauersendUrteil.Dauersenden => "sendet seit langem ohne Pause, fast ohne Sprache",
        DauersendUrteil.Keins => null,
        _ => "sendet dauernd",
    };
}

/// <summary>Ein Eintrag der Auswahl am gelben Zeichen (Q2): was er tut, der Text, ob er gerade geht, und sein Tooltip.</summary>
public sealed record DauersendWahl(FunkerAktion Aktion, string Text, bool Waehlbar, string Hilfe);

/// <summary>
/// Das gelbe "!" an einem anderen Funker (Klangstufe C2b): Tooltip in Wörtern ("Kobold wird bei dir deutlich leiser gestellt: sendet im Takt der
/// anderen."), die Auswahl daran und der Befehl dazu. Gleich, wenn Funker, Name und Stand gleich sind; so ersetzt ein neues Zeichen mit demselben Stand
/// das alte in der Oberfläche nicht (eine offene Auswahl bliebe offen).
/// </summary>
public sealed partial class DauersendZeichen : IEquatable<DauersendZeichen>
{
    private readonly DauersendViewModel _modell;
    private readonly FunkerDaempfung _stand;

    internal DauersendZeichen(DauersendViewModel modell, FunkerDaempfung stand, string name, bool automatikAn)
    {
        _modell = modell;
        _stand = stand;
        Name = name;
        var grund = DauersendViewModel.Grund(stand.Urteil);
        var satz = stand.Stumm ? name + " ist bei dir stumm (deine Wahl)"
            : stand.NichtDaempfen ? name + " wird bei dir nicht mehr gedämpft (deine Wahl)"
            : stand.DaempfungDb < -0.5f ? name + " wird bei dir " + stand.Wort + " gestellt"
            : !automatikAn ? name + " bleibt bei dir normal laut, die Auto Dauersend-Dämpfung ist aus"
            : name + " wird bei dir gleich leiser gestellt";
        Hilfe = satz + (grund is null ? "." : ": " + grund + ".") + " " + DauersendViewModel.AuswahlHinweis;
        Wahlen =
        [
            new DauersendWahl(FunkerAktion.Stumm, "stumm", !stand.Stumm, "Du hörst " + name + " nicht mehr, bis du \"wieder normal\" wählst."),
            new DauersendWahl(FunkerAktion.WiederNormal, "wieder normal", true,
                name + " ist wieder normal laut; erkennt die Automatik wieder Dauersenden, wird er wieder leiser."),
            new DauersendWahl(FunkerAktion.NichtMehrDaempfen, "für diesen Funker nicht mehr dämpfen", !stand.NichtDaempfen,
                "Die Automatik stellt " + name + " bei dir nicht mehr leiser, bis du \"wieder normal\" wählst. Das gelbe Zeichen bleibt."),
        ];
    }

    public uint KontoId => _stand.KontoId;

    public string Name { get; }

    /// <summary>Tooltip in Wörtern mit dem, was ein Klick tut.</summary>
    public string Hilfe { get; }

    /// <summary>Bei dir stumm (deine Wahl am Zeichen oder die Taste "Dauersender stumm").</summary>
    public bool Stumm => _stand.Stumm;

    /// <summary>"Für diesen Funker nicht mehr dämpfen" gilt.</summary>
    public bool NichtDaempfen => _stand.NichtDaempfen;

    /// <summary>Die Auswahl: stumm, wieder normal, für diesen Funker nicht mehr dämpfen.</summary>
    public IReadOnlyList<DauersendWahl> Wahlen { get; }

    [RelayCommand(CanExecute = nameof(KannWaehlen))]
    private void Waehlen(DauersendWahl? wahl)
    {
        if (wahl is { Waehlbar: true }) _modell.Handeln(KontoId, wahl.Aktion);
    }

    private static bool KannWaehlen(DauersendWahl? wahl) => wahl is { Waehlbar: true };

    public bool Equals(DauersendZeichen? other) =>
        other is not null && other._stand == _stand && string.Equals(other.Name, Name, StringComparison.Ordinal) && string.Equals(other.Hilfe, Hilfe, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as DauersendZeichen);

    public override int GetHashCode() => HashCode.Combine(_stand, Name, Hilfe);

    public override string ToString() => Hilfe;
}
