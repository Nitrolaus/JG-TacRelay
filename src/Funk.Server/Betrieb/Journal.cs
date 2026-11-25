// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;

namespace Funk.Server.Betrieb;

/// <summary>Ein Eintrag im Journal: Zeit (ms seit 1970), Art, wer (Konto, 0 = Konsole), welches Konto, Klartext.</summary>
public sealed record Journalvermerk(long Zeit, string Art, uint Durch, uint? KontoId, string Text);

/// <summary>
/// Journal der Rechte- und Sicherheitsänderungen (RAIDLEITUNG-ENTWURF M1/N3, ANMELDUNG-SICHERHEIT *Journal*): Admin-Sitzungen,
/// Fehlversuche, Sperren, Ernennungen, Freigaben, Kontoverwaltung, Mitglieder, Netze. Anders als das Logbuch mit Klarnamen,
/// deshalb nur für Admins lesbar (Admin-Sitzung oder Konsole) und nach 90 Tagen gelöscht. Codes, Geheimnisse und
/// Notfallcodes stehen nie darin. Eigene Datei <c>journal.json</c> im Zustandsverzeichnis, atomar ersetzt.
/// <para>
/// Einträge ändert nur die Arbeitsschleife der Zentrale (nicht threadsicher). Geschrieben wird im eigenen Faden
/// (<see cref="SpeichernImHintergrund"/>): Die Schleife kopiert nur die Liste der Einträge (unveränderliche Records), das
/// Umwandeln in JSON (bis 50.000 Einträge, einige MB) und das Schreiben auf die Platte laufen daneben (Gegenprüfung Stufe 1:
/// bei großem Journal verzögerte das Steuernachrichten um einige 10 ms). Alle Schreibvorgänge laufen nacheinander, und ein
/// älterer Stand überschreibt nie einen neueren.
/// </para>
/// </summary>
public sealed class Journal
{
    public const string Datei = "journal.json";

    /// <summary>So lange bleiben Einträge.</summary>
    public static readonly TimeSpan Aufbewahrung = TimeSpan.FromDays(90);

    /// <summary>Härtung gegen eine Flut: höchstens so viele Einträge, die ältesten gehen zuerst.</summary>
    public const int MaxEintraege = 50_000;

    /// <summary>Längster Text eines Eintrags (Drahtform <c>str(200)</c>).</summary>
    public const int TextMax = 200;

    private readonly List<Journalvermerk> _eintraege = [];
    private readonly string _pfad;
    private readonly Action<string, byte[]> _schreiber;
    private readonly Lock _schreibSperre = new();
    private Task _laufend = Task.CompletedTask;
    private long _staende;
    private long _geschriebenBis;
    private volatile bool _geaendert;
    private Exception? _fehler;

    /// <param name="schreiber">Schreibt die Datei; ohne Angabe atomar (0600). Tests geben einen eigenen hinein.</param>
    public Journal(string pfad, Action<string, byte[]>? schreiber = null)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        _pfad = pfad;
        _schreiber = schreiber ?? ((p, b) => Dateiablage.AtomarSchreiben(p, b, nurBesitzer: true));
        if (Dateiablage.LesenFallsVorhanden(pfad) is { } bytes)
            _eintraege.AddRange(JsonSerializer.Deserialize<List<Journalvermerk>>(bytes, Ablageformat.Optionen) ?? []);
    }

    /// <summary>Seit dem letzten Speichern (oder seiner Übergabe an den Hintergrund) geändert.</summary>
    public bool Geaendert => _geaendert;

    public int Anzahl => _eintraege.Count;

    /// <summary>Zeit des jüngsten Eintrags (für die Uhrprüfung beim Start), oder <c>null</c> ohne Einträge.</summary>
    public DateTimeOffset? Juengster => _eintraege.Count == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(_eintraege.Max(e => e.Zeit));

    /// <summary>Neuer Eintrag; der Text wird auf 200 Zeichen gekürzt.</summary>
    public void Eintragen(long zeit, string art, uint durch, uint? kontoId, string text)
    {
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(text);
        _eintraege.Add(new Journalvermerk(zeit, art, durch, kontoId, Kuerzen(text)));
        if (_eintraege.Count > MaxEintraege) _eintraege.RemoveRange(0, _eintraege.Count - MaxEintraege);
        _geaendert = true;
    }

    /// <summary>Neueste zuerst, nur vor <paramref name="vor"/> (ms) und optional nur zu einem Konto (als Ziel oder Urheber).</summary>
    public IReadOnlyList<Journalvermerk> Lesen(long? vor, uint? kontoId, int anzahl)
    {
        var liste = new List<Journalvermerk>(Math.Min(anzahl, _eintraege.Count));
        for (var i = _eintraege.Count - 1; i >= 0 && liste.Count < anzahl; i--)
        {
            var e = _eintraege[i];
            if (vor is { } v && e.Zeit >= v) continue;
            if (kontoId is { } k && e.KontoId != k && e.Durch != k) continue;
            liste.Add(e);
        }
        return liste;
    }

    /// <summary>Löscht Einträge, die älter als 90 Tage sind.</summary>
    public void Aufraeumen(long jetztMs)
    {
        var grenze = jetztMs - (long)Aufbewahrung.TotalMilliseconds;
        var n = _eintraege.FindIndex(e => e.Zeit >= grenze);
        var weg = n < 0 ? _eintraege.Count : n;
        if (weg == 0) return;
        _eintraege.RemoveRange(0, weg);
        _geaendert = true;
    }

    /// <summary>
    /// Nur aus der Arbeitsschleife: den Stand übergeben und im eigenen Faden schreiben; kehrt sofort zurück. Ein Fehler beim
    /// Schreiben setzt <see cref="Geaendert"/> wieder (nächster Versuch) und steht in <see cref="FehlerAbholen"/>.
    /// </summary>
    public Task SpeichernImHintergrund()
    {
        var (stand, nr) = Uebergeben();
        var vorher = _laufend;
        _laufend = Task.Run(async () =>
        {
            try
            {
                await vorher.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Der vorige Schreibvorgang hat seinen Fehler schon gemeldet.
            }
            try
            {
                Schreiben(stand, nr);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Interlocked.Exchange(ref _fehler, ex);
                _geaendert = true;
            }
        });
        return _laufend;
    }

    /// <summary>Wartet auf den laufenden Schreibvorgang im Hintergrund (Beenden, Tests).</summary>
    public Task WartenAsync() => _laufend;

    /// <summary>Ein Fehler des letzten Schreibens im Hintergrund, einmal; sonst <c>null</c>.</summary>
    public Exception? FehlerAbholen() => Interlocked.Exchange(ref _fehler, null);

    /// <summary>Sofort und in diesem Faden schreiben (Sicherung, Beenden); wartet, bis ein Schreiben im Hintergrund fertig ist.</summary>
    public void Speichern()
    {
        var (stand, nr) = Uebergeben();
        Schreiben(stand, nr);
    }

    /// <summary>Nur aus der Arbeitsschleife: Kopie der Einträge mit laufender Nummer.</summary>
    private (Journalvermerk[] Stand, long Nr) Uebergeben()
    {
        var stand = _eintraege.ToArray();
        _geaendert = false;
        return (stand, Interlocked.Increment(ref _staende));
    }

    /// <summary>Schreibt einen Stand, außer ein neuerer liegt schon auf der Platte.</summary>
    private void Schreiben(Journalvermerk[] stand, long nr)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(stand, Ablageformat.Optionen);
        lock (_schreibSperre)
        {
            if (nr <= _geschriebenBis) return;
            _schreiber(_pfad, bytes);
            _geschriebenBis = nr;
        }
    }

    private static string Kuerzen(string text)
    {
        var n = 0;
        var laenge = 0;
        foreach (var r in text.EnumerateRunes())
        {
            if (++n > TextMax) return text[..laenge];
            laenge += r.Utf16SequenceLength;
        }
        return text;
    }
}
