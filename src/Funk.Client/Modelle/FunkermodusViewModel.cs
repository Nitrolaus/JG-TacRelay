// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;

namespace Funk.Client.Modelle;

/// <summary>
/// "Heute als Funker" (RAID-S2-ENTWURF Abschnitt 1, FUNKPROTOKOLL 6.15): Ein Admin, der einen Abend nur funken will, schaltet am eigenen
/// Konto um; bis 04:00 ruhen Global, Durchsagen und das Brett, und die Einsatzleitung teilt ihn ein wie jeden anderen. Statt einer
/// zweiten Anmeldung. Vor dem Einschalten eine Rückfrage mit den Folgen (eigene Sperre, Nachziehen, wer dann einteilt); "Wieder
/// Admin" ist ein Klick ohne Rückfrage, auch im Menü des Symbols im Infobereich. Dazu die Einstellung "Beim Verbinden als Funker":
/// Dann schaltet der Client den Modus nach jedem Verbinden ein, einmal je Verbindung, aber nicht mehr an dem Abend, an dem du ihn
/// selbst ausgeschaltet hast.
/// <para>
/// Nur für Admins und nur an Servern ab 0.5.0 sichtbar (<see cref="FunkLage.ServerS2"/>); einem älteren Server schickt der Client
/// 0x003C nicht.
/// </para>
/// </summary>
public sealed partial class FunkermodusViewModel : ObservableObject
{
    private readonly IFunkDienst _dienst;
    private readonly Einstellungsablage _ablage;
    private readonly IOberflaechentakt _takt;
    private readonly Func<DateTimeOffset> _jetzt;
    private bool _warAktiv;
    private bool _automatischGeschickt;

    /// <summary>
    /// Der Abend (er beginnt um 04:00), an dem du den Modus selbst ausgeschaltet hast ("Wieder Admin", auch im Menü des Symbols). Bis
    /// zum nächsten schaltet "Beim Verbinden als Funker" ihn nicht wieder ein: Wer mitten im Raid die Leitung übernimmt, verlöre sie sonst
    /// beim nächsten Wiederverbinden still (Gegenprüfung S2; RAID-S2-ENTWURF Abschnitt 4: dein Funker-Modus ändert sich nie ohne dich).
    /// </summary>
    private DateOnly? _selbstAusAm;

    private DateOnly Abend() => DateOnly.FromDateTime(_jetzt().AddHours(-4).Date);

    public FunkermodusViewModel(IFunkDienst dienst, Einstellungsablage ablage, IOberflaechentakt takt, Func<DateTimeOffset>? jetzt = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(takt);
        _dienst = dienst;
        _ablage = ablage;
        _takt = takt;
        _jetzt = jetzt ?? (() => DateTimeOffset.Now);
        _dienst.LageGeaendert += () => _takt.Einreihen(Aktualisieren);
        Aktualisieren();
    }

    /// <summary>Titel des Schalters in "Ich".</summary>
    public const string Titel = "Heute als Funker (bis 04:00)";

    /// <summary>Der Satz darunter.</summary>
    public const string Erklaerung = "Die Einsatzleitung kann dich einteilen. Global, Durchsagen und die Funkfeld-Zuteilung ruhen bis dahin. Die Verwaltung mit Code bleibt dir.";

    /// <summary>Verbunden, Stufe ADMIN, Server ab 0.5.0: Es gibt den Schalter.</summary>
    [ObservableProperty]
    public partial bool Sichtbar { get; private set; }

    /// <summary>Der Modus läuft.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Aus))]
    [NotifyCanExecuteChangedFor(nameof(EinschaltenCommand), nameof(WiederAdminCommand))]
    public partial bool An { get; private set; }

    public bool Aus => !An;

    /// <summary>"als Funker bis 04:00" für die Fußzeile und den Schalter, sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? StandText { get; private set; }

    /// <summary>Die Rückfrage vor dem Einschalten, sonst <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatRueckfrage))]
    [NotifyCanExecuteChangedFor(nameof(JaCommand))]
    public partial string? Rueckfrage { get; private set; }

    public bool HatRueckfrage => Rueckfrage is not null;

    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    /// <summary>Gerade unterwegs zum Server.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EinschaltenCommand), nameof(JaCommand), nameof(WiederAdminCommand))]
    public partial bool Beschaeftigt { get; private set; }

    /// <summary>"Beim Verbinden als Funker" (ab Werk aus), auf diesem PC gespeichert.</summary>
    public bool BeimVerbinden
    {
        get => _ablage.Aktuell.Verbindung.AlsFunkerBeimVerbinden;
        set
        {
            if (value == _ablage.Aktuell.Verbindung.AlsFunkerBeimVerbinden) return;
            _ablage.Aktuell.Verbindung.AlsFunkerBeimVerbinden = value;
            _ablage.Vormerken();
            OnPropertyChanged();
        }
    }

    /// <summary>Der Modus wechselte (für Menü und Fußzeile).</summary>
    public event Action? Geaendert;

    private bool KannEinschalten() => Sichtbar && !An && !Beschaeftigt;

    /// <summary>Erst die Rückfrage mit den Folgen; geschaltet wird mit <see cref="JaCommand"/>.</summary>
    [RelayCommand(CanExecute = nameof(KannEinschalten))]
    private void Einschalten()
    {
        Fehler = null;
        Rueckfrage = RueckfrageText(_dienst.Lage);
    }

    private bool KannJa() => Rueckfrage is not null && !Beschaeftigt;

    [RelayCommand(CanExecute = nameof(KannJa))]
    private async Task Ja()
    {
        Rueckfrage = null;
        await SchaltenAsync(true).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Nein() => Rueckfrage = null;

    private bool KannWiederAdmin() => Sichtbar && An && !Beschaeftigt;

    /// <summary>"Wieder Admin": ein Klick, ohne Rückfrage und ohne Code; die Rechte kommen sofort zurück.</summary>
    [RelayCommand(CanExecute = nameof(KannWiederAdmin))]
    private Task WiederAdmin() => SchaltenAsync(false);

    /// <summary>Ohne Rückfrage schalten; das Menü des Symbols nimmt es nur für "Wieder Admin" (Einschalten geht über die Rückfrage).</summary>
    public Task UmschaltenAsync(bool an) => SchaltenAsync(an);

    private async Task SchaltenAsync(bool an)
    {
        Beschaeftigt = true;
        Fehler = null;
        try
        {
            var a = await _dienst.FunkermodusAsync(an).ConfigureAwait(true);
            if (!a.Ok) Fehler = a.Fehler;
            else if (!an) _selbstAusAm = Abend();
        }
        finally
        {
            Beschaeftigt = false;
        }
        Aktualisieren();
    }

    /// <summary>
    /// Was vor dem Einschalten zu wissen ist (Abschnitt 1, "Folgen, die du kennen musst"): Rechte ruhen, die eigene Sperre der
    /// Selbstwahl und das Nachziehen einer selbst angewandten Aufstellung enden und kommen nicht zurück, und wer dann einteilt.
    /// </summary>
    public static string RueckfrageText(FunkLage lage)
    {
        ArgumentNullException.ThrowIfNull(lage);
        var teile = new List<string> { "Heute als Funker bis 04:00: Die Einsatzleitung teilt dich ein wie jeden anderen. Global, Durchsagen und die Funkfeld-Zuteilung ruhen bis dahin." };
        var ich = lage.Konto?.Id;
        if (!lage.Selbstwahl && ich is { } id && lage.SelbstwahlDurch == id)
            teile.Add("Deine Sperre der Selbstwahl endet jetzt und kommt nicht zurück.");
        teile.Add("Hast du eine Aufstellung angewandt, wird sie nicht mehr nachgezogen.");
        var andere = lage.Funker
            .Where(f => f.Online && f.KontoId != ich && (f.Zeichen.Contains(Zeichensatz.Einsatzleitung) || (f.Zeichen.Contains(Zeichensatz.Admin) && f.Details?.AlsFunker != true)))
            .Select(f => f.ScName ?? f.Name).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        teile.Add(andere.Count == 0
            ? "Gerade teilt sonst niemand ein: Gib zuerst jemandem den Auftrag."
            : "Einteilen können dann: " + string.Join(", ", andere) + ".");
        teile.Add("Zurück geht es jederzeit mit \"Wieder Admin\", ohne Code.");
        return string.Join(" ", teile);
    }

    private void Aktualisieren()
    {
        var l = _dienst.Lage;
        var aktiv = l.IstAktiv && l.Konto is not null;
        Sichtbar = aktiv && l.ServerS2 && l.Konto!.IstAdmin;
        var an = l.Konto?.AlsFunker == true;
        var geaendert = an != An;
        An = an;
        StandText = an && l.Konto!.AlsFunkerBis is { } bis ? "als Funker bis " + Uhrzeit(bis, l) : null;
        if (!Sichtbar) Rueckfrage = null;
        if (!aktiv)
        {
            _warAktiv = false;
            _automatischGeschickt = false;
        }
        else if (!_warAktiv)
        {
            _warAktiv = true;
            // "Beim Verbinden als Funker": einmal je Verbindung, nur wenn der Modus nicht schon läuft und du ihn an diesem Abend nicht
            // selbst ausgeschaltet hast.
            if (Sichtbar && BeimVerbinden && !an && !_automatischGeschickt && _selbstAusAm != Abend())
            {
                _automatischGeschickt = true;
                _ = SchaltenAsync(true);
            }
        }
        if (geaendert) Geaendert?.Invoke();
    }

    private string Uhrzeit(DateTimeOffset server, FunkLage l)
    {
        var lokal = (server - l.ZeitVersatz).ToLocalTime();
        return (lokal - _jetzt()).Duration() < TimeSpan.FromHours(20)
            ? lokal.ToString("HH:mm", CultureInfo.CurrentCulture)
            : lokal.ToString("ddd dd.MM. HH:mm", CultureInfo.CurrentCulture);
    }
}
