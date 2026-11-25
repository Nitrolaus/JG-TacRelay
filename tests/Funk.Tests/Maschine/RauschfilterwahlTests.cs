// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;

namespace Funk.Tests.Maschine;

/// <summary>
/// Umschalten zwischen den Rauschfiltern (Klangstufe B, Recherche Abschnitt 3) ohne native Filter: Die Wege werden als ideale
/// Verzögerungen nachgebildet (Standard = Eingang zwei Rahmen alt, Stark = drei Rahmen alt), jeder mit eigenem Faktor, damit man
/// in der Ausgabe sieht, welcher Weg wirkt und ob die Wege zeitrichtig liegen.
/// </summary>
internal static class RauschfilterwahlTests
{
    private const int R = 480;

    /// <summary>Nachgebildete Kette: Wert eines Samples = seine Nummer; Standard und Stark mit Faktor und Verzug.</summary>
    private sealed class Strecke(float standardFaktor = 1f, float starkFaktor = 1f)
    {
        public Rauschfilterwahl Wahl { get; } = new();
        public long Rahmen { get; private set; }
        public bool StarkAus { get; set; }
        private readonly float[] _roh = new float[R];
        private readonly float[] _standard = new float[R];
        private readonly float[] _stark = new float[R];
        public float[] Aus { get; } = new float[R];

        /// <summary>Wert, den Sample i von Rahmen k mit d Rahmen Verzug trägt.</summary>
        public static float Wert(long k, int i, int d) => k - d < 0 ? 0f : ((k - d) * R) + i;

        public void Takt(bool pause = true)
        {
            for (var i = 0; i < R; i++)
            {
                _roh[i] = Wert(Rahmen, i, 0);
                _standard[i] = standardFaktor * Wert(Rahmen, i, 2);
                _stark[i] = starkFaktor * Wert(Rahmen, i, 3);
            }
            var stark = Wahl.StarkGebraucht && !StarkAus ? (ReadOnlySpan<float>)_stark : [];
            Wahl.Verarbeiten(_roh, _standard, stark, pause, Aus);
            Rahmen++;
        }
    }

    [Test]
    public static void Standard_ZweiRahmenVerzug_OhneStark()
    {
        var s = new Strecke();
        for (var k = 0; k < 10; k++) s.Takt();
        Pruefe.Gleich(Rauschfilter.Standard, s.Wahl.Aktiv, "wirkt");
        Pruefe.Gleich(2, s.Wahl.Verzug, "Verzug in Rahmen");
        Pruefe.Falsch(s.Wahl.StarkGebraucht, "DeepFilterNet3 rechnet nicht");
        Pruefe.Gleich(Strecke.Wert(9, 17, 2), s.Aus[17], "Ausgang ist Standard");
    }

    [Test]
    public static void Aus_ImSprechen_UeberblendetSofort_VerzugErstInDerPause()
    {
        var s = new Strecke(standardFaktor: 0.5f);
        for (var k = 0; k < 5; k++) s.Takt();
        s.Wahl.Ziel = Rauschfilter.Aus;
        for (var k = 0; k < Rauschfilterwahl.BlendeRahmen; k++) s.Takt(pause: false);
        Pruefe.Gleich(Rauschfilter.Aus, s.Wahl.Aktiv, "nach der Blende wirkt Aus");
        s.Takt(pause: false);
        Pruefe.Gleich(2, s.Wahl.Verzug, "im Sprechen bleibt der Verzug");
        Pruefe.Gleich(Strecke.Wert(s.Rahmen - 1, 5, 2), s.Aus[5], "ungefilterter Eingang zeitrichtig zwei Rahmen alt");
        s.Takt(pause: true);
        Pruefe.Gleich(0, s.Wahl.Verzug, "in der Pause fällt der Verzug weg");
        Pruefe.Gleich(Strecke.Wert(s.Rahmen - 1, 5, 0), s.Aus[5], "Eingang ohne Verzug");
    }

    [Test]
    public static void Blende_LinearUndStetigZwischenZeitrichtigenWegen()
    {
        var s = new Strecke(standardFaktor: 1f);
        for (var k = 0; k < 5; k++) s.Takt();
        s.Wahl.Ziel = Rauschfilter.Aus;
        // Beide Wege liegen zeitrichtig und gleich laut: Die Blende darf nichts verändern, der Ausgang läuft Sample für Sample weiter.
        var vorher = s.Aus[R - 1];
        for (var k = 0; k < Rauschfilterwahl.BlendeRahmen + 2; k++)
        {
            s.Takt(pause: false);
            Pruefe.Wahr(Math.Abs(vorher + 1 - s.Aus[0]) < 0.01f, $"Rahmen {k}: stetig am Rahmenanfang ({vorher} -> {s.Aus[0]})");
            for (var i = 1; i < R; i++) Pruefe.Wahr(Math.Abs(s.Aus[i] - s.Aus[i - 1] - 1f) < 1e-3f * Math.Max(1f, Math.Abs(s.Aus[i])), $"Rahmen {k}, Sample {i}");
            vorher = s.Aus[R - 1];
        }
    }

    [Test]
    public static void Blende_DauertFuenfRahmenLinear()
    {
        var s = new Strecke(standardFaktor: 0f);
        for (var k = 0; k < 5; k++) s.Takt();
        s.Wahl.Ziel = Rauschfilter.Aus;
        var gewicht = new List<float>();
        for (var k = 0; k < Rauschfilterwahl.BlendeRahmen; k++)
        {
            s.Takt(pause: false);
            for (var i = 0; i < R; i += 120) gewicht.Add(s.Aus[i] / Strecke.Wert(s.Rahmen - 1, i, 2));
        }
        for (var j = 1; j < gewicht.Count; j++) Pruefe.Wahr(gewicht[j] > gewicht[j - 1], $"Gewicht steigt ({gewicht[j - 1]:0.000} -> {gewicht[j]:0.000})");
        Pruefe.Wahr(gewicht[0] < 0.01f && gewicht[^1] > 0.9f, $"von 0 auf 1 über {Rauschfilterwahl.BlendeRahmen * 10} ms ({gewicht[0]:0.000} bis {gewicht[^1]:0.000})");
    }

    [Test]
    public static void Stark_ErstNachDemAnlauf_UndMitDreiRahmenVerzug()
    {
        var s = new Strecke(standardFaktor: 0.5f, starkFaktor: 1f);
        for (var k = 0; k < 5; k++) s.Takt();
        s.Wahl.Ziel = Rauschfilter.Stark;
        Pruefe.Wahr(s.Wahl.StarkGebraucht, "DeepFilterNet3 rechnet ab dem Wunsch");
        for (var k = 0; k < Rauschfilterwahl.AnlaufRahmen - 1; k++) s.Takt(pause: false);
        Pruefe.Gleich(Rauschfilter.Standard, s.Wahl.Aktiv, "vor dem Ende des Anlaufs wirkt Standard");
        Pruefe.Gleich(2, s.Wahl.Verzug, "im Sprechen kein Sprung im Verzug");
        s.Takt(pause: true);
        Pruefe.Gleich(3, s.Wahl.Verzug, "in der Pause auf drei Rahmen");
        for (var k = 0; k < Rauschfilterwahl.BlendeRahmen; k++) s.Takt(pause: false);
        Pruefe.Gleich(Rauschfilter.Stark, s.Wahl.Aktiv, "nach Anlauf und Blende wirkt Stark");
        s.Takt(pause: false);
        Pruefe.Gleich(Strecke.Wert(s.Rahmen - 1, 33, 3), s.Aus[33], "Ausgang ist Stark, drei Rahmen alt");
    }

    [Test]
    public static void Stark_ZurueckAufStandard_ImSprechen_ZeitrichtigUeberblendet()
    {
        var s = StarkWirkt();
        s.Wahl.Ziel = Rauschfilter.Standard;
        for (var k = 0; k < Rauschfilterwahl.BlendeRahmen; k++)
        {
            s.Takt(pause: false);
            Pruefe.Wahr(Math.Abs(Strecke.Wert(s.Rahmen - 1, 0, 3) - s.Aus[0]) < 0.01f, $"Rahmen {k}: Standard liegt in der Blende zeitrichtig auf Stark ({s.Aus[0]})");
        }
        Pruefe.Gleich(Rauschfilter.Standard, s.Wahl.Aktiv, "Standard wirkt");
        Pruefe.Gleich(3, s.Wahl.Verzug, "Verzug bleibt bis zur Pause");
        Pruefe.Falsch(s.Wahl.StarkGebraucht, "DeepFilterNet3 rechnet nicht mehr");
        s.Takt(pause: true);
        Pruefe.Gleich(2, s.Wahl.Verzug, "in der Pause zurück auf zwei Rahmen");
    }

    [Test]
    public static void Stark_OhneAusgabe_SofortStandardZeitrichtig()
    {
        var s = StarkWirkt();
        s.StarkAus = true;
        s.Takt(pause: false);
        Pruefe.Gleich(Rauschfilter.Standard, s.Wahl.Aktiv, "ohne Ausgabe von DeepFilterNet3 sofort Standard");
        Pruefe.Gleich(Strecke.Wert(s.Rahmen - 1, 7, 3), s.Aus[7], "Standard um einen Rahmen nachgezogen, kein Sprung in der Zeit");
    }

    [Test]
    public static void Verarbeiten_AllokiertNicht()
    {
        var s = new Strecke();
        s.Wahl.Ziel = Rauschfilter.Stark;
        for (var k = 0; k < 80; k++) s.Takt();
        var vor = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 200; k++)
        {
            s.Wahl.Ziel = (k / 60) % 2 == 0 ? Rauschfilter.Aus : Rauschfilter.Stark;
            s.Takt(pause: k % 3 == 0);
        }
        Pruefe.Gleich(0L, GC.GetAllocatedBytesForCurrentThread() - vor, "Bytes in 200 Rahmen mit Umschalten");
    }

    /// <summary>Strecke, in der Stark nach Anlauf und Blende wirkt (Standard halb so laut, damit man die Wege unterscheidet).</summary>
    private static Strecke StarkWirkt()
    {
        var s = new Strecke(standardFaktor: 1f, starkFaktor: 1f);
        s.Wahl.Ziel = Rauschfilter.Stark;
        for (var k = 0; k < Rauschfilterwahl.AnlaufRahmen + Rauschfilterwahl.BlendeRahmen + 2; k++) s.Takt(pause: true);
        Pruefe.Gleich(Rauschfilter.Stark, s.Wahl.Aktiv, "Vorbereitung: Stark wirkt");
        return s;
    }
}
