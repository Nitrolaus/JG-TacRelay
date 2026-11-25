// JG TacRelay - Maintainer: Sarevoc
using Funk.Server.Betrieb;

namespace Funk.Admin;

/// <summary>
/// Wo die Schlüsseldateien eines Servers liegen, gesehen von der Konsole. In server.json stehen sie als
/// <c>${CREDENTIALS_DIRECTORY}/server.key</c> und so weiter; die Variable setzt systemd nur im Dienst (LoadCredential). An der
/// Konsole gilt deshalb <c>--credentials</c>, sonst die Variable, falls gesetzt, sonst <c>/etc/funk/credentials</c> wie in
/// funk-server.service. Pfade ohne die Variable löst die Konfiguration selbst auf.
/// </summary>
public sealed class Schluesselorte
{
    /// <summary>Der Ordner aus funk-server.service (LoadCredential=...:/etc/funk/credentials/...).</summary>
    public const string Voreinstellung = "/etc/funk/credentials";

    private const string Variable = "${CREDENTIALS_DIRECTORY}";

    private readonly ServerKonfiguration _k;

    public Schluesselorte(ServerKonfiguration konfiguration, string? credentials)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);
        _k = konfiguration;
        Ordner = Path.GetFullPath(credentials ?? Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY") ?? Voreinstellung);
    }

    /// <summary>Der Schlüsselordner, den diese Sicht annimmt.</summary>
    public string Ordner { get; }

    /// <summary>Löst einen Pfad aus server.json auf; <c>${CREDENTIALS_DIRECTORY}</c> wird zu <see cref="Ordner"/>.</summary>
    public string Aufloesen(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        return _k.Aufloesen(pfad.Replace(Variable, Ordner.Replace('\\', '/'), StringComparison.Ordinal));
    }

    public string Zertifikat => Aufloesen(_k.Identitaet.Zertifikat);

    public string Schluessel => Aufloesen(_k.Identitaet.Schluessel);

    public string Geheimnis => Aufloesen(_k.Geheimnis);

    public string Totp => Aufloesen(_k.TotpSchluessel);

    public string Zustand => Aufloesen(_k.Zustandsverzeichnis);

    public string? Profil => _k.Profil is { } p ? Aufloesen(p) : null;
}
