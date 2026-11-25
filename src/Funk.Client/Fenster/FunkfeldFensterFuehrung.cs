// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using Funk.Client.Modelle;

namespace Funk.Client.Fenster;

/// <summary>
/// Das echte Funkfeld-Fenster für die <see cref="FunkfeldAblage"/> (RAID-S2-ENTWURF Abschnitt 5, "Ohne Fokus"). Gebaut wird es erst
/// beim ersten Zeigen. Zeigen über Taste und Symbol: <c>Show()</c> mit <c>ShowActivated=false</c>, nie <c>Activate()</c>, danach
/// Topmost neu setzen; nur aus dem Hauptfenster (Knopf, Strg+F) mit Fokus. Ablegen ist <c>Hide()</c>: Lage, Größe, Ansicht und
/// Durchsichtigkeit bleiben, Zurückholen braucht keinen Neuaufbau.
/// </summary>
internal sealed class FunkfeldFensterFuehrung(Func<FunkfeldFenster> bauen) : IFunkfeldFenster
{
    private FunkfeldFenster? _fenster;

    /// <summary>Das Fenster, solange es gebaut ist (auch verborgen).</summary>
    public FunkfeldFenster? Fenster => _fenster;

    public bool Sichtbar => _fenster is { IsVisible: true };

    public void ZeigenOhneFokus()
    {
        var f = Holen();
        f.ShowActivated = false;
        f.Show();
        if (f.WindowState != WindowState.Normal) f.WindowState = WindowState.Normal;
        // Ein verborgenes und wieder gezeigtes Fenster kann unter ein anderes oberstes geraten; neu setzen holt es nach oben, ohne Fokus.
        f.Topmost = false;
        f.Topmost = true;
    }

    public void ZeigenMitFokus()
    {
        var f = Holen();
        f.ShowActivated = true;
        f.Show();
        if (f.WindowState != WindowState.Normal) f.WindowState = WindowState.Normal;
        f.Activate();
    }

    public void Verbergen() => _fenster?.Hide();

    public void Schliessen() => _fenster?.Close();

    /// <summary>Für eine Farbstellung: schließen und, wenn es zu sehen war, neu zeigen (mit Fokus nur, wenn es ihn hatte).</summary>
    public void NeuAufbauen(bool zeigen)
    {
        var aktiv = _fenster?.IsActive == true;
        _fenster?.Close();
        _fenster = null;
        if (!zeigen) return;
        if (aktiv) ZeigenMitFokus();
        else ZeigenOhneFokus();
    }

    private FunkfeldFenster Holen()
    {
        if (_fenster is not null) return _fenster;
        var neu = bauen();
        neu.Closed += (s, _) =>
        {
            if (ReferenceEquals(_fenster, s)) _fenster = null;
        };
        _fenster = neu;
        return neu;
    }
}
