// JG TacRelay - Maintainer: Sarevoc
using System.Windows.Automation;
using System.Windows.Controls;
using Funk.Client.Design;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Der Rauschfilter (Klangstufe B) unter Einstellungen > Senden (seit 0.11.1 offen, ohne Aufklappen; 0.10.0 und 0.11.0 im Klappbereich, bis 0.9.3
/// Karte Klangmodule, bis 0.8.x Verarbeitung), gezeichnet
/// ohne Fensterhandle (<see cref="Bildlauf"/>), in allen drei
/// Farbstellungen: seit 0.9.0 (Q48) der Schalter Rauschunterdrückung mit Zugriffstaste R und zwei Knöpfe Standard und Stark mit Hilfetext (ausgeschaltet
/// zeigen sie gesperrt die Wahl, die beim Einschalten wieder gilt), der Hinweis darunter je Stand (Standard, Stark
/// wirkt, Stark mit Rückfall, Aus) und sein Kontrast; mit <c>FUNK_BILDER</c> entstehen Bilder in der Voreinstellung.
/// Der Name muss nach OberflaecheFensterTests einsortiert sein (Bildlauf, CODEMAP).
/// </summary>
internal static class OberflaecheRauschfilterBilderTests
{
    [Test]
    public static void Rauschfilter_KarteVerarbeitung_KnoepfeHinweisUndKontrast()
    {
        var staende = new (string Name, Rauschfilter Filter, string? Rueckfall, string Hinweis)[]
        {
            ("standard", Rauschfilter.Standard, null, "Der Standardfilter wirkt."),
            ("stark", Rauschfilter.Stark, null, "DeepFilterNet3 wirkt."),
            ("rueckfall", Rauschfilter.Stark, "Stark brauchte mehrmals länger als die 10 ms, die ein Rahmen Zeit hat. Standard wirkt, bis Stark neu gewählt oder Audio neu gestartet wird.",
                "Stark brauchte mehrmals länger als die 10 ms"),
            ("aus", Rauschfilter.Aus, null, "Kein Filter."),
        };
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                foreach (var (name, filter, rueckfall, hinweis) in staende)
                {
                    using var a = new Aufbau();
                    a.Ablage.Aktuell.Audio.Rauschfilter = filter;
                    a.Maschine.RauschfilterRueckfall = rueckfall;
                    var h = a.Haupt(out _, out _, out var audio, out _);
                    // Seit 0.11.1 offen unter Einstellungen > Senden, ohne Aufklappen (Sarevoc 12.10.2026; 0.10.0 und 0.11.0 im Klappbereich, bis 0.9.3
                    // Karte Klangmodule auf der Seite Audio).
                    h.AktiverBereich = Seiten.Einstellungen(h);
                    h.Takt();
                    var haupt = new Hauptfenster(h, a.Ablage);
                    fenster.Add(haupt);
                    var bild = Ganz(haupt);
                    var was = satz.Name + ", " + name;

                    var inhalt = Bildlauf.Nachfahren<StackPanel>(Bildlauf.Wurzel(haupt)).Single(x => x.Name == "AbschnittSenden");
                    Pruefe.Falsch(Bildlauf.Nachfahren<Expander>(inhalt).Single().IsExpanded, was + ": Erweitert zu, der Rauschfilter trotzdem zu sehen");
                    var knoepfe = Bildlauf.Nachfahren<RadioButton>(inhalt).Where(r => r.GroupName == "Rauschfilter").ToList();
                    Pruefe.Gleich("Rauschfilter Standard|Rauschfilter Stark", string.Join("|", knoepfe.Select(AutomationProperties.GetName)), was + ": zwei Knöpfe");
                    Pruefe.Gleich(filter == Rauschfilter.Aus ? "Standard" : filter.ToString(), knoepfe.Single(k => k.IsChecked == true).Content, was + ": gedrückt ist der gewählte");
                    Pruefe.Wahr(knoepfe.All(k => Glasdesign.Hilfe.GetText(k) is { Length: > 20 }), was + ": jeder Knopf hat einen Hilfetext");
                    var schalter = Bildlauf.Nachfahren<CheckBox>(inhalt).Single(c => AutomationProperties.GetName(c) == "Rauschunterdrückung");
                    Pruefe.Gleich(filter != Rauschfilter.Aus, schalter.IsChecked == true, was + ": der Schalter Rauschunterdrückung");
                    Pruefe.Gleich(filter != Rauschfilter.Aus, knoepfe.All(k => k.IsEnabled), was + ": aus sind die Knöpfe gesperrt");
                    Pruefe.Wahr(Bildlauf.Nachfahren<AccessText>(schalter).Any(t => t.Text == "_Rauschunterdrückung"), was + ": Alt+R schaltet die Rauschunterdrückung");
                    Pruefe.Wahr(Sichtbar(inhalt, hinweis), was + ": Hinweis \"" + hinweis + "\"");
                    Pruefe.Wahr(Ansichtspruefung.ZuSehen(schalter), was + ": der Schalter ist ohne Aufklappen zu sehen");
                    KontrastPruefen(inhalt, satz, was + ", Senden", mindestens: 3);
                    if (satz.Stellung == Farbvorschlaege.Voreinstellung) Bildablage.Ablegen(bild, "rauschfilter-" + name);
                }
            }
        });
    }
}
