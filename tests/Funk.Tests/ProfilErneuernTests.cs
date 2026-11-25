// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// <c>funk-admin profil-erneuern</c> auf Sarevocs Stick (EINRICHTEN-UMZUG-ENTWURF, "Was beim Eigentümer bleibt"): bisheriges Profil
/// zeigen, Anfragezeile einfügen oder profilanfrage.txt nehmen oder nur verlängern, Änderungen und Kennung zeigen, j/n zum
/// Telefonvergleich, Version hoch, 365 Tage, Reserve behalten, alte Fassung sichern, neu signieren und selbst prüfen, Kopie
/// nach zum-verschicken. Dazu die Wege, auf denen nichts geschrieben werden darf.
/// </summary>
internal static class ProfilErneuernTests
{
    private sealed record Stick(AdminEinrichtungTests.Ordner O, string Profil, string Schluessel, string Spki, string PinAlt, string PinReserve, string PinNeu, string PinDritt);

    private static async Task<Stick> StickAsync(AdminEinrichtungTests.Ordner o)
    {
        var (code, text) = await AdminEinrichtungTests.Werkzeug("profilschluessel-erzeugen", "--schluessel", o["profil.key"]);
        Pruefe.Gleich(0, code, text);
        var spki = text.Split('\n', StringSplitOptions.TrimEntries)[1];
        string Pin(string name)
        {
            var (c, t) = AdminEinrichtungTests.Werkzeug("schluessel-erzeugen", "--zertifikat", o[name + ".crt"], "--schluessel", o[name + ".key"]).GetAwaiter().GetResult();
            Pruefe.Gleich(0, c, t);
            return AdminEinrichtungTests.Pin(t);
        }
        var alt = Pin("alt");
        var reserve = Pin("reserve");
        var neu = Pin("neu");
        var dritt = Pin("dritt");
        Pruefe.Gleich(0, (await AdminEinrichtungTests.Werkzeug("profil-schreiben", "--name", "Meine Staffel", "--version", "1", "--host", "heim.example.org/v4",
            "--tcp-port", "8443", "--pin", alt, "--pin", reserve, "--gueltig-tage", "30", "--aus", o["daten.json"])).Code, "Profil 1");
        Pruefe.Gleich(0, (await AdminEinrichtungTests.Werkzeug("profil-signieren", "--daten", o["daten.json"], "--schluessel", o["profil.key"], "--aus", o["profil.json"])).Code, "signiert");
        // Auf dem Stick liegen profil.key, reserve.crt und reserve.key nebeneinander (HEIMSERVER-PROXMOX.md, Abschnitt 6).
        File.Delete(o["alt.key"]);
        return new Stick(o, o["profil.json"], o["profil.key"], spki, alt, reserve, neu, dritt);
    }

    private static Serverprofil Lesen(string pfad, string spki)
    {
        Pruefe.Wahr(SigniertesProfil.TryLesen(File.ReadAllBytes(pfad), out var s), pfad + " lesbar");
        using var k = Profilschluessel.AusSpki(Convert.FromBase64String(spki));
        var p = ProfilAnnahme.Pruefen(s!, [k], null, DateTimeOffset.UtcNow);
        Pruefe.Gleich(ProfilUrteil.Uebernehmen, p.Urteil, pfad + ": mit dem Profilschlüssel signiert");
        return p.Profil!;
    }

    private static string Pins(Serverprofil p) => string.Join(" ", p.Pins.Select(x => x.AlsBase64()).Order(StringComparer.Ordinal));

    private static string Sortiert(params string[] pins) => string.Join(" ", pins.Order(StringComparer.Ordinal));

    private static string Kennung(string pin) => Fingerabdruck.TryAusBase64(pin, out var f) ? f.Anzeige : throw new PruefungException("kein Pin");

    private static string Anfrage(string host, string port, string pin) => "TR-PROFILANFRAGE v1 " + host + " " + port + " " + pin;

    [Test]
    public static async Task ProfilErneuern_Uebergang_UndAbschluss()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var s = await StickAsync(o);
        var v1 = File.ReadAllBytes(s.Profil);

        // Der Server-Admin schickt die Zeile, Sarevoc fügt sie ein, vergleicht die Kennung am Telefon und sagt j.
        var (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk.example.org", "8443", s.PinNeu) + "\nj\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "Übergang: " + text);
        foreach (var erwartet in new[] { "Version 1", "heim.example.org", Kennung(s.PinAlt), "Reserve", "TR-PROFILANFRAGE", "Neu:", "Bleibt:",
            "Hat der Server-Admin dir", Kennung(s.PinNeu), "Urteil: Uebernehmen", "zum-verschicken" })
            Pruefe.Wahr(text.Contains(erwartet, StringComparison.Ordinal), "zeigt " + erwartet + ":\n" + text);
        var v2 = Lesen(s.Profil, s.Spki);
        Pruefe.Gleich(2u, v2.ProfilVersion, "Version hoch");
        Pruefe.Gleich("funk.example.org heim.example.org", string.Join(" ", v2.Hosts.Select(h => h.Name).Order(StringComparer.Ordinal)), "alter und neuer Host");
        Pruefe.Wahr(v2.Hosts.All(h => h is { IPv4: true, IPv6: false }), "Familien wie angefragt (v4)");
        Pruefe.Gleich(Sortiert(s.PinAlt, s.PinNeu, s.PinReserve), Pins(v2), "alt, neu und Reserve: alle drei Plätze");
        Pruefe.Gleich((ushort)8443, v2.TcpPort, "Port bleibt");
        var tage = (DateTimeOffset.FromUnixTimeMilliseconds(v2.GueltigBis) - DateTimeOffset.UtcNow).TotalDays;
        Pruefe.Wahr(tage is > 364 and < 366, "365 Tage gültig: " + tage);
        Pruefe.BytesGleich(v1, File.ReadAllBytes(o["profil-v1.json"]), "die alte Fassung gesichert");
        Pruefe.BytesGleich(File.ReadAllBytes(s.Profil), File.ReadAllBytes(Path.Combine(o.Pfad, "zum-verschicken", "profil.json")), "Kopie zum Verschicken");
        Pruefe.Gleich(0, (await AdminEinrichtungTests.Werkzeug("profil-pruefen", "--profil", s.Profil, "--spki", s.Spki)).Code, "profil-pruefen nimmt es an");

        // Zwei bis vier Wochen später dieselbe Zeile: Abschluss, der alte Server fällt heraus, die Reserve bleibt.
        (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk.example.org", "8443", s.PinNeu) + "\nj\nj\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "Abschluss: " + text);
        Pruefe.Wahr(text.Contains("Fällt weg:", StringComparison.Ordinal) && text.Contains(Kennung(s.PinAlt), StringComparison.Ordinal), "zeigt, was wegfällt:\n" + text);
        var v3 = Lesen(s.Profil, s.Spki);
        Pruefe.Gleich(3u, v3.ProfilVersion, "Version 3");
        Pruefe.Gleich("funk.example.org", string.Join(" ", v3.Hosts.Select(h => h.Name)), "nur der neue Host");
        Pruefe.Gleich(Sortiert(s.PinNeu, s.PinReserve), Pins(v3), "neu und Reserve");
        Pruefe.Wahr(File.Exists(o["profil-v2.json"]) && File.Exists(o["profil-v1.json"]), "beide alten Fassungen liegen da");

        // Ein dritter Server, wenn schon drei Pins drinstehen (aus v2): Sarevoc wählt, welcher wegfällt.
        File.Copy(o["profil-v2.json"], s.Profil, overwrite: true);
        (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk3.example.org", "8443", s.PinDritt) + "\n1\nj\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "vierter Pin: " + text);
        Pruefe.Wahr(text.Contains("höchstens 3 Pins", StringComparison.Ordinal), "fragt, welcher Pin wegfällt:\n" + text);
        var v3b = Lesen(s.Profil, s.Spki);
        Pruefe.Wahr(v3b.Pins.Count == 3 && Pins(v3b).Contains(s.PinDritt, StringComparison.Ordinal) && Pins(v3b).Contains(s.PinReserve, StringComparison.Ordinal),
            "die Reserve steht nie zur Wahl: " + Pins(v3b));
        Pruefe.Gleich(1, Directory.GetFiles(o.Pfad, "profil-v2*.json").Length, "dieselbe Fassung wird nicht doppelt gesichert");

        // Eine Sicherung mit anderem Inhalt unter demselben Namen bleibt; die neue bekommt einen eigenen Namen.
        var fremdeSicherung = Encoding.UTF8.GetBytes("{\"nicht\":\"anfassen\"}");
        File.WriteAllBytes(o["profil-v3.json"], fremdeSicherung);
        (code, text) = await AdminEinrichtungTests.MitEingabe("v\nj\n", "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "verlängern über einer belegten Sicherung: " + text);
        Pruefe.BytesGleich(fremdeSicherung, File.ReadAllBytes(o["profil-v3.json"]), "die vorhandene Sicherung bleibt");
        Pruefe.Gleich(2, Directory.GetFiles(o.Pfad, "profil-v3*.json").Length, "die Fassung 3 liegt daneben");
    }

    [Test]
    public static async Task ProfilErneuern_VerlaengernDateiUndNein()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var s = await StickAsync(o);
        var v1 = File.ReadAllBytes(s.Profil);

        // Nein: nichts geschrieben, nichts gesichert. (Bis 0.4.0 mit Port 443; ein Portwechsel neben einem bleibenden Host
        // endet jetzt schon vor der Frage, siehe ProfilErneuern_PortwechselNebenAnderemHost_Abgelehnt.)
        var (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk.example.org", "8443", s.PinNeu) + "\nn\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(1, code, "n: " + text);
        Pruefe.Wahr(text.Contains("Nichts geändert", StringComparison.Ordinal), text);
        Pruefe.Wahr(text.Contains("Hat der Server-Admin dir", StringComparison.Ordinal), "die Telefonfrage kam, n hieß nein:\n" + text);
        Pruefe.BytesGleich(v1, File.ReadAllBytes(s.Profil), "unverändert");
        Pruefe.Falsch(File.Exists(o["profil-v1.json"]) || Directory.Exists(Path.Combine(o.Pfad, "zum-verschicken")), "nichts gesichert, nichts zum Verschicken");

        // Eingabe bricht ab (Fenster zu): ebenso nichts.
        (code, _) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk.example.org", "8443", s.PinNeu) + "\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(1, code, "ohne Antwort");
        Pruefe.BytesGleich(v1, File.ReadAllBytes(s.Profil), "unverändert ohne Antwort");

        // Kaputte Zeile, dann nichts mehr.
        (code, text) = await AdminEinrichtungTests.MitEingabe("TR-PROFILANFRAGE v1 funk.example.org 8443 keinpin\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(1, code, "kaputte Zeile: " + text);
        Pruefe.Wahr(text.Contains("Pin", StringComparison.Ordinal) && text.Contains("noch einmal", StringComparison.Ordinal), "sagt, was an der Zeile nicht stimmt:\n" + text);

        // Nur verlängern.
        (code, text) = await AdminEinrichtungTests.MitEingabe("v\nj\n", "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "verlängern: " + text);
        var v2 = Lesen(s.Profil, s.Spki);
        Pruefe.Wahr(v2.ProfilVersion == 2 && v2.Hosts.Single().Name == "heim.example.org" && Pins(v2) == Sortiert(s.PinAlt, s.PinReserve), "nur Version und Gültigkeit neu");
        Pruefe.Wahr(DateTimeOffset.FromUnixTimeMilliseconds(v2.GueltigBis) > DateTimeOffset.UtcNow.AddDays(364), "365 Tage");
        Pruefe.Falsch(text.Contains("Hat der Server-Admin dir", StringComparison.Ordinal), "ohne Anfrage keine Kennungsfrage");

        // profilanfrage.txt neben dem Profil: Enter nimmt sie.
        File.WriteAllText(o["profilanfrage.txt"], Anfrage("funk.example.org", "8443", s.PinNeu) + "\n", Encoding.UTF8);
        (code, text) = await AdminEinrichtungTests.MitEingabe("\nj\n", "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "aus der Datei: " + text);
        Pruefe.Wahr(text.Contains("profilanfrage.txt", StringComparison.Ordinal), "nennt die Datei:\n" + text);
        Pruefe.Wahr(Pins(Lesen(s.Profil, s.Spki)).Contains(s.PinNeu, StringComparison.Ordinal), "Pin aus der Datei übernommen");

        // Als Option, ohne Frage nach der Zeile. Host und Pin stehen schon drin: Abschluss angeboten, hier mit n abgelehnt.
        (code, text) = await AdminEinrichtungTests.MitEingabe("n\nj\n", "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel,
            "--anfrage", Anfrage("funk.example.org", "8443", s.PinNeu));
        Pruefe.Gleich(0, code, "--anfrage: " + text);
        Pruefe.Falsch(text.Contains("Eingabe:", StringComparison.Ordinal), "mit --anfrage keine Frage nach der Zeile");
        var v4 = Lesen(s.Profil, s.Spki);
        Pruefe.Wahr(v4.ProfilVersion == 4 && v4.Hosts.Count == 2 && v4.Pins.Count == 3, "Abschluss abgelehnt: alter Server bleibt, nur verlängert");
    }

    /// <summary>
    /// Gegenprüfung 01.10.2026: Ohne reserve.crt und reserve.key neben profil.key kannte das Werkzeug die Reserve nicht, und bei
    /// einem vierten Pin stand sie in der Liste, aus der Sarevoc einen streicht. Jetzt fragt es vorher, welcher Pin die Reserve ist.
    /// Dazu: Die Telefonfrage nennt Name und Port der Anfrage, denn die Kennung deckt nur den Pin.
    /// </summary>
    [Test]
    public static async Task ProfilErneuern_OhneReservedatei_ReserveNieZurWahl()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var s = await StickAsync(o);
        var (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk.example.org", "8443", s.PinNeu) + "\nj\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "Übergang: " + text);
        Pruefe.Wahr(text.Contains("Kennung vorgelesen: " + Kennung(s.PinNeu) + ", dazu den Namen funk.example.org und den Port 8443?", StringComparison.Ordinal),
            "Telefonfrage mit Name und Port:\n" + text);
        Pruefe.Gleich(Sortiert(s.PinAlt, s.PinNeu, s.PinReserve), Pins(Lesen(s.Profil, s.Spki)), "drei Pins");

        // Der Stick ohne Reservedateien; ein vierter Pin kommt dazu. Erst die Reserve benennen (2), dann den alten streichen (1).
        File.Delete(o["reserve.crt"]);
        File.Delete(o["reserve.key"]);
        (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk3.example.org", "8443", s.PinDritt) + "\n2\n1\nj\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "vierter Pin ohne Reservedatei: " + text);
        Pruefe.Wahr(text.Contains("Welcher Pin ist die Reserve", StringComparison.Ordinal), "fragt nach der Reserve:\n" + text);
        var neu = Lesen(s.Profil, s.Spki);
        Pruefe.Gleich(Sortiert(s.PinReserve, s.PinNeu, s.PinDritt), Pins(neu), "die Reserve bleibt, der alte fällt weg");

        // Abbruch bei der Frage nach der Reserve: nichts geschrieben.
        var vorher = File.ReadAllBytes(s.Profil);
        (code, _) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk4.example.org", "8443", s.PinAlt) + "\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(1, code, "ohne Antwort zur Reserve");
        Pruefe.BytesGleich(vorher, File.ReadAllBytes(s.Profil), "unverändert");
    }

    /// <summary>
    /// Befund der Prüfung der Pakete 0.4.0 (mittel): profil-erneuern übernahm den TCP-Port aus der Anfragezeile des Server-Admins und zeigte den
    /// Wechsel nur als eine Zeile. Nimmt der Server-Admin beim Einrichten den Vorschlag 443 (ohne Router, wenn 443 frei ist) und liest am
    /// Telefon ehrlich 443 vor, sagt Sarevoc j; jeder Client 0.4.0 übernimmt dann Version 2 mit Port 443, weil Sarevocs Pin darin steht,
    /// und sucht heim.example.org unter 443 (dort antworten nginx und Foundry). Ein Profil kennt nur einen Port für alle
    /// Hosts. Bleibt neben dem angefragten Host ein anderer drin, lehnt das Werkzeug einen Portwechsel deshalb ab, vor der
    /// Telefonfrage. Wechselt der einzige Server seinen Port, oder fällt der alte beim Abschluss heraus, geht es weiter.
    /// </summary>
    [Test]
    public static async Task ProfilErneuern_PortwechselNebenAnderemHost_Abgelehnt()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var s = await StickAsync(o);
        var v1 = File.ReadAllBytes(s.Profil);

        // Übergang mit 443: abgelehnt, auch mit j hinterher; nichts geschrieben, nichts gesichert.
        var (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk.example.org", "443", s.PinNeu) + "\nj\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(1, code, "Portwechsel im Übergang: " + text);
        Pruefe.Wahr(text.Contains("TCP-Port 8443 -> 443", StringComparison.Ordinal) && text.Contains("heim.example.org", StringComparison.Ordinal)
            && text.Contains("Nichts geändert", StringComparison.Ordinal), "sagt, warum:\n" + text);
        Pruefe.Falsch(text.Contains("Hat der Server-Admin dir", StringComparison.Ordinal), "keine Telefonfrage für ein Profil, das nicht entsteht:\n" + text);
        Pruefe.BytesGleich(v1, File.ReadAllBytes(s.Profil), "unverändert");
        Pruefe.Falsch(File.Exists(o["profil-v1.json"]) || Directory.Exists(Path.Combine(o.Pfad, "zum-verschicken")), "nichts gesichert, nichts zum Verschicken");

        // Der Übergang mit 8443 geht.
        (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk.example.org", "8443", s.PinNeu) + "\nj\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "Übergang mit 8443: " + text);
        var v2 = File.ReadAllBytes(s.Profil);

        // Abschluss mit 443, den alten Server behalten (n): wieder zwei Hosts, also abgelehnt.
        (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk.example.org", "443", s.PinNeu) + "\nn\nj\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(1, code, "Abschluss abgelehnt, Port 443: " + text);
        Pruefe.Wahr(text.Contains("TCP-Port 8443 -> 443", StringComparison.Ordinal), "sagt, warum:\n" + text);
        Pruefe.BytesGleich(v2, File.ReadAllBytes(s.Profil), "Version 2 bleibt");

        // Abschluss mit 443 und j: Der alte Server fällt heraus, nur der neue bleibt; dann darf der Port wechseln.
        (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("funk.example.org", "443", s.PinNeu) + "\nj\nj\n",
            "profil-erneuern", "--bisher", s.Profil, "--schluessel", s.Schluessel);
        Pruefe.Gleich(0, code, "Abschluss mit 443: " + text);
        var v3 = Lesen(s.Profil, s.Spki);
        Pruefe.Wahr(v3.TcpPort == 443 && v3.Hosts.Single().Name == "funk.example.org", "Version 3 nur mit dem neuen Server auf 443");

        // Der einzige Server wechselt seinen Port: geht, mit der Zeile, die die Folge nennt.
        using var o2 = new AdminEinrichtungTests.Ordner();
        var s2 = await StickAsync(o2);
        (code, text) = await AdminEinrichtungTests.MitEingabe(Anfrage("heim.example.org", "443", s2.PinAlt) + "\nj\n",
            "profil-erneuern", "--bisher", s2.Profil, "--schluessel", s2.Schluessel);
        Pruefe.Gleich(0, code, "einziger Server auf 443: " + text);
        Pruefe.Wahr(text.Contains("8443 -> 443", StringComparison.Ordinal) && text.Contains("jeder Host darin muss unter 443", StringComparison.Ordinal),
            "Portwechsel gezeigt, mit Folge:\n" + text);
        Pruefe.Gleich((ushort)443, Lesen(s2.Profil, s2.Spki).TcpPort, "Port 443");
    }

    [Test]
    public static async Task ProfilErneuern_FremderProfilschluesselSchreibtNichts()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var s = await StickAsync(o);
        var v1 = File.ReadAllBytes(s.Profil);
        Pruefe.Gleich(0, (await AdminEinrichtungTests.Werkzeug("profilschluessel-erzeugen", "--schluessel", o["anderer.key"])).Code, "anderer Schlüssel");
        var (code, text) = await AdminEinrichtungTests.MitEingabe("v\nj\n", "profil-erneuern", "--bisher", s.Profil, "--schluessel", o["anderer.key"]);
        Pruefe.Gleich(1, code, "fremder Schlüssel: " + text);
        Pruefe.Wahr(text.Contains("profil.key", StringComparison.Ordinal) && text.Contains("Nichts geändert", StringComparison.Ordinal), text);
        Pruefe.BytesGleich(v1, File.ReadAllBytes(s.Profil), "unverändert");

        (code, text) = await AdminEinrichtungTests.MitEingabe("v\nj\n", "profil-erneuern", "--bisher", o["fehlt.json"], "--schluessel", s.Schluessel);
        Pruefe.Gleich(1, code, "ohne bisheriges Profil: " + text);
    }
}
