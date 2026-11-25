// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Serialization;

namespace Funk.Protokoll.Steuerung;

/// <summary>Fehlercodes des Steuerkanals nach 4.9. Drahtform: Name in GROSS_MIT_UNTERSTRICH.</summary>
[JsonConverter(typeof(StrengeAufzaehlung<Fehlercode>))]
public enum Fehlercode
{
    /// <summary>Rahmen, JSON oder Zustand verletzt.</summary>
    Protokoll,
    /// <summary>Haupt- oder Nebenversion unvereinbar.</summary>
    Version,
    /// <summary>Client unter der Mindestversion.</summary>
    VersionZuAlt,
    /// <summary>Grenze einer Rahmen- oder Feldlänge verletzt.</summary>
    ZuGross,
    /// <summary>Inhalt verletzt eine Regel.</summary>
    Ungueltig,
    /// <summary>ID gibt es nicht oder sie gehört nicht dem Anfragenden.</summary>
    Unbekannt,
    /// <summary>Fehlendes Recht oder Rang.</summary>
    KeinRecht,
    /// <summary>Version oder basisVersion stimmt nicht.</summary>
    Veraltet,
    /// <summary>Ratenlimit.</summary>
    Grenze,
    /// <summary>Zustandsmeldung durch eine neuere ersetzt, kein Verstoß.</summary>
    Ueberholt,
    /// <summary>Einmalcode falsch, abgelaufen oder verbraucht; nur an den Bot.</summary>
    CodeUngueltig,
    /// <summary>Mehr als 3 aktive Geräte.</summary>
    GeraeteLimit,
    /// <summary>Privilegiertes Konto, nur über das Admin-Werkzeug.</summary>
    NurAdminWerkzeug,
    /// <summary>Kapazität erreicht.</summary>
    PlatzBelegt,
    /// <summary>Selbstwahl ist gerade gesperrt.</summary>
    SelbstwahlGesperrt,
    /// <summary>Festes Feld, nur der Raidleiter ändert es.</summary>
    FeldFest,
    /// <summary>Global lässt sich nicht abschalten.</summary>
    GlobalPflicht,
    /// <summary>Gerät ENTZOGEN oder ABGELEHNT.</summary>
    GeraetGesperrt,
    /// <summary>Konto GESPERRT.</summary>
    KontoGesperrt,
    /// <summary>Neue Sitzung desselben Kontos von einem anderen Gerät.</summary>
    Verdraengt,
    /// <summary>Zu häufiges Verdrängen, Admin entscheidet.</summary>
    VerdraengenGesperrt,
    /// <summary>Keine neuen Registrierungen und unfreigegebenen Verbindungen.</summary>
    ServerVoll,
    /// <summary>Raid-Helper nicht erreichbar, kein Server-Key oder Antwort ungültig.</summary>
    Extern,
    /// <summary>Serverfehler.</summary>
    Intern,
    /// <summary>SC-Name gehört schon einem Konto oder einer Vormerkung, mit <c>details.name</c>.</summary>
    NameVergeben,
    /// <summary>Das Recht wirkt nur in einer Admin-Sitzung (3.9).</summary>
    AdminSitzungNoetig,
    /// <summary>Heikle Handlung: erst einen frischen Code bestätigen, dann erneut senden (<c>details.bis</c>).</summary>
    SchrittBestaetigen,
    /// <summary>Code aus der Authenticator-App falsch oder schon benutzt.</summary>
    ZweitfaktorFalsch,
    /// <summary>Zu viele Fehlversuche: Pause bis <c>details.bis</c>, ohne <c>bis</c> gesperrt bis zur Konsole.</summary>
    ZweitfaktorGesperrt,
    /// <summary>Höchstens 30 offene Anträge; ein Admin muss aufräumen.</summary>
    AntraegeVoll,
}

/// <summary>Eigenschaften der Fehlercodes aus 4.9.</summary>
public static class Fehlercodes
{
    /// <summary>
    /// Ob der Code nach 4.9 die Verbindung trennt ("schwerer Code"). Ausnahmen der Tabelle: <c>KONTO_GESPERRT</c>
    /// als Push in REGISTRIERUNG trennt nicht (siehe <see cref="TrenntAlsPushInRegistrierung"/>), <c>GRENZE</c>
    /// trennt nur über die Verstoßzählung aus 4.8, <c>CODE_UNGUELTIG</c> geht nur an den Bot.
    /// </summary>
    public static bool Trennt(Fehlercode code) => code is Fehlercode.Protokoll or Fehlercode.Version
        or Fehlercode.VersionZuAlt or Fehlercode.ZuGross or Fehlercode.GeraetGesperrt or Fehlercode.KontoGesperrt
        or Fehlercode.Verdraengt or Fehlercode.VerdraengenGesperrt or Fehlercode.ServerVoll;

    /// <summary>
    /// 3.4 Schritt 4: <c>NUR_ADMIN_WERKZEUG</c>, <c>KONTO_GESPERRT</c> und <c>GERAETE_LIMIT</c> gehen nach einer
    /// gescheiterten Verknüpfung als Push ohne Trennung an die Verbindung in REGISTRIERUNG.
    /// </summary>
    public static bool TrenntAlsPushInRegistrierung(Fehlercode code) =>
        code is not (Fehlercode.KontoGesperrt or Fehlercode.NurAdminWerkzeug or Fehlercode.GeraeteLimit) && Trennt(code);

    /// <summary>Drahtform des Codes, etwa <c>KEIN_RECHT</c>.</summary>
    public static string Drahtname(Fehlercode code) => StrengeAufzaehlung<Fehlercode>.Drahtname(code);
}
