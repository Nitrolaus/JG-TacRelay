// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Server.Lage;

/// <summary>Rechte eines Kontos (6.1). Standard aus der Stufe, Abweichungen als plus/minus (6.10).</summary>
[Flags]
public enum Recht : ushort
{
    Keine = 0,
    GlobalSenden = 1 << 0,
    Durchsage = 1 << 1,
    DurchsageAlle = 1 << 2,
    MehrfachSenden = 1 << 3,
    Zuweisen = 1 << 4,
    SprechanzeigeAlle = 1 << 5,
    GeraeteFreigeben = 1 << 6,
    KontenVerwalten = 1 << 7,
    NetzeVerwalten = 1 << 8,
    Alle = GlobalSenden | Durchsage | DurchsageAlle | MehrfachSenden | Zuweisen | SprechanzeigeAlle
        | GeraeteFreigeben | KontenVerwalten | NetzeVerwalten,
}

/// <summary>Tabelle 6.1 und die Rangregeln aus 6.10 und 6.12.</summary>
public static class Rechteregeln
{
    /// <summary>Rechte, die ein Konto privilegiert machen (2. Begriffe, 6.10).</summary>
    public const Recht Privilegiert = Recht.GeraeteFreigeben | Recht.KontenVerwalten | Recht.NetzeVerwalten;

    /// <summary>
    /// Die Stufe RAIDLEITER, in der Oberfläche "Führung": Global, Durchsagen, Mehrfachsenden und die Sprechanzeige aller
    /// Netze. Seit 0.3.0 ohne <c>ZUWEISEN</c>; einteilen darf nur ein ADMIN oder wer einen Auftrag hat (A37).
    /// </summary>
    public const Recht Fuehrung = Recht.GlobalSenden | Recht.Durchsage | Recht.DurchsageAlle | Recht.MehrfachSenden | Recht.SprechanzeigeAlle;

    /// <summary>Was ein Auftrag tragen kann (6.12 Regel 2): die Rechte der Führung und <c>ZUWEISEN</c>, nie ein Verwaltungsrecht.</summary>
    public const Recht Einsatzleitung = Fuehrung | Recht.Zuweisen;

    /// <summary>Was jeder Auftrag trägt; die übrigen vier Rechte der Einsatzleitung sind abwählbar (6.12).</summary>
    public const Recht EinsatzleitungPflicht = Recht.Zuweisen | Recht.SprechanzeigeAlle;

    /// <summary>Standardrechte je Stufe nach Tabelle 6.1.</summary>
    public static Recht Standard(Stufe stufe) => stufe switch
    {
        Stufe.Admin => Recht.Alle,
        Stufe.Raidleiter => Fuehrung,
        _ => Recht.Keine,
    };

    /// <summary>Wirksame dauerhafte Rechte: Standard der Stufe, dazu plus, ohne minus. Ein Auftrag kommt erst in <see cref="Konto.RechteZu"/> dazu.</summary>
    public static Recht Wirksam(Stufe stufe, Recht plus, Recht minus) => (Standard(stufe) | plus) & ~minus & Recht.Alle;

    /// <summary>Zerlegt gewünschte Rechte in die sichtbare Abweichung vom Standard der Stufe.</summary>
    public static (Recht Plus, Recht Minus) Abweichung(Stufe stufe, Recht gewuenscht)
    {
        var std = Standard(stufe);
        return (gewuenscht & ~std & Recht.Alle, std & ~gewuenscht);
    }

    public static bool Hat(this Recht rechte, Recht recht) => (rechte & recht) == recht;

    /// <summary>Privilegiert: Stufe ADMIN oder eines der Verwaltungsrechte.</summary>
    public static bool IstPrivilegiert(Stufe stufe, Recht rechte) => stufe == Stufe.Admin || (rechte & Privilegiert) != 0;

    /// <summary>
    /// Rangregel (6.10, seit 0.3.0, Funker-Modus seit 0.5.0): <c>ZUWEISEN</c> wirkt auf FUNKER und RAIDLEITER, auf ein ADMIN-Konto
    /// nur durch einen ADMIN oder solange es "Heute als Funker" spielt (6.15). Gilt für Verschieben, PlaetzeFreigeben, Aufstellungen
    /// mit Verdrängen, Schützen und Nachziehen und das Schalten einer Phase. Für Stummschalten gilt <see cref="DarfStummschalten"/>.
    /// </summary>
    public static bool DarfZuweisen(Stufe anfragender, Konto ziel, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(ziel);
        return ziel.Stufe != Stufe.Admin || anfragender == Stufe.Admin || ziel.AlsFunkerZu(jetzt);
    }

    /// <summary>
    /// Stummschalten (6.10): ein ADMIN-Konto nur durch einen ADMIN, auch im Funker-Modus. Der Betroffene hebt eine fremde
    /// Stummschaltung nie selbst auf; ein abtrünniger Beauftragter könnte den Eigentümer sonst bis zu einer Stunde stumm halten.
    /// </summary>
    public static bool DarfStummschalten(Stufe anfragender, Stufe ziel) => ziel != Stufe.Admin || anfragender == Stufe.Admin;
}
