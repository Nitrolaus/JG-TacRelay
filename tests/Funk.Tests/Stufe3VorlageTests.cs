// JG TacRelay - Maintainer: Sarevoc
using System.Text.RegularExpressions;
using Funk.Protokoll.Steuerung;
using L = Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Vorlage "Star Citizen" (Sarevoc, 19.10.2026, Q26): gegen die Vorgabe unter Daten\starcitizen\VORLAGE.md, die zehn Kanäle mit ihren Ch. und die
/// 14 Rollen mit ihren Feldern 1 bis 9 (erstes Sendeziel). So ist sie abgebildet: "Schiff n", "Staffel n", "Trupp n" ohne festen Ch. (den wählt
/// der Funker oder die Leitung), "alle" fest auf den gemeinsamen Ch. Alle am Ende der Liste, Kanäle ohne Nummer fest auf ihrem ersten Ch.
/// Dazu die festen Regeln (alle offen, Sprachaktivierung, selbst wählbar, keine Einheiten, Global bleibt), 24 Schritte, und das Paket am Server
/// neben einer alten Flotte mit RolleWaehlen.
/// </summary>
internal static class Stufe3VorlageTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

    private static string Vorgabe() =>
        File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "tests", "Funk.Tests", "Daten", "starcitizen", "VORLAGE.md")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static (List<Netz> Netze, List<Rolle> Rollen) Lesen(Flottenvorlage v)
    {
        var netze = new List<Netz>();
        var rollen = new List<Rolle>();
        foreach (var s in v.Paket.Ops)
        {
            if (s.Op == VerwaltungsOperation.NetzSetzen) netze.Add((Netz)s.ObjektLesen<Netz>().Body!);
            else if (s.Op == VerwaltungsOperation.RolleSetzen) rollen.Add((Rolle)s.ObjektLesen<Rolle>().Body!);
            else throw new PruefungException("unerwarteter Schritt " + s.Op);
        }
        return (netze, rollen);
    }

    /// <summary>"Schiff 1-12" wird zu Schiff 1 bis Schiff 12, alles andere bleibt, wie es dasteht.</summary>
    private static IEnumerable<string> Aufklappen(string teil)
    {
        var m = Regex.Match(teil, @"^(.*) (\d+)-(\d+)$", RegexOptions.None, Frist);
        if (!m.Success)
        {
            yield return teil;
            yield break;
        }
        var von = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var bis = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        for (var i = von; i <= bis; i++) yield return m.Groups[1].Value + " " + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Die Kanäle der Vorgabe: Nummer, Name und die Ch. nach der Ruhe, wie sie in der Klammer stehen.</summary>
    private static List<(int Nr, string Name, List<string> Ch)> KanaeleDerVorgabe()
    {
        var text = Vorgabe();
        var absatz = text[text.IndexOf("Kanäle (Ch.):", StringComparison.Ordinal)..text.IndexOf("Global (Feld 0)", StringComparison.Ordinal)];
        return [.. Regex.Matches(absatz, @"(\d+) ([^()\d-][^()]*?) \(([^)]*)\)", RegexOptions.None, Frist).Select(m => (
            int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            m.Groups[2].Value.Trim(),
            m.Groups[3].Value.Split(", ").SelectMany(Aufklappen).ToList()))];
    }

    /// <summary>Die Rollen der Vorgabe mit ihren Feldern: Kanal und "n" (Ch. wählt Funker oder Leitung), "alle" oder nichts.</summary>
    private static List<(string Name, List<(string Kanal, string? Zusatz)> Felder)> RollenDerVorgabe(IReadOnlyList<string> kanaele)
    {
        var text = Vorgabe();
        var absatz = text[text.IndexOf("Rollen (Felder 1-9", StringComparison.Ordinal)..];
        var rollen = new List<(string, List<(string, string?)>)>();
        foreach (Match m in Regex.Matches(absatz, @"^- (.+?): (.+)$", RegexOptions.Multiline, Frist))
        {
            var felder = new List<(string, string?)>();
            foreach (var feld in m.Groups[2].Value.Split(", "))
            {
                var k = Regex.Match(feld.Trim(), @"^(.+?)(?: \((.+)\))?$", RegexOptions.None, Frist);
                var wort = k.Groups[1].Value;
                // "Bergung" in den Rollen ist der Kanal 8 "Bergung und Bergbau": der eine Kanal, der so heißt oder mit dem Wort beginnt.
                var kanal = kanaele.Contains(wort) ? wort : kanaele.Single(n => n.StartsWith(wort + " ", StringComparison.Ordinal));
                var zusatz = k.Groups[2].Success ? (k.Groups[2].Value == "alle" ? "alle" : k.Groups[2].Value.EndsWith(" n", StringComparison.Ordinal) ? "n" : k.Groups[2].Value) : null;
                felder.Add((kanal, zusatz));
            }
            rollen.Add((m.Groups[1].Value.Trim(), felder));
        }
        return rollen;
    }

    [Test]
    public static void StarCitizen_KanaeleUndRollenWieDieVorgabe()
    {
        var v = Flottenvorlagen.StarCitizen();
        var (netze, rollen) = Lesen(v);
        var vorgabeKanaele = KanaeleDerVorgabe();
        Pruefe.Gleich(10, vorgabeKanaele.Count, "zehn Kanäle in der Vorgabe");
        var vorgabeRollen = RollenDerVorgabe([.. vorgabeKanaele.Select(k => k.Name)]);
        Pruefe.Gleich(14, vorgabeRollen.Count, "14 Rollen in der Vorgabe");
        // Wo eine Rolle "alle" nennt, hat der Kanal den gemeinsamen Ch. Alle am Ende der Liste.
        var mitAlle = vorgabeRollen.SelectMany(r => r.Felder).Where(f => f.Zusatz == "alle").Select(f => f.Kanal).ToHashSet();
        Pruefe.Gleich("Brücke, Jäger, Bodentrupp", string.Join(", ", vorgabeKanaele.Select(k => k.Name).Where(mitAlle.Contains)), "Kanäle mit \"alle\"");

        Pruefe.Gleich(10, netze.Count, "zehn Kanäle, kein Global");
        for (var i = 0; i < vorgabeKanaele.Count; i++)
        {
            var (nr, name, ch) = vorgabeKanaele[i];
            var netz = netze[i];
            Pruefe.Gleich(name, netz.Name, "Kanal " + nr + " in der Reihenfolge der Vorgabe");
            List<string> soll = ["Ruhe", .. ch, .. mitAlle.Contains(name) ? [Flottenvorlagen.AlleCh] : Array.Empty<string>()];
            Pruefe.Gleich(string.Join(", ", soll), string.Join(", ", netz.Kanaele!), name + ": Ch.-Liste");
        }

        Pruefe.Gleich(14, rollen.Count, "14 Rollen");
        var namen = netze.ToDictionary(n => n.Id);
        for (var i = 0; i < vorgabeRollen.Count; i++)
        {
            var (name, felder) = vorgabeRollen[i];
            var rolle = rollen[i];
            Pruefe.Gleich(name, rolle.Name, "Rolle " + (i + 1) + " in der Reihenfolge der Vorgabe");
            Pruefe.Gleich(i + 1, (int)rolle.Reihenfolge, name + ": Reihenfolge");
            Pruefe.Wahr(felder.Count <= 9 && rolle.Felder.Select(f => (int)f.Feld).SequenceEqual(Enumerable.Range(0, felder.Count)), name + ": Felder 1 bis " + felder.Count);
            for (var f = 0; f < felder.Count; f++)
            {
                var rf = rolle.Felder[f];
                var netz = namen[rf.NetzId!.Value];
                var (kanal, zusatz) = felder[f];
                Pruefe.Gleich(kanal, netz.Name, $"{name}, Feld {f + 1}: Kanal");
                byte? sollCh = zusatz switch
                {
                    "n" => null,
                    "alle" => (byte)netz.Kanaele!.ToList().IndexOf(Flottenvorlagen.AlleCh),
                    null => 1,
                    _ => throw new PruefungException("unbekannter Zusatz " + zusatz),
                };
                Pruefe.Gleich(sollCh, rf.Ch, $"{name}, Feld {f + 1} ({kanal} {zusatz}): Ch.");
            }
            Pruefe.Wahr(rolle.Felder.All(x => x.Hoeren && x.Senden && !x.Fest && x.SendenStandard == (x.Feld == 0) && x.Schiffsnetz is null),
                name + ": hören und senden, nicht fest, Sendeziel ab Werk das erste Feld");
            Pruefe.Wahr(rolle is { SelbstWaehlbar: true, BrauchtSchiff: false, Kapazitaet: null, Herkunft: Flottenvorlagen.StarCitizenName },
                name + ": selbst wählbar, ohne Einheit, ohne Grenze, Herkunft Star Citizen");
        }
        Pruefe.Wahr(v.Paket.Ops.All(o => o.Op is VerwaltungsOperation.NetzSetzen or VerwaltungsOperation.RolleSetzen), "keine Einheiten");
        Pruefe.Gleich(24, v.Paket.Ops.Count, "24 Schritte: 10 Kanäle, 14 Rollen");
        Pruefe.Gleich(Flottenvorlagen.StarCitizenName, v.Name, "Name der Vorlage");
        Pruefe.Gleich("Star Citizen", v.Name, "so heißt sie");
    }

    [Test]
    public static void StarCitizen_KanaeleOffenMitSprachaktivierung_IdsAb100_Annahmen()
    {
        var v = Flottenvorlagen.StarCitizen();
        var (netze, _) = Lesen(v);
        Pruefe.Falsch(netze.Any(n => n.Id == Grenzen.GlobalNetzId), "Global bleibt, wie es ist");
        Pruefe.Wahr(netze.All(n => n.Id is >= 100 and <= Kanalrechnung.KanalnetzIdMax && n.Offen == NetzOffen.HoerenSenden && n.Kanaele![0] == "Ruhe"
            && n is { NurTaste: false, AusloeserStandard: Ausloeser.Va, Standard: false, Gruppe: null, Typ: NetzTyp.Normal, Herkunft: Flottenvorlagen.StarCitizenName }),
            "IDs ab 100 unter 1023, alle offen, Sprachaktivierung, ohne Gruppe, Ch. 0 Ruhe, Herkunft Star Citizen");
        string Liste(string name) => Ecken(netze.Single(n => n.Name == name).Kanaele!);
        Pruefe.Gleich("14: Ruhe, Schiff 1, Schiff 2, Alle", Liste("Brücke"), "Brücke: Schiff n ist Ch. n, Alle am Ende");
        Pruefe.Gleich("13: Ruhe, Schiff 1, Schiff 2, Schiff 12", Liste("Crew"), "Crew ohne Alle");
        Pruefe.Gleich("6: Ruhe, Staffel 1, Staffel 2, Alle", Liste("Jäger"), "Jäger");
        Pruefe.Gleich("6: Ruhe, Trupp 1, Trupp 2, Alle", Liste("Bodentrupp"), "Bodentrupp");
        Pruefe.Gleich("5: Ruhe, Treffpunkt, Reserve 1, Reserve 3", Liste("Allgemein"), "Allgemein");
        Pruefe.Gleich("3: Ruhe, Bergung, Bergbau, Bergbau", Liste("Bergung und Bergbau"), "Bergung und Bergbau");
        Pruefe.Wahr(netze.All(n => n.Kanaele!.All(c => c.Length <= Kanalrechnung.ChNameMax)) && netze.All(n => n.Name.Length <= Grenzen.NameMax), "Längen passen");

        // Die Annahmen sagen, wie die Vorgabe abgebildet ist (gelb im Verwaltungsteil, in der Antwort der Konsole).
        Pruefe.Gleich("ch:n, ch:alle, ch:erster, offen, global, einheiten", string.Join(", ", v.Annahmen.Select(a => a.Bezug)), "Annahmen");
        var alle = string.Join(" ", v.Annahmen.Select(a => a.Text));
        foreach (var wort in new[] { "der Funker selbst oder die Leitung", "Ch. Alle am Ende", "ersten Ch.", "nicht offen", "Global", "Einheiten" })
            Pruefe.Wahr(alle.Contains(wort, StringComparison.Ordinal), "Annahme nennt: " + wort);
    }

    /// <summary>"Anzahl: erste drei, letzte" einer Ch.-Liste.</summary>
    private static string Ecken(IReadOnlyList<string> liste) =>
        liste.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + string.Join(", ", liste.Take(3)) + ", " + liste[^1];

    [Test]
    public static void StarCitizen_NebenDerAltenFlotte_AmServer_RolleWaehlen_ChBleibtBeimWechsel()
    {
        var f = new Funkprobe();
        var alt = Funk.Server.Steuer.Abbildung.NetzListe(f.Z);
        var alteRollen = f.Z.Rollen.Keys.ToHashSet();
        var vorlage = Flottenvorlagen.StarCitizen(alt);
        var (schritte, fehler) = Funk.Server.Kern.Zentrale.PaketLesen(vorlage.Paket);
        Pruefe.Gleich(null, fehler, "Paket lesbar");
        var r = f.Tun(L.Auftrag.Paket(Funkprobe.Admin, schritte!, f.Z.NetzListeVersion));
        Pruefe.Wahr(r.Erfolg, "Star Citizen übernommen: " + r);
        Pruefe.Gleich(0, f.Z.Pruefen().Count, "stimmig: " + string.Join("; ", f.Z.Pruefen()));
        Pruefe.Wahr(f.Z.Netze.ContainsKey(Funkprobe.Kommando) && f.Z.Rollen.ContainsKey(Funkprobe.RolleCo), "die alte Flotte bleibt");
        Pruefe.Gleich(10, L.Kanaele.Anzahl(f.Z.Netze), "zehn Kanalnetze, Global ohne Ch.");
        ushort Kanal(string name, byte ch) => Kanalrechnung.Id(f.Z.Netze.Values.Single(n => n.Name == name && !n.Kanaele.IsEmpty).Id, ch);
        // Die alte Flotte hat Rollen mit denselben Namen (Kapitän, Pilot); gemeint sind die der Vorlage.
        ushort RolleVon(string name) => f.Z.Rollen.Values.Single(x => x.Name == name && !alteRollen.Contains(x.Id)).Id;
        string Felder() => string.Join(" ", f.Z.Konten[Funkprobe.B].Felder.Where(m => !m.Fest).OrderBy(m => m.Feld)
            .Select(m => m.Feld + ":" + f.Z.Netze[(ushort)((m.NetzId - Kanalrechnung.Basis) / Kanalrechnung.ChJeNetz)].Name + "/" + ((m.NetzId - Kanalrechnung.Basis) % Kanalrechnung.ChJeNetz)));

        // Kapitän: Brücke und Crew ohne festen Ch. auf Ruhe, die übrigen auf ihrem ersten Ch.
        Pruefe.Wahr(f.Tun(L.Auftrag.RolleWaehlen(Funkprobe.B, RolleVon("Kapitän"))).Erfolg, "B wählt Kapitän");
        Pruefe.Gleich("0:Brücke/0 1:Crew/0 2:Flotte/1 3:Kommando/1 4:Sanität/1 5:Logistik/1", Felder(), "Kapitän ab Werk");
        Pruefe.Wahr(f.Z.Konten[Funkprobe.B].Sendeauswahl.Felder.IsEmpty, "Brücke steht auf Ruhe: noch kein Sendeziel");

        // Den Ch. wählt der Funker selbst: Brücke und Crew auf Schiff 3.
        Pruefe.Wahr(f.Tun(L.Auftrag.FeldSetzen(Funkprobe.B, 0, Kanal("Brücke", 3))).Erfolg && f.Tun(L.Auftrag.FeldSetzen(Funkprobe.B, 1, Kanal("Crew", 3))).Erfolg,
            "B stellt Schiff 3 ein");
        Pruefe.Wahr(f.Z.Konten[Funkprobe.B].Sendeauswahl.Felder.SequenceEqual([(byte)0]), "aus der Ruhe auf Schiff 3: Brücke ist Sendeziel (0.9.1)");

        // Pilot: Brücke und Crew behalten Schiff 3 (Ch.-Auflösung, der Ch., den das Konto im Kanal hat).
        Pruefe.Wahr(f.Tun(L.Auftrag.RolleWaehlen(Funkprobe.B, RolleVon("Pilot"))).Erfolg, "B wählt Pilot");
        Pruefe.Gleich("0:Brücke/3 1:Flotte/1 2:Crew/3", Felder(), "Pilot behält Schiff 3");

        // Staffelführer: Jäger (Staffel n) auf Ruhe, Jäger (alle) fest auf Alle.
        Pruefe.Wahr(f.Tun(L.Auftrag.RolleWaehlen(Funkprobe.B, RolleVon("Staffelführer"))).Erfolg, "B wählt Staffelführer");
        Pruefe.Gleich("0:Jäger/0 1:Jäger/5 2:Flotte/1 3:Kommando/1", Felder(), "Staffel n auf Ruhe, alle auf Ch. 5 (Alle)");
        Pruefe.Wahr(f.Tun(L.Auftrag.FeldSetzen(Funkprobe.B, 0, Kanal("Jäger", 2))).Erfolg, "B stellt Staffel 2 ein");

        // Kommando mit Sprachaktivierung (Q53): E hört, wenn B dort spricht.
        Pruefe.Wahr(f.Tun(L.Auftrag.RolleWaehlen(Funkprobe.E, RolleVon("Funker (Leitstand)"))).Erfolg, "E wählt Funker (Leitstand)");
        var va = f.Senden(Funkprobe.B, L.Sendeart.Netz, [Kanal("Kommando", 1)], va: true, phase: 1);
        Pruefe.Falsch(va.Verworfen, "Kommando sendet mit Sprachaktivierung: " + va.Grund);
        Pruefe.Wahr(f.M.Sprache.Any(s => s.Stempel.Empfaenger == Funkprobe.S(Funkprobe.E)), "E hört Kommando (Flottenführung)");
        f.M.Leeren();
        var alle = f.Senden(Funkprobe.B, L.Sendeart.Netz, [Kanal("Jäger", 5)], va: true, phase: 2);
        Pruefe.Falsch(alle.Verworfen, "Jäger Alle: " + alle.Grund);
        Pruefe.Wahr(f.M.Sprache.Any(s => s.Stempel.Empfaenger == Funkprobe.S(Funkprobe.E)), "der Leitstand hört Jäger (Alle)");
    }
}
