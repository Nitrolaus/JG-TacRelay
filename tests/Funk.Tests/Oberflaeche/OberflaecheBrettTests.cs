// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Collections.Specialized;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Das Brett der Einsatzleitung (Stufe 2, RAIDLEITUNG-ENTWURF "Das Brett", M5) über der Attrappe mit dem Beispielabend aus Anhang
/// A: Mustermann leitet im Auftrag bis 04:00. Geprüft wird, was auf dem Draht landet (ein Verschieben je Ablage, mit den festen
/// Zusatznetzen; ein Tausch in einer Anfrage; Standard mit rolleId 0), die Bewertung vor dem Loslassen (grün, gelb, rot mit
/// Grund), dass Funkverkehr nichts neu baut (CODEMAP, Fallen), Nachzügler, "wie zuletzt", Sperren, Zurücknehmen mit
/// VERALTET-Schutz, "wartet" und der Rückweg bei Fehlern. Dazu die Abbildung des Protokollstands der Stufe 2 in die Lage.
/// Ohne Bildlauf; die Bilder stehen in OberflaecheRaidBilderTests.
/// </summary>
internal static class OberflaecheBrettTests
{
    internal static RollenBlock Block(BrettViewModel b, ushort? schiff, ushort rolle) =>
        b.Gruppen.SelectMany(g => g.Spalten).Where(s => s.SchiffId == schiff).SelectMany(s => s.Bloecke).Single(x => x.RolleId == rolle);

    private static EinheitSpalte Spalte(BrettViewModel b, string name) => b.Gruppen.SelectMany(g => g.Spalten).Single(s => s.Name == name);

    private static IEnumerable<BrettKarte> Karten(RollenBlock block) => block.Eintraege.OfType<BrettKarte>();

    private static int Frei(RollenBlock block) => block.Eintraege.OfType<FreierPlatz>().Count();

    private static (Aufbau A, BrettViewModel B) Raid()
    {
        var a = new Aufbau(dienst: MitRaidabend());
        var b = a.Brett();
        return (a, b);
    }

    // ---------------------------------------------------------------- Abbildung

    [Test]
    public static void Abbildung_Stufe2_AuftragEinheitenBelegungUndDetails()
    {
        var bis = new DateTimeOffset(2026, 10, 3, 2, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var netzListe = new NetzListe
        {
            Version = 12,
            Einstellungen = new NetzEinstellungen { DurchsageNurTaste = true, Selbstwahl = false, SelbstwahlDurch = 7, SelbstwahlBis = bis, Nachzuegler = false },
            Netze =
            [
                new Netz { Id = 1, Name = "Global", Typ = NetzTyp.Global, Reihenfolge = 0, NurTaste = true, AusloeserStandard = Ausloeser.Taste, Offen = NetzOffen.Nein, Standard = false },
                new Netz { Id = 2, Name = "Kommando", Typ = NetzTyp.Normal, Reihenfolge = 1, NurTaste = false, AusloeserStandard = Ausloeser.Taste, Offen = NetzOffen.Nein, Standard = false },
                new Netz { Id = 10, Name = "Crew 1", Typ = NetzTyp.Normal, Reihenfolge = 2, NurTaste = false, AusloeserStandard = Ausloeser.Va, Offen = NetzOffen.Nein, Standard = false, SchiffId = 1, Schiffsnetz = "CREW" },
            ],
            Schiffe =
            [
                new Schiff { Id = 1, Name = "Schiff 1", Reihenfolge = 1, Art = Einheitsart.Schiff, Typ = "Hammerhead", Netze = new Dictionary<string, ushort> { ["CREW"] = 10 },
                    Plaetze = [new SchiffsPlatz { RolleId = 5, Kapazitaet = 1, Mindest = 1 }, new SchiffsPlatz { RolleId = 6, Kapazitaet = 3, Mindest = 2 }] },
                new Schiff { Id = 20, Name = "Trupp Alpha", Reihenfolge = 2, Art = Einheitsart.Trupp, Netze = new Dictionary<string, ushort>(), Plaetze = [] },
            ],
            Rollen =
            [
                new Rolle { Id = 5, Name = "CO", Reihenfolge = 1, SelbstWaehlbar = true, BrauchtSchiff = true,
                    Felder = [new RollenFeld { Feld = 0, NetzId = 2, Senden = true, Hoeren = true, SendenStandard = true, Fest = true },
                              new RollenFeld { Feld = 1, Schiffsnetz = "CREW", Senden = true, Hoeren = true, SendenStandard = false, Fest = false }] },
                new Rolle { Id = 6, Name = "Gunner", Reihenfolge = 2, SelbstWaehlbar = true, BrauchtSchiff = true, Felder = [] },
                new Rolle { Id = 9, Name = "Jäger", Reihenfolge = 3, SelbstWaehlbar = true, BrauchtSchiff = false, Kapazitaet = 4, Felder = [] },
            ],
        };
        var m = new Mitgliedschaften
        {
            ZustandsVersion = 40, KontoVersion = 3, Felder = [], Global = new GlobalAngabe { Senden = true }, Sendeauswahl = new Sendeauswahl { Felder = [], Global = false },
            Stumm = [], Quelle = MitgliedschaftsQuelle.Verschieben, Durch = 3,
            LetzterStand = new LetzterStand
            {
                SchiffId = 1, RolleId = 6, Felder = [new Mitgliedschaft { Feld = 0, NetzId = 10, Hoeren = true, Senden = true, Fest = false }],
                Sendeauswahl = new Sendeauswahl { Felder = [0], Global = false },
            },
            Konto = new KontoStand
            {
                Rechte = [Recht.GlobalSenden], ScNamen = ["Mustermann"], Rollen = [], Lieblingsrollen = [], Zeichen = ["EINSATZLEITUNG"], ZeichenZeigen = true,
                Beauftragung = new BeauftragungAngabe { Von = bis - 7_200_000, Bis = bis, Rechte = [Recht.Zuweisen, Recht.SprechanzeigeAlle], Durch = 3, Grund = "Op Nachtfalke", Aktiv = true },
            },
        };
        var welcome = new Welcome
        {
            Minor = 0, Status = WelcomeStatus.Freigegeben,
            Konto = new KontoAngabe { Id = 2, Name = "Max", Benutzername = "max", Stufe = Stufe.Funker, Rechte = [Recht.GlobalSenden] },
        };
        var praesenz = new Dictionary<uint, PraesenzEintrag>
        {
            [3] = new()
            {
                KontoId = 3, Name = "Nordwind", Benutzername = "nordwind", Online = true, SchiffId = 1, RolleId = 5, Platz = PlatzStatus.Belegt, ScName = "Nordwind",
                Felder = [new Mitgliedschaft { Feld = 0, NetzId = 2, Hoeren = true, Senden = true, Fest = true }, new Mitgliedschaft { Feld = 4, NetzId = 99, Hoeren = true, Senden = false, Fest = true }],
                SendetIn = [2], Stumm = [new Stummschaltung { NetzId = 0, Bis = bis, Durch = 2 }], Verbindung = Verbindungsweg.Udp, Guete = Guete.Gut,
                Quelle = MitgliedschaftsQuelle.Selbst, Seit = bis - 3_600_000, LetzterPlatz = new LetzterPlatz { SchiffId = 1, RolleId = 6 },
            },
            [4] = new() { KontoId = 4, Name = "Eule", Benutzername = "eule", Online = false, SchiffId = 1, RolleId = 6, Platz = PlatzStatus.Reserviert },
            [5] = new() { KontoId = 5, Name = "Kobold", Benutzername = "kobold", Online = true, SchiffId = 1, RolleId = 6, Platz = PlatzStatus.Belegt },
            [6] = new() { KontoId = 6, Name = "Pollux", Benutzername = "pollux", Online = true },
        };
        var belegung = new Belegung
        {
            ZustandsVersion = 41, Online = 5, MitPlatz = 3,
            Plaetze =
            [
                new PlatzBelegung { SchiffId = 1, RolleId = 5, Belegt = 1, Reserviert = 0, Kapazitaet = 1, Mindest = 1 },
                new PlatzBelegung { SchiffId = 1, RolleId = 6, Belegt = 1, Reserviert = 1, Kapazitaet = 3, Mindest = 2 },
                new PlatzBelegung { RolleId = 9, Belegt = 1, Reserviert = 0, Kapazitaet = 4 },
            ],
        };

        var l = LageAbbildung.Abbilden(new LageEingabe(ClientZustand.AktivUdp, welcome, netzListe, m, praesenz, [], false, [], null, belegung, 1500) { ZustandsVersion = 41 });

        Pruefe.Wahr(l.Konto!.Beauftragung is { Aktiv: true, Durch: 3, Grund: "Op Nachtfalke" } b && b.Bis.ToUnixTimeMilliseconds() == bis, "Auftrag in der Lage");
        Pruefe.Wahr(l.Konto.Hat(Recht.Zuweisen) && !l.Konto.HatDauerhaft(Recht.Zuweisen) && l.Konto.Einsatzleitung, "wirksam ZUWEISEN aus dem Auftrag, dauerhaft nicht");
        Pruefe.Wahr(l is { Selbstwahl: false, SelbstwahlDurch: 7, Nachzuegler: false, NetzListeVersion: 12, ZustandsVersion: 41 } && l.SelbstwahlBis!.Value.ToUnixTimeMilliseconds() == bis,
            "Sperre mit Urheber, Frist und ohne Nachzügler; Versionen");
        Pruefe.Gleich(TimeSpan.FromMilliseconds(1500), l.ZeitVersatz, "Uhr des Servers");
        var s1 = l.SchiffVon(1)!;
        Pruefe.Wahr(s1 is { Art: Einheitsart.Schiff, Typ: "Hammerhead" } && s1.Netze["CREW"] == 10, "Einheit mit Art, Typ und Einheitsnetzen");
        Pruefe.Wahr(s1.PlatzVon(6) is { Kapazitaet: 3, Belegt: 2, Reserviert: 1, Mindest: 2, Frei: 1 }, "K1: belegt und reserviert zählen, wie der Server: " + s1.PlatzVon(6));
        Pruefe.Gleich(Einheitsart.Trupp, l.SchiffVon(20)!.Art, "Trupp");
        Pruefe.Wahr(l.RolleVon(9) is { Kapazitaet: 4, BrauchtSchiff: false }, "Rolle ohne Einheit mit Kapazität");
        Pruefe.Wahr(l.RolleVon(5)!.Felder.Count == 2 && l.RolleVon(5)!.NetzeAuf(s1).SequenceEqual(new ushort[] { 2, 10 }), "Felder der Rolle, auf der Einheit aufgelöst");
        Pruefe.Wahr(l.NetzVon(10)!.SchiffId == 1 && l.NetzVon(2)!.SchiffId is null, "Einheitsnetz und Sondernetz");
        var nordwind = l.Funker.Single(f => f.KontoId == 3);
        Pruefe.Wahr(nordwind is { Platz: PlatzStatus.Belegt, Details: { Quelle: MitgliedschaftsQuelle.Selbst, Verbindung: Verbindungsweg.Udp } d } && d.Felder.Count == 2
            && d.LetzterPlatz == ((ushort?)1, (ushort?)6) && d.Stumm.Count == 1 && d.SendetIn.SequenceEqual(new ushort[] { 2 }), "Details für die Einsatzleitung");
        Pruefe.Wahr(l.Funker.Single(f => f.KontoId == 4) is { Online: false, Platz: PlatzStatus.Reserviert, Details: null }, "reserviert, ohne Details");
        Pruefe.Wahr(l is { Online: 5, MitPlatz: 3, Quelle: MitgliedschaftsQuelle.Verschieben, QuelleDurch: 3 }, "Zähler aus der Belegung, eigene Quelle");
        Pruefe.Wahr(l.LetzterStand is { SchiffId: 1, RolleId: 6 } ls && ls.SendeFelder.SequenceEqual(new byte[] { 0 }) && ls.Felder.Single().NetzId == 10, "letzter Stand (A21)");

        // Ohne Belegung (Server vor 0.3.0): gezählt aus der Praesenz, auch die Reservierten (K1).
        var ohne = LageAbbildung.Abbilden(new LageEingabe(ClientZustand.AktivUdp, welcome, netzListe, m, praesenz, [], false, []));
        Pruefe.Wahr(ohne.SchiffVon(1)!.PlatzVon(6) is { Belegt: 2, Reserviert: 1 } && ohne is { Online: 3, MitPlatz: 2 }, "aus der Praesenz: " + ohne.SchiffVon(1)!.PlatzVon(6));

        // Der Grund statt eines grauen Knopfs (F9).
        Pruefe.Gleich("Einsteigen ganz gesperrt durch die Einsatzleitung (Konto 7) - melde dich bei ihr.", FunkClientDienst.Fehlertext(Fehlercode.SelbstwahlGesperrt, l), "ganz gesperrt");
        Pruefe.Gleich("Wechsel gesperrt durch die Einsatzleitung (Nordwind) - melde dich bei ihr.",
            FunkClientDienst.Fehlertext(Fehlercode.SelbstwahlGesperrt, l with { SelbstwahlDurch = 3, Nachzuegler = true }), "Wechsel gesperrt, mit Namen");
    }

    // ---------------------------------------------------------------- Sichtbarkeit und Kopf

    [Test]
    public static void Brett_ErscheintMitAuftragUndVerschwindetOhneNeustart()
    {
        using var a = new Aufbau(dienst: MitRaidabend());
        var h = a.Haupt(out _, out _, out _, out _);
        var raid = h.Bereiche.SingleOrDefault(x => x.Inhalt is BrettViewModel);
        // Seit 0.10.0 (AP3, Q87) gibt es vor ihm nur Start und Einstellungen; Strg+5 bleibt.
        Pruefe.Wahr(raid is { Kennung: "raid", Taste: "Strg+5" } && h.Bereiche.IndexOf(raid) == 2, "Bereich Raid an dritter Stelle, nach Start und Einstellungen");
        h.BereichWaehlenCommand.Execute("5");
        Pruefe.Wahr(ReferenceEquals(h.AktiverBereich, raid) && h.IstBrett, "Strg+5");
        var b = h.Brett;
        Pruefe.Wahr(b.Sichtbar && b.AuftragText.StartsWith("Einsatzleitung bis 04:00 (von Nordwind)", StringComparison.Ordinal) && b.KannAbgeben, "Kopf: " + b.AuftragText);
        Pruefe.Wahr(b.AuftragText.Contains("Op Nachtfalke", StringComparison.Ordinal), "mit Anlass");
        Pruefe.Gleich(null, b.AuftragWarnung, "noch lange hin");

        // 15 Minuten vor dem Ende ein Hinweis, mit der Uhr des Servers.
        a.Jetzt = new DateTimeOffset(new DateTime(2026, 10, 3, 3, 48, 0, DateTimeKind.Local));
        b.Takt();
        Pruefe.Gleich("Noch 12 Minuten, dann endet die Einsatzleitung.", b.AuftragWarnung, "Warnung vor dem Ende");

        // Abgeben: der Auftrag ist weg, das Brett verschwindet ohne Neustart.
        b.AbgebenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Abgeben"), "Beauftragung ABGEBEN");
        // Seit 0.10.0 steht Einsteigen auf Start (E10); bis 0.9.3 ging es nach Meine Kanäle.
        Pruefe.Wahr(!b.Sichtbar && h.Bereiche.All(x => x.Inhalt is not BrettViewModel) && !h.IstBrett && h.AktiverBereich.Kennung == HauptViewModel.StartKennung,
            "Bereich weg, zurück zum Einsteigen auf Start");

        // Ein ADMIN teilt auch ohne Auftrag ein; eine Führung ohne Auftrag nicht.
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Stufe.Admin, Rechte = [Recht.Zuweisen, Recht.GlobalSenden] } });
        Pruefe.Wahr(b.Sichtbar && b.AuftragText == "Einsatzleitung als Admin" && !b.KannAbgeben && h.Bereiche.Any(x => x.Kennung == "raid"), "Admin: " + b.AuftragText);
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Stufe.Raidleiter, Rechte = [Recht.GlobalSenden] } });
        Pruefe.Falsch(b.Sichtbar || h.Bereiche.Any(x => x.Kennung == "raid"), "Führung ohne Auftrag: kein Brett");
        // Ein geplanter Auftrag wirkt noch nicht.
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Beauftragung = new BeauftragungInfo(Abend.AddDays(1), Abend.AddDays(1).AddHours(5), [Recht.Zuweisen], 1, null, false) } });
        Pruefe.Falsch(b.Sichtbar, "geplant: noch kein Brett");
    }

    [Test]
    public static void Brett_SpaltenNachArt_KopfAmpelZaehlerUndKarten()
    {
        var (a, b) = Raid();
        using var _ = a;
        Pruefe.Wahr(b.Gruppen.Select(g => g.Titel).SequenceEqual(["Schiffe (3)", "Infanterie (1)", "Orga (1)", "Rollen ohne Einheit"]),
            "Spaltengruppen: " + string.Join(" | ", b.Gruppen.Select(g => g.Titel)));
        Pruefe.Gleich("13 online - 9 mit Platz - 4 ohne", b.Zaehler, "Zähler im Kopf");
        Pruefe.Wahr(Spalte(b, "Schiff 1") is { Typ: "Hammerhead", Belegung: "3/6", Ampel: Ampelstufe.Gruen, AmpelText: "Mindestbesetzung da" }, "Schiff 1 grün");
        Pruefe.Wahr(Spalte(b, "Schiff 2") is { Belegung: "2/4", Ampel: Ampelstufe.Gelb, AmpelText: "kein Gunner" }, "Schiff 2 gelb: " + Spalte(b, "Schiff 2").AmpelText);
        Pruefe.Wahr(Spalte(b, "Schiff 3") is { Belegung: "2/6", Ampel: Ampelstufe.Rot, AmpelText: "kein Pilot, kein Gunner" }, "Schiff 3 rot: " + Spalte(b, "Schiff 3").AmpelText);
        Pruefe.Wahr(Spalte(b, "Trupp Alpha") is { Belegung: "1/8", Ampel: Ampelstufe.Gelb, AmpelText: "kein Sanitäter" }, "Trupp gelb");
        Pruefe.Wahr(Spalte(b, "Logistik") is { Ampel: Ampelstufe.Keine, AmpelText: null }, "Logistik ohne Mindestbesetzung");
        Pruefe.Wahr(Spalte(b, "Schiff 3").Netze.Select(n => n.Name).SequenceEqual(["Durchsagen 3", "Crew 3", "Brücke 3"]) && Spalte(b, "Schiff 3").Netze.Single(n => n.Name == "Crew 3").Spricht,
            "Einheitsnetze mit Sprechpunkt (Anker spricht in Crew 3)");

        var gunner1 = Block(b, Schiff1, Gunner);
        Pruefe.Wahr(gunner1 is { Titel: "Gunner", Belegung: "1/2" } && Karten(gunner1).Single().Name == "Eule" && Frei(gunner1) == 1, "Rollenblock: Karte und ein freier Platz");
        Pruefe.Wahr(Block(b, null, Flottenfuehrung) is { Belegung: "0/2" } f && Frei(f) == 2, "Rolle ohne Einheit");
        Pruefe.Wahr(b.OhnePlatz.Select(k => k.Name).SequenceEqual(["Hagel", "Mustermann", "Pollux", "Zinnober"]) && b.Nachzuegler.Count == 0, "links: ohne Platz, nach Namen");
        var eule = b.Karte(Eule)!;
        Pruefe.Wahr(eule is { Marke: "selbst", Online: true, IstAdmin: false } && eule.Rolle == "Medium Gunner", "Karte mit Marke und erster Mitgliederrolle: " + eule.Rolle);
        Pruefe.Wahr(typeof(BrettKarte).GetProperty("Kuerzel") is null, "seit 0.11.8 kein Kürzel der Untergilde an der Karte");
        // Seit 0.11.7 mit Abzeichen: der Kranz zuerst, vorn steht das gewählte (Navy: Kapitän).
        Pruefe.Wahr(b.Karte(Nordwind) is { IstAdmin: true } n && n.Zeichen[0].Titel == "Admin" && n.Beschreibung.Contains("Abzeichen Admin, Navy: Fregattenkapitän, Capital-Pilot", StringComparison.Ordinal),
            "Admin mit Kranz und Schloss: " + b.Karte(Nordwind)!.Beschreibung);
        Pruefe.Wahr(b.Karte(Zinnober) is { OhneScName: true, Name: "Zinnober" }, "Marke ohne SC-Namen");
        Pruefe.Gleich("zuletzt: Schiff 3, Marine", b.Karte(Hagel)!.Zuletzt, "zuletzt");
        Pruefe.Wahr(b.Karte(Anker)!.Spricht && !b.Karte(Quarz)!.Spricht, "Sprechpunkt");
        Pruefe.Wahr(b.Karte(Hagel)!.Beschreibung.Contains("Hagel", StringComparison.Ordinal) && !b.Karte(Hagel)!.Beschreibung.Contains("BS", StringComparison.Ordinal),
            "Satz für den Vorleser, ohne Kürzel der Untergilde: " + b.Karte(Hagel)!.Beschreibung);
        Pruefe.Wahr(b.Global is { Name: "Global", IstGlobal: true } && b.Sondernetze.Select(x => x.Name).SequenceEqual(["Kommando", "Orga"]), "rechts: Global und Sondernetze");
        Pruefe.Gleich("4 drin", b.Sondernetze[0].Mitglieder, "Kommando: Nordwind und Ferox (CO), Distel (Truppführer), Brandung (Frachter)");
        Pruefe.Wahr(b.Ziele.Any(z => z.Text == "Schiff 3 - Pilot (0/1)"), "Setzen auf ...: " + string.Join(" | ", b.Ziele.Select(z => z.Text).Take(4)));
    }

    // ---------------------------------------------------------------- Ablegen

    [Test]
    public static async Task Brett_AblageAufFreienPlatz_EinVerschiebenMitZusatznetzen()
    {
        var (a, b) = Raid();
        using var _ = a;
        // Kobold hat ein Zusatznetz (Orga, fest): Es geht beim Platzwechsel nicht verloren.
        a.Dienst.LageSetzen(l => l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId != Kobold ? f : f with { Details = f.Details! with { Felder = [.. f.Details.Felder, new FeldInfo(5, NetzOrga, true, false, true)] } })],
        });
        var kobold = b.Karte(Kobold)!;
        var pilot3 = Block(b, Schiff3, Pilot);
        var bewertung = b.Bewerten(kobold, pilot3);
        Pruefe.Wahr(bewertung.Wirkung == Ablagewirkung.Passt && bewertung.Text.StartsWith("passt: Schiff 3, Pilot", StringComparison.Ordinal)
            && bewertung.Text.Contains("Brücke 3", StringComparison.Ordinal), "grün mit den Netzen der Rolle (A22): " + bewertung.Text);
        Pruefe.Wahr(await b.AblegenAsync(kobold, pilot3), "abgelegt");
        Pruefe.Gleich("Verschieben 4:2/3 +3h @100", a.Dienst.Aufrufe[^1], "genau ein Verschieben mit Rolle, Einheit und dem festen Zusatznetz als felderPatch");
        Pruefe.Wahr(Karten(pilot3).Single() == kobold && Frei(Block(b, Schiff1, Pilot)) == 1, "Karte steht am Ziel, der alte Platz ist frei");
        Pruefe.Wahr(kobold is { Wartet: false, Marke: "von dir" }, "fertig, Marke");
        Pruefe.Gleich("20:52 du: Kobold -> Schiff 3, Pilot", b.Verlauf[0], "Verlauf");
        Pruefe.Wahr(Spalte(b, "Schiff 3").AmpelText == "kein Gunner" && Spalte(b, "Schiff 1").AmpelText == "kein Pilot", "Ampeln folgen");

        // Ein Nachzügler aus der Liste links auf einen freien Platz einer Rolle ohne Einheit.
        Pruefe.Wahr(await b.AblegenAsync(b.Karte(Pollux)!, Block(b, null, Flottenfuehrung)), "Rolle ohne Einheit");
        Pruefe.Gleich("Verschieben 14:9/- @101", a.Dienst.Aufrufe[^1], "ohne Einheit kein schiffId");
    }

    [Test]
    public static async Task Brett_Tausch_ZweiAenderungenInEinerAnfrage()
    {
        var (a, b) = Raid();
        using var _ = a;
        var falke = b.Karte(Falke)!;
        var kobold = b.Karte(Kobold)!;
        var t = b.Bewerten(falke, kobold);
        Pruefe.Wahr(t is { Wirkung: Ablagewirkung.Tausch, Text: "Tausch mit Kobold" }, "gelb: " + t.Text);
        Pruefe.Wahr(await b.AblegenAsync(falke, kobold), "getauscht");
        Pruefe.Gleich("Verschieben 7:2/1; 4:2/2 @100", a.Dienst.Aufrufe[^1], "ein Verschieben mit zwei Änderungen; die Kapazität prüft der Server am Endzustand");
        Pruefe.Wahr(Karten(Block(b, Schiff1, Pilot)).Single() == falke && Karten(Block(b, Schiff2, Pilot)).Single() == kobold, "beide am neuen Platz");

        // Wer keinen Platz hat, tauscht mit jemandem auf einem Platz: der geht auf Standard.
        var hagel = b.Karte(Hagel)!;
        Pruefe.Gleich("Tausch mit Anker: Anker geht auf Standard", b.Bewerten(hagel, b.Karte(Anker)!).Text, "Tausch aus der Liste");
        Pruefe.Wahr(await b.AblegenAsync(hagel, b.Karte(Anker)!), "Tausch aus der Liste");
        Pruefe.Gleich("Verschieben 10:4/3; 8:0/- @101", a.Dienst.Aufrufe[^1], "Anker auf Standard");
        Pruefe.Wahr(b.OhnePlatz.Contains(b.Karte(Anker)!), "Anker links");
    }

    [Test]
    public static async Task Brett_RotUndGelb_MitGrund_UndBesterPlatzAmKopf()
    {
        var (a, b) = Raid();
        using var _ = a;
        await b.KartenAbwartenAsync();
        Pruefe.Wahr(b.Bewerten(b.Karte(Nordwind)!, Block(b, Schiff3, Pilot)) is { Wirkung: Ablagewirkung.GehtNicht, Text: "Nordwind ist Admin - teilt sich selbst ein" }, "rot: Admin");
        Pruefe.Wahr(b.Bewerten(b.Karte(Falke)!, Block(b, Schiff1, Pilot)) is { Wirkung: Ablagewirkung.GehtNicht, Text: "Schiff 1, Pilot ist voll (1/1)" }, "rot: voll");
        Pruefe.Wahr(b.Bewerten(b.Karte(Kobold)!, Block(b, Schiff1, Pilot)).Wirkung == Ablagewirkung.Keine, "derselbe Platz: nichts");
        Pruefe.Wahr(b.Bewerten(b.Karte(Kobold)!, b.Karte(Nordwind)!) is { Wirkung: Ablagewirkung.GehtNicht }, "Tausch mit einem Admin: rot");
        Pruefe.Falsch(await b.AblegenAsync(b.Karte(Nordwind)!, Block(b, Schiff3, Pilot)), "rot geht nicht hinaus");
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("Verschieben", StringComparison.Ordinal)), "nichts verschoben");

        // Kopf einer Einheit: bester freier Platz nach Mitgliederrolle, Wunsch, letztem Platz, sonst die Rolle mit den meisten freien.
        Pruefe.Gleich("passt: Schiff 1, Gunner - Kanäle Crew 1, Brücke 1, Durchsagen 1", b.Bewerten(b.Karte(Pollux)!, Spalte(b, "Schiff 1")).Text, "Pollux wünscht Medium Gunner");
        Pruefe.Wahr(b.Bewerten(b.Karte(Hagel)!, Spalte(b, "Schiff 3")).Text.StartsWith("passt: Schiff 3, Marine", StringComparison.Ordinal), "Hagel: Marine");
        Pruefe.Wahr(b.Bewerten(b.Karte(Zinnober)!, Spalte(b, "Schiff 1")).Text.StartsWith("passt: Schiff 1, Marine", StringComparison.Ordinal), "ohne Angaben: die meisten freien Plätze");
        Pruefe.Wahr(await b.AblegenAsync(b.Karte(Pollux)!, Spalte(b, "Schiff 1")), "auf den Kopf");
        Pruefe.Gleich("Verschieben 14:3/1 @100", a.Dienst.Aufrufe[^1], "bester Platz");
        Pruefe.Wahr(b.Bewerten(b.Karte(Zinnober)!, Spalte(b, "Schiff 2")) is { Wirkung: Ablagewirkung.Passt }, "Schiff 2 hat noch Gunner");
        Pruefe.Wahr(await b.AblegenAsync(b.Karte(Zinnober)!, Block(b, Schiff2, Gunner)) && await b.AblegenAsync(b.Karte(Hagel)!, Block(b, Schiff2, Gunner)), "Schiff 2 voll machen");
        Pruefe.Wahr(b.Bewerten(b.Karte(Mustermann)!, Spalte(b, "Schiff 2")) is { Wirkung: Ablagewirkung.GehtNicht, Text: "Schiff 2 ist voll" }, "Kopf einer vollen Einheit: rot");
    }

    [Test]
    public static async Task Brett_ListeLinks_StandardUndNachzuegler()
    {
        var (a, b) = Raid();
        using var _ = a;
        var eule = b.Karte(Eule)!;
        // 0.11.2 (Sarevoc 13.10.2026): Ein Name, in die Liste links gezogen, tut nichts mehr (bis 0.11.1 "auf Standard (ohne Platz)"); auf Standard
        // setzt "Platz räumen".
        Pruefe.Gleich(Ablagebewertung.Nichts, b.Bewerten(eule, b.Teilnehmerziel), "Liste links: nichts");
        Pruefe.Falsch(await b.AblegenAsync(eule, b.Teilnehmerziel), "nicht abgelegt");
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("Verschieben", StringComparison.Ordinal)), "nichts verschoben");
        b.WaehlenCommand.Execute(eule);
        await b.PlatzRaeumenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Verschieben 5:0/- @100", a.Dienst.Aufrufe[^1], "rolleId 0");
        Pruefe.Wahr(b.OhnePlatz.Contains(eule) && !b.Nachzuegler.Contains(eule) && eule.Zuletzt == "zuletzt: Schiff 1, Gunner", "Eule links mit letztem Platz");
        Pruefe.Gleich("20:52 du: Eule -> ohne Platz", b.Verlauf[0], "Verlauf");

        // Wer neu in den Funk kommt und keinen Platz hat, steht unter den Nachzüglern, 15 Minuten lang.
        a.Dienst.LageSetzen(l => Belegungsstand(l with { Funker = [.. l.Funker, new FunkerInfo(15, "Wolke", true, null, null) { ScName = "Wolke", Details = new FunkerDetails([], [], []) }] }));
        var wolke = b.Karte(15)!;
        Pruefe.Wahr(b.Nachzuegler.Contains(wolke) && !b.OhnePlatz.Contains(wolke), "neu im Funk");
        a.Jetzt = Abend.AddMinutes(16);
        b.Takt();
        Pruefe.Wahr(!b.Nachzuegler.Contains(wolke) && b.OhnePlatz.Contains(wolke), "nach 15 Minuten unter ohne Platz");
    }

    [Test]
    public static void Brett_Suche_MaxFindetMustermann()
    {
        var (a, b) = Raid();
        using var _ = a;
        b.Suche = "max";
        Pruefe.Wahr(b.Karte(Mustermann)!.Treffer && !b.Karte(Kobold)!.Treffer && !b.Karte(Eule)!.Treffer, "max findet Mustermann über den Discord-Namen");
        // Seit 0.11.8 ohne Untergilden: "bs" findet keine Beispielstaffel mehr, nur Namen, die "bs" enthalten (hier keinen).
        b.Suche = "bs";
        Pruefe.Wahr(!b.Karte(Eule)!.Treffer && !b.Karte(Hagel)!.Treffer && !b.Karte(Mustermann)!.Treffer, "Kürzel BS findet niemanden");
        b.Suche = "Eule/M";
        Pruefe.Wahr(b.Karte(Eule)!.Treffer && !b.Karte(Hagel)!.Treffer, "Discord-Name findet Eule");
        b.Suche = "";
        Pruefe.Wahr(b.Karte(Falke)!.Treffer, "ohne Suche alles");
    }

    // ---------------------------------------------------------------- Funkverkehr baut nichts neu

    /// <summary>
    /// CODEMAP, Fallen: Jeder Push, auch jede Sprechanzeige, baut die FunkLage neu. Das Brett ersetzt dabei nichts, was sich nicht
    /// geändert hat: keine Sammlung meldet etwas, alle Karten bleiben dieselben Objekte; nur der Sprechpunkt wechselt. Wandert eine
    /// Person, ändern sich genau die zwei Rollenblöcke.
    /// </summary>
    [Test]
    public static void Brett_FunkverkehrBautNichtNeu()
    {
        var (a, b) = Raid();
        using var _ = a;
        var meldungen = new List<string>();
        void Beobachten(INotifyCollectionChanged c, string name) => c.CollectionChanged += (_, _) => meldungen.Add(name);
        Beobachten(b.Gruppen, "Gruppen");
        Beobachten(b.OhnePlatz, "OhnePlatz");
        Beobachten(b.Nachzuegler, "Nachzuegler");
        Beobachten(b.Sondernetze, "Sondernetze");
        foreach (var g in b.Gruppen)
        {
            Beobachten(g.Spalten, g.Titel);
            foreach (var s in g.Spalten)
            {
                Beobachten(s.Bloecke, s.Name);
                foreach (var bl in s.Bloecke) Beobachten(bl.Eintraege, s.Name + "/" + bl.Titel);
            }
        }
        var karten = new[] { Nordwind, Kobold, Eule, Falke, Anker, Hagel }.Select(k => b.Karte(k)!).ToList();
        var spalten = b.Gruppen.SelectMany(g => g.Spalten).ToList();
        for (var i = 0; i < 5; i++)
        {
            a.Dienst.LageSetzen(l => l with { Sprecher = [new SprecherInfo(Kobold, "Kobold", [12], false)] });
            a.Dienst.LageSetzen(l => l with { Sprecher = [] });
        }
        a.Dienst.LageSetzen(l => l with { Sprecher = [new SprecherInfo(Kobold, "Kobold", [12], false)] });
        Pruefe.Gleich(0, meldungen.Count, "Sprechanzeige: keine Sammlung ändert sich (" + string.Join(", ", meldungen) + ")");
        Pruefe.Wahr(karten.Zip(new[] { Nordwind, Kobold, Eule, Falke, Anker, Hagel }).All(x => ReferenceEquals(x.First, b.Karte(x.Second))), "dieselben Karten");
        Pruefe.Wahr(spalten.SequenceEqual(b.Gruppen.SelectMany(g => g.Spalten)), "dieselben Spalten");
        Pruefe.Wahr(b.Karte(Kobold)!.Spricht && !b.Karte(Anker)!.Spricht, "nur der Sprechpunkt wechselt");

        // Falke wechselt (von Ferox eingeteilt): genau sein alter und sein neuer Block melden sich.
        a.Dienst.LageSetzen(l => Belegungsstand(l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId != Falke ? f : f with { SchiffId = Schiff3, RolleId = Gunner, Details = f.Details! with { Quelle = MitgliedschaftsQuelle.Verschieben, Durch = Ferox } })],
        }));
        Pruefe.Wahr(meldungen.Distinct().Order(StringComparer.Ordinal).SequenceEqual(["Schiff 2/Pilot", "Schiff 3/Gunner"]), "nur die zwei Blöcke: " + string.Join(", ", meldungen.Distinct()));
        Pruefe.Wahr(ReferenceEquals(karten[3], Karten(Block(b, Schiff3, Gunner)).Single()) && karten[3].Marke == "von Ferox", "dieselbe Karte, neue Marke");
        Pruefe.Gleich("20:52 Ferox: Falke -> Schiff 3, Gunner", b.Verlauf[0], "fremde Umstellung im Verlauf");
    }

    // ---------------------------------------------------------------- Wie zuletzt, Sperren, Abend

    [Test]
    public static async Task Brett_WieZuletzt_NurOnlineOhnePlatzUndNurWasPasst()
    {
        var (a, b) = Raid();
        using var _ = a;
        // Pollux war zuletzt Pilot auf Schiff 1, der Platz ist besetzt; Kranich ist offline.
        a.Dienst.LageSetzen(l => l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId != Pollux ? f : f with { Details = f.Details! with { LetzterPlatz = (Schiff1, Pilot) } })],
        });
        Pruefe.Gleich("1 passt: Hagel -> Schiff 3, Marine. Passt nicht: Pollux (Schiff 1, Pilot ist voll). Ohne letzten Platz: Mustermann, Zinnober.", b.WieZuletztVorschau,
            "Vorschau vor dem Klick");
        await b.WieZuletztCommand.ExecuteAsync(null);
        Pruefe.Gleich("Verschieben 10:4/3", a.Dienst.Aufrufe[^1], "nur Hagel, nur online, nur was passt");
        Pruefe.Wahr(Karten(Block(b, Schiff3, Marine)).Any(k => k.KontoId == Hagel), "Hagel auf Schiff 3");
        Pruefe.Gleich("Niemand ohne Platz hat einen freien letzten Platz.", b.WieZuletztVorschau, "danach");
    }

    [Test]
    public static async Task Brett_WechselSperren_GanzSperren_UndAbendBeenden()
    {
        var (a, b) = Raid();
        using var _ = a;
        b.WechselGesperrt = true;
        Pruefe.Gleich("Selbstwahl aus, Nachzügler ja", a.Dienst.Aufrufe[^1], "Wechsel sperren");
        Pruefe.Gleich("Wechsel gesperrt durch dich bis 08:52 - Nachzügler ohne Platz steigen noch selbst ein.", b.SperrText, "Sperrtext");
        b.GanzGesperrt = true;
        Pruefe.Wahr(a.Dienst.Aufrufe[^1] == "Selbstwahl aus, Nachzügler nein" && b.WechselGesperrt, "ganz sperren");
        Pruefe.Gleich("Einsteigen ganz gesperrt durch dich bis 08:52.", b.SperrText, "ganz");
        b.GanzGesperrt = false;
        Pruefe.Wahr(a.Dienst.Aufrufe[^1] == "Selbstwahl aus, Nachzügler ja" && b.WechselGesperrt, "zurück auf Wechsel sperren");

        // Abend beenden: Selbstwahl an, Plätze der Offline-Konten frei, Eingeteilte auf Standard (ohne Admins), Auftrag abgeben.
        a.Dienst.LageSetzen(l => Belegungsstand(l with { Funker = [.. l.Funker.Select(f => f.KontoId != Distel ? f : f with { Online = false, Platz = PlatzStatus.Reserviert })] }));
        b.AbendOffen = true;
        var vorher = a.Dienst.Aufrufe.Count;
        await b.AbendBeendenCommand.ExecuteAsync(null);
        var neu = a.Dienst.Aufrufe.Skip(vorher).ToList();
        Pruefe.Wahr(neu.Count == 4 && neu[0] == "Selbstwahl an" && neu[1] == "PlaetzeFreigeben" && neu[3] == "Abgeben", "Reihenfolge: " + string.Join(" | ", neu));
        Pruefe.Gleich("Verschieben 3:0/-; 4:0/-; 5:0/-; 7:0/-; 8:0/-; 9:0/-; 13:0/-", neu[2], "alle Eingeteilten außer Admins auf Standard");
        Pruefe.Wahr(!b.AbendOffen && !b.Sichtbar, "fertig, Auftrag abgegeben");
    }

    [Test]
    public static async Task Brett_Rueckgaengig_UndVeraltet()
    {
        var (a, b) = Raid();
        using var _ = a;
        await b.AblegenAsync(b.Karte(Kobold)!, Block(b, Schiff3, Pilot));
        await b.RueckgaengigCommand.ExecuteAsync(null);
        Pruefe.Gleich("Verschieben 4:2/1 @101", a.Dienst.Aufrufe[^1], "Umkehr mit basisVersion");
        Pruefe.Wahr(Karten(Block(b, Schiff1, Pilot)).Single().KontoId == Kobold, "zurück");

        await b.AblegenAsync(b.Karte(Kobold)!, Block(b, Schiff3, Pilot));
        // Ferox setzt Kobold danach selbst um; das Zurücknehmen überschreibt das nicht.
        a.Dienst.LageSetzen(l => Belegungsstand(l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId != Kobold ? f : f with { SchiffId = Schiff2, RolleId = Gunner, Details = f.Details! with { Quelle = MitgliedschaftsQuelle.Verschieben, Durch = Ferox } })],
        }));
        a.Dienst.NaechstesVeraltet = true;
        await b.RueckgaengigCommand.ExecuteAsync(null);
        Pruefe.Gleich("Nicht zurückgenommen: Kobold wurde danach von Ferox verschoben.", b.Meldung, "VERALTET");
        Pruefe.Wahr(Karten(Block(b, Schiff2, Gunner)).Any(k => k.KontoId == Kobold), "Ferox' Stand bleibt");
        await b.RueckgaengigCommand.ExecuteAsync(null);
        Pruefe.Gleich("Nichts zum Zurücknehmen.", b.Meldung, "nur einmal");
    }

    [Test]
    public static async Task Brett_WartetUndBeiFehlerZurueck_AuftragEndetMittendrin()
    {
        var (a, b) = Raid();
        using var _ = a;
        var kobold = b.Karte(Kobold)!;
        var halten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        a.Dienst.VerschiebenHalten = halten;
        a.Dienst.NaechsterFehler = "Der Platz ist schon belegt.";
        var ablage = b.AblegenAsync(kobold, Block(b, Schiff3, Pilot));
        Pruefe.Wahr(kobold.Wartet && Karten(Block(b, Schiff3, Pilot)).Contains(kobold) && !Karten(Block(b, Schiff1, Pilot)).Contains(kobold), "springt sofort ans Ziel, wartet");
        halten.SetResult();
        Pruefe.Falsch(await ablage, "abgelehnt");
        Pruefe.Wahr(!kobold.Wartet && Karten(Block(b, Schiff1, Pilot)).Contains(kobold) && !Karten(Block(b, Schiff3, Pilot)).Contains(kobold), "zurück an den alten Platz");
        Pruefe.Gleich("Kobold: Der Platz ist schon belegt.", b.Meldung, "Grund");

        // Endet der Auftrag, während eine Ablage unterwegs ist, verschwindet das Brett ohne Absturz.
        var zweites = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        a.Dienst.VerschiebenHalten = zweites;
        var offen = b.AblegenAsync(kobold, Block(b, Schiff3, Pilot));
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Beauftragung = null } });
        Pruefe.Falsch(b.Sichtbar, "Brett weg");
        zweites.SetResult();
        Pruefe.Falsch(await offen, "der Server sagt KEIN_RECHT");
        Pruefe.Wahr(b.Meldung!.Contains("Recht", StringComparison.Ordinal), "Grund: " + b.Meldung);
    }

    [Test]
    public static async Task Brett_DetailStummPlatzRaeumenUndZusatznetze()
    {
        var (a, b) = Raid();
        using var _ = a;
        var brandung = b.Karte(Brandung)!;
        b.WaehlenCommand.Execute(brandung);
        Pruefe.Wahr(brandung.Gewaehlt && b.AuswahlFelder.Select(f => f.Netz).SequenceEqual(["Logistik", "Kommando"]) && b.AuswahlFelder.All(f => !f.Zusatz), "Felder der Rolle");
        Pruefe.Wahr(b.AuswahlText!.Contains("Logistik, Frachter", StringComparison.Ordinal) && b.AuswahlText.Contains("selbst", StringComparison.Ordinal), "Detail: " + b.AuswahlText);

        // Zusatznetz per Ablage auf ein Sondernetz rechts: hört, oder hört und sendet.
        var orga = b.Sondernetze.Single(n => n.Name == "Orga");
        Pruefe.Gleich("Orga als Zusatzkanal: hört und sendet", b.Bewerten(brandung, orga.HoerenSenden).Text, "Text an der Maus");
        Pruefe.Wahr(await b.AblegenAsync(brandung, orga.HoerenSenden), "Zusatznetz");
        Pruefe.Gleich("Verschieben 13:-/- +3s @100", a.Dienst.Aufrufe[^1], "felderPatch ohne Rolle");
        var zusatz = b.AuswahlFelder.Single(f => f.Netz == "Orga");
        Pruefe.Wahr(zusatz is { Zusatz: true, Art: "hört und sendet, fest" }, "Zusatznetz im Detail");
        await b.ZusatznetzEntfernenCommand.ExecuteAsync(zusatz);
        Pruefe.Gleich("Verschieben 13:-/- -f" + zusatz.Feld.ToString(System.Globalization.CultureInfo.InvariantCulture) + " @101", a.Dienst.Aufrufe[^1], "Feld leeren");

        await b.StummCommand.ExecuteAsync("300");
        Pruefe.Gleich("Stumm 13 0 300", a.Dienst.Aufrufe[^1], "stumm für 5 Minuten, alle Netze");
        Pruefe.Wahr(b.AuswahlText!.Contains("stumm bis 20:57", StringComparison.Ordinal), "Stumm im Detail: " + b.AuswahlText);
        await b.PlatzRaeumenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Verschieben 13:0/- @102", a.Dienst.Aufrufe[^1], "Platz räumen");

        // Tastatur: "Setzen auf ..." aus dem Kontextmenü.
        b.WaehlenCommand.Execute(b.Karte(Pollux));
        await b.SetzenCommand.ExecuteAsync(b.Ziele.Single(z => z.Text == "Schiff 3 - Pilot (0/1)"));
        Pruefe.Gleich("Verschieben 14:2/3 @103", a.Dienst.Aufrufe[^1], "Setzen auf");
    }

    [Test]
    public static async Task Brett_AlleMitglieder_AusDemVerzeichnis()
    {
        var (a, b) = Raid();
        using var _ = a;
        await b.VerzeichnisLadenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Verzeichnis"), "Kontenverzeichnis geholt");
        var kranich = b.AlleMitglieder.Single();
        Pruefe.Wahr(kranich is { KontoId: Kranich, Online: false, Zuletzt: "zuletzt: Schiff 1, Gunner" }, "Kranich offline");
        Pruefe.Wahr(b.Bewerten(kranich, Block(b, Schiff2, Gunner)).Text.EndsWith("(reserviert, offline)", StringComparison.Ordinal), "Reservierung");
        Pruefe.Wahr(await b.AblegenAsync(kranich, Block(b, Schiff2, Gunner)), "eingeteilt, obwohl offline");
        Pruefe.Gleich("Verschieben 6:3/2 @100", a.Dienst.Aufrufe[^1], "Verschieben mit basisVersion");
        Pruefe.Wahr(Karten(Block(b, Schiff2, Gunner)).Single() is { KontoId: Kranich, Marke: "reserviert" } && b.AlleMitglieder.Count == 0, "jetzt auf dem Brett, reserviert");
    }

    [Test]
    public static void Brett_ZiehenUndAblegenUeberGong()
    {
        var (a, b) = Raid();
        using var _ = a;
        var kobold = b.Karte(Kobold)!;
        var pilot3 = Block(b, Schiff3, Pilot);
        var ziehen = new Ziehinfo(kobold);
        Pruefe.Wahr(b.Ziehen.CanStartDrag(ziehen), "Karten lassen sich ziehen");
        Pruefe.Falsch(b.Ziehen.CanStartDrag(new Ziehinfo(pilot3.Eintraege.OfType<FreierPlatz>().First())), "freie Plätze nicht");
        b.Ziehen.StartDrag(ziehen);
        Pruefe.Wahr(ReferenceEquals(ziehen.Data, kobold) && ziehen.Effects == System.Windows.DragDropEffects.Move, "Daten der Karte");
        // 0.11.0: Die gezogene Karte bleibt bis zum Loslassen gedimmt an ihrer Stelle (Personenkachel in den Listen links).
        Pruefe.Wahr(kobold.Gezogen && b.Karte(Falke)!.Gezogen == false, "gezogen: nur Kobold");
        b.Ziehen.DragCancelled();
        Pruefe.Falsch(kobold.Gezogen, "abgebrochen: nicht mehr gezogen");
        b.Ziehen.StartDrag(ziehen);

        var ueber = new Ablageinfo(kobold, null);
        pilot3.DragOver(ueber);
        Pruefe.Wahr(ueber.Effects == System.Windows.DragDropEffects.Move && ueber.EffectText!.StartsWith("passt", StringComparison.Ordinal) && pilot3.Ablagezustand == Ablagewirkung.Passt,
            "grün über dem Block: " + ueber.EffectText);
        var aufKarte = new Ablageinfo(b.Karte(Falke)!, kobold);
        Block(b, Schiff1, Pilot).DragOver(aufKarte);
        Pruefe.Wahr(aufKarte.EffectText == "Tausch mit Kobold" && Block(b, Schiff1, Pilot).Ablagezustand == Ablagewirkung.Tausch, "gelb über einer Karte");
        Block(b, Schiff1, Pilot).DragLeave(aufKarte);
        Pruefe.Gleich(Ablagewirkung.Keine, Block(b, Schiff1, Pilot).Ablagezustand, "beim Verlassen zurück");
        var rot = new Ablageinfo(b.Karte(Nordwind)!, null);
        pilot3.DragOver(rot);
        Pruefe.Wahr(rot.Effects == System.Windows.DragDropEffects.None && pilot3.Ablagezustand == Ablagewirkung.GehtNicht, "rot");
        pilot3.Drop(ueber);
        Pruefe.Wahr(SpinWait.SpinUntil(() => a.Dienst.Aufrufe.Any(x => x.StartsWith("Verschieben 4:2/3 ", StringComparison.Ordinal)), 5000), "Ablage über gong: " + string.Join(" | ", a.Dienst.Aufrufe));
        Pruefe.Gleich(Ablagewirkung.Keine, pilot3.Ablagezustand, "nach dem Ablegen zurück");
        Pruefe.Wahr(kobold.Gezogen, "bis gong das Ziehen beendet, gezogen");
        b.Ziehen.DragDropOperationFinished(System.Windows.DragDropEffects.Move, ziehen);
        Pruefe.Falsch(kobold.Gezogen, "nach dem Ziehen nicht mehr gezogen");
    }
}

/// <summary>Was gong-wpf-dragdrop beim Ziehen übergibt, ohne Maus und ohne Fenster.</summary>
internal sealed class Ziehinfo(object quelle) : GongSolutions.Wpf.DragDrop.IDragInfo
{
    public System.Windows.DataFormat DataFormat { get; set; } = System.Windows.DataFormats.GetDataFormat("GongSolutions.Wpf.DragDrop");
    public object? Data { get; set; }
    public System.Windows.Point DragStartPosition => default;
    public System.Windows.Point PositionInDraggedItem { get; set; }
    public System.Windows.DragDropEffects Effects { get; set; }
    public System.Windows.Input.MouseButton MouseButton => System.Windows.Input.MouseButton.Left;
    public System.Collections.IEnumerable? SourceCollection => null;
    public int SourceIndex => 0;
    public object? SourceItem => quelle;
    public System.Collections.IEnumerable SourceItems => new[] { quelle };
    public System.Windows.Data.CollectionViewGroup? SourceGroup => null;
    public System.Windows.UIElement? VisualSource => null;
    public System.Windows.UIElement? VisualSourceItem => null;
    public System.Windows.FlowDirection VisualSourceFlowDirection => System.Windows.FlowDirection.LeftToRight;
    public object? DataObject { get; set; }
    public Func<System.Windows.DependencyObject, object, System.Windows.DragDropEffects, System.Windows.DragDropEffects>? DragDropHandler { get; set; }
    public System.Windows.DragDropKeyStates DragDropCopyKeyState => System.Windows.DragDropKeyStates.ControlKey;

    public void RefreshSourceItems(object sender)
    {
    }
}

/// <summary>
/// Was gong-wpf-dragdrop beim Ablegen übergibt: die gezogene Karte und das Element unter der Maus; seit 0.11.1 AP2 auch das Zielelement mit
/// dem Loslasspunkt und das Ziehen (Griff), damit die Landung einen Anfang hat.
/// </summary>
internal sealed class Ablageinfo(object daten, object? zielElement) : GongSolutions.Wpf.DragDrop.IDropInfo
{
    public object? Data { get; set; } = daten;
    public GongSolutions.Wpf.DragDrop.IDragInfo? DragInfo { get; set; }
    public System.Windows.Point DropPosition { get; set; }
    public Type? DropTargetAdorner { get; set; }
    public Type? DropTargetHintAdorner { get; set; }
    public GongSolutions.Wpf.DragDrop.DropHintState DropTargetHintState { get; set; }
    public string? DropHintText { get; set; }
    public System.Windows.DragDropEffects Effects { get; set; }
    public int InsertIndex => 0;
    public int UnfilteredInsertIndex => 0;
    public System.Collections.IEnumerable? TargetCollection => null;
    public object? TargetItem => zielElement;
    public System.Windows.Data.CollectionViewGroup? TargetGroup => null;
    public System.Windows.Controls.ScrollViewer? TargetScrollViewer => null;
    public GongSolutions.Wpf.DragDrop.ScrollingMode TargetScrollingMode { get; set; }
    public System.Windows.UIElement? VisualTarget { get; set; }
    public System.Windows.UIElement? VisualTargetItem => null;
    public System.Windows.Controls.Orientation VisualTargetOrientation => System.Windows.Controls.Orientation.Vertical;
    public System.Windows.FlowDirection VisualTargetFlowDirection => System.Windows.FlowDirection.LeftToRight;
    public string? DestinationText { get; set; }
    public string? EffectText { get; set; }
    public GongSolutions.Wpf.DragDrop.RelativeInsertPosition InsertPosition => GongSolutions.Wpf.DragDrop.RelativeInsertPosition.None;
    public System.Windows.DragDropKeyStates KeyStates => System.Windows.DragDropKeyStates.None;
    public bool NotHandled { get; set; }
    public bool IsSameDragDropContextAsSource => true;
    public GongSolutions.Wpf.DragDrop.EventType EventType => GongSolutions.Wpf.DragDrop.EventType.Bubbled;
    public bool AcceptChildItem { get; set; }
}
