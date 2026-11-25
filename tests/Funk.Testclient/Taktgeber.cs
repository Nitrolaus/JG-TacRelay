// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;

namespace Funk.Testclient;

/// <summary>
/// Gibt alle 20 ms einen Takt wie ein Audio-Encoder. Unter Windows dauert <c>Thread.Sleep(1)</c> bis zu 15,6 ms; darum
/// schläft der Taktgeber nur, solange mehr als 17 ms bis zum nächsten Takt fehlen, und wartet den Rest aktiv ab
/// (ohne die Zeitgeberauflösung des Systems zu verstellen). Er misst seine eigene Verspätung je Takt.
/// </summary>
internal sealed class Taktgeber(TimeSpan abstand)
{
    private readonly List<double> _verspaetungMs = [];

    /// <summary>Verspätung der Takte gegenüber dem Soll (ms), zur Einordnung der gemessenen Werte.</summary>
    public IReadOnlyList<double> VerspaetungMs => _verspaetungMs;

    /// <summary>Läuft <paramref name="takte"/> Takte auf einem eigenen Thread und ruft je Takt <paramref name="aktion"/> (Taktnummer, Takte-Zeitstempel).</summary>
    public Task LaufenAsync(int takte, Action<int, long> aktion, CancellationToken abbruch)
    {
        var fertig = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try
            {
                var periode = (long)(abstand.TotalSeconds * Stopwatch.Frequency);
                var start = Stopwatch.GetTimestamp();
                for (var k = 0; k < takte && !abbruch.IsCancellationRequested; k++)
                {
                    var soll = start + k * periode;
                    while (true)
                    {
                        var rest = soll - Stopwatch.GetTimestamp();
                        if (rest <= 0) break;
                        if (rest > 17 * Stopwatch.Frequency / 1000) Thread.Sleep(1);
                        else if (rest > Stopwatch.Frequency / 2000) Thread.Yield();
                        else Thread.SpinWait(20);
                    }
                    var jetzt = Stopwatch.GetTimestamp();
                    _verspaetungMs.Add((jetzt - soll) * 1000.0 / Stopwatch.Frequency);
                    aktion(k, jetzt);
                }
                fertig.TrySetResult();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                fertig.TrySetException(ex);
            }
        })
        { IsBackground = true, Name = "Taktgeber 20 ms", Priority = ThreadPriority.Highest };
        t.Start();
        return fertig.Task;
    }
}
