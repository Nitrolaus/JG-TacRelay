// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.ExceptionServices;

namespace Funk.Klang.Eingabe;

/// <summary>
/// Zustand der Joystick-Sprechtaste über der Geräteschicht, ohne eigenen Thread und ohne eigene Uhr: offene Geräte
/// (erkannt am Pfad), Belegungen mit Wiederfinden, Knopf- und Hutwechsel, Belegen, Abstecken. Gehört dem Lesethread;
/// die Zeit (Millisekunden, monoton) gibt der Aufrufer mit, damit die Tests sie vorstellen können. Aufgezählt wird
/// außerhalb: <see cref="Faelliges"/> sagt, wann eine Suche fällig ist, <see cref="Abgleichen"/> nimmt ihr Ergebnis.
/// <para>
/// Wiederfinden: Zuerst zählt der Pfad. Fehlt er (umgesteckt an einen Anschluss, der den Pfad ändert), gilt ein Gerät
/// mit gleicher VID/PID und gleichem Produktnamen, sofern es genau eines gibt; bei mehreren bleibt die Belegung ohne
/// Gerät, statt am falschen Stick zu senden. Nie genommen werden Geräte, deren Pfad eine andere Belegung genau meint,
/// und nie ein virtuelles Gerät für ein echtes oder umgekehrt: Ein Klon von Joystick Guardian teilt VID/PID und Namen
/// mit dem Original, kann seine Knöpfe aber anders mischen. Ein anderer Name ist ein Widerspruch (VIRPIL etwa teilt
/// eine VID über alle Geräte); nur wenn eine Seite keinen Namen kennt, reicht VID/PID.
/// </para>
/// <para>
/// Frisch drücken: Eine Belegung zählt erst nach einem Loslassen, wenn ihre Eingabe beim Belegen, beim Öffnen des
/// Geräts oder in dessen ersten <see cref="RuheMs"/> schon gedrückt war. So sendet das Belegen selbst nicht, ein
/// Kippschalter in Dauerstellung löst nicht aus, und ein wieder angestecktes Gerät funkt nicht von allein los.
/// </para>
/// </summary>
internal sealed class Joystickauswertung
{
    /// <summary>
    /// Nach dem Öffnen liefert DirectInput Platzhalter, bis der erste echte Bericht kommt; VIRPIL sendet im Leerlauf nur
    /// etwa alle 450 ms (gemessen im JG Signal Monitor). Solange zählt ein Wechsel weder zum Belegen noch als Drücken.
    /// </summary>
    public const int RuheMs = 750;

    /// <summary>Solange gedrückt ist, wird der Stand der belegten Geräte in diesem Abstand nachgelesen.</summary>
    public const int WaechterMs = 250;

    /// <summary>
    /// Erster Abstand des Nachsuchens, solange eine Belegung ihr Gerät nicht findet oder belegt wird. Beim Belegen bleibt
    /// es dabei; sonst verdoppelt er sich bis <see cref="SuchabstandHoechstensMs"/>, denn eine Aufzählung kostet gemessen
    /// 140 bis 190 ms Rechenzeit (sechs Geräte, 29.09.2026), und ein fehlendes Gerät fehlt oft den ganzen Abend.
    /// Eine Windows-Meldung, ein verlorenes Gerät, neue Belegungen und das Belegen setzen ihn zurück.
    /// </summary>
    public const int SuchabstandMs = 3000;

    public const int SuchabstandHoechstensMs = 30_000;

    /// <summary>Zweites Aufzählen nach einer Windows-Meldung: DirectInput sieht ein neues Gerät manchmal erst etwas später.</summary>
    public const int NachsucheMs = 1000;

    /// <summary>Warteschlange des Lesethreads: 64 Griffe, davon einer für Nachrichten und zwei für Wecken und Takt.</summary>
    public const int GeraeteHoechstens = 61;

    private readonly ISpielgeraeteSchicht _schicht;
    private readonly List<Geraet> _geraete = [];
    private readonly Eingabewechsel[] _puffer = new Eingabewechsel[64];
    private Eintrag[] _eintraege = [];
    private long _suchenAb = long.MaxValue;
    private long _nachsucheAb = long.MaxValue;
    private long _waechterAb = long.MaxValue;
    private int _suchabstand = SuchabstandMs;

    public Joystickauswertung(ISpielgeraeteSchicht schicht)
    {
        ArgumentNullException.ThrowIfNull(schicht);
        _schicht = schicht;
    }

    /// <summary>Zwei Flanken derselben Belegung zählen erst mit diesem Abstand doppelt (prellende Knöpfe, Doppeltipp).</summary>
    public const int EntprellMs = 150;

    /// <summary>Wechsel der Sprechtaste: wahr, sobald eine Belegung zum Sprechen gedrückt ist; falsch, wenn keine mehr.</summary>
    public Action<bool>? Wechsel { get; set; }

    /// <summary>Eine gehaltene Funktion außer Sprechen (Melden) ging hinunter oder hinauf; wie <see cref="Wechsel"/> über alle ihre Belegungen.</summary>
    public Action<Tastenfunktion, bool>? Gehalten { get; set; }

    /// <summary>
    /// Eine Funktion mit Flanke (Sendenetz, Funkfeld, Phasen) ging hinunter: frisch gedrückt und frühestens <see cref="EntprellMs"/>
    /// nach der letzten Flanke derselben Belegung.
    /// </summary>
    public Action<Tastenfunktion>? Flanke { get; set; }

    /// <summary>Eine Belegung der Funktion <see cref="Tastenfunktion.Melden"/> ist gedrückt.</summary>
    public bool MeldenGedrueckt { get; private set; }

    public Action<Tastenbelegung>? Gelernt { get; set; }

    public Action? GeraeteGeaendert { get; set; }

    public bool IstGedrueckt { get; private set; }

    public bool Lernt { get; private set; }

    /// <summary>Steigt bei jeder Änderung der Liste offener Geräte (der Lesethread baut dann seine Warteliste neu).</summary>
    public int Version { get; private set; }

    public int Anzahl => _geraete.Count;

    /// <summary>Die offenen Geräte als unveränderliche Liste für andere Threads.</summary>
    public SpielgeraetInfo[] Liste { get; private set; } = [];

    public long Aufzaehlungen { get; private set; }

    public long Oeffnungsfehler { get; private set; }

    public long Verloren { get; private set; }

    public long Wechselzahl { get; private set; }

    public string? LetzterFehler { get; private set; }

    public nint Ereignis(int index) => _geraete[index].Zugang.Ereignis;

    public SpielgeraetInfo Info(int index) => _geraete[index].Info;

    /// <summary>
    /// Gleicht die offenen Geräte mit einer Aufzählung ab: Fehlende werden geschlossen, neue geöffnet (einige ms je
    /// Gerät). Null heißt: Die Suche scheiterte, alles bleibt, wie es ist.
    /// </summary>
    public void Abgleichen(IReadOnlyList<SpielgeraetInfo>? aufgezaehlt, long jetzt)
    {
        if (aufgezaehlt is null)
        {
            LetzterFehler = "Aufzählen der Spielgeräte scheiterte.";
            Nachsuchen(jetzt);
            Aufzaehlungen++;
            return;
        }
        var geaendert = false;
        for (var i = _geraete.Count - 1; i >= 0; i--)
        {
            if (Suchen(aufgezaehlt, _geraete[i].Info) >= 0) continue;
            _geraete[i].Zugang.Dispose();
            _geraete.RemoveAt(i);
            geaendert = true;
        }
        foreach (var info in aufgezaehlt)
        {
            if (_geraete.Count >= GeraeteHoechstens) break;
            if (Offen(info) >= 0) continue;
            var zugang = _schicht.Oeffnen(info, out var fehler);
            if (zugang is null)
            {
                Oeffnungsfehler++;
                LetzterFehler = fehler;
                continue;
            }
            if (!zugang.StandLesen(out var stand))
            {
                zugang.Dispose();
                Oeffnungsfehler++;
                LetzterFehler = "Erster Stand nicht lesbar: " + info.Kennung.Name;
                continue;
            }
            _geraete.Add(new Geraet(zugang, info, stand, jetzt + RuheMs));
            geaendert = true;
        }
        if (geaendert) ListeGeaendert(jetzt);
        if (NachsuchenNoetig()) Nachsuchen(jetzt);
        else _suchabstand = SuchabstandMs;
        // Erst zählen, wenn die Liste steht: Wer auf die erste Aufzählung wartet, sieht dann auch die Geräte.
        Aufzaehlungen++;
    }

    /// <summary>Das Ereignis eines Geräts kam: gepufferte Wechsel lesen und auswerten. Der heiße Pfad, ohne Allokation.</summary>
    public void Lesen(int index, long jetzt)
    {
        var g = _geraete[index];
        var n = g.Zugang.WechselLesen(_puffer, out var unvollstaendig);
        if (n < 0)
        {
            Verlieren(index, jetzt);
            return;
        }
        for (var i = 0; i < n; i++)
        {
            var neu = g.Stand.Mit(_puffer[i]);
            if (neu != g.Stand) Uebergang(g, neu, jetzt);
        }
        // Nur wenn sich Knöpfe oder Hüte rührten: Ein Ereignis ohne Wechsel (etwa eine Achse) kostet so nichts weiter.
        if (n > 0 || unvollstaendig) Nachlesen(index, jetzt);
    }

    /// <summary>Puffer und Stand lesen, als wäre ein Ereignis gekommen (Rauchtest: der heiße Pfad am echten Gerät).</summary>
    public void LesenMitStand(int index, long jetzt)
    {
        var anzahl = _geraete.Count;
        Lesen(index, jetzt);
        if (_geraete.Count == anzahl) Nachlesen(index, jetzt);
    }

    /// <summary>Takt für Geräte ohne Ereignis (DIDC_POLLEDDEVICE): jedes davon lesen.</summary>
    public void Abfragen(long jetzt)
    {
        for (var i = _geraete.Count - 1; i >= 0; i--)
        {
            if (i < _geraete.Count && _geraete[i].Zugang.Ereignis == 0) Lesen(i, jetzt);
        }
    }

    /// <summary>Windows meldete ein Gerät an oder ab: offene Geräte prüfen, gleich und etwas später neu aufzählen.</summary>
    public void Geraeteaenderung(long jetzt)
    {
        for (var i = _geraete.Count - 1; i >= 0; i--)
        {
            if (i < _geraete.Count) Nachlesen(i, jetzt);
        }
        _suchenAb = jetzt;
        _nachsucheAb = jetzt + NachsucheMs;
        _suchabstand = SuchabstandMs;
    }

    /// <summary>Neue Belegungen, alle zum Sprechen (siehe <see cref="ZuordnungenSetzen"/>).</summary>
    public void BelegungenSetzen(IReadOnlyList<Tastenbelegung> belegungen, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(belegungen);
        var z = new Tastenzuordnung[belegungen.Count];
        for (var i = 0; i < z.Length; i++) z[i] = new Tastenzuordnung(Tastenfunktion.Sprechen, belegungen[i]);
        ZuordnungenSetzen(z, jetzt);
    }

    /// <summary>
    /// Neue Belegungen mit ihren Funktionen. Eingaben, die gerade gedrückt sind, zählen erst nach dem Loslassen (frisch drücken);
    /// war Sprechen oder Melden gedrückt, kommt das Loslassen.
    /// </summary>
    public void ZuordnungenSetzen(IReadOnlyList<Tastenzuordnung> zuordnungen, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(zuordnungen);
        var neu = new Eintrag[zuordnungen.Count];
        for (var i = 0; i < neu.Length; i++) neu[i] = new Eintrag(zuordnungen[i].Taste, zuordnungen[i].Funktion);
        _eintraege = neu;
        foreach (var e in _eintraege)
        {
            e.Ziel = Finden(e);
            e.Gesperrt = Aktiv(e);
        }
        Gesamt(jetzt);
        _suchabstand = SuchabstandMs;
        if (NachsuchenNoetig()) _suchenAb = Math.Min(_suchenAb, jetzt);
    }

    /// <summary>Belegen: Der nächste frisch gedrückte Knopf oder die nächste Hutrichtung irgendeines Geräts wird gemeldet.</summary>
    public void LernenStarten(long jetzt)
    {
        Lernt = true;
        // Gleich aufzählen, damit auch ein eben angestecktes oder freigegebenes Gerät belegt werden kann.
        _suchenAb = Math.Min(_suchenAb, jetzt);
        _suchabstand = SuchabstandMs;
    }

    public void LernenBeenden() => Lernt = false;

    /// <summary>Millisekunden bis zur nächsten fälligen Arbeit (<see cref="Faelliges"/>), oder -1 für keine.</summary>
    public long NaechsteFrist(long jetzt)
    {
        var frist = Math.Min(_suchenAb, _nachsucheAb);
        if (_etwasUnten) frist = Math.Min(frist, _waechterAb);
        return frist == long.MaxValue ? -1 : Math.Max(0, frist - jetzt);
    }

    /// <summary>Wächter ausführen, falls fällig. Rückgabe wahr: Jetzt soll neu aufgezählt werden (<see cref="Abgleichen"/>).</summary>
    public bool Faelliges(long jetzt)
    {
        var suchen = false;
        if (jetzt >= _nachsucheAb)
        {
            _nachsucheAb = long.MaxValue;
            suchen = true;
        }
        if (jetzt >= _suchenAb)
        {
            _suchenAb = long.MaxValue;
            suchen = true;
        }
        if (_etwasUnten && jetzt >= _waechterAb) Waechter(jetzt);
        return suchen;
    }

    /// <summary>
    /// Schließt alle Geräte; eine gedrückte Taste gilt danach als losgelassen. Losgelassen wird zuerst: Wirft das Freigeben
    /// eines Geräts, bleibt so keine Taste gedrückt stehen, und die übrigen Geräte werden trotzdem freigegeben.
    /// </summary>
    public void Schliessen(long jetzt)
    {
        foreach (var e in _eintraege) e.Ziel = null;
        Gesamt(jetzt);
        var geraete = _geraete.ToArray();
        _geraete.Clear();
        Liste = [];
        Version++;
        Exception? fehler = null;
        foreach (var g in geraete)
        {
            try
            {
                g.Zugang.Dispose();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                fehler ??= ex;
            }
        }
        if (fehler is not null) ExceptionDispatchInfo.Throw(fehler);
    }

    /// <summary>
    /// Welches von <paramref name="anwesend"/> meint <paramref name="gesucht"/>? Regeln siehe Klassenbeschreibung.
    /// <paramref name="beansprucht"/> sagt, ob ein Pfad genau zu einer anderen Belegung gehört. -1: keines.
    /// </summary>
    public static int Zuordnen(Geraetekennung gesucht, IReadOnlyList<Geraetekennung> anwesend, Func<string, bool>? beansprucht)
    {
        ArgumentNullException.ThrowIfNull(anwesend);
        var pfad = gesucht.Pfad ?? "";
        if (pfad.Length > 0)
        {
            for (var i = 0; i < anwesend.Count; i++)
            {
                if (string.Equals(anwesend[i].Pfad, pfad, StringComparison.OrdinalIgnoreCase)) return i;
            }
        }
        if (gesucht.Vid == 0 && gesucht.Pid == 0) return -1;
        var name = (gesucht.Name ?? "").Trim();
        int kandidaten = 0, kandidat = -1;
        for (var i = 0; i < anwesend.Count; i++)
        {
            var k = anwesend[i];
            if (k.Vid != gesucht.Vid || k.Pid != gesucht.Pid || k.IstVirtuell != gesucht.IstVirtuell) continue;
            var anderer = (k.Name ?? "").Trim();
            if (name.Length > 0 && anderer.Length > 0 && !string.Equals(anderer, name, StringComparison.Ordinal)) continue;
            if (!string.IsNullOrEmpty(k.Pfad) && beansprucht?.Invoke(k.Pfad) == true) continue;
            kandidaten++;
            kandidat = i;
        }
        return kandidaten == 1 ? kandidat : -1;
    }

    /// <summary>Ob <paramref name="pfad"/> genau der gespeicherte Pfad einer Belegung ist.</summary>
    public static bool Beansprucht(IEnumerable<Tastenbelegung> belegungen, string pfad)
    {
        ArgumentNullException.ThrowIfNull(belegungen);
        foreach (var b in belegungen)
        {
            var p = Geraetekennung.Lesen(b.Geraet).Pfad;
            if (p.Length > 0 && string.Equals(p, pfad, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static int Suchen(IReadOnlyList<SpielgeraetInfo> liste, SpielgeraetInfo info)
    {
        for (var i = 0; i < liste.Count; i++)
        {
            if (Gleich(liste[i], info)) return i;
        }
        return -1;
    }

    private int Offen(SpielgeraetInfo info)
    {
        for (var i = 0; i < _geraete.Count; i++)
        {
            if (Gleich(_geraete[i].Info, info)) return i;
        }
        return -1;
    }

    /// <summary>Dasselbe offene Gerät: gleicher Pfad; nur ohne Pfad (kein HID-Gerät) entscheidet die Instanz-GUID.</summary>
    private static bool Gleich(SpielgeraetInfo a, SpielgeraetInfo b)
    {
        var pa = a.Kennung.Pfad ?? "";
        var pb = b.Kennung.Pfad ?? "";
        if (pa.Length > 0 || pb.Length > 0) return string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase);
        return a.Instanz == b.Instanz;
    }

    private void Uebergang(Geraet g, Eingabestand neu, long jetzt)
    {
        var alt = g.Stand;
        g.Stand = neu;
        Wechselzahl++;
        var ruhe = jetzt < g.RuhigAb;
        if (Lernt && !ruhe && NeuGedrueckt(g, alt, neu) is { } gelernt)
        {
            Lernt = false;
            // Die Taste, mit der eben belegt wurde, sendet nicht: alles Gedrückte gilt bis zum Loslassen als gesperrt.
            foreach (var e in _eintraege) e.Gesperrt |= Aktiv(e);
            Gelernt?.Invoke(gelernt);
        }
        foreach (var e in _eintraege)
        {
            if (!ReferenceEquals(e.Ziel, g)) continue;
            if (!EingabeAktiv(e, neu)) e.Gesperrt = false;
            else if (ruhe) e.Gesperrt = true;
        }
        Gesamt(jetzt);
    }

    /// <summary>Knöpfe vor Hüten, der niedrigste zuerst; ein Hut zählt nur aus der Mitte heraus.</summary>
    private static Tastenbelegung? NeuGedrueckt(Geraet g, Eingabestand alt, Eingabestand neu)
    {
        var knoepfe = neu.Knoepfe & ~alt.Knoepfe;
        if (knoepfe != UInt128.Zero)
            return Joystickeingabe.Knopf((int)UInt128.TrailingZeroCount(knoepfe) + 1, g.Info.Kennung);
        for (var h = 0; h < Joystickeingabe.Huete; h++)
        {
            var richtung = neu.Hut(h);
            if (alt.Hut(h) < 0 && richtung >= 0) return Joystickeingabe.Hut(h + 1, (Hutrichtung)richtung, g.Info.Kennung);
        }
        return null;
    }

    private void Nachlesen(int index, long jetzt)
    {
        var g = _geraete[index];
        if (!g.Zugang.StandLesen(out var stand))
        {
            Verlieren(index, jetzt);
            return;
        }
        if (stand != g.Stand) Uebergang(g, stand, jetzt);
    }

    /// <summary>Hängeschutz: Solange gedrückt ist, den Stand der belegten Geräte nachlesen, falls ein Ereignis fehlte.</summary>
    private void Waechter(long jetzt)
    {
        _waechterAb = jetzt + WaechterMs;
        for (var i = _geraete.Count - 1; i >= 0; i--)
        {
            if (i >= _geraete.Count) continue;
            var g = _geraete[i];
            foreach (var e in _eintraege)
            {
                if (!ReferenceEquals(e.Ziel, g)) continue;
                Nachlesen(i, jetzt);
                break;
            }
        }
    }

    private void Verlieren(int index, long jetzt)
    {
        Verloren++;
        LetzterFehler = "Gerät verloren: " + _geraete[index].Info.Kennung.Name;
        _geraete[index].Zugang.Dispose();
        _geraete.RemoveAt(index);
        ListeGeaendert(jetzt);
        // War es nur ein kurzer Aussetzer, kommt es beim nächsten Aufzählen zurück.
        _suchenAb = Math.Min(_suchenAb, jetzt + NachsucheMs);
        _suchabstand = SuchabstandMs;
    }

    /// <summary>Nächste Suche planen (<see cref="SuchabstandMs"/>): im laufenden Abstand, der sich außer beim Belegen verdoppelt.</summary>
    private void Nachsuchen(long jetzt)
    {
        _suchenAb = Math.Min(_suchenAb, jetzt + _suchabstand);
        if (!Lernt) _suchabstand = (int)Math.Min(2L * _suchabstand, SuchabstandHoechstensMs);
    }

    private void ListeGeaendert(long jetzt)
    {
        Version++;
        foreach (var e in _eintraege)
        {
            var ziel = Finden(e);
            if (ReferenceEquals(ziel, e.Ziel)) continue;
            e.Ziel = ziel;
            e.Gesperrt = Aktiv(e);
        }
        var liste = new SpielgeraetInfo[_geraete.Count];
        for (var i = 0; i < liste.Length; i++) liste[i] = _geraete[i].Info;
        Liste = liste;
        Gesamt(jetzt);
        GeraeteGeaendert?.Invoke();
    }

    private Geraet? Finden(Eintrag e)
    {
        var kennungen = new Geraetekennung[_geraete.Count];
        for (var i = 0; i < kennungen.Length; i++) kennungen[i] = _geraete[i].Info.Kennung;
        var index = Zuordnen(e.Kennung, kennungen, BeanspruchtVonAnderer);
        return index < 0 ? null : _geraete[index];
    }

    private bool BeanspruchtVonAnderer(string pfad)
    {
        foreach (var e in _eintraege)
        {
            if (e.Kennung.Pfad.Length > 0 && string.Equals(e.Kennung.Pfad, pfad, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private bool NachsuchenNoetig()
    {
        if (Lernt) return true;
        foreach (var e in _eintraege)
        {
            if (e.Ziel is null) return true;
        }
        return false;
    }

    private static bool Aktiv(Eintrag e) => e.Ziel is { } g && EingabeAktiv(e, g.Stand);

    private static bool EingabeAktiv(Eintrag e, Eingabestand stand)
    {
        if (!e.IstHut) return e.Index is >= 0 and < Joystickeingabe.Knoepfe && stand.Knopf(e.Index);
        if (e.Index is < 0 or >= Joystickeingabe.Huete) return false;
        var d = stand.Hut(e.Index);
        if (d < 0) return false;
        var r = e.Richtung;
        // Hauptrichtung: auch in den beiden Nachbarschrägen gedrückt; Schräge: nur genau.
        return (r & 1) == 0 ? d == r || d == ((r + 1) & 7) || d == ((r + 7) & 7) : d == r;
    }

    /// <summary>
    /// Gesamtzustand nach jedem Wechsel: Sprechen und Melden als ODER über ihre Belegungen (gemeldet wird nur der Wechsel), jede
    /// andere Funktion als Flanke, sobald eine Belegung frisch gedrückt ist und ihre letzte Flanke <see cref="EntprellMs"/> zurückliegt.
    /// Solange irgendetwas gedrückt ist, läuft der Wächter. Ohne Allokation: Er läuft im heißen Pfad.
    /// </summary>
    private void Gesamt(long jetzt)
    {
        var sprechen = false;
        var melden = false;
        var unten = false;
        var eintraege = _eintraege;
        foreach (var e in eintraege)
        {
            var aktiv = !e.Gesperrt && Aktiv(e);
            unten |= aktiv;
            switch (e.Funktion)
            {
                case Tastenfunktion.Sprechen:
                    sprechen |= aktiv;
                    break;
                case Tastenfunktion.Melden:
                    melden |= aktiv;
                    break;
                default:
                    if (aktiv && !e.War && jetzt - e.LetzteFlanke >= EntprellMs)
                    {
                        e.LetzteFlanke = jetzt;
                        Flanke?.Invoke(e.Funktion);
                    }
                    break;
            }
            e.War = aktiv;
        }
        if (unten && !_etwasUnten) _waechterAb = jetzt + WaechterMs;
        _etwasUnten = unten;
        if (melden != MeldenGedrueckt)
        {
            MeldenGedrueckt = melden;
            Gehalten?.Invoke(Tastenfunktion.Melden, melden);
        }
        if (sprechen == IstGedrueckt) return;
        IstGedrueckt = sprechen;
        Wechsel?.Invoke(sprechen);
    }

    /// <summary>Irgendeine Belegung gilt als gedrückt (Sprechen, Melden oder eine Flanke, die noch gehalten wird): Der Wächter läuft.</summary>
    private bool _etwasUnten;

    private sealed class Geraet(IOffenesSpielgeraet zugang, SpielgeraetInfo info, Eingabestand stand, long ruhigAb)
    {
        public IOffenesSpielgeraet Zugang { get; } = zugang;

        public SpielgeraetInfo Info { get; } = info;

        public Eingabestand Stand { get; set; } = stand;

        public long RuhigAb { get; } = ruhigAb;
    }

    private sealed class Eintrag
    {
        public Eintrag(Tastenbelegung belegung, Tastenfunktion funktion)
        {
            Funktion = funktion;
            Kennung = Geraetekennung.Lesen(belegung.Geraet);
            if (Joystickeingabe.IstHut(belegung.Code, out var hut, out var richtung))
            {
                IstHut = true;
                Index = hut - 1;
                Richtung = (int)richtung;
            }
            else
            {
                Index = belegung.Code - 1;
            }
        }

        public Tastenfunktion Funktion { get; }

        public Geraetekennung Kennung { get; }

        public bool IstHut { get; }

        /// <summary>Beim letzten Gesamtzustand frisch gedrückt (für die Flanke).</summary>
        public bool War { get; set; }

        /// <summary>Zeit der letzten gemeldeten Flanke (Entprellung).</summary>
        public long LetzteFlanke { get; set; } = long.MinValue / 2;

        /// <summary>Knopf 0 bis 127 bzw. Hut 0 bis 3.</summary>
        public int Index { get; }

        public int Richtung { get; }

        public Geraet? Ziel { get; set; }

        public bool Gesperrt { get; set; }
    }
}

/// <summary>
/// Aufzählen der Spielgeräte: DirectInput im Betrieb, eine Attrappe in den Tests. Angelegt und benutzt im Suchthread,
/// weil DirectInputs EnumDevices einige hundert Millisekunden dauern kann.
/// </summary>
internal interface ISpielgeraeteSuche : IDisposable
{
    /// <summary>Hängt alle angeschlossenen Spielgeräte an <paramref name="ziel"/>. false: Aufzählen scheiterte.</summary>
    bool Aufzaehlen(List<SpielgeraetInfo> ziel);
}

/// <summary>
/// Die Geräteschicht unter der Joystick-Sprechtaste: DirectInput im Betrieb, eine Attrappe in den Tests. Sie wird im
/// Lesethread angelegt und nur dort benutzt.
/// </summary>
internal interface ISpielgeraeteSchicht : IDisposable
{
    /// <summary>Öffnet ein eben aufgezähltes Gerät nicht-exklusiv im Hintergrund zum Lesen; null mit Grund, wenn es nicht geht.</summary>
    IOffenesSpielgeraet? Oeffnen(SpielgeraetInfo geraet, out string? fehler);

    /// <summary>Windows meldete seit dem letzten Aufruf ein Gerät an oder ab (WM_DEVICECHANGE); setzt die Meldung zurück.</summary>
    bool GeraeteGeaendert();
}

/// <summary>Ein zum Lesen geöffnetes Spielgerät. Nur Knöpfe und Hüte, keine Achsen.</summary>
internal interface IOffenesSpielgeraet : IDisposable
{
    /// <summary>Wird gesetzt, wenn sich Knöpfe oder Hüte ändern oder der Zugriff verloren geht; 0 bei Geräten, die abgefragt werden müssen.</summary>
    nint Ereignis { get; }

    /// <summary>
    /// Gepufferte Wechsel seit dem letzten Aufruf, in Reihenfolge. Passen nicht alle in <paramref name="ziel"/>, lief der
    /// Puffer des Geräts über oder war der Zugriff kurz weg, ist <paramref name="unvollstaendig"/> gesetzt; dann zählt
    /// <see cref="StandLesen"/>. Rückgabe -1: Gerät weg (abgesteckt, Zugriff nicht wiederherstellbar).
    /// </summary>
    int WechselLesen(Span<Eingabewechsel> ziel, out bool unvollstaendig);

    /// <summary>Aktueller Stand. false: Gerät weg.</summary>
    bool StandLesen(out Eingabestand stand);
}

/// <summary>
/// Knöpfe und Hüte eines Spielgeräts. Bit i von <see cref="Knoepfe"/> ist Knopf i+1; je Hut ein Byte in
/// <see cref="Huete"/>: 0 Mitte, sonst Richtung + 1. Der Vorgabewert ist "nichts gedrückt, alle Hüte in der Mitte".
/// </summary>
internal readonly record struct Eingabestand(UInt128 Knoepfe, uint Huete)
{
    public bool Knopf(int index) => ((Knoepfe >> index) & UInt128.One) != UInt128.Zero;

    /// <summary>Richtung 0 bis 7 (<see cref="Hutrichtung"/>) oder -1 für die Mitte.</summary>
    public int Hut(int hut) => (int)((Huete >> (8 * hut)) & 0xFF) - 1;

    public Eingabestand MitKnopf(int index, bool unten)
    {
        var bit = UInt128.One << index;
        return this with { Knoepfe = unten ? Knoepfe | bit : Knoepfe & ~bit };
    }

    public Eingabestand MitHut(int hut, int richtung)
    {
        var schiebe = 8 * hut;
        var wert = richtung is >= 0 and <= 7 ? (uint)(richtung + 1) : 0u;
        return this with { Huete = (Huete & ~(0xFFu << schiebe)) | (wert << schiebe) };
    }

    public Eingabestand Mit(in Eingabewechsel w) => w.IstHut ? MitHut(w.Index, w.Wert) : MitKnopf(w.Index, w.Wert != 0);

    // Eigener Vergleich: Der erzeugte ginge über EqualityComparer<UInt128>.Default, das beim ersten Mal allokiert.
    public bool Equals(Eingabestand other) => Knoepfe == other.Knoepfe && Huete == other.Huete;

    public override int GetHashCode() => HashCode.Combine(Knoepfe, Huete);
}

/// <summary>
/// Ein gepufferter Wechsel eines Geräts. Knopf: <see cref="Index"/> 0 bis 127, <see cref="Wert"/> 1 gedrückt, 0 los.
/// Hut: Index 0 bis 3, Wert Richtung 0 bis 7 oder -1 für die Mitte.
/// </summary>
internal readonly record struct Eingabewechsel(bool IstHut, int Index, int Wert)
{
    public static Eingabewechsel Knopf(int index, bool unten) => new(false, index, unten ? 1 : 0);

    public static Eingabewechsel Hut(int hut, int richtung) => new(true, hut, richtung);
}
