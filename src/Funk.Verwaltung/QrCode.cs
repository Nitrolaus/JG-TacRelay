// Übertragung nach C# (deutsche Bezeichner, nur Byte-Modus, eigene Strafpunktzählung) der
// QR Code generator library von Project Nayuki:
//
// Copyright (c) Project Nayuki. (MIT License)
// https://www.nayuki.io/page/qr-code-generator-library
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
// persons to whom the Software is furnished to do so, subject to the following conditions:
// - The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
//   Software.
// - The Software is provided "as is", without warranty of any kind, express or implied, including but not limited to the
//   warranties of merchantability, fitness for a particular purpose and noninfringement. In no event shall the authors or
//   copyright holders be liable for any claim, damages or other liability, whether in an action of contract, tort or
//   otherwise, arising from, out of or in connection with the Software or the use or other dealings in the Software.

using System.Text;

namespace Funk.Verwaltung;

/// <summary>Fehlerkorrekturstufe eines QR-Codes (ISO/IEC 18004): etwa 7, 15, 25 und 30 Prozent.</summary>
public enum QrStufe
{
    L,
    M,
    Q,
    H,
}

/// <summary>
/// QR-Code (Modell 2, ISO/IEC 18004) als Quelltext im Verwaltungsteil statt als Paket (ANMELDUNG-SICHERHEIT, Client: "eigener
/// Code oder MIT-Bibliothek"; keine Pakete ohne Freigabe). Aufbau, Formeln und Schleifen folgen der QR Code generator
/// library von Project Nayuki (MIT-Lizenz, Hinweis oben und in THIRD-PARTY-NOTICES.txt; Gegenprüfung Stufe 1: vorher stand
/// hier "selbst gebaut", ohne Lizenzangabe). Nur Byte-Modus, weil ein otpauth-Link (3.9) Klein- und
/// Sonderzeichen trägt; Fassungen 1 bis 40, Maske nach der kleinsten Strafpunktzahl. Die Tabellen und Formeln sind die
/// des Standards: Fehlerkorrekturwörter und Blöcke je Fassung, Reed-Solomon über GF(256) mit dem Polynom 0x11D,
/// BCH-Codes für Format (0x537, Maske 0x5412) und Fassung (0x1F25).
/// Es gibt nur <see cref="Modul"/> zum Lesen; gezeichnet wird in <see cref="QrBild"/>. Geprüft in QrCodeTests mit den
/// Formatwörtern und Fassungswörtern des Standards, einem Reed-Solomon-Beispiel und einem Rückweg über einen eigenen
/// Leser.
/// </summary>
public sealed class QrCode
{
    // Fehlerkorrekturwörter je Block und Zahl der Blöcke, je Stufe (L, M, Q, H) und Fassung (Index 0 unbenutzt).
    private static readonly sbyte[][] EccJeBlock =
    [
        [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
        [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    ];

    private static readonly sbyte[][] Bloecke =
    [
        [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
        [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
        [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
        [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
    ];

    private readonly bool[,] _module;
    private readonly bool[,] _funktion;

    private QrCode(int fassung, QrStufe stufe, int maske, bool[,] module, bool[,] funktion)
    {
        Fassung = fassung;
        Stufe = stufe;
        Maske = maske;
        _module = module;
        _funktion = funktion;
    }

    /// <summary>Fassung 1 bis 40; die Seite misst 17 + 4 · Fassung Module.</summary>
    public int Fassung { get; }

    public QrStufe Stufe { get; }

    /// <summary>Gewählte Maske 0 bis 7.</summary>
    public int Maske { get; }

    /// <summary>Seitenlänge in Modulen, ohne Ruhezone.</summary>
    public int Groesse => (Fassung * 4) + 17;

    /// <summary>Ob das Modul in Spalte <paramref name="x"/>, Zeile <paramref name="y"/> dunkel ist; außerhalb hell (Ruhezone).</summary>
    public bool Modul(int x, int y) => x >= 0 && y >= 0 && x < Groesse && y < Groesse && _module[y, x];

    /// <summary>Ob das Modul zu einem Funktionsmuster gehört (Tests).</summary>
    internal bool IstFunktion(int x, int y) => _funktion[y, x];

    /// <summary>Kodiert <paramref name="text"/> als UTF-8 im Byte-Modus in der kleinsten Fassung, die passt.</summary>
    public static QrCode Kodieren(string text, QrStufe stufe = QrStufe.M, int? maske = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Kodieren(Encoding.UTF8.GetBytes(text), stufe, maske);
    }

    /// <summary>Kodiert Bytes im Byte-Modus; wirft <see cref="ArgumentException"/>, wenn sie auch in Fassung 40 nicht passen.</summary>
    public static QrCode Kodieren(ReadOnlySpan<byte> daten, QrStufe stufe = QrStufe.M, int? maske = null)
    {
        if (maske is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(maske));
        var fassung = 1;
        while (fassung <= 40 && BitsFuerDaten(fassung, daten.Length) > Datenwoerter(fassung, stufe) * 8) fassung++;
        if (fassung > 40) throw new ArgumentException("Zu lang für einen QR-Code.", nameof(daten));
        var woerter = Datenwoerter(fassung, stufe);

        // Bitfolge: Modus 0100 (Byte), Zeichenzahl, Bytes, Endmarke, auf volle Bytes, Füllbytes 0xEC 0x11 im Wechsel.
        var bits = new Bitpuffer();
        bits.Anhaengen(0b0100, 4);
        bits.Anhaengen(daten.Length, fassung <= 9 ? 8 : 16);
        foreach (var b in daten) bits.Anhaengen(b, 8);
        bits.Anhaengen(0, Math.Min(4, (woerter * 8) - bits.Laenge));
        bits.Anhaengen(0, (8 - (bits.Laenge % 8)) % 8);
        for (var fuell = 0xEC; bits.Laenge < woerter * 8; fuell ^= 0xEC ^ 0x11) bits.Anhaengen(fuell, 8);
        var codewoerter = MitFehlerkorrektur(bits.AlsBytes(), fassung, stufe);

        var groesse = (fassung * 4) + 17;
        var module = new bool[groesse, groesse];
        var funktion = new bool[groesse, groesse];
        Funktionsmuster(fassung, module, funktion);
        Datenmodule(codewoerter, module, funktion);

        var beste = maske ?? -1;
        if (beste < 0)
        {
            var wenigste = int.MaxValue;
            for (var m = 0; m < 8; m++)
            {
                Maskieren(m, module, funktion);
                Formatbits(stufe, m, module, funktion);
                var strafe = Strafpunkte(module);
                if (strafe < wenigste)
                {
                    wenigste = strafe;
                    beste = m;
                }
                Maskieren(m, module, funktion);
            }
        }
        Maskieren(beste, module, funktion);
        Formatbits(stufe, beste, module, funktion);
        return new QrCode(fassung, stufe, beste, module, funktion);
    }

    /// <summary>Datenwörter einer Fassung und Stufe: alle Codewörter ohne die zur Fehlerkorrektur.</summary>
    public static int Datenwoerter(int fassung, QrStufe stufe)
    {
        if (fassung is < 1 or > 40) throw new ArgumentOutOfRangeException(nameof(fassung));
        return (Rohmodule(fassung) / 8) - (EccJeBlock[(int)stufe][fassung] * Bloecke[(int)stufe][fassung]);
    }

    /// <summary>Wie viele Bytes im Byte-Modus höchstens hineinpassen.</summary>
    public static int Bytegrenze(int fassung, QrStufe stufe) => ((Datenwoerter(fassung, stufe) * 8) - 4 - (fassung <= 9 ? 8 : 16)) / 8;

    private static int BitsFuerDaten(int fassung, int bytes) => 4 + (fassung <= 9 ? 8 : 16) + (bytes * 8)
        + (fassung <= 9 && bytes > 255 ? int.MaxValue / 2 : 0);

    /// <summary>Module für Daten und Fehlerkorrektur (ohne Funktionsmuster, Format und Fassung), einschließlich Restbits.</summary>
    internal static int Rohmodule(int fassung)
    {
        var ergebnis = (((16 * fassung) + 128) * fassung) + 64;
        if (fassung >= 2)
        {
            var ausrichtung = (fassung / 7) + 2;
            ergebnis -= (((25 * ausrichtung) - 10) * ausrichtung) - 55;
            if (fassung >= 7) ergebnis -= 36;
        }
        return ergebnis;
    }

    /// <summary>Mittelpunkte der Ausrichtungsmuster je Achse; leer für Fassung 1.</summary>
    internal static int[] Ausrichtung(int fassung)
    {
        if (fassung == 1) return [];
        var anzahl = (fassung / 7) + 2;
        var schritt = fassung == 32 ? 26 : (((fassung * 4) + (anzahl * 2) + 1) / ((anzahl * 2) - 2)) * 2;
        var ergebnis = new int[anzahl];
        ergebnis[0] = 6;
        for (int i = anzahl - 1, pos = (fassung * 4) + 17 - 7; i >= 1; i--, pos -= schritt) ergebnis[i] = pos;
        return ergebnis;
    }

    /// <summary>Formatwort (15 Bit) für Stufe und Maske: 5 Bit Daten, 10 Bit BCH, maskiert mit 0x5412.</summary>
    internal static int Formatwort(QrStufe stufe, int maske)
    {
        var stufenbits = stufe switch
        {
            QrStufe.L => 1,
            QrStufe.M => 0,
            QrStufe.Q => 3,
            _ => 2,
        };
        var daten = (stufenbits << 3) | maske;
        var rest = daten;
        for (var i = 0; i < 10; i++) rest = (rest << 1) ^ ((rest >> 9) * 0x537);
        return ((daten << 10) | rest) ^ 0x5412;
    }

    /// <summary>Fassungswort (18 Bit) ab Fassung 7: 6 Bit Fassung, 12 Bit BCH.</summary>
    internal static int Fassungswort(int fassung)
    {
        var rest = fassung;
        for (var i = 0; i < 12; i++) rest = (rest << 1) ^ ((rest >> 11) * 0x1F25);
        return (fassung << 12) | rest;
    }

    // ---------------------------------------------------------------- Fehlerkorrektur

    /// <summary>Teilt die Datenwörter in Blöcke, hängt je Block Reed-Solomon an und verschränkt alles (8.6 des Standards).</summary>
    internal static byte[] MitFehlerkorrektur(byte[] daten, int fassung, QrStufe stufe)
    {
        var bloecke = Bloecke[(int)stufe][fassung];
        var ecc = EccJeBlock[(int)stufe][fassung];
        var roh = Rohmodule(fassung) / 8;
        var kurze = bloecke - (roh % bloecke);
        var kurzLaenge = roh / bloecke;
        var teiler = ReedSolomonTeiler(ecc);
        var liste = new List<byte[]>(bloecke);
        for (int i = 0, k = 0; i < bloecke; i++)
        {
            var laenge = kurzLaenge - ecc + (i < kurze ? 0 : 1);
            var block = new byte[kurzLaenge + 1];
            Array.Copy(daten, k, block, 0, laenge);
            k += laenge;
            var rest = ReedSolomonRest(block.AsSpan(0, laenge), teiler);
            // Kurze Blöcke tragen an der Stelle des fehlenden Datenworts eine Lücke, damit alle gleich lang sind.
            rest.CopyTo(block, laenge + (i < kurze ? 1 : 0));
            liste.Add(block);
        }
        var ergebnis = new byte[roh];
        var n = 0;
        for (var i = 0; i < kurzLaenge + 1; i++)
        {
            for (var j = 0; j < liste.Count; j++)
            {
                if (i == kurzLaenge - ecc && j < kurze) continue;
                ergebnis[n++] = liste[j][i];
            }
        }
        return ergebnis;
    }

    /// <summary>Teilerpolynom vom Grad <paramref name="grad"/> mit den Nullstellen α^0 bis α^(grad-1), ohne führende 1.</summary>
    internal static byte[] ReedSolomonTeiler(int grad)
    {
        var ergebnis = new byte[grad];
        ergebnis[grad - 1] = 1;
        byte wurzel = 1;
        for (var i = 0; i < grad; i++)
        {
            for (var j = 0; j < grad; j++)
            {
                ergebnis[j] = Mal(ergebnis[j], wurzel);
                if (j + 1 < grad) ergebnis[j] ^= ergebnis[j + 1];
            }
            wurzel = Mal(wurzel, 2);
        }
        return ergebnis;
    }

    /// <summary>Rest der Polynomdivision: die Fehlerkorrekturwörter eines Blocks.</summary>
    internal static byte[] ReedSolomonRest(ReadOnlySpan<byte> daten, byte[] teiler)
    {
        var ergebnis = new byte[teiler.Length];
        foreach (var b in daten)
        {
            var faktor = (byte)(b ^ ergebnis[0]);
            Array.Copy(ergebnis, 1, ergebnis, 0, ergebnis.Length - 1);
            ergebnis[^1] = 0;
            for (var i = 0; i < ergebnis.Length; i++) ergebnis[i] ^= Mal(teiler[i], faktor);
        }
        return ergebnis;
    }

    /// <summary>Multiplikation in GF(2^8) mit dem Polynom x^8 + x^4 + x^3 + x^2 + 1 (0x11D).</summary>
    internal static byte Mal(byte x, byte y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    // ---------------------------------------------------------------- Aufbau der Matrix

    private static void Setzen(bool[,] module, bool[,] funktion, int x, int y, bool dunkel)
    {
        module[y, x] = dunkel;
        funktion[y, x] = true;
    }

    private static void Funktionsmuster(int fassung, bool[,] module, bool[,] funktion)
    {
        var groesse = module.GetLength(0);
        // Taktlinien
        for (var i = 0; i < groesse; i++)
        {
            Setzen(module, funktion, 6, i, i % 2 == 0);
            Setzen(module, funktion, i, 6, i % 2 == 0);
        }
        // Suchmuster mit Trennrand in drei Ecken
        foreach (var (mx, my) in new[] { (3, 3), (groesse - 4, 3), (3, groesse - 4) })
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    int x = mx + dx, y = my + dy;
                    if (x < 0 || y < 0 || x >= groesse || y >= groesse) continue;
                    var abstand = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Setzen(module, funktion, x, y, abstand != 2 && abstand != 4);
                }
            }
        }
        // Ausrichtungsmuster, außer an den drei Suchmustern
        var pos = Ausrichtung(fassung);
        for (var i = 0; i < pos.Length; i++)
        {
            for (var j = 0; j < pos.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == pos.Length - 1) || (i == pos.Length - 1 && j == 0)) continue;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        Setzen(module, funktion, pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }
        // Platz für das Formatwort (vorläufig hell) und das Fassungswort
        Formatbits(QrStufe.M, 0, module, funktion);
        if (fassung >= 7)
        {
            var wort = Fassungswort(fassung);
            for (var i = 0; i < 18; i++)
            {
                var bit = ((wort >> i) & 1) != 0;
                int a = groesse - 11 + (i % 3), b = i / 3;
                Setzen(module, funktion, a, b, bit);
                Setzen(module, funktion, b, a, bit);
            }
        }
    }

    private static void Formatbits(QrStufe stufe, int maske, bool[,] module, bool[,] funktion)
    {
        var groesse = module.GetLength(0);
        var wort = Formatwort(stufe, maske);
        bool Bit(int i) => ((wort >> i) & 1) != 0;
        for (var i = 0; i <= 5; i++) Setzen(module, funktion, 8, i, Bit(i));
        Setzen(module, funktion, 8, 7, Bit(6));
        Setzen(module, funktion, 8, 8, Bit(7));
        Setzen(module, funktion, 7, 8, Bit(8));
        for (var i = 9; i < 15; i++) Setzen(module, funktion, 14 - i, 8, Bit(i));
        for (var i = 0; i < 8; i++) Setzen(module, funktion, groesse - 1 - i, 8, Bit(i));
        for (var i = 8; i < 15; i++) Setzen(module, funktion, 8, groesse - 15 + i, Bit(i));
        // Das dunkle Modul neben dem unteren Suchmuster, immer dunkel.
        Setzen(module, funktion, 8, groesse - 8, true);
    }

    /// <summary>Legt die Codewörter im Zickzack von rechts unten nach links ab, in Spaltenpaaren, ohne die Taktspalte 6.</summary>
    private static void Datenmodule(byte[] codewoerter, bool[,] module, bool[,] funktion)
    {
        var groesse = module.GetLength(0);
        var i = 0;
        foreach (var (x, y) in Zickzack(groesse))
        {
            if (funktion[y, x]) continue;
            if (i < codewoerter.Length * 8)
            {
                module[y, x] = ((codewoerter[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                i++;
            }
        }
    }

    /// <summary>Die Lesereihenfolge aller Module (auch der Funktionsmuster, die übersprungen werden).</summary>
    internal static IEnumerable<(int X, int Y)> Zickzack(int groesse)
    {
        for (var rechts = groesse - 1; rechts >= 1; rechts -= 2)
        {
            if (rechts == 6) rechts = 5;
            var aufwaerts = ((rechts + 1) & 2) == 0;
            for (var schritt = 0; schritt < groesse; schritt++)
            {
                var y = aufwaerts ? groesse - 1 - schritt : schritt;
                yield return (rechts, y);
                yield return (rechts - 1, y);
            }
        }
    }

    internal static bool Maskenbit(int maske, int x, int y) => maske switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => ((x / 3) + (y / 2)) % 2 == 0,
        5 => (x * y % 2) + (x * y % 3) == 0,
        6 => ((x * y % 2) + (x * y % 3)) % 2 == 0,
        _ => (((x + y) % 2) + (x * y % 3)) % 2 == 0,
    };

    private static void Maskieren(int maske, bool[,] module, bool[,] funktion)
    {
        var groesse = module.GetLength(0);
        for (var y = 0; y < groesse; y++)
            for (var x = 0; x < groesse; x++)
                if (!funktion[y, x] && Maskenbit(maske, x, y)) module[y, x] = !module[y, x];
    }

    /// <summary>
    /// Strafpunkte nach 8.8.2 des Standards: lange Läufe gleicher Farbe (3 + Überlänge), 2x2-Blöcke (3), Folgen wie ein
    /// Suchmuster 1:1:3:1:1 mit vier hellen Modulen daneben (40), und die Abweichung des Dunkelanteils von 50 % (10 je 5 %).
    /// </summary>
    internal static int Strafpunkte(bool[,] module)
    {
        var groesse = module.GetLength(0);
        var strafe = 0;
        for (var richtung = 0; richtung < 2; richtung++)
        {
            for (var a = 0; a < groesse; a++)
            {
                var zeile = new bool[groesse];
                for (var b = 0; b < groesse; b++) zeile[b] = richtung == 0 ? module[a, b] : module[b, a];
                var lauf = 1;
                for (var b = 1; b <= groesse; b++)
                {
                    if (b < groesse && zeile[b] == zeile[b - 1])
                    {
                        lauf++;
                        continue;
                    }
                    if (lauf >= 5) strafe += 3 + (lauf - 5);
                    lauf = 1;
                }
                strafe += 40 * SuchmusterAehnlich(zeile);
            }
        }
        for (var y = 0; y + 1 < groesse; y++)
        {
            for (var x = 0; x + 1 < groesse; x++)
            {
                var c = module[y, x];
                if (c == module[y, x + 1] && c == module[y + 1, x] && c == module[y + 1, x + 1]) strafe += 3;
            }
        }
        var dunkel = 0;
        foreach (var m in module) if (m) dunkel++;
        var prozent = dunkel * 100.0 / (groesse * groesse);
        strafe += 10 * (int)(Math.Abs(prozent - 50) / 5);
        return strafe;
    }

    /// <summary>Zählt 1011101 mit vier hellen Modulen davor oder danach; außerhalb der Matrix gilt hell.</summary>
    private static int SuchmusterAehnlich(bool[] zeile)
    {
        bool Bei(int i) => i >= 0 && i < zeile.Length && zeile[i];
        ReadOnlySpan<bool> muster = [true, false, true, true, true, false, true];
        var anzahl = 0;
        for (var start = -4; start < zeile.Length + 4; start++)
        {
            var passt = true;
            for (var k = 0; k < muster.Length && passt; k++) passt = Bei(start + k) == muster[k];
            if (!passt) continue;
            var vorne = !Bei(start - 1) && !Bei(start - 2) && !Bei(start - 3) && !Bei(start - 4);
            var hinten = !Bei(start + 7) && !Bei(start + 8) && !Bei(start + 9) && !Bei(start + 10);
            if (vorne) anzahl++;
            if (hinten) anzahl++;
        }
        return anzahl;
    }

    /// <summary>Sammelt Bits von vorn nach hinten, höchstwertiges zuerst.</summary>
    private sealed class Bitpuffer
    {
        private readonly List<bool> _bits = [];

        public int Laenge => _bits.Count;

        public void Anhaengen(int wert, int anzahl)
        {
            for (var i = anzahl - 1; i >= 0; i--) _bits.Add(((wert >> i) & 1) != 0);
        }

        public byte[] AlsBytes()
        {
            var ergebnis = new byte[_bits.Count / 8];
            for (var i = 0; i < ergebnis.Length * 8; i++)
                if (_bits[i]) ergebnis[i >> 3] |= (byte)(0x80 >> (i & 7));
            return ergebnis;
        }
    }
}
