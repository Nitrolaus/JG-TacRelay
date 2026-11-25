// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Funk.Protokoll.Steuerung;

namespace Funk.Server.Betrieb;

/// <summary>
/// Konfiguration des Servers als JSON-Datei. Sie enthält keine Geheimnisse: Serverschlüssel und Betriebsgeheimnis
/// kommen als Dateipfade herein, unter systemd aus <c>$CREDENTIALS_DIRECTORY</c> (<c>LoadCredential</c>, 3.1).
/// Relative Pfade gelten ab dem Verzeichnis der Konfigurationsdatei; <c>${NAME}</c> wird aus der Umgebung ersetzt.
/// Unbekannte Felder sind ein Fehler, damit ein Tippfehler nicht still einen Standardwert ergibt.
/// </summary>
public sealed class ServerKonfiguration
{
    /// <summary>IP-Adressen, an die TCP und UDP gebunden werden, je Adresse beide mit gleicher IP (4.1). Pflicht.</summary>
    public List<string> Binden { get; init; } = [];

    /// <summary>Ausdrücklich an alle Adressen binden (0.0.0.0 und ::); nur mit Warnung im Logbuch (4.1).</summary>
    public bool AlleAdressen { get; init; }

    /// <summary>TCP-Bindport im Container.</summary>
    public int TcpPort { get; init; } = 8443;

    /// <summary>Öffentlicher TCP-Port am Router (steht im Serverprofil); ohne Angabe gleich <see cref="TcpPort"/>.</summary>
    public int? OeffentlicherTcpPort { get; init; }

    /// <summary>UDP-Bindport im Container.</summary>
    public int UdpPort { get; init; } = 47443;

    /// <summary>Öffentlicher UDP-Port am Router; ihn nennt Welcome (Abschnitt 0). Ohne Angabe gleich <see cref="UdpPort"/>.</summary>
    public int? OeffentlicherUdpPort { get; init; }

    /// <summary>Nur TLS 1.3, sobald niemand mehr Windows 10 nutzt (4.1).</summary>
    public bool NurTls13 { get; init; }

    /// <summary>Verzeichnis der Zustandsdateien (Konten, Lage, Komfort, Vorrangspur).</summary>
    public string Zustandsverzeichnis { get; init; } = "zustand";

    /// <summary>Serveridentität: Zertifikat und privater Schlüssel als PEM-Dateien.</summary>
    public IdentitaetsDateien Identitaet { get; init; } = new();

    /// <summary>Betriebsgeheimnis (mindestens 32 Zufallsbytes) für Schwärzung und Vorrangspur.</summary>
    public string Geheimnis { get; init; } = "geheimnis.bin";

    /// <summary>
    /// <c>funk-totp-schluessel</c> (genau 32 Byte) für die TOTP-Geheimnisse der Admins (3.9); unter systemd aus
    /// <c>$CREDENTIALS_DIRECTORY</c>, nie in der Sicherung.
    /// </summary>
    public string TotpSchluessel { get; init; } = "totp-schluessel.bin";

    /// <summary>Wer auf der Mitgliedskarte was sieht (A48, Frage 3 an Sarevoc); Voreinstellung wie im Entwurf.</summary>
    public KartenKonfiguration Mitgliedskarte { get; init; } = new();

    /// <summary>Offline signiertes Serverprofil <c>{daten, signatur}</c>, das Welcome durchreicht (3.1).</summary>
    public string? Profil { get; init; }

    /// <summary>Mindestversion der Clients (4.7).</summary>
    public string MindestClientVersion { get; init; } = "0.1.0";

    /// <summary>UNIX-Socket des Admin-Werkzeugs; ohne Angabe <c>verwaltung.sock</c> im Zustandsverzeichnis.</summary>
    public string? VerwaltungsSocket { get; init; }

    /// <summary>Unter Linux zusätzlich zu root und dem Dienstbenutzer zugelassene UIDs am Verwaltungssocket.</summary>
    public List<int> VerwaltungsUids { get; init; } = [];

    /// <summary>Weiterleiter, die Absenderadressen erhalten (Vorposten, 7.6); für sie gelten nur globale Töpfe.</summary>
    public List<string> VertrauenswuerdigeWeiterleiter { get; init; } = [];

    public GrenzenKonfiguration Grenzen { get; init; } = new();

    /// <summary>Eingebaute Sperrliste gegen Scanner, Handshake- und Antragsfluten (A51); Voreinstellungen ohne Abschnitt.</summary>
    public SperrlistenKonfiguration Sperrliste { get; init; } = new();

    public ProtokollKonfiguration Log { get; init; } = new();

    /// <summary>Nur für Test- und Dauerläufe (P-32): kürzere Rotation.</summary>
    public TestKonfiguration? Test { get; init; }

    /// <summary>Verzeichnis, gegen das relative Pfade aufgelöst werden.</summary>
    [JsonIgnore]
    public string Basisverzeichnis { get; set; } = Environment.CurrentDirectory;

    /// <summary>
    /// Aus einer Datei gelesen, aber ohne <c>totpSchluessel</c>: eine server.json aus 0.1.0 (vor dem Admin-Nachweis, 3.9).
    /// Dann gilt nicht still der Standardpfad; <see cref="Pruefen"/> nennt die Schritte. Im Code gebaute Konfigurationen
    /// (Tests, Probefunk) behalten den Standard.
    /// </summary>
    [JsonIgnore]
    public bool TotpSchluesselFehltInDatei { get; private set; }

    public int TcpPortOeffentlich => OeffentlicherTcpPort ?? TcpPort;

    public int UdpPortOeffentlich => OeffentlicherUdpPort ?? UdpPort;

    internal static readonly JsonSerializerOptions Optionen = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    /// <summary>Liest die Datei. Formfehler werfen <see cref="InvalidDataException"/>; die Regeln prüft <see cref="Pruefen"/>.</summary>
    public static ServerKonfiguration Laden(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        var voll = Path.GetFullPath(pfad);
        return Lesen(File.ReadAllBytes(voll), Path.GetDirectoryName(voll) ?? Environment.CurrentDirectory);
    }

    /// <summary>
    /// Wie <see cref="Laden"/>, aber aus Bytes, die noch nicht als Datei liegen (<c>funk-admin konfig-schreiben</c> prüft so,
    /// bevor es schreibt). <paramref name="basisverzeichnis"/> ist der Ordner, in dem die Datei liegen wird.
    /// </summary>
    public static ServerKonfiguration Lesen(byte[] bytes, string basisverzeichnis)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(basisverzeichnis);
        ServerKonfiguration? k;
        bool mitTotp;
        try
        {
            k = JsonSerializer.Deserialize<ServerKonfiguration>(bytes, Optionen);
            using var dokument = JsonDocument.Parse(bytes, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            mitTotp = dokument.RootElement.ValueKind == JsonValueKind.Object && dokument.RootElement.TryGetProperty("totpSchluessel", out _);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Konfiguration ist kein gültiges JSON: " + e.Message, e);
        }
        if (k is null) throw new InvalidDataException("Konfiguration ist leer.");
        k.Basisverzeichnis = basisverzeichnis;
        k.TotpSchluesselFehltInDatei = !mitTotp;
        return k;
    }

    /// <summary>Ersetzt <c>${NAME}</c> aus der Umgebung und löst relativ zum Basisverzeichnis auf.</summary>
    public string Aufloesen(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        var text = pfad;
        var start = text.IndexOf("${", StringComparison.Ordinal);
        while (start >= 0)
        {
            var ende = text.IndexOf('}', start);
            if (ende < 0) throw new InvalidDataException("Unvollständige Variable in Pfad.");
            var name = text[(start + 2)..ende];
            var wert = Environment.GetEnvironmentVariable(name)
                ?? throw new InvalidDataException($"Umgebungsvariable {name} ist nicht gesetzt.");
            text = string.Concat(text.AsSpan(0, start), wert, text.AsSpan(ende + 1));
            start = text.IndexOf("${", start + wert.Length, StringComparison.Ordinal);
        }
        return Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(Basisverzeichnis, text));
    }

    public string ZustandsPfad(string datei) => Path.Combine(Aufloesen(Zustandsverzeichnis), datei);

    public string SocketPfad => VerwaltungsSocket is { } s ? Aufloesen(s) : ZustandsPfad("verwaltung.sock");

    /// <summary>Die Bindadressen als IP-Adressen (bei <see cref="AlleAdressen"/> 0.0.0.0 und ::).</summary>
    public IReadOnlyList<IPAddress> Bindadressen() => AlleAdressen
        ? [IPAddress.Any, IPAddress.IPv6Any]
        : [.. Binden.Select(IPAddress.Parse)];

    /// <summary>Prüft die Regeln. Leere Liste heißt: startfähig.</summary>
    public IReadOnlyList<string> Pruefen()
    {
        var fehler = new List<string>();
        if (AlleAdressen && Binden.Count > 0)
            fehler.Add("binden und alleAdressen schließen sich aus.");
        if (!AlleAdressen && Binden.Count == 0)
            fehler.Add("Keine Bindadresse: ohne ausdrücklich konfigurierte Adressen startet der Server nicht (4.1).");
        var gesehen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in Binden)
        {
            if (!IPAddress.TryParse(b, out var ip))
            {
                fehler.Add($"binden: '{b}' ist keine IP-Adresse.");
                continue;
            }
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
                fehler.Add("binden: Alle-Adressen-Bindung nur über alleAdressen = true (4.1).");
            if (!gesehen.Add(ip.ToString())) fehler.Add("binden: Adresse doppelt.");
        }
        foreach (var w in VertrauenswuerdigeWeiterleiter)
            if (!IPAddress.TryParse(w, out _)) fehler.Add("vertrauenswuerdigeWeiterleiter: keine IP-Adresse.");
        var nullErlaubt = Test?.ZufallsPorts == true;
        if (!(nullErlaubt && TcpPort == 0)) Port(fehler, TcpPort, "tcpPort");
        if (!(nullErlaubt && UdpPort == 0)) Port(fehler, UdpPort, "udpPort");
        if (string.IsNullOrWhiteSpace(Profil)) fehler.Add("profil ist Pflicht: Welcome{FREIGEGEBEN} reicht das offline signierte Serverprofil durch (3.1).");
        if (OeffentlicherTcpPort is { } ot) Port(fehler, ot, "oeffentlicherTcpPort");
        if (OeffentlicherUdpPort is { } ou) Port(fehler, ou, "oeffentlicherUdpPort");
        if (!SemVer.TryParse(MindestClientVersion, out _)) fehler.Add("mindestClientVersion ist keine SemVer.");
        if (string.IsNullOrWhiteSpace(Identitaet.Zertifikat) || string.IsNullOrWhiteSpace(Identitaet.Schluessel))
            fehler.Add("identitaet.zertifikat und identitaet.schluessel sind Pflicht.");
        if (string.IsNullOrWhiteSpace(Geheimnis)) fehler.Add("geheimnis ist Pflicht.");
        if (string.IsNullOrWhiteSpace(TotpSchluessel)) fehler.Add("totpSchluessel ist Pflicht (funk-totp-schluessel, 3.9).");
        else if (TotpSchluesselFehltInDatei)
            fehler.Add("totpSchluessel fehlt (neu seit 0.2.0, Zweitfaktor der Admins, 3.9). Einmal: funk-admin totp-schluessel-erzeugen --datei "
                + "/etc/funk/credentials/funk-totp-schluessel, dann in server.json \"totpSchluessel\": \"${CREDENTIALS_DIRECTORY}/funk-totp-schluessel\" "
                + "eintragen und in funk-server.service LoadCredential=funk-totp-schluessel:/etc/funk/credentials/funk-totp-schluessel "
                + "(die Unit aus dem Paket hat die Zeile), danach systemctl daemon-reload.");
        fehler.AddRange(Grenzen.Pruefen());
        fehler.AddRange(Sperrliste.Pruefen());
        if (Log.StufeLesen() is null) fehler.Add("log.stufe: DEBUG, INFO, WARNUNG oder FEHLER.");
        return fehler;
    }

    private static void Port(List<string> fehler, int port, string name)
    {
        if (port is < 1 or > 65535) fehler.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: {port} liegt nicht in 1-65535."));
    }
}

/// <summary>Pfade der Serveridentität (3.1): Zertifikat und PKCS#8-Schlüssel als PEM.</summary>
public sealed class IdentitaetsDateien
{
    public string Zertifikat { get; init; } = "server.crt";

    public string Schluessel { get; init; } = "server.key";
}

/// <summary>Grenzen und Leistungswerte; Standard sind die Startwerte der Spezifikation (4.8, 7.4, 1.2).</summary>
public sealed class GrenzenKonfiguration
{
    /// <summary>Ausgangsbudget des Pacers (7.4).</summary>
    public double AusgangMbit { get; init; } = 35;

    /// <summary>Zuschlag je Paket für PPPoE und Ethernet (7.4).</summary>
    public int LeitungsZuschlag { get; init; } = 26;

    public int MaxSitzungen { get; init; } = 100;

    /// <summary>Unfreigegebene Verbindungen serverweit (4.6, 4.8).</summary>
    public int UnbekannteGlobal { get; init; } = 20;

    /// <summary>Unfreigegebene Verbindungen je Präfix.</summary>
    public int UnbekannteJePraefix { get; init; } = 2;

    /// <summary>Gleichzeitige TCP-Verbindungen je Präfix (gemeinsames NAT).</summary>
    public int VerbindungenJePraefix { get; init; } = 8;

    public double HandshakesUnbekanntJeMinute { get; init; } = 10;
    public double HandshakesUnbekanntStoss { get; init; } = 5;
    public double HandshakesVorrangJeMinute { get; init; } = 60;
    public double HandshakesVorrangStoss { get; init; } = 10;

    /// <summary>Globaler Handshake-Topf, nur CPU-Schutz; die Vorrangspur ist ausgenommen.</summary>
    public double HandshakesGlobalJeSekunde { get; init; } = 200;

    /// <summary>Empfangspuffer der UDP-Sockets (5.14: 512 KiB bis 1 MiB).</summary>
    public int UdpEmpfangspuffer { get; init; } = 1024 * 1024;

    /// <summary>Kapazität der Warteschlange zum Relay-Thread; voll heißt verwerfen (5.14).</summary>
    public int RelaisKapazitaet { get; init; } = 4096;


    internal IEnumerable<string> Pruefen()
    {
        if (AusgangMbit is <= 0 or > 100_000) yield return "grenzen.ausgangMbit außerhalb (0, 100000].";
        if (LeitungsZuschlag is < 0 or > 1000) yield return "grenzen.leitungsZuschlag außerhalb 0-1000.";
        if (MaxSitzungen is < 1 or > 10_000) yield return "grenzen.maxSitzungen außerhalb 1-10000.";
        if (UnbekannteGlobal < 0 || UnbekannteJePraefix < 0 || VerbindungenJePraefix < 1) yield return "grenzen: Verbindungsgrenzen ungültig.";
        if (HandshakesUnbekanntJeMinute <= 0 || HandshakesUnbekanntStoss < 1 || HandshakesVorrangJeMinute <= 0
            || HandshakesVorrangStoss < 1 || HandshakesGlobalJeSekunde <= 0)
            yield return "grenzen: Handshake-Töpfe müssen positiv sein.";
        if (UdpEmpfangspuffer is < 64 * 1024 or > 16 * 1024 * 1024) yield return "grenzen.udpEmpfangspuffer außerhalb 64 KiB bis 16 MiB.";
        if (RelaisKapazitaet is < 64 or > 1_000_000) yield return "grenzen.relaisKapazitaet außerhalb 64-1000000.";

    }
}

/// <summary>
/// Sichtbarkeit der Mitgliedskarte (A48, offene Frage 3 an Sarevoc). Hauptname, Zeichen und Platz sieht immer
/// jeder (wie die Belegung heute); weitere SC-Namen, Discord-Name, Verbindung und letzter Platz nur die Einsatzleitung
/// (ZUWEISEN), Admins und das Konto selbst.
/// </summary>
public sealed class KartenKonfiguration
{
    /// <summary>
    /// Mitgliederrollen und Lieblingsrollen für alle sichtbar (Voreinstellung, hilft beim Selbsteinteilen: "wer fliegt bei uns
    /// Gunner?"); <c>false</c>: nur für Einsatzleitung, Admins und das Konto selbst.
    /// </summary>
    public bool RollenFuerAlle { get; init; } = true;
}

/// <summary>Logbuch: Mindeststufe und optionale Datei (zusätzlich zu stdout).</summary>
public sealed class ProtokollKonfiguration
{
    public string Stufe { get; init; } = "INFO";

    public string? Datei { get; init; }

    public Protokollstufe? StufeLesen() => Stufe switch
    {
        "DEBUG" => Protokollstufe.Debug,
        "INFO" => Protokollstufe.Info,
        "WARNUNG" => Protokollstufe.Warnung,
        "FEHLER" => Protokollstufe.Fehler,
        _ => null,
    };
}

/// <summary>Einstellungen nur für Test- und Dauerläufe.</summary>
public sealed class TestKonfiguration
{
    /// <summary>Epochendauer in Sekunden statt 60 min (P-32).</summary>
    public int? RotationSekunden { get; init; }

    /// <summary>Port 0 zulassen: das System teilt freie Ports zu (Zusammenspieltests auf Loopback).</summary>
    public bool ZufallsPorts { get; init; }
}
