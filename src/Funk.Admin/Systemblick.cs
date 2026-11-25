// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Funk.Admin;

/// <summary>
/// Was funk-admin vom Betriebssystem wissen oder dort tun will, an einer Stelle: Rechte einer Datei, Uhrzeit, DNS, freie Ports,
/// ob der Dienst läuft, Besitzer und Rechte setzen, für <c>einrichten</c> auch Programme wie useradd und systemctl aufrufen. Im
/// Betrieb <see cref="Echt"/>; Tests unter Windows setzen eine Attrappe ein, die sich wie Linux verhält. Was sich nicht prüfen
/// lässt, liefert <c>null</c>; dann sagt <c>pruefen</c> "nicht prüfbar".
/// </summary>
public class Systemblick
{
    public static Systemblick Echt { get; } = new();

    public virtual bool IstLinux => OperatingSystem.IsLinux();

    /// <summary>Unix-Rechte einer Datei oder eines Ordners; <c>null</c> unter Windows oder wenn es ihn nicht gibt.</summary>
    public virtual UnixFileMode? Modus(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        if (OperatingSystem.IsWindows() || !Path.Exists(pfad)) return null;
        return File.GetUnixFileMode(pfad);
    }

    /// <summary>
    /// <c>timedatectl show -p NTPSynchronized --value</c>: "yes" oder "no"; <c>null</c>, wenn es das nicht gibt (Windows, Container
    /// ohne systemd-timedated) oder es nicht binnen 3 s antwortet.
    /// </summary>
    public virtual string? ZeitSynchron()
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            var start = new ProcessStartInfo("timedatectl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "show", "-p", "NTPSynchronized", "--value" }) start.ArgumentList.Add(a);
            using var p = Process.Start(start);
            if (p is null) return null;
            var aus = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(3000) || p.ExitCode != 0) return null;
            return aus.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>Namensauflösung über das Betriebssystem; wirft <see cref="SocketException"/>, wenn es den Namen nicht gibt.</summary>
    public virtual IPAddress[] Aufloesen(string host, AddressFamily familie) => Dns.GetHostAddresses(host, familie);

    /// <summary>Ob der Dienst läuft: Sein Verwaltungssocket nimmt eine Verbindung an.</summary>
    public virtual bool DienstLaeuft(string socketPfad)
    {
        ArgumentNullException.ThrowIfNull(socketPfad);
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var frist = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            s.ConnectAsync(new UnixDomainSocketEndPoint(socketPfad), frist.Token).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Versucht, den Port zu binden; <c>null</c> heißt frei, sonst der Grund.</summary>
    public virtual SocketError? PortPruefen(IPAddress adresse, int port, ProtocolType art)
    {
        ArgumentNullException.ThrowIfNull(adresse);
        try
        {
            using var s = new Socket(adresse.AddressFamily, art == ProtocolType.Tcp ? SocketType.Stream : SocketType.Dgram, art);
            if (OperatingSystem.IsWindows()) s.ExclusiveAddressUse = true;
            s.Bind(new IPEndPoint(adresse, port));
            if (art == ProtocolType.Tcp) s.Listen(1);
            return null;
        }
        catch (SocketException ex)
        {
            return ex.SocketErrorCode;
        }
    }

    /// <summary>Ob dieser Prozess mit Rechten von root läuft (<c>funk-admin einrichten</c> legt Benutzer und Units an).</summary>
    public virtual bool IstRoot => Environment.IsPrivilegedProcess;

    /// <summary>
    /// Ruft ein Programm auf (getent, useradd, systemctl, journalctl, ss, ip, ufw ...) und liefert Exitcode und Ausgabe, stdout
    /// und stderr zusammen. Die Eingabe ist leer, damit nichts auf eine Antwort wartet; die Sprache ist C.UTF-8, damit die
    /// Ausgabe sich lesen lässt wie in den Tests. Code -1: ließ sich nicht starten oder brauchte länger als 2 Minuten.
    /// </summary>
    public virtual Prozessergebnis Ausfuehren(string programm, params string[] argumente)
    {
        ArgumentNullException.ThrowIfNull(programm);
        ArgumentNullException.ThrowIfNull(argumente);
        try
        {
            var start = new ProcessStartInfo(programm)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in argumente) start.ArgumentList.Add(a);
            start.Environment["LC_ALL"] = "C.UTF-8";
            using var p = Process.Start(start);
            if (p is null) return new Prozessergebnis(-1, programm + " ließ sich nicht starten.");
            p.StandardInput.Close();
            var aus = p.StandardOutput.ReadToEndAsync();
            var fehler = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(120_000))
            {
                p.Kill(entireProcessTree: true);
                return new Prozessergebnis(-1, programm + " lief länger als 2 Minuten und wurde beendet.");
            }
            return new Prozessergebnis(p.ExitCode, aus.GetAwaiter().GetResult() + fehler.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new Prozessergebnis(-1, programm + " ließ sich nicht starten: " + ex.Message);
        }
    }

    /// <summary>Setzt die Unix-Rechte einer Datei oder eines Ordners; unter Windows ohne Wirkung.</summary>
    public virtual void ModusSetzen(string pfad, UnixFileMode modus)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(pfad, modus);
    }

    /// <summary>Wartet, etwa zwischen zwei Blicken ins Journal. Tests warten nicht.</summary>
    public virtual void Warten(TimeSpan dauer) => Thread.Sleep(dauer);

    /// <summary>
    /// Setzt Besitzer und Gruppe (<c>chown -h besitzer:besitzer</c>); nur unter Linux, sonst ohne Wirkung. .NET hat dafür keine
    /// Funktion, deshalb über das Programm chown aus coreutils. <c>-h</c>: Ist der Pfad ein Verweis, bekommt der Verweis selbst den
    /// Besitzer, nie die Datei, auf die er zeigt. root arbeitet hier in /var/lib/funk, das dem Dienstbenutzer gehört; ohne
    /// <c>-h</c> machte ein dort untergeschobener Verweis auf /etc/shadow diese Datei zum Eigentum von funk.
    /// </summary>
    public virtual void BesitzerSetzen(string pfad, string besitzer)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        ArgumentNullException.ThrowIfNull(besitzer);
        if (!IstLinux) return;
        var r = Ausfuehren("chown", "-h", besitzer + ":" + besitzer, "--", pfad);
        if (r.Code != 0) throw new IOException("chown -h " + besitzer + ":" + besitzer + " " + pfad + " scheiterte: " + r.Ausgabe.Trim());
    }
}

/// <summary>Was ein Programmaufruf ergab: Exitcode (-1: nicht gestartet oder abgebrochen) und Ausgabe.</summary>
public sealed record Prozessergebnis(int Code, string Ausgabe);
