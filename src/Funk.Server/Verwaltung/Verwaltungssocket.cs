// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Funk.Server.Betrieb;
using Funk.Server.Kern;

namespace Funk.Server.Verwaltung;

/// <summary>
/// UNIX-Socket des Admin-Werkzeugs (3.8): keine Netzadresse, nur eine Datei im Zustandsverzeichnis. Unter Linux ist der
/// Socket nur für den Besitzer zugänglich (0600) und prüft zusätzlich die UID der Gegenseite per <c>SO_PEERCRED</c>
/// (root, der Dienstbenutzer und ausdrücklich konfigurierte UIDs). Je Verbindung eine Zeile JSON hin
/// (<c>{befehl, argumente}</c>) und eine zurück (<c>{ok, text, daten}</c>).
/// </summary>
public sealed class Verwaltungssocket : IAsyncDisposable
{
    public const int ZeileMax = 64 * 1024;

    private readonly string _pfad;
    private readonly Zentrale _zentrale;
    private readonly Logbuch _log;
    private readonly HashSet<uint> _uids;
    private readonly CancellationTokenSource _stop = new();
    private Socket? _lauscher;
    private Task? _schleife;

    public Verwaltungssocket(string pfad, Zentrale zentrale, Logbuch log, IEnumerable<int> zusaetzlicheUids)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        ArgumentNullException.ThrowIfNull(zentrale);
        ArgumentNullException.ThrowIfNull(log);
        _pfad = pfad;
        _zentrale = zentrale;
        _log = log;
        _uids = [0, .. (zusaetzlicheUids ?? []).Where(u => u >= 0).Select(u => (uint)u)];
        if (!OperatingSystem.IsWindows()) _uids.Add(EigeneUid());
    }

    public string Pfad => _pfad;

    public void Starten()
    {
        Dateiablage.VerzeichnisAnlegen(Path.GetDirectoryName(Path.GetFullPath(_pfad))!);
        if (File.Exists(_pfad)) File.Delete(_pfad);
        var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        s.Bind(new UnixDomainSocketEndPoint(_pfad));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_pfad, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        s.Listen(8);
        _lauscher = s;
        _schleife = Task.Run(AnnehmenAsync);
        if (OperatingSystem.IsWindows()) _log.Warnung("Verwaltungssocket unter Windows ohne Prüfung der Gegenseite; der Schutz liegt allein an den Rechten des Zustandsverzeichnisses");
    }

    private async Task AnnehmenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket s;
            try
            {
                s = await _lauscher!.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (_stop.IsCancellationRequested) return;
                continue;
            }
            _ = Task.Run(() => BehandelnAsync(s));
        }
    }

    private async Task BehandelnAsync(Socket s)
    {
        using (s)
        {
            try
            {
                if (OperatingSystem.IsLinux() && !GegenseiteErlaubt(s))
                {
                    _log.Warnung("Verwaltungssocket: Gegenseite mit nicht zugelassener UID abgewiesen");
                    return;
                }
                using var strom = new NetworkStream(s, ownsSocket: false);
                using var frist = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                frist.CancelAfter(TimeSpan.FromSeconds(10));
                var zeile = await ZeileLesenAsync(strom, frist.Token).ConfigureAwait(false);
                VerwaltungsAntwort antwort;
                if (zeile is null || JsonNode.Parse(zeile) is not JsonObject anfrage || anfrage["befehl"] is not JsonValue b
                    || !b.TryGetValue<string>(out var befehl))
                {
                    antwort = VerwaltungsAntwort.Fehler("Anfrage ungültig");
                }
                else
                {
                    var argumente = anfrage["argumente"] as JsonObject ?? [];
                    anfrage.Remove("argumente");
                    antwort = await _zentrale.VerwaltungAsync(befehl, argumente).WaitAsync(TimeSpan.FromSeconds(130), _stop.Token).ConfigureAwait(false);
                }
                var aus = new JsonObject { ["ok"] = antwort.Ok, ["text"] = antwort.Text, ["daten"] = antwort.Daten?.DeepClone() };
                var bytes = Encoding.UTF8.GetBytes(aus.ToJsonString() + "\n");
                await strom.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or JsonException or SocketException or ObjectDisposedException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.Fehler("Verwaltungssocket", ex);
            }
        }
    }

    private static async Task<string?> ZeileLesenAsync(Stream strom, CancellationToken abbruch)
    {
        var puffer = new byte[ZeileMax];
        var n = 0;
        while (n < puffer.Length)
        {
            var r = await strom.ReadAsync(puffer.AsMemory(n), abbruch).ConfigureAwait(false);
            if (r == 0) break;
            var ende = Array.IndexOf(puffer, (byte)'\n', n, r);
            n += r;
            if (ende >= 0) return Encoding.UTF8.GetString(puffer, 0, ende);
        }
        return n == 0 || n >= puffer.Length ? null : Encoding.UTF8.GetString(puffer, 0, n);
    }

    /// <summary>SO_PEERCRED (SOL_SOCKET 1, Option 17): struct ucred { pid, uid, gid }.</summary>
    [SupportedOSPlatform("linux")]
    private bool GegenseiteErlaubt(Socket s)
    {
        Span<byte> ucred = stackalloc byte[12];
        var n = s.GetRawSocketOption(1, 17, ucred);
        if (n < 12) return false;
        var uid = BitConverter.IsLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(ucred[4..]) : BinaryPrimitives.ReadUInt32BigEndian(ucred[4..]);
        return _uids.Contains(uid);
    }

    [UnsupportedOSPlatform("windows")]
    private static uint EigeneUid() => Libc.getuid();

    private static class Libc
    {
        [DllImport("libc", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
#pragma warning disable SYSLIB1054 // LibraryImport verlangte unsicheren Code im ganzen Projekt; getuid() braucht kein Marshalling.
        internal static extern uint getuid();
#pragma warning restore SYSLIB1054
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _lauscher?.Dispose();
        if (_schleife is not null)
        {
            try
            {
                await _schleife.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
            }
        }
        try
        {
            if (File.Exists(_pfad)) File.Delete(_pfad);
        }
        catch (IOException)
        {
        }
        _stop.Dispose();
    }
}
