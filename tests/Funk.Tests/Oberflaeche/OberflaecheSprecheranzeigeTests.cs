// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Sprecheranzeige über dem Spiel (0.11.9, Sarevoc 11.10. und 15.10.2026, tmp\sprecher\SPEZ.md). Geprüft mit einer Uhr der Tests: der Filter auf gehörte
/// Kanäle (abgeschaltete und stumm geschaltete nicht, Durchsage und Global immer), die Texte (Name fett aus der Praesenz, Kanal mit Ch., "Durchsage an
/// alle", "Durchsage: Kanal", "Global", "Du -> Kanal"), die Reihenfolge, Zeilen 1 bis 4 mit der Regel für "+n weitere", Nachhalten, Ausblenden und
/// Nachrücken, die Probe beim Schieben; der Weg von der Brücke (Stempel und Empfangsanzeige); die Lage nach den Reglern mit DPI und Begrenzung am Rand und die Wahl des
/// Monitors; das Fenster (Stile, kein Owner, nie aktiviert, im Quelltext bewacht); das Modell unter Einstellungen mit Ablage und Erweitert; im
/// Bildlauf (kein Fenster auf dem Bildschirm) der Abschnitt und die Zeile selbst. Mit FUNK_BILDER die Bilder vorschau-0119-sprecheranzeige-einstellungen
/// und vorschau-0119-sprecheranzeige-zeile. Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheSprecheranzeigeTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Ein Monitor links neben dem Hauptmonitor, 4K bei 175 %.</summary>
    private static readonly Bildschirm Links4k = new(new Pixelrechteck(-3840, 0, 3840, 2160), new Pixelrechteck(-3840, 0, 3840, 2100), 1.75, false);

    /// <summary>Die Lage mit Kanälen (Vorlage Star Citizen, Max Mustermann als Kapitän): Brücke und Crew auf Schiff 3, Kommando 1, Sanität auf Ruhe, Logistik aus.</summary>
    private static FunkLage Lage() => FunkDienstAttrappe.KanalBeispiel();

    private static ushort K(FunkLage l, string name, byte ch) => Kanalrechnung.Id(l.Netze.Single(n => n.Name == name).Id, ch);

    /// <summary>Eine Anzeige über der Attrappe mit eigener Uhr, Einstellungen, Stummen und eigener Sendung.</summary>
    private sealed class Probe
    {
        public Probe(FunkLage? lage = null)
        {
            Dienst = new FunkDienstAttrappe(lage ?? Lage());
            Anzeige = new Sprecheranzeige(Dienst, () => E, () => Uhr, k => Stumm.Contains(k), () => Eigene);
        }

        public FunkDienstAttrappe Dienst { get; }
        public SprecheranzeigeEinstellungen E { get; } = new();
        public long Uhr { get; set; } = 10_000;
        public HashSet<uint> Stumm { get; } = [];
        public IReadOnlyList<ushort>? Eigene { get; set; }
        public Sprecheranzeige Anzeige { get; }

        public FunkLage L => Dienst.Lage;

        /// <summary>Die Zeilen als Text, "|" dazwischen.</summary>
        public string Text => string.Join("|", Anzeige.Bild.Zeilen.Select(z => z.Text));

        public void Melden(params GehoerterSprecher[] s) => Anzeige.Melden(s);

        /// <summary>Die Zeit läuft in Schritten des Bildtakts; dabei melden <paramref name="sprechen"/> alle 40 ms wie die Maschine.</summary>
        public void Weiter(long ms, params GehoerterSprecher[] sprechen)
        {
            var ende = Uhr + ms;
            long naechsteMeldung = Uhr;
            while (Uhr < ende)
            {
                Uhr = Math.Min(ende, Uhr + 33);
                if (sprechen.Length > 0 && Uhr >= naechsteMeldung)
                {
                    Anzeige.Melden(sprechen);
                    naechsteMeldung = Uhr + 40;
                }
                Anzeige.Takt();
            }
        }
    }

    private static GehoerterSprecher Netz(uint konto, params ushort[] netze) => new(konto, Spruchart.Netz, netze);

    // ------------------------------------------------------------------------------------------------------------------------------
    // Filter und Texte
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Nur wer wirklich gehört wird: in Netzen nur eigene Felder mit "hören" (Logistik ist aus: nicht; mit einem zweiten gehörten Netz im Stempel
    /// dessen Name), ein am gelben Zeichen stumm geschalteter Funker gar nicht, das eigene Konto nie. Durchsage und Global hört man auch in einem
    /// abgeschalteten Kanal (6.6): "Durchsage an alle", "Durchsage: Kanal (Ch.)", "Global". Der Name ist der Anzeigename aus der Praesenz, ohne
    /// Eintrag "Funker n". Ohne Zeichen, ohne gelbes "!". Aus heißt: nichts.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Filter_GehoerteKanaele_StummUndAusNicht_Texte()
    {
        var p = new Probe();
        var l = p.L;
        var bruecke = K(l, "Brücke", 3);
        var command = K(l, "Kommando", 1);
        var aus = K(l, "Logistik", 1);
        Pruefe.Wahr(l.Felder.Single(f => f.NetzId == aus) is { Hoeren: false }, "Logistik ist im Beispiel abgeschaltet");
        Pruefe.Gleich("Brücke (Schiff 3)", l.KanalName(bruecke), "der Kanalname mit Ch.");
        Pruefe.Falsch(p.Anzeige.Sichtbar, "ohne Sprecher nichts");

        p.Melden(Netz(3, bruecke));
        Pruefe.Gleich("Kobold Brücke (Schiff 3)", p.Text, "Name fett, dahinter der Kanal");
        Pruefe.Wahr(p.Anzeige.Sichtbar, "sofort sichtbar");
        var z = p.Anzeige.Bild.Zeilen.Single();
        Pruefe.Wahr(z.Name == "Kobold" && z.Rest == " Brücke (Schiff 3)" && z.Deckkraft == 1, "Name und Rest getrennt (fett und normal)");

        var q = new Probe();
        q.E.Zeilen = 4;
        q.Melden(Netz(2, aus));
        Pruefe.Gleich("", q.Text, "ein abgeschalteter Kanal zählt nicht");
        q.Melden(Netz(2, aus, command));
        Pruefe.Gleich("Falke/Tom " + l.KanalName(command), q.Text, "mit einem gehörten Netz im Stempel: dessen Name (Anzeigename aus der Praesenz)");
        q.Stumm.Add(5);
        q.Melden(Netz(5, K(l, "Bergung und Bergbau", 2)));
        Pruefe.Gleich("Falke/Tom " + l.KanalName(command), q.Text, "ein stumm geschalteter Funker (gelbes Zeichen, deine Wahl) zählt nicht");
        q.Melden(Netz(l.Konto!.Id, bruecke));
        Pruefe.Wahr(!q.Text.Contains("Max", StringComparison.Ordinal), "das eigene Konto nie über den Stempel");

        var d = new Probe();
        d.E.Zeilen = 4;
        d.Melden(new GehoerterSprecher(4, Spruchart.Durchsage, []), new GehoerterSprecher(2, Spruchart.Durchsage, [aus]), new GehoerterSprecher(7, Spruchart.Global, [1]),
            Netz(99, bruecke));
        Pruefe.Gleich("Nordlicht/Anna Durchsage an alle|Falke/Tom Durchsage: " + l.KanalName(aus) + "|Sturmvogel Global|Funker 99 Brücke (Schiff 3)", d.Text,
            "Durchsage an alle, an Netze (auch abgeschaltet, sie ist zu hören), Global, unbekannt als Funker n");
        Pruefe.Falsch(d.Text.Contains('!', StringComparison.Ordinal), "kein Zeichen, kein \"!\"");

        var a = new Probe();
        a.E.An = false;
        a.Melden(Netz(3, bruecke));
        Pruefe.Wahr(a.Text.Length == 0 && !a.Anzeige.Sichtbar, "aus: nichts");
        a.E.An = true;
        a.Melden(Netz(3, bruecke));
        Pruefe.Gleich("Kobold Brücke (Schiff 3)", a.Text, "wieder an");
        a.E.An = false;
        a.Anzeige.Takt();
        Pruefe.Wahr(a.Text.Length == 0 && !a.Anzeige.Sichtbar, "ausgeschaltet, während jemand steht: sofort fort");

        // Die Rechnungen einzeln.
        Pruefe.Gleich(null, Sprecheranzeige.Kanaltext(l, Netz(3)), "ohne Netz in Netzen: nichts");
        Pruefe.Gleich(null, Sprecheranzeige.Kanaltext(l, Netz(3, K(l, "Sanität", 0))), "Ruhe gehört nicht (Hören im Feld, aber der Server stempelt sie nie)");
        Pruefe.Gleich("Funker 42", Sprecheranzeige.Anzeigename(l, 42), "unbekannt");
        Pruefe.Gleich("Kobold", Sprecheranzeige.Anzeigename(l, 3), "Kobold ohne Discord-Zusatz");
    }

    /// <summary>
    /// Mehrere gleichzeitig: wer zuerst sprach, steht oben; höchstens so viele Zeilen wie eingestellt. Bei 1 Zeile genau ein Name (kein "+n"), bei 2 bis 4
    /// und mehr Sprechern eine Zeile weniger mit Namen und unten "+n weitere"; passen alle, ohne. Wer schon steht, behält seinen Platz.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Reihenfolge_Zeilen1bis4_Weitere()
    {
        var p = new Probe();
        var l = p.L;
        var b = K(l, "Brücke", 3);
        var c = K(l, "Kommando", 1);
        var crew = K(l, "Crew", 3);
        p.Melden(Netz(3, b));
        p.Uhr += 40;
        p.Melden(Netz(3, b), Netz(2, c));
        p.Uhr += 40;
        p.Melden(Netz(4, crew), Netz(2, c), Netz(3, b));
        Pruefe.Gleich("Kobold Brücke (Schiff 3)", p.Text, "1 Zeile (ab Werk): genau ein Name, der erste");
        Pruefe.Gleich(SprecheranzeigeEinstellungen.ZeilenAbWerk, 1, "ab Werk 1 (Sarevoc 15.10.2026)");
        p.E.Zeilen = 2;
        p.Anzeige.Takt();
        Pruefe.Gleich("Kobold Brücke (Schiff 3)|+2 weitere", p.Text, "2 Zeilen, 3 Sprecher: einer und +2 weitere");
        var weitere = p.Anzeige.Bild.Zeilen[1];
        Pruefe.Wahr(weitere.Name.Length == 0 && weitere.Rest == "+2 weitere", "+n weitere ohne fetten Teil");
        p.E.Zeilen = 3;
        p.Anzeige.Takt();
        Pruefe.Gleich("Kobold Brücke (Schiff 3)|Falke/Tom " + l.KanalName(c) + "|Nordlicht/Anna " + l.KanalName(crew), p.Text,
            "3 Zeilen, 3 Sprecher: alle in der Reihenfolge, in der sie anfingen (die Meldung nennt sie anders)");
        p.E.Zeilen = 4;
        p.Melden(Netz(5, b), Netz(7, b), Netz(6, b), Netz(3, b), Netz(2, c), Netz(4, crew));
        Pruefe.Gleich(4, p.Anzeige.Bild.Zeilen.Count, "4 Zeilen, 6 Sprecher: vier Zeilen");
        Pruefe.Gleich("+3 weitere", p.Anzeige.Bild.Zeilen[3].Text, "drei mit Namen, +3 weitere");
        Pruefe.Gleich("Kobold", p.Anzeige.Bild.Zeilen[0].Name, "der erste bleibt oben");
        p.E.Zeilen = 1;
        p.Anzeige.Takt();
        Pruefe.Gleich("Kobold Brücke (Schiff 3)", p.Text, "wieder 1 Zeile: genau ein Name, kein +n");
        foreach (var (zeilen, anzahl, erwartet) in new[] { (1, 0, 0), (1, 1, 1), (1, 5, 1), (2, 2, 2), (2, 3, 1), (3, 3, 3), (3, 4, 2), (4, 4, 4), (4, 9, 3) })
            Pruefe.Gleich(erwartet, Sprecheranzeige.MitNamen(zeilen, anzahl), T($"{zeilen} Zeilen, {anzahl} Sprecher"));
    }

    /// <summary>
    /// Die Zeit: sofort beim ersten Paket, voll bis 1,0 s nach der letzten Meldung, in 0,5 s aus (bei 1250 ms halb), dann fort und verborgen. Spricht er
    /// beim Ausblenden wieder, ist er sofort voll und an seinem Platz. Nachrücken bei 1 Zeile: Hört der erste auf, steht er noch 1,0 s, dann sofort
    /// der nächste (ohne Ausblenden). Einstellbar: 0 s und 0 s (fort, sobald die Meldungen fehlen, mit 120 ms Lücke für den Takt der Maschine), 5 s und
    /// 2 s. Keine Daueranimation: Ohne Sprecher ändert der Takt nichts.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Zeit_NachhaltenAusblendenNachruecken()
    {
        var p = new Probe();
        var b = K(p.L, "Brücke", 3);
        var kobold = Netz(3, b);
        p.Weiter(500, kobold);
        Pruefe.Gleich("Kobold Brücke (Schiff 3)", p.Text, "spricht");
        p.Melden(kobold);
        var letzte = p.Uhr;
        p.Uhr = letzte + 1000;
        p.Anzeige.Takt();
        Pruefe.Wahr(p.Anzeige.Bild.Zeilen.Single().Deckkraft == 1, "1,0 s nach dem letzten Wort noch voll");
        p.Uhr = letzte + 1250;
        p.Anzeige.Takt();
        Pruefe.Wahr(Math.Abs(p.Anzeige.Bild.Zeilen.Single().Deckkraft - 0.5) < 1e-9, "bei 1,25 s halb ausgeblendet: " + p.Anzeige.Bild.Zeilen.Single().Deckkraft);
        p.Melden(kobold);
        Pruefe.Wahr(p.Anzeige.Bild.Zeilen.Single().Deckkraft == 1, "spricht wieder: sofort voll");
        letzte = p.Uhr;
        p.Uhr = letzte + 1499;
        p.Anzeige.Takt();
        Pruefe.Wahr(p.Anzeige.Sichtbar && p.Anzeige.Bild.Zeilen.Single().Deckkraft > 0, "kurz vor dem Ende noch da");
        p.Uhr = letzte + 1501;
        p.Anzeige.Takt();
        Pruefe.Wahr(!p.Anzeige.Sichtbar && p.Anzeige.Bild.Zeilen.Count == 0, "nach 1,5 s fort und verborgen");
        var vorher = p.Anzeige.Bild.Zeilen.Count;
        var meldungen = 0;
        p.Anzeige.Bild.Zeilen.CollectionChanged += (_, _) => meldungen++;
        p.Anzeige.PropertyChanged += (_, _) => meldungen++;
        p.Weiter(2000);
        Pruefe.Gleich(0, meldungen, "ohne Sprecher ändert der Takt nichts (keine Daueranimation)");
        Pruefe.Gleich(0, vorher, "leer");

        // Nachrücken bei 1 Zeile.
        var n = new Probe();
        var falke = Netz(2, K(n.L, "Kommando", 1));
        n.Weiter(200, kobold);
        n.Weiter(200, kobold, falke);
        n.Melden(kobold, falke);
        Pruefe.Gleich("Kobold Brücke (Schiff 3)", n.Text, "Kobold zuerst, Falke wartet");
        var stille = n.Uhr;
        n.Weiter(990, falke);
        Pruefe.Wahr(n.Text.StartsWith("Kobold", StringComparison.Ordinal), "Kobold steht noch (Nachhalten)");
        n.Weiter(80, falke);
        Pruefe.Wahr(n.Uhr - stille > 1000 && n.Text.StartsWith("Falke/Tom", StringComparison.Ordinal), "nach dem Nachhalten rückt Falke nach, ohne Ausblenden: " + n.Text);
        Pruefe.Gleich(1.0, n.Anzeige.Bild.Zeilen.Single().Deckkraft, "voll");

        // 0 s und 0 s.
        var s = new Probe();
        s.E.NachhaltenMs = 0;
        s.E.AusblendenMs = 0;
        s.Weiter(300, kobold);
        s.Melden(kobold);
        Pruefe.Wahr(s.Anzeige.Sichtbar, "spricht");
        s.Weiter(Sprecheranzeige.MeldeLueckeMs - 30);
        Pruefe.Wahr(s.Anzeige.Sichtbar, "zwischen zwei Meldungen der Maschine nicht weg");
        s.Weiter(100);
        Pruefe.Falsch(s.Anzeige.Sichtbar, "0 s: gleich fort");

        // 5 s und 2 s.
        var g = new Probe();
        g.E.NachhaltenMs = SprecheranzeigeEinstellungen.NachhaltenMaxMs;
        g.E.AusblendenMs = SprecheranzeigeEinstellungen.AusblendenMaxMs;
        g.Melden(kobold);
        var start = g.Uhr;
        g.Uhr = start + 5000;
        g.Anzeige.Takt();
        Pruefe.Gleich(1.0, g.Anzeige.Bild.Zeilen.Single().Deckkraft, "5 s voll");
        g.Uhr = start + 6000;
        g.Anzeige.Takt();
        Pruefe.Wahr(Math.Abs(g.Anzeige.Bild.Zeilen.Single().Deckkraft - 0.5) < 1e-9, "nach 6 s halb (2 s Ausblenden)");
        g.Uhr = start + 7001;
        g.Anzeige.Takt();
        Pruefe.Falsch(g.Anzeige.Sichtbar, "nach 7 s fort");
    }

    /// <summary>
    /// Die eigene Sendung: ab Werk aus (nichts, auch wenn du sendest); an steht "Du -> Kanal (Ch.)" in der Reihenfolge des Anfangs, mit Global "Du ->
    /// Global", mit mehreren Netzen das kleinste Feld; hörst du auf, hält und blendet sie wie jede Zeile.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_EigeneSendung_AbWerkAus_AnDuKanal()
    {
        var p = new Probe();
        var l = p.L;
        var b = K(l, "Brücke", 3);
        var c = K(l, "Kommando", 1);
        Pruefe.Falsch(new SprecheranzeigeEinstellungen().EigeneSendung, "ab Werk aus (Sarevoc 15.10.2026)");
        p.Eigene = [b];
        p.Weiter(200);
        Pruefe.Gleich("", p.Text, "aus: keine Zeile, auch wenn du sendest");
        p.E.EigeneSendung = true;
        p.E.Zeilen = 3;
        p.Melden(Netz(2, c));
        p.Weiter(100, Netz(2, c));
        Pruefe.Gleich("Falke/Tom " + l.KanalName(c) + "|Du -> Brücke (Schiff 3)", p.Text, "an: Du -> Kanal, nach dem, der zuerst sprach");
        Pruefe.Wahr(p.Anzeige.Bild.Zeilen[1] is { Name: "Du", Rest: " -> Brücke (Schiff 3)" }, "Du fett, der Rest normal");
        p.Eigene = [c, b];
        p.Weiter(100);
        Pruefe.Gleich("Du -> Brücke (Schiff 3)", p.Anzeige.Bild.Zeilen[1].Text, "mehrere Netze: das kleinste Feld (Brücke liegt in Feld 1, Kommando in Feld 4)");
        Pruefe.Gleich("Global", Sprecheranzeige.EigenerKanal(l, [b, Sprachkanal.GlobalNetz]), "mit Global: Global");
        Pruefe.Gleich(null, Sprecheranzeige.EigenerKanal(l, []), "ohne Netz nichts");
        p.Eigene = null;
        p.Weiter(1000);
        Pruefe.Wahr(p.Text.Contains("Du -> ", StringComparison.Ordinal), "aufgehört: noch 1,0 s da");
        p.Weiter(700);
        Pruefe.Falsch(p.Text.Contains("Du -> ", StringComparison.Ordinal), "dann fort");
    }

    /// <summary>
    /// Die Probe beim Schieben an der Lage (Sarevoc 15.10.2026, Schieberegler statt Raster): Jedes Schieben zeigt die Probezeile (auch wenn die Anzeige aus
    /// ist) und verlängert sie; solange geschoben wird, bleibt sie, nach dem letzten Schieben noch 3 s, dann wieder, was gerade gilt.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Probe_BeimSchiebenUndNoch3s()
    {
        var p = new Probe();
        p.E.An = false;
        p.Anzeige.ProbeZeigen();
        Pruefe.Gleich(Sprecheranzeige.ProbeName + " " + Sprecheranzeige.ProbeSatz, p.Text, "die Probezeile, auch wenn aus");
        Pruefe.Wahr(p.Anzeige.Sichtbar && p.Anzeige.ProbeLaeuft, "sichtbar");
        // Geschoben wird zehn Sekunden lang, alle 400 ms ein Schritt: Die Probe steht die ganze Zeit.
        for (var i = 0; i < 25; i++)
        {
            p.Uhr += 400;
            p.Anzeige.Takt();
            Pruefe.Wahr(p.Anzeige.Sichtbar, T($"beim Schieben nach {(i + 1) * 400} ms da"));
            p.Anzeige.ProbeZeigen();
        }
        p.Uhr += Sprecheranzeige.ProbeMs - 1;
        p.Anzeige.Takt();
        Pruefe.Wahr(p.Anzeige.Sichtbar, "2999 ms nach dem letzten Schieben noch da");
        p.Uhr += 1;
        p.Anzeige.Takt();
        Pruefe.Falsch(p.Anzeige.Sichtbar, "3 s nach dem letzten Schieben fort");
        p.E.An = true;
        p.Melden(Netz(3, K(p.L, "Brücke", 3)));
        p.Anzeige.ProbeZeigen();
        Pruefe.Gleich(Sprecheranzeige.ProbeName + " " + Sprecheranzeige.ProbeSatz, p.Text, "die Probe steht allein");
        p.Uhr += Sprecheranzeige.ProbeMs;
        p.Melden(Netz(3, K(p.L, "Brücke", 3)));
        Pruefe.Gleich("Kobold Brücke (Schiff 3)", p.Text, "danach wieder der Sprecher");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Der Weg von der Brücke
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Die Brücke: je Sprecher, den die Empfangsanzeige der Maschine als sprechend meldet, Konto, Art und Netze aus dem Stempel seines letzten Pakets
    /// (die Netze in der Reihenfolge des Stempels, bei einer Durchsage an alle keine); wer nicht spricht oder nie ein Paket hatte, fehlt. Verstummen
    /// alle, kommt einmal eine leere Liste, danach nichts. Die eigene Sendung ist das Ziel der offenen Phase, mit dem Ende fort. Die Anzeige über der
    /// Brücke: der Weg vom Paket zur Zeile.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Bruecke_StempelUndSpricht_EigeneSendung()
    {
        var maschine = new KlangMaschinenAttrappe();
        using var b = new KlangBruecke(maschine);
        var gemeldet = new List<IReadOnlyList<GehoerterSprecher>>();
        b.SprecherGehoert += l => gemeldet.Add(l);
        KlangBrueckeTests.Empfangen(b, 9, 1, Kennzeichen.Beginn, Spruchart.Netz, [7, 5]);
        KlangBrueckeTests.Empfangen(b, 10, 1, Kennzeichen.Beginn, Spruchart.Durchsage, []);
        KlangBrueckeTests.Empfangen(b, 11, 1, Kennzeichen.Beginn, Spruchart.Global, [1]);
        var m7 = b.MischerNetzVon(7);
        maschine.SprecherPegelMelden(new SprecherPegel(9, m7, -20f, true), new SprecherPegel(10, 0, -25f, true), new SprecherPegel(11, 0, -30f, false),
            new SprecherPegel(12, m7, -10f, true));
        Pruefe.Gleich(1, gemeldet.Count, "eine Meldung");
        string Zeile(GehoerterSprecher s) => s.KontoId + " " + s.Art + " [" + string.Join(",", s.Netze) + "]";
        Pruefe.Gleich("9 Netz [7,5]|10 Durchsage []", string.Join("|", gemeldet[0].Select(Zeile)),
            "Konto, Art und Netze des Stempels; 11 spricht nicht, 12 hatte nie ein Paket");
        KlangBrueckeTests.Empfangen(b, 9, 2, Kennzeichen.None, Spruchart.Netz, [5]);
        maschine.SprecherPegelMelden(new SprecherPegel(9, m7, -20f, true));
        Pruefe.Gleich("9 Netz [5]", string.Join("|", gemeldet[1].Select(Zeile)), "die Netze des letzten Stempels");
        maschine.SprecherPegelMelden(new SprecherPegel(9, m7, -20f, false));
        maschine.SprecherPegelMelden(new SprecherPegel(9, m7, -20f, false));
        Pruefe.Gleich(3, gemeldet.Count, "verstummt: einmal leer, dann nichts");
        Pruefe.Gleich(0, gemeldet[2].Count, "leer");

        // Die eigene Sendung.
        var weg = new FunkwegAttrappe { Ziel = Sendeziel.Netz(4, 2) };
        b.Anbinden(weg);
        Pruefe.Gleich(null, b.EigeneSendung, "ohne Phase keine");
        maschine.SendenSimulieren(Sprachkennzeichen.Beginn, new byte[100], 1);
        Pruefe.Gleich(Sendeziel.Netz(2, 4), b.EigeneSendung, "das Ziel der offenen Phase");
        maschine.SendenSimulieren(Sprachkennzeichen.Ende, []);
        Pruefe.Gleich(null, b.EigeneSendung, "mit dem Ende fort");

        // Vom Paket zur Zeile: die Anzeige hängt an der Brücke (im Betrieb über den Oberflächentakt).
        var lage = Lage();
        var bruecke = K(lage, "Brücke", 3);
        var uhr = 0L;
        var anzeige = new Sprecheranzeige(new FunkDienstAttrappe(lage), () => new SprecheranzeigeEinstellungen(), () => uhr);
        b.SprecherGehoert += anzeige.Melden;
        KlangBrueckeTests.Empfangen(b, 3, 1, Kennzeichen.Beginn, Spruchart.Netz, [bruecke]);
        maschine.SprecherPegelMelden(new SprecherPegel(3, b.MischerNetzVon(bruecke), -20f, true));
        Pruefe.Gleich("Kobold Brücke (Schiff 3)", anzeige.Bild.Text, "vom Stempel über die Empfangsanzeige zur Zeile");
        b.SprecherGehoert -= anzeige.Melden;
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Lage und Fenster
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Die Lage mit zwei Reglern (Sarevoc 15.10.2026: "die Position mit Schieberegler einstellen, nicht fest vorgeschlagen"): Die linke obere Ecke steht bei 0 %
    /// 24 Einheiten mit der Skala des Monitors vom linken und oberen Rand des ganzen Monitors (Full HD 24, WQHD bei 150 % 36, 4K bei 175 % 42
    /// Bildpunkte) und wandert mit dem Prozentwert bis ebenso weit vom rechten und unteren Rand; der Block ragt nie hinaus (bei 100 % rückt er hinein,
    /// auch mit mehreren Zeilen; ist er fast so breit wie der Monitor, steht er ganz darin; breiter bleibt er links). Ab Werk 0 und 0, ganz oben
    /// links. Der Monitor: fest gewählt, oder Automatisch der von Star Citizen vorn, sonst der zuletzt bekannte (auch wenn das Spiel gerade nicht vorn
    /// ist), sonst der Hauptmonitor; ein zuletzt bekannter, der abgesteckt ist, zählt nicht. Vordergrund und Vollbild als Rechnung; die echten
    /// Abfragen lesen nur und werfen nicht.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Lage_ReglerAufMonitor_BegrenztAmRand_MonitorAutomatisch()
    {
        (int, int) E(int w, int s, Bildschirm b, int breite = 400, int hoehe = 50) => SprecheranzeigeFenster.Ecke(w, s, b, breite, hoehe);
        Pruefe.Gleich((0, 0), (SprecheranzeigeEinstellungen.LageAbWerk, SprecheranzeigeEinstellungen.LageAbWerk), "ab Werk 0 und 0 (Sarevoc 15.10.2026: ganz oben links)");
        Pruefe.Gleich((24, 24), E(0, 0, FullHd), "Full HD bei 0 und 0: 24 Bildpunkte vom Rand");
        Pruefe.Gleich((24 + 936, 24 + 516), E(50, 50, FullHd), "50 und 50: die Ecke in der Mitte zwischen den Rändern");
        Pruefe.Gleich((24 + 225, 24 + 310), E(12, 30, FullHd), "12 und 30: 12 % von 1872 und 30 % von 1032 (gerundet)");
        Pruefe.Gleich((1920 - 24 - 400, 1080 - 24 - 50), E(100, 100, FullHd), "100 und 100: so weit hinein, dass der Block mit 24 Rand im Monitor bleibt");
        Pruefe.Gleich((1496, 24), E(90, 0, FullHd), "90 %: die Ecke läge bei 1709, der Block ragte hinaus, er rückt hinein");
        Pruefe.Gleich((24, 1080 - 24 - 150), E(0, 100, FullHd, 400, 150), "drei Zeilen ganz unten: der ganze Block im Monitor");
        Pruefe.Gleich((20, 24), E(100, 0, FullHd, 1900), "fast so breit wie der Monitor: ganz darin, ohne Rand rechts");
        Pruefe.Gleich((0, 24), E(50, 0, FullHd, 3000), "breiter als der Monitor: links am Rand");
        Pruefe.Gleich(E(0, 0, FullHd), E(-5, -1, FullHd), "unter 0 zählt als 0");
        Pruefe.Gleich(E(100, 100, FullHd), E(150, 999, FullHd), "über 100 zählt als 100");
        Pruefe.Gleich((1920 + 36, 36), E(0, 0, Wqhd), "WQHD bei 150 %: 36 Bildpunkte Rand");
        Pruefe.Gleich((1920 + 2560 - 36 - 400, 36), E(100, 0, Wqhd), "WQHD ganz rechts");
        Pruefe.Gleich((-42 - 400, 2160 - 42 - 50), E(100, 100, Links4k), "4K bei 175 % links: 42 Bildpunkte Rand, Koordinaten negativ");
        var kaputt = new Bildschirm(new Pixelrechteck(0, 0, 1000, 500), new Pixelrechteck(0, 0, 1000, 500), double.NaN, true);
        Pruefe.Gleich((24, 24), E(0, 0, kaputt), "Skala NaN zählt als 1");

        Bildlauf.Ausfuehren(fenster =>
        {
            var anzeige = new Sprecheranzeige(new FunkDienstAttrappe(Lage()), () => new SprecheranzeigeEinstellungen(), () => 0);
            var wahl = new Sprecherlage(0, 0, null);
            Bildschirm? spiel = null;
            var f = new SprecheranzeigeFenster(anzeige, new FesteBildschirme([FullHd, Wqhd, Links4k]), () => wahl, () => spiel);
            fenster.Add(f);
            Pruefe.Gleich((24, 24, FullHd), f.ZielFuer(400, 50).GetValueOrDefault(), "Automatisch ohne je ein Spiel: Hauptmonitor, oben links");
            spiel = Wqhd;
            Pruefe.Gleich((1956, 36, Wqhd), f.ZielFuer(400, 50).GetValueOrDefault(), "Automatisch, Star Citizen vorn auf dem WQHD");
            spiel = null;
            Pruefe.Gleich((1956, 36, Wqhd), f.ZielFuer(400, 50).GetValueOrDefault(), "ein anderes Programm vorn: der zuletzt bekannte");
            wahl = new Sprecherlage(100, 100, 3);
            Pruefe.Gleich((-442, 2068, Links4k), f.ZielFuer(400, 50).GetValueOrDefault(), "fest Monitor 3");
            wahl = new Sprecherlage(0, 0, 0);
            spiel = Wqhd;
            Pruefe.Gleich((24, 24, FullHd), f.ZielFuer(400, 50).GetValueOrDefault(), "fest der Hauptmonitor: das Spiel zählt nicht");
            var ab = new SprecheranzeigeFenster(anzeige, new FesteBildschirme([FullHd]), () => new Sprecherlage(0, 0, null), () => spiel);
            fenster.Add(ab);
            Pruefe.Gleich((24, 24, FullHd), ab.ZielFuer(400, 50).GetValueOrDefault(), "zuletzt bekannter Monitor abgesteckt: der Hauptmonitor");
            var leer = new SprecheranzeigeFenster(anzeige, new FesteBildschirme([]));
            fenster.Add(leer);
            Pruefe.Wahr(leer.ZielFuer(400, 50) is null, "ohne Monitor kein Ziel");
        });

        Pruefe.Wahr(Spielfenster.ImVordergrund(7, new HashSet<uint> { 7, 9 }), "das Vordergrundfenster gehört dem Spiel");
        Pruefe.Falsch(Spielfenster.ImVordergrund(8, new HashSet<uint> { 7 }), "ein anderes Programm vorn");
        Pruefe.Falsch(Spielfenster.ImVordergrund(0, new HashSet<uint> { 0 }), "ohne Fenster vorn");
        Pruefe.Wahr(Spielfenster.IstExklusivesVollbild(3), "QUNS_RUNNING_D3D_FULL_SCREEN (3)");
        foreach (var z in new int?[] { null, 1, 2, 4, 5 }) Pruefe.Falsch(Spielfenster.IstExklusivesVollbild(z), "kein exklusives Vollbild: " + z);
        // Die echten Abfragen, nur lesend (Vordergrundfenster, Prozessliste, Benachrichtigungszustand): ohne Spiel null, sie werfen nie.
        _ = Spielfenster.MonitorImVordergrund();
        _ = Spielfenster.ExklusivesVollbild();
    }

    /// <summary>
    /// Das Fenster: WindowStyle None, durchsichtig, nie aktiviert (ShowActivated falsch), nicht in der Taskleiste, nicht fokussierbar, ohne Trefferprüfung,
    /// immer oben, ohne Owner; der erweiterte Stil NOACTIVATE, TRANSPARENT, TOOLWINDOW, LAYERED ohne APPWINDOW (Meldezeilenlage.Stil). Ohne Handle
    /// verborgen. Der Quelltext: Stil beim SourceInitialized, Lage nur per SetWindowPos mit HWND_TOPMOST und SWP_NOACTIVATE, beim Sichtbarwerden jedes
    /// Mal, kein Activate, kein Focus, kein SetForegroundWindow, kein Owner (auch nicht in App.xaml.cs); Spielfenster ohne Prozess-Handle.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Fenster_StileOhneOwner_NieAktiviert_Quelltext()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var anzeige = new Sprecheranzeige(new FunkDienstAttrappe(Lage()), () => new SprecheranzeigeEinstellungen(), () => 0);
            var f = new SprecheranzeigeFenster(anzeige);
            fenster.Add(f);
            Pruefe.Wahr(f.WindowStyle == WindowStyle.None && f.AllowsTransparency && !f.ShowActivated && !f.ShowInTaskbar && !f.Focusable && !f.IsHitTestVisible
                && f.Topmost && f.SizeToContent == SizeToContent.WidthAndHeight && f.ResizeMode == ResizeMode.NoResize, "die Eigenschaften des Overlays");
            Pruefe.Wahr(f.Owner is null, "kein Owner");
            Pruefe.Wahr(f.Background is SolidColorBrush { Color.A: 0 }, "keine Fläche");
            Pruefe.Falsch(f.IsVisible, "nicht gezeigt");
        });
        var stil = Meldezeilenlage.Stil(Meldezeilenlage.WsExAppwindow);
        Pruefe.Wahr((stil & Meldezeilenlage.WsExNoactivate) != 0 && (stil & Meldezeilenlage.WsExTransparent) != 0 && (stil & Meldezeilenlage.WsExToolwindow) != 0
            && (stil & Meldezeilenlage.WsExLayered) != 0 && (stil & Meldezeilenlage.WsExAppwindow) == 0, "NOACTIVATE, TRANSPARENT, TOOLWINDOW, LAYERED, ohne APPWINDOW");

        var code = File.ReadAllText(Quelle(@"src\Funk.Client\Fenster\SprecheranzeigeFenster.xaml.cs"));
        var xaml = File.ReadAllText(Quelle(@"src\Funk.Client\Fenster\SprecheranzeigeFenster.xaml"));
        var app = File.ReadAllText(Quelle(@"src\Funk.Client\App.xaml.cs"));
        var spiel = File.ReadAllText(Quelle(@"src\Funk.Client\Fenster\Spielfenster.cs"));
        Pruefe.Gleich("", string.Join("; ", Verstoesse(code)), "SprecheranzeigeFenster.xaml.cs");
        Pruefe.Wahr(Verstoesse(code + "\n        Activate();").Count > 0 && Verstoesse(code.Replace("SwpNosize | SwpNoactivate", "SwpNosize", StringComparison.Ordinal)).Count > 0
            && Verstoesse(code + "\n        Owner = this;").Count > 0, "Gegenproben: Activate, Lage ohne NOACTIVATE und ein Owner fielen auf");
        Pruefe.Wahr(xaml.Contains("ShowActivated=\"False\"", StringComparison.Ordinal) && xaml.Contains("IsHitTestVisible=\"False\"", StringComparison.Ordinal),
            "XAML: ohne Aktivieren, ohne Trefferprüfung");
        var verdrahtung = app[app.IndexOf("new SprecheranzeigeFenster(", StringComparison.Ordinal)..];
        verdrahtung = verdrahtung[..verdrahtung.IndexOf("var haupt = HauptfensterBauen();", StringComparison.Ordinal)];
        Pruefe.Falsch(Regex.IsMatch(verdrahtung, @"\bOwner\b|\.Activate\(|\.Focus\("), "App.xaml.cs setzt keinen Owner und aktiviert nicht");
        foreach (var verboten in new[] { "OpenProcess", "ReadProcessMemory", "MainModule", "HasExited", "WaitForExit", "SetForegroundWindow", "SetWindowsHookEx", "SendInput", "PrintWindow" })
            Pruefe.Falsch(spiel.Contains(verboten, StringComparison.Ordinal), "Spielfenster ohne " + verboten);
    }

    /// <summary>Was der Wächter im Quelltext des Fensters findet, ohne Kommentare (leer: nichts).</summary>
    private static List<string> Verstoesse(string quelltext)
    {
        var code = Regex.Replace(quelltext, @"//[^\n]*", "");
        var v = new List<string>();
        foreach (var verboten in new[] { "Activate(", ".Focus(", "SetForegroundWindow", "Owner =", "OpenProcess" })
            if (code.Contains(verboten, StringComparison.Ordinal)) v.Add(verboten);
        if (!Regex.IsMatch(code, @"SourceInitialized \+= \(_, _\) =>\s*\{[^}]*Meldezeilenlage\.Stil\(")) v.Add("Stil nicht beim SourceInitialized");
        foreach (Match m in Regex.Matches(code, @"\bSetWindowPos\(h,[^;]*;"))
            if (!m.Value.Contains("HwndTopmost", StringComparison.Ordinal) || !m.Value.Contains("SwpNoactivate", StringComparison.Ordinal)) v.Add("SetWindowPos ohne TOPMOST und NOACTIVATE");
        if (!Regex.IsMatch(code, @"LageSetzen\(nachOben: neu\)")) v.Add("beim Sichtbarwerden nicht nach oben");
        if (!code.Contains("Hide();", StringComparison.Ordinal)) v.Add("kein Hide");
        return v;
    }

    private static string Quelle(string relativ)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new PruefungException("TacRelay.slnx nicht gefunden"), relativ);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Einstellungen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Das Modell unter Einstellungen: ab Werk an, Lage 0 und 0 (ganz oben links), 100 %, 0 %, 1 Zeile, 1,0 s, 0,5 s, eigene Sendung aus, Automatisch.
    /// Jeder Schritt an den Reglern Waagerecht und Senkrecht merkt die Lage, zieht das Fenster um und zeigt die Probe (live beim Schieben); ein
    /// gleicher Wert nicht. Die Wahl des Monitors zeigt die Probe ebenso. Regler runden auf ihre Schritte und bleiben in den Grenzen. Gespeichert als
    /// Zahlen und Schalter (keine Namen einer Aufzählung), nachsichtig gelesen (Unsinn ab Werk); die Stelle aus der Zeit des Rasters wird in die
    /// Regler umgerechnet, solange sie auf 0 stehen, und nicht mehr geschrieben. Erweitert zählt die neun Werte (dazu die Lage, weil "Zurück auf
    /// Werkseinstellung" sie auf 0 und 0 setzt) und stellt sie zurück; der Schalter steht offen und zählt nicht. Die Vorschau folgt Zeilen, eigener Sendung, Größe
    /// und Durchsichtigkeit. Das exklusive Vollbild: höchstens alle 2 s geprüft, dann der Hinweis im Kasten des Abschnitts statt "Läuft von selbst".
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Einstellungen_Modell_AblageNachsichtig_Erweitert_Vollbild()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Bildschirme = [FullHd, Wqhd] };
        var vollbild = false;
        long ms = 0;
        var s = new SprecheranzeigeViewModel(a.Ablage, () => a.Bildschirme, () => vollbild, () => ms);
        var proben = 0;
        s.ProbeVerlangt += () => proben++;
        Pruefe.Wahr(s.An && s.Waagerecht == 0 && s.Senkrecht == 0 && s.Groesse == 100 && s.Durchsichtigkeit == 0 && s.Zeilen == 1 && s.Zeilen1 && !s.Zeilen2
            && s.Nachhalten == 1.0 && s.Ausblenden == 0.5 && !s.EigeneSendung && s.Monitor is null, "ab Werk");
        Pruefe.Gleich("0 %|0 %|100 %|0 %|1,0 s|0,5 s", string.Join("|", s.WaagerechtText, s.SenkrechtText, s.GroesseText, s.DurchsichtigkeitText, s.NachhaltenText,
            s.AusblendenText), "die Werte in Worten");
        Pruefe.Gleich(new Sprecherlage(0, 0, null), s.Wahl, "ab Werk ganz oben links, Automatisch");
        var wahlen = 0;
        s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SprecheranzeigeViewModel.Wahl)) wahlen++;
        };

        // Schieben wie mit der Maus: Schritt für Schritt, jeder zieht das Fenster um und zeigt die Probe.
        for (var w = 1; w <= 12; w++) s.Waagerecht = w;
        Pruefe.Wahr(s.Waagerecht == 12 && a.Ablage.Aktuell.Sprecheranzeige.Waagerecht == 12 && proben == 12 && wahlen == 12, T($"zwölf Schritte: gemerkt, je eine Probe ({proben}) und Lage ({wahlen})"));
        s.Waagerecht = 12.4;
        Pruefe.Gleich(12, proben, "derselbe Wert (gerundet): keine Probe");
        s.Senkrecht = 29.6;
        Pruefe.Wahr(s.Senkrecht == 30 && proben == 13 && s.SenkrechtText == "30 %" && s.WaagerechtText == "12 %", "Senkrecht 30 %, gerundet, mit Probe");
        Pruefe.Gleich(new Sprecherlage(12, 30, null), s.Wahl, "die Wahl für das Fenster");
        s.Waagerecht = double.NaN;
        s.Senkrecht = double.PositiveInfinity;
        Pruefe.Gleich(new Sprecherlage(12, 30, null), s.Wahl, "NaN und unendlich lassen stehen");
        s.Waagerecht = -5;
        s.Senkrecht = 130;
        Pruefe.Gleich(new Sprecherlage(0, 100, null), s.Wahl, "unter 0 wird 0, über 100 wird 100");
        s.Waagerecht = 12;
        s.Senkrecht = 30;
        proben = 0;
        Pruefe.Gleich(AutomatischText() + "|Hauptmonitor|Monitor 1: 1920 x 1080 (Hauptmonitor)|Monitor 2: 2560 x 1440", string.Join("|", s.Monitore.Select(m => m.Name)),
            "die Monitorliste wie bei der Meldezeile");
        s.GewaehlterMonitor = s.Monitore.Single(m => m.Wert == 2);
        Pruefe.Wahr(s.Monitor == 2 && proben == 1, "Monitor 2 mit Probe");

        s.Groesse = 133;
        s.Durchsichtigkeit = 12;
        s.Nachhalten = 1.26;
        s.Ausblenden = 9;
        s.Zeilen3 = true;
        s.Zeilen1 = false;
        s.EigeneSendung = true;
        Pruefe.Wahr(s.Groesse == 130 && s.Durchsichtigkeit == 10 && s.Nachhalten == 1.3 && s.Ausblenden == 2 && s.Zeilen == 3 && s.Zeilen3 && !s.Zeilen1,
            "Schritte und Grenzen: 130 %, 10 %, 1,3 s, 2,0 s, 3 Zeilen");
        s.Groesse = double.NaN;
        s.Groesse = 20;
        s.Zeilen = 9;
        Pruefe.Wahr(s.Groesse == 60 && s.Zeilen == 4, "NaN lässt stehen, darunter 60, darüber 4");
        s.Zeilen = 3;
        s.Groesse = 130;
        Pruefe.Gleich("Funker 2 Brücke (Schiff 1)\nFunker 3 Durchsage an alle\nDu -> Bodentrupp (Trupp 2)", s.Vorschau.Text,
            "die Vorschau: drei Zeilen mit eigener Sendung (wie im Bild vom 15.10.2026)");
        Pruefe.Wahr(Math.Abs(s.Vorschau.Schrift - (Sprecherbild.SchriftGrund * 1.3)) < 1e-9 && Math.Abs(s.Vorschau.Deckkraft - 0.9) < 1e-9
            && Math.Abs(s.Vorschau.Unschaerfe - (Sprecherbild.UnschaerfeGrund * 1.3)) < 1e-9, "die Vorschau in Größe und Durchsichtigkeit");

        // Gespeichert und nachsichtig gelesen.
        a.Ablage.Sichern();
        var text = File.ReadAllText(a.Ablage.Pfad);
        var abschnitt = Regex.Match(text, "\"sprecheranzeige\":\\s*\\{[^}]*\\}").Value;
        Pruefe.Wahr(Regex.IsMatch(abschnitt, "\"waagerecht\":\\s*12") && Regex.IsMatch(abschnitt, "\"senkrecht\":\\s*30") && !abschnitt.Contains("stelle", StringComparison.Ordinal)
            && Regex.IsMatch(abschnitt, "\"monitor\":\\s*2") && Regex.IsMatch(abschnitt, "\"groesse\":\\s*130")
            && Regex.IsMatch(abschnitt, "\"zeilen\":\\s*3") && Regex.IsMatch(abschnitt, "\"nachhaltenMs\":\\s*1300") && Regex.IsMatch(abschnitt, "\"eigeneSendung\":\\s*true"),
            "als Zahlen und Schalter gespeichert: " + abschnitt);
        Pruefe.Falsch(Regex.IsMatch(abschnitt, ":\\s*\"[A-Za-z]"), "keine Namen einer Aufzählung im Abschnitt");
        var gelesen = new EinstellungsSpeicher(a.Ablage.Pfad).Laden().Sprecheranzeige;
        Pruefe.Wahr(gelesen.Waagerecht == 12 && gelesen.Senkrecht == 30 && gelesen.Stelle is null && gelesen.Monitor == 2 && gelesen.Groesse == 130 && gelesen.Zeilen == 3 && gelesen.NachhaltenMs == 1300 && gelesen.EigeneSendung,
            "wieder gelesen");
        foreach (var (json, erwartet, was) in new (string, string, string)[]
                 {
                     ("{}", "True 0 0 - 100 0 1 1000 500 False", "ohne Abschnitt: ab Werk"),
                     ("{\"sprecheranzeige\":{\"an\":\"quatsch\",\"waagerecht\":101,\"senkrecht\":-1,\"monitor\":\"x\",\"groesse\":999,\"durchsichtigkeit\":95,\"zeilen\":0,\"nachhaltenMs\":-5,\"ausblendenMs\":9000,\"eigeneSendung\":7}}",
                         "True 0 0 - 100 0 1 1000 500 False", "Unsinn: ab Werk"),
                     ("{\"sprecheranzeige\":{\"an\":false,\"waagerecht\":\"100\",\"senkrecht\":33.6,\"monitor\":0,\"groesse\":60.4,\"durchsichtigkeit\":80,\"zeilen\":4,\"nachhaltenMs\":0,\"ausblendenMs\":2000,\"eigeneSendung\":1}}",
                         "False 100 34 0 60 80 4 0 2000 True", "Grenzen, Text mit Zahl, Kommazahl gerundet, 1 als an"),
                     ("{\"sprecheranzeige\":[1,2]}", "True 0 0 - 100 0 1 1000 500 False", "kein Objekt: ab Werk, die Datei bleibt lesbar"),
                     ("{\"sprecheranzeige\":{\"stelle\":5}}", "True 100 50 - 100 0 1 1000 500 False", "Stelle aus der Zeit des Rasters (Mitte rechts): 100 und 50"),
                     ("{\"sprecheranzeige\":{\"stelle\":7}}", "True 50 100 - 100 0 1 1000 500 False", "Stelle unten Mitte: 50 und 100"),
                     ("{\"sprecheranzeige\":{\"stelle\":8,\"waagerecht\":20}}", "True 20 0 - 100 0 1 1000 500 False", "Regler schon gestellt: die alte Stelle zählt nicht"),
                     ("{\"sprecheranzeige\":{\"stelle\":12}}", "True 0 0 - 100 0 1 1000 500 False", "Unsinn als alte Stelle: verworfen"),
                     ("{\"sprecheranzeige\":{\"stelle\":\"MitteRechts\"}}", "True 0 0 - 100 0 1 1000 500 False", "ein Name statt Zahl: verworfen, die Datei bleibt lesbar"),
                 })
        {
            var datei = a.Ordner.Datei("probe-" + Guid.NewGuid().ToString("N")[..6] + ".json");
            File.WriteAllText(datei, json);
            var speicher = new EinstellungsSpeicher(datei);
            var e = speicher.Laden().Sprecheranzeige;
            Pruefe.Gleich(erwartet, string.Join(" ", e.An, e.Waagerecht, e.Senkrecht, e.Monitor?.ToString(CultureInfo.InvariantCulture) ?? "-", e.Groesse, e.Durchsichtigkeit,
                e.Zeilen, e.NachhaltenMs, e.AusblendenMs, e.EigeneSendung), was);
            Pruefe.Wahr(e.Stelle is null, was + ": die alte Stelle wird nicht weitergeführt");
            if (!was.StartsWith("kein Objekt", StringComparison.Ordinal)) Pruefe.Gleich(null, speicher.LetzterFehler, was + ": die Datei ist lesbar");
        }

        // Erweitert: die neun Werte (mit der Lage, die "Zurück auf Werkseinstellung" auf 0 und 0 setzt), der Schalter offen und nicht gezählt.
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var abschnittVm = h.Einstellungen.AbschnittSprecheranzeige!;
        Pruefe.Gleich("Ich|Tasten|Empfangen|Senden|Funkfeld und Darstellung|Sprecheranzeige|Meldungen|Fehlersuche", string.Join("|", h.Einstellungen.Abschnitte.Select(x => x.Name)),
            "der Abschnitt nach Funkfeld und Darstellung (seit 0.12.0 folgt ihm Meldungen)");
        Pruefe.Gleich(SprecheranzeigeViewModel.AutomatikText, abschnittVm.KastenText, "der Kasten: Läuft von selbst");
        var teil = abschnittVm.Feinheiten;
        Pruefe.Gleich(Abschnittskennung.Sprecheranzeige, teil.Kennung, "Kennung");
        var vm = h.Einstellungen.Sprecheranzeige;
        Pruefe.Gleich("Waagerecht 12 %|Senkrecht 30 %|Größe 130 %|Durchsichtigkeit 10 %|höchstens 3 Zeilen|Nachhalten 1,3 s|Ausblenden 2,0 s|Eigene Sendung an|Monitor 2",
            string.Join("|", teil.SelbstGestellt), "Erweitert zählt die neun Werte (der Schalter nicht)");
        vm.An = false;
        Pruefe.Gleich(9, teil.AnzahlSelbstGestellt, "der Schalter zählt nicht");
        teil.ZurueckCommand.Execute(null);
        Pruefe.Gleich("", string.Join("|", teil.SelbstGestellt), "zurück auf Werkseinstellung");
        Pruefe.Wahr(vm.Groesse == 100 && vm.Durchsichtigkeit == 0 && vm.Zeilen == 1 && vm.Nachhalten == 1.0 && vm.Ausblenden == 0.5 && !vm.EigeneSendung && vm.Monitor is null,
            "alle sieben unter Erweitert ab Werk");
        Pruefe.Gleich(new Sprecherlage(0, 0, null), vm.Wahl, "zurück: die Lage auf 0 und 0 (ganz oben links)");
        Pruefe.Falsch(vm.An, "der Schalter bleibt, wie er ist (er steht offen)");
        funkfeld.Dispose();

        // Vollbild: höchstens alle 2 s geprüft, der Hinweis im Kasten.
        using var b = new Aufbau(FunkDienstAttrappe.Beispiel());
        var abfragen = 0;
        var v = new SprecheranzeigeViewModel(b.Ablage, () => [], () =>
        {
            abfragen++;
            return vollbild;
        }, () => ms);
        var ev = new EinstellungenViewModel(b.Ablage, b.Funkfeld(), sprecheranzeige: v);
        ev.AbschnitteBauen(b.Audio(), b.Netze(), "Klang: Attrappe", null);
        var kasten = ev.AbschnittSprecheranzeige!;
        v.Takt();
        Pruefe.Wahr(abfragen == 1 && !kasten.HatHinweis && kasten.KastenText == SprecheranzeigeViewModel.AutomatikText, "kein Vollbild: Läuft von selbst");
        vollbild = true;
        ms += SprecheranzeigeViewModel.VollbildAbstandMs - 1;
        v.Takt();
        Pruefe.Wahr(abfragen == 1 && !v.Vollbild, "vor 2 s nicht neu geprüft");
        ms += 1;
        v.Takt();
        Pruefe.Wahr(abfragen == 2 && v.Vollbild && kasten.HatHinweis, "nach 2 s: Vollbild");
        Pruefe.Gleich("Das Spiel läuft im Vollbild; die Anzeige erscheint nur bei Randlos", kasten.KastenText, "der Hinweis im Kasten (Vorgabe vom 15.10.2026, seit dem Spielprofil mit \"Das Spiel\")");
        vollbild = false;
        ms += SprecheranzeigeViewModel.VollbildAbstandMs;
        v.Takt();
        Pruefe.Wahr(!kasten.HatHinweis && kasten.KastenText == SprecheranzeigeViewModel.AutomatikText, "wieder randlos: Läuft von selbst");
    }

    private static string AutomatischText() => MeldezeilePlatzViewModel.AutomatischText;

    private static string T(FormattableString s) => s.ToString(De);

    // ------------------------------------------------------------------------------------------------------------------------------
    // Bildlauf
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Die zwei Regler der Lage: Name, Name des Werts daneben, Hovertext, Teil des Namens für Bildschirmleser.</summary>
    private static readonly (string Name, string Wert, string Hilfe, string Ziel)[] Lageregler =
    [
        ("SprecheranzeigeWaagerecht", "SprecheranzeigeWaagerechtWert", SprecheranzeigeViewModel.WaagerechtHilfe, "waagerecht"),
        ("SprecheranzeigeSenkrecht", "SprecheranzeigeSenkrechtWert", SprecheranzeigeViewModel.SenkrechtHilfe, "senkrecht"),
    ];

    /// <summary>Die vier Regler unter Erweitert.</summary>
    private static readonly string[] Regler = ["SprecheranzeigeGroesse", "SprecheranzeigeDurchsicht", "SprecheranzeigeNachhalten", "SprecheranzeigeAusblenden"];

    private static ContentPresenter Seite(Hauptfenster haupt) =>
        Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);

    private static T Teil<T>(FrameworkElement wurzel, string name) where T : FrameworkElement =>
        Bildlauf.Nachfahren<T>(wurzel).FirstOrDefault(e => e.Name == name) ?? throw new PruefungException(name + " fehlt");

    /// <summary>
    /// Der Abschnitt Sprecheranzeige im Bildlauf (kein Fenster auf dem Bildschirm), bei 1100 und 640 und bei 175 %: Schalter und unter "Wo im Bild" die
    /// Regler Waagerecht und Senkrecht offen (0 bis 100 in ganzen Prozent, Pfeiltasten 1, Bild auf und ab 10, mit Tastaturfokus, Wert "0 %" daneben,
    /// Hovertext, Namen für Bildschirmleser; kein Raster mehr), Erweitert aufgeklappt mit Größe, Durchsichtigkeit, Zeilen 1 bis 4, Nachhalten,
    /// Ausblenden, eigener Sendung, Monitor, der Vorschau auf dunklem Grund mit der Schrift und dem Schatten der Anzeige, dem Fuß. Nichts abgeschnitten,
    /// Schatten frei, Kontrast, Zugriffstasten weiter eindeutig. Ein Schieben am Regler setzt die Lage, der Wert daneben zieht nach, und die Probe
    /// kommt. Mit FUNK_BILDER das Bild
    /// vorschau-0119-sprecheranzeige-einstellungen.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Bildlauf_Einstellungen_Bild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            var satz = Farbvorschlaege.Von(Farbvorschlaege.Voreinstellung);
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Bildschirme = [FullHd, Wqhd] };
            using var k = OberflaecheStartseiteTests.Einrichten(a);
            OberflaecheStartseiteTests.SprechtasteBelegen(a);
            var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
            audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            h.MeinProfil.LadenAsync().GetAwaiter().GetResult();
            Seiten.Feinheiten(h, Abschnittskennung.Sprecheranzeige);
            h.Takt();
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            var s = h.Einstellungen.Sprecheranzeige;
            var proben = 0;
            s.ProbeVerlangt += () => proben++;

            foreach (var breite in new double[] { 1100, 640 })
            {
                var was = T($"{breite:0}");
                var bild = Ganz(haupt, breite);
                var seite = Seite(haupt);
                var abschnitt = Teil<StackPanel>(seite, "AbschnittSprecheranzeige");
                Pruefe.Wahr(ZuSehen(abschnitt), was + ": der Abschnitt");
                Pruefe.Falsch(Bildlauf.Nachfahren<RadioButton>(abschnitt).Any(r => r.GroupName == "Sprecheranzeigestelle"), was + ": kein Raster mehr");
                foreach (var (name, wert, hilfe, ziel) in Lageregler)
                {
                    var regler = Teil<Slider>(abschnitt, name);
                    Pruefe.Wahr(ZuSehen(regler) && regler.ActualWidth > 120, was + ": " + name + " zu sehen und breit genug");
                    Pruefe.Wahr(regler is { Minimum: 0, Maximum: 100, SmallChange: 1, LargeChange: 10, IsSnapToTickEnabled: true, Value: 0, Focusable: true, IsTabStop: true },
                        was + ": " + name + " von 0 bis 100 in ganzen Prozent, mit der Tastatur bedienbar");
                    Pruefe.Gleich(hilfe, Glasdesign.Hilfe.GetText(regler), was + ": " + name + " Hovertext");
                    Pruefe.Wahr(AutomationProperties.GetName(regler).Contains(ziel, StringComparison.Ordinal), was + ": " + name + " Name für Bildschirmleser");
                    Pruefe.Gleich("0 %", Teil<TextBlock>(abschnitt, wert).Text, was + ": " + name + " Wert daneben");
                }
                Pruefe.Wahr(Sichtbar(abschnitt, "Wo im Bild") && Sichtbar(abschnitt, "Waagerecht") && Sichtbar(abschnitt, "Senkrecht")
                    && Sichtbar(abschnitt, SprecheranzeigeViewModel.LageSatz[..30]), was + ": Beschriftungen und Satz");
                Pruefe.Wahr(Teil<CheckBox>(abschnitt, "SprecheranzeigeSchalter") is { IsChecked: true } schalter && ZuSehen(schalter), was + ": der Schalter an");
                Pruefe.Wahr(Sichtbar(abschnitt, SprecheranzeigeViewModel.AutomatikText[..40]), was + ": Läuft von selbst");
                foreach (var name in Regler)
                    Pruefe.Wahr(ZuSehen(Teil<Slider>(abschnitt, name)) && Glasdesign.Hilfe.GetText(Teil<Slider>(abschnitt, name)) is { Length: > 40 }, was + ": " + name);
                var zeilen = Bildlauf.Nachfahren<RadioButton>(Teil<WrapPanel>(abschnitt, "SprecheranzeigeZeilen")).ToList();
                Pruefe.Gleich("1|2|3|4", string.Join("|", zeilen.Select(r => r.Content)), was + ": Zeilen 1 bis 4");
                Pruefe.Wahr(zeilen[0].IsChecked == true && zeilen.Count(r => r.IsChecked == true) == 1, was + ": 1 Zeile gewählt");
                Pruefe.Wahr(ZuSehen(Teil<CheckBox>(abschnitt, "SprecheranzeigeEigene")) && Teil<CheckBox>(abschnitt, "SprecheranzeigeEigene").IsChecked == false, was + ": eigene Sendung aus");
                Pruefe.Wahr(Teil<ComboBox>(abschnitt, "SprecheranzeigeMonitorWahl") is { SelectedItem: Monitorwahl { Wert: null } } liste && ZuSehen(liste), was + ": Monitor Automatisch");
                var vorschau = Teil<Border>(abschnitt, "SprecheranzeigeVorschau");
                Pruefe.Wahr(ZuSehen(vorschau) && vorschau.Background is SolidColorBrush { Color: var grund } && grund.R < 0x30 && grund.R == grund.G && grund.G == grund.B,
                    was + ": die Vorschau auf neutralem dunklem Grund");
                var vorschauZeilen = Bildlauf.Nachfahren<TextBlock>(vorschau).Where(t => t.Name == "SprecherZeile").ToList();
                Pruefe.Gleich("Funker 2 Brücke (Schiff 1)", string.Join("|", vorschauZeilen.Select(Inhalt)), was + ": eine Zeile in der Vorschau (ab Werk 1)");
                var schrift = vorschauZeilen[0];
                Pruefe.Wahr(schrift.FontFamily.Source == "Segoe UI" && schrift.Foreground is SolidColorBrush { Color: var weiss } && weiss == Colors.White
                    && ((System.Windows.Documents.Run)schrift.Inlines.First()).FontWeight == FontWeights.Bold, was + ": Segoe UI, weiß, Name fett");
                var wurzel = Teil<ItemsControl>(vorschau, "SprecherZeilen");
                Pruefe.Wahr(wurzel.Effect is DropShadowEffect { Color: var schwarz, Opacity: 1 } schatten && schwarz == Colors.Black && Math.Abs(schatten.BlurRadius - 3.5) < 1e-9
                    && Math.Abs(schatten.ShadowDepth - 1.5) < 1e-9 && Math.Abs(wurzel.FontSize - 26) < 1e-9, was + ": Schrift 26 und der Schatten der Variante B");
                Pruefe.Wahr(Sichtbar(abschnitt, "Zurück auf Werkseinstellung") || Bildlauf.Nachfahren<Button>(abschnitt).Any(x => x.Name == "FeinheitenZurueck" && ZuSehen(x)), was + ": der Fuß");
                Pruefe.Wahr(NichtsAbgeschnitten(abschnitt, was) >= 30, was + ": genug geprüft");
                _ = OberflaecheStartseiteTests.SchattenFrei(abschnitt, was);
                if (breite == 1100)
                {
                    _ = KontrastPruefen(abschnitt, satz, was + ", Sprecheranzeige", mindestens: 10);
                    Bildablage.Ablegen(Ausschnitt(haupt, bild, "AbschnittSprecheranzeige"), "vorschau-0119-sprecheranzeige-einstellungen");
                }
                _ = Bildlauf.Zeichnen(haupt, 1.75, new Size(breite, bild.PixelHeight));
                _ = NichtsAbgeschnitten(Teil<StackPanel>(Seite(haupt), "AbschnittSprecheranzeige"), was + ", 175 %");
            }

            // Schieben wie mit Maus oder Tastatur: Der Regler setzt seinen Wert, die Bindung trägt ihn ins Modell; der Wert daneben zieht nach, jede Bewegung
            // zeigt die Probe.
            _ = Ganz(haupt, 1100);
            var waagerecht = Teil<Slider>(Seite(haupt), "SprecheranzeigeWaagerecht");
            foreach (var w in new double[] { 1, 2, 3, 25 }) waagerecht.Value = w;
            Bildlauf.Leeren();
            Pruefe.Wahr(s.Waagerecht == 25 && a.Ablage.Aktuell.Sprecheranzeige.Waagerecht == 25 && proben == 4, T($"Waagerecht auf 25: gemerkt, je Schritt eine Probe ({proben})"));
            var senkrecht = Teil<Slider>(Seite(haupt), "SprecheranzeigeSenkrecht");
            senkrecht.Value = 60;
            Bildlauf.Leeren();
            _ = Ganz(haupt, 1100);
            Pruefe.Wahr(s.Senkrecht == 60 && proben == 5, "Senkrecht auf 60 mit Probe");
            Pruefe.Gleich("25 %|60 %", Teil<TextBlock>(Seite(haupt), "SprecheranzeigeWaagerechtWert").Text + "|" + Teil<TextBlock>(Seite(haupt), "SprecheranzeigeSenkrechtWert").Text,
                "die Werte daneben ziehen nach");
            var stand = h.Einstellungen.AbschnittSprecheranzeige!.Feinheiten;
            Pruefe.Wahr(stand.SelbstGestellt.Contains("Waagerecht 25 %") && stand.SelbstGestellt.Contains("Senkrecht 60 %"), "Erweitert nennt die Lage");
            stand.ZurueckCommand.Execute(null);
            Bildlauf.Leeren();
            _ = Ganz(haupt, 1100);
            Pruefe.Wahr(Teil<Slider>(Seite(haupt), "SprecheranzeigeWaagerecht").Value == 0 && Teil<Slider>(Seite(haupt), "SprecheranzeigeSenkrecht").Value == 0,
                "Zurück auf Werkseinstellung: beide Regler auf 0");
            var tasten = OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt));
            Pruefe.Gleich("", string.Join(" | ", OberflaecheHauptfensterTests.Doppelte(tasten, "Sprecheranzeige offen")), "Zugriffstasten eindeutig");
            funkfeld.Dispose();
        });
    }

    /// <summary>
    /// Die Anzeige selbst, wie das Fenster sie zeichnet (ohne Handle, nichts auf dem Bildschirm): weiße Schrift ohne Fläche und ohne Rahmen, mit
    /// schwarzem, weichem Schatten; auf dunklem und auf hellem einfarbigem Grund, mit 1 und mit 3 Zeilen. Gemessen: Die Ecken sind durchsichtig (keine
    /// Fläche), in der Schrift gibt es weiße Bildpunkte, um sie dunkle des Schattens; 200 % ist doppelt so hoch wie 100 %, die Durchsichtigkeit
    /// nimmt Schrift und Schatten mit. Mit FUNK_BILDER das Bild vorschau-0119-sprecheranzeige-zeile.
    /// </summary>
    [Test]
    public static void Sprecheranzeige_Bildlauf_Zeile_DunkelUndHell_Bild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var lage = Lage();
            var e = new SprecheranzeigeEinstellungen { Zeilen = 3, EigeneSendung = true };
            long uhr = 0;
            IReadOnlyList<ushort>? eigene = null;
            var anzeige = new Sprecheranzeige(new FunkDienstAttrappe(lage), () => e, () => uhr, eigeneSendung: () => eigene);
            var f = new SprecheranzeigeFenster(anzeige);
            fenster.Add(f);

            RenderTargetBitmap Zeichnen() => Bildlauf.Zeichnen(f).Bild;
            anzeige.Melden([Netz(2, K(lage, "Brücke", 3))]);
            var eins = Zeichnen();
            Pruefe.Gleich("Falke/Tom Brücke (Schiff 3)", anzeige.Bild.Text, "eine Zeile");
            anzeige.Melden([Netz(2, K(lage, "Brücke", 3)), new GehoerterSprecher(3, Spruchart.Durchsage, [])]);
            eigene = [K(lage, "Crew", 3)];
            anzeige.Takt();
            var drei = Zeichnen();
            Pruefe.Gleich("Falke/Tom Brücke (Schiff 3)\nKobold Durchsage an alle\nDu -> Crew (Schiff 3)", anzeige.Bild.Text, "drei Zeilen mit eigener Sendung");
            var rand = (int)(anzeige.Bild.Innenrand.Top + anzeige.Bild.Innenrand.Bottom);
            Pruefe.Wahr(drei.PixelHeight - rand > (eins.PixelHeight - rand) * 2.8, "drei Zeilen dreimal so hoch (ohne den Rand für den Schatten): " + eins.PixelHeight + " / "
                + drei.PixelHeight);

            foreach (var (bild, was) in new[] { (eins, "1 Zeile"), (drei, "3 Zeilen") })
            {
                Pruefe.Gleich(0, (int)Bildlauf.Pixel(bild, 0, 0).A, was + ": Ecke oben links durchsichtig (keine Fläche, kein Rahmen)");
                Pruefe.Gleich(0, (int)Bildlauf.Pixel(bild, bild.PixelWidth - 1, bild.PixelHeight - 1).A, was + ": Ecke unten rechts durchsichtig");
                var (weiss, schatten, farbig) = Zaehlen(bild);
                Pruefe.Wahr(weiss > 50, was + ": weiße Schrift (" + weiss + ")");
                Pruefe.Wahr(schatten > weiss / 2, was + ": dunkler Schatten um die Schrift (" + schatten + ")");
                Pruefe.Gleich(0, farbig, was + ": keine Farben, nur Weiß, Grau und Schwarz");
            }

            // Größe und Durchsichtigkeit.
            e.Groesse = 200;
            anzeige.Takt();
            var gross = Zeichnen();
            Pruefe.Wahr(gross.PixelHeight > drei.PixelHeight * 1.8, "200 % fast doppelt so hoch: " + gross.PixelHeight);
            e.Groesse = 100;
            e.Durchsichtigkeit = 50;
            anzeige.Takt();
            var halb = Zeichnen();
            var (weissHalb, _, _) = Zaehlen(halb);
            Pruefe.Wahr(weissHalb == 0 && Math.Abs(Teil<ItemsControl>(Bildlauf.Wurzel(f), "SprecherZeilen").Opacity - 0.5) < 1e-9, "50 % durchsichtig: kein voll deckendes Weiß mehr");
            e.Durchsichtigkeit = 0;
            anzeige.Takt();

            // Der Bogen für Sarevoc: dunkel und hell, 1 und 3 Zeilen.
            var dunkel = Color.FromRgb(0x1A, 0x1D, 0x24);
            var hell = Color.FromRgb(0xE6, 0xE9, 0xEE);
            const double Bw = 620;
            const double Bh = 190;
            var bogen = new Bogen((Bw * 2) + 60, (Bh * 2) + 110, 1, Color.FromRgb(0x2B, 0x2B, 0x2B));
            bogen.Text("Sprecheranzeige 0.11.9 (Variante B): weiße Schrift, Name fett, weicher schwarzer Schatten, keine Fläche", 20, 12, 15, Colors.White, fett: true);
            var felder = new[] { (eins, dunkel, "dunkler Grund, 1 Zeile (ab Werk)"), (eins, hell, "heller Grund, 1 Zeile (ab Werk)"),
                (drei, dunkel, "dunkler Grund, 3 Zeilen mit eigener Sendung"), (drei, hell, "heller Grund, 3 Zeilen mit eigener Sendung") };
            for (var i = 0; i < felder.Length; i++)
            {
                var (bild, grund, text) = felder[i];
                var x = 20 + ((i % 2) * (Bw + 20));
                var y = 44 + ((i / 2) * (Bh + 30));
                bogen.Zeichnung(dc => dc.DrawRectangle(Bogen.Pinsel(grund), null, new Rect(x, y, Bw, Bh)));
                bogen.Bild(bild, x + 24 - anzeige.Bild.Innenrand.Left, y + 24 - anzeige.Bild.Innenrand.Top, bild.PixelWidth, bild.PixelHeight);
                bogen.Text(text, x, y + Bh + 4, 12, Color.FromRgb(0xC8, 0xC8, 0xC8));
            }
            Bildablage.Ablegen(bogen.Fertig(), "vorschau-0119-sprecheranzeige-zeile");
        });
    }

    /// <summary>Zählt deckend weiße Bildpunkte, dunkle des Schattens (deckend genug, dunkel) und farbige (Sättigung).</summary>
    private static (int Weiss, int Schatten, int Farbig) Zaehlen(BitmapSource bild)
    {
        var breite = bild.PixelWidth;
        var px = new byte[breite * bild.PixelHeight * 4];
        bild.CopyPixels(px, breite * 4, 0);
        int weiss = 0, schatten = 0, farbig = 0;
        for (var i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a == 0) continue;
            int Z(byte v) => (int)Math.Round(v * 255.0 / a);
            int b = Z(px[i]), g = Z(px[i + 1]), r = Z(px[i + 2]);
            if (a == 255 && r > 245 && g > 245 && b > 245) weiss++;
            else if (a > 100 && r < 60 && g < 60 && b < 60) schatten++;
            if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 24 && a > 40) farbig++;
        }
        return (weiss, schatten, farbig);
    }

    /// <summary>Ein Ausschnitt um einen Abschnitt der Einstellungen über die Breite der Karte, aus einem Bild der ganzen Seite in Skala 1.</summary>
    private static CroppedBitmap Ausschnitt(Hauptfenster haupt, BitmapSource ganz, string abschnittName)
    {
        var wurzel = Bildlauf.Wurzel(haupt);
        var seite = Seite(haupt);
        var karte = Teil<Border>(seite, "AbschnitteKarte");
        var rk = karte.TransformToAncestor(wurzel).TransformBounds(new Rect(karte.RenderSize));
        var abschnitt = Teil<StackPanel>(seite, abschnittName);
        var r = abschnitt.TransformToAncestor(wurzel).TransformBounds(new Rect(abschnitt.RenderSize));
        var links = (int)Math.Max(0, Math.Floor(rk.Left - 8));
        var oben = (int)Math.Max(0, Math.Floor(r.Top - 14));
        return new CroppedBitmap(ganz, new Int32Rect(links, oben, (int)Math.Min(ganz.PixelWidth - links, Math.Ceiling(rk.Width + 16)),
            (int)Math.Min(ganz.PixelHeight - oben, Math.Ceiling(r.Height + 28))));
    }
}
