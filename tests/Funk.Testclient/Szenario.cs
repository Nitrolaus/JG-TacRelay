// JG TacRelay - Maintainer: Sarevoc
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;

namespace Funk.Testclient;

/// <summary>
/// Szenarien nach server-sicherheit.md, Befund B20, bei N Funkern (Standard 40):
/// A: 5 Netze, 5 gleichzeitige Sprecher mit je 12 Hörern (60 Ströme);
/// B: 12 Sprecher mit je 15 Hörern (180 Ströme);
/// C: Durchsage an alle 39 (39 Ströme);
/// D: alle 40 in einem Netz sprechen gleichzeitig (1560 Ströme);
/// J: Kanäle mit Ch. (seit 0.6.0, 6.16): 5 Sprecher in Ch. 1 bis 5 eines Kanalnetzes mit je 7 Hörern, jeder hält dazu einen Ch.
/// eines zweiten Kanalnetzes (Vergleich für K);
/// K: wie J, und jeder Funker wechselt jede Sekunde den Ch. im zweiten Kanalnetz (FeldSetzen, STUFE3-ENTWURF 6 Last).
/// Die Felder setzt jeder Funker selbst per FeldSetzen (offene Netze, 4.5); nur der Sprecher von C braucht RAIDLEITER.
/// </summary>
internal sealed class Szenario
{
    /// <summary>Netze für A und B: 10 bis 21; für D: 30.</summary>
    public const ushort ErstesNetz = 10;
    public const ushort NetzAlle = 30;

    /// <summary>Kanalnetze für J und K (je Ruhe und Ch. 1 bis 20): 40 zum Sprechen, 41 für die Ch.-Wechsel.</summary>
    public const ushort KanalnetzSprechen = 40;
    public const ushort KanalnetzWechsel = 41;
    public const byte ChAnzahl = 20;

    /// <summary>Ob das Szenario Kanalnetze braucht (J, K); nur dann legt der Lasttest sie an (Versionstor 0.6.0).</summary>
    public static bool MitKanaelen(char kennung) => char.ToUpperInvariant(kennung) is 'J' or 'K';

    /// <summary>Kanalnetz, in dem jeder Funker während des Sprechens jede Sekunde den Ch. wechselt (K), sonst <c>null</c>.</summary>
    public ushort? ChWechselNetz { get; private init; }

    private Szenario(char kennung, string beschreibung, int anzahl)
    {
        Kennung = kennung;
        Beschreibung = beschreibung;
        Felder = new List<ushort>[anzahl];
        for (var i = 0; i < anzahl; i++) Felder[i] = [];
        Hoerer = new HashSet<int>[anzahl];
        for (var i = 0; i < anzahl; i++) Hoerer[i] = [];
    }

    public char Kennung { get; }
    public string Beschreibung { get; }

    /// <summary>Netze je Funker, in Feldreihenfolge (Feld 0, 1, ...).</summary>
    public List<ushort>[] Felder { get; }

    /// <summary>Sprecher mit ihrem Ziel.</summary>
    public Dictionary<int, Sendeziel> Sprecher { get; } = [];

    /// <summary>Wer welchen Sprecher hören muss: Hoerer[sprecher] = Empfänger.</summary>
    public HashSet<int>[] Hoerer { get; }

    /// <summary>Erwartete Ströme (Sprecher x Hörer).</summary>
    public int Stroeme => Sprecher.Keys.Sum(s => Hoerer[s].Count);

    /// <summary>Alle Netze, die dieses Szenario benutzt (werden vorher angelegt).</summary>
    public static IEnumerable<ushort> AlleNetze()
    {
        for (var i = 0; i < 12; i++) yield return (ushort)(ErstesNetz + i);
        yield return NetzAlle;
    }

    public static Szenario Bauen(char kennung, int n)
    {
        switch (char.ToUpperInvariant(kennung))
        {
            case 'A':
            {
                var s = new Szenario('A', "5 Netze, 5 gleichzeitige Sprecher mit je 12 Hörern", n);
                var sprecher = Math.Min(5, n);
                var pool = n - sprecher;
                var jeNetz = Math.Min(12, pool);
                for (var k = 0; k < sprecher; k++)
                {
                    var netz = (ushort)(ErstesNetz + k);
                    s.Sprecher[k] = Sendeziel.Netz(netz);
                    s.Felder[k].Add(netz);
                    for (var j = 0; j < jeNetz; j++)
                    {
                        var h = sprecher + ((k * 12 + j) % pool);
                        if (!s.Felder[h].Contains(netz)) s.Felder[h].Add(netz);
                        s.Hoerer[k].Add(h);
                    }
                }
                return s;
            }
            case 'B':
            {
                var s = new Szenario('B', "12 Sprecher mit je 15 Hörern", n);
                var sprecher = Math.Min(12, n);
                var jeNetz = Math.Min(15, n - 1);
                for (var k = 0; k < sprecher; k++)
                {
                    var netz = (ushort)(ErstesNetz + k);
                    s.Sprecher[k] = Sendeziel.Netz(netz);
                    s.Felder[k].Add(netz);
                    for (var j = 0; j < jeNetz; j++)
                    {
                        // k + 1 + x mit x in 0..n-2 trifft nie den Sprecher selbst; 15 aufeinanderfolgende x sind verschieden.
                        var h = (k + 1 + ((k * 15 + j) % (n - 1))) % n;
                        if (!s.Felder[h].Contains(netz)) s.Felder[h].Add(netz);
                        s.Hoerer[k].Add(h);
                    }
                }
                return s;
            }
            case 'C':
            {
                var s = new Szenario('C', "Durchsage an alle " + (n - 1), n);
                s.Sprecher[0] = Sendeziel.DurchsageAnAlle;
                for (var h = 1; h < n; h++) s.Hoerer[0].Add(h);
                return s;
            }
            case 'D':
            {
                var s = new Szenario('D', "alle " + n + " in einem Netz sprechen gleichzeitig", n);
                for (var k = 0; k < n; k++)
                {
                    s.Sprecher[k] = Sendeziel.Netz(NetzAlle);
                    s.Felder[k].Add(NetzAlle);
                    for (var h = 0; h < n; h++)
                        if (h != k) s.Hoerer[k].Add(h);
                }
                return s;
            }
            case 'J' or 'K':
            {
                var wechsel = char.ToUpperInvariant(kennung) == 'K';
                var s = new Szenario(wechsel ? 'K' : 'J', wechsel
                    ? "Kanäle: 5 Sprecher mit je 7 Hörern, jeder Funker wechselt jede Sekunde einen Ch."
                    : "Kanäle: 5 Sprecher mit je 7 Hörern, ohne Ch.-Wechsel (Vergleich für K)", n)
                {
                    ChWechselNetz = wechsel ? KanalnetzWechsel : null,
                };
                var sprecher = Math.Min(5, n);
                var jeKanal = Math.Min(7, (n - sprecher) / Math.Max(1, sprecher));
                for (var k = 0; k < sprecher; k++)
                {
                    var kanal = Kanalrechnung.Id(KanalnetzSprechen, (byte)(k + 1));
                    s.Sprecher[k] = Sendeziel.Netz(kanal);
                    s.Felder[k].Add(kanal);
                    for (var j = 0; j < jeKanal; j++)
                    {
                        var h = sprecher + k * jeKanal + j;
                        s.Felder[h].Add(kanal);
                        s.Hoerer[k].Add(h);
                    }
                }
                for (var i = 0; i < n; i++) s.Felder[i].Add(Kanalrechnung.Id(KanalnetzWechsel, (byte)(1 + i % ChAnzahl)));
                return s;
            }
            default:
                throw new ArgumentException("Szenario A, B, C, D, J oder K.");
        }
    }
}
