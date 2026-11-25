// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung.Modelle;

/// <summary>
/// Bereich Anträge (3.4, 3.5, ANMELDUNG-SICHERHEIT "Regeln für Admins beim Freigeben"): wartende Geräte mit SC-Name,
/// Discord-Name, Gerätekennung und Schlüsselart ("laut Client, ungeprüft"). Freigeben geht erst, wenn der Admin die Kennung mit
/// der verglichen hat, die ihm die Person nennt (persönlich, am Telefon, im Sprachkanal oder per Nachricht), und auf
/// "Verifiziert" geklickt hat (seit 0.6.3, Sarevoc 04.10.2026; vorher tippte er sie ab, obwohl sie daneben stand). Eine passende
/// Vormerkung wird vorgeschlagen, nie von selbst übernommen. Freigeben und Ablehnen sind heikel und brauchen einen frischen Code.
/// Seit 0.7.0 gilt ein PC, ein Konto (Sarevoc, 04.10.2026): Jeder PC stellt seinen eigenen Antrag und bekommt sein eigenes Konto, und
/// Ablehnen löscht den Antrag vollständig, ohne etwas zu sperren.
/// </summary>
public sealed partial class AntraegeViewModel : ObservableObject
{
    private readonly IVerwaltungsdienst _dienst;
    private readonly Nachschlag _nachschlag;
    private readonly Func<DateTimeOffset> _jetzt;

    public AntraegeViewModel(IVerwaltungsdienst dienst, Nachschlag nachschlag, Func<DateTimeOffset>? jetzt = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(nachschlag);
        _dienst = dienst;
        _nachschlag = nachschlag;
        _jetzt = jetzt ?? (() => DateTimeOffset.Now);
    }

    public ObservableCollection<AntragZeile> Antraege { get; } = [];

    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    public bool Leer => Antraege.Count == 0;

    /// <summary>Merkzettel neben dem Freigabeknopf (ANMELDUNG-SICHERHEIT, Regeln für Admins beim Freigeben).</summary>
    public static IReadOnlyList<string> Regeln { get; } =
    [
        "Die Kennung zählt nur, wenn sie wirklich von der Person kommt: persönlich, am Telefon, im Sprachkanal oder von ihrem bekannten Konto. Bei einer Nachricht den eindeutigen Benutzernamen prüfen, nicht nur den Anzeigenamen; am sichersten schreibst du selbst zuerst.",
        "Keine Kennung \"für einen Freund\" freigeben. Jeder schickt seine eigene.",
        "Zeitdruck (\"der Raid fängt gleich an\") ist ein Warnzeichen, kein Grund. Im Zweifel ablehnen; ein Antrag lässt sich jederzeit neu stellen.",
        "Ein PC, ein Konto: Ein weiterer PC desselben Mitglieds stellt seinen eigenen Antrag und bekommt sein eigenes Konto.",
    ];

    public async Task LadenAsync()
    {
        var a = await _dienst.AntraegeAsync().ConfigureAwait(true);
        if (!a.Ok)
        {
            Fehler = a.Fehler;
            return;
        }
        Fehler = null;
        Antraege.Clear();
        foreach (var g in a.Wert!.OrderBy(g => g.Verknuepft)) Antraege.Add(new AntragZeile(g, this, _nachschlag, _jetzt()));
        OnPropertyChanged(nameof(Leer));
    }

    [RelayCommand]
    private Task Neu() => LadenAsync();

    /// <summary>
    /// Die Rückfrage vor "Alle ablehnen, die zu keiner Vormerkung passen" steht offen (seit 0.7.0): Ein Klick trifft viele Anträge
    /// auf einmal. Gesperrt wird dabei nichts (Sarevoc, 04.10.2026: keine Fehlbedienung darf jemanden aussperren).
    /// </summary>
    [ObservableProperty]
    public partial bool UnpassendeFragen { get; private set; }

    public static string UnpassendeFrage { get; } =
        "Alle Anträge ablehnen, die zu keiner Vormerkung passen? Der Server löscht sie vollständig. Gesperrt wird nichts: Wer echt ist, "
        + "stellt danach einfach einen neuen Antrag. Gedacht gegen eine Flut fremder Anträge.";

    [RelayCommand]
    private void UnpassendeAblehnenFragen() => UnpassendeFragen = true;

    [RelayCommand]
    private void UnpassendeAblehnenAbbrechen() => UnpassendeFragen = false;

    /// <summary>"Alle ablehnen, die zu keiner Vormerkung passen" gegen eine Antragsflut (heikel), erst nach der Rückfrage.</summary>
    [RelayCommand]
    private async Task UnpassendeAblehnen()
    {
        UnpassendeFragen = false;
        var a = await _dienst.UnpassendeAblehnenAsync().ConfigureAwait(true);
        if (!a.Ok)
        {
            Fehler = a.Fehler;
            return;
        }
        Hinweis = a.Wert == 1
            ? "Ein Antrag ohne passende Vormerkung abgelehnt und gelöscht."
            : a.Wert.ToString(CultureInfo.CurrentCulture) + " Anträge ohne passende Vormerkung abgelehnt und gelöscht.";
        await LadenAsync().ConfigureAwait(true);
    }

    internal async Task EntscheidenAsync(AntragZeile zeile, GeraetEntscheidung entscheidung)
    {
        // Eine Vormerkung gibt es nur bei einem Antrag; ein weiteres Gerät eines Kontos hat sein Konto schon. An ein anderes Konto
        // hängt der Verwaltungsteil seit 0.7.0 kein Gerät mehr (ein PC, ein Konto); der Server könnte es noch.
        var freigeben = entscheidung == GeraetEntscheidung.Freigeben && zeile.IstAntrag;
        var e = await _dienst.EntscheidenAsync(zeile.GeraetId, entscheidung, freigeben ? zeile.VormerkungId : null, null).ConfigureAwait(true);
        if (e is not null)
        {
            zeile.Fehler = e;
            return;
        }
        Hinweis = entscheidung == GeraetEntscheidung.Freigeben
            ? zeile.Anzeigename + " ist freigegeben."
            : "Antrag von " + zeile.Anzeigename + " abgelehnt und gelöscht. Der PC kann einen neuen Antrag stellen.";
        Antraege.Remove(zeile);
        OnPropertyChanged(nameof(Leer));
    }
}

/// <summary>Ein wartendes Gerät mit Kennungsvergleich ("Verifiziert") und Vorschlägen.</summary>
public sealed partial class AntragZeile : ObservableObject
{
    private readonly AntraegeViewModel _bereich;

    internal AntragZeile(GeraetEintrag g, AntraegeViewModel bereich, Nachschlag nachschlag, DateTimeOffset jetzt)
    {
        _bereich = bereich;
        GeraetId = g.Id;
        Kennung = g.Anzeige;
        ScName = g.ScName;
        DiscordName = g.DiscordName;
        Benutzername = g.Benutzername is "-" or "?" ? null : g.Benutzername;
        Geraetename = g.Name;
        IstAntrag = g.Antrag == true;
        Einspruch = g.Markierung == Markierung.Einspruch;
        // Ein wartendes Gerät an einem bestehenden Konto (per Einmalcode verknüpft oder nach langer Ruhe wieder wartend, 3.5)
        // sieht sonst aus wie ein neuer Antrag mit denselben Namen. Freigeben gibt ihm aber das Konto samt Rollen; ein
        // gekapertes Discord-Konto übernähme so ein Funkkonto (ANMELDUNG-SICHERHEIT, Regeln für Admins, zweites Gerät).
        WeiteresGeraet = IstAntrag
            ? (Einspruch ? "EINSPRUCH von einem freigegebenen Gerät: nicht freigeben." : null)
            : "Kein neuer Antrag: weiteres Gerät für das bestehende Konto " + g.KontoName + ". Freigeben gibt diesem Gerät das Konto samt Rollen. "
              + (g.Markierung switch
              {
                  Markierung.Einspruch => "EINSPRUCH von einem freigegebenen Gerät des Kontos: nicht freigeben. Mit dem Mitglied klären, im Zweifel ablehnen.",
                  Markierung.Bestaetigt => "Auf einem freigegebenen Gerät des Kontos bestätigt.",
                  _ => "Noch nicht auf einem freigegebenen Gerät des Kontos bestätigt: nur freigeben, wenn das Mitglied es im Sprachkanal bestätigt.",
              });
        Schluesselart = (g.SchluesselArt == SchluesselArt.Tpm ? "TPM" : "Software") + " (laut Client, ungeprüft)";
        var seit = DateTimeOffset.FromUnixTimeMilliseconds(g.Verknuepft).ToLocalTime();
        Zeit = (seit.Date == jetzt.Date ? "heute " : seit.ToString("dd.MM. ", CultureInfo.CurrentCulture)) + seit.ToString("HH:mm", CultureInfo.CurrentCulture) + " Uhr";
        var vergeben = new List<string>(2);
        if (g.ScNamenVergeben is { Count: > 0 } v)
            vergeben.Add(string.Join(", ", v) + " ist schon vergeben; freigegeben wird ohne diesen Namen, und der Name des Kontos kann einen Zusatz wie (2) bekommen.");
        // A53: Einen belegten Discord-Namen bekommt das neue Konto nicht, sonst gäbe es sich als das andere aus. Bis 0.6.x scheiterte
        // die Freigabe daran; seit 0.7.0 (ein PC, ein Konto) geht sie ohne ihn, wie bei den SC-Namen.
        if (g.DiscordNameVergeben == true)
            vergeben.Add("Den Discord-Namen " + g.DiscordName + " trägt schon ein Konto oder eine Vormerkung; freigegeben wird ohne ihn. "
                + "Ist es derselbe Mensch mit einem weiteren PC, passt das so.");
        Vergeben = vergeben.Count > 0 ? string.Join(" ", vergeben) : null;
        Vorschlaege = [new VorschlagWahl(null, "keine Vormerkung übernehmen", this)];
        foreach (var vm in g.Vormerkungen ?? [])
        {
            var text = "Vormerkung " + vm.ScName + (vm.DiscordName is { } d ? " (Discord " + d + ")" : "")
                + (vm.Rollen.Count > 0 ? ": " + nachschlag.Rollen(vm.Rollen) : "")
                + (vm.UeberScName ? ", gleicher SC-Name" : ", gleicher Discord-Name");
            Vorschlaege.Add(new VorschlagWahl(vm.Id, text, this));
        }
        // Genau ein Treffer über den SC-Namen ist vorgewählt (RAIDLEITUNG-ENTWURF, Mitglieder); sonst keiner.
        var ueberSc = g.Vormerkungen?.Where(v => v.UeberScName).ToList() ?? [];
        var vorwahl = ueberSc.Count == 1 && g.Vormerkungen!.Count == 1 ? ueberSc[0].Id : (uint?)null;
        foreach (var w in Vorschlaege) w.Gewaehlt = w.Id == vorwahl;
        // Bestehende Konten mit passenden Namen nur als Hinweis (Sarevocs Fall vom 01.10.2026). Seit 0.7.0 hängt der Verwaltungsteil
        // nichts mehr an sie: Ein weiterer PC bekommt sein eigenes Konto (ein PC, ein Konto, Sarevoc 04.10.2026). Den PC eines Admins mit
        // neuem Geräteschlüssel hängt weiter nur die Konsole an sein Konto, sonst verlöre er die Admin-Rechte.
        var passend = IstAntrag
            ? nachschlag.Mitglieder.Where(m => m.KontoId is not null && m.Zustand != MitgliedZustand.Antrag && m.KontoId != g.KontoId
                && Funk.Protokoll.Identitaet.Kontovorschlag.Passt([ScName, DiscordName], [m.Name, m.Benutzername, m.DiscordName, .. m.ScNamen])).ToList()
            : [];
        BestehendesKonto = passend.Count == 0 ? null : string.Join(" ", passend.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).Select(m => m.Stufe == Stufe.Admin
            ? "Heißt ähnlich wie das Konto " + Text(m) + ", Stufe ADMIN. Ist es dessen PC mit neuem Geräteschlüssel (etwa nach einer Neuinstallation) "
              + "und zeigt er dieselbe Kennung wie hier, hängt ihn nur die Konsole an: funk-admin freigeben " + Kennung + " --an-konto "
              + m.KontoId!.Value.ToString(CultureInfo.InvariantCulture) + ". Freigeben hier machte ein eigenes Konto als FUNKER daraus."
            : "Heißt ähnlich wie das Konto " + Text(m) + ". Ist es derselbe Mensch mit einem weiteren PC, gib frei: Jeder PC bekommt sein eigenes "
              + "Konto. Ist es jemand anderes, der sich nur so nennt, nicht freigeben."));
    }

    public uint GeraetId { get; }

    /// <summary>Gerätekennung (Anzeigeform des Fingerabdrucks); die Person liest sie auf ihrem Bildschirm ab und nennt sie dem Admin.</summary>
    public string Kennung { get; }

    /// <summary>Über der Kennung: woher die Vergleichszahl kommt.</summary>
    public string KennungHinweis => "Lass dir von " + (ScName ?? DiscordName ?? "der Person")
        + " die Gerätekennung durchsagen oder schicken, die auf dem neuen Gerät steht, und vergleiche sie mit dieser.";

    public string? ScName { get; }

    public string? DiscordName { get; }

    public string? Benutzername { get; }

    public string Geraetename { get; }

    public bool IstAntrag { get; }

    /// <summary>Ein freigegebenes Gerät desselben Kontos hat EINSPRUCH markiert (3.5): Freigeben geht hier nicht.</summary>
    public bool Einspruch { get; }

    /// <summary>Warnung, wenn das Gerät kein neuer Antrag ist, mit der Markierung des Kontos; sonst <c>null</c>.</summary>
    public string? WeiteresGeraet { get; }

    public string Schluesselart { get; }

    /// <summary>
    /// Bestehende Konten mit passenden Namen (Sarevocs Fall vom 01.10.2026), nur als Hinweis: Seit 0.7.0 bekommt jeder PC sein eigenes
    /// Konto, auch ein weiterer PC desselben Mitglieds.
    /// </summary>
    public string? BestehendesKonto { get; }

    public string Zeit { get; }

    public string? Vergeben { get; }

    public string Anzeigename => ScName ?? DiscordName ?? Kennung;

    public ObservableCollection<VorschlagWahl> Vorschlaege { get; }

    public bool HatVorschlaege => Vorschlaege.Count > 1;

    public uint? VormerkungId => Vorschlaege.FirstOrDefault(v => v.Gewaehlt)?.Id;

    /// <summary>
    /// Der Admin hat die Kennung mit der verglichen, die ihm die Person nennt, und "Verifiziert" geklickt; ein zweiter Klick
    /// nimmt es zurück. Erst dann geht Freigeben (seit 0.6.3).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerifiziertText), nameof(ZeigtAusrufe), nameof(FreigabeGesperrt))]
    [NotifyCanExecuteChangedFor(nameof(FreigebenCommand))]
    public partial bool Verifiziert { get; private set; }

    /// <summary>Was der Knopf tut: erst "Verifiziert", danach "Verifiziert zurücknehmen".</summary>
    public string VerifiziertText => Verifiziert ? "Verifiziert zurücknehmen" : "Verifiziert";

    /// <summary>Das gelbe "!" (Stil Ausrufe, selbst gezeichnet, keine Schrift) steht am Knopf, solange nicht verifiziert ist.</summary>
    public bool ZeigtAusrufe => !Verifiziert;

    /// <summary>Warum Freigeben gerade nicht geht, sichtbar unter den Knöpfen; <c>null</c>, wenn es geht.</summary>
    public string? FreigabeGesperrt => Einspruch
        ? "Freigeben geht nicht: Ein freigegebenes Gerät des Kontos hat EINSPRUCH eingelegt. Mit dem Mitglied klären, im Zweifel ablehnen."
        : Verifiziert ? null : "Freigeben wird aktiv, sobald du die Kennung verglichen und auf Verifiziert geklickt hast.";

    [ObservableProperty]
    public partial string? Fehler { get; internal set; }

    private static string Text(MitgliedEintrag m) => m.Name + (m.DiscordName is { } dn ? " (" + dn + ")" : "");

    [RelayCommand]
    private void Verifizieren() => Verifiziert = !Verifiziert;

    /// <summary>Freigeben erst nach "Verifiziert" und nie gegen einen EINSPRUCH.</summary>
    private bool KannFreigeben() => Verifiziert && !Einspruch;

    [RelayCommand(CanExecute = nameof(KannFreigeben))]
    private Task Freigeben() => _bereich.EntscheidenAsync(this, GeraetEntscheidung.Freigeben);

    /// <summary>"Ablehnen und löschen" nach der Rückfrage (<see cref="AblehnenFragen"/>).</summary>
    [RelayCommand]
    private Task Ablehnen()
    {
        AblehnenFragen = false;
        return _bereich.EntscheidenAsync(this, GeraetEntscheidung.Ablehnen);
    }

    /// <summary>
    /// Die Rückfrage vor dem Ablehnen steht offen (seit 0.7.0). Ablehnen löscht den Antrag; gesperrt wird nichts. Bis 0.6.x sperrte
    /// es die Gerätekennung auf Dauer, und am 04.10.2026 war so ein Testgerät ausgesperrt (Sarevoc: keine Fehlbedienung darf aussperren).
    /// Die Rückfrage bleibt, weil der Knopf neben "Freigeben" steht und ein frischer Code 5 Minuten lang nicht mehr gefragt wird.
    /// </summary>
    [ObservableProperty]
    public partial bool AblehnenFragen { get; private set; }

    public string AblehnenFrage => "Antrag von " + Anzeigename + " ablehnen? Der Server löscht ihn vollständig, mit Name, Kennung und "
        + "Schlüssel dieses PCs. Gesperrt wird nichts: Der PC kann danach einen neuen Antrag stellen.";

    [RelayCommand]
    private void AblehnenFragenOeffnen() => AblehnenFragen = true;

    [RelayCommand]
    private void AblehnenAbbrechen() => AblehnenFragen = false;

    internal void Waehlen(VorschlagWahl wahl)
    {
        foreach (var w in Vorschlaege) w.Gewaehlt = ReferenceEquals(w, wahl);
    }
}

/// <summary>Eine Wahl unter den Vorschlägen: keine Vormerkung oder eine bestimmte.</summary>
public sealed partial class VorschlagWahl(uint? id, string text, AntragZeile zeile) : ObservableObject
{
    public uint? Id { get; } = id;

    public string Text { get; } = text;

    [ObservableProperty]
    public partial bool Gewaehlt { get; set; }

    partial void OnGewaehltChanged(bool value)
    {
        if (value) zeile.Waehlen(this);
    }
}
