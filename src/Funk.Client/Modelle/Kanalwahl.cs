// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Client.Modelle;

/// <summary>
/// Ein Eintrag der Rollenliste oben links im Funkfeld (Stufe 3, Sarevocs Antworten Q1 und Q2): mit Nummer davor, etwa
/// "3 Kapitän", dazu im Tooltip die Kanäle der Rolle. Jede Rolle der Liste ist wählbar (die Liste führt nur selbst wählbare).
/// </summary>
public sealed record RollenEintrag(ushort Id, string Text, string Hilfe)
{
    public bool Waehlbar { get; } = true;

    public override string ToString() => Text;
}

/// <summary>
/// Ein Ch. im Ch.-Wähler eines Felds (6.16): Index in der Ch.-Liste des Kanalnetzes (0 ist die Ruhe), Bezeichnung wie "Schiff 3", und
/// ob er sich wählen lässt; sonst sagt <see cref="Grund"/> warum (gesperrt, schon in einem anderen Feld).
/// </summary>
public sealed record ChEintrag(byte Ch, string Text, bool Waehlbar, string? Grund)
{
    /// <summary>Tooltip in der Liste: der Grund, wenn er nicht wählbar ist.</summary>
    public string? Hilfe => Grund;

    /// <summary>Gesperrt von der Einsatzleitung oder einem Admin (6.17): In der Liste steht ein roter Punkt davor (STUFE3-ENTWURF 5.3).</summary>
    public bool Gesperrt { get; init; }

    public override string ToString() => Text;
}
