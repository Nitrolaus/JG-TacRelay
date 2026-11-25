// JG TacRelay - Maintainer: Sarevoc
namespace Funk.ClientKern;

/// <summary>
/// Die Uhr des Servers im Client (Meldesystem 0.12.0: Countdown, Bereit-Abfrage), laufend abgeglichen über Ping wie bei NTP. Jede
/// Ping-Antwort trägt <c>serverZeit</c> (Unix-ms); mit Sende- und Empfangszeit auf der monotonen Uhr des Clients ergibt sich der
/// Versatz unter der Annahme gleich langer Hin- und Rückwege, der Fehler ist höchstens die halbe Laufzeit. Von den letzten
/// <see cref="Fenster"/> Proben gilt die mit der kürzesten Laufzeit; so stört ein einzelner Stau nicht, und eine Änderung der
/// Serveruhr ist nach spätestens einem Fenster (16 s bei 2 s Ping) übernommen. Ziel ist eine Genauigkeit um 0,1 s; im LAN und
/// bei üblichen Laufzeiten unter 200 ms liegt sie darunter.
/// </summary>
/// <remarks>
/// Gerechnet wird gegen die monotone Uhr, nicht gegen die Wanduhr des PCs: Stellt Windows die PC-Uhr um, bleibt die Serverzeit
/// richtig. Bis zur ersten Ping-Antwort gilt grob <c>Welcome.serverZeit</c> ohne Laufzeitausgleich, ganz ohne Angabe die eigene
/// Wanduhr. Threadsicher: geschrieben wird aus der Leseschleife der Sitzung, gelesen von der Oberfläche.
/// </remarks>
public sealed class Uhrabgleich
{
    /// <summary>So viele Proben zählen (bei 2 s Ping 16 s).</summary>
    public const int Fenster = 8;

    private readonly Func<long> _mono;
    private readonly Func<long> _wand;
    private readonly Lock _sperre = new();
    private readonly Queue<(long Versatz, long Laufzeit)> _proben = new();
    private long _versatz;
    private long _laufzeit;
    private Abgleichsstufe _stufe;

    /// <param name="mono">Monotone Uhr in ms (ab Werk <see cref="Monouhr.Ms"/>); Tests geben eine eigene.</param>
    /// <param name="wand">Wanduhr in Unix-ms für den Fall ganz ohne Angabe des Servers (ab Werk die Systemuhr).</param>
    public Uhrabgleich(Func<long>? mono = null, Func<long>? wand = null)
    {
        _mono = mono ?? Monouhr.Ms;
        _wand = wand ?? (static () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>Wie gut die Uhr gerade abgeglichen ist.</summary>
    public Abgleichsstufe Stufe
    {
        get
        {
            lock (_sperre) return _stufe;
        }
    }

    /// <summary>
    /// Größter Fehler der Serverzeit in ms (halbe Laufzeit der besten Probe, aufgerundet); <c>null</c>, solange keine Ping-Probe da
    /// ist.
    /// </summary>
    public long? GenauigkeitMs
    {
        get
        {
            lock (_sperre) return _stufe == Abgleichsstufe.Ping ? (_laufzeit + 1) / 2 : null;
        }
    }

    /// <summary>Laufzeit (Hin und zurück) der besten Probe im Fenster in ms; <c>null</c> ohne Probe.</summary>
    public long? LaufzeitMs
    {
        get
        {
            lock (_sperre) return _stufe == Abgleichsstufe.Ping ? _laufzeit : null;
        }
    }

    /// <summary>Die Uhr des Servers jetzt, in Unix-ms.</summary>
    public long ServerJetztMs() => ServerZuMono(_mono());

    /// <summary>Die Uhr des Servers zum monotonen Zeitpunkt <paramref name="monoMs"/> (ms der monotonen Uhr), in Unix-ms.</summary>
    public long ServerZuMono(long monoMs)
    {
        lock (_sperre)
        {
            if (_stufe != Abgleichsstufe.Keine) return monoMs + _versatz;
        }
        return _wand() + (monoMs - _mono());
    }

    /// <summary>
    /// Eine Ping-Probe: <paramref name="gesendetMs"/> und <paramref name="empfangenMs"/> auf der monotonen Uhr (Anfrage los, Antwort
    /// da), <paramref name="serverZeitMs"/> aus <c>PingAntwort.serverZeit</c>. Eine Probe mit negativer Laufzeit zählt nicht.
    /// </summary>
    public void Probe(long gesendetMs, long empfangenMs, long serverZeitMs)
    {
        var laufzeit = empfangenMs - gesendetMs;
        if (laufzeit < 0) return;
        // Der Server stempelt, wenn er die Anfrage beantwortet; bei gleich langen Wegen ist das die Mitte.
        var versatz = serverZeitMs - (gesendetMs + laufzeit / 2);
        lock (_sperre)
        {
            _proben.Enqueue((versatz, laufzeit));
            while (_proben.Count > Fenster) _proben.Dequeue();
            // Die kürzeste Laufzeit gewinnt, bei gleicher die jüngste Probe.
            var beste = _proben.Peek();
            foreach (var p in _proben)
                if (p.Laufzeit <= beste.Laufzeit) beste = p;
            _versatz = beste.Versatz;
            _laufzeit = beste.Laufzeit;
            _stufe = Abgleichsstufe.Ping;
        }
    }

    /// <summary>
    /// Grober Abgleich aus <c>Welcome.serverZeit</c> beim Empfang (<paramref name="empfangenMs"/> auf der monotonen Uhr), ohne
    /// Laufzeitausgleich. Zählt nur, solange noch keine Ping-Probe da ist.
    /// </summary>
    public void Grob(long serverZeitMs, long empfangenMs)
    {
        lock (_sperre)
        {
            if (_stufe == Abgleichsstufe.Ping) return;
            _versatz = serverZeitMs - empfangenMs;
            _stufe = Abgleichsstufe.Welcome;
        }
    }

    /// <summary>Alles vergessen (neue Verbindung, womöglich zu einem anderen Server).</summary>
    public void Zuruecksetzen()
    {
        lock (_sperre)
        {
            _proben.Clear();
            _versatz = 0;
            _laufzeit = 0;
            _stufe = Abgleichsstufe.Keine;
        }
    }
}

/// <summary>Woher die Serverzeit in <see cref="Uhrabgleich"/> kommt.</summary>
public enum Abgleichsstufe
{
    /// <summary>Noch keine Angabe des Servers: eigene Wanduhr.</summary>
    Keine,

    /// <summary>Aus <c>Welcome.serverZeit</c>, ohne Laufzeitausgleich.</summary>
    Welcome,

    /// <summary>Aus Ping-Proben mit Laufzeitausgleich.</summary>
    Ping,
}
