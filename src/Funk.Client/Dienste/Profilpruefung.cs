// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Funk.Protokoll.Identitaet;

namespace Funk.Client.Dienste;

/// <summary>Ein eingefügter Profilschlüssel nach der Prüfung: gültig mit SPKI (Base64) und Kennung, sonst ein Fehlertext.</summary>
public sealed record SchluesselEingabe(string? Spki, string? Kennung, string? Fehler)
{
    public static SchluesselEingabe Leer { get; } = new(null, null, null);

    public bool Gueltig => Spki is not null;
}

/// <summary>
/// Liest einen Profilschlüssel, wie Sarevoc ihn aus der Ausgabe von <c>funk-admin profilschluessel-erzeugen</c> kopiert: die Zeile
/// mit dem öffentlichen Teil (SPKI in Base64, 124 Zeichen, beginnt mit MFkw). Nachsichtig bei dem, was beim Kopieren passiert
/// (Leerraum, Zeilenumbruch mitten im Schlüssel, die Zeilen davor und danach, Anführungszeichen, PEM-Rahmen), streng beim
/// Schlüssel selbst: nur kanonisches P-256, genau die Form, die <see cref="Profilschluessel.AusSpki"/> und die
/// Verbindungsfabrik annehmen (3.1). Die Kennung ist der Fingerabdruck in Anzeigeform, wie funk-admin ihn ausgibt.
/// </summary>
public static class ProfilschluesselText
{
    /// <summary>So beginnt jeder P-256-Schlüssel in Base64: der feste Kopf des SPKI (<see cref="P256Spki"/>).</summary>
    public const string Anfang = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE";

    /// <summary>Länge in Base64: 91 Byte SPKI.</summary>
    public const int Laenge = 124;

    private const string Hinweis = "Kopiere die Zeile unter \"Öffentlicher Teil (SPKI, Base64)\" aus der Ausgabe von funk-admin profilschluessel-erzeugen.";

    public static SchluesselEingabe Pruefen(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SchluesselEingabe.Leer;
        if (text.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            return Fehler("Das ist der private Profilschlüssel. Der bleibt auf deinem Stick und gehört nie in den Client. Hier gehört nur der öffentliche Teil hin (die Zeile, die mit MFkw beginnt).");
        var kandidat = Herausloesen(text, out var mehrdeutig);
        if (kandidat is null)
            return Fehler(mehrdeutig ? "Darin steht kein Profilschlüssel. " + Hinweis : "Das ist kein Profilschlüssel. " + Hinweis);
        if (kandidat.Any(c => !IstBase64(c)))
            return Fehler("Das ist kein Profilschlüssel: Darin dürfen nur Buchstaben, Ziffern und + / = stehen. " + Hinweis);
        if (Fingerabdruck.TryAusBase64(kandidat, out _))
            return Fehler("Das ist ein Pin (44 Zeichen), kein Profilschlüssel. Die Pins stehen schon im Serverprofil. Hier gehört der öffentliche Teil des Profilschlüssels hin: 124 Zeichen, beginnt mit MFkw.");
        if (!kandidat.StartsWith("MFkw", StringComparison.Ordinal))
            return Fehler("Das ist kein Profilschlüssel: Er beginnt mit MFkw und ist 124 Zeichen lang. " + Hinweis);
        if (kandidat.Length < Laenge)
            return Fehler("Der Profilschlüssel ist unvollständig: " + kandidat.Length.ToString(CultureInfo.CurrentCulture) + " von 124 Zeichen. Kopiere die ganze Zeile.");
        if (kandidat.Length > Laenge)
            return Fehler("Der Profilschlüssel ist zu lang: " + kandidat.Length.ToString(CultureInfo.CurrentCulture) + " statt 124 Zeichen. Ist etwas mitkopiert worden?");

        var bytes = new byte[P256Spki.Laenge];
        var lesbar = Convert.TryFromBase64String(kandidat, bytes, out var n) && n == P256Spki.Laenge
            && string.Equals(Convert.ToBase64String(bytes), kandidat, StringComparison.Ordinal);
        if (!lesbar || !P256Spki.IstKanonisch(bytes))
            return Fehler("Das ist kein P-256-Profilschlüssel, wie funk-admin ihn erzeugt. " + Hinweis);
        if (!P256Spki.IstGueltig(bytes))
            return Fehler("Der Profilschlüssel ist beschädigt, vermutlich ist ein Zeichen falsch. Kopiere ihn noch einmal.");
        var kennung = Fingerabdruck.AusSpki(bytes).Anzeige;
        // Steht der Fingerabdruck aus der Ausgabe von funk-admin mit im Text, muss er passen.
        if (GenannteKennung(text) is { } genannt && !string.Equals(genannt, kennung, StringComparison.OrdinalIgnoreCase))
            return Fehler("Schlüssel und Fingerabdruck im eingefügten Text passen nicht zusammen (" + genannt + " genannt, " + kennung + " berechnet). Kopiere die Zeilen noch einmal.");
        return new SchluesselEingabe(kandidat, kennung, null);
    }

    /// <summary>Kennung eines gespeicherten Schlüssels, oder <c>null</c>, wenn er kein gültiger Profilschlüssel ist.</summary>
    public static string? Kennung(string? spki) => Pruefen(spki) is { Gueltig: true } e ? e.Kennung : null;

    private static SchluesselEingabe Fehler(string text) => new(null, null, text);

    private static bool IstBase64(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '=';

    /// <summary>
    /// Der Schlüssel aus dem Text: das Wort, das mit MFkw beginnt, samt der Base64-Wörter direkt dahinter, bis 124 Zeichen
    /// erreicht sind (ein Umbruch des Terminals). Ohne MFkw nur dann das eine Wort, wenn der Text genau eines enthält.
    /// </summary>
    private static string? Herausloesen(string text, out bool mehrdeutig)
    {
        var woerter = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.StartsWith("-----", StringComparison.Ordinal))
            .Select(w => w.Trim('"', '\'', '`', '\u201E', '\u201C', '\u201D'))
            .Where(w => w.Length > 0)
            .ToList();
        mehrdeutig = woerter.Count > 1;
        var start = woerter.FindIndex(w => w.StartsWith("MFkw", StringComparison.Ordinal) && w.All(IstBase64));
        if (start < 0) return woerter.Count == 1 ? woerter[0] : null;
        var schluessel = woerter[start];
        for (var i = start + 1; i < woerter.Count && schluessel.Length < Laenge && woerter[i].All(IstBase64); i++) schluessel += woerter[i];
        return schluessel;
    }

    /// <summary>Die Anzeigeform hinter "Fingerabdruck:", falls der Text sie enthält.</summary>
    private static string? GenannteKennung(string text)
    {
        var i = text.IndexOf("Fingerabdruck:", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var rest = text[(i + "Fingerabdruck:".Length)..].TrimStart();
        return rest.Length >= Fingerabdruck.AnzeigeLaenge ? rest[..Fingerabdruck.AnzeigeLaenge] : null;
    }
}

/// <summary>
/// Was die Prüfung eines Serverprofils vor dem Verbinden ergab, wie <c>funk-admin profil-pruefen</c>. <see cref="Profil"/>
/// gibt es nur, wenn die Signatur mit einem eingetragenen Profilschlüssel stimmt: Unsigniertes wird nicht gelesen, auch nicht
/// für die Anzeige (3.1).
/// </summary>
public sealed record Profilbefund
{
    /// <summary>
    /// Was nicht stimmt, für Funker: ohne Pfad und ohne Ausnahmetext, der erste Satz (bis ". ") steht allein; <c>null</c>, wenn
    /// das Profil benutzt werden kann. Die Fehlernummer steht nicht darin, sondern in <see cref="Nummer"/>.
    /// </summary>
    public string? Fehler { get; init; }

    /// <summary>Fehlernummer aus 01-09 (<see cref="Fehlernummern"/>), etwa "02"; die Oberfläche hängt sie an. <c>null</c> ohne Fehler.</summary>
    public string? Nummer { get; init; }

    /// <summary>Pfad der Profildatei oder Meldung von Windows zu <see cref="Fehler"/>; nur für "Details" im Admin-Setup, nie Funkern zeigen.</summary>
    public string? Einzelheit { get; init; }

    /// <summary>Ohne Profildatei oder ohne gültigen Profilschlüssel lässt sich nichts prüfen; <see cref="Fehler"/> sagt dann, was fehlt.</summary>
    public bool Unvollstaendig { get; init; }

    public Serverprofil? Profil { get; init; }

    /// <summary>SPKI (Base64) des eingetragenen Schlüssels, dessen Signatur gilt.</summary>
    public string? Schluessel { get; init; }

    /// <summary>Kennung dieses Schlüssels.</summary>
    public string? Kennung { get; init; }

    /// <summary>Nach <c>gueltigBis</c>: Das Profil gilt weiter, der Client warnt (3.1).</summary>
    public bool Abgelaufen { get; init; }

    /// <summary>Woher das gültige Profil kommt: aus dem Setup (Programmordner) oder vom Server übernommen (Ablage, F1).</summary>
    public Profilherkunft Herkunft { get; init; }

    /// <summary>Die Datei, aus der das Profil gelesen wurde.</summary>
    public string? Datei { get; init; }

    public bool Gueltig => Profil is not null;
}

/// <summary>Woher ein gültiges Serverprofil kommt.</summary>
public enum Profilherkunft
{
    /// <summary><c>profil.json</c> im Programmordner, gebracht vom Setup.</summary>
    Setup,

    /// <summary><c>serverprofil.json</c> neben den Einstellungen, nach der Freigabe vom Server übernommen (F1).</summary>
    Server,
}

/// <summary>
/// Prüft das Serverprofil gegen die Profilschlüssel wie <c>funk-admin profil-pruefen</c>, ohne Netz und ohne
/// Schlüsselspeicher. Beides kommt mit dem Client (<see cref="Profilquelle"/>); die Sätze sagen deshalb, was am Setup fehlt.
/// </summary>
public static class Profilpruefung
{
    /// <summary>Obergrenze für die Datei; ein Profil hat weit unter 2 KiB.</summary>
    public const long MaxBytes = 64 * 1024;

    /// <summary>
    /// Prüft eine Profildatei. Jeder Fehler trägt eine Nummer aus 01-09 (<see cref="Profilbefund.Nummer"/>, auch die offenen Fälle
    /// ohne Profil oder Schlüssel); der Satz nennt keinen Pfad und keinen Ausnahmetext, beides steht in <see cref="Profilbefund.Einzelheit"/>.
    /// </summary>
    public static Profilbefund Pruefen(string? pfad, IEnumerable<string> schluessel, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        if (string.IsNullOrWhiteSpace(pfad))
        {
            return Fehlerbefund(Fehlernummern.ProfilFehlt, "Diese Fassung bringt kein Serverprofil mit. Bis ein Setup vom Admin es mitbringt, geht nur der Probefunk.",
                null) with { Unvollstaendig = true };
        }
        byte[] bytes;
        try
        {
            if (!File.Exists(pfad))
            {
                return Fehlerbefund(Fehlernummern.ProfildateiFehlt, "Das Serverprofil gibt es nicht (mehr). Führ das Setup noch einmal aus (Reparieren); "
                    + "bis dahin geht nur der Probefunk.", pfad) with { Unvollstaendig = true };
            }
            var laenge = new FileInfo(pfad).Length;
            if (laenge > MaxBytes)
            {
                return Fehlerbefund(Fehlernummern.ProfilZuGross, "Das Serverprofil ist unbrauchbar, die Datei ist viel zu groß. " + Reparieren,
                    pfad + ", " + laenge.ToString(CultureInfo.InvariantCulture) + " Byte");
            }
            bytes = File.ReadAllBytes(pfad);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Fehlerbefund(Fehlernummern.ProfilUnlesbar, "Das Serverprofil lässt sich nicht lesen. " + Reparieren, ex.Message);
        }
        // Ein Editor unter Windows setzt beim Speichern gern eine Byte-Reihenfolge-Marke davor; die gehört nicht zum Profil.
        var inhalt = bytes.AsSpan();
        if (inhalt.StartsWith("\uFEFF"u8)) inhalt = inhalt[3..];
        if (!SigniertesProfil.TryLesen(inhalt, out var angebot))
        {
            // Unsigniert: die Profildaten (daten.json) statt profil.json von funk-admin profil-signieren; das sagt die Nummer dem Admin.
            return Serverprofil.TryLesen(inhalt, out _)
                ? Fehlerbefund(Fehlernummern.ProfilUnsigniert, "Das Serverprofil ist nicht signiert. Dem Setup liegt die falsche Datei bei, "
                    + "sag deinem Admin die Fehlernummer.", pfad)
                : Fehlerbefund(Fehlernummern.ProfilKeinProfil, "Die Profildatei ist kein signiertes Serverprofil. " + Reparieren, pfad);
        }

        var gueltige = schluessel.Select(s => ProfilschluesselText.Pruefen(s)).Where(e => e.Gueltig).DistinctBy(e => e.Spki).ToList();
        if (gueltige.Count == 0)
        {
            return Fehlerbefund(Fehlernummern.ProfilschluesselFehlt, "In diese Fassung ist kein Profilschlüssel eingebaut. Ohne ihn lässt sich nicht prüfen, "
                + "ob das Profil echt ist; bis ein Setup vom Admin ihn mitbringt, geht nur der Probefunk.", null) with { Unvollstaendig = true };
        }
        foreach (var e in gueltige)
        {
            using var s = Profilschluessel.AusSpki(Convert.FromBase64String(e.Spki!));
            if (!s.Prueft(angebot)) continue;
            // Dieselbe Annahmeregel wie im Admin-Werkzeug (3.1): erst die Signatur, dann der Inhalt.
            var p = ProfilAnnahme.Pruefen(angebot, [s], null, jetzt);
            if (p.Profil is null)
            {
                return Fehlerbefund(Fehlernummern.ProfilInhaltUngueltig, "Das Serverprofil ist richtig signiert, aber sein Inhalt ist ungültig. "
                    + "Sag deinem Admin die Fehlernummer, er muss es neu schreiben.", pfad) with { Kennung = e.Kennung };
            }
            return new Profilbefund { Profil = p.Profil, Schluessel = e.Spki, Kennung = e.Kennung, Abgelaufen = p.Abgelaufen };
        }
        return Fehlerbefund(Fehlernummern.ProfilFremd, "Die Signatur des Serverprofils passt zu keinem eingebauten Profilschlüssel. Profil und Programm "
            + "gehören nicht zusammen: Führ das Setup noch einmal aus (Reparieren). Hilft das nicht, war das Profil nicht echt; sag deinem Admin die Fehlernummer.",
            pfad);
    }

    private const string Reparieren = "Führ das Setup noch einmal aus (Reparieren); hilft das nicht, sag deinem Admin die Fehlernummer.";

    private static Profilbefund Fehlerbefund(string nummer, string fehler, string? einzelheit) => new() { Fehler = fehler, Nummer = nummer, Einzelheit = einzelheit };

    /// <summary>
    /// Das Profil, mit dem der Client verbindet (F1): das höchste gültige aus dem Programmordner (Setup) und der Ablage
    /// (<see cref="Profilquelle.Ablage"/>, vom Server übernommen). Beide werden bei jedem Aufruf neu gelesen und gegen die
    /// eingebauten Schlüssel geprüft; eine Ablage, die fehlt, kaputt oder fremd signiert ist, zählt nicht, ohne Meldung: Dann
    /// gilt das Setup-Profil, wie vor der ersten Übernahme. Bei gleicher Version gilt das Setup. Der Befund für das Setup-Profil
    /// trägt dessen Sätze, wenn die Ablage nichts Gültiges hat.
    /// </summary>
    public static Profilbefund Waehlen(Profilquelle quelle, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(quelle);
        var setup = Pruefen(quelle.Datei, quelle.Schluessel, jetzt) with { Datei = Voll(quelle.Datei) };
        if (quelle.Ablage is not { } ablage || !Vorhanden(ablage)) return setup;
        var server = Pruefen(ablage, quelle.Schluessel, jetzt);
        if (server.Profil is not { } neu) return setup;
        if (setup.Profil is { } alt && alt.ProfilVersion >= neu.ProfilVersion) return setup;
        return server with { Herkunft = Profilherkunft.Server, Datei = Voll(ablage) };
    }

    private static string? Voll(string? pfad)
    {
        if (string.IsNullOrWhiteSpace(pfad)) return null;
        try
        {
            return Path.GetFullPath(pfad);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return pfad;
        }
    }

    private static bool Vorhanden(string pfad)
    {
        try
        {
            return File.Exists(pfad);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Hosts mit ihren Familien für die Anzeige, etwa "funk.example.org (nur IPv4)".</summary>
    public static string Hosts(Serverprofil profil)
    {
        ArgumentNullException.ThrowIfNull(profil);
        return string.Join(", ", profil.Hosts.Select(h => h.Name + (h.IPv4 && h.IPv6 ? " (IPv4 und IPv6)" : h.IPv6 ? " (nur IPv6)" : " (nur IPv4)")));
    }

    /// <summary>Ablaufdatum in Ortszeit, etwa 29.09.2027.</summary>
    public static string GueltigBis(Serverprofil profil)
    {
        ArgumentNullException.ThrowIfNull(profil);
        return DateTimeOffset.FromUnixTimeMilliseconds(profil.GueltigBis).ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Die optionale Adresse statt der Hosts aus dem Profil (Heimphase, eigenes Netz): ein Name oder eine IP-Adresse, wahlweise
/// mit Port (<c>192.168.1.250:8443</c>, <c>[2001:db8::5]:8443</c>). Geprüft wird trotzdem der Pin aus dem Profil.
/// </summary>
public readonly record struct Adressangabe(string Host, ushort? Port)
{
    /// <summary>Liest die Angabe. Leer heißt: keine Adresse, die Hosts aus dem Profil gelten.</summary>
    public static bool TryLesen(string? text, out Adressangabe? adresse, out string? fehler)
    {
        adresse = null;
        fehler = null;
        var t = text?.Trim() ?? "";
        if (t.Length == 0) return true;
        if (t.Contains("://", StringComparison.Ordinal))
            return Falsch("Nur der Name oder die IP-Adresse, ohne http:// oder https:// davor.", out fehler);
        if (t.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '@'))
            return Falsch("In der Adresse stehen Zeichen, die dort nicht hingehören. Gemeint ist ein Name wie funk.example.org oder eine IP-Adresse.", out fehler);

        var host = t;
        string? portText = null;
        if (t.StartsWith('['))
        {
            var zu = t.IndexOf(']', StringComparison.Ordinal);
            if (zu < 0) return Falsch("Bei einer IPv6-Adresse in eckigen Klammern fehlt die schließende Klammer.", out fehler);
            host = t[1..zu];
            var rest = t[(zu + 1)..];
            if (rest.Length > 0)
            {
                if (!rest.StartsWith(':')) return Falsch("Hinter der IPv6-Adresse darf nur :Port stehen.", out fehler);
                portText = rest[1..];
            }
            if (!IPAddress.TryParse(host, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
                return Falsch("In den eckigen Klammern steht keine IPv6-Adresse.", out fehler);
        }
        else if (t.Count(c => c == ':') == 1)
        {
            var i = t.IndexOf(':', StringComparison.Ordinal);
            host = t[..i];
            portText = t[(i + 1)..];
        }

        ushort? port = null;
        if (portText is not null)
        {
            if (!ushort.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p == 0)
                return Falsch("Der Port hinter dem Doppelpunkt muss eine Zahl von 1 bis 65535 sein.", out fehler);
            port = p;
        }
        // Nur Ziffern und Punkte, aber keine volle IPv4 ("1", "192.168.178"): Windows läse das als verkürzte IP-Adresse.
        var nurZiffern = host.All(c => char.IsAsciiDigit(c) || c == '.');
        if (!IstIp(host) && (nurZiffern || !Serverprofil.HostnameGueltig(host)))
            return Falsch("Das ist weder ein gültiger Name wie funk.example.org noch eine IP-Adresse wie 192.168.1.250.", out fehler);
        adresse = new Adressangabe(host, port);
        return true;
    }

    /// <summary>IPv4 nur in der vollen Form mit drei Punkten (IPAddress liest sonst auch "1" als 0.0.0.1), IPv6 mit Doppelpunkt.</summary>
    private static bool IstIp(string text) =>
        IPAddress.TryParse(text, out var ip)
        && (ip.AddressFamily == AddressFamily.InterNetworkV6 ? text.Contains(':', StringComparison.Ordinal) : text.Count(c => c == '.') == 3);

    private static bool Falsch(string text, out string? fehler)
    {
        fehler = text;
        return false;
    }
}
