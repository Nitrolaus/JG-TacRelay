// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

/// <summary>Zustände einer Verbindung im Server (4.6).</summary>
public enum ServerZustand : byte
{
    Tls,
    WarteHello,
    Registrierung,
    Warteraum,
    AktivSync,
    Aktiv,
    Ende,
}

/// <summary>Einstufung des Geräts nach Hello (4.6, Zeile WARTE_HELLO).</summary>
public enum Einstufung : byte
{
    /// <summary>Fingerabdruck unbekannt.</summary>
    Unbekannt,
    /// <summary>Gerät ENTFERNT: darf sich neu registrieren.</summary>
    Entfernt,
    /// <summary>Gerät WARTET auf Freigabe.</summary>
    Wartet,
    /// <summary>Gerät FREIGEGEBEN und Konto AKTIV.</summary>
    Freigegeben,
    /// <summary>Gerät ABGELEHNT.</summary>
    Abgelehnt,
    /// <summary>Gerät ENTZOGEN.</summary>
    Entzogen,
    /// <summary>Konto GESPERRT.</summary>
    KontoGesperrt,
}

/// <summary>
/// Ergebnis eines Ereignisses im Serverautomaten: neuer Zustand und was dafür zu senden ist.
/// <see cref="SyncBeginnen"/>: VoiceKeys(START, 0), NetzListe, Mitgliedschaften, Praesenz, PegelVoreinstellungen,
/// Zustand senden und eine ältere Sitzung desselben Kontos nach 4.6 beenden (nur beim Eintritt in AKTIV_SYNC).
/// </summary>
public readonly record struct Uebergang(
    ServerZustand Von,
    ServerZustand Nach,
    WelcomeStatus? Welcome = null,
    Fehlercode? Fehler = null,
    ByeGrund? Bye = null,
    uint? WiederInMs = null,
    bool SyncBeginnen = false)
{
    /// <summary>Verbindung trennen (Zustand ENDE erreicht).</summary>
    public bool Trennen => Nach == ServerZustand.Ende && Von != ServerZustand.Ende;

    /// <summary>Zustand hat gewechselt.</summary>
    public bool Geaendert => Von != Nach;
}

/// <summary>
/// Zustandsautomat einer Verbindung im Server nach 4.6, als reines Modell ohne Netzwerk. Zeiten sind
/// Millisekunden einer monotonen Uhr; der Aufrufer liefert sie (simulierbar). Nicht threadsicher.
/// </summary>
public sealed class ServerAutomat
{
    private long _eintrittMs;
    private long _helloMs;
    private long _letzterRahmenMs;

    public ServerAutomat(long jetztMs)
    {
        Zustand = ServerZustand.Tls;
        _eintrittMs = jetztMs;
        _letzterRahmenMs = jetztMs;
    }

    public ServerZustand Zustand { get; private set; }

    /// <summary>Grenze für das Längenfeld eingehender Rahmen im aktuellen Zustand (4.2).</summary>
    public int Rahmengrenze => Rahmengrenzen.ServerEmpfang(Zustand);

    /// <summary>Jeder empfangene Rahmen setzt die Ruhefrist (10 s) zurück.</summary>
    public void RahmenEmpfangen(long jetztMs) => _letzterRahmenMs = jetztMs;

    /// <summary>
    /// Ob der Client im aktuellen Zustand eine Nachricht dieses Typs und dieser Art senden darf (Tabelle 4.6).
    /// In AKTIV gilt der Katalog; Rechte prüft die Rechteprüfung (<c>KEIN_RECHT</c> ohne Trennung).
    /// </summary>
    public bool ClientNachrichtErlaubt(Nachrichtentyp typ, Nachrichtenart art) => Zustand switch
    {
        ServerZustand.WarteHello => typ == Nachrichtentyp.Hello && art == Nachrichtenart.Push,
        ServerZustand.Registrierung =>
            (typ == Nachrichtentyp.Registrierung && art == Nachrichtenart.Anfrage)
            || ((typ is Nachrichtentyp.VerknuepfungBestaetigen or Nachrichtentyp.Fehler) && art == Nachrichtenart.Antwort)
            || GemeinsamVorAktiv(typ, art),
        ServerZustand.Warteraum => GemeinsamVorAktiv(typ, art),
        ServerZustand.AktivSync =>
            ((typ is Nachrichtentyp.VoiceKeys or Nachrichtentyp.Fehler) && art == Nachrichtenart.Antwort)
            || GemeinsamVorAktiv(typ, art),
        ServerZustand.Aktiv => Katalog.Finden((ushort)typ) is { } e
            && (e.Zustaende & Zustaende.Aktiv) != 0 && e.Fuer(Seite.Client, art).Erlaubt,
        _ => false,
    };

    private static bool GemeinsamVorAktiv(Nachrichtentyp typ, Nachrichtenart art) =>
        (typ == Nachrichtentyp.Ping && art == Nachrichtenart.Anfrage) || (typ == Nachrichtentyp.Bye && art == Nachrichtenart.Push);

    /// <summary>TLS fertig: ALPN passt und P-256-Zertifikat -> WARTE_HELLO, sonst ENDE.</summary>
    public Uebergang TlsFertig(bool alpnPasst, bool p256Zertifikat, long jetztMs)
    {
        Erwarten(ServerZustand.Tls);
        return alpnPasst && p256Zertifikat ? Wechseln(ServerZustand.WarteHello, jetztMs) : Wechseln(ServerZustand.Ende, jetztMs);
    }

    /// <summary>
    /// Hello verarbeitet: Einstufung des Geräts. <paramref name="clientZuAlt"/>: Client-Version unter
    /// <c>mindestClientVersion</c> -> <c>VERSION_ZU_ALT</c> statt Welcome (4.5).
    /// </summary>
    public Uebergang HelloVerarbeitet(Einstufung einstufung, bool clientZuAlt, long jetztMs)
    {
        Erwarten(ServerZustand.WarteHello);
        _helloMs = jetztMs;
        switch (einstufung)
        {
            case Einstufung.Abgelehnt or Einstufung.Entzogen:
                return Wechseln(ServerZustand.Ende, jetztMs) with { Fehler = Fehlercode.GeraetGesperrt };
            case Einstufung.KontoGesperrt:
                return Wechseln(ServerZustand.Ende, jetztMs) with { Fehler = Fehlercode.KontoGesperrt };
        }
        if (clientZuAlt) return Wechseln(ServerZustand.Ende, jetztMs) with { Fehler = Fehlercode.VersionZuAlt };
        return einstufung switch
        {
            Einstufung.Wartet => Wechseln(ServerZustand.Warteraum, jetztMs) with { Welcome = WelcomeStatus.Wartet },
            Einstufung.Freigegeben => Wechseln(ServerZustand.AktivSync, jetztMs) with { Welcome = WelcomeStatus.Freigegeben, SyncBeginnen = true },
            _ => Wechseln(ServerZustand.Registrierung, jetztMs) with { Welcome = WelcomeStatus.Unbekannt },
        };
    }

    /// <summary>Client hat die Verknüpfung bestätigt und das Gerät steht auf WARTET: REGISTRIERUNG -> WARTERAUM.</summary>
    public Uebergang VerknuepfungBestaetigt(long jetztMs)
    {
        Erwarten(ServerZustand.Registrierung);
        return Wechseln(ServerZustand.Warteraum, jetztMs);
    }

    /// <summary>Admin hat freigegeben: WARTERAUM -> Welcome(FREIGEGEBEN) -> AKTIV_SYNC.</summary>
    public Uebergang Freigegeben(long jetztMs)
    {
        Erwarten(ServerZustand.Warteraum);
        return Wechseln(ServerZustand.AktivSync, jetztMs) with { Welcome = WelcomeStatus.Freigegeben, SyncBeginnen = true };
    }

    /// <summary>Admin hat abgelehnt: WARTERAUM -> ENDE.</summary>
    public Uebergang Abgelehnt(long jetztMs)
    {
        Erwarten(ServerZustand.Warteraum);
        return Wechseln(ServerZustand.Ende, jetztMs);
    }

    /// <summary>Gültige Antwort auf VoiceKeys(START): AKTIV_SYNC -> AKTIV.</summary>
    public Uebergang VoiceKeysBeantwortet(long jetztMs)
    {
        Erwarten(ServerZustand.AktivSync);
        return Wechseln(ServerZustand.Aktiv, jetztMs);
    }

    /// <summary>
    /// Beendet die Verbindung aus jedem Zustand: Sperre, Entzug, Entfernung, Verdrängung, Ersetzung,
    /// Protokollfehler oder Bye. <paramref name="fehler"/> bzw. <paramref name="bye"/> gehen vorher hinaus.
    /// </summary>
    public Uebergang Beenden(long jetztMs, Fehlercode? fehler = null, ByeGrund? bye = null)
    {
        if (Zustand == ServerZustand.Ende) return new Uebergang(ServerZustand.Ende, ServerZustand.Ende);
        return Wechseln(ServerZustand.Ende, jetztMs) with { Fehler = fehler, Bye = bye };
    }

    /// <summary>Prüft die Fristen aus 4.6. Kein Ablauf: Übergang ohne Änderung.</summary>
    public Uebergang Ticken(long jetztMs)
    {
        var imZustand = jetztMs - _eintrittMs;
        switch (Zustand)
        {
            case ServerZustand.Tls when imZustand >= Fristen.TlsMs:
            case ServerZustand.WarteHello when imZustand >= Fristen.HelloMs:
            case ServerZustand.Registrierung when jetztMs - _helloMs >= Fristen.RegistrierungMs:
            case ServerZustand.AktivSync when imZustand >= Fristen.SyncMs:
                return Wechseln(ServerZustand.Ende, jetztMs);
            case ServerZustand.Warteraum when imZustand >= Fristen.WarteraumMs:
                return Wechseln(ServerZustand.Ende, jetztMs) with { Bye = ByeGrund.Wartezeit, WiederInMs = Fristen.WartezeitWiederInMs };
        }
        if ((Zustand is ServerZustand.Registrierung or ServerZustand.Warteraum or ServerZustand.AktivSync or ServerZustand.Aktiv)
            && jetztMs - _letzterRahmenMs >= Fristen.RuheMs)
        {
            return Wechseln(ServerZustand.Ende, jetztMs);
        }
        return new Uebergang(Zustand, Zustand);
    }

    private Uebergang Wechseln(ServerZustand nach, long jetztMs)
    {
        var von = Zustand;
        Zustand = nach;
        _eintrittMs = jetztMs;
        return new Uebergang(von, nach);
    }

    private void Erwarten(ServerZustand soll)
    {
        if (Zustand != soll) throw new InvalidOperationException($"Ereignis nur in {soll}, Automat steht in {Zustand}.");
    }
}
