// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;
using Funk.Verwaltung;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Uhr für Zusammenspieltests: die Systemuhr plus ein Versatz, den der Test vorstellt (TOTP-Schritte, Leerlauf der
/// Admin-Sitzung, frische Bestätigung). Threadsicher, denn Server und Test lesen sie gleichzeitig.
/// </summary>
internal sealed class Testuhr : TimeProvider
{
    private long _versatz;

    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _versatz));

    public override long GetTimestamp() =>
        TimeProvider.System.GetTimestamp() + (long)(Interlocked.Read(ref _versatz) / (double)TimeSpan.TicksPerSecond * TimeProvider.System.TimestampFrequency);

    public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;

    public void Vor(TimeSpan dauer) => Interlocked.Add(ref _versatz, dauer.Ticks);
}

/// <summary>
/// Admin-Sitzung und Stufe 1 im Zusammenspiel (3.4, 3.9, 3.10, 6.13): Einrichtung über die Client-Bibliothek
/// (<see cref="Adminzugang"/>), Anmeldung, Schritt-Bestätigung, Leerlauf, Notfallcode; der gemeinsame Test aller
/// Verwaltungsnachrichten ohne Admin-Sitzung; Antrag mit Namen, Vormerkung und Pushes nur an Admin-Sitzungen; die
/// Angriffsfälle auf dem Draht; Mitgliedskarte und Zeichen; Konsole und Neustart. Ohne TLS, prozessintern.
/// </summary>
internal static class AdminSitzungTests
{
    private const string DiscordTest = "910000000000000001";
    private const string DiscordBert = "910000000000000002";
    private const string DiscordFritz = "910000000000000003";
    private const string DiscordGina = "910000000000000004";
    private const string DiscordOtto = "910000000000000005";

    private static IPAddress Lokal(int n) => IPAddress.Parse("127.0.0." + n.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Ein eingerichteter Admin: Funker, Admin-Schlüssel (flüchtig), TOTP-Geheimnis wie in der App, Notfallcodes.</summary>
    private sealed class Admin(KernFunker funker, ECDsa schluessel, byte[] geheimnis, IReadOnlyList<string> notfallcodes, Testuhr uhr, string ersterCode) : IDisposable
    {
        /// <summary>Der Code, mit dem die Einrichtung bestätigt wurde, bzw. der zuletzt für eine Anmeldung benutzte.</summary>
        public string LetzterCode { get; private set; } = ersterCode;

        public KernFunker Funker { get; } = funker;
        public FunkClient Client => Funker.Client;
        public ECDsa Schluessel { get; } = schluessel;
        public byte[] Geheimnis { get; } = geheimnis;
        public IReadOnlyList<string> Notfallcodes { get; } = notfallcodes;
        public Testuhr Uhr { get; } = uhr;

        /// <summary>Was die App gerade zeigt.</summary>
        public string Code(int versatz = 0) => I.Totp.CodeText(Geheimnis, I.Totp.Zeitschritt(Uhr.GetUtcNow()) + versatz);

        /// <summary>Nächster Zeitschritt, dann anmelden: jeder Code gilt einmal.</summary>
        public async Task<AdminSitzungAntwort> AnmeldenAsync()
        {
            Uhr.Vor(TimeSpan.FromSeconds(30));
            var r = await Gedrosselt(() => Client.AdminAnmeldenAsync(Schluessel, LetzterCode = Code()));
            Pruefe.Wahr(r.Ok && r.Body is AdminSitzungAntwort, "Admin-Anmeldung: " + r.Fehler);
            return (AdminSitzungAntwort)r.Body!;
        }

        public void Dispose() => Schluessel.Dispose();
    }

    /// <summary>
    /// Wiederholt eine Anfrage, solange die Ratengrenze greift (Admin 1/s mit Stoß 3, Verwaltung 1/s mit Stoß 5); die
    /// Grenzen selbst prüfen die Tests der Bremse. 3 s Pause füllen den Stoß wieder auf: Nonce und Anmeldung brauchen zwei
    /// Marken, und wer nach 1 s schon wieder anfragt, verbraucht die eine für die Nonce und scheitert immer wieder an der
    /// zweiten (über 50 Verstöße in 60 s trennen).
    /// </summary>
    private static async Task<Antwort> Gedrosselt(Func<Task<Antwort>> anfrage)
    {
        for (var i = 0; ; i++)
        {
            var a = await anfrage();
            if (a.Code != Fehlercode.Grenze || i == 12) return a;
            await Task.Delay(3000);
        }
    }

    private static async Task<Rahmenkopie> Gedrosselt(Func<Task<Rahmenkopie>> anfrage)
    {
        for (var i = 0; ; i++)
        {
            var a = await anfrage();
            if (a.Typ != Nachrichtentyp.Fehler || a.Json<Fehler>().Code != Fehlercode.Grenze || i == 12) return a;
            await Task.Delay(3000);
        }
    }

    private static async Task<bool> Warten(Func<bool> bedingung, int ms = 5000)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(20);
        }
        return true;
    }

    private static string? Op(Meldung m) =>
        m.Details is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("op", out var op) ? op.GetString() : null;

    private static bool HatMeldung(KernFunker f, string art, string? op = null) =>
        f.Pushes.Any(p => p.Body is Meldung m && m.Art == art && (op is null || Op(m) == op));

    /// <summary>Der ganze Weg aus 3.9 über die Client-Bibliothek: vorsehen, Antrag, ernennen, Geheimnis, erster Code.</summary>
    private static async Task<Admin> EinrichtenAsync(Testserver t, Testuhr uhr, KernFunker f)
    {
        var c = f.Client;
        var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await t.AdminOk("admin-vorsehen", new JsonObject { ["konto"] = f.KontoId });
        var antrag = await Gedrosselt(() => c.AdminAntragAsync(schluessel));
        Pruefe.Wahr(antrag.Ok && antrag.Body is AdminEinrichtungAntwort { Kennung: not null }, "Antrag: " + antrag.Fehler);
        var kennung = ((AdminEinrichtungAntwort)antrag.Body!).Kennung!;
        Pruefe.Gleich(I.Fingerabdruck.AusSpki(schluessel.ExportSubjectPublicKeyInfo()).Anzeige, kennung, "Kennung zum Vorlesen");
        await t.AdminOk("admin-ernennen", new JsonObject { ["konto"] = f.KontoId, ["kennung"] = kennung });
        var abholen = await Gedrosselt(() => c.GeheimnisAbholenAsync(schluessel));
        Pruefe.Wahr(abholen.Ok && abholen.Body is AdminEinrichtungAntwort { Geheimnis: not null, Uri: not null }, "Geheimnis: " + abholen.Fehler);
        var e = (AdminEinrichtungAntwort)abholen.Body!;
        Pruefe.Wahr(I.Base32.TryDekodieren(e.Geheimnis!, out var g) && g.Length == I.Totp.GeheimnisBytes, "Geheimnis Base32, 160 Bit");
        Pruefe.Wahr(e.Uri!.StartsWith("otpauth://totp/JG%20TacRelay%3A", StringComparison.Ordinal) && e.Uri.Contains("secret=" + e.Geheimnis, StringComparison.Ordinal),
            "otpauth-URI für den QR-Code: " + e.Uri.Split('?')[0]);
        var code = I.Totp.CodeText(g, I.Totp.Zeitschritt(uhr.GetUtcNow()));
        var bestaetigt = await Gedrosselt(() => c.EinrichtungBestaetigenAsync(schluessel, code));
        Pruefe.Wahr(bestaetigt.Ok && bestaetigt.Body is AdminEinrichtungAntwort { Notfallcodes.Count: 10 }, "erster Code: " + bestaetigt.Fehler);
        return new Admin(f, schluessel, g, ((AdminEinrichtungAntwort)bestaetigt.Body!).Notfallcodes!, uhr, code);
    }

    /// <summary>Antrag ohne Code auf dem rohen Draht: Hello, Registrierung mit Namen, Antwort, dann GeraetStatus{WARTET}.</summary>
    private static async Task<(Testfunker F, GeraetStatus Wartet)> AntragAsync(Testserver t, string sc, string discord, int lokal)
    {
        var f = await t.VerbindenAsync(null, Lokal(lokal));
        var w = await f.HelloAsync();
        Pruefe.Gleich(WelcomeStatus.Unbekannt, w.Status, "Welcome UNBEKANNT");
        var a = await f.AnfrageAsync(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = "PC " + sc, ScName = sc, DiscordName = discord });
        Pruefe.Gleich(Nachrichtentyp.Registrierung, a.Typ, "Antwort auf den Antrag " + sc);
        var antwort = a.Json<RegistrierungAntwort>();
        Pruefe.Wahr(antwort.Antrag == true && antwort.Code is null, "Antrag ohne Code");
        var g = (await f.ErwarteAsync(Nachrichtentyp.GeraetStatus)).Json<GeraetStatus>();
        Pruefe.Wahr(g.Status == GeraeteStatus.Wartet && g.Antrag == true && g.ScName == sc.Trim() && g.DiscordName == discord, "GeraetStatus{WARTET} des Antrags");
        return (f, g);
    }

    private static JsonElement Json(object wert) => JsonSerializer.SerializeToElement(wert);

    private static string[] Liste(params string[] werte) => werte;

    // ---------------------------------------------------------------- Einrichtung, Anmeldung, Bestätigung, Leerlauf

    [Test]
    public static async Task Zusammenspiel_EinrichtenAnmeldenBestaetigenUndLeerlauf()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(31));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(32));
        var c = test.Client;

        // Stufe ADMIN allein öffnet keine Verwaltung.
        var ohne = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { KontoId = fritz.KontoId }));
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, ohne.Code, "GeraeteListe fremd ohne Admin-Sitzung");
        using var fremderSchluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => c.AdminAntragAsync(fremderSchluessel))).Code, "Antrag ohne admin-vorsehen");

        using var admin = await EinrichtenAsync(t, uhr, test);
        Pruefe.Wahr(await Warten(() => c.Lage.Konto is { Admin: AdminStatus.Aktiv } k && k.Zeichen.Contains("ADMIN")), "Mitgliedschaften: AKTIV mit Zeichen ADMIN");
        Pruefe.Wahr(HatMeldung(test, MeldungsArten.AdminGeaendert, "ANTRAG") && HatMeldung(test, MeldungsArten.AdminGeaendert, "EINGERICHTET"),
            "Warnungen an das ADMIN-Konto: Antrag und Einrichtung");
        Pruefe.Falsch(HatMeldung(fritz, MeldungsArten.AdminGeaendert), "ein Funker sieht keine Admin-Warnungen");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => c.GeheimnisAbholenAsync(admin.Schluessel))).Code, "nach der Einrichtung gibt es kein Geheimnis mehr");

        // Code der Einrichtung ein zweites Mal: abgewiesen (Einmalverwendung).
        var wiederholt = await Gedrosselt(() => c.AdminAnmeldenAsync(admin.Schluessel, admin.LetzterCode));
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, wiederholt.Code, "Code der Einrichtung noch einmal");

        var sitzung = await admin.AnmeldenAsync();
        var jetzt = uhr.GetUtcNow();
        // Seit 0.9.1 (Sarevoc 08.10.2026, Q54) nur noch die Höchstdauer 12 h; bis 0.9.0 endete sie nach 30 min ohne Verwaltung.
        Pruefe.Wahr(Math.Abs(sitzung.Bis - (jetzt + TimeSpan.FromHours(12)).ToUnixTimeMilliseconds()) < 5000, "Höchstdauer 12 h");
        Pruefe.Wahr(Math.Abs(sitzung.BestaetigtBis - (jetzt + TimeSpan.FromMinutes(5)).ToUnixTimeMilliseconds()) < 5000, "frisch bestätigt 5 min");
        Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.AdminSitzung, "BEGONNEN")), "Warnung: Admin-Sitzung von einem neuen Gerät");

        var liste = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { KontoId = fritz.KontoId }));
        Pruefe.Wahr(liste.Ok && liste.Body is GeraeteListeAntwort { Geraete.Count: 1 }, "GeraeteListe in der Admin-Sitzung");
        var rechte = new KontoVerwaltungAnfrage { Op = KontoOperation.Rechte, KontoId = fritz.KontoId, Wert = Json(Liste("DURCHSAGE")) };
        Pruefe.Wahr((await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, rechte))).Ok, "RECHTE mit frischer Bestätigung");
        var privilegiert = rechte with { Wert = Json(Liste("KONTEN_VERWALTEN")) };
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, (await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, privilegiert))).Code, "Verwaltungsrecht als Einzelrecht nur an der Konsole");

        // Nach 5 min: SCHRITT_BESTAETIGEN mit dem Ende der Sitzung; der Kern fragt einen Code und sendet erneut.
        uhr.Vor(TimeSpan.FromMinutes(6));
        var ohneCode = rechte with { Wert = Json(Array.Empty<string>()) };
        var schritt = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, ohneCode));
        Pruefe.Gleich(Fehlercode.SchrittBestaetigen, schritt.Code, "heikel nach 6 min");
        Pruefe.Wahr(schritt.Fehler!.Details is { } d && d.TryGetProperty("bis", out var bis) && bis.GetInt64() > uhr.GetUtcNow().ToUnixTimeMilliseconds(), "details.bis");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, (await Gedrosselt(() => c.AdminBestaetigenAsync(admin.LetzterCode))).Code, "Code der Anmeldung wiederholt");
        var gefragt = 0;
        var mit = await Gedrosselt(() => c.MitBestaetigungAsync(Nachrichtentyp.KontoVerwaltung, ohneCode, _ =>
        {
            gefragt++;
            return Task.FromResult<string?>(admin.Code());
        }));
        Pruefe.Wahr(mit.Ok && gefragt >= 1, "mit Bestätigung durchgegangen");
        Pruefe.Wahr(await Warten(() => t.Dienst.Zentrale.Konten.Stand.Konten[fritz.KontoId].Rechte == I.Rechte.Keine), "Rechte gesetzt");

        // Journal: nur in der Admin-Sitzung, mit Klarnamen, ohne Geheimnis und ohne Code.
        var journal = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Journal, new JournalAnfrage()));
        Pruefe.Wahr(journal.Ok && journal.Body is JournalAntwort, "Journal");
        var eintraege = ((JournalAntwort)journal.Body!).Eintraege;
        foreach (var art in new[] { "ADMIN_VORGESEHEN", "ADMIN_ANTRAG", "ADMIN_ERNANNT", "ZWEITFAKTOR_ABGEHOLT", "ZWEITFAKTOR_EINGERICHTET", "ADMIN_SITZUNG", "SCHRITT_BESTAETIGT", "KONTO", "ZWEITFAKTOR_FEHLVERSUCH" })
            Pruefe.Wahr(eintraege.Any(e => e.Art == art), "Journal enthält " + art);
        var geheimnisText = I.Base32.Kodieren(admin.Geheimnis);
        Pruefe.Falsch(eintraege.Any(e => e.Text.Contains(geheimnisText[..8], StringComparison.Ordinal)), "kein Geheimnis im Journal");

        // Seit 0.9.1 (Q54): 31 min ohne Verwaltungshandlung beenden die Admin-Sitzung nicht mehr, erst die Höchstdauer von 12 h.
        uhr.Vor(TimeSpan.FromMinutes(31));
        Pruefe.Wahr((await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { KontoId = fritz.KontoId }))).Ok, "nach 31 min ohne Verwaltung noch offen");
        uhr.Vor(TimeSpan.FromHours(12));
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { KontoId = fritz.KontoId }))).Code, "nach 12 h");
        Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.AdminSitzung, "BEENDET")), "Meldung: Admin-Sitzung beendet");
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => c.AdminAbmeldenAsync())).Code, "Abmelden ohne Sitzung");
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => c.AdminBestaetigenAsync(admin.Code()))).Code, "Bestätigen ohne Sitzung zählt nicht");

        // Telefon weg: Notfallcode, einmal; danach abmelden.
        var notfall = await Gedrosselt(() => c.AdminAnmeldenAsync(admin.Schluessel, null, admin.Notfallcodes[0]));
        Pruefe.Wahr(notfall.Ok && notfall.Body is AdminSitzungAntwort { NotfallcodesUebrig: 9 }, "Notfallcode, 9 übrig");
        Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.Notfallcode)), "Warnung: Notfallcode benutzt");
        Pruefe.Wahr((await Gedrosselt(() => c.AdminAbmeldenAsync())).Ok, "abgemeldet");
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Journal, new JournalAnfrage()))).Code, "nach dem Abmelden");
        Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, (await Gedrosselt(() => c.AdminAnmeldenAsync(admin.Schluessel, null, admin.Notfallcodes[0]))).Code, "Notfallcode nur einmal");

        // Nichts Geheimes auf der Platte oder im Logbuch.
        await Task.Delay(1500);
        var zustand = Path.Combine(t.Verzeichnis, "zustand");
        Pruefe.Wahr(await Warten(() => File.Exists(Path.Combine(zustand, "admins.json"))), "admins.json geschrieben");
        foreach (var datei in Directory.GetFiles(zustand, "*.json"))
        {
            var inhalt = await File.ReadAllTextAsync(datei);
            Pruefe.Falsch(inhalt.Contains(geheimnisText, StringComparison.Ordinal) || inhalt.Contains(Convert.ToBase64String(admin.Geheimnis), StringComparison.Ordinal),
                "Geheimnis im Klartext in " + Path.GetFileName(datei));
            Pruefe.Falsch(admin.Notfallcodes.Any(n => inhalt.Contains(n, StringComparison.Ordinal) || inhalt.Contains(n.Replace("-", "", StringComparison.Ordinal), StringComparison.Ordinal)),
                "Notfallcode in " + Path.GetFileName(datei));
        }
        Pruefe.Falsch(t.Logbuch.Any(z => z.Contains(geheimnisText[..8], StringComparison.Ordinal) || admin.Notfallcodes.Any(n => z.Contains(n, StringComparison.Ordinal))),
            "kein Geheimnis und kein Notfallcode im Logbuch");
    }

    // ---------------------------------------------------------------- Gemeinsamer Test: ohne Admin-Sitzung kein Weg

    [Test]
    public static async Task Zusammenspiel_OhneAdminSitzungKeinVerwaltungsweg()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(41));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(42));
        using var admin = await EinrichtenAsync(t, uhr, test);
        var (antragsteller, wartet) = await AntragAsync(t, "Wartender", "wartender", 43);
        await using var antragHalter = antragsteller;
        var z = t.Dienst.Zentrale;
        var fritzGeraet = z.Konten.Stand.GeraeteVon(fritz.KontoId).Single().GeraetId;
        var adminsVorher = z.Admins.Stand.Version;
        var mitgliederVorher = z.Mitglieder.Stand.Version;

        // Jede Verwaltungsnachricht, ohne Admin-Sitzung gesendet. Erwartet: ADMIN_SITZUNG_NOETIG vom eingerichteten ADMIN,
        // KEIN_RECHT vom Funker (die beiden Admin-Nachrichten ohne Sitzung: ADMIN_SITZUNG_NOETIG für beide).
        var faelle = new (string Was, Nachrichtentyp Typ, object Body, Fehlercode Funker)[]
        {
            ("GeraeteListe fremd", Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { KontoId = wartet.KontoId }, Fehlercode.KeinRecht),
            ("GeraetEntscheiden", Nachrichtentyp.GeraetEntscheiden, new GeraetEntscheidenAnfrage { GeraetId = wartet.GeraetId, Entscheidung = GeraetEntscheidung.Freigeben }, Fehlercode.KeinRecht),
            ("GeraetEntfernen dauerhaft", Nachrichtentyp.GeraetEntfernen, new GeraetEntfernenAnfrage { GeraetId = fritzGeraet, Dauerhaft = true, Grund = "x" }, Fehlercode.KeinRecht),
            // Seit 0.8.0: Sperre aufheben, mit den Rechten des Sperrens.
            ("GeraetEntscheiden ENTSPERREN", Nachrichtentyp.GeraetEntscheiden, new GeraetEntscheidenAnfrage { GeraetId = fritzGeraet, Entscheidung = GeraetEntscheidung.Entsperren }, Fehlercode.KeinRecht),
            ("KontoVerwaltung SPERREN", Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage { Op = KontoOperation.Sperren, KontoId = fritz.KontoId }, Fehlercode.KeinRecht),
            ("KontoVerwaltung STUFE", Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage { Op = KontoOperation.Stufe, KontoId = fritz.KontoId, Wert = Json("RAIDLEITER") }, Fehlercode.KeinRecht),
            ("KontoVerwaltung SCNAMEN", Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage { Op = KontoOperation.Scnamen, KontoId = fritz.KontoId, Wert = Json(Liste("Fremd")) }, Fehlercode.KeinRecht),
            ("Verwaltung NETZ_SETZEN", Nachrichtentyp.Verwaltung, new VerwaltungAnfrage
            {
                Op = VerwaltungsOperation.NetzSetzen,
                Objekt = Json(new { id = 9, name = "Heimlich", typ = "NORMAL", reihenfolge = 9, nurTaste = false, ausloeserStandard = "TASTE", offen = "HOEREN_SENDEN", standard = false }),
                Version = 0,
            }, Fehlercode.KeinRecht),
            ("Journal", Nachrichtentyp.Journal, new JournalAnfrage(), Fehlercode.KeinRecht),
            ("Mitglied LISTE", Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Liste }, Fehlercode.KeinRecht),
            ("Mitglied VORMERKEN", Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Vormerken, ScName = "Heimlich" }, Fehlercode.KeinRecht),
            ("Mitglied EINFUEGEN", Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Einfuegen, Zeilen = ["Heimlich Zwei"] }, Fehlercode.KeinRecht),
            ("Mitglied AENDERN", Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Aendern, KontoId = fritz.KontoId, Notiz = "heimlich" }, Fehlercode.KeinRecht),
            // Seit 0.11.7: Abzeichen vergibt nur ein Admin in der Admin-Sitzung.
            ("Abzeichen", Nachrichtentyp.Abzeichen, new AbzeichenAnfrage { KontoId = fritz.KontoId, Raenge = ["MARINE_01"], Taetigkeiten = ["T_SANI"] }, Fehlercode.KeinRecht),
            ("AntraegeAblehnen", Nachrichtentyp.AntraegeAblehnen, new AntraegeAblehnenAnfrage { NurOhneVormerkung = true }, Fehlercode.KeinRecht),
            ("AdminBestaetigen", Nachrichtentyp.AdminBestaetigen, new AdminBestaetigenAnfrage { Code = "123456" }, Fehlercode.AdminSitzungNoetig),
            ("AdminAbmelden", Nachrichtentyp.AdminAbmelden, LeererBody.Wert, Fehlercode.AdminSitzungNoetig),
        };

        async Task Durchlaufen(KernFunker f, bool alsAdmin)
        {
            foreach (var (was, typ, body, funker) in faelle)
            {
                var a = await Gedrosselt(() => f.Client.AnfrageAsync(typ, body));
                Pruefe.Gleich(alsAdmin ? Fehlercode.AdminSitzungNoetig : funker, a.Code, (alsAdmin ? "ADMIN ohne Admin-Sitzung: " : "Funker: ") + was);
            }
        }
        await Task.WhenAll(Durchlaufen(test, alsAdmin: true), Durchlaufen(fritz, alsAdmin: false));

        // P-45: ohne Seiteneffekt.
        var k = z.Konten.Stand;
        Pruefe.Wahr(k.Konten[fritz.KontoId] is { Status: I.KontoStatus.Aktiv, Stufe: I.Stufe.Funker } f2 && f2.ScNamen.IsEmpty && f2.Rechte == I.Rechte.Keine, "Fritz unverändert");
        Pruefe.Gleich(I.GeraetStatus.Wartet, k.Geraete[wartet.GeraetId].Status, "Antrag wartet weiter");
        Pruefe.Gleich(I.GeraetStatus.Freigegeben, k.Geraete[fritzGeraet].Status, "Gerät von Fritz unverändert");
        Pruefe.Gleich(mitgliederVorher, z.Mitglieder.Stand.Version, "Mitgliederbuch unverändert");
        Pruefe.Gleich(adminsVorher, z.Admins.Stand.Version, "Adminbuch unverändert");
        Pruefe.Wahr(test.Client.Lage.NetzVon(9) is null, "kein Netz angelegt");
        Pruefe.Falsch(test.Pushes.Any(p => p.Body is GeraetStatus { Antrag: true }), "kein Antrag an eine Sitzung ohne Admin-Sitzung");

        // Rollenkatalog SETZEN (Sarevoc am 30.09.2026, Antwort 4): Ergänzen darf die Führung ohne Admin-Sitzung, ein ADMIN-Konto
        // also ebenso; Umbenennen und Löschen bleiben in der Admin-Sitzung. Der Funker darf keins von beiden.
        RollenkatalogAnfrage KatalogSetzen(params (ushort Id, string Name)[] e) =>
            new() { Op = RegelOperation.Setzen, Eintraege = [.. e.Select(x => new Rollenkatalogeintrag { Id = x.Id, Name = x.Name })] };
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.Rollenkatalog, KatalogSetzen((1, "Pilot"))))).Code, "Funker: Rolle ergänzen");
        var ergaenzt = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Rollenkatalog, KatalogSetzen((1, "Pilot"))));
        Pruefe.Wahr(ergaenzt.Ok && ergaenzt.Body is RollenkatalogAntwort { Eintraege: [{ Name: "Pilot" }] }, "ADMIN ohne Admin-Sitzung ergänzt wie die Führung: " + ergaenzt.Fehler);
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Rollenkatalog, KatalogSetzen((1, "Jäger"))))).Code,
            "ADMIN ohne Admin-Sitzung: umbenennen");
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Rollenkatalog, KatalogSetzen((2, "Gunner"))))).Code,
            "ADMIN ohne Admin-Sitzung: ersetzen (Pilot fiele weg)");
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Rollenkatalog, KatalogSetzen()))).Code,
            "ADMIN ohne Admin-Sitzung: leeren");
        Pruefe.Wahr(z.Mitglieder.Stand.Katalog is { Count: 1 } kat && kat[1].Name == "Pilot", "Katalog nur ergänzt");

        // Gegenprobe: dieselbe Nachricht in der Admin-Sitzung geht durch.
        await admin.AnmeldenAsync();
        var liste = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Liste }));
        Pruefe.Wahr(liste.Ok && liste.Body is MitgliedAntwort { Mitglieder: { } m } && m.Any(e => e.Zustand == MitgliedZustand.Antrag && e.ScNamen.Contains("Wartender")),
            "Mitglied LISTE in der Admin-Sitzung, mit dem Antrag");
        var rollen = await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.Rollenkatalog, new RollenkatalogAnfrage { Op = RegelOperation.Lesen }));
        Pruefe.Wahr(rollen.Ok, "Rollenkatalog lesen darf jeder");

        // Gegenprobe für jede Nachricht der Liste: In der frisch bestätigten Admin-Sitzung bleibt keine an der Schranke
        // hängen. Die Handlungen wirken dabei wirklich (Antrag freigegeben, Gerät von Fritz gesperrt und seit 0.8.0 gleich danach die
        // Sperre aufgehoben, also entfernt; Konto gesperrt); AdminAbmelden steht zuletzt. NETZ_SETZEN mit Version 0 darf VERALTET ergeben, AdminBestaetigen mit "123456" einen
        // falschen Code: beides kommt erst hinter der Schranke. Die Konto-ID von Fritz vorher merken: Nach dem Entzug seines
        // Geräts ist er getrennt, und sein Lagebild kennt kein Konto mehr.
        var fritzKonto = fritz.KontoId;
        foreach (var (was, typ, body, _) in faelle)
        {
            var a = await Gedrosselt(() => test.Client.AnfrageAsync(typ, body));
            Pruefe.Falsch(a.Code is Fehlercode.AdminSitzungNoetig or Fehlercode.SchrittBestaetigen or Fehlercode.KeinRecht or Fehlercode.NurAdminWerkzeug,
                "in der Admin-Sitzung an der Schranke vorbei: " + was + " (" + a.Code + ")");
        }
        var nachher = z.Konten.Stand;
        // ENTFERNT beweist beides: Ohne die Sperre davor bliebe das Gerät freigegeben (ENTSPERREN auf ein freies Gerät ist UNGUELTIG).
        Pruefe.Wahr(nachher.Konten[fritzKonto] is { Status: I.KontoStatus.Gesperrt, Stufe: I.Stufe.Raidleiter } && nachher.Geraete[fritzGeraet].Status == I.GeraetStatus.Entfernt
            && nachher.Geraete[wartet.GeraetId].Status == I.GeraetStatus.Freigegeben, "die Handlungen haben gewirkt");
    }

    // ---------------------------------------------------------------- Antrag mit Namen, Vormerkung, Pushes

    [Test]
    public static async Task Zusammenspiel_AntragVormerkungUndPushesNurAnAdminSitzungen()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(51));
        await using var bert = await KernFunker.AktivAsync(halter, DiscordBert, "bert", "ADMIN", Lokal(52));
        using var admin = await EinrichtenAsync(t, uhr, test);
        var c = test.Client;

        // Vor der Admin-Sitzung: ein Antrag erreicht niemanden.
        test.PushesLeeren();
        bert.PushesLeeren();
        var (frueh, _) = await AntragAsync(t, "Frueh", "frueh_dc", 53);
        await using var fruehHalter = frueh;
        await Task.Delay(500);
        Pruefe.Falsch(test.Pushes.Any(p => p.Body is Meldung { Art: MeldungsArten.AntragNeu } || p.Body is GeraetStatus { Antrag: true }), "ohne Admin-Sitzung kein Antrag");

        await admin.AnmeldenAsync();
        var katalog = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Rollenkatalog, new RollenkatalogAnfrage
        {
            Op = RegelOperation.Setzen,
            Eintraege = [new Rollenkatalogeintrag { Id = 1, Name = "Pilot" }, new Rollenkatalogeintrag { Id = 2, Name = "Sanitäter" }],
            Version = 0,
        }));
        Pruefe.Wahr(katalog.Ok && katalog.Body is RollenkatalogAntwort { Version: 1, Eintraege.Count: 2 }, "Rollenkatalog gesetzt: " + katalog.Fehler);
        var vm = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Vormerken, ScName = "Sarevoc", DiscordName = "sarevoc_dc", Rollen = [2] }));
        Pruefe.Wahr(vm.Ok && vm.Body is MitgliedAntwort { Ids.Count: 1 }, "vorgemerkt: " + vm.Fehler);
        var vmId = ((MitgliedAntwort)vm.Body!).Ids![0];
        var zweitVm = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Vormerken, ScName = "Zweiter" }));
        Pruefe.Wahr(zweitVm.Ok, "zweite Vormerkung");
        Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.MitgliedGeaendert)), "MITGLIED_GEAENDERT an die Admin-Sitzung");
        Pruefe.Falsch(HatMeldung(bert, MeldungsArten.MitgliedGeaendert), "nicht an ein ADMIN-Konto ohne Admin-Sitzung");

        // Jetzt erreicht der Antrag die Admin-Sitzung, mit passender Vormerkung als Vorschlag.
        var (sarevoc, _) = await AntragAsync(t, "Sarevoc", "sarevoc_dc", 54);
        await using var sarevocHalter = sarevoc;
        var push = await test.PushAsync<GeraetStatus>(g => g.Antrag == true && g.ScName == "Sarevoc", TimeSpan.FromSeconds(5));
        Pruefe.Wahr(push is { Vormerkungen: { Count: 1 } v } && v[0].Id == vmId && v[0].UeberScName && v[0].Rollen.SequenceEqual(new ushort[] { 2 }),
            "GeraetStatus mit Vorschlag aus den Vormerkungen");
        Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.AntragNeu)), "Meldung ANTRAG_NEU");
        Pruefe.Falsch(bert.Pushes.Any(p => p.Body is GeraetStatus { Antrag: true } || p.Body is Meldung { Art: MeldungsArten.AntragNeu }), "Bert ohne Admin-Sitzung sieht keinen Antrag");

        var entscheiden = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.GeraetEntscheiden, new GeraetEntscheidenAnfrage
        {
            GeraetId = push!.GeraetId,
            Entscheidung = GeraetEntscheidung.Freigeben,
            VormerkungId = vmId,
        }));
        Pruefe.Wahr(entscheiden.Ok, "mit Vormerkung freigegeben: " + entscheiden.Fehler);
        Pruefe.Gleich(GeraeteStatus.Freigegeben, (await sarevoc.ErwarteAsync(Nachrichtentyp.GeraetStatus)).Json<GeraetStatus>().Status, "FREIGEGEBEN");
        var welcome = (await sarevoc.ErwarteAsync(Nachrichtentyp.Welcome)).Json<Welcome>();
        Pruefe.Wahr(welcome.Konto is { ScNamen: ["Sarevoc"], DiscordName: "sarevoc_dc", Benutzername: "-" }, "Konto mit Hauptname aus der Vormerkung, ohne Discord-Anbindung");
        await sarevoc.ErwarteAsync(Nachrichtentyp.NetzListe);
        var m = (await sarevoc.ErwarteAsync(Nachrichtentyp.Mitgliedschaften)).Json<Mitgliedschaften>();
        Pruefe.Wahr(m.Konto is { Rollen: [2] }, "Rollen aus der Vormerkung sofort da");
        var praesenz = (await sarevoc.ErwarteAsync(Nachrichtentyp.Praesenz)).Json<Praesenz>();
        Pruefe.Wahr(praesenz.Eintraege.Any(e => e.ScName == "Sarevoc"), "Praesenz trägt den Hauptnamen");
        Pruefe.Wahr(await Warten(() => test.Client.Lage.Praesenz.Values.Any(e => e.ScName == "Sarevoc")), "die anderen sehen ihn auch");

        // Angriffsfall "Namen besetzen": ein zweiter Antrag "sarevoc" wird angenommen, bekommt den Namen aber nicht.
        var (dieb, _) = await AntragAsync(t, "sarevoc", "dieb", 55);
        await using var diebHalter = dieb;
        var wartende = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { NurWartend = true }));
        var eintrag = ((GeraeteListeAntwort)wartende.Body!).Geraete.Single(e => e.DiscordName == "dieb");
        Pruefe.Wahr(eintrag is { Antrag: true, ScNamenVergeben: ["sarevoc"] }, "Freigabeansicht: Name vergeben");
        Pruefe.Wahr((await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.GeraetEntscheiden, new GeraetEntscheidenAnfrage { GeraetId = eintrag.Id, Entscheidung = GeraetEntscheidung.Freigeben }))).Ok,
            "trotzdem freigegeben");
        await dieb.ErwarteAsync(Nachrichtentyp.GeraetStatus);
        var diebWelcome = (await dieb.ErwarteAsync(Nachrichtentyp.Welcome)).Json<Welcome>();
        Pruefe.Wahr(diebWelcome.Konto is { ScNamen: null } && diebWelcome.Konto.Name != welcome.Konto!.Name, "ohne den Namen, eindeutiger Anzeigename");
        await dieb.ErwarteAsync(Nachrichtentyp.Zustand);
        Pruefe.Wahr(await Warten(() => dieb.VoiceKeysErhalten >= 1), "VoiceKeys beantwortet, AKTIV");
        await Task.Delay(100);
        var profil = await Gedrosselt(() => dieb.AnfrageAsync(Nachrichtentyp.ProfilSetzen, new ProfilSetzenAnfrage { ScNamen = ["SAREVOC"] }));
        Pruefe.Wahr(profil.Typ == Nachrichtentyp.Fehler && profil.Json<Fehler>() is { Code: Fehlercode.NameVergeben } f && f.Details?.GetProperty("name").GetString() == "SAREVOC",
            "ProfilSetzen mit fremdem Namen: NAME_VERGEBEN");
        var eigenerName = await Gedrosselt(() => dieb.AnfrageAsync(Nachrichtentyp.ProfilSetzen, new ProfilSetzenAnfrage { ScNamen = ["Dieb"], ZeichenZeigen = false }));
        Pruefe.Wahr(eigenerName.Typ == Nachrichtentyp.ProfilSetzen && eigenerName.Json<ProfilSetzenAntwort>() is { ScNamen: ["Dieb"], ZeichenZeigen: false }, "eigener Name geht");

        // "Alle ablehnen, die zu keiner Vormerkung passen": Frueh fliegt, Zweiter bleibt.
        var (zweiter, _) = await AntragAsync(t, "Zweiter", "zweiter_dc", 56);
        await using var zweiterHalter = zweiter;
        var ablehnen = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.AntraegeAblehnen, new AntraegeAblehnenAnfrage { NurOhneVormerkung = true }));
        Pruefe.Wahr(ablehnen.Ok && ablehnen.Body is AntraegeAblehnenAntwort { Anzahl: 1 }, "einer abgelehnt: " + ablehnen.Fehler);
        Pruefe.Gleich(GeraeteStatus.Abgelehnt, (await frueh.ErwarteAsync(Nachrichtentyp.GeraetStatus)).Json<GeraetStatus>().Status, "Frueh ABGELEHNT");
        // Seit 0.7.0 ist der Antrag damit gelöscht; Bye{ENTFERNT}, damit der Client nicht von selbst neu beantragt.
        Pruefe.Gleich(ByeGrund.Entfernt, (await frueh.ErwarteAsync(Nachrichtentyp.Bye)).Json<Bye>().Grund, "Frueh Bye ENTFERNT");
        await frueh.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));
        Pruefe.Falsch(zweiter.Getrennt, "Zweiter wartet weiter");

        // Liste einfügen (zweimal, je höchstens 200 Zeilen) und seitenweise lesen: 250 je Seite, weiter mit "ab" (4.10).
        foreach (var block in new[] { Enumerable.Range(0, 200), Enumerable.Range(200, 100) })
        {
            var zeilen = block.Select(i => $"Pilot {i:D3}; pilot{i}; ; Pilot").ToList();
            var einfuegen = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Einfuegen, Zeilen = zeilen }));
            Pruefe.Wahr(einfuegen.Ok && einfuegen.Body is MitgliedAntwort { Zeilen: null } e && e.Ids!.Count == zeilen.Count, "Liste eingefügt: " + einfuegen.Fehler);
        }
        var seite1 = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Liste }));
        var m1 = (MitgliedAntwort)seite1.Body!;
        Pruefe.Wahr(m1.Mitglieder!.Count == Grenzen.MitgliederListeMax && m1.Weiter == (uint)Grenzen.MitgliederListeMax, "erste Seite voll, weiter bei 250");
        var seite2 = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Liste, Ab = m1.Weiter }));
        var m2 = (MitgliedAntwort)seite2.Body!;
        Pruefe.Wahr(m2.Weiter is null && m2.Mitglieder!.Count > 0, "zweite Seite ist die letzte");
        var alle = m1.Mitglieder!.Concat(m2.Mitglieder!).ToList();
        Pruefe.Wahr(alle.Count(e => e.Zustand == MitgliedZustand.Vorgemerkt) == 301 && alle.Select(e => (e.KontoId, e.VormerkungId)).Distinct().Count() == alle.Count,
            "alle Vormerkungen genau einmal (300 eingefügt und Zweiter)");

        // Konsole: die Anträge und Mitglieder lesen.
        var antraege = await t.AdminOk("antraege");
        Pruefe.Wahr(antraege.Text.Contains("Zweiter", StringComparison.Ordinal), "antraege nennt den offenen Antrag: " + antraege.Text);
        var mitglieder = await t.AdminOk("mitglieder");
        Pruefe.Wahr(mitglieder.Text.Contains("Zweiter", StringComparison.Ordinal) && mitglieder.Text.Contains("Sarevoc", StringComparison.Ordinal), "mitglieder");
    }

    /// <summary>Telefon weg: Die Schritt-Bestätigung geht über den Kern auch mit einem Notfallcode (Gegenprüfung Stufe 1).</summary>
    [Test]
    public static async Task Zusammenspiel_SchrittBestaetigungMitNotfallcode()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(91));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(92));
        using var admin = await EinrichtenAsync(t, uhr, test);
        await admin.AnmeldenAsync();
        uhr.Vor(TimeSpan.FromMinutes(6));
        var rechte = new KontoVerwaltungAnfrage { Op = KontoOperation.Rechte, KontoId = fritz.KontoId, Wert = Json(Liste("DURCHSAGE")) };
        var gefragt = 0;
        var mit = await Gedrosselt(() => test.Client.MitBestaetigungAsync(Nachrichtentyp.KontoVerwaltung, rechte, _ =>
        {
            gefragt++;
            return Task.FromResult<string?>(admin.Notfallcodes[gefragt - 1]);
        }));
        Pruefe.Wahr(mit.Ok && gefragt >= 1, "heikle Handlung mit Notfallcode bestätigt: " + mit.Fehler);
        Pruefe.Wahr(await Warten(() => t.Dienst.Zentrale.Konten.Stand.Konten[fritz.KontoId].Rechte == I.Rechte.Durchsage), "Recht gesetzt");
        Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.Notfallcode)), "Warnung: Notfallcode benutzt");
    }

    // ---------------------------------------------------------------- Angriffe auf dem Draht

    [Test]
    public static async Task Zusammenspiel_AngriffeAufZweitfaktorUndRechte()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(61));
        await using var bert = await KernFunker.AktivAsync(halter, DiscordBert, "bert", "ADMIN", Lokal(62));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(63));
        using var testAdmin = await EinrichtenAsync(t, uhr, test);
        using var bertAdmin = await EinrichtenAsync(t, uhr, bert);
        await t.AdminOk("eigentuemer", new JsonObject { ["konto"] = test.KontoId });
        var c = bert.Client;

        // Nonce nur einmal: dieselbe Unterschrift zweimal gesendet.
        uhr.Vor(TimeSpan.FromSeconds(30));
        string? u = null;
        await Gedrosselt(async () =>
        {
            var (unterschrift, antwort) = await c.UnterschreibenAsync(bertAdmin.Schluessel, I.AdminZweck.Anmeldung);
            u = unterschrift;
            return antwort;
        });
        Pruefe.Wahr(u is not null, "Nonce und Unterschrift");
        var erste = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.AdminAnmeldung, new AdminAnmeldungAnfrage { Unterschrift = u!, Code = bertAdmin.Code() }));
        Pruefe.Wahr(erste.Ok, "erste Anmeldung: " + erste.Fehler);
        var zweite = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.AdminAnmeldung, new AdminAnmeldungAnfrage { Unterschrift = u!, Code = bertAdmin.Code(1) }));
        Pruefe.Gleich(Fehlercode.Ungueltig, zweite.Code, "Nonce schon verbraucht");

        // Unterschrift für eine andere Sitzung (etwa mitgeschnitten und umgehängt): KEIN_RECHT, kein Fehlversuch.
        var nonce = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.AdminNonce, LeererBody.Wert));
        var fremdeSitzung = I.Adminnachweis.Unterschreiben(bertAdmin.Schluessel, c.Lage.SitzungsId + 1, c.Geraet,
            Convert.FromBase64String(((AdminNonceAntwort)nonce.Body!).Nonce), I.AdminZweck.Anmeldung);
        var umgehaengt = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.AdminAnmeldung, new AdminAnmeldungAnfrage { Unterschrift = Convert.ToBase64String(fremdeSitzung), Code = "000000" }));
        Pruefe.Gleich(Fehlercode.KeinRecht, umgehaengt.Code, "Unterschrift einer anderen Sitzung");
        Pruefe.Wahr(t.Dienst.Zentrale.Admins.Eintrag(bert.KontoId)!.Fehlversuche.IsEmpty, "ohne gültige Unterschrift kein Fehlversuch");

        // Rechteausweitung und Eigentümer: Bert hat eine Admin-Sitzung mit frischem Code und versucht es trotzdem.
        async Task<Fehlercode?> Kv(FunkClient cl, KontoOperation op, uint konto, object? wert = null) =>
            (await Gedrosselt(() => cl.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage { Op = op, KontoId = konto, Wert = wert is null ? null : Json(wert) }))).Code;
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, await Kv(c, KontoOperation.Stufe, test.KontoId, "FUNKER"), "Eigentümer herabstufen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, await Kv(c, KontoOperation.Sperren, test.KontoId), "Eigentümer sperren");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, await Kv(c, KontoOperation.Loeschen, test.KontoId), "Eigentümer löschen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, await Kv(c, KontoOperation.Stufe, fritz.KontoId, "ADMIN"), "Funker zum ADMIN machen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, await Kv(c, KontoOperation.Rechte, fritz.KontoId, Liste("GERAETE_FREIGEBEN")), "Verwaltungsrecht vergeben");
        Pruefe.Gleich(Fehlercode.KeinRecht, await Kv(fritz.Client, KontoOperation.Stufe, fritz.KontoId, "ADMIN"), "Funker macht sich selbst zum ADMIN");
        Pruefe.Gleich(Fehlercode.KeinRecht, await Kv(fritz.Client, KontoOperation.Rechte, fritz.KontoId, Liste("KONTEN_VERWALTEN")), "Funker gibt sich Rechte");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.AdminNonce, LeererBody.Wert))).Code, "Funker bekommt keine Nonce");
        Pruefe.Gleich(I.Stufe.Admin, t.Dienst.Zentrale.Konten.Stand.Konten[test.KontoId].Stufe, "Test bleibt ADMIN");

        // Raten: fünf falsche Codes in der Admin-Sitzung ergeben 15 min Pause, mit Warnung an jedes ADMIN-Konto.
        await testAdmin.AnmeldenAsync();
        await testAdmin.Client.AdminAbmeldenAsync();
        test.PushesLeeren();
        fritz.PushesLeeren();
        var belegt = new[] { bertAdmin.Code(-1), bertAdmin.Code(), bertAdmin.Code(1) };
        var falsche = Enumerable.Range(0, 20).Select(i => ((100000 + i * 37) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture))
            .Where(x => !belegt.Contains(x)).Take(5).ToList();
        Antwort? letzte = null;
        for (var i = 0; i < 5; i++)
        {
            letzte = await Gedrosselt(() => c.AdminBestaetigenAsync(falsche[i]));
            Pruefe.Gleich(i < 4 ? Fehlercode.ZweitfaktorFalsch : Fehlercode.ZweitfaktorGesperrt, letzte.Code, "Fehlversuch " + (i + 1));
        }
        Pruefe.Wahr(letzte!.Fehler!.Details is { } d && d.TryGetProperty("bis", out _), "Pause mit details.bis");
        Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.ZweitfaktorGesperrt)), "Warnung an Test, auch ohne Admin-Sitzung");
        Pruefe.Falsch(HatMeldung(fritz, MeldungsArten.ZweitfaktorGesperrt), "nicht an einen Funker");
        uhr.Vor(TimeSpan.FromSeconds(30));
        Pruefe.Gleich(Fehlercode.ZweitfaktorGesperrt, (await Gedrosselt(() => c.AdminBestaetigenAsync(bertAdmin.Code()))).Code, "in der Pause auch mit dem richtigen Code nicht");
        var admins = await t.AdminOk("admins");
        var bertJson = admins.Daten!.AsArray().Single(e => e!["kontoId"]!.GetValue<uint>() == bert.KontoId)!;
        Pruefe.Wahr(bertJson["fehlversuche"]!.GetValue<int>() == 5 && bertJson["pauseBis"] is not null, "Konsole zeigt Fehlversuche und Pause");
        Pruefe.Falsch(admins.Daten.ToJsonString().Contains(I.Base32.Kodieren(bertAdmin.Geheimnis)[..8], StringComparison.Ordinal), "admins zeigt kein Geheimnis");
        var journal = await t.AdminOk("journal", new JsonObject { ["konto"] = bert.KontoId, ["anzahl"] = 100 });
        Pruefe.Wahr(journal.Text.Contains("pausiert", StringComparison.Ordinal), "Journal: Pause vermerkt");
    }

    // ---------------------------------------------------------------- Discord-Name: eindeutig, nur vom Admin (A53)

    [Test]
    public static async Task Zusammenspiel_DiscordNameEindeutigUndNurVomAdmin()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(91));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(92));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(93));
        using var admin = await EinrichtenAsync(t, uhr, test);
        var z = t.Dienst.Zentrale;
        var c = test.Client;

        // Konsole: funk-admin discordname. "Wer ist da" zeigt den Namen bei allen.
        await t.AdminOk("discordname", new JsonObject { ["konto"] = otto.KontoId, ["name"] = "Sarevoc/Otto" });
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Konto is { DiscordName: "Sarevoc/Otto" }), "Otto sieht seinen Discord-Namen");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Praesenz.TryGetValue(otto.KontoId, out var e) && e.DiscordName == "Sarevoc/Otto"), "Praesenz trägt ihn für alle");
        var doppelt = await t.Admin("discordname", new JsonObject { ["konto"] = fritz.KontoId, ["name"] = "ＳＡＲＥＶＯＣ/otto" });
        Pruefe.Wahr(!doppelt.Ok && doppelt.Text.Contains("Konto " + otto.KontoId, StringComparison.Ordinal) && doppelt.Text.Contains("Nichts geändert", StringComparison.Ordinal),
            "Konsole: Breitschrift abgewiesen, mit Konto im Klartext: " + doppelt.Text);

        // Angriffe über den Draht: ProfilSetzen mit fremdem oder neuem Discord-Namen, der Funker an der Verwaltung.
        KontoVerwaltungAnfrage DiscordKv(uint konto, string? name) => new() { Op = KontoOperation.Discordname, KontoId = konto, Wert = name is null ? null : Json(name) };
        var fremd = await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.ProfilSetzen, new ProfilSetzenAnfrage { ScNamen = ["Fritz"], DiscordName = "Sarevoc/Otto" }));
        Pruefe.Gleich(Fehlercode.KeinRecht, fremd.Code, "ProfilSetzen mit fremdem Discord-Namen");
        var neu = await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.ProfilSetzen, new ProfilSetzenAnfrage { DiscordName = "Fritz/Neu" }));
        Pruefe.Gleich(Fehlercode.KeinRecht, neu.Code, "ProfilSetzen mit eigenem neuem Discord-Namen");
        Pruefe.Wahr(z.Konten.Stand.Konten[fritz.KontoId] is { DiscordName: null } f0 && f0.ScNamen.IsEmpty, "nichts gesetzt, auch die SC-Namen nicht");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, DiscordKv(fritz.KontoId, "Fritz/Neu")))).Code,
            "Funker: KontoVerwaltung DISCORDNAME");
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, DiscordKv(fritz.KontoId, "Fritz/DC")))).Code,
            "Admin ohne Admin-Sitzung");

        // In der Admin-Sitzung: eindeutig, sonst NAME_VERGEBEN mit dem Namen.
        await admin.AnmeldenAsync();
        var vergeben = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, DiscordKv(fritz.KontoId, "sarevoc/OTTO")));
        Pruefe.Wahr(vergeben.Code == Fehlercode.NameVergeben && vergeben.Fehler!.Details?.GetProperty("name").GetString() == "sarevoc/OTTO", "NAME_VERGEBEN mit details.name");
        Pruefe.Wahr((await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, DiscordKv(fritz.KontoId, "Fritz/DC")))).Ok, "Admin setzt den Namen");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Konto is { DiscordName: "Fritz/DC" }), "Fritz bekommt ihn per Mitgliedschaften");
        var gleich = await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.ProfilSetzen, new ProfilSetzenAnfrage { ScNamen = ["Fritz"], DiscordName = "Fritz/DC" }));
        Pruefe.Wahr(gleich.Ok && gleich.Body is ProfilSetzenAntwort { DiscordName: "Fritz/DC", ScNamen: ["Fritz"] }, "derselbe Name stört ProfilSetzen nicht: " + gleich.Fehler);
        var journal = await t.AdminOk("journal", new JsonObject { ["konto"] = fritz.KontoId, ["anzahl"] = 20 });
        Pruefe.Wahr(journal.Text.Contains("DISCORDNAME", StringComparison.Ordinal) && journal.Text.Contains("Fritz/DC", StringComparison.Ordinal), "Journal: " + journal.Text);

        // Freigabe: Ein Antrag mit dem Discord-Namen eines Mitglieds wird angenommen, und Konsole und Freigabeansicht warnen vorher.
        // Seit 0.7.0 (ein PC, ein Konto, Sarevoc 04.10.2026) geht die Freigabe trotzdem; nur bekommt das neue Konto den belegten Namen
        // nicht, also gibt sich niemand als Fritz aus (A53). Bis 0.6.x scheiterte sie mit NAME_VERGEBEN.
        var (taeuscher, wartet) = await AntragAsync(t, "Falscher", "fritz/dc", 94);
        await using var taeuscherHalter = taeuscher;
        var antraege = await t.AdminOk("antraege");
        Pruefe.Wahr(antraege.Text.Contains("DISCORD-NAME VERGEBEN", StringComparison.Ordinal), "antraege warnt vorher: " + antraege.Text);
        var liste = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { NurWartend = true }));
        Pruefe.Wahr(liste.Body is GeraeteListeAntwort l && l.Geraete.Single(g => g.Id == wartet.GeraetId).DiscordNameVergeben == true, "Freigabeansicht warnt vorher");
        var frei = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.GeraetEntscheiden, new GeraetEntscheidenAnfrage { GeraetId = wartet.GeraetId, Entscheidung = GeraetEntscheidung.Freigeben }));
        Pruefe.Wahr(frei.Ok, "freigegeben: " + frei.Fehler);
        Pruefe.Gleich(null, z.Konten.Stand.Konten[wartet.KontoId].DiscordName, "ohne den belegten Discord-Namen");
        Pruefe.Gleich("Fritz/DC", z.Konten.Stand.Konten[fritz.KontoId].DiscordName, "Fritz behält ihn");
    }

    // ---------------------------------------------------------------- Wer ist da: Mitgliedskarte und Zeichen

    [Test]
    public static async Task Zusammenspiel_MitgliedskarteUndZeichen()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr, karten: new KartenKonfiguration { RollenFuerAlle = false });
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(71));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(72));
        await using var gina = await KernFunker.AktivAsync(halter, DiscordGina, "gina", "FUNKER", Lokal(73));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "RAIDLEITER", Lokal(74));
        using var admin = await EinrichtenAsync(t, uhr, test);
        await admin.AnmeldenAsync();
        var c = test.Client;
        // Seit 0.3.0 teilt RAIDLEITER (Führung) nicht mehr selbst ein: Otto leitet im Auftrag, erteilt an der Konsole (6.12).
        var bis = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow.AddHours(3), Auftragszeit.Zone).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        await t.AdminOk("auftrag", new JsonObject { ["konto"] = otto.KontoId, ["bis"] = bis, ["grund"] = "Probeabend" });
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Konto?.Beauftragung is { Aktiv: true, Grund: "Probeabend" }), "Otto sieht seinen Auftrag");
        await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Rollenkatalog, new RollenkatalogAnfrage { Op = RegelOperation.Setzen, Eintraege = [new Rollenkatalogeintrag { Id = 1, Name = "Pilot" }] }));
        Pruefe.Wahr((await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Aendern, KontoId = fritz.KontoId, Rollen = [1] }))).Ok, "Rolle Pilot für Fritz");
        // Untergilden entfallen seit 0.11.8 (Sarevoc: "überall raus"): 0x001D bleibt als Hülle, LISTE ist leer, SETZEN und LOESCHEN sind
        // UNGUELTIG mit Hinweis, auch in der Admin-Sitzung; eine alte Verwaltung, die noch eine Untergilde mitschickt, ändert nichts.
        foreach (var (was, op) in new[] { ("SETZEN", UntergildeOperation.Setzen), ("LOESCHEN", UntergildeOperation.Loeschen) })
        {
            var ug = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Untergilde,
                new UntergildeAnfrage { Op = op, Id = op == UntergildeOperation.Loeschen ? (ushort)1 : null, Name = "Beispielstaffel", Kuerzel = "BS", Konten = [fritz.KontoId, gina.KontoId] }));
            Pruefe.Gleich(Fehlercode.Ungueltig, ug.Code, "Untergilde " + was + " in der Admin-Sitzung");
            Pruefe.Gleich("Untergilden gibt es seit 0.11.8 nicht mehr", ug.Fehler?.Text, "Untergilde " + was + ": Hinweis");
        }
        var ugListe = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Untergilde, new UntergildeAnfrage { Op = UntergildeOperation.Liste }));
        Pruefe.Wahr(ugListe.Ok && ugListe.Body is UntergildeAntwort { Untergilden: [] }, "LISTE ist leer: " + ugListe.Fehler);
        Pruefe.Wahr((await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied,
            new MitgliedAnfrage { Op = MitgliedOperation.Aendern, KontoId = fritz.KontoId, Untergilde = 1 }))).Ok, "alte Verwaltung mit Untergilde: übergangen");
        // Den Discord-Namen setzt seit A53 nur ein Admin (hier in der Admin-Sitzung), die SC-Namen Fritz selbst.
        var discord = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage { Op = KontoOperation.Discordname, KontoId = fritz.KontoId, Wert = Json("fritz_dc") }));
        Pruefe.Wahr(discord.Ok, "Discord-Name von Fritz durch den Admin: " + discord.Fehler);
        var profil = await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.ProfilSetzen, new ProfilSetzenAnfrage { ScNamen = ["Fritz", "Fritz Alt"] }));
        Pruefe.Wahr(profil.Ok, "Profil von Fritz: " + profil.Fehler);
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Konto is { Rollen: [1], ScNamen: ["Fritz", "Fritz Alt"], Untergilde: null }), "Fritz sieht seine Angaben, ohne Untergilde");
        var lieb = await Gedrosselt(() => gina.Client.AnfrageAsync(Nachrichtentyp.Lieblingsrollen, new LieblingsrollenAnfrage { Rollen = [1], ErsteWahl = 1 }));
        Pruefe.Wahr(lieb.Ok && lieb.Body is LieblingsrollenAntwort { Rollen: [1], ErsteWahl: 1 }, "Gina wünscht Pilot");

        async Task<MitgliedskarteAntwort> Karte(KernFunker wer, uint konto)
        {
            var a = await Gedrosselt(() => wer.Client.AnfrageAsync(Nachrichtentyp.Mitgliedskarte, new MitgliedskarteAnfrage { KontoId = konto }));
            Pruefe.Wahr(a.Ok && a.Body is MitgliedskarteAntwort, "Mitgliedskarte: " + a.Fehler);
            return (MitgliedskarteAntwort)a.Body!;
        }

        var ginaSiehtFritz = await Karte(gina, fritz.KontoId);
        // Antwort 3 (Sarevoc am 30.09.2026): Den Discord-Namen sieht jeder, damit man weiß, wer spricht; die Zeichen, solange Fritz seine
        // Karte freigibt (ab Werk an). Eine Untergilde schickt der Server seit 0.11.8 nie mehr.
        Pruefe.Wahr(ginaSiehtFritz is { ScName: "Fritz", ScNamen: null, DiscordName: "fritz_dc", Rollen: null, Online: true, Freigegeben: true, Untergilde: null },
            "Funker sieht Hauptname und Discord-Namen, keine weiteren Namen, keine Rollen (rollenFuerAlle aus), keine Untergilde");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.Praesenz.TryGetValue(fritz.KontoId, out var e) && e.DiscordName == "fritz_dc" && e.Untergilde is null),
            "Praesenz mit Freigabe: ohne Untergilde");
        var ottoSiehtFritz = await Karte(otto, fritz.KontoId);
        Pruefe.Wahr(ottoSiehtFritz is { ScNamen: ["Fritz", "Fritz Alt"], DiscordName: "fritz_dc", Rollen: [1], Untergilde: null }, "Einsatzleitung (ZUWEISEN) sieht alles, ohne Untergilde");
        var testSiehtFritz = await Karte(test, fritz.KontoId);
        Pruefe.Wahr(testSiehtFritz is { DiscordName: "fritz_dc", Rollen: [1] }, "ADMIN sieht alles");
        Pruefe.Wahr(await Karte(fritz, fritz.KontoId) is { DiscordName: "fritz_dc", Rollen: [1] }, "sich selbst sieht jeder ganz");
        Pruefe.Wahr(await Karte(otto, gina.KontoId) is { Lieblingsrollen: [1], ErsteWahl: 1 }, "Wünsche für die Einsatzleitung");

        // Fritz nimmt die Freigabe zurück: Name und Discord-Name bleiben, Zeichen gehen; die Einsatzleitung sieht weiter alles.
        var zu = await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.ProfilSetzen, new ProfilSetzenAnfrage { KarteFreigeben = false }));
        Pruefe.Wahr(zu.Ok && zu.Body is ProfilSetzenAntwort { KarteFreigeben: false }, "Karte nicht freigegeben: " + zu.Fehler);
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Konto is { KarteFreigeben: false }), "KontoStand trägt die Freigabe");
        var ohneFreigabe = await Karte(gina, fritz.KontoId);
        Pruefe.Wahr(ohneFreigabe is { ScName: "Fritz", DiscordName: "fritz_dc", Untergilde: null, Rollen: null, Lieblingsrollen: null, Freigegeben: false } && ohneFreigabe.Zeichen.Count == 0,
            "ohne Freigabe nur Namen");
        Pruefe.Wahr(await Karte(otto, fritz.KontoId) is { Rollen: [1], Untergilde: null }, "Einsatzleitung sieht trotzdem alles");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.Praesenz.TryGetValue(fritz.KontoId, out var e) && e.DiscordName == "fritz_dc" && e.Untergilde is null),
            "Praesenz: Discord-Name für alle");
        Pruefe.Wahr((await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.ProfilSetzen, new ProfilSetzenAnfrage { KarteFreigeben = true }))).Ok, "wieder freigegeben");

        // Zeichen (A49): Lorbeer für den eingerichteten Admin, Führung für RAIDLEITER, Stern für einen laufenden Auftrag;
        // abschaltbar durch den Inhaber.
        Pruefe.Wahr((await Karte(gina, test.KontoId)).Zeichen is ["ADMIN"], "ADMIN-Zeichen");
        Pruefe.Wahr((await Karte(gina, otto.KontoId)).Zeichen is ["FUEHRUNG", "EINSATZLEITUNG"], "FUEHRUNG- und EINSATZLEITUNG-Zeichen");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.Praesenz.TryGetValue(test.KontoId, out var e) && e.Zeichen is ["ADMIN"]), "Praesenz trägt das Zeichen");
        var aus = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.ProfilSetzen, new ProfilSetzenAnfrage { ZeichenZeigen = false }));
        Pruefe.Wahr(aus.Ok && aus.Body is ProfilSetzenAntwort { ZeichenZeigen: false }, "Zeichen ausgeschaltet");
        Pruefe.Wahr((await Karte(gina, test.KontoId)).Zeichen.Count == 0, "andere sehen es nicht mehr");
        Pruefe.Wahr((await Karte(test, test.KontoId)).Zeichen is ["ADMIN"], "er selbst schon");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.Praesenz.TryGetValue(test.KontoId, out var e) && e.Zeichen is null), "Praesenz ohne Zeichen");

        // Untergilde LISTE (seit 0.11.8 nur die Hülle): Ein Funker bekommt eine leere Liste, getrennt wird er nicht; SETZEN ist UNGUELTIG.
        var liste = await Gedrosselt(() => gina.Client.AnfrageAsync(Nachrichtentyp.Untergilde, new UntergildeAnfrage { Op = UntergildeOperation.Liste }));
        Pruefe.Wahr(liste.Ok && liste.Body is UntergildeAntwort { Untergilden: [] }, "leere Liste für Gina: " + liste.Fehler);
        var ginaSetzt = await Gedrosselt(() => gina.Client.AnfrageAsync(Nachrichtentyp.Untergilde, new UntergildeAnfrage { Op = UntergildeOperation.Setzen, Name = "Heimlich", Kuerzel = "HM" }));
        Pruefe.Gleich(Fehlercode.Ungueltig, ginaSetzt.Code, "Funker: SETZEN UNGUELTIG");
        Pruefe.Wahr((await Karte(gina, gina.KontoId)).KontoId == gina.KontoId, "Gina bleibt verbunden");

        // Wer nicht in der Praesenz steht (getrennt, ohne Platz), dessen Karte bekommt ein Funker nicht: UNBEKANNT wie ein fremdes
        // Konto. So lässt sich über die Karte nicht die ganze Mitgliederliste abfragen. Die Einsatzleitung sieht sie weiter.
        var bert = await KernFunker.AktivAsync(halter, DiscordBert, "bert", "FUNKER", Lokal(75));
        var bertKonto = bert.KontoId;
        Pruefe.Wahr((await Karte(gina, bertKonto)).Online, "online: Gina bekommt die Karte");
        await bert.DisposeAsync();
        Pruefe.Wahr(await Warten(() => !gina.Client.Lage.Praesenz.TryGetValue(bertKonto, out var e) || !e.Online), "Bert getrennt");
        var weg = await Gedrosselt(() => gina.Client.AnfrageAsync(Nachrichtentyp.Mitgliedskarte, new MitgliedskarteAnfrage { KontoId = bertKonto }));
        Pruefe.Gleich(Fehlercode.Unbekannt, weg.Code, "Funker: Karte eines Getrennten");
        Pruefe.Wahr(await Karte(otto, bertKonto) is { Online: false }, "Einsatzleitung sieht sie weiter");
    }

    // ---------------------------------------------------------------- Abzeichen (0.11.7)

    [Test]
    public static async Task Zusammenspiel_Abzeichen()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(81));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(82));
        await using var gina = await KernFunker.AktivAsync(halter, DiscordGina, "gina", "FUNKER", Lokal(83));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "RAIDLEITER", Lokal(84));
        using var admin = await EinrichtenAsync(t, uhr, test);
        var c = test.Client;
        var z = t.Dienst.Zentrale;

        AbzeichenAnfrage Vergeben(string[] raenge, params string[] taetigkeiten) => new() { KontoId = fritz.KontoId, Raenge = raenge, Taetigkeiten = taetigkeiten };
        Task<Antwort> Senden(KernFunker f, object body, Nachrichtentyp typ = Nachrichtentyp.Abzeichen) => Gedrosselt(() => f.Client.AnfrageAsync(typ, body));
        bool Sieht(KernFunker wer, KernFunker wen, Func<PraesenzEintrag, bool> wie) =>
            wer.Client.Lage.Praesenz.TryGetValue(wen.KontoId, out var e) && wie(e);

        // Vergeben nur in der Admin-Sitzung, nie durch die Führung oder einen Funker.
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Senden(test, Vergeben(["MARINE_04"]))).Code, "ADMIN ohne Admin-Sitzung");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Senden(otto, Vergeben(["MARINE_04"]))).Code, "Raidleiter");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Senden(fritz, Vergeben(["MARINE_10"]))).Code, "Funker, auch nicht sich selbst");
        Pruefe.Wahr(z.Mitglieder.Stand.DatenVon(fritz.KontoId).Raenge.IsEmpty, "nichts vergeben");

        await admin.AnmeldenAsync();
        var a = await Senden(test, Vergeben(["NAVY_02", "MARINE_04"], "T_GUNNER_M", "T_JAEGER"));
        Pruefe.Wahr(a.Ok && a.Body is AbzeichenAntwort { Raenge: ["MARINE_04", "NAVY_02"], Taetigkeiten: ["T_JAEGER", "T_GUNNER_M"] }, "vergeben, in Katalogfolge: " + a.Fehler);
        Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.MitgliedGeaendert)), "MITGLIED_GEAENDERT an die Admin-Sitzung");

        // Prüfregeln auf dem Draht: UNGUELTIG mit Grund, ohne Trennung.
        foreach (var (was, body, grund) in new (string, AbzeichenAnfrage, string)[]
        {
            ("unbekannte Kennung", Vergeben(["MARINE_99"]), "unbekanntes Abzeichen MARINE_99"),
            ("zwei Ränge einer Laufbahn", Vergeben(["MARINE_01", "MARINE_02"]), "zwei Ränge in Marine"),
            ("Rang als Tätigkeit", Vergeben([], "NAVY_01"), "NAVY_01 ist keine Tätigkeit"),
        })
        {
            var f = await Senden(test, body);
            Pruefe.Gleich(Fehlercode.Ungueltig, f.Code, was);
            Pruefe.Gleich(grund, f.Fehler?.Text, was + ": Text");
        }
        Pruefe.Gleich(Fehlercode.Unbekannt, (await Senden(test, Vergeben(["MARINE_01"]) with { KontoId = 4711 })).Code, "unbekanntes Konto");

        // Journal ABZEICHEN mit Titeln.
        var journal = await Senden(test, new JournalAnfrage(), Nachrichtentyp.Journal);
        Pruefe.Wahr(journal.Body is JournalAntwort { Eintraege: { } je } && je.Any(e => e.Art == "ABZEICHEN" && e.KontoId == fritz.KontoId
            && e.Text.Contains("Marine: Sergeant, Navy: Maat, Jägerpilot, Gunner M", StringComparison.Ordinal)), "Journal ABZEICHEN mit Titeln");

        // Mitgliedschaften: der eigene Stand voll; Praesenz: Sichtbares für alle; Mitglied LISTE: alles für den Admin.
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Konto is { Abzeichen: ["MARINE_04", "NAVY_02", "T_JAEGER", "T_GUNNER_M"], AbzeichenVorn: null, AbzeichenAus.Count: 0 }),
            "KontoStand trägt die Abzeichen");
        Pruefe.Wahr(await Warten(() => Sieht(gina, fritz, e => e.Abzeichen is ["MARINE_04", "NAVY_02", "T_JAEGER", "T_GUNNER_M"] && e.AbzeichenVorn is null)),
            "Praesenz zeigt die Abzeichen");

        // Fritz wählt vorn und blendet aus: andere sehen nur Sichtbares, er selbst und der Admin alles.
        var p = await Senden(fritz, new ProfilSetzenAnfrage { AbzeichenVorn = "NAVY_02", AbzeichenAus = ["T_JAEGER"] }, Nachrichtentyp.ProfilSetzen);
        Pruefe.Wahr(p.Ok && p.Body is ProfilSetzenAntwort { AbzeichenVorn: "NAVY_02", AbzeichenAus: ["T_JAEGER"] }, "vorn und aus gesetzt: " + p.Fehler);
        Pruefe.Wahr(await Warten(() => Sieht(gina, fritz, e => e.Abzeichen is ["MARINE_04", "NAVY_02", "T_GUNNER_M"] && e.AbzeichenVorn == "NAVY_02")),
            "Praesenz: ohne Ausgeblendetes, vorn NAVY_02");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Konto is { Abzeichen.Count: 4, AbzeichenVorn: "NAVY_02", AbzeichenAus: ["T_JAEGER"] }), "KontoStand: alles, dazu die Ansicht");
        var liste = await Senden(test, new MitgliedAnfrage { Op = MitgliedOperation.Liste }, Nachrichtentyp.Mitglied);
        Pruefe.Wahr(liste.Body is MitgliedAntwort { Mitglieder: { } ml } && ml.Single(m => m.KontoId == fritz.KontoId) is { Raenge: ["MARINE_04", "NAVY_02"], Taetigkeiten: ["T_JAEGER", "T_GUNNER_M"] },
            "Mitglied LISTE: der Admin sieht alles, auch Ausgeblendetes");
        var karte = await Senden(gina, new MitgliedskarteAnfrage { KontoId = fritz.KontoId }, Nachrichtentyp.Mitgliedskarte);
        Pruefe.Wahr(karte.Body is MitgliedskarteAntwort { Abzeichen: ["MARINE_04", "NAVY_02", "T_GUNNER_M"] }, "Mitgliedskarte: nur Sichtbares");
        var selbst = await Senden(fritz, new MitgliedskarteAnfrage { KontoId = fritz.KontoId }, Nachrichtentyp.Mitgliedskarte);
        Pruefe.Wahr(selbst.Body is MitgliedskarteAntwort { Abzeichen.Count: 4 }, "die eigene Karte zeigt alles");

        // Vorn nur, wenn besessen und sichtbar.
        Pruefe.Wahr((await Senden(fritz, new ProfilSetzenAnfrage { AbzeichenAus = ["NAVY_02"] }, Nachrichtentyp.ProfilSetzen)).Ok, "NAVY_02 ausgeblendet");
        Pruefe.Wahr(await Warten(() => Sieht(gina, fritz, e => e.AbzeichenVorn is null && e.Abzeichen is ["MARINE_04", "T_JAEGER", "T_GUNNER_M"])), "ausgeblendet: nicht vorn");
        Pruefe.Wahr((await Senden(fritz, new ProfilSetzenAnfrage { AbzeichenVorn = "T_SANI", AbzeichenAus = [] }, Nachrichtentyp.ProfilSetzen)).Ok, "vorn T_SANI, das er nicht hat");
        Pruefe.Wahr(await Warten(() => Sieht(gina, fritz, e => e.AbzeichenVorn is null && e.Abzeichen!.Count == 4)), "nicht besessen: nicht vorn");
        Pruefe.Wahr((await Senden(fritz, new ProfilSetzenAnfrage { AbzeichenVorn = "T_GUNNER_M" }, Nachrichtentyp.ProfilSetzen)).Ok, "vorn T_GUNNER_M");
        Pruefe.Wahr(await Warten(() => Sieht(gina, fritz, e => e.AbzeichenVorn == "T_GUNNER_M")), "besessen und sichtbar: vorn");

        // Unbekanntes und FUEHRUNG lehnt der Server ab, ohne etwas zu ändern (auch keine Namen).
        foreach (var (was, anfrage) in new (string, ProfilSetzenAnfrage)[]
        {
            ("vorn unbekannt", new ProfilSetzenAnfrage { AbzeichenVorn = "XYZ", ScNamen = ["Heimlich"] }),
            ("vorn FUEHRUNG", new ProfilSetzenAnfrage { AbzeichenVorn = "FUEHRUNG" }),
            ("aus unbekannt", new ProfilSetzenAnfrage { AbzeichenAus = ["T_JAEGER", "XYZ"] }),
        })
        {
            Pruefe.Gleich(Fehlercode.Ungueltig, (await Senden(fritz, anfrage, Nachrichtentyp.ProfilSetzen)).Code, was);
        }
        Pruefe.Wahr(z.Mitglieder.Stand.DatenVon(fritz.KontoId) is { AbzeichenVorn: "T_GUNNER_M" } df && df.AbzeichenAus.IsEmpty
            && z.Konten.Stand.Konten[fritz.KontoId].ScNamen.IsEmpty, "nichts geändert");

        // Auch ADMIN lässt sich ausblenden; das ändert keine Rechte.
        Pruefe.Wahr(await Warten(() => Sieht(gina, test, e => e.Zeichen is ["ADMIN"])), "Kranz sichtbar");
        Pruefe.Wahr((await Senden(test, new ProfilSetzenAnfrage { AbzeichenVorn = "ADMIN", AbzeichenAus = ["ADMIN"] }, Nachrichtentyp.ProfilSetzen)).Ok, "ADMIN ausgeblendet");
        Pruefe.Wahr(await Warten(() => Sieht(gina, test, e => e.Zeichen is null && e.AbzeichenVorn is null)), "Praesenz ohne ADMIN, nicht vorn");
        Pruefe.Wahr((await Senden(gina, new MitgliedskarteAnfrage { KontoId = test.KontoId }, Nachrichtentyp.Mitgliedskarte)).Body is MitgliedskarteAntwort { Zeichen.Count: 0 },
            "Mitgliedskarte ohne ADMIN");
        Pruefe.Wahr((await Senden(test, new MitgliedskarteAnfrage { KontoId = test.KontoId }, Nachrichtentyp.Mitgliedskarte)).Body is MitgliedskarteAntwort { Zeichen: ["ADMIN"] },
            "er selbst sieht es");
        Pruefe.Wahr((await Senden(test, new MitgliedAnfrage { Op = MitgliedOperation.Liste }, Nachrichtentyp.Mitglied)).Ok, "Verwaltung geht weiter");
        Pruefe.Wahr((await Senden(test, new ProfilSetzenAnfrage { AbzeichenAus = [] }, Nachrichtentyp.ProfilSetzen)).Ok, "ADMIN wieder gezeigt");
        Pruefe.Wahr(await Warten(() => Sieht(gina, test, e => e.Zeichen is ["ADMIN"] && e.AbzeichenVorn == "ADMIN")), "Kranz sichtbar und vorn");

        // "Mein Zeichen anderen zeigen" aus: alles aus, auch die Abzeichen.
        Pruefe.Wahr((await Senden(fritz, new ProfilSetzenAnfrage { ZeichenZeigen = false }, Nachrichtentyp.ProfilSetzen)).Ok, "Zeichen aus");
        Pruefe.Wahr(await Warten(() => Sieht(gina, fritz, e => e.Abzeichen is null && e.AbzeichenVorn is null && e.Zeichen is null)), "Praesenz ohne Abzeichen");
        Pruefe.Wahr(await Warten(() => Sieht(otto, fritz, e => e.Abzeichen is null)), "auch Otto sieht keine");
        Pruefe.Wahr((await Senden(gina, new MitgliedskarteAnfrage { KontoId = fritz.KontoId }, Nachrichtentyp.Mitgliedskarte)).Body is MitgliedskarteAntwort { Abzeichen: null },
            "Mitgliedskarte ohne Abzeichen");
        Pruefe.Wahr(fritz.Client.Lage.Konto is { Abzeichen.Count: 4 }, "besitzen tut er sie weiter");
    }

    // ---------------------------------------------------------------- Meldesystem: Haken "darf Meldungen senden" (0.12.0)

    /// <summary>
    /// M15-M18: Den Haken setzt nur ein Admin in der Admin-Sitzung über Mitglied AENDERN, mit Journal; er steht in Mitglied LISTE und im
    /// eigenen KontoStand und bleibt, bis ein Admin ihn wegnimmt. Was der Haken beim Senden bewirkt, prüft <see cref="MeldesystemServerTests"/>.
    /// </summary>
    [Test]
    public static async Task Zusammenspiel_Meldungshaken()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(88));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "RAIDLEITER", Lokal(89));
        using var admin = await EinrichtenAsync(t, uhr, test);
        var c = test.Client;
        var z = t.Dienst.Zentrale;
        var haken = new MitgliedAnfrage { Op = MitgliedOperation.Aendern, KontoId = otto.KontoId, MeldungenSenden = true };
        var befehl = new MeldungSendenAnfrage { Art = Meldungsvorgang.Befehl, Motiv = "sammeln" };

        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, haken))).Code, "ADMIN ohne Admin-Sitzung");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.Mitglied, haken))).Code, "die Führung ernennt sich nicht selbst");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.MeldungSenden, befehl))).Code, "ohne Haken sendet die Führung nicht");

        await admin.AnmeldenAsync();
        var a = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, haken));
        Pruefe.Wahr(a.Ok, "Haken gesetzt: " + a.Fehler?.Text);
        Pruefe.Wahr(z.Mitglieder.Stand.DatenVon(otto.KontoId).MeldungenSenden, "im Mitgliederbuch");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Konto?.MeldungenSenden == true), "Otto bekommt den Haken im KontoStand");
        var liste = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Liste }));
        Pruefe.Wahr(liste.Body is MitgliedAntwort { Mitglieder: { } ml } && ml.Single(m => m.KontoId == otto.KontoId).MeldungenSenden == true
            && ml.Single(m => m.KontoId == test.KontoId).MeldungenSenden is null, "Mitglied LISTE: Haken nur bei Otto");
        Pruefe.Wahr((await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.MeldungSenden, befehl))).Ok, "mit Haken sendet die Führung");

        // Nur für Konten; eine Vormerkung hat keinen Haken.
        var vm = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Vormerken, ScName = "Hexa" }));
        var vormerkung = ((MitgliedAntwort)vm.Body!).Ids![0];
        var vh = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Aendern, VormerkungId = vormerkung, MeldungenSenden = true }));
        Pruefe.Gleich(Fehlercode.Ungueltig, vh.Code, "Vormerkung mit Haken");

        // Weg nimmt ihn nur ein Admin (M18); beides steht im Journal.
        var weg = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Mitglied, haken with { MeldungenSenden = false }));
        Pruefe.Wahr(weg.Ok && !z.Mitglieder.Stand.DatenVon(otto.KontoId).MeldungenSenden, "Haken weggenommen");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Konto is { MeldungenSenden: null }), "KontoStand ohne Haken");
        await Task.Delay(2100);
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.MeldungSenden, befehl))).Code, "danach sendet Otto nicht mehr");
        var journal = await Gedrosselt(() => c.AnfrageAsync(Nachrichtentyp.Journal, new JournalAnfrage()));
        var eintraege = ((JournalAntwort)journal.Body!).Eintraege.Where(e => e.Art == "MELDUNGEN" && e.KontoId == otto.KontoId).ToList();
        Pruefe.Wahr(eintraege.Any(e => e.Text.Contains("erlaubt", StringComparison.Ordinal)) && eintraege.Any(e => e.Text.Contains("weggenommen", StringComparison.Ordinal)),
            "Journal MELDUNGEN: erlaubt und weggenommen");
        Pruefe.Falsch(((JournalAntwort)journal.Body!).Eintraege.Any(e => e.Art == "MITGLIED" && e.KontoId == otto.KontoId), "nur der Haken: kein Eintrag MITGLIED");
    }

    // ---------------------------------------------------------------- Auftrag für einen Abend: nur das Datenmodell (Stufe 2)

    [Test]
    public static void Beauftragung_DatenmodellFuerStufe2()
    {
        var b = new Funk.Server.Lage.Beauftragung(7, Von: 1_000, Bis: 2_000, Funk.Server.Lage.Recht.Zuweisen | Funk.Server.Lage.Recht.GlobalSenden, Durch: 1, Seit: 900)
        {
            Grund = "Raid Freitag",
        };
        Pruefe.Falsch(b.GiltZu(999), "vor dem Beginn");
        Pruefe.Wahr(b.GiltZu(1_000) && b.GiltZu(1_999), "im Zeitraum");
        Pruefe.Falsch(b.GiltZu(2_000), "läuft von selbst ab");
        Pruefe.Gleich("EINSATZLEITUNG", Funk.Server.Kern.Zentrale.Zeichen.Einsatzleitung, "Zeichen für einen laufenden Auftrag");
        // Seit 0.3.0 trägt 0x0017 die Nachricht Beauftragung (6.12); Verhalten in RechteBeauftragungTests und RaidleitungServerTests.
        Pruefe.Gleich(Nachrichtentyp.Beauftragung, Katalog.Finden(0x0017)?.Typ, "0x0017 ist die Nachricht Beauftragung");
    }

    // ---------------------------------------------------------------- Konsole und Neustart

    [Test]
    public static async Task Zusammenspiel_KonsoleUndNeustart()
    {
        var uhr = new Testuhr();
        var verzeichnis = Path.Combine(Testserver.Wurzel(), Guid.NewGuid().ToString("N")[..16]);
        var halter = new ServerHalter();
        var t1 = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr);
        Testserver? t2 = null;
        KernFunker? test = null;
        KernFunker? bert = null;
        Admin? admin = null;
        try
        {
            halter.Server = t1;
            test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(81));
            bert = await KernFunker.AktivAsync(halter, DiscordBert, "bert", "ADMIN", Lokal(82));
            admin = await EinrichtenAsync(t1, uhr, test);
            Pruefe.Falsch((await t1.Admin("eigentuemer", new JsonObject { ["konto"] = 9999 })).Ok, "unbekanntes Konto");
            await t1.AdminOk("eigentuemer", new JsonObject { ["konto"] = test.KontoId });
            Pruefe.Wahr(await Warten(() => test.Client.Lage.Konto is { Eigentuemer: true }), "Mitgliedschaften: Eigentümer");
            var konten = await t1.AdminOk("konten");
            Pruefe.Wahr(konten.Text.Contains("EIGENTÜMER", StringComparison.Ordinal), "konten nennt den Eigentümer");

            // Neuer Zweitfaktor an der Konsole, zum Abtippen: beendet offene Admin-Sitzungen, alter Code gilt nicht mehr.
            await admin.AnmeldenAsync();
            var neu = await t1.AdminOk("admin-einrichtung-neu", new JsonObject { ["konto"] = test.KontoId, ["anzeigen"] = true });
            Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.AdminSitzung, "BEENDET")), "Admin-Sitzung endet mit dem neuen Zweitfaktor");
            var uri = neu.Daten!["uri"]!.GetValue<string>();
            Pruefe.Wahr(I.Base32.TryDekodieren(neu.Daten!["geheimnis"]!.GetValue<string>(), out var g2) && uri.Contains("secret=" + I.Base32.Kodieren(g2), StringComparison.Ordinal),
                "Geheimnis in Gruppen und als otpauth-URI");
            Pruefe.Falsch(t1.Logbuch.Any(z => z.Contains(I.Base32.Kodieren(g2)[..8], StringComparison.Ordinal)), "nicht im Logbuch");
            Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => test.Client.AdminAnmeldenAsync(admin.Schluessel, admin.Code(1)))).Code, "vor dem ersten Code keine Anmeldung");
            var erster = await t1.AdminOk("zweitfaktor-bestaetigen", new JsonObject { ["konto"] = test.KontoId, ["code"] = I.Totp.CodeText(g2, I.Totp.Zeitschritt(uhr.GetUtcNow())) });
            Pruefe.Gleich(10, erster.Daten!.AsArray().Count, "zehn Notfallcodes an der Konsole");
            uhr.Vor(TimeSpan.FromSeconds(30));
            Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, (await Gedrosselt(() => test.Client.AdminAnmeldenAsync(admin.Schluessel, admin.Code()))).Code, "Code des alten Geheimnisses");
            Pruefe.Wahr((await Gedrosselt(() => test.Client.AdminAnmeldenAsync(admin.Schluessel, I.Totp.CodeText(g2, I.Totp.Zeitschritt(uhr.GetUtcNow()))))).Ok, "neues Geheimnis gilt");

            // Konsole sperrt den Admin-Nachweis: Sitzung endet sofort.
            test.PushesLeeren();
            await t1.AdminOk("admin-sperren", new JsonObject { ["konto"] = test.KontoId });
            Pruefe.Wahr(await Warten(() => HatMeldung(test, MeldungsArten.AdminSitzung, "BEENDET")), "Sperre beendet die Admin-Sitzung");
            Pruefe.Wahr(await Warten(() => HatMeldung(bert, MeldungsArten.AdminGeaendert, "GESPERRT")), "Warnung an den anderen Admin");
            uhr.Vor(TimeSpan.FromSeconds(30));
            Pruefe.Gleich(Fehlercode.ZweitfaktorGesperrt, (await Gedrosselt(() => test.Client.AdminAnmeldenAsync(admin.Schluessel, I.Totp.CodeText(g2, I.Totp.Zeitschritt(uhr.GetUtcNow()))))).Code, "gesperrt");
            await t1.AdminOk("admin-entsperren", new JsonObject { ["konto"] = test.KontoId });
            var notfall = await t1.AdminOk("notfallcodes", new JsonObject { ["konto"] = test.KontoId });
            var notfallcodes = notfall.Daten!.AsArray().Select(n => n!.GetValue<string>()).ToList();
            Pruefe.Gleich(Fehlercode.ZweitfaktorFalsch, (await Gedrosselt(() => test.Client.AdminAnmeldenAsync(admin.Schluessel, null, erster.Daten!.AsArray()[0]!.GetValue<string>()))).Code, "alte Notfallcodes ungültig");

            // Neustart (7.2): Adminbuch und TOTP-Schlüssel überstehen ihn; die Admin-Sitzung nicht.
            halter.Server = null;
            await t1.DienstBeendenAsync();
            halter.HartTrennen();
            t2 = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr);
            halter.Server = t2;
            Pruefe.Wahr(await Warten(() => test.Client.Zustand == ClientZustand.AktivUdp && test.Client.Aktivierungen == 2 && bert.Client.Aktivierungen == 2, 15_000), "beide wieder AKTIV");
            Pruefe.Wahr(t2.Dienst.Zentrale.Admins.IstEingerichtet(test.KontoId) && t2.Dienst.Zentrale.Konten.Stand.Konten[test.KontoId].Eigentuemer, "Adminbuch und Eigentümer geladen");
            Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Journal, new JournalAnfrage()))).Code, "keine Admin-Sitzung nach dem Neustart");
            var nachNeustart = await Gedrosselt(() => test.Client.AdminAnmeldenAsync(admin.Schluessel, null, notfallcodes[0]));
            Pruefe.Wahr(nachNeustart.Ok, "Notfallcode nach dem Neustart: " + nachNeustart.Fehler);
            var journal = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Journal, new JournalAnfrage()));
            Pruefe.Wahr(journal.Body is JournalAntwort j && j.Eintraege.Any(e => e.Art == "ADMIN_GESPERRT") && j.Eintraege.Any(e => e.Art == "EIGENTUEMER"), "Journal übersteht den Neustart");

            // Entziehen: Stufe RAIDLEITER, Zeichen fort, Nachweis fort.
            await t2.AdminOk("admin-entziehen", new JsonObject { ["konto"] = bert.KontoId });
            Pruefe.Gleich(I.Stufe.Raidleiter, t2.Dienst.Zentrale.Konten.Stand.Konten[bert.KontoId].Stufe, "Bert ist RAIDLEITER");
            Pruefe.Wahr(await Warten(() => bert.Client.Lage.Konto is { Admin: null } k && !k.Zeichen.Contains("ADMIN")), "Bert ohne Admin-Zustand und ohne Zeichen");
            Pruefe.Falsch((await t2.Admin("admin-entziehen", new JsonObject { ["konto"] = bert.KontoId })).Ok, "kein Admin mehr");

            // Der Eigentümer bleibt ADMIN, auch an der Konsole; erst ein Wechsel des Eigentümers gibt ihn frei (3.10).
            var entziehen = await t2.Admin("admin-entziehen", new JsonObject { ["konto"] = test.KontoId });
            Pruefe.Wahr(!entziehen.Ok && entziehen.Text.Contains("eigentuemer --konto", StringComparison.Ordinal), "Eigentümer nicht entziehen: " + entziehen.Text);
            var herab = await t2.Admin("stufe", new JsonObject { ["konto"] = test.KontoId, ["stufe"] = "FUNKER" });
            Pruefe.Falsch(herab.Ok, "Eigentümer nicht herabstufen: " + herab.Text);
            var z2 = t2.Dienst.Zentrale;
            Pruefe.Wahr(z2.Admins.IstEingerichtet(test.KontoId) && z2.Konten.Stand.Konten[test.KontoId] is { Stufe: I.Stufe.Admin, Eigentuemer: true },
                "Nachweis, Stufe und Merkmal unverändert");
        }
        finally
        {
            admin?.Dispose();
            if (test is not null) await test.DisposeAsync();
            if (bert is not null) await bert.DisposeAsync();
            if (t2 is not null) await t2.DisposeAsync();
            await t1.DisposeAsync();
        }
    }
}
