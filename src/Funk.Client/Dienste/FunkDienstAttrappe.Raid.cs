// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Stufe 2 in der Attrappe (RAIDLEITUNG-ENTWURF, M4, M5, Anhang A): Einsteigen "wie letztes Mal", Verschieben mit Kapazität am
/// Endzustand, Rangregel und VERALTET, Stummschalten, Plätze freigeben, Selbstwahl sperren, Kontenverzeichnis, Auftrag abgeben.
/// Sie verhält sich wie ein freundlicher Server, ohne Netz, und schreibt jede Anfrage in <see cref="Aufrufe"/>.
/// </summary>
public sealed partial class FunkDienstAttrappe
{
    private readonly Dictionary<uint, long> _kontoVersion = [];
    private long _version = 100;

    /// <summary>Konten ohne Sitzung und ohne Platz, die das Kontenverzeichnis zusätzlich nennt ("Alle Mitglieder").</summary>
    public List<VerzeichnisEintrag> OhnePraesenz { get; } = [];

    /// <summary>Stufen der Konten fürs Kontenverzeichnis (sonst FUNKER).</summary>
    public Dictionary<uint, Stufe> Stufen { get; } = [];

    /// <summary>Ist er gesetzt, kommt das nächste Verschieben mit VERALTET zurück (jemand war schneller).</summary>
    public bool NaechstesVeraltet { get; set; }

    /// <summary>Hält die nächste Verschiebung an, bis der Test sie freigibt (Anzeige "wartet").</summary>
    public TaskCompletionSource? VerschiebenHalten { get; set; }

    /// <summary>Die Uhr der Attrappe für Fristen (Sperre 12 h); ohne Angabe die echte.</summary>
    public Func<DateTimeOffset> Uhr { get; set; } = () => DateTimeOffset.Now;

    public Task<string?> WieLetztesMalAsync()
    {
        Merken("WieLetztesMal");
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        if (Lage.LetzterStand is not { } ls) return Task.FromResult<string?>("Es gibt noch keinen letzten Platz.");
        LageSetzen(l => l with
        {
            SchiffId = ls.SchiffId,
            RolleId = ls.RolleId,
            Platz = PlatzStatus.Belegt,
            Quelle = MitgliedschaftsQuelle.Satz,
            QuelleDurch = null,
            Felder = ls.Felder,
            SendeFelder = ls.SendeFelder,
        });
        return Task.FromResult<string?>(null);
    }

    public async Task<Dienstantwort<VerschiebenAntwort>> VerschiebenAsync(IReadOnlyList<Verschiebung> aenderungen, long? basisVersion = null)
    {
        ArgumentNullException.ThrowIfNull(aenderungen);
        Merken("Verschieben " + string.Join("; ", aenderungen.Select(Kurzform)) + (basisVersion is { } b ? " @" + b.ToString(CultureInfo.InvariantCulture) : ""));
        if (VerschiebenHalten is { } halten)
        {
            VerschiebenHalten = null;
            await halten.Task.ConfigureAwait(false);
        }
        if (Scheitern() is { } f) return Dienstantwort.Schlecht<VerschiebenAntwort>(f);
        var l = Lage;
        if (l.Konto is not { } ich || !ich.Hat(Recht.Zuweisen)) return Dienstantwort.Schlecht<VerschiebenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.KeinRecht), Fehlercode.KeinRecht);
        if (NaechstesVeraltet || (basisVersion is { } basis && aenderungen.Any(a => _kontoVersion.GetValueOrDefault(a.KontoId) > basis)))
        {
            NaechstesVeraltet = false;
            return Dienstantwort.Schlecht<VerschiebenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.Veraltet), Fehlercode.Veraltet);
        }
        var funker = l.Funker.ToDictionary(x => x.KontoId);
        foreach (var a in aenderungen)
        {
            if (!funker.ContainsKey(a.KontoId) && OhnePraesenz.FirstOrDefault(v => v.KontoId == a.KontoId) is { } v)
                funker[a.KontoId] = new FunkerInfo(v.KontoId, v.Name, false, null, null) { ScName = v.ScName, DiscordName = v.DiscordName };
            if (!funker.TryGetValue(a.KontoId, out var ziel)) return Dienstantwort.Schlecht<VerschiebenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.Unbekannt), Fehlercode.Unbekannt);
            if (Stufen.GetValueOrDefault(a.KontoId, Stufe.Funker) == Stufe.Admin && ziel.Details?.AlsFunker != true && ich.Stufe != Stufe.Admin)
                return Dienstantwort.Schlecht<VerschiebenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.KeinRecht), Fehlercode.KeinRecht);
            funker[a.KontoId] = Anwenden(l, ziel, a, ich.Id);
        }
        // Kapazität am Endzustand der ganzen Anfrage (4.5): ein Tausch geht.
        foreach (var s in l.Schiffe)
        {
            foreach (var p in s.Plaetze)
            {
                if (funker.Values.Count(x => x.SchiffId == s.Id && x.RolleId == p.RolleId && (x.Online || x.Platz is not null)) > p.Kapazitaet)
                    return Dienstantwort.Schlecht<VerschiebenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.PlatzBelegt), Fehlercode.PlatzBelegt);
            }
        }
        foreach (var r in l.Rollen.Where(r => !r.BrauchtSchiff && r.Kapazitaet is not null))
        {
            if (funker.Values.Count(x => x.SchiffId is null && x.RolleId == r.Id && (x.Online || x.Platz is not null)) > r.Kapazitaet)
                return Dienstantwort.Schlecht<VerschiebenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.PlatzBelegt), Fehlercode.PlatzBelegt);
        }
        var version = ++_version;
        foreach (var a in aenderungen) _kontoVersion[a.KontoId] = version;
        OhnePraesenz.RemoveAll(v => aenderungen.Any(a => a.KontoId == v.KontoId && a.RolleId is not 0));
        LageSetzen(x => Belegungsstand(x with { Funker = [.. funker.Values.Where(y => y.Online || y.Platz is not null).OrderBy(y => y.Name, StringComparer.CurrentCultureIgnoreCase)], ZustandsVersion = version }));
        return Dienstantwort.Gut(new VerschiebenAntwort
        {
            ZustandsVersion = version,
            Offline = [.. aenderungen.Where(a => !funker[a.KontoId].Online).Select(a => a.KontoId)],
            Verdraengt = [],
        });
    }

    /// <summary>Eine Änderung auf eine Person: Rolle und Einheit, dann die Felder der Rolle und die Zusatznetze (4.5).</summary>
    private static FunkerInfo Anwenden(FunkLage l, FunkerInfo f, Verschiebung a, uint durch)
    {
        var details = f.Details ?? new FunkerDetails([], [], []);
        var felder = details.Felder.ToList();
        var (schiff, rolle, platz) = (f.SchiffId, f.RolleId, f.Platz);
        var letzter = details.LetzterPlatz;
        var quelle = details.Quelle;
        if (a.RolleId is 0)
        {
            if (f.RolleId is not null) letzter = (f.SchiffId, f.RolleId);
            (schiff, rolle, platz, quelle) = (null, null, null, MitgliedschaftsQuelle.Standard);
            felder = [];
        }
        else if (a.RolleId is { } r && l.RolleVon(r) is { } ro)
        {
            schiff = a.SchiffId is { } s and not 0 ? s : null;
            rolle = r;
            platz = f.Online ? PlatzStatus.Belegt : PlatzStatus.Reserviert;
            quelle = MitgliedschaftsQuelle.Verschieben;
            var einheit = l.SchiffVon(schiff);
            felder = [.. ro.Felder.Select(rf => (rf, Netz: rf.NetzId ?? (einheit is not null && rf.Kennung is not null && einheit.Netze.TryGetValue(rf.Kennung, out var n) ? n : (ushort)0)))
                .Where(x => x.Netz != 0).Select(x => new FeldInfo(x.rf.Feld, x.Netz, x.rf.Hoeren, x.rf.Senden, x.rf.Fest))];
        }
        foreach (var p in a.FelderPatch ?? [])
        {
            if (p.Leeren == true)
            {
                felder.RemoveAll(x => x.Feld == p.Feld);
                continue;
            }
            var netz = p.NetzId!.Value;
            if (p.Feld is { } nummer)
            {
                // Mit Feldnummer wie am Server (Zustandsschreiber.Zuweisen): Das Netz wandert in genau dieses Feld (Ch. je Person, 0.11.0).
                felder.RemoveAll(x => x.NetzId == netz || x.Feld == nummer);
                felder.Add(new FeldInfo(nummer, netz, p.HoerenWirksam, p.SendenWirksam, p.FestWirksam));
                continue;
            }
            var i = felder.FindIndex(x => x.NetzId == netz);
            var feld = i >= 0 ? felder[i].Feld : (byte)Enumerable.Range(0, 16).First(n => felder.TrueForAll(x => x.Feld != n));
            if (i >= 0) felder.RemoveAt(i);
            felder.Add(new FeldInfo(feld, netz, p.HoerenWirksam, p.SendenWirksam, p.FestWirksam));
        }
        return f with
        {
            SchiffId = schiff,
            RolleId = rolle,
            Platz = platz,
            Details = details with
            {
                Felder = [.. felder.OrderBy(x => x.Feld)],
                Quelle = quelle,
                Durch = quelle == MitgliedschaftsQuelle.Verschieben ? durch : null,
                LetzterPlatz = letzter,
            },
        };
    }

    private static string Kurzform(Verschiebung a)
    {
        var s = a.KontoId.ToString(CultureInfo.InvariantCulture) + ":" + (a.RolleId?.ToString(CultureInfo.InvariantCulture) ?? "-") + "/"
            + (a.SchiffId?.ToString(CultureInfo.InvariantCulture) ?? "-");
        foreach (var p in a.FelderPatch ?? [])
        {
            s += p.Leeren == true
                ? " -f" + p.Feld?.ToString(CultureInfo.InvariantCulture)
                : " +" + p.NetzId?.ToString(CultureInfo.InvariantCulture) + (p.SendenWirksam ? "s" : "h") + (p.Feld is { } f ? "#f" + f.ToString(CultureInfo.InvariantCulture) : "");
        }
        return s;
    }

    /// <summary>Belegung und Zähler aus den Personen, wie der Server sie zählt (K1: BELEGT und RESERVIERT).</summary>
    public static FunkLage Belegungsstand(FunkLage l)
    {
        ArgumentNullException.ThrowIfNull(l);
        bool Hat(FunkerInfo x) => x.Online || x.Platz is not null;
        var schiffe = l.Schiffe.Select(s => s with
        {
            Plaetze = [.. s.Plaetze.Select(p => p with
            {
                Belegt = l.Funker.Count(x => x.SchiffId == s.Id && x.RolleId == p.RolleId && Hat(x)),
                Reserviert = l.Funker.Count(x => x.SchiffId == s.Id && x.RolleId == p.RolleId && !x.Online && x.Platz is not null),
            })],
        }).ToList();
        return l with
        {
            Schiffe = schiffe,
            Rollen = [.. l.Rollen.Select(r => r with { Belegt = l.Funker.Count(x => x.SchiffId is null && x.RolleId == r.Id && Hat(x)) })],
            Online = l.Funker.Count(x => x.Online),
            MitPlatz = l.Funker.Count(x => x.Online && x.RolleId is not null),
        };
    }

    public Task<string?> StummschaltenAsync(uint kontoId, ushort netzId, ushort sekunden)
    {
        Merken("Stumm " + kontoId.ToString(CultureInfo.InvariantCulture) + " " + netzId.ToString(CultureInfo.InvariantCulture) + " " + sekunden.ToString(CultureInfo.InvariantCulture));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        var bis = Uhr().AddSeconds(sekunden).ToUnixTimeMilliseconds();
        LageSetzen(l => l with
        {
            Funker = [.. l.Funker.Select(x => x.KontoId != kontoId || x.Details is not { } d ? x : x with
            {
                Details = d with
                {
                    Stumm = sekunden == 0
                        ? [.. d.Stumm.Where(s => s.NetzId != netzId)]
                        : [.. d.Stumm.Where(s => s.NetzId != netzId), new Stummschaltung { NetzId = netzId, Bis = bis, Durch = l.Konto?.Id ?? 0 }],
                },
            })],
        });
        return Task.FromResult<string?>(null);
    }

    public Task<Dienstantwort<ushort>> PlaetzeFreigebenAsync()
    {
        Merken("PlaetzeFreigeben");
        if (Scheitern() is { } f) return Task.FromResult(Dienstantwort.Schlecht<ushort>(f));
        var frei = Lage.Funker.Where(x => !x.Online && x.Platz is not null).ToList();
        LageSetzen(l => Belegungsstand(l with { Funker = [.. l.Funker.Where(x => x.Online || x.Platz is null)] }));
        foreach (var x in frei)
        {
            OhnePraesenz.Add(new VerzeichnisEintrag
            {
                KontoId = x.KontoId, Name = x.Name, Benutzername = x.Name.ToLowerInvariant(), ScName = x.ScName, DiscordName = x.DiscordName,
                Stufe = Stufen.GetValueOrDefault(x.KontoId, Stufe.Funker), Online = false,
                LetzterPlatz = new LetzterPlatz { SchiffId = x.SchiffId, RolleId = x.RolleId },
            });
        }
        return Task.FromResult(Dienstantwort.Gut((ushort)frei.Count));
    }

    public Task<string?> SelbstwahlSetzenAsync(bool an, bool nachzuegler = true)
    {
        Merken(an ? "Selbstwahl an" : "Selbstwahl aus, Nachzügler " + (nachzuegler ? "ja" : "nein"));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        if (Lage.Konto is not { } ich || !ich.Hat(Recht.Zuweisen)) return Task.FromResult<string?>(FunkClientDienst.Fehlertext(Fehlercode.KeinRecht));
        LageSetzen(l => an
            ? l with { Selbstwahl = true, SelbstwahlDurch = null, SelbstwahlBis = null, Nachzuegler = true, NetzListeVersion = l.NetzListeVersion + 1 }
            : l with { Selbstwahl = false, SelbstwahlDurch = ich.Id, SelbstwahlBis = Uhr().AddHours(12), Nachzuegler = nachzuegler, NetzListeVersion = l.NetzListeVersion + 1 });
        return Task.FromResult<string?>(null);
    }

    public Task<Dienstantwort<IReadOnlyList<VerzeichnisEintrag>>> KontenVerzeichnisAsync()
    {
        Merken("Verzeichnis");
        if (Scheitern() is { } f) return Task.FromResult(Dienstantwort.Schlecht<IReadOnlyList<VerzeichnisEintrag>>(f));
        var l = Lage;
        if (l.Konto is not { } ich || !ich.Hat(Recht.Zuweisen)) return Task.FromResult(Dienstantwort.Schlecht<IReadOnlyList<VerzeichnisEintrag>>(FunkClientDienst.Fehlertext(Fehlercode.KeinRecht), Fehlercode.KeinRecht));
        var liste = l.Funker.Select(x => new VerzeichnisEintrag
        {
            KontoId = x.KontoId, Name = x.Name, Benutzername = x.Name.ToLowerInvariant(), ScName = x.ScName, DiscordName = x.DiscordName,
            Stufe = Stufen.GetValueOrDefault(x.KontoId, Stufe.Funker), Online = x.Online, SchiffId = x.SchiffId, RolleId = x.RolleId, Platz = x.Platz,
            LetzterPlatz = x.Details?.LetzterPlatz is { } lp ? new LetzterPlatz { SchiffId = lp.SchiffId, RolleId = lp.RolleId } : null,
        }).Concat(OhnePraesenz).OrderBy(x => x.KontoId).ToList();
        return Task.FromResult(Dienstantwort.Gut<IReadOnlyList<VerzeichnisEintrag>>(liste));
    }

    public Task<string?> AuftragAbgebenAsync()
    {
        Merken("Abgeben");
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        if (Lage.Konto?.Beauftragung is null) return Task.FromResult<string?>(FunkClientDienst.Fehlertext(Fehlercode.Unbekannt));
        LageSetzen(l => l with { Konto = l.Konto! with { Beauftragung = null } });
        return Task.FromResult<string?>(null);
    }
}
