// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Attrappe des Funkbetriebs für Entwurf, Vorführung und Tests: hält eine <see cref="FunkLage"/>, ändert sie auf
/// Anfrage wie ein freundlicher Server und schreibt jede Anfrage in <see cref="Aufrufe"/> mit. Keine Netzwerkverbindung.
/// </summary>
public sealed partial class FunkDienstAttrappe : IFunkDienst
{
    private readonly Lock _sperre = new();
    private readonly List<string> _aufrufe = [];
    private FunkLage _lage;

    public FunkDienstAttrappe(FunkLage? lage = null) => _lage = lage ?? FunkLage.Leer;

    public event Action? LageGeaendert;

    public FunkLage Lage
    {
        get
        {
            lock (_sperre) return _lage;
        }
    }

    /// <summary>Alle Anfragen in Kurzform, etwa <c>Hoeren 4 aus</c>.</summary>
    public IReadOnlyList<string> Aufrufe
    {
        get
        {
            lock (_sperre) return [.. _aufrufe];
        }
    }

    /// <summary>Ist er gesetzt, scheitert die nächste Anfrage mit diesem Text.</summary>
    public string? NaechsterFehler { get; set; }

    /// <summary>Ersetzt den Stand (Tests, Vorführung) und meldet die Änderung.</summary>
    public void LageSetzen(Func<FunkLage, FunkLage> aenderung)
    {
        ArgumentNullException.ThrowIfNull(aenderung);
        lock (_sperre) _lage = aenderung(_lage);
        LageGeaendert?.Invoke();
    }

    public Task VerbindenAsync()
    {
        Merken("Verbinden");
        LageSetzen(l => l with { Status = Verbindungsstatus.Aktiv, Hinweis = null });
        return Task.CompletedTask;
    }

    public Task TrennenAsync()
    {
        Merken("Trennen");
        LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt, Sprecher = [], Hinweis = "Vom Nutzer getrennt" });
        return Task.CompletedTask;
    }

    public Task<string?> HoerenSetzenAsync(ushort netzId, bool hoeren)
    {
        Merken("Hoeren " + netzId + (hoeren ? " an" : " aus"));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        LageSetzen(l => l with { Felder = [.. l.Felder.Select(x => x.NetzId == netzId ? x with { Hoeren = hoeren } : x)] });
        return Task.FromResult<string?>(null);
    }

    public Task<string?> SendeauswahlSetzenAsync(IReadOnlyList<byte> felder, bool mitGlobal)
    {
        ArgumentNullException.ThrowIfNull(felder);
        var sortiert = felder.Distinct().Order().ToArray();
        Merken("Senden [" + string.Join(",", sortiert) + "]" + (mitGlobal ? " +Global" : ""));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        LageSetzen(l => l with { SendeFelder = sortiert, GlobalSenden = mitGlobal });
        return Task.FromResult<string?>(null);
    }

    public Task<string?> EinsteigenAsync(ushort rolleId, ushort? schiffId)
    {
        Merken("Einsteigen " + rolleId + " " + (schiffId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        LageSetzen(l => l with { RolleId = rolleId, SchiffId = schiffId });
        return Task.FromResult<string?>(null);
    }

    public Task<string?> PegelSetzenAsync(uint kontoId, float db)
    {
        Merken("Pegel " + kontoId + " " + db.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        LageSetzen(l => l with { FunkerPegel = new Dictionary<uint, float>(l.FunkerPegel) { [kontoId] = db } });
        return Task.FromResult<string?>(null);
    }

    /// <summary>Rollenkatalog der Attrappe; ohne Setzen der aus dem Beispielabend (A46).</summary>
    public RollenkatalogAntwort Katalog { get; set; } = BeispielKatalog();

    /// <summary>Karten je Konto; fehlt eine, baut die Attrappe sie aus der Praesenz (nur Namen).</summary>
    public Dictionary<uint, MitgliedskarteAntwort> Karten { get; } = BeispielKarten();

    public Task<Dienstantwort<RollenkatalogAntwort>> RollenkatalogAsync()
    {
        Merken("Rollenkatalog");
        return Task.FromResult(Scheitern() is { } f ? Dienstantwort.Schlecht<RollenkatalogAntwort>(f) : Dienstantwort.Gut(Katalog));
    }

    public Task<string?> RollenkatalogErgaenzenAsync(IReadOnlyList<Rollenkatalogeintrag> eintraege, long version)
    {
        ArgumentNullException.ThrowIfNull(eintraege);
        Merken("Katalog ergänzen " + eintraege.Count + " v" + version);
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        if (version != Katalog.Version) return Task.FromResult<string?>("Der Stand hat sich inzwischen geändert; bitte noch einmal.");
        if (Katalog.Eintraege.Any(alt => eintraege.All(e => e.Id != alt.Id || e.Name != alt.Name))) return Task.FromResult<string?>("Dafür fehlt das Recht.");
        Katalog = new RollenkatalogAntwort { Eintraege = eintraege, Version = version + 1 };
        return Task.FromResult<string?>(null);
    }

    public Task<Dienstantwort<MitgliedskarteAntwort>> MitgliedskarteAsync(uint kontoId)
    {
        Merken("Karte " + kontoId);
        if (Scheitern() is { } f) return Task.FromResult(Dienstantwort.Schlecht<MitgliedskarteAntwort>(f));
        if (Karten.TryGetValue(kontoId, out var k)) return Task.FromResult(Dienstantwort.Gut(k));
        var p = Lage.Funker.FirstOrDefault(x => x.KontoId == kontoId);
        return Task.FromResult(p is null
            ? Dienstantwort.Schlecht<MitgliedskarteAntwort>("Das gibt es nicht (mehr).")
            : Dienstantwort.Gut(new MitgliedskarteAntwort
            {
                KontoId = kontoId,
                Name = p.Name,
                ScName = p.ScName,
                DiscordName = p.DiscordName,
                Online = p.Online,
                SchiffId = p.SchiffId,
                RolleId = p.RolleId,
                Zeichen = p.Zeichen,
                Freigegeben = true,
            }));
    }

    public Task<string?> LieblingsrollenSetzenAsync(IReadOnlyList<ushort> rollen, ushort? ersteWahl)
    {
        ArgumentNullException.ThrowIfNull(rollen);
        Merken("Lieblingsrollen [" + string.Join(",", rollen) + "]" + (ersteWahl is { } w ? " *" + w : ""));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        LageSetzen(l => l with
        {
            Konto = l.Konto is { } k ? k with { Lieblingsrollen = [.. rollen.Where(r => !k.Rollen.Contains(r))], ErsteWahl = ersteWahl } : null,
        });
        return Task.FromResult<string?>(null);
    }

    public Task<string?> ProfilSetzenAsync(ProfilSetzenAnfrage anfrage)
    {
        ArgumentNullException.ThrowIfNull(anfrage);
        Merken("Profil" + (anfrage.ScNamen is { } s ? " [" + string.Join(",", s) + "]" : "") + (anfrage.DiscordName is { } d ? " discord=" + d : "")
            + (anfrage.ZeichenZeigen is { } z ? " zeichen=" + z : "") + (anfrage.KarteFreigeben is { } kf ? " karte=" + kf : "")
            + (anfrage.AbzeichenVorn is { } v ? " vorn=" + v : "") + (anfrage.AbzeichenAus is { } aus ? " aus=[" + string.Join(",", aus) + "]" : ""));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        // Wie der Server (A53): Den Discord-Namen ändert nur ein Admin; ein anderer als der bisherige wird abgewiesen.
        if (anfrage.DiscordName is { } dn && !string.Equals(dn, Lage.Konto?.DiscordName, StringComparison.Ordinal))
            return Task.FromResult<string?>("Den Discord-Namen ändert nur ein Admin.");
        LageSetzen(l => l with
        {
            Konto = l.Konto is { } k
                ? k with
                {
                    ScNamen = anfrage.ScNamen ?? k.ScNamen,
                    ZeichenZeigen = anfrage.ZeichenZeigen ?? k.ZeichenZeigen,
                    KarteFreigeben = anfrage.KarteFreigeben ?? k.KarteFreigeben,
                    // Wie der Server (0.11.7): "" heißt keine Wahl, die Liste ersetzt die gespeicherte.
                    AbzeichenVorn = anfrage.AbzeichenVorn is { } vorn ? vorn.Length == 0 ? null : vorn : k.AbzeichenVorn,
                    AbzeichenAus = anfrage.AbzeichenAus ?? k.AbzeichenAus,
                }
                : null,
        });
        return Task.FromResult<string?>(null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Merken(string aufruf)
    {
        lock (_sperre) _aufrufe.Add(aufruf);
    }

    private string? Scheitern()
    {
        var f = NaechsterFehler;
        NaechsterFehler = null;
        return f;
    }

    /// <summary>Beispiel-Lage einer Schiffsschlacht (Namen erfunden), verbunden und aktiv.</summary>
    public static FunkLage Beispiel()
    {
        NetzInfo N(ushort id, string name, bool va, bool nurTaste = false) => new(id, name, id == 1, nurTaste, va, id);
        var netze = new[]
        {
            N(1, "Global", false, nurTaste: true),
            N(2, "Kommando", false),
            N(3, "Durchsagen", false, nurTaste: true),
            N(4, "Crew", true),
            N(5, "Brücke", true),
            N(6, "Jäger", true),
            N(7, "Reserve 2", true),
            N(8, "Reserve 3", true),
            N(9, "Logistik", true),
            N(10, "Sanität", true),
        };
        var felder = new[]
        {
            new FeldInfo(0, 2, true, true, true),
            new FeldInfo(1, 3, true, false, true),
            new FeldInfo(2, 4, true, true, false),
            new FeldInfo(3, 5, true, true, false),
            new FeldInfo(4, 6, false, false, false),
            new FeldInfo(5, 7, true, false, false),
            new FeldInfo(6, 8, false, false, false),
            new FeldInfo(7, 9, true, false, false),
            new FeldInfo(8, 10, true, false, false),
        };
        // Namen erfunden; Discord-Namen mit Schrägstrich wie "Mustermann/Max" (A42), Zeichen aus dem Beispielabend.
        var funker = new[]
        {
            new FunkerInfo(1, "Max", true, 1, 1)
            {
                ScName = "Mustermann", DiscordName = "Mustermann/Max", Zeichen = ["FUEHRUNG"], Abzeichen = ["NAVY_06", "T_FUNKER"], AbzeichenVorn = "T_FUNKER",
            },
            new FunkerInfo(2, "Falke", true, 1, 2) { ScName = "Falke", DiscordName = "Falke/Tom" },
            new FunkerInfo(3, "Kobold", true, 1, 3) { ScName = "Kobold", DiscordName = "Kobold" },
            new FunkerInfo(4, "Nordlicht", true, 2, 1) { ScName = "Nordlicht", DiscordName = "Nordlicht/Anna", Zeichen = ["ADMIN"], Abzeichen = ["MARINE_04", "T_GUNNER_M"] },
            new FunkerInfo(5, "Kranich", true, 2, 3) { ScName = "Kranich", DiscordName = "Kranich/Ben", Abzeichen = ["T_SANI"] },
            new FunkerInfo(6, "Eule", false, null, null) { ScName = "Eule", DiscordName = "Eule/Mia" },
            new FunkerInfo(7, "Sturmvogel", true, null, 5) { ScName = "Sturmvogel", DiscordName = "Sturmvogel" },
        };
        PlatzInfo P(ushort rolle, int kap, int belegt) => new(rolle, kap, belegt);
        var schiffe = new[]
        {
            new SchiffInfo(1, "Schiff 1", 1, [P(1, 1, 1), P(2, 1, 1), P(3, 3, 1), P(4, 2, 0)]),
            new SchiffInfo(2, "Schiff 2", 2, [P(1, 1, 1), P(2, 1, 0), P(3, 3, 1), P(4, 2, 0)]),
            new SchiffInfo(3, "Schiff 3", 3, [P(1, 1, 0), P(2, 1, 0), P(3, 3, 0), P(4, 2, 0)]),
        };
        var rollen = new[]
        {
            new RolleInfo(1, "Kapitän", 1, true, true, ["Kommando", "Durchsagen", "Crew", "Brücke"]),
            new RolleInfo(2, "Pilot", 2, true, true, ["Durchsagen", "Crew", "Brücke"]),
            new RolleInfo(3, "Gunner", 3, true, true, ["Durchsagen", "Crew"]),
            new RolleInfo(4, "Ingenieur", 4, true, true, ["Durchsagen", "Crew"]),
            new RolleInfo(5, "Jäger", 5, false, true, ["Kommando", "Jäger"]),
            new RolleInfo(6, "Raidleitung", 6, false, false, ["Kommando", "Durchsagen", "Jäger", "Logistik"]),
        };
        return new FunkLage
        {
            Status = Verbindungsstatus.Aktiv,
            Server = "funk.example.org (Vorführung)",
            Konto = new KontoInfo(1, "Max", "max", Stufe.Raidleiter, [Recht.MehrfachSenden, Recht.Durchsage, Recht.GlobalSenden])
            {
                ScNamen = ["Mustermann"],
                DiscordName = "Mustermann/Max",
                Rollen = [1, 2],
                Lieblingsrollen = [4],
                ErsteWahl = 1,
                Zeichen = ["FUEHRUNG"],
                Abzeichen = ["NAVY_06", "T_FUNKER"],
                AbzeichenVorn = "T_FUNKER",
            },
            Geraet = "4F2A 81C0 ... 9C31",
            Schluesselart = "TPM",
            Netze = netze,
            Felder = felder,
            SendeFelder = [2],
            GlobalDarfSenden = true,
            Schiffe = schiffe,
            Rollen = rollen,
            Funker = funker,
            Sprecher = [new SprecherInfo(2, "Falke", [2], false), new SprecherInfo(3, "Kobold", [4], false)],
            SchiffId = 1,
            RolleId = 1,
            Selbstwahl = true,
            ServerS2 = true,
        };
    }

    /// <summary>Rollenkatalog des Beispielabends (A46: Medium Fighter Pilot, Ingenieur, Medium Gunner, Marine und zwei mehr).</summary>
    public static RollenkatalogAntwort BeispielKatalog() => new()
    {
        Eintraege =
        [
            new Rollenkatalogeintrag { Id = 1, Name = "Medium Fighter Pilot", Gruppe = "Flug" },
            new Rollenkatalogeintrag { Id = 2, Name = "Ingenieur", Gruppe = "Technik" },
            new Rollenkatalogeintrag { Id = 3, Name = "Medium Gunner", Gruppe = "Geschütz" },
            new Rollenkatalogeintrag { Id = 4, Name = "Marine", Gruppe = "Boden" },
            new Rollenkatalogeintrag { Id = 5, Name = "Sanitäter", Gruppe = "Boden" },
            new Rollenkatalogeintrag { Id = 6, Name = "Logistik", Gruppe = "Orga" },
        ],
        Version = 3,
    };

    /// <summary>Karten des Beispielabends: Nordlicht mit Rollen und Wünschen, Kobold ohne Freigabe seiner Angaben.</summary>
    public static Dictionary<uint, MitgliedskarteAntwort> BeispielKarten() => new()
    {
        [1] = new MitgliedskarteAntwort
        {
            KontoId = 1, Name = "Max", ScName = "Mustermann", ScNamen = ["Mustermann"], DiscordName = "Mustermann/Max",
            Rollen = [1, 2], Lieblingsrollen = [4], ErsteWahl = 1, Online = true, SchiffId = 1, RolleId = 1, Platz = PlatzStatus.Belegt, Zeichen = ["FUEHRUNG"],
            Freigegeben = true,
        },
        [4] = new MitgliedskarteAntwort
        {
            KontoId = 4, Name = "Nordlicht", ScName = "Nordlicht", ScNamen = ["Nordlicht", "Nordlicht Zwei"], DiscordName = "Nordlicht/Anna",
            Rollen = [3], Lieblingsrollen = [1, 3], ErsteWahl = 1, Online = true, SchiffId = 2, RolleId = 1, Platz = PlatzStatus.Belegt,
            LetzterPlatz = new LetzterPlatz { SchiffId = 3, RolleId = 2 }, Zeichen = ["ADMIN"], Verbindung = Verbindungsweg.Udp, Guete = Guete.Gut,
            Freigegeben = true,
        },
        [3] = new MitgliedskarteAntwort
        {
            KontoId = 3, Name = "Kobold", ScName = "Kobold", DiscordName = "Kobold", Online = true, SchiffId = 1, RolleId = 3, Platz = PlatzStatus.Belegt, Zeichen = [],
            Freigegeben = false,
        },
    };
}
