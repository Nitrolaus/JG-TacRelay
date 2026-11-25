// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;

namespace Funk.Server.Betrieb;

/// <summary>
/// Eingebaute Sperrliste (A51, Schutz gegen Botnetze, Scanner und Durchprobieren) als Abschnitt <c>sperrliste</c> in
/// server.json. Ohne den Abschnitt gelten die Voreinstellungen hier; sie sind so gewählt, dass ein Mitglied mit einem
/// gewöhnlichen Client nie hineinläuft (BETRIEB.md, Sperrliste). Werte in Punkten, Sekunden, Minuten und Stunden.
/// </summary>
public sealed class SperrlistenKonfiguration
{
    /// <summary>Sperrliste an; <c>false</c> nur zur Fehlersuche.</summary>
    public bool Aktiv { get; init; } = true;

    /// <summary>So viele Punkte innerhalb von <see cref="FensterSekunden"/> führen zur Sperre (Punkte je Anlass: <c>Sperrliste.Punkte</c>).</summary>
    public int Schwelle { get; init; } = 10;

    /// <summary>Zeitfenster, in dem Punkte zusammenkommen; danach beginnt die Zählung neu.</summary>
    public int FensterSekunden { get; init; } = 600;

    /// <summary>Dauer der ersten Sperre einer Adresse.</summary>
    public int ErsteSperreSekunden { get; init; } = 300;

    /// <summary>Jede weitere Sperre derselben Adresse dauert so viel länger (5 min, 30 min, 3 h, 18 h ...).</summary>
    public int Faktor { get; init; } = 6;

    /// <summary>Obergrenze einer Sperre.</summary>
    public int HoechstdauerStunden { get; init; } = 24;

    /// <summary>So lange nach dem Ende der letzten Sperre merkt sich die Liste die Stufe; danach beginnt eine Adresse wieder bei 5 min.</summary>
    public int VergessenStunden { get; init; } = 48;

    /// <summary>
    /// Auch während einer Sperre darf je Adresse alle so viele Sekunden eine Verbindung bis zum TLS-Handshake. So kommt ein
    /// freigegebenes Gerät hinter derselben Adresse (Familie, CGNAT) herein und hebt die Sperre auf.
    /// </summary>
    public int DurchlassSekunden { get; init; } = 30;

    /// <summary>Höchstens so viele Adressen verfolgt die Liste (Speicher). Darüber fallen zuerst ungesperrte, älteste heraus.</summary>
    public int MaxEintraege { get; init; } = 10_000;

    /// <summary>
    /// IPv6: Sind so viele /64 aus demselben /48 gesperrt, gilt die Sperre für das ganze /48 (gegen Angreifer, die ihr /64
    /// wechseln). Ebenso, wenn die /64 eines /48 zusammen <see cref="Schwelle"/> mal diesen Wert an Punkten im Fenster sammeln
    /// (Voreinstellung 40), gegen Angreifer, die je Verbindung ein neues /64 nehmen. 0 schaltet beides ab.
    /// </summary>
    public int V6Sammelschwelle { get; init; } = 4;

    /// <summary>Adressen oder Netze (CIDR), die nie gesperrt werden, etwa eine Überwachung im Heimnetz ("192.168.1.0/24").</summary>
    public List<string> Ausnahmen { get; init; } = [];

    /// <summary>
    /// Optional, Voreinstellung aus: Datei, in die bei jeder neuen Sperre eine Zeile in fester Form mit der Klartext-IP geht
    /// (für fail2ban auf dem Proxmox-Host, konfig/fail2ban). Das Logbuch bleibt ohne Klartext-IP.
    /// </summary>
    public string? KlartextDatei { get; init; }

    /// <summary>Die Ausnahmen als Netze; ungültige Einträge meldet <see cref="Pruefen"/>.</summary>
    public IReadOnlyList<IPNetwork> AusnahmeNetze()
    {
        var netze = new List<IPNetwork>(Ausnahmen.Count);
        foreach (var a in Ausnahmen)
            if (NetzLesen(a) is { } n) netze.Add(n);
        return netze;
    }

    /// <summary>Eine Adresse ("192.168.1.20") oder ein Netz ("192.168.1.0/24", "2001:db8::/48").</summary>
    public static IPNetwork? NetzLesen(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (IPNetwork.TryParse(text, out var netz)) return netz;
        if (IPAddress.TryParse(text, out var ip)) return new IPNetwork(ip, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
        return null;
    }

    internal IEnumerable<string> Pruefen()
    {
        if (Schwelle is < 1 or > 1000) yield return "sperrliste.schwelle außerhalb 1-1000.";
        if (FensterSekunden is < 10 or > 86_400) yield return "sperrliste.fensterSekunden außerhalb 10-86400.";
        if (ErsteSperreSekunden is < 10 or > 86_400) yield return "sperrliste.ersteSperreSekunden außerhalb 10-86400.";
        if (Faktor is < 1 or > 100) yield return "sperrliste.faktor außerhalb 1-100.";
        if (HoechstdauerStunden is < 1 or > 720) yield return "sperrliste.hoechstdauerStunden außerhalb 1-720.";
        else if (HoechstdauerStunden * 3600L < ErsteSperreSekunden) yield return "sperrliste.hoechstdauerStunden ist kürzer als ersteSperreSekunden.";
        if (VergessenStunden is < 1 or > 720) yield return "sperrliste.vergessenStunden außerhalb 1-720.";
        // Ohne Durchlass käme ein Mitglied hinter derselben Adresse wie ein Angreifer bis zum Ende der Sperre nicht herein.
        if (DurchlassSekunden is < 5 or > 3600) yield return "sperrliste.durchlassSekunden außerhalb 5-3600.";
        if (MaxEintraege is < 100 or > 1_000_000) yield return "sperrliste.maxEintraege außerhalb 100-1000000.";
        if (V6Sammelschwelle is < 0 or 1 or > 1000) yield return "sperrliste.v6Sammelschwelle: 0 (aus) oder 2-1000.";
        foreach (var a in Ausnahmen)
            if (NetzLesen(a) is null) yield return string.Create(CultureInfo.InvariantCulture, $"sperrliste.ausnahmen: '{a}' ist weder Adresse noch Netz (CIDR).");
        if (KlartextDatei is { } d && string.IsNullOrWhiteSpace(d)) yield return "sperrliste.klartextDatei: leer; ohne Datei null eintragen oder die Zeile weglassen.";
    }
}
