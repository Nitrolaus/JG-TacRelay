// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Verwaltung ohne Server für die Tests der Oberfläche: eine Admin-Sitzung, Anträge, Mitglieder und Katalog eines erfundenen
/// Abends (seit 0.11.8 ohne Untergilden; die Felder füllt auch der Server nicht mehr). Heikle Handlungen fragen über
/// <see cref="IVerwaltungsdienst.CodeFragen"/> nach einem Code, solange <see cref="BrauchtCode"/> gesetzt ist, wie der Server mit <c>SCHRITT_BESTAETIGEN</c>. Alle Aufrufe stehen in
/// <see cref="Aufrufe"/>. Namen und Kennungen sind erfunden. Abzeichen (0.11.7): Sarevoc und Nordlicht mit Rängen und Tätigkeiten, Falke mit einem
/// Rang, Kobold ganz oben in der Marine und mit sechs Tätigkeiten (an beiden Grenzen), Kranich ohne.
/// </summary>
internal sealed class VerwaltungsdienstAttrappe : IVerwaltungsdienst
{
    public const string Kennung = "AK7Q 2M9X 4DFA 81C0";
    public const string GeheimnisRoh = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    public const string RichtigerCode = "123456";

    private readonly List<string> _aufrufe = [];

    /// <summary>Feste Zeit des Abends, damit Bilder und Texte gleich bleiben (30.09.2026, 20:12 Uhr Ortszeit).</summary>
    public static DateTimeOffset Abend { get; } = new(new DateTime(2026, 9, 30, 20, 12, 0, DateTimeKind.Local));

    public Verwaltungslage Lage { get; private set; } = Offen();

    public event Action? LageGeaendert;

    public event Action<object>? Nachricht;

    public Func<string, CancellationToken, Task<string?>>? CodeFragen { get; set; }

    /// <summary>Die nächste heikle Handlung braucht einen frischen Code (5 Minuten seit dem letzten sind um).</summary>
    public bool BrauchtCode { get; set; }

    public IReadOnlyList<string> Aufrufe
    {
        get
        {
            lock (_aufrufe) return [.. _aufrufe];
        }
    }

    public RollenkatalogAntwort Katalog { get; set; } = FunkDienstAttrappe.BeispielKatalog();

    public List<MitgliedEintrag> Mitglieder { get; } =
    [
        new MitgliedEintrag
        {
            KontoId = 1, Name = "Test", ScNamen = ["Mustermann"], DiscordName = "Mustermann/Max", Benutzername = "sarevoc", Rollen = [1, 2],
            Lieblingsrollen = [4], ErsteWahl = 1, Stufe = Stufe.Admin, Eigentuemer = true, Zustand = MitgliedZustand.Online, Admin = AdminStatus.Aktiv,
            Raenge = ["NAVY_09"], Taetigkeiten = ["T_CAPITAL", "T_FUNKER"],
        },
        new MitgliedEintrag
        {
            KontoId = 4, Name = "Nordlicht", ScNamen = ["Nordlicht", "Nordlicht Zwei"], DiscordName = "Nordlicht/Anna", Rollen = [3],
            Lieblingsrollen = [1, 3], ErsteWahl = 1, Stufe = Stufe.Admin, Zustand = MitgliedZustand.Online, Admin = AdminStatus.Aktiv, Notiz = "fliegt meist Gunner",
            Raenge = ["MARINE_04", "ORGA_05"], Taetigkeiten = ["T_GUNNER_M", "T_GUNNER_L", "T_SCHARF"],
        },
        new MitgliedEintrag
        {
            KontoId = 2, Name = "Falke", ScNamen = ["Falke"], DiscordName = "Falke/Tom", Rollen = [1], Stufe = Stufe.Funker, Zustand = MitgliedZustand.Online,
            Raenge = ["NAVY_02"], Taetigkeiten = ["T_JAEGER"],
        },
        new MitgliedEintrag { KontoId = 5, Name = "Kranich", ScNamen = ["Kranich"], DiscordName = "Kranich/Ben", Rollen = [], Stufe = Stufe.Raidleiter, Zustand = MitgliedZustand.Freigegeben },
        new MitgliedEintrag
        {
            KontoId = 3, Name = "Kobold", ScNamen = ["Kobold"], DiscordName = "Kobold", Rollen = [], Stufe = Stufe.Funker, Zustand = MitgliedZustand.Freigegeben,
            Raenge = ["MARINE_10"], Taetigkeiten = ["T_JAEGER", "T_BOMBER", "T_GUNNER_S", "T_ING", "T_EVA", "T_SCOUT"],
        },
        new MitgliedEintrag { VormerkungId = 7, Name = "Brandy", ScNamen = ["Brandy"], DiscordName = "Brandy/Lea", Rollen = [4], Zustand = MitgliedZustand.Vorgemerkt, Notiz = "kommt Freitag dazu" },
        new MitgliedEintrag { VormerkungId = 8, Name = "Mondschein", ScNamen = ["Mondschein"], Rollen = [5], Zustand = MitgliedZustand.Vorgemerkt },
        new MitgliedEintrag { KontoId = 21, Name = "Brandy", ScNamen = ["Brandy"], DiscordName = "Brandy/Lea", Rollen = [], Zustand = MitgliedZustand.Antrag },
    ];

    public List<GeraetEintrag> Antraege { get; } =
    [
        new GeraetEintrag
        {
            Id = 101, KontoId = 21, KontoName = "Brandy", Benutzername = "-", Name = "Gaming-PC", Anzeige = "7Q2K-9M4X-D3FA-81C0", Fingerabdruck = "7q2k9m4xd3fa81c0",
            SchluesselArt = SchluesselArt.Tpm, Status = GeraeteStatus.Wartet, Verknuepft = Abend.AddMinutes(-3).ToUnixTimeMilliseconds(),
            ScName = "Brandy", DiscordName = "Brandy/Lea", Antrag = true,
            Vormerkungen = [new VormerkungVorschlag { Id = 7, ScName = "Brandy", DiscordName = "Brandy/Lea", Rollen = [4], UeberScName = true }],
        },
        new GeraetEintrag
        {
            Id = 102, KontoId = 22, KontoName = "Falke", Benutzername = "-", Name = "Laptop", Anzeige = "C0DE-55A1-9B2E-4F10", Fingerabdruck = "c0de55a19b2e4f10",
            SchluesselArt = SchluesselArt.Software, Status = GeraeteStatus.Wartet, Verknuepft = Abend.AddMinutes(-1).ToUnixTimeMilliseconds(),
            ScName = "Falke", DiscordName = "Falke/Tim", Antrag = true, ScNamenVergeben = ["Falke"],
        },
    ];

    /// <summary>
    /// Geräte der Konten für die Mitgliederkarte (seit 0.8.0): Falke hat ein freigegebenes und ein gesperrtes, Nordlicht (Admin) ein
    /// gesperrtes, Kranich ein entferntes.
    /// </summary>
    public List<GeraetEintrag> Geraete { get; } =
    [
        Geraet(201, 2, "Falke", "PC-F4LK", "F4LK-2B7Q-91CE-0D3A", GeraeteStatus.Freigegeben),
        Geraet(202, 2, "Falke", "PC-7Z3Q", "7Z3Q-K8M2-4F1D-B6A0", GeraeteStatus.Entzogen),
        Geraet(401, 4, "Nordlicht", "PC-N0RD", "N0RD-5C2E-77A9-1B4F", GeraeteStatus.Entzogen),
        Geraet(501, 5, "Kranich", "PC-KR4N", "KR4N-0A9B-3C8D-E7F6", GeraeteStatus.Entfernt),
    ];

    private static GeraetEintrag Geraet(uint id, uint konto, string kontoName, string name, string anzeige, GeraeteStatus status) => new()
    {
        Id = id, KontoId = konto, KontoName = kontoName, Benutzername = "-", Name = name, Anzeige = anzeige, Fingerabdruck = anzeige.Replace("-", "", StringComparison.Ordinal),
        SchluesselArt = SchluesselArt.Tpm, Status = status, Verknuepft = Abend.AddDays(-20).ToUnixTimeMilliseconds(),
    };

    /// <summary>Eingerichteter Admin mit offener Sitzung bis 20:42 Uhr.</summary>
    public static Verwaltungslage Offen() => new()
    {
        Verbunden = true,
        Name = "Nordlicht",
        Admin = AdminStatus.Aktiv,
        SchluesselVorhanden = true,
        SitzungBis = Abend.AddMinutes(30),
        BestaetigtBis = Abend.AddMinutes(5),
    };

    public void LageSetzen(Func<Verwaltungslage, Verwaltungslage> aendern)
    {
        ArgumentNullException.ThrowIfNull(aendern);
        Lage = aendern(Lage);
        LageGeaendert?.Invoke();
    }

    public void Senden(object nachricht) => Nachricht?.Invoke(nachricht);

    private void Merken(string aufruf)
    {
        lock (_aufrufe) _aufrufe.Add(aufruf);
    }

    /// <summary>Wie der Server bei heiklen Handlungen: ohne frische Bestätigung erst einen Code holen.</summary>
    private async Task<string?> Heikel(string wofuer)
    {
        if (!BrauchtCode) return null;
        if (CodeFragen is null) return "Dafür braucht es einen frischen Code aus der Authenticator-App.";
        var code = await CodeFragen(wofuer, CancellationToken.None).ConfigureAwait(true);
        if (code is null) return "Abgebrochen; nichts geändert.";
        Merken("Code " + code);
        if (code != RichtigerCode) return "Der Code passt nicht.";
        BrauchtCode = false;
        return null;
    }

    /// <summary>Erfundener PCP_PLATFORM_TYPE in der Form, die Windows meldet.</summary>
    public const string Plattform = "TPM-Version:2.0 -Level:0-Revision:1.59-VendorID:'AMD '-Firmware:6.24.0.6";

    /// <summary>Was die Vorprüfung meldet; voreingestellt TPM 2.0 gefunden.</summary>
    public TpmStand Tpm { get; set; } = Tpmdeutung.AusPlattform(Plattform);

    /// <summary>Gesetzt: die Vorprüfung wartet, bis der Test sie mit einem Stand beendet.</summary>
    public TaskCompletionSource<TpmStand>? VoraussetzungenOffen { get; set; }

    /// <summary>So oft wurde die Vorprüfung gerufen (nicht in <see cref="Aufrufe"/>, dort zählen ältere Tests mit).</summary>
    public int VoraussetzungenGeprueft { get; private set; }

    public Task<TpmStand> VoraussetzungenAsync()
    {
        VoraussetzungenGeprueft++;
        return VoraussetzungenOffen?.Task ?? Task.FromResult(Tpm);
    }

    public Task<Verwaltungsantwort<string>> AntragStellenAsync()
    {
        Merken("Antrag");
        LageSetzen(l => l with { SchluesselVorhanden = true });
        return Task.FromResult(Verwaltungsantwort.Gut(Kennung));
    }

    public Task<Verwaltungsantwort<AdminEinrichtungAntwort>> GeheimnisAbholenAsync()
    {
        Merken("Geheimnis");
        return Task.FromResult(Verwaltungsantwort.Gut(new AdminEinrichtungAntwort
        {
            Geheimnis = GeheimnisRoh,
            Uri = "otpauth://totp/JG%20TacRelay:Nordlicht?secret=" + GeheimnisRoh + "&issuer=JG%20TacRelay&algorithm=SHA1&digits=6&period=30",
            GueltigBis = Abend.AddMinutes(10).ToUnixTimeMilliseconds(),
        }));
    }

    public Task<Verwaltungsantwort<IReadOnlyList<string>>> EinrichtungBestaetigenAsync(string code)
    {
        Merken("Einrichtung " + code);
        if (Codeform.Totp(code) != RichtigerCode) return Task.FromResult(Verwaltungsantwort.Schlecht<IReadOnlyList<string>>("Der Code passt nicht. Uhrzeit am Telefon prüfen und den nächsten nehmen."));
        IReadOnlyList<string> codes = [.. Enumerable.Range(0, 10).Select(i => "K7M" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-Q2X9-D4FA")];
        LageSetzen(l => l with { Admin = AdminStatus.Aktiv });
        return Task.FromResult(Verwaltungsantwort.Gut(codes));
    }

    public Task<Verwaltungsantwort<AdminSitzungAntwort>> AnmeldenAsync(string code, bool notfallcode)
    {
        Merken("Anmelden " + code + (notfallcode ? " (Notfall)" : ""));
        if (!notfallcode && Codeform.Totp(code) != RichtigerCode)
            return Task.FromResult(Verwaltungsantwort.Schlecht<AdminSitzungAntwort>("Der Code passt nicht. Noch 4 Versuche, dann ist der Zweitfaktor gesperrt."));
        LageSetzen(l => l with { SitzungBis = Abend.AddMinutes(30), BestaetigtBis = Abend.AddMinutes(5), NotfallcodesUebrig = notfallcode ? 2 : null });
        return Task.FromResult(Verwaltungsantwort.Gut(new AdminSitzungAntwort
        {
            Bis = Abend.AddMinutes(30).ToUnixTimeMilliseconds(),
            BestaetigtBis = Abend.AddMinutes(5).ToUnixTimeMilliseconds(),
        }));
    }

    public Task AbmeldenAsync()
    {
        Merken("Abmelden");
        LageSetzen(l => l with { SitzungBis = null, BestaetigtBis = null });
        return Task.CompletedTask;
    }

    public Task<Verwaltungsantwort<IReadOnlyList<GeraetEintrag>>> AntraegeAsync()
    {
        Merken("Anträge");
        return Task.FromResult(Verwaltungsantwort.Gut<IReadOnlyList<GeraetEintrag>>([.. Antraege]));
    }

    public async Task<string?> EntscheidenAsync(uint geraetId, GeraetEntscheidung entscheidung, uint? vormerkungId = null, uint? kontoId = null)
    {
        if (await Heikel(entscheidung == GeraetEntscheidung.Freigeben ? "Gerät freigeben" : "Antrag ablehnen").ConfigureAwait(true) is { } f) return f;
        Merken($"Entscheiden {geraetId} {entscheidung} v{vormerkungId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} k{kontoId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}");
        Antraege.RemoveAll(a => a.Id == geraetId);
        return null;
    }

    public async Task<Verwaltungsantwort<int>> UnpassendeAblehnenAsync()
    {
        if (await Heikel("Anträge ablehnen").ConfigureAwait(true) is { } f) return Verwaltungsantwort.Schlecht<int>(f);
        var n = Antraege.RemoveAll(a => a.Vormerkungen is not { Count: > 0 });
        Merken("Unpassende " + n);
        return Verwaltungsantwort.Gut(n);
    }

    public Task<Verwaltungsantwort<IReadOnlyList<GeraetEintrag>>> GeraeteAsync(uint kontoId)
    {
        Merken("Geräte " + kontoId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Task.FromResult(Verwaltungsantwort.Gut<IReadOnlyList<GeraetEintrag>>([.. Geraete.Where(g => g.KontoId == kontoId)]));
    }

    /// <summary>Wie der Server ab 0.8.0: nur ein gesperrtes Gerät, danach ENTFERNT (3.6).</summary>
    public async Task<string?> SperreAufhebenAsync(uint geraetId)
    {
        if (!Lage.SperreAufhebenBekannt) return Verwaltungstexte.SperreAufhebenZuAlt;
        if (await Heikel("Sperre aufheben").ConfigureAwait(true) is { } f) return f;
        Merken("Sperre aufheben " + geraetId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var i = Geraete.FindIndex(g => g.Id == geraetId);
        if (i < 0 || Geraete[i].Status != GeraeteStatus.Entzogen) return Verwaltungstexte.NichtGesperrt;
        Geraete[i] = Geraete[i] with { Status = GeraeteStatus.Entfernt };
        return null;
    }

    public Task<Verwaltungsantwort<IReadOnlyList<MitgliedEintrag>>> MitgliederAsync()
    {
        Merken("Mitglieder");
        return Task.FromResult(Verwaltungsantwort.Gut<IReadOnlyList<MitgliedEintrag>>([.. Mitglieder]));
    }

    public Task<string?> VormerkenAsync(string scName, string? discordName, IReadOnlyList<ushort> rollen, string? notiz)
    {
        Merken($"Vormerken {scName} {discordName} [{string.Join(",", rollen)}] {notiz}");
        return Task.FromResult<string?>(null);
    }

    public Task<string?> KontoAendernAsync(uint kontoId, IReadOnlyList<ushort> rollen, string notiz)
    {
        Merken($"Konto {kontoId} [{string.Join(",", rollen)}] {notiz}");
        return Task.FromResult<string?>(null);
    }

    /// <summary>Wie der Server (0.11.7): Prüfregeln aus dem Katalog, gespeichert in Katalogfolge, am Mitglied der Liste.</summary>
    public Task<Verwaltungsantwort<AbzeichenAntwort>> AbzeichenSetzenAsync(uint kontoId, IReadOnlyList<string> raenge, IReadOnlyList<string> taetigkeiten)
    {
        Merken($"Abzeichen {kontoId} [{string.Join(",", raenge)}] [{string.Join(",", taetigkeiten)}]");
        if (Funk.Protokoll.Identitaet.Mitgliederbuch.AbzeichenPruefen(raenge, taetigkeiten) is { } grund)
            return Task.FromResult(Verwaltungsantwort.Schlecht<AbzeichenAntwort>(grund));
        var r = Abzeichenkatalog.Ordnen(raenge);
        var t = Abzeichenkatalog.Ordnen(taetigkeiten);
        var i = Mitglieder.FindIndex(m => m.KontoId == kontoId);
        if (i < 0) return Task.FromResult(Verwaltungsantwort.Schlecht<AbzeichenAntwort>("Konto unbekannt"));
        Mitglieder[i] = Mitglieder[i] with { Raenge = r.IsEmpty ? null : [.. r], Taetigkeiten = t.IsEmpty ? null : [.. t] };
        return Task.FromResult(Verwaltungsantwort.Gut(new AbzeichenAntwort { KontoId = kontoId, Raenge = [.. r], Taetigkeiten = [.. t] }));
    }

    /// <summary>Wie der Server (0.12.0): der Haken am Mitglied der Liste, nur für Konten; an einem älteren Server der Satz des Dienstes.</summary>
    public Task<string?> MeldungenSendenSetzenAsync(uint kontoId, bool an)
    {
        Merken($"Meldungen {kontoId} {(an ? "an" : "aus")}");
        if (!Lage.MeldungenBekannt) return Task.FromResult<string?>(Verwaltungstexte.MeldungenZuAlt);
        var i = Mitglieder.FindIndex(m => m.KontoId == kontoId);
        if (i < 0) return Task.FromResult<string?>("Konto unbekannt");
        Mitglieder[i] = Mitglieder[i] with { MeldungenSenden = an ? true : null };
        return Task.FromResult<string?>(null);
    }

    public Task<string?> DiscordNameSetzenAsync(uint kontoId, string? discordName)
    {
        Merken($"Discord {kontoId} {discordName}");
        // Wie der Server (A53): eindeutig über Konten und Vormerkungen, nach NFKC und ohne Groß und klein.
        if (discordName is not null)
        {
            var schluessel = Funk.Protokoll.Identitaet.Namensregeln.Vergleichsschluessel(discordName);
            if (Mitglieder.Any(m => m.KontoId != kontoId && m.Zustand != MitgliedZustand.Antrag && m.DiscordName is { } d
                && Funk.Protokoll.Identitaet.Namensregeln.Vergleichsschluessel(d) == schluessel))
                return Task.FromResult<string?>("Den Discord-Namen " + discordName + " hat schon jemand.");
        }
        var i = Mitglieder.FindIndex(m => m.KontoId == kontoId);
        if (i >= 0) Mitglieder[i] = Mitglieder[i] with { DiscordName = discordName };
        return Task.FromResult<string?>(null);
    }

    public Task<string?> VormerkungAendernAsync(uint vormerkungId, string scName, string? discordName, IReadOnlyList<ushort> rollen, string notiz)
    {
        Merken($"Vormerkung {vormerkungId} {scName} {discordName} [{string.Join(",", rollen)}] {notiz}");
        return Task.FromResult<string?>(null);
    }

    public Task<string?> VormerkungLoeschenAsync(uint vormerkungId)
    {
        Merken("Vormerkung löschen " + vormerkungId);
        return Task.FromResult<string?>(null);
    }

    public async Task<string?> KontoLoeschenAsync(uint kontoId)
    {
        if (await Heikel("Konto löschen").ConfigureAwait(true) is { } f) return f;
        // Wie der Server (3.6, 3.10): ADMIN-Konten und den Eigentümer löscht nur die Konsole.
        if (Mitglieder.Any(m => m.KontoId == kontoId && (m.Stufe == Stufe.Admin || m.Eigentuemer == true)))
            return "Das geht nur an der Serverkonsole (funk-admin), nicht im Client.";
        Merken("Konto löschen " + kontoId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Mitglieder.RemoveAll(m => m.KontoId == kontoId);
        return null;
    }

    public Task<Verwaltungsantwort<Einfuegeergebnis>> EinfuegenAsync(IReadOnlyList<string> zeilen)
    {
        Merken("Einfügen " + zeilen.Count);
        // Wie der Server: mehr als vier Teile ist ein Fehler dieser Zeile; Spalte 3 (Untergilde) wird seit 0.11.8 übergangen, mit Hinweis.
        var fehler = new List<Zeilenfehler>();
        var spalte3 = false;
        for (var i = 0; i < zeilen.Count; i++)
        {
            var teile = zeilen[i].Split(';');
            if (teile.Length > 4) fehler.Add(new Zeilenfehler { Zeile = (ushort)(i + 1), Grund = "mehr als vier Teile" });
            else if (teile.Length >= 3 && teile[2].Trim().Length > 0) spalte3 = true;
        }
        var hinweis = fehler.Count == 0 && spalte3 ? Funk.Protokoll.Identitaet.Mitgliederbuch.Spalte3Hinweis : null;
        return Task.FromResult(Verwaltungsantwort.Gut(new Einfuegeergebnis(fehler, hinweis)));
    }

    public Task<Verwaltungsantwort<RollenkatalogAntwort>> KatalogAsync()
    {
        Merken("Katalog");
        return Task.FromResult(Verwaltungsantwort.Gut(Katalog));
    }

    public Task<string?> KatalogSetzenAsync(IReadOnlyList<Rollenkatalogeintrag> eintraege, long version)
    {
        Merken($"Katalog setzen {eintraege.Count} v{version}");
        if (version != Katalog.Version) return Task.FromResult<string?>("Der Stand hat sich inzwischen geändert; neu laden und noch einmal.");
        Katalog = new RollenkatalogAntwort { Eintraege = eintraege, Version = version + 1 };
        return Task.FromResult<string?>(null);
    }

    // ---------------------------------------------------------------- Einsatzleitung und Flotte (0.3.0)

    private Funk.Server.Lage.Zustandsschreiber? _flotte;

    /// <summary>Aufträge des Abends: Falke leitet seit einer Stunde bis zum nächsten Morgen, erteilt von Nordlicht.</summary>
    public List<BeauftragungEintrag> Auftraege { get; } =
    [
        new BeauftragungEintrag
        {
            KontoId = 2, Name = "Falke", ScName = "Falke", Von = Abend.AddHours(-1).ToUnixTimeMilliseconds(), Bis = Auftragszeit.NaechsterMorgen(Abend.ToUnixTimeMilliseconds()),
            Rechte = [Recht.GlobalSenden, Recht.Durchsage, Recht.DurchsageAlle, Recht.MehrfachSenden, Recht.Zuweisen, Recht.SprechanzeigeAlle],
            Durch = 4, Seit = Abend.AddHours(-2).ToUnixTimeMilliseconds(), Grund = "Op Nachtfalke", Aktiv = true,
        },
    ];

    /// <summary>Wie viele gerade eingeteilt sind (Rückfrage beim Übernehmen ab 6).</summary>
    public int Eingeteilte { get; set; } = 3;

    public Dictionary<ushort, int> NetzMitglieder { get; } = [];

    public string? Ablageordner { get; set; }

    /// <summary>Das nächste Übernehmen scheitert an VERALTET, wie wenn ein anderer Admin dazwischen kam.</summary>
    public bool NaechsteFlotteVeraltet { get; set; }

    /// <summary>Die Flotte hält ein echter Zustandsschreiber: Schiffsflotte, dazu ein Trupp und eine Orga-Einheit.</summary>
    private Funk.Server.Lage.Zustandsschreiber Server()
    {
        if (_flotte is not null) return _flotte;
        var s = FlottenentwurfTests.SchiffsflottenServer();
        var e = Funk.Verwaltung.Flotte.Flottenentwurf.Aus(Funk.Server.Steuer.Abbildung.NetzListe(s.Aktuell));
        e.BausteinTrupp();
        e.BausteinOrga();
        FlottenentwurfTests.Anwenden(s, e.Paket().Paket!);
        return _flotte = s;
    }

    /// <summary>Der Server kennt Phasen (0.5.0, wie der Zustandsschreiber dahinter); <c>false</c> spielt einen Server 0.4.0.</summary>
    public bool PhasenBekannt { get; set; } = true;

    /// <summary>Der Server kennt Kanäle mit Ch. (0.6.0, wie der Zustandsschreiber dahinter); <c>false</c> spielt einen Server 0.5.0.</summary>
    public bool KanaeleBekannt { get; set; } = true;

    /// <summary>Die laufende Phase, wie sie 0x4004 meldete.</summary>
    public byte LaufendePhase { get; set; } = 1;

    public Flottenlage Flotte => new()
    {
        NetzListe = Funk.Server.Steuer.Abbildung.NetzListe(Server().Aktuell),
        Eingeteilte = Eingeteilte,
        Mitglieder = new Dictionary<ushort, int>(NetzMitglieder),
        PhasenBekannt = PhasenBekannt,
        KanaeleBekannt = KanaeleBekannt,
        LaufendePhase = LaufendePhase,
    };

    /// <summary>Ein anderer Admin ändert die Flotte am Server; die NetzListe kommt neu.</summary>
    public void FlotteVonAussen(Action<Funk.Verwaltung.Flotte.Flottenentwurf> aendern)
    {
        ArgumentNullException.ThrowIfNull(aendern);
        var e = Funk.Verwaltung.Flotte.Flottenentwurf.Aus(Flotte.NetzListe!);
        aendern(e);
        FlottenentwurfTests.Anwenden(Server(), e.Paket().Paket!);
        LageGeaendert?.Invoke();
    }

    public Task<Verwaltungsantwort<IReadOnlyList<BeauftragungEintrag>>> AuftraegeAsync()
    {
        Merken("Aufträge");
        return Task.FromResult(Verwaltungsantwort.Gut<IReadOnlyList<BeauftragungEintrag>>([.. Auftraege]));
    }

    public async Task<Verwaltungsantwort<IReadOnlyList<BeauftragungEintrag>>> AuftragErteilenAsync(uint kontoId, long? von, long? bis, IReadOnlyList<Recht> rechte, string? grund)
    {
        ArgumentNullException.ThrowIfNull(rechte);
        if (await Heikel("Einsatzleitung vergeben").ConfigureAwait(true) is { } f) return Verwaltungsantwort.Schlecht<IReadOnlyList<BeauftragungEintrag>>(f);
        Merken("Auftrag " + kontoId + " von " + (von is { } v ? Auftragszeit.Anzeigen(v) : "jetzt") + " bis " + (bis is { } b ? Auftragszeit.Anzeigen(b) : "04:00")
            + " [" + string.Join(",", rechte) + "] " + grund);
        var jetzt = Abend.ToUnixTimeMilliseconds();
        var m = Mitglieder.First(x => x.KontoId == kontoId);
        Auftraege.RemoveAll(a => a.KontoId == kontoId);
        Auftraege.Add(new BeauftragungEintrag
        {
            KontoId = kontoId, Name = m.Name, ScName = m.ScNamen.Count > 0 ? m.ScNamen[0] : null, Von = von ?? jetzt, Bis = bis ?? Auftragszeit.NaechsterMorgen(von ?? jetzt), Rechte = rechte,
            Durch = 4, Seit = jetzt, Grund = grund, Aktiv = (von ?? jetzt) <= jetzt,
        });
        return Verwaltungsantwort.Gut<IReadOnlyList<BeauftragungEintrag>>([.. Auftraege]);
    }

    public async Task<string?> AuftragEntziehenAsync(uint kontoId)
    {
        if (await Heikel("Einsatzleitung entziehen").ConfigureAwait(true) is { } f) return f;
        Merken("Entziehen " + kontoId);
        Auftraege.RemoveAll(a => a.KontoId == kontoId);
        return null;
    }

    public async Task<string?> FlotteUebernehmenAsync(VerwaltungsPaket paket, long version)
    {
        ArgumentNullException.ThrowIfNull(paket);
        if (await Heikel("Flotte übernehmen").ConfigureAwait(true) is { } f) return f;
        Merken("Flotte " + paket.Ops.Count + " v" + version);
        if (NaechsteFlotteVeraltet || version != Server().Aktuell.NetzListeVersion)
        {
            NaechsteFlotteVeraltet = false;
            return "Der Stand hat sich inzwischen geändert. Neu laden und noch einmal.";
        }
        FlottenentwurfTests.Anwenden(Server(), paket);
        LageGeaendert?.Invoke();
        return null;
    }
}
