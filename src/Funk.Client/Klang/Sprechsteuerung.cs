// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;

namespace Funk.Client.Klang;

/// <summary>Wohin die Melde-Taste gerade sprechen würde: Netz und Name, oder warum es nicht geht.</summary>
public readonly record struct MeldeEntscheid(ushort? NetzId, string Name, string? Hindernis);

/// <summary>Das Ziel der Melde-Taste an der Brücke zum Sprechkanal; im Betrieb die <see cref="KlangBruecke"/>.</summary>
public interface IMeldeziel
{
    /// <summary>
    /// Solange gesetzt, beginnt jede Sprechphase an diesem Netz statt an der Sendeauswahl; <c>null</c> heißt wieder Sendeauswahl. Mit
    /// <paramref name="umschalten"/> wahr (das Mikrofon bleibt offen, die Maschine beginnt keine neue Phase) beendet ein Wechsel eine laufende
    /// Phase, und der nächste Rahmen beginnt am neuen Ziel. Mit <paramref name="umschalten"/> falsch beginnt die Maschine selbst eine neue Phase
    /// (Melde-Taste los, das Mikrofon schließt; oder eine Taste öffnet es neu, auch im Nachlauf der anderen): Die laufende Phase bleibt bis zu
    /// ihrem letzten Rahmen an ihrem Ziel, denn der Melde-Thread holt die Rahmen von vor dem Wechsel erst danach ab, und die Maschine sendet noch
    /// ihren Nachlauf; erst die nächste Phase beginnt am neuen Ziel. Ein Aufruf mit <c>null</c> und <paramref name="umschalten"/> wahr beendet
    /// den Rest einer Meldung (Melde-Taste los, während die Sprechtaste das Mikrofon offen hält).
    /// </summary>
    void MeldezielSetzen(ushort? netzId, bool umschalten = true);
}

/// <summary>
/// Sprechtaste und Melde-Taste vor der Klangmaschine (RAID-S2-ENTWURF Abschnitt 4, A4). Das Mikrofon ist offen, solange eine von
/// beiden gehalten ist (ein ODER, wie der <c>SprechtastenVerbund</c> über die Quellen). Die Melde-Taste setzt für die Dauer des
/// Haltens das Meldenetz als Ziel (<see cref="IMeldeziel"/>), die Sendeauswahl bleibt; die Sprachaktivierung ruht, damit nach dem
/// Loslassen kein Nachlauf der Sprachaktivierung ins Meldenetz geht. Ohne Meldenetz oder ohne Senderecht dort öffnet sie nichts und sagt es
/// (<see cref="MeldenGehtNicht"/>), statt still nichts zu tun.
/// <para>
/// Der Nachlauf der Sprechtaste (Q66, seit 0.9.3, <see cref="IKlangMaschine.TastenNachlaufMs"/>) gilt für beide Tasten: Nach dem Loslassen der
/// Melde-Taste sendet die Maschine noch kurz weiter, und dieser Rest bleibt am Meldenetz (<see cref="IMeldeziel"/> ohne Umschalten). Wer sie im
/// Nachlauf wieder drückt, setzt die Meldung nahtlos fort. Kommt im Nachlauf die andere Taste (ein anderes Ziel), sagt die Steuerung der Maschine,
/// dass sie nicht fortsetzt (<see cref="IKlangMaschine.SprechtasteGedrueckt(bool)"/>): Die alte Phase endet mit ENDE an ihrem Ziel, die neue
/// beginnt am neuen, mit Klick beim Loslassen und beim Drücken (Prüfung 0.9.3).
/// </para>
/// <para>
/// Nur Client: Der Server prüft je Paket die Mitgliedschaft mit senden (6.3), nicht die Sendeauswahl; kein Steuerverkehr, kein neues
/// Recht. Die Ereignisse kommen aus den Threads der Tastenquellen, wie die Sprechtaste selbst; alles hier ist threadsicher.
/// </para>
/// </summary>
public sealed class Sprechsteuerung
{
    private readonly IKlangMaschine _maschine;
    private readonly IMeldeziel _ziel;
    private readonly Func<MeldeEntscheid> _meldenetz;
    private readonly Lock _sperre = new();
    private bool _sprechen;
    private bool _melden;

    /// <summary>Ziel der Sprechtaste in <see cref="_zielZuletzt"/>: die Sendeauswahl.</summary>
    private const int Sendeauswahl = -1;

    /// <summary>
    /// Wohin das Mikrofon zuletzt sprach: <see cref="Sendeauswahl"/> oder die Nummer des Meldenetzes. Nur ein Druck fürs selbe Ziel setzt einen
    /// Nachlauf fort; unter <see cref="_sperre"/>.
    /// </summary>
    private int _zielZuletzt = Sendeauswahl;

    /// <param name="meldenetz">Fragt beim Drücken der Melde-Taste, wohin sie spricht (Einstellung, Lage).</param>
    public Sprechsteuerung(IKlangMaschine maschine, IMeldeziel ziel, Func<MeldeEntscheid> meldenetz)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(ziel);
        ArgumentNullException.ThrowIfNull(meldenetz);
        _maschine = maschine;
        _ziel = ziel;
        _meldenetz = meldenetz;
    }

    /// <summary>Melden läuft, mit dem Namen des Meldenetzes (für die Meldezeile, solange gehalten).</summary>
    public event Action<string>? MeldenBegonnen;

    /// <summary>Die Melde-Taste ist los.</summary>
    public event Action? MeldenBeendet;

    /// <summary>Melden ging nicht, mit dem Satz für die Meldezeile ("Melden geht nicht: kein Senderecht in Kommando").</summary>
    public event Action<string>? MeldenGehtNicht;

    /// <summary>
    /// Die Sprachaktivierung ruht (<c>true</c>, beim Drücken) oder darf wieder (<c>false</c>, beim Loslassen). Die Oberfläche merkt
    /// es sich, damit sie die Sprachaktivierung in der Zwischenzeit nicht wieder einschaltet, und stellt sie danach nach den
    /// Sendenetzen ein (A10).
    /// </summary>
    public Action<bool>? SprachaktivierungRuht { get; set; }

    /// <summary>Die Melde-Taste ist gerade wirksam gedrückt.</summary>
    public bool Meldet
    {
        get
        {
            lock (_sperre) return _melden;
        }
    }

    public void SprechenGedrueckt()
    {
        bool oeffnen;
        var fortsetzen = false;
        lock (_sperre)
        {
            if (_sprechen) return;
            _sprechen = true;
            oeffnen = !_melden;
            if (oeffnen)
            {
                fortsetzen = _zielZuletzt == Sendeauswahl;
                _zielZuletzt = Sendeauswahl;
            }
        }
        if (!oeffnen) return;
        // Läuft noch der Rest einer Meldung (Nachlauf, oder die Maschine sah das Loslassen noch nicht), gehört er ans Meldenetz, der Spruch an die
        // Sendeauswahl (Gegenprüfung S2). Die Maschine beendet die Phase der Meldung mit ENDE und beginnt eine neue; bis zu diesem ENDE bleibt
        // die Brücke am Meldenetz, erst die neue Phase geht an die Sendeauswahl (Prüfung 0.9.3).
        _ziel.MeldezielSetzen(null, umschalten: false);
        _maschine.SprechtasteGedrueckt(fortsetzen);
    }

    public void SprechenLosgelassen()
    {
        bool schliessen;
        lock (_sperre)
        {
            if (!_sprechen) return;
            _sprechen = false;
            schliessen = !_melden;
        }
        if (schliessen) _maschine.SprechtasteLosgelassen();
    }

    public void MeldenGedrueckt()
    {
        var entscheid = _meldenetz();
        if (entscheid.Hindernis is { } grund || entscheid.NetzId is not { } netz)
        {
            MeldenGehtNicht?.Invoke(entscheid.Hindernis ?? "Melden geht nicht: kein Meldekanal");
            return;
        }
        bool oeffnen, fortsetzen;
        lock (_sperre)
        {
            if (_melden) return;
            _melden = true;
            oeffnen = !_sprechen;
            fortsetzen = _zielZuletzt == netz;
            _zielZuletzt = netz;
        }
        SprachaktivierungRuht?.Invoke(true);
        _maschine.Sprachaktivierung = false;
        // Hält die Sprechtaste das Mikrofon offen, endet ihre Phase hier, und der nächste Rahmen beginnt am Meldenetz. Sonst öffnet erst dieser
        // Druck: Was die Maschine davor aufnahm (etwa den Nachlauf der Sprechtaste), bleibt an seinem Ziel bis zum ENDE, mit dem die Maschine die
        // Phase beendet; die Meldung beginnt mit der neuen (Prüfung 0.9.3).
        _ziel.MeldezielSetzen(netz, umschalten: !oeffnen);
        if (oeffnen) _maschine.SprechtasteGedrueckt(fortsetzen);
        MeldenBegonnen?.Invoke(entscheid.Name);
    }

    public void MeldenLosgelassen()
    {
        bool schliessen;
        lock (_sperre)
        {
            if (!_melden) return;
            _melden = false;
            schliessen = !_sprechen;
            // Hält die Sprechtaste das Mikrofon offen, spricht es ab hier wieder an die Sendeauswahl.
            if (!schliessen) _zielZuletzt = Sendeauswahl;
        }
        if (schliessen) _maschine.SprechtasteLosgelassen();
        // Schließt das Mikrofon, gehören die Rahmen, die der Melde-Thread noch nicht abgeholt hat, zur Meldung: kein Umschalten, sonst
        // ginge ihr Rest als neue Phase an die Crew (Gegenprüfung S2). Hält die Sprechtaste es offen, geht es sofort an die Sendeauswahl.
        _ziel.MeldezielSetzen(null, umschalten: !schliessen);
        SprachaktivierungRuht?.Invoke(false);
        MeldenBeendet?.Invoke();
    }
}
