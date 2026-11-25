// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Protokoll.Identitaet;

namespace Funk.Admin;

/// <summary>
/// Die Bitte eines Serverbetreibers um ein Profil (EINRICHTEN-UMZUG-ENTWURF): eine Zeile
/// <c>TR-PROFILANFRAGE v1 &lt;host&gt; &lt;port&gt; &lt;pin&gt;</c>, die über jeden Weg zu Sarevoc darf, dazu die Kennung des Pins, die am
/// Telefon vorgelesen wird. Nichts darin ist geheim; geprüft wird sie durch den Vergleich der Kennung. Der Host darf wie bei
/// <c>profil-schreiben</c> die Familien tragen (<c>name/v4v6</c>), ohne Angabe nur IPv4.
/// </summary>
public sealed record Profilanfrage(ProfilHost Host, ushort Port, Fingerabdruck Pin)
{
    public const string Kopf = "TR-PROFILANFRAGE v1";

    /// <summary>Die Zeile zum Verschicken.</summary>
    public string Zeile => Kopf + " " + Hostangabe.Text(Host) + " " + Port.ToString(CultureInfo.InvariantCulture) + " " + Pin.AlsBase64();

    /// <summary>Die Kennung zum Vorlesen: der Pin in Anzeigeform, wie sie Sarevoc in profil-erneuern sieht.</summary>
    public string Kennung => Pin.Anzeige;

    /// <summary>
    /// Sucht die Zeile in <paramref name="text"/> (eingefügt oder aus profilanfrage.txt, auch mit Zeilen davor und danach) und
    /// liest sie streng. <paramref name="fehler"/> sagt in einem Satz, was nicht stimmt.
    /// </summary>
    public static bool TryLesen(string? text, out Profilanfrage? anfrage, out string? fehler)
    {
        anfrage = null;
        fehler = null;
        var zeile = (text ?? "").Split('\n').Select(z => z.Trim()).FirstOrDefault(z => z.StartsWith("TR-PROFILANFRAGE", StringComparison.Ordinal));
        if (zeile is null)
        {
            fehler = "Darin steht keine Anfragezeile; sie beginnt mit " + Kopf + ".";
            return false;
        }
        var teile = zeile.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (teile.Length < 2 || !string.Equals(teile[0] + " " + teile[1], Kopf, StringComparison.Ordinal))
        {
            fehler = "Unbekannte Fassung der Anfragezeile; dieses Werkzeug kennt " + Kopf + ".";
            return false;
        }
        if (teile.Length != 5)
        {
            fehler = "Die Anfragezeile hat " + teile.Length.ToString(CultureInfo.InvariantCulture) + " Teile statt 5: " + Kopf + " <host> <port> <pin>.";
            return false;
        }
        ProfilHost host;
        try
        {
            host = Hostangabe.Lesen(teile[2], "Der Host");
        }
        catch (ArgumentException e)
        {
            fehler = e.Message;
            return false;
        }
        if (!ushort.TryParse(teile[3], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port == 0)
        {
            fehler = "Der Port in der Anfragezeile muss eine Zahl von 1 bis 65535 sein: " + teile[3] + ".";
            return false;
        }
        if (!Fingerabdruck.TryAusBase64(teile[4], out var pin))
        {
            fehler = "Der Pin in der Anfragezeile ist kein Pin (44 Zeichen Base64); ist beim Kopieren etwas verloren gegangen?";
            return false;
        }
        anfrage = new Profilanfrage(host, port, pin);
        return true;
    }
}
