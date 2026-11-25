// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;
using Funk.Tests.Verarbeitung;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Hörprobe und das Mithören, gezeichnet ohne Fensterhandle (<see cref="Bildlauf"/>), in allen drei Farbstellungen: mit Lautsprechern
/// (Mithören gesperrt, mit Grund), mit Headset und Schalter aus (der Text sagt "Aus." zuerst, Prüfung 0.4.1), mit Headset und Mithören an (mit
/// Auslösung), während Aufnahme und Wiedergabe. Bis 0.9.3 stand beides in einer Karte auf der Seite Audio; seit 0.10.0 (AP3, Q75, E19) steht die
/// eine Hörprobe auf Start und das Mithören unter Einstellungen > Senden > Feinheiten. Prüft Texte, Schalter und Kontrast; mit <c>FUNK_BILDER</c>
/// entstehen Bilder in der Voreinstellung. Der Name muss nach OberflaecheFensterTests einsortiert sein (Bildlauf, CODEMAP).
/// </summary>
internal static class OberflaecheHoerprobeBilderTests
{
    private static readonly string[] Varianten = ["lautsprecher", "headset", "mithoeren", "aufnahme", "wiedergabe"];

    [Test]
    public static void Hoerprobe_Bilder_TexteSchalterUndKontrast()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var breiten = new List<double>();
                foreach (var variante in Varianten)
                {
                    using var a = new Aufbau();
                    var headset = variante is "headset" or "mithoeren";
                    a.Ablage.Aktuell.Audio.Hoerweise = headset ? Hoerweise.Headset : Hoerweise.Lautsprecher;
                    var h = a.Haupt(out _, out _, out var audio, out _);
                    audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                    // Wie bei Sarevoc: Sendenetz auf Taste, die Maschine hört nicht auf die Sprachaktivierung.
                    a.Maschine.Sprachaktivierung = false;
                    if (variante == "mithoeren") audio.Hoerprobe.Mithoeren = true;
                    if (variante == "aufnahme")
                    {
                        audio.Hoerprobe.ProbeCommand.Execute(null);
                        a.Maschine.Probe = new ProbeStand(ProbeZustand.Aufnahme, 3200);
                    }
                    if (variante == "wiedergabe") a.Maschine.Probe = new ProbeStand(ProbeZustand.Wiedergabe, 4100);
                    h.Takt();
                    var haupt = new Hauptfenster(h, a.Ablage);
                    fenster.Add(haupt);
                    var was = satz.Name + ", " + variante;

                    // Mithören unter Einstellungen > Senden > Feinheiten (E19).
                    Seiten.Feinheiten(h, Abschnittskennung.Senden);
                    _ = Ganz(haupt);
                    var einstellungen = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);
                    var schalter = Bildlauf.Nachfahren<CheckBox>(einstellungen).Single(c => c.Name == "MithoerenSchalter");
                    var wo = (FrameworkElement)schalter.Parent;
                    Pruefe.Gleich(headset, schalter.IsEnabled, was + ": Mithören nur mit Headset schaltbar");
                    Pruefe.Gleich(variante == "mithoeren", schalter.IsChecked == true, was + ": Schalter zeigt, ob Mithören an ist");
                    Pruefe.Wahr(Sichtbar(wo, "Beim Senden mithören"), was + ": Schalter beschriftet");
                    Pruefe.Wahr(Sichtbar(wo, variante switch
                    {
                        // Schalter aus: Der Text sagt es zuerst und verspricht nichts (Prüfung 0.4.1).
                        "headset" => "Aus. Eingeschaltet hörst du mit, was du sendest, gerade nur mit der Sprechtaste.",
                        "mithoeren" => "Gerade nur mit der Sprechtaste: Halte sie, um dich zu hören.",
                        _ => "Nur mit Headset. Über Lautsprecher",
                    }), was + ": Text zum Mithören");
                    if (variante != "mithoeren")
                        Pruefe.Falsch(Sichtbar(wo, "Du hörst mit") || Sichtbar(wo, "Halte sie"), was + ": ohne Mithören kein Versprechen, dass man sich hört");
                    // Gesperrt (Lautsprecher) zählt der Text im Schalter nicht (WCAG 1.4.3); dann bleibt die Überschrift.
                    KontrastPruefen(wo, satz, was + ", Mithören", mindestens: 1);
                    if (satz.Stellung == Farbvorschlaege.Voreinstellung) Bildablage.Ablegen(Ganz(haupt), "hoerprobe-mithoeren-" + variante);

                    // Die eine Hörprobe auf Start (Q75): Knopf ohne Zugriffstaste, daneben der Stand.
                    h.AktiverBereich = Seiten.Start(h);
                    var bild = Ganz(haupt);
                    var start = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is StartViewModel);
                    var zeile = Bildlauf.Nachfahren<StackPanel>(start).Single(s => s.Name == "HoerprobeZeile");
                    var knopf = Bildlauf.Nachfahren<Button>(zeile).Single();
                    var probe = variante is "aufnahme" or "wiedergabe";
                    Pruefe.Wahr(ZuSehen(zeile) && Sichtbar(knopf, probe ? "Abbrechen" : "Hörprobe"), was + ": Knopf " + (probe ? "Abbrechen" : "Hörprobe"));
                    Pruefe.Falsch(Bildlauf.Nachfahren<AccessText>(knopf).Any(t => t.AccessKey != '\0'), was + ": Hörprobe ohne Zugriffstaste (AP2)");
                    Pruefe.Wahr(Sichtbar(zeile, variante switch
                    {
                        "aufnahme" => "Aufnahme läuft, sprich jetzt. Noch 4 s.",
                        "wiedergabe" => "So hören dich die anderen. Noch 5 s.",
                        _ => StartViewModel.HoerprobeBereitText,
                    }), was + ": Text zur Probe");
                    Pruefe.Falsch(Bildlauf.Nachfahren<CheckBox>(zeile).Any(), was + ": kein Mithören neben der Hörprobe (E19)");
                    KontrastPruefen(zeile, satz, was + ", Hörprobe", mindestens: 2);
                    breiten.Add(zeile.ActualWidth);
                    if (satz.Stellung == Farbvorschlaege.Voreinstellung) Bildablage.Ablegen(bild, "hoerprobe-" + variante);
                }
                // Der Stand neben dem Knopf wechselt beim Klicken; die Breite der Zeile darf dabei nicht springen.
                PruefeWert.Hoechstens(1, breiten.Max() - breiten.Min(), satz.Name + ": Breite der Zeile in allen Ständen gleich (" + string.Join(", ", breiten) + ")");
            }
        });
    }
}
