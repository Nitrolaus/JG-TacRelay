// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;

namespace Funk.Tests.Oberflaeche;

/// <summary>Eine Fehlernummer und was sie bedeutet, zum Nachschlagen für den Admin.</summary>
internal sealed record Fehlernummer(string Nummer, string Bedeutung);

/// <summary>
/// Die Tabelle der Fehlernummern des Clients mit ihren Bedeutungen für Admins (Sarevoc, Grilling Q5). Bis Schritt 3.5 stand sie im Client
/// (<c>Fehlernummern.Alle</c>), wurde dort aber nie benutzt, sondern nur von diesen Tests gelesen (Gegenprüfung Stufe 2, N6). Jede Nummer
/// steht genau einmal da; FehlertexteTests gleicht sie mit den Nummern ab, die der Client wirklich vergibt.
/// </summary>
internal static class FehlernummernTabelle
{
    /// <summary>Alle Nummern mit Bedeutung, aufsteigend. Die Bedeutung ist für Admins und darf Technisches nennen.</summary>
    public static IReadOnlyList<Fehlernummer> Alle { get; } =
    [
        new(Fehlernummern.ProfilFehlt, "Die Fassung bringt kein Serverprofil mit (Setup ohne profil.json); nur Probefunk."),
        new(Fehlernummern.ProfildateiFehlt, "Die Profildatei im Programmordner fehlt; Einzelheit: der Pfad. Abhilfe: Setup reparieren."),
        new(Fehlernummern.ProfilZuGross, "Die Profildatei ist größer als 64 KiB, also kein Serverprofil; Einzelheit: Pfad und Größe."),
        new(Fehlernummern.ProfilUnlesbar, "Die Profildatei lässt sich nicht lesen (gesperrt, keine Rechte); Einzelheit: Meldung von Windows."),
        new(Fehlernummern.ProfilUnsigniert, "Unsignierte Profildaten (daten.json) statt profil.json von funk-admin profil-signieren; Einzelheit: der Pfad."),
        new(Fehlernummern.ProfilKeinProfil, "Die Profildatei ist kein signiertes Serverprofil (profil.json von funk-admin profil-signieren); Einzelheit: der Pfad."),
        new(Fehlernummern.ProfilschluesselFehlt, "In die Fassung ist kein gültiger Profilschlüssel eingebaut; nur Probefunk."),
        new(Fehlernummern.ProfilInhaltUngueltig, "Signatur stimmt, Inhalt des Profils ungültig; mit funk-admin profil-schreiben neu schreiben. Einzelheit: der Pfad."),
        new(Fehlernummern.ProfilFremd, "Die Signatur passt zu keinem eingebauten Profilschlüssel: Profil und Programm gehören nicht zusammen. Einzelheit: der Pfad."),
        new(Fehlernummern.ServerNichtErreichbar, "Namensauflösung oder TCP gescheitert: Internet, DynDNS-Name, Portfreigabe im Router. Einzelheit: Adresse und Meldung von Windows."),
        new(Fehlernummern.TlsGescheitert, "TLS-Handshake gescheitert (auch: Geräteschlüssel für TLS unbrauchbar). Einzelheit: Adresse:Port und Grund."),
        new(Fehlernummern.KeinFunkServer, "ALPN nicht vereinbart: Dort antwortet kein JG-TacRelay-Server; Port im Serverprofil oder in der Adresse und Portfreigabe prüfen."),
        new(Fehlernummern.FremderServer, "Pin passt nicht an dieser Adresse (beim Wiederverbinden zuerst falscher Host); der Client meidet sie 60 s."),
        new(Fehlernummern.NichtVertrauenswuerdig, "Erstverbindung: Der Serverschlüssel passt zu keinem Pin im Serverprofil; kein Neuversuch. Neuer Serverschlüssel braucht ein neues Profil."),
        new(Fehlernummern.UnbekannterSchluessel, "Wiederverbinden: 2 min lang an keiner Adresse ein gültiger Pin; der Client versucht weiter."),
        new(Fehlernummern.EinstellungUngueltig, "Die Adresse in den Verbindungseinstellungen ist ungültig; Einzelheit: Eingabe und Grund."),
        new(Fehlernummern.GeraeteschluesselFehlt, "Der Geräteschlüssel im Schlüsselspeicher von Windows lässt sich nicht öffnen oder anlegen; Einzelheit: Meldung von Windows."),
        new(Fehlernummern.KeineAntwort, "6 s lang keine Ping-Antwort vom Server; der Client verbindet neu."),
        new(Fehlernummern.VerbindungVerloren, "Die Verbindung brach ab oder der Server schloss sie ohne Bye; der Client verbindet neu."),
        new(Fehlernummern.VerbindungGetrennt, "Der Client trennte aus einem anderen Grund (etwa Schreibfehler, interner Fehler, neue Sitzung); Einzelheit: der Grund."),
        new(Fehlernummern.ServerBeendet, "Der Server beendete die Sitzung mit Bye BEENDET; der Client verbindet neu."),
        new(Fehlernummern.VersionZuAlt, "Fehlercode VERSION_ZU_ALT: Die Fassung des Clients liegt unter der Mindestversion des Servers."),
        new(Fehlernummern.VersionPasstNicht, "Fehlercode VERSION: Haupt- oder Nebenversion von Client und Server unvereinbar."),
        new(Fehlernummern.KontoGesperrt, "Fehlercode KONTO_GESPERRT oder Bye GESPERRT: Das Konto ist gesperrt."),
        new(Fehlernummern.GeraetGesperrt, "Fehlercode GERAET_GESPERRT oder Bye ENTZOGEN: Ein Admin hat das Gerät bewusst gesperrt (seit 0.7.0 die einzige Sperre; bis 0.6.x auch nach dem Ablehnen)."),
        new(Fehlernummern.GeraetEntfernt, "Bye ENTFERNT: Das Gerät wurde entfernt, sein Konto gelöscht oder (seit 0.7.0) sein Antrag abgelehnt; kein automatischer Neuversuch (seit 0.6.3), erst Verbinden stellt einen neuen Antrag."),
        new(Fehlernummern.WechselGesperrt, "Fehlercode VERDRAENGEN_GESPERRT: zu oft zwischen Geräten gewechselt; Freigabe mit funk-admin verdraengung-freigeben."),
        new(Fehlernummern.ServerVoll, "Fehlercode SERVER_VOLL oder Bye WARTEZEIT: keine neuen oder wartenden Geräte mehr."),
        new(Fehlernummern.Protokollfehler, "Fehlercode PROTOKOLL oder Bye PROTOKOLL: Rahmen, JSON oder Zustand verletzt, auf einer der beiden Seiten; Einzelheit: der Grund."),
        new(Fehlernummern.Abgelehnt, "Ein anderer Fehlercode trennte die Verbindung; Einzelheit: Grund und Drahtname des Codes."),
        new(Fehlernummern.InternVerbinden, "Unerwartete Ausnahme im Client beim Verbinden; Einzelheit: Ort und Ausnahmetext."),
        new(Fehlernummern.InternAnmeldung, "Unerwartete Ausnahme im Client bei Anmeldung, Warteraum oder Abgleich; Einzelheit: Ort und Ausnahmetext."),
        new(Fehlernummern.InternSitzung, "Unerwartete Ausnahme im Client in der laufenden Sitzung; Einzelheit: Ort und Ausnahmetext."),
        new(Fehlernummern.InternVorbereitung, "Unerwartete Ausnahme beim Bauen des Clients vor dem Verbinden (auch ein ungültiger Testzugang); Einzelheit: Ausnahmetext."),
    ];
}
