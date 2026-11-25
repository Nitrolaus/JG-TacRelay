// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung.Modelle;

/// <summary>
/// Bereich "Einsatzleitung" der Verwaltung (RAIDLEITUNG-ENTWURF, Flotten-Einstellungsseite, Absatz Einsatzleitung; 6.12): die
/// laufenden und geplanten Aufträge mit Name, Zeit, Rechten und wer sie erteilt hat, "Einsatzleitung vergeben" (jetzt oder
/// geplant) und "Entziehen", beides mit frischem Code. Vor dem Senden gelten dieselben Regeln wie am Server (Regeln 2 und 4):
/// ZUWEISEN und SPRECHANZEIGE_ALLE immer, Beginn höchstens 14 Tage voraus, höchstens 24 Stunden, Anlass bis 48 Zeichen,
/// höchstens 4 Aufträge. Zeiten in der Ortszeit der Gilde (<see cref="Auftragszeit"/>), auch deutsch geschrieben ("03.10. 04:00").
/// </summary>
public sealed partial class EinsatzleitungViewModel : ObservableObject
{
    /// <summary>Die Rechte, die ein Auftrag immer trägt (6.12 Regel 2).</summary>
    public static IReadOnlyList<Recht> Pflichtrechte { get; } = [Recht.Zuweisen, Recht.SprechanzeigeAlle];

    private static readonly TimeSpan Voraus = TimeSpan.FromDays(14);
    private static readonly TimeSpan Dauer = TimeSpan.FromHours(24);
    private static readonly string[] DeutscheFormate = ["dd.MM. HH:mm", "d.M. HH:mm", "d.M. H:mm", "dd.MM. H:mm"];
    private static readonly string[] DeutscheFormateMitJahr = ["dd.MM.yyyy HH:mm", "d.M.yyyy HH:mm", "d.M.yyyy H:mm", "dd.MM.yyyy H:mm"];

    private readonly IVerwaltungsdienst _dienst;
    private readonly Nachschlag _nachschlag;
    private readonly Func<DateTimeOffset> _jetzt;
    private IReadOnlyList<BeauftragungEintrag> _auftraege = [];

    public EinsatzleitungViewModel(IVerwaltungsdienst dienst, Nachschlag nachschlag, Func<DateTimeOffset> jetzt)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(nachschlag);
        ArgumentNullException.ThrowIfNull(jetzt);
        _dienst = dienst;
        _nachschlag = nachschlag;
        _jetzt = jetzt;
    }

    public ObservableCollection<AuftragZeile> Auftraege { get; } = [];

    public bool Leer => Auftraege.Count == 0;

    /// <summary>Wer einen Auftrag bekommen kann: freigegebene Konten ohne ADMIN (der braucht keinen), gefiltert nach <see cref="Suche"/>.</summary>
    public ObservableCollection<Auswahl<uint>> Kandidaten { get; } = [];

    [ObservableProperty]
    public partial string Suche { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VergebenCommand))]
    public partial Auswahl<uint>? Konto { get; set; }

    /// <summary>Beginn; leer heißt jetzt.</summary>
    [ObservableProperty]
    public partial string Ab { get; set; } = "";

    /// <summary>Ende; leer heißt der nächste Morgen 04:00 nach dem Beginn.</summary>
    [ObservableProperty]
    public partial string Bis { get; set; } = "";

    /// <summary>Anlass, etwa "Op Nachtfalke" (bis 48 Zeichen, frei).</summary>
    [ObservableProperty]
    public partial string Grund { get; set; } = "";

    [ObservableProperty]
    public partial bool GlobalSenden { get; set; } = true;

    [ObservableProperty]
    public partial bool Durchsage { get; set; } = true;

    [ObservableProperty]
    public partial bool DurchsageAlle { get; set; } = true;

    [ObservableProperty]
    public partial bool MehrfachSenden { get; set; } = true;

    /// <summary>"Kranich leitet ab jetzt bis 01.10. 04:00 (7 h 48 min)."</summary>
    [ObservableProperty]
    public partial string? Vorschau { get; private set; }

    /// <summary>Was gegen das Vergeben spricht, im Satz des Servers; sonst <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VergebenCommand))]
    public partial string? Pruefung { get; private set; }

    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VergebenCommand))]
    public partial bool Beschaeftigt { get; private set; }

    /// <summary>Wer gerade im Auftrag leitet (für die Rückfrage der Flotte), sonst <c>null</c>.</summary>
    public string? Laufend => Auftraege.Any(a => a.Laeuft) ? string.Join(", ", Auftraege.Where(a => a.Laeuft).Select(a => a.Name)) : null;

    /// <summary>Die Rechte im Klartext, in der Folge der Aufzählung.</summary>
    public static string Rechtetext(IEnumerable<Recht> rechte)
    {
        ArgumentNullException.ThrowIfNull(rechte);
        var menge = rechte.ToHashSet();
        return string.Join(", ", new[] { Recht.Zuweisen, Recht.SprechanzeigeAlle, Recht.GlobalSenden, Recht.Durchsage, Recht.DurchsageAlle, Recht.MehrfachSenden }
            .Where(menge.Contains).Select(r => r switch
            {
                Recht.Zuweisen => "einteilen",
                Recht.SprechanzeigeAlle => "alle Sprecher sehen",
                Recht.GlobalSenden => "in Global senden",
                Recht.Durchsage => "Durchsagen",
                Recht.DurchsageAlle => "Durchsagen an alle",
                _ => "in mehrere Kanäle zugleich senden",
            }));
    }

    /// <summary>Kandidaten aus den Mitgliedern neu (nach dem Laden der Verwaltung).</summary>
    public void Uebernehmen()
    {
        KandidatenSetzen();
        Pruefen();
    }

    /// <summary>Aufträge vom Server (Beauftragung LISTE).</summary>
    public async Task LadenAsync()
    {
        var a = await _dienst.AuftraegeAsync().ConfigureAwait(true);
        if (!a.Ok)
        {
            Fehler = a.Fehler;
            return;
        }
        ListeSetzen(a.Wert!);
    }

    private void ListeSetzen(IReadOnlyList<BeauftragungEintrag> liste)
    {
        _auftraege = liste;
        Auftraege.Clear();
        foreach (var e in liste.OrderBy(e => e.Von)) Auftraege.Add(Zeile(e));
        OnPropertyChanged(nameof(Leer));
        OnPropertyChanged(nameof(Laufend));
        Pruefen();
    }

    private AuftragZeile Zeile(BeauftragungEintrag e) => new()
    {
        KontoId = e.KontoId,
        Name = e.ScName ?? e.Name,
        Laeuft = e.Aktiv,
        Zeit = e.Aktiv ? "läuft bis " + Auftragszeit.Anzeigen(e.Bis) : "geplant " + Auftragszeit.Anzeigen(e.Von) + " bis " + Auftragszeit.Anzeigen(e.Bis),
        Rechte = Rechtetext(e.Rechte),
        Von = e.Durch == 0 ? "von der Serverkonsole" : "von " + KontoName(e.Durch),
        Grund = e.Grund,
    };

    private string KontoName(uint id) => _nachschlag.Mitglieder.FirstOrDefault(m => m.KontoId == id) is { } m
        ? (m.ScNamen.Count > 0 ? m.ScNamen[0] : m.Name)
        : "Konto " + id.ToString(CultureInfo.InvariantCulture);

    private void KandidatenSetzen()
    {
        var suche = Suche.Trim();
        var schluessel = suche.Length == 0 ? null : Namensregeln.Vergleichsschluessel(suche);
        var liste = _nachschlag.Mitglieder
            .Where(m => m.KontoId is not null && m.Zustand != MitgliedZustand.Antrag && m.Stufe != Funk.Protokoll.Steuerung.Stufe.Admin)
            .Select(m => (Mitglied: m, Text: Kandidatentext(m)))
            .Where(x => schluessel is null || new[] { x.Mitglied.Name, x.Mitglied.DiscordName }.Concat(x.Mitglied.ScNamen).OfType<string>()
                .Any(n => Namensregeln.Vergleichsschluessel(n).Contains(schluessel, StringComparison.Ordinal)))
            .OrderBy(x => x.Text, StringComparer.Ordinal)
            .Select(x => new Auswahl<uint>(x.Mitglied.KontoId!.Value, x.Text))
            .ToList();
        var bisher = Konto?.Wert;
        Kandidaten.Clear();
        foreach (var k in liste) Kandidaten.Add(k);
        if (bisher is { } b && liste.FirstOrDefault(k => k.Wert == b) is { } gleich) Konto = gleich;
    }

    private static string Kandidatentext(MitgliedEintrag m)
    {
        var name = m.ScNamen.Count > 0 ? m.ScNamen[0] : m.Name;
        return m.DiscordName is { Length: > 0 } d && !string.Equals(d, name, StringComparison.Ordinal) ? name + " (" + d + ")" : name;
    }

    partial void OnSucheChanged(string value) => KandidatenSetzen();

    partial void OnKontoChanged(Auswahl<uint>? value) => Pruefen();

    partial void OnAbChanged(string value) => Pruefen();

    partial void OnBisChanged(string value) => Pruefen();

    partial void OnGrundChanged(string value) => Pruefen();

    /// <summary>Liest eine Zeit: "20:00" (die nächste solche Uhrzeit), "03.10. 04:00", "03.10.2026 04:00" oder "2026-10-03 04:00".</summary>
    internal static long? ZeitLesen(string text, long nachUnixMs)
    {
        ArgumentNullException.ThrowIfNull(text);
        var t = text.Trim();
        if (Auftragszeit.Lesen(t, nachUnixMs) is { } ms) return ms;
        var zone = Auftragszeit.Zone;
        var nach = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nachUnixMs), zone);
        if (DateTime.TryParseExact(t, DeutscheFormateMitJahr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var mitJahr))
            return Auftragszeit.Lesen(mitJahr.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), nachUnixMs);
        foreach (var format in DeutscheFormate)
        {
            if (!DateTime.TryParseExact(t + " " + nach.Year.ToString(CultureInfo.InvariantCulture), format + " yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var datum))
                continue;
            // Ohne Jahr: das nächste solche Datum (im Dezember ist "03.01." im neuen Jahr).
            if (datum < nach.DateTime.Date) datum = datum.AddYears(1);
            return Auftragszeit.Lesen(datum.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), nachUnixMs);
        }
        return null;
    }

    private static string Unlesbar(string text) => "Die Zeit \"" + text.Trim() + "\" verstehe ich nicht. So geht es: 20:00, 03.10. 04:00 oder 2026-10-03 04:00.";

    /// <summary>Prüft wie der Server und baut die Vorschau.</summary>
    private void Pruefen()
    {
        Vorschau = null;
        if (Konto is not { } k)
        {
            Pruefung = null;
            return;
        }
        var jetzt = _jetzt().ToUnixTimeMilliseconds();
        long? von = null;
        if (Ab.Trim().Length > 0 && !string.Equals(Ab.Trim(), "jetzt", StringComparison.OrdinalIgnoreCase))
        {
            von = ZeitLesen(Ab, jetzt);
            if (von is null)
            {
                Pruefung = Unlesbar(Ab);
                return;
            }
        }
        var beginn = Math.Max(von ?? jetzt, jetzt);
        long bis;
        if (Bis.Trim().Length == 0) bis = Auftragszeit.NaechsterMorgen(beginn);
        else if (ZeitLesen(Bis, beginn) is { } b) bis = b;
        else
        {
            Pruefung = Unlesbar(Bis);
            return;
        }
        // Ein ausgeschriebenes Datum vor dem Beginn bleibt, wie es ist; eine bloße Uhrzeit liest Auftragszeit schon als die nächste.
        if (beginn - jetzt > (long)Voraus.TotalMilliseconds) Pruefung = "Ein Auftrag beginnt höchstens 14 Tage im Voraus.";
        else if (bis <= beginn) Pruefung = "Das Ende liegt nicht nach dem Beginn.";
        else if (bis - beginn > (long)Dauer.TotalMilliseconds) Pruefung = "Ein Auftrag dauert höchstens 24 Stunden.";
        else if (Grund.Trim().EnumerateRunes().Count() > Grenzen.AuftragGrundMax) Pruefung = "Der Anlass hat höchstens 48 Zeichen.";
        else if (_auftraege.Count(a => a.KontoId != k.Wert && a.Bis > jetzt) >= Grenzen.AuftraegeMax) Pruefung = "Es laufen schon 4 Aufträge (laufend oder geplant).";
        else Pruefung = null;
        if (Pruefung is not null) return;
        var dauer = TimeSpan.FromMilliseconds(bis - beginn);
        var name = k.Text.Split(" (", 2)[0];
        Vorschau = name + " leitet ab " + (von is null || von <= jetzt ? "jetzt" : Auftragszeit.Anzeigen(beginn)) + " bis " + Auftragszeit.Anzeigen(bis)
            + " (" + ((int)dauer.TotalHours).ToString(CultureInfo.InvariantCulture) + " h " + dauer.Minutes.ToString(CultureInfo.InvariantCulture) + " min).";
    }

    private bool KannVergeben() => Konto is not null && Pruefung is null && !Beschaeftigt;

    /// <summary>"Einsatzleitung vergeben": ohne Angaben jetzt bis 04:00 (der Server rechnet das Ende), mit frischem Code.</summary>
    [RelayCommand(CanExecute = nameof(KannVergeben))]
    private async Task Vergeben()
    {
        if (Konto is not { } k) return;
        Pruefen();
        if (Pruefung is not null) return;
        var jetzt = _jetzt().ToUnixTimeMilliseconds();
        long? von = Ab.Trim().Length == 0 || string.Equals(Ab.Trim(), "jetzt", StringComparison.OrdinalIgnoreCase) ? null : ZeitLesen(Ab, jetzt);
        if (von <= jetzt) von = null;
        long? bis = Bis.Trim().Length == 0 ? null : ZeitLesen(Bis, Math.Max(von ?? jetzt, jetzt));
        var rechte = new List<Recht>();
        if (GlobalSenden) rechte.Add(Recht.GlobalSenden);
        if (Durchsage) rechte.Add(Recht.Durchsage);
        if (DurchsageAlle) rechte.Add(Recht.DurchsageAlle);
        if (MehrfachSenden) rechte.Add(Recht.MehrfachSenden);
        rechte.AddRange(Pflichtrechte);
        Fehler = null;
        Hinweis = null;
        Beschaeftigt = true;
        try
        {
            var a = await _dienst.AuftragErteilenAsync(k.Wert, von, bis, rechte, Grund.Trim().Length == 0 ? null : Grund.Trim()).ConfigureAwait(true);
            if (!a.Ok)
            {
                Fehler = a.Fehler;
                return;
            }
            var name = k.Text.Split(" (", 2)[0];
            ListeSetzen(a.Wert!);
            Hinweis = "Einsatzleitung für " + name + " vergeben.";
            Konto = null;
            Ab = "";
            Bis = "";
            Grund = "";
            GlobalSenden = Durchsage = DurchsageAlle = MehrfachSenden = true;
        }
        finally
        {
            Beschaeftigt = false;
        }
    }

    /// <summary>"Entziehen" mit frischem Code; der Auftrag endet sofort.</summary>
    [RelayCommand]
    private async Task Entziehen(AuftragZeile? zeile)
    {
        if (zeile is null) return;
        Fehler = null;
        Hinweis = null;
        var f = await _dienst.AuftragEntziehenAsync(zeile.KontoId).ConfigureAwait(true);
        if (f is not null)
        {
            Fehler = f;
            return;
        }
        Hinweis = "Einsatzleitung von " + zeile.Name + " entzogen.";
        await LadenAsync().ConfigureAwait(true);
    }
}

/// <summary>Ein laufender oder geplanter Auftrag in der Liste.</summary>
public sealed class AuftragZeile
{
    public uint KontoId { get; init; }

    /// <summary>SC-Name, sonst Kontoname.</summary>
    public string Name { get; init; } = "";

    /// <summary>"läuft bis 01.10. 04:00" oder "geplant 02.10. 20:00 bis 03.10. 04:00".</summary>
    public string Zeit { get; init; } = "";

    public string Rechte { get; init; } = "";

    /// <summary>"von Nordlicht", "von der Serverkonsole".</summary>
    public string Von { get; init; } = "";

    public string? Grund { get; init; }

    public bool Laeuft { get; init; }

    /// <summary>Alles in einem Satz, für Vorleser.</summary>
    public string Beschreibung => "Einsatzleitung " + Name + ", " + Zeit + ", " + Von + (Grund is { } g ? ", " + g : "");
}
