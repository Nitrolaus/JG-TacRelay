// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Nacharbeit der Klangstufe C2a nach der Entscheidung der Koordination vom 04.10.2026 (im Sinn Sarevocs: "Hit muss raus, im Zweifel 80 ms, nie
/// abschneiden"): Die Raum-Anhebung sperrt nur leises Niveau. Kommt ein Rahmen auf Funkniveau, öffnet das Gate sofort mit dem ganzen Vorpuffer,
/// und für diese Phase gilt die Anhebung nicht mehr (weder Linie noch Freigabe). Die Angleichung (Q29) nur noch abwärts prüft
/// <c>KlangC2EmpfangTests</c>.
/// </summary>
internal static class KlangC2NacharbeitTests
{
    private const int R = 480;

    private static float[] Ton(int rahmen, float dbfs)
    {
        var x = new float[rahmen * R];
        var a = MathF.Sqrt(2f) * MathF.Pow(10f, dbfs / 20f);
        for (var i = 0; i < x.Length; i++) x[i] = a * MathF.Sin(2f * MathF.PI * 200f * i / 48000f);
        return x;
    }

    /// <summary>Ein Gate mit 120 ms Vorlauf, strenger Freigabe (wie die Raum-Anhebung: -25 dBFS) und Funkniveau ab -29 dBFS, nach 1,5 s Stille.</summary>
    private static (SprachGate Gate, Func<float[], float, List<long>> Lauf) RaumGate()
    {
        var g = new SprachGate(new GateEinstellungen { VorlaufMs = 120 }) { MindestDbfs = -25f - 4f, FunkniveauDbfs = -29f };
        var puffer = new float[R];
        List<long> Lauf(float[] signal, float p)
        {
            var gesendet = new List<long>();
            for (var r = 0; r < signal.Length / R; r++)
            {
                g.Verarbeiten(signal.AsSpan(r * R, R), p, 0f, false);
                while (g.Holen(puffer, out _, out var nummer)) gesendet.Add(nummer);
            }
            return gesendet;
        }
        Lauf(new float[R * 150], 0f);
        return (g, Lauf);
    }

    [Test]
    public static void C2N_Gate_RahmenAufFunkniveau_OeffnetSofort_GanzerVorpuffer_LeisesBleibtZurueck()
    {
        var (g, lauf) = RaumGate();
        // Leise Rede (12 dB unter der Stimme): zurückgehalten, nichts hinaus.
        Pruefe.Gleich(0, lauf(Ton(80, -36f), 0.95f).Count, "leise: nichts");
        Pruefe.Wahr(g.Zustand is GateZustand.Zurueckgehalten or GateZustand.Anlauf, "zurückgehalten: " + g.Zustand);
        lauf(new float[R * 60], 0f);
        Pruefe.Gleich(GateZustand.Leise, g.Zustand, "wieder leise");

        // Ein kurzer Spruch nach leiser Rede: 60 ms leiser Anlaut (-40), dann 50 ms auf Funkniveau (-27). Ohne die Regel wartete die Freigabe auf
        // 150 ms im Mittel ab -25 dBFS; mit ihr öffnet das Gate nach 50 ms auf Funkniveau, und der Vorpuffer reicht vor den Anlaut.
        var anlaut = g.Rahmennummer;
        var gesendet = lauf([.. Ton(6, -40f), .. Ton(5, -27f)], 0.95f);
        Pruefe.Wahr(gesendet.Count > 0, "50 ms auf Funkniveau öffnen sofort");
        PruefeWert.Hoechstens(anlaut, gesendet.Min(), "der Vorpuffer reicht bis vor den Anlaut");
        Pruefe.Gleich(GateZustand.Senden, g.Zustand, "offen");
        Pruefe.Wahr(g.FunkniveauPhase, "diese Phase gilt als Funkniveau");
        // Der Rest des Spruchs ist leiser (Silben unter der Freigabe): Die Phase bleibt offen, nichts wird abgeschnitten.
        Pruefe.Gleich(20, lauf(Ton(20, -38f), 0.95f).Count, "danach leiser: jeder Rahmen hinaus");
        lauf(new float[R * 60], 0f);
        Pruefe.Gleich(GateZustand.Leise, g.Zustand, "Phase zu");
        Pruefe.Falsch(g.FunkniveauPhase, "nach der Phase wieder ohne");
        lauf(new float[R * 100], 0f);

        // Ohne Funkniveau (Rahmen bleiben darunter) gilt die Freigabe wie bisher; ebenso einzelne laute Rahmen (betonte Silben leiser Rede).
        Pruefe.Gleich(0, lauf(Ton(40, -33f), 0.95f).Count, "knapp unter Funkniveau: zurückgehalten");
        lauf(new float[R * 100], 0f);
        Pruefe.Gleich(0, lauf([.. Ton(10, -38f), .. Ton(1, -26f), .. Ton(10, -38f), .. Ton(1, -26f), .. Ton(10, -38f)], 0.95f).Count,
            "einzelne Rahmen auf Funkniveau: zurückgehalten");
        lauf(new float[R * 60], 0f);
        // Keine Sprache (Wahrscheinlichkeit niedrig): auch laut kein sofortiges Öffnen.
        Pruefe.Gleich(0, lauf(Ton(3, -20f), 0.05f).Count, "laut, aber keine Sprache: nichts");
    }

    [Test]
    public static void C2N_Fuehrung_RaumAnhebung_GibtFunkniveau_UndGiltNichtInDerPhaseAufFunkniveau()
    {
        var f = new GateFuehrung { Einstellung = GateVorgaben.Standard, StimmeDb = -23f, BezugDb = -23f, ZusatzWunschDb = -26f };
        var still = new float[R];
        for (var i = 0; i < 150; i++)
        {
            _ = f.Vorbereiten(-80f, 0f, false);
            f.Gate.Verarbeiten(still, 0f);
        }
        Pruefe.Gleich(-29f, f.LinieDb, "Linie an der Obergrenze (Stimme minus 6)");
        PruefeWert.Nahe(-26.0, f.Gate.FunkniveauDbfs, 1e-3, "Funkniveau während der Anhebung: ihr Wunsch (10 dB über dem leisen Niveau)");
        var mindestMitAnhebung = f.Gate.MindestDbfs;

        // Ein Spruch auf Funkniveau: öffnet; in dieser Phase gelten Linie und Freigabe ohne die Anhebung.
        var laut = Ton(5, -22f);
        for (var r = 0; r < 5; r++)
        {
            var p = f.Vorbereiten(-22f, 0.95f, false);
            f.Gate.Verarbeiten(laut.AsSpan(r * R, R), p);
        }
        Pruefe.Wahr(f.Offen && f.Gate.FunkniveauPhase, "offen auf Funkniveau");
        _ = f.Vorbereiten(-38f, 0.95f, false);
        PruefeWert.Hoechstens(-40.0, f.LinieDb, "in der Phase die Linie ohne Anhebung");
        PruefeWert.Hoechstens(mindestMitAnhebung - 1.0, f.Gate.MindestDbfs, "und die Freigabe ohne Anhebung");

        // Ohne Anhebung gilt die Regel seit der Gegenprüfung C2a (H1) auch, ab Stimme minus 6 dB; ohne bekannte Stimme nicht.
        var ohne = new GateFuehrung { Einstellung = GateVorgaben.Standard, StimmeDb = -23f, BezugDb = -23f };
        _ = ohne.Vorbereiten(-80f, 0f, false);
        PruefeWert.Nahe(-29.0, ohne.Gate.FunkniveauDbfs, 1e-3, "ohne Anhebung: Stimme minus 6 dB");
        var unbekannt = new GateFuehrung { Einstellung = GateVorgaben.Standard };
        _ = unbekannt.Vorbereiten(-80f, 0f, false);
        Pruefe.Wahr(float.IsPositiveInfinity(unbekannt.Gate.FunkniveauDbfs), "ohne bekannte Stimme: keine Regel");
    }
}
