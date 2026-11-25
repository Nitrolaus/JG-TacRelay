// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Funk.Protokoll.Schutz;
using Funk.Protokoll.Sprache;
using Funk.Server.Betrieb;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Sprachpfad;

/// <summary>
/// Der Sprachpfad des Servers (5.9 bis 5.14, 6.3, 6.4, 7.4, 7.8). Genau ein Relay-Thread verarbeitet alle Datagramme
/// aus UDP und Tunnel: Empfangsprüfung, Budgets, Replay-Fenster, Adressbindung und Pfadprüfung, Keepalive-Echo,
/// Weiterleitung mit Stempel und Neuversiegeln, Rotation und Messwerte. Ihm gehören alle <c>AesGcm</c>-Instanzen des
/// Sprachpfads; andere Threads ändern den Zustand nur über Befehle in seiner Warteschlange. Je UDP-Socket gibt es einen
/// Empfangsthread, der Puffer aus einem Vorrat über eine begrenzte Warteschlange übergibt (voll heißt verwerfen), und
/// einen Sende-Thread mit Pacer (<see cref="Ausgang"/>). Auf Ungültiges antwortet der Server nie.
/// </summary>
public sealed class Relais : L.IWeiterleitungsZiel, L.IZustellung, IDisposable
{
    private static readonly long TaktAbstand = Zeit.TakteAusMs(20);

    /// <summary>Länge einer PFAD_PRUEFUNG auf dem Draht (Kopf, 16 Byte Challenge, Tag): 48 Byte.</summary>
    internal const int PruefpaketLaenge = Steuerpakete.Laenge + Sprachkanal.Ueberhang;

    private readonly Func<L.Schnappschuss> _lage;
    private readonly IRelaisSenke _senke;
    private readonly Logbuch _log;
    private readonly RelaisOptionen _optionen;
    private readonly IReadOnlyList<Socket> _sockets;
    private readonly BlockingCollection<Empfangspaket> _eingang;
    private readonly ConcurrentQueue<Empfangspaket> _vorrat = new();
    private readonly ConcurrentQueue<Action> _befehle = new();
    private readonly Empfangspaket _wecker = new();
    private readonly Dictionary<uint, RelaisSitzung> _sitzungen = [];
    private readonly Vorfilter _vorfilter = new();
    private long _vorfilterGemeldet;
    private readonly L.Weiterleiter _weiterleiter;
    private readonly Ausgang _ausgang;
    private readonly Umgebung _umgebung;
    private readonly byte[] _klartext = new byte[Sprachkanal.MaxKlartext];
    private readonly byte[] _runter = new byte[Sprachkanal.MaxKlartext];
    private readonly List<Thread> _faeden = [];
    private readonly ConcurrentDictionary<uint, (S.Verbindungsweg Weg, S.Guete Guete)> _anzeige = new();
    private readonly List<double> _verarbeitungMs = new(4096);
    private volatile bool _stop;
    private long _letzterTakt;
    private long _letztesLastfenster;
    private int _laststufe;
    private long _lastSeit;

    private readonly Dictionary<uint, (Sprecher Eintrag, ushort[] Ziele, long Letztes)> _sprecher = [];
    private bool _anzeigeGeaendert;
    private long _letzteAnzeige;
    private long _verarbeitet;
    private long _verworfenVoll;
    private long _verworfenPruefung;
    private double _p50;
    private double _p99;

    public Relais(Func<L.Schnappschuss> lage, IRelaisSenke senke, Logbuch log, RelaisOptionen optionen, IReadOnlyList<Socket> sockets)
    {
        ArgumentNullException.ThrowIfNull(lage);
        ArgumentNullException.ThrowIfNull(senke);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(optionen);
        ArgumentNullException.ThrowIfNull(sockets);
        _lage = lage;
        _senke = senke;
        _log = log;
        _optionen = optionen;
        _sockets = sockets;
        _eingang = new BlockingCollection<Empfangspaket>(new ConcurrentQueue<Empfangspaket>(), optionen.Kapazitaet);
        _weiterleiter = new L.Weiterleiter(this);
        _ausgang = new Ausgang(sockets, optionen.AusgangMbit, optionen.LeitungsZuschlag, ex => log.Fehler("Ausgang", ex));
        _umgebung = new Umgebung(this);
        _letzterTakt = Zeit.Takte();
        _letztesLastfenster = _letzterTakt;
    }

    /// <summary>Verbindungsweg und Güte je Sitzung für Praesenz (7.8); von jedem Thread lesbar.</summary>
    public bool Anzeige(uint sitzung, out S.Verbindungsweg weg, out S.Guete guete)
    {
        if (_anzeige.TryGetValue(sitzung, out var a))
        {
            weg = a.Weg;
            guete = a.Guete;
            return true;
        }
        weg = default;
        guete = default;
        return false;
    }

    /// <summary>Startet Relay-, Sende- und Empfangsthreads.</summary>
    public void Starten()
    {
        _ausgang.Starten();
        var relay = new Thread(Schleife) { IsBackground = true, Name = "Funk-Relais", Priority = ThreadPriority.AboveNormal };
        _faeden.Add(relay);
        for (var i = 0; i < _sockets.Count; i++)
        {
            var index = i;
            _faeden.Add(new Thread(() => Empfangen(index)) { IsBackground = true, Name = "Funk-UDP-" + index.ToString(CultureInfo.InvariantCulture) });
        }
        foreach (var f in _faeden) f.Start();
    }

    // ---- Befehle (threadsicher) ----

    /// <summary>
    /// Sitzung mit dem Material der Epoche 0 anlegen (VoiceKeys START ist unterwegs). <paramref name="material"/> wird
    /// im Relay-Thread übernommen und genullt. Sprache gilt erst nach <see cref="VoiceKeysBeantwortet"/>.
    /// </summary>
    public void SitzungAnlegen(uint sitzung, uint kontoId, Adresse tlsIp, byte[] material, SitzungsZaehler zaehler)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(zaehler);
        Befehl(() =>
        {
            try
            {
                var t = Zeit.Takte();
                if (_sitzungen.Remove(sitzung, out var alt)) alt.Dispose();
                var bund = new ServerSchluesselbund(sitzung, material, t, _optionen.Rotation);
                _sitzungen[sitzung] = new RelaisSitzung(sitzung, kontoId, tlsIp, bund, zaehler, t);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(material);
            }
        });
    }

    /// <summary>Antwort des Clients auf VoiceKeys (START oder ROTATION).</summary>
    public void VoiceKeysBeantwortet(uint sitzung, byte epoche) => Befehl(() =>
    {
        if (!_sitzungen.TryGetValue(sitzung, out var s)) return;
        if (!s.Bund.AntwortErhalten(epoche, Zeit.Takte()))
        {
            _senke.Trennen(sitzung, S.Fehlercode.Protokoll);
            return;
        }
        if (!s.Aktiv)
        {
            s.Aktiv = true;
            _anzeige[sitzung] = (S.Verbindungsweg.Udp, S.Guete.Gut);
        }
    });

    /// <summary>Sitzung endet: Schlüssel verwerfen (<c>Dispose</c>), Phasen schließen (4.6 ENDE, 6.4).</summary>
    public void SitzungEntfernen(uint sitzung) => Befehl(() =>
    {
        if (_sitzungen.Remove(sitzung, out var s)) s.Dispose();
        _anzeige.TryRemove(sitzung, out _);
        _anzeigeGeaendert |= _sprecher.Remove(sitzung);
        _weiterleiter.SitzungBeendet(_lage(), sitzung, Zeit.LageMs(), this);
    });

    /// <summary>TunnelModus (5.12): nur der Client schaltet.</summary>
    public void TunnelSetzen(uint sitzung, bool an) => Befehl(() =>
    {
        if (!_sitzungen.TryGetValue(sitzung, out var s)) return;
        s.Tunnel = an;
        if (_anzeige.TryGetValue(sitzung, out var a)) _anzeige[sitzung] = (an ? S.Verbindungsweg.Tunnel : S.Verbindungsweg.Udp, a.Guete);
    });

    /// <summary>KeyRotation des Clients (4.5): höchstens eine Rotation zugleich.</summary>
    public void RotationAnfordern(uint sitzung) => Befehl(() =>
    {
        if (_sitzungen.TryGetValue(sitzung, out var s) && s.Aktiv) Rotieren(s, Zeit.Takte(), "Anforderung des Clients");
    });

    /// <summary>EmpfangsBericht (4.5) für die Messwerte (7.8).</summary>
    public void EmpfangsBericht(uint sitzung, S.EmpfangsBericht bericht)
    {
        ArgumentNullException.ThrowIfNull(bericht);
        Befehl(() =>
        {
            if (!_sitzungen.TryGetValue(sitzung, out var s)) return;
            var gesamt = (double)bericht.Runter.Empfangen + bericht.Runter.Verloren;
            s.Messwerte.RunterVerlustProzent = gesamt <= 0 ? 0 : 100.0 * bericht.Runter.Verloren / gesamt;
            s.Messwerte.ClientJitterMs = bericht.JitterMs;
            if (bericht.RttMs is { } rtt) s.Messwerte.RttMs = rtt.P50;
        });
    }

    /// <summary>SpracheTunnel-Rahmen hoch (5.12): Body in einen Eingangspuffer kopieren und an den Relay-Thread reichen.</summary>
    public bool TunnelEingang(uint tragendeSitzung, ReadOnlySpan<byte> datagramm)
    {
        if (datagramm.Length > Sprachkanal.EmpfangspufferLaenge) return false;
        var e = Leihen();
        datagramm.CopyTo(e.Puffer);
        e.Laenge = datagramm.Length;
        e.Empfangen = Zeit.Takte();
        e.Weg = Weg.Tunnel;
        e.Tragende = tragendeSitzung;
        e.Quelle = null;
        e.Abgeschnitten = false;
        if (_eingang.TryAdd(e)) return true;
        Interlocked.Increment(ref _verworfenVoll);
        Zurueck(e);
        return false;
    }

    /// <summary>Messwerte aller Sitzungen (Admin-Werkzeug, 7.4, 7.8).</summary>
    public Task<RelaisMesswerte> MesswerteAsync()
    {
        var tcs = new TaskCompletionSource<RelaisMesswerte>(TaskCreationOptions.RunContinuationsAsynchronously);
        Befehl(() => tcs.TrySetResult(Messwerte()));
        return tcs.Task;
    }

    private void Befehl(Action a)
    {
        _befehle.Enqueue(a);
        _eingang.TryAdd(_wecker);
    }

    // ---- Empfangsthreads ----

    private Empfangspaket Leihen() => _vorrat.TryDequeue(out var e) ? e : new Empfangspaket();

    private void Zurueck(Empfangspaket e)
    {
        if (!ReferenceEquals(e, _wecker)) _vorrat.Enqueue(e);
    }

    private void Empfangen(int index)
    {
        var socket = _sockets[index];
        while (!_stop)
        {
            var e = Leihen();
            if (e.Quelle is null || e.Quelle.Family != socket.AddressFamily) e.Quelle = new SocketAddress(socket.AddressFamily);
            try
            {
                var n = socket.ReceiveFrom(e.Puffer, SocketFlags.None, e.Quelle);
                e.Empfangen = Zeit.Takte();
                e.Laenge = n;
                // Füllt das Datagramm den Puffer, gilt es als abgeschnitten (5.9 Schritt 1).
                e.Abgeschnitten = n >= e.Puffer.Length;
                e.Socket = index;
                e.Weg = Weg.Udp;
                e.Tragende = 0;
                if (_eingang.TryAdd(e)) continue;
                Interlocked.Increment(ref _verworfenVoll);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.MessageSize or SocketError.ConnectionReset)
            {
                // Zu groß für den Puffer (Windows) oder ein ICMP-Echo auf eine frühere Sendung: verwerfen.
                Interlocked.Increment(ref _verworfenPruefung);
            }
            catch (SocketException)
            {
                if (_stop) return;
                // Etwa kein Pufferplatz im Kernel: kurz abgeben statt heiß zu drehen.
                Thread.Sleep(1);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Eine unbehandelte Ausnahme in einem eigenen Thread beendet den ganzen Prozess; der Empfang läuft weiter.
                if (_stop) return;
                Interlocked.Increment(ref _verworfenPruefung);
                FehlerBegrenzt("UDP-Empfang", ex);
            }
            Zurueck(e);
        }
    }

    private long _letzterFehler = -Stopwatch.Frequency;

    /// <summary>Höchstens eine Logbuchzeile je Sekunde, damit ein Dauerfehler das Logbuch nicht flutet.</summary>
    private void FehlerBegrenzt(string wo, Exception ex)
    {
        var jetzt = Zeit.Takte();
        var alt = Interlocked.Read(ref _letzterFehler);
        if (jetzt - alt < Stopwatch.Frequency || Interlocked.CompareExchange(ref _letzterFehler, jetzt, alt) != alt) return;
        _log.Fehler(wo, ex);
    }

    // ---- Relay-Thread ----

    private void Schleife()
    {
        while (!_stop)
        {
            try
            {
                if (_eingang.TryTake(out var e, 10))
                {
                    if (!ReferenceEquals(e, _wecker))
                    {
                        try
                        {
                            Verarbeiten(e);
                        }
                        finally
                        {
                            Zurueck(e);
                        }
                    }
                }
                while (_befehle.TryDequeue(out var b)) b();
                var t = Zeit.Takte();
                if (t - _letzterTakt >= TaktAbstand)
                {
                    _letzterTakt = t;
                    Takt(t);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Ein Fehler trifft nur dieses Datagramm oder diesen Befehl; der Sprachpfad läuft weiter.
                FehlerBegrenzt("Relais", ex);
            }
        }
    }

    private void Verarbeiten(Empfangspaket e)
    {
        var t = Zeit.Takte();
        Interlocked.Increment(ref _verarbeitet);
        var quelle = default(Adresse);
        if (e.Weg == Weg.Udp && (e.Quelle is null || !Adresse.TryAus(e.Quelle, out quelle)))
        {
            Interlocked.Increment(ref _verworfenPruefung);
            return;
        }
        _umgebung.Setzen(e.Weg, quelle, t);
        var daten = new Empfangsdaten(Richtung.Hoch, e.Weg, e.Empfangen, e.Abgeschnitten, e.Tragende);
        var datagramm = e.Puffer.AsSpan(0, e.Laenge);
        var ergebnis = Empfangspruefung.Pruefen(_umgebung, datagramm, daten, t, _klartext, out var kopf, out _);
        var s = _umgebung.Gefunden;
        if (ergebnis != Empfangsergebnis.Angenommen)
        {
            Interlocked.Increment(ref _verworfenPruefung);
            if (s is null) return;
            if (ergebnis == Empfangsergebnis.Replay) s.Messwerte.Doppelt();
            else if (ergebnis == Empfangsergebnis.AuthentischesDuplikat) Kopierverdacht(s, t);
            return;
        }
        if (s is null) return;
        var klartext = _klartext.AsSpan(0, e.Laenge - Sprachkanal.Ueberhang);
        var neuHoch = s.Bund.Hoch.NeuerHoechstwert;
        s.Messwerte.Angenommen(kopf.Zaehler, e.Weg == Weg.Tunnel);

        // 9. Adressbehandlung, nur UDP (5.10)
        if (e.Weg == Weg.Udp)
        {
            s.Zaehler.Empfangen();
            Adressbehandlung(s, quelle, e, kopf, klartext, neuHoch, t);
        }

        // 10. nach Typ
        switch (kopf.Typ)
        {
            case PaketTyp.SpracheHoch:
                Sprache(s, klartext, e.Empfangen);
                break;
            case PaketTyp.Keepalive when e.Weg == Weg.Udp && s.IstB(quelle):
                if (Keepalive.TryLesen(klartext, out var ka))
                {
                    Span<byte> echo = stackalloc byte[Steuerpakete.Laenge];
                    new KeepaliveEcho(ka.Nr, ka.ZeitMs, unchecked((uint)Zeit.MonoMs())).Schreiben(echo);
                    SendenUdp(s, s.BSock!, s.BSocket, PaketTyp.KeepaliveEcho, echo, 0, t);
                }
                break;
        }
        // Verarbeitungszeit ab Empfangsstempel (7.4); ein fester Probenpuffer, damit der Heißpfad nicht allokiert.
        if (_verarbeitungMs.Count < _verarbeitungMs.Capacity)
            _verarbeitungMs.Add((Zeit.Takte() - e.Empfangen) * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>
    /// 5.10: Pakete von B halten B lebendig; ein gültiges Paket mit neuem Höchstwert von einer anderen Adresse A löst
    /// eine Pfadprüfung aus (Challenge an A, Gegenprobe an B), eine kürzlich gebundene Adresse bindet sofort zurück.
    /// Umgebunden wird erst nach gültiger PFAD_ANTWORT, 500 ms Schweigen von B und ohne Kopierverdacht.
    /// </summary>
    private void Adressbehandlung(RelaisSitzung s, Adresse quelle, Empfangspaket e, in SprachKopf kopf, ReadOnlySpan<byte> klartext, bool neuHoch, long t)
    {
        if (s.IstB(quelle))
        {
            s.LetzteAuthVonB = t;
            if (kopf.Typ == PaketTyp.PfadAntwort && s.GegenprobeOffen && Steuerpakete.ChallengeGleich(klartext, s.Gegenprobe))
            {
                // B lebt: der Kandidat wird verworfen.
                s.KandidatenVerwerfen();
            }
            return;
        }

        // Keine Verstärkung (5.10, 7.5): die PFAD_PRUEFUNG an eine noch unbestätigte Adresse ist nie größer als das
        // auslösende Datagramm. Ein ENDE ohne Opus hat nur 41 bis 47 Byte und löst deshalb keine Prüfung aus.
        var darfPruefen = e.Laenge >= PruefpaketLaenge;
        var k = s.KandidatVon(quelle);
        if (k is not null)
        {
            if (s.KopierZaehler.Count < RelaisSitzung.MaxKopierzaehler) s.KopierZaehler.Add(kopf.Zaehler);
            if (kopf.Typ == PaketTyp.PfadAntwort)
            {
                if (t - k.ChallengeZeit <= RelaisSitzung.ChallengeGueltigTakte && Steuerpakete.ChallengeGleich(klartext, k.Challenge))
                {
                    k.Bestaetigt = true;
                    s.Messwerte.RttMs ??= Zeit.MsAusTakten(t - k.LetztePruefung);
                }
            }
            else if (neuHoch && !k.Bestaetigt && darfPruefen)
            {
                Pruefen(s, k, t);
            }
            BindenPruefen(s, t);
            return;
        }

        if (!neuHoch || kopf.Typ == PaketTyp.PfadAntwort) return;
        if (s.RueckkehrSuchen(quelle, t, out var rueckSock, out var rueckSocket))
        {
            Binden(s, quelle, rueckSock, rueckSocket, t);
            return;
        }
        if (!darfPruefen || s.OffeneKandidaten >= RelaisSitzung.MaxKandidaten) return;

        var neu = new Kandidat { A = quelle, Sock = Kopie(e.Quelle!), Socket = e.Socket };
        s.Kandidaten[s.Kandidaten[0] is null ? 0 : 1] = neu;
        if (s.KopierZaehler.Count < RelaisSitzung.MaxKopierzaehler) s.KopierZaehler.Add(kopf.Zaehler);
        Pruefen(s, neu, t);
        if (s.HatB && !neu.GegenprobeGesendet)
        {
            // Gegenprobe an die schon geprüfte Adresse B, höchstens eine je Kandidat.
            neu.GegenprobeGesendet = true;
            Steuerpakete.ChallengeErzeugen(s.Gegenprobe);
            s.GegenprobeOffen = true;
            s.GegenprobeZeit = t;
            SendenUdp(s, s.BSock!, s.BSocket, PaketTyp.PfadPruefung, s.Gegenprobe, 0, t);
        }
    }

    /// <summary>
    /// PFAD_PRUEFUNG an einen Kandidaten: nie mehr als eine je auslösendem Paket, Wiederholung frühestens nach 250 ms,
    /// höchstens 4 je Sekunde; eine neue Challenge an dieselbe Adresse ersetzt die alte. 48 Byte, also Verhältnis 1.
    /// </summary>
    private void Pruefen(RelaisSitzung s, Kandidat k, long t)
    {
        if (k.LetztePruefung != 0 && t - k.LetztePruefung < RelaisSitzung.WiederholungTakte) return;
        if (t - k.FensterStart >= RelaisSitzung.SekundeTakte)
        {
            k.FensterStart = t;
            k.ImFenster = 0;
        }
        if (k.ImFenster >= 4) return;
        k.ImFenster++;
        k.LetztePruefung = t;
        k.ChallengeZeit = t;
        Steuerpakete.ChallengeErzeugen(k.Challenge);
        SendenUdp(s, k.Sock, k.Socket, PaketTyp.PfadPruefung, k.Challenge, 0, t);
    }

    private void BindenPruefen(RelaisSitzung s, long t)
    {
        foreach (var k in s.Kandidaten)
        {
            if (k is null || !k.Bestaetigt) continue;
            // Erstbindung ohne B: die Frist aus (2) entfällt.
            if (s.HatB && t - s.LetzteAuthVonB < RelaisSitzung.UmbindungsfristTakte) continue;
            Binden(s, k.A, k.Sock, k.Socket, t);
            return;
        }
    }

    private void Binden(RelaisSitzung s, Adresse a, SocketAddress sock, int socket, long t)
    {
        var wechsel = s.HatB;
        s.Binden(a, sock, socket, t);
        if (wechsel) s.Messwerte.Pfadwechsel++;
        _senke.UdpStatus(s.Id, S.UdpZustand.Gebunden, a.AlsText(), a.OhnePort == s.TlsIp);
    }

    /// <summary>
    /// Kopierprüfung (5.10): Ein Duplikat von B mit gültigem Tag heißt, jemand hat echte Pakete kopiert und schneller
    /// zugestellt. Die Kandidaten fallen weg, ihre Adressen sind 60 s für diese Sitzung gesperrt.
    /// </summary>
    private void Kopierverdacht(RelaisSitzung s, long t)
    {
        foreach (var k in s.Kandidaten)
        {
            if (k is null) continue;
            if (s.Gesperrt.Count >= RelaisSitzung.MaxFremdadressen) s.Gesperrt.Remove(s.Gesperrt.MinBy(g => g.Value).Key);
            s.Gesperrt[k.A] = t + RelaisSitzung.SperreTakte;
        }
        s.KandidatenVerwerfen();
        s.Messwerte.Kopierverdacht++;
        _log.Warnung("Kopierverdacht an Sitzung von " + _log.Schwaerzung.Konto(s.KontoId));
        _senke.Kopierverdacht(s.Id, s.KontoId);
    }

    private void Sprache(RelaisSitzung s, ReadOnlySpan<byte> klartext, long empfangen)
    {
        // Nur die Struktur hier; die Formregeln (Zielart, Sortierung, ENDE, Opus, TOC) prüft der Weiterleiter (6.3).
        if (klartext.Length < SpracheHoch.FesterTeil) return;
        int n = klartext[8];
        if (n > Sprachkanal.DurchsageZieleMax || klartext.Length < SpracheHoch.FesterTeil + 2 * n) return;
        Span<ushort> ziele = stackalloc ushort[Sprachkanal.DurchsageZieleMax];
        for (var i = 0; i < n; i++) ziele[i] = BinaryPrimitives.ReadUInt16BigEndian(klartext[(SpracheHoch.FesterTeil + 2 * i)..]);
        var kz = (L.Paketkennzeichen)klartext[6];
        var frame = BinaryPrimitives.ReadUInt32BigEndian(klartext);
        var phase = BinaryPrimitives.ReadUInt16BigEndian(klartext[4..]);
        var paket = new L.Sprachpaket(s.Id, frame, phase, kz, (L.Sendeart)klartext[7], ziele[..n], klartext[(SpracheHoch.FesterTeil + 2 * n)..]);
        s.Messwerte.Sprache(phase, frame, (kz & L.Paketkennzeichen.Preroll) != 0, empfangen);
        var ergebnis = _weiterleiter.Verarbeiten(_lage(), paket, Zeit.LageMs(), this);
        if (!ergebnis.Verworfen) SprecherMerken(s, paket);
    }

    /// <summary>
    /// Sprechanzeige (4.5, 0x0053): wer gerade spricht, mit Art und Zielen der Phase. Ohne Allokation, solange sich
    /// nichts ändert; eine Phase endet mit ENDE oder nach 300 ms ohne Paket.
    /// </summary>
    private void SprecherMerken(RelaisSitzung s, in L.Sprachpaket paket)
    {
        if (paket.IstEnde)
        {
            _anzeigeGeaendert |= _sprecher.Remove(s.Id);
            return;
        }
        var t = Zeit.Takte();
        var art = paket.Zielart == L.Sendeart.Durchsage ? S.SprechArt.Durchsage
            : paket.Ziele.Contains(L.Netz.GlobalId) ? S.SprechArt.Global : S.SprechArt.Netz;
        if (_sprecher.TryGetValue(s.Id, out var alt) && alt.Eintrag.Art == art && alt.Ziele.AsSpan().SequenceEqual(paket.Ziele))
        {
            _sprecher[s.Id] = (alt.Eintrag, alt.Ziele, t);
            return;
        }
        var ziele = paket.Ziele.ToArray();
        _sprecher[s.Id] = (new Sprecher(s.KontoId, art, ziele), ziele, t);
        _anzeigeGeaendert = true;
    }

    private void SprechanzeigeTakt(long t)
    {
        foreach (var (id, e) in _sprecher)
        {
            if (t - e.Letztes <= Zeit.TakteAusMs(300)) continue;
            _sprecher.Remove(id);
            _anzeigeGeaendert = true;
        }
        if (!_anzeigeGeaendert || t - _letzteAnzeige < Zeit.TakteAusMs(200)) return;
        _anzeigeGeaendert = false;
        _letzteAnzeige = t;
        _senke.Sprechanzeige([.. _sprecher.Values.Select(e => e.Eintrag)]);
    }

    // ---- Senden ----

    private void SendenUdp(RelaisSitzung s, SocketAddress ziel, int socket, PaketTyp typ, ReadOnlySpan<byte> klartext, int rang, long t)
    {
        var p = _ausgang.Leihen();
        if (!s.Bund.Runter.Versiegeln(typ, klartext, p.Puffer, t, out var laenge))
        {
            _ausgang.Zurueckgeben(p);
            return;
        }
        p.Laenge = laenge;
        p.Ziel = ziel;
        p.Socket = socket;
        p.Rang = rang;
        p.V6 = ziel.Family == AddressFamily.InterNetworkV6;
        _ausgang.Einreihen(p);
        s.Zaehler.Gesendet();
        s.Messwerte.RunterGesendet();
    }

    private void SendenRunter(RelaisSitzung r, uint absender, ReadOnlySpan<byte> klartext, int rang, long t)
    {
        if (r.Tunnel)
        {
            var puffer = new byte[klartext.Length + Sprachkanal.Ueberhang];
            if (!r.Bund.Runter.Versiegeln(PaketTyp.SpracheRunter, klartext, puffer, t, out _)) return;
            r.Messwerte.RunterGesendet();
            _senke.Tunnel(r.Id, absender, puffer);
            return;
        }
        if (!r.HatB) return;
        SendenUdp(r, r.BSock!, r.BSocket, PaketTyp.SpracheRunter, klartext, rang, t);
    }

    // ---- IWeiterleitungsZiel, IZustellung ----

    public bool Zustellbar(uint empfaenger, uint absenderSitzung, ushort sprechphase, L.Stempelart art) =>
        _sitzungen.TryGetValue(empfaenger, out var r) && r.Aktiv && (r.Tunnel || r.HatB);

    public void Sprache(in L.Stempel stempel, ReadOnlySpan<ushort> netze, ReadOnlySpan<byte> opus)
    {
        if (!_sitzungen.TryGetValue(stempel.Empfaenger, out var r) || !r.Aktiv) return;
        var n = SpracheRunter.Schreiben(_runter, stempel.Absender, stempel.FrameNr, stempel.Sprechphase,
            (Kennzeichen)(byte)stempel.Kennzeichen, (Spruchart)(byte)stempel.Art, netze, opus);
        SendenRunter(r, stempel.Absender, _runter.AsSpan(0, n), stempel.Art == L.Stempelart.Netz ? 2 : 1, Zeit.Takte());
    }

    public void DurchsageStart(uint empfaenger, uint sprecher, L.Stempelart art, ReadOnlySpan<ushort> netze, ushort sprechphase) =>
        _senke.DurchsageStart(empfaenger, sprecher, art, netze, sprechphase);

    public void DurchsageEnde(uint empfaenger, uint sprecher, ushort sprechphase) => _senke.DurchsageEnde(empfaenger, sprecher, sprechphase);

    public void SendeFehler(uint sitzung, ReadOnlySpan<L.SendeFehlerEintrag> fehler, bool teilweise, uint verworfen) =>
        _senke.SendeFehler(sitzung, fehler, teilweise, verworfen);

    public void RateUeberschritten(uint sitzung, uint kontoId) => _senke.Rate(sitzung, kontoId);

    // ---- Budgets (5.9 Schritt 4) ----

    private bool BudgetNehmen(RelaisSitzung s, Weg weg, Adresse quelle, long t)
    {
        if (weg == Weg.Tunnel) return s.TunnelBudget.Nehmen(t);
        if (s.IstGesperrt(quelle, t)) return false;
        if (s.IstB(quelle)) return s.Gebunden.Nehmen(t);
        if (!_vorfilter.Nehmen(quelle.Praefix, t)) return false;
        return s.FremdNehmen(quelle, t);
    }

    // ---- Takt: Rotation, Fristen, Messwerte, Last ----

    private void Takt(long t)
    {
        var z = _lage();
        _weiterleiter.Takt(z, Zeit.LageMs(), this);
        foreach (var s in _sitzungen.Values)
        {
            s.Bund.Takt(t);
            if (s.Bund.MussTrennen(t))
            {
                _senke.Trennen(s.Id, S.Fehlercode.Protokoll);
                continue;
            }
            if (!s.Aktiv) continue;
            var grund = s.Bund.RotationFaellig(t);
            if (grund != Rotationsgrund.Keiner) Rotieren(s, t, grund.ToString());

            foreach (var k in s.Kandidaten)
            {
                if (k is null || t - k.ChallengeZeit <= RelaisSitzung.ChallengeGueltigTakte || k.Bestaetigt) continue;
                s.Kandidaten[Array.IndexOf(s.Kandidaten, k)] = null;
            }
            if (!s.KandidatOffen) s.KopierZaehler.Clear();
            BindenPruefen(s, t);

            if (s.Messwerte.FensterAbschliessen(t)) FensterAuswerten(s, t);
        }
        SprechanzeigeTakt(t);
        if (Zeit.MsAusTakten(t - _letztesLastfenster) >= 1000)
        {
            LastAuswerten(t);
            // Einmal je Sekunde und ohne Kopie der Tabelle; früher alle 20 ms mit ToArray über bis zu 65.536 Einträge.
            _vorfilter.Pflegen(t);
            if (_vorfilter.Uebergelaufen != _vorfilterGemeldet)
            {
                _log.Warnung(string.Create(CultureInfo.InvariantCulture,
                    $"Vorfilter voll ({_vorfilter.Anzahl} Präfixe): {_vorfilter.Uebergelaufen - _vorfilterGemeldet} Datagramme neuer Präfixe ohne Vorfilter, nur mit den Budgets je Sitzung"));
                _vorfilterGemeldet = _vorfilter.Uebergelaufen;
            }
        }
    }

    private void Rotieren(RelaisSitzung s, long t, string grund)
    {
        if (!s.Bund.RotationMoeglich(t)) return;
        var material = new byte[Schluesselmaterial.Laenge];
        var epoche = s.Bund.RotationBeginnen(t, material);
        if (grund == nameof(Rotationsgrund.Angriffsverdacht))
            _log.Warnung("Angriffsverdacht (mehr als 2^16 falsche Tags) an Sitzung von " + _log.Schwaerzung.Konto(s.KontoId) + ", Rotation erzwungen");
        else
            _log.Debug("Rotation auf Epoche " + epoche.ToString(CultureInfo.InvariantCulture) + " (" + grund + ") für " + _log.Schwaerzung.Konto(s.KontoId));
        _senke.VoiceKeys(s.Id, epoche, material);
    }

    private void FensterAuswerten(RelaisSitzung s, long t)
    {
        var m = s.Messwerte;
        var r = m.Letztes;
        var tunnelAnteil = r.Empfangen == 0 ? 0 : (double)r.Tunnel / r.Empfangen;
        _anzeige[s.Id] = (s.Tunnel ? S.Verbindungsweg.Tunnel : S.Verbindungsweg.Udp, m.Guete() switch
        {
            "GUT" => S.Guete.Gut,
            "MAESSIG" => S.Guete.Maessig,
            _ => S.Guete.Schlecht,
        });
        if (_log.Aktiv(Protokollstufe.Info) && (r.Empfangen > 0 || r.RunterGesendet > 0))
        {
            _log.Info(string.Create(CultureInfo.InvariantCulture,
                $"Messwerte {_log.Schwaerzung.Konto(s.KontoId)}: hoch {r.Empfangen} verloren {r.Verloren} umsortiert {r.Umsortiert} doppelt {r.Doppelt}, jitter {m.JitterMs:F1} ms, runter {r.RunterGesendet}, tunnel {tunnelAnteil:P0}, pfadwechsel {m.Pfadwechsel}, kopierverdacht {m.Kopierverdacht}, rtt {m.RttMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "-"} ms, runter-verlust {m.RunterVerlustProzent?.ToString("F1", CultureInfo.InvariantCulture) ?? "-"} %, guete {m.Guete()}"));
        }

        // EIGENER_VERLUST hoch: über 30 s mehr als 10 % (7.8), höchstens einmal je 30 s.
        if (m.VerlustVerlauf.Count == 3 && m.VerlustVerlauf.All(v => v > 10)
            && Zeit.MsAusTakten(t - m.LetzteEigenerVerlustMeldung) >= 30_000)
        {
            m.LetzteEigenerVerlustMeldung = t;
            _senke.EigenerVerlust(s.Id, "HOCH");
        }

        // VerlustHinweis (4.5) an Sender: eigener Verlust hoch plus p75 des Verlusts runter der Hörer.
        if (r.Erwartet > 0 && m.SprachpaketeGesamt > 0)
        {
            var hoerer = _sitzungen.Values.Where(x => x.Id != s.Id && x.Messwerte.RunterVerlustProzent is { } v && v <= 20)
                .Select(x => x.Messwerte.RunterVerlustProzent!.Value).Order().ToList();
            var p75 = hoerer.Count == 0 ? 0 : hoerer[(int)Math.Min(hoerer.Count - 1, Math.Ceiling(hoerer.Count * 0.75) - 1)];
            var prozent = (byte)Math.Clamp(Math.Round(m.VerlustProzent + p75), 0, 100);
            if ((m.LetzterHinweisWert == 255 || Math.Abs(prozent - m.LetzterHinweisWert) >= 2)
                && Zeit.MsAusTakten(t - m.LetzterHinweis) >= 5000)
            {
                m.LetzterHinweis = t;
                m.LetzterHinweisWert = prozent;
                _senke.VerlustHinweis(s.Id, prozent);
            }
        }
        foreach (var x in _sitzungen.Values)
        {
            if (x.Messwerte.RunterVerlustProzent is > 20 && Zeit.MsAusTakten(t - x.Messwerte.LetzteEigenerVerlustMeldung) >= 30_000)
            {
                x.Messwerte.LetzteEigenerVerlustMeldung = t;
                _senke.EigenerVerlust(x.Id, "RUNTER");
            }
        }
    }

    /// <summary>
    /// Überlaststufen (7.4): Stufe 1 bei Altersverwürfen im Pacer oder mittlerer Pacer-Wartezeit über 5 ms, Stufe 2 bei
    /// einer Verarbeitungszeit über 10 ms (Median über 1 s). Stufe 2 sperrt neue Registrierungen und unfreigegebene
    /// Verbindungen (über die Senke). Der Strom-Abwurf je Empfänger aus Stufe 1 ist noch nicht gebaut (offener Punkt).
    /// </summary>
    private void LastAuswerten(long t)
    {
        _letztesLastfenster = t;
        var warte = _ausgang.WartezeitAbholen();
        _verarbeitungMs.Sort();
        if (_verarbeitungMs.Count > 0)
        {
            _p50 = _verarbeitungMs[_verarbeitungMs.Count / 2];
            _p99 = _verarbeitungMs[Math.Min(_verarbeitungMs.Count - 1, (int)(_verarbeitungMs.Count * 0.99))];
        }
        _verarbeitungMs.Clear();
        var stufe = _p50 > 10 ? 2 : warte > 5 || Interlocked.Read(ref _ausgang.Altersverwurf) > _lastSeit ? 1 : 0;
        _lastSeit = Interlocked.Read(ref _ausgang.Altersverwurf);
        if (stufe == _laststufe) return;
        _laststufe = stufe;
        _log.Warnung("Laststufe " + stufe.ToString(CultureInfo.InvariantCulture));
        _senke.Last(stufe, (uint)Math.Min(uint.MaxValue, Interlocked.Read(ref _ausgang.Altersverwurf)), _ausgang.MbitGemessen);
    }

    private RelaisMesswerte Messwerte()
    {
        var liste = _sitzungen.Values.Select(s =>
        {
            var m = s.Messwerte;
            var r = m.Letztes;
            return new SitzungsMesswerte(s.Id, s.KontoId, s.Tunnel ? "TUNNEL" : "UDP", s.HatB, r.Empfangen, r.Verloren, r.Umsortiert, r.Doppelt,
                Math.Round(m.JitterMs, 2), r.RunterGesendet, r.Empfangen == 0 ? 0 : (double)r.Tunnel / r.Empfangen, m.Pfadwechsel,
                m.Kopierverdacht, m.RttMs, m.RunterVerlustProzent, m.Guete());
        }).ToList();
        return new RelaisMesswerte(_sitzungen.Count, Interlocked.Read(ref _verarbeitet), Interlocked.Read(ref _verworfenVoll),
            Interlocked.Read(ref _verworfenPruefung), Interlocked.Read(ref _ausgang.Gesendet), Interlocked.Read(ref _ausgang.Altersverwurf),
            _ausgang.MbitGemessen, _p50, _p99, _laststufe, liste);
    }

    private static SocketAddress Kopie(SocketAddress quelle)
    {
        var kopie = new SocketAddress(quelle.Family, quelle.Size);
        quelle.Buffer.Span[..quelle.Size].CopyTo(kopie.Buffer.Span);
        return kopie;
    }

    public void Dispose()
    {
        _stop = true;
        _eingang.TryAdd(_wecker);
        foreach (var f in _faeden)
            if (f.IsAlive && f != Thread.CurrentThread) f.Join(1000);
        _ausgang.Dispose();
        foreach (var s in _sitzungen.Values) s.Dispose();
        _sitzungen.Clear();
        _eingang.Dispose();
    }

    /// <summary>Umgebung der Empfangsprüfung (5.9 Schritte 3 und 4, Kopierprüfung), einmal je Datagramm gesetzt.</summary>
    private sealed class Umgebung(Relais relais) : IEmpfangsUmgebung
    {
        private Weg _weg;
        private Adresse _quelle;
        private long _jetzt;

        public RelaisSitzung? Gefunden { get; private set; }

        public void Setzen(Weg weg, Adresse quelle, long jetzt)
        {
            _weg = weg;
            _quelle = quelle;
            _jetzt = jetzt;
            Gefunden = null;
        }

        public EmpfangsKanal? KanalSuchen(uint sitzungsId)
        {
            Gefunden = relais._sitzungen.TryGetValue(sitzungsId, out var s) && s.Aktiv ? s : null;
            return Gefunden?.Bund.Hoch;
        }

        public bool BudgetNehmen(EmpfangsKanal kanal) => Gefunden is { } s && relais.BudgetNehmen(s, _weg, _quelle, _jetzt);

        public bool DuplikatEntschluesseln(EmpfangsKanal kanal, ulong zaehler) =>
            _weg == Weg.Udp && Gefunden is { } s && s.IstB(_quelle) && s.KandidatOffen && s.KopierZaehler.Contains(zaehler);
    }
}
