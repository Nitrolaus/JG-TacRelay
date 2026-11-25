// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;

namespace Funk.Klang.Maschine;

/// <summary>
/// Eigener Steuer-Thread (MTA) für alles, was Geräte anfasst: Öffnen, Schließen, Gerätewechsel, Aufräumen. So laufen
/// Starten, Stoppen und die Reaktion auf Windows-Geräteereignisse nie gleichzeitig, die COM-Objekte von WASAPI leben in
/// einem Apartment, und kein Aufruf blockiert einen Audio-Thread oder die Oberfläche länger als nötig. Aufträge laufen
/// der Reihe nach; dazwischen ruft die Schleife etwa jede Sekunde <c>takt</c> auf.
/// </summary>
internal sealed class Steuerschleife : IDisposable
{
    private static readonly TimeSpan Taktabstand = TimeSpan.FromSeconds(1);

    private readonly BlockingCollection<Action> _auftraege = [];
    private readonly Thread _thread;
    private readonly Action _takt;
    private readonly Action<Exception> _fehler;

    public Steuerschleife(string name, Action takt, Action<Exception> fehler)
    {
        ArgumentNullException.ThrowIfNull(takt);
        ArgumentNullException.ThrowIfNull(fehler);
        _takt = takt;
        _fehler = fehler;
        _thread = new Thread(Schleife) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public bool ImThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    public Task Ausfuehren(Action auftrag)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        return Ausfuehren(() =>
        {
            auftrag();
            return true;
        });
    }

    public Task<T> Ausfuehren<T>(Func<T> auftrag)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        if (ImThread) return Task.FromResult(auftrag());
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _auftraege.Add(() =>
            {
                try
                {
                    tcs.SetResult(auftrag());
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    tcs.SetException(ex);
                }
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            tcs.SetException(new ObjectDisposedException(nameof(Steuerschleife)));
        }
        return tcs.Task;
    }

    /// <summary>Auftrag vormerken, ohne auf ihn zu warten (etwa aus einem Geräteereignis). Nach dem Beenden verworfen.</summary>
    public void Vormerken(Action auftrag)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        try
        {
            _auftraege.Add(auftrag);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Schleife beendet (auch ein Geräteereignis, das während des Freigebens eintrifft): nichts mehr zu tun.
        }
    }

    public void Dispose()
    {
        _auftraege.CompleteAdding();
        if (ImThread) return; // die Schleife endet nach diesem Auftrag von selbst
        _thread.Join();
        _auftraege.Dispose();
    }

    private void Schleife()
    {
        var letzterTakt = Environment.TickCount64;
        while (!_auftraege.IsCompleted)
        {
            Action? auftrag = null;
            try
            {
                _ = _auftraege.TryTake(out auftrag, Taktabstand);
            }
            catch (InvalidOperationException)
            {
                break;
            }
            Sicher(auftrag);
            if (Environment.TickCount64 - letzterTakt < Taktabstand.TotalMilliseconds) continue;
            letzterTakt = Environment.TickCount64;
            Sicher(_takt);
        }
    }

    private void Sicher(Action? aktion)
    {
        if (aktion is null) return;
        try
        {
            aktion();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _fehler(ex);
        }
    }
}
