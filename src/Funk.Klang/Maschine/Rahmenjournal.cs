// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Maschine;

/// <summary>Merkmale eines Aufnahmerahmens im <see cref="Rahmenjournal"/>.</summary>
[Flags]
internal enum RahmenMerkmal : byte
{
    Keine = 0,

    /// <summary>Das Gate war nach diesem Rahmen offen (Senden oder Nachlauf) oder die Sprechtaste gedrückt.</summary>
    Sendet = 0x01,

    /// <summary>Die offene Phase kommt aus der Sprachaktivierung.</summary>
    Sprachaktivierung = 0x02,

    /// <summary>Die Sprechtaste war gedrückt.</summary>
    Sprechtaste = 0x04,

    /// <summary>Mikro stumm (Klangstufe C2a): der Rahmen wurde verworfen.</summary>
    Stumm = 0x08,

    /// <summary>
    /// Das Gate hält eine bestätigte Sprechphase unter der Mindest-Funklautstärke zurück (<see cref="Verarbeitung.GateZustand.Zurueckgehalten"/>,
    /// seit 0.10.0 für die Statuszeile); nie zusammen mit <see cref="Sendet"/> oder <see cref="Stumm"/>.
    /// </summary>
    Zurueckgehalten = 0x10,
}

/// <summary>Ein Aufnahmerahmen (10 ms) aus Sicht des Gates.</summary>
internal readonly record struct RahmenEintrag(
    long ZeitMs, float PegelDbfs, float RauschbodenDb, float LinieDb, float Sprachwahrscheinlichkeit, RahmenMerkmal Merkmale)
{
    public bool Sendet => (Merkmale & RahmenMerkmal.Sendet) != 0;

    public bool Stumm => (Merkmale & RahmenMerkmal.Stumm) != 0;

    public bool Zurueckgehalten => (Merkmale & RahmenMerkmal.Zurueckgehalten) != 0;
}

/// <summary>
/// Die letzten 1024 Aufnahmerahmen (gut 10 s) für Pegelmeldungen und die Lernfunktion. Genau ein Schreiber (der
/// Aufnahme-Thread), beliebig viele Leser, ohne Sperre und ohne Allokation: Der Schreiber veröffentlicht einen Eintrag
/// erst nach dem Kopieren (Volatile.Write auf den Zähler). Ein Leser kopiert und prüft danach am Zähler, welche der
/// kopierten Einträge der Schreiber inzwischen überschrieben haben kann; die verwirft er.
/// </summary>
internal sealed class Rahmenjournal
{
    public const int Kapazitaet = 1024;
    private const int Maske = Kapazitaet - 1;

    private readonly RahmenEintrag[] _ring = new RahmenEintrag[Kapazitaet];
    private long _anzahl;

    /// <summary>Zahl der je geschriebenen Einträge; der nächste bekommt diese Nummer.</summary>
    public long Anzahl => Volatile.Read(ref _anzahl);

    /// <summary>Nur der Schreiber.</summary>
    public void Schreiben(in RahmenEintrag eintrag)
    {
        var n = _anzahl;
        _ring[(int)(n & Maske)] = eintrag;
        Volatile.Write(ref _anzahl, n + 1);
    }

    /// <summary>
    /// Kopiert die Einträge ab Nummer <paramref name="von"/> (höchstens <paramref name="ziel"/>.Length) und liefert,
    /// ab welcher Nummer sie gültig sind (<paramref name="ersteGueltige"/>) und wie viele es sind. Einträge vor
    /// <paramref name="ersteGueltige"/> waren schon überschrieben; die Rückgabe zählt nur die gültigen, die ab
    /// <paramref name="ziel"/>[0] liegen.
    /// </summary>
    public int Lesen(long von, Span<RahmenEintrag> ziel, out long ersteGueltige)
    {
        var bis = Anzahl;
        var start = Math.Max(von, bis - Kapazitaet + 1);
        var anzahl = (int)Math.Clamp(bis - start, 0, ziel.Length);
        for (var i = 0; i < anzahl; i++) ziel[i] = _ring[(int)((start + i) & Maske)];
        // Was der Schreiber seit dem ersten Lesen des Zählers geschrieben hat, kann kopierte Plätze überschrieben
        // haben: gültig bleibt nur, was hinter dem ältesten noch nicht überschriebenen Eintrag liegt. Die Barriere
        // hält die Kopie vor dem zweiten Lesen des Zählers (Volatile.Read allein ordnet nur spätere Zugriffe).
        Interlocked.MemoryBarrier();
        var danach = Anzahl;
        var aeltesteSichere = danach - Kapazitaet + 1;
        var verworfen = (int)Math.Clamp(aeltesteSichere - start, 0, anzahl);
        if (verworfen > 0) ziel[verworfen..anzahl].CopyTo(ziel);
        ersteGueltige = start + verworfen;
        return anzahl - verworfen;
    }
}
