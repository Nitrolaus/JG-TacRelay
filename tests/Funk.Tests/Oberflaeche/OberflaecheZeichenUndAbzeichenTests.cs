// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;
using Funk.Verwaltung.Ansichten;
using Funk.Verwaltung.Modelle;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using R = Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Abzeichen im Client (0.11.7, Sarevoc 14./15.10.2026): die 50 Bilder aus TacRelay.dll, das Abzeichen "vorn" in Wer ist da und auf der
/// Funkfeld-Zuteilung, der Hilfetext mit allen sichtbaren, "Meine Abzeichen" unter Einstellungen (vorn, zeigen, ohne Echo), der Eintrag ganz unten
/// im Hauptmenü nur in der Admin-Sitzung und die Seite Abzeichen (befördern, zurückstufen, entfernen, aufnehmen, höchstens 6 Tätigkeiten). Mit
/// <c>FUNK_BILDER</c> entstehen vorschau-0117-abzeichen-seite, vorschau-0117-meine-abzeichen und vorschau-0117-weristda. Bis 0.11.8 hieß die Klasse
/// OberflaecheAbzeichenTests; mit ihren Bildläufen lief sie damit vor OberflaecheFensterTests, und dieser scheiterte, sobald er nicht neben einem Spiel
/// übersprungen wurde (Befund beim vollen Lauf für 0.11.9). Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheZeichenUndAbzeichenTests
{
    private static Bereich? Abzeichenbereich(HauptViewModel h) => h.Bereiche.SingleOrDefault(b => b.Kennung == HauptViewModel.AbzeichenKennung);

    [Test]
    public static void Zeichensatz_LaedtAlle50Bilder()
    {
        var kennungen = Abzeichenkatalog.Laufbahnen.SelectMany(l => l.Raenge.Select(r => r.Kennung)).Concat(Abzeichenkatalog.Taetigkeiten.Select(t => t.Kennung)).ToList();
        Pruefe.Gleich(50, kennungen.Count, "Katalog: 28 Ränge und 22 Tätigkeiten");
        var ressourcen = typeof(Zeichensatz).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("Abzeichen.", StringComparison.Ordinal)).ToHashSet();
        Pruefe.Gleich(50, ressourcen.Count, "50 PNG eingebettet: " + string.Join(",", ressourcen.Take(3)));
        foreach (var k in kennungen)
        {
            Pruefe.Wahr(ressourcen.Contains(Zeichensatz.Ressource(k)), "eingebettet: " + k);
            var bild = Zeichensatz.Bild(k);
            Pruefe.Wahr(bild is BitmapSource { IsFrozen: true, PixelWidth: Zeichensatz.AbzeichenPixel } b && b.PixelHeight == Zeichensatz.AbzeichenPixel,
                k + ": eingefroren, auf " + Zeichensatz.AbzeichenPixel + " Bildpunkte geladen");
            Pruefe.Wahr(ReferenceEquals(bild, Zeichensatz.Bild(k)), k + ": einmal geladen");
            Pruefe.Gleich(Abzeichenkatalog.Titel(k), Zeichensatz.Titel(k), k + ": Titel aus dem Katalog");
        }
        Pruefe.Gleich("Marine: Sergeant", Zeichensatz.Titel("MARINE_04"), "Rang mit Laufbahn");
        Pruefe.Gleich("Gunner M", Zeichensatz.Titel("T_GUNNER_M"), "Tätigkeit");
        Pruefe.Gleich(null, Zeichensatz.Bild("FUEHRUNG"), "FUEHRUNG wird nicht mehr gezeichnet");
        Pruefe.Gleich(null, Zeichensatz.Bild("T_UNBEKANNT"), "Unbekanntes ohne Bild");
        Pruefe.Wahr(Zeichensatz.Bild("ADMIN") is DrawingImage && Zeichensatz.Bild("EINSATZLEITUNG") is DrawingImage, "Kranz und Stern bleiben gezeichnet");
        // Die Bilder sind nicht leer: In der Mitte des Rangs MARINE_04 steht etwas Deckendes.
        var marine = (BitmapSource)Zeichensatz.Bild("MARINE_04")!;
        var punkte = new byte[marine.PixelWidth * marine.PixelHeight * 4];
        new FormatConvertedBitmap(marine, PixelFormats.Bgra32, null, 0).CopyPixels(punkte, marine.PixelWidth * 4, 0);
        Pruefe.Wahr(Enumerable.Range(0, punkte.Length / 4).Count(i => punkte[(i * 4) + 3] > 200) > punkte.Length / 40, "MARINE_04 hat deckende Bildpunkte");
    }

    [Test]
    public static void Vorn_GewaehltSonstErstesSichtbares()
    {
        var sichtbar = Zeichensatz.Sichtbare(["EINSATZLEITUNG"], ["T_FUNKER", "MARINE_04"]);
        Pruefe.Gleich("EINSATZLEITUNG|MARINE_04|T_FUNKER", string.Join("|", sichtbar.Select(z => z.Kennung)), "Automatik, Ränge, Tätigkeiten");
        Pruefe.Gleich("T_FUNKER", Zeichensatz.Vorn(sichtbar, "T_FUNKER")?.Kennung, "gewählt und sichtbar");
        Pruefe.Gleich("EINSATZLEITUNG", Zeichensatz.Vorn(sichtbar, null)?.Kennung, "ohne Wahl das erste");
        Pruefe.Gleich("EINSATZLEITUNG", Zeichensatz.Vorn(sichtbar, "NAVY_03")?.Kennung, "gewählt, aber nicht sichtbar: das erste");
        Pruefe.Gleich("MARINE_04", Zeichensatz.Vorn(Zeichensatz.Sichtbare(["FUEHRUNG"], ["MARINE_04"]), null)?.Kennung, "FUEHRUNG steht nie vorn");
        Pruefe.Gleich(null, Zeichensatz.Vorn([], "ADMIN"), "ohne sichtbare keins");

        // Vom Draht in die Lage: Praesenz und eigenes Konto.
        var welcome = new Welcome
        {
            Minor = 0, Status = WelcomeStatus.Freigegeben,
            Konto = new KontoAngabe { Id = 7, Name = "Test", Benutzername = "test", Stufe = Stufe.Funker, Rechte = [] },
        };
        var m = new Mitgliedschaften
        {
            ZustandsVersion = 1, KontoVersion = 1, Felder = [], Global = new GlobalAngabe { Senden = false }, Sendeauswahl = new Sendeauswahl { Felder = [], Global = false },
            Stumm = [], Quelle = MitgliedschaftsQuelle.Rolle,
            Konto = new KontoStand
            {
                Rechte = [], ScNamen = ["Test"], Rollen = [], Lieblingsrollen = [], Zeichen = [], ZeichenZeigen = true,
                Abzeichen = ["MARINE_04", "T_FUNKER"], AbzeichenVorn = "T_FUNKER", AbzeichenAus = ["ADMIN"],
            },
        };
        var praesenz = new Dictionary<uint, PraesenzEintrag>
        {
            [9] = new()
            {
                KontoId = 9, Name = "Falke", Benutzername = "falke", Online = true, Zeichen = ["ADMIN"], Abzeichen = ["NAVY_02", "T_JAEGER"], AbzeichenVorn = "T_JAEGER",
            },
        };
        var lage = LageAbbildung.Abbilden(new LageEingabe(ClientZustand.AktivUdp, welcome, null, m, praesenz, [], false, []));
        Pruefe.Wahr(lage.Funker.Single() is { Abzeichen: ["NAVY_02", "T_JAEGER"], AbzeichenVorn: "T_JAEGER", Zeichen: ["ADMIN"] }, "Praesenz: Abzeichen und vorn");
        Pruefe.Wahr(lage.Konto is { Abzeichen: ["MARINE_04", "T_FUNKER"], AbzeichenVorn: "T_FUNKER", AbzeichenAus: ["ADMIN"] }, "Konto: alle eigenen, Wahl, ausgeblendet");

        // Wer ist da und Funkfeld-Zuteilung über dem Beispielabend.
        using var a = new Aufbau(dienst: R.MitRaidabend());
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        h.Brett.KartenAbwartenAsync().GetAwaiter().GetResult();
        var w = h.WerIstDa;
        WerIstDaZeile Zeile(uint konto) => w.Zeilen.Single(z => z.KontoId == konto);
        Pruefe.Wahr(ReferenceEquals(Zeile(R.Nordwind).Abzeichen, Zeichensatz.Bild("NAVY_09")), "Nordwind hat Navy: Fregattenkapitän vorn gewählt, nicht den Kranz");
        Pruefe.Wahr(ReferenceEquals(Zeile(R.Mustermann).Abzeichen, Zeichensatz.Bild("EINSATZLEITUNG")), "Max ohne Wahl: der Stern zuerst");
        Pruefe.Wahr(ReferenceEquals(Zeile(R.Falke).Abzeichen, Zeichensatz.Bild("T_JAEGER")), "Falke: Jägerpilot vorn");
        Pruefe.Wahr(ReferenceEquals(Zeile(R.Ferox).Abzeichen, Zeichensatz.Bild("NAVY_06")), "Ferox: FUEHRUNG ohne Bild, der Rang vorn");
        Pruefe.Wahr(Zeile(R.Ferox).Rang == 1, "Ferox bleibt Führung in der Reihenfolge (Logik an FUEHRUNG unverändert)");
        Pruefe.Wahr(Zeile(R.Hagel) is { HatAbzeichen: false, Anfang: "H" } || !w.Zeilen.Any(z => z.KontoId == R.Hagel), "Hagel ohne Abzeichen");
        var b = h.Brett;
        Pruefe.Wahr(ReferenceEquals(b.Karte(R.Nordwind)!.Abzeichen, Zeichensatz.Bild("NAVY_09")) && b.Karte(R.Nordwind)!.IstAdmin, "Karte: vorn das gewählte, Admin bleibt Admin");
        Pruefe.Wahr(ReferenceEquals(b.Karte(R.Distel)!.Abzeichen, Zeichensatz.Bild("T_SANI")), "Distel: Sanitäter vorn");
        Pruefe.Gleich("Marine: Leutnant, Sanitäter, Ausbilder", b.Karte(R.Distel)!.AbzeichenTitel, "Tooltip am Abzeichen der Karte");
        Pruefe.Wahr(b.Karte(R.Distel)!.Beschreibung.Contains("Abzeichen Marine: Leutnant, Sanitäter, Ausbilder", StringComparison.Ordinal), "Hilfetext der Karte: " + b.Karte(R.Distel)!.Beschreibung);
        Pruefe.Gleich(2, BrettViewModel.Rang(b.Karte(R.Mustermann)!), "Einsatzleitung bleibt Rang 2 auf dem Brett");
        Pruefe.Gleich(1, BrettViewModel.Rang(b.Karte(R.Ferox)!), "Führung bleibt Rang 1 auf dem Brett");

        // Zeichen neben Namen aus (Betrachter): nichts vorn.
        h.MeinProfil.ZeichenNebenNamen = false;
        Pruefe.Wahr(Zeile(R.Nordwind) is { HatAbzeichen: false, Zeichen.Count: 0 }, "Zeichen neben Namen aus: kein Abzeichen");
        h.MeinProfil.ZeichenNebenNamen = true;
        funkfeld.Dispose();
    }

    [Test]
    public static void WerIstDa_HilfetextNenntAlleSichtbaren()
    {
        using var a = new Aufbau(dienst: R.MitRaidabend());
        var w = new WerIstDaViewModel(a.Dienst, a.Ablage, a.Takt);
        WerIstDaZeile Zeile(uint konto) => w.Zeilen.Single(z => z.KontoId == konto);
        Pruefe.Gleich("Distel: Marine: Leutnant, Sanitäter, Ausbilder. Online.", Zeile(R.Distel).AbzeichenHilfe, "alle sichtbaren, in Katalogfolge");
        Pruefe.Gleich("Nordwind: Admin, Navy: Fregattenkapitän, Capital-Pilot. Online.", Zeile(R.Nordwind).AbzeichenHilfe, "Kranz und Abzeichen");
        Pruefe.Gleich("Mustermann/Max: Einsatzleitung (Auftrag für heute), Marine: Sergeant, Orga: Veteran, Funker. Online.", Zeile(R.Mustermann).AbzeichenHilfe, "Stern, Ränge, Tätigkeit");
        Pruefe.Gleich("Zinnober trägt kein Abzeichen. Online.", Zeile(R.Zinnober).AbzeichenHilfe, "ohne Abzeichen");
        Pruefe.Wahr(Zeile(R.Distel).AbzeichenAnsage.Contains("Marine: Leutnant, Sanitäter, Ausbilder", StringComparison.Ordinal), "Vorleser nennt sie auch");
        // Ausgeblendetes schickt der Server nicht; was fehlt, nennt auch der Hilfetext nicht.
        a.Dienst.LageSetzen(l => l with { Funker = [.. l.Funker.Select(f => f.KontoId != R.Distel ? f : f with { Abzeichen = ["T_SANI"], AbzeichenVorn = "T_SANI" })] });
        Pruefe.Gleich("Distel: Sanitäter. Online.", Zeile(R.Distel).AbzeichenHilfe, "nur das sichtbare");
        foreach (var z in w.Zeilen) Console.WriteLine("  Hilfetext " + z.Name + ": " + z.AbzeichenHilfe);
    }

    [Test]
    public static async Task MeineAbzeichen_VornUndZeigenGehenSofortAnDenServer()
    {
        using var a = new Aufbau(dienst: R.MitRaidabend());
        var m = new MeinProfilViewModel(a.Dienst, a.Ablage, a.Takt);
        Pruefe.Gleich("EINSATZLEITUNG|MARINE_04|ORGA_03|T_FUNKER", string.Join("|", m.MeineAbzeichen.Select(x => x.Kennung)), "Stern, Ränge, Tätigkeit");
        Pruefe.Wahr(m is { HatAbzeichen: true, OhneAbzeichen: false } && m.MeineAbzeichen.All(x => !x.Vorn && x.Zeigen && x.Bild is not null), "keine Wahl, alles gezeigt, mit Bild");
        Pruefe.Gleich("Marine: Sergeant", m.MeineAbzeichen[1].Titel, "Titel aus dem Katalog");
        int Profilaufrufe() => a.Dienst.Aufrufe.Count(x => x.StartsWith("Profil", StringComparison.Ordinal));
        string Letzter() => a.Dienst.Aufrufe.Last(x => x.StartsWith("Profil", StringComparison.Ordinal));
        MeinAbzeichen A(string k) => m.MeineAbzeichen.Single(x => x.Kennung == k);

        A("MARINE_04").Vorn = true;
        await Task.Yield();
        Pruefe.Gleich("Profil vorn=MARINE_04", Letzter(), "vorn geht sofort hinaus");
        Pruefe.Gleich("MARINE_04", a.Dienst.Lage.Konto!.AbzeichenVorn, "steht am Konto");
        var n = Profilaufrufe();
        A("ORGA_03").Vorn = true;
        Pruefe.Wahr(!A("MARINE_04").Vorn && A("ORGA_03").Vorn, "eine Wahl: die alte geht aus");
        Pruefe.Gleich(n + 1, Profilaufrufe(), "ein Aufruf, kein Echo für die alte Wahl");
        Pruefe.Gleich("Profil vorn=ORGA_03", Letzter(), "neue Wahl");
        A("ORGA_03").Vorn = false;
        Pruefe.Gleich("Profil vorn=", Letzter(), "keine Wahl heißt \"\"");
        Pruefe.Gleich(null, a.Dienst.Lage.Konto!.AbzeichenVorn, "keine Wahl am Konto");

        // Ausgeblendetes, das gerade nicht in der Liste steht (ADMIN), bleibt ausgeblendet.
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { AbzeichenAus = ["ADMIN"] } });
        n = Profilaufrufe();
        A("EINSATZLEITUNG").Zeigen = false;
        Pruefe.Gleich("Profil aus=[ADMIN,EINSATZLEITUNG]", Letzter(), "Stern aus, ADMIN bleibt");
        A("T_FUNKER").Zeigen = false;
        Pruefe.Gleich("Profil aus=[ADMIN,EINSATZLEITUNG,T_FUNKER]", Letzter(), "Funker auch aus");
        A("EINSATZLEITUNG").Zeigen = true;
        Pruefe.Gleich("Profil aus=[ADMIN,T_FUNKER]", Letzter(), "Stern wieder an");
        Pruefe.Gleich(n + 3, Profilaufrufe(), "je Klick ein Aufruf, die Rückmeldung des Servers sendet nichts");
        Pruefe.Wahr(!A("T_FUNKER").Zeigen && A("EINSATZLEITUNG").Zeigen, "Haken nach dem Stand des Kontos");

        // Ein Fehler des Servers steht da, der Haken geht auf den Stand des Kontos zurück.
        a.Dienst.NaechsterFehler = "Ungültige Angabe.";
        A("MARINE_04").Zeigen = false;
        Pruefe.Wahr(m.SchalterFehler == "Ungültige Angabe." && A("MARINE_04").Zeigen, "Fehler sichtbar, Haken zurück");

        // "Mein Zeichen anderen zeigen" bleibt und heißt: alle aus.
        m.ZeichenZeigen = false;
        Pruefe.Gleich("Profil zeichen=False", Letzter(), "der Schalter für alle bleibt");

        // FUEHRUNG wird nicht angeboten; ohne Abzeichen steht der Satz.
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Zeichen = ["FUEHRUNG"], Abzeichen = [] } });
        Pruefe.Wahr(m is { HatAbzeichen: false, OhneAbzeichen: true } && m.MeineAbzeichen.Count == 0, "FUEHRUNG nicht angeboten, keine Abzeichen");
        Pruefe.Wahr(MeinProfilViewModel.OhneAbzeichenText.Contains("Admins", StringComparison.Ordinal), "der Satz sagt, wer vergibt");
    }

    [Test]
    public static void Hauptmenue_AbzeichenGanzUntenNurInDerAdminSitzung()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            var d = new VerwaltungsdienstAttrappe();
            var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
            using var teil = new Verwaltungsteil(d, vm);
            using var a = new Aufbau(R.Beispiel()) { Verwaltung = teil };
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            Pruefe.Gleich(null, Abzeichenbereich(h), "ohne Admin-Zustand am Konto kein Abzeichen");
            a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = AdminStatus.Aktiv } });
            var bereich = Abzeichenbereich(h);
            Pruefe.Wahr(bereich is { Unten: true, Titel: "Abzeichen" } && ReferenceEquals(h.Bereiche[^1], bereich) && ReferenceEquals(h.Navigation[^1], bereich),
                "Admin-Sitzung offen: Abzeichen als letzter Bereich, unten");
            Pruefe.Wahr(!h.Tastenhinweis.Contains("Abzeichen", StringComparison.Ordinal), "ohne Tastenkürzel");

            // Geht die Sitzung zu, verschwindet der Eintrag; war die Seite vorn, geht es auf Start.
            h.AktiverBereich = bereich!;
            Pruefe.Wahr(h is { IstAbzeichenseite: true, MitBildlauf: false } && h.AbzeichenInhalt is AbzeichenBereich, "Seite vorn, eigener Platz");
            vm.AbmeldenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Pruefe.Gleich(null, Abzeichenbereich(h), "Sitzung zu: kein Abzeichen");
            Pruefe.Gleich(HauptViewModel.StartKennung, h.AktiverBereich.Kennung, "zurück auf Start");
            Pruefe.Wahr(h.Bereiche.Any(b => b.Kennung == HauptViewModel.VerwaltungKennung), "die Verwaltung bleibt (zum Anmelden)");
            vm.Code = VerwaltungsdienstAttrappe.RichtigerCode;
            vm.AnmeldenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Pruefe.Wahr(Abzeichenbereich(h) is not null && ReferenceEquals(h.Bereiche[^1], Abzeichenbereich(h)), "nach der Anmeldung wieder da, ganz unten");

            // Gezeichnet: der Eintrag steht ganz unten in der Leiste, mit Abstand zu den anderen, bedienbar wie sie.
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1280, 800));
            var navi = (ListBox)haupt.FindName("Navi");
            var eintraege = Bildlauf.Nachfahren<ListBoxItem>(navi).ToList();
            var unten = eintraege.Single(e => e.DataContext is Bereich { Kennung: HauptViewModel.AbzeichenKennung });
            double Oben(FrameworkElement e) => e.TransformToAncestor(navi).Transform(new Point(0, 0)).Y;
            var andere = eintraege.Where(e => !ReferenceEquals(e, unten)).ToList();
            Pruefe.Wahr(Oben(unten) > andere.Max(e => Oben(e) + e.ActualHeight) + 200, $"ganz unten: {Oben(unten):0} gegen {andere.Max(e => Oben(e) + e.ActualHeight):0}");
            Pruefe.Wahr(navi.ActualHeight - (Oben(unten) + unten.ActualHeight) < 20, "am Boden der Leiste");
            Pruefe.Wahr(unten.Focusable && unten.Style == andere[0].Style && Sichtbar(unten, "Abzeichen"), "gleicher Stil, mit Text, fokussierbar");
            Pruefe.Gleich(HauptViewModel.AbzeichenTooltip, ((Bereich)unten.DataContext).Taste, "Tooltip statt Tastenkürzel");
            funkfeld.Dispose();
        });
    }

    [Test]
    public static async Task Seite_BefoerdernZurueckstufenEntfernenAufnehmen()
    {
        var d = new VerwaltungsdienstAttrappe();
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var s = vm.Abzeichen;
        Pruefe.Gleich("5 Mitglieder", s.Zaehlung, "nur Konten, keine Vormerkungen und Anträge");
        Pruefe.Gleich("Falke|Kobold|Kranich|Mustermann|Nordlicht", string.Join("|", s.Sichtbar.Select(x => x.Name)), "nach SC-Namen");
        s.Suche = "kob";
        Pruefe.Gleich("Kobold", string.Join("|", s.Sichtbar.Select(x => x.Name)), "Suche");
        s.Suche = "";
        Pruefe.Wahr(s.OhneKarte, "erst wählen");

        AbzeichenMitglied M(string name) => s.Sichtbar.Single(x => x.Name == name);
        s.WaehlenCommand.Execute(M("Falke"));
        var k = s.Karte!;
        Pruefe.Wahr(M("Falke").Gewaehlt && k.Titel == "Abzeichen von Falke", "Karte rechts, links markiert");
        LaufbahnZeile L(string kennung) => s.Karte!.Laufbahnen.Single(l => l.Kennung == kennung);
        Pruefe.Wahr(L("NAVY").Rang?.Kennung == "NAVY_02" && L("MARINE").Rang is null && L("ORGA").OhneRang, "Navy: Maat, sonst nichts");
        Pruefe.Gleich("Maat (Stufe 2 von 10)", L("NAVY").RangText, "Rang mit Stufe");

        await L("NAVY").BefoerdernCommand.ExecuteAsync(null);
        Pruefe.Gleich("Abzeichen 2 [NAVY_03] [T_JAEGER]", d.Aufrufe[^1], "befördern ersetzt den Rang, Tätigkeiten gehen mit");
        Pruefe.Wahr(L("NAVY").Rang?.Name == "Petty Officer" && s.Karte!.Hinweis == "Falke ist jetzt Navy: Petty Officer." && s.Karte.Fehler is null, "sofort gespeichert: " + s.Karte?.Hinweis);
        await L("NAVY").ZurueckstufenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Abzeichen 2 [NAVY_02] [T_JAEGER]", d.Aufrufe[^1], "zurückstufen");
        await L("MARINE").AufnehmenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Abzeichen 2 [MARINE_01,NAVY_02] [T_JAEGER]", d.Aufrufe[^1], "aufnehmen = Stufe 1, je Laufbahn ein Rang, in Laufbahnfolge");
        Pruefe.Wahr(L("MARINE").Rang?.Kennung == "MARINE_01" && M("Falke").Raenge.SequenceEqual(["MARINE_01", "NAVY_02"]), "Liste links zieht nach, in Laufbahnfolge");
        Pruefe.Wahr(!L("MARINE").ZurueckstufenCommand.CanExecute(null) && L("MARINE").ZurueckstufenGesperrt.Contains("erste Rang", StringComparison.Ordinal),
            "unten an der Leiter: zurückstufen gesperrt mit Grund");
        await L("NAVY").EntfernenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Abzeichen 2 [MARINE_01] [T_JAEGER]", d.Aufrufe[^1], "entfernen");
        Pruefe.Wahr(L("NAVY").OhneRang && L("NAVY").AufnehmenCommand.CanExecute(null) && !L("NAVY").BefoerdernCommand.CanExecute(null), "ohne Rang: nur aufnehmen");

        // Kobold steht ganz oben in der Marine und hat sechs Tätigkeiten.
        s.WaehlenCommand.Execute(M("Kobold"));
        Pruefe.Wahr(!L("MARINE").BefoerdernCommand.CanExecute(null) && L("MARINE").BefoerdernGesperrt == "Oberst ist der höchste Rang der Marine; höher geht es nicht.",
            "oben an der Leiter: befördern gesperrt mit Grund");
        Pruefe.Gleich("Tätigkeiten (6 von höchstens 6)", s.Karte!.TaetigkeitenTitel, "Zähler");
        TaetigkeitFeld T(string kennung) => s.Karte!.Gruppen.SelectMany(g => g.Eintraege).Single(t => t.Kennung == kennung);
        Pruefe.Gleich("Piloten|Waffen|Technik und Versorgung|Erkundung und Gewerbe", string.Join("|", s.Karte.Gruppen.Select(g => g.Name)), "nach Gruppe");
        Pruefe.Gleich(22, s.Karte.Gruppen.Sum(g => g.Eintraege.Count), "alle 22 Tätigkeiten");
        var vorher = d.Aufrufe.Count;
        T("T_SANI").Vergeben = true;
        Pruefe.Wahr(!T("T_SANI").Vergeben && s.Karte.Fehler == AbzeichenKarte.GrenzeText && d.Aufrufe.Count == vorher, "bei 6 ist Schluss, mit klarer Meldung: " + s.Karte.Fehler);
        T("T_EVA").Vergeben = false;
        await Task.Yield();
        Pruefe.Gleich("Abzeichen 3 [MARINE_10] [T_JAEGER,T_BOMBER,T_GUNNER_S,T_ING,T_SCOUT]", d.Aufrufe[^1], "wegnehmen");
        Pruefe.Wahr(s.Karte.Fehler is null && s.Karte.TaetigkeitenTitel.StartsWith("Tätigkeiten (5", StringComparison.Ordinal), "Fehler weg, fünf");
        T("T_SANI").Vergeben = true;
        await Task.Yield();
        Pruefe.Gleich("Abzeichen 3 [MARINE_10] [T_JAEGER,T_BOMBER,T_GUNNER_S,T_ING,T_SANI,T_SCOUT]", d.Aufrufe[^1], "jetzt geht eine neue");

        // Ein Fehler des Servers steht an der Karte; sie zeigt wieder den Stand davor.
        s.WaehlenCommand.Execute(M("Kranich"));
        d.Mitglieder.RemoveAll(x => x.KontoId == 5);
        await L("ORGA").AufnehmenCommand.ExecuteAsync(null);
        Pruefe.Wahr(s.Karte!.Fehler == "Konto unbekannt" && L("ORGA").OhneRang && s.Karte.Hinweis is null, "Fehler sichtbar, nichts übernommen: " + s.Karte.Fehler);

        // Nach "Mitglied geändert" lädt die Verwaltung neu; die Karte bleibt beim selben Mitglied.
        s.WaehlenCommand.Execute(M("Nordlicht"));
        await vm.NeuLadenAsync();
        Pruefe.Wahr(s.Karte?.Titel == "Abzeichen von Nordlicht" && s.Sichtbar.Single(x => x.Name == "Nordlicht").Gewaehlt, "Karte und Markierung bleiben");
    }

    /// <summary>
    /// Vorschau 0.11.7 für Sarevoc: die Seite Abzeichen (Nordlicht gewählt, Kobold an beiden Grenzen in der Liste), "Meine Abzeichen" unter Einstellungen
    /// und Wer ist da mit mehreren Mitgliedern mit verschiedenen Abzeichen, im echten Hauptfenster bei 1920 x 1080 und 100 %, erfundene Namen. Dazu die
    /// Kontrastregeln auf der neuen Seite. Den Hilfetext eines Abzeichens zeigt ein Popup, das sich ohne Fenster nicht zeichnen lässt; er steht als Text
    /// im Testbericht (WerIstDa_HilfetextNenntAlleSichtbaren).
    /// </summary>
    [Test]
    public static void Vorschau0117_SeiteMeineAbzeichenUndWerIstDa()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var dienst = new VerwaltungsdienstAttrappe();
                var vm = new VerwaltungViewModel(dienst, jetzt: () => VerwaltungsdienstAttrappe.Abend);
                using var teil = new Verwaltungsteil(dienst, vm);
                using var a = new Aufbau(dienst: OberflaecheWerIstDaTests.Abend()) { Verwaltung = teil };
                a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
                a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
                var h = a.Haupt(out var funkfeld, out _, out _, out _);
                a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Stufe.Admin, Admin = AdminStatus.Aktiv, AbzeichenVorn = "ORGA_03", AbzeichenAus = ["T_FUNKER"] } });
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var groesse = new Size(1920, 1080);
                var was = satz.Name + ", Abzeichen";

                // 1. Die Seite Abzeichen.
                h.AktiverBereich = Abzeichenbereich(h)!;
                vm.Abzeichen.WaehlenCommand.Execute(vm.Abzeichen.Sichtbar.Single(x => x.Name == "Nordlicht"));
                _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                var (seite, _) = Bildlauf.Zeichnen(haupt, 1, groesse);
                var ansicht = Bildlauf.Nachfahren<AbzeichenAnsicht>(Bildlauf.Wurzel(haupt)).Single();
                Pruefe.Wahr(Sichtbar(ansicht, "Abzeichen von Nordlicht") && Sichtbar(ansicht, "Sergeant (Stufe 4 von 10)") && Sichtbar(ansicht, "Gunner M"), was + ": Karte rechts");
                var bilder = Bildlauf.Nachfahren<Image>(ansicht).Where(ZuSehen).ToList();
                Pruefe.Wahr(bilder.Count >= 22 + 2 + 5 && bilder.All(i => i.Source is not null), was + ": Bilder der Abzeichen gezeichnet (" + bilder.Count + ")");
                KontrastPruefen(ansicht, satz, was, mindestens: 30);
                Pruefe.Gleich(0, GekuerztOhneHilfe(ansicht).Count, was + ": nichts gekürzt");
                if (satz.Stellung == Farbvorschlaege.Voreinstellung) Bildablage.Ablegen(seite, "vorschau-0117-abzeichen-seite");

                // 2. Meine Abzeichen unter Einstellungen, Ich > Erweitert offen.
                h.Einstellungen.AbschnittIch!.Feinheiten.Offen = true;
                h.AktiverBereich = Seiten.Einstellungen(h);
                _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                var teilEinst = (FrameworkElement)Bildlauf.Nachfahren<StackPanel>(Bildlauf.Wurzel(haupt)).Single(p => p.Name == "MeineAbzeichenTeil");
                var lauf = (ScrollViewer)haupt.FindName("SeitenPlatz");
                lauf.ScrollToVerticalOffset(Math.Max(0, teilEinst.TransformToAncestor(lauf).Transform(new Point(0, 0)).Y + lauf.VerticalOffset - 200));
                _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                var (meine, _) = Bildlauf.Zeichnen(haupt, 1, groesse);
                Pruefe.Wahr(Sichtbar(teilEinst, "Meine Abzeichen") && Sichtbar(teilEinst, "Orga: Veteran") && Sichtbar(teilEinst, "Einsatzleitung (Auftrag für heute)"), was + ": Liste");
                Pruefe.Gleich(4, Bildlauf.Nachfahren<Image>(teilEinst).Count(i => ZuSehen(i) && i.Source is not null), was + ": ein Bild je Abzeichen");
                var haken = Bildlauf.Nachfahren<CheckBox>(teilEinst).Where(ZuSehen).ToList();
                Pruefe.Wahr(haken.Count(c => (string)c.Content == "vorn") == 4 && haken.Count(c => (string)c.Content == "vorn" && c.IsChecked == true) == 1
                    && haken.Count(c => (string)c.Content == "zeigen" && c.IsChecked == false) == 1, was + ": eine Wahl vorn, Funker ausgeblendet");
                KontrastPruefen(teilEinst, satz, was + " (Meine Abzeichen)", mindestens: 5);
                if (satz.Stellung == Farbvorschlaege.Voreinstellung) Bildablage.Ablegen(meine, "vorschau-0117-meine-abzeichen");

                // 3. Wer ist da mit verschiedenen Abzeichen, auch offline.
                h.BereichWaehlenCommand.Execute("1");
                var w = h.WerIstDa;
                w.UmschaltenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                w.AuchOffline = true;
                _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                var (werIstDa, _) = Bildlauf.Zeichnen(haupt, 1, groesse);
                var spalte = (FrameworkElement)haupt.FindName("WerIstDaSpalte");
                var abzeichen = Bildlauf.Nachfahren<Image>(spalte).Where(i => i.Name == "Abzeichen" && ZuSehen(i)).ToList();
                Pruefe.Wahr(abzeichen.Select(i => i.Source).Distinct().Count() >= 6, was + ": verschiedene Abzeichen vorn (" + abzeichen.Select(i => i.Source).Distinct().Count() + ")");
                if (satz.Stellung == Farbvorschlaege.Voreinstellung) Bildablage.Ablegen(werIstDa, "vorschau-0117-weristda");
                w.UmschaltenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                funkfeld.Dispose();
            }
        });
    }
}
