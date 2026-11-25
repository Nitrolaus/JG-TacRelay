// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.IO;
using System.Text;
using Funk.Protokoll.Identitaet;
using Draht = Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// F1 (EINRICHTEN-UMZUG-ENTWURF, FUNKPROTOKOLL-v1.md 3.1): Nach <c>Welcome{FREIGEGEBEN}</c> prüft der Client
/// <c>Welcome.profil</c> gegen die eingebauten Profilschlüssel und die höchste bekannte <c>profilVersion</c>
/// (<see cref="ProfilAnnahme"/>). Ist es neuer, schreibt er es atomar als <c>serverprofil.json</c> neben seine Einstellungen
/// (<see cref="Profilquelle.Ablage"/>); danach nimmt <see cref="Profilpruefung.Waehlen"/> das höchste gültige aus Setup und
/// Ablage, und der Verbindungsaufbau bekommt es für den nächsten Versuch. Ältere, gleiche, unsignierte, fremd signierte
/// oder kaputte Profile verwirft er und schreibt das ins Protokoll daneben (<see cref="ProtokollDateiname"/>).
/// <para>
/// Was das schützt: Ein Server, auch einer mit gültigem Pin oder erbeutetem Serverschlüssel, kann nur reichen, was Sarevoc offline
/// signiert hat. Ein älteres echtes Profil (Downgrade auf alte Hosts oder Pins) fällt an der Versionsregel durch. Ein Profil ohne
/// den Pin des Servers, der es liefert, fällt ebenso durch: Es sperrte die Clients aus, die es übernehmen. Die Ablage
/// selbst wird bei jedem Lesen neu geprüft; wer sie löscht, setzt den Client nur auf das Setup-Profil zurück.
/// </para>
/// </summary>
public sealed class Profiluebernahme
{
    /// <summary>Protokoll neben der Ablage.</summary>
    public const string ProtokollDateiname = "serverprofil-protokoll.txt";

    /// <summary>So viele Zeilen behält das Protokoll.</summary>
    public const int ProtokollZeilen = 200;

    private readonly Func<DateTimeOffset> _jetzt;
    private readonly Lock _sperre = new();

    /// <param name="quelle">Setup-Profil, eingebaute Schlüssel und Ablage.</param>
    /// <param name="jetzt">Uhr für Ablauf und Protokoll (Tests).</param>
    public Profiluebernahme(Profilquelle quelle, Func<DateTimeOffset>? jetzt = null)
    {
        ArgumentNullException.ThrowIfNull(quelle);
        Quelle = quelle;
        _jetzt = jetzt ?? (() => DateTimeOffset.UtcNow);
    }

    public Profilquelle Quelle { get; }

    /// <summary>Pfad des Protokolls, oder <c>null</c> ohne Ablage.</summary>
    public string? ProtokollPfad => Quelle.Ablage is { } a ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(a)) ?? ".", ProtokollDateiname) : null;

    /// <summary>Ein Profil wurde übernommen (aus dem Thread des Clients; die Oberfläche reiht sich selbst ein).</summary>
    public event Action<Uebernahmeergebnis>? Uebernommen;

    /// <summary>
    /// Prüft das Profil aus <c>Welcome.profil</c> und speichert es, wenn es neuer ist. Wirft nie; jedes Ergebnis außer der
    /// Übernahme heißt: nichts geändert. Ohne Pin des liefernden Servers; im Betrieb gilt die Überladung mit Pin.
    /// </summary>
    public Uebernahmeergebnis Annehmen(Draht.SigniertesProfil? angebot) => Annehmen(angebot, mitPin: false, null);

    /// <summary>
    /// Wie <see cref="Annehmen(Draht.SigniertesProfil?)"/>, dazu: Das Profil muss den Pin <paramref name="serverPin"/> des Servers
    /// enthalten, der es gerade geliefert hat (<see cref="Funk.ClientKern.TlsAufbau.LetzterPin"/>); <c>null</c> heißt unbekannt
    /// und verwirft ebenso. Sonst wäre derselbe Server nach der Übernahme nicht mehr erreichbar: Legt ein Betreiber aus Versehen
    /// ein echt signiertes Profil ohne den Pin seines Servers auf (etwa das Abschlussprofil eines Umzugs auf dem alten Server),
    /// übernähme es jeder Client, der sich dort verbindet, und fände danach keinen Server mehr, bis ein neues Setup kommt.
    /// </summary>
    public Uebernahmeergebnis Annehmen(Draht.SigniertesProfil? angebot, Fingerabdruck? serverPin) => Annehmen(angebot, mitPin: true, serverPin);

    private Uebernahmeergebnis Annehmen(Draht.SigniertesProfil? angebot, bool mitPin, Fingerabdruck? serverPin)
    {
        Uebernahmeergebnis e;
        lock (_sperre)
        {
            e = Pruefen(angebot, mitPin, serverPin);
            Protokollieren(e);
        }
        if (e.Urteil == Uebernahmeurteil.Uebernommen) Uebernommen?.Invoke(e);
        return e;
    }

    private Uebernahmeergebnis Pruefen(Draht.SigniertesProfil? angebot, bool mitPin, Fingerabdruck? serverPin)
    {
        if (Quelle.Ablage is not { } ablage) return new(Uebernahmeurteil.OhneAblage, null, null, "keine Ablage vorgesehen");
        if (!Lesen(angebot, out var signiert))
            return new(Uebernahmeurteil.Unlesbar, null, null, "VERWORFEN unlesbar: kein gültiges {daten, signatur} in Welcome.profil");

        var jetzt = _jetzt();
        var schluessel = new List<Profilschluessel>();
        try
        {
            foreach (var e in Quelle.Schluessel.Select(s => ProfilschluesselText.Pruefen(s)).Where(e => e.Gueltig).DistinctBy(e => e.Spki))
                schluessel.Add(Profilschluessel.AusSpki(Convert.FromBase64String(e.Spki!)));
            if (schluessel.Count == 0)
                return new(Uebernahmeurteil.OhneSchluessel, null, null, "VERWORFEN ohne Schlüssel: diese Fassung kennt keinen Profilschlüssel");

            var bekannt = Profilpruefung.Waehlen(Quelle, jetzt).Profil?.ProfilVersion;
            var p = ProfilAnnahme.Pruefen(signiert, schluessel, bekannt, jetzt);
            var bisher = bekannt is { } b ? b.ToString(CultureInfo.InvariantCulture) : "keine";
            switch (p.Urteil)
            {
                case ProfilUrteil.SignaturUngueltig:
                    return new(Uebernahmeurteil.SignaturUngueltig, null, bekannt, "VERWORFEN Signatur ungültig: mit keinem eingebauten Profilschlüssel signiert");
                case ProfilUrteil.InhaltUngueltig:
                    return new(Uebernahmeurteil.InhaltUngueltig, null, bekannt, "VERWORFEN Inhalt ungültig: richtig signiert, aber gegen 3.1");
                case ProfilUrteil.NichtNeuer:
                    return new(Uebernahmeurteil.NichtNeuer, p.Profil, bekannt,
                        "VERWORFEN nicht neuer: Version " + p.Profil!.ProfilVersion.ToString(CultureInfo.InvariantCulture) + ", bekannt " + bisher);
            }
            var neu = p.Profil!;
            if (mitPin && (serverPin is not { } sp || !neu.PinPasst(sp)))
            {
                return new(Uebernahmeurteil.OhneServerPin, neu, bekannt, "VERWORFEN ohne Pin dieses Servers: Version " + neu.ProfilVersion.ToString(CultureInfo.InvariantCulture)
                    + (serverPin is { } s ? " nennt den Pin " + s.Anzeige + " des liefernden Servers nicht" : ", der Pin des liefernden Servers ist unbekannt")
                    + "; danach wäre er nicht mehr erreichbar");
            }
            try
            {
                Schreiben(ablage, signiert.AlsJson());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return new(Uebernahmeurteil.Schreibfehler, neu, bekannt, "NICHT GESPEICHERT Version " + neu.ProfilVersion.ToString(CultureInfo.InvariantCulture) + ": " + ex.Message);
            }
            return new(Uebernahmeurteil.Uebernommen, neu, bekannt,
                "ÜBERNOMMEN Version " + neu.ProfilVersion.ToString(CultureInfo.InvariantCulture) + " (bisher " + bisher + "), Hosts " + Profilpruefung.Hosts(neu)
                + ", TCP-Port " + neu.TcpPort.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            foreach (var s in schluessel) s.Dispose();
        }
    }

    /// <summary>Base64 streng (kanonisch, wie es der Server durchreicht), Signatur 64 Byte.</summary>
    private static bool Lesen(Draht.SigniertesProfil? angebot, out SigniertesProfil signiert)
    {
        signiert = null!;
        if (angebot is null || !Base64(angebot.Daten, Serverprofil.MaxDatenBytes, out var daten) || !Base64(angebot.Signatur, SigniertesProfil.SignaturLaenge, out var signatur)
            || signatur.Length != SigniertesProfil.SignaturLaenge || daten.Length == 0)
            return false;
        signiert = new SigniertesProfil(daten, signatur);
        return true;
    }

    private static bool Base64(string? text, int maxBytes, out byte[] wert)
    {
        wert = [];
        if (string.IsNullOrEmpty(text) || text.Length > (maxBytes + 2) / 3 * 4) return false;
        var puffer = new byte[text.Length / 4 * 3 + 3];
        if (!Convert.TryFromBase64String(text, puffer, out var n) || n > maxBytes) return false;
        wert = puffer[..n];
        return string.Equals(Convert.ToBase64String(wert), text, StringComparison.Ordinal);
    }

    /// <summary>Atomar: erst eine Nachbardatei, auf die Platte, dann ersetzen.</summary>
    private static void Schreiben(string pfad, byte[] inhalt)
    {
        var voll = Path.GetFullPath(pfad);
        Directory.CreateDirectory(Path.GetDirectoryName(voll)!);
        var neu = voll + ".neu";
        using (var strom = new FileStream(neu, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            strom.Write(inhalt);
            strom.Flush(flushToDisk: true);
        }
        File.Move(neu, voll, overwrite: true);
    }

    /// <summary>
    /// Eine Zeile je Ergebnis, mit Zeit in UTC; dieselbe Meldung wie die letzte nicht noch einmal (sonst stünde bei jedem
    /// Verbinden "nicht neuer" da). Höchstens <see cref="ProtokollZeilen"/> Zeilen. Ein Schreibfehler stört die Übernahme nicht.
    /// </summary>
    private void Protokollieren(Uebernahmeergebnis e)
    {
        if (e.Urteil == Uebernahmeurteil.OhneAblage || ProtokollPfad is not { } pfad) return;
        try
        {
            var zeilen = File.Exists(pfad) ? File.ReadAllLines(pfad, Encoding.UTF8).ToList() : [];
            if (zeilen.Count > 0 && Meldung(zeilen[^1]) == e.Text) return;
            zeilen.Add(_jetzt().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z " + e.Text);
            if (zeilen.Count > ProtokollZeilen) zeilen.RemoveRange(0, zeilen.Count - ProtokollZeilen);
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.WriteAllLines(pfad, zeilen, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Das Protokoll ist Beiwerk; die Entscheidung über das Profil gilt trotzdem.
        }
    }

    /// <summary>Der Text einer Protokollzeile ohne die Zeit davor ("2026-10-01 12:00:00Z ").</summary>
    private static string Meldung(string zeile) => zeile.Length > 21 ? zeile[21..] : zeile;
}

/// <summary>Was mit einem Profil aus <c>Welcome.profil</c> geschah.</summary>
public enum Uebernahmeurteil
{
    /// <summary>Signiert, gültig, neuer als alles Bekannte: als <c>serverprofil.json</c> gespeichert.</summary>
    Uebernommen,

    /// <summary>Gleiche oder kleinere Version als das höchste bekannte gültige Profil.</summary>
    NichtNeuer,

    /// <summary>Keine Signatur eines eingebauten Profilschlüssels gilt.</summary>
    SignaturUngueltig,

    /// <summary>Signatur gilt, Inhalt verletzt 3.1.</summary>
    InhaltUngueltig,

    /// <summary>Echt und neuer, aber der Pin des Servers, der es geliefert hat, steht nicht darin (oder ist unbekannt).</summary>
    OhneServerPin,

    /// <summary>Kein lesbares <c>{daten, signatur}</c> (Base64, Länge).</summary>
    Unlesbar,

    /// <summary>Diese Fassung kennt keinen Profilschlüssel; nichts lässt sich prüfen.</summary>
    OhneSchluessel,

    /// <summary>Die Ablage ließ sich nicht schreiben.</summary>
    Schreibfehler,

    /// <summary>Keine Ablage vorgesehen (Testzugang, Vorführung).</summary>
    OhneAblage,
}

/// <summary>Ergebnis von <see cref="Profiluebernahme.Annehmen"/>.</summary>
public sealed record Uebernahmeergebnis(Uebernahmeurteil Urteil, Serverprofil? Profil, uint? BekannteVersion, string Text);
