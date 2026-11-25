// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung.Modelle;

/// <summary>
/// Die Seite Abzeichen (0.11.7, Sarevoc 14./15.10.2026): eigener Eintrag ganz unten im Hauptmenü, nur in der Admin-Sitzung. Links die Mitglieder
/// (Konten, keine Vormerkungen und Anträge) mit Suche, rechts das gewählte: je Laufbahn eine Zeile mit Bild und Rang und den Knöpfen
/// "befördern", "zurückstufen", "entfernen" (ohne Rang "aufnehmen" = Stufe 1), darunter alle Tätigkeiten nach Gruppe zum Umschalten (höchstens
/// 6). Jede Änderung geht sofort als <c>Abzeichen</c> (0x0025) an den Server und ersetzt Ränge und Tätigkeiten des Kontos; ein Fehler steht
/// sichtbar an der Karte. Bilder und Namen kommen mit dem Programm (<see cref="Abzeichenkatalog"/>); umbenannt oder angelegt wird hier nichts.
/// Admins sehen hier alles, was ein Mitglied besitzt, auch was es ausgeblendet hat.
/// </summary>
public sealed partial class AbzeichenViewModel : ObservableObject
{
    private readonly IVerwaltungsdienst _dienst;
    private readonly Nachschlag _nachschlag;
    private List<AbzeichenMitglied> _alle = [];

    public AbzeichenViewModel(IVerwaltungsdienst dienst, Nachschlag nachschlag)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(nachschlag);
        _dienst = dienst;
        _nachschlag = nachschlag;
    }

    /// <summary>Die Mitglieder, die zur Suche passen.</summary>
    public ObservableCollection<AbzeichenMitglied> Sichtbar { get; } = [];

    /// <summary>Suche über SC-Namen, Discord-Name und Namen, auch Teile.</summary>
    [ObservableProperty]
    public partial string Suche { get; set; } = "";

    partial void OnSucheChanged(string value) => Filtern();

    [ObservableProperty]
    public partial string Zaehlung { get; private set; } = "";

    /// <summary>Das gewählte Mitglied; <c>null</c>, solange keins gewählt ist.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatKarte), nameof(OhneKarte))]
    public partial AbzeichenKarte? Karte { get; private set; }

    public bool HatKarte => Karte is not null;

    public bool OhneKarte => Karte is null;

    /// <summary>Übernimmt die Liste aus dem <see cref="Nachschlag"/> (nach jedem Laden der Verwaltung); eine offene Karte zeigt den neuen Stand.</summary>
    public void Uebernehmen()
    {
        _alle = [.. _nachschlag.Mitglieder
            .Where(m => m.KontoId is not null && m.Zustand != MitgliedZustand.Antrag)
            .Select(m => new AbzeichenMitglied(m, _nachschlag))
            .OrderBy(z => z.Name, StringComparer.CurrentCultureIgnoreCase)];
        Zaehlung = _alle.Count == 1 ? "1 Mitglied" : _alle.Count.ToString(CultureInfo.CurrentCulture) + " Mitglieder";
        Filtern();
        if (Karte is { } k)
        {
            // Hinweis und Fehler bleiben stehen: Nach jeder Änderung meldet der Server "Mitglied geändert", und die Verwaltung lädt neu.
            var neu = _alle.FirstOrDefault(z => z.KontoId == k.KontoId);
            Karte = neu is null ? null : new AbzeichenKarte(neu, this) { Hinweis = k.Hinweis, Fehler = k.Fehler };
        }
        Markieren();
    }

    private void Filtern()
    {
        var s = Suche.Trim();
        Sichtbar.Clear();
        foreach (var z in _alle.Where(z => s.Length == 0 || z.Passt(s))) Sichtbar.Add(z);
    }

    private void Markieren()
    {
        foreach (var z in _alle) z.Gewaehlt = Karte?.KontoId == z.KontoId;
    }

    /// <summary>Ein Mitglied links gewählt: rechts seine Karte.</summary>
    [RelayCommand]
    private void Waehlen(AbzeichenMitglied? mitglied)
    {
        Karte = mitglied is null ? null : new AbzeichenKarte(mitglied, this);
        Markieren();
    }

    /// <summary>
    /// Schickt Ränge und Tätigkeiten des Kontos an den Server (0x0025, ersetzt beide). Gelingt es, übernehmen Liste und Karte den gespeicherten
    /// Stand und <paramref name="erfolg"/> steht als Hinweis da; sonst steht der Fehler an der Karte, und sie zeigt wieder den Stand davor.
    /// </summary>
    internal async Task SetzenAsync(AbzeichenKarte karte, IReadOnlyList<string> raenge, IReadOnlyList<string> taetigkeiten, string erfolg)
    {
        karte.Fehler = null;
        karte.Hinweis = null;
        karte.Beschaeftigt = true;
        try
        {
            // In Katalogfolge, wie der Server speichert.
            var a = await _dienst.AbzeichenSetzenAsync(karte.KontoId, [.. Abzeichenkatalog.Ordnen(raenge)], [.. Abzeichenkatalog.Ordnen(taetigkeiten)]).ConfigureAwait(true);
            if (!a.Ok || a.Wert is not { } stand)
            {
                karte.Fehler = a.Fehler ?? "Der Server hat die Abzeichen nicht gespeichert.";
                karte.Zeigen(karte.Mitglied.Raenge, karte.Mitglied.Taetigkeiten);
                return;
            }
            karte.Mitglied.Setzen(stand.Raenge, stand.Taetigkeiten);
            karte.Zeigen(stand.Raenge, stand.Taetigkeiten);
            karte.Hinweis = erfolg;
        }
        finally
        {
            karte.Beschaeftigt = false;
        }
    }
}

/// <summary>Ein Mitglied in der Liste links: Name, Discord-Name und seine Abzeichen (alle, auch ausgeblendete).</summary>
public sealed partial class AbzeichenMitglied : ObservableObject
{
    internal AbzeichenMitglied(MitgliedEintrag e, Nachschlag n)
    {
        Eintrag = e;
        KontoId = e.KontoId!.Value;
        Name = e.ScNamen.Count > 0 ? e.ScNamen[0] : e.Name;
        Setzen(e.Raenge ?? [], e.Taetigkeiten ?? []);
    }

    public MitgliedEintrag Eintrag { get; }

    public uint KontoId { get; }

    public string Name { get; }

    public string? DiscordName => Eintrag.DiscordName;

    /// <summary>Die Ränge (einer je Laufbahn) in Laufbahnfolge.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Raenge { get; private set; } = [];

    /// <summary>Die Tätigkeiten in Katalogfolge.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Taetigkeiten { get; private set; } = [];

    /// <summary>Alle Kennungen, für die kleinen Bilder in der Zeile.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Alle { get; private set; } = [];

    /// <summary>"Marine: Sergeant, Gunner M" oder "keine Abzeichen", für Hilfetext und Vorleser.</summary>
    [ObservableProperty]
    public partial string Zusammenfassung { get; private set; } = "";

    /// <summary>Rechts steht seine Karte.</summary>
    [ObservableProperty]
    public partial bool Gewaehlt { get; internal set; }

    internal void Setzen(IReadOnlyList<string> raenge, IReadOnlyList<string> taetigkeiten)
    {
        Raenge = [.. Abzeichenkatalog.Ordnen(raenge)];
        Taetigkeiten = [.. Abzeichenkatalog.Ordnen(taetigkeiten)];
        Alle = [.. Raenge, .. Taetigkeiten];
        Zusammenfassung = Alle.Count == 0 ? "keine Abzeichen" : string.Join(", ", Alle.Select(k => Abzeichenkatalog.Titel(k) ?? k));
    }

    public bool Passt(string suche) =>
        Eintrag.ScNamen.Any(n => n.Contains(suche, StringComparison.CurrentCultureIgnoreCase))
        || Eintrag.Name.Contains(suche, StringComparison.CurrentCultureIgnoreCase)
        || Eintrag.DiscordName?.Contains(suche, StringComparison.CurrentCultureIgnoreCase) == true;
}

/// <summary>Das gewählte Mitglied rechts: je Laufbahn eine Zeile, darunter die Tätigkeiten nach Gruppe.</summary>
public sealed partial class AbzeichenKarte : ObservableObject
{
    private readonly AbzeichenViewModel _seite;

    internal AbzeichenKarte(AbzeichenMitglied mitglied, AbzeichenViewModel seite)
    {
        _seite = seite;
        Mitglied = mitglied;
        Laufbahnen = [.. Abzeichenkatalog.Laufbahnen.Select(l => new LaufbahnZeile(l, this))];
        Gruppen = [.. Abzeichenkatalog.Gruppen.Select(g => new TaetigkeitGruppe(g,
            [.. Abzeichenkatalog.Taetigkeiten.Where(t => t.Gruppe == g).Select(t => new TaetigkeitFeld(t, this))]))];
        Zeigen(mitglied.Raenge, mitglied.Taetigkeiten);
    }

    public AbzeichenMitglied Mitglied { get; }

    public uint KontoId => Mitglied.KontoId;

    public string Titel => "Abzeichen von " + Mitglied.Name;

    public IReadOnlyList<LaufbahnZeile> Laufbahnen { get; }

    public IReadOnlyList<TaetigkeitGruppe> Gruppen { get; }

    /// <summary>"Tätigkeiten (2 von höchstens 6)".</summary>
    [ObservableProperty]
    public partial string TaetigkeitenTitel { get; private set; } = "";

    [ObservableProperty]
    public partial string? Fehler { get; internal set; }

    [ObservableProperty]
    public partial string? Hinweis { get; internal set; }

    /// <summary>Eine Änderung ist unterwegs zum Server: Knöpfe und Umschalter warten.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Frei))]
    public partial bool Beschaeftigt { get; internal set; }

    public bool Frei => !Beschaeftigt;

    partial void OnBeschaeftigtChanged(bool value)
    {
        foreach (var l in Laufbahnen) l.Melden();
    }

    /// <summary>Der Stand, wie ihn die Karte zeigt (nach dem Server); Grundlage jeder Änderung.</summary>
    private IReadOnlyList<string> _raenge = [];
    private IReadOnlyList<string> _taetigkeiten = [];
    private bool _zeigt;

    internal int TaetigkeitenAnzahl => _taetigkeiten.Count;

    /// <summary>Setzt Zeilen und Umschalter auf einen Stand, ohne etwas zu senden.</summary>
    internal void Zeigen(IReadOnlyList<string> raenge, IReadOnlyList<string> taetigkeiten)
    {
        _raenge = [.. raenge];
        _taetigkeiten = [.. taetigkeiten];
        _zeigt = true;
        try
        {
            foreach (var l in Laufbahnen) l.Setzen(raenge.Select(Abzeichenkatalog.Rang).FirstOrDefault(r => r?.Laufbahn == l.Kennung));
            foreach (var g in Gruppen)
                foreach (var t in g.Eintraege) t.Vergeben = taetigkeiten.Contains(t.Kennung, StringComparer.Ordinal);
        }
        finally
        {
            _zeigt = false;
        }
        TaetigkeitenTitel = "Tätigkeiten (" + taetigkeiten.Count.ToString(CultureInfo.CurrentCulture) + " von höchstens "
            + Grenzen.TaetigkeitenMax.ToString(CultureInfo.CurrentCulture) + ")";
    }

    /// <summary>Der Rang einer Laufbahn wird ersetzt (<paramref name="neu"/>) oder entfernt (<c>null</c>).</summary>
    internal Task RangSetzenAsync(LaufbahnZeile zeile, AbzeichenRang? neu, string erfolg)
    {
        var raenge = _raenge.Where(k => Abzeichenkatalog.Rang(k)?.Laufbahn != zeile.Kennung).ToList();
        if (neu is not null) raenge.Add(neu.Kennung);
        return _seite.SetzenAsync(this, raenge, _taetigkeiten, erfolg);
    }

    /// <summary>
    /// Eine Tätigkeit vergeben oder wegnehmen. Bei 6 ist Schluss (Sarevoc): Der Umschalter springt zurück, und die Karte sagt warum; der Server wird
    /// gar nicht erst gefragt.
    /// </summary>
    internal void TaetigkeitGeaendert(TaetigkeitFeld feld, bool an)
    {
        if (_zeigt) return;
        if (Beschaeftigt)
        {
            Zeigen(_raenge, _taetigkeiten);
            return;
        }
        if (an && _taetigkeiten.Count >= Grenzen.TaetigkeitenMax)
        {
            Hinweis = null;
            Fehler = GrenzeText;
            Zeigen(_raenge, _taetigkeiten);
            return;
        }
        IReadOnlyList<string> neu = an ? [.. _taetigkeiten, feld.Kennung] : [.. _taetigkeiten.Where(k => k != feld.Kennung)];
        _ = _seite.SetzenAsync(this, _raenge, neu,
            Mitglied.Name + (an ? " hat jetzt die Tätigkeit " + feld.Name + "." : ": Tätigkeit " + feld.Name + " weggenommen."));
    }

    /// <summary>Die Meldung bei der siebten Tätigkeit.</summary>
    public static string GrenzeText { get; } = "Höchstens " + Grenzen.TaetigkeitenMax.ToString(CultureInfo.CurrentCulture)
        + " Tätigkeiten je Mitglied. Nimm erst eine weg, dann geht eine neue.";
}

/// <summary>Eine Laufbahn auf der Karte: Bild und Name des Rangs, dazu befördern, zurückstufen, entfernen oder aufnehmen.</summary>
public sealed partial class LaufbahnZeile : ObservableObject
{
    private readonly AbzeichenKarte _karte;
    private readonly AbzeichenLaufbahn _laufbahn;

    internal LaufbahnZeile(AbzeichenLaufbahn laufbahn, AbzeichenKarte karte)
    {
        _laufbahn = laufbahn;
        _karte = karte;
    }

    public string Kennung => _laufbahn.Kennung;

    public string Name => _laufbahn.Name;

    /// <summary>Der Rang in dieser Laufbahn, <c>null</c> ohne.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangKennung), nameof(RangText), nameof(HatRang), nameof(OhneRang), nameof(BefoerdernGesperrt), nameof(ZurueckstufenGesperrt),
        nameof(BefoerdernHilfe), nameof(ZurueckstufenHilfe), nameof(EntfernenHilfe), nameof(AufnehmenHilfe), nameof(Ansage))]
    public partial AbzeichenRang? Rang { get; private set; }

    /// <summary>Kennung des Rangs fürs Bild (<c>AbzeichenBild</c> der Anwendung), sonst <c>null</c>.</summary>
    public string? RangKennung => Rang?.Kennung;

    public string RangText => Rang is { } r ? r.Name + " (Stufe " + r.Stufe.ToString(CultureInfo.CurrentCulture) + " von "
        + _laufbahn.Raenge.Length.ToString(CultureInfo.CurrentCulture) + ")" : "kein Rang";

    public bool HatRang => Rang is not null;

    public bool OhneRang => Rang is null;

    /// <summary>Für Vorleser: "Marine: Sergeant" oder "Marine: kein Rang".</summary>
    public string Ansage => Name + ": " + (Rang?.Name ?? "kein Rang");

    private AbzeichenRang? Stufe(int stufe) => stufe >= 1 && stufe <= _laufbahn.Raenge.Length ? _laufbahn.Raenge[stufe - 1] : null;

    internal void Setzen(AbzeichenRang? rang) => Rang = rang;

    internal void Melden()
    {
        BefoerdernCommand.NotifyCanExecuteChanged();
        ZurueckstufenCommand.NotifyCanExecuteChanged();
        EntfernenCommand.NotifyCanExecuteChanged();
        AufnehmenCommand.NotifyCanExecuteChanged();
    }

    partial void OnRangChanged(AbzeichenRang? value) => Melden();

    private bool KannBefoerdern() => _karte.Frei && Rang is { } r && Stufe(r.Stufe + 1) is not null;

    private bool KannZurueckstufen() => _karte.Frei && Rang is { } r && Stufe(r.Stufe - 1) is not null;

    private bool KannEntfernen() => _karte.Frei && Rang is not null;

    private bool KannAufnehmen() => _karte.Frei && Rang is null;

    public string BefoerdernHilfe => Rang is { } r && Stufe(r.Stufe + 1) is { } hoeher
        ? "Befördert " + _karte.Mitglied.Name + " in der " + Name + " von " + r.Name + " zu " + hoeher.Name + ". Gilt sofort."
        : "Befördern: eine Stufe höher in der " + Name + ".";

    /// <summary>Warum "befördern" gesperrt ist: am Ende der Leiter.</summary>
    public string BefoerdernGesperrt => Rang is { } r ? r.Name + " ist der höchste Rang der " + Name + "; höher geht es nicht." : "Ohne Rang erst aufnehmen.";

    public string ZurueckstufenHilfe => Rang is { } r && Stufe(r.Stufe - 1) is { } tiefer
        ? "Stuft " + _karte.Mitglied.Name + " in der " + Name + " von " + r.Name + " auf " + tiefer.Name + " zurück. Gilt sofort."
        : "Zurückstufen: eine Stufe tiefer in der " + Name + ".";

    /// <summary>Warum "zurückstufen" gesperrt ist: am Anfang der Leiter.</summary>
    public string ZurueckstufenGesperrt => Rang is { } r
        ? r.Name + " ist der erste Rang der " + Name + "; tiefer geht es nicht. Entfernen nimmt den Rang ganz weg."
        : "Ohne Rang erst aufnehmen.";

    public string EntfernenHilfe => "Nimmt " + _karte.Mitglied.Name + " den Rang in der " + Name + " ganz weg. Gilt sofort.";

    public string AufnehmenHilfe => "Nimmt " + _karte.Mitglied.Name + " in die " + Name + " auf, mit dem ersten Rang (" + _laufbahn.Raenge[0].Name + "). Gilt sofort.";

    [RelayCommand(CanExecute = nameof(KannBefoerdern))]
    private Task Befoerdern() => Rang is { } r && Stufe(r.Stufe + 1) is { } neu
        ? _karte.RangSetzenAsync(this, neu, _karte.Mitglied.Name + " ist jetzt " + Name + ": " + neu.Name + ".")
        : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(KannZurueckstufen))]
    private Task Zurueckstufen() => Rang is { } r && Stufe(r.Stufe - 1) is { } neu
        ? _karte.RangSetzenAsync(this, neu, _karte.Mitglied.Name + " ist jetzt " + Name + ": " + neu.Name + ".")
        : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(KannEntfernen))]
    private Task Entfernen() => Rang is not null
        ? _karte.RangSetzenAsync(this, null, _karte.Mitglied.Name + " hat keinen Rang mehr in der " + Name + ".")
        : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(KannAufnehmen))]
    private Task Aufnehmen() => Rang is null && Stufe(1) is { } erster
        ? _karte.RangSetzenAsync(this, erster, _karte.Mitglied.Name + " ist jetzt " + Name + ": " + erster.Name + ".")
        : Task.CompletedTask;
}

/// <summary>Eine Gruppe der Tätigkeiten ("Piloten", "Waffen") mit ihren Umschaltern.</summary>
public sealed record TaetigkeitGruppe(string Name, IReadOnlyList<TaetigkeitFeld> Eintraege);

/// <summary>Eine Tätigkeit als Umschalter mit Bild: an heißt vergeben.</summary>
public sealed partial class TaetigkeitFeld : ObservableObject
{
    private readonly AbzeichenKarte _karte;

    internal TaetigkeitFeld(AbzeichenTaetigkeit t, AbzeichenKarte karte)
    {
        _karte = karte;
        Kennung = t.Kennung;
        Name = t.Name;
    }

    public string Kennung { get; }

    public string Name { get; }

    public string Hilfe => "Vergibt die Tätigkeit " + Name + " an " + _karte.Mitglied.Name + " oder nimmt sie weg. Gilt sofort; höchstens "
        + Grenzen.TaetigkeitenMax.ToString(CultureInfo.CurrentCulture) + " je Mitglied.";

    [ObservableProperty]
    public partial bool Vergeben { get; set; }

    partial void OnVergebenChanged(bool value) => _karte.TaetigkeitGeaendert(this, value);
}
