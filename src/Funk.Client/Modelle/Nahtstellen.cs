// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Design;

namespace Funk.Client.Modelle;

/// <summary>
/// Fenster der Oberfläche; im Betrieb die App, in Tests eine Attrappe. Die Schwelle hat kein eigenes Fenster (A26); seit 0.10.0 ist
/// sie eine Seite im Hauptfenster (<see cref="HauptViewModel.SprachaktivierungEinstellenCommand"/>).
/// </summary>
public interface IFensterDienst
{
    /// <summary>Öffnet das Funkfeld und holt es nach vorn.</summary>
    void FunkfeldZeigen();

    /// <summary>
    /// Farbstellung wechseln (A31), sofort für Hauptfenster und Funkfeld. Die Stile lösen ihre Pinsel beim Bauen auf
    /// (StaticResource); die App baut deshalb beide Fenster am selben Platz neu. Seit 19.10.2026 gibt es nur Anthrazit und unter
    /// Einstellungen keine Wahl mehr; der Weg bleibt für eine spätere zweite Farbstellung.
    /// </summary>
    void FarbenAnwenden(Farbstellung stellung);

    /// <summary>Öffnet einen Ordner im Explorer (Einstellungen, Ablage); im Betrieb die App, in Tests eine Attrappe.</summary>
    void OrdnerOeffnen(string ordner);
}

/// <summary>Zwischenablage für "Kopieren" (Gerätekennung im Warteraum); im Betrieb die von Windows, in Tests eine Attrappe.</summary>
public interface IZwischenablage
{
    /// <summary>Legt <paramref name="text"/> in die Zwischenablage; <c>false</c>, wenn Windows sie gerade nicht hergibt.</summary>
    bool Kopieren(string text);
}
