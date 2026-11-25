// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Text.Json.Nodes;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Tests.Oberflaeche;
using Funk.Verwaltung;
using Funk.Verwaltung.Flotte;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Raidleitung 0.3.0, Client gegen einen echten Server im Prozess (ohne TLS, ohne Geräte, ohne Fenster): Otto verbindet sich
/// wie der WPF-Client (FunkClientDienst über den Testzugang), bekommt an der Konsole einen Auftrag und hat ohne Neustart das
/// Brett; er teilt Fritz per Ablage ein, sieht die Belegung, holt das Kontenverzeichnis, sperrt und öffnet die Selbstwahl und
/// gibt den Auftrag ab. Dazu der Verwaltungsteil (Verwaltungsdienst) mit Admin-Sitzung: Aufträge vergeben, auflisten und
/// entziehen, eine Flotte aus dem Entwurf als ein Paket übernehmen, nach 5 Minuten nur mit frischem Code.
/// </summary>
internal static class RaidleitungClientTests
{
    private const string DiscordTest = "940000000000000001";
    private const string DiscordOtto = "940000000000000002";
    private const string DiscordFritz = "940000000000000003";

    private static IPAddress Lokal(int n) => IPAddress.Parse("127.0.2." + n.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static async Task<bool> Warten(Func<bool> bedingung, int ms = 8000)
    {
        var bis = Environment.TickCount64 + ms;
        while (!bedingung())
        {
            if (Environment.TickCount64 > bis) return false;
            await Task.Delay(20);
        }
        return true;
    }

    /// <summary>Etwas Abstand vor jeder Anfrage der gedrosselten Gruppen (Verwaltung 1/s, Verzeichnis 1 je 5 s).</summary>
    private static async Task<T> Ruhig<T>(Func<Task<T>> anfrage, int ms = 1100)
    {
        await Task.Delay(ms);
        return await anfrage();
    }

    [Test]
    public static async Task Dienst_AuftragBrettAblageBelegungVerzeichnisSelbstwahlAbgeben()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        await Testflotte.AnDieKonsoleAsync(t);
        await using var zugang = new TestzugangLauscher(t.Dienst);
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(3));

        // Otto wie der WPF-Client: FunkClientDienst, Verbindungsfabrik über den Testzugang, ohne Klang.
        await using var otto = new FunkClientDienst(() => Verbindungsfabrik.Bauen(new VerbindungsEinstellungen { Geraetename = "PC Otto" },
            (_, _) => Task.FromResult(true), null, zugang.Endpunkt.ToString()));
        await otto.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => otto.Lage.Registrierung is not null, 10_000), "Registrierungscode");
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = otto.Lage.Registrierung!.Code, ["discord"] = DiscordOtto, ["benutzername"] = "otto", ["stufe"] = "FUNKER" });
        Pruefe.Wahr(await Warten(() => otto.Lage.Status == Verbindungsstatus.Warteraum), "Warteraum");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = otto.Lage.Geraet });
        Pruefe.Wahr(await Warten(() => otto.Lage.IstAktiv && otto.Lage.Konto is not null, 10_000), "aktiv");
        var ottoId = otto.Lage.Konto!.Id;
        Pruefe.Falsch(BrettViewModel.Gilt(otto.Lage), "ohne Auftrag kein Brett");

        // Das Brett lebt auf einem eigenen Thread mit Kontext wie auf dem Oberflächen-Thread: Takt und alles nach await laufen dort.
        using var faden = new EinFaden();
        var takt = new FadenTakt(faden);
        var brett = await faden.Ausfuehren(() => new BrettViewModel(otto, takt, null, TimeSpan.Zero));
        Task<bool> Am(Func<bool> bedingung) => faden.Ausfuehren(bedingung);

        await t.AdminOk("auftrag", new JsonObject { ["konto"] = ottoId, ["grund"] = "Probe" });
        Pruefe.Wahr(await faden.Warten(() => BrettViewModel.Gilt(otto.Lage) && brett.Sichtbar), "Auftrag ohne Neustart: das Brett ist da");
        Pruefe.Wahr(otto.Lage.Konto!.Beauftragung is { Aktiv: true, Grund: "Probe", Durch: 0 } && await Am(() => brett.KannAbgeben), "eigener Auftrag in der Lage");
        var kopf = await faden.Ausfuehren(() => brett.AuftragText);
        Pruefe.Wahr(kopf.StartsWith("Einsatzleitung bis ", StringComparison.Ordinal) && kopf.EndsWith(": Probe", StringComparison.Ordinal), "Kopf: " + kopf);
        Pruefe.Wahr(await faden.Warten(() => brett.Karte(fritz.KontoId) is { Online: true }), "Fritz als Karte (Praesenz mit Details)");
        Pruefe.Wahr(await Am(() => brett.Gruppen.SelectMany(g => g.Spalten).Count(s => s.Name.StartsWith("Schiff ", StringComparison.Ordinal)) == 15), "15 Schiffe als Spalten");

        // Ablage: Fritz auf Schiff 1, Kapitän; der Server stellt um, die Belegung kommt als Push.
        var block = await faden.Ausfuehren(() => brett.Gruppen.SelectMany(g => g.Spalten).Single(s => s.Name == "Schiff 1").Bloecke.Single());
        Pruefe.Wahr(await Am(() => brett.Bewerten(brett.Karte(fritz.KontoId)!, block).Wirkung == Ablagewirkung.Passt), "grün vor dem Loslassen");
        Pruefe.Wahr(await faden.AusfuehrenAsync(() => brett.AblegenAsync(brett.Karte(fritz.KontoId)!, block)), "abgelegt: " + await faden.Ausfuehren(() => brett.Meldung));
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften is { RolleId: var r, SchiffId: var s } && r == block.RolleId && s == block.SchiffId),
            "Fritz sitzt auf dem Platz");
        Pruefe.Wahr(await faden.Warten(() => otto.Lage.SchiffVon(block.SchiffId)!.Plaetze.Single().Belegt == 1 && otto.Lage.MitPlatz >= 1 && block.Belegung == "1/1"),
            "Belegung (K1) im Kopf der Spalte: " + await faden.Ausfuehren(() => block.Belegung));
        var verlauf = await faden.Ausfuehren(() => brett.Verlauf.ToList());
        Pruefe.Wahr(verlauf.Count >= 1 && verlauf[0].Contains("du: ", StringComparison.Ordinal), "Verlauf: " + string.Join(" | ", verlauf));

        // Kontenverzeichnis (ZUWEISEN) und Selbstwahl sperren und öffnen.
        var verzeichnis = await Ruhig(otto.KontenVerzeichnisAsync, 5200);
        Pruefe.Wahr(verzeichnis.Ok && verzeichnis.Wert!.Any(k => k.KontoId == fritz.KontoId && k.RolleId == block.RolleId), "Verzeichnis: " + verzeichnis.Fehler);
        Pruefe.Gleich(null, await Ruhig(() => otto.SelbstwahlSetzenAsync(false, nachzuegler: true)), "Wechsel gesperrt");
        Pruefe.Wahr(await Warten(() => otto.Lage is { Selbstwahl: false, Nachzuegler: true } && otto.Lage.SelbstwahlDurch == ottoId), "Sperre mit Urheber in der Lage");
        Pruefe.Wahr(await faden.Warten(() => brett.WechselGesperrt && brett.SperrText!.StartsWith("Wechsel gesperrt durch dich", StringComparison.Ordinal)),
            "Schalter und Satz: " + await faden.Ausfuehren(() => brett.SperrText));
        Pruefe.Gleich(null, await Ruhig(() => otto.SelbstwahlSetzenAsync(true)), "wieder frei");
        Pruefe.Wahr(await Warten(() => otto.Lage.Selbstwahl), "frei");

        // Abgeben: das Brett verschwindet ohne Neustart.
        Pruefe.Gleich(null, await Ruhig(otto.AuftragAbgebenAsync), "abgegeben");
        Pruefe.Wahr(await faden.Warten(() => !BrettViewModel.Gilt(otto.Lage) && !brett.Sichtbar), "das Brett ist fort");
    }

    [Test]
    public static async Task Verwaltungsdienst_AuftraegeUndFlottePaket_MitFrischemCode()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(11));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(12));
        var wirt = new VerwaltungsdienstTests.Wirt(test.Client);
        var speicher = new VerwaltungsdienstTests.FluechtigerSpeicher();
        using var dienst = new Verwaltungsdienst(wirt, speicher, uhr, TimeSpan.FromMilliseconds(1500));

        // Admin einrichten und anmelden wie in VerwaltungsdienstTests.
        await t.AdminOk("admin-vorsehen", new JsonObject { ["konto"] = test.KontoId });
        Pruefe.Wahr(await Warten(() =>
        {
            wirt.Ausloesen();
            return dienst.Lage.AntragMoeglich;
        }), "vorgesehen");
        var antrag = await Ruhig(dienst.AntragStellenAsync);
        await t.AdminOk("admin-ernennen", new JsonObject { ["konto"] = test.KontoId, ["kennung"] = antrag.Wert });
        Pruefe.Wahr(await Warten(() =>
        {
            wirt.Ausloesen();
            return dienst.Lage.Admin == AdminStatus.Einrichtung;
        }), "ernannt");
        var e = await Ruhig(dienst.GeheimnisAbholenAsync);
        I.Base32.TryDekodieren(e.Wert!.Geheimnis!, out var geheimnis);
        string Code() => I.Totp.CodeText(geheimnis, I.Totp.Zeitschritt(uhr.GetUtcNow()));
        Pruefe.Wahr((await Ruhig(() => dienst.EinrichtungBestaetigenAsync(Code()))).Ok, "eingerichtet");
        uhr.Vor(TimeSpan.FromSeconds(30));
        Pruefe.Wahr((await Ruhig(() => dienst.AnmeldenAsync(Code(), notfallcode: false))).Ok && dienst.Lage.SitzungOffen, "Admin-Sitzung offen");

        // Aufträge: leer, vergeben (in den 5 Minuten nach dem Code ohne neue Frage), auflisten.
        var leer = await Ruhig(dienst.AuftraegeAsync);
        Pruefe.Wahr(leer.Ok && leer.Wert!.Count == 0, "noch keine Aufträge: " + leer.Fehler);
        var vergeben = await Ruhig(() => dienst.AuftragErteilenAsync(otto.KontoId, null, null,
            [Recht.GlobalSenden, Recht.Zuweisen, Recht.SprechanzeigeAlle], "Op Nachtfalke"));
        Pruefe.Wahr(vergeben.Ok && vergeben.Wert! is [{ Aktiv: true, Grund: "Op Nachtfalke" } a] && a.KontoId == otto.KontoId && a.Durch == test.KontoId, "vergeben: " + vergeben.Fehler);
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.HatRecht(Recht.Zuweisen) && !otto.Client.Lage.HatRecht(Recht.Durchsage)), "Otto leitet, nur mit den vergebenen Rechten");
        var ungueltig = await Ruhig(() => dienst.AuftragErteilenAsync(otto.KontoId, null, Auftragszeit.NaechsterMorgen(uhr.GetUtcNow().ToUnixTimeMilliseconds()) + 30 * 3600_000L,
            [Recht.Zuweisen, Recht.SprechanzeigeAlle], null));
        Pruefe.Gleich("Ein Auftrag dauert höchstens 24 Stunden.", ungueltig.Fehler, "der Satz des Servers");

        // Flotte: Entwurf aus der NetzListe des Clients, ein Trupp als Baustein, ein Netz umbenannt, als ein Paket.
        Pruefe.Wahr(await Warten(() => dienst.Flotte.NetzListe is not null), "NetzListe da");
        var vorher = dienst.Flotte.NetzListe!;
        var entwurf = Flottenentwurf.Aus(vorher);
        entwurf.BausteinTrupp();
        var paket = entwurf.Paket();
        Pruefe.Wahr(paket.Paket is not null, "Paket: " + paket.Fehler);
        var geaendert = 0;
        dienst.LageGeaendert += () => Interlocked.Increment(ref geaendert);
        Pruefe.Gleich(null, await Ruhig(() => dienst.FlotteUebernehmenAsync(paket.Paket!, vorher.Version)), "übernommen");
        Pruefe.Wahr(await Warten(() => dienst.Flotte.NetzListe is { } l && l.Version != vorher.Version && l.Schiffe.Any(s => s.Name == "Trupp Alpha")),
            "die neue NetzListe ist da");
        Pruefe.Wahr(Volatile.Read(ref geaendert) > 0, "und meldet sich als Lageänderung");
        Pruefe.Gleich(0, Flottenentwurf.Aus(dienst.Flotte.NetzListe!, entwurf.AlsNetzListe()).Aenderungen().Count, "der Server steht wie der Entwurf");
        var veraltet = await Ruhig(() => dienst.FlotteUebernehmenAsync(paket.Paket!, vorher.Version));
        Pruefe.Wahr(veraltet is not null && veraltet.Contains("geändert", StringComparison.Ordinal), "alte Version: " + veraltet);

        // Nach 6 Minuten fragt jede heikle Handlung nach einem frischen Code.
        uhr.Vor(TimeSpan.FromMinutes(6));
        var fragen = new List<string>();
        dienst.CodeFragen = (wofuer, _) =>
        {
            fragen.Add(wofuer);
            return Task.FromResult<string?>(Code());
        };
        var zweiter = Flottenentwurf.Aus(dienst.Flotte.NetzListe!);
        zweiter.NetzSetzen(zweiter.Netze.Single(n => n.Name == "Trupp Alpha") with { NurTaste = true });
        Pruefe.Gleich(null, await Ruhig(() => dienst.FlotteUebernehmenAsync(zweiter.Paket().Paket!, dienst.Flotte.NetzListe!.Version)), "mit frischem Code übernommen");
        Pruefe.Wahr(fragen.SequenceEqual(["Flotte übernehmen"]), "gefragt: " + string.Join(" | ", fragen));
        uhr.Vor(TimeSpan.FromMinutes(6));
        Pruefe.Gleich(null, await Ruhig(() => dienst.AuftragEntziehenAsync(otto.KontoId)), "entzogen");
        Pruefe.Wahr(fragen.Count == 2 && fragen[1] == "Einsatzleitung entziehen", "auch das Entziehen fragt");
        Pruefe.Wahr(await Warten(() => !otto.Client.Lage.HatRecht(Recht.Zuweisen)), "Otto leitet nicht mehr");
        var zuletzt = await Ruhig(dienst.AuftraegeAsync);
        Pruefe.Wahr(zuletzt.Ok && zuletzt.Wert!.Count == 0, "Liste leer");
    }
}

/// <summary>
/// Ein eigener Thread mit <see cref="SynchronizationContext"/>, wie der Oberflächen-Thread: Was dort beginnt, läuft nach jedem
/// await dort weiter, eins nach dem anderen. So prüfen die Tests ein Modell der Oberfläche über einem echten Dienst, ohne dass
/// zwei Threads zugleich in seinen Listen arbeiten.
/// </summary>
internal sealed class EinFaden : SynchronizationContext, IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Arbeit, object? Zustand)> _schlange = [];
    private readonly Thread _thread;

    public EinFaden()
    {
        _thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (arbeit, zustand) in _schlange.GetConsumingEnumerable()) arbeit(zustand);
        })
        { IsBackground = true, Name = "EinFaden" };
        _thread.Start();
    }

    /// <summary>Nach dem Ende (Dienste melden sich noch beim Abbauen) fällt Arbeit still weg.</summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        try
        {
            _schlange.Add((d, state));
        }
        catch (InvalidOperationException)
        {
        }
    }

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("Nur Post.");

    public Task<T> Ausfuehren<T>(Func<T> arbeit)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ =>
        {
            try
            {
                tcs.SetResult(arbeit());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        }, null);
        return tcs.Task;
    }

    public Task<T> AusfuehrenAsync<T>(Func<Task<T>> arbeit)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ => _ = Lauf(), null);
        return tcs.Task;

        async Task Lauf()
        {
            try
            {
                tcs.SetResult(await arbeit().ConfigureAwait(true));
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        }
    }

    /// <summary>Wartet, bis die Bedingung auf diesem Thread gilt.</summary>
    public async Task<bool> Warten(Func<bool> bedingung, int ms = 8000)
    {
        var bis = Environment.TickCount64 + ms;
        while (!await Ausfuehren(bedingung).ConfigureAwait(false))
        {
            if (Environment.TickCount64 > bis) return false;
            await Task.Delay(20).ConfigureAwait(false);
        }
        return true;
    }

    public void Dispose()
    {
        _schlange.CompleteAdding();
        _thread.Join(2000);
    }
}

/// <summary>Der Oberflächentakt über einem <see cref="EinFaden"/>.</summary>
internal sealed class FadenTakt(EinFaden faden) : IOberflaechentakt
{
    public void Einreihen(Action aktion) => faden.Post(_ => aktion(), null);
}