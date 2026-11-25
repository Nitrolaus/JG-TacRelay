// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Formats.Tar;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Funk.Admin;

/// <summary>
/// Format des Umzugspakets (EINRICHTEN-UMZUG-ENTWURF, "Umzugspaket"), nur mit Bordmitteln von .NET:
/// <list type="bullet">
/// <item>Innen ein tar (System.Formats.Tar): zuerst <c>MANIFEST.json</c> mit Name, Größe und SHA-256 jeder Datei, danach die
/// Dateien, nur gewöhnliche, nur mit den Namen aus <see cref="Erlaubt"/>, ohne Ordner. Beim Lesen zählt dieser Name, nie ein
/// Pfad aus dem Paket; ein Eintrag wie <c>../x</c>, ein Verweis oder eine Datei außerhalb des MANIFEST verwirft das ganze Paket.</item>
/// <item>Außen: <c>TRUMZUG1</c> (8 Byte), Iterationen (4 Byte, Big Endian), Salz (16), Nonce (12), dann das tar mit AES-256-GCM
/// verschlüsselt, Tag (16) am Ende. Der Kopf ist zusätzliche authentifizierte Information: Wer ihn ändert, macht das Paket
/// unbrauchbar, statt es zu schwächen.</item>
/// <item>Schlüssel: PBKDF2-SHA256 mit <see cref="Iterationen"/> Runden aus einer Passphrase, die das Werkzeug erzeugt: 26 Zeichen
/// Base32 (130 Bit), gezeigt in Vierergruppen. Das Paket darf über jeden Weg gehen, die Passphrase nur am Telefon.</item>
/// </list>
/// </summary>
public static class Umzugspaket
{
    /// <summary>Kennung am Anfang jeder Paketdatei.</summary>
    public static ReadOnlySpan<byte> Magie => "TRUMZUG1"u8;

    /// <summary>Runden für PBKDF2-SHA256 (Empfehlung der OWASP für SHA-256, Stand 2023).</summary>
    public const int Iterationen = 600_000;

    /// <summary>Weniger nimmt das Einspielen nicht an, mehr auch nicht (sonst rechnete ein untergeschobener Kopf ewig).</summary>
    public const int MindestIterationen = 100_000;

    public const int HoechstIterationen = 10_000_000;

    public const int SalzLaenge = 16;
    public const int NonceLaenge = 12;
    public const int TagLaenge = 16;
    public const int KopfLaenge = 8 + 4 + SalzLaenge + NonceLaenge;

    /// <summary>Größer wird ein Paket nie angenommen (journal.json wächst, aber nicht so).</summary>
    public const long MaxBytes = 256L * 1024 * 1024;

    public const string ManifestName = "MANIFEST.json";
    public const string Format = "tr-umzug 1";
    public const int PassphraseLaenge = 26;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Zustandsdateien im Paket, immer, soweit vorhanden. vorrangspur.json kommt nie mit (passt nur zum alten Geheimnis).</summary>
    public static IReadOnlyList<string> Zustand { get; } = ["lage.json", "konten.json", "komfort.json", "admins.json", "mitglieder.json", "journal.json"];

    public const string TotpName = "funk-totp-schluessel";
    public const string SchluesselName = "server.key";
    public const string ZertifikatName = "server.crt";

    /// <summary>Alles, was ein Paket enthalten darf. Das Betriebsgeheimnis (funk-geheimnis) gehört nie dazu.</summary>
    public static IReadOnlySet<string> Erlaubt { get; } = new HashSet<string>([.. Zustand, TotpName, SchluesselName, ZertifikatName], StringComparer.Ordinal);

    /// <summary>Eine neue Passphrase: 26 Zeichen aus dem Base32-Alphabet (130 Bit), gleichverteilt.</summary>
    public static string PassphraseErzeugen()
    {
        Span<char> p = stackalloc char[PassphraseLaenge];
        for (var i = 0; i < p.Length; i++) p[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(p);
    }

    /// <summary>Zum Vorlesen: Vierergruppen mit Bindestrich, ABCD-EFGH-...-YZ.</summary>
    public static string PassphraseAnzeigen(string passphrase)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        return string.Join("-", Enumerable.Range(0, (passphrase.Length + 3) / 4).Select(i => passphrase.Substring(i * 4, Math.Min(4, passphrase.Length - i * 4))));
    }

    /// <summary>
    /// So, wie man sie nach dem Telefonat abtippt: Leerzeichen und Bindestriche egal, klein oder groß egal, 0 und 1 als O und I
    /// gelesen (Base32 kennt 0, 1, 8 und 9 nicht). <c>null</c>, wenn danach nicht genau 26 Base32-Zeichen bleiben.
    /// </summary>
    public static string? PassphraseNormalisieren(string? eingabe)
    {
        if (eingabe is null) return null;
        var sb = new StringBuilder(PassphraseLaenge);
        foreach (var c in eingabe)
        {
            if (c is ' ' or '-' or '\t') continue;
            var g = char.ToUpperInvariant(c) switch
            {
                '0' => 'O',
                '1' => 'I',
                var x => x,
            };
            if (!Alphabet.Contains(g, StringComparison.Ordinal)) return null;
            sb.Append(g);
        }
        return sb.Length == PassphraseLaenge ? sb.ToString() : null;
    }

    /// <summary>Verschlüsselt <paramref name="klartext"/> (das tar) mit einer Passphrase; liefert die ganze Paketdatei.</summary>
    public static byte[] Verschluesseln(ReadOnlySpan<byte> klartext, string passphrase, int iterationen = Iterationen)
    {
        var p = PassphraseNormalisieren(passphrase) ?? throw new ArgumentException("Die Passphrase hat 26 Zeichen aus A-Z und 2-7.", nameof(passphrase));
        if (iterationen is < MindestIterationen or > HoechstIterationen) throw new ArgumentOutOfRangeException(nameof(iterationen));
        var paket = new byte[KopfLaenge + klartext.Length + TagLaenge];
        Magie.CopyTo(paket);
        BinaryPrimitives.WriteInt32BigEndian(paket.AsSpan(8, 4), iterationen);
        RandomNumberGenerator.Fill(paket.AsSpan(12, SalzLaenge + NonceLaenge));
        var kopf = paket.AsSpan(0, KopfLaenge);
        var schluessel = Ableiten(p, paket.AsSpan(12, SalzLaenge), iterationen);
        try
        {
            using var gcm = new AesGcm(schluessel, TagLaenge);
            gcm.Encrypt(paket.AsSpan(12 + SalzLaenge, NonceLaenge), klartext, paket.AsSpan(KopfLaenge, klartext.Length), paket.AsSpan(KopfLaenge + klartext.Length), kopf);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(schluessel);
        }
        return paket;
    }

    /// <summary>Ob die Bytes überhaupt ein Umzugspaket sein können (Kennung, Länge, Iterationen); sonst die Meldung.</summary>
    public static string? KopfPruefen(ReadOnlySpan<byte> paket)
    {
        if (paket.Length < KopfLaenge + TagLaenge || !paket.StartsWith(Magie))
            return "Das ist kein Umzugspaket (falsche Kennung oder zu kurz). Gemeint ist die Datei von funk-admin umzug-packen.";
        var n = BinaryPrimitives.ReadInt32BigEndian(paket.Slice(8, 4));
        if (n is < MindestIterationen or > HoechstIterationen)
            return "Der Kopf des Pakets ist verändert (Iterationen " + n.ToString(CultureInfo.InvariantCulture) + ").";
        return null;
    }

    /// <summary>Entschlüsselt; <see cref="UmzugsFehler"/> bei falscher Passphrase, Veränderung oder abgeschnittenem Paket.</summary>
    public static byte[] Entschluesseln(ReadOnlySpan<byte> paket, string passphrase)
    {
        if (KopfPruefen(paket) is { } f) throw new UmzugsFehler(f);
        var p = PassphraseNormalisieren(passphrase) ?? throw new ArgumentException("Die Passphrase hat 26 Zeichen aus A-Z und 2-7.", nameof(passphrase));
        var iterationen = BinaryPrimitives.ReadInt32BigEndian(paket.Slice(8, 4));
        var laenge = paket.Length - KopfLaenge - TagLaenge;
        var klar = new byte[laenge];
        var schluessel = Ableiten(p, paket.Slice(12, SalzLaenge), iterationen);
        try
        {
            using var gcm = new AesGcm(schluessel, TagLaenge);
            gcm.Decrypt(paket.Slice(12 + SalzLaenge, NonceLaenge), paket.Slice(KopfLaenge, laenge), paket[^TagLaenge..], klar, paket[..KopfLaenge]);
            return klar;
        }
        catch (AuthenticationTagMismatchException e)
        {
            CryptographicOperations.ZeroMemory(klar);
            throw new UmzugsFehler("Passphrase falsch, oder das Paket wurde unterwegs verändert oder abgeschnitten.", e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(schluessel);
        }
    }

    private static byte[] Ableiten(string passphrase, ReadOnlySpan<byte> salz, int iterationen)
    {
        var p = Encoding.ASCII.GetBytes(passphrase);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(p, salz, iterationen, HashAlgorithmName.SHA256, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(p);
        }
    }

    /// <summary>Das tar: zuerst das MANIFEST, dann die Dateien, jede mit Modus 0600.</summary>
    public static byte[] Packen(IReadOnlyList<Umzugsdatei> dateien, DateTimeOffset jetzt, string fassung)
    {
        ArgumentNullException.ThrowIfNull(dateien);
        foreach (var d in dateien)
            if (!Erlaubt.Contains(d.Name)) throw new ArgumentException("Gehört nicht ins Umzugspaket: " + d.Name);
        if (dateien.Select(d => d.Name).Distinct(StringComparer.Ordinal).Count() != dateien.Count) throw new ArgumentException("Eine Datei steht doppelt da.");
        var manifest = new JsonObject
        {
            ["format"] = Format,
            ["erstellt"] = jetzt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["fassung"] = fassung,
            ["dateien"] = new JsonArray([.. dateien.Select(d => (JsonNode)new JsonObject
            {
                ["name"] = d.Name,
                ["groesse"] = d.Inhalt.Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(d.Inhalt)),
            })]),
        };
        using var tar = new MemoryStream();
        using (var w = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            Eintrag(w, ManifestName, Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true })), jetzt);
            foreach (var d in dateien) Eintrag(w, d.Name, d.Inhalt, jetzt);
        }
        return tar.ToArray();
    }

    private static void Eintrag(TarWriter w, string name, byte[] inhalt, DateTimeOffset jetzt)
    {
        using var daten = new MemoryStream(inhalt, writable: false);
        w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = daten,
            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            ModificationTime = jetzt,
        });
    }

    /// <summary>
    /// Liest das tar streng: MANIFEST zuerst, Format bekannt, jeder Name erlaubt und einmal, nur gewöhnliche Dateien, Größe und
    /// SHA-256 wie im MANIFEST, keine Datei zu viel und keine zu wenig. Jeder Verstoß verwirft das ganze Paket.
    /// </summary>
    public static IReadOnlyList<Umzugsdatei> Auspacken(byte[] tar)
    {
        ArgumentNullException.ThrowIfNull(tar);
        try
        {
            using var strom = new MemoryStream(tar, writable: false);
            using var r = new TarReader(strom);
            var erster = r.GetNextEntry(copyData: true) ?? throw new UmzugsFehler("Das Paket ist leer.");
            if (!string.Equals(erster.Name, ManifestName, StringComparison.Ordinal) || erster.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                throw new UmzugsFehler("Das Paket beginnt nicht mit " + ManifestName + ".");
            var soll = ManifestLesen(Lesen(erster));
            var ist = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            while (r.GetNextEntry(copyData: true) is { } e)
            {
                if (!soll.TryGetValue(e.Name, out var s)) throw new UmzugsFehler(e.Name + " steht nicht im MANIFEST.");
                if (e.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new UmzugsFehler(e.Name + " ist keine gewöhnliche Datei (" + e.EntryType + ").");
                if (ist.ContainsKey(e.Name)) throw new UmzugsFehler(e.Name + " steht doppelt im Paket.");
                var inhalt = Lesen(e);
                if (inhalt.Length != s.Groesse || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(inhalt)), s.Sha256, StringComparison.Ordinal))
                    throw new UmzugsFehler("Prüfsumme von " + e.Name + " passt nicht zum MANIFEST.");
                ist.Add(e.Name, inhalt);
            }
            foreach (var name in soll.Keys)
                if (!ist.ContainsKey(name)) throw new UmzugsFehler(name + " fehlt im Paket, steht aber im MANIFEST.");
            return [.. soll.Keys.Select(n => new Umzugsdatei(n, ist[n]))];
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or ArgumentException or EndOfStreamException or JsonException or InvalidOperationException)
        {
            throw new UmzugsFehler("Das Innere des Pakets ist kein gültiges tar: " + e.Message, e);
        }
    }

    private sealed record Soll(long Groesse, string Sha256);

    private static byte[] Lesen(TarEntry e)
    {
        if (e.DataStream is not { } s) return [];
        if (s.Length > MaxBytes) throw new UmzugsFehler(e.Name + " ist zu groß.");
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    private static Dictionary<string, Soll> ManifestLesen(byte[] json)
    {
        using var d = JsonDocument.Parse(json);
        var w = d.RootElement;
        if (w.ValueKind != JsonValueKind.Object || !w.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() != Format)
            throw new UmzugsFehler("Unbekanntes Format im MANIFEST; dieses Werkzeug kennt " + Format + ".");
        if (!w.TryGetProperty("dateien", out var dateien) || dateien.ValueKind != JsonValueKind.Array)
            throw new UmzugsFehler("Das MANIFEST nennt keine Dateien.");
        var soll = new Dictionary<string, Soll>(StringComparer.Ordinal);
        foreach (var e in dateien.EnumerateArray())
        {
            var name = e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
            if (!Erlaubt.Contains(name)) throw new UmzugsFehler("Das MANIFEST nennt eine Datei, die nicht ins Umzugspaket gehört: " + name + ".");
            if (!e.TryGetProperty("groesse", out var g) || !g.TryGetInt64(out var groesse) || groesse < 0
                || !e.TryGetProperty("sha256", out var h) || h.ValueKind != JsonValueKind.String)
                throw new UmzugsFehler("Das MANIFEST ist unvollständig bei " + name + ".");
            if (!soll.TryAdd(name, new Soll(groesse, h.GetString()!))) throw new UmzugsFehler(name + " steht doppelt im MANIFEST.");
        }
        return soll;
    }
}

/// <summary>Eine Datei im Umzugspaket: ihr Name (aus <see cref="Umzugspaket.Erlaubt"/>) und ihr Inhalt.</summary>
public sealed record Umzugsdatei(string Name, byte[] Inhalt);

/// <summary>Das Paket passt nicht: kein Umzugspaket, falsche Passphrase, verändert, abgeschnitten oder ein Inhalt, der nicht hineingehört.</summary>
public sealed class UmzugsFehler : IOException
{
    public UmzugsFehler()
    {
    }

    public UmzugsFehler(string meldung) : base(meldung)
    {
    }

    public UmzugsFehler(string meldung, Exception innen) : base(meldung, innen)
    {
    }
}
