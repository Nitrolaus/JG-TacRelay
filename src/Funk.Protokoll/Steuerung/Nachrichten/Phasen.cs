// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

// Bodies der Ausbaustufe S2 (Fassung 0.5.0, RAID-S2-ENTWURF): 0x003C Funkermodus ("Heute als Funker"), 0x003D PhaseSchalten,
// 0x4004 PhaseGeschaltet und die Phasen in NetzListe. Neue Felder in bestehenden Nachrichten sind optional (4.7), der neue Push
// liegt im optionalen Bereich, damit Clients 0.4.x ihn übergehen; die neuen Anfragen schickt ein Client nur an Server ab 0.5.0
// (Versionen.ServerFassungFuer).

/// <summary>
/// 0x003C Funkermodus (C->S, 6.15): Ein ADMIN schaltet für das eigene Konto "Heute als Funker" ein oder aus, ohne
/// Admin-Sitzung. Solange der Modus läuft, ruhen die Rechte der Einsatzleitung, und die Einsatzleitung teilt das Konto ein.
/// </summary>
public sealed record FunkermodusAnfrage : ISteuerBody
{
    public required bool An { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>0x003C Funkermodus, Antwort: der Stand danach; <c>bis</c> (Unix-ms) nur, solange der Modus läuft.</summary>
public sealed record FunkermodusAntwort : ISteuerBody
{
    public required bool An { get; init; }
    public long? Bis { get; init; }

    public void Pruefen(ref Pruefung p) => p.Bereich(Bis, 0, JsonVorpruefung.MaxGanzzahl, "bis");
}

/// <summary>
/// 0x003D PhaseSchalten (C->S, 6.14, <c>ZUWEISEN</c>): schaltet die Flotte auf Phase <c>nr</c>. <c>erwartet</c> ist die Phase,
/// die der Client für die laufende hält; weicht sie ab, kommt <c>VERALTET</c> mit <c>details {nr, durch}</c>, und nichts ändert sich.
/// </summary>
public sealed record PhaseSchaltenAnfrage : ISteuerBody
{
    public required byte Nr { get; init; }
    public required byte Erwartet { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Nr, 1, Grenzen.PhaseNrMax, "nr");
        p.Bereich(Erwartet, 1, Grenzen.PhaseNrMax, "erwartet");
    }
}

/// <summary>0x003D PhaseSchalten, Antwort: wie viele Konten umgestellt sind und wen die Rangregel ausgelassen hat.</summary>
public sealed record PhaseSchaltenAntwort : ISteuerBody
{
    public required byte Nr { get; init; }
    public required ushort Umgestellt { get; init; }
    public required IReadOnlyList<uint> Ausgelassen { get; init; }
    public required long ZustandsVersion { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Nr, 1, Grenzen.PhaseNrMax, "nr");
        p.Liste(Ausgelassen, Grenzen.KontenMax, "ausgelassen");
        p.Bereich(ZustandsVersion, 0, JsonVorpruefung.MaxGanzzahl, "zustandsVersion");
    }
}

/// <summary>
/// 0x4004 PhaseGeschaltet (S->C, optional): die laufende Phase, an alle AKTIV-Sitzungen bei jedem Wechsel und nach Welcome,
/// sobald die Flotte Phasen hat. <c>durch</c> 0 = Konsole oder der Rückfall nach 12 h; <c>seit</c> in Unix-ms.
/// </summary>
public sealed record PhaseGeschaltet : ISteuerBody
{
    public required byte Nr { get; init; }
    public required string Name { get; init; }
    public required string Kurz { get; init; }
    public required uint Durch { get; init; }
    public required long Seit { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Nr, 1, Grenzen.PhaseNrMax, "nr");
        p.PflichtText(Name, Grenzen.PhaseNameMax, "name");
        p.PflichtText(Kurz, Grenzen.PhaseKurzMax, "kurz");
        p.Bereich(Seit, 0, JsonVorpruefung.MaxGanzzahl, "seit");
    }
}

/// <summary>
/// Eine Phase in <c>NetzListe.phasen</c> und das Objekt von <c>Verwaltung PHASE_SETZEN</c> (6.14): Nummer 1 ist die
/// Grundstellung und trägt nur Name und Kurzname; 2 bis 5 tragen je Rolle, die sich ändert, eine vollständige Feldliste. Die
/// Felder aller Varianten stehen flach in <c>felder</c>, jedes mit seiner <c>rolleId</c>; die Variante einer Rolle sind ihre
/// Einträge. Grund ist die Tiefe 8 aus 4.3: Als <c>varianten[{rolleId, felder[]}]</c> läge eine Phase in <c>Verwaltung PAKET</c>
/// auf Tiefe 9, und der Server trennte das Paket mit PROTOKOLL.
/// </summary>
public sealed record NetzPhase : ISteuerBody
{
    public required byte Nr { get; init; }
    public required string Name { get; init; }
    public required string Kurz { get; init; }
    public IReadOnlyList<PhasenFeld>? Felder { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Nr, 1, Grenzen.PhaseNrMax, "phasen.nr");
        p.PflichtText(Name, Grenzen.PhaseNameMax, "phasen.name");
        p.PflichtText(Kurz, Grenzen.PhaseKurzMax, "phasen.kurz");
        p.Objekte(Felder, Grenzen.PhasenFelderMax, "phasen.felder");
        if (!p.Ok || Felder is null) return;
        if (Nr == 1) p.Regel(Felder.Count == 0, "phasen.felder");
        // Je Rolle höchstens 16 Felder, wie eine Rolle (6.9); doppelte Feldnummern prüft der Server.
        foreach (var g in Felder.GroupBy(f => f.RolleId))
        {
            if (g.Count() > Grenzen.FelderMax)
            {
                p.Melden(Fehlercode.ZuGross, "phasen.felder");
                return;
            }
        }
    }

    /// <summary>
    /// Zahl der Variantenfelder dieser Phase (Grenze <see cref="Grenzen.PhasenFelderMax"/> über alle Phasen). Eine Methode, keine
    /// Eigenschaft: Der Source-Generator schriebe eine Eigenschaft mit auf den Draht.
    /// </summary>
    public int FelderAnzahl() => Felder?.Count ?? 0;
}

/// <summary>Ein Feld der Variante einer Rolle in einer Phase: <c>rolleId</c> und die Form von <c>rollen[].felder</c>.</summary>
public sealed record PhasenFeld : ISteuerBody
{
    public required ushort RolleId { get; init; }
    public required byte Feld { get; init; }
    public ushort? NetzId { get; init; }
    public string? Schiffsnetz { get; init; }
    public required bool Senden { get; init; }
    public required bool Hoeren { get; init; }
    public required bool SendenStandard { get; init; }
    public required bool Fest { get; init; }

    /// <summary>Fester Ch. wie in <c>rollen[].felder[].ch</c> (6.16, seit 0.6.0).</summary>
    public byte? Ch { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(RolleId, 1, Grenzen.NetzIdMax, "felder.rolleId");
        p.Bereich(Feld, 0, Grenzen.FeldMax, "felder.feld");
        p.Regel(NetzId is null != Schiffsnetz is null, "felder.netzId");
        p.Bereich(NetzId, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "felder.netzId");
        p.Kennung(Schiffsnetz, "felder.schiffsnetz");
        p.Bereich(Ch, 0, Kanalrechnung.ChMax, "felder.ch");
        p.Regel(Ch is null || NetzId is not null, "felder.ch");
    }
}
