// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Frozen;
using System.Text.Json.Serialization.Metadata;

namespace Funk.Protokoll.Steuerung;

/// <summary>Wer einen Rahmen sendet.</summary>
public enum Seite : byte
{
    /// <summary>Client, also "hoch" (C->S).</summary>
    Client,
    /// <summary>Server, also "runter" (S->C).</summary>
    Server,
}

/// <summary>Art eines Rahmens nach Flags und Anfrage-Nr (4.2).</summary>
public enum Nachrichtenart : byte
{
    /// <summary>Anfrage-Nr 0, ohne ANTWORT.</summary>
    Push,
    /// <summary>Anfrage-Nr ungleich 0, ohne ANTWORT.</summary>
    Anfrage,
    /// <summary>Flag ANTWORT gesetzt.</summary>
    Antwort,
}

/// <summary>Zustände des Servers (4.6), in denen eine Nachricht vorkommt (Katalogspalte "Zustand").</summary>
[Flags]
public enum Zustaende : byte
{
    Keine = 0,
    WarteHello = 1,
    Registrierung = 2,
    Warteraum = 4,
    AktivSync = 8,
    Aktiv = 16,
    Alle = WarteHello | Registrierung | Warteraum | AktivSync | Aktiv,
}

/// <summary>Ob und mit welchem Body eine Nachricht in einer Rolle vorkommt. Binär: erlaubt ohne JSON-Info.</summary>
public readonly record struct Verwendung(bool Erlaubt, JsonTypeInfo? Body)
{
    internal static Verwendung Json<T>() => new(true, SteuerJson.Info<T>());

    internal static Verwendung Binaer => new(true, null);
}

/// <summary>Eine Zeile des Nachrichtenkatalogs (4.4) mit den Bodies je Richtung und Art (4.5).</summary>
public sealed class KatalogEintrag
{
    internal KatalogEintrag(Nachrichtentyp typ, Zustaende zustaende, string recht)
    {
        Typ = typ;
        Zustaende = zustaende;
        Recht = recht;
    }

    public Nachrichtentyp Typ { get; }

    /// <summary>Katalogspalte "Zustand" (Serverzustände).</summary>
    public Zustaende Zustaende { get; }

    /// <summary>Katalogspalte "Recht"; ausgewertet wird sie in der Rechteprüfung (6.1, 6.10), nicht hier.</summary>
    public string Recht { get; }

    public Typbereich Bereich => Typbereiche.Von((ushort)Typ);

    public bool IstBinaer => Bereich is Typbereich.BinaerPflicht or Typbereich.BinaerOptional;

    /// <summary>Client sendet als Push.</summary>
    public Verwendung HochPush { get; internal init; }
    /// <summary>Client stellt eine Anfrage.</summary>
    public Verwendung HochAnfrage { get; internal init; }
    /// <summary>Client beantwortet eine Server-Anfrage.</summary>
    public Verwendung HochAntwort { get; internal init; }
    /// <summary>Server sendet als Push.</summary>
    public Verwendung RunterPush { get; internal init; }
    /// <summary>Server stellt eine Anfrage.</summary>
    public Verwendung RunterAnfrage { get; internal init; }
    /// <summary>Server beantwortet eine Client-Anfrage.</summary>
    public Verwendung RunterAntwort { get; internal init; }

    /// <summary>Verwendung für einen Rahmen von <paramref name="absender"/> mit Art <paramref name="art"/>.</summary>
    public Verwendung Fuer(Seite absender, Nachrichtenart art) => (absender, art) switch
    {
        (Seite.Client, Nachrichtenart.Push) => HochPush,
        (Seite.Client, Nachrichtenart.Anfrage) => HochAnfrage,
        (Seite.Client, Nachrichtenart.Antwort) => HochAntwort,
        (Seite.Server, Nachrichtenart.Push) => RunterPush,
        (Seite.Server, Nachrichtenart.Anfrage) => RunterAnfrage,
        _ => RunterAntwort,
    };
}

/// <summary>Der Nachrichtenkatalog aus 4.4 und 4.5.</summary>
public static class Katalog
{
    private static readonly FrozenDictionary<ushort, KatalogEintrag> Eintraege = Aufbauen().ToFrozenDictionary(e => (ushort)e.Typ);

    /// <summary>Alle Einträge.</summary>
    public static IEnumerable<KatalogEintrag> Alle => Eintraege.Values;

    /// <summary>Eintrag zu einer Typnummer oder <c>null</c> für einen unbekannten Typ.</summary>
    public static KatalogEintrag? Finden(ushort typ) => Eintraege.GetValueOrDefault(typ);

    /// <summary>Eintrag zu einem bekannten Typ.</summary>
    public static KatalogEintrag Von(Nachrichtentyp typ) => Eintraege[(ushort)typ];

    private static List<KatalogEintrag> Aufbauen()
    {
        const Zustaende AbRegistrierung = Zustaende.Registrierung | Zustaende.Warteraum | Zustaende.AktivSync | Zustaende.Aktiv;
        const Zustaende Sync = Zustaende.AktivSync | Zustaende.Aktiv;
        var leer = Verwendung.Json<LeererBody>();
        var version = Verwendung.Json<ZustandsVersionBody>();

        KatalogEintrag Anfrage<TAnfrage, TAntwort>(Nachrichtentyp t, string recht, Zustaende z = Zustaende.Aktiv) =>
            new(t, z, recht) { HochAnfrage = Verwendung.Json<TAnfrage>(), RunterAntwort = Verwendung.Json<TAntwort>() };

        KatalogEintrag Push<T>(Nachrichtentyp t, string recht = "-", Zustaende z = Zustaende.Aktiv) =>
            new(t, z, recht) { RunterPush = Verwendung.Json<T>() };

        var fehler = Verwendung.Json<Fehler>();
        return
        [
            new(Nachrichtentyp.Hello, Zustaende.WarteHello, "-") { HochPush = Verwendung.Json<Hello>() },
            new(Nachrichtentyp.Welcome, Zustaende.WarteHello | Zustaende.Warteraum, "-") { RunterPush = Verwendung.Json<Welcome>() },
            new(Nachrichtentyp.Bye, Zustaende.Alle, "-") { HochPush = Verwendung.Json<Bye>(), RunterPush = Verwendung.Json<Bye>() },
            Anfrage<PingAnfrage, PingAntwort>(Nachrichtentyp.Ping, "-", AbRegistrierung),
            new(Nachrichtentyp.Fehler, Zustaende.Alle, "-") { HochPush = fehler, HochAntwort = fehler, RunterPush = fehler, RunterAntwort = fehler },
            Anfrage<RegistrierungAnfrage, RegistrierungAntwort>(Nachrichtentyp.Registrierung, "-", Zustaende.Registrierung),
            Push<GeraetStatus>(Nachrichtentyp.GeraetStatus, "-", Zustaende.Registrierung | Zustaende.Warteraum | Zustaende.Aktiv),
            Anfrage<GeraeteListeAnfrage, GeraeteListeAntwort>(Nachrichtentyp.GeraeteListe, "selbst, fremd GERAETE_FREIGEBEN"),
            Anfrage<GeraetEntscheidenAnfrage, LeererBody>(Nachrichtentyp.GeraetEntscheiden, "GERAETE_FREIGEBEN; selbes Konto nur Markierung"),
            Anfrage<GeraetEntfernenAnfrage, GeraetEntfernenAntwort>(Nachrichtentyp.GeraetEntfernen, "selbst, Geschwister nach 3.6, dauerhaft nur GERAETE_FREIGEBEN"),
            Anfrage<KontoVerwaltungAnfrage, LeererBody>(Nachrichtentyp.KontoVerwaltung, "KONTEN_VERWALTEN (6.10)"),
            new(Nachrichtentyp.VerknuepfungBestaetigen, Zustaende.Registrierung, "-")
            {
                RunterAnfrage = Verwendung.Json<VerknuepfungBestaetigenAnfrage>(),
                HochAntwort = Verwendung.Json<VerknuepfungBestaetigenAntwort>(),
            },
            Anfrage<BeauftragungAnfrage, BeauftragungAntwort>(Nachrichtentyp.Beauftragung,
                "ERTEILEN, ENTZIEHEN KONTEN_VERWALTEN in Admin-Sitzung mit frischer Bestätigung; LISTE Admin-Sitzung; ABGEBEN selbst"),
            Anfrage<ProfilSetzenAnfrage, ProfilSetzenAntwort>(Nachrichtentyp.ProfilSetzen, "selbst"),
            Anfrage<JournalAnfrage, JournalAntwort>(Nachrichtentyp.Journal, "KONTEN_VERWALTEN in Admin-Sitzung"),
            Anfrage<RollenkatalogAnfrage, RollenkatalogAntwort>(Nachrichtentyp.Rollenkatalog, "LESEN alle; SETZEN KONTEN_VERWALTEN in Admin-Sitzung"),
            Anfrage<MitgliedAnfrage, MitgliedAntwort>(Nachrichtentyp.Mitglied, "KONTEN_VERWALTEN in Admin-Sitzung"),
            Anfrage<LieblingsrollenAnfrage, LieblingsrollenAntwort>(Nachrichtentyp.Lieblingsrollen, "selbst"),
            Anfrage<UntergildeAnfrage, UntergildeAntwort>(Nachrichtentyp.Untergilde, "entfallen seit 0.11.8: LISTE leer, sonst UNGUELTIG"),
            Anfrage<MitgliedskarteAnfrage, MitgliedskarteAntwort>(Nachrichtentyp.Mitgliedskarte, "Grundangaben alle; Rest ZUWEISEN, Admin oder selbst"),
            Anfrage<AntraegeAblehnenAnfrage, AntraegeAblehnenAntwort>(Nachrichtentyp.AntraegeAblehnen, "GERAETE_FREIGEBEN in Admin-Sitzung, frische Bestätigung"),
            Anfrage<AbzeichenAnfrage, AbzeichenAntwort>(Nachrichtentyp.Abzeichen, "KONTEN_VERWALTEN in Admin-Sitzung"),
            Anfrage<MeldungSendenAnfrage, LeererBody>(Nachrichtentyp.MeldungSenden,
                "Haken Meldungen senden und Stufe RAIDLEITER oder ADMIN oder laufender Auftrag, nicht im Funker-Modus"),
            Anfrage<BereitMeldenAnfrage, LeererBody>(Nachrichtentyp.BereitMelden, "alle, solange die Bereit-Abfrage offen ist"),
            new(Nachrichtentyp.Zustand, Sync, "-") { HochAnfrage = leer, RunterAntwort = version, RunterPush = version },
            Push<NetzListe>(Nachrichtentyp.NetzListe, "-", Sync),
            Push<Mitgliedschaften>(Nachrichtentyp.Mitgliedschaften, "-", Sync),
            Push<Praesenz>(Nachrichtentyp.Praesenz, "Details nur ZUWEISEN", Sync),
            Push<SendeFehler>(Nachrichtentyp.SendeFehler),
            Anfrage<SendeauswahlSetzenAnfrage, LeererBody>(Nachrichtentyp.SendeauswahlSetzen, "selbst"),
            Anfrage<HoerenSetzenAnfrage, ZustandsVersionBody>(Nachrichtentyp.HoerenSetzen, "selbst"),
            Anfrage<EinsteigenRolleSchiffAnfrage, ZustandsVersionBody>(Nachrichtentyp.EinsteigenRolleSchiff, "selbst (Selbstwahl) oder ZUWEISEN"),
            Anfrage<VerschiebenAnfrage, VerschiebenAntwort>(Nachrichtentyp.Verschieben, "ZUWEISEN"),
            Anfrage<StummschaltenAnfrage, LeererBody>(Nachrichtentyp.Stummschalten, "ZUWEISEN"),
            Anfrage<FeldSetzenAnfrage, ZustandsVersionBody>(Nachrichtentyp.FeldSetzen, "selbst: Ch.-Wechsel im selben Kanalnetz, offene Netze in freie Felder"),
            Anfrage<SatzAnwendenAnfrage, SatzAnwendenAntwort>(Nachrichtentyp.SatzAnwenden, "selbst"),
            Anfrage<LeererBody, PlaetzeFreigebenAntwort>(Nachrichtentyp.PlaetzeFreigeben, "ZUWEISEN"),
            Anfrage<LeererBody, KontenVerzeichnisAntwort>(Nachrichtentyp.KontenVerzeichnis, "ZUWEISEN"),
            Anfrage<RolleWaehlenAnfrage, SatzAnwendenAntwort>(Nachrichtentyp.RolleWaehlen, "selbst, Rolle selbstwählbar, ohne Sperre der Selbstwahl"),
            Anfrage<KanalSperreAnfrage, KanalSperreAntwort>(Nachrichtentyp.KanalSperre, "ZUWEISEN; ein ADMIN-Konto nur durch einen ADMIN"),
            Anfrage<FunkermodusAnfrage, FunkermodusAntwort>(Nachrichtentyp.Funkermodus, "Stufe ADMIN, nur das eigene Konto, ohne Admin-Sitzung"),
            Anfrage<PhaseSchaltenAnfrage, PhaseSchaltenAntwort>(Nachrichtentyp.PhaseSchalten, "ZUWEISEN"),
            Anfrage<AufstellungSpeichernAnfrage, AufstellungsVerweis>(Nachrichtentyp.AufstellungSpeichern, "ZUWEISEN"),
            Anfrage<AufstellungListeAnfrage, AufstellungListeAntwort>(Nachrichtentyp.AufstellungListe, "ZUWEISEN"),
            Anfrage<AufstellungAnwendenAnfrage, AufstellungAnwendenAntwort>(Nachrichtentyp.AufstellungAnwenden, "ZUWEISEN"),
            Anfrage<AufstellungsVerweis, LeererBody>(Nachrichtentyp.AufstellungLoeschen, "ZUWEISEN"),
            Anfrage<RaidHelperImportAnfrage, RaidHelperImportAntwort>(Nachrichtentyp.RaidHelperImport, "ZUWEISEN"),
            Anfrage<RegelTabelleAnfrage, RegelTabelleAntwort>(Nachrichtentyp.RegelTabelle, "ZUWEISEN"),
            Anfrage<LeererBody, RaidHelperEventsAntwort>(Nachrichtentyp.RaidHelperEvents, "ZUWEISEN"),
            Push<AufstellungGeaendert>(Nachrichtentyp.AufstellungGeaendert, "an ZUWEISEN"),
            Push<DurchsageStart>(Nachrichtentyp.DurchsageStart),
            Push<DurchsageEnde>(Nachrichtentyp.DurchsageEnde),
            Anfrage<AnAus, LeererBody>(Nachrichtentyp.SprechanzeigeAbo, "SPRECHANZEIGE_ALLE"),
            Push<Sprechanzeige>(Nachrichtentyp.Sprechanzeige, "Abo"),
            new(Nachrichtentyp.PegelVoreinstellungen, Sync, "selbst")
            {
                RunterPush = Verwendung.Json<PegelVoreinstellungen>(),
                HochAnfrage = leer,
                RunterAntwort = Verwendung.Json<PegelVoreinstellungen>(),
            },
            Anfrage<PegelSetzenAnfrage, LeererBody>(Nachrichtentyp.PegelSetzen, "selbst"),
            Anfrage<AblageAnfrage, AblageAntwort>(Nachrichtentyp.Ablage, "selbst"),
            Anfrage<KeyRotationAnfrage, LeererBody>(Nachrichtentyp.KeyRotation, "-"),
            Push<UdpStatus>(Nachrichtentyp.UdpStatus),
            Anfrage<TunnelModusAnfrage, AnAus>(Nachrichtentyp.TunnelModus, "-"),
            new(Nachrichtentyp.EmpfangsBericht, Zustaende.Aktiv, "-") { HochPush = Verwendung.Json<EmpfangsBericht>() },
            Push<VerlustHinweis>(Nachrichtentyp.VerlustHinweis),
            Anfrage<VerwaltungAnfrage, LeererBody>(Nachrichtentyp.Verwaltung, "NETZE_VERWALTEN in Admin-Sitzung, SELBSTWAHL auch ZUWEISEN"),
            Anfrage<LeererBody, AdminNonceAntwort>(Nachrichtentyp.AdminNonce, "ADMIN-Konto"),
            Anfrage<AdminAnmeldungAnfrage, AdminSitzungAntwort>(Nachrichtentyp.AdminAnmeldung, "eingerichteter Admin mit Admin-Schlüssel"),
            Anfrage<AdminBestaetigenAnfrage, AdminSitzungAntwort>(Nachrichtentyp.AdminBestaetigen, "offene Admin-Sitzung"),
            Anfrage<LeererBody, LeererBody>(Nachrichtentyp.AdminAbmelden, "offene Admin-Sitzung"),
            Anfrage<AdminEinrichtungAnfrage, AdminEinrichtungAntwort>(Nachrichtentyp.AdminEinrichtung, "vorgesehenes Konto; Admin-Schlüssel"),
            Push<ServerLast>(Nachrichtentyp.ServerLast, "an ZUWEISEN und Admins"),
            Push<Meldung>(Nachrichtentyp.Meldung, "je Art"),
            Push<Belegung>(Nachrichtentyp.Belegung, "-", Sync),
            Push<PhaseGeschaltet>(Nachrichtentyp.PhaseGeschaltet, "-", Sync),
            Push<Meldestand>(Nachrichtentyp.Meldestand, "an alle", Sync),
            Push<BereitStand>(Nachrichtentyp.BereitStand, "an alle, die Meldungen senden dürfen", Sync),
            new(Nachrichtentyp.SpracheTunnel, Zustaende.Aktiv, "-") { HochPush = Verwendung.Binaer, RunterPush = Verwendung.Binaer },
            new(Nachrichtentyp.VoiceKeys, Sync, "-") { RunterAnfrage = Verwendung.Binaer, HochAntwort = Verwendung.Binaer },
        ];
    }
}
