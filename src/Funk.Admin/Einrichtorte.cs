// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Admin;

/// <summary>
/// Wo <c>funk-admin einrichten</c> liest und schreibt. Im Betrieb <see cref="Linux"/>, dieselben Pfade wie in
/// funk-server.service, funk-echo.service und BETRIEB.md (EinrichtenSkriptTests gleicht sie ab). Tests lenken mit
/// <see cref="Unter"/> alles in einen Ordner unter C:\Claude\tmp; nichts davon zeigt dann auf das System.
/// </summary>
public sealed record Einrichtorte(string Programme, string Etc, string Systemd, string Kurzbefehl, string Zustand, string EchoZustand)
{
    /// <summary>Die Pfade auf dem Server.</summary>
    public static Einrichtorte Linux { get; } = new("/opt/funk", "/etc/funk", "/etc/systemd/system", "/usr/local/sbin/funk-admin", "/var/lib/funk", "/var/lib/funk-echo");

    /// <summary>Dieselbe Ordnung unter <paramref name="wurzel"/>, für Tests.</summary>
    public static Einrichtorte Unter(string wurzel)
    {
        ArgumentNullException.ThrowIfNull(wurzel);
        string P(params string[] teile) => Path.Combine([wurzel, .. teile]);
        return new(P("opt", "funk"), P("etc", "funk"), P("etc", "systemd", "system"), P("usr", "local", "sbin", "funk-admin"), P("var", "lib", "funk"),
            P("var", "lib", "funk-echo"));
    }

    /// <summary>Die Vorlagen aus dem Paket: Units, profilschluessel.txt.</summary>
    public string Paketkonfig => Pfad(Programme, "konfig");

    public string Credentials => Pfad(Etc, "credentials");

    public string Konfig => Pfad(Etc, "server.json");

    public string Profil => Pfad(Etc, "profil.json");

    /// <summary>Die öffentlichen Profilschlüssel neben server.json; dort sucht sie auch <c>funk-admin pruefen</c>.</summary>
    public string Profilschluessel => Pfad(Etc, "profilschluessel.txt");

    /// <summary>Die Anfragezeile für Sarevoc, solange das Profil fehlt.</summary>
    public string Anfrage => Pfad(Etc, "profilanfrage.txt");

    /// <summary>Die Adresse des Echo-Funkers für funk-echo.service (EnvironmentFile).</summary>
    public string EchoEnv => Pfad(Etc, "echo.env");

    public string EchoIdentitaet => Pfad(EchoZustand, "geraet.pem");

    public string Unit(string name) => Pfad(Systemd, name);

    /// <summary>Das Drop-in, das funk-server einen Port unter 1024 erlaubt.</summary>
    public string DropIn => Pfad(Pfad(Systemd, "funk-server.service.d"), "10-port-unter-1024.conf");

    /// <summary>Wie die Units dotnet aufrufen; funk-einrichten.sh sorgt dafür, dass es ihn gibt.</summary>
    public const string Dotnet = "/usr/bin/dotnet";

    /// <summary>Linux-Pfade bleiben mit / verbunden, auch wenn ein Test unter Windows sie liest; sonst wie das System.</summary>
    private static string Pfad(string ordner, string name) =>
        ordner.StartsWith('/') ? ordner.TrimEnd('/') + "/" + name : Path.Combine(ordner, name);
}
