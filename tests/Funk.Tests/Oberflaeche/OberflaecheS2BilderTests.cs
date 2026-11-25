// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Eingabe;
using Funk.Tests.Maschine;
using static Funk.Client.Dienste.FunkDienstAttrappe;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Bilder der Ausbaustufe S2 im Speicher (<see cref="Bildlauf"/>, ohne Fensterhandle, auch neben einem Spiel), in Anthrazit mit
/// Kontrastprüfung: Einstellungen mit "Heute als Funker" samt Rückfrage, den neuen Tasten (Melde-Taste, Funkfeld ablegen/holen,
/// Abschnitt Einsatzleitung) und dem Meldenetz; die Fußzeile im Funker-Modus (bis 0.11.1 dazu das Brett mit Phasenleiste, Rückfrage und einem
/// Admin "als Funker"); die Meldezeile mit Fehler, Leitung und Hinweis; das X des Funkfelds, das ablegt. Dazu die Zugriffstasten (eindeutig).
/// Mit FUNK_BILDER liegen die Bilder danach dort. Der Klassenname steht nach OberflaecheFensterTests, damit die App dort zuerst entsteht.
/// </summary>
internal static class OberflaecheS2BilderTests
{
    private static readonly Tastenbelegung F9 = Tastenbelegung.Taste(0x0043);
    private static readonly Tastenbelegung F10 = Tastenbelegung.Taste(0x0044);
    private static readonly Tastenbelegung Stick = new(EingabeArt.Joystick, 12, @"\\?\HID#VID_3344&PID_0194&MI_00#7&1a2b3c&0&0000#{4d1e55b2}");

    private static readonly string[] EinstellungenTexte =
    [
        FunkermodusViewModel.Titel, "Die Einsatzleitung kann dich einteilen.", "Einteilen können dann: Mustermann", "Ja, als Funker", "Beim Verbinden als Funker",
        "Melde-Taste", "Taste 0x0043", "Funkfeld ablegen/holen", "Taste 0x0044", "Melde-Taste spricht in", "ab Werk: Kommando", "Einsatzleitung", "Phase vor", "Phase zurück",
        "Wirken nur, solange du die Einsatzleitung hast", "Knöpfe und Tasten, die du hier belegst, sieht Star Citizen genauso",
        "Aus heißt: Es liegt beim Start im Infobereich",
    ];

    private static string Kurzname(Farbsatz satz) => satz.Stellung switch
    {
        _ => "anthrazit",
    };

    private static Aufbau AlsNordwindAufbau()
    {
        var a = new Aufbau(dienst: S2FunkerClientTests.AlsNordwind()) { Jetzt = Abend };
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
        a.Ablage.Aktuell.Tasten.Hinzufuegen(new Tastenzuordnung(Tastenfunktion.Melden, F9));
        a.Ablage.Aktuell.Tasten.Hinzufuegen(new Tastenzuordnung(Tastenfunktion.FunkfeldUmschalten, F10));
        a.Ablage.Aktuell.Tasten.Hinzufuegen(new Tastenzuordnung(Tastenfunktion.PhaseVor, Stick));
        a.Tastenquelle = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus, EingabeArt.Joystick);
        return a;
    }

    /// <summary>Den Bereich ganz zeichnen: so hoch, dass sein Bildlauf nichts verbirgt.</summary>
    private static System.Windows.Media.Imaging.RenderTargetBitmap BereichGanz(Hauptfenster haupt, double breite)
    {
        _ = Bildlauf.Zeichnen(haupt, 1, new Size(breite, 1000));
        var rollbar = Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(haupt)).First(sv => Bildlauf.Nachfahren<ContentControl>(sv).Any(c => c.Content is EinstellungenViewModel));
        var ganz = 1000 + Math.Max(0, rollbar.ExtentHeight - rollbar.ViewportHeight) + 8;
        return Bildlauf.Zeichnen(haupt, 1, new Size(breite, Math.Ceiling(ganz))).Bild;
    }

    [Test]
    public static void Einstellungen_FunkerTastenUndAblage_BilderKontrastZugriffstasten()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var was = satz.Name + ", Einstellungen S2";
                using var a = AlsNordwindAufbau();
                var h = a.Haupt(out var funkfeld, out _, out _, out _);
                // Seit 0.10.0 (AP3) stehen die Tasten und "Funkfeld beim Start" in den Feinheiten von Tasten und Funkfeld und Darstellung.
                Seiten.Feinheiten(h, Abschnittskennung.Tasten, Abschnittskennung.Funkfeld);
                h.Funkermodus.EinschaltenCommand.Execute(null);
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var bild = BereichGanz(haupt, 1120);
                var wurzel = Bildlauf.Wurzel(haupt);
                foreach (var text in EinstellungenTexte)
                    Pruefe.Wahr(Sichtbar(wurzel, text), was + ": \"" + text + "\" zu sehen");
                Pruefe.Wahr(Sichtbar(wurzel, "VID_3344&PID_0194"), was + ": der Joystickknopf mit Gerät");
                KontrastPruefen(wurzel, satz, was, mindestens: 60);
                Bildablage.Ablegen(bild, "einstellungen-s2-" + Kurzname(satz));

                // Zugriffstasten: die neuen Zeilen bringen keine doppelten.
                if (satz.Stellung == Farbvorschlaege.Voreinstellung)
                {
                    var tasten = OberflaecheHauptfensterTests.Zugriffstasten(wurzel);
                    var doppelt = OberflaecheHauptfensterTests.Doppelte(tasten, "Einstellungen").ToList();
                    Pruefe.Gleich(0, doppelt.Count, "doppelte Zugriffstasten: " + string.Join(" | ", doppelt));
                }

                // Eingeschaltet: Fußzeile "als Funker bis 04:00", Knopf "Wieder Admin".
                h.Funkermodus.JaCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                var (an, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1120, 760));
                // Die Uhrzeit hängt an der echten Uhr (mit Wochentag, wenn 04:00 weiter als 20 h weg ist); der Satz davor nicht.
                var stand = h.Funkermodus.StandText ?? "";
                Pruefe.Wahr(stand.StartsWith("als Funker bis ", StringComparison.Ordinal) && stand.EndsWith("04:00", StringComparison.Ordinal), was + ": " + stand);
                Pruefe.Wahr(Sichtbar(wurzel, stand) && Sichtbar(wurzel, "Wieder Admin") && Sichtbar(wurzel, "Läuft: " + stand),
                    was + ": Fußzeile und Wieder Admin");
                KontrastPruefen(wurzel, satz, was + ", als Funker", mindestens: 30);
                Bildablage.Ablegen(an, "einstellungen-als-funker-" + Kurzname(satz));
                funkfeld.Dispose();
            }
        });
    }

    // 0.11.2: Gelöscht ist Brett_PhasenleisteRueckfrageUndAdminAlsFunker_BilderKontrast (Phasenleiste mit Rückfrage und die Marke "als Funker"
    // an der Karte auf dem Brett, gezeichnet in Anthrazit). Die Raid-Seite zeigt seit 0.11.2 weder Phasenleiste noch Marken an den Namen (Sarevoc 13.10.2026,
    // Stammbaum); das Schalten der Phasen prüfen weiter S2BrettPhasenTests (Modell) und die Phasentasten (S2PhasenClientTests).

    /// <summary>
    /// Die Meldezeile in jeder Farbstellung (Bild meldezeile-anthrazit): Fehler, Leitung und Hinweis gemeldet. Seit Sarevoc Q112 steht immer nur
    /// eine Meldung da, die ranghöchste (bis dahin alle drei untereinander). Das Bild zeigt die drei Stände untereinander: alle drei gemeldet
    /// (der Fehler, rot), der Fehler gelöst (die Leitung), die Leitung gelöst (der Hinweis); jeder Stand genau ein Satz in einer Zeile, alle
    /// gleich hoch, mit Kontrast.
    /// </summary>
    [Test]
    public static void Meldezeile_FehlerLeitungHinweis_BilderKontrast()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            const string Fehler = "Nicht gesendet: kein Senderecht mehr in Crew 1 (Kobold)";
            const string Leitung = "Phase 2/3 ENTERN? Nochmal drücken - 9 Funker, ausgelassen: Nordwind (Admin)";
            // Seit 0.11.2 erscheint nur, was Meldezeile.Erlaubt nennt; der Hinweis ist eine Einteilung (bis 0.11.1 "Sendekanal: Crew 2").
            const string Hinweis = "Eingeteilt von Kobold: Schiff 2, Gunner";
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var was = satz.Name + ", Meldezeile";
                long ms = 0;
                var zeile = new Meldezeile(() => ms);
                // Seit 0.11.0 der Urheber in Klammern (bis dahin " - verschoben durch Kobold"), wie ihn Teilnehmermeldungen setzt. Der Fehler zuerst:
                // Leitung und Hinweis warten dann hinter ihm (Q112; umgekehrt ersetzte jede höhere die stehende, und nichts wartete).
                zeile.Zeigen("entzug", Fehler, Meldeart.Fehler, Meldezeiten.EntzugMs);
                zeile.Zeigen("phase", Leitung, Meldeart.Leitung, Meldezeiten.ScharfMs);
                zeile.Zeigen(Teilnehmermeldungen.UmstellungSchluessel, Hinweis, Meldeart.Hinweis, Meldezeiten.UmstellungMs);
                var monitor = new Bildschirm(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1040), 1, true);
                var w = new MeldezeileFenster(zeile, new FesteBildschirme([monitor]));
                fenster.Add(w);
                var staende = new List<(string Titel, System.Windows.Media.Imaging.RenderTargetBitmap Bild, Size Groesse)>();
                foreach (var (titel, erwartet, danach) in new[]
                {
                    ("Fehler, Leitung und Hinweis gemeldet: der Fehler", Fehler, "entzug"),
                    ("Fehler gelöst: die Leitung", Leitung, "phase"),
                    ("Leitung gelöst: der Hinweis", Hinweis, Teilnehmermeldungen.UmstellungSchluessel),
                })
                {
                    var (bild, groesse) = Bildlauf.Zeichnen(w);
                    var wurzel = Bildlauf.Wurzel(w);
                    var saetze = Bildlauf.Nachfahren<TextBlock>(wurzel).Where(t => t.Name == "Satz" && ZuSehen(t)).ToList();
                    Pruefe.Gleich(erwartet, string.Join("|", saetze.Select(Inhalt)), was + ", " + titel + ": genau eine Meldung");
                    Pruefe.Gleich(erwartet, zeile.Text, was + ", " + titel + ": Text des Modells");
                    var rot = Farbe(saetze[0].Foreground) == satz.RotSchrift;
                    Pruefe.Gleich(erwartet == Fehler, rot, was + ", " + titel + ": rot nur der Fehler");
                    Pruefe.Wahr(groesse.Width <= 641 && groesse.Height < 40, was + ", " + titel + ": klein, eine Zeile (" + groesse + ")");
                    KontrastPruefen(wurzel, satz, was + ", " + titel, mindestens: 1);
                    staende.Add((titel, bild, groesse));
                    zeile.Loesen(danach);
                }
                Pruefe.Falsch(zeile.Sichtbar, was + ": ohne Meldung unsichtbar");
                Pruefe.Wahr(staende.Max(s => s.Groesse.Height) - staende.Min(s => s.Groesse.Height) < 0.5, was + ": jeder Stand gleich hoch, eine Zeile");

                // Ein Bogen: die drei Stände untereinander, je mit Überschrift.
                const double Rand = 12;
                const double Kopf = 18;
                var breite = Math.Max(staende.Max(s => s.Groesse.Width), 300) + (2 * Rand);
                var hoehe = Rand + staende.Sum(s => Kopf + s.Groesse.Height + Rand);
                var bogen = new Bogen(breite, hoehe, 1, OberflaecheFunkfeldBilderTests.Grund);
                var y = Rand;
                foreach (var s in staende)
                {
                    bogen.Text(s.Titel, Rand, y, 11, OberflaecheFunkfeldBilderTests.Grau);
                    bogen.Bild(s.Bild, Rand, y + Kopf, s.Groesse.Width, s.Groesse.Height);
                    y += Kopf + s.Groesse.Height + Rand;
                }
                Bildablage.Ablegen(bogen.Fertig(), "meldezeile-" + Kurzname(satz));
            }
        });
    }

    [Test]
    public static void Funkfeld_XLegtAb_TooltipNenntDieTaste()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau();
            var f = a.Funkfeld();
            f.AblegenTooltip = FunkfeldAblage.AblegenTooltip(["F10"]);
            var w = new FunkfeldFenster(f, a.Ablage, new FesteBildschirme([new Bildschirm(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1040), 1, true)]));
            fenster.Add(w);
            _ = Bildlauf.Zeichnen(w);
            var x = Bildlauf.Nachfahren<Button>(Bildlauf.Wurzel(w)).Single(k => k.Command == SystemCommands.CloseWindowCommand);
            Pruefe.Gleich("Funkfeld ablegen. Zurück mit F10 oder dem Symbol im Infobereich.", x.ToolTip as string, "der Tooltip nennt die Taste");
            Pruefe.Gleich("Funkfeld ablegen", AutomationProperties.GetName(x), "für Vorleser");
            Pruefe.Gleich("Funkfeld ablegen. Zurück mit dem Symbol im Infobereich oder Strg+F im Hauptfenster.", FunkfeldAblage.AblegenTooltip([]), "ohne Taste");
            f.Dispose();
        });
    }
}
