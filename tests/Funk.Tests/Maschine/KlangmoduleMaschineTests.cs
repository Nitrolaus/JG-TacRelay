// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Klangmodule (0.9.0, Sarevoc 06.10.2026, Q48 bis Q50) in der Klangmaschine: Mindest-Funklautstärke und Pegelausgleich einzeln schaltbar, der
/// Pegelausgleich unabhängig von der Auto Dauersend-Dämpfung, alle drei Schalter von der Maschine bis an ihre Stelle durchgereicht.
/// </summary>
internal static class KlangmoduleMaschineTests
{
    private const int R = 480;

    [Test]
    public static void Klangmodule_MindestFunklautstaerke_Aus_KeineFreigabeschwelle_ObergrenzeBleibt()
    {
        var f = new GateFuehrung { Einstellung = GateVorgaben.Standard, StimmeDb = -20f };
        Pruefe.Wahr(f.MindestAn, "ab Werk an");
        f.Vorbereiten(-60f, 0f, false);
        var an = f.Korridor;
        Pruefe.Wahr(float.IsFinite(an.UntergrenzeDb) && float.IsFinite(f.Gate.MindestDbfs), "an: Mindest-Funklautstärke aus der Stimme");
        Pruefe.Wahr(float.IsFinite(an.ObergrenzeDb), "an: Obergrenze");

        f.MindestAn = false;
        f.Vorbereiten(-60f, 0f, false);
        Pruefe.Wahr(float.IsNaN(f.Korridor.UntergrenzeDb), "aus: keine Untergrenze");
        Pruefe.Gleich(float.NegativeInfinity, f.Gate.MindestDbfs, "aus: keine Freigabeschwelle");
        Pruefe.Gleich(an.ObergrenzeDb, f.Korridor.ObergrenzeDb, "aus: die Obergrenze bleibt");

        f.MindestAn = true;
        f.Vorbereiten(-60f, 0f, false);
        Pruefe.Gleich(an.UntergrenzeDb, f.Korridor.UntergrenzeDb, "wieder an: wie vorher");

        // Ohne gelernte Stimme gibt es ohnehin keine Mindest-Funklautstärke; der Schalter ändert dort nichts.
        var g = new GateFuehrung { Einstellung = GateVorgaben.Standard, MindestAn = false };
        g.Vorbereiten(-60f, 0f, false);
        Pruefe.Wahr(float.IsNaN(g.Korridor.UntergrenzeDb), "ohne Stimme: keine Untergrenze, wie bisher");
    }

    [Test]
    public static void Klangmodule_Pegelausgleich_UnabhaengigVonDerDaempfung_UndAbschaltbar()
    {
        // Bis 0.8.x hing die Angleichung (Q29) am Schalter der Auto Dauersend-Dämpfung; seit 0.9.0 ist sie ein eigenes Modul.
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null, Daempfung = new DaempfungEinstellung(false, Daempfungsstufe.DeutlichLeiser) };
        Pruefe.Wahr(m.Waechter.AngleichungAn, "ab Werk an");
        var laut = KlangC2EmpfangTests.Korpusrede(150, out var phasen, saat: 8, pausenMs: 1500, pegelDb: 16f);
        var p = m.QuelleHinzufuegen(new Funkquelle(laut, phasen), 0);
        var block = new float[R];
        for (var b = 0; b < 120 * 100; b++) m.Mischen(block, 1);
        PruefeWert.Hoechstens(-5.0, m.Zustand(p).AngleichungDb, "Dämpfung aus, Pegelausgleich an: der zu laute Funker ist angeglichen");

        m.Waechter.AngleichungAn = false;
        for (var b = 0; b < 10 * 100; b++) m.Mischen(block, 1);
        Pruefe.Gleich(0f, m.Zustand(p).AngleichungDb, "Pegelausgleich aus: gerampt zurück auf 0");
    }

    [Test]
    public static async Task Klangmodule_Maschine_ReichtDieSchalterDurch()
    {
        var maschine = new KlangMaschine(new KlangMaschinenOptionen { AudioSystem = new AttrappenSystem() });
        try
        {
            Pruefe.Wahr(maschine.Echoschutz && maschine.MindestFunklautstaerke && maschine.Pegelausgleich, "ab Werk alle an");
            maschine.Echoschutz = false;
            maschine.MindestFunklautstaerke = false;
            maschine.Pegelausgleich = false;
            Pruefe.Falsch(maschine.Echoschutz, "Echoschutz aus");
            Pruefe.Falsch(maschine.MindestFunklautstaerke, "Mindest-Funklautstärke aus");
            Pruefe.Falsch(maschine.Mischer.Waechter.AngleichungAn, "Pegelausgleich aus bis in den Wächter der Empfängerseite");
            maschine.Echoschutz = true;
            maschine.MindestFunklautstaerke = true;
            maschine.Pegelausgleich = true;
            Pruefe.Wahr(maschine.Echoschutz && maschine.MindestFunklautstaerke && maschine.Mischer.Waechter.AngleichungAn, "wieder an");
        }
        finally
        {
            await maschine.DisposeAsync();
        }
    }

    /// <summary>Funk-Lautstärke (seit 0.10.0, Q74, E7): Die Maschine reicht den Faktor an ihren Mischer, begrenzt; keine Zahl gilt als 1.</summary>
    [Test]
    public static async Task FunkLautstaerke_Maschine_ReichtAnDenMischer()
    {
        var maschine = new KlangMaschine(new KlangMaschinenOptionen { AudioSystem = new AttrappenSystem() });
        try
        {
            Pruefe.Gleich(1f, maschine.FunkLautstaerke, "ab Werk 1, so laut wie bisher");
            maschine.FunkLautstaerke = 0.25f;
            Pruefe.Gleich(0.25f, maschine.Mischer.FunkLautstaerke, "an den Mischer");
            Pruefe.Gleich(0.25f, maschine.FunkLautstaerke, "und zurück gelesen");
            maschine.FunkLautstaerke = float.NaN;
            Pruefe.Gleich(1f, maschine.Mischer.FunkLautstaerke, "keine Zahl: 1, nie stumm durch einen Fehler");
            maschine.FunkLautstaerke = 2f;
            Pruefe.Gleich(1f, maschine.Mischer.FunkLautstaerke, "begrenzt");
        }
        finally
        {
            await maschine.DisposeAsync();
        }
    }
}
