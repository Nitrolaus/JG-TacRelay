// JG TacRelay - Maintainer: Sarevoc
namespace Funk.ClientKern;

/// <summary>
/// Die schmale Naht zum Verwaltungsteil (A45, ANMELDUNG-SICHERHEIT "Admin-Teile nur für Admins"). Der normale Client kennt
/// nur diese drei Typen; Oberfläche und Logik der Verwaltung (Admin-Schlüssel, Code, Anträge, Mitglieder) liegen in
/// <c>verwaltung\Funk.Verwaltung.dll</c>, die nur das Admin-Setup mitbringt. Geladen wird sie nur mit passendem SHA-256.
/// Durchgesetzt wird trotzdem allein am Server; die Naht ist Bequemlichkeit, kein Schutz.
/// </summary>
public interface IVerwaltungsteil : IDisposable
{
    /// <summary>Titel des Bereichs im Hauptfenster, etwa "Verwaltung".</summary>
    string Titel { get; }

    /// <summary>
    /// Baut die Ansicht des Bereichs, ein WPF-Element (der Kern selbst kennt kein WPF), auf dem gemeinsamen Zustand. Neu bei
    /// jedem Aufbau des Hauptfensters, etwa nach einem Farbwechsel: Stile lösen ihre Farben beim Bauen auf, und ein Element
    /// hat nur einen Platz im Baum.
    /// </summary>
    object AnsichtBauen();

    /// <summary>Die Admin-Sitzung ist offen (0.9.1): Beim Beenden des Programms fragt das Hauptfenster nach.</summary>
    bool AdminSitzungOffen => false;

    /// <summary>Der Nutzer hat den Bereich verlassen (anderer Bereich gewählt oder Verwaltung entzogen): Geheimnisse aus der Anzeige.</summary>
    void Verlassen()
    {
    }

    /// <summary>
    /// Zeigt ein Mitglied (seit 0.11.5, Status in "Wer ist da"): Seite Mitglieder, dieses Konto gewählt, damit der Admin dort Rollen und
    /// Angaben vergibt. Ist die Admin-Sitzung noch zu, wählt die Verwaltung es nach der Anmeldung. Ohne Umsetzung geschieht nichts.
    /// </summary>
    void MitgliedZeigen(uint kontoId)
    {
    }

    /// <summary>
    /// Die Admin-Sitzung ging auf oder zu (seit 0.11.7, <see cref="AdminSitzungOffen"/>): Das Hauptfenster zeigt dann den Bereich Abzeichen ganz
    /// unten in der Navigation oder nimmt ihn weg. Kommt auf dem Oberflächen-Thread. Ohne Umsetzung kommt nichts.
    /// </summary>
    event Action? AdminSitzungGeaendert
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Baut die Seite Abzeichen (seit 0.11.7: Ränge und Tätigkeiten vergeben, nur in der Admin-Sitzung), ein WPF-Element wie
    /// <see cref="AnsichtBauen"/>; neu bei jedem Aufbau des Hauptfensters. <c>null</c> ohne Umsetzung: Dann gibt es den Bereich nicht.
    /// </summary>
    object? AbzeichenAnsichtBauen() => null;
}

/// <summary>Was der Client dem Verwaltungsteil gibt: den laufenden Client und den Oberflächen-Thread.</summary>
public interface IVerwaltungsWirt
{
    /// <summary>Der Client der laufenden Anbindung oder <c>null</c>; nach Trennen und Verbinden ein anderer.</summary>
    FunkClient? Client { get; }

    /// <summary>Client gewechselt, Zustand oder eigenes Konto geändert; kommt aus einem beliebigen Thread.</summary>
    event Action? Geaendert;

    /// <summary>Arbeit auf den Oberflächen-Thread bringen.</summary>
    void Einreihen(Action arbeit);

    /// <summary>Ordner für Sicherungen und Entwürfe der Flotte (0.3.0); ohne Angabe keiner.</summary>
    string? Ablageordner => null;
}

/// <summary>Baut den Verwaltungsteil; der Einstieg des Verwaltungsteils hat einen öffentlichen Konstruktor ohne Argumente.</summary>
public interface IVerwaltungsteilFabrik
{
    IVerwaltungsteil Erzeugen(IVerwaltungsWirt wirt);
}

/// <summary>Nennt im Verwaltungsteil die Klasse, die <see cref="IVerwaltungsteilFabrik"/> umsetzt.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class VerwaltungsteilAttribute(Type einstieg) : Attribute
{
    public Type Einstieg { get; } = einstieg;
}
