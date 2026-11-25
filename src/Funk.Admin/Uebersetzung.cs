// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json.Nodes;

namespace Funk.Admin;

/// <summary>
/// Übersetzt eine Befehlszeile in Befehl und Argumente für den Verwaltungssocket (3.8). Redet mit niemandem; was hier
/// gelesen wird, steht auch in <see cref="Befehlsoptionen"/>.
/// </summary>
public static class Uebersetzung
{
    public static (string Befehl, JsonObject Argumente) Uebersetzen(Befehlszeile z)
    {
        ArgumentNullException.ThrowIfNull(z);
        var a = new JsonObject();
        void Text(string name, string? feld = null)
        {
            if (z.Wert(name) is { } w) a[feld ?? name] = w;
        }
        void Zahl(string name, string? feld = null)
        {
            if (z.Zahl(name) is { } w) a[feld ?? name] = w;
        }
        string Geraet() => z.Positional.FirstOrDefault() ?? z.Wert("geraet") ?? throw new ArgumentException("Gerät fehlt (Anzeigeform oder Fingerabdruck).");

        // Eine leere Liste nimmt alles weg; das geht nur ausdrücklich mit --leeren. Bis 01.10.2026 ergab eine fehlende oder
        // vertippte Option (--name statt --namen) still eine leere Liste.
        JsonArray ListeOderLeeren(string option, string was, string beispiel)
        {
            var wert = z.Wert(option);
            if (z.Schalter("leeren"))
            {
                if (wert is not null) throw new ArgumentException($"--{option} und --leeren zusammen geht nicht. Nichts geändert.");
                a["leeren"] = true;
                return [];
            }
            if (wert is null)
                throw new ArgumentException($"--{option} fehlt, etwa --{option} {beispiel}. Alle {was} wegnehmen geht nur mit --leeren. Nichts geändert.");
            var teile = wert.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (teile.Length == 0)
                throw new ArgumentException($"--{option} ist leer. Alle {was} wegnehmen geht nur mit --leeren. Nichts geändert.");
            return [.. teile.Select(t => (JsonNode)t)];
        }

        // Kanalsperre: genau eins von --konto und --alle, dazu --netz. Der Aufrufer liest --alle selbst (z.Schalter), damit die Tabelle es sieht.
        void Sperrziel(bool alle)
        {
            if (alle && z.Wert("konto") is not null) throw new ArgumentException("--konto und --alle zusammen geht nicht. Nichts geändert.");
            if (alle) a["alle"] = true;
            else if (z.Wert("konto") is null) throw new ArgumentException("Für wen? --konto <id> oder --alle. Nichts geändert.");
            if (z.Wert("netz") is null) throw new ArgumentException("--netz fehlt (Nummer oder Name, siehe netzliste). Nichts geändert.");
        }

        switch (z.Befehl)
        {
            case "status" or "konten" or "netzliste" or "vorrangspur" or "vorrangspur-leeren" or "messwerte" or "profil-neu-laden"
                or "admins" or "antraege" or "mitglieder":
                return (z.Befehl, a);
            case "journal":
                Zahl("konto");
                Zahl("anzahl");
                return (z.Befehl, a);
            case "eigentuemer" or "admin-vorsehen" or "admin-sperren" or "admin-entsperren" or "admin-zuruecksetzen" or "notfallcodes":
                Zahl("konto");
                return (z.Befehl, a);
            case "admin-ernennen" or "admin-schluessel-entfernen":
                Zahl("konto"); Text("kennung");
                return (z.Befehl, a);
            case "admin-einrichtung-neu":
                Zahl("konto");
                if (z.Schalter("anzeigen")) a["anzeigen"] = true;
                return (z.Befehl, a);
            case "zweitfaktor-bestaetigen":
                Zahl("konto"); Text("code");
                return (z.Befehl, a);
            case "admin-entziehen":
                Zahl("konto"); Text("stufe");
                return (z.Befehl, a);
            case "scnamen":
                Zahl("konto");
                a["namen"] = ListeOderLeeren("namen", "SC-Namen", "\"Mustermann,Max\"");
                return (z.Befehl, a);
            case "discordname":
                // A53: Discord-Name eines Kontos, eindeutig; --leeren nimmt ihn weg.
                Zahl("konto"); Text("name");
                if (z.Schalter("leeren")) a["leeren"] = true;
                return (z.Befehl, a);
            case "sicherung":
                Text("ziel");
                return (z.Befehl, a);
            case "sperrliste":
                return (z.Befehl, a);
            case "entsperren":
                // A51: genau eins davon; der Server prüft es und nennt die Kennung, nie die Adresse.
                Text("eintrag"); Text("ip");
                if (z.Schalter("alle")) a["alle"] = true;
                return (z.Befehl, a);
            case "konto-anlegen":
                Text("discord"); Text("benutzername"); Text("name"); Text("stufe");
                return (z.Befehl, a);
            case "verknuepfen":
                Text("code"); Text("discord"); Text("benutzername"); Text("name"); Text("stufe");
                return (z.Befehl, a);
            case "konto-sperren" or "konto-entsperren" or "konto-loeschen" or "verdraengung-freigeben":
                Zahl("konto");
                return (z.Befehl, a);
            case "stufe":
                Zahl("konto"); Text("stufe");
                return (z.Befehl, a);
            case "name":
                Zahl("konto"); Text("name");
                return (z.Befehl, a);
            case "rechte":
                Zahl("konto");
                a["rechte"] = ListeOderLeeren("rechte", "Rechte", "ZUWEISEN,GLOBAL_SENDEN");
                return (z.Befehl, a);
            case "mitgliedschaft":
                Text("discord"); Text("status"); Text("benutzername"); Text("name");
                return (z.Befehl, a);
            case "geraete":
                Zahl("konto");
                if (z.Schalter("wartend")) a["wartend"] = true;
                // Die Nachzügler vor einem Umzug: Geräte ohne Sitzung seit dem Tag (UTC).
                if (z.Wert("nicht-seit") is { } ns && !DateOnly.TryParseExact(ns, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new ArgumentException("--nicht-seit braucht ein Datum wie 2026-10-01 (Jahr-Monat-Tag). Nichts geändert.");
                Text("nicht-seit", "nichtSeit");
                return (z.Befehl, a);
            case "freigeben" or "ablehnen" or "entfernen" or "sperren":
                a["geraet"] = Geraet();
                Text("grund");
                Zahl("vormerkung");
                Zahl("an-konto", "anKonto");
                return (z.Befehl, a);
            case "geraet-umhaengen":
                a["geraet"] = Geraet();
                Zahl("an-konto", "anKonto");
                return (z.Befehl, a);
            case "sperre-aufheben":
                a["geraet"] = Geraet();
                return (z.Befehl, a);
            case "netz-setzen":
            {
                if (z.Wert("json") is { } nj)
                {
                    a["objekt"] = JsonNode.Parse(nj);
                    return (z.Befehl, a);
                }
                // A62, A64 (Sarevoc, 01.10.2026): Ab 0.5.0 steht ein Netz ohne Schalter auf Sprachaktivierung, Taste nur ausdrücklich
                // (--taste, oder --nur-taste: dann gibt es ohnehin nur die Taste). --va bleibt erlaubt und wirkt wie ohne Schalter, damit
                // die Befehle aus den Anleitungen für 0.4.x weiter stimmen. Global (Netz 1) bleibt immer Taste, auch nurTaste wie beim
                // Anlegen durch den Server (6.5). Gespeicherte Netze ändert das nicht; es gilt nur für das, was hier gesetzt wird.
                var id = z.Zahl("id") ?? throw new ArgumentException("--id fehlt.");
                var global = id == 1;
                var taste = z.Schalter("taste");
                var va = z.Schalter("va");
                var nurTaste = z.Schalter("nur-taste");
                if (taste && va)
                    throw new ArgumentException("--taste und --va zusammen geht nicht: ohne Schalter steht ein Kanal auf Sprachaktivierung, mit --taste auf Taste. Nichts geändert.");
                if (va && global) throw new ArgumentException("Global (Kanal 1) bleibt immer auf Taste, --va geht dort nicht. Nichts geändert.");
                if (va && nurTaste)
                    throw new ArgumentException("--va und --nur-taste zusammen geht nicht: ein Kanal nur mit Taste steht auch ab Werk auf Taste. Nichts geändert.");
                var netz = new JsonObject
                {
                    ["id"] = id,
                    ["name"] = z.Pflicht("name"),
                    ["typ"] = global ? "GLOBAL" : "NORMAL",
                    ["reihenfolge"] = z.Zahl("reihenfolge") ?? 0,
                    ["nurTaste"] = nurTaste || global,
                    ["ausloeserStandard"] = taste || nurTaste || global ? "TASTE" : "VA",
                    ["offen"] = z.Wert("offen") ?? "NEIN",
                    ["standard"] = z.Schalter("standard"),
                };
                // Stufe 3 (0.6.0, 6.16): Mit einer Ch.-Liste wird das Netz ein Kanalnetz, aus der Befehlszeile oder aus einer Datei (eine
                // Bezeichnung je Zeile). Die erste ist immer die Ruhe (Ch. 0); fehlt sie, rutschte jede Bezeichnung um einen Ch. weiter.
                if (Kanalliste(z.Wert("kanaele"), z.Wert("kanaele-datei")) is { } kanaele) netz["kanaele"] = kanaele;
                if (z.Wert("gruppe") is { } gruppe) netz["gruppe"] = gruppe;
                a["objekt"] = netz;
                return (z.Befehl, a);
            }
            case "schiff-setzen":
                a["objekt"] = JsonNode.Parse(z.Pflicht("json"));
                return (z.Befehl, a);
            case "rolle-setzen":
            {
                if (z.Wert("json") is { } rj)
                {
                    a["objekt"] = JsonNode.Parse(rj);
                    return (z.Befehl, a);
                }
                var felder = new JsonArray();
                foreach (var f in z.Alle("feld"))
                {
                    // feld:netzId[:h][:s][:std][:fest], etwa 0:2:h:s:std
                    var t = f.Split(':');
                    if (t.Length < 2) throw new ArgumentException("--feld feld:netzId[:h][:s][:std][:fest]");
                    felder.Add(new JsonObject
                    {
                        ["feld"] = byte.Parse(t[0], CultureInfo.InvariantCulture),
                        ["netzId"] = ushort.Parse(t[1], CultureInfo.InvariantCulture),
                        ["hoeren"] = t.Contains("h"),
                        ["senden"] = t.Contains("s"),
                        ["sendenStandard"] = t.Contains("std"),
                        ["fest"] = t.Contains("fest"),
                    });
                }
                var rolle = new JsonObject
                {
                    ["id"] = z.Zahl("id") ?? throw new ArgumentException("--id fehlt."),
                    ["name"] = z.Pflicht("name"),
                    ["reihenfolge"] = z.Zahl("reihenfolge") ?? 0,
                    ["selbstWaehlbar"] = z.Schalter("selbst"),
                    ["brauchtSchiff"] = z.Schalter("braucht-schiff"),
                    ["felder"] = felder,
                };
                if (z.Zahl("kapazitaet") is { } kap) rolle["kapazitaet"] = kap;
                a["objekt"] = rolle;
                return (z.Befehl, a);
            }
            case "netz-loeschen" or "schiff-loeschen" or "rolle-loeschen":
                Zahl("id");
                return (z.Befehl, a);
            case "einstellung":
                if (z.Wert("selbstwahl") is { } sw) a["selbstwahl"] = sw is "an" or "true";
                if (z.Wert("durchsage-nur-taste") is { } dt) a["durchsageNurTaste"] = dt is "an" or "true";
                if (z.Wert("nachzuegler") is { } nz) a["nachzuegler"] = nz is "an" or "true";
                return (z.Befehl, a);
            case "auftrag":
                // Einsatzleitung für einen Abend (6.12): --von und --bis als HH:MM oder JJJJ-MM-TT HH:MM in Ortszeit (Europe/Berlin);
                // ohne --bis der nächste Zeitpunkt 04:00, ohne --rechte alle sechs der Einsatzleitung.
                Zahl("konto"); Text("von"); Text("bis"); Text("grund");
                if (z.Wert("rechte") is { } rechte)
                    a["rechte"] = new JsonArray([.. rechte.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(r => (JsonNode)r)]);
                return (z.Befehl, a);
            case "auftrag-entziehen":
                Zahl("konto");
                return (z.Befehl, a);
            case "auftraege":
                return (z.Befehl, a);
            case "flotte-vorlage":
                Text("name");
                return (z.Befehl, a);
            case "kanalsperre-setzen":
                // Kanalsperre (0.6.0, 6.17): für eine Person (--konto) oder alle ohne Einsatzleitung (--alle); --netz als Nummer oder Name,
                // mit --ch ein einzelner Ch., ohne das ganze Netz; ohne --bis bis zum nächsten 04:00 (höchstens 24 h).
                Zahl("konto"); Text("netz"); Zahl("ch"); Text("bis");
                Sperrziel(z.Schalter("alle"));
                return (z.Befehl, a);
            case "kanalsperre-aufheben":
                Zahl("konto"); Text("netz"); Zahl("ch");
                Sperrziel(z.Schalter("alle"));
                return (z.Befehl, a);
            case "kanalsperren":
                return (z.Befehl, a);
        }
        throw new ArgumentException("Unbekannter Befehl: " + z.Befehl + ". funk-admin hilfe zeigt alle.");
    }

    /// <summary>
    /// Die Ch.-Liste von <c>netz-setzen</c>: <c>--kanaele "Ruhe,Schiff 1,Schiff 2"</c> oder <c>--kanaele-datei</c> mit einer Bezeichnung je
    /// Zeile (leere Zeilen und Zeilen mit # am Anfang zählen nicht). Ohne beides <c>null</c> (ein gewöhnliches Netz).
    /// </summary>
    internal static JsonArray? Kanalliste(string? liste, string? datei)
    {
        if (liste is not null && datei is not null) throw new ArgumentException("--kanaele und --kanaele-datei zusammen geht nicht. Nichts geändert.");
        string[] teile;
        if (liste is not null)
        {
            teile = liste.Split(',', StringSplitOptions.TrimEntries);
        }
        else if (datei is not null)
        {
            if (!File.Exists(datei)) throw new ArgumentException("--kanaele-datei: " + datei + " gibt es nicht. Nichts geändert.");
            teile = [.. File.ReadAllLines(datei).Select(z => z.Trim()).Where(z => z.Length > 0 && !z.StartsWith('#'))];
        }
        else
        {
            return null;
        }
        if (teile.Any(t => t.Length == 0)) throw new ArgumentException("--kanaele hat eine leere Bezeichnung (zwei Kommas hintereinander?). Nichts geändert.");
        if (teile.Length < 2 || !string.Equals(teile[0], "Ruhe", StringComparison.Ordinal))
            throw new ArgumentException("Die Ch.-Liste beginnt mit Ruhe (Ch. 0) und hat mindestens einen Ch., etwa --kanaele \"Ruhe,Schiff 1,Schiff 2\". Nichts geändert.");
        if (teile.Length > 31) throw new ArgumentException("Höchstens 30 Ch. nach der Ruhe, hier sind es " + (teile.Length - 1).ToString(CultureInfo.InvariantCulture) + ". Nichts geändert.");
        return [.. teile.Select(t => (JsonNode)t)];
    }
}
