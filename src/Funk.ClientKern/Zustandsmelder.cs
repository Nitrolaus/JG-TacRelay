// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Schutz;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>
/// C5: Zustandsmeldungen (SendeauswahlSetzen, HoerenSetzen, PegelSetzen, SprechanzeigeAbo) gehen nur mit dem letzten Stand
/// hinaus, höchstens 4 je Sekunde und Art (Stoß 6, etwas unter dem Stoß des Servers von 8). Was über den Topf geht, wartet
/// je Art und Schlüssel (etwa das Netz bei HoerenSetzen); ein neuerer Stand ersetzt den wartenden, und dessen Aufrufer
/// bekommen die Antwort des neueren. So entsteht nie ein Verstoß und kaum ein UEBERHOLT. Threadsicher.
/// </summary>
internal sealed class Zustandsmelder
{
    public const double JeSekunde = 4;
    public const double Stoss = 6;

    private sealed class Wartend
    {
        public required Func<Task<Antwort>> Senden;
        public List<TaskCompletionSource<Antwort>> Warter { get; } = [];
    }

    private readonly Lock _sperre = new();
    private readonly Dictionary<Nachrichtentyp, TokenEimer> _toepfe = [];
    private readonly Dictionary<(Nachrichtentyp, long), Wartend> _wartend = [];
    private readonly List<(Nachrichtentyp, long)> _faellig = [];

    /// <summary>
    /// Meldet einen Stand. <paramref name="senden"/> stellt die Anfrage; sie läuft sofort, wenn der Topf es zulässt,
    /// sonst beim nächsten <see cref="Takt"/>. Die Aufgabe endet mit der Antwort auf diesen oder einen neueren Stand.
    /// </summary>
    public Task<Antwort> Melden(Nachrichtentyp art, long schluessel, Func<Task<Antwort>> senden)
    {
        var tcs = new TaskCompletionSource<Antwort>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool sofort;
        lock (_sperre)
        {
            if (_wartend.TryGetValue((art, schluessel), out var w))
            {
                w.Senden = senden;
                w.Warter.Add(tcs);
                return tcs.Task;
            }
            sofort = Topf(art).Nehmen(Monouhr.Takte());
            if (!sofort)
            {
                var neu = new Wartend { Senden = senden };
                neu.Warter.Add(tcs);
                _wartend[(art, schluessel)] = neu;
                return tcs.Task;
            }
        }
        _ = Weiterleiten(senden, [tcs]);
        return tcs.Task;
    }

    /// <summary>Schickt Wartendes, sobald der Topf der Art es zulässt.</summary>
    public void Takt()
    {
        List<Wartend>? los = null;
        lock (_sperre)
        {
            _faellig.Clear();
            foreach (var (k, _) in _wartend)
                if (Topf(k.Item1).Nehmen(Monouhr.Takte())) _faellig.Add(k);
            foreach (var k in _faellig)
            {
                (los ??= []).Add(_wartend[k]);
                _wartend.Remove(k);
            }
        }
        if (los is null) return;
        foreach (var w in los) _ = Weiterleiten(w.Senden, w.Warter);
    }

    /// <summary>Die Sitzung endet: Wartende bekommen die Ausnahme.</summary>
    public void Abbrechen(Exception grund)
    {
        List<Wartend> alle;
        lock (_sperre)
        {
            alle = [.. _wartend.Values];
            _wartend.Clear();
        }
        foreach (var w in alle)
            foreach (var t in w.Warter) t.TrySetException(grund);
    }

    private TokenEimer Topf(Nachrichtentyp art)
    {
        if (!_toepfe.TryGetValue(art, out var t))
        {
            t = new TokenEimer(Stoss, JeSekunde, Monouhr.Takte());
            _toepfe[art] = t;
        }
        return t;
    }

    private static async Task Weiterleiten(Func<Task<Antwort>> senden, List<TaskCompletionSource<Antwort>> warter)
    {
        try
        {
            var a = await senden().ConfigureAwait(false);
            foreach (var t in warter) t.TrySetResult(a);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            foreach (var t in warter) t.TrySetException(ex);
        }
    }
}
