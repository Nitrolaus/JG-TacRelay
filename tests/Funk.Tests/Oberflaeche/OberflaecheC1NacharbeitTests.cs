// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Nacharbeit aus der Gegenprüfung der Klangstufe C1 im Client (output\test\20261004_pruefung-c1.md): H1 (die gemessene Funkstimme wird mit der
/// Verstärkung gemerkt und an die Maschine gegeben, damit der Anker nie aus einer begrenzten Verstärkung kommt), M4 (fest eingemessen und
/// "eingemessen" überhaupt gelten je Mikrofon; auf einem anderen kommt der Hinweis zurück), N3 (Umstieg Lernend: ein selbst gesetztes "Korridor
/// bis" bleibt als Obergrenze). Die Maschine ist die Attrappe; den Kern prüft <c>KlangC1NacharbeitTests</c>.
/// </summary>
internal static class OberflaecheC1NacharbeitTests
{
    private static TelemetrieStand Telemetrie(string eingang, float verstaerkung = 9.5f) =>
        new(500, 0, 10_000, [], default(TelemetrieEintrag) with { PegelstufeDb = verstaerkung }) { Eingang = eingang };

    [Test]
    public static async Task C1N_Client_Einmessen_MerktStimmeUndMikrofon_HinweisJeMikrofon()
    {
        using var a = new Aufbau();
        var k = new KorridorViewModel(a.Maschine, a.Ablage);
        await a.Maschine.StartenAsync(null, null, CancellationToken.None);
        a.Maschine.Telemetrie = Telemetrie("mic-a");
        k.Takt();

        // Automatik: Verstärkung als gelernt, mit der gemessenen Stimme (H1) und dem Mikrofon.
        k.EinmessenCommand.Execute(null);
        a.Maschine.MindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.Fertig, 0, 9.5f, 12f, -32.5f, -50.5f);
        k.Takt();
        var audio = a.Ablage.Aktuell.Audio;
        Pruefe.Gleich<double?>(-32.5, audio.GelernteStimmeDbfs, "gemessene Stimme gemerkt");
        Pruefe.Gleich("mic-a", audio.GelernteVerstaerkungEingang, "Mikrofon gemerkt");
        Pruefe.Wahr(a.Maschine.Lautstaerke is { Automatisch: true, FestDb: 9.5f, Gelernt: true, Eingang: "mic-a", StimmeDbfs: -32.5f },
            "an die Maschine mit Stimme und Mikrofon: " + a.Maschine.Lautstaerke);
        Pruefe.Wahr(a.Ablage.Aktuell.Gate is { MindestEingemessen: true, MindestEingemessenEingang: "mic-a" }, "eingemessen für mic-a");
        Pruefe.Falsch(k.ZeigeEinmessHinweis, "eingemessen: kein Hinweis");

        // M4: Auf einem anderen Mikrofon gilt das Einmessen nicht; der Hinweis kommt zurück.
        a.Maschine.Telemetrie = Telemetrie("mic-b");
        k.Takt();
        Pruefe.Wahr(k.ZeigeEinmessHinweis, "anderes Mikrofon: Hinweis zurück");
        a.Maschine.Telemetrie = Telemetrie("mic-a");
        k.Takt();
        Pruefe.Falsch(k.ZeigeEinmessHinweis, "wieder mic-a: kein Hinweis");

        // Fest: der eingemessene Wert mit Stimme und Mikrofon (M4); ein anderes Mikrofon lernt in der Maschine neu.
        audio.AutomatischeLautstaerke = false;
        k.EinmessenCommand.Execute(null);
        a.Maschine.MindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.Fertig, 0, 4f, 10f, -27f, -42f);
        k.Takt();
        Pruefe.Wahr(audio is { FesteVerstaerkungDb: 4.0, FestEingemessen: true, FesteStimmeDbfs: -27.0, FestEingang: "mic-a" }, "fest: Stimme und Mikrofon gemerkt");
        Pruefe.Wahr(audio.LautstaerkeFuerMaschine() is { Automatisch: false, Gelernt: true, Eingang: "mic-a", StimmeDbfs: -27f },
            "fest: mit Mikrofon an die Maschine (sonst bekäme ein anderes Mikrofon den alten Anker)");

        // Rundreise durch die Datei.
        a.Ablage.Sichern();
        var gelesen = a.Speicher.Laden();
        Pruefe.Wahr(gelesen.Audio is { GelernteStimmeDbfs: -32.5, FesteStimmeDbfs: -27.0, FestEingang: "mic-a" }, "Audio gemerkt");
        Pruefe.Gleich("mic-a", gelesen.Gate.MindestEingemessenEingang, "Mikrofon des Einmessens gemerkt");
    }

    [Test]
    public static async Task C1N_Client_GelernteStimmeWirdMitDerVerstaerkungGemerkt()
    {
        using var a = new Aufbau();
        var l = new LautstaerkeViewModel(a.Maschine, a.Ablage);
        await a.Maschine.StartenAsync(null, null, CancellationToken.None);
        a.Maschine.Korridor = new KorridorStand(-23f, -48f, -29f, AnkerDbfs: -37.2f);
        a.Maschine.Telemetrie = Telemetrie("mic-a", 14.2f);
        for (var i = 0; i < LautstaerkeViewModel.AbgleichTakte; i++) l.Takt();
        Pruefe.Wahr(a.Ablage.Aktuell.Audio is { GelernteVerstaerkungDb: 14.2, GelernteStimmeDbfs: -37.2, GelernteVerstaerkungEingang: "mic-a" },
            "Verstärkung mit der gelernten Stimme (Anker) gemerkt");
        Pruefe.Wahr(a.Ablage.Aktuell.Audio.LautstaerkeFuerMaschine() is { Gelernt: true, StimmeDbfs: -37.2f, Eingang: "mic-a" }, "beim nächsten Start an die Maschine");
        // Der Anker wandert (Mikrofon leiser gestellt): neu gemerkt, auch wenn sich die Verstärkung kaum ändert.
        a.Maschine.Korridor = new KorridorStand(-23f, -48f, -29f, AnkerDbfs: -44f);
        for (var i = 0; i < LautstaerkeViewModel.AbgleichTakte; i++) l.Takt();
        Pruefe.Gleich<double?>(-44.0, a.Ablage.Aktuell.Audio.GelernteStimmeDbfs, "neuer Anker gemerkt");
    }

    [Test]
    public static void C1N_Client_Normalisieren_StimmeUndMikrofon()
    {
        var audio = new AudioEinstellungen { GelernteVerstaerkungDb = 5, GelernteStimmeDbfs = 12, FesteVerstaerkungDb = 3, FesteStimmeDbfs = -300, FestEingang = new string('x', 600) };
        audio.Normalisieren();
        Pruefe.Wahr(audio is { GelernteStimmeDbfs: null, FesteStimmeDbfs: null, FestEingang: null }, "Unsinn fällt weg");
        audio = new AudioEinstellungen { GelernteStimmeDbfs = -30, FesteStimmeDbfs = -31, FestEingang = "mic-a", FestEingemessen = true };
        audio.Normalisieren();
        Pruefe.Wahr(audio is { GelernteStimmeDbfs: null, FesteStimmeDbfs: null, FestEingang: null }, "ohne Verstärkung keine Stimme und kein Mikrofon");
    }

    /// <summary>N3: Umstieg von 0.6.0 im Modus Lernend: ein selbst gesetztes "Korridor bis" bleibt als Obergrenze (Abstand unter dem Ziel der Pegelstufe).</summary>
    [Test]
    public static void C1N_Client_Umstieg060_Lernend_KorridorBisBleibtAlsObergrenze()
    {
        var g = new GateWerte { Modus = GateModus.Lernend, KorridorVonDb = -50f, KorridorBisDb = -40f };
        g.AufC1Umstellen();
        Pruefe.Gleich(Pegelstufe.ZielDbfs + 40f, g.ObergrenzeAbstandDb, "Obergrenze 17 dB unter der Stimme (wie -40 dBFS)");
        Pruefe.Gleich(Pegelstufe.ZielDbfs + 50f, g.MindestAbstandDb, "Korridor von wie bisher");
        var werk = new GateWerte { Modus = GateModus.Lernend, KorridorVonDb = -50f };
        werk.AufC1Umstellen();
        Pruefe.Gleich(GateVorgaben.ObergrenzeAbstandAbWerkDb, werk.ObergrenzeAbstandDb, "nie verstellt (-30): ab Werk");
        var eng = new GateWerte { Modus = GateModus.Lernend, KorridorVonDb = -40f, KorridorBisDb = -38f };
        eng.AufC1Umstellen();
        PruefeWert.Hoechstens(eng.MindestAbstandDb - GateVorgaben.KorridorMindestBreiteDb, eng.ObergrenzeAbstandDb, "die Mindestbreite bleibt");
        PruefeWert.Mindestens(GateVorgaben.ObergrenzeAbstandAbWerkDb, eng.ObergrenzeAbstandDb, "nie über Stimme minus 6 dB");
    }
}
