// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>
/// Führt das <see cref="SprachGate"/> nach den drei Modi der <see cref="GateEinstellung"/> (A26 bis A28), je
/// 10-ms-Rahmen im Aufnahme-Thread, ohne Allokation.
/// <para>
/// Das Gate selbst bleibt unverändert: Zustandsmaschine, Bestätigung (4 aus 10), Vorlauf, Nachlauf, Hysterese,
/// Rauschboden und die Selbstjustierung der Sprachschwelle sind seine. Die Führung stellt nur zwei Größen vor jedem
/// Rahmen ein. Erstens die Linie: Das Gate öffnet über Rauschboden plus wirksamem Abstand, und der wirksame Abstand ist
/// sein Selbstabstand minus Empfindlichkeit mal <see cref="GateEinstellungen.EmpfindlichkeitDb"/>. Mit einem Stellbereich
/// von 120 dB je Stufe (statt 6 dB für den Nutzerregler) setzt die Führung die Empfindlichkeit so, dass Rauschboden
/// plus wirksamer Abstand genau die gewünschte Linie ergibt. Das Gate rechnet Boden und Abstand vor dem Nachführen des
/// Bodens, daher stimmt die Linie auf den Rahmen genau. Die Führung ist der einzige Besitzer der Linie (Klangstufe C1): Sie führt die
/// Wünsche zusammen (per Maximum) und hält sie im Korridor (<see cref="Linie"/>); seit C1 liegt der Korridor relativ zur gelernten
/// Funkstimme, vorher und solange die Stimme unbekannt ist wie in 0.6.0 (Grenzlinie bzw. Korridor des Modus Lernend). Zweitens die Sprachsicherheit: Ein Rahmen unter ihr geht mit
/// Wahrscheinlichkeit 0 ins Gate (offen mit derselben Hysterese von 0,15 wie das Gate). Im Modus Fest geht ein
/// erfüllender Rahmen mit 1 hinein, damit die Selbstjustierung der Sprachschwelle dort nicht mitredet.
/// </para>
/// </summary>
internal sealed class GateFuehrung
{
    /// <summary>Stellbereich des Empfindlichkeitsreglers je Stufe; deckt jede Linie von -100 bis 0 dBFS ab.</summary>
    public const float StellbereichDb = 120f;

    /// <summary>Hysterese der Sprachsicherheit bei offenem Gate, wie <see cref="GateEinstellungen.SchwelleHysterese"/>.</summary>
    public const float SicherheitHysterese = 0.15f;

    private readonly SprachGate _gate;
    private GateEinstellung _e = GateVorgaben.Standard;
    private float _hintergrund = GateVorgaben.MinPegelDb;

    public GateFuehrung()
    {
        _gate = new SprachGate(new GateEinstellungen
        {
            EmpfindlichkeitDb = StellbereichDb, EmpfindlichkeitSchwelle = 0f, VorlaufMs = GateVorgaben.VorlaufAbWerkMs,
        });
    }

    public SprachGate Gate => _gate;

    /// <summary>Nur im Aufnahme-Thread setzen; wird nicht noch einmal bereinigt. Stellt auch den Vorlauf des Gates (Klangstufe C1).</summary>
    public GateEinstellung Einstellung
    {
        get => _e;
        set
        {
            _e = value;
            _gate.VorlaufMs = value.VorlaufMs;
        }
    }

    /// <summary>Linie des letzten Rahmens in dBFS: Über ihr öffnet das Gate (Pegel, bei erfüllter Sprachsicherheit).</summary>
    public float LinieDb { get; private set; } = GateVorgaben.Standard.SchwelleDb;

    /// <summary>
    /// Die Linie für die Anzeige (rote Linie) in dBFS: ab hier beginnt eine Sendung. Mit gelernter Stimme nie unter der Mindest-Funklautstärke,
    /// auch wenn die Linie des Gates darunter liegt (sie hält nur eine laufende Phase offen); sonst gleich <see cref="LinieDb"/>.
    /// </summary>
    public float AnzeigeLinieDb => _gate.MindestDbfs > LinieDb ? _gate.MindestDbfs : LinieDb;

    /// <summary>Gelernter Hintergrundpegel (Modus Lernend) in dBFS; wird in allen Modi mitgeführt.</summary>
    public float HintergrundDb => _hintergrund;

    /// <summary>Das Gate war nach dem letzten Rahmen offen (Senden oder Nachlauf).</summary>
    public bool Offen => _gate.Zustand is GateZustand.Senden or GateZustand.Nachlauf;

    /// <summary>
    /// Die gelernte Funkstimme am Gate in dBFS (Pegelstufe: Grundpegel plus Verstärkung), NaN solange sie nicht bekannt ist. Setzt die
    /// Sendekette vor jedem Rahmen; nur Aufnahme-Thread.
    /// </summary>
    public float StimmeDb { get; set; } = float.NaN;

    /// <summary>
    /// Bezug der Mindest-Funklautstärke am Gate in dBFS (Pegelstufe: <see cref="Verarbeitung.Pegelstufe.AnkerDbfs"/> plus Verstärkung): Sie
    /// liegt <see cref="GateEinstellung.MindestAbstandDb"/> darunter. NaN: wie <see cref="StimmeDb"/>. Setzt die Sendekette vor jedem Rahmen.
    /// </summary>
    public float BezugDb { get; set; } = float.NaN;

    /// <summary>
    /// Weiterer Wunsch an die Linie in dBFS (Klangstufe C2: der Wächter, etwa bei Raumgeräusch); minus unendlich heißt keiner. Er wird
    /// mit dem Wunsch des Modus per Maximum zusammengeführt, nie addiert, und bleibt unter der Obergrenze. Nur Aufnahme-Thread.
    /// </summary>
    public float ZusatzWunschDb { get; set; } = float.NegativeInfinity;

    private volatile bool _mindestAn = true;

    /// <summary>
    /// Klangmodul "Mindest-Funklautstärke" (seit 0.9.0, Sarevoc 06.10.2026, Q48): an (ab Werk) hält eine Phase zurück, bis ihr Anlauf so laut ist wie die
    /// Mindest-Funklautstärke; aus gibt es diese Bedingung nicht, Linie und Obergrenze bleiben. Der Wunsch des Wächters bei Raumgeräusch gehört zur
    /// Auto Dauersend-Dämpfung und wirkt weiter. Aus jedem Thread setzbar.
    /// </summary>
    public bool MindestAn
    {
        get => _mindestAn;
        set => _mindestAn = value;
    }

    /// <summary>Der Korridor des letzten Rahmens (Untergrenze, Obergrenze, Stimme), NaN wo keiner gilt.</summary>
    public KorridorStand Korridor { get; private set; } = KorridorStand.Leer;

    /// <summary>
    /// Vor <see cref="SprachGate.Verarbeiten"/> bzw. der Sendeaufbereitung aufrufen: lernt, setzt die Linie und liefert
    /// die Wahrscheinlichkeit, die ins Gate gehen soll. <paramref name="pegelDbfs"/> ist der Pegel des Rahmens, den das
    /// Gate gleich bekommt. <paramref name="echoVerdacht"/> (aus dem <see cref="Echoschutz"/>) verhindert, dass dieser
    /// Rahmen eine Phase beginnt; eine offene Phase bleibt davon unberührt.
    /// </summary>
    public float Vorbereiten(float pegelDbfs, float sprachwahrscheinlichkeit, bool sprechtaste, bool echoVerdacht = false)
    {
        var offen = Offen;
        var boden = _gate.RauschbodenDbfs;
        Lernen(pegelDbfs, offen, sprechtaste || _gate.TasteGedrueckt);

        var linie = Linie(boden);
        LinieDb = linie;
        _gate.Empfindlichkeit = (_gate.AbstandAutoDb - (linie - boden)) / StellbereichDb;

        // Gesperrtes Echo ohne Echoreferenz (Klangstufe C2a) nicht in die Selbstjustierung des Gates.
        _gate.OhneStatistik = echoVerdacht && EchoOhneStatistik;
        if (echoVerdacht && !offen) return 0f;
        var p = float.IsNaN(sprachwahrscheinlichkeit) ? 0f : Math.Clamp(sprachwahrscheinlichkeit, 0f, 1f);
        var sicherheit = _e.Sprachsicherheit;
        var grenze = offen ? MathF.Max(0f, sicherheit - SicherheitHysterese) : sicherheit;
        var erfuellt = p >= grenze;
        if (_e.Modus == GateModus.Fest) return erfuellt ? 1f : 0f;
        return erfuellt ? p : 0f;
    }

    /// <summary>
    /// Nur zum Vergleich in Messungen (<c>MessungKlangC1</c>): die Mindest-Funklautstärke als Untergrenze der Linie wie im ersten Bau von C1,
    /// statt als Freigabe der Phase. Falsch (Voreinstellung): Freigabe über den Vorpuffer (<see cref="SprachGate.MindestDbfs"/>).
    /// </summary>
    internal bool MindestAlsLinie { get; set; }

    /// <summary>
    /// Der Wunsch des Wächters (<see cref="ZusatzWunschDb"/>) gilt auch als Mindest-Funklautstärke der Freigabe (Voreinstellung, Klangstufe C2a); falsch
    /// nur zum Vergleich im Prüfstand (MessungKlangC2): dann hebt er nur die Linie.
    /// </summary>
    internal bool ZusatzAlsFreigabe { get; set; } = true;

    /// <summary>
    /// Echoverdacht-Rahmen gehen nicht in die Selbstjustierung des Gates (Klangstufe C2a); die Sendekette setzt das, solange der Echoschutz ohne
    /// Echoreferenz mit der Funk-Wiedergabe arbeitet (dort liegt das ganze Echo im Mikrofon, hinter AEC3 nur ein Rest). Nur Aufnahme-Thread.
    /// </summary>
    public bool EchoOhneStatistik { get; set; }

    /// <summary>Als Freigabe verlangt der Wunsch des Wächters höchstens Stimme minus 2 dB in den lautesten 150 ms (kurze Funksprüche schaffen das, Prüfstand).</summary>
    public const float RaumFreigabeUnterStimmeDb = 2f;

    /// <summary>Während der Raum-Anhebung öffnet ein Sprachrahmen ab Stimme minus so viel sofort (SprachGate.FunkniveauDbfs).</summary>
    public const float RaumFunkniveauUnterStimmeDb = 6f;

    /// <summary>
    /// Die Linie: Fest die gezogene Schwelle. Sonst der Wunsch des Modus (Automatisch: Boden plus Abstand, Lernend: Hintergrund plus
    /// Abstand), mit weiteren Wünschen (<see cref="ZusatzWunschDb"/>) per Maximum zusammengeführt und gedeckelt durch die Obergrenze.
    /// <list type="bullet">
    /// <item>Funkstimme bekannt (<see cref="StimmeDb"/>, Klangstufe C1): Obergrenze Stimme minus <see cref="GateEinstellung.ObergrenzeAbstandDb"/>,
    /// in Automatisch und Lernend gleich. Die Mindest-Funklautstärke (Stimme minus <see cref="GateEinstellung.MindestAbstandDb"/>) ist keine
    /// Untergrenze der Linie, sondern eine Bedingung an die Phase (<see cref="SprachGate.MindestDbfs"/>): Eine Phase öffnet erst, wenn ihr
    /// Anlauf insgesamt so laut ist, und bleibt dann offen, auch wenn Silben leiser werden. Als Untergrenze der Linie (erster Bau) schnitt
    /// sie Wörter mitten im Satz ab und hielt doch leise Sprache nicht draußen, weil 4 von 10 Rahmen über der Linie genügen (MessungKlangC1).
    /// Ganz unten hält die Bodenuntergrenze die Linie auch beim Lernen über den Resten der Rauschunterdrückung.</item>
    /// <item>Noch nicht bekannt: wie bis 0.6.0. Automatisch nie unter der Grenzlinie (ab Werk ohne Wirkung), ohne Obergrenze; Lernend im
    /// Korridor <see cref="GateEinstellung.KorridorVonDb"/> bis <see cref="GateEinstellung.KorridorBisDb"/>. Eine Mindest-Funklautstärke aus der
    /// Stimme gibt es dann nicht: Bei leisem Mikrofon und ungelernter Pegelstufe sperrte sie jede Sendung, und die Pegelstufe lernte nie.</item>
    /// </list>
    /// </summary>
    private float Linie(float boden)
    {
        var stimme = StimmeDb;
        _gate.MindestDbfs = float.NegativeInfinity;
        _gate.FunkniveauDbfs = float.PositiveInfinity;
        var bekannt = float.IsFinite(stimme);
        // Ein Wunsch des Wächters gilt nur mit gelernter Stimme: Ohne sie gibt es keine Obergrenze, die ihn deckelt (Entwurf: nie über die Decke).
        // Hat ein Rahmen auf Funkniveau die Phase geöffnet, gilt er für diese Phase nicht (Nacharbeit C2a: nie einen Funkspruch abschneiden).
        // Gegenprüfung C2a, H2: Eine offene Phase schließt die Anhebung nie, Nachlauf und Halten gehen mit der normalen Linie.
        var zusatz = bekannt && !_gate.FunkniveauPhase && !Offen ? ZusatzWunschDb : float.NegativeInfinity;
        // Funkniveau öffnet sofort mit dem ganzen Vorpuffer, mit bekannter Stimme immer (Gegenprüfung C2a, H1: nach dem Echo-Urteil drückt RNNoise
        // eigene Anlaute im Echo-Gemisch): ab Stimme minus 6 dB; während der Raum-Anhebung ab ihrem Wunsch, also 10 dB über dem erkannten leisen
        // Niveau (höchstens Stimme minus 2 dB), H2. Bei Stimmen im Raum (Kinder, Fernseher) liegt das weit unter der Stimme, bei eigener leiser Rede
        // (12 dB darunter) bei Stimme minus 2 dB, sonst gingen ihre lautesten 50 ms wieder hinaus.
        if (bekannt)
        {
            _gate.FunkniveauDbfs = float.IsFinite(zusatz) ? zusatz : stimme - RaumFunkniveauUnterStimmeDb;
        }
        if (_e.Modus == GateModus.Fest)
        {
            // Q9 (Klangstufe C2a): Auch in Fest darf der Wächter bei Raumgeräusch unempfindlicher stellen, nie über Stimme minus Obergrenze.
            Korridor = new KorridorStand(stimme, float.NaN, float.NaN, BezugDb);
            var fest = _e.SchwelleDb;
            if (zusatz > fest) fest = MathF.Max(fest, MathF.Min(zusatz, stimme - _e.ObergrenzeAbstandDb));
            return Math.Clamp(fest, GateVorgaben.MinPegelDb, GateVorgaben.MaxPegelDb);
        }
        // Der Boden zählt mindestens mit der Bodenuntergrenze: Hinter RNNoise liegt er in ruhigen Räumen bei -100 dBFS, und
        // ohne Untergrenze bliebe die Linie dort unabhängig vom Abstand an einer festen Grenze stehen (Regler ohne Wirkung).
        var wunsch = _e.Modus == GateModus.Automatisch
            ? MathF.Max(boden, GateVorgaben.BodenUntergrenzeAutomatischDb) + _e.AbstandDb
            : MathF.Max(MathF.Max(boden, _hintergrund), bekannt ? GateVorgaben.BodenUntergrenzeAutomatischDb : float.NegativeInfinity) + _e.AbstandDb;
        // Mehrere Regler wünschen: das Maximum zählt, nie die Summe (Entwurf, Ineinandergreifen 3).
        wunsch = MathF.Max(wunsch, zusatz);
        var (unten, oben) = Grenzen(stimme);
        // Klangmodul aus: keine Mindest-Funklautstärke aus der Stimme (ohne gelernte Stimme gibt es sie ohnehin nicht).
        if (bekannt && !MindestAn) unten = float.NegativeInfinity;
        float linie;
        if (bekannt && !MindestAlsLinie)
        {
            linie = MathF.Min(wunsch, oben);
            _gate.MindestDbfs = unten;
            // Wunsch des Wächters bei Raumgeräusch (Klangstufe C2a, Q38): Er gilt auch für die Freigabe der Phase, wie die Mindest-Funklautstärke;
            // so hält das Gate leise Sprache an ihrem Mittel zurück, nicht nur an einzelnen Rahmen über der Linie, und eine Phase auf Funkniveau
            // geht mit dem Vorlauf hinaus. Höchstens so hoch, dass Sprache auf Funkniveau (Obergrenze) die Freigabe schafft.
            if (ZusatzAlsFreigabe && zusatz > unten) _gate.MindestDbfs = MathF.Max(unten, MathF.Min(zusatz, stimme - RaumFreigabeUnterStimmeDb) - _gate.FreigabeZuschlagDb);
        }
        else
        {
            linie = MathF.Max(unten, MathF.Min(wunsch, oben));
        }
        Korridor = new KorridorStand(stimme, unten > GateVorgaben.MinPegelDb ? unten : float.NaN, float.IsFinite(oben) ? oben : float.NaN, BezugDb);
        return Math.Clamp(linie, GateVorgaben.MinPegelDb, GateVorgaben.MaxPegelDb);
    }

    /// <summary>Unter- und Obergrenze der Linie (Automatisch und Lernend); ohne Obergrenze plus unendlich.</summary>
    private (float Unten, float Oben) Grenzen(float stimme)
    {
        if (float.IsFinite(stimme))
        {
            // Gegenprüfung C1, M1: Liegt der Bezug über der jetzt leiseren Stimme, käme die Untergrenze an die Obergrenze heran oder darüber; sie bleibt
            // mindestens die Mindestbreite des Korridors darunter (die Pegelstufe zieht den Anker bei mehr als 6 dB Abweichung nach).
            var oben = stimme - _e.ObergrenzeAbstandDb;
            // Nacharbeit M1 (Koordination 04.10.2026): Ist die gesendete Funkstimme leiser als der Bezug, gilt die Untergrenze ab der Stimme (nie
            // höher als Stimme minus Abstand); nach oben bleibt der Bezug.
            var unten = (float.IsFinite(BezugDb) ? MathF.Min(BezugDb, stimme) : stimme) - _e.MindestAbstandDb;
            return (MathF.Min(unten, oben - GateVorgaben.KorridorMindestBreiteDb), oben);
        }
        return _e.Modus == GateModus.Lernend ? (_e.KorridorVonDb, _e.KorridorBisDb) : (_e.GrenzlinieDb, float.PositiveInfinity);
    }

    /// <summary>
    /// Hintergrundpegel als asymmetrischer Quantil-Folger: je Rahmen höchstens 10 dB / (Anstieg * 100) nach oben und
    /// 10 dB / (Rückkehr * 100) nach unten. Nicht bei gedrückter Taste; bei offenem Gate nur Rahmen deutlich unter der
    /// Obergrenze, damit die eigene Stimme die Linie nicht mitten im Satz hochzieht.
    /// </summary>
    private void Lernen(float pegel, bool offen, bool taste)
    {
        if (!float.IsFinite(pegel) || taste) return;
        var oben = float.IsFinite(StimmeDb) ? StimmeDb - _e.ObergrenzeAbstandDb : _e.KorridorBisDb;
        if (offen && pegel >= oben - GateVorgaben.LernabstandUnterKorridorDb) return;
        var hoch = GateVorgaben.LernstufeDb / (_e.AnstiegSekunden * 100f);
        var runter = GateVorgaben.LernstufeDb / (_e.RueckkehrSekunden * 100f);
        _hintergrund = pegel > _hintergrund
            ? MathF.Min(pegel, _hintergrund + hoch)
            : MathF.Max(pegel, _hintergrund - runter);
    }
}
