// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Prüfstand der Klangstufe C2a (Auto Dauersend-Dämpfung): nachgestellte Gespräche aus dem Klangkorpus durch die ganze Kette (Klangmaschine mit
/// Geräte-Attrappe, simulierter Uhr, AEC3, RNNoise, Pegelstufe, Gate, Opus; die anderen Funker als Quellen im Mischer, gestreamt aus dem Korpus mit
/// eigener Stimmlage je Funker). Läuft nur mit FUNK_MESSUNG=1 (wie MessungKlangC1), rechnet die Szenen parallel (FUNK_C2_KERNE, ab Werk 12).
/// <list type="bullet">
/// <item><see cref="Messung_KlangC2_Einmessen"/>: misst die Wahrscheinlichkeiten der Formel bei Echo (Überlappung, Anlauf, Ansprechen) und die
/// Verteilungen des Gleichlaufs bei Echo und bei eigener Stimme mit Funk (Doppelsprechen), mit eigenen Saaten; der Wächter urteilt, handelt aber
/// nicht. Ergebnis: die Zahlen für <c>WaechterKalibrierung.AbWerk</c>.</item>
/// <item><see cref="Messung_KlangC2_Abnahme"/>: Q7, mindestens 10 Stunden Gespräche mit 4, 10 und 25 Sprechern (Headset und Lautsprecher mit
/// Echounterdrückung, Doppelsprechen im Gefecht, lange eigene Ansage) ohne einen einzigen Fehlalarm, dazu je Szene die Zeit bis zur Erkennung (Echo in
/// mehreren Kopplungen, verzerrend, Spielton und Musik, Kinder- und Fremdstimmen im Raum, leise eigene Stimme Q38, klemmende Taste, Empfängerseite)
/// und die kurzen Funksprüche (Sarevoc: immer gesendet).</item>
/// </list>
/// Ergebnis in der Konsole und mit FUNK_MESSUNG_ORDNER (unter C:\Claude) als MESSUNG-C2-EINMESSEN.md und MESSUNG-C2-ABNAHME.md.
/// </summary>
internal static class MessungKlangC2
{
    private const int R = 480;
    private const int Rate = 48000;
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------------------------------------------------------------------
    // Pläne
    // ------------------------------------------------------------------------------------------------------------------------------

    internal enum Hoerweise { Headset, Lautsprecher, Offen }

    internal enum Spiel { Keins, Spielton, Musik }

    /// <summary>Was eine Szene prüft; bestimmt, welches Urteil richtig und welches ein Fehlalarm ist.</summary>
    internal enum Gruppe { Gespraech, Ansage, Echo, Raum, Leise, Spiel, Klemm, Kurz, Empfang }

    /// <summary>
    /// Ein Stück Korpus auf der Zeitachse einer Szene (Samples): Äußerung <paramref name="Nr"/> (ganz, oder ab <paramref name="Von"/> für
    /// <paramref name="Dauer"/> Samples), Verstärkung, Abspieltempo (Stimmlage).
    /// </summary>
    internal readonly record struct Teil(int Start, int Nr, bool Kurz, float Gain, float Tempo, int Von = 0, int Dauer = 0)
    {
        public KorpusAeusserung Aeusserung => Kurz ? Kurzkorpus.Aeusserungen[Nr] : Klangkorpus.Aeusserungen[Nr];

        private int Quelle => Dauer > 0 ? Dauer : Aeusserung.Ende - Aeusserung.Start - Von;

        public int Laenge => (int)(Quelle / Tempo);

        public int Ende => Start + Laenge;

        public float Wert(int i)
        {
            var a = Aeusserung;
            var korpus = Kurz ? Kurzkorpus.Signal : Klangkorpus.Signal;
            var x = a.Start + Von + (i * Tempo);
            var k = (int)x;
            if (k + 1 >= a.Start + Von + Quelle) return 0f;
            var t = x - k;
            return Gain * ((korpus[k] * (1f - t)) + (korpus[k + 1] * t));
        }
    }

    internal sealed class Plan(string name, Gruppe gruppe, int sekunden, int saat)
    {
        public string Name { get; } = name;

        public Gruppe Gruppe { get; } = gruppe;

        public int Sekunden { get; } = sekunden;

        public int Saat { get; } = saat;

        public int Sprecher { get; set; }

        public List<Teil> Eigen { get; } = [];

        public List<List<Teil>> Funker { get; } = [];

        public List<Teil> Raum { get; } = [];

        public Hoerweise Hoerweise { get; set; }

        public float Kopplung { get; set; } = 0.25f;

        public bool Verzerrt { get; set; }

        public int VerzugMs { get; set; } = 60;

        /// <summary>Ab dieser Sekunde kommt die Wiedergabe ins Mikrofon (vorher Headset-artig); für die Zeit bis zur Erkennung.</summary>
        public int EchoAbS { get; set; }

        public Spiel Spiel { get; set; }

        public float SpielDbfs { get; set; } = -24f;

        public (int VonS, int BisS)? Taste { get; set; }

        public bool Sprachaktivierung { get; set; } = true;

        /// <summary>Empfängerseite: ein Funker echot den ersten (Max echot Moritz), ab dieser Sekunde; -1 keiner.</summary>
        public int EmpfangEchoAbS { get; set; } = -1;

        /// <summary>Empfängerseite: ein Funker sendet ohne Pause ohne Sprache (klemmende Taste oder Musik), ab dieser Sekunde; -1 keiner.</summary>
        public int EmpfangDauerAbS { get; set; } = -1;

        public bool EmpfangDauerMusik { get; set; }

        /// <summary>Ab hier ist das Problem da (für die Zeit bis zur Erkennung), in Sekunden.</summary>
        public int ProblemAbS { get; set; }

        public string Beschreibung { get; set; } = "";

        /// <summary>Schalter "Leises Reden im Raum aussperren" (ab Werk aus); die Szenen Raum, Leise und kurze Sprüche "raum" messen ihn an.</summary>
        public bool LeisesRedenSperren { get; set; }

        /// <summary>Rauschfilter der Szene (ab Werk Standard, RNNoise).</summary>
        public Rauschfilter Rauschfilter { get; set; } = Rauschfilter.Standard;
    }

    /// <summary>Äußerungen der eigenen Stimme (Stefan) in Funklautstärke: normal, abfallend, anschwellend, mit betontem Wort, gerufen.</summary>
    private static int[] EigeneNr => [.. Klangkorpus.Aeusserungen.Where(a => a.Stimme == "Stefan" && a.Huelle != "leise").Select(a => a.Nr)];

    private static int[] EigeneFunklautNr => [.. Klangkorpus.Aeusserungen.Where(a => a.Stimme == "Stefan" && a.Huelle is not ("leise" or "laut")).Select(a => a.Nr)];

    /// <summary>
    /// Sätze für leises Reden im Raum (Q38): gleichmäßig gesprochene (normal, abfallend) aller drei Stimmen. Ohne die mit einem 10 dB lauteren Wort oder
    /// anschwellendem Ende: 12 dB leiser gespielt erreicht deren lautes Wort fast die Funkstimme, das ist kein anhaltend leises Reden mehr (gemessen: die
    /// lautesten 150 ms solcher Sätze liegen 2 bis 4 dB unter der Stimme).
    /// </summary>
    private static int[] LeiseNr => [.. Klangkorpus.Aeusserungen.Where(a => a.Huelle is "normal" or "abfallend").Select(a => a.Nr)];

    /// <summary>
    /// Funker im Gespräch: jeder redet unabhängig, mit eigener Stimmlage (Tempo 0,85 bis 1,2) und eigenem Pegel (±4 dB); jeder Spruch ist ein zufälliger
    /// Ausschnitt (0,6 bis 4 s) einer Äußerung von Katja oder Hedda. Ausschnitte statt ganzer Äußerungen und nie die eigene Stimme (Stefan): Der Korpus hat
    /// nur 22 Sätze, und zwei Funker, die denselben Satz zur selben Zeit sagen, liefen im Takt wie ein Echo (im ersten Einmessen 27 eigene Phasen mit
    /// Gleichlauf ab 0,8, meist derselbe Satz bei Funk und eigener Stimme). Die Pausen so, dass zusammen etwa <paramref name="anteil"/> der Zeit Funk läuft.
    /// </summary>
    private static void FunkerAnlegen(Plan p, Random r, int sprecher, double anteil)
    {
        p.Sprecher = sprecher;
        var n = p.Sekunden * Rate;
        var jeSprecher = 1 - Math.Pow(1 - anteil, 1.0 / sprecher);
        var alle = Klangkorpus.Aeusserungen.Where(a => a.Stimme != "Stefan").ToList();
        for (var s = 0; s < sprecher; s++)
        {
            var liste = new List<Teil>();
            var tempo = 0.85f + (0.35f * (float)r.NextDouble());
            var pegel = Pegel.DbZuLinear((float)((r.NextDouble() * 8) - 4));
            var pos = (int)(r.NextDouble() * Rate * 8);
            while (pos < n)
            {
                var a = alle[r.Next(alle.Count)];
                var gesamt = a.Ende - a.Start;
                var dauer = Math.Min(gesamt, (int)(Rate * (0.6 + (3.4 * r.NextDouble()))));
                var von = r.Next(Math.Max(1, gesamt - dauer));
                var t = new Teil(pos, a.Nr, false, pegel * Pegel.DbZuLinear((float)(r.NextDouble() - 0.5)), tempo, von, dauer);
                liste.Add(t);
                var pause = -Math.Log(1 - r.NextDouble()) * t.Laenge * (1 - jeSprecher) / jeSprecher;
                pos = t.Ende + (int)pause + (Rate * 3 / 10);
            }
            p.Funker.Add(liste);
        }
    }

    /// <summary>
    /// Eigene Rede: Anteil <paramref name="anteil"/> der Zeit, Pausen mindestens 0,8 s; mit Wahrscheinlichkeit <paramref name="doppel"/> beginnt ein Spruch
    /// 200 bis 700 ms nach einem Funk-Anfang (Reaktion im Gefecht, mitten ins Reden der anderen), sonst zufällig.
    /// </summary>
    private static void EigenAnlegen(Plan p, Random r, double anteil, double doppel, int[] auswahl, bool kurz = false, float gainDb = 0f, int vonS = 2, int bisS = -1)
    {
        var n = (bisS < 0 ? p.Sekunden : bisS) * Rate;
        var anfaenge = p.Funker.SelectMany(f => f.Select(t => t.Start)).Order().ToArray();
        var pos = vonS * Rate;
        var g = Pegel.DbZuLinear(gainDb);
        while (pos < n)
        {
            var nr = auswahl[r.Next(auswahl.Length)];
            var t = new Teil(pos, nr, kurz, g, 1f);
            if (t.Ende >= n) break;
            p.Eigen.Add(t);
            var mittel = t.Laenge * (1 - anteil) / anteil;
            var naechster = t.Ende + (int)(0.8 * Rate) + (int)(-Math.Log(1 - r.NextDouble()) * mittel);
            if (anfaenge.Length > 0 && r.NextDouble() < doppel)
            {
                var i = Array.BinarySearch(anfaenge, naechster);
                if (i < 0) i = ~i;
                if (i < anfaenge.Length) naechster = Math.Max(naechster, anfaenge[i] + (int)(Rate * (0.2 + (0.5 * r.NextDouble()))));
            }
            pos = naechster;
        }
    }

    internal static Plan Gespraech(string name, int sekunden, int saat, int sprecher, double funk, double eigen, double doppel, Hoerweise h)
    {
        var p = new Plan(name, Gruppe.Gespraech, sekunden, saat) { Hoerweise = h };
        var r = new Random(saat);
        FunkerAnlegen(p, r, sprecher, funk);
        EigenAnlegen(p, r, eigen, doppel, EigeneNr);
        p.Beschreibung = string.Create(C, $"{sprecher} Sprecher, Funk etwa {funk:0%}, eigene Rede {eigen:0%}, Doppelsprechen {doppel:0%}, {h}");
        return p;
    }

    /// <summary>Lange eigene Ansage: fünf Minuten Sprüche mit Pausen von 150 bis 500 ms, über Sprachaktivierung oder mit gehaltener Taste; Funk daneben.</summary>
    internal static Plan Ansage(string name, int saat, bool taste, Hoerweise h)
    {
        var p = new Plan(name, Gruppe.Ansage, 360, saat) { Hoerweise = h };
        var r = new Random(saat);
        FunkerAnlegen(p, r, 10, 0.4);
        var pos = 30 * Rate;
        var auswahl = EigeneNr;
        while (pos < 330 * Rate)
        {
            var t = new Teil(pos, auswahl[r.Next(auswahl.Length)], false, 1f, 1f);
            p.Eigen.Add(t);
            pos = t.Ende + (int)(Rate * (0.15 + (0.35 * r.NextDouble())));
        }
        if (taste)
        {
            p.Taste = (29, 331);
            p.Sprachaktivierung = false;
        }
        p.Beschreibung = $"5 min eigene Ansage am Stück ({(taste ? "Sprechtaste gehalten" : "Sprachaktivierung")}), 10 Sprecher daneben, {h}";
        return p;
    }

    /// <summary>Echo: der Funker schweigt, die Wiedergabe kommt ab <paramref name="abS"/> über Lautsprecher oder offene Kopfhörer ins Mikrofon.</summary>
    internal static Plan Echo(string name, int sekunden, int saat, int sprecher, double funk, Hoerweise h, float kopplung, bool verzerrt, int abS, double eigen = 0)
    {
        var p = new Plan(name, Gruppe.Echo, sekunden, saat) { Hoerweise = h, Kopplung = kopplung, Verzerrt = verzerrt, EchoAbS = abS, ProblemAbS = abS };
        var r = new Random(saat);
        FunkerAnlegen(p, r, sprecher, funk);
        if (eigen > 0) EigenAnlegen(p, r, eigen, 0.3, EigeneNr);
        var k = verzerrt ? "verzerrend" : string.Create(C, $"{20 * Math.Log10(kopplung):0} dB");
        p.Beschreibung = string.Create(C, $"{sprecher} Sprecher, Echo ab {abS} s, Kopplung {k}, {h}, eigene Rede {eigen:0%}");
        return p;
    }

    /// <summary>Kinder- oder Fremdstimmen im Raum (Korpusstimmen schneller und höher), <paramref name="raumDb"/> unter der Funkstimme, dazu eigene Rede.</summary>
    internal static Plan Raum(string name, int sekunden, int saat, float raumDb, float tempo, double eigen, int abS)
    {
        var p = new Plan(name, Gruppe.Raum, sekunden, saat) { Hoerweise = Hoerweise.Headset, ProblemAbS = abS, LeisesRedenSperren = true };
        var r = new Random(saat);
        FunkerAnlegen(p, r, 10, 0.5);
        EigenAnlegen(p, r, eigen, 0.2, EigeneNr);
        var stimmen = Klangkorpus.Aeusserungen.Where(a => a.Stimme != "Stefan" && a.Huelle != "leise").Select(a => a.Nr).ToArray();
        var pos = abS * Rate;
        var g = Pegel.DbZuLinear(raumDb);
        while (pos < sekunden * Rate)
        {
            var t = new Teil(pos, stimmen[r.Next(stimmen.Length)], false, g, tempo);
            p.Raum.Add(t);
            pos = t.Ende + (int)(Rate * (0.5 + (2.5 * r.NextDouble())));
        }
        p.Beschreibung = string.Create(C, $"Stimmen im Raum ab {abS} s, {raumDb:0} dB, Tempo {tempo:0.00}, eigene Rede {eigen:0%}, 10 Sprecher, Headset");
        return p;
    }

    /// <summary>Q38: erst normale Funksprüche, ab <paramref name="leiseAbS"/> die eigene Stimme 12 dB leiser über mehrere Sätze, ab <paramref name="normalAbS"/> wieder normal.</summary>
    internal static Plan Leise(string name, int saat, int leiseAbS, int normalAbS, int sekunden, int sprecher)
    {
        var p = new Plan(name, Gruppe.Leise, sekunden, saat) { Hoerweise = Hoerweise.Headset, ProblemAbS = leiseAbS, LeisesRedenSperren = true };
        var r = new Random(saat);
        FunkerAnlegen(p, r, sprecher, 0.3);
        EigenAnlegen(p, r, 0.3, 0.1, EigeneFunklautNr, bisS: leiseAbS - 2);
        EigenAnlegen(p, r, 0.5, 0, LeiseNr, gainDb: -12f, vonS: leiseAbS, bisS: normalAbS - 2);
        EigenAnlegen(p, r, 0.3, 0.1, EigeneFunklautNr, vonS: normalAbS);
        p.Beschreibung = string.Create(C, $"eigene Stimme 12 dB leiser von {leiseAbS} bis {normalAbS} s, davor und danach normale Funksprüche, {sprecher} Sprecher, Headset");
        return p;
    }

    internal static Plan Spielton(string name, int saat, Spiel spiel, float spielDbfs, Hoerweise h, bool verzerrt, double eigen)
    {
        var p = new Plan(name, Gruppe.Spiel, 240, saat) { Hoerweise = h, Spiel = spiel, SpielDbfs = spielDbfs, Verzerrt = verzerrt, Kopplung = 0.25f, ProblemAbS = 0 };
        var r = new Random(saat);
        FunkerAnlegen(p, r, 4, 0.3);
        if (eigen > 0) EigenAnlegen(p, r, eigen, 0.2, EigeneNr);
        p.Beschreibung = string.Create(C, $"{spiel} {spielDbfs:0} dBFS über die Lautsprecher ins Mikrofon ({h}, {(verzerrt ? "verzerrend" : "-12 dB")}), eigene Rede {eigen:0%}, 4 Sprecher");
        return p;
    }

    internal static Plan Klemm(string name, int saat)
    {
        var p = new Plan(name, Gruppe.Klemm, 150, saat) { Hoerweise = Hoerweise.Headset, Taste = (20, 130), Sprachaktivierung = false, ProblemAbS = 20 };
        var r = new Random(saat);
        FunkerAnlegen(p, r, 10, 0.5);
        p.Beschreibung = "Sprechtaste klemmt ab 20 s für 110 s, Raum still, 10 Sprecher";
        return p;
    }

    /// <summary>Empfängerseite: ein Funker echot den ersten oder sendet dauernd (Rauschen oder Musik), zwischen anderen.</summary>
    internal static Plan Empfang(string name, int saat, int sprecher, bool echo, bool musik)
    {
        var p = new Plan(name, Gruppe.Empfang, 300, saat) { Hoerweise = Hoerweise.Headset, ProblemAbS = 30 };
        var r = new Random(saat);
        FunkerAnlegen(p, r, sprecher, sprecher <= 4 ? 0.3 : 0.6);
        EigenAnlegen(p, r, 0.1, 0.2, EigeneNr);
        if (echo) p.EmpfangEchoAbS = 30;
        else p.EmpfangDauerAbS = 30;
        p.EmpfangDauerMusik = musik;
        p.Beschreibung = string.Create(C, $"{sprecher} Sprecher, ab 30 s {(echo ? "echot ein Funker den ersten" : musik ? "sendet ein Funker Musik ohne Pause" : "klemmt bei einem Funker die Taste (Raumrauschen)")}");
        return p;
    }

    /// <summary>Kurze Funksprüche (Sarevoc): normal und gerufen, allein, im Reden anderer, mit Echo, während der Raum-Sperre, kurz nach einem Echo-Urteil.</summary>
    internal static Plan Kurz(string name, int saat, string fall)
    {
        var r = new Random(saat);
        var kurz = Enumerable.Range(0, Kurzkorpus.Aeusserungen.Count).ToArray();
        Plan p;
        switch (fall)
        {
            case "allein":
                p = new Plan(name, Gruppe.Kurz, 200, saat) { Hoerweise = Hoerweise.Headset };
                FunkerAnlegen(p, r, 1, 0.01);
                p.Funker[0].Clear();
                KurzAnlegen(p, r, kurz, 3, 190, 1.5, 3.5);
                break;
            case "doppel":
                p = new Plan(name, Gruppe.Kurz, 240, saat) { Hoerweise = Hoerweise.Headset };
                FunkerAnlegen(p, r, 10, 0.7);
                KurzAnlegen(p, r, kurz, 3, 230, 1.0, 3.0);
                break;
            case "echo":
                p = new Plan(name, Gruppe.Kurz, 240, saat) { Hoerweise = Hoerweise.Lautsprecher, Kopplung = 0.25f };
                FunkerAnlegen(p, r, 10, 0.7);
                KurzAnlegen(p, r, kurz, 3, 230, 1.0, 3.0);
                break;
            case "raum":
                // Leise Sätze (12 dB unter der Funkstimme), bis die Linie oben ist, dann ein kurzer Spruch; immer wieder.
                p = new Plan(name, Gruppe.Kurz, 600, saat) { Hoerweise = Hoerweise.Headset, LeisesRedenSperren = true };
                FunkerAnlegen(p, r, 4, 0.2);
                var pos = 3 * Rate;
                var leise = LeiseNr;
                while (pos < 590 * Rate)
                {
                    for (var i = 0; i < 4; i++)
                    {
                        var t = new Teil(pos, leise[r.Next(leise.Length)], false, Pegel.DbZuLinear(-12f), 1f);
                        p.Eigen.Add(t);
                        pos = t.Ende + (int)(Rate * (0.9 + (0.6 * r.NextDouble())));
                    }
                    var w = new Teil(pos, kurz[r.Next(kurz.Length)], true, 1f, 1f);
                    p.Eigen.Add(w);
                    pos = w.Ende + (int)(Rate * 1.5);
                }
                break;
            case "Lautsprecher verzerrend":
                // Verzerrender Lautsprecher mit Echounterdrückung, kurze Sprüche mitten im Funk. Gedacht als "kurz nach einem Echo-Urteil"; mit AEC3 kommt
                // aber kaum Echo durch, es gibt kein Urteil (1. Abnahme). Was hier fehlt, verschluckt die Echounterdrückung im Doppelsprechen.
                p = new Plan(name, Gruppe.Kurz, 300, saat) { Hoerweise = Hoerweise.Lautsprecher, Verzerrt = true, EchoAbS = 10 };
                FunkerAnlegen(p, r, 10, 0.6);
                KurzAnlegen(p, r, kurz, 60, 290, 1.5, 3.5);
                break;
            default: // nach Echo-Urteil (ohne Echounterdrückung): offene Kopfhörer, Echo ab 10 s, kurze Sprüche ab 60 s mitten im Funk
                p = new Plan(name, Gruppe.Kurz, 300, saat) { Hoerweise = Hoerweise.Offen, Kopplung = 0.25f, EchoAbS = 10 };
                if (fall.Contains("ohne Rauschfilter", StringComparison.Ordinal)) p.Rauschfilter = Rauschfilter.Aus;
                FunkerAnlegen(p, r, 10, 0.6);
                KurzAnlegen(p, r, kurz, 60, 290, 1.5, 3.5);
                break;
        }
        p.Beschreibung = "kurze Sprüche, " + fall;
        return p;
    }

    private static void KurzAnlegen(Plan p, Random r, int[] kurz, int vonS, int bisS, double pauseMin, double pauseMax)
    {
        var pos = vonS * Rate;
        while (pos < bisS * Rate)
        {
            var t = new Teil(pos, kurz[r.Next(kurz.Length)], true, 1f, 1f);
            p.Eigen.Add(t);
            pos = t.Ende + (int)(Rate * (pauseMin + ((pauseMax - pauseMin) * r.NextDouble())));
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Quellen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Ein Funker im Mischer aus einem Plan (gestreamt): jede Äußerung eine Sprechphase mit 300 ms Nachlauf wie die Sprachaktivierung. Mit
    /// <paramref name="versatz"/> spielt er den Inhalt später (Samples), mit <paramref name="vorAb"/>/<paramref name="nachAb"/> öffnen und schließen seine Phasen
    /// versetzt (für den Funker, der einen anderen echot).
    /// </summary>
    internal sealed class Planquelle(List<Teil> teile, float gain = 1f, int versatz = 0, int vorAb = 0, int nachAb = 300 * 48, float rausch = 0f,
        int abSample = 0) : IKlangquelle
    {
        private int _pos;
        private int _i;
        private readonly Random _r = new(teile.Count + versatz);

        public QuellenEreignis Lesen(Span<float> ziel)
        {
            var von = _pos;
            var bis = _pos + R;
            _pos = bis;
            while (_i < teile.Count && teile[_i].Ende + versatz + nachAb <= von) _i++;
            ziel.Clear();
            var e = QuellenEreignis.Keine;
            for (var k = _i; k < teile.Count && k <= _i + 2; k++)
            {
                var t = teile[k];
                var pv = t.Start + versatz + vorAb;
                var pb = t.Ende + versatz + nachAb;
                if (pv >= bis || pb <= von || t.Start + versatz < abSample) continue;
                e |= QuellenEreignis.Aktiv;
                if (pv >= von) e |= QuellenEreignis.Begonnen;
                if (pb <= bis) e |= QuellenEreignis.Beendet;
                for (var s = 0; s < R; s++)
                {
                    var i = von + s - t.Start - versatz;
                    if (i >= 0 && i < t.Laenge) ziel[s] += gain * t.Wert(i);
                    if (rausch > 0 && von + s >= pv && von + s < pb) ziel[s] += rausch * (float)((_r.NextDouble() * 2) - 1);
                }
            }
            return e;
        }
    }

    /// <summary>Ein Funker, der ab <paramref name="abSample"/> ohne Pause sendet: Raumrauschen (-40 dBFS) oder Musik (-30 dBFS).</summary>
    internal sealed class Dauerquelle(int abSample, bool musik) : IKlangquelle
    {
        private int _pos;
        private readonly float[] _musik = musik ? MessungSpielton.Musik(10 * Rate, -30f, 5) : [];
        private readonly Random _r = new(7);
        private readonly float _a = Pegel.DbZuLinear(-40f) * MathF.Sqrt(3f);

        public QuellenEreignis Lesen(Span<float> ziel)
        {
            var von = _pos;
            _pos += R;
            if (von + R <= abSample)
            {
                ziel.Clear();
                return QuellenEreignis.Keine;
            }
            for (var s = 0; s < R; s++)
                ziel[s] = musik ? _musik[(von + s) % _musik.Length] : _a * (float)((_r.NextDouble() * 2) - 1);
            var e = QuellenEreignis.Aktiv;
            if (von <= abSample) e |= QuellenEreignis.Begonnen;
            return e;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Lauf
    // ------------------------------------------------------------------------------------------------------------------------------

    internal sealed record Phase(int Von, int Bis, Beobachtung B, float PO, float PA, float PG, double Eigen, bool EchoWeg);

    internal sealed record Ergebnis(Plan Plan, List<(int Rahmen, DauersendUrteil Urteil)> Urteile, Dictionary<int, List<(int Rahmen, DauersendUrteil Urteil)>> Empfang,
        bool[] Gesendet, int[] PhasenId, int[] Phasenbeginne, List<Phase> Phasen, long Beantwortet, long Unbeantwortet, int[] Plaetze, int EchoPlatz, int DauerPlatz,
        List<(int Rahmen, float Wunsch)> Raumwunsch, double Sekunden, float EndDaempfungEcho, float EndDaempfungDauer);

    private static float _gain = float.NaN;

    /// <summary>
    /// Die gelernte Verstärkung der eigenen Stimme (Stefan, Funklautstärke): Mittel der Leistung der Rahmen mit Sprachwahrscheinlichkeit ab 0,9, wie die
    /// Halteregel misst, auf das Ziel -23 dBFS. Einmal je Lauf.
    /// </summary>
    internal static float Verstaerkung()
    {
        if (float.IsFinite(_gain)) return _gain;
        using var s = new RnnoiseSchaetzer();
        double summe = 0;
        var n = 0;
        foreach (var nr in EigeneFunklautNr)
        {
            var a = Klangkorpus.Aeusserungen[nr];
            for (var pos = a.Start; pos + R <= a.Ende; pos += R)
            {
                var x = Klangkorpus.Signal.AsSpan(pos, R);
                if (s.Schaetzen(x) < Pegelstufe.SprachSchwelle) continue;
                summe += Pegel.Leistung(x);
                n++;
            }
        }
        _gain = Pegelstufe.ZielDbfs - (float)(10 * Math.Log10(summe / n));
        return _gain;
    }

    internal static async Task<Ergebnis> Laufen(Plan p, bool handeln, Func<Pruefstand, Task>? vorher = null)
    {
        var uhr = Stopwatch.StartNew();
        await using var s = new Pruefstand();
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(true, Verstaerkung(), Gelernt: true);
        s.Maschine.Echounterdrueckung = p.Hoerweise == Hoerweise.Lautsprecher;
        s.Maschine.Daempfung = new DaempfungEinstellung(handeln, Daempfungsstufe.DeutlichLeiser, p.LeisesRedenSperren);
        s.Maschine.Sprachaktivierung = p.Sprachaktivierung;
        s.Maschine.Rauschfilter = p.Rauschfilter;
        await s.StartenAsync();
        if (vorher is not null) await vorher(s);
        s.TelemetrieMitschreiben = true;
        var mischer = s.Maschine.Mischer;
        mischer.Waechter.AngleichungAn = Environment.GetEnvironmentVariable("FUNK_C2_ANGLEICHUNG") != "0";
        var plaetze = p.Funker.Select(f => mischer.QuelleHinzufuegen(new Planquelle(f), 0)).ToArray();
        var echoPlatz = -1;
        var dauerPlatz = -1;
        if (p.EmpfangEchoAbS >= 0)
        {
            echoPlatz = mischer.QuelleHinzufuegen(new Planquelle(p.Funker[0], 0.5f, 200 * 48, -50 * 48, 500 * 48, Pegel.DbZuLinear(-60f), p.EmpfangEchoAbS * Rate), 0);
        }
        if (p.EmpfangDauerAbS >= 0) dauerPlatz = mischer.QuelleHinzufuegen(new Dauerquelle(p.EmpfangDauerAbS * Rate, p.EmpfangDauerMusik), 0);
        if (p.Hoerweise != Hoerweise.Headset)
        {
            var linie = new float[p.VerzugMs * 48];
            var li = 0;
            var abTakt = p.EchoAbS * 100;
            Func<float, float> pfad = p.Verzerrt ? KlangMaschineTests.KopplungVerzerrt : x => p.Kopplung * x;
            s.Echo = (w, mic) =>
            {
                var an = s.Takte >= abTakt;
                for (var i = 0; i < mic.Length; i++)
                {
                    var alt = linie[li];
                    linie[li] = an ? pfad(0.5f * (w[2 * i] + w[2 * i + 1])) : 0f;
                    li = (li + 1) % linie.Length;
                    mic[i] += alt;
                }
            };
        }
        if (p.Spiel != Spiel.Keins)
        {
            var spiel = p.Spiel == Spiel.Musik ? MessungSpielton.Musik(10 * Rate, p.SpielDbfs, p.Saat) : MessungSpielton.Spielton(10 * Rate, p.SpielDbfs, p.Saat);
            s.Fremdton = (takt, w) =>
            {
                var basis = (int)((takt - 1) % (10 * 100)) * R;
                for (var i = 0; i < w.Length / 2; i++)
                {
                    w[2 * i] += spiel[basis + i];
                    w[(2 * i) + 1] += spiel[basis + i];
                }
            };
        }

        var phasen = new List<Phase>();
        var basisTakt = s.Takte;
        var takte = p.Sekunden * 100;
        var diagnose = Environment.GetEnvironmentVariable("FUNK_C2_VERLAUF") is "1" or "2" ? new (float W, float K, int V, float Mic, float P, float Gate, float Linie, float Schwelle, float Abstand)[takte] : null;
        var eigenAktiv = new bool[takte + 200];
        foreach (var t in p.Eigen)
        {
            for (var f = t.Start / R; f <= t.Ende / R && f < eigenAktiv.Length; f++) eigenAktiv[f] = true;
        }
        var w = s.Maschine.Kette.Waechter;
        if (Environment.GetEnvironmentVariable("FUNK_C2_VERLAUF") == "1") w.Diagnose = z => Console.WriteLine($"  VERLAUF {p.Name}: {z}");
        w.Beobachtet = (b, pO, pA, pG) =>
        {
            var bis = (int)(s.Takte - basisTakt);
            var von = Math.Max(0, bis - b.Rahmen);
            var eigen = 0;
            for (var f = von; f < bis; f++)
            {
                if (eigenAktiv[Math.Max(0, f - 3)]) eigen++;
            }
            phasen.Add(new Phase(von, bis, b, pO, pA, pG, b.Rahmen == 0 ? 0 : (double)eigen / b.Rahmen, p.Hoerweise != Hoerweise.Headset && bis >= p.EchoAbS * 100));
        };
        var urteile = new List<(int, DauersendUrteil)> { (0, DauersendUrteil.Keins) };
        var empfang = new Dictionary<int, List<(int, DauersendUrteil)>>();
        foreach (var pl in plaetze.Append(echoPlatz).Append(dauerPlatz).Where(x => x >= 0)) empfang[pl] = [(0, DauersendUrteil.Keins)];
        var raumwunsch = new List<(int, float)> { (0, float.NegativeInfinity) };
        var mic = new float[R];
        var rausch = new Random(p.Saat * 31);
        var rauschA = Pegel.DbZuLinear(Pumpmessung.GrundrauschenDbfs) * MathF.Sqrt(3f);
        var ei = 0;
        var ri = 0;
        for (var t = 0; t < takte; t++)
        {
            var von = t * R;
            for (var i = 0; i < R; i++) mic[i] = rauschA * (float)((rausch.NextDouble() * 2) - 1);
            Mischen(p.Eigen, ref ei, von, mic);
            Mischen(p.Raum, ref ri, von, mic);
            if (p.Taste is { } taste)
            {
                if (t == taste.VonS * 100) s.Maschine.SprechtasteGedrueckt();
                if (t == taste.BisS * 100) s.Maschine.SprechtasteLosgelassen();
            }
            s.Takt(mic);
            var u = s.Maschine.EigenesUrteil.Urteil;
            if (u != urteile[^1].Item2) urteile.Add((t, u));
            if (diagnose is not null) diagnose[t] = (s.Maschine.Kette.Echoschutz.WiedergabeDb, s.Maschine.Kette.Echoschutz.KopplungDb, s.Maschine.Kette.Echoschutz.VerzugRahmen, s.Telemetrie.Count > 0 ? s.Telemetrie[^1].EingangDbfs : 0f, s.Telemetrie.Count > 0 ? s.Telemetrie[^1].Sprachwahrscheinlichkeit : 0f, s.Telemetrie.Count > 0 ? s.Telemetrie[^1].GatePegelDbfs : 0f, s.Maschine.Kette.Fuehrung.LinieDb, s.Maschine.Kette.Fuehrung.Gate.SchwelleWirksam, s.Maschine.Kette.Fuehrung.Gate.AbstandAutoDb);
            var zw = s.Maschine.Kette.Fuehrung.ZusatzWunschDb;
            if (zw != raumwunsch[^1].Item2) raumwunsch.Add((t, zw));
            if (t % 10 == 0)
            {
                foreach (var (pl, liste) in empfang)
                {
                    var eu = mischer.Zustand(pl).Urteil;
                    if (eu != liste[^1].Item2) liste.Add((t, eu));
                }
            }
        }
        if (Environment.GetEnvironmentVariable("FUNK_C2_VERLAUF") == "1") Console.WriteLine($"  VERLAUF {p.Name}: Raum zurück Anlauf/Phase/Frist {string.Join("/", w.RaumAufgehoben)}");
        if (Environment.GetEnvironmentVariable("FUNK_C2_FENSTER") == "1") FensterAnalyse(p, s.Telemetrie);
        var ids = PhasenRahmen(s.Telemetrie, 12, ohneTaste: p.Taste is null);
        var gesendet = ids.Select(x => x > 0).ToArray();
        if (diagnose is not null)
        {
            var probe = new Ergebnis(p, urteile, empfang, gesendet, ids, [], [], 0, 0, [], -1, -1, [], 0, 0, 0);
            foreach (var t in p.Eigen.Where(t => t.Kurz || Environment.GetEnvironmentVariable("FUNK_C2_VERLAUF") == "2"))
            {
                var wErg = Wort(probe, t);
                if (wErg.Hinaus && wErg.FehltAnfangMs <= 80 && !wErg.EndeFehlt && !wErg.Unterbrochen) continue;
                var f0 = t.Start / R;
                var zeilen = Enumerable.Range(f0 - 2, Math.Min(50, (t.Ende / R) - f0 + 4)).Where(f => f >= 0 && f < diagnose.Length)
                    .Select(f => string.Create(C, $"{f}:{diagnose[f].Mic:0}/{diagnose[f].W:0}{diagnose[f].K:+0;-0}/e{s.Telemetrie[f].EchoDbfs:0}/v{s.Telemetrie[f].PegelstufeDb:0}/r{s.Telemetrie[f].RauschfilterDbfs:0}/g{diagnose[f].Gate:0}/l{diagnose[f].Linie:0}/p{diagnose[f].P:0.00}/s{diagnose[f].Schwelle:0.00}/a{diagnose[f].Abstand:0}/{s.Telemetrie[f].Gate.ToString()[..2]}"));
                Console.WriteLine(string.Create(C, $"  VERLAUF {p.Name} Wort {t.Aeusserung.Text} bei {t.Start / (double)Rate:0.00} s: hinaus {wErg.Hinaus}, fehlt {wErg.FehltAnfangMs} ms, Ende {wErg.EndeFehlt}, unterbrochen {wErg.Unterbrochen}, Verzug {diagnose[f0].V}: {string.Join(" ", zeilen)}"));
            }
        }
        var beginne = s.Pakete.Where(k => (k.Kennzeichen & Sprachkennzeichen.Beginn) != 0).Select(k => (int)(k.Takt - basisTakt)).ToArray();
        return new Ergebnis(p, urteile, empfang, gesendet, ids, beginne, phasen, w.Beantwortet, w.Unbeantwortet, plaetze, echoPlatz, dauerPlatz, raumwunsch,
            uhr.Elapsed.TotalSeconds, echoPlatz >= 0 ? mischer.Zustand(echoPlatz).DaempfungDb : 0f, dauerPlatz >= 0 ? mischer.Zustand(dauerPlatz).DaempfungDb : 0f);
    }

    /// <summary>Die Teile, die den Block ab <paramref name="von"/> berühren, ins Mikrofon; <paramref name="i"/> ist der erste noch mögliche Teil.</summary>
    private static void Mischen(List<Teil> teile, ref int i, int von, Span<float> mic)
    {
        while (i < teile.Count && teile[i].Ende <= von) i++;
        for (var k = i; k < teile.Count && teile[k].Start < von + R; k++)
        {
            var t = teile[k];
            for (var s = 0; s < R; s++)
            {
                var j = von + s - t.Start;
                if (j >= 0 && j < t.Laenge) mic[s] += t.Wert(j);
            }
        }
    }

    /// <summary>Welche Rahmen gesendet wurden, aus dem Zustand des Gates, mit dem Vorlauf (wie <c>MessungKlangC.GesendeteRahmen</c>).</summary>
    internal static bool[] GesendeteRahmen(List<TelemetrieEintrag> t, int vorlaufRahmen) => [.. PhasenRahmen(t, vorlaufRahmen).Select(x => x > 0)];

    /// <summary>Zu welcher Sendephase jeder Rahmen gehört (1, 2, ...; 0: nicht gesendet), mit dem Vorlauf beim Öffnen.</summary>
    /// <param name="ohneTaste">Die Szene nutzt keine Sprechtaste: Öffnet das Gate direkt aus Leise, war es das Funkniveau mit dem ganzen Vorpuffer
    /// (die Taste öffnet auch direkt, aber ohne Vorlauf).</param>
    internal static int[] PhasenRahmen(List<TelemetrieEintrag> t, int vorlaufRahmen, bool ohneTaste = false)
    {
        var gesendet = new int[t.Count];
        var phase = 0;
        var vorher = false;
        var letzter = -1;
        var anlauf = -1;
        var burst = new GateEinstellungen().BurstHoechstRahmen;
        var zuvor = GateZustand.Leise;
        for (var f = 0; f < t.Count; f++)
        {
            var z = t[f].Gate;
            if (z == GateZustand.Anlauf && anlauf < 0) anlauf = f;
            var offen = t[f].Sendet && z is GateZustand.Senden or GateZustand.Nachlauf;
            if (offen)
            {
                if (!vorher) phase++;
                if (anlauf >= 0)
                {
                    for (var r = Math.Max(Math.Max(letzter + 1, anlauf - vorlaufRahmen), f - burst); r < f; r++) gesendet[r] = phase;
                }
                else if (ohneTaste && !vorher && zuvor == GateZustand.Leise)
                {
                    // Direkt aus Leise geöffnet: das Funkniveau während der Raum-Anhebung (SprachGate.FunkniveauDbfs), mit dem ganzen Vorpuffer.
                    for (var r = Math.Max(letzter + 1, f - burst); r < f; r++) gesendet[r] = phase;
                }
                gesendet[f] = phase;
                letzter = f;
            }
            vorher = offen;
            zuvor = z;
            if (z is not (GateZustand.Anlauf or GateZustand.Zurueckgehalten)) anlauf = -1;
        }
        return gesendet;
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Auswertung
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Ein Wort aus Sicht des Empfängers: fehlt sein Anfang (ms), fehlt sein Ende, ist es unterbrochen (Lücke oder neue Phase mittendrin)?</summary>
    internal readonly record struct WortErgebnis(Teil Teil, bool Hinaus, int FehltAnfangMs, bool EndeFehlt, bool Unterbrochen);

    internal static WortErgebnis Wort(Ergebnis e, Teil t)
    {
        var verzug = MessungKlangB.VerzugGate(Rauschfilter.Standard);
        var f0 = (t.Start + verzug) / R;
        var f1 = (t.Ende + verzug - 1) / R;
        var g = e.Gesendet;
        int? erster = null;
        var luecke = false;
        for (var f = f0; f <= f1 && f < g.Length; f++)
        {
            if (g[f]) erster ??= f;
            else if (erster is not null) luecke = true;
        }
        var ende = f1 >= g.Length || !g[f1];
        // Mehr als eine Sendephase in einem Wort: ENDE und neuer BEGINN mittendrin (beim Empfänger ein Bruch), auch ohne Lücke.
        var neu = e.PhasenId.Skip(f0).Take(f1 - f0 + 1).Where(x => x > 0).Distinct().Count() > 1;
        return new WortErgebnis(t, erster is not null, erster is { } x ? (x - f0) * 10 : (f1 - f0 + 1) * 10, ende, luecke || neu);
    }

    /// <summary>Wortanfänge eines normalen Spruchs, deren erste 30 ms nicht ganz gesendet wurden (Zählweise MessungKlangC1).</summary>
    internal static (int Woerter, int Angeschnitten) Anfaenge(Ergebnis e, IEnumerable<Teil> teile)
    {
        var verzug = MessungKlangB.VerzugGate(Rauschfilter.Standard);
        int woerter = 0, ab = 0;
        foreach (var t in teile)
        {
            var a = t.Aeusserung;
            foreach (var w in a.Woerter)
            {
                var pos = t.Start + (int)((w - a.Start) / t.Tempo);
                var f0 = (pos + verzug) / R;
                var f1 = (pos + verzug + 1440 - 1) / R;
                woerter++;
                var fehlt = false;
                for (var f = f0; f <= f1; f++) fehlt |= f >= e.Gesendet.Length || !e.Gesendet[f];
                if (fehlt) ab++;
            }
        }
        return (woerter, ab);
    }

    /// <summary>Phasen der Sprachaktivierung, die hinausgingen, ohne dass die eigene Stimme sprach (Echo, Spiel, Raum), ab <paramref name="vonRahmen"/>.</summary>
    internal static int FremdePhasen(Ergebnis e, int vonRahmen, int bisRahmen = int.MaxValue)
    {
        var eigen = new bool[e.Gesendet.Length + 100];
        foreach (var t in e.Plan.Eigen)
        {
            for (var f = (t.Start / R) - 15; f <= (t.Ende / R) + 40; f++)
            {
                if (f >= 0 && f < eigen.Length) eigen[f] = true;
            }
        }
        return e.Phasenbeginne.Count(b => b >= vonRahmen && b < bisRahmen && !eigen[Math.Clamp(b, 0, eigen.Length - 1)]);
    }

    private static string Zeit(int rahmen) => rahmen < 0 ? "-" : string.Create(C, $"{rahmen / 100.0:0.0} s");

    // ------------------------------------------------------------------------------------------------------------------------------
    // Einmessen
    // ------------------------------------------------------------------------------------------------------------------------------

    private static bool Aus(string was)
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") == "1") return false;
        Console.WriteLine($"HINWEIS {was} übersprungen (FUNK_MESSUNG=1 setzen).");
        return true;
    }

    private static int Kerne => Environment.GetEnvironmentVariable("FUNK_C2_KERNE") is { Length: > 0 } k && int.TryParse(k, C, out var n) ? Math.Max(1, n) : 12;

    private static async Task<List<Ergebnis>> AlleLaufen(List<Plan> plaene, bool handeln)
    {
        var tor = new SemaphoreSlim(Kerne);
        var uhr = Stopwatch.StartNew();
        var fertig = 0;
        var aufgaben = plaene.Select(p => Task.Run(async () =>
        {
            await tor.WaitAsync();
            try
            {
                var e = await Laufen(p, handeln);
                Console.WriteLine(string.Create(C, $"  C2 fertig {Interlocked.Increment(ref fertig)}/{plaene.Count}: {p.Name} ({p.Sekunden} s in {e.Sekunden:0} s, gesamt {uhr.Elapsed.TotalSeconds:0} s)"));
                return e;
            }
            finally
            {
                tor.Release();
            }
        })).ToList();
        return [.. await Task.WhenAll(aufgaben)];
    }

    private static void Schreiben(string datei, StringBuilder md)
    {
        Console.WriteLine(md.ToString());
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG_ORDNER") is not { Length: > 0 } ordner) return;
        var ziel = Path.GetFullPath(ordner.Trim());
        if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_MESSUNG_ORDNER muss unter C:\Claude liegen: " + ziel);
        Directory.CreateDirectory(ziel);
        File.WriteAllText(Path.Combine(ziel, datei), md.ToString(), new UTF8Encoding(false));
    }

    [Test]
    public static async Task Messung_KlangC2_Einmessen()
    {
        if (Aus("Einmessen Klangstufe C2a")) return;
        var plaene = new List<Plan>();
        var saat = 1000;
        foreach (var (sp, funk) in new[] { (4, 0.3), (10, 0.55), (25, 0.8) })
        {
            foreach (var (h, k, v) in new[] { (Hoerweise.Offen, 0.25f, false), (Hoerweise.Offen, 0.1f, false), (Hoerweise.Offen, 0.5f, false), (Hoerweise.Lautsprecher, 0.8f, true), (Hoerweise.Offen, 0.8f, true) })
                plaene.Add(Echo($"Einmessen Echo {sp}", 300, saat++, sp, funk, h, k, v, 0));
            plaene.Add(Gespraech($"Einmessen Gespräch {sp}", 900, saat++, sp, funk, 0.25, 0.4, Hoerweise.Headset));
            plaene.Add(Gespraech($"Einmessen Gespräch {sp} Lautsprecher", 900, saat++, sp, funk, 0.25, 0.4, Hoerweise.Lautsprecher));
        }
        var erg = await AlleLaufen(plaene, handeln: false);
        var echo = erg.Where(e => e.Plan.Gruppe == Gruppe.Echo).SelectMany(e => e.Phasen).Where(x => x.Eigen == 0 && x.EchoWeg).ToList();
        var eigen = erg.Where(e => e.Plan.Gruppe == Gruppe.Gespraech).SelectMany(e => e.Phasen).Where(x => x.Eigen >= 0.5).ToList();
        var qO = echo.Count(x => x.B.Ueberlappung) / (double)echo.Count;
        var ersteEcho = echo.Where(x => x.PA != 0).ToList();
        var qA = ersteEcho.Count(x => x.B.Anlauf) / (double)Math.Max(1, ersteEcho.Count);
        var beantwortet = erg.Where(e => e.Plan.Gruppe == Gruppe.Echo).Sum(e => e.Beantwortet);
        var unbeantwortet = erg.Where(e => e.Plan.Gruppe == Gruppe.Echo).Sum(e => e.Unbeantwortet);
        var qAn = beantwortet / (double)Math.Max(1, beantwortet + unbeantwortet);
        var hE = Histogramm(echo.Where(x => x.B.Ueberlappung && float.IsFinite(x.B.Gleichlauf)).Select(x => x.B.Gleichlauf));
        var hO = Histogramm(eigen.Where(x => x.B.Ueberlappung && float.IsFinite(x.B.Gleichlauf)).Select(x => x.B.Gleichlauf));
        var punkte = Punkte(hE, hO);
        var md = new StringBuilder();
        md.AppendLine("# Einmessen der Formel (Klangstufe C2a)");
        md.AppendLine();
        md.AppendLine(string.Create(C, $"Erzeugt am {DateTime.Now:dd.MM.yyyy HH:mm} von `MessungKlangC2.Messung_KlangC2_Einmessen`; Szenen {plaene.Count}, Saaten ab 1000 (die Abnahme nimmt andere), Wächter urteilt, handelt nicht."));
        md.AppendLine();
        md.AppendLine(string.Create(C, $"- Echo-Phasen (keine eigene Stimme, Echo da): {echo.Count}; eigene Phasen im Gespräch (eigene Stimme in mindestens der Hälfte): {eigen.Count}"));
        md.AppendLine(string.Create(C, $"- Überlappung bei Echo: {qO:0.000} ({echo.Count(x => x.B.Ueberlappung)}/{echo.Count})"));
        md.AppendLine(string.Create(C, $"- Anlauf bei Echo (erste Abschnitte): {qA:0.000} ({ersteEcho.Count(x => x.B.Anlauf)}/{ersteEcho.Count})"));
        md.AppendLine(string.Create(C, $"- Ansprechen bei Echo (Funk-Anfang bei geschlossenem Gate, Phase in 400 ms): {qAn:0.000} ({beantwortet}/{beantwortet + unbeantwortet})"));
        md.AppendLine(string.Create(C, $"- Eigene Phasen: Überlappung {eigen.Count(x => x.B.Ueberlappung) / (double)Math.Max(1, eigen.Count):0.000}, Anlauf {eigen.Count(x => x.B.Anlauf) / (double)Math.Max(1, eigen.Count):0.000}"));
        md.AppendLine(string.Create(C, $"- Phasen mit Gleichlauf (ab {Phasenbeobachter.MindestRahmenGleichlauf * 10} ms und Funk): Echo {echo.Count(x => x.B.Ueberlappung && float.IsFinite(x.B.Gleichlauf))}, eigene {eigen.Count(x => x.B.Ueberlappung && float.IsFinite(x.B.Gleichlauf))}"));
        var hoch = eigen.Where(x => x.B.Gleichlauf >= 0.8f).ToList();
        md.AppendLine(string.Create(C, $"- Eigene Phasen mit Gleichlauf ab 0,8: {hoch.Count} (Länge Median {(hoch.Count == 0 ? 0 : hoch.Select(x => x.B.Rahmen).Order().ElementAt(hoch.Count / 2) * 10)} ms, Anteil eigener Stimme Mittel {(hoch.Count == 0 ? 0 : hoch.Average(x => x.Eigen)):0.00})"));
        var echoLaenge = echo.Select(x => x.B.Rahmen).Order().ToList();
        md.AppendLine(string.Create(C, $"- Länge der Echo-Phasen: Median {(echoLaenge.Count == 0 ? 0 : echoLaenge[echoLaenge.Count / 2] * 10)} ms, unter 400 ms {echo.Count(x => x.B.Rahmen < Phasenbeobachter.MindestRahmenGleichlauf)}"));
        var punkteEcho = echo.Select(x => x.PO + x.PA + x.PG).ToList();
        var punkteEigen = eigen.Select(x => x.PO + x.PA + x.PG).ToList();
        md.AppendLine(string.Create(C, $"- Punkte je Phase (mit der Kalibrierung dieses Laufs): Echo Mittel {(punkteEcho.Count == 0 ? 0 : punkteEcho.Average()):0.00}, eigene Mittel {(punkteEigen.Count == 0 ? 0 : punkteEigen.Average()):0.00}, eigene über 0: {punkteEigen.Count(x => x > 0)}"));
        md.AppendLine();
        md.AppendLine("| Gleichlauf r | Echo | eigene Stimme mit Funk | Punkte (ln, geglättet, monoton, begrenzt ±3) |");
        md.AppendLine("| --- | --- | --- | --- |");
        for (var i = 0; i < WaechterKalibrierung.GleichlaufFaecher; i++)
            md.AppendLine(string.Create(C, $"| {-1 + (i * 0.1):0.0} bis {-0.9 + (i * 0.1):0.0} | {hE[i]} | {hO[i]} | {punkte[i]:0.00} |"));
        md.AppendLine();
        md.AppendLine("Für den Code: `new(" + qO.ToString("0.00", C) + "f, " + qA.ToString("0.00", C) + "f, " + qAn.ToString("0.00", C) + "f, [" + string.Join(", ", punkte.Select(x => x.ToString("0.0", C) + "f")) + "])`");
        Schreiben("MESSUNG-C2-EINMESSEN.md", md);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Abnahme
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Die Szenen der Abnahme (Saaten ab 5000, nicht die des Einmessens).</summary>
    internal static List<Plan> AbnahmePlaene()
    {
        var plaene = new List<Plan>();
        var saat = 5000;
        foreach (var (name, sp, funk, eigen, doppel, je) in new[]
        {
            ("4 Sprecher", 4, 0.3, 0.2, 0.2, 4), ("10 Sprecher", 10, 0.55, 0.2, 0.3, 4), ("25 Sprecher", 25, 0.8, 0.15, 0.4, 4),
            ("Gefecht 25, Doppelsprechen", 25, 0.8, 0.35, 0.7, 4), ("Gefecht 10, Doppelsprechen", 10, 0.6, 0.4, 0.5, 2), ("4 Sprecher, viel eigene Rede", 4, 0.3, 0.5, 0.3, 2),
        })
        {
            for (var i = 0; i < je; i++)
            {
                plaene.Add(Gespraech($"{name}, Headset {i + 1}", 900, saat++, sp, funk, eigen, doppel, Hoerweise.Headset));
                plaene.Add(Gespraech($"{name}, Lautsprecher {i + 1}", 900, saat++, sp, funk, eigen, doppel, Hoerweise.Lautsprecher));
            }
        }
        plaene.Add(Ansage("Ansage 5 min, Sprachaktivierung, Headset", saat++, false, Hoerweise.Headset));
        plaene.Add(Ansage("Ansage 5 min, Sprachaktivierung, Lautsprecher", saat++, false, Hoerweise.Lautsprecher));
        plaene.Add(Ansage("Ansage 5 min, Sprechtaste, Headset", saat++, true, Hoerweise.Headset));
        plaene.Add(Ansage("Ansage 5 min, Sprechtaste, Lautsprecher", saat++, true, Hoerweise.Lautsprecher));
        foreach (var (sp, funk) in new[] { (4, 0.3), (10, 0.55), (25, 0.8) })
        {
            plaene.Add(Echo($"Echo {sp} Sprecher, offen -12 dB", 300, saat++, sp, funk, Hoerweise.Offen, 0.25f, false, 60));
            plaene.Add(Echo($"Echo {sp} Sprecher, offen -20 dB", 300, saat++, sp, funk, Hoerweise.Offen, 0.1f, false, 60));
            plaene.Add(Echo($"Echo {sp} Sprecher, offen -6 dB", 300, saat++, sp, funk, Hoerweise.Offen, 0.5f, false, 60));
            plaene.Add(Echo($"Echo {sp} Sprecher, Lautsprecher verzerrend (AEC an)", 300, saat++, sp, funk, Hoerweise.Lautsprecher, 0.8f, true, 60));
        }
        plaene.Add(Echo("Echo 10 Sprecher, offen verzerrend", 300, saat++, 10, 0.55, Hoerweise.Offen, 0.8f, true, 60));
        plaene.Add(Echo("Echo 10 Sprecher, offen -12 dB, eigene Rede", 300, saat++, 10, 0.55, Hoerweise.Offen, 0.25f, false, 60, eigen: 0.2));
        plaene.Add(Raum("Kinder im Raum, -15 dB", 240, saat++, -15f, 1.3f, 0.15, 60));
        plaene.Add(Raum("Kinder im Raum, -20 dB", 240, saat++, -20f, 1.3f, 0.15, 60));
        plaene.Add(Raum("Fremde Stimme im Raum, -12 dB", 240, saat++, -12f, 1.0f, 0.15, 60));
        plaene.Add(Raum("Fernseher im Raum, -18 dB", 240, saat++, -18f, 0.95f, 0.15, 60));
        plaene.Add(Leise("Leise zum Sohn, 4 Sprecher", saat++, 60, 180, 300, 4));
        plaene.Add(Leise("Leise zum Sohn, 10 Sprecher", saat++, 60, 180, 300, 10));
        plaene.Add(Leise("Leise zum Sohn, 25 Sprecher", saat++, 60, 180, 300, 25));
        plaene.Add(Spielton("Spielton -24 dBFS, Lautsprecher verzerrend", saat++, Spiel.Spielton, -24f, Hoerweise.Lautsprecher, true, 0.1));
        plaene.Add(Spielton("Musik -24 dBFS, Lautsprecher verzerrend", saat++, Spiel.Musik, -24f, Hoerweise.Lautsprecher, true, 0.1));
        plaene.Add(Spielton("Spielton -18 dBFS, Lautsprecher", saat++, Spiel.Spielton, -18f, Hoerweise.Lautsprecher, false, 0.1));
        plaene.Add(Spielton("Musik -24 dBFS, offene Kopfhörer", saat++, Spiel.Musik, -24f, Hoerweise.Offen, false, 0.1));
        plaene.Add(Klemm("Klemmende Taste 1", saat++));
        plaene.Add(Klemm("Klemmende Taste 2", saat++));
        plaene.Add(Empfang("Empfang: Echo eines Funkers, 4 Sprecher", saat++, 4, true, false));
        plaene.Add(Empfang("Empfang: Echo eines Funkers, 10 Sprecher", saat++, 10, true, false));
        plaene.Add(Empfang("Empfang: Echo eines Funkers, 25 Sprecher", saat++, 25, true, false));
        plaene.Add(Empfang("Empfang: klemmende Taste eines Funkers", saat++, 10, false, false));
        plaene.Add(Empfang("Empfang: Musik eines Funkers", saat++, 10, false, true));
        foreach (var fall in KurzFaelle) plaene.Add(Kurz("Kurze Sprüche, " + fall, saat++, fall));
        return plaene;
    }

    /// <summary>
    /// Die Fälle der kurzen Sprüche. "nach Echo-Urteil" läuft ohne Echounterdrückung (nur dort urteilt der Wächter Echo), einmal mit RNNoise (ab Werk)
    /// und einmal ohne Rauschfilter: RNNoise verschluckt im Gemisch mit dem Echo eigene Wörter, mit und ohne Wächter (KlangC2MaschineTests).
    /// </summary>
    internal static readonly string[] KurzFaelle = ["allein", "doppel", "echo", "raum", "Lautsprecher verzerrend", "nach Echo-Urteil", "nach Echo-Urteil ohne Rauschfilter"];

    /// <summary>Was in einer Szene richtig ist (eigene Seite); alles andere ist ein Fehlurteil.</summary>
    private static bool Richtig(Plan p, DauersendUrteil u) => p.Gruppe switch
    {
        Gruppe.Echo => u == DauersendUrteil.Echo,
        Gruppe.Raum or Gruppe.Leise => u == DauersendUrteil.Raumgeraeusch,
        Gruppe.Spiel => u is DauersendUrteil.Spielton or DauersendUrteil.Raumgeraeusch,
        Gruppe.Klemm => u == DauersendUrteil.KlemmendeTaste,
        Gruppe.Kurz => (p.Beschreibung.Contains("raum", StringComparison.Ordinal) && u == DauersendUrteil.Raumgeraeusch)
            || (p.Beschreibung.Contains("Echo-Urteil", StringComparison.Ordinal) && u == DauersendUrteil.Echo)
            || (p.Beschreibung.Contains("Lautsprecher", StringComparison.Ordinal) && u == DauersendUrteil.Echo),
        _ => false,
    };

    internal sealed record Zeile(Ergebnis E, int ErstesRichtig, int Fehlurteile, int FremdeVorher, int FremdeNachher, int EmpfangFehl, int EmpfangErkannt,
        (int Woerter, int Ab) AnfaengeVorher, (int Woerter, int Ab) AnfaengeNachher, List<WortErgebnis> Kurze, int Leise, int LeiseVorher, int LeiseNachher,
        Saetze Saetze);

    /// <summary>
    /// Normale eigene Sätze, gezählt wie in der Gegenprüfung C1 (M3): Wortanfänge, denen mehr als 80 ms fehlen; Lücken (ein Wort nach dem ersten
    /// gesendeten Rahmen des Satzes angeschnitten); Sätze, deren letzte 150 ms nicht ganz hinausgingen. Ein Ende der Phase in einer Satzpause ist keine Lücke.
    /// </summary>
    internal readonly record struct Saetze(int Anzahl, int Woerter, int Ueber80, int Luecken, int EndeFehlt)
    {
        public static Saetze Aus(Ergebnis e, IEnumerable<Teil> teile)
        {
            var verzug = MessungKlangB.VerzugGate(Rauschfilter.Standard);
            var g = e.Gesendet;
            bool Hinaus(int f) => f >= 0 && f < g.Length && g[f];
            int anzahl = 0, woerter = 0, ueber80 = 0, luecken = 0, ende = 0;
            foreach (var t in teile)
            {
                anzahl++;
                var f0 = (t.Start + verzug) / R;
                var f1 = (t.Ende + verzug - 1) / R;
                var erster = Enumerable.Range(f0, Math.Max(0, f1 - f0 + 1)).Where(Hinaus).DefaultIfEmpty(int.MaxValue).First();
                var a = t.Aeusserung;
                foreach (var w in a.Woerter)
                {
                    var imTeil = (w - a.Start - t.Von) / t.Tempo;
                    if (imTeil < 0 || imTeil >= t.Laenge) continue;
                    woerter++;
                    var pos = t.Start + (int)imTeil + verzug;
                    var cut = Enumerable.Range(pos / R, ((pos + 1440 - 1) / R) - (pos / R) + 1).Any(f => !Hinaus(f));
                    if (cut && pos / R > erster) luecken++;
                    if (!Hinaus((pos + 3840) / R)) ueber80++;
                }
                if (Enumerable.Range((t.Ende + verzug - 7200) / R, 15).Any(f => !Hinaus(f))) ende++;
            }
            return new Saetze(anzahl, woerter, ueber80, luecken, ende);
        }

        public static Saetze operator +(Saetze a, Saetze b) =>
            new(a.Anzahl + b.Anzahl, a.Woerter + b.Woerter, a.Ueber80 + b.Ueber80, a.Luecken + b.Luecken, a.EndeFehlt + b.EndeFehlt);

        public override string ToString() => string.Create(C, $"{Ueber80} / {Luecken} von {Woerter} Wörtern, Ende {EndeFehlt} von {Anzahl}");
    }

    /// <summary>Ging von einem Teil der eigenen Stimme mindestens ein Rahmen hinaus?</summary>
    private static bool Gesendet(Ergebnis e, Teil t)
    {
        var verzug = MessungKlangB.VerzugGate(Rauschfilter.Standard);
        for (var f = (t.Start + verzug) / R; f <= (t.Ende + verzug) / R && f < e.Gesendet.Length; f++)
        {
            if (e.Gesendet[f]) return true;
        }
        return false;
    }

    internal static Zeile Auswerten(Ergebnis e)
    {
        var p = e.Plan;
        var ab = p.ProblemAbS * 100;
        var erstes = e.Urteile.Where(u => u.Rahmen >= ab && Richtig(p, u.Urteil)).Select(u => u.Rahmen).DefaultIfEmpty(-1).First();
        var fehl = e.Urteile.Count(u => u.Urteil != DauersendUrteil.Keins && !Richtig(p, u.Urteil));
        var problemPlatz = e.EchoPlatz >= 0 ? e.EchoPlatz : e.DauerPlatz;
        var empfFehl = e.Empfang.Where(kv => kv.Key != problemPlatz).Sum(kv => kv.Value.Count(u => u.Urteil != DauersendUrteil.Keins));
        var empfErkannt = problemPlatz >= 0 ? e.Empfang[problemPlatz].Where(u => u.Urteil != DauersendUrteil.Keins).Select(u => u.Rahmen).DefaultIfEmpty(-1).First() : -1;
        var grenze = erstes >= 0 ? erstes : e.Gesendet.Length;
        var fremdVor = FremdePhasen(e, ab, grenze);
        var fremdNach = erstes >= 0 ? FremdePhasen(e, erstes + 300) : 0;
        // Im Gespräch gibt es kein Problem ab einer Zeit: alle normalen eigenen Sätze zählen (1. Abnahme: die Spalte zeigte 0 von 0).
        var gespraech = p.Gruppe is Gruppe.Gespraech or Gruppe.Ansage;
        var eigeneVor = gespraech ? p.Eigen.Where(t => t.Gain >= 0.9f && !t.Kurz)
            : p.Gruppe == Gruppe.Leise ? p.Eigen.Where(t => t.Start < ab * R) : p.Eigen.Where(t => t.Start < ab * R && t.Gain >= 0.9f && !t.Kurz);
        var eigeneNach = gespraech ? []
            : p.Gruppe == Gruppe.Leise ? p.Eigen.Where(t => t.Gain >= 0.9f && t.Start > ab * R) : p.Eigen.Where(t => t.Gain >= 0.9f && !t.Kurz && t.Start >= grenze * R);
        var kurze = p.Eigen.Where(t => t.Kurz).Select(t => Wort(e, t)).ToList();
        var leise = p.Eigen.Where(t => t.Gain < 0.9f).ToList();
        var bis = erstes >= 0 ? erstes : int.MaxValue / 2;
        var leiseVor = leise.Count(t => t.Start / R < bis && Gesendet(e, t));
        var leiseNach = leise.Count(t => t.Start / R >= bis + 300 && Gesendet(e, t));
        return new Zeile(e, erstes, fehl, fremdVor, fremdNach, empfFehl, empfErkannt, Anfaenge(e, eigeneVor), Anfaenge(e, eigeneNach), kurze, leise.Count, leiseVor, leiseNach,
            Saetze.Aus(e, eigeneVor.Concat(eigeneNach).Where(t => !t.Kurz && t.Gain >= 0.9f)));
    }

    [Test]
    public static async Task Messung_KlangC2_Abnahme()
    {
        if (Aus("Abnahme Klangstufe C2a")) return;
        var plaene = AbnahmePlaene();
        if (Environment.GetEnvironmentVariable("FUNK_C2_NUR") is { Length: > 0 } nur)
            plaene = [.. plaene.Where(p => nur.Split('|').Any(n => p.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))];
        var uhr = Stopwatch.StartNew();
        var erg = await AlleLaufen(plaene, handeln: Environment.GetEnvironmentVariable("FUNK_C2_HANDELN") != "0");
        // Kurze Sprüche zum Vergleich auch ohne Wächter (Schalter aus): Was dort ebenso fehlt, liegt nicht am Wächter.
        var ohneWaechter = (await AlleLaufen([.. plaene.Where(p => p.Gruppe == Gruppe.Kurz)], handeln: false)).ToDictionary(e => e.Plan.Name);
        if (Environment.GetEnvironmentVariable("FUNK_C2_VERLAUF") == "1")
        {
            foreach (var e in erg)
            {
                Console.WriteLine($"  VERLAUF {e.Plan.Name}: Urteile {string.Join(", ", e.Urteile.Select(u => string.Create(C, $"{u.Urteil}@{u.Rahmen / 100.0:0.0}")))}");
                Console.WriteLine($"  VERLAUF {e.Plan.Name}: Wunsch {string.Join(", ", e.Raumwunsch.Select(u => string.Create(C, $"{u.Wunsch:0.0}@{u.Rahmen / 100.0:0.0}")))}");
                foreach (var t in e.Plan.Eigen.Where(t => t.Gain < 0.9f)) Console.WriteLine(string.Create(C, $"  VERLAUF leise {t.Start / (double)Rate:0.0}-{t.Ende / (double)Rate:0.0} s hinaus {Gesendet(e, t)}"));
                var erstesUrteil = e.Urteile.FirstOrDefault(u => u.Urteil != DauersendUrteil.Keins);
                if (erstesUrteil.Urteil != DauersendUrteil.Keins)
                {
                    foreach (var ph in e.Phasen.Where(x => x.Bis > erstesUrteil.Rahmen - 6000 && x.Bis <= erstesUrteil.Rahmen + 10))
                        Console.WriteLine(string.Create(C, $"  VERLAUF Phase {ph.Von / 100.0:0.0}-{ph.Bis / 100.0:0.0} s eigen {ph.Eigen:0.00} O {ph.B.Ueberlappung} A {ph.B.Anlauf} r {ph.B.Gleichlauf:0.00}@{ph.B.BesteVerschiebung} Punkte {ph.PO:0.00}/{ph.PA:0.00}/{ph.PG:0.00} Pegel {ph.B.PegelDb:0.0} Sprache {ph.B.SprachAnteil:0.00}"));
                }
            }
        }
        var zeilen = erg.Select(Auswerten).ToList();
        var md = new StringBuilder();
        md.AppendLine("# Abnahme der Auto Dauersend-Dämpfung (Klangstufe C2a)");
        md.AppendLine();
        md.AppendLine(string.Create(C, $"Erzeugt am {DateTime.Now:dd.MM.yyyy HH:mm} von `MessungKlangC2.Messung_KlangC2_Abnahme`, {plaene.Count} Szenen, {erg.Sum(e => e.Plan.Sekunden) / 3600.0:0.0} h Ton, gerechnet in {uhr.Elapsed.TotalMinutes:0.0} min auf {Kerne} Kernen. Saaten ab 5000 (Einmessen: ab 1000)."));
        md.AppendLine();
        var gespraech = zeilen.Where(z => z.E.Plan.Gruppe is Gruppe.Gespraech or Gruppe.Ansage).ToList();
        var stunden = gespraech.Sum(z => z.E.Plan.Sekunden) / 3600.0;
        var fehlEigen = gespraech.Sum(z => z.E.Urteile.Count(u => u.Urteil != DauersendUrteil.Keins));
        var fehlEmpf = gespraech.Sum(z => z.EmpfangFehl);
        md.AppendLine("## Q7: Gespräche ohne Fehlalarm");
        md.AppendLine();
        md.AppendLine(string.Create(C, $"{stunden:0.00} h Gespräch in {gespraech.Count} Szenen: Fehlalarme der eigenen Seite {fehlEigen}, der Empfängerseite (je Funker) {fehlEmpf}."));
        md.AppendLine();
        md.AppendLine("| Szene | Dauer | Fehlalarme eigen | Fehlalarme Empfang | Phasen ohne eigene Stimme hinaus | Wortanfänge eigener Sprüche angeschnitten | normale Sätze: Wortanfang über 80 ms / Lücken, Ende fehlt |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var g in gespraech.GroupBy(z => z.E.Plan.Name.TrimEnd("0123456789 ".ToCharArray())))
        {
            var l = g.ToList();
            var w = l.Sum(z => z.AnfaengeVorher.Woerter + z.AnfaengeNachher.Woerter);
            var a = l.Sum(z => z.AnfaengeVorher.Ab + z.AnfaengeNachher.Ab);
            md.AppendLine(string.Create(C, $"| {g.Key} ({l.Count}) | {l.Sum(z => z.E.Plan.Sekunden) / 60.0:0} min | {l.Sum(z => z.E.Urteile.Count(u => u.Urteil != DauersendUrteil.Keins))} | {l.Sum(z => z.EmpfangFehl)} | {l.Sum(z => FremdePhasen(z.E, 0))} | {a} von {w} | {l.Aggregate(default(Saetze), (x, z) => x + z.Saetze)} |"));
        }
        md.AppendLine();
        md.AppendLine("## Erkennung je Szene (eigene Seite)");
        md.AppendLine();
        md.AppendLine("| Szene | Beschreibung | richtiges Urteil nach | Fehlurteile | fremde Phasen hinaus bis dahin | danach (ab +3 s) | leise eigene Sätze hinaus bis dahin / danach (von) | Wortanfänge eigener Sprüche angeschnitten vorher / nachher | normale Sätze: Wortanfang über 80 ms / Lücken, Ende fehlt |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var z in zeilen.Where(z => z.E.Plan.Gruppe is Gruppe.Echo or Gruppe.Raum or Gruppe.Leise or Gruppe.Spiel or Gruppe.Klemm))
        {
            var seit = z.ErstesRichtig >= 0 ? z.ErstesRichtig - (z.E.Plan.ProblemAbS * 100) : -1;
            md.AppendLine(string.Create(C, $"| {z.E.Plan.Name} | {z.E.Plan.Beschreibung} | {Zeit(seit)} | {z.Fehlurteile} | {z.FremdeVorher} | {z.FremdeNachher} | {(z.Leise == 0 ? "-" : $"{z.LeiseVorher} / {z.LeiseNachher} ({z.Leise})")} | {z.AnfaengeVorher.Ab}/{z.AnfaengeVorher.Woerter} / {z.AnfaengeNachher.Ab}/{z.AnfaengeNachher.Woerter} | {z.Saetze} |"));
        }
        md.AppendLine();
        md.AppendLine("## Empfängerseite");
        md.AppendLine();
        md.AppendLine("| Szene | erkannt nach | Dämpfung am Ende | Fehlalarme an anderen Funkern | eigene Fehlalarme |");
        md.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var z in zeilen.Where(z => z.E.Plan.Gruppe == Gruppe.Empfang))
        {
            var seit = z.EmpfangErkannt >= 0 ? z.EmpfangErkannt - (z.E.Plan.ProblemAbS * 100) : -1;
            var d = z.E.EchoPlatz >= 0 ? z.E.EndDaempfungEcho : z.E.EndDaempfungDauer;
            md.AppendLine(string.Create(C, $"| {z.E.Plan.Name} | {Zeit(seit)} | {d:0.0} dB ({Daempfungsworte.Wort(d)}) | {z.EmpfangFehl} | {z.E.Urteile.Count(u => u.Urteil != DauersendUrteil.Keins)} |"));
        }
        md.AppendLine();
        md.AppendLine("## Kurze Funksprüche (Sarevoc: immer gesendet; am Anfang höchstens 80 ms, nie Ende weg, nie unterbrochen)");
        md.AppendLine();
        md.AppendLine("Je Fall dieselbe Szene auch mit ausgeschaltetem Schalter (ohne Wächter). Verfehlt heißt: nicht hinaus, am Anfang mehr als 80 ms weg, Ende weg oder unterbrochen. Was ohne Wächter ebenso verfehlt wird, liegt an Echounterdrückung oder Rauschfilter; dem Wächter angerechnet wird nur, was er mehr verfehlt.");
        md.AppendLine();
        md.AppendLine("| Fall | Sprüche (davon unter 0,5 s) | hinaus | Anfang fehlt: Mittel / höchstens | mehr als 80 ms | mehr als 30 ms | Ende fehlt | unterbrochen | verfehlt mit / ohne Wächter | Urteile in der Szene |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        var kurzFehler = 0;
        static int Verfehlt(List<WortErgebnis> k) => k.Count(w => !w.Hinaus || w.FehltAnfangMs > 80 || w.EndeFehlt || w.Unterbrochen);
        foreach (var z in zeilen.Where(z => z.E.Plan.Gruppe == Gruppe.Kurz))
        {
            var k = z.Kurze;
            var schlecht = Verfehlt(k);
            var ohne = Verfehlt(Auswerten(ohneWaechter[z.E.Plan.Name]).Kurze);
            kurzFehler += Math.Max(0, schlecht - ohne);
            md.AppendLine(string.Create(C, $"| {z.E.Plan.Name} | {k.Count} ({k.Count(w => w.Teil.Laenge < Rate / 2)}) | {k.Count(w => w.Hinaus)} | {k.Average(w => w.FehltAnfangMs):0} / {k.Max(w => w.FehltAnfangMs)} ms | {k.Count(w => w.FehltAnfangMs > 80)} | {k.Count(w => w.FehltAnfangMs > 30)} | {k.Count(w => w.EndeFehlt)} | {k.Count(w => w.Unterbrochen)} | {schlecht} / {ohne} | {string.Join(", ", z.E.Urteile.Skip(1).Select(u => $"{u.Urteil} ab {u.Rahmen / 100.0:0.0} s"))} |"));
        }
        md.AppendLine();
        md.AppendLine("Laufzeiten je Szene (Sekunden Rechenzeit): " + string.Join(", ", erg.Select(e => string.Create(C, $"{e.Sekunden:0}"))));
        Schreiben("MESSUNG-C2-ABNAHME.md", md);
        var ganz = Environment.GetEnvironmentVariable("FUNK_C2_NUR") is not { Length: > 0 };
        if (ganz && stunden < 10) throw new PruefungException(string.Create(C, $"Nur {stunden:0.0} h Gespräch."));
        if (fehlEigen + fehlEmpf > 0) throw new PruefungException($"Fehlalarme im Gespräch: eigen {fehlEigen}, Empfang {fehlEmpf}");
        if (kurzFehler > 0) throw new PruefungException($"Kurze Sprüche, die der Wächter mehr verfehlt als ohne ihn: {kurzFehler}");
    }

    /// <summary>Diagnose (FUNK_C2_FENSTER=1): lautestes 150-ms-Fenster der Sprachrahmen je eigenem Satz, relativ zur Stimme am Gate (-23 dBFS).</summary>
    private static void FensterAnalyse(Plan p, List<TelemetrieEintrag> t)
    {
        foreach (var schwelle in new[] { -8f, -6f, -5f, -4f, -3f, -2f, -1f, 0f })
        {
            int leiseDurch = 0, leise = 0, normal = 0, normalSpaet = 0;
            double verlust = 0;
            foreach (var teil in p.Eigen)
            {
                var f0 = (teil.Start / R) + 2;
                var f1 = Math.Min(t.Count - 1, (teil.Ende / R) + 2);
                int? erstes = null;
                for (var f = f0 + 1; f <= f1 && erstes is null; f++)
                {
                    double summe = 0;
                    var n = 0;
                    for (var k = Math.Max(f0, f - 14); k <= f; k++)
                    {
                        if (t[k].Sprachwahrscheinlichkeit < 0.35f) continue;
                        summe += Math.Pow(10, t[k].GatePegelDbfs / 10.0);
                        n++;
                    }
                    if (n > 0 && 10 * Math.Log10(summe / n) >= Pegelstufe.ZielDbfs + schwelle) erstes = f;
                }
                if (teil.Gain < 0.9f)
                {
                    leise++;
                    if (erstes is not null) leiseDurch++;
                }
                else
                {
                    normal++;
                    var ms = erstes is { } e ? Math.Max(0, ((e - f0) * 10) - 150) : (f1 - f0) * 10;
                    verlust += ms;
                    if (ms > 80) normalSpaet++;
                }
            }
            Console.WriteLine(string.Create(C, $"  FENSTER {p.Name} Schwelle Stimme{schwelle:+0;-0}: leise durch {leiseDurch}/{leise}, normal Anfang verloren im Mittel {verlust / Math.Max(1, normal):0} ms, über 80 ms {normalSpaet}/{normal}"));
        }
    }

    private static int[] Histogramm(IEnumerable<float> r)
    {
        var h = new int[WaechterKalibrierung.GleichlaufFaecher];
        foreach (var x in r) h[Math.Clamp((int)MathF.Floor((x + 1f) * 10f), 0, h.Length - 1)]++;
        return h;
    }

    /// <summary>ln der Häufigkeiten (Laplace 1), von unten her monoton steigend gemacht (größte Untergrenze), begrenzt auf ±3.</summary>
    private static float[] Punkte(int[] echo, int[] eigen)
    {
        var nE = echo.Sum() + echo.Length;
        var nO = eigen.Sum() + eigen.Length;
        var p = new float[echo.Length];
        for (var i = 0; i < p.Length; i++) p[i] = Math.Clamp((float)Math.Log(((echo[i] + 1.0) / nE) / ((eigen[i] + 1.0) / nO)), -3f, 3f);
        // Monoton: Ein höherer Gleichlauf spricht nie weniger für Echo (Glättung gegen Zufall in dünn besetzten Fächern).
        for (var i = p.Length - 2; i >= 0; i--) p[i] = MathF.Min(p[i], p[i + 1]);
        return p;
    }
}
