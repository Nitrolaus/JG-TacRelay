// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Funk.Server.Betrieb;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Befehle des Admin-Werkzeugs (3.8), serverintern über den UNIX-Socket: Konten, Verknüpfen per Code, Geräte
/// freigeben, ablehnen, entfernen und sperren, Netze, Schiffe und Rollen, Einstellungen, Vorrangspur, Messwerte, Profil.
/// Das Admin-Werkzeug ersetzt vorläufig den Bot (3.8); Codes erscheinen nie im Logbuch.
/// </summary>
public sealed partial class Zentrale
{
    /// <summary>Führt einen Verwaltungsbefehl aus. <paramref name="argumente"/> ist ein JSON-Objekt.</summary>
    public Task<VerwaltungsAntwort> VerwaltungAsync(string befehl, JsonObject argumente)
    {
        ArgumentNullException.ThrowIfNull(befehl);
        ArgumentNullException.ThrowIfNull(argumente);
        switch (befehl)
        {
            case "verknuepfen":
                if (Text(argumente, "code") is not { } code || Text(argumente, "discord") is not { } discord || Text(argumente, "benutzername") is not { } benutzer)
                    return Task.FromResult(VerwaltungsAntwort.Fehler("verknuepfen braucht code, discord und benutzername"));
                var stufe = StufeAusText(Text(argumente, "stufe") ?? "FUNKER");
                if (stufe is null) return Task.FromResult(VerwaltungsAntwort.Fehler("stufe: ADMIN, RAIDLEITER oder FUNKER"));
                return VerknuepfenAsync(code, discord, benutzer, Text(argumente, "name"), stufe.Value);
            case "messwerte":
                return MesswerteAsync();
        }
        return Ausfuehren<VerwaltungsAntwort>(tcs =>
        {
            var sofort = Verwaltung(befehl, argumente, tcs);
            if (sofort is not null) tcs.TrySetResult(sofort);
        });
    }

    private async Task<VerwaltungsAntwort> MesswerteAsync()
    {
        if (Relais is null) return VerwaltungsAntwort.Fehler("kein Relais");
        var m = await Relais.MesswerteAsync().ConfigureAwait(false);
        var daten = JsonSerializer.SerializeToNode(m, Ablageformat.Optionen);
        return VerwaltungsAntwort.Gut(string.Create(CultureInfo.InvariantCulture,
            $"{m.Sitzungen} Sitzungen, verarbeitet {m.Verarbeitet}, verworfen {m.VerworfenPruefung}+{m.VerworfenVoll}, Ausgang {m.AusgangGesendet} ({m.AusgangMbitGemessen:F2} Mbit/s), p50 {m.VerarbeitungMsP50:F3} ms, p99 {m.VerarbeitungMsP99:F3} ms, Laststufe {m.Laststufe}"),
            daten);
    }

    private VerwaltungsAntwort? Verwaltung(string befehl, JsonObject a, TaskCompletionSource<VerwaltungsAntwort> tcs)
    {
        var admin = I.Akteur.AdminWerkzeug;
        switch (befehl)
        {
            case "status":
            {
                var z = Schreiber.Aktuell;
                var s = Konten.Stand;
                return VerwaltungsAntwort.Gut(string.Create(CultureInfo.InvariantCulture,
                    $"Funk-Server {Version}: {_verbindungen.Count} Verbindungen, {_sitzungen.Count} Sitzungen, {s.Konten.Count} Konten, {s.Geraete.Count} Geräte, {z.Netze.Count} Kanäle, zustandsVersion {z.ZustandsVersion}, unfreigegeben {Zulassung.UnbekannteVerbindungen}, Vorrangspur {Spur.Anzahl}"));
            }

            case "konto-anlegen":
            {
                if (Text(a, "discord") is not { } discord || Text(a, "benutzername") is not { } benutzer)
                    return VerwaltungsAntwort.Fehler("konto-anlegen braucht discord und benutzername");
                if (StufeAusText(Text(a, "stufe") ?? "FUNKER") is not { } stufe) return VerwaltungsAntwort.Fehler("stufe ungültig");
                var e = Konten.KontoAnlegen(admin, discord, benutzer, Text(a, "name"), stufe);
                if (!e.Ok) return VerwaltungsAntwort.Fehler(I.Drahtnamen.Drahtname(e.Fehler));
                Log.Info("Admin-Werkzeug: Konto angelegt " + Log.Schwaerzung.Konto(e.KontoId));
                return VerwaltungsAntwort.Gut("Konto " + e.KontoId.ToString(CultureInfo.InvariantCulture) + " angelegt", new JsonObject { ["kontoId"] = e.KontoId });
            }

            case "konten":
            {
                var liste = new JsonArray();
                var sb = new StringBuilder();
                foreach (var k in Konten.Stand.Konten.Values.OrderBy(k => k.KontoId))
                {
                    var status = k.IstAntrag ? "ANTRAG" : k.Status == I.KontoStatus.Aktiv ? "AKTIV" : "GESPERRT";
                    // Ohne SC-Namen (etwa Konten aus 0.1.0) der Anzeigename, damit jede Zeile einen Namen trägt.
                    var namen = k.Antrag is { } an ? an.ScName : k.ScNamen.IsEmpty ? k.Name : string.Join("/", k.ScNamen);
                    liste.Add(new JsonObject
                    {
                        ["kontoId"] = k.KontoId,
                        ["name"] = k.Name,
                        ["benutzername"] = k.Benutzername,
                        ["scNamen"] = new JsonArray([.. (k.Antrag is { } a2 ? [a2.ScName] : k.ScNamen.ToArray()).Select(n => (JsonNode)n)]),
                        ["discordName"] = k.DiscordName,
                        ["stufe"] = I.Rechteregeln.Drahtname(k.Stufe),
                        ["status"] = status,
                        ["eigentuemer"] = k.Eigentuemer,
                        ["admin"] = Admins.Eintrag(k.KontoId)?.Zustand.ToString().ToUpperInvariant(),
                        ["rechte"] = new JsonArray([.. I.Rechteregeln.Drahtnamen(k.Rechte).Select(r => (JsonNode)r)]),
                    });
                    sb.Append(CultureInfo.InvariantCulture,
                        $"{k.KontoId,5}  {I.Rechteregeln.Drahtname(k.Stufe),-10} {status,-8} {(k.Eigentuemer ? "EIGENTÜMER " : "")}{namen} (Discord: {k.DiscordName ?? "-"}{(k.Benutzername.Length > 0 ? ", " + k.Benutzername : "")})\n");
                }
                return VerwaltungsAntwort.Gut(sb.ToString().TrimEnd(), liste);
            }

            case "konto-sperren" or "konto-entsperren" or "konto-loeschen" or "stufe" or "rechte" or "name":
            {
                if (Zahl(a, "konto") is not { } konto) return VerwaltungsAntwort.Fehler("konto fehlt");
                // Wie bei stufe (3.10): Den Eigentümer erst wechseln. Sonst stünde der Server ohne Eigentümer da, etwa beim Aufräumen
                // nach einem Antrag ins falsche Konto (HEIMSERVER-PROXMOX.md, Abschnitt 14, Weg B).
                if (befehl == "konto-loeschen" && IstEigentuemer(konto))
                    return VerwaltungsAntwort.Fehler("Das Konto ist Eigentümer und lässt sich nicht löschen. Erst mit 'eigentuemer --konto <id>' ein anderes ADMIN-Konto zum Eigentümer machen. Nichts geändert.");
                I.Ergebnis e;
                var text = befehl;
                switch (befehl)
                {
                    case "konto-sperren": e = Konten.KontoSperren(admin, konto); break;
                    case "konto-entsperren": e = Konten.KontoEntsperren(admin, konto); break;
                    case "konto-loeschen": e = Konten.KontoLoeschen(admin, konto); break;
                    case "stufe":
                        if (StufeAusText(Text(a, "stufe")) is not { } st) return VerwaltungsAntwort.Fehler("stufe ungültig");
                        if (st != I.Stufe.Admin && IstEigentuemer(konto)) return EigentuemerBleibtAdmin();
                        e = Konten.StufeSetzen(admin, konto, st);
                        text = "stufe " + I.Rechteregeln.Drahtname(st);
                        break;
                    case "rechte":
                        if (a["rechte"] is not JsonArray r || !I.Rechteregeln.TryAusDrahtnamen(r.Select(x => x?.GetValue<string>() ?? ""), out var rechte))
                            return VerwaltungsAntwort.Fehler("rechte ungültig");
                        if (r.Count == 0 && !Leeren(a)) return VerwaltungsAntwort.Fehler("rechte leer: alle Rechte wegnehmen nur mit --leeren. Nichts geändert.");
                        e = Konten.RechteSetzen(admin, konto, rechte);
                        break;
                    default:
                        e = Konten.NameSetzen(admin, konto, Text(a, "name") ?? "");
                        break;
                }
                if (!e.Ok) return VerwaltungsAntwort.Fehler(I.Drahtnamen.Drahtname(e.Fehler));
                Vermerken("KONTO", KonsoleDurch, konto, $"Konsole: {text} für {KontoText(konto)}");
                // Wer nicht mehr ADMIN ist, verliert auch den Admin-Nachweis (vorgesehene Konten behalten ihn bis zur Ernennung).
                if (befehl == "stufe" && Konten.Stand.Konten.TryGetValue(konto, out var nachher) && nachher.Stufe != I.Stufe.Admin
                    && Admins.Eintrag(konto) is { Zustand: not I.AdminZustand.Vorgesehen })
                {
                    Admins.Entziehen(konto);
                    Vermerken("ADMIN_ENTZOGEN", KonsoleDurch, konto, $"Konsole: Admin-Nachweis von {KontoText(konto)} entfernt (Stufe geändert)");
                }
                WirkungenAusfuehren(e.Wirkungen);
                KontoGeaendert(konto);
                Log.Info("Admin-Werkzeug: " + befehl + " für " + Log.Schwaerzung.Konto(konto));
                return VerwaltungsAntwort.Gut("OK");
            }

            case "scnamen":
            {
                if (Zahl(a, "konto") is not { } konto) return VerwaltungsAntwort.Fehler("konto fehlt");
                if (a["namen"] is not JsonArray namen) return VerwaltungsAntwort.Fehler("namen fehlt");
                // Eine leere Liste nimmt alle SC-Namen weg; das nur ausdrücklich (funk-admin scnamen --leeren), nie aus Versehen.
                if (namen.Count == 0 && !Leeren(a)) return VerwaltungsAntwort.Fehler("namen leer: alle SC-Namen wegnehmen nur mit --leeren. Nichts geändert.");
                var n = Konten.ScNamenSetzen(admin, konto, [.. namen.Select(x => x?.GetValue<string>() ?? "")]);
                if (!n.Ok) return VerwaltungsAntwort.Fehler(n.Fehler == I.Fehlercode.NameVergeben ? "Den Namen " + n.Vergeben + " hat schon jemand" : I.Drahtnamen.Drahtname(n.Fehler));
                Vermerken("SCNAMEN", KonsoleDurch, konto, $"Konsole: SC-Namen von {KontoText(konto)}: {string.Join(", ", n.ScNamen)}");
                KontoGeaendert(konto);
                return VerwaltungsAntwort.Gut("SC-Namen: " + string.Join(", ", n.ScNamen));
            }

            case "discordname":
            {
                // A53: den Discord-Namen ändert nur ein Admin; hier der Eigentümer an der Konsole, für jedes Konto.
                if (Zahl(a, "konto") is not { } konto) return VerwaltungsAntwort.Fehler("konto fehlt");
                var leeren = Leeren(a);
                var name = Text(a, "name");
                if (leeren == (name is not null)) return VerwaltungsAntwort.Fehler("genau eins von --name <Discord-Name> und --leeren angeben");
                var vorher = Konten.Stand.Konten.TryGetValue(konto, out var kvor) ? kvor.DiscordName : null;
                var n = Konten.DiscordNameSetzen(admin, konto, leeren ? null : name);
                if (!n.Ok)
                {
                    return VerwaltungsAntwort.Fehler(n.Fehler switch
                    {
                        I.Fehlercode.NameVergeben => $"Den Discord-Namen {n.Vergeben} trägt schon {DiscordBesitzer(n.Vergeben!)}. Nichts geändert.",
                        I.Fehlercode.Ungueltig => "Discord-Name ungültig: 2 bis 32 Zeichen, keine unsichtbaren Zeichen, nur einfache Leerzeichen. Nichts geändert.",
                        I.Fehlercode.Unbekannt => "Konto unbekannt (oder noch ein offener Antrag). Nichts geändert.",
                        _ => I.Drahtnamen.Drahtname(n.Fehler),
                    });
                }
                if (!string.Equals(vorher, n.DiscordName, StringComparison.Ordinal))
                {
                    Vermerken("DISCORDNAME", KonsoleDurch, konto, $"Konsole: Discord-Name von {KontoText(konto)}: {n.DiscordName ?? "(keiner)"} statt {vorher ?? "(keiner)"}");
                    KontoGeaendert(konto);
                }
                return VerwaltungsAntwort.Gut("Discord-Name von " + KontoText(konto) + ": " + (n.DiscordName ?? "(keiner)"),
                    new JsonObject { ["kontoId"] = konto, ["discordName"] = n.DiscordName });
            }

            case "journal":
            {
                var anzahl = (int)Math.Clamp(Zahl(a, "anzahl") ?? 50, 1, 1000);
                var liste = Journal.Lesen(null, Zahl(a, "konto"), anzahl);
                var sb = new StringBuilder();
                var daten = new JsonArray();
                foreach (var e in liste)
                {
                    var zeit = DateTimeOffset.FromUnixTimeMilliseconds(e.Zeit).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    sb.Append(CultureInfo.InvariantCulture, $"{zeit} UTC  {e.Art,-24} {e.Text}\n");
                    daten.Add(new JsonObject { ["zeit"] = e.Zeit, ["art"] = e.Art, ["durch"] = e.Durch, ["kontoId"] = e.KontoId, ["text"] = e.Text });
                }
                return VerwaltungsAntwort.Gut(liste.Count == 0 ? "Journal leer" : sb.ToString().TrimEnd(), daten);
            }

            case "mitglieder":
                return MitgliederKonsole();

            case "auftrag" or "auftrag-entziehen" or "auftraege" or "flotte-vorlage":
                return RaidleitungKonsole(befehl, a, tcs);

            case "kanalsperre-setzen" or "kanalsperre-aufheben" or "kanalsperren":
                return KanalsperrKonsole(befehl, a, tcs);

            case "antraege":
            {
                var stand = Konten.Stand;
                var sb = new StringBuilder();
                var daten = new JsonArray();
                foreach (var k in stand.Konten.Values.Where(k => k.IstAntrag).OrderBy(k => k.KontoId))
                {
                    foreach (var g in stand.GeraeteVon(k.KontoId).Where(g => g.Status == I.GeraetStatus.Wartet))
                    {
                        var vorschlaege = Vorschlaege(k);
                        var vergeben = Konten.VergebeneNamen(k);
                        var discordVergeben = Konten.DiscordNameVergeben(k);
                        var passend = PassendeKonten(stand, k);
                        var hinweis = (vergeben.Count > 0 ? "  NAME VERGEBEN" : "")
                            + (discordVergeben ? "  DISCORD-NAME VERGEBEN (" + DiscordBesitzer(k.Antrag!.DiscordName) + ")" : "")
                            + (vorschlaege.Count > 0 ? "  passt zu Vormerkung " + string.Join(", ", vorschlaege.Select(p => p.Id.ToString(CultureInfo.InvariantCulture) + " " + p.ScName)) : "")
                            + string.Concat(passend.Select(p => KontoHinweis(p, g.Anzeige)));
                        sb.Append(CultureInfo.InvariantCulture,
                            $"{g.Anzeige}  {k.Antrag!.ScName} (Discord: {k.Antrag.DiscordName})  seit {k.Antrag.Gestellt:yyyy-MM-dd HH:mm} UTC{hinweis}\n");
                        daten.Add(new JsonObject
                        {
                            ["geraetId"] = g.GeraetId,
                            ["anzeige"] = g.Anzeige,
                            ["kontoId"] = k.KontoId,
                            ["scName"] = k.Antrag.ScName,
                            ["discordName"] = k.Antrag.DiscordName,
                            ["vergeben"] = vergeben.Count > 0,
                            ["discordNameVergeben"] = discordVergeben,
                            ["vormerkungen"] = new JsonArray([.. vorschlaege.Select(p => (JsonNode)p.Id)]),
                            ["konten"] = new JsonArray([.. passend.Select(p => (JsonNode)p.KontoId)]),
                        });
                    }
                }
                return VerwaltungsAntwort.Gut(daten.Count == 0 ? "Keine offenen Anträge" : sb.ToString().TrimEnd(), daten);
            }

            case "eigentuemer" or "admin-vorsehen" or "admin-ernennen" or "admin-einrichtung-neu" or "zweitfaktor-bestaetigen" or "admin-sperren"
                or "admin-entsperren" or "admin-zuruecksetzen" or "admin-entziehen" or "admin-schluessel-entfernen" or "notfallcodes" or "admins":
                return AdminKonsole(befehl, a);

            case "geraete":
            {
                // nichtSeit (JJJJ-MM-TT, UTC): nur Geräte, die seit dem Tag keine Sitzung hatten; die Nachzügler vor einem Umzug
                // (EINRICHTEN-UMZUG-ENTWURF, Vorlauf Schritt 6). Ohne Sitzung überhaupt zählt ein Gerät immer dazu.
                DateTimeOffset? grenze = null;
                if (Text(a, "nichtSeit") is { } ns)
                {
                    if (!DateOnly.TryParseExact(ns, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag))
                        return VerwaltungsAntwort.Fehler("nichtSeit braucht ein Datum wie 2026-10-01");
                    grenze = new DateTimeOffset(tag.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                }
                var auskunft = Konten.GeraeteListe(admin, Zahl(a, "konto"), a["wartend"]?.GetValue<bool>() ?? false);
                var stand = Konten.Stand;
                var liste = new JsonArray();
                var sb = new StringBuilder();
                foreach (var g in auskunft.Geraete)
                {
                    if (grenze is { } gr && g.Zuletzt is { } z && z >= gr) continue;
                    stand.Konten.TryGetValue(g.KontoId, out var k);
                    liste.Add(new JsonObject
                    {
                        ["geraetId"] = g.GeraetId,
                        ["kontoId"] = g.KontoId,
                        ["name"] = g.Name,
                        ["anzeige"] = g.Anzeige,
                        ["fingerabdruck"] = g.Fingerabdruck.AlsBase64(),
                        ["status"] = g.Status.ToString().ToUpperInvariant(),
                        ["schluesselArt"] = I.Drahtnamen.Drahtname(g.SchluesselArt),
                        ["zuletzt"] = g.Zuletzt?.ToUnixTimeMilliseconds(),
                    });
                    var zuletzt = grenze is null ? ""
                        : g.Zuletzt is { } zl ? "  zuletzt " + zl.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "  nie verbunden";
                    sb.Append(CultureInfo.InvariantCulture,
                        $"{g.GeraetId,5}  {g.Anzeige}  {g.Status.ToString().ToUpperInvariant(),-11} {I.Drahtnamen.Drahtname(g.SchluesselArt),-8} {g.Name}  Konto {g.KontoId} {k?.Benutzername} (laut Client, ungeprüft){zuletzt}\n");
                }
                if (grenze is { } gz)
                {
                    var tag = gz.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    sb.Insert(0, liste.Count == 0
                        ? "Alle Geräte waren seit " + tag + " verbunden."
                        : $"Seit {tag} nicht verbunden: {liste.Count} Gerät{(liste.Count == 1 ? "" : "e")}\n");
                }
                return VerwaltungsAntwort.Gut(sb.ToString().TrimEnd(), liste);
            }

            case "freigeben" or "ablehnen" or "entfernen" or "sperren":
            {
                if (Text(a, "geraet") is not { } suche) return VerwaltungsAntwort.Fehler("geraet fehlt (Anzeigeform oder Fingerabdruck)");
                var f = Konten.GeraetSuchen(suche, out var g);
                if (f != I.Fehlercode.Keiner || g is null) return VerwaltungsAntwort.Fehler(f == I.Fehlercode.Ungueltig ? "Anzeigeform mehrdeutig, vollen Fingerabdruck nehmen" : "Gerät unbekannt");
                IReadOnlyList<I.Wirkung> wirkungen;
                if (befehl is "freigeben" or "ablehnen")
                {
                    var entscheidung = befehl == "freigeben" ? S.GeraetEntscheidung.Freigeben : S.GeraetEntscheidung.Ablehnen;
                    var vonVorher = KontoText(g.KontoId);
                    var (e, vm) = GeraetEntscheidenIntern(admin, g.GeraetId, Abbildung.Entscheidung(entscheidung), Zahl(a, "vormerkung"), Zahl(a, "anKonto"));
                    if (!e.Ok) return VerwaltungsAntwort.Fehler(I.Drahtnamen.Drahtname(e.Fehler));
                    FreigabeVermerken(KonsoleDurch, g, vonVorher, entscheidung, vm);
                    wirkungen = e.Wirkungen;
                }
                else
                {
                    var e = Konten.GeraetEntfernen(admin, g.GeraetId, dauerhaft: befehl == "sperren", Text(a, "grund") ?? "");
                    if (!e.Ok) return VerwaltungsAntwort.Fehler(I.Drahtnamen.Drahtname(e.Fehler));
                    Vermerken(befehl == "sperren" ? "GERAET_ENTZOGEN" : "GERAET_ENTFERNT", KonsoleDurch, g.KontoId,
                        $"Konsole: Gerät {g.Name} ({g.Anzeige}) von {KontoText(g.KontoId)} {(befehl == "sperren" ? "dauerhaft gesperrt" : "entfernt")}");
                    wirkungen = e.Wirkungen;
                }
                WirkungenAusfuehren(wirkungen);
                Log.Info("Admin-Werkzeug: Gerät " + befehl + ", Konto " + Log.Schwaerzung.Konto(g.KontoId));
                return VerwaltungsAntwort.Gut("OK: " + g.Anzeige);
            }

            case "sperre-aufheben":
            {
                // Seit 0.8.0: das Gegenstück zu "sperren". Erst ein Gerät suchen, sonst eine Sperre ohne Gerät (Konto später gelöscht).
                if (Text(a, "geraet") is not { } suche) return VerwaltungsAntwort.Fehler("geraet fehlt (Anzeigeform oder Fingerabdruck)");
                var f = Konten.GeraetSuchen(suche, out var g);
                if (f == I.Fehlercode.Ungueltig) return VerwaltungsAntwort.Fehler("Anzeigeform mehrdeutig, vollen Fingerabdruck nehmen");
                I.Ergebnis e;
                string was;
                string anzeige;
                if (g is not null)
                {
                    was = $"Gerät {g.Name} ({g.Anzeige}) von {KontoText(g.KontoId)}";
                    anzeige = g.Anzeige;
                    e = Konten.GeraetEntsperren(admin, g.GeraetId);
                }
                else
                {
                    var fs = Konten.SperreSuchen(suche, out var fp);
                    if (fs == I.Fehlercode.Ungueltig) return VerwaltungsAntwort.Fehler("Anzeigeform mehrdeutig, vollen Fingerabdruck nehmen");
                    if (fs != I.Fehlercode.Keiner) return VerwaltungsAntwort.Fehler("Gerät unbekannt und keine Sperre mit dieser Kennung");
                    was = "Gerätekennung " + fp.Anzeige + " (ohne Gerät, Konto gelöscht)";
                    anzeige = fp.Anzeige;
                    e = Konten.FingerabdruckEntsperren(admin, fp);
                }
                if (!e.Ok) return VerwaltungsAntwort.Fehler(e.Fehler == I.Fehlercode.Ungueltig ? "Gerät ist nicht gesperrt" : I.Drahtnamen.Drahtname(e.Fehler));
                Vermerken("SPERRE_AUFGEHOBEN", KonsoleDurch, g?.KontoId, "Konsole: " + was + ": Sperre aufgehoben, der PC kann neu beantragen");
                WirkungenAusfuehren(e.Wirkungen);
                Log.Info("Admin-Werkzeug: Gerätesperre aufgehoben" + (g is null ? " (ohne Gerät)" : ", Konto " + Log.Schwaerzung.Konto(g.KontoId)));
                return VerwaltungsAntwort.Gut("OK: " + anzeige + " nicht mehr gesperrt. Der PC kann einen neuen Antrag stellen.");
            }

            case "netz-setzen" or "schiff-setzen" or "rolle-setzen":
            {
                if (a["objekt"] is not JsonObject objekt) return VerwaltungsAntwort.Fehler("objekt fehlt");
                var roh = Encoding.UTF8.GetBytes(objekt.ToJsonString());
                Func<L.VerwaltungsZiel, L.Ergebnis> auftrag;
                switch (befehl)
                {
                    case "netz-setzen":
                    {
                        var e = S.SteuerJson.Lesen(roh, S.SteuerJson.Info<S.Netz>());
                        if (e.Body is not S.Netz n) return VerwaltungsAntwort.Fehler("Kanal ungültig: " + e.Feld);
                        var netz = Abbildung.NetzLage(n);
                        auftrag = z => z.NetzSetzen(netz);
                        break;
                    }
                    case "schiff-setzen":
                    {
                        var e = S.SteuerJson.Lesen(roh, S.SteuerJson.Info<S.Schiff>());
                        if (e.Body is not S.Schiff sc) return VerwaltungsAntwort.Fehler("Schiff ungültig: " + e.Feld);
                        var schiff = Abbildung.SchiffLage(sc);
                        auftrag = z => z.SchiffSetzen(schiff);
                        break;
                    }
                    default:
                    {
                        var e = S.SteuerJson.Lesen(roh, S.SteuerJson.Info<S.Rolle>());
                        if (e.Body is not S.Rolle r) return VerwaltungsAntwort.Fehler("Rolle ungültig: " + e.Feld);
                        var rolle = Abbildung.RolleLage(r);
                        auftrag = z => z.RolleSetzen(rolle);
                        break;
                    }
                }
                Schreiben(L.InterneAuftraege.Verwaltung(befehl, auftrag), e => tcs.TrySetResult(Verwaltungsergebnis(e)));
                return null;
            }

            case "netz-loeschen" or "schiff-loeschen" or "rolle-loeschen":
            {
                if (Zahl(a, "id") is not { } id || id is 0 or > ushort.MaxValue) return VerwaltungsAntwort.Fehler("id fehlt");
                var kurz = (ushort)id;
                Func<L.VerwaltungsZiel, L.Ergebnis> auftrag = befehl switch
                {
                    "netz-loeschen" => z => z.NetzLoeschen(kurz),
                    "schiff-loeschen" => z => z.SchiffLoeschen(kurz),
                    _ => z => z.RolleLoeschen(kurz),
                };
                Schreiben(L.InterneAuftraege.Verwaltung(befehl, auftrag), e => tcs.TrySetResult(Verwaltungsergebnis(e)));
                return null;
            }

            case "einstellung":
            {
                bool? selbstwahl = a["selbstwahl"]?.GetValue<bool>();
                bool? nurTaste = a["durchsageNurTaste"]?.GetValue<bool>();
                // Seit 0.3.0 (F9): beim Sperren "Wechsel sperren" (Nachzügler dürfen, Standard) oder "Ganz sperren".
                bool? nachzuegler = a["nachzuegler"]?.GetValue<bool>();
                Schreiben(L.InterneAuftraege.Verwaltung(befehl, z => z.Einstellung(selbstwahl, nurTaste, nachzuegler)), e => tcs.TrySetResult(Verwaltungsergebnis(e)));
                return null;
            }

            case "verdraengung-freigeben":
            {
                if (Zahl(a, "konto") is not { } konto) return VerwaltungsAntwort.Fehler("konto fehlt");
                Schreiben(L.InterneAuftraege.Verwaltung(befehl, z => z.VerdraengungFreigeben(konto)), e => tcs.TrySetResult(Verwaltungsergebnis(e)));
                return null;
            }

            case "netzliste":
            {
                var liste = Abbildung.NetzListe(Schreiber.Aktuell);
                var knoten = JsonNode.Parse(S.SteuerJson.Serialisieren(liste));
                var sb = new StringBuilder();
                // Ein Kanalnetz (6.16) nennt seine Ch., damit die Anleitung die Kanalnetze für den Rückweg herauslesen kann (Abschnitt 18).
                foreach (var n in liste.Netze)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"Kanal {n.Id,5}  {n.Name}  offen {n.Offen}  standard {n.Standard}");
                    if (n.Kanaele is { Count: > 0 } k) sb.Append(CultureInfo.InvariantCulture, $"  Ch. 0 bis {k.Count - 1}");
                    sb.Append('\n');
                }
                foreach (var r in liste.Rollen) sb.Append(CultureInfo.InvariantCulture, $"Rolle {r.Id,4}  {r.Name}  {r.Felder.Count} Felder\n");
                foreach (var s in liste.Schiffe) sb.Append(CultureInfo.InvariantCulture, $"Schiff {s.Id,3}  {s.Name}  {s.Plaetze.Count} Plätze\n");
                sb.Append(CultureInfo.InvariantCulture, $"Version {liste.Version}, Selbstwahl {liste.Einstellungen.Selbstwahl}, Durchsage nur Taste {liste.Einstellungen.DurchsageNurTaste}");
                return VerwaltungsAntwort.Gut(sb.ToString(), knoten);
            }

            case "mitgliedschaft":
            {
                // Server-Seite des Discord-Abgleichs (3.7); bis der Bot-Dienst steht, meldet das Admin-Werkzeug.
                if (Text(a, "discord") is not { } discord) return VerwaltungsAntwort.Fehler("discord fehlt");
                I.Mitgliedschaftsstatus? status = Text(a, "status") switch
                {
                    "OK" => I.Mitgliedschaftsstatus.Ok,
                    "NICHT_MITGLIED" => I.Mitgliedschaftsstatus.NichtMitglied,
                    "ROLLE_FEHLT" => I.Mitgliedschaftsstatus.RolleFehlt,
                    _ => null,
                };
                if (status is null) return VerwaltungsAntwort.Fehler("status: OK, NICHT_MITGLIED oder ROLLE_FEHLT");
                var e = Konten.MitgliedschaftMelden(discord, status.Value, Text(a, "benutzername"), Text(a, "name"));
                if (!e.Ok) return VerwaltungsAntwort.Fehler(I.Drahtnamen.Drahtname(e.Fehler));
                WirkungenAusfuehren(e.Wirkungen);
                return VerwaltungsAntwort.Gut("OK");
            }

            case "sicherung":
            {
                // Sicherung der Zustandsdateien (3.8); Schlüssel und Geheimnis liegen nicht im Zustandsverzeichnis
                // ($CREDENTIALS_DIRECTORY) und kommen nie mit.
                KontenSichern();
                Dateiablage.AtomarSchreiben(_komfortPfad, Komfort.AlsJson());
                Dateiablage.AtomarSchreiben(_vorrangPfad, Spur.AlsJson());
                // Immer und sofort: Ein Stand, der gerade im Hintergrund geschrieben wird, wäre sonst in der Kopie noch nicht drin.
                Journal.Speichern();
                var quelle = Konfiguration.Aufloesen(Konfiguration.Zustandsverzeichnis);
                var ziel = Text(a, "ziel") is { } z ? Konfiguration.Aufloesen(z)
                    : Path.Combine(quelle, "sicherungen", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                Dateiablage.VerzeichnisAnlegen(ziel);
                var kopiert = new JsonArray();
                // admins.json enthält die TOTP-Geheimnisse nur als Chiffrat; der Schlüssel dazu liegt in $CREDENTIALS_DIRECTORY
                // und kommt nie mit (3.9).
                foreach (var datei in new[]
                {
                    Zustandsablage.LageDatei, Zustandsablage.KontenDatei, Komfortablage.Datei, "vorrangspur.json",
                    Zustandsablage.AdminsDatei, Zustandsablage.MitgliederDatei, Journal.Datei,
                })
                {
                    var q = Path.Combine(quelle, datei);
                    if (!File.Exists(q)) continue;
                    var bytes = File.ReadAllBytes(q);
                    // Aufträge kommen nie mit (6.12): Aus einer zurückgespielten Sicherung käme sonst ein entzogener Auftrag zurück.
                    if (datei == Zustandsablage.LageDatei) bytes = Zustandsablage.OhneAuftraege(bytes);
                    Dateiablage.AtomarSchreiben(Path.Combine(ziel, datei), bytes);
                    kopiert.Add(datei);
                }
                return VerwaltungsAntwort.Gut("Sicherung nach " + ziel, new JsonObject { ["ziel"] = ziel, ["dateien"] = kopiert });
            }

            case "vorrangspur":
                return VerwaltungsAntwort.Gut(Spur.Anzahl.ToString(CultureInfo.InvariantCulture) + " Präfixe in der Vorrangspur (nur HMAC-Werte)");

            case "vorrangspur-leeren":
                Spur.Leeren();
                return VerwaltungsAntwort.Gut("Vorrangspur geleert");

            case "geraet-umhaengen":
                return GeraetUmhaengenKonsole(a);

            case "sperrliste" or "entsperren":
                return SperrlisteKonsole(befehl, a);

            case "profil-neu-laden":
            {
                var fehler = ProfilLaden();
                if (fehler is not null) return VerwaltungsAntwort.Fehler(fehler);
                // 0.4.0: dieselben Warnungen wie beim Start, auch hier (UMZUG.md, Schritt 4): der eigene Pin fehlt im Profil, oder das
                // Profil nennt einen anderen TCP-Port als server.json (Prüfung der Pakete 0.4.0: ein Übergangsprofil mit 443 lud Sarevocs
                // Server ohne ein Wort). Geladen ist das Profil trotzdem, wie beim Start.
                var inhalt = _profil is { } p && I.Serverprofil.TryLesen(Convert.FromBase64String(p.Daten), out var gelesen) ? gelesen : null;
                var antwort = inhalt is null ? "Profil geladen." : "Profil Version " + inhalt.ProfilVersion.ToString(CultureInfo.InvariantCulture) + " geladen.";
                var warnungen = new List<string>();
                if (ProfilPinWarnung() is { } pinWarnung) warnungen.Add(pinWarnung);
                if (inhalt is not null && Profilabgleich.PortWarnung(Konfiguration, inhalt.TcpPort) is { } portWarnung) warnungen.Add(portWarnung);
                foreach (var w in warnungen)
                {
                    Log.Warnung(w);
                    antwort += "\nWARNUNG " + w;
                }
                return VerwaltungsAntwort.Gut(antwort);
            }
        }
        return VerwaltungsAntwort.Fehler("unbekannter Befehl: " + befehl);
    }

    private bool IstEigentuemer(uint konto) => Konten.Stand.Konten.TryGetValue(konto, out var k) && k.Eigentuemer;

    /// <summary>Wer einen Discord-Namen trägt, für die Konsole: "Mustermann (Konto 7)" oder "die Vormerkung Hexa".</summary>
    private string DiscordBesitzer(string name) =>
        Konten.DiscordNameBesitzer(Konten.Stand, name, 0, null) is { } b ? DiscordBesitzerText(b.KontoId, b.VormerkungId) : "jemand";

    /// <summary>Der Eigentümer bleibt ADMIN (3.10); herabstufen oder entziehen erst nach einem Wechsel des Eigentümers.</summary>
    private static VerwaltungsAntwort EigentuemerBleibtAdmin() =>
        VerwaltungsAntwort.Fehler("Das Konto ist Eigentümer und bleibt ADMIN. Erst mit 'eigentuemer --konto <id>' ein anderes ADMIN-Konto zum Eigentümer machen. Nichts geändert.");

    private static VerwaltungsAntwort Verwaltungsergebnis(L.Ergebnis e) =>
        e.Erfolg
            ? VerwaltungsAntwort.Gut("OK, zustandsVersion " + e.ZustandsVersion.ToString(CultureInfo.InvariantCulture))
            : VerwaltungsAntwort.Fehler(L.Drahtnamen.Von(e.Fehler));

    private static string? Text(JsonObject a, string name) =>
        a[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary><c>"leeren": true</c> (funk-admin <c>--leeren</c>): Wegnehmen ist gewollt, nicht vertippt.</summary>
    private static bool Leeren(JsonObject a) => a["leeren"] is JsonValue v && v.TryGetValue<bool>(out var l) && l;

    private static uint? Zahl(JsonObject a, string name)
    {
        if (a[name] is not JsonValue v) return null;
        if (v.TryGetValue<uint>(out var u)) return u;
        if (v.TryGetValue<long>(out var l) && l is >= 0 and <= uint.MaxValue) return (uint)l;
        if (v.TryGetValue<string>(out var s) && uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }
}
