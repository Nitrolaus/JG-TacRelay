// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Verarbeitung;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Zuordnung 0.11.0, Punkte 2 und 3, im Speicher gezeichnet (Bildlauf) bei 175 % (Sarevocs Bildschirm), in Anthrazit, über dem
/// Beispielabend der Attrappe (Namen erfunden). Bis 0.11.1 dazu "Ch." je Feld im Detail des Bretts (Q46) mit dem Raster; seit 0.11.2 zeigt
/// die Raid-Seite kein Detailfeld mehr. Die Ansage beim Login in der Meldezeile ("Eingeteilt von Ferox: Schiff 1, Gunner", seit
/// der einzeiligen Meldezeile in einer Zeile und höchstens so breit wie das Funkfeld) über den echten Weg (Teilnehmermeldungen nach
/// Trennung und Abgleich), dazu das Funkfeld mit der Platzrolle oben links statt "Rolle wählen". Geprüft: Kontrast, nichts abgeschnitten,
/// Zugriffstasten eindeutig, nur einfache Zeichen. Mit <c>FUNK_BILDER</c>
/// entsteht tr0110-ansage-login, je mit -anthrazit. Der Klassenname steht nach OberflaecheFensterTests.
/// </summary>
internal static class OberflaecheZuordnungBilderTests
{
    /// <summary>Sarevocs Bildschirm: 175 %.</summary>
    private const double Skala = 1.75;

    private const ushort KanalBruecke = 100;

    private static ushort K(byte ch) => Kanalrechnung.Id(KanalBruecke, ch);

    private static string Kurzname(Farbsatz satz) => satz.Stellung switch
    {
        _ => "anthrazit",
    };

    /// <summary>
    /// Der Beispielabend mit dem Kanalnetz Brücke (Ruhe, Schiff 1 bis 20) an einem Server, der Kanal-IDs kennt; Kobold hält in
    /// Feld 6 Schiff 3 als festen Zusatzkanal (wie nach dem Ziehen auf einen Kanal mit Ch.): Dort stehen "Ch.", "entfernen" und "sperren"
    /// zusammen (Befund der Prüfung 0.11.0). Nordwind trägt auch als Konto- und Discord-Name einen erfundenen Namen.
    /// </summary>
    private static FunkDienstAttrappe Szene()
    {
        var d = MitRaidabend();
        var namen = new List<string> { "Ruhe" };
        for (var i = 1; i <= 20; i++) namen.Add("Schiff " + i.ToString(CultureInfo.InvariantCulture));
        d.LageSetzen(l => Belegungsstand(l with
        {
            Netze = [.. l.Netze, new NetzInfo(KanalBruecke, "Brücke", false, false, true, 60) { Kanaele = namen }],
            Funker = [.. l.Funker.Select(f => (f.KontoId, f.Details) switch
            {
                (Nordwind, _) => f with { Name = "Nordwind", DiscordName = "Nordwind" },
                (Kobold, { } x) => f with { Details = x with { Felder = [.. x.Felder, new FeldInfo(5, K(3), true, true, true)] } },
                _ => f,
            })],
            ServerKanaele = true,
        }));
        return d;
    }

    private static Aufbau Aufbauen(FunkDienstAttrappe dienst)
    {
        var a = new Aufbau(dienst: dienst);
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        // Ein erfundener Gerätename, sonst stünde der Name dieses Rechners im Bild.
        a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
        return a;
    }

    // 0.11.2: Gelöscht ist Zuordnung_BrettDetail_ChJePerson_Bilder (das Detailfeld der gewählten Person mit dem Knopf "Ch." und dem Ch.-Raster,
    // gezeichnet in Anthrazit). Die Raid-Seite zeigt seit 0.11.2 weder Detailfeld noch Ch.-Raster (Sarevoc 13.10.2026, Stammbaum: einen Ch. wählt man,
    // indem man den Namen auf ihn zieht); das Modell der Wahl prüft weiter Stufe36BrettTests.Brett_ChJePerson_ImDetailfeld, den Stammbaum
    // BrettBaumTests.

    // ------------------------------------------------------------------------------------------------------------------------------
    // Ansage beim Login und Platzrolle oben links
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void Zuordnung_AnsageBeimLogin_UndPlatzrolle_Bilder()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var was = satz.Name + ", Ansage beim Login";
                var d = Szene();
                using var a = Aufbauen(d);
                var f = a.Funkfeld();
                var zeile = new Meldezeile(() => 0);
                using var t = new Teilnehmermeldungen(d, zeile, SofortTakt.Instanz);
                Pruefe.Gleich("Rolle wählen", f.RolleName, was + ": vorher ohne Platz");

                // Getrennt; Ferox teilt Mustermann vorab als Gunner auf Schiff 1 ein; beim Login kommt die Einteilung, dann der Abgleich.
                d.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt, Abgeglichen = false });
                d.LageSetzen(l => l with
                {
                    Status = Verbindungsstatus.Aktiv,
                    KontoVersion = l.KontoVersion + 1,
                    SchiffId = Schiff1,
                    RolleId = Gunner,
                    Platz = PlatzStatus.Belegt,
                    Quelle = MitgliedschaftsQuelle.Verschieben,
                    QuelleDurch = Ferox,
                    Rolle = null,
                    Felder = [new FeldInfo(0, 12, true, true, false), new FeldInfo(1, 13, true, false, false), new FeldInfo(2, 11, true, false, false)],
                    SendeFelder = [0],
                });
                Pruefe.Falsch(zeile.Sichtbar, was + ": vor dem Abgleich nichts");
                d.LageSetzen(l => l with { Abgeglichen = true });
                // Seit der einzeiligen Meldezeile (Sarevoc, 12.10.2026) nur wer und der Platz; Kanäle und Sendekanal stehen in den Kacheln darunter.
                Pruefe.Gleich(S2TeilnehmerTests.AnsageGunner, zeile.Text, was + ": Ansage");

                Pruefe.Gleich("3 Gunner", f.RolleName, was + ": oben links die Platzrolle");
                Pruefe.Wahr(f.Rolle is null && f.RolleHilfe.Contains("Dein Platz: Schiff 1, Gunner (eingeteilt).", StringComparison.Ordinal), was + ": Hilfetext: " + f.RolleHilfe);
                // Das Funkfeld auf Sarevocs Bildschirm (175 %), die Meldezeile mit seiner Skalierung (seit 0.11.1 an einem festen Platz, fest 640 breit).
                var monitore = new FesteBildschirme([OberflaecheFunkfeldGroesseTests.TestHaupt]);
                var ff = new FunkfeldFenster(f, a.Ablage, monitore);
                fenster.Add(ff);
                var (feldBild, feldGroesse) = Bildlauf.Zeichnen(ff, Skala);

                var mf = new MeldezeileFenster(zeile, monitore, () => f.Skala);
                fenster.Add(mf);
                mf.GroesseSetzen();
                var (zeileBild, zeileGroesse) = Bildlauf.Zeichnen(mf, Skala);
                var zw = Bildlauf.Wurzel(mf);
                Pruefe.Wahr(Sichtbar(zw, zeile.Text), was + ": die Ansage steht ganz in der Meldezeile");
                var satzText = Bildlauf.Nachfahren<TextBlock>(zw).Single(t => t.Name == "Satz" && ZuSehen(t));
                Pruefe.Wahr(satzText.TextWrapping == TextWrapping.NoWrap && satzText.ActualHeight < satzText.FontSize * 2, was + ": eine Zeile");
                Pruefe.Wahr(zeileGroesse.Width <= Meldezeilenlage.BreiteFest + 0.5, string.Create(CultureInfo.InvariantCulture,
                    $"{was}: höchstens 640 breit ({zeileGroesse.Width:0.#})"));
                NichtsAbgeschnitten(zw, was + ", Meldezeile");
                KontrastPruefen(zw, satz, was + ", Meldezeile", mindestens: 1);
                NurEinfacheZeichen(zw, was + ", Meldezeile");
                var fw = Bildlauf.Wurzel(ff);
                Pruefe.Wahr(Sichtbar(fw, "3 Gunner"), was + ": \"3 Gunner\" oben links");
                Pruefe.Falsch(Sichtbar(fw, "Rolle wählen"), was + ": nicht mehr \"Rolle wählen\"");
                var rolleText = Bildlauf.Nachfahren<TextBlock>(fw).Single(x => Inhalt(x) == "3 Gunner" && ZuSehen(x));
                NichtsAbgeschnitten((FrameworkElement)VisualTreeHelper.GetParent(rolleText), was + ", Funkfeld oben links");
                NurEinfacheZeichen(fw, was + ", Funkfeld");

                // Ein Bogen: oben die Meldezeile, darunter das Funkfeld (seit 0.11.1 stehen beide im Programm getrennt; der Bogen zeigt nur beide).
                const double rand = 16;
                const double abstand = 4;
                var bogen = new Bogen(Math.Max(zeileGroesse.Width, feldGroesse.Width) + 2 * rand, zeileGroesse.Height + abstand + feldGroesse.Height + 2 * rand,
                    Skala, Palette.Anthrazit);
                bogen.Bild(zeileBild, rand, rand, zeileGroesse.Width, zeileGroesse.Height);
                bogen.Bild(feldBild, rand, rand + zeileGroesse.Height + abstand, feldGroesse.Width, feldGroesse.Height);
                Bildablage.Ablegen(bogen.Fertig(), "tr0110-ansage-login-" + Kurzname(satz));
                f.Dispose();
            }
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Prüfen
    // ------------------------------------------------------------------------------------------------------------------------------

    private static void NurEinfacheZeichen(DependencyObject wurzel, string was)
    {
        foreach (var t in Bildlauf.Nachfahren<TextBlock>(wurzel).Where(ZuSehen))
        {
            var inhalt = Inhalt(t);
            Pruefe.Falsch(inhalt.Any(z => z is '\u2013' or '\u2014' or '\u2212' or '\u201E' or '\u201C' or '\u201D' or '\u2026' or '\u2022' or '\u00A0'),
                was + ": nur einfache Zeichen in \"" + inhalt + "\"");
        }
    }
}
