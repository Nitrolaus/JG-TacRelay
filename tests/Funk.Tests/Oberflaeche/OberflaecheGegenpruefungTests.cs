// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Befunde der Gegenprüfungen am Bereich Verbindung und am Dienst (Modul feinschliff), je mit Regressionstest:
/// Hintergrundfehler nur über den Oberflächentakt und mit passendem Text, der Pin-Alarm beim Wiederverbinden sagt, dass weiter
/// versucht wird, die Profilprüfung auf Anlass (Verbinden, Aktivieren, Zeigen) statt nur beim Start, und der abgelaufene
/// Registrierungscode. Ohne Netz, ohne Fenster: Attrappen und ein Client, dessen Aufbau hängt oder gleich scheitert.
/// </summary>
internal static class OberflaecheGegenpruefungTests
{
    /// <summary>Ein Aufbau, der bis zum Beenden des Clients hängt: Der Client steht auf "Verbinde...", ohne Netz.</summary>
    private static async Task<Steuerverbindung> Haengen(CancellationToken abbruch)
    {
        await Task.Delay(Timeout.Infinite, abbruch).ConfigureAwait(false);
        throw new InvalidOperationException("nie erreicht");
    }

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

    [Test]
    public static async Task Hintergrundfehler_UeberDenTakt_NeubauNurBeiNeuemText_UndAbloesung()
    {
        // Befund: Jeder Hintergrundfehler hieß "Interner Fehler beim Verbinden", auch mitten in der Sitzung, und baute die Lage
        // im Thread des Fehlers neu, bei einem Fehler je Paket also je Paket im Empfangs-Thread neben dem Spiel.
        var takt = new WarteschlangenTakt();
        FunkClient? client = null;
        using var id = Geraeteidentitaet.Fluechtig();
        // Die Frist muss länger sein als jeder Schritt unten, der einen stehenden Fehler erwartet: Neben einem Spiel läuft der
        // Testlauf mit niedriger Priorität, und 400 ms zwischen zwei Schritten kommen dort vor (Gegenprüfung feinschliff).
        await using var d = new FunkClientDienst(() =>
        {
            client = new FunkClient(new FunkClientOptionen { Aufbau = new DelegatAufbau((_, abbruch) => Haengen(abbruch)), Identitaet = id, Zufall = () => 0.0 });
            return new FunkAnbindung(client, null, "Meine Staffel", id.Fingerabdruck.Anzeige, "Software");
        }, takt: takt, fehlerDauer: TimeSpan.FromSeconds(2));
        await d.VerbindenAsync();
        takt.Abarbeiten();
        var c = client!;
        var bauten = 0;
        d.LageGeaendert += () => Interlocked.Increment(ref bauten);

        // 2000 Fehler aus einem anderen Thread, wie aus dem Empfangs-Thread bei einem Fehler je Paket.
        await Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++) c.FehlerMelden("Empfangs-Thread", new InvalidOperationException("Paket kaputt"));
        });
        Pruefe.Gleich(0, Volatile.Read(ref bauten), "im Thread des Fehlers keine Lage gebaut");
        Pruefe.Gleich(1, takt.Anzahl, "2000 Fehler, eine Übergabe an den Oberflächentakt");
        takt.Abarbeiten();
        Pruefe.Gleich(1, Volatile.Read(ref bauten), "ein Neubau für den neuen Text");
        // Der Satz sagt die Lage mit Fehlernummer, Ort und Ausnahmetext stehen nur in der Einzelheit (Q5).
        Pruefe.Wahr(d.Lage.Hinweis is { } h1 && h1.StartsWith("Interner Fehler beim Verbinden.", StringComparison.Ordinal)
            && h1.EndsWith("(Fehler " + Fehlernummern.InternVerbinden + ")", StringComparison.Ordinal), "Text, solange der Client verbindet: " + d.Lage.Hinweis);
        Pruefe.Gleich("Empfangs-Thread: Paket kaputt", d.Lage.HinweisEinzelheit, "Ort und Ausnahmetext als Einzelheit");
        Pruefe.Gleich(Verbindungsstatus.Verbinden, d.Lage.Status, "Status bleibt Verbinde...");

        // Derselbe Fehler noch einmal: kein Neubau, er bleibt nur länger stehen. Ein anderer (andere Einzelheit): genau einer.
        for (var i = 0; i < 100; i++) c.FehlerMelden("Empfangs-Thread", new InvalidOperationException("Paket kaputt"));
        takt.Abarbeiten();
        Pruefe.Gleich(1, Volatile.Read(ref bauten), "gleicher Text, kein Neubau");
        c.FehlerMelden("Takt", new InvalidOperationException("anders"));
        takt.Abarbeiten();
        Pruefe.Gleich(2, Volatile.Read(ref bauten), "neuer Fehler, ein Neubau");
        Pruefe.Gleich("Takt: anders", d.Lage.HinweisEinzelheit, "die neue Einzelheit");
        Pruefe.Falsch(d.Lage.Hinweis!.Contains("anders", StringComparison.Ordinal), "nicht im Satz: " + d.Lage.Hinweis);

        // Ohne Nachschub verschwindet er nach der Frist, auch das über den Takt.
        Pruefe.Wahr(await Warten(() =>
        {
            takt.Abarbeiten();
            return d.Lage.Hinweis is null;
        }, 10_000), "nach der Frist weg: " + d.Lage.Hinweis);

        // Ein Erfolg löst ab: Ein Fehler von davor, der erst danach im Takt ankommt, erscheint nicht mehr.
        c.FehlerMelden("Leseschleife", new InvalidOperationException("alt"));
        c.ZustandMelden(ClientZustand.Getrennt, ClientZustand.Registrierung, "Test");
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Registrierung, 5_000), "Registrierung, war " + d.Lage.Status);
        takt.Abarbeiten();
        Pruefe.Gleich(null, d.Lage.Hinweis, "vom Erfolg abgelöst");

        // Je Zustand der passende Text.
        c.FehlerMelden("CodeErhalten", new InvalidOperationException("x"));
        takt.Abarbeiten();
        Pruefe.Gleich(FunkClientDienst.Fehleranzeige(ClientZustand.Registrierung, "CodeErhalten: x"), new Verbindungsmeldung(d.Lage.Hinweis!, d.Lage.HinweisEinzelheit),
            "in der Anmeldung");
        Pruefe.Wahr(d.Lage.Hinweis!.StartsWith("Interner Fehler bei der Anmeldung.", StringComparison.Ordinal), d.Lage.Hinweis);
        c.ZustandMelden(ClientZustand.Registrierung, ClientZustand.AktivUdp, "Test");
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Aktiv && d.Lage.Hinweis is null, 5_000), "AKTIV löst ab: " + d.Lage.Hinweis);
        c.FehlerMelden("Empfangs-Thread", new InvalidOperationException("y"));
        takt.Abarbeiten();
        var imClient = FunkClientDienst.Fehleranzeige(ClientZustand.AktivUdp, "Empfangs-Thread: y");
        Pruefe.Wahr(imClient.Text.StartsWith("Interner Fehler im Client.", StringComparison.Ordinal), imClient.Text);
        Pruefe.Gleich(imClient, new Verbindungsmeldung(d.Lage.Hinweis!, d.Lage.HinweisEinzelheit), "mitten in der Sitzung");
        // Ein Wechsel innerhalb von AKTIV (UDP zu Tunnel) ist kein neuer Erfolg.
        c.ZustandMelden(ClientZustand.AktivUdp, ClientZustand.AktivTunnel, "Test");
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.AktivTunnel, 5_000), "Tunnel");
        takt.Abarbeiten();
        Pruefe.Gleich(imClient, new Verbindungsmeldung(d.Lage.Hinweis!, d.Lage.HinweisEinzelheit), "bleibt im Tunnel stehen");

        // Trennen: der eigene Hinweis, der Fehler ist weg.
        await d.TrennenAsync();
        takt.Abarbeiten();
        Pruefe.Gleich("Getrennt", d.Lage.Hinweis, "nach dem Trennen");
        Pruefe.Gleich(null, d.Lage.HinweisEinzelheit, "ohne Einzelheit");
        foreach (var z in Enum.GetValues<ClientZustand>())
        {
            var m = FunkClientDienst.Fehleranzeige(z, "Ort: Meldung");
            var t = m.Text;
            Pruefe.Wahr(t.StartsWith("Interner Fehler ", StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.IsMatch(t, @" \(Fehler 4\d\)$")
                && !t.Contains("Ort", StringComparison.Ordinal), z + ": " + t);
            Pruefe.Gleich("Ort: Meldung", m.Einzelheit, z + ": Ort und Meldung als Einzelheit");
            Pruefe.Falsch(t.Any(zeichen => zeichen is '\u2013' or '\u2014' or '\u201E' or '\u201C' or '\u201D' or '\u2026'), z + ": nur einfache Zeichen");
        }
    }

    [Test]
    public static async Task PinAlarm_BeimWiederverbinden_SagtDassWeiterVersuchtWird()
    {
        // Befund: Beim Wiederverbinden (7.1 Schritt 4, 2 min ohne gültigen Pin) versucht die Schleife weiter, der Text sagte
        // aber "verbindet nicht" wie bei der Erstverbindung (4.1, harter Abbruch).
        var erst = Verbindungstexte.Aufbau(AufbauFehler.PinAlarm, "Pin passt nicht an 203.0.113.7:8443").Text;
        var wiederMeldung = Verbindungstexte.Aufbau(AufbauFehler.PinAlarm, "2 min lang kein gültiger Pin, zuletzt 203.0.113.7:8443", wiederverbindung: true);
        var wieder = wiederMeldung.Text;
        Pruefe.Wahr(erst.StartsWith("Server nicht vertrauenswürdig", StringComparison.Ordinal) && erst.Contains("verbindet der Client nicht", StringComparison.Ordinal)
            && !erst.Contains("von selbst", StringComparison.Ordinal), "Erstverbindung: harter Abbruch: " + erst);
        // Der Neuversuch steht vor der Fehlernummer; Adresse und Port nur in der Einzelheit, der Weg über den Admin und ein neues Setup.
        Pruefe.Wahr(wieder.Contains(Verbindungstexte.NeuerVersuch + " (Fehler " + Fehlernummern.UnbekannterSchluessel + ")", StringComparison.Ordinal)
            && wieder.Contains("unbekannten Schlüssel", StringComparison.Ordinal)
            && wieder.Contains("Admin", StringComparison.Ordinal) && wieder.Contains("neues Setup", StringComparison.Ordinal)
            && !wieder.Contains("203.0.113.7", StringComparison.Ordinal)
            && !wieder.Contains("verbindet der Client nicht", StringComparison.Ordinal), "Wiederverbinden: " + wieder);
        Pruefe.Gleich("2 min lang kein gültiger Pin, zuletzt 203.0.113.7:8443", wiederMeldung.Einzelheit, "Einzelheit für die Details");
        Pruefe.Falsch(wieder.Any(z => z is '\u2013' or '\u2014' or '\u201E' or '\u201C' or '\u201D' or '\u2026'), "nur einfache Zeichen");
        Pruefe.Gleich(wiederMeldung, Verbindungstexte.Ende(new Sitzungsende("Aufbau: PinAlarm", null, null, null, null, null, AufbauFehler.PinAlarm,
            "2 min lang kein gültiger Pin, zuletzt 203.0.113.7:8443", Wiederverbindung: true)), "über das Ende der Sitzung");
        Pruefe.Gleich(Verbindungstexte.Aufbau(AufbauFehler.Netz, "x"), Verbindungstexte.Aufbau(AufbauFehler.Netz, "x", wiederverbindung: true),
            "andere Arten unverändert");

        // Im Dienst mit dem echten Client: Die erste Sitzung endet sofort, das Wiederverbinden trifft auf den Pin-Alarm.
        FunkClient? client = null;
        var versuche = new List<bool>();
        using var id = Geraeteidentitaet.Fluechtig();
        await using var d = new FunkClientDienst(() =>
        {
            client = new FunkClient(new FunkClientOptionen
            {
                Aufbau = new DelegatAufbau((wiederverbindung, abbruch) =>
                {
                    int n;
                    lock (versuche)
                    {
                        versuche.Add(wiederverbindung);
                        n = versuche.Count;
                    }
                    return n switch
                    {
                        1 => Task.FromResult(new Steuerverbindung(Stream.Null, IPAddress.Loopback, IPAddress.Loopback)),
                        2 => Task.FromException<Steuerverbindung>(new AufbauException(AufbauFehler.PinAlarm, "2 min lang kein gültiger Pin, zuletzt 203.0.113.7:8443")),
                        _ => Haengen(abbruch),
                    };
                }),
                Identitaet = id,
                Zufall = () => 0.0,
            });
            return new FunkAnbindung(client, null, "Meine Staffel", id.Fingerabdruck.Anzeige, "Software");
        });
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Hinweis is { } h && h.StartsWith("Verbindung verloren", StringComparison.Ordinal), 5_000), "erste Sitzung endet: " + d.Lage.Hinweis);
        // Nicht auf den Backoff warten.
        client!.NeuVerbinden();
        Pruefe.Wahr(await Warten(() => d.Lage.Hinweis is { } h && h.Contains("unbekannten Schlüssel", StringComparison.Ordinal), 5_000), "Pin-Alarm: " + d.Lage.Hinweis);
        Pruefe.Wahr(d.Lage.Hinweis!.Contains(Verbindungstexte.NeuerVersuch, StringComparison.Ordinal), "sagt, dass es weitergeht");
        Pruefe.Gleich("2 min lang kein gültiger Pin, zuletzt 203.0.113.7:8443", d.Lage.HinweisEinzelheit, "die Einzelheit kommt in der Lage an");
        Pruefe.Falsch(client.NeuversuchGesperrt, "die Schleife ist nicht gesperrt");
        Pruefe.Gleich(Verbindungsstatus.Verbinden, d.Lage.Status, "Verbinde..., nicht Getrennt");
        lock (versuche) Pruefe.Wahr(versuche.Count >= 2 && !versuche[0] && versuche[1], "erst Erstverbindung, dann Wiederverbinden: " + string.Join(",", versuche));
        Pruefe.Wahr(client.LetztesEnde is { Aufbau: AufbauFehler.PinAlarm, Wiederverbindung: true }, "das Ende trägt die Wiederverbindung");
    }

    [Test]
    public static async Task Profilpruefung_BeimVerbindenAktivierenUndZeigen_NichtImTakt()
    {
        // Befund: Die Prüfung lief nur beim Start und bei Änderungen im Bereich. Fehlte profil.json beim Start oder wurde die
        // Datei ersetzt, blieb der alte Befund stehen und Verbinden gesperrt. Seit Stufe 1 liegt das Profil im Programmordner
        // (eingebaut, Profilquelle); dasselbe gilt, wenn es dort fehlt oder ein Reparieren es ersetzt.
        using var k = new OberflaecheProfilTests.Schluessel();
        using var fremd = new OberflaecheProfilTests.Schluessel();
        using var a = new Aufbau(FunkLage.Leer);
        var profil = a.Ordner.Datei("profil.json");
        a.Profil = new Profilquelle(profil, [k.Spki]);
        a.Ablage.Aktuell.Bereich = "verbindung";
        var h = a.Haupt(out _, out _, out _, out _);
        var vm = h.Verbindung;
        Pruefe.Falsch(vm.Bereit || vm.VerbindenCommand.CanExecute(null), "Profil fehlt im Programmordner: kein Verbinden");
        Pruefe.Wahr(vm.ProfilOffen is { } f && f.Contains("gibt es nicht", StringComparison.Ordinal), "sagt es: " + vm.ProfilOffen);

        // Stick eingesteckt. Der Bildtakt fragt nicht ab (kein Dauerabfragen), und das Aktivieren nur, wenn der Bereich zu
        // sehen ist ...
        OberflaecheProfilTests.ProfilSchreiben(profil, k);
        for (var i = 0; i < 10; i++) h.Takt();
        Pruefe.Falsch(vm.Bereit, "ohne Anlass der alte Befund");
        h.AktiverBereich = Seiten.Einstellungen(h);
        h.FensterAktiviert();
        Pruefe.Falsch(vm.Bereit, "Einstellungen vorn: nicht geprüft");
        h.AktiverBereich = h.Bereiche[0];
        Pruefe.Wahr(vm.Bereit, "beim Zeigen geprüft: " + vm.WasFehlt);
        File.Delete(profil);
        Pruefe.Wahr(vm.Bereit, "abgezogen, noch der alte Befund");
        // ... das Aktivieren des Hauptfensters mit dem Bereich Verbindung prüft neu.
        h.FensterAktiviert();
        Pruefe.Falsch(vm.Bereit, "nach dem Aktivieren: Stick fehlt wieder");
        OberflaecheProfilTests.ProfilSchreiben(profil, k);
        h.FensterAktiviert();
        Pruefe.Wahr(vm.Bereit && vm.VerbindenCommand.CanExecute(null) && vm.ProfilFehler is null, "nach dem Aktivieren bereit: " + vm.WasFehlt);

        // Datei ersetzt (mit einem anderen Profilschlüssel signiert): Zeigen des Bereichs prüft neu.
        OberflaecheProfilTests.ProfilSchreiben(profil, fremd);
        h.AktiverBereich = Seiten.Einstellungen(h);
        h.AktiverBereich = h.Bereiche[0];
        Pruefe.Wahr(!vm.Bereit && vm.ProfilFehler!.Contains("passt zu keinem", StringComparison.Ordinal), "beim Zeigen: " + vm.ProfilFehler);

        // Wieder gut und geprüft; dann Stick abgezogen, der Knopf steht noch auf bereit. Der Klick prüft selbst.
        OberflaecheProfilTests.ProfilSchreiben(profil, k);
        h.FensterAktiviert();
        Pruefe.Wahr(vm.VerbindenCommand.CanExecute(null), "bereit");
        File.Delete(profil);
        await vm.VerbindenCommand.ExecuteAsync(null);
        Pruefe.Falsch(a.Dienst.Aufrufe.Contains("Verbinden"), "ohne Datei verbindet der Klick nicht");
        Pruefe.Wahr(!vm.Bereit && vm.ZeigtWasFehlt && vm.WasFehlt!.Contains("Serverprofil", StringComparison.Ordinal), "und sagt, was fehlt: " + vm.WasFehlt);
        Pruefe.Falsch(vm.VerbindenCommand.CanExecute(null), "Knopf gesperrt");
        OberflaecheProfilTests.ProfilSchreiben(profil, k);
        h.FensterAktiviert();
        await vm.VerbindenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Verbinden"), "mit Datei verbindet er");
    }

    [Test]
    public static async Task Registrierungscode_AbgelaufenSagtEsUndHoltEinenNeuen()
    {
        // Befund: Nach drei abgelaufenen Codes kommt keiner mehr (3 je Verbindung, 3.4); die Karte zeigte weiter nur
        // "Gültig bis" mit einer Uhrzeit in der Vergangenheit.
        var jetzt = new DateTimeOffset(2026, 9, 29, 21, 40, 0, TimeSpan.Zero);
        var bis = jetzt.AddMinutes(5);
        using var a = new Aufbau(FunkLage.Leer with
        {
            Status = Verbindungsstatus.Registrierung,
            Registrierung = new Registrierungsangabe("ABCD-EFGH-JKMN", "/funk verknuepfen", bis),
        });
        var vm = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, jetzt: () => jetzt);
        var uhr = bis.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
        Pruefe.Wahr(vm.IstRegistrierung && !vm.CodeAbgelaufen, "gültig");
        Pruefe.Gleich("Gültig bis " + uhr + " Uhr, nur für dieses Gerät.", vm.CodeGueltig, "wie bisher");
        Pruefe.Falsch(vm.NeuenCodeCommand.CanExecute(null), "gültig: nichts zu holen");

        // Knapp abgelaufen: Die ersten Ersatzcodes holt der Client selbst, gleich; die Karte sagt noch nichts.
        jetzt = bis.AddSeconds(2);
        vm.Takt();
        Pruefe.Falsch(vm.CodeAbgelaufen, "in der Nachfrist");
        jetzt = bis + VerbindungViewModel.CodeNachfrist + TimeSpan.FromSeconds(1);
        vm.Takt();
        Pruefe.Wahr(vm.CodeAbgelaufen, "abgelaufen, kein neuer gekommen");
        Pruefe.Gleich("Abgelaufen um " + uhr + " Uhr.", vm.CodeGueltig, "die Zeile sagt es");
        var t = vm.CodeAbgelaufenText;
        Pruefe.Wahr(t.Contains("abgelaufen", StringComparison.Ordinal) && t.Contains("höchstens drei", StringComparison.Ordinal)
            && t.Contains("Neuen Code holen", StringComparison.Ordinal) && t.Contains("Trennen und Verbinden", StringComparison.Ordinal), t);
        Pruefe.Falsch(t.Any(z => z is '\u2013' or '\u2014' or '\u201E' or '\u201C' or '\u201D' or '\u2026'), "nur einfache Zeichen");

        // Weitere Bildtakte schreiben nichts neu (30 je Sekunde).
        var gemeldet = new List<string?>();
        vm.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);
        for (var i = 0; i < 30; i++) vm.Takt();
        Pruefe.Gleich(0, gemeldet.Count, "Takt ohne Wechsel: " + string.Join(",", gemeldet));

        // Neuen Code holen: verbindet einmal neu, und bis der neue da ist, steht der Knopf still.
        Pruefe.Wahr(vm.NeuenCodeCommand.CanExecute(null), "Knopf bereit");
        await vm.NeuenCodeCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Verbinden"), "Verbinden an den Dienst (mit laufender Sitzung: neu verbinden)");
        Pruefe.Falsch(vm.NeuenCodeCommand.CanExecute(null), "nicht zweimal");
        Pruefe.Wahr(vm.CodeAbgelaufenText.Contains("wird geholt", StringComparison.Ordinal), vm.CodeAbgelaufenText);
        a.Dienst.LageSetzen(_ => FunkLage.Leer with
        {
            Status = Verbindungsstatus.Registrierung,
            Registrierung = new Registrierungsangabe("QRST-VWXY-Z234", "/funk verknuepfen", jetzt.AddMinutes(5)),
        });
        Pruefe.Wahr(!vm.CodeAbgelaufen && vm.Code == "QRST-VWXY-Z234" && vm.CodeGueltig!.StartsWith("Gültig bis", StringComparison.Ordinal), "der neue Code");

        // Im Programm läuft die Uhr über den Bildtakt des Hauptfensters. 2 s Rest in der Nachfrist, nicht weniger: Aufbau und
        // Hauptmodell brauchen neben einem Spiel bei niedriger Priorität mehr als 300 ms (Gegenprüfung feinschliff).
        using var b = new Aufbau(FunkLage.Leer with
        {
            Status = Verbindungsstatus.Registrierung,
            Registrierung = new Registrierungsangabe("ABCD-EFGH-JKMN", "/funk verknuepfen", DateTimeOffset.UtcNow - VerbindungViewModel.CodeNachfrist + TimeSpan.FromSeconds(2)),
        });
        var h = b.Haupt(out _, out _, out _, out _);
        Pruefe.Falsch(h.Verbindung.CodeAbgelaufen, "noch in der Nachfrist");
        Pruefe.Wahr(await Warten(() =>
        {
            h.Takt();
            return h.Verbindung.CodeAbgelaufen;
        }, 10_000), "der Bildtakt merkt den Ablauf");
    }
}
