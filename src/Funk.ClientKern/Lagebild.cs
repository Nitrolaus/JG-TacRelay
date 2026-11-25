// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>
/// Was der Client vom Server weiß: Welcome, NetzListe, eigene Mitgliedschaften, Praesenz und Pegel. Die Teile sind
/// unveränderliche Objekte und werden als Ganzes ausgetauscht, lesen geht also von jedem Thread ohne Sperre. Geschrieben
/// wird nur aus der Leseschleife der Sitzung.
/// </summary>
public sealed class Lagebild
{
    private volatile Welcome? _welcome;
    private volatile NetzListe? _netzListe;
    private volatile Mitgliedschaften? _mitgliedschaften;
    private volatile PegelVoreinstellungen? _pegel;
    private volatile Belegung? _belegung;
    private volatile PhaseGeschaltet? _phase;
    private volatile Meldestand? _meldestand;
    private volatile BereitStand? _bereitStand;
    private ImmutableDictionary<uint, PraesenzEintrag> _praesenz = ImmutableDictionary<uint, PraesenzEintrag>.Empty;
    private ImmutableDictionary<ushort, Netz> _netze = ImmutableDictionary<ushort, Netz>.Empty;
    private long _zustandsVersion;
    private long _zeitVersatzMs;
    private volatile bool _vollstaendig;

    /// <summary>Letztes Welcome{FREIGEGEBEN} (Sitzung, Konto, UDP-Port, Grenzen, Profil).</summary>
    public Welcome? Welcome => _welcome;

    /// <summary>
    /// Letzte Belegung (0x4003, Stufe 2): je Platz BELEGT und RESERVIERT, wie die Kapazität des Servers zählt (K1). Fehlt bei
    /// einem Server vor 0.3.0; dann zählt die Oberfläche aus der Praesenz.
    /// </summary>
    public Belegung? Belegung => _belegung;

    /// <summary>
    /// Die laufende Einsatzphase (0x4004 PhaseGeschaltet, 6.14, seit 0.5.0): kommt nach Welcome, sobald die Flotte Phasen hat, und bei
    /// jedem Wechsel. Ohne Push (keine Phasen, älterer Server) <c>null</c>; dann gilt die Grundstellung. Ein neues Welcome löscht sie.
    /// </summary>
    public PhaseGeschaltet? Phase => _phase;

    /// <summary>
    /// Der laufende Stand des Meldesystems (0x4005 Meldestand, seit 0.12.0): Zustand, Ampel, Countdown, offene Bereit-Abfrage, eine
    /// bleibende Textmeldung. Ohne <c>befehl</c> und ohne Textmeldung mit 8 s: Beides ist ein Ereignis, kein Stand; die Oberfläche
    /// bekommt es über den Push selbst. Nach Welcome
    /// schickt der Server den Stand neu, ein neues Welcome löscht ihn deshalb; ohne Push (nichts läuft, älterer Server) <c>null</c>.
    /// </summary>
    public Meldestand? Meldestand => _meldestand;

    /// <summary>
    /// "n von m bereit" der letzten Bereit-Abfrage (0x4006, seit 0.12.0), nur wer senden darf bekommt ihn; nach dem Ende mit
    /// <c>offen</c> false. Ein neues Welcome löscht ihn.
    /// </summary>
    public BereitStand? BereitStand => _bereitStand;

    /// <summary>
    /// Wie weit die Uhr des Servers der eigenen voraus ist, in ms (<c>Welcome.serverZeit</c> gegen die eigene Uhr beim Empfang).
    /// Fristen wie das Ende eines Auftrags rechnet die Anzeige damit, damit eine falsch gehende PC-Uhr nicht "noch 2 h" zeigt.
    /// </summary>
    public long ZeitVersatzMs => Interlocked.Read(ref _zeitVersatzMs);

    public uint SitzungsId => _welcome?.SitzungsId ?? 0;

    public uint KontoId => _welcome?.Konto?.Id ?? 0;

    public NetzListe? NetzListe => _netzListe;

    public Mitgliedschaften? Mitgliedschaften => _mitgliedschaften;

    public PegelVoreinstellungen? Pegel => _pegel;

    /// <summary>Praesenz je Konto (voll ersetzt, Delta ergänzt, <c>entfernt</c> gelöscht).</summary>
    public ImmutableDictionary<uint, PraesenzEintrag> Praesenz => Volatile.Read(ref _praesenz);

    public long ZustandsVersion => Interlocked.Read(ref _zustandsVersion);

    /// <summary>Nach dem Push <c>Zustand</c> in SYNC ist der Stand vollständig (4.5).</summary>
    public bool Vollstaendig => _vollstaendig;

    /// <summary>
    /// Ob das eigene Konto das Recht hat: nach der letzten Änderung (<c>Mitgliedschaften.konto.rechte</c>, F3), sonst aus
    /// Welcome, dazu die Rechte eines laufenden Auftrags (<c>konto.beauftragung</c>, 6.12; Beginn und Ende meldet der Server
    /// mit neuen Mitgliedschaften). Verwaltungsrechte wirken beim Server davon nur in einer Admin-Sitzung (3.9). Solange
    /// <c>konto.alsFunkerBis</c> dasteht ("Heute als Funker", 6.15, seit 0.5.0), ruhen die Rechte der Einsatzleitung; der Server
    /// schickt das Feld nur, solange der Modus läuft, und meldet sein Ende mit neuen Mitgliedschaften.
    /// </summary>
    public bool HatRecht(Recht recht)
    {
        var funker = _mitgliedschaften?.Konto is { } m ? m.AlsFunkerBis is not null : _welcome?.Konto?.AlsFunkerBis is not null;
        if (funker && recht is Recht.GlobalSenden or Recht.Durchsage or Recht.DurchsageAlle or Recht.MehrfachSenden or Recht.Zuweisen or Recht.SprechanzeigeAlle)
            return false;
        if ((_mitgliedschaften?.Konto?.Rechte ?? _welcome?.Konto?.Rechte)?.Contains(recht) == true) return true;
        var auftrag = _mitgliedschaften?.Konto is { } k ? k.Beauftragung : _welcome?.Konto?.Beauftragung;
        return auftrag is { Aktiv: true } && auftrag.Rechte.Contains(recht);
    }

    /// <summary>Eigene Kontoangaben (Namen, Admin-Zustand, Rollen, Zeichen), zuletzt mit Mitgliedschaften gekommen.</summary>
    public KontoStand? Konto => _mitgliedschaften?.Konto;

    /// <summary>Admin-Zustand des eigenen Kontos (3.9): Menü "Verwaltung" nur mit VORGESEHEN, EINRICHTUNG oder AKTIV.</summary>
    public AdminStatus? Admin => _mitgliedschaften?.Konto is { } k ? k.Admin : _welcome?.Konto?.Admin;

    /// <summary>
    /// Netz aus der NetzListe oder <c>null</c>; für eine Kanal-ID (6.16, seit 0.6.0) ihr Kanalnetz, wenn der Ch. in seiner Liste steht.
    /// So ist ein gestempelter Kanal kein unbekanntes Netz (C8).
    /// </summary>
    public Netz? NetzVon(ushort id)
    {
        var netze = Volatile.Read(ref _netze);
        if (netze.TryGetValue(id, out var netz)) return netz;
        return Kanalrechnung.Zerlegen(id, out var n, out var ch) && netze.TryGetValue(n, out var kanalnetz) && kanalnetz.Kanaele is { } liste && ch < liste.Count
            ? kanalnetz
            : null;
    }

    /// <summary>Eigenes Feld mit dieser Nummer oder <c>null</c>.</summary>
    public Mitgliedschaft? FeldVon(byte feld)
    {
        var m = _mitgliedschaften;
        if (m is null) return null;
        foreach (var f in m.Felder)
            if (f.Feld == feld) return f;
        return null;
    }

    /// <summary>Eigenes Feld mit diesem Netz oder <c>null</c>.</summary>
    public Mitgliedschaft? FeldMitNetz(ushort netzId)
    {
        var m = _mitgliedschaften;
        if (m is null) return null;
        foreach (var f in m.Felder)
            if (f.NetzId == netzId) return f;
        return null;
    }

    /// <summary>
    /// Ob das Konto gerade in <paramref name="netzId"/> stummgeschaltet ist (0 = alle, 1 = Global, sonst das Netz, seit 0.6.0 auch
    /// das Kanalnetz eines Kanals), nach der Uhr <paramref name="jetztUnixMs"/>.
    /// </summary>
    public bool IstStumm(ushort netzId, long jetztUnixMs)
    {
        var m = _mitgliedschaften;
        if (m is null) return false;
        var kanalnetz = Kanalrechnung.Zerlegen(netzId, out var n, out _) ? n : (ushort)0;
        foreach (var s in m.Stumm)
        {
            if (s.Bis <= jetztUnixMs) continue;
            if (s.NetzId == 0 || s.NetzId == netzId || (kanalnetz != 0 && s.NetzId == kanalnetz)) return true;
        }
        return false;
    }

    /// <summary>
    /// Ob eine Kanalsperre (6.17, <c>Mitgliedschaften.gesperrt</c>) das Konto in <paramref name="netzId"/> trifft: auf genau diese ID
    /// oder auf das ganze Kanalnetz eines Kanals. Eine Ruhe ist nie gesperrt.
    /// </summary>
    public static bool Gesperrt(Mitgliedschaften m, ushort netzId)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (m.Gesperrt is not { Count: > 0 } sperren || Kanalrechnung.IstRuhe(netzId)) return false;
        var kanalnetz = Kanalrechnung.Zerlegen(netzId, out var n, out _) ? n : (ushort)0;
        foreach (var s in sperren)
            if (s.NetzId == netzId || (kanalnetz != 0 && s.NetzId == kanalnetz)) return true;
        return false;
    }

    /// <summary>Ein Feld mit diesem Netz darf senden, soweit es um Kanäle geht: keine Ruhe (6.16), keine Kanalsperre (6.17).</summary>
    public static bool KanalSendbar(Mitgliedschaften m, ushort netzId) => !Kanalrechnung.IstRuhe(netzId) && !Gesperrt(m, netzId);

    internal void Zuruecksetzen()
    {
        _welcome = null;
        _vollstaendig = false;
        // Ohne Verbindung weiß niemand, was läuft; das Overlay zeigt dann nichts mehr (0.12.0).
        _meldestand = null;
        _bereitStand = null;
    }

    internal void WelcomeSetzen(Welcome w)
    {
        _welcome = w;
        _phase = null;
        _meldestand = null;
        _bereitStand = null;
        if (w.ZustandsVersion is { } v) VersionMerken(v);
        if (w.ServerZeit is { } s) Interlocked.Exchange(ref _zeitVersatzMs, s - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    internal void BelegungSetzen(Belegung b)
    {
        _belegung = b;
        VersionMerken(b.ZustandsVersion);
    }

    internal void NetzListeSetzen(NetzListe n)
    {
        _netzListe = n;
        Volatile.Write(ref _netze, n.Netze.ToImmutableDictionary(x => x.Id));
    }

    internal void MitgliedschaftenSetzen(Mitgliedschaften m)
    {
        _mitgliedschaften = m;
        VersionMerken(m.ZustandsVersion);
    }

    internal void PegelSetzen(PegelVoreinstellungen p) => _pegel = p;

    internal void PhaseSetzen(PhaseGeschaltet p) => _phase = p;

    internal void MeldestandSetzen(Meldestand m) => _meldestand = m with { Befehl = null, Text = Textmeldungen.Bleibend(m.Text) };

    internal void BereitStandSetzen(BereitStand b) => _bereitStand = b;

    internal void PraesenzAnwenden(Praesenz p)
    {
        var alt = Volatile.Read(ref _praesenz);
        var neu = p.Voll ? ImmutableDictionary.CreateBuilder<uint, PraesenzEintrag>() : alt.ToBuilder();
        foreach (var e in p.Eintraege) neu[e.KontoId] = e;
        foreach (var k in p.Entfernt) neu.Remove(k);
        Volatile.Write(ref _praesenz, neu.ToImmutable());
        VersionMerken(p.ZustandsVersion);
    }

    internal void Vollstaendigkeit(long zustandsVersion)
    {
        VersionMerken(zustandsVersion);
        _vollstaendig = true;
    }

    internal void VersionMerken(long v)
    {
        long alt;
        do
        {
            alt = Interlocked.Read(ref _zustandsVersion);
            if (v <= alt) return;
        }
        while (Interlocked.CompareExchange(ref _zustandsVersion, v, alt) != alt);
    }
}
