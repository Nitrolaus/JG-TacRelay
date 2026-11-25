// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung.Modelle;

/// <summary>
/// Rollenkatalog (A46, N6): was jemand kann, etwa "Medium Fighter Pilot". Bis 64 Einträge mit Name (40) und Gruppe (20).
/// Gespeichert wird der ganze Katalog auf einmal mit Versionsprüfung; ein entfernter Eintrag verschwindet auch bei den
/// Mitgliedern und Vormerkungen, das sagt die Ansicht vor dem Speichern.
/// </summary>
public sealed partial class KatalogViewModel : ObservableObject
{
    /// <summary>Die Beispiele aus A46 als Vorschlag.</summary>
    public static IReadOnlyList<(string Name, string Gruppe)> Vorschlaege { get; } =
    [
        ("Medium Fighter Pilot", "Flug"),
        ("Ingenieur", "Technik"),
        ("Medium Gunner", "Geschütz"),
        ("Marine", "Boden"),
    ];

    private readonly IVerwaltungsdienst _dienst;
    private readonly Nachschlag _nachschlag;
    private readonly Func<Task> _neuLaden;
    private long _version;
    private HashSet<ushort> _gespeichert = [];

    public KatalogViewModel(IVerwaltungsdienst dienst, Nachschlag nachschlag, Func<Task> neuLaden)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(nachschlag);
        ArgumentNullException.ThrowIfNull(neuLaden);
        _dienst = dienst;
        _nachschlag = nachschlag;
        _neuLaden = neuLaden;
    }

    public ObservableCollection<KatalogZeile> Eintraege { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(HinzufuegenCommand))]
    public partial string NeuName { get; set; } = "";

    [ObservableProperty]
    public partial string NeuGruppe { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SpeichernCommand))]
    public partial bool Geaendert { get; private set; }

    /// <summary>Was das Speichern bewirkt, wenn Einträge wegfallen.</summary>
    [ObservableProperty]
    public partial string? Warnung { get; private set; }

    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    public void Uebernehmen()
    {
        _version = _nachschlag.Katalog.Version;
        _gespeichert = [.. _nachschlag.Katalog.Eintraege.Select(e => e.Id)];
        Eintraege.Clear();
        foreach (var e in _nachschlag.Katalog.Eintraege) Eintraege.Add(new KatalogZeile(e, this));
        Geaendert = false;
        Warnung = null;
    }

    private bool KannHinzufuegen() => NeuName.Trim().Length > 0 && Eintraege.Count < 64;

    [RelayCommand(CanExecute = nameof(KannHinzufuegen))]
    private void Hinzufuegen()
    {
        Fehler = null;
        if (!Anlegen(NeuName, NeuGruppe)) return;
        NeuName = "";
        NeuGruppe = "";
    }

    /// <summary>Die Beispiele aus A46 dazu, soweit sie fehlen.</summary>
    [RelayCommand]
    private void BeispieleDazu()
    {
        foreach (var (name, gruppe) in Vorschlaege)
            if (Eintraege.All(e => !string.Equals(Namensregeln.Vergleichsschluessel(e.Name), Namensregeln.Vergleichsschluessel(name), StringComparison.Ordinal)))
                Anlegen(name, gruppe);
    }

    private bool Anlegen(string name, string gruppe)
    {
        var n = name.Trim();
        var g = gruppe.Trim();
        if (n.Length is 0 or > 40 || g.Length > 20)
        {
            Fehler = "Name bis 40 Zeichen, Gruppe bis 20.";
            return false;
        }
        if (Eintraege.Any(e => string.Equals(Namensregeln.Vergleichsschluessel(e.Name), Namensregeln.Vergleichsschluessel(n), StringComparison.Ordinal)))
        {
            Fehler = "Die Rolle " + n + " gibt es schon.";
            return false;
        }
        if (Eintraege.Count >= 64)
        {
            Fehler = "Höchstens 64 Rollen.";
            return false;
        }
        var id = (ushort)(Enumerable.Range(1, 65534).First(i => Eintraege.All(e => e.Id != i) && !_gespeichert.Contains((ushort)i)));
        Eintraege.Add(new KatalogZeile(new Rollenkatalogeintrag { Id = id, Name = n, Gruppe = g.Length == 0 ? null : g }, this));
        Veraendert();
        return true;
    }

    internal void Entfernen(KatalogZeile z)
    {
        Eintraege.Remove(z);
        Veraendert();
    }

    private void Veraendert()
    {
        Geaendert = true;
        var weg = _gespeichert.Count(id => Eintraege.All(e => e.Id != id));
        Warnung = weg == 0 ? null
            : "Beim Speichern fallen " + (weg == 1 ? "eine Rolle" : weg + " Rollen") + " weg, und damit auch bei allen Mitgliedern, Wünschen und Vormerkungen, die sie tragen.";
        HinzufuegenCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(Geaendert))]
    private async Task Speichern()
    {
        Fehler = null;
        var e = await _dienst.KatalogSetzenAsync([.. Eintraege.Select(z => z.AlsEintrag())], _version).ConfigureAwait(true);
        if (e is not null)
        {
            Fehler = e;
            return;
        }
        Hinweis = "Rollenkatalog gespeichert.";
        await _neuLaden().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task Verwerfen()
    {
        Uebernehmen();
        return Task.CompletedTask;
    }
}

/// <summary>Ein Eintrag des Katalogs; Name und Gruppe sind änderbar.</summary>
public sealed partial class KatalogZeile : ObservableObject
{
    private readonly KatalogViewModel _bereich;
    private readonly Rollenkatalogeintrag _original;

    internal KatalogZeile(Rollenkatalogeintrag e, KatalogViewModel bereich)
    {
        _original = e;
        _bereich = bereich;
        Id = e.Id;
        Name = e.Name;
        Gruppe = e.Gruppe ?? "";
    }

    public ushort Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Gruppe { get; set; }

    internal Rollenkatalogeintrag AlsEintrag() => _original with { Name = Name.Trim(), Gruppe = Gruppe.Trim().Length == 0 ? null : Gruppe.Trim() };

    [RelayCommand]
    private void Entfernen() => _bereich.Entfernen(this);
}
