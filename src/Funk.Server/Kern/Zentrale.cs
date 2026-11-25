// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Channels;
using Funk.Server.Betrieb;
using Funk.Server.Lage;
using Funk.Server.Netzzugang;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Der eine Schreiber des Servers (6.2): Kontenbuch, Einmalcodes, Zustandsschreiber und Komfortzustand gehören ihm und
/// werden nur in seiner Arbeitsschleife verändert. Verbindungen, Relais und Admin-Werkzeug reichen Arbeit als
/// Aufträge herein und bekommen ihre Antwort über eine Task. Aufträge an den Zustandsschreiber sammelt er je Durchlauf
/// zu einem Gruppen-Commit; danach verteilt er die Folgen (Mitgliedschaften, Praesenz, NetzListe, Meldungen,
/// Trennungen) und erst dann die Antworten (6.7 Schritt 7).
/// </summary>
public sealed partial class Zentrale : IAsyncDisposable
{
    /// <summary>Sitzungs-IDs werden so lange nicht neu vergeben (4.6 ENDE).</summary>
    public const long SitzungsIdSperreMs = 24L * 60 * 60 * 1000;

    private readonly Channel<Action> _arbeit = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<(L.Auftrag Auftrag, Action<L.Ergebnis> Danach)> _auftraege = [];
    private readonly Dictionary<long, Verbindung> _verbindungen = [];
    private readonly Dictionary<uint, Verbindung> _wartende = [];
    private readonly ConcurrentDictionary<uint, Verbindung> _sitzungen = new();
    private readonly Dictionary<long, TaskCompletionSource<VerwaltungsAntwort>> _adminVerknuepfungen = [];
    private readonly Dictionary<uint, long> _benutzteIds = [];
    private readonly Zustandsablage _ablage;
    private readonly string _komfortPfad;
    private readonly string _vorrangPfad;
    private Task? _schleife;
    private long _gespeicherteKontenVersion = -1;
    private long _gespeicherteAdminVersion = -1;
    private long _gespeicherteMitgliederVersion = -1;
    private long _letzteKomfortSpeicherung;
    private long _letzteFristen;
    private long _letzteVorrangSpeicherung;
    private long _letzteJournalSpeicherung;
    private S.SigniertesProfil? _profil;
    private readonly I.TotpTresor _tresor;

    public Zentrale(ServerKonfiguration konfiguration, Logbuch log, Zustandsablage ablage, Zulassung zulassung, Vorrangspur spur,
        I.TotpTresor tresor, TimeProvider? zeit = null)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(zulassung);
        ArgumentNullException.ThrowIfNull(spur);
        ArgumentNullException.ThrowIfNull(tresor);
        Konfiguration = konfiguration;
        Log = log;
        _ablage = ablage;
        Zulassung = zulassung;
        Spur = spur;
        _tresor = tresor;
        Uhr = zeit ?? TimeProvider.System;
        _komfortPfad = konfiguration.ZustandsPfad(Komfortablage.Datei);
        _vorrangPfad = konfiguration.ZustandsPfad("vorrangspur.json");

        var (lage, neben) = ablage.LageLaden();
        Schreiber = new L.Zustandsschreiber(lage, neben, ablage);
        Konten = new I.Kontenbuch(Uhr, ablage.KontenLaden());
        _gespeicherteKontenVersion = Konten.Stand.Version;
        if (ablage.AltsperrenVerworfen > 0)
        {
            // Stand bis 0.6.x (Zustandsablage.KontenLaden): gleich neu speichern, damit Datei und Speicher übereinstimmen.
            _gespeicherteKontenVersion = -1;
            Log.Info("Umstellung auf 0.7.0: " + ablage.AltsperrenVerworfen.ToString(CultureInfo.InvariantCulture)
                + " alte Gerätesperren und abgelehnte Geräte verworfen; seit 0.7.0 sperrt nur ein Admin bewusst.");
        }
        Admins = new I.Adminbuch(Uhr, tresor, ablage.AdminsLaden());
        _gespeicherteAdminVersion = Admins.Stand.Version;
        Mitglieder = new I.Mitgliederbuch(Uhr, ablage.MitgliederLaden());
        _gespeicherteMitgliederVersion = Mitglieder.Stand.Version;
        Konten.VormerkungsNamen = Mitglieder.NameBelegt;
        Konten.VormerkungsDiscordName = Mitglieder.DiscordNameVon;
        Journal = new Journal(konfiguration.ZustandsPfad(Journal.Datei));
        // Umstellung auf 0.7.0 (oben): Welche Gerätekennungen dabei frei wurden, steht im Journal. War eine bewusste Sperre dabei, deren
        // Konto schon gelöscht war, sperrt ein Admin den PC wieder mit "sperren", sobald er einen Antrag stellt.
        foreach (var f in ablage.VerworfeneSperren)
            Vermerken("SPERRE_VERWORFEN", KonsoleDurch, null,
                "Umstellung auf 0.7.0: Gerätekennung " + f.Anzeige + " nicht mehr gesperrt (Ablehnen und Löschen sperren seit 0.7.0 nichts)");
        Codes = new I.EinmalcodeRegister(Uhr);
        if (Dateiablage.LesenFallsVorhanden(_komfortPfad) is { } komfort) Komfort.Laden(komfort);
        if (Dateiablage.LesenFallsVorhanden(_vorrangPfad) is { } vorrang) spur.AusJsonLaden(vorrang, Zeit.UnixMs());
        foreach (var (konto, a) in Komfort.Auswahlen) Schreiber.SendeauswahlSetzen(konto, a.Felder, a.Global, Zeit.LageMs());
        // Einmal laden; der Dienst wertet den Fehler aus (bis 0.4.0 lud er ein zweites Mal, und das Journal nannte das Profil doppelt).
        ProfilFehlerBeimStart = ProfilLaden();
        Version = typeof(Zentrale).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    }

    /// <summary>Was das Laden des Profils beim Anlegen meldete (<see cref="ProfilLaden"/>); <c>null</c>, wenn es geladen ist.</summary>
    public string? ProfilFehlerBeimStart { get; }

    public ServerKonfiguration Konfiguration { get; }
    public Logbuch Log { get; }
    public Zulassung Zulassung { get; }
    public Vorrangspur Spur { get; }
    public TimeProvider Uhr { get; }
    /// <summary>Fassung für Welcome und Status; setzen nur Tests, die einen älteren Server nachstellen.</summary>
    public string Version { get; internal set; }

    /// <summary>
    /// Nur für Tests: So lange hält die Arbeitsschleife an, nachdem eine Freigabe die wartende Verbindung fortgesetzt hat. Deren
    /// Sitzungsstart läuft dann noch im selben Durchlauf, vor dem Spiegeln des Kontenbuchs (siehe <see cref="SitzungStartenAsync"/>).
    /// </summary>
    internal static int FreigabeZeitlupeMs { get; set; }

    /// <summary>Gehören der Arbeitsschleife; von außen nur lesend über <see cref="L.Zustandsschreiber.Aktuell"/> bzw. <see cref="I.Kontenbuch.Stand"/>.</summary>
    public L.Zustandsschreiber Schreiber { get; }
    public I.Kontenbuch Konten { get; }
    public I.EinmalcodeRegister Codes { get; }

    /// <summary>Admin-Nachweise (3.9), <c>admins.json</c>.</summary>
    public I.Adminbuch Admins { get; }

    /// <summary>Rollenkatalog, Vormerkungen, Mitgliedsangaben (6.13), <c>mitglieder.json</c>.</summary>
    public I.Mitgliederbuch Mitglieder { get; }

    /// <summary>Journal der Rechte- und Sicherheitsänderungen, nur für Admins.</summary>
    public Journal Journal { get; }
    public Komfortablage Komfort { get; } = new();

    /// <summary>Wird nach dem Bau gesetzt (Relais und Zentrale kennen einander).</summary>
    public Sprachpfad.Relais? Relais { get; set; }

    /// <summary>Verbindung einer Sitzung, für die Brücke zum Relais; von jedem Thread lesbar.</summary>
    public bool SitzungSuchen(uint sitzung, out Verbindung verbindung) => _sitzungen.TryGetValue(sitzung, out verbindung!);

    public int SitzungenAnzahl => _sitzungen.Count;

    public void Starten() => _schleife = Task.Run(SchleifeAsync);

    // ---- Arbeit hereinreichen (threadsicher) ----

    /// <summary>Führt <paramref name="arbeit"/> in der Arbeitsschleife aus.</summary>
    public void Einreihen(Action arbeit)
    {
        ArgumentNullException.ThrowIfNull(arbeit);
        if (!_arbeit.Writer.TryWrite(arbeit)) throw new InvalidOperationException("Zentrale ist beendet.");
    }

    /// <summary>
    /// Wie <see cref="Einreihen"/>, aber ohne Ausnahme, wenn die Zentrale schon beendet ist: für Meldungen, die beim
    /// Herunterfahren nichts mehr bewirken müssen (eine Verbindung schließt erst nach dem Ende der Zentrale).
    /// </summary>
    internal bool VersucheEinreihen(Action arbeit) => _arbeit.Writer.TryWrite(arbeit);

    /// <summary>Führt <paramref name="arbeit"/> in der Arbeitsschleife aus und liefert das Ergebnis.</summary>
    public Task<T> Ausfuehren<T>(Func<T> arbeit)
    {
        ArgumentNullException.ThrowIfNull(arbeit);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Einreihen(() =>
        {
            try
            {
                tcs.TrySetResult(arbeit());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>Wie <see cref="Ausfuehren{T}(Func{T})"/>, aber die Arbeit meldet ihr Ergebnis selbst (etwa nach einem Commit).</summary>
    public Task<T> Ausfuehren<T>(Action<TaskCompletionSource<T>> arbeit)
    {
        ArgumentNullException.ThrowIfNull(arbeit);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Einreihen(() =>
        {
            try
            {
                arbeit(tcs);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>Nur in der Arbeitsschleife: Auftrag an den Zustandsschreiber; <paramref name="danach"/> läuft nach dem Commit.</summary>
    private void Schreiben(L.Auftrag auftrag, Action<L.Ergebnis>? danach = null)
    {
        Schreiber.Einreihen(auftrag);
        _auftraege.Add((auftrag, danach ?? (_ => { })));
    }

    // ---- Arbeitsschleife ----

    private async Task SchleifeAsync()
    {
        var leser = _arbeit.Reader;
        using var takt = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        var taktTask = TaktAsync(takt);
        while (await leser.WaitToReadAsync().ConfigureAwait(false))
        {
            while (leser.TryRead(out var arbeit))
            {
                try
                {
                    arbeit();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log.Fehler("Zentrale", ex);
                }
            }
            try
            {
                GruppenCommit();
                KontenSichern();
                BelegungVerteilen(erzwingen: false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Fehler("Zentrale, Commit", ex);
            }
        }
        takt.Dispose();
        await taktTask.ConfigureAwait(false);
    }

    private async Task TaktAsync(PeriodicTimer takt)
    {
        try
        {
            while (await takt.WaitForNextTickAsync().ConfigureAwait(false))
            {
                if (!_arbeit.Writer.TryWrite(Takt)) return;
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Gruppen-Commit (6.2): alle Aufträge dieses Durchlaufs in einer Transaktion. Die Folgen werden verteilt, bevor die
    /// Anfragenden ihre Antwort bekommen; die Antworten laufen über <c>danach</c>.
    /// </summary>
    private void GruppenCommit()
    {
        while (_auftraege.Count > 0)
        {
            var offen = _auftraege.ToList();
            _auftraege.Clear();
            var ergebnisse = Schreiber.Abarbeiten(Zeit.LageMs());
            for (var i = 0; i < ergebnisse.Count; i++)
            {
                var e = ergebnisse[i].Ergebnis;
                if (e.Ausnahme is { } ausnahme) Log.Fehler("Zustandsschreiber, Auftrag " + ergebnisse[i].Auftrag.Art, ausnahme);
                FolgenVerteilen(e);
            }
            for (var i = 0; i < offen.Count; i++)
            {
                var e = i < ergebnisse.Count ? ergebnisse[i].Ergebnis : L.Ergebnis.Mit(L.Fehlercode.Intern);
                try
                {
                    offen[i].Danach(e);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log.Fehler("Zentrale, Folge eines Auftrags", ex);
                }
            }
        }
    }

    private void KontenSichern()
    {
        var admins = Admins.Stand;
        if (admins.Version != _gespeicherteAdminVersion)
        {
            _ablage.AdminsSpeichern(admins);
            _gespeicherteAdminVersion = admins.Version;
        }
        var mitglieder = Mitglieder.Stand;
        if (mitglieder.Version != _gespeicherteMitgliederVersion)
        {
            _ablage.MitgliederSpeichern(mitglieder);
            _gespeicherteMitgliederVersion = mitglieder.Version;
        }
        var stand = Konten.Stand;
        if (stand.Version == _gespeicherteKontenVersion) return;
        _ablage.KontenSpeichern(stand);
        _gespeicherteKontenVersion = stand.Version;
        SpiegelAbgleichen();
    }

    private void Takt()
    {
        var jetzt = Zeit.MonoMs();
        AdminSitzungenPruefen();
        RaidleitungTakt(jetzt);
        MeldungenTakt();
        if (Journal.Geaendert && jetzt - _letzteJournalSpeicherung >= 1000)
        {
            _letzteJournalSpeicherung = jetzt;
            // Im eigenen Faden: das ganze Journal (bis einige MB) hält so keine Steuernachricht auf.
            _ = Journal.SpeichernImHintergrund();
        }
        if (Journal.FehlerAbholen() is { } journalFehler) Log.Fehler("Journal, Speichern", journalFehler);
        var minute = jetzt - _letzteFristen >= 60_000;
        SperrlisteTakt(jetzt, minute);
        if (minute)
        {
            _letzteFristen = jetzt;
            WirkungenAusfuehren(Konten.FristenAnwenden());
            Journal.Aufraeumen(Uhr.GetUtcNow().ToUnixTimeMilliseconds());
            Codes.Aufraeumen();
            Zulassung.Aufraeumen(jetzt);
            Spur.Aufraeumen(Zeit.UnixMs());
            var grenze = Zeit.UnixMs() - SitzungsIdSperreMs;
            foreach (var (id, ende) in _benutzteIds.ToArray())
                if (ende != 0 && ende < grenze) _benutzteIds.Remove(id);
        }
        if (Komfort.Geaendert && jetzt - _letzteKomfortSpeicherung >= 1000)
        {
            _letzteKomfortSpeicherung = jetzt;
            Dateiablage.AtomarSchreiben(_komfortPfad, Komfort.AlsJson());
        }
        if (Spur.Geaendert && jetzt - _letzteVorrangSpeicherung >= 10_000)
        {
            _letzteVorrangSpeicherung = jetzt;
            Dateiablage.AtomarSchreiben(_vorrangPfad, Spur.AlsJson());
        }
    }

    // ---- Spiegelung Kontenbuch -> Schnappschuss ----

    /// <summary>
    /// Bringt die Konten im Schnappschuss auf den Stand des Kontenbuchs (Stufe, Rechte, Status, Name). Das Kontenbuch
    /// ist die Quelle; die Weiterleitung entscheidet am Schnappschuss (6.2, 6.3).
    /// </summary>
    private void SpiegelAbgleichen()
    {
        var z = Schreiber.Aktuell;
        var stand = Konten.Stand;
        foreach (var k in stand.Konten.Values)
        {
            // Ein offener Antrag hat im Funk nichts verloren (3.4): Er erscheint erst mit der Freigabe im Schnappschuss.
            if (k.IstAntrag) continue;
            var stufe = Abbildung.StufeLage(k.Stufe);
            var plus = Abbildung.RechtLage(k.Plus);
            var minus = Abbildung.RechtLage(k.Minus);
            var status = k.Status == I.KontoStatus.Gesperrt ? L.KontoStatus.Gesperrt : L.KontoStatus.Aktiv;
            if (z.Konten.TryGetValue(k.KontoId, out var l) && l.Stufe == stufe && l.Plus == plus && l.Minus == minus && l.Status == status
                && string.Equals(l.Name, k.Name, StringComparison.Ordinal) && string.Equals(l.DiscordId, k.DiscordId, StringComparison.Ordinal))
                continue;
            Schreiben(L.InterneAuftraege.KontoUebernehmen(k.KontoId, k.Name, k.DiscordId, stufe, plus, minus, status));
        }
        foreach (var l in z.Konten.Values)
            if (!stand.Konten.TryGetValue(l.Id, out var ik) || ik.IstAntrag) Schreiben(L.InterneAuftraege.KontoEntfernen(l.Id));
    }

    // ---- Folgen verteilen ----

    /// <summary>Folgen eines Commits für den Steuerkanal (6.7 Schritt 7).</summary>
    private void FolgenVerteilen(L.Ergebnis e)
    {
        if (!e.Erfolg) return;
        foreach (var b in e.BeendeteSitzungen)
        {
            if (!_sitzungen.TryGetValue(b.SitzungsId, out var v)) continue;
            switch (b.Grund)
            {
                case L.Sitzungsende.KontoGesperrt:
                    v.Beenden(S.Fehlercode.KontoGesperrt, null);
                    break;
                case L.Sitzungsende.KontoGeloescht:
                    v.Beenden(null, S.ByeGrund.Entfernt);
                    break;
                // Verdrängt und Ersetzt beendet SitzungStarten mit dem verdrängenden Gerät.
            }
        }
        if (e.NetzListeGeaendert)
        {
            // Erst das Versionstor (6.16), damit Clients unter 0.6.0 keine NetzListe mit Kanalnetzen mehr bekommen.
            VersionstorDurchsetzen();
            var liste = Abbildung.NetzListe(Schreiber.Aktuell);
            foreach (var v in AktiveVerbindungen()) v.Push(S.Nachrichtentyp.NetzListe, liste);
        }
        var betroffen = e.BetroffeneKonten.Concat(e.Offline).Concat(e.Verdraengt).Concat(e.Zurueckgesetzt).Distinct().ToList();
        // Ein Phasenwechsel (6.14 Regel 11): Mitgliedschaften mit phase an die Umgestellten, PhaseGeschaltet an alle.
        var umgestellt = e.Phasenwechsel?.Umgestellt ?? [];
        foreach (var konto in betroffen) MitgliedschaftenSenden(konto, umgestellt.Contains(konto) ? e.Phasenwechsel!.Nr : null);
        // Eine Sperre für alle (6.17): Mitgliedschaften an jeden Getroffenen, ohne Praesenz-Delta (Gegenprüfung 3.7).
        foreach (var konto in e.NurMitgliedschaften)
            if (!betroffen.Contains(konto)) MitgliedschaftenSenden(konto);
        // Ändern sich nur Details (Ch.-Wechsel, Rollenwahl, Kanalsperre), geht das Delta nur an die Einsatzleitung (4.5, Gegenprüfung 3.7).
        var nurDetails = e.NurDetails && e.Offline.IsEmpty && e.Verdraengt.IsEmpty && e.Zurueckgesetzt.IsEmpty;
        if (betroffen.Count > 0) PraesenzDelta(betroffen, nurEinsatzleitung: nurDetails);
        if (e.Phasenwechsel is { } pw)
        {
            Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Phase {pw.Vorher} -> {pw.Nr}: {pw.Umgestellt.Length} umgestellt, {pw.Ausgelassen.Length} ausgelassen, durch {(pw.Durch == 0 ? "Konsole oder Rückfall" : Log.Schwaerzung.Konto(pw.Durch))}"));
            var push = Abbildung.PhaseGeschaltet(Schreiber.Aktuell, new L.LaufendePhase(pw.Nr, pw.Seit, pw.Durch));
            foreach (var v in AktiveVerbindungen()) v.Push(S.Nachrichtentyp.PhaseGeschaltet, push);
        }
        if (!e.Funkermodi.IsEmpty) FunkermodusFolgenVerteilen(e);
        foreach (var m in e.Meldungen) LageMeldung(m);
        if (e.AufstellungGeaendert && e.AufstellungsId != 0)
        {
            var geloescht = !Schreiber.Neben.Aufstellungen.ContainsKey(e.AufstellungsId);
            // K3: durch ist, wer gespeichert oder gelöscht hat (vorher immer 0).
            var push = new S.AufstellungGeaendert { Id = e.AufstellungsId, Version = e.AufstellungsVersion, Durch = e.AufstellungDurch, Geloescht = geloescht };
            foreach (var v in VerbindungenMitRecht(L.Recht.Zuweisen)) v.Push(S.Nachrichtentyp.AufstellungGeaendert, push);
        }
        if (!e.Beauftragungen.IsEmpty) BeauftragungFolgenVerteilen(e);
        if (!e.Kanalsperren.IsEmpty) KanalsperrFolgenVerteilen(e);
    }

    private void LageMeldung(L.Meldung m)
    {
        switch (m.Art)
        {
            case L.MeldungsArt.VerdraengungHaeufig:
                MeldungSenden(S.MeldungsArten.VerdraengungHaeufig, L.Recht.GeraeteFreigeben, m.KontoId);
                break;
            case L.MeldungsArt.AufstellungNachgezogen:
                MeldungSenden(S.MeldungsArten.AufstellungNachgezogen, L.Recht.Zuweisen, m.KontoId);
                break;
            case L.MeldungsArt.RechteGeaendert:
                MeldungSenden(S.MeldungsArten.RechteGeaendert, L.Recht.KontenVerwalten, m.KontoId);
                break;
            case L.MeldungsArt.Rate:
                MeldungSenden(S.MeldungsArten.Rate, L.Recht.Zuweisen, m.KontoId);
                break;
        }
    }

    /// <summary>
    /// Sicherheitsmeldungen (3.9): Sie gehen an jede Sitzung eines ADMIN-Kontos, auch ohne Admin-Sitzung, denn nur so
    /// bemerkt ein Admin, wenn jemand anderes mit seinem Konto die Verwaltung öffnet. Sie tragen keine Verwaltungsdaten.
    /// </summary>
    private static readonly HashSet<string> Warnarten = new(StringComparer.Ordinal)
    {
        S.MeldungsArten.VerdraengungHaeufig, S.MeldungsArten.Kopierverdacht, S.MeldungsArten.AdminSitzung,
        S.MeldungsArten.ZweitfaktorGesperrt, S.MeldungsArten.Notfallcode, S.MeldungsArten.AdminGeaendert, S.MeldungsArten.AntraegeVoll,
    };

    /// <summary>
    /// Meldung-Push (0x4002) an alle Sitzungen mit dem Recht, auf Wunsch zusätzlich an die des Kontos. Verwaltungsrechte zählen
    /// nur in einer Admin-Sitzung; Warnungen gehen an jede Sitzung eines ADMIN-Kontos.
    /// </summary>
    internal void MeldungSenden(string art, L.Recht recht, uint? kontoId, uint? geraetId = null, bool auchInhaber = false,
        ushort? netzId = null, System.Text.Json.JsonElement? details = null)
    {
        var meldung = new S.Meldung { Art = art, KontoId = kontoId, GeraetId = geraetId, NetzId = netzId, Details = details };
        var empfaenger = Warnarten.Contains(art) ? AdminKontoVerbindungen().ToList() : VerbindungenMitRecht(recht).ToList();
        if (auchInhaber && kontoId is { } k && SitzungVonKonto(k) is { } eigene && !empfaenger.Contains(eigene)) empfaenger.Add(eigene);
        foreach (var v in empfaenger) v.Push(S.Nachrichtentyp.Meldung, meldung);
    }

    /// <summary>Warnung (3.9) an jede Sitzung eines ADMIN-Kontos.</summary>
    internal void WarnungSenden(string art, uint? kontoId, System.Text.Json.JsonElement? details = null) =>
        MeldungSenden(art, L.Recht.Keine, kontoId, details: details);

    /// <summary>Sitzungen aller ADMIN-Konten, mit oder ohne Admin-Sitzung.</summary>
    private IEnumerable<Verbindung> AdminKontoVerbindungen()
    {
        var stand = Konten.Stand;
        foreach (var v in AktiveVerbindungen())
            if (stand.Konten.TryGetValue(v.KontoId, out var k) && k.Stufe == I.Stufe.Admin) yield return v;
    }

    internal IEnumerable<Verbindung> AktiveVerbindungen() => _sitzungen.Values.Where(v => !v.Endet);

    /// <summary>Nur in der Arbeitsschleife: alle offenen Verbindungen, auch in REGISTRIERUNG und WARTERAUM.</summary>
    internal List<Verbindung> AlleVerbindungen() => [.. _verbindungen.Values.Where(v => !v.Endet)];

    /// <summary>
    /// Sitzungen mit dem Recht. Die drei Verwaltungsrechte zählen nur in einer offenen Admin-Sitzung (3.9, P10): Pushes wie
    /// <c>GeraetStatus</c> an <c>GERAETE_FREIGEBEN</c> oder Meldungen an <c>KONTEN_VERWALTEN</c> erreichen sonst niemanden.
    /// </summary>
    internal IEnumerable<Verbindung> VerbindungenMitRecht(L.Recht recht)
    {
        var z = Schreiber.Aktuell;
        var verwaltung = (recht & L.Rechteregeln.Privilegiert) != 0;
        foreach (var sitzung in z.SitzungenMitRecht(recht, Zeit.LageMs()))
            if (_sitzungen.TryGetValue(sitzung, out var v) && !v.Endet && (!verwaltung || AdminSitzungVon(v) is not null)) yield return v;
    }

    internal Verbindung? SitzungVonKonto(uint kontoId) =>
        Schreiber.Aktuell.SitzungVonKonto(kontoId) is { } s && _sitzungen.TryGetValue(s, out var v) && !v.Endet ? v : null;

    internal void MitgliedschaftenSenden(uint kontoId, byte? phase = null)
    {
        var z = Schreiber.Aktuell;
        if (SitzungVonKonto(kontoId) is not { } v || !z.Konten.TryGetValue(kontoId, out var k)) return;
        v.Push(S.Nachrichtentyp.Mitgliedschaften, MitgliedschaftenBauen(z, k, phase));
    }

    /// <summary>Mitgliedschaften (4.5) mit den eigenen Kontoangaben (F3, F13); <paramref name="phase"/> nach einem Phasenwechsel (6.14).</summary>
    internal S.Mitgliedschaften MitgliedschaftenBauen(L.Schnappschuss z, L.Konto k, byte? phase = null) =>
        Abbildung.Mitgliedschaften(z, Schreiber.Komfort, k, Zeit.LageMs(), KontoStandVon(k.Id), phase);

    /// <summary>Eigene Angaben eines Kontos haben sich geändert (Namen, Rollen, Admin-Zustand, Zeichen): Push.</summary>
    internal void KontoGeaendert(uint kontoId)
    {
        MitgliedschaftenSenden(kontoId);
        PraesenzDelta([kontoId]);
    }

    /// <summary>
    /// Praesenz-Delta an alle aktiven Sitzungen; Details nur für <c>ZUWEISEN</c> (4.5). Mit <paramref name="nurEinsatzleitung"/> nur an
    /// Sitzungen mit <c>ZUWEISEN</c>: Die Änderung betrifft nur Details, die anderen sähen einen unveränderten Eintrag (Gegenprüfung 3.7).
    /// </summary>
    internal void PraesenzDelta(IReadOnlyCollection<uint> konten, bool nurEinsatzleitung = false)
    {
        var z = Schreiber.Aktuell;
        var empfaenger = AktiveVerbindungen().Select(v => (v, HatRecht(z, v, L.Recht.Zuweisen)))
            .Where(x => x.Item2 || !nurEinsatzleitung).ToList();
        if (empfaenger.Count == 0) return;
        var (mit, ohne) = Praesenz(z, konten, voll: false);
        foreach (var (v, details) in empfaenger) v.Push(S.Nachrichtentyp.Praesenz, details ? mit : ohne);
    }

    /// <summary>Praesenz voll bzw. als Delta, einmal mit und einmal ohne Details.</summary>
    internal (S.Praesenz MitDetails, S.Praesenz OhneDetails) Praesenz(L.Schnappschuss z, IEnumerable<uint>? konten, bool voll)
    {
        var jetzt = Zeit.LageMs();
        var stand = Konten.Stand;
        var mit = new List<S.PraesenzEintrag>();
        var ohne = new List<S.PraesenzEintrag>();
        var entfernt = new List<uint>();
        foreach (var id in konten ?? z.Konten.Keys)
        {
            if (!z.Konten.TryGetValue(id, out var k) || id == L.InterneAuftraege.AdminWerkzeug || !Abbildung.InPraesenz(z, k, jetzt))
            {
                if (!voll) entfernt.Add(id);
                continue;
            }
            stand.Konten.TryGetValue(id, out var ik);
            S.Verbindungsweg? weg = null;
            S.Guete? guete = null;
            if (z.SitzungVonKonto(id) is { } sitzung && Relais is { } r && r.Anzeige(sitzung, out var w, out var g))
            {
                weg = w;
                guete = g;
            }
            var zusatz = PraesenzZusatz(ik);
            mit.Add(Abbildung.PraesenzEintrag(z, Schreiber.Komfort, k, ik, true, weg, guete, jetzt, zusatz));
            ohne.Add(Abbildung.PraesenzEintrag(z, Schreiber.Komfort, k, ik, false, null, null, jetzt, zusatz));
        }
        return (new S.Praesenz { Voll = voll, ZustandsVersion = z.ZustandsVersion, Eintraege = mit, Entfernt = entfernt },
            new S.Praesenz { Voll = voll, ZustandsVersion = z.ZustandsVersion, Eintraege = ohne, Entfernt = entfernt });
    }

    /// <summary>
    /// Recht einer Verbindung am Schnappschuss; die drei Verwaltungsrechte nur in einer offenen Admin-Sitzung (3.9).
    /// </summary>
    internal bool HatRecht(L.Schnappschuss z, Verbindung v, L.Recht recht) =>
        z.KontoVonSitzung(v.SitzungsId) is { } k && k.RechteZu(Zeit.LageMs()).Hat(recht)
        && ((recht & L.Rechteregeln.Privilegiert) == 0 || AdminSitzungVon(v) is not null);

    // ---- Wirkungen des Kontenbuchs (Identität) ----

    /// <summary>Führt die Folgen einer Kontenbuch-Aktion aus: Trennen, GeraetStatus, Meldungen, Löschungen (3.4 bis 3.8).</summary>
    internal void WirkungenAusfuehren(IEnumerable<I.Wirkung> wirkungen)
    {
        foreach (var w in wirkungen)
        {
            switch (w)
            {
                case I.SitzungenBeenden sb:
                    foreach (var v in _verbindungen.Values.Where(v => v.KontoId == sb.KontoId && (sb.GeraetId is null || v.GeraetId == sb.GeraetId)).ToList())
                    {
                        switch (sb.Grund)
                        {
                            case I.Trenngrund.KontoGesperrt: v.Beenden(S.Fehlercode.KontoGesperrt, null); break;
                            case I.Trenngrund.GeraetEntzogen: v.Beenden(null, S.ByeGrund.Entzogen); break;
                            // ABGELEHNT endet seit 0.7.0 mit Bye{ENTFERNT} (unten): Der Antrag ist dann gelöscht, und ohne Bye verbände
                            // sich der Client gleich neu und beantragte von selbst wieder.
                            case I.Trenngrund.Umgehaengt: v.Beenden(null, null); break;
                            default: v.Beenden(null, S.ByeGrund.Entfernt); break;
                        }
                    }
                    break;
                case I.GeraetStatusGeaendert gs:
                    GeraetStatusVerteilen(gs);
                    break;
                case I.GeraetGeloescht gg:
                    if (_wartende.TryGetValue(gg.GeraetId, out var wartend)) wartend.Beenden(null, S.ByeGrund.Entfernt);
                    break;
                case I.KontoGeloescht kg:
                    Schreiben(L.InterneAuftraege.KontoEntfernen(kg.KontoId));
                    Komfort.KontoLoeschen(kg.KontoId);
                    // Mit dem Konto gehen Admin-Nachweis und Mitgliedsangaben (Datenschutz, 6.13).
                    if (Admins.Eintrag(kg.KontoId) is not null) Admins.Entziehen(kg.KontoId);
                    Mitglieder.KontoEntfernt(kg.KontoId);
                    break;
                case I.Meldung m:
                    var recht = m.Art switch
                    {
                        S.MeldungsArten.GeraetAntrag or S.MeldungsArten.AntragNeu => L.Recht.GeraeteFreigeben,
                        _ => L.Recht.KontenVerwalten,
                    };
                    MeldungSenden(m.Art, recht, m.KontoId, m.GeraetId, auchInhaber: m.Art == S.MeldungsArten.GeraetAntrag);
                    break;
                case I.BotNachricht bn:
                    // Der Bot-Dienst ist nicht Teil dieses Schritts (3.8); der Text bleibt aus dem Logbuch (Namen, Codes).
                    Log.Info("Bot-Meldung " + bn.Art + " an " + (bn.Ziel == I.BotZiel.AdminKanal ? "Admin-Kanal" : "Direktnachricht") + " (kein Bot-Dienst angebunden)");
                    break;
                case I.Alarm a:
                    Log.Warnung("Alarm " + a.Art);
                    break;
                case I.VerknuepfungVerfallen vv:
                    if (_adminVerknuepfungen.Remove(vv.Verbindung, out var tcs)) tcs.TrySetResult(VerwaltungsAntwort.Fehler("FRIST"));
                    break;
            }
        }
    }

    /// <summary>
    /// GeraetStatus (4.5) an Admin-Sitzungen mit <c>GERAETE_FREIGEBEN</c> (mit den passenden Vormerkungen als Vorschlag, F14),
    /// an Sitzungen desselben Kontos und an die wartende Verbindung dieses Geräts (nur für ihr eigenes Gerät, ohne
    /// Vormerkungen, P-44). Eine Freigabe setzt die wartende Verbindung fort (3.5), auch wenn das Gerät dabei an ein anderes
    /// Konto kam (zweites Gerät eines Mitglieds).
    /// </summary>
    private void GeraetStatusVerteilen(I.GeraetStatusGeaendert gs)
    {
        // Ein abgelehntes Gerät ist seit 0.7.0 schon fort; seine Angaben stehen dann im Stand davor.
        var stand = gs.Vorher ?? Konten.Stand;
        if (!stand.Geraete.TryGetValue(gs.GeraetId, out var g)) return;
        var push = GeraetStatusBauen(stand, g, gs.Status, gs.Durch, gs.Grund, mitVorschlaegen: false);
        var fuerAdmins = GeraetStatusBauen(stand, g, gs.Status, gs.Durch, gs.Grund, mitVorschlaegen: true);
        var admins = VerbindungenMitRecht(L.Recht.GeraeteFreigeben).ToList();
        var eigene = SitzungVonKonto(g.KontoId);
        _wartende.TryGetValue(gs.GeraetId, out var wartend);
        foreach (var v in admins)
            if (!ReferenceEquals(v, wartend)) v.Push(S.Nachrichtentyp.GeraetStatus, fuerAdmins);
        if (eigene is not null && !admins.Contains(eigene) && !ReferenceEquals(eigene, wartend)) eigene.Push(S.Nachrichtentyp.GeraetStatus, push);
        if (wartend is null) return;
        if (gs.Status != I.GeraetStatusMeldung.Freigegeben)
        {
            wartend.Push(S.Nachrichtentyp.GeraetStatus, push);
            return;
        }
        // Status und Freigabe über die Befehle der Verbindung: So stehen sie im Draht hinter der Antwort auf den Antrag und
        // GeraetStatus{WARTET}, auch wenn ein automatischer Admin-Client binnen Millisekunden freigibt (Rennen Antrag/WARTERAUM).
        wartend.KontoId = g.KontoId;
        wartend.FreigabeAnwenden(push);
        if (FreigabeZeitlupeMs > 0) Thread.Sleep(FreigabeZeitlupeMs);
    }

    /// <summary>GeraetStatus mit Namen des Antrags, vergebenen Namen und (nur für Admins) passenden Vormerkungen.</summary>
    internal S.GeraetStatus GeraetStatusBauen(I.Kontenstand stand, I.Geraet g, I.GeraetStatusMeldung status, uint? durch, string? grund, bool mitVorschlaegen)
    {
        stand.Konten.TryGetValue(g.KontoId, out var k);
        return Abbildung.GeraetStatus(stand, g, status, durch, grund, k is null ? [] : Konten.VergebeneNamen(k),
            mitVorschlaegen && k is { IstAntrag: true } ? Vorschlaege(k) : null,
            // Nur für Admins wie die Vorschläge: Dem Antragsteller verriete es, dass eine Vormerkung den Namen trägt (P-44).
            mitVorschlaegen && k is { IstAntrag: true } && Konten.DiscordNameVergeben(k));
    }

    /// <summary>Vormerkungen, die zu einem Antrag passen (F14), als Vorschlag für die Freigabeansicht.</summary>
    internal List<S.VormerkungVorschlag> Vorschlaege(I.Konto antrag)
    {
        var m = Mitglieder.Stand;
        return [.. Mitglieder.Vorschlaege(antrag.Antrag?.ScName, antrag.Antrag?.DiscordName ?? antrag.DiscordName)
            .Take(S.Grenzen.VorschlaegeMax)
            .Select(v => new S.VormerkungVorschlag
            {
                Id = v.Vormerkung.Id,
                ScName = v.Vormerkung.ScName,
                DiscordName = v.Vormerkung.DiscordName,
                Rollen = [.. v.Vormerkung.Rollen],
                UeberScName = v.UeberScName,
            })];
    }

    // ---- Sitzungs-IDs ----

    /// <summary>Zufällige Sitzungs-ID ungleich 0, weder aktiv noch in den letzten 24 h vergeben (4.6 ENDE).</summary>
    private uint NeueSitzungsId()
    {
        Span<byte> roh = stackalloc byte[4];
        while (true)
        {
            RandomNumberGenerator.Fill(roh);
            var id = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(roh);
            if (id == 0 || _benutzteIds.ContainsKey(id) || _sitzungen.ContainsKey(id)) continue;
            _benutzteIds[id] = 0;
            return id;
        }
    }

    // ---- Profil ----

    /// <summary>Lädt das offline signierte Serverprofil, das Welcome durchreicht (3.1). Ohne Profil gibt es kein FREIGEGEBEN.</summary>
    public string? ProfilLaden()
    {
        if (Konfiguration.Profil is not { } pfad) return "kein Profil konfiguriert";
        var datei = Konfiguration.Aufloesen(pfad);
        if (!File.Exists(datei)) return "Profildatei fehlt";
        if (!I.SigniertesProfil.TryLesen(File.ReadAllBytes(datei), out var profil)) return "Profil ist kein gültiges {daten, signatur}";
        _profil = new S.SigniertesProfil { Daten = Convert.ToBase64String(profil.Daten), Signatur = Convert.ToBase64String(profil.Signatur) };
        if (I.Serverprofil.TryLesen(profil.Daten, out var inhalt))
            Log.Info(string.Create(CultureInfo.InvariantCulture, $"Serverprofil Version {inhalt.ProfilVersion} mit {inhalt.Pins.Count} Pins geladen"));
        return null;
    }

    /// <summary>Das signierte Profil für Welcome.</summary>
    public S.SigniertesProfil? Profil => _profil;

    /// <summary>Der Pin des eigenen Serverschlüssels; der Dienst setzt ihn vor dem Laden des Profils (<see cref="ProfilPinWarnung"/>).</summary>
    public I.Fingerabdruck? EigenerPin { get; set; }

    /// <summary>
    /// Die Warnung, wenn das geladene Profil den Pin dieses Servers nicht nennt, mit Version und Kennung; sonst <c>null</c>.
    /// Beim Start und bei <c>profil-neu-laden</c> (0.4.0; vorher warnte nur der Start, ohne Version). So ein Profil ist fast
    /// immer ein Versehen, etwa das Abschlussprofil eines Umzugs auf dem alten Server (UMZUG.md, Schritt 4): Clients ab 0.4.0
    /// übernehmen es nicht (3.1), und ein Setup mit diesem Profil fände diesen Server nie.
    /// </summary>
    public string? ProfilPinWarnung()
    {
        if (EigenerPin is not { } pin || _profil is not { } p || !I.Serverprofil.TryLesen(Convert.FromBase64String(p.Daten), out var inhalt)) return null;
        if (inhalt.PinPasst(pin)) return null;
        return string.Create(CultureInfo.InvariantCulture,
            $"Der Pin des Serverschlüssels ({pin.Anzeige}) steht nicht im Serverprofil Version {inhalt.ProfilVersion}: Clients mit diesem Profil lehnen den Server ab und übernehmen es nicht (3.1). Ein Profil mit diesem Pin einspielen; funk-admin pruefen --vollstaendig zeigt es vorher.");
    }

    public async ValueTask DisposeAsync()
    {
        _arbeit.Writer.TryComplete();
        if (_schleife is not null) await _schleife.ConfigureAwait(false);
        if (Komfort.Geaendert) Dateiablage.AtomarSchreiben(_komfortPfad, Komfort.AlsJson());
        Dateiablage.AtomarSchreiben(_vorrangPfad, Spur.AlsJson());
        SperrlisteSpeichern();
        if (Konten.Stand.Version != _gespeicherteKontenVersion) _ablage.KontenSpeichern(Konten.Stand);
        if (Admins.Stand.Version != _gespeicherteAdminVersion) _ablage.AdminsSpeichern(Admins.Stand);
        if (Mitglieder.Stand.Version != _gespeicherteMitgliederVersion) _ablage.MitgliederSpeichern(Mitglieder.Stand);
        await Journal.WartenAsync().ConfigureAwait(false);
        if (Journal.Geaendert) Journal.Speichern();
        _tresor.Dispose();
    }
}

/// <summary>Antwort an das Admin-Werkzeug: ok, Text und optionale Daten.</summary>
public sealed record VerwaltungsAntwort(bool Ok, string Text, System.Text.Json.Nodes.JsonNode? Daten = null)
{
    public static VerwaltungsAntwort Gut(string text, System.Text.Json.Nodes.JsonNode? daten = null) => new(true, text, daten);

    public static VerwaltungsAntwort Fehler(string text) => new(false, text);
}
