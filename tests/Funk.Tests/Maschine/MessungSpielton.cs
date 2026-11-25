// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Messungen zum Spielton (Star Citizen) in der Echoreferenz, nur von Hand: <c>FUNK_MESSUNG=1</c>. Sie prüfen nichts, sie
/// geben Zahlen für die Wahl der AEC3-Abstimmung und für den Hörtest aus (docs\MODULBERICHT-aec3.md). Befund der
/// Gegenprüfung vor dem Modul aec3: AEC3 in der Upstream-Vorgabe dämpft bei laufender Referenz die ersten Rahmen jeder
/// eigenen Äußerung, auch ganz ohne Echo im Mikrofon (Headset); RNNoise erkennt den Anlaut dann seltener als Sprache, das
/// Gate öffnet später. In den ersten etwa 7 s nach dem Einschalten ist die eigene Stimme bei laufender Wiedergabe fast
/// ganz unterdrückt.
/// <para>
/// <see cref="Messung_Kandidaten"/> misst jede Konfiguration aus <see cref="Kandidaten"/> (dazu eine eigene aus
/// <c>FUNK_AEC3</c>, Auswahl über <c>FUNK_AEC3_NUR</c>) in allen Szenen, parallel über die Kandidaten; mit
/// <c>FUNK_HEADSET_SCHUTZ=alt</c> laufen die Headset-Szenen mit dem Echoschutz von vor dem Modul (Grundlinie).
/// Zur Ursachensuche: <see cref="Messung_Diagnose"/> (je Äußerung, <c>FUNK_DIAGNOSE</c>, <c>FUNK_SEED</c>),
/// <see cref="Messung_KetteOhneGate"/> (APM und RNNoise, <c>FUNK_RAHMEN</c> für den Rahmenverlauf),
/// <see cref="Messung_Aec3_Zeitverlauf"/>, <see cref="Messung_RestechoLaufzeit"/>, <see cref="Messung_RestechoVergleich"/>,
/// <see cref="Messung_WiedergabeSprachartig"/> und <see cref="Messung_Fingerabdruck"/> (Vergleich zweier DLLs). Aus der
/// Gegenprüfung: <see cref="Messung_SpieltonEcho"/> (Echo von Spielton und Musik ohne Funk, je Abstand des Echoschutzes
/// bei Geräusch) und <see cref="Messung_FunkImSpielton"/> (Funk neben Spielton oder Musik). Alle Szenen sind
/// deterministisch (feste Seeds) und selbst synthetisiert.
/// </para>
/// </summary>
internal static class MessungSpielton
{
    /// <summary>
    /// Die gemessenen Konfigurationen, jeweils ab der Upstream-Vorgabe (<see cref="EchoProzessor.Aec3Webrtc"/>), damit die
    /// Zahlen nicht von der Voreinstellung der DLL abhängen. "DLL" ist die Voreinstellung, mit der das Produkt läuft.
    /// </summary>
    internal static readonly (string Name, string? Spec)[] Kandidaten =
    [
        ("webrtc", EchoProzessor.Aec3Webrtc),
        ("DLL", null),
        ("A dn-empfindlich", EchoProzessor.Aec3Webrtc + ";suppressor.dominant_nearend_detection.enr_threshold=0.75;suppressor.dominant_nearend_detection.trigger_threshold=4"),
        ("B anstieg", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8"),
        ("C erle", EchoProzessor.Aec3Webrtc + ";erle.max_l=16;erle.max_h=8"),
        ("D gain0.1", EchoProzessor.Aec3Webrtc + ";ep_strength.default_gain=0.1"),
        ("E stationaer", EchoProzessor.Aec3Webrtc + ";echo_audibility.use_stationarity_properties=1;echo_audibility.use_stationarity_properties_at_init=1"),
        ("F transparenter", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.mask_lf.enr_transparent=0.4;suppressor.normal_tuning.mask_lf.enr_suppress=0.5;"
            + "suppressor.normal_tuning.mask_hf.enr_transparent=0.3;suppressor.normal_tuning.mask_hf.enr_suppress=0.4;"
            + "suppressor.nearend_tuning.mask_lf.enr_transparent=1.29;suppressor.nearend_tuning.mask_lf.enr_suppress=1.3;"
            + "suppressor.nearend_tuning.mask_hf.enr_transparent=1.09;suppressor.nearend_tuning.mask_hf.enr_suppress=1.1"),
        ("G anfang0.5", EchoProzessor.Aec3Webrtc + ";filter.initial_state_seconds=0.5"),
        ("B2 anstieg3/6", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=3;suppressor.nearend_tuning.max_inc_factor=6"),
        ("B3 anstieg6/12", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=6;suppressor.nearend_tuning.max_inc_factor=12"),
        ("BA", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;suppressor.dominant_nearend_detection.enr_threshold=0.75;suppressor.dominant_nearend_detection.trigger_threshold=4"),
        ("BC", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;erle.max_l=16;erle.max_h=8"),
        ("J mittel1", EchoProzessor.Aec3Webrtc + ";suppressor.nearend_average_blocks=1"),
        ("BJ", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;suppressor.nearend_average_blocks=1"),
        ("L nachhall-aus", EchoProzessor.Aec3Webrtc + ";echo_model.model_reverb_in_nonlinear_mode=0"),
        ("I boden0.001", EchoProzessor.Aec3Webrtc + ";suppressor.floor_first_increase=0.001"),
        ("D05 gain0.5", EchoProzessor.Aec3Webrtc + ";ep_strength.default_gain=0.5"),
        ("BD05", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;ep_strength.default_gain=0.5"),
        ("BCJ", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;erle.max_l=16;erle.max_h=8;suppressor.nearend_average_blocks=1"),
        ("R3 emin2", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;erle.min=2;erle.max_h=2"),
        ("R3 emin2-c", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;erle.min=2;erle.max_l=16;erle.max_h=8"),
        ("R3 t4", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;suppressor.dominant_nearend_detection.trigger_threshold=4"),
        ("R3 t2snr10", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;suppressor.dominant_nearend_detection.trigger_threshold=2;suppressor.dominant_nearend_detection.snr_threshold=10"),
        ("R3 enr075", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;suppressor.dominant_nearend_detection.enr_threshold=0.75"),
        ("R3 hf", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;suppressor.normal_tuning.mask_hf.enr_transparent=0.3;suppressor.normal_tuning.mask_hf.enr_suppress=0.4"),
        ("R3 lfhf", EchoProzessor.Aec3Webrtc + ";suppressor.normal_tuning.max_inc_factor=4;suppressor.nearend_tuning.max_inc_factor=8;suppressor.normal_tuning.mask_lf.enr_transparent=0.4;suppressor.normal_tuning.mask_lf.enr_suppress=0.5;suppressor.normal_tuning.mask_hf.enr_transparent=0.3;suppressor.normal_tuning.mask_hf.enr_suppress=0.4"),
    ];

    private static readonly int[] Seeds = [151, 171, 181];
    private static readonly int[] Dreiklang = [0, 4, 7];

    /// <summary>Kopplung Lautsprecher zu Mikrofon: 12, 20 und 30 dB.</summary>
    private static readonly float[] Kopplungen = [0.25f, 0.1f, 0.0316f];

    /// <summary>Echolaufzeiten in Samples (48 kHz) für die Messung des Restechos: 30 bis 50 ms, dicht um 40 ms (10 Blöcke).</summary>
    private static readonly int[] Laufzeiten = [1440, 1632, 1776, 1872, 1900, 1920, 1940, 1968, 2064, 2400];

    private static bool Aus()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") == "1") return false;
        Console.WriteLine("HINWEIS Messung Spielton übersprungen (FUNK_MESSUNG=1 setzen).");
        return true;
    }

    /// <summary>Verpasste Anlaute, Anzahl Anlaute, Phasen ohne eigene Äußerung.</summary>
    internal readonly record struct Anlaute(int Verpasst, int Gesamt, int Fehl)
    {
        public static Anlaute operator +(Anlaute a, Anlaute b) => new(a.Verpasst + b.Verpasst, a.Gesamt + b.Gesamt, a.Fehl + b.Fehl);

        public override string ToString() => Fehl == 0 ? $"{Verpasst}/{Gesamt}" : $"{Verpasst}/{Gesamt} F{Fehl}";
    }

    /// <summary>Stellung des Echoschutzes in den Lautsprecher-Szenen: aus, jede Wiedergabe zählt (vor aec3), nur sprachartige.</summary>
    internal enum Schutz
    {
        Aus,
        Alt,
        Neu,
    }

    /// <summary>Kennzahlen einer Konfiguration über alle Szenen.</summary>
    internal sealed record Kennzahlen(
        string Name,
        Anlaute HeadsetOhne,
        Anlaute HeadsetSpiel,
        Anlaute HeadsetLaut,
        Anlaute HeadsetLeise,
        Anlaute HeadsetMusik,
        Anlaute[] LautsprecherNeu,
        Anlaute[] LautsprecherAlt,
        Anlaute[] LautsprecherAus,
        Anlaute LautsprecherMusik,
        (float Anfang, float Anlaut, float Mitte) Headset,
        (float Anfang, float Anlaut, float Mitte) Lautsprecher12,
        (float Db3, float Db12, float Verzerrt, float Spiel12) Restecho,
        (int Db3, int Db12, int Verzerrt, int VerzerrtOhneSchutz) EchoFehl,
        (int Frei, int Gesendet) Doppelsprechen,
        (int Neu12, int Aus12, int NeuVerzerrt, int AltVerzerrt, int AusVerzerrt) Spielstimme);

    private static List<(long Beginn, long Ende)> Phasen(List<Gesendet> pakete)
    {
        var phasen = new List<(long, long)>();
        long beginn = -1;
        foreach (var p in pakete)
        {
            if ((p.Kennzeichen & Sprachkennzeichen.Beginn) != 0)
            {
                var vorlauf = pakete.Count(q => q.Takt == p.Takt && (q.Kennzeichen & Sprachkennzeichen.Preroll) != 0);
                beginn = p.Takt - 2 * vorlauf;
            }
            if ((p.Kennzeichen & Sprachkennzeichen.Ende) != 0 && beginn >= 0)
            {
                phasen.Add((beginn, p.Takt));
                beginn = -1;
            }
        }
        return phasen;
    }

    /// <summary>Verpasst heißt wie in <see cref="KlangMaschineTests"/>: keine Phase, die spätestens 100 ms nach dem Anlaut beginnt.</summary>
    internal static Anlaute Auswerten(List<Gesendet> pakete, List<Aeusserung> marken, long abTakt)
    {
        var phasen = Phasen(pakete);
        var fehl = phasen.Count(ph => ph.Beginn >= abTakt && !marken.Any(a => ph.Beginn * 480 < a.Ende + 24000 && (ph.Ende + 1) * 480 > a.Start));
        var eigene = marken.Where(a => a.Start > abTakt * 480).ToList();
        var verpasst = eigene.Count(a => !phasen.Any(ph => ph.Beginn * 480 <= a.Start + 4800 && (ph.Ende + 1) * 480 >= a.Start));
        return new Anlaute(verpasst, eigene.Count, fehl);
    }

    private static void SpieltonZuspielen(Pruefstand s, float[] spiel) => s.Fremdton = (takt, w) =>
    {
        var basis = (int)(takt - 1) * 480;
        for (var i = 0; i < 480 && basis + i < spiel.Length; i++)
        {
            w[2 * i] += spiel[basis + i];
            w[2 * i + 1] += spiel[basis + i];
        }
    };

    /// <summary>Headset: Spielton (oder null) nur im Loopback, kein Echo; der Nutzer spricht 60 s.</summary>
    internal static async Task<Anlaute> Headset(string? spec, int seed, Func<int, int, float[]>? spiel, bool aec = true, Schutz schutz = Schutz.Neu,
        float? geraeuschAbstand = null)
    {
        var (sprache, marken) = Testszenen.Sprache(60, seed);
        await using var s = new Pruefstand(aec3: spec);
        s.Maschine.Echounterdrueckung = aec;
        await s.StartenAsync();
        s.Maschine.Kette.EchoschutzAn = schutz != Schutz.Aus;
        s.Maschine.Kette.EchoschutzNurSprache = schutz == Schutz.Neu;
        if (geraeuschAbstand is { } abstand) s.Maschine.Kette.Echoschutz.GeraeuschAbstandDb = abstand;
        if (spiel is not null) SpieltonZuspielen(s, spiel(sprache.Length, seed + 1));
        s.Abspielen(sprache);
        return Auswerten(s.Pakete, marken, 300);
    }

    internal static async Task<Anlaute> HeadsetDreiSeeds(string? spec, Func<int, int, float[]>? spiel, bool aec = true, Schutz schutz = Schutz.Neu,
        float? geraeuschAbstand = null)
    {
        var teile = await Task.WhenAll(Seeds.Select(seed => Task.Run(() => Headset(spec, seed, spiel, aec, schutz, geraeuschAbstand))));
        return teile.Aggregate((a, b) => a + b);
    }

    /// <summary>Lautsprecher: Spielton im Loopback und, um 40 ms verzögert und um die Kopplung gedämpft, im Mikrofon.</summary>
    internal static async Task<Anlaute> Lautsprecher(string? spec, int seed, float kopplung, Schutz schutz, bool musik,
        float? geraeuschAbstand = null)
    {
        var (sprache, marken) = Testszenen.Sprache(60, seed);
        var spiel = musik ? Musik(sprache.Length, -24f, seed + 2) : Spielton(sprache.Length, -24f, seed + 1);
        await using var s = new Pruefstand(aec3: spec);
        await s.StartenAsync();
        s.Maschine.Kette.EchoschutzAn = schutz != Schutz.Aus;
        s.Maschine.Kette.EchoschutzNurSprache = schutz == Schutz.Neu;
        if (geraeuschAbstand is { } abstand) s.Maschine.Kette.Echoschutz.GeraeuschAbstandDb = abstand;
        SpieltonZuspielen(s, spiel);
        EchoAnlegen(s, x => kopplung * x);
        s.Abspielen(sprache);
        return Auswerten(s.Pakete, marken, 500);
    }

    internal static async Task<Anlaute> LautsprecherDreiSeeds(string? spec, float kopplung, Schutz schutz, bool musik = false,
        float? geraeuschAbstand = null)
    {
        var teile = await Task.WhenAll(Seeds.Select(seed => Task.Run(() => Lautsprecher(spec, seed, kopplung, schutz, musik, geraeuschAbstand))));
        return teile.Aggregate((a, b) => a + b);
    }

    /// <summary>Echo der Wiedergabe (Mittel beider Kanäle) über <paramref name="pfad"/>, 40 ms später im Mikrofon.</summary>
    private static void EchoAnlegen(Pruefstand s, Func<float, float> pfad)
    {
        var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
        s.Echo = (w, mic) =>
        {
            for (var i = 0; i < mic.Length; i++)
            {
                linie.Enqueue(pfad(0.5f * (w[2 * i] + w[2 * i + 1])));
                mic[i] += linie.Dequeue();
            }
        };
    }

    /// <summary>
    /// Sicherheit bei sprachartigem Spielton: Spielton (-36 dBFS) mit eingestreuten Sprachausgaben (synthetische Sprache,
    /// 6 dB über der Testszene, also -26 bis -12 dBFS) über die Lautsprecher ins Mikrofon; der Nutzer schweigt
    /// (Grundrauschen -65 dBFS). Ergebnis: Phasen ab 5 s, also Auslösungen durch das Echo.
    /// </summary>
    internal static async Task<int> Spielstimme(string? spec, Func<float, float> pfad, Schutz schutz)
    {
        var stimme = Testszenen.Sprache(40, 92).Signal;
        var spiel = Spielton(stimme.Length, -36f, 91);
        for (var i = 0; i < spiel.Length; i++) spiel[i] += 2f * stimme[i];
        await using var s = new Pruefstand(aec3: spec);
        await s.StartenAsync();
        s.Maschine.Kette.EchoschutzAn = schutz != Schutz.Aus;
        s.Maschine.Kette.EchoschutzNurSprache = schutz == Schutz.Neu;
        SpieltonZuspielen(s, spiel);
        EchoAnlegen(s, pfad);
        s.Abspielen(AnbindungSignale.Rauschen(spiel.Length, -65, 93));
        return Phasen(s.Pakete).Count(ph => ph.Beginn >= 500);
    }

    /// <summary>
    /// AEC3 allein (mit Hochpass, ohne AGC2, damit nur der AEC wirkt): Wie stark dämpft er die eigene Stimme? Referenz ist
    /// derselbe Lauf ohne AEC (nur Hochpass). Mikrofon: Sprache, bei <paramref name="kopplung"/> &gt; 0 dazu das Echo des
    /// Spieltons (40 ms, linear). Ergebnis in dB (negativ = gedämpft): schlechteste Äußerungsmitte in den ersten 7 s, danach
    /// im Mittel die ersten 100 ms ab dem Anlaut und die Mitte (100 ms).
    /// </summary>
    internal static (float Anfang, float Anlaut, float Mitte) Aec3Allein(string? spec, float kopplung, bool zeilen = false, bool musik = false, bool agc = false)
    {
        var werte = Aec3Daempfung(spec, kopplung, musik, agc);
        if (zeilen) Console.WriteLine("    Anlaut/Mitte in dB: " + string.Join(" | ", werte.Select(w => $"{w.StartS:F1}s {w.Anlaut:+0;-0}/{w.Mitte:+0;-0}")));
        var danach = werte.Where(w => w.StartS >= 7).ToList();
        return (werte.Where(w => w.StartS < 7).Select(w => w.Mitte).Append(0f).Min(), danach.Average(w => w.Anlaut), danach.Average(w => w.Mitte));
    }

    /// <summary>
    /// Wie <see cref="Aec3Allein"/>, aber je Äußerung: Beginn in s, Dämpfung der ersten 100 ms ab Anlaut und der Mitte (100 ms)
    /// in dB gegenüber dem Lauf ohne AEC.
    /// </summary>
    internal static List<(double StartS, float Anlaut, float Mitte)> Aec3Daempfung(string? spec, float kopplung, bool musik = false, bool agc = false)
    {
        var (sprache, marken) = Testszenen.Sprache(60, 151);
        var spiel = musik ? Musik(sprache.Length, -24f, 153) : Spielton(sprache.Length, -24f, 152);
        var mikrofon = (float[])sprache.Clone();
        if (kopplung > 0)
        {
            for (var i = 1920; i < mikrofon.Length; i++) mikrofon[i] += kopplung * spiel[i - 1920];
        }
        var mit = ApmLauf(spec, true, spiel, mikrofon, agc);
        var ohne = ApmLauf(spec, false, spiel, sprache, agc);
        float Differenz(int start, int laenge) =>
            Pegel.Dbfs(mit.AsSpan(start, laenge)) - Pegel.Dbfs(ohne.AsSpan(start, laenge));
        return marken.Select(a =>
        {
            var m = (a.Start + a.Ende) / 2;
            return (a.Start / 48000.0, Differenz(a.Start, 4800), Differenz(m - 2400, 4800));
        }).ToList();
    }

    /// <summary>APM mit AEC3 (oder ohne) und Hochpass über ein ganzes Signal; Referenz im Gleichschritt.</summary>
    private static float[] ApmLauf(string? spec, bool aec, float[] referenz, float[] mikrofon, bool agc = false)
    {
        using var apm = new EchoProzessor();
        if (spec is not null) apm.Aec3Einstellen(spec);
        apm.Konfigurieren(agc ? ApmEinstellungen.Sprechfunk with { Echounterdrueckung = aec } : new ApmEinstellungen(aec, true, ApmRauschStufe.Aus, false, 0f));
        _ = apm.SetzeVerzoegerung(20);
        var aus = new float[mikrofon.Length];
        var r = new float[480];
        var x = new float[480];
        for (var t = 0; t + 480 <= mikrofon.Length; t += 480)
        {
            Array.Copy(referenz, t, r, 0, 480);
            _ = apm.VerarbeiteWiedergabe(r);
            Array.Copy(mikrofon, t, x, 0, 480);
            _ = apm.VerarbeiteAufnahme(x);
            Array.Copy(x, 0, aus, t, 480);
        }
        return aus;
    }

    /// <summary>
    /// Restecho von AEC3 allein (Hochpass, ohne AGC2) in dB: Ausgang minus Mikrofon über die Rahmen ab 5 s, in denen das
    /// Echo über -50 dBFS liegt. Fernsprache (oder Spielton) 40 ms verzögert über <paramref name="pfad"/>, Grundrauschen -65 dBFS.
    /// </summary>
    internal static float Restecho(string? spec, Func<float, float> pfad, bool spielton = false, int verzugSamples = 1920)
    {
        var fern = spielton ? Spielton(40 * 48000, -24f, 152) : Testszenen.Sprache(40, 61).Signal;
        var rauschen = AnbindungSignale.Rauschen(fern.Length, -65, 62);
        var echo = new float[fern.Length];
        for (var i = verzugSamples; i < fern.Length; i++) echo[i] = pfad(fern[i - verzugSamples]);
        var mikrofon = new float[fern.Length];
        for (var i = 0; i < fern.Length; i++) mikrofon[i] = echo[i] + rauschen[i];
        var aus = ApmLauf(spec, true, fern, mikrofon);
        double ein = 0, raus = 0;
        for (var t = 500 * 480; t + 480 <= fern.Length; t += 480)
        {
            if (Pegel.Dbfs(echo.AsSpan(t, 480)) < -50f) continue;
            ein += Pegel.Leistung(mikrofon.AsSpan(t, 480));
            raus += Pegel.Leistung(aus.AsSpan(t, 480));
        }
        return (float)(10 * Math.Log10(Math.Max(raus, 1e-20) / Math.Max(ein, 1e-20)));
    }

    /// <summary>Alle Szenen für eine Konfiguration; die Läufe innerhalb laufen parallel.</summary>
    internal static async Task<Kennzahlen> Messen(string name, string? spec)
    {
        Func<int, int, float[]> spiel24 = (n, seed) => Spielton(n, -24f, seed);
        Func<int, int, float[]> spiel18 = (n, seed) => Spielton(n, -18f, seed);
        Func<int, int, float[]> spiel36 = (n, seed) => Spielton(n, -36f, seed);
        Func<int, int, float[]> musik = (n, seed) => Musik(n, -24f, seed + 1);
        // Headset-Szenen mit neuem Echoschutz; FUNK_HEADSET_SCHUTZ=alt misst sie wie vor dem Modul aec3 (Grundlinie).
        var hs = Environment.GetEnvironmentVariable("FUNK_HEADSET_SCHUTZ") == "alt" ? Schutz.Alt : Schutz.Neu;
        var headsetOhne = HeadsetDreiSeeds(spec, null, schutz: hs);
        var headsetSpiel = HeadsetDreiSeeds(spec, spiel24, schutz: hs);
        var headsetLaut = HeadsetDreiSeeds(spec, spiel18, schutz: hs);
        var headsetLeise = HeadsetDreiSeeds(spec, spiel36, schutz: hs);
        var headsetMusik = HeadsetDreiSeeds(spec, musik, schutz: hs);
        var lautNeu = Kopplungen.Select(k => LautsprecherDreiSeeds(spec, k, Schutz.Neu)).ToArray();
        var lautAlt = Kopplungen.Select(k => LautsprecherDreiSeeds(spec, k, Schutz.Alt)).ToArray();
        var lautAus = Kopplungen.Select(k => LautsprecherDreiSeeds(spec, k, Schutz.Aus)).ToArray();
        var lautMusik = LautsprecherDreiSeeds(spec, 0.25f, Schutz.Neu, musik: true);
        var stimmeNeu12 = Task.Run(() => Spielstimme(spec, KlangMaschineTests.Kopplung12Db, Schutz.Neu));
        var stimmeAus12 = Task.Run(() => Spielstimme(spec, KlangMaschineTests.Kopplung12Db, Schutz.Aus));
        var stimmeNeuVerzerrt = Task.Run(() => Spielstimme(spec, KlangMaschineTests.KopplungVerzerrt, Schutz.Neu));
        var stimmeAltVerzerrt = Task.Run(() => Spielstimme(spec, KlangMaschineTests.KopplungVerzerrt, Schutz.Alt));
        var stimmeAusVerzerrt = Task.Run(() => Spielstimme(spec, KlangMaschineTests.KopplungVerzerrt, Schutz.Aus));
        var headsetAllein = Task.Run(() => Aec3Allein(spec, 0f));
        var lautAllein = Task.Run(() => Aec3Allein(spec, 0.25f));
        var rest3 = Task.Run(() => Restecho(spec, KlangMaschineTests.Kopplung3Db));
        var rest12 = Task.Run(() => Restecho(spec, KlangMaschineTests.Kopplung12Db));
        var restVerzerrt = Task.Run(() => Restecho(spec, KlangMaschineTests.KopplungVerzerrt));
        var restSpiel = Task.Run(() => Restecho(spec, KlangMaschineTests.Kopplung12Db, spielton: true));
        var echo3 = Task.Run(() => KlangMaschineTests.EchoLauf(KlangMaschineTests.Kopplung3Db, true, true, false, spec));
        var echo12 = Task.Run(() => KlangMaschineTests.EchoLauf(KlangMaschineTests.Kopplung12Db, true, true, false, spec));
        var echoVerzerrt = Task.Run(() => KlangMaschineTests.EchoLauf(KlangMaschineTests.KopplungVerzerrt, true, true, false, spec));
        var echoOhneSchutz = Task.Run(() => KlangMaschineTests.EchoLauf(KlangMaschineTests.KopplungVerzerrt, true, false, false, spec));
        var doppel = Task.Run(() => KlangMaschineTests.EchoLauf(KlangMaschineTests.Kopplung12Db, true, true, true, spec));
        return new Kennzahlen(
            name,
            await headsetOhne, await headsetSpiel, await headsetLaut, await headsetLeise, await headsetMusik,
            await Task.WhenAll(lautNeu), await Task.WhenAll(lautAlt), await Task.WhenAll(lautAus), await lautMusik,
            await headsetAllein, await lautAllein,
            (await rest3, await rest12, await restVerzerrt, await restSpiel),
            ((await echo3).Fehlphasen, (await echo12).Fehlphasen, (await echoVerzerrt).Fehlphasen, (await echoOhneSchutz).Fehlphasen),
            ((await doppel).Frei, (await doppel).FreiGesendet),
            (await stimmeNeu12, await stimmeAus12, await stimmeNeuVerzerrt, await stimmeAltVerzerrt, await stimmeAusVerzerrt));
    }

    /// <summary>Eine Zeile je Konfiguration, Spalten wie in docs\MODULBERICHT-aec3.md.</summary>
    internal static string Zeile(Kennzahlen k)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(c,
            $"  MESSUNG {k.Name,-18} | H ohne {k.HeadsetOhne} | H -24 {k.HeadsetSpiel} | H -18 {k.HeadsetLaut} | H -36 {k.HeadsetLeise} | H Musik {k.HeadsetMusik}" +
            $" | L 12/20/30 neu {k.LautsprecherNeu[0]} {k.LautsprecherNeu[1]} {k.LautsprecherNeu[2]} | alt {k.LautsprecherAlt[0]} {k.LautsprecherAlt[1]} {k.LautsprecherAlt[2]}" +
            $" | ohne {k.LautsprecherAus[0]} {k.LautsprecherAus[1]} {k.LautsprecherAus[2]} | L Musik {k.LautsprecherMusik}" +
            $" | Spielstimme 12 neu/aus {k.Spielstimme.Neu12}/{k.Spielstimme.Aus12} verz neu/alt/aus {k.Spielstimme.NeuVerzerrt}/{k.Spielstimme.AltVerzerrt}/{k.Spielstimme.AusVerzerrt}" +
            $" | AEC H 7s {k.Headset.Anfang:F0} Anl {k.Headset.Anlaut:F1} Mit {k.Headset.Mitte:F1} | AEC L12 7s {k.Lautsprecher12.Anfang:F0} Anl {k.Lautsprecher12.Anlaut:F1} Mit {k.Lautsprecher12.Mitte:F1}" +
            $" | Rest 3/12/verz/Spiel {k.Restecho.Db3:F1} {k.Restecho.Db12:F1} {k.Restecho.Verzerrt:F1} {k.Restecho.Spiel12:F1}" +
            $" | Echo-F 3/12/verz {k.EchoFehl.Db3} {k.EchoFehl.Db12} {k.EchoFehl.Verzerrt} ohne Schutz {k.EchoFehl.VerzerrtOhneSchutz} | Doppel {k.Doppelsprechen.Gesendet}/{k.Doppelsprechen.Frei}");
    }

    [Test]
    public static async Task Messung_Kandidaten()
    {
        if (Aus()) return;
        var liste = Kandidaten.ToList();
        if (Environment.GetEnvironmentVariable("FUNK_AEC3") is { Length: > 0 } eigen)
            liste.Add(("eigen", EchoProzessor.Aec3Webrtc + ";" + eigen));
        if (Environment.GetEnvironmentVariable("FUNK_AEC3_NUR") is { Length: > 0 } nur)
            liste = liste.Where(k => nur.Split(',').Any(n => k.Name.StartsWith(n.Trim(), StringComparison.Ordinal))).ToList();
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        var ergebnisse = await Task.WhenAll(liste.Select(k => Task.Run(() => Messen(k.Name, k.Spec))));
        foreach (var k in ergebnisse) Console.WriteLine(Zeile(k));
        Console.WriteLine($"  MESSUNG {liste.Count} Konfigurationen in {uhr.Elapsed.TotalSeconds:F0} s");
    }

    [Test]
    public static async Task Messung_HeadsetOhneAec()
    {
        // Vergleichswert: Headset mit Spielton, Echounterdrückung aus (dann ist die AEC3-Konfiguration ohne Belang).
        if (Aus()) return;
        var spiel = await HeadsetDreiSeeds(null, (n, seed) => Spielton(n, -24f, seed), aec: false);
        var ohne = await HeadsetDreiSeeds(null, null, aec: false);
        Console.WriteLine($"  MESSUNG Headset AEC aus: ohne Spielton {ohne}, Spielton -24 dBFS {spiel}");
        var lautOhne = await Task.WhenAll(Kopplungen.Select(k => Task.Run(async () =>
        {
            var summe = new Anlaute(0, 0, 0);
            foreach (var seed in Seeds)
            {
                var (sprache, marken) = Testszenen.Sprache(60, seed);
                await using var s = new Pruefstand();
                s.Maschine.Echounterdrueckung = false;
                await s.StartenAsync();
                SpieltonZuspielen(s, Spielton(sprache.Length, -24f, seed + 1));
                EchoAnlegen(s, x => k * x);
                s.Abspielen(sprache);
                summe += Auswerten(s.Pakete, marken, 500);
            }
            return summe;
        })));
        Console.WriteLine($"  MESSUNG Lautsprecher AEC aus: 12/20/30 dB {lautOhne[0]} {lautOhne[1]} {lautOhne[2]}");
    }

    [Test]
    public static async Task Messung_Diagnose()
    {
        // Je Äußerung: Verzug der Phase ab Anlaut (ms, "-" = verpasst) und die höchste Sprachwahrscheinlichkeit in den
        // ersten 120 ms (RNNoise, im Gate um 20 ms versetzt). Szene über FUNK_DIAGNOSE: "headset" oder die Kopplung in dB
        // (12, 20, 30); Konfiguration über FUNK_AEC3 (ab Upstream), sonst die Voreinstellung der DLL.
        if (Aus()) return;
        var szene = Environment.GetEnvironmentVariable("FUNK_DIAGNOSE") ?? "12";
        var spec = Environment.GetEnvironmentVariable("FUNK_AEC3") is { Length: > 0 } e ? EchoProzessor.Aec3Webrtc + ";" + e : null;
        var seed = int.Parse(Environment.GetEnvironmentVariable("FUNK_SEED") ?? "151", CultureInfo.InvariantCulture);
        foreach (var (name, aec, schutz) in new[] { ("AEC an, Schutz an", true, true), ("AEC an, Schutz aus", true, false), ("AEC aus", false, false) })
        {
            var (sprache, marken) = Testszenen.Sprache(60, seed);
            var spiel = Spielton(sprache.Length, -24f, seed + 1);
            await using var s = new Pruefstand(aec3: spec);
            s.Maschine.Echounterdrueckung = aec;
            await s.StartenAsync();
            s.Maschine.Kette.EchoschutzAn = schutz;
            SpieltonZuspielen(s, spiel);
            if (szene != "headset")
            {
                var k = MathF.Pow(10f, -float.Parse(szene, CultureInfo.InvariantCulture) / 20f);
                var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
                s.Echo = (w, mic) =>
                {
                    for (var i = 0; i < mic.Length; i++)
                    {
                        linie.Enqueue(k * 0.5f * (w[2 * i] + w[2 * i + 1]));
                        mic[i] += linie.Dequeue();
                    }
                };
            }
            s.Abspielen(sprache);
            var phasen = Phasen(s.Pakete);
            var zeilen = marken.Select(a =>
            {
                var ph = phasen.FirstOrDefault(p => p.Beginn * 480 <= a.Start + 4800 && (p.Ende + 1) * 480 >= a.Start, (-1, -1));
                var verzug = ph.Item1 < 0 ? "-" : ((ph.Item1 * 480 - a.Start) / 48).ToString(CultureInfo.InvariantCulture);
                var t0 = 1000 + a.Start / 480 * 10L;
                var fenster = s.Meldungen.Where(m => m.ZeitMs >= t0 && m.ZeitMs < t0 + 120).ToList();
                var p = fenster.Select(m => m.Sprachwahrscheinlichkeit).DefaultIfEmpty(0f).Max();
                var pegel = fenster.Select(m => m.PegelDbfs).DefaultIfEmpty(-100f).Max();
                var vorher = s.Meldungen.LastOrDefault(m => m.ZeitMs < t0);
                return $"{a.Start / 48000.0:F1}s {verzug} p{p:F2} {pegel:F0}/{vorher.RauschbodenDb:F0}/{vorher.SchwelleDb:F0}";
            });
            Console.WriteLine($"  MESSUNG Diagnose {szene}, {name}, Kopplung gelernt {s.Maschine.Kette.Echoschutz.KopplungDb:F1} dB: " + string.Join(" | ", zeilen));
        }
    }

    [Test]
    public static void Messung_WiedergabeSprachartig()
    {
        // RNNoise auf der Echoreferenz: Anteil der Rahmen mit Sprachwahrscheinlichkeit über 0,5 / 0,7 / 0,9 für Spielton,
        // Musik und Sprache (Grundlage für den Echoschutz, der nur sprachartige Wiedergabe beachten soll).
        if (Aus()) return;
        var n = 60 * 48000;
        foreach (var (name, signal) in new (string, float[])[]
                 {
                     ("Spielton -24", Spielton(n, -24f, 152)), ("Spielton -12", Spielton(n, -12f, 152)),
                     ("Musik -24", Musik(n, -24f, 153)), ("Sprache", Testszenen.Sprache(60, 61).Signal),
                     ("Rauschen weiß -30", AnbindungSignale.Rauschen(n, -30, 7)),
                 })
        {
            using var rnn = new RnnoiseProzessor();
            var aus = new float[480];
            var p = new List<float>();
            for (var t = 0; t + 480 <= signal.Length; t += 480)
            {
                if (Pegel.Dbfs(signal.AsSpan(t, 480)) < -60f) { _ = rnn.Verarbeiten(signal.AsSpan(t, 480), aus); continue; }
                p.Add(rnn.Verarbeiten(signal.AsSpan(t, 480), aus));
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  MESSUNG Wiedergabe {name}: {p.Count} Rahmen über -60 dBFS, p>0,5 {p.Count(x => x > 0.5f) * 100.0 / p.Count:F0} %, p>0,7 {p.Count(x => x > 0.7f) * 100.0 / p.Count:F0} %, p>0,9 {p.Count(x => x > 0.9f) * 100.0 / p.Count:F0} %"));
        }
    }

    /// <summary>Echopfade der Gegenprüfung: 3, 12, 20 und 30 dB Kopplung (linear) und der verzerrende Lautsprecher.</summary>
    private static readonly (string Name, Func<float, float> Pfad)[] PruefPfade =
    [
        ("3 dB", KlangMaschineTests.Kopplung3Db), ("12 dB", KlangMaschineTests.Kopplung12Db), ("20 dB", x => 0.1f * x),
        ("30 dB", x => 0.0316f * x), ("verzerrt", KlangMaschineTests.KopplungVerzerrt),
    ];

    /// <summary>
    /// Spielton (oder Musik) über die Lautsprecher ins Mikrofon, der Nutzer schweigt (Grundrauschen -65 dBFS), 40 s; kein
    /// Funk. Ergebnis: Phasen ab 5 s, also Auslösungen durch das Echo von Geräusch. <paramref name="abstand"/>: Abstand des
    /// Echoschutzes bei Geräusch (<see cref="Echoschutz.GeraeuschAbstandDb"/>), null heißt Voreinstellung.
    /// </summary>
    /// <param name="vorbereiten">Vor dem Start an der Maschine (etwa eine feste Verstärkung der Pegelstufe).</param>
    internal static async Task<int> SpieltonEcho(int seed, float dbfs, Func<float, float> pfad, bool musik, float? abstand, string? spec = null,
        Action<Pruefstand>? vorbereiten = null)
    {
        var spiel = musik ? Musik(40 * 48000, dbfs, seed) : Spielton(40 * 48000, dbfs, seed);
        await using var s = new Pruefstand(aec3: spec);
        vorbereiten?.Invoke(s);
        await s.StartenAsync();
        if (abstand is { } a) s.Maschine.Kette.Echoschutz.GeraeuschAbstandDb = a;
        SpieltonZuspielen(s, spiel);
        EchoAnlegen(s, pfad);
        s.Abspielen(AnbindungSignale.Rauschen(spiel.Length, -65, 93));
        return Phasen(s.Pakete).Count(p => p.Beginn >= 500);
    }

    [Test]
    public static async Task Messung_SpieltonEcho()
    {
        // Gegenprüfung aec3: Öffnet das Echo von Spielton oder Musik das Gate, wenn der Echoschutz bei Geräusch ruht? Je
        // Abstand bei Geräusch (FUNK_ABSTAENDE, "-inf" = Schutz ruht): Auslösungen über drei Seeds, sieben Pegel (-42 bis
        // -18 dBFS) und fünf Echopfade, getrennt für Spielton und Musik, dazu die Anlaut-Szenen (Headset -24 dBFS,
        // Lautsprecher 12/20/30 dB, Lautsprecher mit Musik). Konfiguration über FUNK_AEC3 (ab Upstream), sonst die der DLL.
        if (Aus()) return;
        var abstaende = (Environment.GetEnvironmentVariable("FUNK_ABSTAENDE") ?? "-inf,0,3,6,10")
            .Split(',').Select(a => a == "-inf" ? float.NegativeInfinity : float.Parse(a, CultureInfo.InvariantCulture)).ToArray();
        var spec = Environment.GetEnvironmentVariable("FUNK_AEC3") is { Length: > 0 } e ? EchoProzessor.Aec3Webrtc + ";" + e : null;
        foreach (var abstand in abstaende)
        {
            var spiel = new List<Task<int>>();
            var musik = new List<Task<int>>();
            foreach (var seed in new[] { 152, 162, 172 })
            {
                foreach (var db in new[] { -42f, -36f, -33f, -30f, -27f, -24f, -18f })
                {
                    foreach (var (_, pfad) in PruefPfade)
                    {
                        spiel.Add(Task.Run(() => SpieltonEcho(seed, db, pfad, false, abstand, spec)));
                        musik.Add(Task.Run(() => SpieltonEcho(seed + 1, db, pfad, true, abstand, spec)));
                    }
                }
            }
            var fehlSpiel = (await Task.WhenAll(spiel)).Sum();
            var fehlMusik = (await Task.WhenAll(musik)).Sum();
            var headset = await HeadsetDreiSeeds(spec, (n, seed) => Spielton(n, -24f, seed), geraeuschAbstand: abstand);
            var laut = await Task.WhenAll(Kopplungen.Select(k => LautsprecherDreiSeeds(spec, k, Schutz.Neu, geraeuschAbstand: abstand)));
            var lautMusik = await LautsprecherDreiSeeds(spec, 0.25f, Schutz.Neu, musik: true, geraeuschAbstand: abstand);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  MESSUNG Abstand bei Geräusch {abstand} dB: Auslösungen Spielton {fehlSpiel}/{spiel.Count}, Musik {fehlMusik}/{musik.Count} Szenen" +
                $" | Headset -24 {headset} | L 12/20/30 {laut[0]} {laut[1]} {laut[2]} | L Musik {lautMusik}"));
        }
    }

    [Test]
    public static async Task Messung_FunkImSpielton()
    {
        // Gegenprüfung aec3: Ein anderer Funker (echt Opus-kodiert) und Spielton oder Musik auf demselben Ausgang, beides über
        // die Lautsprecher ins Mikrofon, der Nutzer schweigt. Gezählt: Auslösungen durch das Echo je Stellung des
        // Echoschutzes, über drei Seeds, fünf Pegel (-42 bis -27 dBFS) und fünf Echopfade. "mit Funk" ist die
        // Voreinstellung, "nur RNNoise" ohne die Meldung des eigenen Mischers, "alt" zählt jede Wiedergabe als Sprache.
        if (Aus()) return;
        var stellungen = new (string Name, bool NurSprache, bool MitFunk)[] { ("mit Funk", true, true), ("nur RNNoise", true, false), ("alt", false, true) };
        foreach (var musik in new[] { false, true })
        {
            var laeufe = new List<(string Stellung, string Szene, Task<KlangMaschineTests.EchoErgebnis> Lauf)>();
            foreach (var seed in new[] { 152, 162, 172 })
            {
                foreach (var db in new[] { -42f, -36f, -33f, -30f, -27f })
                {
                    var spiel = musik ? Musik(40 * 48000, db, seed + 1) : Spielton(40 * 48000, db, seed);
                    foreach (var (pfadName, pfad) in PruefPfade)
                    {
                        foreach (var (name, nurSprache, mitFunk) in stellungen)
                        {
                            laeufe.Add((name, $"Seed {seed}, {db} dBFS, {pfadName}",
                                Task.Run(() => KlangMaschineTests.EchoLauf(pfad, true, true, false, spielton: spiel, nurSprache: nurSprache, mitFunk: mitFunk))));
                        }
                    }
                }
            }
            foreach (var (name, _, _) in stellungen)
            {
                var eigene = laeufe.Where(l => l.Stellung == name).ToList();
                var summe = 0;
                foreach (var (_, szene, lauf) in eigene)
                {
                    var f = (await lauf).Fehlphasen;
                    summe += f;
                    if (f > 0) Console.WriteLine($"    {(musik ? "Musik" : "Spielton")}, {name}, {szene}: {f}");
                }
                Console.WriteLine($"  MESSUNG Funk im {(musik ? "Musik" : "Spielton")}, {name}: {summe} Auslösungen in {eigene.Count} Szenen zu 40 s");
            }
        }
    }

    [Test]
    public static void Messung_KetteOhneGate()
    {
        // APM (Produkteinstellung) und RNNoise wie in der Sendekette, ohne Gate: je Anlaut höchste Sprachwahrscheinlichkeit und
        // höchster Pegel nach RNNoise in den ersten 120 ms, mit und ohne AEC. Kopplung über FUNK_DIAGNOSE (dB), Konfiguration
        // über FUNK_AEC3 (ab Upstream).
        if (Aus()) return;
        var db = float.Parse(Environment.GetEnvironmentVariable("FUNK_DIAGNOSE") ?? "30", CultureInfo.InvariantCulture);
        var spec = Environment.GetEnvironmentVariable("FUNK_AEC3") is { Length: > 0 } e ? EchoProzessor.Aec3Webrtc + ";" + e : null;
        var (sprache, marken) = Testszenen.Sprache(60, 151);
        var spiel = Spielton(sprache.Length, -24f, 152);
        var mikrofon = (float[])sprache.Clone();
        var k = MathF.Pow(10f, -db / 20f);
        for (var i = 1920; i < mikrofon.Length; i++) mikrofon[i] += k * spiel[i - 1920];
        foreach (var aec in new[] { true, false })
        {
            var nachApm = ApmLauf(spec, aec, spiel, mikrofon, agc: true);
            using var rnn = new RnnoiseProzessor();
            var p = new float[nachApm.Length / 480];
            var aus = new float[nachApm.Length];
            var ent = new float[480];
            for (var t = 0; t + 480 <= nachApm.Length; t += 480)
            {
                p[t / 480] = rnn.Verarbeiten(nachApm.AsSpan(t, 480), ent);
                ent.CopyTo(aus, t);
            }
            var zeilen = marken.Select(a =>
            {
                var f0 = a.Start / 480;
                var pMax = Enumerable.Range(f0, 12).Max(f => p[f]);
                var vorApm = Enumerable.Range(f0, 12).Max(f => Pegel.Dbfs(nachApm.AsSpan(f * 480, 480)));
                var nachRnn = Enumerable.Range(f0 + 2, 12).Max(f => Pegel.Dbfs(aus.AsSpan(f * 480, 480)));
                // Höhen: Pegel der ersten Differenz (Vorbetonung, +6 dB je Oktave) in denselben 120 ms.
                var diff = new float[12 * 480];
                for (var i = 1; i < diff.Length; i++) diff[i] = nachApm[f0 * 480 + i] - nachApm[f0 * 480 + i - 1];
                return $"{a.Start / 48000.0:F1}s p{pMax:F2} {vorApm:F0}/{nachRnn:F0} H{Pegel.Dbfs(diff):F0}";
            });
            Console.WriteLine($"  MESSUNG Kette ohne Gate {db} dB, AEC {(aec ? "an" : "aus")} (p, Pegel nach APM/nach RNNoise): " + string.Join(" | ", zeilen));
            if (Environment.GetEnvironmentVariable("FUNK_RAHMEN") is { Length: > 0 } zeit)
            {
                // Rahmen um einen Anlaut: Pegel nach APM, p, Pegel nach RNNoise (dessen Ausgang ist 2 Rahmen später).
                var a = marken.First(m => m.Start >= double.Parse(zeit, CultureInfo.InvariantCulture) * 48000);
                var f0 = a.Start / 480;
                var r = Enumerable.Range(f0 - 30, 45).Select(f =>
                    $"{(f - f0) * 10}ms {Pegel.Dbfs(nachApm.AsSpan(f * 480, 480)):F0}/p{p[f]:F2}/{Pegel.Dbfs(aus.AsSpan((f + 2) * 480, 480)):F0}");
                Console.WriteLine("    " + string.Join(" ", r));
            }
        }
    }

    [Test]
    public static void Messung_RestechoLaufzeit()
    {
        // Restecho (AEC3 allein, Fernsprache, 12 dB) über die Echolaufzeit in Samples und den Vorlauf des Filters.
        if (Aus()) return;
        foreach (var vorlauf in new[] { 32, 64, 128 })
        {
            var spec = EchoProzessor.Aec3Webrtc + ";delay.delay_headroom_samples=" + vorlauf.ToString(CultureInfo.InvariantCulture);
            var zeile = Laufzeiten.AsParallel().AsOrdered()
                .Select(v => $"{v}:{Restecho(spec, KlangMaschineTests.Kopplung12Db, verzugSamples: v):F1}").ToList();
            Console.WriteLine($"  MESSUNG Restecho 12 dB, Vorlauf {vorlauf}: " + string.Join(" ", zeile));
        }
    }

    [Test]
    public static void Messung_RestechoVergleich()
    {
        // Restecho (AEC3 allein, Fernsprache) über alle Laufzeiten aus Laufzeiten: Mittel und schlechtester Wert, Upstream
        // gegen die Voreinstellung der DLL. Ein einzelner Wert bei 40 ms schwankt zu stark für ein Urteil.
        if (Aus()) return;
        foreach (var (name, spec) in new[] { ("webrtc", EchoProzessor.Aec3Webrtc), ("DLL", (string?)null) })
        {
            foreach (var (pfadName, pfad) in new (string, Func<float, float>)[] { ("3 dB", KlangMaschineTests.Kopplung3Db), ("12 dB", KlangMaschineTests.Kopplung12Db), ("verzerrt", KlangMaschineTests.KopplungVerzerrt) })
            {
                var werte = Laufzeiten.AsParallel().Select(v => Restecho(spec, pfad, verzugSamples: v)).ToList();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  MESSUNG Restecho {name}, {pfadName}: Mittel {werte.Average():F1} dB, schlechtester {werte.Max():F1} dB über {werte.Count} Laufzeiten"));
            }
        }
    }

    [Test]
    public static void Messung_Fingerabdruck()
    {
        // SHA-256 der APM-Ausgabe (AEC3, Hochpass, AGC2) für eine feste Szene mit Echo, Spielton und eigener Sprache, ohne
        // Aec3Einstellen: So lässt sich jede webrtc_apm.dll vergleichen, auch eine ohne apm_aec3_* (Version 1.0.0).
        if (Aus()) return;
        var (sprache, _) = Testszenen.Sprache(20, 151);
        var spiel = Spielton(sprache.Length, -24f, 152);
        var mikrofon = (float[])sprache.Clone();
        for (var i = 1920; i < mikrofon.Length; i++) mikrofon[i] += 0.25f * spiel[i - 1920];
        using var apm = new EchoProzessor();
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk);
        _ = apm.SetzeVerzoegerung(20);
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var r = new float[480];
        var x = new float[480];
        // Ein stiller Aufnahmerahmen vorweg: Version 1.0.0 schaltet den AEC per ApplyConfig ein, und das APM initialisiert
        // sich erst beim nächsten Aufnahmerahmen neu. Ohne diesen Rahmen verwirft 1.0.0 den ersten Wiedergaberahmen (der
        // alte EchoCanceller3 nimmt ihn noch an), 1.1.0 nicht; die Ausgaben lägen dann um diesen Rahmen versetzt.
        _ = apm.VerarbeiteAufnahme(x);
        for (var t = 0; t + 480 <= mikrofon.Length; t += 480)
        {
            Array.Copy(spiel, t, r, 0, 480);
            _ = apm.VerarbeiteWiedergabe(r);
            Array.Copy(mikrofon, t, x, 0, 480);
            _ = apm.VerarbeiteAufnahme(x);
            sha.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(x.AsSpan()));
        }
        Console.WriteLine($"  MESSUNG Fingerabdruck {EchoProzessor.Version}: {Convert.ToHexString(sha.GetHashAndReset())}");
    }

    [Test]
    public static void Messung_Aec3_Zeitverlauf()
    {
        // AEC3 allein je Äußerung: Dämpfung in den ersten 100 ms ab Anlaut und in der Mitte, Headset und Lautsprecher 12 dB.
        if (Aus()) return;
        foreach (var (name, spec) in new[] { ("webrtc", EchoProzessor.Aec3Webrtc), ("DLL", (string?)null) })
        {
            Console.WriteLine($"  MESSUNG AEC3 allein {name}, Headset:");
            _ = Aec3Allein(spec, 0f, zeilen: true);
            Console.WriteLine($"  MESSUNG AEC3 allein {name}, Lautsprecher 12 dB:");
            _ = Aec3Allein(spec, 0.25f, zeilen: true);
            Console.WriteLine($"  MESSUNG AEC3 mit AGC2 {name}, Lautsprecher 30 dB:");
            _ = Aec3Allein(spec, 0.0316f, zeilen: true, agc: true);
            Console.WriteLine($"  MESSUNG AEC3 allein {name}, Lautsprecher 30 dB:");
            _ = Aec3Allein(spec, 0.0316f, zeilen: true);
            Console.WriteLine($"  MESSUNG AEC3 allein {name}, Headset, Musik:");
            _ = Aec3Allein(spec, 0f, zeilen: true, musik: true);
        }
    }

    /// <summary>Spielton: tiefpassgefiltertes Rauschen mit langsamer Hüllkurve und alle 2 s einem lauten Stoß.</summary>
    internal static float[] Spielton(int laenge, float dbfs, int seed)
    {
        var r = new Random(seed);
        var x = new float[laenge];
        double y = 0;
        for (var i = 0; i < laenge; i++)
        {
            y = 0.97 * y + 0.03 * (r.NextDouble() * 2 - 1);
            var huelle = 0.7 + 0.3 * Math.Sin(2 * Math.PI * 0.3 * i / 48000.0);
            if (i % 96000 < 9600) huelle *= 2.5;
            x[i] = (float)(y * huelle);
        }
        return Normieren(x, dbfs);
    }

    /// <summary>
    /// Musik statt Rauschen: alle 0,5 s ein neuer Dreiklang aus harmonischen Tönen (Obertöne 1/k bis 5 kHz, Abklingen
    /// 0,5 s) und ein tiefer Schlag (60 Hz, 30 ms). Tonal und impulsiv, also das Gegenteil des Rauschens.
    /// </summary>
    internal static float[] Musik(int laenge, float dbfs, int seed)
    {
        var r = new Random(seed);
        var x = new float[laenge];
        int[] tonleiter = [0, 2, 4, 5, 7, 9];
        const int Schritt = 24000;
        for (var start = 0; start < laenge; start += Schritt)
        {
            var grund = tonleiter[r.Next(tonleiter.Length)];
            foreach (var intervall in Dreiklang)
            {
                var f = 110 * Math.Pow(2, (grund + intervall + 12 * r.Next(2)) / 12.0);
                var obertoene = (int)(5000 / f);
                for (var i = 0; i < Schritt && start + i < laenge; i++)
                {
                    var t = i / 48000.0;
                    var huelle = Math.Exp(-t * 2) * Math.Min(1, t / 0.01);
                    var phase = 2 * Math.PI * f * t;
                    double s = 0;
                    for (var k = 1; k <= obertoene; k++) s += Math.Sin(k * phase) / k;
                    x[start + i] += (float)(huelle * s);
                }
            }
            for (var i = 0; i < 4800 && start + i < laenge; i++)
            {
                var t = i / 48000.0;
                x[start + i] += (float)(3 * Math.Exp(-t / 0.03) * Math.Sin(2 * Math.PI * 60 * t));
            }
        }
        return Normieren(x, dbfs);
    }

    private static float[] Normieren(float[] x, float dbfs)
    {
        var rms = Math.Sqrt(x.Select(v => (double)v * v).Average());
        var skala = Math.Pow(10, dbfs / 20) / rms;
        for (var i = 0; i < x.Length; i++) x[i] = (float)(x[i] * skala);
        return x;
    }
}
