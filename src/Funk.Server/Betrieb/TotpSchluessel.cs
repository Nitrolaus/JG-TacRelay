// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;

namespace Funk.Server.Betrieb;

/// <summary>
/// Der Schlüssel <c>funk-totp-schluessel</c> (3.9): genau 32 Zufallsbytes, per systemd <c>LoadCredential</c> wie der
/// Serverschlüssel, nie in einer lesbaren Konfiguration und nie in einer Sicherung. Damit verschlüsselt der Server die
/// TOTP-Geheimnisse der Admins (AES-256-GCM) und bildet die Prüfwerte der Notfallcodes. Geht er verloren, richten alle
/// Admins den Zweitfaktor an der Konsole neu ein; eine Kopie außerhalb des Servers ist nicht nötig.
/// </summary>
public static class TotpSchluessel
{
    public const int Laenge = 32;

    /// <summary>Liest die Datei; jede andere Länge ist ein Konfigurationsfehler. Der Aufrufer nullt das Ergebnis.</summary>
    public static byte[] Laden(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        if (!File.Exists(pfad))
            throw new InvalidDataException("funk-totp-schluessel fehlt (" + pfad + "): funk-admin totp-schluessel-erzeugen --datei <pfad>, dann per LoadCredential einbinden.");
        if (new FileInfo(pfad).Length != Laenge)
            throw new InvalidDataException($"funk-totp-schluessel muss genau {Laenge} Byte lang sein.");
        return File.ReadAllBytes(pfad);
    }

    /// <summary>
    /// Schreibt einen neuen Schlüssel (Admin-Werkzeug, offline). Unter Linux nur für den Besitzer lesbar. Ohne
    /// <paramref name="ersetzen"/> bleibt ein vorhandener unangetastet (<see cref="SchonDaException"/>): Ein ersetzter Schlüssel
    /// macht jeden eingerichteten Zweitfaktor unlesbar.
    /// </summary>
    public static void Erzeugen(string pfad, bool ersetzen = false)
    {
        var wert = RandomNumberGenerator.GetBytes(Laenge);
        try
        {
            Dateiablage.AtomarSchreiben(pfad, wert, nurBesitzer: true, ersetzen);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wert);
        }
    }
}
