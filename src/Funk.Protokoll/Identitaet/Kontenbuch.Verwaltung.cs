// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Identitaet;

/// <summary>
/// Ergebnis von <c>ProfilSetzen</c>, <c>KontoVerwaltung SCNAMEN</c> und <c>DISCORDNAME</c>: die bereinigten Namen oder der
/// vergebene; <see cref="DiscordVergeben"/> sagt, ob es ein Discord-Name war.
/// </summary>
public sealed record Namensergebnis(Fehlercode Fehler, IReadOnlyList<string> ScNamen, string? DiscordName, string? Vergeben)
    : Ergebnis(Fehler, [])
{
    public bool DiscordVergeben { get; init; }
}

public sealed partial class Kontenbuch
{
    /// <summary>Höchstens so viele SC-Namen je Konto (Hauptname zuerst).</summary>
    public const int MaxScNamen = 3;

    /// <summary>
    /// <c>KontoVerwaltung SPERREN</c> (3.6, 6.10): Status GESPERRT mit Grund ADMIN, alle Sitzungen des Kontos enden
    /// sofort. Der letzte aktive ADMIN nur über das Admin-Werkzeug. Heikel: frische Bestätigung (3.9). Einen anderen Admin
    /// darf ein Admin im Client sperren (Notbremse), den Eigentümer niemand.
    /// </summary>
    public Ergebnis KontoSperren(Akteur akteur, uint kontoId)
    {
        var s = _stand;
        var fehler = PruefeVerwaltung(s, akteur, kontoId, heikel: true, out _, out var ziel);
        if (fehler != Fehlercode.Keiner) return Ergebnis.Mit(fehler);
        if (!akteur.IstAdminWerkzeug && IstLetzterAktiverAdmin(s, ziel!)) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
        if (BestaetigungPruefen(akteur) is var b && b != Fehlercode.Keiner) return Ergebnis.Mit(b);
        if (ziel!.Status == KontoStatus.Gesperrt && ziel.Sperrgrund == Sperrgrund.Admin) return new Ergebnis(Fehlercode.Keiner, []);
        Uebernehmen(MitKonto(s, ziel with { Status = KontoStatus.Gesperrt, Sperrgrund = Sperrgrund.Admin }));
        return new Ergebnis(Fehlercode.Keiner, [new SitzungenBeenden(kontoId, null, Trenngrund.KontoGesperrt)]);
    }

    /// <summary>
    /// <c>KontoVerwaltung ENTSPERREN</c>: hebt jede Sperre auf, auch eine des Abgleichs. Ein ADMIN-Konto entsperrt nur die
    /// Konsole (3.9: im Client nur sperren, als Notbremse).
    /// </summary>
    public Ergebnis KontoEntsperren(Akteur akteur, uint kontoId)
    {
        var s = _stand;
        var fehler = PruefeVerwaltung(s, akteur, kontoId, heikel: true, out _, out var ziel, nurKonsoleFuerAdmins: true);
        if (fehler == Fehlercode.Keiner) fehler = BestaetigungPruefen(akteur);
        if (fehler != Fehlercode.Keiner) return Ergebnis.Mit(fehler);
        if (ziel!.Status == KontoStatus.Aktiv) return new Ergebnis(Fehlercode.Keiner, []);
        Uebernehmen(MitKonto(s, ziel with { Status = KontoStatus.Aktiv, Sperrgrund = Sperrgrund.Keiner }));
        return new Ergebnis(Fehlercode.Keiner, []);
    }

    /// <summary>
    /// <c>KontoVerwaltung STUFE</c> (6.10): nie die eigene, nie höher als die eigene Stufe, keine Rechte über die
    /// eigenen hinaus, den letzten aktiven ADMIN nicht herabstufen. Setzt die Rechte auf den Standard der neuen Stufe
    /// zurück. Wird das Konto dadurch privilegiert, verfallen seine Geräte in WARTET. Im Client (3.9, P10) nur FUNKER und
    /// RAIDLEITER für Konten, die weder ADMIN noch privilegiert sind, mit frischer Bestätigung; alles andere nur an der
    /// Konsole. Der Eigentümer bleibt ADMIN, auch an der Konsole: erst einen anderen Eigentümer setzen (3.10).
    /// </summary>
    public Ergebnis StufeSetzen(Akteur akteur, uint kontoId, Stufe stufe)
    {
        if (!Enum.IsDefined(stufe)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var s = _stand;
        var fehler = PruefeVerwaltung(s, akteur, kontoId, heikel: true, out var ak, out var ziel, nurKonsoleFuerAdmins: true);
        if (fehler != Fehlercode.Keiner) return Ergebnis.Mit(fehler);
        if (ziel!.Eigentuemer && stufe != Stufe.Admin) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (!akteur.IstAdminWerkzeug)
        {
            if (akteur.KontoId == kontoId || stufe > ak!.Stufe) return Ergebnis.Mit(Fehlercode.KeinRecht);
            if (stufe == Stufe.Admin || ziel!.Privilegiert) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
            if ((Rechteregeln.Standard(stufe) & ~ziel.Rechte & ~ak.Rechte) != 0) return Ergebnis.Mit(Fehlercode.KeinRecht);
            if (stufe != Stufe.Admin && IstLetzterAktiverAdmin(s, ziel)) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
            if (BestaetigungPruefen(akteur) is var b && b != Fehlercode.Keiner) return Ergebnis.Mit(b);
        }
        var neu = ziel! with { Stufe = stufe, Plus = Rechte.Keine, Minus = Rechte.Keine };
        if (neu == ziel) return new Ergebnis(Fehlercode.Keiner, []);
        return RechteUebernehmen(s, ziel, neu, $"Stufe von {Anzeige(neu)} ist jetzt {stufe.Drahtname()}.");
    }

    /// <summary>
    /// <c>KontoVerwaltung RECHTE</c> (6.10): setzt die wirksamen Rechte; gespeichert wird die Abweichung vom
    /// Standard der Stufe als <c>plus</c> und <c>minus</c>. Niemand vergibt ein Recht, das er nicht hat, und niemand
    /// ändert die eigenen Rechte. Verwaltungsrechte als Einzelrecht und Rechte privilegierter Konten nur an der Konsole.
    /// </summary>
    public Ergebnis RechteSetzen(Akteur akteur, uint kontoId, Rechte rechte)
    {
        if ((rechte & ~Rechteregeln.Alle) != 0) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var s = _stand;
        var fehler = PruefeVerwaltung(s, akteur, kontoId, heikel: true, out var ak, out var ziel, nurKonsoleFuerAdmins: true);
        if (fehler != Fehlercode.Keiner) return Ergebnis.Mit(fehler);
        if (!akteur.IstAdminWerkzeug)
        {
            if (akteur.KontoId == kontoId || (rechte & ~ziel!.Rechte & ~ak!.Rechte) != 0) return Ergebnis.Mit(Fehlercode.KeinRecht);
            if ((rechte & Rechteregeln.Privilegierend) != 0 || ziel.Privilegiert) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
            if (BestaetigungPruefen(akteur) is var b && b != Fehlercode.Keiner) return Ergebnis.Mit(b);
        }
        var standard = Rechteregeln.Standard(ziel!.Stufe);
        var neu = ziel with { Plus = rechte & ~standard, Minus = standard & ~rechte };
        if (neu == ziel) return new Ergebnis(Fehlercode.Keiner, []);
        return RechteUebernehmen(s, ziel, neu,
            $"Rechte von {Anzeige(neu)}: {string.Join(' ', Rechteregeln.Drahtnamen(neu.Rechte))}.");
    }

    /// <summary><c>KontoVerwaltung NAME</c>: eindeutig nach NFKC und Großschreibung, bei Kollision mit Zusatz. Admin-Sitzung.</summary>
    public Ergebnis NameSetzen(Akteur akteur, uint kontoId, string name)
    {
        if (!Zeichenregeln.IstText(name, 1, 32)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var s = _stand;
        var fehler = PruefeVerwaltung(s, akteur, kontoId, heikel: false, out _, out var ziel, nurKonsoleFuerAdmins: akteur.KontoId != kontoId);
        if (fehler != Fehlercode.Keiner) return Ergebnis.Mit(fehler);
        var eindeutig = EindeutigerName(s, name, kontoId);
        if (string.Equals(eindeutig, ziel!.Name, StringComparison.Ordinal)) return new Ergebnis(Fehlercode.Keiner, []);
        Uebernehmen(NameSetzenIntern(s, ziel, eindeutig));
        return new Ergebnis(Fehlercode.Keiner, []);
    }

    /// <summary>
    /// <c>KontoVerwaltung LOESCHEN</c> (3.6, DSGVO): Konto, Geräte samt Fingerabdrücken und Namen fort; Pegel, Ablage, Plätze,
    /// Admin-Nachweis und Mitgliedsangaben folgen über <see cref="KontoGeloescht"/>. Seit 0.7.0 kommt dabei nichts auf die
    /// Sperrliste (Sarevoc, 04.10.2026: Löschen heißt vollständig entfernen). ADMIN-Konten und den Eigentümer löscht nur die
    /// Konsole; sonst heikel.
    /// </summary>
    public Ergebnis KontoLoeschen(Akteur akteur, uint kontoId)
    {
        var s = _stand;
        var fehler = PruefeVerwaltung(s, akteur, kontoId, heikel: true, out _, out var ziel, nurKonsoleFuerAdmins: true);
        if (fehler != Fehlercode.Keiner) return Ergebnis.Mit(fehler);
        if (!akteur.IstAdminWerkzeug && IstLetzterAktiverAdmin(s, ziel!)) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
        if (BestaetigungPruefen(akteur) is var b && b != Fehlercode.Keiner) return Ergebnis.Mit(b);
        var w = new List<Wirkung>();
        Uebernehmen(KontoEntfernenIntern(s, ziel!, Trenngrund.KontoGeloescht, w));
        return new Ergebnis(Fehlercode.Keiner, w);
    }

    /// <summary>
    /// <c>KontoVerwaltung SCNAMEN</c> (F6): ein Admin korrigiert die SC-Namen eines Kontos (Admin-Sitzung, ohne frische
    /// Bestätigung, denn Namen verleihen keine Rechte). Eindeutig über alle Konten und Vormerkungen; sonst NAME_VERGEBEN.
    /// </summary>
    public Namensergebnis ScNamenSetzen(Akteur akteur, uint kontoId, IReadOnlyList<string> scNamen)
    {
        ArgumentNullException.ThrowIfNull(scNamen);
        var s = _stand;
        var fehler = PruefeVerwaltung(s, akteur, kontoId, heikel: false, out _, out var ziel, nurKonsoleFuerAdmins: akteur.KontoId != kontoId);
        if (fehler != Fehlercode.Keiner) return new(fehler, [], null, null);
        if (ziel!.IstAntrag) return new(Fehlercode.Ungueltig, [], null, null);
        return NamenSetzenIntern(s, ziel, scNamen, leerErlaubt: true);
    }

    /// <summary>
    /// <c>ProfilSetzen</c> (N2, F11): das eigene Konto setzt seine SC-Namen (1 bis 3, Hauptname zuerst). Eindeutig über alle
    /// Konten und Vormerkungen; sonst NAME_VERGEBEN mit dem Namen. Den Discord-Namen ändert seit A53 nur ein Admin
    /// (<see cref="DiscordNameSetzen"/>): Derselbe Wert wie bisher stört nicht (ältere Clients schicken ihn mit), ein anderer
    /// ergibt KEIN_RECHT, und dann ändert sich auch sonst nichts.
    /// </summary>
    public Namensergebnis ProfilSetzen(Akteur akteur, IReadOnlyList<string>? scNamen, string? discordName)
    {
        var s = _stand;
        if (akteur.IstAdminWerkzeug || !AkteurPruefen(s, akteur, out var ak)) return new(Fehlercode.KeinRecht, [], null, null);
        if (discordName is not null
            && !string.Equals(Zeichenregeln.DiscordNameBereinigen(discordName) ?? discordName, ak!.DiscordName, StringComparison.Ordinal))
            return new(Fehlercode.KeinRecht, [], null, null);
        return NamenSetzenIntern(s, ak!, scNamen, leerErlaubt: false);
    }

    /// <summary>
    /// <c>KontoVerwaltung DISCORDNAME</c> (A53, Schutz vor Täuschung in "Wer ist da"): Den Discord-Namen eines Kontos setzt nur
    /// ein Konto mit <c>KONTEN_VERWALTEN</c> in einer Admin-Sitzung (ohne frische Bestätigung wie SCNAMEN, denn Namen verleihen
    /// keine Rechte) oder die Konsole. Andere ADMIN-Konten und den Eigentümer nur die Konsole, das eigene Konto der Admin selbst
    /// (3.9, 3.10). Eindeutig über alle Konten und Vormerkungen nach NFKC und Großschreibung wie die SC-Namen, sonst
    /// NAME_VERGEBEN; <c>null</c> oder leer leert ihn.
    /// </summary>
    public Namensergebnis DiscordNameSetzen(Akteur akteur, uint kontoId, string? discordName)
    {
        var s = _stand;
        var fehler = PruefeVerwaltung(s, akteur, kontoId, heikel: false, out _, out var ziel, nurKonsoleFuerAdmins: akteur.KontoId != kontoId);
        if (fehler != Fehlercode.Keiner) return new(fehler, [], null, null);
        string? neu = null;
        if (discordName is not null && discordName.Trim(' ').Length > 0)
        {
            if (Zeichenregeln.DiscordNameBereinigen(discordName) is not { } bereinigt) return new(Fehlercode.Ungueltig, [], null, null);
            if (DiscordNameBesitzer(s, bereinigt, kontoId, null) is not null)
                return new(Fehlercode.NameVergeben, [], null, bereinigt) { DiscordVergeben = true };
            neu = bereinigt;
        }
        if (!string.Equals(neu, ziel!.DiscordName, StringComparison.Ordinal)) Uebernehmen(MitDiscordName(s, ziel, neu));
        return new(Fehlercode.Keiner, ziel.ScNamen, neu, null);
    }

    /// <summary>
    /// Wem ein Discord-Name gehört (A53): einem anderen Konto als <paramref name="eigenesKonto"/> oder einer Vormerkung außer
    /// <paramref name="auszer"/>; <c>null</c>, wenn er frei ist. Vergleich nach NFKC und Großschreibung wie bei den SC-Namen.
    /// </summary>
    public (uint? KontoId, uint? VormerkungId)? DiscordNameBesitzer(Kontenstand s, string name, uint eigenesKonto, uint? auszer)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(name);
        var schluessel = Zeichenregeln.Namensschluessel(name);
        if (s.NachDiscordName.TryGetValue(schluessel, out var konto) && konto != eigenesKonto) return (konto, null);
        if (VormerkungsDiscordName?.Invoke(schluessel) is { } vm && vm != auszer) return (null, vm);
        return null;
    }

    /// <summary>
    /// Ob der Discord-Name eines Antrags schon einem Konto oder einer Vormerkung gehört; dann gibt ihn der Server nicht frei,
    /// außer mit genau dieser Vormerkung (für <c>discordNameVergeben</c> in der Freigabeansicht und an der Konsole, A53).
    /// </summary>
    public bool DiscordNameVergeben(Konto antragskonto)
    {
        ArgumentNullException.ThrowIfNull(antragskonto);
        return antragskonto.Antrag is { } a && DiscordNameBesitzer(Stand, a.DiscordName, antragskonto.KontoId, null) is not null;
    }

    private Namensergebnis NamenSetzenIntern(Kontenstand s, Konto ziel, IReadOnlyList<string>? scNamen, bool leerErlaubt)
    {
        var namen = ziel.ScNamen.ToList();
        if (scNamen is not null)
        {
            if (scNamen.Count > MaxScNamen || (scNamen.Count == 0 && !leerErlaubt)) return new(Fehlercode.Ungueltig, [], null, null);
            namen.Clear();
            var schluessel = new HashSet<string>(StringComparer.Ordinal);
            foreach (var roh in scNamen)
            {
                if (Zeichenregeln.ScNameBereinigen(roh) is not { } n) return new(Fehlercode.Ungueltig, [], null, null);
                var k = Zeichenregeln.Namensschluessel(n);
                if (!schluessel.Add(k)) return new(Fehlercode.Ungueltig, [], null, null);
                if ((s.NachScName.TryGetValue(k, out var besitzer) && besitzer != ziel.KontoId) || VormerkungsNamen?.Invoke(k) == true)
                    return new(Fehlercode.NameVergeben, [], null, n);
                namen.Add(n);
            }
        }
        var dn = ziel.DiscordName;
        var neu = ziel with { ScNamen = [.. namen] };
        if (neu.ScNamen.SequenceEqual(ziel.ScNamen, StringComparer.Ordinal)) return new(Fehlercode.Keiner, namen, dn, null);
        s = MitScNamen(MitKonto(s, neu), ziel.KontoId, ziel.ScNamen, neu.ScNamen);
        Uebernehmen(s);
        return new(Fehlercode.Keiner, namen, dn, null);
    }

    /// <summary>
    /// Admin-Werkzeug <c>eigentuemer --konto</c> (3.10): setzt das Eigentümer-Merkmal auf genau dieses Konto (ein früherer
    /// Eigentümer verliert es). Nur ein freigegebenes ADMIN-Konto kann Eigentümer sein. Aus dem Client nicht möglich.
    /// </summary>
    public Ergebnis EigentuemerSetzen(Akteur akteur, uint kontoId)
    {
        if (!akteur.IstAdminWerkzeug) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var s = _stand;
        if (!s.Konten.TryGetValue(kontoId, out var ziel) || ziel.IstAntrag) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (ziel.Stufe != Stufe.Admin) return Ergebnis.Mit(Fehlercode.Ungueltig);
        foreach (var k in s.Konten.Values)
            if (k.Eigentuemer && k.KontoId != kontoId) s = MitKonto(s, k with { Eigentuemer = false });
        if (!ziel.Eigentuemer) s = MitKonto(s, ziel with { Eigentuemer = true });
        if (!ReferenceEquals(s, _stand)) Uebernehmen(s);
        return new Ergebnis(Fehlercode.Keiner, []);
    }

    /// <summary>Admin-Werkzeug <c>Konto anlegen</c> (3.8), etwa für den Bootstrap. Aus dem Client nicht möglich.</summary>
    public Anlageergebnis KontoAnlegen(Akteur akteur, string discordId, string benutzername, string? name, Stufe stufe)
    {
        if (!akteur.IstAdminWerkzeug) return new(Fehlercode.KeinRecht, 0, []);
        if (!Zeichenregeln.IstSnowflake(discordId) || !Zeichenregeln.IstBenutzername(benutzername) || !Enum.IsDefined(stufe))
            return new(Fehlercode.Ungueltig, 0, []);
        var s = _stand;
        if (s.NachDiscordId.ContainsKey(discordId)) return new(Fehlercode.Ungueltig, 0, []);
        if (s.Konten.Count >= MaxKonten || s.NaechsteKontoId > int.MaxValue) return new(Fehlercode.Grenze, 0, []);
        Uebernehmen(KontoAnlegenIntern(s, discordId, benutzername, name, stufe, Jetzt, out var konto));
        return new(Fehlercode.Keiner, konto.KontoId, []);
    }

    /// <summary>
    /// Server-Seite von <c>Mitgliedschaft{discordId, status, benutzername?, name?}</c> (3.6, 3.7, 3.8). NICHT_MITGLIED
    /// und ROLLE_FEHLT sperren mit Grund ABGLEICH, nie ein ADMIN-Konto und höchstens 3 Konten je Stunde; darüber nur
    /// Meldung und Alarm. OK hebt nur eine Sperre mit Grund ABGLEICH auf. <c>name</c> ist nur ein Vorschlag.
    /// </summary>
    public Ergebnis MitgliedschaftMelden(string discordId, Mitgliedschaftsstatus status, string? benutzername, string? name)
    {
        if (!Zeichenregeln.IstSnowflake(discordId) || !Enum.IsDefined(status)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var s = _stand;
        if (s.KontoZuDiscordId(discordId) is not { } alt) return new Ergebnis(Fehlercode.Keiner, []);
        var neu = alt;
        if (Zeichenregeln.IstBenutzername(benutzername)) neu = neu with { Benutzername = benutzername! };
        if (Zeichenregeln.IstText(name, 1, 32)) neu = neu with { NamensvorschlagAbgleich = name };

        var w = new List<Wirkung>();
        var wer = Anzeige(neu);
        if (status == Mitgliedschaftsstatus.Ok)
        {
            if (neu.Status == KontoStatus.Gesperrt && neu.Sperrgrund == Sperrgrund.Abgleich)
            {
                neu = neu with { Status = KontoStatus.Aktiv, Sperrgrund = Sperrgrund.Keiner };
                w.Add(new Meldung("ABGLEICH", neu.KontoId, null));
                w.Add(new BotNachricht(BotZiel.AdminKanal, null, "ABGLEICH", $"Abgleich: Sperre von {wer} aufgehoben."));
            }
        }
        else if (neu.Stufe == Stufe.Admin)
        {
            w.Add(new Meldung("ABGLEICH", neu.KontoId, null));
            w.Add(new BotNachricht(BotZiel.AdminKanal, null, "ABGLEICH", $"Abgleich meldet {wer} ohne Gilde oder Rolle. ADMIN-Konto, nicht gesperrt."));
        }
        else if (neu.Status == KontoStatus.Aktiv)
        {
            if (_abgleichSperren.Anzahl() >= AbgleichSperrenJeStunde)
            {
                w.Add(new Meldung("ABGLEICH", neu.KontoId, null));
                w.Add(new Alarm("ABGLEICH_GRENZE"));
                w.Add(new BotNachricht(BotZiel.AdminKanal, null, "ABGLEICH", $"Abgleich wollte {wer} sperren; Grenze je Stunde erreicht, nicht gesperrt."));
            }
            else
            {
                _abgleichSperren.Zaehlen();
                neu = neu with { Status = KontoStatus.Gesperrt, Sperrgrund = Sperrgrund.Abgleich };
                w.Add(new SitzungenBeenden(neu.KontoId, null, Trenngrund.KontoGesperrt));
                w.Add(new Meldung("ABGLEICH", neu.KontoId, null));
                w.Add(new BotNachricht(BotZiel.AdminKanal, null, "ABGLEICH", $"Abgleich: {wer} gesperrt (Gilde oder Rolle fehlt)."));
            }
        }
        if (neu != alt) Uebernehmen(MitKonto(s, neu));
        return new Ergebnis(Fehlercode.Keiner, w);
    }

    /// <summary>"benutzername (Name)" für Meldungen; ohne Discord-Benutzernamen nur der Name.</summary>
    private static string Anzeige(Konto k) => k.Benutzername.Length > 0 ? $"{k.Benutzername} ({k.Name})" : k.Name;

    /// <summary>
    /// Rechte zuerst (kein Hinweis auf Existenz), dann Admin-Sitzung, dann Ziel, dann Regel 3 aus 6.10, dann die Regeln
    /// für den Eigentümer und für ADMIN-Konten (3.9, 3.10). Die frische Bestätigung prüft jede heikle Handlung selbst, erst
    /// nach ihren eigenen Regeln: Was ohnehin verboten ist, fragt nicht nach einem Code.
    /// </summary>
    private static Fehlercode PruefeVerwaltung(Kontenstand s, Akteur akteur, uint kontoId, bool heikel, out Konto? ak, out Konto? ziel,
        bool nurKonsoleFuerAdmins = false)
    {
        ziel = null;
        if (!AkteurPruefen(s, akteur, out ak)) return Fehlercode.KeinRecht;
        var recht = VerwaltungsrechtPruefen(akteur, ak, Rechte.KontenVerwalten);
        if (recht != Fehlercode.Keiner) return recht;
        if (!s.Konten.TryGetValue(kontoId, out ziel) || ziel.IstAntrag) return Fehlercode.Unbekannt;
        if (akteur.IstAdminWerkzeug) return Fehlercode.Keiner;
        if (ziel.Stufe == Stufe.Admin && ak!.Stufe != Stufe.Admin) return Fehlercode.KeinRecht;
        // Das Konto des Eigentümers fasst im Client niemand an, nur er selbst seine Namen (3.10).
        if (ziel.Eigentuemer && (heikel || akteur.KontoId != ziel.KontoId)) return Fehlercode.NurAdminWerkzeug;
        // Andere Admins im Client nur sperren (Notbremse); entsperren, löschen, Stufe, Rechte und Namen nur an der Konsole (3.9).
        if (nurKonsoleFuerAdmins && ziel.Stufe == Stufe.Admin) return Fehlercode.NurAdminWerkzeug;
        return Fehlercode.Keiner;
    }

    /// <summary>Übernimmt geänderte Stufe oder Rechte, meldet sie (6.10 Regel 6) und lässt WARTET verfallen, falls privilegiert.</summary>
    private Ergebnis RechteUebernehmen(Kontenstand s, Konto alt, Konto neu, string text)
    {
        var jetzt = Jetzt;
        var w = new List<Wirkung>();
        s = MitKonto(s, neu);
        if (!alt.Privilegiert && neu.Privilegiert)
        {
            foreach (var g in s.GeraeteVon(neu.KontoId))
            {
                if (g.Status != GeraetStatus.Wartet) continue;
                s = Ereignis(OhneGeraet(s, g), neu.KontoId, jetzt);
                w.Add(new SitzungenBeenden(neu.KontoId, g.GeraetId, Trenngrund.GeraetEntfernt));
                w.Add(new GeraetGeloescht(g.GeraetId, neu.KontoId));
            }
        }
        Uebernehmen(s);
        w.Add(new Meldung("RECHTE_GEAENDERT", neu.KontoId, null));
        w.Add(new BotNachricht(BotZiel.AdminKanal, null, "RECHTE_GEAENDERT", text));
        return new Ergebnis(Fehlercode.Keiner, w);
    }
}
