// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Klang;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Klangfarbe (Q70 bis Q72, 0.9.3) in der echten Kette im Prüfstand: Gate, Führung, Korridor, Pegelanzeige, Telemetrie und Wächter sehen mit und
/// ohne Klangfarbe Rahmen für Rahmen dieselben Werte (auch beim Umstellen mitten im Spruch), gesendet wird aber der gefärbte Klang; die Hörprobe
/// hört ihn mit, die Stimmprobe nicht; Maschine und Attrappe begrenzen; im Takt keine Allokation.
/// </summary>
internal static class KlangfarbeMaschineTests
{
    private static readonly KlangfarbeEinstellung Bunt = new(6, 4, -6);

    /// <summary>Was ein Lauf von außen und innen zeigt, ohne die Rechenzeiten.</summary>
    private sealed record Lauf(List<TelemetrieEintrag> Telemetrie, List<PegelMeldung> Meldungen, List<(long Takt, Sprachkennzeichen K)> Pakete,
        List<byte[]> Opus, List<string> Stand);

    private static async Task<Lauf> Laufen(float[] signal, KlangfarbeEinstellung anfang, KlangfarbeEinstellung mitte)
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        s.Maschine.Klangfarbe = anfang;
        await s.StartenAsync();
        var stand = new List<string>();
        s.Abspielen(signal, t =>
        {
            // Umstellen mitten in einem Spruch der Sprechtaste, dazu Sprachaktivierung vorher und nachher.
            if (t == 600) s.Maschine.SprechtasteGedrueckt();
            if (t == 700) s.Maschine.Klangfarbe = mitte;
            if (t == 800) s.Maschine.SprechtasteLosgelassen();
            var k = s.Maschine.Kette;
            var g = k.Fuehrung.Gate;
            stand.Add(FormattableString.Invariant(
                $"{s.Maschine.Korridor} {g.Zustand} {g.MindestDbfs} {g.RauschbodenDbfs} {g.LetzterPegelDbfs} {k.Fuehrung.AnzeigeLinieDb} {k.Urteil} {k.Pegelstufe?.VerstaerkungDb}"));
        });
        return new Lauf([.. s.Telemetrie.Select(e => e with { Zeiten = default })], [.. s.Meldungen], [.. s.Pakete.Select(p => (p.Takt, p.Kennzeichen))],
            [.. s.Pakete.Select(p => p.Opus)], stand);
    }

    [Test]
    public static async Task Klangfarbe_GateUndAnzeigeSehenDenselbenPegel_GesendetWirdGefaerbt()
    {
        var (sprache, _) = Testszenen.Sprache(16, 73);
        var ohne = await Laufen(sprache, KlangfarbeEinstellung.Neutral, KlangfarbeEinstellung.Neutral);
        var mit = await Laufen(sprache, Bunt, new KlangfarbeEinstellung(-6, -6, 6));

        Pruefe.Wahr(ohne.Pakete.Count > 100 && ohne.Pakete.Any(p => (p.K & Sprachkennzeichen.Sprachaktivierung) != 0), "es wird gesendet, auch per Sprachaktivierung");
        Pruefe.Gleich(ohne.Telemetrie.Count, mit.Telemetrie.Count, "gleich viele Rahmen");
        for (var i = 0; i < ohne.Telemetrie.Count; i++)
            Pruefe.Gleich(ohne.Telemetrie[i], mit.Telemetrie[i], "Rahmen " + i + ": Pegel aller Stufen, Gate-Pegel, Wahrscheinlichkeit, Zustand und Senden");
        Pruefe.Wahr(ohne.Meldungen.SequenceEqual(mit.Meldungen), "Pegelanzeige: jede Meldung gleich");
        for (var i = 0; i < ohne.Stand.Count; i++) Pruefe.Gleich(ohne.Stand[i], mit.Stand[i], "Takt " + i + ": Korridor, Gate, Linie, Wächter, Lautstärke");
        Pruefe.Wahr(ohne.Pakete.SequenceEqual(mit.Pakete), "dieselben Pakete zur selben Zeit mit denselben Kennzeichen");
        var anders = ohne.Opus.Zip(mit.Opus).Count(p => !p.First.AsSpan().SequenceEqual(p.Second));
        PruefeWert.Mindestens(0.9 * ohne.Opus.Count(o => o.Length > 0), anders, "gesendet wird der gefärbte Klang (fast jedes Audiopaket anders)");
    }

    [Test]
    public static async Task Klangfarbe_HoerprobeHoertMit_StimmprobeNicht()
    {
        var (sprache, _) = Testszenen.Sprache(6, 74);
        async Task<(float[] Stimmprobe, float[] Wiedergabe)> Probe(KlangfarbeEinstellung e)
        {
            await using var s = new Pruefstand();
            s.Maschine.Sprachaktivierung = false;
            s.Maschine.Klangfarbe = e;
            await s.StartenAsync();
            s.Stille(20);
            s.Maschine.ProbeStarten();
            s.Maschine.StimmprobeStarten(4000);
            s.Abspielen(sprache.AsSpan(0, IKlangMaschine.ProbeDauerMs * 48 + 4800));
            s.Mitschreiben = true;
            s.Stille((IKlangMaschine.ProbeDauerMs / 10) + 40);
            Pruefe.Gleich(ProbeZustand.Bereit, s.Maschine.Probe.Zustand, "Probe abgespielt");
            return (s.Maschine.StimmprobeHolen()!, [.. s.Ausgabe]);
        }
        var neutral = await Probe(KlangfarbeEinstellung.Neutral);
        var warm = await Probe(new KlangfarbeEinstellung(6, 0, -6));
        Pruefe.Wahr(neutral.Stimmprobe.AsSpan().SequenceEqual(warm.Stimmprobe), "die Stimmprobe greift vor der Klangfarbe ab: bitgleich");

        // Die Wiedergabe der Probe: mit Wärme +6 und Schärfe -6 mehr Tiefe gegen die Höhen als neutral.
        static double TiefGegenHoch(float[] x)
        {
            var (tief, hoch) = (Biquad.Tiefpass(300, 0.7071), Biquad.Hochpass(4000, 0.7071));
            double pt = 0, ph = 0;
            foreach (var v in x)
            {
                var a = tief.Schritt(v);
                var b = hoch.Schritt(v);
                pt += a * a;
                ph += b * b;
            }
            return 10 * Math.Log10(pt / ph);
        }
        var unterschied = TiefGegenHoch(warm.Wiedergabe) - TiefGegenHoch(neutral.Wiedergabe);
        Console.WriteLine(FormattableString.Invariant($"  Hörprobe: Tiefe gegen Höhe mit Wärme +6, Schärfe -6 um {unterschied:0.0} dB mehr als neutral"));
        // Gemessen 4,3 dB (die Testszene hat über 4 kHz wenig); ohne Klangfarbe in der Probe wäre es 0, denn sonst ist alles gleich.
        PruefeWert.Mindestens(3, unterschied, "die Hörprobe hört die Klangfarbe mit");
        PruefeWert.Nahe(Pegel.Dbfs(neutral.Wiedergabe), Pegel.Dbfs(warm.Wiedergabe), 1.5, "und ungefähr gleich laut");
    }

    [Test]
    public static async Task Klangfarbe_MaschineUndAttrappe_AbWerkNeutral_Begrenzt()
    {
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false);
        var a = new KlangMaschinenAttrappe();
        Pruefe.Gleich(KlangfarbeEinstellung.Neutral, s.Maschine.Klangfarbe, "die Maschine ab Werk neutral");
        Pruefe.Gleich(KlangfarbeEinstellung.Neutral, a.Klangfarbe, "die Attrappe ab Werk neutral");
        foreach (var (ein, aus) in new[]
                 {
                     (new KlangfarbeEinstellung(10, -10, 2), new KlangfarbeEinstellung(6, -6, 2)),
                     (new KlangfarbeEinstellung(int.MaxValue, int.MinValue, -6), new KlangfarbeEinstellung(6, -6, -6)),
                     (new KlangfarbeEinstellung(1, 0, 0), new KlangfarbeEinstellung(1, 0, 0)),
                 })
        {
            s.Maschine.Klangfarbe = ein;
            a.Klangfarbe = ein;
            Pruefe.Gleich(aus, s.Maschine.Klangfarbe, $"Maschine: {ein}");
            Pruefe.Gleich(aus, s.Maschine.Kette.KlangfarbeStufe.Einstellung, $"bis in die Kette: {ein}");
            Pruefe.Gleich(aus, a.Klangfarbe, $"Attrappe: {ein}");
        }
        await s.StartenAsync();
        s.Stille(3);
        Pruefe.Wahr(s.Maschine.Kette.KlangfarbeStufe is { Ueberbrueckt: false } k && k.Wirksam == new KlangfarbeEinstellung(1, 0, 0), "nach der Blende wirkt sie");
        s.Maschine.Klangfarbe = KlangfarbeEinstellung.Neutral;
        s.Stille(5);
        Pruefe.Wahr(s.Maschine.Kette.KlangfarbeStufe.Ueberbrueckt, "zurück auf neutral: überbrückt");
    }

    /// <summary>Wie <see cref="KlangMaschineTests.Echtzeit_AudioPfade_AllokierenNicht"/>, mit wirkender Klangfarbe und Umstellen im Takt.</summary>
    [Test]
    public static async Task Klangfarbe_Echtzeit_MitUmstellen_AllokiertNicht()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Hoerprobe = true;
        s.Maschine.Klangfarbe = Bunt;
        var (signal, _) = Testszenen.Sprache(10, 75);
        var wiedergabe = new float[960];
        var stellungen = new[] { new KlangfarbeEinstellung(-3, 2, 6), KlangfarbeEinstellung.Neutral, Bunt };
        void Schritt(int t)
        {
            s.Uhr.Us += 10_000;
            s.System.Wiedergabe!.Ziehen(wiedergabe);
            s.System.Loopback!.Liefern(wiedergabe);
            s.System.Aufnahme!.Liefern(signal.AsSpan(t % 900 * 480, 480));
            if (t % 100 == 30) s.Maschine.Klangfarbe = stellungen[t / 100 % stellungen.Length];
            if (t % 300 == 150) s.Maschine.SprechtasteGedrueckt();
            if (t % 300 == 250) s.Maschine.SprechtasteLosgelassen();
        }
        for (var t = 0; t < 400; t++)
        {
            Schritt(t);
            s.Maschine.Zustellen();
        }
        var bytes = PruefeWert.Allokiert(() =>
        {
            for (var t = 400; t < 1000; t++) Schritt(t);
        });
        s.Maschine.Zustellen();
        Pruefe.Gleich(0L, bytes, "allokierte Bytes in 6 s mit Klangfarbe und Umstellen");
        Pruefe.Wahr(s.Maschine.Statistik.Pakete > 50, "es wurde gesendet");
    }
}
