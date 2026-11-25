// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Text.RegularExpressions;
using Funk.Client;

namespace Funk.Tests;

/// <summary>
/// Nur eine Instanz je Einstellungsdatei (src\Funk.Client\Einzelinstanz.cs), ohne Fenster: zwei Anmeldungen im selben
/// Testprozess mit gleichem und mit anderem Pfad, das Signal an die erste und ihre Antwort, eine erste, die gerade endet
/// (der zweite Start übernimmt), eine, die nicht antwortet, und die Marke für das Setup. Die Tests rufen <c>Anmelden</c> mit
/// <c>vordergrundErlauben: false</c> und eigener Marke: Sie fassen weder die Vordergrundregeln von Windows noch den Mutex
/// an, den das Setup prüft, und kein Fenster.
/// </summary>
internal static class EinzelinstanzTests
{
    /// <summary>Wie lange ein Signal höchstens braucht, auch mit niedriger Priorität neben einem Spiel.</summary>
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(15);

    /// <summary>Ein eigener Pfad je Test; die Datei muss nicht existieren, sie wird nur gehasht.</summary>
    private static string Pfad(string datei) =>
        Path.Combine(Testserver.Wurzel(), "einzelinstanz-" + Guid.NewGuid().ToString("N"), datei);

    private static string Marke() => @"Local\JG-TacRelay-Test-" + Guid.NewGuid().ToString("N");

    private static bool NieGerufen() => throw new PruefungException("diese Anmeldung horcht nicht");

    [Test]
    public static void Einzelinstanz_Name_AusDemPfadOhneGrossKlein()
    {
        const string Normal = @"C:\Users\Test\AppData\Local\JG-TacRelay\client-einstellungen.json";
        const string Probe = @"C:\Users\Test\AppData\Local\JG-TacRelay\probe\probe-einstellungen.json";
        // Feste Werte: Ändert sich die Bildung, liefen während einer Aktualisierung alte und neue Fassung nebeneinander.
        Pruefe.Gleich(@"Local\JG-TacRelay-Instanz-B198004B5CA98246", Einzelinstanz.MutexName(Normal), "Mutex des Normalbetriebs");
        Pruefe.Gleich(@"Local\JG-TacRelay-Instanz-C62F09A2751617FF", Einzelinstanz.MutexName(Probe), "Mutex des Probefunks");
        Pruefe.Gleich(Einzelinstanz.MutexName(Normal), Einzelinstanz.MutexName(Normal.ToLowerInvariant()), "Groß und klein zählen nicht");
        Pruefe.Gleich(Einzelinstanz.MutexName(Normal), Einzelinstanz.MutexName(@"C:\Users\Test\AppData\Local\JG-TacRelay\probe\..\client-einstellungen.json"),
            "vollständiger Pfad, .. aufgelöst");
        Pruefe.Wahr(Einzelinstanz.MutexName(Normal) != Einzelinstanz.MutexName(Probe), "Normalbetrieb und Probefunk haben verschiedene Namen");
        Pruefe.Wahr(Regex.IsMatch(Einzelinstanz.MutexName(Normal), @"^Local\\JG-TacRelay-Instanz-[0-9A-F]{16}$", RegexOptions.None, TimeSpan.FromSeconds(1)),
            "Mutex im Namensraum der Sitzung");
        using var ich = Process.GetCurrentProcess();
        Pruefe.Gleich("JG-TacRelay-Instanz-B198004B5CA98246-" + ich.SessionId, Einzelinstanz.PipeName(Normal), "Pipe mit Sitzungsnummer");
        Pruefe.Wirft<ArgumentException>(() => Einzelinstanz.Kennung(" "), "leerer Pfad");
        Pruefe.Falsch(Einzelinstanz.FensterZeigen(null), "ohne Hauptfenster nicht gezeigt");
        Pruefe.Wahr(Einzelinstanz.StandardFrist > TimeSpan.FromSeconds(5) + Einzelinstanz.OberflaechenFrist,
            "die Frist eines zweiten Starts überdauert das Beenden in App.OnExit (bis 5 s)");
    }

    [Test]
    public static void Einzelinstanz_GleicherPfad_ZweiteBittetDieErsteUndEndet()
    {
        var pfad = Pfad("client-einstellungen.json");
        using var gerufen = new SemaphoreSlim(0);
        var erste = Einzelinstanz.Anmelden(pfad, () => { gerufen.Release(); return true; }, vordergrundErlauben: false, marke: Marke());
        Pruefe.Wahr(erste.Instanz is not null, "die erste Anmeldung bekommt die Instanz");
        using (erste.Instanz)
        {
            var zweite = Einzelinstanz.Anmelden(pfad, NieGerufen, vordergrundErlauben: false, marke: Marke(), frist: Frist);
            Pruefe.Wahr(zweite.Instanz is null, "die zweite Anmeldung mit demselben Pfad bekommt keine Instanz");
            Pruefe.Wahr(zweite.Gemeldet, "die erste hat ihr Fenster gezeigt und das geantwortet");
            Pruefe.Wahr(gerufen.Wait(Frist), "das Signal kommt bei der ersten an");

            // Dieselbe Datei in anderer Schreibweise, und ein zweites Mal: Die erste horcht weiter.
            var dritte = Einzelinstanz.Anmelden(pfad.ToUpperInvariant(), NieGerufen, vordergrundErlauben: false, marke: Marke(), frist: Frist);
            Pruefe.Wahr(dritte.Instanz is null && dritte.Gemeldet, "auch der dritte Start bittet die erste");
            Pruefe.Wahr(gerufen.Wait(Frist), "das zweite Signal kommt an");
            Pruefe.Falsch(gerufen.Wait(TimeSpan.FromMilliseconds(200)), "je Start genau ein Signal");
        }

        // Nach dem Ende der ersten ist der Name frei.
        var danach = Einzelinstanz.Anmelden(pfad, () => true, vordergrundErlauben: false, marke: Marke());
        Pruefe.Wahr(danach.Instanz is not null, "nach dem Beenden startet wieder eine erste");
        danach.Instanz!.Dispose();
    }

    [Test]
    public static void Einzelinstanz_AndererPfad_LaeuftDaneben()
    {
        var normal = Pfad("client-einstellungen.json");
        var probe = Pfad(@"probe\probe-einstellungen.json");
        using var normalGerufen = new SemaphoreSlim(0);
        using var probeGerufen = new SemaphoreSlim(0);
        var a = Einzelinstanz.Anmelden(normal, () => { normalGerufen.Release(); return true; }, vordergrundErlauben: false, marke: Marke());
        var b = Einzelinstanz.Anmelden(probe, () => { probeGerufen.Release(); return true; }, vordergrundErlauben: false, marke: Marke());
        using (a.Instanz)
        using (b.Instanz)
        {
            Pruefe.Wahr(a.Instanz is not null && b.Instanz is not null, "zwei Einstellungsdateien, zwei Instanzen (Probefunk neben dem Client)");
            var zweiteProbe = Einzelinstanz.Anmelden(probe, NieGerufen, vordergrundErlauben: false, marke: Marke(), frist: Frist);
            Pruefe.Wahr(zweiteProbe.Instanz is null && zweiteProbe.Gemeldet, "ein zweiter Probefunk bittet den ersten");
            Pruefe.Wahr(probeGerufen.Wait(Frist), "das Signal geht an den Probefunk");
            Pruefe.Falsch(normalGerufen.Wait(TimeSpan.FromMilliseconds(300)), "der normale Client bekommt es nicht");
        }
    }

    [Test]
    public static void Einzelinstanz_ErsteEndetGerade_ZweiteUebernimmt()
    {
        // Die erste hat kein Fenster mehr (App.OnExit läuft) und antwortet "nicht gezeigt"; kurz darauf gibt sie den Namen
        // frei. Der zweite Start darf dabei nicht still verloren gehen, sondern wird selbst die erste.
        var pfad = Pfad("client-einstellungen.json");
        var gefragt = 0;
        var erste = Einzelinstanz.Anmelden(pfad, () => { Interlocked.Increment(ref gefragt); return false; }, vordergrundErlauben: false, marke: Marke());
        Pruefe.Wahr(erste.Instanz is not null, "die erste Anmeldung bekommt die Instanz");
        var beenden = Task.Run(async () =>
        {
            await Task.Delay(700).ConfigureAwait(false);
            erste.Instanz!.Dispose();
        });
        var uhr = Stopwatch.StartNew();
        var zweite = Einzelinstanz.Anmelden(pfad, () => true, vordergrundErlauben: false, marke: Marke(), frist: Frist);
        uhr.Stop();
        Pruefe.Wahr(beenden.Wait(Frist), "die erste ist beendet");
        Pruefe.Wahr(zweite.Instanz is not null, "der zweite Start übernimmt, sobald der Name frei ist");
        Pruefe.Falsch(zweite.Gemeldet, "keine Meldung: die erste hat nichts gezeigt");
        Pruefe.Wahr(Volatile.Read(ref gefragt) >= 1, "die erste wurde gefragt: " + gefragt);
        Pruefe.Wahr(uhr.Elapsed < Frist, "ohne auf die ganze Frist zu warten: " + uhr.Elapsed);
        zweite.Instanz!.Dispose();
    }

    [Test]
    public static void Einzelinstanz_ErsteAntwortetNicht_ZweiteMeldetDas()
    {
        var pfad = Pfad("client-einstellungen.json");
        // Eine "erste Instanz", die den Mutex hält, aber nicht horcht (etwa eine hängende).
        using (new Mutex(false, Einzelinstanz.MutexName(pfad)))
        {
            var uhr = Stopwatch.StartNew();
            var zweite = Einzelinstanz.Anmelden(pfad, NieGerufen, vordergrundErlauben: false, marke: Marke(), frist: TimeSpan.FromMilliseconds(600));
            Pruefe.Wahr(zweite.Instanz is null, "keine zweite Instanz, auch wenn die erste nicht antwortet");
            Pruefe.Falsch(zweite.Gemeldet, "nicht gemeldet: das Programm sagt es dann im Meldungsfenster");
            Pruefe.Wahr(uhr.Elapsed < Frist, "die Frist greift: " + uhr.Elapsed);
        }

        // Eine erste, die horcht, deren Oberfläche aber nie dazu kommt: Sie antwortet "nicht gezeigt", der Name bleibt belegt.
        var pfad2 = Pfad("client-einstellungen.json");
        var haengt = Einzelinstanz.Anmelden(pfad2, () => false, vordergrundErlauben: false, marke: Marke());
        using (haengt.Instanz)
        {
            var uhr = Stopwatch.StartNew();
            var zweite = Einzelinstanz.Anmelden(pfad2, NieGerufen, vordergrundErlauben: false, marke: Marke(), frist: TimeSpan.FromMilliseconds(1500));
            Pruefe.Wahr(zweite.Instanz is null && !zweite.Gemeldet, "Oberfläche hängt: keine Instanz, nicht gemeldet");
            Pruefe.Wahr(uhr.Elapsed < Frist, "die Frist greift: " + uhr.Elapsed);
        }
    }

    [Test]
    public static void Einzelinstanz_Marke_SolangeEineInstanzLaeuft()
    {
        var marke = Marke();
        Pruefe.Falsch(Offen(marke), "vorher keine Marke");
        var a = Einzelinstanz.Anmelden(Pfad("client-einstellungen.json"), () => true, vordergrundErlauben: false, marke: marke);
        var b = Einzelinstanz.Anmelden(Pfad(@"probe\probe-einstellungen.json"), () => true, vordergrundErlauben: false, marke: marke);
        Pruefe.Wahr(Offen(marke), "die Marke steht, solange eine Instanz läuft");
        a.Instanz!.Dispose();
        Pruefe.Wahr(Offen(marke), "zwei Instanzen teilen die Marke; eine beendet, die andere hält sie");
        b.Instanz!.Dispose();
        Pruefe.Falsch(Offen(marke), "nach der letzten ist sie weg");
        Pruefe.Gleich(@"Local\JG-TacRelay-Laeuft", Einzelinstanz.LaeuftMarke, "Marke des Programms (AppMutex im Setup, SetupTests)");
    }

    private static bool Offen(string name)
    {
        if (!Mutex.TryOpenExisting(name, out var m)) return false;
        m.Dispose();
        return true;
    }
}
