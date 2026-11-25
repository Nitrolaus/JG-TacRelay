// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

/// <summary>Feld- und Anzahlgrenzen der Bodies aus 4.5 und 1.2.</summary>
public static class Grenzen
{
    /// <summary>Namen von Konten, Netzen und Schiffen, Benutzernamen: <c>str(32)</c>.</summary>
    public const int NameMax = 32;
    /// <summary>Rollennamen: <c>str(40)</c>.</summary>
    public const int RollenNameMax = 40;
    /// <summary>Gerätenamen: <c>str(40)</c>.</summary>
    public const int GeraeteNameMax = 40;
    /// <summary>Herkunft eines Netzes oder einer Rolle ("Star Citizen", "von Hand"), seit 0.8.0: <c>str(32)</c>.</summary>
    public const int HerkunftMax = 32;
    /// <summary>Anzeigeform eines Fingerabdrucks <c>XXXX-XXXX-XXXX-XXXX</c>: <c>str(19)</c>.</summary>
    public const int AnzeigeMax = 19;
    /// <summary><c>Hello.faehigkeiten</c>.</summary>
    public const int FaehigkeitenMax = 16;
    /// <summary>Es gibt 9 Einzelrechte (6.1).</summary>
    public const int RechteMax = 9;

    /// <summary>Netze in NetzListe.</summary>
    public const int NetzeMax = 128;
    /// <summary>Schiffe in NetzListe.</summary>
    public const int SchiffeMax = 64;
    /// <summary>Rollen in NetzListe.</summary>
    public const int RollenMax = 64;
    /// <summary>Schiffsnetz-Kennungen je Schiff.</summary>
    public const int SchiffsnetzKennungenMax = 8;
    /// <summary>Plätze je Schiff.</summary>
    public const int PlaetzeJeSchiffMax = 16;
    /// <summary>Felder je Konto, Rolle oder Satz.</summary>
    public const int FelderMax = 16;
    /// <summary>Höchste Feldnummer (Felder 0-15).</summary>
    public const int FeldMax = 15;
    /// <summary>Kleinste Netz-ID.</summary>
    public const int NetzIdMin = 1;
    /// <summary>Größte Netz-ID.</summary>
    public const int NetzIdMax = 65534;
    /// <summary>Netz-ID von Global.</summary>
    public const ushort GlobalNetzId = 1;

    /// <summary>Konten serverweit (1.2).</summary>
    public const int KontenMax = 500;
    /// <summary>Gleichzeitig aktive Sitzungen (1.2).</summary>
    public const int SitzungenMax = 100;
    /// <summary>Einträge in GeraeteListe.</summary>
    public const int GeraeteListeMax = 500;
    /// <summary>Einträge in SendeFehler.</summary>
    public const int SendeFehlerMax = 17;
    /// <summary>Änderungen je Verschieben.</summary>
    public const int AenderungenMax = 200;
    /// <summary>Höchste Stummschaltdauer.</summary>
    public const int StummSekundenMax = 3600;
    /// <summary>Name einer Aufstellung.</summary>
    public const int AufstellungsNameMax = 48;
    /// <summary>Einträge je Aufstellung.</summary>
    public const int AufstellungsEintraegeMax = 200;
    /// <summary>Anmeldungen im RaidHelperImport.</summary>
    public const int AnmeldungenMax = 200;
    /// <summary>Regeln der Regeltabelle.</summary>
    public const int RegelnMax = 200;
    /// <summary><c>className</c> und <c>specName</c>.</summary>
    public const int KlassenNameMax = 40;
    /// <summary>Events in RaidHelperEvents.</summary>
    public const int EventsMax = 50;
    /// <summary>Ziel-Netze je NETZ-Paket.</summary>
    public const int ZieleNetzMax = 16;
    /// <summary>Ziel-Netze je Durchsage.</summary>
    public const int ZieleDurchsageMax = 64;
    /// <summary>Einträge in PegelVoreinstellungen.</summary>
    public const int PegelMax = 2000;
    /// <summary><c>Ablage.daten</c> serialisiert.</summary>
    public const int AblageMax = 16 * 1024;
    /// <summary><c>Fehler.details</c> serialisiert.</summary>
    public const int FehlerDetailsMax = 4 * 1024;

    /// <summary>SC-Namen je Konto (Hauptname zuerst).</summary>
    public const int ScNamenMax = 3;
    /// <summary>Vormerkungen als Vorschlag in der Freigabe.</summary>
    public const int VorschlaegeMax = 8;
    /// <summary>Einträge im Rollenkatalog.</summary>
    public const int KatalogMax = 64;
    /// <summary>Mitgliederrollen je Mitglied bzw. Vormerkung, Rollen der Flotte je Rollenkatalogeintrag.</summary>
    public const int MitgliederrollenMax = 16;
    /// <summary>Lieblingsrollen je Mitglied.</summary>
    public const int LieblingsrollenMax = 8;
    /// <summary>
    /// Einträge je Seite von <c>Mitglied LISTE</c> (Konten und Vormerkungen, zusammen bis 1000, weiter mit <c>ab</c>):
    /// 250 zu höchstens 1,4 KB bleiben mit dem Faktor 2 unter der Rahmengrenze (4.10).
    /// </summary>
    public const int MitgliederListeMax = 250;
    /// <summary>Zeilen in <c>Mitglied EINFUEGEN</c>.</summary>
    public const int EinfuegenZeilenMax = 200;
    /// <summary>Länge einer Zeile in <c>Mitglied EINFUEGEN</c>.</summary>
    public const int EinfuegenZeileLaenge = 200;
    /// <summary>Untergilden (0x001D, seit 0.11.8 entfallen; Grenze bleibt für die Hülle).</summary>
    public const int UntergildenMax = 32;
    /// <summary>Mitglieder je Untergilde (Konten und Vormerkungen).</summary>
    public const int UntergildeMitgliederMax = 40;
    /// <summary>Einträge je Journal-Abruf.</summary>
    public const int JournalMax = 200;
    /// <summary>Zeichen neben einem Namen (Kennungen, A49).</summary>
    public const int ZeichenMax = 4;
    /// <summary>Ränge je Person: einer je Laufbahn (Marine, Navy, Orga), seit 0.11.7.</summary>
    public const int RaengeMax = 3;
    /// <summary>Tätigkeiten je Person, seit 0.11.7.</summary>
    public const int TaetigkeitenMax = 6;
    /// <summary>Abzeichen je Person (Ränge und Tätigkeiten), seit 0.11.7.</summary>
    public const int AbzeichenMax = RaengeMax + TaetigkeitenMax;
    /// <summary>Kennung eines Abzeichens oder Zeichens ("MARINE_04", "T_GUNNER_M", "ADMIN").</summary>
    public const int AbzeichenKennungMax = 16;
    /// <summary>
    /// Ausgeblendete Kennungen in <c>ProfilSetzen.abzeichenAus</c>: der ganze Katalog (50) und die beiden automatischen Zeichen
    /// passen hinein, mit Luft für einen größeren Katalog.
    /// </summary>
    public const int AbzeichenAusMax = 64;
    /// <summary>Notfallcodes je Admin.</summary>
    public const int NotfallcodesMax = 10;
    /// <summary>
    /// otpauth-URI in <c>AdminEinrichtung</c> (3.9): der feste Teil hat 124 Zeichen, dazu der prozentkodierte Name mit
    /// höchstens 32 Skalarwerten zu je 4 UTF-8-Byte (12 Zeichen), zusammen höchstens 508.
    /// </summary>
    public const int OtpauthUriMax = 512;

    /// <summary>Laufende oder geplante Aufträge gleichzeitig (6.12 Regel 4).</summary>
    public const int AuftraegeMax = 4;
    /// <summary>Anlass eines Auftrags (<c>Beauftragung.grund</c>).</summary>
    public const int AuftragGrundMax = 48;
    /// <summary>Typ einer Einheit (<c>schiffe[].typ</c>, F5).</summary>
    public const int EinheitTypMax = 32;
    /// <summary>Operationen je Verwaltungspaket (N5).</summary>
    public const int PaketSchritteMax = 200;
    /// <summary>Plätze in <c>Belegung</c>: 64 Einheiten zu 16 Plätzen und 64 Rollen ohne Einheit.</summary>
    public const int BelegungPlaetzeMax = SchiffeMax * PlaetzeJeSchiffMax + RollenMax;

    /// <summary>Höchste Phasennummer (6.14): 1 ist die Grundstellung, dazu höchstens 4 Phasen.</summary>
    public const int PhaseNrMax = 5;
    /// <summary>Phasen über der Grundstellung (6.14).</summary>
    public const int PhasenMax = 4;
    /// <summary>Name einer Phase ("Entern").</summary>
    public const int PhaseNameMax = 24;
    /// <summary>Kurzname einer Phase für die Meldezeile.</summary>
    public const int PhaseKurzMax = 12;
    /// <summary>Variantenfelder über alle Phasen zusammen (geschätzt rund 50 KB NetzListe im ungünstigsten Fall, 4.10).</summary>
    public const int PhasenFelderMax = 512;

    /// <summary>Art einer Meldung (<c>MeldungSenden.art</c>, <c>Meldestand.anlass</c>), seit 0.12.0.</summary>
    public const int MeldungsArtMax = 24;
    /// <summary>Kennung eines Meldungsmotivs ("einsatz_noch_nicht_gestartet" hat 28), seit 0.12.0.</summary>
    public const int MotivKennungMax = 32;
    /// <summary>Freiwillige Zeile unter der Grafik (Sarevoc: bis 60 Zeichen), seit 0.12.0.</summary>
    public const int MeldungZeileMax = 60;
    /// <summary>Größe einer Meldung: 1 normal, 2 doppelt, 3 dreifach (M21), seit 0.12.0.</summary>
    public const int MeldungGroesseMax = 3;
    /// <summary>Form eines Countdowns ("Startzeit", "Restzeit"), seit 0.12.0.</summary>
    public const int CountdownFormMax = 16;
    /// <summary>So weit darf das Ziel eines Countdowns höchstens voraus liegen (24 h), seit 0.12.0.</summary>
    public const long CountdownVorlaufMaxMs = 24L * 60 * 60 * 1000;
    /// <summary>Text einer Textmeldung (M24, Sarevoc: bis 120 Zeichen), seit 0.12.0.</summary>
    public const int MeldungTextMax = 120;
    /// <summary>Farbe und Dauer einer Textmeldung als Kennung ("orange", "bleibt"), seit 0.12.0.</summary>
    public const int TextKennungMax = 16;
}

/// <summary>Größte zulässige Werte des Längenfelds (8 + Body) je Zustand (4.2).</summary>
public static class Rahmengrenzen
{
    /// <summary>Vor <c>Hello</c> (TLS, WARTE_HELLO).</summary>
    public const int VorHello = 4096;
    /// <summary>REGISTRIERUNG und WARTERAUM: Body höchstens 1024.</summary>
    public const int RegistrierungUndWarteraum = 1032;
    /// <summary>AKTIV_SYNC und AKTIV: 1 MiB.</summary>
    public const int Aktiv = 1_048_576;

    /// <summary>
    /// Grenze für Rahmen, die der Server im Zustand <paramref name="zustand"/> vom Client annimmt.
    /// In ENDE wird nichts mehr gelesen; die kleinste Grenze gilt.
    /// </summary>
    public static int ServerEmpfang(ServerZustand zustand) => zustand switch
    {
        ServerZustand.Registrierung or ServerZustand.Warteraum => RegistrierungUndWarteraum,
        ServerZustand.AktivSync or ServerZustand.Aktiv => Aktiv,
        _ => VorHello,
    };

    /// <summary>
    /// Grenze für Rahmen, die der Client vom Server annimmt: immer 1 MiB. 4.2 staffelt nach Zustand, das passt
    /// aber nur zur Empfangsrichtung des Servers: <c>Welcome{FREIGEGEBEN}</c> mit Profil kommt schon vor AKTIV_SYNC
    /// und kann 1032 Byte überschreiten (Bericht, Abweichungen).
    /// </summary>
    public const int ClientEmpfang = Aktiv;
}

/// <summary>Fristen aus 4.2, 4.6 und 4.8 in Millisekunden der monotonen Uhr.</summary>
public static class Fristen
{
    /// <summary>TLS-Handshake.</summary>
    public const long TlsMs = 5_000;
    /// <summary>Hello nach dem Handshake.</summary>
    public const long HelloMs = 3_000;
    /// <summary>REGISTRIERUNG, gerechnet ab Hello.</summary>
    public const long RegistrierungMs = 5 * 60_000;
    /// <summary>WARTERAUM, gerechnet ab Eintritt.</summary>
    public const long WarteraumMs = 15 * 60_000;
    /// <summary>AKTIV_SYNC bis zur Antwort auf VoiceKeys.</summary>
    public const long SyncMs = 10_000;
    /// <summary>Server trennt nach so langer Zeit ohne jeden Rahmen vom Client.</summary>
    public const long RuheMs = 10_000;
    /// <summary>Server-Anfrage VoiceKeys.</summary>
    public const long VoiceKeysMs = 10_000;
    /// <summary>Server-Anfrage VerknuepfungBestaetigen; Ablauf gilt als "nein".</summary>
    public const long VerknuepfungMs = 120_000;
    /// <summary>Client sendet Ping in diesem Abstand ab Hello.</summary>
    public const long PingAbstandMs = 2_000;
    /// <summary>Client hält die Verbindung für tot, wenn so lange jede Ping-Antwort ausbleibt.</summary>
    public const long PingTotMs = 6_000;
    /// <summary>Client schaltet den Tunnel ein, wenn so lange nach Eintritt in AKTIV/UDP_BINDEN keine Bindung steht.</summary>
    public const long BindungMs = 3_000;
    /// <summary>Saubere UDP-Probe vor der Rückkehr aus dem Tunnel.</summary>
    public const long TunnelProbeMs = 10_000;
    /// <summary><c>Bye{WARTEZEIT, wiederInMs}</c> nach Ablauf des WARTERAUMS.</summary>
    public const uint WartezeitWiederInMs = 60_000;
    /// <summary>Meldesystem (seit 0.12.0, M19): je Sender höchstens eine Meldung in dieser Zeit (Ratengruppe Meldung).</summary>
    public const long MeldungAbstandMs = 2_000;
    /// <summary>Meldesystem (seit 0.12.0): Eine Bereit-Abfrage endet spätestens nach dieser Zeit von selbst.</summary>
    public const long BereitAbfrageMs = 120_000;
}
