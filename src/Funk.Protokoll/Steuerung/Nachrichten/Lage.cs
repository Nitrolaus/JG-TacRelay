// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

// Bodies 0x0021-0x0024 aus 4.5: NetzListe, Mitgliedschaften, Praesenz, SendeFehler.

/// <summary>0x0021 NetzListe (S->C, immer vollständig).</summary>
public sealed record NetzListe : ISteuerBody
{
    public required long Version { get; init; }
    public required NetzEinstellungen Einstellungen { get; init; }
    public required IReadOnlyList<Netz> Netze { get; init; }
    public required IReadOnlyList<Schiff> Schiffe { get; init; }
    public required IReadOnlyList<Rolle> Rollen { get; init; }

    /// <summary>
    /// Phasen der Flotte (6.14, seit 0.5.0): Grundstellung (Nummer 1, nur Name) und höchstens 4 weitere mit den Feldern ihrer
    /// Varianten je Rolle. Fehlt das Feld, kennt der Server keine Phasen oder hat keine angelegt.
    /// </summary>
    public IReadOnlyList<NetzPhase>? Phasen { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Version, 0, JsonVorpruefung.MaxGanzzahl, "version");
        p.Objekte(Netze, Grenzen.NetzeMax, "netze");
        p.Objekte(Schiffe, Grenzen.SchiffeMax, "schiffe");
        p.Objekte(Rollen, Grenzen.RollenMax, "rollen");
        p.Objekte(Phasen, Grenzen.PhasenMax + 1, "phasen");
        if (p.Ok && Phasen is not null && Phasen.Sum(x => x.FelderAnzahl()) > Grenzen.PhasenFelderMax) p.Melden(Fehlercode.ZuGross, "phasen.varianten");
    }
}

/// <summary>
/// <c>NetzListe.einstellungen</c>. Seit 0.3.0 (F9) trägt eine Sperre der Selbstwahl ihren Urheber (<c>selbstwahlDurch</c>, 0 =
/// Konsole), ihr Ende (<c>selbstwahlBis</c>, Unix-ms) und ob Nachzügler ohne Platz trotzdem einen freien nehmen dürfen.
/// </summary>
public sealed record NetzEinstellungen
{
    public required bool DurchsageNurTaste { get; init; }
    public required bool Selbstwahl { get; init; }
    public uint? SelbstwahlDurch { get; init; }
    public long? SelbstwahlBis { get; init; }

    /// <summary>Fehlt das Feld (älterer Server), gilt true.</summary>
    public bool? Nachzuegler { get; init; }
}

/// <summary>Ein Netz in <c>NetzListe.netze</c>.</summary>
public sealed record Netz : ISteuerBody
{
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    public required NetzTyp Typ { get; init; }
    public required ushort Reihenfolge { get; init; }
    public required bool NurTaste { get; init; }
    public required Ausloeser AusloeserStandard { get; init; }
    public required NetzOffen Offen { get; init; }
    public required bool Standard { get; init; }
    public ushort? SchiffId { get; init; }
    public string? Schiffsnetz { get; init; }

    /// <summary>
    /// Ch.-Bezeichnungen eines Kanalnetzes (6.16, seit 0.6.0): Index 0 ist die Ruhe, 2 bis 31 Einträge. Fehlt die Liste, ist es ein
    /// gewöhnliches Netz.
    /// </summary>
    public IReadOnlyList<string>? Kanaele { get; init; }

    /// <summary>Gruppe für die Kanalwahl ("Schiffe", "Boden"), seit 0.6.0.</summary>
    public string? Gruppe { get; init; }

    /// <summary>
    /// Woher das Netz kommt ("Star Citizen", "Baustein Trupp", "von Hand"), seit 0.8.0; nur für die Verwaltung, der Funk liest es nicht.
    /// Fehlt das Feld beim Setzen, behält der Server die bisherige Herkunft.
    /// </summary>
    public string? Herkunft { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Id, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "netze.id");
        p.PflichtText(Name, Grenzen.NameMax, "netze.name");
        p.Kennung(Schiffsnetz, "netze.schiffsnetz");
        p.Text(Gruppe, Kanalrechnung.GruppeMax, "netze.gruppe");
        p.Text(Herkunft, Grenzen.HerkunftMax, "netze.herkunft");
        if (Kanaele is null) return;
        p.Liste(Kanaele, Kanalrechnung.EintraegeMax, "netze.kanaele");
        p.Regel(Kanaele.Count >= Kanalrechnung.EintraegeMin, "netze.kanaele");
        p.Bereich(Id, Grenzen.NetzIdMin, Kanalrechnung.KanalnetzIdMax, "netze.id");
        if (!p.Ok) return;
        foreach (var name in Kanaele)
        {
            p.Text(name, Kanalrechnung.ChNameMax, "netze.kanaele");
            p.Regel(!string.IsNullOrWhiteSpace(name), "netze.kanaele");
        }
    }
}

/// <summary>Ein Schiff in <c>NetzListe.schiffe</c>.</summary>
public sealed record Schiff : ISteuerBody
{
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    public required ushort Reihenfolge { get; init; }
    public required IReadOnlyDictionary<string, ushort> Netze { get; init; }
    public required IReadOnlyList<SchiffsPlatz> Plaetze { get; init; }

    /// <summary>Schiff, Trupp oder Orga (F5, A39); fehlt das Feld, ist es ein Schiff.</summary>
    public Einheitsart? Art { get; init; }

    /// <summary>Freier Typ, etwa "Hammerhead".</summary>
    public string? Typ { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.NameMax, "schiffe.name");
        p.Text(Typ, Grenzen.EinheitTypMax, "schiffe.typ");
        if (Netze.Count > Grenzen.SchiffsnetzKennungenMax)
        {
            p.Melden(Fehlercode.ZuGross, "schiffe.netze");
            return;
        }
        foreach (var (kennung, netzId) in Netze)
        {
            p.Kennung(kennung, "schiffe.netze");
            p.Bereich(netzId, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "schiffe.netze");
        }
        p.Liste(Plaetze, Grenzen.PlaetzeJeSchiffMax, "schiffe.plaetze");
        if (!p.Ok) return;
        foreach (var pl in Plaetze) p.Regel(pl.Mindest is null || pl.Mindest <= pl.Kapazitaet, "schiffe.plaetze.mindest");
    }
}

/// <summary><c>schiffe[].plaetze[]</c>; <c>mindest</c> (F5) ist die Mindestbesetzung für die Ampel, der Server prüft sie beim Einteilen nicht.</summary>
public sealed record SchiffsPlatz
{
    public required ushort RolleId { get; init; }
    public required byte Kapazitaet { get; init; }
    public byte? Mindest { get; init; }
}

/// <summary>Eine Rolle in <c>NetzListe.rollen</c>.</summary>
public sealed record Rolle : ISteuerBody
{
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    public required ushort Reihenfolge { get; init; }
    public required bool SelbstWaehlbar { get; init; }
    public required bool BrauchtSchiff { get; init; }
    public byte? Kapazitaet { get; init; }
    public required IReadOnlyList<RollenFeld> Felder { get; init; }

    /// <summary>Woher die Rolle kommt, seit 0.8.0, wie <see cref="Netz.Herkunft"/>.</summary>
    public string? Herkunft { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.RollenNameMax, "rollen.name");
        p.Text(Herkunft, Grenzen.HerkunftMax, "rollen.herkunft");
        p.Objekte(Felder, Grenzen.FelderMax, "rollen.felder");
    }
}

/// <summary><c>rollen[].felder[]</c>: genau eines von <c>netzId</c> und <c>schiffsnetz</c>.</summary>
public sealed record RollenFeld : ISteuerBody
{
    public required byte Feld { get; init; }
    public ushort? NetzId { get; init; }
    public string? Schiffsnetz { get; init; }
    public required bool Senden { get; init; }
    public required bool Hoeren { get; init; }
    public required bool SendenStandard { get; init; }
    public required bool Fest { get; init; }

    /// <summary>Fester Ch. eines Rollenfelds mit einem Kanalnetz (6.16, seit 0.6.0); fehlt er, wird der Ch. aufgelöst.</summary>
    public byte? Ch { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Feld, 0, Grenzen.FeldMax, "felder.feld");
        p.Regel(NetzId is null != Schiffsnetz is null, "felder.netzId");
        p.Bereich(NetzId, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "felder.netzId");
        p.Kennung(Schiffsnetz, "felder.schiffsnetz");
        p.Bereich(Ch, 0, Kanalrechnung.ChMax, "felder.ch");
        p.Regel(Ch is null || NetzId is not null, "felder.ch");
    }
}

/// <summary>Eine Mitgliedschaft (Feld, Netz, hören, senden, fest).</summary>
public sealed record Mitgliedschaft : ISteuerBody
{
    public required byte Feld { get; init; }
    public required ushort NetzId { get; init; }
    public required bool Hoeren { get; init; }
    public required bool Senden { get; init; }
    public required bool Fest { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Feld, 0, Grenzen.FeldMax, "felder.feld");
        p.Bereich(NetzId, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "felder.netzId");
    }

    /// <summary>Felder einer Liste: höchstens 16, <c>feld</c> und <c>netzId</c> je eindeutig, Global nie darin.</summary>
    internal static void ListePruefen(IReadOnlyList<Mitgliedschaft>? felder, ref Pruefung p, string name)
    {
        p.Objekte(felder, Grenzen.FelderMax, name);
        if (felder is null || !p.Ok) return;
        Span<bool> feldBelegt = stackalloc bool[Grenzen.FeldMax + 1];
        for (var i = 0; i < felder.Count; i++)
        {
            var f = felder[i];
            p.Regel(!feldBelegt[f.Feld], name);
            feldBelegt[f.Feld] = true;
            p.Regel(f.NetzId != Grenzen.GlobalNetzId, name);
            for (var j = 0; j < i; j++) p.Regel(felder[j].NetzId != f.NetzId, name);
        }
    }
}

/// <summary><c>{senden}</c> für Global in Mitgliedschaften.</summary>
public sealed record GlobalAngabe
{
    public required bool Senden { get; init; }
}

/// <summary>Sendeauswahl <c>{felder: [u8], global}</c>.</summary>
public sealed record Sendeauswahl : ISteuerBody
{
    public required IReadOnlyList<byte> Felder { get; init; }
    public required bool Global { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Liste(Felder, Grenzen.FelderMax, "sendeauswahl.felder");
        p.Aufsteigend(Felder, "sendeauswahl.felder");
        if (Felder is not null) foreach (var f in Felder) p.Bereich(f, 0, Grenzen.FeldMax, "sendeauswahl.felder");
    }
}

/// <summary>Stummschaltung <c>{netzId (0 = alle, 1 = Global), bis, durch}</c>.</summary>
public sealed record Stummschaltung
{
    public required ushort NetzId { get; init; }
    public required long Bis { get; init; }
    public required uint Durch { get; init; }
}

/// <summary>Feld, das bei einer Umstellung nicht untergebracht werden konnte.</summary>
public sealed record EntfallenesFeld
{
    public required byte Feld { get; init; }
    public required ushort NetzId { get; init; }
    public required string Grund { get; init; }
}

/// <summary><c>Mitgliedschaften.letzterStand</c> (A21).</summary>
public sealed record LetzterStand : ISteuerBody
{
    public ushort? SchiffId { get; init; }
    public ushort? RolleId { get; init; }
    public required IReadOnlyList<Mitgliedschaft> Felder { get; init; }
    public required Sendeauswahl Sendeauswahl { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        Mitgliedschaft.ListePruefen(Felder, ref p, "letzterStand.felder");
        p.Objekt(Sendeauswahl);
    }
}

/// <summary>0x0022 Mitgliedschaften (S->C, eigene, immer vollständig).</summary>
public sealed record Mitgliedschaften : ISteuerBody
{
    public required long ZustandsVersion { get; init; }
    public required long KontoVersion { get; init; }
    public ushort? SchiffId { get; init; }
    public ushort? RolleId { get; init; }
    public PlatzStatus? Platz { get; init; }
    public required IReadOnlyList<Mitgliedschaft> Felder { get; init; }
    public required GlobalAngabe Global { get; init; }
    public required Sendeauswahl Sendeauswahl { get; init; }
    public required IReadOnlyList<Stummschaltung> Stumm { get; init; }
    public required MitgliedschaftsQuelle Quelle { get; init; }
    public uint? Durch { get; init; }
    public LetzterStand? LetzterStand { get; init; }
    public IReadOnlyList<EntfallenesFeld>? Entfallen { get; init; }

    /// <summary>Eigene Kontoangaben (F3, F13): Rechte, Namen, Admin-Zustand, Rollen, Zeichen.</summary>
    public KontoStand? Konto { get; init; }

    /// <summary>Nur wenn dieser Stand aus einem Phasenwechsel kommt (6.14, seit 0.5.0): die Nummer der neuen Phase.</summary>
    public byte? Phase { get; init; }

    /// <summary>Die gewählte Rolle (<c>RolleWaehlen</c>, 6.16, seit 0.6.0); fehlt, wenn die Felder aus einer anderen Vorgabe kommen.</summary>
    public ushort? Rolle { get; init; }

    /// <summary>
    /// Kanalsperren, die das Konto gerade treffen (6.17, seit 0.6.0): die eigenen und die für alle (<c>alle</c>); fehlt ohne Sperre. Ein
    /// Feld mit einem gesperrten Kanal bleibt stehen und zeigt rot.
    /// </summary>
    public IReadOnlyList<Kanalsperre>? Gesperrt { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(ZustandsVersion, 0, JsonVorpruefung.MaxGanzzahl, "zustandsVersion");
        p.Bereich(KontoVersion, 0, JsonVorpruefung.MaxGanzzahl, "kontoVersion");
        p.Bereich(Phase, 1, Grenzen.PhaseNrMax, "phase");
        p.Bereich(Rolle, 1, Grenzen.NetzIdMax, "rolle");
        Mitgliedschaft.ListePruefen(Felder, ref p, "felder");
        p.Objekt(Sendeauswahl);
        p.Liste(Stumm, int.MaxValue, "stumm");
        p.Liste(Gesperrt, Kanalrechnung.GesperrtMax, "gesperrt");
        p.Objekt(LetzterStand);
        p.Liste(Entfallen, int.MaxValue, "entfallen");
        p.Objekt(Konto);
    }
}

/// <summary>
/// <c>Mitgliedschaften.konto</c> (F3, F13): was das eigene Konto über sich wissen muss und bei jeder Änderung durch den
/// Admin neu bekommt, damit Menüs und "Meine Rollen" ohne Neustart stimmen. <c>rechte</c> sind die dauerhaften Rechte des
/// Kontos; Verwaltungsrechte wirken davon nur in einer Admin-Sitzung.
/// </summary>
public sealed record KontoStand : ISteuerBody
{
    public required IReadOnlyList<Recht> Rechte { get; init; }
    public required IReadOnlyList<string> ScNamen { get; init; }
    public string? DiscordName { get; init; }
    public AdminStatus? Admin { get; init; }
    public bool? AdminAntrag { get; init; }
    public bool? Eigentuemer { get; init; }
    public required IReadOnlyList<ushort> Rollen { get; init; }
    public required IReadOnlyList<ushort> Lieblingsrollen { get; init; }
    public ushort? ErsteWahl { get; init; }

    /// <summary>Untergilde. Seit 0.11.8 entfallen: Der Server füllt das Feld nicht mehr; es bleibt nur für ältere Gegenstellen.</summary>
    public ushort? Untergilde { get; init; }

    public required IReadOnlyList<string> Zeichen { get; init; }
    public required bool ZeichenZeigen { get; init; }

    /// <summary>
    /// "Meine Angaben in Wer ist da zeigen" (Sarevoc, 30.09.2026, Antwort 3), Standard an; fehlt das Feld (älterer Server), gilt
    /// es als an.
    /// </summary>
    public bool? KarteFreigeben { get; init; }

    /// <summary>Laufender oder geplanter Auftrag für die Einsatzleitung (F3, 6.12); <c>rechte</c> oben bleiben die dauerhaften.</summary>
    public BeauftragungAngabe? Beauftragung { get; init; }

    /// <summary>
    /// "Heute als Funker" (6.15, seit 0.5.0): bis wann (Unix-ms) der eigene Funker-Modus läuft; fehlt, wenn er aus ist. Solange er
    /// läuft, ruhen die Rechte der Einsatzleitung; <c>rechte</c> oben bleiben die dauerhaften.
    /// </summary>
    public long? AlsFunkerBis { get; init; }

    /// <summary>
    /// Seit 0.11.7: alle eigenen Abzeichen (Ränge in Laufbahnfolge, dann Tätigkeiten in Katalogfolge), auch ausgeblendete; fehlt
    /// bei einem älteren Server. Die automatischen Zeichen stehen weiter in <see cref="Zeichen"/>.
    /// </summary>
    public IReadOnlyList<string>? Abzeichen { get; init; }

    /// <summary>Seit 0.11.7: das gewählte Abzeichen "vorn" (ProfilSetzen), auch wenn es gerade nicht besessen oder ausgeblendet ist.</summary>
    public string? AbzeichenVorn { get; init; }

    /// <summary>Seit 0.11.7: die ausgeblendeten Abzeichen und Zeichen (ProfilSetzen); fehlt bei einem älteren Server.</summary>
    public IReadOnlyList<string>? AbzeichenAus { get; init; }

    /// <summary>
    /// Seit 0.12.0: der eigene Haken "darf Meldungen senden" (vom Admin gesetzt); fehlt ohne Haken oder bei einem älteren Server. Ob
    /// gerade gesendet werden darf, rechnet der Client daraus mit Stufe, Auftrag und Funker-Modus (<see cref="Melderecht"/>); jede
    /// Änderung davon kommt mit neuen Mitgliedschaften.
    /// </summary>
    public bool? MeldungenSenden { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Objekt(Beauftragung);
        p.Bereich(AlsFunkerBis, 0, JsonVorpruefung.MaxGanzzahl, "konto.alsFunkerBis");
        p.Liste(Rechte, Grenzen.RechteMax, "konto.rechte");
        p.Texte(ScNamen, Grenzen.ScNamenMax, Grenzen.NameMax, "konto.scNamen");
        p.Text(DiscordName, Grenzen.NameMax, "konto.discordName");
        p.Liste(Rollen, Grenzen.MitgliederrollenMax, "konto.rollen");
        p.Liste(Lieblingsrollen, Grenzen.LieblingsrollenMax, "konto.lieblingsrollen");
        p.Texte(Zeichen, Grenzen.ZeichenMax, 16, "konto.zeichen");
        p.Texte(Abzeichen, Grenzen.AbzeichenMax, Grenzen.AbzeichenKennungMax, "konto.abzeichen");
        p.Text(AbzeichenVorn, Grenzen.AbzeichenKennungMax, "konto.abzeichenVorn");
        p.Texte(AbzeichenAus, Grenzen.AbzeichenAusMax, Grenzen.AbzeichenKennungMax, "konto.abzeichenAus");
    }
}

/// <summary>0x0023 Praesenz (S->C).</summary>
public sealed record Praesenz : ISteuerBody
{
    public required bool Voll { get; init; }
    public required long ZustandsVersion { get; init; }
    public required IReadOnlyList<PraesenzEintrag> Eintraege { get; init; }
    public required IReadOnlyList<uint> Entfernt { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(ZustandsVersion, 0, JsonVorpruefung.MaxGanzzahl, "zustandsVersion");
        p.Objekte(Eintraege, Grenzen.KontenMax, "eintraege");
        p.Liste(Entfernt, Grenzen.KontenMax, "entfernt");
    }
}

/// <summary>Eintrag in <c>Praesenz.eintraege</c>; Details nur für Sitzungen mit <c>ZUWEISEN</c>.</summary>
public sealed record PraesenzEintrag : ISteuerBody
{
    public required uint KontoId { get; init; }
    public required string Name { get; init; }
    public required string Benutzername { get; init; }
    public required bool Online { get; init; }
    public ushort? SchiffId { get; init; }
    public ushort? RolleId { get; init; }
    public PlatzStatus? Platz { get; init; }
    public IReadOnlyList<Mitgliedschaft>? Felder { get; init; }
    public IReadOnlyList<ushort>? SendetIn { get; init; }
    public IReadOnlyList<Stummschaltung>? Stumm { get; init; }
    public Verbindungsweg? Verbindung { get; init; }
    public Guete? Guete { get; init; }

    /// <summary>Hauptname (erster SC-Name) für alle (F4).</summary>
    public string? ScName { get; init; }

    /// <summary>
    /// Discord-Name, wie beim Antrag eingetippt ("Mustermann/Max"), für alle: "Wer ist da" zeigt ihn als Namen (Sarevoc, 30.09.2026,
    /// Antwort 3). Nur eine Angabe, nicht eindeutig; eindeutig ist der SC-Name daneben.
    /// </summary>
    public string? DiscordName { get; init; }

    /// <summary>Untergilde (F12). Seit 0.11.8 entfallen: Der Server füllt das Feld nicht mehr; es bleibt nur für ältere Gegenstellen.</summary>
    public ushort? Untergilde { get; init; }

    /// <summary>
    /// Kennungen der Zeichen neben dem Namen (F12, A49), etwa ADMIN oder FUEHRUNG; der Client zeichnet selbst. Seit 0.11.7 ohne
    /// die automatischen Zeichen, die der Inhaber ausgeblendet hat.
    /// </summary>
    public IReadOnlyList<string>? Zeichen { get; init; }

    /// <summary>
    /// Seit 0.11.7: die sichtbaren Abzeichen (Ränge in Laufbahnfolge, dann Tätigkeiten in Katalogfolge), mit denselben Sperren wie
    /// <see cref="Zeichen"/> ("Mein Zeichen anderen zeigen" aus: nichts; ohne Freigabe nur für <c>ZUWEISEN</c>).
    /// </summary>
    public IReadOnlyList<string>? Abzeichen { get; init; }

    /// <summary>Seit 0.11.7: das Abzeichen "vorn", nur wenn der Inhaber es besitzt und es sichtbar ist (Abzeichen oder Zeichen).</summary>
    public string? AbzeichenVorn { get; init; }

    /// <summary>Details für <c>ZUWEISEN</c> (F4): wie der Platz zustande kam, durch wen, seit wann (Unix-ms).</summary>
    public MitgliedschaftsQuelle? Quelle { get; init; }
    public uint? Durch { get; init; }
    public long? Seit { get; init; }

    /// <summary>Details für <c>ZUWEISEN</c> (F4): der letzte Platz, für "wie zuletzt für alle ohne Platz".</summary>
    public LetzterPlatz? LetzterPlatz { get; init; }

    /// <summary>Details für <c>ZUWEISEN</c> (6.15, seit 0.5.0): ein ADMIN spielt heute als Funker und ist einteilbar; fehlt sonst.</summary>
    public bool? AlsFunker { get; init; }

    /// <summary>Details für <c>ZUWEISEN</c> (6.16, seit 0.6.0): die gewählte Rolle, wie in Mitgliedschaften.</summary>
    public ushort? Rolle { get; init; }

    /// <summary>
    /// Details für <c>ZUWEISEN</c> (6.17, seit 0.6.0): die Kanalsperren dieses Kontos (höchstens 16); die für alle stehen in
    /// <c>KanalSperre LISTE</c>.
    /// </summary>
    public IReadOnlyList<Kanalsperre>? Gesperrt { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.NameMax, "eintraege.name");
        p.PflichtText(Benutzername, Grenzen.NameMax, "eintraege.benutzername");
        Mitgliedschaft.ListePruefen(Felder, ref p, "eintraege.felder");
        p.Liste(SendetIn, Grenzen.FelderMax + 1, "eintraege.sendetIn");
        p.Liste(Stumm, int.MaxValue, "eintraege.stumm");
        p.Liste(Gesperrt, Kanalrechnung.SperrenJeKontoMax, "eintraege.gesperrt");
        p.Text(ScName, Grenzen.NameMax, "eintraege.scName");
        p.Text(DiscordName, Grenzen.NameMax, "eintraege.discordName");
        p.Texte(Zeichen, Grenzen.ZeichenMax, 16, "eintraege.zeichen");
        p.Texte(Abzeichen, Grenzen.AbzeichenMax, Grenzen.AbzeichenKennungMax, "eintraege.abzeichen");
        p.Text(AbzeichenVorn, Grenzen.AbzeichenKennungMax, "eintraege.abzeichenVorn");
    }
}

/// <summary>0x0024 SendeFehler (S->C).</summary>
public sealed record SendeFehler : ISteuerBody
{
    public required IReadOnlyList<SendeFehlerEintrag> Fehler { get; init; }
    public required bool Teilweise { get; init; }
    public required uint Verworfen { get; init; }

    public void Pruefen(ref Pruefung p) => p.Liste(Fehler, Grenzen.SendeFehlerMax, "fehler");
}

/// <summary>Eintrag in <c>SendeFehler.fehler</c>.</summary>
public sealed record SendeFehlerEintrag
{
    public required SendeFehlerCode Code { get; init; }
    public ushort? NetzId { get; init; }
    public uint? Durch { get; init; }
}
