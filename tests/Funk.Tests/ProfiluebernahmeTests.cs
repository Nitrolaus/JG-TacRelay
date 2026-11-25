// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Text.Json.Nodes;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;
using Funk.Tests.Oberflaeche;
using Schluessel = Funk.Tests.Oberflaeche.OberflaecheProfilTests.Schluessel;
using SigniertesProfil = Funk.Protokoll.Identitaet.SigniertesProfil;
using DrahtProfil = Funk.Protokoll.Steuerung.SigniertesProfil;

namespace Funk.Tests;

/// <summary>
/// F1 (EINRICHTEN-UMZUG-ENTWURF, A55): Der Client übernimmt nach Welcome{FREIGEGEBEN} ein neueres, offline signiertes Serverprofil
/// aus <c>Welcome.profil</c>, legt es als <c>serverprofil.json</c> neben seine Einstellungen und nimmt danach das höchste gültige
/// aus Setup und Ablage. Angriffe: Downgrade (ein Server mit gültigem Pin reicht ein älteres signiertes Profil durch), fremd
/// signiert, unsigniert, kaputt, ein untergeschobenes oder gelöschtes <c>serverprofil.json</c>.
/// </summary>
internal static class ProfiluebernahmeTests
{
    private static readonly Fingerabdruck PinAlt = Fingerabdruck.AusSpki([1]);
    private static readonly Fingerabdruck PinNeu = Fingerabdruck.AusSpki([3]);
    private static readonly Fingerabdruck Reserve = Fingerabdruck.AusSpki([2]);

    /// <summary>Ein mit <paramref name="k"/> signiertes Profil als <c>{daten, signatur}</c>.</summary>
    internal static byte[] Signiert(Schluessel k, uint version, string[]? hosts = null, Fingerabdruck[]? pins = null, ushort port = 8443,
        DateTimeOffset? gueltigBis = null)
    {
        var jetzt = DateTimeOffset.UtcNow;
        var ende = gueltigBis ?? jetzt.AddDays(365);
        var p = new Serverprofil("Meine Staffel", version, Math.Min(jetzt.AddDays(-1).ToUnixTimeMilliseconds(), ende.AddDays(-1).ToUnixTimeMilliseconds()),
            ende.ToUnixTimeMilliseconds(), [.. (hosts ?? ["heim.example.org"]).Select(h => new ProfilHost(h, true, false))], port,
            pins ?? [PinAlt, Reserve], []);
        return SigniertesProfil.Signieren(p.AlsJson(), k.Privat).AlsJson();
    }

    /// <summary>So steht ein signiertes Profil in <c>Welcome.profil</c>.</summary>
    internal static DrahtProfil Draht(byte[] json)
    {
        Pruefe.Wahr(SigniertesProfil.TryLesen(json, out var s), "Testprofil lesbar");
        return new DrahtProfil { Daten = Convert.ToBase64String(s!.Daten), Signatur = Convert.ToBase64String(s.Signatur) };
    }

    private static string[] Protokoll(Profiluebernahme u) =>
        u.ProtokollPfad is { } p && File.Exists(p) ? File.ReadAllLines(p) : [];

    [Test]
    public static void Profiluebernahme_NeueresWirdGespeichertUndGewaehlt()
    {
        using var o = new Testordner();
        using var k = new Schluessel();
        using var fremd = new Schluessel();
        var setup = o.Datei("profil.json");
        File.WriteAllBytes(setup, Signiert(k, 1));
        Directory.CreateDirectory(o.Datei("benutzer"));
        var ablage = Path.Combine(o.Datei("benutzer"), Profilquelle.AblageDateiname);
        var quelle = new Profilquelle(setup, [k.Spki], ablage);
        var jetzt = DateTimeOffset.UtcNow;

        var b = Profilpruefung.Waehlen(quelle, jetzt);
        Pruefe.Wahr(b.Gueltig && b.Profil!.ProfilVersion == 1 && b.Herkunft == Profilherkunft.Setup, "ohne Ablage: das Setup-Profil");

        var u = new Profiluebernahme(quelle);
        var gemeldet = new List<Uebernahmeergebnis>();
        u.Uebernommen += gemeldet.Add;
        var v2 = Signiert(k, 2, ["funk.example.org", "heim.example.org"], [PinAlt, PinNeu, Reserve]);
        var e = u.Annehmen(Draht(v2));
        Pruefe.Gleich(Uebernahmeurteil.Uebernommen, e.Urteil, "Version 2 über 1: " + e.Text);
        Pruefe.Gleich((uint?)1, e.BekannteVersion, "verglichen mit der höchsten bekannten");
        Pruefe.Gleich(1, gemeldet.Count, "einmal gemeldet");
        Pruefe.BytesGleich(v2, File.ReadAllBytes(ablage), "die Ablage hält genau die signierten Bytes");
        Pruefe.Falsch(File.Exists(ablage + ".neu"), "keine Zwischendatei übrig (atomar)");
        b = Profilpruefung.Waehlen(quelle, jetzt);
        Pruefe.Wahr(b.Gueltig && b.Profil!.ProfilVersion == 2 && b.Herkunft == Profilherkunft.Server && b.Datei == Path.GetFullPath(ablage),
            "danach gilt Version 2 aus der Ablage");
        Pruefe.Gleich(2, b.Profil!.Hosts.Count, "mit dem neuen Host");

        // Neustart: eine neue Übernahme sieht die gespeicherte Version.
        var nachNeustart = new Profiluebernahme(quelle);
        e = nachNeustart.Annehmen(Draht(v2));
        Pruefe.Gleich(Uebernahmeurteil.NichtNeuer, e.Urteil, "dieselbe Version nach dem Neustart: " + e.Text);
        Pruefe.Gleich((uint?)2, e.BekannteVersion, "bekannt ist jetzt 2");

        // Downgrade: ein Server mit gültigem Pin reicht das ältere, echt signierte Profil durch.
        e = u.Annehmen(Draht(Signiert(k, 1, ["boese.example.org"])));
        Pruefe.Gleich(Uebernahmeurteil.NichtNeuer, e.Urteil, "ältere Version verworfen: " + e.Text);
        Pruefe.BytesGleich(v2, File.ReadAllBytes(ablage), "Ablage unverändert nach dem Downgrade-Versuch");

        // Fremd signiert, mit höherer Version und neuem Host.
        e = u.Annehmen(Draht(Signiert(fremd, 9, ["boese.example.org"], [Fingerabdruck.AusSpki([9]), Reserve])));
        Pruefe.Gleich(Uebernahmeurteil.SignaturUngueltig, e.Urteil, "fremd signiert: " + e.Text);

        // Unsigniert: Daten einer Version 9 mit der Signatur von Version 2.
        Pruefe.Wahr(SigniertesProfil.TryLesen(v2, out var echt), "v2 lesbar");
        var neuDaten = new Serverprofil("Meine Staffel", 9, 0, long.MaxValue / 4, [new ProfilHost("boese.example.org", true, false)], 443, [PinNeu, Reserve], []).AlsJson();
        e = u.Annehmen(new DrahtProfil { Daten = Convert.ToBase64String(neuDaten), Signatur = Convert.ToBase64String(echt!.Signatur) });
        Pruefe.Gleich(Uebernahmeurteil.SignaturUngueltig, e.Urteil, "Daten ohne passende Signatur: " + e.Text);

        // Signatur stimmt, Inhalt nicht (nur ein Pin).
        var einPin = new Serverprofil("Meine Staffel", 10, 0, long.MaxValue / 4, [new ProfilHost("funk.example.org", true, false)], 443, [PinNeu, Reserve], []).AlsJson();
        var kaputt = System.Text.Encoding.UTF8.GetString(einPin).Replace(",\"" + Reserve.AlsBase64() + "\"", "", StringComparison.Ordinal);
        e = u.Annehmen(Draht(SigniertesProfil.Signieren(System.Text.Encoding.UTF8.GetBytes(kaputt), k.Privat).AlsJson()));
        Pruefe.Gleich(Uebernahmeurteil.InhaltUngueltig, e.Urteil, "richtig signiert, aber nur ein Pin: " + e.Text);

        // Kein Base64, falsche Signaturlänge, nichts.
        Pruefe.Gleich(Uebernahmeurteil.Unlesbar, u.Annehmen(new DrahtProfil { Daten = "@@@@", Signatur = Convert.ToBase64String(new byte[64]) }).Urteil, "kein Base64");
        Pruefe.Gleich(Uebernahmeurteil.Unlesbar, u.Annehmen(new DrahtProfil { Daten = Convert.ToBase64String(neuDaten), Signatur = "AAAA" }).Urteil, "Signatur zu kurz");
        Pruefe.Gleich(Uebernahmeurteil.Unlesbar, u.Annehmen(null).Urteil, "kein Profil");
        Pruefe.BytesGleich(v2, File.ReadAllBytes(ablage), "nach allen Angriffen gilt weiter Version 2");
        Pruefe.Gleich(1, gemeldet.Count, "gemeldet wird nur die Übernahme");

        // Protokolliert wird jedes Verwerfen, dieselbe Meldung nicht zweimal hintereinander.
        var protokoll = Protokoll(u);
        Pruefe.Wahr(protokoll.Any(z => z.Contains("ÜBERNOMMEN", StringComparison.Ordinal) && z.Contains("Version 2", StringComparison.Ordinal)), "Übernahme im Protokoll");
        foreach (var grund in new[] { "nicht neuer", "Signatur", "Inhalt", "unlesbar" })
            Pruefe.Wahr(protokoll.Any(z => z.Contains("VERWORFEN", StringComparison.Ordinal) && z.Contains(grund, StringComparison.OrdinalIgnoreCase)), "im Protokoll: " + grund + "\n" + string.Join("\n", protokoll));
        for (var i = 1; i < protokoll.Length; i++)
            Pruefe.Falsch(string.Equals(protokoll[i][protokoll[i].IndexOf(' ', StringComparison.Ordinal)..], protokoll[i - 1][protokoll[i - 1].IndexOf(' ', StringComparison.Ordinal)..], StringComparison.Ordinal),
                "keine doppelte Zeile hintereinander: " + protokoll[i]);
        var vorher = protokoll.Length;
        nachNeustart.Annehmen(Draht(v2));
        nachNeustart.Annehmen(Draht(v2));
        var gleich = Protokoll(u);
        Pruefe.Gleich(vorher + 1, gleich.Length, "dieselbe Version bei jedem Verbinden: eine Zeile, nicht eine je Verbindung");
    }

    [Test]
    public static void Profiluebernahme_AblageGeloeschtKaputtOderUntergeschoben()
    {
        using var o = new Testordner();
        using var k = new Schluessel();
        using var fremd = new Schluessel();
        var setup = o.Datei("profil.json");
        File.WriteAllBytes(setup, Signiert(k, 3));
        var ablage = o.Datei(Profilquelle.AblageDateiname);
        var quelle = new Profilquelle(setup, [k.Spki], ablage);
        var jetzt = DateTimeOffset.UtcNow;
        var u = new Profiluebernahme(quelle);
        Pruefe.Gleich(Uebernahmeurteil.Uebernommen, u.Annehmen(Draht(Signiert(k, 4, ["funk.example.org"]))).Urteil, "Version 4 übernommen");

        // Gelöscht: wieder das Setup-Profil, kein Fehler.
        File.Delete(ablage);
        var b = Profilpruefung.Waehlen(quelle, jetzt);
        Pruefe.Wahr(b.Gueltig && b.Profil!.ProfilVersion == 3 && b.Herkunft == Profilherkunft.Setup, "gelöschte Ablage: Setup-Profil");

        // Kaputt: übergangen; die nächste Übernahme ersetzt sie.
        File.WriteAllText(ablage, "{kaputt");
        b = Profilpruefung.Waehlen(quelle, jetzt);
        Pruefe.Wahr(b.Gueltig && b.Profil!.ProfilVersion == 3 && b.Herkunft == Profilherkunft.Setup && b.Fehler is null, "kaputte Ablage übergangen: " + b.Fehler);
        Pruefe.Gleich(Uebernahmeurteil.Uebernommen, u.Annehmen(Draht(Signiert(k, 4, ["funk.example.org"]))).Urteil, "kaputte Ablage ersetzt");

        // Untergeschoben: fremd signiert und höher; zählt nicht, auch nicht für die bekannte Version.
        File.WriteAllBytes(ablage, Signiert(fremd, 50, ["boese.example.org"]));
        b = Profilpruefung.Waehlen(quelle, jetzt);
        Pruefe.Wahr(b.Gueltig && b.Profil!.ProfilVersion == 3 && b.Herkunft == Profilherkunft.Setup, "fremd signierte Ablage zählt nicht");
        var e = u.Annehmen(Draht(Signiert(k, 4, ["funk.example.org"])));
        Pruefe.Wahr(e.Urteil == Uebernahmeurteil.Uebernommen && e.BekannteVersion == 3, "bekannt bleibt 3, nicht 50: " + e.Text);

        // Das Setup bringt später eine höhere Version: Sie gilt, die Ablage ist überholt.
        File.WriteAllBytes(setup, Signiert(k, 7));
        b = Profilpruefung.Waehlen(quelle, jetzt);
        Pruefe.Wahr(b.Profil!.ProfilVersion == 7 && b.Herkunft == Profilherkunft.Setup, "neueres Setup schlägt die Ablage");
        Pruefe.Gleich(Uebernahmeurteil.NichtNeuer, u.Annehmen(Draht(Signiert(k, 5))).Urteil, "5 ist nicht neuer als 7");

        // Gleichstand: das Setup.
        File.WriteAllBytes(ablage, Signiert(k, 7, ["funk.example.org"]));
        b = Profilpruefung.Waehlen(quelle, jetzt);
        Pruefe.Wahr(b.Herkunft == Profilherkunft.Setup && b.Profil!.Hosts[0].Name == "heim.example.org", "Gleichstand: Setup");

        // Ohne Setup-Profil (Datei weg): die Ablage allein reicht.
        File.Delete(setup);
        b = Profilpruefung.Waehlen(quelle, jetzt);
        Pruefe.Wahr(b.Gueltig && b.Herkunft == Profilherkunft.Server && b.Profil!.ProfilVersion == 7, "nur die Ablage: " + b.Fehler);

        // Ohne Schlüssel und ohne Ablage: nichts geschieht.
        var ohne = new Profiluebernahme(new Profilquelle(setup, [], o.Datei("nie.json")));
        Pruefe.Gleich(Uebernahmeurteil.OhneSchluessel, ohne.Annehmen(Draht(Signiert(k, 99))).Urteil, "ohne eingebauten Schlüssel");
        Pruefe.Falsch(File.Exists(o.Datei("nie.json")), "ohne Schlüssel nichts gespeichert");
        Pruefe.Gleich(Uebernahmeurteil.OhneAblage, new Profiluebernahme(new Profilquelle(setup, [k.Spki])).Annehmen(Draht(Signiert(k, 99))).Urteil, "ohne Ablage");
    }

    /// <summary>
    /// Gegenprüfung 01.10.2026: Ein echt signiertes, neueres Profil ohne den Pin des Servers, der es liefert (etwa das Abschlussprofil
    /// eines Umzugs, aus Versehen auf den alten Server gelegt), übernahm jeder Client, der sich dort verband, und fand danach keinen
    /// Server mehr, bis ein neues Setup kam. Jetzt verwirft der Client so ein Profil.
    /// </summary>
    [Test]
    public static void Profiluebernahme_OhnePinDesLieferndenServers_Verworfen()
    {
        using var o = new Testordner();
        using var k = new Schluessel();
        var setup = o.Datei("profil.json");
        File.WriteAllBytes(setup, Signiert(k, 1));
        var quelle = new Profilquelle(setup, [k.Spki], o.Datei(Profilquelle.AblageDateiname));
        var u = new Profiluebernahme(quelle);
        var gemeldet = 0;
        u.Uebernommen += _ => gemeldet++;

        // Version 3 kennt nur noch den neuen Server und die Reserve; geliefert vom alten (PinAlt).
        var abschluss = Draht(Signiert(k, 3, ["funk.example.org"], [PinNeu, Reserve]));
        var e = u.Annehmen(abschluss, PinAlt);
        Pruefe.Gleich(Uebernahmeurteil.OhneServerPin, e.Urteil, "ohne den Pin des liefernden Servers: " + e.Text);
        Pruefe.Falsch(File.Exists(quelle.Ablage!), "nichts gespeichert");
        Pruefe.Gleich(0, gemeldet, "nichts gemeldet");
        Pruefe.Wahr(Protokoll(u).Any(z => z.Contains("VERWORFEN", StringComparison.Ordinal) && z.Contains(PinAlt.Anzeige, StringComparison.Ordinal)),
            "protokolliert, mit dem Pin: " + string.Join(" | ", Protokoll(u)));
        e = u.Annehmen(abschluss, null);
        Pruefe.Gleich(Uebernahmeurteil.OhneServerPin, e.Urteil, "Pin unbekannt: ebenso verworfen");

        // Derselbe Abschluss vom neuen Server (PinNeu) gilt; ebenso ein Übergangsprofil vom alten.
        Pruefe.Gleich(Uebernahmeurteil.Uebernommen, u.Annehmen(Draht(Signiert(k, 2, ["heim.example.org", "funk.example.org"], [PinAlt, PinNeu, Reserve])), PinAlt).Urteil,
            "Übergang vom alten Server");
        Pruefe.Gleich(Uebernahmeurteil.Uebernommen, u.Annehmen(abschluss, PinNeu).Urteil, "Abschluss vom neuen Server");
        Pruefe.Gleich(2, gemeldet, "zweimal übernommen");
        // Über die Reserve (Variante C: der neue Server zeigt den Reserveschlüssel) ebenso.
        Pruefe.Gleich(Uebernahmeurteil.Uebernommen, u.Annehmen(Draht(Signiert(k, 4, ["funk.example.org"], [PinNeu, Reserve])), Reserve).Urteil, "geliefert über die Reserve");
    }

    /// <summary>Der Weg über FunkClientDienst: Mit dem Pin der Verbindung (FunkAnbindung.ServerPin) wird ein Profil ohne ihn verworfen.</summary>
    [Test]
    public static async Task Profiluebernahme_OhnePinDesServers_UeberDieVerbindung()
    {
        using var k = new Schluessel();
        using var o = new Testordner();
        await using var t = await Testserver.StartenAsync();
        var serverProfil = Path.Combine(t.Verzeichnis, "profil.json");
        var setup = o.Datei("profil.json");
        File.WriteAllBytes(setup, Signiert(k, 1));
        var quelle = new Profilquelle(setup, [k.Spki], o.Datei(Profilquelle.AblageDateiname));
        var u = new Profiluebernahme(quelle);
        var nachgezogen = new List<Serverprofil>();

        // Der Server (Pin PinAlt) reicht Version 2 ohne seinen eigenen Pin durch.
        File.WriteAllBytes(serverProfil, Signiert(k, 2, ["funk.example.org"], [PinNeu, Reserve]));
        await t.AdminOk("profil-neu-laden");

        var halter = new ServerHalter { Server = t };
        var id = Geraeteidentitaet.Fluechtig();
        await using var d = new FunkClientDienst(() =>
        {
            var c = new FunkClient(new FunkClientOptionen
            {
                Aufbau = halter.Aufbau(id, IPAddress.Parse("127.0.0.62")),
                Identitaet = id,
                ClientVersion = "1.0.0",
                Rueckfrage = (_, _) => Task.FromResult(true),
                Zufall = () => 0.0,
            });
            return new FunkAnbindung(c, id, "Meine Staffel", id.Fingerabdruck.Anzeige, "Software")
            {
                Profil = u,
                ProfilNachziehen = nachgezogen.Add,
                ServerPin = () => PinAlt,
            };
        });
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null, 10_000), "Code da");
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = d.Lage.Registrierung!.Code, ["discord"] = "930000000000000002", ["benutzername"] = "toni" });
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Warteraum, 5_000), "Warteraum");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = id.Fingerabdruck.Anzeige });
        Pruefe.Wahr(await Warten(() => Protokoll(u).Any(z => z.Contains("ohne Pin dieses Servers", StringComparison.Ordinal)), 10_000),
            "verworfen und protokolliert: " + string.Join(" | ", Protokoll(u)));
        Pruefe.Falsch(File.Exists(quelle.Ablage!), "nichts gespeichert");
        Pruefe.Gleich(0, nachgezogen.Count, "nichts an den Aufbau gegeben");
        await d.TrennenAsync();
        id.Dispose();
    }

    /// <summary>Die Karte im Bereich Verbindung: "Vom Server übernommen, Version N", wirksam beim nächsten Verbinden.</summary>
    [Test]
    public static void Profiluebernahme_KarteZeigtHerkunft()
    {
        using var k = new Schluessel();
        using var a = new Aufbau(FunkLage.Leer);
        var setup = a.Ordner.Datei("profil.json");
        File.WriteAllBytes(setup, Signiert(k, 1));
        var quelle = new Profilquelle(setup, [k.Spki], a.Ordner.Datei(Profilquelle.AblageDateiname));
        var u = new Profiluebernahme(quelle);
        var vm = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, quelle, uebernahme: u);
        Pruefe.Gleich(null, vm.ProfilHerkunft, "Setup-Profil: keine Herkunftszeile");
        Pruefe.Wahr(vm.ProfilSignatur!.Contains("Version 1", StringComparison.Ordinal), vm.ProfilSignatur);

        u.Annehmen(Draht(Signiert(k, 2, ["funk.example.org"])));
        Pruefe.Gleich("Vom Server übernommen, Version 2. Gilt ab dem nächsten Verbinden.", vm.ProfilHerkunft, "gleich nach der Übernahme");
        Pruefe.Wahr(vm.ProfilZiel!.Contains("funk.example.org", StringComparison.Ordinal) && vm.ProfilSignatur!.Contains("Version 2", StringComparison.Ordinal),
            "die Karte zeigt das neue Profil: " + vm.ProfilZiel);

        var spaeter = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, quelle, uebernahme: new Profiluebernahme(quelle));
        Pruefe.Gleich("Vom Server übernommen, Version 2.", spaeter.ProfilHerkunft, "nach dem Neustart: gilt schon");
        Pruefe.Falsch(spaeter.ProfilHerkunft!.Any(z => z is (char)0x2013 or (char)0x2014 or (char)0x201E or (char)0x201C or (char)0x2026), "nur einfache Zeichen");
    }

    /// <summary>
    /// Der ganze Weg ohne TLS: Antrag, Freigabe, Welcome mit Profil Version 2 vom Testserver; der Client speichert es und gibt es
    /// dem Aufbau weiter. Danach reicht der Server ein älteres (Downgrade) und ein fremd signiertes durch: beides verworfen.
    /// </summary>
    [Test]
    public static async Task Profiluebernahme_NachDerFreigabeVomServer()
    {
        using var k = new Schluessel();
        using var fremd = new Schluessel();
        using var o = new Testordner();
        await using var t = await Testserver.StartenAsync();
        var serverProfil = Path.Combine(t.Verzeichnis, "profil.json");
        var setup = o.Datei("profil.json");
        File.WriteAllBytes(setup, Signiert(k, 1));
        var quelle = new Profilquelle(setup, [k.Spki], o.Datei(Profilquelle.AblageDateiname));
        var u = new Profiluebernahme(quelle);
        var nachgezogen = new List<Serverprofil>();

        File.WriteAllBytes(serverProfil, Signiert(k, 2, ["funk.example.org", "heim.example.org"], [PinAlt, PinNeu, Reserve]));
        await t.AdminOk("profil-neu-laden");

        var halter = new ServerHalter { Server = t };
        var id = Geraeteidentitaet.Fluechtig();
        await using var d = new FunkClientDienst(() =>
        {
            var c = new FunkClient(new FunkClientOptionen
            {
                Aufbau = halter.Aufbau(id, IPAddress.Parse("127.0.0.61")),
                Identitaet = id,
                ClientVersion = "1.0.0",
                Rueckfrage = (_, _) => Task.FromResult(true),
                Zufall = () => 0.0,
            });
            return new FunkAnbindung(c, id, "Meine Staffel", id.Fingerabdruck.Anzeige, "Software") { Profil = u, ProfilNachziehen = nachgezogen.Add };
        });
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null, 10_000), "Code da");
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = d.Lage.Registrierung!.Code, ["discord"] = "930000000000000001", ["benutzername"] = "otto" });
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Warteraum, 5_000), "Warteraum");
        Pruefe.Falsch(File.Exists(quelle.Ablage!), "vor der Freigabe nichts übernommen (Welcome WARTET trägt kein Profil)");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = id.Fingerabdruck.Anzeige });
        Pruefe.Wahr(await Warten(() => File.Exists(quelle.Ablage!) && nachgezogen.Count == 1, 10_000), "nach der Freigabe übernommen und dem Aufbau gegeben");
        Pruefe.Gleich(2u, nachgezogen[0].ProfilVersion, "Version 2 für den nächsten Versuch");
        Pruefe.Gleich(Profilherkunft.Server, Profilpruefung.Waehlen(quelle, DateTimeOffset.UtcNow).Herkunft, "gilt ab jetzt aus der Ablage");

        // Downgrade über einen Server mit gültigem Pin: das echte Profil Version 1.
        var gespeichert = File.ReadAllBytes(quelle.Ablage!);
        File.WriteAllBytes(serverProfil, Signiert(k, 1));
        await t.AdminOk("profil-neu-laden");
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => Protokoll(u).Any(z => z.Contains("nicht neuer", StringComparison.OrdinalIgnoreCase) && z.Contains("Version 1", StringComparison.Ordinal)), 10_000),
            "älteres Profil verworfen und protokolliert: " + string.Join(" | ", Protokoll(u)));
        // Fremd signiert, höher.
        File.WriteAllBytes(serverProfil, Signiert(fremd, 8, ["boese.example.org"]));
        await t.AdminOk("profil-neu-laden");
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => Protokoll(u).Any(z => z.Contains("Signatur", StringComparison.Ordinal)), 10_000), "fremd signiert verworfen");
        Pruefe.BytesGleich(gespeichert, File.ReadAllBytes(quelle.Ablage!), "Ablage hält weiter Version 2");
        Pruefe.Gleich(1, nachgezogen.Count, "nichts weiter an den Aufbau gegeben");
        await d.TrennenAsync();
        id.Dispose();
    }

    private static async Task<bool> Warten(Func<bool> bedingung, int ms)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(20);
        }
        return true;
    }
}
