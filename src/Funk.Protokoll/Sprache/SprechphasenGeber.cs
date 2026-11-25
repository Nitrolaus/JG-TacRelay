// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;

namespace Funk.Protokoll.Sprache;

/// <summary>Nummern und Kennzeichen eines zu sendenden Frames.</summary>
public readonly record struct Sendeframe(ushort Sprechphase, uint FrameNr, Kennzeichen Kennzeichen);

/// <summary>
/// Vergibt beim Sender Sprechphase, Frame-Nr und Kennzeichen nach 5.6 (P-18).
/// Frame-Nr steigt je codiertem Frame um genau 1; nur beim Beginn einer Phase wird sie an die Sitzungsuhr
/// angelegt: <c>max(letzte vergebene + 1, floor((t_aufnahme - t0) / 20 ms))</c>.
/// Nicht threadsicher; gehört dem Sende-Thread.
/// </summary>
public sealed class SprechphasenGeber
{
    /// <summary>Höchstens so viele Vorlauf-Frames je Phase (140 ms).</summary>
    public const int MaxPreroll = 7;

    /// <summary>So oft wird das ENDE-Paket gesendet, im 20-ms-Abstand.</summary>
    public const int EndeWiederholungen = 3;

    private enum Zustand : byte { Ruhe, Offen, Endet }

    private readonly long _t0;
    private readonly ushort _startwert;
    private Zustand _zustand;
    private bool _phaseVergeben;
    private ushort _phase;
    private bool _frameVergeben;
    private ulong _letzte;
    private bool _va;
    private bool _prerollErlaubt;
    private int _audioFrames;
    private int _prerollFrames;
    private int _enden;

    /// <param name="t0">Beginn der Sitzungsuhr als Stopwatch-Zeitstempel.</param>
    /// <param name="startwert">Zufälliger Startwert der Sprechphase je Sitzung.</param>
    public SprechphasenGeber(long t0, ushort startwert)
    {
        _t0 = t0;
        _startwert = startwert;
    }

    public ushort Sprechphase => _phase;

    /// <summary>Zuletzt vergebene Frame-Nr (mod 2^32).</summary>
    public uint LetzteFrameNr => (uint)_letzte;

    public bool PhaseOffen => _zustand == Zustand.Offen;

    /// <summary>
    /// Beginnt eine neue Sprechphase. <paramref name="tAufnahme"/> ist der Aufnahmezeitpunkt des ersten Frames
    /// (beim Pre-roll der des ersten Vorlauf-Frames). <paramref name="fortsetzung"/>: neue Phase nach einem
    /// Zielwechsel (C2, C4), dann gibt es keinen Pre-roll.
    /// </summary>
    public void Beginnen(long tAufnahme, bool sprachaktivierung, bool fortsetzung = false)
    {
        if (_zustand == Zustand.Offen)
            throw new InvalidOperationException("Die laufende Sprechphase muss erst mit Ende abgeschlossen werden.");

        _phase = _phaseVergeben ? unchecked((ushort)(_phase + 1)) : _startwert;
        _phaseVergeben = true;

        var anker = Anker(tAufnahme);
        var erste = _frameVergeben ? Math.Max(_letzte + 1, anker) : anker;
        _letzte = unchecked(erste - 1);
        _frameVergeben = true;

        _va = sprachaktivierung;
        _prerollErlaubt = sprachaktivierung && !fortsetzung;
        _audioFrames = 0;
        _prerollFrames = 0;
        _enden = 0;
        _zustand = Zustand.Offen;
    }

    /// <summary>Nächster Audio-Frame. Das erste Paket der Phase trägt BEGINN; Pre-roll nur vorn, nur bei VA, höchstens 7.</summary>
    public Sendeframe Audio(bool preroll = false)
    {
        if (_zustand != Zustand.Offen) throw new InvalidOperationException("Keine offene Sprechphase.");
        if (preroll)
        {
            if (!_prerollErlaubt || _audioFrames != _prerollFrames || _prerollFrames >= MaxPreroll)
                throw new InvalidOperationException("Pre-roll nur am Anfang einer VA-Phase ohne Fortsetzung, höchstens 7 Frames.");
            _prerollFrames++;
        }

        var kennzeichen = _va ? Kennzeichen.Va : Kennzeichen.None;
        if (_audioFrames == 0) kennzeichen |= Kennzeichen.Beginn;
        if (preroll) kennzeichen |= Kennzeichen.Preroll;
        _audioFrames++;
        return new Sendeframe(_phase, (uint)++_letzte, kennzeichen);
    }

    /// <summary>
    /// ENDE-Frame der Phase, ohne Opus, mit fortlaufender Frame-Nr. Dreimal abrufen (im 20-ms-Abstand senden),
    /// bevor die nächste Phase beginnt. Der erste Aufruf schließt die Phase.
    /// </summary>
    public Sendeframe Ende()
    {
        if (_zustand == Zustand.Ruhe || _enden >= EndeWiederholungen)
            throw new InvalidOperationException("Keine Sprechphase, die ein ENDE braucht.");
        _zustand = Zustand.Endet;
        _enden++;
        var kennzeichen = Kennzeichen.Ende | (_va ? Kennzeichen.Va : Kennzeichen.None);
        return new Sendeframe(_phase, (uint)++_letzte, kennzeichen);
    }

    /// <summary>floor((t - t0) / 20 ms), vor t0 gilt 0.</summary>
    private ulong Anker(long tAufnahme)
    {
        var dauer = tAufnahme - _t0;
        return dauer <= 0 ? 0 : (ulong)((Int128)dauer * 50 / Stopwatch.Frequency);
    }
}
