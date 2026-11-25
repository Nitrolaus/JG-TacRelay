// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Text.Json;
using Funk.Server.Betrieb;
using I = Funk.Protokoll.Identitaet;
using L = Funk.Server.Lage;

namespace Funk.Server.Kern;

/// <summary>
/// Dauerhafte Ablage im Zustandsverzeichnis als eigene JSON-Dateien mit atomarem Ersetzen (Offener Punkt 7, 7.2):
/// <c>lage.json</c> (Schnappschuss ohne Sitzungen, Nebenstand), <c>konten.json</c> (Kontenstand). Flüchtig bleiben
/// Sitzungen, Schlüssel, Zähler, Replay-Fenster, Phasenbuch und Einmalcodes. Der Zustandsschreiber ruft
/// <see cref="Speichern"/> vor dem Austausch des Schnappschusses (6.2); wirft es, bleibt der alte Stand in Kraft.
/// </summary>
public sealed class Zustandsablage : L.IZustandsSpeicher
{
    public const string LageDatei = "lage.json";
    public const string KontenDatei = "konten.json";
    public const string AdminsDatei = "admins.json";
    public const string MitgliederDatei = "mitglieder.json";

    private readonly string _lagePfad;
    private readonly string _kontenPfad;
    private readonly string _adminsPfad;
    private readonly string _mitgliederPfad;

    public Zustandsablage(string verzeichnis)
    {
        ArgumentNullException.ThrowIfNull(verzeichnis);
        Dateiablage.VerzeichnisAnlegen(verzeichnis);
        Verzeichnis = verzeichnis;
        _lagePfad = Path.Combine(verzeichnis, LageDatei);
        _kontenPfad = Path.Combine(verzeichnis, KontenDatei);
        _adminsPfad = Path.Combine(verzeichnis, AdminsDatei);
        _mitgliederPfad = Path.Combine(verzeichnis, MitgliederDatei);
    }

    public string Verzeichnis { get; }

    /// <summary>
    /// Wie viele Gerätesperren und abgelehnte Geräte das letzte <see cref="KontenLaden"/> aus einem Stand bis 0.6.x verworfen hat (seit
    /// 0.7.0 sperrt nur noch ein Admin bewusst; Sperren entzogener Geräte bleiben); 0 bei einem neueren Stand. Die Zentrale meldet es
    /// und speichert dann gleich neu.
    /// </summary>
    public int AltsperrenVerworfen { get; private set; }

    /// <summary>
    /// Die Fingerabdrücke, deren Sperre das letzte <see cref="KontenLaden"/> verworfen hat. Die Zentrale schreibt ihre Kennungen ins
    /// Journal: Eine bewusste Sperre, deren Konto schon gelöscht war, lässt sich von denen aus Ablehnen und Löschen nicht
    /// unterscheiden, und so sieht ein Admin, welche PCs wieder hereindürfen.
    /// </summary>
    public IReadOnlyList<I.Fingerabdruck> VerworfeneSperren { get; private set; } = [];

    /// <summary>
    /// <c>admins.json</c> (3.9): Admin-Schlüssel, Zustände, TOTP-Geheimnisse nur als Chiffrat, Notfallcodes nur als Prüfwert,
    /// Fehlversuche. Unter Linux 0600.
    /// </summary>
    public void AdminsSpeichern(I.Adminstand stand)
    {
        ArgumentNullException.ThrowIfNull(stand);
        var datei = new AdminsInhalt([.. stand.Eintraege.Values.OrderBy(e => e.KontoId)], stand.Version);
        Dateiablage.AtomarSchreiben(_adminsPfad, JsonSerializer.SerializeToUtf8Bytes(datei, Ablageformat.Optionen), nurBesitzer: true);
    }

    public I.Adminstand AdminsLaden()
    {
        var bytes = Dateiablage.LesenFallsVorhanden(_adminsPfad);
        if (bytes is null) return I.Adminstand.Leer;
        var d = JsonSerializer.Deserialize<AdminsInhalt>(bytes, Ablageformat.Optionen) ?? throw new InvalidDataException("admins.json ist leer.");
        return new I.Adminstand { Eintraege = d.Eintraege.ToImmutableDictionary(e => e.KontoId), Version = d.Version };
    }

    /// <summary>
    /// <c>mitglieder.json</c> (6.13): Rollenkatalog, Vormerkungen, Mitgliedsangaben. <c>untergilden</c> wird seit 0.11.8 nur
    /// unverändert durchgereicht: Eine ältere Serverfassung wirft beim Laden, wenn das Feld fehlt (Rückweg bleibt offen).
    /// </summary>
    public void MitgliederSpeichern(I.Mitgliederstand stand)
    {
        ArgumentNullException.ThrowIfNull(stand);
        var datei = new MitgliederInhalt(
            [.. stand.Katalog.Values],
            stand.KatalogVersion,
            [.. stand.Vormerkungen.Values],
            stand.NaechsteVormerkung,
            [.. stand.Mitglieder.Values.OrderBy(m => m.KontoId)],
            [.. stand.Untergilden.Values],
            stand.Version);
        Dateiablage.AtomarSchreiben(_mitgliederPfad, JsonSerializer.SerializeToUtf8Bytes(datei, Ablageformat.Optionen), nurBesitzer: true);
    }

    public I.Mitgliederstand MitgliederLaden()
    {
        var bytes = Dateiablage.LesenFallsVorhanden(_mitgliederPfad);
        if (bytes is null) return I.Mitgliederstand.Leer;
        var d = JsonSerializer.Deserialize<MitgliederInhalt>(bytes, Ablageformat.Optionen) ?? throw new InvalidDataException("mitglieder.json ist leer.");
        return new I.Mitgliederstand
        {
            Katalog = d.Katalog.ToImmutableSortedDictionary(k => k.Id, k => k),
            KatalogVersion = d.KatalogVersion,
            Vormerkungen = d.Vormerkungen.ToImmutableSortedDictionary(v => v.Id, v => v),
            NaechsteVormerkung = d.NaechsteVormerkung,
            Mitglieder = d.Mitglieder.ToImmutableDictionary(m => m.KontoId),
            Untergilden = d.Untergilden.ToImmutableSortedDictionary(u => u.Id, u => u),
            Version = d.Version,
        }.MitIndex();
    }

    /// <summary>Zahl der Speichervorgänge (Messwert).</summary>
    public long Speicherungen { get; private set; }

    public void Speichern(L.Schnappschuss schnappschuss, L.Nebenstand nebenstand)
    {
        ArgumentNullException.ThrowIfNull(schnappschuss);
        ArgumentNullException.ThrowIfNull(nebenstand);
        var datei = new LageInhalt(
            schnappschuss.ZustandsVersion,
            schnappschuss.NetzListeVersion,
            schnappschuss.Einstellungen,
            [.. schnappschuss.Netze.Values],
            [.. schnappschuss.Schiffe.Values],
            [.. schnappschuss.Rollen.Values],
            [.. schnappschuss.Konten.Values.Where(k => k.Id != L.InterneAuftraege.AdminWerkzeug)],
            nebenstand,
            [.. schnappschuss.Phasen.Values],
            [.. schnappschuss.SperrenAlle]);
        Dateiablage.AtomarSchreiben(_lagePfad, JsonSerializer.SerializeToUtf8Bytes(datei, Ablageformat.Optionen));
        Speicherungen++;
    }

    /// <summary>
    /// <c>lage.json</c> ohne Aufträge, ohne Funker-Modus und ohne Kanalsperren, für die Sicherung (6.12, 6.15, 6.17, RAIDLEITUNG-ENTWURF
    /// "Wo der Auftrag liegt"): Beim Zurückspielen einer Sicherung verfallen diese Abendzustände, damit ein entzogener Auftrag oder eine
    /// aufgehobene Sperre nicht aus einem alten Stand zurückkommt. Phasen und die laufende Phase bleiben, denn die Felder der Konten passen zu ihr. Alles andere bleibt Byte
    /// für Byte, wie es gespeichert war, bis auf die weggelassenen Felder.
    /// </summary>
    public static byte[] OhneAuftraege(byte[] lageJson)
    {
        ArgumentNullException.ThrowIfNull(lageJson);
        var knoten = System.Text.Json.Nodes.JsonNode.Parse(lageJson) as System.Text.Json.Nodes.JsonObject
            ?? throw new InvalidDataException("lage.json ist kein Objekt.");
        var geaendert = knoten.Remove("sperrenAlle");
        if (knoten["konten"] is System.Text.Json.Nodes.JsonArray konten)
        {
            foreach (var k in konten)
            {
                if (k is not System.Text.Json.Nodes.JsonObject o) continue;
                // Nicht kurzschließen: Alle Felder müssen fort.
                geaendert |= o.Remove("beauftragung") | o.Remove("alsFunkerBis") | o.Remove("sperren");
            }
        }
        return geaendert ? System.Text.Encoding.UTF8.GetBytes(knoten.ToJsonString()) : lageJson;
    }

    /// <summary>Lädt die Lage oder liefert den Anfangsstand (nur Global). Sitzungen gibt es nach einem Neustart keine (7.2).</summary>
    public (L.Schnappschuss Schnappschuss, L.Nebenstand Nebenstand) LageLaden()
    {
        var bytes = Dateiablage.LesenFallsVorhanden(_lagePfad);
        if (bytes is null) return (new L.SchnappschussBauer().Bauen(), L.Nebenstand.Leer);
        var d = JsonSerializer.Deserialize<LageInhalt>(bytes, Ablageformat.Optionen) ?? throw new InvalidDataException("lage.json ist leer.");
        var bauer = new L.SchnappschussBauer()
            .MitEinstellungen(d.Einstellungen)
            .MitVersion(d.ZustandsVersion, d.NetzListeVersion);
        foreach (var n in d.Netze) bauer.MitNetz(n);
        // Wörterbücher mit Zeichenkettenschlüsseln ordnen ordinal, wie im Modul angelegt.
        foreach (var s in d.Schiffe) bauer.MitSchiff(s with { Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, s.Netze) });
        foreach (var r in d.Rollen) bauer.MitRolle(r);
        // Stände von vor 0.5.0 kennen keine Phasen (6.14); dann gibt es keine, und die Grundstellung läuft. Eine Variante ohne Feld
        // (geschrieben vor der Gegenprüfung S2, nach NETZ_LOESCHEN) fällt weg, statt den Start zu verhindern: Die Rolle steht in
        // der Phase dann wie in der Grundstellung, so wie die Clients sie ohnehin sahen.
        foreach (var p in d.Phasen ?? []) bauer.MitPhase(p with { Varianten = [.. p.Varianten.Where(v => !v.Felder.IsEmpty)] });
        foreach (var k in d.Konten) bauer.MitKonto(k);
        // Stände von vor 0.6.0 kennen keine Kanalsperren (6.17).
        foreach (var s in d.SperrenAlle ?? []) bauer.MitSperreFuerAlle(s);
        var neben = d.Nebenstand;
        if (neben.Letzte is { } letzte)
            neben = neben with { Letzte = letzte with { OffeneDiscordIds = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, letzte.OffeneDiscordIds) } };
        // Stände von vor 0.3.0 kennen den Ersteller einer Aufstellung nicht (F10); dann gilt, wer sie zuletzt gespeichert hat.
        if (neben.Aufstellungen.Values.Any(a => a.Ersteller == 0))
            neben = neben with { Aufstellungen = neben.Aufstellungen.SetItems(neben.Aufstellungen.Values.Where(a => a.Ersteller == 0)
                .Select(a => new KeyValuePair<uint, L.Aufstellung>(a.Id, a with { Ersteller = a.Durch }))) };
        return (bauer.Bauen(), neben);
    }

    public void KontenSpeichern(I.Kontenstand stand)
    {
        ArgumentNullException.ThrowIfNull(stand);
        var datei = new KontenInhalt(
            [.. stand.Konten.Values.OrderBy(k => k.KontoId)],
            [.. stand.Geraete.Values.OrderBy(g => g.GeraetId)],
            new SortedDictionary<string, uint>(stand.NachNamensschluessel, StringComparer.Ordinal),
            [.. stand.GesperrteFingerabdruecke.Select(f => f.AlsBase64()).Order(StringComparer.Ordinal)],
            stand.NaechsteKontoId,
            stand.NaechsteGeraetId,
            stand.Version,
            NurBewussteSperren: true);
        Dateiablage.AtomarSchreiben(_kontenPfad, JsonSerializer.SerializeToUtf8Bytes(datei, Ablageformat.Optionen));
    }

    public I.Kontenstand KontenLaden()
    {
        var bytes = Dateiablage.LesenFallsVorhanden(_kontenPfad);
        if (bytes is null) return I.Kontenstand.Leer;
        var d = JsonSerializer.Deserialize<KontenInhalt>(bytes, Ablageformat.Optionen) ?? throw new InvalidDataException("konten.json ist leer.");
        var gesperrt = ImmutableHashSet.CreateBuilder<I.Fingerabdruck>();
        var geraete = d.Geraete;
        AltsperrenVerworfen = 0;
        VerworfeneSperren = [];
        if (d.NurBewussteSperren == true)
        {
            foreach (var f in d.Gesperrt)
                gesperrt.Add(I.Fingerabdruck.TryAusBase64(f, out var fp) ? fp : throw new InvalidDataException("Gesperrter Fingerabdruck ungültig."));
        }
        else
        {
            // Stand bis 0.6.x: Dort sperrten auch Ablehnen und das Löschen eines abgelehnten Geräts den Fingerabdruck auf Dauer. Seit
            // 0.7.0 sperrt nur ein Admin bewusst (Sarevoc, 04.10.2026: Löschen heißt vollständig entfernen, nichts sperrt von selbst). Eine
            // Sperre bleibt deshalb nur, wo noch ein entzogenes Gerät dazu steht (bewusst gesperrt, Konsole "sperren"), sonst würde
            // das Update etwa einen gestohlenen PC wieder freilassen. Die übrigen Sperren und die abgelehnten Geräte fallen einmal weg;
            // eine bewusste Sperre, deren Konto schon gelöscht war, lässt sich nicht mehr erkennen und fällt mit. Die Antragskonten
            // ohne Gerät räumt die nächste Frist ab (Kontenbuch.FristenAnwenden).
            var entzogen = geraete.Where(g => g.Status == I.GeraetStatus.Entzogen).Select(g => g.Fingerabdruck).ToHashSet();
            var verworfen = new List<I.Fingerabdruck>();
            foreach (var f in d.Gesperrt)
            {
                var fp = I.Fingerabdruck.TryAusBase64(f, out var x) ? x : throw new InvalidDataException("Gesperrter Fingerabdruck ungültig.");
                if (entzogen.Contains(fp)) gesperrt.Add(fp);
                else verworfen.Add(fp);
            }
            verworfen.AddRange(geraete.Where(g => g.Status == I.GeraetStatus.Abgelehnt).Select(g => g.Fingerabdruck));
            geraete = [.. geraete.Where(g => g.Status != I.GeraetStatus.Abgelehnt)];
            VerworfeneSperren = [.. verworfen.Distinct()];
            AltsperrenVerworfen = VerworfeneSperren.Count;
        }
        return new I.Kontenstand
        {
            Konten = d.Konten.ToImmutableDictionary(k => k.KontoId),
            Geraete = geraete.ToImmutableDictionary(g => g.GeraetId),
            NachDiscordId = d.Konten.Where(k => k.DiscordId is not null).ToImmutableDictionary(k => k.DiscordId!, k => k.KontoId, StringComparer.Ordinal),
            NachNamensschluessel = d.Namen.ToImmutableDictionary(StringComparer.Ordinal),
            NachFingerabdruck = geraete.ToImmutableDictionary(g => g.Fingerabdruck, g => g.GeraetId),
            GesperrteFingerabdruecke = gesperrt.ToImmutable(),
            NaechsteKontoId = d.NaechsteKontoId,
            NaechsteGeraetId = d.NaechsteGeraetId,
            Version = d.Version,
        }.MitScNamenIndex();
    }

    private sealed record LageInhalt(
        long ZustandsVersion,
        long NetzListeVersion,
        L.Einstellungen Einstellungen,
        List<L.Netz> Netze,
        List<L.Schiff> Schiffe,
        List<L.Rolle> Rollen,
        List<L.Konto> Konten,
        L.Nebenstand Nebenstand,
        List<L.Einsatzphase>? Phasen = null,
        List<L.Kanalsperre>? SperrenAlle = null);

    /// <summary>
    /// <c>konten.json</c>. <see cref="NurBewussteSperren"/> steht seit 0.7.0 auf true; fehlt es, stammt der Stand von 0.6.x oder
    /// älter, und <see cref="KontenLaden"/> verwirft einmal die Sperren, zu denen kein entzogenes Gerät mehr steht. Fassungen bis
    /// 0.6.x lesen die Datei weiter (sie übergehen das unbekannte Feld), ein Rückweg bleibt also möglich.
    /// </summary>
    private sealed record KontenInhalt(
        List<I.Konto> Konten,
        List<I.Geraet> Geraete,
        SortedDictionary<string, uint> Namen,
        List<string> Gesperrt,
        uint NaechsteKontoId,
        uint NaechsteGeraetId,
        long Version,
        bool? NurBewussteSperren = null);

    private sealed record AdminsInhalt(List<I.AdminEintrag> Eintraege, long Version);

    private sealed record MitgliederInhalt(
        List<I.Katalogrolle> Katalog,
        long KatalogVersion,
        List<I.Vormerkung> Vormerkungen,
        uint NaechsteVormerkung,
        List<I.Mitgliedsdaten> Mitglieder,
        List<I.Untergilde> Untergilden,
        long Version);
}
