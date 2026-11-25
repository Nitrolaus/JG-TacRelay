// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Client.Messung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Messwerkzeug für das echte Funkfeld (A32: <c>TacRelay.exe --funkfeld-messen</c>, dazu tools\funkfeld-messen.ps1).
/// Es öffnet ein sichtbares Fenster und wird darum hier nie gestartet; geprüft werden nur die Argumente, die Sperren
/// (Freigabe, Ablageort unter C:\Claude, Spiel) und die Auswertung, als reine Rechnung, dazu am Quelltext, dass vor dem
/// Öffnen alles geprüft ist und nur Attrappen im Spiel sind.
/// </summary>
internal static class OberflaecheMesswerkzeugTests
{
    private const string Bericht = @"C:\Claude\tmp\tr-funkfeld-messen\probe\bericht.json";

    private static (FunkfeldMessauftrag? Auftrag, int Code, string? Grund, string? Bericht) Lesen(params string[] args) => FunkfeldMessauftrag.Lesen(args);

    [Test]
    public static void Messwerkzeug_ArgumenteUndSperren()
    {
        var (auftrag, code, grund, _) = Lesen("--funkfeld-messen", Bericht, "--sichtbar-freigegeben");
        Pruefe.Gleich(new FunkfeldMessauftrag(Bericht, FunkfeldDarstellung.Raster, 10, 3), auftrag, "Voreinstellung: Raster, 10 Felder, 3 Sekunden; " + grund);
        Pruefe.Gleich(0, code, "angenommen");
        Pruefe.Gleich(@"C:\Claude\tmp\tr-funkfeld-messen\probe\bericht.png", auftrag!.Bild, "das Bild neben dem Bericht");
        Pruefe.Gleich(@"C:\Claude\tmp\tr-funkfeld-messen\probe\bericht-einstellungen.json", auftrag.Einstellungen, "die Wegwerf-Einstellungen neben dem Bericht");
        (auftrag, code, _, _) = Lesen("--vorfuehrung", "--funkfeld-messen", Bericht, "--ansicht", "Liste", "--felder", "5", "--sekunden", "7", "--sichtbar-freigegeben");
        Pruefe.Gleich(new FunkfeldMessauftrag(Bericht, FunkfeldDarstellung.Liste, 5, 7), auftrag, "alle Schalter, --vorfuehrung erlaubt");

        // Ohne Freigabe nichts, der Grund darf aber in den Bericht (er liegt in der Zone).
        (auftrag, code, grund, var bericht) = Lesen("--funkfeld-messen", Bericht);
        Pruefe.Wahr(auftrag is null && code == FunkfeldMessauftrag.CodeNichtFreigegeben && bericht == Bericht, "ohne --sichtbar-freigegeben: Code 5");
        Pruefe.Wahr(grund!.Contains("Sarevoc", StringComparison.Ordinal), "der Grund nennt, wer freigibt: " + grund);
        OberflaecheFunkfeldAnsichtenTests.NurEinfacheZeichen(grund, "Grund");

        // Ablageort nur unter C:\Claude, nur .json.
        foreach (var pfad in new[] { @"C:\Users\Mustermann\bericht.json", @"bericht.json", @"C:\Claude\..\Windows\bericht.json", @"C:\Claude\tmp\bericht.txt", @"C:\ClaudeX\b.json", @"\\server\c$\Claude\b.json" })
        {
            (auftrag, code, _, bericht) = Lesen("--funkfeld-messen", pfad, "--sichtbar-freigegeben");
            Pruefe.Wahr(auftrag is null && code == FunkfeldMessauftrag.CodeAusserhalbZone && bericht is null, "außerhalb der Zone: Code 6 für " + pfad);
        }
        (auftrag, code, _, bericht) = Lesen("--funkfeld-messen", @"C:\Users\x\b.json");
        Pruefe.Wahr(code == FunkfeldMessauftrag.CodeNichtFreigegeben && bericht is null, "ohne Freigabe und außerhalb: nichts wird geschrieben");

        // Unbrauchbare Argumente: Code 2, nichts wird geöffnet.
        foreach (var falsch in new[]
                 {
                     new[] { "--funkfeld-messen" }, ["--funkfeld-messen", "--sichtbar-freigegeben"], ["--sichtbar-freigegeben"],
                     ["--funkfeld-messen", Bericht, "--ansicht", "leiste", "--sichtbar-freigegeben"], ["--funkfeld-messen", Bericht, "--felder", "7", "--sichtbar-freigegeben"],
                     ["--funkfeld-messen", Bericht, "--sekunden", "0", "--sichtbar-freigegeben"], ["--funkfeld-messen", Bericht, "--sekunden", "31", "--sichtbar-freigegeben"],
                     ["--funkfeld-messen", Bericht, "--sekunden", "drei", "--sichtbar-freigegeben"], ["--funkfeld-messen", Bericht, "--stumm", "--sichtbar-freigegeben"],
                     ["--funkfeld-messen", Bericht, "--testzugang", "127.0.0.1:1", "--sichtbar-freigegeben"], ["--funkfeld-messen", Bericht, "--felder", "--sichtbar-freigegeben"],
                     ["--funkfeld-messen", Bericht, "--funkfeld-messen", Bericht, "--sichtbar-freigegeben"],
                 })
        {
            (auftrag, code, grund, _) = Lesen(falsch);
            Pruefe.Wahr(auftrag is null && code == FunkfeldMessauftrag.CodeArgumente && grund is { Length: > 0 }, "Code 2 für: " + string.Join(" ", falsch));
        }
        Pruefe.Wahr(FunkfeldMessung.Verlangt(["--vorfuehrung", "--funkfeld-messen", Bericht]) && !FunkfeldMessung.Verlangt(["--vorfuehrung"]), "nur mit dem Schalter");
    }

    [Test]
    public static void Messwerkzeug_Spielsperre()
    {
        Pruefe.Gleich("StarCitizen_Launcher läuft", Spielsperre.Grund(["explorer", "StarCitizen_Launcher"], null), "Launcher");
        Pruefe.Gleich("starcitizen läuft", Spielsperre.Grund(["starcitizen"], 5), "ohne Rücksicht auf Groß- und Kleinschreibung");
        foreach (var zustand in new[] { 2, 3, 4 })
            Pruefe.Wahr(Spielsperre.Grund(["explorer"], zustand)?.Contains("Vollbild", StringComparison.Ordinal) == true, "Vollbild " + zustand);
        Pruefe.Gleich(null, Spielsperre.Grund(["explorer", "pwsh"], 5), "kein Spiel, Benachrichtigung normal");
        Pruefe.Gleich(null, Spielsperre.Grund([], null), "nichts bekannt");
        Pruefe.Wahr(Spielsperre.Spiele.Contains("StarCitizen"), "Star Citizen steht in der Liste, wie in Spielschutz.cs");
    }

    private static readonly string[] Berichtsnamen =
    [
        "\"bildschirme\"", "\"bereich\"", "\"arbeit\"", "\"rechts\"", "\"unten\"", "\"skala\"", "\"haupt\"", "\"erwartet\"", "\"breitePx\"", "\"hoehePx\"",
        "\"breiteEinheiten\"", "\"wunschBreite\"", "\"beimZeigen\"", "\"nachgefuehrt\"", "\"proben\"", "\"sizeToContent\"", "\"rasterAngebotBreite\"",
        "\"ergebnis\"", "\"waechst\"", "\"endeBreitePx\"", "\"erwartetBreitePx\"", "\"abweichungPx\"", "\"passt\"", "\"bild\"", "\"abbruch\"",
    ];

    [Test]
    public static void Messwerkzeug_Auswertung()
    {
        static Messprobe P(long ms, int b, int h) => new(ms, 42, 42, b, h, b / 1.75, h / 1.75, 168, 0, "Manual", 766, -1, 766, 160, 1);
        var ruhig = new[] { P(0, 1358, 338), P(100, 1358, 338), P(200, 1358, 338) };
        var e = Messergebnis.Auswerten(ruhig, 1358, 338);
        Pruefe.Wahr(!e.Waechst && e.Passt && e.AbweichungPx == 0, "stabil in der erwarteten Größe: passt");
        var alt = new[] { P(0, 1358, 338), P(100, 7708, 2188), P(200, 7708, 2188) };
        e = Messergebnis.Auswerten(alt, 1358, 338);
        Pruefe.Wahr(e.Waechst && !e.Passt && e.EndeBreitePx == 7708, "der alte Befund: wächst, passt nicht");
        e = Messergebnis.Auswerten([P(0, 7708, 2188), P(100, 7708, 2188)], 1358, 338);
        Pruefe.Wahr(!e.Waechst && !e.Passt && e.AbweichungPx == 7708 - 1358, "gleich zu groß: wächst nicht, passt aber nicht");
        e = Messergebnis.Auswerten([P(0, 1358, 338), P(100, 1359, 340)], 1358, 338);
        Pruefe.Wahr(!e.Waechst && e.Passt, "zwei Bildpunkte Spiel");
        e = Messergebnis.Auswerten([], 1358, 338);
        Pruefe.Falsch(e.Passt, "ohne Probe passt nichts");

        // Der Bericht trägt die Namen, die tools\funkfeld-messen.ps1 liest.
        var bildschirm = new Bildschirm(new Pixelrechteck(0, 0, 3840, 2160), new Pixelrechteck(0, 0, 3840, 2076), 1.75, true);
        var bericht = new Messbericht("2026-09-30 12:00:00", new FunkfeldMessauftrag(Bericht, FunkfeldDarstellung.Raster, 10, 3), [bildschirm],
            new Messerwartung(42, 42, 1358, 338, 776, 193.14, 1.75, 776, 192.6), new Pixelrechteck(42, 42, 1358, 338), false, null, ruhig,
            Messergebnis.Auswerten(ruhig, 1358, 338), null);
        var json = System.Text.Json.JsonSerializer.Serialize(bericht, FunkfeldMessung.Json);
        foreach (var name in Berichtsnamen)
            Pruefe.Wahr(json.Contains(name, StringComparison.Ordinal), "Bericht mit " + name);
        Pruefe.Wahr(json.Contains("\"darstellung\": \"Raster\"", StringComparison.Ordinal), "Ansicht als Text");
    }

    /// <summary>
    /// Am Quelltext: Die App prüft den Schalter vor allem anderen (vor Einstellungen im Profil, Klang und Tasten); die Messung
    /// prüft Auftrag und Spiel, bevor ein Fenster aufgeht, öffnet es ohne Fokus, benutzt nur Attrappen, schreibt Einstellungen
    /// nur neben den Bericht und nimmt das Fenster per PrintWindow auf, nie den Bildschirm. Das Skript startet nur mit
    /// -SichtbarFreigegeben und ohne Spiel.
    /// </summary>
    [Test]
    public static void Messwerkzeug_ErstPruefenDannOeffnen()
    {
        var app = File.ReadAllText(Datei(@"src\Funk.Client\App.xaml.cs"));
        var verlangt = app.IndexOf("FunkfeldMessung.Verlangt(args)", StringComparison.Ordinal);
        Pruefe.Wahr(verlangt > 0 && verlangt < app.IndexOf("EinstellungsSpeicher.StandardPfad()", StringComparison.Ordinal)
            && verlangt < app.IndexOf("MaschineErzeugen(", verlangt, StringComparison.Ordinal) && verlangt < app.IndexOf("TastenFuerStart(", verlangt, StringComparison.Ordinal),
            "die App fragt nach der Messung, bevor Einstellungen, Klang oder Tasten angefasst werden");
        // Vor der Einzelinstanz: Sonst gälte eine Messung bei laufendem JG TacRelay als zweiter Start und bäte dessen
        // Hauptfenster nach vorn (beim Zusammenführen von setup und funkfeld3 am 30.09.2026 so entstanden).
        var einzel = app.IndexOf("Einzelinstanz.ProgrammStart(", StringComparison.Ordinal);
        Pruefe.Wahr(einzel > 0 && verlangt < einzel, "die Messung zweigt vor der Einzelinstanz ab");

        var messung = File.ReadAllText(Datei(@"src\Funk.Client\Messung\FunkfeldMessung.cs"));
        var lesen = messung.IndexOf("FunkfeldMessauftrag.Lesen(args)", StringComparison.Ordinal);
        var spiel = messung.IndexOf("Spielsperre.Jetzt()", StringComparison.Ordinal);
        var lauf = messung.IndexOf("new Messlauf(", StringComparison.Ordinal);
        Pruefe.Wahr(lesen > 0 && spiel > lesen && lauf > spiel, "erst Auftrag, dann Spiel, dann der Lauf");
        // Die Suchtexte zusammengesetzt, damit SicherungTests sie nicht selbst für Aufrufe hält.
        var zeigen = messung.IndexOf("_fenster.Show" + "()", StringComparison.Ordinal);
        Pruefe.Wahr(zeigen > messung.IndexOf("ShowActivated = false", StringComparison.Ordinal) && messung.IndexOf("ShowActivated = false", StringComparison.Ordinal) > 0,
            "ohne Fokus geöffnet");
        foreach (var verboten in new[] { "new KlangMaschine" + "(", "RawInputSprechtaste", "DirectInput", "FunkClientDienst", "Verbindungsfabrik", "StandardPfad()", "CopyFromScreen", "GetDC(", "BitBlt" })
            Pruefe.Falsch(messung.Contains(verboten, StringComparison.Ordinal), "die Messung benutzt nicht: " + verboten);
        foreach (var noetig in new[] { "new KlangMaschinenAttrappe(vorfuehrung: true)", "new FunkDienstAttrappe(", "auftrag.Einstellungen", "PwRenderfullcontent = 2", "Spielsperre.Jetzt()" })
            Pruefe.Wahr(messung.Contains(noetig, StringComparison.Ordinal), "die Messung hat: " + noetig);
        Pruefe.Wahr(messung.IndexOf("Spielsperre.Jetzt()", messung.IndexOf("private void Probe()", StringComparison.Ordinal), StringComparison.Ordinal) > 0,
            "auch während der Messung: bei einem Spiel sofort schließen");

        var skript = File.ReadAllText(Datei(@"tools\funkfeld-messen.ps1"));
        var sperre = skript.IndexOf("if (-not $SichtbarFreigegeben)", StringComparison.Ordinal);
        var start = skript.IndexOf("Start-Process", StringComparison.Ordinal);
        Pruefe.Wahr(sperre > 0 && start > sperre && skript.IndexOf("exit 5", sperre, StringComparison.Ordinal) < start, "Skript: ohne -SichtbarFreigegeben Exit 5 vor dem Start");
        Pruefe.Wahr(skript.IndexOf("if ($grund = Spielgrund)", StringComparison.Ordinal) is var s && s > sperre && s < start, "Skript: Spiel geprüft vor dem Start");
        Pruefe.Wahr(skript.Contains("'--sichtbar-freigegeben'", StringComparison.Ordinal) && skript.Contains("'--vorfuehrung'", StringComparison.Ordinal),
            "Skript: reicht die Freigabe weiter, nur mit den Attrappen");
        Pruefe.Wahr(skript.Contains(@"StartsWith('C:\Claude\'", StringComparison.Ordinal), "Skript: Bericht nur unter C:\\Claude");
    }

    private static string Datei(string relativ)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden."), relativ);
    }
}
