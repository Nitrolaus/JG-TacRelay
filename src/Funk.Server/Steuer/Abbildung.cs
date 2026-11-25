// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Server.Lage;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Steuer;

/// <summary>
/// Übersetzung zwischen den Modellen der Module und den Drahtnachrichten des Steuerkanals (4.5). Die drei Module haben
/// eigene Aufzählungen für dieselben Begriffe (Stufe, Recht, Schlüsselart); hier stehen die Zuordnungen an einer Stelle.
/// </summary>
public static class Abbildung
{
    // ---- Aufzählungen ----

    public static S.Stufe Stufe(L.Stufe s) => s switch
    {
        L.Stufe.Admin => S.Stufe.Admin,
        L.Stufe.Raidleiter => S.Stufe.Raidleiter,
        _ => S.Stufe.Funker,
    };

    public static S.Stufe Stufe(I.Stufe s) => s switch
    {
        I.Stufe.Admin => S.Stufe.Admin,
        I.Stufe.Raidleiter => S.Stufe.Raidleiter,
        _ => S.Stufe.Funker,
    };

    public static I.Stufe StufeIdentitaet(S.Stufe s) => s switch
    {
        S.Stufe.Admin => I.Stufe.Admin,
        S.Stufe.Raidleiter => I.Stufe.Raidleiter,
        _ => I.Stufe.Funker,
    };

    public static L.Stufe StufeLage(I.Stufe s) => s switch
    {
        I.Stufe.Admin => L.Stufe.Admin,
        I.Stufe.Raidleiter => L.Stufe.Raidleiter,
        _ => L.Stufe.Funker,
    };

    /// <summary>Beide Module legen die neun Rechte aus 6.1 in derselben Bitfolge ab.</summary>
    public static L.Recht RechtLage(I.Rechte r) => (L.Recht)((int)r & (int)L.Recht.Alle);

    public static I.Rechte RechtIdentitaet(L.Recht r) => (I.Rechte)(int)r;

    /// <summary>Drahtform der Rechte in der Reihenfolge der Tabelle 6.1.</summary>
    public static IReadOnlyList<S.Recht> Rechte(L.Recht r)
    {
        var liste = new List<S.Recht>(9);
        for (var i = 0; i < 9; i++)
            if (((int)r & (1 << i)) != 0) liste.Add((S.Recht)i);
        return liste;
    }

    public static I.Rechte RechteAus(IEnumerable<S.Recht> rechte)
    {
        ArgumentNullException.ThrowIfNull(rechte);
        var r = 0;
        foreach (var x in rechte) r |= 1 << (int)x;
        return (I.Rechte)r;
    }

    public static S.SchluesselArt SchluesselArt(I.SchluesselArt a) => a == I.SchluesselArt.Tpm ? S.SchluesselArt.Tpm : S.SchluesselArt.Software;

    public static I.SchluesselArt SchluesselArtIdentitaet(S.SchluesselArt a) => a == S.SchluesselArt.Tpm ? I.SchluesselArt.Tpm : I.SchluesselArt.Software;

    public static S.GeraeteStatus GeraetStatus(I.GeraetStatusMeldung s) => s switch
    {
        I.GeraetStatusMeldung.Wartet => S.GeraeteStatus.Wartet,
        I.GeraetStatusMeldung.Freigegeben => S.GeraeteStatus.Freigegeben,
        I.GeraetStatusMeldung.Abgelehnt => S.GeraeteStatus.Abgelehnt,
        I.GeraetStatusMeldung.Entfernt => S.GeraeteStatus.Entfernt,
        I.GeraetStatusMeldung.Entzogen => S.GeraeteStatus.Entzogen,
        _ => S.GeraeteStatus.Beantragt,
    };

    public static S.GeraeteStatus GeraetStatus(I.GeraetStatus s) => GeraetStatus((I.GeraetStatusMeldung)(int)s);

    public static I.Entscheidung Entscheidung(S.GeraetEntscheidung e) => e switch
    {
        S.GeraetEntscheidung.Freigeben => I.Entscheidung.Freigeben,
        S.GeraetEntscheidung.Ablehnen => I.Entscheidung.Ablehnen,
        S.GeraetEntscheidung.Bestaetigen => I.Entscheidung.Bestaetigen,
        S.GeraetEntscheidung.Einspruch => I.Entscheidung.Einspruch,
        // ENTSPERREN ist keine Entscheidung über einen Antrag; die Zentrale führt es vorher eigens aus (seit 0.8.0).
        _ => throw new ArgumentOutOfRangeException(nameof(e), e, "keine Entscheidung über einen Antrag"),
    };

    public static S.Markierung? Markierung(I.Markierung m) => m switch
    {
        I.Markierung.Bestaetigt => S.Markierung.Bestaetigt,
        I.Markierung.Einspruch => S.Markierung.Einspruch,
        _ => null,
    };

    public static S.Fehlercode Fehler(L.Fehlercode c) => c switch
    {
        L.Fehlercode.Ungueltig => S.Fehlercode.Ungueltig,
        L.Fehlercode.Unbekannt => S.Fehlercode.Unbekannt,
        L.Fehlercode.KeinRecht => S.Fehlercode.KeinRecht,
        L.Fehlercode.Veraltet => S.Fehlercode.Veraltet,
        L.Fehlercode.PlatzBelegt => S.Fehlercode.PlatzBelegt,
        L.Fehlercode.SelbstwahlGesperrt => S.Fehlercode.SelbstwahlGesperrt,
        L.Fehlercode.FeldFest => S.Fehlercode.FeldFest,
        L.Fehlercode.GlobalPflicht => S.Fehlercode.GlobalPflicht,
        L.Fehlercode.NurAdminWerkzeug => S.Fehlercode.NurAdminWerkzeug,
        L.Fehlercode.KontoGesperrt => S.Fehlercode.KontoGesperrt,
        L.Fehlercode.VerdraengenGesperrt => S.Fehlercode.VerdraengenGesperrt,
        _ => S.Fehlercode.Intern,
    };

    public static S.Fehlercode Fehler(I.Fehlercode c) => c switch
    {
        I.Fehlercode.Ungueltig => S.Fehlercode.Ungueltig,
        I.Fehlercode.Unbekannt => S.Fehlercode.Unbekannt,
        I.Fehlercode.KeinRecht => S.Fehlercode.KeinRecht,
        I.Fehlercode.Grenze => S.Fehlercode.Grenze,
        I.Fehlercode.CodeUngueltig => S.Fehlercode.CodeUngueltig,
        I.Fehlercode.GeraeteLimit => S.Fehlercode.GeraeteLimit,
        I.Fehlercode.NurAdminWerkzeug => S.Fehlercode.NurAdminWerkzeug,
        I.Fehlercode.GeraetGesperrt => S.Fehlercode.GeraetGesperrt,
        I.Fehlercode.KontoGesperrt => S.Fehlercode.KontoGesperrt,
        I.Fehlercode.NameVergeben => S.Fehlercode.NameVergeben,
        I.Fehlercode.AdminSitzungNoetig => S.Fehlercode.AdminSitzungNoetig,
        I.Fehlercode.SchrittBestaetigen => S.Fehlercode.SchrittBestaetigen,
        I.Fehlercode.ZweitfaktorFalsch => S.Fehlercode.ZweitfaktorFalsch,
        I.Fehlercode.ZweitfaktorGesperrt => S.Fehlercode.ZweitfaktorGesperrt,
        I.Fehlercode.AntraegeVoll => S.Fehlercode.AntraegeVoll,
        I.Fehlercode.Veraltet => S.Fehlercode.Veraltet,
        I.Fehlercode.Frist or I.Fehlercode.Abgelehnt => S.Fehlercode.Ungueltig,
        _ => S.Fehlercode.Intern,
    };

    private static S.NetzOffen Offen(L.Offenheit o) => o switch
    {
        L.Offenheit.Hoeren => S.NetzOffen.Hoeren,
        L.Offenheit.HoerenSenden => S.NetzOffen.HoerenSenden,
        _ => S.NetzOffen.Nein,
    };

    private static L.Offenheit OffenLage(S.NetzOffen o) => o switch
    {
        S.NetzOffen.Hoeren => L.Offenheit.Hoeren,
        S.NetzOffen.HoerenSenden => L.Offenheit.HoerenSenden,
        _ => L.Offenheit.Nein,
    };

    private static S.MitgliedschaftsQuelle Quelle(L.Quelle q) => q switch
    {
        L.Quelle.Rolle => S.MitgliedschaftsQuelle.Rolle,
        L.Quelle.Selbst => S.MitgliedschaftsQuelle.Selbst,
        L.Quelle.Verschieben => S.MitgliedschaftsQuelle.Verschieben,
        L.Quelle.Aufstellung => S.MitgliedschaftsQuelle.Aufstellung,
        L.Quelle.Wiederhergestellt => S.MitgliedschaftsQuelle.Wiederhergestellt,
        L.Quelle.Satz => S.MitgliedschaftsQuelle.Satz,
        _ => S.MitgliedschaftsQuelle.Standard,
    };

    // ---- NetzListe (4.5) ----

    public static S.NetzListe NetzListe(L.Schnappschuss z)
    {
        ArgumentNullException.ThrowIfNull(z);
        var e = z.Einstellungen;
        return new S.NetzListe
        {
            Version = z.NetzListeVersion,
            Einstellungen = new S.NetzEinstellungen
            {
                DurchsageNurTaste = e.DurchsageNurTaste,
                Selbstwahl = e.Selbstwahl,
                SelbstwahlDurch = e.Selbstwahl ? null : e.SelbstwahlDurch,
                SelbstwahlBis = e.Selbstwahl || e.SelbstwahlBis == 0 ? null : e.SelbstwahlBis,
                Nachzuegler = e.Nachzuegler,
            },
            Netze = [.. z.Netze.Values.Select(Netz)],
            Schiffe = [.. z.Schiffe.Values.Select(Schiff)],
            Rollen = [.. z.Rollen.Values.Select(Rolle)],
            Phasen = z.Phasen.IsEmpty ? null : Phasen(z),
        };
    }

    /// <summary>Name der Grundstellung, solange der Admin ihr keinen gegeben hat (6.14).</summary>
    public const string GrundstellungName = "Grundstellung";
    public const string GrundstellungKurz = "GRUND";

    /// <summary>
    /// <c>NetzListe.phasen</c> (6.14): die Grundstellung immer zuerst (mit ihrem Namen oder "Grundstellung"), dann die Phasen 2 bis 5.
    /// </summary>
    private static List<S.NetzPhase> Phasen(L.Schnappschuss z)
    {
        var liste = new List<S.NetzPhase>(z.Phasen.Count + 1);
        if (!z.Phasen.ContainsKey(1)) liste.Add(new S.NetzPhase { Nr = 1, Name = GrundstellungName, Kurz = GrundstellungKurz });
        liste.AddRange(z.Phasen.Values.Select(NetzPhase));
        return liste;
    }

    public static S.NetzPhase NetzPhase(L.Einsatzphase p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new S.NetzPhase
        {
            Nr = p.Nr,
            Name = p.Name,
            Kurz = p.Kurz,
            // Flach, jedes Feld mit seiner Rolle (Tiefe 8 aus 4.3, siehe S.NetzPhase).
            Felder = p.Varianten.IsEmpty ? null : [.. p.Varianten.SelectMany(v => v.Felder.Select(f => new S.PhasenFeld
            {
                RolleId = v.RolleId,
                Feld = f.Feld,
                NetzId = f.NetzId == 0 ? null : f.NetzId,
                Schiffsnetz = f.Schiffsnetz,
                Senden = f.Senden,
                Hoeren = f.Hoeren,
                SendenStandard = f.SendenStandard,
                Fest = f.Fest,
                Ch = f.Ch,
            }))],
        };
    }

    /// <summary>Die Felder einer Phase vom Draht, gruppiert nach Rolle in der Folge ihres ersten Auftretens.</summary>
    public static L.Einsatzphase EinsatzphaseLage(S.NetzPhase d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new L.Einsatzphase(d.Nr, d.Name, d.Kurz)
        {
            Varianten = [.. (d.Felder ?? []).GroupBy(f => f.RolleId).Select(g => new L.PhasenVariante(g.Key)
            {
                Felder = [.. g.Select(f => new L.RollenFeld(f.Feld, f.NetzId ?? 0, f.Schiffsnetz, f.Hoeren, f.Senden, f.SendenStandard, f.Fest) { Ch = f.Ch })],
            })],
        };
    }

    /// <summary>
    /// Push <c>PhaseGeschaltet</c> (0x4004) für die laufende Phase; Zeiten in Lage-ms, also Unix-ms. Eine Phase ohne Definition (die
    /// Grundstellung ohne Namen) heißt "Grundstellung".
    /// </summary>
    public static S.PhaseGeschaltet PhaseGeschaltet(L.Schnappschuss z, L.LaufendePhase? laufend)
    {
        ArgumentNullException.ThrowIfNull(z);
        var nr = laufend?.Nr ?? 1;
        var def = z.Phasen.GetValueOrDefault(nr);
        return new S.PhaseGeschaltet
        {
            Nr = nr,
            Name = def?.Name ?? GrundstellungName,
            Kurz = def?.Kurz ?? GrundstellungKurz,
            Durch = laufend?.Durch ?? 0,
            Seit = laufend?.Seit ?? 0,
        };
    }

    public static S.Netz Netz(L.Netz n)
    {
        ArgumentNullException.ThrowIfNull(n);
        return new S.Netz
        {
            Id = n.Id,
            Name = n.Name,
            Typ = n.Typ == L.NetzTyp.Global ? S.NetzTyp.Global : S.NetzTyp.Normal,
            Reihenfolge = n.Reihenfolge,
            NurTaste = n.NurTaste,
            AusloeserStandard = n.AusloeserStandard == L.Ausloeser.Va ? S.Ausloeser.Va : S.Ausloeser.Taste,
            Offen = Offen(n.Offen),
            Standard = n.Standard,
            SchiffId = n.SchiffId == 0 ? null : n.SchiffId,
            Schiffsnetz = n.Schiffsnetz,
            Kanaele = n.Kanaele.IsEmpty ? null : n.Kanaele.ToArray(),
            Gruppe = n.Gruppe,
            Herkunft = n.Herkunft,
        };
    }

    public static L.Netz NetzLage(S.Netz d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new L.Netz(d.Id, d.Name)
        {
            Typ = d.Typ == S.NetzTyp.Global ? L.NetzTyp.Global : L.NetzTyp.Normal,
            Reihenfolge = d.Reihenfolge,
            NurTaste = d.NurTaste,
            AusloeserStandard = d.AusloeserStandard == S.Ausloeser.Va ? L.Ausloeser.Va : L.Ausloeser.Taste,
            Offen = OffenLage(d.Offen),
            Standard = d.Standard,
            SchiffId = d.SchiffId ?? 0,
            Schiffsnetz = d.Schiffsnetz,
            Kanaele = d.Kanaele is { } k ? [.. k] : [],
            Gruppe = d.Gruppe,
            Herkunft = d.Herkunft,
        };
    }

    public static S.Schiff Schiff(L.Schiff s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new S.Schiff
        {
            Id = s.Id,
            Name = s.Name,
            Reihenfolge = s.Reihenfolge,
            Netze = s.Netze,
            Plaetze = [.. s.Plaetze.Select(p => new S.SchiffsPlatz { RolleId = p.RolleId, Kapazitaet = p.Kapazitaet, Mindest = p.Mindest == 0 ? null : p.Mindest })],
            Art = Einheitsart(s.Art),
            Typ = s.Typ,
        };
    }

    public static L.Schiff SchiffLage(S.Schiff d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new L.Schiff(d.Id, d.Name)
        {
            Reihenfolge = d.Reihenfolge,
            Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, d.Netze),
            Plaetze = [.. d.Plaetze.Select(p => new L.SchiffsPlatz(p.RolleId, p.Kapazitaet) { Mindest = p.Mindest ?? 0 })],
            Art = d.Art is { } a ? EinheitsartLage(a) : L.Einheitsart.Schiff,
            Typ = d.Typ,
        };
    }

    public static S.Einheitsart Einheitsart(L.Einheitsart a) => a switch
    {
        L.Einheitsart.Trupp => S.Einheitsart.Trupp,
        L.Einheitsart.Orga => S.Einheitsart.Orga,
        _ => S.Einheitsart.Schiff,
    };

    public static L.Einheitsart EinheitsartLage(S.Einheitsart a) => a switch
    {
        S.Einheitsart.Trupp => L.Einheitsart.Trupp,
        S.Einheitsart.Orga => L.Einheitsart.Orga,
        _ => L.Einheitsart.Schiff,
    };

    public static S.Rolle Rolle(L.Rolle r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new S.Rolle
        {
            Id = r.Id,
            Name = r.Name,
            Reihenfolge = r.Reihenfolge,
            SelbstWaehlbar = r.SelbstWaehlbar,
            BrauchtSchiff = r.BrauchtSchiff,
            Kapazitaet = r.Kapazitaet,
            Felder = [.. r.Felder.Select(RollenFeld)],
            Herkunft = r.Herkunft,
        };
    }

    private static S.RollenFeld RollenFeld(L.RollenFeld f) => new()
    {
        Feld = f.Feld,
        NetzId = f.NetzId == 0 ? null : f.NetzId,
        Schiffsnetz = f.Schiffsnetz,
        Senden = f.Senden,
        Hoeren = f.Hoeren,
        SendenStandard = f.SendenStandard,
        Fest = f.Fest,
        Ch = f.Ch,
    };

    private static L.RollenFeld RollenFeldLage(S.RollenFeld f) => new(f.Feld, f.NetzId ?? 0, f.Schiffsnetz, f.Hoeren, f.Senden, f.SendenStandard, f.Fest) { Ch = f.Ch };

    public static L.Rolle RolleLage(S.Rolle d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new L.Rolle(d.Id, d.Name)
        {
            Reihenfolge = d.Reihenfolge,
            SelbstWaehlbar = d.SelbstWaehlbar,
            BrauchtSchiff = d.BrauchtSchiff,
            Kapazitaet = d.Kapazitaet,
            Felder = [.. d.Felder.Select(RollenFeldLage)],
            Herkunft = d.Herkunft,
        };
    }

    // ---- Mitgliedschaften, Praesenz (4.5) ----

    public static S.Sendeauswahl Sendeauswahl(L.Sendeauswahl a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new S.Sendeauswahl { Felder = [.. a.Felder], Global = a.Global };
    }

    public static L.Sendeauswahl SendeauswahlLage(S.Sendeauswahl a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new L.Sendeauswahl([.. a.Felder], a.Global);
    }

    private static S.Mitgliedschaft Mitglied(L.Mitgliedschaft m) =>
        new() { Feld = m.Feld, NetzId = m.NetzId, Hoeren = m.Hoeren, Senden = m.Senden, Fest = m.Fest };

    private static IReadOnlyList<S.Stummschaltung> Stumm(L.Konto k, long jetzt) =>
        [.. k.Stumm.Where(s => s.Bis > jetzt).Select(s => new S.Stummschaltung { NetzId = s.NetzId, Bis = s.Bis, Durch = s.Durch })];

    /// <summary>Eine Kanalsperre auf dem Draht (6.17): in <c>KanalSperre</c>-Antworten mit <paramref name="kontoId"/> (0 = für alle).</summary>
    public static S.Kanalsperre Kanalsperre(L.Kanalsperre s, uint? kontoId, bool? alle = null) =>
        new() { NetzId = s.NetzId, Bis = s.Bis, Durch = s.Durch, KontoId = kontoId, Alle = alle };

    /// <summary><c>Mitgliedschaften.gesperrt</c>: die Sperren, die das Konto gerade treffen, eigene und für alle; ohne Sperre fehlt das Feld.</summary>
    private static IReadOnlyList<S.Kanalsperre>? Gesperrt(L.Schnappschuss z, L.Konto k, long jetzt)
    {
        var liste = L.Sperrregeln.Wirksam(k, k.RechteZu(jetzt), z.SperrenAlle, jetzt);
        return liste.Count == 0 ? null : [.. liste.Take(S.Kanalrechnung.GesperrtMax).Select(x => Kanalsperre(x.Sperre, null, x.Alle ? true : null))];
    }

    /// <summary>Praesenz-Details (6.17): die eigenen Sperren des Kontos; ohne Sperre fehlt das Feld.</summary>
    private static IReadOnlyList<S.Kanalsperre>? EigeneSperren(L.Konto k, long jetzt) =>
        k.Sperren.Any(s => s.Bis > jetzt) ? [.. k.Sperren.Where(s => s.Bis > jetzt).Select(s => Kanalsperre(s, null))] : null;

    private static S.PlatzStatus? Platz(L.PlatzZustand p) => p switch
    {
        L.PlatzZustand.Belegt => S.PlatzStatus.Belegt,
        L.PlatzZustand.Reserviert => S.PlatzStatus.Reserviert,
        _ => null,
    };

    /// <summary>
    /// Eigene Mitgliedschaften (4.5), immer vollständig. Global steht nie in den Feldern. <paramref name="konto"/>: die
    /// eigenen Kontoangaben (F3, F13), die das Kontenbuch, das Mitgliederbuch und das Adminbuch liefern. <paramref name="phase"/>:
    /// nur wenn der Stand aus einem Phasenwechsel kommt (6.14).
    /// </summary>
    public static S.Mitgliedschaften Mitgliedschaften(L.Schnappschuss z, L.Komfortzustand komfort, L.Konto k, long jetzt, S.KontoStand? konto = null, byte? phase = null)
    {
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(komfort);
        ArgumentNullException.ThrowIfNull(k);
        var platz = z.PlatzZustandVon(k.Id, jetzt);
        return new S.Mitgliedschaften
        {
            ZustandsVersion = z.ZustandsVersion,
            KontoVersion = k.KontoVersion,
            SchiffId = k.Platz is { SchiffId: not 0 } p ? p.SchiffId : null,
            RolleId = k.Platz?.RolleId,
            Platz = k.Platz is null ? null : Platz(platz),
            Felder = [.. k.Felder.Select(Mitglied)],
            Global = new S.GlobalAngabe { Senden = k.RechteZu(jetzt).Hat(L.Recht.GlobalSenden) },
            Sendeauswahl = Sendeauswahl(komfort.AuswahlVon(z, k.Id)),
            Stumm = Stumm(k, jetzt),
            Quelle = Quelle(k.Quelle),
            Durch = k.QuelleDurch == 0 ? null : k.QuelleDurch,
            LetzterStand = k.LetzterStand is { } ls
                ? new S.LetzterStand
                {
                    SchiffId = ls.Platz is { SchiffId: not 0 } lp ? lp.SchiffId : null,
                    RolleId = ls.Platz?.RolleId,
                    Felder = [.. ls.Felder.Select(Mitglied)],
                    Sendeauswahl = Sendeauswahl(ls.Sendeauswahl),
                }
                : null,
            Entfallen = k.Entfallen.IsDefaultOrEmpty
                ? null
                : [.. k.Entfallen.Select(e => new S.EntfallenesFeld { Feld = e.Feld, NetzId = e.NetzId, Grund = e.Grund })],
            Konto = konto,
            Phase = phase,
            Rolle = k.GewaehlteRolle,
            Gesperrt = Gesperrt(z, k, jetzt),
        };
    }

    /// <summary>
    /// Ob ein Konto in Praesenz erscheint: mit Sitzung oder mit Platz (BELEGT oder RESERVIERT), seit 0.11.2 auch offline mit einer frischen
    /// Einteilung ohne Platz (Sarevoc 13.10.2026: der Stammbaum der Raid-Seite "kann in der Vorplanung verwendet werden, um die zu erwartenden
    /// Teilnehmer vorzusortieren"; wer vorab nur in Kanäle gezogen wurde, fehlte sonst im Baum). Dieselbe Frist wie die Reservierung.
    /// </summary>
    public static bool InPraesenz(L.Schnappschuss z, L.Konto k, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(k);
        return z.IstOnline(k.Id) || z.PlatzZustandVon(k.Id, jetzt) != L.PlatzZustand.Frei
            || (k.Reservierung is { } r && jetzt - r.Gesetzt < L.Fristen.ReservierungMs);
    }

    /// <summary>
    /// Was die Praesenz aus Konten- und Mitgliederbuch trägt (F4, F12): Hauptname und Discord-Name für alle, Zeichen und
    /// Abzeichen für alle nur mit <see cref="Freigegeben"/> ("Meine Angaben in Wer ist da zeigen", Sarevoc, 30.09.2026, Antwort 3),
    /// sonst nur für Empfänger mit <c>ZUWEISEN</c>.
    /// </summary>
    public readonly record struct Praesenzzusatz(string? ScName, string? DiscordName, IReadOnlyList<string>? Zeichen, bool Freigegeben)
    {
        /// <summary>Sichtbare Abzeichen (0.11.7), mit denselben Sperren wie <see cref="Zeichen"/>.</summary>
        public IReadOnlyList<string>? Abzeichen { get; init; }

        /// <summary>Das Abzeichen "vorn" (0.11.7), nur wenn besessen und sichtbar.</summary>
        public string? AbzeichenVorn { get; init; }
    }

    /// <summary>
    /// Praesenz-Eintrag (4.5). Details (Felder, sendetIn, stumm, Verbindung, Güte) nur für Empfänger mit
    /// <c>ZUWEISEN</c>; ein Funker sieht die Belegung, nicht die Felder der anderen.
    /// </summary>
    public static S.PraesenzEintrag PraesenzEintrag(L.Schnappschuss z, L.Komfortzustand komfort, L.Konto k, I.Konto? ik,
        bool details, S.Verbindungsweg? weg, S.Guete? guete, long jetzt, Praesenzzusatz zusatz = default)
    {
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(komfort);
        ArgumentNullException.ThrowIfNull(k);
        var platz = z.PlatzZustandVon(k.Id, jetzt);
        return new S.PraesenzEintrag
        {
            KontoId = k.Id,
            Name = k.Name.Length > 0 ? k.Name : ik?.Name ?? "?",
            Benutzername = Benutzername(ik),
            Online = z.IstOnline(k.Id),
            SchiffId = platz != L.PlatzZustand.Frei && k.Platz is { SchiffId: not 0 } p ? p.SchiffId : null,
            RolleId = platz != L.PlatzZustand.Frei ? k.Platz?.RolleId : null,
            Platz = Platz(platz),
            Felder = details ? [.. k.Felder.Select(Mitglied)] : null,
            SendetIn = details ? [.. komfort.SendetIn(z, k.Id)] : null,
            Stumm = details ? Stumm(k, jetzt) : null,
            Verbindung = details ? weg : null,
            Guete = details ? guete : null,
            ScName = zusatz.ScName,
            DiscordName = zusatz.DiscordName,
            Zeichen = details || zusatz.Freigegeben ? zusatz.Zeichen : null,
            Abzeichen = details || zusatz.Freigegeben ? zusatz.Abzeichen : null,
            AbzeichenVorn = details || zusatz.Freigegeben ? zusatz.AbzeichenVorn : null,
            // F4: für das Brett, nur mit ZUWEISEN.
            Quelle = details && platz != L.PlatzZustand.Frei ? Quelle(k.Quelle) : null,
            Durch = details && platz != L.PlatzZustand.Frei && k.QuelleDurch != 0 ? k.QuelleDurch : null,
            Seit = details && platz != L.PlatzZustand.Frei ? k.Reservierung?.Gesetzt ?? k.Platz?.Gesetzt : null,
            LetzterPlatz = details && k.LetzterStand?.Platz is { } lp
                ? new S.LetzterPlatz { SchiffId = lp.SchiffId == 0 ? null : lp.SchiffId, RolleId = lp.RolleId }
                : null,
            // 6.15: Für die Einsatzleitung, damit das Brett einen Admin im Funker-Modus als einteilbar zeigt.
            AlsFunker = details && k.AlsFunkerZu(jetzt) ? true : null,
            // 6.16: die gewählte Rolle für die Einsatzleitung.
            Rolle = details ? k.GewaehlteRolle : null,
            // 6.17: die Kanalsperren des Kontos für das Brett.
            Gesperrt = details ? EigeneSperren(k, jetzt) : null,
        };
    }

    /// <summary>Push <c>Belegung</c> (0x4003) aus dem Stand der Lage.</summary>
    public static S.Belegung Belegung(L.Schnappschuss z, L.Belegungsstand b)
    {
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(b);
        return new S.Belegung
        {
            ZustandsVersion = z.ZustandsVersion,
            Online = (ushort)Math.Min(b.Online, ushort.MaxValue),
            MitPlatz = (ushort)Math.Min(b.MitPlatz, ushort.MaxValue),
            Plaetze = [.. b.Plaetze.Select(p => new S.PlatzBelegung
            {
                SchiffId = p.SchiffId == 0 ? null : p.SchiffId,
                RolleId = p.RolleId,
                Belegt = (ushort)Math.Min(p.Belegt, ushort.MaxValue),
                Reserviert = (ushort)Math.Min(p.Reserviert, ushort.MaxValue),
                Kapazitaet = p.Kapazitaet,
                Mindest = p.Mindest == 0 ? null : p.Mindest,
            })],
        };
    }

    /// <summary>Der eigene Auftrag für Welcome und Mitgliedschaften (F2, F3); Zeiten in Lage-ms, also Unix-ms.</summary>
    public static S.BeauftragungAngabe? BeauftragungAngabe(L.Konto? k, long jetzt) => k?.Beauftragung is { } b
        ? new S.BeauftragungAngabe { Von = b.Von, Bis = b.Bis, Rechte = Rechte(b.Rechte), Durch = b.Durch, Grund = b.Grund, Aktiv = b.GiltZu(jetzt) }
        : null;

    // ---- Konten und Geräte ----

    public static S.KontoAngabe KontoAngabe(I.Konto k, bool mitRechten)
    {
        ArgumentNullException.ThrowIfNull(k);
        return new S.KontoAngabe
        {
            Id = k.KontoId,
            Name = k.Name,
            Benutzername = Benutzername(k),
            Stufe = mitRechten ? Stufe(k.Stufe) : null,
            Rechte = mitRechten ? Rechte(RechtLage(k.Rechte)) : null,
            ScNamen = k.ScNamen.IsEmpty ? null : [.. k.ScNamen],
            DiscordName = k.DiscordName,
        };
    }

    /// <summary>
    /// Discord-Benutzername für den Draht, wo er Pflicht ist: Konten aus einem Antrag ohne Code (3.4) haben keinen, dort
    /// steht <c>-</c>; unbekannte Konten <c>?</c>. Der getippte Discord-Name ist ungeprüft und steht nie an seiner Stelle.
    /// </summary>
    public static string Benutzername(I.Konto? k) => k is null ? "?" : k.Benutzername.Length > 0 ? k.Benutzername : "-";

    /// <summary>SC-Name für Freigabe und Anzeige: beim Antrag der beantragte, sonst der Hauptname.</summary>
    private static string? ScNameVon(I.Konto? k) => k?.Antrag?.ScName ?? k?.Hauptname;

    public static S.GeraetStatus GeraetStatus(I.Kontenstand s, I.Geraet g, I.GeraetStatusMeldung status, uint? durch, string? grund,
        IReadOnlyList<string>? vergeben = null, IReadOnlyList<S.VormerkungVorschlag>? vorschlaege = null, bool discordVergeben = false)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(g);
        s.Konten.TryGetValue(g.KontoId, out var k);
        return new S.GeraetStatus
        {
            GeraetId = g.GeraetId,
            KontoId = g.KontoId,
            KontoName = k?.Name ?? "?",
            Benutzername = Benutzername(k),
            Status = GeraetStatus(status),
            Anzeige = g.Anzeige,
            Fingerabdruck = g.Fingerabdruck.AlsBase64(),
            SchluesselArt = SchluesselArt(g.SchluesselArt),
            ErstesGeraet = !s.Geraete.Values.Any(a => a.KontoId == g.KontoId && a.GeraetId != g.GeraetId),
            Markierung = Markierung(g.Markierung),
            Grund = grund,
            Durch = durch,
            ScName = ScNameVon(k),
            DiscordName = k?.DiscordName,
            Antrag = k is { IstAntrag: true } ? true : null,
            ScNamenVergeben = vergeben is { Count: > 0 } ? vergeben : null,
            DiscordNameVergeben = discordVergeben ? true : null,
            Vormerkungen = vorschlaege is { Count: > 0 } ? vorschlaege : null,
        };
    }

    public static S.GeraetEintrag GeraetEintrag(I.Kontenstand s, I.Geraet g, IReadOnlyList<string>? vergeben = null,
        IReadOnlyList<S.VormerkungVorschlag>? vorschlaege = null, bool discordVergeben = false)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(g);
        s.Konten.TryGetValue(g.KontoId, out var k);
        return new S.GeraetEintrag
        {
            Id = g.GeraetId,
            KontoId = g.KontoId,
            KontoName = k?.Name ?? "?",
            Benutzername = Benutzername(k),
            Name = g.Name,
            Anzeige = g.Anzeige,
            Fingerabdruck = g.Fingerabdruck.AlsBase64(),
            SchluesselArt = SchluesselArt(g.SchluesselArt),
            Status = GeraetStatus(g.Status),
            Markierung = Markierung(g.Markierung),
            Verknuepft = g.Verknuepft.ToUnixTimeMilliseconds(),
            Zuletzt = g.Zuletzt?.ToUnixTimeMilliseconds(),
            ScName = ScNameVon(k),
            DiscordName = k?.DiscordName,
            Antrag = k is { IstAntrag: true } ? true : null,
            ScNamenVergeben = vergeben is { Count: > 0 } ? vergeben : null,
            DiscordNameVergeben = discordVergeben ? true : null,
            Vormerkungen = vorschlaege is { Count: > 0 } ? vorschlaege : null,
        };
    }

    // ---- Aufträge aus Drahtnachrichten ----

    public static L.Feldvorgabe Feldvorgabe(S.FeldBelegung f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new L.Feldvorgabe(f.Feld, f.NetzId, f.Hoeren, f.Senden);
    }

    public static S.FeldBelegung FeldBelegung(L.Feldvorgabe f) => new() { Feld = f.Feld, NetzId = f.NetzId, Hoeren = f.Hoeren, Senden = f.Senden };

    public static L.Feldpatch Feldpatch(S.FeldPatch p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Leeren == true ? L.Feldpatch.Leer(p.Feld ?? 0) : new L.Feldpatch(p.Feld, p.NetzId ?? 0, p.Hoeren, p.Senden, p.Fest);
    }

    public static L.Aenderung Aenderung(S.Verschiebung v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return new L.Aenderung(v.KontoId)
        {
            RolleId = v.RolleId,
            SchiffId = v.SchiffId,
            Felder = v.Felder is null ? null : [.. v.Felder.Select(Feldvorgabe)],
            FelderPatch = v.FelderPatch is null ? null : [.. v.FelderPatch.Select(Feldpatch)],
            SendeFelder = v.SendeFelder is null ? null : [.. v.SendeFelder],
        };
    }

    public static L.AufstellungsEintrag AufstellungsEintrag(S.AufstellungsEintrag e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new L.AufstellungsEintrag
        {
            KontoId = e.KontoId ?? 0,
            DiscordId = e.DiscordId,
            RolleId = e.RolleId,
            SchiffId = e.SchiffId ?? 0,
            Felder = e.Felder is null ? null : [.. e.Felder.Select(Feldvorgabe)],
            SendeFelder = e.SendeFelder is null ? null : [.. e.SendeFelder],
        };
    }

    public static S.AufstellungsEintrag AufstellungsEintrag(L.AufstellungsEintrag e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new S.AufstellungsEintrag
        {
            KontoId = e.KontoId == 0 ? null : e.KontoId,
            DiscordId = e.KontoId == 0 ? e.DiscordId : null,
            RolleId = e.RolleId,
            SchiffId = e.SchiffId == 0 ? null : e.SchiffId,
            Felder = e.Felder is { } f ? [.. f.Select(FeldBelegung)] : null,
            SendeFelder = e.SendeFelder is { } sf ? [.. sf] : null,
        };
    }
}
