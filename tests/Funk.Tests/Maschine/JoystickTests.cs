// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Klang.Eingabe;

namespace Funk.Tests.Maschine;

/// <summary>
/// Joystick-Sprechtaste (A3) ohne Geräte: Zustandslogik und Wiederfinden über der Attrappe der Geräteschicht (Knopf,
/// Hut, Abstecken während gedrückt, neue Instanz-GUID, zwei gleiche Geräte), dazu der Lesethread mit Latenz, Belegen
/// und Freigabe, und die Kennung, die Codes und der Aufbau der DirectInput-Strukturen.
/// </summary>
internal static class JoystickTests
{
    private static readonly string PfadA = GeraeteschichtAttrappe.Pfad("7&2a3b4c5d&0&0000");
    private static readonly string PfadB = GeraeteschichtAttrappe.Pfad("7&9f8e7d6c&0&0000");

    /// <summary>Nach der Ruhezeit des Öffnens: ab hier zählen Wechsel.</summary>
    private const long Bereit = Joystickauswertung.RuheMs + 50;

    private sealed class Aufzeichnung
    {
        public List<bool> Wechsel { get; } = [];

        public List<Tastenbelegung> Gelernt { get; } = [];
    }

    private static (Joystickauswertung Auswertung, GeraeteschichtAttrappe Schicht, Aufzeichnung Auf) Neu()
    {
        var schicht = new GeraeteschichtAttrappe();
        var auf = new Aufzeichnung();
        var a = new Joystickauswertung(schicht) { Wechsel = auf.Wechsel.Add, Gelernt = auf.Gelernt.Add };
        return (a, schicht, auf);
    }

    private static int Index(Joystickauswertung a, GeraetAttrappe g)
    {
        for (var i = 0; i < a.Anzahl; i++)
        {
            if (a.Info(i).Kennung.Pfad == g.Info.Kennung.Pfad) return i;
        }
        return -1;
    }

    /// <summary>Suche und Abgleich, wie Such- und Lesethread es tun, hier ohne Threads.</summary>
    private static void Aufzaehlen(Joystickauswertung a, GeraeteschichtAttrappe s, long jetzt)
    {
        var liste = new List<SpielgeraetInfo>();
        Pruefe.Wahr(s.Aufzaehlen(liste), "Aufzählen");
        a.Abgleichen(liste, jetzt);
    }

    /// <summary>Fällige Arbeit; ist eine Suche fällig, läuft sie gleich.</summary>
    private static void Faelliges(Joystickauswertung a, GeraeteschichtAttrappe s, long jetzt)
    {
        if (a.Faelliges(jetzt)) Aufzaehlen(a, s, jetzt);
    }

    /// <summary>Wie das Ereignis des Geräts: Puffer lesen, falls das Gerät offen ist.</summary>
    private static void Lesen(Joystickauswertung a, GeraetAttrappe g, long jetzt)
    {
        var i = Index(a, g);
        if (i >= 0) a.Lesen(i, jetzt);
    }

    private static bool Folge(Aufzeichnung auf, params bool[] erwartet) => auf.Wechsel.SequenceEqual(erwartet);

    [Test]
    public static void Knopf_NurDerBelegte_MeldetDrueckenUndLoslassen()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        Pruefe.Gleich(1, a.Anzahl, "ein Gerät offen");
        a.BelegungenSetzen([Joystickeingabe.Knopf(5, stick.Info.Kennung)], 0);
        stick.Knopf(6, true);
        Lesen(a, stick, Bereit);
        Pruefe.Wahr(auf.Wechsel.Count == 0, "fremder Knopf meldet nichts");
        stick.Knopf(5, true);
        Lesen(a, stick, Bereit + 1);
        stick.Knopf(5, true);
        Lesen(a, stick, Bereit + 2);
        Pruefe.Wahr(Folge(auf, true), "einmal gedrückt, Wiederholung zählt nicht");
        Pruefe.Wahr(a.IstGedrueckt, "gedrückt");
        stick.Knopf(6, false);
        stick.Knopf(5, false);
        Lesen(a, stick, Bereit + 3);
        Pruefe.Wahr(Folge(auf, true, false), "losgelassen");
    }

    [Test]
    public static void KurzerDruck_ZwischenZweiAbholungen_GehtNichtVerloren()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(1, stick.Info.Kennung)], 0);
        stick.Knopf(1, true);
        stick.Knopf(1, false);
        Lesen(a, stick, Bereit);
        Pruefe.Wahr(Folge(auf, true, false), "Drücken und Loslassen aus dem Puffer, obwohl der Endstand gleich ist");
    }

    [Test]
    public static void Hut_HauptrichtungMitSchraegen_SchraegeNurGenau()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Hut(1, Hutrichtung.Oben, stick.Info.Kennung)], 0);
        var t = Bereit;
        void Schritt(Hutrichtung? r)
        {
            stick.Hut(1, r);
            Lesen(a, stick, t++);
        }
        Schritt(Hutrichtung.Oben);
        Schritt(Hutrichtung.ObenRechts);
        Pruefe.Wahr(Folge(auf, true), "oben, dann oben rechts: bleibt gedrückt");
        Schritt(Hutrichtung.Rechts);
        Schritt(Hutrichtung.ObenLinks);
        Schritt(null);
        Pruefe.Wahr(Folge(auf, true, false, true, false), "rechts los, oben links gedrückt, Mitte los");

        auf.Wechsel.Clear();
        a.BelegungenSetzen([Joystickeingabe.Hut(1, Hutrichtung.ObenRechts, stick.Info.Kennung)], t);
        Schritt(Hutrichtung.Oben);
        Pruefe.Wahr(auf.Wechsel.Count == 0, "Schräge: oben allein zählt nicht");
        Schritt(Hutrichtung.ObenRechts);
        Schritt(Hutrichtung.Rechts);
        Pruefe.Wahr(Folge(auf, true, false), "nur genau oben rechts");

        auf.Wechsel.Clear();
        a.BelegungenSetzen([Joystickeingabe.Hut(2, Hutrichtung.Unten, stick.Info.Kennung)], t);
        Schritt(Hutrichtung.Unten);
        Pruefe.Wahr(auf.Wechsel.Count == 0, "Hut 1 bewegt, Hut 2 belegt");
        stick.Hut(2, Hutrichtung.UntenLinks);
        Lesen(a, stick, t++);
        Pruefe.Wahr(Folge(auf, true), "Hut 2 unten links gilt für unten");
    }

    [Test]
    public static void AbsteckenWaehrendGedrueckt_MeldetLoslassen()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(3, stick.Info.Kennung)], 0);
        stick.Knopf(3, true);
        Lesen(a, stick, Bereit);
        Pruefe.Wahr(Folge(auf, true), "gedrückt");
        var version = a.Version;
        s.Abstecken(stick);
        Lesen(a, stick, Bereit + 10);
        Pruefe.Wahr(Folge(auf, true, false), "Abstecken beim Lesen: losgelassen");
        Pruefe.Gleich(0, a.Anzahl, "Gerät geschlossen");
        Pruefe.Wahr(a.Version != version, "Warteliste wird neu gebaut");
        Pruefe.Gleich(0, stick.Offen, "Zugang freigegeben");
        Pruefe.Wahr(a.NaechsteFrist(Bereit + 10) is >= 0 and <= Joystickauswertung.NachsucheMs, "bald neu aufzählen");

        // Zweiter Weg: Windows meldet das Abstecken, das Gerät hat selbst nichts gesagt.
        var zweiter = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 2000);
        zweiter.Knopf(3, false);
        zweiter.Knopf(3, true);
        Lesen(a, zweiter, 2000 + Bereit);
        Pruefe.Wahr(Folge(auf, true, false, true), "wieder gedrückt");
        s.Abstecken(zweiter);
        a.Geraeteaenderung(3000);
        Pruefe.Wahr(Folge(auf, true, false, true, false), "Windows-Meldung: losgelassen");
        Faelliges(a, s, 3000);
        Pruefe.Gleich(0, a.Anzahl, "nach dem Aufzählen nichts offen");
    }

    [Test]
    public static void WiederAngesteckt_MitNeuerInstanzGuid_FindetBelegungUeberPfad()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA, instanz: Guid.NewGuid());
        Aufzaehlen(a, s, 0);
        var belegung = Joystickeingabe.Knopf(9, stick.Info.Kennung);
        a.BelegungenSetzen([belegung], 0);
        s.Abstecken(stick);
        Aufzaehlen(a, s, 1000);
        Pruefe.Gleich(0, a.Anzahl, "weg");
        Pruefe.Gleich(1000 + Joystickauswertung.SuchabstandMs, 1000 + a.NaechsteFrist(1000), "Nachsuchen, solange die Belegung ihr Gerät nicht findet");

        var neueGuid = Guid.NewGuid();
        var zurueck = s.Anstecken(PfadA, instanz: neueGuid);
        zurueck.Knopf(9, true); // beim Anstecken schon gehalten
        Faelliges(a, s, 1000 + Joystickauswertung.SuchabstandMs);
        Pruefe.Gleich(neueGuid, a.Info(0).Instanz, "mit neuer Instanz-GUID geöffnet");
        Pruefe.Wahr(auf.Wechsel.Count == 0, "gehaltener Knopf beim Wiederkommen sendet nicht von allein");
        Pruefe.Gleich(-1L, a.NaechsteFrist(5000), "gefunden: kein Nachsuchen mehr");
        zurueck.Knopf(9, false);
        zurueck.Knopf(9, true);
        Lesen(a, zurueck, 6000);
        Pruefe.Wahr(Folge(auf, true), "frisch gedrückt: sendet");
    }

    [Test]
    public static void InstanzGuidWandertZumKlon_BelegungBleibtAmOriginal()
    {
        var (a, s, auf) = Neu();
        var alteGuid = Guid.NewGuid();
        var original = s.Anstecken(PfadA, instanz: alteGuid);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(2, original.Info.Kennung)], 0);
        // Joystick Guardian legt einen Klon gleicher VID/PID an; DirectInput gibt ihm die alte GUID, dem Original eine neue.
        original.NeueInstanz(Guid.NewGuid());
        var klon = s.Anstecken(GeraeteschichtAttrappe.Pfad("1&79f5d87&0&0002"), instanz: alteGuid);
        Aufzaehlen(a, s, 100);
        Pruefe.Gleich(2, a.Anzahl, "Original und Klon offen");
        Pruefe.Gleich(1, original.Offen, "das Original blieb offen, sein Zugang hängt nicht an der GUID");
        klon.Knopf(2, true);
        Lesen(a, klon, 100 + Bereit);
        Pruefe.Wahr(auf.Wechsel.Count == 0, "der Klon mit der alten GUID löst nicht aus");
        original.Knopf(2, true);
        Lesen(a, original, 100 + Bereit);
        Pruefe.Wahr(Folge(auf, true), "das Original am selben Pfad löst aus");

        // Nach echtem Abstecken kommt das Original mit einer neuen GUID wieder, der Klon behält die alte.
        s.Abstecken(original);
        a.Geraeteaenderung(2000);
        Pruefe.Wahr(Folge(auf, true, false), "abgesteckt");
        var wieder = s.Anstecken(PfadA, instanz: Guid.NewGuid());
        wieder.Knopf(2, true); // beim Wiederkommen schon gehalten
        Faelliges(a, s, 2000 + Joystickauswertung.NachsucheMs);
        klon.Knopf(2, false);
        klon.Knopf(2, true);
        Lesen(a, klon, 4000);
        Pruefe.Wahr(Folge(auf, true, false), "Klon weiter ohne Wirkung");
        Lesen(a, wieder, 4000);
        Pruefe.Wahr(Folge(auf, true, false), "gehalten beim Wiederkommen: sendet nicht von allein");
        wieder.Knopf(2, false);
        wieder.Knopf(2, true);
        Lesen(a, wieder, 4001);
        Pruefe.Wahr(Folge(auf, true, false, true), "frisch gedrückt am Original");
    }

    [Test]
    public static void ZweiGleicheGeraete_DerPfadEntscheidet()
    {
        var (a, s, auf) = Neu();
        var links = s.Anstecken(PfadA);
        var rechts = s.Anstecken(PfadB);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(3, rechts.Info.Kennung)], 0);
        links.Knopf(3, true);
        Lesen(a, links, Bereit);
        Pruefe.Wahr(auf.Wechsel.Count == 0, "Knopf 3 am linken Stick zählt nicht");
        rechts.Knopf(3, true);
        Lesen(a, rechts, Bereit);
        Pruefe.Wahr(Folge(auf, true), "Knopf 3 am rechten Stick");
    }

    [Test]
    public static void Umgesteckt_NeuerPfad_FindetUeberVidPidUndName()
    {
        var alt = new Geraetekennung(PfadA, 0x3344, 0x4130, "L-VPC Stick MT-50CM3");
        var belegung = Joystickeingabe.Knopf(1, alt);
        var neuLinks = new Geraetekennung(GeraeteschichtAttrappe.Pfad("8&11&0&0000"), 0x3344, 0x4130, "L-VPC Stick MT-50CM3");
        var neuRechts = new Geraetekennung(GeraeteschichtAttrappe.Pfad("8&22&0&0000"), 0x3344, 0x4130, "R-VPC Stick MT-50CM3");
        var anderesModell = new Geraetekennung(GeraeteschichtAttrappe.Pfad("8&33&0&0000"), 0x3344, 0x0194, "VPC Throttle");
        var klon = new Geraetekennung(@"\\?\HID#HIDMAESTRO&VID_3344&PID_4130#1&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}", 0x3344, 0x4130, "L-VPC Stick MT-50CM3");

        Pruefe.Gleich(0, Joystickauswertung.Zuordnen(alt, [neuLinks, anderesModell], null), "einziger Kandidat gleicher VID/PID");
        Pruefe.Gleich(1, Joystickauswertung.Zuordnen(alt, [neuRechts, neuLinks], null), "zwei gleiche Modelle: der Name entscheidet");
        Pruefe.Gleich(-1, Joystickauswertung.Zuordnen(alt, [neuRechts], null), "anderer Name ist ein Widerspruch");
        Pruefe.Gleich(-1, Joystickauswertung.Zuordnen(alt with { Name = "" }, [neuRechts, neuLinks], null), "zwei Kandidaten ohne Namen: keiner");
        Pruefe.Gleich(0, Joystickauswertung.Zuordnen(alt with { Name = "" }, [neuRechts], null), "ohne gespeicherten Namen reicht VID/PID");
        Pruefe.Gleich(-1, Joystickauswertung.Zuordnen(alt, [neuLinks, neuLinks with { Pfad = GeraeteschichtAttrappe.Pfad("8&55&0&0000") }], null),
            "zwei gleiche Sticks, beide mit neuem Pfad: keiner, lieber nicht senden als am falschen");
        Pruefe.Gleich(-1, Joystickauswertung.Zuordnen(alt, [anderesModell], null), "anderes Modell nie");
        Pruefe.Gleich(-1, Joystickauswertung.Zuordnen(alt, [klon], null), "ein Klon ersetzt kein echtes Gerät");
        Pruefe.Gleich(1, Joystickauswertung.Zuordnen(alt, [neuRechts, neuLinks with { Name = "L-VPC Stick MT-50CM3" }, klon], null), "Klon übergangen");
        Pruefe.Gleich(1, Joystickauswertung.Zuordnen(alt with { Name = "" }, [neuRechts, neuLinks], p => p == neuRechts.Pfad),
            "der rechte Pfad gehört einer anderen Belegung");
        Pruefe.Gleich(1, Joystickauswertung.Zuordnen(alt, [neuRechts, alt with { Pfad = PfadA.ToUpperInvariant() }], null), "Pfad ohne Groß/Klein");

        // Dasselbe in der Auswertung: zwei gleiche Sticks, beide belegt, der linke kommt an einem anderen Anschluss zurück.
        var (a, s, auf) = Neu();
        var l = s.Anstecken(PfadA);
        var r = s.Anstecken(PfadB);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(1, l.Info.Kennung), Joystickeingabe.Knopf(2, r.Info.Kennung)], 0);
        s.Abstecken(l);
        var lNeu = s.Anstecken(GeraeteschichtAttrappe.Pfad("8&44&0&0000"));
        a.Geraeteaenderung(100);
        Faelliges(a, s, 100);
        lNeu.Knopf(1, true);
        Lesen(a, lNeu, 100 + Bereit);
        Pruefe.Wahr(Folge(auf, true), "der linke Stick am neuen Anschluss: gefunden, weil der rechte Pfad belegt ist");
    }

    [Test]
    public static void Belegen_ErsterFrischerKnopfOderHut_MitKennung()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA, name: "VPC Stick MT-50CM3");
        Aufzaehlen(a, s, 0);
        stick.Knopf(2, true); // vor dem Belegen gehalten
        Lesen(a, stick, Bereit);
        a.LernenStarten(Bereit);
        Pruefe.Wahr(a.NaechsteFrist(Bereit) == 0, "Belegen zählt sofort neu auf");
        Faelliges(a, s, Bereit);
        stick.Knopf(2, true);
        Lesen(a, stick, Bereit + 1);
        stick.Knopf(2, false);
        Lesen(a, stick, Bereit + 2);
        Pruefe.Wahr(auf.Gelernt.Count == 0, "gehaltener Knopf und Loslassen belegen nicht");
        stick.Knopf(7, true);
        Lesen(a, stick, Bereit + 3);
        Pruefe.Gleich(1, auf.Gelernt.Count, "belegt");
        var b = auf.Gelernt[0];
        Pruefe.Gleich(EingabeArt.Joystick, b.Art, "Joystick");
        Pruefe.Gleich(7, b.Code, "Knopf 7");
        Pruefe.Gleich(stick.Info.Kennung, Geraetekennung.Lesen(b.Geraet), "Kennung mit Pfad, VID/PID und Name");
        Pruefe.Gleich("VPC Stick MT-50CM3 - Knopf 7", Joystickeingabe.Anzeigename(b), "Anzeigename");
        Pruefe.Falsch(a.Lernt, "nur einmal");

        a.LernenStarten(2000);
        stick.Hut(1, Hutrichtung.Links);
        Lesen(a, stick, 2001);
        Pruefe.Gleich(Joystickeingabe.Hut(1, Hutrichtung.Links, stick.Info.Kennung), auf.Gelernt[^1], "Hut 1 links");
        Pruefe.Gleich("VPC Stick MT-50CM3 - Hut 1 links", Joystickeingabe.Anzeigename(auf.Gelernt[^1]), "Anzeigename Hut");

        // Ein eben angestecktes Gerät zählt erst nach der Ruhezeit (DirectInput liefert anfangs Platzhalter).
        a.LernenStarten(3000);
        var neu = s.Anstecken(PfadB);
        Faelliges(a, s, 3000);
        neu.Knopf(4, true);
        Lesen(a, neu, 3100);
        Pruefe.Gleich(2, auf.Gelernt.Count, "in der Ruhezeit nicht belegt");
        neu.Knopf(4, false);
        neu.Knopf(4, true);
        Lesen(a, neu, 3000 + Bereit);
        Pruefe.Gleich(3, auf.Gelernt.Count, "nach der Ruhezeit belegt");
        Pruefe.Gleich(PfadB, Geraetekennung.Lesen(auf.Gelernt[^1].Geraet).Pfad, "vom neuen Gerät");
    }

    [Test]
    public static void Belegen_MitDerBelegtenTaste_SendetNicht()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(7, stick.Info.Kennung)], 0);
        a.LernenStarten(Bereit);
        stick.Knopf(7, true);
        Lesen(a, stick, Bereit);
        Pruefe.Gleich(1, auf.Gelernt.Count, "belegt");
        Pruefe.Falsch(a.IstGedrueckt, "der Druck zum Belegen sendet nicht");
        stick.Knopf(7, false);
        stick.Knopf(7, true);
        Lesen(a, stick, Bereit + 1);
        Pruefe.Wahr(Folge(auf, true), "danach sendet sie");
    }

    [Test]
    public static void FrischDruecken_BeimBelegenGehalten_ZaehltErstNachLoslassen()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        stick.Knopf(1, true);
        stick.Knopf(2, true);
        Lesen(a, stick, Bereit);
        a.BelegungenSetzen([Joystickeingabe.Knopf(1, stick.Info.Kennung)], Bereit);
        Pruefe.Falsch(a.IstGedrueckt, "gehalten beim Belegen: sendet nicht");
        stick.Knopf(1, false);
        Lesen(a, stick, Bereit + 1);
        stick.Knopf(1, true);
        Lesen(a, stick, Bereit + 2);
        Pruefe.Wahr(Folge(auf, true), "nach Loslassen und Drücken");
        a.BelegungenSetzen([Joystickeingabe.Knopf(2, stick.Info.Kennung)], Bereit + 3);
        Pruefe.Wahr(Folge(auf, true, false), "Umbelegen bei gehaltener Taste meldet Loslassen");
        Pruefe.Falsch(a.IstGedrueckt, "Knopf 2 war schon gehalten");
    }

    [Test]
    public static void MehrereBelegungen_WirkenWieEineTaste()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        var pedale = s.Anstecken(PfadB, pid: 0x0198, name: "VPC Pedals R1");
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(1, stick.Info.Kennung), Joystickeingabe.Hut(1, Hutrichtung.Unten, pedale.Info.Kennung)], 0);
        stick.Knopf(1, true);
        Lesen(a, stick, Bereit);
        pedale.Hut(1, Hutrichtung.Unten);
        Lesen(a, pedale, Bereit);
        stick.Knopf(1, false);
        Lesen(a, stick, Bereit);
        Pruefe.Wahr(Folge(auf, true), "gedrückt, solange eine der beiden gedrückt ist");
        pedale.Hut(1, null);
        Lesen(a, pedale, Bereit);
        Pruefe.Wahr(Folge(auf, true, false), "los, wenn beide los sind");
    }

    [Test]
    public static void Ueberlauf_UndVerlorenesLoslassen_StandZaehlt()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(4, stick.Info.Kennung)], 0);
        stick.KnopfUeberlauf(4, true);
        Lesen(a, stick, Bereit);
        Pruefe.Wahr(Folge(auf, true), "Pufferüberlauf: der Stand meldet den Druck");

        // Hängeschutz: Das Loslassen kam weder im Puffer noch als Ereignis. Der Wächter liest nach.
        stick.KnopfStill(4, false);
        var frist = a.NaechsteFrist(Bereit);
        Pruefe.Wahr(frist is > 0 and <= Joystickauswertung.WaechterMs, "Wächter geplant");
        Faelliges(a, s, Bereit + frist);
        Pruefe.Wahr(Folge(auf, true, false), "Wächter meldet das Loslassen");
        Pruefe.Gleich(-1L, a.NaechsteFrist(Bereit + frist), "danach nichts mehr zu tun");
    }

    [Test]
    public static void Schliessen_LaesstGedrueckteTasteLos()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(1, stick.Info.Kennung)], 0);
        stick.Knopf(1, true);
        Lesen(a, stick, Bereit);
        a.Schliessen(Bereit);
        Pruefe.Wahr(Folge(auf, true, false), "losgelassen");
        Pruefe.Gleich(0, stick.Offen, "Zugang zu");
    }

    [Test]
    public static void Schliessen_FreigebenWirft_TasteTrotzdemLosUndAlleZu()
    {
        // Gegenprüfung: Vorher gab Schliessen die Geräte frei und ließ erst danach los. Warf das Freigeben des ersten
        // Geräts, blieb die Taste gedrückt stehen (offenes Mikrofon) und das zweite Gerät offen.
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        var pedale = s.Anstecken(PfadB, pid: 0x0198, name: "VPC Pedals R1");
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(1, stick.Info.Kennung)], 0);
        stick.Knopf(1, true);
        Lesen(a, stick, Bereit);
        Pruefe.Wahr(Folge(auf, true), "gedrückt");
        stick.SchliessenWirft = true;
        _ = Pruefe.Wirft<InvalidOperationException>(() => a.Schliessen(Bereit + 1), "der Fehler beim Freigeben kommt beim Aufrufer an");
        Pruefe.Wahr(Folge(auf, true, false), "trotzdem losgelassen");
        Pruefe.Falsch(a.IstGedrueckt, "nicht mehr gedrückt");
        Pruefe.Gleich(0, a.Anzahl, "keine offenen Geräte mehr");
        Pruefe.Gleich(0, pedale.Offen, "das zweite Gerät ist trotzdem freigegeben");
    }

    [Test]
    public static void Lesethread_FreigebenWirft_LoslassenUndFehlermeldung()
    {
        var (taste, schicht) = Sprechtaste(PfadA);
        var stick = schicht.Geraet(PfadA);
        taste.Belegung = Joystickeingabe.Knopf(1, stick.Info.Kennung);
        Thread.Sleep(Joystickauswertung.RuheMs + 50);
        var los = 0;
        var fehler = 0;
        taste.Losgelassen += () => Interlocked.Increment(ref los);
        taste.Fehler += (_, _) => Interlocked.Increment(ref fehler);
        stick.Knopf(1, true);
        Pruefe.Wahr(Warten(() => taste.IstGedrueckt), "gedrückt");
        stick.SchliessenWirft = true;
        taste.Dispose();
        Pruefe.Gleich(1, Volatile.Read(ref los), "Loslassen trotz Fehler beim Freigeben");
        Pruefe.Falsch(taste.IstGedrueckt, "nicht mehr gedrückt");
        Pruefe.Gleich(1, Volatile.Read(ref fehler), "der Fehler wird gemeldet, nicht verschluckt");
        Pruefe.Gleich(2, schicht.Entsorgungen, "Geräteschicht und Suche trotzdem freigegeben");
    }

    [Test]
    public static void Verbund_FreigabeBeiGedrueckterTaste_MeldetLoslassen()
    {
        // Gegenprüfung: Der Verbund meldete sich vor dem Freigeben von seinen Quellen ab. Deren Loslassen beim Freigeben
        // kam so nicht mehr durch, und wer am Verbund hing, sah die Taste für immer gedrückt.
        var (taste, schicht) = Sprechtaste(PfadA);
        var verbund = new SprechtastenVerbund(taste);
        var stick = schicht.Geraet(PfadA);
        verbund.Belegung = Joystickeingabe.Knopf(3, stick.Info.Kennung);
        Thread.Sleep(Joystickauswertung.RuheMs + 50);
        var gedrueckt = 0;
        var los = 0;
        verbund.Gedrueckt += () => Interlocked.Increment(ref gedrueckt);
        verbund.Losgelassen += () => Interlocked.Increment(ref los);
        stick.Knopf(3, true);
        Pruefe.Wahr(Warten(() => Volatile.Read(ref gedrueckt) == 1), "gedrückt über den Verbund");
        verbund.Dispose();
        Pruefe.Gleich(1, Volatile.Read(ref los), "Freigabe des Verbunds meldet Loslassen");
        Pruefe.Falsch(verbund.IstGedrueckt, "nicht mehr gedrückt");
        verbund.Dispose();
        Pruefe.Gleich(1, Volatile.Read(ref los), "zweite Freigabe meldet nichts mehr");
    }

    [Test]
    public static void Kennung_Codes_Und_Hutwerte()
    {
        var k = new Geraetekennung(PfadA, 0x3344, 0x4130, "VPC Stick | MT-50CM3");
        Pruefe.Gleich("3344:4130|" + PfadA + "|VPC Stick | MT-50CM3", k.ToString(), "Textform");
        Pruefe.Gleich(k, Geraetekennung.Lesen(k.ToString()), "hin und zurück, auch mit | im Namen");
        var nurPfad = Geraetekennung.Lesen("HID#VID_3344&PID_0001#1");
        Pruefe.Gleich((ushort)0x3344, nurPfad.Vid, "VID aus dem bloßen Pfad");
        Pruefe.Gleich((ushort)0x0001, nurPfad.Pid, "PID aus dem bloßen Pfad");
        Pruefe.Gleich("HID#VID_3344&PID_0001#1", nurPfad.Pfad, "Pfad");
        Pruefe.Gleich(new Geraetekennung("", 0, 0, ""), Geraetekennung.Lesen(null), "leer");
        Pruefe.Wahr(new Geraetekennung(@"\\?\ROOT#HIDCLASS#0000#{x}", 1, 2, "").IstVirtuell, "virtuell");
        Pruefe.Falsch(k.IstVirtuell, "echt");

        Pruefe.Wahr(Joystickeingabe.IstKnopf(Joystickeingabe.Knopf(128, k).Code, out var knopf) && knopf == 128, "Knopf 128");
        var hut = Joystickeingabe.Hut(4, Hutrichtung.ObenLinks, k);
        Pruefe.Wahr(Joystickeingabe.IstHut(hut.Code, out var h, out var r) && h == 4 && r == Hutrichtung.ObenLinks, "Hut 4 oben links");
        Pruefe.Falsch(Joystickeingabe.IstGueltig(0), "0 ist nichts");
        Pruefe.Falsch(Joystickeingabe.IstGueltig(129), "129 ist nichts");
        Pruefe.Falsch(Joystickeingabe.IstGueltig(Joystickeingabe.HutBasis + 32), "Hut 5 gibt es nicht");
        _ = Pruefe.Wirft<ArgumentOutOfRangeException>(() => Joystickeingabe.Knopf(0, k), "Knopf 0");
        Pruefe.Gleich("Joystick - Knopf 3", Joystickeingabe.Anzeigename(new Tastenbelegung(EingabeArt.Joystick, 3, PfadA)), "ohne Namen");
        Pruefe.Gleich("Taste 0xE01D", Joystickeingabe.Anzeigename(Tastenbelegung.Taste(0xE01D)), "Tastatur unverändert");

        Pruefe.Gleich(-1, Joystickeingabe.HutRichtungAus(0xFFFFFFFF), "Mitte (-1)");
        Pruefe.Gleich(-1, Joystickeingabe.HutRichtungAus(0xFFFF), "Mitte (65535)");
        Pruefe.Gleich(-1, Joystickeingabe.HutRichtungAus(36000), "ungültig");
        Pruefe.Gleich(0, Joystickeingabe.HutRichtungAus(0), "oben");
        Pruefe.Gleich(1, Joystickeingabe.HutRichtungAus(4500), "oben rechts");
        Pruefe.Gleich(2, Joystickeingabe.HutRichtungAus(9000), "rechts");
        Pruefe.Gleich(4, Joystickeingabe.HutRichtungAus(18000), "unten");
        Pruefe.Gleich(7, Joystickeingabe.HutRichtungAus(31500), "oben links");
        Pruefe.Gleich(0, Joystickeingabe.HutRichtungAus(35000), "knapp vor oben");
        Pruefe.Gleich(2, Joystickeingabe.HutRichtungAus(8000), "gerundet auf rechts");
    }

    [Test]
    public static unsafe void DirectInput_AufbauDerStrukturen_WieDinputH()
    {
        Pruefe.Gleich(24, sizeof(DirectInputNativ.Objektformat), "DIOBJECTDATAFORMAT");
        Pruefe.Gleich(32, sizeof(DirectInputNativ.Datenformat), "DIDATAFORMAT");
        Pruefe.Gleich(24, sizeof(DirectInputNativ.Objektdaten), "DIDEVICEOBJECTDATA");
        Pruefe.Gleich(44, sizeof(DirectInputNativ.Faehigkeiten), "DIDEVCAPS");
        Pruefe.Gleich(16, sizeof(DirectInputNativ.Eigenschaftskopf), "DIPROPHEADER");
        Pruefe.Gleich(20, sizeof(DirectInputNativ.DwordEigenschaft), "DIPROPDWORD");
        Pruefe.Gleich(552, sizeof(DirectInputNativ.PfadEigenschaft), "DIPROPGUIDANDPATH");
        Pruefe.Gleich(1100, sizeof(DirectInputNativ.Geraeteinstanz), "DIDEVICEINSTANCEW");
        Pruefe.Gleich(80, sizeof(DirectInputNativ.Fensterklasse), "WNDCLASSEXW");
        Pruefe.Gleich(32, sizeof(DirectInputNativ.Schnittstellenfilter), "DEV_BROADCAST_DEVICEINTERFACE_W");
        Pruefe.Gleich(DirectInputNativ.Datengroesse, sizeof(DirectInputNativ.Rohstand), "Stand im Datenformat");

        var f = DirectInputNativ.Sprechtastenformat;
        Pruefe.Gleich(132u, f->Objekte, "4 Hüte und 128 Knöpfe, keine Achsen");
        Pruefe.Gleich((uint)DirectInputNativ.Datengroesse, f->DatenGroesse, "Datengröße, Vielfaches von 4");
        Pruefe.Gleich(12u, f->Liste[3].Versatz, "Hut 4");
        Pruefe.Gleich(0x80FFFF10u, f->Liste[0].Typ, "Hut: DIDFT_OPTIONAL | DIDFT_POV | DIDFT_ANYINSTANCE");
        Pruefe.Gleich(16u + 127u, f->Liste[131].Versatz, "Knopf 128");
        Pruefe.Gleich(0x80FFFF0Cu, f->Liste[131].Typ, "Knopf: DIDFT_OPTIONAL | DIDFT_BUTTON | DIDFT_ANYINSTANCE");
        for (var i = 0; i < 132; i++) Pruefe.Wahr(f->Liste[i].Art == null, "jedes Objekt seines Typs");
    }

    // ---------------------------------------------------------------- Lesethread mit der Attrappe

    private static bool Warten(Func<bool> bedingung, int ms = 2000)
    {
        var uhr = Stopwatch.StartNew();
        while (!bedingung())
        {
            if (uhr.ElapsedMilliseconds > ms) return false;
            Thread.Sleep(1);
        }
        return true;
    }

    private static (DirectInputSprechtaste Taste, GeraeteschichtAttrappe Schicht) Sprechtaste(params string[] pfade)
    {
        var schicht = new GeraeteschichtAttrappe();
        foreach (var p in pfade) _ = schicht.Anstecken(p);
        var taste = new DirectInputSprechtaste(() => schicht, () => schicht);
        Pruefe.Wahr(Warten(() => taste.Geraete.Count == pfade.Length), "Geräte aufgezählt");
        return (taste, schicht);
    }

    /// <summary>
    /// Meldung aus dem Lesethread ohne Ereignisobjekt: Blockiert der Wartende (unter Last, etwa neben einem Spiel), weckt
    /// ManualResetEventSlim.Set ihn über Monitor.PulseAll und allokiert dabei im Lesethread. Die Messung schriebe das der
    /// Sprechtaste zu (gemessen 200 Byte in der vollen Suite). Deshalb nur ein Zähler, auf den der Test kurz wartet.
    /// </summary>
    private sealed class Messung : IDisposable
    {
        private long _zeit;
        private int _meldungen;
        private int _gesehen;

        public void Dispose()
        {
        }

        public long Zeit => Volatile.Read(ref _zeit);

        public void Melden()
        {
            Volatile.Write(ref _zeit, Stopwatch.GetTimestamp());
            Interlocked.Increment(ref _meldungen);
        }

        public bool Abwarten(int ms = 1000)
        {
            var bis = Environment.TickCount64 + ms;
            while (Volatile.Read(ref _meldungen) == _gesehen)
            {
                if (Environment.TickCount64 > bis) return false;
                Thread.Yield();
            }
            _gesehen = Volatile.Read(ref _meldungen);
            return true;
        }
    }

    [Test]
    public static void Lesethread_LatenzUnterZehnMillisekunden_OhneAllokation()
    {
        var (taste, schicht) = Sprechtaste(PfadA);
        using var aufraeumen = taste;
        var stick = schicht.Anstecken(PfadB);
        taste.GeraeteNeuSuchen();
        Pruefe.Wahr(Warten(() => taste.Geraete.Count == 2), "zweites Gerät gefunden");
        var kennung = taste.Geraete.First(g => g.Kennung.Pfad == PfadB).Kennung;
        taste.Belegung = Joystickeingabe.Knopf(5, kennung);
        Thread.Sleep(Joystickauswertung.RuheMs + 50);
        using var gedrueckt = new Messung();
        using var losgelassen = new Messung();
        taste.Gedrueckt += gedrueckt.Melden;
        taste.Losgelassen += losgelassen.Melden;
        const int Runden = 200;
        var latenz = new double[2 * Runden];
        var vorher = taste.Statistik.AllokiertImTakt;
        for (var i = 0; i < Runden; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            stick.Knopf(5, true);
            Pruefe.Wahr(gedrueckt.Abwarten(), "Gedrückt kam");
            latenz[2 * i] = Stopwatch.GetElapsedTime(t0, gedrueckt.Zeit).TotalMilliseconds;
            Pruefe.Wahr(taste.IstGedrueckt, "IstGedrueckt");
            t0 = Stopwatch.GetTimestamp();
            stick.Knopf(5, false);
            Pruefe.Wahr(losgelassen.Abwarten(), "Losgelassen kam");
            latenz[(2 * i) + 1] = Stopwatch.GetElapsedTime(t0, losgelassen.Zeit).TotalMilliseconds;
        }
        Array.Sort(latenz);
        var p50 = latenz[latenz.Length / 2];
        var p99 = latenz[(int)(latenz.Length * 0.99)];
        Console.WriteLine(FormattableString.Invariant($"  Joystick-Lesethread: Latenz p50 {p50:F3} ms, p99 {p99:F3} ms, max {latenz[^1]:F3} ms ({latenz.Length} Wechsel)"));
        Pruefe.Wahr(p50 < 10, "Median unter 10 ms");
        Pruefe.Wahr(p99 < 50, "p99 unter 50 ms (großzügig, die Suite läuft parallel zu anderem)");
        Pruefe.Gleich(vorher, taste.Statistik.AllokiertImTakt, "keine Allokation im Takt");
        Pruefe.Gleich(0L, taste.Statistik.Fehler, "keine Fehler");
    }

    [Test]
    public static async Task Lesethread_BelegenUeberVerbund_UndAbbrechen()
    {
        var (taste, schicht) = Sprechtaste(PfadA);
        using var verbund = new SprechtastenVerbund(taste);
        var stick = schicht.Anstecken(PfadB, name: "VPC Throttle CM3");
        using (var kurz = new CancellationTokenSource(50))
        {
            var abgebrochen = false;
            try
            {
                _ = await taste.NaechsteTasteAsync(kurz.Token);
            }
            catch (OperationCanceledException)
            {
                abgebrochen = true;
            }
            Pruefe.Wahr(abgebrochen, "Belegen abgebrochen");
        }
        var lernen = verbund.NaechsteTasteAsync(CancellationToken.None);
        Pruefe.Wahr(Warten(() => taste.Geraete.Count == 2), "Belegen zählt neu auf, das neue Gerät ist dabei");
        Thread.Sleep(Joystickauswertung.RuheMs + 50);
        stick.Hut(1, Hutrichtung.Rechts);
        var belegt = await lernen.WaitAsync(TimeSpan.FromSeconds(2));
        Pruefe.Gleich("VPC Throttle CM3 - Hut 1 rechts", DirectInputSprechtaste.Anzeigename(belegt), "Hut rechts vom neuen Gerät");
        verbund.Belegung = belegt;
        Pruefe.Gleich(belegt, taste.Belegung, "Belegung an der Joystick-Quelle");
        Pruefe.Wahr(taste.IstAngeschlossen(belegt), "angeschlossen");
        stick.Hut(1, null);
        var gedrueckt = 0;
        verbund.Gedrueckt += () => Interlocked.Increment(ref gedrueckt);
        stick.Hut(1, Hutrichtung.UntenRechts);
        Pruefe.Wahr(Warten(() => Volatile.Read(ref gedrueckt) == 1), "Hut rechts (schräg unten) sendet über den Verbund");
        Pruefe.Wahr(verbund.IstGedrueckt, "Verbund gedrückt");
        schicht.Abstecken(stick);
        Pruefe.Wahr(Warten(() => !verbund.IstGedrueckt), "Abstecken lässt los");
        Pruefe.Wahr(Warten(() => !taste.IstAngeschlossen(belegt)), "nicht mehr angeschlossen");
        _ = Pruefe.Wirft<NotSupportedException>(() => taste.Belegung = Tastenbelegung.Taste(0x1D), "Tastatur abgelehnt");
        _ = Pruefe.Wirft<ArgumentException>(() => taste.Belegung = new Tastenbelegung(EingabeArt.Joystick, 0, PfadA), "Code 0 abgelehnt");
    }

    [Test]
    public static async Task Lesethread_FreigabeAusJedemThread()
    {
        // Dispose bei gedrückter Taste aus einem anderen Thread: Loslassen kommt, offenes Belegen wird abgebrochen.
        var (taste, schicht) = Sprechtaste(PfadA);
        var stick = schicht.Anstecken(PfadB);
        taste.GeraeteNeuSuchen();
        Pruefe.Wahr(Warten(() => taste.Geraete.Count == 2), "zwei Geräte");
        taste.Belegungen = [Joystickeingabe.Knopf(1, taste.Geraete.First(g => g.Kennung.Pfad == PfadB).Kennung)];
        Thread.Sleep(Joystickauswertung.RuheMs + 50);
        var los = 0;
        taste.Losgelassen += () => Interlocked.Increment(ref los);
        stick.Knopf(1, true);
        Pruefe.Wahr(Warten(() => taste.IstGedrueckt), "gedrückt");
        var lernen = taste.NaechsteTasteAsync(CancellationToken.None);
        await Task.Run(taste.Dispose);
        Pruefe.Gleich(1, Volatile.Read(ref los), "Dispose meldet Loslassen");
        Pruefe.Falsch(taste.IstGedrueckt, "nicht mehr gedrückt");
        Pruefe.Wahr(lernen.IsCanceled, "Belegen abgebrochen");
        Pruefe.Gleich(2, schicht.Entsorgungen, "Geräteschicht und Suche freigegeben");
        Pruefe.Gleich(0, stick.Offen, "Gerät geschlossen");
        taste.Dispose();
        taste.Belegung = null;
        taste.GeraeteNeuSuchen();
        Pruefe.Wahr(taste.NaechsteTasteAsync(CancellationToken.None).IsCanceled, "Belegen nach Dispose: abgebrochen");

        // Dispose aus dem eigenen Ereignis heraus (Lesethread): kein Hängen, die Schleife endet danach.
        var (zweite, schicht2) = Sprechtaste(PfadA);
        var stick2 = schicht2.Anstecken(PfadB);
        zweite.GeraeteNeuSuchen();
        Pruefe.Wahr(Warten(() => zweite.Geraete.Count == 2), "zwei Geräte");
        zweite.Belegung = Joystickeingabe.Knopf(2, zweite.Geraete.First(g => g.Kennung.Pfad == PfadB).Kennung);
        Thread.Sleep(Joystickauswertung.RuheMs + 50);
        zweite.Gedrueckt += zweite.Dispose;
        stick2.Knopf(2, true);
        Pruefe.Wahr(Warten(() => schicht2.Entsorgungen == 2), "Lese- und Suchthread enden nach Dispose im Ereignis");
        Pruefe.Falsch(zweite.IstGedrueckt, "und meldet Loslassen");
    }

    [Test]
    public static void Lesethread_LangsameSuche_HaeltDasLesenNichtAuf()
    {
        var (taste, schicht) = Sprechtaste(PfadA);
        using var aufraeumen = taste;
        var stick = schicht.Geraet(PfadA);
        taste.Belegung = Joystickeingabe.Knopf(1, stick.Info.Kennung);
        Thread.Sleep(Joystickauswertung.RuheMs + 50);
        using var gedrueckt = new Messung();
        taste.Gedrueckt += gedrueckt.Melden;
        schicht.SucheDauertMs = 600;
        var suchenVorher = schicht.Aufzaehlungen;
        taste.GeraeteNeuSuchen();
        Thread.Sleep(100); // die Suche läuft jetzt im Suchthread
        var t0 = Stopwatch.GetTimestamp();
        stick.Knopf(1, true);
        Pruefe.Wahr(gedrueckt.Abwarten(), "gedrückt");
        var ms = Stopwatch.GetElapsedTime(t0, gedrueckt.Zeit).TotalMilliseconds;
        Console.WriteLine(FormattableString.Invariant($"  Joystick: Druck während einer Suche von {schicht.SucheDauertMs} ms nach {ms:F3} ms gemeldet"));
        Pruefe.Wahr(ms < 10, "Drücken während der Suche unter 10 ms");
        Pruefe.Gleich(suchenVorher, schicht.Aufzaehlungen, "die Suche lief noch");
        schicht.SucheDauertMs = 0;
    }

    [Test]
    public static void GescheiterteSuche_LaesstGeraeteOffen_UndVersuchtEsWieder()
    {
        var (a, s, auf) = Neu();
        var stick = s.Anstecken(PfadA);
        Aufzaehlen(a, s, 0);
        a.BelegungenSetzen([Joystickeingabe.Knopf(1, stick.Info.Kennung), Joystickeingabe.Knopf(1, new Geraetekennung(PfadB, 0x3344, 0x0194, "fehlt"))], 0);
        Pruefe.Wahr(a.Faelliges(0), "eine Belegung ohne Gerät: gleich suchen");
        a.Abgleichen(null, 0);
        Pruefe.Gleich(1, a.Anzahl, "gescheiterte Suche schließt nichts");
        Pruefe.Wahr(a.NaechsteFrist(0) is > 0 and <= Joystickauswertung.SuchabstandMs, "später wieder versuchen");
        stick.Knopf(1, true);
        Lesen(a, stick, Bereit);
        Pruefe.Wahr(Folge(auf, true), "das offene Gerät liest weiter");
    }

    [Test]
    public static void Nachsuchen_OhneGeraet_WirdSeltener_UndNachMeldungWiederKurz()
    {
        // Gegenprüfung: Vorher lief die Suche alle 3 s, solange eine Belegung ihr Gerät nicht fand, den ganzen Abend
        // lang und neben Star Citizen; eine Aufzählung kostet gemessen 140 bis 190 ms Rechenzeit.
        var (a, s, _) = Neu();
        a.BelegungenSetzen([Joystickeingabe.Knopf(1, new Geraetekennung(PfadA, 0x3344, 0x4130, GeraeteschichtAttrappe.Name))], 0);
        var t = 0L;
        var abstaende = new List<long>();
        for (var i = 0; i < 7; i++)
        {
            var frist = a.NaechsteFrist(t);
            abstaende.Add(frist);
            t += frist;
            Faelliges(a, s, t);
        }
        Pruefe.Gleich("0 3000 6000 12000 24000 30000 30000", string.Join(' ', abstaende), "gleich, dann doppelt so selten bis 30 s");

        // Windows meldet ein Gerät: gleich, nach einer Sekunde und dann wieder im kurzen Abstand.
        a.Geraeteaenderung(t);
        Pruefe.Gleich(0L, a.NaechsteFrist(t), "sofort");
        Faelliges(a, s, t);
        Pruefe.Gleich((long)Joystickauswertung.NachsucheMs, a.NaechsteFrist(t), "Nachsuche nach einer Sekunde");
        t += Joystickauswertung.NachsucheMs;
        Faelliges(a, s, t);
        Pruefe.Gleich((long)(Joystickauswertung.SuchabstandMs - Joystickauswertung.NachsucheMs), a.NaechsteFrist(t), "danach wieder der kurze Abstand");

        // Beim Belegen bleibt es beim kurzen Abstand, auch über viele Runden.
        a.LernenStarten(t);
        Pruefe.Gleich(0L, a.NaechsteFrist(t), "Belegen zählt sofort auf");
        for (var i = 0; i < 5; i++)
        {
            Faelliges(a, s, t);
            var frist = a.NaechsteFrist(t);
            Pruefe.Gleich((long)Joystickauswertung.SuchabstandMs, frist, "beim Belegen alle 3 s");
            t += frist;
        }
        a.LernenBeenden();

        // Kommt das Gerät, ist Schluss; nach einem Verlust beginnt es wieder kurz.
        var stick = s.Anstecken(PfadA);
        t += a.NaechsteFrist(t);
        Faelliges(a, s, t);
        Pruefe.Gleich(1, a.Anzahl, "gefunden");
        Pruefe.Gleich(-1L, a.NaechsteFrist(t), "kein Nachsuchen mehr");
        s.Abstecken(stick, windowsMeldet: false);
        Lesen(a, stick, t + 10);
        Pruefe.Gleich((long)Joystickauswertung.NachsucheMs, a.NaechsteFrist(t + 10), "verloren: nach einer Sekunde nachsehen");
        Faelliges(a, s, t + 10 + Joystickauswertung.NachsucheMs);
        Pruefe.Gleich((long)Joystickauswertung.SuchabstandMs, a.NaechsteFrist(t + 10 + Joystickauswertung.NachsucheMs), "dann im kurzen Abstand");
    }

    [Test]
    public static void Lesethread_GeraetOhneEreignis_UeberDenTakt()
    {
        var schicht = new GeraeteschichtAttrappe();
        var stick = schicht.Anstecken(PfadA, mussAbfragen: true);
        using var taste = new DirectInputSprechtaste(() => schicht, () => schicht);
        Pruefe.Wahr(Warten(() => taste.Geraete.Count == 1), "aufgezählt");
        taste.Belegung = Joystickeingabe.Knopf(1, stick.Info.Kennung);
        Thread.Sleep(Joystickauswertung.RuheMs + 50);
        using var gedrueckt = new Messung();
        taste.Gedrueckt += gedrueckt.Melden;
        var latenz = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            stick.Knopf(1, true);
            Pruefe.Wahr(gedrueckt.Abwarten(), "gedrückt über den Takt");
            latenz.Add(Stopwatch.GetElapsedTime(t0, gedrueckt.Zeit).TotalMilliseconds);
            stick.Knopf(1, false);
            Pruefe.Wahr(Warten(() => !taste.IstGedrueckt), "losgelassen");
        }
        latenz.Sort();
        Console.WriteLine(FormattableString.Invariant($"  Joystick-Takt ({DirectInputSprechtaste.AbfrageMs} ms): Latenz p50 {latenz[latenz.Count / 2]:F2} ms, max {latenz[^1]:F2} ms"));
        Pruefe.Wahr(latenz[latenz.Count / 2] < 10, "Median unter 10 ms");
    }

    [Test]
    public static void Lesethread_Startfehler_WirdGeworfen()
    {
        var ex = Pruefe.Wirft<InvalidOperationException>(
            () => new DirectInputSprechtaste(() => throw new InvalidOperationException("kein DirectInput"), () => new GeraeteschichtAttrappe()).Dispose(), "Startfehler");
        Pruefe.Gleich("kein DirectInput", ex.InnerException?.Message, "Ursache");
    }
}
