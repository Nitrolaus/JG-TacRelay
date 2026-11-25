// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Maschine;

/// <summary>
/// Bedeutung, Voreinstellung und Grenzen der <see cref="GateEinstellung"/> (A26 bis A28). Alle Pegel sind dBFS über
/// den mittleren quadratischen Wert eines 10-ms-Rahmens, dieselbe Skala wie <see cref="PegelMeldung.PegelDbfs"/>.
/// <list type="bullet">
/// <item><b>Fest:</b> Die Linie liegt bei <see cref="GateEinstellung.SchwelleDb"/>.</item>
/// <item><b>Automatisch:</b> Die Linie folgt dem Rauschboden (Minimum-Statistik über 2 s) im Abstand
/// <see cref="GateEinstellung.AbstandDb"/>. Der Boden zählt dabei mindestens mit <see cref="BodenUntergrenzeAutomatischDb"/>:
/// Hinter AEC3 und RNNoise ist er in einem ruhigen Raum fast digitale Stille (-100 dBFS), und Reste der Verarbeitung
/// weit unter jeder Stimme sollen dort nicht öffnen. Mit der Voreinstellung (9 dB) liegt die Linie dann bei
/// <see cref="MinLinieAutomatischDb"/>; der Abstand verschiebt sie auch dort, sonst bliebe der Regler wirkungslos. Unter
/// <see cref="GateEinstellung.GrenzlinieDb"/> geht die Linie nie (Sarevoc, Stufe 2c: die Zone, in der sich die Automatik bewegen darf).
/// Ab Werk liegt die Grenzlinie bei <see cref="MinPegelDb"/>, unter jeder möglichen automatischen Linie (mindestens
/// <see cref="BodenUntergrenzeAutomatischDb"/>), und ändert nichts.</item>
/// <item><b>Lernend:</b> Wie automatisch, aber über dem gelernten Hintergrundpegel statt über dem Rauschboden, und
/// immer im Korridor <see cref="GateEinstellung.KorridorVonDb"/> bis <see cref="GateEinstellung.KorridorBisDb"/>. Die Untergrenze
/// des Korridors hat hier die Rolle der Grenzlinie; die Grenzlinie selbst gilt nur im Modus Automatisch.
/// Der Hintergrundpegel steigt um höchstens 10 dB je <see cref="GateEinstellung.AnstiegSekunden"/> und fällt um
/// höchstens 10 dB je <see cref="GateEinstellung.RueckkehrSekunden"/>. Er lernt aus Rahmen, die nicht gesendet
/// werden, und aus gesendeten Rahmen der Sprachaktivierung, die mindestens 6 dB unter der Korridor-Obergrenze liegen:
/// Dort liegt nach Absicht des Nutzers nicht mehr die eigene Stimme. Mit schnellem Anstieg und langsamer Rückkehr
/// folgt er nicht dem Mittel, sondern einem oberen Quantil des Hintergrunds (bei 2 s und 10 s dem 83-%-Quantil):
/// Seltene Klicks heben ihn kaum, anhaltendes Geschrei im Raum schon.</item>
/// <item><b>Sprachsicherheit</b> (0 bis 1): So sicher muss die Spracherkennung (RNNoise) sein, damit ein Rahmen als
/// Sprache zählt. Fest: genau dieser Wert. Automatisch und Lernend: Untergrenze; die Selbstjustierung des Gates hebt
/// die Anforderung an, wenn Geräusche der Umgebung sprachähnlich sind (höchstens 0,8), und bleibt dabei mindestens 0,4.
/// 0 heißt deshalb nur im Modus Fest: allein der Pegel zählt.</item>
/// <item>Die Rückmeldungen "kein Sprechen" und "abgeschnitten" verschieben dauerhaft die Linie (Fest:
/// <see cref="GateEinstellung.SchwelleDb"/>, sonst <see cref="GateEinstellung.AbstandDb"/>) und die Sprachsicherheit;
/// der neue Stand steht danach in <see cref="IKlangMaschine.Gate"/> und kann gespeichert werden.</item>
/// </list>
/// </summary>
public static class GateVorgaben
{
    public const float MinPegelDb = -100f;
    public const float MaxPegelDb = 0f;
    public const float MinAbstandDb = 0f;
    public const float MaxAbstandDb = 40f;
    public const float MinZeitSekunden = 0.1f;
    public const float MaxZeitSekunden = 600f;

    /// <summary>
    /// Linie der Voreinstellung im ruhigen Raum (Boden unter <see cref="BodenUntergrenzeAutomatischDb"/>, Abstand 9 dB).
    /// Hinter der Kette liegt Sprache dank der Pegelstufe (bis 0.6.0 Stufe 2c der AGC2) um -35 bis -15 dBFS; -60 dBFS hält Reste der Rauschunterdrückung
    /// draußen, die bei hoher AGC-Verstärkung bis gegen -70 dBFS reichen und für RNNoise sprachähnlich wirken können
    /// (gemessen am Echo-Prüfstand, Wahrscheinlichkeit um 0,6). Wer den Abstand kleiner stellt, geht bewusst darunter.
    /// </summary>
    public const float MinLinieAutomatischDb = -60f;

    /// <summary>
    /// Untergrenze des Rauschbodens für die automatische Linie: <see cref="MinLinieAutomatischDb"/> minus der Abstand der
    /// Voreinstellung (9 dB), also -69 dBFS. So bleibt die Voreinstellung bei -60 dBFS, und ein größerer oder kleinerer
    /// Abstand verschiebt die Linie auch dann, wenn der gemessene Boden darunter liegt.
    /// </summary>
    public const float BodenUntergrenzeAutomatischDb = MinLinieAutomatischDb - 9f;

    /// <summary>Um so viel steigt oder fällt der Hintergrundpegel im Lernmodus je Anstiegs- bzw. Rückkehrzeit.</summary>
    public const float LernstufeDb = 10f;

    /// <summary>Gesendete Rahmen lernen nur mit, wenn sie so weit unter der Korridor-Obergrenze liegen.</summary>
    public const float LernabstandUnterKorridorDb = 6f;

    /// <summary>
    /// Vorlauf des Gates ab Werk (Klangstufe C1, Q19): gemessen im Klangkorpus (570 Wortanfänge, drei Rauschmuster) schnitten 30 / 80 / 120 ms
    /// 67 / 24 / 12 Wortanfänge an, für +29 / +47 ms Verzug am Spruchanfang (Median, Rauschfilter Standard). Die Sprechtaste hat keinen Vorlauf.
    /// </summary>
    public const int VorlaufAbWerkMs = 120;

    public const int MinVorlaufMs = 0;

    /// <summary>Mehr bringt nichts: Beim Öffnen gehen höchstens <see cref="Verarbeitung.GateEinstellungen.BurstHoechstRahmen"/> vergangene Rahmen hinaus.</summary>
    public const int MaxVorlaufMs = 150;

    /// <summary>
    /// Mindest-Funklautstärke ab Werk als Abstand unter der gelernten Funkstimme (Klangstufe C1). Relativ, nicht absolut: Bei leisem
    /// Mikrofon und noch ungelernter Pegelstufe liegt die Stimme am Gate tief (bei Sarevoc -48 bis -55 dBFS); eine feste Grenze um -38 dBFS
    /// sperrte dort jede Sendung, die Pegelstufe lernte nie, und die Sperre bliebe.
    /// <para>
    /// Gemessen mit dem Klangkorpus (<c>MessungKlangC1</c>, 492 Wortanfänge normaler Äußerungen, drei Rauschmuster, Vorlauf 120 ms,
    /// Freigabe über den Vorpuffer mit 8 dB Zuschlag, der erste Stand von C1; ab Werk gilt seit klang-c1b ein Zuschlag von 4 dB,
    /// siehe <see cref="Verarbeitung.SprachGate.FreigabeZuschlagDb"/>): angeschnitten ohne Untergrenze 0, bei 30 und 25 dB 0, bei 20 dB 4, bei 15 dB 27; von
    /// Sprache 12 dB unter der Stimme gingen bei 25 dB 450, bei 15 dB 309, bei 9 dB 0 Wortanfänge hinaus. Ab Werk soll normales Sprechen
    /// sicher senden (Q12), darum 25 dB; enger stellt "Mindestlautstärke einmessen" (12 dB leiser eingemessen: 15 dB, Linie 3 dB unter dem
    /// leisen Satz, <see cref="Verarbeitung.Mindesteinmessung.LinieVersatzDb"/>).
    /// </para>
    /// </summary>
    public const float MindestAbstandAbWerkDb = 25f;

    /// <summary>Obergrenze ab Werk: die gelernte Funkstimme minus 6 dB (Entwurf, "Decke"); weniger Abstand ist nicht erlaubt.</summary>
    public const float ObergrenzeAbstandAbWerkDb = 6f;

    public const float MinObergrenzeAbstandDb = ObergrenzeAbstandAbWerkDb;

    /// <summary>Die Untergrenze liegt mindestens so weit unter der Obergrenze (wie die Mindestbreite des Korridors der Oberfläche).</summary>
    public const float KorridorMindestBreiteDb = 3f;

    /// <summary>Weiter unter der Stimme wirkt die Untergrenze praktisch nicht mehr.</summary>
    public const float MaxMindestAbstandDb = 60f;

    /// <summary>
    /// Voreinstellung: automatisch, 9 dB über dem Rauschboden (audio-dsp, Empfehlung 6), Sprachsicherheit 0,5 wie der
    /// Startwert der Selbstjustierung, Grenzlinie ganz unten (ohne Wirkung), Korridor relativ zur gelernten Stimme
    /// (<see cref="MindestAbstandAbWerkDb"/>, <see cref="ObergrenzeAbstandAbWerkDb"/>), Vorlauf <see cref="VorlaufAbWerkMs"/>. Für Fest und
    /// Lernend stehen Startwerte bereit, die der Nutzer auf der Grafik zieht.
    /// </summary>
    public static GateEinstellung Standard { get; } = new(
        GateModus.Automatisch, SchwelleDb: -45f, AbstandDb: 9f, KorridorVonDb: -60f, KorridorBisDb: -30f,
        AnstiegSekunden: 2f, RueckkehrSekunden: 10f, Sprachsicherheit: 0.5f, GrenzlinieDb: MinPegelDb,
        MindestAbstandDb: MindestAbstandAbWerkDb, ObergrenzeAbstandDb: ObergrenzeAbstandAbWerkDb, VorlaufMs: VorlaufAbWerkMs);

    /// <summary>
    /// Bringt eine Einstellung in den gültigen Bereich: unbekannter Modus wird Automatisch, NaN nimmt den Wert der
    /// Voreinstellung, Pegel und Grenzlinie auf -100 bis 0 dBFS, Abstand 0 bis 40 dB, Zeiten 0,1 bis 600 s, Sprachsicherheit
    /// 0 bis 1. Ein vertauschter Korridor wird umgedreht. Obergrenze mindestens 6 dB unter der Stimme, Untergrenze mindestens
    /// <see cref="KorridorMindestBreiteDb"/> unter der Obergrenze (die Untergrenze rückt nach unten), höchstens
    /// <see cref="MaxMindestAbstandDb"/>; Vorlauf auf 0 bis <see cref="MaxVorlaufMs"/> in ganzen 10 ms.
    /// </summary>
    public static GateEinstellung Bereinigen(GateEinstellung e)
    {
        var s = Standard;
        var modus = Enum.IsDefined(e.Modus) ? e.Modus : s.Modus;
        var von = Begrenzen(e.KorridorVonDb, s.KorridorVonDb, MinPegelDb, MaxPegelDb);
        var bis = Begrenzen(e.KorridorBisDb, s.KorridorBisDb, MinPegelDb, MaxPegelDb);
        if (von > bis) (von, bis) = (bis, von);
        var oben = Begrenzen(e.ObergrenzeAbstandDb, s.ObergrenzeAbstandDb, MinObergrenzeAbstandDb, MaxMindestAbstandDb - KorridorMindestBreiteDb);
        var unten = Begrenzen(e.MindestAbstandDb, s.MindestAbstandDb, oben + KorridorMindestBreiteDb, MaxMindestAbstandDb);
        return new GateEinstellung(
            modus,
            Begrenzen(e.SchwelleDb, s.SchwelleDb, MinPegelDb, MaxPegelDb),
            Begrenzen(e.AbstandDb, s.AbstandDb, MinAbstandDb, MaxAbstandDb),
            von,
            bis,
            Begrenzen(e.AnstiegSekunden, s.AnstiegSekunden, MinZeitSekunden, MaxZeitSekunden),
            Begrenzen(e.RueckkehrSekunden, s.RueckkehrSekunden, MinZeitSekunden, MaxZeitSekunden),
            Begrenzen(e.Sprachsicherheit, s.Sprachsicherheit, 0f, 1f),
            Begrenzen(e.GrenzlinieDb, s.GrenzlinieDb, MinPegelDb, MaxPegelDb),
            unten,
            oben,
            Math.Clamp(e.VorlaufMs, MinVorlaufMs, MaxVorlaufMs) / 10 * 10);
    }

    private static float Begrenzen(float wert, float ersatz, float min, float max) =>
        float.IsNaN(wert) ? ersatz : Math.Clamp(wert, min, max);
}
