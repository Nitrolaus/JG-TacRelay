// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Maschine;

/// <summary>
/// Echoschutz der Sprachaktivierung ohne Headset (A14): AEC3 dämpft das Echo der Lautsprecher stark, aber ein Rest
/// bleibt, und der ist Sprache (die Stimme der anderen). Das Gate darf auf ihn nicht öffnen. Nach dem Prinzip des
/// Geigel-Doppelsprech-Detektors, auf Pegeln je 10-ms-Rahmen: Solange die Wiedergabe spielt, gilt ein Mikrofonrahmen
/// als Echoverdacht, wenn sein Pegel vor dem APM höchstens <see cref="SicherheitDb"/> über dem erwarteten Echo liegt.
/// Das erwartete Echo ist der höchste Wiedergabepegel der letzten 400 ms (Laufzeit, Nachhall, Kette) plus die gelernte
/// Kopplung Lautsprecher zu Mikrofon. Die Kopplung ist der Median von Mikrofon minus Wiedergabe über Rahmen, in denen
/// die Wiedergabe spielt und das Gate zu ist (meist hört man dann zu); sie startet vorsichtig bei 0 dB und lernt mit
/// 5 dB je Sekunde. Wer spricht, während die anderen reden, muss also lauter sein als deren Echo: halbduplex nur beim
/// Öffnen, eine offene Phase bleibt offen. Allokiert nicht; nur aus dem Aufnahme-Thread.
/// <para>
/// Abstand nach Art der Wiedergabe (Modul aec3, 29.09.2026): Die vollen <see cref="SicherheitDb"/> gelten nur, wenn in
/// den letzten 400 ms ein Wiedergaberahmen sprachartig war (RNNoise auf der Echoreferenz, <see cref="SprachSchwelle"/>,
/// oder der eigene Mischer spielte hörbar, <see cref="Sendekette.FunkWiedergabeMelden"/>). Gemessen: Läuft das Spiel über
/// die Lautsprecher, sperrten 10 dB jeden eigenen Anlaut, der nicht so weit über dem Spielecho lag (Lautsprecher 12 dB,
/// Spielton -24 dBFS: 11 von 22 Anlauten verpasst gegen 4 ohne Schutz). Bei Geräusch (Spielton, Musik) gilt deshalb nur
/// <see cref="GeraeuschSicherheitDb"/>. Ganz ohne Schutz geht es dort nicht: Hinter AEC3 und RNNoise öffnet auch das Echo
/// von Geräusch das Gate, vor allem das von Musik (Gegenprüfung aec3, je 105 Szenen zu 40 s: Spielton 12, Musik 124
/// Auslösungen; mit 6 dB keine). Die Stimmen der anderen zählen immer als Sprache, auch wenn RNNoise sie im Loopback
/// neben Musik nicht mehr erkennt; Sprachausgaben des Spiels über dieselbe Sprachwahrscheinlichkeit wie im Gate, die
/// Haltezeit überbrückt die Silben.
/// </para>
/// <para>
/// Gelernt wird nur, solange die Wiedergabe gerade spielt, nicht in den 400 ms danach (seit 0.6.0 Stufe 2b). In der Haltezeit
/// steht der höchste Wiedergabepegel noch da, das Mikrofon hört aber nur noch Stille; jeder Rahmen zog die Kopplung dort nach
/// unten, nach jeder Sprechphase und jedem Funk-Klick um bis zu 2 dB. Beim verzerrenden Lautsprecher (leise Stellen kommen mit
/// +10 dB an) lag sie so bei -1 bis -5 dB, und das Echo des nächsten Anlauts öffnete das Gate, auch ganz ohne Klick; mit dem
/// Rausch-Klick (90 ms statt 20) in 7 von 18 Läufen (KlickMaschineTests, je Startwert des Rauschens).
/// </para>
/// </summary>
internal sealed class Echoschutz
{
    /// <summary>
    /// 400 ms Haltezeit des Wiedergabepegels: Laufzeit zum Mikrofon und Nachhall (bis etwa 150 ms), dazu die Verzögerung
    /// der Kette hinter dem Vergleich (APM 9 ms, RNNoise 20 ms) und das Ausklingen des Echorests in AEC3.
    /// </summary>
    public const int HalteRahmen = 40;

    /// <summary>Unter diesem Wiedergabepegel spielt nichts, was ein Echo geben könnte.</summary>
    public const float AktivAbDb = -60f;

    /// <summary>So weit über dem erwarteten Echo muss das Mikrofon liegen, damit ein Rahmen öffnen darf.</summary>
    public const float SicherheitDb = 10f;

    /// <summary>
    /// Abstand bei nicht sprachartiger Wiedergabe (Spielton, Musik), der kleinste ohne Auslösung durch das Echo. Gemessen
    /// in der Gegenprüfung aec3 (MessungSpielton.Messung_SpieltonEcho; Nutzer schweigt, Spielton bzw. Musik -42 bis
    /// -18 dBFS, Kopplung 3 bis 30 dB und verzerrend, je 105 Szenen zu 40 s; verpasste Anlaute im Lautsprecherfall 12/20/30 dB
    /// je 65): ohne Schutz 12 bzw. 124 Auslösungen, 7/11/19 verpasst; 3 dB 0 bzw. 42, 13/13/16; 5 dB 0 bzw. 3, 19/13/17;
    /// 6 dB 0 bzw. 0, 21/16/17; 10 dB (wie bei Sprache) 0 bzw. 0, 31/18/17.
    /// </summary>
    public const float GeraeuschSicherheitDb = 6f;

    /// <summary>Lernschritt der Kopplung je Rahmen (5 dB je Sekunde).</summary>
    public const float LernschrittDb = 0.05f;

    public const float MinKopplungDb = -60f;
    public const float MaxKopplungDb = 10f;

    /// <summary>
    /// Ab dieser Sprachwahrscheinlichkeit (RNNoise) ist ein Wiedergaberahmen sprachartig. Gemessen an eigenen Signalen je
    /// 60 s: Sprache 98 % der Rahmen darüber, Spielton (-24 und -12 dBFS), Musik und weißes Rauschen je 0 %.
    /// </summary>
    public const float SprachSchwelle = 0.9f;

    private readonly float[] _verlauf = new float[HalteRahmen];
    private int _index;
    private int _seitSprache = HalteRahmen;
    private float _jetztDb = -100f;

    public Echoschutz() => Array.Fill(_verlauf, -100f);

    /// <summary>
    /// Abstand bei nicht sprachartiger Wiedergabe, Voreinstellung <see cref="GeraeuschSicherheitDb"/>; nur für
    /// Vergleichsmessungen änderbar. Minus unendlich heißt: Bei Geräusch ruht der Schutz und lernt nicht (Stand des Moduls
    /// aec3 vor der Gegenprüfung).
    /// </summary>
    public float GeraeuschAbstandDb { get; set; } = GeraeuschSicherheitDb;

    /// <summary>
    /// Abstand bei sprachartiger Wiedergabe, ab Werk <see cref="SicherheitDb"/> (hinter AEC3 bleibt ein Rest). Ohne Echoreferenz, wenn der Wächter den
    /// Schutz mit der Funk-Wiedergabe des eigenen Mischers einschaltet (Klangstufe C2a), setzt die Sendekette <see cref="FunkspurSicherheitDb"/>.
    /// </summary>
    public float SprachAbstandDb { get; set; } = SicherheitDb;

    /// <summary>
    /// Abstand ohne Echoreferenz (Klangstufe C2a): Die Kopplung ist dann der Median von Mikrofon minus höchster Funk-Wiedergabe der letzten 400 ms; die
    /// Echospitzen liegen bis etwa 6 dB darüber (wenn der jetzige Rahmen der lauteste ist). 6 dB sperren also das Echo, die eigene Stimme nur, wenn sie
    /// nicht über dem Echo liegt; mit 10 dB fehlten kurzen Sprüchen mitten im Funk die Anfänge (Prüfstand, kurze Sprüche nach dem Echo-Urteil).
    /// </summary>
    public const float FunkspurSicherheitDb = 6f;

    /// <summary>Gelernte Kopplung (Echo minus Wiedergabe) in dB.</summary>
    public float KopplungDb { get; private set; }

    /// <summary>So viele Lernschritte (1 s), bis die Kopplung als gelernt gilt; vorher ist sie der vorsichtige Startwert.</summary>
    public const int GelerntAbSchritten = 100;

    private int _lernschritte;

    /// <summary>Höchster Wiedergabepegel der letzten 400 ms in dBFS.</summary>
    public float WiedergabeDb { get; private set; } = -100f;

    /// <summary>Lag in den letzten 400 ms ein sprachartiger Wiedergaberahmen (dann gilt <see cref="SicherheitDb"/>)?</summary>
    public bool Sprachartig => _seitSprache < HalteRahmen;

    /// <summary>
    /// Pegel des Wiedergaberahmens, der gerade als Echoreferenz ans APM ging, und ob er sprachartig ist. Ohne
    /// Sprachschätzung auf der Referenz gilt jeder Rahmen als sprachartig (der Schutz wie vor dem Modul aec3).
    /// </summary>
    public void Wiedergabe(float pegelDb, bool sprachartig = true)
    {
        var pegel = float.IsFinite(pegelDb) ? pegelDb : -100f;
        _verlauf[_index] = pegel;
        _jetztDb = pegel;
        _index = (_index + 1) % HalteRahmen;
        _seitSprache = sprachartig && pegel >= AktivAbDb ? 0 : Math.Min(_seitSprache + 1, HalteRahmen);
        var hoechster = -100f;
        var verzug = VerzugRahmen;
        if (verzug >= 0)
        {
            // Im Takt des Echos (Klangstufe C2a, ohne Echoreferenz): die Wiedergabe von vor <verzug> Rahmen, ±2 Rahmen.
            for (var k = Math.Max(0, verzug - 2); k <= Math.Min(HalteRahmen - 1, verzug + 2); k++)
                hoechster = MathF.Max(hoechster, _verlauf[(_index - 1 - k + (2 * HalteRahmen)) % HalteRahmen]);
        }
        else
        {
            foreach (var v in _verlauf) hoechster = MathF.Max(hoechster, v);
        }
        WiedergabeDb = hoechster;
    }

    /// <summary>
    /// Verschiebung des Echos in Rahmen (Klangstufe C2a): Ohne Echoreferenz schaltet der Wächter den Schutz mit der Funk-Wiedergabe des eigenen
    /// Mischers ein und kennt dann aus dem Gleichlauf, wie spät das Echo im Mikrofon ist. Statt des höchsten Pegels der letzten 400 ms gilt dann der
    /// Pegel in dieser Verschiebung (±20 ms): Das Echo liegt nie darüber (die Kopplung ist dazu im selben Takt gemessen), die eigene Stimme im Funk
    /// fast immer. -1 (ab Werk): 400 ms wie bisher.
    /// </summary>
    public int VerzugRahmen { get; set; } = -1;

    /// <summary>
    /// Bewertet einen Mikrofonrahmen (Pegel vor dem APM) und lernt die Kopplung, solange die Wiedergabe gerade spielt. Wahr
    /// heißt: Echoverdacht, dieser Rahmen darf keine Phase beginnen.
    /// </summary>
    public bool Verdacht(float mikrofonDb, bool gateOffen, bool sprechtaste)
    {
        if (WiedergabeDb < AktivAbDb || !float.IsFinite(mikrofonDb)) return false;
        var sprache = Sprachartig;
        if (!sprache && float.IsNegativeInfinity(GeraeuschAbstandDb)) return false;
        var verdacht = mikrofonDb < WiedergabeDb + KopplungDb + (sprache ? SprachAbstandDb : GeraeuschAbstandDb + WaechterAbstandDb);
        if (!gateOffen && !sprechtaste && _jetztDb >= AktivAbDb)
        {
            var differenz = mikrofonDb - WiedergabeDb;
            KopplungDb = Math.Clamp(KopplungDb + (differenz > KopplungDb ? LernschrittDb : -LernschrittDb), MinKopplungDb, MaxKopplungDb);
            _lernschritte++;
        }
        return verdacht;
    }

    /// <summary>
    /// Das Echo-Urteil des Wächters korrigiert die gelernte Kopplung (Klangstufe C2a, Entwurf "Ineinandergreifen" 7: kein zweiter Schutz
    /// daneben): Sie steigt auf das, was der Wächter in den Echo-Phasen gemessen hat (Median von Mikrofon minus höchster Wiedergabe der letzten
    /// 400 ms, also dieselbe Größe, die hier gelernt wird), nie nach unten. Danach lernt der Echoschutz weiter wie bisher. Nur Aufnahme-Thread.
    /// </summary>
    public void KopplungKorrigieren(float gemessenDb)
    {
        if (!float.IsFinite(gemessenDb)) return;
        // Hat er noch nicht gelernt (keine Echoreferenz bisher, Startwert 0 dB), gilt das Gemessene; sonst nur nach oben.
        var neu = _lernschritte < GelerntAbSchritten ? gemessenDb : MathF.Max(KopplungDb, gemessenDb);
        KopplungDb = Math.Clamp(neu, MinKopplungDb, MaxKopplungDb);
    }

    private float _waechterAbstand;

    /// <summary>
    /// Mehr Abstand bei Spielton, den der Wächter verlangt (Klangstufe C2a, Urteil Spielton), zusätzlich zu <see cref="GeraeuschAbstandDb"/>; nie
    /// negativ. Gilt nur für nicht sprachartige Wiedergabe. Nur Aufnahme-Thread.
    /// </summary>
    public float WaechterAbstandDb
    {
        get => _waechterAbstand;
        set => _waechterAbstand = float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }

    public void Zuruecksetzen()
    {
        Array.Fill(_verlauf, -100f);
        _seitSprache = HalteRahmen;
        WiedergabeDb = -100f;
        _jetztDb = -100f;
        KopplungDb = 0f;
        _lernschritte = 0;
    }
}
