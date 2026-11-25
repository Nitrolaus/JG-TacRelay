// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Globalization;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>Was die Abbildung braucht, als reine Werte (Tests bauen sie ohne Verbindung).</summary>
public sealed record LageEingabe(
    ClientZustand Zustand,
    Welcome? Welcome,
    NetzListe? NetzListe,
    Mitgliedschaften? Mitgliedschaften,
    IReadOnlyDictionary<uint, PraesenzEintrag> Praesenz,
    ImmutableArray<byte> SendeFelder,
    bool GlobalSenden,
    IReadOnlyList<SprechanzeigeEintrag> Sprechanzeige,
    PegelVoreinstellungen? Pegel = null,
    Belegung? Belegung = null,
    long ZeitVersatzMs = 0)
{
    /// <summary>Höchste bekannte <c>zustandsVersion</c> (Lagebild), für <c>basisVersion</c> auf dem Brett.</summary>
    public long ZustandsVersion { get; init; }

    /// <summary>Die laufende Phase aus dem letzten Push 0x4004 (Lagebild), sonst <c>null</c>.</summary>
    public PhaseGeschaltet? Phase { get; init; }

    /// <summary>Der Abgleich der laufenden Sitzung ist abgeschlossen (<c>Lagebild.Vollstaendig</c>).</summary>
    public bool Vollstaendig { get; init; }

    /// <summary>Seit 0.12.0: der laufende Stand des Meldesystems (Lagebild, ohne Befehl), sonst <c>null</c>.</summary>
    public Meldestand? Meldestand { get; init; }

    /// <summary>Seit 0.12.0: der letzte Bereit-Stand (Lagebild), sonst <c>null</c>.</summary>
    public BereitStand? BereitStand { get; init; }
}

/// <summary>Übersetzt den Protokollstand in die <see cref="FunkLage"/> der Oberfläche; reine Funktion.</summary>
public static class LageAbbildung
{
    public static FunkLage Abbilden(LageEingabe e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var netzListe = e.NetzListe;
        var netze = netzListe is null
            ? []
            : netzListe.Netze
                .OrderBy(n => n.Typ == NetzTyp.Global ? 1 : 0)
                .ThenBy(n => n.Reihenfolge)
                .ThenBy(n => n.Id)
                .Select(n => new NetzInfo(n.Id, n.Name, n.Typ == NetzTyp.Global, n.NurTaste, n.AusloeserStandard == Ausloeser.Va, n.Reihenfolge)
                {
                    SchiffId = n.SchiffId,
                    Kanaele = n.Kanaele is { Count: >= Kanalrechnung.EintraegeMin } k ? k : null,
                    Offen = n.Offen,
                    Gruppe = n.Gruppe,
                })
                .ToList();
        var namen = netze.ToDictionary(n => n.Id, n => n.Name);

        var m = e.Mitgliedschaften;
        var felder = m is null ? [] : m.Felder.OrderBy(f => f.Feld).Select(Feld).ToList();

        var praesenz = e.Praesenz.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var funker = praesenz.Select(p => new FunkerInfo(p.KontoId, p.Name, p.Online, p.SchiffId, p.RolleId)
        {
            ScName = p.ScName,
            DiscordName = p.DiscordName,
            Zeichen = p.Zeichen ?? [],
            Abzeichen = p.Abzeichen ?? [],
            AbzeichenVorn = p.AbzeichenVorn,
            Platz = p.Platz,
            Details = Details(p),
        }).ToList();

        // K1: Belegt zählt BELEGT und RESERVIERT wie die Kapazität des Servers. Mit dem Push Belegung (0x4003) genau seine Zahlen,
        // ohne ihn (Server vor 0.3.0) aus der Praesenz: jede Person mit Platz, auch ohne Sitzung.
        var belegung = e.Belegung?.Plaetze.GroupBy(p => ((ushort)(p.SchiffId ?? 0), p.RolleId)).ToDictionary(g => g.Key, g => g.First());
        int Besetzt(ushort schiff, ushort rolle) => belegung is not null
            ? belegung.TryGetValue((schiff, rolle), out var b) ? b.Belegt + b.Reserviert : 0
            : praesenz.Count(x => (x.SchiffId ?? 0) == schiff && x.RolleId == rolle && (x.Online || x.Platz is not null));
        int Reserviert(ushort schiff, ushort rolle) => belegung is not null
            ? belegung.TryGetValue((schiff, rolle), out var b) ? b.Reserviert : 0
            : praesenz.Count(x => (x.SchiffId ?? 0) == schiff && x.RolleId == rolle && !x.Online && x.Platz == PlatzStatus.Reserviert);

        var schiffe = netzListe is null
            ? []
            : netzListe.Schiffe.OrderBy(s => s.Reihenfolge).ThenBy(s => s.Id).Select(s => new SchiffInfo(s.Id, s.Name, s.Reihenfolge,
                [.. s.Plaetze.Select(p => new PlatzInfo(p.RolleId, p.Kapazitaet, Besetzt(s.Id, p.RolleId))
                {
                    Reserviert = Reserviert(s.Id, p.RolleId),
                    Mindest = p.Mindest ?? 0,
                })])
            {
                Art = s.Art ?? Einheitsart.Schiff,
                Typ = s.Typ,
                Netze = s.Netze,
            }).ToList();

        var rollen = netzListe is null
            ? []
            : netzListe.Rollen.OrderBy(r => r.Reihenfolge).ThenBy(r => r.Id).Select(r => new RolleInfo(r.Id, r.Name, r.Reihenfolge, r.BrauchtSchiff,
                r.SelbstWaehlbar, [.. r.Felder.OrderBy(f => f.Feld).Select(f => f.NetzId is { } id
                    ? namen.GetValueOrDefault(id, "Kanal " + id.ToString(CultureInfo.InvariantCulture))
                    : Schiffsnetz(f.Schiffsnetz))])
            {
                Kapazitaet = r.Kapazitaet,
                Belegt = Besetzt(0, r.Id),
                Felder = [.. r.Felder.OrderBy(f => f.Feld).Select(f => new RollenfeldInfo(f.Feld, f.NetzId, f.Schiffsnetz, f.Hoeren, f.Senden, f.SendenStandard, f.Fest) { Ch = f.Ch })],
            }).ToList();

        var alleNetze = netze.Where(n => !n.IstGlobal).Select(n => n.Id).ToArray();
        var sprecher = e.Sprechanzeige.Select(s => new SprecherInfo(s.KontoId,
            e.Praesenz.TryGetValue(s.KontoId, out var p) ? p.Name : "Funker " + s.KontoId.ToString(CultureInfo.InvariantCulture),
            s.Art switch
            {
                SprechArt.Global => [Grenzen.GlobalNetzId],
                SprechArt.Durchsage when s.Netze.Count == 0 => alleNetze,
                _ => [.. s.Netze],
            },
            s.Art == SprechArt.Durchsage)).ToList();

        // Die eigenen Angaben kommen zuerst mit Welcome, danach mit jeder Änderung in Mitgliedschaften.konto (F3, F13).
        var ks = m?.Konto;
        var auftrag = ks is not null ? ks.Beauftragung : e.Welcome?.Konto?.Beauftragung;
        var alsFunkerBis = ks is not null ? ks.AlsFunkerBis : e.Welcome?.Konto?.AlsFunkerBis;
        var konto = e.Welcome?.Konto is { } k
            ? new KontoInfo(k.Id, k.Name, k.Benutzername, k.Stufe, ks?.Rechte ?? k.Rechte ?? [])
            {
                AlsFunkerBis = alsFunkerBis is { } funkerBis ? DateTimeOffset.FromUnixTimeMilliseconds(funkerBis) : null,
                Beauftragung = auftrag is null
                    ? null
                    : new BeauftragungInfo(DateTimeOffset.FromUnixTimeMilliseconds(auftrag.Von), DateTimeOffset.FromUnixTimeMilliseconds(auftrag.Bis), auftrag.Rechte,
                        auftrag.Durch, auftrag.Grund, auftrag.Aktiv),
                ScNamen = ks?.ScNamen ?? k.ScNamen ?? [],
                DiscordName = ks is not null ? ks.DiscordName : k.DiscordName,
                Admin = ks is not null ? ks.Admin : k.Admin,
                AdminAntrag = (ks is not null ? ks.AdminAntrag : k.AdminAntrag) == true,
                Eigentuemer = (ks is not null ? ks.Eigentuemer : k.Eigentuemer) == true,
                Rollen = ks?.Rollen ?? [],
                Lieblingsrollen = ks?.Lieblingsrollen ?? [],
                ErsteWahl = ks?.ErsteWahl,
                Zeichen = ks?.Zeichen ?? [],
                ZeichenZeigen = ks?.ZeichenZeigen ?? true,
                KarteFreigeben = ks?.KarteFreigeben ?? true,
                Abzeichen = ks?.Abzeichen ?? [],
                AbzeichenVorn = ks?.AbzeichenVorn,
                AbzeichenAus = ks?.AbzeichenAus ?? [],
                MeldungenSenden = ks?.MeldungenSenden == true,
            }
            : null;

        var pegel = new Dictionary<uint, float>();
        if (e.Pegel is { } pv)
            foreach (var p in pv.Eintraege.Where(x => x.NetzId == 0)) pegel[p.KontoId] = (float)p.Db;

        return new FunkLage
        {
            Status = Status(e.Zustand),
            Konto = konto,
            Netze = netze,
            Felder = felder,
            SendeFelder = [.. e.SendeFelder],
            GlobalSenden = e.GlobalSenden,
            GlobalDarfSenden = m?.Global.Senden == true,
            Schiffe = schiffe,
            Rollen = rollen,
            Funker = funker,
            Sprecher = sprecher,
            FunkerPegel = pegel,
            SchiffId = m?.SchiffId,
            RolleId = m?.RolleId,
            Selbstwahl = netzListe?.Einstellungen.Selbstwahl == true,
            SelbstwahlDurch = netzListe is { Einstellungen.Selbstwahl: false } ? netzListe.Einstellungen.SelbstwahlDurch ?? 0 : null,
            SelbstwahlBis = netzListe is { Einstellungen: { Selbstwahl: false, SelbstwahlBis: { } bis } } ? DateTimeOffset.FromUnixTimeMilliseconds(bis) : null,
            Nachzuegler = netzListe?.Einstellungen.Nachzuegler ?? true,
            NetzListeVersion = netzListe?.Version ?? 0,
            ZustandsVersion = Math.Max(e.ZustandsVersion, Math.Max(m?.ZustandsVersion ?? 0, e.Belegung?.ZustandsVersion ?? 0)),
            Online = e.Belegung?.Online ?? praesenz.Count(x => x.Online),
            MitPlatz = e.Belegung?.MitPlatz ?? praesenz.Count(x => x.Online && x.RolleId is not null),
            Platz = m?.Platz,
            Quelle = m?.Quelle,
            QuelleDurch = m?.Durch,
            LetzterStand = m?.LetzterStand is { } ls
                ? new LetzterStandInfo(ls.SchiffId, ls.RolleId, [.. ls.Felder.OrderBy(f => f.Feld).Select(Feld)], [.. ls.Sendeauswahl.Felder], ls.Sendeauswahl.Global)
                : null,
            ZeitVersatz = TimeSpan.FromMilliseconds(e.ZeitVersatzMs),
            // Wie FunkClient.AnfrageAsync: ohne lesbare Fassung gilt der Server als neu genug.
            ServerS2 = e.Welcome is { } w && Versionen.ServerKennt(w.Server?.Version, Nachrichtentyp.PhaseSchalten),
            Phasen = Phasen(netzListe),
            LaufendePhase = e.Phase is { } ph
                ? new LaufendePhaseInfo(ph.Nr, ph.Name, ph.Kurz, ph.Durch, DateTimeOffset.FromUnixTimeMilliseconds(ph.Seit))
                : null,
            KontoVersion = m?.KontoVersion ?? 0,
            UmstellungPhase = m?.Phase,
            Rolle = m?.Rolle,
            Gesperrt = Sperren(m?.Gesperrt),
            // Klangstufe C2b: Stummschaltungen durch Einsatzleitung oder Admin (0x0034) für den Mikro-Knopf; ob sie noch gelten, rechnet die Oberfläche.
            Stumm = m?.Stumm is { } stumm ? [.. stumm.Select(s => new StummschaltungInfo(s.NetzId, DateTimeOffset.FromUnixTimeMilliseconds(s.Bis), s.Durch))] : [],
            Abgeglichen = e.Vollstaendig,
            // Wie FunkClient.AnfrageAsync: ohne lesbare Fassung gilt der Server als neu genug.
            ServerKanaele = e.Welcome is { } wk && Versionen.ServerKennt(wk.Server?.Version, Nachrichtentyp.RolleWaehlen),
            // Meldesystem (0.12.0): wie oben nach der Fassung in Welcome; der Stand selbst kommt aus den Pushes 0x4005 und 0x4006.
            ServerMeldungen = e.Welcome is { } wm && Versionen.ServerKennt(wm.Server?.Version, Nachrichtentyp.MeldungSenden),
            Meldestand = e.Meldestand,
            BereitStand = e.BereitStand,
        };
    }

    /// <summary>Phasen aus der NetzListe (flach mit rolleId, 6.14) nach Rollen geordnet.</summary>
    private static IReadOnlyList<PhaseInfo> Phasen(NetzListe? liste) => liste?.Phasen is not { Count: > 0 } phasen
        ? []
        : [.. phasen.OrderBy(p => p.Nr).Select(p => new PhaseInfo(p.Nr, p.Name, p.Kurz,
            (p.Felder ?? []).GroupBy(f => f.RolleId).ToDictionary(g => g.Key,
                g => (IReadOnlyList<RollenfeldInfo>)[.. g.OrderBy(f => f.Feld).Select(f => new RollenfeldInfo(f.Feld, f.NetzId, f.Schiffsnetz, f.Hoeren, f.Senden, f.SendenStandard, f.Fest))])))];

    private static FeldInfo Feld(Mitgliedschaft f) => new(f.Feld, f.NetzId, f.Hoeren, f.Senden, f.Fest);

    /// <summary>Details für die Einsatzleitung (F4): nur, wenn der Server sie schickt (an ZUWEISEN, erkennbar an den Feldern).</summary>
    private static FunkerDetails? Details(PraesenzEintrag p) => p.Felder is null
        ? null
        : new FunkerDetails([.. p.Felder.OrderBy(f => f.Feld).Select(Feld)], p.SendetIn ?? [], p.Stumm ?? [])
        {
            Verbindung = p.Verbindung,
            Guete = p.Guete,
            Quelle = p.Quelle,
            Durch = p.Durch,
            Seit = p.Seit,
            LetzterPlatz = p.LetzterPlatz is { } lp ? (lp.SchiffId, lp.RolleId) : null,
            AlsFunker = p.AlsFunker == true,
            Gesperrt = Sperren(p.Gesperrt),
        };

    /// <summary>Kanalsperren (6.17) vom Draht in die Lage; fehlt die Liste, ist sie leer.</summary>
    private static IReadOnlyList<KanalsperreInfo> Sperren(IReadOnlyList<Kanalsperre>? sperren) => sperren is null
        ? []
        : [.. sperren.Select(s => new KanalsperreInfo(s.NetzId, DateTimeOffset.FromUnixTimeMilliseconds(s.Bis), s.Durch, s.Alle == true))];

    /// <summary>4.6 verdichtet für die Anzeige.</summary>
    public static Verbindungsstatus Status(ClientZustand z) => z switch
    {
        ClientZustand.Getrennt => Verbindungsstatus.Getrennt,
        ClientZustand.Verbinden => Verbindungsstatus.Verbinden,
        ClientZustand.Registrierung => Verbindungsstatus.Registrierung,
        ClientZustand.Warteraum => Verbindungsstatus.Warteraum,
        ClientZustand.Sync => Verbindungsstatus.Abgleich,
        ClientZustand.AktivTunnel => Verbindungsstatus.AktivTunnel,
        _ => Verbindungsstatus.Aktiv,
    };

    private static string Schiffsnetz(string? kennung) =>
        string.IsNullOrEmpty(kennung) ? "Schiffskanal" : char.ToUpper(kennung[0], CultureInfo.CurrentCulture) + kennung[1..] + " (Schiff)";
}
