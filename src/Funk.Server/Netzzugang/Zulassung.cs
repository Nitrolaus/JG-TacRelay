// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Schutz;
using Funk.Server.Betrieb;

namespace Funk.Server.Netzzugang;

/// <summary>Urteil der Zulassung über eine neue TCP-Verbindung.</summary>
public enum Zulassungsurteil
{
    Zugelassen,
    /// <summary>Mehr als die erlaubten gleichzeitigen Verbindungen dieses Präfixes.</summary>
    ZuVieleVerbindungen,
    /// <summary>Handshake-Topf des Präfixes leer.</summary>
    PraefixTopfLeer,
    /// <summary>Globaler CPU-Schutztopf leer (gilt nicht für die Vorrangspur).</summary>
    GlobalTopfLeer,
}

/// <summary>
/// Zulassung vor und nach dem TLS-Handshake (4.6, 4.8, Prüfbericht zu 4.8): Handshake-Töpfe je Präfix (IPv4-Adresse
/// bzw. IPv6-/64), größerer Topf für Präfixe der Vorrangspur, gleichzeitige Verbindungen je Präfix, ein globaler Topf
/// nur als CPU-Schutz, an dem die Vorrangspur vorbeigeht. Die Grenzen für unfreigegebene Verbindungen (je Präfix 2,
/// global 20) zählen erst nach der Einstufung und nie freigegebene Geräte. So gibt es keine gemeinsame Grenze, über die
/// Unbekannte freigegebene Geräte aussperren können (Z6). Dazu die Gerätesperre nach zu vielen Verstößen: sie trifft den
/// Fingerabdruck, nicht die IP (4.8). Threadsicher; Zeiten sind monotone Millisekunden.
/// </summary>
public sealed class Zulassung
{
    /// <summary>So viele Präfixe werden höchstens mit Töpfen verfolgt; darüber werden volle Töpfe verworfen.</summary>
    public const int MaxPraefixe = 65_536;

    private readonly Lock _sperre = new();
    private readonly GrenzenKonfiguration _grenzen;
    private readonly Vorrangspur _spur;
    private readonly HashSet<Adresse> _weiterleiter;
    private readonly Dictionary<Praefix, Topfpaar> _toepfe = [];
    private readonly Dictionary<Praefix, int> _offen = [];
    private readonly Dictionary<Praefix, int> _unbekannt = [];
    private readonly Dictionary<Fingerabdruck, long> _geraetesperren = [];
    private readonly TokenEimer _global;
    private int _unbekanntGesamt;

    public Zulassung(GrenzenKonfiguration grenzen, Vorrangspur spur, IEnumerable<IPAddress>? weiterleiter, long jetztMs)
    {
        ArgumentNullException.ThrowIfNull(grenzen);
        ArgumentNullException.ThrowIfNull(spur);
        _grenzen = grenzen;
        _spur = spur;
        _weiterleiter = [.. (weiterleiter ?? []).Select(ip => Adresse.Aus(ip, 0))];
        _global = new TokenEimer(grenzen.HandshakesGlobalJeSekunde, grenzen.HandshakesGlobalJeSekunde, Zeit.TakteAusMs(jetztMs));
    }

    /// <summary>Überlaststufe 2 (7.4): keine neuen unfreigegebenen Verbindungen und Registrierungen.</summary>
    public bool Ueberlast { get; set; }

    public int UnbekannteVerbindungen
    {
        get
        {
            lock (_sperre) return _unbekanntGesamt;
        }
    }

    /// <summary>Zahl der verfolgten Handshake-Töpfe (Messwert, Tests).</summary>
    public int VerfolgtePraefixe
    {
        get
        {
            lock (_sperre) return _toepfe.Count;
        }
    }

    /// <summary>Ob die Quelle ein vertrauenswürdiger Weiterleiter ist (7.6): dann gelten nur globale Grenzen.</summary>
    public bool IstWeiterleiter(Adresse quelle) => _weiterleiter.Contains(quelle.OhnePort);

    /// <summary>
    /// Vor dem TLS-Handshake: gleichzeitige Verbindungen und Handshake-Töpfe. Bei <see cref="Zulassungsurteil.Zugelassen"/>
    /// ist die Verbindung gezählt; der Aufrufer meldet ihr Ende mit <see cref="TcpBeendet"/>.
    /// </summary>
    public Zulassungsurteil TcpPruefen(Adresse quelle, long jetztMs)
    {
        var p = quelle.Praefix;
        var t = Zeit.TakteAusMs(jetztMs);
        var weiter = IstWeiterleiter(quelle);
        var vorrang = !weiter && _spur.Enthaelt(p, Zeit.UnixMs());
        lock (_sperre)
        {
            if (!weiter)
            {
                _offen.TryGetValue(p, out var offen);
                if (offen >= _grenzen.VerbindungenJePraefix) return Zulassungsurteil.ZuVieleVerbindungen;
                var paar = Toepfe(p, t);
                var topf = vorrang ? paar.Vorrang : paar.Unbekannt;
                if (!topf.Nehmen(t)) return Zulassungsurteil.PraefixTopfLeer;
            }
            if (!vorrang && !_global.Nehmen(t)) return Zulassungsurteil.GlobalTopfLeer;
            _offen[p] = _offen.GetValueOrDefault(p) + 1;
            return Zulassungsurteil.Zugelassen;
        }
    }

    /// <summary>Die TCP-Verbindung ist zu.</summary>
    public void TcpBeendet(Adresse quelle)
    {
        var p = quelle.Praefix;
        lock (_sperre)
        {
            if (!_offen.TryGetValue(p, out var n)) return;
            if (n <= 1) _offen.Remove(p);
            else _offen[p] = n - 1;
        }
    }

    /// <summary>
    /// Nach der Einstufung eines unbekannten Geräts: belegt einen der Plätze für unfreigegebene Verbindungen (je Präfix,
    /// global). <c>false</c> heißt <c>SERVER_VOLL</c> bzw. Trennung. Freigegebene Geräte rufen das nie auf.
    /// </summary>
    public bool UnbekanntBelegen(Adresse quelle)
    {
        var p = quelle.Praefix;
        var weiter = IstWeiterleiter(quelle);
        lock (_sperre)
        {
            if (Ueberlast || _unbekanntGesamt >= _grenzen.UnbekannteGlobal) return false;
            _unbekannt.TryGetValue(p, out var n);
            if (!weiter && n >= _grenzen.UnbekannteJePraefix) return false;
            _unbekannt[p] = n + 1;
            _unbekanntGesamt++;
            return true;
        }
    }

    /// <summary>Gibt einen Platz aus <see cref="UnbekanntBelegen"/> frei (Verbindung zu oder Gerät freigegeben).</summary>
    public void UnbekanntFreigeben(Adresse quelle)
    {
        var p = quelle.Praefix;
        lock (_sperre)
        {
            if (!_unbekannt.TryGetValue(p, out var n)) return;
            if (n <= 1) _unbekannt.Remove(p);
            else _unbekannt[p] = n - 1;
            _unbekanntGesamt--;
        }
    }

    /// <summary>Mehr als 50 Verstöße in 60 s (4.8): dieses Gerät ist 60 s gesperrt, nicht die IP.</summary>
    public void GeraetSperren(Fingerabdruck geraet, long jetztMs, long dauerMs)
    {
        lock (_sperre) _geraetesperren[geraet] = jetztMs + dauerMs;
    }

    public bool GeraetGesperrt(Fingerabdruck geraet, long jetztMs)
    {
        lock (_sperre)
        {
            if (!_geraetesperren.TryGetValue(geraet, out var bis)) return false;
            if (jetztMs < bis) return true;
            _geraetesperren.Remove(geraet);
            return false;
        }
    }

    /// <summary>Verwirft volle, also ruhende Töpfe und abgelaufene Gerätesperren (regelmäßig aufrufen).</summary>
    public void Aufraeumen(long jetztMs)
    {
        var t = Zeit.TakteAusMs(jetztMs);
        lock (_sperre)
        {
            foreach (var (p, paar) in _toepfe.ToArray())
            {
                if (_offen.ContainsKey(p)) continue;
                paar.Unbekannt.Nehmen(t, 0);
                paar.Vorrang.Nehmen(t, 0);
                if (paar.Unbekannt.Stand >= _grenzen.HandshakesUnbekanntStoss && paar.Vorrang.Stand >= _grenzen.HandshakesVorrangStoss)
                    _toepfe.Remove(p);
            }
            foreach (var (g, bis) in _geraetesperren.ToArray())
                if (jetztMs >= bis) _geraetesperren.Remove(g);
        }
    }

    private Topfpaar Toepfe(Praefix p, long takte)
    {
        if (_toepfe.TryGetValue(p, out var paar)) return paar;
        if (_toepfe.Count >= MaxPraefixe)
        {
            // Unter einer Flut vieler Präfixe fällt ein Topf ohne offene Verbindung heraus. Ohne Kopie der Schlüssel:
            // früher legte jedes neue Präfix unter dieser Sperre ein Feld mit 65.536 Einträgen an (Entfernen beim
            // Aufzählen ist bei Dictionary seit .NET Core 3.0 erlaubt).
            foreach (var alt in _toepfe.Keys)
            {
                if (_offen.ContainsKey(alt)) continue;
                _toepfe.Remove(alt);
                break;
            }
        }
        paar = new Topfpaar(
            new TokenEimer(_grenzen.HandshakesUnbekanntStoss, _grenzen.HandshakesUnbekanntJeMinute / 60.0, takte),
            new TokenEimer(_grenzen.HandshakesVorrangStoss, _grenzen.HandshakesVorrangJeMinute / 60.0, takte));
        _toepfe[p] = paar;
        return paar;
    }

    private sealed record Topfpaar(TokenEimer Unbekannt, TokenEimer Vorrang);
}
