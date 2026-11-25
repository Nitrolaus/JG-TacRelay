// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung.Flotte;

namespace Funk.Verwaltung.Modelle;

/// <summary>Wo der Admin mit seinem Nachweis steht (3.9); danach richtet sich, was der Bereich zeigt.</summary>
public enum Verwaltungsschritt
{
    /// <summary>Keine Verbindung: nichts geht.</summary>
    Getrennt,

    /// <summary>Das Konto ist kein Admin und nicht vorgesehen (dann zeigt der Client den Bereich gar nicht).</summary>
    KeinAdmin,

    /// <summary>Vom Eigentümer vorgesehen: Admin-Schlüssel anlegen und beantragen, Kennung am Telefon vorlesen.</summary>
    Einrichten,

    /// <summary>Ernannt: Geheimnis abholen (QR-Code), ersten Code eingeben, Notfallcodes sichern.</summary>
    Zweitfaktor,

    /// <summary>Eingerichtet, Admin-Sitzung zu: mit Code anmelden.</summary>
    Anmelden,

    /// <summary>Admin-Sitzung offen: Anträge, Mitglieder, Rollenkatalog, Abzeichen.</summary>
    Offen,

    /// <summary>Zweitfaktor gesperrt; entsperren nur an der Konsole.</summary>
    Gesperrt,
}

/// <summary>
/// Die Bereiche der offenen Verwaltung; Flotte und Einsatzleitung seit 0.3.0 (A40, 6.12), Heute und Werkzeuge seit 0.8.0 (Umbau der
/// Verwaltung). Links stehen sie gruppiert: Heute; Leute (Anträge, Mitglieder, Rollenkatalog); Funk (Kanäle, Rollen,
/// Einheiten, Phasen: alle im Bereich Flotte, der Reiter der Flotte sagt welcher); Einsatz (Einsatzleitung); Werkzeuge.
/// </summary>
public enum Verwaltungsbereich
{
    Antraege,
    Mitglieder,
    Katalog,
    Flotte,
    Einsatzleitung,

    /// <summary>Übersicht: was ansteht und Hinweise mit Sprung dorthin.</summary>
    Heute,

    /// <summary>Vorlagen, Bausteine und Sicherung, je mit Erklärung und Rückfrage.</summary>
    Werkzeuge,
}

/// <summary>Eine Zeile der Übersicht "Heute" (seit 0.8.0): Klartext und, wenn es einen gibt, ein Knopf, der dorthin springt.</summary>
public sealed class HeuteZeile
{
    private readonly Action? _sprung;

    internal HeuteZeile(string text, string? knopf, Action? sprung)
    {
        Text = text;
        Knopf = knopf;
        _sprung = sprung;
    }

    public string Text { get; }

    /// <summary>Text des Knopfs ("Zeigen", "Zu den Anträgen"); <c>null</c>, wenn es nichts zu zeigen gibt.</summary>
    public string? Knopf { get; }

    public bool HatSprung => _sprung is not null;

    internal void Springen() => _sprung?.Invoke();
}

/// <summary>
/// Der Bereich "Verwaltung" im Hauptfenster (A43-A47, ANMELDUNG-SICHERHEIT): führt durch Einrichtung und Anmeldung mit
/// Code aus einer Authenticator-App (jede nach RFC 6238, seit 0.6.4 ohne Markennamen) und zeigt in der offenen Admin-Sitzung Anträge, Mitglieder, Rollenkatalog und
/// Abzeichen. Das Geheimnis steht nur, solange der Schritt offen ist, und wird nie gespeichert; die Notfallcodes nur, bis
/// "Aufgeschrieben" gedrückt ist. Alles läuft auf dem Oberflächen-Thread (<paramref name="einreihen"/>).
/// </summary>
public sealed partial class VerwaltungViewModel : ObservableObject, IDisposable
{
    private readonly IVerwaltungsdienst _dienst;
    private readonly Action<Action> _einreihen;
    private readonly Func<DateTimeOffset> _jetzt;
    private bool _geladen;

    /// <param name="einreihen">Bringt Arbeit auf den Oberflächen-Thread; in Tests sofort.</param>
    // ---- Navigation als Baum (0.9.1, Q55): welche Ordner offen sind, gemerkt über den Neustart (0.9.3) ----

    /// <summary>Datei im Ablageordner mit den zugeklappten Ordnern der Navigation, eine Zeile je Ordner.</summary>
    public const string NaviDatei = "verwaltung-navigation.txt";

    private bool _naviLaedt;

    [ObservableProperty]
    public partial bool NaviHeuteOffen { get; set; } = true;

    [ObservableProperty]
    public partial bool NaviLeuteOffen { get; set; } = true;

    [ObservableProperty]
    public partial bool NaviFunkOffen { get; set; } = true;

    [ObservableProperty]
    public partial bool NaviEinsatzOffen { get; set; } = true;

    [ObservableProperty]
    public partial bool NaviWerkzeugeOffen { get; set; } = true;

    partial void OnNaviHeuteOffenChanged(bool value) => NaviSpeichern();

    partial void OnNaviLeuteOffenChanged(bool value) => NaviSpeichern();

    partial void OnNaviFunkOffenChanged(bool value) => NaviSpeichern();

    partial void OnNaviEinsatzOffenChanged(bool value) => NaviSpeichern();

    partial void OnNaviWerkzeugeOffenChanged(bool value) => NaviSpeichern();

    private string? NaviPfad => _dienst.Ablageordner is { } o ? Path.Combine(o, NaviDatei) : null;

    /// <summary>Liest die zugeklappten Ordner; fehlt die Datei oder ist sie unlesbar, sind alle offen.</summary>
    private void NaviLaden()
    {
        if (NaviPfad is not { } pfad || !File.Exists(pfad)) return;
        try
        {
            var zu = File.ReadAllLines(pfad).Select(z => z.Trim()).ToHashSet(StringComparer.Ordinal);
            _naviLaedt = true;
            NaviHeuteOffen = !zu.Contains("Heute");
            NaviLeuteOffen = !zu.Contains("Leute");
            NaviFunkOffen = !zu.Contains("Funk");
            NaviEinsatzOffen = !zu.Contains("Einsatz");
            NaviWerkzeugeOffen = !zu.Contains("Werkzeuge");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unlesbar: alle offen wie ab Werk.
        }
        finally
        {
            _naviLaedt = false;
        }
    }

    private void NaviSpeichern()
    {
        if (_naviLaedt || NaviPfad is not { } pfad) return;
        var zu = new List<string>();
        if (!NaviHeuteOffen) zu.Add("Heute");
        if (!NaviLeuteOffen) zu.Add("Leute");
        if (!NaviFunkOffen) zu.Add("Funk");
        if (!NaviEinsatzOffen) zu.Add("Einsatz");
        if (!NaviWerkzeugeOffen) zu.Add("Werkzeuge");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.WriteAllLines(pfad, zu);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nicht gemerkt; die Navigation funktioniert trotzdem.
        }
    }

    public VerwaltungViewModel(IVerwaltungsdienst dienst, Action<Action>? einreihen = null, Func<DateTimeOffset>? jetzt = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        _dienst = dienst;
        _einreihen = einreihen ?? (a => a());
        _jetzt = jetzt ?? (() => DateTimeOffset.Now);
        Antraege = new AntraegeViewModel(dienst, Nachschlag, _jetzt);
        // Freigeben und Ablehnen nehmen Zeilen weg; die Zahl im Reiter geht mit.
        Antraege.Antraege.CollectionChanged += (_, _) => AntraegeZaehlen();
        Mitglieder = new MitgliederViewModel(dienst, Nachschlag, NeuLadenAsync);
        Katalog = new KatalogViewModel(dienst, Nachschlag, NeuLadenAsync);
        Einsatzleitung = new EinsatzleitungViewModel(dienst, Nachschlag, _jetzt);
        Flotte = new FlotteViewModel(dienst, _jetzt, () => Einsatzleitung.Laufend);
        Abzeichen = new AbzeichenViewModel(dienst, Nachschlag);
        // Die Einträge unter Funk folgen dem Reiter der Flotte; die Übersicht folgt dem Entwurf und der Einsatzleitung.
        Flotte.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FlotteViewModel.Reiter) or nameof(FlotteViewModel.PhasenSichtbar)) FunkMelden();
        };
        Flotte.Nachgezogen += HeuteAktualisieren;
        NaviLaden();
        Einsatzleitung.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EinsatzleitungViewModel.Laufend)) HeuteAktualisieren();
        };
        _dienst.CodeFragen = (wofuer, abbruch) =>
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _einreihen(async () =>
            {
                try
                {
                    tcs.TrySetResult(await Abfrage.FragenAsync(wofuer, abbruch).ConfigureAwait(true));
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetResult(null);
                }
            });
            return tcs.Task;
        };
        _dienst.LageGeaendert += LageGeaendert;
        _dienst.Nachricht += NachrichtErhalten;
        Aktualisieren();
    }

    public Nachschlag Nachschlag { get; } = new();

    public AntraegeViewModel Antraege { get; }

    public MitgliederViewModel Mitglieder { get; }

    public KatalogViewModel Katalog { get; }

    /// <summary>Aufträge für die Einsatzleitung vergeben und entziehen (6.12).</summary>
    public EinsatzleitungViewModel Einsatzleitung { get; }

    /// <summary>Einheiten, Rollen und Netze im Entwurf (A40).</summary>
    public FlotteViewModel Flotte { get; }

    /// <summary>Die Seite Abzeichen (0.11.7): eigener Bereich ganz unten im Hauptmenü, nur in der offenen Admin-Sitzung.</summary>
    public AbzeichenViewModel Abzeichen { get; }

    /// <summary>Code-Feld für die Schritt-Bestätigung.</summary>
    public CodeAbfrage Abfrage { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstGetrennt), nameof(IstEinrichten), nameof(IstZweitfaktor), nameof(IstAnmelden), nameof(IstOffen),
        nameof(IstGesperrt), nameof(IstKeinAdmin))]
    public partial Verwaltungsschritt Schritt { get; private set; }

    public bool IstGetrennt => Schritt == Verwaltungsschritt.Getrennt;

    public bool IstKeinAdmin => Schritt == Verwaltungsschritt.KeinAdmin;

    public bool IstEinrichten => Schritt == Verwaltungsschritt.Einrichten;

    public bool IstZweitfaktor => Schritt == Verwaltungsschritt.Zweitfaktor;

    public bool IstAnmelden => Schritt == Verwaltungsschritt.Anmelden;

    public bool IstOffen => Schritt == Verwaltungsschritt.Offen;

    public bool IstGesperrt => Schritt == Verwaltungsschritt.Gesperrt;

    [ObservableProperty]
    public partial string Beschreibung { get; private set; } = "";

    /// <summary>"Admin-Sitzung offen bis 23:10 Uhr", in der offenen Verwaltung oben.</summary>
    [ObservableProperty]
    public partial string? SitzungText { get; private set; }

    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstAntraege), nameof(IstMitglieder), nameof(IstKatalog), nameof(IstFlotte), nameof(IstEinsatzleitung),
        nameof(IstHeute), nameof(IstWerkzeuge), nameof(IstKanaele), nameof(IstRollen), nameof(IstEinheiten), nameof(IstPhasen))]
    public partial Verwaltungsbereich Bereich { get; set; }

    partial void OnBereichChanged(Verwaltungsbereich value)
    {
        if (value == Verwaltungsbereich.Heute) HeuteAktualisieren();
    }

    /// <summary>Übersicht "Heute" (seit 0.8.0).</summary>
    public bool IstHeute
    {
        get => Bereich == Verwaltungsbereich.Heute;
        set
        {
            if (value) Bereich = Verwaltungsbereich.Heute;
        }
    }

    /// <summary>Werkzeuge: Vorlagen und Sicherung (seit 0.8.0).</summary>
    public bool IstWerkzeuge
    {
        get => Bereich == Verwaltungsbereich.Werkzeuge;
        set
        {
            if (value) Bereich = Verwaltungsbereich.Werkzeuge;
        }
    }

    /// <summary>Funk, Kanäle: Bereich Flotte mit dem Reiter Kanäle (seit 0.8.0 ein eigener Eintrag links).</summary>
    public bool IstKanaele
    {
        get => IstFunk(FlotteReiter.Netze);
        set => FunkZeigen(value, FlotteReiter.Netze);
    }

    /// <summary>Funk, Rollen mit dem Funkfeld-Editor.</summary>
    public bool IstRollen
    {
        get => IstFunk(FlotteReiter.Rollen);
        set => FunkZeigen(value, FlotteReiter.Rollen);
    }

    /// <summary>Funk, Einheiten.</summary>
    public bool IstEinheiten
    {
        get => IstFunk(FlotteReiter.Einheiten);
        set => FunkZeigen(value, FlotteReiter.Einheiten);
    }

    /// <summary>Funk, Phasen; nur wählbar, wenn der Server Phasen kennt.</summary>
    public bool IstPhasen
    {
        get => IstFunk(FlotteReiter.Phasen);
        set => FunkZeigen(value && Flotte.PhasenSichtbar, FlotteReiter.Phasen);
    }

    private bool IstFunk(FlotteReiter reiter) => Bereich == Verwaltungsbereich.Flotte && Flotte.Reiter == reiter;

    private void FunkZeigen(bool an, FlotteReiter reiter)
    {
        if (!an) return;
        Flotte.Reiter = reiter;
        Bereich = Verwaltungsbereich.Flotte;
    }

    private void FunkMelden()
    {
        OnPropertyChanged(nameof(IstKanaele));
        OnPropertyChanged(nameof(IstRollen));
        OnPropertyChanged(nameof(IstEinheiten));
        OnPropertyChanged(nameof(IstPhasen));
    }

    public bool IstAntraege
    {
        get => Bereich == Verwaltungsbereich.Antraege;
        set
        {
            if (value) Bereich = Verwaltungsbereich.Antraege;
        }
    }

    public bool IstMitglieder
    {
        get => Bereich == Verwaltungsbereich.Mitglieder;
        set
        {
            if (value) Bereich = Verwaltungsbereich.Mitglieder;
        }
    }

    public bool IstKatalog
    {
        get => Bereich == Verwaltungsbereich.Katalog;
        set
        {
            if (value) Bereich = Verwaltungsbereich.Katalog;
        }
    }

    public bool IstFlotte
    {
        get => Bereich == Verwaltungsbereich.Flotte;
        set
        {
            if (value) Bereich = Verwaltungsbereich.Flotte;
        }
    }

    public bool IstEinsatzleitung
    {
        get => Bereich == Verwaltungsbereich.Einsatzleitung;
        set
        {
            if (value) Bereich = Verwaltungsbereich.Einsatzleitung;
        }
    }

    [ObservableProperty]
    public partial string AntraegeTitel { get; private set; } = "Anträge";

    // ---- Vorprüfung vor dem Einrichten (0.4.1) ----

    /// <summary>
    /// Ergebnis der Vorprüfung (TPM 2.0, technisch Pflicht); <c>null</c>, solange keine gelaufen ist. Läuft beim Wechsel in den
    /// Schritt Einrichten und auf "Erneut prüfen", ohne einen Schlüssel anzulegen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TpmText), nameof(TpmGefunden), nameof(TpmSperrt), nameof(TpmPlattform))]
    [NotifyCanExecuteChangedFor(nameof(EinrichtenCommand))]
    public partial TpmStand? Tpm { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TpmText))]
    [NotifyCanExecuteChangedFor(nameof(EinrichtenCommand), nameof(VoraussetzungenPruefenCommand))]
    public partial bool PrueftVoraussetzungen { get; private set; }

    public string TpmText => PrueftVoraussetzungen || Tpm is null ? Voraussetzungstexte.Prueft : Tpm.Text;

    public bool TpmGefunden => Tpm?.Gefunden == true;

    /// <summary>Kein TPM 2.0 nutzbar: Einrichten geht hier sicher nicht.</summary>
    public bool TpmSperrt => Tpm?.Sperrt == true;

    /// <summary>"Windows meldet: TPM-Version:2.0 ..." (PCP_PLATFORM_TYPE), wenn es eine Meldung gab.</summary>
    public string? TpmPlattform => Tpm?.Plattform is { Length: > 0 } p ? "Windows meldet: " + p : null;

    private int _pruefung;

    /// <summary>Vorprüfung über den Dienst (wirft nicht); eine neuere ersetzt das Ergebnis einer älteren.</summary>
    public async Task VoraussetzungenPruefenAsync()
    {
        var nummer = ++_pruefung;
        PrueftVoraussetzungen = true;
        try
        {
            var stand = await _dienst.VoraussetzungenAsync().ConfigureAwait(true);
            if (nummer == _pruefung) Tpm = stand;
        }
        finally
        {
            if (nummer == _pruefung) PrueftVoraussetzungen = false;
        }
    }

    private bool KannPruefen() => !PrueftVoraussetzungen;

    [RelayCommand(CanExecute = nameof(KannPruefen))]
    private Task VoraussetzungenPruefen() => VoraussetzungenPruefenAsync();

    // ---- Einrichtung ----

    /// <summary>Die Admin-Kennung nach dem Antrag, zum Vorlesen am Telefon.</summary>
    [ObservableProperty]
    public partial string? Kennung { get; private set; }

    /// <summary>Das Geheimnis in Vierergruppen zum Abtippen; nur solange der Schritt offen ist.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatGeheimnis), nameof(NurErsterCode))]
    public partial string? Geheimnis { get; private set; }

    public bool HatGeheimnis => Geheimnis is not null;

    /// <summary>Das Geheimnis wurde in diesem Schritt abgeholt (genau einmal möglich); der Knopf dafür bleibt dann weg.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NurErsterCode))]
    public partial bool GeheimnisAbgeholt { get; private set; }

    /// <summary>Abgeholt, aber nicht mehr angezeigt (Bereich verlassen): nur noch das Feld für den ersten Code.</summary>
    public bool NurErsterCode => GeheimnisAbgeholt && !HatGeheimnis;

    /// <summary>Der QR-Code des otpauth-Links als Vektor (Einheiten = Module samt Ruhezone).</summary>
    [ObservableProperty]
    public partial Geometry? QrBildGeometrie { get; private set; }

    [ObservableProperty]
    public partial double QrSeite { get; private set; }

    [ObservableProperty]
    public partial string? GeheimnisBis { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EinrichtungBestaetigenCommand))]
    public partial string EinrichtungCode { get; set; } = "";

    /// <summary>Die zehn Notfallcodes, genau einmal gezeigt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatNotfallcodes))]
    public partial IReadOnlyList<string>? Notfallcodes { get; private set; }

    public bool HatNotfallcodes => Notfallcodes is not null;

    // ---- Anmeldung ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnmeldenCommand))]
    public partial string Code { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnmeldenCommand))]
    public partial bool MitNotfallcode { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnmeldenCommand), nameof(EinrichtenCommand), nameof(GeheimnisAbholenCommand), nameof(EinrichtungBestaetigenCommand))]
    public partial bool Beschaeftigt { get; private set; }

    /// <summary>
    /// Hinweis zur App für den Zweitfaktor. Jede Authenticator-App nach RFC 6238 geht; welche, entscheidet jeder Admin selbst,
    /// deshalb nennt der Text keine (seit 0.6.4, Sarevoc 04.10.2026).
    /// </summary>
    public static string AppHinweis { get; } =
        "Nimm eine Authenticator-App deiner Wahl auf deinem Telefon und scanne damit, "
        + "nicht auf diesem PC: Liegen Admin-Schlüssel und Code auf demselben Rechner, fängt Schadcode hier beide. Geht es nur mit "
        + "einer App unter Windows, dann mit Sperre der App und wissend, dass beide Faktoren hier liegen. Kein Bildschirmfoto: Wer den "
        + "QR-Code hat, hat das Geheimnis.";

    public void Dispose()
    {
        _dienst.LageGeaendert -= LageGeaendert;
        _dienst.Nachricht -= NachrichtErhalten;
        _dienst.CodeFragen = null;
        GeheimnisVergessen();
    }

    private void LageGeaendert() => _einreihen(Aktualisieren);

    private void NachrichtErhalten(object nachricht) => _einreihen(() =>
    {
        if (!IstOffen) return;
        switch (nachricht)
        {
            case Meldung { Art: MeldungsArten.AntragNeu }:
            case GeraetStatus { Status: GeraeteStatus.Wartet }:
                _ = AntraegeNeuAsync();
                break;
            case Meldung { Art: MeldungsArten.MitgliedGeaendert }:
                _ = NeuLadenAsync();
                break;
            case Meldung { Art: MeldungsArten.Beauftragung }:
                _ = Einsatzleitung.LadenAsync();
                break;
        }
    });

    /// <summary>Stand aus dem Dienst: Schritt, Beschreibung, Sitzung. Beim Öffnen der Sitzung einmal alles laden.</summary>
    internal void Aktualisieren()
    {
        var l = _dienst.Lage;
        var schritt = !l.Verbunden ? Verwaltungsschritt.Getrennt
            : l.SitzungOffen ? Verwaltungsschritt.Offen
            : l.Admin == AdminStatus.Aktiv ? Verwaltungsschritt.Anmelden
            : l.Admin == AdminStatus.Gesperrt ? Verwaltungsschritt.Gesperrt
            : l.Admin == AdminStatus.Einrichtung ? Verwaltungsschritt.Zweitfaktor
            : l.AntragMoeglich || l.Admin == AdminStatus.Vorgesehen ? Verwaltungsschritt.Einrichten
            : Verwaltungsschritt.KeinAdmin;
        var vorher = Schritt;
        Schritt = schritt;
        Beschreibung = schritt switch
        {
            Verwaltungsschritt.Getrennt => "Die Verwaltung braucht eine laufende Verbindung zum Server.",
            Verwaltungsschritt.KeinAdmin => "Dieses Konto ist kein Admin. Admins ernennt der Eigentümer an der Serverkonsole.",
            Verwaltungsschritt.Einrichten => Kennung is null
                ? "Der Eigentümer hat dich als Admin vorgesehen. Richte die Verwaltung auf diesem PC ein: Der Client legt einen Admin-Schlüssel im TPM an und zeigt dir eine Kennung, die du ihm am Telefon vorliest."
                : "Lies dem Eigentümer diese Admin-Kennung am Telefon vor. Sobald er dich an der Konsole ernannt hat, geht es hier von selbst weiter.",
            Verwaltungsschritt.Zweitfaktor => "Der Eigentümer hat dich ernannt. Hol jetzt das Geheimnis für deine Authenticator-App ab und gib den ersten Code ein (binnen 10 Minuten).",
            Verwaltungsschritt.Anmelden => "Admin-Sitzung öffnen: Code aus deiner Authenticator-App eingeben. Der Funk läuft dabei weiter.",
            Verwaltungsschritt.Gesperrt => "Dein Zweitfaktor ist gesperrt (zu viele falsche Codes oder vom Eigentümer gesperrt). Entsperren kann nur der Eigentümer an der Serverkonsole.",
            _ => "Admin-Sitzung offen.",
        };
        SitzungText = l.SitzungBis is { } bis
            ? "Admin-Sitzung offen bis " + bis.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) + " Uhr (oder bis du die Verwaltung schließt oder die Verbindung trennst)."
              + (l.NotfallcodesUebrig is { } n && n < 3 ? " Nur noch " + n.ToString(CultureInfo.CurrentCulture) + " Notfallcodes übrig: den Eigentümer um neue bitten." : "")
            : null;
        // Vorprüfung beim Wechsel in den Schritt Einrichten (0.4.1), nicht bei jeder Lageänderung darin.
        if (schritt == Verwaltungsschritt.Einrichten && vorher != Verwaltungsschritt.Einrichten) _ = VoraussetzungenPruefenAsync();
        if (schritt == Verwaltungsschritt.Offen && (vorher != Verwaltungsschritt.Offen || !_geladen)) _ = NeuLadenAsync();
        // Eine neue NetzListe (Push nach jeder Verwaltung) kommt hier als Lageänderung an.
        if (schritt == Verwaltungsschritt.Offen) Flotte.Aktualisieren();
        if (schritt != Verwaltungsschritt.Offen) _geladen = false;
        if (schritt != Verwaltungsschritt.Zweitfaktor && Geheimnis is not null) GeheimnisVergessen();
        if (schritt != Verwaltungsschritt.Zweitfaktor) GeheimnisAbgeholt = false;
    }

    /// <summary>Lädt Katalog, Mitglieder und Anträge und verteilt sie an die Bereiche.</summary>
    public async Task NeuLadenAsync()
    {
        _geladen = true;
        Fehler = null;
        var katalog = await _dienst.KatalogAsync().ConfigureAwait(true);
        var mitglieder = await _dienst.MitgliederAsync().ConfigureAwait(true);
        var fehler = katalog.Fehler ?? mitglieder.Fehler;
        if (fehler is not null)
        {
            Fehler = fehler;
            return;
        }
        Nachschlag.Katalog = katalog.Wert!;
        Nachschlag.Mitglieder = mitglieder.Wert!;
        Katalog.Uebernehmen();
        Mitglieder.Uebernehmen();
        Abzeichen.Uebernehmen();
        Einsatzleitung.Uebernehmen();
        await Antraege.LadenAsync().ConfigureAwait(true);
        AntraegeZaehlen();
        await Einsatzleitung.LadenAsync().ConfigureAwait(true);
        Flotte.Aktualisieren();
    }

    private async Task AntraegeNeuAsync()
    {
        await Antraege.LadenAsync().ConfigureAwait(true);
        AntraegeZaehlen();
    }

    private void AntraegeZaehlen()
    {
        AntraegeTitel = Antraege.Antraege.Count == 0 ? "Anträge" : "Anträge (" + Antraege.Antraege.Count.ToString(CultureInfo.CurrentCulture) + ")";
        HeuteAktualisieren();
    }

    // ---- Übersicht "Heute" (seit 0.8.0) ----

    /// <summary>Was ansteht: wartende Anträge, ein offener Entwurf, eine laufende Einsatzleitung; je mit Knopf dorthin.</summary>
    public ObservableCollection<HeuteZeile> Ansteht { get; } = [];

    /// <summary>Hinweise, die das Programm selbst in der Flotte findet (<see cref="Flottenentwurf.Hinweise"/>), je mit "Zeigen".</summary>
    public ObservableCollection<HeuteZeile> Hinweise { get; } = [];

    /// <summary>Die Flotte ist da und das Programm findet nichts: Dann steht "Keine Hinweise" da.</summary>
    [ObservableProperty]
    public partial bool KeineHinweise { get; private set; }

    /// <summary>Baut die Übersicht neu; ändert die Listen nur, wenn sich ein Text geändert hat.</summary>
    internal void HeuteAktualisieren()
    {
        var ansteht = new List<HeuteZeile>();
        var n = Antraege.Antraege.Count;
        ansteht.Add(n == 0
            ? new HeuteZeile("Keine Anträge offen.", null, null)
            : new HeuteZeile((n == 1 ? "1 Antrag wartet" : n.ToString(CultureInfo.CurrentCulture) + " Anträge warten") + " auf Freigabe.", "Zu den Anträgen",
                () => Bereich = Verwaltungsbereich.Antraege));
        var f = Flotte;
        if (!f.HatFlotte) ansteht.Add(new HeuteZeile("Noch keine Flotte vom Server. Sie kommt mit der Verbindung.", null, null));
        else if (f.HatAenderungen)
            ansteht.Add(new HeuteZeile("Entwurf: " + f.LeisteText + (f.SperrText is { } sperre ? "; " + sperre : "") + ".", "Was ändert sich?", () => f.EntwurfOffen = true));
        else ansteht.Add(new HeuteZeile("Kein offener Entwurf.", null, null));
        if (Einsatzleitung.Laufend is { } wer)
            ansteht.Add(new HeuteZeile("Es läuft eine Einsatzleitung: " + wer + ".", "Zur Einsatzleitung", () => Bereich = Verwaltungsbereich.Einsatzleitung));
        Zeilen(Ansteht, ansteht);
        Zeilen(Hinweise, [.. (f.Entwurf?.Hinweise() ?? []).Select(h => new HeuteZeile(h.Text, "Zeigen", () => HinweisZeigen(h)))]);
        KeineHinweise = f.HatFlotte && Hinweise.Count == 0;
    }

    private static void Zeilen(ObservableCollection<HeuteZeile> liste, List<HeuteZeile> neu)
    {
        if (liste.Select(z => (z.Text, z.Knopf)).SequenceEqual(neu.Select(z => (z.Text, z.Knopf)))) return;
        liste.Clear();
        foreach (var z in neu) liste.Add(z);
    }

    /// <summary>Sprung aus einem Hinweis: in den Bereich Funk, dort die Rolle, den Kanal oder die Einheit wählen.</summary>
    private void HinweisZeigen(FlottenHinweis h)
    {
        Bereich = Verwaltungsbereich.Flotte;
        switch (h.Ziel)
        {
            case HinweisZiel.Rolle:
                Flotte.RolleZeigen(h.Id, h.Suche);
                break;
            case HinweisZiel.Kanal:
                Flotte.KanalZeigen(h.Id);
                break;
            case HinweisZiel.KanaeleAlteFlotte:
                Flotte.KanalZeigen(h.Id, Flottenentwurf.AlteFlotte);
                break;
            default:
                Flotte.EinheitZeigen(h.Id);
                break;
        }
    }

    /// <summary>
    /// Ein Sprung aus "Wer ist da" kam bei geschlossener Admin-Sitzung: Nach "Anmelden" beginnt die Verwaltung dann bei Mitglieder statt bei "Heute"
    /// (Gegenprüfung 0.11.5: Anmelden setzte "Heute" ohne Bedingung, und die Karte ging unsichtbar auf).
    /// </summary>
    private bool _mitgliederNachAnmeldung;

    /// <summary>
    /// Sprung von außen (seit 0.11.5, Status in "Wer ist da" des Clients): Bereich Mitglieder, dieses Konto gewählt. Ist die Admin-Sitzung noch zu,
    /// steht der Bereich schon da, und nach der Anmeldung öffnet sich die Karte bei Mitglieder, sobald die Liste geladen ist. Ist sie offen, kennt die
    /// Liste das Konto aber nicht (älter als das Konto), lädt die Verwaltung neu, und der Sprung wartet genau auf diese Liste.
    /// </summary>
    public void MitgliedZeigen(uint kontoId)
    {
        Bereich = Verwaltungsbereich.Mitglieder;
        Mitglieder.KontoZeigen(kontoId);
        _mitgliederNachAnmeldung = !IstOffen;
        if (IstOffen && Mitglieder.SprungWartet) _ = NeuLadenAsync();
    }

    /// <summary>Der Knopf einer Zeile der Übersicht; springt nur in der offenen Verwaltung.</summary>
    [RelayCommand]
    private void Springen(HeuteZeile? zeile)
    {
        if (IstOffen) zeile?.Springen();
    }

    [RelayCommand]
    private Task NeuLaden() => NeuLadenAsync();

    // ---- Einrichtung ----

    private bool Frei() => !Beschaeftigt;

    /// <summary>Erst nach der Vorprüfung, und nie ohne TPM 2.0.</summary>
    private bool KannEinrichten() => Frei() && !PrueftVoraussetzungen && Tpm is { Sperrt: false };

    [RelayCommand(CanExecute = nameof(KannEinrichten))]
    private async Task Einrichten()
    {
        // Auch am Knopf vorbei (ExecuteAsync fragt CanExecute nicht): ohne Prüfung erst prüfen, ohne TPM 2.0 nichts anlegen.
        if (Tpm is null) await VoraussetzungenPruefenAsync().ConfigureAwait(true);
        if (Tpm is { Sperrt: true } tpm)
        {
            Fehler = tpm.Text;
            return;
        }
        await Arbeiten(async () =>
        {
            var a = await _dienst.AntragStellenAsync().ConfigureAwait(true);
            if (!a.Ok)
            {
                Fehler = a.Fehler;
                return;
            }
            Kennung = a.Wert;
            Aktualisieren();
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(Frei))]
    private async Task GeheimnisAbholen()
    {
        await Arbeiten(async () =>
        {
            var a = await _dienst.GeheimnisAbholenAsync().ConfigureAwait(true);
            if (!a.Ok)
            {
                Fehler = a.Fehler;
                return;
            }
            var e = a.Wert!;
            Geheimnis = Vierergruppen(e.Geheimnis!);
            GeheimnisAbgeholt = true;
            var qr = QrCode.Kodieren(e.Uri!, QrStufe.M);
            QrBildGeometrie = QrBild.Geometrie(qr);
            QrSeite = QrBild.Seite(qr);
            GeheimnisBis = e.GueltigBis is { } bis
                ? "Den ersten Code bis " + DateTimeOffset.FromUnixTimeMilliseconds(bis).ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) + " Uhr eingeben."
                : null;
        }).ConfigureAwait(true);
    }

    private bool KannBestaetigen() => !Beschaeftigt && Codeform.Totp(EinrichtungCode) is not null;

    [RelayCommand(CanExecute = nameof(KannBestaetigen))]
    private async Task EinrichtungBestaetigen()
    {
        await Arbeiten(async () =>
        {
            var a = await _dienst.EinrichtungBestaetigenAsync(EinrichtungCode).ConfigureAwait(true);
            if (!a.Ok)
            {
                Fehler = a.Fehler;
                return;
            }
            GeheimnisVergessen();
            GeheimnisAbgeholt = false;
            EinrichtungCode = "";
            Notfallcodes = a.Wert;
            Hinweis = "Zweitfaktor eingerichtet. Melde dich jetzt mit dem nächsten Code an.";
        }).ConfigureAwait(true);
    }

    /// <summary>"Aufgeschrieben": die Notfallcodes verschwinden aus der Anzeige und kommen nie wieder.</summary>
    [RelayCommand]
    private void NotfallcodesGesehen() => Notfallcodes = null;

    // ---- Anmeldung ----

    private bool KannAnmelden() => !Beschaeftigt && (MitNotfallcode ? Codeform.Notfall(Code) : Codeform.Totp(Code)) is not null;

    [RelayCommand(CanExecute = nameof(KannAnmelden))]
    private async Task Anmelden()
    {
        // Vor dem Warten gelesen: Die Lage mit der offenen Sitzung kann vor dem Ende von AnmeldenAsync ankommen.
        var zuMitgliedern = _mitgliederNachAnmeldung;
        await Arbeiten(async () =>
        {
            var a = await _dienst.AnmeldenAsync(Code, MitNotfallcode).ConfigureAwait(true);
            Code = "";
            if (!a.Ok)
            {
                Fehler = a.Fehler;
                return;
            }
            MitNotfallcode = false;
            // Seit 0.8.0 beginnt die offene Verwaltung mit der Übersicht "Heute"; wartende Anträge stehen dort mit Sprung. Kam vorher ein Sprung aus
            // "Wer ist da" (seit 0.11.5), beginnt sie bei Mitglieder, wo seine Karte aufgeht.
            Bereich = zuMitgliedern ? Verwaltungsbereich.Mitglieder : Verwaltungsbereich.Heute;
            _mitgliederNachAnmeldung = false;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task Abmelden()
    {
        await _dienst.AbmeldenAsync().ConfigureAwait(true);
        Hinweis = "Verwaltung geschlossen. Der Funk läuft weiter.";
    }

    private async Task Arbeiten(Func<Task> arbeit)
    {
        Fehler = null;
        Hinweis = null;
        Beschaeftigt = true;
        try
        {
            await arbeit().ConfigureAwait(true);
        }
        finally
        {
            Beschaeftigt = false;
        }
    }

    /// <summary>
    /// Der Nutzer hat den Bereich Verwaltung verlassen: Geheimnis und QR-Code fort (Gegenprüfung Stufe 1; die Vorlage verlangt
    /// "löscht es, sobald das Fenster zugeht"). Abholen geht nur einmal; der Hinweis sagt, wie es weitergeht.
    /// </summary>
    public void BereichVerlassen()
    {
        // Ein Sprung aus "Wer ist da", der auf die Anmeldung wartete, gilt nicht mehr (seit der Gegenprüfung 0.11.5).
        Mitglieder.SprungVergessen();
        _mitgliederNachAnmeldung = false;
        if (Geheimnis is null && QrBildGeometrie is null) return;
        GeheimnisVergessen();
        Hinweis = "Das Geheimnis ist fort, weil du den Bereich verlassen hast. Steht es schon in deiner Authenticator-App, gib einfach den "
            + "ersten Code ein. Sonst richtet der Eigentümer den Zweitfaktor an der Konsole neu ein (admin-einrichtung-neu).";
    }

    /// <summary>Geheimnis und QR-Code aus der Anzeige. Die Zeichenketten selbst lassen sich in .NET nicht nullen (3.9).</summary>
    private void GeheimnisVergessen()
    {
        Geheimnis = null;
        QrBildGeometrie = null;
        QrSeite = 0;
        GeheimnisBis = null;
    }

    /// <summary>Base32 in Vierergruppen zum Abtippen, etwa "JBSW Y3DP EHPK 3PXP".</summary>
    internal static string Vierergruppen(string text)
    {
        var s = text.Replace(" ", "", StringComparison.Ordinal);
        return string.Join(" ", Enumerable.Range(0, (s.Length + 3) / 4).Select(i => s.Substring(i * 4, Math.Min(4, s.Length - (i * 4)))));
    }
}

