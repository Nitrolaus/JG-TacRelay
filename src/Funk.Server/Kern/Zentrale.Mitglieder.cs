// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Server.Betrieb;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Mitglieder, Rollen und "Wer ist da" (6.13, A46-A48): ProfilSetzen, Journal, Rollenkatalog, Mitglied, Lieblingsrollen,
/// Untergilde (seit 0.11.8 nur noch die Hülle), Mitgliedskarte, AntraegeAblehnen. Bearbeiten nur mit <c>KONTEN_VERWALTEN</c>
/// in einer Admin-Sitzung (A43); das prüfen Kontenbuch und Mitgliederbuch selbst am Akteur. Jede Änderung durch einen Admin
/// steht im Journal und geht als <c>MITGLIED_GEAENDERT</c> an die Admin-Sitzungen; betroffene Konten bekommen ihre
/// Mitgliedschaften neu.
/// </summary>
public sealed partial class Zentrale
{
    private Antwort ProfilSetzen(Verbindung v, I.Akteur akteur, S.ProfilSetzenAnfrage p)
    {
        // Abzeichen (0.11.7) vorher prüfen: Eine unbekannte Kennung lässt die ganze Anfrage scheitern, ohne dass sich Namen ändern.
        if (I.Mitgliederbuch.AnsichtPruefen(p.AbzeichenVorn, p.AbzeichenAus) is { } grund)
            return Antwort.Fehl(S.Fehlercode.Ungueltig, grund);
        var e = Konten.ProfilSetzen(akteur, p.ScNamen, p.DiscordName);
        // A53: einen anderen Discord-Namen nimmt ProfilSetzen nicht an; dann ändert sich auch sonst nichts.
        if (e.Fehler == I.Fehlercode.KeinRecht && p.DiscordName is not null)
            return Antwort.Fehl(S.Fehlercode.KeinRecht, "Den Discord-Namen ändert nur ein Admin");
        if (!e.Ok) return NamensFehler(e);
        if (p.ZeichenZeigen is { } zeigen) Mitglieder.ZeichenZeigenSetzen(v.KontoId, zeigen);
        if (p.KarteFreigeben is { } freigeben) Mitglieder.KarteFreigebenSetzen(v.KontoId, freigeben);
        Mitglieder.AbzeichenAnsichtSetzen(v.KontoId, p.AbzeichenVorn, p.AbzeichenAus);
        KontoGeaendert(v.KontoId);
        var d = Mitglieder.Stand.DatenVon(v.KontoId);
        return Antwort.Mit(new S.ProfilSetzenAntwort
        {
            ScNamen = [.. e.ScNamen],
            DiscordName = e.DiscordName,
            ZeichenZeigen = d.ZeichenZeigen,
            KarteFreigeben = d.KarteFreigeben,
            AbzeichenVorn = d.AbzeichenVorn,
            AbzeichenAus = [.. d.AbzeichenAus],
        });
    }

    /// <summary>
    /// Abzeichen (0x0025, seit 0.11.7): Ränge und Tätigkeiten eines Kontos setzen, nur in einer Admin-Sitzung mit
    /// <c>KONTEN_VERWALTEN</c> (das prüft das Mitgliederbuch). Danach wie bei Mitglied: Journal, MITGLIED_GEAENDERT an die
    /// Admin-Sitzungen, Mitgliedschaften und Praesenz-Delta für das Konto.
    /// </summary>
    private Antwort AbzeichenSetzen(Verbindung v, I.Akteur akteur, S.AbzeichenAnfrage a)
    {
        var e = Mitglieder.AbzeichenSetzen(Konten.Stand, akteur, a.KontoId, a.Raenge, a.Taetigkeiten);
        if (e.Fehler == I.Fehlercode.Ungueltig && e.Grund is { } grund) return Antwort.Fehl(S.Fehlercode.Ungueltig, grund);
        if (!e.Ok) return ModulFehler(v, e.Fehler);
        var d = Mitglieder.Stand.DatenVon(a.KontoId);
        Vermerken("ABZEICHEN", v.KontoId, a.KontoId, $"Abzeichen von {KontoText(a.KontoId)} durch {KontoText(v.KontoId)}: {I.Mitgliederbuch.AbzeichenText(d)}");
        MitgliedGeaendert(e.BetroffeneKonten);
        return Antwort.Mit(new S.AbzeichenAntwort { KontoId = a.KontoId, Raenge = [.. d.Raenge], Taetigkeiten = [.. d.Taetigkeiten] });
    }

    private static Antwort NamensFehler(I.Namensergebnis e) => e.Fehler == I.Fehlercode.NameVergeben
        ? Antwort.Fehl(S.Fehlercode.NameVergeben, $"Den {(e.DiscordVergeben ? "Discord-Namen" : "Namen")} {e.Vergeben} hat schon jemand",
            Antwort.DetailsBauen(w => w.WriteString("name", e.Vergeben)))
        : Antwort.Fehl(Abbildung.Fehler(e.Fehler));

    private Antwort JournalLesen(I.Akteur akteur, S.JournalAnfrage j)
    {
        var f = I.Kontenbuch.RechtPruefen(Konten.Stand, akteur, I.Rechte.KontenVerwalten);
        if (f != I.Fehlercode.Keiner) return Antwort.Fehl(Abbildung.Fehler(f));
        var liste = Journal.Lesen(j.Vor, j.KontoId, S.Grenzen.JournalMax);
        return Antwort.Mit(new S.JournalAntwort
        {
            Eintraege = [.. liste.Select(e => new S.JournalEintrag { Zeit = e.Zeit, Art = e.Art, Durch = e.Durch, KontoId = e.KontoId, Text = e.Text })],
        });
    }

    private Antwort Rollenkatalog(Verbindung v, I.Akteur akteur, S.RollenkatalogAnfrage r)
    {
        if (r.Op == S.RegelOperation.Setzen)
        {
            var eintraege = r.Eintraege!.Select(e => new I.Katalogrolle(e.Id, e.Name)
            {
                Gruppe = e.Gruppe,
                Rollen = e.Rollen is null ? [] : [.. e.Rollen],
                RaidHelper = e.RaidHelper is null ? [] : [.. e.RaidHelper.Select(k => new I.RaidHelperKlasse(k.ClassName, k.SpecName))],
            }).ToList();
            // Führung und Einsatzleitung dürfen ergänzen (Antwort 4); was sie nicht dürfen, sagt das Mitgliederbuch.
            var z = Schreiber.Aktuell;
            var fuehrung = HatRecht(z, v, L.Recht.GlobalSenden) || HatRecht(z, v, L.Recht.Zuweisen);
            var e2 = Mitglieder.KatalogSetzen(Konten.Stand, akteur, eintraege, r.Version, fuehrung);
            if (!e2.Ok) return ModulFehler(v, e2.Fehler);
            Vermerken("ROLLENKATALOG", v.KontoId, null, $"Rollenkatalog gesetzt von {KontoText(v.KontoId)}: {eintraege.Count} Rollen");
            MitgliedGeaendert(e2.BetroffeneKonten);
        }
        var m = Mitglieder.Stand;
        return Antwort.Mit(new S.RollenkatalogAntwort
        {
            Eintraege = [.. m.Katalog.Values.Select(k => new S.Rollenkatalogeintrag
            {
                Id = k.Id,
                Name = k.Name,
                Gruppe = k.Gruppe,
                Rollen = k.Rollen.IsEmpty ? null : [.. k.Rollen],
                RaidHelper = k.RaidHelper.IsEmpty ? null : [.. k.RaidHelper.Select(x => new S.RaidHelperKlasse { ClassName = x.ClassName, SpecName = x.SpecName })],
            })],
            Version = m.KatalogVersion,
        });
    }

    private Antwort Mitglied(Verbindung v, I.Akteur akteur, S.MitgliedAnfrage a)
    {
        var konten = Konten.Stand;
        // MitgliedAnfrage.untergilde wird seit 0.11.8 übergangen (Untergilden entfallen).
        switch (a.Op)
        {
            case S.MitgliedOperation.Liste:
            {
                var f = I.Kontenbuch.RechtPruefen(konten, akteur, I.Rechte.KontenVerwalten);
                if (f != I.Fehlercode.Keiner) return Antwort.Fehl(Abbildung.Fehler(f));
                // Seitenweise, damit die Antwort sicher unter der Rahmengrenze bleibt (4.10).
                var alle = MitgliederListe();
                var ab = (int)Math.Min(a.Ab ?? 0, (uint)alle.Count);
                var seite = alle.Skip(ab).Take(S.Grenzen.MitgliederListeMax).ToList();
                var weiter = ab + seite.Count < alle.Count ? (uint?)(ab + seite.Count) : null;
                return Antwort.Mit(new S.MitgliedAntwort { Mitglieder = seite, Weiter = weiter });
            }
            case S.MitgliedOperation.Vormerken:
            {
                var e = Mitglieder.Vormerken(konten, akteur, a.ScName!, a.DiscordName, a.Rollen, a.Notiz);
                if (!e.Ok) return MitgliederFehler(v, e);
                Vermerken("VORMERKUNG", v.KontoId, null, $"Vorgemerkt von {KontoText(v.KontoId)}: {a.ScName}");
                MitgliedGeaendert([]);
                return Antwort.Mit(new S.MitgliedAntwort { Ids = [.. e.Ids] });
            }
            case S.MitgliedOperation.Aendern when a.KontoId is { } kontoId:
            {
                var hakenVorher = Mitglieder.Stand.DatenVon(kontoId).MeldungenSenden;
                var e = Mitglieder.KontoAendern(konten, akteur, kontoId, a.Rollen, a.Notiz, a.MeldungenSenden);
                if (!e.Ok) return MitgliederFehler(v, e);
                if (a.Rollen is not null || a.Notiz is not null || a.MeldungenSenden is null)
                {
                    var rollen = a.Rollen is null ? "" : ", Rollen: " + I.Mitgliederbuch.RollenText(Mitglieder.Stand, a.Rollen);
                    Vermerken("MITGLIED", v.KontoId, kontoId, $"Mitglied {KontoText(kontoId)} geändert von {KontoText(v.KontoId)}{rollen}");
                }
                // Meldesystem (0.12.0, M15): den Haken nur vermerken, wenn er sich ändert; er wirkt sofort auf das Senderecht.
                if (a.MeldungenSenden is { } haken && haken != hakenVorher)
                {
                    Vermerken("MELDUNGEN", v.KontoId, kontoId,
                        $"Meldungen senden für {KontoText(kontoId)} {(haken ? "erlaubt" : "weggenommen")} durch {KontoText(v.KontoId)}");
                    MelderechtGeaendert(kontoId);
                }
                MitgliedGeaendert(e.BetroffeneKonten);
                return Antwort.Mit(new S.MitgliedAntwort { Ids = [kontoId] });
            }
            case S.MitgliedOperation.Aendern when a.MeldungenSenden is not null:
            {
                // Erst das Recht wie bei jeder Änderung (ohne Admin-Sitzung ADMIN_SITZUNG_NOETIG), dann die Regel.
                var f = I.Kontenbuch.RechtPruefen(konten, akteur, I.Rechte.KontenVerwalten);
                return f != I.Fehlercode.Keiner
                    ? Antwort.Fehl(Abbildung.Fehler(f))
                    : Antwort.Fehl(S.Fehlercode.Ungueltig, "Meldungen senden gibt es nur für Konten, nicht für Vormerkungen");
            }
            case S.MitgliedOperation.Aendern:
            {
                var e = Mitglieder.VormerkungAendern(konten, akteur, a.VormerkungId!.Value, a.ScName, a.DiscordName, a.Rollen, a.Notiz);
                if (!e.Ok) return MitgliederFehler(v, e);
                Vermerken("VORMERKUNG", v.KontoId, null, $"Vormerkung {a.VormerkungId} geändert von {KontoText(v.KontoId)}");
                MitgliedGeaendert([]);
                return Antwort.Mit(new S.MitgliedAntwort { Ids = [.. e.Ids] });
            }
            case S.MitgliedOperation.Loeschen:
            {
                var e = Mitglieder.VormerkungLoeschen(konten, akteur, a.VormerkungId!.Value);
                if (!e.Ok) return MitgliederFehler(v, e);
                Vermerken("VORMERKUNG", v.KontoId, null, $"Vormerkung {a.VormerkungId} gelöscht von {KontoText(v.KontoId)}");
                MitgliedGeaendert([]);
                return Antwort.Mit(new S.MitgliedAntwort { Ids = [.. e.Ids] });
            }
            default:
            {
                var e = Mitglieder.Einfuegen(konten, akteur, a.Zeilen!);
                if (e.Fehler == I.Fehlercode.Ungueltig && e.Zeilen.Count > 0)
                {
                    // Nichts angelegt; jede Zeile, die nicht passt, mit Klartext (zu groß für Fehler.details).
                    return Antwort.Mit(new S.MitgliedAntwort
                    {
                        Ids = [],
                        Zeilen = [.. e.Zeilen.Select(z => new S.Zeilenfehler { Zeile = (ushort)z.Zeile, Grund = z.Grund })],
                    });
                }
                if (!e.Ok) return MitgliederFehler(v, e);
                Vermerken("VORMERKUNG", v.KontoId, null, $"Liste eingefügt von {KontoText(v.KontoId)}: {e.Ids.Count} Vormerkungen");
                MitgliedGeaendert([]);
                return Antwort.Mit(new S.MitgliedAntwort { Ids = [.. e.Ids], Hinweis = e.Hinweis });
            }
        }
    }

    private Antwort MitgliederFehler(Verbindung v, I.Mitgliederergebnis e) => e.Fehler == I.Fehlercode.NameVergeben
        ? Antwort.Fehl(S.Fehlercode.NameVergeben, $"Den {(e.DiscordVergeben ? "Discord-Namen" : "Namen")} {e.Vergeben} hat schon jemand",
            Antwort.DetailsBauen(w => w.WriteString("name", e.Vergeben)))
        : ModulFehler(v, e.Fehler);

    /// <summary>Alle Konten (auch Anträge) und Vormerkungen für die Verwaltung, höchstens 1000.</summary>
    private List<S.MitgliedEintrag> MitgliederListe()
    {
        var konten = Konten.Stand;
        var m = Mitglieder.Stand;
        var z = Schreiber.Aktuell;
        var liste = new List<S.MitgliedEintrag>();
        foreach (var k in konten.Konten.Values.OrderBy(k => k.KontoId))
        {
            var d = m.DatenVon(k.KontoId);
            var geraete = konten.GeraeteVon(k.KontoId);
            var zustand = k.IstAntrag ? S.MitgliedZustand.Antrag
                : k.Status == I.KontoStatus.Gesperrt ? S.MitgliedZustand.Gesperrt
                : z.IstOnline(k.KontoId) ? S.MitgliedZustand.Online
                : S.MitgliedZustand.Freigegeben;
            DateTimeOffset? zuletzt = null;
            foreach (var g in geraete)
                if (g.Zuletzt is { } t && (zuletzt is null || t > zuletzt)) zuletzt = t;
            liste.Add(new S.MitgliedEintrag
            {
                KontoId = k.KontoId,
                Name = k.Name,
                ScNamen = k.IstAntrag ? [k.Antrag!.ScName] : [.. k.ScNamen],
                DiscordName = k.DiscordName,
                Benutzername = k.Benutzername.Length > 0 ? k.Benutzername : null,
                Rollen = [.. d.Rollen],
                Lieblingsrollen = d.Lieblingsrollen.IsEmpty ? null : [.. d.Lieblingsrollen],
                ErsteWahl = d.ErsteWahl,
                Stufe = Abbildung.Stufe(k.Stufe),
                Zustand = zustand,
                Zuletzt = zuletzt?.ToUnixTimeMilliseconds(),
                Notiz = d.Notiz,
                Eigentuemer = k.Eigentuemer ? true : null,
                Admin = AdminStatusVon(k.KontoId),
                // Abzeichen (0.11.7): alles, was das Konto besitzt, auch Ausgeblendetes; sonst kann der Admin nicht vergeben.
                Raenge = d.Raenge.IsEmpty ? null : [.. d.Raenge],
                Taetigkeiten = d.Taetigkeiten.IsEmpty ? null : [.. d.Taetigkeiten],
                // Meldesystem (0.12.0): der Haken, auch wenn er bei einem Funker gerade nicht wirkt.
                MeldungenSenden = d.MeldungenSenden ? true : null,
            });
        }
        foreach (var vm in m.Vormerkungen.Values)
        {
            liste.Add(new S.MitgliedEintrag
            {
                VormerkungId = vm.Id,
                Name = vm.ScName,
                ScNamen = [vm.ScName],
                DiscordName = vm.DiscordName,
                Rollen = [.. vm.Rollen],
                Zustand = S.MitgliedZustand.Vorgemerkt,
                Notiz = vm.Notiz,
            });
        }
        return liste;
    }

    /// <summary>Mitglieder geändert: Meldung an die Admin-Sitzungen, betroffene Konten bekommen ihre Angaben neu.</summary>
    private void MitgliedGeaendert(IReadOnlyList<uint> betroffen)
    {
        MeldungSenden(S.MeldungsArten.MitgliedGeaendert, L.Recht.KontenVerwalten, null);
        foreach (var k in betroffen) MitgliedschaftenSenden(k);
        if (betroffen.Count > 0) PraesenzDelta([.. betroffen]);
    }

    private Antwort Lieblingsrollen(Verbindung v, I.Akteur akteur, S.LieblingsrollenAnfrage l)
    {
        var e = Mitglieder.LieblingsrollenSetzen(Konten.Stand, akteur, l.Rollen, l.ErsteWahl);
        if (!e.Ok) return Antwort.Fehl(Abbildung.Fehler(e.Fehler));
        MitgliedschaftenSenden(v.KontoId);
        var d = Mitglieder.Stand.DatenVon(v.KontoId);
        return Antwort.Mit(new S.LieblingsrollenAntwort { Rollen = [.. d.Lieblingsrollen], ErsteWahl = d.ErsteWahl });
    }

    /// <summary>
    /// Untergilde (0x001D): seit 0.11.8 entfallen (Sarevoc: "Untergilden entfallen ganz, überall"). Der Typ bleibt als Hülle im
    /// Katalog, damit ein neuer Server ältere Clients nicht trennt, die LISTE beim Öffnen rufen: LISTE liefert eine leere
    /// Liste, SETZEN und LOESCHEN antworten mit UNGUELTIG und einem Hinweis. Gespeicherte Untergilden bleiben unberührt.
    /// </summary>
    private static Antwort Untergilde(S.UntergildeAnfrage u) => u.Op == S.UntergildeOperation.Liste
        ? Antwort.Mit(new S.UntergildeAntwort { Untergilden = [] })
        : Antwort.Fehl(S.Fehlercode.Ungueltig, UntergildenEntfallen);

    /// <summary>Klartext für <c>Untergilde SETZEN</c> und <c>LOESCHEN</c> seit 0.11.8.</summary>
    internal const string UntergildenEntfallen = "Untergilden gibt es seit 0.11.8 nicht mehr";

    /// <summary>
    /// Mitgliedskarte (A48, N10, Sarevoc am 30.09.2026, Antwort 3). Den Namen samt Discord-Name ("Mustermann/Max") und Hauptname sieht
    /// jeder, dazu Platz und online. Zeichen, Abzeichen, Rollen und Wünsche sieht jeder, solange der Inhaber seine Angaben
    /// freigibt (<c>ProfilSetzen.karteFreigeben</c>, ab Werk an); Rollen und Wünsche zusätzlich nur mit
    /// <see cref="KartenKonfiguration.RollenFuerAlle"/>. Weitere SC-Namen, Verbindung, Güte und letzten Platz sehen nur die
    /// Einsatzleitung (ZUWEISEN), ADMIN-Konten und das Konto selbst; sie sehen auch alles andere. Andere bekommen nur Karten
    /// von Konten, die in der Praesenz stehen (online oder mit Platz), sonst <c>UNBEKANNT</c>.
    /// </summary>
    private Antwort Mitgliedskarte(Verbindung v, S.MitgliedskarteAnfrage a)
    {
        var konten = Konten.Stand;
        if (!konten.Konten.TryGetValue(a.KontoId, out var k) || k.IstAntrag) return Antwort.Fehl(S.Fehlercode.Unbekannt);
        var z = Schreiber.Aktuell;
        var selbst = a.KontoId == v.KontoId;
        var anfragender = konten.Konten.TryGetValue(v.KontoId, out var ak) ? ak : null;
        var voll = selbst || HatRecht(z, v, L.Recht.Zuweisen) || anfragender?.Stufe == I.Stufe.Admin;
        var jetzt = Zeit.LageMs();
        z.Konten.TryGetValue(a.KontoId, out var lk);
        if (!voll && (lk is null || !Abbildung.InPraesenz(z, lk, jetzt))) return Antwort.Fehl(S.Fehlercode.Unbekannt);
        var m = Mitglieder.Stand;
        var d = m.DatenVon(a.KontoId);
        var freigegeben = voll || d.KarteFreigeben;
        var rollen = voll || (d.KarteFreigeben && Konfiguration.Mitgliedskarte.RollenFuerAlle);
        var platz = lk is null ? L.PlatzZustand.Frei : z.PlatzZustandVon(a.KontoId, jetzt);
        S.Verbindungsweg? weg = null;
        S.Guete? guete = null;
        if (voll && z.SitzungVonKonto(a.KontoId) is { } sitzung && Relais is { } r && r.Anzeige(sitzung, out var w, out var g))
        {
            weg = w;
            guete = g;
        }
        // Zeichen und Abzeichen (0.11.7): Andere sehen nur, was der Inhaber nicht ausgeblendet hat; das Konto selbst sieht alles Eigene.
        var zeigen = freigegeben && (selbst || d.ZeichenZeigen);
        var zeichen = !zeigen ? [] : selbst ? ZeichenVon(k) : SichtbareZeichen(k, d);
        var abzeichen = !zeigen ? [] : selbst ? S.Abzeichenkatalog.Ordnen(d.AlleAbzeichen().Where(S.Abzeichenkatalog.Kennt)) : I.Mitgliederbuch.SichtbareAbzeichen(d);
        return Antwort.Mit(new S.MitgliedskarteAntwort
        {
            KontoId = k.KontoId,
            Name = k.Name,
            ScName = k.Hauptname,
            ScNamen = voll ? [.. k.ScNamen] : null,
            DiscordName = k.DiscordName,
            Rollen = rollen ? [.. d.Rollen] : null,
            Lieblingsrollen = rollen ? [.. d.Lieblingsrollen] : null,
            ErsteWahl = rollen ? d.ErsteWahl : null,
            Online = z.IstOnline(k.KontoId),
            SchiffId = platz != L.PlatzZustand.Frei && lk?.Platz is { SchiffId: not 0 } p ? p.SchiffId : null,
            RolleId = platz != L.PlatzZustand.Frei ? lk?.Platz?.RolleId : null,
            Platz = platz switch
            {
                L.PlatzZustand.Belegt => S.PlatzStatus.Belegt,
                L.PlatzZustand.Reserviert => S.PlatzStatus.Reserviert,
                _ => null,
            },
            LetzterPlatz = voll && lk?.LetzterStand?.Platz is { } lp
                ? new S.LetzterPlatz { SchiffId = lp.SchiffId == 0 ? null : lp.SchiffId, RolleId = lp.RolleId }
                : null,
            Zeichen = zeichen,
            Verbindung = weg,
            Guete = guete,
            Freigegeben = freigegeben,
            Abzeichen = abzeichen.IsEmpty ? null : [.. abzeichen],
        });
    }

    private Antwort AntraegeAblehnen(Verbindung v, I.Akteur akteur)
    {
        var e = Konten.AntraegeAblehnen(akteur, k => k.Antrag is { } a && Mitglieder.Vorschlaege(a.ScName, a.DiscordName).Count > 0);
        if (!e.Ok) return ModulFehler(v, e.Fehler);
        WirkungenAusfuehren(e.Wirkungen);
        Vermerken("ANTRAEGE_ABGELEHNT", v.KontoId, null,
            $"{e.Geraete.Count.ToString(CultureInfo.InvariantCulture)} Anträge ohne passende Vormerkung abgelehnt und gelöscht von {KontoText(v.KontoId)}");
        return Antwort.Mit(new S.AntraegeAblehnenAntwort { Anzahl = (ushort)Math.Min(e.Geraete.Count, ushort.MaxValue) });
    }

    /// <summary>Konsole: Mitglieder als Text und JSON (nur lesend; Pflege gehört in den Verwaltungsteil, A43).</summary>
    private VerwaltungsAntwort MitgliederKonsole()
    {
        var liste = MitgliederListe();
        var sb = new System.Text.StringBuilder();
        foreach (var e in liste)
        {
            var id = e.KontoId is { } k ? "Konto " + k.ToString(CultureInfo.InvariantCulture) : "Vormerkung " + e.VormerkungId?.ToString(CultureInfo.InvariantCulture);
            sb.Append(CultureInfo.InvariantCulture, $"{id,-16} {e.Zustand,-11} {string.Join("/", e.ScNamen),-24} {e.DiscordName}\n");
        }
        var json = System.Text.Json.JsonSerializer.SerializeToNode(new S.MitgliedAntwort { Mitglieder = liste }, S.SteuerJson.Optionen);
        return VerwaltungsAntwort.Gut(sb.ToString().TrimEnd(), json);
    }
}
