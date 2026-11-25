// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Funk.Client.Ablage;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Baut aus den Verbindungseinstellungen einen <see cref="FunkClient"/>: das signierte Serverprofil prüfen, das höchste gültige aus
/// Setup und vom Server übernommener Ablage (3.1, F1, <see cref="Profilquelle"/>, <see cref="Profilpruefung.Waehlen"/>, dieselben
/// Sätze wie in der Oberfläche; ein später übernommenes gibt <see cref="FunkAnbindung.ProfilNachziehen"/> dem Aufbau), Geräteschlüssel aus dem
/// Schlüsselspeicher von Windows holen oder anlegen (3.2, TPM zuerst), TLS mit Pin (4.1). Kennt der Server das Gerät nicht,
/// fragt der Client die Namen für den Antrag (A42, <paramref name="anmeldung"/> in <see cref="Bauen"/>).
/// UNGEPRÜFT gegen einen echten Server: Der persistierte Schlüssel landet im Schlüsselspeicher des Benutzers, das tun
/// die automatischen Tests nicht; sie geben einen flüchtigen Speicher hinein. Für Entwickler gibt es den Testzugang eines
/// funk-testserver auf Loopback.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Verbindungsfabrik
{
    /// <summary>Name des Geräteschlüssels im Schlüsselspeicher des Benutzers.</summary>
    public const string SchluesselName = "JG-TacRelay-Geraet";

    /// <param name="v">Gespeicherte Verbindungseinstellungen.</param>
    /// <param name="rueckfrage">Rückfrage "Mit Konto X verknüpfen?" (3.4).</param>
    /// <param name="klang">Empfänger der Sprache und Opus-Prüfung; ohne ihn wird nur nach dem TOC-Byte geprüft und nichts abgespielt.</param>
    /// <param name="testzugang">Testzugang nur für diesen Lauf (Kommandozeile), vor dem gespeicherten.</param>
    /// <param name="speicher">Schlüsselspeicher; ohne Angabe der von Windows (<see cref="CngSchluesselspeicher"/>). Tests geben einen flüchtigen.</param>
    /// <param name="profil">Serverprofil und Profilschlüssel; ohne Angabe die eingebauten (<see cref="Profilquelle.Eingebaut"/>). Tests geben eigene.</param>
    /// <param name="anmeldung">
    /// "Anmelden als ... - stimmt das?" (A42): liefert SC-Name und Discord-Name für den Antrag, wenn der Server das Gerät nicht
    /// kennt; <c>null</c> heißt jetzt keinen Antrag.
    /// </param>
    /// <param name="uebernahme">
    /// Übernahme neuerer Profile aus Welcome (F1); ohne Angabe eine über <paramref name="profil"/>. Der Testzugang übernimmt nichts.
    /// </param>
    public static FunkAnbindung Bauen(VerbindungsEinstellungen v, Func<VerknuepfungKonto, CancellationToken, Task<bool>>? rueckfrage,
        IKlangAnschluss? klang = null, string? testzugang = null, IGeraeteschluesselSpeicher? speicher = null, Profilquelle? profil = null,
        Func<CancellationToken, Task<Anmeldedaten?>>? anmeldung = null, Profiluebernahme? uebernahme = null)
    {
        ArgumentNullException.ThrowIfNull(v);
        var zugang = string.IsNullOrWhiteSpace(testzugang) ? v.Testzugang : testzugang;
        if (!string.IsNullOrWhiteSpace(zugang)) return Testzugang(zugang, rueckfrage, klang, anmeldung);

        // Das höchste gültige Profil aus Setup und Ablage (F1): ein vom Server übernommenes gilt hier, beim nächsten Verbinden.
        var quelle = profil ?? uebernahme?.Quelle ?? Profilquelle.Eingebaut;
        var befund = Profilpruefung.Waehlen(quelle, DateTimeOffset.UtcNow);
        if (befund.Profil is not { } serverprofil)
        {
            // Der Hinweis erscheint ganz, also mit der Nummer im Satz; der Pfad bleibt in der Einzelheit.
            throw new VorbereitungException(new Verbindungsmeldung(Fehlernummern.Anhaengen(befund.Fehler ?? "Das Serverprofil ließ sich nicht prüfen.", befund.Nummer),
                befund.Einzelheit));
        }
        var optionen = AufbauOptionen(v, serverprofil);

        speicher ??= new CngSchluesselspeicher(SchluesselName);
        Geraeteschluessel gs;
        try
        {
            gs = speicher.Laden() ?? speicher.Erzeugen();
        }
        catch (CryptographicException ex)
        {
            throw new VorbereitungException(Verbindungstexte.Geraeteschluessel(ex.Message), ex);
        }
        var art = gs.Art == Protokoll.Identitaet.SchluesselArt.Tpm ? Protokoll.Steuerung.SchluesselArt.Tpm : Protokoll.Steuerung.SchluesselArt.Software;
        Geraeteidentitaet identitaet;
        try
        {
            identitaet = Geraeteidentitaet.Aus(gs.Schluessel, art);
        }
        catch
        {
            gs.Dispose();
            throw;
        }
        try
        {
            var aufbau = new TlsAufbau(serverprofil, identitaet, optionen);
            var client = new FunkClient(new FunkClientOptionen
            {
                Aufbau = aufbau,
                Identitaet = identitaet,
                ClientName = "JG TacRelay",
                // Nie der Name des Windows-PCs, sondern aus der Gerätekennung (seit 0.6.3).
                Geraetename = VerbindungsEinstellungen.NameAusKennung(identitaet.Fingerabdruck.Anzeige),
                Rueckfrage = rueckfrage,
                AnmeldungFragen = anmeldung,
                Empfaenger = klang,
                OpusPruefer = klang ?? (IOpusPruefer)TocOpusPruefer.Instanz,
            });
            return new FunkAnbindung(client, identitaet, serverprofil.Name, identitaet.Fingerabdruck.Anzeige,
                art == Protokoll.Steuerung.SchluesselArt.Tpm ? "TPM" : "Software")
            {
                Profil = uebernahme ?? new Profiluebernahme(quelle),
                ProfilNachziehen = aufbau.ProfilSetzen,
                // Nur ein Profil, das den Server dieser Verbindung noch kennt (3.1, F1).
                ServerPin = () => aufbau.LetzterPin,
            };
        }
        catch
        {
            identitaet.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Optionen des TLS-Aufbaus aus dem geprüften Profil: Hosts und öffentlicher Port kommen aus dem Profil (Heimphase etwa
    /// <c>name/v4</c> und 8443). Die optionale Adresse ersetzt nur die Hosts (und mit :Port den Port); der Pin aus dem Profil
    /// gilt immer. Ist die Adresse ungültig, wirft sie eine <see cref="VorbereitungException"/>: der Satz für Funker ohne Adresse,
    /// Eingabe und Grund als Einzelheit.
    /// </summary>
    public static TlsAufbauOptionen AufbauOptionen(VerbindungsEinstellungen v, Serverprofil profil)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(profil);
        if (!Adressangabe.TryLesen(v.Adresse, out var adresse, out var fehler))
            throw new VorbereitungException(Verbindungstexte.EinstellungUngueltig("Adresse \"" + v.Adresse + "\": " + fehler));
        return new TlsAufbauOptionen { Host = adresse?.Host, Port = adresse?.Port };
    }

    /// <summary>Ob <paramref name="text"/> ein Testzugang ist: Loopback-Adresse mit Port, etwa 127.0.0.1:47000.</summary>
    public static bool TestzugangGueltig(string? text) =>
        IPEndPoint.TryParse(text ?? "", out var ziel) && ClientKern.Testzugang.IstLoopback(ziel.Address) && ziel.Port != 0;

    private static FunkAnbindung Testzugang(string zugang, Func<VerknuepfungKonto, CancellationToken, Task<bool>>? rueckfrage,
        IKlangAnschluss? klang, Func<CancellationToken, Task<Anmeldedaten?>>? anmeldung)
    {
        if (!TestzugangGueltig(zugang) || !IPEndPoint.TryParse(zugang, out var ziel))
            throw new InvalidOperationException("Testzugang braucht die Form 127.0.0.1:Port (nur Loopback).");
        var identitaet = Geraeteidentitaet.Fluechtig();
        try
        {
            var client = new FunkClient(new FunkClientOptionen
            {
                Aufbau = new TestzugangAufbau(ziel, identitaet),
                Identitaet = identitaet,
                ClientName = "JG TacRelay",
                Geraetename = VerbindungsEinstellungen.NameAusKennung(identitaet.Fingerabdruck.Anzeige),
                Rueckfrage = rueckfrage,
                AnmeldungFragen = anmeldung,
                Empfaenger = klang,
                OpusPruefer = klang ?? (IOpusPruefer)TocOpusPruefer.Instanz,
            });
            return new FunkAnbindung(client, identitaet, "Testzugang " + zugang, identitaet.Fingerabdruck.Anzeige, "flüchtig");
        }
        catch
        {
            identitaet.Dispose();
            throw;
        }
    }
}
