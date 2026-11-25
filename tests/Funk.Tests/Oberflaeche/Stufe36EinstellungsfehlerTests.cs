// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Client.Dienste;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Fehler der Einstellungsdatei ohne Pfad (Fassung 0.6.0, Schritt 3.6, offen aus Stufe 2): Was Laden und Speichern melden, steht in
/// Fußzeile und Einstellungen und landet so auf Bildschirmfotos. Der Pfad trägt den Windows-Benutzernamen; gemeldet wird deshalb eine
/// Beschreibung, nie <c>ex.Message</c>, die bei Dateifehlern den ganzen Pfad nennt.
/// </summary>
internal static class Stufe36EinstellungsfehlerTests
{
    private static void OhnePfad(string? text, string ordner, string was)
    {
        Pruefe.Wahr(text is { Length: > 0 }, was + ": gemeldet");
        Pruefe.Falsch(text!.Contains(ordner, StringComparison.OrdinalIgnoreCase), was + ": ohne Ordner: " + text);
        Pruefe.Falsch(text.Contains("e.json", StringComparison.OrdinalIgnoreCase), was + ": ohne Dateinamen: " + text);
        Pruefe.Falsch(text.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase), was + ": ohne Benutzernamen: " + text);
    }

    [Test]
    public static void Einstellungen_Fehlertexte_OhnePfad()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("e.json"));

        File.WriteAllText(s.Pfad, "{ \"gate\": { \"modus\": ");
        s.Laden();
        OhnePfad(s.LetzterFehler, o.Pfad, "kaputtes JSON");
        Pruefe.Wahr(s.LetzterFehler!.StartsWith("Einstellungen unlesbar, Standardwerte gelten: ", StringComparison.Ordinal)
            && s.LetzterFehler.Contains("JSON", StringComparison.Ordinal), "sagt, was los ist: " + s.LetzterFehler);

        // Eine Datei, die ein anderes Programm ohne Teilen offen hält: IOException mit Pfad in der Meldung.
        File.WriteAllText(s.Pfad, "{}");
        using (new FileStream(s.Pfad, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            s.Laden();
            OhnePfad(s.LetzterFehler, o.Pfad, "gesperrte Datei");
            Pruefe.Wahr(s.LetzterFehler!.StartsWith("Einstellungen nicht lesbar, Standardwerte gelten: ", StringComparison.Ordinal), "nicht lesbar: " + s.LetzterFehler);
        }

        // Ein Ordner an der Stelle der Datei: Ersetzen scheitert mit Pfad in der Meldung.
        File.Delete(s.Pfad);
        Directory.CreateDirectory(s.Pfad);
        using var a = new Einstellungsablage(s, new ClientEinstellungen(), SofortTakt.Instanz, TimeSpan.FromHours(1));
        a.Vormerken();
        a.Sichern();
        OhnePfad(a.Fehler, o.Pfad, "Schreibfehler");
        Pruefe.Wahr(a.Fehler!.StartsWith("Einstellungen nicht gespeichert: ", StringComparison.Ordinal), "nicht gespeichert: " + a.Fehler);
    }
}
