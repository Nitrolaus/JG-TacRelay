// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.Klang.Eingabe;

namespace Funk.Client.Modelle;

/// <summary>
/// Was die globalen Tasten und Joystickknöpfe auslösen (RAID-S2-ENTWURF Abschnitte 3 bis 5), an einer Stelle statt verstreut in
/// App.OnStartup und darum mit Attrappen prüfbar. Die Ereignisse kommen aus den Threads der Tastenquellen (Raw Input, DirectInput):
/// <list type="bullet">
/// <item>Sprechen und Melden gehen direkt an die <see cref="Sprechsteuerung"/> (zeitkritisch, dort threadsicher). Melden lässt die
/// Sprachaktivierung ruhen, bis die Taste los ist; danach stellt das Funkfeld sie nach den Sendenetzen ein.</item>
/// <item>Sendenetz vor und zurück, Funkfeld ablegen/holen und Phase vor/zurück gehen über den Oberflächentakt. Liegt das Funkfeld
/// in der Ablage, sagt die Meldezeile das neue Sendenetz.</item>
/// <item>Seit Klangstufe C2b ebenso "Dauersender stumm" (Q8) und "Mikro stumm" (Q32); beide sagen in der Meldezeile, was sie getan haben.</item>
/// <item>Seit 0.12.0 "Bereit" auf die offene Bereit-Abfrage der Leitung (<see cref="Bereitmeldung"/>); ohne offene Abfrage nichts.</item>
/// <item>Ein Phasenwechsel per Taste kommt in den Verlauf des Bretts.</item>
/// </list>
/// Raw Input gilt je Prozess und Klasse nur einmal (CODEMAP): Hier entsteht keine zweite Anmeldung, nur Abonnements an der einen Quelle.
/// </summary>
public sealed class Tastenbetrieb : IDisposable
{
    private readonly ITastenQuelle? _quelle;
    private readonly Sprechsteuerung _sprechen;
    private readonly IOberflaechentakt _takt;
    private readonly FunkfeldViewModel _funkfeld;
    private readonly FunkfeldAblage _ablage;
    private readonly Phasensteuerung _phasen;
    private readonly Teilnehmermeldungen _teilnehmer;
    private readonly BrettViewModel? _brett;
    private readonly Bereitmeldung? _bereit;

    /// <param name="quelle">Die globalen Tasten; ohne (Vorführung) nur die Ablage für Funkfeld und Meldungen.</param>
    /// <param name="bereit">"Bereit" auf die Bereit-Abfrage (seit 0.12.0); ohne Angabe tut die Taste nichts.</param>
    public Tastenbetrieb(ITastenQuelle? quelle, Sprechsteuerung sprechen, IOberflaechentakt takt, FunkfeldViewModel funkfeld, FunkfeldAblage ablage,
        Phasensteuerung phasen, Teilnehmermeldungen teilnehmer, BrettViewModel? brett = null, Bereitmeldung? bereit = null)
    {
        ArgumentNullException.ThrowIfNull(sprechen);
        ArgumentNullException.ThrowIfNull(takt);
        ArgumentNullException.ThrowIfNull(funkfeld);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(phasen);
        ArgumentNullException.ThrowIfNull(teilnehmer);
        _quelle = quelle;
        _sprechen = sprechen;
        _takt = takt;
        _funkfeld = funkfeld;
        _ablage = ablage;
        _phasen = phasen;
        _teilnehmer = teilnehmer;
        _brett = brett;
        _bereit = bereit;

        if (_quelle is not null)
        {
            _quelle.Gedrueckt += _sprechen.SprechenGedrueckt;
            _quelle.Losgelassen += _sprechen.SprechenLosgelassen;
            _quelle.Gehalten += Gehalten;
            _quelle.Ausgeloest += Ausgeloest;
        }
        _sprechen.SprachaktivierungRuht = Ruht;
        _sprechen.MeldenBegonnen += MeldenBegonnen;
        _sprechen.MeldenBeendet += MeldenBeendet;
        _sprechen.MeldenGehtNicht += MeldenGehtNicht;
        _ablage.AbgelegtGeaendert += AbgelegtGeaendert;
        if (_brett is not null) _phasen.Verlauf += _brett.VerlaufEintragen;
        AbgelegtGeaendert(_ablage.Abgelegt);
    }

    public void Dispose()
    {
        if (_quelle is not null)
        {
            _quelle.Gedrueckt -= _sprechen.SprechenGedrueckt;
            _quelle.Losgelassen -= _sprechen.SprechenLosgelassen;
            _quelle.Gehalten -= Gehalten;
            _quelle.Ausgeloest -= Ausgeloest;
        }
        _sprechen.SprachaktivierungRuht = null;
        _sprechen.MeldenBegonnen -= MeldenBegonnen;
        _sprechen.MeldenBeendet -= MeldenBeendet;
        _sprechen.MeldenGehtNicht -= MeldenGehtNicht;
        _ablage.AbgelegtGeaendert -= AbgelegtGeaendert;
        if (_brett is not null) _phasen.Verlauf -= _brett.VerlaufEintragen;
    }

    /// <summary>Melden (gehalten), aus dem Thread der Quelle.</summary>
    private void Gehalten(Tastenfunktion funktion, bool unten)
    {
        if (funktion != Tastenfunktion.Melden) return;
        if (unten) _sprechen.MeldenGedrueckt();
        else _sprechen.MeldenLosgelassen();
    }

    /// <summary>Eine Flanke, aus dem Thread der Quelle: alles Weitere auf dem Oberflächen-Thread.</summary>
    private void Ausgeloest(Tastenfunktion funktion) => _takt.Einreihen(() => _ = AusfuehrenAsync(funktion));

    private async Task AusfuehrenAsync(Tastenfunktion funktion)
    {
        switch (funktion)
        {
            case Tastenfunktion.NetzVor or Tastenfunktion.NetzZurueck:
                // A5, A7: durch die eigenen Netze, auch wenn das Spiel vorn ist; in der Ablage sagt es die Meldezeile.
                await _funkfeld.WeiterschaltenAsync(funktion == Tastenfunktion.NetzVor ? 1 : -1).ConfigureAwait(true);
                _takt.Einreihen(_teilnehmer.SendenetzGewechselt);
                break;
            case Tastenfunktion.FunkfeldUmschalten:
                _ablage.Umschalten();
                break;
            case Tastenfunktion.PhaseVor or Tastenfunktion.PhaseZurueck:
                await _phasen.TasteAsync(funktion == Tastenfunktion.PhaseVor ? 1 : -1).ConfigureAwait(true);
                break;
            case Tastenfunktion.DauersenderStumm:
                // Klangstufe C2b (Q8): zuletzt gedämpfter Funker stumm, ein zweiter Druck wieder normal; die Meldezeile sagt wen.
                _funkfeld.Dauersend.StummTaste();
                break;
            case Tastenfunktion.MikroStumm:
                // Klangstufe C2b (Q32): Mikro stumm an oder aus, die Meldezeile sagt es.
                _funkfeld.Mikro.TasteGedrueckt();
                break;
            case Tastenfunktion.Bereit when _bereit is not null:
                // Meldesystem (0.12.0): bereit melden, wenn die Leitung fragt; "gemeldet" steht danach am Knopf und über dem Spiel.
                await _bereit.Melden().ConfigureAwait(true);
                break;
        }
    }

    /// <summary>Die Sprachaktivierung ruht (Melden gedrückt) oder darf wieder; aus dem Thread der Quelle.</summary>
    private void Ruht(bool ruht)
    {
        _funkfeld.SprachaktivierungRuht = ruht;
        if (!ruht) _takt.Einreihen(_funkfeld.AusloesungGeaendert);
    }

    private void MeldenBegonnen(string netz) => _takt.Einreihen(() => _teilnehmer.MeldenBegonnen(netz));

    private void MeldenBeendet() => _takt.Einreihen(_teilnehmer.MeldenBeendet);

    private void MeldenGehtNicht(string text) => _takt.Einreihen(() => _teilnehmer.MeldenGehtNicht(text));

    private void AbgelegtGeaendert(bool abgelegt)
    {
        _funkfeld.Abgelegt = abgelegt;
        _teilnehmer.Abgelegt = abgelegt;
    }
}
