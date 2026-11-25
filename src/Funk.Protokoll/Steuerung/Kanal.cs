// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;

namespace Funk.Protokoll.Steuerung;

/// <summary>Was mit einem eingegangenen Rahmen geschieht.</summary>
public enum EingangsStatus : byte
{
    /// <summary>Gültig: <see cref="Eingang.Body"/> verarbeiten (binär: Rohbody verwenden).</summary>
    Annehmen,
    /// <summary>Still übergehen (unbekannter optionaler Typ, verworfener Tunnelrahmen).</summary>
    Uebergehen,
    /// <summary>
    /// Verstoß: bei <see cref="Eingang.Trennen"/> <c>Fehler</c> senden und trennen; sonst bei einer Anfrage mit
    /// <c>Fehler{code}</c> und derselben Anfrage-Nr antworten (UNGUELTIG ohne Trennung, 4.3).
    /// </summary>
    Fehler,
}

/// <summary>Ergebnis von <see cref="ServerKanal.Empfangen"/> bzw. <see cref="ClientKanal.Empfangen"/>.</summary>
/// <param name="Status">Annehmen, Übergehen oder Fehler.</param>
/// <param name="Fehler">Fehlercode bei <see cref="EingangsStatus.Fehler"/>.</param>
/// <param name="Feld">Feld des Verstoßes (für <c>Fehler.text</c>).</param>
/// <param name="Kopf">Kopf des Rahmens.</param>
/// <param name="Body">Gelesener JSON-Body oder <c>null</c> (binär).</param>
/// <param name="Bezug">Bei Antworten: die damit erledigte eigene Anfrage.</param>
public readonly record struct Eingang(
    EingangsStatus Status,
    Fehlercode Fehler,
    string? Feld,
    RahmenKopf Kopf,
    object? Body,
    OffeneAnfrage? Bezug)
{
    /// <summary>Verstoß mit Trennung (4.9).</summary>
    public bool Trennen => Status == EingangsStatus.Fehler && Fehlercodes.Trennt(Fehler);

    internal static Eingang Mit(Fehlercode code, in RahmenKopf kopf, string? feld = null) =>
        new(EingangsStatus.Fehler, code, feld, kopf, null, null);

    internal static Eingang Uebergehen(in RahmenKopf kopf) => new(EingangsStatus.Uebergehen, default, null, kopf, null, null);
}

/// <summary>
/// Steuerkanal einer Verbindung im Server als reines Modell: Zustandsautomat (4.6), Anfrage-Nummern (4.2) und
/// Body-Prüfung (4.3, 4.5) in einem Schritt je Rahmen. Ohne Netzwerk, Zeit von außen. Nicht threadsicher.
/// </summary>
public sealed class ServerKanal
{
    public ServerKanal(long jetztMs) => Automat = new ServerAutomat(jetztMs);

    public ServerAutomat Automat { get; }

    /// <summary>Eigene Anfragen des Servers (VoiceKeys, VerknuepfungBestaetigen).</summary>
    public EigeneAnfragen Eigene { get; } = new(Seite.Server);

    /// <summary>Anfragen des Clients.</summary>
    public FremdeAnfragen Fremde { get; } = new(Seite.Client);

    /// <summary>Sitzungs-ID dieser Verbindung (ab AKTIV_SYNC); Tunnelrahmen müssen sie tragen.</summary>
    public uint SitzungsId { get; set; }

    /// <summary>Letzter Übergang, den ein Rahmen ausgelöst hat (Bye, Protokollfehler, VoiceKeys-Antwort).</summary>
    public Uebergang? LetzterUebergang { get; private set; }

    /// <summary>Grenze für das Längenfeld des nächsten Rahmens.</summary>
    public int Rahmengrenze => Automat.Rahmengrenze;

    /// <summary>Prüft einen Rahmen, dessen Kopf schon gegen <see cref="Rahmengrenze"/> geprüft wurde.</summary>
    public Eingang Empfangen(in RahmenKopf kopf, ReadOnlySequence<byte> body, long jetztMs)
    {
        if (body.IsSingleSegment) return Empfangen(kopf, body.FirstSpan, jetztMs);
        var miete = ArrayPool<byte>.Shared.Rent((int)body.Length);
        try
        {
            body.CopyTo(miete);
            return Empfangen(kopf, miete.AsSpan(0, (int)body.Length), jetztMs);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(miete, clearArray: true);
        }
    }

    /// <summary>Prüft einen Rahmen, dessen Kopf schon gegen <see cref="Rahmengrenze"/> geprüft wurde.</summary>
    public Eingang Empfangen(in RahmenKopf kopf, ReadOnlySpan<byte> body, long jetztMs)
    {
        LetzterUebergang = null;
        Automat.RahmenEmpfangen(jetztMs);
        var e = Pruefen(kopf, body, jetztMs);
        if (e.Trennen) LetzterUebergang = Automat.Beenden(jetztMs, e.Fehler);
        return e;
    }

    private Eingang Pruefen(in RahmenKopf kopf, ReadOnlySpan<byte> body, long jetztMs)
    {
        var art = kopf.Art;
        // 4.5: Hello ist der erste Rahmen nach TLS; 4.6 WARTE_HELLO: "etwas anderes -> ENDE". Das gilt auch für
        // unbekannte optionale Typen, denn vor Hello ist keine Nebenversion vereinbart (4.7).
        var vorHello = Automat.Zustand == ServerZustand.WarteHello;
        if (vorHello && (kopf.Typ != (ushort)Nachrichtentyp.Hello || art != Nachrichtenart.Push)) return Eingang.Mit(Fehlercode.Protokoll, kopf);
        var eintrag = Katalog.Finden(kopf.Typ);
        var verwendung = eintrag?.Fuer(Seite.Client, art) ?? default;
        if (eintrag is null || !verwendung.Erlaubt)
        {
            return Typbereiche.IstOptional(kopf.Typ) ? Eingang.Uebergehen(kopf) : Eingang.Mit(Fehlercode.Protokoll, kopf);
        }
        if (!Automat.ClientNachrichtErlaubt(eintrag.Typ, art)) return Eingang.Mit(Fehlercode.Protokoll, kopf);

        OffeneAnfrage? bezug = null;
        if (art == Nachrichtenart.Anfrage && Fremde.Annehmen(kopf.AnfrageNr) is { } f) return Eingang.Mit(f, kopf);
        if (art == Nachrichtenart.Antwort)
        {
            if (!Eigene.VersucheAufloesen(kopf.AnfrageNr, kopf.Typ, out var offen)) return Eingang.Mit(Fehlercode.Protokoll, kopf);
            bezug = offen;
        }

        switch (eintrag.Typ)
        {
            case Nachrichtentyp.SpracheTunnel:
                return SpracheTunnel.Pruefen(body, SitzungsId, hoch: true) switch
                {
                    TunnelPruefung.Weiter => new Eingang(EingangsStatus.Annehmen, default, null, kopf, null, null),
                    TunnelPruefung.Verwerfen => Eingang.Uebergehen(kopf),
                    _ => Eingang.Mit(Fehlercode.Protokoll, kopf),
                };
            case Nachrichtentyp.VoiceKeys:
                if (VoiceKeys.AntwortPruefen(body, bezug!.Value.Kontext) is { } fk) return Eingang.Mit(fk, kopf);
                if (Automat.Zustand == ServerZustand.AktivSync) LetzterUebergang = Automat.VoiceKeysBeantwortet(jetztMs);
                return new Eingang(EingangsStatus.Annehmen, default, null, kopf, null, bezug);
        }

        var ergebnis = SteuerJson.Lesen(body, verwendung.Body!);
        if (!ergebnis.Ok)
        {
            // Ein ungültiges Hello ist "etwas anderes" als Hello (4.6): ohne gültiges Hello gibt es keinen Folgezustand,
            // und ein Push lässt sich nicht mit UNGUELTIG beantworten.
            var code = vorHello && !Fehlercodes.Trennt(ergebnis.Fehler!.Value) ? Fehlercode.Protokoll : ergebnis.Fehler!.Value;
            return new Eingang(EingangsStatus.Fehler, code, ergebnis.Feld, kopf, null, bezug);
        }

        if (eintrag.Typ == Nachrichtentyp.Bye)
        {
            LetzterUebergang = Automat.Beenden(jetztMs);
        }
        else if (eintrag.Typ == Nachrichtentyp.Fehler && bezug is { Typ: Nachrichtentyp.VoiceKeys })
        {
            // Der Client konnte die Schlüssel nicht installieren: ohne Schlüssel keine Sitzung.
            LetzterUebergang = Automat.Beenden(jetztMs);
        }
        return new Eingang(EingangsStatus.Annehmen, default, null, kopf, ergebnis.Body, bezug);
    }

    /// <summary>Stellt eine VoiceKeys-Anfrage (Frist 10 s) und liefert ihre Anfrage-Nr.</summary>
    public uint VoiceKeysStellen(byte epoche, long jetztMs) =>
        Eigene.VersucheStellen(Nachrichtentyp.VoiceKeys, jetztMs, Fristen.VoiceKeysMs, epoche, out var nr)
            ? nr
            : throw new InvalidOperationException("Zu viele offene Server-Anfragen.");

    /// <summary>Stellt eine VerknuepfungBestaetigen-Anfrage (Frist 120 s) und liefert ihre Anfrage-Nr.</summary>
    public uint VerknuepfungStellen(long jetztMs) =>
        Eigene.VersucheStellen(Nachrichtentyp.VerknuepfungBestaetigen, jetztMs, Fristen.VerknuepfungMs, 0, out var nr)
            ? nr
            : throw new InvalidOperationException("Zu viele offene Server-Anfragen.");

    /// <summary>Die Antwort auf eine Client-Anfrage ist hinausgegangen.</summary>
    public bool AntwortGesendet(uint anfrageNr) => Fremde.Beantwortet(anfrageNr);

    /// <summary>
    /// Fristen: Zustandsfristen aus 4.6, dazu Server-Anfragen: VoiceKeys ohne Antwort trennt; VerknuepfungBestaetigen
    /// ohne Antwort gilt als "nein" und wird in <paramref name="abgelaufen"/> gemeldet.
    /// </summary>
    public Uebergang Ticken(long jetztMs, out OffeneAnfrage? abgelaufen)
    {
        abgelaufen = null;
        if (Eigene.NaechsteAbgelaufene(jetztMs, out var a))
        {
            abgelaufen = a;
            if (a.Typ == Nachrichtentyp.VoiceKeys) return Automat.Beenden(jetztMs);
        }
        return Automat.Ticken(jetztMs);
    }
}

/// <summary>
/// Steuerkanal auf Seite des Clients als reines Modell: Zustandsautomat (4.6), Anfrage-Nummern (4.2) und
/// Body-Prüfung. Nicht threadsicher.
/// </summary>
public sealed class ClientKanal
{
    public ClientAutomat Automat { get; } = new();

    /// <summary>Eigene Anfragen des Clients.</summary>
    public EigeneAnfragen Eigene { get; private set; } = new(Seite.Client);

    /// <summary>Server-Anfragen (VoiceKeys, VerknuepfungBestaetigen).</summary>
    public FremdeAnfragen Fremde { get; private set; } = new(Seite.Server);

    /// <summary>Sitzungs-ID aus Welcome; Tunnelrahmen runter müssen sie tragen.</summary>
    public uint SitzungsId { get; set; }

    /// <summary>Letzter Übergang, den ein Rahmen ausgelöst hat.</summary>
    public ClientUebergang? LetzterUebergang { get; private set; }

    /// <summary>Grenze für das Längenfeld eingehender Rahmen.</summary>
    public static int Rahmengrenze => Rahmengrenzen.ClientEmpfang;

    /// <summary>Neue Verbindung: Anfragebücher zurücksetzen, Automat nach VERBINDEN.</summary>
    public ClientUebergang Verbinden(long jetztMs, bool vomNutzer = false)
    {
        Eigene = new EigeneAnfragen(Seite.Client);
        Fremde = new FremdeAnfragen(Seite.Server);
        SitzungsId = 0;
        return Automat.Verbinden(jetztMs, vomNutzer);
    }

    /// <summary>Vergibt eine Anfrage-Nr für eine eigene Anfrage; <c>false</c> bei 32 offenen.</summary>
    public bool VersucheAnfrageStellen(Nachrichtentyp typ, long jetztMs, out uint anfrageNr) =>
        Eigene.VersucheStellen(typ, jetztMs, 0, 0, out anfrageNr);

    /// <summary>Die Antwort auf eine Server-Anfrage ist hinausgegangen.</summary>
    public bool AntwortGesendet(uint anfrageNr) => Fremde.Beantwortet(anfrageNr);

    /// <summary>Prüft einen Rahmen vom Server.</summary>
    public Eingang Empfangen(in RahmenKopf kopf, ReadOnlySpan<byte> body, long jetztMs)
    {
        LetzterUebergang = null;
        var e = Pruefen(kopf, body, jetztMs);
        if (e.Trennen) LetzterUebergang = Automat.Verbindungsverlust(jetztMs);
        return e;
    }

    private Eingang Pruefen(in RahmenKopf kopf, ReadOnlySpan<byte> body, long jetztMs)
    {
        var art = kopf.Art;
        // Vor Welcome antwortet der Server auf Hello nur mit Welcome, Fehler oder Bye (4.5, 4.6); vor der Vereinbarung
        // der Nebenversion gibt es auch keine optionalen Typen. Alles andere, auch ein ungültiges Welcome, ließe den
        // Client ohne Folgezustand in VERBINDEN stehen.
        var vorWelcome = Automat.Zustand == ClientZustand.Verbinden;
        if (vorWelcome && (art != Nachrichtenart.Push
                || (Nachrichtentyp)kopf.Typ is not (Nachrichtentyp.Welcome or Nachrichtentyp.Fehler or Nachrichtentyp.Bye)))
        {
            return Eingang.Mit(Fehlercode.Protokoll, kopf);
        }
        var eintrag = Katalog.Finden(kopf.Typ);
        var verwendung = eintrag?.Fuer(Seite.Server, art) ?? default;
        if (eintrag is null || !verwendung.Erlaubt)
        {
            return Typbereiche.IstOptional(kopf.Typ) ? Eingang.Uebergehen(kopf) : Eingang.Mit(Fehlercode.Protokoll, kopf);
        }
        if ((eintrag.Zustaende & Automat.Empfangszustaende) == 0) return Eingang.Mit(Fehlercode.Protokoll, kopf);

        OffeneAnfrage? bezug = null;
        if (art == Nachrichtenart.Anfrage && Fremde.Annehmen(kopf.AnfrageNr) is { } f) return Eingang.Mit(f, kopf);
        if (art == Nachrichtenart.Antwort)
        {
            if (!Eigene.VersucheAufloesen(kopf.AnfrageNr, kopf.Typ, out var offen)) return Eingang.Mit(Fehlercode.Protokoll, kopf);
            bezug = offen;
        }

        switch (eintrag.Typ)
        {
            case Nachrichtentyp.SpracheTunnel:
                return SpracheTunnel.Pruefen(body, SitzungsId, hoch: false) switch
                {
                    TunnelPruefung.Weiter => new Eingang(EingangsStatus.Annehmen, default, null, kopf, null, null),
                    TunnelPruefung.Verwerfen => Eingang.Uebergehen(kopf),
                    _ => Eingang.Mit(Fehlercode.Protokoll, kopf),
                };
            case Nachrichtentyp.VoiceKeys:
                return VoiceKeys.AnfragePruefen(body) is { } fk
                    ? Eingang.Mit(fk, kopf)
                    : new Eingang(EingangsStatus.Annehmen, default, null, kopf, null, null);
        }

        var ergebnis = SteuerJson.Lesen(body, verwendung.Body!);
        if (!ergebnis.Ok)
        {
            var code = vorWelcome && !Fehlercodes.Trennt(ergebnis.Fehler!.Value) ? Fehlercode.Protokoll : ergebnis.Fehler!.Value;
            return new Eingang(EingangsStatus.Fehler, code, ergebnis.Feld, kopf, null, bezug);
        }

        switch (ergebnis.Body)
        {
            case Welcome w:
                if (w.SitzungsId is { } id) SitzungsId = id;
                LetzterUebergang = Automat.WelcomeEmpfangen(w.Status, jetztMs);
                break;
            case GeraetStatus g:
                LetzterUebergang = Automat.GeraetStatusEmpfangen(g.Status, jetztMs);
                break;
            case ZustandsVersionBody when art == Nachrichtenart.Push:
                LetzterUebergang = Automat.ZustandPushEmpfangen(jetztMs);
                break;
            case PingAntwort:
                Automat.PingAntwortEmpfangen(jetztMs);
                break;
            case Bye b:
                LetzterUebergang = Automat.ByeEmpfangen(b.Grund, b.WiederInMs, jetztMs);
                break;
            case Fehler fehler:
                LetzterUebergang = Automat.FehlerEmpfangen(fehler.Code, art == Nachrichtenart.Push, jetztMs);
                break;
        }
        return new Eingang(EingangsStatus.Annehmen, default, null, kopf, ergebnis.Body, bezug);
    }
}
