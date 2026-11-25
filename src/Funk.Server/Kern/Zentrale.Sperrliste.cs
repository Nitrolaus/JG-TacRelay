// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Funk.Server.Betrieb;
using Funk.Server.Netzzugang;

namespace Funk.Server.Kern;

/// <summary>
/// Sperrliste (A51) in der Zentrale: beim Start laden, alle 10 s speichern (nur HMAC-Werte, <c>sperrliste.json</c>), jede
/// Minute aufräumen, jede neue Sperre ins Logbuch (Kennung, nie die Adresse) und auf Wunsch als Klartextzeile für fail2ban
/// in eine eigene Datei. Konsole: <c>sperrliste</c> und <c>entsperren</c>.
/// </summary>
public sealed partial class Zentrale
{
    /// <summary>Datei im Zustandsverzeichnis; Neustart und Update vergessen eine laufende Sperre so nicht.</summary>
    public const string SperrlistenDatei = "sperrliste.json";

    private static readonly Lock KlartextSperre = new();
    private long _letzteSperrlistenSpeicherung;

    /// <summary>Die eingebaute Sperrliste; der Dienst setzt sie beim Start über <see cref="SperrlisteEinrichten"/>.</summary>
    public Sperrliste Sperrliste { get; private set; } = null!;

    private string SperrlistenPfad => Konfiguration.ZustandsPfad(SperrlistenDatei);

    /// <summary>Beim Start: Liste übernehmen, gespeicherten Stand laden, Meldungen anschließen.</summary>
    internal void SperrlisteEinrichten(Sperrliste liste)
    {
        ArgumentNullException.ThrowIfNull(liste);
        Sperrliste = liste;
        if (Dateiablage.LesenFallsVorhanden(SperrlistenPfad) is { } gespeichert)
        {
            try
            {
                liste.AusJsonLaden(gespeichert, Zeit.UnixMs());
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
            {
                // Eine kaputte Sperrliste darf den Start nicht verhindern; schlimmstenfalls beginnt sie leer.
                Log.Warnung("Sperrliste: " + SperrlistenDatei + " nicht lesbar, beginne leer (" + ex.GetType().Name + ")");
            }
            var n = liste.Gesperrte(Zeit.UnixMs()).Count;
            if (n > 0) Log.Info(string.Create(CultureInfo.InvariantCulture, $"Sperrliste: {n} Sperre(n) aus {SperrlistenDatei} übernommen"));
        }
        var klartext = Konfiguration.Sperrliste.KlartextDatei is { } d ? Konfiguration.Aufloesen(d) : null;
        liste.Gesperrt = m =>
        {
            Log.Warnung(string.Create(CultureInfo.InvariantCulture,
                $"Sperrliste: {(m.Gruppe ? "Sammelsperre /48 " : "")}{m.Kennung} für {Dauertext(m.DauerMs)} gesperrt (Stufe {m.Stufe}, Anlass {Sperrliste.Drahtname(m.Anlass)}, {m.Punkte} Punkte)"));
            if (klartext is not null) KlartextSchreiben(klartext, Sperrliste.Klartextzeile(m, Uhr.GetUtcNow()));
        };
        if (klartext is not null)
        {
            Log.Info("Sperrliste: neue Sperren gehen zusätzlich mit Klartext-IP nach " + klartext + " (für fail2ban; das Logbuch bleibt ohne)");
            // Gleich anlegen, leer und ohne etwas abzuschneiden: fail2ban startet mit einem Jail, dessen Datei fehlt, gar nicht
            // (mit allen Jails), und eine fehlende Schreibberechtigung (Unit ohne LogsDirectory) zeigt sich so beim Start statt
            // erst bei der ersten Sperre (Befund vom 01.10.2026).
            KlartextSchreiben(klartext, null);
        }
    }

    /// <summary>
    /// Hängt eine Zeile an die Klartextdatei (unter Linux 0600); ohne Zeile legt es die Datei nur an. Scheitert das, bleibt die
    /// Sperre trotzdem.
    /// </summary>
    private void KlartextSchreiben(string pfad, string? zeile)
    {
        try
        {
            lock (KlartextSperre)
            {
                var optionen = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
                if (!OperatingSystem.IsWindows()) optionen.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var strom = new FileStream(pfad, optionen);
                if (zeile is not null) strom.Write(Encoding.UTF8.GetBytes(zeile + "\n"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warnung("Sperrliste: Klartextdatei nicht schreibbar (" + ex.GetType().Name + ")");
        }
    }

    private static string Dauertext(long ms) => ms >= 3_600_000
        ? string.Create(CultureInfo.InvariantCulture, $"{ms / 3_600_000.0:0.#} h")
        : string.Create(CultureInfo.InvariantCulture, $"{(ms + 59_999) / 60_000} min");

    /// <summary>Aus dem Takt der Zentrale: jede Minute aufräumen, höchstens alle 10 s speichern.</summary>
    private void SperrlisteTakt(long jetztMono, bool minute)
    {
        if (Sperrliste is null) return;
        if (minute) Sperrliste.Aufraeumen(Zeit.UnixMs());
        if (!Sperrliste.Geaendert || jetztMono - _letzteSperrlistenSpeicherung < 10_000) return;
        _letzteSperrlistenSpeicherung = jetztMono;
        Dateiablage.AtomarSchreiben(SperrlistenPfad, Sperrliste.AlsJson());
    }

    /// <summary>Beim Beenden.</summary>
    private void SperrlisteSpeichern()
    {
        if (Sperrliste is { Geaendert: true } s) Dateiablage.AtomarSchreiben(SperrlistenPfad, s.AlsJson());
    }

    /// <summary>Konsole: <c>sperrliste</c> zeigt die Sperren, <c>entsperren</c> hebt eine (oder alle) auf.</summary>
    private VerwaltungsAntwort SperrlisteKonsole(string befehl, JsonObject a)
    {
        var jetzt = Zeit.UnixMs();
        if (befehl == "sperrliste")
        {
            var liste = Sperrliste.Gesperrte(jetzt);
            var k = Konfiguration.Sperrliste;
            var daten = new JsonArray();
            var sb = new StringBuilder();
            if (liste.Count == 0)
                sb.Append(CultureInfo.InvariantCulture, $"Keine Sperren ({Sperrliste.Beobachtet} Adressen beobachtet).");
            else
                sb.Append(CultureInfo.InvariantCulture,
                    $"{liste.Count} Sperre(n), {Sperrliste.Beobachtet} Adressen beobachtet (Schwelle {k.Schwelle} Punkte in {k.FensterSekunden / 60} min):");
            foreach (var e in liste)
            {
                var bis = DateTimeOffset.FromUnixTimeMilliseconds(e.BisMs);
                sb.Append(CultureInfo.InvariantCulture,
                    $"\n{e.Kennung}  {(e.Gruppe ? "/48  " : "")}bis {bis:yyyy-MM-dd HH:mm} UTC (noch {Dauertext(e.BisMs - jetzt)})  Stufe {e.Stufe}  {Sperrliste.Drahtname(e.Anlass)}  abgewiesen {e.Abgewiesen}");
                daten.Add(new JsonObject
                {
                    ["kennung"] = e.Kennung,
                    ["gruppe"] = e.Gruppe,
                    ["stufe"] = e.Stufe,
                    ["bis"] = e.BisMs,
                    ["anlass"] = Sperrliste.Drahtname(e.Anlass),
                    ["abgewiesen"] = e.Abgewiesen,
                });
            }
            return VerwaltungsAntwort.Gut(sb.ToString(), daten);
        }

        // entsperren: genau eins von --eintrag, --ip, --alle.
        var eintrag = Text(a, "eintrag");
        var ipText = Text(a, "ip");
        var alle = a["alle"] is JsonValue av && av.TryGetValue<bool>(out var al) && al;
        if ((eintrag is null ? 0 : 1) + (ipText is null ? 0 : 1) + (alle ? 1 : 0) != 1)
            return VerwaltungsAntwort.Fehler("genau eins von --eintrag <kennung>, --ip <adresse> und --alle angeben. Nichts geändert.");
        if (alle)
        {
            var n = Sperrliste.AlleAufheben();
            Vermerken("SPERRE_AUFGEHOBEN", KonsoleDurch, null, string.Create(CultureInfo.InvariantCulture, $"Konsole: alle Sperren aufgehoben ({n})"));
            return VerwaltungsAntwort.Gut(string.Create(CultureInfo.InvariantCulture, $"Alle Sperren aufgehoben ({n})."));
        }
        string kennung;
        if (eintrag is not null)
        {
            kennung = eintrag.Trim();
            if (!Sperrliste.Aufheben(kennung))
                return VerwaltungsAntwort.Fehler("Keine Sperre mit der Kennung " + kennung + " (funk-admin sperrliste zeigt sie). Nichts geändert.");
        }
        else
        {
            if (!IPAddress.TryParse(ipText, out var ip))
                return VerwaltungsAntwort.Fehler("--ip: keine IP-Adresse. Nichts geändert.");
            var quelle = Adresse.Aus(ip, 0);
            kennung = Log.Schwaerzung.Praefix(quelle.Praefix);
            if (Sperrliste.AufhebenFuer(quelle) == 0)
                return VerwaltungsAntwort.Fehler("Für diese Adresse (" + kennung + ") gibt es keine Sperre. Nichts geändert.");
        }
        // Wie im Logbuch nur die Kennung, nie die Adresse.
        Vermerken("SPERRE_AUFGEHOBEN", KonsoleDurch, null, "Konsole: Sperre " + kennung + " aufgehoben");
        Log.Info("Admin-Werkzeug: Sperre " + kennung + " aufgehoben");
        return VerwaltungsAntwort.Gut("Sperre für " + kennung + " aufgehoben.");
    }
}
