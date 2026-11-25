// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>Ein Netz (2. Begriffe, 4.5 NetzListe). ID 1 ist Global.</summary>
public sealed record Netz(ushort Id, string Name)
{
    public const ushort GlobalId = 1;

    public NetzTyp Typ { get; init; } = NetzTyp.Normal;
    public ushort Reihenfolge { get; init; }
    public bool NurTaste { get; init; }

    /// <summary>
    /// Ohne Angabe Taste: Das gilt nur für gespeicherte Stände ohne das Feld (seit 0.1.0 steht es immer da), damit sich an ihnen
    /// nichts ändert. Neue Netze bekommen ihren Auslöser immer ausdrücklich: an der Konsole seit 0.5.0 ohne Schalter VA (A62),
    /// im Verwaltungsteil VA, Global Taste (A64).
    /// </summary>
    public Ausloeser AusloeserStandard { get; init; } = Ausloeser.Taste;
    public Offenheit Offen { get; init; } = Offenheit.Nein;

    /// <summary>Teil von STANDARD (6.9).</summary>
    public bool Standard { get; init; }

    /// <summary>0 = kein Schiffsnetz.</summary>
    public ushort SchiffId { get; init; }

    public string? Schiffsnetz { get; init; }

    /// <summary>
    /// Ch.-Bezeichnungen eines Kanalnetzes (6.16, seit 0.6.0), Index 0 ist die Ruhe; leer bei einem gewöhnlichen Netz. Stände von
    /// vor 0.6.0 kennen das Feld nicht.
    /// </summary>
    public ImmutableArray<string> Kanaele { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Gruppe für die Kanalwahl ("Schiffe"), seit 0.6.0.</summary>
    public string? Gruppe { get; init; }

    /// <summary>Woher das Netz kommt ("Star Citizen", "von Hand"), seit 0.8.0; nur für die Verwaltung. Stände davor kennen das Feld nicht.</summary>
    public string? Herkunft { get; init; }

    /// <summary>Global nach Erstkonfiguration (6.5): nurTaste, Auslöser TASTE.</summary>
    public static Netz Global(string name = "Global") =>
        new(GlobalId, name) { Typ = NetzTyp.Global, NurTaste = true, AusloeserStandard = Ausloeser.Taste };
}

/// <summary>
/// Kapazität einer Rolle auf einem Schiff, dazu die Mindestbesetzung (F5): nur für die Ampel auf dem Brett, der Server
/// prüft sie beim Einteilen nicht. 0 heißt keine.
/// </summary>
public readonly record struct SchiffsPlatz(ushort RolleId, byte Kapazitaet)
{
    public byte Mindest { get; init; }
}

/// <summary>
/// Einheit mit Netz-Kennungen und Plätzen (4.5 NetzListe <c>schiffe[]</c>): ein Schiff, ein Trupp der Infanterie oder eine
/// Orga-Einheit (A39). Alles außer <see cref="Art"/> und <see cref="Typ"/> ist für alle drei gleich; im Code und auf dem
/// Draht heißt sie weiter Schiff.
/// </summary>
public sealed record Schiff(ushort Id, string Name)
{
    public ushort Reihenfolge { get; init; }

    /// <summary>Schiff (Standard, auch für Stände ohne das Feld), Trupp oder Orga.</summary>
    public Einheitsart Art { get; init; }

    /// <summary>Freier Typ, etwa "Hammerhead", höchstens 32 Zeichen.</summary>
    public string? Typ { get; init; }

    /// <summary>Kennung (etwa <c>CREW</c>) auf Netz-ID.</summary>
    public ImmutableSortedDictionary<string, ushort> Netze { get; init; } = ImmutableSortedDictionary.Create<string, ushort>(StringComparer.Ordinal);

    public ImmutableArray<SchiffsPlatz> Plaetze { get; init; } = [];

    /// <summary>Kapazität der Rolle auf diesem Schiff, null wenn die Rolle hier nicht wählbar ist.</summary>
    public byte? Kapazitaet(ushort rolleId)
    {
        foreach (var p in Plaetze)
            if (p.RolleId == rolleId) return p.Kapazitaet;
        return null;
    }
}

/// <summary>
/// Feld einer Rolle: genau eines von <see cref="NetzId"/> (ungleich 0) und <see cref="Schiffsnetz"/>. Nennt <see cref="NetzId"/> ein
/// Kanalnetz, legt <see cref="Ch"/> den Ch. fest, sonst wird er aufgelöst (6.16, seit 0.6.0).
/// </summary>
public readonly record struct RollenFeld(byte Feld, ushort NetzId, string? Schiffsnetz, bool Hoeren, bool Senden, bool SendenStandard, bool Fest)
{
    public byte? Ch { get; init; }
}

/// <summary>Funkrolle als Vorlage für Felder (2. Begriffe, A22).</summary>
public sealed record Rolle(ushort Id, string Name)
{
    public ushort Reihenfolge { get; init; }
    public bool SelbstWaehlbar { get; init; }
    public bool BrauchtSchiff { get; init; }

    /// <summary>Kapazität ohne Schiff; null = unbegrenzt.</summary>
    public byte? Kapazitaet { get; init; }

    public ImmutableArray<RollenFeld> Felder { get; init; } = [];

    /// <summary>Woher die Rolle kommt, seit 0.8.0, wie <see cref="Netz.Herkunft"/>.</summary>
    public string? Herkunft { get; init; }
}

/// <summary>Mitgliedschaft eines Kontos: (Feld, Netz, hören, senden, fest) (2. Begriffe).</summary>
public readonly record struct Mitgliedschaft(byte Feld, ushort NetzId, bool Hoeren, bool Senden, bool Fest);

/// <summary>Stummschaltung; <see cref="NetzId"/> 0 = alle, 1 = nur Global (4.5 Stummschalten).</summary>
public readonly record struct Stummschaltung(ushort NetzId, long Bis, uint Durch);

/// <summary>
/// Kanalsperre (6.17, seit 0.6.0): <see cref="NetzId"/> ist eine Kanal-ID (ein Ch.), die Netz-ID eines Kanalnetzes (alle Ch.) oder ein
/// gewöhnliches Netz, nie Global (auch nicht dessen Ch. 1) und nie eine Ruhe. Am Konto gilt sie für dieses Konto, im Schnappschuss
/// (<see cref="Schnappschuss.SperrenAlle"/>) für alle ohne wirksames <c>ZUWEISEN</c>. Sie wirkt bis <see cref="Bis"/> (Lage-ms) und nur,
/// solange <see cref="Durch"/> <c>ZUWEISEN</c> hat; danach räumt der Fristen-Takt sie ab.
/// </summary>
public readonly record struct Kanalsperre(ushort NetzId, long Bis, uint Durch)
{
    /// <summary>Wann sie gesetzt wurde (Lage-ms).</summary>
    public long Seit { get; init; }

    /// <summary>
    /// Nur bei einer Sperre für alle: Sie trifft auch ADMIN-Konten ohne <c>ZUWEISEN</c> (Funker-Modus), weil ein ADMIN sie gesetzt hat. Ein
    /// ADMIN-Konto sperrt nur ein ADMIN (Rangregel 6.10), auch über eine Sperre für alle.
    /// </summary>
    public bool AuchAdmins { get; init; }
}

/// <summary>
/// Folge einer Änderung an einer Kanalsperre für den Steuerkanal (6.17): Journal, Meldung KANALSPERRE an <c>ZUWEISEN</c>.
/// <see cref="KontoId"/> 0 = Sperre für alle; <see cref="Durch"/>: wer gesetzt oder aufgehoben hat (0 beim Ablauf und beim Ende ohne
/// Zutun); <see cref="Grund"/> bei BEENDET: AUFTRAG (der Setzende hat kein <c>ZUWEISEN</c> mehr) oder NETZ (Kanal oder Netz gelöscht).
/// </summary>
public readonly record struct KanalsperrFolge(uint KontoId, Kanalsperre Sperre, KanalsperrAnlass Anlass, uint Durch, string? Grund = null);

/// <summary>Wegfall eines Senderechts durch eine Umstellung, für ZIEL_ENTZOGEN (4.5 SendeFehler).</summary>
public readonly record struct Entzug(ushort NetzId, long Zeit, uint Durch, long Umstellung);

/// <summary>Platz (Schiff, Rolle) eines Kontos; <see cref="SchiffId"/> 0 = ohne Schiff.</summary>
public readonly record struct Platz(ushort SchiffId, ushort RolleId, long Gesetzt);

/// <summary>Vorgabe durch ZUWEISEN oder eine Aufstellung, gilt 12 h ab dem Setzen (6.9).</summary>
public readonly record struct Reservierung(long Gesetzt, Quelle Quelle, uint Durch);

/// <summary>Feld, das bei einer Umstellung nicht untergebracht werden konnte (Mitgliedschaften.entfallen).</summary>
public readonly record struct EntfallenesFeld(byte Feld, ushort NetzId, string Grund);

/// <summary>Sendeauswahl: Feldnummern aufsteigend ohne Doppelte, dazu Global (2. Begriffe).</summary>
public sealed class Sendeauswahl : IEquatable<Sendeauswahl>
{
    public static readonly Sendeauswahl Leer = new([], false);

    public Sendeauswahl(ImmutableArray<byte> felder, bool global)
    {
        Felder = felder.IsDefault ? [] : felder;
        Global = global;
    }

    public ImmutableArray<byte> Felder { get; }
    public bool Global { get; }

    public bool Equals(Sendeauswahl? other) =>
        other is not null && Global == other.Global && Felder.AsSpan().SequenceEqual(other.Felder.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as Sendeauswahl);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Global);
        foreach (var f in Felder) h.Add(f);
        return h.ToHashCode();
    }

    public override string ToString() => $"[{string.Join(",", Felder)}]{(Global ? "+G" : "")}";
}

/// <summary>Früherer Stand zur Übernahme (Mitgliedschaften.letzterStand, A21).</summary>
public sealed record FrueherStand(Platz? Platz, ImmutableArray<Mitgliedschaft> Felder, Sendeauswahl Sendeauswahl);

/// <summary>Konto mit allem, was Weiterleitung und Rechte entscheiden (3.3, 6.2).</summary>
public sealed record Konto
{
    public required uint Id { get; init; }
    public string Name { get; init; } = "";
    public string? DiscordId { get; init; }
    public Stufe Stufe { get; init; }
    public Recht Plus { get; init; }
    public Recht Minus { get; init; }
    public KontoStatus Status { get; init; }

    // Nicht initialisierte Arrays werden beim Setzen zu leeren: der Relay-Thread liest Felder, Stumm und Entzuege ohne
    // weitere Prüfung, und ein default-Array würde dort erst beim Paket werfen.

    /// <summary>Felder aufsteigend nach Feldnummer, höchstens 16, Global nie darin.</summary>
    public ImmutableArray<Mitgliedschaft> Felder { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Vorschlag des Servers bzw. gespeicherter Stand (Komfortzustand liegt im <see cref="Komfortzustand"/>).</summary>
    public Sendeauswahl Sendeauswahl { get; init => field = value ?? Sendeauswahl.Leer; } = Sendeauswahl.Leer;

    public Platz? Platz { get; init; }
    public Reservierung? Reservierung { get; init; }
    public Quelle Quelle { get; init; }
    public uint QuelleDurch { get; init; }
    public ImmutableArray<Stummschaltung> Stumm { get; init => field = value.IsDefault ? [] : value; } = [];
    public ImmutableArray<Entzug> Entzuege { get; init => field = value.IsDefault ? [] : value; } = [];
    public ImmutableArray<EntfallenesFeld> Entfallen { get; init => field = value.IsDefault ? [] : value; } = [];
    public FrueherStand? LetzterStand { get; init; }

    /// <summary><c>zustandsVersion</c> der letzten weiterleitungsrelevanten Änderung (6.2).</summary>
    public long KontoVersion { get; init; }

    /// <summary>Ende der letzten Sitzung in ms, 0 = noch nie; Beginn der Wiederverbindungsfrist.</summary>
    public long SitzungsEnde { get; init; }

    /// <summary>
    /// Laufender oder geplanter Auftrag für die Einsatzleitung (6.12), höchstens einer je Konto. Er liegt im Lagezustand, weil
    /// die Weiterleitung je Paket nach ihm fragt (Global, Durchsage); die dauerhaften Rechte bleiben im Kontenbuch.
    /// </summary>
    public Beauftragung? Beauftragung { get; init; }

    /// <summary>
    /// "Heute als Funker" (6.15, seit 0.5.0): Bis wann (Lage-ms) ein ADMIN im Funk ein normaler Funker ist; 0 = aus. Abendzustand
    /// wie ein Auftrag: im Lagezustand, nicht im Kontenbuch, und die Sicherung lässt ihn weg. Solange er läuft, ruhen die Rechte
    /// der Einsatzleitung (<see cref="RechteZu"/>), und die Einsatzleitung darf das Konto einteilen (<see cref="Rechteregeln.DarfZuweisen"/>).
    /// </summary>
    public long AlsFunkerBis { get; init; }

    /// <summary>
    /// Die gewählte Rolle (6.16, seit 0.6.0): die Rolle, deren Felder das Konto über <c>RolleWaehlen</c> gerade hat, nicht der Platz.
    /// <c>null</c>, sobald eine andere Vorgabe die Felder bildet (Einsteigen, Verschieben, Aufstellung, STANDARD).
    /// </summary>
    public ushort? GewaehlteRolle { get; init; }

    /// <summary>
    /// Kanalsperren dieses Kontos (6.17, seit 0.6.0), höchstens 16, nach Netz-ID. Abendzustand wie ein Auftrag: im Lagezustand, die
    /// Sicherung lässt sie weg. Abgelaufene bleiben bis zum nächsten Fristen-Takt stehen und wirken nicht.
    /// </summary>
    public ImmutableArray<Kanalsperre> Sperren { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Ob der Funker-Modus zum Zeitpunkt <paramref name="jetzt"/> läuft. Eine Methode, damit sie nicht in <c>lage.json</c> steht.</summary>
    public bool AlsFunkerZu(long jetzt) => AlsFunkerBis > jetzt;

    /// <summary>Dauerhafte Rechte aus Stufe und Abweichungen, ohne Auftrag. Regel 1 aus 6.10 zählt nur sie.</summary>
    public Recht Rechte => Rechteregeln.Wirksam(Stufe, Plus, Minus);

    /// <summary>
    /// Rechte zum Zeitpunkt <paramref name="jetzt"/> (Lage-ms): die dauerhaften und, solange der Auftrag gilt, seine. So
    /// endet ein Auftrag für jede Aktion und jedes Sprachpaket genau zu <c>bis</c>, auch bevor der Takt ihn abräumt. Im
    /// Funker-Modus (6.15) fehlen die Rechte der Einsatzleitung; die Verwaltungsrechte bleiben (sie wirken ohnehin nur in einer
    /// Admin-Sitzung).
    /// </summary>
    public Recht RechteZu(long jetzt)
    {
        var r = Beauftragung is { } b && b.GiltZu(jetzt) ? (Rechte | b.Rechte) & Recht.Alle : Rechte;
        return AlsFunkerZu(jetzt) ? r & ~Rechteregeln.Einsatzleitung : r;
    }

    public bool IstPrivilegiert => Rechteregeln.IstPrivilegiert(Stufe, Rechte);

    /// <summary>Feld mit diesem Netz, sonst null.</summary>
    public Mitgliedschaft? FeldMitNetz(ushort netzId)
    {
        foreach (var f in Felder)
            if (f.NetzId == netzId) return f;
        return null;
    }

    /// <summary>Mitgliedschaft in diesem Feld, sonst null.</summary>
    public Mitgliedschaft? FeldNummer(byte feld)
    {
        foreach (var f in Felder)
            if (f.Feld == feld) return f;
        return null;
    }
}

/// <summary>
/// Einstellungen aus NetzListe (4.5). Die Sperre der Selbsteinteilung trägt seit 0.3.0 Urheber, Frist und die Ausnahme für
/// Nachzügler (F9): Wer noch keinen Platz hat, nimmt trotz gesperrtem Wechsel einen freien, außer bei "Ganz sperren".
/// </summary>
public sealed record Einstellungen(bool DurchsageNurTaste = true, bool Selbstwahl = true)
{
    /// <summary>Wer die Selbstwahl gesperrt hat; 0 = Konsole oder ein Stand von vor 0.3.0.</summary>
    public uint SelbstwahlDurch { get; init; }

    /// <summary>Ende der Sperre (Lage-ms), 12 h nach dem Setzen; 0 = ohne Frist (nur aus Ständen von vor 0.3.0).</summary>
    public long SelbstwahlBis { get; init; }

    /// <summary>Bei gesperrter Selbstwahl dürfen Konten ohne Platz einen freien Platz nehmen ("Wechsel sperren"); false = "Ganz sperren".</summary>
    public bool Nachzuegler { get; init; } = true;
}

/// <summary>Aktive Sitzung eines freigegebenen Geräts (nur AKTIV_SYNC und AKTIV, 6.8).</summary>
public readonly record struct AktiveSitzung(uint SitzungsId, uint KontoId, uint GeraetId);

/// <summary>Feld in einer Aufstellung oder Verschieben-Liste: immer fest (4.5).</summary>
public readonly record struct Feldvorgabe(byte Feld, ushort NetzId, bool Hoeren, bool Senden);

/// <summary>Eintrag einer Aufstellung (4.5 AufstellungSpeichern): <see cref="KontoId"/> oder <see cref="DiscordId"/>.</summary>
public sealed record AufstellungsEintrag
{
    /// <summary>0 = über <see cref="DiscordId"/>.</summary>
    public uint KontoId { get; init; }

    public string? DiscordId { get; init; }

    /// <summary>0 = keine Rolle, kein Platz.</summary>
    public ushort RolleId { get; init; }

    /// <summary>0 = ohne Schiff.</summary>
    public ushort SchiffId { get; init; }

    public ImmutableArray<Feldvorgabe>? Felder { get; init; }
    public ImmutableArray<byte>? SendeFelder { get; init; }
}

/// <summary>Benannte, gespeicherte Aufstellung (2. Begriffe, A5, A13).</summary>
public sealed record Aufstellung(uint Id, uint Version, string Name)
{
    public ImmutableArray<AufstellungsEintrag> Eintraege { get; init; } = [];
    public long Geaendert { get; init; }

    /// <summary>Wer zuletzt geändert hat.</summary>
    public uint Durch { get; init; }

    /// <summary>
    /// Wer sie angelegt hat (F10): Ändern und Löschen nur er oder ein ADMIN. Stände von vor 0.3.0 kennen das Feld nicht; dort
    /// gilt beim Laden <see cref="Durch"/>.
    /// </summary>
    public uint Ersteller { get; init; }
}

/// <summary>
/// "Auftrag für einen Abend" (RAIDLEITUNG-ENTWURF, 6.12, A37): befristete Beauftragung eines Kontos mit der Einsatzleitung
/// durch einen Admin. Im Code <c>Beauftragung</c>, weil <see cref="Auftrag"/> die Einträge der Warteschlange des
/// Zustandsschreibers sind. Liegt am Lage-Konto (<see cref="Konto.Beauftragung"/>) und wird mit dem Lagezustand
/// gespeichert; die Sicherung (<c>funk-admin sicherung</c>) lässt sie weg. Zeiten in Lage-ms.
/// </summary>
/// <param name="KontoId">Beauftragtes Konto.</param>
/// <param name="Von">Beginn (auch im Voraus erteilt, höchstens 14 Tage).</param>
/// <param name="Bis">Ablauf, höchstens 24 h nach <paramref name="Von"/>; danach gilt die Beauftragung nicht mehr, ohne dass jemand etwas tut.</param>
/// <param name="Rechte">Verliehene Rechte, höchstens die der Führung und <c>ZUWEISEN</c>, nie ein Verwaltungsrecht.</param>
/// <param name="Durch">Konto des Admins, der sie erteilt hat (0 = Konsole).</param>
/// <param name="Seit">Wann sie erteilt wurde.</param>
public sealed record Beauftragung(uint KontoId, long Von, long Bis, Recht Rechte, uint Durch, long Seit)
{
    /// <summary>Anlass, höchstens 48 Zeichen ("Op Nachtfalke").</summary>
    public string? Grund { get; init; }

    /// <summary>Der Beginn ist gemeldet (Journal, Meldung BEGONNEN); bei einem sofort wirksamen Auftrag gleich beim Erteilen.</summary>
    public bool Begonnen { get; init; }

    /// <summary>Gilt zum Zeitpunkt <paramref name="jetzt"/> (Lage-ms).</summary>
    public bool GiltZu(long jetzt) => jetzt >= Von && jetzt < Bis;
}

/// <summary>
/// Folge einer Änderung an einem Auftrag für den Steuerkanal (6.12 Regel 8 und 9): Journal, Meldung BEAUFTRAGUNG, Bot-Zeile,
/// ein voller Praesenz-Stand für den Beauftragten. <see cref="Durch"/> ist, wer es ausgelöst hat (0 = Konsole oder Takt);
/// <see cref="Grund"/> sagt bei BEENDET, warum (GESPERRT, STUFE, GELOESCHT, ERTEILER).
/// </summary>
public readonly record struct BeauftragungsFolge(uint KontoId, BeauftragungsAnlass Anlass, Beauftragung Auftrag, uint Durch, string? Grund = null);

/// <summary>Ein Schritt eines Verwaltungspakets (N5, <c>Verwaltung PAKET</c>): dieselben Operationen wie einzeln, alles oder nichts.</summary>
public abstract record Verwaltungsschritt
{
    public sealed record NetzSetzen(Netz Netz) : Verwaltungsschritt;

    public sealed record NetzLoeschen(ushort Id) : Verwaltungsschritt;

    public sealed record SchiffSetzen(Schiff Schiff) : Verwaltungsschritt;

    public sealed record SchiffLoeschen(ushort Id) : Verwaltungsschritt;

    public sealed record RolleSetzen(Rolle Rolle) : Verwaltungsschritt;

    public sealed record RolleLoeschen(ushort Id) : Verwaltungsschritt;

    public sealed record Einstellung(bool DurchsageNurTaste) : Verwaltungsschritt;

    /// <summary>PHASE_SETZEN (6.14, seit 0.5.0).</summary>
    public sealed record PhaseSetzen(Einsatzphase Phase) : Verwaltungsschritt;

    /// <summary>PHASE_LOESCHEN (6.14, seit 0.5.0).</summary>
    public sealed record PhaseLoeschen(byte Nr) : Verwaltungsschritt;
}

/// <summary>Variante einer Rolle in einer Einsatzphase: eine vollständige Feldliste in der Form von <see cref="Rolle.Felder"/>.</summary>
public sealed record PhasenVariante(ushort RolleId)
{
    public ImmutableArray<RollenFeld> Felder { get; init => field = value.IsDefault ? [] : value; } = [];
}

/// <summary>
/// Einsatzphase der Flotte (6.14, RAID-S2-ENTWURF Abschnitt 3): ein Netzbild, keine Personenliste. Nummer 1 ist die Grundstellung
/// (die Felder der Rollen) und trägt nur Name und Kurzname; 2 bis 5 tragen je Rolle, die sich ändert, eine Variante. Plätze, Rollen
/// und Belegung ändert eine Phase nie. Im Code "Einsatzphase", weil der Weiterleiter "Phase" für Sprechphasen braucht.
/// </summary>
public sealed record Einsatzphase(byte Nr, string Name, string Kurz)
{
    public ImmutableArray<PhasenVariante> Varianten { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Die Variante dieser Rolle oder <c>null</c>, dann gilt in dieser Phase die Grundstellung der Rolle.</summary>
    public PhasenVariante? VarianteFuer(ushort rolleId)
    {
        foreach (var v in Varianten)
            if (v.RolleId == rolleId) return v;
        return null;
    }

    /// <summary>Zahl der Variantenfelder (Grenze <see cref="Fristen.PhasenFelderMax"/> über alle Phasen).</summary>
    public int FelderAnzahl()
    {
        var n = 0;
        foreach (var v in Varianten) n += v.Felder.Length;
        return n;
    }
}

/// <summary>
/// Die laufende Einsatzphase im Lagezustand (6.14 Regel 10), nicht in der NetzListe: Sonst ginge bei jedem Schalten die ganze
/// NetzListe hinaus. <see cref="Durch"/> 0 = Konsole oder Rückfall nach 12 h.
/// </summary>
public sealed record LaufendePhase(byte Nr, long Seit, uint Durch);

/// <summary>
/// Folge eines Phasenwechsels für den Steuerkanal (6.14 Regel 11): Push PhaseGeschaltet an alle, Mitgliedschaften mit
/// <c>phase</c> an die Umgestellten. <see cref="Ausgelassen"/>: Konten, die der Schaltende nach der Rangregel nicht einteilen darf.
/// </summary>
public sealed record Phasenwechsel(byte Nr, byte Vorher, uint Durch, long Seit)
{
    public ImmutableArray<uint> Umgestellt { get; init; } = [];
    public ImmutableArray<uint> Ausgelassen { get; init; } = [];
}

/// <summary>
/// Folge eines Wechsels im Funker-Modus (6.15) für den Steuerkanal: Journal, voller Praesenz-Stand an das Konto, Ende des
/// Sprechanzeige-Abos. <see cref="Grund"/>: null, wenn das Konto selbst geschaltet hat, sonst ABGELAUFEN, STUFE oder GESPERRT.
/// </summary>
public readonly record struct FunkermodusFolge(uint KontoId, bool An, long Bis, string? Grund = null);

/// <summary>Belegung eines Platzes (Einheit, Rolle) für das Live-Brett und das Einsteigen (Push <c>Belegung</c>).</summary>
/// <param name="SchiffId">Einheit, 0 = Rolle ohne Einheit.</param>
/// <param name="Kapazitaet">null = unbegrenzt (Rolle ohne Einheit ohne Kapazität).</param>
/// <param name="Mindest">Mindestbesetzung für die Ampel, 0 = keine.</param>
public readonly record struct Platzbelegung(ushort SchiffId, ushort RolleId, int Belegt, int Reserviert, byte? Kapazitaet, byte Mindest);

/// <summary>Belegung aller Plätze, dazu wie viele online sind und wie viele davon einen Platz haben.</summary>
public sealed record Belegungsstand(ImmutableArray<Platzbelegung> Plaetze, int Online, int MitPlatz)
{
    public bool Gleich(Belegungsstand? anderer) =>
        anderer is not null && Online == anderer.Online && MitPlatz == anderer.MitPlatz && Plaetze.AsSpan().SequenceEqual(anderer.Plaetze.AsSpan());
}
