// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using System.Text;

namespace Funk.Server.Betrieb;

/// <summary>
/// Das Betriebsgeheimnis des Servers (per systemd <c>LoadCredential</c>, nie in der Konfiguration): mindestens 32
/// Zufallsbytes. Daraus leitet HKDF-SHA256 getrennte Schlüssel ab, einen für die Schwärzung im Logbuch und einen für
/// die HMAC-Werte der Vorrangspur (4.8, 7.5). Die Datei selbst wird nur gelesen, nie ausgegeben.
/// </summary>
public sealed class Geheimnis : IDisposable
{
    public const int MindestLaenge = 32;
    public const int HoechstLaenge = 4096;

    private readonly byte[] _wert;

    private Geheimnis(byte[] wert) => _wert = wert;

    /// <summary>Liest die Datei; zu kurz oder zu lang ist ein Konfigurationsfehler.</summary>
    public static Geheimnis Laden(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        var laenge = new FileInfo(pfad).Length;
        if (laenge is < MindestLaenge or > HoechstLaenge)
            throw new InvalidDataException($"Das Geheimnis muss {MindestLaenge} bis {HoechstLaenge} Byte lang sein.");
        return new Geheimnis(File.ReadAllBytes(pfad));
    }

    /// <summary>Nur für Tests: zufälliges Geheimnis im Speicher.</summary>
    public static Geheimnis Zufaellig() => new(RandomNumberGenerator.GetBytes(MindestLaenge));

    /// <summary>
    /// Schreibt ein neues Geheimnis (Admin-Werkzeug). Unter Linux nur für den Besitzer lesbar. Eine vorhandene Datei bleibt ohne
    /// <paramref name="ersetzen"/> unangetastet (<see cref="SchonDaException"/>).
    /// </summary>
    public static void Erzeugen(string pfad, bool ersetzen = false)
    {
        var wert = RandomNumberGenerator.GetBytes(MindestLaenge);
        try
        {
            Dateiablage.AtomarSchreiben(pfad, wert, nurBesitzer: true, ersetzen);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wert);
        }
    }

    /// <summary>Abgeleiteter Schlüssel für einen Zweck (HKDF-Expand mit dem Zweck als Info).</summary>
    public byte[] Ableiten(string zweck)
    {
        ArgumentNullException.ThrowIfNull(zweck);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _wert, 32, salt: "tacrelay server"u8.ToArray(),
            info: Encoding.UTF8.GetBytes(zweck));
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_wert);
}
