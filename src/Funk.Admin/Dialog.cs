// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Admin;

/// <summary>
/// Fragen an der Konsole für die Befehle mit Rückfrage (profil-erneuern, umzug-einspielen, erster-admin). Liest Zeilen, bis eine
/// passt; ein Ende der Eingabe (Fenster zu, Strg+Z) heißt immer: abbrechen, nichts tun.
/// </summary>
public sealed class Dialog(TextReader ein, TextWriter aus)
{
    public TextWriter Aus { get; } = aus;

    /// <summary>Zeigt <paramref name="frage"/> und liest eine Zeile; <c>null</c> am Ende der Eingabe.</summary>
    public string? Zeile(string frage)
    {
        Aus.Write(frage);
        Aus.Flush();
        return ein.ReadLine();
    }

    /// <summary>
    /// Ja oder nein (j, ja, n, nein, Groß und Klein egal). Andere Antworten fragen noch einmal; am Ende der Eingabe nein.
    /// </summary>
    public bool JaNein(string frage)
    {
        for (var i = 0; i < 5; i++)
        {
            var antwort = Zeile(frage + " (j/n): ")?.Trim().ToLowerInvariant();
            switch (antwort)
            {
                case null:
                    Aus.WriteLine();
                    return false;
                case "j" or "ja":
                    return true;
                case "n" or "nein":
                    return false;
            }
            Aus.WriteLine("Bitte j oder n.");
        }
        return false;
    }
}
