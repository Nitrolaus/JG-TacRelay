// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Klang.Eingabe;

namespace Funk.Tests.Maschine;

/// <summary>
/// Ausbaustufe S2, Teil A1 (RAID-S2-ENTWURF Abschnitt 6): Der Joystick kann mehr als Sprechen. Die DirectInput-Quelle ist eine volle
/// Tastenquelle mit Flanken (Sendenetz, Funkfeld, Phasen) und gehaltenen Funktionen (Melden), mit 150 ms Entprellung je Belegung
/// und "frisch drücken"; Raw Input meldet Melden als gehalten statt als Flanke; der Verbund bildet ein ODER über alle Quellen.
/// </summary>
internal static class S2EingabeTests
{
    private static readonly string PfadA = GeraeteschichtAttrappe.Pfad("7&5a5a5a5a&0&0000");
    private static readonly string PfadB = GeraeteschichtAttrappe.Pfad("7&6b6b6b6b&0&0000");

    /// <summary>Nach der Ruhezeit des Öffnens: ab hier zählen Wechsel.</summary>
    private const long Bereit = Joystickauswertung.RuheMs + 50;

    private sealed class Aufzeichnung
    {
        public List<bool> Sprechen { get; } = [];

        public List<string> Gehalten { get; } = [];

        public List<Tastenfunktion> Flanken { get; } = [];
    }

    private static (Joystickauswertung A, GeraeteschichtAttrappe S, Aufzeichnung Auf) Neu()
    {
        var schicht = new GeraeteschichtAttrappe();
        var auf = new Aufzeichnung();
        var a = new Joystickauswertung(schicht)
        {
            Wechsel = auf.Sprechen.Add,
            Gehalten = (f, unten) => auf.Gehalten.Add(f + (unten ? " runter" : " hoch")),
            Flanke = auf.Flanken.Add,
        };
        return (a, schicht, auf);
    }

    private static void Aufzaehlen(Joystickauswertung a, GeraeteschichtAttrappe s, long jetzt)
    {
        var liste = new List<SpielgeraetInfo>();
        Pruefe.Wahr(s.Aufzaehlen(liste), "Aufzählen");
        a.Abgleichen(liste, jetzt);
    }

    private static void Lesen(Joystickauswertung a, GeraetAttrappe g, long jetzt)
    {
        for (var i = 0; i < a.Anzahl; i++)
        {
            if (a.Info(i).Kennung.Pfad != g.Info.Kennung.Pfad) continue;
            a.Lesen(i, jetzt);
            return;
        }
    }

    private static Tastenzuordnung Z(Tastenfunktion f, Tastenbelegung t) => new(f, t);

    [Test]
    public static void Auswertung_MeldenAmStick_GedruecktUndLosgelassen_OhneSprechen()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        var k = stick.Info.Kennung;
        a.ZuordnungenSetzen([Z(Tastenfunktion.Sprechen, Joystickeingabe.Knopf(1, k)), Z(Tastenfunktion.Melden, Joystickeingabe.Knopf(2, k))], 0);
        stick.Knopf(2, true);
        Lesen(a, stick, Bereit);
        Pruefe.Gleich("Melden runter", string.Join(",", auf.Gehalten), "Melden gehalten");
        Pruefe.Wahr(a.MeldenGedrueckt, "Melden gilt als gedrückt");
        Pruefe.Gleich(0, auf.Sprechen.Count, "Melden ist nicht die Sprechtaste");
        Pruefe.Gleich(0, auf.Flanken.Count, "Melden ist keine Flanke");
        stick.Knopf(1, true);
        Lesen(a, stick, Bereit + 10);
        stick.Knopf(2, false);
        Lesen(a, stick, Bereit + 20);
        Pruefe.Gleich("Melden runter,Melden hoch", string.Join(",", auf.Gehalten), "Melden losgelassen");
        Pruefe.Gleich("True", string.Join(",", auf.Sprechen), "die Sprechtaste daneben unabhängig");
        Pruefe.Wahr(a.IstGedrueckt && !a.MeldenGedrueckt, "Sprechen gedrückt, Melden los");
    }

    [Test]
    public static void Auswertung_HutrichtungAlsFlanke_SchraegeHaeltDieRichtung()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        var k = stick.Info.Kennung;
        a.ZuordnungenSetzen([Z(Tastenfunktion.PhaseVor, Joystickeingabe.Hut(1, Hutrichtung.Rechts, k)), Z(Tastenfunktion.PhaseZurueck, Joystickeingabe.Hut(1, Hutrichtung.Links, k))], 0);
        var t = Bereit;
        stick.Hut(1, Hutrichtung.Rechts);
        Lesen(a, stick, t);
        stick.Hut(1, Hutrichtung.ObenRechts);
        Lesen(a, stick, t += 40);
        stick.Hut(1, Hutrichtung.Rechts);
        Lesen(a, stick, t += 40);
        Pruefe.Gleich("PhaseVor", string.Join(",", auf.Flanken), "rechts, schräg, rechts: einmal");
        stick.Hut(1, null);
        Lesen(a, stick, t += 400);
        stick.Hut(1, Hutrichtung.Links);
        Lesen(a, stick, t += 400);
        stick.Hut(1, null);
        Lesen(a, stick, t += 400);
        stick.Hut(1, Hutrichtung.Rechts);
        Lesen(a, stick, t += 400);
        Pruefe.Gleich("PhaseVor,PhaseZurueck,PhaseVor", string.Join(",", auf.Flanken), "jede neue Richtung aus der Mitte");
        Pruefe.Gleich(0, auf.Sprechen.Count, "kein Sprechen");
        Pruefe.Falsch(a.IstGedrueckt, "Flanken halten nichts gedrückt");
    }

    [Test]
    public static void Auswertung_ZweiFlankenBinnen100ms_ZaehlenEinmal_Nach150msWieder()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        a.ZuordnungenSetzen([Z(Tastenfunktion.FunkfeldUmschalten, Joystickeingabe.Knopf(4, stick.Info.Kennung))], 0);
        var t = Bereit;
        stick.Knopf(4, true);
        Lesen(a, stick, t);
        stick.Knopf(4, false);
        Lesen(a, stick, t + 30);
        stick.Knopf(4, true);
        Lesen(a, stick, t + 90);
        stick.Knopf(4, false);
        Lesen(a, stick, t + 120);
        Pruefe.Gleich(1, auf.Flanken.Count, "prellender Knopf: zwei Drücke binnen 100 ms zählen einmal");
        stick.Knopf(4, true);
        Lesen(a, stick, t + Joystickauswertung.EntprellMs + 10);
        Pruefe.Gleich(2, auf.Flanken.Count, "nach der Entprellzeit zählt ein Druck wieder");
        Pruefe.Gleich(Tastenfunktion.FunkfeldUmschalten, auf.Flanken[^1], "Funktion");
    }

    [Test]
    public static void Auswertung_Kippschalter_InDauerstellung_LoestNichtsAus()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        stick.Knopf(9, true); // Kippschalter steht schon auf "an", bevor belegt wird
        stick.Knopf(10, true);
        Lesen(a, stick, Bereit);
        a.ZuordnungenSetzen([Z(Tastenfunktion.PhaseVor, Joystickeingabe.Knopf(9, stick.Info.Kennung)), Z(Tastenfunktion.Melden, Joystickeingabe.Knopf(10, stick.Info.Kennung))], Bereit);
        stick.Knopf(11, true);
        Lesen(a, stick, Bereit + 200);
        Pruefe.Gleich(0, auf.Flanken.Count, "Dauerstellung beim Belegen: keine Flanke");
        Pruefe.Gleich(0, auf.Gehalten.Count, "Melden beim Belegen gehalten: zählt nicht");
        stick.Knopf(9, false);
        stick.Knopf(10, false);
        Lesen(a, stick, Bereit + 400);
        stick.Knopf(9, true);
        stick.Knopf(10, true);
        Lesen(a, stick, Bereit + 800);
        Pruefe.Gleich("PhaseVor", string.Join(",", auf.Flanken), "frisch gedrückt: Flanke");
        Pruefe.Gleich("Melden runter", string.Join(",", auf.Gehalten), "frisch gedrückt: Melden");

        // Ein wieder angestecktes Gerät mit Schalter in Dauerstellung löst ebenfalls nichts aus.
        s.Abstecken(stick);
        a.Geraeteaenderung(2000);
        Pruefe.Gleich("Melden runter,Melden hoch", string.Join(",", auf.Gehalten), "Abstecken lässt Melden los");
        var wieder = s.Anstecken(PfadA);
        wieder.Knopf(9, true);
        Aufzaehlen(a, s, 3000);
        wieder.Knopf(12, true);
        Lesen(a, wieder, 3000 + Bereit);
        Pruefe.Gleich(1, auf.Flanken.Count, "wieder angesteckt, Schalter auf an: keine Flanke");
    }

    [Test]
    public static void Auswertung_SprechenMeldenUndFlanke_AmSelbenGeraet_Abstecken()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        var pedale = s.Anstecken(PfadB, pid: 0x0198, name: "VPC Pedals R1");
        Aufzaehlen(a, s, 0);
        var k = stick.Info.Kennung;
        a.ZuordnungenSetzen([
            Z(Tastenfunktion.Sprechen, Joystickeingabe.Knopf(1, k)),
            Z(Tastenfunktion.Sprechen, Joystickeingabe.Hut(1, Hutrichtung.Unten, pedale.Info.Kennung)),
            Z(Tastenfunktion.Melden, Joystickeingabe.Knopf(2, k)),
            Z(Tastenfunktion.NetzVor, Joystickeingabe.Knopf(3, k)),
        ], 0);
        stick.Knopf(1, true);
        Lesen(a, stick, Bereit);
        pedale.Hut(1, Hutrichtung.Unten);
        Lesen(a, pedale, Bereit);
        stick.Knopf(1, false);
        Lesen(a, stick, Bereit + 1);
        Pruefe.Gleich("True", string.Join(",", auf.Sprechen), "Sprechen über Knopf und Hut wirkt wie eine Taste");
        stick.Knopf(2, true);
        stick.Knopf(3, true);
        Lesen(a, stick, Bereit + 2);
        Pruefe.Gleich("NetzVor", string.Join(",", auf.Flanken), "Sendenetz vor am Stick");
        s.Abstecken(stick);
        Lesen(a, stick, Bereit + 3);
        Pruefe.Gleich("Melden runter,Melden hoch", string.Join(",", auf.Gehalten), "Abstecken lässt Melden los");
        Pruefe.Wahr(a.IstGedrueckt, "der Hut der Pedale hält die Sprechtaste weiter");
        pedale.Hut(1, null);
        Lesen(a, pedale, Bereit + 4);
        Pruefe.Gleich("True,False", string.Join(",", auf.Sprechen), "los mit dem letzten");
    }

    [Test]
    public static void Auswertung_MeldenHaengt_WaechterLaesstLos()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        a.ZuordnungenSetzen([Z(Tastenfunktion.Melden, Joystickeingabe.Knopf(6, stick.Info.Kennung))], 0);
        stick.Knopf(6, true);
        Lesen(a, stick, Bereit);
        stick.KnopfStill(6, false);
        var frist = a.NaechsteFrist(Bereit);
        Pruefe.Wahr(frist is > 0 and <= Joystickauswertung.WaechterMs, "Wächter geplant, auch für Melden: " + frist);
        if (a.Faelliges(Bereit + frist)) Aufzaehlen(a, s, Bereit + frist);
        Pruefe.Gleich("Melden runter,Melden hoch", string.Join(",", auf.Gehalten), "verlorenes Loslassen: der Wächter meldet es");
    }

    [Test]
    public static void RawInput_MeldenGehalten_NichtAlsFlanke_HaengerLaesstLos()
    {
        var strg = Tastenbelegung.Taste(0xE01D);
        var f9 = Tastenbelegung.Taste(0x0043);
        var plus = Tastenbelegung.Taste(Tastenbelegung.NumpadPlus);
        var z = new SprechtastenZustand();
        _ = z.Zuordnen([Z(Tastenfunktion.Sprechen, strg), Z(Tastenfunktion.Melden, f9), Z(Tastenfunktion.PhaseVor, plus)]);
        z.Verarbeiten(new Tastenereignis(f9, true), 0x78, out var w, out var f, out _);
        Pruefe.Gleich(0, w, "Melden öffnet nicht die Sprechtaste");
        Pruefe.Gleich(null, f, "Melden ist keine Flanke");
        Pruefe.Wahr(z.MeldenGedrueckt, "Melden gehalten");
        z.Verarbeiten(new Tastenereignis(f9, true), 0x78, out _, out f, out _);
        Pruefe.Wahr(z.MeldenGedrueckt && f is null, "Tastenwiederholung: weiter gehalten, nichts Neues");
        z.Verarbeiten(new Tastenereignis(plus, true), 0x6B, out _, out f, out _);
        Pruefe.Gleich(Tastenfunktion.PhaseVor, f, "Phase vor als Flanke");
        Pruefe.Falsch(z.HaengerPruefen(_ => false), "erste Prüfung");
        Pruefe.Falsch(z.HaengerPruefen(_ => false), "Melden gilt jetzt als los, die Sprechtaste war nie gedrückt");
        Pruefe.Falsch(z.MeldenGedrueckt, "Hängeschutz lässt Melden los");
        z.Verarbeiten(new Tastenereignis(f9, true), 0x78, out _, out _, out _);
        Pruefe.Wahr(z.MeldenGedrueckt, "wieder gedrückt");
        _ = z.Zuordnen([Z(Tastenfunktion.Sprechen, strg)]);
        Pruefe.Falsch(z.MeldenGedrueckt, "neue Zuordnungen lassen Melden los");
    }

    [Test]
    public static void Verbund_MeldenOderUeberQuellen_NeueZuordnungenLassenLos()
    {
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        var stick = new TastenQuelleAttrappe(EingabeArt.Joystick);
        using var verbund = new SprechtastenVerbund(raw, stick);
        var folge = new List<string>();
        verbund.Gehalten += (f, unten) => folge.Add(f + (unten ? "+" : "-"));
        raw.Halten(Tastenfunktion.Melden, true);
        stick.Halten(Tastenfunktion.Melden, true);
        raw.Halten(Tastenfunktion.Melden, false);
        Pruefe.Gleich("Melden+", string.Join(",", folge), "Tastatur los, der Stick hält Melden noch");
        stick.Halten(Tastenfunktion.Melden, false);
        Pruefe.Gleich("Melden+,Melden-", string.Join(",", folge), "los mit der letzten Quelle");
        stick.Halten(Tastenfunktion.Melden, true);
        verbund.Zuordnungen = [];
        Pruefe.Gleich("Melden+,Melden-,Melden+,Melden-", string.Join(",", folge), "neue Zuordnungen lassen ein gehaltenes Melden los");
        stick.Halten(Tastenfunktion.Melden, false);
        Pruefe.Gleich(4, folge.Count, "das späte Loslassen der Quelle zählt nicht doppelt");
    }

    // ---------------------------------------------------------------- Lesethread mit der Attrappe

    private static bool Warten(Func<bool> bedingung, int ms = 3000)
    {
        var uhr = Stopwatch.StartNew();
        while (!bedingung())
        {
            if (uhr.ElapsedMilliseconds > ms) return false;
            Thread.Sleep(1);
        }
        return true;
    }

    [Test]
    public static void Verbund_ZweiteSprechtasteUndSendenetzVorAmStick_WerdenBedient()
    {
        // Heute (0.4.0) rot: Der Verbund gab der DirectInput-Quelle nur ihre erste Sprechtaste; die zweite und "Sendenetz vor"
        // standen unter Tasten als "nicht verfügbar".
        var schicht = new GeraeteschichtAttrappe();
        var g = schicht.Anstecken(PfadA);
        var taste = new DirectInputSprechtaste(() => schicht, () => schicht);
        Pruefe.Wahr(Warten(() => taste.Geraete.Count == 1), "Gerät aufgezählt");
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        using var verbund = new SprechtastenVerbund(raw, taste);
        var k = g.Info.Kennung;
        verbund.Zuordnungen = [
            Z(Tastenfunktion.Sprechen, Joystickeingabe.Knopf(1, k)),
            Z(Tastenfunktion.Sprechen, Joystickeingabe.Knopf(2, k)),
            Z(Tastenfunktion.NetzVor, Joystickeingabe.Knopf(3, k)),
            Z(Tastenfunktion.Melden, Joystickeingabe.Knopf(4, k)),
        ];
        Pruefe.Gleich(0, verbund.NichtBedient.Count, "alles am Stick bedient");
        Thread.Sleep(Joystickauswertung.RuheMs + 100);
        var gedrueckt = 0;
        var netz = 0;
        var melden = 0;
        verbund.Gedrueckt += () => Interlocked.Increment(ref gedrueckt);
        verbund.Ausgeloest += f =>
        {
            if (f == Tastenfunktion.NetzVor) Interlocked.Increment(ref netz);
        };
        verbund.Gehalten += (f, unten) =>
        {
            if (f == Tastenfunktion.Melden && unten) Interlocked.Increment(ref melden);
        };
        g.Knopf(2, true);
        Pruefe.Wahr(Warten(() => Volatile.Read(ref gedrueckt) == 1), "die zweite Sprechtaste am Stick sendet");
        g.Knopf(2, false);
        g.Knopf(3, true);
        Pruefe.Wahr(Warten(() => Volatile.Read(ref netz) == 1), "Sendenetz vor am Stick schaltet");
        g.Knopf(4, true);
        Pruefe.Wahr(Warten(() => Volatile.Read(ref melden) == 1), "Melden am Stick");
        Pruefe.Gleich(1, Volatile.Read(ref gedrueckt), "Melden und Sendenetz öffnen nicht die Sprechtaste");
    }

    [Test]
    public static void Lesethread_FlankenUndMelden_OhneAllokationImTakt()
    {
        var schicht = new GeraeteschichtAttrappe();
        var g = schicht.Anstecken(PfadA);
        using var taste = new DirectInputSprechtaste(() => schicht, () => schicht);
        Pruefe.Wahr(Warten(() => taste.Geraete.Count == 1), "Gerät aufgezählt");
        var k = taste.Geraete[0].Kennung;
        taste.Zuordnungen = [Z(Tastenfunktion.PhaseVor, Joystickeingabe.Knopf(5, k)), Z(Tastenfunktion.Melden, Joystickeingabe.Knopf(6, k)), Z(Tastenfunktion.Sprechen, Joystickeingabe.Knopf(7, k))];
        Thread.Sleep(Joystickauswertung.RuheMs + 100);
        long flanken = 0, melden = 0;
        taste.Ausgeloest += _ => Interlocked.Increment(ref flanken);
        taste.Gehalten += (_, unten) =>
        {
            if (unten) Interlocked.Increment(ref melden);
        };
        var vorher = taste.Statistik.AllokiertImTakt;
        for (var i = 0; i < 20; i++)
        {
            g.Knopf(5, true);
            Pruefe.Wahr(Warten(() => Interlocked.Read(ref flanken) == i + 1), "Flanke " + i);
            g.Knopf(5, false);
            g.Knopf(6, true);
            Pruefe.Wahr(Warten(() => Interlocked.Read(ref melden) == i + 1), "Melden " + i);
            g.Knopf(6, false);
            Thread.Sleep(Joystickauswertung.EntprellMs + 20);
        }
        Pruefe.Gleich(vorher, taste.Statistik.AllokiertImTakt, "keine Allokation im Takt, auch mit Flanken und Melden");
        Pruefe.Gleich(0L, taste.Statistik.Fehler, "keine Fehler");
    }
}
