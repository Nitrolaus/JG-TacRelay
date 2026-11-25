// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using Funk.Server.Steuer;
using static Funk.Tests.Kanalprobe;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Herkunft von Netzen und Rollen (Fassung 0.8.0, Umbau der Verwaltung, FUNKPROTOKOLL 4.5 und 4.7): ein optionales Feld <c>herkunft</c>
/// bis 32 Zeichen, das die Vorlagen mitschreiben ("Star Citizen", "Schiffsflotte"). Ohne das Feld behält der Server die bisherige
/// Herkunft, damit ein Client 0.7.0 sie beim Ändern nicht löscht; gespeichert und wieder geladen bleibt sie.
/// </summary>
internal static class HerkunftTests
{
    private const uint Admin = Funkprobe.Admin;

    private static S.Netz DrahtNetz() => new()
    {
        Id = 5, Name = "Kommando", Typ = S.NetzTyp.Normal, Reihenfolge = 1, NurTaste = false, AusloeserStandard = S.Ausloeser.Va,
        Offen = S.NetzOffen.Nein, Standard = false,
    };

    private static S.Rolle DrahtRolle() => new() { Id = 12, Name = "Kapitän", Reihenfolge = 12, SelbstWaehlbar = true, BrauchtSchiff = true, Felder = [] };

    [Test]
    public static void Draht_OptionalUndHoechstens32Zeichen()
    {
        var ohne = Encoding.UTF8.GetString(S.SteuerJson.Serialisieren(DrahtNetz()));
        Pruefe.Falsch(ohne.Contains("herkunft", StringComparison.Ordinal), "ohne Herkunft steht kein Feld da: " + ohne);
        var mit = S.SteuerJson.Serialisieren(DrahtNetz() with { Herkunft = "Star Citizen" });
        Pruefe.Wahr(Encoding.UTF8.GetString(mit).Contains("\"herkunft\":\"Star Citizen\"", StringComparison.Ordinal), "mit Herkunft: " + Encoding.UTF8.GetString(mit));
        var gelesen = S.SteuerJson.Lesen(mit, S.SteuerJson.Info<S.Netz>());
        Pruefe.Wahr(gelesen.Ok && gelesen.Body is S.Netz { Herkunft: "Star Citizen" }, "Netz gelesen: " + gelesen.Fehler + " " + gelesen.Feld);
        var lang = S.SteuerJson.Lesen(S.SteuerJson.Serialisieren(DrahtNetz() with { Herkunft = new string('x', S.Grenzen.HerkunftMax + 1) }), S.SteuerJson.Info<S.Netz>());
        Pruefe.Wahr(!lang.Ok && lang.Feld == "netze.herkunft", "33 Zeichen am Netz: " + lang.Fehler + " " + lang.Feld);
        var genau = S.SteuerJson.Lesen(S.SteuerJson.Serialisieren(DrahtNetz() with { Herkunft = new string('x', S.Grenzen.HerkunftMax) }), S.SteuerJson.Info<S.Netz>());
        Pruefe.Wahr(genau.Ok, "32 Zeichen gehen");

        var rolle = S.SteuerJson.Lesen(S.SteuerJson.Serialisieren(DrahtRolle() with { Herkunft = "Schiffsflotte" }), S.SteuerJson.Info<S.Rolle>());
        Pruefe.Wahr(rolle.Ok && rolle.Body is S.Rolle { Herkunft: "Schiffsflotte" }, "Rolle gelesen");
        var rolleLang = S.SteuerJson.Lesen(S.SteuerJson.Serialisieren(DrahtRolle() with { Herkunft = new string('x', S.Grenzen.HerkunftMax + 1) }), S.SteuerJson.Info<S.Rolle>());
        Pruefe.Wahr(!rolleLang.Ok && rolleLang.Feld == "rollen.herkunft", "33 Zeichen an der Rolle: " + rolleLang.Fehler + " " + rolleLang.Feld);

        // So kommt eine Gegenstelle 0.7.0 mit dem Feld zurecht (4.7): Unbekannte Felder überliest der Leser, wie hier ein erfundenes.
        var zukunft = Encoding.UTF8.GetString(mit).Replace("\"herkunft\":", "\"zukunft\":1,\"herkunft\":", StringComparison.Ordinal);
        Pruefe.Wahr(S.SteuerJson.Lesen(Encoding.UTF8.GetBytes(zukunft), S.SteuerJson.Info<S.Netz>()).Ok, "ein unbekanntes Feld stört nicht");
    }

    [Test]
    public static void Server_OhneFeldBleibtDieHerkunft_GeprueftGespeichertGeladen()
    {
        var f = Neu();
        var command = f.Z.Netze[Kommando];
        Ok(f, L.Auftrag.NetzSetzen(Admin, command with { Herkunft = "Star Citizen" }, f.Z.NetzListeVersion), "Herkunft gesetzt");
        Ok(f, L.Auftrag.NetzSetzen(Admin, f.Z.Netze[Kommando] with { Name = "Kommando neu", Herkunft = null }, f.Z.NetzListeVersion), "umbenannt ohne Herkunft, wie ein Client 0.7.0");
        Pruefe.Wahr(f.Z.Netze[Kommando] is { Name: "Kommando neu", Herkunft: "Star Citizen" }, "die Herkunft bleibt: " + f.Z.Netze[Kommando].Herkunft);
        Ok(f, L.Auftrag.NetzSetzen(Admin, f.Z.Netze[Kommando] with { Herkunft = "von Hand" }, f.Z.NetzListeVersion), "neue Herkunft");
        Pruefe.Gleich("von Hand", f.Z.Netze[Kommando].Herkunft, "eine neue Herkunft ersetzt die alte");
        Pruefe.Gleich(L.Fehlercode.Ungueltig, Code(f, L.Auftrag.NetzSetzen(Admin, f.Z.Netze[Kommando] with { Herkunft = "  " }, f.Z.NetzListeVersion)), "nur Leerzeichen");
        Pruefe.Gleich(L.Fehlercode.Ungueltig, Code(f, L.Auftrag.NetzSetzen(Admin, f.Z.Netze[Kommando] with { Herkunft = new string('x', 33) }, f.Z.NetzListeVersion)),
            "33 Zeichen");

        var co = f.Z.Rollen[Co];
        Ok(f, L.Auftrag.RolleSetzen(Admin, co with { Herkunft = "Star Citizen" }, f.Z.NetzListeVersion), "Rolle mit Herkunft");
        Ok(f, L.Auftrag.RolleSetzen(Admin, f.Z.Rollen[Co] with { SelbstWaehlbar = false, Herkunft = null }, f.Z.NetzListeVersion), "Rolle ohne Herkunft geändert");
        Pruefe.Wahr(f.Z.Rollen[Co] is { SelbstWaehlbar: false, Herkunft: "Star Citizen" }, "die Herkunft der Rolle bleibt");
        Pruefe.Gleich(L.Fehlercode.Ungueltig, Code(f, L.Auftrag.RolleSetzen(Admin, f.Z.Rollen[Co] with { Herkunft = "" }, f.Z.NetzListeVersion)), "leere Herkunft");

        var netz = Abbildung.Netz(f.Z.Netze[Kommando]);
        Pruefe.Wahr(netz.Herkunft == "von Hand" && Abbildung.NetzLage(netz).Herkunft == "von Hand", "Abbildung des Netzes in beide Richtungen");
        var rolle = Abbildung.Rolle(f.Z.Rollen[Co]);
        Pruefe.Wahr(rolle.Herkunft == "Star Citizen" && Abbildung.RolleLage(rolle).Herkunft == "Star Citizen", "Abbildung der Rolle in beide Richtungen");
        Pruefe.Gleich(null, Abbildung.Netz(f.Z.Netze[KanalBruecke]).Herkunft, "ohne Herkunft bleibt sie leer");

        var verzeichnis = Path.Combine(Testserver.Wurzel(), "herkunft-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(verzeichnis);
        try
        {
            var ablage = new Funk.Server.Kern.Zustandsablage(verzeichnis);
            ablage.Speichern(f.Z, f.Schreiber.Neben);
            var (geladen, _) = ablage.LageLaden();
            Pruefe.Wahr(geladen.Netze[Kommando].Herkunft == "von Hand" && geladen.Rollen[Co].Herkunft == "Star Citizen" && geladen.Netze[KanalBruecke].Herkunft is null,
                "gespeichert und wieder geladen");
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Test]
    public static void Vorlagen_SchreibenIhrenNamen_GlobalBleibt()
    {
        var vorlage = S.Flottenvorlagen.StarCitizen();
        var netze = vorlage.Paket.Ops.Where(o => o.Op == S.VerwaltungsOperation.NetzSetzen).Select(o => (S.Netz)o.ObjektLesen<S.Netz>().Body!).ToList();
        var rollen = vorlage.Paket.Ops.Where(o => o.Op == S.VerwaltungsOperation.RolleSetzen).Select(o => (S.Rolle)o.ObjektLesen<S.Rolle>().Body!).ToList();
        Pruefe.Wahr(netze.All(n => n.Herkunft == S.Flottenvorlagen.StarCitizenName) && netze.Count == 10, "zehn Kanäle mit Herkunft Star Citizen");
        Pruefe.Falsch(netze.Any(n => n.Id == S.Grenzen.GlobalNetzId), "Global bleibt, wie es ist");
        Pruefe.Wahr(rollen.Count == 14 && rollen.All(r => r.Herkunft == S.Flottenvorlagen.StarCitizenName), "14 Rollen mit Herkunft Star Citizen");

        var flotte = Testflotte.Schiffsflotte();
        var alle = flotte.Paket.Ops.Where(o => o.Op is S.VerwaltungsOperation.NetzSetzen).Select(o => ((S.Netz)o.ObjektLesen<S.Netz>().Body!).Herkunft)
            .Concat(flotte.Paket.Ops.Where(o => o.Op is S.VerwaltungsOperation.RolleSetzen).Select(o => ((S.Rolle)o.ObjektLesen<S.Rolle>().Body!).Herkunft))
            .ToList();
        Pruefe.Wahr(alle.Count == 52 && alle.All(h => h == Testflotte.Name), "51 Netze und die Rolle mit Herkunft Schiffsflotte: " + alle.Count);

        // Über die Konsole übernommen steht die Herkunft im Zustand (dieselbe Abbildung wie für jedes Paket).
        var (schritte, fehler) = Funk.Server.Kern.Zentrale.PaketLesen(vorlage.Paket);
        Pruefe.Wahr(schritte is not null, "Paket lesbar: " + fehler);
        var probe = Neu();
        var leer = new L.Zustandsschreiber(new L.SchnappschussBauer().MitKonto(new L.Konto { Id = Admin, Name = "Test", Stufe = L.Stufe.Admin }).Bauen());
        var r = leer.Ausfuehren(L.Auftrag.Paket(Admin, schritte!, leer.Aktuell.NetzListeVersion), 1);
        Pruefe.Wahr(r.Erfolg, "übernommen: " + r);
        Pruefe.Wahr(leer.Aktuell.Netze.Values.Count(n => n.Herkunft == S.Flottenvorlagen.StarCitizenName) == 10
            && leer.Aktuell.Rollen.Values.All(x => x.Herkunft == S.Flottenvorlagen.StarCitizenName), "im Zustand");
        Pruefe.Wahr(probe.Z.Netze.Values.All(n => n.Herkunft is null), "Bestand vor 0.8.0 hat keine Herkunft");
    }
}
