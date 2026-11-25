// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Funk.Server.Betrieb;
using Funk.Server.Lage;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Raidleitung, Server und Protokoll (Fassung 0.3.0, RAIDLEITUNG-ENTWURF Bauplan S1, M1 und der Serverteil von M3 bis M5):
/// Auftrag für einen Abend (0x0017) mit Journal, Meldung, Bot-Zeile und Takt; das Kontenverzeichnis für das Brett (0x0038);
/// die Belegung als Push (0x4003); die Konsolenbefehle <c>auftrag</c>, <c>auftrag-entziehen</c>, <c>auftraege</c> und
/// <c>flotte-vorlage</c>; das Verwaltungspaket (N5) vom Draht in die Lage.
/// </summary>
public sealed partial class Zentrale
{
    /// <summary>So oft prüft der Takt die Fristen der Aufträge und der Selbstwahlsperre (6.12: spätestens 5 s nach dem Ablauf).</summary>
    private const long FristenTaktMs = 1000;

    /// <summary>So oft wird die Belegung auch ohne neuen Stand neu gerechnet: Platzfristen laufen ohne Änderung ab (6.9).</summary>
    private const long BelegungTaktMs = 10_000;

    private long _letzteAuftragsfristen;
    private long _letzteBelegung;

    /// <summary>Bis wann (Lage-ms) abgelaufene Platzfristen gemeldet sind; Clients, die später kommen, bekommen den vollen Stand.</summary>
    private long _platzfristenGemeldet = Zeit.LageMs();
    private long _belegungVersion = -1;
    private L.Belegungsstand? _belegung;

    // ---------------------------------------------------------------- Anfragen

    /// <summary>
    /// Beauftragung (0x0017). ERTEILEN und ENTZIEHEN: <c>KONTEN_VERWALTEN</c> in einer Admin-Sitzung (sonst KEIN_RECHT bzw.
    /// ADMIN_SITZUNG_NOETIG), die Regeln vor der frischen Bestätigung (damit ein verbotener Auftrag nicht nach einem Code fragt),
    /// dann SCHRITT_BESTAETIGEN oder der Auftrag. LISTE in einer Admin-Sitzung. ABGEBEN der Beauftragte selbst.
    /// </summary>
    private Antwort? Beauftragung(Verbindung v, I.Akteur akteur, S.BeauftragungAnfrage a, TaskCompletionSource<Antwort> tcs)
    {
        var jetzt = Zeit.LageMs();
        if (a.Op == S.BeauftragungOperation.Abgeben)
        {
            Schreiben(L.Auftrag.BeauftragungAbgeben(v.KontoId), e => tcs.TrySetResult(e.Erfolg
                ? Antwort.Mit(new S.BeauftragungAntwort { Beauftragungen = [] })
                : FehlerAus(e)));
            return null;
        }
        var recht = I.Kontenbuch.RechtPruefen(Konten.Stand, akteur, I.Rechte.KontenVerwalten);
        if (recht != I.Fehlercode.Keiner) return Antwort.Fehl(Abbildung.Fehler(recht));
        if (a.Op == S.BeauftragungOperation.Liste) return Antwort.Mit(BeauftragungListe(jetzt));

        var kontoId = a.KontoId!.Value;
        var z = Schreiber.Aktuell;
        if (a.Op == S.BeauftragungOperation.Erteilen)
        {
            var rechte = a.Rechte is null ? (L.Recht?)null : Abbildung.RechtLage(Abbildung.RechteAus(a.Rechte));
            var bis = a.Bis ?? S.Auftragszeit.NaechsterMorgen(Math.Max(a.Von ?? jetzt, jetzt));
            var (code, text, _) = L.Zustandsschreiber.BeauftragungBilden(z.Konten, v.KontoId, kontoId, a.Von, bis, rechte, a.Grund, jetzt);
            if (code != L.Fehlercode.Keiner) return Antwort.Fehl(Abbildung.Fehler(code), text);
            if (!akteur.Bestaetigt) return SchrittBestaetigen(v);
            Schreiben(L.Auftrag.BeauftragungErteilen(v.KontoId, kontoId, a.Von, bis, rechte, a.Grund),
                e => tcs.TrySetResult(e.Erfolg ? Antwort.Mit(BeauftragungListe(Zeit.LageMs())) : FehlerAus(e)));
            return null;
        }
        if (!z.Konten.TryGetValue(kontoId, out var ziel) || ziel.Beauftragung is null) return Antwort.Fehl(S.Fehlercode.Unbekannt);
        if (!akteur.Bestaetigt) return SchrittBestaetigen(v);
        Schreiben(L.Auftrag.BeauftragungEntziehen(v.KontoId, kontoId),
            e => tcs.TrySetResult(e.Erfolg ? Antwort.Mit(BeauftragungListe(Zeit.LageMs())) : FehlerAus(e)));
        return null;
    }

    private S.BeauftragungAntwort BeauftragungListe(long jetzt)
    {
        var stand = Konten.Stand;
        return new S.BeauftragungAntwort
        {
            Beauftragungen = [.. Schreiber.Aktuell.Konten.Values
                .Where(k => k.Beauftragung is { } b && b.Bis > jetzt)
                .OrderBy(k => k.Beauftragung!.Von)
                .Take(S.Grenzen.AuftraegeMax)
                .Select(k =>
                {
                    var b = k.Beauftragung!;
                    stand.Konten.TryGetValue(k.Id, out var ik);
                    return new S.BeauftragungEintrag
                    {
                        KontoId = k.Id,
                        Name = k.Name.Length > 0 ? k.Name : ik?.Name ?? "?",
                        ScName = ik?.Hauptname,
                        Von = b.Von,
                        Bis = b.Bis,
                        Rechte = Abbildung.Rechte(b.Rechte),
                        Durch = b.Durch,
                        Seit = b.Seit,
                        Grund = b.Grund,
                        Aktiv = b.GiltZu(jetzt),
                    };
                })],
        };
    }

    /// <summary>
    /// KontenVerzeichnis (0x0038, N4): alle freigegebenen Konten, auch offline, für "Alle Mitglieder" auf dem Brett; ohne
    /// Discord-ID, ohne Anträge und gesperrte Konten. Nur mit <c>ZUWEISEN</c> (auch aus einem Auftrag).
    /// </summary>
    private Antwort KontenVerzeichnis(Verbindung v)
    {
        var z = Schreiber.Aktuell;
        var jetzt = Zeit.LageMs();
        if (!HatRecht(z, v, L.Recht.Zuweisen)) return Antwort.Fehl(S.Fehlercode.KeinRecht);
        var stand = Konten.Stand;
        var m = Mitglieder.Stand;
        var liste = new List<S.VerzeichnisEintrag>();
        foreach (var k in z.Konten.Values.OrderBy(k => k.Id))
        {
            if (k.Id == L.InterneAuftraege.AdminWerkzeug || k.Status != L.KontoStatus.Aktiv) continue;
            if (!stand.Konten.TryGetValue(k.Id, out var ik) || ik.IstAntrag) continue;
            var platz = z.PlatzZustandVon(k.Id, jetzt);
            liste.Add(new S.VerzeichnisEintrag
            {
                KontoId = k.Id,
                Name = k.Name.Length > 0 ? k.Name : ik.Name,
                Benutzername = Abbildung.Benutzername(ik),
                ScName = ik.Hauptname,
                DiscordName = ik.DiscordName,
                Stufe = Abbildung.Stufe(k.Stufe),
                Online = z.IstOnline(k.Id),
                SchiffId = platz != L.PlatzZustand.Frei && k.Platz is { SchiffId: not 0 } p ? p.SchiffId : null,
                RolleId = platz != L.PlatzZustand.Frei ? k.Platz?.RolleId : null,
                Platz = platz switch { L.PlatzZustand.Belegt => S.PlatzStatus.Belegt, L.PlatzZustand.Reserviert => S.PlatzStatus.Reserviert, _ => null },
                LetzterPlatz = k.LetzterStand?.Platz is { } lp ? new S.LetzterPlatz { SchiffId = lp.SchiffId == 0 ? null : lp.SchiffId, RolleId = lp.RolleId } : null,
                AlsFunker = k.AlsFunkerZu(jetzt) ? true : null,
            });
            if (liste.Count == S.Grenzen.KontenMax) break;
        }
        return Antwort.Mit(new S.KontenVerzeichnisAntwort { Konten = liste });
    }

    /// <summary>
    /// Verwaltung PAKET (N5) vom Draht in Schritte der Lage. Jeder Schritt wird wie einzeln gelesen und geprüft; ein Fehler nennt
    /// den Schritt (<c>ops[3]: ...</c>) und ändert nichts.
    /// </summary>
    internal static (List<L.Verwaltungsschritt>? Schritte, string? Fehler) PaketLesen(S.VerwaltungsPaket paket)
    {
        ArgumentNullException.ThrowIfNull(paket);
        var schritte = new List<L.Verwaltungsschritt>(paket.Ops.Count);
        for (var i = 0; i < paket.Ops.Count; i++)
        {
            var s = paket.Ops[i];
            string? feld = null;
            L.Verwaltungsschritt? schritt = null;
            switch (s.Op)
            {
                case S.VerwaltungsOperation.NetzSetzen:
                    if (s.ObjektLesen<S.Netz>() is { Body: S.Netz n }) schritt = new L.Verwaltungsschritt.NetzSetzen(Abbildung.NetzLage(n));
                    else feld = s.ObjektLesen<S.Netz>().Feld;
                    break;
                case S.VerwaltungsOperation.SchiffSetzen:
                    if (s.ObjektLesen<S.Schiff>() is { Body: S.Schiff sc }) schritt = new L.Verwaltungsschritt.SchiffSetzen(Abbildung.SchiffLage(sc));
                    else feld = s.ObjektLesen<S.Schiff>().Feld;
                    break;
                case S.VerwaltungsOperation.RolleSetzen:
                    if (s.ObjektLesen<S.Rolle>() is { Body: S.Rolle r }) schritt = new L.Verwaltungsschritt.RolleSetzen(Abbildung.RolleLage(r));
                    else feld = s.ObjektLesen<S.Rolle>().Feld;
                    break;
                case S.VerwaltungsOperation.NetzLoeschen or S.VerwaltungsOperation.SchiffLoeschen or S.VerwaltungsOperation.RolleLoeschen:
                    if (s.ObjektLesen<S.IdObjekt>() is { Body: S.IdObjekt id })
                    {
                        schritt = s.Op switch
                        {
                            S.VerwaltungsOperation.NetzLoeschen => new L.Verwaltungsschritt.NetzLoeschen(id.Id),
                            S.VerwaltungsOperation.SchiffLoeschen => new L.Verwaltungsschritt.SchiffLoeschen(id.Id),
                            _ => new L.Verwaltungsschritt.RolleLoeschen(id.Id),
                        };
                    }
                    else feld = "objekt.id";
                    break;
                case S.VerwaltungsOperation.Einstellung:
                    if (s.ObjektLesen<S.EinstellungObjekt>() is { Body: S.EinstellungObjekt e }) schritt = new L.Verwaltungsschritt.Einstellung(e.DurchsageNurTaste);
                    else feld = "objekt";
                    break;
                case S.VerwaltungsOperation.PhaseSetzen:
                    if (s.ObjektLesen<S.NetzPhase>() is { Body: S.NetzPhase np }) schritt = new L.Verwaltungsschritt.PhaseSetzen(Abbildung.EinsatzphaseLage(np));
                    else feld = s.ObjektLesen<S.NetzPhase>().Feld;
                    break;
                case S.VerwaltungsOperation.PhaseLoeschen:
                    if (s.ObjektLesen<S.IdObjekt>() is { Body: S.IdObjekt pid } && pid.Id is > 0 and <= S.Grenzen.PhaseNrMax) schritt = new L.Verwaltungsschritt.PhaseLoeschen((byte)pid.Id);
                    else feld = "objekt.id";
                    break;
            }
            if (schritt is null) return (null, string.Create(CultureInfo.InvariantCulture, $"ops[{i}]: {feld ?? "op"}"));
            schritte.Add(schritt);
        }
        return (schritte, null);
    }

    // ---------------------------------------------------------------- Folgen

    /// <summary>
    /// Folgen eines Auftrags (6.12 Regeln 8 und 9): Journal, Meldung BEAUFTRAGUNG an den Beauftragten und die Admin-Sitzungen mit
    /// <c>KONTEN_VERWALTEN</c>, eine Zeile für den Admin-Kanal (bis der Bot-Dienst läuft, nur im Logbuch) und, wenn der Auftrag
    /// gerade wirksam wurde oder endete, ein voller Praesenz-Stand mit oder ohne Details: Das Brett erscheint und verschwindet
    /// ohne Neustart. Wer kein <c>SPRECHANZEIGE_ALLE</c> mehr hat, verliert sein Abo.
    /// </summary>
    private void BeauftragungFolgenVerteilen(L.Ergebnis e)
    {
        foreach (var f in e.Beauftragungen)
        {
            var op = L.Drahtnamen.Von(f.Anlass);
            Vermerken("AUFTRAG_" + op, f.Durch, f.KontoId, AuftragText(f));
            Log.Info("Bot-Meldung AUFTRAG_" + op + " an Admin-Kanal (kein Bot-Dienst angebunden)");
            MeldungSenden(S.MeldungsArten.Beauftragung, L.Recht.KontenVerwalten, f.KontoId, auchInhaber: true, details: Antwort.DetailsBauen(w =>
            {
                w.WriteString("op", op);
                w.WriteNumber("von", f.Auftrag.Von);
                w.WriteNumber("bis", f.Auftrag.Bis);
                w.WriteNumber("durch", f.Auftrag.Durch);
                if (f.Auftrag.Grund is { } g) w.WriteString("grund", g);
                if (f.Grund is { } warum) w.WriteString("warum", warum);
            }));
            // Wirksamkeit gewechselt: begonnen, oder ein schon begonnener endet.
            var wechsel = f.Anlass == L.BeauftragungsAnlass.Begonnen || (f.Auftrag.Begonnen && f.Anlass != L.BeauftragungsAnlass.Erteilt)
                || (f.Anlass == L.BeauftragungsAnlass.Erteilt && f.Auftrag.Begonnen);
            if (!wechsel || SitzungVonKonto(f.KontoId) is not { } v) continue;
            var z = Schreiber.Aktuell;
            var (mit, ohne) = Praesenz(z, null, voll: true);
            v.Push(S.Nachrichtentyp.Praesenz, HatRecht(z, v, L.Recht.Zuweisen) ? mit : ohne);
            if (!HatRecht(z, v, L.Recht.SprechanzeigeAlle)) v.SprechanzeigeAbo = false;
        }
    }

    /// <summary>
    /// Folgen des Funker-Modus (6.15), wie beim Beginn und Ende eines Auftrags (6.12 Regel 9): Journal, ein voller Praesenz-Stand mit
    /// oder ohne Details an das Konto (das Brett verschwindet und erscheint ohne Neustart) und das Ende eines Sprechanzeige-Abos ohne
    /// <c>SPRECHANZEIGE_ALLE</c>. Mitgliedschaften mit <c>konto.alsFunkerBis</c> und das Praesenz-Delta an alle gehen schon über die
    /// betroffenen Konten.
    /// </summary>
    private void FunkermodusFolgenVerteilen(L.Ergebnis e)
    {
        foreach (var f in e.Funkermodi)
        {
            var wer = KontoText(f.KontoId);
            var text = f.An
                ? $"{wer} spielt als Funker bis {S.Auftragszeit.Anzeigen(f.Bis)}"
                : $"{wer} wieder als Admin" + f.Grund switch
                {
                    "ABGELAUFEN" => " (Funker-Modus abgelaufen)",
                    "STUFE" => " (Stufe geändert)",
                    "GESPERRT" => " (Konto gesperrt)",
                    _ => "",
                };
            Vermerken(f.An ? "FUNKER_MODUS_EIN" : "FUNKER_MODUS_AUS", f.Grund is null ? f.KontoId : KonsoleDurch, f.KontoId, text.Length <= 200 ? text : text[..200]);
            if (SitzungVonKonto(f.KontoId) is not { } v) continue;
            var z = Schreiber.Aktuell;
            var (mit, ohne) = Praesenz(z, null, voll: true);
            v.Push(S.Nachrichtentyp.Praesenz, HatRecht(z, v, L.Recht.Zuweisen) ? mit : ohne);
            if (!HatRecht(z, v, L.Recht.SprechanzeigeAlle)) v.SprechanzeigeAbo = false;
        }
    }

    /// <summary>Journaltext eines Auftrags mit Klarnamen, Zeiten in Ortszeit (Europe/Berlin), höchstens 200 Zeichen.</summary>
    private string AuftragText(L.BeauftragungsFolge f)
    {
        var b = f.Auftrag;
        var wer = KontoText(f.KontoId);
        var durch = f.Durch == KonsoleDurch ? "Konsole" : KontoText(f.Durch);
        var anlass = b.Grund is { } g ? " (" + g + ")" : "";
        var text = f.Anlass switch
        {
            L.BeauftragungsAnlass.Erteilt => $"Einsatzleitung für {wer} von {S.Auftragszeit.Anzeigen(b.Von)} bis {S.Auftragszeit.Anzeigen(b.Bis)}{anlass}, erteilt durch {durch}",
            L.BeauftragungsAnlass.Begonnen => $"Einsatzleitung von {wer} beginnt, bis {S.Auftragszeit.Anzeigen(b.Bis)}{anlass}",
            L.BeauftragungsAnlass.Entzogen => $"Einsatzleitung von {wer} entzogen durch {durch}{anlass}",
            L.BeauftragungsAnlass.Abgegeben => $"Einsatzleitung von {wer} abgegeben{anlass}",
            L.BeauftragungsAnlass.Abgelaufen => $"Einsatzleitung von {wer} abgelaufen ({S.Auftragszeit.Anzeigen(b.Bis)}){anlass}",
            _ => $"Einsatzleitung von {wer} beendet: " + f.Grund switch
            {
                "GESPERRT" => "Konto gesperrt",
                "STUFE" => "Stufe geändert",
                "GELOESCHT" => "Konto gelöscht",
                "ERTEILER" => "der Erteilende darf keine Konten mehr verwalten",
                _ => f.Grund ?? "",
            },
        };
        return text.Length <= 200 ? text : text[..200];
    }

    /// <summary>
    /// Belegung (0x4003) an alle AKTIV-Sitzungen, wenn sie sich geändert hat: nach jedem Commit mit neuem Stand und alle 10 s,
    /// weil Platzfristen ohne Änderung ablaufen.
    /// </summary>
    private void BelegungVerteilen(bool erzwingen)
    {
        var z = Schreiber.Aktuell;
        if (!erzwingen && z.ZustandsVersion == _belegungVersion) return;
        _belegungVersion = z.ZustandsVersion;
        var b = z.Belegung(Zeit.LageMs());
        if (b.Gleich(_belegung)) return;
        _belegung = b;
        var push = Abbildung.Belegung(z, b);
        foreach (var v in AktiveVerbindungen()) v.Push(S.Nachrichtentyp.Belegung, push);
    }

    /// <summary>Die Belegung für eine Verbindung, die den ganzen Stand anfordert (Zustand).</summary>
    private void BelegungSenden(Verbindung v)
    {
        var z = Schreiber.Aktuell;
        v.Push(S.Nachrichtentyp.Belegung, Abbildung.Belegung(z, z.Belegung(Zeit.LageMs())));
    }

    /// <summary>Takt der Raidleitung: Fristen der Aufträge und der Selbstwahlsperre (1 s), abgelaufene Platzfristen und Belegung (10 s).</summary>
    private void RaidleitungTakt(long monoMs)
    {
        if (monoMs - _letzteAuftragsfristen >= FristenTaktMs)
        {
            _letzteAuftragsfristen = monoMs;
            if (FristFaellig(Schreiber.Aktuell, Schreiber.Neben, Zeit.LageMs())) Schreiben(L.Auftrag.FristenPruefen());
        }
        if (monoMs - _letzteBelegung >= BelegungTaktMs)
        {
            _letzteBelegung = monoMs;
            PlatzfristenMelden();
            BelegungVerteilen(erzwingen: true);
        }
    }

    /// <summary>
    /// Plätze, deren Frist seit dem letzten Takt ohne jede Aktion abgelaufen ist (Wiederverbindung 10 min, Reservierung 12 h),
    /// gehen als Praesenz-Delta hinaus (6.9). Ohne das stand eine Person, die vor 10 Minuten gegangen ist, auf dem Brett weiter
    /// im Block, und das Brett lehnte Ablagen dort als "voll" ab, obwohl der Server den Platz frei sah (Gegenprüfung 01.10.2026).
    /// </summary>
    private void PlatzfristenMelden()
    {
        var jetzt = Zeit.LageMs();
        var gewechselt = Schreiber.Aktuell.PlatzzustandGewechselt(_platzfristenGemeldet, jetzt);
        _platzfristenGemeldet = jetzt;
        if (gewechselt.Count > 0) PraesenzDelta(gewechselt);
    }

    /// <summary>
    /// Ob ein Auftrag begonnen hat oder abgelaufen ist, ein Funker-Modus abgelaufen ist (6.15), die laufende Phase seit 12 h nicht
    /// geschaltet wurde (6.14 Regel 12), eine Kanalsperre endet (6.17) oder die Sperre der Selbstwahl enden muss. Jede neue Frist gehört hierher, sonst kommt ihr
    /// Ablauf nie an (CODEMAP, Platzfristen).
    /// </summary>
    private static bool FristFaellig(L.Schnappschuss z, L.Nebenstand neben, long jetzt)
    {
        if (neben.Phase is { Nr: > 1 } p && jetzt - p.Seit >= L.Fristen.PhaseRueckfallMs) return true;
        // Kanalsperren (6.17): abgelaufen, oder der Setzende hat kein ZUWEISEN mehr.
        bool SperreFaellig(L.Kanalsperre s) => s.Bis <= jetzt || (s.Durch != 0 && !z.RechteVon(s.Durch, jetzt).Hat(L.Recht.Zuweisen));
        foreach (var s in z.SperrenAlle)
            if (SperreFaellig(s)) return true;
        foreach (var k in z.Konten.Values)
        {
            if (k.Beauftragung is { } b && (b.Bis <= jetzt || (!b.Begonnen && b.Von <= jetzt))) return true;
            if (k.AlsFunkerBis != 0 && k.AlsFunkerBis <= jetzt) return true;
            foreach (var s in k.Sperren)
                if (SperreFaellig(s)) return true;
        }
        var e = z.Einstellungen;
        if (e.Selbstwahl) return false;
        return (e.SelbstwahlBis != 0 && e.SelbstwahlBis <= jetzt)
            || (e.SelbstwahlDurch != 0 && !z.RechteVon(e.SelbstwahlDurch, jetzt).Hat(L.Recht.Zuweisen));
    }

    // ---------------------------------------------------------------- Konsole

    /// <summary>
    /// <c>auftrag --konto &lt;id&gt; [--von] [--bis] [--rechte A,B] [--grund]</c>, <c>auftrag-entziehen --konto &lt;id&gt;</c>,
    /// <c>auftraege</c> und <c>flotte-vorlage [--name starcitizen]</c>. Die Konsole ist der Eigentümer am Server; im Journal steht sie
    /// als Urheber 0.
    /// </summary>
    private VerwaltungsAntwort? RaidleitungKonsole(string befehl, JsonObject a, TaskCompletionSource<VerwaltungsAntwort> tcs)
    {
        var jetzt = Zeit.LageMs();
        switch (befehl)
        {
            case "auftraege":
            {
                var liste = BeauftragungListe(jetzt).Beauftragungen;
                var sb = new StringBuilder();
                var daten = new JsonArray();
                foreach (var b in liste)
                {
                    sb.Append(CultureInfo.InvariantCulture,
                        $"{KontoText(b.KontoId)}: {(b.Aktiv ? "läuft" : "geplant")}, {S.Auftragszeit.Anzeigen(b.Von)} bis {S.Auftragszeit.Anzeigen(b.Bis)}, "
                        + $"von {(b.Durch == KonsoleDurch ? "Konsole" : KontoText(b.Durch))}{(b.Grund is { } g ? ", " + g : "")}, Rechte {string.Join(' ', b.Rechte.Select(Rechtname))}\n");
                    daten.Add(new JsonObject
                    {
                        ["kontoId"] = b.KontoId,
                        ["von"] = b.Von,
                        ["bis"] = b.Bis,
                        ["aktiv"] = b.Aktiv,
                        ["durch"] = b.Durch,
                        ["grund"] = b.Grund,
                    });
                }
                return VerwaltungsAntwort.Gut(liste.Count == 0 ? "Keine Aufträge" : sb.ToString().TrimEnd(), daten);
            }
            case "auftrag":
            {
                if (Zahl(a, "konto") is not { } konto) return VerwaltungsAntwort.Fehler("konto fehlt");
                long? von = null;
                if (Text(a, "von") is { } vonText)
                {
                    von = S.Auftragszeit.Lesen(vonText, Zeit.UnixMs());
                    if (von is null) return VerwaltungsAntwort.Fehler("--von: HH:MM oder JJJJ-MM-TT HH:MM. Nichts geändert.");
                }
                var beginn = Math.Max(von ?? jetzt, jetzt);
                long bis;
                if (Text(a, "bis") is { } bisText)
                {
                    if (S.Auftragszeit.Lesen(bisText, beginn) is not { } b) return VerwaltungsAntwort.Fehler("--bis: HH:MM oder JJJJ-MM-TT HH:MM. Nichts geändert.");
                    bis = b;
                }
                else
                {
                    bis = S.Auftragszeit.NaechsterMorgen(beginn);
                }
                L.Recht? rechte = null;
                if (a["rechte"] is JsonArray r)
                {
                    if (!I.Rechteregeln.TryAusDrahtnamen(r.Select(x => x?.GetValue<string>() ?? ""), out var ir)) return VerwaltungsAntwort.Fehler("rechte ungültig. Nichts geändert.");
                    rechte = Abbildung.RechtLage(ir);
                }
                var grund = Text(a, "grund");
                Schreiben(L.InterneAuftraege.Verwaltung(befehl, z => z.BeauftragungErteilen(konto, von, bis, rechte, grund)), e => tcs.TrySetResult(e.Erfolg
                    ? VerwaltungsAntwort.Gut($"Einsatzleitung für {KontoText(konto)} von {S.Auftragszeit.Anzeigen(Math.Max(von ?? jetzt, jetzt))} bis {S.Auftragszeit.Anzeigen(bis)}.")
                    : VerwaltungsAntwort.Fehler(L.Drahtnamen.Von(e.Fehler) + (e.Text is { } t ? ": " + t : "") + " Nichts geändert.")));
                return null;
            }
            case "auftrag-entziehen":
            {
                if (Zahl(a, "konto") is not { } konto) return VerwaltungsAntwort.Fehler("konto fehlt");
                Schreiben(L.InterneAuftraege.Verwaltung(befehl, z => z.BeauftragungEntziehen(konto)), e => tcs.TrySetResult(e.Erfolg
                    ? VerwaltungsAntwort.Gut("Auftrag von " + KontoText(konto) + " entzogen.")
                    : VerwaltungsAntwort.Fehler(e.Fehler == L.Fehlercode.Unbekannt ? KontoText(konto) + " hat keinen Auftrag." : L.Drahtnamen.Von(e.Fehler))));
                return null;
            }
            case "flotte-vorlage":
            {
                var name = Text(a, "name") ?? "starcitizen";
                var z = Schreiber.Aktuell;
                if (!string.Equals(name, "starcitizen", StringComparison.OrdinalIgnoreCase)) return VerwaltungsAntwort.Fehler("Vorlagen: starcitizen. Nichts geändert.");
                // Die Vorlage legt neben die vorhandene Flotte; ein zweiter Lauf legte alle Kanäle mit Ch. doppelt an.
                if (z.Netze.Values.Any(n => n.Id != L.Netz.GlobalId && !n.Kanaele.IsEmpty))
                    return VerwaltungsAntwort.Fehler("Es gibt schon Kanalnetze; die Vorlage Star Citizen legt alle neu an und gehört auf eine Flotte ohne sie. Erst die Kanalnetze löschen. Nichts geändert.");
                return VorlageUebernehmen(befehl, S.Flottenvorlagen.StarCitizen(Abbildung.NetzListe(z)), tcs,
                    " Ab jetzt kommen nur noch Clients ab 0.6.0 hinein (Versionstor); Rückweg: die Kanalnetze löschen (HEIMSERVER-PROXMOX.md, Abschnitt 18).");
            }
        }
        return VerwaltungsAntwort.Fehler("unbekannter Befehl: " + befehl);
    }

    /// <summary>Eine Flottenvorlage als ein Paket übernehmen, mit Journal und den Annahmen in der Antwort; <paramref name="zusatz"/> folgt den Schritten.</summary>
    private VerwaltungsAntwort? VorlageUebernehmen(string befehl, S.Flottenvorlage vorlage, TaskCompletionSource<VerwaltungsAntwort> tcs, string zusatz)
    {
        var (schritte, fehler) = PaketLesen(vorlage.Paket);
        if (schritte is null) return VerwaltungsAntwort.Fehler("Vorlage fehlerhaft: " + fehler);
        Schreiben(L.InterneAuftraege.Verwaltung(befehl, ziel => ziel.Paket(schritte)), e =>
        {
            if (e.Erfolg) Vermerken("VERWALTUNG", KonsoleDurch, null, "Konsole: Flottenvorlage " + vorlage.Name + " übernommen");
            tcs.TrySetResult(e.Erfolg
                ? VerwaltungsAntwort.Gut($"{vorlage.Name} übernommen: {vorlage.Paket.Ops.Count} Schritte.{zusatz} Annahmen, bitte prüfen:\n  "
                    + string.Join("\n  ", vorlage.Annahmen.Select(x => "- " + x.Text)),
                    new JsonArray([.. vorlage.Annahmen.Select(x => (JsonNode)new JsonObject { ["bezug"] = x.Bezug, ["text"] = x.Text })]))
                : VerwaltungsAntwort.Fehler(L.Drahtnamen.Von(e.Fehler) + (e.Text is { } t ? ": " + t : "") + " Nichts geändert."));
        });
        return null;
    }

    private static string Rechtname(S.Recht r) => I.Rechteregeln.Drahtnamen(Abbildung.RechteAus([r])).Single();
}
