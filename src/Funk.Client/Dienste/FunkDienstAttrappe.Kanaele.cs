// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Stufe 3 in der Attrappe (STUFE3-ENTWURF 4.3, FUNKPROTOKOLL 6.16 und 6.17): <c>FeldSetzen</c> und <c>RolleWaehlen</c> wie ein
/// freundlicher Server, dazu die Vorführdaten der Vorlage "Star Citizen" mit Max Mustermann: zehn Kanäle mit ihren Ch.-Listen und die
/// 14 Rollen, gebaut aus derselben Vorlage, die der Server übernimmt (<see cref="Flottenvorlagen.StarCitizen"/>), dazu Global ohne Ch.
/// </summary>
public sealed partial class FunkDienstAttrappe
{
    public Task<string?> FeldSetzenAsync(byte feld, ushort? netzId)
    {
        Merken("FeldSetzen " + feld.ToString(CultureInfo.InvariantCulture) + " " + (netzId?.ToString(CultureInfo.InvariantCulture) ?? "-"));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        string? fehler = null;
        LageSetzen(l =>
        {
            var alt = l.FeldVon(feld);
            if (((alt is not null ? l.SperreVon(alt.NetzId) : null) ?? (netzId is { } n0 ? l.SperreVon(n0) : null)) is { } sperre)
            {
                fehler = l.SperrSatz(sperre) + ".";
                return l;
            }
            // Seit 0.8.1 wie der Server (Q44/Q45): selbst nur der Ch.-Wechsel im selben Kanalnetz (auch im festen Feld) und ein offenes
            // Netz in ein freies Feld; ein anderes Netz im belegten Feld und das Leeren lehnt er ab.
            var nichtSelbst = alt is { Fest: true } ? "Festes Feld, nur die Einsatzleitung ändert es." : "Dafür fehlt das Recht.";
            var felder = l.Felder.Where(x => x.Feld != feld).ToList();
            if (netzId is not { } n)
            {
                if (alt is not null) fehler = nichtSelbst;
                return l;
            }
            if (felder.Any(x => x.NetzId == n))
            {
                fehler = "Die Angaben passen nicht (Länge oder Zeichen).";
                return l;
            }
            var netz = l.NetzVon(n);
            if (alt is not null)
            {
                if (netz is not { IstKanalnetz: true } || netz.Id == n || l.NetzVon(alt.NetzId)?.Id != netz.Id)
                {
                    fehler = nichtSelbst;
                    return l;
                }
                felder.Add(alt with { NetzId = n });
            }
            else
            {
                if (netz is null || netz.Offen == NetzOffen.Nein)
                {
                    fehler = "Dafür fehlt das Recht.";
                    return l;
                }
                felder.Add(new FeldInfo(feld, n, true, netz.Offen == NetzOffen.HoerenSenden, false));
            }
            var neu = l with { Felder = [.. felder.OrderBy(x => x.Feld)] };
            // Ein Feld auf Ruhe oder ohne Kanal sendet nicht (6.16): Es fällt aus der Sendeauswahl.
            var bleibt = l.SendeFelder.Where(s => neu.FeldVon(s) is { Senden: true } x && !Kanalrechnung.IstRuhe(x.NetzId)).ToList();
            // 0.9.1 wie der Server: Geht ein Feld von der Ruhe auf einen Ch. und ist nichts gewählt, wird es Sendeziel.
            if (bleibt.Count == 0 && !l.GlobalSenden && alt is not null && Kanalrechnung.IstRuhe(alt.NetzId) && !Kanalrechnung.IstRuhe(n)
                && neu.FeldVon(feld) is { Senden: true } && neu.SperreVon(n) is null)
                bleibt.Add(feld);
            return neu with { SendeFelder = [.. bleibt] };
        });
        return Task.FromResult(fehler);
    }

    public Task<string?> RolleWaehlenAsync(ushort rolleId, IReadOnlyList<ChWunsch> ch)
    {
        ArgumentNullException.ThrowIfNull(ch);
        Merken("RolleWaehlen " + rolleId.ToString(CultureInfo.InvariantCulture) + " ["
            + string.Join(",", ch.Select(w => w.Feld.ToString(CultureInfo.InvariantCulture) + ":" + w.Ch.ToString(CultureInfo.InvariantCulture))) + "]");
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        var l0 = Lage;
        if (l0.RolleVon(rolleId) is not { } rolle) return Task.FromResult<string?>("Das gibt es nicht (mehr).");
        if (!rolle.SelbstWaehlbar) return Task.FromResult<string?>("Dafür fehlt das Recht.");
        LageSetzen(l =>
        {
            var felder = new List<FeldInfo>();
            // Ch., die ein Feld der Rolle fest nennt, nimmt ein freies Feld im selben Kanalnetz nicht (wie der Server seit 19.10.2026).
            var fest = rolle.Felder.Where(r => r.NetzId is not null && r.Ch is not null).Select(r => (r.NetzId!.Value, r.Ch!.Value)).ToHashSet();
            foreach (var rf in rolle.Felder)
            {
                if (rf.NetzId is not { } n || l.NetzVon(n) is not { } netz) continue;
                var id = n;
                if (netz.IstKanalnetz)
                {
                    // Ch.-Auflösung (6.16, seit 0.8.1 ohne Wünsche, Q46): fester Ch., der Ch., den das Konto in diesem Kanalnetz hat, sonst Ruhe.
                    var bisher = l.Felder.OrderBy(x => x.Feld == rf.Feld ? 0 : 1).ThenBy(x => x.Feld)
                        .Select(x => Kanalrechnung.Zerlegen(x.NetzId, out var k, out var c) && k == n && !fest.Contains((n, c)) ? (byte?)c : null).FirstOrDefault(c => c is not null);
                    id = Kanalrechnung.Id(n, rf.Ch ?? bisher ?? Kanalrechnung.Ruhe);
                }
                felder.Add(new FeldInfo(rf.Feld, id, rf.Hoeren, rf.Senden, rf.Fest));
            }
            var neu = l with { Felder = felder, Rolle = rolleId, Quelle = MitgliedschaftsQuelle.Selbst };
            bool Sendbar(FeldInfo x) => x.Senden && !Kanalrechnung.IstRuhe(x.NetzId) && neu.SperreVon(x.NetzId) is null;
            var vorschlag = rolle.Felder.Where(r => r.Sendevorschlag).Select(r => felder.FirstOrDefault(x => x.Feld == r.Feld)).FirstOrDefault(x => x is not null && Sendbar(x))
                ?? felder.FirstOrDefault(Sendbar);
            return neu with { SendeFelder = vorschlag is null ? [] : [vorschlag.Feld], GlobalSenden = false };
        });
        return Task.FromResult<string?>(null);
    }

    /// <summary>Die Sperren für alle (Konto 0), wie der Server sie in LISTE nennt.</summary>
    private readonly List<KanalsperreInfo> _sperrenFuerAlle = [];

    /// <summary>
    /// Kanalsperre wie ein freundlicher Server (6.17): nur mit ZUWEISEN, nie eine Ruhe, Global nur als einzelner Global-Kanal, bis zum nächsten
    /// 04:00 nach der Uhr der Attrappe; die Sperre steht danach in den Details der Person, eine für alle (Konto 0) in
    /// <see cref="SperrenFuerAlleAsync"/>.
    /// </summary>
    public Task<string?> KanalSperreAsync(uint kontoId, ushort netzId, bool sperren)
    {
        Merken("KanalSperre " + (sperren ? "SETZEN " : "AUFHEBEN ") + kontoId.ToString(CultureInfo.InvariantCulture) + " " + netzId.ToString(CultureInfo.InvariantCulture));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        var l0 = Lage;
        if (l0.Konto is not { } ich || !ich.Hat(Recht.Zuweisen)) return Task.FromResult<string?>(FunkClientDienst.Fehlertext(Fehlercode.KeinRecht));
        if (Kanalrechnung.IstRuhe(netzId) || netzId == Kanalrechnung.GlobalSelbstId || l0.NetzVon(netzId) is not { } ziel || (ziel.IstGlobal && !Kanalrechnung.IstKanalbereich(netzId)))
            return Task.FromResult<string?>(FunkClientDienst.Fehlertext(Fehlercode.Ungueltig));
        var jetzt = Uhr();
        var vier = new DateTimeOffset(DateTime.SpecifyKind(jetzt.LocalDateTime.Date.AddHours(4), DateTimeKind.Local));
        if (vier <= jetzt) vier = vier.AddDays(1);
        if (kontoId == 0)
        {
            lock (_sperrenFuerAlle)
            {
                var vorher = _sperrenFuerAlle.RemoveAll(s => s.NetzId == netzId);
                if (!sperren && vorher == 0) return Task.FromResult<string?>(FunkClientDienst.Fehlertext(Fehlercode.Unbekannt));
                if (sperren) _sperrenFuerAlle.Add(new KanalsperreInfo(netzId, vier, ich.Id, true));
            }
            return Task.FromResult<string?>(null);
        }
        LageSetzen(l => l with
        {
            Funker = [.. l.Funker.Select(x => x.KontoId != kontoId || x.Details is not { } d ? x : x with
            {
                Details = d with
                {
                    Gesperrt = sperren
                        ? [.. d.Gesperrt.Where(s => s.NetzId != netzId), new KanalsperreInfo(netzId, vier, ich.Id, false)]
                        : [.. d.Gesperrt.Where(s => s.NetzId != netzId)],
                },
            })],
        });
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// LISTE der Sperren für alle wie ein freundlicher Server. Nur gelesen, nicht mitgeschrieben (<see cref="Aufrufe"/>): Das Brett holt sie
    /// beim Zeigen und alle 10 s, das soll die Aufrufliste der Tests nicht verschieben.
    /// </summary>
    public Task<Dienstantwort<IReadOnlyList<KanalsperreInfo>>> SperrenFuerAlleAsync()
    {
        if (Lage.Konto is not { } ich || !ich.Hat(Recht.Zuweisen))
            return Task.FromResult(Dienstantwort.Schlecht<IReadOnlyList<KanalsperreInfo>>(FunkClientDienst.Fehlertext(Fehlercode.KeinRecht), Fehlercode.KeinRecht));
        lock (_sperrenFuerAlle) return Task.FromResult(Dienstantwort.Gut<IReadOnlyList<KanalsperreInfo>>([.. _sperrenFuerAlle]));
    }

    /// <summary>
    /// Die NetzListe der Vorlage "Star Citizen" (zehn Kanäle mit Ch., 14 Rollen), wie ein Server sie nach der Übernahme schickt: aus denselben
    /// Schritten gebaut, dazu Global ohne Ch., das die Vorlage nicht ändert.
    /// </summary>
    public static NetzListe StarCitizenListe()
    {
        var netze = new List<Netz>
        {
            new()
            {
                Id = Grenzen.GlobalNetzId, Name = "Global", Typ = NetzTyp.Global, Reihenfolge = 0, NurTaste = true, AusloeserStandard = Ausloeser.Taste,
                Offen = NetzOffen.Nein, Standard = false,
            },
        };
        var rollen = new List<Rolle>();
        foreach (var s in Flottenvorlagen.StarCitizen().Paket.Ops)
        {
            if (s.Op == VerwaltungsOperation.NetzSetzen) netze.Add(s.Objekt.Deserialize(SteuerJson.Info<Netz>())!);
            else if (s.Op == VerwaltungsOperation.RolleSetzen) rollen.Add(s.Objekt.Deserialize(SteuerJson.Info<Rolle>())!);
        }
        return new NetzListe
        {
            Version = 7,
            Einstellungen = new NetzEinstellungen { DurchsageNurTaste = true, Selbstwahl = true },
            Netze = netze,
            Schiffe = [],
            Rollen = rollen,
        };
    }

    /// <summary>
    /// Vorführung und Tests mit Kanälen (Namen erfunden, Max Mustermann): gewählt ist "Kapitän" aus der Vorlage Star Citizen. Brücke und Crew
    /// stehen auf Schiff 3, Brücke sendet (Kobold spricht), Flotte auf Allgemein (Falke spricht), Kommando auf Flottenführung, Sanität liegt auf
    /// Ruhe, Logistik (Fracht) ist aus; dazu selbst gelegt Allgemein auf Reserve 1 (von Nordlicht bis 04:00 gesperrt) und Bergung und Bergbau
    /// auf Bergbau (Kranich spricht). Feld 8 ist leer. Sanität auf Ruhe (die Rolle legt sie auf Rettung) und die beiden selbst gelegten Felder
    /// weichen von der Rolle ab; oben links steht darum "3 Kapitän (geändert)". So zeigt das Beispiel Ruhe, Aus, Sperre und Abweichung zugleich.
    /// </summary>
    public static FunkLage KanalBeispiel()
    {
        var liste = StarCitizenListe();
        ushort K(string name, byte ch) => Kanalrechnung.Id(liste.Netze.Single(n => n.Name == name).Id, ch);
        static Mitgliedschaft F(byte feld, ushort netz, bool hoeren = true) => new() { Feld = feld, NetzId = netz, Hoeren = hoeren, Senden = true, Fest = false };
        var bis = new DateTimeOffset(DateTime.SpecifyKind(Abend.Date.AddDays(1).AddHours(4), DateTimeKind.Local));
        var m = new Mitgliedschaften
        {
            ZustandsVersion = 41,
            KontoVersion = 12,
            Felder =
            [
                F(0, K("Brücke", 3)), F(1, K("Crew", 3)), F(2, K("Flotte", 1)), F(3, K("Kommando", 1)), F(4, K("Sanität", 0)),
                F(5, K("Logistik", 1), hoeren: false), F(6, K("Allgemein", 2)), F(8, K("Bergung und Bergbau", 2)),
            ],
            Global = new GlobalAngabe { Senden = true },
            Sendeauswahl = new Sendeauswahl { Felder = [0], Global = false },
            Stumm = [],
            Quelle = MitgliedschaftsQuelle.Selbst,
            Rolle = liste.Rollen.Single(r => r.Name == "Kapitän").Id,
            Gesperrt = [new Kanalsperre { NetzId = K("Allgemein", 2), Bis = bis.ToUnixTimeMilliseconds(), Durch = 4 }],
        };
        var eingabe = new LageEingabe(ClientZustand.AktivUdp, null, liste, m, new Dictionary<uint, PraesenzEintrag>(), [0], false, [])
        {
            ZustandsVersion = 41,
            Vollstaendig = true,
        };
        var b = Beispiel();
        return LageAbbildung.Abbilden(eingabe) with
        {
            Server = b.Server,
            Konto = b.Konto,
            Geraet = b.Geraet,
            Schluesselart = b.Schluesselart,
            Funker = b.Funker,
            Sprecher =
            [
                new SprecherInfo(2, "Falke", [K("Flotte", 1)], false), new SprecherInfo(3, "Kobold", [K("Brücke", 3)], false),
                new SprecherInfo(5, "Kranich", [K("Bergung und Bergbau", 2)], false),
            ],
            Selbstwahl = true,
            ServerS2 = true,
            ServerKanaele = true,
        };
    }
}
