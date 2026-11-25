// JG TacRelay - Maintainer: Sarevoc
using System.Reflection;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>Kennzahlen des Jitterpuffers aus der Audio-Schicht für den EmpfangsBericht (4.5); ohne sie 0.</summary>
public interface IEmpfangsKennzahlen
{
    double ZielVerzoegerungMs { get; }

    /// <summary>Seit dem letzten Abruf zu spät verworfene Frames.</summary>
    uint SpaetVerworfenAbholen();

    /// <summary>Seit dem letzten Abruf Unterläufe der Ausgabe.</summary>
    uint UnterlaeufeAbholen();
}

/// <summary>Namen für den Antrag ohne Code (A42): "Mustermann" als SC-Name, "Mustermann/Max" als Discord-Name.</summary>
public sealed record Anmeldedaten(string ScName, string DiscordName);

/// <summary>Einstellungen des <see cref="FunkClient"/>.</summary>
public sealed class FunkClientOptionen
{
    /// <summary>Wie die Steuerverbindung entsteht (TLS mit Pin, Testzugang, prozessintern).</summary>
    public required IVerbindungsaufbau Aufbau { get; init; }

    /// <summary>Geräteschlüssel; die Schlüsselart geht in Hello.</summary>
    public required Geraeteidentitaet Identitaet { get; init; }

    public string ClientName { get; init; } = "Funk";

    /// <summary>
    /// Die Fassung dieses Baus aus Directory.Build.props (Major.Minor.Patch, ohne Build-Anhang). Bis 0.1.0 stand hier eine feste
    /// "0.1.0"; so hätte auch ein neuer Client sich als 0.1.0 gemeldet, und <c>mindestClientVersion</c> ließe sich nie anheben.
    /// </summary>
    public static string Fassung { get; } =
        typeof(FunkClientOptionen).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>SemVer des Clients für Hello (4.7, Mindestversion); ohne Angabe <see cref="Fassung"/>.</summary>
    public string ClientVersion { get; init; } = Fassung;

    /// <summary>libopus-Version für Hello.</summary>
    public string OpusVersion { get; init; } = "1.6.1";

    /// <summary>Gerätename für die Registrierung (höchstens 40 Zeichen).</summary>
    public string Geraetename { get; init; } = "Funk-Gerät";

    /// <summary>In REGISTRIERUNG selbst einen Code anfordern (und nach Ablauf einen neuen, höchstens 3 je Verbindung).</summary>
    public bool AutomatischRegistrieren { get; init; } = true;

    /// <summary>
    /// SC-Name und Discord-Name aus dem Setup (A42, <c>anmeldung.json</c>). Gesetzt: Die Registrierung stellt einen Antrag
    /// ohne Code (3.4, P3), den ein Admin nach dem Kennungsvergleich freigibt. Ohne (und ohne <see cref="AnmeldungFragen"/>):
    /// Einmalcode für den Bot.
    /// </summary>
    public Anmeldedaten? Anmeldung { get; init; }

    /// <summary>
    /// Fragt die Namen erst, wenn der Server das Gerät nicht kennt (REGISTRIERUNG): "Anmelden als Mustermann (Discord:
    /// Mustermann/Max) - stimmt das?" (ANMELDUNG-SICHERHEIT, Ablauf des Antrags, Schritt 2). Ein freigegebenes Gerät fragt so nie.
    /// <c>null</c> als Antwort heißt: jetzt keinen Antrag stellen. Die Frist ist kürzer als die 5 Minuten der REGISTRIERUNG
    /// (4.6); danach gilt die Frage als unbeantwortet. Gilt nur, wenn <see cref="Anmeldung"/> fehlt.
    /// </summary>
    public Func<CancellationToken, Task<Anmeldedaten?>>? AnmeldungFragen { get; init; }

    /// <summary>So lange wartet die Registrierung höchstens auf <see cref="AnmeldungFragen"/>.</summary>
    public TimeSpan AnmeldungFrist { get; init; } = TimeSpan.FromSeconds(270);

    /// <summary>
    /// Rückfrage VerknuepfungBestaetigen (3.4 Schritt 5): "Mit Konto X verknüpfen?". Ohne Angabe wird abgelehnt.
    /// Frist 120 s; danach gilt "nein".
    /// </summary>
    public Func<VerknuepfungKonto, CancellationToken, Task<bool>>? Rueckfrage { get; init; }

    /// <summary>Nach Verlust automatisch neu verbinden (7.1).</summary>
    public bool Wiederverbinden { get; init; } = true;

    /// <summary>Zufall in [0, 1) für die Streuung; ohne Angabe <see cref="Random.Shared"/>.</summary>
    public Func<double>? Zufall { get; init; }

    /// <summary>Empfänger der Sprache (Audio-Schicht, Testclient).</summary>
    public ISprachEmpfaenger? Empfaenger { get; init; }

    /// <summary>Opus-Prüfung beim Empfang (5.13); die Audio-Schicht setzt libopus ein.</summary>
    public IOpusPruefer OpusPruefer { get; init; } = TocOpusPruefer.Instanz;

    /// <summary>Kennzahlen des Jitterpuffers für den EmpfangsBericht.</summary>
    public IEmpfangsKennzahlen? Kennzahlen { get; init; }

    /// <summary>Abstand der UDP-Probe im Tunnel (5.12: 30 s; nach zwei Rückfällen binnen 10 min verdoppelt, bis 5 min).</summary>
    public TimeSpan TunnelProbeAbstand { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>So lange muss die Probe sauber sein, bevor der Tunnel endet (5.12: 10 s).</summary>
    public TimeSpan TunnelProbeDauer { get; init; } = TimeSpan.FromMilliseconds(Fristen.TunnelProbeMs);

    /// <summary>EmpfangsBericht in AKTIV (4.5: alle 5 s).</summary>
    public TimeSpan BerichtAbstand { get; init; } = TimeSpan.FromSeconds(5);
}
