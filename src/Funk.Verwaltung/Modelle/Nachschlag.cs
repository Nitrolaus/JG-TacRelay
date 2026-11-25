// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung.Modelle;

/// <summary>
/// Was die Bereiche der Verwaltung gemeinsam nachschlagen: Rollenkatalog und Mitglieder (für Namen,
/// Auswahllisten und Vorschläge). Geladen vom <see cref="VerwaltungViewModel"/>, gelesen von allen Bereichen.
/// </summary>
public sealed class Nachschlag
{
    public RollenkatalogAntwort Katalog { get; set; } = new() { Eintraege = [], Version = 0 };

    public IReadOnlyList<MitgliedEintrag> Mitglieder { get; set; } = [];

    public string Rolle(ushort id) =>
        Katalog.Eintraege.FirstOrDefault(e => e.Id == id)?.Name ?? "Rolle " + id.ToString(CultureInfo.InvariantCulture);

    public string Rollen(IEnumerable<ushort>? ids) => ids is null ? "" : string.Join(", ", ids.Select(Rolle));

    /// <summary>Häkchenliste des Katalogs, vorbelegt mit <paramref name="gewaehlt"/>.</summary>
    public ObservableCollection<Haken<ushort>> RollenHaken(IEnumerable<ushort>? gewaehlt)
    {
        var an = gewaehlt?.ToHashSet() ?? [];
        return [.. Katalog.Eintraege.Select(e => new Haken<ushort>(e.Id, e.Gruppe is null ? e.Name : e.Name + " (" + e.Gruppe + ")", an.Contains(e.Id)))];
    }
}

/// <summary>Ein Eintrag für ein Auswahlfeld. Das Feld zeigt den Text, auch wo die Vorlage DisplayMemberPath nicht kennt.</summary>
public sealed record Auswahl<T>(T Wert, string Text)
{
    public override string ToString() => Text;
}

/// <summary>Ein Häkchen mit Wert und Text.</summary>
public sealed partial class Haken<T>(T wert, string text, bool gewaehlt) : ObservableObject
{
    public T Wert { get; } = wert;

    public string Text { get; } = text;

    [ObservableProperty]
    public partial bool Gewaehlt { get; set; } = gewaehlt;
}
