// JG TacRelay - Maintainer: Sarevoc
using Microsoft.Win32;

namespace Funk.Client.Fenster;

/// <summary>
/// "Programmdatei wählen ..." beim Spiel (Umbau 19.10.2026, Sarevoc Q10): der Dateidialog von Windows für eine .exe. Liefert nur den Pfad; gemerkt wird
/// daraus der Prozessname (<see cref="Ablage.Spielprofil.Prozessname"/>). Nur im Betrieb; Tests geben eine eigene Wahl mit.
/// </summary>
public static class Spieldatei
{
    /// <summary>Der gewählte Pfad oder <c>null</c>, wenn abgebrochen.</summary>
    public static string? Waehlen()
    {
        var d = new OpenFileDialog
        {
            Title = "Programmdatei des Spiels wählen",
            Filter = "Programme (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        return d.ShowDialog() == true ? d.FileName : null;
    }
}
