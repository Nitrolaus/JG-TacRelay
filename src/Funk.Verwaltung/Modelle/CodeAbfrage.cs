// JG TacRelay - Maintainer: Sarevoc
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Funk.Verwaltung.Modelle;

/// <summary>
/// Das kleine Code-Feld für die Schritt-Bestätigung (3.9): Code aus der Authenticator-App, sechs Ziffern, oder ein Notfallcode,
/// wenn das Telefon fehlt (der Server nimmt beide bei AdminBestaetigen); Einfügen erlaubt.
/// Es liegt als Karte über der Verwaltung, kein eigenes Fenster. Wartet auf OK oder Abbrechen; eine neue Frage löst eine
/// offene mit Abbrechen ab.
/// </summary>
public sealed partial class CodeAbfrage : ObservableObject
{
    private TaskCompletionSource<string?>? _offen;

    /// <summary>Wofür der Code gebraucht wird, etwa "Gerät freigeben".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Offen))]
    public partial string? Frage { get; private set; }

    public bool Offen => Frage is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    public partial string Eingabe { get; set; } = "";

    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    /// <summary>Fragt und wartet; <c>null</c> nach Abbrechen oder wenn <paramref name="abbruch"/> auslöst.</summary>
    public async Task<string?> FragenAsync(string wofuer, CancellationToken abbruch)
    {
        _offen?.TrySetResult(null);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _offen = tcs;
        Eingabe = "";
        Fehler = null;
        // Jede Authenticator-App nach RFC 6238 geht (seit 0.6.4 ohne Markennamen, Sarevoc 04.10.2026).
        Frage = wofuer + ": Das braucht einen frischen Code aus deiner Authenticator-App. Tipp die sechs Ziffern hier ein; danach "
            + "gilt er 5 Minuten für weitere solche Schritte. Ohne Telefon geht auch ein Notfallcode; jeder gilt nur einmal.";
        using var r = abbruch.Register(() => tcs.TrySetResult(null));
        var code = await tcs.Task.ConfigureAwait(true);
        if (ReferenceEquals(_offen, tcs))
        {
            _offen = null;
            Frage = null;
            Eingabe = "";
        }
        return code;
    }

    /// <summary>Sechs Ziffern aus der App oder ein Notfallcode (zwölf Zeichen); beides in der Form, die der Server erwartet.</summary>
    private static string? Lesen(string eingabe) => Codeform.Totp(eingabe) ?? Codeform.Notfall(eingabe);

    private bool KannOk() => Lesen(Eingabe) is not null;

    [RelayCommand(CanExecute = nameof(KannOk))]
    private void Ok()
    {
        if (Lesen(Eingabe) is not { } code)
        {
            Fehler = Verwaltungstexte.CodeForm + " " + Verwaltungstexte.NotfallForm;
            return;
        }
        _offen?.TrySetResult(code);
    }

    [RelayCommand]
    private void Abbrechen() => _offen?.TrySetResult(null);
}
