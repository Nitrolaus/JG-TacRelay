// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Eine aufgebaute Anbindung: der Client und was mit ihm zusammen freigegeben wird (Geräteschlüssel). <see cref="Profil"/>
/// nimmt nach der Freigabe ein neueres Serverprofil aus <c>Welcome.profil</c> an (F1); <see cref="ProfilNachziehen"/> gibt es
/// dem Verbindungsaufbau für den nächsten Versuch. Beides fehlt beim Testzugang.
/// </summary>
public sealed record FunkAnbindung(FunkClient Client, IDisposable? Besitz, string? Server, string? Geraet, string? Schluesselart)
{
    /// <summary>Übernahme neuerer Profile aus Welcome; <c>null</c> ohne Profil (Testzugang).</summary>
    public Profiluebernahme? Profil { get; init; }

    /// <summary>Gibt ein übernommenes Profil an den Aufbau weiter (<see cref="TlsAufbau.ProfilSetzen"/>).</summary>
    public Action<Protokoll.Identitaet.Serverprofil>? ProfilNachziehen { get; init; }

    /// <summary>
    /// Der Pin des Servers der laufenden Verbindung (<see cref="TlsAufbau.LetzterPin"/>). Gesetzt, übernimmt der Client nur ein
    /// Profil, das diesen Pin enthält (3.1); <c>null</c> nur ohne TLS (Tests).
    /// </summary>
    public Func<Protokoll.Identitaet.Fingerabdruck?>? ServerPin { get; init; }
}

/// <summary>
/// Was der Funkbetrieb vom Klang braucht: Empfänger der Sprache und Opus-Prüfung mit libopus (5.13) für die Optionen des
/// Clients, dazu das An- und Abbinden des Sprechkanals und die Nachricht, dass die Sendeauswahl lokal wechselte (C2).
/// Im Betrieb die <see cref="Klang.KlangBruecke"/>.
/// </summary>
public interface IKlangAnschluss : ISprachEmpfaenger, IOpusPruefer
{
    void Anbinden(FunkClient client);

    void Loesen(FunkClient client);

    void AuswahlGeaendert();
}

/// <summary>
/// <see cref="IFunkDienst"/> über <see cref="FunkClient"/> (Funk.ClientKern). Übersetzt Lagebild, Sendeauswahl,
/// Sprechanzeige und Registrierungscode in die <see cref="FunkLage"/> der Oberfläche und stellt die Anfragen. Die
/// Ereignisse des Clients kommen aus dessen Threads; die Lage wird dort gebaut und als Ganzes ausgetauscht. Ausnahme sind
/// Hintergrundfehler: Sie kommen auch aus dem Empfangs- und Sende-Thread der Sprache und gehen deshalb erst über den
/// Oberflächentakt (<see cref="HintergrundFehler"/>).
/// </summary>
public sealed class FunkClientDienst : IFunkDienst
{
    /// <summary>So lange steht ein Hintergrundfehler da, wenn keiner nachkommt und kein neuer Zustand ihn ablöst.</summary>
    public static readonly TimeSpan FehlerDauer = TimeSpan.FromSeconds(30);

    private readonly Func<FunkAnbindung> _fabrik;
    private readonly IKlangAnschluss? _klang;
    private readonly IOberflaechentakt _takt;
    private readonly TimeSpan _fehlerDauer;
    private readonly TimeProvider _zeit;
    private readonly Lock _sperre = new();
    private FunkAnbindung? _anbindung;
    private FunkLage _lage = FunkLage.Leer;
    private IReadOnlyList<SprechanzeigeEintrag> _sprechanzeige = [];
    private Registrierungsangabe? _registrierung;
    private IReadOnlyList<string> _vergeben = [];
    private Verbindungsmeldung? _hinweis;

    /// <summary>Die letzte Sitzung endete mit Bye{ENTFERNT}; erst eine Antwort des Servers hebt es auf (<see cref="FunkLage.Entfernt"/>).</summary>
    private bool _entfernt;
    private long _bauNummer;
    private long _uebernommen;

    // Hintergrundfehler: _fehlerOffen und _fehlerEingereiht schreibt jeder Thread ohne Sperre, den Rest nur unter _sperre.
    // _stand zählt jeden neuen Stand der Anzeige (Erfolg, Hinweis, Verbinden, Trennen); ein Fehler, der älter ist, gilt nicht mehr.
    private Hintergrundmeldung? _fehlerOffen;
    private int _fehlerEingereiht;
    private long _stand;
    private Verbindungsmeldung? _fehler;
    private long _fehlerSeit;
    private ITimer? _fehlerUhr;
    private bool _uhrLaeuft;

    /// <summary>Ein Hintergrundfehler, wie er im Thread des Clients ankam: Zustand und Stand der Anzeige in dem Moment.</summary>
    private sealed record Hintergrundmeldung(FunkClient Client, ClientZustand Zustand, string Meldung, long Stand);

    /// <param name="fabrik">
    /// Baut Client und Schlüssel; wirft eine <see cref="VorbereitungException"/> mit Satz und Einzelheit, wenn etwas fehlt (etwa das
    /// Serverprofil). Jede andere Ausnahme zeigt der Dienst als internen Fehler (<see cref="Verbindungstexte.Vorbereitung"/>).
    /// </param>
    /// <param name="klang">Klang für Senden und Empfangen; ohne ihn läuft nur der Protokollteil (Tests).</param>
    /// <param name="takt">
    /// Oberflächentakt, auf dem Hintergrundfehler in die Lage kommen. Ohne ihn (Tests) geschieht das sofort im Thread des
    /// Fehlers; im Programm ist es der Dispatcher, damit der Empfangs-Thread nie eine Lage baut.
    /// </param>
    /// <param name="fehlerDauer">Wie lange ein Hintergrundfehler ohne Nachschub stehen bleibt; ohne Angabe <see cref="FehlerDauer"/>.</param>
    /// <param name="zeit">Uhr für diese Frist (Tests).</param>
    public FunkClientDienst(Func<FunkAnbindung> fabrik, IKlangAnschluss? klang = null, IOberflaechentakt? takt = null, TimeSpan? fehlerDauer = null,
        TimeProvider? zeit = null)
    {
        ArgumentNullException.ThrowIfNull(fabrik);
        _fabrik = fabrik;
        _klang = klang;
        _takt = takt ?? SofortTakt.Instanz;
        _fehlerDauer = fehlerDauer ?? FehlerDauer;
        _zeit = zeit ?? TimeProvider.System;
    }

    public event Action? LageGeaendert;

    public FunkLage Lage
    {
        get
        {
            lock (_sperre) return _lage;
        }
    }

    private FunkClient? Client
    {
        get
        {
            lock (_sperre) return _anbindung?.Client;
        }
    }

    /// <summary>Der Client der laufenden Anbindung, für den Verwaltungsteil (<see cref="VerwaltungsWirt"/>); sonst <c>null</c>.</summary>
    public FunkClient? AktiverClient => Client;

    /// <summary>
    /// Verbindet. Steht schon ein Client ohne Sitzung da (etwa nach "Server nicht vertrauenswürdig" oder einem gesperrten
    /// Neuversuch), wird er abgebaut und aus den aktuellen Einstellungen neu gebaut: Ein inzwischen gewähltes Profil, ein
    /// neuer Profilschlüssel oder eine andere Adresse gelten so sofort, statt dass der alte Client mit dem alten Stand weiter
    /// probiert. Mit laufender Sitzung verbindet er nur neu.
    /// </summary>
    public async Task VerbindenAsync()
    {
        var vorhanden = Client;
        if (vorhanden is not null)
        {
            if (vorhanden.Zustand != ClientZustand.Getrennt)
            {
                vorhanden.NeuVerbinden();
                return;
            }
            await AbbauenAsync().ConfigureAwait(false);
        }
        FunkAnbindung a;
        try
        {
            a = _fabrik();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException
                                       or CryptographicException or FormatException or PlatformNotSupportedException)
        {
            lock (_sperre)
            {
                _hinweis = Verbindungstexte.Vorbereitung(ex);
                Abloesen();
            }
            Neu();
            return;
        }
        var c = a.Client;
        c.ZustandGeaendert += ZustandGeaendert;
        c.PushErhalten += PushErhalten;
        c.CodeErhalten += CodeErhalten;
        c.SitzungBeendet += SitzungBeendet;
        c.HintergrundFehler += HintergrundFehler;
        c.Sprechen.SendenetzGeaendert += SendenetzGeaendert;
        _klang?.Anbinden(c);
        lock (_sperre)
        {
            _anbindung = a;
            _hinweis = null;
            Abloesen();
        }
        c.Starten();
        Neu();
    }

    public async Task TrennenAsync()
    {
        if (await AbbauenAsync().ConfigureAwait(false))
        {
            lock (_sperre) _hinweis = new Verbindungsmeldung("Getrennt", null);
        }
        Neu();
    }

    /// <summary>Baut die Anbindung ab, ohne die Lage neu zu bauen. Liefert, ob eine da war.</summary>
    private async Task<bool> AbbauenAsync()
    {
        FunkAnbindung? a;
        lock (_sperre)
        {
            a = _anbindung;
            _anbindung = null;
            _sprechanzeige = [];
            _registrierung = null;
            _vergeben = [];
            Abloesen();
        }
        if (a is null) return false;
        _klang?.Loesen(a.Client);
        a.Client.ZustandGeaendert -= ZustandGeaendert;
        a.Client.PushErhalten -= PushErhalten;
        a.Client.CodeErhalten -= CodeErhalten;
        a.Client.SitzungBeendet -= SitzungBeendet;
        a.Client.HintergrundFehler -= HintergrundFehler;
        a.Client.Sprechen.SendenetzGeaendert -= SendenetzGeaendert;
        await a.Client.DisposeAsync().ConfigureAwait(false);
        a.Besitz?.Dispose();
        return true;
    }

    public Task<string?> HoerenSetzenAsync(ushort netzId, bool hoeren) => AnfrageAsync(c => c.HoerenSetzenAsync(netzId, hoeren));

    public async Task<string?> SendeauswahlSetzenAsync(IReadOnlyList<byte> felder, bool mitGlobal)
    {
        ArgumentNullException.ThrowIfNull(felder);
        // Der Client setzt die Auswahl lokal, bevor er auf den Server wartet; ein laufender Spruch schaltet sofort um (C2).
        var anfrage = AnfrageAsync(c => c.SendeauswahlSetzenAsync(felder, mitGlobal));
        _klang?.AuswahlGeaendert();
        // Die Auswahl gilt lokal sofort (C1), auch wenn die Meldung an den Server wartet (C5) oder scheitert: Die Lage
        // zeigt sie gleich, nicht erst mit der Antwort.
        Neu();
        var ergebnis = await anfrage.ConfigureAwait(false);
        Neu();
        return ergebnis;
    }

    public Task<string?> EinsteigenAsync(ushort rolleId, ushort? schiffId) =>
        AnfrageAsync(c => c.AnfrageAsync(Nachrichtentyp.EinsteigenRolleSchiff, new EinsteigenRolleSchiffAnfrage { RolleId = rolleId, SchiffId = schiffId }));

    public Task<string?> PegelSetzenAsync(uint kontoId, float db)
    {
        // 4.5: -60 bis +12 dB in Schritten von 0,5; Netz 0 = alle Netze.
        var gerundet = Math.Clamp(Math.Round(db * 2.0, MidpointRounding.AwayFromZero) / 2.0, -60.0, 12.0);
        return AnfrageAsync(c => c.PegelSetzenAsync(kontoId, 0, gerundet));
    }

    public Task<Dienstantwort<RollenkatalogAntwort>> RollenkatalogAsync() =>
        LesenAsync<RollenkatalogAntwort, RollenkatalogAnfrage>(Nachrichtentyp.Rollenkatalog, new RollenkatalogAnfrage { Op = RegelOperation.Lesen });

    public Task<string?> RollenkatalogErgaenzenAsync(IReadOnlyList<Rollenkatalogeintrag> eintraege, long version) =>
        AnfrageAsync(c => c.AnfrageAsync(Nachrichtentyp.Rollenkatalog, new RollenkatalogAnfrage { Op = RegelOperation.Setzen, Eintraege = eintraege, Version = version }));

    public Task<Dienstantwort<MitgliedskarteAntwort>> MitgliedskarteAsync(uint kontoId) =>
        LesenAsync<MitgliedskarteAntwort, MitgliedskarteAnfrage>(Nachrichtentyp.Mitgliedskarte, new MitgliedskarteAnfrage { KontoId = kontoId });

    public Task<string?> LieblingsrollenSetzenAsync(IReadOnlyList<ushort> rollen, ushort? ersteWahl) =>
        AnfrageAsync(c => c.AnfrageAsync(Nachrichtentyp.Lieblingsrollen, new LieblingsrollenAnfrage { Rollen = rollen, ErsteWahl = ersteWahl }));

    public Task<string?> ProfilSetzenAsync(ProfilSetzenAnfrage anfrage)
    {
        ArgumentNullException.ThrowIfNull(anfrage);
        return AnfrageAsync(c => c.AnfrageAsync(Nachrichtentyp.ProfilSetzen, anfrage));
    }

    public Task<string?> WieLetztesMalAsync()
    {
        var c = Client;
        if (c?.Lage.Mitgliedschaften?.LetzterStand is not { } ls) return Task.FromResult<string?>(c is null ? "Nicht verbunden." : "Es gibt noch keinen letzten Platz.");
        var satz = new SatzAnwendenAnfrage
        {
            RolleId = ls.RolleId,
            SchiffId = ls.SchiffId,
            Felder = [.. ls.Felder.Select(f => new FeldBelegung { Feld = f.Feld, NetzId = f.NetzId, Hoeren = f.Hoeren, Senden = f.Senden })],
            Sendeauswahl = ls.Sendeauswahl,
        };
        return AnfrageAsync(k => k.AnfrageAsync(Nachrichtentyp.SatzAnwenden, satz));
    }

    public Task<Dienstantwort<VerschiebenAntwort>> VerschiebenAsync(IReadOnlyList<Verschiebung> aenderungen, long? basisVersion = null)
    {
        ArgumentNullException.ThrowIfNull(aenderungen);
        return LesenAsync<VerschiebenAntwort, VerschiebenAnfrage>(Nachrichtentyp.Verschieben, new VerschiebenAnfrage { Aenderungen = aenderungen, BasisVersion = basisVersion });
    }

    public Task<string?> StummschaltenAsync(uint kontoId, ushort netzId, ushort sekunden) =>
        AnfrageAsync(c => c.AnfrageAsync(Nachrichtentyp.Stummschalten, new StummschaltenAnfrage { KontoId = kontoId, NetzId = netzId, Sekunden = sekunden }));

    public async Task<Dienstantwort<ushort>> PlaetzeFreigebenAsync()
    {
        var a = await LesenAsync<PlaetzeFreigebenAntwort, LeererBody>(Nachrichtentyp.PlaetzeFreigeben, new LeererBody()).ConfigureAwait(false);
        return a.Ok ? Dienstantwort.Gut(a.Wert!.Anzahl) : Dienstantwort.Schlecht<ushort>(a.Fehler!, a.Code);
    }

    public Task<string?> SelbstwahlSetzenAsync(bool an, bool nachzuegler = true)
    {
        var c = Client;
        var version = c?.Lage.NetzListe?.Version ?? 0;
        var objekt = SteuerJson.Element(new SelbstwahlObjekt { An = an, Nachzuegler = an ? null : nachzuegler });
        return AnfrageAsync(k => k.AnfrageAsync(Nachrichtentyp.Verwaltung, new VerwaltungAnfrage { Op = VerwaltungsOperation.Selbstwahl, Objekt = objekt, Version = version }));
    }

    public async Task<Dienstantwort<IReadOnlyList<VerzeichnisEintrag>>> KontenVerzeichnisAsync()
    {
        var a = await LesenAsync<KontenVerzeichnisAntwort, LeererBody>(Nachrichtentyp.KontenVerzeichnis, new LeererBody()).ConfigureAwait(false);
        return a.Ok ? Dienstantwort.Gut(a.Wert!.Konten) : Dienstantwort.Schlecht<IReadOnlyList<VerzeichnisEintrag>>(a.Fehler!, a.Code);
    }

    public Task<string?> AuftragAbgebenAsync() =>
        AnfrageAsync(c => c.AnfrageAsync(Nachrichtentyp.Beauftragung, new BeauftragungAnfrage { Op = BeauftragungOperation.Abgeben }));

    public Task<Dienstantwort<FunkermodusAntwort>> FunkermodusAsync(bool an) =>
        LesenAsync<FunkermodusAntwort, FunkermodusAnfrage>(Nachrichtentyp.Funkermodus, new FunkermodusAnfrage { An = an });

    public Task<Dienstantwort<PhaseSchaltenAntwort>> PhaseSchaltenAsync(byte nr, byte erwartet) =>
        LesenAsync<PhaseSchaltenAntwort, PhaseSchaltenAnfrage>(Nachrichtentyp.PhaseSchalten, new PhaseSchaltenAnfrage { Nr = nr, Erwartet = erwartet });

    public Task<string?> FeldSetzenAsync(byte feld, ushort? netzId) => AnfrageAsync(c => c.FeldSetzenAsync(feld, netzId));

    public Task<string?> RolleWaehlenAsync(ushort rolleId, IReadOnlyList<ChWunsch> ch)
    {
        ArgumentNullException.ThrowIfNull(ch);
        return AnfrageAsync(c => c.RolleWaehlenAsync(rolleId, ch.Count == 0 ? null : ch));
    }

    public Task<string?> KanalSperreAsync(uint kontoId, ushort netzId, bool sperren) =>
        AnfrageAsync(c => c.KanalSperreAsync(sperren ? KanalSperreOperation.Setzen : KanalSperreOperation.Aufheben, kontoId, netzId));

    public async Task<Dienstantwort<IReadOnlyList<KanalsperreInfo>>> SperrenFuerAlleAsync()
    {
        var a = await LesenAsync<KanalSperreAntwort, KanalSperreAnfrage>(Nachrichtentyp.KanalSperre, new KanalSperreAnfrage { Op = KanalSperreOperation.Liste })
            .ConfigureAwait(false);
        if (!a.Ok) return Dienstantwort.Schlecht<IReadOnlyList<KanalsperreInfo>>(a.Fehler!, a.Code);
        return Dienstantwort.Gut<IReadOnlyList<KanalsperreInfo>>([.. a.Wert!.Sperren.Where(s => s.KontoId is null or 0)
            .Select(s => new KanalsperreInfo(s.NetzId, DateTimeOffset.FromUnixTimeMilliseconds(s.Bis), s.Durch, true))]);
    }

    public Task<string?> MeldungSendenAsync(MeldungSendenAnfrage anfrage)
    {
        ArgumentNullException.ThrowIfNull(anfrage);
        return MeldeAnfrageAsync(c => c.AnfrageAsync(Nachrichtentyp.MeldungSenden, anfrage));
    }

    public Task<string?> BereitMeldenAsync(uint nummer) =>
        MeldeAnfrageAsync(c => c.AnfrageAsync(Nachrichtentyp.BereitMelden, new BereitMeldenAnfrage { Nummer = nummer }));

    public long ServerJetztMs() => Client?.Uhr.ServerJetztMs() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Wie <see cref="AnfrageAsync"/>, aber mit den Sätzen des Meldesystems (<see cref="Meldefehler"/>).</summary>
    private async Task<string?> MeldeAnfrageAsync(Func<FunkClient, Task<Antwort>> anfrage)
    {
        var c = Client;
        if (c is null) return "Nicht verbunden.";
        try
        {
            var a = await anfrage(c).ConfigureAwait(false);
            return a.Ok ? null : Meldefehler(a.Code, a.Fehler?.Text, Lage);
        }
        catch (VerbindungGetrenntException)
        {
            return "Verbindung getrennt.";
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or InvalidOperationException or ArgumentException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Fehler des Meldesystems als Satz: GRENZE heißt "noch keine 2 s seit der letzten Meldung"; bei UNGUELTIG, UNBEKANNT und KEIN_RECHT
    /// sagt der Server selbst, was nicht passt (etwa "Die Zielzeit ist schon vorbei"); sonst wie <see cref="Fehlertext"/>.
    /// </summary>
    internal static string Meldefehler(Fehlercode? code, string? text, FunkLage? lage = null) => code switch
    {
        Fehlercode.Grenze => "Höchstens eine Meldung in 2 Sekunden, kurz warten.",
        Fehlercode.Ungueltig or Fehlercode.Unbekannt or Fehlercode.KeinRecht when text is { Length: > 0 } t => t.EndsWith('.') ? t : t + ".",
        _ => Fehlertext(code, lage, text),
    };

    public event Action<FunkEreignis>? Ereignis;

    /// <summary>C4 aus dem Sprechkanal (Leseschleife): Ziele, die die laufende Sendung verlor, als Ereignis für die Meldezeile.</summary>
    private void SendenetzGeaendert(object? sender, SendenetzGeaendert s)
    {
        var weg = s.Vorher.Netze.Where(n => s.Jetzt?.Netze.Contains(n) != true).ToList();
        if (weg.Count > 0) Ereignis?.Invoke(new SendenetzEntzogen(weg, s.Durch));
    }

    /// <summary>Eine lesende Anfrage mit Antwort vom Typ <typeparamref name="T"/>.</summary>
    private async Task<Dienstantwort<T>> LesenAsync<T, TAnfrage>(Nachrichtentyp typ, TAnfrage body) where T : class where TAnfrage : class
    {
        var c = Client;
        if (c is null) return Dienstantwort.Schlecht<T>("Nicht verbunden.");
        try
        {
            var a = await c.AnfrageAsync(typ, body).ConfigureAwait(false);
            return a.Ok && a.Body is T t
                ? Dienstantwort.Gut(t)
                : Dienstantwort.Schlecht<T>(Fehlertext(a.Code, Lage, a.Fehler?.Text), a.Code) with { Details = a.Fehler?.Details };
        }
        catch (VerbindungGetrenntException)
        {
            return Dienstantwort.Schlecht<T>("Verbindung getrennt.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or InvalidOperationException or ArgumentException)
        {
            return Dienstantwort.Schlecht<T>(ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await TrennenAsync().ConfigureAwait(false);
        lock (_sperre)
        {
            _fehlerUhr?.Dispose();
            _fehlerUhr = null;
            _uhrLaeuft = false;
        }
    }

    private async Task<string?> AnfrageAsync(Func<FunkClient, Task<Antwort>> anfrage)
    {
        var c = Client;
        if (c is null) return "Nicht verbunden.";
        try
        {
            var a = await anfrage(c).ConfigureAwait(false);
            if (a.Ok) return null;
            return Sperrfehler(a.Code, a.Fehler?.Details, Lage) ?? Fehlertext(a.Code, Lage, a.Fehler?.Text);
        }
        catch (VerbindungGetrenntException)
        {
            return "Verbindung getrennt.";
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or InvalidOperationException or ArgumentException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Fehlercode als Satz für die Anzeige. Mit <paramref name="lage"/> nennt SELBSTWAHL_GESPERRT, wer gesperrt hat (F9,
    /// RAIDLEITUNG-ENTWURF Teilnehmersicht: Grund statt grauem Knopf). VERSION mit <paramref name="text"/>: Der Client hat eine
    /// Anfrage nicht geschickt, weil der Server sie nach seiner Fassung noch nicht kennt (FunkClient.AnfrageAsync); der Satz sagt es.
    /// </summary>
    internal static string Fehlertext(Fehlercode? code, FunkLage? lage = null, string? text = null) => code switch
    {
        null => "Unbekannter Fehler.",
        Fehlercode.Version => text is { Length: > 0 } t ? t : "Client und Server passen in der Fassung nicht zusammen. Erst den Server aktualisieren.",
        Fehlercode.KeinRecht => "Dafür fehlt das Recht.",
        Fehlercode.PlatzBelegt => "Der Platz ist schon belegt.",
        Fehlercode.SelbstwahlGesperrt => Sperrtext(lage),
        Fehlercode.FeldFest => "Festes Feld, nur die Einsatzleitung ändert es.",
        Fehlercode.GlobalPflicht => "Global lässt sich nicht abschalten.",
        Fehlercode.Veraltet => "Der Stand hat sich inzwischen geändert; bitte noch einmal.",
        Fehlercode.Grenze => "Zu viele Anfragen, kurz warten.",
        Fehlercode.Unbekannt => "Das gibt es nicht (mehr).",
        Fehlercode.NameVergeben => "Den Namen hat schon jemand. Nimm einen anderen oder sprich mit dem Admin.",
        Fehlercode.Ungueltig => "Die Angaben passen nicht (Länge oder Zeichen).",
        Fehlercode.AdminSitzungNoetig => "Das geht nur in der Verwaltung mit Admin-Sitzung.",
        { } c => "Abgelehnt: " + Fehlercodes.Drahtname(c),
    };

    /// <summary>
    /// KEIN_RECHT wegen einer Kanalsperre (6.17, <c>details: {grund: "GESPERRT", durch, bis}</c>) als Satz: "Gesperrt von Nordlicht bis
    /// 04:00"; sonst <c>null</c>.
    /// </summary>
    internal static string? Sperrfehler(Fehlercode? code, System.Text.Json.JsonElement? details, FunkLage lage)
    {
        if (code != Fehlercode.KeinRecht || details is not { ValueKind: System.Text.Json.JsonValueKind.Object } d) return null;
        if (!d.TryGetProperty("grund", out var g) || g.ValueKind != System.Text.Json.JsonValueKind.String || g.GetString() != "GESPERRT") return null;
        var durch = d.TryGetProperty("durch", out var du) && du.TryGetUInt32(out var k) ? k : 0u;
        var bis = d.TryGetProperty("bis", out var b) && b.TryGetInt64(out var ms) ? ms : 0L;
        return lage.SperrSatz(new KanalsperreInfo(0, DateTimeOffset.FromUnixTimeMilliseconds(bis), durch, false)) + ".";
    }

    /// <summary>
    /// Warum Einsteigen gerade nicht geht (6.9, F9): wer gesperrt hat und was man tun kann, statt eines grauen Knopfs ohne Grund
    /// (RAIDLEITUNG-ENTWURF, Vor dem Raid: jeder steigt selbst ein).
    /// </summary>
    internal static string Sperrtext(FunkLage? lage)
    {
        var wer = lage?.SelbstwahlDurch is { } d and not 0 ? " (" + lage.HauptnameVon(d) + ")" : "";
        return lage is { Nachzuegler: false }
            ? "Einsteigen ganz gesperrt durch die Einsatzleitung" + wer + " - melde dich bei ihr."
            : "Wechsel gesperrt durch die Einsatzleitung" + wer + " - melde dich bei ihr.";
    }

    private void ZustandGeaendert(object? sender, Zustandswechsel w)
    {
        var aktivJetzt = w.Nach is ClientZustand.AktivUdpBinden or ClientZustand.AktivUdp or ClientZustand.AktivTunnel;
        var aktivVorher = w.Von is ClientZustand.AktivUdpBinden or ClientZustand.AktivUdp or ClientZustand.AktivTunnel;
        if (aktivJetzt && !aktivVorher && sender is FunkClient c) _ = AboAsync(c);
        // Erfolg: Der Server hat geantwortet (Anmeldung, Warteraum, Abgleich) oder die Sitzung ist AKTIV geworden. Wechsel
        // innerhalb von AKTIV (UDP und Tunnel) lösen einen Hintergrundfehler nicht ab.
        var erfolg = w.Nach is not (ClientZustand.Getrennt or ClientZustand.Verbinden) && !(aktivJetzt && aktivVorher);
        lock (_sperre)
        {
            if (w.Nach != ClientZustand.Registrierung) _registrierung = null;
            if (!aktivJetzt) _sprechanzeige = [];
            // Der Server hat geantwortet: Der Grund des letzten gescheiterten Versuchs gilt nicht mehr, auch "entfernt" nicht.
            if (w.Nach is not (ClientZustand.Getrennt or ClientZustand.Verbinden))
            {
                _hinweis = null;
                _entfernt = false;
            }
            if (erfolg) Abloesen();
        }
        Neu();
    }

    private static async Task AboAsync(FunkClient c)
    {
        try
        {
            await c.SprechanzeigeAboAsync(true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is VerbindungGetrenntException or OperationCanceledException or TimeoutException or InvalidOperationException)
        {
            // Beim nächsten AKTIV wird neu abonniert.
        }
    }

    private void PushErhalten(object? sender, PushEreignis p)
    {
        // F1: Nach der Freigabe reicht der Server das offline signierte Profil durch; ein neueres gilt ab dem nächsten Versuch.
        if (p.Body is Welcome { Status: WelcomeStatus.Freigegeben } w && sender is FunkClient wc)
        {
            FunkAnbindung? a;
            lock (_sperre) a = _anbindung;
            if (a is { Profil: { } uebernahme } && ReferenceEquals(a.Client, wc)
                && (a.ServerPin is { } pin ? uebernahme.Annehmen(w.Profil, pin()) : uebernahme.Annehmen(w.Profil)) is { Urteil: Uebernahmeurteil.Uebernommen, Profil: { } neu })
                a.ProfilNachziehen?.Invoke(neu);
        }
        if (p.Body is Sprechanzeige s)
        {
            lock (_sperre) _sprechanzeige = s.Aktiv;
        }
        // ZIEL_ENTZOGEN (6.3): Pakete an ein Netz, in dem das Senderecht inzwischen fehlt; die Meldezeile sagt es (bisher nirgends ausgewertet).
        if (p.Body is SendeFehler sf)
        {
            var entzogen = sf.Fehler.Where(f => f.Code == SendeFehlerCode.ZielEntzogen && f.NetzId is not null).ToList();
            if (entzogen.Count > 0)
                Ereignis?.Invoke(new SendenetzEntzogen([.. entzogen.Select(f => f.NetzId!.Value).Distinct()], entzogen.FirstOrDefault(f => f.Durch is not null)?.Durch));
        }
        // Der eigene Antrag im Warteraum (3.4, F1): Welche Namen schon ein freigegebenes Konto trägt.
        if (p.Body is GeraetStatus gs && sender is FunkClient c && string.Equals(gs.Fingerabdruck, c.Geraet.AlsBase64(), StringComparison.Ordinal))
        {
            lock (_sperre) _vergeben = gs.Status == GeraeteStatus.Wartet ? gs.ScNamenVergeben ?? [] : [];
        }
        Neu();
        // Meldesystem (0.12.0): erst die Lage mit dem neuen Stand, dann das Ereignis mit dem Befehl (Overlay, Ton).
        if (p.Body is Meldestand ms) Ereignis?.Invoke(new MeldungEingegangen(ms));
    }

    private void CodeErhalten(object? sender, Registrierungscode c)
    {
        lock (_sperre) _registrierung = new Registrierungsangabe(c.Code, c.Befehl, DateTimeOffset.FromUnixTimeMilliseconds(c.GueltigBis));
        Neu();
    }

    private void SitzungBeendet(object? sender, Sitzungsende e)
    {
        lock (_sperre)
        {
            _hinweis = Verbindungstexte.Ende(e);
            // Seit 0.6.3 verbindet der Client danach nicht von selbst (ClientAutomat.ByeEmpfangen); die Startseite nimmt den Haken weg.
            if (e.Bye == ByeGrund.Entfernt) _entfernt = true;
            Abloesen();
        }
        Neu();
    }

    /// <summary>
    /// Eine unerwartete Ausnahme im Client. Ohne diese Meldung stünde die Oberfläche etwa auf "Verbinde..." und niemand wüsste
    /// warum. Sie kommt aus dem Thread, in dem es geschah: Verbindungsschleife, Leseschleife, Takt, aber auch Empfangs- und
    /// Sende-Thread der Sprache, dort womöglich bei jedem Paket. Hier wird deshalb nur gemerkt und höchstens einmal an den
    /// Oberflächentakt übergeben, ohne Sperre, ohne Warten und ohne Neubau der Lage; die jüngste Meldung gewinnt
    /// (<see cref="FehlerUebernehmen"/>).
    /// </summary>
    private void HintergrundFehler(object? sender, Exception e)
    {
        if (sender is not FunkClient c) return;
        Volatile.Write(ref _fehlerOffen, new Hintergrundmeldung(c, c.Zustand, e.Message, Volatile.Read(ref _stand)));
        if (Interlocked.Exchange(ref _fehlerEingereiht, 1) == 0) _takt.Einreihen(FehlerUebernehmen);
    }

    /// <summary>
    /// Im Oberflächentakt: den jüngsten Hintergrundfehler zeigen, solange er nicht abgelöst ist (anderer Client, Erfolg oder
    /// neuerer Hinweis seit dem Fehler). Neu gebaut wird nur, wenn sich Satz oder Einzelheit ändern; derselbe Fehler noch einmal
    /// verlängert nur die Anzeige.
    /// </summary>
    private void FehlerUebernehmen()
    {
        // Erst freigeben, dann abholen: Ein Fehler, der dazwischen kommt, reiht sich neu ein, statt verloren zu gehen.
        Volatile.Write(ref _fehlerEingereiht, 0);
        if (Interlocked.Exchange(ref _fehlerOffen, null) is not { } m) return;
        var meldung = Fehleranzeige(m.Zustand, m.Meldung);
        bool neu;
        lock (_sperre)
        {
            if (!ReferenceEquals(m.Client, _anbindung?.Client) || m.Stand != _stand) return;
            neu = _fehler != meldung;
            _fehler = meldung;
            _fehlerSeit = _zeit.GetTimestamp();
            UhrStellen(_fehlerDauer);
        }
        if (neu) Neu();
    }

    /// <summary>Im Oberflächentakt: Die Frist des Hintergrundfehlers ist um, außer es kam inzwischen derselbe wieder.</summary>
    private void FehlerAblaufen()
    {
        lock (_sperre)
        {
            _uhrLaeuft = false;
            if (_fehler is null) return;
            var rest = _fehlerDauer - _zeit.GetElapsedTime(_fehlerSeit);
            if (rest > TimeSpan.Zero)
            {
                UhrStellen(rest);
                return;
            }
            _fehler = null;
        }
        Neu();
    }

    /// <summary>Stellt die Uhr für den Ablauf, wenn sie nicht schon läuft (unter der Sperre).</summary>
    private void UhrStellen(TimeSpan nach)
    {
        if (_uhrLaeuft) return;
        _uhrLaeuft = true;
        _fehlerUhr ??= _zeit.CreateTimer(static d => ((FunkClientDienst)d!)._takt.Einreihen(((FunkClientDienst)d!).FehlerAblaufen), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _fehlerUhr.Change(nach, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Die Anzeige hat einen neuen Stand (unter der Sperre): Ein Hintergrundfehler von davor gilt nicht mehr.</summary>
    private void Abloesen()
    {
        _fehler = null;
        _stand++;
    }

    /// <summary>
    /// Ein Hintergrundfehler als Satz mit Fehlernummer, je nachdem, wo der Client gerade stand. Die Meldung (Ort und Ausnahmetext,
    /// darin womöglich Adresse oder Pfad) geht nur als Einzelheit mit.
    /// </summary>
    internal static Verbindungsmeldung Fehleranzeige(ClientZustand zustand, string meldung) => zustand switch
    {
        ClientZustand.Getrennt or ClientZustand.Verbinden =>
            new(Fehlernummern.Anhaengen("Interner Fehler beim Verbinden. Hält es an, sag deinem Admin die Fehlernummer.", Fehlernummern.InternVerbinden), meldung),
        ClientZustand.Registrierung or ClientZustand.Warteraum or ClientZustand.Sync =>
            new(Fehlernummern.Anhaengen("Interner Fehler bei der Anmeldung. Hält es an, sag deinem Admin die Fehlernummer.", Fehlernummern.InternAnmeldung), meldung),
        _ => new(Fehlernummern.Anhaengen("Interner Fehler im Client. Hält es an, sag deinem Admin die Fehlernummer.", Fehlernummern.InternSitzung), meldung),
    };

    /// <summary>
    /// Baut die Lage neu. Das geschieht aus mehreren Threads (Ereignisse des Clients, Anfragen der Oberfläche). Jeder Bau
    /// zieht vor dem Lesen eine Nummer; übernommen wird er nur, wenn keine höhere Nummer schon übernommen ist. Ein Bau mit
    /// höherer Nummer hat seinen Stand später gelesen und enthält damit alles, was den niedrigeren ausgelöst hat. So
    /// überschreibt ein langsamer Bau mit altem Stand nie einen neueren.
    /// </summary>
    private void Neu()
    {
        var nummer = Interlocked.Increment(ref _bauNummer);
        FunkAnbindung? a;
        IReadOnlyList<SprechanzeigeEintrag> sprechanzeige;
        Registrierungsangabe? registrierung;
        IReadOnlyList<string> vergeben;
        Verbindungsmeldung? hinweis;
        bool entfernt;
        lock (_sperre)
        {
            entfernt = _entfernt;
            a = _anbindung;
            sprechanzeige = _sprechanzeige;
            registrierung = _registrierung;
            vergeben = _vergeben;
            // Ein frischer Hintergrundfehler steht vor dem Hinweis; läuft er ab, ist der Hinweis wieder da.
            hinweis = _fehler ?? _hinweis;
        }
        FunkLage neu;
        if (a is null)
        {
            neu = FunkLage.Leer with { Hinweis = hinweis?.Text, HinweisEinzelheit = hinweis?.Einzelheit, Entfernt = entfernt };
        }
        else
        {
            var l = a.Client.Lage;
            var zustand = a.Client.Zustand;
            neu = LageAbbildung.Abbilden(new LageEingabe(zustand, l.Welcome, l.NetzListe, l.Mitgliedschaften, l.Praesenz,
                a.Client.Sprechen.Auswahl.Felder, a.Client.Sprechen.Auswahl.Global, sprechanzeige, l.Pegel, l.Belegung, l.ZeitVersatzMs)
            {
                ZustandsVersion = l.ZustandsVersion,
                Phase = l.Phase,
                Vollstaendig = l.Vollstaendig,
                Meldestand = l.Meldestand,
                BereitStand = l.BereitStand,
            })
                with
                {
                    Status = AnzeigeStatus(zustand, a.Client.NeuversuchGesperrt),
                    Registrierung = registrierung,
                    ScNamenVergeben = zustand == ClientZustand.Warteraum ? vergeben : [],
                    Hinweis = hinweis?.Text,
                    HinweisEinzelheit = hinweis?.Einzelheit,
                    Entfernt = entfernt,
                    Server = a.Server,
                    Geraet = a.Geraet,
                    Schluesselart = a.Schluesselart,
                };
        }
        if (!Uebernehmen(nummer, neu)) return;
        LageGeaendert?.Invoke();
    }

    /// <summary>Setzt <paramref name="neu"/>, wenn <paramref name="nummer"/> jünger ist als die zuletzt übernommene.</summary>
    internal bool Uebernehmen(long nummer, FunkLage neu)
    {
        lock (_sperre)
        {
            if (nummer <= _uebernommen) return false;
            _uebernommen = nummer;
            _lage = neu;
            return true;
        }
    }

    /// <summary>Zieht eine Baunummer (Tests).</summary>
    internal long BauNummerZiehen() => Interlocked.Increment(ref _bauNummer);

    /// <summary>
    /// Status für die Anzeige, solange ein Client da ist. Zwischen zwei Versuchen und während DNS, TCP und TLS steht der
    /// Automat auf GETRENNT; die Schleife versucht es aber weiter (7.1). Das heißt für Sarevoc "Verbinde...", nicht "Getrennt":
    /// Sonst lüde der Knopf Verbinden zu einem zweiten Client ein, während der erste noch probiert. Getrennt heißt es nur,
    /// wenn kein Neuversuch mehr kommt (4.6, 4.1).
    /// </summary>
    internal static Verbindungsstatus AnzeigeStatus(ClientZustand zustand, bool neuversuchGesperrt) =>
        zustand == ClientZustand.Getrennt && !neuversuchGesperrt ? Verbindungsstatus.Verbinden : LageAbbildung.Status(zustand);
}
