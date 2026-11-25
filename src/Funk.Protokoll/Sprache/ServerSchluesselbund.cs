// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Sprache;

/// <summary>Warum der Server rotieren will (5.8).</summary>
public enum Rotationsgrund : byte
{
    Keiner = 0,

    /// <summary>60 min seit Epochenbeginn.</summary>
    Zeit,

    /// <summary>2^20 Pakete runter gesendet.</summary>
    PaketeRunter,

    /// <summary>2^20 Pakete hoch angenommen.</summary>
    PaketeHoch,

    /// <summary>Mehr als 2^16 gescheiterte Entschlüsselungen: Rotation erzwingen und Angriffsverdacht protokollieren.</summary>
    Angriffsverdacht,
}

/// <summary>
/// Schlüssel, Epochen und Rotation einer Sitzung im Server (5.8), als Zustandsobjekt des Relay-Threads.
/// Hält den Empfangskanal hoch und den Sendekanal runter. Ablauf "make before break":
/// <see cref="RotationBeginnen(ReadOnlySpan{byte}, long)"/> (nimmt hoch e und e+1 an),
/// <see cref="AntwortErhalten"/> (sendet runter in e+1, verwirft e nach 10 s), <see cref="MussTrennen"/> ohne Antwort binnen 10 s.
/// Nicht threadsicher; andere Threads ändern ihn nur über die Warteschlange des Relay-Threads (5.14).
/// </summary>
public sealed class ServerSchluesselbund : IDisposable
{
    private readonly Rotationsgrenzen _grenzen;
    private RichtungsSchluessel? _runterNaechste;
    private byte? _erwarteteAntwort;
    private long _antwortFrist;

    /// <summary>Sitzungsbeginn mit dem Material der Epoche 0 (VoiceKeys START); wartet auf die Antwort.</summary>
    public ServerSchluesselbund(uint sitzungsId, ReadOnlySpan<byte> material, long jetzt, Rotationsgrenzen? grenzen = null)
    {
        _grenzen = grenzen ?? Rotationsgrenzen.Standard;
        Hoch = new EmpfangsKanal(sitzungsId, Richtung.Hoch, Schluesselmaterial.Hoch(0, material), _grenzen);
        Runter = new SendeKanal(sitzungsId, Richtung.Runter, Schluesselmaterial.Runter(0, material), jetzt, _grenzen);
        Erwarten(0, jetzt);
    }

    /// <summary>Wie der Konstruktor, erzeugt das Material aber selbst und legt es für VoiceKeys in <paramref name="materialAusgabe"/>.</summary>
    public static ServerSchluesselbund Erzeugen(uint sitzungsId, long jetzt, Span<byte> materialAusgabe, Rotationsgrenzen? grenzen = null)
    {
        Schluesselmaterial.Erzeugen(materialAusgabe);
        return new ServerSchluesselbund(sitzungsId, materialAusgabe[..Schluesselmaterial.Laenge], jetzt, grenzen);
    }

    public uint SitzungsId => Hoch.SitzungsId;

    /// <summary>Empfang hoch: Schritte 5 bis 8 der Empfangsprüfung.</summary>
    public EmpfangsKanal Hoch { get; }

    /// <summary>Senden runter an diese Sitzung als Empfänger.</summary>
    public SendeKanal Runter { get; }

    /// <summary>Epoche, in der runter gesendet wird.</summary>
    public byte Epoche => Runter.Epoche;

    public bool AntwortAusstehend => _erwarteteAntwort.HasValue;

    /// <summary>
    /// Eine Rotation läuft von der Ankündigung bis zum Verwerfen der vorigen Epoche; höchstens eine zugleich.
    /// Stand des letzten <see cref="Takt"/>, <see cref="RotationFaellig"/> oder <see cref="RotationMoeglich"/>.
    /// </summary>
    public bool RotationLaeuft => _runterNaechste is not null || Hoch.NaechsteEpoche.HasValue || Hoch.VorigeEpoche.HasValue;

    /// <summary>Prüft die Auslöser aus 5.8. Während einer Rotation oder einer offenen Antwort nie.</summary>
    public Rotationsgrund RotationFaellig(long jetzt)
    {
        Hoch.Takt(jetzt);
        if (AntwortAusstehend || RotationLaeuft) return Rotationsgrund.Keiner;
        if (Hoch.AngriffsVerdacht) return Rotationsgrund.Angriffsverdacht;
        if (jetzt - Runter.EpochenBeginn >= Uhr.Ticks(_grenzen.EpochenDauer)) return Rotationsgrund.Zeit;
        if (Runter.GesendetInEpoche >= _grenzen.PaketeBisRotation) return Rotationsgrund.PaketeRunter;
        if (Hoch.EmpfangenInEpoche >= _grenzen.PaketeBisRotation) return Rotationsgrund.PaketeHoch;
        return Rotationsgrund.Keiner;
    }

    /// <summary>Ob jetzt eine Rotation beginnen darf, etwa auf KeyRotation des Clients hin.</summary>
    public bool RotationMoeglich(long jetzt)
    {
        Hoch.Takt(jetzt);
        return !AntwortAusstehend && !RotationLaeuft;
    }

    /// <summary>Rotation Schritt 1 mit neuem Zufallsmaterial, das für VoiceKeys in <paramref name="materialAusgabe"/> landet.</summary>
    public byte RotationBeginnen(long jetzt, Span<byte> materialAusgabe)
    {
        Schluesselmaterial.Erzeugen(materialAusgabe);
        return RotationBeginnen(materialAusgabe[..Schluesselmaterial.Laenge], jetzt);
    }

    /// <summary>Rotation Schritt 1: installiert e+1; ab sofort nimmt der Server hoch e und e+1 an. Gibt e+1 zurück.</summary>
    public byte RotationBeginnen(ReadOnlySpan<byte> material, long jetzt)
    {
        if (!RotationMoeglich(jetzt)) throw new InvalidOperationException("Es läuft schon eine Rotation oder eine Antwort steht aus.");
        var naechste = unchecked((byte)(Epoche + 1));
        var hoch = Schluesselmaterial.Hoch(naechste, material);
        try
        {
            Hoch.Ankuendigen(hoch, jetzt);
        }
        catch
        {
            hoch.Dispose();
            throw;
        }
        _runterNaechste = Schluesselmaterial.Runter(naechste, material);
        Erwarten(naechste, jetzt);
        return naechste;
    }

    /// <summary>
    /// Antwort des Clients auf VoiceKeys. Bei einer Rotation sendet der Server ab jetzt runter in e+1 (Schritt 3).
    /// false heißt: falsche Epoche, keine Anfrage offen oder Frist verpasst, also PROTOKOLL.
    /// </summary>
    public bool AntwortErhalten(byte epoche, long jetzt)
    {
        if (_erwarteteAntwort != epoche || jetzt >= _antwortFrist) return false;
        _erwarteteAntwort = null;
        if (_runterNaechste is not null)
        {
            Runter.Umschalten(_runterNaechste, jetzt);
            _runterNaechste = null;
            Hoch.Uebernehmen(jetzt);
        }
        return true;
    }

    /// <summary>Keine Antwort auf VoiceKeys binnen 10 s: Der Server trennt, der Client verbindet neu.</summary>
    public bool MussTrennen(long jetzt) => _erwarteteAntwort.HasValue && jetzt >= _antwortFrist;

    /// <summary>Zeitgeber: verwirft die vorige Epoche nach ihrer Frist.</summary>
    public void Takt(long jetzt) => Hoch.Takt(jetzt);

    public void Dispose()
    {
        Hoch.Dispose();
        Runter.Dispose();
        _runterNaechste?.Dispose();
    }

    private void Erwarten(byte epoche, long jetzt)
    {
        _erwarteteAntwort = epoche;
        _antwortFrist = jetzt + Uhr.Ticks(_grenzen.AntwortFrist);
    }
}
