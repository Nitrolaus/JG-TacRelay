// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Serialization;

namespace Funk.Protokoll.Steuerung;

// Aufzählungen der JSON-Bodies aus 4.5. Drahtform per StrengeAufzaehlung: HoerenSenden -> "HOEREN_SENDEN".

/// <summary>Schlüsselart laut Client (3.2), ungeprüft.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<SchluesselArt>))]
public enum SchluesselArt { Tpm, Software }

/// <summary><c>Welcome.status</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<WelcomeStatus>))]
public enum WelcomeStatus { Unbekannt, Wartet, Freigegeben }

/// <summary><c>Bye.grund</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<ByeGrund>))]
public enum ByeGrund { Beendet, Wartung, Verdraengt, Ersetzt, Gesperrt, Entzogen, Entfernt, Wartezeit, Protokoll }

/// <summary>Stufe eines Kontos (6.1).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<Stufe>))]
public enum Stufe { Admin, Raidleiter, Funker }

/// <summary>Einzelrechte (6.1).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<Recht>))]
public enum Recht
{
    GlobalSenden, Durchsage, DurchsageAlle, MehrfachSenden, Zuweisen, SprechanzeigeAlle,
    GeraeteFreigeben, KontenVerwalten, NetzeVerwalten,
}

/// <summary>Status eines Geräts (3.3, 3.6), dazu BEANTRAGT für einen Entfernungsantrag.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<GeraeteStatus>))]
public enum GeraeteStatus { Wartet, Freigegeben, Abgelehnt, Entfernt, Entzogen, Beantragt }

/// <summary>Markierung durch ein Geschwistergerät (3.5).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<Markierung>))]
public enum Markierung { Bestaetigt, Einspruch }

/// <summary>
/// <c>GeraetEntscheiden.entscheidung</c>. ENTSPERREN (seit 0.8.0) hebt die bewusste Sperre eines Geräts auf (3.6); ein Client
/// stellt es nur an Server ab <see cref="Versionen.EntsperrenAb"/>, ein älterer antwortet darauf UNGUELTIG.
/// </summary>
[JsonConverter(typeof(StrengeAufzaehlung<GeraetEntscheidung>))]
public enum GeraetEntscheidung { Freigeben, Ablehnen, Bestaetigen, Einspruch, Entsperren }

/// <summary><c>KontoVerwaltung.op</c>; SCNAMEN aus F6, DISCORDNAME aus A53 (den Discord-Namen ändert nur ein Admin).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<KontoOperation>))]
public enum KontoOperation { Sperren, Entsperren, Stufe, Rechte, Name, Loeschen, Scnamen, Discordname }

/// <summary>Zustand des Admin-Nachweises des eigenen Kontos (3.9).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<AdminStatus>))]
public enum AdminStatus { Vorgesehen, Einrichtung, Aktiv, Gesperrt }

/// <summary><c>AdminEinrichtung.op</c> (3.9).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<AdminEinrichtungOperation>))]
public enum AdminEinrichtungOperation { Antrag, GeheimnisAbholen, Bestaetigen }

/// <summary><c>Mitglied.op</c> (6.13).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<MitgliedOperation>))]
public enum MitgliedOperation { Liste, Vormerken, Aendern, Loeschen, Einfuegen }

/// <summary><c>Untergilde.op</c> (6.13), seit 0.11.8 entfallen: nur noch die Hülle.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<UntergildeOperation>))]
public enum UntergildeOperation { Liste, Setzen, Loeschen }

/// <summary>Zustand eines Mitglieds in der Liste der Verwaltung.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<MitgliedZustand>))]
public enum MitgliedZustand { Online, Freigegeben, Antrag, Vorgemerkt, Gesperrt }

/// <summary><c>netze[].typ</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<NetzTyp>))]
public enum NetzTyp { Global, Normal }

/// <summary>Auslöser: Sprachaktivierung oder Taste.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<Ausloeser>))]
public enum Ausloeser { Va, Taste }

/// <summary><c>netze[].offen</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<NetzOffen>))]
public enum NetzOffen { Nein, Hoeren, HoerenSenden }

/// <summary>Platzzustand eines Kontos.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<PlatzStatus>))]
public enum PlatzStatus { Belegt, Reserviert }

/// <summary><c>Mitgliedschaften.quelle</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<MitgliedschaftsQuelle>))]
public enum MitgliedschaftsQuelle { Standard, Rolle, Selbst, Verschieben, Aufstellung, Wiederhergestellt, Satz }

/// <summary>Weg der Sprache: UDP oder Tunnel.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<Verbindungsweg>))]
public enum Verbindungsweg { Udp, Tunnel }

/// <summary>Verbindungsgüte (7.8).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<Guete>))]
public enum Guete { Gut, Maessig, Schlecht }

/// <summary><c>SendeFehler.fehler[].code</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<SendeFehlerCode>))]
public enum SendeFehlerCode
{
    KeinSenderecht, ZielEntzogen, NurTaste, KeinRechtGlobal, KeinRechtDurchsage, MehrfachNichtErlaubt,
    Stummgeschaltet, UnbekanntesNetz, Ungueltig, Grenze,
}

/// <summary><c>AufstellungAnwenden.modus</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<AufstellungsModus>))]
public enum AufstellungsModus { Ergaenzen, Ersetzen }

/// <summary><c>RegelTabelle.op</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<RegelOperation>))]
public enum RegelOperation { Lesen, Setzen }

/// <summary><c>DurchsageStart.art</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<DurchsageArt>))]
public enum DurchsageArt { Durchsage, Global }

/// <summary><c>Sprechanzeige.aktiv[].art</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<SprechArt>))]
public enum SprechArt { Netz, Durchsage, Global }

/// <summary><c>Ablage.op</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<AblageOperation>))]
public enum AblageOperation { Lesen, Schreiben }

/// <summary><c>Ablage.bereich</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<AblageBereich>))]
public enum AblageBereich { Saetze, Letzte, Ansicht }

/// <summary><c>KeyRotation.grund</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<RotationsGrund>))]
public enum RotationsGrund { Zaehler, Fehler }

/// <summary><c>UdpStatus.zustand</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<UdpZustand>))]
public enum UdpZustand { Aus, Pruefung, Gebunden }

/// <summary><c>TunnelModus.grund</c>.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<TunnelGrund>))]
public enum TunnelGrund { KeineBindung, UdpAusfall, UdpZurueck, Nutzer }

/// <summary>
/// <c>Verwaltung.op</c>; PAKET seit 0.3.0 (N5): bis zu 200 Operationen, alles oder nichts. PHASE_SETZEN und PHASE_LOESCHEN seit 0.5.0
/// (6.14): Ein Server 0.4.x kennt sie nicht, der Verwaltungsteil schickt sie nur an Server ab 0.5.0. Clients lesen die Aufzählung
/// nie, deshalb bricht der neue Wert keinen Client 0.4.0.
/// </summary>
[JsonConverter(typeof(StrengeAufzaehlung<VerwaltungsOperation>))]
public enum VerwaltungsOperation
{
    NetzSetzen, NetzLoeschen, SchiffSetzen, SchiffLoeschen, RolleSetzen, RolleLoeschen, Selbstwahl, Einstellung, Paket, PhaseSetzen, PhaseLoeschen,
}

/// <summary><c>Beauftragung.op</c> (0x0017, 6.12).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<BeauftragungOperation>))]
public enum BeauftragungOperation { Erteilen, Entziehen, Abgeben, Liste }

/// <summary><c>KanalSperre.op</c> (0x003A, 6.17, seit 0.6.0).</summary>
[JsonConverter(typeof(StrengeAufzaehlung<KanalSperreOperation>))]
public enum KanalSperreOperation { Setzen, Aufheben, Liste }

/// <summary><c>schiffe[].art</c> (F5, A39): Schiff, Trupp (Infanterie) oder Orga-Einheit. Fehlt das Feld, ist es ein Schiff.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<Einheitsart>))]
public enum Einheitsart { Schiff, Trupp, Orga }
