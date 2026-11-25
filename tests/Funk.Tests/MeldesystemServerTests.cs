// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Meldesystem 0.12.0 im Zusammenspiel von Server und Client-Kern (prozessintern, ohne TLS): wer senden darf (Haken, Stufe, Auftrag,
/// Funker-Modus), Push an alle, Regeln auf dem Draht, je Sender eine Meldung in 2 s, Nachlieferung nach Welcome (Zustand, Ampel,
/// Countdown, offene Abfrage, keine vergangenen Befehle), Bereit-Zählung mit kommen und gehen und das Ende nach 2 min mit gestellter
/// Uhr, dazu der Uhrabgleich über Ping und die Textmeldung (M24). Den Haken setzt hier die Konsole (Mitgliederbuch in der Arbeitsschleife); der Weg über die
/// Admin-Sitzung mit Journal steht in <see cref="AdminSitzungTests"/> (Zusammenspiel_Meldungshaken).
/// </summary>
internal static class MeldesystemServerTests
{
    private static IPAddress Lokal(int n) => IPAddress.Parse("127.0.0." + n.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static MeldungSendenAnfrage A(string art, string? motiv = null, byte? groesse = null, long? ziel = null, string? form = null) =>
        new() { Art = art, Motiv = motiv, Groesse = groesse, Ziel = ziel, Form = form };

    /// <summary>Haken wie über die Konsole setzen; danach bekommt das Konto seinen Stand neu (wie bei Mitglied AENDERN).</summary>
    private static Task<bool> Haken(Testserver t, KernFunker f, bool an)
    {
        var z = t.Dienst.Zentrale;
        return z.Ausfuehren(() =>
        {
            Pruefe.Wahr(z.Mitglieder.KontoAendern(z.Konten.Stand, I.Akteur.AdminWerkzeug, f.KontoId, null, null, an).Ok, "Haken für " + f.Name);
            z.KontoGeaendert(f.KontoId);
            z.MelderechtGeaendert(f.KontoId);
            return true;
        });
    }

    /// <summary>Sendet und wartet bei GRENZE (je Sender eine in 2 s) ab, bis es geht; die Grenze selbst prüft <see cref="Rate_ZweiSekundenJeSender"/>.</summary>
    private static async Task<Antwort> Senden(KernFunker f, MeldungSendenAnfrage a)
    {
        for (var i = 0; ; i++)
        {
            var r = await f.Client.AnfrageAsync(Nachrichtentyp.MeldungSenden, a);
            if (r.Code != Fehlercode.Grenze || i == 12) return r;
            await Task.Delay(400);
        }
    }

    /// <summary>Bereit melden; wartet bei GRENZE (BereitMelden 1/s, Stoß 2) ab, bis es geht.</summary>
    private static async Task<Antwort> Bereit(KernFunker f, uint nummer)
    {
        for (var i = 0; ; i++)
        {
            var r = await f.Client.AnfrageAsync(Nachrichtentyp.BereitMelden, new BereitMeldenAnfrage { Nummer = nummer });
            if (r.Code != Fehlercode.Grenze || i == 12) return r;
            await Task.Delay(400);
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

    [Test]
    public static async Task Zusammenspiel_RechtPushUndNachlieferung()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, "920000000000000001", "test", "ADMIN", Lokal(91));
        await using var otto = await KernFunker.AktivAsync(halter, "920000000000000002", "otto", "RAIDLEITER", Lokal(92));
        await using var eli = await KernFunker.AktivAsync(halter, "920000000000000003", "eli", "FUNKER", Lokal(93));
        await using var fritz = await KernFunker.AktivAsync(halter, "920000000000000004", "fritz", "FUNKER", Lokal(94));
        KernFunker[] alle = [test, otto, eli, fritz];
        var befehl = A(Meldungsvorgang.Befehl, "sammeln", 2);

        // Ohne Haken sendet niemand, auch Führung und Admin nicht (M1).
        foreach (var f in alle) Pruefe.Gleich(Fehlercode.KeinRecht, (await Senden(f, befehl)).Code, f.Name + " ohne Haken");
        foreach (var f in alle) await Haken(t, f, true);
        Pruefe.Wahr(await Warten(() => alle.All(f => f.Client.Lage.Konto?.MeldungenSenden == true)), "KontoStand trägt den Haken");

        // Mit Haken: Führung und Admin ja, ein Funker nicht (M16), die Einsatzleitung erst mit laufendem Auftrag (M17).
        foreach (var f in alle) f.PushesLeeren();
        var gut = await Senden(otto, befehl);
        Pruefe.Wahr(gut.Ok, "Führung mit Haken: " + gut.Fehler?.Text);
        foreach (var f in alle)
        {
            var push = await f.PushAsync<Meldestand>(m => m.Anlass == Meldungsvorgang.Befehl && m.Von == otto.KontoId, TimeSpan.FromSeconds(5));
            Pruefe.Wahr(push is { Befehl: { Motiv: "sammeln", Groesse: 2, Von: var von } } && von == otto.KontoId, f.Name + " bekommt den Befehl (an alle, M2)");
        }
        Pruefe.Wahr(otto.Client.Lage.Meldestand is { Befehl: null }, "im Lagebild steht kein Befehl, nur der laufende Stand");
        Pruefe.Wahr((await Senden(test, befehl)).Ok, "Admin mit Haken");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Senden(fritz, befehl)).Code, "Funker mit Haken");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Senden(eli, befehl)).Code, "Einsatzleitung ohne Auftrag");
        await t.AdminOk("auftrag", new JsonObject { ["konto"] = eli.KontoId, ["grund"] = "Probeabend" });
        Pruefe.Wahr((await Senden(eli, befehl)).Ok, "Einsatzleitung mit laufendem Auftrag");

        // "Heute als Funker": der Admin sendet nicht, danach wieder.
        Pruefe.Wahr((await test.Client.AnfrageAsync(Nachrichtentyp.Funkermodus, new FunkermodusAnfrage { An = true })).Ok, "Funker-Modus an");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Senden(test, befehl)).Code, "Admin heute als Funker");
        Pruefe.Wahr((await test.Client.AnfrageAsync(Nachrichtentyp.Funkermodus, new FunkermodusAnfrage { An = false })).Ok, "Funker-Modus aus");
        Pruefe.Wahr((await Senden(test, befehl)).Ok, "Admin wieder");

        // Regeln auf dem Draht: UNGUELTIG mit Klartext, ohne Trennung.
        var art = await Senden(otto, A("Feuer frei"));
        Pruefe.Wahr(art.Code == Fehlercode.Ungueltig && art.Fehler?.Text == "unbekannte Art Feuer frei", "unbekannte Art: " + art.Fehler?.Text);
        var vorbei = await Senden(otto, A(Meldungsvorgang.Countdown, ziel: uhr.GetUtcNow().ToUnixTimeMilliseconds() - 1000, form: "Startzeit"));
        Pruefe.Wahr(vorbei.Code == Fehlercode.Ungueltig && vorbei.Fehler?.Text == "Die Zielzeit ist schon vorbei", "Countdown in der Vergangenheit: " + vorbei.Fehler?.Text);
        var falsch = await Senden(otto, A(Meldungsvorgang.Zustand, "sammeln"));
        Pruefe.Wahr(falsch.Code == Fehlercode.Ungueltig && falsch.Fehler?.Text == "sammeln ist kein Motiv für Zustand", "Motiv der falschen Art");

        // Laufender Stand: Zustand, Ampel, Countdown; wer später kommt, bekommt ihn nach Welcome, ohne vergangene Befehle (M20).
        Pruefe.Wahr((await Senden(otto, A(Meldungsvorgang.Zustand, "position_halten"))).Ok, "Zustand");
        Pruefe.Wahr((await Senden(otto, A(Meldungsvorgang.Ampel, "ampel_gelb_bereithalten", 3))).Ok, "Ampel gelb, dreifach");
        var ziel = uhr.GetUtcNow().ToUnixTimeMilliseconds() + 600_000;
        Pruefe.Wahr((await Senden(otto, A(Meldungsvorgang.Countdown, ziel: ziel, form: "Restzeit"))).Ok, "Countdown 10 min");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Meldestand is { Zustand: not null, Ampel: not null, Countdown: not null }), "Fritz hat den Stand");
        await using var gina = await KernFunker.AktivAsync(halter, "920000000000000005", "gina", "FUNKER", Lokal(95));
        var nach = await gina.PushAsync<Meldestand>(m => m.Anlass == Meldungsvorgang.Stand, TimeSpan.FromSeconds(5));
        Pruefe.Wahr(nach is { Befehl: null, Abfrage: null, Zustand.Motiv: "position_halten", Ampel: { Motiv: "ampel_gelb_bereithalten", Groesse: 3 } }
            && nach.Countdown is { Motiv: "countdown_restzeit", Form: "Restzeit", Ziel: var z } && z == ziel, "Nachlieferung nach Welcome");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.Meldestand?.Countdown?.Ziel == ziel), "Gina hat den Stand im Lagebild");
        Pruefe.Falsch(gina.Pushes.Any(p => p.Body is Meldestand { Befehl: not null }), "kein vergangener Befehl");
        Pruefe.Falsch(gina.Pushes.Any(p => p.Body is BereitStand), "ohne Abfrage kein Bereit-Stand");

        // Haken weg: auch die Führung sendet nicht mehr (M18).
        await Haken(t, otto, false);
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Senden(otto, befehl)).Code, "Führung ohne Haken");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Konto is { MeldungenSenden: null }), "KontoStand ohne Haken");

        // Uhrabgleich über Ping (Ziel etwa 0,1 s); Server und Test laufen auf demselben Rechner, die Wanduhr ist also dieselbe.
        Pruefe.Wahr(await Warten(() => test.Client.Uhr.Stufe == Abgleichsstufe.Ping, 8000), "nach der ersten Ping-Antwort abgeglichen");
        var abweichung = Math.Abs(test.Client.Uhr.ServerJetztMs() - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Console.WriteLine($"Uhrabgleich über Ping: {abweichung} ms daneben, Genauigkeit {test.Client.Uhr.GenauigkeitMs} ms");
        Pruefe.Wahr(abweichung <= 100, "höchstens 0,1 s daneben, war " + abweichung);
    }

    private static MeldungSendenAnfrage Text(string text, string? farbe = null, byte? groesse = null, string? dauer = null) =>
        new() { Art = Meldungsvorgang.Text, Text = text, Farbe = farbe, Groesse = groesse, Dauer = dauer };

    /// <summary>
    /// Textmeldung (M24) über Server und Client-Kern: dasselbe Recht wie alle Meldungen (ohne Haken keiner, ein Funker mit Haken nicht), an alle. "bleibt"
    /// steht im Lagebild und kommt nach Welcome; "8 s" (ab Werk) ersetzt sie, steht nur im Push und wird nicht nachgeliefert; aufheben leert. Farbe und
    /// Dauer nur aus der festen Liste, sonst UNGUELTIG ohne Trennung; 120 Zeichen gehen, 121 sind ZU_GROSS und trennen die Verbindung. Zwischen zwei
    /// Meldungen eines Senders wartet der Test die 2 s ab der Antwort ab wie der Client (keine Verstöße gegen die Rate).
    /// </summary>
    [Test]
    public static async Task Zusammenspiel_Textmeldung()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        await using var otto = await KernFunker.AktivAsync(halter, "920000000000000031", "otto", "RAIDLEITER", Lokal(111));
        await using var fritz = await KernFunker.AktivAsync(halter, "920000000000000032", "fritz", "FUNKER", Lokal(112));
        var letzte = new Dictionary<KernFunker, long>();
        async Task<Antwort> Ruhig(KernFunker f, MeldungSendenAnfrage a)
        {
            if (letzte.TryGetValue(f, out var l) && Fristen.MeldungAbstandMs + 100 - (Environment.TickCount64 - l) is var warten and > 0) await Task.Delay((int)warten);
            var r = await Senden(f, a);
            letzte[f] = Environment.TickCount64;
            return r;
        }
        var hangar = Text("Alle zum Hangar 2", Textmeldungen.Gelb, 2, Textmeldungen.Bleibt);

        // Recht wie bei allen Meldungen (M1, M16).
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Ruhig(otto, hangar)).Code, "Führung ohne Haken");
        await Haken(t, otto, true);
        await Haken(t, fritz, true);
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Ruhig(fritz, hangar)).Code, "Funker mit Haken");

        // Bleibt stehen: an alle, im Lagebild, nach Welcome nachgeliefert.
        otto.PushesLeeren();
        fritz.PushesLeeren();
        Pruefe.Wahr((await Ruhig(otto, hangar)).Ok, "Führung mit Haken: Text, der stehen bleibt");
        foreach (var f in new[] { otto, fritz })
        {
            var push = await f.PushAsync<Meldestand>(m => m.Anlass == Meldungsvorgang.Text, TimeSpan.FromSeconds(5));
            Pruefe.Wahr(push?.Text is { Text: "Alle zum Hangar 2", Farbe: "gelb", Groesse: 2, Dauer: "bleibt", Von: var von } && von == otto.KontoId, f.Name + " bekommt den Text");
        }
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Meldestand?.Text?.Text == "Alle zum Hangar 2"), "der Text steht im Lagebild");
        await using (var gina = await KernFunker.AktivAsync(halter, "920000000000000033", "gina", "FUNKER", Lokal(113)))
        {
            var nach = await gina.PushAsync<Meldestand>(m => m.Anlass == Meldungsvorgang.Stand, TimeSpan.FromSeconds(5));
            Pruefe.Wahr(nach?.Text is { Text: "Alle zum Hangar 2", Dauer: "bleibt" }, "Nachlieferung nach Welcome (M20)");
        }

        // Farbe, Dauer, Text: UNGUELTIG mit Klartext, ohne Trennung und ohne Seiteneffekt.
        foreach (var (anfrage, grund) in new (MeldungSendenAnfrage, string)[]
        {
            (Text("Achtung", "lila"), "unbekannte Farbe lila"),
            (Text("Achtung", dauer: "5min"), "unbekannte Dauer 5min"),
            (Text("   "), "Textmeldung braucht einen Text"),
        })
        {
            var r = await Ruhig(otto, anfrage);
            Pruefe.Wahr(r.Code == Fehlercode.Ungueltig && r.Fehler?.Text == grund, grund + ": " + r.Code + " " + r.Fehler?.Text);
        }
        Pruefe.Wahr(otto.Enden.IsEmpty && fritz.Client.Lage.Meldestand?.Text?.Farbe == "gelb", "nicht getrennt, der Text steht noch");

        // 8 s (ab Werk): ersetzt den stehenden, kommt nur im Push, nicht ins Lagebild und nicht nach Welcome. 120 Zeichen gehen ganz hinaus.
        var lang = string.Concat(Enumerable.Repeat("Achtung, ", 14))[..Grenzen.MeldungTextMax];
        fritz.PushesLeeren();
        Pruefe.Wahr((await Ruhig(otto, Text(lang, Textmeldungen.Rot))).Ok, "120 Zeichen, rot, 8 s");
        var kurz = await fritz.PushAsync<Meldestand>(m => m.Anlass == Meldungsvorgang.Text, TimeSpan.FromSeconds(5));
        Pruefe.Wahr(kurz?.Text is { Farbe: "rot", Groesse: 1, Dauer: "8s", Text: var kt } && kt == lang, "der Text mit 8 s im Push, ganz");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Meldestand is { Text: null }), "nicht im Lagebild; der stehende ist ersetzt");
        await using (var hans = await KernFunker.AktivAsync(halter, "920000000000000034", "hans", "FUNKER", Lokal(114)))
        {
            await Task.Delay(500);
            Pruefe.Falsch(hans.Pushes.Any(p => p.Body is Meldestand), "nichts nachgeliefert: ein Text mit 8 s bleibt nicht stehen, sonst läuft nichts");
        }

        // Ein neuer stehender, dann aufheben.
        Pruefe.Wahr((await Ruhig(otto, Text("Pause bis 21 Uhr", Textmeldungen.Blau, 3, Textmeldungen.Bleibt))).Ok, "neuer stehender Text, blau, dreifach");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Meldestand?.Text is { Farbe: "blau", Groesse: 3 }), "steht bei Fritz");
        fritz.PushesLeeren();
        Pruefe.Wahr((await Ruhig(otto, new MeldungSendenAnfrage { Art = Meldungsvorgang.TextAufheben })).Ok, "aufheben");
        Pruefe.Wahr(await fritz.PushAsync<Meldestand>(m => m.Anlass == Meldungsvorgang.TextAufheben && m.Text is null, TimeSpan.FromSeconds(5)) is not null
            && await Warten(() => fritz.Client.Lage.Meldestand is null or { Text: null }), "aufgehoben, an alle");

        // 121 Zeichen: ZU_GROSS, und die Verbindung ist zu (darum begrenzt der Client hart auf 120).
        await Task.Delay((int)Fristen.MeldungAbstandMs + 100);
        try
        {
            var r = await otto.Client.AnfrageAsync(Nachrichtentyp.MeldungSenden, Text(new string('y', Grenzen.MeldungTextMax + 1)));
            Pruefe.Gleich(Fehlercode.ZuGross, r.Code, "121 Zeichen: ZU_GROSS");
        }
        catch (VerbindungGetrenntException)
        {
            // Kam die Trennung vor dem Fehler an, ist das ebenso richtig.
        }
        Pruefe.Wahr(await Warten(() => !otto.Enden.IsEmpty), "Überlänge trennt die Verbindung");
        Pruefe.Wahr(fritz.Enden.IsEmpty, "die anderen bleiben verbunden");
    }

    [Test]
    public static async Task Rate_ZweiSekundenJeSender()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        await using var otto = await KernFunker.AktivAsync(halter, "920000000000000012", "otto", "RAIDLEITER", Lokal(96));
        await using var ida = await KernFunker.AktivAsync(halter, "920000000000000013", "ida", "RAIDLEITER", Lokal(97));
        await Haken(t, otto, true);
        await Haken(t, ida, true);
        var befehl = A(Meldungsvorgang.Befehl, "los");
        Pruefe.Wahr((await otto.Client.AnfrageAsync(Nachrichtentyp.MeldungSenden, befehl)).Ok, "erste Meldung");
        Pruefe.Gleich(Fehlercode.Grenze, (await otto.Client.AnfrageAsync(Nachrichtentyp.MeldungSenden, A(Meldungsvorgang.AmpelAus))).Code, "zweite gleich danach: GRENZE");
        Pruefe.Wahr((await ida.Client.AnfrageAsync(Nachrichtentyp.MeldungSenden, befehl)).Ok, "ein anderer Sender gleich danach: geht");
        await Task.Delay(2100);
        Pruefe.Wahr((await otto.Client.AnfrageAsync(Nachrichtentyp.MeldungSenden, befehl)).Ok, "nach 2 s wieder");
        Pruefe.Wahr(otto.Client.Zustand is ClientZustand.AktivUdp or ClientZustand.AktivTunnel, "nicht getrennt");
    }

    [Test]
    public static async Task Zusammenspiel_BereitAbfrage()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var otto = await KernFunker.AktivAsync(halter, "920000000000000021", "otto", "RAIDLEITER", Lokal(101));
        await using var fritz = await KernFunker.AktivAsync(halter, "920000000000000022", "fritz", "FUNKER", Lokal(102));
        var gina = await KernFunker.AktivAsync(halter, "920000000000000023", "gina", "FUNKER", Lokal(103));
        await Haken(t, otto, true);
        // Der Haken bei einem Funker wirkt nicht: Fritz bekommt keinen Bereit-Stand.
        await Haken(t, fritz, true);
        KernFunker[] alle = [otto, fritz, gina];
        uint[] Sortiert(params KernFunker[] f) => [.. f.Select(x => x.KontoId).Order()];

        foreach (var f in alle) f.PushesLeeren();
        Pruefe.Wahr((await Senden(otto, A(Meldungsvorgang.BereitAbfrage, groesse: 2))).Ok, "Bereit-Abfrage gestartet");
        uint nummer = 0;
        foreach (var f in alle)
        {
            var m = await f.PushAsync<Meldestand>(x => x.Anlass == Meldungsvorgang.BereitAbfrage, TimeSpan.FromSeconds(5));
            Pruefe.Wahr(m is { Abfrage: { Motiv: "bereit_melden", Groesse: 2, Nummer: not null } }, f.Name + " sieht die Abfrage");
            nummer = m!.Abfrage!.Nummer!.Value;
        }
        var stand = await otto.PushAsync<BereitStand>(b => b.Offen, TimeSpan.FromSeconds(5));
        // Otto hat gefragt und zählt nicht mit, weder in m noch unter "fehlt" (Schritt 2 vom 16.10.2026).
        Pruefe.Wahr(stand is { Bereit: 0, Online: 2 } && stand.Nummer == nummer && stand.Fehlen.SequenceEqual(Sortiert(fritz, gina)), "Leitung: 0 von 2, es fehlen Fritz und Gina");
        await Task.Delay(300);
        Pruefe.Falsch(fritz.Pushes.Any(p => p.Body is BereitStand) || gina.Pushes.Any(p => p.Body is BereitStand), "Funker bekommen keinen Bereit-Stand");

        // Fritz meldet bereit (zweimal ändert nichts), mit falscher Nummer UNBEKANNT.
        otto.PushesLeeren();
        Pruefe.Wahr((await Bereit(fritz, nummer)).Ok, "Fritz bereit");
        var eins = await otto.PushAsync<BereitStand>(b => b.Bereit == 1, TimeSpan.FromSeconds(5));
        Pruefe.Wahr(eins is { Online: 2 } && eins.Fehlen.SequenceEqual([gina.KontoId]), "1 von 2, es fehlt Gina");
        Pruefe.Wahr((await Bereit(fritz, nummer)).Ok, "zweimal ist wie einmal");
        var falsch = await Bereit(fritz, nummer + 7);
        Pruefe.Gleich(Fehlercode.Unbekannt, falsch.Code, "falsche Nummer");

        // Gina geht, Hans kommt: die Zahl folgt den Online-Sitzungen; Hans bekommt die offene Abfrage nach Welcome.
        otto.PushesLeeren();
        await gina.DisposeAsync();
        var zwei = await otto.PushAsync<BereitStand>(b => b.Online == 1, TimeSpan.FromSeconds(5));
        Pruefe.Wahr(zwei is { Bereit: 1 } && zwei.Fehlen.Count == 0, "Gina weg: 1 von 1");
        otto.PushesLeeren();
        await using var hans = await KernFunker.AktivAsync(halter, "920000000000000024", "hans", "FUNKER", Lokal(104));
        var nachgeliefert = await hans.PushAsync<Meldestand>(m => m.Anlass == Meldungsvorgang.Stand, TimeSpan.FromSeconds(5));
        Pruefe.Wahr(nachgeliefert?.Abfrage?.Nummer == nummer, "Hans bekommt die offene Abfrage (M20)");
        var drei = await otto.PushAsync<BereitStand>(b => b.Online == 2, TimeSpan.FromSeconds(5));
        Pruefe.Wahr(drei is { Bereit: 1 } && drei.Fehlen.SequenceEqual([hans.KontoId]), "Hans da: 1 von 2");
        Pruefe.Wahr((await Bereit(hans, nummer)).Ok, "Hans bereit");

        // Nach 2 min von selbst zu: an alle ohne Abfrage, die Leitung bekommt den letzten Stand.
        foreach (var f in new[] { otto, fritz, hans }) f.PushesLeeren();
        uhr.Vor(TimeSpan.FromSeconds(121));
        foreach (var f in new[] { otto, fritz, hans })
        {
            var m = await f.PushAsync<Meldestand>(x => x.Anlass == Meldungsvorgang.BereitAbgelaufen, TimeSpan.FromSeconds(5));
            Pruefe.Wahr(m is { Abfrage: null }, f.Name + ": abgelaufen");
        }
        var zu = await otto.PushAsync<BereitStand>(b => !b.Offen, TimeSpan.FromSeconds(5));
        Pruefe.Wahr(zu is { Bereit: 2, Online: 2 } && zu.Nummer == nummer && zu.Fehlen.Count == 0, "Leitung: zu, 2 von 2");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Meldestand is { Abfrage: null }), "Lagebild ohne Abfrage");
        Pruefe.Gleich(Fehlercode.Unbekannt, (await Bereit(fritz, nummer)).Code, "danach UNBEKANNT");

        // Eine neue Abfrage schließt die Leitung selbst.
        Pruefe.Wahr((await Senden(otto, A(Meldungsvorgang.BereitAbfrage))).Ok, "neue Abfrage");
        var neu = await otto.PushAsync<BereitStand>(b => b.Offen && b.Nummer != nummer, TimeSpan.FromSeconds(5));
        Pruefe.Wahr(neu is { Bereit: 0, Online: 2 }, "neue Nummer, niemand bereit");
        otto.PushesLeeren();
        Pruefe.Wahr((await Senden(otto, A(Meldungsvorgang.BereitSchliessen))).Ok, "geschlossen");
        Pruefe.Wahr(await otto.PushAsync<Meldestand>(m => m.Anlass == Meldungsvorgang.BereitSchliessen && m.Abfrage is null, TimeSpan.FromSeconds(5)) is not null, "an alle: zu");
        Pruefe.Wahr(await otto.PushAsync<BereitStand>(b => !b.Offen && b.Nummer == neu!.Nummer, TimeSpan.FromSeconds(5)) is not null, "Leitung: zu");
    }
}
