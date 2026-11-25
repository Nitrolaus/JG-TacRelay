// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Ausbaustufe S2 in der Attrappe (RAID-S2-ENTWURF): "Heute als Funker" (nur ADMIN, bis 04:00, ein zweites Einschalten verlängert
/// nicht) und Phasen schalten wie der Server (6.14: nur mit ZUWEISEN, <c>erwartet</c> muss die laufende sein, sonst VERALTET mit
/// <c>{nr, durch}</c>; umgestellt werden Konten mit Platz, deren Rolle in der alten oder neuen Phase eine Variante hat, außer Admins
/// ohne Funker-Modus, wenn ein Nicht-Admin schaltet). Dazu Ereignisse für die Meldezeile.
/// </summary>
public sealed partial class FunkDienstAttrappe
{
    public event Action<FunkEreignis>? Ereignis;

    /// <summary>Löst ein Ereignis aus wie der Funkbetrieb (Tests, Vorführung).</summary>
    public void EreignisMelden(FunkEreignis e) => Ereignis?.Invoke(e);

    /// <summary>Hält die nächste Phasenschaltung an, bis der Test sie freigibt ("keine Antwort").</summary>
    public TaskCompletionSource? PhaseHalten { get; set; }

    /// <summary>Ist er gesetzt, war ein anderer Leiter schneller: Das nächste Schalten kommt mit VERALTET zurück, und diese Phase läuft.</summary>
    public (byte Nr, uint Durch)? PhaseVeraltet { get; set; }

    public Task<Dienstantwort<FunkermodusAntwort>> FunkermodusAsync(bool an)
    {
        Merken("Funkermodus " + (an ? "an" : "aus"));
        if (Scheitern() is { } f) return Task.FromResult(Dienstantwort.Schlecht<FunkermodusAntwort>(f));
        if (Lage.Konto is not { IstAdmin: true } ich)
            return Task.FromResult(Dienstantwort.Schlecht<FunkermodusAntwort>(FunkClientDienst.Fehlertext(Fehlercode.KeinRecht), Fehlercode.KeinRecht));
        DateTimeOffset? bis = an ? ich.AlsFunkerBis ?? DateTimeOffset.FromUnixTimeMilliseconds(Auftragszeit.NaechsterMorgen(Uhr().ToUnixTimeMilliseconds())) : null;
        LageSetzen(l => l with { Konto = l.Konto! with { AlsFunkerBis = bis }, KontoVersion = l.KontoVersion + 1 });
        return Task.FromResult(Dienstantwort.Gut(new FunkermodusAntwort { An = an, Bis = bis?.ToUnixTimeMilliseconds() }));
    }

    public async Task<Dienstantwort<PhaseSchaltenAntwort>> PhaseSchaltenAsync(byte nr, byte erwartet)
    {
        Merken("Phase " + nr.ToString(CultureInfo.InvariantCulture) + " erwartet " + erwartet.ToString(CultureInfo.InvariantCulture));
        if (PhaseHalten is { } halten)
        {
            PhaseHalten = null;
            await halten.Task.ConfigureAwait(false);
        }
        if (Scheitern() is { } f) return Dienstantwort.Schlecht<PhaseSchaltenAntwort>(f);
        var l = Lage;
        if (l.Konto is not { } ich || !ich.Hat(Recht.Zuweisen))
            return Dienstantwort.Schlecht<PhaseSchaltenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.KeinRecht), Fehlercode.KeinRecht);
        if (PhaseVeraltet is { } schneller)
        {
            PhaseVeraltet = null;
            var p = l.PhaseVon(schneller.Nr);
            LageSetzen(x => x with { LaufendePhase = new LaufendePhaseInfo(schneller.Nr, p?.Name ?? "", p?.Kurz ?? "", schneller.Durch, Uhr()) });
            return Dienstantwort.Schlecht<PhaseSchaltenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.Veraltet), Fehlercode.Veraltet) with
            {
                Details = PhaseDetails(schneller.Nr, schneller.Durch),
            };
        }
        if (erwartet != l.PhaseNr)
        {
            return Dienstantwort.Schlecht<PhaseSchaltenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.Veraltet), Fehlercode.Veraltet) with
            {
                Details = PhaseDetails(l.PhaseNr, l.LaufendePhase?.Durch ?? 0),
            };
        }
        if (l.PhaseVon(nr) is not { } neu || l.PhaseVon(l.PhaseNr) is not { } alt)
            return Dienstantwort.Schlecht<PhaseSchaltenAntwort>(FunkClientDienst.Fehlertext(Fehlercode.Unbekannt), Fehlercode.Unbekannt);
        if (nr == l.PhaseNr) return Dienstantwort.Gut(new PhaseSchaltenAntwort { Nr = nr, Umgestellt = 0, Ausgelassen = [], ZustandsVersion = l.ZustandsVersion });

        var rollen = alt.Varianten.Keys.Union(neu.Varianten.Keys).ToHashSet();
        var umgestellt = new List<uint>();
        var ausgelassen = new List<uint>();
        var funker = new List<FunkerInfo>();
        foreach (var x in l.Funker)
        {
            if (x.RolleId is not { } r || !rollen.Contains(r) || l.RolleVon(r) is not { } rolle || !(x.Online || x.Platz is not null))
            {
                funker.Add(x);
                continue;
            }
            var istAdmin = Stufen.GetValueOrDefault(x.KontoId, Stufe.Funker) == Stufe.Admin || x.Zeichen.Contains(Design.Zeichensatz.Admin);
            if (istAdmin && x.Details?.AlsFunker != true && ich.Stufe != Stufe.Admin)
            {
                ausgelassen.Add(x.KontoId);
                funker.Add(x);
                continue;
            }
            var felder = FelderAus(neu.FelderVon(rolle), l.SchiffVon(x.SchiffId));
            umgestellt.Add(x.KontoId);
            funker.Add(x with
            {
                Details = (x.Details ?? new FunkerDetails([], [], [])) with
                {
                    Felder = felder,
                    SendetIn = [.. felder.Where(fe => fe.Senden).Take(1).Select(fe => fe.NetzId)],
                },
            });
        }
        var version = l.ZustandsVersion + 1;
        LageSetzen(x =>
        {
            var y = x with
            {
                Funker = funker,
                LaufendePhase = new LaufendePhaseInfo(nr, neu.Name, neu.Kurz, ich.Id, Uhr()),
                ZustandsVersion = version,
            };
            // Die eigene Rolle mit Variante: eigene Felder und Sendevorschlag wie beim Server (Regel 7, vereinfacht).
            if (x.RolleId is { } eigen && rollen.Contains(eigen) && x.RolleVon(eigen) is { } er)
            {
                var neueFelder = FelderAus(neu.FelderVon(er), x.SchiffVon(x.SchiffId));
                var vorschlag = neu.FelderVon(er).FirstOrDefault(rf => rf.Sendevorschlag)?.Feld;
                y = y with
                {
                    Felder = neueFelder,
                    SendeFelder = vorschlag is { } v ? [v] : x.SendeFelder,
                    KontoVersion = x.KontoVersion + 1,
                    UmstellungPhase = nr,
                };
            }
            return y;
        });
        return Dienstantwort.Gut(new PhaseSchaltenAntwort { Nr = nr, Umgestellt = (ushort)umgestellt.Count, Ausgelassen = ausgelassen, ZustandsVersion = version });
    }

    /// <summary><c>details</c> von VERALTET auf PhaseSchalten wie beim Server: <c>{nr, durch}</c>.</summary>
    private static System.Text.Json.JsonElement PhaseDetails(byte nr, uint durch)
    {
        using var d = System.Text.Json.JsonDocument.Parse(string.Create(CultureInfo.InvariantCulture, $$"""{"nr":{{nr}},"durch":{{durch}}}"""));
        return d.RootElement.Clone();
    }

    /// <summary>Rollenfelder einer Phase auf einer Einheit aufgelöst (Einheitsnetze über die Kennungen).</summary>
    private static IReadOnlyList<FeldInfo> FelderAus(IReadOnlyList<RollenfeldInfo> felder, SchiffInfo? einheit) =>
        [.. felder.Select(rf => (rf, Netz: rf.NetzId ?? (einheit is not null && rf.Kennung is not null && einheit.Netze.TryGetValue(rf.Kennung, out var n) ? n : (ushort)0)))
            .Where(x => x.Netz != 0).Select(x => new FeldInfo(x.rf.Feld, x.Netz, x.rf.Hoeren, x.rf.Senden, x.rf.Fest)).OrderBy(x => x.Feld)];

    /// <summary>
    /// Phasen des Beispielabends: 1 Anflug (Grundstellung), 2 Entern (Marine sendet in Trupp Alpha und hört Crew, der CO hört Trupp
    /// Alpha mit; Pilot und Gunner bleiben), 3 Rückzug (Marine und Gunner hören Kommando mit).
    /// </summary>
    public static IReadOnlyList<PhaseInfo> RaidPhasen(IReadOnlyList<RolleInfo> rollen)
    {
        ArgumentNullException.ThrowIfNull(rollen);
        RollenfeldInfo K(byte feld, string kennung, bool senden, bool vorschlag = false) => new(feld, null, kennung, true, senden, vorschlag, false);
        RollenfeldInfo F(byte feld, ushort netz, bool senden, bool vorschlag = false) => new(feld, netz, null, true, senden, vorschlag, false);
        var co = rollen.First(r => r.Id == Kapitaen);
        return
        [
            new PhaseInfo(1, "Anflug", "ANFLUG", new Dictionary<ushort, IReadOnlyList<RollenfeldInfo>>()),
            new PhaseInfo(2, "Entern", "ENTERN", new Dictionary<ushort, IReadOnlyList<RollenfeldInfo>>
            {
                [Marine] = [K(0, "CREW", false), K(1, "DURCHSAGEN", false), F(2, 41, true, true)],
                [Kapitaen] = [.. co.Felder, F(4, 41, false)],
            }),
            new PhaseInfo(3, "Rückzug", "RÜCKZUG", new Dictionary<ushort, IReadOnlyList<RollenfeldInfo>>
            {
                [Marine] = [K(0, "CREW", true, true), K(1, "DURCHSAGEN", false), F(2, NetzKommando, false)],
                [Gunner] = [K(0, "CREW", true, true), K(1, "BRUECKE", false), K(2, "DURCHSAGEN", false), F(3, NetzKommando, false)],
            }),
        ];
    }
}
