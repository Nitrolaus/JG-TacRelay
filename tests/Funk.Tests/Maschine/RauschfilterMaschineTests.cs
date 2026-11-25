// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>Rauschfilter Standard, Stark und Aus in der echten Klangmaschine (Klangstufe B): Wechsel, Rückfall, Freigabe, Allokation.</summary>
internal static class RauschfilterMaschineTests
{
    private static float[] Rauschen(int rahmen, ulong saat) => AnbindungSignale.Rauschen(rahmen * 480, -50, saat);

    [Test]
    public static async Task Rauschfilter_AbWerkStandard()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Abspielen(Rauschen(20, 1));
        var stand = s.Maschine.Rauschfilterstand;
        Pruefe.Gleich(Rauschfilter.Standard, stand.Gewaehlt, "gewählt");
        Pruefe.Gleich(Rauschfilter.Standard, stand.Wirksam, "wirkt");
        Pruefe.Gleich<string?>(null, stand.Rueckfall, "kein Rückfall");
        Pruefe.Falsch(s.Maschine.Kette.HatStark, "kein DeepFilterNet3 angelegt");
    }

    [Test]
    public static async Task Stark_WirktNachAnlauf_UndWirdNachDemZurueckschaltenFreigegeben()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Rauschfilter = Rauschfilter.Stark;
        Pruefe.Wahr(s.Maschine.Kette.HatStark, "DeepFilterNet3 angelegt (ohne eigene Threads gleich im Aufrufer)");
        s.Abspielen(Rauschen(Rauschfilterwahl.AnlaufRahmen - 2, 2));
        Pruefe.Gleich(Rauschfilter.Standard, s.Maschine.Rauschfilterstand.Wirksam, "im Anlauf wirkt noch Standard");
        s.Abspielen(Rauschen(Rauschfilterwahl.BlendeRahmen + 5, 3));
        Pruefe.Gleich(Rauschfilter.Stark, s.Maschine.Rauschfilterstand.Wirksam, "nach Anlauf und Blende wirkt Stark");

        s.Maschine.Rauschfilter = Rauschfilter.Standard;
        s.Abspielen(Rauschen(Rauschfilterwahl.BlendeRahmen + 3, 4));
        Pruefe.Gleich(Rauschfilter.Standard, s.Maschine.Rauschfilterstand.Wirksam, "zurück auf Standard");
        Pruefe.Falsch(s.Maschine.Kette.HatStark, "DeepFilterNet3 abgelegt und beim Zustellen freigegeben");
    }

    [Test]
    public static async Task Aus_WirktUndKehrtZurueck()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Rauschfilter = Rauschfilter.Aus;
        s.Abspielen(Rauschen(Rauschfilterwahl.BlendeRahmen + 2, 5));
        Pruefe.Gleich(Rauschfilter.Aus, s.Maschine.Rauschfilterstand.Wirksam, "Aus wirkt");
        s.TelemetrieMitschreiben = true;
        s.Abspielen(Rauschen(10, 6));
        var t = s.Telemetrie.TakeLast(10).ToList();
        Pruefe.Wahr(t.All(e => Math.Abs(e.RauschfilterDb) < 0.01f), "ohne Filter keine Wirkung in der Telemetrie");
        Pruefe.Wahr(t.All(e => Math.Abs(e.GatePegelDbfs - e.VerstaerktDbfs) < 0.01f), "das Gate sieht den Eingang des Filters");
        s.Maschine.Rauschfilter = Rauschfilter.Standard;
        s.Abspielen(Rauschen(Rauschfilterwahl.BlendeRahmen + 2, 7));
        Pruefe.Gleich(Rauschfilter.Standard, s.Maschine.Rauschfilterstand.Wirksam, "Standard wirkt wieder");
    }

    [Test]
    public static async Task Stark_DllFehlt_StandardMitGrund_UndNeuWaehlenVersuchtWieder()
    {
        var versuche = 0;
        await using var s = new Pruefstand(deepFilter: () =>
        {
            versuche++;
            throw new DllNotFoundException("deepfilternet.dll fehlt");
        });
        await s.StartenAsync();
        s.Maschine.Rauschfilter = Rauschfilter.Stark;
        s.Abspielen(Rauschen(Rauschfilterwahl.AnlaufRahmen + 10, 8));
        var stand = s.Maschine.Rauschfilterstand;
        Pruefe.Gleich(Rauschfilter.Stark, stand.Gewaehlt, "gewählt bleibt Stark");
        Pruefe.Gleich(Rauschfilter.Standard, stand.Wirksam, "wirkt Standard");
        Pruefe.Wahr(stand.Rueckfall is { } r && r.Contains("ließ sich nicht laden", StringComparison.Ordinal) && r.Contains("deepfilternet.dll fehlt", StringComparison.Ordinal),
            "Grund mit der Meldung: " + stand.Rueckfall);
        Pruefe.Gleich(1, versuche, "ein Versuch");
        s.Maschine.Rauschfilter = Rauschfilter.Standard;
        s.Maschine.Rauschfilter = Rauschfilter.Stark;
        Pruefe.Gleich(2, versuche, "neu gewählt: neuer Versuch");
    }

    /// <summary>Eine Sprachschätzung, die ab <see cref="LangsamAb"/> jeden Rahmen 12 ms braucht: Die Kette verpasst dann ihre Frist.</summary>
    private sealed class LangsameSchaetzung : ISprachwahrscheinlichkeit
    {
        public bool LangsamAb { get; set; }

        public float Schaetzen(ReadOnlySpan<float> rahmen10ms)
        {
            if (LangsamAb) Thread.Sleep(12);
            return 0f;
        }

        public void Zuruecksetzen() { }
    }

    [Test]
    public static async Task Stark_ZuWenigRechenzeit_FaelltAufStandardZurueck_NeustartVersuchtWieder()
    {
        var schaetzung = new LangsameSchaetzung();
        await using var s = new Pruefstand(schaetzer: schaetzung);
        await s.StartenAsync();
        s.Maschine.Rauschfilter = Rauschfilter.Stark;
        s.Abspielen(Rauschen(Rauschfilterwahl.AnlaufRahmen + Rauschfilterwahl.BlendeRahmen + 5, 9));
        Pruefe.Gleich(Rauschfilter.Stark, s.Maschine.Rauschfilterstand.Wirksam, "Stark wirkt");

        schaetzung.LangsamAb = true;
        s.Abspielen(Rauschen(Sendekette.RueckfallFristen - 1, 10));
        Pruefe.Gleich<string?>(null, s.Maschine.Rauschfilterstand.Rueckfall, "zwei verpasste Fristen reichen nicht");
        s.Abspielen(Rauschen(1, 11));
        schaetzung.LangsamAb = false;
        s.Abspielen(Rauschen(Rauschfilterwahl.BlendeRahmen + 2, 12));
        var stand = s.Maschine.Rauschfilterstand;
        Pruefe.Gleich(Rauschfilter.Standard, stand.Wirksam, "beim dritten Mal zurück auf Standard");
        Pruefe.Wahr(stand.Rueckfall?.Contains("10 ms", StringComparison.Ordinal) == true, "Grund: " + stand.Rueckfall);
        Pruefe.Falsch(s.Maschine.Kette.HatStark, "DeepFilterNet3 freigegeben");

        await s.Maschine.StoppenAsync();
        await s.StartenAsync();
        Pruefe.Gleich<string?>(null, s.Maschine.Rauschfilterstand.Rueckfall, "Neustart hebt den Rückfall auf");
        Pruefe.Wahr(s.Maschine.Kette.HatStark, "und legt DeepFilterNet3 neu an");
    }

    /// <summary>Wie <see cref="KlangMaschineTests.Echtzeit_AudioPfade_AllokierenNicht"/>, mit Stark und mit dem Wechsel zwischen Standard und Aus.</summary>
    [Test]
    public static async Task Echtzeit_MitStarkUndWechsel_AllokiertNicht()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Rauschfilter = Rauschfilter.Stark;
        var signal = AnbindungSignale.Sprache(480 * 400, 31);
        var rauschen = Rauschen(400, 32);
        for (var i = 0; i < signal.Length; i++) signal[i] += rauschen[i];
        // Wie im Vorbild ohne Takt und Zustellen des Prüfstands: Die sammeln Pakete und Pegel in Listen, das allokiert im Test.
        var wiedergabe = new float[960];
        void Schritt(int t)
        {
            s.Uhr.Us += 10_000;
            s.System.Wiedergabe!.Ziehen(wiedergabe);
            s.System.Loopback!.Liefern(wiedergabe);
            s.System.Aufnahme!.Liefern(signal.AsSpan(t % 400 * 480, 480));
            if (t % 200 == 50) s.Maschine.SprechtasteGedrueckt();
            if (t % 200 == 150) s.Maschine.SprechtasteLosgelassen();
        }
        for (var t = 0; t < 300; t++)
        {
            Schritt(t);
            s.Maschine.Zustellen();
        }
        Pruefe.Gleich(Rauschfilter.Stark, s.Maschine.Rauschfilterstand.Wirksam, "Stark wirkt");
        var bytes = PruefeWert.Allokiert(() =>
        {
            for (var t = 300; t < 900; t++) Schritt(t);
        });
        Pruefe.Gleich(0L, bytes, "Bytes in 6 s mit Stark");
        s.Maschine.Rauschfilter = Rauschfilter.Standard;
        for (var t = 900; t < 1000; t++)
        {
            Schritt(t);
            s.Maschine.Zustellen();
        }
        var wechsel = PruefeWert.Allokiert(() =>
        {
            for (var t = 1000; t < 1600; t++)
            {
                if (t % 100 == 0) s.Maschine.Rauschfilter = (t / 100) % 2 == 0 ? Rauschfilter.Aus : Rauschfilter.Standard;
                Schritt(t);
            }
        });
        Pruefe.Gleich(0L, wechsel, "Bytes in 6 s mit Wechseln zwischen Standard und Aus");
        s.Maschine.Zustellen();
        Pruefe.Wahr(s.Maschine.Statistik.Pakete > 100, "es wurde gesendet");
    }
}
