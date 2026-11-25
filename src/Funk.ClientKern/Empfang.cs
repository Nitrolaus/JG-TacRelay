// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>
/// Prüfung der empfangenen Opus-Nutzlast (5.13): Der Client MUSS <c>opus_packet_get_nb_samples(..., 48000) == 960</c>
/// prüfen. Die Audio-Schicht setzt libopus ein; ohne sie gilt <see cref="TocOpusPruefer"/>.
/// </summary>
public interface IOpusPruefer
{
    /// <summary>Samples bei 48 kHz, die das Paket ergibt, oder -1, wenn es ungültig ist.</summary>
    int Samples(ReadOnlySpan<byte> opus);
}

/// <summary>Prüfung allein aus dem TOC-Byte (RFC 6716 Abschnitt 3.1), wie der Server; ohne libopus.</summary>
public sealed class TocOpusPruefer : IOpusPruefer
{
    public static TocOpusPruefer Instanz { get; } = new();

    public int Samples(ReadOnlySpan<byte> opus) => OpusPruefung.Pruefen(opus) == Opusbefund.Gueltig ? OpusPruefung.SamplesJeFrame : -1;
}

/// <summary>Ein empfangenes, geprüftes Sprachpaket samt Auswertung nach C6. Gilt nur während des Aufrufs.</summary>
public readonly ref struct SprachEmpfang
{
    internal SprachEmpfang(SpracheRunter paket, bool neueZeitlinie, bool fortsetzung, bool ende, long empfangenTakte, Verbindungsweg weg)
    {
        Paket = paket;
        NeueZeitlinie = neueZeitlinie;
        Fortsetzung = fortsetzung;
        Ende = ende;
        EmpfangenTakte = empfangenTakte;
        Weg = weg;
    }

    /// <summary>Stempel des Servers: Absender, Frame-Nr, Sprechphase, Kennzeichen, Art, Netze, Opus.</summary>
    public SpracheRunter Paket { get; }

    public uint Absender => Paket.Absender;

    public ushort Sprechphase => Paket.Sprechphase;

    /// <summary>C6: erstes Paket des Paares (Absender, Sprechphase), auch ohne BEGINN. Neue Abspiel-Zeitlinie.</summary>
    public bool NeueZeitlinie { get; }

    /// <summary>C6: Die neue Phase folgt binnen 100 ms auf ein ENDE desselben Absenders (C4): kein Ende- und kein Beginn-Klick.</summary>
    public bool Fortsetzung { get; }

    /// <summary>Erstes ENDE der Phase (weitere ENDE-Kopien werden nicht gemeldet).</summary>
    public bool Ende { get; }

    /// <summary>Beginn-Klick (A11): BEGINN gesetzt, neue Zeitlinie, keine Fortsetzung.</summary>
    public bool BeginnKlick => NeueZeitlinie && !Fortsetzung && (Paket.Kennzeichen & Kennzeichen.Beginn) != 0;

    /// <summary>Empfangszeit (Stopwatch-Takte) direkt nach dem Lesen vom Socket bzw. aus dem Tunnel.</summary>
    public long EmpfangenTakte { get; }

    public Verbindungsweg Weg { get; }
}

/// <summary>Empfänger der Sprache; wird auf dem Empfangs-Thread (UDP) bzw. der Leseschleife (Tunnel) aufgerufen, kurz halten.</summary>
public interface ISprachEmpfaenger
{
    void Empfangen(in SprachEmpfang empfang);

    /// <summary>Eine Zeitlinie endete ohne ENDE: 300 ms ohne Paket (C6, hier ohne Jitterpuffer ab dem letzten Paket gerechnet).</summary>
    void ZeitlinieBeendet(uint absender, ushort sprechphase);
}

/// <summary>
/// Zeitlinien je (Absender, Sprechphase) nach C6 und 7.3: Beginn mit dem ersten Paket, Ende mit dem ersten ENDE oder
/// nach 300 ms ohne Paket; spätere Pakete setzen ohne Klick fort; ein Sprung der Frame-Nr um mehr als 3000 zählt als neue
/// Phase; eine neue Phase binnen 100 ms nach einem ENDE desselben Absenders ist eine Fortsetzung. Nicht threadsicher.
/// </summary>
internal sealed class Zeitlinienbuch
{
    public const long LeerlaufMs = 300;
    public const long FortsetzungMs = 100;
    public const int SprungMax = 3000;

    private sealed class Linie
    {
        public uint LetzteFrame;
        public long LetzteMs;
        public bool Beendet;
        public bool Gemeldet;
    }

    private readonly Dictionary<(uint, ushort), Linie> _linien = [];
    private readonly Dictionary<uint, long> _letztesEnde = [];
    private readonly List<(uint, ushort)> _weg = [];

    public int Offen => _linien.Count;

    /// <summary>Wertet ein Paket aus. <c>false</c>: nicht melden (weitere ENDE-Kopien).</summary>
    public bool Paket(uint absender, ushort phase, uint frame, bool istEnde, long jetztMs, out bool neu, out bool fortsetzung, out bool ende)
    {
        neu = false;
        fortsetzung = false;
        ende = false;
        var schluessel = (absender, phase);
        if (!_linien.TryGetValue(schluessel, out var l) || (!l.Beendet && Sprung(l.LetzteFrame, frame)))
        {
            l = new Linie { LetzteFrame = frame, LetzteMs = jetztMs };
            _linien[schluessel] = l;
            neu = true;
            fortsetzung = _letztesEnde.TryGetValue(absender, out var endeMs) && jetztMs - endeMs <= FortsetzungMs;
        }
        if (istEnde)
        {
            if (l.Beendet) return false;
            l.Beendet = true;
            l.LetzteMs = jetztMs;
            _letztesEnde[absender] = jetztMs;
            ende = true;
            return true;
        }
        // Spätere Pakete nach dem Ende setzen ohne Klick fort (C6).
        l.Beendet = false;
        l.Gemeldet = false;
        if (unchecked((int)(frame - l.LetzteFrame)) > 0) l.LetzteFrame = frame;
        l.LetzteMs = jetztMs;
        return true;
    }

    /// <summary>Beendet Zeitlinien nach 300 ms ohne Paket; <paramref name="beendet"/> bekommt sie. Räumt nach 5 s auf.</summary>
    public void Takt(long jetztMs, Action<uint, ushort> beendet)
    {
        _weg.Clear();
        foreach (var (k, l) in _linien)
        {
            var ruhe = jetztMs - l.LetzteMs;
            if (!l.Beendet && !l.Gemeldet && ruhe >= LeerlaufMs)
            {
                l.Gemeldet = true;
                beendet(k.Item1, k.Item2);
            }
            if (ruhe > 5000) _weg.Add(k);
        }
        foreach (var k in _weg) _linien.Remove(k);
        if (_letztesEnde.Count > 256)
            foreach (var (a, t) in _letztesEnde.ToList())
                if (jetztMs - t > 5000) _letztesEnde.Remove(a);
    }

    private static bool Sprung(uint letzte, uint frame)
    {
        var d = unchecked((int)(frame - letzte));
        return d > SprungMax || d < -SprungMax;
    }
}
