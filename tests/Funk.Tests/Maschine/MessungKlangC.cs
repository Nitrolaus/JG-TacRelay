// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Hörproben der Klangstufe C für Sarevocs Ohr (Entwurf KLANGSTUFE-C-ENTWURF.md, Q2, Q14 und Q19), nur von Hand und nur mit
/// FUNK_HOERPROBEN (Ordner unter C:\Claude). Sie prüfen nichts am Programm, sie erzeugen Ton und Zahlen.
/// <para>
/// Satz 1 "Dämpfung": ein Funker spricht Funksprüche (Stefan, Sprechtaste je Spruch), ein zweiter sendet ohne Pause (Katja, Sprechtaste
/// gehalten, durchgehendes Reden aus einem anderen Abschnitt des Korpus). Beide gehen durch die echte Sendekette (Pegelstufe fest auf
/// den Wert, den die Halteregel nach dem Korpus hätte) und werden von einer zweiten Klangmaschine empfangen (Jitterpuffer, Mischer
/// mit Pegel je Funker, Begrenzer, ohne Klicks). Nur der Pegel des Dauersenders im Mischer ändert sich: 0, -6, -10, -20 dB. Die Sende-
/// seite läuft einmal; alle vier Dateien bekommen dieselben Pakete. Darunter liegt leiser Spielton
/// (<see cref="MessungSpielton.Spielton"/>). Gemessen wird jede Stimme für sich durch denselben Empfänger.
/// </para>
/// <para>
/// Satz 2 "Vorpuffer": Funksprüche aus dem Korpus (darunter die zwei kürzesten) durch die echte Sendekette mit Sprachaktivierung und
/// einer Mindest-Funklautstärke von -38 dBFS (<see cref="GateEinstellung.GrenzlinieDb"/>, 15 dB unter dem Ziel -23 dBFS der Pegelstufe),
/// mit 30, 80 und 120 ms Vorlauf des Gates, je mit Rauschfilter Standard und Stark: sechs Dateien A bis F in zufälliger
/// Zuordnung (nur in Aufloesung\Zuordnung.txt) und der Eingang. Gezählt werden Wortanfänge, deren erste 30 ms nicht gesendet wurden
/// (Zählweise von <see cref="Pumpmessung.Auswerten"/>, für beliebigen Vorlauf nachgebaut und am Korpuslauf mit 30 ms gegen sie
/// geprüft), und der Verzug Eingang bis Ausgang je Spruch.
/// </para>
/// <para>
/// Ein Behelf: Der Rückfall von Stark auf Standard bei drei verpassten Fristen (Rechenzeit einer langsamen Testmaschine, nicht die der
/// Funker) wird je Rahmen aufgehoben und gezählt, damit die Datei wirklich Stark ist. Den Vorlauf stellte die Messung bis Klangstufe C1 per
/// Reflection; seitdem über <see cref="GateEinstellung.VorlaufMs"/>, und der Burst ist auf 15 vergangene Rahmen begrenzt (7 PREROLL-Pakete).
/// </para>
/// </summary>
internal static class MessungKlangC
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private const int R = 480;
    private const uint KontoFunkstimme = 101;
    private const uint KontoDauersender = 102;

    /// <summary>Mindest-Funklautstärke für Satz 2: 15 dB unter dem Ziel der Pegelstufe (-23 dBFS).</summary>
    private const float GrenzlinieDb = -38f;

    /// <summary>Leiser Spielton unter den Funkstimmen in Satz 1 (Effektivwert in der Datei).</summary>
    private const float SpieltonDbfs = -40f;

    private static readonly (string Datei, string Name, float Db)[] Stufen =
    [
        ("1-ungedaempft", "ungedämpft (Bezug)", 0f),
        ("2-etwas-leiser", "etwas leiser", -6f),
        ("3-deutlich-leiser", "deutlich leiser", -10f),
        ("4-fast-weg", "fast weg", -20f),
    ];

    private static readonly (int Nr, int PauseVorMs, float AnhebungDb)[] FunkfolgeSatz1 = [(0, 500, 0f), (4, 1000, 0f), (2, 1200, 0f)];

    private static readonly (int Nr, int PauseVorMs, float AnhebungDb)[] DauerfolgeSatz1 = [(8, 0, 0f), (11, 100, 0f), (12, 100, 0f)];

    /// <summary>
    /// Sprüche für Satz 2, je 1,5 s Pause davor (die Sprachaktivierung schließt nach 300 ms Nachlauf, jeder Spruch öffnet sie neu):
    /// Stefan, Katja und Hedda; die zwei kürzesten des Korpus ("Bestätigt." zweimal, "Ende." im Korpus 14 dB leiser als normal, hier
    /// um 14 dB angehoben, sonst bliebe es unter der Mindest-Funklautstärke und fiele in jeder Variante aus), laute, leise (-37 bis
    /// -38 dB) und anschwellende Sprüche.
    /// </summary>
    private static readonly (int Nr, int PauseVorMs, float AnhebungDb)[] FolgeSatz2 =
    [
        (4, 1000, 0f), (2, 1500, 0f), (20, 1500, 14f), (8, 1500, 0f), (17, 1500, 0f), (4, 1500, 0f), (5, 1500, 0f), (1, 1500, 0f),
        (9, 1500, 0f), (14, 1500, 0f), (10, 1500, 0f),
    ];

    private static readonly int[] VorlaufStufen = [30, 80, 120];

    private static readonly int[] KorpusSaaten = [77, 78, 79];


    /// <summary>Ein Spruch auf der Zeitachse eines zusammengesetzten Eingangs (Samples bei 48 kHz).</summary>
    private sealed record Platziert(int Nr, string Stimme, string Text, int Start, int Ende, int[] Woerter, float AnhebungDb);

    private sealed record Eingangssignal(float[] Signal, List<Platziert> Sprueche);

    /// <summary>Was die Sendeseite lieferte, auf die Zeitachse des Signals zurückgesetzt (ohne den Vorlauf mit reinem Rauschen).</summary>
    private sealed record Sendeergebnis(List<Gesendet> Pakete, List<TelemetrieEintrag> Telemetrie, int Takte, int RueckfallAufgehoben, bool StarkAmEnde,
        long Fristen, TelemetrieStand Stand);

    private sealed record Empfang(float[] Links, float SpitzeVorBegrenzerDbfs);

    private sealed record Pegelwerte(double GesamtDbfs, double AktivDbfs, double SpitzeDbfs);

    private sealed record Zaehlung(int Woerter, int Angeschnitten, int Sprueche, int SpruchAngeschnitten, double FehltMittelMs, int FehltMaxMs, int[] SpruchFehltMs,
        int RahmenRekonstruiert);

    /// <summary>Verzug je Spruch in ms am Anfang und am Ende mit der Korrelation im besten Punkt (je Spruch ein Wert).</summary>
    private sealed record Verzug(double[] AnfangMs, double[] EndeMs, double[] KAnfang, double[] KEnde);

    private sealed record HoerLauf(Satz2Variante V, Sendeergebnis Lauf, Zaehlung Z, Verzug Absolut, double PrerollJePhase, float[] Links);

    private sealed record Satz2Variante(Rauschfilter Filter, int VorlaufMs)
    {
        public string Name => string.Create(C, $"Rauschfilter {Filter}, Vorlauf {VorlaufMs} ms");
    }

    private sealed class Waechter
    {
        public int RueckfallAufgehoben;
    }

    [Test]
    public static async Task Messung_KlangC_Hoerproben()
    {
        if (Environment.GetEnvironmentVariable("FUNK_HOERPROBEN") is not { Length: > 0 } ordner)
        {
            Console.WriteLine("HINWEIS Hörproben Klangstufe C übersprungen (FUNK_HOERPROBEN setzen).");
            return;
        }
        var ziel = Path.GetFullPath(ordner.Trim());
        if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_HOERPROBEN muss unter C:\Claude liegen: " + ziel);
        var ordner1 = Path.Combine(ziel, "Satz1-Daempfung");
        var ordner2 = Path.Combine(ziel, "Satz2-Vorpuffer");
        var aufloesung = Path.Combine(ziel, "Aufloesung");
        foreach (var o in new[] { ordner1, ordner2, aufloesung }) Directory.CreateDirectory(o);

        var uhr = Stopwatch.StartNew();
        // Wie in den Hörproben der Klangstufe A: der Wert, bei dem die Halteregel nach einem Lauf über den ganzen Korpus stünde.
        var pegelDb = (await Pumpmessung.Ausfuehren(Variante.Halteregel(), 0f, Pumpmessung.Eingang(0f))).Telemetrie[^1].PegelstufeDb;
        Console.WriteLine(string.Create(C, $"  MESSUNG KlangC Pegelstufe nach dem Korpus (Halteregel warm): {pegelDb:+0.0;-0.0} dB, fest verwendet; {uhr.Elapsed.TotalSeconds:0} s"));

        var md = new StringBuilder();
        md.AppendLine("# Messung zu den Hörproben der Klangstufe C");
        md.AppendLine();
        md.AppendLine(string.Create(C, $"Erzeugt am {DateTime.Now:dd.MM.yyyy HH:mm} von `MessungKlangC.Messung_KlangC_Hoerproben` (tests\\Funk.Tests\\Maschine\\MessungKlangC.cs), Arbeitskopie modul/klang-c. Die Zahlen sind gemessen, nicht bewertet."));
        md.AppendLine();
        md.AppendLine("Gemeinsam für beide Sätze: Prüfstand der Klangmaschine (simulierte Uhr, Geräte-Attrappe, kein echtes Gerät), Mikrofon = Klangkorpus mit 0 dB und Grundrauschen -65 dBFS,");
        md.AppendLine(string.Create(C, $"AEC3 und Hochpass, Pegelstufe fest auf {pegelDb:+0.0;-0.0} dB (der Wert, bei dem die Halteregel nach einem Lauf über den ganzen Korpus stünde, wie in den Hörproben der Klangstufe A), Begrenzer, Opus 48 kbit/s, Empfänger mit Jitterpuffer und Mischer (Panorama Mitte, Klicks aus)."));
        md.AppendLine("Die Dateien sind der linke Kanal des Empfängers (3 dB unter mittig gehört, damit keine Spitze an 0 dBFS stößt), mono, 16 Bit, 48 kHz. Pegel sind Effektivwerte in dBFS;");
        md.AppendLine("\"aktiv\" heißt: nur die 10-ms-Blöcke bis 25 dB unter dem lautesten Block, also ohne Pausen.");
#if DEBUG
        md.AppendLine("Bau: Debug.");
#else
        md.AppendLine("Bau: Release.");
#endif
        md.AppendLine();

        var zuordnung = new List<string> { "Zuordnung der Hörproben Klangstufe C (erst nach dem Hören öffnen)", "" };
        // Zum Entwickeln: FUNK_KLANGC_NUR=1 oder 2 rechnet nur diesen Satz, FUNK_KLANGC_SAATEN=n nur n Rauschmuster im Korpuslauf.
        var nur = Environment.GetEnvironmentVariable("FUNK_KLANGC_NUR");
        var saaten = Environment.GetEnvironmentVariable("FUNK_KLANGC_SAATEN") is { Length: > 0 } sa ? KorpusSaaten.Take(int.Parse(sa, C)).ToArray() : KorpusSaaten;
        if (nur != "2")
        {
            await Satz1(ordner1, pegelDb, md);
            Console.WriteLine(string.Create(C, $"  MESSUNG KlangC Satz 1 fertig nach {uhr.Elapsed.TotalSeconds:0} s"));
        }
        if (nur != "1")
        {
            await Satz2(ordner2, pegelDb, md, zuordnung, saaten);
            Console.WriteLine(string.Create(C, $"  MESSUNG KlangC Satz 2 fertig nach {uhr.Elapsed.TotalSeconds:0} s"));
        }

        await File.WriteAllTextAsync(Path.Combine(ziel, "MESSUNG.md"), md.ToString(), new UTF8Encoding(false));
        await File.WriteAllLinesAsync(Path.Combine(aufloesung, "Zuordnung.txt"), zuordnung);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Satz 1: Dämpfung
    // ---------------------------------------------------------------------------------------------------------------------

    private static async Task Satz1(string ordner, float pegelDb, StringBuilder md)
    {
        const int Laenge = 15500;
        var funk = Zusammensetzen(FunkfolgeSatz1, Laenge, 11);
        var dauer = Zusammensetzen(DauerfolgeSatz1, Laenge, 12);
        var n = funk.Signal.Length;
        var tastenFunk = funk.Sprueche.Select(s => (Von: (s.Start / R) - 5, Bis: (s.Ende / R) + 20)).ToArray();
        (int Von, int Bis)[] tastenDauer = [(0, dauer.Signal.Length / R)];
        var a = await SendenMitTaste(Vorlauf(21), funk.Signal, pegelDb, tastenFunk);
        var b = await SendenMitTaste(Vorlauf(22), dauer.Signal, pegelDb, tastenDauer);
        var takte = (n / R) + 80;
        var ausLaenge = n + 24000;
        var spiel = MessungSpielton.Spielton(ausLaenge, SpieltonDbfs, 5);
        var spielWerte = Messen(spiel);
        var funkLinks = Kuerzen((await Empfangen([(KontoFunkstimme, a.Pakete, 0f)], takte)).Links, ausLaenge);
        var soloFunk = Messen(funkLinks);
        md.AppendLine("## Satz 1: Dämpfung eines Dauersenders (Q2, Q14)");
        md.AppendLine();
        md.AppendLine(string.Create(C, $"Funkstimme: Stefan, {funk.Sprueche.Count} Funksprüche (Nr. {string.Join(", ", funk.Sprueche.Select(s => s.Nr))}), Sprechtaste je Spruch. Dauersender: Katja, zusammenhängendes Reden aus einem anderen Abschnitt des Korpus (Nr. {string.Join(", ", dauer.Sprueche.Select(s => s.Nr))}, Pausen etwa 200 ms), Sprechtaste die ganze Zeit gehalten. Länge je Datei {ausLaenge / 48000.0:0.0} s (Eingang {n / 48000.0:0.0} s, dahinter 0,5 s für den Verzug der Übertragung)."));
        md.AppendLine(string.Create(C, $"Beide durch dieselbe Sendekette (Standard-Rauschfilter, Pegelstufe fest {pegelDb:+0.0;-0.0} dB); die Sendeseite lief einmal, alle vier Dateien bekommen dieselben Pakete. Nur der Pegel des Dauersenders im Mischer (\"Pegel für Funker\") ist verschieden."));
        md.AppendLine(string.Create(C, $"Spielton: Rauschen mit langsamer Hüllkurve und einem Stoß alle 2 s aus dem Prüfstand (`MessungSpielton.Spielton`), Effektivwert {spielWerte.GesamtDbfs:0.0} dBFS in der Datei, hinter dem Empfänger addiert (er läuft nicht durch den Funk-Mischer)."));
        md.AppendLine("Die Pegel der beiden Stimmen sind aus je einem Einzellauf durch denselben Empfänger gemessen (nur diese Stimme, gleiche Einstellung), ohne Spielton.");
        md.AppendLine();
        md.AppendLine("| Datei | Dauersender im Mischer | Funkstimme aktiv | Dauersender aktiv | Dauersender minus Funkstimme (aktiv) | Funkstimme gesamt | Dauersender gesamt | Mischung gesamt | Mischung Spitze | Spitze vor dem Begrenzer | Mischung gegen Summe der Einzelläufe |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var (datei, name, db) in Stufen)
        {
            var dauerLinks = Kuerzen((await Empfangen([(KontoDauersender, b.Pakete, db)], takte)).Links, ausLaenge);
            var soloDauer = Messen(dauerLinks);
            var mischung = await Empfangen([(KontoFunkstimme, a.Pakete, 0f), (KontoDauersender, b.Pakete, db)], takte);
            var links = Kuerzen(mischung.Links, ausLaenge);
            // Summe der beiden Einzelläufe gegen die Mischung: Weicht sie ab, hat der Begrenzer oder das weiche Clipping eingegriffen.
            double abweichung = 0;
            for (var i = 0; i < links.Length; i++) abweichung = Math.Max(abweichung, Math.Abs(links[i] - (funkLinks[i] + dauerLinks[i])));
            var datei1 = new float[links.Length];
            for (var i = 0; i < links.Length; i++) datei1[i] = links[i] + spiel[i];
            var mass = Messen(datei1);
            if (mass.SpitzeDbfs > -1.0) throw new PruefungException(string.Create(C, $"Satz 1 {datei}: Spitze {mass.SpitzeDbfs:0.0} dBFS, zu nah an 0 dBFS."));
            MessungKlangA.WavSchreiben(Path.Combine(ordner, datei + ".wav"), datei1);
            md.AppendLine(string.Create(C,
                $"| {datei}.wav | {name} ({db:+0;-0;0} dB) | {soloFunk.AktivDbfs:0.0} dBFS | {soloDauer.AktivDbfs:0.0} dBFS | {soloDauer.AktivDbfs - soloFunk.AktivDbfs:+0.0;-0.0;0.0} dB | {soloFunk.GesamtDbfs:0.0} dBFS | {soloDauer.GesamtDbfs:0.0} dBFS | {mass.GesamtDbfs:0.0} dBFS | {mass.SpitzeDbfs:0.0} dBFS | {mischung.SpitzeVorBegrenzerDbfs:0.0} dBFS | {(abweichung < 1e-6 ? "keine (die Summe der beiden ist die Mischung)" : "größte Abweichung " + (20 * Math.Log10(abweichung)).ToString("0.0", C) + " dBFS")} |"));
            Console.WriteLine(string.Create(C,
                $"  MESSUNG KlangC Satz 1 {datei}: Funkstimme aktiv {soloFunk.AktivDbfs:0.0}, Dauersender aktiv {soloDauer.AktivDbfs:0.0} dBFS, Mischung gesamt {mass.GesamtDbfs:0.0}, Spitze {mass.SpitzeDbfs:0.0} dBFS"));
        }
        md.AppendLine();
        md.AppendLine(string.Create(C, $"Pakete der Sendeseite: Funkstimme {a.Pakete.Count}, Dauersender {b.Pakete.Count}; Rückfall-Aufhebungen {a.RueckfallAufgehoben + b.RueckfallAufgehoben} (Standard-Rauschfilter, daher keine)."));
        md.AppendLine();
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Satz 2: Vorpuffer
    // ---------------------------------------------------------------------------------------------------------------------

    private static async Task Satz2(string ordner, float pegelDb, StringBuilder md, List<string> zuordnung, int[] saaten)
    {
        var e = Zusammensetzen(FolgeSatz2, 0, 31);
        var n = e.Signal.Length;
        var vorlauf = Vorlauf(32);
        var varianten = new List<Satz2Variante>();
        foreach (var f in new[] { Rauschfilter.Standard, Rauschfilter.Stark })
        {
            foreach (var v in VorlaufStufen) varianten.Add(new Satz2Variante(f, v));
        }

        // Hörproben: ein Lauf je Variante über die Sprüche von Satz 2.
        var hoer = new List<HoerLauf>();
        foreach (var v in varianten)
        {
            var lauf = await SendenMitSprachaktivierung(vorlauf, e.Signal, v.Filter, v.VorlaufMs, pegelDb);
            var z = Zaehlen(lauf.Telemetrie, e.Sprueche, v.Filter, v.VorlaufMs);
            PlausibilitaetPruefen(lauf, z, v);
            var empfang = await Empfangen([(KontoFunkstimme, lauf.Pakete, 0f)], lauf.Takte + 80);
            var links = Kuerzen(empfang.Links, n + 24000);
            var absolut = AbsoluterVerzug(e.Signal, links, e.Sprueche);
            var phasen = lauf.Pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0);
            var preroll = lauf.Pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Preroll) != 0);
            hoer.Add(new HoerLauf(v, lauf, z, absolut, phasen == 0 ? 0 : (double)preroll / phasen, links));
            Console.WriteLine(string.Create(C,
                $"  MESSUNG KlangC Satz 2 {v.Name}: Wortanfänge angeschnitten {z.Angeschnitten}/{z.Woerter}, Spruchanfänge {z.SpruchAngeschnitten}/{z.Sprueche}, Verzug Anfang Median {Median(absolut.AnfangMs):0.0} ms, Rückfall aufgehoben {lauf.RueckfallAufgehoben}"));
        }

        // Blindbuchstaben A bis F in zufälliger Zuordnung.
        var reihenfolge = Enumerable.Range(0, varianten.Count).OrderBy(_ => Random.Shared.Next()).ToArray();
        var buchstabe = new Dictionary<Satz2Variante, char>();
        for (var i = 0; i < reihenfolge.Length; i++) buchstabe[varianten[reihenfolge[i]]] = (char)('A' + i);
        foreach (var h in hoer)
        {
            var mass = Messen(h.Links);
            if (mass.SpitzeDbfs > -1.0) throw new PruefungException(string.Create(C, $"Satz 2 {h.V.Name}: Spitze {mass.SpitzeDbfs:0.0} dBFS, zu nah an 0 dBFS."));
            MessungKlangA.WavSchreiben(Path.Combine(ordner, buchstabe[h.V] + ".wav"), h.Links);
        }
        MessungKlangA.WavSchreiben(Path.Combine(ordner, "Eingang-Bezug.wav"), e.Signal);
        foreach (var kv in buchstabe.OrderBy(k => k.Value))
        {
            zuordnung.Add($"{kv.Value} = {kv.Key.Name}");
        }
        zuordnung.Add("");
        zuordnung.Add("Alle sechs: dieselben Sprüche, dieselbe Sendekette mit Sprachaktivierung (nicht Sprechtaste), Mindest-Funklautstärke (Grenzlinie) -38 dBFS,");
        zuordnung.Add(string.Create(C, $"Pegelstufe fest {pegelDb:+0.0;-0.0} dB, Opus 48 kbit/s, Empfänger mit Jitterpuffer und Mischer. Verschieden sind nur der Rauschfilter (Standard = RNNoise, Stark = DeepFilterNet3) und der Vorlauf des Gates (30, 80, 120 ms)."));
        zuordnung.Add("Eingang-Bezug.wav = was das Mikrofon lieferte (ohne Pegelstufe, Rauschfilter, Gate, Opus), gleiche Zeitachse wie A bis F ohne deren Verzug.");

        // Ganzer Korpus mit drei Rauschmustern: mehr Wortanfänge für die Zählung als die Hörprobe allein.
        var korpus = new List<(Satz2Variante V, int Saat, Zaehlung Z, Sendeergebnis Lauf)>();
        var amGate = new List<(Rauschfilter Filter, double Dbfs)>();
        var korpusSprueche = Klangkorpus.Aeusserungen
            .Select(a => new Platziert(a.Nr, a.Stimme, a.Text, a.Start, a.Ende, [.. a.Woerter], 0f)).ToList();
        foreach (var saat in saaten)
        {
            var eingang = Pumpmessung.Eingang(0f, (ulong)saat);
            foreach (var v in varianten)
            {
                var lauf = await SendenMitSprachaktivierung(Vorlauf((ulong)saat), eingang, v.Filter, v.VorlaufMs, pegelDb);
                var z = Zaehlen(lauf.Telemetrie, korpusSprueche, v.Filter, v.VorlaufMs);
                PlausibilitaetPruefen(lauf, z, v);
                if (v.VorlaufMs == 30)
                {
                    // Gegenprobe der nachgebauten Zählung gegen die vorhandene aus den Messungen der Klangstufen A und B.
                    var k = Pumpmessung.Auswerten(new Pumplauf(Variante.Fest(pegelDb), 0f, eingang, lauf.Telemetrie, lauf.Pakete, null, lauf.Stand),
                        MessungKlangB.VerzugGate(v.Filter), v.VorlaufMs / 10);
                    if (k.AnfaengeAbgeschnitten != z.Angeschnitten || k.Woerter != z.Woerter)
                        throw new PruefungException($"Zählung weicht von Pumpmessung.Auswerten ab ({v.Name}, Saat {saat}): {z.Angeschnitten}/{z.Woerter} gegen {k.AnfaengeAbgeschnitten}/{k.Woerter}");
                    amGate.Add((v.Filter, k.SprachePegelDbfs));
                }
                korpus.Add((v, saat, z, lauf));
                Console.WriteLine(string.Create(C, $"  MESSUNG KlangC Korpus Saat {saat} {v.Name}: angeschnitten {z.Angeschnitten}/{z.Woerter}, Spruchanfänge {z.SpruchAngeschnitten}/{z.Sprueche}"));
            }
        }

        SchreibeSatz2(md, e, varianten, hoer, korpus, saaten, amGate, pegelDb, Messen(e.Signal));
    }

    private static void SchreibeSatz2(StringBuilder md, Eingangssignal e, List<Satz2Variante> varianten,
        List<HoerLauf> hoer,
        List<(Satz2Variante V, int Saat, Zaehlung Z, Sendeergebnis Lauf)> korpus, int[] saaten, List<(Rauschfilter Filter, double Dbfs)> amGate, float pegelDb, Pegelwerte eingangsPegel)
    {
        md.AppendLine("## Satz 2: Vorpuffer des Gates (Q19)");
        md.AppendLine();
        md.AppendLine(string.Create(C, $"Sechs Varianten: Rauschfilter Standard und Stark, Vorlauf des Gates (`SprachGate.VorlaufMs`) 30, 80 und 120 ms. Echte Sendekette mit Sprachaktivierung (Modus Automatisch, Abstand 9 dB, Sprachsicherheit 0,5, Bestätigung 4 aus 10, Nachlauf 300 ms), Mindest-Funklautstärke (Grenzlinie) {GrenzlinieDb:0} dBFS, Pegelstufe fest {pegelDb:+0.0;-0.0} dB. Stark mit der DLL aus native\\out\\win-x64, Rückfall für die Probe aufgehoben (siehe Ende)."));
        md.AppendLine(string.Create(C, $"Die Hörprobe: {e.Sprueche.Count} Sprüche in {e.Signal.Length / 48000.0:0.0} s, je 1,5 s Pause davor (das Gate schließt dazwischen), Eingang {eingangsPegel.AktivDbfs:0.0} dBFS aktiv."));
        md.AppendLine();
        md.AppendLine("| Nr. im Korpus | Stimme | Text | Dauer | Pegel im Korpus | Anhebung hier |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var s in e.Sprueche)
        {
            var a = Klangkorpus.Aeusserungen.Single(x => x.Nr == s.Nr);
            md.AppendLine(string.Create(C, $"| {s.Nr} | {s.Stimme} | {s.Text} | {(s.Ende - s.Start) / 48000.0:0.00} s | {a.ZielDb:0} dB ({a.Huelle}) | {(s.AnhebungDb == 0 ? "keine" : s.AnhebungDb.ToString("+0 dB", C))} |"));
        }
        md.AppendLine();
        md.AppendLine("### Wortanfänge, die nicht ganz gesendet wurden");
        md.AppendLine();
        md.AppendLine("Zählweise (die von `Pumpmessung.Auswerten` in den Messungen der Klangstufen A und B, \"Anfänge ab\"): Ein Wort zählt, wenn von seinen ersten 30 ms (Rahmen am Gate, zeitrichtig um AEC3 und Rauschfilter verschoben) mindestens ein 10-ms-Rahmen nicht gesendet wurde.");
        md.AppendLine("Für 80 und 120 ms Vorlauf nachgebaut (die Rahmen vor dem Anlauf, die das Gate beim Öffnen nachliefert, zählen als gesendet) und beim Korpuslauf mit 30 ms Zahl für Zahl gegen `Pumpmessung.Auswerten` geprüft; zusätzlich stimmen die gesendeten Rahmen mit den Opus-Paketen überein (je Lauf geprüft).");
        md.AppendLine("\"Spruchanfang\" ist das erste Wort eines Spruchs. \"Fehlt\" ist die Zeit vom Wortanfang bis zum ersten gesendeten Rahmen, nur bei den angeschnittenen Wörtern.");
        md.AppendLine();
        md.AppendLine("Hörprobe (die Sprüche oben):");
        md.AppendLine();
        md.AppendLine("| Variante | Wortanfänge angeschnitten | davon Spruchanfänge | fehlt im Mittel | fehlt höchstens | Preroll-Pakete je Sendephase | Pegel der Datei aktiv / Spitze |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var h in hoer)
        {
            var pegel = Messen(h.Links);
            md.AppendLine(string.Create(C, $"| {h.V.Name} | {h.Z.Angeschnitten} von {h.Z.Woerter} | {h.Z.SpruchAngeschnitten} von {h.Z.Sprueche} | {(h.Z.Angeschnitten == 0 ? "-" : h.Z.FehltMittelMs.ToString("0", C) + " ms")} | {(h.Z.Angeschnitten == 0 ? "-" : h.Z.FehltMaxMs + " ms")} | {h.PrerollJePhase:0.0} | {pegel.AktivDbfs:0.0} / {pegel.SpitzeDbfs:0.0} dBFS |"));
        }
        md.AppendLine();
        md.AppendLine("Je Spruch, fehlende Zeit am ersten Wort in ms (0 = der Anfang ist da):");
        md.AppendLine();
        md.AppendLine("| Spruch | " + string.Join(" | ", hoer.Select(h => h.V.Name.Replace("Rauschfilter ", "", StringComparison.Ordinal))) + " |");
        md.AppendLine("| --- |" + string.Concat(hoer.Select(_ => " --- |")));
        for (var i = 0; i < e.Sprueche.Count; i++)
        {
            var s = e.Sprueche[i];
            md.AppendLine(string.Create(C, $"| {i + 1}. Nr. {s.Nr} {Kurz(s.Text)} | ") + string.Join(" | ", hoer.Select(h => h.Z.SpruchFehltMs[i].ToString(C))) + " |");
        }
        md.AppendLine();
        var korpusWoerter = korpus.Where(k => k.V == varianten[0]).Sum(k => k.Z.Woerter);
        var korpusSprueche = korpus.Where(k => k.V == varianten[0]).Sum(k => k.Z.Sprueche);
        md.AppendLine(string.Create(C, $"Ganzer Klangkorpus (22 Sprüche, drei Stimmen, 110 s) mit {saaten.Length} verschiedenen Rauschmustern (Saaten {string.Join(", ", saaten)}), zusammen {korpusWoerter} Wortanfänge und {korpusSprueche} Spruchanfänge je Variante:"));
        md.AppendLine();
        md.AppendLine("| Variante | Wortanfänge angeschnitten | davon Spruchanfänge | fehlt im Mittel | fehlt höchstens | je Saat angeschnitten |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var v in varianten)
        {
            var l = korpus.Where(k => k.V == v).ToList();
            var ab = l.Sum(k => k.Z.Angeschnitten);
            var summeFehlt = l.Sum(k => k.Z.FehltMittelMs * k.Z.Angeschnitten);
            md.AppendLine(string.Create(C,
                $"| {v.Name} | {ab} von {l.Sum(k => k.Z.Woerter)} | {l.Sum(k => k.Z.SpruchAngeschnitten)} von {l.Sum(k => k.Z.Sprueche)} | {(ab == 0 ? "-" : (summeFehlt / ab).ToString("0", C) + " ms")} | {(ab == 0 ? "-" : l.Max(k => k.Z.FehltMaxMs) + " ms")} | {string.Join(" / ", l.Select(k => k.Z.Angeschnitten))} |"));
        }
        md.AppendLine();
        md.AppendLine("### Verzögerung vom Mikrofon bis zum Ausgang");
        md.AppendLine();
        md.AppendLine("Zwei Messungen je Spruch der Hörprobe, jeweils am Anfang des Spruchs (erste 0,3 bis 0,5 s) und an seinem Ende (letzte 0,3 bis 0,5 s); bei kürzeren Sprüchen der ganze Spruch.");
        md.AppendLine("- \"zusätzlich gegen 30 ms\": Die Ausgabe der Variante gegen die Ausgabe mit 30 ms Vorlauf und demselben Rauschfilter, Verschiebung mit der größten normierten Kreuzkorrelation der Wellenform (Suche -50 bis +250 ms, Schritt 1 Sample). Beide Ausgaben haben dieselbe Kette durchlaufen, darum ist die Korrelation hoch und die Differenz genau.");
        md.AppendLine("- \"vom Mikrofon\": Die Ausgabe gegen den Eingang, Verschiebung mit der größten Korrelation der Hüllkurven (Effektivwert über 10 ms, Schritt 1 ms; Suche 0 bis 250 ms). Es ist alles zwischen Mikrofon und Ausgang des Prüfstands darin (AEC3, Rauschfilter, Paketbildung zu 20 ms, Opus, Jitterpuffer des Empfängers). Die Wellenform des Eingangs lässt sich gegen die der Ausgabe nicht sauber korrelieren (Rauschfilter, Opus, Zeitdehnung des Jitterpuffers); die Hüllkurve kommt besser durch.");
        md.AppendLine();
        var zusatz = hoer.ToDictionary(h => h.V, h => ZusatzVerzug(hoer.First(x => x.V.Filter == h.V.Filter && x.V.VorlaufMs == 30).Links, h.Links, e.Sprueche));
        md.AppendLine("| Variante | zusätzlich gegen 30 ms, Anfang des Spruchs | zusätzlich gegen 30 ms, Ende des Spruchs | vom Mikrofon, Anfang des Spruchs | vom Mikrofon, Ende des Spruchs | kleinste Korrelation (zusätzlich / vom Mikrofon) |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var h in hoer)
        {
            var z = zusatz[h.V];
            md.AppendLine(string.Create(C,
                $"| {h.V.Name} | {Spanne(z.AnfangMs)} | {Spanne(z.EndeMs)} | {SpanneAbsolut(h.Absolut.AnfangMs)} | {SpanneAbsolut(h.Absolut.EndeMs)} | {Math.Min(z.KAnfang.Min(), z.KEnde.Min()):0.00} / {Math.Min(h.Absolut.KAnfang.Min(), h.Absolut.KEnde.Min()):0.00} |"));
        }
        md.AppendLine();
        md.AppendLine("(Median, in Klammern kleinster und größter Wert über die Sprüche, in ms.)");
        md.AppendLine();
        md.AppendLine("Je Spruch, zusätzlicher Verzug gegen 30 ms am Anfang des Spruchs in ms (Korrelation in Klammern):");
        md.AppendLine();
        SchreibeJeSpruch(md, e, hoer, h => zusatz[h.V].AnfangMs, h => zusatz[h.V].KAnfang);
        md.AppendLine();
        md.AppendLine("Je Spruch, zusätzlicher Verzug gegen 30 ms am Ende des Spruchs in ms (Korrelation in Klammern):");
        md.AppendLine();
        SchreibeJeSpruch(md, e, hoer, h => zusatz[h.V].EndeMs, h => zusatz[h.V].KEnde);
        md.AppendLine();
        md.AppendLine("Je Spruch, Verzug vom Mikrofon am Anfang des Spruchs in ms (Korrelation der Hüllkurven in Klammern):");
        md.AppendLine();
        SchreibeJeSpruch(md, e, hoer, h => h.Absolut.AnfangMs, h => h.Absolut.KAnfang);
        md.AppendLine();
        md.AppendLine("### Anmerkungen zum Lauf");
        md.AppendLine();
        var starkImmer = hoer.Where(h => h.V.Filter == Rauschfilter.Stark).All(h => h.Lauf.StarkAmEnde) && korpus.Where(k => k.V.Filter == Rauschfilter.Stark).All(k => k.Lauf.StarkAmEnde);
        md.AppendLine(string.Create(C, $"- Pegel der Sprache am Gate (Mittel über die aktiven Rahmen der Äußerungen, Korpuslauf mit Pegelstufe fest {pegelDb:+0.0;-0.0} dB, gemessen mit `Pumpmessung.Auswerten`): Standard {amGate.Where(x => x.Filter == Rauschfilter.Standard).Average(x => x.Dbfs):0.0} dBFS, Stark {amGate.Where(x => x.Filter == Rauschfilter.Stark).Average(x => x.Dbfs):0.0} dBFS. Die Mindest-Funklautstärke liegt bei {GrenzlinieDb:0} dBFS, also so viel unter diesen Werten: Standard {amGate.Where(x => x.Filter == Rauschfilter.Standard).Average(x => x.Dbfs) - GrenzlinieDb:0.0} dB, Stark {amGate.Where(x => x.Filter == Rauschfilter.Stark).Average(x => x.Dbfs) - GrenzlinieDb:0.0} dB."));
        md.AppendLine("- Der Vorlauf des Gates wird über `GateEinstellung.VorlaufMs` gestellt (seit Klangstufe C1; der Burst höchstens 15 vergangene Rahmen). Dass es wirkt, zeigen die Preroll-Pakete je Phase und der Abgleich der gesendeten Rahmen mit den Paketen.");
        md.AppendLine(string.Create(C, $"- Rückfall von Stark auf Standard (drei Rahmen über 10 ms Rechenzeit in einer Sekunde) wurde in den Läufen mit Stark je Rahmen aufgehoben: Hörprobe {string.Join(", ", hoer.Where(h => h.V.Filter == Rauschfilter.Stark).Select(h => h.Lauf.RueckfallAufgehoben))} Aufhebungen (Vorlauf 30, 80, 120 ms), Korpus {korpus.Where(k => k.V.Filter == Rauschfilter.Stark).Sum(k => k.Lauf.RueckfallAufgehoben)}. Stark wirkte am Ende jedes Laufs: {(starkImmer ? "ja" : "nein")}."));
        md.AppendLine(string.Create(C, $"- Verpasste 10-ms-Fristen der Kette (Prüfstand, ohne Last): {string.Join(", ", hoer.Select(h => $"{h.V.Filter} {h.V.VorlaufMs}: {h.Lauf.Fristen}"))}."));
        md.AppendLine("- \"Ende.\" ist im Korpus mit -42 dB leise gesprochen (Funker \"Hedda, leise\"); hier um 14 dB angehoben, damit es als normal gesprochenes kurzes Wort ankommt und nicht unter der Mindest-Funklautstärke ausfällt. Kurze Äußerungen wie \"Hit\" oder \"Check\" hat der Korpus nicht; die kürzesten sind \"Bestätigt.\" (0,86 s) und \"Ende.\" (0,37 s).");
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Eingänge
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sprüche des Korpus hintereinander, vor jedem die angegebene Pause, auf Grundrauschen -65 dBFS (wie <see cref="Pumpmessung.Eingang"/>).
    /// <paramref name="gesamtMs"/> 0: Länge nach dem letzten Spruch plus 2 s.
    /// </summary>
    private static Eingangssignal Zusammensetzen(IReadOnlyList<(int Nr, int PauseVorMs, float AnhebungDb)> folge, int gesamtMs, ulong saat)
    {
        const int Rand = 2400;
        var korpus = Klangkorpus.Signal;
        var teile = new List<(Platziert P, int Von, int Laenge, int Pos)>();
        var pos = 0;
        foreach (var (nr, pauseMs, db) in folge)
        {
            var a = Klangkorpus.Aeusserungen.Single(x => x.Nr == nr);
            pos += pauseMs * 48;
            var von = a.Start - Rand;
            var bis = Math.Min(korpus.Length, a.Ende + Rand);
            var start = pos + Rand;
            var woerter = a.Woerter.Select(w => w - a.Start + start).ToArray();
            teile.Add((new Platziert(a.Nr, a.Stimme, a.Text, start, start + (a.Ende - a.Start), woerter, db), von, bis - von, pos));
            pos += bis - von;
        }
        var n = (gesamtMs == 0 ? pos + (2 * 48000) : gesamtMs * 48) / R * R;
        if (pos > n) throw new InvalidOperationException(string.Create(C, $"Die Sprüche brauchen {pos / 48.0:0} ms, vorgesehen sind {n / 48.0:0} ms."));
        var signal = AnbindungSignale.Rauschen(n, Pumpmessung.GrundrauschenDbfs, saat);
        foreach (var (p, von, laenge, ort) in teile)
        {
            var g = Pegel.DbZuLinear(p.AnhebungDb);
            for (var i = 0; i < laenge; i++) signal[ort + i] += korpus[von + i] * g;
        }
        return new Eingangssignal(signal, [.. teile.Select(t => t.P)]);
    }

    /// <summary>1,5 s Grundrauschen vor dem Signal: Stark rechnet sich darin warm (wie <see cref="MessungKlangB.Vorlauf"/>).</summary>
    private static float[] Vorlauf(ulong saat) => AnbindungSignale.Rauschen(150 * R, Pumpmessung.GrundrauschenDbfs, saat);

    // ---------------------------------------------------------------------------------------------------------------------
    // Sendeseite
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Der Vorlauf des Gates (seit Klangstufe C1 über die Einstellung).</summary>
    private static void VorlaufSetzen(Pruefstand s, int ms) => s.Maschine.Gate = s.Maschine.Gate with { VorlaufMs = ms };

    /// <summary>
    /// Spielt Takte in die Maschine. Hebt den Rückfall der Rechenzeit (Stark auf Standard) vor jedem Rahmen auf und zählt das: Die
    /// Rechenzeit der Testmaschine ist nicht die der Funker, und die Probe soll Stark hören lassen.
    /// </summary>
    private static void Spielen(Pruefstand s, ReadOnlySpan<float> signal, Waechter w, Action<int>? vorTakt = null)
    {
        var kette = s.Maschine.Kette;
        for (int pos = 0, t = 0; pos + R <= signal.Length; pos += R, t++)
        {
            vorTakt?.Invoke(t);
            if (kette.Rueckfall == Sendekette.RueckfallRechenzeit)
            {
                kette.RueckfallSetzen(0);
                w.RueckfallAufgehoben++;
            }
            s.Takt(signal.Slice(pos, R));
        }
    }

    private static Sendeergebnis Ergebnis(Pruefstand s, long basis, Waechter w, Rauschfilter wunsch)
    {
        s.Stille(50);
        var rueckfall = s.Maschine.Kette.Rueckfall;
        if (rueckfall is not 0 and not Sendekette.RueckfallRechenzeit)
            throw new InvalidOperationException($"Rückfall aus anderem Grund als der Rechenzeit: {rueckfall} ({s.Maschine.Rauschfilterstand})");
        var stand = s.Maschine.Telemetrie;
        return new Sendeergebnis([.. s.Pakete.Select(p => p with { Takt = p.Takt - basis })], [.. s.Telemetrie], (int)(s.Takte - basis), w.RueckfallAufgehoben,
            wunsch != Rauschfilter.Stark || s.Maschine.Rauschfilterstand.Wirksam == Rauschfilter.Stark, stand.DeadlineVerpasst, stand);
    }

    /// <summary>Sendeseite mit Sprechtaste: je Tuple von Takt bis Takt gedrückt (Takte des Signals), Pakete auf die Zeitachse des Signals.</summary>
    private static async Task<Sendeergebnis> SendenMitTaste(float[] vorlauf, float[] signal, float pegelDb, (int Von, int Bis)[] tasten)
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, pegelDb);
        s.Maschine.Sprachaktivierung = false;
        await s.StartenAsync();
        var w = new Waechter();
        Spielen(s, vorlauf, w);
        var basis = s.Takte;
        s.Telemetrie.Clear();
        s.Pakete.Clear();
        Spielen(s, signal, w, t =>
        {
            foreach (var (von, bis) in tasten)
            {
                if (t == von) s.Maschine.SprechtasteGedrueckt();
                if (t == bis) s.Maschine.SprechtasteLosgelassen();
            }
        });
        s.Maschine.SprechtasteLosgelassen();
        return Ergebnis(s, basis, w, Rauschfilter.Standard);
    }

    /// <summary>Sendeseite mit Sprachaktivierung, Mindest-Funklautstärke und dem gewählten Vorlauf des Gates.</summary>
    private static async Task<Sendeergebnis> SendenMitSprachaktivierung(float[] vorlauf, float[] signal, Rauschfilter filter, int vorlaufMs, float pegelDb)
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        s.Maschine.Rauschfilter = filter;
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, pegelDb);
        s.Maschine.Gate = GateVorgaben.Standard with { GrenzlinieDb = GrenzlinieDb };
        await s.StartenAsync();
        VorlaufSetzen(s, vorlaufMs);
        var w = new Waechter();
        Spielen(s, vorlauf, w);
        if (s.Maschine.Rauschfilterstand.Wirksam != filter) throw new InvalidOperationException($"{filter} wirkt nach dem Vorlauf nicht ({s.Maschine.Rauschfilterstand})");
        var basis = s.Takte;
        s.Telemetrie.Clear();
        s.Pakete.Clear();
        Spielen(s, signal, w);
        return Ergebnis(s, basis, w, filter);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Empfangsseite
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Pakete mehrerer Sender im Takt ihres Versands an eine zweite Maschine (Pegel je Funker im Mischer, ohne Klicks); der linke Kanal
    /// ihrer Wiedergabe und die größte Spitze der Summe vor dem Begrenzer.
    /// </summary>
    private static async Task<Empfang> Empfangen(IReadOnlyList<(uint Konto, List<Gesendet> Pakete, float PegelDb)> quellen, int takte)
    {
        await using var e = new Pruefstand();
        e.Maschine.Klick = new FunkKlickEinstellungen { EigeneAn = false, AndereAn = false };
        await e.StartenAsync();
        foreach (var q in quellen) e.Maschine.PegelFuerFunker(q.Konto, q.PegelDb);
        e.Mitschreiben = true;
        var naechstes = new int[quellen.Count];
        var phase = new ushort[quellen.Count];
        var frame = new uint[quellen.Count];
        var spitze = 0f;
        for (var t = 1; t <= takte; t++)
        {
            for (var k = 0; k < quellen.Count; k++)
            {
                var (konto, pakete, _) = quellen[k];
                while (naechstes[k] < pakete.Count && pakete[naechstes[k]].Takt <= t)
                {
                    var p = pakete[naechstes[k]++];
                    if ((p.Kennzeichen & Sprachkennzeichen.Beginn) != 0)
                    {
                        phase[k]++;
                        frame[k] = 0;
                    }
                    _ = e.Maschine.SpracheEmpfangen(konto, 0, phase[k], frame[k]++, p.Kennzeichen, p.Opus, e.Uhr.Us);
                }
            }
            e.Stille(1);
            spitze = MathF.Max(spitze, e.Maschine.Mischer.SpitzeVorBegrenzer);
        }
        var stereo = e.Ausgabe;
        var links = new float[stereo.Count / 2];
        for (var i = 0; i < links.Length; i++) links[i] = stereo[2 * i];
        return new Empfang(links, Pegel.LinearZuDb(spitze));
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Messen und Zählen
    // ---------------------------------------------------------------------------------------------------------------------

    private static float[] Kuerzen(float[] x, int laenge) => x.Length <= laenge ? x : x[..laenge];

    private static Pegelwerte Messen(float[] x)
    {
        var bloecke = x.Length / R;
        var e = new double[bloecke];
        double gesamt = 0;
        var spitze = 0f;
        for (var b = 0; b < bloecke; b++)
        {
            double s = 0;
            for (var i = 0; i < R; i++)
            {
                var v = x[(b * R) + i];
                s += (double)v * v;
                spitze = MathF.Max(spitze, MathF.Abs(v));
            }
            e[b] = s / R;
            gesamt += s;
        }
        var schwelle = e.Max() * Math.Pow(10, -2.5);
        var aktiv = e.Where(v => v >= schwelle).DefaultIfEmpty(1e-30).Average();
        return new Pegelwerte(10 * Math.Log10((gesamt / (bloecke * R)) + 1e-30), 10 * Math.Log10(aktiv + 1e-30), 20 * Math.Log10(spitze + 1e-30));
    }

    /// <summary>Welche Rahmen gesendet wurden, aus dem Zustand des Gates: wie <see cref="Pumpmessung.GesendeteRahmen"/>, nur mit beliebigem Vorlauf.</summary>
    private static bool[] GesendeteRahmen(List<TelemetrieEintrag> t, int vorlaufRahmen)
    {
        var gesendet = new bool[t.Count];
        var letzter = -1;
        var anlauf = -1;
        for (var f = 0; f < t.Count; f++)
        {
            var z = t[f].Gate;
            if (z == GateZustand.Anlauf && anlauf < 0) anlauf = f;
            if (z is GateZustand.Senden or GateZustand.Nachlauf)
            {
                if (anlauf >= 0)
                {
                    for (var r = Math.Max(Math.Max(letzter + 1, anlauf - vorlaufRahmen), f - new GateEinstellungen().BurstHoechstRahmen); r < f; r++) gesendet[r] = true;
                }
                gesendet[f] = true;
                letzter = f;
            }
            // Zurückgehalten (Klangstufe C1): der Anlauf zählt weiter, bis die Phase öffnet oder verfällt.
            if (z is not (GateZustand.Anlauf or GateZustand.Zurueckgehalten)) anlauf = -1;
        }
        return gesendet;
    }

    private static Zaehlung Zaehlen(List<TelemetrieEintrag> t, IReadOnlyList<Platziert> sprueche, Rauschfilter filter, int vorlaufMs)
    {
        var verzug = MessungKlangB.VerzugGate(filter);
        var gesendet = GesendeteRahmen(t, vorlaufMs / 10);
        var n = t.Count;
        int woerter = 0, angeschnitten = 0, spruchAngeschnitten = 0, fehltMax = 0;
        double fehltSumme = 0;
        var spruchFehlt = new int[sprueche.Count];
        for (var s = 0; s < sprueche.Count; s++)
        {
            for (var i = 0; i < sprueche[s].Woerter.Length; i++)
            {
                var w = sprueche[s].Woerter[i];
                var f0 = (w + verzug) / R;
                var f1 = (w + verzug + 1440 - 1) / R;
                woerter++;
                var ab = false;
                for (var f = f0; f <= f1; f++) ab |= f >= n || !gesendet[f];
                var fehlt = 0;
                if (f0 < n && !gesendet[f0])
                {
                    var k = f0;
                    while (k < n && !gesendet[k] && k - f0 < 30) k++;
                    fehlt = (k - f0) * 10;
                }
                if (i == 0) spruchFehlt[s] = ab ? Math.Max(fehlt, 10) : 0;
                if (!ab) continue;
                angeschnitten++;
                if (i == 0) spruchAngeschnitten++;
                fehltSumme += fehlt;
                fehltMax = Math.Max(fehltMax, fehlt);
            }
        }
        return new Zaehlung(woerter, angeschnitten, sprueche.Count, spruchAngeschnitten, angeschnitten == 0 ? 0 : fehltSumme / angeschnitten, fehltMax, spruchFehlt, gesendet.Count(x => x));
    }

    /// <summary>
    /// Die gesendeten Rahmen aus der Telemetrie müssen zu den Paketen passen: Jedes Paket trägt zwei Rahmen, eine Phase mit ungerader
    /// Zahl wird aufgefüllt, der letzte Rahmen einer Phase steht schon im Zustand Leise. Und beim Öffnen müssen Preroll-Pakete da sein,
    /// wenn ein Vorlauf eingestellt ist (sonst hätte die Einstellung nicht gewirkt).
    /// </summary>
    private static void PlausibilitaetPruefen(Sendeergebnis lauf, Zaehlung z, Satz2Variante v)
    {
        var audio = lauf.Pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Ende) == 0);
        var phasen = lauf.Pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0);
        if (Math.Abs((2 * audio) - z.RahmenRekonstruiert) > (2 * phasen) + 2)
            throw new PruefungException($"{v.Name}: {z.RahmenRekonstruiert} Rahmen aus dem Gate gegen {2 * audio} aus {audio} Paketen in {phasen} Phasen.");
        if (!lauf.StarkAmEnde) throw new PruefungException($"{v.Name}: Stark wirkte am Ende nicht mehr.");
    }

    private static void SchreibeJeSpruch(StringBuilder md, Eingangssignal e, List<HoerLauf> hoer, Func<HoerLauf, double[]> werte, Func<HoerLauf, double[]> korrelation)
    {
        md.AppendLine("| Spruch | " + string.Join(" | ", hoer.Select(h => h.V.Name.Replace("Rauschfilter ", "", StringComparison.Ordinal))) + " |");
        md.AppendLine("| --- |" + string.Concat(hoer.Select(_ => " --- |")));
        for (var i = 0; i < e.Sprueche.Count; i++)
        {
            md.AppendLine(string.Create(C, $"| {i + 1}. Nr. {e.Sprueche[i].Nr} {Kurz(e.Sprueche[i].Text)} | ") + string.Join(" | ", hoer.Select(h => string.Create(C, $"{werte(h)[i]:0.0} ({korrelation(h)[i]:0.00})"))) + " |");
        }
    }

    /// <summary>Verzug Eingang bis Ausgang je Spruch aus den Hüllkurven (Effektivwert über 10 ms, Schritt 1 ms), am Anfang und am Ende.</summary>
    private static Verzug AbsoluterVerzug(float[] ein, float[] aus, List<Platziert> sprueche)
    {
        var he = Huellkurve(ein);
        var ha = Huellkurve(aus);
        var n = sprueche.Count;
        double[] anfang = new double[n], ende = new double[n], kAnfang = new double[n], kEnde = new double[n];
        for (var s = 0; s < n; s++)
        {
            var start = sprueche[s].Start / 48;
            var stop = sprueche[s].Ende / 48;
            var laenge = Math.Min(500, stop - start);
            (anfang[s], kAnfang[s]) = HuellkurvenVerzug(he, ha, start, laenge, 250);
            (ende[s], kEnde[s]) = HuellkurvenVerzug(he, ha, stop - laenge, laenge, 250);
        }
        return new Verzug(anfang, ende, kAnfang, kEnde);
    }

    private static double[] Huellkurve(float[] x)
    {
        var p = new double[x.Length + 1];
        for (var i = 0; i < x.Length; i++) p[i + 1] = p[i] + ((double)x[i] * x[i]);
        var h = new double[((x.Length - R) / 48) + 1];
        for (var m = 0; m < h.Length; m++) h[m] = Math.Sqrt((p[(m * 48) + R] - p[m * 48]) / R);
        return h;
    }

    /// <summary>Verschiebung in ms (0 bis <paramref name="maxMs"/>) mit der größten Pearson-Korrelation zweier Hüllkurven im Fenster.</summary>
    private static (double Ms, double Korrelation) HuellkurvenVerzug(double[] ein, double[] aus, int startMs, int laengeMs, int maxMs)
    {
        double mittelE = 0;
        for (var i = 0; i < laengeMs; i++) mittelE += ein[startMs + i];
        mittelE /= laengeMs;
        double ee = 0;
        for (var i = 0; i < laengeMs; i++) ee += (ein[startMs + i] - mittelE) * (ein[startMs + i] - mittelE);
        var best = 0;
        var bestK = double.NegativeInfinity;
        for (var d = 0; d <= maxMs && startMs + d + laengeMs <= aus.Length; d++)
        {
            double mittelA = 0;
            for (var i = 0; i < laengeMs; i++) mittelA += aus[startMs + d + i];
            mittelA /= laengeMs;
            double ea = 0, ea2 = 0;
            for (var i = 0; i < laengeMs; i++)
            {
                var a = aus[startMs + d + i] - mittelA;
                ea += (ein[startMs + i] - mittelE) * a;
                ea2 += a * a;
            }
            if (ee <= 0 || ea2 <= 0) continue;
            var k = ea / Math.Sqrt(ee * ea2);
            if (k > bestK)
            {
                bestK = k;
                best = d;
            }
        }
        return (best, bestK);
    }

    /// <summary>
    /// Wie viel später <paramref name="aus"/> gegen <paramref name="bezug"/> liegt (ms, Wellenform, Schritt 1 Sample) in einem Fenster von
    /// 0,3 s am Anfang und am Ende jedes Spruchs; beide Ausgaben haben dieselbe Kette durchlaufen.
    /// </summary>
    private static Verzug ZusatzVerzug(float[] bezug, float[] aus, List<Platziert> sprueche)
    {
        var quadratSumme = new double[aus.Length + 1];
        for (var i = 0; i < aus.Length; i++) quadratSumme[i + 1] = quadratSumme[i] + ((double)aus[i] * aus[i]);
        var n = sprueche.Count;
        double[] anfang = new double[n], ende = new double[n], kAnfang = new double[n], kEnde = new double[n];
        for (var s = 0; s < n; s++)
        {
            // Die Ausgabe liegt etwa 60 bis 130 ms hinter dem Eingang: Das Fenster beginnt dort, wo der Spruch in der Ausgabe etwa beginnt.
            var laenge = Math.Min(14400, sprueche[s].Ende - sprueche[s].Start);
            var (d1, k1) = BesterVerzug(bezug, aus, quadratSumme, sprueche[s].Start + 2880, laenge, -2400, 12000);
            var (d2, k2) = BesterVerzug(bezug, aus, quadratSumme, sprueche[s].Ende - laenge + 2880, laenge, -2400, 12000);
            (anfang[s], kAnfang[s]) = (d1 / 48.0, k1);
            (ende[s], kEnde[s]) = (d2 / 48.0, k2);
        }
        return new Verzug(anfang, ende, kAnfang, kEnde);
    }

    private static (int Verzug, double Korrelation) BesterVerzug(float[] ein, float[] aus, double[] quadratSumme, int start, int laenge, int minVerzug, int maxVerzug)
    {
        var a = ein.AsSpan(start, laenge);
        double aa = 0;
        foreach (var v in a) aa += (double)v * v;
        var best = 0;
        var bestK = double.NegativeInfinity;
        for (var d = minVerzug; d <= maxVerzug && start + d + laenge <= aus.Length; d++)
        {
            if (start + d < 0) continue;
            var bb = quadratSumme[start + d + laenge] - quadratSumme[start + d];
            if (bb <= 0 || aa <= 0) continue;
            var k = Punkt(a, aus.AsSpan(start + d, laenge)) / Math.Sqrt(aa * bb);
            if (k > bestK)
            {
                bestK = k;
                best = d;
            }
        }
        return (best, bestK);
    }

    private static double Punkt(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var acc = Vector<float>.Zero;
        var i = 0;
        for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count) acc += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
        double s = Vector.Sum(acc);
        for (; i < a.Length; i++) s += (double)a[i] * b[i];
        return s;
    }

    private static double Median(double[] x)
    {
        var s = x.Order().ToArray();
        return s.Length % 2 == 1 ? s[s.Length / 2] : (s[(s.Length / 2) - 1] + s[s.Length / 2]) / 2;
    }

    /// <summary>Median und in Klammern kleinster und größter Wert in ms.</summary>
    private static string SpanneAbsolut(double[] ms) => string.Create(C, $"{Median(ms):0.0} ({ms.Min():0.0} bis {ms.Max():0.0})");

    /// <summary>Wie <see cref="SpanneAbsolut"/>, mit Vorzeichen (Unterschiede).</summary>
    private static string Spanne(double[] ms) => string.Create(C, $"{Median(ms):+0.0;-0.0;0.0} ({ms.Min():+0.0;-0.0;0.0} bis {ms.Max():+0.0;-0.0;0.0})");

    private static string Kurz(string text) => text.Length <= 28 ? text : text[..25] + "...";
}
