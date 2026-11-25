// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>Fehler zu einem Eintrag einer Anfrage (<c>details.fehler</c>, 4.5).</summary>
public readonly record struct Eintragsfehler(int Eintrag, uint KontoId, string? DiscordId, Fehlercode Code);

/// <summary>Konto mit seiner Version (<c>details.konten</c> bei VERALTET, 4.5).</summary>
public readonly record struct KontoStand(uint KontoId, long KontoVersion);

/// <summary>Übersprungener Eintrag: bei Aufstellungen der Eintrag, bei SatzAnwenden das Feld.</summary>
public readonly record struct UebersprungenerEintrag(int Eintrag, uint KontoId, Fehlercode Code);

/// <summary>Sitzung, die eine Änderung beendet; der Steuerkanal schickt Bye oder Fehler und trennt.</summary>
public readonly record struct BeendeteSitzung(uint SitzungsId, uint KontoId, Sitzungsende Grund);

/// <summary>Anlass für eine Meldung (4.5); die Empfänger ergeben sich aus der Art.</summary>
public readonly record struct Meldung(MeldungsArt Art, uint KontoId, uint Durch = 0);

/// <summary>
/// Antwort des Zustandsschreibers auf einen Auftrag, dazu die Folgen für den Steuerkanal: wer
/// <c>Mitgliedschaften</c> bekommt, welche Sitzungen enden, welche Meldungen anfallen (6.7 Schritt 7).
/// </summary>
public sealed class Ergebnis
{
    public Fehlercode Fehler { get; init; }

    public bool Erfolg => Fehler == Fehlercode.Keiner;

    /// <summary>Nach dem Austausch gesetzt: die Version, die diese Änderung enthält.</summary>
    public long ZustandsVersion { get; internal set; }

    /// <summary>Alle betroffenen Einträge bei einer Ablehnung (<c>details.fehler</c>).</summary>
    public ImmutableArray<Eintragsfehler> Fehlerliste { get; init; } = [];

    /// <summary>Bei VERALTET die Konten mit neuerer <c>kontoVersion</c> (<c>details.konten</c>).</summary>
    public ImmutableArray<KontoStand> VeralteteKonten { get; init; } = [];

    public ImmutableArray<uint> Offline { get; init; } = [];
    public ImmutableArray<uint> Verdraengt { get; init; } = [];

    /// <summary>Bei ERSETZEN auf STANDARD zurückgefallene, nicht gelistete Konten (6.7 Schritt 3).</summary>
    public ImmutableArray<uint> Zurueckgesetzt { get; init; } = [];

    public ImmutableArray<string> Unverknuepft { get; init; } = [];
    public ImmutableArray<UebersprungenerEintrag> Uebersprungen { get; init; } = [];

    /// <summary>Zahl (PlaetzeFreigeben: freigegebene Plätze; AufstellungAnwenden: angewendete Einträge).</summary>
    public int Anzahl { get; init; }

    public uint AufstellungsId { get; init; }
    public uint AufstellungsVersion { get; init; }

    /// <summary>Konten, deren eigene Sitzung <c>Mitgliedschaften</c> bekommen soll.</summary>
    public ImmutableArray<uint> BetroffeneKonten { get; init; } = [];

    /// <summary>
    /// An den Praesenz-Einträgen der <see cref="BetroffeneKonten"/> ändern sich nur Details (Felder, Sendeauswahl, gewählte Rolle, eigene
    /// Kanalsperren), die allein Sitzungen mit <c>ZUWEISEN</c> bekommen (4.5): Dann geht das Praesenz-Delta nur an sie. Ein Ch.-Wechsel
    /// schickte sonst jedem Funker einen unveränderten Eintrag (Gegenprüfung 3.7).
    /// </summary>
    public bool NurDetails { get; init; }

    /// <summary>
    /// Konten, deren Sitzung nur <c>Mitgliedschaften</c> bekommt, ohne Praesenz-Delta: Eine Sperre für alle (6.17) ändert ihre
    /// <c>gesperrt</c>-Liste, aber an keinem Praesenz-Eintrag etwas (Gegenprüfung 3.7; vorher ging ein Delta mit allen an alle).
    /// </summary>
    public ImmutableArray<uint> NurMitgliedschaften { get; init; } = [];

    public ImmutableArray<Meldung> Meldungen { get; init; } = [];
    public ImmutableArray<BeendeteSitzung> BeendeteSitzungen { get; init; } = [];
    public bool NetzListeGeaendert { get; init; }
    public bool AufstellungGeaendert { get; init; }

    /// <summary>Konto wurde privilegiert; seine Geräte fallen auf WARTET (6.10).</summary>
    public bool GeraeteZurueckstufen { get; init; }

    /// <summary>Aufträge, die erteilt, begonnen, entzogen, abgegeben, beendet oder abgelaufen sind (6.12).</summary>
    public ImmutableArray<BeauftragungsFolge> Beauftragungen { get; init; } = [];

    /// <summary>Wer eine Aufstellung gespeichert oder gelöscht hat (<c>AufstellungGeaendert.durch</c>, K3).</summary>
    public uint AufstellungDurch { get; init; }

    /// <summary>Ein Phasenwechsel (6.14): Push PhaseGeschaltet an alle, Mitgliedschaften mit <c>phase</c> an die Umgestellten.</summary>
    public Phasenwechsel? Phasenwechsel { get; init; }

    /// <summary>Bei VERALTET aus PhaseSchalten: die laufende Phase, die der Client nicht kannte (<c>details {nr, durch}</c>).</summary>
    public LaufendePhase? PhaseStand { get; init; }

    /// <summary>Beginn und Ende des Funker-Modus (6.15): Journal, voller Praesenz-Stand, Ende des Sprechanzeige-Abos.</summary>
    public ImmutableArray<FunkermodusFolge> Funkermodi { get; init; } = [];

    /// <summary>Kanalsperren, die gesetzt, aufgehoben, abgelaufen oder beendet sind (6.17): Journal, Meldung KANALSPERRE.</summary>
    public ImmutableArray<KanalsperrFolge> Kanalsperren { get; init; } = [];

    /// <summary>Bei KEIN_RECHT wegen einer Kanalsperre (FeldSetzen, SendeauswahlSetzen): die Sperre (<c>details {grund: "GESPERRT", durch, bis}</c>).</summary>
    public Kanalsperre? Sperre { get; init; }

    /// <summary>Klartext zu einer Ablehnung, etwa welche Grenze verletzt ist (<c>Fehler.text</c>).</summary>
    public string? Text { get; init; }

    /// <summary>Bei INTERN: die Ausnahme, die den Auftrag abgebrochen hat, für das Protokoll des Servers.</summary>
    public Exception? Ausnahme { get; init; }

    public static Ergebnis Mit(Fehlercode code) => new() { Fehler = code };

    public static Ergebnis Mit(Fehlercode code, IEnumerable<Eintragsfehler> fehler) =>
        new() { Fehler = code, Fehlerliste = [.. fehler] };

    public static Ergebnis Mit(Fehlercode code, string text) => new() { Fehler = code, Text = text };

    /// <summary>KEIN_RECHT, weil eine Kanalsperre greift (6.17).</summary>
    public static Ergebnis Gesperrt(Kanalsperre sperre) => new() { Fehler = Fehlercode.KeinRecht, Sperre = sperre };

    public override string ToString() => Erfolg ? $"OK v{ZustandsVersion}" : Drahtnamen.Von(Fehler);
}
