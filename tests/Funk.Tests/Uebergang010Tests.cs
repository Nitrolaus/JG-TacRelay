// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Übergang von 0.1.0 (Sarevocs Heimserver seit dem 30.09.2026, Stand 5053590) auf diesen Stand: Der Server lädt ein
/// Zustandsverzeichnis, das die Programme von 0.1.0 selbst geschrieben haben (tests\Funk.Tests\Daten\zustand-0.1.0, Herkunft
/// dort), ohne Datenverlust, auch über einen Neustart. Sarevoc erreicht danach an der Konsole alles, was er braucht (eigentuemer,
/// scnamen, discordname, admin-vorsehen, admin-ernennen), und sperrt sich dabei nicht aus: Sein Gerät aus 0.1.0 verbindet
/// weiter, meldet "0.1.0" als Version, und am Ende steht er in einer Admin-Sitzung.
/// </summary>
internal static class Uebergang010Tests
{
    /// <summary>Fingerabdruck von Sarevocs Gerät in den Testdaten (flüchtiger Schlüssel von damals).</summary>
    private const string TestGeraetDamals = "ZjDEb8gWaqnWqUHbQbcL5jW6DejjeoK2oCLFGCHSo74=";

    private static string Daten()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new PruefungException("Repository nicht gefunden"), "tests", "Funk.Tests", "Daten", "zustand-0.1.0");
    }

    /// <summary>
    /// Wanduhr ab einem festen Zeitpunkt, die mit der echten Zeit weiterläuft; die monotone Uhr bleibt die des Systems. Vor()
    /// schiebt beide wie die Testuhr (TOTP-Schritte).
    /// </summary>
    private sealed class Rueckuhr(DateTimeOffset start) : TimeProvider
    {
        private readonly long _t0 = TimeProvider.System.GetTimestamp();
        private long _vor;

        public override DateTimeOffset GetUtcNow() => start + TimeProvider.System.GetElapsedTime(_t0) + TimeSpan.FromTicks(Interlocked.Read(ref _vor));

        public override long GetTimestamp() =>
            TimeProvider.System.GetTimestamp() + (long)(Interlocked.Read(ref _vor) / (double)TimeSpan.TicksPerSecond * TimeProvider.System.TimestampFrequency);

        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;

        public void Vor(TimeSpan dauer) => Interlocked.Add(ref _vor, dauer.Ticks);
    }

    private static async Task<bool> Warten(Func<bool> bedingung, int ms = 10_000)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(20);
        }
        return true;
    }

    private static async Task<Antwort> Gedrosselt(Func<Task<Antwort>> anfrage)
    {
        for (var i = 0; ; i++)
        {
            var a = await anfrage();
            if (a.Code != Fehlercode.Grenze || i == 12) return a;
            await Task.Delay(3000);
        }
    }

    [Test]
    public static async Task Zustand010_LaedtOhneVerlust_JuErreichtAllesAnDerKonsole()
    {
        // Die Wanduhr steht kurz nach der Zeit der Testdaten (30.09.2026, 22:28 UTC), wie bei Sarevoc am Morgen nach dem Update. Sonst
        // griffen mit den Wochen die Fristen (Konto ohne Gerät nach 30 Tagen fort), und der Test prüfte die Fristen statt des Ladens.
        var uhr = new Rueckuhr(new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero));
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "uebergang-" + Guid.NewGuid().ToString("N")[..12]);
        var zustand = Path.Combine(verzeichnis, "zustand");
        Directory.CreateDirectory(zustand);
        foreach (var datei in Directory.GetFiles(Daten(), "*.json")) File.Copy(datei, Path.Combine(zustand, Path.GetFileName(datei)));
        var vorher = await File.ReadAllTextAsync(Path.Combine(zustand, "konten.json"));
        Pruefe.Wahr(!vorher.Contains("scNamen", StringComparison.Ordinal) && !vorher.Contains("eigentuemer", StringComparison.Ordinal)
            && !File.Exists(Path.Combine(zustand, "admins.json")) && !File.Exists(Path.Combine(zustand, "mitglieder.json")),
            "die Testdaten haben das Format von 0.1.0: ohne Namen, Eigentümer, Admin-Nachweis und Mitglieder");

        // Sarevocs Gerät: Der Schlüssel von damals ist fort; hier trägt ein neuer flüchtiger Schlüssel den Platz, sonst bleibt jedes Byte.
        var test = Geraeteidentitaet.Fluechtig();
        await File.WriteAllTextAsync(Path.Combine(zustand, "konten.json"), vorher.Replace(TestGeraetDamals, test.Fingerabdruck.AlsBase64(), StringComparison.Ordinal));

        var halter = new ServerHalter();
        Testserver? t1 = null;
        Testserver? t2 = null;
        FunkClient? client = null;
        using var adminSchluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            t1 = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr);
            halter.Server = t1;
            var z = t1.Dienst.Zentrale;

            // Geladen, wie 0.1.0 es schrieb.
            var k = z.Konten.Stand;
            Pruefe.Gleich(3, k.Konten.Count, "drei Konten");
            Pruefe.Wahr(k.Konten[1] is { Name: "Test", DiscordId: "111111111111111111", Benutzername: "test", Stufe: I.Stufe.Admin, Status: I.KontoStatus.Aktiv, Eigentuemer: false, DiscordName: null }
                && k.Konten[1].ScNamen.IsEmpty, "Test: ADMIN, ohne Namen und ohne Eigentümer");
            Pruefe.Wahr(k.Konten[2] is { Name: "Echo", DiscordId: "100000000000000001", Stufe: I.Stufe.Funker } && k.Konten[3] is { Name: "Probe" }, "Echo und Probe");
            Pruefe.Wahr(k.Geraete[1] is { KontoId: 1, Status: I.GeraetStatus.Freigegeben } && k.Geraete[2] is { KontoId: 2, Status: I.GeraetStatus.Freigegeben }
                && k.Geraete[3] is { KontoId: 3, Status: I.GeraetStatus.Entfernt }, "Geräte mit Status");
            Pruefe.Wahr(k.NaechsteKontoId == 4 && k.NaechsteGeraetId == 4 && k.KontoZuDiscordId("100000000000000002")?.KontoId == 3, "Zähler und Discord-IDs");
            Pruefe.Wahr(k.NachScName.IsEmpty && k.NachDiscordName.IsEmpty && k.Eigentuemer is null, "leere Namensräume, kein Eigentümer");
            var lage = z.Schreiber.Aktuell;
            Pruefe.Wahr(lage.Netze.TryGetValue(90, out var echo) && echo.Name == "Echo", "Netz 90 Echo");
            Pruefe.Wahr(lage.Konten.TryGetValue(1, out var testLage) && testLage.Felder.Any(f => f.NetzId == 90 && f.Hoeren && f.Senden), "Sarevocs Feld mit Echo aus 0.1.0 geladen");
            Pruefe.Wahr(t1.Logbuch.Any(l => l.Contains("Kein Eigentümer gesetzt", StringComparison.Ordinal)), "Warnung beim Start: kein Eigentümer");
            Pruefe.Wahr(z.Admins.Stand.Eintraege.IsEmpty && z.Mitglieder.Stand.Vormerkungen.IsEmpty, "kein Admin-Nachweis, keine Mitglieder");

            // Sarevocs Gerät verbindet und meldet sich als 0.1.0: AKTIV als ADMIN, im Feld mit Echo wie damals.
            client = new FunkClient(new FunkClientOptionen
            {
                Aufbau = halter.Aufbau(test, IPAddress.Parse("127.0.0.91")),
                Identitaet = test,
                Geraetename = "Sarevocs PC",
                ClientName = "JG TacRelay",
                ClientVersion = "0.1.0",
                Rueckfrage = (_, _) => Task.FromResult(false),
                Empfaenger = new Sammler(),
                Zufall = () => 0.0,
            });
            client.Starten();
            Pruefe.Wahr(await client.WartenAufAsync(s => s == ClientZustand.AktivUdp, TimeSpan.FromSeconds(10)), "Test AKTIV, war " + client.Zustand);
            Pruefe.Wahr(client.Lage.Welcome?.Konto is { Id: 1, Stufe: Stufe.Admin }, "Welcome: Konto 1, ADMIN");
            // Die Testdaten sind älter als 10 min: Beim Anmelden gilt dann der Standard, und der Stand von damals steht als
            // letzterStand bereit (6.9). Das ist die Regel für jede Sitzung nach langer Pause, kein Verlust durch den Übergang.
            var nachAnmeldung = z.Schreiber.Aktuell.Konten[1];
            Pruefe.Wahr(nachAnmeldung.Felder.Any(f => f.NetzId == 90) || nachAnmeldung.LetzterStand?.Felder.Any(f => f.NetzId == 90) == true,
                "Feld mit Echo: wiederhergestellt oder als letzter Stand gemerkt (6.9)");

            // Keine Rechteausweitung durch den Übergang (Gegenprüfung): Sarevoc ist aus 0.1.0 ADMIN mit allen Rechten, aber ohne
            // Admin-Nachweis. Verwaltungsrechte wirken erst in einer Admin-Sitzung, auch vor dem Setzen des Eigentümers.
            foreach (var (was, anfrage) in new (string, Func<Task<Antwort>>)[]
                     {
                         ("Konto sperren", () => client.AnfrageAsync(Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage { Op = KontoOperation.Sperren, KontoId = 2 })),
                         ("Discord-Name setzen", () => client.AnfrageAsync(Nachrichtentyp.KontoVerwaltung,
                             new KontoVerwaltungAnfrage { Op = KontoOperation.Discordname, KontoId = 2, Wert = System.Text.Json.JsonSerializer.SerializeToElement("Echo/DC") })),
                         ("fremde Geräte ansehen", () => client.AnfrageAsync(Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { KontoId = 2 })),
                         ("vormerken", () => client.AnfrageAsync(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Vormerken, ScName = "Neuling" })),
                     })
            {
                var ohne = await Gedrosselt(anfrage);
                Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, ohne.Code, "ohne Admin-Sitzung: " + was);
            }
            Pruefe.Wahr(z.Konten.Stand.Konten[2] is { Status: I.KontoStatus.Aktiv, DiscordName: null } && z.Mitglieder.Stand.Vormerkungen.IsEmpty,
                "nichts geändert");

            // An der Konsole, in der Reihenfolge der Anleitung.
            Pruefe.Falsch((await t1.Admin("eigentuemer", new JsonObject { ["konto"] = 2 })).Ok, "ein FUNKER wird nicht Eigentümer");
            await t1.AdminOk("eigentuemer", new JsonObject { ["konto"] = 1 });
            await t1.AdminOk("scnamen", new JsonObject { ["konto"] = 1, ["namen"] = new JsonArray("Sarevoc") });
            await t1.AdminOk("discordname", new JsonObject { ["konto"] = 1, ["name"] = "Sarevoc/Otto" });
            await t1.AdminOk("admin-vorsehen", new JsonObject { ["konto"] = 1 });
            Pruefe.Wahr(await Warten(() => client.Lage.Konto is { Eigentuemer: true, AdminAntrag: true, DiscordName: "Sarevoc/Otto", ScNamen: ["Sarevoc"] }),
                "Sarevocs Client sieht Eigentümer, Namen und 'Verwaltung einrichten'");
            var konten = await t1.AdminOk("konten");
            Pruefe.Wahr(konten.Text.Contains("EIGENTÜMER Sarevoc (Discord: Sarevoc/Otto, test)", StringComparison.Ordinal)
                && konten.Text.Contains("Echo (Discord: -, echo-funker)", StringComparison.Ordinal), "konten: " + konten.Text);

            // Admin-Nachweis wie in 3.9: Antrag aus dem Client, Kennung vorlesen, admin-ernennen, Geheimnis, erster Code.
            var antrag = await Gedrosselt(() => client.AdminAntragAsync(adminSchluessel));
            Pruefe.Wahr(antrag.Ok && antrag.Body is AdminEinrichtungAntwort { Kennung: not null }, "Antrag: " + antrag.Fehler);
            await t1.AdminOk("admin-ernennen", new JsonObject { ["konto"] = 1, ["kennung"] = ((AdminEinrichtungAntwort)antrag.Body!).Kennung });
            var abholen = await Gedrosselt(() => client.GeheimnisAbholenAsync(adminSchluessel));
            Pruefe.Wahr(abholen.Ok && abholen.Body is AdminEinrichtungAntwort { Geheimnis: not null } && I.Base32.TryDekodieren(((AdminEinrichtungAntwort)abholen.Body!).Geheimnis!, out _),
                "Geheimnis: " + abholen.Fehler);
            I.Base32.TryDekodieren(((AdminEinrichtungAntwort)abholen.Body!).Geheimnis!, out var geheimnis);
            var erster = await Gedrosselt(() => client.EinrichtungBestaetigenAsync(adminSchluessel, I.Totp.CodeText(geheimnis, I.Totp.Zeitschritt(uhr.GetUtcNow()))));
            Pruefe.Wahr(erster.Ok && erster.Body is AdminEinrichtungAntwort { Notfallcodes.Count: 10 }, "erster Code: " + erster.Fehler);
            uhr.Vor(TimeSpan.FromSeconds(30));
            var sitzung = await Gedrosselt(() => client.AdminAnmeldenAsync(adminSchluessel, I.Totp.CodeText(geheimnis, I.Totp.Zeitschritt(uhr.GetUtcNow()))));
            Pruefe.Wahr(sitzung.Ok, "Admin-Sitzung: " + sitzung.Fehler);
            var echoName = await Gedrosselt(() => client.AnfrageAsync(Nachrichtentyp.KontoVerwaltung,
                new KontoVerwaltungAnfrage { Op = KontoOperation.Discordname, KontoId = 2, Wert = System.Text.Json.JsonSerializer.SerializeToElement("Echo-Funker") }));
            Pruefe.Wahr(echoName.Ok, "in der Admin-Sitzung: Discord-Name des Echo-Funkers gesetzt: " + echoName.Fehler);
            Pruefe.Wahr(await Warten(() => client.Lage.Konto is { Admin: AdminStatus.Aktiv } kk && kk.Zeichen.Contains("ADMIN")), "Test eingerichtet, mit Lorbeerkranz");

            // Neustart: nichts von damals und nichts von eben geht verloren.
            halter.Server = null;
            await t1.DienstBeendenAsync();
            halter.HartTrennen();
            t2 = await Testserver.StartenAsync(verzeichnis: verzeichnis, zeit: uhr);
            halter.Server = t2;
            var k2 = t2.Dienst.Zentrale.Konten.Stand;
            Pruefe.Wahr(k2.Konten[1] is { Name: "Test", Stufe: I.Stufe.Admin, Eigentuemer: true, DiscordName: "Sarevoc/Otto" } j && j.ScNamen.SequenceEqual(["Sarevoc"]),
                "Test nach dem Neustart: Eigentümer mit Namen");
            Pruefe.Wahr(k2.Konten[2].DiscordName == "Echo-Funker" && k2.NachDiscordName.Count == 2 && k2.NachScName.Count == 1, "Namensräume nach dem Laden gebaut");
            Pruefe.Wahr(k2.Geraete[2] is { Status: I.GeraetStatus.Freigegeben } && k2.Geraete[3] is { Status: I.GeraetStatus.Entfernt } && k2.NaechsteKontoId == 4,
                "Geräte und Zähler von damals");
            Pruefe.Wahr(t2.Dienst.Zentrale.Schreiber.Aktuell.Netze.ContainsKey(90) && t2.Dienst.Zentrale.Admins.IstEingerichtet(1), "Netz Echo und Admin-Nachweis");
            foreach (var datei in new[] { "konten.json", "lage.json", "admins.json", "journal.json" })
                Pruefe.Wahr(File.Exists(Path.Combine(zustand, datei)), datei + " geschrieben");
            Pruefe.Wahr(await client.WartenAufAsync(s => s == ClientZustand.AktivUdp && client.Aktivierungen >= 2, TimeSpan.FromSeconds(15)), "Test wieder AKTIV nach dem Neustart");
            var journal = await t2.AdminOk("journal", new JsonObject { ["anzahl"] = 50 });
            foreach (var art in new[] { "EIGENTUEMER", "SCNAMEN", "DISCORDNAME", "ADMIN_VORGESEHEN", "ADMIN_ERNANNT" })
                Pruefe.Wahr(journal.Text.Contains(art, StringComparison.Ordinal), "Journal enthält " + art);
        }
        finally
        {
            if (client is not null) await client.DisposeAsync();
            test.Dispose();
            if (t2 is not null) await t2.DisposeAsync();
            if (t1 is not null) await t1.DisposeAsync();
        }
    }
}
