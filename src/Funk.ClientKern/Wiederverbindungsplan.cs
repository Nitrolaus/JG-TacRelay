// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>Phase der Wiederverbindung (7.1).</summary>
public enum Wiederverbindungsphase
{
    /// <summary>Noch keine Sitzung verloren, oder der letzte Versuch hatte Erfolg.</summary>
    Keine,

    /// <summary>2 min lang alle 2 s (+/-20 %), nach dem Verlust einer Sitzung, die mindestens 30 s AKTIV war.</summary>
    Schnell,

    /// <summary>4 / 8 / 15 / 15 ... s, jeweils +/-20 %.</summary>
    Backoff,

    /// <summary>Nach <c>Bye{WARTUNG, wiederInMs}</c>: <c>wiederInMs</c> plus 0-2 s, dann Schnellphase.</summary>
    Wartung,

    /// <summary>Nach <c>Bye{WARTEZEIT, wiederInMs}</c> aus dem Warteraum: <c>wiederInMs</c> +/-20 % (4.6).</summary>
    Wartezeit,
}

/// <summary>
/// Zeitplan der Wiederverbindung nach 7.1 als reines Modell (Millisekunden einer monotonen Uhr, Zufall von außen):
/// Schnellphase 2 min lang alle 2 s nach einer Sitzung, die mindestens 30 s AKTIV war; danach Backoff 4 / 8 / 15 / 15 s,
/// jeweils +/-20 %. Nach <c>Bye{WARTUNG}</c> erst <c>wiederInMs</c> plus 0-2 s, dann die Schnellphase. Nicht threadsicher.
/// </summary>
public sealed class Wiederverbindungsplan
{
    public const long SchnellphaseMs = 120_000;
    public const uint SchnellAbstandMs = 2_000;
    public const long MindestAktivMs = 30_000;
    public const uint WartungZufallMs = 2_000;
    private static readonly uint[] BackoffMs = [4_000, 8_000, 15_000];

    private readonly Func<double> _zufall;
    private long _schnellBis = long.MinValue;
    private int _stufe;

    /// <param name="zufall01">Gleichverteilt in [0, 1); ohne Angabe <see cref="Random.Shared"/>.</param>
    public Wiederverbindungsplan(Func<double>? zufall01 = null) => _zufall = zufall01 ?? Random.Shared.NextDouble;

    public Wiederverbindungsphase Phase { get; private set; }

    /// <summary>Zeitpunkt des nächsten Versuchs.</summary>
    public long NaechsterVersuchMs { get; private set; }

    /// <summary>
    /// Eine Verbindung ging verloren. <paramref name="aktivDauerMs"/>: wie lange die Sitzung AKTIV war (<c>null</c>: nie).
    /// <paramref name="wartungInMs"/> bzw. <paramref name="wartezeitInMs"/> aus <c>Bye</c>. Liefert den nächsten Versuch.
    /// </summary>
    public long Verloren(long jetztMs, long? aktivDauerMs, uint? wartungInMs = null, uint? wartezeitInMs = null)
    {
        if (wartungInMs is { } w)
        {
            Phase = Wiederverbindungsphase.Wartung;
            NaechsterVersuchMs = jetztMs + w + (long)(WartungZufallMs * Zufall());
            _schnellBis = NaechsterVersuchMs + SchnellphaseMs;
            _stufe = 0;
            return NaechsterVersuchMs;
        }
        if (wartezeitInMs is { } z)
        {
            Phase = Wiederverbindungsphase.Wartezeit;
            NaechsterVersuchMs = jetztMs + ClientAutomat.Streuen(z, Zufall());
            _stufe = 0;
            return NaechsterVersuchMs;
        }
        if (aktivDauerMs >= MindestAktivMs)
        {
            _schnellBis = jetztMs + SchnellphaseMs;
            _stufe = 0;
            Phase = Wiederverbindungsphase.Schnell;
            NaechsterVersuchMs = jetztMs + ClientAutomat.Streuen(SchnellAbstandMs, Zufall());
            return NaechsterVersuchMs;
        }
        return Fehlschlag(jetztMs);
    }

    /// <summary>Ein Versuch ist gescheitert (Aufbau oder Sitzung, die nicht AKTIV wurde). Liefert den nächsten Versuch.</summary>
    public long Fehlschlag(long jetztMs)
    {
        if (jetztMs < _schnellBis)
        {
            Phase = Wiederverbindungsphase.Schnell;
            NaechsterVersuchMs = jetztMs + ClientAutomat.Streuen(SchnellAbstandMs, Zufall());
            return NaechsterVersuchMs;
        }
        Phase = Wiederverbindungsphase.Backoff;
        var basis = BackoffMs[Math.Min(_stufe, BackoffMs.Length - 1)];
        if (_stufe < BackoffMs.Length) _stufe++;
        NaechsterVersuchMs = jetztMs + ClientAutomat.Streuen(basis, Zufall());
        return NaechsterVersuchMs;
    }

    /// <summary>Die Sitzung ist AKTIV: Backoff und Schnellphase zurücksetzen.</summary>
    public void Erfolg()
    {
        Phase = Wiederverbindungsphase.Keine;
        _stufe = 0;
        _schnellBis = long.MinValue;
    }

    private double Zufall() => Math.Clamp(_zufall(), 0, 0.999_999);
}
