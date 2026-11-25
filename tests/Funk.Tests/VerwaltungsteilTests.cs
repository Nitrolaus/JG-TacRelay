// JG TacRelay - Maintainer: Sarevoc
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Funk.Client;
using Funk.Client.Dienste;
using Funk.ClientKern;
using Funk.Verwaltung;

namespace Funk.Tests;

/// <summary>
/// Admin-Teile nur für Admins (A45, ANMELDUNG-SICHERHEIT): Der normale Client enthält keinen Verwaltungscode und lädt den
/// Verwaltungsteil nur, wenn das Admin-Setup ihn nach <c>verwaltung\</c> gelegt hat und seine Prüfsumme zu der passt, die beim
/// Bau in TacRelay.dll steht. Geprüft am gebauten Client (Dateiliste, deps.json, Typen aller Programmdateien) und am
/// Lader mit echter und fremder Datei. Dazu der Admin-Schlüssel: nur TPM, ohne Rückfall.
/// </summary>
internal static class VerwaltungsteilTests
{
    /// <summary>Typen, die nur in den Verwaltungsteil oder den Server gehören, nie in den normalen Client.</summary>
    private static readonly string[] VerboteneTypen =
    [
        "Adminzugang", "TpmAdminSchluessel", "Adminschluesselwahl", "Verwaltungsdienst", "VerwaltungViewModel", "AntraegeViewModel",
        "MitgliederViewModel", "KatalogViewModel", "QrCode", "QrBild", "VerwaltungAnsicht",
        "Adminbuch", "Mitgliederbuch", "TotpTresor", "Totp",
        // Vorprüfung vor dem Einrichten (0.4.1).
        "Adminvoraussetzungen", "PcpTpmPruefung", "Tpmdeutung",
    ];

    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden.");
    }

    /// <summary>Der Ausgabeordner des Clients in derselben Konfiguration wie dieser Testlauf.</summary>
    private static string ClientAusgabe()
    {
        var konfiguration = AppContext.BaseDirectory.Contains(@"\Release\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        var ordner = Path.Combine(Wurzel(), "src", "Funk.Client", "bin", konfiguration);
        var dateien = Directory.GetFiles(ordner, "TacRelay.dll", SearchOption.AllDirectories);
        Pruefe.Wahr(dateien.Length > 0, "gebauter Client in " + ordner);
        return Path.GetDirectoryName(dateien.OrderByDescending(File.GetLastWriteTimeUtc).First())!;
    }

    [Test]
    public static void Client_OhneVerwaltungscode()
    {
        var ausgabe = ClientAusgabe();
        var alle = Directory.GetFiles(ausgabe, "*", SearchOption.AllDirectories);
        Pruefe.Falsch(alle.Any(f => Path.GetFileName(f).StartsWith("Funk.Verwaltung", StringComparison.OrdinalIgnoreCase)), "keine Funk.Verwaltung.* im Client");
        Pruefe.Falsch(Directory.Exists(Path.Combine(ausgabe, Verwaltungslader.Ordner)), "kein Ordner verwaltung\\ im Client");
        var deps = File.ReadAllText(Path.Combine(ausgabe, "TacRelay.deps.json"));
        Pruefe.Falsch(deps.Contains("Funk.Verwaltung", StringComparison.OrdinalIgnoreCase) || deps.Contains("funk-server", StringComparison.OrdinalIgnoreCase),
            "deps.json kennt weder Verwaltung noch Server");
        Pruefe.Falsch(typeof(App).Assembly.GetReferencedAssemblies().Any(r => r.Name is "Funk.Verwaltung" or "funk-server"), "TacRelay.dll verweist nicht darauf");

        var gesehen = 0;
        foreach (var datei in alle.Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            using var strom = File.OpenRead(datei);
            using var pe = new PEReader(strom);
            if (!pe.HasMetadata) continue;
            var md = pe.GetMetadataReader();
            if (!md.IsAssembly) continue;
            gesehen++;
            foreach (var h in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(h);
                var name = md.GetString(t.Name);
                var raum = md.GetString(t.Namespace);
                Pruefe.Falsch(VerboteneTypen.Contains(name, StringComparer.Ordinal) || raum.StartsWith("Funk.Verwaltung", StringComparison.Ordinal)
                    || raum.StartsWith("Funk.Server", StringComparison.Ordinal),
                    $"{Path.GetFileName(datei)}: Typ {raum}.{name} gehört nicht in den normalen Client");
            }
        }
        Pruefe.Wahr(gesehen >= 5, "Programmdateien des Clients durchgesehen: " + gesehen);

        // Das Setup-Werkzeug prüft beim Bau dieselben Typen wie dieser Test (tools\setup-bauen.ps1, $adminTypen).
        var bauen = File.ReadAllText(Path.Combine(Wurzel(), "tools", "setup-bauen.ps1"));
        var start = bauen.IndexOf("$adminTypen = @(", StringComparison.Ordinal);
        Pruefe.Wahr(start >= 0, "setup-bauen.ps1 hat eine Liste $adminTypen");
        var liste = bauen[start..bauen.IndexOf(')', start)];
        foreach (var typ in VerboteneTypen)
            Pruefe.Wahr(liste.Contains("'" + typ + "'", StringComparison.Ordinal), "setup-bauen.ps1 prüft das normale Setup auch auf " + typ);
    }

    /// <summary>
    /// Urheberrecht (CLAUDE.md, Urheberrecht; ANMELDUNG-SICHERHEIT, Client: "MIT-Bibliothek oder eigener Code, Eintrag in
    /// THIRD-PARTY-NOTICES Pflicht"): Der QR-Code ist eine Übertragung der QR Code generator library von Project Nayuki (MIT).
    /// Quelltext und THIRD-PARTY-NOTICES nennen Herkunft und Lizenz; der Lizenztext geht mit jedem Setup mit.
    /// </summary>
    [Test]
    public static void QrCode_HerkunftUndLizenzGenannt()
    {
        var quelle = File.ReadAllText(Path.Combine(Wurzel(), "src", "Funk.Verwaltung", "QrCode.cs"));
        Pruefe.Wahr(quelle.Contains("Project Nayuki", StringComparison.Ordinal) && quelle.Contains("MIT", StringComparison.Ordinal), "QrCode.cs nennt Herkunft und Lizenz");
        Pruefe.Falsch(quelle.Contains("selbst gebaut statt aus einer Bibliothek", StringComparison.Ordinal), "keine Behauptung, es sei Eigenbau");
        var notices = File.ReadAllText(Path.Combine(Wurzel(), "THIRD-PARTY-NOTICES.txt"));
        Pruefe.Wahr(notices.Contains("Project Nayuki", StringComparison.Ordinal) && notices.Contains("Funk.Verwaltung.dll", StringComparison.Ordinal)
            && notices.Contains("Permission is hereby granted, free of charge", StringComparison.Ordinal), "THIRD-PARTY-NOTICES mit Copyright und MIT-Text");
    }

    [Test]
    public static void Client_KenntDiePruefsummeDesVerwaltungsteils()
    {
        var passende = Verwaltungslader.Passende(typeof(App).Assembly);
        var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Einstieg).Assembly.Location)));
        Pruefe.Wahr(passende.Count == 1 && passende[0] == sha, "VerwaltungSha256 in TacRelay.dll = SHA-256 der gebauten Funk.Verwaltung.dll: "
            + string.Join(",", passende) + " / " + sha);
        var gebaut = Directory.GetFiles(Path.Combine(Wurzel(), "src", "Funk.Verwaltung", "bin"), "Funk.Verwaltung.dll", SearchOption.AllDirectories)
            .Select(f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))));
        Pruefe.Wahr(gebaut.Contains(sha), "dieselbe Datei liegt in src\\Funk.Verwaltung\\bin, woher das Admin-Setup sie nimmt");
    }

    [Test]
    public static void Verwaltungslader_NurDieGepruefteDatei()
    {
        using var ordner = new Oberflaeche.Testordner();
        var wirt = new Wirt();
        var ohne = Verwaltungslader.Laden(ordner.Pfad, [], wirt);
        Pruefe.Wahr(ohne.Teil is null && ohne.Hinweis is null, "ohne Admin-Setup: kein Teil und kein Hinweis");

        var echt = File.ReadAllBytes(typeof(Einstieg).Assembly.Location);
        var sha = Convert.ToHexStringLower(SHA256.HashData(echt));
        Directory.CreateDirectory(Path.Combine(ordner.Pfad, Verwaltungslader.Ordner));
        var datei = Path.Combine(ordner.Pfad, Verwaltungslader.Ordner, Verwaltungslader.Datei);
        File.WriteAllBytes(datei, echt);
        var geladen = 0;
        Assembly Laden(byte[] bytes)
        {
            geladen++;
            Pruefe.BytesGleich(echt, bytes, "geladen werden genau die geprüften Bytes");
            return typeof(Einstieg).Assembly;
        }

        var fremd = Verwaltungslader.Laden(ordner.Pfad, [new string('0', 64)], wirt, Laden);
        Pruefe.Wahr(fremd.Teil is null && fremd.Hinweis == Verwaltungslader.PasstNicht && geladen == 0, "andere Prüfsumme: nicht geladen, Hinweis");
        var leer = Verwaltungslader.Laden(ordner.Pfad, [], wirt, Laden);
        Pruefe.Wahr(leer.Teil is null && leer.Hinweis == Verwaltungslader.PasstNicht && geladen == 0, "Fassung ohne Liste: nicht geladen");

        var gut = Verwaltungslader.Laden(ordner.Pfad, [sha], wirt, Laden);
        Pruefe.Wahr(gut.Teil is { Titel: "Verwaltung" } && gut.Hinweis is null && geladen == 1, "passende Datei: geladen, Einstieg aus dem Attribut");
        gut.Teil!.Dispose();

        // Ein Byte anders: nicht geladen.
        echt[^1] ^= 0xFF;
        File.WriteAllBytes(datei, echt);
        Pruefe.Wahr(Verwaltungslader.Laden(ordner.Pfad, [sha], wirt, Laden) is { Teil: null, Hinweis: Verwaltungslader.PasstNicht } && geladen == 1, "verändert: nicht geladen");

        // Eine Bibliothek ohne Einstieg: nicht geladen, sagt es.
        var ohneEinstieg = Verwaltungslader.Laden(ordner.Pfad, [Convert.ToHexStringLower(SHA256.HashData(echt))], wirt, _ => typeof(Profilquelle).Assembly);
        Pruefe.Wahr(ohneEinstieg is { Teil: null, Hinweis: Verwaltungslader.PasstNicht }, "ohne Verwaltungsteil-Attribut: nicht geladen");

        // Metadaten: nur 64 Hexzeichen, klein geschrieben, jede einmal.
        Pruefe.Wahr(Verwaltungslader.Passende(typeof(App).Assembly).All(p => p.Length == 64 && string.Equals(p, p.ToLowerInvariant(), StringComparison.Ordinal)),
            "nur volle Prüfsummen, klein geschrieben");
    }

    [Test]
    public static void Adminschluessel_NurTpmOhneRueckfall()
    {
        var f = Pruefe.Wirft<InvalidOperationException>(() => Adminschluesselwahl.NurTpm(() => throw new CryptographicException("kein TPM")), "ohne TPM keine Verwaltung");
        Pruefe.Gleich(Adminschluesselwahl.OhneTpm, f.Message, "lesbarer Satz");
        // 0.4.1: Meldet Windows einen Code, steht er dabei, damit sich ein Fehlschlag am echten PC einordnen lässt.
        var mitCode = Pruefe.Wirft<InvalidOperationException>(() => Adminschluesselwahl.NurTpm(() => throw new CryptographicException(unchecked((int)0x80090030))), "mit Code");
        Pruefe.Gleich(Adminschluesselwahl.OhneTpm + " (Windows meldet 0x80090030.)", mitCode.Message, "Code von Windows im Satz");
        Pruefe.Wirft<InvalidOperationException>(() => Adminschluesselwahl.NurTpm(() => throw new PlatformNotSupportedException()), "auch ohne Plattform");

        // Im Quelltext kein Weg in den Software-Speicher: Der Admin-Schlüssel nennt nur den Platform Crypto Provider.
        var quelle = File.ReadAllText(Path.Combine(Wurzel(), "src", "Funk.Verwaltung", "AdminSchluessel.cs"));
        Pruefe.Falsch(quelle.Contains("SoftwareKeyStorageProvider", StringComparison.Ordinal), "kein Software-Speicher im Admin-Schlüssel");
        Pruefe.Wahr(quelle.Contains("CngProvider.MicrosoftPlatformCryptoProvider, Name", StringComparison.Ordinal), "angelegt im TPM unter festem Namen");

        // Die Parameter an einem flüchtigen Software-Schlüssel (kein Name, nichts bleibt): P-256, nicht exportierbar, nur Signieren.
        using var probe = TpmAdminSchluessel.Parameter.Erzeugen(System.Security.Cryptography.CngProvider.MicrosoftSoftwareKeyStorageProvider, null);
        Pruefe.Gleich(256, probe.KeySize, "P-256");
        Pruefe.Gleich(System.Security.Cryptography.CngExportPolicies.None, probe.Key.ExportPolicy, "nicht exportierbar");
        Pruefe.Gleich(System.Security.Cryptography.CngKeyUsages.Signing, probe.Key.KeyUsage, "nur Signieren");
        Pruefe.Wirft<CryptographicException>(() => probe.ExportECPrivateKey(), "privater Teil bleibt drin");
        Pruefe.Wahr(Adminschluesselwahl.Kennung(probe).Length > 10, "Kennung zum Vorlesen");
    }

    /// <summary>Wirt ohne laufenden Client: Arbeit läuft sofort.</summary>
    private sealed class Wirt : IVerwaltungsWirt
    {
        public FunkClient? Client => null;

        public event Action? Geaendert;

        public void Einreihen(Action arbeit) => arbeit();

        public void Ausloesen() => Geaendert?.Invoke();
    }
}
