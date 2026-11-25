// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Geraete;
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>
/// Geräte der Klangmaschine, nur im Steuer-Thread (ohne eigene Threads: im Aufrufer): Ströme öffnen und schließen, Gerätewechsel
/// und Abbrüche, Echoreferenz. Die Ströme laufen auf den gewünschten Geräten oder, wenn keins gewählt ist oder das gewählte fehlt,
/// auf den Standardgeräten für Kommunikation.
/// </summary>
public sealed partial class KlangMaschine
{
    private void Starten(string? eingangId, string? ausgangId)
    {
        Schliessen();
        _wunschEingang = eingangId;
        _wunschAusgang = ausgangId;
        _aufnahmeFehler = null;
        _wiedergabeFehler = null;
        // Wiedergabe zuerst und jedes Gerät für sich: Sperrt Windows das Mikrofon (Datenschutz), hört man die Netze
        // trotzdem (A7); der Takt holt das Mikrofon später nach.
        var wiedergabe = Versuchen(WiedergabeAbgleichen, aufnahme: false);
        var aufnahme = Versuchen(AufnahmeAbgleichen, aufnahme: true);
        if (!wiedergabe && !aufnahme)
        {
            Schliessen();
            throw new InvalidOperationException("Weder Mikrofon noch Wiedergabe ließen sich öffnen. Mikrofon: " + _aufnahmeFehler
                + " Wiedergabe: " + _wiedergabeFehler);
        }
        _laeuft = true;
        _letzterVersuchMs = Environment.TickCount64;
        EchoAnpassen();
    }

    private void Stoppen()
    {
        _laeuft = false;
        _aufnahmeFehler = null;
        _wiedergabeFehler = null;
        Schliessen();
        // Kein Aufnahme-Thread mehr in der Kette: Eine laufende Hörprobe endet, sonst stünde sie beim nächsten Start mitten in
        // Aufnahme oder Wiedergabe. Ebenso ein Nachlauf der Sprechtaste (Q66); einen Klick hört ohne Wiedergabe niemand.
        _kette.ProbeZuruecksetzen();
        _ = _kette.TasteAbschliessen();
    }

    /// <summary>Aufnahme auf das gewünschte bzw. Standardgerät bringen; wirft, wenn es keins gibt oder das Öffnen scheitert.</summary>
    private void AufnahmeAbgleichen()
    {
        var ein = Aufloesen(_wunschEingang, _system.Eingaenge(), _system.StandardEingang());
        if (ein is null)
        {
            AufnahmeFreigeben();
            throw new InvalidOperationException("Kein Aufnahmegerät gefunden.");
        }
        if (_aufnahme?.GeraetId != ein) AufnahmeOeffnen(ein);
    }

    /// <summary>
    /// Die Aufnahme freigeben, während Audio weiterläuft (Strom abgebrochen, Gerät gewechselt oder weg). Ein offenes Loslassen schließt gleich ab,
    /// samt Nachlauf (Q66), und klickt, solange die Wiedergabe läuft; sonst liefe der Rest des Nachlaufs mit dem nächsten Strom weiter, in die seit
    /// dem Abbruch stillstehende Phase, und ein neuer Druck gälte bis dahin als Fortsetzung (Prüfung 0.9.3).
    /// </summary>
    private void AufnahmeFreigeben()
    {
        Freigeben(ref _aufnahme, ref _eingangswandler);
        if (_kette.TasteAbschliessen() && !_kette.Stumm && Volatile.Read(ref _wiedergabe) is not null) _empfang.Mischer.Klick?.Anfordern(KlickArt.Loslassen);
    }

    private void WiedergabeAbgleichen()
    {
        var aus = Aufloesen(_wunschAusgang, _system.Ausgaenge(), _system.StandardAusgang());
        if (aus is null)
        {
            LoopbackSchliessen();
            Freigeben(ref _wiedergabe, ref _ausgabewandler);
            throw new InvalidOperationException("Kein Wiedergabegerät gefunden.");
        }
        if (_wiedergabe?.GeraetId == aus) return;
        LoopbackSchliessen();
        WiedergabeOeffnen(aus);
    }

    /// <summary>
    /// Führt einen Abgleich aus und merkt den Fehler. Gemeldet (<see cref="Fehler"/>) wird nur ein neuer oder geänderter
    /// Grund, damit ein gesperrtes Mikrofon bei jedem Wiederholen nicht erneut meldet.
    /// </summary>
    private bool Versuchen(Action abgleich, bool aufnahme)
    {
        try
        {
            abgleich();
            if (aufnahme) _aufnahmeFehler = null;
            else _wiedergabeFehler = null;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var grund = Beschreiben(ex, aufnahme);
            var alt = aufnahme ? _aufnahmeFehler : _wiedergabeFehler;
            if (aufnahme) _aufnahmeFehler = grund;
            else _wiedergabeFehler = grund;
            if (!string.Equals(alt, grund, StringComparison.Ordinal))
                FehlerMelden(new InvalidOperationException((aufnahme ? "Mikrofon: " : "Wiedergabe: ") + grund, ex));
            return false;
        }
    }

    /// <summary>Lesbarer Grund; verweigert Windows den Zugriff (E_ACCESSDENIED), steht dort, wo man ihn erlaubt.</summary>
    internal static string Beschreiben(Exception ex, bool aufnahme)
    {
        var verweigert = ex is UnauthorizedAccessException || ex.InnerException is UnauthorizedAccessException
            || unchecked((uint)ex.HResult) == 0x80070005 || (ex.InnerException is { } innen && unchecked((uint)innen.HResult) == 0x80070005);
        if (verweigert && aufnahme)
            return "Windows verweigert den Zugriff auf das Mikrofon. In den Einstellungen unter Datenschutz und Sicherheit > Mikrofon "
                + "den Zugriff für Desktop-Apps erlauben, dann erneut versuchen.";
        return ex.Message;
    }

    private void Schliessen()
    {
        LoopbackSchliessen();
        Freigeben(ref _aufnahme, ref _eingangswandler);
        Freigeben(ref _wiedergabe, ref _ausgabewandler);
    }

    private void AufnahmeOeffnen(string id)
    {
        AufnahmeFreigeben();
        // Der Strom läuft schon, bevor sein Wandler steht (das Format kennt erst der offene Strom); bis dahin gehen die
        // ersten Daten verloren.
        Eingangswandler? wandler = null;
        var strom = _system.OeffneAufnahme(id, werte => Volatile.Read(ref wandler)?.Einspeisen(werte));
        try
        {
            Volatile.Write(ref wandler, new Eingangswandler(strom.Format, Aufnahmerahmen));
        }
        catch
        {
            strom.Dispose();
            throw;
        }
        strom.Abgebrochen += ursache => StromAbgebrochen(strom, ursache);
        _eingangswandler = wandler;
        Volatile.Write(ref _aufnahme, strom);
        LernEingangPruefen(id);
    }

    private void WiedergabeOeffnen(string id)
    {
        Freigeben(ref _wiedergabe, ref _ausgabewandler);
        Ausgabewandler? wandler = null;
        var strom = _system.OeffneWiedergabe(id, _o.AusgabeLatenzMs, werte =>
        {
            var w = Volatile.Read(ref wandler);
            if (w is null) werte.Clear();
            else w.Fuellen(werte);
        });
        try
        {
            Volatile.Write(ref wandler, new Ausgabewandler(strom.Format, Mischen));
        }
        catch
        {
            strom.Dispose();
            throw;
        }
        strom.Abgebrochen += ursache => StromAbgebrochen(strom, ursache);
        _ausgabewandler = wandler;
        Volatile.Write(ref _wiedergabe, strom);
        GrenzeWechseln(id);
    }

    private void LoopbackOeffnen(string ausgangId)
    {
        LoopbackSchliessen();
        Eingangswandler? wandler = null;
        var strom = _system.OeffneLoopback(ausgangId, werte => Volatile.Read(ref wandler)?.Einspeisen(werte));
        try
        {
            Volatile.Write(ref wandler, new Eingangswandler(strom.Format, Referenzrahmen));
        }
        catch
        {
            strom.Dispose();
            throw;
        }
        strom.Abgebrochen += ursache => StromAbgebrochen(strom, ursache);
        _loopbackWandler = wandler;
        Volatile.Write(ref _loopback, strom);
        _kette.EchoAn = true;
    }

    private void LoopbackSchliessen()
    {
        _kette.EchoAn = false;
        Freigeben(ref _loopback, ref _loopbackWandler);
    }

    /// <summary>AEC3 ein- oder ausschalten und die Echoreferenz (Loopback der Wiedergabe) dazu öffnen oder schließen.</summary>
    private void EchoAnpassen()
    {
        var an = _echoGewuenscht;
        if (_echo is not null && an != _apmMitEcho)
        {
            // Konfigurieren sperrt im APM; der Aufnahme-Thread wartet dort höchstens diesen einen Aufruf lang.
            _echo.Konfigurieren(ApmFuer(an));
            _apmMitEcho = an;
        }
        if (!an || _echo is null || !_laeuft)
        {
            LoopbackSchliessen();
            return;
        }
        if (_wiedergabe is null || _loopback?.GeraetId == _wiedergabe.GeraetId) return;
        try
        {
            LoopbackOeffnen(_wiedergabe.GeraetId);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ohne Referenz läuft die Kette weiter; AEC3 sieht dann Stille als Wiedergabe.
            FehlerMelden(new InvalidOperationException("Echoreferenz (Loopback) ließ sich nicht öffnen.", ex));
        }
    }

    /// <summary>Nach einem Geräteereignis oder Abbruch: Ströme auf die gewünschten bzw. neuen Standardgeräte bringen.</summary>
    private void GeraeteAbgleichen()
    {
        if (!_laeuft) return;
        _letzterVersuchMs = Environment.TickCount64;
        // Jedes Gerät für sich: Ein gesperrtes Mikrofon hält die Wiedergabe nicht auf und umgekehrt.
        _ = Versuchen(AufnahmeAbgleichen, aufnahme: true);
        _ = Versuchen(WiedergabeAbgleichen, aufnahme: false);
        try
        {
            EchoAnpassen();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            FehlerMelden(ex);
        }
    }

    private void Takt()
    {
        _empfang.Aufraeumen();
        // Nach einem Fehler seltener: Ein vom Datenschutz gesperrtes Mikrofon kommt erst frei, wenn der Nutzer es erlaubt.
        var abstand = _aufnahmeFehler is null && _wiedergabeFehler is null ? WiederholAbstandMs : WiederholAbstandFehlerMs;
        if (_laeuft && (_aufnahme is null || _wiedergabe is null || (_echoGewuenscht && _echo is not null && _loopback is null))
            && Environment.TickCount64 - _letzterVersuchMs >= abstand)
        {
            GeraeteAbgleichen();
        }
    }

    /// <summary>
    /// Ein Geräteereignis von Windows (neues Gerät, entfernt, neuer Standard): erst die Ströme abgleichen, dann der Oberfläche melden, dass sich die
    /// Geräteliste geändert hat (seit 0.10.0, <see cref="IKlangMaschine.GeraeteListeGeaendert"/>). Das Abo besteht seit dem Konstruktor, also auch
    /// ohne laufendes Audio; ein Fehler im Empfänger geht als Hintergrundfehler, nicht in den Steuer-Thread.
    /// </summary>
    private void GeraeteGeaendert() => Steuern(() =>
    {
        GeraeteAbgleichen();
        Melden(() => GeraeteListeGeaendert?.Invoke());
    });

    public event Action? GeraeteListeGeaendert;

    /// <summary>Ein Strom blieb von selbst stehen: freigeben, damit der Abgleich ihn neu öffnet (auch auf demselben Gerät).</summary>
    private void StromAbgebrochen(IAudioStrom strom, Exception? ursache)
    {
        FehlerMelden(new InvalidOperationException("Ein Audiostrom ist stehen geblieben: " + strom.GeraetId, ursache));
        Steuern(() =>
        {
            if (ReferenceEquals(_aufnahme, strom)) AufnahmeFreigeben();
            if (ReferenceEquals(_wiedergabe, strom))
            {
                LoopbackSchliessen();
                Freigeben(ref _wiedergabe, ref _ausgabewandler);
            }
            if (ReferenceEquals(_loopback, strom)) LoopbackSchliessen();
            GeraeteAbgleichen();
        });
    }

    private static string? Aufloesen(string? wunsch, IReadOnlyList<AudioGeraet> geraete, string? standard)
    {
        if (wunsch is not null && geraete.Any(g => g.Id == wunsch)) return wunsch;
        return standard;
    }

    /// <summary>Strom anhalten (blockiert, bis sein Audio-Thread steht), danach seinen Wandler freigeben.</summary>
    private void Freigeben<TWandler>(ref IAudioStrom? strom, ref TWandler? wandler) where TWandler : class, IDisposable
    {
        var s = strom;
        Volatile.Write(ref strom, null);
        try
        {
            s?.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ein schon entferntes Gerät kann beim Anhalten noch einen Fehler melden.
            FehlerMelden(ex);
        }
        wandler?.Dispose();
        wandler = null;
    }

    private ApmEinstellungen ApmFuer(bool echo) => _o.Apm with { Echounterdrueckung = echo && _o.Apm.Echounterdrueckung };
}
