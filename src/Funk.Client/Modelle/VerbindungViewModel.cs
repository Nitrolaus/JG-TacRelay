// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Verbindung und Anmeldung (A12, A25, A42), seit 0.6.0 auf der Startseite (<see cref="StartViewModel"/>): Status, Anmeldung mit
/// SC-Name und Discord-Name ("Anmelden als ... - stimmt das?"), Warteraum mit der Gerätekennung zum Vergleichen; für die Details im
/// Admin-Setup das eingebaute Serverprofil (nur zur Ansicht: Profil und Profilschlüssel kommen mit dem Client, eine Eingabe gibt
/// es nicht, ANMELDUNG-SICHERHEIT P1), Server, Konto, Gerät und die Adresse. Aus der Zeit vor dem Bot bleiben Registrierungscode
/// (3.4) und die Rückfrage "Mit Konto X verknüpfen?".
/// </summary>
public sealed partial class VerbindungViewModel : ObservableObject
{
    private readonly IFunkDienst _dienst;
    private readonly Einstellungsablage _ablage;
    private readonly IOberflaechentakt _takt;
    private readonly IZwischenablage? _zwischenablage;
    private readonly Profilquelle _profil;
    private readonly bool _ohneProfil;
    private readonly Func<DateTimeOffset> _jetzt;
    private TaskCompletionSource<bool>? _rueckfrage;
    private TaskCompletionSource<Anmeldedaten?>? _anmeldung;
    private DateTimeOffset? _codeBis;
    private string? _codeAngefordert;
    private bool _frischUebernommen;

    /// <param name="zwischenablage">Für "Kopieren" der Gerätekennung; ohne sie (Tests) gibt es den Knopf nicht.</param>
    /// <param name="profil">Serverprofil und Profilschlüssel; ohne Angabe die eingebauten (<see cref="Profilquelle.Eingebaut"/>).</param>
    /// <param name="ohneProfil">Vorführung oder Testzugang von der Kommandozeile: kein Serverprofil nötig.</param>
    /// <param name="jetzt">Uhr für den Ablauf des Profils und des Registrierungscodes (Tests).</param>
    /// <param name="uebernahme">Übernahme neuerer Profile vom Server (F1); meldet sie, damit die Karte sie zeigt.</param>
    public VerbindungViewModel(IFunkDienst dienst, Einstellungsablage ablage, IOberflaechentakt takt, IZwischenablage? zwischenablage = null,
        Profilquelle? profil = null, bool ohneProfil = false, Func<DateTimeOffset>? jetzt = null, Profiluebernahme? uebernahme = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(takt);
        _dienst = dienst;
        _ablage = ablage;
        _takt = takt;
        _zwischenablage = zwischenablage;
        _profil = profil ?? uebernahme?.Quelle ?? Profilquelle.Eingebaut;
        _ohneProfil = ohneProfil;
        _jetzt = jetzt ?? (() => DateTimeOffset.UtcNow);
        ScName = _ablage.Aktuell.Anmeldung.ScName ?? "";
        DiscordName = _ablage.Aktuell.Anmeldung.DiscordName ?? "";
        _dienst.LageGeaendert += () => _takt.Einreihen(Aktualisieren);
        // F1: Ein vom Server übernommenes Profil zeigt die Karte sofort; der Client nimmt es beim nächsten Verbinden.
        if (uebernahme is not null) uebernahme.Uebernommen += _ => _takt.Einreihen(() =>
        {
            _frischUebernommen = true;
            Pruefen();
        });
        Aktualisieren();
        Pruefen();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstAktiv), nameof(IstRegistrierung), nameof(IstGetrennt), nameof(IstWarteraum), nameof(ZeigtWasFehlt),
        nameof(ZeigtAnmeldung))]
    [NotifyCanExecuteChangedFor(nameof(VerbindenCommand), nameof(TrennenCommand), nameof(SoAnmeldenCommand))]
    public partial Verbindungsstatus Status { get; private set; }

    public bool IstAktiv => Status is Verbindungsstatus.Aktiv or Verbindungsstatus.AktivTunnel;

    public bool IstGetrennt => Status == Verbindungsstatus.Getrennt;

    public bool IstRegistrierung => Status == Verbindungsstatus.Registrierung && Code is not null;

    public bool IstWarteraum => Status == Verbindungsstatus.Warteraum && GeraetAnzeige is not null;

    [ObservableProperty]
    public partial string Server { get; private set; } = "-";

    [ObservableProperty]
    public partial string Konto { get; private set; } = "-";

    [ObservableProperty]
    public partial string Stufe { get; private set; } = "-";

    [ObservableProperty]
    public partial string Geraet { get; private set; } = "-";

    /// <summary>Die letzte Sitzung endete mit "entfernt" (<see cref="FunkLage.Entfernt"/>, seit 0.6.3).</summary>
    [ObservableProperty]
    public partial bool Entfernt { get; private set; }

    /// <summary>Gerätekennung (Anzeigeform des Fingerabdrucks) allein, zum Vergleichen durch den Admin.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstWarteraum), nameof(GeraetenameSatz))]
    public partial string? GeraetAnzeige { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HinweisKurz), nameof(HinweisGanz))]
    public partial string? Hinweis { get; private set; }

    /// <summary>
    /// Der <see cref="Hinweis"/> für die Startseite, gekürzt wie <see cref="WasFehlt"/> (Q23e: lange Hinweise kürzen, Rest im
    /// Hilfetext): der erste Satz, bei Fehlern mit der Fehlernummer. So bleibt die Startseite auch mit Gerätewarnung ohne
    /// Rollbalken (Q12, Gegenprüfung Stufe 2).
    /// </summary>
    public string? HinweisKurz => Hinweis is { } h ? Kuerzen(h) : null;

    /// <summary>Der ganze <see cref="Hinweis"/> als Hilfetext, nur wenn <see cref="HinweisKurz"/> etwas weglässt.</summary>
    public string? HinweisGanz => Hinweis is { } h && !string.Equals(Kuerzen(h), h, StringComparison.Ordinal) ? h : null;

    /// <summary>Erster Satz; eine Fehlernummer am Ende (" (Fehler NN)", <see cref="Fehlernummern.Anhaengen"/>) bleibt dran.</summary>
    private static string Kuerzen(string text)
    {
        var n = text.LastIndexOf(" (Fehler ", StringComparison.Ordinal);
        return n > 0 && text.EndsWith(')') ? ErsterSatz(text[..n]) + text[n..] : ErsterSatz(text);
    }

    /// <summary>Die technischen Einzelheiten zum <see cref="Hinweis"/> (Adresse, Port, Meldung von Windows); nur unter Details im Admin-Setup.</summary>
    [ObservableProperty]
    public partial string? HinweisEinzelheit { get; private set; }

    // ---- Anmeldung mit Namen (A42) ----

    /// <summary>SC-Name, wie im Spiel ("Mustermann"); vorbelegt aus dem Setup.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NamenFehler), nameof(AnmeldeFrage))]
    [NotifyCanExecuteChangedFor(nameof(SoAnmeldenCommand))]
    public partial string ScName { get; set; }

    /// <summary>Discord-Name, wie er auf dem Discord-Server der Gruppe steht ("Mustermann/Max"); der Schrägstrich trennt nichts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NamenFehler), nameof(AnmeldeFrage))]
    [NotifyCanExecuteChangedFor(nameof(SoAnmeldenCommand))]
    public partial string DiscordName { get; set; }

    /// <summary>Was an den Namen nicht passt, oder <c>null</c>.</summary>
    public string? NamenFehler
    {
        get
        {
            var sc = ScName.Trim().Length > 0 && Namensregeln.ScName(ScName) is null;
            var dc = DiscordName.Trim().Length > 0 && Namensregeln.DiscordName(DiscordName) is null;
            return sc ? "Der SC-Name braucht 2 bis 32 Zeichen: Buchstaben, Ziffern, Leerzeichen, _ - und Punkt, so wie im Spiel."
                : dc ? "Der Discord-Name braucht 2 bis 32 Zeichen, ohne unsichtbare Zeichen und ohne doppelte Leerzeichen."
                : null;
        }
    }

    /// <summary>"Anmelden als Mustermann (Discord: Mustermann/Max) - stimmt das?", sobald beide Namen passen.</summary>
    public string AnmeldeFrage => Namensregeln.ScName(ScName) is { } sc && Namensregeln.DiscordName(DiscordName) is { } dc
        ? "Anmelden als " + sc + " (Discord: " + dc + ") - stimmt das?"
        : "Trag deinen Namen in Star Citizen und deinen Namen in Discord ein.";

    /// <summary>Der Client wartet in der REGISTRIERUNG auf die Namen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZeigtAnmeldung), nameof(AnmeldeKnopf))]
    [NotifyCanExecuteChangedFor(nameof(SoAnmeldenCommand), nameof(AnmeldungAbbrechenCommand))]
    public partial bool AnmeldungWartet { get; private set; }

    /// <summary>
    /// Die Karte mit den Namen: solange der Client in der REGISTRIERUNG danach fragt, und vor dem ersten Verbinden, solange die
    /// Namen noch nicht bestätigt sind (erster Start nach dem Setup).
    /// </summary>
    public bool ZeigtAnmeldung => AnmeldungWartet || (IstGetrennt && !_ablage.Aktuell.Anmeldung.Bestaetigt);

    public string AnmeldeKnopf => AnmeldungWartet ? "So anmelden" : "So anmelden und verbinden";

    // ---- Registrierungscode (Weg mit Bot, 3.4) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstRegistrierung), nameof(CodeAbgelaufenText))]
    [NotifyCanExecuteChangedFor(nameof(NeuenCodeCommand))]
    public partial string? Code { get; private set; }

    /// <summary>Der ganze Befehl für Discord, mit Code (der Server schickt nur <c>/funk verknuepfen</c>, 4.5).</summary>
    [ObservableProperty]
    public partial string? CodeBefehl { get; private set; }

    /// <summary>"Gültig bis 23:45 Uhr, ..." oder, nach Ablauf, "Abgelaufen um 23:45 Uhr."</summary>
    [ObservableProperty]
    public partial string? CodeGueltig { get; private set; }

    /// <summary>
    /// Der Code ist abgelaufen und kein neuer da. Die ersten beiden Ersatzcodes holt der Client selbst (Sitzung, 3.4); nach dem
    /// dritten kommt keiner mehr, denn der Server gibt höchstens drei je Verbindung. Dann hilft nur neu verbinden.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NeuenCodeCommand))]
    public partial bool CodeAbgelaufen { get; private set; }

    /// <summary>Was bei abgelaufenem Code zu tun ist, oder dass der neue schon unterwegs ist.</summary>
    public string CodeAbgelaufenText => Code is not null && Code == _codeAngefordert
        ? "Neuer Code wird geholt, dafür verbindet der Client einmal neu."
        : "Dieser Code ist abgelaufen, und von selbst kommt keiner mehr: Der Server gibt je Verbindung höchstens drei. "
          + "Neuen Code holen verbindet einmal neu und bringt einen frischen, wie Trennen und Verbinden.";

    /// <summary>
    /// So lange nach "gültig bis" gilt ein Code noch nicht als abgelaufen: Die ersten Ersatzcodes holt der Client selbst,
    /// binnen Sekundenbruchteilen, und die Karte soll dabei nicht kurz "abgelaufen" zeigen.
    /// </summary>
    public static readonly TimeSpan CodeNachfrist = TimeSpan.FromSeconds(5);

    /// <summary>Offene Rückfrage "Mit Konto X verknüpfen?" oder <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatRueckfrage))]
    public partial string? Rueckfrage { get; private set; }

    public bool HatRueckfrage => Rueckfrage is not null;

    // ---- Warteraum ----

    /// <summary>Namen des Antrags, die schon ein freigegebenes Konto trägt (F1), als Satz; sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? Vergeben { get; private set; }

    /// <summary>Ob es "Kopieren" gibt (nicht in Tests ohne Zwischenablage).</summary>
    public bool KannKopieren => _zwischenablage is not null;

    /// <summary>Rückmeldung nach dem Kopieren.</summary>
    [ObservableProperty]
    public partial string? Kopiert { get; private set; }

    /// <summary>Was du dem Admin in Discord schickst (ANMELDUNG-SICHERHEIT, Ablauf des Antrags, Schritt 3).</summary>
    public static string WarteraumSatz { get; } =
        "Schick dem Admin in Discord deinen SC-Namen und diese Kennung. Die Kennung ist kein Geheimnis. Nach Codes oder Passwörtern fragt dich niemand.";

    // ---- Serverprofil (eingebaut, nur Ansicht) ----

    /// <summary>Name, Host und Port aus dem geprüften Profil, etwa "Meine Staffel: funk.example.org (nur IPv4), TCP-Port 8443".</summary>
    [ObservableProperty]
    public partial string? ProfilZiel { get; private set; }

    /// <summary>"Signatur stimmt ..." mit Kennung und Ablaufdatum; nur mit gültigem Profil.</summary>
    [ObservableProperty]
    public partial string? ProfilSignatur { get; private set; }

    /// <summary>Was mit dem eingebauten Profil nicht stimmt; steht in der Warnfarbe.</summary>
    [ObservableProperty]
    public partial string? ProfilFehler { get; private set; }

    /// <summary>Hinweis ohne Rot: Diese Fassung bringt kein Profil mit (dann nur Probefunk).</summary>
    [ObservableProperty]
    public partial string? ProfilOffen { get; private set; }

    /// <summary>Warnung, die das Verbinden nicht aufhält: Das Profil ist abgelaufen (3.1).</summary>
    [ObservableProperty]
    public partial string? ProfilWarnung { get; private set; }

    /// <summary>Pfad oder Meldung von Windows zum Profilfehler; nur unter Details im Admin-Setup.</summary>
    [ObservableProperty]
    public partial string? ProfilEinzelheit { get; private set; }

    /// <summary>
    /// "Vom Server übernommen, Version N." (F1), gleich nach der Übernahme mit "Gilt ab dem nächsten Verbinden."; <c>null</c>,
    /// solange das Profil aus dem Setup gilt.
    /// </summary>
    [ObservableProperty]
    public partial string? ProfilHerkunft { get; private set; }

    // ---- Adresse und Bereitschaft ----

    public string Adresse
    {
        get => _ablage.Aktuell.Verbindung.Adresse ?? "";
        set
        {
            var neu = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(neu, _ablage.Aktuell.Verbindung.Adresse, StringComparison.Ordinal)) return;
            _ablage.Aktuell.Verbindung.Adresse = neu;
            _ablage.Vormerken();
            OnPropertyChanged();
            Pruefen();
            Geaendert();
        }
    }

    [ObservableProperty]
    public partial string? AdresseFehler { get; private set; }

    /// <summary>
    /// Wie das Gerät im Antrag heißt, nur als Zeile in der Antragskarte (Q4: kein Feld, kein Knopf). Seit 0.6.3 aus der
    /// Gerätekennung (<see cref="VerbindungsEinstellungen.NameAusKennung"/>), nie der Name des Windows-PCs. Vor dem ersten
    /// Verbinden gibt es die Kennung noch nicht; dann sagt die Zeile, wie der Name entsteht.
    /// </summary>
    public string GeraetenameSatz => GeraetAnzeige is { } kennung
        ? "Dieses Gerät heißt im Antrag: " + VerbindungsEinstellungen.NameAusKennung(kennung)
        : "Im Antrag heißt dieses Gerät PC- und die ersten vier Zeichen seiner Gerätekennung, nie wie dein Windows-PC.";

    /// <summary>Was zum Verbinden noch fehlt, genau ein Satz; <c>null</c>, wenn alles passt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bereit), nameof(ZeigtWasFehlt))]
    [NotifyCanExecuteChangedFor(nameof(VerbindenCommand), nameof(SoAnmeldenCommand))]
    public partial string? WasFehlt { get; private set; }

    public bool Bereit => WasFehlt is null;

    public bool ZeigtWasFehlt => WasFehlt is not null && IstGetrennt;

    /// <summary>
    /// Die Adresse (Details im Admin-Setup) wurde geändert, während ein Client läuft oder es noch versucht. Der bleibt beim Stand, mit dem er
    /// gebaut wurde; die Änderung gilt erst nach Trennen und Verbinden. <c>null</c>, wenn nichts wartet.
    /// </summary>
    [ObservableProperty]
    public partial string? AenderungWartet { get; private set; }

    private bool KannVerbinden() => IstGetrennt && Bereit;

    /// <summary>Prüft das Profil vorher noch einmal; passt es nicht mehr, verbindet der Klick nicht, und oben steht, was fehlt.</summary>
    [RelayCommand(CanExecute = nameof(KannVerbinden))]
    private Task VerbindenAsync()
    {
        Pruefen();
        return Bereit ? _dienst.VerbindenAsync() : Task.CompletedTask;
    }

    private bool KannNeuenCode() => CodeAbgelaufen && Code is not null && Code != _codeAngefordert;

    /// <summary>Nach dem dritten abgelaufenen Code: einmal neu verbinden (3.4), wie Trennen und Verbinden in einem Schritt.</summary>
    [RelayCommand(CanExecute = nameof(KannNeuenCode))]
    private Task NeuenCodeAsync()
    {
        _codeAngefordert = Code;
        NeuenCodeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CodeAbgelaufenText));
        return _dienst.VerbindenAsync();
    }

    /// <summary>
    /// Bildtakt: Läuft der Registrierungscode ab, ändert sich die Lage nicht, das merkt nur die Uhr. Ohne Code kostet es nichts,
    /// mit Code einen Vergleich; neu geschrieben wird nur beim Wechsel.
    /// </summary>
    public void Takt()
    {
        if (_codeBis is not { } bis) return;
        if (Abgelaufen(bis) != CodeAbgelaufen) CodeZeitSetzen();
    }

    private bool Abgelaufen(DateTimeOffset bis) => _jetzt() > bis + CodeNachfrist;

    /// <summary>Ablauf und Zeile "Gültig bis" des Registrierungscodes aus <see cref="_codeBis"/>.</summary>
    private void CodeZeitSetzen()
    {
        if (_codeBis is not { } bis)
        {
            CodeAbgelaufen = false;
            CodeGueltig = null;
            return;
        }
        CodeAbgelaufen = Abgelaufen(bis);
        var uhr = bis.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
        CodeGueltig = CodeAbgelaufen ? "Abgelaufen um " + uhr + " Uhr." : "Gültig bis " + uhr + " Uhr, nur für dieses Gerät.";
    }

    [RelayCommand(CanExecute = nameof(KannTrennen))]
    private Task TrennenAsync() => _dienst.TrennenAsync();

    private bool KannTrennen() => !IstGetrennt;

    /// <summary>Eine Verbindungseinstellung hat sich geändert; läuft ein Client, gilt sie erst nach Trennen und Verbinden.</summary>
    private void Geaendert()
    {
        if (!IstGetrennt) AenderungWartet = "Geändert. Das gilt erst beim nächsten Verbinden: erst Trennen, dann Verbinden.";
    }

    /// <summary>Getrennt heißt: Der nächste Klick auf Verbinden baut den Client mit dem neuen Stand.</summary>
    partial void OnStatusChanged(Verbindungsstatus value)
    {
        if (value == Verbindungsstatus.Getrennt) AenderungWartet = null;
    }

    // ---- Anmeldung ----

    private bool KannAnmelden() => Namensregeln.ScName(ScName) is not null && Namensregeln.DiscordName(DiscordName) is not null
        && (AnmeldungWartet || (IstGetrennt && Bereit));

    /// <summary>
    /// "So anmelden": die Namen merken (bestätigt) und, wenn der Client gerade fragt, den Antrag stellen; vor dem ersten
    /// Verbinden verbinden. Der Server nimmt sie nur als Angabe; freigeschaltet wird durch einen Admin nach dem Kennungsvergleich.
    /// </summary>
    [RelayCommand(CanExecute = nameof(KannAnmelden))]
    private Task SoAnmeldenAsync()
    {
        if (Namensregeln.ScName(ScName) is not { } sc || Namensregeln.DiscordName(DiscordName) is not { } dc) return Task.CompletedTask;
        var a = _ablage.Aktuell.Anmeldung;
        a.ScName = sc;
        a.DiscordName = dc;
        a.Bestaetigt = true;
        _ablage.Vormerken();
        ScName = sc;
        DiscordName = dc;
        OnPropertyChanged(nameof(ZeigtAnmeldung));
        if (_anmeldung is { } offen)
        {
            _anmeldung = null;
            AnmeldungWartet = false;
            offen.TrySetResult(new Anmeldedaten(sc, dc));
            return Task.CompletedTask;
        }
        return VerbindenAsync();
    }

    private bool KannAbbrechen() => AnmeldungWartet;

    /// <summary>Kein Antrag jetzt; die Verbindung läuft nach 5 Minuten ab (4.6), Trennen geht sofort.</summary>
    [RelayCommand(CanExecute = nameof(KannAbbrechen))]
    private void AnmeldungAbbrechen()
    {
        var offen = _anmeldung;
        _anmeldung = null;
        AnmeldungWartet = false;
        offen?.TrySetResult(null);
    }

    /// <summary>
    /// Frage des Clients in der REGISTRIERUNG (A42), aus dessen Thread: Sind die Namen schon bestätigt, gehen sie ohne Frage
    /// hinaus; sonst zeigt der Bereich die Karte und wartet auf "So anmelden". Frist und Abbruch gelten als "jetzt nicht".
    /// </summary>
    public async Task<Anmeldedaten?> AnmeldungAsync(CancellationToken abbruch)
    {
        var a = _ablage.Aktuell.Anmeldung;
        if (a is { Bestaetigt: true, ScName: { } sc, DiscordName: { } dc }) return new Anmeldedaten(sc, dc);
        var tcs = new TaskCompletionSource<Anmeldedaten?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _takt.Einreihen(() =>
        {
            _anmeldung?.TrySetResult(null);
            _anmeldung = tcs;
            AnmeldungWartet = true;
        });
        using var registrierung = abbruch.Register(() => tcs.TrySetResult(null));
        var antwort = await tcs.Task.ConfigureAwait(false);
        _takt.Einreihen(() =>
        {
            if (!ReferenceEquals(_anmeldung, tcs)) return;
            _anmeldung = null;
            AnmeldungWartet = false;
        });
        return antwort;
    }

    // ---- Warteraum ----

    private bool KannKopierenJetzt() => _zwischenablage is not null && GeraetAnzeige is not null;

    [RelayCommand(CanExecute = nameof(KannKopierenJetzt))]
    private void KennungKopieren()
    {
        if (_zwischenablage is null || GeraetAnzeige is null) return;
        Kopiert = _zwischenablage.Kopieren(GeraetAnzeige)
            ? "Kopiert. In Discord mit Strg+V einfügen."
            : "Kopieren ging nicht; markiere die Kennung und nimm Strg+C.";
    }

    // ---- Rückfrage mit Code (Weg mit Bot) ----

    [RelayCommand]
    private void RueckfrageJa() => RueckfrageBeenden(true);

    [RelayCommand]
    private void RueckfrageNein() => RueckfrageBeenden(false);

    /// <summary>
    /// Rückfrage des Servers (3.4 Schritt 5), aus dem Thread des Clients. Wartet auf Ja oder Nein; Frist und Abbruch
    /// gelten als Nein.
    /// </summary>
    public async Task<bool> RueckfrageAsync(VerknuepfungKonto konto, CancellationToken abbruch)
    {
        ArgumentNullException.ThrowIfNull(konto);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _takt.Einreihen(() =>
        {
            _rueckfrage?.TrySetResult(false);
            _rueckfrage = tcs;
            Rueckfrage = "Mit dem Konto " + konto.Name + " (" + konto.Benutzername + ") verknüpfen?" + (konto.Neu ? " Das Konto wird neu angelegt." : "");
        });
        using var registrierung = abbruch.Register(() => tcs.TrySetResult(false));
        var antwort = await tcs.Task.ConfigureAwait(false);
        _takt.Einreihen(() =>
        {
            if (ReferenceEquals(_rueckfrage, tcs))
            {
                _rueckfrage = null;
                Rueckfrage = null;
            }
        });
        return antwort;
    }

    private void RueckfrageBeenden(bool ja)
    {
        var t = _rueckfrage;
        _rueckfrage = null;
        Rueckfrage = null;
        t?.TrySetResult(ja);
    }

    /// <summary>
    /// Prüft das Profil und die Adresse wie vor dem Verbinden und sagt, was fehlt. Es gilt das höchste gültige aus Setup und
    /// Ablage (F1, <see cref="Profilpruefung.Waehlen"/>), wie in der Verbindungsfabrik.
    /// </summary>
    internal void Pruefen()
    {
        var v = _ablage.Aktuell.Verbindung;
        var testzugang = _ohneProfil || !string.IsNullOrWhiteSpace(v.Testzugang);
        var befund = Profilpruefung.Waehlen(_profil, _jetzt());
        if (befund.Profil is { } p)
        {
            var version = p.ProfilVersion.ToString(CultureInfo.CurrentCulture);
            ProfilZiel = p.Name + ": " + Profilpruefung.Hosts(p) + ", TCP-Port " + p.TcpPort.ToString(CultureInfo.InvariantCulture);
            ProfilSignatur = "Signatur stimmt (Profilschlüssel " + befund.Kennung + "), Version " + version + ", gültig bis " + Profilpruefung.GueltigBis(p) + ".";
            ProfilHerkunft = befund.Herkunft == Profilherkunft.Server
                ? "Vom Server übernommen, Version " + version + "." + (_frischUebernommen ? " Gilt ab dem nächsten Verbinden." : "")
                : null;
            ProfilWarnung = befund.Abgelaufen
                ? "Das Profil ist am " + Profilpruefung.GueltigBis(p) + " abgelaufen. Es gilt weiter; ein neueres kommt mit dem nächsten Setup oder vom Server."
                : null;
            ProfilFehler = null;
            ProfilOffen = null;
        }
        else
        {
            // Ohne eingebautes Profil ist nichts falsch, nur offen (Probefunk); rot wird es erst, wenn eines da ist und nicht passt.
            ProfilZiel = null;
            ProfilSignatur = null;
            ProfilHerkunft = null;
            ProfilWarnung = null;
            ProfilFehler = befund.Unvollstaendig ? null : befund.Fehler;
            ProfilOffen = befund.Unvollstaendig ? befund.Fehler : null;
        }

        ProfilEinzelheit = befund.Einzelheit;
        AdresseFehler = Adressangabe.TryLesen(v.Adresse, out _, out var fehler) ? null : fehler;
        // Für Funker der erste Satz mit Fehlernummer, ohne Pfad (Q5); die ganze Meldung und die Einzelheit stehen unter Details.
        WasFehlt = testzugang ? null
            : befund.Fehler is { } f ? Fehlernummern.Anhaengen(ErsterSatz(f), befund.Nummer)
            : AdresseFehler is not null ? Fehlernummern.Anhaengen("Die Adresse unter Details ist ungültig.", Fehlernummern.EinstellungUngueltig)
            : null;
        OnPropertyChanged(nameof(ZeigtAnmeldung));
    }

    /// <summary>Der erste Satz einer Meldung; die ganze steht in der Karte darunter.</summary>
    private static string ErsterSatz(string text)
    {
        var ende = text.IndexOf(". ", StringComparison.Ordinal);
        return ende < 0 ? text : text[..(ende + 1)];
    }

    internal void Aktualisieren()
    {
        var l = _dienst.Lage;
        // Vor dem Status: Die Startseite rechnet beim Statuswechsel und liest dann schon den neuen Stand.
        Entfernt = l.Entfernt;
        Status = l.Status;
        Server = l.Server ?? "-";
        Konto = l.Konto is { } k ? k.Name + (k.Benutzername is "-" or "" ? "" : " (" + k.Benutzername + ")") : "-";
        Stufe = l.Konto?.Stufe switch
        {
            Protokoll.Steuerung.Stufe.Admin => "Admin",
            Protokoll.Steuerung.Stufe.Raidleiter => "Führung",
            Protokoll.Steuerung.Stufe.Funker => "Funker",
            _ => "-",
        };
        Geraet = l.Geraet is null ? "-" : l.Geraet + (l.Schluesselart is null ? "" : " (" + l.Schluesselart + ")");
        GeraetAnzeige = l.Geraet;
        if (l.Status != Verbindungsstatus.Warteraum) Kopiert = null;
        Vergeben = l.ScNamenVergeben.Count == 0 ? null
            : string.Join(", ", l.ScNamenVergeben) + (l.ScNamenVergeben.Count == 1 ? " ist" : " sind")
              + " schon vergeben. Nach der Freigabe in den Einstellungen ändern oder mit dem Admin klären.";
        Hinweis = l.Hinweis;
        HinweisEinzelheit = l.HinweisEinzelheit;
        Code = l.Registrierung?.Code;
        CodeBefehl = l.Registrierung is { } reg
            ? reg.Befehl.Contains(reg.Code, StringComparison.Ordinal) ? reg.Befehl : reg.Befehl + " " + reg.Code
            : null;
        _codeBis = l.Registrierung?.GueltigBis;
        CodeZeitSetzen();
        OnPropertyChanged(nameof(IstRegistrierung));
        OnPropertyChanged(nameof(ZeigtWasFehlt));
        KennungKopierenCommand.NotifyCanExecuteChanged();
    }

    public static string Text(Verbindungsstatus s) => s switch
    {
        Verbindungsstatus.Getrennt => "Getrennt",
        Verbindungsstatus.Verbinden => "Verbinde...",
        Verbindungsstatus.Registrierung => "Gerät anmelden",
        Verbindungsstatus.Warteraum => "Wartet auf Freigabe",
        Verbindungsstatus.Abgleich => "Abgleich",
        Verbindungsstatus.AktivTunnel => "Verbunden (Tunnel)",
        _ => "Verbunden",
    };
}
