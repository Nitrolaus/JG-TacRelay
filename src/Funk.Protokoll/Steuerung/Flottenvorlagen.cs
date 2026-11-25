// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

/// <summary>Eine Annahme einer Vorlage, die der Admin prüfen soll (auf der Flottenseite gelb markiert).</summary>
/// <param name="Bezug">Worauf sie sich bezieht, etwa <c>ch:alle</c> oder <c>offen</c>.</param>
public sealed record Vorlagenannahme(string Bezug, string Text);

/// <summary>Eine fertige Flotte zum Übernehmen: ein Verwaltungspaket (N5) und die Annahmen, die darin stecken.</summary>
public sealed record Flottenvorlage(string Name, string Quelle, VerwaltungsPaket Paket, IReadOnlyList<Vorlagenannahme> Annahmen);

/// <summary>Wie ein Rollenfeld der Vorlage zu seinem Ch. kommt.</summary>
public enum VorlagenCh : byte
{
    /// <summary>Kanal ohne Nummer (etwa Kommando, Flotte): Das Feld beginnt fest auf dem ersten Ch. der Liste, der Funker wechselt selbst.</summary>
    Erster,

    /// <summary>"Schiff n", "Staffel n", "Trupp n": kein fester Ch.; den Ch. wählt der Funker oder die Leitung (6.16, Ch.-Auflösung).</summary>
    Waehlbar,

    /// <summary>"alle": fest der gemeinsame Ch. <see cref="Flottenvorlagen.AlleCh"/> am Ende der Liste.</summary>
    Alle,
}

/// <summary>Ein Rollenfeld der Vorlage: der Kanal und wie das Feld seinen Ch. bekommt.</summary>
public sealed record Vorlagenfeld(string Kanal, VorlagenCh Ch);

/// <summary>
/// Vorlagen für die Flotte (A40, Flotten-Einstellungsseite). Die Flotte ist ein Datenmodell, das ein Admin pflegt; die Vorlage ist nur der
/// Anfang, die Gruppe legt sie danach selbst fest. Server (Konsole <c>flotte-vorlage --name starcitizen</c>) und Verwaltungsteil bauen
/// dasselbe Paket. Seit 0.8.0 tragen die Kanäle und Rollen einer Vorlage ihren Namen als Herkunft (Herkunftsschild in der Verwaltung);
/// Global bleibt, wie es ist.
/// </summary>
public static class Flottenvorlagen
{
    /// <summary>Name und Herkunft der Vorlage "Star Citizen" (Sarevoc, 19.10.2026, Q13, Q18, Q26).</summary>
    public const string StarCitizenName = "Star Citizen";

    /// <summary>Der gemeinsame Ch. eines Kanals für "alle" (Brücke, Jäger, Bodentrupp), am Ende der Liste.</summary>
    public const string AlleCh = "Alle";

    private const string Bergung = "Bergung und Bergbau";

    private static string[] Liste(params IEnumerable<string>[] teile) => ["Ruhe", .. teile.SelectMany(t => t)];

    private static IEnumerable<string> Zaehlen(string wort, int bis) =>
        Enumerable.Range(1, bis).Select(i => wort + " " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Die zehn Kanäle der Vorlage mit ihrer Nummer und Ch.-Liste (Index 0 Ruhe). Brücke und Crew haben Schiff 1 bis 12, Jäger Staffel 1 bis 4,
    /// Bodentrupp Trupp 1 bis 4; Brücke, Jäger und Bodentrupp dazu den Ch. <see cref="AlleCh"/> am Ende, damit Ch. n weiter Schiff n heißt.
    /// </summary>
    public static IReadOnlyList<(int Nr, string Name, IReadOnlyList<string> Kanaele)> StarCitizenKanaele { get; } =
    [
        (1, "Kommando", Liste(["Flottenführung", "Stab"])),
        (2, "Flotte", Liste(["Allgemein"])),
        (3, "Brücke", Liste(Zaehlen("Schiff", 12), [AlleCh])),
        (4, "Crew", Liste(Zaehlen("Schiff", 12))),
        (5, "Jäger", Liste(Zaehlen("Staffel", 4), [AlleCh])),
        (6, "Bodentrupp", Liste(Zaehlen("Trupp", 4), [AlleCh])),
        (7, "Logistik", Liste(["Fracht", "Betankung", "Reparatur"])),
        (8, Bergung, Liste(["Bergung", "Bergbau"])),
        (9, "Sanität", Liste(["Rettung"])),
        (10, "Allgemein", Liste(["Treffpunkt"], Zaehlen("Reserve", 3))),
    ];

    private static Vorlagenfeld E(string kanal) => new(kanal, VorlagenCh.Erster);

    private static Vorlagenfeld N(string kanal) => new(kanal, VorlagenCh.Waehlbar);

    private static Vorlagenfeld A(string kanal) => new(kanal, VorlagenCh.Alle);

    /// <summary>Die 14 Rollen der Vorlage in ihrer Reihenfolge; das erste Feld ist das Sendeziel ab Werk.</summary>
    public static IReadOnlyList<(string Name, IReadOnlyList<Vorlagenfeld> Felder)> StarCitizenRollen { get; } =
    [
        ("Standard (Gast)", [E("Allgemein"), E("Flotte")]),
        ("Flottenführung", [E("Kommando"), E("Flotte"), A("Brücke"), A("Jäger"), A("Bodentrupp"), E("Logistik"), E("Sanität"), E(Bergung), E("Allgemein")]),
        ("Kapitän", [N("Brücke"), N("Crew"), E("Flotte"), E("Kommando"), E("Sanität"), E("Logistik")]),
        ("Pilot", [N("Brücke"), E("Flotte"), N("Crew")]),
        ("Gunner", [N("Crew"), N("Brücke")]),
        ("Ingenieur", [N("Crew"), N("Brücke"), E("Logistik")]),
        ("Staffelführer", [N("Jäger"), A("Jäger"), E("Flotte"), E("Kommando")]),
        ("Jägerpilot", [N("Jäger"), E("Flotte")]),
        ("Truppführer", [N("Bodentrupp"), A("Bodentrupp"), E("Kommando"), E("Sanität"), E("Flotte")]),
        ("Bodentrupp", [N("Bodentrupp"), E("Sanität")]),
        ("Sanitäter", [E("Sanität"), A("Bodentrupp"), E("Flotte"), N("Crew")]),
        ("Logistik", [E("Logistik"), E("Flotte"), E(Bergung)]),
        (Bergung, [E(Bergung), E("Logistik"), E("Flotte")]),
        ("Funker (Leitstand)", [E("Kommando"), E("Flotte"), A("Jäger"), A("Bodentrupp"), E("Logistik"), E("Sanität"), E("Allgemein")]),
    ];

    /// <summary>Der Ch., auf dem ein Rollenfeld der Vorlage fest beginnt, oder <c>null</c> (Ch. wählt der Funker oder die Leitung).</summary>
    public static byte? ChVon(Vorlagenfeld feld, IReadOnlyList<string> kanaele)
    {
        ArgumentNullException.ThrowIfNull(feld);
        ArgumentNullException.ThrowIfNull(kanaele);
        return feld.Ch switch
        {
            VorlagenCh.Erster => 1,
            VorlagenCh.Alle => (byte)IndexVon(kanaele, AlleCh),
            _ => null,
        };
    }

    private static int IndexVon(IReadOnlyList<string> liste, string name)
    {
        for (var i = 0; i < liste.Count; i++)
            if (liste[i] == name) return i;
        throw new InvalidOperationException("Ch. " + name + " fehlt in der Liste.");
    }

    /// <summary>
    /// Die Vorlage "Star Citizen" (Sarevoc, 19.10.2026, Q26, "so übernehmen"): zehn Kanäle mit Ch. (6.16), alle offen zum Hören und Senden, mit
    /// Sprachaktivierung, ohne Gruppe; 14 Rollen mit ihren Feldern 1 bis 9 (im Protokoll Feld 0 bis 8), alle hören und senden, Sendevorschlag
    /// das erste Feld, selbst wählbar, ohne Einheit und ohne Kapazität. So ist die Vorgabe abgebildet:
    /// <list type="bullet">
    /// <item>"Schiff n", "Staffel n", "Trupp n": Das Rollenfeld nennt den Kanal ohne festen Ch.; den Ch. wählt der Funker selbst (Ch.-Wähler)
    /// oder die Leitung (Brett, Ch. im Feld der Person). Die Ch.-Auflösung behält einen Ch., den das Konto im Kanal schon hat, sonst Ruhe.</item>
    /// <item>"alle": der gemeinsame Ch. <see cref="AlleCh"/> am Ende der Liste von Brücke, Jäger und Bodentrupp, fest im Rollenfeld. Ein Hören
    /// über alle Ch. eines Kanals gibt es im Protokoll nicht (die Netz-ID eines Kanalnetzes als Ziel erreicht niemanden). Staffelführer und
    /// Truppführer haben ihren Kanal zweimal, frei und fest auf Alle; das erlaubt der Flottenentwurf seit 19.10.2026, und ein freies Feld
    /// nimmt in der Ch.-Auflösung nie den Ch., den ein anderes Feld der Rolle fest nennt.</item>
    /// <item>Kanäle ohne Nummer: fest der erste Ch. (Flottenführung, Allgemein, Fracht, Bergung, Rettung, Treffpunkt); wechseln darf jeder.</item>
    /// </list>
    /// Global (das feste Feld) und die Durchsagen bleiben eigene Wege; die Vorlage ändert Global nicht. Keine Einheiten. Legt nur dazu (Netz-IDs
    /// frei ab 100, unter 1023, ohne fremdes Netz im Bereich ihrer Kanal-IDs; Rollen-IDs frei ab 101); die alte Flotte bleibt, bis der Admin sie
    /// löscht. 24 Schritte. Danach gilt das Versionstor 0.6.0 (4.7).
    /// </summary>
    public static Flottenvorlage StarCitizen(NetzListe? bestand = null)
    {
        var vorhanden = bestand?.Netze.Select(n => n.Id).ToList() ?? [Grenzen.GlobalNetzId];
        // Eine Kanalnetz-ID n belegt die Kanal-IDs 0x8000 + n x 32 bis + 31; dort darf kein Netz eines älteren Stands liegen.
        bool KanalbereichFrei(ushort n) => n <= Kanalrechnung.KanalnetzIdMax
            && !vorhanden.Any(id => id >= Kanalrechnung.Id(n, 0) && id < Kanalrechnung.Id(n, 0) + Kanalrechnung.ChJeNetz);
        var netzIds = new FreieIds(vorhanden, 100);
        var rollenIds = new FreieIds(bestand?.Rollen.Select(r => r.Id) ?? [], 101);

        var ops = new List<VerwaltungsSchritt>();
        var kanaele = new Dictionary<string, (ushort Id, IReadOnlyList<string> Liste)>(StringComparer.Ordinal);
        for (var i = 0; i < StarCitizenKanaele.Count; i++)
        {
            var k = StarCitizenKanaele[i];
            var id = netzIds.Naechste(KanalbereichFrei);
            kanaele[k.Name] = (id, k.Kanaele);
            ops.Add(Schritt(VerwaltungsOperation.NetzSetzen, new Netz
            {
                Id = id,
                Name = k.Name,
                Typ = NetzTyp.Normal,
                Reihenfolge = (ushort)(100 + i),
                NurTaste = false,
                AusloeserStandard = Ausloeser.Va,
                Offen = NetzOffen.HoerenSenden,
                Standard = false,
                Kanaele = k.Kanaele,
                Herkunft = StarCitizenName,
            }));
        }
        for (var r = 0; r < StarCitizenRollen.Count; r++)
        {
            var (name, felder) = StarCitizenRollen[r];
            ops.Add(Schritt(VerwaltungsOperation.RolleSetzen, new Rolle
            {
                Id = rollenIds.Naechste(),
                Name = name,
                Reihenfolge = (ushort)(r + 1),
                SelbstWaehlbar = true,
                BrauchtSchiff = false,
                Felder = [.. felder.Select((f, i) => new RollenFeld
                {
                    Feld = (byte)i,
                    NetzId = kanaele[f.Kanal].Id,
                    Hoeren = true,
                    Senden = true,
                    SendenStandard = i == 0,
                    Fest = false,
                    Ch = ChVon(f, kanaele[f.Kanal].Liste),
                })],
                Herkunft = StarCitizenName,
            }));
        }

        Vorlagenannahme[] annahmen =
        [
            new("ch:n", "Schiff n, Staffel n und Trupp n: Das Feld hat keinen festen Ch., den wählt der Funker selbst oder die Leitung auf dem Brett. "
                + "Ab Werk steht es auf Ruhe; wer im Kanal schon einen Ch. hat, behält ihn."),
            new("ch:alle", "\"alle\" ist der gemeinsame Ch. Alle am Ende von Brücke, Jäger und Bodentrupp. Wer nur auf Schiff 3 steht, hört Alle nicht mit; "
                + "ein Hören über alle Ch. eines Kanals gibt es nicht. Staffelführer und Truppführer haben ihren Kanal zweimal: frei und fest auf Alle."),
            new("ch:erster", "Kanäle ohne Nummer beginnen auf ihrem ersten Ch.: Kommando auf Flottenführung, Flotte auf Allgemein, Logistik auf Fracht, "
                + "Bergung und Bergbau auf Bergung, Sanität auf Rettung, Allgemein auf Treffpunkt. Wechseln darf jeder."),
            new("offen", "Alle Kanäle offen zum Hören und Senden, Sprachaktivierung ab Werk, alle Rollen selbst wählbar. Wer Kommando nur der Führung "
                + "lassen will, stellt den Kanal auf nicht offen und Flottenführung und Funker (Leitstand) auf nicht selbst wählbar."),
            new("global", "Global (das feste Feld) und die Durchsagen bleiben eigene Wege; die Vorlage ändert Global nicht."),
            new("einheiten", "Keine Einheiten: Schiffe, Staffeln und Trupps sind Ch. Wer Plätze auf dem Brett braucht, legt Einheiten an und gibt ihnen "
                + "eine Kennung auf den Ch. (etwa Brücke, Schiff 3); dann folgt der Ch. dem Platz."),
        ];
        return new Flottenvorlage(StarCitizenName, "Kanalvorlage Star Citizen (19.10.2026): 10 Kanäle mit Ch., 14 Rollen",
            new VerwaltungsPaket { Ops = ops }, annahmen);
    }

    private static VerwaltungsSchritt Schritt<T>(VerwaltungsOperation op, T objekt) => new() { Op = op, Objekt = SteuerJson.Element(objekt) };

    /// <summary>Vergibt aufsteigend IDs ab einem Startwert, die in der vorhandenen Liste nicht vorkommen.</summary>
    private sealed class FreieIds(IEnumerable<ushort> belegt, ushort start)
    {
        private readonly HashSet<ushort> _belegt = [.. belegt];
        private ushort _naechste = start;

        public ushort Naechste(Func<ushort, bool>? passt = null)
        {
            while (_belegt.Contains(_naechste) || (passt is not null && !passt(_naechste)))
            {
                if (_naechste == ushort.MaxValue) throw new InvalidOperationException("Keine freie ID mehr.");
                _naechste++;
            }
            _belegt.Add(_naechste);
            return _naechste++;
        }
    }
}
