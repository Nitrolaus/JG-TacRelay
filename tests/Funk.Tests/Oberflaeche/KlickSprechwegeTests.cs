// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Klang.Eingabe;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Maschine;
using Funk.Tests.Verarbeitung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// A63: Jeder Weg, auf dem man selbst spricht, löst den eigenen Funk-Klick aus, beim Drücken und beim Loslassen: Sprechtaste an der
/// Tastatur und an der Maus, Sprechtaste und Melde-Taste am Joystick, Melde-Taste an der Tastatur (die Sprechtaste zum Ausprobieren im Bereich
/// Audio gibt es seit 0.10.0 nicht mehr, Endfassung #26). Verdrahtet wie in App.OnStartup: Tastenquellen im <see cref="SprechtastenVerbund"/> (Tastatur und Maus als
/// Attrappe wie Raw Input, der Joystick über die echte DirectInput-Quelle mit Geräteschicht-Attrappe), <see cref="Tastenbetrieb"/>,
/// <see cref="Sprechsteuerung"/>, dahinter die echte Klangmaschine im Prüfstand. Gemessen wird, was aus der Wiedergabe kommt.
/// Kein echtes Gerät, kein Fenster.
/// </summary>
internal static class KlickSprechwegeTests
{
    private sealed class Meldeziel : IMeldeziel
    {
        public void MeldezielSetzen(ushort? netzId, bool umschalten = true)
        {
        }
    }

    private static bool Warten(Func<bool> bedingung, int ms = 3000)
    {
        var bis = Environment.TickCount64 + ms;
        while (!bedingung())
        {
            if (Environment.TickCount64 > bis) return false;
            Thread.Sleep(5);
        }
        return true;
    }

    [Test]
    public static async Task AlleSprechwege_LoesenDenEigenenKlickAus()
    {
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false);
        await s.StartenAsync();
        s.Stille(5);

        var schicht = new GeraeteschichtAttrappe();
        var stick = schicht.Anstecken(GeraeteschichtAttrappe.Pfad("7&5a5a5a5a&0&0000"));
        var joystick = new DirectInputSprechtaste(() => schicht, () => schicht);
        Pruefe.Wahr(Warten(() => joystick.Geraete.Count == 1), "Stick aufgezählt");
        var tastatur = new TastenQuelleAttrappe(EingabeArt.Tastatur);
        var maus = new TastenQuelleAttrappe(EingabeArt.Maus);
        using var verbund = new SprechtastenVerbund(tastatur, maus, joystick);
        var k = stick.Info.Kennung;
        verbund.Zuordnungen = [
            new(Tastenfunktion.Sprechen, Tastenbelegung.Taste(0xE01D)),
            new(Tastenfunktion.Melden, Tastenbelegung.Taste(0x0043)),
            new(Tastenfunktion.Sprechen, Tastenbelegung.Maus(Tastenbelegung.MausX1)),
            new(Tastenfunktion.Sprechen, Joystickeingabe.Knopf(1, k)),
            new(Tastenfunktion.Sprechen, Joystickeingabe.Knopf(3, k)),
            new(Tastenfunktion.Melden, Joystickeingabe.Knopf(2, k)),
        ];
        Pruefe.Gleich(0, verbund.NichtBedient.Count, "alles bedient");
        Thread.Sleep(Joystickauswertung.RuheMs + 100);

        var takt = new WarteschlangenTakt();
        using var a = new Aufbau(dienst: MitRaidabend(), takt: takt) { Jetzt = Abend };
        var funkfeld = a.Funkfeld();
        var zeile = new Meldezeile(() => a.Uhr);
        var ablage = new FunkfeldAblage(new FunkfeldFensterAttrappe(), new AblagesymbolAttrappe(), a.Ablage, zeile, a.Dienst, () => a.Uhr);
        var phasen = new Phasensteuerung(a.Dienst, zeile, () => a.Uhr, () => a.Jetzt);
        using var teilnehmer = new Teilnehmermeldungen(a.Dienst, zeile, takt);
        var sprechen = new Sprechsteuerung(s.Maschine, new Meldeziel(), () => new MeldeEntscheid(NetzOrga, "Orga", null));
        using var betrieb = new Tastenbetrieb(verbund, sprechen, takt, funkfeld, ablage, phasen, teilnehmer);
        // Den eigenen Klick gibt es seit Klangstufe C1 nur mit Headset (Q6).
        a.Ablage.Aktuell.Audio.Hoerweise = Funk.Client.Ablage.Hoerweise.Headset;
        // Das Modell gibt der Maschine den Klick wie im Programm.
        _ = new AudioViewModel(s.Maschine, new Funk.Client.Klang.PegelVerlauf(), a.Ablage, takt);

        s.Mitschreiben = true;
        var klicks = new List<string>();
        void Weg(string was, Action druecken, Action loslassen)
        {
            foreach (var (schritt, tun, offen) in new[] { ("Drücken", druecken, true), ("Loslassen", loslassen, false) })
            {
                s.Ausgabe.Clear();
                tun();
                Pruefe.Wahr(Warten(() => s.Maschine.Kette.Sprechtaste == offen), was + ", " + schritt + ": Mikrofon " + (offen ? "offen" : "zu"));
                // Den Klick fordert die Maschine gleich nach dem Mikrofon an, aus dem Thread der Quelle.
                Pruefe.Wahr(Warten(() =>
                {
                    s.Stille(1);
                    return KlickMaschineTests.Links(s.Ausgabe).Any(v => v != 0f);
                }), was + ", " + schritt + ": Klick kommt");
                s.Stille(25);
                PruefeWert.Nahe(-37, KlickMaschineTests.KlickPegel(s.Ausgabe), 0.1, was + ", " + schritt + ": eigener Klick in der Voreinstellung");
                klicks.Add(was + " " + schritt);
            }
            s.Stille(10);
        }

        Weg("Sprechtaste Tastatur", () => tastatur.Druecken(true), () => tastatur.Druecken(false));
        Weg("Sprechtaste Maus", () => maus.Druecken(true), () => maus.Druecken(false));
        Weg("Melde-Taste Tastatur", () => tastatur.Halten(Tastenfunktion.Melden, true), () => tastatur.Halten(Tastenfunktion.Melden, false));
        Weg("Sprechtaste Joystick", () => stick.Knopf(1, true), () => stick.Knopf(1, false));
        Thread.Sleep(Joystickauswertung.EntprellMs + 20);
        Weg("zweite Sprechtaste Joystick", () => stick.Knopf(3, true), () => stick.Knopf(3, false));
        Thread.Sleep(Joystickauswertung.EntprellMs + 20);
        Weg("Melde-Taste Joystick", () => stick.Knopf(2, true), () => stick.Knopf(2, false));
        Pruefe.Gleich(12, klicks.Count, "sechs Wege, je Drücken und Loslassen");

        // Ist das Mikrofon schon offen, klickt die Melde-Taste nicht noch einmal: Ein Klick heißt "Mikrofon auf" oder "Mikrofon zu".
        s.Ausgabe.Clear();
        tastatur.Druecken(true);
        Pruefe.Wahr(Warten(() => s.Maschine.Kette.Sprechtaste), "Sprechtaste gehalten");
        s.Stille(25);
        s.Ausgabe.Clear();
        stick.Knopf(2, true);
        Pruefe.Wahr(Warten(() => sprechen.Meldet), "Melden am Stick, während die Sprechtaste gehalten ist");
        s.Stille(25);
        stick.Knopf(2, false);
        Pruefe.Wahr(Warten(() => !sprechen.Meldet), "Melden los");
        s.Stille(25);
        Pruefe.Gleich(Pegel.MinDbfs, KlickMaschineTests.KlickPegel(s.Ausgabe), "kein zweiter Klick, solange das Mikrofon offen bleibt");
        tastatur.Druecken(false);
        Pruefe.Wahr(Warten(() => !s.Maschine.Kette.Sprechtaste), "zu");
        Pruefe.Wahr(Warten(() =>
        {
            s.Stille(1);
            return KlickMaschineTests.Links(s.Ausgabe).Any(v => v != 0f);
        }), "Loslassen klickt");
        takt.Abarbeiten();
        funkfeld.Dispose();
    }
}
