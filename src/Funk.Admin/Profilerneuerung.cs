// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Funk.Protokoll.Identitaet;
using Funk.Server.Betrieb;

namespace Funk.Admin;

/// <summary>
/// <c>funk-admin profil-erneuern</c>, der eine Schritt auf Sarevocs Stick (A55, EINRICHTEN-UMZUG-ENTWURF "Was beim Eigentümer
/// bleibt"): das bisherige Profil zeigen, eine Anfragezeile einfügen, profilanfrage.txt nehmen oder nur verlängern, zeigen was
/// neu ist, was bleibt und was wegfällt, die Kennung zum Telefonvergleich, dann Version hoch, 365 Tage, Reserve behalten, die
/// alte Fassung sichern, neu signieren, gegen den öffentlichen Teil aus profil.key selbst prüfen und eine Kopie nach
/// zum-verschicken legen. Vor dem j wird nichts geschrieben.
/// <para>
/// Was mit einer Anfrage geschieht: Ein neuer Pin kommt dazu, ebenso ein neuer Host (Übergang, alter und neuer Server stehen
/// beide drin). Steht beides schon drin und daneben noch ein alter Server, fragt das Werkzeug, ob der alte herausfällt
/// (Abschluss: dann bleiben nur dieser Host, dieser Pin und die Reserve). Ein Profil hat höchstens 3 Pins und 4 Hosts (3.1); wird
/// es mehr, fragt es, was wegfällt. Die Reserve steht nie zur Wahl. Sie kommt aus <c>--reserve</c> oder aus reserve.crt
/// beziehungsweise reserve.key neben profil.key.
/// </para>
/// </summary>
public sealed class Profilerneuerung(TextReader ein, TextWriter aus, Func<DateTimeOffset>? uhr = null)
{
    public const string AnfrageDatei = "profilanfrage.txt";
    public const string Versandordner = "zum-verschicken";

    private readonly Dialog _d = new(ein, aus);
    private readonly Func<DateTimeOffset> _uhr = uhr ?? (() => DateTimeOffset.UtcNow);

    private int Nichts(string grund)
    {
        aus.WriteLine(grund + " Nichts geändert.");
        return 1;
    }

    public int Ausfuehren(string bisherPfad, string schluesselPfad, string? anfrageText, string? reserveAngabe, uint gueltigTage)
    {
        ArgumentNullException.ThrowIfNull(bisherPfad);
        ArgumentNullException.ThrowIfNull(schluesselPfad);
        if (gueltigTage is 0 or > 3650) throw new ArgumentException("--gueltig-tage muss zwischen 1 und 3650 liegen. Nichts geändert.");
        var jetzt = _uhr();

        using var schluessel = ECDsa.Create();
        try
        {
            schluessel.ImportFromPem(File.ReadAllText(schluesselPfad));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            return Nichts("Der Profilschlüssel " + schluesselPfad + " ist nicht lesbar (" + e.Message + ").");
        }
        var spki = schluessel.ExportSubjectPublicKeyInfo();
        if (!P256Spki.IstKanonisch(spki)) return Nichts("Der Profilschlüssel " + schluesselPfad + " ist kein P-256-Schlüssel von profilschluessel-erzeugen.");
        using var oeffentlich = Profilschluessel.AusSpki(spki);

        var bisherVoll = Path.GetFullPath(bisherPfad);
        if (!File.Exists(bisherVoll)) return Nichts("Das bisherige Profil " + bisherVoll + " fehlt.");
        var bisherBytes = File.ReadAllBytes(bisherVoll);
        var roh = bisherBytes.AsSpan();
        if (roh.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) roh = roh[3..];
        if (!SigniertesProfil.TryLesen(roh, out var bisherSigniert)) return Nichts(bisherVoll + " ist kein signiertes Serverprofil (profil.json, nicht daten.json).");
        var geprueft = ProfilAnnahme.Pruefen(bisherSigniert, [oeffentlich], null, jetzt);
        if (geprueft.Urteil == ProfilUrteil.SignaturUngueltig)
            return Nichts("Das bisherige Profil ist nicht mit diesem profil.key unterschrieben (Kennung " + oeffentlich.Fingerabdruck.Anzeige + "). Falscher Schlüssel oder falsches Profil?");
        if (geprueft.Profil is not { } bisher) return Nichts("Das bisherige Profil ist richtig unterschrieben, aber sein Inhalt verletzt 3.1.");
        if (bisher.ProfilVersion == uint.MaxValue) return Nichts("Die Version des bisherigen Profils lässt sich nicht mehr erhöhen.");

        if (!ReserveFinden(reserveAngabe, schluesselPfad, bisher, out var reserve)) return 1;
        Zeigen("Bisheriges Profil: " + bisher.Name + ", Version " + Zahl(bisher.ProfilVersion) + ", gültig bis " + Datum(bisher.GueltigBis)
            + (geprueft.Abgelaufen ? " (abgelaufen)" : "") + ", TCP-Port " + Zahl(bisher.TcpPort));
        foreach (var h in bisher.Hosts) Zeigen("  Host " + Hostangabe.Anzeige(h));
        foreach (var p in bisher.Pins) Zeigen("  Pin  " + PinText(p, reserve));

        Profilanfrage? anfrage;
        if (anfrageText is not null)
        {
            var text = File.Exists(anfrageText) ? File.ReadAllText(anfrageText) : anfrageText;
            if (!Profilanfrage.TryLesen(text, out anfrage, out var fehler)) return Nichts(fehler!);
            Zeigen("Anfrage: " + anfrage!.Zeile);
        }
        else if (!AnfrageErfragen(Path.GetDirectoryName(bisherVoll)!, out anfrage)) return 1;

        // ---- das neue Profil ----
        var hosts = bisher.Hosts.ToList();
        var pins = bisher.Pins.ToList();
        var port = bisher.TcpPort;
        if (anfrage is { } a)
        {
            var hostDa = hosts.FindIndex(h => string.Equals(h.Name, a.Host.Name, StringComparison.OrdinalIgnoreCase));
            var anderePins = pins.Where(p => p != a.Pin && p != reserve).ToList();
            if (hostDa >= 0 && pins.Contains(a.Pin) && (hosts.Count > 1 || anderePins.Count > 0))
            {
                // Abschluss eines Umzugs: Der neue Server steht schon drin, der alte noch daneben.
                Zeigen("Host " + a.Host.Name + " und Pin " + a.Kennung + " stehen schon im Profil, daneben noch "
                    + string.Join(", ", hosts.Where((_, i) => i != hostDa).Select(h => h.Name).Concat(anderePins.Select(p => "Pin " + p.Anzeige))) + ".");
                if (reserve is null && !ReserveErfragen(pins.Where(p => p != a.Pin).ToList(), out reserve)) return Nichts("Ohne Reserve kein Abschluss.");
                if (reserve == a.Pin)
                    return Nichts("Der neue Server benutzt den Reserveschlüssel. Ein Profil braucht zwei verschiedene Pins; eine neue Reserve kann profil-erneuern "
                        + "noch nicht eintragen (profil-schreiben und profil-signieren von Hand).");
                if (_d.JaNein("Den alten Server herausnehmen? Dann bleiben nur " + a.Host.Name + ", der Pin " + a.Kennung + " und die Reserve " + reserve!.Value.Anzeige + "."))
                {
                    hosts = [a.Host];
                    pins = [a.Pin, reserve.Value];
                }
            }
            else
            {
                if (hostDa >= 0) hosts[hostDa] = a.Host;
                else hosts.Add(a.Host);
                if (!pins.Contains(a.Pin)) pins.Add(a.Pin);
                // Muss ein Pin weichen und liegt keine reserve.crt oder reserve.key neben profil.key, fragt es erst nach der Reserve;
                // sonst stünde sie in der Liste, aus der Sarevoc einen streicht.
                if (pins.Count > 3 && reserve is null && !ReserveErfragen(pins.Where(p => p != a.Pin).ToList(), out reserve))
                    return Nichts("Ohne Reserve keine Wahl, welcher Pin wegfällt.");
            }
            if (!Begrenzen(pins, 3, "Pins", p => p != a.Pin && p != reserve, p => PinText(p, reserve))) return Nichts("Keine Wahl getroffen.");
            if (!Begrenzen(hosts, 4, "Hosts", h => !string.Equals(h.Name, a.Host.Name, StringComparison.OrdinalIgnoreCase), Hostangabe.Anzeige)) return Nichts("Keine Wahl getroffen.");
            // Ein Profil kennt nur einen TCP-Port für alle Hosts (3.1). Bleibt neben dem angefragten ein anderer Host drin (Übergang
            // eines Umzugs), suchten die Clients auch ihn unter dem neuen Port: Mit 443 vom Server-Admin im Übergangsprofil fände kein Client
            // 0.4.0 Sarevocs Server auf 8443 mehr (Prüfung der Pakete 0.4.0). Ein Portwechsel geht deshalb nur, wenn der angefragte
            // Host allein bleibt; die Ablehnung kommt vor der Telefonfrage.
            var andere = hosts.Where(h => !string.Equals(h.Name, a.Host.Name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Name).ToList();
            if (a.Port != bisher.TcpPort && andere.Count > 0)
                return Nichts("TCP-Port " + Zahl(bisher.TcpPort) + " -> " + Zahl(a.Port) + ": Die Anfrage nennt einen anderen Port als das bisherige Profil, und "
                    + string.Join(", ", andere) + " bleibt drin. Ein Profil kennt nur einen Port für alle Hosts; die Clients suchten dann auch "
                    + andere[0] + " unter Port " + Zahl(a.Port) + ". " + a.Host.Name + " muss für die Clients unter Port " + Zahl(bisher.TcpPort)
                    + " erreichbar sein, dann eine neue Anfragezeile (UMZUG.md, Schritt 3).");
            port = a.Port;
        }
        var neu = new Serverprofil(bisher.Name, bisher.ProfilVersion + 1, jetzt.ToUnixTimeMilliseconds(), jetzt.AddDays(gueltigTage).ToUnixTimeMilliseconds(),
            hosts, port, pins, bisher.Nameserver);
        var daten = neu.AlsJson();
        if (!Serverprofil.TryLesen(daten, out _)) return Nichts("Das neue Profil verletzt 3.1 (Hosts 1-4, Pins 2-3).");

        Unterschied(bisher, neu, reserve);
        // Die Kennung deckt nur den Pin. Name und Port stehen ungeschützt in der Zeile; deshalb gehören sie mit ans Telefon.
        var ja = anfrage is { } fa
            ? _d.JaNein("Hat der Server-Admin dir am Telefon genau diese Kennung vorgelesen: " + fa.Kennung + ", dazu den Namen " + fa.Host.Name + " und den Port "
                + fa.Port.ToString(CultureInfo.InvariantCulture) + "?")
            : _d.JaNein("Profil Version " + Zahl(neu.ProfilVersion) + " so schreiben?");
        if (!ja) return Nichts(anfrage is null ? "Nicht geschrieben." : "Ohne den Vergleich am Telefon wird nichts unterschrieben.");

        // ---- signieren, selbst prüfen, sichern, schreiben ----
        var signiert = SigniertesProfil.Signieren(daten, schluessel);
        if (ProfilAnnahme.Pruefen(signiert, [oeffentlich], bisher.ProfilVersion, jetzt).Urteil != ProfilUrteil.Uebernehmen)
            return Nichts("Das neu signierte Profil besteht die eigene Prüfung nicht.");
        var json = signiert.AlsJson();
        var ordner = Path.GetDirectoryName(bisherVoll)!;
        var sicherung = Sichern(ordner, bisherBytes, bisher.ProfilVersion, jetzt);
        Dateiablage.AtomarSchreiben(bisherVoll, json, nurBesitzer: false);
        if (!SigniertesProfil.TryLesen(File.ReadAllBytes(bisherVoll), out var gelesen))
            return Nichts("Das geschriebene Profil ist nicht lesbar; die alte Fassung liegt in " + sicherung + ".");
        var urteil = ProfilAnnahme.Pruefen(gelesen, [oeffentlich], bisher.ProfilVersion, jetzt).Urteil;
        Zeigen("Geschrieben: " + bisherVoll + ", Version " + Zahl(neu.ProfilVersion) + ". Die alte Fassung liegt in " + sicherung + ".");
        Zeigen("Selbst geprüft gegen den öffentlichen Teil von profil.key (Kennung " + oeffentlich.Fingerabdruck.Anzeige + "): Urteil: " + urteil);
        if (urteil != ProfilUrteil.Uebernehmen) return 1;
        var versand = Path.Combine(ordner, Versandordner, Profilquelldatei);
        Dateiablage.AtomarSchreiben(versand, json, nurBesitzer: false);
        Zeigen("Kopie zum Verschicken: " + versand);
        Zeigen("Jetzt:");
        Zeigen("  - Diese Datei auf den laufenden Server nach /etc/funk/profil.json, dann funk-admin profil-neu-laden. Ein Client mit");
        Zeigen("    Profilübernahme nimmt sie beim nächsten Verbinden; einer ohne (etwa 0.2.0) braucht dafür ein neues Setup.");
        Zeigen("  - Dieselbe Datei an Claude für neue Setups. Sie ist öffentlich; profil.key bleibt auf dem Stick.");
        return 0;
    }

    private const string Profilquelldatei = "profil.json";

    /// <summary>Wo die Reserve auf dem Stick liegt (HEIMSERVER-PROXMOX.md, Abschnitt 6), in dieser Reihenfolge.</summary>
    private static readonly string[] ReserveDateien = ["reserve.crt", "reserve.key"];

    private void Zeigen(string zeile) => aus.WriteLine(zeile);

    private static string Zahl(uint z) => z.ToString(CultureInfo.InvariantCulture);

    private static string Zahl(ushort z) => z.ToString(CultureInfo.InvariantCulture);

    private static string Datum(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string PinText(Fingerabdruck p, Fingerabdruck? reserve) => p.AlsBase64() + " (Kennung " + p.Anzeige + (p == reserve ? ", Reserve" : "") + ")";

    /// <summary>Die Anfrage: eingefügt, aus profilanfrage.txt (Enter) oder keine (v, nur verlängern). <c>false</c>: abgebrochen.</summary>
    private bool AnfrageErfragen(string ordner, out Profilanfrage? anfrage)
    {
        anfrage = null;
        var datei = Path.Combine(ordner, AnfrageDatei);
        var dateiDa = File.Exists(datei);
        Zeigen("Was soll ins neue Profil?");
        Zeigen("  - die Anfragezeile vom Server-Admin einfügen (sie beginnt mit " + Profilanfrage.Kopf + ")");
        if (dateiDa) Zeigen("  - Enter: " + AnfrageDatei + " nehmen (" + datei + ")");
        Zeigen("  - v: nur verlängern, sonst bleibt alles");
        for (var i = 0; i < 5; i++)
        {
            var z = _d.Zeile("Eingabe: ");
            if (z is null)
            {
                Nichts("Abgebrochen.");
                return false;
            }
            z = z.Trim();
            if (z is "v" or "V") return true;
            string text;
            if (z.Length == 0)
            {
                if (!dateiDa)
                {
                    Zeigen("Bitte die Zeile einfügen oder v.");
                    continue;
                }
                text = File.ReadAllText(datei);
                Zeigen("Aus " + AnfrageDatei + ".");
            }
            else text = z;
            if (Profilanfrage.TryLesen(text, out anfrage, out var fehler))
            {
                Zeigen("Anfrage: " + anfrage!.Zeile);
                return true;
            }
            Zeigen(fehler + " Bitte die Zeile noch einmal einfügen.");
        }
        Nichts("Zu viele Versuche.");
        return false;
    }

    /// <summary>
    /// Die Reserve: aus <c>--reserve</c> (Zertifikat, Schlüssel oder Pin) oder aus reserve.crt beziehungsweise reserve.key neben
    /// profil.key. Steht sie nicht im bisherigen Profil, zählt sie nicht. <c>false</c> nur bei einer unlesbaren Angabe.
    /// </summary>
    private bool ReserveFinden(string? angabe, string schluesselPfad, Serverprofil bisher, out Fingerabdruck? reserve)
    {
        reserve = null;
        string? quelle = angabe;
        if (quelle is null)
        {
            var ordner = Path.GetDirectoryName(Path.GetFullPath(schluesselPfad)) ?? ".";
            quelle = ReserveDateien.Select(n => Path.Combine(ordner, n)).FirstOrDefault(File.Exists);
            if (quelle is null) return true;
        }
        if (PinAus(quelle) is not { } pin)
        {
            Nichts("--reserve " + quelle + " ist weder Zertifikat, Schlüssel noch Pin der Reserve.");
            return false;
        }
        if (bisher.PinPasst(pin)) reserve = pin;
        else Zeigen("Hinweis: Die Reserve " + pin.Anzeige + " aus " + quelle + " steht nicht im bisherigen Profil; sie zählt hier nicht als Reserve.");
        return true;
    }

    private static Fingerabdruck? PinAus(string angabe)
    {
        if (Fingerabdruck.TryAusBase64(angabe.Trim(), out var direkt)) return direkt;
        if (!File.Exists(angabe)) return null;
        try
        {
            var text = File.ReadAllText(angabe);
            if (text.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
            {
                using var z = X509Certificate2.CreateFromPem(text);
                return Fingerabdruck.AusZertifikat(z);
            }
            using var k = ECDsa.Create();
            k.ImportFromPem(text);
            return Fingerabdruck.AusSchluessel(k);
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException or IOException)
        {
            return null;
        }
    }

    private bool ReserveErfragen(List<Fingerabdruck> kandidaten, out Fingerabdruck? reserve)
    {
        reserve = null;
        if (Waehlen("Welcher Pin ist die Reserve? Nummer: ", [.. kandidaten.Select(p => PinText(p, null))]) is not { } i) return false;
        reserve = kandidaten[i];
        return true;
    }

    /// <summary>Kürzt <paramref name="liste"/> auf <paramref name="hoechstens"/>; Sarevoc wählt unter denen, die <paramref name="waehlbar"/> sind.</summary>
    private bool Begrenzen<T>(List<T> liste, int hoechstens, string was, Func<T, bool> waehlbar, Func<T, string> text)
    {
        while (liste.Count > hoechstens)
        {
            var kandidaten = liste.Where(waehlbar).ToList();
            if (kandidaten.Count == 0) return false;
            Zeigen("Ein Profil hat höchstens " + hoechstens.ToString(CultureInfo.InvariantCulture) + " " + was + ". Was fällt weg?");
            if (Waehlen("Nummer: ", [.. kandidaten.Select(text)]) is not { } i) return false;
            liste.Remove(kandidaten[i]);
        }
        return true;
    }

    private int? Waehlen(string frage, IReadOnlyList<string> optionen)
    {
        for (var i = 0; i < optionen.Count; i++) Zeigen("  " + (i + 1).ToString(CultureInfo.InvariantCulture) + ") " + optionen[i]);
        for (var versuch = 0; versuch < 5; versuch++)
        {
            var z = _d.Zeile(frage);
            if (z is null) return null;
            if (int.TryParse(z.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= optionen.Count) return n - 1;
            Zeigen("Bitte eine Zahl von 1 bis " + optionen.Count.ToString(CultureInfo.InvariantCulture) + ".");
        }
        return null;
    }

    /// <summary>Was neu ist, was bleibt, was wegfällt; dazu ein Portwechsel mit seiner Folge.</summary>
    private void Unterschied(Serverprofil alt, Serverprofil neu, Fingerabdruck? reserve)
    {
        Zeigen("Neues Profil: Version " + Zahl(neu.ProfilVersion) + ", gültig bis " + Datum(neu.GueltigBis));
        foreach (var h in neu.Hosts)
            Zeigen((alt.Hosts.Contains(h) ? "  Bleibt:    " : "  Neu:       ") + "Host " + Hostangabe.Anzeige(h));
        foreach (var h in alt.Hosts.Where(h => !neu.Hosts.Contains(h)))
            Zeigen("  Fällt weg: Host " + Hostangabe.Anzeige(h));
        foreach (var p in neu.Pins)
            Zeigen((alt.PinPasst(p) ? "  Bleibt:    " : "  Neu:       ") + "Pin " + PinText(p, reserve));
        foreach (var p in alt.Pins.Where(p => !neu.PinPasst(p)))
            Zeigen("  Fällt weg: Pin " + PinText(p, reserve));
        if (alt.TcpPort != neu.TcpPort)
            Zeigen("  TCP-Port:  " + Zahl(alt.TcpPort) + " -> " + Zahl(neu.TcpPort) + ". Ein Profil kennt nur einen Port; jeder Host darin muss unter "
                + Zahl(neu.TcpPort) + " erreichbar sein.");
    }

    /// <summary>
    /// Sichert die bisherige Fassung als profil-v&lt;N&gt;.json neben das Profil. Liegt dort schon dieselbe Fassung, bleibt sie;
    /// liegt dort etwas anderes, bekommt die neue Sicherung einen eigenen Namen. Überschrieben wird nie.
    /// </summary>
    private static string Sichern(string ordner, byte[] bytes, uint version, DateTimeOffset jetzt)
    {
        var name = Path.Combine(ordner, "profil-v" + Zahl(version) + ".json");
        if (File.Exists(name) && File.ReadAllBytes(name).AsSpan().SequenceEqual(bytes)) return name;
        if (File.Exists(name)) name = Path.Combine(ordner, "profil-v" + Zahl(version) + "-" + jetzt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
        Dateiablage.NeuSchreiben(name, bytes, nurBesitzer: false);
        return name;
    }
}
