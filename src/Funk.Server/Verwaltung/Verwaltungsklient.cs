// JG TacRelay - Maintainer: Sarevoc
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Funk.Server.Kern;

namespace Funk.Server.Verwaltung;

/// <summary>Gegenstück zum <see cref="Verwaltungssocket"/>: eine Anfrage, eine Antwort, je eine Zeile JSON.</summary>
public static class Verwaltungsklient
{
    /// <summary>Sendet <paramref name="befehl"/> mit <paramref name="argumente"/> und wartet höchstens <paramref name="frist"/>.</summary>
    public static async Task<VerwaltungsAntwort> SendenAsync(string socketPfad, string befehl, JsonObject? argumente, TimeSpan frist)
    {
        ArgumentNullException.ThrowIfNull(socketPfad);
        ArgumentNullException.ThrowIfNull(befehl);
        using var abbruch = new CancellationTokenSource(frist);
        using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await s.ConnectAsync(new UnixDomainSocketEndPoint(socketPfad), abbruch.Token).ConfigureAwait(false);
        using var strom = new NetworkStream(s, ownsSocket: false);
        var anfrage = new JsonObject { ["befehl"] = befehl, ["argumente"] = argumente ?? [] };
        await strom.WriteAsync(Encoding.UTF8.GetBytes(anfrage.ToJsonString() + "\n"), abbruch.Token).ConfigureAwait(false);
        using var leser = new StreamReader(strom, Encoding.UTF8);
        var zeile = await leser.ReadLineAsync(abbruch.Token).ConfigureAwait(false);
        if (zeile is null || JsonNode.Parse(zeile) is not JsonObject antwort) return VerwaltungsAntwort.Fehler("keine Antwort vom Server");
        return new VerwaltungsAntwort(antwort["ok"]?.GetValue<bool>() ?? false, antwort["text"]?.GetValue<string>() ?? "", antwort["daten"]?.DeepClone());
    }
}
