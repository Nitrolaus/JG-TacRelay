// JG TacRelay - Maintainer: Sarevoc
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Dienste;

namespace Funk.Client.Modelle;

/// <summary>
/// "Bereit" auf die offene Bereit-Abfrage der Leitung (Meldesystem 0.12.0, M3-M14): über die Taste "Bereit" (Einstellungen > Tasten > Erweitert,
/// ab Werk unbelegt, <c>Tastenbetrieb</c>) oder den Knopf "Bereit melden" in der Titelleiste des Hauptfensters, der nur da ist, solange eine Abfrage
/// offen ist (<see cref="Offen"/>). Nach dem Melden steht "gemeldet" auf dem Knopf und unter der Grafik über dem Spiel (die Quittung). Wer die
/// Abfrage gestartet hat, meldet nicht (er zählt nicht mit); ohne offene Abfrage tut die Taste nichts. Zweimal ist wie einmal.
/// </summary>
public sealed partial class Bereitmeldung : ObservableObject
{
    /// <summary>Text des Knopfs, solange du noch nicht gemeldet hast.</summary>
    public const string MeldenText = "Bereit melden";

    /// <summary>Text des Knopfs nach dem Melden.</summary>
    public const string GemeldetText = "gemeldet";

    /// <summary>Hovertext am Knopf.</summary>
    public const string Hilfe = "Bereit melden: Die Leitung fragt, wer bereit ist. Ein Klick meldet dich bereit; sie sieht dann \"n von m bereit\" und wer noch fehlt. "
        + "Dasselbe geht mit der Taste \"Bereit\" (Einstellungen, Tasten, Erweitert), auch wenn das Spiel vorn ist. Der Knopf ist nur da, solange die "
        + "Abfrage offen ist, höchstens 2 Minuten.";

    private readonly IFunkDienst _dienst;
    private bool _laeuft;

    public Bereitmeldung(IFunkDienst dienst)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        _dienst = dienst;
        Aktualisieren();
    }

    /// <summary>Eine Bereit-Abfrage ist offen, und du bist gefragt (nicht der, der sie gestartet hat).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KnopfText))]
    [NotifyCanExecuteChangedFor(nameof(MeldenCommand))]
    public partial bool Offen { get; private set; }

    /// <summary>Du hast auf die offene Abfrage bereit gemeldet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KnopfText))]
    [NotifyCanExecuteChangedFor(nameof(MeldenCommand))]
    public partial bool Gemeldet { get; private set; }

    /// <summary>Der Satz, wenn das Melden scheiterte; sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    /// <summary>Die Nummer der Abfrage, auf die du gemeldet hast; <c>null</c> vorher.</summary>
    public uint? GemeldetNummer { get; private set; }

    /// <summary>"Bereit melden" oder "gemeldet".</summary>
    public string KnopfText => Gemeldet ? GemeldetText : MeldenText;

    /// <summary>Nach jeder neuen Lage: offen oder nicht, gemeldet auf diese Nummer oder nicht.</summary>
    public void Aktualisieren()
    {
        var l = _dienst.Lage;
        var abfrage = l.IstAktiv ? l.Meldestand?.Abfrage : null;
        Offen = abfrage is { Nummer: not null } a && a.Von != l.Konto?.Id;
        Gemeldet = Offen && abfrage?.Nummer == GemeldetNummer;
        if (Offen) return;
        // Zu: Fehler und Nummer vergessen (nach einem Neustart des Servers zählen die Nummern wieder ab 1).
        Fehler = null;
        GemeldetNummer = null;
    }

    /// <summary>Knopf und Taste: bereit melden, wenn eine Abfrage offen ist und du gefragt bist; sonst nichts.</summary>
    [RelayCommand(CanExecute = nameof(KannMelden))]
    public async Task Melden()
    {
        Aktualisieren();
        if (!Offen || Gemeldet || _laeuft || _dienst.Lage.Meldestand?.Abfrage?.Nummer is not { } nummer) return;
        _laeuft = true;
        try
        {
            Fehler = await _dienst.BereitMeldenAsync(nummer).ConfigureAwait(true);
            if (Fehler is null) GemeldetNummer = nummer;
        }
        finally
        {
            _laeuft = false;
        }
        Aktualisieren();
    }

    private bool KannMelden() => Offen && !Gemeldet;
}
