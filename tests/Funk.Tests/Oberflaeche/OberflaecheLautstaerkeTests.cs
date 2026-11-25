// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json;
using Funk.Client.Ablage;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Automatische Lautstärke (Klangstufe A) im Bereich Audio: gespeichert unter <c>audio</c> in vier Feldern, die nachsichtig gelesen
/// werden (ein falscher Wert gilt als fehlend, die Datei bleibt); eine Datei von 0.5.1 liest sich mit den Voreinstellungen. Das Modell
/// gibt den Schalter sofort an die Maschine, übernimmt ein Einmessen, merkt die gelernte Verstärkung für die nächste Sitzung und
/// schaltet das Klangprotokoll in den Ordner der Einstellungen.
/// </summary>
internal static class OberflaecheLautstaerkeTests
{
    [Test]
    public static void Datei_Voreinstellung_NachsichtigUndAltLesbar()
    {
        using var o = new Testordner();
        var pfad = o.Datei("client-einstellungen.json");
        var speicher = new EinstellungsSpeicher(pfad);
        var e = speicher.Laden();
        Pruefe.Wahr(e.Audio is { AutomatischeLautstaerke: true, FesteVerstaerkungDb: null, GelernteVerstaerkungDb: null, Klangprotokoll: false },
            "ab Werk: automatisch, nichts eingemessen, nichts gelernt, kein Protokoll");
        Pruefe.Gleich(new LautstaerkeEinstellung(true, Pegelstufe.StartDb), e.Audio.LautstaerkeFuerMaschine(), "für die Maschine");

        // Falsche Werte in den neuen Feldern legen die Datei nicht beiseite; der Rest der Datei gilt.
        var faelle = new (string Json, bool Automatisch, double? Fest, double? Gelernt, bool Protokoll)[]
        {
            ("\"automatischeLautstaerke\": \"ja\", \"festeVerstaerkungDb\": \"zwölf\", \"gelernteVerstaerkungDb\": [1], \"klangprotokoll\": 7", true, null, null, false),
            ("\"automatischeLautstaerke\": 0, \"festeVerstaerkungDb\": 99, \"gelernteVerstaerkungDb\": -50.04, \"klangprotokoll\": \"true\"", false, Pegelstufe.MaxDb, Pegelstufe.MinDb, true),
            ("\"automatischeLautstaerke\": null, \"festeVerstaerkungDb\": \"7.26\", \"gelernteVerstaerkungDb\": \"NaN\", \"klangprotokoll\": { \"a\": 1 }", true, 7.3, null, false),
            ("\"automatischeLautstaerke\": false, \"festeVerstaerkungDb\": 4.5, \"gelernteVerstaerkungDb\": null, \"klangprotokoll\": true", false, 4.5, null, true),
        };
        foreach (var f in faelle)
        {
            File.WriteAllText(pfad, "{ \"audio\": { \"rauschunterdrueckung\": false, " + f.Json + " }, \"farbstellung\": \"Heute\" }");
            var l = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, f.Json);
            Pruefe.Wahr(l.Audio.AutomatischeLautstaerke == f.Automatisch && l.Audio.FesteVerstaerkungDb == f.Fest
                && l.Audio.GelernteVerstaerkungDb == f.Gelernt && l.Audio.Klangprotokoll == f.Protokoll,
                $"{f.Json}: {l.Audio.AutomatischeLautstaerke} {l.Audio.FesteVerstaerkungDb} {l.Audio.GelernteVerstaerkungDb} {l.Audio.Klangprotokoll}");
            Pruefe.Falsch(l.Audio.Rauschunterdrueckung, "der Rest der Datei gilt");
            Pruefe.Gleich(Funk.Client.Design.Farbstellung.Anthrazit, l.Farbstellung, "Farbstellung: das alte P1 wird Anthrazit");
        }
        Pruefe.Gleich(0, Directory.GetFiles(o.Pfad, "*.defekt-*").Length, "nichts beiseitegelegt");

        // Fest: der eingemessene Wert, sonst der gelernte; automatisch: der gelernte als Startwert, sonst der eingemessene.
        var a = new AudioEinstellungen { AutomatischeLautstaerke = false, GelernteVerstaerkungDb = 9 };
        Pruefe.Gleich(new LautstaerkeEinstellung(false, 9f), a.LautstaerkeFuerMaschine(), "fest ohne Einmessen: gelernt");
        a.FesteVerstaerkungDb = 3;
        Pruefe.Gleich(new LautstaerkeEinstellung(false, 3f), a.LautstaerkeFuerMaschine(), "fest: eingemessen");
        a.AutomatischeLautstaerke = true;
        Pruefe.Gleich(new LautstaerkeEinstellung(true, 9f, Gelernt: true), a.LautstaerkeFuerMaschine(), "automatisch: setzt beim gelernten fort");
        a.GelernteVerstaerkungEingang = "mic-a";
        Pruefe.Gleich(new LautstaerkeEinstellung(true, 9f, Gelernt: true, Eingang: "mic-a"), a.LautstaerkeFuerMaschine(), "mit dem Mikrofon, auf dem gelernt wurde");
        a.GelernteVerstaerkungDb = null;
        Pruefe.Gleich(new LautstaerkeEinstellung(true, 3f, Gelernt: true), a.LautstaerkeFuerMaschine(), "automatisch ohne Gelerntes: ab dem eingemessenen");

        // Das Mikrofon der gelernten Verstärkung (Gegenprüfung Klangstufe A): nachsichtig, ohne gelernten Wert bedeutungslos.
        foreach (var (inhalt, erwartet) in new (string, string?)[]
        {
            ("\"gelernteVerstaerkungDb\": 5, \"gelernteVerstaerkungEingang\": \"mic-x\"", "mic-x"),
            ("\"gelernteVerstaerkungDb\": 5, \"gelernteVerstaerkungEingang\": 12", null),
            ("\"gelernteVerstaerkungDb\": null, \"gelernteVerstaerkungEingang\": \"mic-x\"", null),
        })
        {
            File.WriteAllText(pfad, "{ \"audio\": { " + inhalt + " } }");
            var l = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, inhalt);
            Pruefe.Gleich(erwartet, l.Audio.GelernteVerstaerkungEingang, inhalt);
        }

        // Eine echte Datei von 0.5.1 (ohne die Felder) liest sich mit den Voreinstellungen; geschrieben stehen die Felder als Zahl,
        // Schalter oder null.
        File.Copy(Path.Combine(EinrichtungTests.Repo(), "tests", "Funk.Tests", "Daten", "einstellungen-0.5.1", "client-einstellungen.json"), pfad, true);
        var alt = speicher.Laden();
        Pruefe.Gleich(null, speicher.LetzterFehler, "0.5.1");
        Pruefe.Wahr(alt.Audio is { AutomatischeLautstaerke: true, FesteVerstaerkungDb: null, Klangprotokoll: false }, "0.5.1: Voreinstellungen");
        alt.Audio.FesteVerstaerkungDb = 5.5;
        speicher.Speichern(alt);
        using var json = JsonDocument.Parse(File.ReadAllBytes(pfad));
        var audio = json.RootElement.GetProperty("audio");
        Pruefe.Gleich(5.5, audio.GetProperty("festeVerstaerkungDb").GetDouble(), "geschrieben");
        Pruefe.Gleich(JsonValueKind.Null, audio.GetProperty("gelernteVerstaerkungDb").ValueKind, "nichts gelernt: null");
        Pruefe.Gleich(JsonValueKind.True, audio.GetProperty("automatischeLautstaerke").ValueKind, "Schalter");
    }

    /// <summary>
    /// Rauschfilter (Klangstufe B) in der Datei: Stark ist ein eigenes Feld neben "rauschunterdrueckung", damit ältere Clients weiter
    /// RNNoise an- oder ausschalten; nachsichtig gelesen, und der Rauschfilter selbst steht nicht in der Datei.
    /// </summary>
    [Test]
    public static void Datei_Rauschfilter_StarkAlsEigenesFeld_Nachsichtig()
    {
        using var o = new Testordner();
        var pfad = o.Datei("client-einstellungen.json");
        var speicher = new EinstellungsSpeicher(pfad);
        Pruefe.Gleich(Rauschfilter.Standard, speicher.Laden().Audio.Rauschfilter, "ab Werk Standard");
        foreach (var (inhalt, erwartet) in new (string, Rauschfilter)[]
        {
            ("\"rauschunterdrueckung\": true, \"starkerRauschfilter\": true", Rauschfilter.Stark),
            ("\"rauschunterdrueckung\": false, \"starkerRauschfilter\": true", Rauschfilter.Aus),
            ("\"rauschunterdrueckung\": true, \"starkerRauschfilter\": \"ja\"", Rauschfilter.Standard),
            ("\"rauschunterdrueckung\": false", Rauschfilter.Aus),
        })
        {
            File.WriteAllText(pfad, "{ \"audio\": { " + inhalt + " } }");
            var l = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, inhalt);
            Pruefe.Gleich(erwartet, l.Audio.Rauschfilter, inhalt);
        }
        Pruefe.Gleich(0, Directory.GetFiles(o.Pfad, "*.defekt-*").Length, "nichts beiseitegelegt");

        var e = speicher.Laden();
        e.Audio.Rauschfilter = Rauschfilter.Stark;
        speicher.Speichern(e);
        using var json = JsonDocument.Parse(File.ReadAllBytes(pfad));
        var audio = json.RootElement.GetProperty("audio");
        Pruefe.Gleich(JsonValueKind.True, audio.GetProperty("rauschunterdrueckung").ValueKind, "ältere Clients lesen: an");
        Pruefe.Gleich(JsonValueKind.True, audio.GetProperty("starkerRauschfilter").ValueKind, "stark");
        Pruefe.Falsch(audio.TryGetProperty("rauschfilter", out _), "der abgeleitete Wert steht nicht in der Datei");
    }

    [Test]
    public static async Task Modell_SchalterUndEinmessen()
    {
        using var a = new Aufbau();
        var l = new LautstaerkeViewModel(a.Maschine, a.Ablage);
        Pruefe.Gleich(new LautstaerkeEinstellung(true, Pegelstufe.StartDb), a.Maschine.Lautstaerke, "beim Anlegen an die Maschine");
        Pruefe.Gleich(null, a.Maschine.KlangprotokollOrdner, "kein Protokoll");

        // Ausschalten ohne Einmessen: Es gilt, was die Automatik gerade hat (hier der Startwert), damit nichts springt.
        a.Maschine.Telemetrie = new TelemetrieStand(500, 0, 10_000, [], default(TelemetrieEintrag) with { PegelstufeDb = 13.27f });
        l.Automatisch = false;
        Pruefe.Gleich(new LautstaerkeEinstellung(false, 13.3f), a.Maschine.Lautstaerke, "fest mit dem Wert der Automatik");
        Pruefe.Gleich(13.3, a.Ablage.Aktuell.Audio.FesteVerstaerkungDb, "gemerkt");
        Pruefe.Wahr(l.FestText.Contains(13.3.ToString("+0.0", CultureInfo.CurrentCulture), StringComparison.Ordinal), l.FestText);

        // Ohne Mikrofon gesperrt, mit Grund.
        Pruefe.Falsch(l.EinmessenCommand.CanExecute(null), "ohne Audio gesperrt");
        Pruefe.Wahr(l.EinmessenGesperrt is { Length: > 0 }, "mit Grund");
        await a.Maschine.StartenAsync(null, null, CancellationToken.None);
        l.Takt();
        Pruefe.Wahr(l.EinmessenCommand.CanExecute(null), "mit Mikrofon frei");
        Pruefe.Gleich(null, l.EinmessenGesperrt, "kein Grund");
        l.EinmessenCommand.Execute(null);
        Pruefe.Gleich(EinmessZustand.Laeuft, a.Maschine.Einmessen.Zustand, "an die Maschine");
        Pruefe.Gleich("Abbrechen", l.EinmessKnopf, "der Knopf bricht jetzt ab");
        Pruefe.Wahr(l.EinmessText.StartsWith("Sprich jetzt. Noch 8 s", StringComparison.Ordinal), l.EinmessText);

        a.Maschine.Einmessen = new EinmessStand(EinmessZustand.Fertig, 0, 11.3f);
        l.Takt();
        Pruefe.Gleich(11.3, a.Ablage.Aktuell.Audio.FesteVerstaerkungDb, "Ergebnis übernommen");
        // Seit Klangstufe C1 gilt ein eingemessener fester Wert als gelernte Stimme (die Mindest-Funklautstärke wirkt dann).
        Pruefe.Gleich(new LautstaerkeEinstellung(false, 11.3f, Gelernt: true), a.Maschine.Lautstaerke, "und gilt sofort, als gelernt");
        Pruefe.Wahr(a.Ablage.Aktuell.Audio.FestEingemessen, "als eingemessen gemerkt");
        Pruefe.Wahr(l.EinmessText.StartsWith("Eingemessen", StringComparison.Ordinal), l.EinmessText);
        // Seit 0.10.0 (Befund der Prüfung, Vorlage m-5) mit vollem Namen: Daneben steht das Einmessen der Mindest-Funklautstärke.
        Pruefe.Gleich("Feste Verstärkung einmessen", l.EinmessKnopf, "Knopf wieder zum Einmessen");
        a.Ablage.Aktuell.Audio.FesteVerstaerkungDb = 2;
        l.Takt();
        Pruefe.Gleich(2.0, a.Ablage.Aktuell.Audio.FesteVerstaerkungDb, "ein Ergebnis wird nur einmal übernommen");

        a.Maschine.Einmessen = new EinmessStand(EinmessZustand.ZuWenigSprache, 0, 0f);
        l.Takt();
        Pruefe.Wahr(l.EinmessText.StartsWith("Zu wenig Sprache", StringComparison.Ordinal), l.EinmessText);

        // Wieder an: ab dem gemerkten Wert.
        a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb = 7;
        l.Automatisch = true;
        Pruefe.Gleich(new LautstaerkeEinstellung(true, 7f, Gelernt: true), a.Maschine.Lautstaerke, "automatisch: setzt beim gelernten Wert fort");
        a.Ablage.Sichern();
        Pruefe.Wahr(a.Speicher.Laden().Audio is { AutomatischeLautstaerke: true, FesteVerstaerkungDb: 2 }, "gespeichert");

        // 0.10.0 (Befund der Prüfung, m-5): Mit Automatik gilt die feste Verstärkung nicht; Einmessen ist dann gesperrt, auch mit Mikrofon.
        a.Maschine.Einmessen = new EinmessStand(EinmessZustand.Bereit, 0, 0f);
        l.Takt();
        Pruefe.Falsch(l.EinmessenCommand.CanExecute(null), "mit Automatik gesperrt");
        Pruefe.Gleich(LautstaerkeViewModel.NurOhneAutomatik, l.EinmessenGesperrt, "mit dem Grund aus m-5");
        Pruefe.Gleich("Nur ohne Automatik nötig.", l.EinmessHinweisText, "darunter der Satz aus m-5");
        l.Automatisch = false;
        Pruefe.Wahr(l.EinmessenCommand.CanExecute(null) && l.EinmessenGesperrt is null, "Automatik aus: frei");
        Pruefe.Gleich(LautstaerkeViewModel.EinmessHinweis, l.EinmessHinweisText, "darunter, was zu tun ist");
        await a.Maschine.StoppenAsync();
        l.Takt();
        Pruefe.Gleich(LautstaerkeViewModel.OhneMikrofon, l.EinmessenGesperrt, "ohne Mikrofon: Audio auf Start starten");
    }

    [Test]
    public static async Task Modell_GelernteVerstaerkungWirdGemerkt()
    {
        using var a = new Aufbau();
        var l = new LautstaerkeViewModel(a.Maschine, a.Ablage);
        void Takte()
        {
            for (var i = 0; i < LautstaerkeViewModel.AbgleichTakte; i++) l.Takt();
        }
        a.Maschine.Telemetrie = new TelemetrieStand(500, 0, 10_000, [], default(TelemetrieEintrag) with { PegelstufeDb = 14.2f });
        Takte();
        Pruefe.Gleich(null, a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb, "ohne laufendes Mikrofon nichts");
        await a.Maschine.StartenAsync(null, null, CancellationToken.None);
        // Seit Klangstufe C1 nur, wenn die Pegelstufe die Stimme wirklich gelernt hat (sonst wäre es der unberührte Startwert).
        a.Maschine.Korridor = KorridorStand.Leer;
        Takte();
        Pruefe.Gleich(null, a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb, "Stimme noch nicht gelernt: nichts gemerkt (Klangstufe C1)");
        a.Maschine.Korridor = new KorridorStand(-23f, -48f, -29f);
        Takte();
        Pruefe.Gleich(14.2, a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb, "gemerkt");
        a.Maschine.Telemetrie = new TelemetrieStand(800, 0, 10_000, [], default(TelemetrieEintrag) with { PegelstufeDb = 14.5f });
        Takte();
        Pruefe.Gleich(14.2, a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb, "kleine Änderung nicht");
        a.Maschine.Telemetrie = new TelemetrieStand(900, 0, 10_000, [], default(TelemetrieEintrag) with { PegelstufeDb = 15.4f });
        Takte();
        Pruefe.Gleich(15.4, a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb, "größere schon");
        Pruefe.Gleich(null, a.Ablage.Aktuell.Audio.GelernteVerstaerkungEingang, "Mikrofon unbekannt");
        // Mit dem Mikrofon, auf dem gelernt wurde; ein anderes Mikrofon wird auch bei kleiner Änderung gemerkt.
        a.Maschine.Telemetrie = new TelemetrieStand(920, 0, 10_000, [], default(TelemetrieEintrag) with { PegelstufeDb = 15.5f }) { Eingang = "mic-a" };
        Takte();
        Pruefe.Gleich("mic-a", a.Ablage.Aktuell.Audio.GelernteVerstaerkungEingang, "Mikrofon gemerkt");
        a.Maschine.Telemetrie = new TelemetrieStand(930, 0, 10_000, [], default(TelemetrieEintrag) with { PegelstufeDb = 15.6f }) { Eingang = "mic-b" };
        Takte();
        Pruefe.Wahr(a.Ablage.Aktuell.Audio is { GelernteVerstaerkungDb: 15.6, GelernteVerstaerkungEingang: "mic-b" }, "anderes Mikrofon");
        l.Automatisch = false;
        a.Maschine.Telemetrie = new TelemetrieStand(950, 0, 10_000, [], default(TelemetrieEintrag) with { PegelstufeDb = 3f });
        Takte();
        Pruefe.Gleich(15.6, a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb, "fest: nichts zu lernen");
    }

    [Test]
    public static void Modell_Klangprotokoll_ImOrdnerDerEinstellungen()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Audio.Klangprotokoll = true;
        var l = new LautstaerkeViewModel(a.Maschine, a.Ablage);
        var ordner = Path.GetDirectoryName(a.Ablage.Pfad);
        Pruefe.Gleich(ordner, a.Maschine.KlangprotokollOrdner, "beim Anlegen: Ordner der Einstellungen");
        l.Klangprotokoll = false;
        Pruefe.Gleich(null, a.Maschine.KlangprotokollOrdner, "aus");
        Pruefe.Falsch(a.Ablage.Aktuell.Audio.Klangprotokoll, "gemerkt");
        l.Klangprotokoll = true;
        Pruefe.Gleich(ordner, a.Maschine.KlangprotokollOrdner, "wieder an");
    }

    [Test]
    public static void Audio_KarteVerarbeitung_BindetLautstaerke()
    {
        using var a = new Aufbau();
        var audio = a.Audio();
        Pruefe.Wahr(audio.Lautstaerke is not null, "der Bereich Audio trägt die Lautstärke");
        var bereiche = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Fenster", "Bereiche.xaml"));
        var ab = bereiche.IndexOf("x:Name=\"LautstaerkeTeil\"", StringComparison.Ordinal);
        Pruefe.Wahr(ab > 0, "Teil Lautstärke unter Senden");
        // Seit 0.11.1 (Sarevoc 12.10.2026) steht der Schalter offen unter Senden, das Einmessen der festen Verstärkung unter Senden > Erweitert
        // (FesteVerstaerkungTeil); bis 0.11.0 standen beide zusammen im Klappbereich.
        var offen = bereiche[ab..];
        offen = offen[..offen.IndexOf("</StackPanel>", StringComparison.Ordinal)];
        var klapp = bereiche.IndexOf("x:Name=\"SendenFeinheiten\"", StringComparison.Ordinal);
        Pruefe.Wahr(offen.Contains("{Binding Automatisch", StringComparison.Ordinal) && ab < klapp, "der Schalter offen, vor dem Klappbereich");
        var fest = bereiche.IndexOf("x:Name=\"FesteVerstaerkungTeil\"", StringComparison.Ordinal);
        Pruefe.Wahr(fest > klapp, "die feste Verstärkung unter Senden > Erweitert");
        var teil = bereiche[fest..];
        teil = teil[..teil.IndexOf("</Expander>", StringComparison.Ordinal)];
        foreach (var bindung in new[] { "EinmessenCommand", "EinmessKnopf", "EinmessenGesperrt", "EinmessText", "FestText" })
            Pruefe.Wahr(teil.Contains("{Binding " + bindung, StringComparison.Ordinal), "der Teil bindet " + bindung);
        teil = offen + teil;
        // Seit 0.10.0 (AP3) steht das Klangprotokoll unter Fehlersuche > Feinheiten, nicht mehr bei der Lautstärke.
        Pruefe.Falsch(teil.Contains("{Binding Klangprotokoll", StringComparison.Ordinal), "das Klangprotokoll steht nicht bei der Lautstärke");
        var fehlersuche = bereiche[bereiche.IndexOf("x:Name=\"FehlersucheFeinheiten\"", StringComparison.Ordinal)..];
        fehlersuche = fehlersuche[..fehlersuche.IndexOf("</Expander>", StringComparison.Ordinal)];
        Pruefe.Wahr(fehlersuche.Contains("{Binding Klangprotokoll", StringComparison.Ordinal), "das Klangprotokoll unter Fehlersuche");
        Pruefe.Wahr(teil.Contains("Automatische Lautstärke", StringComparison.Ordinal), "Schalter heißt Automatische Lautstärke");
        var texte = TexteOhneKommentar(teil);
        foreach (var fremd in new[] { "AGC", "Gate", "PTT", "VA ", "pump" })
            Pruefe.Falsch(texte.Contains(fremd, StringComparison.OrdinalIgnoreCase), "kein Fachwort in der Oberfläche: " + fremd);
    }

    private static string TexteOhneKommentar(string xaml)
    {
        while (xaml.IndexOf("<!--", StringComparison.Ordinal) is var a and >= 0)
        {
            var e = xaml.IndexOf("-->", a, StringComparison.Ordinal);
            xaml = xaml[..a] + xaml[(e + 3)..];
        }
        return xaml;
    }
}
