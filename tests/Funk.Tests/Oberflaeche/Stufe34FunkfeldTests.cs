// JG TacRelay - Maintainer: Sarevoc
using System.Reflection;
using System.Text;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Stufe 3, Schritt 3.4 (STUFE3-ENTWURF 5.1 bis 5.4 mit Sarevocs Antworten in Abschnitt 9, FUNKPROTOKOLL 6.16 und 6.17): das Funkfeld kennt
/// Kanäle mit Ch. Rollenliste oben links mit Nummer ("3 Kapitän"), Ch.-Wähler je Feld mit den Bezeichnungen der Ch.-Liste
/// (Ruhe ab Werk, 400 ms Ruhe vor dem Senden, gesperrte nicht wählbar), seit 0.8.1 der Feldname als feste Beschriftung (Q44, Q45),
/// Statuspunkte, Mehrfachsenden als Schalter, Einstellungen je Kanalnetz, Rolle nach der Wiederverbindung, Meldekanal, Skalierung.
/// Über der Attrappe mit den Vorführdaten der Vorlage Star Citizen (Max Mustermann als Kapitän), ohne Fenster.
/// </summary>
internal static class Stufe34FunkfeldTests
{
    private static ushort Netz(FunkLage l, string name) => l.Netze.Single(n => n.Name == name).Id;

    private static ushort K(FunkLage l, string name, byte ch) => Kanalrechnung.Id(Netz(l, name), ch);

    private static ushort RolleId(FunkLage l, string name) => l.Rollen.Single(r => r.Name == name).Id;

    /// <summary>
    /// Die Lage mit Global als Kanal mit Ch. (Ruhe, Gesamt, Global 1 bis 20), wie es die Konsole setzen kann; die Vorlage Star Citizen lässt
    /// Global ohne Ch.
    /// </summary>
    internal static FunkLage MitGlobalKanaelen(FunkLage l) => l with
    {
        Netze = [.. l.Netze.Select(n => n.IstGlobal ? n with { Kanaele = ["Ruhe", "Gesamt", .. Enumerable.Range(1, 20).Select(i => "Global " + i)] } : n)],
    };

    private static IReadOnlyList<string> Aufrufe(Aufbau a, string beginn) => [.. a.Dienst.Aufrufe.Where(x => x.StartsWith(beginn, StringComparison.Ordinal))];

    /// <summary>Lautstärke und Ohr, wie sie der Klang je Kanal-ID bekommt.</summary>
    private sealed class KlangProtokoll : INetzKlang
    {
        public Dictionary<ushort, (float Db, float Panorama)> Zuletzt { get; } = [];

        public event Action<ushort, float>? NetzPegel
        {
            add { }
            remove { }
        }

        public void NetzEinstellen(ushort netzId, float db, float panorama) => Zuletzt[netzId] = (db, panorama);
    }

    [Test]
    public static void Rollenliste_NummerSortiertNurSelbstWaehlbar()
    {
        using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel());
        var f = a.Funkfeld();
        Pruefe.Gleich(14, f.Rollen.Count, "die 14 Rollen der Vorlage Star Citizen");
        Pruefe.Gleich("1 Standard (Gast)", f.Rollen[0].Text, "sortiert, Nummer davor");
        Pruefe.Gleich("3 Kapitän", f.Rollen[2].Text, "die Rolle der Vorführung");
        Pruefe.Gleich("14 Funker (Leitstand)", f.Rollen[^1].Text, "die letzte");
        Pruefe.Gleich("3 Kapitän", f.Rolle?.Text, "die gewählte Rolle steht oben links");
        Pruefe.Wahr(f.Rollen[2].Hilfe.Contains("Brücke, Crew, Flotte, Kommando, Sanität, Logistik", StringComparison.Ordinal),
            "der Tooltip nennt die Kanäle: " + f.Rollen[2].Hilfe);
        Pruefe.Wahr(f.RolleWaehlbar, "wählbar am Server 0.6.0");

        // Rollen ohne Nummer im Namen bekommen ihre Reihenfolge davor; nicht selbst wählbare fehlen (RolleWaehlen lehnte sie ab).
        using var b = new Aufbau(FunkDienstAttrappe.Beispiel() with { ServerKanaele = true });
        var g = b.Funkfeld();
        Pruefe.Gleich("1 Kapitän|2 Pilot|3 Gunner|4 Ingenieur|5 Jäger", string.Join("|", g.Rollen.Select(r => r.Text)), "Nummer aus der Reihenfolge");
        Pruefe.Gleich(null, g.Rolle, "ohne gewählte Rolle steht keine da");

        // Ein Server vor 0.6.0 kennt RolleWaehlen nicht: die Liste ist gesperrt und sagt warum.
        using var c = new Aufbau(FunkDienstAttrappe.KanalBeispiel() with { ServerKanaele = false });
        var h = c.Funkfeld();
        Pruefe.Falsch(h.RolleWaehlbar, "Server 0.5: nicht wählbar");
        Pruefe.Wahr(h.RolleHilfe.Contains("Server", StringComparison.Ordinal), "Grund: " + h.RolleHilfe);
    }

    [Test]
    public static void Rollenliste_Wahl_Nach400msRuhe_OhneWuensche()
    {
        // Bis 0.8.0 gingen die gemerkten Ch. als Wünsche mit; seit 0.8.1 (Q46) beginnt jeder Ch. auf Ruhe, alte Merkwerte gelten nicht.
        var l = FunkDienstAttrappe.KanalBeispiel();
        using var a = new Aufbau(l);
        a.Ablage.Aktuell.NetzFuer(Netz(l, "Brücke")).Ch = 5;
        a.Ablage.Aktuell.NetzFuer(Netz(l, "Jäger")).Ch = 4;
        var f = a.Funkfeld();
        var ziel = f.Rollen.Single(r => r.Text == "4 Pilot");
        f.Rolle = ziel;
        Pruefe.Gleich(ziel, f.Rolle, "die Wahl steht sofort da");
        a.Uhr += FunkfeldViewModel.RuheMs - 1;
        f.Takt();
        Pruefe.Gleich(0, Aufrufe(a, "RolleWaehlen").Count, "vor 400 ms Ruhe geht nichts hinaus");
        a.Uhr += 1;
        f.Takt();
        Pruefe.Gleich("RolleWaehlen " + ziel.Id + " []", Aufrufe(a, "RolleWaehlen").SingleOrDefault(), "nach 400 ms, ohne Wünsche");
        Pruefe.Gleich((int?)ziel.Id, a.Ablage.Aktuell.Funkfeld.Rolle, "die Rolle ist gemerkt");
        var lage = a.Dienst.Lage;
        Pruefe.Gleich(K(l, "Brücke", 3), lage.FeldVon(0)?.NetzId, "Brücke behält Schiff 3 (der Ch., den das Konto schon hat, 6.16), der Merkwert 5 gilt nicht");
        Pruefe.Gleich(K(l, "Flotte", 1), lage.FeldVon(1)?.NetzId, "Flotte fest auf Allgemein");
        Pruefe.Gleich(K(l, "Crew", 3), lage.FeldVon(2)?.NetzId, "Crew behält Schiff 3 aus einem anderen Feld");
        Pruefe.Gleich("4 Pilot", f.Rolle?.Text, "die neue Rolle steht oben links");

        // Mit der Maus gewählt (die Liste klappt zu): sofort, ohne die 400 ms. Jäger hatte das Konto nicht: Ruhe, der Merkwert 4 gilt nicht.
        var jaeger = f.Rollen.Single(r => r.Text == "8 Jägerpilot");
        f.Rolle = jaeger;
        f.VormerkungenAbschicken();
        Pruefe.Gleich("RolleWaehlen " + jaeger.Id + " []", Aufrufe(a, "RolleWaehlen")[^1], "sofort");
        Pruefe.Gleich(K(l, "Jäger", 0), a.Dienst.Lage.FeldVon(0)?.NetzId, "Jäger (Staffel n) beginnt auf Ruhe");
    }

    [Test]
    public static void ChWaehler_BezeichnungenRuheSperrenUndGlobal()
    {
        var b = FunkDienstAttrappe.KanalBeispiel();
        var bis = b.Gesperrt[0].Bis;
        var l = b with
        {
            Felder = [.. b.Felder.Where(x => x.Feld != 7), new FeldInfo(7, K(b, "Brücke", 5), true, true, false)],
            Gesperrt = [.. b.Gesperrt, new KanalsperreInfo(K(b, "Brücke", 7), bis, 4, false)],
        };
        using var a = new Aufbau(l);
        var f = a.Funkfeld();
        var bridge = f.Kacheln[0];
        Pruefe.Wahr(bridge.HatCh, "Ch.-Wähler an einem Kanalnetz");
        Pruefe.Gleich("Brücke", bridge.Name, "Name des Kanalnetzes");
        Pruefe.Gleich((byte?)3, bridge.Ch, "Ch. 3");
        Pruefe.Gleich("Schiff 3", bridge.ChText, "die Bezeichnung aus der Ch.-Liste");
        Pruefe.Gleich(14, bridge.ChListe.Count, "Ruhe, Schiff 1 bis 12 und Alle");
        Pruefe.Gleich("Ruhe", bridge.ChListe[0].Text, "Index 0 ist die Ruhe");
        Pruefe.Gleich("Alle", bridge.ChListe[^1].Text, "der gemeinsame Ch. am Ende");
        Pruefe.Gleich("Schiff 3", bridge.ChWahl?.Text, "gewählt");
        Pruefe.Wahr(bridge.ChWaehlbar, "wählbar");
        var sieben = bridge.ChListe[7];
        Pruefe.Falsch(sieben.Waehlbar, "Schiff 7 gesperrt: nicht wählbar");
        Pruefe.Wahr(sieben.Grund?.StartsWith("Gesperrt von Nordlicht bis 04:00", StringComparison.Ordinal) == true, "mit Grund: " + sieben.Grund);
        var fuenf = bridge.ChListe[5];
        Pruefe.Falsch(fuenf.Waehlbar, "Schiff 5 liegt schon in Feld 8");
        Pruefe.Gleich("Liegt schon in Feld 8.", fuenf.Grund, "Grund");

        Pruefe.Gleich("Ruhe", f.Kacheln[4].ChText, "Sanität auf Ruhe");
        Pruefe.Gleich("Fracht", f.Kacheln[5].ChText, "Logistik auf Ch. 1");
        var w4 = f.Kacheln[6];
        Pruefe.Gleich("Reserve 1", w4.ChText, "Allgemein auf Reserve 1");
        Pruefe.Falsch(w4.ChWaehlbar, "im gesperrten Feld kein Ch.-Wechsel");
        Pruefe.Wahr(w4.ChGrund?.StartsWith("Gesperrt von Nordlicht bis 04:00", StringComparison.Ordinal) == true, "Grund: " + w4.ChGrund);

        var global = f.Kacheln[^1];
        Pruefe.Falsch(global.HatCh, "Global hat keinen Wähler");
        Pruefe.Gleich("", global.ChText, "Global ohne Ch. (die Vorlage lässt es so)");
        Pruefe.Gleich(KachelViewModel.HerkunftGlobal, global.Herkunft, "Global ist nicht änderbar, der Tooltip sagt es");
        using (var mit = new Aufbau(MitGlobalKanaelen(l)))
        {
            var g = mit.Funkfeld().Kacheln[^1];
            Pruefe.Wahr(!g.HatCh && g.ChText == "Gesamt", "Global mit Ch.: fest auf Ch. 1, ohne Wähler");
        }

        // Ein gewöhnliches Netz hat keinen Ch.
        using var c = new Aufbau(FunkDienstAttrappe.Beispiel());
        Pruefe.Falsch(c.Funkfeld().Kacheln[0].HatCh, "Kommando ohne Ch.-Liste: kein Wähler");
    }

    [Test]
    public static void ChWaehler_Wechsel_Nach400msRuhe_FeldSetzenMitKanalId()
    {
        var l = FunkDienstAttrappe.KanalBeispiel();
        using var a = new Aufbau(l);
        var f = a.Funkfeld();
        var bridge = f.Kacheln[0];
        bridge.ChWahl = bridge.ChListe[5];
        Pruefe.Gleich("Schiff 5", bridge.ChText, "sofort angezeigt");
        a.Uhr += FunkfeldViewModel.RuheMs - 1;
        f.Takt();
        bridge.ChWahl = bridge.ChListe[6];
        a.Uhr += FunkfeldViewModel.RuheMs - 1;
        f.Takt();
        Pruefe.Gleich(0, Aufrufe(a, "FeldSetzen").Count, "solange gewählt wird, geht nichts hinaus");
        a.Uhr += 1;
        f.Takt();
        Pruefe.Gleich("FeldSetzen 0 " + K(l, "Brücke", 6), Aufrufe(a, "FeldSetzen").SingleOrDefault(), "ein FeldSetzen nach 400 ms Ruhe, mit der Kanal-ID");
        Pruefe.Gleich((int?)null, a.Ablage.Aktuell.NetzOderNull(Netz(l, "Brücke"))?.Ch, "seit 0.8.1 merkt sich der Client den Ch. nicht (Q46)");
        Pruefe.Gleich("Schiff 6", f.Kacheln[0].ChText, "die Lage zeigt Schiff 6");

        // Mausrad und Pfeiltasten: ein Schritt je Raste, gesperrte werden übersprungen, Ruhe ist der Anfang.
        var w4 = f.Kacheln[6];
        w4.ChSchritt(+1);
        Pruefe.Gleich("Reserve 1", w4.ChText, "im gesperrten Feld bewegt das Mausrad nichts");
        var crew = f.Kacheln[1];
        crew.ChSchritt(+1);
        Pruefe.Gleich("Schiff 4", crew.ChText, "+1");
        crew.ChSchritt(-1);
        crew.ChSchritt(-1);
        crew.ChSchritt(-1);
        crew.ChSchritt(-1);
        Pruefe.Gleich("Ruhe", crew.ChText, "bis zur Ruhe");
        crew.ChSchritt(-1);
        Pruefe.Gleich("Ruhe", crew.ChText, "unter der Ruhe ist nichts");
        f.VormerkungenAbschicken();
        Pruefe.Gleich("FeldSetzen 1 " + K(l, "Crew", 0), Aufrufe(a, "FeldSetzen")[^1], "beim Zuklappen sofort");

        // Ein gesperrter Ch. lässt sich nicht wählen; die Wahl bleibt, wie sie war.
        using var b = new Aufbau(l with { Gesperrt = [.. l.Gesperrt, new KanalsperreInfo(K(l, "Crew", 4), l.Gesperrt[0].Bis, 4, false)] });
        var g = b.Funkfeld();
        var crewB = g.Kacheln[1];
        crewB.ChWahl = crewB.ChListe[4];
        Pruefe.Gleich("Schiff 3", crewB.ChText, "Schiff 4 gesperrt: bleibt auf Schiff 3");
        crewB.ChSchritt(+1);
        Pruefe.Gleich("Schiff 5", crewB.ChText, "das Mausrad springt über Schiff 4");
    }

    /// <summary>Alle Mitglieder einer Klasse, auch die nicht öffentlichen (für "entfernt statt stillgelegt").</summary>
    private const BindingFlags AlleMitglieder = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Test]
    public static void Feldname_FesteBeschriftung_ChAuchImFestenFeld_NurSperreUndVerbindungSperren()
    {
        // Q44 (0.8.1): Am Namen wählt der Funker nichts mehr; den Kanal eines Felds legen Rolle, Admin und Einsatzleitung fest.
        // Was nur der Kanalwahl am Namen diente, ist entfernt, nicht stillgelegt.
        foreach (var weg in new[] { "KanalListe", "Kanal", "KanalWaehlbar", "KanalHilfe", "NameHilfe", "KanalWaehlen", "KanalWaehlenCommand", "FestText" })
        {
            Pruefe.Gleich(0, typeof(KachelViewModel).GetMember(weg, AlleMitglieder).Length, "KachelViewModel." + weg + " entfernt");
        }
        foreach (var weg in new[] { "KanalSetzenAsync", "FreierCh", "KanalEintraege", "Gedeckt" })
        {
            Pruefe.Gleich(0, typeof(FunkfeldViewModel).GetMember(weg, AlleMitglieder).Length, "FunkfeldViewModel." + weg + " entfernt");
        }
        Pruefe.Gleich(null, typeof(KachelViewModel).Assembly.GetType("Funk.Client.Modelle.KanalEintrag"), "KanalEintrag entfernt");
        // Die Gruppe eines Netzes brauchte bis 0.11.1 nur die Kanalwahl und war entfernt; seit 0.11.2 trägt sie die Reiter der Raid-Seite
        // (BrettViewModel.Reiter, aus der NetzListe gemappt in LageAbbildung).
        Pruefe.Gleich(1, typeof(NetzInfo).GetMember("Gruppe", AlleMitglieder).Length, "NetzInfo.Gruppe für die Reiter der Raid-Seite");

        var l = FunkDienstAttrappe.KanalBeispiel();
        using var a = new Aufbau(l with { Felder = [.. l.Felder.Select(x => x.Feld == 1 ? x with { Fest = true } : x)] });
        var f = a.Funkfeld();

        // Der Tooltip der Kachel sagt, woher der Kanal kommt: aus der Rolle, fest eingetragen oder Global; ein leeres Feld bleibt "frei".
        var bridge = f.Kacheln[0];
        Pruefe.Gleich(KachelViewModel.HerkunftRolle, bridge.Herkunft, "aus der Rolle");
        Pruefe.Wahr(bridge.KopfHilfe.StartsWith("Brücke (Schiff 3): " + KachelViewModel.HerkunftRolle + " Dein Sendekanal", StringComparison.Ordinal),
            "Tooltip mit Namen, Ch. und Herkunft vor dem Zustand: " + bridge.KopfHilfe);
        var crew = f.Kacheln[1];
        Pruefe.Wahr(crew.Fest, "Feld 2 ist fest");
        Pruefe.Gleich(KachelViewModel.HerkunftFest, crew.Herkunft, "fest eingetragen");
        var leer = f.Kacheln[7];
        Pruefe.Wahr(leer.IstLeer && leer.Name == "frei" && leer.Herkunft is null, "ein leeres Feld: frei, ohne Herkunft");
        Pruefe.Wahr(leer.KopfHilfe.StartsWith("Kein Kanal in diesem Feld.", StringComparison.Ordinal), "Tooltip: " + leer.KopfHilfe);
        using (var ohne = new Aufbau(l with { Rolle = null }))
        {
            Pruefe.Gleich(KachelViewModel.HerkunftOhneRolle, ohne.Funkfeld().Kacheln[0].Herkunft, "ohne gewählte Rolle legt der Server die Kanäle fest");
        }

        // Q45: "fest" sperrt den Ch. nicht mehr, der Funker wechselt ihn wie in jedem anderen Feld.
        Pruefe.Wahr(crew.ChWaehlbar && crew.ChGrund is null, "fest: Ch. wählbar, ohne Grund dagegen");
        Pruefe.Wahr(crew.ChHilfe.StartsWith("Ch. wählen", StringComparison.Ordinal), "Tooltip am Ch.-Wähler: " + crew.ChHilfe);
        crew.ChWahl = crew.ChListe[5];
        Pruefe.Gleich("Schiff 5", crew.ChText, "sofort angezeigt");
        f.VormerkungenAbschicken();
        Pruefe.Gleich("FeldSetzen 1 " + K(l, "Crew", 5), Aufrufe(a, "FeldSetzen").SingleOrDefault(), "geht hinaus wie in jedem Feld");

        // Den Ch. sperren nur die Kanalsperre der Einsatzleitung (6.17), die fehlende Verbindung und Global (ohne Wähler).
        var w4 = f.Kacheln[6];
        Pruefe.Falsch(w4.ChWaehlbar, "gesperrt");
        Pruefe.Wahr(w4.ChGrund?.StartsWith("Gesperrt von Nordlicht bis 04:00", StringComparison.Ordinal) == true, "Grund: " + w4.ChGrund);
        Pruefe.Falsch(f.Kacheln[^1].ChWaehlbar, "Global ohne Wähler");
        a.Dienst.LageSetzen(x => x with { Status = Verbindungsstatus.Verbinden });
        Pruefe.Falsch(crew.ChWaehlbar || bridge.ChWaehlbar, "nicht verbunden");
        Pruefe.Gleich("Nicht verbunden.", crew.ChGrund, "Grund");
    }

    [Test]
    public static async Task Statuspunkte_AlleZustaende_MitWortUndTooltip()
    {
        var l = FunkDienstAttrappe.KanalBeispiel();
        using var a = new Aufbau(l);
        var f = a.Funkfeld();
        var erwartet = new (int Platz, KachelZustand Zustand, string Text, bool Punkt, string Hilfe)[]
        {
            (0, KachelZustand.Senden, "SENDET", true, "Dein Sendekanal"),
            (1, KachelZustand.Hoeren, "HÖRT", true, "Du hörst diesen Kanal"),
            (6, KachelZustand.Gesperrt, "GESPERRT", true, "Gesperrt von Nordlicht bis 04:00"),
            (5, KachelZustand.Aus, "AUS", true, "Ganz aus, du hörst nichts"),
            (4, KachelZustand.Ruhe, "RUHE", false, "Ruhe: Dieser Kanal hört und sendet nichts. Wähle einen Ch."),
            (7, KachelZustand.Leer, "FREI", false, "Kein Kanal in diesem Feld"),
        };
        foreach (var (platz, zustand, text, punkt, hilfe) in erwartet)
        {
            var k = f.Kacheln[platz];
            Pruefe.Gleich(zustand, k.Zustand, "Feld " + (platz + 1));
            Pruefe.Gleich(text, k.ZustandText, "Wort zur Farbe, Feld " + (platz + 1));
            Pruefe.Gleich(punkt, k.HatPunkt, "Punkt, Feld " + (platz + 1));
            Pruefe.Wahr(k.PunktHilfe.StartsWith(hilfe, StringComparison.Ordinal), "Tooltip Feld " + (platz + 1) + ": " + k.PunktHilfe);
        }
        Pruefe.Gleich(KachelZustand.Hoeren, f.Kacheln[^1].Zustand, "Global hört");
        Pruefe.Falsch(f.Kacheln[4].KannSenden || f.Kacheln[6].KannSenden, "Ruhe und gesperrt senden nicht");
        Pruefe.Falsch(f.Kacheln[4].KannAusschalten, "Ruhe hört ohnehin nicht");
        Pruefe.Gleich("Senden: Brücke (Schiff 3)", f.Kopfzeile, "Kopfzeile mit Ch.");
        Pruefe.Gleich("Brücke (Schiff 3): SENDET, spricht: Kobold", f.Kacheln[0].Beschreibung, "für Bildschirmleser");
        await f.Kacheln[6].SendenCommand.ExecuteAsync(null);
        Pruefe.Wahr(f.Hinweis?.Contains("gesperrt", StringComparison.OrdinalIgnoreCase) == true, "gesperrt wählen sagt es: " + f.Hinweis);
        Pruefe.Falsch(Aufrufe(a, "Senden").Any(), "und schickt nichts");
    }

    [Test]
    public static async Task Mehrfachsenden_Schalter_NurMitRecht_KlickNimmtDazu_AusLaesstDenLetzten()
    {
        var l = FunkDienstAttrappe.KanalBeispiel();
        using var a = new Aufbau(l);
        var f = a.Funkfeld();
        Pruefe.Wahr(f.MehrfachSichtbar, "Max ist Führung: Schalter da");
        Pruefe.Falsch(f.MehrfachAn, "beim Start aus");
        f.MehrfachAn = true;
        await f.Kacheln[1].SendenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Senden [0,1]", Aufrufe(a, "Senden")[^1], "Klick nimmt dazu");
        await f.Kacheln[2].SendenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Senden [0,1,2]", Aufrufe(a, "Senden")[^1], "noch einer");
        await f.Kacheln[1].SendenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Senden [0,2]", Aufrufe(a, "Senden")[^1], "Klick nimmt heraus");
        f.MehrfachAn = false;
        Pruefe.Gleich("Senden [2]", Aufrufe(a, "Senden")[^1], "beim Ausschalten bleibt der zuletzt gewählte Kanal");
        await f.Kacheln[1].SendenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Senden [1]", Aufrufe(a, "Senden")[^1], "aus: Klick ersetzt");

        using var b = new Aufbau(l with { Konto = new KontoInfo(9, "Neu", "neu", Stufe.Funker, []) });
        Pruefe.Falsch(b.Funkfeld().MehrfachSichtbar, "ohne MEHRFACH_SENDEN unsichtbar");
        using var c = new Aufbau(l with { Konto = l.Konto! with { AlsFunkerBis = DateTimeOffset.Now.AddHours(2) } });
        Pruefe.Falsch(c.Funkfeld().MehrfachSichtbar, "im Funker-Modus ruht es");
    }

    [Test]
    public static void EinstellungenJeKanalnetz_LautstaerkeVaUndOhrGeltenFuerAlleCh()
    {
        var b = FunkDienstAttrappe.KanalBeispiel();
        var mitGlobal = MitGlobalKanaelen(b);
        var l = mitGlobal with
        {
            Felder = [.. b.Felder.Where(x => x.Feld is not (4 or 7)), new FeldInfo(4, K(mitGlobal, "Global", 2), true, true, false), new FeldInfo(7, K(b, "Brücke", 5), true, true, false)],
        };
        using var a = new Aufbau(l);
        var klang = new KlangProtokoll();
        using var f = new FunkfeldViewModel(a.Dienst, a.Maschine, a.Verlauf, a.Ablage, a.Takt, () => a.Uhr, klang);
        var drei = f.Kacheln[0];
        var fuenf = f.Kacheln[7];
        drei.LautstaerkeDb = -6;
        Pruefe.Gleich(-6.0, fuenf.LautstaerkeDb, "Schiff 5 hat dieselbe Lautstärke wie Schiff 3");
        Pruefe.Gleich(-6f, a.Ablage.Aktuell.Netze[Netz(l, "Brücke").ToString(System.Globalization.CultureInfo.InvariantCulture)].LautstaerkeDb, "gemerkt unter dem Kanalnetz");
        Pruefe.Wahr(klang.Zuletzt[K(l, "Brücke", 3)].Db == -6f && klang.Zuletzt[K(l, "Brücke", 5)].Db == -6f, "der Klang bekommt es für beide Kanäle");
        Pruefe.Wahr(drei.Sprachaktivierung && fuenf.Sprachaktivierung, "ab Werk VA wie das Kanalnetz");
        drei.Sprachaktivierung = false;
        Pruefe.Falsch(fuenf.Sprachaktivierung, "Taste gilt für alle Ch.");
        var netze = new NetzeViewModel(a.Dienst, a.Maschine, a.Takt, a.Ablage, klang, f);
        netze.PanoramaSetzen(K(l, "Brücke", 3), -1f);
        Pruefe.Gleich(-1f, klang.Zuletzt[K(l, "Brücke", 5)].Panorama, "Hören auf links gilt auch für Schiff 5");
        Pruefe.Gleich(-1f, a.Ablage.Aktuell.NetzOderNull(K(l, "Brücke", 7))?.Panorama, "auch für einen Ch., der in keinem Feld liegt");

        // Global 1 bis 20 erlauben Sprachaktivierung; nur Ch. 1 (Gesamt, das feste Feld) bleibt Taste.
        Pruefe.Gleich("Global 1", f.Kacheln[4].ChText, "Global 1 in Feld 5");
        Pruefe.Wahr(f.Kacheln[4].KannSprachaktivierung && !f.Kacheln[4].NurTaste, "Global 1: VA erlaubt");
        Pruefe.Falsch(f.Kacheln[4].Sprachaktivierung, "ab Werk Taste wie Ch. 1 (ausloeserStandard von Global)");
        Pruefe.Falsch(f.Kacheln[^1].KannSprachaktivierung, "Ch. 1 von Global nur Taste");
    }

    [Test]
    public static void Einstellungen_Nachsichtig_GrenzenUndRueckweg()
    {
        using var o = new Testordner();
        var datei = o.Datei("einstellungen.json");
        File.WriteAllText(datei, """{ "funkfeld": { "skalierung": "x", "rolle": -5 }, "netze": { "110": { "ch": 99, "lautstaerkeDb": -3 }, "111": { "ch": "4" } } }""", new UTF8Encoding(false));
        var s = new EinstellungsSpeicher(datei);
        var e = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "lesbar");
        Pruefe.Gleich(0, Directory.GetFiles(o.Pfad, "*.defekt*").Length, "nichts beiseitegelegt");
        Pruefe.Gleich(0, e.Funkfeld.Skalierung, "kein Wert: 0");
        Pruefe.Gleich(null, e.Funkfeld.Rolle, "keine Rolle");
        Pruefe.Gleich(null, e.Netze["110"].Ch, "Ch. 99 gibt es nicht");
        Pruefe.Gleich(-3f, e.Netze["110"].LautstaerkeDb, "der Rest bleibt");
        Pruefe.Gleich(4, e.Netze["111"].Ch, "Ch. als Text");
        foreach (var (roh, soll) in new[] { ("45", 30), ("-50", -30), ("12.4", 12) })
        {
            File.WriteAllText(datei, """{ "funkfeld": { "skalierung": """ + roh + " } }", new UTF8Encoding(false));
            Pruefe.Gleich(soll, s.Laden().Funkfeld.Skalierung, "Skalierung " + roh);
        }
        var n = new ClientEinstellungen();
        n.Funkfeld.Skalierung = 20;
        n.Funkfeld.Rolle = 112;
        n.NetzFuer(Kanalrechnung.Id(110, 3)).Ch = 3;
        Pruefe.Wahr(ReferenceEquals(n.NetzFuer(Kanalrechnung.Id(110, 3)), n.NetzFuer(110)), "eine Einstellung je Kanalnetz");
        s.Speichern(n);
        var text = File.ReadAllText(datei);
        Pruefe.Wahr(text.Contains("\"skalierung\": 20", StringComparison.Ordinal) && text.Contains("\"rolle\": 112", StringComparison.Ordinal)
            && text.Contains("\"ch\": 3", StringComparison.Ordinal) && !text.Contains("33", StringComparison.Ordinal), "geschrieben unter dem Kanalnetz: " + text.Length);
        var z = s.Laden();
        Pruefe.Wahr(z.Funkfeld.Skalierung == 20 && z.Funkfeld.Rolle == 112 && z.Netze["110"].Ch == 3, "zurückgelesen");
    }

    [Test]
    public static void Rolle_NachDerWiederverbindungsfrist_StelltDerClientWiederHer()
    {
        var l = FunkDienstAttrappe.KanalBeispiel();
        var co = RolleId(l, "Kapitän");
        using var a = new Aufbau(l);
        a.Ablage.Aktuell.NetzFuer(Netz(l, "Kommando")).Ch = 3;
        a.Ablage.Aktuell.NetzFuer(Netz(l, "Brücke")).Ch = 3;
        using var f = a.Funkfeld();
        Pruefe.Gleich((int?)co, a.Ablage.Aktuell.Funkfeld.Rolle, "die gewählte Rolle aus der Lage ist gemerkt");

        void Wiederverbinden(MitgliedschaftsQuelle quelle, ushort? rolle, long kontoVersion)
        {
            var vorher = Aufrufe(a, "RolleWaehlen").Count;
            a.Dienst.LageSetzen(x => x with { Status = Verbindungsstatus.Verbinden, Abgeglichen = false });
            a.Dienst.LageSetzen(x => x with { Status = Verbindungsstatus.Aktiv, Abgeglichen = false, Rolle = rolle, Quelle = quelle, Felder = [], SendeFelder = [] });
            Pruefe.Gleich(vorher, Aufrufe(a, "RolleWaehlen").Count, "vor dem Abgleich nichts");
            a.Dienst.LageSetzen(x => x with { Abgeglichen = true, KontoVersion = kontoVersion });
        }

        // Nach 10 min ohne Verbindung setzt der Server auf STANDARD; der Client stellt die Rolle wieder her, seit 0.8.1 ohne die
        // gemerkten Ch. (Q46: jeder Ch. beginnt auf Ruhe).
        Wiederverbinden(MitgliedschaftsQuelle.Standard, null, 13);
        Pruefe.Gleich("RolleWaehlen " + co + " []", Aufrufe(a, "RolleWaehlen").SingleOrDefault(), "einmal, ohne Wünsche");
        a.Dienst.LageSetzen(x => x with { KontoVersion = 14 });
        Pruefe.Gleich(1, Aufrufe(a, "RolleWaehlen").Count, "nicht noch einmal");

        // Innerhalb der Frist stellt der Server selbst wieder her (WIEDERHERGESTELLT mit rolle): nichts zu tun.
        Wiederverbinden(MitgliedschaftsQuelle.Wiederhergestellt, co, 15);
        Pruefe.Gleich(1, Aufrufe(a, "RolleWaehlen").Count, "innerhalb der Frist nichts");

        // Teilt die Einsatzleitung um, bleibt die gemerkte Rolle seit 0.11.0 stehen (bis 0.10.0 wurde sie vergessen): Die Einteilung
        // gilt für den Abend. Kommt der Funker eingeteilt zurück (VERSCHIEBEN aus der Reservierung), schickt der Client nichts; ohne
        // Einteilung (STANDARD) stellt er die gemerkte Rolle wieder her.
        a.Dienst.LageSetzen(x => x with { Rolle = null, Quelle = MitgliedschaftsQuelle.Verschieben, QuelleDurch = 4, KontoVersion = 16 });
        Pruefe.Gleich((int?)co, a.Ablage.Aktuell.Funkfeld.Rolle, "die gemerkte Rolle bleibt");
        Wiederverbinden(MitgliedschaftsQuelle.Verschieben, null, 17);
        Pruefe.Gleich(1, Aufrufe(a, "RolleWaehlen").Count, "eingeteilt zurück: kein RolleWaehlen");
        Pruefe.Gleich((int?)co, a.Ablage.Aktuell.Funkfeld.Rolle, "auch dann bleibt sie gemerkt");
        Wiederverbinden(MitgliedschaftsQuelle.Standard, null, 18);
        Pruefe.Gleich(2, Aufrufe(a, "RolleWaehlen").Count, "ohne Einteilung kommt die gemerkte Rolle zurück");
        Pruefe.Gleich("RolleWaehlen " + co + " []", Aufrufe(a, "RolleWaehlen")[^1], "dieselbe Rolle, ohne Wünsche");
    }

    [Test]
    public static void Meldekanal_SprichtInDenKanalAusDenFeldern()
    {
        var l = FunkDienstAttrappe.KanalBeispiel();
        var e = Meldenetzwahl.Entscheid(null, l);
        Pruefe.Gleich((ushort?)K(l, "Kommando", 1), e.NetzId, "ab Werk Kommando, der Ch. aus dem Feld");
        Pruefe.Gleich("Kommando (Flottenführung)", e.Name, "Name mit Ch.");
        Pruefe.Gleich(null, e.Hindernis, "geht");
        var bridge = Meldenetzwahl.Entscheid(Netz(l, "Brücke"), l);
        Pruefe.Gleich((ushort?)K(l, "Brücke", 3), bridge.NetzId, "gewählt Brücke: Schiff 3 aus dem Feld");

        var ruhe = l with { Felder = [.. l.Felder.Select(x => x.Feld == 3 ? x with { NetzId = K(l, "Kommando", 0) } : x)] };
        Pruefe.Gleich("Melden geht nicht: Kommando steht auf Ruhe", Meldenetzwahl.Entscheid(null, ruhe).Hindernis, "auf Ruhe");
        var weg = l with { Felder = [.. l.Felder.Where(x => x.Feld != 3)] };
        Pruefe.Gleich("Melden geht nicht: kein Senderecht in Kommando", Meldenetzwahl.Entscheid(null, weg).Hindernis, "nicht in den Feldern");
        var gesperrt = l with { Gesperrt = [new KanalsperreInfo(Netz(l, "Kommando"), l.Gesperrt[0].Bis, 4, false)] };
        Pruefe.Gleich("Melden geht nicht: Gesperrt von Nordlicht bis 04:00", Meldenetzwahl.Entscheid(null, gesperrt).Hindernis, "gesperrt");
    }

    [Test]
    public static void Skalierung_Grenzen_GemerktUndUeberDieEinstellungen()
    {
        using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel());
        var f = a.Funkfeld();
        var platzNeu = 0;
        f.PlatzNeu += () => platzNeu++;
        a.Ablage.Aktuell.Funkfeld.Fensterorte["Raster-10"] = new FensterOrt(100, 100, 900, 260);
        f.Skalierung = 45;
        Pruefe.Gleich(30, f.Skalierung, "höchstens +30 %");
        Pruefe.Gleich(1.3, f.Skala, "Faktor");
        Pruefe.Gleich(30, a.Ablage.Aktuell.Funkfeld.Skalierung, "gemerkt");
        Pruefe.Wahr(platzNeu > 0 && !a.Ablage.Aktuell.Funkfeld.Fensterorte["Raster-10"].MitGroesse, "die gezogene Größe gilt danach nicht mehr, die Lage bleibt");
        f.Skalierung = -45;
        Pruefe.Gleich(-30, f.Skalierung, "mindestens -30 %");
        Pruefe.Gleich(0.7, f.Skala, "Faktor");
        var e = new EinstellungenViewModel(a.Ablage, f);
        e.Skalierung = 10;
        Pruefe.Gleich(10, f.Skalierung, "der Regler unter Einstellungen");
        Pruefe.Gleich("+10 %", e.SkalierungText, "Anzeige");
        e.Skalierung = double.NaN;
        Pruefe.Gleich(10, f.Skalierung, "NaN ändert nichts");
    }
}
