// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Funk.Testclient;

/// <summary>
/// Frisches Zustandsverzeichnis für einen Lasttest (Befehl <c>last</c>): Serverschlüssel, Reserve-Pin,
/// Betriebsgeheimnis, Profilschlüssel und signiertes Profil erzeugt <c>funk-admin</c> offline, alles nur für diesen Lauf;
/// danach startet <c>funk-testserver</c> mit Testzugang auf Loopback. Nach dem Lauf wird das Verzeichnis gelöscht.
/// </summary>
internal sealed class Einrichtung : IAsyncDisposable
{
    private readonly List<string> _log = [];
    private Process? _server;

    private Einrichtung(string verzeichnis, string adminDll) => (Verzeichnis, AdminDll) = (verzeichnis, adminDll);

    public string Verzeichnis { get; }
    public string AdminDll { get; }
    public string Socket => Path.Combine(Verzeichnis, "v.sock");
    public string ProfilPfad => Path.Combine(Verzeichnis, "profil.json");
    public string ProfilSpki { get; private set; } = "";
    public int TcpPort { get; private set; }
    public int Testzugang { get; private set; }
    public Process Server => _server ?? throw new InvalidOperationException("Server läuft nicht.");
    public IReadOnlyList<string> ServerLog => _log;

    public static async Task<Einrichtung> AnlegenAsync(string wurzel, string adminDll, string serverDll, double ausgangMbit, TextWriter aus)
    {
        var verzeichnis = Path.Combine(wurzel, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(verzeichnis);
        var e = new Einrichtung(verzeichnis, adminDll);
        try
        {
            await e.SchluesselUndProfilAsync().ConfigureAwait(false);
            e.KonfigurationSchreiben(ausgangMbit);
            await e.ServerStartenAsync(serverDll).ConfigureAwait(false);
            aus.WriteLine($"Testserver läuft: PID {e.Server.Id}, Testzugang 127.0.0.1:{e.Testzugang}, Zustand {e.Verzeichnis}");
            return e;
        }
        catch
        {
            await e.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<string> AdminAsync(params string[] argumente)
    {
        var a = await Werkzeuge.DotnetAsync(AdminDll, argumente, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        if (!a.Ok) throw new InvalidOperationException("funk-admin " + argumente[0] + ": " + a.Fehler.Trim());
        return a.Ausgabe;
    }

    private static string Pin(string ausgabe) =>
        ausgabe.Split('\n').Select(z => z.Trim()).First(z => z.StartsWith("Pin (", StringComparison.Ordinal)).Split(": ", 2)[1].Trim();

    private async Task SchluesselUndProfilAsync()
    {
        var d = Verzeichnis;
        var pin = Pin(await AdminAsync("schluessel-erzeugen", "--zertifikat", Path.Combine(d, "server.crt"), "--schluessel", Path.Combine(d, "server.key")).ConfigureAwait(false));
        var reserve = Pin(await AdminAsync("schluessel-erzeugen", "--zertifikat", Path.Combine(d, "reserve.crt"), "--schluessel", Path.Combine(d, "reserve.key")).ConfigureAwait(false));
        await AdminAsync("geheimnis-erzeugen", "--datei", Path.Combine(d, "geheimnis.bin")).ConfigureAwait(false);
        await AdminAsync("totp-schluessel-erzeugen", "--datei", Path.Combine(d, "totp-schluessel.bin")).ConfigureAwait(false);
        var spki = await AdminAsync("profilschluessel-erzeugen", "--schluessel", Path.Combine(d, "profil.key")).ConfigureAwait(false);
        ProfilSpki = spki.Split('\n').Select(z => z.Trim()).First(z => z.Length > 100 && !z.Contains(' ', StringComparison.Ordinal));
        TcpPort = Werkzeuge.FreierPort(SocketType.Stream);
        Testzugang = Werkzeuge.FreierPort(SocketType.Stream);
        await AdminAsync("profil-schreiben", "--name", "Lasttest", "--version", "1", "--host", "localhost/v4",
            "--tcp-port", TcpPort.ToString(CultureInfo.InvariantCulture), "--pin", pin, "--pin", reserve, "--gueltig-tage", "2",
            "--aus", Path.Combine(d, "profil-daten.json")).ConfigureAwait(false);
        await AdminAsync("profil-signieren", "--daten", Path.Combine(d, "profil-daten.json"), "--schluessel", Path.Combine(d, "profil.key"),
            "--aus", ProfilPfad).ConfigureAwait(false);
        await AdminAsync("profil-pruefen", "--profil", ProfilPfad, "--spki", ProfilSpki).ConfigureAwait(false);
    }

    private void KonfigurationSchreiben(double ausgangMbit)
    {
        var k = new JsonObject
        {
            ["binden"] = new JsonArray("127.0.0.1"),
            ["tcpPort"] = TcpPort,
            ["udpPort"] = Werkzeuge.FreierPort(SocketType.Dgram),
            ["zustandsverzeichnis"] = "zustand",
            ["identitaet"] = new JsonObject { ["zertifikat"] = "server.crt", ["schluessel"] = "server.key" },
            ["geheimnis"] = "geheimnis.bin",
            ["totpSchluessel"] = "totp-schluessel.bin",
            ["profil"] = "profil.json",
            ["mindestClientVersion"] = "0.1.0",
            ["verwaltungsSocket"] = "v.sock",
            ["grenzen"] = new JsonObject { ["ausgangMbit"] = ausgangMbit },
            ["log"] = new JsonObject { ["stufe"] = "WARNUNG" },
        };
        File.WriteAllText(Path.Combine(Verzeichnis, "server.json"), k.ToJsonString(), new UTF8Encoding(false));
    }

    private async Task ServerStartenAsync(string serverDll)
    {
        var si = Werkzeuge.Start(serverDll, ["--konfig", Path.Combine(Verzeichnis, "server.json"), "--testzugang", Testzugang.ToString(CultureInfo.InvariantCulture)]);
        si.RedirectStandardInput = true;
        _server = Process.Start(si) ?? throw new InvalidOperationException("funk-testserver startet nicht.");
        var bereit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.OutputDataReceived += (_, z) =>
        {
            if (z.Data is null) return;
            lock (_log)
            {
                _log.Add(z.Data);
                if (_log.Count > 500) _log.RemoveAt(0);
            }
            if (z.Data == "bereit") bereit.TrySetResult();
        };
        _server.ErrorDataReceived += (_, z) =>
        {
            if (z.Data is null) return;
            lock (_log) _log.Add("ERR " + z.Data);
        };
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();
        var fertig = await Task.WhenAny(bereit.Task, _server.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
        if (fertig != bereit.Task) throw new InvalidOperationException("funk-testserver nicht bereit:\n" + string.Join("\n", _log));
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is { HasExited: false } p)
        {
            try
            {
                await p.StandardInput.WriteLineAsync("ende").ConfigureAwait(false);
                await p.StandardInput.FlushAsync().ConfigureAwait(false);
                await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        _server?.Dispose();
        // Schlüssel, Geheimnis und Zustand dieses Laufs weg (nichts davon gehört ins Repository).
        for (var versuch = 0; versuch < 10 && Directory.Exists(Verzeichnis); versuch++)
        {
            try
            {
                Directory.Delete(Verzeichnis, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(500).ConfigureAwait(false);
            }
        }
        // Der Ordner tr-last selbst bleibt nur, wenn noch etwas anderes darin liegt.
        if (Path.GetDirectoryName(Verzeichnis) is { } wurzel && Directory.Exists(wurzel) && !Directory.EnumerateFileSystemEntries(wurzel).Any())
        {
            try
            {
                Directory.Delete(wurzel);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
