// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Sprache;

/// <summary>
/// Senderichtung einer Sitzung (5.7, 5.8): ein einziger, streng steigender Zähler ab 0 über alle Epochen und
/// über UDP und Tunnel, der Schlüssel der aktuellen Epoche und die Paketgrenzen je Epoche.
/// Client: Richtung hoch, gehört dem Sende-Thread. Server: Richtung runter, je Empfänger, gehört dem Relay-Thread.
/// Nicht threadsicher.
/// </summary>
public sealed class SendeKanal : IDisposable
{
    private readonly Rotationsgrenzen _grenzen;
    private RichtungsSchluessel _schluessel;
    private ulong _zaehler;
    private long _gesperrtSeit;
    private bool _gesperrt;

    /// <param name="sitzungsId">Kopf-Sitzungs-ID: hoch die des Senders, runter die des Empfängers.</param>
    /// <param name="richtung">Senderichtung; nur Typen dieser Richtung werden versiegelt.</param>
    /// <param name="schluessel">Schlüssel der Startepoche; der Kanal übernimmt ihn und gibt ihn frei.</param>
    /// <param name="jetzt">Stopwatch-Zeitstempel des Epochenbeginns.</param>
    /// <param name="grenzen">Grenzen aus 5.8; ohne Angabe die Spezifikation.</param>
    public SendeKanal(uint sitzungsId, Richtung richtung, RichtungsSchluessel schluessel, long jetzt, Rotationsgrenzen? grenzen = null)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        SitzungsId = sitzungsId;
        Richtung = richtung;
        _schluessel = schluessel;
        _grenzen = grenzen ?? Rotationsgrenzen.Standard;
        EpochenBeginn = jetzt;
    }

    public uint SitzungsId { get; }
    public Richtung Richtung { get; }
    public byte Epoche => _schluessel.Epoche;

    /// <summary>Zähler, den das nächste Paket bekommt.</summary>
    public ulong NaechsterZaehler => _zaehler;

    public long GesendetInEpoche { get; private set; }
    public long EpochenBeginn { get; private set; }

    /// <summary>Harte Grenze erreicht: In dieser Epoche wird nichts mehr gesendet.</summary>
    public bool Gesperrt => _gesperrt;

    /// <summary>Client: KeyRotation{ZAEHLER} anfordern (ab 2^21 Paketen hoch in der Epoche).</summary>
    public bool RotationAnfordern => GesendetInEpoche >= _grenzen.PaketeBisAnforderung;

    /// <summary>Client: Die Sperre hält länger als 5 s an, also neue Sitzung.</summary>
    public bool NeueSitzungNoetig(long jetzt) => _gesperrt && jetzt - _gesperrtSeit > Uhr.Ticks(_grenzen.SperreBisNeueSitzung);

    /// <summary>
    /// Versiegelt ein Paket mit dem nächsten Zähler und der aktuellen Epoche. false, wenn die harte Grenze der
    /// Epoche erreicht ist; dann wird das Paket verworfen und kein Zähler verbraucht.
    /// Ein Typ der falschen Richtung oder eine unzulässige Klartextlänge wirft.
    /// </summary>
    public bool Versiegeln(PaketTyp typ, ReadOnlySpan<byte> klartext, Span<byte> ziel, long jetzt, out int laenge)
    {
        if (!PaketTypen.IstBekannt(typ) || PaketTypen.RichtungVon(typ) != Richtung)
            throw new ArgumentException($"Typ {typ} gehört nicht in Richtung {Richtung}.", nameof(typ));
        if (!PaketTypen.KlartextLaengePasst(typ, klartext.Length))
            throw new ArgumentException($"Klartextlänge {klartext.Length} passt nicht zu {typ}.", nameof(klartext));

        laenge = 0;
        if (_gesperrt) return false;
        if (_zaehler == ulong.MaxValue)
        {
            Sperren(jetzt);
            return false;
        }

        laenge = Datagramm.Versiegeln(_schluessel, new SprachKopf(typ, Epoche, SitzungsId, _zaehler), klartext, ziel);
        _zaehler++;
        GesendetInEpoche++;
        if (GesendetInEpoche >= _grenzen.HarteGrenze) Sperren(jetzt);
        return true;
    }

    /// <summary>
    /// Sendet ab jetzt in der nächsten Epoche (Client: bei VoiceKeys ROTATION; Server: mit der Antwort).
    /// Der Zähler läuft weiter; der alte Schlüssel wird hier nicht mehr gebraucht und sofort freigegeben.
    /// </summary>
    public void Umschalten(RichtungsSchluessel neu, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(neu);
        if (neu.Epoche != unchecked((byte)(Epoche + 1)))
            throw new ArgumentException($"Nach Epoche {Epoche} kommt {(byte)(Epoche + 1)}, nicht {neu.Epoche}.", nameof(neu));
        _schluessel.Dispose();
        _schluessel = neu;
        GesendetInEpoche = 0;
        EpochenBeginn = jetzt;
        _gesperrt = false;
    }

    public void Dispose() => _schluessel.Dispose();

    private void Sperren(long jetzt)
    {
        if (_gesperrt) return;
        _gesperrt = true;
        _gesperrtSeit = jetzt;
    }
}
