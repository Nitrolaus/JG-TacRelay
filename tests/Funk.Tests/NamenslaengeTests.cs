// JG TacRelay - Maintainer: Sarevoc
using System.Text.RegularExpressions;

namespace Funk.Tests;

/// <summary>
/// Sarevoc 13.10.2026: "beim Namen anlegen eine maximale Länge angeben, Beispiel Superschütze/Alexander, nicht länger" - damit jeder Name in Wer ist
/// da und auf der Raid-Seite ganz steht. Die Eingabe in Client (erster Start, Ich) und Verwaltung (SC-Name) nimmt höchstens 22 Zeichen; der Server
/// nimmt weiter bis <see cref="Funk.Protokoll.Identitaet.Zeichenregeln.ScNameMax"/>, damit ältere, längere Namen gültig bleiben.
/// </summary>
internal static class NamenslaengeTests
{
    [Test]
    public static void Namensfelder_HoechstensZweiundzwanzigZeichen()
    {
        var repo = EinrichtungTests.Repo();
        var bereiche = File.ReadAllText(Path.Combine(repo, "src", "Funk.Client", "Fenster", "Bereiche.xaml"));
        foreach (var name in new[] { "ScNameFeld", "MeinScName" })
            Pruefe.Wahr(Regex.IsMatch(bereiche, "x:Name=\"" + name + "\"[^>]*MaxLength=\"22\""), name + ": höchstens 22 Zeichen");
        var verwaltung = File.ReadAllText(Path.Combine(repo, "src", "Funk.Verwaltung", "Ansichten", "VerwaltungAnsicht.xaml"));
        Pruefe.Wahr(Regex.IsMatch(verwaltung, "Binding ScName[^>]*MaxLength=\"22\"[^>]*SC-Name"), "Verwaltung, SC-Name: höchstens 22 Zeichen");
        Pruefe.Gleich(22, "Superschütze/Alexander".Length, "Sarevocs Beispiel hat genau 22 Zeichen");
    }
}
