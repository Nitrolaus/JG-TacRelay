// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using Funk.Server.Betrieb;
using Funk.Server.Lage;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Anfragen des Clients in AKTIV (4.4, 4.5). Die Rechte prüfen die Module am aktuellen Stand (6.10, 3.5, 3.6); ohne
/// Recht kommt <c>KEIN_RECHT</c> ohne Trennung und ohne Seiteneffekt (P-45).
/// </summary>
public sealed partial class Zentrale
{
    internal Task<Antwort> AnfrageAsync(Verbindung v, S.Nachrichtentyp typ, object body) => Ausfuehren<Antwort>(tcs =>
    {
        var antwort = Beantworten(v, typ, body, tcs);
        if (antwort is not null) tcs.TrySetResult(antwort);
    });

    /// <summary>
    /// Nachrichten, die eine Verwaltungshandlung sein können: Sie verlängern eine offene Admin-Sitzung (Leerlauf 30 min, 3.9).
    /// </summary>
    private static readonly HashSet<S.Nachrichtentyp> Verwaltungstypen =
    [
        S.Nachrichtentyp.GeraeteListe, S.Nachrichtentyp.GeraetEntscheiden, S.Nachrichtentyp.GeraetEntfernen, S.Nachrichtentyp.KontoVerwaltung,
        S.Nachrichtentyp.Verwaltung, S.Nachrichtentyp.Journal, S.Nachrichtentyp.Rollenkatalog, S.Nachrichtentyp.Mitglied,
        S.Nachrichtentyp.Untergilde, S.Nachrichtentyp.AntraegeAblehnen, S.Nachrichtentyp.Beauftragung, S.Nachrichtentyp.Abzeichen,
    ];

    /// <summary>Liefert die Antwort sofort oder <c>null</c>, wenn sie nach einem Commit über <paramref name="tcs"/> kommt.</summary>
    private Antwort? Beantworten(Verbindung v, S.Nachrichtentyp typ, object body, TaskCompletionSource<Antwort> tcs)
    {
        var konto = v.KontoId;
        // Die Verwaltungsrechte wirken nur in einer offenen Admin-Sitzung (3.9); das tragen Akteur und Schranken in die Module.
        var akteur = AkteurVon(v);
        if (akteur.AdminSitzung && Verwaltungstypen.Contains(typ)) AdminHandlung(v);
        var z = Schreiber.Aktuell;
        switch (body)
        {
            case S.LeererBody when typ == S.Nachrichtentyp.AdminNonce:
                return AdminNonce(v);

            case S.AdminAnmeldungAnfrage aa:
                return AdminAnmeldung(v, aa);

            case S.AdminBestaetigenAnfrage ab:
                return AdminBestaetigen(v, ab);

            case S.LeererBody when typ == S.Nachrichtentyp.AdminAbmelden:
                return AdminAbmelden(v);

            case S.AdminEinrichtungAnfrage ae:
                return AdminEinrichtung(v, ae);

            case S.ProfilSetzenAnfrage ps:
                return ProfilSetzen(v, akteur, ps);

            case S.BeauftragungAnfrage ba:
                return Beauftragung(v, akteur, ba, tcs);

            case S.LeererBody when typ == S.Nachrichtentyp.KontenVerzeichnis:
                return KontenVerzeichnis(v);

            case S.FunkermodusAnfrage fm:
            {
                // "Heute als Funker" bis zum nächsten 04:00 Ortszeit, höchstens 24 h (6.15); ohne Admin-Sitzung, nur das eigene Konto.
                var jetzt = Zeit.LageMs();
                var bis = Math.Min(S.Auftragszeit.NaechsterMorgen(jetzt), jetzt + L.Fristen.FunkerModusMaxMs);
                Schreiben(L.Auftrag.FunkermodusSetzen(konto, fm.An, bis), e => tcs.TrySetResult(e.Erfolg
                    ? Antwort.Mit(new S.FunkermodusAntwort { An = AlsFunkerBisVon(konto) is not null, Bis = AlsFunkerBisVon(konto) })
                    : FehlerAus(e)));
                return null;
            }

            case S.PhaseSchaltenAnfrage ps:
                Schreiben(L.Auftrag.PhaseSchalten(konto, ps.Nr, ps.Erwartet), e => tcs.TrySetResult(e.Erfolg
                    ? Antwort.Mit(new S.PhaseSchaltenAntwort
                    {
                        Nr = ps.Nr,
                        Umgestellt = (ushort)Math.Min(e.Phasenwechsel?.Umgestellt.Length ?? 0, ushort.MaxValue),
                        Ausgelassen = [.. (e.Phasenwechsel?.Ausgelassen ?? []).Take(S.Grenzen.KontenMax)],
                        ZustandsVersion = e.ZustandsVersion,
                    })
                    : FehlerAus(e)));
                return null;

            case S.JournalAnfrage ja:
                return JournalLesen(akteur, ja);

            case S.RollenkatalogAnfrage rk:
                return Rollenkatalog(v, akteur, rk);

            case S.MitgliedAnfrage ma:
                return Mitglied(v, akteur, ma);

            case S.LieblingsrollenAnfrage lr:
                return Lieblingsrollen(v, akteur, lr);

            case S.UntergildeAnfrage ug:
                return Untergilde(ug);

            case S.MitgliedskarteAnfrage mk:
                return Mitgliedskarte(v, mk);

            case S.AntraegeAblehnenAnfrage:
                return AntraegeAblehnen(v, akteur);

            case S.AbzeichenAnfrage az:
                return AbzeichenSetzen(v, akteur, az);

            case S.MeldungSendenAnfrage ms:
                return MeldungSenden(v, ms);

            case S.BereitMeldenAnfrage bm:
                return BereitMelden(v, bm);

            case S.LeererBody when typ == S.Nachrichtentyp.Zustand:
                ZustandSenden(v);
                return Antwort.Mit(new S.ZustandsVersionBody { ZustandsVersion = Schreiber.Aktuell.ZustandsVersion });

            case S.LeererBody when typ == S.Nachrichtentyp.PegelVoreinstellungen:
                return Antwort.Mit(Komfort.PegelVon(konto));

            case S.PegelSetzenAnfrage p:
                return Komfort.PegelSetzen(konto, p.KontoId, p.NetzId, p.Db) is { } fp ? Antwort.Fehl(fp) : Antwort.Leer();

            case S.AblageAnfrage a:
                if (a.Op == S.AblageOperation.Lesen) return Antwort.Mit(Komfort.AblageLesen(konto, a.Bereich));
                var (fa, ablage) = Komfort.AblageSchreiben(konto, a.Bereich, a.Daten!.Value, a.Version);
                return fa is { } f ? Antwort.Fehl(f) : Antwort.Mit(ablage);

            case S.SendeauswahlSetzenAnfrage sa:
            {
                var code = Schreiber.SendeauswahlSetzen(konto, sa.Felder, sa.Global, Zeit.LageMs());
                if (code != L.Fehlercode.Keiner) return Antwort.Fehl(Abbildung.Fehler(code));
                Komfort.AuswahlMerken(konto, sa.Felder, sa.Global);
                PraesenzAnZuweiser(konto);
                return Antwort.Leer();
            }

            case S.HoerenSetzenAnfrage h:
                Schreiben(L.Auftrag.HoerenSetzen(konto, h.NetzId, h.Hoeren), e => tcs.TrySetResult(MitVersion(e)));
                return null;

            case S.FeldSetzenAnfrage fs:
                Schreiben(L.Auftrag.FeldSetzen(konto, fs.Feld, fs.NetzId), e => tcs.TrySetResult(MitVersion(e)));
                return null;

            case S.SatzAnwendenAnfrage s:
                Schreiben(L.Auftrag.SatzAnwenden(konto, s.RolleId, s.SchiffId, [.. s.Felder.Select(Abbildung.Feldvorgabe)],
                    s.Sendeauswahl is null ? null : Abbildung.SendeauswahlLage(s.Sendeauswahl)), e => tcs.TrySetResult(e.Erfolg
                    ? Antwort.Mit(new S.SatzAnwendenAntwort
                    {
                        ZustandsVersion = e.ZustandsVersion,
                        Uebersprungen = [.. e.Uebersprungen.Select(u => new S.UebersprungenesFeld { Feld = (byte)u.Eintrag, Code = Drahtnamen.Von(u.Code) })],
                    })
                    : FehlerAus(e)));
                return null;

            case S.RolleWaehlenAnfrage rw:
                Schreiben(L.Auftrag.RolleWaehlen(konto, rw.RolleId, rw.Ch?.ToDictionary(c => c.Feld, c => c.Ch)), e => tcs.TrySetResult(e.Erfolg
                    ? Antwort.Mit(new S.SatzAnwendenAntwort
                    {
                        ZustandsVersion = e.ZustandsVersion,
                        Uebersprungen = [.. e.Uebersprungen.Select(u => new S.UebersprungenesFeld { Feld = (byte)u.Eintrag, Code = Drahtnamen.Von(u.Code) })],
                    })
                    : FehlerAus(e)));
                return null;

            case S.KanalSperreAnfrage ks:
                return KanalSperre(v, ks, tcs);

            case S.EinsteigenRolleSchiffAnfrage er:
                Schreiben(L.Auftrag.EinsteigenRolleSchiff(konto, er.KontoId, er.RolleId, er.SchiffId, er.Erzwingen ?? false, er.BasisVersion),
                    e => tcs.TrySetResult(MitVersion(e)));
                return null;

            case S.VerschiebenAnfrage vs:
                Schreiben(L.Auftrag.Verschieben(konto, [.. vs.Aenderungen.Select(Abbildung.Aenderung)], vs.BasisVersion),
                    e => tcs.TrySetResult(e.Erfolg
                        ? Antwort.Mit(new S.VerschiebenAntwort { ZustandsVersion = e.ZustandsVersion, Offline = [.. e.Offline], Verdraengt = [.. e.Verdraengt] })
                        : FehlerAus(e)));
                return null;

            case S.StummschaltenAnfrage st:
                Schreiben(L.Auftrag.Stummschalten(konto, st.KontoId, st.NetzId, st.Sekunden), e => tcs.TrySetResult(e.Erfolg ? Antwort.Leer() : FehlerAus(e)));
                return null;

            case S.LeererBody when typ == S.Nachrichtentyp.PlaetzeFreigeben:
                Schreiben(L.Auftrag.PlaetzeFreigeben(konto), e => tcs.TrySetResult(e.Erfolg
                    ? Antwort.Mit(new S.PlaetzeFreigebenAntwort { ZustandsVersion = e.ZustandsVersion, Anzahl = (ushort)Math.Min(e.Anzahl, ushort.MaxValue) })
                    : FehlerAus(e)));
                return null;

            case S.AufstellungSpeichernAnfrage asp:
                Schreiben(L.Auftrag.AufstellungSpeichern(konto, asp.Id, asp.Version, asp.Name, [.. asp.Eintraege.Select(Abbildung.AufstellungsEintrag)]),
                    e => tcs.TrySetResult(e.Erfolg
                        ? Antwort.Mit(new S.AufstellungsVerweis { Id = e.AufstellungsId, Version = e.AufstellungsVersion })
                        : FehlerAus(e)));
                return null;

            case S.AufstellungListeAnfrage al:
                return AufstellungListe(z, konto, al.Id);

            case S.AufstellungAnwendenAnfrage aa:
                Schreiben(L.Auftrag.AufstellungAnwenden(konto, aa.Id, aa.Version,
                        aa.Modus == S.AufstellungsModus.Ersetzen ? L.Aufstellungsmodus.Ersetzen : L.Aufstellungsmodus.Ergaenzen, aa.BasisVersion, aa.Erzwingen ?? false),
                    e => tcs.TrySetResult(e.Erfolg
                        ? Antwort.Mit(new S.AufstellungAnwendenAntwort
                        {
                            ZustandsVersion = e.ZustandsVersion,
                            Version = e.AufstellungsVersion,
                            Angewendet = (ushort)Math.Min(e.Anzahl, ushort.MaxValue),
                            Offline = [.. e.Offline],
                            Unverknuepft = [.. e.Unverknuepft],
                            Verdraengt = [.. e.Verdraengt],
                            Uebersprungen = [.. e.Uebersprungen.Select(u => new S.UebersprungenerEintrag
                            {
                                Eintrag = (ushort)u.Eintrag,
                                KontoId = u.KontoId == 0 ? null : u.KontoId,
                                Code = Drahtnamen.Von(u.Code),
                            })],
                        })
                        : FehlerAus(e)));
                return null;

            case S.AufstellungsVerweis loeschen when typ == S.Nachrichtentyp.AufstellungLoeschen:
                Schreiben(L.Auftrag.AufstellungLoeschen(konto, loeschen.Id, loeschen.Version), e => tcs.TrySetResult(e.Erfolg ? Antwort.Leer() : FehlerAus(e)));
                return null;

            case S.KontoVerwaltungAnfrage kv:
                return KontoVerwaltung(v, akteur, kv);

            case S.GeraeteListeAnfrage gl:
            {
                var auskunft = Konten.GeraeteListe(akteur, gl.KontoId, gl.NurWartend ?? false);
                if (auskunft.Fehler != I.Fehlercode.Keiner) return Antwort.Fehl(Abbildung.Fehler(auskunft.Fehler));
                var stand = Konten.Stand;
                // Vorschläge aus den Vormerkungen nur für die Freigabeansicht in einer Admin-Sitzung (F14).
                var fuerAdmin = I.Kontenbuch.RechtPruefen(stand, akteur, I.Rechte.GeraeteFreigeben) == I.Fehlercode.Keiner;
                return Antwort.Mit(new S.GeraeteListeAntwort
                {
                    Geraete = [.. auskunft.Geraete.Select(g => GeraetEintragBauen(stand, g, fuerAdmin))],
                });
            }

            case S.GeraetEntscheidenAnfrage ge:
                return GeraetEntscheiden(v, akteur, ge);

            case S.GeraetEntfernenAnfrage gf:
            {
                var e = Konten.GeraetEntfernen(akteur, gf.GeraetId, gf.Dauerhaft, gf.Grund);
                if (!e.Ok || e.Status is not { } status) return ModulFehler(v, e.Fehler);
                var antwort = Antwort.Mit(new S.GeraetEntfernenAntwort { Status = Abbildung.GeraetStatus(status) });
                if (Konten.Stand.Geraete.TryGetValue(gf.GeraetId, out var entfernt) && entfernt.KontoId != konto)
                    Vermerken("GERAET_" + status.ToString().ToUpperInvariant(), konto, entfernt.KontoId,
                        $"Gerät {entfernt.Name} ({entfernt.Anzeige}) von {KontoText(entfernt.KontoId)}: {status.ToString().ToUpperInvariant()} durch {KontoText(konto)}");
                // Erst antworten, dann trennen: entfernt sich das Gerät selbst, soll die Antwort noch hinausgehen.
                tcs.TrySetResult(antwort);
                WirkungenAusfuehren(e.Wirkungen);
                return null;
            }

            case S.VerwaltungAnfrage va:
                return VerwaltungSchranke(v, akteur, va.Op) ?? Verwaltung(konto, va, tcs);

            case S.AnAus abo when typ == S.Nachrichtentyp.SprechanzeigeAbo:
                if (!HatRecht(z, v, L.Recht.SprechanzeigeAlle)) return Antwort.Fehl(S.Fehlercode.KeinRecht);
                v.SprechanzeigeAbo = abo.An;
                return Antwort.Leer();

            case S.KeyRotationAnfrage:
                Relais?.RotationAnfordern(v.SitzungsId);
                return Antwort.Leer();

            case S.TunnelModusAnfrage tm:
                Relais?.TunnelSetzen(v.SitzungsId, tm.An);
                v.TunnelGeaendert(tm.An);
                Log.Info("Tunnel " + (tm.An ? "an" : "aus") + " (" + tm.Grund + ") für " + Log.Schwaerzung.Konto(konto));
                PraesenzAnZuweiser(konto);
                return Antwort.Mit(new S.AnAus { An = tm.An });

            case S.RaidHelperImportAnfrage:
            case S.LeererBody when typ == S.Nachrichtentyp.RaidHelperEvents:
                if (!HatRecht(z, v, L.Recht.Zuweisen)) return Antwort.Fehl(S.Fehlercode.KeinRecht);
                // Raid-Helper braucht Netz nach außen und einen Server-Key; nicht Teil dieses Schritts.
                return Antwort.Fehl(S.Fehlercode.Extern, "Raid-Helper nicht angebunden", Antwort.DetailsBauen(w => w.WriteString("grund", "kein Server-Key")));

            case S.RegelTabelleAnfrage rt:
            {
                if (!HatRecht(z, v, L.Recht.Zuweisen)) return Antwort.Fehl(S.Fehlercode.KeinRecht);
                if (rt.Op == S.RegelOperation.Lesen) return Antwort.Mit(new S.RegelTabelleAntwort { Regeln = Komfort.Regeln, Version = Komfort.RegelVersion });
                var (regeln, version) = Komfort.RegelnSetzen(rt.Regeln!);
                return Antwort.Mit(new S.RegelTabelleAntwort { Regeln = regeln, Version = version });
            }
        }
        return Antwort.Fehl(S.Fehlercode.Ungueltig, "nicht unterstützt");
    }

    /// <summary>Zustand (4.5): NetzListe, Mitgliedschaften, Praesenz{voll}, PegelVoreinstellungen, danach die Antwort.</summary>
    internal void ZustandSenden(Verbindung v)
    {
        var z = Schreiber.Aktuell;
        v.Push(S.Nachrichtentyp.NetzListe, Abbildung.NetzListe(z));
        if (z.Konten.TryGetValue(v.KontoId, out var k))
            v.Push(S.Nachrichtentyp.Mitgliedschaften, MitgliedschaftenBauen(z, k));
        var (mit, ohne) = Praesenz(z, null, voll: true);
        v.Push(S.Nachrichtentyp.Praesenz, HatRecht(z, v, L.Recht.Zuweisen) ? mit : ohne);
        v.Push(S.Nachrichtentyp.PegelVoreinstellungen, Komfort.PegelVon(v.KontoId));
        // Optionaler Push (0x4003): Clients 0.2.0 übergehen ihn (4.7).
        BelegungSenden(v);
    }

    /// <summary>Praesenz-Delta nur für Sitzungen mit ZUWEISEN (Details: sendetIn, Verbindung).</summary>
    private void PraesenzAnZuweiser(uint konto)
    {
        var z = Schreiber.Aktuell;
        var (mit, _) = Praesenz(z, [konto], voll: false);
        foreach (var v in VerbindungenMitRecht(L.Recht.Zuweisen)) v.Push(S.Nachrichtentyp.Praesenz, mit);
    }

    private Antwort MitVersion(L.Ergebnis e) =>
        e.Erfolg ? Antwort.Mit(new S.ZustandsVersionBody { ZustandsVersion = e.ZustandsVersion }) : FehlerAus(e);

    /// <summary>Fehler des Zustandsschreibers mit <c>details</c> (VERALTET: konten und zustandsVersion; Fehlerliste).</summary>
    private Antwort FehlerAus(L.Ergebnis e)
    {
        var code = Abbildung.Fehler(e.Fehler);
        // Kanalsperre (6.17): KEIN_RECHT nennt Grund, Setzenden und Ende, damit der Client "gesperrt von Max bis 04:00" zeigen kann.
        if (e.Fehler == L.Fehlercode.KeinRecht && e.Sperre is { } sperre)
        {
            return Antwort.Fehl(code, null, Antwort.DetailsBauen(w =>
            {
                w.WriteString("grund", "GESPERRT");
                w.WriteNumber("durch", sperre.Durch);
                w.WriteNumber("bis", sperre.Bis);
            }));
        }
        // PhaseSchalten (6.14 Regel 2): VERALTET nennt die laufende Phase und wer sie geschaltet hat.
        if (e.Fehler == L.Fehlercode.Veraltet && e.PhaseStand is { } stand)
        {
            return Antwort.Fehl(code, null, Antwort.DetailsBauen(w =>
            {
                w.WriteNumber("nr", stand.Nr);
                w.WriteNumber("durch", stand.Durch);
            }));
        }
        if (e.Fehler == L.Fehlercode.Veraltet && e.VeralteteKonten.Length > 0)
        {
            var version = Schreiber.Aktuell.ZustandsVersion;
            return Antwort.Fehl(code, null, Antwort.DetailsBauen(w =>
            {
                w.WriteStartArray("konten");
                foreach (var k in e.VeralteteKonten.Take(64))
                {
                    w.WriteStartObject();
                    w.WriteNumber("kontoId", k.KontoId);
                    w.WriteNumber("kontoVersion", k.KontoVersion);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteNumber("zustandsVersion", version);
            }));
        }
        if (e.Fehlerliste.Length > 0)
        {
            return Antwort.Fehl(code, null, Antwort.DetailsBauen(w =>
            {
                w.WriteStartArray("fehler");
                foreach (var f in e.Fehlerliste.Take(48))
                {
                    w.WriteStartObject();
                    w.WriteNumber("eintrag", f.Eintrag);
                    if (f.KontoId != 0) w.WriteNumber("kontoId", f.KontoId);
                    if (f.DiscordId is not null) w.WriteString("discordId", f.DiscordId);
                    w.WriteString("code", Drahtnamen.Von(f.Code));
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }));
        }
        return Antwort.Fehl(code);
    }

    private Antwort AufstellungListe(L.Schnappschuss z, uint konto, uint? id)
    {
        if (!z.RechteVon(konto, Zeit.LageMs()).Hat(L.Recht.Zuweisen)) return Antwort.Fehl(S.Fehlercode.KeinRecht);
        var alle = Schreiber.Neben.Aufstellungen;
        if (id is null)
        {
            return Antwort.Mit(new S.AufstellungListeAntwort
            {
                Aufstellungen = [.. alle.Values.Select(a => new S.AufstellungKurz
                {
                    Id = a.Id,
                    Name = a.Name,
                    Version = a.Version,
                    Anzahl = (ushort)a.Eintraege.Length,
                    Geaendert = a.Geaendert,
                    Durch = a.Durch,
                    Ersteller = a.Ersteller,
                })],
            });
        }
        if (!alle.TryGetValue(id.Value, out var auf)) return Antwort.Fehl(S.Fehlercode.Unbekannt);
        return Antwort.Mit(new S.AufstellungListeAntwort
        {
            Id = auf.Id,
            Version = auf.Version,
            Name = auf.Name,
            Eintraege = [.. auf.Eintraege.Select(Abbildung.AufstellungsEintrag)],
            Geaendert = auf.Geaendert,
            Durch = auf.Durch,
            Ersteller = auf.Ersteller,
        });
    }

    /// <summary>
    /// KontoVerwaltung (4.5, 6.10) über das Kontenbuch; die Spiegelung in den Schnappschuss folgt danach. Das Kontenbuch prüft
    /// Admin-Sitzung, frische Bestätigung, Eigentümer und ADMIN-Konten (3.9, 3.10); jede Änderung steht im Journal.
    /// </summary>
    private Antwort KontoVerwaltung(Verbindung v, I.Akteur akteur, S.KontoVerwaltungAnfrage kv)
    {
        I.Ergebnis e;
        var ziel = KontoText(kv.KontoId);
        string text;
        switch (kv.Op)
        {
            case S.KontoOperation.Sperren:
                e = Konten.KontoSperren(akteur, kv.KontoId);
                text = "gesperrt";
                break;
            case S.KontoOperation.Entsperren:
                e = Konten.KontoEntsperren(akteur, kv.KontoId);
                text = "entsperrt";
                break;
            case S.KontoOperation.Stufe:
                if (kv.Wert is not { ValueKind: JsonValueKind.String } sw || StufeAusText(sw.GetString()) is not { } stufe)
                    return Antwort.Fehl(S.Fehlercode.Ungueltig, "wert");
                e = Konten.StufeSetzen(akteur, kv.KontoId, stufe);
                text = "Stufe " + I.Rechteregeln.Drahtname(stufe);
                break;
            case S.KontoOperation.Rechte:
                if (kv.Wert is not { ValueKind: JsonValueKind.Array } rw || rw.GetArrayLength() > 9
                    || !I.Rechteregeln.TryAusDrahtnamen(rw.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : ""), out var rechte))
                    return Antwort.Fehl(S.Fehlercode.Ungueltig, "wert");
                e = Konten.RechteSetzen(akteur, kv.KontoId, rechte);
                text = "Rechte " + string.Join(' ', I.Rechteregeln.Drahtnamen(rechte));
                break;
            case S.KontoOperation.Name:
                if (kv.Wert is not { ValueKind: JsonValueKind.String } nw) return Antwort.Fehl(S.Fehlercode.Ungueltig, "wert");
                e = Konten.NameSetzen(akteur, kv.KontoId, nw.GetString() ?? "");
                text = "Name " + nw.GetString();
                break;
            case S.KontoOperation.Scnamen:
            {
                if (kv.Wert is not { ValueKind: JsonValueKind.Array } nv || nv.GetArrayLength() > I.Kontenbuch.MaxScNamen
                    || nv.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                    return Antwort.Fehl(S.Fehlercode.Ungueltig, "wert");
                var n = Konten.ScNamenSetzen(akteur, kv.KontoId, [.. nv.EnumerateArray().Select(x => x.GetString()!)]);
                if (!n.Ok) return n.Fehler == I.Fehlercode.SchrittBestaetigen ? SchrittBestaetigen(v) : NamensFehler(n);
                Vermerken("SCNAMEN", v.KontoId, kv.KontoId, $"SC-Namen von {KontoText(kv.KontoId)}: {string.Join(", ", n.ScNamen)} durch {KontoText(v.KontoId)}");
                KontoGeaendert(kv.KontoId);
                return Antwort.Leer();
            }
            case S.KontoOperation.Discordname:
            {
                // A53: wert ist der neue Discord-Name; null, fehlend oder leer leert ihn.
                string? name = null;
                if (kv.Wert is { } w)
                {
                    if (w.ValueKind == JsonValueKind.String) name = w.GetString();
                    else if (w.ValueKind != JsonValueKind.Null) return Antwort.Fehl(S.Fehlercode.Ungueltig, "wert");
                }
                var vorher = Konten.Stand.Konten.TryGetValue(kv.KontoId, out var kvor) ? kvor.DiscordName : null;
                var n = Konten.DiscordNameSetzen(akteur, kv.KontoId, name);
                if (!n.Ok) return NamensFehler(n);
                if (!string.Equals(vorher, n.DiscordName, StringComparison.Ordinal))
                {
                    Vermerken("DISCORDNAME", v.KontoId, kv.KontoId,
                        $"Discord-Name von {KontoText(kv.KontoId)}: {n.DiscordName ?? "(keiner)"} statt {vorher ?? "(keiner)"} durch {KontoText(v.KontoId)}");
                    KontoGeaendert(kv.KontoId);
                }
                return Antwort.Leer();
            }
            default:
                e = Konten.KontoLoeschen(akteur, kv.KontoId);
                text = "gelöscht";
                break;
        }
        if (!e.Ok) return ModulFehler(v, e.Fehler);
        Vermerken("KONTO", v.KontoId, kv.KontoId, $"Konto {ziel} {text} durch {KontoText(v.KontoId)}");
        WirkungenAusfuehren(e.Wirkungen);
        return Antwort.Leer();
    }

    /// <summary>
    /// GeraetEntscheiden (3.5, F14): bei der Freigabe eines Antrags auf Wunsch eine Vormerkung übernehmen (Rollen gelten
    /// sofort, auch im wartenden Client) oder das Gerät an ein vorhandenes Konto hängen.
    /// </summary>
    private Antwort GeraetEntscheiden(Verbindung v, I.Akteur akteur, S.GeraetEntscheidenAnfrage ge)
    {
        if (ge.Entscheidung == S.GeraetEntscheidung.Entsperren) return GeraetEntsperren(v, akteur, ge.GeraetId);
        // Vorher festhalten: Ablehnen löscht seit 0.7.0 Gerät und Antrag, das Journal nennt beide trotzdem.
        var vorher = Konten.Stand.Geraete.TryGetValue(ge.GeraetId, out var g) ? g : null;
        var vonVorher = vorher is null ? "" : KontoText(vorher.KontoId);
        var (e, vormerkung) = GeraetEntscheidenIntern(akteur, ge.GeraetId, Abbildung.Entscheidung(ge.Entscheidung), ge.VormerkungId, ge.KontoId);
        if (!e.Ok) return ModulFehler(v, e.Fehler);
        if (vorher is not null) FreigabeVermerken(v.KontoId, vorher, vonVorher, ge.Entscheidung, vormerkung);
        WirkungenAusfuehren(e.Wirkungen);
        return Antwort.Leer();
    }

    /// <summary>
    /// ENTSPERREN (seit 0.8.0): Sperre eines Geräts aufheben, mit den Rechten des Sperrens (<see cref="I.Kontenbuch.GeraetEntsperren"/>,
    /// frischer Code). Das Gerät ist danach ENTFERNT, der PC kann neu beantragen. Das Journal nennt Gerät, Konto und wer.
    /// </summary>
    private Antwort GeraetEntsperren(Verbindung v, I.Akteur akteur, uint geraetId)
    {
        var e = Konten.GeraetEntsperren(akteur, geraetId);
        if (!e.Ok) return ModulFehler(v, e.Fehler);
        if (Konten.Stand.Geraete.TryGetValue(geraetId, out var g))
            Vermerken("SPERRE_AUFGEHOBEN", v.KontoId, g.KontoId,
                $"Gerät {g.Name} ({g.Anzeige}) von {KontoText(g.KontoId)}: Sperre aufgehoben durch {KontoText(v.KontoId)}, der PC kann neu beantragen");
        WirkungenAusfuehren(e.Wirkungen);
        return Antwort.Leer();
    }

    /// <summary>Gemeinsam für Client und Konsole: Zusatz bauen, entscheiden, Vormerkung übernehmen.</summary>
    private (I.Ergebnis Ergebnis, I.Vormerkung? Vormerkung) GeraetEntscheidenIntern(I.Akteur akteur, uint geraetId, I.Entscheidung entscheidung,
        uint? vormerkungId, uint? anKonto)
    {
        I.Freigabezusatz? zusatz = null;
        I.Vormerkung? vm = null;
        if (vormerkungId is { } vid)
        {
            if (!Mitglieder.Stand.Vormerkungen.TryGetValue(vid, out vm)) return (new I.Ergebnis(I.Fehlercode.Unbekannt, []), null);
            zusatz = new I.Freigabezusatz(Vormerkung: new I.Vormerkungsuebernahme(vm.Id, vm.ScName, vm.DiscordName));
        }
        else if (anKonto is { } kid)
        {
            zusatz = new I.Freigabezusatz(AnKonto: kid);
        }
        var e = Konten.GeraetEntscheiden(akteur, geraetId, entscheidung, zusatz);
        if (e.Ok && vm is not null && Konten.Stand.Geraete.TryGetValue(geraetId, out var g)) Mitglieder.VormerkungUebernehmen(vm.Id, g.KontoId);
        return (e, vm);
    }

    /// <summary>
    /// Journal für FREIGEBEN und ABLEHNEN. <paramref name="vorher"/> und <paramref name="vonVorher"/> stammen von vor der
    /// Entscheidung: Ablehnen löscht seit 0.7.0 das Gerät und einen Antrag samt Konto. Nach einer Freigabe nennt der Eintrag das
    /// Konto, an dem das Gerät jetzt hängt, mit seinem neuen Namen.
    /// </summary>
    private void FreigabeVermerken(uint durch, I.Geraet vorher, string vonVorher, S.GeraetEntscheidung entscheidung, I.Vormerkung? vormerkung)
    {
        if (entscheidung is not (S.GeraetEntscheidung.Freigeben or S.GeraetEntscheidung.Ablehnen)) return;
        var kontoId = Konten.Stand.Geraete.TryGetValue(vorher.GeraetId, out var g) ? g.KontoId : vorher.KontoId;
        var von = Konten.Stand.Konten.ContainsKey(kontoId) ? KontoText(kontoId) : vonVorher;
        var freigegeben = entscheidung == S.GeraetEntscheidung.Freigeben;
        var zusatz = vormerkung is null ? "" : $", Vormerkung {vormerkung.ScName} übernommen";
        Vermerken(freigegeben ? "GERAET_FREIGEGEBEN" : "GERAET_ABGELEHNT", durch, kontoId,
            $"Gerät {vorher.Name} ({vorher.Anzeige}) von {von} {(freigegeben ? "freigegeben" : "abgelehnt und gelöscht")} durch {(durch == 0 ? "Konsole" : KontoText(durch))}{zusatz}");
    }

    /// <summary>Eintrag der GeraeteListe mit Namen des Antrags und, für Admin-Sitzungen, den passenden Vormerkungen.</summary>
    private S.GeraetEintrag GeraetEintragBauen(I.Kontenstand stand, I.Geraet g, bool fuerAdmin)
    {
        stand.Konten.TryGetValue(g.KontoId, out var k);
        return Abbildung.GeraetEintrag(stand, g, k is null ? [] : Konten.VergebeneNamen(k),
            fuerAdmin && k is { IstAntrag: true } ? Vorschlaege(k) : null, fuerAdmin && k is { IstAntrag: true } && Konten.DiscordNameVergeben(k));
    }

    /// <summary>Wem ein Discord-Name gehört, als Text für Konsole und Verwaltungsteil: "Mustermann (Konto 7)" oder "die Vormerkung Hexa".</summary>
    private string DiscordBesitzerText(uint? kontoId, uint? vormerkungId) =>
        kontoId is { } k ? KontoText(k)
        : vormerkungId is { } v && Mitglieder.Stand.Vormerkungen.TryGetValue(v, out var vm) ? "die Vormerkung " + vm.ScName
        : "jemand";

    /// <summary>
    /// Schranke vor der Verwaltung von Netzen, Schiffen und Rollen (A43, 3.9): <c>NETZE_VERWALTEN</c> nur in einer Admin-Sitzung,
    /// Löschen nur mit frischer Bestätigung. SELBSTWAHL darf auch die Einsatzleitung (ZUWEISEN) ohne Admin-Sitzung.
    /// </summary>
    private Antwort? VerwaltungSchranke(Verbindung v, I.Akteur akteur, S.VerwaltungsOperation op)
    {
        var stand = Konten.Stand;
        // ZUWEISEN kommt auch aus einem laufenden Auftrag (6.12); deshalb am Lagezustand zum Zeitpunkt, nicht am Kontenbuch.
        if (op == S.VerwaltungsOperation.Selbstwahl && Schreiber.Aktuell.RechteVon(v.KontoId, Zeit.LageMs()).Hat(L.Recht.Zuweisen)) return null;
        var f = I.Kontenbuch.RechtPruefen(stand, akteur, I.Rechte.NetzeVerwalten);
        if (f != I.Fehlercode.Keiner) return Antwort.Fehl(Abbildung.Fehler(f));
        // Löschen und ein ganzes Paket ("Übernehmen" der Flotte, N5) nur mit frischer Bestätigung.
        if (op is S.VerwaltungsOperation.NetzLoeschen or S.VerwaltungsOperation.SchiffLoeschen or S.VerwaltungsOperation.RolleLoeschen
                or S.VerwaltungsOperation.Paket or S.VerwaltungsOperation.PhaseLoeschen
            && !akteur.Bestaetigt)
            return SchrittBestaetigen(v);
        return null;
    }

    internal static I.Stufe? StufeAusText(string? text) => text switch
    {
        "ADMIN" => I.Stufe.Admin,
        "RAIDLEITER" => I.Stufe.Raidleiter,
        "FUNKER" => I.Stufe.Funker,
        _ => null,
    };

    /// <summary>Verwaltung (4.5): Netze, Schiffe, Rollen, Selbstwahl und Einstellung, atomar über den Zustandsschreiber.</summary>
    private Antwort? Verwaltung(uint konto, S.VerwaltungAnfrage va, TaskCompletionSource<Antwort> tcs)
    {
        L.Auftrag auftrag;
        Antwort? fehler;
        switch (va.Op)
        {
            case S.VerwaltungsOperation.NetzSetzen:
                if (!Lesen<S.Netz>(va, out var netz, out fehler)) return fehler;
                auftrag = L.Auftrag.NetzSetzen(konto, Abbildung.NetzLage(netz), va.Version);
                break;
            case S.VerwaltungsOperation.SchiffSetzen:
                if (!Lesen<S.Schiff>(va, out var schiff, out fehler)) return fehler;
                auftrag = L.Auftrag.SchiffSetzen(konto, Abbildung.SchiffLage(schiff), va.Version);
                break;
            case S.VerwaltungsOperation.RolleSetzen:
                if (!Lesen<S.Rolle>(va, out var rolle, out fehler)) return fehler;
                auftrag = L.Auftrag.RolleSetzen(konto, Abbildung.RolleLage(rolle), va.Version);
                break;
            case S.VerwaltungsOperation.NetzLoeschen or S.VerwaltungsOperation.SchiffLoeschen or S.VerwaltungsOperation.RolleLoeschen:
                if (!Lesen<S.IdObjekt>(va, out var idObjekt, out fehler)) return fehler;
                auftrag = va.Op switch
                {
                    S.VerwaltungsOperation.NetzLoeschen => L.Auftrag.NetzLoeschen(konto, idObjekt.Id, va.Version),
                    S.VerwaltungsOperation.SchiffLoeschen => L.Auftrag.SchiffLoeschen(konto, idObjekt.Id, va.Version),
                    _ => L.Auftrag.RolleLoeschen(konto, idObjekt.Id, va.Version),
                };
                break;
            case S.VerwaltungsOperation.Selbstwahl:
                // {an} wie bisher, seit 0.3.0 mit nachzuegler (F9); Clients 0.2.0 schicken nur an.
                if (!Lesen<S.SelbstwahlObjekt>(va, out var an, out fehler)) return fehler;
                auftrag = L.Auftrag.SelbstwahlSetzen(konto, an.An, va.Version, an.Nachzuegler);
                break;
            case S.VerwaltungsOperation.Paket:
            {
                if (!Lesen<S.VerwaltungsPaket>(va, out var paket, out fehler)) return fehler;
                var (schritte, text) = PaketLesen(paket);
                if (schritte is null) return Antwort.Fehl(S.Fehlercode.Ungueltig, text);
                auftrag = L.Auftrag.Paket(konto, schritte, va.Version);
                break;
            }
            case S.VerwaltungsOperation.PhaseSetzen:
                if (!Lesen<S.NetzPhase>(va, out var phase, out fehler)) return fehler;
                auftrag = L.Auftrag.PhaseSetzen(konto, Abbildung.EinsatzphaseLage(phase), va.Version);
                break;
            case S.VerwaltungsOperation.PhaseLoeschen:
                if (!Lesen<S.IdObjekt>(va, out var phaseNr, out fehler)) return fehler;
                if (phaseNr.Id is 0 or > S.Grenzen.PhaseNrMax) return Antwort.Fehl(S.Fehlercode.Ungueltig, "objekt.id");
                auftrag = L.Auftrag.PhaseLoeschen(konto, (byte)phaseNr.Id, va.Version);
                break;
            default:
                if (!Lesen<S.EinstellungObjekt>(va, out var einst, out fehler)) return fehler;
                auftrag = L.Auftrag.EinstellungSetzen(konto, einst.DurchsageNurTaste, va.Version);
                break;
        }
        Schreiben(auftrag, e =>
        {
            if (e.Erfolg && va.Op != S.VerwaltungsOperation.Selbstwahl)
                Vermerken("VERWALTUNG", konto, null, $"Verwaltung {va.Op} durch {KontoText(konto)}");
            tcs.TrySetResult(e.Erfolg ? Antwort.Leer() : FehlerAus(e));
        });
        return null;
    }

    /// <summary>Liest <c>Verwaltung.objekt</c>; ein Regelverstoß im Objekt ist UNGUELTIG ohne Trennung (4.3).</summary>
    private static bool Lesen<T>(S.VerwaltungAnfrage va, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? wert, out Antwort? fehler)
        where T : class, S.ISteuerBody
    {
        var e = va.ObjektLesen<T>();
        wert = e.Body as T;
        fehler = wert is null ? Antwort.Fehl(S.Fehlercode.Ungueltig, e.Feld ?? "objekt") : null;
        return wert is not null;
    }
}
