// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Funk.Server.Betrieb;

/// <summary>Stufen des Serverprotokolls.</summary>
public enum Protokollstufe
{
    Debug,
    Info,
    Warnung,
    Fehler,
}

/// <summary>
/// Pseudonymisierung für das Logbuch (7.5, P-46): IP-Adressen, Präfixe und Konto-IDs erscheinen nur als gekürzter
/// HMAC-SHA256-Wert. Der Schlüssel stammt aus dem Geheimnis (per <c>LoadCredential</c>) und wird nie ausgegeben.
/// Schlüssel, IVs, Einmalcodes und Opus stehen nie im Logbuch; dafür gibt es hier bewusst keine Methode.
/// Threadsicher.
/// </summary>
public sealed class Schwaerzung
{
    private readonly byte[] _schluessel;

    public Schwaerzung(ReadOnlySpan<byte> schluessel)
    {
        if (schluessel.Length < 16) throw new ArgumentException("Der Schlüssel der Schwärzung braucht mindestens 16 Byte.", nameof(schluessel));
        _schluessel = schluessel.ToArray();
    }

    /// <summary>Für Tests: zufälliger Schlüssel.</summary>
    public static Schwaerzung Zufaellig() => new(RandomNumberGenerator.GetBytes(32));

    public string Ip(IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        return Kurz("ip", Betrieb.Adresse.Aus(ip, 0).Ip, 0);
    }

    public string Adresse(Adresse a) => Kurz(a.V6 ? "ip6" : "ip", a.Ip, a.Port);

    public string Praefix(Praefix p) => Kurz(p.V6 ? "p6" : "p4", p.Wert, 0);

    /// <summary>Ein IPv6-/48 der Sammelsperre (Sperrliste); <c>g6</c> statt <c>p6</c>, damit es nie wie ein /64 mit gleichen Bits aussieht.</summary>
    public string Gruppe(Praefix p48) => Kurz("g6", p48.Wert, 0);

    public string Konto(uint kontoId) => Kurz("k", kontoId, 0);

    private string Kurz(string art, UInt128 wert, ushort port)
    {
        Span<byte> eingabe = stackalloc byte[20];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt128BigEndian(eingabe, wert);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(eingabe[16..], port);
        eingabe[18] = (byte)art.Length;
        eingabe[19] = (byte)art[0];
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(_schluessel, eingabe, mac);
        var text = Convert.ToHexStringLower(mac[..5]);
        return port == 0 ? art + "#" + text : string.Create(CultureInfo.InvariantCulture, $"{art}#{text}:{port}");
    }
}

/// <summary>
/// Schlichtes Serverprotokoll ohne Fremdbibliothek: eine Zeile je Eintrag mit UTC-Zeit und Stufe, nach stdout
/// (unter systemd ins Journal) und optional in eine Datei. Die Aufrufer geben Adressen und Konto-IDs nur über
/// <see cref="Schwaerzung"/> hinein. Threadsicher.
/// </summary>
public sealed class Logbuch : IDisposable
{
    private readonly Lock _sperre = new();
    private readonly TextWriter? _konsole;
    private readonly StreamWriter? _datei;
    private readonly Action<string>? _abgriff;

    public Logbuch(Protokollstufe mindestens, Schwaerzung schwaerzung, TextWriter? konsole = null, string? datei = null,
        Action<string>? abgriff = null)
    {
        ArgumentNullException.ThrowIfNull(schwaerzung);
        Mindestens = mindestens;
        Schwaerzung = schwaerzung;
        _konsole = konsole;
        _abgriff = abgriff;
        if (datei is not null)
        {
            var strom = new FileStream(datei, FileMode.Append, FileAccess.Write, FileShare.Read);
            _datei = new StreamWriter(strom, new UTF8Encoding(false)) { AutoFlush = true };
        }
    }

    public Protokollstufe Mindestens { get; }

    public Schwaerzung Schwaerzung { get; }

    /// <summary>Logbuch nur in einen Abgriff, etwa für Tests.</summary>
    public static Logbuch Fuer(Action<string> abgriff, Protokollstufe mindestens = Protokollstufe.Debug) =>
        new(mindestens, Schwaerzung.Zufaellig(), null, null, abgriff);

    public void Debug(string text) => Schreiben(Protokollstufe.Debug, text);

    public void Info(string text) => Schreiben(Protokollstufe.Info, text);

    public void Warnung(string text) => Schreiben(Protokollstufe.Warnung, text);

    public void Fehler(string text) => Schreiben(Protokollstufe.Fehler, text);

    /// <summary>Ausnahme ohne Stapel und ohne Nachrichtentext fremder Herkunft: nur Typ und Ort.</summary>
    public void Fehler(string text, Exception ausnahme)
    {
        ArgumentNullException.ThrowIfNull(ausnahme);
        Schreiben(Protokollstufe.Fehler, text + ": " + ausnahme.GetType().Name + " in " + (ausnahme.TargetSite?.Name ?? "?"));
    }

    public bool Aktiv(Protokollstufe stufe) => stufe >= Mindestens;

    private void Schreiben(Protokollstufe stufe, string text)
    {
        if (stufe < Mindestens) return;
        var kurz = stufe switch
        {
            Protokollstufe.Debug => "DEBUG",
            Protokollstufe.Info => "INFO",
            Protokollstufe.Warnung => "WARNUNG",
            _ => "FEHLER",
        };
        var zeile = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {kurz} {text}");
        lock (_sperre)
        {
            _konsole?.WriteLine(zeile);
            _datei?.WriteLine(zeile);
            _abgriff?.Invoke(zeile);
        }
    }

    public void Dispose()
    {
        lock (_sperre) _datei?.Dispose();
    }
}
