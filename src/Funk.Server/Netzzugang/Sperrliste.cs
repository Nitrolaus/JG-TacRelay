// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Funk.Server.Betrieb;

namespace Funk.Server.Netzzugang;

/// <summary>Warum eine Adresse Punkte auf der Sperrliste bekommt (A51).</summary>
public enum Sperranlass
{
    /// <summary>TLS-Handshake gescheitert oder abgebrochen (Scanner, Browser, falsches Protokoll, Abbruch nach dem Serverzertifikat).</summary>
    Handshake,

    /// <summary>Handshake fertig, aber ohne vereinbartes ALPN oder ohne P-256-Gerätezertifikat.</summary>
    OhneGeraetezertifikat,

    /// <summary>Handshake-Topf des Präfixes leer: mehr Verbindungsversuche, als ein Client je macht.</summary>
    HandshakeFlut,

    /// <summary>Protokollmüll vor der Anmeldung (Rahmen, JSON, Zustand) oder kein Hello in der Frist.</summary>
    Protokollfehler,

    /// <summary>Ein unfreigegebenes Gerät hat die Ratengrenzen gesprengt (Gerätesperre, 4.8).</summary>
    Ratengrenze,

    /// <summary>Antrag mit Namen über der Grenze je Verbindung oder je Präfix und Stunde.</summary>
    Antragsflut,

    /// <summary>Registrierungscode über der Grenze je Verbindung oder je Präfix und Stunde.</summary>
    Registrierungsflut,
}

/// <summary>Urteil der Sperrliste direkt nach <c>accept</c>.</summary>
public enum Sperrurteil
{
    Frei,

    /// <summary>Gesperrt, aber diese eine Verbindung darf bis zum Handshake (freigegebenes Gerät hinter derselben Adresse).</summary>
    Durchlass,

    Gesperrt,
}

/// <summary>Eine Sperre für Konsole und Tests: Kennung wie im Logbuch (HMAC), nie die Adresse.</summary>
public sealed record Sperreintrag(string Kennung, bool Gruppe, int Stufe, long BisMs, Sperranlass Anlass, long Abgewiesen);

/// <summary>
/// Eine neue Sperre. <see cref="Ausloeser"/> trägt die Adresse im Klartext nur im Speicher, für die optionale
/// fail2ban-Zeile; ins Logbuch geht nur <see cref="Kennung"/>.
/// </summary>
public sealed record Sperrmeldung(Adresse Ausloeser, string Kennung, bool Gruppe, int Stufe, long DauerMs, Sperranlass Anlass, int Punkte);

/// <summary>
/// Eingebaute, befristete Sperrliste (A51, Schutz gegen Botnetze, Scanner und Durchprobieren). Präfixe nach Abschnitt 0
/// (IPv4-Adresse, IPv6-/64) sammeln Punkte für gescheiterte Handshakes, Protokollmüll vor der Anmeldung und Antrags- oder
/// Registrierungsfluten; ab der Schwelle ist das Präfix gesperrt, und die TLS-Annahme schließt seine Verbindungen gleich
/// nach <c>accept</c>, vor jeder Arbeit. Jede weitere Sperre dauert länger, bis zur Höchstdauer; nach einer ruhigen Zeit
/// beginnt die Stufe neu. Sind mehrere /64 eines /48 gesperrt oder sammeln seine /64 zusammen genug Punkte (wer je Verbindung
/// sein /64 wechselt), gilt eine Sammelsperre für das /48.
/// <para>
/// Nie gesperrt werden Präfixe der Vorrangspur (in den letzten 14 Tagen eine AKTIV-Sitzung: Familie, gemeinsames NAT),
/// vertrauenswürdige Weiterleiter und die Ausnahmen aus server.json; dort zählen nicht einmal Punkte. Freigegebene Geräte
/// heben eine Sperre auf (<see cref="Entlasten"/>); damit sie hinter einer gesperrten Adresse (CGNAT) überhaupt bis zum
/// Handshake kommen, lässt die Sperre alle <c>durchlassSekunden</c> eine Verbindung durch.
/// </para>
/// <para>
/// Gespeichert werden nur HMAC-Werte (Schlüssel aus dem Betriebsgeheimnis) und die Kennung des Logbuchs, wie bei der
/// Vorrangspur: Eine Sperre übersteht einen Neustart, ohne dass eine Adresse im Klartext auf der Platte steht. Höchstens
/// <c>maxEintraege</c> Präfixe; ist die Liste voll, fallen zuerst ungesperrte, lange ruhige heraus, Sperren nie. Threadsicher;
/// Zeiten sind Unix-Millisekunden.
/// </para>
/// </summary>
public sealed class Sperrliste
{
    private static readonly UInt128 Obere48 = UInt128.MaxValue << 80;

    private readonly Lock _sperre = new();
    private readonly SperrlistenKonfiguration _k;
    private readonly byte[] _schluessel;
    private readonly Schwaerzung _schwaerzung;
    private readonly Vorrangspur _spur;
    private readonly IPNetwork[] _ausnahmen;
    private readonly Dictionary<UInt128, Eintrag> _eintraege = [];
    private int _gruppen;
    private bool _geaendert;

    public Sperrliste(SperrlistenKonfiguration konfiguration, ReadOnlySpan<byte> schluessel, Schwaerzung schwaerzung, Vorrangspur spur,
        IEnumerable<IPNetwork>? ausnahmen = null)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);
        ArgumentNullException.ThrowIfNull(schwaerzung);
        ArgumentNullException.ThrowIfNull(spur);
        if (schluessel.Length < 16) throw new ArgumentException("HMAC-Schlüssel zu kurz.", nameof(schluessel));
        _k = konfiguration;
        _schluessel = schluessel.ToArray();
        _schwaerzung = schwaerzung;
        _spur = spur;
        _ausnahmen = [.. ausnahmen ?? []];
    }

    /// <summary>Punkte je Anlass; die Schwelle steht in server.json (Voreinstellung 10 in 10 min).</summary>
    public static int Punkte(Sperranlass anlass) => anlass switch
    {
        Sperranlass.Handshake or Sperranlass.HandshakeFlut => 1,
        Sperranlass.OhneGeraetezertifikat => 2,
        Sperranlass.Protokollfehler or Sperranlass.Antragsflut or Sperranlass.Registrierungsflut => 3,
        Sperranlass.Ratengrenze => 5,
        _ => 1,
    };

    public static string Drahtname(Sperranlass anlass) => anlass switch
    {
        Sperranlass.Handshake => "HANDSHAKE",
        Sperranlass.OhneGeraetezertifikat => "OHNE_GERAETEZERTIFIKAT",
        Sperranlass.HandshakeFlut => "HANDSHAKE_FLUT",
        Sperranlass.Protokollfehler => "PROTOKOLLFEHLER",
        Sperranlass.Ratengrenze => "RATENGRENZE",
        Sperranlass.Antragsflut => "ANTRAGSFLUT",
        Sperranlass.Registrierungsflut => "REGISTRIERUNGSFLUT",
        _ => anlass.ToString().ToUpperInvariant(),
    };

    private static Sperranlass? AnlassAus(string? text)
    {
        foreach (var a in Enum.GetValues<Sperranlass>())
            if (string.Equals(Drahtname(a), text, StringComparison.Ordinal)) return a;
        return null;
    }

    /// <summary>Wird bei jeder neuen Sperre aufgerufen, außerhalb der Sperre der Liste (Logbuch, Klartextzeile).</summary>
    public Action<Sperrmeldung>? Gesperrt { get; set; }

    /// <summary>Seit dem letzten <see cref="AlsJson"/> geändert (Sperren, Stufen).</summary>
    public bool Geaendert
    {
        get
        {
            lock (_sperre) return _geaendert;
        }
    }

    /// <summary>Verfolgte Präfixe (mit Punkten, Sperre oder gemerkter Stufe).</summary>
    public int Beobachtet
    {
        get
        {
            lock (_sperre) return _eintraege.Count;
        }
    }

    private long FensterMs => _k.FensterSekunden * 1000L;

    private long DurchlassMs => _k.DurchlassSekunden * 1000L;

    private long HoechstMs => _k.HoechstdauerStunden * 3_600_000L;

    private long VergessenMs => _k.VergessenStunden * 3_600_000L;

    /// <summary>Direkt nach <c>accept</c>: frei, gesperrt oder (gesperrt, aber) Durchlass für diese eine Verbindung.</summary>
    public Sperrurteil Pruefen(Adresse quelle, long jetztMs)
    {
        if (!_k.Aktiv || Ausgenommen(quelle, jetztMs)) return Sperrurteil.Frei;
        var h = Wert(quelle.Praefix, quelle.V6 ? (byte)64 : (byte)32);
        var g = quelle.V6 && Volatile.Read(ref _gruppen) > 0 ? GruppenWert(quelle) : default;
        lock (_sperre)
        {
            if (Lesen(h, jetztMs) is { } e && e.SperreBis > jetztMs) return Urteil(e, jetztMs);
            if (g != default && Lesen(g, jetztMs) is { } ge && ge.SperreBis > jetztMs) return Urteil(ge, jetztMs);
        }
        return Sperrurteil.Frei;
    }

    /// <summary>Nur unter der Sperre: Eintrag holen und einen Rücksprung der Uhr ausgleichen (<see cref="Uhrsprung"/>).</summary>
    private Eintrag? Lesen(UInt128 schluessel, long jetztMs)
    {
        if (!_eintraege.TryGetValue(schluessel, out var e)) return null;
        Uhrsprung(e, jetztMs);
        return e;
    }

    /// <summary>
    /// Nur unter der Sperre: Die Liste rechnet mit der Wanduhr, damit Sperren einen Neustart überstehen. Springt sie zurück
    /// (NTP-Schritt, beim Start falsche Uhr), gäbe es bis zum Einholen keinen Durchlass, eine Sperre dauerte um den Sprung
    /// länger, und das Fenster der Punkte liefe nie ab. Deshalb: eine Sperre höchstens ihre Dauer ab jetzt, der Durchlass sofort
    /// wieder möglich, das Fenster beginnt neu.
    /// </summary>
    private void Uhrsprung(Eintrag e, long jetztMs)
    {
        if (e.SperreBis > jetztMs && e.Stufe > 0)
        {
            var dauer = Dauer(e.Stufe);
            if (e.SperreBis - jetztMs > dauer)
            {
                e.SperreBis = jetztMs + dauer;
                e.StufeBis = Math.Min(e.StufeBis, e.SperreBis + VergessenMs);
                _geaendert = true;
            }
        }
        if (e.DurchlassZuletzt > jetztMs) e.DurchlassZuletzt = jetztMs - DurchlassMs;
        if (e.FensterStart > jetztMs)
        {
            e.FensterStart = jetztMs;
            e.Punkte = 0;
        }
    }

    private Sperrurteil Urteil(Eintrag e, long jetztMs)
    {
        if (jetztMs - e.DurchlassZuletzt >= DurchlassMs)
        {
            e.DurchlassZuletzt = jetztMs;
            return Sperrurteil.Durchlass;
        }
        e.Abgewiesen++;
        return Sperrurteil.Gesperrt;
    }

    /// <summary>Ob die Adresse gerade gesperrt ist (auch über eine Sammelsperre), ohne einen Durchlass zu verbrauchen.</summary>
    public bool IstGesperrt(Adresse quelle, long jetztMs)
    {
        if (!_k.Aktiv || Ausgenommen(quelle, jetztMs)) return false;
        var h = Wert(quelle.Praefix, quelle.V6 ? (byte)64 : (byte)32);
        var g = quelle.V6 && Volatile.Read(ref _gruppen) > 0 ? GruppenWert(quelle) : default;
        lock (_sperre)
        {
            return (Lesen(h, jetztMs) is { } e && e.SperreBis > jetztMs)
                || (g != default && Lesen(g, jetztMs) is { } ge && ge.SperreBis > jetztMs);
        }
    }

    /// <summary>
    /// Ein Verstoß dieser Adresse. Erreichen die Punkte im Fenster die Schwelle, beginnt eine Sperre (Meldung über
    /// <see cref="Gesperrt"/>). Während einer Sperre zählt nichts; ausgenommene Adressen zählen nie. Bei IPv6 zählen die
    /// Punkte zusätzlich für das /48 (<see cref="Gruppenpunkte"/>).
    /// </summary>
    public void Vermerken(Adresse quelle, Sperranlass anlass, long jetztMs)
    {
        if (!_k.Aktiv || Ausgenommen(quelle, jetztMs)) return;
        var p = quelle.Praefix;
        var h = Wert(p, quelle.V6 ? (byte)64 : (byte)32);
        var g = quelle.V6 && _k.V6Sammelschwelle > 0 ? GruppenWert(quelle) : default;
        var punkte = Punkte(anlass);
        Sperrmeldung? einzeln = null;
        Sperrmeldung? sammel = null;
        lock (_sperre)
        {
            var e = Lesen(h, jetztMs);
            if (e is null && Platz(jetztMs))
            {
                e = new Eintrag { Kennung = _schwaerzung.Praefix(p), GruppenSchluessel = g, FensterStart = jetztMs };
                _eintraege[h] = e;
            }
            if (e is not null)
            {
                e.Zuletzt = jetztMs;
                // Gesperrt: Die Verbindung kam über den Durchlass. Sie zählt nicht, auch nicht für das /48.
                if (e.SperreBis > jetztMs) return;
                if (jetztMs - e.FensterStart >= FensterMs)
                {
                    e.FensterStart = jetztMs;
                    e.Punkte = 0;
                }
                e.Punkte += punkte;
                if (e.Punkte >= _k.Schwelle) einzeln = Sperren(e, quelle, anlass, jetztMs);
            }
            if (g != default)
            {
                sammel = Gruppenpunkte(g, quelle, anlass, punkte, jetztMs);
                if (sammel is null && einzeln is not null) sammel = Sammelsperre(g, quelle, anlass, jetztMs);
            }
        }
        Melden(einzeln);
        Melden(sammel);
    }

    /// <summary>
    /// Nur unter der Sperre: Punkte auch für das /48 (Gegenprüfung 0.3.0). Wer je Verbindung sein /64 wechselt, bringt kein
    /// /64 an die Schwelle, und die Sammelsperre nach gesperrten /64 griffe nie. Das /48 ist deshalb auch gesperrt, wenn seine
    /// /64 zusammen <c>schwelle</c> mal <c>v6Sammelschwelle</c> Punkte im Fenster sammeln (Voreinstellung 40). Präfixe der
    /// Vorrangspur zählen wie überall nicht mit, und für sie gilt die Sammelsperre nicht.
    /// </summary>
    private Sperrmeldung? Gruppenpunkte(UInt128 g, Adresse quelle, Sperranlass anlass, int punkte, long jetztMs)
    {
        if (Gruppeneintrag(g, quelle, jetztMs) is not { } ge) return null;
        ge.Zuletzt = jetztMs;
        if (ge.SperreBis > jetztMs) return null;
        if (jetztMs - ge.FensterStart >= FensterMs)
        {
            ge.FensterStart = jetztMs;
            ge.Punkte = 0;
        }
        ge.Punkte += punkte;
        return ge.Punkte >= (long)_k.Schwelle * _k.V6Sammelschwelle ? Sperren(ge, quelle, anlass, jetztMs) : null;
    }

    /// <summary>Nur unter der Sperre: der Eintrag des /48, neu angelegt, wenn Platz ist.</summary>
    private Eintrag? Gruppeneintrag(UInt128 g, Adresse quelle, long jetztMs)
    {
        if (Lesen(g, jetztMs) is { } ge) return ge;
        if (!Platz(jetztMs)) return null;
        ge = new Eintrag { Kennung = _schwaerzung.Gruppe(Gruppe48(quelle)), Gruppe = true, FensterStart = jetztMs };
        _eintraege[g] = ge;
        Volatile.Write(ref _gruppen, _gruppen + 1);
        return ge;
    }

    private void Melden(Sperrmeldung? m)
    {
        if (m is not null) Gesperrt?.Invoke(m);
    }

    /// <summary>Nur unter der Sperre: Sperre mit der nächsten Stufe beginnen.</summary>
    private Sperrmeldung Sperren(Eintrag e, Adresse quelle, Sperranlass anlass, long jetztMs)
    {
        var stufe = e.Stufe > 0 && jetztMs < e.StufeBis ? e.Stufe + 1 : 1;
        var dauer = Dauer(stufe);
        var punkte = e.Punkte;
        e.Stufe = stufe;
        e.SperreBis = jetztMs + dauer;
        e.StufeBis = e.SperreBis + VergessenMs;
        e.Anlass = anlass;
        e.DurchlassZuletzt = jetztMs;
        e.Abgewiesen = 0;
        e.Punkte = 0;
        e.FensterStart = jetztMs;
        e.Zuletzt = jetztMs;
        _geaendert = true;
        return new Sperrmeldung(quelle, e.Kennung, e.Gruppe, stufe, dauer, anlass, punkte);
    }

    /// <summary>Dauer einer Sperre der Stufe: erste Sperre mal Faktor hoch (Stufe - 1), höchstens die Höchstdauer.</summary>
    private long Dauer(int stufe)
    {
        var dauer = _k.ErsteSperreSekunden * 1000L;
        for (var i = 1; i < stufe && dauer < HoechstMs; i++) dauer *= _k.Faktor;
        return Math.Min(dauer, HoechstMs);
    }

    /// <summary>Nur unter der Sperre: sind genug /64 dieses /48 gesperrt, auch das /48 sperren.</summary>
    private Sperrmeldung? Sammelsperre(UInt128 g, Adresse quelle, Sperranlass anlass, long jetztMs)
    {
        var gesperrt = 0;
        foreach (var e in _eintraege.Values)
            if (!e.Gruppe && e.GruppenSchluessel == g && e.SperreBis > jetztMs) gesperrt++;
        if (gesperrt < _k.V6Sammelschwelle) return null;
        if (Gruppeneintrag(g, quelle, jetztMs) is not { } ge) return null;
        ge.Zuletzt = jetztMs;
        return ge.SperreBis > jetztMs ? null : Sperren(ge, quelle, anlass, jetztMs);
    }

    /// <summary>
    /// Ein freigegebenes Gerät kam von dieser Adresse: Sperre, Punkte und Stufe fort, bei IPv6 auch eine Sammelsperre des
    /// /48. <c>true</c>, wenn dabei eine Sperre aufgehoben wurde.
    /// </summary>
    public bool Entlasten(Adresse quelle, long jetztMs)
    {
        var h = Wert(quelle.Praefix, quelle.V6 ? (byte)64 : (byte)32);
        var g = quelle.V6 && Volatile.Read(ref _gruppen) > 0 ? GruppenWert(quelle) : default;
        lock (_sperre)
        {
            var aufgehoben = Entfernen(h, out var e) && e.SperreBis > jetztMs;
            if (g != default && Entfernen(g, out var ge) && ge.SperreBis > jetztMs) aufgehoben = true;
            return aufgehoben;
        }
    }

    /// <summary>Nur unter der Sperre: Eintrag entfernen und Zähler pflegen.</summary>
    private bool Entfernen(UInt128 schluessel, out Eintrag eintrag)
    {
        if (!_eintraege.Remove(schluessel, out eintrag!)) return false;
        if (eintrag.Gruppe) Volatile.Write(ref _gruppen, _gruppen - 1);
        if (eintrag.Stufe > 0) _geaendert = true;
        return true;
    }

    /// <summary>Laufende Sperren, die zuerst endende vorn.</summary>
    public IReadOnlyList<Sperreintrag> Gesperrte(long jetztMs)
    {
        lock (_sperre)
        {
            foreach (var e in _eintraege.Values) Uhrsprung(e, jetztMs);
            return [.. _eintraege.Values.Where(e => e.SperreBis > jetztMs).OrderBy(e => e.SperreBis).ThenBy(e => e.Kennung, StringComparer.Ordinal)
                .Select(e => new Sperreintrag(e.Kennung, e.Gruppe, e.Stufe, e.SperreBis, e.Anlass, e.Abgewiesen))];
        }
    }

    /// <summary>Konsole: Eintrag mit dieser Kennung (wie im Logbuch) fort, samt Stufe. <c>true</c>, wenn er eine Sperre trug.</summary>
    public bool Aufheben(string kennung)
    {
        ArgumentNullException.ThrowIfNull(kennung);
        lock (_sperre)
        {
            foreach (var (h, e) in _eintraege)
                if (string.Equals(e.Kennung, kennung, StringComparison.Ordinal)) return Entfernen(h, out var weg) && weg.SperreBis > 0;
        }
        return false;
    }

    /// <summary>Konsole: Einträge dieser Adresse fort (bei IPv6 das /64 und das /48). Liefert, wie viele davon eine Sperre trugen.</summary>
    public int AufhebenFuer(Adresse quelle)
    {
        var h = Wert(quelle.Praefix, quelle.V6 ? (byte)64 : (byte)32);
        var g = quelle.V6 ? GruppenWert(quelle) : default;
        lock (_sperre)
        {
            var n = 0;
            if (Entfernen(h, out var e) && e.SperreBis > 0) n++;
            if (g != default && Entfernen(g, out var ge) && ge.SperreBis > 0) n++;
            return n;
        }
    }

    /// <summary>Konsole: alles fort. Liefert, wie viele Einträge eine Sperre trugen.</summary>
    public int AlleAufheben()
    {
        lock (_sperre)
        {
            var n = _eintraege.Values.Count(e => e.SperreBis > 0);
            if (_eintraege.Count > 0) _geaendert = true;
            _eintraege.Clear();
            Volatile.Write(ref _gruppen, 0);
            return n;
        }
    }

    /// <summary>Entfernt, was nichts mehr bewirkt: keine Sperre, keine gemerkte Stufe, Fenster abgelaufen (regelmäßig aufrufen).</summary>
    public void Aufraeumen(long jetztMs)
    {
        lock (_sperre)
        {
            // Entfernen beim Aufzählen ist bei Dictionary seit .NET Core 3.0 erlaubt.
            foreach (var (h, e) in _eintraege)
            {
                Uhrsprung(e, jetztMs);
                if (Verfallen(e, jetztMs)) Entfernen(h, out _);
            }
        }
    }

    private bool Verfallen(Eintrag e, long jetztMs) =>
        e.SperreBis <= jetztMs && e.StufeBis <= jetztMs && jetztMs - e.FensterStart >= FensterMs;

    /// <summary>
    /// Nur unter der Sperre: Platz für einen neuen Eintrag. Ist die Liste voll, fällt Verfallenes heraus, sonst der am
    /// längsten ruhige Eintrag ohne laufende Sperre. Tragen alle eine Sperre, wird das neue Präfix nicht verfolgt (die
    /// Grenzen der Zulassung gelten für es weiter).
    /// </summary>
    private bool Platz(long jetztMs)
    {
        if (_eintraege.Count < _k.MaxEintraege) return true;
        UInt128? opfer = null;
        var aeltest = long.MaxValue;
        foreach (var (h, e) in _eintraege)
        {
            if (Verfallen(e, jetztMs))
            {
                Entfernen(h, out _);
                continue;
            }
            if (e.SperreBis <= jetztMs && e.Zuletzt < aeltest)
            {
                aeltest = e.Zuletzt;
                opfer = h;
            }
        }
        if (_eintraege.Count < _k.MaxEintraege) return true;
        if (opfer is not { } o) return false;
        Entfernen(o, out _);
        return true;
    }

    /// <summary>Sperren und gemerkte Stufen als JSON (nur HMAC-Werte und Kennungen, keine Adresse).</summary>
    public byte[] AlsJson()
    {
        lock (_sperre)
        {
            _geaendert = false;
            var liste = new List<Gespeichert>();
            foreach (var (h, e) in _eintraege)
            {
                if (e.Stufe == 0) continue;
                liste.Add(new Gespeichert(Base64(h), e.Kennung, e.Gruppe, e.GruppenSchluessel == default ? null : Base64(e.GruppenSchluessel),
                    e.Stufe, e.SperreBis, e.StufeBis, Drahtname(e.Anlass)));
            }
            return JsonSerializer.SerializeToUtf8Bytes(new Datei(liste), Ablageformat.Optionen);
        }
    }

    /// <summary>
    /// Lädt einen gespeicherten Stand. Vergessene Einträge fallen fort; eine Sperre, die weiter als die Höchstdauer in die
    /// Zukunft reicht (verstellte Uhr, bearbeitete Datei), gilt nur die Höchstdauer.
    /// </summary>
    public void AusJsonLaden(ReadOnlySpan<byte> json, long jetztMs)
    {
        var d = JsonSerializer.Deserialize<Datei>(json, Ablageformat.Optionen) ?? throw new InvalidDataException("Sperrliste leer.");
        lock (_sperre)
        {
            _eintraege.Clear();
            var gruppen = 0;
            foreach (var s in d.Eintraege)
            {
                if (_eintraege.Count >= _k.MaxEintraege) break;
                if (!TryWert(s.H, out var h) || s.Stufe is < 1 or > 1000 || s.StufeBis <= jetztMs || string.IsNullOrEmpty(s.K)) continue;
                var bis = Math.Min(s.Bis, jetztMs + HoechstMs);
                var e = new Eintrag
                {
                    Kennung = s.K,
                    Gruppe = s.Gruppe,
                    GruppenSchluessel = s.G is { } gt && TryWert(gt, out var gw) ? gw : default,
                    Stufe = s.Stufe,
                    SperreBis = bis,
                    StufeBis = Math.Min(s.StufeBis, Math.Max(bis, jetztMs) + VergessenMs),
                    Anlass = AnlassAus(s.Anlass) ?? Sperranlass.Handshake,
                    DurchlassZuletzt = jetztMs,
                    FensterStart = jetztMs,
                    Zuletzt = jetztMs,
                };
                _eintraege[h] = e;
                if (e.Gruppe) gruppen++;
            }
            Volatile.Write(ref _gruppen, gruppen);
            _geaendert = false;
        }
    }

    /// <summary>
    /// Die Zeile für fail2ban (nur mit <c>klartextDatei</c>), in fester Form:
    /// <c>2026-10-01T09:30:05Z FUNK-SPERRE ip=203.0.113.7 netz=203.0.113.7/32 dauer=300 stufe=1 anlass=HANDSHAKE kennung=p4#...</c>.
    /// </summary>
    public static string Klartextzeile(Sperrmeldung m, DateTimeOffset zeit)
    {
        ArgumentNullException.ThrowIfNull(m);
        var a = m.Ausloeser;
        string netz;
        if (!a.V6)
        {
            netz = a.IpAdresse() + "/32";
        }
        else
        {
            var bits = m.Gruppe ? 48 : 64;
            var maske = m.Gruppe ? Obere48 : UInt128.MaxValue << 64;
            Span<byte> roh = stackalloc byte[16];
            BinaryPrimitives.WriteUInt128BigEndian(roh, a.Ip & maske);
            netz = new IPAddress(roh) + "/" + bits.ToString(CultureInfo.InvariantCulture);
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"{zeit.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'} FUNK-SPERRE ip={a.IpAdresse()} netz={netz} dauer={m.DauerMs / 1000} stufe={m.Stufe} anlass={Drahtname(m.Anlass)} kennung={m.Kennung}");
    }

    // ---------------------------------------------------------------- Ausnahmen und Schlüssel

    private bool Ausgenommen(Adresse quelle, long jetztMs)
    {
        if (_spur.Enthaelt(quelle.Praefix, jetztMs)) return true;
        if (_ausnahmen.Length == 0) return false;
        var ip = quelle.IpAdresse();
        foreach (var n in _ausnahmen)
            if (n.Contains(ip)) return true;
        return false;
    }

    private static Praefix Gruppe48(Adresse quelle) => new(quelle.Ip & Obere48, true);

    private UInt128 GruppenWert(Adresse quelle) => Wert(Gruppe48(quelle), 48);

    /// <summary>HMAC-SHA256 über Präfix und Präfixlänge (ein /48 und ein /64 mit gleichen Bits sind verschieden), 128 Bit, nie 0.</summary>
    private UInt128 Wert(Praefix p, byte laenge)
    {
        Span<byte> eingabe = stackalloc byte[17];
        p.Schreiben(eingabe);
        eingabe[16] = laenge;
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(_schluessel, eingabe, mac);
        var w = BinaryPrimitives.ReadUInt128BigEndian(mac);
        return w == default ? 1 : w;
    }

    private static string Base64(UInt128 wert)
    {
        Span<byte> b = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128BigEndian(b, wert);
        return Convert.ToBase64String(b);
    }

    private static bool TryWert(string text, out UInt128 wert)
    {
        wert = default;
        Span<byte> b = stackalloc byte[16];
        if (!Convert.TryFromBase64String(text, b, out var n) || n != 16) return false;
        wert = BinaryPrimitives.ReadUInt128BigEndian(b);
        return wert != default;
    }

    private sealed class Eintrag
    {
        public required string Kennung;
        public bool Gruppe;
        public UInt128 GruppenSchluessel;
        public int Punkte;
        public long FensterStart;
        public long SperreBis;
        public long StufeBis;
        public int Stufe;
        public Sperranlass Anlass;
        public long DurchlassZuletzt;
        public long Abgewiesen;
        public long Zuletzt;
    }

    private sealed record Gespeichert(string H, string K, bool Gruppe, string? G, int Stufe, long Bis, long StufeBis, string Anlass);

    private sealed record Datei(List<Gespeichert> Eintraege);
}
