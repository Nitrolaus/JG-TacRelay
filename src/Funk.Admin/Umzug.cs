// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Security.Cryptography;
using Funk.Server.Betrieb;

namespace Funk.Admin;

/// <summary>
/// <c>funk-admin umzug-packen</c> und <c>umzug-einspielen</c> (EINRICHTEN-UMZUG-ENTWURF, "Umzugspaket"): Der alte Server packt
/// lage, konten, komfort, admins, mitglieder und journal, dazu den TOTP-Schlüssel (abwählbar mit <c>--ohne-totp</c>) und nur auf
/// ausdrücklichen Wunsch den Serverschlüssel (<c>--mit-serverschluessel</c>). Nie dabei: das Betriebsgeheimnis und die
/// Vorrangspur; der neue Server hat eigene. Beide Befehle laufen nur bei angehaltenem Dienst, damit der Stand fest ist und
/// nichts unter dem laufenden Server ausgetauscht wird. Einspielen legt Vorhandenes zur Seite (Endung
/// <c>.vor-umzug-JJJJMMTT-HHmmss</c>), schreibt mit 0600 und setzt unter Linux den Besitzer der Zustandsdateien.
/// </summary>
public sealed class Umzug(TextReader ein, TextWriter aus, Systemblick system)
{
    /// <summary>Endung, unter der Vorhandenes beim Einspielen zur Seite gelegt wird, gefolgt von der Zeit.</summary>
    public const string BeiseiteEndung = ".vor-umzug-";

    private readonly Dialog _d = new(ein, aus);

    private int Nichts(string grund)
    {
        aus.WriteLine(grund + " Nichts geändert.");
        return 1;
    }

    private bool DienstLaeuft(ServerKonfiguration k)
    {
        if (!system.DienstLaeuft(k.SocketPfad)) return false;
        aus.WriteLine("Der Dienst läuft noch (der Verwaltungssocket " + k.SocketPfad + " antwortet). Erst anhalten: systemctl stop funk-server. Nichts geändert.");
        return true;
    }

    public int Packen(ServerKonfiguration k, string? credentials, string ziel, bool ohneTotp, bool mitServerschluessel, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(k);
        ArgumentNullException.ThrowIfNull(ziel);
        if (DienstLaeuft(k)) return 1;
        var zielVoll = Path.GetFullPath(ziel);
        if (File.Exists(zielVoll)) throw new SchonDaException(zielVoll);
        var orte = new Schluesselorte(k, credentials);

        var dateien = new List<Umzugsdatei>();
        foreach (var name in Umzugspaket.Zustand)
        {
            var p = Path.Combine(orte.Zustand, name);
            if (File.Exists(p)) dateien.Add(new Umzugsdatei(name, File.ReadAllBytes(p)));
        }
        if (!dateien.Any(d => d.Name == "konten.json"))
            return Nichts("Im Zustandsverzeichnis " + orte.Zustand + " liegt keine konten.json: falsches Verzeichnis, oder der Server lief hier nie.");
        if (!ohneTotp)
        {
            if (!File.Exists(orte.Totp))
                return Nichts("Der TOTP-Schlüssel " + orte.Totp + " fehlt (--credentials <ordner>?). Ohne ihn packen: --ohne-totp; dann richtet jeder Admin den Zweitfaktor neu ein.");
            var totp = File.ReadAllBytes(orte.Totp);
            if (totp.Length != TotpSchluessel.Laenge) return Nichts("Der TOTP-Schlüssel " + orte.Totp + " hat nicht 32 Byte.");
            dateien.Add(new Umzugsdatei(Umzugspaket.TotpName, totp));
        }
        if (mitServerschluessel)
        {
            foreach (var (name, pfad) in new[] { (Umzugspaket.SchluesselName, orte.Schluessel), (Umzugspaket.ZertifikatName, orte.Zertifikat) })
            {
                if (!File.Exists(pfad)) return Nichts(pfad + " fehlt (--credentials <ordner>?).");
                dateien.Add(new Umzugsdatei(name, File.ReadAllBytes(pfad)));
            }
        }

        var passphrase = Umzugspaket.PassphraseErzeugen();
        var tar = Umzugspaket.Packen(dateien, jetzt, Fassung);
        try
        {
            var paket = Umzugspaket.Verschluesseln(tar, passphrase);
            Dateiablage.NeuSchreiben(zielVoll, paket, nurBesitzer: true);
            aus.WriteLine("Umzugspaket geschrieben: " + zielVoll + " (" + paket.Length.ToString(CultureInfo.InvariantCulture) + " Byte).");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tar);
            foreach (var d in dateien) CryptographicOperations.ZeroMemory(d.Inhalt);
        }
        aus.WriteLine("Darin: " + string.Join(", ", dateien.Select(d => d.Name)) + ".");
        // sperrliste.json (ab 0.3.0) trägt wie vorrangspur.json nur HMAC-Werte aus dem Betriebsgeheimnis; beim neuen Server passt keine.
        aus.WriteLine("Nicht darin: funk-geheimnis, vorrangspur.json und sperrliste.json (der neue Server hat eigene)"
            + (mitServerschluessel ? "." : ", der Serverschlüssel (nur mit --mit-serverschluessel)."));
        if (mitServerschluessel)
            aus.WriteLine("Der Serverschlüssel reist mit: Alte Kopien (Stick, Sicherungen, vzdump) gehören nach dem Umzug weg.");
        if (ohneTotp)
            aus.WriteLine("Ohne TOTP-Schlüssel: Auf dem neuen Server richtet jeder Admin den Zweitfaktor an der Konsole neu ein (admin-einrichtung-neu).");
        aus.WriteLine();
        aus.WriteLine("Passphrase, nur jetzt zu sehen. Nicht aufschreiben, nicht abfotografieren, nur am Telefon weitergeben:");
        aus.WriteLine();
        aus.WriteLine("    " + Umzugspaket.PassphraseAnzeigen(passphrase));
        aus.WriteLine();
        aus.WriteLine("Das Paket darf über jeden Weg gehen, die Passphrase nur am Telefon. Einspielen auf dem neuen Server:");
        aus.WriteLine("funk-admin umzug-einspielen --konfig /etc/funk/server.json --paket <datei>");
        return 0;
    }

    /// <summary>Spielt das Paket ein. <paramref name="weiterZeigen"/>: die Zeile "Weiter: systemctl start ..." (nicht aus einrichten, das startet selbst).</summary>
    public int Einspielen(ServerKonfiguration k, string? credentials, string paketPfad, bool mitServerschluessel, string besitzer, DateTimeOffset jetzt, bool weiterZeigen = true)
    {
        ArgumentNullException.ThrowIfNull(k);
        ArgumentNullException.ThrowIfNull(paketPfad);
        ArgumentNullException.ThrowIfNull(besitzer);
        if (DienstLaeuft(k)) return 1;
        var paketVoll = Path.GetFullPath(paketPfad);
        if (!File.Exists(paketVoll)) return Nichts("Das Paket " + paketVoll + " gibt es nicht.");
        if (new FileInfo(paketVoll).Length > Umzugspaket.MaxBytes) return Nichts("Das Paket ist größer als jedes Umzugspaket.");
        var paket = File.ReadAllBytes(paketVoll);
        if (Umzugspaket.KopfPruefen(paket) is { } kopf) return Nichts(kopf);

        byte[]? tar = null;
        for (var versuch = 0; versuch < 3 && tar is null;)
        {
            var eingabe = _d.Zeile("Passphrase (26 Zeichen, Bindestriche und Leerzeichen egal): ");
            if (eingabe is null) return Nichts("Abgebrochen.");
            if (Umzugspaket.PassphraseNormalisieren(eingabe) is not { } p)
            {
                aus.WriteLine("Die Passphrase hat 26 Zeichen aus A-Z und 2-7, in Vierergruppen. Bitte noch einmal.");
                continue;
            }
            versuch++;
            try
            {
                tar = Umzugspaket.Entschluesseln(paket, p);
            }
            catch (UmzugsFehler e)
            {
                aus.WriteLine(e.Message + (versuch < 3 ? " Bitte noch einmal." : ""));
            }
        }
        if (tar is null) return Nichts("Dreimal ohne Erfolg.");

        IReadOnlyList<Umzugsdatei> dateien;
        try
        {
            dateien = Umzugspaket.Auspacken(tar);
        }
        catch (UmzugsFehler e)
        {
            return Nichts("Das Paket ist beschädigt: " + e.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tar);
        }
        try
        {
            return Schreiben(k, credentials, dateien, mitServerschluessel, besitzer, jetzt, weiterZeigen);
        }
        finally
        {
            foreach (var d in dateien) CryptographicOperations.ZeroMemory(d.Inhalt);
        }
    }

    private int Schreiben(ServerKonfiguration k, string? credentials, IReadOnlyList<Umzugsdatei> dateien, bool mitServerschluessel, string besitzer, DateTimeOffset jetzt, bool weiterZeigen)
    {
        var namen = dateien.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        // Schlüssel und Zertifikat ersetzen die Identität des neuen Servers (neuer Pin); beides nur ausdrücklich und nur zusammen.
        var mitSchluessel = namen.Contains(Umzugspaket.SchluesselName);
        var mitZertifikat = namen.Contains(Umzugspaket.ZertifikatName);
        if ((mitSchluessel || mitZertifikat) && !mitServerschluessel)
            return Nichts("Das Paket enthält den Serverschlüssel oder das Zertifikat des alten Servers. Einspielen nur ausdrücklich mit --mit-serverschluessel.");
        if (mitSchluessel != mitZertifikat)
            return Nichts("Im Paket steht " + (mitSchluessel ? Umzugspaket.SchluesselName + " ohne " + Umzugspaket.ZertifikatName : Umzugspaket.ZertifikatName + " ohne " + Umzugspaket.SchluesselName)
                + "; beide gehören zusammen.");
        if (!namen.Contains("konten.json")) return Nichts("Im Paket fehlt konten.json.");
        var orte = new Schluesselorte(k, credentials);

        // Wohin jede Datei gehört; der Name aus dem Paket wählt nur unter festen Zielen, er wird nie selbst zum Pfad.
        string Ziel(string name) => name switch
        {
            Umzugspaket.TotpName => orte.Totp,
            Umzugspaket.SchluesselName => orte.Schluessel,
            Umzugspaket.ZertifikatName => orte.Zertifikat,
            _ => Path.Combine(orte.Zustand, Umzugspaket.Zustand.Single(z => z == name)),
        };
        if (dateien.Any(d => d.Name is Umzugspaket.TotpName or Umzugspaket.SchluesselName or Umzugspaket.ZertifikatName) && !Directory.Exists(orte.Ordner))
            return Nichts("Der Schlüsselordner " + orte.Ordner + " fehlt (--credentials <ordner>, sonst /etc/funk/credentials).");

        // Vorhandenes zur Seite: alle Zustandsdateien (auch die, die das Paket nicht hat, sonst mischten sich zwei Stände) und die
        // Schlüssel, die ersetzt werden.
        var zeit = jetzt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var beiseite = new List<string>();
        Dateiablage.VerzeichnisAnlegen(orte.Zustand);
        var weg = Umzugspaket.Zustand.Select(z => Path.Combine(orte.Zustand, z)).Concat(dateien.Where(d => !Umzugspaket.Zustand.Contains(d.Name)).Select(d => Ziel(d.Name)));
        foreach (var pfad in weg.Where(File.Exists))
        {
            var neu = pfad + BeiseiteEndung + zeit;
            if (File.Exists(neu)) return Nichts(neu + " gibt es schon; in einer Minute noch einmal.");
            File.Move(pfad, neu);
            beiseite.Add(Path.GetFileName(pfad));
        }

        foreach (var d in dateien)
        {
            var ziel = Ziel(d.Name);
            Dateiablage.NeuSchreiben(ziel, d.Inhalt, nurBesitzer: true);
            if (!File.ReadAllBytes(ziel).AsSpan().SequenceEqual(d.Inhalt)) throw new IOException(ziel + " wurde nicht vollständig geschrieben.");
        }
        var linux = system.IstLinux;
        if (linux)
        {
            system.BesitzerSetzen(orte.Zustand, besitzer);
            foreach (var d in dateien.Where(d => Umzugspaket.Zustand.Contains(d.Name))) system.BesitzerSetzen(Ziel(d.Name), besitzer);
        }

        var zustand = dateien.Where(d => Umzugspaket.Zustand.Contains(d.Name)).Select(d => d.Name).ToList();
        var schluessel = dateien.Where(d => !Umzugspaket.Zustand.Contains(d.Name)).Select(d => d.Name).ToList();
        aus.WriteLine("Eingespielt nach " + orte.Zustand + ": " + string.Join(", ", zustand) + ".");
        if (schluessel.Count > 0) aus.WriteLine("Eingespielt nach " + orte.Ordner + ": " + string.Join(", ", schluessel) + ".");
        if (beiseite.Count > 0) aus.WriteLine("Zur Seite gelegt (Endung " + BeiseiteEndung + zeit + "): " + string.Join(", ", beiseite) + ".");
        aus.WriteLine(linux
            ? "Rechte 0600; Besitzer " + besitzer + ":" + besitzer + " für die Zustandsdateien, die Schlüssel bleiben bei root."
            : "Rechte und Besitzer gibt es so nur unter Linux; hier nicht gesetzt.");
        if (!namen.Contains(Umzugspaket.TotpName))
            aus.WriteLine("Ohne TOTP-Schlüssel im Paket: Jeder Admin richtet den Zweitfaktor an der Konsole neu ein (admin-einrichtung-neu).");
        if (weiterZeigen) aus.WriteLine("Weiter: systemctl start funk-server, dann funk-admin konten und funk-admin admins.");
        return 0;
    }

    /// <summary>Die Fassung dieses Werkzeugs für das MANIFEST (und für die Ausgabe von <c>einrichten</c>).</summary>
    internal static string Fassung =>
        typeof(Umzug).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false) is [System.Reflection.AssemblyInformationalVersionAttribute a, ..]
            ? a.InformationalVersion.Split('+')[0]
            : "0.0.0";
}
