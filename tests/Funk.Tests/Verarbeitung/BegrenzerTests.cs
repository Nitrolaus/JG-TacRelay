// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

internal static class BegrenzerTests
{
    [Test]
    public static void Kennlinie_UnterDemKnieUnberuehrt_StetigUndMonoton()
    {
        var b = new Begrenzer();
        Pruefe.Gleich(0f, b.KennlinieDb(-20f), "leise unberührt");
        Pruefe.Gleich(0f, b.KennlinieDb(-2f), "Kniebeginn unberührt");
        var vorher = -30f + b.KennlinieDb(-30f);
        for (var x = -30f; x <= 12f; x += 0.01f)
        {
            var aus = x + b.KennlinieDb(x);
            PruefeWert.Hoechstens(-0.1 + 1e-4, aus, "nie über der Decke");
            PruefeWert.Mindestens(vorher - 1e-4, aus, "monoton");
            PruefeWert.Hoechstens(0.05, Math.Abs(aus - vorher), "stetig");
            vorher = aus;
        }
        // Oberhalb des Knies 5:1 um die Schwelle -1 dBFS: +2 dBFS wird zu -1 + 3/5 = -0,4 dBFS.
        PruefeWert.Nahe(-0.4, 2f + b.KennlinieDb(2f), 1e-4, "Kompression 5:1 oberhalb des Knies");
        // Sättigung: +10 dBFS wird auf die Decke -0,1 dBFS geführt.
        PruefeWert.Nahe(-0.1, 10f + b.KennlinieDb(10f), 1e-4, "Sättigung an der Decke");
    }

    [Test]
    public static void LeisesSignal_BitgenauUnveraendert()
    {
        var b = new Begrenzer();
        var links = new float[480];
        var rechts = new float[480];
        for (var i = 0; i < 480; i++)
        {
            links[i] = 0.7f * MathF.Sin(i * 0.05f);
            rechts[i] = -0.5f * MathF.Sin(i * 0.03f);
        }
        var l0 = (float[])links.Clone();
        var r0 = (float[])rechts.Clone();
        for (var k = 0; k < 5; k++)
        {
            b.Verarbeiten(links, rechts);
            Pruefe.Wahr(l0.AsSpan().SequenceEqual(links), "links unverändert");
            Pruefe.Wahr(r0.AsSpan().SequenceEqual(rechts), "rechts unverändert");
        }
    }

    [Test]
    public static void LautesSignal_WirdAufDeckeGefuehrt_SofortigerAnstieg()
    {
        var b = new Begrenzer();
        var x = new float[480];
        var spitzen = new List<float>();
        for (var blockNr = 0; blockNr < 20; blockNr++)
        {
            for (var i = 0; i < 480; i++) x[i] = 3f * MathF.Sin(2f * MathF.PI * 1000f * (blockNr * 480 + i) / 48000f);
            b.Verarbeiten(x, Span<float>.Empty);
            spitzen.Add(Pegel.Spitze(x));
        }
        // Ab dem zweiten Block liegt die Spitze an der Decke (-0,1 dBFS); im ersten fängt das weiche Clipping den Rest.
        foreach (var s in spitzen.Skip(1)) PruefeWert.Nahe(Pegel.DbZuLinear(-0.1f), s, 0.01, "Spitze an der Decke");
        PruefeWert.Hoechstens(3.0, spitzen[0], "erster Block");
    }

    [Test]
    public static void Abklingen_NachLautemBlock_Weich()
    {
        var b = new Begrenzer();
        var laut = new float[480];
        Array.Fill(laut, 2f);
        b.Verarbeiten(laut, Span<float>.Empty);
        var leise = new float[480];
        var faktoren = new List<float>();
        for (var k = 0; k < 100; k++)
        {
            Array.Fill(leise, 0.1f);
            b.Verarbeiten(leise, Span<float>.Empty);
            faktoren.Add(b.LetzterFaktor);
        }
        PruefeWert.Hoechstens(0.99, faktoren[0], "direkt danach noch abgesenkt");
        for (var i = 1; i < faktoren.Count; i++) PruefeWert.Mindestens(faktoren[i - 1], faktoren[i], "Faktor steigt monoton");
        Pruefe.Gleich(1f, faktoren[^1], "nach 1 s wieder frei");
    }

    [Test]
    public static void WeichesClipping_HandgerechnetesBeispiel()
    {
        var x = new[] { 0.5f, 1.5f, 0.5f, -0.2f };
        var mem = 0f;
        WeichesClipping.Anwenden(x, ref mem);
        var a = 0.5f / 2.25f;
        a += a * 2.4e-7f;
        PruefeWert.Nahe(0.5 - a * 0.25, x[0], 1e-6, "x0");
        PruefeWert.Nahe(1.0, x[1], 1e-6, "Spitze genau 1");
        PruefeWert.Hoechstens(1.0, x[1], "Spitze nie über 1");
        PruefeWert.Nahe(0.5 - a * 0.25, x[2], 1e-6, "x2");
        Pruefe.Gleich(-0.2f, x[3], "hinter dem Nulldurchgang unberührt");
        Pruefe.Gleich(0f, mem, "Gedächtnis leer");
    }

    [Test]
    public static void WeichesClipping_SetztKennlinieUeberBlockgrenzeFort()
    {
        var x1 = new[] { 0.5f, 1.5f };
        var mem = 0f;
        WeichesClipping.Anwenden(x1, ref mem);
        var a = 0.5f / 2.25f;
        a += a * 2.4e-7f;
        PruefeWert.Nahe(-a, mem, 1e-7, "Gedächtnis trägt a");
        var x2 = new[] { 0.9f, 0.5f, -0.1f };
        WeichesClipping.Anwenden(x2, ref mem);
        PruefeWert.Nahe(0.9 - a * 0.81, x2[0], 1e-6, "gleiche Kennlinie im Folgeblock");
        PruefeWert.Nahe(0.5 - a * 0.25, x2[1], 1e-6, "bis zum Nulldurchgang");
        Pruefe.Gleich(-0.1f, x2[2], "danach unberührt");
    }

    [Test]
    public static void WeichesClipping_ImBereichUnveraendert_SonstInnerhalbEins()
    {
        var zufall = new Random(5);
        var x = new float[480];
        for (var i = 0; i < x.Length; i++) x[i] = (float)(zufall.NextDouble() * 2 - 1) * 0.999f;
        var kopie = (float[])x.Clone();
        var mem = 0f;
        WeichesClipping.Anwenden(x, ref mem);
        Pruefe.Wahr(kopie.AsSpan().SequenceEqual(x), "in [-1, 1] bitgenau unverändert");

        for (var runde = 0; runde < 200; runde++)
        {
            for (var i = 0; i < x.Length; i++) x[i] = (float)(zufall.NextDouble() * 6 - 3);
            if (runde == 0) x[7] = float.NaN;
            WeichesClipping.Anwenden(x, ref mem);
            foreach (var v in x)
            {
                Pruefe.Wahr(float.IsFinite(v), "endlich");
                PruefeWert.Hoechstens(1.0, MathF.Abs(v), "innerhalb [-1, 1]");
            }
        }
    }

    [Test]
    public static void WeichesClipping_Sinus_OhneKnickUndSprung()
    {
        // Sinus mit Spitze 1,6 über mehrere Blöcke: keine Sprünge größer als die Steigung des Eingangs.
        var mem = 0f;
        var vorher = 0f;
        var maxSchritt = 0f;
        var block = new float[480];
        for (var b = 0; b < 20; b++)
        {
            for (var i = 0; i < 480; i++) block[i] = 1.6f * MathF.Sin(2f * MathF.PI * 300f * (b * 480 + i) / 48000f);
            WeichesClipping.Anwenden(block, ref mem);
            foreach (var v in block)
            {
                maxSchritt = MathF.Max(maxSchritt, MathF.Abs(v - vorher));
                vorher = v;
            }
        }
        var eingangsSteigung = 1.6f * 2f * MathF.PI * 300f / 48000f;
        PruefeWert.Hoechstens(eingangsSteigung * 1.001, maxSchritt, "größter Schritt");
    }
}
