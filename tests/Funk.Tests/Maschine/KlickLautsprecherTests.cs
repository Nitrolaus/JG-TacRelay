// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Der eigene Druck-Klick mit Lautsprechern (Echounterdrückung an, Befund der Gegenprüfung 2c): Er lief über den Loopback in die
/// AEC3-Referenz, und AEC3 dämpfte den Anlaut, der mit dem Tastendruck zusammenfällt. Seit Klangstufe A nimmt die Sendekette den
/// eigenen Druck-Klick aus der Referenz (<see cref="Klang.Maschine.Sendekette.EigenenKlickMelden"/>). Gemessen wird der Pegel der
/// ersten 100 ms jedes Spruchs im gesendeten Signal gegen denselben Ablauf ohne eigenen Klick.
/// </summary>
internal static class KlickLautsprecherTests
{
    /// <summary>Ein Ablauf: Taste drücken, nach <paramref name="reaktionMs"/> sprechen (1 s), loslassen, Pause; Pegel der Anläufe.</summary>
    internal static async Task<(double[] Anfaenge, double[] Klickrest, long Ausgeblendet)> Ablauf(int saat, bool eigenerKlick, bool ausblenden,
        Func<float, float>? echo, int reaktionMs, int sprueche = 5, float eigeneDb = FunkKlickEinstellungen.EigeneVoreinstellungDb)
    {
        await using var s = new Pruefstand(klickStartwert: saat);
        s.Maschine.Echounterdrueckung = true;
        s.Maschine.Kette.EigenenKlickAusblenden = ausblenden;
        s.Maschine.Klick = new FunkKlickEinstellungen { EigeneAn = eigenerKlick, EigeneDb = eigeneDb };
        s.Maschine.Sprachaktivierung = false;
        await s.StartenAsync();
        if (echo is not null)
        {
            var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
            s.Echo = (w, mic) =>
            {
                for (var i = 0; i < mic.Length; i++)
                {
                    linie.Enqueue(echo(0.5f * (w[2 * i] + w[2 * i + 1])));
                    mic[i] += linie.Dequeue();
                }
            };
        }
        var grund = AnbindungSignale.Rauschen(480 * 100, -65, (ulong)saat);
        s.Abspielen(grund);
        var reaktion = reaktionMs / 10;
        for (var k = 0; k < sprueche; k++)
        {
            s.Maschine.SprechtasteGedrueckt();
            s.Abspielen(grund.AsSpan(0, 480 * reaktion));
            s.Abspielen(AnbindungSignale.Sprache(48000, (ulong)(saat * 10 + k), -24));
            s.Maschine.SprechtasteLosgelassen();
            s.Abspielen(grund.AsSpan(0, 480 * 100));
        }
        // Gesendete Phasen dekodieren; der Anlaut liegt reaktion + 40 ms nach dem Beginn (APM, RNNoise, Opus).
        var phasen = new List<List<byte[]>>();
        foreach (var p in s.Pakete)
        {
            if ((p.Kennzeichen & Sprachkennzeichen.Beginn) != 0) phasen.Add([]);
            if (phasen.Count > 0 && p.Opus.Length > 0) phasen[^1].Add(p.Opus);
        }
        var anfaenge = new double[phasen.Count];
        var klickrest = new double[phasen.Count];
        for (var i = 0; i < phasen.Count; i++)
        {
            using var dek = new OpusDekodierer();
            var pcm = new List<float>();
            var rahmen = new float[OpusDekodierer.MaxSamples];
            foreach (var paket in phasen[i]) pcm.AddRange(rahmen.AsSpan(0, dek.Dekodieren(paket, rahmen)).ToArray());
            var x = pcm.ToArray();
            var ab = (reaktionMs + 40) * 48;
            anfaenge[i] = Pegel.Dbfs(x.AsSpan(ab, 4800));
            klickrest[i] = reaktionMs >= 100 ? Pegel.Dbfs(x.AsSpan(0, (reaktionMs - 10) * 48)) : double.NaN;
        }
        return (anfaenge, klickrest, s.Maschine.Kette.ReferenzAusgeblendet);
    }

    /// <summary>
    /// Wie <see cref="Ablauf"/>, aber mit dem Zeitverhalten echter Geräte: Was die Wiedergabe zieht, spielt erst
    /// <paramref name="verzugTakte"/> Takte später (Wiedergabepuffer von 30 ms, Mischtakt von Windows, Loopback-Paket) und steht erst
    /// dann im Loopback und im Echo; dazu liegen im Ring der Echoreferenz <paramref name="rueckstand"/> Rahmen mehr als nötig (zwei
    /// Geräteuhren laufen auseinander; erst über <see cref="Klang.Maschine.Sendekette.ReferenzHoechstRahmen"/> kürzt die Kette).
    /// Rückgabe: Pegel der ersten 100 ms jedes Spruchs wie in <see cref="Ablauf"/>.
    /// </summary>
    internal static async Task<double[]> AblaufMitVerzug(int saat, bool eigenerKlick, Func<float, float>? echo, int reaktionMs, int verzugTakte,
        int rueckstand, int sprueche = 5, bool ausblenden = true)
    {
        await using var s = new Pruefstand(klickStartwert: saat);
        s.Maschine.Echounterdrueckung = true;
        s.Maschine.Kette.EigenenKlickAusblenden = ausblenden;
        s.Maschine.Klick = new FunkKlickEinstellungen { EigeneAn = eigenerKlick };
        s.Maschine.Sprachaktivierung = false;
        await s.StartenAsync();
        var stereo = s.System.WiedergabeFormat.Kanaele * 480;
        for (var r = 0; r < rueckstand; r++) s.System.Loopback!.Liefern(new float[stereo]);
        var gespielt = new Queue<float[]>();
        for (var v = 0; v < verzugTakte; v++) gespielt.Enqueue(new float[stereo]);
        var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
        var wiedergabe = new float[stereo];
        var mikrofon = new float[480];
        void Takt(ReadOnlySpan<float> ein)
        {
            s.Uhr.Us += Pruefstand.TaktUs;
            s.System.Wiedergabe!.Ziehen(wiedergabe);
            gespielt.Enqueue((float[])wiedergabe.Clone());
            var jetzt = gespielt.Dequeue();
            s.System.Loopback!.Liefern(jetzt);
            ein.CopyTo(mikrofon);
            if (echo is not null)
            {
                for (var i = 0; i < 480; i++)
                {
                    linie.Enqueue(echo(0.5f * (jetzt[2 * i] + jetzt[2 * i + 1])));
                    mikrofon[i] += linie.Dequeue();
                }
            }
            s.System.Aufnahme!.Liefern(mikrofon);
            s.Maschine.Zustellen();
        }
        void Spielen(ReadOnlySpan<float> x)
        {
            for (var p = 0; p + 480 <= x.Length; p += 480) Takt(x.Slice(p, 480));
        }
        var grund = AnbindungSignale.Rauschen(480 * 100, -65, (ulong)saat);
        Spielen(grund);
        var reaktion = reaktionMs / 10;
        for (var k = 0; k < sprueche; k++)
        {
            s.Maschine.SprechtasteGedrueckt();
            Spielen(grund.AsSpan(0, 480 * reaktion));
            Spielen(AnbindungSignale.Sprache(48000, (ulong)(saat * 10 + k), -24));
            s.Maschine.SprechtasteLosgelassen();
            Spielen(grund.AsSpan(0, 480 * 100));
        }
        var anfaenge = new List<double>();
        List<byte[]>? phase = null;
        foreach (var p in s.Pakete.Cast<Gesendet?>().Append(null))
        {
            if (p is null || (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0)
            {
                if (phase is not null)
                {
                    using var dek = new OpusDekodierer();
                    var pcm = new List<float>();
                    var rahmen = new float[OpusDekodierer.MaxSamples];
                    foreach (var paket in phase) pcm.AddRange(rahmen.AsSpan(0, dek.Dekodieren(paket, rahmen)).ToArray());
                    anfaenge.Add(Pegel.Dbfs(pcm.ToArray().AsSpan((reaktionMs + 40) * 48, 4800)));
                }
                phase = [];
            }
            if (p is not null && p.Opus.Length > 0) phase?.Add(p.Opus);
        }
        return [.. anfaenge];
    }

    [Test]
    public static async Task EigenerDruckKlick_VerzugUndRueckstand_DaempftDenAnlautNicht()
    {
        // Gegenprüfung Klangstufe A: Das Fenster hing an der Uhr der Aufnahme (120 ms ab dem Druck). Kommt der Klick erst 50 ms nach dem
        // Mischen in den Loopback und liegen 7 Rahmen Rückstand im Ring (zwei Geräteuhren, ohne Kürzen erlaubt), liest die Kette ihn
        // erst nach dem Fenster als Referenz, und AEC3 dämpfte den Anlaut wie bis 2c. Das Fenster muss an den Rahmen des Loopbacks hängen.
        foreach (var (name, echo, grenze) in new (string, Func<float, float>?, double)[] { ("kein Echo", null, 1.5), ("12 dB", KlangMaschineTests.Kopplung12Db, 3) })
        {
            foreach (var saat in new[] { 5, 10 })
            {
                var ohne = await AblaufMitVerzug(saat, eigenerKlick: false, echo, 100, verzugTakte: 5, rueckstand: 7);
                var mit = await AblaufMitVerzug(saat, eigenerKlick: true, echo, 100, verzugTakte: 5, rueckstand: 7);
                Pruefe.Gleich(ohne.Length, mit.Length, $"{name}, Saat {saat}: Phasen");
                for (var i = 0; i < mit.Length; i++)
                    PruefeWert.Mindestens(ohne[i] - grenze, mit[i], $"{name}, Saat {saat}, Spruch {i}: Anlaut 100 ms nach dem Druck, 50 ms Verzug, 7 Rahmen Rückstand (ohne Klick {ohne[i]:0.0} dBFS)");
            }
        }
    }

    [Test]
    public static async Task EigenerDruckKlick_Lautsprecher_DaempftDenAnlautNicht()
    {
        // Gemessen (MessungKlangA.Messung_KlickImLautsprecher, Sprechen 50 ms nach dem Druck, 10 Sprüche): Klick in der Referenz
        // im Mittel -17 bis -18 dB, schlechtester bis -52 dB; aus der Referenz ohne Echo 0,0 / -0,9, mit 12 dB +0,5 / -2,0 dB.
        // Über 12 dB kommt der Klick selbst ins Mikrofon und färbt den Anlaut etwas; die Grenze ist deshalb 3 dB statt 1,5.
        foreach (var (name, echo, grenze) in new (string, Func<float, float>?, double)[] { ("kein Echo", null, 1.5), ("12 dB", KlangMaschineTests.Kopplung12Db, 3) })
        {
            var alt = new List<double>();
            var bezug = new List<double>();
            foreach (var saat in new[] { 5, 10 })
            {
                var ohne = await Ablauf(saat, eigenerKlick: false, ausblenden: true, echo, 50);
                var mit = await Ablauf(saat, eigenerKlick: true, ausblenden: true, echo, 50);
                var inReferenz = await Ablauf(saat, eigenerKlick: true, ausblenden: false, echo, 50);
                Pruefe.Gleich(ohne.Anfaenge.Length, mit.Anfaenge.Length, $"{name}, Saat {saat}: Phasen");
                for (var i = 0; i < mit.Anfaenge.Length; i++)
                    PruefeWert.Mindestens(ohne.Anfaenge[i] - grenze, mit.Anfaenge[i], $"{name}, Saat {saat}, Spruch {i}: Anlaut 50 ms nach dem Druck (ohne Klick {ohne.Anfaenge[i]:0.0} dBFS)");
                PruefeWert.Mindestens(mit.Anfaenge.Length * 10, mit.Ausgeblendet, $"{name}, Saat {saat}: Referenz je Druck ausgeblendet");
                Pruefe.Gleich(0L, ohne.Ausgeblendet, "ohne eigenen Klick nichts ausgeblendet");
                Pruefe.Gleich(0L, inReferenz.Ausgeblendet, "Vergleich: nichts ausgeblendet");
                alt.AddRange(inReferenz.Anfaenge);
                bezug.AddRange(ohne.Anfaenge);
            }
            PruefeWert.Hoechstens(bezug.Average() - 6, alt.Average(), $"{name}: mit dem Klick in der Referenz (bis 2c) gedämpft (die Szene misst etwas)");
        }
    }
}
