// JG TacRelay - Maintainer: Sarevoc
using System.Text;

namespace Funk.Testserver;

/// <summary>
/// Argumente des Begleitprogramms (<c>--client-argumente</c>): durch Leerzeichen getrennt, doppelte Anführungszeichen
/// fassen ein Argument mit Leerzeichen zusammen. <c>{testzugang}</c> ersetzt der Testserver durch 127.0.0.1:Port.
/// </summary>
public static class Begleitprogramm
{
    /// <summary>
    /// Warum das Begleitprogramm <paramref name="programm"/> nicht gestartet wird, oder null. JG TacRelay selbst zeigt
    /// Fenster (das Hauptfenster aktiviert, das Funkfeld immer vorn), öffnet Mikrofon, Loopback und eine hörbare
    /// Wiedergabe und meldet globale Tasten und Joysticks an. Das ist der Probefunk auf Sarevocs PC (Probefunk starten.cmd),
    /// nie ein stiller Werkzeuglauf; deshalb nur mit <c>--client-freigegeben</c>. Andere Programme (funk-testclient
    /// echo-probe) brauchen keine Freigabe.
    /// </summary>
    public static string? Sperre(string programm, bool freigegeben)
    {
        ArgumentNullException.ThrowIfNull(programm);
        if (freigegeben || !string.Equals(Path.GetFileNameWithoutExtension(programm), "TacRelay", StringComparison.OrdinalIgnoreCase)) return null;
        return "JG TacRelay als Begleitprogramm zeigt Fenster und öffnet Mikrofon, Wiedergabe und globale Tasten; " +
            "nur mit --client-freigegeben (Probefunk starten.cmd). Prüfen ohne Fenster und Audio: --client funk-testclient.exe " +
            "--client-argumente \"echo-probe --testzugang {testzugang}\".";
    }

    public static IReadOnlyList<string> Argumente(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var liste = new List<string>();
        var aktuell = new StringBuilder();
        var inAnfuehrung = false;
        var begonnen = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inAnfuehrung = !inAnfuehrung;
                begonnen = true;
            }
            else if (char.IsWhiteSpace(c) && !inAnfuehrung)
            {
                if (begonnen) liste.Add(aktuell.ToString());
                aktuell.Clear();
                begonnen = false;
            }
            else
            {
                aktuell.Append(c);
                begonnen = true;
            }
        }
        if (inAnfuehrung) throw new ArgumentException("Anführungszeichen nicht geschlossen: " + text, nameof(text));
        if (begonnen) liste.Add(aktuell.ToString());
        return liste;
    }
}
