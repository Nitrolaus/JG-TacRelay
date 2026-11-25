// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json.Nodes;
using Funk.Server.Betrieb;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Einstufung nach Hello samt Welcome für WARTET (4.5, 4.6) und der Mindestfassung, die gerade gilt (<see cref="Zentrale.MindestVersion"/>).
/// </summary>
public sealed record HelloBescheid(I.EinstufungsArt Art, S.Welcome? Welcome, string Mindest);

/// <summary>Alles, was eine Verbindung beim Eintritt in AKTIV_SYNC sendet (4.6), oder der Grund, warum es nicht geht.</summary>
public sealed record SitzungsStart(
    S.Fehlercode? Fehler,
    uint SitzungsId,
    S.Welcome? Welcome,
    S.NetzListe? NetzListe,
    S.Mitgliedschaften? Mitgliedschaften,
    S.Praesenz? Praesenz,
    S.PegelVoreinstellungen? Pegel,
    long ZustandsVersion);

public sealed partial class Zentrale
{
    /// <summary>Befehl, den Welcome für die Registrierung nennt (3.4 Schritt 2).</summary>
    public const string Registrierungsbefehl = "/funk verknuepfen";

    /// <summary>Öffentlicher UDP-Port für Welcome (Abschnitt 0); der Dienst setzt ihn nach dem Binden.</summary>
    public ushort UdpPortFuerWelcome { get; set; }

    /// <summary>Neue TLS-Verbindung (nach Handshake und Einstufung "bekannt"/"unbekannt").</summary>
    internal void VerbindungAnmelden(Verbindung v) => Einreihen(() => _verbindungen[v.Id] = v);

    /// <summary>
    /// Hello (4.6 WARTE_HELLO): Einstufung über den Fingerabdruck am aktuellen Kontenbuch. Bei WARTET enthält die
    /// Antwort das Welcome mit Konto und Gerät, bei UNBEKANNT und FREIGEGEBEN baut die Verbindung ihr Welcome selbst.
    /// </summary>
    internal Task<HelloBescheid> HelloAsync(Verbindung v) => Ausfuehren(() =>
    {
        var e = Konten.Einstufen(v.Geraet);
        WirkungenAusfuehren(e.Wirkungen);
        S.Welcome? welcome = null;
        switch (e.Art)
        {
            case I.EinstufungsArt.Wartet:
            {
                v.KontoId = e.KontoId;
                v.GeraetId = e.GeraetId;
                if (_wartende.TryGetValue(e.GeraetId, out var alt) && !ReferenceEquals(alt, v)) alt.Beenden(null, S.ByeGrund.Ersetzt);
                _wartende[e.GeraetId] = v;
                var stand = Konten.Stand;
                var k = stand.Konten[e.KontoId];
                var g = stand.Geraete[e.GeraetId];
                welcome = new S.Welcome
                {
                    Minor = v.Minor,
                    Status = S.WelcomeStatus.Wartet,
                    Konto = new S.KontoAngabe
                    {
                        Id = k.KontoId,
                        Name = k.Name,
                        Benutzername = Abbildung.Benutzername(k),
                        ScNamen = k.Antrag is { } a ? [a.ScName] : k.ScNamen.IsEmpty ? null : [.. k.ScNamen],
                        DiscordName = k.DiscordName,
                    },
                    Geraet = new S.GeraetAngabe { Id = g.GeraetId, Name = g.Name, Anzeige = g.Anzeige },
                };
                break;
            }
            case I.EinstufungsArt.Freigegeben:
            case I.EinstufungsArt.KontoGesperrt:
            case I.EinstufungsArt.GeraetGesperrt:
                v.KontoId = e.KontoId;
                v.GeraetId = e.GeraetId;
                break;
        }
        return new HelloBescheid(e.Art, welcome, MindestVersion());
    });

    /// <summary>
    /// Die Mindestfassung für Hello (4.7): aus server.json, und sobald es ein Kanalnetz gibt, wenigstens 0.6.0 (Versionstor, 6.16). Nur
    /// in der Arbeitsschleife: Hello und der Commit des ersten Kanalnetzes laufen beide dort, also kommt keine Verbindung unter 0.6.0
    /// zwischen beiden durch (<see cref="VersionstorDurchsetzen"/>).
    /// </summary>
    internal string MindestVersion() =>
        Schreiber.Aktuell.KanalnetzeAktiv && S.Versionen.ClientZuAlt(Konfiguration.MindestClientVersion, S.Kanalrechnung.MindestFassung)
            ? S.Kanalrechnung.MindestFassung
            : Konfiguration.MindestClientVersion;

    /// <summary>
    /// Versionstor (4.7, 6.16): Gibt es ein Kanalnetz, trennt der Server jede Verbindung, die mit einer Fassung unter der Mindestfassung
    /// Hello geschickt hat, mit <c>VERSION_ZU_ALT</c>, auch in der Registrierung und im Warteraum. Läuft nach jeder Änderung der NetzListe.
    /// </summary>
    private void VersionstorDurchsetzen()
    {
        if (!Schreiber.Aktuell.KanalnetzeAktiv) return;
        var mindest = MindestVersion();
        foreach (var v in AlleVerbindungen())
        {
            if (v.ClientVersion is not { } fassung || !S.Versionen.ClientZuAlt(fassung, mindest)) continue;
            Log.Info("Versionstor: Fassung " + fassung + " unter " + mindest + ", getrennt: " + Log.Schwaerzung.Konto(v.KontoId));
            v.VersionZuAlt(mindest);
        }
    }

    /// <summary>
    /// Eintritt in AKTIV_SYNC (4.6): Sitzung im Schnappschuss aktivieren (Verdrängung, Wiederherstellung, Nachziehen,
    /// VERDRAENGEN_GESPERRT vor jedem Sitzungsdatum), ältere Sitzung desselben Kontos beenden, Stand für die Sync-Folge
    /// bauen. Ab hier puffert die Verbindung fremde Pushes, bis die Sync-Folge eingereiht ist.
    /// </summary>
    internal Task<SitzungsStart> SitzungStartenAsync(Verbindung v) => Ausfuehren<SitzungsStart>(tcs =>
    {
        var stand = Konten.Stand;
        if (!stand.Konten.TryGetValue(v.KontoId, out var ik) || ik.Status != I.KontoStatus.Aktiv)
        {
            tcs.TrySetResult(Fehlstart(S.Fehlercode.KontoGesperrt));
            return;
        }
        if (_sitzungen.Count >= Konfiguration.Grenzen.MaxSitzungen)
        {
            tcs.TrySetResult(Fehlstart(S.Fehlercode.ServerVoll));
            return;
        }
        // Seit 0.8.0: Kommt der Start im selben Durchlauf wie die Freigabe (wartende Verbindung, 3.5), steht das neue Konto noch nicht
        // im Schnappschuss; das Kontenbuch wird sonst erst nach dem Commit gespiegelt (KontenSichern). SitzungAktivieren fand es dann
        // nicht, und der Server trennte mit UNBEKANNT (Wackler der Reparaturwege in HaertungTests unter Last, 01.10. und 04.10.2026).
        // Erst spiegeln: Der Zustandsschreiber arbeitet die Aufträge in ihrer Folge ab, die Übernahme des Kontos also zuerst.
        if (!Schreiber.Aktuell.Konten.ContainsKey(v.KontoId)) SpiegelAbgleichen();
        var id = NeueSitzungsId();
        Schreiben(L.Auftrag.SitzungAktivieren(v.KontoId, id, v.GeraetId), e =>
        {
            if (!e.Erfolg)
            {
                _benutzteIds.Remove(id);
                tcs.TrySetResult(Fehlstart(Abbildung.Fehler(e.Fehler)));
                return;
            }
            if (_wartende.TryGetValue(v.GeraetId, out var w) && ReferenceEquals(w, v)) _wartende.Remove(v.GeraetId);
            v.SitzungsId = id;
            v.SyncBeginnt();
            _sitzungen[id] = v;
            Konten.SitzungBegonnen(v.GeraetId);

            var aktuell = Konten.Stand;
            aktuell.Geraete.TryGetValue(v.GeraetId, out var geraet);
            foreach (var b in e.BeendeteSitzungen)
            {
                if (!_sitzungen.TryGetValue(b.SitzungsId, out var alt) || ReferenceEquals(alt, v)) continue;
                if (b.Grund == L.Sitzungsende.Ersetzt) alt.Beenden(null, S.ByeGrund.Ersetzt);
                else if (b.Grund == L.Sitzungsende.Verdraengt)
                    alt.Beenden(null, S.ByeGrund.Verdraengt, new S.ByeDurch { GeraetName = geraet?.Name ?? "?", Anzeige = geraet?.Anzeige ?? v.Geraet.Anzeige });
            }

            var z = Schreiber.Aktuell;
            var konto = aktuell.Konten[v.KontoId];
            var jetzt = Zeit.LageMs();
            if (Profil is not { } profil)
            {
                tcs.TrySetResult(Fehlstart(S.Fehlercode.Intern));
                return;
            }
            var welcome = new S.Welcome
            {
                Minor = v.Minor,
                Status = S.WelcomeStatus.Freigegeben,
                SitzungsId = id,
                Konto = Abbildung.KontoAngabe(konto, mitRechten: true) with
                {
                    Admin = AdminStatusVon(konto.KontoId),
                    AdminAntrag = Admins.Eintrag(konto.KontoId) is { AntragErlaubt: true } ? true : null,
                    Eigentuemer = konto.Eigentuemer ? true : null,
                    Beauftragung = Abbildung.BeauftragungAngabe(z.Konten.GetValueOrDefault(konto.KontoId), jetzt),
                    AlsFunkerBis = AlsFunkerBisVon(konto.KontoId),
                },
                Server = new S.ServerAngabe { Version = Version },
                Udp = new S.UdpAngabe { Port = UdpPortFuerWelcome, MaxDatagramm = (ushort)Protokoll.Sprache.Sprachkanal.MaxDatagramm },
                ServerZeit = Zeit.UnixMs(),
                MindestClientVersion = MindestVersion(),
                Profil = profil,
                ZustandsVersion = z.ZustandsVersion,
                Grenzen = S.WelcomeGrenzen.Standard,
            };
            var (mit, ohne) = Praesenz(z, null, voll: true);
            var start = new SitzungsStart(null, id, welcome, Abbildung.NetzListe(z),
                MitgliedschaftenBauen(z, z.Konten[v.KontoId]),
                HatRecht(z, v, L.Recht.Zuweisen) ? mit : ohne,
                Komfort.PegelVon(v.KontoId), z.ZustandsVersion);
            Log.Info("Sitzung beginnt: " + Log.Schwaerzung.Konto(v.KontoId) + " von " + Log.Schwaerzung.Adresse(v.Quelle));
            // Die laufende Phase nach Welcome (6.14), sobald die Flotte Phasen hat: Die Verbindung hält Pushes bis nach der
            // Sync-Folge zurück (SyncBeginnt), also kommt er hinter Zustand. Clients 0.4.x übergehen den optionalen Typ.
            if (!z.Phasen.IsEmpty || Schreiber.Neben.Phase is { Nr: > 1 })
                v.Push(S.Nachrichtentyp.PhaseGeschaltet, Abbildung.PhaseGeschaltet(z, Schreiber.Neben.Phase));
            // Meldesystem (0.12.0, M20): laufender Zustand, Ampel, Countdown und offene Bereit-Abfrage, ebenso optional und zurückgehalten.
            MeldungenNachWelcome(v);
            tcs.TrySetResult(start);
        });
    });

    private static SitzungsStart Fehlstart(S.Fehlercode code) => new(code, 0, null, null, null, null, null, 0);

    /// <summary>Die Verbindung hat AKTIV erreicht (Antwort auf VoiceKeys START): ihr Präfix kommt in die Vorrangspur (4.8).</summary>
    internal void AktivErreicht(Verbindung v) => VersucheEinreihen(() =>
    {
        Spur.Eintragen(v.Quelle.Praefix, Zeit.UnixMs());
        Log.Info("Sitzung aktiv: " + Log.Schwaerzung.Konto(v.KontoId));
    });

    /// <summary>Registrierung (3.4 Schritt 2): Code an Verbindung und Fingerabdruck gebunden, 5 min gültig.</summary>
    internal Task<(S.RegistrierungAntwort? Antwort, S.Fehlercode? Fehler)> RegistrierenAsync(Verbindung v, string geraetename) => Ausfuehren(() =>
    {
        if (Zulassung.Ueberlast) return ((S.RegistrierungAntwort?)null, (S.Fehlercode?)S.Fehlercode.ServerVoll);
        var a = Codes.Ausstellen(v.Id, v.Geraet, geraetename, v.SchluesselArt, PraefixSchluessel(v.Quelle));
        WirkungenAusfuehren(a.Wirkungen);
        // Nur Grenzen der Quelle selbst zählen (A51); eine volle Zählertabelle trifft jedes neue Präfix, auch ein neues Mitglied.
        if (I.EinmalcodeRegister.GrenzeDerQuelle(a)) Sperrliste.Vermerken(v.Quelle, Netzzugang.Sperranlass.Registrierungsflut, Zeit.UnixMs());
        if (a.Fehler != I.Fehlercode.Keiner || a.Code is null) return (null, Abbildung.Fehler(a.Fehler));
        return (new S.RegistrierungAntwort { Code = a.Code, GueltigBis = a.GueltigBis.ToUnixTimeMilliseconds(), Befehl = Registrierungsbefehl }, null);
    });

    /// <summary>
    /// Registrierung mit SC-Name und Discord-Name (3.4, P3): Antrag ohne Code, Konto und Gerät in WARTET. Es gelten dieselben
    /// Grenzen wie für Codes (je Verbindung, je Präfix und Stunde) und höchstens 30 offene Anträge. Die Verbindung bekommt
    /// ihre Antwort und danach <c>GeraetStatus{WARTET}</c> (mit vergebenen Namen); den Admin-Sitzungen geht der Antrag mit
    /// passenden Vormerkungen zu.
    /// </summary>
    internal Task<(S.RegistrierungAntwort? Antwort, S.Fehlercode? Fehler, S.GeraetStatus? Wartet)> AntragStellenAsync(Verbindung v, string geraetename,
        string scName, string discordName) => Ausfuehren(() =>
    {
        if (Zulassung.Ueberlast) return ((S.RegistrierungAntwort?)null, (S.Fehlercode?)S.Fehlercode.ServerVoll, (S.GeraetStatus?)null);
        var zulassen = Codes.AntragZulassen(v.Id, PraefixSchluessel(v.Quelle));
        WirkungenAusfuehren(zulassen.Wirkungen);
        if (!zulassen.Ok)
        {
            if (I.EinmalcodeRegister.GrenzeDerQuelle(zulassen)) Sperrliste.Vermerken(v.Quelle, Netzzugang.Sperranlass.Antragsflut, Zeit.UnixMs());
            return (null, S.Fehlercode.Grenze, null);
        }
        var e = Konten.AntragStellen(v.Geraet, geraetename, v.SchluesselArt, scName, discordName);
        if (!e.Ok)
        {
            WirkungenAusfuehren(e.Wirkungen);
            return (null, e.Fehler switch
            {
                I.Fehlercode.AntraegeVoll => S.Fehlercode.AntraegeVoll,
                I.Fehlercode.Grenze => S.Fehlercode.Grenze,
                _ => S.Fehlercode.Ungueltig,
            }, null);
        }
        // Erst die Admins (Wirkungen), dann die eigene Verbindung: Sie bekommt GeraetStatus erst nach ihrer Antwort.
        WirkungenAusfuehren(e.Wirkungen);
        v.KontoId = e.KontoId;
        v.GeraetId = e.GeraetId;
        _wartende[e.GeraetId] = v;
        var stand = Konten.Stand;
        Vermerken("ANTRAG", e.KontoId, e.KontoId, $"Antrag von {scName} (Discord: {discordName}), Gerät {GeraetText(e.GeraetId)}");
        Log.Info("Antrag gestellt, wartet auf Freigabe: " + Log.Schwaerzung.Konto(e.KontoId));
        return ((S.RegistrierungAntwort?)new S.RegistrierungAntwort { Antrag = true }, (S.Fehlercode?)null,
            (S.GeraetStatus?)GeraetStatusBauen(stand, stand.Geraete[e.GeraetId], I.GeraetStatusMeldung.Wartet, null, null, mitVorschlaegen: false));
    });

    /// <summary>Präfix als Schlüssel für das Einmalcode-Register; bleibt im Speicher.</summary>
    private static string PraefixSchluessel(Adresse a)
    {
        var p = a.Praefix;
        return p.Wert.ToString("x32", CultureInfo.InvariantCulture) + (p.V6 ? "/64" : "/32");
    }

    /// <summary>
    /// Admin-Werkzeug <c>verknuepfen --code --discord [--stufe]</c> (3.5 Bootstrap): Code einlösen, Rückfrage an die
    /// Verbindung, auf die Antwort warten (höchstens 120 s). Kein Weg führt vom Code direkt zu FREIGEGEBEN.
    /// </summary>
    internal Task<VerwaltungsAntwort> VerknuepfenAsync(string code, string discordId, string benutzername, string? name, I.Stufe stufe) =>
        Ausfuehren<VerwaltungsAntwort>(tcs =>
        {
            var e = Codes.EinloesenUeberAdminWerkzeug(code);
            WirkungenAusfuehren(e.Wirkungen);
            if (e.Fehler != I.Fehlercode.Keiner || e.Bindung is null)
            {
                tcs.TrySetResult(VerwaltungsAntwort.Fehler(I.Drahtnamen.Drahtname(e.Fehler)));
                return;
            }
            if (!_verbindungen.TryGetValue(e.Bindung.Verbindung, out var v) || v.Endet)
            {
                tcs.TrySetResult(VerwaltungsAntwort.Fehler("CODE_UNGUELTIG"));
                return;
            }
            var anfrage = Konten.VerknuepfungAnfragen(e.Bindung, discordId, benutzername, name, I.Herkunft.AdminWerkzeug, stufe);
            if (anfrage.Fehler != I.Fehlercode.Keiner)
            {
                // 3.4 Schritt 4: dieselbe Meldung als Fehler-Push an die Verbindung, ohne Trennung.
                if (anfrage.Fehler is I.Fehlercode.NurAdminWerkzeug or I.Fehlercode.KontoGesperrt or I.Fehlercode.GeraeteLimit)
                    v.Push(S.Nachrichtentyp.Fehler, new S.Fehler { Code = Abbildung.Fehler(anfrage.Fehler) });
                tcs.TrySetResult(VerwaltungsAntwort.Fehler(I.Drahtnamen.Drahtname(anfrage.Fehler)));
                return;
            }
            _adminVerknuepfungen[v.Id] = tcs;
            v.VerknuepfungFragen(new S.VerknuepfungKonto { Name = anfrage.KontoName!, Benutzername = anfrage.Benutzername!, Neu = anfrage.Neu });
        });

    /// <summary>
    /// Antwort des Clients auf VerknuepfungBestaetigen (3.4 Schritt 5 und 6); Fristablauf gilt als nein. Bei ja entsteht
    /// das Gerät in WARTET, die Verbindung bekommt GeraetStatus{WARTET} und geht in den WARTERAUM.
    /// </summary>
    internal Task<bool> VerknuepfungBeantwortetAsync(Verbindung v, bool ja) => Ausfuehren(() =>
    {
        var r = Konten.VerknuepfungBeantworten(v.Id, ja);
        _adminVerknuepfungen.Remove(v.Id, out var tcs);
        if (r.Fehler != I.Fehlercode.Keiner)
        {
            tcs?.TrySetResult(VerwaltungsAntwort.Fehler(I.Drahtnamen.Drahtname(r.Fehler)));
            return false;
        }
        v.KontoId = r.KontoId;
        v.GeraetId = r.GeraetId;
        _wartende[r.GeraetId] = v;
        WirkungenAusfuehren(r.Wirkungen);
        var daten = new JsonObject { ["geraet"] = new JsonObject { ["name"] = r.GeraetName, ["anzeige"] = r.Anzeige } };
        tcs?.TrySetResult(VerwaltungsAntwort.Gut("OK", daten));
        Log.Info("Gerät verknüpft, wartet auf Freigabe: " + Log.Schwaerzung.Konto(r.KontoId));
        return true;
    });

    /// <summary>Verbindung zu (4.6 ENDE): Codes und Rückfragen verfallen, Sitzung endet, Präfix in die Vorrangspur.</summary>
    internal void VerbindungGeschlossen(Verbindung v) => VersucheEinreihen(() =>
    {
        AdminVerbindungEndet(v);
        _verbindungen.Remove(v.Id);
        if (v.GeraetId != 0 && _wartende.TryGetValue(v.GeraetId, out var w) && ReferenceEquals(w, v)) _wartende.Remove(v.GeraetId);
        Codes.VerbindungBeendet(v.Id);
        Konten.VerbindungBeendet(v.Id);
        if (_adminVerknuepfungen.Remove(v.Id, out var tcs)) tcs.TrySetResult(VerwaltungsAntwort.Fehler("FRIST"));
        if (v.SitzungsId == 0 || !_sitzungen.TryGetValue(v.SitzungsId, out var s) || !ReferenceEquals(s, v)) return;
        _sitzungen.TryRemove(v.SitzungsId, out _);
        _benutzteIds[v.SitzungsId] = Zeit.UnixMs();
        Relais?.SitzungEntfernen(v.SitzungsId);
        Konten.SitzungBeendet(v.GeraetId);
        if (v.WarAktiv) Spur.Eintragen(v.Quelle.Praefix, Zeit.UnixMs());
        var konto = v.KontoId;
        Schreiben(L.Auftrag.SitzungBeenden(v.SitzungsId), _ =>
        {
            PraesenzDelta([konto]);
            MeldungenSitzungEndet();
        });
        Log.Info("Sitzung endet: " + Log.Schwaerzung.Konto(konto));
    });
}
