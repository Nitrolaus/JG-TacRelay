// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Server.Kern;
using Funk.Server.Lage;
using static Funk.Tests.Kanalprobe;

namespace Funk.Tests;

/// <summary>
/// Kanalnetze verwalten (Fassung 0.6.0, Verwaltung NETZ_SETZEN und NETZ_LOESCHEN, FUNKPROTOKOLL 4.5, 6.16, P-61): anlegen mit seinen
/// Grenzen, die Liste kürzen, entfernen und löschen räumt jede betroffene Kanal-ID aus Konten, Stummschaltungen, Einheiten, Rollen und
/// Phasen; die Stimmigkeit des Zustands erkennt jede falsche Kanal-ID; gespeichert und geladen bleibt alles.
/// </summary>
internal static class Stufe3VerwaltungTests
{
    private const uint Admin = Funkprobe.Admin;

    private static Netz Kanalnetz(ushort id, int eintraege) =>
        new(id, "Kanalnetz " + id) { Kanaele = Liste("Ch", eintraege - 1), Offen = Offenheit.HoerenSenden };

    private static Fehlercode Setzen(Funkprobe f, Netz n) => f.Tun(Auftrag.NetzSetzen(Admin, n, f.Z.NetzListeVersion)).Fehler;

    [Test]
    public static void Kanalnetz_Anlegen_UndSeineGrenzen()
    {
        var f = Neu(b => b.MitNetz(new Netz(K(400, 5), "Altes Netz aus 0.5.0")));
        var v = f.Z.NetzListeVersion;
        Pruefe.Gleich(Fehlercode.Keiner, Setzen(f, Kanalnetz(200, 3) with { Gruppe = "Marine" }), "Kanalnetz 200 mit drei Einträgen");
        Pruefe.Wahr(f.Z.NetzListeVersion > v && f.Z.Netze[200] is { Gruppe: "Marine", Kanaele.Length: 3 }, "angelegt, NetzListe neu");
        Pruefe.Gleich(Fehlercode.Keiner, Setzen(f, Kanalnetz(1022, 31)), "Netz 1022 mit 31 Einträgen");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Kanalnetz(1023, 3)), "Kanalnetz über 1022");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, new Netz(0x8000, "Kanalbereich")), "gewöhnliches Netz ab 0x8000");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, new Netz(K(400, 5), "Altes Netz geändert")), "ein altes Netz im Kanalbereich lässt sich nicht ändern");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Kanalnetz(300, 1)), "nur Ruhe");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Kanalnetz(300, 32)), "32 Einträge");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Kanalnetz(300, 3) with { Kanaele = ["Ruhe", "", "Ch 2"] }), "leere Bezeichnung");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Kanalnetz(300, 3) with { Kanaele = ["Ruhe", new string('x', 21), "Ch 2"] }), "Bezeichnung über 20");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Kanalnetz(400, 3)), "in den Kanal-IDs von 400 liegt ein altes Netz");
        Pruefe.Gleich(Fehlercode.Keiner, f.Tun(Auftrag.NetzLoeschen(Admin, K(400, 5), f.Z.NetzListeVersion)).Fehler, "das alte Netz lässt sich löschen");
        Pruefe.Gleich(Fehlercode.Keiner, Setzen(f, Kanalnetz(400, 3)), "danach geht Kanalnetz 400");
        // Bis 64 Kanalnetze: die Probe hat 5 (Global, 100 bis 103), dazu 200, 1022 und 400.
        for (ushort id = 500; f.Z.Netze.Values.Count(n => !n.Kanaele.IsEmpty) < 64; id++)
            Pruefe.Gleich(Fehlercode.Keiner, Setzen(f, Kanalnetz(id, 2)), "Kanalnetz " + id);
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Kanalnetz(700, 2)), "das 65. Kanalnetz");
        Pruefe.Gleich(Fehlercode.Keiner, Setzen(f, new Netz(701, "Gewöhnlich")), "ein gewöhnliches Netz geht weiter");
        Pruefe.Gleich("", string.Join("; ", f.Z.Pruefen()), "stimmig");
    }

    /// <summary>F hält Schiff 15 und 5, ist auf Schiff 15 stumm; Schiff 4 nennt Ch. 15, eine Rolle und eine Phase legen Ch. 15 fest.</summary>
    private static Funkprobe MitCh15()
    {
        var f = Neu();
        Ok(f, Auftrag.FeldSetzen(F, 0, K(KanalBruecke, 15)), "F Schiff 15");
        Ok(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 5)), "F Schiff 5");
        Ok(f, Auftrag.FeldSetzen(F, 2, K(KanalBruecke, 0)), "F Ruhe");
        Pruefe.Wahr(f.Stumm(F, K(KanalBruecke, 15)).Erfolg, "F auf Schiff 15 stumm");
        var schiff = f.Z.Schiffe[Schiff4];
        Ok(f, Auftrag.SchiffSetzen(Admin, schiff with { Netze = schiff.Netze.SetItem("BRIDGE", K(KanalBruecke, 15)) }, f.Z.NetzListeVersion), "Schiff 4 nennt Ch. 15");
        var rolle = f.Z.Rollen[JaegerRolle];
        Ok(f, Auftrag.RolleSetzen(Admin, rolle with { Felder = rolle.Felder.SetItem(1, rolle.Felder[1] with { Ch = 15 }) }, f.Z.NetzListeVersion), "Rolle mit Ch. 15");
        Ok(f, Auftrag.PhaseSetzen(Admin, new Einsatzphase(2, "Entern", "ENTERN") { Varianten = [new PhasenVariante(Co) { Felder = [Rf(0, KanalBruecke, ch: 15)] }] }, f.Z.NetzListeVersion),
            "Phase mit Ch. 15");
        return f;
    }

    [Test]
    public static void Kanaele_Kuerzen_RaeumtDieWeggefallenenCh()
    {
        var f = MitCh15();
        var r = f.Tun(Auftrag.NetzSetzen(Admin, f.Z.Netze[KanalBruecke] with { Kanaele = Liste("Schiff", 10) }, f.Z.NetzListeVersion));
        Pruefe.Wahr(r.Erfolg && r.BetroffeneKonten.Contains(F), "gekürzt auf Schiff 10, F betroffen: " + r);
        Pruefe.Gleich("1:100/5hs 2:100/0hs", Felder(f, F), "Schiff 15 fällt weg, Schiff 5 und Ruhe bleiben");
        Pruefe.Wahr(Konto(f, F).Stumm.IsEmpty, "die Stummschaltung auf Schiff 15 fällt weg");
        Pruefe.Falsch(f.Z.Schiffe[Schiff4].Netze.ContainsKey("BRIDGE"), "die Kennung auf Schiff 15 fällt weg");
        Pruefe.Gleich((byte?)null, f.Z.Rollen[JaegerRolle].Felder[1].Ch, "der feste Ch. 15 der Rolle verfällt");
        Pruefe.Gleich((byte?)null, f.Z.Phasen[2].Varianten[0].Felder[0].Ch, "der feste Ch. 15 der Phase verfällt");
        Pruefe.Gleich("", string.Join("; ", f.Z.Pruefen()), "stimmig");
    }

    [Test]
    public static void Kanaele_Entfernen_UndKanalnetzLoeschen_RaeumenJedeKanalId()
    {
        var f = MitCh15();
        Ok(f, Auftrag.NetzSetzen(Admin, f.Z.Netze[KanalBruecke] with { Kanaele = [] }, f.Z.NetzListeVersion), "Brücke wird ein gewöhnliches Netz");
        Pruefe.Gleich("", Felder(f, F), "alle Kanal-IDs von 100 fort, auch die Ruhe");
        Pruefe.Wahr(f.Z.Rollen[Co].Felder[1] is { NetzId: KanalBruecke, Ch: null }, "das Rollenfeld nennt jetzt das gewöhnliche Netz 100");
        Pruefe.Gleich("", string.Join("; ", f.Z.Pruefen()), "stimmig nach dem Entfernen");

        var g = MitCh15();
        Ok(g, Auftrag.NetzLoeschen(Admin, KanalBruecke, g.Z.NetzListeVersion), "Brücke gelöscht");
        Pruefe.Gleich("", Felder(g, F), "keine Kanal-ID von 100 mehr");
        Pruefe.Wahr(Konto(g, F).Stumm.IsEmpty && !g.Z.Schiffe[Schiff4].Netze.ContainsKey("BRIDGE"), "Stummschaltung und Kennung fort");
        Pruefe.Falsch(g.Z.Rollen.Values.Any(r => r.Felder.Any(x => x.NetzId == KanalBruecke)), "keine Rolle nennt 100");
        Pruefe.Gleich("", string.Join("; ", g.Z.Pruefen()), "stimmig nach dem Löschen");

        var h = Neu();
        Ok(h, Auftrag.FeldSetzen(F, 0, K(Netz.GlobalId, 3)), "F Global 2");
        Pruefe.Gleich(Fehlercode.Ungueltig, Code(h, Auftrag.NetzSetzen(Admin, h.Z.Netze[Netz.GlobalId] with { Kanaele = [] }, h.Z.NetzListeVersion)),
            "Global ohne Ch. geht nicht, solange eine Rolle einen Global-Kanal nennt");
        Ok(h, Auftrag.RolleLoeschen(Admin, JaegerRolle, h.Z.NetzListeVersion), "die Rolle mit Global 1 gelöscht");
        Ok(h, Auftrag.NetzSetzen(Admin, h.Z.Netze[Netz.GlobalId] with { Kanaele = [] }, h.Z.NetzListeVersion), "Global ohne Ch.");
        Pruefe.Wahr(Felder(h, F) == "" && h.Z.Netze[Netz.GlobalId].Typ == NetzTyp.Global, "Global-Kanäle fort, Global bleibt");
    }

    [Test]
    public static void GewoehnlichesNetz_WirdKanalnetz_FelderAufRuhe_KennungenFort()
    {
        var f = Neu();
        var crew = f.Z.Netze[Funkprobe.Crew];
        var r = f.Tun(Auftrag.NetzSetzen(Admin, crew with { Kanaele = Liste("Schiff", 20) }, f.Z.NetzListeVersion));
        Pruefe.Wahr(r.Erfolg && r.BetroffeneKonten.Contains(Funkprobe.B), "Crew wird Kanalnetz, B betroffen: " + r);
        Pruefe.Wahr(Konto(f, Funkprobe.B).FeldNummer(0) is { NetzId: var n, Hoeren: true, Senden: true } && n == K(Funkprobe.Crew, 0), "B: Crew auf Ruhe, hören und senden bleiben");
        Pruefe.Wahr(Konto(f, Funkprobe.C).FeldNummer(1) is { NetzId: var c, Hoeren: false, Senden: true } && c == K(Funkprobe.Crew, 0), "C: hören aus bleibt");
        Pruefe.Falsch(f.Z.Schiffe[Funkprobe.Schiff3].Netze.ContainsKey("CREW"), "die Kennung CREW nennt kein Kanalnetz mehr");
        Pruefe.Wahr(Konto(f, Funkprobe.B).Sendeauswahl.Felder.All(x => x != 0), "eine Ruhe sendet nicht");
        Pruefe.Gleich("", string.Join("; ", f.Z.Pruefen()), "stimmig");
    }

    [Test]
    public static void Stimmigkeit_ErkenntFalscheKanalIds()
    {
        static string Fehler(Func<SchnappschussBauer, SchnappschussBauer> mehr)
        {
            try
            {
                Neu(mehr);
                return "";
            }
            catch (InvalidOperationException e)
            {
                return e.Message;
            }
        }
        Pruefe.Gleich("", Fehler(b => b), "die Probe selbst ist stimmig");
        foreach (var (was, mehr) in new (string, Func<SchnappschussBauer, SchnappschussBauer>)[]
        {
            ("Kanalnetz-ID in einem Feld", b => b.MitKonto(new Konto { Id = 1020, Felder = [Funkprobe.F(0, KanalBruecke, true, true)] })),
            ("Gesamt in einem Feld", b => b.MitKonto(new Konto { Id = 1020, Felder = [Funkprobe.F(0, Funk.Protokoll.Steuerung.Kanalrechnung.GlobalSelbstId, true, true)] })),
            ("Ch. außerhalb der Liste", b => b.MitKonto(new Konto { Id = 1020, Felder = [Funkprobe.F(0, K(KanalBruecke, 21), true, true)] })),
            ("Kanal eines unbekannten Kanalnetzes", b => b.MitKonto(new Konto { Id = 1020, Felder = [Funkprobe.F(0, K(500, 1), true, true)] })),
            ("Rollenfeld mit Ch. außerhalb der Liste", b => b.MitRolle(new Rolle(30, "X") { Felder = [Rf(0, KanalBruecke, ch: 21)] })),
            ("Rollenfeld mit Ch. an einem gewöhnlichen Netz", b => b.MitRolle(new Rolle(30, "X") { Felder = [Rf(0, Funkprobe.Kommando, ch: 1)] })),
            ("Rollenfeld mit Gesamt", b => b.MitRolle(new Rolle(30, "X") { Felder = [Rf(0, Netz.GlobalId, ch: 1)] })),
            ("Rollenfeld mit einer Kanal-ID", b => b.MitRolle(new Rolle(30, "X") { Felder = [Rf(0, K(KanalBruecke, 3))] })),
            ("Kennung auf ein Kanalnetz", b => b.MitSchiff(new Schiff(30, "X") { Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [new KeyValuePair<string, ushort>("A", KanalBruecke)]) })),
            ("Kennung auf eine Ruhe", b => b.MitSchiff(new Schiff(30, "X") { Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [new KeyValuePair<string, ushort>("A", K(KanalBruecke, 0))]) })),
            ("Kanalnetz über 1022", b => b.MitNetz(new Netz(1023, "X") { Kanaele = Liste("Ch", 2) })),
            ("Kanalnetz mit einem Eintrag", b => b.MitNetz(new Netz(300, "X") { Kanaele = ["Ruhe"] })),
            ("altes Netz im Bereich eines Kanalnetzes", b => b.MitNetz(new Netz(K(KanalBruecke, 30), "Alt"))),
            ("gewählte Rolle unbekannt", b => b.MitKonto(new Konto { Id = 1020, GewaehlteRolle = 999 })),
            ("65 Kanalnetze", b =>
            {
                for (ushort id = 300; id < 360; id++) b.MitNetz(new Netz(id, "K" + id) { Kanaele = Liste("Ch", 2) });
                return b;
            }),
        })
        {
            Pruefe.Wahr(Fehler(mehr).Length > 0, was + ": abgelehnt");
        }
    }

    [Test]
    public static void Gespeichert_UndGeladen_BleibtAllesStimmig()
    {
        var f = MitCh15();
        Ok(f, Auftrag.RolleWaehlen(G, Co), "G wählt CO");
        // Seit 0.8.1 (Q46) ohne Wunsch: G wechselt selbst auf Schiff 9.
        Ok(f, Auftrag.FeldSetzen(G, 1, K(KanalBruecke, 9)), "G auf Schiff 9");
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "s3-lage-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            var ablage = new Zustandsablage(verzeichnis);
            ablage.Speichern(f.Z, f.Schreiber.Neben);
            var (z, _) = ablage.LageLaden();
            Pruefe.Gleich("", string.Join("; ", z.Pruefen()), "geladen stimmig");
            Pruefe.Wahr(z.Netze[KanalBruecke] is { Gruppe: "Schiffe", Kanaele.Length: 21 } && z.Netze[KanalBruecke].Kanaele[20] == "Schiff 20", "Kanalnetz mit Gruppe und Liste");
            Pruefe.Gleich((byte?)15, z.Rollen[JaegerRolle].Felder[1].Ch, "fester Ch. der Rolle");
            Pruefe.Gleich((byte?)15, z.Phasen[2].Varianten[0].Felder[0].Ch, "fester Ch. der Phase");
            Pruefe.Gleich((ushort?)Co, z.Konten[G].GewaehlteRolle, "gewählte Rolle");
            Pruefe.Gleich(f.Z.Konten[F].Felder.Length, z.Konten[F].Felder.Length, "Felder mit Kanal-IDs");
            Pruefe.Wahr(z.Konten[G].FeldMitNetz(K(KanalBruecke, 9)) is not null, "G auf Schiff 9");
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }
}
