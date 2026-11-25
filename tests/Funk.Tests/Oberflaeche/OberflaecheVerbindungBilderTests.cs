// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Verbindung und Anmeldung auf der Startseite (seit 0.6.0; bis 0.5.x ein eigener Bereich), im Speicher gezeichnet
/// (<see cref="Bildlauf"/>, ohne Fensterhandle, auch neben einem Spiel): mit eingebautem Profil einmal mit Fehler, dazu die
/// Anmeldung mit Namen (erster Start und Frage des Clients), der Code mit Rückfrage (Weg mit Bot), der abgelaufene Code und im
/// Admin-Setup eine während des Verbindens geänderte Adresse. Geprüft werden Bindungen, Sichtbarkeit und die Kontrastregeln an
/// den gezeichneten Elementen, in allen drei Farbstellungen; für Funker nie eine Adresse. Mit <c>FUNK_BILDER</c> entsteht je
/// Variante verbindung-&lt;variante&gt;.png (in Anthrazit). Die übrigen Zustände zeigt OberflaecheStartseiteTests.
/// </summary>
internal static class OberflaecheVerbindungBilderTests
{
    // "fehler": Das Profil ist mit einem anderen Profilschlüssel signiert; die Startseite sagt es mit Nummer, ohne Pfad.
    // "anmeldung": erster Start nach dem Setup, Namen vorbelegt, noch nicht bestätigt.
    // "antrag": Der Client fragt in der REGISTRIERUNG nach den Namen (A42).
    // "code": Weg mit Bot (3.4), Code und Rückfrage.
    // "abgelaufen" (Gegenprüfung): Nach dem dritten Code kommt keiner mehr; die Karte sagt es und bietet einen neuen an.
    // "geaendert" (Gegenprüfung, Admin-Setup): Der Client versucht es noch, die LAN-Adresse wird unter Details eingetragen.
    private static readonly string[] Varianten = ["fehler", "anmeldung", "antrag", "code", "abgelaufen", "geaendert"];

    private const string Kennung = "4F2A-81C0-77D3-9C31";

    [Test]
    public static void Verbindung_Varianten_BilderUndKontrast()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var k = new OberflaecheProfilTests.Schluessel();
            using var fremd = new OberflaecheProfilTests.Schluessel();
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                foreach (var variante in Varianten)
                {
                    using var a = new Aufbau(FunkLage.Leer);
                    a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
                    // Ein alter Gerätename in den Einstellungen (bis 0.6.2 der Windows-Name): Seit 0.6.3 zeigt und schickt ihn nichts mehr.
                    a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
                    a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
                    a.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
                    a.Ablage.Aktuell.Anmeldung.Bestaetigt = variante is not ("anmeldung" or "antrag");
                    var profil = a.Ordner.Datei("profil.json");
                    OberflaecheProfilTests.ProfilSchreiben(profil, variante == "fehler" ? fremd : k);
                    a.Profil = new Profilquelle(profil, [k.Spki]);
                    Funk.Verwaltung.Verwaltungsteil? teil = null;
                    if (variante == "geaendert")
                    {
                        var vd = new VerwaltungsdienstAttrappe();
                        teil = new Funk.Verwaltung.Verwaltungsteil(vd, new Funk.Verwaltung.Modelle.VerwaltungViewModel(vd));
                        a.Verwaltung = teil;
                    }
                    var h = a.Haupt(out var funkfeld, out _, out _, out _);
                    var vm = h.Verbindung;
                    Task<bool>? frage = null;
                    Task<Anmeldedaten?>? antrag = null;
                    switch (variante)
                    {
                        case "code" or "abgelaufen":
                            a.Dienst.LageSetzen(_ => FunkLage.Leer with
                            {
                                Status = Verbindungsstatus.Registrierung,
                                Server = "Meine Staffel",
                                Geraet = Kennung,
                                Schluesselart = "TPM",
                                Registrierung = new Registrierungsangabe("7K3M-Q9PX-2HDT", "/funk verknuepfen",
                                    variante == "abgelaufen" ? DateTimeOffset.Now.AddMinutes(-2) : DateTimeOffset.Now.AddMinutes(5)),
                            });
                            if (variante == "code")
                                frage = vm.RueckfrageAsync(new VerknuepfungKonto { Name = "Max", Benutzername = "max", Neu = true }, CancellationToken.None);
                            break;
                        case "antrag":
                            a.Dienst.LageSetzen(_ => FunkLage.Leer with
                            {
                                Status = Verbindungsstatus.Registrierung, Server = "Meine Staffel", Geraet = Kennung, Schluesselart = "TPM",
                            });
                            antrag = vm.AnmeldungAsync(CancellationToken.None);
                            break;
                        case "geaendert":
                            var meldung = Verbindungstexte.Aufbau(AufbauFehler.Netz, "203.0.113.7:8443 antwortet nicht");
                            a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Verbinden, Hinweis = meldung.Text, HinweisEinzelheit = meldung.Einzelheit });
                            vm.Adresse = "192.168.1.250";
                            break;
                    }
                    h.AktiverBereich = h.Bereiche[0];
                    var haupt = new Hauptfenster(h, a.Ablage);
                    fenster.Add(haupt);
                    var bild = Ganz(haupt);
                    var was = satz.Name + ", " + variante;

                    var inhalt = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is StartViewModel);
                    FrameworkElement Benannt(string name) => Bildlauf.Nachfahren<FrameworkElement>(inhalt).Single(b => b.Name == name);
                    Button Knopf(DependencyObject wo, string text) => Bildlauf.Nachfahren<Button>(wo).Single(b => b.Content is string s && s == text);
                    var verbinden = (Button)Benannt("VerbindenKnopf");
                    Pruefe.Gleich(variante == "abgelaufen", ZuSehen(Benannt("CodeAbgelaufenRand")), was + ": Hinweis auf den abgelaufenen Code");
                    Pruefe.Gleich(variante is "anmeldung" or "antrag", ZuSehen(Benannt("AnmeldungKarte")), was + ": Karte mit den Namen");
                    // Für Funker nie eine Adresse oder ein Port, auch nicht im Hinweis; im Admin-Setup stehen die Einzelheiten unter Details.
                    Pruefe.Falsch(Sichtbar(inhalt, "203.0.113.7"), was + ": die Adresse steht nicht auf der Startseite (Details: Einstellungen, Erweitert)");
                    Pruefe.Falsch(Sichtbar(inhalt, "Meine Staffel:") && variante != "geaendert", was + ": kein Servername");
                    KontrastPruefen(inhalt, satz, was, mindestens: 10);
                    if (satz.Stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(bild, "verbindung-" + variante);
                    // Ab hier ändern die Prüfungen das Modell (Eingaben, Antworten); Bild und Kontrast zeigen den Stand davor.
                    switch (variante)
                    {
                        case "fehler":
                            Pruefe.Falsch(vm.Bereit || verbinden.IsEnabled, was + ": kein Verbinden");
                            Pruefe.Wahr(ZuSehen(Benannt("WasFehltRand")), was + ": sagt, was fehlt");
                            Pruefe.Wahr(vm.WasFehlt is { } wf && wf.Contains("(Fehler 09)", StringComparison.Ordinal) && Sichtbar(inhalt, wf), was + ": mit Fehlernummer: " + vm.WasFehlt);
                            Pruefe.Falsch(Sichtbar(inhalt, a.Ordner.Pfad), was + ": kein Pfad");
                            break;
                        case "anmeldung":
                            Pruefe.Wahr(Sichtbar(inhalt, "Anmelden als Mustermann (Discord: Mustermann/Max) - stimmt das?"), was + ": Frage mit beiden Namen");
                            var los = Knopf(Benannt("AnmeldungKarte"), "So anmelden und verbinden");
                            Pruefe.Wahr(los.IsEnabled && ZuSehen(los), was + ": vor dem ersten Verbinden: anmelden und verbinden");
                            Pruefe.Wahr(Sichtbar(inhalt, "Im Antrag heißt dieses Gerät PC- und die ersten vier Zeichen seiner Gerätekennung"), was + ": Gerätename nur als Zeile im Antrag");
                            Pruefe.Falsch(Sichtbar(inhalt, "Gaming-PC"), was + ": nie der Windows-Name (0.6.3)");
                            Pruefe.Wahr(Bildlauf.Nachfahren<TextBox>(inhalt).Any(t => t.Name == "ScNameFeld" && t.Text == "Mustermann" && ZuSehen(t)), was + ": SC-Name aus dem Setup");
                            Pruefe.Wahr(Bildlauf.Nachfahren<TextBox>(inhalt).Any(t => t.Name == "DiscordFeld" && t.Text == "Mustermann/Max" && ZuSehen(t)), was + ": Discord-Name aus dem Setup");
                            vm.ScName = "S";
                            Pruefe.Wahr(vm.NamenFehler is not null && !vm.SoAnmeldenCommand.CanExecute(null), was + ": zu kurzer Name hält auf");
                            break;
                        case "antrag":
                            Pruefe.Wahr(vm.AnmeldungWartet && !antrag!.IsCompleted, was + ": Client wartet auf die Namen");
                            var karte = Benannt("AnmeldungKarte");
                            Pruefe.Wahr(ZuSehen(Knopf(karte, "So anmelden")) && ZuSehen(Knopf(karte, "Jetzt nicht")), was + ": So anmelden und Jetzt nicht");
                            vm.SoAnmeldenCommand.Execute(null);
                            Pruefe.Gleich(new Anmeldedaten("Mustermann", "Mustermann/Max"), antrag!.GetAwaiter().GetResult(), was + ": die Namen gehen hinaus");
                            Pruefe.Wahr(a.Ablage.Aktuell.Anmeldung.Bestaetigt && !vm.AnmeldungWartet, was + ": bestätigt, die Karte geht");
                            break;
                        case "code":
                            Pruefe.Wahr(vm.IstRegistrierung && vm.HatRueckfrage, was + ": Code und Rückfrage");
                            var code = Bildlauf.Nachfahren<TextBox>(inhalt).Single(t => t.Text == "7K3M-Q9PX-2HDT");
                            Pruefe.Wahr(code.IsReadOnly && ZuSehen(code), was + ": Code groß, nur lesbar, kopierbar");
                            Pruefe.Wahr(Sichtbar(inhalt, "/funk verknuepfen 7K3M-Q9PX-2HDT"), was + ": Befehl mit Code");
                            Pruefe.Wahr(ZuSehen(Benannt("RueckfrageKarte")), was + ": Rückfrage sichtbar");
                            vm.RueckfrageNeinCommand.Execute(null);
                            Pruefe.Falsch(frage!.GetAwaiter().GetResult(), was + ": Nein kommt an");
                            break;
                        case "geaendert":
                            Pruefe.Wahr(!ZuSehen(verbinden) && ((Button)Benannt("TrennenKnopf")).IsEnabled, was + ": während der Versuche Trennen, nicht Verbinden");
                            Pruefe.Wahr(Sichtbar(inhalt, "Server nicht erreichbar"), was + ": der Grund steht auf der Startseite");
                            Pruefe.Wahr(Sichtbar(inhalt, "(Fehler 11)"), was + ": Fehlernummer zum Weitersagen");
                            // Wann die Adresse gilt, steht unter Einstellungen, Erweitert (Schritt 3.5: Die Details sind nicht mehr auf der Startseite).
                            h.AktiverBereich = h.Bereiche.Single(b => b.Inhalt is EinstellungenViewModel);
                            _ = Ganz(haupt);
                            var einstellungen = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);
                            Pruefe.Wahr(Sichtbar(einstellungen, vm.AenderungWartet!) && Sichtbar(einstellungen, "203.0.113.7"), was + ": wann die Adresse gilt, unter Einstellungen, Erweitert");
                            h.AktiverBereich = h.Bereiche[0];
                            break;
                        default:
                            Pruefe.Wahr(vm.IstRegistrierung && vm.CodeAbgelaufen && !vm.HatRueckfrage, was + ": Code abgelaufen, keine Rückfrage");
                            var neu = Knopf(Benannt("CodeAbgelaufenRand"), "Neuen _Code holen");
                            Pruefe.Wahr(neu.IsEnabled && ZuSehen(neu), was + ": Knopf Neuen Code holen");
                            Pruefe.Wahr(Sichtbar(inhalt, "Abgelaufen um ") && Sichtbar(inhalt, "höchstens drei"), was + ": sagt, dass und warum");
                            break;
                    }
                    funkfeld.Dispose();
                    teil?.Dispose();
                }
            }
        });
    }
}
