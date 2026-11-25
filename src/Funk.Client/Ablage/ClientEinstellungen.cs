// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json.Serialization;
using Funk.Client.Design;
using Funk.Protokoll.Identitaet;
using Funk.Klang.Eingabe;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Client.Ablage;

/// <summary>
/// Einstellungen des Clients, als JSON im Benutzerordner der App (<see cref="EinstellungsSpeicher"/>). Keine Geheimnisse:
/// Der Geräteschlüssel liegt im Schlüsselspeicher von Windows, nie hier. Gelesene Werte gehen durch
/// <see cref="Normalisieren"/>, damit eine von Hand bearbeitete oder beschädigte Datei die Oberfläche nicht sprengt.
/// </summary>
public sealed class ClientEinstellungen
{
    /// <summary>
    /// 2 seit Klangstufe C1: Beim Laden einer Datei mit Version 1 (bis 0.6.0) stellt <see cref="Normalisieren"/> einmal um
    /// (<see cref="GateWerte.AufC1Umstellen"/>, eine nie gelernte Verstärkung fällt weg). Ältere Clients lesen die Datei weiter, schreiben aber nur
    /// die Felder ihres Modells mit Version 1 zurück: Das Einmessen (Abstand, "eingemessen", fest eingemessen) geht dabei verloren, beim nächsten
    /// Start wird erneut umgestellt, und der Hinweis "noch nicht eingemessen" kommt wieder (Gegenprüfung C1, N1: nach einem Rückschritt auf 0.6.0
    /// neu einmessen).
    /// </summary>
    public const int AktuelleVersion = 2;

    public int Version { get; set; } = AktuelleVersion;

    public VerbindungsEinstellungen Verbindung { get; } = new();

    /// <summary>Namen für den Antrag (A42), aus dem Setup oder hier eingetippt.</summary>
    public AnmeldeEinstellungen Anmeldung { get; } = new();

    /// <summary>Was dieser Client neben Namen zeigt (A49): Zeichen ja oder nein, im Funkfeld ja oder nein.</summary>
    public AnzeigeEinstellungen Anzeige { get; } = new();

    public AudioEinstellungen Audio { get; } = new();

    public GateWerte Gate { get; } = new();

    /// <summary>Auto Dauersend-Dämpfung (Klangstufe C2a): Schalter und Stufe; Dateien ohne den Abschnitt bekommen die Werte ab Werk.</summary>
    public DaempfungWerte Daempfung { get; } = new();

    public FunkfeldEinstellungen Funkfeld { get; } = new();

    /// <summary>
    /// Die Sprecheranzeige über dem Spiel (seit 0.11.9, JSON <c>sprecheranzeige</c>): an oder aus, Lage (waagerecht, senkrecht), Monitor, Größe, Durchsichtigkeit, Zeilen,
    /// Zeiten, eigene Sendung. Nur Zahlen und Schalter, nachsichtig gelesen; ein älterer Client übergeht den Abschnitt.
    /// </summary>
    public SprecheranzeigeEinstellungen Sprecheranzeige { get; } = new();

    /// <summary>
    /// Die Meldungen der Leitung über dem Spiel (seit 0.12.0, JSON <c>meldungen</c>): Lage (waagerecht, senkrecht), Grundgröße, Ausführung,
    /// Stopp-Bild, Ton. Nur Zahlen und Schalter, nachsichtig gelesen; ein älterer Client übergeht den Abschnitt.
    /// </summary>
    public MeldungenEinstellungen Meldungen { get; } = new();

    /// <summary>
    /// Das Spiel, über dem Sprecheranzeige, Meldungen und Meldezeile stehen (Umbau 19.10.2026, Sarevoc Q10, JSON <c>spiel</c>): ab Werk Star Citizen,
    /// sonst ein anderes Spiel über seinen Prozessnamen. Nur für die Suche nach dem Monitor; ein älterer Client übergeht den Abschnitt.
    /// </summary>
    public SpielEinstellungen Spiel { get; } = new();

    /// <summary>
    /// Je Netz (Schlüssel: Netz-ID als Dezimaltext) Auslösung, Lautstärke und Ohr (A9, A10). Seit 0.6.0 gilt eine Einstellung je
    /// Kanalnetz für alle seine Ch. (Sarevoc Q15): Eine Kanal-ID nimmt den Schlüssel ihres Kanalnetzes (<see cref="Schluessel"/>), so wie
    /// ältere Clients ein Netz unter seiner ID ablegten; die Datei bleibt für sie lesbar.
    /// </summary>
    public Dictionary<string, NetzEinstellung> Netze { get; } = new(StringComparer.Ordinal);

    /// <summary>Schlüssel in <see cref="Netze"/>: die Netz-ID, bei einer Kanal-ID (6.16) die ihres Kanalnetzes.</summary>
    public static string Schluessel(ushort netzId) =>
        (Funk.Protokoll.Steuerung.Kanalrechnung.Zerlegen(netzId, out var kanalnetz, out _) ? kanalnetz : netzId).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Platz des Hauptfensters (A32), so wie du es hingeschoben, gezogen oder maximiert hast: Lage in Bildpunkten, Größe in
    /// Einheiten (<see cref="FensterOrt"/>). Ohne Eintrag steht es mittig auf dem Hauptmonitor.
    /// </summary>
    public FensterOrt? HauptfensterOrt { get; set; }

    /// <summary>
    /// Platz aus der Zeit vor dem 30.09.2026, ganz in Einheiten. Nur noch gelesen (<see cref="HauptfensterFuer"/>); sobald
    /// du das Fenster bewegst, gilt <see cref="HauptfensterOrt"/>, und dieses Feld wird nicht mehr geschrieben.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FensterStand? Hauptfenster { get; set; }

    /// <summary>
    /// Größe des Hauptfensters je Größenklasse (seit 0.6.0, Sarevoc Q19 und Vorgabe j): Schlüssel aus
    /// <c>Fenstergroessen.Schluessel</c> ("start", "seite", "brett", "verwaltung", "sprachaktivierung-seite"; "start-aufgeklappt" und "seite-aufgeklappt"
    /// bis 0.9.3, seit 0.10.0 ungelesen; bis Stufe 2 "klein" und "sprachaktivierung", die nicht mehr gelten), Wert die zuletzt gezogene Größe.
    /// <c>null</c> heißt: Die Datei stammt von einem Client vor 0.6.0 (oder es gab nie ein Hauptfenster); dann wird die Größe aus
    /// <see cref="HauptfensterOrt"/> einmal die der Seiten, und das Hauptfenster öffnet auf der Startseite, klein, an der alten Lage.
    /// Ein Client 0.5.x kennt das Feld nicht und übergeht es; für ihn gilt weiter <see cref="HauptfensterOrt"/>, das mitgeschrieben
    /// wird.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, Fenstergroesse>? HauptfensterGroessen { get; set; }

    /// <summary>A26: der aufklappbare Bereich der Sprachaktivierung unten im Hauptfenster; seit 0.10.0 ungenutzt, bleibt für ältere Clients.</summary>
    public SchwellenbereichEinstellungen Schwellenbereich { get; } = new();

    /// <summary>Globale Tasten (A3, A5): Sprechtaste, Sendenetz vor und zurück.</summary>
    public TastenEinstellungen Tasten { get; } = new();

    /// <summary>
    /// Funk-Klick (A63, seit 0.5.0), eingestellt unter Einstellungen > Tasten > Erweitert (seit 0.10.0; bis 0.9.3 im Bereich Audio, bis Stufe 2 unten in der Sprachaktivierung). Ein Client 0.4.x
    /// kennt <c>klick</c> nicht und übergeht es; die Datei bleibt für ihn lesbar.
    /// </summary>
    public KlickEinstellungen Klick { get; } = new();

    /// <summary>
    /// Zuletzt geöffneter Bereich des Hauptfensters, bis 0.6.0 beim Start wiederhergestellt. Seit Klangstufe C1 (Sarevoc, Q25) beginnt jeder
    /// Start auf der Startseite, der Bereich wird nur in der laufenden Sitzung gemerkt und nicht mehr geschrieben; das Feld bleibt, damit
    /// Dateien älterer Clients lesbar bleiben.
    /// </summary>
    public string? Bereich { get; set; }

    /// <summary>
    /// Farbstellung der Oberfläche (A31, <see cref="Farbvorschlaege"/>). Seit 19.10.2026 gibt es nur Anthrazit; ohne Angabe und
    /// für jeden alten oder unbekannten Wert (P1 bis P3, "Heute", "Weinrot", "Dunkel", eine Zahl) gilt still die Voreinstellung,
    /// statt die ganze Datei unlesbar zu machen.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeFarbstellung))]
    public Farbstellung Farbstellung { get; set; } = Farbvorschlaege.Voreinstellung;

    /// <summary>Einstellung eines Netzes (einer Kanal-ID: ihres Kanalnetzes); legt sie bei Bedarf an.</summary>
    public NetzEinstellung NetzFuer(ushort netzId)
    {
        var schluessel = Schluessel(netzId);
        if (!Netze.TryGetValue(schluessel, out var n))
        {
            n = new NetzEinstellung();
            Netze[schluessel] = n;
        }
        return n;
    }

    /// <summary>Einstellung eines Netzes (einer Kanal-ID: ihres Kanalnetzes) oder <c>null</c>, ohne sie anzulegen.</summary>
    public NetzEinstellung? NetzOderNull(ushort netzId) => Netze.GetValueOrDefault(Schluessel(netzId));

    /// <summary>
    /// Der gemerkte Platz des Hauptfensters; aus einer älteren Datei der alte Stand, mit der Skala des Hauptmonitors
    /// (<paramref name="hauptSkala"/>) in Bildpunkte umgesetzt, so wie WPF ihn damals gesetzt hat.
    /// </summary>
    public FensterOrt? HauptfensterFuer(double hauptSkala) => HauptfensterOrt ?? FensterOrt.AusAltemStand(Hauptfenster, hauptSkala, mitGroesse: true);

    /// <summary>Merkt den Platz des Hauptfensters; der alte Stand ist damit abgelöst.</summary>
    public void HauptfensterMerken(FensterOrt ort)
    {
        ArgumentNullException.ThrowIfNull(ort);
        if (FensterOrt.OderNull(ort) is not { } o) return;
        HauptfensterOrt = o;
        Hauptfenster = null;
    }

    /// <summary>Bringt alle Werte in ihre Grenzen und verwirft Unbrauchbares; eine Datei bis 0.6.0 wird einmal umgestellt.</summary>
    public void Normalisieren()
    {
        if (Version < 2)
        {
            Gate.AufC1Umstellen();
            Audio.AufC1Umstellen();
        }
        Version = AktuelleVersion;
        Verbindung.Normalisieren();
        Anmeldung.Normalisieren();
        Anzeige.Normalisieren();
        Audio.Normalisieren();
        Gate.Normalisieren();
        Funkfeld.Normalisieren();
        Sprecheranzeige.Normalisieren();
        Meldungen.Normalisieren();
        Spiel.Normalisieren();
        foreach (var schluessel in Netze.Keys.ToList())
        {
            var gueltig = ushort.TryParse(schluessel, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id is > 0 and < ushort.MaxValue
                && string.Equals(id.ToString(CultureInfo.InvariantCulture), schluessel, StringComparison.Ordinal);
            if (!gueltig || Netze[schluessel] is null) Netze.Remove(schluessel);
            else Netze[schluessel].Normalisieren();
        }
        HauptfensterOrt = FensterOrt.OderNull(HauptfensterOrt);
        Hauptfenster = FensterStand.OderNull(Hauptfenster);
        foreach (var (schluessel, g) in HauptfensterGroessen?.ToList() ?? [])
        {
            if (Fenstergroesse.OderNull(g) is null) HauptfensterGroessen!.Remove(schluessel);
        }
        Schwellenbereich.Normalisieren();
        Tasten.Normalisieren();
        Klick.Normalisieren();
        if (Bereich is { Length: > 40 }) Bereich = null;
        if (!Enum.IsDefined(Farbstellung)) Farbstellung = Farbvorschlaege.Voreinstellung;
    }
}

/// <summary>Eine gespeicherte globale Taste mit ihrer Funktion.</summary>
public sealed class TastenEintrag
{
    public Tastenfunktion Funktion { get; set; }

    public EingabeArt Art { get; set; }

    /// <summary>Scancode mit Präfix, Mausknopf 1-5 oder Joystickknopf ab 1 (<see cref="Tastenbelegung.Code"/>).</summary>
    public int Code { get; set; }

    /// <summary>Gerätepfad beim Joystick, sonst leer.</summary>
    public string? Geraet { get; set; }

    public Tastenzuordnung AlsZuordnung() => new(Funktion, new Tastenbelegung(Art, Code, Art == EingabeArt.Joystick ? Geraet : null));

    public static TastenEintrag Aus(Tastenzuordnung z) =>
        new() { Funktion = z.Funktion, Art = z.Taste.Art, Code = z.Taste.Code, Geraet = z.Taste.Geraet };

    internal bool Gueltig() =>
        Enum.IsDefined(Funktion) && Enum.IsDefined(Art) && Art switch
        {
            EingabeArt.Tastatur => Code is > 0 and <= 0xE1FF,
            EingabeArt.Maus => Code is >= Tastenbelegung.MausLinks and <= Tastenbelegung.MausX2,
            _ => Code is > 0 and <= 0xFFFF && !string.IsNullOrWhiteSpace(Geraet) && Geraet.Length <= 512,
        };
}

/// <summary>
/// Eine gespeicherte Taste einer Funktion ab 0.5.0 (Melden, Funkfeld, Phasen; RAID-S2-ENTWURF Abschnitt 6). Die Funktion steht als
/// Text: Ein Client 0.4.0 kennt diese Liste nicht und übergeht sie als unbekannte Eigenschaft; ein neuerer Name, den dieser Client
/// nicht kennt, bleibt stehen, wirkt aber nicht.
/// </summary>
public sealed class WeitereTaste
{
    public string Funktion { get; set; } = "";

    public EingabeArt Art { get; set; }

    /// <summary>Wie <see cref="TastenEintrag.Code"/>.</summary>
    public int Code { get; set; }

    /// <summary>Gerätekennung beim Joystick, sonst leer.</summary>
    public string? Geraet { get; set; }

    /// <summary>Die Zuordnung, wenn dieser Client die Funktion kennt und sie in diese Liste gehört; sonst <c>null</c>.</summary>
    public Tastenzuordnung? AlsZuordnung() =>
        Enum.TryParse<Tastenfunktion>(Funktion, ignoreCase: false, out var f) && Enum.IsDefined(f) && !Tastenfunktionen.IstAlt(f)
            && !int.TryParse(Funktion, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
            ? new Tastenzuordnung(f, new Tastenbelegung(Art, Code, Art == EingabeArt.Joystick ? Geraet : null))
            : null;

    public static WeitereTaste Aus(Tastenzuordnung z) =>
        new() { Funktion = z.Funktion.ToString(), Art = z.Taste.Art, Code = z.Taste.Code, Geraet = z.Taste.Geraet };

    /// <summary>Art, Code und Gerät sind brauchbar (die Funktion prüft <see cref="AlsZuordnung"/>).</summary>
    internal bool Gueltig() =>
        Funktion is { Length: > 0 and <= 40 } && new TastenEintrag { Funktion = Tastenfunktion.Sprechen, Art = Art, Code = Code, Geraet = Geraet }.Gueltig();
}

/// <summary>
/// Globale Tasten (A3, A5, A7): beliebig viele je Funktion, jede Taste nur einmal. Beim ersten Start gelten Numpad + und -
/// fürs Weiterschalten des Sendenetzes, wie bei Sarevoc gewohnt; was der Nutzer danach löscht, bleibt gelöscht.
/// <para>
/// Seit 0.5.0 zwei Listen: <see cref="Eintraege"/> trägt nur Sprechen, Sendenetz vor und zurück, wie seit 0.1.0, denn ein Client
/// 0.4.0 liest dort die Funktion als festen Namen und legt die ganze Datei als <c>.defekt</c> beiseite, wenn er einen nicht kennt.
/// Melden, Funkfeld und Phasen stehen in <see cref="Weitere"/>, die er übergeht. So bleibt die Rückkehr auf 0.4.0 möglich.
/// </para>
/// </summary>
public sealed class TastenEinstellungen
{
    public const int MaxEintraege = 32;

    /// <summary>Sprechen, Sendenetz vor und zurück (lesbar für alle Clients seit 0.1.0).</summary>
    public List<TastenEintrag> Eintraege { get; } = [];

    /// <summary>Die Funktionen ab 0.5.0 (Melden, Funkfeld ablegen und holen, Phase vor und zurück).</summary>
    public List<WeitereTaste> Weitere { get; } = [];

    /// <summary>
    /// "Melde-Taste spricht in": das Netz (ID) der Melde-Taste. Ohne Wahl gilt das Netz "Kommando", wenn die Flotte eines hat
    /// (<c>Meldenetzwahl</c>).
    /// </summary>
    public ushort? Meldenetz { get; set; }

    /// <summary>Die Voreinstellung (Numpad + und -) ist einmal gesetzt worden.</summary>
    public bool Voreingestellt { get; set; }

    /// <summary>
    /// Nachlauf der Sprechtaste ab Werk (Q66, 0.9.3): gemessen fehlen beim Loslassen genau am Wortende bis 35 ms Sprache, und bis 50 ms nach dem
    /// Loslassen kommt noch Sprache aus der Kette (Rauschfilter Stark mit Echounterdrückung, <c>MessungNachlauf</c>); aufgerundet plus Reserve
    /// für den Aufnahmepuffer des Geräts, den der Prüfstand nicht hat.
    /// </summary>
    public const int NachlaufAbWerkMs = 100;

    /// <summary>
    /// Nachlauf der Sprechtaste in ms (Q66, seit 0.9.3): So lange sendet JG TacRelay nach dem Loslassen von Sprech- oder Melde-Taste weiter,
    /// 0 bis 1000 in 10-ms-Schritten, ab Werk <see cref="NachlaufAbWerkMs"/>. Nachsichtig gelesen: Was keine Zahl ist, gilt als ab Werk, Werte
    /// außerhalb werden begrenzt; ältere Clients übergehen das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeZahlOderNull))]
    public int? NachlaufMs { get; set; } = NachlaufAbWerkMs;

    /// <summary>Der Nachlauf, der gilt: <see cref="NachlaufMs"/>, ohne Wert ab Werk.</summary>
    [JsonIgnore]
    public int Nachlauf => NachlaufMs ?? NachlaufAbWerkMs;

    /// <summary>
    /// Der Schritt "Drück jetzt deine Sprechtaste" beim ersten Start (seit 0.10.0, Q76, E8; JSON <c>tasten.erststartSprechtaste</c>): <c>null</c>
    /// heißt kein Schritt (eine Datei von vorher, oder Headset oder Lautsprecher war schon gewählt), <c>false</c> offen (die Startseite setzt es
    /// beim allerersten Start, solange die Hörweise fehlt und keine Sprechtaste belegt ist), <c>true</c> erledigt (eine Sprechtaste belegt oder
    /// "Später"). Ab Werk ist keine Taste belegt. Nachsichtig gelesen; <c>null</c> wird nicht geschrieben, ältere Clients übergehen das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigerSchalterOderNull))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ErststartSprechtaste { get; set; }

    /// <summary>Alle wirksamen Zuordnungen aus beiden Listen; Einträge mit unbekannter Funktion fehlen.</summary>
    public IReadOnlyList<Tastenzuordnung> Zuordnungen() =>
        [.. Eintraege.Select(e => e.AlsZuordnung()), .. Weitere.Select(w => w.AlsZuordnung()).OfType<Tastenzuordnung>()];

    /// <summary>Wie viele Tasten wirken (für die Grenze <see cref="MaxEintraege"/> je Liste).</summary>
    public int Anzahl(Tastenfunktion funktion) =>
        Tastenfunktionen.IstAlt(funktion) ? Eintraege.Count : Weitere.Count(w => w.AlsZuordnung() is not null);

    /// <summary>Die Funktion, auf der <paramref name="taste"/> liegt, oder <c>null</c>.</summary>
    public Tastenfunktion? FunktionVon(Tastenbelegung taste)
    {
        foreach (var z in Zuordnungen())
            if (z.Taste.GleicheTaste(taste)) return z.Funktion;
        return null;
    }

    /// <summary>Legt die Taste in die Liste ihrer Funktion; die Prüfung auf Doppelte macht der Aufrufer.</summary>
    public void Hinzufuegen(Tastenzuordnung z)
    {
        if (Tastenfunktionen.IstAlt(z.Funktion)) Eintraege.Add(TastenEintrag.Aus(z));
        else Weitere.Add(WeitereTaste.Aus(z));
    }

    /// <summary>Nimmt <paramref name="taste"/> aus beiden Listen, mit <paramref name="funktion"/> nur dort; Anzahl der entfernten.</summary>
    public int Entfernen(Tastenbelegung taste, Tastenfunktion? funktion = null) =>
        Eintraege.RemoveAll(e => (funktion is null || e.Funktion == funktion) && e.AlsZuordnung().Taste.GleicheTaste(taste))
        + Weitere.RemoveAll(w => w.AlsZuordnung() is { } z && (funktion is null || z.Funktion == funktion) && z.Taste.GleicheTaste(taste));

    internal void Normalisieren()
    {
        var gesehen = new List<Tastenbelegung>();
        Eintraege.RemoveAll(e =>
        {
            if (e is null || !e.Gueltig() || !Tastenfunktionen.IstAlt(e.Funktion)) return true;
            var t = e.AlsZuordnung().Taste;
            if (gesehen.Any(g => g.GleicheTaste(t))) return true;
            gesehen.Add(t);
            return false;
        });
        if (Eintraege.Count > MaxEintraege) Eintraege.RemoveRange(MaxEintraege, Eintraege.Count - MaxEintraege);
        // Unbekannte Funktionen bleiben stehen (ein neuerer Client hat sie geschrieben), zählen aber weder als Taste noch zur Grenze.
        Weitere.RemoveAll(w =>
        {
            if (w is null || !w.Gueltig()) return true;
            if (w.AlsZuordnung() is not { } z) return false;
            if (gesehen.Any(g => g.GleicheTaste(z.Taste))) return true;
            gesehen.Add(z.Taste);
            return false;
        });
        while (Weitere.Count(w => w.AlsZuordnung() is not null) > MaxEintraege) Weitere.RemoveAt(Weitere.FindLastIndex(w => w.AlsZuordnung() is not null));
        if (Meldenetz is 0 or ushort.MaxValue) Meldenetz = null;
        NachlaufMs = NachlaufMs is { } n ? IKlangMaschine.TastenNachlaufBegrenzen(n) : NachlaufAbWerkMs;
        if (Voreingestellt) return;
        Voreingestellt = true;
        foreach (var (funktion, code) in new[] { (Tastenfunktion.NetzVor, Tastenbelegung.NumpadPlus), (Tastenfunktion.NetzZurueck, Tastenbelegung.NumpadMinus) })
        {
            var taste = Tastenbelegung.Taste(code);
            if (!gesehen.Any(g => g.GleicheTaste(taste))) Eintraege.Add(TastenEintrag.Aus(new Tastenzuordnung(funktion, taste)));
        }
    }
}

/// <summary>
/// Funk-Klick (A11, A63, seit 0.5.0): eigene Taste und andere Sprecher je an oder aus mit Lautstärke (dB unter der Sprache,
/// <see cref="FunkKlickEinstellungen"/>) und ob es bei Sprachaktivierung dezent klickt oder gar nicht. Die Wahl des Klangs (Klick,
/// Doppelklick, Piep, 0.5.0 bis Stufe 2) entfällt seit 0.6.0 Stufe 2b, der Klick ist ein kurzes Rauschen. Ein <c>klang</c> aus
/// einer älteren Datei wird übergangen wie jedes unbekannte Feld; 0.5.x liest eine Datei ohne <c>klang</c> als Klick.
/// </summary>
public sealed class KlickEinstellungen
{
    public bool EigeneAn { get; set; } = true;

    public double EigeneDb { get; set; } = FunkKlickEinstellungen.EigeneVoreinstellungDb;

    public bool AndereAn { get; set; } = true;

    public double AndereDb { get; set; } = FunkKlickEinstellungen.AndereVoreinstellungDb;

    /// <summary>Andere, die per Sprachaktivierung sprechen: dezent (18 dB leiser) klicken; aus heißt dann kein Klick.</summary>
    public bool BeiSprachaktivierung { get; set; } = true;

    /// <summary>
    /// Was die Klangmaschine bekommt; die Werte sind nach <see cref="Normalisieren"/> immer gültig. Den eigenen Klick spielt sie nur mit
    /// Headset (Klangstufe C1, Sarevoc Q6): Über Lautsprecher hört man ihn nicht, die anderen hören den Klick ihres Empfängers weiter. Ob
    /// Lautsprecher oder Headset spielen, lässt sich unter Windows nicht verlässlich erkennen (der Formfaktor des Endpunkts sagt bei USB-Karten
    /// und Klinkenbuchsen oft "Lautsprecher", auch wenn ein Kopfhörer steckt); es gilt die Antwort auf "Headset oder Lautsprecher?"
    /// (<paramref name="hoerweise"/>), solange sie fehlt, kein eigener Klick. Ohne eigenen Klick bleibt auch die Echoreferenz unangetastet
    /// (Gegenprüfung Klangstufe A: das Ausblenden des Klicks ließ in den ersten 250 ms das Echo der anderen durch).
    /// </summary>
    public FunkKlickEinstellungen FuerMaschine(Hoerweise? hoerweise) => new()
    {
        EigeneAn = EigeneAn && hoerweise == Hoerweise.Headset,
        EigeneDb = (float)EigeneDb,
        AndereAn = AndereAn,
        AndereDb = (float)AndereDb,
        SprachaktivierungAus = !BeiSprachaktivierung,
    };

    /// <summary>Eine Lautstärke in die Grenzen, auf ganze dB; was keine Zahl ist, wird die Voreinstellung.</summary>
    public static double Begrenzen(double db, double voreinstellung) =>
        double.IsFinite(db) ? Math.Clamp(Math.Round(db), FunkKlickEinstellungen.MinDb, FunkKlickEinstellungen.MaxDb) : voreinstellung;

    internal void Normalisieren()
    {
        EigeneDb = Begrenzen(EigeneDb, FunkKlickEinstellungen.EigeneVoreinstellungDb);
        AndereDb = Begrenzen(AndereDb, FunkKlickEinstellungen.AndereVoreinstellungDb);
    }
}

/// <summary>
/// A26 (Sarevoc, 29.09.2026): Die Schwelle wurde unten im Hauptfenster eingestellt, aufklappbar, kein eigenes Fenster. Gemerkt wurden, ob der
/// Bereich offen ist, und seine Höhe in geräteunabhängigen Einheiten. Seit 0.10.0 ungenutzt: Unten steht die Statuszeile, und die
/// Sprachaktivierung ist nur noch eine Seite (Q87). Das Feld <c>schwellenbereich</c> bleibt lesbar und wird weiter geschrieben, damit ein
/// älterer Client seine Datei unverändert vorfindet.
/// </summary>
public sealed class SchwellenbereichEinstellungen
{
    public const double MinHoehe = 230;
    public const double MaxHoehe = 1600;
    public const double StandardHoehe = 340;

    public bool Offen { get; set; }

    public double Hoehe { get; set; } = StandardHoehe;

    internal void Normalisieren() =>
        Hoehe = double.IsFinite(Hoehe) ? Math.Clamp(Hoehe, MinHoehe, MaxHoehe) : StandardHoehe;
}

/// <summary>
/// Wie der Client den Server erreicht. Serverprofil und Profilschlüssel stehen hier nicht mehr: Sie kommen mit dem Client
/// (<see cref="Dienste.Profilquelle"/>, 3.1, ANMELDUNG-SICHERHEIT P1). Die Felder <c>profilDatei</c> und
/// <c>profilSchluessel</c> einer älteren Datei werden beim Lesen übergangen, damit niemand einem Mitglied ein falsches Profil
/// samt Schlüssel unterschieben kann.
/// </summary>
public sealed class VerbindungsEinstellungen
{
    /// <summary>Statt der Hosts aus dem Profil diesen Host oder diese IP verbinden (Heimphase, Tests).</summary>
    public string? Adresse { get; set; }

    /// <summary>Nur für Entwickler: Testzugang eines funk-testserver auf Loopback, Form <c>127.0.0.1:port</c>.</summary>
    public string? Testzugang { get; set; }

    /// <summary>
    /// Der Gerätename im Antrag (seit 0.6.3, Sarevoc 04.10.2026): "PC-" und die ersten vier Zeichen der Gerätekennung, etwa
    /// <c>PC-7Q2K</c>, nie der Name des Windows-PCs. Der stünde sonst ungeprüft auf dem Server und vor jedem Admin, auch wenn er
    /// anstößig ist oder einen Klarnamen trägt. Die Kennung ist der Fingerabdruck des Geräteschlüssels (3.2, 8.5): Sie verrät
    /// nichts über den Rechner, und der Name passt sichtbar zu ihr.
    /// </summary>
    public static string NameAusKennung(string kennung)
    {
        ArgumentNullException.ThrowIfNull(kennung);
        var kopf = new string([.. kennung.Where(char.IsAsciiLetterOrDigit).Take(4)]).ToUpperInvariant();
        return kopf.Length == 0 ? "PC" : "PC-" + kopf;
    }

    /// <summary>
    /// Nur noch zum Lesen älterer Dateien: Bis 0.6.2 stand hier der Name des Windows-PCs (<c>Environment.MachineName</c>), und der
    /// Antrag schickte ihn. Seit 0.6.3 gilt <see cref="NameAusKennung"/>; <see cref="Normalisieren"/> leert das Feld, damit der
    /// alte Name auch nicht mehr in der Datei steht.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Geraetename { get; set; }

    /// <summary>Beim Start selbst verbinden; seit 0.10.0 nach der Freigabe einmal von selbst an (<see cref="NachFreigabeVoreinstellen"/>, E12).</summary>
    public bool BeimStartVerbinden { get; set; }

    /// <summary>
    /// "Beim Start verbinden" ist nach der Freigabe schon einmal von selbst angegangen (seit 0.10.0, E12, Endfassung #83). Ab dann gilt der Schalter,
    /// so wie du ihn stellst. JSON <c>verbindung.beimStartVerbindenVoreingestellt</c>; nachsichtig gelesen (ein falscher Wert gilt als nein), ein
    /// älterer Client übergeht das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigAus))]
    public bool BeimStartVerbindenVoreingestellt { get; set; }

    /// <summary>
    /// E12: Ist dieses Gerät freigegeben (<see cref="Freigegeben"/>, auch aus einer Datei vor 0.10.0) und "Beim Start verbinden" noch nie von selbst
    /// angegangen, geht es jetzt einmal an. Wer es danach ausschaltet, behält aus; wer vor 0.10.0 "aus" hatte, findet es einmal an. Wahr, wenn sich
    /// dabei etwas geändert hat (dann ist die Datei vorzumerken).
    /// </summary>
    public bool NachFreigabeVoreinstellen()
    {
        if (Freigegeben != true || BeimStartVerbindenVoreingestellt) return false;
        BeimStartVerbindenVoreingestellt = true;
        BeimStartVerbinden = true;
        return true;
    }

    /// <summary>
    /// "Beim Verbinden als Funker" (6.15, seit 0.5.0, ab Werk aus): Ein Admin schaltet nach jedem Verbinden "Heute als Funker" ein;
    /// ein älterer Client übergeht das Feld.
    /// </summary>
    public bool AlsFunkerBeimVerbinden { get; set; }

    /// <summary>
    /// Dieses Gerät war mit seinem Konto verbunden, also freigegeben (seit 0.6.0): Die Startseite sagt "Gerät freigegeben" auch
    /// getrennt. Ein neuer Antrag (Gerät entfernt) setzt es zurück; ein älterer Client übergeht das Feld. <c>null</c> heißt unbekannt
    /// (eine Datei aus einer Fassung davor): Die Startseite sagt dann "Wird beim Verbinden geprüft", nicht "noch nicht freigegeben".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Freigegeben { get; set; }

    internal void Normalisieren()
    {
        Adresse = Leer(Adresse, 253);
        Testzugang = Leer(Testzugang, 64);
        // Ein Feld zum Ändern gab es nie (Q4), also stammt jeder Wert hier vom Windows-PC: weg damit (Sarevoc 04.10.2026).
        Geraetename = null;
    }

    private static string? Leer(string? s, int max) => string.IsNullOrWhiteSpace(s) || s.Length > max ? null : s.Trim();
}

/// <summary>
/// Namen für den Antrag ohne Code (A42, 3.4): SC-Name ("Mustermann") und Discord-Name ("Mustermann/Max"). Kommen aus dem Setup
/// (<c>anmeldung.json</c>, <see cref="Anmeldedatei"/>) oder werden hier eingetippt. <see cref="Bestaetigt"/> heißt: Die
/// Frage "Anmelden als ... - stimmt das?" ist mit Ja beantwortet; dann fragt der Client beim nächsten Antrag nicht noch
/// einmal. Geheim ist daran nichts.
/// </summary>
public sealed class AnmeldeEinstellungen
{
    public string? ScName { get; set; }

    public string? DiscordName { get; set; }

    public bool Bestaetigt { get; set; }

    /// <summary>Beide Namen gültig nach den Regeln des Servers.</summary>
    public bool Vollstaendig => ScName is not null && DiscordName is not null;

    internal void Normalisieren()
    {
        ScName = Namensregeln.ScName(ScName);
        DiscordName = Namensregeln.DiscordName(DiscordName);
        if (!Vollstaendig) Bestaetigt = false;
    }
}

/// <summary>Zeichen neben Namen (A49): für den Betrachter abschaltbar, im Funkfeld ab Werk aus (A29: das Feld bleibt kompakt).</summary>
public sealed class AnzeigeEinstellungen
{
    public bool ZeichenNebenNamen { get; set; } = true;

    public bool ZeichenImFunkfeld { get; set; }

    /// <summary>
    /// Hilfetexte beim Überfahren der Knöpfe (Sarevoc Q9, seit 0.6.0), ab Werk an. Ein Client 0.5.x kennt das Feld nicht und
    /// übergeht es; ein fehlendes Feld liest sich als an.
    /// </summary>
    public bool Hilfetexte { get; set; } = true;

    /// <summary>
    /// Aufgeklappte Klappbereiche "Erweitert:" der Einstellungen (seit 0.9.3, Sarevoc 11.10.2026; bis 0.11.0 unter anderem Namen, der Schlüssel in
    /// der Datei blieb), je eine Kennung; seit 0.10.0 eine je Abschnitt
    /// ("ich", "tasten", "empfangen", "senden", "funkfeld", seit 0.11.9 "sprecheranzeige", "fehlersuche"). Ab Werk sind alle zu. Nachsichtig gelesen: Was kein Text ist, fällt
    /// weg, und Unbrauchbares beim Normalisieren; ältere Clients übergehen die Liste.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeTextliste))]
    public List<string> OffeneFeinheiten { get; set; } = [];

    /// <summary>Höchstens so viele Kennungen in <see cref="OffeneFeinheiten"/>.</summary>
    public const int MaxOffeneFeinheiten = 32;

    /// <summary>
    /// Kennungen aus 0.9.3, die seit 0.10.0 (E6) ihr Abschnitt trägt: Der Nachlauf der Sprechtaste steht unter Tasten, die Klangfarbe unter
    /// Senden. Das Normalisieren deutet sie einmal um, damit ein offener Klappbereich offen bleibt.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AlteFeinheiten { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sprechtaste"] = "tasten",
        ["klangfarbe"] = "senden",
    };

    internal void Normalisieren()
    {
        OffeneFeinheiten ??= [];
        for (var i = 0; i < OffeneFeinheiten.Count; i++)
            if (OffeneFeinheiten[i] is { } k && AlteFeinheiten.TryGetValue(k, out var neu)) OffeneFeinheiten[i] = neu;
        var gesehen = new HashSet<string>(StringComparer.Ordinal);
        OffeneFeinheiten.RemoveAll(k => k is not { Length: > 0 and <= 40 } || !gesehen.Add(k));
        if (OffeneFeinheiten.Count > MaxOffeneFeinheiten) OffeneFeinheiten.RemoveRange(MaxOffeneFeinheiten, OffeneFeinheiten.Count - MaxOffeneFeinheiten);
    }
}

/// <summary>
/// Die Sprecheranzeige über dem Spiel (seit 0.11.9, Sarevoc 11.10. und 15.10.2026): Solange jemand zu hören ist, steht sein Name fett mit dem Kanal
/// über dem Spiel. Ab Werk an, ganz oben links (Lage 0 % und 0 %), eine Zeile, 1,0 s Nachhalten, 0,5 s Ausblenden, eigene Sendung aus. Die Lage
/// stellen seit Sarevocs Änderung vom 15.10.2026 zwei Schieberegler ein ("die Position mit Schieberegler einstellen, nicht fest vorgeschlagen"), je 0 bis
/// 100 % des Monitors; das Raster mit neun Feldern davor ist entfallen. Gespeichert nur Zahlen und Schalter (keine neuen Namen einer Aufzählung,
/// die einen älteren Client die Datei beiseitelegen ließen), nachsichtig gelesen: Was keine Zahl ist, liest sich als 0; was danach außerhalb der
/// Grenzen liegt, wird beim Normalisieren ab Werk. Ein älterer Client übergeht den Abschnitt.
/// </summary>
public sealed class SprecheranzeigeEinstellungen
{
    /// <summary>Ab Werk an (Sarevoc: ein Standardmitglied muss nichts einstellen).</summary>
    public const bool AnAbWerk = true;

    /// <summary>Die Lage je Richtung in Prozent des Monitors: 0 bis 100, ab Werk 0 (Sarevoc 15.10.2026: "im Original ganz oben links anzuordnen").</summary>
    public const int LageMax = 100;

    public const int LageAbWerk = 0;

    /// <summary>Größe in Prozent der Grundschrift: 60 bis 200, ab Werk 100.</summary>
    public const int GroesseMin = 60;

    public const int GroesseMax = 200;

    public const int GroesseAbWerk = 100;

    /// <summary>Durchsichtigkeit der Schrift in Prozent: 0 bis 80, ab Werk 0 (deckend).</summary>
    public const int DurchsichtigkeitMax = 80;

    /// <summary>Höchstens so viele Zeilen: 1 bis 4, ab Werk 1 (Sarevoc 15.10.2026: "mehr als einem gleichzeitig zuzuhören ist Mist").</summary>
    public const int ZeilenMin = 1;

    public const int ZeilenMax = 4;

    public const int ZeilenAbWerk = 1;

    /// <summary>So lange bleibt ein Name nach dem letzten Wort stehen: 0 bis 5 s, ab Werk 1,0 s.</summary>
    public const int NachhaltenMaxMs = 5000;

    public const int NachhaltenAbWerkMs = 1000;

    /// <summary>So lange blendet er danach aus: 0 bis 2 s, ab Werk 0,5 s.</summary>
    public const int AusblendenMaxMs = 2000;

    public const int AusblendenAbWerkMs = 500;

    /// <summary>Die eigene Sendung als Zeile "Du -> Kanal (Ch.)", ab Werk aus (Sarevoc 15.10.2026).</summary>
    public const bool EigeneSendungAbWerk = false;

    /// <summary>Die Sprecheranzeige ist an.</summary>
    [JsonConverter(typeof(NachsichtigAn))]
    public bool An { get; set; } = AnAbWerk;

    /// <summary>Waagerecht in Prozent des Monitors: 0 links, 100 rechts (der Block bleibt ganz im Monitor).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Waagerecht { get; set; } = LageAbWerk;

    /// <summary>Senkrecht in Prozent des Monitors: 0 oben, 100 unten (der Block bleibt ganz im Monitor).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Senkrecht { get; set; } = LageAbWerk;

    /// <summary>
    /// Nur noch gelesen: die Stelle im Raster mit neun Feldern (0 bis 8, drei Zeilen zu drei Spalten, oben links 0), das bis zu Sarevocs Änderung vom
    /// 15.10.2026 die Lage bestimmte. Steht sie in einer Datei und sind beide Regler noch auf 0, rechnet <see cref="Normalisieren"/> sie in die
    /// Regler um (Spalte und Zeile zu 0, 50 oder 100 %); geschrieben wird sie danach nicht mehr.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeZahlOderNull))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Stelle { get; set; }

    /// <summary>
    /// Der Monitor: <c>null</c> Automatisch (der Monitor des Spiels, ab Werk Star Citizen, sonst der zuletzt bekannte, sonst der Hauptmonitor), 0 der
    /// Hauptmonitor, 1 bis <see cref="FunkfeldEinstellungen.MeldezeileMonitorMax"/> der n-te; <c>null</c> wird nicht geschrieben.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeZahlOderNull))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Monitor { get; set; }

    /// <summary>Größe in Prozent (<see cref="GroesseMin"/> bis <see cref="GroesseMax"/>).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Groesse { get; set; } = GroesseAbWerk;

    /// <summary>Durchsichtigkeit der Schrift in Prozent (0 bis <see cref="DurchsichtigkeitMax"/>).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Durchsichtigkeit { get; set; }

    /// <summary>Höchstens so viele Zeilen (<see cref="ZeilenMin"/> bis <see cref="ZeilenMax"/>).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Zeilen { get; set; } = ZeilenAbWerk;

    /// <summary>Nachhalten in ms (0 bis <see cref="NachhaltenMaxMs"/>).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int NachhaltenMs { get; set; } = NachhaltenAbWerkMs;

    /// <summary>Ausblenden in ms (0 bis <see cref="AusblendenMaxMs"/>).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int AusblendenMs { get; set; } = AusblendenAbWerkMs;

    /// <summary>Die eigene Sendung zeigen.</summary>
    [JsonConverter(typeof(NachsichtigAus))]
    public bool EigeneSendung { get; set; } = EigeneSendungAbWerk;

    internal void Normalisieren()
    {
        if (Waagerecht is < 0 or > LageMax) Waagerecht = LageAbWerk;
        if (Senkrecht is < 0 or > LageMax) Senkrecht = LageAbWerk;
        // Eine Datei aus der Zeit des Rasters: die Stelle in die Regler, wenn sie noch nicht gestellt sind; danach fällt sie weg.
        if (Stelle is { } s && s is >= 0 and <= 8 && Waagerecht == LageAbWerk && Senkrecht == LageAbWerk)
        {
            Waagerecht = s % 3 * 50;
            Senkrecht = s / 3 * 50;
        }
        Stelle = null;
        if (Monitor is not (>= 0 and <= FunkfeldEinstellungen.MeldezeileMonitorMax)) Monitor = null;
        if (Groesse is < GroesseMin or > GroesseMax) Groesse = GroesseAbWerk;
        if (Durchsichtigkeit is < 0 or > DurchsichtigkeitMax) Durchsichtigkeit = 0;
        if (Zeilen is < ZeilenMin or > ZeilenMax) Zeilen = ZeilenAbWerk;
        if (NachhaltenMs is < 0 or > NachhaltenMaxMs) NachhaltenMs = NachhaltenAbWerkMs;
        if (AusblendenMs is < 0 or > AusblendenMaxMs) AusblendenMs = AusblendenAbWerkMs;
    }
}

/// <summary>Wo die Sprecheranzeige steht: waagerecht und senkrecht in Prozent des Monitors und der Monitor (<c>null</c>: Automatisch).</summary>
public readonly record struct Sprecherlage(int Waagerecht, int Senkrecht, int? Monitor);

/// <summary>
/// Die Meldungen der Leitung über dem Spiel beim Funker (seit 0.12.0, Sarevoc 11.10. und 16.10.2026, M3-M23): Lage über zwei Schieberegler wie die
/// Sprecheranzeige, ab Werk oben Mitte (waagerecht 50 %, senkrecht 0 %); Grundgröße der Grafiken in Einheiten (ab Werk 180, die Leitung sendet
/// normal, doppelt oder dreifach davon); Ausführung Schlicht (ab Werk), Metall oder Nur Schrift; das Bild beim Stopp, Handschuh (ab Werk) oder
/// Xeno; der Ton bei Befehlen, Textmeldungen und in den letzten 10 s eines Countdowns (ab Werk an). Einen Schalter zum ganz Abschalten gibt es nicht (Vorgabe).
/// Gespeichert nur Zahlen und Schalter (Ausführung und Stopp-Bild als Nummer, keine Namen einer Aufzählung, die einen älteren Client die Datei
/// beiseitelegen ließen), nachsichtig gelesen: Was keine Zahl ist, liest sich als 0; was danach außerhalb der Grenzen liegt, wird beim
/// Normalisieren ab Werk. Ein älterer Client übergeht den Abschnitt.
/// </summary>
public sealed class MeldungenEinstellungen
{
    /// <summary>Lage je Richtung in Prozent des Monitors, 0 bis 100.</summary>
    public const int LageMax = 100;

    /// <summary>Ab Werk oben Mitte: waagerecht 50 % (der Block steht mittig), senkrecht 0 % (oben).</summary>
    public const int WaagerechtAbWerk = 50;

    public const int SenkrechtAbWerk = 0;

    /// <summary>Grundgröße in Einheiten: längste Seite einer Grafik bei "normal"; 100 bis 400 in Schritten zu 10, ab Werk 180.</summary>
    public const int GrundgroesseMin = 100;

    public const int GrundgroesseMax = 400;

    public const int GrundgroesseAbWerk = 180;

    /// <summary>Ausführung als Nummer: 0 Schlicht (ab Werk), 1 Metall, 2 Nur Schrift (<see cref="AusfuehrungKennung"/>).</summary>
    public const int AusfuehrungSchlicht = 0;

    public const int AusfuehrungMetall = 1;

    public const int AusfuehrungSchrift = 2;

    /// <summary>Stopp-Bild als Nummer: 0 Handschuh (ab Werk), 1 Xeno (<see cref="StoppBildKennung"/>).</summary>
    public const int StoppHandschuh = 0;

    public const int StoppXeno = 1;

    public const bool TonAbWerk = true;

    /// <summary>Waagerecht in Prozent: 0 links, 50 mittig, 100 rechts (der Block bleibt ganz im Monitor).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Waagerecht { get; set; } = WaagerechtAbWerk;

    /// <summary>Senkrecht in Prozent: 0 oben, 100 unten.</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Senkrecht { get; set; } = SenkrechtAbWerk;

    /// <summary>Grundgröße in Einheiten (<see cref="GrundgroesseMin"/> bis <see cref="GrundgroesseMax"/>).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Grundgroesse { get; set; } = GrundgroesseAbWerk;

    /// <summary>Ausführung als Nummer (0 bis 2).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Ausfuehrung { get; set; } = AusfuehrungSchlicht;

    /// <summary>Stopp-Bild als Nummer (0 oder 1).</summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int StoppBild { get; set; } = StoppHandschuh;

    /// <summary>Ton bei Befehlen, Textmeldungen und in den letzten 10 s eines Countdowns.</summary>
    [JsonConverter(typeof(NachsichtigAn))]
    public bool Ton { get; set; } = TonAbWerk;

    /// <summary>Die Kennung der Ausführung für den Meldungskatalog: "schlicht", "metall" oder "schrift".</summary>
    [JsonIgnore]
    public string AusfuehrungKennung => Ausfuehrung switch
    {
        AusfuehrungMetall => Funk.Protokoll.Steuerung.Meldungskatalog.Metall,
        AusfuehrungSchrift => Funk.Protokoll.Steuerung.Meldungskatalog.Schrift,
        _ => Funk.Protokoll.Steuerung.Meldungskatalog.Schlicht,
    };

    /// <summary>Das Bild beim Stopp: "stopp_handschuh" oder "stopp_xeno".</summary>
    [JsonIgnore]
    public string StoppBildKennung => StoppBild == StoppXeno ? "stopp_xeno" : "stopp_handschuh";

    internal void Normalisieren()
    {
        if (Waagerecht is < 0 or > LageMax) Waagerecht = WaagerechtAbWerk;
        if (Senkrecht is < 0 or > LageMax) Senkrecht = SenkrechtAbWerk;
        if (Grundgroesse is < GrundgroesseMin or > GrundgroesseMax) Grundgroesse = GrundgroesseAbWerk;
        if (Ausfuehrung is < AusfuehrungSchlicht or > AusfuehrungSchrift) Ausfuehrung = AusfuehrungSchlicht;
        if (StoppBild is < StoppHandschuh or > StoppXeno) StoppBild = StoppHandschuh;
    }
}

/// <summary>Womit man hört (Frage beim ersten Start); danach richtet sich die Echounterdrückung (A14).</summary>
public enum Hoerweise
{
    /// <summary>Kopfhörer oder Headset: kein Echo, die Echounterdrückung bleibt aus (sie dämpft sonst Anlaute bei Spielton).</summary>
    Headset,

    /// <summary>Lautsprecher: Echounterdrückung an, sonst hören die anderen sich selbst.</summary>
    Lautsprecher,
}

/// <summary>Audiogeräte und Verarbeitung.</summary>
public sealed class AudioEinstellungen
{
    public string? EingangId { get; set; }

    public string? AusgangId { get; set; }

    /// <summary>Headset oder Lautsprecher; null heißt: noch nicht gefragt (Karte im Hauptfenster).</summary>
    public Hoerweise? Hoerweise { get; set; }

    /// <summary>A11: erlaubt, Standard an. Seit Klangstufe B heißt aus: Rauschfilter Aus (siehe <see cref="Rauschfilter"/>).</summary>
    public bool Rauschunterdrueckung { get; set; } = true;

    /// <summary>
    /// Rauschfilter Stark statt Standard (Klangstufe B, seit 0.6.0), nur zusammen mit <see cref="Rauschunterdrueckung"/>. Ein eigenes
    /// Feld statt eines neuen Werts, damit ältere Clients die Datei lesen und RNNoise anlassen; sie übergehen das Feld und lassen es
    /// beim Zurückschreiben weg. Nachsichtig gelesen: ein falscher Wert gilt als aus.
    /// </summary>
    [JsonConverter(typeof(NachsichtigAus))]
    public bool StarkerRauschfilter { get; set; }

    /// <summary>Der Rauschfilter aus <see cref="Rauschunterdrueckung"/> und <see cref="StarkerRauschfilter"/>; setzen schreibt beide.</summary>
    [JsonIgnore]
    public Rauschfilter Rauschfilter
    {
        get => !Rauschunterdrueckung ? Rauschfilter.Aus : StarkerRauschfilter ? Rauschfilter.Stark : Rauschfilter.Standard;
        set
        {
            Rauschunterdrueckung = value != Rauschfilter.Aus;
            StarkerRauschfilter = value == Rauschfilter.Stark;
        }
    }

    /// <summary>
    /// A14: Pflicht für alle ohne Headset, Standard an. Seit 0.10.0 (E11) immer der wirksame Wert (<see cref="EchoWirksam"/>), damit ein älterer
    /// Client, der nur dieses Feld kennt, dasselbe einschaltet.
    /// </summary>
    public bool Echounterdrueckung { get; set; } = true;

    /// <summary>
    /// Echounterdrückung von Hand (seit 0.10.0, E11, Bild m-5: Automatisch, An, Aus). <c>null</c> heißt Automatisch: Sie folgt Headset oder
    /// Lautsprecher (<see cref="EchoNachHoerweise"/>). An oder aus gilt, egal was auf Start gewählt ist. JSON
    /// <c>audio.echounterdrueckungVonHand</c>; nachsichtig gelesen (Unlesbares gilt als Automatisch), <c>null</c> wird nicht geschrieben, ältere
    /// Clients übergehen das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigerSchalterOderNull))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EchounterdrueckungVonHand { get; set; }

    /// <summary>Was Automatisch ergibt (E11): mit Headset aus, über Lautsprecher an, ohne Wahl an (sicher ist sicher).</summary>
    public static bool EchoNachHoerweise(Hoerweise? hoerweise) => hoerweise != Ablage.Hoerweise.Headset;

    /// <summary>Die Echounterdrückung, die gilt: von Hand, sonst nach der Hörweise.</summary>
    [JsonIgnore]
    public bool EchoWirksam => EchounterdrueckungVonHand ?? EchoNachHoerweise(Hoerweise);

    /// <summary>
    /// Klangmodule (seit 0.9.0, Sarevoc 06.10.2026, Q48 bis Q50): ab Werk an, danach wie zuletzt geschaltet. Echoschutz der Sprachaktivierung (A14),
    /// Sprachaktivierung überhaupt (aus: jedes Feld sendet nur mit Taste), Mindest-Funklautstärke (Klangstufe C1) und Pegelausgleich beim
    /// Empfänger (Q29). Nachsichtig gelesen wie die Felder der Klangstufe A; ein älterer Client übergeht sie.
    /// </summary>
    [JsonConverter(typeof(NachsichtigAn))]
    public bool Echoschutz { get; set; } = true;

    /// <inheritdoc cref="Echoschutz"/>
    [JsonConverter(typeof(NachsichtigAn))]
    public bool Sprachaktivierung { get; set; } = true;

    /// <inheritdoc cref="Echoschutz"/>
    [JsonConverter(typeof(NachsichtigAn))]
    public bool MindestFunklautstaerke { get; set; } = true;

    /// <inheritdoc cref="Echoschutz"/>
    [JsonConverter(typeof(NachsichtigAn))]
    public bool Pegelausgleich { get; set; } = true;

    /// <summary>
    /// Klangfarbe der eigenen Stimme (Q70 bis Q72, seit 0.9.3): Wärme, Klarheit und Schärfe in ganzen dB von -6 bis +6, ab Werk 0 (neutral). JSON
    /// <c>audio.klangfarbeWaermeDb</c>, <c>audio.klangfarbeKlarheitDb</c>, <c>audio.klangfarbeSchaerfeDb</c>. Nachsichtig gelesen: Was keine Zahl
    /// ist, gilt als 0, Kommazahlen werden gerundet, Werte außerhalb begrenzt (beim Normalisieren); ältere Clients übergehen die Felder.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int KlangfarbeWaermeDb { get; set; }

    /// <inheritdoc cref="KlangfarbeWaermeDb"/>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int KlangfarbeKlarheitDb { get; set; }

    /// <inheritdoc cref="KlangfarbeWaermeDb"/>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int KlangfarbeSchaerfeDb { get; set; }

    /// <summary>Die drei Werte der Klangfarbe zusammen, begrenzt; setzen schreibt alle drei.</summary>
    [JsonIgnore]
    public KlangfarbeEinstellung Klangfarbe
    {
        get => new KlangfarbeEinstellung(KlangfarbeWaermeDb, KlangfarbeKlarheitDb, KlangfarbeSchaerfeDb).Begrenzt();
        set
        {
            var k = value.Begrenzt();
            (KlangfarbeWaermeDb, KlangfarbeKlarheitDb, KlangfarbeSchaerfeDb) = (k.WaermeDb, k.KlarheitDb, k.SchaerfeDb);
        }
    }

    /// <summary>Funk-Lautstärke ab Werk: ganz rechts, so laut wie bis 0.9.3 (E7).</summary>
    public const int FunkLautstaerkeAbWerk = 100;

    /// <summary>
    /// Funk-Lautstärke in Prozent (seit 0.10.0, Q74, E7; JSON <c>audio.funkLautstaerkeProzent</c>): wie laut du den ganzen Funk hörst, 0 bis 100,
    /// ab Werk <see cref="FunkLautstaerkeAbWerk"/>; für bisherige Nutzer ändert sich also nichts. Nachsichtig gelesen: Was keine Zahl ist oder unter
    /// 0 liegt, gilt als ab Werk (ein Fehler in der Datei macht den Funk nie stumm), über 100 wird 100, Kommazahlen werden gerundet. Ältere
    /// Clients übergehen das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeZahlOderNull))]
    public int? FunkLautstaerkeProzent { get; set; } = FunkLautstaerkeAbWerk;

    /// <summary>Die Funk-Lautstärke, die gilt (<see cref="FunkLautstaerkeProzent"/>, ohne gültigen Wert ab Werk).</summary>
    [JsonIgnore]
    public int FunkLautstaerke => FunkLautstaerkeBegrenzen(FunkLautstaerkeProzent);

    /// <summary>Ein gemerkter Wert in 0 bis 100; fehlt er oder liegt er unter 0, gilt ab Werk (nie stumm durch einen Fehler).</summary>
    public static int FunkLautstaerkeBegrenzen(int? prozent) => prozent is { } p and >= 0 ? Math.Min(p, 100) : FunkLautstaerkeAbWerk;

    /// <summary>
    /// Der Faktor für die Klangmaschine (E7): Prozent geteilt durch 100, zum Quadrat. So folgt der Regler dem Gehör: 0 % ist stumm, 50 % etwa
    /// -12 dB, 100 % unverändert.
    /// </summary>
    public static float FunkLautstaerkeFaktor(int prozent)
    {
        var f = Math.Clamp(prozent, 0, 100) / 100f;
        return f * f;
    }

    /// <summary>Audio beim Start öffnen.</summary>
    public bool BeimStartOeffnen { get; set; } = true;

    /// <summary>
    /// Automatische Lautstärke (Klangstufe A, seit 0.6.0): an heißt Halteregel (gleicht nur die Grundlautstärke an, in Pausen),
    /// aus heißt feste Verstärkung (<see cref="FesteVerstaerkungDb"/>). Ab Werk an; ein älterer Client übergeht das Feld. Die vier
    /// Felder der Klangstufe A werden nachsichtig gelesen: Ein falscher Wert gilt als fehlend, die übrige Datei bleibt.
    /// </summary>
    [JsonConverter(typeof(NachsichtigAn))]
    public bool AutomatischeLautstaerke { get; set; } = true;

    /// <summary>Eingemessene feste Verstärkung in dB; null heißt: noch nicht eingemessen.</summary>
    [JsonConverter(typeof(NachsichtigeDezibel))]
    public double? FesteVerstaerkungDb { get; set; }

    /// <summary>
    /// Was die Automatik zuletzt gelernt hat (dB), damit die nächste Sitzung dort beginnt statt beim Startwert; null heißt: noch
    /// nichts gelernt.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeDezibel))]
    public double? GelernteVerstaerkungDb { get; set; }

    /// <summary>
    /// Mikrofon (Kennung), für das <see cref="GelernteVerstaerkungDb"/> gelernt wurde; null heißt unbekannt. Läuft die nächste
    /// Sitzung auf einem anderen, lernt die Automatik neu (Gegenprüfung Klangstufe A). Nachsichtig gelesen wie die anderen.
    /// </summary>
    [JsonConverter(typeof(NachsichtigerText))]
    public string? GelernteVerstaerkungEingang { get; set; }

    /// <summary>
    /// Die Funkstimme vor der Pegelstufe (dBFS) zu <see cref="GelernteVerstaerkungDb"/>: beim Einmessen gemessen, sonst der Anker, den die Pegelstufe
    /// gelernt hat. Die Maschine setzt den Anker der Mindest-Funklautstärke daraus statt aus Ziel minus Verstärkung (Gegenprüfung C1, H1: bei
    /// begrenzter Verstärkung lag er sonst um die Begrenzung zu hoch). Null: unbekannt. Nachsichtig gelesen, ältere Clients übergehen das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeDezibel))]
    public double? GelernteStimmeDbfs { get; set; }

    /// <summary>Die beim Einmessen gemessene Funkstimme vor der Pegelstufe (dBFS) zu <see cref="FesteVerstaerkungDb"/>; null: unbekannt (H1).</summary>
    [JsonConverter(typeof(NachsichtigeDezibel))]
    public double? FesteStimmeDbfs { get; set; }

    /// <summary>
    /// Mikrofon (Kennung), auf dem <see cref="FesteVerstaerkungDb"/> eingemessen wurde; null heißt unbekannt. Öffnet die Maschine ein anderes, gilt
    /// die Stimme dort als unbekannt (Gegenprüfung C1, M4: sonst bekam ein anderes, leiseres Mikrofon den alten Anker).
    /// </summary>
    [JsonConverter(typeof(NachsichtigerText))]
    public string? FestEingang { get; set; }

    /// <summary>Klangprotokoll der Sendekette in den Ordner der Einstellungen schreiben (Fehlersuche, ab Werk aus).</summary>
    [JsonConverter(typeof(NachsichtigAus))]
    public bool Klangprotokoll { get; set; }

    /// <summary>
    /// <see cref="FesteVerstaerkungDb"/> stammt aus einem Einmessen (Klangstufe C1): Mit fester Verstärkung gilt die Stimme dann als bekannt,
    /// und die Mindest-Funklautstärke wirkt. Ein fester Wert, der nur von der Automatik übernommen wurde, zählt nicht. Ältere Clients
    /// übergehen das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigAus))]
    public bool FestEingemessen { get; set; }

    /// <summary>
    /// Was die Klangmaschine bekommt: Automatik ab dem gelernten Wert (setzt dort fort, für das Mikrofon, auf dem er gelernt
    /// wurde), sonst ab dem eingemessenen (ebenso), sonst ab <see cref="Pegelstufe.StartDb"/> mit den Anfangsschritten; fest der
    /// eingemessene (sonst der gelernte) Wert, ohne beides <see cref="Pegelstufe.StartDb"/>, als gelernt nur, wenn er eingemessen ist.
    /// </summary>
    public LautstaerkeEinstellung LautstaerkeFuerMaschine()
    {
        if (!AutomatischeLautstaerke)
        {
            if (FesteVerstaerkungDb is { } f)
                return new LautstaerkeEinstellung(false, (float)f, Gelernt: FestEingemessen, Eingang: FestEingemessen ? FestEingang : null, StimmeDbfs: FestEingemessen ? Db(FesteStimmeDbfs) : float.NaN);
            return new LautstaerkeEinstellung(false, (float)(GelernteVerstaerkungDb ?? Pegelstufe.StartDb));
        }
        if (GelernteVerstaerkungDb is { } gelernt)
            return new LautstaerkeEinstellung(true, (float)gelernt, Gelernt: true, Eingang: GelernteVerstaerkungEingang, StimmeDbfs: Db(GelernteStimmeDbfs));
        if (FesteVerstaerkungDb is { } fest) return new LautstaerkeEinstellung(true, (float)fest, Gelernt: true, Eingang: FestEingang, StimmeDbfs: Db(FesteStimmeDbfs));
        return new LautstaerkeEinstellung(true, Pegelstufe.StartDb);
    }

    private static float Db(double? stimme) => stimme is { } s ? (float)s : float.NaN;

    /// <summary>Eine gemerkte Funkstimme vor der Pegelstufe auf 0,1 dB; außerhalb -120 bis 0 dBFS oder keine Zahl wird null.</summary>
    public static double? Stimme(double? dbfs) => dbfs is { } w && double.IsFinite(w) && w is >= -120 and <= 0 ? Math.Round(w, 1) : null;

    /// <summary>Eine Verstärkung in die Grenzen der Pegelstufe, auf 0,1 dB; was keine Zahl ist, wird null.</summary>
    public static double? Verstaerkung(double? db) =>
        db is { } w && double.IsFinite(w) ? Math.Round(Math.Clamp(w, Pegelstufe.MinDb, Pegelstufe.MaxDb), 1) : null;

    internal void Normalisieren()
    {
        if (EingangId is { Length: > 512 }) EingangId = null;
        if (AusgangId is { Length: > 512 }) AusgangId = null;
        if (Hoerweise is { } h && !Enum.IsDefined(h)) Hoerweise = null;
        FesteVerstaerkungDb = Verstaerkung(FesteVerstaerkungDb);
        GelernteVerstaerkungDb = Verstaerkung(GelernteVerstaerkungDb);
        if (GelernteVerstaerkungEingang is { Length: > 512 } || GelernteVerstaerkungDb is null) GelernteVerstaerkungEingang = null;
        GelernteStimmeDbfs = GelernteVerstaerkungDb is null ? null : Stimme(GelernteStimmeDbfs);
        if (FesteVerstaerkungDb is null) FestEingemessen = false;
        FesteStimmeDbfs = FesteVerstaerkungDb is null ? null : Stimme(FesteStimmeDbfs);
        if (FestEingang is { Length: > 512 } || FesteVerstaerkungDb is null) FestEingang = null;
        KlangfarbeWaermeDb = KlangfarbeEinstellung.Begrenzen(KlangfarbeWaermeDb);
        KlangfarbeKlarheitDb = KlangfarbeEinstellung.Begrenzen(KlangfarbeKlarheitDb);
        KlangfarbeSchaerfeDb = KlangfarbeEinstellung.Begrenzen(KlangfarbeSchaerfeDb);
        FunkLautstaerkeProzent = FunkLautstaerkeBegrenzen(FunkLautstaerkeProzent);
        // E11: Weicht eine Datei von vor 0.10.0 von dem ab, was die Hörweise ergäbe, hast du die Echounterdrückung von Hand umgestellt; das bleibt
        // so (einmal umgedeutet). Danach trägt das alte Feld den wirksamen Wert.
        if (EchounterdrueckungVonHand is null && Echounterdrueckung != EchoNachHoerweise(Hoerweise)) EchounterdrueckungVonHand = Echounterdrueckung;
        Echounterdrueckung = EchoWirksam;
    }

    /// <summary>
    /// Umstieg von 0.6.0 (Klangstufe C1): Bis dahin merkte der Client die Verstärkung der Automatik auch, wenn sie nie gelernt hatte (nichts
    /// gesendet, etwa Kanal "Ruhe"); sie stand dann auf dem Startwert <see cref="Pegelstufe.StartDb"/>. Als "gelernt" gemerkt hätte die
    /// Pegelstufe die Stimme für bekannt gehalten, die Mindest-Funklautstärke gesetzt und bei leisem Mikrofon jede Sendung gesperrt. Ein
    /// gemerkter Wert genau auf dem Startwert gilt deshalb als nie gelernt und fällt weg.
    /// </summary>
    internal void AufC1Umstellen()
    {
        if (GelernteVerstaerkungDb is not { } g || Math.Abs(g - Pegelstufe.StartDb) > 0.05) return;
        GelernteVerstaerkungDb = null;
        GelernteVerstaerkungEingang = null;
        GelernteStimmeDbfs = null;
    }
}

/// <summary>
/// Auto Dauersend-Dämpfung (Klangstufe C2a): Schalter (Q1, ab Werk an) und Stufe für andere Dauersender (Q26, ab Werk "deutlich leiser"). Ein eigener
/// Abschnitt <c>daempfung</c>: Dateien der Version 2 ohne ihn bekommen die Werte ab Werk, ohne Umstellen; ältere Clients übergehen ihn. Nachsichtig
/// gelesen: ein unlesbarer Wert gilt als ab Werk, die übrige Datei bleibt.
/// </summary>
public sealed class DaempfungWerte
{
    [JsonConverter(typeof(NachsichtigAn))]
    public bool Auto { get; set; } = true;

    [JsonConverter(typeof(NachsichtigeStufe))]
    public Daempfungsstufe Stufe { get; set; } = Daempfungsstufe.DeutlichLeiser;

    /// <summary>
    /// "Leises Reden im Raum aussperren (Erprobung)", ab Werk aus (Nacharbeit Gegenprüfung C2a, H2); nachsichtig gelesen. Seit 0.10.0 (E15) ohne
    /// Schalter und ohne Wirkung: Die Maschine bekommt immer aus (<see cref="AlsEinstellung"/>). Das Feld bleibt lesbar und wird unverändert
    /// zurückgeschrieben, damit ein älterer Client seine Datei vorfindet.
    /// </summary>
    [JsonConverter(typeof(NachsichtigAus))]
    public bool LeisesRedenSperren { get; set; }

    /// <summary>Was die Maschine bekommt: Schalter und Stufe; "Leises Reden aussperren" seit 0.10.0 immer aus (E15).</summary>
    public DaempfungEinstellung AlsEinstellung() => new(Auto, Stufe, LeisesRedenSperren: false);
}

/// <summary>Stufe nachsichtig gelesen; ein unbekannter Wert wird "deutlich leiser" (ab Werk), nicht der erste Wert.</summary>
public sealed class NachsichtigeStufe : NachsichtigeAufzaehlung<Daempfungsstufe>
{
    protected override Daempfungsstufe Ersatz => DaempfungEinstellung.AbWerk.Stufe;
}

/// <summary>
/// Werte der Sprachaktivierung (A26-A28) als ablegbare Form von <see cref="GateEinstellung"/>. Grenzen: Pegel in dBFS
/// zwischen <see cref="MinDb"/> und 0, Korridor mindestens <see cref="KorridorMinBreiteDb"/> breit.
/// </summary>
public sealed class GateWerte
{
    public const float MinDb = -90f;
    public const float MaxDb = 0f;
    public const float AbstandMinDb = 3f;
    public const float AbstandMaxDb = 30f;

    /// <summary>
    /// Höchste Grenzlinie der Automatik (Schritt 3.5, N3 der Gegenprüfung 2c): Darüber ließe sich niemand mehr leise hören, bis 0 dBFS
    /// blieb das Gate zu. Dateien mit einem höheren Wert werden hierher gezogen.
    /// </summary>
    public const float GrenzeMaxDb = -30f;

    /// <summary>
    /// Ab hier wirkt die Grenzlinie (N4 der Gegenprüfung 2c): Die automatische Linie liegt nie unter -69 dBFS, dazu drei dB Spielraum.
    /// Darunter ändert sie nichts, und die Anzeige sagt "keine Grenze".
    /// </summary>
    public const float GrenzeWirktAbDb = -66f;
    public const float KorridorMinBreiteDb = 3f;
    public const float ZeitMinSek = 0.1f;
    public const float AnstiegMaxSek = 30f;
    public const float RueckkehrMaxSek = 60f;

    public GateModus Modus { get; set; } = GateModus.Automatisch;

    /// <summary>
    /// A28: Schalter "Lernen". Solange die Automatik läuft, folgt er dem Modus (Lernend); ist die Automatik aus (Fest),
    /// merkt er sich die Wahl, damit sie beim Wiedereinschalten der Automatik gilt.
    /// </summary>
    public bool Lernen { get; set; }

    public float SchwelleDb { get; set; } = -45f;
    public float AbstandDb { get; set; } = 9f;
    public float KorridorVonDb { get; set; } = -55f;
    public float KorridorBisDb { get; set; } = -30f;
    public float AnstiegSekunden { get; set; } = 2f;
    public float RueckkehrSekunden { get; set; } = 5f;
    public float Sprachsicherheit { get; set; } = 0.5f;

    /// <summary>
    /// Grenzlinie der Automatik (Sarevoc, Stufe 2c), absolut: Darunter ging die automatische Schwelle nie. Seit Klangstufe C1 ersetzt durch die
    /// Mindest-Funklautstärke relativ zur gelernten Stimme (<see cref="MindestAbstandDb"/>); beim Umstieg wird ein selbst gesetzter Wert dorthin
    /// übertragen und die Grenzlinie wieder ganz unten (<see cref="MinDb"/>, ohne Wirkung) gesetzt, denn eine feste Grenze sperrte bei leisem
    /// Mikrofon jede Sendung, bevor die Pegelstufe lernen kann. Ältere Clients lesen sie weiter.
    /// </summary>
    public float GrenzlinieDb { get; set; } = MinDb;

    /// <summary>
    /// Mindest-Funklautstärke (Klangstufe C1): so viele dB unter der gelernten Funkstimme liegt die Untergrenze der Linie in Automatisch und
    /// Lernend; ab Werk <see cref="GateVorgaben.MindestAbstandAbWerkDb"/>. Ändert sich durch Ziehen am Pegelbalken oder in der Grafik und durch
    /// "Mindestlautstärke einmessen". Solange die Stimme nicht gelernt ist, wirkt sie nicht.
    /// </summary>
    public float MindestAbstandDb { get; set; } = GateVorgaben.MindestAbstandAbWerkDb;

    /// <summary>Obergrenze der Linie (Klangstufe C1): so viele dB unter der gelernten Funkstimme, mindestens 6.</summary>
    public float ObergrenzeAbstandDb { get; set; } = GateVorgaben.ObergrenzeAbstandAbWerkDb;

    /// <summary>
    /// "Mindestlautstärke einmessen" ist einmal gelungen (Klangstufe C1); bis dahin steht auf der Startseite der Hinweis
    /// "Mindestlautstärke noch nicht eingemessen" mit dem Knopf. Ältere Clients übergehen das Feld.
    /// </summary>
    public bool MindestEingemessen { get; set; }

    /// <summary>
    /// Mikrofon (Kennung), auf dem eingemessen wurde (Gegenprüfung C1, M4); null heißt unbekannt (Dateien bis klang-c2a: gilt für jedes). Läuft ein
    /// anderes Mikrofon, kommt der Hinweis "noch nicht eingemessen" zurück. Ältere Clients übergehen das Feld.
    /// </summary>
    public string? MindestEingemessenEingang { get; set; }

    /// <summary>Gilt das Einmessen für dieses Mikrofon (null: noch keins offen)?</summary>
    public bool EingemessenFuer(string? eingang) =>
        MindestEingemessen && (MindestEingemessenEingang is null || eingang is null || string.Equals(MindestEingemessenEingang, eingang, StringComparison.Ordinal));

    public GateEinstellung AlsEinstellung() =>
        new(Modus, SchwelleDb, AbstandDb, KorridorVonDb, KorridorBisDb, AnstiegSekunden, RueckkehrSekunden, Sprachsicherheit, GrenzlinieDb,
            MindestAbstandDb, ObergrenzeAbstandDb);

    /// <summary>Voreinstellung von "Korridor von" in Dateien bis 0.6.0 (nötig für den Umstieg).</summary>
    private const float KorridorVonAbWerkDb = -55f;

    /// <summary>Voreinstellung von "Korridor bis" in Dateien bis 0.6.0.</summary>
    private const float KorridorBisAbWerkDb = -30f;

    /// <summary>
    /// Umstieg von 0.6.0 (Klangstufe C1, Sarevoc: "Wer die Grenzlinie nie verstellt hat, bekommt den neuen Wert; ein selbst gesetzter Wert bleibt"):
    /// Eine wirkende Grenzlinie (über <see cref="GrenzeWirktAbDb"/>) wird zum Abstand unter dem Ziel der Pegelstufe (-23 dBFS), wo die gelernte
    /// Stimme liegt; sonst im Modus Lernend ein selbst gesetztes "Korridor von" (dort hatte es die Rolle der Grenzlinie); sonst der Wert ab Werk.
    /// Die Grenzlinie selbst steht danach ganz unten (keine feste Grenze mehr).
    /// </summary>
    internal void AufC1Umstellen()
    {
        if (float.IsFinite(GrenzlinieDb) && GrenzlinieDb > GrenzeWirktAbDb) MindestAbstandDb = Pegelstufe.ZielDbfs - GrenzlinieDb;
        else if (Modus == GateModus.Lernend && float.IsFinite(KorridorVonDb) && Math.Abs(KorridorVonDb - KorridorVonAbWerkDb) > 0.05f)
            MindestAbstandDb = Pegelstufe.ZielDbfs - KorridorVonDb;
        else MindestAbstandDb = GateVorgaben.MindestAbstandAbWerkDb;
        // Gegenprüfung C1, N3: Im Modus Lernend bleibt ein selbst gesetztes "Korridor bis" als Obergrenze (Abstand unter dem Ziel), mindestens 6 dB unter
        // der Stimme und mindestens die Mindestbreite über der Untergrenze.
        if (Modus == GateModus.Lernend && float.IsFinite(KorridorBisDb) && Math.Abs(KorridorBisDb - KorridorBisAbWerkDb) > 0.05f)
        {
            ObergrenzeAbstandDb = Math.Max(GateVorgaben.MinObergrenzeAbstandDb,
                Math.Min(Pegelstufe.ZielDbfs - KorridorBisDb, MindestAbstandDb - GateVorgaben.KorridorMindestBreiteDb));
        }
        GrenzlinieDb = MinDb;
    }

    /// <summary>Automatik an: Schwelle folgt dem Rauschboden (Automatisch oder Lernend).</summary>
    [JsonIgnore]
    public bool Automatik => Modus != GateModus.Fest;

    /// <summary>Modus aus den beiden Schaltern von A28.</summary>
    public static GateModus ModusAus(bool automatik, bool lernen) =>
        !automatik ? GateModus.Fest : lernen ? GateModus.Lernend : GateModus.Automatisch;

    /// <summary>Übernimmt <paramref name="e"/> und normalisiert.</summary>
    public void Uebernehmen(GateEinstellung e)
    {
        Modus = e.Modus;
        SchwelleDb = e.SchwelleDb;
        AbstandDb = e.AbstandDb;
        KorridorVonDb = e.KorridorVonDb;
        KorridorBisDb = e.KorridorBisDb;
        AnstiegSekunden = e.AnstiegSekunden;
        RueckkehrSekunden = e.RueckkehrSekunden;
        Sprachsicherheit = e.Sprachsicherheit;
        GrenzlinieDb = e.GrenzlinieDb;
        MindestAbstandDb = e.MindestAbstandDb;
        ObergrenzeAbstandDb = e.ObergrenzeAbstandDb;
        Normalisieren();
    }

    public void Normalisieren()
    {
        if (!Enum.IsDefined(Modus)) Modus = GateModus.Automatisch;
        if (Modus != GateModus.Fest) Lernen = Modus == GateModus.Lernend;
        SchwelleDb = Begrenzen(SchwelleDb, MinDb, MaxDb, -45f);
        AbstandDb = Begrenzen(AbstandDb, AbstandMinDb, AbstandMaxDb, 9f);
        KorridorVonDb = Begrenzen(KorridorVonDb, MinDb, MaxDb - KorridorMinBreiteDb, -55f);
        KorridorBisDb = Begrenzen(KorridorBisDb, MinDb + KorridorMinBreiteDb, MaxDb, -30f);
        if (KorridorBisDb - KorridorVonDb < KorridorMinBreiteDb)
        {
            // Von hat Vorrang; Bis rückt nach oben, notfalls rückt Von nach unten.
            KorridorBisDb = Math.Min(MaxDb, KorridorVonDb + KorridorMinBreiteDb);
            KorridorVonDb = KorridorBisDb - KorridorMinBreiteDb;
        }
        AnstiegSekunden = Begrenzen(AnstiegSekunden, ZeitMinSek, AnstiegMaxSek, 2f);
        RueckkehrSekunden = Begrenzen(RueckkehrSekunden, ZeitMinSek, RueckkehrMaxSek, 5f);
        Sprachsicherheit = Begrenzen(Sprachsicherheit, 0f, 1f, 0.5f);
        GrenzlinieDb = Begrenzen(GrenzlinieDb, MinDb, GrenzeMaxDb, MinDb);
        // Wie GateVorgaben.Bereinigen: Obergrenze mindestens 6 dB unter der Stimme, Untergrenze mindestens 3 dB darunter.
        ObergrenzeAbstandDb = Begrenzen(ObergrenzeAbstandDb, GateVorgaben.MinObergrenzeAbstandDb,
            GateVorgaben.MaxMindestAbstandDb - GateVorgaben.KorridorMindestBreiteDb, GateVorgaben.ObergrenzeAbstandAbWerkDb);
        MindestAbstandDb = Begrenzen(MindestAbstandDb, ObergrenzeAbstandDb + GateVorgaben.KorridorMindestBreiteDb, GateVorgaben.MaxMindestAbstandDb,
            GateVorgaben.MindestAbstandAbWerkDb);
    }

    internal static float Begrenzen(float wert, float min, float max, float ersatz) =>
        float.IsFinite(wert) ? Math.Clamp(wert, min, max) : ersatz;
}

/// <summary>
/// Wie das Funkfeld seine Felder zeigt. Sarevoc, 29.09.2026: Das Funkfeld läuft als Overlay über dem Spiel, und nicht jeder hat
/// zwei Monitore; es muss so kompakt werden wie ein 10er-Feld gängiger Funkprogramme (etwa 800 x 222 Einheiten). Entschieden nach den
/// Vergleichsbildern (A29): A Raster ist die Voreinstellung, C Liste die Alternative; B Leiste ist entfallen. Die Zahl 1
/// war die Leiste und bleibt frei; eine gemerkte "Leiste" wird beim Lesen still zum Raster
/// (<see cref="NachsichtigeAufzaehlung{T}"/> an <see cref="FunkfeldEinstellungen.Darstellung"/>).
/// </summary>
public enum FunkfeldDarstellung
{
    /// <summary>A: 2 Reihen zu 5 (bei 5 Feldern eine), je Feld Zustand, Sprecher, Pegel, Regler und kleine Knöpfe.</summary>
    Raster = 0,

    /// <summary>C: senkrecht und schmal für den Bildschirmrand, eine Zeile je Feld.</summary>
    Liste = 2,
}

/// <summary>
/// Platz des Funkfelds aus der Zeit vor dem 30.09.2026, in Einheiten: die Lage, dazu eine Größe, wenn das Fenster am Rand
/// gezogen war. Nur noch gelesen (<see cref="FunkfeldEinstellungen.Orte"/>), und davon nur die Lage: Die Größen stammen
/// womöglich vom Funkfeld, das über beide Bildschirme aufging (Befund 30.09.2026).
/// </summary>
public sealed record FunkfeldPlatz(double Links, double Oben, double? Breite = null, double? Hoehe = null)
{
    /// <summary><c>null</c>, wenn die Lage unbrauchbar ist.</summary>
    public static FunkfeldPlatz? OderNull(FunkfeldPlatz? p) =>
        p is null || FensterStand.OderNull(new FensterStand(p.Links, p.Oben, 1, 1)) is null ? null : p;
}

/// <summary>
/// Wo die Meldezeile auf dem Monitor steht (seit 0.11.1, Sarevoc 12.10.2026: "an einem festen Platz am Bildschirm, nicht da, wo das Funkfeld
/// ist"): eines von neun Feldern, drei Zeilen mal drei Spalten, ab Werk oben Mitte. Gerechnet in <c>Fenster\Meldezeilenlage.Fest</c>.
/// </summary>
public enum Meldezeilenstelle
{
    ObenLinks,
    ObenMitte,
    ObenRechts,
    MitteLinks,
    Mitte,
    MitteRechts,
    UntenLinks,
    UntenMitte,
    UntenRechts,
}

/// <summary>Die Wahl unter Einstellungen, wo die Meldezeile steht: die Stelle im Raster und der Monitor (<c>null</c>: Automatisch).</summary>
public readonly record struct Meldezeilenwahl(Meldezeilenstelle Stelle, int? Monitor);

/// <summary>Die Stelle der Meldezeile nachsichtig gelesen; ein unbekannter Wert wird oben Mitte, die Voreinstellung, nicht der erste Wert.</summary>
public sealed class NachsichtigeMeldezeilenstelle : NachsichtigeAufzaehlung<Meldezeilenstelle>
{
    protected override Meldezeilenstelle Ersatz => FunkfeldEinstellungen.MeldezeileStelleAbWerk;
}

/// <summary>Das Funkfeld-Fenster (A24).</summary>
public sealed class FunkfeldEinstellungen
{
    /// <summary>Die Meldezeile ab Werk oben Mitte (Sarevoc 12.10.2026).</summary>
    public const Meldezeilenstelle MeldezeileStelleAbWerk = Meldezeilenstelle.ObenMitte;

    /// <summary>Höchste Nummer eines Monitors für <see cref="MeldezeileMonitor"/> (mehr Monitore hat niemand).</summary>
    public const int MeldezeileMonitorMax = 16;

    /// <summary>
    /// Die Stelle der Meldezeile auf dem Monitor (seit 0.11.1, JSON <c>funkfeld.meldezeileStelle</c>): eines von neun Feldern, ab Werk
    /// <see cref="MeldezeileStelleAbWerk"/>. Bis 0.11.0 hing die Meldezeile am Funkfeld. Nachsichtig gelesen, ein älterer Client übergeht das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeMeldezeilenstelle))]
    public Meldezeilenstelle MeldezeileStelle { get; set; } = MeldezeileStelleAbWerk;

    /// <summary>
    /// Der Monitor der Meldezeile (seit 0.11.1, JSON <c>funkfeld.meldezeileMonitor</c>): <c>null</c> heißt Automatisch (der Monitor, auf dem Star
    /// Citizen läuft, sonst der Hauptmonitor), 0 der Hauptmonitor, 1 bis <see cref="MeldezeileMonitorMax"/> der n-te Monitor, wie Windows sie
    /// aufzählt; fehlt er gerade, gilt der Hauptmonitor. Nachsichtig gelesen, <c>null</c> wird nicht geschrieben, ein älterer Client übergeht das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeZahlOderNull))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MeldezeileMonitor { get; set; }

    /// <summary>
    /// Höchste Durchsichtigkeit in Prozent (Sarevoc, 30.09.2026: das ganze Fenster soll durchsichtig werden). Das Fenster behält
    /// mindestens 15 % Deckkraft, damit es greifbar bleibt: Auf ganz durchsichtigen Stellen eines geschichteten Fensters
    /// fallen Mausklicks durch.
    /// </summary>
    public const int DurchsichtigkeitMax = 85;

    /// <summary>Kleinste und größte Skalierung des Funkfelds in Prozent (Sarevoc Q12: minus bis plus 30 Prozent).</summary>
    public const int SkalierungMin = -30;

    public const int SkalierungMax = 30;

    /// <summary>5 oder 10 Kacheln, Global eingerechnet.</summary>
    public int Ansicht { get; set; } = 10;

    /// <summary>
    /// Skalierung des Funkfelds in Prozent, <see cref="SkalierungMin"/> bis <see cref="SkalierungMax"/>, ab Werk 0 (seit 0.6.0, Sarevoc Q12).
    /// Das Funkfeld hat je Ansicht eine feste Mindestgröße, gerade so groß, dass alles zu sehen ist; die Skalierung macht es als Ganzes
    /// kleiner oder größer. Nachsichtig gelesen, ein älterer Client übergeht das Feld.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeZahl))]
    public int Skalierung { get; set; }

    /// <summary>
    /// Die zuletzt gewählte Rolle aus der Rollenliste (seit 0.6.0, 6.16). Setzt der Server nach der Wiederverbindungsfrist (10 min) auf
    /// STANDARD zurück, stellt der Client sie damit und mit den gemerkten Ch. wieder her; teilt die Einsatzleitung dich anders ein,
    /// fällt sie weg. Nachsichtig gelesen.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeZahlOderNull))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Rolle { get; set; }

    /// <summary>Raster (Voreinstellung, A29) oder Liste; eine gemerkte Leiste wird still zum Raster.</summary>
    [JsonConverter(typeof(NachsichtigeAufzaehlung<FunkfeldDarstellung>))]
    public FunkfeldDarstellung Darstellung { get; set; } = FunkfeldDarstellung.Raster;

    /// <summary>0 bis <see cref="DurchsichtigkeitMax"/> Prozent (<see cref="Durchsicht"/>).</summary>
    public int Durchsichtigkeit { get; set; }

    /// <summary>
    /// Die Option "Schrift und Symbole deckend halten", ab Werk aus: Dann werden nur Lücken und Titelzeile durchsichtig, und
    /// Kacheln, Schrift und Zustandsfarben bleiben über hellem Spielbild lesbar (<c>Design.FunkfeldDeckung</c>). Ohne sie
    /// (der Hauptweg, Sarevoc 30.09.2026) wird das ganze Fenster gleichmäßig durchsichtig.
    /// <para>
    /// Ältere Dateien kennen stattdessen "ganzesFenster". Dessen <c>false</c> war die alte Voreinstellung, keine Wahl, und
    /// Sarevoc hat genau diese Wirkung verworfen; sein <c>true</c> ist der neue Hauptweg. Beide werden deshalb zum Hauptweg, das
    /// alte Feld wird still übergangen und nicht mehr geschrieben.
    /// </para>
    /// </summary>
    public bool SchriftDeckend { get; set; }

    /// <summary>Beim Start zeigen; aus heißt seit 0.5.0: abgelegt im Infobereich (RAID-S2-ENTWURF Abschnitt 5).</summary>
    public bool BeimStartZeigen { get; set; } = true;

    /// <summary>
    /// Der Hinweis beim ersten Ablegen (<see cref="Modelle.FunkfeldAblage.ErsterHinweis"/>, seit 0.11.0 "Funkfeld abgelegt: Taste oder Symbol (^)
    /// holt es") stand schon einmal da (seit 0.5.0; ein älterer Client übergeht das Feld).
    /// </summary>
    public bool AblageHinweisGezeigt { get; set; }

    /// <summary>
    /// Platz aus der Zeit vor den Ansichten (eine Reihe aus 10 Kacheln, etwa 1640 Einheiten breit). Nur seine Lage gilt
    /// noch, solange für eine Ansicht nichts gemerkt ist; seine Größe passt zu keiner Ansicht mehr.
    /// </summary>
    public FensterStand? Fenster { get; set; }

    /// <summary>
    /// Plätze aus modul/funkfeld (29.09.2026): Dort wurde jede Größe gemerkt, auch die vom Inhalt bestimmte. Beim Lesen
    /// übernimmt <see cref="Orte"/> nur ihre Lage; geschrieben wird das Feld nicht mehr.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, FensterStand>? Plaetze { get; set; }

    /// <summary>
    /// Plätze vom 29. und 30.09.2026 in Einheiten, Schlüssel wie <see cref="Schluessel"/>. Nur noch gelesen, und nur ihre Lage
    /// (<see cref="OrtFuer"/>); sobald du das Funkfeld in einer Ansicht bewegst, gilt dort <see cref="Fensterorte"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, FunkfeldPlatz>? Orte { get; set; }

    /// <summary>
    /// Platz je Ansicht und Feldzahl (A32), Schlüssel wie <see cref="Schluessel"/>, etwa "Raster-10": Lage in Bildpunkten,
    /// dazu eine Größe in Einheiten, wenn du das Funkfeld am Rand gezogen hast (<see cref="FensterOrt"/>).
    /// </summary>
    public Dictionary<string, FensterOrt> Fensterorte { get; } = new(StringComparer.Ordinal);

    /// <summary>Schlüssel in <see cref="Fensterorte"/>.</summary>
    public static string Schluessel(FunkfeldDarstellung darstellung, int anzahl) =>
        darstellung.ToString() + "-" + (anzahl >= 10 ? "10" : "5");

    /// <summary>
    /// Wo das Funkfeld für diese Ansicht und Feldzahl hin soll: an den eigenen Platz, sonst an die Lage der anderen Feldzahl
    /// derselben Ansicht (ohne deren Größe), sonst an die Lage aus einer älteren Datei, mit der Skala des Hauptmonitors
    /// (<paramref name="hauptSkala"/>) in Bildpunkte umgesetzt. <c>null</c>: kein Anhalt.
    /// </summary>
    public FensterOrt? OrtFuer(FunkfeldDarstellung darstellung, int anzahl, double hauptSkala)
    {
        var eigen = Schluessel(darstellung, anzahl);
        var andere = Schluessel(darstellung, anzahl >= 10 ? 5 : 10);
        if (Fensterorte.TryGetValue(eigen, out var o)) return o;
        if (Fensterorte.TryGetValue(andere, out var a)) return a with { Breite = null, Hoehe = null };
        var alt = Orte?.GetValueOrDefault(eigen) ?? Orte?.GetValueOrDefault(andere);
        var stand = alt is not null ? new FensterStand(alt.Links, alt.Oben, 1, 1) : Fenster;
        return FensterOrt.AusAltemStand(stand, hauptSkala, mitGroesse: false) is { } umgesetzt ? umgesetzt with { Maximiert = false } : null;
    }

    /// <summary>
    /// Merkt, wohin du das Funkfeld in dieser Ansicht und Feldzahl geschoben hast, die Größe nur mit
    /// <paramref name="groesseGezogen"/>; nur geschoben bleibt eine früher gezogene Größe. Die Lage gilt für beide
    /// Feldzahlen derselben Ansicht, damit das Umschalten zwischen 5 und 10 das Fenster nicht springen lässt; die Größe nur
    /// für diese. Alte Plätze der Ansicht sind damit abgelöst.
    /// </summary>
    public void Merken(FunkfeldDarstellung darstellung, int anzahl, FensterOrt ort, bool groesseGezogen)
    {
        ArgumentNullException.ThrowIfNull(ort);
        if (FensterOrt.OderNull(ort) is not { } o) return;
        var eigen = Schluessel(darstellung, anzahl);
        var andere = Schluessel(darstellung, anzahl >= 10 ? 5 : 10);
        var bisher = Fensterorte.GetValueOrDefault(eigen);
        Fensterorte[eigen] = groesseGezogen && o.MitGroesse
            ? o with { Maximiert = false }
            : new FensterOrt(o.Links, o.Oben, bisher?.Breite, bisher?.Hoehe);
        if (Fensterorte.TryGetValue(andere, out var a)) Fensterorte[andere] = a with { Links = o.Links, Oben = o.Oben };
        Orte?.Remove(eigen);
        Orte?.Remove(andere);
        if (Orte is { Count: 0 }) Orte = null;
    }

    /// <summary>
    /// "Größe nach Inhalt": vergisst die gezogenen Größen aller Ansichten, die Lagen bleiben. Danach gilt wieder die
    /// optimale Größe, gerade so, dass alles angezeigt wird.
    /// </summary>
    public bool GroessenVergessen()
    {
        var geaendert = false;
        foreach (var (schluessel, o) in Fensterorte.ToList())
        {
            if (!o.MitGroesse) continue;
            Fensterorte[schluessel] = o with { Breite = null, Hoehe = null };
            geaendert = true;
        }
        return geaendert;
    }

    internal void Normalisieren()
    {
        if (Ansicht is not (5 or 10)) Ansicht = 10;
        if (!Enum.IsDefined(Darstellung)) Darstellung = FunkfeldDarstellung.Raster;
        Durchsichtigkeit = Math.Clamp(Durchsichtigkeit, 0, DurchsichtigkeitMax);
        Skalierung = Math.Clamp(Skalierung, SkalierungMin, SkalierungMax);
        if (Rolle is not (>= 1 and <= ushort.MaxValue - 1)) Rolle = null;
        if (!Enum.IsDefined(MeldezeileStelle)) MeldezeileStelle = MeldezeileStelleAbWerk;
        if (MeldezeileMonitor is not (>= 0 and <= MeldezeileMonitorMax)) MeldezeileMonitor = null;
        Fenster = FensterStand.OderNull(Fenster);
        var gueltig = Enum.GetValues<FunkfeldDarstellung>().SelectMany(d => new[] { Schluessel(d, 5), Schluessel(d, 10) }).ToHashSet(StringComparer.Ordinal);
        foreach (var (schluessel, alt) in Plaetze ?? [])
        {
            if (FensterStand.OderNull(alt) is not { } s) continue;
            Orte ??= new(StringComparer.Ordinal);
            Orte.TryAdd(schluessel, new FunkfeldPlatz(s.Links, s.Oben));
        }
        Plaetze = null;
        foreach (var schluessel in Orte?.Keys.ToList() ?? [])
        {
            var p = FunkfeldPlatz.OderNull(Orte![schluessel]);
            if (!gueltig.Contains(schluessel) || p is null || Fensterorte.ContainsKey(schluessel)) Orte.Remove(schluessel);
            else Orte[schluessel] = p with { Breite = null, Hoehe = null };
        }
        if (Orte is { Count: 0 }) Orte = null;
        foreach (var schluessel in Fensterorte.Keys.ToList())
        {
            var o = FensterOrt.OderNull(Fensterorte[schluessel]);
            if (!gueltig.Contains(schluessel) || o is null) Fensterorte.Remove(schluessel);
            else Fensterorte[schluessel] = o with { Maximiert = false };
        }
    }
}

/// <summary>
/// Durchsichtigkeit des Funkfelds: Deckkraft der Lücken und der Titelzeile (<see cref="Hintergrund"/>) und des ganzen
/// Fensters (<see cref="Fenster"/>). Der Hauptweg (Sarevoc, 30.09.2026: "nicht nur den Hintergrund") ist das ganze Fenster:
/// Alles wird gleichmäßig durchsichtig, bis 85 %, die Deckkraft sinkt nie unter <see cref="MindestDeckkraft"/>. Mit der
/// Option "Schrift und Symbole deckend halten" werden nur Lücken und Titelzeile durchsichtig, bis
/// <see cref="MindestAlpha"/>, und Kacheln und Schrift bleiben lesbar (wie deckend, rechnet <c>Design.FunkfeldDeckung</c>).
/// Das Funkfeld ist dafür immer geschichtet (AllowsTransparency); ein Wechsel der Durchsichtigkeit öffnet kein neues Fenster.
/// </summary>
public readonly record struct Durchsicht(double Hintergrund, double Fenster)
{
    /// <summary>Kleinste Deckkraft der Lücken mit der Option "deckend halten"; bei 80 % und mehr bleibt es dabei.</summary>
    public const double MindestAlpha = 0.2;

    /// <summary>Kleinste Deckkraft des ganzen Fensters (bei 85 %): So bleibt jeder Bildpunkt greifbar, kein Klick fällt durch.</summary>
    public const double MindestDeckkraft = 1 - (FunkfeldEinstellungen.DurchsichtigkeitMax / 100.0);

    public static Durchsicht Deckend { get; } = new(1, 1);

    /// <summary>Aus der Einstellung in Prozent (begrenzt auf 0 bis 85) und der Option "Schrift und Symbole deckend halten".</summary>
    public static Durchsicht Aus(int prozent, bool schriftDeckend)
    {
        var p = Math.Clamp(prozent, 0, FunkfeldEinstellungen.DurchsichtigkeitMax);
        return schriftDeckend
            ? new Durchsicht(Math.Max(MindestAlpha, 1 - (p / 100.0)), 1)
            : new Durchsicht(1, 1 - (p / 100.0));
    }
}

/// <summary>Wie ein Sprechweg in einem Netz ausgelöst wird (A10).</summary>
public enum Ausloesung
{
    Sprachaktivierung,
    Sprechtaste,
}

/// <summary>Eigene Einstellung für ein Netz.</summary>
public sealed class NetzEinstellung
{
    public const float LautstaerkeMinDb = -30f;
    public const float LautstaerkeMaxDb = 12f;

    /// <summary><c>null</c>: Standard des Netzes (<c>ausloeserStandard</c>); bei Global ist das die Taste (A20).</summary>
    public Ausloesung? Ausloesung { get; set; }

    public float LautstaerkeDb { get; set; }

    /// <summary>A9: -1 linkes Ohr, 0 beide (Standard), +1 rechtes Ohr.</summary>
    public float Panorama { get; set; }

    /// <summary>
    /// Bei einem Kanalnetz der Ch., den du zuletzt selbst gewählt hast (seit 0.6.0, 0 ist die Ruhe); <c>null</c>: nie gewählt, ab Werk
    /// gilt die Ruhe (Sarevoc Q6). Die Rollenliste schickt ihn als Wunsch mit (<c>RolleWaehlen.ch</c>). Nachsichtig gelesen: Was kein Ch.
    /// sein kann, fällt weg.
    /// </summary>
    [JsonConverter(typeof(NachsichtigeZahlOderNull))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Ch { get; set; }

    internal void Normalisieren()
    {
        if (Ausloesung is { } a && !Enum.IsDefined(a)) Ausloesung = null;
        LautstaerkeDb = GateWerte.Begrenzen(LautstaerkeDb, LautstaerkeMinDb, LautstaerkeMaxDb, 0f);
        Panorama = GateWerte.Begrenzen(Panorama, -1f, 1f, 0f);
        if (Ch is not (>= 0 and <= Funk.Protokoll.Steuerung.Kanalrechnung.ChMax)) Ch = null;
    }
}

/// <summary>Position und Größe eines Fensters in geräteunabhängigen Einheiten.</summary>
public sealed record FensterStand(double Links, double Oben, double Breite, double Hoehe, bool Maximiert = false)
{
    /// <summary><c>null</c>, wenn nicht endlich oder ohne Fläche.</summary>
    public static FensterStand? OderNull(FensterStand? s) =>
        s is null || !double.IsFinite(s.Links) || !double.IsFinite(s.Oben) || !double.IsFinite(s.Breite) || !double.IsFinite(s.Hoehe)
            || s.Breite < 1 || s.Hoehe < 1 || s.Breite > 100_000 || s.Hoehe > 100_000
            || Math.Abs(s.Links) > 100_000 || Math.Abs(s.Oben) > 100_000
            ? null
            : s;
}
