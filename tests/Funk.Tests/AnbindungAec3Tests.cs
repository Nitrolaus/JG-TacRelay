// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Klang.Kern;
using Funk.Tests.Maschine;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests;

/// <summary>
/// AEC3-Konfiguration der C-Schicht 1.1 (apm_aec3_configure, apm_aec3_describe) gegen die echte webrtc_apm.dll: Voreinstellung,
/// Alles-oder-nichts, wirksame Konfiguration des laufenden EchoCanceller3, messbare Wirkung, Umbau unter Last.
/// </summary>
internal static class AnbindungAec3Tests
{
    private const int R = 480;

    private const string Schneller = "suppressor.normal_tuning.max_inc_factor";
    private const string SchnellerNah = "suppressor.nearend_tuning.max_inc_factor";

    [Test]
    public static void Aec3_Voreinstellung_TacRelay_WeichtNurImAnstiegAb()
    {
        using var apm = new EchoProzessor();
        using var upstream = new EchoProzessor();
        upstream.Aec3Einstellen(EchoProzessor.Aec3Webrtc);
        var eigen = apm.Aec3Lesen()!;
        var webrtc = upstream.Aec3Lesen()!;
        Pruefe.Wahr(eigen.Count >= 80, $"alle Felder beschrieben ({eigen.Count})");
        Pruefe.Gleich(4.0, eigen[Schneller], "Anstieg normal");
        Pruefe.Gleich(8.0, eigen[SchnellerNah], "Anstieg Nahsprecher");
        Pruefe.Gleich(2.0, webrtc[Schneller], "Upstream normal (echo_canceller3_config.h)");
        Pruefe.Gleich(2.0, webrtc[SchnellerNah], "Upstream Nahsprecher");
        var abweichend = eigen.Keys.Where(k => eigen[k] != webrtc[k]).Order(StringComparer.Ordinal).ToList();
        Pruefe.Gleich($"{SchnellerNah},{Schneller}", string.Join(",", abweichend),
            "nur die beiden Anstiegsfaktoren weichen von Upstream ab");
        Pruefe.Wahr(EchoProzessor.Version.Contains("1.1.0", StringComparison.Ordinal) && EchoProzessor.Version.Contains("Transparentmodus HMM", StringComparison.Ordinal),
            $"Version: {EchoProzessor.Version}");
        Pruefe.Wahr(apm.Aec3Lesen(wirksam: true) is null, "nach dem Anlegen ist der AEC aus, also kein laufender EchoCanceller3");
    }

    [Test]
    public static void Aec3_Einstellen_AllesOderNichts()
    {
        using var apm = new EchoProzessor();
        var vorher = apm.Aec3Text();
        foreach (var falsch in new[]
                 {
                     "gibt.es.nicht=1",
                     "ep_strength.default_gain=0.5;ep_strength.default_gain=abc",
                     "ep_strength.default_gain=NaN",
                     "filter.conservative_initial_phase=2",
                     "delay.default_delay=1.5",
                     "delay.down_sampling_factor=5",
                     "suppressor.normal_tuning.mask_lf.enr_transparent=0.5",
                     "voreinstellung=irgendwas",
                 })
        {
            _ = Pruefe.Wirft<ArgumentException>(() => apm.Aec3Einstellen(falsch), falsch);
            Pruefe.Gleich(vorher, apm.Aec3Text(), $"nach Ablehnung von {falsch} unverändert");
        }
        apm.Aec3Einstellen(" ep_strength.default_gain = 0.5 ; filter.initial_state_seconds=1.25;");
        var neu = apm.Aec3Lesen()!;
        Pruefe.Gleich(0.5, neu["ep_strength.default_gain"], "Wert gesetzt");
        Pruefe.Gleich(1.25, neu["filter.initial_state_seconds"], "zweiter Wert gesetzt");
        Pruefe.Gleich(4.0, neu[Schneller], "übrige Werte bleiben (Ausgangspunkt ist die aktuelle Konfiguration)");
        apm.Aec3Einstellen(EchoProzessor.Aec3TacRelay);
        Pruefe.Gleich(vorher, apm.Aec3Text(), "voreinstellung=tacrelay setzt zurück");
    }

    [Test]
    public static void Aec3_Wirksam_BeimEinschaltenUndBeiLaufendemAec()
    {
        using var apm = new EchoProzessor();
        apm.Aec3Einstellen("ep_strength.default_gain=0.75");
        Pruefe.Wahr(apm.Aec3Text(wirksam: true) is null, "AEC aus: nichts wirksam");
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk);
        Pruefe.Gleich(apm.Aec3Text(), apm.Aec3Text(wirksam: true), "der laufende EchoCanceller3 bekam genau die eingestellte Konfiguration");
        Pruefe.Gleich(0.75, apm.Aec3Lesen(wirksam: true)!["ep_strength.default_gain"], "Wert im laufenden AEC3");
        apm.Aec3Einstellen("suppressor.dominant_nearend_detection.trigger_threshold=6");
        Pruefe.Gleich(6.0, apm.Aec3Lesen(wirksam: true)!["suppressor.dominant_nearend_detection.trigger_threshold"],
            "Änderung bei laufendem AEC wird sofort wirksam (neu aufgebaut)");
        Pruefe.Falsch(apm.Statistik().Gueltig, "nach dem Umbau beginnt die Anlaufphase der Kennwerte neu");
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk with { Echounterdrueckung = false });
        Pruefe.Wahr(apm.Aec3Text(wirksam: true) is null, "AEC wieder aus");
        Pruefe.Gleich(6.0, apm.Aec3Lesen()!["suppressor.dominant_nearend_detection.trigger_threshold"], "eingestellt bleibt für das nächste Einschalten");
    }

    [Test]
    public static void Aec3_Transparentmodus_HeadsetNachDreiSekundenFrei()
    {
        // Headset: Spielton nur in der Referenz, kein Echo. Mit dem Legacy-Klassifikator war die eigene Stimme hier bis etwa 7 s
        // um 44 bis 52 dB gedämpft; mit dem HMM (Feldversuch in der DLL) ist ab 2,5 s jede Äußerung frei.
        var werte = MessungSpielton.Aec3Daempfung(null, 0f);
        Console.WriteLine("  AEC3 Headset mit Spielton, Mitte je Äußerung in dB: " + string.Join(" ", werte.Select(w => $"{w.StartS:F1}s {w.Mitte:F0}")));
        foreach (var w in werte.Where(w => w.StartS >= 2.5))
            PruefeWert.Mindestens(-6, w.Mitte, $"Äußerung bei {w.StartS:F1} s nicht unterdrückt");
    }

    [Test]
    public static void Aec3_Anstieg_WirktAufAnlaute()
    {
        // Messbare Wirkung der Voreinstellung: Lautsprecher 12 dB mit Spielton, eigene Stimme. Der schnellere Anstieg der
        // Unterdrückungsverstärkung dämpft die ersten 100 ms jeder Äußerung weniger als die Upstream-Vorgabe.
        var upstream = MessungSpielton.Aec3Daempfung(EchoProzessor.Aec3Webrtc, 0.25f).Where(w => w.StartS >= 7).Average(w => w.Anlaut);
        var eigen = MessungSpielton.Aec3Daempfung(null, 0.25f).Where(w => w.StartS >= 7).Average(w => w.Anlaut);
        Console.WriteLine($"  AEC3 Lautsprecher 12 dB, Anlaut im Mittel: Upstream {upstream:F2} dB, Voreinstellung {eigen:F2} dB");
        PruefeWert.Mindestens(upstream + 0.3, eigen, "Voreinstellung dämpft den Anlaut weniger");
    }

    [Test]
    public static void Aec3_UmbauWaehrendDerVerarbeitung_OhneFehler()
    {
        // Ein Thread verarbeitet Wiedergabe und Aufnahme im Takt, ein zweiter schaltet den AEC um und ändert die
        // AEC3-Konfiguration. Die C-Schicht baut dabei das APM neu auf; laufende Aufrufe warten, keiner scheitert.
        using var apm = new EchoProzessor();
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk);
        var fern = AnbindungSignale.Sprache(R * 400, 51, -20);
        var nah = AnbindungSignale.Rauschen(R * 400, -50, 52);
        using var stopp = new CancellationTokenSource();
        long rahmen = 0, fehler = 0, nichtEndlich = 0;
        var arbeiter = Task.Run(() =>
        {
            var auf = new float[R];
            var f = 0;
            while (!stopp.IsCancellationRequested)
            {
                var i = f++ % 400;
                if (apm.VerarbeiteWiedergabe(fern.AsSpan(i * R, R)) != 0) fehler++;
                nah.AsSpan(i * R, R).CopyTo(auf);
                if (apm.VerarbeiteAufnahme(auf) != 0) fehler++;
                foreach (var x in auf)
                {
                    if (!float.IsFinite(x)) nichtEndlich++;
                }
                rahmen++;
            }
        });
        var dauer = new List<double>();
        for (var k = 0; k < 30; k++)
        {
            var uhr = Stopwatch.StartNew();
            apm.Konfigurieren(ApmEinstellungen.Sprechfunk with { Echounterdrueckung = k % 2 == 1 });
            dauer.Add(uhr.Elapsed.TotalMilliseconds);
            apm.Aec3Einstellen(k % 3 == 0 ? "filter.initial_state_seconds=1" : EchoProzessor.Aec3TacRelay);
            Thread.Sleep(3);
        }
        stopp.Cancel();
        arbeiter.GetAwaiter().GetResult();
        dauer.Sort();
        Console.WriteLine($"  AEC3 Umbau: {rahmen} Rahmen nebenher, Umschalten des AEC Median {dauer[dauer.Count / 2]:F2} ms, höchstens {dauer[^1]:F2} ms");
        Pruefe.Gleich(0L, fehler, "keine APM-Fehlercodes");
        Pruefe.Gleich(0L, nichtEndlich, "keine nicht endlichen Werte");
        PruefeWert.Mindestens(30, rahmen, "der Arbeiter lief");
        // Ohne Vorrang für den Umbau in der C-Schicht wartete er hier bis zu 1,4 s auf die geteilte Sperre.
        PruefeWert.Hoechstens(100, dauer[^1], "Umschalten wartet nicht auf einen Strom von Verarbeitungsaufrufen");
    }
}
