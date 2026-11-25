// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;
using Funk.Verwaltung.Modelle;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Der Verwaltungsteil über einem echten Server (prozessintern, ohne TLS): <see cref="Verwaltungsdienst"/> mit einem flüchtigen
/// Admin-Schlüssel statt des TPM. Der ganze Weg aus 3.9 wie in der Oberfläche: vorgesehen, Antrag mit Kennung, ernannt,
/// Geheimnis, erster Code, Notfallcodes, Anmeldung; dann Mitglieder und Katalog, ein Antrag, der nach 5 Minuten nur mit
/// frischem Code freigegeben wird (Codefrage der Oberfläche), Abmelden und Leerlauf. Dazu das Modell der Oberfläche auf
/// demselben Dienst.
/// </summary>
internal static class VerwaltungsdienstTests
{
    private const string DiscordTest = "920000000000000001";
    private const string DiscordFritz = "920000000000000002";

    private static IPAddress Lokal(int n) => IPAddress.Parse("127.0.0." + n.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Admin-Schlüssel im Speicher: wie das TPM, nur flüchtig.</summary>
    internal sealed class FluechtigerSpeicher : IAdminSchluesselSpeicher
    {
        private ECDsa? _schluessel;

        public int Erzeugt { get; private set; }

        public ECDsa? Laden() => _schluessel;

        public ECDsa Erzeugen()
        {
            Erzeugt++;
            return _schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }
    }

    /// <summary>Wirt über einem Kern-Client; die Oberfläche fehlt, Arbeit läuft sofort.</summary>
    internal sealed class Wirt(FunkClient client) : IVerwaltungsWirt
    {
        public FunkClient? Client => client;

        public event Action? Geaendert;

        public void Einreihen(Action arbeit) => arbeit();

        public void Ausloesen() => Geaendert?.Invoke();
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

    /// <summary>Etwas Abstand vor jeder Anfrage: Die Eimer der Ratengrenzen füllen sich mit 1/s (4.8).</summary>
    private static async Task<T> Ruhig<T>(Func<Task<T>> anfrage)
    {
        await Task.Delay(1100);
        return await anfrage();
    }

    [Test]
    public static async Task Verwaltungsdienst_VomAntragBisZurFreigabeMitCode()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(91));
        var wirt = new Wirt(test.Client);
        var speicher = new FluechtigerSpeicher();
        using var dienst = new Verwaltungsdienst(wirt, speicher, uhr, TimeSpan.FromMilliseconds(1500));
        bool Lage(Func<Verwaltungslage, bool> wie)
        {
            wirt.Ausloesen();
            return wie(dienst.Lage);
        }

        Pruefe.Wahr(dienst.Lage is { Verbunden: true, Admin: null, AntragMoeglich: false, SitzungOffen: false }, "verbunden, noch nicht vorgesehen");
        var ohne = await dienst.AntragStellenAsync();
        Pruefe.Falsch(ohne.Ok, "ohne Vorsehen kein Antrag");

        // Sarevoc sieht an der Konsole vor; der Verwaltungsteil sieht es am Kontostand.
        await t.AdminOk("admin-vorsehen", new JsonObject { ["konto"] = test.KontoId });
        Pruefe.Wahr(await Warten(() => Lage(l => l.AntragMoeglich)), "vorgesehen");
        var antrag = await Ruhig(dienst.AntragStellenAsync);
        Pruefe.Wahr(antrag.Ok && speicher.Erzeugt == 1, "Antrag: " + antrag.Fehler);
        Pruefe.Gleich(I.Fingerabdruck.AusSpki(speicher.Laden()!.ExportSubjectPublicKeyInfo()).Anzeige, antrag.Wert, "Kennung des Admin-Schlüssels zum Vorlesen");

        await t.AdminOk("admin-ernennen", new JsonObject { ["konto"] = test.KontoId, ["kennung"] = antrag.Wert });
        Pruefe.Wahr(await Warten(() => Lage(l => l.Admin == AdminStatus.Einrichtung)), "ernannt: Einrichtung");
        var e = await Ruhig(dienst.GeheimnisAbholenAsync);
        Pruefe.Wahr(e.Ok && I.Base32.TryDekodieren(e.Wert!.Geheimnis!, out _), "Geheimnis: " + e.Fehler);
        Pruefe.Wahr(QrCode.Kodieren(e.Wert!.Uri!).Fassung >= 5, "der otpauth-Link passt in einen QR-Code");
        I.Base32.TryDekodieren(e.Wert.Geheimnis!, out var geheimnis);
        string Code() => I.Totp.CodeText(geheimnis, I.Totp.Zeitschritt(uhr.GetUtcNow()));

        var falsch = await Ruhig(() => dienst.EinrichtungBestaetigenAsync("12345"));
        Pruefe.Gleich(Verwaltungstexte.CodeForm, falsch.Fehler, "fünf Ziffern gehen gar nicht erst hinaus");
        var notfall = await Ruhig(() => dienst.EinrichtungBestaetigenAsync(Code()));
        Pruefe.Wahr(notfall.Ok && notfall.Wert!.Count == 10, "zehn Notfallcodes: " + notfall.Fehler);
        Pruefe.Wahr(await Warten(() => Lage(l => l.Admin == AdminStatus.Aktiv)), "eingerichtet");

        // Anmelden: im nächsten Zeitschritt (jeder Code gilt einmal).
        uhr.Vor(TimeSpan.FromSeconds(30));
        var sitzung = await Ruhig(() => dienst.AnmeldenAsync(Code(), notfallcode: false));
        Pruefe.Wahr(sitzung.Ok && dienst.Lage.SitzungOffen && dienst.Lage.BestaetigtBis is not null, "Admin-Sitzung offen: " + sitzung.Fehler);

        // Mitglieder und Katalog.
        Pruefe.Gleich(null, await Ruhig(() => dienst.VormerkenAsync("Brandy", "Brandy/Lea", [], "kommt Freitag")), "vorgemerkt");
        var mitglieder = await Ruhig(dienst.MitgliederAsync);
        Pruefe.Wahr(mitglieder.Ok && mitglieder.Wert!.Any(m => m.VormerkungId is not null && m.ScNamen.Contains("Brandy") && m.Notiz == "kommt Freitag"), "Vormerkung in der Liste");
        Pruefe.Gleich(null, await Ruhig(() => dienst.KatalogSetzenAsync([new Rollenkatalogeintrag { Id = 1, Name = "Marine", Gruppe = "Boden" }], 0)), "Katalog gesetzt");
        var katalog = await Ruhig(dienst.KatalogAsync);
        Pruefe.Wahr(katalog.Ok && katalog.Wert!.Version == 1 && katalog.Wert.Eintraege.Single().Name == "Marine", "Katalog gelesen");
        // Liste einfügen seit 0.11.8: Spalte 3 (Untergilde) wird übergangen, ein Hinweis je Liste, angelegt wird trotzdem.
        var liste = await Ruhig(() => dienst.EinfuegenAsync(["Sturm; Sturm/Kai; BS; Marine", "Wind; ; XY"]));
        Pruefe.Wahr(liste.Ok && liste.Wert!.Zeilen.Count == 0 && liste.Wert.Hinweis == "Spalte 3 (Untergilde) wird seit 0.11.8 übergangen",
            "Liste mit Spalte 3: " + (liste.Fehler ?? liste.Wert?.Hinweis));
        var ohneSpalte3 = await Ruhig(() => dienst.EinfuegenAsync(["Regen;;;Marine"]));
        Pruefe.Wahr(ohneSpalte3.Ok && ohneSpalte3.Wert!.Zeilen.Count == 0 && ohneSpalte3.Wert.Hinweis is null, "Spalte 3 leer: kein Hinweis");
        var eingefuegt = await Ruhig(dienst.MitgliederAsync);
        Pruefe.Wahr(eingefuegt.Ok && eingefuegt.Wert!.Single(m => m.Name == "Sturm") is { DiscordName: "Sturm/Kai", Rollen: [1], Untergilde: null }
            && eingefuegt.Wert!.Any(m => m.Name == "Wind") && eingefuegt.Wert!.All(m => m.Untergilde is null), "angelegt, ohne Untergilde");
        var veraltet = await Ruhig(() => dienst.KatalogSetzenAsync([], 0));
        Pruefe.Wahr(veraltet!.Contains("geändert", StringComparison.Ordinal), "alte Version: " + veraltet);

        // Ein Antrag mit Namen; nach 6 Minuten braucht das Freigeben einen frischen Code, den die Oberfläche erfragt.
        await using var fritz = await t.VerbindenAsync(null, Lokal(92));
        Pruefe.Gleich(WelcomeStatus.Unbekannt, (await fritz.HelloAsync()).Status, "Fritz unbekannt");
        await fritz.AnfrageAsync(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = "PC Fritz", ScName = "Brandy", DiscordName = "Brandy/Lea" });
        var antraege = await Ruhig(dienst.AntraegeAsync);
        var wartet = antraege.Wert?.SingleOrDefault(g => g.Antrag == true);
        Pruefe.Wahr(wartet is { ScName: "Brandy", Vormerkungen: [{ UeberScName: true }] }, "Antrag mit Vorschlag der Vormerkung: " + antraege.Fehler);

        uhr.Vor(TimeSpan.FromMinutes(6));
        var fragen = new List<string>();
        dienst.CodeFragen = (wofuer, _) =>
        {
            fragen.Add(wofuer);
            return Task.FromResult<string?>(fragen.Count == 1 ? null : Code());
        };
        var abgebrochen = await Ruhig(() => dienst.EntscheidenAsync(wartet!.Id, GeraetEntscheidung.Freigeben, wartet.Vormerkungen![0].Id));
        Pruefe.Wahr(abgebrochen is not null && fragen.SequenceEqual(["Gerät freigeben"]), "ohne Code: nicht freigegeben (" + abgebrochen + ")");
        var frei = await Ruhig(() => dienst.EntscheidenAsync(wartet!.Id, GeraetEntscheidung.Freigeben, wartet.Vormerkungen![0].Id));
        Pruefe.Wahr(frei is null && fragen.Count == 2, "mit frischem Code freigegeben: " + frei);
        var nachher = await Ruhig(dienst.MitgliederAsync);
        Pruefe.Wahr(nachher.Wert!.Any(m => m.KontoId is not null && m.Zustand != MitgliedZustand.Antrag && m.ScNamen.Contains("Brandy"))
            && !nachher.Wert!.Any(m => m.VormerkungId is not null && m.ScNamen.Contains("Brandy")), "Vormerkung ins Konto übernommen");

        // Sperre aufheben (seit 0.8.0): an der Konsole gesperrt, hier aufgehoben; danach ist das Gerät entfernt, nicht freigegeben.
        var brandy = nachher.Wert!.Single(m => m.KontoId is not null && m.ScNamen.Contains("Brandy")).KontoId!.Value;
        await t.AdminOk("sperren", new JsonObject { ["geraet"] = fritz.Fingerabdruck.AlsBase64(), ["grund"] = "Test" });
        var geraete = await Ruhig(() => dienst.GeraeteAsync(brandy));
        Pruefe.Wahr(geraete.Ok && geraete.Wert!.Single().Status == GeraeteStatus.Entzogen, "gesperrt in der Liste: " + geraete.Fehler);
        Pruefe.Wahr(dienst.Lage.SperreAufhebenBekannt, "der Server kennt es: " + dienst.Lage.ServerFassung);
        var aufgehoben = await Ruhig(() => dienst.SperreAufhebenAsync(wartet!.Id));
        Pruefe.Gleich(null, aufgehoben, "Sperre aufgehoben");
        var danach = await Ruhig(() => dienst.GeraeteAsync(brandy));
        Pruefe.Gleich(GeraeteStatus.Entfernt, danach.Wert!.Single().Status, "danach entfernt");
        Pruefe.Gleich(Verwaltungstexte.NichtGesperrt, await Ruhig(() => dienst.SperreAufhebenAsync(wartet!.Id)), "ein zweites Mal: nicht mehr gesperrt");

        // Das Modell der Oberfläche auf demselben Dienst: offen, alles geladen.
        using (var vm = new VerwaltungViewModel(dienst))
        {
            Pruefe.Wahr(await Warten(() => vm.IstOffen && vm.Mitglieder.Sichtbar.Any(z => z.Name == "Brandy") && vm.Katalog.Eintraege.Count == 1, 10_000),
                "Oberfläche offen und geladen: " + vm.Fehler);
        }

        // Abmelden: die Sitzung ist zu, Verwaltung sagt es.
        await Ruhig(async () =>
        {
            await dienst.AbmeldenAsync();
            return 0;
        });
        Pruefe.Falsch(dienst.Lage.SitzungOffen, "abgemeldet");
        var zu = await Ruhig(dienst.MitgliederAsync);
        Pruefe.Wahr(!zu.Ok && zu.Fehler!.Contains("Admin-Sitzung ist zu", StringComparison.Ordinal), "ohne Sitzung: " + zu.Fehler);

        // Seit 0.9.1 (Sarevoc 08.10.2026, Q54) kein Ende nach 30 Minuten ohne Verwaltung mehr; nach 12 Stunden schließt der Server, der Dienst merkt
        // es an der Antwort.
        uhr.Vor(TimeSpan.FromSeconds(30));
        Pruefe.Wahr((await Ruhig(() => dienst.AnmeldenAsync(Code(), notfallcode: false))).Ok && dienst.Lage.SitzungOffen, "wieder angemeldet");
        uhr.Vor(TimeSpan.FromMinutes(31));
        var leerlauf = await Ruhig(dienst.KatalogAsync);
        Pruefe.Wahr(leerlauf.Ok, "Katalog lesen darf jeder");
        var nachLeerlauf = await Ruhig(dienst.MitgliederAsync);
        Pruefe.Wahr(nachLeerlauf.Ok && dienst.Lage.SitzungOffen, "31 Minuten ohne Verwaltung: Sitzung noch offen (" + nachLeerlauf.Fehler + ")");
        uhr.Vor(TimeSpan.FromHours(12));
        var nachZwoelf = await Ruhig(dienst.MitgliederAsync);
        Pruefe.Wahr(!nachZwoelf.Ok && !dienst.Lage.SitzungOffen, "nach 12 Stunden: Sitzung zu (" + nachZwoelf.Fehler + ")");
    }
}
