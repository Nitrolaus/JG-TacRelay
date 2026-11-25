// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

/// <summary>Zustände des Clients (4.6).</summary>
public enum ClientZustand : byte
{
    Getrennt,
    Verbinden,
    Registrierung,
    Warteraum,
    Sync,
    AktivUdpBinden,
    AktivUdp,
    AktivTunnel,
}

/// <summary>
/// Ergebnis eines Ereignisses im Clientautomaten. <see cref="TunnelAn"/> gesetzt: <c>TunnelModus{an, grund}</c>
/// senden. <see cref="NeuverbindenNachMs"/>: nach dieser Zeit (+/-20 %, siehe <see cref="ClientAutomat.Streuen"/>)
/// neu verbinden. <see cref="NeuenCodeAnbieten"/>: in REGISTRIERUNG einen neuen Code anbieten.
/// </summary>
public readonly record struct ClientUebergang(
    ClientZustand Von,
    ClientZustand Nach,
    bool? TunnelAn = null,
    TunnelGrund? TunnelGrund = null,
    bool NeuversuchGesperrt = false,
    uint? NeuverbindenNachMs = null,
    bool NeuenCodeAnbieten = false)
{
    /// <summary>Zustand hat gewechselt.</summary>
    public bool Geaendert => Von != Nach;
}

/// <summary>
/// Zustandsautomat des Clients nach 4.6, als reines Modell ohne Netzwerk. Zeiten in Millisekunden einer
/// monotonen Uhr. Nicht threadsicher.
/// </summary>
public sealed class ClientAutomat
{
    private long _eintrittMs;
    private long _letztePingAntwortMs;
    private long _letzterPingMs;

    public ClientZustand Zustand { get; private set; } = ClientZustand.Getrennt;

    /// <summary>
    /// Kein automatischer Neuversuch: nach <c>KONTO_GESPERRT</c>, <c>GERAET_GESPERRT</c>, <c>VERSION_ZU_ALT</c>,
    /// <c>VERDRAENGEN_GESPERRT</c> und <c>Bye{VERDRAENGT}</c>. Nur der Nutzer verbindet dann neu.
    /// </summary>
    public bool NeuversuchGesperrt { get; private set; }

    /// <summary>Serverzustände, deren Nachrichten der Client im aktuellen Zustand annimmt.</summary>
    public Zustaende Empfangszustaende => Zustand switch
    {
        ClientZustand.Verbinden => Zustaende.WarteHello,
        ClientZustand.Registrierung => Zustaende.Registrierung,
        ClientZustand.Warteraum => Zustaende.Warteraum,
        ClientZustand.Sync => Zustaende.AktivSync,
        ClientZustand.AktivUdpBinden or ClientZustand.AktivUdp or ClientZustand.AktivTunnel => Zustaende.Aktiv,
        _ => Zustaende.Keine,
    };

    /// <summary>
    /// GETRENNT -> VERBINDEN (Start, Schnellphase oder Backoff abgelaufen). Hello gilt ab <paramref name="jetztMs"/>
    /// als gesendet; ab dann laufen Ping alle 2 s und die Totfrist von 6 s. Bei gesperrtem Neuversuch nur
    /// <paramref name="vomNutzer"/>.
    /// </summary>
    public ClientUebergang Verbinden(long jetztMs, bool vomNutzer = false)
    {
        if (Zustand != ClientZustand.Getrennt) throw new InvalidOperationException($"Verbinden nur aus GETRENNT, Automat steht in {Zustand}.");
        if (NeuversuchGesperrt && !vomNutzer) return new ClientUebergang(Zustand, Zustand, NeuversuchGesperrt: true);
        NeuversuchGesperrt = false;
        _letztePingAntwortMs = jetztMs;
        _letzterPingMs = jetztMs;
        return Wechseln(ClientZustand.Verbinden, jetztMs);
    }

    /// <summary>Welcome empfangen: UNBEKANNT -> REGISTRIERUNG, WARTET -> WARTERAUM, FREIGEGEBEN -> SYNC.</summary>
    public ClientUebergang WelcomeEmpfangen(WelcomeStatus status, long jetztMs) => (Zustand, status) switch
    {
        (ClientZustand.Verbinden, WelcomeStatus.Unbekannt) => Wechseln(ClientZustand.Registrierung, jetztMs),
        (ClientZustand.Verbinden, WelcomeStatus.Wartet) => Wechseln(ClientZustand.Warteraum, jetztMs),
        (ClientZustand.Verbinden or ClientZustand.Warteraum, WelcomeStatus.Freigegeben) => Wechseln(ClientZustand.Sync, jetztMs),
        _ => Getrennt(jetztMs),
    };

    /// <summary>GeraetStatus für das eigene Gerät: WARTET in REGISTRIERUNG -> WARTERAUM; ABGELEHNT -> Trennung folgt.</summary>
    public ClientUebergang GeraetStatusEmpfangen(GeraeteStatus status, long jetztMs) => (Zustand, status) switch
    {
        (ClientZustand.Registrierung, GeraeteStatus.Wartet) => Wechseln(ClientZustand.Warteraum, jetztMs),
        _ => new ClientUebergang(Zustand, Zustand),
    };

    /// <summary>Push Zustand in SYNC: der Stand ist vollständig -> AKTIV/UDP_BINDEN.</summary>
    public ClientUebergang ZustandPushEmpfangen(long jetztMs) =>
        Zustand == ClientZustand.Sync ? Wechseln(ClientZustand.AktivUdpBinden, jetztMs) : new ClientUebergang(Zustand, Zustand);

    /// <summary><c>UdpStatus{GEBUNDEN}</c>: AKTIV/UDP_BINDEN -> AKTIV/UDP.</summary>
    public ClientUebergang UdpGebunden(long jetztMs) =>
        Zustand == ClientZustand.AktivUdpBinden ? Wechseln(ClientZustand.AktivUdp, jetztMs) : new ClientUebergang(Zustand, Zustand);

    /// <summary>UDP-Ausfall erkannt (5.12): AKTIV/UDP -> AKTIV/TUNNEL mit <c>TunnelModus{an, UDP_AUSFALL}</c>.</summary>
    public ClientUebergang UdpAusfall(long jetztMs) =>
        Zustand == ClientZustand.AktivUdp
            ? Wechseln(ClientZustand.AktivTunnel, jetztMs) with { TunnelAn = true, TunnelGrund = TunnelGrund.UdpAusfall }
            : new ClientUebergang(Zustand, Zustand);

    /// <summary>Der Nutzer schaltet den Tunnel ein (Grund NUTZER).</summary>
    public ClientUebergang TunnelVomNutzer(long jetztMs) =>
        Zustand is ClientZustand.AktivUdp or ClientZustand.AktivUdpBinden
            ? Wechseln(ClientZustand.AktivTunnel, jetztMs) with { TunnelAn = true, TunnelGrund = TunnelGrund.Nutzer }
            : new ClientUebergang(Zustand, Zustand);

    /// <summary>UDP-Probe 10 s sauber (5.12): AKTIV/TUNNEL -> AKTIV/UDP mit <c>TunnelModus{aus, UDP_ZURUECK}</c>.</summary>
    public ClientUebergang TunnelProbeSauber(long jetztMs) =>
        Zustand == ClientZustand.AktivTunnel
            ? Wechseln(ClientZustand.AktivUdp, jetztMs) with { TunnelAn = false, TunnelGrund = TunnelGrund.UdpZurueck }
            : new ClientUebergang(Zustand, Zustand);

    /// <summary>
    /// Bye auf der aktuellen Verbindung: -> GETRENNT. VERDRAENGT und ENTFERNT sperren den automatischen Neuversuch, WARTEZEIT
    /// verbindet nach <c>wiederInMs</c> neu. ENTFERNT seit 0.6.3 (Sarevoc 04.10.2026): Vorher verband der Client gleich neu und stellte
    /// still einen neuen Antrag, und statt "entfernt" stand dann etwa "Name schon vergeben" da. Ein neuer Antrag kommt nur noch,
    /// wenn der Nutzer selbst verbindet.
    /// </summary>
    public ClientUebergang ByeEmpfangen(ByeGrund grund, uint? wiederInMs, long jetztMs)
    {
        var u = Getrennt(jetztMs);
        if (grund is ByeGrund.Verdraengt or ByeGrund.Entfernt)
        {
            NeuversuchGesperrt = true;
            return u with { NeuversuchGesperrt = true };
        }
        return (grund is ByeGrund.Wartezeit or ByeGrund.Wartung) && wiederInMs is { } w ? u with { NeuverbindenNachMs = w } : u;
    }

    /// <summary>
    /// Fehler vom Server. Schwere Codes (4.9) trennen; einige sperren den Neuversuch. Ausnahme 3.4: In
    /// REGISTRIERUNG kommen <c>KONTO_GESPERRT</c>, <c>NUR_ADMIN_WERKZEUG</c> und <c>GERAETE_LIMIT</c> als Push ohne
    /// Trennung; dann wird ein neuer Code angeboten.
    /// </summary>
    public ClientUebergang FehlerEmpfangen(Fehlercode code, bool alsPush, long jetztMs)
    {
        if (Zustand == ClientZustand.Registrierung && alsPush && !Fehlercodes.TrenntAlsPushInRegistrierung(code))
            return new ClientUebergang(Zustand, Zustand, NeuenCodeAnbieten: true);
        if (!Fehlercodes.Trennt(code)) return new ClientUebergang(Zustand, Zustand);
        var u = Getrennt(jetztMs);
        if (code is Fehlercode.KontoGesperrt or Fehlercode.GeraetGesperrt or Fehlercode.VersionZuAlt or Fehlercode.VerdraengenGesperrt)
        {
            NeuversuchGesperrt = true;
            return u with { NeuversuchGesperrt = true };
        }
        return u;
    }

    /// <summary>Verbindungsverlust oder schwerer Fehler auf eigener Seite: -> GETRENNT.</summary>
    public ClientUebergang Verbindungsverlust(long jetztMs) => Getrennt(jetztMs);

    /// <summary>Eine Ping-Antwort kam an.</summary>
    public void PingAntwortEmpfangen(long jetztMs) => _letztePingAntwortMs = jetztMs;

    /// <summary>Ob ein Ping fällig ist (alle 2 s ab Hello, unabhängig vom übrigen Verkehr).</summary>
    public bool PingFaellig(long jetztMs) => Zustand != ClientZustand.Getrennt && jetztMs - _letzterPingMs >= Fristen.PingAbstandMs;

    /// <summary>Ein Ping wurde gesendet.</summary>
    public void PingGesendet(long jetztMs) => _letzterPingMs = jetztMs;

    /// <summary>
    /// Fristen: 6 s ohne Ping-Antwort -> GETRENNT; 3 s in AKTIV/UDP_BINDEN ohne Bindung -> Tunnel (KEINE_BINDUNG).
    /// </summary>
    public ClientUebergang Ticken(long jetztMs)
    {
        if (Zustand == ClientZustand.Getrennt) return new ClientUebergang(Zustand, Zustand);
        if (jetztMs - _letztePingAntwortMs >= Fristen.PingTotMs) return Getrennt(jetztMs);
        if (Zustand == ClientZustand.AktivUdpBinden && jetztMs - _eintrittMs >= Fristen.BindungMs)
            return Wechseln(ClientZustand.AktivTunnel, jetztMs) with { TunnelAn = true, TunnelGrund = TunnelGrund.KeineBindung };
        return new ClientUebergang(Zustand, Zustand);
    }

    /// <summary>Streut eine Wartezeit um +/-20 % (<paramref name="zufall01"/> gleichverteilt in [0, 1)).</summary>
    public static uint Streuen(uint wartezeitMs, double zufall01) =>
        (uint)Math.Round(wartezeitMs * (0.8 + 0.4 * Math.Clamp(zufall01, 0, 1)));

    private ClientUebergang Getrennt(long jetztMs) => Wechseln(ClientZustand.Getrennt, jetztMs);

    private ClientUebergang Wechseln(ClientZustand nach, long jetztMs)
    {
        var von = Zustand;
        Zustand = nach;
        _eintrittMs = jetztMs;
        return new ClientUebergang(von, nach);
    }
}
