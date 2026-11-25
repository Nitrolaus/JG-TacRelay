// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Modelle hinter dem Hauptfenster: Audio (A11, A14), Netze und Rollen (A4, A15, A22), Navigation und Kopfzeile (A25). Seit 0.10.0 sind Audio und
/// Meine Kanäle keine eigenen Seiten mehr (Q87); ihre Modelle stehen auf Start und unter Einstellungen in den Feinheiten.
/// </summary>
internal static class OberflaecheBereicheTests
{
    private static AudioViewModel Audio(Aufbau a) => a.Audio();

    [Test]
    public static async Task Audio_GeraeteStartUndWechsel()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Audio.EingangId = "attrappe-ein-2";
        var vm = Audio(a);
        Pruefe.Gleich(2, vm.Eingaenge.Count, "Eingänge der Maschine");
        Pruefe.Gleich("attrappe-ein-2", vm.Eingang?.Id, "gespeichertes Gerät gewählt");
        Pruefe.Gleich("attrappe-aus-1", vm.Ausgang?.Id, "sonst das Standardgerät");
        Pruefe.Falsch(vm.StoppenCommand.CanExecute(null), "Stoppen erst im Lauf");
        await vm.StartenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Laeuft && a.Maschine.Laeuft, "läuft");
        Pruefe.Gleich("attrappe-ein-2", a.Maschine.EingangId, "mit dem gewählten Eingang");
        Pruefe.Falsch(vm.StartenCommand.CanExecute(null), "Starten gesperrt im Lauf");

        vm.Eingang = vm.Eingaenge[0];
        var bis = DateTime.UtcNow.AddSeconds(5);
        while (a.Maschine.Starts < 2 && DateTime.UtcNow < bis) await Task.Delay(5);
        Pruefe.Gleich(1, a.Maschine.Stopps, "Gerätewechsel stoppt");
        Pruefe.Gleich(2, a.Maschine.Starts, "und startet neu");
        Pruefe.Gleich("attrappe-ein-1", a.Maschine.EingangId, "mit dem neuen Gerät");
        Pruefe.Gleich("attrappe-ein-1", a.Ablage.Aktuell.Audio.EingangId, "gemerkt");
        await vm.StoppenCommand.ExecuteAsync(null);
        Pruefe.Falsch(vm.Laeuft || a.Maschine.Laeuft, "gestoppt");

        // Seit 0.10.0: Die Liste folgt dem Geräteereignis von Windows (der Knopf "Geräte neu lesen" entfällt), auch ohne laufendes Audio. Fehlt das
        // gemerkte Gerät, steht der Standard in der Auswahl, die Wahl in der Datei bleibt; kommt es zurück, ist es wieder gewählt.
        var starts = a.Maschine.Starts;
        var headset = new AudioGeraet("usb-headset", "Headset (USB)", false);
        a.Maschine.GeraeteSetzen([new("attrappe-ein-1", "Mikrofon (Attrappe)", true), headset]);
        Pruefe.Wahr(vm.Eingaenge.Any(g => g.Id == "usb-headset") && vm.Eingaenge.Count == 2, "neues Gerät in der Liste: " + string.Join(", ", vm.Eingaenge.Select(g => g.Id)));
        Pruefe.Gleich("attrappe-ein-1", vm.Eingang?.Id, "die gewählte Kennung bleibt gewählt");
        vm.Eingang = headset;
        Pruefe.Gleich("usb-headset", a.Ablage.Aktuell.Audio.EingangId, "Headset gewählt und gemerkt");
        a.Maschine.GeraeteSetzen([new("attrappe-ein-1", "Mikrofon (Attrappe)", true)]);
        Pruefe.Gleich("attrappe-ein-1", vm.Eingang?.Id, "Headset abgezogen: der Standard in der Auswahl");
        Pruefe.Gleich("usb-headset", a.Ablage.Aktuell.Audio.EingangId, "die gemerkte Wahl bleibt");
        Pruefe.Gleich(starts, a.Maschine.Starts, "ohne laufendes Audio kein Neustart durch das Ereignis");
        a.Maschine.GeraeteSetzen([new("attrappe-ein-1", "Mikrofon (Attrappe)", true), headset]);
        Pruefe.Gleich("usb-headset", vm.Eingang?.Id, "zurück: wieder gewählt");
    }

    /// <summary>Verarbeitung und Pegel. Bis 0.9.3 dazu "Sprechtaste halten" im Bereich Audio; das entfällt seit 0.10.0 (Endfassung #26).</summary>
    [Test]
    public static void Audio_VerarbeitungUndPegel()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Audio.Echounterdrueckung = false;
        var vm = Audio(a);
        Pruefe.Falsch(a.Maschine.Echounterdrueckung, "gespeicherter Wert an die Maschine");
        vm.RauschfilterAus = true;
        Pruefe.Gleich(Rauschfilter.Aus, a.Maschine.Rauschfilter, "Rauschfilter aus");
        Pruefe.Falsch(a.Ablage.Aktuell.Audio.Rauschunterdrueckung, "gemerkt (für ältere Clients wie bisher)");
        vm.Echounterdrueckung = true;
        Pruefe.Wahr(a.Maschine.Echounterdrueckung, "Echounterdrückung an");

        a.Maschine.Melden(new PegelMeldung(5, -24, -61, -52, 0.93f, true));
        vm.Takt();
        Pruefe.Gleich(-24.0, vm.PegelDbfs, "Pegel");
        Pruefe.Gleich(-52.0, vm.SchwelleDb, "Schwellenmarke");
        Pruefe.Gleich("SENDET", vm.SendetText, "sendet");
    }

    /// <summary>Klangstufe B: drei Knöpfe, gemerkt in zwei Feldern (ältere Clients lesen weiter "Rauschunterdrückung"), Rückfall einmal gemeldet.</summary>
    [Test]
    public static void Audio_Rauschfilter_DreiKnoepfeGemerktUndRueckfallGemeldet()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Audio.StarkerRauschfilter = true;
        var vm = Audio(a);
        Pruefe.Gleich(Rauschfilter.Stark, a.Maschine.Rauschfilter, "gespeichertes Stark an die Maschine");
        Pruefe.Wahr(vm.RauschfilterStark && !vm.RauschfilterStandard && !vm.RauschfilterAus, "Knopf Stark gedrückt");
        Pruefe.Gleich("DeepFilterNet3 wirkt.", vm.RauschfilterHinweis, "Hinweis");

        var meldungen = new List<string>();
        vm.RauschfilterRueckfall += meldungen.Add;
        a.Maschine.RauschfilterRueckfall = "Stark brauchte mehrmals länger als die 10 ms.";
        vm.Takt();
        vm.Takt();
        Pruefe.Gleich("Stark brauchte mehrmals länger als die 10 ms.", vm.RauschfilterHinweis, "Grund unter den Knöpfen");
        Pruefe.Gleich(1, meldungen.Count, "Rückfall einmal gemeldet (für die Meldezeile)");

        vm.RauschfilterStandard = true;
        Pruefe.Gleich(Rauschfilter.Standard, a.Maschine.Rauschfilter, "Standard an die Maschine");
        Pruefe.Wahr(a.Ablage.Aktuell.Audio is { Rauschunterdrueckung: true, StarkerRauschfilter: false }, "gemerkt: an, nicht stark");
        Pruefe.Gleich("Der Standardfilter wirkt.", vm.RauschfilterHinweis, "Hinweis Standard (seit der Prüfung 0.9.3 ohne \"keine Effekte\", die Klangfarbe darunter ist einer)");
        vm.RauschfilterStark = true;
        Pruefe.Wahr(a.Ablage.Aktuell.Audio is { Rauschunterdrueckung: true, StarkerRauschfilter: true }, "gemerkt: an und stark");
        vm.RauschfilterAus = true;
        Pruefe.Wahr(a.Ablage.Aktuell.Audio is { Rauschunterdrueckung: false, StarkerRauschfilter: false }, "gemerkt: aus");
        Pruefe.Gleich("Kein Filter.", vm.RauschfilterHinweis, "Hinweis Aus");
    }

    [Test]
    public static void Audio_Ausloesung_MeldetNurBeimWechsel()
    {
        using var a = new Aufbau();
        var vm = Audio(a);
        var meldungen = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AudioViewModel.AusloesungText)) meldungen++;
        };
        for (var i = 0; i < 30; i++) vm.Takt();
        Pruefe.Gleich(1, meldungen, "eine Sekunde Bildtakt ohne Wechsel: nur die erste Meldung");
        a.Maschine.Sprachaktivierung = !a.Maschine.Sprachaktivierung;
        vm.Takt();
        vm.Takt();
        Pruefe.Gleich(2, meldungen, "Wechsel wird einmal gemeldet");
    }

    [Test]
    public static async Task Netze_EinsteigenMitBelegungUndVorschau()
    {
        using var a = new Aufbau();
        var vm = new NetzeViewModel(a.Dienst, a.Maschine, a.Takt);
        Pruefe.Gleich("Kapitän auf Schiff 1", vm.AktuellerPlatz, "jetziger Platz");
        Pruefe.Gleich(6, vm.Rollen.Count, "Rollen");
        Pruefe.Gleich(3, vm.Schiffe.Count, "Schiffe");
        vm.GewaehlteRolle = vm.Rollen.Single(r => r.Name == "Gunner");
        Pruefe.Wahr(vm.SchiffNoetig, "Gunner braucht ein Schiff");
        Pruefe.Gleich("Durchsagen, Crew", vm.NetzVorschau, "Vorschau der Netze (A22)");
        Pruefe.Gleich("3/7 belegt, Gunner 1/3", vm.Schiffe[0].Belegung, "Belegung je Schiff (A15)");
        Pruefe.Falsch(vm.Schiffe[0].Voll, "noch Platz");
        vm.GewaehltesSchiff = vm.Schiffe[1];
        Pruefe.Wahr(vm.EinsteigenCommand.CanExecute(null), "Einsteigen möglich");
        await vm.EinsteigenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Einsteigen 3 2"), "Rolle und Schiff in einem Schritt: " + string.Join(" | ", a.Dienst.Aufrufe));
        Pruefe.Gleich("Gunner auf Schiff 2", vm.AktuellerPlatz, "neuer Platz");

        vm.GewaehlteRolle = vm.Rollen.Single(r => r.Name == "Kapitän");
        Pruefe.Wahr(vm.Schiffe[0].Voll && vm.Schiffe[1].Voll && !vm.Schiffe[2].Voll, "CO-Plätze auf Schiff 1 und 2 belegt");
        vm.GewaehlteRolle = vm.Rollen.Single(r => r.Name == "Jäger");
        Pruefe.Falsch(vm.SchiffNoetig, "Jäger ohne Schiff");
        await vm.EinsteigenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Einsteigen 5 -"), "ohne Schiff");
        vm.GewaehlteRolle = vm.Rollen.Single(r => r.Name == "Raidleitung");
        Pruefe.Falsch(vm.EinsteigenCommand.CanExecute(null), "nicht selbst wählbar");

        a.Dienst.LageSetzen(l => l with { Selbstwahl = false });
        vm.GewaehlteRolle = vm.Rollen.Single(r => r.Name == "Pilot");
        vm.GewaehltesSchiff = vm.Schiffe[2];
        Pruefe.Falsch(vm.EinsteigenCommand.CanExecute(null), "Selbstwahl gesperrt: Raidleiter teilt ein");
    }

    [Test]
    public static void Netze_FelderUndPegelJeFunker()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel() with { FunkerPegel = new Dictionary<uint, float> { [3] = -4.5f } });
        var vm = new NetzeViewModel(a.Dienst, a.Maschine, a.Takt);
        Pruefe.Wahr(a.Maschine.FunkerPegel.Contains((3u, -4.5f)), "gespeicherter Pegel an die Maschine (A4)");
        Pruefe.Gleich(9, vm.Felder.Count, "neun Felder");
        var crew = vm.Felder.Single(f => f.Netz == "Crew");
        Pruefe.Wahr(crew.Gewaehlt && crew.Hoeren, "Crew gewählt und hört");
        var jaeger = vm.Felder.Single(f => f.Netz == "Jäger");
        jaeger.Hoeren = true;
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Hoeren 6 an"), "Hören über den Dienst");
        Pruefe.Falsch(vm.Funker.Any(f => f.Name == "Test"), "eigenes Konto nicht in der Liste");
        var kobold = vm.Funker.Single(f => f.Name == "Kobold");
        Pruefe.Gleich(-4.5, kobold.PegelDb, "gespeicherter Wert im Regler");
        Pruefe.Gleich("Gunner, Schiff 1", kobold.Platz, "Platz");
        Pruefe.Gleich("offline", vm.Funker.Single(f => f.Name == "Eule").Platz, "offline");
        var falke = vm.Funker.Single(f => f.Name == "Falke");
        falke.PegelDb = -6.2;
        Pruefe.Gleich(-6.0, falke.PegelDb, "auf 0,5 dB gerundet");
        Pruefe.Wahr(a.Maschine.FunkerPegel.Contains((2u, -6f)), "an die Maschine");
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Pegel 2 -6.0"), "am Konto gespeichert: " + string.Join(" | ", a.Dienst.Aufrufe));
        var vorher = a.Maschine.FunkerPegel.Count;
        a.Dienst.LageSetzen(l => l);
        Pruefe.Gleich(vorher, a.Maschine.FunkerPegel.Count, "unveränderte Pegel nicht erneut");

        a.Dienst.NaechsterFehler = "Festes Feld, nur der Raidleiter ändert es.";
        crew.Hoeren = false;
        Pruefe.Wahr(vm.Hinweis?.StartsWith("Crew:", StringComparison.Ordinal) == true, "Ablehnung mit Netz: " + vm.Hinweis);
    }

    [Test]
    public static void Haupt_NavigationKopfzeileUndTakt()
    {
        using var a = new Aufbau();
        var h = a.Haupt(out var funkfeld, out var schwelle, out var audio, out var netze);
        var einstellungen = (EinstellungenViewModel)Seiten.Einstellungen(h).Inhalt;
        // Seit 0.10.0 (E1, E2): Start und Einstellungen; Strg+3 und Strg+4 gibt es nicht mehr.
        Pruefe.Gleich("Start, Einstellungen", string.Join(", ", h.Bereiche.Select(b => b.Titel)), "zwei Bereiche");
        Pruefe.Gleich("Strg+1, Strg+2", string.Join(", ", h.Bereiche.Select(b => b.Taste)), "ihre Tasten");
        Pruefe.Gleich(HauptViewModel.StartKennung, h.AktiverBereich.Kennung, "Start auf der Startseite");
        h.BereichWaehlenCommand.Execute("2");
        Pruefe.Wahr(ReferenceEquals(einstellungen, h.AktiverBereich.Inhalt), "Strg+2: Einstellungen");
        foreach (var n in new[] { "3", "4", "9" })
        {
            h.BereichWaehlenCommand.Execute(n);
            Pruefe.Wahr(ReferenceEquals(einstellungen, h.AktiverBereich.Inhalt), "Strg+" + n + " ändert nichts");
        }
        h.BereichWaehlenCommand.Execute("1");
        Pruefe.Wahr(ReferenceEquals(h.Start, h.AktiverBereich.Inhalt), "Strg+1: Start");
        h.BereichWaehlenCommand.Execute("2");
        // Seit Klangstufe C1 (Q25) gilt der Bereich nur in der Sitzung; bis dahin stand hier "Bereich gemerkt" ("netze" in der Datei).
        Pruefe.Wahr(a.Ablage.Aktuell.Bereich != HauptViewModel.EinstellungenKennung, "Q25: der Bereich wird nicht in die Datei gemerkt");
        Pruefe.Gleich("Verbunden", h.StatusText, "Status");
        Pruefe.Gleich("spricht: Falke, Kobold", h.Sprechend, "Sprecher in der Kopfzeile");
        h.FunkfeldZeigenCommand.Execute(null);
        Pruefe.Gleich(1, a.Fenster.Funkfeld, "Funkfeld angefordert");

        a.Maschine.Melden(new PegelMeldung(1, -20, -60, -50, 0.9f, true));
        h.Takt();
        Pruefe.Gleich(-20.0, audio.PegelDbfs, "Takt erreicht Audio");
        Pruefe.Gleich(-20.0, schwelle.PegelDbfs, "Takt erreicht Schwelle");
        Pruefe.Gleich(-20.0, h.Statuszeile.PegelDb, "Takt erreicht die Statuszeile (0.10.0)");
        Pruefe.Wahr(funkfeld.Kacheln.Any(k => k.EigeneSendung), "Takt erreicht das Funkfeld");

        // Funkfeld 5 und 10 stehen seit 0.10.0 nur im Funkfeld (#74), nicht mehr unter Einstellungen.
        einstellungen.FunkfeldBeimStart = false;
        Pruefe.Falsch(a.Ablage.Aktuell.Funkfeld.BeimStartZeigen, "Startverhalten gemerkt");

        var zweites = new HauptViewModel(h.Verbindung, audio, netze, einstellungen, funkfeld, schwelle, a.Dienst, a.Fenster, a.Ablage, a.Takt, "");
        // Q25 (Klangstufe C1): Jeder Start beginnt auf der Startseite; bis dahin "beim nächsten Start derselbe Bereich" (netze).
        Pruefe.Gleich(HauptViewModel.StartKennung, zweites.AktiverBereich.Kennung, "Q25: beim nächsten Start die Startseite");
    }

    /// <summary>
    /// Seit 0.10.0 (E2, E3): Strg+T öffnet die Seite Sprachaktivierung und legt den Fokus auf die Grafik, kein eigenes Fenster und kein
    /// Aufklappen unten mehr (bis 0.9.3 klappte es den Bereich unter der Leiste auf und zu). Ist "Senden mit Stimme" aus, bewirkt es nichts; ist
    /// es an, öffnet es die Seite auch ohne Unterpunkt (Befund der Prüfung), der dann da steht, solange sie vorn ist. In der Fußzeile steht Strg+T
    /// nicht mehr.
    /// </summary>
    [Test]
    public static void Haupt_StrgT_OeffnetDieSeite_OhneUnterpunktWirkungslos()
    {
        using var a = new Aufbau();
        var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
        var fokus = 0;
        h.SchwelleFokussieren += () => fokus++;
        Pruefe.Wahr(h.Navigation.Any(b => b.Kennung == HauptViewModel.SprachaktivierungKennung), "Beispiel: der Unterpunkt ist da");
        Pruefe.Wahr(h.SprachaktivierungEinstellenCommand.CanExecute(null), "mit Unterpunkt geht Strg+T");

        h.SprachaktivierungEinstellenCommand.Execute(null);
        Pruefe.Wahr(h.IstSprachaktivierungsseite, "Strg+T öffnet die Seite");
        Pruefe.Gleich(1, fokus, "und die Grafik bekommt den Fokus");
        h.SprachaktivierungEinstellenCommand.Execute(null);
        Pruefe.Wahr(h.IstSprachaktivierungsseite && fokus == 2, "noch einmal: bleibt auf der Seite, wieder der Fokus");
        Pruefe.Gleich(0, a.Fenster.Funkfeld, "kein Fenster geöffnet");

        // Klangmodul Sprachaktivierung aus: Kein Kanal sendet mit Stimme, den Unterpunkt gibt es nicht, Strg+T bewirkt nichts.
        audio.SprachaktivierungAn = false;
        Pruefe.Falsch(funkfeld.SprachaktivierungBenutzt, "nur noch Sprechtaste");
        Pruefe.Gleich(HauptViewModel.EinstellungenKennung, h.AktiverBereich.Kennung, "die Seite verschwand: es geht zu Einstellungen (E3)");
        Pruefe.Falsch(h.SprachaktivierungEinstellenCommand.CanExecute(null), "ohne Unterpunkt gesperrt");
        h.AktiverBereich = h.Bereiche[0];
        h.SprachaktivierungEinstellenCommand.Execute(null);
        Pruefe.Wahr(ReferenceEquals(h.AktiverBereich, h.Bereiche[0]) && fokus == 2, "ohne Unterpunkt: nichts");
        Pruefe.Falsch(h.Tastenhinweis.Contains("Strg+T", StringComparison.Ordinal), "Strg+T steht nicht in der Fußzeile (E2)");

        // Befund der Prüfung: "Senden mit Stimme" an, aber gerade sendet kein Kanal mit Sprachaktivierung (getrennt). Strg+T öffnet die Seite
        // trotzdem, damit sie sich vor dem Verbinden einstellen lässt (bis 0.9.3 ging das jederzeit); der Unterpunkt steht da, solange sie vorn ist.
        audio.SprachaktivierungAn = true;
        a.Dienst.LageSetzen(_ => FunkLage.Leer);
        bool Unterpunkt() => h.Navigation.Any(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
        Pruefe.Wahr(!funkfeld.SprachaktivierungBenutzt && !Unterpunkt(), "getrennt: nicht benutzt, kein Unterpunkt");
        Pruefe.Wahr(h.SprachaktivierungEinstellenCommand.CanExecute(null), "Senden mit Stimme an: Strg+T geht");
        h.SprachaktivierungEinstellenCommand.Execute(null);
        Pruefe.Wahr(h.IstSprachaktivierungsseite && fokus == 3, "die Seite ist offen, die Grafik hat den Fokus");
        Pruefe.Wahr(Unterpunkt() && h.Navigation.Contains(h.AktiverBereich), "der Unterpunkt steht da und ist gewählt");
        h.AktiverBereich = h.Bereiche[0];
        Pruefe.Falsch(Unterpunkt(), "Seite verlassen: der Unterpunkt geht wieder");
        h.SprachaktivierungEinstellenCommand.Execute(null);
        audio.SprachaktivierungAn = false;
        Pruefe.Wahr(h.AktiverBereich.Kennung == HauptViewModel.EinstellungenKennung && !Unterpunkt(), "Senden mit Stimme aus: zu Einstellungen, ohne Unterpunkt");
        Pruefe.Falsch(h.SprachaktivierungEinstellenCommand.CanExecute(null), "und Strg+T gesperrt");
        funkfeld.Dispose();
    }

    [Test]
    public static void Haupt_A26_KeinEigenesFensterFuerDieSchwelle()
    {
        var fenster = typeof(Funk.Client.App).Assembly.GetTypes()
            .Where(t => typeof(System.Windows.Window).IsAssignableFrom(t))
            .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        // Seit S2 dazu die Meldezeile (RAID-S2-ENTWURF Abschnitt 3): ein eigenes kleines Fenster am Funkfeld, nie aktiviert. Die
        // Schwelle hat weiterhin keins.
        // Seit 0.11.5 dazu Wer ist da als ablösbares Fenster (Sarevoc 14.10.2026: "wenn möglich auch als Popup und eigenes Fenster ablösbar").
        // Seit 0.11.9 dazu die Sprecheranzeige über dem Spiel, nach dem Muster der Meldezeile, nie aktiviert.
        Pruefe.Gleich("FunkfeldFenster, Hauptfenster, MeldeanzeigeFenster, MeldezeileFenster, SprecheranzeigeFenster, WerIstDaFenster", string.Join(", ", fenster),
            "Hauptfenster, Funkfeld, Meldezeile, Sprecheranzeige, Meldungen und Wer ist da (A24, A25, A26, S2, 0.11.5, 0.11.9, 0.12.0)");
    }
}
