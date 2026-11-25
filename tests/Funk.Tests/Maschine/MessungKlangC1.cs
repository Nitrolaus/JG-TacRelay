// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Messung für die Mindest-Funklautstärke ab Werk (Klangstufe C1, nur mit FUNK_MESSUNG=1): Wie weit unter der gelernten Funkstimme darf die
/// Untergrenze liegen, damit normales Sprechen keine zusätzlichen Wortanfänge verliert, und Sprache 12 dB unter der Funkstimme nicht mehr
/// hinausgeht? Der Klangkorpus (22 Äußerungen, drei Stimmen, 190 Wortanfänge) durch die echte Sendekette mit Sprachaktivierung (Automatisch,
/// Vorlauf 120 ms), Pegelstufe fest auf dem Wert, den die Halteregel nach dem Korpus hätte, als gelernt (die Stimme liegt damit am Ziel
/// -23 dBFS); einmal mit 0 dB, einmal 12 dB leiser, je drei Rauschmuster. Gezählt wie <see cref="Pumpmessung.Auswerten"/>: ein Wortanfang ist
/// angeschnitten, wenn eine seiner ersten 30 ms nicht hinausging. Getrennt nach den leisen Äußerungen des Korpus (Hülle "leise", 11 bis 16 dB
/// unter "normal") und allen übrigen. Ergebnis in der Konsole und mit FUNK_MESSUNG_ORDNER (unter C:\Claude) als MESSUNG-C1.md.
/// </summary>
internal static class MessungKlangC1
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private static readonly float[] Abstaende = [GateVorgaben.MaxMindestAbstandDb, 30f, 25f, 20f, 15f, 12f, 10f, 9f];

    private static readonly ulong[] Saaten = [77, 78, 79];

    /// <param name="Cut80">Wortanfänge normaler Äußerungen, denen mehr als 80 ms fehlen (Sarevoc: am Anfang höchstens 80 ms; Gegenprüfung C1, M3).</param>
    /// <param name="Luecken">Angeschnittene Wörter nach dem ersten gesendeten Rahmen der Äußerung: eine Lücke mitten im Spruch.</param>
    /// <param name="EndeFehlt">Normale Äußerungen, deren letzte 150 ms nicht ganz gesendet wurden.</param>
    private sealed record Zaehlung(int WoerterNormal, int CutNormal, int WoerterLeise, int CutLeise, int Rahmen, int Gesendet, int Phasen, int CutAnschwellend = 0,
        int Cut80 = 0, int Luecken = 0, int EndeFehlt = 0, int Aeusserungen = 0);

    [Test]
    public static async Task Messung_KlangC1_Mindestabstand()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") != "1")
        {
            Console.WriteLine("HINWEIS Messung Klangstufe C1 übersprungen (FUNK_MESSUNG=1 setzen).");
            return;
        }
        // Verstärkung, mit der die Stimme des Korpus genau am Ziel ankommt: aus dem Grundpegel, den die Halteregel über den Korpus lernt.
        float g;
        await using (var lern = new Pruefstand())
        {
            await lern.StartenAsync();
            lern.Abspielen(Pumpmessung.Eingang(0f));
            g = Pegelstufe.ZielDbfs - lern.Maschine.Kette.Pegelstufe!.GrundpegelDbfs;
        }
        Console.WriteLine(string.Create(C, $"  MESSUNG C1 Verstärkung für die Stimme am Ziel: {g:+0.0;-0.0} dB, fest und gelernt verwendet"));
        var md = new StringBuilder();
        md.AppendLine("# Messung Mindest-Funklautstärke (Klangstufe C1)");
        md.AppendLine();
        md.AppendLine(string.Create(C, $"Erzeugt am {DateTime.Now:dd.MM.yyyy HH:mm} von `MessungKlangC1.Messung_KlangC1_Mindestabstand`. Pegelstufe fest {g:+0.0;-0.0} dB als gelernt (Stimme am Ziel -23 dBFS), Automatisch, Vorlauf 120 ms, Rauschfilter Standard, Saaten {string.Join("/", Saaten)}."));
        md.AppendLine();
        md.AppendLine("| Abstand unter der Stimme | 0 dB: Wortanfänge angeschnitten, normale Äußerungen | davon anschwellende Sätze | 0 dB: mehr als 80 ms fehlend / Lücken / Ende fehlt | 0 dB: leise Äußerungen | -12 dB: Wortanfänge hinaus, normale Äußerungen | -12 dB: Rahmen der Äußerungen gesendet | -12 dB: Phasen |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var abstand in Abstaende)
        {
            var normal = new List<Zaehlung>();
            var leise = new List<Zaehlung>();
            var welche = new List<string>();
            foreach (var saat in Saaten)
            {
                normal.Add(Zaehlen(await Lauf(0f, saat, g, abstand), welche));
                leise.Add(Zaehlen(await Lauf(-12f, saat, g, abstand)));
            }
            var name = abstand >= GateVorgaben.MaxMindestAbstandDb ? "ohne" : string.Create(C, $"{abstand:0} dB");
            var n = Summe(normal);
            var l = Summe(leise);
            var zeile = string.Create(C,
                $"| {name} | {n.CutNormal}/{n.WoerterNormal} | {n.CutAnschwellend} | {n.Cut80} / {n.Luecken} / {n.EndeFehlt} von {n.Aeusserungen} | {n.CutLeise}/{n.WoerterLeise} | {l.WoerterNormal - l.CutNormal}/{l.WoerterNormal} | {100.0 * l.Gesendet / l.Rahmen:0.0} % | {l.Phasen} |");
            md.AppendLine(zeile);
            Console.WriteLine("  MESSUNG C1 " + zeile);
            if (welche.Count > 0) Console.WriteLine("  MESSUNG C1   angeschnitten bei " + name + ": " + string.Join("; ", welche.GroupBy(w => w).Select(x => $"{x.Key} x{x.Count()}")));
        }
        md.AppendLine();
        md.AppendLine("Spalten: angeschnitten heißt, mindestens eine der ersten 30 ms des Wortes ging nicht hinaus (Summe über drei Rauschmuster). \"Wortanfänge hinaus\" bei -12 dB: so viele Wörter normaler Äußerungen kamen mit ihrem Anfang beim Empfänger an, obwohl sie 12 dB unter der Funkstimme lagen. Rahmen der Äußerungen: Anteil der 10-ms-Rahmen innerhalb der Äußerungen, die gesendet wurden.");
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG_ORDNER") is { Length: > 0 } ordner)
        {
            var ziel = Path.GetFullPath(ordner.Trim());
            if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_MESSUNG_ORDNER muss unter C:\Claude liegen: " + ziel);
            Directory.CreateDirectory(ziel);
            await File.WriteAllTextAsync(Path.Combine(ziel, "MESSUNG-C1.md"), md.ToString(), new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Nachweis des Einmessens (Klangstufe C1, Auftrag 03.10.2026): In der echten Kette wird eingemessen mit einem Satz des Korpus (Stefan,
    /// "Staffel Eins an Leitstand ...") normal und 12 dB leiser. Danach läuft der ganze Korpus mit dem Ergebnis (Verstärkung als eingemessen,
    /// Abstand der Mindest-Funklautstärke) einmal normal und einmal 12 dB leiser, je drei Rauschmuster, gezählt wie in
    /// <see cref="Messung_KlangC1_Mindestabstand"/>. Vergleich: ohne Untergrenze, und die Mindest-Funklautstärke als Untergrenze der Linie
    /// (erster Bau von C1) statt als Freigabe über den Vorpuffer.
    /// </summary>
    [Test]
    public static async Task Messung_KlangC1_Einmessen()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") != "1")
        {
            Console.WriteLine("HINWEIS Messung Einmessen Klangstufe C1 übersprungen (FUNK_MESSUNG=1 setzen).");
            return;
        }
        var st = await EinmessenImPruefstand();
        Console.WriteLine(string.Create(C, $"  MESSUNG C1E Einmessen: {st.Schritt}, normal {st.NormalDbfs:0.0} dBFS, leise {st.LeiseDbfs:0.0} dBFS, Verstärkung {st.VerstaerkungDb:+0.0;-0.0} dB, Mindest {st.MindestAbstandDb:0.0} dB unter der Stimme"));
        if (st.Schritt != MindestEinmessSchritt.Fertig) throw new PruefungException("Einmessen nicht fertig: " + st.Schritt);
        var md = new StringBuilder();
        md.AppendLine("# Nachweis Einmessen (Klangstufe C1)");
        md.AppendLine();
        md.AppendLine(string.Create(C, $"Erzeugt am {DateTime.Now:dd.MM.yyyy HH:mm} von `MessungKlangC1.Messung_KlangC1_Einmessen`. Eingemessen mit Satz 0 des Korpus normal und 12 dB leiser: normal {st.NormalDbfs:0.0} dBFS, leise {st.LeiseDbfs:0.0} dBFS (vor der Pegelstufe), Verstärkung {st.VerstaerkungDb:+0.0;-0.0} dB, Mindest-Funklautstärke {st.MindestAbstandDb:0.0} dB unter der Stimme. Danach der ganze Korpus, Automatik mit der eingemessenen Verstärkung, Vorlauf 120 ms, Saaten {string.Join("/", Saaten)}."));
        md.AppendLine();
        md.AppendLine("| Variante | normal: Wortanfänge angeschnitten (normale Äußerungen) | davon anschwellende Sätze | normal: mehr als 80 ms fehlend / Lücken / Ende fehlt | normal: leise Äußerungen | -12 dB: Wortanfänge hinaus | -12 dB: Rahmen gesendet | -12 dB: Phasen |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        // Zum Abwägen: FUNK_C1_ZUSCHLAEGE (dB) und FUNK_C1_FENSTER (Rahmen) als Listen; ohne Angabe die Werte des Programms.
        var vorgabe = new SprachGate();
        var zuschlaege = Environment.GetEnvironmentVariable("FUNK_C1_ZUSCHLAEGE") is { Length: > 0 } z
            ? z.Split(',').Select(x => float.Parse(x, C)).ToArray() : [vorgabe.FreigabeZuschlagDb];
        var fenster = Environment.GetEnvironmentVariable("FUNK_C1_FENSTER") is { Length: > 0 } f
            ? f.Split(',').Select(x => int.Parse(x, C)).ToArray() : [vorgabe.FreigabeFensterRahmen];
        var varianten = new List<(string, float, bool, float, int)> { ("ohne Untergrenze", GateVorgaben.MaxMindestAbstandDb, false, 0f, vorgabe.FreigabeFensterRahmen) };
        foreach (var w in fenster)
            foreach (var k in zuschlaege) varianten.Add((string.Create(C, $"eingemessen, Freigabe über den Vorpuffer, Fenster {w * 10} ms, Zuschlag {k:0} dB"), st.MindestAbstandDb, false, k, w));
        varianten.Add(("eingemessen, als Untergrenze der Linie (erster Bau)", st.MindestAbstandDb, true, 0f, vorgabe.FreigabeFensterRahmen));
        foreach (var (name, abstand, alsLinie, zuschlag, w) in varianten)
        {
            var normal = new List<Zaehlung>();
            var leise = new List<Zaehlung>();
            var welche = new List<string>();
            var verstaerkung = new List<string>();
            foreach (var saat in Saaten)
            {
                var tn = await Lauf(0f, saat, st.VerstaerkungDb, abstand, eingemessen: true, alsLinie, zuschlag, w);
                var tl = await Lauf(-12f, saat, st.VerstaerkungDb, abstand, eingemessen: true, alsLinie, zuschlag, w);
                normal.Add(Zaehlen(tn, welche));
                leise.Add(Zaehlen(tl));
                verstaerkung.Add(string.Create(C, $"{tn[^1].PegelstufeDb:+0.0;-0.0}/{tl[^1].PegelstufeDb:+0.0;-0.0}"));
            }
            Console.WriteLine(string.Create(C, $"  MESSUNG C1E   {name}: Verstärkung am Ende (normal/leise je Saat) {string.Join(", ", verstaerkung)}"));
            var n = Summe(normal);
            var l = Summe(leise);
            var zeile = string.Create(C,
                $"| {name} | {n.CutNormal}/{n.WoerterNormal} | {n.CutAnschwellend} | {n.Cut80} / {n.Luecken} / {n.EndeFehlt} von {n.Aeusserungen} | {n.CutLeise}/{n.WoerterLeise} | {l.WoerterNormal - l.CutNormal}/{l.WoerterNormal} | {100.0 * l.Gesendet / l.Rahmen:0.0} % | {l.Phasen} |");
            md.AppendLine(zeile);
            Console.WriteLine("  MESSUNG C1E " + zeile);
            if (welche.Count > 0) Console.WriteLine("  MESSUNG C1E   angeschnitten: " + string.Join("; ", welche.GroupBy(w => w).Select(x => $"{x.Key} x{x.Count()}")));
        }
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG_ORDNER") is { Length: > 0 } ordner)
        {
            var ziel = Path.GetFullPath(ordner.Trim());
            if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_MESSUNG_ORDNER muss unter C:\Claude liegen: " + ziel);
            Directory.CreateDirectory(ziel);
            await File.WriteAllTextAsync(Path.Combine(ziel, "MESSUNG-C1-EINMESSEN.md"), md.ToString(), new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Raster der milderen Einstellung (Klangstufe C1, Auftrag 03.10.2026, klang-c1b): Lage der Linie nach dem Einmessen (leise plus ein Drittel
    /// des Abstands, leise, leise minus 3 dB) mal Zuschlag der Freigabe über den Vorpuffer (8, 4, 0 dB), Fenster 150 ms (Protokollgrenze).
    /// Eingemessen wie in <see cref="Messung_KlangC1_Einmessen"/> (Satz 0 normal und 12 dB leiser), danach der ganze Korpus normal und 12 dB
    /// leiser, je drei Rauschmuster, gezählt wie dort. Je Kombination: angeschnittene Wortanfänge normaler Sprüche (von 492) und
    /// hinausgegangene Wortanfänge der 12 dB leiseren Sprache (von 492). Gewählt wird unter allen Kombinationen mit höchstens
    /// <see cref="HoechstensAngeschnitten"/> angeschnittenen normalen Anfängen die mit den wenigsten hinausgegangenen leisen (bei Gleichstand die
    /// mit weniger angeschnittenen); schafft keine die Grenze, die mit den wenigsten angeschnittenen. Die Lage der Linie rechnet
    /// <see cref="Mindesteinmessung.AbstandFuer"/>, der Zuschlag steht in der Kette ausdrücklich, unabhängig von den Werten ab Werk.
    /// </summary>
    [Test]
    public static async Task Messung_KlangC1_Raster()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") != "1")
        {
            Console.WriteLine("HINWEIS Messung Raster Klangstufe C1 übersprungen (FUNK_MESSUNG=1 setzen).");
            return;
        }
        var st = await EinmessenImPruefstand();
        if (st.Schritt != MindestEinmessSchritt.Fertig) throw new PruefungException("Einmessen nicht fertig: " + st.Schritt);
        var unterschied = st.NormalDbfs - st.LeiseDbfs;
        Console.WriteLine(string.Create(C, $"  MESSUNG C1R Einmessen: normal {st.NormalDbfs:0.0} dBFS, leise {st.LeiseDbfs:0.0} dBFS, Unterschied {unterschied:0.0} dB, Verstärkung {st.VerstaerkungDb:+0.0;-0.0} dB"));
        var linien = new (string Name, double Anteil, double Versatz)[]
        {
            ("leise + 1/3 des Abstands", 1.0 / 3.0, 0.0),
            ("leise", 0.0, 0.0),
            ("leise - 3 dB", 0.0, -3.0),
        };
        float[] zuschlaege = [8f, 4f, 0f];
        var md = new StringBuilder();
        md.AppendLine("# Raster Linie nach dem Einmessen mal Zuschlag der Freigabe (Klangstufe C1)");
        md.AppendLine();
        md.AppendLine(string.Create(C, $"Erzeugt am {DateTime.Now:dd.MM.yyyy HH:mm} von `MessungKlangC1.Messung_KlangC1_Raster`. Eingemessen mit Satz 0 des Korpus normal ({st.NormalDbfs:0.0} dBFS) und 12 dB leiser ({st.LeiseDbfs:0.0} dBFS), Unterschied {unterschied:0.0} dB, Verstärkung {st.VerstaerkungDb:+0.0;-0.0} dB als eingemessen. Danach der ganze Korpus, Automatik, Vorlauf 120 ms, Fenster der Freigabe 150 ms, Saaten {string.Join("/", Saaten)}."));
        md.AppendLine();
        md.AppendLine("| Linie nach dem Einmessen | Abstand unter der Stimme | Zuschlag | normal: Wortanfänge angeschnitten | davon anschwellend | normal: leise Äußerungen | -12 dB: Wortanfänge hinaus | -12 dB: Rahmen gesendet |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        var ergebnisse = new List<(string Name, float Abstand, float Zuschlag, int Cut, int Hinaus)>();
        foreach (var (name, anteil, versatz) in linien)
        {
            var abstand = Mindesteinmessung.AbstandFuer(unterschied, anteil, versatz);
            foreach (var zuschlag in zuschlaege)
            {
                var normal = new List<Zaehlung>();
                var leise = new List<Zaehlung>();
                foreach (var saat in Saaten)
                {
                    normal.Add(Zaehlen(await Lauf(0f, saat, st.VerstaerkungDb, abstand, eingemessen: true, alsLinie: false, zuschlag, fenster: 0)));
                    leise.Add(Zaehlen(await Lauf(-12f, saat, st.VerstaerkungDb, abstand, eingemessen: true, alsLinie: false, zuschlag, fenster: 0)));
                }
                var n = Summe(normal);
                var l = Summe(leise);
                var hinaus = l.WoerterNormal - l.CutNormal;
                ergebnisse.Add((name, abstand, zuschlag, n.CutNormal, hinaus));
                var zeile = string.Create(C,
                    $"| {name} | {abstand:0.0} dB | {zuschlag:0} dB | {n.CutNormal}/{n.WoerterNormal} | {n.CutAnschwellend} | {n.CutLeise}/{n.WoerterLeise} | {hinaus}/{l.WoerterNormal} | {100.0 * l.Gesendet / l.Rahmen:0.0} % |");
                md.AppendLine(zeile);
                Console.WriteLine("  MESSUNG C1R " + zeile);
            }
        }
        var erlaubt = ergebnisse.Where(e => e.Cut <= HoechstensAngeschnitten).ToList();
        var wahl = erlaubt.Count > 0
            ? erlaubt.OrderBy(e => e.Hinaus).ThenBy(e => e.Cut).First()
            : ergebnisse.OrderBy(e => e.Cut).ThenBy(e => e.Hinaus).First();
        var urteil = string.Create(C, $"Wahl: {wahl.Name}, Zuschlag {wahl.Zuschlag:0} dB (Abstand {wahl.Abstand:0.0} dB unter der Stimme): {wahl.Cut} normale Anfänge angeschnitten, {wahl.Hinaus} leise Anfänge hinaus. {(erlaubt.Count > 0 ? $"Aus {erlaubt.Count} Kombinationen mit höchstens {HoechstensAngeschnitten} angeschnittenen." : $"KEINE Kombination schafft höchstens {HoechstensAngeschnitten} angeschnittene; genommen die mit den wenigsten angeschnittenen.")}");
        md.AppendLine();
        md.AppendLine(urteil);
        Console.WriteLine("  MESSUNG C1R " + urteil);
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG_ORDNER") is { Length: > 0 } ordner)
        {
            var ziel = Path.GetFullPath(ordner.Trim());
            if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_MESSUNG_ORDNER muss unter C:\Claude liegen: " + ziel);
            Directory.CreateDirectory(ziel);
            await File.WriteAllTextAsync(Path.Combine(ziel, "MESSUNG-C1-RASTER.md"), md.ToString(), new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Hält die Grenze der milden Einstellung fest (klang-c1b, <see cref="Messung_KlangC1_Raster"/>): Nach dem Einmessen (Satz 0 des Korpus normal und
    /// 12 dB leiser) mit den Werten ab Werk (Lage der Linie des Einmessens, Zuschlag der Freigabe) verlieren normale Sprüche über drei Rauschmuster
    /// höchstens <see cref="HoechstensUeber80"/> von 492 Wortanfängen mehr als 80 ms, kein Ende und kein Wort mitten im Spruch. Das ist Sarevocs Vorgabe
    /// vom 03.10.2026 ("am Anfang dürfen im Zweifel bis zu 80 ms fehlen, abschneiden und unterbrechen nie"), dieselbe wie in der Abnahme der
    /// Klangstufe C2a; bis zum 04.10.2026 galt hier die strengere Zählung "eine der ersten 30 ms fehlt", die als Messwert bleibt (ohne Wächter 9,
    /// mit Wächter 12). Ein verlorener Befehl wiegt schwerer als ein durchgerutschter leiser Satz; mit leise plus ein Drittel des Unterschieds und
    /// 8 dB Zuschlag waren es 69 angeschnittene.
    /// </summary>
    [Test]
    public static async Task C1_Einmessen_NormaleSpruecheHoechstensZehnAngeschnitten()
    {
        var st = await EinmessenImPruefstand();
        Pruefe.Gleich(MindestEinmessSchritt.Fertig, st.Schritt, "eingemessen mit dem Satz des Korpus");
        var normal = new List<Zaehlung>();
        var welche = new List<string>();
        foreach (var saat in Saaten) normal.Add(Zaehlen(await Lauf(0f, saat, st.VerstaerkungDb, st.MindestAbstandDb, eingemessen: true), welche));
        var n = Summe(normal);
        Pruefe.Gleich(492, n.WoerterNormal, "492 Wortanfänge normaler Äußerungen in drei Rauschmustern");
        // Was Sarevoc verlangt (Vorgabe 03.10.2026, Gegenprüfung C1, M3): Lücken und fehlende Enden nie, mehr als 80 ms am Anfang höchstens 10 von 492.
        // Die 30-ms-Zählung (angeschnitten) bleibt als Messwert.
        Console.WriteLine(string.Create(C, $"  C1 eingemessen: angeschnitten {n.CutNormal}/{n.WoerterNormal}, mehr als 80 ms fehlend {n.Cut80}, Lücken {n.Luecken}, Ende fehlt {n.EndeFehlt}/{n.Aeusserungen}; angeschnitten: {string.Join("; ", welche.Where(w => !w.Contains("/leise/")).GroupBy(w => w[..w.LastIndexOf('/')]).Select(x => $"{x.Key} x{x.Count()}"))}"));
        Pruefe.Gleich(0, n.Luecken, "keine Lücke mitten im Spruch");
        Pruefe.Gleich(0, n.EndeFehlt, "kein Ende fehlt");
        if (n.Cut80 > HoechstensUeber80)
            throw new PruefungException(string.Create(C,
                $"normale Sprüche nach dem Einmessen (Abstand {st.MindestAbstandDb:0.0} dB): höchstens {HoechstensUeber80} von {n.WoerterNormal} Wortanfängen mit mehr als 80 ms Verlust, war {n.Cut80} (angeschnitten über 30 ms {n.CutNormal}, davon anschwellend {n.CutAnschwellend})"));
    }

    /// <summary>Höchstens so viele von 492 Anfängen normaler Sprüche darf das Einmessen anschneiden (etwa 2 %, Auftrag 03.10.2026).</summary>
    internal const int HoechstensAngeschnitten = 10;

    /// <summary>
    /// Höchstens so viele von 492 normalen Wortanfängen dürfen mehr als 80 ms verlieren (Vorgabe Sarevoc 03.10.2026, Grenze der Koordination
    /// 04.10.2026); gemessen 6, alles leise beginnende Satzanfänge (Gegenprüfung C1, M3).
    /// </summary>
    internal const int HoechstensUeber80 = 10;

    private static float[] Satz(float[] eingang)
    {
        var satz = new float[Mindesteinmessung.SatzRahmen * 480];
        AnbindungSignale.Rauschen(satz.Length, Pumpmessung.GrundrauschenDbfs, 5).CopyTo(satz, 0);
        var stueck = MessungGegenpruefungKlangA.Stueck(eingang, 0, 1);
        stueck.AsSpan(0, Math.Min(stueck.Length, satz.Length)).CopyTo(satz);
        return satz;
    }

    /// <summary>Einmessen in der echten Kette mit Satz 0 des Korpus, normal und 12 dB leiser (wie im Nachweis Einmessen).</summary>
    private static async Task<MindestEinmessStand> EinmessenImPruefstand()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Abspielen(AnbindungSignale.Rauschen(150 * 480, Pumpmessung.GrundrauschenDbfs, 3));
        s.Maschine.MindestEinmessenStarten();
        s.Abspielen(Satz(Pumpmessung.Eingang(0f)));
        s.Abspielen(AnbindungSignale.Rauschen(Mindesteinmessung.UmschaltRahmen * 480, Pumpmessung.GrundrauschenDbfs, 4));
        s.Abspielen(Satz(Pumpmessung.Eingang(-12f)));
        s.Stille(5);
        return s.Maschine.MindestEinmessen;
    }

    private static Zaehlung Summe(List<Zaehlung> z) => new(z.Sum(x => x.WoerterNormal), z.Sum(x => x.CutNormal), z.Sum(x => x.WoerterLeise), z.Sum(x => x.CutLeise),
        z.Sum(x => x.Rahmen), z.Sum(x => x.Gesendet), z.Sum(x => x.Phasen), z.Sum(x => x.CutAnschwellend), z.Sum(x => x.Cut80), z.Sum(x => x.Luecken),
        z.Sum(x => x.EndeFehlt), z.Sum(x => x.Aeusserungen));

    /// <param name="eingemessen">Automatik ab der eingemessenen Verstärkung (wie der Client nach dem Einmessen); sonst fest und gelernt.</param>
    /// <param name="alsLinie">Mindest-Funklautstärke als Untergrenze der Linie (erster Bau von C1, nur zum Vergleich).</param>
    /// <param name="zuschlag">Zuschlag der Freigabe (<see cref="SprachGate.FreigabeZuschlagDb"/>); NaN: der des Programms.</param>
    private static async Task<List<TelemetrieEintrag>> Lauf(float mikrofonDb, ulong saat, float g, float abstand, bool eingemessen = false, bool alsLinie = false,
        float zuschlag = float.NaN, int fenster = 0)
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        s.Maschine.Lautstaerke = eingemessen ? new LautstaerkeEinstellung(true, g, Gelernt: true, Eingemessen: true) : new LautstaerkeEinstellung(false, g, Gelernt: true);
        s.Maschine.Gate = GateVorgaben.Standard with { MindestAbstandDb = abstand };
        s.Maschine.Kette.Fuehrung.MindestAlsLinie = alsLinie;
        if (float.IsFinite(zuschlag)) s.Maschine.Kette.Fuehrung.Gate.FreigabeZuschlagDb = zuschlag;
        if (fenster > 0) s.Maschine.Kette.Fuehrung.Gate.FreigabeFensterRahmen = fenster;
        // Diagnose: FUNK_C1_WAECHTER=0 schaltet die Auto Dauersend-Dämpfung (Klangstufe C2a) aus, zum Vergleich.
        if (Environment.GetEnvironmentVariable("FUNK_C1_WAECHTER") == "0") s.Maschine.Daempfung = new DaempfungEinstellung(false, Daempfungsstufe.DeutlichLeiser);
        if (Environment.GetEnvironmentVariable("FUNK_C1_WAECHTER") == "1") s.Maschine.Kette.Waechter.Diagnose = z => Console.WriteLine("  W " + z);
        await s.StartenAsync();
        var urteil = DauersendUrteil.Keins;
        var verlauf = Environment.GetEnvironmentVariable("FUNK_C1_WAECHTER") == "1";
        s.Abspielen(Pumpmessung.Eingang(mikrofonDb, saat), t =>
        {
            if (!verlauf || s.Maschine.EigenesUrteil.Urteil == urteil) return;
            urteil = s.Maschine.EigenesUrteil.Urteil;
            Console.WriteLine(string.Create(C, $"  W Urteil {urteil} bei {t / 100.0:0.00} s"));
        });
        return [.. s.Telemetrie];
    }

    /// <param name="welche">Wenn angegeben: je angeschnittenem Wort "Nr/Hülle/Wort i".</param>
    private static Zaehlung Zaehlen(List<TelemetrieEintrag> t, List<string>? welche = null)
    {
        var n = t.Count;
        var gesendet = Pumpmessung.GesendeteRahmen(t);
        var verzug = Pumpmessung.VerzugGate;
        int wn = 0, cn = 0, wl = 0, cl = 0, rahmen = 0, sendet = 0, phasen = 0, ca = 0, c80 = 0, luecken = 0, ende = 0, ae = 0;
        foreach (var a in Klangkorpus.Aeusserungen)
        {
            var leise = a.Huelle == "leise";
            // Erster gesendeter Rahmen der Äußerung: Ein angeschnittenes Wort danach ist eine Lücke, davor fehlt der Anfang (Zählweise der Gegenprüfung C1).
            var vonA = (a.Start + verzug) / 480;
            var bisA = Math.Min(n, ((a.Ende + verzug) / 480) + 1);
            var erster = int.MaxValue;
            for (var f = vonA; f < bisA; f++)
            {
                if (!gesendet[f]) continue;
                erster = f;
                break;
            }
            for (var i = 0; i < a.Woerter.Count; i++)
            {
                var w = a.Woerter[i];
                var f0 = (w + verzug) / 480;
                var f1 = (w + verzug + 1440 - 1) / 480;
                var cut = Enumerable.Range(f0, f1 - f0 + 1).Any(f => f >= n || !gesendet[f]);
                if (cut) welche?.Add($"{a.Nr}/{a.Huelle}/Wort {i + 1}");
                if (leise)
                {
                    wl++;
                    if (cut) cl++;
                }
                else
                {
                    wn++;
                    if (cut) cn++;
                    if (cut && a.Huelle == "anschwellend") ca++;
                    if (cut && f0 > erster) luecken++;
                    var f80 = (w + verzug + 3840) / 480;
                    if (f80 >= n || !gesendet[f80]) c80++;
                }
            }
            if (leise) continue;
            ae++;
            var e0 = (a.Ende + verzug - 7200) / 480;
            var e1 = (a.Ende + verzug - 1) / 480;
            if (Enumerable.Range(e0, e1 - e0 + 1).Any(f => f >= n || !gesendet[f])) ende++;
            var von = (a.Start + verzug) / 480;
            var bis = Math.Min(n, (a.Ende + verzug) / 480 + 1);
            for (var f = von; f < bis; f++)
            {
                rahmen++;
                if (gesendet[f]) sendet++;
                if (gesendet[f] && (f == 0 || !gesendet[f - 1])) phasen++;
            }
        }
        return new Zaehlung(wn, cn, wl, cl, rahmen, sendet, phasen, ca, c80, luecken, ende, ae);
    }
}
