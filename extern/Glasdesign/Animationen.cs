// Glasdesign - Maintainer: Sarevoc
using System.Windows;

namespace Glasdesign;

/// <summary>
/// Was die Elemente mit Daueranimation (Läufer des Fortschritts, Lichtkante) über ihre Umgebung wissen müssen: ob Windows
/// Animationen erlaubt (SystemParameters.ClientAreaAnimation, "Animationen anzeigen"), ob ein Element auf dem Bildschirm
/// steht, und eine Nachricht, wenn jemand die Animationen im Lauf umschaltet (SystemParameters.StaticPropertyChanged).
/// Die Elemente abonnieren <see cref="Geaendert"/> nur, solange sie auf dem Bildschirm etwas zu bewegen haben, und prüfen
/// ihre Regel dann neu: Schaltet jemand die Animationen aus, hält der Läufer in der Mitte an; schaltet er sie wieder ein,
/// läuft er weiter.
/// <para>
/// Die beiden Abfragen sind für die Tests austauschbar: Windows lässt sich im Test nicht umschalten, und im Speicher
/// gezeichnet steht nichts auf dem Bildschirm.
/// </para>
/// </summary>
internal static class Animationen
{
    private static readonly Lock Sperre = new();
    private static EventHandler? _geaendert;
    private static bool _verbunden;

    /// <summary>Ob Windows Animationen erlaubt.</summary>
    internal static Func<bool> Abfrage { get; set; } = () => SystemParameters.ClientAreaAnimation;

    /// <summary>Ob ein Element auf dem Bildschirm steht: sichtbar und in einer PresentationSource (einem Fenster).</summary>
    internal static Func<FrameworkElement, bool> Bildschirm { get; set; } = e => e.IsVisible && PresentationSource.FromVisual(e) is not null;

    public static bool Erlaubt => Abfrage();

    public static bool AufDemBildschirm(FrameworkElement element) => Bildschirm(element);

    /// <summary>
    /// Kommt, wenn Windows "Animationen anzeigen" umschaltet, auf dem Faden, der die Nachricht von Windows verarbeitet; die
    /// Abonnenten wechseln selbst auf ihren Faden. Das erste Abonnement verbindet einmal mit SystemParameters.
    /// </summary>
    public static event EventHandler? Geaendert
    {
        add
        {
            lock (Sperre)
            {
                if (!_verbunden)
                {
                    _verbunden = true;
                    SystemParameters.StaticPropertyChanged += (_, e) =>
                    {
                        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Melden();
                    };
                }
                _geaendert += value;
            }
        }
        remove
        {
            lock (Sperre) _geaendert -= value;
        }
    }

    /// <summary>Wie viele Elemente gerade zuhören (für die Tests: nach dem Abmelden keines mehr).</summary>
    internal static int Zuhoerer
    {
        get
        {
            lock (Sperre) return _geaendert?.GetInvocationList().Length ?? 0;
        }
    }

    /// <summary>Gibt eine Änderung an alle Abonnenten weiter; Windows über SystemParameters, die Tests direkt.</summary>
    internal static void Melden()
    {
        EventHandler? h;
        lock (Sperre) h = _geaendert;
        h?.Invoke(null, EventArgs.Empty);
    }
}
