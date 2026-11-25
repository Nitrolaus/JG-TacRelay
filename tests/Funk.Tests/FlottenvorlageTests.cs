// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;
using Funk.Server.Kern;
using Funk.Server.Steuer;
using L = Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Startvorlage der Flotte (A40) und die Zeitrechnung eines Auftrags (6.12 "bis 04:00"): Die Vorlage Star Citizen (Sarevoc, 19.10.2026, Q26) ergibt
/// auf einer leeren Flotte zehn Kanäle mit Ch. und 14 Rollen als ein Paket unter der Grenze; die gelb markierten Annahmen bleiben Annahmen,
/// mit erlaubten Strichen. IDs weichen der vorhandenen NetzListe aus. Den Abgleich mit der Vorgabe macht Stufe3VorlageTests.
/// </summary>
internal static class FlottenvorlageTests
{
    private const uint Admin = 1;

    private static L.Zustandsschreiber LeereFlotte(Func<L.SchnappschussBauer, L.SchnappschussBauer>? zusatz = null)
    {
        var bauer = new L.SchnappschussBauer().MitKonto(new L.Konto { Id = Admin, Name = "Test", Stufe = L.Stufe.Admin });
        return new L.Zustandsschreiber((zusatz?.Invoke(bauer) ?? bauer).Bauen());
    }

    [Test]
    public static void StarCitizen_ZehnKanaele_VierzehnRollen_EinPaket_AufLeererFlotte()
    {
        var vorlage = Flottenvorlagen.StarCitizen();
        Pruefe.Wahr(vorlage.Paket.Ops.Count is > 0 and <= Grenzen.PaketSchritteMax, "ein Paket unter der Grenze: " + vorlage.Paket.Ops.Count);
        var (schritte, fehler) = Zentrale.PaketLesen(vorlage.Paket);
        Pruefe.Wahr(schritte is not null, "Paket lesbar: " + fehler);
        var s = LeereFlotte();
        var r = s.Ausfuehren(L.Auftrag.Paket(Admin, schritte!, s.Aktuell.NetzListeVersion), 1);
        Pruefe.Wahr(r.Erfolg && r.NetzListeGeaendert, "übernommen: " + r);
        var z = s.Aktuell;

        Pruefe.Gleich(0, z.Schiffe.Count, "keine Einheiten");
        Pruefe.Gleich(11, z.Netze.Count, "zehn Kanäle und Global");
        Pruefe.Gleich(10, L.Kanaele.Anzahl(z.Netze), "alle zehn mit Ch., Global ohne");
        Pruefe.Gleich("Kommando,Flotte,Brücke,Crew,Jäger,Bodentrupp,Logistik,Bergung und Bergbau,Sanität,Allgemein",
            string.Join(',', z.Netze.Values.Where(n => n.Id != L.Netz.GlobalId).OrderBy(n => n.Reihenfolge).Select(n => n.Name)), "Kanäle in ihrer Reihenfolge");
        Pruefe.Gleich(14, z.Rollen.Count, "14 Rollen");
        var kapitaen = z.Rollen.Values.Single(x => x.Name == "Kapitän");
        Pruefe.Wahr(kapitaen is { SelbstWaehlbar: true, BrauchtSchiff: false }, "Kapitän selbst wählbar, ohne Einheit");
        Pruefe.Gleich("Brücke,Crew,Flotte,Kommando,Sanität,Logistik", string.Join(',', kapitaen.Felder.OrderBy(f => f.Feld).Select(f => z.Netze[f.NetzId].Name)), "Felder des Kapitäns");
        Pruefe.Wahr(kapitaen.Felder.Single(f => f.SendenStandard) is { Feld: 0 }, "Sendevorschlag Brücke in Feld 0");
        Pruefe.Wahr(z.Netze.Values.Where(n => n.Id != L.Netz.GlobalId).All(n => n.AusloeserStandard == L.Ausloeser.Va && n.Offen == L.Offenheit.HoerenSenden),
            "alle mit VA, auch Kommando (Q53), alle offen");

        // Die Rolle wählen: Brücke und Crew stehen auf Ruhe, bis der Funker oder die Leitung den Ch. wählt.
        Pruefe.Wahr(s.Ausfuehren(L.Auftrag.RolleWaehlen(Admin, kapitaen.Id), 2).Erfolg, "Test wählt Kapitän");
        var felder = s.Aktuell.Konten[Admin].Felder;
        Pruefe.Gleich("0,0,1,1,1,1", string.Join(',', felder.OrderBy(f => f.Feld).Select(f => Kanalrechnung.Zerlegen(f.NetzId, out _, out var ch) ? ch : -1)),
            "Ch. der Felder: Brücke und Crew Ruhe, die übrigen ihr erster Ch.");

        // Die Annahmen bleiben Annahmen und stehen im Klartext da, nur mit erlaubten Strichen.
        Pruefe.Gleich(6, vorlage.Annahmen.Count, "sechs Annahmen");
        foreach (var a in vorlage.Annahmen)
        {
            Pruefe.Wahr(a.Bezug.Length > 0 && a.Text.Length > 20, "Annahme mit Bezug und Text: " + a.Bezug);
            Pruefe.Falsch(a.Text.Any(ch => ch is (char)0x2013 or (char)0x2014 or (char)0x2212 or (char)0x2026 or (char)0x2022), "nur - und _ als Striche: " + a.Bezug);
        }
    }

    [Test]
    public static void StarCitizen_WeichtVorhandenenIdsAus()
    {
        // 101 ist belegt; ein Netz aus einem alten Stand liegt im Bereich der Kanal-IDs von 102 (0x8000 + 102 x 32 + 1).
        var imBereich = Kanalrechnung.Id(102, 1);
        var s = LeereFlotte(b => b.MitNetz(new L.Netz(90, "Echo") { Offen = L.Offenheit.HoerenSenden }).MitNetz(new L.Netz(101, "Belegt"))
            .MitNetz(new L.Netz(imBereich, "Alt")));
        var bestand = Abbildung.NetzListe(s.Aktuell);
        var vorlage = Flottenvorlagen.StarCitizen(bestand);
        var (schritte, _) = Zentrale.PaketLesen(vorlage.Paket);
        var neueNetze = schritte!.OfType<L.Verwaltungsschritt.NetzSetzen>().Select(n => n.Netz.Id).ToList();
        Pruefe.Wahr(!neueNetze.Contains((ushort)90) && !neueNetze.Contains((ushort)101) && !neueNetze.Contains((ushort)102) && !neueNetze.Contains((ushort)1),
            "keine vorhandene Netz-ID und keine, in deren Kanalbereich ein Netz liegt: " + string.Join(",", neueNetze));
        Pruefe.Gleich("100,103,104,105,106,107,108,109,110,111", string.Join(",", neueNetze), "die nächsten freien ab 100");
        var r = s.Ausfuehren(L.Auftrag.Paket(Admin, schritte!, s.Aktuell.NetzListeVersion), 1);
        Pruefe.Wahr(r.Erfolg && s.Aktuell.Netze[90].Name == "Echo" && s.Aktuell.Netze[101].Name == "Belegt", "Echo und das belegte Netz bleiben: " + r);
        Pruefe.Gleich(14, s.Aktuell.Netze.Count, "zehn neue, Global und die drei vorhandenen");
    }

    [Test]
    public static void Auftragszeit_BisVierUhr_Lesen_Sommerzeit()
    {
        var zone = Auftragszeit.Zone;
        long Ms(int j, int m, int t, int h, int min) => new DateTimeOffset(new DateTime(j, m, t, h, min, 0), zone.GetUtcOffset(new DateTime(j, m, t, h, min, 0))).ToUnixTimeMilliseconds();
        string Lokal(long ms) => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), zone).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        Pruefe.Gleich("2026-10-03 04:00", Lokal(Auftragszeit.NaechsterMorgen(Ms(2026, 10, 2, 19, 0))), "Abend bis zum nächsten Morgen");
        Pruefe.Gleich("2026-10-03 04:00", Lokal(Auftragszeit.NaechsterMorgen(Ms(2026, 10, 3, 3, 0))), "nach Mitternacht bis 04:00 desselben Tages");
        Pruefe.Gleich("2026-10-04 04:00", Lokal(Auftragszeit.NaechsterMorgen(Ms(2026, 10, 3, 4, 0))), "genau 04:00: der nächste");
        // Ende der Sommerzeit am 25.10.2026: die Nacht hat eine Stunde mehr.
        var nacht = Auftragszeit.NaechsterMorgen(Ms(2026, 10, 24, 20, 0));
        Pruefe.Gleich("2026-10-25 04:00", Lokal(nacht), "Zeitumstellung");
        Pruefe.Gleich(9L * 3600_000, nacht - Ms(2026, 10, 24, 20, 0), "neun Stunden in der langen Nacht");

        var nach = Ms(2026, 10, 2, 18, 0);
        Pruefe.Gleich("2026-10-02 19:30", Lokal(Auftragszeit.Lesen("19:30", nach)!.Value), "HH:MM heute");
        Pruefe.Gleich("2026-10-03 17:00", Lokal(Auftragszeit.Lesen("17:00", nach)!.Value), "HH:MM schon vorbei: morgen");
        Pruefe.Gleich("2026-10-10 19:00", Lokal(Auftragszeit.Lesen("2026-10-10 19:00", nach)!.Value), "Datum und Uhrzeit");
        Pruefe.Gleich("2026-10-10 19:00", Lokal(Auftragszeit.Lesen("2026-10-10T19:00", nach)!.Value), "mit T");
        Pruefe.Gleich(null, Auftragszeit.Lesen("morgen", nach), "unlesbar");
        Pruefe.Gleich("03.10. 04:00", Auftragszeit.Anzeigen(Ms(2026, 10, 3, 4, 0)), "Anzeige");
    }
}
