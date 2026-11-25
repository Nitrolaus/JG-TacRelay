// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Der Beispielabend aus RAIDLEITUNG-ENTWURF Anhang A für Brett, Einsteigen und Vorführung: Nordwind ist Admin, Max Mustermann leitet im
/// Auftrag bis 04:00, Ferox ist Führung. Drei Schiffe, Trupp Alpha und Orga Logistik (seit 0.11.8 ohne Untergilden). Alle Namen sind
/// erfunden, Max Mustermann ist der Platzhalter für ein Mitglied; Netze und Rollen heißen wie in der Vorlage Star Citizen (Kommando,
/// Kapitän). Seit 0.11.7 tragen die meisten Abzeichen (Ränge und Tätigkeiten), einige mit eigener Wahl "vorn" (Nordwind, Falke, Distel).
/// </summary>
public sealed partial class FunkDienstAttrappe
{
    /// <summary>Feste Zeit des Abends, damit Bilder und Texte gleich bleiben: Freitag, 02.10.2026, 20:52 Uhr Ortszeit.</summary>
    public static DateTimeOffset Abend { get; } = new(new DateTime(2026, 10, 2, 20, 52, 0, DateTimeKind.Local));

    public const uint Nordwind = 1;
    public const uint Mustermann = 2;
    public const uint Ferox = 3;
    public const uint Kobold = 4;
    public const uint Eule = 5;
    public const uint Kranich = 6;
    public const uint Falke = 7;
    public const uint Anker = 8;
    public const uint Quarz = 9;
    public const uint Hagel = 10;
    public const uint Distel = 11;
    public const uint Zinnober = 12;
    public const uint Brandung = 13;
    public const uint Pollux = 14;

    public const ushort Kapitaen = 1;
    public const ushort Pilot = 2;
    public const ushort Gunner = 3;
    public const ushort Marine = 4;
    public const ushort Truppfuehrer = 5;
    public const ushort Schuetze = 6;
    public const ushort Sanitaeter = 7;
    public const ushort Frachter = 8;
    public const ushort Flottenfuehrung = 9;
    public const ushort Bergung = 10;

    public const ushort Schiff1 = 1;
    public const ushort Schiff2 = 2;
    public const ushort Schiff3 = 3;
    public const ushort TruppAlpha = 10;
    public const ushort Logistik = 20;

    public const ushort NetzKommando = 2;
    public const ushort NetzOrga = 3;
    public const ushort NetzLogistik = 51;

    /// <summary>Die Attrappe mit dem Beispielabend: Stufen, Kontenverzeichnis, Karten und Rollenkatalog dazu.</summary>
    public static FunkDienstAttrappe MitRaidabend()
    {
        var d = new FunkDienstAttrappe(Raidabend()) { Uhr = () => Abend, Katalog = RaidKatalog() };
        d.Stufen[Nordwind] = Stufe.Admin;
        d.Stufen[Ferox] = Stufe.Raidleiter;
        d.OhnePraesenz.Add(new VerzeichnisEintrag
        {
            KontoId = Kranich, Name = "Kranich", Benutzername = "kranich", ScName = "Kranich", DiscordName = "Kranich/Ben", Stufe = Stufe.Funker, Online = false,
            LetzterPlatz = new LetzterPlatz { SchiffId = Schiff1, RolleId = Gunner },
        });
        d.Karten.Clear();
        foreach (var (konto, rollen, wuensche) in new (uint, ushort[], ushort[])[]
                 {
                     (Mustermann, [1, 2], []), (Kobold, [1], []), (Eule, [3], []), (Kranich, [3], []), (Hagel, [4], [3]), (Pollux, [], [3]),
                     (Anker, [4], []), (Quarz, [4], []), (Falke, [1], []), (Distel, [4], []), (Brandung, [6], []),
                 })
        {
            var f = d.Lage.Funker.FirstOrDefault(x => x.KontoId == konto);
            d.Karten[konto] = new MitgliedskarteAntwort
            {
                KontoId = konto, Name = f?.Name ?? "Kranich", ScName = f?.ScName ?? "Kranich", DiscordName = f?.DiscordName,
                Rollen = rollen, Lieblingsrollen = wuensche, Online = f?.Online ?? false, SchiffId = f?.SchiffId, RolleId = f?.RolleId, Platz = f?.Platz,
                Zeichen = f?.Zeichen ?? [], Freigegeben = true,
            };
        }
        return d;
    }

    /// <summary>Rollenkatalog des Abends (A46) mit den Rollen der Flotte, auf die jeder Eintrag zeigt (für "bester Platz").</summary>
    public static RollenkatalogAntwort RaidKatalog()
    {
        var k = BeispielKatalog();
        ushort[] Zu(ushort id) => id switch
        {
            1 => [Pilot],
            3 => [Gunner],
            4 => [Marine, Schuetze],
            5 => [Sanitaeter],
            6 => [Frachter, Bergung],
            _ => [],
        };
        return k with { Eintraege = [.. k.Eintraege.Select(e => e with { Rollen = Zu(e.Id) })] };
    }

    /// <summary>Lage des Beispielabends, aus Mustermanns Sicht (FUNKER mit Auftrag, ohne Platz).</summary>
    public static FunkLage Raidabend()
    {
        NetzInfo N(ushort id, string name, int reihenfolge, ushort? schiff = null, bool va = true) => new(id, name, id == 1, id is 1 or 2, va, reihenfolge) { SchiffId = schiff };
        var netze = new List<NetzInfo> { N(1, "Global", 0, va: false), N(NetzKommando, "Kommando", 1, va: false), N(NetzOrga, "Orga", 2) };
        var einheitsnetze = new Dictionary<ushort, Dictionary<string, ushort>>();
        foreach (var (s, nummer) in new[] { (Schiff1, 1), (Schiff2, 2), (Schiff3, 3) })
        {
            var basis = (ushort)(10 * nummer);
            netze.Add(N((ushort)(basis + 1), "Durchsagen " + nummer, basis + 1, s));
            netze.Add(N((ushort)(basis + 2), "Crew " + nummer, basis + 2, s));
            netze.Add(N((ushort)(basis + 3), "Brücke " + nummer, basis + 3, s));
            einheitsnetze[s] = new Dictionary<string, ushort>(StringComparer.Ordinal) { ["DURCHSAGEN"] = (ushort)(basis + 1), ["CREW"] = (ushort)(basis + 2), ["BRUECKE"] = (ushort)(basis + 3) };
        }
        netze.Add(N(41, "Trupp Alpha", 41, TruppAlpha));
        netze.Add(N(NetzLogistik, "Logistik", 51, Logistik));
        einheitsnetze[TruppAlpha] = new Dictionary<string, ushort>(StringComparer.Ordinal) { ["TRUPP"] = 41 };
        einheitsnetze[Logistik] = new Dictionary<string, ushort>(StringComparer.Ordinal) { ["ORGA"] = NetzLogistik };

        RollenfeldInfo K(byte feld, string kennung, bool senden, bool vorschlag = false) => new(feld, null, kennung, true, senden, vorschlag, false);
        RollenfeldInfo F(byte feld, ushort netz, bool senden, bool vorschlag = false) => new(feld, netz, null, true, senden, vorschlag, false);
        RolleInfo R(ushort id, string name, bool schiff, IReadOnlyList<RollenfeldInfo> felder, int? kapazitaet = null) =>
            new(id, name, id, schiff, true, [.. felder.Select(f => f.Kennung is { } k ? Kennungsname(k) : netze.First(n => n.Id == f.NetzId).Name)])
            {
                Felder = felder, Kapazitaet = kapazitaet,
            };
        var rollen = new[]
        {
            R(Kapitaen, "Kapitän", true, [F(0, NetzKommando, true, true), K(1, "DURCHSAGEN", true), K(2, "CREW", true), K(3, "BRUECKE", true)]),
            R(Pilot, "Pilot", true, [K(0, "BRUECKE", true, true), K(1, "CREW", false), K(2, "DURCHSAGEN", false)]),
            R(Gunner, "Gunner", true, [K(0, "CREW", true, true), K(1, "BRUECKE", false), K(2, "DURCHSAGEN", false)]),
            R(Marine, "Marine", true, [K(0, "CREW", true, true), K(1, "DURCHSAGEN", false)]),
            R(Truppfuehrer, "Truppführer", true, [K(0, "TRUPP", true, true), F(1, NetzKommando, false)]),
            R(Schuetze, "Schütze", true, [K(0, "TRUPP", true, true)]),
            R(Sanitaeter, "Sanitäter", true, [K(0, "TRUPP", true, true), F(1, NetzLogistik, false)]),
            R(Frachter, "Frachter", true, [K(0, "ORGA", true, true), F(1, NetzKommando, false)]),
            R(Flottenfuehrung, "Flottenführung", false, [F(0, NetzKommando, true, true)], 2),
            R(Bergung, "Bergung", true, [K(0, "ORGA", true, true)]),
        };

        PlatzInfo P(ushort rolle, int kap, int mindest = 0) => new(rolle, kap, 0) { Mindest = mindest };
        SchiffInfo S(ushort id, string name, int reihenfolge, Einheitsart art, string? typ, params PlatzInfo[] plaetze) =>
            new(id, name, reihenfolge, plaetze) { Art = art, Typ = typ, Netze = einheitsnetze[id] };
        var schiffe = new[]
        {
            S(Schiff1, "Schiff 1", 1, Einheitsart.Schiff, "Hammerhead", P(Kapitaen, 1, 1), P(Pilot, 1, 1), P(Gunner, 2, 1), P(Marine, 2)),
            S(Schiff2, "Schiff 2", 2, Einheitsart.Schiff, "Redeemer", P(Kapitaen, 1, 1), P(Pilot, 1, 1), P(Gunner, 2, 1)),
            S(Schiff3, "Schiff 3", 3, Einheitsart.Schiff, "Valkyrie", P(Kapitaen, 1), P(Pilot, 1, 1), P(Gunner, 1, 1), P(Marine, 3)),
            S(TruppAlpha, "Trupp Alpha", 10, Einheitsart.Trupp, "Infanterie", P(Truppfuehrer, 1, 1), P(Sanitaeter, 1, 1), P(Schuetze, 6)),
            S(Logistik, "Logistik", 20, Einheitsart.Orga, null, P(Frachter, 4), P(Bergung, 2)),
        };

        FunkerInfo M(uint id, string name, string? sc, string? discord, ushort? schiff, ushort? rolle, bool online = true,
            IReadOnlyList<string>? zeichen = null, (ushort?, ushort?)? zuletzt = null, IReadOnlyList<string>? abzeichen = null, string? vorn = null)
        {
            var felder = new List<FeldInfo>();
            if (rolle is { } r)
            {
                var einheit = schiffe.FirstOrDefault(x => x.Id == schiff);
                foreach (var rf in rollen.First(x => x.Id == r).Felder)
                {
                    var netz = rf.NetzId ?? (einheit is not null && rf.Kennung is not null && einheit.Netze.TryGetValue(rf.Kennung, out var n) ? n : (ushort)0);
                    if (netz != 0) felder.Add(new FeldInfo(rf.Feld, netz, rf.Hoeren, rf.Senden, rf.Fest));
                }
            }
            return new FunkerInfo(id, name, online, schiff, rolle)
            {
                ScName = sc, DiscordName = discord, Zeichen = zeichen ?? [], Abzeichen = abzeichen ?? [], AbzeichenVorn = vorn,
                Platz = rolle is null ? null : online ? PlatzStatus.Belegt : PlatzStatus.Reserviert,
                Details = new FunkerDetails(felder, felder.Where(x => x.Senden).Take(1).Select(x => x.NetzId).ToList(), [])
                {
                    Verbindung = online ? Verbindungsweg.Udp : null, Guete = online ? Guete.Gut : null,
                    Quelle = rolle is null ? null : MitgliedschaftsQuelle.Selbst, Seit = rolle is null ? null : Abend.AddMinutes(-20).ToUnixTimeMilliseconds(),
                    LetzterPlatz = zuletzt,
                },
            };
        }
        var funker = new[]
        {
            M(Nordwind, "Nordwind", "Nordwind", "Nordwind", Schiff1, Kapitaen, zeichen: ["ADMIN"], abzeichen: ["NAVY_09", "T_CAPITAL"], vorn: "NAVY_09"),
            M(Mustermann, "Max", "Mustermann", "Mustermann/Max", null, null, zeichen: ["EINSATZLEITUNG"], abzeichen: MustermannAbzeichen),
            M(Ferox, "Ferox", "Ferox", "Ferox", Schiff2, Kapitaen, zeichen: ["FUEHRUNG"], abzeichen: ["NAVY_06", "T_MEDIUM"]),
            M(Kobold, "Kobold", "Kobold", "Kobold", Schiff1, Pilot, abzeichen: ["T_JAEGER"]),
            M(Eule, "Eule", "Eule", "Eule/Mia", Schiff1, Gunner, abzeichen: ["T_GUNNER_S", "T_GUNNER_M"]),
            M(Falke, "Falke", "Falke", "Falke/Tom", Schiff2, Pilot, abzeichen: ["NAVY_02", "T_JAEGER"], vorn: "T_JAEGER"),
            M(Anker, "Anker", "Anker", "Anker", Schiff3, Marine, abzeichen: ["MARINE_03", "T_BODEN"]),
            M(Quarz, "Quarz", "Quarz", "Quarz", Schiff3, Marine, abzeichen: ["MARINE_01"]),
            M(Hagel, "Hagel", "Hagel", "Hagel", null, null, zuletzt: (Schiff3, Marine)),
            M(Distel, "Distel", "Distel", "Distel", TruppAlpha, Truppfuehrer, abzeichen: ["MARINE_07", "T_SANI", "T_AUSBILDER"], vorn: "T_SANI"),
            M(Zinnober, "Zinnober", null, "Zinnober", null, null),
            M(Brandung, "Brandung", "Brandung", "Brandung", Logistik, Frachter, abzeichen: ["ORGA_02", "T_LOGISTIK", "T_HANDEL"]),
            M(Pollux, "Pollux", "Pollux", "Pollux", null, null),
        };
        var ende = new DateTimeOffset(new DateTime(2026, 10, 3, 4, 0, 0, DateTimeKind.Local));
        return Belegungsstand(new FunkLage
        {
            Status = Verbindungsstatus.Aktiv,
            Server = "funk.example.org (Vorführung)",
            Konto = new KontoInfo(Mustermann, "Max", "max", Stufe.Funker, [])
            {
                ScNamen = ["Mustermann"], DiscordName = "Mustermann/Max", Zeichen = ["EINSATZLEITUNG"], Abzeichen = MustermannAbzeichen, Rollen = [1, 2],
                Beauftragung = new BeauftragungInfo(Abend.AddHours(-2), ende,
                    [Recht.GlobalSenden, Recht.Durchsage, Recht.DurchsageAlle, Recht.MehrfachSenden, Recht.Zuweisen, Recht.SprechanzeigeAlle], Nordwind, "Op Nachtfalke", true),
            },
            Netze = netze,
            Felder = [new FeldInfo(0, NetzKommando, true, false, false), new FeldInfo(1, NetzOrga, true, true, false)],
            SendeFelder = [1],
            GlobalDarfSenden = true,
            Schiffe = schiffe,
            Rollen = rollen,
            Funker = funker,
            Sprecher = [new SprecherInfo(Anker, "Anker", [32], false)],
            Selbstwahl = true,
            NetzListeVersion = 7,
            ZustandsVersion = 100,
            ServerS2 = true,
            Phasen = RaidPhasen(rollen),
            LaufendePhase = new LaufendePhaseInfo(1, "Anflug", "ANFLUG", Nordwind, Abend.AddMinutes(-40)),
            KontoVersion = 1,
            LetzterStand = new LetzterStandInfo(Schiff2, Pilot, [new FeldInfo(0, 23, true, true, false), new FeldInfo(1, 22, true, false, false)], [0], false),
        });
    }

    /// <summary>Die Abzeichen von Max Mustermann am Beispielabend: zwei Ränge und eine Tätigkeit, keine Wahl "vorn" (dann steht der Stern vorn).</summary>
    public static IReadOnlyList<string> MustermannAbzeichen { get; } = ["MARINE_04", "ORGA_03", "T_FUNKER"];

    /// <summary>Klartext einer Kennung für Einheitsnetze (Flottenseite und Vorschau): "Crew (Einheit)".</summary>
    internal static string Kennungsname(string kennung) => kennung switch
    {
        "DURCHSAGEN" => "Durchsagen (Einheit)",
        "CREW" => "Crew (Einheit)",
        "BRUECKE" => "Brücke (Einheit)",
        "TRUPP" => "Trupp (Einheit)",
        "ORGA" => "Orga (Einheit)",
        _ => kennung + " (Einheit)",
    };
}
