namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Weiches Clipping mit derselben Kennlinie wie <c>opus_pcm_soft_clip</c> aus libopus 1.6.1 (src/opus.c, Funktion
/// <c>opus_pcm_soft_clip_impl</c>; Copyright (c) 2011 Xiph.Org Foundation, Skype Limited, Copyright (c) 2024 Arm Limited;
/// BSD-Lizenz aus dem Dateikopf, Text in THIRD-PARTY-NOTICES.txt Nr. 12), in C# nachgebaut: Zwischen den Nulldurchgängen
/// um eine Spitze über 1 wird <c>x + a*x*x</c> angewendet, mit <c>a</c> so, dass die Spitze genau 1 wird.
/// Das ist stetig und ohne Knick; Werte in [-1, 1] bleiben unberührt, solange kein Abschnitt über 1 geht.
/// Eingaben werden vorher wie im Original auf [-2, 2] begrenzt (dort ist die Steigung der Kennlinie null)
/// und NaN wird zu 0.
/// <para>
/// Zusatz gegenüber dem Original: Ein abschließender Schutz hält das Ergebnis auf [-1, 1]. Er greift nur
/// bei Rundungsfehlern in der Größenordnung eines ULP, ist also kein hartes Clipping des Signals.
/// </para>
/// Je Kanal ein Gedächtniswert, der die Kennlinie über Blockgrenzen fortsetzt. Allokiert nicht.
/// </summary>
public static class WeichesClipping
{
    public static void Anwenden(Span<float> x, ref float gedaechtnis)
    {
        var n = x.Length;
        if (n == 0) return;

        for (var j = 0; j < n; j++)
        {
            var v = x[j];
            x[j] = float.IsNaN(v) ? 0f : Math.Clamp(v, -2f, 2f);
        }

        var a = gedaechtnis;
        // Kennlinie des vorigen Blocks bis zum ersten Nulldurchgang fortsetzen.
        for (var j = 0; j < n; j++)
        {
            if (x[j] * a >= 0) break;
            x[j] += a * x[j] * x[j];
        }

        var aktuell = 0;
        var x0 = x[0];
        while (true)
        {
            int i;
            for (i = aktuell; i < n; i++)
            {
                if (x[i] > 1f || x[i] < -1f) break;
            }
            if (i == n)
            {
                a = 0;
                break;
            }
            var spitzenPos = i;
            var start = i;
            var ende = i;
            var maxWert = MathF.Abs(x[i]);
            // Nulldurchgang vor der Überschreitung
            while (start > 0 && x[i] * x[start - 1] >= 0) start--;
            // Nulldurchgang danach, dabei weitere Spitzen suchen
            while (ende < n && x[i] * x[ende] >= 0)
            {
                if (MathF.Abs(x[ende]) > maxWert)
                {
                    maxWert = MathF.Abs(x[ende]);
                    spitzenPos = ende;
                }
                ende++;
            }
            // Sonderfall: Überschreitung vor dem ersten Nulldurchgang des Blocks
            var sonderfall = start == 0 && x[i] * x[0] >= 0;

            a = (maxWert - 1) / (maxWert * maxWert);
            a += a * 2.4e-7f;
            if (x[i] > 0) a = -a;
            for (var j = start; j < ende; j++) x[j] += a * x[j] * x[j];

            if (sonderfall && spitzenPos >= 2)
            {
                // Lineare Rampe vom ersten Sample zur Spitze gegen einen Sprung am Blockanfang.
                var versatz = x0 - x[0];
                var schritt = versatz / spitzenPos;
                for (var j = aktuell; j < spitzenPos; j++)
                {
                    versatz -= schritt;
                    x[j] = Math.Clamp(x[j] + versatz, -1f, 1f);
                }
            }
            aktuell = ende;
            if (aktuell == n) break;
        }
        gedaechtnis = a;

        for (var j = 0; j < n; j++) x[j] = Math.Clamp(x[j], -1f, 1f);
    }
}
