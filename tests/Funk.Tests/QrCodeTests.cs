// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using Funk.Verwaltung;

namespace Funk.Tests;

/// <summary>
/// Der eigene QR-Code (src\Funk.Verwaltung\QrCode.cs) gegen die Werte des Standards (ISO/IEC 18004): Reed-Solomon am
/// Beispiel aus Anhang I, alle 32 Formatwörter, Fassungswörter, Kapazitäten und Ausrichtungsmuster. Dazu ein Rückweg über
/// einen Leser, der hier eigenständig gebaut ist (eigene Maskenformeln, eigene Lesereihenfolge, eigene Blocktabelle und eine
/// Syndromprüfung über GF(256) mit Logarithmentafel statt der Multiplikation des Kodierers). Stimmt der Rückweg für kurze,
/// lange und mehrblockige Codes, liest auch die Kamera einer Authenticator-App, was der Admin-Teil zeigt.
/// </summary>
internal static class QrCodeTests
{
    [Test]
    public static void QrCode_ReedSolomon_BeispielDesStandards()
    {
        // ISO/IEC 18004, Anhang I: "01234567" in 1-M; die 16 Datenwörter und die 10 Fehlerkorrekturwörter.
        byte[] daten = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17];
        byte[] soll = [196, 35, 39, 119, 235, 215, 231, 226, 93, 23];
        var ecc = QrCode.ReedSolomonRest(daten, QrCode.ReedSolomonTeiler(10));
        Pruefe.BytesGleich(soll, ecc, "Fehlerkorrekturwörter 1-M");
        Pruefe.Wahr(Syndrome([.. daten, .. ecc], 10), "Syndrome null");
        Pruefe.Gleich(26, QrCode.MitFehlerkorrektur(daten, 1, QrStufe.M).Length, "1-M: 26 Codewörter");
        Pruefe.BytesGleich([.. daten, .. soll], QrCode.MitFehlerkorrektur(daten, 1, QrStufe.M), "ein Block: Daten, dann Fehlerkorrektur");
        for (var a = 0; a < 256; a += 17)
            for (var b = 0; b < 256; b += 13)
                Pruefe.Gleich(GfMal((byte)a, (byte)b), QrCode.Mal((byte)a, (byte)b), $"GF(256) {a}·{b}");
    }

    [Test]
    public static void QrCode_FormatUndFassungswoerter_WieImStandard()
    {
        // Tabelle C.1: je Stufe die Wörter für Maske 0 bis 7, schon mit 0x5412 maskiert.
        var tabelle = new Dictionary<QrStufe, int[]>
        {
            [QrStufe.L] = [0x77C4, 0x72F3, 0x7DAA, 0x789D, 0x662F, 0x6318, 0x6C41, 0x6976],
            [QrStufe.M] = [0x5412, 0x5125, 0x5E7C, 0x5B4B, 0x45F9, 0x40CE, 0x4F97, 0x4AA0],
            [QrStufe.Q] = [0x355F, 0x3068, 0x3F31, 0x3A06, 0x24B4, 0x2183, 0x2EDA, 0x2BED],
            [QrStufe.H] = [0x1689, 0x13BE, 0x1CE7, 0x19D0, 0x0762, 0x0255, 0x0D0C, 0x083B],
        };
        foreach (var (stufe, woerter) in tabelle)
            for (var m = 0; m < 8; m++)
                Pruefe.Gleich(woerter[m], QrCode.Formatwort(stufe, m), $"Formatwort {stufe}, Maske {m}");
        // Tabelle D.1
        foreach (var (fassung, wort) in new[] { (7, 0x07C94), (8, 0x085BC), (9, 0x09A99), (10, 0x0A4D3), (40, 0x28C69) })
            Pruefe.Gleich(wort, QrCode.Fassungswort(fassung), "Fassungswort " + fassung);
    }

    [Test]
    public static void QrCode_KapazitaetUndAusrichtung_WieImStandard()
    {
        Pruefe.Gleich(14, QrCode.Bytegrenze(1, QrStufe.M), "1-M: 14 Bytes");
        Pruefe.Gleich(213, QrCode.Bytegrenze(10, QrStufe.M), "10-M: 213 Bytes");
        Pruefe.Gleich(2953, QrCode.Bytegrenze(40, QrStufe.L), "40-L: 2953 Bytes");
        Pruefe.Gleich(1273, QrCode.Bytegrenze(40, QrStufe.H), "40-H: 1273 Bytes");
        Pruefe.Wahr(QrCode.Ausrichtung(1).Length == 0, "1: keine Ausrichtung");
        Pruefe.Wahr(QrCode.Ausrichtung(2).SequenceEqual([6, 18]), "2: 6, 18");
        Pruefe.Wahr(QrCode.Ausrichtung(7).SequenceEqual([6, 22, 38]), "7: 6, 22, 38");
        Pruefe.Wahr(QrCode.Ausrichtung(32).SequenceEqual([6, 34, 60, 86, 112, 138]), "32: der Sonderfall mit Schritt 26");
        Pruefe.Wahr(QrCode.Ausrichtung(40).SequenceEqual([6, 30, 58, 86, 114, 142, 170]), "40");
        Pruefe.Gleich(208, QrCode.Rohmodule(1), "1: 208 Module für Daten und Fehlerkorrektur");
        Pruefe.Gleich(29648, QrCode.Rohmodule(40), "40: 29648 Module");

        // Grenzen: genau voll passt, ein Byte mehr nimmt die nächste Fassung, über 40 geht es nicht.
        Pruefe.Gleich(1, QrCode.Kodieren(new byte[14]).Fassung, "14 Bytes in 1-M");
        Pruefe.Gleich(2, QrCode.Kodieren(new byte[15]).Fassung, "15 Bytes in 2-M");
        Pruefe.Gleich(40, QrCode.Kodieren(new byte[2953], QrStufe.L).Fassung, "2953 Bytes in 40-L");
        Pruefe.Wirft<ArgumentException>(() => QrCode.Kodieren(new byte[2954], QrStufe.L), "2954 Bytes gehen nicht");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => QrCode.Kodieren("x", maske: 8), "Maske 8 gibt es nicht");
    }

    [Test]
    public static void QrCode_Rueckweg_EigenerLeser()
    {
        var link = "otpauth://totp/JG%20TacRelay:Nordlicht?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=JG%20TacRelay&algorithm=SHA1&digits=6&period=30";
        var faelle = new List<(byte[] Daten, QrStufe Stufe, int? Maske)>
        {
            (Encoding.UTF8.GetBytes("HALLO"), QrStufe.M, null),
            (Encoding.UTF8.GetBytes(link), QrStufe.M, null),
            (Encoding.UTF8.GetBytes("Schlüssel für Bo äöü ß"), QrStufe.Q, 5),
            (Enumerable.Range(0, 200).Select(i => (byte)(i * 7)).ToArray(), QrStufe.M, null),
            (Enumerable.Range(0, 300).Select(i => (byte)(i * 11)).ToArray(), QrStufe.M, null),
        };
        for (var m = 0; m < 8; m++) faelle.Add((Encoding.UTF8.GetBytes(link), QrStufe.L, m));
        foreach (var (daten, stufe, maske) in faelle)
        {
            var q = QrCode.Kodieren(daten, stufe, maske);
            var was = $"{daten.Length} Bytes, {stufe}, Fassung {q.Fassung}, Maske {q.Maske}";
            if (maske is { } m) Pruefe.Gleich(m, q.Maske, was + ": vorgegebene Maske");
            Pruefe.Gleich(17 + (4 * q.Fassung), q.Groesse, was + ": Seite");
            Pruefe.BytesGleich(daten, Lesen(q, out var gelesen, out var gelesenMaske), was + ": Rückweg");
            Pruefe.Gleich(stufe, gelesen, was + ": Stufe aus dem Formatwort");
            Pruefe.Gleich(q.Maske, gelesenMaske, was + ": Maske aus dem Formatwort");
            SuchmusterPruefen(q, was);
        }
        Pruefe.Wahr(QrCode.Kodieren(link).Fassung >= 7, "der Link braucht ein Fassungswort (ab 7)");
        Pruefe.Wahr(QrCode.Kodieren(new byte[300]).Fassung >= 10, "300 Bytes: Zeichenzahl mit 16 Bit");

        // Die Zeichnung: Ruhezone 4 Module, eine Geometrie mit genau den dunklen Modulen.
        var code = QrCode.Kodieren(link);
        Pruefe.Gleich(code.Groesse + (2 * QrBild.Ruhezone), QrBild.Seite(code), "Seite mit Ruhezone");
        var dunkel = 0;
        for (var y = 0; y < code.Groesse; y++)
            for (var x = 0; x < code.Groesse; x++)
                if (code.Modul(x, y)) dunkel++;
        var flaeche = QrBild.Geometrie(code).GetArea(0.001, System.Windows.Media.ToleranceType.Absolute);
        Verarbeitung.PruefeWert.Nahe(dunkel, flaeche, 0.5, "Fläche der Geometrie = dunkle Module");
        Pruefe.Falsch(code.Modul(-1, 0) || code.Modul(code.Groesse, 0), "außerhalb hell");
    }

    // ---------------------------------------------------------------- eigener Leser

    // Blöcke und Fehlerkorrekturwörter je Block, aus Tabelle 9 des Standards, nur was die Fälle oben brauchen.
    private static readonly Dictionary<(int, QrStufe), (int Bloecke, int Ecc)> Blocktabelle = new()
    {
        [(1, QrStufe.M)] = (1, 10), [(2, QrStufe.M)] = (1, 16), [(3, QrStufe.M)] = (1, 26), [(4, QrStufe.M)] = (2, 18),
        [(5, QrStufe.M)] = (2, 24), [(6, QrStufe.M)] = (4, 16), [(7, QrStufe.M)] = (4, 18), [(8, QrStufe.M)] = (4, 22),
        [(9, QrStufe.M)] = (5, 22), [(10, QrStufe.M)] = (5, 26), [(11, QrStufe.M)] = (5, 30), [(12, QrStufe.M)] = (8, 22), [(13, QrStufe.M)] = (9, 22),
        [(1, QrStufe.Q)] = (1, 13), [(2, QrStufe.Q)] = (1, 22), [(3, QrStufe.Q)] = (2, 18),
        [(5, QrStufe.L)] = (1, 26), [(6, QrStufe.L)] = (2, 18), [(7, QrStufe.L)] = (2, 20),
    };

    /// <summary>Liest einen Code zurück: Format (beide Kopien), Demaskieren, Lesereihenfolge, Blöcke, Syndrome, Bytefolge.</summary>
    private static byte[] Lesen(QrCode q, out QrStufe stufe, out int maske)
    {
        var n = q.Groesse;
        int wort = 0, zweite = 0;
        for (var i = 0; i <= 5; i++) wort |= Bit(q.Modul(8, i)) << i;
        wort |= Bit(q.Modul(8, 7)) << 6;
        wort |= Bit(q.Modul(8, 8)) << 7;
        wort |= Bit(q.Modul(7, 8)) << 8;
        for (var i = 9; i < 15; i++) wort |= Bit(q.Modul(14 - i, 8)) << i;
        for (var i = 0; i < 8; i++) zweite |= Bit(q.Modul(n - 1 - i, 8)) << i;
        for (var i = 8; i < 15; i++) zweite |= Bit(q.Modul(8, n - 15 + i)) << i;
        Pruefe.Gleich(wort, zweite, "beide Kopien des Formatworts");
        Pruefe.Wahr(q.Modul(8, n - 8), "das dunkle Modul");
        var daten5 = (wort ^ 0x5412) >> 10;
        stufe = (daten5 >> 3) switch { 1 => QrStufe.L, 0 => QrStufe.M, 3 => QrStufe.Q, _ => QrStufe.H };
        maske = daten5 & 7;
        if (q.Fassung >= 7)
        {
            int fw = 0, fw2 = 0;
            for (var i = 0; i < 18; i++)
            {
                fw |= Bit(q.Modul(n - 11 + (i % 3), i / 3)) << i;
                fw2 |= Bit(q.Modul(i / 3, n - 11 + (i % 3))) << i;
            }
            Pruefe.Gleich(q.Fassung, fw >> 12, "Fassung aus dem Fassungswort");
            Pruefe.Gleich(fw, fw2, "beide Kopien des Fassungsworts");
        }

        // Lesereihenfolge: Spaltenpaare von rechts, abwechselnd hoch und runter, Taktspalte 6 übersprungen.
        var bits = new List<bool>();
        var spalte = n - 1;
        var hoch = true;
        while (spalte > 0)
        {
            if (spalte == 6) spalte--;
            for (var s = 0; s < n; s++)
            {
                var y = hoch ? n - 1 - s : s;
                for (var dx = 0; dx < 2; dx++)
                {
                    var x = spalte - dx;
                    if (q.IstFunktion(x, y)) continue;
                    bits.Add(q.Modul(x, y) ^ Maske(maske, y, x));
                }
            }
            hoch = !hoch;
            spalte -= 2;
        }
        var roh = QrCode.Rohmodule(q.Fassung) / 8;
        Pruefe.Wahr(bits.Count >= roh * 8 && bits.Count - (roh * 8) < 8, "Module und Restbits: " + bits.Count);
        var woerter = new byte[roh];
        for (var i = 0; i < roh * 8; i++) if (bits[i]) woerter[i / 8] |= (byte)(0x80 >> (i % 8));

        // Blöcke entflechten: erst die Datenwörter reihum (kurze Blöcke zuerst, ohne ihr letztes), dann die Fehlerkorrektur.
        Pruefe.Wahr(Blocktabelle.TryGetValue((q.Fassung, stufe), out var bt), "Blocktabelle kennt " + q.Fassung + "-" + stufe);
        var kurze = bt.Bloecke - (roh % bt.Bloecke);
        var kurzDaten = (roh / bt.Bloecke) - bt.Ecc;
        var bloecke = Enumerable.Range(0, bt.Bloecke).Select(b => new List<byte>()).ToList();
        var k = 0;
        for (var i = 0; i <= kurzDaten; i++)
            for (var b = 0; b < bt.Bloecke; b++)
                if (i < kurzDaten || b >= kurze) bloecke[b].Add(woerter[k++]);
        var daten = bloecke.SelectMany(b => b).ToArray();
        for (var i = 0; i < bt.Ecc; i++)
            for (var b = 0; b < bt.Bloecke; b++)
                bloecke[b].Add(woerter[k++]);
        Pruefe.Gleich(roh, k, "alle Codewörter verteilt");
        foreach (var b in bloecke) Pruefe.Wahr(Syndrome([.. b], bt.Ecc), "Syndrome eines Blocks null");

        // Bytefolge: Modus 0100, Zeichenzahl (8 oder 16 Bit), Bytes, Endmarke, Füllbytes EC 11.
        var pos = 0;
        int Lies(int anzahl)
        {
            var v = 0;
            for (var i = 0; i < anzahl; i++, pos++) v = (v << 1) | ((daten[pos / 8] >> (7 - (pos % 8))) & 1);
            return v;
        }
        Pruefe.Gleich(0b0100, Lies(4), "Byte-Modus");
        var laenge = Lies(q.Fassung <= 9 ? 8 : 16);
        var ergebnis = new byte[laenge];
        for (var i = 0; i < laenge; i++) ergebnis[i] = (byte)Lies(8);
        var frei = (daten.Length * 8) - pos;
        if (frei >= 4) Pruefe.Gleich(0, Lies(4), "Endmarke");
        pos = (pos + 7) / 8 * 8;
        for (var fuell = 0xEC; pos < daten.Length * 8; fuell ^= 0xEC ^ 0x11) Pruefe.Gleich(fuell, Lies(8), "Füllbyte");
        return ergebnis;
    }

    private static int Bit(bool b) => b ? 1 : 0;

    /// <summary>Die acht Masken aus Tabelle 10, hier mit i = Zeile, j = Spalte wie im Standard geschrieben.</summary>
    private static bool Maske(int m, int i, int j) => m switch
    {
        0 => (i + j) % 2 == 0,
        1 => i % 2 == 0,
        2 => j % 3 == 0,
        3 => (i + j) % 3 == 0,
        4 => ((i / 2) + (j / 3)) % 2 == 0,
        5 => ((i * j) % 2) + ((i * j) % 3) == 0,
        6 => (((i * j) % 2) + ((i * j) % 3)) % 2 == 0,
        _ => (((i * j) % 3) + ((i + j) % 2)) % 2 == 0,
    };

    /// <summary>Die drei Suchmuster samt hellem Rand und die Taktlinien.</summary>
    private static void SuchmusterPruefen(QrCode q, string was)
    {
        var n = q.Groesse;
        foreach (var (x0, y0) in new[] { (0, 0), (n - 7, 0), (0, n - 7) })
        {
            for (var dy = -1; dy <= 7; dy++)
            {
                for (var dx = -1; dx <= 7; dx++)
                {
                    var rand = dx is -1 or 7 || dy is -1 or 7;
                    var ring = !rand && (dx is 1 or 5 && dy is >= 1 and <= 5 || dy is 1 or 5 && dx is >= 1 and <= 5);
                    Pruefe.Gleich(!rand && !ring, q.Modul(x0 + dx, y0 + dy), $"{was}: Suchmuster bei {x0 + dx},{y0 + dy}");
                }
            }
        }
        for (var i = 8; i < n - 8; i++)
        {
            Pruefe.Gleich(i % 2 == 0, q.Modul(i, 6), was + ": waagerechte Taktlinie");
            Pruefe.Gleich(i % 2 == 0, q.Modul(6, i), was + ": senkrechte Taktlinie");
        }
    }

    // ---------------------------------------------------------------- GF(256) mit Logarithmentafel

    private static readonly byte[] Exp = new byte[512];
    private static readonly int[] Log = TafelnBauen();

    private static int[] TafelnBauen()
    {
        var log = new int[256];
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            log[x] = i;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;
        }
        for (var i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        return log;
    }

    private static byte GfMal(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    /// <summary>Ein gültiges Codewort hat bei α^0 bis α^(ecc-1) den Wert null (Horner-Schema).</summary>
    private static bool Syndrome(byte[] codewort, int ecc)
    {
        for (var i = 0; i < ecc; i++)
        {
            byte s = 0;
            foreach (var c in codewort) s = (byte)(GfMal(s, Exp[i]) ^ c);
            if (s != 0) return false;
        }
        return true;
    }
}
