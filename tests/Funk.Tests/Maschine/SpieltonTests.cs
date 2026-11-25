// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Spielton (Star Citizen) in der Echoreferenz, Modul aec3: Der Echoschutz verlangt bei Geräusch in der Wiedergabe nur
/// einen kleinen Abstand, die AEC3-Voreinstellung und der Transparentmodus lassen die eigene Stimme durch, und weder das
/// Echo von Sprache (Funk oder Spiel) noch das von Spielton oder Musik öffnet das Gate. Szenen aus
/// <see cref="MessungSpielton"/>, feste Seeds, keine echten Geräte.
/// </summary>
internal static class SpieltonTests
{
    /// <summary>Feste Verstärkung 15 dB: so viel gab die AGC2 bis 0.6.0 Stufe 2c in Szenen ohne eigene Sprache (Startwert).</summary>
    private static void WieAgc2(Pruefstand s) => s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 15f);

    [Test]
    public static void Echoschutz_GeraeuschMitKleinemAbstand_SpracheMitVollem()
    {
        // Bei Geräusch in der Wiedergabe (Spielton, Musik) gilt der kleine Abstand, bei Sprache der volle. Kopplung zu Beginn
        // 0 dB; jeder Aufruf von Verdacht bei geschlossenem Gate lernt um 0,05 dB.
        var schutz = new Echoschutz();
        for (var i = 0; i < 100; i++) schutz.Wiedergabe(-20f, sprachartig: false);
        Pruefe.Gleich(-20f, schutz.WiedergabeDb, "Pegel der Wiedergabe auch bei Geräusch");
        Pruefe.Falsch(schutz.Sprachartig, "Geräusch in der Wiedergabe");
        Pruefe.Wahr(schutz.Verdacht(-16f, false, false), "Geräusch: 4 dB über dem erwarteten Echo reicht nicht");
        Pruefe.Falsch(schutz.Verdacht(-12f, false, false), $"Geräusch: 8 dB darüber öffnet (Abstand {Echoschutz.GeraeuschSicherheitDb} dB)");
        PruefeWert.Mindestens(0.05, schutz.KopplungDb, "die Kopplung lernt auch bei Geräusch");

        schutz.Wiedergabe(-20f, sprachartig: true);
        Pruefe.Wahr(schutz.Sprachartig, "ein sprachartiger Rahmen");
        Pruefe.Wahr(schutz.Verdacht(-12f, false, false), $"Sprache: 8 dB darüber reicht nicht (Abstand {Echoschutz.SicherheitDb} dB)");
        for (var i = 0; i < Echoschutz.HalteRahmen - 1; i++) schutz.Wiedergabe(-20f, sprachartig: false);
        Pruefe.Wahr(schutz.Sprachartig, "Haltezeit 400 ms über die Silben");
        schutz.Wiedergabe(-20f, sprachartig: false);
        Pruefe.Falsch(schutz.Sprachartig, "400 ms ohne sprachartigen Rahmen: wieder Geräusch");

        schutz.Wiedergabe(-70f, sprachartig: true);
        Pruefe.Falsch(schutz.Sprachartig, "unter -60 dBFS zählt auch Sprache nicht");

        // Vergleichsstellung (Stand vor der Gegenprüfung): Bei Geräusch ruht der Schutz ganz.
        var ruht = new Echoschutz { GeraeuschAbstandDb = float.NegativeInfinity };
        for (var i = 0; i < 100; i++) ruht.Wiedergabe(-20f, sprachartig: false);
        Pruefe.Falsch(ruht.Verdacht(-40f, false, false), "ruhender Schutz: kein Verdacht");
        Pruefe.Gleich(0f, ruht.KopplungDb, "ruhender Schutz lernt nicht");
    }

    [Test]
    public static async Task SpieltonUndMusik_EchoOeffnetGateNicht()
    {
        // Gegenprüfung aec3: Spielton oder Musik über die Lautsprecher, kein Funk, der Nutzer schweigt. Ruht der Echoschutz
        // bei Geräusch (Stand vor der Gegenprüfung), öffnet das Echo hinter AEC3 und RNNoise das Gate (gemessen je 105 Szenen
        // zu 40 s: Spielton 12, Musik 124 Auslösungen); mit dem Abstand bei Geräusch keine. Szenen, in denen der ruhende
        // Schutz auslöste.
        var szenen = new (int Seed, float Dbfs, Func<float, float> Pfad, bool Musik)[]
        {
            (152, -36f, KlangMaschineTests.Kopplung3Db, false), (162, -42f, x => 0.0316f * x, false),
            (172, -24f, KlangMaschineTests.Kopplung12Db, false), (153, -30f, KlangMaschineTests.Kopplung12Db, true),
            (163, -24f, KlangMaschineTests.KopplungVerzerrt, true),
        };
        // Seit Klangstufe A verstärkt die Pegelstufe statt der AGC2; ab Werk mit dem Startwert, der Reste des Echos weniger anhebt als
        // die AGC2 (die in diesen Szenen ohne Sprache bei 15 dB blieb). Die Gegenprobe läuft deshalb mit fester Verstärkung wie damals,
        // dem Fall eines leisen Mikrofons, und der Schutz muss auch dort halten.
        var ruht = szenen.Select(z => Task.Run(() => MessungSpielton.SpieltonEcho(z.Seed, z.Dbfs, z.Pfad, z.Musik, float.NegativeInfinity, vorbereiten: WieAgc2))).ToArray();
        var neu = szenen.Select(z => Task.Run(() => MessungSpielton.SpieltonEcho(z.Seed, z.Dbfs, z.Pfad, z.Musik, null))).ToArray();
        var neuLaut = szenen.Select(z => Task.Run(() => MessungSpielton.SpieltonEcho(z.Seed, z.Dbfs, z.Pfad, z.Musik, null, vorbereiten: WieAgc2))).ToArray();
        var (r, n, l) = (await Task.WhenAll(ruht), await Task.WhenAll(neu), await Task.WhenAll(neuLaut));
        Console.WriteLine($"  Spielton und Musik über Lautsprecher, Nutzer schweigt: Schutz ruht bei Geräusch {string.Join("/", r)} Auslösungen (fest 15 dB), " +
            $"Abstand {Echoschutz.GeraeuschSicherheitDb} dB {string.Join("/", n)} (ab Werk), {string.Join("/", l)} (fest 15 dB)");
        PruefeWert.Mindestens(2, r.Take(3).Sum(), "ruhender Schutz: das Echo von Spielton öffnet (die Szenen messen etwas)");
        PruefeWert.Mindestens(2, r.Skip(3).Sum(), "ruhender Schutz: das Echo von Musik öffnet");
        Pruefe.Gleich(0, n.Sum(), "mit Abstand bei Geräusch keine Auslösung");
        Pruefe.Gleich(0, l.Sum(), "mit Abstand bei Geräusch keine Auslösung, auch mit 15 dB Verstärkung");
    }

    [Test]
    public static async Task FunkNebenMusik_EchoschutzGreiftWeiter()
    {
        // Gegenprüfung aec3: Ein anderer Funker und Musik (-42 dBFS) über einen verzerrenden Lautsprecher, der Nutzer schweigt.
        // RNNoise auf der Referenz erkennt die Funkstimme neben der Musik nicht durchgehend als Sprache; ohne die Meldung des
        // eigenen Mischers gilt dann nur der kleine Abstand, und das Echo öffnet das Gate.
        var musik = MessungSpielton.Musik(40 * 48000, -42f, 153);
        // Die Szene ist ein Grenzfall (eine Auslösung ohne die Meldung) und mit dem Klick bis 0.4.1 geeicht: -22,6 dB unter der Sprache
        // ergibt dessen Spitze -24 dBFS. Seit 0.5.0 (A63) ist der Klick ab Werk lauter; mit ihm gilt die Prüfung mit Meldung ebenso.
        var alt = new FunkKlickEinstellungen { EigeneDb = -22.6f, AndereDb = -22.6f };
        // Verstärkung fest 15 dB wie die AGC2 bis 2c (siehe SpieltonUndMusik_EchoOeffnetGateNicht); ab Werk zusätzlich.
        var nurRnnoise = KlangMaschineTests.EchoLauf(KlangMaschineTests.KopplungVerzerrt, true, true, false, spielton: musik, mitFunk: false, klick: alt, vorbereiten: WieAgc2);
        var mitFunk = KlangMaschineTests.EchoLauf(KlangMaschineTests.KopplungVerzerrt, true, true, false, spielton: musik, klick: alt, vorbereiten: WieAgc2);
        var mitFunkAbWerk = KlangMaschineTests.EchoLauf(KlangMaschineTests.KopplungVerzerrt, true, true, false, spielton: musik);
        var (o, m, w) = ((await nurRnnoise).Fehlphasen, (await mitFunk).Fehlphasen, (await mitFunkAbWerk).Fehlphasen);
        Console.WriteLine($"  Funk neben Musik, verzerrender Lautsprecher: nur RNNoise {o} Auslösungen, mit Meldung des Mischers {m}, mit Klick ab Werk {w}");
        PruefeWert.Mindestens(1, o, "ohne die Meldung des Mischers öffnet das Echo (die Szene misst etwas)");
        Pruefe.Gleich(0, m, "mit der Meldung keine Auslösung");
        Pruefe.Gleich(0, w, "mit der Meldung und dem Klick ab Werk keine Auslösung");
    }

    [Test]
    public static async Task Lautsprecher_Spielton_EchoschutzSperrtEigeneStimmeNicht()
    {
        // Lautsprecher mit 12 dB Kopplung, Spielton -24 dBFS, der Nutzer spricht 60 s, drei Seeds. Vor dem Modul aec3
        // sperrte der Echoschutz jeden Anlaut, der nicht 10 dB über dem Spielecho lag; jetzt gilt bei Geräusch der kleine
        // Abstand (gemessen 21 statt 31 von 65 verpasst; ganz ohne Schutz bei Geräusch 7, dann öffnet aber das Echo von
        // Musik das Gate, siehe SpieltonUndMusik_EchoOeffnetGateNicht).
        var alt = MessungSpielton.LautsprecherDreiSeeds(null, 0.25f, MessungSpielton.Schutz.Alt);
        var neu = MessungSpielton.LautsprecherDreiSeeds(null, 0.25f, MessungSpielton.Schutz.Neu);
        var (a, n) = (await alt, await neu);
        Console.WriteLine($"  Lautsprecher 12 dB mit Spielton: verpasst mit altem Echoschutz {a}, mit neuem {n}");
        PruefeWert.Hoechstens(23, n.Verpasst, "neuer Echoschutz: höchstens 23 von 65 Anlauten verpasst (gemessen 21)");
        PruefeWert.Mindestens(n.Verpasst + 8, a.Verpasst, "der alte Schutz verpasste deutlich mehr (die Szene misst etwas)");
        Pruefe.Gleich(0, n.Fehl, "keine Phase ohne eigene Äußerung");
    }

    [Test]
    public static async Task Spielstimme_EchoschutzGreiftWeiter()
    {
        // Sprachausgaben im Spielton über einen verzerrenden Lautsprecher, der Nutzer schweigt: Ohne Schutz öffnet der
        // Rest hinter AEC3 das Gate, mit dem neuen Schutz nicht, weil die Wiedergabe dann sprachartig ist.
        var ohne = Task.Run(() => MessungSpielton.Spielstimme(null, KlangMaschineTests.KopplungVerzerrt, MessungSpielton.Schutz.Aus));
        var neu = Task.Run(() => MessungSpielton.Spielstimme(null, KlangMaschineTests.KopplungVerzerrt, MessungSpielton.Schutz.Neu));
        var neu12 = Task.Run(() => MessungSpielton.Spielstimme(null, KlangMaschineTests.Kopplung12Db, MessungSpielton.Schutz.Neu));
        var (o, n, n12) = (await ohne, await neu, await neu12);
        Console.WriteLine($"  Sprachausgaben im Spielton: ohne Echoschutz {o} Phasen, mit neuem {n} (verzerrt), {n12} (12 dB)");
        PruefeWert.Mindestens(1, o, "ohne Schutz öffnet das Echo der Sprachausgaben (die Szene misst etwas)");
        Pruefe.Gleich(0, n, "neuer Schutz, verzerrender Lautsprecher: keine Auslösung");
        Pruefe.Gleich(0, n12, "neuer Schutz, 12 dB: keine Auslösung");
    }

    [Test]
    public static async Task Headset_Spielton_AnlauteKommenDurch()
    {
        // Headset: Spielton nur in der Referenz, drei Seeds zu 60 s. Ziel des Moduls aec3: höchstens 6 von 68 Anlauten
        // verpasst, nahe am Wert ohne Spielton; gemessen 7, weil der Echoschutz bei Geräusch einen Anlaut mehr sperrt
        // (Gegenprüfung; ohne ihn 6). Die Upstream-Vorgabe (mit demselben Transparentmodus) verpasst deutlich mehr.
        Func<int, int, float[]> spiel = (n, seed) => MessungSpielton.Spielton(n, -24f, seed);
        var eigen = MessungSpielton.HeadsetDreiSeeds(null, spiel);
        var upstream = MessungSpielton.HeadsetDreiSeeds(EchoProzessor.Aec3Webrtc, spiel);
        var ohne = MessungSpielton.HeadsetDreiSeeds(null, null);
        var (e, u, o) = (await eigen, await upstream, await ohne);
        Console.WriteLine($"  Headset mit Spielton -24 dBFS: Voreinstellung {e}, Upstream {u}, ohne Spielton {o}");
        PruefeWert.Hoechstens(7, e.Verpasst, "Voreinstellung: höchstens 7 von 68 (Ziel 6, gemessen 7)");
        PruefeWert.Mindestens(e.Verpasst + 5, u.Verpasst, "die Upstream-Vorgabe verpasst deutlich mehr");
        Pruefe.Gleich(0, e.Fehl + o.Fehl, "keine Phase ohne eigene Äußerung");
    }
}
