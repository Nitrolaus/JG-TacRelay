// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Messung aus der Gegenprüfung 0.6.0 Stufe 2c, nur von Hand: FUNK_MESSUNG=1. Sie prüft nichts, sie zeigt, was die Grenzlinie der
/// Automatik (<see cref="GateEinstellung.GrenzlinieDb"/>) im Klang bewirkt: dieselben Szenen wie <see cref="MessungSpielton"/> und
/// <see cref="MessungAntwortUndKlick"/> (feste Seeds, Voreinstellung der Maschine), je Grenzlinie einmal. Ab Werk steht sie im Kern
/// bei -100 und in der Oberfläche bei -90 dBFS; beide müssen dieselben Zahlen geben wie ohne Grenzlinie. Andere Werte über
/// <c>FUNK_GRENZLINIEN</c> (Komma, dBFS). Gesetzt wird sie über <see cref="Pruefstand.GateFuerMessung"/>, so laufen die Szenen
/// unverändert.
/// <para>
/// Spalten: verpasste Anlaute (keine Phase spätestens 100 ms nach dem Anlaut) von allen, mit "F" Phasen ohne eigene Rede; H Headset
/// ohne und mit Spielton -24 dBFS, H leise dieselbe Sprache 12 dB leiser ins Mikrofon; L Lautsprecher 12/20/30 dB mit Echoschutz;
/// Spielstimme Auslösungen durch sprachartigen Spielton über Lautsprecher (12 dB, verzerrt); Spielton- und Musik-Echo
/// Auslösungen in je 105 Szenen zu 40 s; Antwort und Ins-Wort-Fallen nach fremdem Funk (verpasst, mittlerer Verzug ab Anlaut,
/// Fehlphasen); Echo-F Fehlphasen der Echoläufe 3/12 dB/verzerrt.
/// </para>
/// <para>
/// Gemessen am 03.10.2026 auf modul/ui2c (2fde4e3 und diese Messung): -100, -90 und -60 dBFS Zahl für Zahl gleich (H ohne 3/68, H -24
/// 7/68, H leise 5/68, L 21/16/17 von 65, Antwort 254/1128 mit 54 ms, Ins-Wort 370/564, alle Auslösungen 0) und gleich main 46ed581.
/// -50 dBFS: H ohne 5/68, H -24 11/68, Antwort 282/1128 mit 60 ms, Ins-Wort 382/564, Auslösungen weiter 0; -40 dBFS: H -24 19/68,
/// Antwort 389/1128 mit 79 ms. In diesen Szenen kostet eine hohe Grenzlinie also Anlaute und gewinnt nichts, denn Fehlauslösungen gab
/// es schon ohne sie keine. Einen Gewinn zeigte erst eine Szene, in der die Automatik ohne Grenze zu tief liegt; die gibt es hier nicht.
/// </para>
/// </summary>
internal static class MessungGrenzlinie
{
    private static readonly int[] Seeds = [151, 171, 181];

    private static readonly float[] Kopplungen = [0.25f, 0.1f, 0.0316f];

    private static readonly Func<float, float>[] EchoPfade =
        [KlangMaschineTests.Kopplung3Db, KlangMaschineTests.Kopplung12Db, x => 0.1f * x, x => 0.0316f * x, KlangMaschineTests.KopplungVerzerrt];

    private static readonly Func<float, float>?[] AntwortPfade =
        [null, KlangMaschineTests.Kopplung3Db, KlangMaschineTests.Kopplung12Db, x => 0.1f * x, x => 0.0316f * x, KlangMaschineTests.KopplungVerzerrt];

    [Test]
    public static async Task Messung_Grenzlinie()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") != "1")
        {
            Console.WriteLine("HINWEIS Messung Grenzlinie übersprungen (FUNK_MESSUNG=1 setzen).");
            return;
        }
        var c = CultureInfo.InvariantCulture;
        var grenzen = (Environment.GetEnvironmentVariable("FUNK_GRENZLINIEN") ?? "-100,-90,-60,-50,-40")
            .Split(',').Select(g => float.Parse(g, c)).ToArray();
        foreach (var grenze in grenzen)
        {
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            Pruefstand.GateFuerMessung.Value = GateVorgaben.Standard with { GrenzlinieDb = grenze };
            try
            {
                Func<int, int, float[]> spiel24 = (n, seed) => MessungSpielton.Spielton(n, -24f, seed);
                var hOhne = MessungSpielton.HeadsetDreiSeeds(null, null);
                var hSpiel = MessungSpielton.HeadsetDreiSeeds(null, spiel24);
                var hLeise = Task.WhenAll(Seeds.Select(seed => Task.Run(() => HeadsetLeise(seed, -12f))));
                var laut = Task.WhenAll(Kopplungen.Select(k => MessungSpielton.LautsprecherDreiSeeds(null, k, MessungSpielton.Schutz.Neu)));
                var stimme12 = Task.Run(() => MessungSpielton.Spielstimme(null, KlangMaschineTests.Kopplung12Db, MessungSpielton.Schutz.Neu));
                var stimmeVerz = Task.Run(() => MessungSpielton.Spielstimme(null, KlangMaschineTests.KopplungVerzerrt, MessungSpielton.Schutz.Neu));
                var spielEcho = new List<Task<int>>();
                var musikEcho = new List<Task<int>>();
                foreach (var seed in new[] { 152, 162, 172 })
                {
                    foreach (var db in new[] { -42f, -36f, -33f, -30f, -27f, -24f, -18f })
                    {
                        foreach (var pfad in EchoPfade)
                        {
                            spielEcho.Add(Task.Run(() => MessungSpielton.SpieltonEcho(seed, db, pfad, false, null)));
                            musikEcho.Add(Task.Run(() => MessungSpielton.SpieltonEcho(seed + 1, db, pfad, true, null)));
                        }
                    }
                }
                var antwort = Antworten([150, 300, 450, 600, 900]);
                var insWort = Antworten([-600, -300]);
                var echo = Task.WhenAll(new[] { KlangMaschineTests.Kopplung3Db, KlangMaschineTests.Kopplung12Db, KlangMaschineTests.KopplungVerzerrt }
                    .Select(p => Task.Run(() => KlangMaschineTests.EchoLauf(p, true, true, false))));

                var (ohne, spiel, leise) = (await hOhne, await hSpiel, (await hLeise).Aggregate((a, b) => a + b));
                var (l, e) = (await laut, await echo);
                var (s12, sVerz) = (await stimme12, await stimmeVerz);
                var (fehlSpiel, fehlMusik) = ((await Task.WhenAll(spielEcho)).Sum(), (await Task.WhenAll(musikEcho)).Sum());
                var (a1, a2) = (await antwort, await insWort);
                Console.WriteLine(string.Create(c,
                    $"  MESSUNG Grenzlinie {grenze} dBFS | H ohne {ohne} | H -24 {spiel} | H leise {leise} | L 12/20/30 {l[0]} {l[1]} {l[2]}" +
                    $" | Spielstimme 12/verz {s12}/{sVerz} | Spielton-Echo {fehlSpiel}/{spielEcho.Count} Musik-Echo {fehlMusik}/{musikEcho.Count}" +
                    $" | Antwort {a1} | InsWort {a2} | Echo-F 3/12/verz {e[0].Fehlphasen} {e[1].Fehlphasen} {e[2].Fehlphasen} | {uhr.Elapsed.TotalSeconds:F0} s"));
            }
            finally
            {
                Pruefstand.GateFuerMessung.Value = null;
            }
        }
    }

    /// <summary>Headset ohne Spielton, die Sprache um <paramref name="db"/> leiser im Mikrofon (leiser Sprecher, Mikrofon weiter weg).</summary>
    private static async Task<MessungSpielton.Anlaute> HeadsetLeise(int seed, float db)
    {
        var (sprache, marken) = Testszenen.Sprache(60, seed);
        var faktor = Pegel.DbZuLinear(db);
        for (var i = 0; i < sprache.Length; i++) sprache[i] *= faktor;
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Abspielen(sprache);
        return MessungSpielton.Auswerten(s.Pakete, marken, 300);
    }

    /// <summary>Antworten nach fremdem Funk über alle Echopfade und drei Seeds: "verpasst/gesamt, Verzug ms, F Fehlphasen".</summary>
    private static async Task<string> Antworten(int[] abstaende)
    {
        var laeufe = new List<Task<MessungAntwortUndKlick.Ergebnis>>();
        foreach (var pfad in AntwortPfade)
        {
            foreach (var abstand in abstaende)
            {
                foreach (var seed in Seeds) laeufe.Add(Task.Run(() => MessungAntwortUndKlick.Szene(pfad, abstand, seed)));
            }
        }
        var e = await Task.WhenAll(laeufe);
        var verzug = e.Sum(x => x.VerzugSumme) / Math.Max(1, e.Sum(x => x.VerzugAnzahl));
        return string.Create(CultureInfo.InvariantCulture, $"{e.Sum(x => x.Verpasst)}/{e.Sum(x => x.Gesamt)} {verzug:F0} ms F{e.Sum(x => x.Fehl)}");
    }
}
