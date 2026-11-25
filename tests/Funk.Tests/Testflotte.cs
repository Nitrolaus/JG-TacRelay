// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Eine große Flotte mit Einheiten für die Tests des Flottenentwurfs, der Herkunft und der Konsole (seit 19.10.2026 nur noch hier, nicht mehr
/// als Vorlage im Programm): 15 Schiffe "Schiff 1" bis "Schiff 15" mit Durchsagen, Crew und Brücke als Einheitsnetzen, die Netze Kommando
/// und Reserve 2 bis Reserve 6 für die ganze Flotte und die Rolle "Kapitän" mit neun Feldern: 0 Kommando, 1 Durchsagen, 2 Crew, 3 Brücke
/// (über die Kennungen der Einheit), 4 bis 8 Reserve 2 bis 6, alle hören und senden, Sendevorschlag Kommando, je Schiff ein Platz. Gebaut wie
/// eine Vorlage (ein Verwaltungspaket mit Annahmen), damit der Entwurf sie wie jede Vorlage anwendet. IDs weichen der vorhandenen NetzListe aus.
/// </summary>
internal static class Testflotte
{
    /// <summary>Name und Herkunft der Testflotte.</summary>
    public const string Name = "Schiffsflotte";

    /// <summary>Zahl der Schiffe.</summary>
    public const int Schiffe = 15;

    public static Flottenvorlage Schiffsflotte(NetzListe? bestand = null)
    {
        var netzIds = new FreieIds(bestand?.Netze.Select(n => n.Id) ?? [Grenzen.GlobalNetzId], 100);
        var schiffIds = new FreieIds(bestand?.Schiffe.Select(s => s.Id) ?? [], 1);
        var rollenIds = new FreieIds(bestand?.Rollen.Select(r => r.Id) ?? [], 12);

        var kommando = netzIds.Naechste();
        var reserve = Enumerable.Range(2, 5).Select(n => (Nummer: n, Id: netzIds.Naechste())).ToList();
        var schiffe = Enumerable.Range(1, Schiffe)
            .Select(n => (Nummer: n, Id: schiffIds.Naechste(), Durchsagen: netzIds.Naechste(), Crew: netzIds.Naechste(), Bruecke: netzIds.Naechste()))
            .ToList();
        var kapitaen = rollenIds.Naechste();

        var ops = new List<VerwaltungsSchritt>();
        // Erst die Schiffe ohne Netze und Plätze, damit die Schiffsnetze ihr Schiff finden; zuletzt die Schiffe vollständig.
        foreach (var s in schiffe) ops.Add(Schritt(VerwaltungsOperation.SchiffSetzen, SchiffObjekt(s.Id, s.Nummer, null, null)));
        ops.Add(Schritt(VerwaltungsOperation.NetzSetzen, NetzObjekt(kommando, "Kommando", 1, null, null)));
        foreach (var a in reserve)
            ops.Add(Schritt(VerwaltungsOperation.NetzSetzen, NetzObjekt(a.Id, "Reserve " + a.Nummer, (ushort)a.Nummer, null, null)));
        foreach (var s in schiffe)
        {
            var basis = (ushort)(10 + s.Nummer * 3);
            ops.Add(Schritt(VerwaltungsOperation.NetzSetzen, NetzObjekt(s.Durchsagen, "Durchsagen " + s.Nummer, basis, s.Id, "DURCHSAGEN")));
            ops.Add(Schritt(VerwaltungsOperation.NetzSetzen, NetzObjekt(s.Crew, "Crew " + s.Nummer, (ushort)(basis + 1), s.Id, "CREW")));
            ops.Add(Schritt(VerwaltungsOperation.NetzSetzen, NetzObjekt(s.Bruecke, "Brücke " + s.Nummer, (ushort)(basis + 2), s.Id, "BRUECKE")));
        }
        RollenFeld Feld(byte feld, ushort? netz, string? kennung, bool vorschlag = false) =>
            new() { Feld = feld, NetzId = netz, Schiffsnetz = kennung, Hoeren = true, Senden = true, SendenStandard = vorschlag, Fest = false };
        var felder = new List<RollenFeld>
        {
            Feld(0, kommando, null, vorschlag: true),
            Feld(1, null, "DURCHSAGEN"),
            Feld(2, null, "CREW"),
            Feld(3, null, "BRUECKE"),
        };
        felder.AddRange(reserve.Select((a, i) => Feld((byte)(4 + i), a.Id, null)));
        ops.Add(Schritt(VerwaltungsOperation.RolleSetzen, new Rolle
        {
            Id = kapitaen,
            Name = "Kapitän",
            Reihenfolge = 12,
            SelbstWaehlbar = true,
            BrauchtSchiff = true,
            Felder = felder,
            Herkunft = Name,
        }));
        foreach (var s in schiffe)
        {
            ops.Add(Schritt(VerwaltungsOperation.SchiffSetzen, SchiffObjekt(s.Id, s.Nummer,
                new Dictionary<string, ushort>(StringComparer.Ordinal) { ["DURCHSAGEN"] = s.Durchsagen, ["CREW"] = s.Crew, ["BRUECKE"] = s.Bruecke },
                [new SchiffsPlatz { RolleId = kapitaen, Kapazitaet = 1 }])));
        }

        Vorlagenannahme[] annahmen =
        [
            new("netz:Kommando", "Kommando ist ein Netz für die ganze Flotte, nicht je Schiff."),
            new("schiffe", "Durchsagen, Crew und Brücke gibt es je Schiff, 15 Schiffe \"Schiff 1\" bis \"Schiff 15\"."),
            new("rolle:Kapitän", "Der Kapitän hat je Schiff genau einen Platz."),
            new("felder", "Alle neun Felder hören und senden, Sendevorschlag ist Kommando."),
            new("ausloeser", "Alle Netze mit Sprachaktivierung als Voreinstellung, auch Kommando (Q53, 08.10.2026)."),
            new("fehlt", "Keine Trupps, keine Orga-Einheit und keine weiteren Rollen; die kommen mit den Bausteinen oder von Hand."),
        ];
        return new Flottenvorlage(Name, "Testflotte mit Einheiten (Funk.Tests)", new VerwaltungsPaket { Ops = ops }, annahmen);
    }

    /// <summary>
    /// Legt die Testflotte über die Konsole an (<c>netz-setzen</c>, <c>schiff-setzen</c>, <c>rolle-setzen</c> mit <c>objekt</c>, Schritt für
    /// Schritt in der Folge des Pakets), auf einem Server ohne eigene Flotte; bis 19.10.2026 tat das <c>flotte-vorlage</c> mit einer
    /// eingebauten Vorlage.
    /// </summary>
    public static async Task AnDieKonsoleAsync(Testserver t)
    {
        ArgumentNullException.ThrowIfNull(t);
        foreach (var s in Schiffsflotte().Paket.Ops)
        {
            var befehl = s.Op switch
            {
                VerwaltungsOperation.NetzSetzen => "netz-setzen",
                VerwaltungsOperation.SchiffSetzen => "schiff-setzen",
                VerwaltungsOperation.RolleSetzen => "rolle-setzen",
                _ => throw new InvalidOperationException("unerwarteter Schritt " + s.Op),
            };
            await t.AdminOk(befehl, new System.Text.Json.Nodes.JsonObject { ["objekt"] = System.Text.Json.Nodes.JsonNode.Parse(s.Objekt.GetRawText()) });
        }
    }

    private static VerwaltungsSchritt Schritt<T>(VerwaltungsOperation op, T objekt) => new() { Op = op, Objekt = SteuerJson.Element(objekt) };

    private static Netz NetzObjekt(ushort id, string name, ushort reihenfolge, ushort? schiff, string? kennung) => new()
    {
        Id = id,
        Name = name,
        Typ = NetzTyp.Normal,
        Reihenfolge = reihenfolge,
        NurTaste = false,
        AusloeserStandard = Ausloeser.Va,
        Offen = NetzOffen.Nein,
        Standard = false,
        SchiffId = schiff,
        Schiffsnetz = kennung,
        Herkunft = Name,
    };

    private static Schiff SchiffObjekt(ushort id, int nummer, IReadOnlyDictionary<string, ushort>? netze, IReadOnlyList<SchiffsPlatz>? plaetze) => new()
    {
        Id = id,
        Name = "Schiff " + nummer,
        Reihenfolge = (ushort)nummer,
        Netze = netze ?? new Dictionary<string, ushort>(StringComparer.Ordinal),
        Plaetze = plaetze ?? [],
        Art = Einheitsart.Schiff,
    };

    /// <summary>Vergibt aufsteigend IDs ab einem Startwert, die in der vorhandenen Liste nicht vorkommen.</summary>
    private sealed class FreieIds(IEnumerable<ushort> belegt, ushort start)
    {
        private readonly HashSet<ushort> _belegt = [.. belegt];
        private ushort _naechste = start;

        public ushort Naechste()
        {
            while (_belegt.Contains(_naechste))
            {
                if (_naechste == ushort.MaxValue) throw new InvalidOperationException("Keine freie ID mehr.");
                _naechste++;
            }
            _belegt.Add(_naechste);
            return _naechste++;
        }
    }
}
