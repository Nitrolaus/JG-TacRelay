// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;

namespace Funk.Client.Modelle;

/// <summary>Hilfen für Listen der Oberfläche.</summary>
internal static class Sammlung
{
    /// <summary>
    /// Bringt <paramref name="ziel"/> auf den Stand von <paramref name="quelle"/>, ohne vorhandene Einträge neu anzulegen:
    /// Gleiche Schlüssel behalten ihr ViewModel (Fokus, Schieberegler, Auswahl bleiben), neue kommen hinzu, fehlende
    /// fallen weg, die Reihenfolge folgt der Quelle.
    /// </summary>
    public static void Abgleichen<TQuelle, TZiel, TSchluessel>(
        ObservableCollection<TZiel> ziel,
        IEnumerable<TQuelle> quelle,
        Func<TQuelle, TSchluessel> schluesselQuelle,
        Func<TZiel, TSchluessel> schluesselZiel,
        Func<TQuelle, TZiel> anlegen,
        Action<TZiel, TQuelle> aktualisieren)
        where TSchluessel : notnull
    {
        var liste = quelle.ToList();
        var vorhanden = new Dictionary<TSchluessel, TZiel>();
        foreach (var z in ziel) vorhanden.TryAdd(schluesselZiel(z), z);
        for (var i = 0; i < liste.Count; i++)
        {
            var q = liste[i];
            var k = schluesselQuelle(q);
            if (vorhanden.Remove(k, out var z))
            {
                aktualisieren(z, q);
                var alt = ziel.IndexOf(z);
                if (alt != i) ziel.Move(alt, i);
            }
            else
            {
                ziel.Insert(i, anlegen(q));
            }
        }
        while (ziel.Count > liste.Count) ziel.RemoveAt(ziel.Count - 1);
    }
}
