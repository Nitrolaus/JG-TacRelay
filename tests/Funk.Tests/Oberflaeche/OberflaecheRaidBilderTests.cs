// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Ablage;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using Gong = GongSolutions.Wpf.DragDrop.DragDrop;
using static Funk.Client.Dienste.FunkDienstAttrappe;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Bilder der Raidleitung im Speicher (<see cref="Bildlauf"/>, ohne Fensterhandle, auch neben einem Spiel): die Raid-Seite der Einsatzleitung
/// über dem Beispielabend (seit 0.11.2 Namen links, Reiter und Stammbaum; bis 0.11.1 das Brett mit Plätzen und dazu die Karte "Einsteigen" auf Start),
/// in Anthrazit mit Kontrastprüfung, dazu die Zugriffstasten und die Verdrahtung des Ziehens; die Verwaltung mit Flotte und Einsatzleitung.
/// Mit FUNK_BILDER liegen die Bilder danach dort.
/// Der Klassenname steht nach OberflaecheFensterTests, damit die App dort zuerst entsteht.
/// </summary>
internal static class OberflaecheRaidBilderTests
{
    private static readonly string[] BrettTexte =
    [
        "LIVE - wirkt sofort", "Einsatzleitung bis 04:00 (von Nordwind): Op Nachtfalke", "Teilnehmer", "Kobold", "Nordwind", "Pollux", "Zinnober",
        "0 Brett-Probe", "Marine", "Brücke", "Crew", "Ruhe", "Schiff 3", "19 weitere Ch.",
    ];

    private static string Kurzname(Farbsatz satz) => satz.Stellung switch
    {
        _ => "anthrazit",
    };

    private static Aufbau Aufbauen(FunkDienstAttrappe dienst)
    {
        var a = new Aufbau(dienst: dienst);
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        // Ein erfundener Gerätename, sonst stünde der Name dieses Rechners im Bild.
        a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
        return a;
    }

    /// <summary>
    /// Die Raid-Seite seit 0.11.2 (Sarevoc 13.10.2026, Stammbaum) in Anthrazit über dem Beispielabend mit dem Kanalnetz Brücke (Gruppe Schiffe): links
    /// alle Namen mit der Zahl ihrer Kanäle, oben die Reiter, darunter der Stammbaum (Kanal, Ruhe, besetzte Ch., "weitere Ch.", die Leute). Kontrast,
    /// Zugriffstasten eindeutig und die Verdrahtung des Ziehens: Quellen sind die Namen links und die Leute im Baum (mit dem Ziehen des Bretts),
    /// Ziele die Liste links, jeder Kanal und jeder Ch.; ein Menü an den Namen gibt es nicht mehr. Bis 0.11.1 zeigte dieses Bild das Brett mit
    /// Spalten, Plätzen, Detailfeld, Kanalliste und Verlauf.
    /// </summary>
    [Test]
    public static void Brett_Stammbaum_BilderKontrastZugriffstastenUndZiehen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var was = satz.Name + ", Raid-Seite";
                var d = MitRaidabend();
                d.LageSetzen(BrettBaumTests.MitKanalBruecke);
                using var a = Aufbauen(d);
                var h = a.Haupt(out _, out _, out _, out _);
                var b = h.Brett;
                b.KartenAbwartenAsync().GetAwaiter().GetResult();
                h.BereichWaehlenCommand.Execute("5");
                Pruefe.Wahr(h.IstBrett && h.SeitenInhalt is null && ReferenceEquals(h.BrettInhalt, b), was + ": Strg+5 zeigt das Brett auf eigenem Platz");
                // Seit 0.11.3: Rollenwahl vorn (Brett-Probe), daneben ihre Kanäle als Reiter; offen ist Brücke.
                b.ReiterWaehlenCommand.Execute(b.Reiter.Single(r => r.Name == "Brücke"));

                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
                var ansicht = Bildlauf.Nachfahren<BrettAnsicht>(Bildlauf.Wurzel(haupt)).Single();
                Pruefe.Wahr(ZuSehen(ansicht), was + ": Brett zu sehen");
                Pruefe.Falsch(Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(haupt)).Any(s => s.Content is StackPanel p && p.Children.OfType<Border>().Any(x => x.Name == "ErststartKarte") && ZuSehen(s)),
                    was + ": der Bildlauf der übrigen Bereiche ist weg");
                foreach (var text in BrettTexte)
                    Pruefe.Wahr(Sichtbar(ansicht, text), was + ": \"" + text + "\" zu sehen");
                // Die Zahl der Kanäle rechts im Namen (Sarevoc: "1 heißt in einem Kanal"): Brandung sitzt in Kommando und Brücke.
                var zahl = Bildlauf.Nachfahren<TextBlock>(ansicht).Single(t => t.Name == "Zahl" && t.DataContext is BrettKarte { KontoId: Brandung });
                Pruefe.Wahr(ZuSehen(zahl) && zahl.Text == "2", was + ": Brandung mit der Zahl 2 (" + zahl.Text + ")");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(ansicht).Single(t => t.Name == "Zahl" && t.DataContext is BrettKarte { KontoId: Zinnober }).Text.Length == 0,
                    was + ": ohne Kanal keine Zahl");
                KontrastPruefen(ansicht, satz, was, mindestens: 30);
                Bildablage.Ablegen(bild, "brett-" + Kurzname(satz));

                // Verdrahtung des Ziehens: die Namen links und die Leute an jedem Ch. und Kanal sind Quelle mit dem Ziehen des Bretts, Ziele sind
                // die Liste links, jeder Kanal-Reiter, der Kanal darunter und jeder seiner Ch.
                var quellen = Bildlauf.Nachfahren<ItemsControl>(ansicht).Where(Gong.GetIsDragSource).ToList();
                var namen = quellen.Where(q => q.Name is "NamenLiveListe" or "NamenOfflineListe").ToList();
                Pruefe.Gleich(2, namen.Count, was + ": die zwei Namenlisten sind Quelle");
                Pruefe.Wahr(quellen.All(q => ReferenceEquals(Gong.GetDragHandler(q), b.Ziehen)), was + ": jede Quelle zieht mit dem Brett");
                var chs = b.Baum.Where(k => k.Offen).SelectMany(k => k.Ch).ToList();
                Pruefe.Wahr(chs.Count >= 3 && chs.All(c => quellen.Any(q => ReferenceEquals(q.ItemsSource, c.Leute))), was + ": die Leute jedes Ch. sind Quelle (" + chs.Count + " Ch.)");
                var ziele = Bildlauf.Nachfahren<UIElement>(ansicht).Where(Gong.GetIsDropTarget).Select(Gong.GetDropHandler).ToHashSet();
                Pruefe.Wahr(ziele.Contains(b.Teilnehmerziel) && b.Reiter.All(ziele.Contains) && b.Baum.All(ziele.Contains) && chs.All(ziele.Contains),
                    was + ": Liste links, Reiter, jeder Kanal und jeder Ch. verdrahtet");

                // Kein Menü an einem Namen (Sarevoc 13.10.2026: "kein Menü bei einem Funker").
                var menue = new ContextMenu();
                BrettAnsicht.Fuellen(menue, b);
                Pruefe.Gleich(0, menue.Items.Count, was + ": kein Menü an den Namen");

                // Zugriffstasten: eindeutig, auch mit "Wer ist da".
                if (satz.Stellung == Farbvorschlaege.Voreinstellung)
                {
                    h.WerIstDa.Offen = true;
                    _ = Bildlauf.Zeichnen(haupt, 1, new Size(1600, 1000));
                    var tasten = OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt));
                    var da = tasten.Select(t => t.Taste).ToHashSet();
                    Pruefe.Wahr(da.IsSupersetOf(['R', 'E', 'S']), "Brett: Rückgängig, Abend beenden und Suchen mit Taste: " + string.Join("", da.Order()));
                    var doppelt = OberflaecheHauptfensterTests.Doppelte(tasten, "Brett").ToList();
                    Pruefe.Gleich(0, doppelt.Count, "doppelte Zugriffstasten: " + string.Join(" | ", doppelt));
                }
            }
        });
    }

    private static readonly string[] VerwaltungsAnsichten =
        ["flotte-einheiten", "flotte-rollen", "flotte-netze", "flotte-vorlage", "flotte-phasen", "einsatzleitung", "heute", "werkzeuge"];

    /// <summary>Was der Reiter Phasen (S2, C1) zeigen muss: Liste, Rollen mit Variante, Betroffene, Felder-Editor, Vorschau.</summary>
    private static readonly string[] PhasenTexte =
    [
        "Phasen", "1 Grundstellung", "2 Entern", "ENTERN, 1 Rolle mit Variante, läuft gerade", "Neue Phase", "Phase löschen", "Rollen in dieser Phase",
        "Diese Phase läuft gerade", "Variante anlegen", "wie Grundstellung", "eigene Variante, 8 Felder", "Wen sie trifft: Kapitän (alle Schiffe)",
        "Kapitän in Phase 2 Entern", "Feld 10: Global, fest", "So sieht Kapitän auf Schiff 2 in Phase 2 Entern aus",
        // Seit 0.8.0 steht der Entwurf in der Leiste unten; "Was ändert sich?" nennt je Änderung, was sich ändert (vorher "geändert: Phase 2 Entern").
        "1 Änderung nicht übernommen", "Phase 2 Entern: Variante für Kapitän geändert",
    ];

    /// <summary>
    /// Flotte und Einsatzleitung im Verwaltungsteil, im Hauptfenster wie mit dem Admin-Setup gezeichnet, in Anthrazit mit
    /// Kontrast und ohne Bindungsfehler. Die Flotte hält der Zustandsschreiber der Attrappe (eine Schiffsflotte); seit 19.10.2026 zeigt
    /// "flotte-vorlage" die Vorlage Star Citizen daneben im Entwurf, Reiter Kanäle mit Brücke.
    /// </summary>
    [Test]
    public static void Verwaltung_FlotteUndEinsatzleitung_BilderUndKontrast()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                foreach (var ansichtName in VerwaltungsAnsichten)
                {
                    var was = satz.Name + ", " + ansichtName;
                    using var ordner = new Testordner();
                    var d = new VerwaltungsdienstAttrappe { Ablageordner = ordner.Pfad, Eingeteilte = 12 };
                    var vm = new Funk.Verwaltung.Modelle.VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
                    using var teil = new Funk.Verwaltung.Verwaltungsteil(d, vm);
                    var f = vm.Flotte;
                    var command = d.Flotte.NetzListe!.Netze.Single(n => n.Name == "Kommando");
                    d.NetzMitglieder[command.Id] = 4;
                    switch (ansichtName)
                    {
                        case "flotte-einheiten":
                            vm.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Flotte;
                            f.BausteinTruppCommand.Execute(null);
                            f.SpeichernCommand.Execute(null);
                            break;
                        case "flotte-rollen":
                            vm.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Flotte;
                            f.Reiter = Funk.Verwaltung.Modelle.FlotteReiter.Rollen;
                            f.GewaehlteRolle = f.Rollen.Single(r => r.Name == "Kapitän");
                            f.Rolle!.VorschauEinheit = f.Rolle.VorschauEinheiten.Single(x => x.Text == "Schiff 2");
                            f.Rolle.Felder[5].Quelle = f.Rolle.Quellen.Single(q => q.Text == "Kommando");
                            // Seit 0.8.0 der Funkfeld-Editor: Feld 3 (Crew der eigenen Einheit) angeklickt, rechts oder darunter sein Formular.
                            f.Rolle.GewaehltesFeld = f.Rolle.Felder[2];
                            break;
                        case "flotte-netze":
                            vm.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Flotte;
                            f.Reiter = Funk.Verwaltung.Modelle.FlotteReiter.Netze;
                            f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Kommando");
                            f.Netz!.Name = "Flottenkommando";
                            f.UebernehmenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                            break;
                        case "flotte-vorlage":
                            // Die Vorlage Star Citizen (Werkzeuge) neben die Schiffsflotte in den Entwurf, dann ihr Kanal Brücke mit den Ch.
                            vm.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Flotte;
                            f.VorlageStarCitizenCommand.Execute(null);
                            f.Reiter = Funk.Verwaltung.Modelle.FlotteReiter.Netze;
                            f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Brücke");
                            break;
                        case "flotte-phasen":
                            // Phase 2 Entern mit einer Variante des CO übernehmen, dann läuft sie, und die Variante ändert sich noch einmal.
                            vm.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Flotte;
                            f.PhaseNeuCommand.Execute(null);
                            f.Phase!.Name = "Entern";
                            f.Phase.Kurz = "ENTERN";
                            f.Phase.GewaehlteRolle = f.Phase.Rollen.Single(r => r.Name == "Kapitän");
                            f.Phase.VarianteAnlegenCommand.Execute(null);
                            f.UebernehmenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                            if (f.Rueckfrage is not null) f.JaUebernehmenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                            d.LaufendePhase = 2;
                            vm.Aktualisieren();
                            f.Phase!.Variante!.Felder[8].Quelle = f.Phase.Variante.Quellen[0];
                            f.Phase.Variante.VorschauEinheit = f.Phase.Variante.VorschauEinheiten.Single(x => x.Text == "Schiff 2");
                            f.WasAendertSichCommand.Execute(null);
                            break;
                        case "heute":
                            // Eine neue Rolle ohne Felder und ohne Einheit: zwei Hinweise; dazu der offene Entwurf.
                            vm.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Flotte;
                            f.RolleNeuCommand.Execute(null);
                            vm.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Heute;
                            break;
                        case "werkzeuge":
                            f.SpeichernCommand.Execute(null);
                            vm.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Werkzeuge;
                            f.Leeren.FragenCommand.Execute(null);
                            break;
                        default:
                            vm.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Einsatzleitung;
                            vm.Einsatzleitung.Konto = vm.Einsatzleitung.Kandidaten.Single(k => k.Text.StartsWith("Kranich", StringComparison.Ordinal));
                            vm.Einsatzleitung.Grund = "Op Nachtfalke";
                            break;
                    }
                    using var a = Aufbauen(new FunkDienstAttrappe(FunkDienstAttrappe.Beispiel()));
                    a.Verwaltung = teil;
                    var h = a.Haupt(out _, out _, out _, out _);
                    a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = AdminStatus.Aktiv } });
                    h.AktiverBereich = h.Bereiche.Single(b => b.Kennung == HauptViewModel.VerwaltungKennung);
                    var haupt = new Hauptfenster(h, a.Ablage);
                    fenster.Add(haupt);
                    var bild = Ganz(haupt, 1280);
                    var ansicht = Bildlauf.Nachfahren<Funk.Verwaltung.Ansichten.VerwaltungAnsicht>(Bildlauf.Wurzel(haupt)).Single();
                    switch (ansichtName)
                    {
                        case "flotte-einheiten":
                            // Seit 0.8.0 stehen die gespeicherten Stände unter Werkzeuge (Ansicht "werkzeuge"), die Annahmen unter "Was ändert sich?".
                            Pruefe.Wahr(Sichtbar(ansicht, "Annahmen, bitte prüfen") && Sichtbar(ansicht, "Trupp Bravo") && Sichtbar(ansicht, "Truppkanal der eigenen Einheit")
                                && Sichtbar(ansicht, "Einheitskanäle") && Sichtbar(ansicht, "Plätze") && Sichtbar(ansicht, "Entwurf gespeichert"), was + ": Einheit, Annahmen, Meldung");
                            break;
                        case "flotte-rollen":
                            Pruefe.Wahr(Sichtbar(ansicht, "Feld 1 und Feld 6 nutzen beide Kommando") && Sichtbar(ansicht, "So sieht Kapitän auf Schiff 2 aus")
                                && Sichtbar(ansicht, "Feld 10: Global, fest") && Sichtbar(ansicht, "Crew der eigenen Einheit"), was + ": Felder, Prüfung, Vorschau");
                            Pruefe.Wahr(Sichtbar(ansicht, "Funkfeld von Kapitän") && Sichtbar(ansicht, "Schiffsflotte") && Sichtbar(ansicht, "Feld 3")
                                && Sichtbar(ansicht, "Rolle verdoppeln") && Sichtbar(ansicht, "zuerst senden"), was + ": Funkfeld-Editor");
                            FunkfeldGanz(ansicht, was);
                            break;
                        case "flotte-netze":
                            Pruefe.Wahr(Sichtbar(ansicht, "Betrifft 12 eingeteilte Personen") && Sichtbar(ansicht, "Ja, übernehmen")
                                && Sichtbar(ansicht, "Jeder darf ihn selbst dazunehmen") && Sichtbar(ansicht, "4 Mitglieder"), was + ": Rückfrage und Kanal");
                            Pruefe.Wahr(Sichtbar(ansicht, "Verwendet in") && Sichtbar(ansicht, "Rolle Kapitän (Schiffsflotte): Feld 1")
                                && Sichtbar(ansicht, "1 Änderung nicht übernommen"), was + ": Verwendet in, Leiste");
                            break;
                        case "flotte-vorlage":
                            Pruefe.Wahr(Sichtbar(ansicht, "Brücke") && Sichtbar(ansicht, "Star Citizen") && Sichtbar(ansicht, "Verwendet in")
                                && f.Netz?.Name == "Brücke" && f.Rollen.Count(r => r.Name == "Kapitän") == 2, was + ": die Vorlage neben der Flotte, Brücke gewählt");
                            break;
                        case "heute":
                            Pruefe.Wahr(Sichtbar(ansicht, "Was ansteht") && Sichtbar(ansicht, "2 Anträge warten auf Freigabe.") && Sichtbar(ansicht, "Entwurf: 1 Änderung nicht übernommen.")
                                && Sichtbar(ansicht, "Die Rolle Neue Rolle hat keine Felder") && Sichtbar(ansicht, "Die Rolle Neue Rolle braucht eine Einheit")
                                && Sichtbar(ansicht, "Es läuft eine Einsatzleitung: Falke."), was + ": was ansteht und Hinweise");
                            break;
                        case "werkzeuge":
                            Pruefe.Wahr(Sichtbar(ansicht, "Vorlage Star Citizen ...") && Sichtbar(ansicht, "Baustein Trupp ...") && Sichtbar(ansicht, "Gespeicherte Stände")
                                && Sichtbar(ansicht, "Leer (nur Global) ...") && Sichtbar(ansicht, "Wirklich alles bis auf Global") && Sichtbar(ansicht, "Ja, leeren"),
                                was + ": Werkzeuge mit Erklärung und Rückfrage");
                            break;
                        case "flotte-phasen":
                            foreach (var text in PhasenTexte)
                                Pruefe.Wahr(Sichtbar(ansicht, text), was + ": \"" + text + "\" zu sehen");
                            FeldauswahlGanz(ansicht, was);
                            break;
                        default:
                            Pruefe.Wahr(Sichtbar(ansicht, "Laufende und geplante Aufträge") && Sichtbar(ansicht, "Falke") && Sichtbar(ansicht, "Op Nachtfalke")
                                && Sichtbar(ansicht, "Kranich leitet ab jetzt bis"), was + ": Liste und Vorschau");
                            break;
                    }
                    KontrastPruefen(ansicht, satz, was, mindestens: 20);
                    Bildablage.Ablegen(bild, "verwaltung-" + ansichtName + "-" + Kurzname(satz));
                }
            }
        });
    }

    /// <summary>
    /// Die Auswahl je Feld (Flotte, Rollen, "Felder wie im Funkfeld") zeigt ihren Text ganz, bei 1280 Bildpunkten auch "Crew der
    /// eigenen Einheit" und "Brücke der eigenen Einheit". Befund der Prüfung 0.3.0 (niedrig): Das UniformGrid mit fünf Spalten
    /// schnitt beide rechts ab, und der Bildtest prüfte nur, ob der Text sichtbar ist. Gemessen wird der Text gegen den Platz, den
    /// die Vorlage der Auswahl ihm lässt (rechts 32 Bildpunkte für den Pfeil), nicht gegen seine eigene Breite.
    /// </summary>
    private static void FeldauswahlGanz(DependencyObject ansicht, string was)
    {
        var auswahl = Bildlauf.Nachfahren<ComboBox>(ansicht).Where(c => c.DataContext is Funk.Verwaltung.Modelle.FeldBearbeitung && ZuSehen(c)).ToList();
        Pruefe.Wahr(auswahl.Count == 9, was + ": neun Felder (" + auswahl.Count + ")");
        var lang = 0;
        foreach (var c in auswahl)
        {
            var inhalt = (ContentPresenter)c.Template.FindName("Inhalt", c);
            foreach (var t in Bildlauf.Nachfahren<TextBlock>(inhalt).Where(ZuSehen))
            {
                var r = t.TransformToAncestor(c).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight));
                Pruefe.Wahr(r.Right <= c.ActualWidth - 32 + 0.5 && r.Bottom <= c.ActualHeight + 0.5,
                    $"{was}: \"{Inhalt(t)}\" passt nicht in die Auswahl ({r.Right:0} von {c.ActualWidth - 32:0} breit, {r.Bottom:0} von {c.ActualHeight:0} hoch)");
                if (Inhalt(t).Contains("der eigenen Einheit", StringComparison.Ordinal)) lang++;
            }
        }
        Pruefe.Wahr(lang >= 2, was + ": beide langen Texte geprüft (" + lang + ")");
    }

    /// <summary>
    /// Seit 0.8.0 der Funkfeld-Editor der Rollen (statt neun Auswahlfeldern im 2x5): neun Kacheln und Global fest rechts unten, jeder Text
    /// ganz in seiner Kachel (umbrochen, nicht abgeschnitten), genau ein Feld angeklickt, und dessen Auswahl daneben (Feld 3, "Crew der
    /// eigenen Einheit") zeigt ihren Text ganz, gemessen wie in <see cref="FeldauswahlGanz"/>.
    /// </summary>
    private static void FunkfeldGanz(DependencyObject ansicht, string was)
    {
        var kacheln = Bildlauf.Nachfahren<Button>(ansicht).Where(b => b.Name == "Kachel" && ZuSehen(b)).ToList();
        Pruefe.Wahr(kacheln.Count == 9 && kacheln.All(k => k.DataContext is Funk.Verwaltung.Modelle.FeldBearbeitung), was + ": neun Kacheln (" + kacheln.Count + ")");
        Pruefe.Gleich(1, kacheln.Count(k => k.BorderThickness.Left >= 2), was + ": genau ein Feld angeklickt");
        foreach (var k in kacheln)
        {
            foreach (var t in Bildlauf.Nachfahren<TextBlock>(k).Where(ZuSehen))
            {
                var r = t.TransformToAncestor(k).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight));
                Pruefe.Wahr(r.Left >= -0.5 && r.Right <= k.ActualWidth + 0.5 && r.Bottom <= k.ActualHeight + 0.5,
                    $"{was}: \"{Inhalt(t)}\" ragt aus seiner Kachel ({r.Right:0} von {k.ActualWidth:0} breit, {r.Bottom:0} von {k.ActualHeight:0} hoch)");
            }
        }
        Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(ansicht).Count(t => ZuSehen(t) && Inhalt(t) == "Feld 10: Global, fest") == 1, was + ": Global fest rechts unten");
        var auswahl = Bildlauf.Nachfahren<ComboBox>(ansicht).Where(c => c.DataContext is Funk.Verwaltung.Modelle.FeldBearbeitung && ZuSehen(c)).ToList();
        Pruefe.Gleich(1, auswahl.Count, was + ": eine Auswahl für das angeklickte Feld");
        var inhalt = (ContentPresenter)auswahl[0].Template.FindName("Inhalt", auswahl[0]);
        var texte = Bildlauf.Nachfahren<TextBlock>(inhalt).Where(ZuSehen).ToList();
        Pruefe.Wahr(texte.Any(t => Inhalt(t).Contains("der eigenen Einheit", StringComparison.Ordinal)), was + ": Feld 3 zeigt Crew der eigenen Einheit");
        foreach (var t in texte)
        {
            var r = t.TransformToAncestor(auswahl[0]).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight));
            Pruefe.Wahr(r.Right <= auswahl[0].ActualWidth - 32 + 0.5 && r.Bottom <= auswahl[0].ActualHeight + 0.5,
                $"{was}: \"{Inhalt(t)}\" passt nicht in die Auswahl ({r.Right:0} von {auswahl[0].ActualWidth - 32:0} breit)");
        }
    }

    // 0.11.2: Gelöscht ist Einsteigen_BilderUndKontrast (die Karte Einsteigen auf Start in Anthrazit, auch gesperrt). Seit 0.11.2 steht sie nicht mehr
    // auf Start (Sarevoc 13.10.2026: "auf der Startseite nur das Audio, nichts weiter"); das Modell prüft weiter OberflaecheEinsteigenTests.
}
