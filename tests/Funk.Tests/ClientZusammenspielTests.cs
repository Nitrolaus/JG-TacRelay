// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Zusammenspiel der Client-Bibliothek (Funk.ClientKern) mit dem Dienst auf Loopback: 3 bis 6 Clients, Anmeldung bis
/// AKTIV/UDP, Sprache mit Stempel, Rechte-Fälle aus 8.9 Ende zu Ende, C4 mitten im Spruch (F1), Wiederverbinden nach
/// geordnetem und hartem Serverneustart (7.1, 7.2), Tunnel bei gesperrtem UDP und Rückkehr (5.12). Die Verbindungen gehen
/// am TLS-Handshake vorbei (<see cref="ServerHalter"/>), alles Weitere ist der echte Weg.
/// </summary>
internal static class ClientZusammenspielTests
{
    private static IPAddress Lokal(int i) => new([127, 0, 7, (byte)(10 + i)]);

    private static string Discord(int i) => (200_000_000_000_000_000UL + (ulong)i).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static JsonObject Netz(ushort id, string name, string offen = "HOEREN_SENDEN", bool standard = false, bool nurTaste = false) => new()
    {
        ["objekt"] = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["typ"] = "NORMAL",
            ["reihenfolge"] = id,
            ["nurTaste"] = nurTaste,
            ["ausloeserStandard"] = "TASTE",
            ["offen"] = offen,
            ["standard"] = standard,
        },
    };

    [Test]
    public static async Task Kern_DreiClients_AnmeldungSpracheUndEmpfang()
    {
        var halter = new ServerHalter();
        await using var t = await Testserver.StartenAsync();
        halter.Server = t;
        await t.AdminOk("netz-setzen", Netz(2, "Kommando", standard: true));
        await using var a = await KernFunker.AktivAsync(halter, Discord(1), "anna", "FUNKER", Lokal(1));
        await using var b = await KernFunker.AktivAsync(halter, Discord(2), "bert", "FUNKER", Lokal(2));
        await using var c = await KernFunker.AktivAsync(halter, Discord(3), "carl", "FUNKER", Lokal(3));

        // Sync: NetzListe, Mitgliedschaften (STANDARD-Feld Kommando), Praesenz, vollständig.
        Pruefe.Wahr(a.Client.Lage.Vollstaendig && a.Client.Lage.NetzVon(2) is not null, "Lagebild vollständig mit Netz 2");
        Pruefe.Wahr(a.Client.Lage.FeldMitNetz(2) is { Senden: true, Hoeren: true }, "STANDARD-Feld Kommando mit hören und senden");
        Pruefe.Wahr(await Warten(() => a.Client.Lage.Praesenz.Count >= 3, 3000), "Praesenz kennt alle drei");

        // C1: Sendeauswahl als Feldnummer, beim Senden über die Mitgliedschaften in Netz-IDs umgesetzt.
        var feld = a.Client.Lage.FeldMitNetz(2)!.Feld;
        var sa = await a.Client.SendeauswahlSetzenAsync([feld], false);
        Pruefe.Wahr(sa.Ok, "SendeauswahlSetzen angenommen");
        Pruefe.Wahr(a.Client.Sprechen.BeginnenAusAuswahl(), "Beginnen aus der Auswahl");
        Pruefe.Gleich(Sendeziel.Netz(2), a.Client.Sprechen.Ziel, "Ziel aus Feld -> Netz 2");
        var opus = new byte[160];
        opus[0] = 0xF8;
        for (var i = 1; i < opus.Length; i++) opus[i] = (byte)i;
        for (var i = 0; i < 10; i++) Pruefe.Wahr(a.Client.Sprechen.Senden(opus), "Frame gesendet");
        a.Client.Sprechen.Beenden();

        foreach (var hoerer in new[] { b, c })
        {
            Pruefe.Wahr(await hoerer.Empfang.WartenAsync(p => p.Any(x => x.Ende) && p.Count(x => !x.Ende) >= 10, TimeSpan.FromSeconds(3)),
                hoerer.Name + " empfängt 10 Frames und ENDE");
            var pakete = hoerer.Empfang.Von(a.KontoId);
            var audio = pakete.Where(p => !p.Ende).OrderBy(p => p.Frame).ToList();
            Pruefe.Gleich(10, audio.Count, hoerer.Name + ": 10 Audio-Frames");
            Pruefe.Wahr(audio.All(p => p.Art == Spruchart.Netz && p.Netze.SequenceEqual(new ushort[] { 2 })), "Art NETZ [2]");
            Pruefe.Wahr((audio[0].Kennzeichen & Kennzeichen.Beginn) != 0 && audio.Skip(1).All(p => (p.Kennzeichen & Kennzeichen.Beginn) == 0), "BEGINN nur beim ersten");
            Pruefe.Wahr(audio[0].NeueZeitlinie && audio.Skip(1).All(p => !p.NeueZeitlinie), "C6: eine Zeitlinie");
            Pruefe.Wahr(audio.Zip(audio.Skip(1)).All(x => x.Second.Frame == x.First.Frame + 1), "Frame-Nr lückenlos");
            Pruefe.BytesGleich(opus, audio[5].Opus, "Opus bitgleich");
            Pruefe.Gleich(1, pakete.Count(p => p.Ende), "nur das erste ENDE wird gemeldet");
            Pruefe.Wahr(audio.All(p => p.Weg == Verbindungsweg.Udp), "über UDP");
        }
        Pruefe.Gleich(0, a.Empfang.Von(a.KontoId).Count, "der Absender hört sich nie selbst");

        // Keepalive-Echo liefert RTT; der runter-Zähler hat keine Lücken.
        Pruefe.Wahr(await Warten(() => b.Client.Messwerte.Stand().RttP50Ms is not null, 5000), "RTT aus KEEPALIVE_ECHO");
        var m = b.Client.Messwerte.Stand();
        Pruefe.Gleich(0L, m.RunterVerloren, "kein Verlust runter auf Loopback");
        Pruefe.Wahr(m.Sprachpakete >= 10, "Sprachpakete gezählt");

        // C5: viele HoerenSetzen hintereinander, ohne UEBERHOLT und ohne GRENZE.
        var hoeren = new List<Task<Antwort>>();
        for (var i = 0; i < 20; i++) hoeren.Add(b.Client.HoerenSetzenAsync(2, i % 2 == 1));
        var antworten = await Task.WhenAll(hoeren).WaitAsync(TimeSpan.FromSeconds(10));
        Pruefe.Wahr(antworten.All(x => x.Ok), "alle HoerenSetzen beantwortet, keins überholt oder gebremst");
        Pruefe.Wahr(await Warten(() => b.Client.Lage.FeldMitNetz(2)?.Hoeren == true, 3000), "letzter Stand gilt: hören an");
        Pruefe.Wahr(b.Client.Zustand == ClientZustand.AktivUdp, "keine Trennung");
    }

    /// <summary>8.9 Ende zu Ende, Stichproben: Fälle 1-11, 13, 17, 19, 20 und 23 über die Client-Bibliothek.</summary>
    [Test]
    public static async Task Kern_Rechtematrix89_Stichproben()
    {
        var halter = new ServerHalter();
        await using var t = await Testserver.StartenAsync();
        halter.Server = t;
        await t.AdminOk("netz-setzen", Netz(2, "Kommando"));
        await t.AdminOk("netz-setzen", Netz(7, "Crew S3"));
        await t.AdminOk("netz-setzen", Netz(8, "Bruecke S3", nurTaste: true));
        await using var z = await KernFunker.AktivAsync(halter, Discord(10), "zora", "ADMIN", Lokal(10));
        await using var a = await KernFunker.AktivAsync(halter, Discord(11), "anna", "RAIDLEITER", Lokal(11));
        await using var b = await KernFunker.AktivAsync(halter, Discord(12), "bert", "FUNKER", Lokal(12));
        await using var c = await KernFunker.AktivAsync(halter, Discord(13), "carl", "FUNKER", Lokal(13));
        await using var d = await KernFunker.AktivAsync(halter, Discord(14), "dora", "FUNKER", Lokal(14));
        await using var e = await KernFunker.AktivAsync(halter, Discord(15), "emil", "FUNKER", Lokal(15));
        var alle = new[] { a, b, c, d, e };

        static JsonNode Feld(byte f, ushort netz, bool h, bool s) => new JsonObject { ["feld"] = f, ["netzId"] = netz, ["hoeren"] = h, ["senden"] = s };
        var v = await z.Client.AnfrageAsync(Nachrichtentyp.Verschieben, SteuerJson.LesenOderWerfen<VerschiebenAnfrage>(System.Text.Encoding.UTF8.GetBytes(new JsonObject
        {
            ["aenderungen"] = new JsonArray
            {
                new JsonObject { ["kontoId"] = a.KontoId, ["felder"] = new JsonArray(Feld(0, 2, true, true), Feld(1, 7, true, true)) },
                new JsonObject { ["kontoId"] = b.KontoId, ["felder"] = new JsonArray(Feld(0, 7, true, true), Feld(1, 8, true, true)) },
                new JsonObject { ["kontoId"] = c.KontoId, ["felder"] = new JsonArray(Feld(0, 2, true, false), Feld(1, 7, false, true), Feld(2, 8, true, false)) },
                new JsonObject { ["kontoId"] = e.KontoId, ["felder"] = new JsonArray(Feld(0, 7, true, false), Feld(1, 2, true, false)) },
            },
        }.ToJsonString())));
        Pruefe.Wahr(v.Ok, "Verschieben (Grundzustand 8.9): " + v.Code);
        Pruefe.Wahr(await Warten(() => c.Client.Lage.Mitgliedschaften?.Felder.Count == 3 && e.Client.Lage.Mitgliedschaften?.Felder.Count == 2
            && a.Client.Lage.FeldMitNetz(7) is not null && b.Client.Lage.FeldMitNetz(8) is not null, 3000), "Mitgliedschaften-Push bei allen");

        var name = new Dictionary<uint, string>();
        foreach (var f in alle) name[f.KontoId] = f.Name;

        async Task Fall(string nr, KernFunker sender, Sendeziel ziel, bool va, Dictionary<KernFunker, (Spruchart Art, ushort[] Netze)> erwartet, SendeFehlerCode? fehler = null, ushort? fehlerNetz = null)
        {
            foreach (var f in alle) f.Empfang.Leeren();
            sender.PushesLeeren();
            Pruefe.Wahr(await sender.SpruchAsync(ziel, va), $"Fall {nr}: gesendet");
            // Fest 250 ms reichten unter Last nicht (volle Suite neben einem Spiel: ein Frame fehlte). Auf die Erwarteten warten,
            // dann kurz nachhören, ob bei den anderen etwas ankommt, das nicht ankommen darf.
            await Warten(() => erwartet.Keys.All(f => f.Empfang.Von(sender.KontoId).Count(p => !p.Ende) >= 3), 2000);
            await Task.Delay(150);
            foreach (var f in alle)
            {
                var audio = f.Empfang.Von(sender.KontoId).Where(p => !p.Ende).ToList();
                if (f == sender)
                {
                    Pruefe.Gleich(0, audio.Count, $"Fall {nr}: der Sender bekommt nichts zurück");
                    continue;
                }
                if (!erwartet.TryGetValue(f, out var soll))
                {
                    Pruefe.Gleich(0, audio.Count, $"Fall {nr}: {f.Name} bekommt nichts");
                    continue;
                }
                Pruefe.Gleich(3, audio.Count, $"Fall {nr}: {f.Name} bekommt 3 Frames");
                Pruefe.Wahr(audio.All(p => p.Art == soll.Art && p.Netze.SequenceEqual(soll.Netze)),
                    $"Fall {nr}: {f.Name} erwartet {soll.Art} [{string.Join(",", soll.Netze)}], war {audio[0].Art} [{string.Join(",", audio[0].Netze)}]");
                Pruefe.Wahr((audio.OrderBy(p => p.Frame).First().Kennzeichen & Kennzeichen.Beginn) != 0, $"Fall {nr}: {f.Name} BEGINN beim ersten Paket");
            }
            if (fehler is { } code)
            {
                var sf = await sender.PushAsync<SendeFehler>(s => s.Fehler.Any(x => x.Code == code && (fehlerNetz is null || x.NetzId == fehlerNetz)), TimeSpan.FromSeconds(2));
                Pruefe.Wahr(sf is not null, $"Fall {nr}: SendeFehler {code} an {sender.Name}");
            }
        }

        var n = Spruchart.Netz;
        var g = Spruchart.Global;
        var ds = Spruchart.Durchsage;
        await Fall("1", b, Sendeziel.Netz(7), false, new() { [a] = (n, [7]), [e] = (n, [7]) });
        await Fall("2", b, Sendeziel.Netz(8), true, [], SendeFehlerCode.NurTaste);
        await Fall("3", b, Sendeziel.Netz(8), false, new() { [c] = (n, [8]) });
        await Fall("4", c, Sendeziel.Netz(2), false, [], SendeFehlerCode.KeinSenderecht);
        await Fall("5", b, Sendeziel.Netz(1), false, [], SendeFehlerCode.KeinRechtGlobal);
        await Fall("6", a, Sendeziel.Netz(1), false, new() { [b] = (g, [1]), [c] = (g, [1]), [d] = (g, [1]), [e] = (g, [1]) });
        await Fall("7", a, Sendeziel.Netz(2, 7), false, new() { [b] = (n, [7]), [c] = (n, [2]), [e] = (n, [7, 2]) });
        await Fall("8", b, Sendeziel.Netz(7, 8), false, [], SendeFehlerCode.MehrfachNichtErlaubt);
        await Fall("9", a, Sendeziel.Durchsage(7), false, new() { [b] = (ds, [7]), [c] = (ds, [7]), [e] = (ds, [7]) });
        await Fall("10", a, Sendeziel.DurchsageAnAlle, false, new() { [b] = (ds, []), [c] = (ds, []), [d] = (ds, []), [e] = (ds, []) });
        await Fall("11", d, Sendeziel.DurchsageAnAlle, false, [], SendeFehlerCode.KeinRechtDurchsage);
        await Fall("13", a, Sendeziel.Netz(1, 2), false, new() { [b] = (g, [1]), [c] = (g, [1, 2]), [d] = (g, [1]), [e] = (g, [1, 2]) });
        await Fall("17", a, Sendeziel.Netz(1), true, [], SendeFehlerCode.NurTaste);

        var stumm = await z.Client.AnfrageAsync(Nachrichtentyp.Stummschalten, new StummschaltenAnfrage { KontoId = a.KontoId, NetzId = 7, Sekunden = 60 });
        Pruefe.Wahr(stumm.Ok, "Stummschalten(A, 7, 60 s)");
        Pruefe.Wahr(await Warten(() => a.Client.Lage.Mitgliedschaften?.Stumm.Any(s => s.NetzId == 7) == true, 2000), "A sieht die Stummschaltung");
        await Fall("19", a, Sendeziel.Netz(2, 7), false, new() { [c] = (n, [2]), [e] = (n, [2]) }, SendeFehlerCode.Stummgeschaltet, 7);
        var aufheben = await z.Client.AnfrageAsync(Nachrichtentyp.Stummschalten, new StummschaltenAnfrage { KontoId = a.KontoId, NetzId = 7, Sekunden = 0 });
        Pruefe.Wahr(aufheben.Ok, "Stummschaltung aufgehoben");
        await Fall("20", a, Sendeziel.Netz(2, 99), false, new() { [c] = (n, [2]), [e] = (n, [2]) }, SendeFehlerCode.UnbekanntesNetz, 99);

        // 23: A verliert Feld 1 (Netz 7) durch eine Umstellung des Admins; A sendet trotzdem an [2, 7].
        var entzug = await z.Client.AnfrageAsync(Nachrichtentyp.Verschieben, SteuerJson.LesenOderWerfen<VerschiebenAnfrage>(System.Text.Encoding.UTF8.GetBytes(new JsonObject
        {
            ["aenderungen"] = new JsonArray { new JsonObject { ["kontoId"] = a.KontoId, ["felderPatch"] = new JsonArray(new JsonObject { ["feld"] = 1, ["leeren"] = true }) } },
        }.ToJsonString())));
        Pruefe.Wahr(entzug.Ok, "Feld 1 von A geleert: " + entzug.Code);
        Pruefe.Wahr(await Warten(() => a.Client.Lage.FeldMitNetz(7) is null, 2000), "A sieht den Entzug");
        await Fall("23", a, Sendeziel.Netz(2, 7), false, new() { [c] = (n, [2]), [e] = (n, [2]) }, SendeFehlerCode.ZielEntzogen, 7);
        var ze = await a.PushAsync<SendeFehler>(s => s.Fehler.Any(x => x.Code == SendeFehlerCode.ZielEntzogen), TimeSpan.FromSeconds(1));
        Pruefe.Wahr(ze!.Teilweise && ze.Fehler.First(x => x.Code == SendeFehlerCode.ZielEntzogen).Durch == z.KontoId, "ZIEL_ENTZOGEN durch den Admin, teilweise");
    }

    /// <summary>F1 mit echten Clients: A spricht an [2, 7], der Admin nimmt A das Netz 7; A's Client setzt nach C4 an [2] fort.</summary>
    [Test]
    public static async Task Kern_C4_UmstellungMittenImSpruch()
    {
        var halter = new ServerHalter();
        await using var t = await Testserver.StartenAsync();
        halter.Server = t;
        await t.AdminOk("netz-setzen", Netz(2, "Kommando"));
        await t.AdminOk("netz-setzen", Netz(7, "Crew"));
        await using var z = await KernFunker.AktivAsync(halter, Discord(20), "zora", "ADMIN", Lokal(20));
        await using var a = await KernFunker.AktivAsync(halter, Discord(21), "anna", "RAIDLEITER", Lokal(21));
        await using var b = await KernFunker.AktivAsync(halter, Discord(22), "bert", "FUNKER", Lokal(22));
        await using var e = await KernFunker.AktivAsync(halter, Discord(25), "emil", "FUNKER", Lokal(25));
        await OkAsync(z.Client.AnfrageAsync(Nachrichtentyp.Verschieben, VerschiebenJson(
            $$"""{"aenderungen":[{"kontoId":{{a.KontoId}},"felder":[{"feld":0,"netzId":2,"hoeren":true,"senden":true},{"feld":1,"netzId":7,"hoeren":true,"senden":true}]},{"kontoId":{{b.KontoId}},"felder":[{"feld":0,"netzId":7,"hoeren":true,"senden":true}]},{"kontoId":{{e.KontoId}},"felder":[{"feld":0,"netzId":7,"hoeren":true,"senden":false},{"feld":1,"netzId":2,"hoeren":true,"senden":false}]}]}""")), "Grundzustand");
        Pruefe.Wahr(await Warten(() => a.Client.Lage.FeldMitNetz(7) is not null && e.Client.Lage.FeldMitNetz(2) is not null && b.Client.Lage.FeldMitNetz(7) is not null, 3000), "Felder angekommen");

        SendenetzGeaendert? hinweis = null;
        a.Client.Sprechen.SendenetzGeaendert += (_, h) => hinweis = h;
        Pruefe.Wahr(a.Client.Sprechen.Beginnen(Sendeziel.Netz(2, 7)), "A beginnt an [2, 7]");
        for (var i = 0; i < 3; i++) a.Client.Sprechen.Senden(KernFunker.Opus);
        Pruefe.Wahr(await e.Empfang.WartenAsync(p => p.Count >= 3, TimeSpan.FromSeconds(2)), "E hört [7, 2]");

        await OkAsync(z.Client.AnfrageAsync(Nachrichtentyp.Verschieben, VerschiebenJson(
            $$"""{"aenderungen":[{"kontoId":{{a.KontoId}},"felderPatch":[{"feld":1,"leeren":true}]}]}""")), "Admin nimmt A das Feld 1");
        Pruefe.Wahr(await Warten(() => hinweis is not null, 2000), "C4: Sendenetz geändert gemeldet");
        Pruefe.Gleich(Sendeziel.Netz(2), hinweis!.Jetzt, "C4: Fortsetzung an [2]");
        Pruefe.Gleich(z.KontoId, hinweis.Durch ?? 0, "C4: durch den Admin");
        Pruefe.Gleich(Sendeziel.Netz(2), a.Client.Sprechen.Ziel, "Phase läuft mit [2]");
        for (var i = 0; i < 3; i++) Pruefe.Wahr(a.Client.Sprechen.Senden(KernFunker.Opus), "weiter senden ohne Unterbrechung");
        a.Client.Sprechen.Beenden();
        await Task.Delay(300);

        var beiE = e.Empfang.Von(a.KontoId);
        var phasen = beiE.Select(p => p.Phase).Distinct().ToList();
        Pruefe.Gleich(2, phasen.Count, "E: zwei Phasen (alte endet, neue setzt fort)");
        var neu = beiE.Where(p => p.Phase == phasen[1] && !p.Ende).OrderBy(p => p.Frame).ToList();
        Pruefe.Wahr(neu.Count == 3 && neu.All(p => p.Netze.SequenceEqual(new ushort[] { 2 })), "E: neue Phase an [2]");
        Pruefe.Wahr(neu[0].NeueZeitlinie && neu[0].Fortsetzung, "C6: Fortsetzung binnen 100 ms nach ENDE, kein Klick");
        Pruefe.Wahr(beiE.Any(p => p.Phase == phasen[0] && p.Ende), "E: ENDE der alten Phase");
        var beiB = b.Empfang.Von(a.KontoId);
        Pruefe.Wahr(beiB.Where(p => !p.Ende).All(p => p.Phase == phasen[0]), "B (Netz 7) bekommt aus der neuen Phase nichts");
        Pruefe.Wahr(beiB.Any(p => p.Ende), "B bekommt das ENDE");
    }

    /// <summary>7.1 und 7.2: geordneter Neustart mit Bye{WARTUNG}, dann harter Ausfall; alle Clients kommen von selbst zurück.</summary>
    [Test]
    public static async Task Kern_WiederverbindenNachServerneustart()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), Guid.NewGuid().ToString("N")[..16]);
        var halter = new ServerHalter();
        var t1 = await Testserver.StartenAsync(verzeichnis: verzeichnis);
        halter.Server = t1;
        await t1.AdminOk("netz-setzen", Netz(2, "Kommando", standard: true));
        var funker = new List<KernFunker>();
        Testserver? t2 = null, t3 = null;
        try
        {
            for (var i = 0; i < 3; i++) funker.Add(await KernFunker.AktivAsync(halter, Discord(30 + i), "wieder" + i, "FUNKER", Lokal(30 + i)));
            var sitzungen = funker.Select(f => f.Client.Lage.SitzungsId).ToList();

            // Geordnet: Bye{WARTUNG, 500}; die Clients warten 500 ms plus 0-2 s, dann Schnellphase alle 2 s.
            halter.Server = null;
            await t1.GeordnetBeendenAsync(500);
            Pruefe.Wahr(await Warten(() => funker.All(f => f.Enden.Any(x => x.Bye == ByeGrund.Wartung && x.WiederInMs == 500)), 3000), "Bye{WARTUNG} kam bei allen an");
            t2 = await Testserver.StartenAsync(verzeichnis: verzeichnis);
            halter.Server = t2;
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            Pruefe.Wahr(await Warten(() => funker.All(f => f.Client.Zustand == ClientZustand.AktivUdp && f.Client.Aktivierungen == 2), 12_000), "alle wieder AKTIV/UDP");
            Console.WriteLine($"HINWEIS Wiederverbinden nach geordnetem Neustart: {uhr.ElapsedMilliseconds} ms (Ziel 7.1: höchstens 10 s)");
            Pruefe.Wahr(funker.Select(f => f.Client.Lage.SitzungsId).Zip(sitzungen).All(x => x.First != 0), "neue Sitzungen");
            Pruefe.Wahr(funker.All(f => f.Client.Lage.Mitgliedschaften?.Quelle == MitgliedschaftsQuelle.Wiederhergestellt), "Mitgliedschaften WIEDERHERGESTELLT (7.1)");
            await SprechenUndHoeren(funker[0], funker[1], funker[2]);

            // Hart: die Server-Seite fällt ohne Bye weg; Erkennung sofort über TCP, Backoff 4 s (hier ohne Streuung 3,2 s).
            halter.Server = null;
            await t2.DienstBeendenAsync();
            halter.HartTrennen();
            Pruefe.Wahr(await Warten(() => funker.All(f => f.Client.Zustand != ClientZustand.AktivUdp), 7000), "Ausfall erkannt");
            t3 = await Testserver.StartenAsync(verzeichnis: verzeichnis);
            halter.Server = t3;
            uhr.Restart();
            Pruefe.Wahr(await Warten(() => funker.All(f => f.Client.Zustand == ClientZustand.AktivUdp && f.Client.Aktivierungen == 3), 15_000), "alle nach hartem Ausfall wieder AKTIV/UDP");
            Console.WriteLine($"HINWEIS Wiederverbinden nach hartem Ausfall: {uhr.ElapsedMilliseconds} ms");
            await SprechenUndHoeren(funker[1], funker[0], funker[2]);
        }
        finally
        {
            foreach (var f in funker) await f.DisposeAsync();
            if (t3 is not null) await t3.DisposeAsync();
            if (t2 is not null) await t2.DisposeAsync();
            await t1.DisposeAsync();
        }
    }

    /// <summary>
    /// 5.12: B hat kein UDP (gesperrt) und schaltet nach 3 s ohne Bindung den Tunnel ein (KEINE_BINDUNG); Sprache geht in
    /// beiden Richtungen über den Steuerkanal. Nach Freigabe von UDP bindet die Probe und B kehrt nach sauberer Probe
    /// zurück (UDP_ZURUECK). Dann fällt UDP bei B aus: Erkennung und Tunnel binnen 4 s (UDP_AUSFALL).
    /// </summary>
    [Test]
    public static async Task Kern_Tunnel_KeineBindungRueckkehrUndAusfall()
    {
        var halter = new ServerHalter();
        await using var t = await Testserver.StartenAsync();
        halter.Server = t;
        await t.AdminOk("netz-setzen", Netz(2, "Kommando", standard: true));
        await using var a = await KernFunker.AktivAsync(halter, Discord(40), "anna", "FUNKER", Lokal(40));
        await using var b = await KernFunker.AktivAsync(halter, Discord(41), "bert", "FUNKER", Lokal(41), udpSperre: true,
            probeAbstand: TimeSpan.FromSeconds(1), probeDauer: TimeSpan.FromSeconds(2), aktivErwarten: false);
        await using var c = await KernFunker.AktivAsync(halter, Discord(42), "carl", "FUNKER", Lokal(42));

        Pruefe.Wahr(await b.Client.WartenAufAsync(z => z == ClientZustand.AktivTunnel, TimeSpan.FromSeconds(5)), "B: 3 s ohne Bindung -> AKTIV/TUNNEL");
        Pruefe.Wahr(b.Client.Tunnel, "B sendet hoch im Tunnel");

        await a.SpruchAsync(Sendeziel.Netz(2), frames: 5);
        Pruefe.Wahr(await b.Empfang.WartenAsync(p => p.Count(x => !x.Ende) >= 5, TimeSpan.FromSeconds(2)), "B hört A über den Tunnel: " + b.Client.Messwerte.Stand() + " Pakete " + b.Empfang.Pakete.Count + " A " + a.Client.Messwerte.Stand() + " Log: " + string.Join(" | ", t.Logbuch.TakeLast(15)));
        Pruefe.Wahr(b.Empfang.Von(a.KontoId).All(p => p.Weg == Verbindungsweg.Tunnel), "runter im Tunnel");
        await b.SpruchAsync(Sendeziel.Netz(2), frames: 5);
        Pruefe.Wahr(await a.Empfang.WartenAsync(p => p.Count(x => x.Absender == b.KontoId && !x.Ende) >= 5, TimeSpan.FromSeconds(2)), "A hört B (hoch im Tunnel, runter per UDP)");
        Pruefe.Wahr(await c.Empfang.WartenAsync(p => p.Count(x => x.Absender == b.KontoId && !x.Ende) >= 5, TimeSpan.FromSeconds(2)), "C hört B");
        var mb = b.Client.Messwerte.Stand();
        Pruefe.Wahr(mb.HochTunnel > 0 && mb.RunterTunnel > 0 && mb.RunterUdp == 0, "B: Zähler hoch und runter im Tunnel, nichts per UDP");

        // UDP wieder frei: Probe bindet (PFAD_PRUEFUNG), 2 s sauber, zurück auf UDP.
        b.Client.UdpSperre = false;
        Pruefe.Wahr(await b.Client.WartenAufAsync(z => z == ClientZustand.AktivUdp, TimeSpan.FromSeconds(10)), "B zurück auf AKTIV/UDP nach sauberer Probe");
        Pruefe.Falsch(b.Client.Tunnel, "B sendet wieder per UDP");
        b.Empfang.Leeren();
        await a.SpruchAsync(Sendeziel.Netz(2), frames: 5);
        Pruefe.Wahr(await b.Empfang.WartenAsync(p => p.Count(x => !x.Ende) >= 5, TimeSpan.FromSeconds(2)), "B hört A wieder per UDP");
        Pruefe.Wahr(b.Empfang.Von(a.KontoId).All(p => p.Weg == Verbindungsweg.Udp), "runter per UDP");

        // UDP fällt aus: kein Echo seit 3 s, 3 Keepalives im Abstand von 100 ms, nach 1 s Tunnel (UDP_AUSFALL).
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        b.Client.UdpSperre = true;
        Pruefe.Wahr(await b.Client.WartenAufAsync(z => z == ClientZustand.AktivTunnel, TimeSpan.FromSeconds(8)), "B erkennt den UDP-Ausfall");
        Console.WriteLine($"HINWEIS UDP-Ausfall erkannt und Tunnel an nach {uhr.ElapsedMilliseconds} ms (Ziel 5.12: 4 s ab Ausfall; Keepalive-Takt 2 s kommt dazu)");
        Pruefe.Wahr(uhr.ElapsedMilliseconds < 6500, "Tunnel binnen Keepalive-Takt plus 4 s");
        b.Empfang.Leeren();
        await a.SpruchAsync(Sendeziel.Netz(2), frames: 5);
        Pruefe.Wahr(await b.Empfang.WartenAsync(p => p.Count(x => !x.Ende) >= 5, TimeSpan.FromSeconds(2)), "B hört A im Tunnel nach dem Ausfall");
    }

    private static async Task SprechenUndHoeren(KernFunker sprecher, params KernFunker[] hoerer)
    {
        foreach (var h in hoerer) h.Empfang.Leeren();
        Pruefe.Wahr(await sprecher.SpruchAsync(Sendeziel.Netz(2), frames: 5), sprecher.Name + " spricht");
        foreach (var h in hoerer)
            Pruefe.Wahr(await h.Empfang.WartenAsync(p => p.Count(x => x.Absender == sprecher.KontoId && !x.Ende) >= 5, TimeSpan.FromSeconds(3)), h.Name + " hört " + sprecher.Name);
    }

    private static async Task OkAsync(Task<Antwort> anfrage, string was)
    {
        var a = await anfrage;
        Pruefe.Wahr(a.Ok, was + ": " + a.Code);
    }

    private static VerschiebenAnfrage VerschiebenJson(string json) => SteuerJson.LesenOderWerfen<VerschiebenAnfrage>(System.Text.Encoding.UTF8.GetBytes(json));

    private static async Task<bool> Warten(Func<bool> bedingung, int ms)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(10);
        }
        return true;
    }
}
