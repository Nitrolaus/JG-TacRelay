// JG TacRelay - Maintainer: Sarevoc
using System.Windows.Threading;

namespace Funk.Client.Dienste;

/// <summary>
/// Bringt Arbeit auf den Oberflächen-Thread. Die ViewModels hängen nur an dieser Naht, nicht am WPF-Dispatcher; so laufen
/// sie in den Tests ohne Fenster.
/// </summary>
public interface IOberflaechentakt
{
    /// <summary>Reiht <paramref name="aktion"/> ein; kehrt sofort zurück.</summary>
    void Einreihen(Action aktion);
}

/// <summary>Führt sofort auf dem aufrufenden Thread aus (Tests).</summary>
public sealed class SofortTakt : IOberflaechentakt
{
    public static SofortTakt Instanz { get; } = new();

    public void Einreihen(Action aktion)
    {
        ArgumentNullException.ThrowIfNull(aktion);
        aktion();
    }
}

/// <summary>Reiht auf dem WPF-Dispatcher ein.</summary>
public sealed class DispatcherTakt(Dispatcher dispatcher) : IOberflaechentakt
{
    public void Einreihen(Action aktion)
    {
        ArgumentNullException.ThrowIfNull(aktion);
        if (dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(aktion, DispatcherPriority.Normal);
    }
}
