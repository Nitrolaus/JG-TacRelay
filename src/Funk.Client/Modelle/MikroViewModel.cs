// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Dienste;
using Funk.Klang.Maschine;

namespace Funk.Client.Modelle;

/// <summary>
/// "Mikro stumm" (Klangstufe C2b, KLANGSTUFE-C-ENTWURF.md, "Mikro stumm", Q31 bis Q36): der Knopf in der oberen Steuerleiste des Funkfelds
/// (Q34, nur dort) und die Taste (Q32). Selbst stumm verwirft der Klangkern alles vom Mikrofon, auch mit Sprechtaste (Q31, Q36); das Gerät bleibt offen,
/// Windows unberührt. Dazu die Stummschaltung durch Einsatzleitung oder Admin aus dem Lagezustand (Q33, <see cref="FunkLage.Stumm"/>): dasselbe
/// Zeichen mit Schloss und "bis Uhrzeit", nicht selbst lösbar, im Tooltip wer, bis wann und welche Kanäle; gilt sie nur für einzelne Kanäle, tragen
/// deren Felder den roten Punkt (<see cref="KanalStumm"/>). Ob sie noch gilt, rechnet <see cref="Takt"/> mit der Uhr des Servers nach. Drückt jemand
/// die Sprechtaste, während das Mikro stumm ist, steht kurz "Mikro ist stumm" in der Meldezeile. Selbst stumm gilt nur in der Sitzung: Ein Neustart
/// beginnt mit freiem Mikrofon.
/// </summary>
public sealed partial class MikroViewModel : ObservableObject, IDisposable
{
    public const string Schluessel = "mikro";

    public const string IstStumm = "Mikro ist stumm";

    /// <summary>
    /// Die Taste "Mikro stumm" in der Meldezeile; seit 0.11.0 eine Zeile (bis dahin "Mikro stumm: Nichts wird gesendet, auch nicht mit der
    /// Sprechtaste."). Dass auch die Sprechtaste nichts sendet, sagt der Hilfetext am Knopf (<see cref="HilfeStumm"/>).
    /// </summary>
    public const string StummMeldung = "Mikro stumm: nichts wird gesendet";

    /// <summary>Seit 0.11.0 ohne Punkt wie die anderen Meldungen der Zeile.</summary>
    public const string AnMeldung = "Mikro wieder an";

    public const string HilfeFrei =
        "Mikro stumm: Ein Klick schaltet dein Mikrofon in JG TacRelay stumm. Dann geht nichts hinaus, auch nicht mit der Sprechtaste; Windows bleibt unberührt.";

    public const string HilfeStumm = "Dein Mikro ist stumm: Nichts geht hinaus, auch nicht mit der Sprechtaste. Ein Klick schaltet es wieder an.";

    private readonly IKlangMaschine _maschine;
    private readonly IFunkDienst _dienst;
    private readonly IOberflaechentakt _takt;
    private readonly Func<DateTimeOffset> _jetzt;
    private IReadOnlyList<StummschaltungInfo> _leitung = [];
    private DateTimeOffset _naechstesEnde = DateTimeOffset.MaxValue;

    /// <param name="jetzt">Die eigene Uhr (Tests stellen sie vor); die Zeit des Servers folgt mit <see cref="FunkLage.ZeitVersatz"/>.</param>
    public MikroViewModel(IKlangMaschine maschine, IFunkDienst dienst, IOberflaechentakt takt, Func<DateTimeOffset>? jetzt = null)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(takt);
        _maschine = maschine;
        _dienst = dienst;
        _takt = takt;
        _jetzt = jetzt ?? (() => DateTimeOffset.Now);
        _maschine.SprechtasteBeiStumm += BeiStumm;
        _dienst.LageGeaendert += LageGeaendert;
        Lesen();
    }

    /// <summary>Die Meldezeile (App.OnStartup); ohne sie nur der Knopf.</summary>
    public Meldezeile? Meldezeile { get; set; }

    /// <summary>Die Stummschaltung durch die Leitung hat sich geändert, auch durch Ablauf (die Kacheln ziehen ihre roten Punkte nach).</summary>
    public event Action? Geaendert;

    /// <summary>Selbst stumm (Knopf oder Taste).</summary>
    public bool Stumm => _maschine.MikroStumm;

    /// <summary>Einsatzleitung oder Admin haben stummgeschaltet, in mindestens einem Kanal.</summary>
    public bool LeitungStumm => _leitung.Count > 0;

    /// <summary>... in allen Kanälen.</summary>
    public bool LeitungAlle => _leitung.Any(s => s.Alle);

    /// <summary>
    /// "bis 21:30" (das späteste Ende, Ortszeit) für Bildschirmleser; <c>null</c> ohne Stummschaltung durch die Leitung. Bis 0.6.1 stand es am Knopf und
    /// kürzte "Senden: ..." bis auf die Auslassung; seit 0.6.2 steht die Zeit im Tooltip (<see cref="KnopfHilfe"/>).
    /// </summary>
    public string? LeitungBis => _leitung.Count == 0 ? null : "bis " + Uhrzeit(_leitung.Max(s => s.Bis));

    public MikroZustand Zustand => LeitungStumm ? MikroZustand.Leitung : Stumm ? MikroZustand.Stumm : MikroZustand.Frei;

    /// <summary>Das Mikro mit Querstrich: selbst stumm oder durch die Leitung.</summary>
    public bool Querstrich => Stumm || LeitungStumm;

    /// <summary>Rote Leuchte: Es geht gar nichts hinaus (selbst stumm oder durch die Leitung in allen Kanälen).</summary>
    public bool Leuchte => Stumm || LeitungAlle;

    /// <summary>Tooltip des Knopfs in Wörtern.</summary>
    public string KnopfHilfe
    {
        get
        {
            if (!LeitungStumm) return Stumm ? HilfeStumm : HilfeFrei;
            var lage = _dienst.Lage;
            var saetze = _leitung.GroupBy(s => (s.Durch, s.Bis)).Select(g =>
                "von " + lage.HauptnameVon(g.Key.Durch) + " bis " + Uhrzeit(g.Key.Bis)
                + (g.Any(s => s.Alle) ? " in allen Kanälen" : " in " + string.Join(", ", g.Select(s => KanalName(lage, s.NetzId)))));
            var text = "Stummgeschaltet " + string.Join("; ", saetze) + ".";
            if (!LeitungAlle) text += " Dort zeigen die Felder einen roten Punkt; in den anderen Kanälen sendest du.";
            text += " Das kannst du nicht selbst aufheben, es endet von selbst oder durch die Einsatzleitung.";
            text += Stumm ? " Dazu ist dein Mikro selbst stumm; ein Klick schaltet das wieder an." : " Ein Klick schaltet dein Mikro zusätzlich selbst stumm.";
            return text;
        }
    }

    /// <summary>Für Bildschirmleser: was der Knopf zeigt.</summary>
    public string ZustandText => Zustand switch
    {
        MikroZustand.Leitung => "Mikro stummgeschaltet von der Einsatzleitung " + LeitungBis,
        MikroZustand.Stumm => "Mikro stumm",
        _ => "Mikro an",
    };

    /// <summary>Knopf: selbst stumm an oder aus (die Stummschaltung der Leitung bleibt).</summary>
    [RelayCommand]
    private void Umschalten()
    {
        _maschine.MikroStumm = !_maschine.MikroStumm;
        Melden();
    }

    /// <summary>
    /// Taste "Mikro stumm" (Q32), auf dem Oberflächen-Thread: umschalten und in der Meldezeile sagen (im Vollbild sieht man den Knopf nicht), bei
    /// jedem Druck (eine Taste ohne Bild, Sarevoc 12.10.2026).
    /// </summary>
    public void TasteGedrueckt()
    {
        Umschalten();
        Meldezeile?.Zeigen(Schluessel, Stumm ? StummMeldung : AnMeldung, Meldeart.Hinweis, Meldezeiten.MikroTasteMs, immer: true);
    }

    /// <summary>Gilt eine Stummschaltung der Leitung in diesem Kanal einzeln (nicht für alle)? Dann trägt das Feld den roten Punkt.</summary>
    public bool KanalStumm(FunkLage lage, ushort netzId) => KanalSatz(lage, netzId) is not null;

    /// <summary>"Stummgeschaltet von Nordlicht bis 21:30" für den Tooltip des Felds; <c>null</c>, wenn keine einzelne Stummschaltung den Kanal trifft.</summary>
    public string? KanalSatz(FunkLage lage, ushort netzId)
    {
        ArgumentNullException.ThrowIfNull(lage);
        var s = lage.StummschaltungenAn(_jetzt()).FirstOrDefault(x => !x.Alle && x.Trifft(netzId));
        return s is null ? null : "Stummgeschaltet von " + lage.HauptnameVon(s.Durch) + " bis " + Uhrzeit(s.Bis);
    }

    /// <summary>Bildtakt: Eine Stummschaltung endet zur Zeit des Servers, ohne dass er etwas schickt.</summary>
    public void Takt()
    {
        if (_naechstesEnde == DateTimeOffset.MaxValue || _jetzt() + _dienst.Lage.ZeitVersatz < _naechstesEnde) return;
        Lesen();
    }

    public void Dispose()
    {
        _maschine.SprechtasteBeiStumm -= BeiStumm;
        _dienst.LageGeaendert -= LageGeaendert;
    }

    private void LageGeaendert() => _takt.Einreihen(Lesen);

    /// <summary>Sprechtaste bei stummem Mikro (Q31): bei jedem Druck "Mikro ist stumm", denn die Taste zeigt sonst nichts.</summary>
    private void BeiStumm() => _takt.Einreihen(() => Meldezeile?.Zeigen(Schluessel, IstStumm, Meldeart.Fehler, Meldezeiten.MikroIstStummMs, immer: true));

    private void Lesen()
    {
        // Die Lage kommt mit jeder Sprechanzeige neu; gemeldet wird nur eine Änderung (das Funkfeld überträgt sonst sein ganzes Bild).
        var neu = _dienst.Lage.StummschaltungenAn(_jetzt());
        _naechstesEnde = neu.Count == 0 ? DateTimeOffset.MaxValue : neu.Min(s => s.Bis);
        if (neu.SequenceEqual(_leitung)) return;
        _leitung = neu;
        Melden();
        Geaendert?.Invoke();
    }

    private void Melden()
    {
        OnPropertyChanged(nameof(Stumm));
        OnPropertyChanged(nameof(LeitungStumm));
        OnPropertyChanged(nameof(LeitungAlle));
        OnPropertyChanged(nameof(LeitungBis));
        OnPropertyChanged(nameof(Zustand));
        OnPropertyChanged(nameof(Querstrich));
        OnPropertyChanged(nameof(Leuchte));
        OnPropertyChanged(nameof(KnopfHilfe));
        OnPropertyChanged(nameof(ZustandText));
    }

    private static string KanalName(FunkLage lage, ushort netzId) => netzId == 1 && lage.Global is { } g ? g.Name : lage.KanalName(netzId);

    private static string Uhrzeit(DateTimeOffset bis) => bis.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Was der Mikro-Knopf im Funkfeld zeigt.</summary>
public enum MikroZustand
{
    /// <summary>Das Mikrofon sendet wie eingestellt.</summary>
    Frei,

    /// <summary>Selbst stumm geschaltet (Knopf oder Taste): rote Leuchte.</summary>
    Stumm,

    /// <summary>Stummgeschaltet durch Einsatzleitung oder Admin (0x0034): Schloss und "bis Uhrzeit", nicht selbst lösbar.</summary>
    Leitung,
}
