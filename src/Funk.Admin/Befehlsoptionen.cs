// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Admin;

/// <summary>
/// Welche Optionen jeder Befehl von funk-admin kennt. <see cref="Pruefen"/> lehnt alles andere ab, bevor eine Datei
/// entsteht oder der Server etwas hört (Exit 2): eine unbekannte oder doppelte Option, einen Wert ohne Option, eine
/// Option ohne Wert. Bis 01.10.2026 nahm funk-admin jede Option still an; ein vertipptes <c>scnamen --name</c> statt
/// <c>--namen</c> löschte so alle SC-Namen eines Kontos, mit Exit 0 (Gegenprüfung 01.10.2026).
/// Eine neue Option gehört hierher und an ihre Stelle in <see cref="Uebersetzung"/> oder in die Offline-Befehle in
/// Program.cs; AdminWerkzeugTests gleicht die Tabelle mit beiden und mit den Anleitungen ab.
/// </summary>
public static class Befehlsoptionen
{
    /// <summary>Optionen ohne Wert. Erlaubt sind sie nur bei den Befehlen, die sie nennen.</summary>
    public static IReadOnlySet<string> Schalter { get; } =
        Menge("standard", "taste", "nur-taste", "va", "selbst", "braucht-schiff", "wartend", "json-aus", "anzeigen", "leeren", "alle",
            "ersetzen", "vollstaendig", "ohne-totp", "mit-serverschluessel");

    /// <summary>Wo der Server zu finden ist; jeder Befehl nimmt sie an, auch die offline (dort ohne Wirkung).</summary>
    public static IReadOnlySet<string> Ueberall { get; } = Menge("konfig", "socket");

    /// <summary>Dazu bei jedem Befehl über den Server: die Antwort als JSON.</summary>
    public static IReadOnlySet<string> UeberDenServer { get; } = Menge("json-aus");

    /// <summary>Optionen, die mehrfach stehen dürfen; alle anderen höchstens einmal.</summary>
    public static IReadOnlySet<string> Mehrfach { get; } = Menge("host", "pin", "nameserver", "spki", "feld", "binden");

    /// <summary>Ein Befehl: offline oder über den Server, mit einem Gerät als Wert ohne Option, und seine Optionen.</summary>
    public sealed record Regel(bool Offline, bool MitGeraet, IReadOnlyList<string> Optionen)
    {
        public bool Kennt(string name) =>
            Optionen.Contains(name, StringComparer.Ordinal) || Ueberall.Contains(name) || (!Offline && UeberDenServer.Contains(name));
    }

    /// <summary>Alle Befehle außer <c>hilfe</c>.</summary>
    public static IReadOnlyDictionary<string, Regel> Befehle { get; } = Aufbauen();

    private static Dictionary<string, Regel> Aufbauen()
    {
        var b = new Dictionary<string, Regel>(StringComparer.Ordinal);
        void Offline(string befehl, params string[] optionen) => b.Add(befehl, new Regel(true, false, optionen));
        void Server(string befehle, params string[] optionen)
        {
            foreach (var befehl in befehle.Split(' '))
                b.Add(befehl, new Regel(false, false, optionen));
        }

        // Erzeugen ersetzt eine vorhandene Datei nur mit --ersetzen (A55; bis 01.10.2026 ersetzte es still).
        Offline("schluessel-erzeugen", "zertifikat", "schluessel", "name", "ersetzen");
        Offline("geheimnis-erzeugen", "datei", "ersetzen");
        Offline("totp-schluessel-erzeugen", "datei", "ersetzen");
        Offline("profilschluessel-erzeugen", "schluessel", "ersetzen");
        Offline("profil-schreiben", "name", "version", "host", "tcp-port", "pin", "nameserver", "gueltig-tage", "aus");
        Offline("profil-signieren", "daten", "schluessel", "aus");
        Offline("profil-pruefen", "profil", "spki");
        // Einrichten und Umzug (A55, EINRICHTEN-UMZUG-ENTWURF). "Offline" heißt hier: im Werkzeug selbst ausgeführt (Program.cs);
        // erster-admin redet dabei Schritt für Schritt über den Verwaltungssocket, umzug-* prüfen dort nur, ob der Dienst steht.
        Offline("konfig-schreiben", "aus", "binden", "tcp-port", "oeffentlicher-tcp-port", "udp-port", "oeffentlicher-udp-port", "profil", "zustand");
        Offline("pruefen", "vollstaendig", "credentials", "spki", "host");
        Offline("profil-anfrage", "host", "tcp-port", "zertifikat", "credentials", "aus");
        Offline("profil-erneuern", "bisher", "schluessel", "anfrage", "reserve", "gueltig-tage");
        Offline("umzug-packen", "aus", "credentials", "ohne-totp", "mit-serverschluessel");
        Offline("umzug-einspielen", "paket", "credentials", "mit-serverschluessel", "besitzer");
        Offline("erster-admin");
        Offline("einrichten", "profil", "umzug", "mit-serverschluessel");

        Server("status konten netzliste vorrangspur vorrangspur-leeren messwerte profil-neu-laden admins antraege mitglieder");
        Server("journal", "konto", "anzahl");
        Server("eigentuemer admin-vorsehen admin-sperren admin-entsperren admin-zuruecksetzen notfallcodes", "konto");
        Server("admin-ernennen admin-schluessel-entfernen", "konto", "kennung");
        Server("admin-einrichtung-neu", "konto", "anzeigen");
        Server("zweitfaktor-bestaetigen", "konto", "code");
        Server("admin-entziehen", "konto", "stufe");
        Server("scnamen", "konto", "namen", "leeren");
        Server("discordname", "konto", "name", "leeren");
        Server("sicherung", "ziel");
        // A51: eingebaute Sperrliste.
        Server("sperrliste");
        Server("entsperren", "eintrag", "ip", "alle");
        Server("konto-anlegen", "discord", "benutzername", "name", "stufe");
        Server("verknuepfen", "code", "discord", "benutzername", "name", "stufe");
        Server("konto-sperren konto-entsperren konto-loeschen verdraengung-freigeben", "konto");
        Server("stufe", "konto", "stufe");
        Server("name", "konto", "name");
        Server("rechte", "konto", "rechte", "leeren");
        Server("mitgliedschaft", "discord", "status", "benutzername", "name");
        Server("geraete", "konto", "wartend", "nicht-seit");
        foreach (var befehl in new[] { "freigeben", "ablehnen", "entfernen", "sperren" })
            b.Add(befehl, new Regel(false, true, ["geraet", "grund", "vormerkung", "an-konto"]));
        // Reparatur: ein freigegebenes Gerät an ein anderes Konto (HEIMSERVER-PROXMOX.md, Abschnitt 14).
        b.Add("geraet-umhaengen", new Regel(false, true, ["geraet", "an-konto"]));
        // Seit 0.8.0 das Gegenstück zu "sperren": hebt eine Gerätesperre auf, auch ohne Gerät (Konto später gelöscht).
        b.Add("sperre-aufheben", new Regel(false, true, ["geraet"]));
        // A62 (0.5.0): ohne Schalter Sprachaktivierung, --taste für Taste; --va bleibt und wirkt wie ohne.
        Server("netz-setzen", "id", "name", "offen", "standard", "taste", "nur-taste", "va", "reihenfolge", "json", "kanaele", "kanaele-datei", "gruppe");
        Server("schiff-setzen", "json");
        Server("rolle-setzen", "id", "name", "selbst", "braucht-schiff", "kapazitaet", "feld", "reihenfolge", "json");
        Server("netz-loeschen schiff-loeschen rolle-loeschen", "id");
        Server("einstellung", "selbstwahl", "durchsage-nur-taste", "nachzuegler");
        Server("auftrag", "konto", "von", "bis", "rechte", "grund");
        Server("auftrag-entziehen", "konto");
        Server("auftraege");
        Server("flotte-vorlage", "name");
        // Kanalsperren (0.6.0, 6.17): für eine Person (--konto) oder alle ohne Einsatzleitung (--alle); ohne --ch das ganze Netz.
        Server("kanalsperre-setzen", "konto", "alle", "netz", "ch", "bis");
        Server("kanalsperre-aufheben", "konto", "alle", "netz", "ch");
        Server("kanalsperren");
        return b;
    }

    /// <summary>
    /// <c>null</c>, wenn die Befehlszeile zum Befehl passt; sonst die Meldung für die Konsole. Geschehen ist dann nichts.
    /// </summary>
    public static string? Pruefen(Befehlszeile z)
    {
        ArgumentNullException.ThrowIfNull(z);
        if (z.Befehl is "hilfe" or "--hilfe" or "-h") return null;
        if (!Befehle.TryGetValue(z.Befehl, out var regel)) return "Unbekannter Befehl: " + z.Befehl + ". funk-admin hilfe zeigt alle.";
        const string Nichts = " Nichts geändert.";

        foreach (var (name, _) in z.Genannt)
        {
            if (regel.Kennt(name)) continue;
            var gleich = name.IndexOf('=', StringComparison.Ordinal);
            if (gleich > 0 && regel.Kennt(name[..gleich]))
                return $"--{name}: zwischen Option und Wert gehört ein Leerzeichen, kein =, etwa --{name[..gleich]} {Beispiel(name[(gleich + 1)..])}." + Nichts;
            return $"{z.Befehl} kennt --{name} nicht. {Liste(z.Befehl, regel)}" + Nichts;
        }
        if (z.OhneWert is { } ohne) return $"--{ohne} braucht einen Wert." + Nichts;
        foreach (var g in z.Genannt.GroupBy(g => g.Name, StringComparer.Ordinal))
        {
            if (g.Count() > 1 && !Mehrfach.Contains(g.Key)) return $"--{g.Key} darf nur einmal dastehen, hier steht es {g.Count()}-mal." + Nichts;
        }
        foreach (var (name, wert) in z.Genannt)
        {
            // "--name --leeren": Der Wert fehlt, und die Option hat die nächste Option als Wert genommen.
            if (wert is { Length: > 2 } && wert.StartsWith("--", StringComparison.Ordinal) && regel.Kennt(wert[2..]))
                return $"--{name} braucht einen Wert, dahinter steht aber {wert}." + Nichts;
        }
        var frei = regel.MitGeraet ? 1 : 0;
        if (z.Positional.Count > frei)
        {
            var zuviel = z.Positional[frei];
            var beispiel = regel.Optionen.FirstOrDefault(o => o is "namen" or "name") is { } o2 ? $", etwa --{o2} \"Max Mustermann\"" : "";
            return regel.MitGeraet
                ? $"{z.Befehl} nimmt nur ein Gerät, \"{zuviel}\" ist zu viel." + Nichts
                : $"\"{zuviel}\" gehört zu keiner Option. Werte mit Leerzeichen gehören in Anführungszeichen{beispiel}." + Nichts;
        }
        if (regel.MitGeraet && z.Positional.Count == 1 && z.Wert("geraet") is not null)
            return $"{z.Befehl}: Das Gerät steht zweimal da, vorn und mit --geraet." + Nichts;
        return null;
    }

    private static string Beispiel(string wert) => wert.Contains(' ', StringComparison.Ordinal) ? "\"" + wert + "\"" : wert;

    private static string Liste(string befehl, Regel regel)
    {
        var dazu = regel.Offline ? "" : " (dazu --konfig oder --socket, --json-aus)";
        if (regel.MitGeraet) return $"Erlaubt: das Gerät, {string.Join(", ", regel.Optionen.Select(o => "--" + o))}{dazu}.";
        if (regel.Optionen.Count == 0) return $"{befehl} braucht keine Optionen{dazu}.";
        return $"Erlaubt: {string.Join(", ", regel.Optionen.Select(o => "--" + o))}{dazu}.";
    }

    private static HashSet<string> Menge(params string[] namen) => new(namen, StringComparer.Ordinal);
}
