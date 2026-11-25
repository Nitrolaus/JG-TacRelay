// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Ein Satz für Funker und getrennt davon die technische <see cref="Einzelheit"/> (Adresse:Port, Meldung von Windows, Pfad,
/// Ausnahmetext), <c>null</c> ohne. Der Satz nennt nie Adresse, Port oder Pfad und endet bei Fehlern mit der Fehlernummer
/// (<see cref="Fehlernummern"/>); die Einzelheit ist nur für "Details" im Admin-Setup. Als Text ist die Meldung immer nur der
/// Satz (<see cref="ToString"/>): Gerät sie versehentlich in einen Text, kommt die Einzelheit nicht mit.
/// </summary>
public sealed record Verbindungsmeldung(string Text, string? Einzelheit)
{
    public override string ToString() => Text;
}

/// <summary>
/// Verbinden lässt sich nicht vorbereiten (Serverprofil, Verbindungseinstellungen, Geräteschlüssel). <see cref="Exception.Message"/>
/// ist der Satz für Funker mit Fehlernummer; die technische Einzelheit steht nur in <see cref="Meldung"/>.
/// </summary>
public sealed class VorbereitungException : InvalidOperationException
{
    public VorbereitungException()
    {
        Meldung = new(Message, null);
    }

    public VorbereitungException(string meldung) : base(meldung) => Meldung = new(Message, null);

    public VorbereitungException(string meldung, Exception innen) : base(meldung, innen) => Meldung = new(Message, null);

    public VorbereitungException(Verbindungsmeldung meldung, Exception? innen = null) : base(meldung?.Text, innen) => Meldung = meldung ?? new(Message, null);

    /// <summary>Satz und Einzelheit, wie sie in die Lage gehen (<see cref="FunkLage.Hinweis"/>, <see cref="FunkLage.HinweisEinzelheit"/>).</summary>
    public Verbindungsmeldung Meldung { get; }
}

/// <summary>
/// Übersetzt das Ende einer Sitzung oder eines Aufbauversuchs in einen Satz für die Anzeige: was passiert ist und was der Funker
/// tun kann, meist warten, weil der Client selbst weiter versucht (7.1: "Der Client zeigt den Zustand deutlich an"), sonst dem
/// Admin die Fehlernummer sagen. Einfache Sprache, per du, ohne Adresse, Port, Pfad und Admin-Themen (Sarevoc, Grilling Q5); die
/// Einzelheiten aus dem Aufbau (Adresse, Port, Meldung von Windows) und der Grund des Clients gehen als Einzelheit mit.
/// </summary>
public static class Verbindungstexte
{
    /// <summary>Steht vor der Fehlernummer, wenn die Verbindungsschleife von selbst neu versucht.</summary>
    public const string NeuerVersuch = "Neuer Versuch läuft von selbst.";

    private const string Nummer = "sag deinem Admin die Fehlernummer";

    public static Verbindungsmeldung Ende(Sitzungsende ende)
    {
        ArgumentNullException.ThrowIfNull(ende);
        if (ende.Aufbau is { } art) return Aufbau(art, ende.Einzelheit, ende.Wiederverbindung);
        if (ende.Bye is { } bye && ende.Grund != "Beendet") return Bye(bye, ende);
        if (ende.Fehler is { } f) return Fehler(f, ende.Grund);
        return ende.Grund switch
        {
            "Beendet" or "Nutzer verbindet neu" => new("Getrennt.", null),
            "6 s ohne Ping-Antwort" => Mit("Verbindung verloren: 6 s lang keine Antwort vom Server. " + NeuerVersuch, Fehlernummern.KeineAntwort, ende.Grund),
            "Verbindungsverlust" or "Verbindung geschlossen" or "Geschlossen" => Mit("Verbindung verloren. " + NeuerVersuch, Fehlernummern.VerbindungVerloren, ende.Grund),
            _ => Mit("Verbindung getrennt. " + NeuerVersuch, Fehlernummern.VerbindungGetrennt, ende.Grund),
        };
    }

    /// <summary>
    /// Aufbau gescheitert, bevor eine Sitzung lief; <paramref name="einzelheit"/> (Adresse, Port, Meldung von Windows) geht
    /// unverändert als Einzelheit mit. <paramref name="wiederverbindung"/> zählt nur beim Pin-Alarm: Bei der Erstverbindung bricht
    /// der Client ab (4.1), beim Wiederverbinden (7.1 Schritt 4, 2 min ohne gültigen Pin) versucht die Schleife weiter, und das
    /// muss dastehen.
    /// </summary>
    public static Verbindungsmeldung Aufbau(AufbauFehler art, string? einzelheit, bool wiederverbindung = false) => art switch
    {
        AufbauFehler.PinAlarm when wiederverbindung => Mit("Der Server zeigt gerade einen unbekannten Schlüssel, der nicht zum Serverprofil passt; "
            + "dem vertraut der Client nicht. Bleibt das so, " + Nummer + ", vermutlich brauchst du ein neues Setup. " + NeuerVersuch,
            Fehlernummern.UnbekannterSchluessel, einzelheit),
        AufbauFehler.Netz => Mit("Server nicht erreichbar. Prüfe deine Internetverbindung; hält es an, " + Nummer + ". " + NeuerVersuch,
            Fehlernummern.ServerNichtErreichbar, einzelheit),
        AufbauFehler.Tls => Mit("Der sichere Verbindungsaufbau ist gescheitert. Hält es an, " + Nummer + ". " + NeuerVersuch, Fehlernummern.TlsGescheitert, einzelheit),
        AufbauFehler.Alpn => Mit("Der Server antwortet, ist aber kein JG-TacRelay-Server. Hält es an, " + Nummer + ". " + NeuerVersuch,
            Fehlernummern.KeinFunkServer, einzelheit),
        AufbauFehler.Pin => Mit("Es antwortet ein fremder Server, sein Schlüssel passt nicht zum Serverprofil. Der Client lässt ihn 60 s lang aus. "
            + NeuerVersuch, Fehlernummern.FremderServer, einzelheit),
        _ => Mit("Server nicht vertrauenswürdig: Sein Schlüssel passt nicht zum Serverprofil. Ohne passendes Profil verbindet der Client nicht. "
            + "Sag deinem Admin die Fehlernummer, vermutlich brauchst du ein neues Setup.", Fehlernummern.NichtVertrauenswuerdig, einzelheit),
    };

    /// <summary>Eine Verbindungseinstellung (die Adresse, die ein Admin einträgt) ist ungültig; <paramref name="einzelheit"/> sagt, was und warum.</summary>
    public static Verbindungsmeldung EinstellungUngueltig(string einzelheit) =>
        Mit("Eine Verbindungseinstellung ist ungültig, deshalb verbindet der Client nicht. Sag deinem Admin die Fehlernummer.",
            Fehlernummern.EinstellungUngueltig, einzelheit);

    /// <summary>Der Geräteschlüssel lässt sich nicht öffnen oder anlegen; <paramref name="einzelheit"/> ist die Meldung von Windows.</summary>
    public static Verbindungsmeldung Geraeteschluessel(string einzelheit) =>
        Mit("Der Geräteschlüssel lässt sich nicht öffnen oder anlegen. Starte JG TacRelay neu; hält es an, " + Nummer + ".",
            Fehlernummern.GeraeteschluesselFehlt, einzelheit);

    /// <summary>
    /// Was beim Bauen des Clients vor dem Verbinden scheiterte: eine <see cref="VorbereitungException"/> bringt ihre Meldung mit,
    /// jede andere Ausnahme ist ein interner Fehler mit ihrem Text als Einzelheit.
    /// </summary>
    public static Verbindungsmeldung Vorbereitung(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex is VorbereitungException v
            ? v.Meldung
            : Mit("Interner Fehler vor dem Verbinden. Starte JG TacRelay neu; hält es an, " + Nummer + ".", Fehlernummern.InternVorbereitung, ex.Message);
    }

    private static Verbindungsmeldung Bye(ByeGrund bye, Sitzungsende ende) => bye switch
    {
        ByeGrund.Wartung => new("Der Server wird gewartet. Neuer Versuch in etwa "
            + ((ende.WiederInMs ?? Protokoll.Steuerung.Fristen.WartezeitWiederInMs) / 1000).ToString(CultureInfo.CurrentCulture) + " s, von selbst.", null),
        // Name und Kennung des anderen Geräts nur als Einzelheit: Der Windows-Gerätename steht für Funker nur in der Antragskarte (Q4).
        ByeGrund.Verdraengt => new("Dein Konto hat sich auf einem anderen Gerät angemeldet. Verbinden holt die Verbindung hierher zurück.",
            ende.Durch is { } d ? "Anderes Gerät: " + d.GeraetName + ", " + d.Anzeige : null),
        ByeGrund.Ersetzt => new("Diese Verbindung wurde durch eine neuere desselben Geräts ersetzt.", null),
        ByeGrund.Gesperrt => Mit("Das Konto ist gesperrt. Wende dich an einen Admin.", Fehlernummern.KontoGesperrt, ende.Grund),
        ByeGrund.Entzogen => Mit("Dieses Gerät ist gesperrt. Wende dich an einen Admin.", Fehlernummern.GeraetGesperrt, ende.Grund),
        // Seit 0.7.0 löscht Ablehnen den Antrag, und der Server meldet das wartende Gerät mit Bye{ENTFERNT} ab, ebenso wenn der
        // Antrag nach 24 Stunden verfällt. Ohne AKTIV-Zeit war es also ein Antrag, kein freigegebenes Gerät.
        ByeGrund.Entfernt when ende.AktivDauerMs is null => Mit("Dein Antrag ist nicht mehr offen: Ein Admin hat ihn abgelehnt, oder er ist "
            + "nach 24 Stunden verfallen. Willst du dabei sein, sprich mit einem Admin und klick dann auf Verbinden; dann stellst du einen neuen Antrag.",
            Fehlernummern.GeraetEntfernt, ende.Grund),
        // Seit 0.6.3 verbindet der Client danach nicht mehr von selbst (ClientAutomat.ByeEmpfangen); der Satz bleibt also stehen.
        ByeGrund.Entfernt => Mit("Dieses Gerät wurde vom Funk abgemeldet (entfernt). Wenn du wieder dabei sein willst, klick auf Verbinden; dann stellst du einen neuen Antrag.",
            Fehlernummern.GeraetEntfernt, ende.Grund),
        ByeGrund.Wartezeit => Mit("Der Server nimmt gerade keine weiteren wartenden Geräte an. Neuer Versuch später, von selbst.", Fehlernummern.ServerVoll, ende.Grund),
        ByeGrund.Protokoll => Mit("Der Server hat die Verbindung wegen eines Protokollfehlers getrennt. " + NeuerVersuch, Fehlernummern.Protokollfehler, ende.Grund),
        _ => Mit("Der Server hat die Verbindung beendet. " + NeuerVersuch, Fehlernummern.ServerBeendet, ende.Grund),
    };

    /// <summary>Ein Fehlercode des Servers oder einer, den der Client selbst erkannte; die Einzelheit ist der Grund, mit dem Drahtnamen.</summary>
    private static Verbindungsmeldung Fehler(Fehlercode f, string grund)
    {
        var name = Fehlercodes.Drahtname(f);
        var einzelheit = grund.Contains(name, StringComparison.Ordinal) ? grund : grund + " (" + name + ")";
        return f switch
        {
            Fehlercode.Verdraengt => new("Dein Konto hat sich auf einem anderen Gerät angemeldet. Verbinden holt die Verbindung hierher zurück.", null),
            Fehlercode.VersionZuAlt => Mit("Diese Version von JG TacRelay ist zu alt für den Server. Bitte aktualisieren.", Fehlernummern.VersionZuAlt, einzelheit),
            Fehlercode.Version => Mit("Client und Server passen in der Version nicht zusammen. Bitte aktualisieren.", Fehlernummern.VersionPasstNicht, einzelheit),
            Fehlercode.KontoGesperrt => Mit("Das Konto ist gesperrt. Wende dich an einen Admin.", Fehlernummern.KontoGesperrt, einzelheit),
            // Seit 0.7.0 sperrt nur ein Admin bewusst; Ablehnen löscht den Antrag nur (Bye{ENTFERNT}, oben).
            Fehlercode.GeraetGesperrt => Mit("Dieses Gerät ist gesperrt. Wende dich an einen Admin.", Fehlernummern.GeraetGesperrt, einzelheit),
            Fehlercode.VerdraengenGesperrt => Mit("Zu oft zwischen Geräten gewechselt. Ein Admin muss das freigeben, " + Nummer + ".",
                Fehlernummern.WechselGesperrt, einzelheit),
            Fehlercode.ServerVoll => Mit("Der Server nimmt gerade keine neuen Geräte an. " + NeuerVersuch, Fehlernummern.ServerVoll, einzelheit),
            Fehlercode.Protokoll => Mit("Die Verbindung wurde wegen eines Protokollfehlers getrennt. " + NeuerVersuch, Fehlernummern.Protokollfehler, einzelheit),
            _ => Mit("Die Verbindung wurde wegen eines Fehlers getrennt. " + NeuerVersuch, Fehlernummern.Abgelehnt, einzelheit),
        };
    }

    private static Verbindungsmeldung Mit(string satz, string nummer, string? einzelheit) =>
        new(Fehlernummern.Anhaengen(satz, nummer), string.IsNullOrWhiteSpace(einzelheit) ? null : einzelheit);
}
