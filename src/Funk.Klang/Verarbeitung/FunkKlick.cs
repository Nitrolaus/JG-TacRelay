// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

public enum KlickArt
{
    /// <summary>Taste gedrückt, Beginn einer Sprechphase.</summary>
    Druck,

    /// <summary>Taste losgelassen, Ende einer Sprechphase.</summary>
    Loslassen,
}

/// <summary>
/// Einstellung des Funk-Klicks (A11, A63). Die Lautstärken sind Pegel über 200 ms in dB, bezogen auf die Sprache hinter der Kette
/// (<see cref="SprachpegelDbfs"/>): -12 dB heißt 12 dB leiser als eine Stimme. Über 200 ms summiert das Gehör kurze Laute ungefähr
/// (Faustregel, nicht am Ohr gemessen); so klingen das kurze Rauschen beim Drücken und das längere beim Loslassen bei derselben Zahl
/// etwa gleich laut.
/// <para>
/// Voreinstellung (A63: ab Werk hörbar, aber dezent): Bis 0.4.1 lag der Klick über 200 ms gut 22 dB unter der Sprache und ging
/// neben Spielton unter. Seit 0.5.0 eigene Taste -12 dB, andere Sprecher -15 dB, damit der eigene Klick als Rückmeldung vorn steht
/// und fremde Sprechphasen nur angedeutet werden, bei Sprachaktivierung weitere 18 dB leiser wie seit A11 ("sehr dezent, sonst
/// klappert es"). Das Rauschen seit 0.6.0 Stufe 2b ist auf denselben Bezug geeicht (KlickTests).
/// </para>
/// </summary>
public sealed class FunkKlickEinstellungen
{
    /// <summary>Bezug der Lautstärke: Sprache hinter der Kette liegt bei -35 bis -15 dBFS (GateVorgaben), die Mitte.</summary>
    public const float SprachpegelDbfs = -25f;

    /// <summary>Leisester einstellbarer Klick, 30 dB unter der Sprache.</summary>
    public const float MinDb = -30f;

    /// <summary>Lautester einstellbarer Klick; auch dort bleibt die Spitze unter dem Knie des Begrenzers (KlickTests).</summary>
    public const float MaxDb = -6f;

    public const float EigeneVoreinstellungDb = -12f;

    public const float AndereVoreinstellungDb = -15f;

    /// <summary>Klick beim Drücken und Loslassen der eigenen Sprech- oder Melde-Taste.</summary>
    public bool EigeneAn { get; init; } = true;

    /// <summary>Lautstärke des eigenen Klicks, <see cref="MinDb"/> bis <see cref="MaxDb"/>.</summary>
    public float EigeneDb { get; init; } = EigeneVoreinstellungDb;

    /// <summary>Klick, wenn andere zu sprechen beginnen und aufhören (und bei der Hörprobe wie beim Empfänger).</summary>
    public bool AndereAn { get; init; } = true;

    /// <summary>Lautstärke des Klicks anderer Sprecher, <see cref="MinDb"/> bis <see cref="MaxDb"/>.</summary>
    public float AndereDb { get; init; } = AndereVoreinstellungDb;

    /// <summary>Absenkung gegenüber <see cref="AndereDb"/>, wenn die Phase aus der Sprachaktivierung kommt ("dezent").</summary>
    public float SprachaktivierungDb { get; init; } = -18f;

    /// <summary>Bei Sprachaktivierung gar kein Klick.</summary>
    public bool SprachaktivierungAus { get; init; }
}

/// <summary>
/// Funk-Klick, zur Laufzeit erzeugt und lokal gemischt (nie durch den Codec), keine Tondatei. Seit 0.6.0 Stufe 2b ein kurzes rosa
/// Rauschen statt Klick, Doppelklick und Piep, nach Sarevocs Wahl Variante A der Hörproben vom 02.10.2026: rosa Rauschen nach Paul
/// Kellet, Band <see cref="BandUntenHz"/> bis <see cref="BandObenHz"/> (je zwei Biquads Hochpass und Tiefpass, Q 0,707), Anstieg
/// <see cref="AnstiegMs"/> mit Sinusflanke, danach exp(-4 t / T) bis zum Ende; <see cref="DruckMs"/> beim Drücken,
/// <see cref="LoslassenMs"/> beim Loslassen. Jeder Abspielvorgang zieht ein neues Rauschen, sonst klingt es mechanisch; mit
/// Startwert ist die Folge fest (Tests). Jedes Rauschen wird auf <see cref="FunkKlickEinstellungen.SprachpegelDbfs"/> über 200 ms
/// gebracht, so stimmen die Lautstärken wie beim Klick bis 0.5.x.
/// <para>
/// Das Rauschen entsteht im Audio-Thread beim Start eines Klicks, in Puffern aus dem Konstruktor; <see cref="Mischen"/> allokiert
/// nichts. Eine Instanz gehört genau einem Mischer (einem Audio-Thread); <see cref="Anfordern"/> und <see cref="AnhoerenAnfordern"/>
/// sind aus jedem Thread erlaubt. Eine andere Einstellung ist eine neue Instanz (<see cref="Mischer.Klick"/>).
/// </para>
/// </summary>
public sealed class FunkKlick
{
    /// <summary>Dauer beim Drücken.</summary>
    public const float DruckMs = 30f;

    /// <summary>Dauer beim Loslassen.</summary>
    public const float LoslassenMs = 90f;

    /// <summary>Anstieg mit Sinusflanke (Viertelperiode) bis zur vollen Hülle.</summary>
    public const float AnstiegMs = 3f;

    /// <summary>Untere Kante des Bands (zwei Hochpässe 2. Ordnung).</summary>
    public const float BandUntenHz = 900f;

    /// <summary>Obere Kante des Bands (zwei Tiefpässe 2. Ordnung).</summary>
    public const float BandObenHz = 3500f;

    private const int Stimmen = 4;
    private const int Planplaetze = 4;

    /// <summary>"Anhören": eigene Taste loslassen nach 300 ms, andere Sprecher ab 800 ms (drücken) und 1100 ms (loslassen).</summary>
    private const int AnhoerenLoslassenMs = 300;
    private const int AnhoerenAndereMs = 800;

    /// <summary>Samples, über die <see cref="PegelUeber200Ms"/> rechnet.</summary>
    private const int Integration = 200 * Klangformat.SamplesJeMs;

    /// <summary>Güte der Bandfilter.</summary>
    private const double BandQ = 0.707;

    private static readonly Biquad Hochpass = Biquad.Hochpass(BandUntenHz, BandQ);
    private static readonly Biquad Tiefpass = Biquad.Tiefpass(BandObenHz, BandQ);

    private readonly Random _zufall;
    private readonly float[][] _stimmeKlang = new float[Stimmen][];
    private readonly int[] _stimmeLaenge = new int[Stimmen];
    private readonly int[] _stimmePos = new int[Stimmen];
    private readonly float[] _stimmeLinks = new float[Stimmen];
    private readonly float[] _stimmeRechts = new float[Stimmen];
    private readonly int[] _planRest = new int[Planplaetze];
    private readonly KlickArt[] _planArt = new KlickArt[Planplaetze];
    private readonly float[] _planFaktor = new float[Planplaetze];
    private readonly float _eigene;
    private readonly float _andere;
    private readonly float _anhoerenEigene;
    private readonly float _anhoerenAndere;
    private readonly float _vaFaktor;
    private int _anfrageDruck;
    private int _anfrageLoslassen;
    private int _anfrageAnhoeren;
    private long _gestartet;
    private long _eigeneDruecke;

    /// <param name="saat">Startwert des Rauschens (Tests); ohne ihn ein zufälliger.</param>
    public FunkKlick(FunkKlickEinstellungen? einstellungen = null, int? saat = null)
    {
        var e = einstellungen ?? new FunkKlickEinstellungen();
        // Negiert formuliert, damit NaN überall durchfällt.
        if (!(e.EigeneDb is >= FunkKlickEinstellungen.MinDb and <= FunkKlickEinstellungen.MaxDb)
            || !(e.AndereDb is >= FunkKlickEinstellungen.MinDb and <= FunkKlickEinstellungen.MaxDb))
            throw new ArgumentException("Lautstärken zwischen -30 und -6 dB.", nameof(einstellungen));
        if (!(e.SprachaktivierungDb is >= -60f and <= 0f))
            throw new ArgumentException("Absenkung bei Sprachaktivierung zwischen -60 und 0 dB.", nameof(einstellungen));
        Einstellungen = e;
        _zufall = saat is { } s ? new Random(s) : new Random();
        for (var i = 0; i < Stimmen; i++) _stimmeKlang[i] = new float[Laenge(KlickArt.Loslassen)];
        _anhoerenEigene = Pegel.DbZuLinear(e.EigeneDb);
        _anhoerenAndere = Pegel.DbZuLinear(e.AndereDb);
        _eigene = e.EigeneAn ? _anhoerenEigene : 0f;
        _andere = e.AndereAn ? _anhoerenAndere : 0f;
        _vaFaktor = e.SprachaktivierungAus ? 0f : Pegel.DbZuLinear(e.SprachaktivierungDb);
        Array.Fill(_planRest, -1);
    }

    public FunkKlickEinstellungen Einstellungen { get; }

    /// <summary>Wie viele Klicks hörbar gestartet wurden (eigene, andere, Anhören); für Messungen und Diagnose.</summary>
    public long Gestartet => Interlocked.Read(ref _gestartet);

    /// <summary>
    /// Wie oft der eigene Druck-Klick (Sprechtaste gedrückt) hörbar gestartet wurde. Der Mischer meldet ihn an die Sendekette, die
    /// ihn aus der Echoreferenz nimmt (Klangstufe A).
    /// </summary>
    public long EigeneDrueckeGestartet => Interlocked.Read(ref _eigeneDruecke);

    /// <summary>Länge eines Klicks in Samples (48 kHz).</summary>
    public static int Laenge(KlickArt art) => (int)MathF.Round((art == KlickArt.Druck ? DruckMs : LoslassenMs) * Klangformat.SamplesJeMs);

    /// <summary>
    /// Pegel über 200 ms in dBFS (mittlerer quadratischer Wert wie <see cref="Pegel.Dbfs"/>): Energie von <paramref name="x"/>,
    /// verteilt auf 200 ms. Für kürzere Laute das Maß der Lautstärke hier; was länger ist, zählt ganz.
    /// </summary>
    public static float PegelUeber200Ms(ReadOnlySpan<float> x)
    {
        double summe = 0;
        foreach (var v in x) summe += (double)v * v;
        return Pegel.LeistungZuDb((float)(summe / Math.Max(Integration, x.Length)));
    }

    /// <summary>
    /// Schreibt ein neues Rauschen nach <paramref name="ziel"/> und gibt seine Länge zurück: bei 0 dB, also über 200 ms so laut wie
    /// die Sprache. Mit demselben Zufall in demselben Zustand Sample für Sample die Vorlage der Hörprobe (KlickTests). Allokiert
    /// nichts.
    /// </summary>
    internal static int Erzeugen(KlickArt art, Random zufall, Span<float> ziel)
    {
        var n = Laenge(art);
        if (ziel.Length < n) throw new ArgumentException("Ziel zu kurz für den Klick.", nameof(ziel));
        var anstieg = (int)MathF.Round(AnstiegMs * Klangformat.SamplesJeMs);
        var abfall = Math.Max(1, n - anstieg);
        var (h1, h2, t1, t2) = (Hochpass, Hochpass, Tiefpass, Tiefpass);
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        double summe = 0;
        for (var i = 0; i < n; i++)
        {
            // Rosa Rauschen nach Paul Kellet ("refined method"), aus weißem Rauschen -1 bis 1.
            var w = (zufall.NextDouble() * 2) - 1;
            b0 = (0.99886 * b0) + (w * 0.0555179);
            b1 = (0.99332 * b1) + (w * 0.0750759);
            b2 = (0.96900 * b2) + (w * 0.1538520);
            b3 = (0.86650 * b3) + (w * 0.3104856);
            b4 = (0.55000 * b4) + (w * 0.5329522);
            b5 = (-0.7616 * b5) - (w * 0.0168980);
            var rosa = b0 + b1 + b2 + b3 + b4 + b5 + b6 + (w * 0.5362);
            b6 = w * 0.115926;
            var v = t2.Schritt(t1.Schritt(h2.Schritt(h1.Schritt(rosa))));
            v *= i < anstieg ? Math.Sin(Math.PI / 2 * i / anstieg) : Math.Exp(-4.0 * (i - anstieg) / abfall);
            ziel[i] = (float)v;
            summe += v * v;
        }
        if (summe > 0)
        {
            var bezug = Pegel.DbZuLinear(FunkKlickEinstellungen.SprachpegelDbfs);
            var faktor = (float)Math.Sqrt(bezug * bezug * Math.Max(Integration, n) / summe);
            for (var i = 0; i < n; i++) ziel[i] *= faktor;
        }
        return n;
    }

    /// <summary>Nur im Audio-Thread: Klick eines anderen Sprechers starten, mit Panorama-Faktoren je Kanal.</summary>
    public void Ausloesen(KlickArt art, bool sprachaktivierung, float links = 0.70710677f, float rechts = 0.70710677f) =>
        _ = Starten(art, sprachaktivierung ? _andere * _vaFaktor : _andere, links, rechts);

    /// <summary>Aus jedem Thread: Klick für die eigene Sprechtaste anfordern (mittig, wird beim nächsten Mischen gestartet).</summary>
    public void Anfordern(KlickArt art)
    {
        if (art == KlickArt.Druck) Interlocked.Increment(ref _anfrageDruck);
        else Interlocked.Increment(ref _anfrageLoslassen);
    }

    /// <summary>
    /// Aus jedem Thread: "Anhören" (A63). Spielt beim nächsten Mischen den eigenen Klick (drücken, nach 300 ms loslassen) und danach
    /// den eines anderen Sprechers (ab 800 ms), jeweils nur, wenn er an ist, in seiner Lautstärke und mittig; ist nur einer an,
    /// beginnt er sofort. Falsch, wenn beide aus sind und nichts zu hören wäre.
    /// </summary>
    public bool AnhoerenAnfordern()
    {
        if (_eigene <= 0f && _andere <= 0f) return false;
        Interlocked.Exchange(ref _anfrageAnhoeren, 1);
        return true;
    }

    /// <summary>Nur im Audio-Thread: laufende Klicks auf die Kanäle addieren. <paramref name="rechts"/> leer für Mono.</summary>
    public void Mischen(Span<float> links, Span<float> rechts)
    {
        if (Interlocked.Exchange(ref _anfrageDruck, 0) > 0 && Starten(KlickArt.Druck, _eigene)) Interlocked.Increment(ref _eigeneDruecke);
        if (Interlocked.Exchange(ref _anfrageLoslassen, 0) > 0) _ = Starten(KlickArt.Loslassen, _eigene);
        if (Interlocked.Exchange(ref _anfrageAnhoeren, 0) > 0) AnhoerenPlanen();
        for (var p = 0; p < Planplaetze; p++)
        {
            if (_planRest[p] < 0) continue;
            if (_planRest[p] < links.Length)
            {
                _ = Starten(_planArt[p], _planFaktor[p]);
                _planRest[p] = -1;
            }
            else
            {
                _planRest[p] -= links.Length;
            }
        }

        var mono = rechts.IsEmpty;
        for (var s = 0; s < Stimmen; s++)
        {
            var laenge = _stimmeLaenge[s];
            if (laenge == 0) continue;
            var klang = _stimmeKlang[s];
            var pos = _stimmePos[s];
            var n = Math.Min(links.Length, laenge - pos);
            var gl = mono ? (_stimmeLinks[s] + _stimmeRechts[s]) * 0.70710677f : _stimmeLinks[s];
            var gr = _stimmeRechts[s];
            for (var i = 0; i < n; i++)
            {
                var v = klang[pos + i];
                links[i] += v * gl;
                if (!mono) rechts[i] += v * gr;
            }
            pos += n;
            if (pos >= laenge) _stimmeLaenge[s] = 0;
            else _stimmePos[s] = pos;
        }
    }

    /// <summary>
    /// Nur im Audio-Thread: eine Stimme mit <paramref name="faktor"/> (linear) und neuem Rauschen starten; 0 heißt aus. Wahr, wenn
    /// sie gestartet wurde.
    /// </summary>
    private bool Starten(KlickArt art, float faktor, float links = 0.70710677f, float rechts = 0.70710677f)
    {
        if (faktor <= 0f) return false;
        Interlocked.Increment(ref _gestartet);
        var frei = 0;
        for (var s = 0; s < Stimmen; s++)
        {
            if (_stimmeLaenge[s] == 0)
            {
                frei = s;
                break;
            }
            if (_stimmePos[s] > _stimmePos[frei]) frei = s; // sonst die am weitesten abgespielte ersetzen
        }
        _stimmeLaenge[frei] = Erzeugen(art, _zufall, _stimmeKlang[frei]);
        _stimmePos[frei] = 0;
        _stimmeLinks[frei] = links * faktor;
        _stimmeRechts[frei] = rechts * faktor;
        return true;
    }

    /// <summary>Nur im Audio-Thread: die Folge für "Anhören" in den Plan legen (ersetzt eine laufende).</summary>
    private void AnhoerenPlanen()
    {
        var i = 0;
        var ab = 0;
        if (_eigene > 0f)
        {
            Planen(i++, 0, KlickArt.Druck, _anhoerenEigene);
            Planen(i++, AnhoerenLoslassenMs, KlickArt.Loslassen, _anhoerenEigene);
            ab = AnhoerenAndereMs;
        }
        if (_andere > 0f)
        {
            Planen(i++, ab, KlickArt.Druck, _anhoerenAndere);
            Planen(i++, ab + AnhoerenLoslassenMs, KlickArt.Loslassen, _anhoerenAndere);
        }
        for (; i < Planplaetze; i++) _planRest[i] = -1;
    }

    private void Planen(int platz, int ms, KlickArt art, float faktor)
    {
        _planRest[platz] = ms * Klangformat.SamplesJeMs;
        _planArt[platz] = art;
        _planFaktor[platz] = faktor;
    }
}
