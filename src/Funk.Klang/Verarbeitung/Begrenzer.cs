// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>Kennlinie und Zeitverhalten des Begrenzers.</summary>
public sealed class BegrenzerEinstellungen
{
    /// <summary>Mitte des Knies in dBFS (Spitzenpegel). Darunter, abzüglich halber Kniebreite, bleibt das Signal unberührt.</summary>
    public float SchwelleDbfs { get; init; } = -1f;

    public float KniebreiteDb { get; init; } = 2f;

    /// <summary>Kompressionsverhältnis oberhalb des Knies.</summary>
    public float Verhaeltnis { get; init; } = 5f;

    /// <summary>Sättigung: Die Hüllkurve wird nie über diesen Pegel geführt.</summary>
    public float DeckeDbfs { get; init; } = -0.1f;

    /// <summary>Abklingen des Hüllkurvenfilters je 0,5-ms-Unterblock (WebRTC AGC2: 0,9971259).</summary>
    public float Abklingfaktor { get; init; } = 0.9971259f;
}

/// <summary>
/// Weicher Begrenzer nach dem Muster des WebRTC-AGC2-Limiters: je 10-ms-Block 20 Unterblöcke zu 0,5 ms,
/// Hüllkurve als Spitzenwert je Unterblock, eine Stufe vorgezogen, sofortiger Anstieg und geglättetes
/// Abklingen, Verstärkung aus einer Kennlinie mit quadratischem Knie, Kompression und Sättigung, je Sample
/// linear zwischen den Unterblockgrenzen interpoliert. Setzt die Absenkung im ersten Unterblock ein, wird
/// dort mit Exponent 8 interpoliert, damit sie schneller greift. Eigene Umsetzung; die Zahlen folgen
/// codec-ausgabe.md B22. Stereo wird gekoppelt begrenzt. Allokiert nach dem Konstruktor nichts.
/// <para>
/// Nicht endliche Samples (NaN, ±Unendlich) werden zu 0, bevor sie in die Hüllkurve gehen: Ein einziges
/// Unendlich würde den Hüllkurvenpegel sonst für immer auf Unendlich setzen und das Gerät stummschalten.
/// Der Hüllkurvenpegel wird unterhalb von <see cref="MinPegel"/> auf 0 gesetzt, damit er nach langer Stille
/// nicht in Denormalen hängen bleibt.
/// </para>
/// </summary>
public sealed class Begrenzer
{
    public const int Blocklaenge = Klangformat.Samples10ms;
    public const int Unterbloecke = 20;
    private const int Unterblocklaenge = Blocklaenge / Unterbloecke;
    private const float AnstiegsExponent = 8f;
    private const float MinPegel = 1e-10f; // -200 dBFS; die Kennlinie rechnet ohnehin erst ab 1e-5

    private readonly BegrenzerEinstellungen _e;
    private readonly float[] _huelle = new float[Unterbloecke];
    private readonly float[] _faktoren = new float[Unterbloecke + 1];
    private readonly float _kneeStart;
    private readonly float _kneeEnde;
    private float _letzterFaktor = 1f;
    private float _pegel;

    public Begrenzer(BegrenzerEinstellungen? einstellungen = null)
    {
        _e = einstellungen ?? new BegrenzerEinstellungen();
        // Negiert formuliert, damit NaN überall durchfällt.
        if (!float.IsFinite(_e.KniebreiteDb) || !float.IsFinite(_e.Verhaeltnis) || !float.IsFinite(_e.SchwelleDbfs)
            || !float.IsFinite(_e.DeckeDbfs) || !(_e.KniebreiteDb > 0) || !(_e.Verhaeltnis > 1)
            || !(_e.DeckeDbfs <= 0) || !(_e.DeckeDbfs >= _e.SchwelleDbfs))
            throw new ArgumentException("Kniebreite > 0, Verhältnis > 1 und Schwelle <= Decke <= 0 dBFS nötig, alles endlich.", nameof(einstellungen));
        if (!(_e.Abklingfaktor >= 0 && _e.Abklingfaktor < 1))
            throw new ArgumentException("Abklingfaktor muss in [0, 1) liegen.", nameof(einstellungen));
        _kneeStart = _e.SchwelleDbfs - _e.KniebreiteDb / 2f;
        _kneeEnde = _e.SchwelleDbfs + _e.KniebreiteDb / 2f;
    }

    /// <summary>Zuletzt angewandter Faktor (1 = unberührt).</summary>
    public float LetzterFaktor => _letzterFaktor;

    /// <summary>Kleinster Faktor im zuletzt verarbeiteten Block (1 = unberührt); für die Telemetrie der Sendekette.</summary>
    public float KleinsterFaktor { get; private set; } = 1f;

    /// <summary>Verstärkung der Kennlinie in dB für einen Spitzenpegel in dBFS (0 oder negativ).</summary>
    public float KennlinieDb(float eingangDbfs)
    {
        if (eingangDbfs <= _kneeStart) return 0f;
        float ausgang;
        if (eingangDbfs < _kneeEnde)
        {
            var d = eingangDbfs - _kneeStart;
            ausgang = eingangDbfs + (1f / _e.Verhaeltnis - 1f) * d * d / (2f * _e.KniebreiteDb);
        }
        else
        {
            ausgang = _e.SchwelleDbfs + (eingangDbfs - _e.SchwelleDbfs) / _e.Verhaeltnis;
        }
        ausgang = MathF.Min(ausgang, _e.DeckeDbfs);
        return ausgang - eingangDbfs;
    }

    /// <summary>Einen 10-ms-Block begrenzen. <paramref name="rechts"/> leer für Mono, sonst gleich lang.</summary>
    public void Verarbeiten(Span<float> links, Span<float> rechts)
    {
        if (links.Length != Blocklaenge || (!rechts.IsEmpty && rechts.Length != Blocklaenge))
            throw new ArgumentException("Block muss 480 Samples haben.", nameof(links));

        for (var u = 0; u < Unterbloecke; u++)
        {
            var spitze = SpitzeUndBereinigen(links.Slice(u * Unterblocklaenge, Unterblocklaenge));
            if (!rechts.IsEmpty) spitze = MathF.Max(spitze, SpitzeUndBereinigen(rechts.Slice(u * Unterblocklaenge, Unterblocklaenge)));
            _huelle[u] = spitze;
        }
        // Anstieg eine Stufe vorziehen, damit die Interpolation einen plötzlichen Anstieg nicht verpasst.
        for (var u = 0; u < Unterbloecke - 1; u++)
        {
            if (_huelle[u] < _huelle[u + 1]) _huelle[u] = _huelle[u + 1];
        }
        _faktoren[0] = _letzterFaktor;
        var kleinster = _letzterFaktor;
        for (var u = 0; u < Unterbloecke; u++)
        {
            var h = _huelle[u];
            _pegel = h > _pegel ? h : h * (1f - _e.Abklingfaktor) + _pegel * _e.Abklingfaktor;
            if (_pegel < MinPegel) _pegel = 0f;
            _faktoren[u + 1] = Pegel.DbZuLinear(KennlinieDb(Pegel.LinearZuDb(_pegel)));
            kleinster = MathF.Min(kleinster, _faktoren[u + 1]);
        }
        KleinsterFaktor = kleinster;

        var anstieg = _faktoren[0] > _faktoren[1];
        for (var u = 0; u < Unterbloecke; u++)
        {
            var f0 = _faktoren[u];
            var f1 = _faktoren[u + 1];
            var basis = u * Unterblocklaenge;
            for (var j = 0; j < Unterblocklaenge; j++)
            {
                float f;
                if (u == 0 && anstieg)
                    f = MathF.Pow(1f - (float)j / Unterblocklaenge, AnstiegsExponent) * (f0 - f1) + f1;
                else
                    f = f0 + (f1 - f0) * j / Unterblocklaenge;
                links[basis + j] *= f;
                if (!rechts.IsEmpty) rechts[basis + j] *= f;
            }
        }
        _letzterFaktor = _faktoren[Unterbloecke];
    }

    public void Zuruecksetzen()
    {
        _letzterFaktor = 1f;
        _pegel = 0f;
    }

    /// <summary>Hüllkurvenpegel (linear), für Tests.</summary>
    internal float Huellpegel => _pegel;

    /// <summary>Spitzenwert des Abschnitts; nicht endliche Samples werden dabei auf 0 gesetzt.</summary>
    private static float SpitzeUndBereinigen(Span<float> x)
    {
        var s = 0f;
        for (var i = 0; i < x.Length; i++)
        {
            var v = x[i];
            if (!float.IsFinite(v))
            {
                x[i] = 0f;
                continue;
            }
            var b = MathF.Abs(v);
            if (b > s) s = b;
        }
        return s;
    }
}
