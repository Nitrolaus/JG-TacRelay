// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using Funk.Protokoll.Schutz;

namespace Funk.Protokoll.Sprache;

/// <summary>
/// Empfangsrichtung einer Sitzung: angenommene Epochen (5.8) und das gemeinsame Replay-Fenster über alle
/// Epochen, UDP und Tunnel (5.9). Führt die Schritte 5 bis 8 der Empfangsprüfung aus.
/// Server: Richtung hoch, gehört dem Relay-Thread. Client: Richtung runter, gehört dem Empfangs-Thread.
/// Nicht threadsicher bis auf das Fenster (Schritt 8 atomar).
/// </summary>
public sealed class EmpfangsKanal : IDisposable
{
    private sealed class Epochenstand(RichtungsSchluessel schluessel)
    {
        public RichtungsSchluessel Schluessel { get; } = schluessel;
        public byte Epoche => Schluessel.Epoche;
        public long Empfangen { get; set; }
        public long Fehlschlaege { get; set; }
    }

    private readonly Rotationsgrenzen _grenzen;
    private readonly ReplayFenster _fenster = new(Sprachkanal.Fensterbreite);
    private Epochenstand _aktuell;
    private Epochenstand? _naechste;
    private Epochenstand? _vorige;
    private long _vorigeFrist;
    private bool _wartetAufErstesPaket;

    /// <param name="sitzungsId">Sitzungs-ID im Kopf der empfangenen Pakete.</param>
    /// <param name="richtung">Empfangsrichtung: hoch beim Server, runter beim Client.</param>
    /// <param name="schluessel">Schlüssel der Startepoche; der Kanal übernimmt ihn und gibt ihn frei.</param>
    /// <param name="grenzen">Grenzen aus 5.8; ohne Angabe die Spezifikation.</param>
    public EmpfangsKanal(uint sitzungsId, Richtung richtung, RichtungsSchluessel schluessel, Rotationsgrenzen? grenzen = null)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        SitzungsId = sitzungsId;
        Richtung = richtung;
        _aktuell = new Epochenstand(schluessel);
        _grenzen = grenzen ?? Rotationsgrenzen.Standard;
    }

    public uint SitzungsId { get; }
    public Richtung Richtung { get; }
    public byte Epoche => _aktuell.Epoche;
    public byte? NaechsteEpoche => _naechste?.Epoche;
    public byte? VorigeEpoche => _vorige?.Epoche;

    /// <summary>Angenommene Pakete in der aktuellen Epoche (Server: Auslöser 2^20).</summary>
    public long EmpfangenInEpoche => _aktuell.Empfangen;

    /// <summary>Gescheiterte Entschlüsselungen in der aktuellen Epoche.</summary>
    public long FehlschlaegeInEpoche => _aktuell.Fehlschlaege;

    /// <summary>Mehr als 2^16 gescheiterte Entschlüsselungen in der aktuellen Epoche (5.8).</summary>
    public bool AngriffsVerdacht => _aktuell.Fehlschlaege > _grenzen.FehlschlaegeBisVerdacht;

    /// <summary>Anzahl der Entschlüsselungsversuche insgesamt (Schritt 7), für Messwerte und Tests.</summary>
    public long Entschluesselungen { get; private set; }

    /// <summary>Höchster angenommener Zähler.</summary>
    public ulong Hoechster => _fenster.Hoechster;

    /// <summary>
    /// Ob das zuletzt geprüfte Paket angenommen wurde und einen neuen Höchstwert brachte (nur solche lösen 5.10 aus).
    /// Nach jedem Verwurf false, damit ein gefälschtes Paket nie mit dem Stand seines Vorgängers eine Prüfung auslöst.
    /// </summary>
    public bool NeuerHoechstwert { get; private set; }

    /// <summary>Schritt-6-Vorprüfung, nur lesend; etwa für die Kopierprüfung (5.10).</summary>
    public bool IstNeu(ulong zaehler) => _fenster.IstNeu(zaehler);

    /// <summary>Server, Rotation Schritt 1: nimmt ab sofort auch die angekündigte Epoche e+1 an.</summary>
    public void Ankuendigen(RichtungsSchluessel naechste, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(naechste);
        Takt(jetzt);
        if (_naechste is not null || _vorige is not null)
            throw new InvalidOperationException("Es läuft schon eine Rotation.");
        NaechsteFolgt(naechste);
        _naechste = new Epochenstand(naechste);
    }

    /// <summary>Server, Rotation Schritt 3: e+1 wird aktuell, e bleibt noch 10 s angenommen.</summary>
    public void Uebernehmen(long jetzt)
    {
        if (_naechste is null) throw new InvalidOperationException("Keine Epoche angekündigt.");
        _vorige = _aktuell;
        _vorigeFrist = jetzt + Uhr.Ticks(_grenzen.VorigeNachUmschalten);
        _aktuell = _naechste;
        _naechste = null;
    }

    /// <summary>
    /// Client, Rotation Schritte 2 und 4: e+1 sofort aktuell, e bleibt bis 10 s nach dem ersten Paket in e+1
    /// angenommen, spätestens bis 20 s nach der Antwort (<paramref name="jetzt"/>).
    /// </summary>
    public void Umschalten(RichtungsSchluessel neu, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(neu);
        if (_naechste is not null) throw new InvalidOperationException("Umschalten ist der Weg des Clients, Ankündigen der des Servers.");
        NaechsteFolgt(neu);
        VorigeVerwerfen();
        _vorige = _aktuell;
        _vorigeFrist = jetzt + Uhr.Ticks(_grenzen.VorigeSpaetestens);
        _wartetAufErstesPaket = true;
        _aktuell = new Epochenstand(neu);
    }

    /// <summary>Verwirft die vorige Epoche, wenn ihre Frist abgelaufen ist (<c>Dispose</c>).</summary>
    public void Takt(long jetzt)
    {
        if (_vorige is not null && jetzt >= _vorigeFrist) VorigeVerwerfen();
    }

    /// <summary>
    /// Schritte 5 bis 8 der Empfangsprüfung: Epoche angenommen, Replay-Vorprüfung, entschlüsseln, Fenster setzen.
    /// Das Datagramm muss die Schritte 1 bis 4 bestanden haben. <paramref name="duplikatEntschluesseln"/> ist die
    /// Ausnahme der Kopierprüfung (5.10): Ein Duplikat wird trotzdem entschlüsselt, ergibt aber höchstens
    /// <see cref="Empfangsergebnis.AuthentischesDuplikat"/> und verändert das Fenster nicht.
    /// </summary>
    public Empfangsergebnis Oeffnen(ReadOnlySpan<byte> datagramm, Span<byte> klartext, long jetzt, bool duplikatEntschluesseln = false)
    {
        NeuerHoechstwert = false;
        if (datagramm.Length < Sprachkanal.Ueberhang) return Empfangsergebnis.LaengeUngueltig;
        var epoche = datagramm[2];
        var zaehler = BinaryPrimitives.ReadUInt64BigEndian(datagramm[8..]);

        // 5. Epoche angenommen
        var stand = Stand(epoche, jetzt);
        if (stand is null) return Empfangsergebnis.EpocheNichtAngenommen;

        // 6. Replay-Vorprüfung, nur lesend
        var duplikat = !_fenster.IstNeu(zaehler);
        if (duplikat && !duplikatEntschluesseln) return Empfangsergebnis.Replay;

        // 7. Entschlüsseln; bei falschem Tag zählen, Fenster unverändert
        Entschluesselungen++;
        if (!Datagramm.Oeffnen(stand.Schluessel, datagramm, klartext))
        {
            stand.Fehlschlaege++;
            return Empfangsergebnis.TagUngueltig;
        }
        if (duplikat) return Empfangsergebnis.AuthentischesDuplikat;

        // 8. Replay-Fenster atomar prüfen und setzen, maßgeblich
        if (!_fenster.Annehmen(zaehler, out var neuerHoechstwert)) return Empfangsergebnis.ReplayNachTag;
        NeuerHoechstwert = neuerHoechstwert;
        stand.Empfangen++;
        if (_wartetAufErstesPaket && stand == _aktuell)
        {
            _wartetAufErstesPaket = false;
            _vorigeFrist = Math.Min(_vorigeFrist, jetzt + Uhr.Ticks(_grenzen.VorigeNachErstemPaket));
        }
        return Empfangsergebnis.Angenommen;
    }

    public void Dispose()
    {
        _aktuell.Schluessel.Dispose();
        _naechste?.Schluessel.Dispose();
        _vorige?.Schluessel.Dispose();
    }

    /// <summary>Angenommen werden nur die aktuelle, die angekündigte nächste und bis zur Frist die vorige Epoche (mod 256).</summary>
    private Epochenstand? Stand(byte epoche, long jetzt)
    {
        if (epoche == _aktuell.Epoche) return _aktuell;
        if (_naechste is not null && epoche == _naechste.Epoche) return _naechste;
        if (_vorige is not null && epoche == _vorige.Epoche)
        {
            if (jetzt < _vorigeFrist) return _vorige;
            VorigeVerwerfen();
        }
        return null;
    }

    private void NaechsteFolgt(RichtungsSchluessel neu)
    {
        if (neu.Epoche != unchecked((byte)(Epoche + 1)))
            throw new ArgumentException($"Nach Epoche {Epoche} kommt {(byte)(Epoche + 1)}, nicht {neu.Epoche}.", nameof(neu));
    }

    private void VorigeVerwerfen()
    {
        _vorige?.Schluessel.Dispose();
        _vorige = null;
        _wartetAufErstesPaket = false;
    }
}
