// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;

namespace Funk.Protokoll.Steuerung;

// Bodies der Raidleitung, Stufe 2 (Fassung 0.3.0): 0x0017 Beauftragung, 0x0038 KontenVerzeichnis, 0x4003 Belegung, dazu die
// Objekte für Verwaltung SELBSTWAHL und PAKET. Neue Felder in bestehenden Nachrichten sind optional (4.7); neue Pushes liegen
// im optionalen Bereich, damit Clients 0.2.0 sie übergehen.

/// <summary>
/// 0x0017 Beauftragung (C->S, 6.12): Auftrag für die Einsatzleitung. ERTEILEN und ENTZIEHEN nur mit <c>KONTEN_VERWALTEN</c> in
/// einer Admin-Sitzung mit frischer Bestätigung, LISTE in einer Admin-Sitzung, ABGEBEN der Beauftragte selbst. Zeiten in
/// Unix-ms; <c>von</c> fehlt oder liegt zurück: jetzt; <c>bis</c> fehlt: der nächste Zeitpunkt 04:00 Uhr (Europe/Berlin) nach dem
/// Beginn; <c>rechte</c> fehlt: alle sechs der Einsatzleitung.
/// </summary>
public sealed record BeauftragungAnfrage : ISteuerBody
{
    public required BeauftragungOperation Op { get; init; }
    public uint? KontoId { get; init; }
    public long? Von { get; init; }
    public long? Bis { get; init; }
    public IReadOnlyList<Recht>? Rechte { get; init; }
    public string? Grund { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Von, 0, JsonVorpruefung.MaxGanzzahl, "von");
        p.Bereich(Bis, 0, JsonVorpruefung.MaxGanzzahl, "bis");
        p.Liste(Rechte, Grenzen.RechteMax, "rechte");
        p.Text(Grund, Grenzen.AuftragGrundMax, "grund");
        if (Op is BeauftragungOperation.Erteilen or BeauftragungOperation.Entziehen) p.Pflicht(KontoId is not null, "kontoId");
    }
}

/// <summary>Ein Auftrag in der Liste der Verwaltung.</summary>
public sealed record BeauftragungEintrag : ISteuerBody
{
    public required uint KontoId { get; init; }
    public required string Name { get; init; }
    public string? ScName { get; init; }
    public required long Von { get; init; }
    public required long Bis { get; init; }
    public required IReadOnlyList<Recht> Rechte { get; init; }

    /// <summary>Wer ihn erteilt hat, 0 = Konsole.</summary>
    public required uint Durch { get; init; }
    public required long Seit { get; init; }
    public string? Grund { get; init; }

    /// <summary>Gilt gerade (zwischen <c>von</c> und <c>bis</c>); sonst geplant.</summary>
    public required bool Aktiv { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.NameMax, "beauftragungen.name");
        p.Text(ScName, Grenzen.NameMax, "beauftragungen.scName");
        p.Liste(Rechte, Grenzen.RechteMax, "beauftragungen.rechte");
        p.Text(Grund, Grenzen.AuftragGrundMax, "beauftragungen.grund");
    }
}

/// <summary>0x0017 Beauftragung, Antwort: alle laufenden und geplanten Aufträge (bei ABGEBEN nur noch die übrigen eigenen, also leer).</summary>
public sealed record BeauftragungAntwort : ISteuerBody
{
    public required IReadOnlyList<BeauftragungEintrag> Beauftragungen { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekte(Beauftragungen, Grenzen.AuftraegeMax, "beauftragungen");
}

/// <summary>
/// Der eigene Auftrag in <c>Welcome.konto</c> und <c>Mitgliedschaften.konto</c> (F2, F3): Das Brett erscheint mit
/// <see cref="Aktiv"/> und verschwindet ohne Neustart; der Kopf zeigt "Einsatzleitung bis 04:00 (von Nordwind)".
/// </summary>
public sealed record BeauftragungAngabe : ISteuerBody
{
    public required long Von { get; init; }
    public required long Bis { get; init; }
    public required IReadOnlyList<Recht> Rechte { get; init; }
    public required uint Durch { get; init; }
    public string? Grund { get; init; }
    public required bool Aktiv { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Liste(Rechte, Grenzen.RechteMax, "beauftragung.rechte");
        p.Text(Grund, Grenzen.AuftragGrundMax, "beauftragung.grund");
    }
}

/// <summary>Ein Konto in <c>KontenVerzeichnis</c> (N4): auch offline, für "Alle Mitglieder" auf dem Brett.</summary>
public sealed record VerzeichnisEintrag : ISteuerBody
{
    public required uint KontoId { get; init; }
    public required string Name { get; init; }
    public required string Benutzername { get; init; }
    public string? ScName { get; init; }
    public string? DiscordName { get; init; }
    public required Stufe Stufe { get; init; }
    public required bool Online { get; init; }
    public ushort? SchiffId { get; init; }
    public ushort? RolleId { get; init; }
    public PlatzStatus? Platz { get; init; }
    public LetzterPlatz? LetzterPlatz { get; init; }

    /// <summary>Untergilde. Seit 0.11.8 entfallen: Der Server füllt das Feld nicht mehr; es bleibt nur für ältere Gegenstellen.</summary>
    public ushort? Untergilde { get; init; }

    /// <summary>Ein ADMIN spielt heute als Funker (6.15, seit 0.5.0) und ist einteilbar; fehlt sonst.</summary>
    public bool? AlsFunker { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.NameMax, "konten.name");
        p.PflichtText(Benutzername, Grenzen.NameMax, "konten.benutzername");
        p.Text(ScName, Grenzen.NameMax, "konten.scName");
        p.Text(DiscordName, Grenzen.NameMax, "konten.discordName");
    }
}

/// <summary>0x0038 KontenVerzeichnis, Antwort (Anfrage <c>{}</c>, <c>ZUWEISEN</c>): alle freigegebenen, nicht gesperrten Konten, ohne Discord-ID.</summary>
public sealed record KontenVerzeichnisAntwort : ISteuerBody
{
    public required IReadOnlyList<VerzeichnisEintrag> Konten { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekte(Konten, Grenzen.KontenMax, "konten");
}

/// <summary>Belegung eines Platzes; ohne <c>schiffId</c> eine Rolle ohne Einheit.</summary>
public sealed record PlatzBelegung : ISteuerBody
{
    public ushort? SchiffId { get; init; }
    public required ushort RolleId { get; init; }
    public required ushort Belegt { get; init; }
    public required ushort Reserviert { get; init; }

    /// <summary>Fehlt bei unbegrenzter Kapazität.</summary>
    public byte? Kapazitaet { get; init; }

    /// <summary>Mindestbesetzung für die Ampel (F5), fehlt ohne.</summary>
    public byte? Mindest { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>
/// 0x4003 Belegung (S->C, optional, an alle AKTIV-Sitzungen bei jeder Änderung): je Platz BELEGT und RESERVIERT, wie die
/// Kapazität des Servers sie zählt (K1), dazu wie viele online sind und wie viele davon einen Platz haben. Daraus baut das
/// Brett Kopf ("23 online - 19 mit Platz - 4 ohne"), Spaltenköpfe ("5/8") und Ampel, das Einsteigen die Anzeige "Gunner 1/2".
/// </summary>
public sealed record Belegung : ISteuerBody
{
    public required long ZustandsVersion { get; init; }
    public required ushort Online { get; init; }
    public required ushort MitPlatz { get; init; }
    public required IReadOnlyList<PlatzBelegung> Plaetze { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(ZustandsVersion, 0, JsonVorpruefung.MaxGanzzahl, "zustandsVersion");
        p.Objekte(Plaetze, Grenzen.BelegungPlaetzeMax, "plaetze");
    }
}

/// <summary>
/// <c>Verwaltung SELBSTWAHL</c>, <c>objekt</c> (F9): <c>{an, nachzuegler?}</c>. Fehlt <c>nachzuegler</c>, gilt true ("Wechsel
/// sperren": wer keinen Platz hat, nimmt weiter einen freien); false ist "Ganz sperren".
/// </summary>
public sealed record SelbstwahlObjekt : ISteuerBody
{
    public required bool An { get; init; }
    public bool? Nachzuegler { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>Ein Schritt in <c>Verwaltung PAKET</c>: dieselben Operationen und Objekte wie einzeln, ohne SELBSTWAHL und PAKET.</summary>
public sealed record VerwaltungsSchritt : ISteuerBody
{
    public required VerwaltungsOperation Op { get; init; }
    public required JsonElement Objekt { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Regel(Op is not (VerwaltungsOperation.Paket or VerwaltungsOperation.Selbstwahl), "ops.op");
        p.IstObjekt(Objekt, "ops.objekt");
    }

    /// <summary>Liest <c>objekt</c> als <typeparamref name="T"/> und prüft es, wie <see cref="VerwaltungAnfrage.ObjektLesen{T}"/>.</summary>
    public DekodierErgebnis ObjektLesen<T>() where T : class, ISteuerBody =>
        SteuerJson.Lesen(System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(Objekt), SteuerJson.Info<T>());
}

/// <summary>
/// <c>Verwaltung PAKET</c>, <c>objekt</c> (N5): <c>{ops: [{op, objekt}] 1..200}</c>. Die NetzListe-Version steht wie immer in
/// <c>Verwaltung.version</c> und wird einmal geprüft; danach eine NetzListe für alles. Braucht eine frische Bestätigung.
/// </summary>
public sealed record VerwaltungsPaket : ISteuerBody
{
    public required IReadOnlyList<VerwaltungsSchritt> Ops { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Objekte(Ops, Grenzen.PaketSchritteMax, "ops");
        p.Regel(Ops.Count >= 1, "ops");
    }
}
