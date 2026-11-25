// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung;

/// <summary>
/// <see cref="IVerwaltungsdienst"/> über den laufenden Client (<see cref="IVerwaltungsWirt"/>) und den Admin-Schlüssel im TPM
/// (<see cref="IAdminSchluesselSpeicher"/>). Merkt sich die offene Admin-Sitzung, bis der Server sie schließt (Leerlauf,
/// 12 h, <c>ADMIN_SITZUNG_NOETIG</c>, Meldung BEENDET), der Nutzer sie schließt oder die Verbindung wechselt. Heikle Handlungen
/// laufen über <see cref="Adminzugang.MitBestaetigungAsync"/>: kommt <c>SCHRITT_BESTAETIGEN</c>, fragt
/// <see cref="CodeFragen"/> nach einem Code, und die Handlung geht danach selbst noch einmal hinaus. Geheimnis und
/// Notfallcodes gibt er nur weiter und hält sie nie fest.
/// </summary>
public sealed class Verwaltungsdienst : IVerwaltungsdienst, IDisposable
{
    /// <summary>So oft wird eine Anfrage wiederholt, die an der Ratengrenze scheiterte, mit etwas mehr als 1 s Abstand.</summary>
    private const int GrenzVersuche = 3;

    private readonly IVerwaltungsWirt _wirt;
    private readonly IAdminSchluesselSpeicher _speicher;
    private readonly IAdminvoraussetzungen _voraussetzungen;
    private readonly TimeProvider _zeit;
    private readonly TimeSpan _grenzPause;
    private readonly Lock _sperre = new();
    private FunkClient? _client;
    private ECDsa? _schluessel;
    private DateTimeOffset? _sitzungBis;
    private DateTimeOffset? _bestaetigtBis;
    private int? _notfallcodesUebrig;
    private Verwaltungslage _lage = Verwaltungslage.Getrennt;

    /// <param name="grenzPause">Pause vor einem neuen Versuch nach <c>GRENZE</c>; ohne Angabe 1,2 s (Tests kürzer).</param>
    /// <param name="voraussetzungen">
    /// Vorprüfung vor dem Einrichten; im Betrieb <see cref="Adminvoraussetzungen.AmPc"/> (<see cref="Einstieg"/>). Ohne Angabe
    /// <see cref="Adminvoraussetzungen.Ungeprueft"/>: nichts wird angefasst, beides gilt als nicht feststellbar.
    /// </param>
    public Verwaltungsdienst(IVerwaltungsWirt wirt, IAdminSchluesselSpeicher speicher, TimeProvider? zeit = null, TimeSpan? grenzPause = null,
        IAdminvoraussetzungen? voraussetzungen = null)
    {
        ArgumentNullException.ThrowIfNull(wirt);
        ArgumentNullException.ThrowIfNull(speicher);
        _wirt = wirt;
        _speicher = speicher;
        _voraussetzungen = voraussetzungen ?? Adminvoraussetzungen.Ungeprueft;
        _zeit = zeit ?? TimeProvider.System;
        _grenzPause = grenzPause ?? TimeSpan.FromMilliseconds(1200);
        _wirt.Geaendert += WirtGeaendert;
        WirtGeaendert();
    }

    public event Action? LageGeaendert;

    public event Action<object>? Nachricht;

    public Func<string, CancellationToken, Task<string?>>? CodeFragen { get; set; }

    public Verwaltungslage Lage
    {
        get
        {
            lock (_sperre) return _lage;
        }
    }

    public void Dispose()
    {
        _wirt.Geaendert -= WirtGeaendert;
        lock (_sperre)
        {
            if (_client is not null) _client.PushErhalten -= PushErhalten;
            _client = null;
            _schluessel?.Dispose();
            _schluessel = null;
        }
    }

    // ---------------------------------------------------------------- Admin-Nachweis

    public Task<TpmStand> VoraussetzungenAsync() => _voraussetzungen.PruefenAsync();

    public async Task<Verwaltungsantwort<string>> AntragStellenAsync()
    {
        if (Client() is not { } c) return Verwaltungsantwort.Schlecht<string>(Verwaltungstexte.NichtVerbunden);
        ECDsa schluessel;
        try
        {
            schluessel = SchluesselOderNeu();
        }
        catch (InvalidOperationException ex)
        {
            return Verwaltungsantwort.Schlecht<string>(ex.Message);
        }
        var a = await Versuchen(() => c.AdminAntragAsync(schluessel)).ConfigureAwait(false);
        if (!a.Ok) return Verwaltungsantwort.Schlecht<string>(Fehlertext(a));
        Neu();
        return Verwaltungsantwort.Gut<string>(a.Body is AdminEinrichtungAntwort { Kennung: { } k } ? k : Adminschluesselwahl.Kennung(schluessel));
    }

    public async Task<Verwaltungsantwort<AdminEinrichtungAntwort>> GeheimnisAbholenAsync()
    {
        if (Client() is not { } c) return Verwaltungsantwort.Schlecht<AdminEinrichtungAntwort>(Verwaltungstexte.NichtVerbunden);
        if (Schluessel() is not { } schluessel) return Verwaltungsantwort.Schlecht<AdminEinrichtungAntwort>(Verwaltungstexte.KeinSchluessel);
        var a = await Versuchen(() => c.GeheimnisAbholenAsync(schluessel)).ConfigureAwait(false);
        return a.Ok && a.Body is AdminEinrichtungAntwort e && e.Geheimnis is not null && e.Uri is not null
            ? Verwaltungsantwort.Gut<AdminEinrichtungAntwort>(e)
            : Verwaltungsantwort.Schlecht<AdminEinrichtungAntwort>(Fehlertext(a));
    }

    public async Task<Verwaltungsantwort<IReadOnlyList<string>>> EinrichtungBestaetigenAsync(string code)
    {
        if (Client() is not { } c) return Verwaltungsantwort.Schlecht<IReadOnlyList<string>>(Verwaltungstexte.NichtVerbunden);
        if (Schluessel() is not { } schluessel) return Verwaltungsantwort.Schlecht<IReadOnlyList<string>>(Verwaltungstexte.KeinSchluessel);
        if (Codeform.Totp(code) is not { } sauber) return Verwaltungsantwort.Schlecht<IReadOnlyList<string>>(Verwaltungstexte.CodeForm);
        var a = await Versuchen(() => c.EinrichtungBestaetigenAsync(schluessel, sauber)).ConfigureAwait(false);
        return a.Ok && a.Body is AdminEinrichtungAntwort { Notfallcodes: { } n }
            ? Verwaltungsantwort.Gut<IReadOnlyList<string>>(n)
            : Verwaltungsantwort.Schlecht<IReadOnlyList<string>>(Fehlertext(a));
    }

    public async Task<Verwaltungsantwort<AdminSitzungAntwort>> AnmeldenAsync(string code, bool notfallcode)
    {
        if (Client() is not { } c) return Verwaltungsantwort.Schlecht<AdminSitzungAntwort>(Verwaltungstexte.NichtVerbunden);
        if (Schluessel() is not { } schluessel) return Verwaltungsantwort.Schlecht<AdminSitzungAntwort>(Verwaltungstexte.KeinSchluessel);
        var sauber = notfallcode ? Codeform.Notfall(code) : Codeform.Totp(code);
        if (sauber is null) return Verwaltungsantwort.Schlecht<AdminSitzungAntwort>(notfallcode ? Verwaltungstexte.NotfallForm : Verwaltungstexte.CodeForm);
        var a = await Versuchen(() => notfallcode ? c.AdminAnmeldenAsync(schluessel, null, sauber) : c.AdminAnmeldenAsync(schluessel, sauber)).ConfigureAwait(false);
        if (!a.Ok || a.Body is not AdminSitzungAntwort s) return Verwaltungsantwort.Schlecht<AdminSitzungAntwort>(Fehlertext(a));
        lock (_sperre)
        {
            _sitzungBis = DateTimeOffset.FromUnixTimeMilliseconds(s.Bis);
            _bestaetigtBis = DateTimeOffset.FromUnixTimeMilliseconds(s.BestaetigtBis);
            _notfallcodesUebrig = s.NotfallcodesUebrig;
        }
        Neu();
        return Verwaltungsantwort.Gut<AdminSitzungAntwort>(s);
    }

    public async Task AbmeldenAsync()
    {
        if (Client() is { } c)
        {
            try
            {
                await c.AdminAbmeldenAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is VerbindungGetrenntException or OperationCanceledException or TimeoutException or InvalidOperationException)
            {
                // Ohne Verbindung ist die Admin-Sitzung ohnehin zu.
            }
        }
        SitzungSchliessen();
    }

    // ---------------------------------------------------------------- Anträge und Geräte

    public async Task<Verwaltungsantwort<IReadOnlyList<GeraetEintrag>>> AntraegeAsync()
    {
        var a = await Anfrage(Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { NurWartend = true }).ConfigureAwait(false);
        return a.Ok && a.Body is GeraeteListeAntwort l
            ? Verwaltungsantwort.Gut<IReadOnlyList<GeraetEintrag>>(l.Geraete)
            : Verwaltungsantwort.Schlecht<IReadOnlyList<GeraetEintrag>>(Fehlertext(a));
    }

    public async Task<string?> EntscheidenAsync(uint geraetId, GeraetEntscheidung entscheidung, uint? vormerkungId = null, uint? kontoId = null)
    {
        var body = new GeraetEntscheidenAnfrage { GeraetId = geraetId, Entscheidung = entscheidung, VormerkungId = vormerkungId, KontoId = kontoId };
        var a = await Heikel(Nachrichtentyp.GeraetEntscheiden, body, entscheidung == GeraetEntscheidung.Freigeben ? "Gerät freigeben" : "Antrag ablehnen").ConfigureAwait(false);
        return a.Ok ? null : Fehlertext(a);
    }

    public async Task<Verwaltungsantwort<IReadOnlyList<GeraetEintrag>>> GeraeteAsync(uint kontoId)
    {
        var a = await Anfrage(Nachrichtentyp.GeraeteListe, new GeraeteListeAnfrage { KontoId = kontoId }).ConfigureAwait(false);
        return a.Ok && a.Body is GeraeteListeAntwort l
            ? Verwaltungsantwort.Gut<IReadOnlyList<GeraetEintrag>>(l.Geraete)
            : Verwaltungsantwort.Schlecht<IReadOnlyList<GeraetEintrag>>(Fehlertext(a));
    }

    public async Task<string?> SperreAufhebenAsync(uint geraetId)
    {
        // Ein Server vor 0.8.0 kennt ENTSPERREN nicht und antwortete UNGUELTIG; dann gleich sagen, woran es liegt.
        if (!Lage.SperreAufhebenBekannt) return Verwaltungstexte.SperreAufhebenZuAlt;
        var body = new GeraetEntscheidenAnfrage { GeraetId = geraetId, Entscheidung = GeraetEntscheidung.Entsperren };
        var a = await Heikel(Nachrichtentyp.GeraetEntscheiden, body, "Sperre aufheben").ConfigureAwait(false);
        return a.Ok ? null
            : a.Code == Fehlercode.Ungueltig ? Verwaltungstexte.NichtGesperrt
            : Fehlertext(a);
    }

    public async Task<Verwaltungsantwort<int>> UnpassendeAblehnenAsync()
    {
        var a = await Heikel(Nachrichtentyp.AntraegeAblehnen, new AntraegeAblehnenAnfrage { NurOhneVormerkung = true },
            "Alle Anträge ablehnen, die zu keiner Vormerkung passen").ConfigureAwait(false);
        return a.Ok && a.Body is AntraegeAblehnenAntwort n
            ? Verwaltungsantwort.Gut<int>(n.Anzahl)
            : Verwaltungsantwort.Schlecht<int>(Fehlertext(a));
    }

    // ---------------------------------------------------------------- Mitglieder

    public async Task<Verwaltungsantwort<IReadOnlyList<MitgliedEintrag>>> MitgliederAsync()
    {
        var alle = new List<MitgliedEintrag>();
        uint? ab = null;
        // Seitenweise zu 250 (4.10); höchstens so viele Seiten, wie es Konten und Vormerkungen geben kann.
        for (var seite = 0; seite < 8; seite++)
        {
            var a = await Anfrage(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Liste, Ab = ab }).ConfigureAwait(false);
            if (!a.Ok || a.Body is not MitgliedAntwort m) return Verwaltungsantwort.Schlecht<IReadOnlyList<MitgliedEintrag>>(Fehlertext(a));
            alle.AddRange(m.Mitglieder ?? []);
            if (m.Weiter is not { } weiter || weiter <= (ab ?? 0)) break;
            ab = weiter;
        }
        return Verwaltungsantwort.Gut<IReadOnlyList<MitgliedEintrag>>(alle);
    }

    public Task<string?> VormerkenAsync(string scName, string? discordName, IReadOnlyList<ushort> rollen, string? notiz) =>
        Einfach(Nachrichtentyp.Mitglied, new MitgliedAnfrage
        {
            Op = MitgliedOperation.Vormerken,
            ScName = scName,
            DiscordName = string.IsNullOrWhiteSpace(discordName) ? null : discordName,
            Rollen = rollen,
            Notiz = string.IsNullOrWhiteSpace(notiz) ? null : notiz,
        });

    public Task<string?> KontoAendernAsync(uint kontoId, IReadOnlyList<ushort> rollen, string notiz) =>
        Einfach(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Aendern, KontoId = kontoId, Rollen = rollen, Notiz = notiz });

    public async Task<Verwaltungsantwort<AbzeichenAntwort>> AbzeichenSetzenAsync(uint kontoId, IReadOnlyList<string> raenge, IReadOnlyList<string> taetigkeiten)
    {
        var a = await Anfrage(Nachrichtentyp.Abzeichen, new AbzeichenAnfrage { KontoId = kontoId, Raenge = raenge, Taetigkeiten = taetigkeiten }).ConfigureAwait(false);
        return a.Ok && a.Body is AbzeichenAntwort stand
            ? Verwaltungsantwort.Gut(stand)
            : Verwaltungsantwort.Schlecht<AbzeichenAntwort>(Fehlertext(a));
    }

    public Task<string?> MeldungenSendenSetzenAsync(uint kontoId, bool an)
    {
        // Ein Server vor 0.12.0 übergänge das Feld still und meldete Erfolg; dann gleich sagen, woran es liegt.
        if (!Lage.MeldungenBekannt) return Task.FromResult<string?>(Verwaltungstexte.MeldungenZuAlt);
        return Einfach(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Aendern, KontoId = kontoId, MeldungenSenden = an });
    }

    public Task<string?> DiscordNameSetzenAsync(uint kontoId, string? discordName) =>
        Einfach(Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage
        {
            Op = KontoOperation.Discordname,
            KontoId = kontoId,
            Wert = string.IsNullOrWhiteSpace(discordName) ? null : JsonSerializer.SerializeToElement(discordName),
        });

    public Task<string?> VormerkungAendernAsync(uint vormerkungId, string scName, string? discordName, IReadOnlyList<ushort> rollen, string notiz) =>
        Einfach(Nachrichtentyp.Mitglied, new MitgliedAnfrage
        {
            Op = MitgliedOperation.Aendern,
            VormerkungId = vormerkungId,
            ScName = scName,
            DiscordName = string.IsNullOrWhiteSpace(discordName) ? null : discordName,
            Rollen = rollen,
            Notiz = notiz,
        });

    public Task<string?> VormerkungLoeschenAsync(uint vormerkungId) =>
        Einfach(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Loeschen, VormerkungId = vormerkungId });

    public async Task<string?> KontoLoeschenAsync(uint kontoId)
    {
        var a = await Heikel(Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage { Op = KontoOperation.Loeschen, KontoId = kontoId }, "Konto löschen")
            .ConfigureAwait(false);
        return a.Ok ? null : Fehlertext(a);
    }

    public async Task<Verwaltungsantwort<Einfuegeergebnis>> EinfuegenAsync(IReadOnlyList<string> zeilen)
    {
        ArgumentNullException.ThrowIfNull(zeilen);
        var a = await Anfrage(Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Einfuegen, Zeilen = zeilen }).ConfigureAwait(false);
        return a.Ok && a.Body is MitgliedAntwort m
            ? Verwaltungsantwort.Gut(new Einfuegeergebnis(m.Zeilen ?? [], m.Hinweis))
            : Verwaltungsantwort.Schlecht<Einfuegeergebnis>(Fehlertext(a));
    }

    // ---------------------------------------------------------------- Rollenkatalog

    public async Task<Verwaltungsantwort<RollenkatalogAntwort>> KatalogAsync()
    {
        var a = await Anfrage(Nachrichtentyp.Rollenkatalog, new RollenkatalogAnfrage { Op = RegelOperation.Lesen }).ConfigureAwait(false);
        return a.Ok && a.Body is RollenkatalogAntwort k
            ? Verwaltungsantwort.Gut<RollenkatalogAntwort>(k)
            : Verwaltungsantwort.Schlecht<RollenkatalogAntwort>(Fehlertext(a));
    }

    public Task<string?> KatalogSetzenAsync(IReadOnlyList<Rollenkatalogeintrag> eintraege, long version) =>
        Einfach(Nachrichtentyp.Rollenkatalog, new RollenkatalogAnfrage { Op = RegelOperation.Setzen, Eintraege = eintraege, Version = version });

    // ---------------------------------------------------------------- Einsatzleitung und Flotte (0.3.0)

    public Flottenlage Flotte
    {
        get
        {
            FunkClient? c;
            lock (_sperre) c = _client;
            if (c is null) return Flottenlage.Leer;
            var l = c.Lage;
            var mitglieder = new Dictionary<ushort, int>();
            foreach (var p in l.Praesenz.Values)
                foreach (var f in p.Felder ?? [])
                    mitglieder[f.NetzId] = mitglieder.GetValueOrDefault(f.NetzId) + 1;
            return new Flottenlage
            {
                NetzListe = l.NetzListe,
                Eingeteilte = l.Belegung is { } b ? b.Plaetze.Sum(x => x.Belegt + x.Reserviert) : l.Praesenz.Values.Count(p => p.RolleId is not null),
                Mitglieder = mitglieder,
                // Wie FunkClient.AnfrageAsync: Ohne lesbare Fassung gilt der Server als neu genug.
                PhasenBekannt = l.Welcome is { } w && Versionen.ServerKennt(w.Server?.Version, Nachrichtentyp.PhaseSchalten),
                KanaeleBekannt = l.Welcome is { } w2 && Versionen.ServerKennt(w2.Server?.Version, Nachrichtentyp.RolleWaehlen),
                LaufendePhase = l.Phase?.Nr ?? 1,
            };
        }
    }

    public string? Ablageordner => _wirt.Ablageordner;

    public async Task<Verwaltungsantwort<IReadOnlyList<BeauftragungEintrag>>> AuftraegeAsync()
    {
        var a = await Anfrage(Nachrichtentyp.Beauftragung, new BeauftragungAnfrage { Op = BeauftragungOperation.Liste }).ConfigureAwait(false);
        return a.Ok && a.Body is BeauftragungAntwort b
            ? Verwaltungsantwort.Gut(b.Beauftragungen)
            : Verwaltungsantwort.Schlecht<IReadOnlyList<BeauftragungEintrag>>(MitText(a));
    }

    public async Task<Verwaltungsantwort<IReadOnlyList<BeauftragungEintrag>>> AuftragErteilenAsync(uint kontoId, long? von, long? bis, IReadOnlyList<Recht> rechte, string? grund)
    {
        var body = new BeauftragungAnfrage
        {
            Op = BeauftragungOperation.Erteilen,
            KontoId = kontoId,
            Von = von,
            Bis = bis,
            Rechte = rechte,
            Grund = string.IsNullOrWhiteSpace(grund) ? null : grund.Trim(),
        };
        var a = await Heikel(Nachrichtentyp.Beauftragung, body, "Einsatzleitung vergeben").ConfigureAwait(false);
        return a.Ok && a.Body is BeauftragungAntwort b
            ? Verwaltungsantwort.Gut(b.Beauftragungen)
            : Verwaltungsantwort.Schlecht<IReadOnlyList<BeauftragungEintrag>>(MitText(a));
    }

    public async Task<string?> AuftragEntziehenAsync(uint kontoId)
    {
        var a = await Heikel(Nachrichtentyp.Beauftragung, new BeauftragungAnfrage { Op = BeauftragungOperation.Entziehen, KontoId = kontoId }, "Einsatzleitung entziehen")
            .ConfigureAwait(false);
        return a.Ok ? null : MitText(a);
    }

    public async Task<string?> FlotteUebernehmenAsync(VerwaltungsPaket paket, long version)
    {
        ArgumentNullException.ThrowIfNull(paket);
        var body = new VerwaltungAnfrage { Op = VerwaltungsOperation.Paket, Objekt = SteuerJson.Element(paket), Version = version };
        var a = await Heikel(Nachrichtentyp.Verwaltung, body, "Flotte übernehmen").ConfigureAwait(false);
        return a.Ok ? null : MitText(a);
    }

    /// <summary>Die Regeln eines Auftrags begründet der Server in einem Satz (6.12); den zeigen, sonst den allgemeinen Text.</summary>
    private static string MitText(Antwort a) =>
        a.Fehler is { Code: Fehlercode.Ungueltig or Fehlercode.KeinRecht, Text: { Length: > 0 } t } && !t.Contains('[', StringComparison.Ordinal)
            ? (t.EndsWith('.') ? t : t + ".")
            : Fehlertext(a);

    // ---------------------------------------------------------------- Innen

    private FunkClient? Client()
    {
        lock (_sperre) return _lage.Verbunden ? _client : null;
    }

    private ECDsa? Schluessel()
    {
        lock (_sperre)
        {
            _schluessel ??= _speicher.Laden();
            return _schluessel;
        }
    }

    private ECDsa SchluesselOderNeu()
    {
        lock (_sperre)
        {
            _schluessel ??= _speicher.Laden() ?? _speicher.Erzeugen();
            return _schluessel;
        }
    }

    private async Task<string?> Einfach<T>(Nachrichtentyp typ, T body) where T : class
    {
        var a = await Anfrage(typ, body).ConfigureAwait(false);
        return a.Ok ? null : Fehlertext(a);
    }

    private async Task<Antwort> Anfrage<T>(Nachrichtentyp typ, T body) where T : class
    {
        if (Client() is not { } c) return Ohne();
        var a = await Versuchen(() => c.AnfrageAsync(typ, body)).ConfigureAwait(false);
        Pruefen(a);
        return a;
    }

    /// <summary>Heikle Handlung: kommt <c>SCHRITT_BESTAETIGEN</c>, Code fragen, bestätigen, noch einmal senden (3.9).</summary>
    private async Task<Antwort> Heikel<T>(Nachrichtentyp typ, T body, string wofuer) where T : class
    {
        if (Client() is not { } c) return Ohne();
        var fragen = CodeFragen;
        var a = await Versuchen(() => c.MitBestaetigungAsync(typ, body, async abbruch =>
        {
            if (fragen is null) return null;
            var roh = await fragen(wofuer, abbruch).ConfigureAwait(false);
            return roh is null ? null : Codeform.Totp(roh) ?? roh;
        })).ConfigureAwait(false);
        Pruefen(a);
        if (a.Ok)
        {
            // Die Bestätigung gilt 5 Minuten ab dem Code; genau kennt die Frist nur der Server, die Anzeige ist ein Hinweis.
            lock (_sperre) _bestaetigtBis ??= _zeit.GetUtcNow().AddMinutes(5);
        }
        return a;
    }

    private static Antwort Ohne() => new(Nachrichtentyp.Fehler, null, new Fehler { Code = Fehlercode.Intern, Text = Verwaltungstexte.NichtVerbunden });

    /// <summary>Wiederholt nach <c>GRENZE</c> (1/s, Stoß 3 bzw. 5) mit etwas Abstand; Trennung ergibt eine Fehlerantwort.</summary>
    private async Task<Antwort> Versuchen(Func<Task<Antwort>> anfrage)
    {
        for (var i = 0; ; i++)
        {
            Antwort a;
            try
            {
                a = await anfrage().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is VerbindungGetrenntException or OperationCanceledException or TimeoutException or InvalidOperationException)
            {
                return new Antwort(Nachrichtentyp.Fehler, null, new Fehler { Code = Fehlercode.Intern, Text = Verwaltungstexte.Getrennt });
            }
            if (a.Code != Fehlercode.Grenze || i + 1 >= GrenzVersuche) return a;
            await Task.Delay(_grenzPause).ConfigureAwait(false);
        }
    }

    /// <summary>Ohne Admin-Sitzung (Leerlauf, 12 h, Server neu) ist sie auch hier zu.</summary>
    private void Pruefen(Antwort a)
    {
        if (a.Code == Fehlercode.AdminSitzungNoetig) SitzungSchliessen();
        else if (a.Code == Fehlercode.SchrittBestaetigen)
            lock (_sperre) _bestaetigtBis = null;
    }

    private void SitzungSchliessen()
    {
        lock (_sperre)
        {
            if (_sitzungBis is null && _bestaetigtBis is null) return;
            _sitzungBis = null;
            _bestaetigtBis = null;
        }
        Neu();
    }

    private void WirtGeaendert()
    {
        var neu = _wirt.Client;
        lock (_sperre)
        {
            if (!ReferenceEquals(neu, _client))
            {
                if (_client is not null) _client.PushErhalten -= PushErhalten;
                _client = neu;
                if (neu is not null) neu.PushErhalten += PushErhalten;
                _sitzungBis = null;
                _bestaetigtBis = null;
                _notfallcodesUebrig = null;
            }
        }
        Neu();
    }

    private void PushErhalten(object? sender, PushEreignis p)
    {
        if (p.Body is Meldung { Art: MeldungsArten.AdminSitzung } m && Op(m) == "BEENDET") SitzungSchliessen();
        if (p.Body is Meldung or GeraetStatus) Nachricht?.Invoke(p.Body);
        if (p.Body is Mitgliedschaften or NetzListe) Neu();
    }

    internal static string? Op(Meldung m) =>
        m.Details is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : null;

    /// <summary>Baut die Lage aus Client, Kontostand und gemerkter Admin-Sitzung neu und meldet sie.</summary>
    private void Neu()
    {
        lock (_sperre)
        {
            var c = _client;
            var aktiv = c is not null && c.Zustand is ClientZustand.AktivUdpBinden or ClientZustand.AktivUdp or ClientZustand.AktivTunnel;
            if (!aktiv)
            {
                _sitzungBis = null;
                _bestaetigtBis = null;
            }
            if (_sitzungBis is { } bis && bis <= _zeit.GetUtcNow())
            {
                _sitzungBis = null;
                _bestaetigtBis = null;
            }
            var konto = c?.Lage.Konto;
            var welcome = c?.Lage.Welcome?.Konto;
            _lage = new Verwaltungslage
            {
                Verbunden = aktiv,
                Name = welcome?.Name,
                Admin = aktiv ? c!.Lage.Admin : null,
                AntragMoeglich = aktiv && (konto?.AdminAntrag ?? welcome?.AdminAntrag) == true,
                Eigentuemer = aktiv && (konto?.Eigentuemer ?? welcome?.Eigentuemer) == true,
                SchluesselVorhanden = _schluessel is not null,
                SitzungBis = _sitzungBis,
                BestaetigtBis = _bestaetigtBis,
                NotfallcodesUebrig = _notfallcodesUebrig,
                ServerFassung = aktiv ? c!.Lage.Welcome?.Server?.Version : null,
            };
        }
        LageGeaendert?.Invoke();
    }

    /// <summary>Ein Fehler als Satz, mit Frist in Ortszeit, wo der Server eine nennt.</summary>
    internal static string Fehlertext(Antwort a)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a.Fehler is not { } f) return "Unerwartete Antwort des Servers.";
        return f.Code switch
        {
            Fehlercode.KeinRecht => "Dafür fehlt das Recht.",
            Fehlercode.AdminSitzungNoetig => "Die Admin-Sitzung ist zu (nach 12 Stunden, nach dem Trennen oder mit Verwaltung schließen). Melde dich mit einem neuen Code an.",
            Fehlercode.SchrittBestaetigen => "Ohne frischen Code aus der App geht das nicht.",
            Fehlercode.ZweitfaktorFalsch => "Der Code stimmt nicht oder war schon benutzt. Nimm den nächsten, den die App zeigt, und prüf die Uhr des Telefons.",
            Fehlercode.ZweitfaktorGesperrt when Bis(f) is { } bis => "Zu viele falsche Codes. Die Anmeldung ist bis " + bis.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) + " Uhr pausiert.",
            Fehlercode.ZweitfaktorGesperrt => "Zu viele falsche Codes. Der Zweitfaktor ist gesperrt, bis der Eigentümer ihn an der Serverkonsole entsperrt.",
            Fehlercode.NurAdminWerkzeug => "Das geht nur an der Serverkonsole (funk-admin), nicht im Client.",
            // Der Server sagt, welcher Name (SC- oder Discord-Name) und bei der Freigabe auch, wem er gehört (A53).
            Fehlercode.NameVergeben when f.Text is { Length: > 0 } t => t.EndsWith('.') ? t : t + ".",
            Fehlercode.NameVergeben => "Den Namen " + (Name(f) ?? "") + " hat schon jemand.",
            Fehlercode.Veraltet => "Der Stand hat sich inzwischen geändert. Neu laden und noch einmal.",
            Fehlercode.Grenze => "Zu viele Anfragen hintereinander. Kurz warten und noch einmal.",
            Fehlercode.Unbekannt => "Das gibt es nicht (mehr). Neu laden.",
            Fehlercode.Ungueltig => "Die Angaben passen nicht (Länge, Zeichen, doppelte Namen oder zu viele Einträge).",
            Fehlercode.GeraeteLimit => "Das Konto hat schon drei aktive Geräte.",
            Fehlercode.KontoGesperrt => "Das Konto ist gesperrt.",
            Fehlercode.Intern when f.Text is { Length: > 0 } t => t,
            // Der Client schickt keine Anfrage, die der Server nach seiner Fassung nicht kennt (ein Server 0.2.0 trennt sonst).
            Fehlercode.Version when f.Text is { Length: > 0 } t => t,
            _ => "Abgelehnt: " + Fehlercodes.Drahtname(f.Code) + (f.Text is { Length: > 0 } t ? " (" + t + ")" : ""),
        };
    }

    private static DateTimeOffset? Bis(Fehler f) =>
        f.Details is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("bis", out var b) && b.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

    private static string? Name(Fehler f) =>
        f.Details is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
}

/// <summary>Feste Sätze der Verwaltung, per du, nur einfache Striche.</summary>
public static class Verwaltungstexte
{
    public const string NichtVerbunden = "Nicht verbunden. Die Verwaltung braucht eine laufende Verbindung zum Server.";
    public const string Getrennt = "Die Verbindung ist weg. Nach dem Wiederverbinden noch einmal.";
    public const string KeinSchluessel = "Auf diesem PC gibt es keinen Admin-Schlüssel. Die Verwaltung richtet nur der Eigentümer an der Konsole für einen neuen Schlüssel ein.";
    public const string CodeForm = "Der Code hat sechs Ziffern, so wie deine Authenticator-App ihn zeigt.";
    public const string NotfallForm = "Ein Notfallcode hat zwölf Zeichen, etwa ABCD-EFGH-JKMN.";
    public const string SperreAufhebenZuAlt = "Sperre aufheben geht erst mit einem Server ab 0.8.0.";
    public const string MeldungenZuAlt = "Meldungen senden gibt es erst mit einem Server ab 0.12.0.";
    public const string NichtGesperrt = "Das Gerät ist nicht mehr gesperrt. Neu laden.";
}

/// <summary>Eingaben für Codes: Leerzeichen und Bindestriche beim Einfügen sind egal.</summary>
public static class Codeform
{
    /// <summary>Sechs Ziffern aus der App, oder <c>null</c>.</summary>
    public static string? Totp(string? text)
    {
        if (text is null) return null;
        var ziffern = new string([.. text.Where(z => !char.IsWhiteSpace(z) && z != '-')]);
        return ziffern.Length == 6 && ziffern.All(char.IsAsciiDigit) ? ziffern : null;
    }

    /// <summary>Notfallcode: zwölf Zeichen Crockford-Base32, in Gruppen geschrieben wie angezeigt; sonst <c>null</c>.</summary>
    public static string? Notfall(string? text)
    {
        if (text is null) return null;
        var roh = new string([.. text.Where(z => !char.IsWhiteSpace(z) && z != '-')]).ToUpperInvariant();
        if (roh.Length != 12 || !roh.All(char.IsAsciiLetterOrDigit)) return null;
        return roh[..4] + "-" + roh[4..8] + "-" + roh[8..];
    }
}
