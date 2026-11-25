// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung.Modelle;

/// <summary>
/// Bereich Mitglieder (A46, 6.13): Konten und Vormerkungen in einer Liste mit Suche über alle Namen; ein Klick öffnet die
/// Karte zum Bearbeiten (Mitgliederrollen, Notiz nur für Admins, bei Vormerkungen auch die Namen). Dazu "Mitglied vormerken"
/// und "Liste einfügen" (eine Zeile je Mitglied: <c>SC-Name; Discord-Name; Kürzel; Rolle, Rolle</c>; die dritte Spalte wird seit
/// 0.11.8 übergangen, sie bleibt leer: <c>SC;Discord;;Rollen</c>).
/// Nichts davon verleiht Rechte; Stufe und Admin vergibt der Admin am Konto bzw. nur an der Konsole.
/// </summary>
public sealed partial class MitgliederViewModel : ObservableObject
{
    private readonly IVerwaltungsdienst _dienst;
    private readonly Nachschlag _nachschlag;
    private readonly Func<Task> _neuLaden;

    /// <param name="neuLaden">Lädt Mitglieder und Katalog neu (nach einer Änderung).</param>
    public MitgliederViewModel(IVerwaltungsdienst dienst, Nachschlag nachschlag, Func<Task> neuLaden)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(nachschlag);
        ArgumentNullException.ThrowIfNull(neuLaden);
        _dienst = dienst;
        _nachschlag = nachschlag;
        _neuLaden = neuLaden;
    }

    public ObservableCollection<MitgliedZeile> Sichtbar { get; } = [];

    /// <summary>Suche über SC-Namen, Discord-Name und Namen, auch Teile ("max" findet "Mustermann/Max").</summary>
    [ObservableProperty]
    public partial string Suche { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatKarte))]
    public partial MitgliedKarte? Karte { get; private set; }

    public bool HatKarte => Karte is not null;

    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    [ObservableProperty]
    public partial string Zaehlung { get; private set; } = "";

    // ---- Liste einfügen ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EinfuegenVorschau))]
    public partial string EinfuegenText { get; set; } = "";

    /// <summary>Wie viele Zeilen hineingehen, bevor sie der Server prüft.</summary>
    public string EinfuegenVorschau
    {
        get
        {
            var n = Zeilen(EinfuegenText).Count;
            return n == 0 ? "Eine Zeile je Mitglied: SC-Name; Discord-Name; ; Rolle, Rolle. Nur der SC-Name ist Pflicht, die dritte Spalte bleibt leer."
                : n == 1 ? "1 Zeile. Einfügen legt alles oder nichts an." : n.ToString(CultureInfo.CurrentCulture) + " Zeilen. Einfügen legt alles oder nichts an.";
        }
    }

    public ObservableCollection<string> EinfuegenFehler { get; } = [];

    private List<MitgliedZeile> _alle = [];

    /// <summary>
    /// Ein Konto, dessen Karte aufgehen soll, sobald die Liste kommt (Sprung aus "Wer ist da", als die Liste noch fehlte oder das Konto nicht
    /// kannte); sonst <c>null</c>. Es gilt genau für das nächste <see cref="Uebernehmen"/>, gefunden oder nicht: Bis zur Gegenprüfung 0.11.5 blieb
    /// es sonst stehen und öffnete bei einem viel späteren Laden (etwa nach "Mitglied geändert") seine Karte über einer anderen, die gerade
    /// bearbeitet wurde.
    /// </summary>
    private uint? _zeigen;

    /// <summary>Ein Sprung wartet auf die nächste Liste (<see cref="KontoZeigen"/> fand das Konto nicht).</summary>
    public bool SprungWartet => _zeigen is not null;

    /// <summary>Übernimmt die geladene Liste (aus dem <see cref="Nachschlag"/>) und filtert.</summary>
    public void Uebernehmen()
    {
        _alle = [.. _nachschlag.Mitglieder.Select(m => new MitgliedZeile(m, _nachschlag))
            .OrderBy(z => z.Sortierung).ThenBy(z => z.Name, StringComparer.CurrentCultureIgnoreCase)];
        var konten = _alle.Count(z => z.Eintrag.KontoId is not null && z.Eintrag.Zustand != MitgliedZustand.Antrag);
        var vorgemerkt = _alle.Count(z => z.Eintrag.VormerkungId is not null);
        Zaehlung = konten.ToString(CultureInfo.CurrentCulture) + " Konten, " + vorgemerkt.ToString(CultureInfo.CurrentCulture) + " vorgemerkt";
        Filtern();
        // Wartet ein Sprung aus "Wer ist da" auf die Liste, öffnet er jetzt die Karte; ein einziges Mal, auch wenn das Konto fehlt.
        var zeigen = _zeigen;
        _zeigen = null;
        if (zeigen is { } konto && KontoOeffnen(konto)) return;
        // Eine offene Karte zeigt den neuen Stand, sofern es das Mitglied noch gibt.
        if (Karte is { Neu: false } k)
        {
            Karte = _alle.FirstOrDefault(z => z.Eintrag.KontoId == k.KontoId && z.Eintrag.VormerkungId == k.VormerkungId) is { } z2 ? new MitgliedKarte(z2.Eintrag, _nachschlag, this) : null;
            if (Karte is { ZeigtGeraete: true } neu) _ = GeraeteLadenAsync(neu);
        }
    }

    /// <summary>
    /// Sprung aus "Wer ist da" (seit 0.11.5, Status einer Zeile im Admin-Setup): die Suche leer und die Karte dieses Kontos offen, wo der Admin
    /// Rollen und Angaben vergibt. Steht das Konto nicht in der Liste (noch keine geladen, weil die Admin-Sitzung zu ist, oder eine ältere als das
    /// Konto), wartet der Sprung auf die nächste (<see cref="SprungWartet"/>).
    /// </summary>
    public void KontoZeigen(uint kontoId)
    {
        Suche = "";
        _zeigen = KontoOeffnen(kontoId) ? null : kontoId;
    }

    /// <summary>Ein wartender Sprung gilt nicht mehr (der Admin hat die Verwaltung verlassen, ohne sich anzumelden).</summary>
    public void SprungVergessen() => _zeigen = null;

    private bool KontoOeffnen(uint kontoId)
    {
        if (_alle.FirstOrDefault(z => z.Eintrag.KontoId == kontoId && z.Eintrag.Zustand != MitgliedZustand.Antrag) is not { } zeile) return false;
        Oeffnen(zeile);
        return true;
    }

    partial void OnSucheChanged(string value) => Filtern();

    private void Filtern()
    {
        var s = Suche.Trim();
        Sichtbar.Clear();
        foreach (var z in _alle.Where(z => s.Length == 0 || z.Passt(s))) Sichtbar.Add(z);
    }

    [RelayCommand]
    private void Oeffnen(MitgliedZeile? zeile)
    {
        Fehler = null;
        Karte = zeile is null ? null : new MitgliedKarte(zeile.Eintrag, _nachschlag, this);
        if (Karte is { ZeigtGeraete: true } k) _ = GeraeteLadenAsync(k);
    }

    /// <summary>Lädt die Geräte des Kontos in die Karte (seit 0.8.0); wirft nicht, ein Fehler steht dann an der Karte.</summary>
    internal async Task GeraeteLadenAsync(MitgliedKarte k)
    {
        if (k.KontoId is not { } konto || !k.ZeigtGeraete) return;
        k.GeraeteLaden();
        var a = await _dienst.GeraeteAsync(konto).ConfigureAwait(true);
        k.GeraeteUebernehmen(a, _dienst.Lage.SperreAufhebenBekannt);
    }

    /// <summary>
    /// "Sperre aufheben" an einem Gerät der Karte (seit 0.8.0) nach der Rückfrage; den frischen Code fragt der Dienst, wenn der
    /// Server ihn will. Danach gilt das Gerät als entfernt, und der PC kann neu beantragen.
    /// </summary>
    internal async Task SperreAufhebenAsync(MitgliedKarte k, GeraetZeile g)
    {
        if (!g.KannAufheben) return;
        k.Fehler = null;
        var e = await _dienst.SperreAufhebenAsync(g.Id).ConfigureAwait(true);
        if (e is not null)
        {
            k.Fehler = e;
            return;
        }
        Hinweis = "Sperre von " + g.Name + " (" + g.Kennung + ") aufgehoben. Der PC kann einen neuen Antrag stellen; freigeben musst du ihn dann wieder.";
        await GeraeteLadenAsync(k).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Vormerken()
    {
        Fehler = null;
        Karte = MitgliedKarte.NeueVormerkung(_nachschlag, this);
    }

    [RelayCommand]
    private void Schliessen() => Karte = null;

    [RelayCommand]
    private async Task Einfuegen()
    {
        var zeilen = Zeilen(EinfuegenText);
        if (zeilen.Count == 0) return;
        EinfuegenFehler.Clear();
        var a = await _dienst.EinfuegenAsync(zeilen).ConfigureAwait(true);
        if (!a.Ok)
        {
            Fehler = a.Fehler;
            return;
        }
        var falsch = a.Wert!.Zeilen;
        if (falsch.Count > 0)
        {
            foreach (var z in falsch) EinfuegenFehler.Add("Zeile " + z.Zeile.ToString(CultureInfo.CurrentCulture) + ": " + z.Grund);
            Fehler = "Nichts angelegt: " + (falsch.Count == 1 ? "1 Zeile passt" : falsch.Count.ToString(CultureInfo.CurrentCulture) + " Zeilen passen")
                + " nicht (unten). Korrigieren und noch einmal.";
            return;
        }
        // Seit 0.11.8: Steht etwas in Spalte 3 (Untergilde), sagt der Server es einmal je Liste; angelegt ist trotzdem alles.
        Hinweis = zeilen.Count.ToString(CultureInfo.CurrentCulture) + " Mitglieder vorgemerkt." + (a.Wert.Hinweis is { } h ? " " + h + "." : "");
        EinfuegenText = "";
        await _neuLaden().ConfigureAwait(true);
    }

    /// <summary>Der Server kennt den Haken "darf Meldungen senden" (ab 0.12.0); sonst steht er nicht auf der Karte.</summary>
    internal bool MeldungenBekannt => _dienst.Lage.MeldungenBekannt;

    internal static List<string> Zeilen(string text) =>
        [.. (text ?? "").Split('\n').Select(z => z.Trim().TrimEnd('\r').Trim()).Where(z => z.Length > 0)];

    internal async Task SpeichernAsync(MitgliedKarte k)
    {
        k.Fehler = null;
        var rollen = k.Rollen.Where(r => r.Gewaehlt).Select(r => r.Wert).ToList();
        string? e;
        if (k.Neu)
        {
            if (Namensregeln.ScName(k.ScName) is not { } sc)
            {
                k.Fehler = "Der SC-Name braucht 2 bis 32 Zeichen: Buchstaben, Ziffern, Leerzeichen, _ - und Punkt.";
                return;
            }
            if (k.DiscordName.Trim().Length > 0 && Namensregeln.DiscordName(k.DiscordName) is null)
            {
                k.Fehler = "Der Discord-Name braucht 2 bis 32 Zeichen, ohne unsichtbare Zeichen.";
                return;
            }
            e = await _dienst.VormerkenAsync(sc, k.DiscordName.Trim(), rollen, k.Notiz.Trim()).ConfigureAwait(true);
        }
        else if (k.KontoId is { } konto)
        {
            // Seit 0.12.0 zuerst der Haken "darf Meldungen senden" (M15, nur in der Admin-Sitzung, mit Journal); scheitert er, geht nichts weiter.
            if (k.MeldungenGeaendert)
            {
                e = await _dienst.MeldungenSendenSetzenAsync(konto, k.MeldungenSenden).ConfigureAwait(true);
                if (e is not null)
                {
                    k.Fehler = e;
                    return;
                }
            }
            // Erst der Discord-Name (A53, nur Admins, eindeutig): Ist er vergeben, geht auch der Rest nicht halb hinaus.
            if (k.DiscordGeaendert)
            {
                string? neu = null;
                if (k.DiscordName.Trim().Length > 0 && (neu = Namensregeln.DiscordName(k.DiscordName)) is null)
                {
                    k.Fehler = "Der Discord-Name braucht 2 bis 32 Zeichen, ohne unsichtbare Zeichen.";
                    return;
                }
                e = await _dienst.DiscordNameSetzenAsync(konto, neu).ConfigureAwait(true);
                if (e is not null)
                {
                    k.Fehler = e;
                    return;
                }
            }
            e = await _dienst.KontoAendernAsync(konto, rollen, k.Notiz.Trim()).ConfigureAwait(true);
        }
        else
        {
            e = await _dienst.VormerkungAendernAsync(k.VormerkungId!.Value, k.ScName.Trim(), k.DiscordName.Trim(), rollen, k.Notiz.Trim()).ConfigureAwait(true);
        }
        if (e is not null)
        {
            k.Fehler = e;
            return;
        }
        Hinweis = (k.Neu ? "Vorgemerkt: " : "Gespeichert: ") + k.ScName.Trim();
        Karte = null;
        await _neuLaden().ConfigureAwait(true);
    }

    internal async Task LoeschenAsync(MitgliedKarte k)
    {
        if (k.VormerkungId is not { } id) return;
        var e = await _dienst.VormerkungLoeschenAsync(id).ConfigureAwait(true);
        if (e is not null)
        {
            k.Fehler = e;
            return;
        }
        Hinweis = "Vormerkung " + k.ScName + " gelöscht.";
        Karte = null;
        await _neuLaden().ConfigureAwait(true);
    }

    /// <summary>"Endgültig löschen" auf der Karte eines Kontos (seit 0.6.3): Der Dienst fragt den frischen Code, wenn der Server ihn will.</summary>
    internal async Task KontoLoeschenAsync(MitgliedKarte k)
    {
        if (k.KontoId is not { } konto || !k.KannLoeschen) return;
        k.Fehler = null;
        var e = await _dienst.KontoLoeschenAsync(konto).ConfigureAwait(true);
        if (e is not null)
        {
            k.Fehler = e;
            return;
        }
        Hinweis = "Konto " + k.Name + " gelöscht. Name, Kennung und Schlüssel sind fort; wer wieder dabei sein will, stellt einen neuen Antrag.";
        Karte = null;
        await _neuLaden().ConfigureAwait(true);
    }
}

/// <summary>Eine Zeile der Mitgliederliste.</summary>
public sealed class MitgliedZeile
{
    internal MitgliedZeile(MitgliedEintrag e, Nachschlag n)
    {
        Eintrag = e;
        Name = e.ScNamen.Count > 0 ? e.ScNamen[0] : e.Name;
        Weitere = e.ScNamen.Count > 1 ? string.Join(", ", e.ScNamen.Skip(1)) : null;
        Rollen = n.Rollen(e.Rollen);
        Wuensche = e.Lieblingsrollen is { Count: > 0 } l ? "wünscht: " + n.Rollen(l) : null;
        Zustand = e.Zustand switch
        {
            MitgliedZustand.Online => "online",
            // Der Server meldet FREIGEGEBEN für jedes Konto, das nicht online, gesperrt oder ein Antrag ist, auch ohne Gerät. Seit
            // 0.7.0 steht hier deshalb "offline": Am 04.10.2026 stand ein Konto ohne Gerät als "freigegeben" da (Sarevocs Test).
            MitgliedZustand.Freigegeben => "offline",
            MitgliedZustand.Antrag => "Antrag",
            MitgliedZustand.Vorgemerkt => "vorgemerkt",
            _ => "gesperrt",
        };
        Stufe = e.Stufe switch
        {
            Protokoll.Steuerung.Stufe.Admin => e.Eigentuemer == true ? "Admin, Eigentümer" : "Admin",
            Protokoll.Steuerung.Stufe.Raidleiter => "Führung",
            Protokoll.Steuerung.Stufe.Funker => "Funker",
            _ => null,
        };
        Sortierung = e.Zustand switch
        {
            MitgliedZustand.Online => 0,
            MitgliedZustand.Freigegeben => 1,
            MitgliedZustand.Vorgemerkt => 2,
            MitgliedZustand.Antrag => 3,
            _ => 4,
        };
    }

    public MitgliedEintrag Eintrag { get; }

    public string Name { get; }

    public string? Weitere { get; }

    public string? DiscordName => Eintrag.DiscordName;

    public string Rollen { get; }

    public string? Wuensche { get; }

    public string Zustand { get; }

    public string? Stufe { get; }

    internal int Sortierung { get; }

    public bool Passt(string suche) =>
        Eintrag.ScNamen.Any(n => n.Contains(suche, StringComparison.CurrentCultureIgnoreCase))
        || Eintrag.Name.Contains(suche, StringComparison.CurrentCultureIgnoreCase)
        || Eintrag.DiscordName?.Contains(suche, StringComparison.CurrentCultureIgnoreCase) == true
        || Eintrag.Benutzername?.Contains(suche, StringComparison.CurrentCultureIgnoreCase) == true;
}

/// <summary>Karte eines Mitglieds zum Bearbeiten; bei einer neuen Vormerkung leer.</summary>
public sealed partial class MitgliedKarte : ObservableObject
{
    private readonly MitgliederViewModel _bereich;

    internal MitgliedKarte(MitgliedEintrag e, Nachschlag n, MitgliederViewModel bereich)
    {
        _bereich = bereich;
        KontoId = e.KontoId;
        VormerkungId = e.VormerkungId;
        Titel = e.VormerkungId is not null ? "Vormerkung " + (e.ScNamen.Count > 0 ? e.ScNamen[0] : e.Name) : "Konto " + e.Name;
        Name = e.ScNamen.Count > 0 ? e.ScNamen[0] : e.Name;
        // Konto löschen (seit 0.6.3): im Client nur FUNKER und RAIDLEITER, kein offener Antrag (den lehnt man unter Anträge ab).
        // ADMIN-Konten und den Eigentümer löscht nur die Konsole (3.6, 3.10); der Server lehnt es sonst ab.
        var privilegiert = e.Stufe == Protokoll.Steuerung.Stufe.Admin || e.Eigentuemer == true;
        _privilegiert = privilegiert;
        KannLoeschen = e.KontoId is not null && e.Zustand != MitgliedZustand.Antrag && !privilegiert;
        // Geräte (seit 0.8.0) zeigt jede Karte eines Kontos; ein offener Antrag hat sein Gerät unter Anträge.
        ZeigtGeraete = e.KontoId is not null && e.Zustand != MitgliedZustand.Antrag;
        LoeschenNurKonsole = e.KontoId is not null && privilegiert
            ? "Admin-Konten löscht nur die Serverkonsole (funk-admin konto-loeschen), nicht der Client."
            : null;
        ScName = e.ScNamen.Count > 0 ? e.ScNamen[0] : "";
        DiscordName = e.DiscordName ?? "";
        _discordVorher = DiscordName;
        // Ein offener Antrag hat noch kein Konto im Namensraum; seinen Discord-Namen prüft erst die Freigabe (A53).
        DiscordAenderbar = e.KontoId is not null && e.Zustand != MitgliedZustand.Antrag;
        Notiz = e.Notiz ?? "";
        Rollen = n.RollenHaken(e.Rollen);
        Wuensche = e.Lieblingsrollen is { Count: > 0 } l ? n.Rollen(l) + (e.ErsteWahl is { } w ? " (erste Wahl: " + n.Rolle(w) + ")" : "") : null;
        Angaben = e.KontoId is not null
            ? string.Join("  ", new[] { e.DiscordName is null ? null : "Discord " + e.DiscordName, e.Benutzername is null ? null : "Benutzername " + e.Benutzername }.Where(x => x is not null))
            : null;
        // Meldesystem (0.12.0, M15-M18): der Haken an jedem Konto (kein offener Antrag), nur an einem Server, der ihn kennt.
        ZeigtMeldungen = e.KontoId is not null && e.Zustand != MitgliedZustand.Antrag && bereich.MeldungenBekannt;
        _meldungenVorher = e.MeldungenSenden == true;
        MeldungenSenden = _meldungenVorher;
    }

    private MitgliedKarte(Nachschlag n, MitgliederViewModel bereich)
    {
        _bereich = bereich;
        Neu = true;
        Titel = "Mitglied vormerken";
        Rollen = n.RollenHaken(null);
    }

    internal static MitgliedKarte NeueVormerkung(Nachschlag n, MitgliederViewModel bereich) => new(n, bereich);

    public bool Neu { get; }

    /// <summary>Hauptname (erster SC-Name), sonst der Kontoname; für Rückfrage und Hinweis beim Löschen.</summary>
    public string Name { get; } = "";

    public uint? KontoId { get; }

    public uint? VormerkungId { get; }

    public string Titel { get; }

    private readonly string _discordVorher = "";

    /// <summary>
    /// Namen sind bei Vormerkungen änderbar; die SC-Namen eines Kontos ändert das Mitglied selbst (oder die Konsole), den
    /// Discord-Namen eines Kontos nur ein Admin (<see cref="DiscordAenderbar"/>, A53).
    /// </summary>
    public bool NamenAenderbar => KontoId is null;

    /// <summary>Der Discord-Name eines Kontos (kein offener Antrag): eindeutig, nur ein Admin ändert ihn (A53); das Mitglied kann es nicht.</summary>
    public bool DiscordAenderbar { get; }

    /// <summary>Der Admin hat den Discord-Namen des Kontos geändert (Leerzeichen am Rand zählen nicht).</summary>
    internal bool DiscordGeaendert => DiscordAenderbar && !string.Equals(DiscordName.Trim(), _discordVorher.Trim(), StringComparison.Ordinal);

    public bool IstVormerkung => VormerkungId is not null;

    public string? Angaben { get; }

    // ---- Meldungen senden (seit 0.12.0, M15-M18) ----

    /// <summary>Hovertext am Haken.</summary>
    public const string MeldungenHilfe = "Darf Meldungen senden: Erlaubt diesem Mitglied, Befehle, Zustände, Ampel, Countdown und Bereit-Abfragen als Grafik an alle "
        + "zu schicken, die online sind (Feld Meldungen auf der Funkfeld-Zuteilung). Wirkt nur bei Führung, Admin oder Einsatzleitung mit laufendem Auftrag; "
        + "bei einem Funker bleibt der Haken stehen, er kann aber nichts senden, und \"Heute als Funker\" sendet nie. Er gilt, bis ein Admin ihn wegnimmt. "
        + "Gespeichert wird mit Speichern, im Journal steht, wer ihn gesetzt hat.";

    /// <summary>Der Satz unter dem Haken.</summary>
    public const string MeldungenSatz = "Wirkt nur bei Führung, Admin oder Einsatzleitung mit Auftrag.";

    private readonly bool _meldungenVorher;

    /// <summary>Der Haken steht auf dieser Karte: ein Konto (kein offener Antrag), Server ab 0.12.0.</summary>
    public bool ZeigtMeldungen { get; }

    /// <summary>"darf Meldungen senden"; gespeichert wird mit Speichern.</summary>
    [ObservableProperty]
    public partial bool MeldungenSenden { get; set; }

    /// <summary>Der Admin hat den Haken geändert.</summary>
    internal bool MeldungenGeaendert => ZeigtMeldungen && MeldungenSenden != _meldungenVorher;

    [ObservableProperty]
    public partial string ScName { get; set; } = "";

    [ObservableProperty]
    public partial string DiscordName { get; set; } = "";

    [ObservableProperty]
    public partial string Notiz { get; set; } = "";

    public ObservableCollection<Haken<ushort>> Rollen { get; }

    /// <summary>Es gibt noch keinen Rollenkatalog, also nichts abzuhaken.</summary>
    public bool OhneKatalog => Rollen.Count == 0;

    public string? Wuensche { get; }

    [ObservableProperty]
    public partial string? Fehler { get; internal set; }

    [RelayCommand]
    private Task Speichern() => _bereich.SpeichernAsync(this);

    [RelayCommand]
    private Task Loeschen() => _bereich.LoeschenAsync(this);

    // ---- Konto löschen (seit 0.6.3) ----

    /// <summary>Das Konto lässt sich hier löschen: Funker oder Führung, kein offener Antrag.</summary>
    public bool KannLoeschen { get; }

    /// <summary>Statt des Knopfs bei ADMIN-Konten und dem Eigentümer: wo es geht.</summary>
    public string? LoeschenNurKonsole { get; }

    /// <summary>Die Rückfrage steht offen; erst "Endgültig löschen" schickt es an den Server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZeigtLoeschenKnopf))]
    public partial bool LoeschenFragen { get; private set; }

    public bool ZeigtLoeschenKnopf => KannLoeschen && !LoeschenFragen;

    public string LoeschenFrage => "Konto " + Name + " endgültig löschen? Der Server entfernt alles: Konto, Namen, die Geräte mit Kennung und "
        + "Schlüssel, Plätze und Mitgliedsangaben; eine laufende Verbindung endet sofort. Gesperrt wird nichts: Wer wieder dabei sein will, "
        + "stellt einen neuen Antrag. Rückgängig machen lässt es sich nicht.";

    [RelayCommand]
    private void KontoLoeschenFragen()
    {
        if (KannLoeschen) LoeschenFragen = true;
    }

    [RelayCommand]
    private void KontoLoeschenAbbrechen() => LoeschenFragen = false;

    [RelayCommand]
    private async Task KontoLoeschen()
    {
        await _bereich.KontoLoeschenAsync(this).ConfigureAwait(true);
        LoeschenFragen = false;
    }

    // ---- Geräte (seit 0.8.0) ----

    private readonly bool _privilegiert;

    /// <summary>Die Karte gehört zu einem Konto (kein offener Antrag, keine Vormerkung) und zeigt dessen Geräte.</summary>
    public bool ZeigtGeraete { get; }

    /// <summary>Name, Kennung und Zustand je Gerät; gesperrte mit "Sperre aufheben".</summary>
    public ObservableCollection<GeraetZeile> Geraete { get; } = [];

    /// <summary>"Geräte werden geladen ...", "Keine Geräte." oder warum die Liste fehlt; <c>null</c>, wenn sie steht.</summary>
    [ObservableProperty]
    public partial string? GeraeteHinweis { get; private set; }

    internal void GeraeteLaden()
    {
        Geraete.Clear();
        GeraeteHinweis = "Geräte werden geladen ...";
    }

    internal void GeraeteUebernehmen(Verwaltungsantwort<IReadOnlyList<GeraetEintrag>> a, bool aufhebenBekannt)
    {
        Geraete.Clear();
        if (!a.Ok || a.Wert is not { } liste)
        {
            GeraeteHinweis = a.Fehler ?? "Die Geräte ließen sich nicht laden.";
            return;
        }
        foreach (var g in liste) Geraete.Add(new GeraetZeile(g, this, aufhebenBekannt, _privilegiert));
        GeraeteHinweis = Geraete.Count == 0 ? "Keine Geräte." : null;
    }

    internal Task SperreAufhebenAsync(GeraetZeile g) => _bereich.SperreAufhebenAsync(this, g);
}

/// <summary>
/// Ein Gerät auf der Karte eines Kontos (seit 0.8.0): Name, Kennung und Zustand. Ein gesperrtes bekommt "Sperre aufheben ..." mit
/// Rückfrage; erst die Rückfrage schickt es an den Server, und der fragt nach einem frischen Code. Bei Admin-Konten und dem
/// Eigentümer hebt nur die Konsole eine Sperre auf, wie sie auch nur dort gesperrt werden (3.6).
/// </summary>
public sealed partial class GeraetZeile : ObservableObject
{
    private readonly MitgliedKarte _karte;

    internal GeraetZeile(GeraetEintrag e, MitgliedKarte karte, bool aufhebenBekannt, bool privilegiert)
    {
        _karte = karte;
        Id = e.Id;
        Name = e.Name;
        Kennung = e.Anzeige;
        Gesperrt = e.Status is GeraeteStatus.Entzogen or GeraeteStatus.Abgelehnt;
        Zustand = e.Status switch
        {
            GeraeteStatus.Wartet => "wartet auf Freigabe",
            GeraeteStatus.Freigegeben => "freigegeben",
            GeraeteStatus.Entfernt => "entfernt",
            GeraeteStatus.Beantragt => "Entfernen beantragt",
            _ => "gesperrt",
        };
        KannAufheben = Gesperrt && aufhebenBekannt && !privilegiert;
        NurKonsole = !Gesperrt ? null
            : !aufhebenBekannt ? Verwaltungstexte.SperreAufhebenZuAlt
            : privilegiert ? "Gerätesperren an Admin-Konten hebt nur die Serverkonsole auf: funk-admin sperre-aufheben " + Kennung
            : null;
    }

    public uint Id { get; }

    public string Name { get; }

    /// <summary>Die Gerätekennung, wie der PC sie auf seiner Startseite zeigt.</summary>
    public string Kennung { get; }

    public string Zustand { get; }

    public bool Gesperrt { get; }

    /// <summary>Hier lässt sich die Sperre aufheben: gesperrt, Server ab 0.8.0, kein Admin-Konto.</summary>
    public bool KannAufheben { get; }

    /// <summary>Statt des Knopfs: warum es hier nicht geht und wo doch.</summary>
    public string? NurKonsole { get; }

    /// <summary>Die Rückfrage steht offen; erst "Sperre aufheben" darin schickt es an den Server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZeigtAufhebenKnopf))]
    public partial bool AufhebenFragen { get; private set; }

    public bool ZeigtAufhebenKnopf => KannAufheben && !AufhebenFragen;

    public string AufhebenFrage => "Sperre von " + Name + " (" + Kennung + ") aufheben? Das Gerät gilt danach als entfernt: Der PC kann einen "
        + "neuen Antrag stellen, und ein Admin entscheidet wieder über die Freigabe. Von selbst kommt er nicht in den Funk.";

    [RelayCommand]
    private void SperreAufhebenFragen()
    {
        if (KannAufheben) AufhebenFragen = true;
    }

    [RelayCommand]
    private void SperreAufhebenAbbrechen() => AufhebenFragen = false;

    [RelayCommand]
    private async Task SperreAufheben()
    {
        await _karte.SperreAufhebenAsync(this).ConfigureAwait(true);
        AufhebenFragen = false;
    }
}
