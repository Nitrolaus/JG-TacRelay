// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Messungen der Klangstufe A, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie geben die Zahlen für die Wahl der Pegelregelung
/// aus (<see cref="Pumpmessung"/>): den Klangkorpus mit drei Mikrofonpegeln durch die alte Kette (AGC2 im APM, 30 dB), die AGC2 mit
/// den Startwerten der Recherche, soweit die C-Schicht sie einstellen lässt (höchstens 12 und 18 dB; Headroom und Änderungsrate
/// stellt apm_c 1.1.0 nicht ein), die Festverstärkung nach dem Einmessen und die Halteregel kalt und warm; dazu die Stelle der
/// Verstärkung (vor oder hinter RNNoise) und den eigenen Druck-Klick über Lautsprecher. Hörproben nur mit FUNK_HOERPROBEN.
/// </summary>
internal static class MessungKlangA
{
    private static readonly float[] Mikrofone = [0f, -15f, -25f];

    private static bool Aus()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") == "1") return false;
        Console.WriteLine("HINWEIS Messung Klangstufe A übersprungen (FUNK_MESSUNG=1 setzen).");
        return true;
    }

    [Test]
    public static async Task Messung_Pumpen()
    {
        if (Aus()) return;
        var zeilen = new List<(float Mikrofon, int Nr, string Zeile)>();
        var laeufe = new List<Task>();
        foreach (var mikrofon in Mikrofone)
        {
            var eingang = Pumpmessung.Eingang(mikrofon);
            var fest = await Einmessen(eingang);
            // Warm: so, wie sie nach einer früheren Sitzung mit dieser Stimme beginnt (Ende eines ersten Laufs).
            var warm = (await Pumpmessung.Ausfuehren(Variante.Halteregel(), mikrofon, eingang)).Telemetrie[^1].PegelstufeDb;
            var varianten = new[] { Variante.Alt, Variante.Agc2(18), Variante.Agc2(12), Variante.Fest(fest), Variante.Halteregel(), Variante.Halteregel(warm, gelernt: true) };
            for (var i = 0; i < varianten.Length; i++)
            {
                var (v, nr) = (varianten[i], i);
                laeufe.Add(Task.Run(async () =>
                {
                    var lauf = await Pumpmessung.Ausfuehren(v, mikrofon, eingang);
                    var k = Pumpmessung.Auswerten(lauf);
                    var zeile = Pumpmessung.Zeile(lauf, k) + " | je Äußerung " + string.Join(" ", k.PGainJeAeusserung.Select(x => x.ToString("0.0", CultureInfo.InvariantCulture)));
                    lock (zeilen) zeilen.Add((mikrofon, nr, zeile));
                }));
            }
        }
        await Task.WhenAll(laeufe);
        foreach (var z in zeilen.OrderByDescending(z => z.Mikrofon).ThenBy(z => z.Nr)) Console.WriteLine("  MESSUNG " + z.Zeile);
    }

    /// <summary>
    /// Reihenfolge (Recherche Abschnitt 1): Macht es für RNNoise und das Gate einen Unterschied, wo dieselbe Verstärkung liegt? Im
    /// Mikrofon: Sprache 0 dB mit Rauschen -65 dBFS, fest 0 dB. Sonst alles 15 bzw. 25 dB leiser ins Mikrofon (Sprache und
    /// Rauschen) und fest +15 bzw. +25 dB, einmal vor RNNoise (wo die AGC2 saß, die gewählte Stelle) und einmal dahinter (wie die
    /// Recherche vorschlägt). Am Gate liegt jedes Mal derselbe Pegel; ein Unterschied bei Sprachsicherheit, Anlauten oder
    /// Fehlauslösungen käme aus der Stelle der Verstärkung.
    /// </summary>
    [Test]
    public static async Task Messung_Reihenfolge()
    {
        if (Aus()) return;
        var mikrofon = await Pumpmessung.Ausfuehren(Variante.Fest(0f) with { Name = "im Mikrofon" }, 0f, Pumpmessung.Eingang(0f));
        Console.WriteLine("  MESSUNG Reihenfolge " + Pumpmessung.Zeile(mikrofon, Pumpmessung.Auswerten(mikrofon)));
        foreach (var db in new[] { 15f, 25f })
        {
            var eingang = Pumpmessung.Eingang(-db, rauschenDbfs: Pumpmessung.GrundrauschenDbfs - db);
            var vor = await Pumpmessung.Ausfuehren(Variante.Fest(db) with { Name = $"vor RNNoise +{db:0} dB" }, -db, eingang);
            var hinter = await Pumpmessung.Ausfuehren(Variante.Fest(db) with { Name = $"hinter RNNoise +{db:0} dB", HinterRnnoise = true }, -db, eingang);
            foreach (var l in new[] { vor, hinter }) Console.WriteLine("  MESSUNG Reihenfolge " + Pumpmessung.Zeile(l, Pumpmessung.Auswerten(l)));
        }
    }

    /// <summary>
    /// Antwort nach Funk und Ins-Wort-Fallen (<see cref="MessungAntwortUndKlick.Szene"/>, alle Echowege und Abstände) für die alte
    /// Kette (AGC2 im APM) und die neue (Pegelstufe, Halteregel ab Werk) über zehn Saaten statt drei. Die Zahl verpasster Anlaute
    /// hängt an einzelnen knappen Entscheidungen des Gates; mit drei Saaten lagen gleichwertige Fassungen der neuen Kette (Ziel -20
    /// oder -23 dBFS, fest 0 dB) bei 257 bis 267 von 1128, darum mehr Saaten für den Vergleich.
    /// </summary>
    [Test]
    public static async Task Messung_AntwortAltNeu()
    {
        if (Aus()) return;
        var saaten = Enumerable.Range(0, 10).Select(i => 151 + (10 * i)).ToArray();
        var ketten = new (string Name, Func<Pruefstand> Stand)[]
        {
            ("alt", () => new Pruefstand(mitPegelstufe: false, apm: Klang.Kern.ApmEinstellungen.Sprechfunk)), ("neu", () => new Pruefstand()),
        };
        var szenen = new (string Name, int[] Abstaende)[] { ("Antwort", [150, 300, 450, 600, 900]), ("InsWort", [-600, -300]) };
        using var plaetze = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 2));
        var laeufe = new List<(string Kette, string Szene, string Pfad, Task<MessungAntwortUndKlick.Ergebnis> Lauf)>();
        foreach (var (kette, stand) in ketten)
        {
            foreach (var (szene, abstaende) in szenen)
            {
                foreach (var (pfadName, pfad) in MessungAntwortUndKlick.Pfade)
                {
                    foreach (var abstand in abstaende)
                    {
                        foreach (var saat in saaten)
                        {
                            laeufe.Add((kette, szene, pfadName, Task.Run(async () =>
                            {
                                await plaetze.WaitAsync();
                                try { return await MessungAntwortUndKlick.Szene(pfad, abstand, saat, stand); }
                                finally { plaetze.Release(); }
                            })));
                        }
                    }
                }
            }
        }
        await Task.WhenAll(laeufe.Select(l => l.Lauf));
        foreach (var (szene, _) in szenen)
        {
            foreach (var (kette, _) in ketten)
            {
                var teile = MessungAntwortUndKlick.Pfade.Select(p =>
                {
                    var e = laeufe.Where(l => l.Kette == kette && l.Szene == szene && l.Pfad == p.Name).Select(l => l.Lauf.Result).ToList();
                    return $"{p.Name} {e.Sum(x => x.Verpasst)}/{e.Sum(x => x.Gesamt)}";
                });
                var alle = laeufe.Where(l => l.Kette == kette && l.Szene == szene).Select(l => l.Lauf.Result).ToList();
                Console.WriteLine($"  MESSUNG AltNeu {szene} {kette}: verpasst {alle.Sum(x => x.Verpasst)}/{alle.Sum(x => x.Gesamt)}, Fehlphasen "
                    + $"{alle.Sum(x => x.Fehl)} ({string.Join(" | ", teile)})");
            }
        }
    }

    /// <summary>
    /// Der eigene Druck-Klick mit Lautsprechern (Befund der Gegenprüfung 2c): Taste drücken, nach 0, 50, 100 oder 200 ms sprechen.
    /// Pegel der ersten 100 ms jedes Spruchs im gesendeten Signal gegen denselben Ablauf ohne eigenen Klick (Unterschied in dB,
    /// Mittel und schlechtester von 2 Saaten zu je 5 Sprüchen), und was vom Klick vor dem Spruch mitgesendet wird. Wege: Klick in der
    /// Referenz (bis 2c), Klick aus der Referenz genommen (Klangstufe A), Klick leiser (-30 dB statt -12 dB).
    /// </summary>
    [Test]
    public static async Task Messung_KlickImLautsprecher()
    {
        if (Aus()) return;
        var pfade = new (string Name, Func<float, float>? Pfad)[]
        {
            ("kein Echo", null), ("20 dB", x => 0.1f * x), ("12 dB", KlangMaschineTests.Kopplung12Db), ("verzerrt", KlangMaschineTests.KopplungVerzerrt),
        };
        var wege = new (string Name, bool Ausblenden, float Db)[] { ("in der Referenz (bis 2c)", false, -12f), ("aus der Referenz (neu)", true, -12f), ("leiser, -30 dB", false, -30f) };
        foreach (var (name, pfad) in pfade)
        {
            foreach (var reaktion in new[] { 0, 50, 100, 200 })
            {
                var teile = new List<string>();
                var bezug = new List<double>();
                foreach (var saat in new[] { 5, 10 }) bezug.AddRange((await KlickLautsprecherTests.Ablauf(saat, false, true, pfad, reaktion)).Anfaenge);
                foreach (var weg in wege)
                {
                    var d = new List<double>();
                    var rest = new List<double>();
                    for (var i = 0; i < 2; i++)
                    {
                        var saat = i == 0 ? 5 : 10;
                        var l = await KlickLautsprecherTests.Ablauf(saat, true, weg.Ausblenden, pfad, reaktion, eigeneDb: weg.Db);
                        for (var k = 0; k < l.Anfaenge.Length; k++) d.Add(l.Anfaenge[k] - bezug[i * 5 + k]);
                        rest.AddRange(l.Klickrest.Where(double.IsFinite));
                    }
                    teile.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{weg.Name}: Mittel {d.Average():+0.0;-0.0;0.0} schlechtester {d.Min():+0.0;-0.0;0.0} dB{(rest.Count > 0 ? $", vor dem Spruch {rest.Max():0.0} dBFS" : "")}"));
                }
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  MESSUNG Klick {name}, Sprechen {reaktion} ms nach dem Druck (ohne Klick {bezug.Average():0.0} dBFS): ") + string.Join(" | ", teile));
            }
        }
    }

    /// <summary>
    /// Hörproben für Sarevocs Blindvergleich, nur mit FUNK_HOERPROBEN (Ordner unter C:\Claude): der Klangkorpus durch die alte Kette (AGC2
    /// im APM) und die neue (Halteregel ab dem Wert, den sie in einer früheren Sitzung gelernt hätte), gesendet und von einer zweiten
    /// Klangmaschine empfangen, so wie die anderen es hören (Jitterpuffer, Mischer, ohne Klicks). Je Mikrofonpegel ein Paar A/B bzw.
    /// C/D in zufälliger Zuordnung, die nur in Aufloesung\Zuordnung.txt steht. Dazu der Eingang und die Laufzeit Eingang bis Empfang
    /// (Kreuzkorrelation), um zu zeigen, dass die neue Kette keine Latenz hinzufügt.
    /// </summary>
    [Test]
    public static async Task Messung_Hoerproben()
    {
        if (Environment.GetEnvironmentVariable("FUNK_HOERPROBEN") is not { Length: > 0 } ordner)
        {
            Console.WriteLine("HINWEIS Hörproben Klangstufe A übersprungen (FUNK_HOERPROBEN setzen).");
            return;
        }
        var ziel = Path.GetFullPath(ordner.Trim());
        if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_HOERPROBEN muss unter C:\Claude liegen: " + ziel);
        Directory.CreateDirectory(Path.Combine(ziel, "Aufloesung"));
        var zuordnung = new List<string> { "Zuordnung der Hörproben (erst nach dem Hören öffnen)", "" };
        var paare = new (float Mikrofon, string Erster, string Zweiter)[] { (0f, "A", "B"), (-15f, "C", "D") };
        foreach (var (mikrofon, erster, zweiter) in paare)
        {
            var eingang = Pumpmessung.Eingang(mikrofon);
            WavSchreiben(Path.Combine(ziel, string.Create(CultureInfo.InvariantCulture, $"Eingang-Mikrofon{mikrofon:+0;-0}dB.wav")), eingang);
            var warm = (await Pumpmessung.Ausfuehren(Variante.Halteregel(), mikrofon, eingang)).Telemetrie[^1].PegelstufeDb;
            var alt = await Empfangen(Variante.Alt, eingang);
            var neu = await Empfangen(Variante.Halteregel(warm, gelernt: true), eingang);
            var tausch = Random.Shared.Next(2) == 1;
            WavSchreiben(Path.Combine(ziel, erster + ".wav"), tausch ? neu : alt);
            WavSchreiben(Path.Combine(ziel, zweiter + ".wav"), tausch ? alt : neu);
            var altVerzug = Verzug(eingang, alt);
            var neuVerzug = Verzug(eingang, neu);
            zuordnung.Add(string.Create(CultureInfo.InvariantCulture,
                $"Mikrofon {mikrofon:+0;-0} dB: {erster} = {(tausch ? "neu" : "alt")}, {zweiter} = {(tausch ? "alt" : "neu")}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  MESSUNG Hörprobe Mikrofon {mikrofon:+0;-0} dB: Laufzeit Eingang bis Empfang alt {altVerzug} Samples, neu {neuVerzug} Samples; neu ab {warm:+0.0;-0.0} dB"));
        }
        // Eigener Druck-Klick über Lautsprecher (12 dB): fünf Sätze des Korpus, jeweils 50 ms nach dem Tastendruck begonnen.
        var klickNeu = await EmpfangenMitKlick(ausblenden: true);
        var klickAlt = await EmpfangenMitKlick(ausblenden: false);
        var klickTausch = Random.Shared.Next(2) == 1;
        WavSchreiben(Path.Combine(ziel, "E.wav"), klickTausch ? klickNeu : klickAlt);
        WavSchreiben(Path.Combine(ziel, "F.wav"), klickTausch ? klickAlt : klickNeu);
        zuordnung.Add($"Druck-Klick über Lautsprecher: E = {(klickTausch ? "neu" : "alt")}, F = {(klickTausch ? "alt" : "neu")}");
        zuordnung.Add("");
        zuordnung.Add("A bis D: alt = Kette bis 0.6.0 Stufe 2c (AGC2 im APM, höchstens 30 dB, regelt mit bis zu 6 dB/s nach); neu = Klangstufe A");
        zuordnung.Add("(Halteregel: ändert die Lautstärke nur nach 600 ms Pause, höchstens 1 dB je Pause). A/B Mikrofon normal, C/D 15 dB leiser.");
        zuordnung.Add("E/F: alt = eigener Druck-Klick in der Echoreferenz (AEC3 dämpft den Anlaut); neu = Klick aus der Referenz genommen.");
        await File.WriteAllLinesAsync(Path.Combine(ziel, "Aufloesung", "Zuordnung.txt"), zuordnung);
    }

    /// <summary>
    /// Sprechtaste über Lautsprecher (12 dB, 40 ms): fünf Sätze des Korpus, je 50 ms nach dem Druck begonnen, gesendet und von einer
    /// zweiten Maschine empfangen (ohne Klicks beim Empfänger). <paramref name="ausblenden"/>: der eigene Druck-Klick aus der Referenz.
    /// </summary>
    private static async Task<float[]> EmpfangenMitKlick(bool ausblenden)
    {
        await using var sender = new Pruefstand(klickStartwert: 5);
        sender.Maschine.Echounterdrueckung = true;
        sender.Maschine.Sprachaktivierung = false;
        sender.Maschine.Kette.EigenenKlickAusblenden = ausblenden;
        await sender.StartenAsync();
        var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
        sender.Echo = (w, mic) =>
        {
            for (var i = 0; i < mic.Length; i++)
            {
                linie.Enqueue(KlangMaschineTests.Kopplung12Db(0.5f * (w[2 * i] + w[2 * i + 1])));
                mic[i] += linie.Dequeue();
            }
        };
        var eingang = Pumpmessung.Eingang(0f);
        var grund = AnbindungSignale.Rauschen(480 * 150, Pumpmessung.GrundrauschenDbfs, 7);
        sender.Abspielen(grund);
        foreach (var a in Klangkorpus.Aeusserungen.Take(5))
        {
            sender.Maschine.SprechtasteGedrueckt();
            sender.Abspielen(grund.AsSpan(0, 480 * 5));
            sender.Abspielen(eingang.AsSpan(a.Start, (a.Ende - a.Start) / 480 * 480));
            sender.Abspielen(grund.AsSpan(0, 480 * 20));
            sender.Maschine.SprechtasteLosgelassen();
            sender.Abspielen(grund);
        }
        return await Empfaenger(sender.Pakete, (int)sender.Takte + 80);
    }

    /// <summary>Korpus durch die Kette, gesendet und von einer zweiten Maschine empfangen (ohne Klicks); mono wie mittig gehört.</summary>
    private static async Task<float[]> Empfangen(Variante v, float[] eingang)
    {
        await using var sender = new Pruefstand(mitPegelstufe: v.MitPegelstufe, apm: v.Apm);
        if (v.MitPegelstufe) sender.Maschine.Lautstaerke = v.Lautstaerke;
        await sender.StartenAsync();
        sender.Abspielen(eingang);
        sender.Stille(50);
        return await Empfaenger(sender.Pakete, eingang.Length / 480 + 80);
    }

    /// <summary>Gesendete Pakete im Takt ihres Versands an eine zweite Maschine (ohne Klicks); ihre Wiedergabe mono wie mittig gehört.</summary>
    internal static async Task<float[]> Empfaenger(List<Gesendet> pakete, int takte)
    {
        await using var empfaenger = new Pruefstand();
        empfaenger.Maschine.Klick = new FunkKlickEinstellungen { EigeneAn = false, AndereAn = false };
        await empfaenger.StartenAsync();
        empfaenger.Mitschreiben = true;
        var naechstes = 0;
        ushort phase = 0;
        uint frame = 0;
        for (var t = 1; t <= takte; t++)
        {
            while (naechstes < pakete.Count && pakete[naechstes].Takt <= t)
            {
                var p = pakete[naechstes++];
                if ((p.Kennzeichen & Sprachkennzeichen.Beginn) != 0)
                {
                    phase++;
                    frame = 0;
                }
                _ = empfaenger.Maschine.SpracheEmpfangen(77, 0, phase, frame++, p.Kennzeichen, p.Opus, empfaenger.Uhr.Us);
            }
            empfaenger.Stille(1);
        }
        var stereo = empfaenger.Ausgabe;
        var mono = new float[stereo.Count / 2];
        for (var i = 0; i < mono.Length; i++) mono[i] = stereo[2 * i] * 1.41421356f;
        return mono;
    }

    /// <summary>
    /// Verzug von <paramref name="aus"/> gegen <paramref name="ein"/> in Samples (größte Korrelation, 0 bis 250 ms), über die ersten 2 s
    /// des ersten Satzes (eine längere Strecke kostete Minuten).
    /// </summary>
    private static int Verzug(float[] ein, float[] aus) =>
        AnbindungSignale.BesterVerzug(ein, aus, Klangkorpus.Aeusserungen[0].Start, 2 * 48000, 12000).Verzug;

    internal static void WavSchreiben(string pfad, float[] mono)
    {
        using var w = new BinaryWriter(File.Create(pfad));
        w.Write("RIFF"u8);
        w.Write(36 + 2 * mono.Length);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(48000);
        w.Write(96000);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(2 * mono.Length);
        foreach (var v in mono) w.Write((short)Math.Clamp(MathF.Round(v * 32767f), -32768f, 32767f));
    }

    /// <summary>
    /// Einmessen wie in der Oberfläche verlangt, normal und einmal deutlich lauter: Äußerung 0 (normal) und Äußerung 2 (laut) des
    /// Korpus hintereinander in die Maschine, 8 s lang; Ergebnis in dB.
    /// </summary>
    internal static async Task<float> Einmessen(float[] eingang)
    {
        var a = Klangkorpus.Aeusserungen;
        var probe = new float[Pegelstufe.EinmessRahmen * 480];
        var normal = eingang.AsSpan(a[0].Start - 9600, a[0].Ende - a[0].Start + 9600);
        normal.CopyTo(probe);
        eingang.AsSpan(a[2].Start - 9600, Math.Min(a[2].Ende - a[2].Start + 19200, probe.Length - normal.Length)).CopyTo(probe.AsSpan(normal.Length));
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Abspielen(eingang.AsSpan(0, 48000));
        s.Maschine.EinmessenStarten();
        s.Abspielen(probe);
        s.Stille(2);
        var e = s.Maschine.Einmessen;
        return e.Zustand == EinmessZustand.Fertig ? e.ErgebnisDb : throw new InvalidOperationException("Einmessen: " + e.Zustand);
    }
}
