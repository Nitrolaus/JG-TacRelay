// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Funk.Testclient;

/// <summary>Ergebnis eines Programmaufrufs.</summary>
internal sealed record Aufruf(int Exitcode, string Ausgabe, string Fehler)
{
    public bool Ok => Exitcode == 0;
}

/// <summary>
/// Aufrufe von <c>funk-admin</c> (Admin-Werkzeug, 3.8) als eigener Prozess, so wie ein Admin sie tippt, und eine kleine
/// Abfrage des Verwaltungssockets für die Messwerte im Sekundentakt (dieselbe Zeile JSON wie das Werkzeug).
/// </summary>
internal sealed class AdminWerkzeug(string dll, string socket)
{
    public string Socket { get; } = socket;

    /// <summary>Führt <c>funk-admin &lt;befehl&gt; ... --socket &lt;pfad&gt;</c> aus.</summary>
    public Task<Aufruf> AusfuehrenAsync(params string[] argumente) =>
        Werkzeuge.DotnetAsync(dll, [.. argumente, "--socket", Socket], TimeSpan.FromSeconds(150));

    /// <summary>Wie <see cref="AusfuehrenAsync"/>, wirft bei Fehler.</summary>
    public async Task<string> PflichtAsync(params string[] argumente)
    {
        var a = await AusfuehrenAsync(argumente).ConfigureAwait(false);
        if (!a.Ok) throw new InvalidOperationException("funk-admin " + argumente[0] + ": " + a.Fehler.Trim() + " " + a.Ausgabe.Trim());
        return a.Ausgabe;
    }

    /// <summary>Messwerte des Relais über den Verwaltungssocket (Befehl <c>messwerte</c>), als JSON-Daten.</summary>
    public async Task<JsonNode?> MesswerteAsync()
    {
        using var abbruch = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await s.ConnectAsync(new UnixDomainSocketEndPoint(Socket), abbruch.Token).ConfigureAwait(false);
        using var strom = new NetworkStream(s, ownsSocket: false);
        var anfrage = new JsonObject { ["befehl"] = "messwerte", ["argumente"] = new JsonObject() };
        await strom.WriteAsync(Encoding.UTF8.GetBytes(anfrage.ToJsonString() + "\n"), abbruch.Token).ConfigureAwait(false);
        using var leser = new StreamReader(strom, Encoding.UTF8);
        var zeile = await leser.ReadLineAsync(abbruch.Token).ConfigureAwait(false);
        return zeile is null ? null : JsonNode.Parse(zeile)?["daten"];
    }
}

internal static class Werkzeuge
{
    /// <summary>Der dotnet-Host: aus <c>DOTNET_HOST_PATH</c>, sonst aus PATH.</summary>
    public static string Dotnet()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } h && File.Exists(h)) return h;
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var ordner in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = Path.Combine(ordner.Trim(), name);
            if (File.Exists(p)) return p;
        }
        return name;
    }

    public static ProcessStartInfo Start(string dll, IEnumerable<string> argumente)
    {
        var si = new ProcessStartInfo(Dotnet())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        si.ArgumentList.Add(dll);
        foreach (var a in argumente) si.ArgumentList.Add(a);
        return si;
    }

    /// <summary>Startet <c>dotnet &lt;dll&gt; argumente</c> und wartet auf das Ende.</summary>
    public static async Task<Aufruf> DotnetAsync(string dll, IReadOnlyList<string> argumente, TimeSpan frist)
    {
        using var p = Process.Start(Start(dll, argumente)) ?? throw new InvalidOperationException("Start gescheitert: " + dll);
        using var abbruch = new CancellationTokenSource(frist);
        var aus = p.StandardOutput.ReadToEndAsync(abbruch.Token);
        var fehler = p.StandardError.ReadToEndAsync(abbruch.Token);
        try
        {
            await p.WaitForExitAsync(abbruch.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException("Zeitüberschreitung: " + Path.GetFileName(dll) + " " + (argumente.Count > 0 ? argumente[0] : ""));
        }
        return new Aufruf(p.ExitCode, await aus.ConfigureAwait(false), await fehler.ConfigureAwait(false));
    }

    /// <summary>Sucht die Lösungswurzel (TacRelay.slnx) oberhalb des Programms.</summary>
    public static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden; --admin und --server angeben.");
    }

    /// <summary>
    /// Ablage der Lasttest-Läufe außerhalb der Arbeitskopie, denn ein Lauf legt Serverschlüssel und Geheimnis ab; nach
    /// einem Absturz blieben sie sonst im Repository-Ordner liegen. <c>$FUNK_TESTVERZEICHNIS\tr-last</c>, sonst unter
    /// Windows <c>C:\Claude\tmp\tr-last</c>; ohne C:\Claude bricht es ab, statt ins Temp-Verzeichnis des Benutzers
    /// auszuweichen (dort blieben nach einem Absturz Schlüssel liegen). Unter Linux das Temp-Verzeichnis.
    /// </summary>
    public static string Lastwurzel()
    {
        var gesetzt = Environment.GetEnvironmentVariable("FUNK_TESTVERZEICHNIS");
        if (!string.IsNullOrEmpty(gesetzt)) return Path.Combine(gesetzt, "tr-last");
        if (!OperatingSystem.IsWindows()) return Path.Combine(Path.GetTempPath(), "tr-last");
        if (!Directory.Exists(@"C:\Claude"))
            throw new InvalidOperationException(@"C:\Claude fehlt: FUNK_TESTVERZEICHNIS auf einen Testordner setzen. Ins Temp-Verzeichnis des Benutzers weicht der Lasttest nicht aus.");
        return @"C:\Claude\tmp\tr-last";
    }

    /// <summary>Build-Konfiguration (Debug/Release) aus dem eigenen Ausgabepfad.</summary>
    public static string Konfiguration() =>
        AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";

    /// <summary>Pfad der DLL eines Projekts der Lösung im selben Build.</summary>
    public static string Dll(string projektOrdner, string assembly) =>
        Path.Combine(Wurzel(), projektOrdner, "bin", Konfiguration(), "net10.0", assembly + ".dll");

    /// <summary>Freier TCP- bzw. UDP-Port auf Loopback (kurz gebunden und wieder freigegeben).</summary>
    public static int FreierPort(SocketType art)
    {
        using var s = new Socket(System.Net.Sockets.AddressFamily.InterNetwork, art, art == SocketType.Dgram ? ProtocolType.Udp : ProtocolType.Tcp);
        s.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        return ((System.Net.IPEndPoint)s.LocalEndPoint!).Port;
    }
}
