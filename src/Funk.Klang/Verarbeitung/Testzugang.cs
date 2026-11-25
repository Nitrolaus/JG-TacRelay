// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.CompilerServices;

// Die Tests prüfen interne Bausteine direkt (Übergabering zwischen Netz- und Audio-Thread, Zeitdehnung,
// Filterzustände auf Denormale). Das Attribut darf mehrfach stehen, ein gleiches aus einem anderen Modul stört nicht.
[assembly: InternalsVisibleTo("Funk.Tests")]
