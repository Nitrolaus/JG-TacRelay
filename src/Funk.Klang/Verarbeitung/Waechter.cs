// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Urteil des Wächters der Auto Dauersend-Dämpfung (Klangstufe C2a, KLANGSTUFE-C-ENTWURF.md, "Was Dauersenden ist"). Auf der eigenen
/// Seite Echo, Spielton, Raumgeräusch oder klemmende Taste; auf der Empfängerseite je Funker Echo oder Dauersenden (ohne Echo, fast ohne Sprache).
/// </summary>
public enum DauersendUrteil { Keins, Echo, Spielton, Raumgeraeusch, KlemmendeTaste, Dauersenden }

/// <summary>Stufe, bis zu der andere Dauersender bei mir leiser werden (Q2, Q13, Q14); ab Werk "deutlich leiser" (Q26).</summary>
public enum Daempfungsstufe { EtwasLeiser, DeutlichLeiser, FastWeg }

/// <summary>
/// Schalter "Auto Dauersend-Dämpfung" (Q1, ab Werk an) und die gewählte Stufe. Aus heißt: Der Wächter urteilt weiter (für das gelbe Zeichen),
/// aber keine Stellgröße ändert sich (keine Dämpfung, keine Korrektur am Echoschutz, keine Anhebung der Linie, keine Angleichung, die klemmende
/// Taste sendet weiter).
/// </summary>
/// <param name="LeisesRedenSperren">
/// Raum-Anhebung (Q38): leises Reden im Raum aussperren, als Erprobung ab Werk aus (Entscheidung der Koordination nach der Gegenprüfung C2a, H2:
/// kein Funkspruch darf verloren gehen; mit ihr verlor eine müde Stimme 3 bis 7 dB unter dem Schnitt Satzanfänge). Wirkt nur mit <paramref name="An"/>.
/// </param>
public readonly record struct DaempfungEinstellung(bool An, Daempfungsstufe Stufe, bool LeisesRedenSperren = false)
{
    public static DaempfungEinstellung AbWerk { get; } = new(true, Daempfungsstufe.DeutlichLeiser);

    /// <summary>Die Stufe in dB (nur für Admin-Anzeigen und den Klangkern).</summary>
    public float StufeDb => Daempfungsworte.Db(Stufe);
}

/// <summary>Für Funker Wörter statt dB (Grundsatz, Q13); die Zahlen gibt es nur im Klangkern und im Admin-Bereich.</summary>
public static class Daempfungsworte
{
    /// <summary>"etwas leiser": 6 dB (Q2, die durchkommende Stimme und die ganze Funk-Wiedergabe höchstens so weit).</summary>
    public const float EtwasLeiserDb = -6f;

    /// <summary>"deutlich leiser": 10 dB (Q26, gegen die Funkstimme gemessen -11,2 dB in den Hörproben).</summary>
    public const float DeutlichLeiserDb = -10f;

    /// <summary>"fast weg": 20 dB.</summary>
    public const float FastWegDb = -20f;

    public static float Db(Daempfungsstufe stufe) => stufe switch
    {
        Daempfungsstufe.EtwasLeiser => EtwasLeiserDb,
        Daempfungsstufe.FastWeg => FastWegDb,
        _ => DeutlichLeiserDb,
    };

    public static string Wort(Daempfungsstufe stufe) => stufe switch
    {
        Daempfungsstufe.EtwasLeiser => "etwas leiser",
        Daempfungsstufe.FastWeg => "fast weg",
        _ => "deutlich leiser",
    };

    /// <summary>Eine wirkende Dämpfung in Wörtern, auch auf dem Weg zwischen zwei Stufen (dorthin, wo sie gerade ist).</summary>
    public static string Wort(float daempfungDb)
    {
        if (float.IsNegativeInfinity(daempfungDb)) return "stumm";
        if (!(daempfungDb < -0.5f)) return "normal";
        if (daempfungDb > -8f) return Wort(Daempfungsstufe.EtwasLeiser);
        if (daempfungDb > -15f) return Wort(Daempfungsstufe.DeutlichLeiser);
        return Wort(Daempfungsstufe.FastWeg);
    }

    /// <summary>Echo, während die Echounterdrückung aus ist (Hörweise Headset, aber offene Kopfhörer oder Lautsprecher).</summary>
    public const string EchoOhneEchounterdrueckung =
        "Deine Kopfhörer oder Lautsprecher kommen ins Mikrofon zurück, die anderen hören sich selbst. Leiser stellen oder unter Einstellungen > Senden die Echounterdrückung auf An stellen.";

    /// <summary>Die Ursache eines Urteils in Wörtern, mit dem, was der Funker tun kann; leer ohne Urteil.</summary>
    public static string Ursache(DauersendUrteil urteil) => urteil switch
    {
        DauersendUrteil.Echo => "Deine Lautsprecher kommen ins Mikrofon zurück, die anderen hören sich selbst. Lautsprecher leiser oder Mikrofon näher.",
        DauersendUrteil.Spielton => "Das Spiel kommt über die Lautsprecher ins Mikrofon. Der Echoschutz hält mehr Abstand, bis es aufhört.",
        DauersendUrteil.Raumgeraeusch => "Im Raum wird leiser gesprochen als am Funk oder es ist laut. Die Sprachaktivierung ist unempfindlicher, bis wieder auf Funklautstärke gesprochen wird. Ist dein Mikrofon leiser geworden: Sprechtaste oder neu einmessen.",
        DauersendUrteil.KlemmendeTaste => "Die Sprechtaste ist seit über einer Minute gedrückt, fast ohne Sprache. Senden ist aus, bis du sie neu drückst.",
        DauersendUrteil.Dauersenden => "Sendet seit langem ohne Pause, fast ohne Sprache.",
        _ => "",
    };
}

/// <summary>Das eigene Urteil des Wächters mit Ursache in Wörtern.</summary>
public readonly record struct WaechterStand(DauersendUrteil Urteil, string Ursache)
{
    public static WaechterStand Keins { get; } = new(DauersendUrteil.Keins, "");

    public static WaechterStand Aus(DauersendUrteil urteil) => urteil == DauersendUrteil.Keins ? Keins : new(urteil, Daempfungsworte.Ursache(urteil));
}

/// <summary>Wahl am gelben Zeichen eines Funkers (Q2).</summary>
public enum FunkerAktion { Stumm, WiederNormal, NichtMehrDaempfen }

/// <summary>
/// Ein Funker aus Sicht der Empfängerseite: Urteil seines Wächters bei mir, wirkende Dämpfung in dB (nur Admin) und in Wörtern, stumm,
/// "nicht mehr dämpfen".
/// </summary>
public readonly record struct FunkerDaempfung(uint KontoId, DauersendUrteil Urteil, float DaempfungDb, string Wort, bool Stumm, bool NichtDaempfen);

/// <summary>
/// Die eingemessenen Werte der Formel (Klangstufe C2a): wie oft eine Echo-Phase mit Funk überlappt (<paramref name="UeberlappungBeiEcho"/>), wie oft
/// sie 50 bis 400 ms nach einem Funk-Anfang aufgeht (<paramref name="AnlaufBeiEcho"/>), wie oft ein Funk-Anfang bei Echo eine Phase auslöst
/// (<paramref name="AnsprechenBeiEcho"/>), und die Punkte des Gleichlaufs je Fach der Korrelation (<paramref name="GleichlaufPunkte"/>, 20 Fächer von
/// -1 bis 1, ln der Häufigkeit bei Echo gegen die bei eigener Stimme mit Funk, zwischen den Fachmitten linear).
/// </summary>
internal sealed record WaechterKalibrierung(float UeberlappungBeiEcho, float AnlaufBeiEcho, float AnsprechenBeiEcho, float[] GleichlaufPunkte)
{
    public const int GleichlaufFaecher = 20;

    /// <summary>Startwerte des Entwurfs (0,95 und 0,9), ohne Gleichlauf.</summary>
    public static WaechterKalibrierung Entwurf { get; } = new(0.95f, 0.9f, 0.9f, new float[GleichlaufFaecher]);

    /// <summary>
    /// Eingemessen im Prüfstand (MessungKlangC2.Messung_KlangC2_Einmessen, 21 Szenen, Saaten ab 1000, nicht die der Abnahme; Zahlen im Modulbericht,
    /// Abschnitt klang-c2a): 997 Echo-Phasen, Überlappung 997/997 (wirkt als 0,99, die Formel begrenzt), Anlauf 394/900 (0,44: Echo öffnet oft mitten
    /// im Reden eines Funkers, nach einer Pause, nicht nach seinem Anfang), Ansprechen 436/849 (0,51). Gleichlauf (Phasen ab 400 ms mit Funk): Echo
    /// 792 von 834 ab 0,9 und 40 zwischen 0,8 und 0,9; eigene Stimme mit Funk 0 von 423 ab 0,9, 2 zwischen 0,8 und 0,9, 7 zwischen 0,7 und 0,8.
    /// </summary>
    public static WaechterKalibrierung AbWerk { get; } = new(0.99f, 0.44f, 0.51f,
        [-3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -3f, -2f, 2f, 3f]);

    /// <summary>Punkte für eine Korrelation <paramref name="r"/>; NaN gibt 0.</summary>
    public float Gleichlauf(float r)
    {
        if (!float.IsFinite(r)) return 0f;
        var x = ((Math.Clamp(r, -1f, 1f) + 1f) * GleichlaufFaecher / 2f) - 0.5f;
        var i = (int)MathF.Floor(x);
        var t = x - i;
        var a = GleichlaufPunkte[Math.Clamp(i, 0, GleichlaufFaecher - 1)];
        var b = GleichlaufPunkte[Math.Clamp(i + 1, 0, GleichlaufFaecher - 1)];
        return Math.Clamp(a + (t * (b - a)), -Waechterformel.PunkteHoechst, Waechterformel.PunkteHoechst);
    }
}

/// <summary>
/// Die Formel des Entwurfs: sequenzieller Wahrscheinlichkeitstest nach Wald. Je Sprechphase Punkte = ln(P(Beobachtung | Echo) / P(Beobachtung |
/// eigene Stimme)) für Überlappung, Anlauf und Gleichlauf; ab ln(1000) gilt Echo als erkannt, nach 30 s unter 0 Entwarnung. Jede Beobachtung
/// bringt höchstens <see cref="PunkteHoechst"/>, damit eine einzelne Phase nie allein entscheidet und ein Modellfehler nicht durchschlägt.
/// </summary>
internal static class Waechterformel
{
    /// <summary>ln(1000): ab hier gilt das Urteil als erkannt.</summary>
    public const float ErkanntPunkte = 6.9077554f;

    /// <summary>
    /// Das Konto fällt nie unter -ln(10): Lange eigene Rede ohne Echo spart keine Punkte an, die ein später beginnendes Echo erst aufbrauchen
    /// müsste (sonst dauerte die Erkennung nach einer Stunde Gespräch ein Vielfaches). Wald für das laufende Überwachen, wie ein CUSUM mit Boden.
    /// </summary>
    public const float UntergrenzePunkte = -2.3025851f;

    /// <summary>Höchstens 2 ln(1000): Nach dem Ende des Echos reichen einige unbeantwortete Funk-Anfänge bis unter 0.</summary>
    public const float ObergrenzePunkte = 2f * ErkanntPunkte;

    /// <summary>Höchstbetrag einer einzelnen Beobachtung.</summary>
    public const float PunkteHoechst = 3f;

    /// <summary>30 s unter 0: Entwarnung.</summary>
    public const int EntwarnungRahmen = 3000;

    /// <summary>Anlauf: die Sendung beginnt 50 bis 400 ms nach einem Funk-Anfang (in Rahmen zu 10 ms).</summary>
    public const int AnlaufVonRahmen = 5;

    public const int AnlaufBisRahmen = 40;

    /// <summary>Breite des Anlauffensters in Sekunden (der Zufall eigener Stimme: Anfänge je Sekunde mal diese Breite).</summary>
    public const float AnlaufFensterS = (AnlaufBisRahmen - AnlaufVonRahmen) / 100f;

    /// <summary>Überlappung: <paramref name="anteil"/> ist a, der Anteil der Zeit, in der Funk spielt (60 s).</summary>
    public static float Ueberlappung(bool ja, float anteil, WaechterKalibrierung k)
    {
        var a = Math.Clamp(float.IsFinite(anteil) ? anteil : 0.5f, 0.02f, 0.98f);
        var q = Math.Clamp(k.UeberlappungBeiEcho, 0.01f, 0.99f);
        return Begrenzt(ja ? MathF.Log(q / a) : MathF.Log((1f - q) / (1f - a)));
    }

    /// <summary>Wahrscheinlichkeit, dass eigene Stimme zufällig im Anlauffenster nach einem Funk-Anfang beginnt.</summary>
    public static float ZufallAnlauf(float anfaengeJeS) =>
        Math.Clamp((float.IsFinite(anfaengeJeS) ? anfaengeJeS : 0.5f) * AnlaufFensterS, 0.01f, 0.95f);

    /// <summary>Anlauf: Ging die Sendung 50 bis 400 ms nach einem Funk-Anfang auf?</summary>
    public static float Anlauf(bool ja, float anfaengeJeS, WaechterKalibrierung k)
    {
        var z = ZufallAnlauf(anfaengeJeS);
        var q = Math.Clamp(k.AnlaufBeiEcho, 0.01f, 0.99f);
        return Begrenzt(ja ? MathF.Log(q / z) : MathF.Log((1f - q) / (1f - z)));
    }

    /// <summary>
    /// Ein Funk-Anfang bei geschlossenem Gate, auf den in 400 ms keine Sendung folgte: bei Echo selten (1 - <see cref="WaechterKalibrierung.AnsprechenBeiEcho"/>),
    /// bei eigener Stimme fast immer (1 - eigene Phasenanfänge je Sekunde mal 0,35 s). Bringt das Konto nach dem Ende eines Echos unter 0.
    /// </summary>
    public static float Unbeantwortet(float eigenePhasenJeS, WaechterKalibrierung k)
    {
        var z = ZufallAnlauf(eigenePhasenJeS);
        var q = Math.Clamp(k.AnsprechenBeiEcho, 0.01f, 0.99f);
        return Begrenzt(MathF.Log((1f - q) / (1f - z)));
    }

    private static float Begrenzt(float p) => Math.Clamp(p, -PunkteHoechst, PunkteHoechst);
}

/// <summary>Das Punktekonto eines Urteils: Summe über die letzten Phasen, zwischen Unter- und Obergrenze; erkannt ab ln(1000), Entwarnung nach 30 s unter 0.</summary>
internal sealed class Punktekonto
{
    private long _unterNullAb = -1;

    public float Stand { get; private set; }

    public bool Erkannt { get; private set; }

    public void Buchen(float punkte, long rahmen)
    {
        if (!float.IsFinite(punkte)) return;
        Stand = Math.Clamp(Stand + punkte, Waechterformel.UntergrenzePunkte, Waechterformel.ObergrenzePunkte);
        if (Stand >= Waechterformel.ErkanntPunkte) Erkannt = true;
        if (Stand >= 0f) _unterNullAb = -1;
        else if (_unterNullAb < 0) _unterNullAb = rahmen;
    }

    /// <summary>Je Rahmen: Entwarnung, wenn das Konto seit 30 s unter 0 steht.</summary>
    public void Takt(long rahmen)
    {
        if (Erkannt && _unterNullAb >= 0 && rahmen - _unterNullAb >= Waechterformel.EntwarnungRahmen) Erkannt = false;
    }

    public void Zuruecksetzen()
    {
        Stand = 0f;
        Erkannt = false;
        _unterNullAb = -1;
    }
}

/// <summary>Die letzten 64 Pegel einer Hüllkurve (10-ms-Rahmen, dBFS, unten bei <see cref="BodenDb"/> abgeschnitten). Allokiert nicht.</summary>
internal sealed class Referenzspur
{
    public const float BodenDb = -80f;
    private const int Laenge = 64;
    private readonly float[] _werte = new float[Laenge];
    private int _schreib;

    public Referenzspur() => Array.Fill(_werte, BodenDb);

    public void Hinzufuegen(float db)
    {
        _schreib = (_schreib + 1) & (Laenge - 1);
        _werte[_schreib] = float.IsFinite(db) ? MathF.Max(db, BodenDb) : BodenDb;
    }

    /// <summary>Der Wert von vor <paramref name="rahmen"/> Rahmen (0: der jüngste).</summary>
    public float Vor(int rahmen) => _werte[(_schreib - rahmen) & (Laenge - 1)];

    /// <summary>Höchster Wert der letzten <paramref name="rahmen"/> Rahmen (wie der Echoschutz die Wiedergabe hält).</summary>
    public float Hoechster(int rahmen)
    {
        var h = BodenDb;
        for (var i = 0; i < rahmen && i < Laenge; i++) h = MathF.Max(h, Vor(i));
        return h;
    }

    public void Zuruecksetzen()
    {
        Array.Fill(_werte, BodenDb);
        _schreib = 0;
    }
}

/// <summary>
/// Die Lage der letzten 60 s: Anteil der Zeit, in der Funk spielt (a), Funk-Anfänge je Sekunde, eigene Phasenanfänge je Sekunde, eigene
/// Aktivität. Ein Ring von 6000 Rahmen; bis er voll ist, gehen 10 s einer mittleren Lage (a = 0,5; 0,5 Anfänge je Sekunde) mit ein, damit die
/// ersten Phasen nach dem Start nicht auf Zufallswerten urteilen. Allokiert nach dem Konstruktor nichts.
/// </summary>
internal sealed class Funklage
{
    public const int Fenster = 6000;
    private const int Vorwissen = 1000;
    private const byte Laeuft = 1;
    private const byte Anfang = 2;
    private const byte EigenerBeginn = 4;
    private const byte EigenAktiv = 8;

    private readonly byte[] _ring = new byte[Fenster];
    private int _pos;
    private int _gefuellt;
    private int _laeuft;
    private int _anfaenge;
    private int _eigeneBeginne;
    private int _eigenAktiv;

    public void Rahmen(bool funkLaeuft, bool anfang, bool eigenerBeginn, bool eigenAktiv)
    {
        var alt = _ring[_pos];
        if (_gefuellt == Fenster)
        {
            if ((alt & Laeuft) != 0) _laeuft--;
            if ((alt & Anfang) != 0) _anfaenge--;
            if ((alt & EigenerBeginn) != 0) _eigeneBeginne--;
            if ((alt & EigenAktiv) != 0) _eigenAktiv--;
        }
        else
        {
            _gefuellt++;
        }
        byte neu = 0;
        if (funkLaeuft)
        {
            neu |= Laeuft;
            _laeuft++;
        }
        if (anfang)
        {
            neu |= Anfang;
            _anfaenge++;
        }
        if (eigenerBeginn)
        {
            neu |= EigenerBeginn;
            _eigeneBeginne++;
        }
        if (eigenAktiv)
        {
            neu |= EigenAktiv;
            _eigenAktiv++;
        }
        _ring[_pos] = neu;
        _pos = (_pos + 1) % Fenster;
    }

    /// <summary>a: Anteil der Zeit mit Funk (60 s, mit Vorwissen).</summary>
    public float Anteil => (_laeuft + (0.5f * Vorwissen)) / (_gefuellt + Vorwissen);

    public float AnfaengeJeS => (_anfaenge + (0.005f * Vorwissen)) * 100f / (_gefuellt + Vorwissen);

    public float EigenePhasenJeS => (_eigeneBeginne + (0.001f * Vorwissen)) * 100f / (_gefuellt + Vorwissen);

    /// <summary>Anteil der letzten 60 s, in denen die eigene Seite aktiv war (ohne Vorwissen); unter 60 s gemessen über das Vorhandene.</summary>
    public float EigenAktivAnteil => _gefuellt == 0 ? 0f : (float)_eigenAktiv / _gefuellt;

    public int Gefuellt => _gefuellt;

    public void Zuruecksetzen()
    {
        Array.Clear(_ring);
        _pos = _gefuellt = _laeuft = _anfaenge = _eigeneBeginne = _eigenAktiv = 0;
    }
}

/// <summary>Was eine Sprechphase (oder ein Abschnitt einer langen) ergab.</summary>
internal struct Beobachtung
{
    public int Rahmen;

    /// <summary>Lief in mindestens der Hälfte der Rahmen Funk (mit 400 ms Haltezeit)?</summary>
    public bool Ueberlappung;

    /// <summary>Ging die Phase 50 bis 400 ms nach einem Funk-Anfang auf (nur im ersten Abschnitt bekannt)?</summary>
    public bool Anlauf;

    /// <summary>Größte Korrelation der Hüllkurven über die Verschiebungen 50 bis 400 ms; NaN unter 200 ms.</summary>
    public float Gleichlauf;

    /// <summary>Verschiebung des besten Gleichlaufs in Rahmen.</summary>
    public int BesteVerschiebung;

    /// <summary>Platz (im Mischer) des Funkers, der in der Phase am häufigsten am lautesten spielte, oder -1.</summary>
    public int FunkPlatz;

    /// <summary>Anteil der Rahmen mit Sprachwahrscheinlichkeit ab 0,9 (ohne den Nachlauf).</summary>
    public float SprachAnteil;

    /// <summary>Zahl der Sprachrahmen (ab 0,9).</summary>
    public int SprachRahmen;

    /// <summary>Mittlerer Pegel der Sprachrahmen (Leistung, dBFS, vor der Pegelstufe); NaN ohne Sprache.</summary>
    public float PegelDb;

    /// <summary>80. Perzentil der Pegel der Sprachrahmen, sonst aller Rahmen ohne Nachlauf (dBFS, vor der Pegelstufe).</summary>
    public float PerzentilDb;

    /// <summary>Median von Mikrofon (vor dem APM) minus höchster Wiedergabe der letzten 400 ms: die Kopplung, wie der Echoschutz sie lernt.</summary>
    public float KopplungDb;

    /// <summary>Anteil der Rahmen, in denen nicht sprachartiger Spielton (Musik) aus den Lautsprechern kam.</summary>
    public float SpielNichtSpracheAnteil;

    /// <summary>Mittel von Mikrofon (vor dem APM) minus Funk-Wiedergabe in der besten Verschiebung (Rahmen mit Funk über -50 dBFS): die Kopplung im Takt des Echos.</summary>
    public float KopplungVerzugDb;
}

/// <summary>
/// Sammelt eine Sprechphase Rahmen für Rahmen, ohne Allokation: die Summen für die Korrelation der Hüllkurven (eigener Pegel gegen die
/// Referenzspur) über alle Verschiebungen von 50 bis 400 ms zugleich, die Überlappung, Sprachanteil, Pegel, Kopplung und den Platz des
/// durchkommenden Funkers. Jede Verschiebung kostet je Rahmen drei Additionen.
/// </summary>
internal sealed class Phasenbeobachter
{
    /// <summary>
    /// Gleichlauf erst ab 400 ms ohne Nachlauf: Über weniger Rahmen findet das Maximum über 36 Verschiebungen auch bei unabhängiger Sprache oft hohe
    /// Werte (Einmessen, 1. Lauf mit 200 ms).
    /// </summary>
    public const int MindestRahmenGleichlauf = 40;

    /// <summary>Boden der Hüllkurven für den Gleichlauf in dBFS (Stille zählt nicht tiefer).</summary>
    public const float GleichlaufBodenDb = -60f;
    private const int VonL = Waechterformel.AnlaufVonRahmen;
    private const int BisL = Waechterformel.AnlaufBisRahmen;
    private const int Verschiebungen = BisL - VonL + 1;
    private const int PegelFaecher = 100; // -100 bis 0 dBFS in 1-dB-Fächern
    private const int KopplungVon = -60;
    private const int KopplungFaecher = 80; // -60 bis +20 dB

    private readonly double[] _sy = new double[Verschiebungen];
    private readonly double[] _syy = new double[Verschiebungen];
    private readonly double[] _sxy = new double[Verschiebungen];
    private readonly double[] _sd = new double[Verschiebungen];
    private readonly int[] _nd = new int[Verschiebungen];
    private readonly int[] _sprachPegel = new int[PegelFaecher];
    private readonly int[] _allePegel = new int[PegelFaecher];
    private readonly int[] _kopplung = new int[KopplungFaecher];
    private readonly int[] _plaetze = new int[64];
    private double _sx;
    private double _sxx;
    private double _sprachLeistung;
    private int _n;
    private int _kern;
    private int _funk;
    private int _sprache;
    private int _kopplungen;
    private int _spiel;
    private bool _anlauf;

    public int Anzahl => _n;

    /// <summary>Mittlerer Pegel der bisherigen Sprachrahmen (dBFS), NaN ohne.</summary>
    public float LaufendPegelDb => _sprache == 0 ? float.NaN : 10f * MathF.Log10((float)(_sprachLeistung / _sprache));

    public int LaufendSprachRahmen => _sprache;

    public void Beginnen(long rahmen, bool anlauf)
    {
        Array.Clear(_sy);
        Array.Clear(_syy);
        Array.Clear(_sxy);
        Array.Clear(_sd);
        Array.Clear(_nd);
        Array.Clear(_sprachPegel);
        Array.Clear(_allePegel);
        Array.Clear(_kopplung);
        Array.Clear(_plaetze);
        _sx = _sxx = _sprachLeistung = 0;
        _n = _kern = _funk = _sprache = _kopplungen = _spiel = 0;
        _anlauf = anlauf;
    }

    /// <param name="eigenDb">Eigener Pegel vor der Pegelstufe (nach dem APM).</param>
    /// <param name="eingangDb">Pegel vor dem APM (für die Kopplung wie im Echoschutz).</param>
    /// <param name="wiedergabeDb">Höchster Wiedergabepegel der Echoreferenz (Echoschutz) oder NaN: dann die höchste Referenz der letzten 400 ms.</param>
    /// <param name="p">Sprachwahrscheinlichkeit.</param>
    /// <param name="funkLaeuft">Funk spielt (mit Haltezeit).</param>
    /// <param name="spur">Referenzspur, der jüngste Wert gehört zu diesem Rahmen.</param>
    /// <param name="funkPlatz">Platz des lautesten Funkers von vor etwa 100 ms, oder -1.</param>
    /// <param name="spielNichtSprache">Aus den Lautsprechern kam nicht sprachartiger Spielton.</param>
    /// <param name="nachlauf">Der Rahmen gehört zum Nachlauf des Gates (zählt nicht zum Sprachanteil).</param>
    public void Rahmen(float eigenDb, float eingangDb, float wiedergabeDb, float p, bool funkLaeuft, Referenzspur spur, int funkPlatz, bool spielNichtSprache,
        bool nachlauf = false)
    {
        var x = float.IsFinite(eigenDb) ? MathF.Max(eigenDb, Referenzspur.BodenDb) : Referenzspur.BodenDb;
        _n++;
        if (!nachlauf)
        {
            // Gleichlauf nur ohne den Nachlauf und mit einem Boden bei -60 dBFS: Sonst bestimmt der eine große Sprung vom Satzende in die Stille
            // die Korrelation, und eine eigene Phase, die endet, während auch der Funk endet, sähe aus wie Echo (Einmessen, 2. Lauf).
            var xg = MathF.Max(x, GleichlaufBodenDb);
            _sx += xg;
            _sxx += (double)xg * xg;
            for (var i = 0; i < Verschiebungen; i++)
            {
                var roh = spur.Vor(VonL + i);
                double y = MathF.Max(roh, GleichlaufBodenDb);
                if (roh > -50f && float.IsFinite(eingangDb))
                {
                    _sd[i] += eingangDb - roh;
                    _nd[i]++;
                }
                _sy[i] += y;
                _syy[i] += y * y;
                _sxy[i] += xg * y;
            }
        }
        if (funkLaeuft) _funk++;
        if (spielNichtSprache) _spiel++;
        if (funkPlatz is >= 0 and < 64 && funkLaeuft) _plaetze[funkPlatz]++;
        var fach = Math.Clamp((int)MathF.Floor(x + PegelFaecher), 0, PegelFaecher - 1);
        if (!nachlauf)
        {
            _kern++;
            _allePegel[fach]++;
            if (p >= Pegelstufe.SprachSchwelle)
            {
                _sprache++;
                _sprachPegel[fach]++;
                _sprachLeistung += Math.Pow(10.0, x / 10.0);
            }
        }
        var bezug = float.IsFinite(wiedergabeDb) ? wiedergabeDb : spur.Hoechster(40);
        if (bezug > -60f && float.IsFinite(eingangDb))
        {
            var k = Math.Clamp((int)MathF.Floor(eingangDb - bezug) - KopplungVon, 0, KopplungFaecher - 1);
            _kopplung[k]++;
            _kopplungen++;
        }
    }

    public Beobachtung Ergebnis()
    {
        var b = new Beobachtung
        {
            Rahmen = _n,
            Ueberlappung = _n > 0 && 2 * _funk >= _n,
            Anlauf = _anlauf,
            Gleichlauf = float.NaN,
            KopplungVerzugDb = float.NaN,
            BesteVerschiebung = 0,
            FunkPlatz = -1,
            SprachRahmen = _sprache,
            SprachAnteil = _kern == 0 ? 0f : (float)_sprache / _kern,
            PegelDb = LaufendPegelDb,
            PerzentilDb = _sprache >= 5 ? Perzentil(_sprachPegel, _sprache, 0.8) : _kern > 0 ? Perzentil(_allePegel, _kern, 0.8) : float.NaN,
            KopplungDb = _kopplungen >= 10 ? Perzentil(_kopplung, _kopplungen, 0.5) + PegelFaecher + KopplungVon : float.NaN,
            SpielNichtSpracheAnteil = _n == 0 ? 0f : (float)_spiel / _n,
        };
        if (_kern >= MindestRahmenGleichlauf)
        {
            var bester = float.NegativeInfinity;
            double n = _kern;
            var vx = (n * _sxx) - (_sx * _sx);
            for (var i = 0; i < Verschiebungen; i++)
            {
                var vy = (n * _syy[i]) - (_sy[i] * _sy[i]);
                var nenner = vx * vy;
                var r = nenner > 1e-9 ? (float)(((n * _sxy[i]) - (_sx * _sy[i])) / Math.Sqrt(nenner)) : 0f;
                if (r > bester)
                {
                    bester = r;
                    b.BesteVerschiebung = VonL + i;
                }
            }
            b.Gleichlauf = bester;
            var j = b.BesteVerschiebung - VonL;
            b.KopplungVerzugDb = _nd[j] >= 10 ? (float)(_sd[j] / _nd[j]) : float.NaN;
        }
        var meiste = 0;
        for (var i = 0; i < _plaetze.Length; i++)
        {
            if (_plaetze[i] > meiste)
            {
                meiste = _plaetze[i];
                b.FunkPlatz = i;
            }
        }
        return b;
    }

    /// <summary>Quantil eines Fächerzählers mit 1-dB-Fächern, in dB ab der Fachmitte, der Versatz (Fach 0 = -100 dB) kommt beim Aufrufer dazu.</summary>
    private static float Perzentil(int[] faecher, int anzahl, double q)
    {
        var ziel = q * anzahl;
        var summe = 0;
        for (var i = 0; i < faecher.Length; i++)
        {
            summe += faecher[i];
            if (summe >= ziel) return i + 0.5f - PegelFaecher;
        }
        return faecher.Length - 0.5f - PegelFaecher;
    }
}
