// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Globalization;
using Abzeichenkatalog = Funk.Protokoll.Steuerung.Abzeichenkatalog;

namespace Funk.Protokoll.Identitaet;

// Liegt im Server (funk-server), nicht in der Client-Bibliothek Funk.Protokoll: Das normale Setup bringt keinen
// Verwaltungscode mit (A45, Gegenprüfung Stufe 1). Der Namensraum bleibt, damit Spezifikation, Server und Tests dieselben
// Namen tragen.

/// <summary>Klasse einer Raid-Helper-Anmeldung, auf die eine Katalogrolle zeigt (erst mit S4 benutzt).</summary>
public sealed record RaidHelperKlasse(string ClassName, string SpecName);

/// <summary>
/// Eintrag des Rollenkatalogs (6.13, A46): was jemand kann, etwa "Medium Fighter Pilot". <see cref="Rollen"/> zeigt auf
/// Rollen der Flotte (Kanalvorlagen), die dazu passen; das eine bleibt, das andere ändert sich mit jeder Flotte.
/// </summary>
public sealed record Katalogrolle(ushort Id, string Name)
{
    public string? Gruppe { get; init; }

    public ImmutableArray<ushort> Rollen { get; init => field = value.IsDefault ? [] : value; } = [];

    public ImmutableArray<RaidHelperKlasse> RaidHelper { get; init => field = value.IsDefault ? [] : value; } = [];
}

/// <summary>
/// Vormerkung (6.13): ein Mitglied, das sich noch nicht angemeldet hat, mit SC-Name, Discord-Name, Mitgliederrollen und
/// einer Notiz nur für Admins. Trägt keine Rechte; übernommen wird sie nur ausdrücklich bei der Freigabe.
/// </summary>
public sealed record Vormerkung(uint Id, string ScName)
{
    public string? DiscordName { get; init; }

    public ImmutableArray<ushort> Rollen { get; init => field = value.IsDefault ? [] : value; } = [];

    public string? Notiz { get; init; }

    public DateTimeOffset Angelegt { get; init; }

    /// <summary>Konto des Admins, 0 = Konsole.</summary>
    public uint Durch { get; init; }
}

/// <summary>Mitgliedsangaben eines Kontos: Rollen vom Admin, Lieblingsrollen vom Mitglied selbst, Zeichen ja oder nein.</summary>
public sealed record Mitgliedsdaten(uint KontoId)
{
    /// <summary>Mitgliederrollen, vom Admin eingetragen (höchstens 16); der Nutzer kann sie nicht abwählen.</summary>
    public ImmutableArray<ushort> Rollen { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Lieblingsrollen, vom Mitglied abgehakt (höchstens 8), nur aus dem Katalog.</summary>
    public ImmutableArray<ushort> Lieblingsrollen { get; init => field = value.IsDefault ? [] : value; } = [];

    public ushort? ErsteWahl { get; init; }

    /// <summary>"Mein Zeichen zeigen" (A49), Standard an.</summary>
    public bool ZeichenZeigen { get; init; } = true;

    /// <summary>
    /// "Meine Angaben in Wer ist da zeigen" (Sarevoc, 30.09.2026, Antwort 3), Standard an: Zeichen, Abzeichen und Rollen sieht
    /// dann jeder; ohne Freigabe sehen andere nur den Namen, die Einsatzleitung und Admins weiter alles.
    /// </summary>
    public bool KarteFreigeben { get; init; } = true;

    /// <summary>Notiz nur für Admins (80 Zeichen), aus der Vormerkung übernommen.</summary>
    public string? Notiz { get; init; }

    /// <summary>Ränge (0.11.7), vom Admin vergeben: höchstens einer je Laufbahn, in Laufbahnfolge gespeichert.</summary>
    public ImmutableArray<string> Raenge { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Tätigkeiten (0.11.7), vom Admin vergeben: höchstens 6, in Katalogfolge gespeichert.</summary>
    public ImmutableArray<string> Taetigkeiten { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Das Abzeichen "vorn" (0.11.7), vom Mitglied selbst gewählt; <c>null</c> = keine Wahl.</summary>
    public string? AbzeichenVorn { get; init; }

    /// <summary>
    /// Ausgeblendete Abzeichen und automatische Zeichen (0.11.7), vom Mitglied selbst: "ein Abzeichen haben ist eine Sache, es
    /// zeigen eine eigene Entscheidung" (Sarevoc). Ändert nur die Anzeige bei anderen, nie Rechte.
    /// </summary>
    public ImmutableArray<string> AbzeichenAus { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Alle Abzeichen: Ränge, dann Tätigkeiten (Methode, kein Feld: steht nicht in <c>mitglieder.json</c>).</summary>
    public IEnumerable<string> AlleAbzeichen() => Raenge.Concat(Taetigkeiten);

    /// <summary>
    /// Haken "darf Meldungen senden" (Meldesystem 0.12.0, M15-M18), nur vom Admin in der Admin-Sitzung gesetzt; bleibt, bis ein Admin
    /// ihn wegnimmt. Wirkt nur bei Führung, Admin oder Einsatzleitung mit laufendem Auftrag; eine ältere <c>mitglieder.json</c> lädt
    /// ohne ihn.
    /// </summary>
    public bool MeldungenSenden { get; init; }
}

/// <summary>
/// Untergilde (A47), entfallen seit 0.11.8 (Sarevoc: "Untergilden entfallen ganz, überall"). Der Datensatz bleibt nur, damit
/// <c>untergilden</c> aus <c>mitglieder.json</c> unverändert geladen und wieder geschrieben wird: Eine ältere Serverfassung
/// liest das Feld ohne Prüfung (NullReference, wenn es fehlt), der Rückweg bleibt so offen. Nichts liest oder zeigt es; geändert
/// wird es nur, wenn ein Konto oder eine Vormerkung wegfällt: Deren Nummer fällt heraus (Datenschutz, 15.10.2026).
/// </summary>
public sealed record Untergilde(ushort Id, string Name, string Kuerzel)
{
    public ImmutableArray<uint> Konten { get; init => field = value.IsDefault ? [] : value; } = [];

    public ImmutableArray<uint> Vormerkungen { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Einheit, die sie meist fliegt (schiffId), nur Anzeige.</summary>
    public ushort? StammEinheit { get; init; }

    /// <summary>Ansprechpartner (Konto), nur Anzeige, ohne Rechte.</summary>
    public uint? Ansprechpartner { get; init; }
}

/// <summary>Unveränderlicher Stand des Mitgliederbuchs (<c>mitglieder.json</c>).</summary>
public sealed record Mitgliederstand
{
    public static Mitgliederstand Leer { get; } = new();

    public ImmutableSortedDictionary<ushort, Katalogrolle> Katalog { get; init; } = ImmutableSortedDictionary<ushort, Katalogrolle>.Empty;

    /// <summary>Version des Katalogs für <c>Rollenkatalog SETZEN</c> mit Versionsprüfung.</summary>
    public long KatalogVersion { get; init; }

    public ImmutableSortedDictionary<uint, Vormerkung> Vormerkungen { get; init; } = ImmutableSortedDictionary<uint, Vormerkung>.Empty;

    public uint NaechsteVormerkung { get; init; } = 1;

    public ImmutableDictionary<uint, Mitgliedsdaten> Mitglieder { get; init; } = ImmutableDictionary<uint, Mitgliedsdaten>.Empty;

    /// <summary>Untergilden aus einem Stand vor 0.11.8: nur durchgereicht (siehe <see cref="Untergilde"/>), nie gelesen; nur Nummern gelöschter Konten und Vormerkungen fallen heraus.</summary>
    public ImmutableSortedDictionary<ushort, Untergilde> Untergilden { get; init; } = ImmutableSortedDictionary<ushort, Untergilde>.Empty;

    /// <summary>Namensraum der Vormerkungen (NFKC, Großschreibung); gebaut, nicht gespeichert.</summary>
    public ImmutableDictionary<string, uint> NachScName { get; init; } = ImmutableDictionary.Create<string, uint>(StringComparer.Ordinal);

    /// <summary>Discord-Namen der Vormerkungen (NFKC, Großschreibung, A53); gebaut, nicht gespeichert.</summary>
    public ImmutableDictionary<string, uint> NachDiscordName { get; init; } = ImmutableDictionary.Create<string, uint>(StringComparer.Ordinal);

    public long Version { get; init; }

    public Mitgliedsdaten DatenVon(uint kontoId) => Mitglieder.TryGetValue(kontoId, out var m) ? m : new Mitgliedsdaten(kontoId);

    /// <summary>Baut den Namensindex aus den Vormerkungen (nach dem Laden).</summary>
    public Mitgliederstand MitIndex()
    {
        // Discord-Namen der Vormerkungen (A53); bei Doppelten aus einem alten Stand gewinnt die kleinere Nummer. Ebenso bei den
        // SC-Namen: Seit ẞ wie ß zählt (01.10.2026), können zwei gespeicherte Vormerkungen denselben Schlüssel haben; das Laden
        // darf daran nicht scheitern.
        var sc = ImmutableDictionary.CreateBuilder<string, uint>(StringComparer.Ordinal);
        var discord = ImmutableDictionary.CreateBuilder<string, uint>(StringComparer.Ordinal);
        foreach (var v in Vormerkungen.Values.OrderBy(v => v.Id))
        {
            sc.TryAdd(Zeichenregeln.Namensschluessel(v.ScName), v.Id);
            if (v.DiscordName is { } d) discord.TryAdd(Zeichenregeln.Namensschluessel(d), v.Id);
        }
        return this with
        {
            NachScName = sc.ToImmutable(),
            NachDiscordName = discord.ToImmutable(),
        };
    }
}

/// <summary>Eine Zeile aus "Liste einfügen", die nicht passt, mit Klartext.</summary>
public sealed record Zeilenfehler(int Zeile, string Grund);

/// <summary>Ergebnis einer Aktion des <see cref="Mitgliederbuch"/>.</summary>
public sealed record Mitgliederergebnis(Fehlercode Fehler) : Ergebnis(Fehler, [])
{
    /// <summary>Neu angelegte oder geänderte Vormerkungen.</summary>
    public IReadOnlyList<uint> Ids { get; init; } = [];

    /// <summary>Bei NAME_VERGEBEN der Name.</summary>
    public string? Vergeben { get; init; }

    /// <summary>Bei NAME_VERGEBEN: Es war der Discord-Name (A53), nicht der SC-Name.</summary>
    public bool DiscordVergeben { get; init; }

    /// <summary>Bei "Liste einfügen": jede Zeile, die nicht passt (dann wurde nichts angelegt).</summary>
    public IReadOnlyList<Zeilenfehler> Zeilen { get; init; } = [];

    /// <summary>Bei "Liste einfügen" ein Hinweis, der nichts verhindert hat (seit 0.11.8: Spalte 3 übergangen), sonst <c>null</c>.</summary>
    public string? Hinweis { get; init; }

    /// <summary>Konten, deren eigene Angaben (Rollen, Abzeichen) sich geändert haben: Mitgliedschaften-Push.</summary>
    public IReadOnlyList<uint> BetroffeneKonten { get; init; } = [];

    /// <summary>Bei UNGUELTIG ein Klartext für <c>Fehler.text</c> (bisher nur bei den Abzeichen), sonst <c>null</c>.</summary>
    public string? Grund { get; init; }
}

/// <summary>
/// Mitglieder als reine Logik (6.13, A46-A48): Rollenkatalog, Vormerkungen mit Mitgliederrollen, Rollen und Lieblingsrollen
/// der Konten, Abzeichen. Bearbeiten ist Admin-Sache (<c>KONTEN_VERWALTEN</c> in einer Admin-Sitzung, A43); Lieblingsrollen
/// setzt jeder selbst. SC-Namen der Vormerkungen teilen den Namensraum mit den Konten. Nichts hier verleiht Rechte. Nicht
/// threadsicher: genau ein Schreiber (die Zentrale).
/// </summary>
public sealed class Mitgliederbuch
{
    public const int MaxKatalog = 64;
    public const int MaxVormerkungen = 500;
    public const int MaxMitgliederrollen = 16;
    public const int MaxLieblingsrollen = 8;
    public const int MaxEinfuegenZeilen = 200;

    /// <summary>Hinweis bei "Liste einfügen", wenn in Spalte 3 etwas steht (seit 0.11.8, einmal je Liste).</summary>
    public const string Spalte3Hinweis = "Spalte 3 (Untergilde) wird seit 0.11.8 übergangen";
    public const int KatalogNameMax = 40;
    public const int GruppeMax = 20;
    public const int NotizMax = 80;
    public const int MaxRaenge = Funk.Protokoll.Steuerung.Grenzen.RaengeMax;
    public const int MaxTaetigkeiten = Funk.Protokoll.Steuerung.Grenzen.TaetigkeitenMax;

    private readonly TimeProvider _zeit;
    private Mitgliederstand _stand;

    public Mitgliederbuch(TimeProvider zeit, Mitgliederstand? stand = null)
    {
        ArgumentNullException.ThrowIfNull(zeit);
        _zeit = zeit;
        _stand = (stand ?? Mitgliederstand.Leer).MitIndex();
    }

    public Mitgliederstand Stand => Volatile.Read(ref _stand);

    /// <summary>Ob ein SC-Name (als Namensschlüssel) einer Vormerkung gehört.</summary>
    public bool NameBelegt(string schluessel) => Stand.NachScName.ContainsKey(schluessel);

    /// <summary>Welche Vormerkung einen Discord-Namen (als Namensschlüssel) trägt, oder <c>null</c> (A53).</summary>
    public uint? DiscordNameVon(string schluessel) => Stand.NachDiscordName.TryGetValue(schluessel, out var id) ? id : null;

    // ---------------------------------------------------------------- Rollenkatalog (N6)

    /// <summary>
    /// <c>Rollenkatalog SETZEN</c>: ersetzt den ganzen Katalog (höchstens 64, IDs und Namen eindeutig). Entfernte Einträge
    /// verschwinden auch aus Vormerkungen, Mitgliederrollen und Lieblingsrollen. Mit <c>KONTEN_VERWALTEN</c> in einer
    /// Admin-Sitzung ohne Einschränkung. Ohne sie nur, wenn <paramref name="fuehrung"/> gilt (Führung oder Einsatzleitung, Sarevoc
    /// am 30.09.2026, Antwort 4; das prüft die Zentrale am Schnappschuss), und dann nur als Ergänzung: Jeder vorhandene Eintrag
    /// bleibt genau so stehen. Mit einem Eintrag verschwänden sonst die Mitgliederrollen, die nach A43 nur der Admin vergibt.
    /// </summary>
    public Mitgliederergebnis KatalogSetzen(Kontenstand konten, Akteur akteur, IReadOnlyList<Katalogrolle> eintraege, long? version,
        bool fuehrung = false)
    {
        ArgumentNullException.ThrowIfNull(eintraege);
        var recht = Recht(konten, akteur);
        if (recht is not null && (!fuehrung || akteur.IstAdminWerkzeug)) return recht;
        var nurErgaenzen = recht is not null;
        var s = _stand;
        if (version is { } v && v != s.KatalogVersion) return new(Fehlercode.Veraltet);
        if (eintraege.Count > MaxKatalog) return new(Fehlercode.Ungueltig);
        var ids = new HashSet<ushort>();
        var namen = new HashSet<string>(StringComparer.Ordinal);
        var katalog = ImmutableSortedDictionary.CreateBuilder<ushort, Katalogrolle>();
        foreach (var e in eintraege)
        {
            var name = e.Name?.Trim(' ');
            if (e.Id == 0 || !ids.Add(e.Id) || !Zeichenregeln.IstText(name, 1, KatalogNameMax)
                || !namen.Add(Zeichenregeln.Namensschluessel(name!))
                || (e.Gruppe is not null && !Zeichenregeln.IstText(e.Gruppe, 1, GruppeMax))
                || e.Rollen.Length > MaxMitgliederrollen || e.RaidHelper.Length > 8
                || e.RaidHelper.Any(k => !Zeichenregeln.IstText(k.ClassName, 1, 40) || !Zeichenregeln.IstText(k.SpecName, 1, 40)))
                return new(Fehlercode.Ungueltig);
            katalog[e.Id] = e with { Name = name!, Rollen = [.. e.Rollen.Distinct()] };
        }
        var neu = katalog.ToImmutable();
        // Führung ohne Admin-Sitzung: nur dazu, nichts weg und nichts geändert; sonst dieselbe Antwort wie ohne Recht.
        if (nurErgaenzen && s.Katalog.Values.Any(alt => !neu.TryGetValue(alt.Id, out var n) || !GleicherEintrag(alt, n))) return recht!;
        var betroffen = new List<uint>();
        var mitglieder = s.Mitglieder;
        foreach (var m in s.Mitglieder.Values)
        {
            var bereinigt = m with
            {
                Rollen = [.. m.Rollen.Where(neu.ContainsKey)],
                Lieblingsrollen = [.. m.Lieblingsrollen.Where(neu.ContainsKey)],
                ErsteWahl = m.ErsteWahl is { } w && neu.ContainsKey(w) ? w : null,
            };
            if (bereinigt.Rollen.SequenceEqual(m.Rollen) && bereinigt.Lieblingsrollen.SequenceEqual(m.Lieblingsrollen) && bereinigt.ErsteWahl == m.ErsteWahl) continue;
            mitglieder = mitglieder.SetItem(m.KontoId, bereinigt);
            betroffen.Add(m.KontoId);
        }
        var vormerkungen = s.Vormerkungen;
        foreach (var vm in s.Vormerkungen.Values)
            if (vm.Rollen.Any(r => !neu.ContainsKey(r))) vormerkungen = vormerkungen.SetItem(vm.Id, vm with { Rollen = [.. vm.Rollen.Where(neu.ContainsKey)] });
        Uebernehmen(s with { Katalog = neu, KatalogVersion = s.KatalogVersion + 1, Mitglieder = mitglieder, Vormerkungen = vormerkungen });
        return new(Fehlercode.Keiner) { BetroffeneKonten = betroffen };
    }

    // ---------------------------------------------------------------- Mitglieder und Vormerkungen (N7)

    /// <summary><c>Mitglied VORMERKEN</c>: neues Mitglied vor der Anmeldung; immer FUNKER, ohne Rechte.</summary>
    public Mitgliederergebnis Vormerken(Kontenstand konten, Akteur akteur, string scName, string? discordName, IReadOnlyList<ushort>? rollen,
        string? notiz)
    {
        if (Recht(konten, akteur) is { } f) return f;
        var s = _stand;
        var e = VormerkungPruefen(konten, s, 0, scName, discordName, rollen, notiz, out var vm);
        if (e is not null) return e;
        if (s.Vormerkungen.Count >= MaxVormerkungen) return new(Fehlercode.Grenze);
        vm = vm! with { Id = s.NaechsteVormerkung, Angelegt = _zeit.GetUtcNow(), Durch = akteur.IstAdminWerkzeug ? 0 : akteur.KontoId };
        s = MitVormerkung(s with { NaechsteVormerkung = s.NaechsteVormerkung + 1 }, vm);
        Uebernehmen(s);
        return new(Fehlercode.Keiner) { Ids = [vm.Id] };
    }

    /// <summary>
    /// <c>Mitglied AENDERN</c> einer Vormerkung: Namen, Rollen, Notiz. Nicht angegebene Felder bleiben.
    /// </summary>
    public Mitgliederergebnis VormerkungAendern(Kontenstand konten, Akteur akteur, uint vormerkungId, string? scName, string? discordName,
        IReadOnlyList<ushort>? rollen, string? notiz)
    {
        if (Recht(konten, akteur) is { } f) return f;
        var s = _stand;
        if (!s.Vormerkungen.TryGetValue(vormerkungId, out var alt)) return new(Fehlercode.Unbekannt);
        var e = VormerkungPruefen(konten, s, vormerkungId, scName ?? alt.ScName, discordName ?? alt.DiscordName, rollen ?? alt.Rollen,
            notiz ?? alt.Notiz, out var vm);
        if (e is not null) return e;
        vm = vm! with { Id = vormerkungId, Angelegt = alt.Angelegt, Durch = alt.Durch };
        s = MitVormerkung(OhneVormerkungImIndex(s, alt), vm);
        Uebernehmen(s);
        return new(Fehlercode.Keiner) { Ids = [vormerkungId] };
    }

    /// <summary>
    /// <c>Mitglied AENDERN</c> eines Kontos: Mitgliederrollen, Notiz, seit 0.12.0 der Haken "darf Meldungen senden"
    /// (<paramref name="meldungenSenden"/>, fehlt = bleibt). Push an das Konto.
    /// </summary>
    public Mitgliederergebnis KontoAendern(Kontenstand konten, Akteur akteur, uint kontoId, IReadOnlyList<ushort>? rollen, string? notiz,
        bool? meldungenSenden = null)
    {
        ArgumentNullException.ThrowIfNull(konten);
        if (Recht(konten, akteur) is { } f) return f;
        if (!konten.Konten.TryGetValue(kontoId, out var k) || k.IstAntrag) return new(Fehlercode.Unbekannt);
        var s = _stand;
        var m = s.DatenVon(kontoId);
        if (rollen is not null)
        {
            if (!RollenGueltig(s, rollen, MaxMitgliederrollen)) return new(Fehlercode.Ungueltig);
            m = m with { Rollen = [.. rollen.Distinct()] };
            // Eine vom Admin eingetragene Rolle steht nicht zugleich als Wunsch da.
            m = m with { Lieblingsrollen = [.. m.Lieblingsrollen.Where(r => !m.Rollen.Contains(r))] };
        }
        if (notiz is not null)
        {
            if (notiz.Length > 0 && !Zeichenregeln.IstText(notiz, 1, NotizMax)) return new(Fehlercode.Ungueltig);
            m = m with { Notiz = notiz.Length == 0 ? null : notiz };
        }
        if (meldungenSenden is { } haken) m = m with { MeldungenSenden = haken };
        s = s with { Mitglieder = s.Mitglieder.SetItem(kontoId, m) };
        Uebernehmen(s);
        return new(Fehlercode.Keiner) { BetroffeneKonten = [kontoId] };
    }

    /// <summary><c>Mitglied LOESCHEN</c>: eine Vormerkung fort (Konten löscht <c>KontoVerwaltung LOESCHEN</c>).</summary>
    public Mitgliederergebnis VormerkungLoeschen(Kontenstand konten, Akteur akteur, uint vormerkungId)
    {
        if (Recht(konten, akteur) is { } f) return f;
        var s = _stand;
        if (!s.Vormerkungen.TryGetValue(vormerkungId, out var vm)) return new(Fehlercode.Unbekannt);
        Uebernehmen(OhneVormerkung(s, vm));
        return new(Fehlercode.Keiner) { Ids = [vormerkungId] };
    }

    /// <summary>
    /// <c>Mitglied EINFUEGEN</c> ("Liste einfügen"): je Zeile <c>SC-Name; Discord-Name; Kürzel; Rolle, Rolle</c>, nur der
    /// SC-Name ist Pflicht. Passt eine Zeile nicht, wird nichts angelegt, und jede fehlerhafte Zeile steht mit Klartext da.
    /// Seit 0.11.8 gibt es keine Untergilden mehr: Die dritte Spalte bleibt im Format, damit alte Listen weiter passen, wird
    /// aber übergangen; steht dort etwas, kommt je Liste ein <see cref="Mitgliederergebnis.Hinweis"/>.
    /// </summary>
    public Mitgliederergebnis Einfuegen(Kontenstand konten, Akteur akteur, IReadOnlyList<string> zeilen)
    {
        ArgumentNullException.ThrowIfNull(zeilen);
        if (Recht(konten, akteur) is { } f) return f;
        if (zeilen.Count is 0 or > MaxEinfuegenZeilen) return new(Fehlercode.Ungueltig);
        var s = _stand;
        var fehler = new List<Zeilenfehler>();
        var neue = new List<Vormerkung>();
        var spalte3 = false;
        var geplant = new HashSet<string>(StringComparer.Ordinal);
        var geplantDiscord = new HashSet<string>(StringComparer.Ordinal);
        // Nicht ToDictionary: Zwei Katalogrollen aus einem älteren Stand können heute denselben Schlüssel haben (ẞ wie ß).
        var katalog = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var k in s.Katalog.Values.OrderBy(k => k.Id)) katalog.TryAdd(Zeichenregeln.Namensschluessel(k.Name), k.Id);
        for (var i = 0; i < zeilen.Count; i++)
        {
            var nr = i + 1;
            var teile = (zeilen[i] ?? "").Split(';');
            if (teile.Length > 4)
            {
                fehler.Add(new(nr, "zu viele Felder (SC-Name; Discord-Name; Kürzel; Rollen)"));
                continue;
            }
            if (Zeichenregeln.ScNameBereinigen(teile[0]) is not { } sc)
            {
                fehler.Add(new(nr, "SC-Name fehlt oder enthält unerlaubte Zeichen"));
                continue;
            }
            var schluessel = Zeichenregeln.Namensschluessel(sc);
            if (!geplant.Add(schluessel))
            {
                fehler.Add(new(nr, $"{sc} steht doppelt in der Liste"));
                continue;
            }
            if (konten.NachScName.ContainsKey(schluessel) || s.NachScName.ContainsKey(schluessel))
            {
                fehler.Add(new(nr, $"Den Namen {sc} hat schon jemand"));
                continue;
            }
            string? dn = null;
            if (teile.Length > 1 && teile[1].Trim(' ').Length > 0)
            {
                dn = Zeichenregeln.DiscordNameBereinigen(teile[1]);
                if (dn is null)
                {
                    fehler.Add(new(nr, "Discord-Name ungültig (2 bis 32 Zeichen)"));
                    continue;
                }
                // Discord-Namen eindeutig wie die SC-Namen (A53): nicht doppelt in der Liste, nicht schon an einem Konto oder
                // einer Vormerkung.
                var d = Zeichenregeln.Namensschluessel(dn);
                if (!geplantDiscord.Add(d))
                {
                    fehler.Add(new(nr, $"Den Discord-Namen {dn} gibt es in der Liste doppelt"));
                    continue;
                }
                if (konten.NachDiscordName.ContainsKey(d) || s.NachDiscordName.ContainsKey(d))
                {
                    fehler.Add(new(nr, $"Den Discord-Namen {dn} hat schon jemand"));
                    continue;
                }
            }
            // Spalte 3 (Untergilde) wird seit 0.11.8 übergangen.
            if (teile.Length > 2 && teile[2].Trim(' ').Length > 0) spalte3 = true;
            var rollen = new List<ushort>();
            var rolleFehlt = (string?)null;
            if (teile.Length > 3)
            {
                foreach (var roh in teile[3].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!katalog.TryGetValue(Zeichenregeln.Namensschluessel(roh), out var id))
                    {
                        rolleFehlt = roh;
                        break;
                    }
                    if (!rollen.Contains(id)) rollen.Add(id);
                }
            }
            if (rolleFehlt is not null)
            {
                fehler.Add(new(nr, $"Rolle {rolleFehlt} steht nicht im Rollenkatalog"));
                continue;
            }
            if (rollen.Count > MaxMitgliederrollen)
            {
                fehler.Add(new(nr, "mehr als 16 Rollen"));
                continue;
            }
            neue.Add(new Vormerkung(0, sc) { DiscordName = dn, Rollen = [.. rollen] });
        }
        if (fehler.Count > 0) return new(Fehlercode.Ungueltig) { Zeilen = fehler };
        if (s.Vormerkungen.Count + neue.Count > MaxVormerkungen) return new(Fehlercode.Grenze);
        var jetzt = _zeit.GetUtcNow();
        var durch = akteur.IstAdminWerkzeug ? 0 : akteur.KontoId;
        var ids = new List<uint>();
        foreach (var roh in neue)
        {
            var vm = roh with { Id = s.NaechsteVormerkung, Angelegt = jetzt, Durch = durch };
            s = MitVormerkung(s with { NaechsteVormerkung = s.NaechsteVormerkung + 1 }, vm);
            ids.Add(vm.Id);
        }
        Uebernehmen(s);
        return new(Fehlercode.Keiner) { Ids = ids, Hinweis = spalte3 ? Spalte3Hinweis : null };
    }

    // ---------------------------------------------------------------- Lieblingsrollen (N8) und Zeichen (F11)

    /// <summary><c>Lieblingsrollen</c>: das eigene Konto hakt bis zu 8 Rollen aus dem Katalog ab, eine davon als erste Wahl.</summary>
    public Mitgliederergebnis LieblingsrollenSetzen(Kontenstand konten, Akteur akteur, IReadOnlyList<ushort> rollen, ushort? ersteWahl)
    {
        ArgumentNullException.ThrowIfNull(konten);
        ArgumentNullException.ThrowIfNull(rollen);
        if (akteur.IstAdminWerkzeug || Kontenbuch.RechtPruefen(konten, akteur, Rechte.Keine) != Fehlercode.Keiner) return new(Fehlercode.KeinRecht);
        var s = _stand;
        if (!RollenGueltig(s, rollen, MaxLieblingsrollen)) return new(Fehlercode.Ungueltig);
        var m = s.DatenVon(akteur.KontoId);
        var liste = rollen.Distinct().Where(r => !m.Rollen.Contains(r)).ToImmutableArray();
        if (ersteWahl is { } w && !liste.Contains(w) && !m.Rollen.Contains(w)) return new(Fehlercode.Ungueltig);
        Uebernehmen(s with { Mitglieder = s.Mitglieder.SetItem(akteur.KontoId, m with { Lieblingsrollen = liste, ErsteWahl = ersteWahl }) });
        return new(Fehlercode.Keiner) { BetroffeneKonten = [akteur.KontoId] };
    }

    /// <summary>"Mein Zeichen zeigen" (A49) für das eigene Konto.</summary>
    public Mitgliederergebnis ZeichenZeigenSetzen(uint kontoId, bool zeigen)
    {
        var s = _stand;
        var m = s.DatenVon(kontoId);
        if (m.ZeichenZeigen == zeigen) return new(Fehlercode.Keiner);
        Uebernehmen(s with { Mitglieder = s.Mitglieder.SetItem(kontoId, m with { ZeichenZeigen = zeigen }) });
        return new(Fehlercode.Keiner) { BetroffeneKonten = [kontoId] };
    }

    /// <summary>"Meine Angaben in Wer ist da zeigen" (Antwort 3) für das eigene Konto.</summary>
    public Mitgliederergebnis KarteFreigebenSetzen(uint kontoId, bool freigeben)
    {
        var s = _stand;
        var m = s.DatenVon(kontoId);
        if (m.KarteFreigeben == freigeben) return new(Fehlercode.Keiner);
        Uebernehmen(s with { Mitglieder = s.Mitglieder.SetItem(kontoId, m with { KarteFreigeben = freigeben }) });
        return new(Fehlercode.Keiner) { BetroffeneKonten = [kontoId] };
    }

    // ---------------------------------------------------------------- Abzeichen (0.11.7)

    /// <summary>
    /// <c>Abzeichen</c> (0x0025): Ränge und Tätigkeiten eines Kontos, beide Listen ersetzen die gespeicherten. Nur mit
    /// <c>KONTEN_VERWALTEN</c> in einer Admin-Sitzung (wie <see cref="KontoAendern"/>), nie durch die Führung. Jede Kennung muss im
    /// Abzeichenkatalog stehen und von der richtigen Art sein, höchstens ein Rang je Laufbahn, höchstens 6 Tätigkeiten, keine
    /// Doppelten. Gespeichert wird in Katalogfolge.
    /// </summary>
    public Mitgliederergebnis AbzeichenSetzen(Kontenstand konten, Akteur akteur, uint kontoId, IReadOnlyList<string> raenge, IReadOnlyList<string> taetigkeiten)
    {
        ArgumentNullException.ThrowIfNull(konten);
        ArgumentNullException.ThrowIfNull(raenge);
        ArgumentNullException.ThrowIfNull(taetigkeiten);
        if (Recht(konten, akteur) is { } f) return f;
        if (!konten.Konten.TryGetValue(kontoId, out var k) || k.IstAntrag) return new(Fehlercode.Unbekannt);
        if (AbzeichenPruefen(raenge, taetigkeiten) is { } grund) return new(Fehlercode.Ungueltig) { Grund = grund };
        var s = _stand;
        var m = s.DatenVon(kontoId);
        var neueRaenge = Abzeichenkatalog.Ordnen(raenge);
        var neueTaetigkeiten = Abzeichenkatalog.Ordnen(taetigkeiten);
        if (neueRaenge.SequenceEqual(m.Raenge) && neueTaetigkeiten.SequenceEqual(m.Taetigkeiten)) return new(Fehlercode.Keiner) { BetroffeneKonten = [kontoId] };
        Uebernehmen(s with { Mitglieder = s.Mitglieder.SetItem(kontoId, m with { Raenge = neueRaenge, Taetigkeiten = neueTaetigkeiten }) });
        return new(Fehlercode.Keiner) { BetroffeneKonten = [kontoId] };
    }

    /// <summary>Prüfregeln für <see cref="AbzeichenSetzen"/>: <c>null</c>, wenn alles passt, sonst der Grund als Klartext.</summary>
    public static string? AbzeichenPruefen(IReadOnlyList<string> raenge, IReadOnlyList<string> taetigkeiten)
    {
        ArgumentNullException.ThrowIfNull(raenge);
        ArgumentNullException.ThrowIfNull(taetigkeiten);
        if (raenge.Count > MaxRaenge) return "höchstens ein Rang je Laufbahn";
        if (taetigkeiten.Count > MaxTaetigkeiten) return "höchstens 6 Tätigkeiten";
        var laufbahnen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in raenge)
        {
            if (Abzeichenkatalog.Rang(r) is not { } rang) return Abzeichenkatalog.IstTaetigkeit(r) ? r + " ist kein Rang" : "unbekanntes Abzeichen " + Kurz(r);
            if (!laufbahnen.Add(rang.Laufbahn)) return "zwei Ränge in " + (Abzeichenkatalog.Laufbahn(rang.Laufbahn)?.Name ?? rang.Laufbahn);
        }
        var gesehen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in taetigkeiten)
        {
            if (!Abzeichenkatalog.IstTaetigkeit(t)) return Abzeichenkatalog.IstRang(t) ? t + " ist keine Tätigkeit" : "unbekanntes Abzeichen " + Kurz(t);
            if (!gesehen.Add(t)) return t + " doppelt";
        }
        return null;
    }

    /// <summary>
    /// Die eigene Ansicht der Abzeichen (<c>ProfilSetzen</c>, 0.11.7): <paramref name="vorn"/> fehlt = bleibt, "" = keine Wahl;
    /// <paramref name="aus"/> fehlt = bleibt, sonst ersetzt es die Liste. Erlaubt sind Katalogkennungen und ADMIN/EINSATZLEITUNG,
    /// besitzen muss man sie nicht (ein Stern kommt und geht mit dem Auftrag); gezeigt wird ohnehin nur, was man besitzt.
    /// </summary>
    public Mitgliederergebnis AbzeichenAnsichtSetzen(uint kontoId, string? vorn, IReadOnlyList<string>? aus)
    {
        if (AnsichtPruefen(vorn, aus) is { } grund) return new(Fehlercode.Ungueltig) { Grund = grund };
        if (vorn is null && aus is null) return new(Fehlercode.Keiner);
        var s = _stand;
        var m = s.DatenVon(kontoId);
        var neu = m with
        {
            AbzeichenVorn = vorn is null ? m.AbzeichenVorn : vorn.Length == 0 ? null : vorn,
            AbzeichenAus = aus is null ? m.AbzeichenAus : Abzeichenkatalog.Ordnen(aus),
        };
        if (neu.AbzeichenVorn == m.AbzeichenVorn && neu.AbzeichenAus.SequenceEqual(m.AbzeichenAus)) return new(Fehlercode.Keiner);
        Uebernehmen(s with { Mitglieder = s.Mitglieder.SetItem(kontoId, neu) });
        return new(Fehlercode.Keiner) { BetroffeneKonten = [kontoId] };
    }

    /// <summary>Prüfregeln für <see cref="AbzeichenAnsichtSetzen"/>, ohne etwas zu ändern: <c>null</c> oder der Grund.</summary>
    public static string? AnsichtPruefen(string? vorn, IReadOnlyList<string>? aus)
    {
        if (vorn is { Length: > 0 } && !Abzeichenkatalog.IstWaehlbar(vorn)) return "unbekanntes Abzeichen " + Kurz(vorn);
        if (aus is null) return null;
        if (aus.Count > Funk.Protokoll.Steuerung.Grenzen.AbzeichenAusMax) return "zu viele ausgeblendete Abzeichen";
        foreach (var a in aus)
            if (!Abzeichenkatalog.IstWaehlbar(a)) return "unbekanntes Abzeichen " + Kurz(a);
        return null;
    }

    private static string Kurz(string? kennung) => kennung is null ? "" : kennung.Length <= 16 ? kennung : kennung[..16];

    /// <summary>
    /// Was andere von den Abzeichen eines Kontos sehen (0.11.7): die nicht ausgeblendeten, Ränge in Laufbahnfolge, dann
    /// Tätigkeiten; nur Kennungen, die der Katalog kennt. Über "Mein Zeichen anderen zeigen" und die Freigabe entscheidet der Aufrufer.
    /// </summary>
    public static ImmutableArray<string> SichtbareAbzeichen(Mitgliedsdaten d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return Abzeichenkatalog.Ordnen(d.AlleAbzeichen().Where(a => Abzeichenkatalog.Kennt(a) && !d.AbzeichenAus.Contains(a)));
    }

    /// <summary>Text für das Journal: "Marine: Sergeant, Gunner M" oder "keine".</summary>
    public static string AbzeichenText(Mitgliedsdaten d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var titel = d.AlleAbzeichen().Select(a => Abzeichenkatalog.Titel(a) ?? a).ToList();
        return titel.Count == 0 ? "keine" : string.Join(", ", titel);
    }

    // ---------------------------------------------------------------- Freigabe, Löschung, Vorschlag

    /// <summary>
    /// Vormerkungen, die zu einem Antrag passen (gleicher SC-Name oder gleicher Discord-Name nach NFKC, ohne Groß und klein).
    /// Nur ein Vorschlag für die Freigabe; übernommen wird nie von selbst.
    /// </summary>
    public IReadOnlyList<(Vormerkung Vormerkung, bool UeberScName)> Vorschlaege(string? scName, string? discordName)
    {
        var s = Stand;
        var sc = scName is null ? null : Zeichenregeln.Namensschluessel(scName);
        var dn = discordName is null ? null : Zeichenregeln.Namensschluessel(discordName);
        var liste = new List<(Vormerkung, bool)>();
        foreach (var v in s.Vormerkungen.Values)
        {
            var ueberSc = sc is not null && Zeichenregeln.Namensschluessel(v.ScName) == sc;
            var ueberDn = dn is not null && v.DiscordName is { } d && Zeichenregeln.Namensschluessel(d) == dn;
            if (ueberSc || ueberDn) liste.Add((v, ueberSc));
        }
        return liste;
    }

    /// <summary>
    /// Nach einer Freigabe mit <c>vormerkungId</c>: Rollen und Notiz gehen an das Konto, die Vormerkung verschwindet. Rollen
    /// gelten damit sofort, auch im wartenden Client. Eine Untergilde wird seit 0.11.8 nicht mehr übernommen; aus den gespeicherten
    /// eines älteren Stands fällt nur die Nummer der Vormerkung heraus (siehe <see cref="Untergilde"/>).
    /// </summary>
    public Mitgliederergebnis VormerkungUebernehmen(uint vormerkungId, uint kontoId)
    {
        var s = _stand;
        if (!s.Vormerkungen.TryGetValue(vormerkungId, out var vm)) return new(Fehlercode.Unbekannt);
        var m = s.DatenVon(kontoId);
        m = m with { Rollen = [.. m.Rollen.Union(vm.Rollen).Take(MaxMitgliederrollen)], Notiz = m.Notiz ?? vm.Notiz };
        s = s with { Mitglieder = s.Mitglieder.SetItem(kontoId, m) };
        Uebernehmen(OhneVormerkung(s, vm));
        return new(Fehlercode.Keiner) { BetroffeneKonten = [kontoId] };
    }

    /// <summary>
    /// Konto gelöscht: Die Mitgliedsangaben fallen weg (Datenschutz). Aus den gespeicherten Untergilden eines Stands vor 0.11.8
    /// (siehe <see cref="Untergilde"/>) fällt die Kontonummer heraus, als Mitglied und als Ansprechpartner; sonst bleiben sie
    /// unverändert.
    /// </summary>
    public void KontoEntfernt(uint kontoId)
    {
        var s = _stand;
        var untergilden = s.Untergilden;
        foreach (var u in s.Untergilden.Values)
        {
            if (!u.Konten.Contains(kontoId) && u.Ansprechpartner != kontoId) continue;
            untergilden = untergilden.SetItem(u.Id, u with
            {
                Konten = u.Konten.Remove(kontoId),
                Ansprechpartner = u.Ansprechpartner == kontoId ? null : u.Ansprechpartner,
            });
        }
        if (!s.Mitglieder.ContainsKey(kontoId) && ReferenceEquals(untergilden, s.Untergilden)) return;
        Uebernehmen(s with { Mitglieder = s.Mitglieder.Remove(kontoId), Untergilden = untergilden });
    }

    // ---------------------------------------------------------------- intern

    private static Mitgliederergebnis? Recht(Kontenstand konten, Akteur akteur)
    {
        ArgumentNullException.ThrowIfNull(konten);
        var f = Kontenbuch.RechtPruefen(konten, akteur, Rechte.KontenVerwalten);
        return f == Fehlercode.Keiner ? null : new Mitgliederergebnis(f);
    }

    private static bool RollenGueltig(Mitgliederstand s, IReadOnlyList<ushort> rollen, int max) =>
        rollen.Count <= max && rollen.All(s.Katalog.ContainsKey);

    /// <summary>Ein Katalogeintrag unverändert (Name, Gruppe, Funkrollen, Raid-Helper-Klassen), so wie der Server ihn hält.</summary>
    private static bool GleicherEintrag(Katalogrolle a, Katalogrolle b) =>
        a.Id == b.Id && string.Equals(a.Name, b.Name, StringComparison.Ordinal) && string.Equals(a.Gruppe, b.Gruppe, StringComparison.Ordinal)
        && a.Rollen.SequenceEqual(b.Rollen) && a.RaidHelper.SequenceEqual(b.RaidHelper);

    private static Mitgliederergebnis? VormerkungPruefen(Kontenstand konten, Mitgliederstand s, uint eigeneId, string? scName, string? discordName,
        IReadOnlyList<ushort>? rollen, string? notiz, out Vormerkung? vm)
    {
        vm = null;
        if (Zeichenregeln.ScNameBereinigen(scName) is not { } sc) return new(Fehlercode.Ungueltig);
        string? dn = null;
        if (discordName is { Length: > 0 } && (dn = Zeichenregeln.DiscordNameBereinigen(discordName)) is null) return new(Fehlercode.Ungueltig);
        if (rollen is not null && !RollenGueltig(s, rollen, MaxMitgliederrollen)) return new(Fehlercode.Ungueltig);
        if (notiz is { Length: > 0 } && !Zeichenregeln.IstText(notiz, 1, NotizMax)) return new(Fehlercode.Ungueltig);
        var schluessel = Zeichenregeln.Namensschluessel(sc);
        if (konten.NachScName.ContainsKey(schluessel) || (s.NachScName.TryGetValue(schluessel, out var besitzer) && besitzer != eigeneId))
            return new(Fehlercode.NameVergeben) { Vergeben = sc };
        // Discord-Namen sind eindeutig wie die SC-Namen (A53), über Konten und Vormerkungen.
        if (dn is not null)
        {
            var d = Zeichenregeln.Namensschluessel(dn);
            if (konten.NachDiscordName.ContainsKey(d) || (s.NachDiscordName.TryGetValue(d, out var andere) && andere != eigeneId))
                return new(Fehlercode.NameVergeben) { Vergeben = dn, DiscordVergeben = true };
        }
        vm = new Vormerkung(eigeneId, sc)
        {
            DiscordName = dn,
            Rollen = rollen is null ? [] : [.. rollen.Distinct()],
            Notiz = notiz is { Length: > 0 } ? notiz : null,
        };
        return null;
    }

    private static Mitgliederstand MitVormerkung(Mitgliederstand s, Vormerkung vm) => s with
    {
        Vormerkungen = s.Vormerkungen.SetItem(vm.Id, vm),
        NachScName = s.NachScName.SetItem(Zeichenregeln.Namensschluessel(vm.ScName), vm.Id),
        NachDiscordName = vm.DiscordName is { } d ? s.NachDiscordName.SetItem(Zeichenregeln.Namensschluessel(d), vm.Id) : s.NachDiscordName,
    };

    private static Mitgliederstand OhneVormerkungImIndex(Mitgliederstand s, Vormerkung vm)
    {
        var k = Zeichenregeln.Namensschluessel(vm.ScName);
        if (s.NachScName.TryGetValue(k, out var id) && id == vm.Id) s = s with { NachScName = s.NachScName.Remove(k) };
        if (vm.DiscordName is { } d && s.NachDiscordName.TryGetValue(Zeichenregeln.Namensschluessel(d), out var dId) && dId == vm.Id)
            s = s with { NachDiscordName = s.NachDiscordName.Remove(Zeichenregeln.Namensschluessel(d)) };
        return s;
    }

    private static Mitgliederstand OhneVormerkung(Mitgliederstand s, Vormerkung vm)
    {
        // Aus den gespeicherten Untergilden eines Stands vor 0.11.8 fällt nur die Nummer der Vormerkung heraus (siehe Untergilde).
        s = OhneVormerkungImIndex(s, vm);
        var untergilden = s.Untergilden;
        foreach (var u in s.Untergilden.Values)
            if (u.Vormerkungen.Contains(vm.Id)) untergilden = untergilden.SetItem(u.Id, u with { Vormerkungen = u.Vormerkungen.Remove(vm.Id) });
        return s with { Vormerkungen = s.Vormerkungen.Remove(vm.Id), Untergilden = untergilden };
    }

    private void Uebernehmen(Mitgliederstand neu) => Volatile.Write(ref _stand, neu with { Version = _stand.Version + 1 });

    /// <summary>Text für das Journal: "Rollen: A, B".</summary>
    public static string RollenText(Mitgliederstand s, IEnumerable<ushort> rollen)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(rollen);
        return string.Join(", ", rollen.Select(r => s.Katalog.TryGetValue(r, out var k) ? k.Name : r.ToString(CultureInfo.InvariantCulture)));
    }
}
