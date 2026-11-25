// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;

namespace Funk.Client.Klang;

/// <summary>
/// Die letzten Pegelmeldungen als Ring. 1024 Plätze reichen für 20 s bei 50 je Sekunde.
/// <para>
/// Echtzeitregel: Die Maschine meldet aus ihrem Thread, womöglich dem Audio-Thread. Der darf nie auf die Oberfläche
/// warten und nichts anlegen. Darum liest die Oberfläche ohne Sperre: Der Schreiber legt die Meldung in den Ring und
/// veröffentlicht erst danach den Zähler; der Leser liest den Zähler, kopiert und prüft danach, welche Plätze in der
/// Zwischenzeit überschrieben sein könnten, und verwirft genau die. Schreiber untereinander (etwa Zeitgeber der
/// Vorführung) trennt eine eigene Sperre, die die Oberfläche nie nimmt.
/// </para>
/// </summary>
public sealed class PegelVerlauf
{
    /// <summary>So weit reicht die Verlaufsgrafik zurück (A26: die letzten 10 s).</summary>
    public const long FensterMs = 10_000;

    /// <summary>So lange nach dem letzten Sprechen über der Linie bleibt der Hinweis "es wird nichts gesendet" stehen (ui-fix 0.6.2).</summary>
    public const long HinweisHaltMs = 1500;

    /// <summary>Ab dieser Sprachwahrscheinlichkeit zählt Pegel über der Linie als Sprechen (wie die Sprachsicherheit ab Werk).</summary>
    public const float HinweisSprache = 0.5f;

    private readonly PegelMeldung[] _ring;
    private readonly Lock _schreibSperre = new();
    private long _begonnen;
    private long _geschrieben;
    private int _ziel = (int)Sendeweg.Offen;
    private long _hinweisMs = long.MinValue / 2;
    private long _zurueckMs = long.MinValue / 2;

    public PegelVerlauf(int kapazitaet = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(kapazitaet, 2);
        _ring = new PegelMeldung[kapazitaet];
    }

    public int Kapazitaet => _ring.Length;

    /// <summary>Alle je eingegangenen Meldungen; ändert sich die Zahl, gibt es Neues.</summary>
    public long Gesamt => Volatile.Read(ref _geschrieben);

    /// <summary>
    /// Wohin gesendet würde (ui-fix 0.6.2, Grün-Fehler): Das Funkfeld setzt es aus der Sendeauswahl. Nur bei <see cref="Sendeweg.Offen"/> bleibt
    /// "sendet" der Maschine stehen; sonst geht nichts hinaus, und die Meldung wird ohne "sendet" abgelegt. So heißt Grün in jeder Ansicht
    /// (Pegelbalken, Schwellengrafik, Funkfeld, Meldezeile) dasselbe: Es geht tatsächlich etwas hinaus. Seit Q43 (0.8.0) heißt Gelb ebenso überall
    /// dasselbe: würde senden, geht aber nirgends hin (<see cref="PegelMeldung.WuerdeSenden"/>). Ohne Funkfeld (Tests, Messung) offen.
    /// Wird aus dem Oberflächen-Thread und dem Thread der Tastenquelle geschrieben, aus dem Thread der Maschine gelesen.
    /// </summary>
    public Sendeweg Weg
    {
        get => (Sendeweg)Volatile.Read(ref _ziel);
        set => Volatile.Write(ref _ziel, (int)value);
    }

    /// <summary>Der Satz, wenn die Sprachaktivierung eine Phase unter der Mindest-Funklautstärke zurückhält (seit 0.10.0, E4a).</summary>
    public const string UnterMindestText = "Stimme unter der Mindest-Funklautstärke";

    /// <summary>
    /// Der Hinweis zur jüngsten Meldung <paramref name="letzte"/> (ui-fix 0.6.2): Steht das Ziel nicht offen und wurde in den letzten
    /// <see cref="HinweisHaltMs"/> über der Linie gesprochen (oder die Sprechtaste gedrückt), der Satz dazu, etwa "Ruhe: es wird nichts gesendet".
    /// Steht es offen und hat die Maschine in den letzten <see cref="HinweisHaltMs"/> eine Phase unter der Mindest-Funklautstärke zurückgehalten
    /// (<see cref="PegelMeldung.Zurueckgehalten"/>, seit 0.10.0), <see cref="UnterMindestText"/>; sonst <c>null</c>. Ein nicht offenes Ziel geht vor:
    /// Dort ginge auch lautere Sprache nirgends hin.
    /// </summary>
    public string? Hinweis(in PegelMeldung letzte)
    {
        var ziel = Weg;
        if (ziel != Sendeweg.Offen) return letzte.ZeitMs - Volatile.Read(ref _hinweisMs) > HinweisHaltMs ? null : HinweisText(ziel);
        return letzte.ZeitMs - Volatile.Read(ref _zurueckMs) > HinweisHaltMs ? null : UnterMindestText;
    }

    /// <summary>
    /// Der Satz, wenn bei <paramref name="ziel"/> gesprochen wird; bei offenem Ziel <c>null</c>. Seit 0.11.2 (Sarevoc 13.10.2026: "die Meldung 'es
    /// wird nichts gesendet' will ich nicht ... raus heißt raus") gibt es die Sätze zu Ruhe, Stummschaltung und Trennung nirgends mehr, weder
    /// über dem Spiel noch in der Statuszeile oder am Pegelbalken; dass nichts hinausgeht, zeigt der Balken ohne Grün. Es bleibt nur der Satz,
    /// dass ein Kanal die Sprechtaste braucht und keine belegt ist: Das ist eine fehlende Einstellung, kein Zustand.
    /// </summary>
    public static string? HinweisText(Sendeweg ziel) => ziel switch
    {
        Sendeweg.OhneSprechtaste => "Kanal braucht die Sprechtaste - keine belegt",
        _ => null,
    };

    /// <summary>
    /// Nimmt eine Meldung auf; eine nicht endliche Zahl wird durch die Untergrenze ersetzt. Ohne Allokation; die Sperre
    /// teilen sich nur Schreiber, die Oberfläche wartet nie darauf und hält sie nie. Steht das <see cref="Weg"/> nicht offen, fällt "sendet" weg,
    /// und wo es ausgelöst hätte (<see cref="WuerdeAusloesen"/>), steht seit Q43 <see cref="PegelMeldung.WuerdeSenden"/>: gelb statt grün. Eine
    /// zurückgehaltene Phase (<see cref="PegelMeldung.Zurueckgehalten"/>, seit 0.10.0) merkt nur den Zeitpunkt für den Satz; der Balken bleibt
    /// dabei grau (Vorlage m-8, Fall 4), denn ausgelöst hat die Sprachaktivierung ja nicht.
    /// </summary>
    public void Hinzufuegen(PegelMeldung m)
    {
        m = Bereinigen(m);
        var weg = Weg;
        // Ohne belegte Sprechtaste sendet die Maschine nur, wenn eine Taste doch drückt (etwa die Melde-Taste vor dem Umschalten oder ein Knopf zum
        // Halten): Dann geht wirklich etwas hinaus, und es bleibt grün.
        if (weg != Sendeweg.Offen && !(weg == Sendeweg.OhneSprechtaste && m.Sendet))
        {
            var wuerde = WuerdeAusloesen(m);
            if (wuerde) Volatile.Write(ref _hinweisMs, m.ZeitMs);
            m = m with { Sendet = false, WuerdeSenden = wuerde };
        }
        if (m is { Zurueckgehalten: true, Sendet: false, Stumm: false }) Volatile.Write(ref _zurueckMs, m.ZeitMs);
        lock (_schreibSperre)
        {
            var n = _geschrieben;
            // Erst ansagen (voller Zaun, damit die Ansage vor dem Überschreiben sichtbar ist), dann schreiben, dann freigeben.
            Interlocked.Exchange(ref _begonnen, n + 1);
            _ring[n % _ring.Length] = m;
            Volatile.Write(ref _geschrieben, n + 1);
        }
    }

    /// <summary>Die jüngste Meldung.</summary>
    public bool Letzte(out PegelMeldung meldung)
    {
        while (true)
        {
            var ende = Volatile.Read(ref _geschrieben);
            if (ende == 0)
            {
                meldung = default;
                return false;
            }
            meldung = _ring[(ende - 1) % _ring.Length];
            Interlocked.MemoryBarrier();
            // Überschrieben (oder gerade in Arbeit) ist der Platz erst, wenn ein begonnener Schreibvorgang einmal herum ist.
            if (Volatile.Read(ref _begonnen) - (ende - 1) <= _ring.Length) return true;
        }
    }

    /// <summary>
    /// Kopiert die Meldungen ab <paramref name="abZeitMs"/> (älteste zuerst) nach <paramref name="ziel"/>; liefert die
    /// Anzahl. Passt nicht alles, gehen die jüngsten vor. Ohne Sperre und ohne Allokation.
    /// </summary>
    public int Kopieren(Span<PegelMeldung> ziel, long abZeitMs)
    {
        var laenge = _ring.Length;
        var ende = Volatile.Read(ref _geschrieben);
        var anfang = Math.Max(0, ende - laenge);
        // Von hinten zählen, wie viele im Fenster liegen.
        var n = 0;
        for (var i = ende - 1; i >= anfang && n < ziel.Length; i--)
        {
            if (_ring[i % laenge].ZeitMs < abZeitMs) break;
            n++;
        }
        var erster = ende - n;
        for (var k = 0; k < n; k++) ziel[k] = _ring[(erster + k) % laenge];

        // Was der Schreiber währenddessen überschrieben haben kann, fliegt vorn heraus. Maßgeblich sind die begonnenen
        // Schreibvorgänge, auch der eine, der gerade läuft: Nummer w trifft den Platz von w - Länge.
        Interlocked.MemoryBarrier();
        var gueltigAb = Volatile.Read(ref _begonnen) - laenge;
        if (erster < gueltigAb)
        {
            var weg = (int)Math.Min(n, gueltigAb - erster);
            ziel[weg..n].CopyTo(ziel);
            n -= weg;
        }
        return n;
    }

    /// <summary>Hängt den Verlauf an das Pegel-Ereignis der Maschine; Freigeben hängt ab.</summary>
    public IDisposable Anbinden(IKlangMaschine maschine)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        maschine.Pegel += Hinzufuegen;
        return new Abmeldung(() => maschine.Pegel -= Hinzufuegen);
    }

    /// <summary>
    /// Q43 (0.8.0): Hätte die Meldung <paramref name="m"/> ausgelöst? Ja, wenn die Maschine sendet (Sprechtaste oder offene Sprachaktivierung) oder über
    /// der Linie gesprochen wird (Sprachwahrscheinlichkeit ab <see cref="HinweisSprache"/>); bei stummem Mikro nie, dort misst die Maschine nichts.
    /// Ohne gewählten Kanal schaltet das Funkfeld die Sprachaktivierung der Maschine ab (A10), dann zählt nur das Zweite; dasselbe Maß wie der Satz
    /// "es wird nichts gesendet".
    /// </summary>
    internal static bool WuerdeAusloesen(in PegelMeldung m) =>
        !m.Stumm && (m.Sendet || (m.PegelDbfs > m.SchwelleDb && m.Sprachwahrscheinlichkeit >= HinweisSprache));

    /// <summary>Endliche Zahlen; <see cref="PegelMeldung.WuerdeSenden"/> setzt nur dieser Verlauf, nie die Maschine.</summary>
    private static PegelMeldung Bereinigen(PegelMeldung m) => m with
    {
        PegelDbfs = Endlich(m.PegelDbfs),
        RauschbodenDb = Endlich(m.RauschbodenDb),
        SchwelleDb = Endlich(m.SchwelleDb),
        Sprachwahrscheinlichkeit = float.IsFinite(m.Sprachwahrscheinlichkeit) ? Math.Clamp(m.Sprachwahrscheinlichkeit, 0f, 1f) : 0f,
        WuerdeSenden = false,
    };

    private static float Endlich(float db) => float.IsFinite(db) ? Math.Clamp(db, -120f, 20f) : -120f;

    private sealed class Abmeldung(Action abmelden) : IDisposable
    {
        private Action? _abmelden = abmelden;

        public void Dispose() => Interlocked.Exchange(ref _abmelden, null)?.Invoke();
    }
}

/// <summary>Wohin die eigene Stimme gerade ginge (<see cref="PegelVerlauf.Weg"/>, ui-fix 0.6.2).</summary>
public enum Sendeweg
{
    /// <summary>Mindestens ein gewählter Kanal nimmt die Stimme an (oder die Melde-Taste spricht ins Meldenetz).</summary>
    Offen,

    /// <summary>Kein gewählter Kanal oder nur Ruhe: Es geht nichts hinaus.</summary>
    Ruhe,

    /// <summary>Die Einsatzleitung hat alle gewählten Kanäle stummgeschaltet.</summary>
    Leitung,

    /// <summary>Nicht verbunden.</summary>
    Getrennt,

    /// <summary>
    /// Seit 0.10.0: Ein gewählter Kanal sendet nur mit der Sprechtaste (die Sprachaktivierung ist aus), aber es ist keine Sprechtaste belegt;
    /// was du sagst, geht nirgends hin. Wird nicht gespeichert.
    /// </summary>
    OhneSprechtaste,
}

/// <summary>
/// Jüngster Empfangspegel je Netz, für die Kacheln des Funkfelds. Geschrieben aus dem Thread der Klangmaschine, gelesen
/// im Bildtakt; ohne Sperre und ohne Allokation (ein <c>ConcurrentDictionary</c> legte bei jeder Aktualisierung einen
/// neuen Knoten an). Offene Adressierung mit <see cref="Plaetze"/> Plätzen: Ein Netz belegt seinen Platz einmal per
/// CompareExchange, danach wird nur noch ein 64-Bit-Wert (Pegel und Zeit) atomar ersetzt. Sind alle Plätze belegt,
/// fällt die Anzeige für weitere Netze weg; es ist nur eine Anzeige.
/// </summary>
public sealed class EmpfangsPegelTafel
{
    public const int Plaetze = 64;

    private readonly int[] _netz = new int[Plaetze];
    private readonly long[] _wert = new long[Plaetze];

    /// <summary>Merkt <paramref name="db"/> für <paramref name="netzId"/> zur Zeit <paramref name="ms"/>.</summary>
    public void Setzen(ushort netzId, float db, long ms)
    {
        var schluessel = netzId + 1;
        var wert = ((long)BitConverter.SingleToInt32Bits(db) << 32) | ((uint)ms | 1u);
        var start = netzId % Plaetze;
        for (var i = 0; i < Plaetze; i++)
        {
            var p = (start + i) % Plaetze;
            var k = Volatile.Read(ref _netz[p]);
            if (k == 0)
            {
                var alt = Interlocked.CompareExchange(ref _netz[p], schluessel, 0);
                k = alt == 0 ? schluessel : alt;
            }
            if (k != schluessel) continue;
            Volatile.Write(ref _wert[p], wert);
            return;
        }
    }

    /// <summary>Pegel des Netzes, wenn er nicht älter als <paramref name="hoechstensMs"/> ist.</summary>
    public bool Lesen(ushort netzId, long jetztMs, long hoechstensMs, out float db)
    {
        var schluessel = netzId + 1;
        var start = netzId % Plaetze;
        for (var i = 0; i < Plaetze; i++)
        {
            var p = (start + i) % Plaetze;
            var k = Volatile.Read(ref _netz[p]);
            if (k == 0) break;
            if (k != schluessel) continue;
            var wert = Volatile.Read(ref _wert[p]);
            if (wert == 0) break;
            // Mit Vorzeichen: Das Markierungsbit und Uhren verschiedener Threads können "jetzt" knapp vor die Meldung legen.
            var alter = unchecked((int)((uint)jetztMs - (uint)wert));
            db = BitConverter.Int32BitsToSingle((int)(wert >> 32));
            return alter <= hoechstensMs;
        }
        db = 0;
        return false;
    }
}

/// <summary>
/// Pegel dessen, was gerade je Funknetz ankommt (für die Pegelanzeige der Funkfeld-Kacheln, A18). Im Betrieb bildet die
/// <see cref="KlangBruecke"/> die Empfangsanzeige der Maschine (je Sprecher und Mischer-Netz) darauf ab, in der
/// Vorführung speist die <see cref="KlangMaschinenAttrappe"/> ihn ein. Fehlt er, zeigt die Kachel nur, dass jemand spricht.
/// </summary>
public interface IEmpfangsPegelQuelle
{
    /// <summary>Netz-ID (Funknetz) und Pegel in dBFS, aus einem Nicht-UI-Thread.</summary>
    event Action<ushort, float>? NetzPegel;
}
