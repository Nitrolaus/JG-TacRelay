// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.Client.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>Eigener Ordner unter dem Testordner (<see cref="Testserver.Wurzel"/>), wird beim Freigeben gelöscht.</summary>
internal sealed class Testordner : IDisposable
{
    public Testordner()
    {
        Pfad = Path.Combine(Testserver.Wurzel(), "oberflaeche", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Pfad);
    }

    public string Pfad { get; }

    public string Datei(string name) => Path.Combine(Pfad, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Pfad, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Sammelt, was auf den Oberflächen-Thread soll, bis der Test es abarbeitet.</summary>
internal sealed class WarteschlangenTakt : IOberflaechentakt
{
    private readonly ConcurrentQueue<Action> _schlange = new();

    public int Anzahl => _schlange.Count;

    public void Einreihen(Action aktion) => _schlange.Enqueue(aktion);

    public int Abarbeiten()
    {
        var n = 0;
        while (_schlange.TryDequeue(out var a))
        {
            a();
            n++;
        }
        return n;
    }
}

/// <summary>
/// Merkt, ob das Funkfeld geöffnet, welche Farbstellung angewandt und was in die Zwischenablage gelegt werden sollte (die
/// Schwelle hat kein eigenes Fenster mehr, A26; die echte Zwischenablage fasst kein Test an).
/// </summary>
internal sealed class FensterAttrappe : IFensterDienst, IZwischenablage
{
    public int Funkfeld;
    public List<Funk.Client.Design.Farbstellung> Farben { get; } = [];
    public List<string> Geoeffnet { get; } = [];
    public List<string> Kopiert { get; } = [];

    public void FunkfeldZeigen() => Funkfeld++;

    public void FarbenAnwenden(Funk.Client.Design.Farbstellung stellung) => Farben.Add(stellung);

    public void OrdnerOeffnen(string ordner) => Geoeffnet.Add(ordner);

    public bool Kopieren(string text)
    {
        Kopiert.Add(text);
        return true;
    }
}

/// <summary>
/// Die Seiten des Hauptfensters nach ihrem Inhalt statt nach ihrer Stelle in <see cref="HauptViewModel.Bereiche"/> (seit 0.10.0, AP1). Seit AP3
/// gibt es nur noch Start und Einstellungen (Audio und Meine Kanäle sind aufgelöst); was dort stand, öffnen die Tests über
/// <see cref="Feinheiten"/> unter Einstellungen.
/// </summary>
internal static class Seiten
{
    public static Bereich Start(HauptViewModel h) => h.Bereiche.Single(b => b.Inhalt is StartViewModel);

    public static Bereich Einstellungen(HauptViewModel h) => h.Bereiche.Single(b => b.Inhalt is EinstellungenViewModel);

    /// <summary>Einstellungen vorn und die Feinheiten der genannten Abschnitte offen (<see cref="Abschnittskennung"/>); ohne Angabe alle.</summary>
    public static void Feinheiten(HauptViewModel h, params string[] kennungen)
    {
        h.AktiverBereich = Einstellungen(h);
        foreach (var a in h.Einstellungen.Abschnitte)
            if (kennungen.Length == 0 || kennungen.Contains(a.Kennung, StringComparer.Ordinal)) a.Feinheiten.Offen = true;
    }
}

/// <summary>Alles, was ein ViewModel-Test braucht, mit Attrappen und Einstellungen im Testordner.</summary>
internal sealed class Aufbau : IDisposable
{
    public Aufbau(FunkLage? lage = null, ClientEinstellungen? einstellungen = null, IOberflaechentakt? takt = null, FunkDienstAttrappe? dienst = null)
    {
        Ordner = new Testordner();
        Speicher = new EinstellungsSpeicher(Ordner.Datei("einstellungen.json"));
        Takt = takt ?? SofortTakt.Instanz;
        Ablage = new Einstellungsablage(Speicher, einstellungen ?? Speicher.Laden(), Takt, TimeSpan.FromHours(1));
        Dienst = dienst ?? new FunkDienstAttrappe(lage ?? FunkDienstAttrappe.Beispiel());
        Anbindung = Verlauf.Anbinden(Maschine);
        Bruecke = new KlangBruecke(Maschine);
    }

    public Testordner Ordner { get; }
    public EinstellungsSpeicher Speicher { get; }
    public IOberflaechentakt Takt { get; }
    public Einstellungsablage Ablage { get; }
    public FunkDienstAttrappe Dienst { get; }
    public KlangMaschinenAttrappe Maschine { get; } = new();
    public PegelVerlauf Verlauf { get; } = new();
    public FensterAttrappe Fenster { get; } = new();
    public IDisposable Anbindung { get; }

    /// <summary>Die Brücke zwischen Klang und Funkbetrieb, hier über der Attrappe (Lautstärke und Ohr je Netz, Pegel).</summary>
    public KlangBruecke Bruecke { get; }

    /// <summary>Globale Tasten für die Einstellungen; ohne Angabe keine Quelle.</summary>
    public Funk.Klang.Eingabe.ITastenQuelle? Tastenquelle { get; set; }

    /// <summary>Serverprofil und Profilschlüssel für den Bereich Verbindung; ohne Angabe die eingebauten (im Testlauf keine).</summary>
    public Profilquelle? Profil { get; set; }

    /// <summary>Der Verwaltungsteil für das Hauptfenster, sonst keiner (wie ohne Admin-Setup).</summary>
    public Funk.ClientKern.IVerwaltungsteil? Verwaltung { get; set; }

    /// <summary>Die Monitore für den Platz der Meldezeile unter Einstellungen (seit 0.11.1); ohne Setzen ein Full-HD-Hauptmonitor.</summary>
    public IReadOnlyList<Bildschirm> Bildschirme { get; set; } = [OberflaecheFunkfeldGroesseTests.FullHd];

    public long Uhr { get; set; } = 1_000;

    /// <summary>Uhrzeit für Brett und Einsteigen (Fristen, Verlauf); ohne Setzen der Beispielabend, 20:52 Uhr.</summary>
    public DateTimeOffset Jetzt { get; set; } = FunkDienstAttrappe.Abend;

    /// <summary>Das Funkfeld über den Attrappen; Stummschaltungen der Leitung (Klangstufe C2b) rechnen mit der Uhrzeit <see cref="Jetzt"/>.</summary>
    public FunkfeldViewModel Funkfeld() => new(Dienst, Maschine, Verlauf, Ablage, Takt, () => Uhr, Bruecke, () => Jetzt);

    /// <summary>Das Brett der Einsatzleitung über der Attrappe, mit der Uhr <see cref="Jetzt"/>.</summary>
    public BrettViewModel Brett() => new(Dienst, Takt, () => Jetzt, TimeSpan.Zero);

    public SchwellenViewModel Schwelle() => new(Maschine, Verlauf, Ablage);

    public AudioViewModel Audio() => new(Maschine, Verlauf, Ablage, Takt);

    /// <param name="funkfeld">Mit ihm zeigen die Felder Lautstärke und VA/Taste wie die Kacheln (wie im Programm).</param>
    public NetzeViewModel Netze(FunkfeldViewModel? funkfeld = null) => new(Dienst, Maschine, Takt, Ablage, Bruecke, funkfeld);

    /// <summary>Das Hauptfenster-ViewModel mit allen Bereichen, verdrahtet wie in App.OnStartup.</summary>
    public HauptViewModel Haupt(out FunkfeldViewModel funkfeld, out SchwellenViewModel schwelle, out AudioViewModel audio, out NetzeViewModel netze)
    {
        var verbindung = new VerbindungViewModel(Dienst, Ablage, Takt, Fenster, Profil);
        audio = Audio();
        funkfeld = Funkfeld();
        netze = Netze(funkfeld);
        var einstellungen = new EinstellungenViewModel(Ablage, funkfeld, new TastenViewModel(Ablage, Tastenquelle, dienst: Dienst, takt: Takt, maschine: Maschine), Fenster,
            new MeldezeilePlatzViewModel(Ablage, () => Bildschirme));
        // Wie in App.OnStartup: eine Hörprobe für Audio und Sprachaktivierung.
        schwelle = new SchwellenViewModel(Maschine, Verlauf, Ablage, audio.Hoerprobe, audio.Korridor);
        return new HauptViewModel(verbindung, audio, netze, einstellungen, funkfeld, schwelle, Dienst, Fenster, Ablage, Takt, "Klang: Attrappe",
            verwaltung: Verwaltung, brett: Brett());
    }

    public void Dispose()
    {
        Bruecke.Dispose();
        Anbindung.Dispose();
        Ablage.Dispose();
        Ordner.Dispose();
    }
}
