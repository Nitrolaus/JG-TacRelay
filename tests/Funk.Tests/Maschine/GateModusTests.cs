// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Kennzahlen wie <see cref="GateKennzahlen"/>, aber mit der <see cref="GateFuehrung"/> vor dem Gate, damit alle drei
/// Modi an denselben synthetischen Szenen messbar sind. Liefert auf Wunsch die Linie je Rahmen.
/// </summary>
internal static class FuehrungsMessung
{
    private const int Rahmen = 480;
    private const int ToleranzSamples = 480;

    public static GateMessung Messen(ReadOnlySpan<float> signal, IReadOnlyList<Aeusserung> marken, ISprachwahrscheinlichkeit schaetzer,
        GateEinstellung einstellung, List<float>? linien = null, List<float>? hintergrund = null, List<bool>? gesendetJeRahmen = null)
    {
        var fuehrung = new GateFuehrung { Einstellung = GateVorgaben.Bereinigen(einstellung) };
        var gate = fuehrung.Gate;
        schaetzer.Zuruecksetzen();
        var rahmenGesamt = signal.Length / Rahmen;
        var gesendet = new bool[rahmenGesamt];
        var phasen = new List<(long Start, long Ende, long Geoeffnet)>();
        var puffer = new float[Rahmen];
        long phasenStart = -1, geoeffnet = -1;
        for (var r = 0; r < rahmenGesamt; r++)
        {
            var rahmen = signal.Slice(r * Rahmen, Rahmen);
            var p = schaetzer.Schaetzen(rahmen);
            var pGate = fuehrung.Vorbereiten(Pegel.Dbfs(rahmen), p, false);
            linien?.Add(fuehrung.LinieDb);
            hintergrund?.Add(fuehrung.HintergrundDb);
            gate.Verarbeiten(rahmen, pGate);
            while (gate.Holen(puffer, out var art, out var nummer))
            {
                if (nummer >= 0 && nummer < rahmenGesamt) gesendet[nummer] = true;
                if ((art & SenderahmenArt.Beginn) != 0)
                {
                    if (phasenStart >= 0) phasen.Add((phasenStart, nummer - 1, geoeffnet));
                    phasenStart = nummer;
                    geoeffnet = r;
                }
                if ((art & SenderahmenArt.Letzter) != 0 && phasenStart >= 0)
                {
                    phasen.Add((phasenStart, nummer, geoeffnet));
                    phasenStart = -1;
                }
            }
        }
        if (phasenStart >= 0) phasen.Add((phasenStart, rahmenGesamt - 1, geoeffnet));

        var fehl = phasen.Count(ph => !marken.Any(a => ph.Start * Rahmen < a.Ende && (ph.Ende + 1) * Rahmen > a.Start));
        var abgeschnitten = 0;
        double verzug = 0;
        var verzugAnzahl = 0;
        foreach (var a in marken)
        {
            var erster = -1;
            for (var r = Math.Max(0, a.Start / Rahmen); r < rahmenGesamt && r * Rahmen < a.Ende; r++)
            {
                if (!gesendet[r]) continue;
                erster = r;
                break;
            }
            if (erster < 0 || erster * Rahmen - a.Start > ToleranzSamples)
            {
                abgeschnitten++;
                continue;
            }
            foreach (var (start, ende, offen) in phasen)
            {
                if (erster < start || erster > ende) continue;
                verzug += Math.Max(0, (offen + 1) * Rahmen - a.Start) / 48.0;
                verzugAnzahl++;
                break;
            }
        }
        gesendetJeRahmen?.AddRange(gesendet);
        var minuten = signal.Length / 48000.0 / 60.0;
        return new GateMessung(minuten, phasen.Count, fehl, fehl / minuten, marken.Count, abgeschnitten,
            marken.Count == 0 ? 0 : abgeschnitten / (double)marken.Count, verzugAnzahl == 0 ? 0 : verzug / verzugAnzahl,
            gate.AbgewieseneAnlaeufe, gesendet.Count(g => g) / (double)Math.Max(1, rahmenGesamt));
    }
}

/// <summary>Feste Sprachwahrscheinlichkeit, für Tests, die nur den Pegel oder nur die Sicherheit prüfen.</summary>
internal sealed class FesteWahrscheinlichkeit(float p) : ISprachwahrscheinlichkeit
{
    public float P { get; set; } = p;

    public float Schaetzen(ReadOnlySpan<float> rahmen10ms) => P;

    public void Zuruecksetzen()
    {
    }
}

/// <summary>
/// Die drei Modi der Sprachaktivierung (A26 bis A28) und die Sprachsicherheit. Lernen aus Rückmeldung gibt es nicht mehr
/// (Sarevoc, 29.09.2026, A28): Der Modus Lernend lernt den Hintergrund still.
/// </summary>
internal static class GateModusTests
{
    private static readonly GateEinstellung Standard = GateVorgaben.Standard;

    /// <summary>Rahmen mit Sinus (200 Hz) auf gegebenem Pegel (dBFS, mittlerer quadratischer Wert) plus leisem Rauschen.</summary>
    private static float[] Ton(int rahmen, float dbfs, float grundDbfs = -70f, int seed = 1)
    {
        var x = new float[rahmen * 480];
        var a = MathF.Sqrt(2f) * MathF.Pow(10f, dbfs / 20f);
        var g = MathF.Pow(10f, grundDbfs / 20f) * MathF.Sqrt(3f);
        var z = new Random(seed);
        for (var i = 0; i < x.Length; i++) x[i] = a * MathF.Sin(2f * MathF.PI * 200f * i / 48000f) + g * (float)(z.NextDouble() * 2 - 1);
        return x;
    }

    private static float[] Folge(params float[][] teile) => [.. teile.SelectMany(t => t)];

    /// <summary>Läuft eine Folge durch Führung und Gate, liefert je Rahmen, ob gesendet wurde, und die Linie.</summary>
    private static (bool[] Offen, float[] Linie) Lauf(GateFuehrung f, float[] signal, float p)
    {
        var n = signal.Length / 480;
        var offen = new bool[n];
        var linie = new float[n];
        var puffer = new float[480];
        for (var r = 0; r < n; r++)
        {
            var rahmen = signal.AsSpan(r * 480, 480);
            var pGate = f.Vorbereiten(Pegel.Dbfs(rahmen), p, false);
            linie[r] = f.LinieDb;
            f.Gate.Verarbeiten(rahmen, pGate);
            while (f.Gate.Holen(puffer, out _, out _))
            {
            }
            offen[r] = f.Gate.Zustand is GateZustand.Senden or GateZustand.Nachlauf;
        }
        return (offen, linie);
    }

    [Test]
    public static void Fest_LinieLiegtGenauAufDerSchwelle()
    {
        var f = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Fest, SchwelleDb = -40f } };
        var (offenDrueber, linie) = Lauf(f, Folge(Ton(200, -80f), Ton(40, -37f)), 1f);
        Pruefe.Wahr(linie.All(l => l == -40f), "Linie immer bei -40 dBFS");
        Pruefe.Wahr(offenDrueber[^1], "3 dB über der Linie öffnet");

        var g = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Fest, SchwelleDb = -40f } };
        var (offenDrunter, _) = Lauf(g, Folge(Ton(200, -80f), Ton(40, -43f)), 1f);
        Pruefe.Falsch(offenDrunter.Any(o => o), "3 dB unter der Linie bleibt zu");
    }

    [Test]
    public static void Fest_LinieUnabhaengigVomRauschboden()
    {
        // Der Boden steigt von -80 auf -50 dBFS; die feste Linie bei -45 bleibt, und das Rauschen bei -50 öffnet nicht.
        var f = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Fest, SchwelleDb = -45f } };
        var rauschen = AnbindungSignale.Rauschen(48000 * 3, -50, 7);
        var (offen, linie) = Lauf(f, Folge(Ton(100, -80f), rauschen), 1f);
        Pruefe.Wahr(linie.All(l => l == -45f), "Linie fest");
        Pruefe.Falsch(offen.Any(o => o), "Rauschen 5 dB unter der Linie sendet nicht");
    }

    [Test]
    public static void Automatisch_LinieFolgtRauschbodenPlusAbstand()
    {
        var f = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Automatisch, AbstandDb = 12f } };
        var rauschen = AnbindungSignale.Rauschen(48000 * 4, -60, 3);
        var (_, linie) = Lauf(f, rauschen, 0f);
        var boden = f.Gate.RauschbodenDbfs;
        PruefeWert.Nahe(-60, boden, 2.5, "Rauschboden");
        PruefeWert.Nahe(boden + 12, linie[^1], 0.5, "Linie = Boden + Abstand");

        // Lauter werdendes Rauschen: Die Linie zieht mit.
        var lauter = AnbindungSignale.Rauschen(48000 * 4, -40, 4);
        var (_, linie2) = Lauf(f, lauter, 0f);
        PruefeWert.Nahe(-40 + 12, linie2[^1], 3, "Linie folgt dem neuen Boden");
    }

    [Test]
    public static void Automatisch_OeffnetBeiSpracheUeberDerLinie()
    {
        var f = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Automatisch, AbstandDb = 9f } };
        var (offen, linie) = Lauf(f, Folge(AnbindungSignale.Rauschen(48000 * 2, -65, 5), Ton(50, -40f, -65f)), 0.95f);
        PruefeWert.Hoechstens(-50, linie[190], "Linie vor dem Ton nahe Boden plus 9");
        Pruefe.Wahr(offen[^1], "Ton 25 dB über dem Boden öffnet");
    }

    [Test]
    public static void Lernend_HebtSichMitHintergrund_BleibtImKorridor_KehrtZurueck()
    {
        var e = Standard with
        {
            Modus = GateModus.Lernend, AbstandDb = 6f, KorridorVonDb = -58f, KorridorBisDb = -35f,
            AnstiegSekunden = 1f, RueckkehrSekunden = 4f,
        };
        var f = new GateFuehrung { Einstellung = e };
        // 3 s leise (-70), 8 s lauter Hintergrund (-38, sprachunähnlich), 12 s wieder leise.
        var signal = Folge(
            AnbindungSignale.Rauschen(48000 * 3, -70, 11),
            AnbindungSignale.Rauschen(48000 * 8, -38, 12),
            AnbindungSignale.Rauschen(48000 * 12, -70, 13));
        var (_, linie) = Lauf(f, signal, 0f);
        Pruefe.Wahr(linie.All(l => l >= -58f - 1e-3f && l <= -35f + 1e-3f), "Linie immer im Korridor");
        PruefeWert.Nahe(-58, linie[290], 0.5, "leise: an der Korridor-Untergrenze");
        // Nach 2,5 s Lärm: Hintergrund plus 6 dB läge bei -32, gedeckelt auf die Obergrenze -35.
        PruefeWert.Nahe(-35, linie[300 + 250], 0.5, "lauter Hintergrund hebt die Linie an die Obergrenze");
        // Rückkehr 10 dB je 4 s: Der Rauschboden ist nach 2 s wieder unten, der gelernte Hintergrund fällt langsam.
        // Nach 1 s liegt er noch nahe -40 (Linie gedeckelt), nach 12 s ist er etwa 30 dB tiefer, die Linie wieder unten.
        PruefeWert.Mindestens(-36, linie[1100 + 100], "eine Sekunde nach dem Lärm noch oben");
        PruefeWert.Mindestens(-50, linie[1100 + 400], "vier Sekunden danach noch deutlich über dem Boden");
        PruefeWert.Nahe(-58, linie[^1], 0.5, "nach der Rückkehrzeit wieder unten");
    }

    [Test]
    public static void Lernend_SchnellerAnstieg_LangsameRueckkehr_FolgtOberemQuantil()
    {
        // Bei 1 s / 4 s liegt das Gleichgewicht beim 80-%-Quantil: 10 % laute Rahmen (Klicks) heben den Hintergrund
        // kaum, 50 % (anhaltender Lärm) heben ihn bis zum lauten Pegel. 20 s, damit er von -100 aus ankommt.
        var e = Standard with { Modus = GateModus.Lernend, AnstiegSekunden = 1f, RueckkehrSekunden = 4f, KorridorVonDb = -90f, KorridorBisDb = -10f };
        static float[] Muster(int lautJeZehn)
        {
            var teile = new List<float[]>();
            for (var i = 0; i < 2000; i++) teile.Add(Ton(1, i % 10 < lautJeZehn ? -40f : -70f, -90f, i));
            return Folge([.. teile]);
        }
        var selten = new GateFuehrung { Einstellung = e };
        _ = Lauf(selten, Muster(1), 0f);
        var oft = new GateFuehrung { Einstellung = e };
        _ = Lauf(oft, Muster(5), 0f);
        PruefeWert.Hoechstens(-60, selten.HintergrundDb, "seltene laute Rahmen heben den Hintergrund kaum");
        PruefeWert.Mindestens(-45, oft.HintergrundDb, "häufige laute Rahmen heben ihn bis zum Lärm");
    }

    [Test]
    public static void Sprachsicherheit_FiltertUnsichereRahmen()
    {
        var signal = Folge(Ton(200, -80f), Ton(40, -30f));
        var streng = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Fest, SchwelleDb = -60f, Sprachsicherheit = 0.5f } };
        var (offenStreng, _) = Lauf(streng, signal, 0.4f);
        Pruefe.Falsch(offenStreng.Any(o => o), "Wahrscheinlichkeit 0,4 unter Sicherheit 0,5 sendet nicht");

        var locker = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Fest, SchwelleDb = -60f, Sprachsicherheit = 0.3f } };
        var (offenLocker, _) = Lauf(locker, signal, 0.4f);
        Pruefe.Wahr(offenLocker[^1], "Wahrscheinlichkeit 0,4 über Sicherheit 0,3 sendet");

        var nurPegel = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Fest, SchwelleDb = -60f, Sprachsicherheit = 0f } };
        var (offenPegel, _) = Lauf(nurPegel, signal, 0f);
        Pruefe.Wahr(offenPegel[^1], "Sicherheit 0: nur der Pegel zählt");

        // Automatisch: Die Selbstjustierung (mindestens 0,4) bleibt Untergrenze, auch bei Sicherheit 0.
        var auto = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Automatisch, Sprachsicherheit = 0f } };
        var (offenAuto, _) = Lauf(auto, signal, 0.3f);
        Pruefe.Falsch(offenAuto.Any(o => o), "automatisch: 0,3 liegt unter der selbstjustierten Schwelle");
    }

    [Test]
    public static void Szenen_AlleModi_ZieleAusDemEntwurf()
    {
        // Dieselben Szenen wie GateSzenenTests, jetzt durch die Führung: Automatisch (Voreinstellung), Lernend mit weitem
        // Korridor und Fest knapp über dem Grundrauschen der Szenen (-65 dBFS).
        const double Minuten = 2;
        var (sprache, marken) = Testszenen.Sprache(Minuten * 60, 4);
        var (spracheT, markenT) = Testszenen.Sprache(Minuten * 60, 5);
        var szenen = new (string Name, float[] Signal, IReadOnlyList<Aeusserung> Marken)[]
        {
            ("Stille", Testszenen.Stille(Minuten * 60, 1), []),
            ("Tastatur", Testszenen.Tastatur(Minuten * 60, 2), []),
            ("Tüte", Testszenen.Tuete(Minuten * 60, 3), []),
            ("Sprache", sprache, marken),
            ("Sprache mit Tastatur", Testszenen.Summe(spracheT, Testszenen.Tastatur(Minuten * 60, 6)), markenT),
        };
        var modi = new (string Name, GateEinstellung E)[]
        {
            ("Automatisch", Standard),
            ("Lernend", Standard with { Modus = GateModus.Lernend, KorridorVonDb = -62f, KorridorBisDb = -40f }),
            ("Fest -52", Standard with { Modus = GateModus.Fest, SchwelleDb = -52f }),
        };
        foreach (var (modus, e) in modi)
        {
            foreach (var (name, signal, m) in szenen)
            {
                var ergebnis = FuehrungsMessung.Messen(signal, m, new Stimmhaftigkeit(), e);
                Console.WriteLine($"  Führung {modus}, {name}: {ergebnis}");
                PruefeWert.Hoechstens(0.5, ergebnis.FehlausloesungenJeMinute, $"{modus}/{name}: Fehlauslösungen je Minute");
                if (m.Count > 0) PruefeWert.Hoechstens(0.02, ergebnis.AnteilAbgeschnitten, $"{modus}/{name}: abgeschnittene Anlaute");
            }
        }
    }

    [Test]
    public static void Lernend_StimmenImHintergrund_WenigerFehlausloesungenAlsAutomatisch()
    {
        // A28, das Beispiel von Sarevoc: Im Hintergrund reden oder schreien andere (sprachähnlich, 26 dB leiser als die eigene
        // Stimme, etwa die Hälfte der Zeit; eigene Stimme -36 bis -18 dBFS, fremde -62 bis -44). Automatisch öffnet
        // darauf, Lernend hebt die Linie über den Hintergrund, höchstens bis zur Korridorgrenze unter der eigenen Stimme.
        const double Sekunden = 150;
        var (eigen, marken) = Testszenen.Sprache(Sekunden, 31);
        var (fremd, _) = Testszenen.Sprache(Sekunden, 32);
        for (var i = 0; i < fremd.Length; i++) fremd[i] *= 0.05f;
        var signal = Testszenen.Summe(eigen, fremd);

        var gesendetAuto = new List<bool>();
        var gesendetLernend = new List<bool>();
        var auto = FuehrungsMessung.Messen(signal, marken, new Stimmhaftigkeit(), Standard, gesendetJeRahmen: gesendetAuto);
        var lernend = FuehrungsMessung.Messen(signal, marken, new Stimmhaftigkeit(),
            Standard with { Modus = GateModus.Lernend, KorridorVonDb = -62f, KorridorBisDb = -42f, AnstiegSekunden = 2f, RueckkehrSekunden = 10f },
            gesendetJeRahmen: gesendetLernend);
        // Anteil der Zeit außerhalb der eigenen Äußerungen (samt 400 ms Nachlauf), in der trotzdem gesendet wurde.
        var eigen400 = new bool[gesendetAuto.Count];
        foreach (var m in marken)
            for (var r = m.Start / 480; r < Math.Min(eigen400.Length, m.Ende / 480 + 40); r++) eigen400[r] = true;
        double Fremdanteil(List<bool> g) => g.Where((s, i) => !eigen400[i] && s).Count() / (double)eigen400.Count(e => !e);
        var fremdAuto = Fremdanteil(gesendetAuto);
        var fremdLernend = Fremdanteil(gesendetLernend);
        Console.WriteLine($"  Hintergrundstimmen, Automatisch: {auto}, fremd gesendet {fremdAuto:P0}");
        Console.WriteLine($"  Hintergrundstimmen, Lernend: {lernend}, fremd gesendet {fremdLernend:P0}");
        PruefeWert.Mindestens(0.2, fremdAuto, "automatisch sendet fremde Stimmen mit (die Szene misst etwas)");
        PruefeWert.Mindestens(1, auto.FehlausloesungenJeMinute, "automatisch öffnet auch allein auf fremde Stimmen");
        PruefeWert.Hoechstens(fremdAuto / 2, fremdLernend, "lernend: höchstens halb so viel Hintergrund gesendet");
        PruefeWert.Hoechstens(0.5, lernend.FehlausloesungenJeMinute, "lernend: Ziel aus dem Entwurf (unter 0,5 je Minute)");
        PruefeWert.Hoechstens(0.05, lernend.AnteilAbgeschnitten, "lernend: eigene Anlaute bleiben erhalten");
    }

    [Test]
    public static void Echoschutz_LerntKopplung_LaesstLautereStimmeDurch()
    {
        var schutz = new Echoschutz();
        Pruefe.Falsch(schutz.Verdacht(-30f, false, false), "ohne Wiedergabe nie Verdacht");
        // 5 s Wiedergabe bei -20 dBFS, Echo im Mikrofon 15 dB darunter: Die Kopplung lernt sich von 0 auf etwa -15 dB.
        for (var i = 0; i < 500; i++)
        {
            schutz.Wiedergabe(-20f);
            _ = schutz.Verdacht(-35f, false, false);
        }
        PruefeWert.Nahe(-15, schutz.KopplungDb, 0.2, "gelernte Kopplung");
        schutz.Wiedergabe(-20f);
        Pruefe.Wahr(schutz.Verdacht(-33f, false, false), "Echo (auch 2 dB lauter) ist Verdacht");
        Pruefe.Falsch(schutz.Verdacht(-24f, false, false), "eigene Stimme 11 dB über dem Echo darf öffnen");
        var vorher = schutz.KopplungDb;
        for (var i = 0; i < 100; i++) _ = schutz.Verdacht(-10f, true, false);
        Pruefe.Gleich(vorher, schutz.KopplungDb, "offenes Gate: Kopplung bleibt");
        for (var i = 0; i < 100; i++) _ = schutz.Verdacht(-10f, false, true);
        Pruefe.Gleich(vorher, schutz.KopplungDb, "Sprechtaste: Kopplung bleibt");
        // Stufe 2b: Nach dem Ende der Wiedergabe gilt der Schutz noch 400 ms, gelernt wird dort nicht mehr (das Mikrofon hört nur
        // Stille, die Kopplung fiele sonst um 0,05 dB je Rahmen).
        for (var i = 0; i < Echoschutz.HalteRahmen - 1; i++)
        {
            schutz.Wiedergabe(-100f);
            Pruefe.Wahr(schutz.Verdacht(-65f, false, false), "Haltezeit: Stille ist noch Verdacht");
        }
        Pruefe.Gleich(vorher, schutz.KopplungDb, "Haltezeit: Kopplung bleibt");
        schutz.Wiedergabe(-20f);
        _ = schutz.Verdacht(-35f, false, false);
        Pruefe.Wahr(schutz.KopplungDb != vorher, "spielt sie wieder, lernt sie wieder");
        // 400 ms nach dem Ende der Wiedergabe ist der Schutz vorbei.
        for (var i = 0; i < Echoschutz.HalteRahmen; i++) schutz.Wiedergabe(-100f);
        Pruefe.Falsch(schutz.Verdacht(-50f, false, false), "Wiedergabe still: kein Verdacht");
    }

    [Test]
    public static void Automatisch_LinieNieUnterUntergrenze()
    {
        // Hinter RNNoise ist der Boden oft digitale Stille; die Linie bleibt dann bei -60 dBFS.
        var f = new GateFuehrung { Einstellung = Standard };
        var (offen, linie) = Lauf(f, Folge(new float[480 * 300], Ton(40, -65f, -120f)), 0.9f);
        Pruefe.Wahr(linie.All(l => l >= GateVorgaben.MinLinieAutomatischDb), "Linie nie unter -60 dBFS");
        Pruefe.Falsch(offen.Any(o => o), "ein Rest bei -65 dBFS öffnet nicht");
    }

    /// <summary>
    /// Sarevoc (Stufe 2c): Eine Grenzlinie legt fest, wie tief die Automatik gehen darf. Im ruhigen Raum läge die Linie bei -60 dBFS
    /// (Boden unter der Untergrenze plus 9 dB); mit einer Grenzlinie bei -40 bleibt sie dort, und ein Ton 4 dB darunter öffnet
    /// nicht, obwohl er ohne Grenze öffnen würde. Über der Grenzlinie bestimmt weiter der Boden die Linie.
    /// </summary>
    [Test]
    public static void Automatisch_Grenzlinie_SchwelleNieDarunter()
    {
        float[] Szene(float ton) => Folge(AnbindungSignale.Rauschen(48000 * 2, -65, 5), Ton(50, ton, -65f));

        var ohne = new GateFuehrung { Einstellung = Standard };
        var (offenOhne, linieOhne) = Lauf(ohne, Szene(-44f), 0.95f);
        PruefeWert.Hoechstens(-50, linieOhne[190], "ohne Grenzlinie: Linie nahe Boden plus 9");
        Pruefe.Wahr(offenOhne[^1], "ohne Grenzlinie öffnet ein Ton bei -44 dBFS");

        var mit = new GateFuehrung { Einstellung = Standard with { GrenzlinieDb = -40f } };
        var (offenMit, linieMit) = Lauf(mit, Szene(-44f), 0.95f);
        Pruefe.Wahr(linieMit.All(l => l >= -40f), "die Linie geht nie unter die Grenzlinie");
        Pruefe.Gleich(-40f, linieMit[190], "im ruhigen Raum steht sie genau auf der Grenzlinie");
        Pruefe.Falsch(offenMit.Any(o => o), "derselbe Ton 4 dB unter der Grenzlinie öffnet nicht");

        var drueber = new GateFuehrung { Einstellung = Standard with { GrenzlinieDb = -40f } };
        var (offenDrueber, _) = Lauf(drueber, Szene(-34f), 0.95f);
        Pruefe.Wahr(offenDrueber[^1], "ein Ton 6 dB über der Grenzlinie öffnet");

        // Lauter Boden: Boden plus Abstand liegt über der Grenzlinie, die Linie folgt wieder dem Boden.
        var laut = new GateFuehrung { Einstellung = Standard with { GrenzlinieDb = -60f, AbstandDb = 12f } };
        var (_, linieLaut) = Lauf(laut, AnbindungSignale.Rauschen(48000 * 4, -40, 4), 0f);
        PruefeWert.Nahe(laut.Gate.RauschbodenDbfs + 12, linieLaut[^1], 0.5, "über der Grenzlinie: Boden plus Abstand");
    }

    /// <summary>
    /// Die Grenzlinie gilt nur für die Automatik. Im Modus Lernend hat der Korridor diese Rolle (seine Untergrenze ist die tiefste
    /// Linie), im Modus Fest gilt die gezogene Schwelle; eine gemerkte Grenzlinie ändert an beiden nichts.
    /// </summary>
    [Test]
    public static void Grenzlinie_NurAutomatisch_LernendHatDenKorridor_FestDieSchwelle()
    {
        var lernend = new GateFuehrung
        {
            Einstellung = Standard with { Modus = GateModus.Lernend, KorridorVonDb = -58f, KorridorBisDb = -35f, GrenzlinieDb = -45f },
        };
        var (_, linieLernend) = Lauf(lernend, AnbindungSignale.Rauschen(48000 * 3, -70, 11), 0f);
        PruefeWert.Nahe(-58, linieLernend[^1], 0.5, "Lernend im Ruhigen: an der Korridor-Untergrenze, nicht an der Grenzlinie");

        var fest = new GateFuehrung { Einstellung = Standard with { Modus = GateModus.Fest, SchwelleDb = -50f, GrenzlinieDb = -40f } };
        var (_, linieFest) = Lauf(fest, Ton(100, -80f), 1f);
        Pruefe.Wahr(linieFest.All(l => l == -50f), "Fest: die gezogene Schwelle, nicht die Grenzlinie");
    }

    /// <summary>
    /// Ab Werk ändert die Grenzlinie nichts (Sarevoc: der Klang bleibt ab Werk gleich). Die Maschine hat ohne Angabe -100 dBFS, die
    /// Oberfläche ab Werk -90 (ganz unten in der Grafik); die automatische Linie liegt nie unter -69 dBFS. Linie und Senden sind
    /// in beiden Fällen Rahmen für Rahmen gleich, im ruhigen Raum, bei Sprache und bei lautem Boden.
    /// </summary>
    [Test]
    public static void Grenzlinie_AbWerk_OhneWirkung()
    {
        Pruefe.Gleich(GateVorgaben.MinPegelDb, GateVorgaben.Standard.GrenzlinieDb, "Voreinstellung der Maschine ganz unten");
        var signal = Folge(new float[480 * 200], AnbindungSignale.Rauschen(48000 * 2, -65, 5), Ton(80, -40f, -65f), AnbindungSignale.Rauschen(48000 * 3, -38, 6));
        var a = new GateFuehrung { Einstellung = Standard };
        var b = new GateFuehrung { Einstellung = Standard with { GrenzlinieDb = -90f } };
        var (offenA, linieA) = Lauf(a, signal, 0.9f);
        var (offenB, linieB) = Lauf(b, signal, 0.9f);
        Pruefe.Wahr(linieA.SequenceEqual(linieB), "Linie Rahmen für Rahmen gleich");
        Pruefe.Wahr(offenA.SequenceEqual(offenB), "Senden Rahmen für Rahmen gleich");
        Pruefe.Wahr(offenA.Any(o => o), "die Szene sendet überhaupt");
    }

    [Test]
    public static void Bereinigen_NaNUndVertauschterKorridor()
    {
        var e = GateVorgaben.Bereinigen(new GateEinstellung((GateModus)7, float.NaN, 99f, -20f, -70f, 0f, float.PositiveInfinity, 2f, float.NaN));
        Pruefe.Gleich(GateVorgaben.MinPegelDb, e.GrenzlinieDb, "Grenzlinie NaN: ganz unten wie ab Werk");
        Pruefe.Gleich(GateVorgaben.MaxPegelDb, GateVorgaben.Bereinigen(Standard with { GrenzlinieDb = 12f }).GrenzlinieDb, "Grenzlinie höchstens 0 dBFS");
        Pruefe.Gleich(GateVorgaben.MinPegelDb, GateVorgaben.Bereinigen(Standard with { GrenzlinieDb = -500f }).GrenzlinieDb, "Grenzlinie mindestens -100 dBFS");
        Pruefe.Gleich(-42f, GateVorgaben.Bereinigen(Standard with { GrenzlinieDb = -42f }).GrenzlinieDb, "eine gültige Grenzlinie bleibt");
        Pruefe.Gleich(GateModus.Automatisch, e.Modus, "unbekannter Modus");
        Pruefe.Gleich(Standard.SchwelleDb, e.SchwelleDb, "NaN nimmt die Voreinstellung");
        Pruefe.Gleich(GateVorgaben.MaxAbstandDb, e.AbstandDb, "Abstand begrenzt");
        Pruefe.Gleich(-70f, e.KorridorVonDb, "Korridor umgedreht (von)");
        Pruefe.Gleich(-20f, e.KorridorBisDb, "Korridor umgedreht (bis)");
        Pruefe.Gleich(GateVorgaben.MinZeitSekunden, e.AnstiegSekunden, "Anstieg mindestens 0,1 s");
        Pruefe.Gleich(GateVorgaben.MaxZeitSekunden, e.RueckkehrSekunden, "Rückkehr höchstens 600 s");
        Pruefe.Gleich(1f, e.Sprachsicherheit, "Sicherheit höchstens 1");
    }
}
