// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// "Ich" unter Einstellungen: eigene Namen (SC-Namen, Discord-Name, N2), die eigenen Rollen (A46: vom Admin eingetragen mit
/// Schloss, dazu bis zu 8 Lieblingsrollen, eine als erste Wahl), die Zeichen (A49: "Mein Zeichen zeigen", dazu die Schalter
/// des Betrachters) und "Meine Angaben in Wer ist da zeigen" (Sarevoc, 30.09.2026, Antwort 3). Führung und Einsatzleitung können
/// den Rollenkatalog ergänzen (Antwort 4). Der Abschnitt Rollen fehlt, solange es keinen Katalog gibt. Seit 0.10.0 (E14, Bild m-4) die Karte
/// "Ich" oben in den Einstellungen, offen: Namen und Wünsche mit einem Knopf <see cref="SpeichernCommand"/>; Zeichen und Sichtbarkeit stehen
/// darunter unter Erweitert. Seit 0.11.7 dort auch "Meine Abzeichen" (Sarevoc: "ein Abzeichen haben ist eine Sache, es zeigen eine eigene
/// Entscheidung"): je eigenes Abzeichen die Wahl "vorn" und der Haken "zeigen".
/// </summary>
public sealed partial class MeinProfilViewModel : ObservableObject
{
    public const int MaxWuensche = 8;

    private readonly IFunkDienst _dienst;
    private readonly Einstellungsablage _ablage;
    private readonly IOberflaechentakt _takt;
    private RollenkatalogAntwort? _katalog;
    private string? _rollenStand;
    private uint? _konto;
    private bool _schalterSetzen;

    public MeinProfilViewModel(IFunkDienst dienst, Einstellungsablage ablage, IOberflaechentakt takt)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(takt);
        _dienst = dienst;
        _ablage = ablage;
        _takt = takt;
        Funkermodus = new FunkermodusViewModel(dienst, ablage, takt);
        _dienst.LageGeaendert += () => _takt.Einreihen(Aktualisieren);
        Aktualisieren();
    }

    /// <summary>"Heute als Funker" (6.15): nur für Admins an Servern ab 0.5.0; auch im Menü des Symbols und in der Fußzeile.</summary>
    public FunkermodusViewModel Funkermodus { get; }

    /// <summary>Verbunden mit freigegebenem Konto: Namen, Rollen und Schalter gehen an den Server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NichtAktiv))]
    [NotifyCanExecuteChangedFor(nameof(NamenSpeichernCommand), nameof(WuenscheSpeichernCommand), nameof(RolleErgaenzenCommand), nameof(SpeichernCommand))]
    public partial bool Aktiv { get; private set; }

    public bool NichtAktiv => !Aktiv;

    // ---- Namen ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NamenSpeichernCommand))]
    public partial string ScName { get; set; } = "";

    /// <summary>Bis zu zwei weitere SC-Namen (Zweitkonten im Spiel), durch Komma getrennt.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NamenSpeichernCommand))]
    public partial string WeitereScNamen { get; set; } = "";

    /// <summary>
    /// Der Discord-Name des Kontos, nur zur Anzeige: Ändern darf ihn nur ein Admin (A53, Schutz vor Täuschung in "Wer ist da"),
    /// und der Server nimmt ihn aus <c>ProfilSetzen</c> nicht mehr an.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscordNameAnzeige))]
    public partial string DiscordName { get; private set; } = "";

    /// <summary>Was unter "Mein Discord-Name" steht.</summary>
    public string DiscordNameAnzeige => DiscordName.Length > 0 ? DiscordName : "noch keiner";

    /// <summary>Kurz unter dem Discord-Namen (seit 0.10.0, Bild m-4); der ganze Hinweis (<see cref="DiscordNameHinweis"/>) steht im Hilfetext.</summary>
    public const string DiscordNameKurz = "Den ändert nur ein Admin.";

    /// <summary>Der ganze Hinweis zum Discord-Namen.</summary>
    public const string DiscordNameHinweis =
        "Den Discord-Namen gibt ein Admin frei, und nur ein Admin ändert ihn. Er ist eindeutig, damit sich niemand als jemand anderes ausgeben kann. Stimmt er nicht, sag es einem Admin.";

    [ObservableProperty]
    public partial string? NamenHinweis { get; private set; }

    [ObservableProperty]
    public partial string? NamenFehler { get; private set; }

    // ---- Zeichen und Freigabe ----

    /// <summary>"Mein Zeichen zeigen" (A49), am Konto gespeichert.</summary>
    [ObservableProperty]
    public partial bool ZeichenZeigen { get; set; } = true;

    /// <summary>"Meine Angaben in Wer ist da zeigen" (Antwort 3), am Konto gespeichert, ab Werk an.</summary>
    [ObservableProperty]
    public partial bool KarteFreigeben { get; set; } = true;

    /// <summary>
    /// "Meine Abzeichen" (seit 0.11.7): ADMIN und EINSATZLEITUNG, wenn man sie gerade hat, dann die Ränge und Tätigkeiten, die ein Admin vergeben
    /// hat; je mit Bild, Titel, der Wahl "vorn" und dem Haken "zeigen". FUEHRUNG steht nicht darin: Sie wird nicht mehr gezeichnet.
    /// </summary>
    public ObservableCollection<MeinAbzeichen> MeineAbzeichen { get; } = [];

    /// <summary>Es gibt eigene Abzeichen; sonst steht <see cref="OhneAbzeichenText"/> da.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OhneAbzeichen))]
    public partial bool HatAbzeichen { get; private set; }

    public bool OhneAbzeichen => !HatAbzeichen;

    /// <summary>Der Satz ohne eigene Abzeichen.</summary>
    public const string OhneAbzeichenText = "Du hast noch keine Abzeichen. Ränge und Tätigkeiten vergeben die Admins.";

    /// <summary>Hilfetext an "vorn".</summary>
    public const string VornHilfe = "Zeigt dieses Abzeichen vorn, vor deinem Namen in Wer ist da und auf der Funkfeld-Zuteilung. Es gibt eine Wahl; ein "
        + "zweiter Klick nimmt sie zurück, dann steht dein erstes sichtbares Abzeichen vorn. Steht an deinem Konto.";

    /// <summary>Hilfetext an "zeigen".</summary>
    public const string ZeigenHilfe = "Zeigt dieses Abzeichen den anderen. Ohne Haken sehen sie es nicht; deine Rechte ändert das nie. Steht an deinem Konto.";

    /// <summary>Die Kennungen der Liste, damit sie nur bei einer Änderung neu gebaut wird (Fokus und Klick bleiben).</summary>
    private string? _abzeichenStand;

    /// <summary>Zeichen neben Namen sehen (Betrachter, nur hier gespeichert).</summary>
    public bool ZeichenNebenNamen
    {
        get => _ablage.Aktuell.Anzeige.ZeichenNebenNamen;
        set
        {
            if (value == _ablage.Aktuell.Anzeige.ZeichenNebenNamen) return;
            _ablage.Aktuell.Anzeige.ZeichenNebenNamen = value;
            _ablage.Vormerken();
            OnPropertyChanged();
            AnzeigeGeaendert?.Invoke();
        }
    }

    /// <summary>Zeichen auch im Funkfeld (A49: ab Werk aus, damit das Feld kompakt bleibt).</summary>
    public bool ZeichenImFunkfeld
    {
        get => _ablage.Aktuell.Anzeige.ZeichenImFunkfeld;
        set
        {
            if (value == _ablage.Aktuell.Anzeige.ZeichenImFunkfeld) return;
            _ablage.Aktuell.Anzeige.ZeichenImFunkfeld = value;
            _ablage.Vormerken();
            OnPropertyChanged();
            AnzeigeGeaendert?.Invoke();
        }
    }

    /// <summary>Ein Schalter der Anzeige hat sich geändert (Wer ist da und Funkfeld zeichnen neu).</summary>
    public event Action? AnzeigeGeaendert;

    [ObservableProperty]
    public partial string? SchalterFehler { get; private set; }

    // ---- Rollen ----

    /// <summary>Vom Admin eingetragen (Schloss); abwählen kann man sie nicht.</summary>
    public ObservableCollection<string> VomAdmin { get; } = [];

    public bool HatVomAdmin => VomAdmin.Count > 0;

    /// <summary>Der Katalog zum Abhaken; leer, solange der Admin keinen angelegt hat.</summary>
    public ObservableCollection<RollenWunsch> Wuensche { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OhneKatalog))]
    public partial bool HatKatalog { get; private set; }

    public bool OhneKatalog => !HatKatalog;

    [ObservableProperty]
    public partial string? RollenHinweis { get; private set; }

    [ObservableProperty]
    public partial string? RollenFehler { get; private set; }

    /// <summary>Führung und Einsatzleitung dürfen den Katalog ergänzen (Antwort 4).</summary>
    [ObservableProperty]
    public partial bool KannErgaenzen { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RolleErgaenzenCommand))]
    public partial string NeueRolle { get; set; } = "";

    [ObservableProperty]
    public partial string NeueGruppe { get; set; } = "";

    /// <summary>Katalog und eigene Angaben neu holen (beim Zeigen des Bereichs Einstellungen).</summary>
    [RelayCommand]
    public async Task LadenAsync()
    {
        if (!_dienst.Lage.IstAktiv) return;
        var k = await _dienst.RollenkatalogAsync().ConfigureAwait(true);
        if (!k.Ok)
        {
            RollenFehler = k.Fehler;
            return;
        }
        _katalog = k.Wert;
        RollenFehler = null;
        // Derselbe Katalog beim erneuten Zeigen der Einstellungen: Die Häkchen bleiben stehen.
        if (!string.Equals(RollenStand(_dienst.Lage.Konto, _katalog), _rollenStand, StringComparison.Ordinal)) RollenAufbauen();
    }

    private bool NamenGueltig() => Aktiv && Namensregeln.ScName(ScName) is not null;

    [RelayCommand(CanExecute = nameof(NamenGueltig))]
    private async Task NamenSpeichern()
    {
        NamenFehler = null;
        NamenHinweis = null;
        var weitere = WeitereScNamen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (weitere.Length > 2)
        {
            NamenFehler = "Höchstens zwei weitere SC-Namen.";
            return;
        }
        var namen = new List<string> { Namensregeln.ScName(ScName)! };
        foreach (var w in weitere)
        {
            if (Namensregeln.ScName(w) is not { } sc)
            {
                NamenFehler = "\"" + w + "\" ist kein gültiger SC-Name (2 bis 32 Zeichen: Buchstaben, Ziffern, Leerzeichen, _ - und Punkt).";
                return;
            }
            namen.Add(sc);
        }
        // Nur die SC-Namen: Den Discord-Namen setzt der Admin (A53).
        var e = await _dienst.ProfilSetzenAsync(new ProfilSetzenAnfrage { ScNamen = namen }).ConfigureAwait(true);
        if (e is not null)
        {
            NamenFehler = e;
            return;
        }
        NamenHinweis = "Gespeichert.";
    }

    // ---- Speichern (seit 0.10.0, E14): ein Knopf für Namen und Wünsche ----

    /// <summary>Hovertext an "Speichern" in der Karte Ich.</summary>
    public const string SpeichernHilfe = "Speichern: Schreibt deine SC-Namen und deine Rollenwünsche an dein Konto, jeweils nur, was du geändert hast. "
        + "Mit diesen Namen findet dich die Einsatzleitung, und sie sieht deine Wünsche. Den Discord-Namen ändert nur ein Admin. Geht nur, solange du "
        + "verbunden und freigegeben bist; vor der Freigabe stehen die Namen auf Start.";

    /// <summary>Gesperrt-Grund von "Speichern".</summary>
    public const string SpeichernGesperrt = "Speichern geht, sobald du verbunden und freigegeben bist.";

    /// <summary>Neben "Speichern": was geschah (Gespeichert, Nichts geändert); Fehler stehen getrennt darunter.</summary>
    [ObservableProperty]
    public partial string? SpeichernHinweis { get; private set; }

    /// <summary>
    /// "Speichern" in der Karte Ich (seit 0.10.0, E14; bis 0.9.3 je ein Knopf für Namen und Wünsche): Namen nur, wenn sie vom Konto abweichen, Wünsche
    /// nur, wenn sie von den gespeicherten abweichen und es einen Katalog gibt. Ein Fehler bei einem hält das andere nicht auf; die Fehler stehen getrennt.
    /// </summary>
    [RelayCommand(CanExecute = nameof(Aktiv))]
    private async Task Speichern()
    {
        SpeichernHinweis = null;
        var k = _dienst.Lage.Konto;
        var namen = k is null || NamenGeaendert(k);
        var wuensche = HatKatalog && WuenscheGeaendert(k);
        if (!namen && !wuensche)
        {
            SpeichernHinweis = "Nichts geändert.";
            return;
        }
        if (namen && Namensregeln.ScName(ScName) is null)
        {
            NamenFehler = "\"" + ScName.Trim() + "\" ist kein gültiger SC-Name (2 bis 32 Zeichen: Buchstaben, Ziffern, Leerzeichen, _ - und Punkt).";
            NamenHinweis = null;
        }
        else if (namen)
        {
            await NamenSpeichern().ConfigureAwait(true);
        }
        if (wuensche) await WuenscheSpeichern().ConfigureAwait(true);
        var gelungen = (!namen || NamenFehler is null) && (!wuensche || RollenFehler is null);
        SpeichernHinweis = gelungen ? "Gespeichert." : null;
        NamenHinweis = null;
        RollenHinweis = null;
    }

    /// <summary>Weichen die Häkchen oder die erste Wahl von dem ab, was am Konto steht?</summary>
    private bool WuenscheGeaendert(KontoInfo? k)
    {
        if (k is null) return true;
        var gewaehlt = Wuensche.Where(w => w.Gewaehlt && !w.VomAdmin).Select(w => w.Id).Order().ToList();
        var erste = Wuensche.FirstOrDefault(w => w.ErsteWahl && (w.Gewaehlt || w.VomAdmin))?.Id;
        return !gewaehlt.SequenceEqual(k.Lieblingsrollen.Where(id => !k.Rollen.Contains(id)).Order()) || erste != k.ErsteWahl;
    }

    partial void OnZeichenZeigenChanged(bool value)
    {
        if (!_schalterSetzen) _ = SchalterSendenAsync(new ProfilSetzenAnfrage { ZeichenZeigen = value });
    }

    partial void OnKarteFreigebenChanged(bool value)
    {
        if (!_schalterSetzen) _ = SchalterSendenAsync(new ProfilSetzenAnfrage { KarteFreigeben = value });
    }

    private async Task SchalterSendenAsync(ProfilSetzenAnfrage anfrage)
    {
        SchalterFehler = null;
        if (!Aktiv)
        {
            SchalterFehler = "Das gilt erst mit Verbindung; es steht am Konto, nicht auf diesem PC.";
            Aktualisieren();
            return;
        }
        var e = await _dienst.ProfilSetzenAsync(anfrage).ConfigureAwait(true);
        if (e is not null) SchalterFehler = e;
        Aktualisieren();
    }

    /// <summary>
    /// "vorn" an einem eigenen Abzeichen (0.11.7): eine Wahl; an heißt dieses vorn (die anderen gehen aus), aus heißt keine Wahl (""). Geht wie die
    /// Schalter darüber gleich an den Server; was der Server zurückmeldet, setzt die Liste ohne neues Senden.
    /// </summary>
    internal void VornGeaendert(MeinAbzeichen abzeichen, bool an)
    {
        if (_schalterSetzen) return;
        _schalterSetzen = true;
        try
        {
            if (an)
                foreach (var a in MeineAbzeichen) if (!ReferenceEquals(a, abzeichen)) a.Vorn = false;
        }
        finally
        {
            _schalterSetzen = false;
        }
        _ = SchalterSendenAsync(new ProfilSetzenAnfrage { AbzeichenVorn = an ? abzeichen.Kennung : "" });
    }

    /// <summary>
    /// "zeigen" an einem eigenen Abzeichen (0.11.7): schickt die ganze Liste der ausgeblendeten; was am Konto ausgeblendet steht, aber gerade nicht
    /// in der Liste ist (etwa EINSATZLEITUNG nach dem Abend), bleibt darin.
    /// </summary>
    internal void ZeigenGeaendert(MeinAbzeichen abzeichen, bool an)
    {
        if (_schalterSetzen) return;
        var bisher = _dienst.Lage.Konto?.AbzeichenAus ?? [];
        IReadOnlyList<string> aus = [.. bisher.Where(k => MeineAbzeichen.All(a => a.Kennung != k))
            .Concat(MeineAbzeichen.Where(a => !a.Zeigen).Select(a => a.Kennung)).Distinct(StringComparer.Ordinal)];
        _ = SchalterSendenAsync(new ProfilSetzenAnfrage { AbzeichenAus = aus });
    }

    /// <summary>Baut "Meine Abzeichen" nur neu, wenn sich die Kennungen ändern; Wahl und Haken kommen immer aus dem Konto (ohne Senden).</summary>
    private void AbzeichenAktualisieren(KontoInfo k)
    {
        var kennungen = Abzeichenkatalog.Ordnen(k.Zeichen.Where(Abzeichenkatalog.IstAutomatisch).Concat(k.Abzeichen.Where(Abzeichenkatalog.Kennt)));
        var stand = string.Join(",", kennungen);
        if (!string.Equals(stand, _abzeichenStand, StringComparison.Ordinal))
        {
            _abzeichenStand = stand;
            MeineAbzeichen.Clear();
            foreach (var kennung in kennungen)
                MeineAbzeichen.Add(new MeinAbzeichen(kennung, Zeichensatz.Bild(kennung), Zeichensatz.Titel(kennung), this));
        }
        HatAbzeichen = MeineAbzeichen.Count > 0;
        foreach (var a in MeineAbzeichen)
        {
            a.Vorn = string.Equals(k.AbzeichenVorn, a.Kennung, StringComparison.Ordinal);
            a.Zeigen = !k.AbzeichenAus.Contains(a.Kennung, StringComparer.Ordinal);
        }
    }

    private bool KannWuenscheSpeichern() => Aktiv && HatKatalog;

    [RelayCommand(CanExecute = nameof(KannWuenscheSpeichern))]
    private async Task WuenscheSpeichern()
    {
        RollenFehler = null;
        RollenHinweis = null;
        var gewaehlt = Wuensche.Where(w => w.Gewaehlt && !w.VomAdmin).Select(w => w.Id).ToList();
        if (gewaehlt.Count > MaxWuensche)
        {
            RollenFehler = "Höchstens " + MaxWuensche.ToString(CultureInfo.CurrentCulture) + " Lieblingsrollen.";
            return;
        }
        var erste = Wuensche.FirstOrDefault(w => w.ErsteWahl && (w.Gewaehlt || w.VomAdmin))?.Id;
        var e = await _dienst.LieblingsrollenSetzenAsync(gewaehlt, erste).ConfigureAwait(true);
        if (e is not null)
        {
            RollenFehler = e;
            return;
        }
        RollenHinweis = "Gespeichert.";
    }

    private bool KannRolleErgaenzen() => Aktiv && KannErgaenzen && NeueRolle.Trim().Length is > 0 and <= 40;

    /// <summary>Führung und Einsatzleitung: eine Rolle zum Katalog (Antwort 4). Vorhandene bleiben unverändert.</summary>
    [RelayCommand(CanExecute = nameof(KannRolleErgaenzen))]
    private async Task RolleErgaenzen()
    {
        RollenFehler = null;
        RollenHinweis = null;
        var k = await _dienst.RollenkatalogAsync().ConfigureAwait(true);
        if (!k.Ok)
        {
            RollenFehler = k.Fehler;
            return;
        }
        var katalog = k.Wert!;
        var name = NeueRolle.Trim();
        var gruppe = NeueGruppe.Trim();
        if (katalog.Eintraege.Any(e => string.Equals(Namensregeln.Vergleichsschluessel(e.Name), Namensregeln.Vergleichsschluessel(name), StringComparison.Ordinal)))
        {
            RollenFehler = "Die Rolle " + name + " gibt es schon.";
            return;
        }
        if (katalog.Eintraege.Count >= 64)
        {
            RollenFehler = "Der Katalog ist voll (64 Rollen).";
            return;
        }
        var id = (ushort)(katalog.Eintraege.Count == 0 ? 1 : katalog.Eintraege.Max(e => e.Id) + 1);
        var neu = new Rollenkatalogeintrag { Id = id, Name = name, Gruppe = gruppe.Length == 0 ? null : gruppe };
        var e = await _dienst.RollenkatalogErgaenzenAsync([.. katalog.Eintraege, neu], katalog.Version).ConfigureAwait(true);
        if (e is not null)
        {
            RollenFehler = e;
            return;
        }
        NeueRolle = "";
        NeueGruppe = "";
        RollenHinweis = "Rolle " + name + " steht jetzt im Katalog.";
        await LadenAsync().ConfigureAwait(true);
    }

    /// <summary>Stand aus der Lage: Namen und Schalter, sobald ein anderes Konto da ist oder der Server neue Angaben schickt.</summary>
    internal void Aktualisieren()
    {
        var l = _dienst.Lage;
        Aktiv = l.IstAktiv && l.Konto is not null;
        if (l.Konto is not { } k) return;
        var neuesKonto = _konto != k.Id;
        _konto = k.Id;
        if (neuesKonto || !NamenGeaendert(k))
        {
            ScName = k.ScNamen.Count > 0 ? k.ScNamen[0] : "";
            WeitereScNamen = string.Join(", ", k.ScNamen.Skip(1));
        }
        // Nur angezeigt, also immer der Stand des Servers.
        DiscordName = k.DiscordName ?? "";
        // Die Lage kommt mit jedem Push neu, auch mit jeder Sprechanzeige. Neu gebaut wird nur, was sich geändert hat: Sonst
        // löschte jeder Funkspruch die Häkchen, die du gerade setzt und noch nicht gespeichert hast (Gegenprüfung Stufe 1).
        _schalterSetzen = true;
        try
        {
            ZeichenZeigen = k.ZeichenZeigen;
            KarteFreigeben = k.KarteFreigeben;
            AbzeichenAktualisieren(k);
        }
        finally
        {
            _schalterSetzen = false;
        }
        KannErgaenzen = k.Fuehrung;
        if (!string.Equals(RollenStand(k, _katalog), _rollenStand, StringComparison.Ordinal)) RollenAufbauen();
    }

    /// <summary>Was die Rollenliste zeigt, als ein Text: Konto, Rollen vom Admin, Wünsche, erste Wahl und Katalog.</summary>
    private static string RollenStand(KontoInfo? k, RollenkatalogAntwort? katalog)
    {
        static string Liste(IEnumerable<ushort> ids) => string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        return k is null
            ? "-"
            : k.Id.ToString(CultureInfo.InvariantCulture) + "|" + Liste(k.Rollen) + "|" + Liste(k.Lieblingsrollen) + "|"
              + (k.ErsteWahl?.ToString(CultureInfo.InvariantCulture) ?? "") + "|"
              + (katalog is null ? "-" : string.Join("\n", katalog.Eintraege.Select(e => e.Id.ToString(CultureInfo.InvariantCulture) + "\t" + e.Name + "\t" + e.Gruppe)));
    }

    /// <summary>Ob die Felder vom Stand des Kontos abweichen (dann nicht überschreiben, du tippst gerade).</summary>
    private bool NamenGeaendert(KontoInfo k) =>
        !string.Equals(ScName.Trim(), k.ScNamen.Count > 0 ? k.ScNamen[0] : "", StringComparison.Ordinal)
        || !string.Equals(WeitereScNamen.Trim(), string.Join(", ", k.ScNamen.Skip(1)), StringComparison.Ordinal);

    private void RollenAufbauen()
    {
        var k = _dienst.Lage.Konto;
        var katalog = _katalog;
        _rollenStand = RollenStand(k, katalog);
        HatKatalog = katalog is { Eintraege.Count: > 0 };
        string Name(ushort id) => katalog?.Eintraege.FirstOrDefault(e => e.Id == id)?.Name ?? "Rolle " + id.ToString(CultureInfo.CurrentCulture);
        VomAdmin.Clear();
        foreach (var r in k?.Rollen ?? []) VomAdmin.Add(Name(r));
        OnPropertyChanged(nameof(HatVomAdmin));
        Wuensche.Clear();
        if (katalog is null || k is null) return;
        foreach (var e in katalog.Eintraege)
        {
            var vomAdmin = k.Rollen.Contains(e.Id);
            Wuensche.Add(new RollenWunsch(e.Id, e.Name, e.Gruppe, vomAdmin, vomAdmin || k.Lieblingsrollen.Contains(e.Id), k.ErsteWahl == e.Id, this));
        }
    }

    /// <summary>Genau eine erste Wahl.</summary>
    internal void ErsteWahlSetzen(RollenWunsch wunsch)
    {
        foreach (var w in Wuensche) if (!ReferenceEquals(w, wunsch)) w.ErsteWahl = false;
    }
}

/// <summary>
/// Ein eigenes Abzeichen unter "Meine Abzeichen" (0.11.7): Bild, Titel ("Marine: Sergeant", "Gunner M", "Admin"), die Wahl "vorn" (eine für alle,
/// auch keine) und der Haken "zeigen". Jede Änderung geht sofort an den Server (<see cref="MeinProfilViewModel.VornGeaendert"/>).
/// </summary>
public sealed partial class MeinAbzeichen : ObservableObject
{
    private readonly MeinProfilViewModel _bereich;

    internal MeinAbzeichen(string kennung, System.Windows.Media.ImageSource? bild, string titel, MeinProfilViewModel bereich)
    {
        _bereich = bereich;
        Kennung = kennung;
        Bild = bild;
        Titel = titel;
    }

    public string Kennung { get; }

    public System.Windows.Media.ImageSource? Bild { get; }

    public string Titel { get; }

    /// <summary>Für Vorleser: "Marine: Sergeant vorn zeigen".</summary>
    public string VornAnsage => Titel + " vorn zeigen";

    /// <summary>Für Vorleser: "Marine: Sergeant anderen zeigen".</summary>
    public string ZeigenAnsage => Titel + " anderen zeigen";

    [ObservableProperty]
    public partial bool Vorn { get; set; }

    [ObservableProperty]
    public partial bool Zeigen { get; set; } = true;

    partial void OnVornChanged(bool value) => _bereich.VornGeaendert(this, value);

    partial void OnZeigenChanged(bool value) => _bereich.ZeigenGeaendert(this, value);
}

/// <summary>Eine Rolle aus dem Katalog zum Abhaken; vom Admin eingetragene stehen fest.</summary>
public sealed partial class RollenWunsch : ObservableObject
{
    private readonly MeinProfilViewModel _bereich;

    internal RollenWunsch(ushort id, string name, string? gruppe, bool vomAdmin, bool gewaehlt, bool ersteWahl, MeinProfilViewModel bereich)
    {
        _bereich = bereich;
        Id = id;
        Name = name;
        Gruppe = gruppe;
        VomAdmin = vomAdmin;
        Gewaehlt = gewaehlt;
        ErsteWahl = ersteWahl;
    }

    public ushort Id { get; }

    public string Name { get; }

    public string? Gruppe { get; }

    /// <summary>Vom Admin eingetragen: fest, mit Schloss.</summary>
    public bool VomAdmin { get; }

    public bool Waehlbar => !VomAdmin;

    public string Text => Name + (Gruppe is null ? "" : " (" + Gruppe + ")") + (VomAdmin ? ", vom Admin eingetragen" : "");

    /// <summary>Hovertext am Umschalter einer Rolle (seit dem Befund der Prüfung ein Umschalter wie in m-4).</summary>
    public const string WunschHilfe = "Wünscht dir diese Rolle; die Einsatzleitung sieht deine Wünsche beim Einteilen. Ein zweiter Klick nimmt den Wunsch "
        + "zurück. Neben jeder gewählten Rolle steht ein Stern: Damit markierst du deine erste Wahl. Gespeichert wird mit Speichern.";

    /// <summary>Sperrgrund am Umschalter einer Rolle, die ein Admin eingetragen hat.</summary>
    public const string VomAdminGrund = "Diese Rolle hat dir ein Admin eingetragen (Schloss); abwählen kann sie nur er. Zur ersten Wahl machen kannst du sie mit dem Stern daneben.";

    /// <summary>Hovertext am Stern neben einer gewählten Rolle.</summary>
    public const string ErsteWahlHilfe = "Markiert diese Rolle als deine erste Wahl; die Einsatzleitung sieht sie mit Stern. Es gibt genau eine erste Wahl: "
        + "Ein Stern an einer anderen Rolle nimmt ihn hier weg.";

    /// <summary>Hovertext am Umschalter dieser Rolle: Name mit Gruppe (auf dem Umschalter steht nur der Name), dann <see cref="WunschHilfe"/>.</summary>
    public string Hilfe => Text + ". " + WunschHilfe;

    /// <summary>Der Stern zur ersten Wahl steht nur an gewählten und vom Admin eingetragenen Rollen.</summary>
    public bool SternZeigen => Gewaehlt || VomAdmin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SternZeigen))]
    public partial bool Gewaehlt { get; set; }

    [ObservableProperty]
    public partial bool ErsteWahl { get; set; }

    partial void OnErsteWahlChanged(bool value)
    {
        if (!value) return;
        if (!VomAdmin) Gewaehlt = true;
        _bereich.ErsteWahlSetzen(this);
    }

    partial void OnGewaehltChanged(bool value)
    {
        if (!value && ErsteWahl && !VomAdmin) ErsteWahl = false;
    }
}
