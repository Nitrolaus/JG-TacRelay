// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Security.Cryptography;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>
/// Senden nach 5.6 und den Client-Regeln C1, C2 und C4 (6.11). Je Sprechphase sind Zielart, Ziele und VA konstant
/// (C2); ein Wechsel beendet die Phase mit ENDE an die alten Ziele, die neue Phase beginnt erst, wenn die Auswahl 250 ms
/// stabil ist, ohne Pre-roll. Die Sendeauswahl steht als Feldnummern und wird erst beim Beginn über die aktuellen
/// Mitgliedschaften in Netz-IDs umgesetzt (C1). Fällt durch einen Mitgliedschaften-Push ein Ziel weg, endet die Phase
/// und setzt ohne Unterbrechung mit den verbleibenden Zielen fort; ein Netzwechsel hinter einem Feld wirkt erst nach
/// erneutem Drücken (C4). ENDE geht dreimal im 20-ms-Abstand (5.6). Threadsicher; <see cref="Senden"/> blockiert nie.
/// </summary>
public sealed class Sprechkanal
{
    /// <summary>C2: Die Auswahl muss so lange stabil sein, bevor die neue Phase beginnt.</summary>
    public const long StabilMs = 250;

    private readonly FunkClient _client;
    private readonly Lock _sperre = new();
    private SprechphasenGeber? _geber;
    private Sprachweg? _weg;
    private Sendeziel? _ziel;
    private bool _va;
    private Sendeziel? _ausstehend;
    private long _ausstehendSeitMs;
    private ImmutableArray<byte> _auswahlFelder = [];
    private bool _auswahlGlobal;

    internal Sprechkanal(FunkClient client) => _client = client;

    /// <summary>C4: Sendenetz geändert durch eine Umstellung (auf dem Thread der Leseschleife).</summary>
    public event EventHandler<SendenetzGeaendert>? SendenetzGeaendert;

    /// <summary>Ziel der laufenden Phase oder <c>null</c>.</summary>
    public Sendeziel? Ziel
    {
        get
        {
            lock (_sperre) return _ziel;
        }
    }

    /// <summary>Eine Phase ist offen oder wartet auf 250 ms Stabilität (Taste gedrückt).</summary>
    public bool Aktiv
    {
        get
        {
            lock (_sperre) return _ziel is not null || _ausstehend is not null;
        }
    }

    /// <summary>Gesendete Audio-Frames seit dem Start (für Messungen).</summary>
    public long GesendeteFrames => Interlocked.Read(ref _frames);

    private long _frames;

    /// <summary>Sendeauswahl als Feldnummern plus Global (C1), wie sie der Client hält.</summary>
    public (ImmutableArray<byte> Felder, bool Global) Auswahl
    {
        get
        {
            lock (_sperre) return (_auswahlFelder, _auswahlGlobal);
        }
    }

    internal void AuswahlSetzen(IReadOnlyList<byte> felder, bool global)
    {
        lock (_sperre)
        {
            _auswahlFelder = [.. felder.Distinct().Order()];
            _auswahlGlobal = global;
        }
    }

    /// <summary>Neue Sitzung: neuer Phasengeber (zufälliger Startwert je Sitzung, 5.6), alte Phase ist vorbei.</summary>
    internal void SitzungBeginnt(Sprachweg weg)
    {
        lock (_sperre)
        {
            _weg = weg;
            _geber = new SprechphasenGeber(Monouhr.Takte(), (ushort)RandomNumberGenerator.GetInt32(0, 65536));
            _ziel = null;
            _ausstehend = null;
        }
    }

    internal void SitzungEndet()
    {
        lock (_sperre)
        {
            _weg = null;
            _geber = null;
            _ziel = null;
            _ausstehend = null;
        }
    }

    /// <summary>
    /// Beginnt eine Sprechphase an <paramref name="ziel"/>. <paramref name="sprachaktivierung"/> setzt VA (Klick beim
    /// Empfänger, "nur Taste"). <paramref name="aufnahmeTakte"/>: Aufnahmezeitpunkt des ersten Frames (Pre-roll: des
    /// ersten Vorlauf-Frames). <c>false</c> ohne Sitzung oder Schlüssel.
    /// </summary>
    public bool Beginnen(Sendeziel ziel, bool sprachaktivierung = false, long? aufnahmeTakte = null)
    {
        ArgumentNullException.ThrowIfNull(ziel);
        lock (_sperre)
        {
            if (_geber is null || _weg is null || !_weg.HatSchluessel) return false;
            if (_ziel is not null) EndeOhneSperre();
            _ausstehend = null;
            _geber.Beginnen(aufnahmeTakte ?? Monouhr.Takte(), sprachaktivierung);
            _ziel = ziel;
            _va = sprachaktivierung;
            return true;
        }
    }

    /// <summary>C1: Beginnt mit der Sendeauswahl, umgesetzt über die aktuellen Mitgliedschaften (nur Felder mit senden).</summary>
    public bool BeginnenAusAuswahl(bool sprachaktivierung = false, long? aufnahmeTakte = null)
    {
        var ziel = ZielAusAuswahl();
        return ziel is not null && Beginnen(ziel, sprachaktivierung, aufnahmeTakte);
    }

    /// <summary>Die Sendeauswahl als Ziel: Felder mit senden in Netz-IDs, dazu Global (1), wenn gewählt und erlaubt.</summary>
    public Sendeziel? ZielAusAuswahl()
    {
        var (felder, global) = Auswahl;
        var lage = _client.Lage;
        var netze = new List<ushort>();
        foreach (var f in felder)
        {
            // Seit 0.6.0: nie in eine Ruhe und nie in einen für das Konto gesperrten Kanal (6.16, 6.17).
            if (lage.FeldVon(f) is { Senden: true } m && lage.Mitgliedschaften is { } alle && Lagebild.KanalSendbar(alle, m.NetzId)) netze.Add(m.NetzId);
        }
        if (global && lage.Mitgliedschaften?.Global.Senden == true) netze.Add(Sprachkanal.GlobalNetz);
        if (netze.Count == 0) return null;
        // Ohne MEHRFACH_SENDEN gilt nur das niedrigste markierte Feld (6.9); die Taste wählt ohnehin genau eines (C3).
        if (netze.Count > 1 && !lage.HatRecht(Recht.MehrfachSenden)) netze.RemoveRange(1, netze.Count - 1);
        return Sendeziel.Netz([.. netze]);
    }

    /// <summary>
    /// Ein Audio-Frame (20 ms Opus, höchstens 400 Byte, 5.6). <paramref name="preroll"/> nur am Anfang einer VA-Phase,
    /// höchstens 7. <c>false</c>: nichts gesendet (keine Phase, Auswahl noch nicht stabil, keine Sitzung).
    /// </summary>
    public bool Senden(ReadOnlySpan<byte> opus, bool preroll = false)
    {
        if (opus.IsEmpty || opus.Length > Sprachkanal.OpusMax) throw new ArgumentException("Opus-Nutzlast 1 bis 400 Byte (5.4).", nameof(opus));
        lock (_sperre)
        {
            if (_ausstehend is not null)
            {
                if (Monouhr.Ms() - _ausstehendSeitMs < StabilMs) return false;
                if (_geber is null || _weg is null) return false;
                // C2: neue Phase erst nach 250 ms stabiler Auswahl, mit BEGINN und ohne Pre-roll.
                _geber.Beginnen(Monouhr.Takte(), _va, fortsetzung: true);
                _ziel = _ausstehend;
                _ausstehend = null;
                preroll = false;
            }
            if (_ziel is null || _geber is null || _weg is null) return false;
            var f = _geber.Audio(preroll);
            var a = new Sendeauftrag { Typ = PaketTyp.SpracheHoch, Puffer = System.Buffers.ArrayPool<byte>.Shared.Rent(Sprachkanal.MaxKlartext) };
            a.Laenge = SpracheHoch.Schreiben(a.Puffer, f.FrameNr, f.Sprechphase, f.Kennzeichen, _ziel.Art, _ziel.Netze.AsSpan(), opus);
            _weg.SpracheEinreihen(a);
            Interlocked.Increment(ref _frames);
            return true;
        }
    }

    /// <summary>Beendet die Phase: ENDE dreimal im Abstand von 20 ms, ohne Opus (5.6).</summary>
    public void Beenden()
    {
        lock (_sperre)
        {
            _ausstehend = null;
            if (_ziel is not null) EndeOhneSperre();
        }
    }

    /// <summary>
    /// C2: Die Auswahl wechselt bei gedrückter Taste. Die laufende Phase endet sofort mit ENDE an die alten Ziele; die
    /// neue beginnt erst, wenn <paramref name="neu"/> 250 ms stabil ist (Frames dazwischen werden nicht gesendet).
    /// </summary>
    public void Umschalten(Sendeziel neu)
    {
        ArgumentNullException.ThrowIfNull(neu);
        lock (_sperre)
        {
            if (_ziel is null && _ausstehend is null) return;
            if (_ausstehend is null && neu.Equals(_ziel)) return;
            if (_ziel is not null) EndeOhneSperre();
            _ausstehend = neu;
            _ausstehendSeitMs = Monouhr.Ms();
        }
    }

    /// <summary>
    /// C4 nach einem Mitgliedschaften-Push: Ziele der laufenden NETZ-Phase, die kein Senderecht mehr haben (Feld geleert,
    /// senden entzogen, Netz hinter dem Feld gewechselt, stumm), fallen weg; die Phase endet und setzt ohne Unterbrechung
    /// mit dem Rest fort. Bei einer Durchsage beendet nur "stumm für alle" das Senden.
    /// </summary>
    internal void MitgliedschaftenGeaendert(Mitgliedschaften m, long jetztUnixMs)
    {
        SendenetzGeaendert? meldung = null;
        lock (_sperre)
        {
            if (_ziel is null || _geber is null) return;
            var alt = _ziel;
            Sendeziel? neu;
            if (alt.Art == Zielart.Durchsage)
            {
                neu = Stumm(m, 0, jetztUnixMs) ? null : alt;
            }
            else
            {
                var bleiben = new List<ushort>();
                foreach (var n in alt.Netze)
                {
                    // Seit 0.6.0: Ein Feld auf Ruhe oder mit einem gesperrten Kanal sendet nicht (6.16, 6.17).
                    var darf = n == Sprachkanal.GlobalNetz ? m.Global.Senden : m.Felder.Any(f => f.NetzId == n && f.Senden) && Lagebild.KanalSendbar(m, n);
                    if (darf && !Stumm(m, n, jetztUnixMs)) bleiben.Add(n);
                }
                neu = bleiben.Count == 0 ? null : Sendeziel.Netz([.. bleiben]);
            }
            if (neu is not null && neu.Equals(alt)) return;
            EndeOhneSperre();
            if (neu is not null)
            {
                _geber.Beginnen(Monouhr.Takte(), _va, fortsetzung: true);
                _ziel = neu;
            }
            meldung = new SendenetzGeaendert(alt, neu, m.Durch);
        }
        // Läuft auf der Leseschleife: eine Ausnahme der Anwendung geht an HintergrundFehler, nicht in die Schleife.
        if (SendenetzGeaendert is not { } behandler) return;
        try
        {
            behandler(this, meldung);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _client.FehlerMelden("SendenetzGeaendert", ex);
        }
    }

    private static bool Stumm(Mitgliedschaften m, ushort netz, long jetzt)
    {
        // Seit 0.6.0 trifft eine Stummschaltung auf ein Kanalnetz alle seine Ch. (4.5 Stummschalten).
        var kanalnetz = Kanalrechnung.Zerlegen(netz, out var n, out _) ? n : (ushort)0;
        foreach (var s in m.Stumm)
        {
            if (s.Bis <= jetzt) continue;
            if (s.NetzId == 0 || s.NetzId == netz || (kanalnetz != 0 && s.NetzId == kanalnetz)) return true;
        }
        return false;
    }

    /// <summary>Nur unter der Sperre: drei ENDE vorab holen, eines sofort, die anderen nach 20 und 40 ms.</summary>
    private void EndeOhneSperre()
    {
        var ziel = _ziel!;
        _ziel = null;
        if (_geber is null || _weg is null) return;
        var t = Monouhr.Takte();
        for (var i = 0; i < SprechphasenGeber.EndeWiederholungen; i++)
        {
            var f = _geber.Ende();
            var a = new Sendeauftrag { Typ = PaketTyp.SpracheHoch, Puffer = System.Buffers.ArrayPool<byte>.Shared.Rent(Sprachkanal.MaxKlartext) };
            a.Laenge = SpracheHoch.Schreiben(a.Puffer, f.FrameNr, f.Sprechphase, f.Kennzeichen, ziel.Art, ziel.Netze.AsSpan(), []);
            _weg.SpracheEinreihen(a, i == 0 ? 0 : t + Monouhr.TakteAus(20 * i));
        }
    }
}
