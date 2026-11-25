// JG TacRelay - Maintainer: Sarevoc
using Funk.ClientKern;
using Funk.Verwaltung;
using Funk.Verwaltung.Ansichten;
using Funk.Verwaltung.Modelle;

[assembly: Verwaltungsteil(typeof(Einstieg))]

namespace Funk.Verwaltung;

/// <summary>
/// Einstieg für den Client (<see cref="IVerwaltungsteilFabrik"/>): baut Dienst, Modell und Ansicht über dem Wirt. Der
/// Admin-Schlüssel liegt im TPM (<see cref="TpmAdminSchluessel"/>), die Vorprüfung vor dem Einrichten liest am echten PC
/// (<see cref="Adminvoraussetzungen.AmPc"/>); Tests geben einen flüchtigen Speicher und eine Attrappe hinein.
/// </summary>
public sealed class Einstieg : IVerwaltungsteilFabrik
{
    public IVerwaltungsteil Erzeugen(IVerwaltungsWirt wirt) => Erzeugen(wirt, new TpmAdminSchluessel(), voraussetzungen: Adminvoraussetzungen.AmPc());

    public static Verwaltungsteil Erzeugen(IVerwaltungsWirt wirt, IAdminSchluesselSpeicher speicher, TimeProvider? zeit = null,
        IAdminvoraussetzungen? voraussetzungen = null)
    {
        ArgumentNullException.ThrowIfNull(wirt);
        var dienst = new Verwaltungsdienst(wirt, speicher, zeit, voraussetzungen: voraussetzungen);
        return new Verwaltungsteil(dienst, new VerwaltungViewModel(dienst, wirt.Einreihen), dienst);
    }
}

/// <summary>
/// Der geladene Verwaltungsteil: ein Modell für alle Fenster, eine Ansicht je Aufbau des Hauptfensters. Seit 0.11.7 dazu die Seite Abzeichen, die das
/// Hauptfenster ganz unten in seine Navigation stellt, solange die Admin-Sitzung offen ist (<see cref="AdminSitzungGeaendert"/>).
/// </summary>
public sealed class Verwaltungsteil : IVerwaltungsteil
{
    private readonly IDisposable? _besitz;
    private bool _offen;

    public Verwaltungsteil(IVerwaltungsdienst dienst, VerwaltungViewModel modell, IDisposable? besitz = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(modell);
        Dienst = dienst;
        Modell = modell;
        _besitz = besitz;
        _offen = modell.IstOffen;
        modell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(VerwaltungViewModel.IstOffen) || modell.IstOffen == _offen) return;
            _offen = modell.IstOffen;
            AdminSitzungGeaendert?.Invoke();
        };
    }

    public string Titel => "Verwaltung";

    public IVerwaltungsdienst Dienst { get; }

    public VerwaltungViewModel Modell { get; }

    public object AnsichtBauen() => new VerwaltungAnsicht { DataContext = Modell };

    public bool AdminSitzungOffen => Modell.IstOffen;

    /// <summary>Die Admin-Sitzung ging auf oder zu (0.11.7); auf dem Oberflächen-Thread, wie das Modell es meldet.</summary>
    public event Action? AdminSitzungGeaendert;

    /// <summary>Die Seite Abzeichen (0.11.7) auf dem Modell der Verwaltung.</summary>
    public object? AbzeichenAnsichtBauen() => new AbzeichenAnsicht { DataContext = Modell.Abzeichen };

    public void Verlassen() => Modell.BereichVerlassen();

    public void MitgliedZeigen(uint kontoId) => Modell.MitgliedZeigen(kontoId);

    public void Dispose()
    {
        Modell.Dispose();
        _besitz?.Dispose();
    }
}
