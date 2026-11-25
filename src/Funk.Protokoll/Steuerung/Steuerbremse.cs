// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Protokoll.Schutz;

namespace Funk.Protokoll.Steuerung;

/// <summary>Rate eines Token-Eimers: Marken je Sekunde und Stoß (Burst).</summary>
public readonly record struct Rate(double ProSekunde, double Stoss);

/// <summary>Zeilen der Tabelle 4.8, soweit sie Steuerrahmen und Bodies betreffen.</summary>
public enum Ratengruppe : byte
{
    /// <summary>Ohne eigene Grenze (nur die Gesamtgrenzen).</summary>
    Keine,
    /// <summary>SendeauswahlSetzen, HoerenSetzen, PegelSetzen, SprechanzeigeAbo: je Art 4/s, Stoß 8, Überschuss wird zusammengefasst.</summary>
    Zustandsmeldung,
    /// <summary>FeldSetzen, SatzAnwenden, RolleWaehlen (seit 0.6.0): zusammen 5/s, Stoß 10.</summary>
    FeldUndSatz,
    /// <summary>EinsteigenRolleSchiff, Verschieben, Stummschalten, PlaetzeFreigeben, KanalSperre (seit 0.6.0): 10/s, Stoß 20.</summary>
    Umstellung,
    /// <summary>AufstellungAnwenden: 2/s, Stoß 5.</summary>
    AufstellungAnwenden,
    /// <summary>AufstellungSpeichern/Loeschen, Verwaltung, KontoVerwaltung, RegelTabelle, Geräte-Nachrichten: 1/s, Stoß 5.</summary>
    Verwaltung,
    /// <summary>Ablage: 1/s, Stoß 5.</summary>
    Ablage,
    /// <summary>RaidHelperImport und RaidHelperEvents zusammen: 1 je 10 s.</summary>
    RaidHelper,
    /// <summary>Zustand (Anfrage): 1 je 5 s, Stoß 2.</summary>
    ZustandAnfrage,
    /// <summary>Ping: 2/s.</summary>
    Ping,
    /// <summary>EmpfangsBericht: 1 je 2 s.</summary>
    EmpfangsBericht,
    /// <summary>KeyRotation: 1 je 10 s.</summary>
    KeyRotation,
    /// <summary>ProfilSetzen, Lieblingsrollen: 1/s, Stoß 2.</summary>
    Profil,
    /// <summary>Mitgliedskarte: 5/s, Stoß 10.</summary>
    Mitgliedskarte,
    /// <summary>AdminAnmeldung, AdminBestaetigen, AdminAbmelden, AdminEinrichtung: 1/s, Stoß 3 (3.9).</summary>
    Admin,

    /// <summary>
    /// AdminNonce: 1/s, Stoß 3, eigener Eimer (Offener Punkt 30). Jede Anmeldung braucht erst eine Nonce; teilten sich beide
    /// einen Eimer, scheiterte eine Oberfläche, die nach einem Tippfehler gleich wieder anmeldet, an der zweiten Nachricht.
    /// </summary>
    AdminNonce,

    /// <summary>KontenVerzeichnis: 1 je 5 s, Stoß 2 (N4).</summary>
    Kontenverzeichnis,

    /// <summary>Funkermodus: 1/s, Stoß 2 (6.15, seit 0.5.0).</summary>
    Funkermodus,

    /// <summary>PhaseSchalten: 1 je 2 s, Stoß 2 (6.14, seit 0.5.0). Eine Flut von Phasenwechseln endet bei GRENZE.</summary>
    Phase,

    /// <summary>
    /// MeldungSenden: 1 je 2 s, Stoß 1 (Meldesystem, seit 0.12.0, M19: je Sender höchstens eine Meldung in 2 s). Eine Sitzung je
    /// Konto, also je Sender. Wer früher sendet, bekommt GRENZE; der Client zählt die 2 s ab der Antwort auf die vorige.
    /// </summary>
    Meldung,

    /// <summary>BereitMelden: 1/s, Stoß 2 (seit 0.12.0); ein zweites Bereit ändert ohnehin nichts.</summary>
    Bereit,
}

/// <summary>Startwerte der Ratengrenzen aus 4.8.</summary>
public static class Ratengrenzen
{
    /// <summary>Steuerrahmen gesamt je Sitzung (ohne SpracheTunnel): 30/s, Stoß 60.</summary>
    public static Rate RahmenGesamt { get; } = new(30, 60);

    /// <summary>Steuerbytes gesamt je Sitzung (ohne SpracheTunnel): 256 KiB/s, Stoß 1 MiB.</summary>
    public static Rate BytesGesamt { get; } = new(256 * 1024, 1024 * 1024);

    /// <summary>Mehr als so viele Verstöße in <see cref="VerstossFensterMs"/> trennen und sperren das Gerät.</summary>
    public const int VerstoesseMax = 50;

    /// <summary>Fenster für die Verstoßzählung.</summary>
    public const long VerstossFensterMs = 60_000;

    /// <summary>Gerätesperre nach zu vielen Verstößen (Fingerabdruck, nicht IP).</summary>
    public const long GeraetesperreMs = 60_000;

    /// <summary>Gruppe eines Nachrichtentyps (nur Client-Anfragen und -Pushes haben Gruppen).</summary>
    public static Ratengruppe Gruppe(Nachrichtentyp typ) => typ switch
    {
        Nachrichtentyp.SendeauswahlSetzen or Nachrichtentyp.HoerenSetzen or Nachrichtentyp.PegelSetzen
            or Nachrichtentyp.SprechanzeigeAbo => Ratengruppe.Zustandsmeldung,
        Nachrichtentyp.FeldSetzen or Nachrichtentyp.SatzAnwenden or Nachrichtentyp.RolleWaehlen => Ratengruppe.FeldUndSatz,
        Nachrichtentyp.EinsteigenRolleSchiff or Nachrichtentyp.Verschieben or Nachrichtentyp.Stummschalten
            or Nachrichtentyp.PlaetzeFreigeben or Nachrichtentyp.KanalSperre => Ratengruppe.Umstellung,
        Nachrichtentyp.AufstellungAnwenden => Ratengruppe.AufstellungAnwenden,
        Nachrichtentyp.AufstellungSpeichern or Nachrichtentyp.AufstellungLoeschen or Nachrichtentyp.Verwaltung
            or Nachrichtentyp.KontoVerwaltung or Nachrichtentyp.RegelTabelle or Nachrichtentyp.GeraeteListe
            or Nachrichtentyp.GeraetEntscheiden or Nachrichtentyp.GeraetEntfernen or Nachrichtentyp.Journal
            or Nachrichtentyp.Rollenkatalog or Nachrichtentyp.Mitglied or Nachrichtentyp.Untergilde
            or Nachrichtentyp.AntraegeAblehnen or Nachrichtentyp.Beauftragung or Nachrichtentyp.Abzeichen => Ratengruppe.Verwaltung,
        Nachrichtentyp.KontenVerzeichnis => Ratengruppe.Kontenverzeichnis,
        Nachrichtentyp.Funkermodus => Ratengruppe.Funkermodus,
        Nachrichtentyp.PhaseSchalten => Ratengruppe.Phase,
        Nachrichtentyp.MeldungSenden => Ratengruppe.Meldung,
        Nachrichtentyp.BereitMelden => Ratengruppe.Bereit,
        Nachrichtentyp.ProfilSetzen or Nachrichtentyp.Lieblingsrollen => Ratengruppe.Profil,
        Nachrichtentyp.Mitgliedskarte => Ratengruppe.Mitgliedskarte,
        Nachrichtentyp.AdminNonce => Ratengruppe.AdminNonce,
        Nachrichtentyp.AdminAnmeldung or Nachrichtentyp.AdminBestaetigen
            or Nachrichtentyp.AdminAbmelden or Nachrichtentyp.AdminEinrichtung => Ratengruppe.Admin,
        Nachrichtentyp.Ablage => Ratengruppe.Ablage,
        Nachrichtentyp.RaidHelperImport or Nachrichtentyp.RaidHelperEvents => Ratengruppe.RaidHelper,
        Nachrichtentyp.Zustand => Ratengruppe.ZustandAnfrage,
        Nachrichtentyp.Ping => Ratengruppe.Ping,
        Nachrichtentyp.EmpfangsBericht => Ratengruppe.EmpfangsBericht,
        Nachrichtentyp.KeyRotation => Ratengruppe.KeyRotation,
        _ => Ratengruppe.Keine,
    };

    /// <summary>
    /// Rate einer Gruppe. Wo 4.8 keinen Stoß nennt (Ping, EmpfangsBericht, KeyRotation, RaidHelper), gilt hier
    /// der kleinste sinnvolle: 2 bei Ping, sonst 1.
    /// </summary>
    public static Rate Von(Ratengruppe gruppe) => gruppe switch
    {
        Ratengruppe.Zustandsmeldung => new(4, 8),
        Ratengruppe.FeldUndSatz => new(5, 10),
        Ratengruppe.Umstellung => new(10, 20),
        Ratengruppe.AufstellungAnwenden => new(2, 5),
        Ratengruppe.Verwaltung or Ratengruppe.Ablage => new(1, 5),
        Ratengruppe.RaidHelper => new(0.1, 1),
        Ratengruppe.ZustandAnfrage or Ratengruppe.Kontenverzeichnis => new(0.2, 2),
        Ratengruppe.Ping => new(2, 2),
        Ratengruppe.EmpfangsBericht => new(0.5, 1),
        Ratengruppe.KeyRotation => new(0.1, 1),
        Ratengruppe.Profil or Ratengruppe.Funkermodus or Ratengruppe.Bereit => new(1, 2),
        Ratengruppe.Phase => new(0.5, 2),
        Ratengruppe.Meldung => new(1000.0 / Fristen.MeldungAbstandMs, 1),
        Ratengruppe.Mitgliedskarte => new(5, 10),
        Ratengruppe.Admin or Ratengruppe.AdminNonce => new(1, 3),
        _ => new(double.PositiveInfinity, double.PositiveInfinity),
    };
}

/// <summary>Urteil der <see cref="Steuerbremse"/> über einen eingehenden Rahmen.</summary>
public enum Bremsurteil : byte
{
    /// <summary>Innerhalb aller Grenzen.</summary>
    Annehmen,
    /// <summary>Zustandsmeldung über ihrer Grenze: kein Verstoß, in den wartenden Platz legen (4.8, <c>UEBERHOLT</c>).</summary>
    Zusammenfassen,
    /// <summary>Verstoß: Nachricht verwerfen, <c>Fehler{GRENZE}</c> senden, wenn <see cref="Bremsergebnis.GrenzeMelden"/>.</summary>
    Grenze,
    /// <summary>Mehr als 50 Verstöße in 60 s: trennen und dieses Gerät 60 s sperren.</summary>
    Trennen,
}

/// <summary>
/// Ergebnis der Bremse samt Hinweis, ob <c>Fehler{GRENZE}</c> jetzt zu senden ist. Bei einer Anfrage ist das immer
/// der Fall, als Antwort mit ihrer Anfrage-Nr (4.2: jede Anfrage bekommt genau eine Antwort; sonst bliebe sie beim
/// Client für immer offen und belegte einen der 32 Plätze). Die Grenze "höchstens 1 Meldung/s" aus 4.8 gilt für
/// verworfene Pushes, die als <c>Fehler</c>-Push gemeldet werden.
/// </summary>
public readonly record struct Bremsergebnis(Bremsurteil Urteil, bool GrenzeMelden);

/// <summary>
/// Ratengrenzen eines Steuerkanals je Sitzung nach 4.8 mit Token-Eimern und simulierbarer Uhr (ms).
/// SpracheTunnel zählt nicht mit (dafür gelten die Sprachgrenzen). Nicht threadsicher.
/// </summary>
public sealed class Steuerbremse
{
    private readonly TokenEimer _rahmen;
    private readonly TokenEimer _bytes;
    private readonly Dictionary<int, TokenEimer> _gruppen = [];
    private readonly HashSet<ushort> _wartend = [];
    private readonly Queue<long> _verstoesse = new();
    private long? _letzteMeldungMs;

    public Steuerbremse(long jetztMs)
    {
        var t = Ticks(jetztMs);
        _rahmen = new TokenEimer(Ratengrenzen.RahmenGesamt.Stoss, Ratengrenzen.RahmenGesamt.ProSekunde, t);
        _bytes = new TokenEimer(Ratengrenzen.BytesGesamt.Stoss, Ratengrenzen.BytesGesamt.ProSekunde, t);
    }

    /// <summary>
    /// Bewertet einen eingehenden Rahmen des Clients. Bei <see cref="Bremsurteil.Zusammenfassen"/> legt der
    /// Aufrufer die Meldung in den wartenden Platz (<see cref="Zusammenfasser{T}"/>); solange dort eine wartet,
    /// geht jede weitere derselben Art ebenfalls dorthin, damit immer der letzte Stand gilt.
    /// </summary>
    public Bremsergebnis Pruefen(in RahmenKopf kopf, long jetztMs)
    {
        if (kopf.Typ == (ushort)Nachrichtentyp.SpracheTunnel) return new Bremsergebnis(Bremsurteil.Annehmen, false);
        var t = Ticks(jetztMs);
        var ok = _rahmen.Nehmen(t) & _bytes.Nehmen(t, kopf.GesamtLaenge);
        if (ok && kopf.Art != Nachrichtenart.Antwort)
        {
            var gruppe = Ratengrenzen.Gruppe(kopf.Nachricht);
            if (gruppe == Ratengruppe.Zustandsmeldung)
            {
                if (_wartend.Contains(kopf.Typ) || !Eimer(gruppe, kopf.Typ, t).Nehmen(t))
                {
                    _wartend.Add(kopf.Typ);
                    return new Bremsergebnis(Bremsurteil.Zusammenfassen, false);
                }
            }
            else if (gruppe != Ratengruppe.Keine && !Eimer(gruppe, kopf.Typ, t).Nehmen(t))
            {
                ok = false;
            }
        }
        return ok ? new Bremsergebnis(Bremsurteil.Annehmen, false) : Verstoss(jetztMs, kopf.Art == Nachrichtenart.Anfrage);
    }

    /// <summary>Ob die wartende Zustandsmeldung dieser Art jetzt angewendet werden darf; verbraucht dann eine Marke.</summary>
    public bool WartendeFreigeben(ushort typ, long jetztMs)
    {
        if (!_wartend.Contains(typ)) return false;
        var t = Ticks(jetztMs);
        if (!Eimer(Ratengruppe.Zustandsmeldung, typ, t).Nehmen(t)) return false;
        _wartend.Remove(typ);
        return true;
    }

    private Bremsergebnis Verstoss(long jetztMs, bool anfrage)
    {
        while (_verstoesse.Count > 0 && jetztMs - _verstoesse.Peek() >= Ratengrenzen.VerstossFensterMs) _verstoesse.Dequeue();
        _verstoesse.Enqueue(jetztMs);
        if (_verstoesse.Count > Ratengrenzen.VerstoesseMax) return new Bremsergebnis(Bremsurteil.Trennen, false);
        // Eine Anfrage wird immer beantwortet (4.2); nur Pushes unterliegen der Meldegrenze von 1/s. Die Zahl der
        // Antworten bleibt klein, denn nach 50 Verstößen in 60 s wird getrennt.
        var melden = anfrage || _letzteMeldungMs is not { } letzte || jetztMs - letzte >= 1000;
        if (melden) _letzteMeldungMs = jetztMs;
        return new Bremsergebnis(Bremsurteil.Grenze, melden);
    }

    private TokenEimer Eimer(Ratengruppe gruppe, ushort typ, long ticks)
    {
        // Zustandsmeldungen je Art, die übrigen Zeilen teilen sich einen Eimer je Zeile.
        var schluessel = gruppe == Ratengruppe.Zustandsmeldung ? 0x10000 | typ : (int)gruppe;
        if (!_gruppen.TryGetValue(schluessel, out var eimer))
        {
            var r = Ratengrenzen.Von(gruppe);
            eimer = new TokenEimer(r.Stoss, r.ProSekunde, ticks);
            _gruppen[schluessel] = eimer;
        }
        return eimer;
    }

    /// <summary>Millisekunden in Stopwatch-Ticks, ohne Überlauf bei großen Werten.</summary>
    internal static long Ticks(long ms) => ms / 1000 * Stopwatch.Frequency + ms % 1000 * Stopwatch.Frequency / 1000;
}

/// <summary>
/// Wartender Platz je Sitzung und Art für Zustandsmeldungen (4.8): Eine neuere Anfrage ersetzt die wartende; die
/// ersetzte bekommt <c>Fehler{UEBERHOLT}</c>. Die wartende wird angewendet, sobald ihr Eimer es erlaubt.
/// </summary>
public sealed class Zusammenfasser<T> where T : notnull
{
    private readonly Dictionary<ushort, (uint Nr, T Wert)> _wartend = [];
    private readonly Steuerbremse _bremse;

    public Zusammenfasser(Steuerbremse bremse)
    {
        ArgumentNullException.ThrowIfNull(bremse);
        _bremse = bremse;
    }

    /// <summary>Zahl wartender Einträge.</summary>
    public int Wartend => _wartend.Count;

    /// <summary>Arten mit wartender Meldung.</summary>
    public IEnumerable<ushort> WartendeArten => _wartend.Keys;

    /// <summary>
    /// Legt eine Zustandsmeldung ab, nachdem die Bremse <see cref="Bremsurteil.Zusammenfassen"/> geurteilt hat.
    /// Gibt die ersetzte zurück (sie bekommt <c>Fehler{UEBERHOLT}</c>), sonst <c>null</c>.
    /// </summary>
    public (uint Nr, T Wert)? Einreihen(ushort typ, uint anfrageNr, T wert)
    {
        (uint, T)? alt = _wartend.TryGetValue(typ, out var w) ? w : null;
        _wartend[typ] = (anfrageNr, wert);
        return alt;
    }

    /// <summary>Gibt die wartende Meldung einer Art heraus, wenn die Bremse es jetzt erlaubt.</summary>
    public bool VersucheEntnehmen(ushort typ, long jetztMs, out uint anfrageNr, out T? wert)
    {
        anfrageNr = 0;
        wert = default;
        if (!_wartend.TryGetValue(typ, out var w) || !_bremse.WartendeFreigeben(typ, jetztMs)) return false;
        _wartend.Remove(typ);
        (anfrageNr, wert) = w;
        return true;
    }
}
