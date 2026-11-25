// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace Funk.Client;

/// <summary>
/// Nur eine Instanz je Einstellungsdatei (Sarevoc, 30.09.2026: "Wichtig auch nur eine Instanz"). Der erste Start belegt einen
/// benannten Mutex der Sitzung, dessen Name aus dem SHA-256 des Einstellungspfads entsteht (<see cref="MutexName"/>), und
/// horcht auf einer benannten Pipe (<see cref="PipeName"/>). Ein zweiter Start mit derselben Datei, also derselbe Nutzer
/// im selben Betrieb, findet den Mutex, erlaubt dem ersten mit AllowSetForegroundWindow (Prozessnummer aus der Pipe), ein
/// Fenster nach vorn zu holen, schickt "zeigen" und wartet auf die Antwort. Der erste holt daraufhin sein Hauptfenster
/// zurück (wiederherstellen, falls minimiert, und nach vorn) und antwortet mit einem Byte, ob das geklappt hat. Das ist der
/// Wunsch dessen, der gerade doppelt geklickt hat, kein eigenmächtiges Nach-vorn-Holen; ohne zweiten Start holt
/// JG TacRelay nie ein Fenster nach vorn.
/// <para>
/// Nur nach "gezeigt" endet der zweite Start. Antwortet der erste "nicht gezeigt" (er beendet sich gerade, sein Fenster ist
/// schon zu, oder seine Oberfläche reagiert nicht binnen <see cref="OberflaechenFrist"/>) oder ist seine Pipe nicht
/// erreichbar, fragt der zweite erneut und belegt den Mutex selbst, sobald der erste ihn freigibt. So geht ein Start kurz
/// nach dem Schließen nicht verloren. Erst wenn innerhalb der Frist (<see cref="StandardFrist"/>) weder das eine noch das
/// andere geschieht, meldet er, dass die laufende Instanz nicht antwortet.
/// </para>
/// <para>
/// Der Probefunk hat seine eigene Einstellungsdatei (TACRELAY_EINSTELLUNGEN aus probefunk-starten.cmd) und damit einen
/// eigenen Namen: Er darf neben dem normalen Client laufen. Beide Betriebe teilen nichts, was sich gegenseitig verdirbt
/// (eigene Einstellungen, der Probefunk nur auf 127.0.0.1), und so lässt sich der Echo-Test machen, ohne die Verbindung zur Org
/// zu trennen. Laufen beide, reagieren allerdings beide auf die Sprechtaste; das ist der Preis. Ein zweiter Probefunk dagegen
/// bittet den ersten nach vorn; sein Testserver endet dann mit dem Exitcode 0 dieses Starts.
/// </para>
/// <para>
/// Dazu belegt jede laufende Instanz den festen Mutex <see cref="LaeuftMarke"/>. Das Setup (installer\TacRelay.iss,
/// AppMutex) prüft ihn beim Installieren und Deinstallieren und bittet dann, JG TacRelay zu schließen. Alles sind Namen der
/// Sitzung, keine Dateien und keine Registry; Windows gibt sie frei, wenn der Prozess endet, auch nach einem Absturz.
/// </para>
/// </summary>
internal sealed partial class Einzelinstanz : IDisposable
{
    /// <summary>Mutex jeder laufenden Instanz, für das Setup. Steht gleich als AppMutex in installer\TacRelay.iss.</summary>
    internal const string LaeuftMarke = @"Local\JG-TacRelay-Laeuft";

    private const string MutexVorsatz = @"Local\JG-TacRelay-Instanz-";

    /// <summary>
    /// Die Bitte "zeig dein Hauptfenster": vier feste Bytes. Die laufende Instanz antwortet mit einem Byte
    /// (<see cref="Gezeigt"/> oder <see cref="NichtGezeigt"/>).
    /// </summary>
    private static readonly byte[] ZeigenBefehl = "TRZ2"u8.ToArray();

    private const byte Gezeigt = 1;
    private const byte NichtGezeigt = 0;

    /// <summary>Wie lange die laufende Instanz auf ihre Oberfläche wartet, bevor sie "nicht gezeigt" antwortet.</summary>
    internal static readonly TimeSpan OberflaechenFrist = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Wie lange ein zweiter Start höchstens wartet, bis die laufende Instanz "gezeigt" antwortet oder ihren Namen freigibt.
    /// Länger als das Beenden in App.OnExit (bis zu 5 s für Funk und Klang), damit ein Start kurz nach dem Schließen nicht
    /// als "antwortet nicht" endet.
    /// </summary>
    internal static readonly TimeSpan StandardFrist = TimeSpan.FromSeconds(10);

    /// <summary>Ein einzelner Verbindungsversuch zur Pipe; dazwischen prüft der zweite Start, ob der Name frei geworden ist.</summary>
    private static readonly TimeSpan Verbindungsversuch = TimeSpan.FromMilliseconds(250);

    private readonly Mutex _mutex;
    private readonly Mutex? _marke;
    private readonly string _pipeName;
    private readonly Func<bool> _zeigen;
    private readonly CancellationTokenSource _abbruch = new();
    private readonly Task _horchen;
    private int _freigegeben;

    private Einzelinstanz(Mutex mutex, Mutex? marke, string pipeName, Func<bool> zeigen)
    {
        _mutex = mutex;
        _marke = marke;
        _pipeName = pipeName;
        _zeigen = zeigen;
        _horchen = Task.Run(() => HorchenAsync(_abbruch.Token));
    }

    /// <summary>Ergebnis einer Anmeldung: die eigene Instanz, oder null und ob die laufende ihr Fenster gezeigt hat.</summary>
    internal readonly record struct Anmeldung(Einzelinstanz? Instanz, bool Gemeldet);

    /// <summary>Antwort der laufenden Instanz auf eine Bitte.</summary>
    private enum Antwort
    {
        NichtErreicht,
        NichtGezeigt,
        Gezeigt,
    }

    /// <summary>
    /// Kurze Kennung einer Einstellungsdatei: die ersten 8 Byte des SHA-256 über den vollständigen Pfad in Großbuchstaben
    /// (Windows unterscheidet in Pfaden nicht nach Groß und klein), als 16 Hexzeichen.
    /// </summary>
    internal static string Kennung(string einstellungsPfad)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(einstellungsPfad);
        var voll = Path.GetFullPath(einstellungsPfad).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(voll)), 0, 8);
    }

    /// <summary>Mutex je Sitzung und Einstellungsdatei.</summary>
    internal static string MutexName(string einstellungsPfad) => MutexVorsatz + Kennung(einstellungsPfad);

    /// <summary>
    /// Pipe je Sitzung und Einstellungsdatei. Pipes haben keinen Namensraum je Sitzung; die Sitzungsnummer im Namen trennt sie,
    /// und <see cref="PipeOptions.CurrentUserOnly"/> lässt nur das eigene Konto an beide Enden.
    /// </summary>
    internal static string PipeName(string einstellungsPfad)
    {
        using var ich = Process.GetCurrentProcess();
        return "JG-TacRelay-Instanz-" + Kennung(einstellungsPfad) + "-" + ich.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Für App.OnStartup, ganz am Anfang: Wahr heißt weiterstarten (die Instanz liegt dann in <paramref name="freigeben"/>).
    /// Falsch heißt: Mit derselben Einstellungsdatei läuft schon JG TacRelay, es hat sein Hauptfenster gezeigt, und dieser
    /// Start endet. Antwortet es innerhalb von <see cref="StandardFrist"/> nicht (hängt es), sagt ein Meldungsfenster das,
    /// statt still nichts zu tun.
    /// </summary>
    internal static bool ProgrammStart(Application app, string einstellungsPfad, ICollection<IDisposable> freigeben)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(freigeben);
        var anmeldung = Anmelden(einstellungsPfad, () => AufOberflaecheZeigen(app));
        if (anmeldung.Instanz is { } instanz)
        {
            freigeben.Add(instanz);
            return true;
        }
        if (!anmeldung.Gemeldet)
        {
            MessageBox.Show("JG TacRelay läuft schon mit denselben Einstellungen, antwortet aber nicht.\n\n" +
                "Warte einen Moment und versuche es noch einmal. Hängt es weiter, beende JG TacRelay im Task-Manager und starte es neu.",
                "JG TacRelay", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        return false;
    }

    /// <summary>
    /// Meldet eine Instanz für <paramref name="einstellungsPfad"/> an. Ist der Name frei, ist sie die erste: Sie horcht ab
    /// jetzt und ruft <paramref name="zeigen"/> (auf einem Hintergrund-Thread), wenn ein zweiter Start darum bittet; dessen
    /// Rückgabe (wahr: Fenster gezeigt) geht als Antwort zurück. Sonst bittet dieser Aufruf die laufende darum, bis sie
    /// "gezeigt" antwortet (keine Instanz, gemeldet) oder ihren Namen freigibt (dann ist dieser Aufruf die erste), höchstens
    /// <paramref name="frist"/> lang (danach keine Instanz, nicht gemeldet). <paramref name="vordergrundErlauben"/> und
    /// <paramref name="marke"/> sind für Tests: Sie fassen weder die Vordergrundregeln von Windows noch den Mutex an, den das
    /// Setup prüft.
    /// </summary>
    internal static Anmeldung Anmelden(string einstellungsPfad, Func<bool> zeigen, bool vordergrundErlauben = true, string marke = LaeuftMarke,
        TimeSpan? frist = null)
    {
        ArgumentNullException.ThrowIfNull(zeigen);
        var name = MutexName(einstellungsPfad);
        var pipe = PipeName(einstellungsPfad);
        var gesamt = frist ?? StandardFrist;
        var uhr = Stopwatch.StartNew();
        while (true)
        {
            if (Belegen(name) is { } mutex)
            {
                Mutex? laeuft = null;
                try
                {
                    laeuft = new Mutex(false, marke);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
                {
                    // Ohne Marke warnt das Setup nicht; es prüft dann noch die Prozesse im Programmordner.
                }
                return new Anmeldung(new Einzelinstanz(mutex, laeuft, pipe, zeigen), false);
            }
            var rest = gesamt - uhr.Elapsed;
            if (rest <= TimeSpan.Zero) return new Anmeldung(null, false);
            // Die Antwort darf die Oberflächenfrist der laufenden Instanz abwarten, aber nicht über die eigene Frist hinaus.
            var warten = OberflaechenFrist + TimeSpan.FromSeconds(1);
            if (Melden(pipe, vordergrundErlauben, rest < Verbindungsversuch ? rest : Verbindungsversuch, rest < warten ? rest : warten) == Antwort.Gezeigt)
                return new Anmeldung(null, true);
            if (uhr.Elapsed >= gesamt) return new Anmeldung(null, false);
            Thread.Sleep(100);
        }
    }

    /// <summary>
    /// Belegt den Namen, wenn er frei ist, und gibt den Mutex zurück; sonst null. Nur das Dasein des Namens zählt, nicht der
    /// Besitz (Mutexe gehören einem Thread). So prüft auch Inno AppMutex.
    /// </summary>
    private static Mutex? Belegen(string name)
    {
        try
        {
            var mutex = new Mutex(false, name, out var neu);
            if (neu) return mutex;
            mutex.Dispose();
        }
        catch (UnauthorizedAccessException)
        {
            // Den Namen hält ein Prozess mit anderen Rechten (etwa als Administrator gestartet): Er läuft also.
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Der Name gehört einem anderen Objekttyp; frei ist er damit auch nicht.
        }
        return null;
    }

    /// <summary>Bittet die laufende Instanz einmal, sich zu zeigen, und liest ihre Antwort.</summary>
    private static Antwort Melden(string pipeName, bool vordergrundErlauben, TimeSpan verbinden, TimeSpan warten)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            // Connect wartet auch, solange die erste Instanz ihre Pipe gerade neu anlegt.
            pipe.Connect(Math.Max(1, (int)verbinden.TotalMilliseconds));
            // Dieser Start kam vom Nutzer und darf ein Fenster nach vorn holen; er gibt das an die laufende Instanz weiter.
            if (vordergrundErlauben && Nativ.PipeServerProzess(pipe.SafePipeHandle, out var prozess)) _ = Nativ.VordergrundErlauben(prozess);
            pipe.Write(ZeigenBefehl);
            pipe.Flush();
            var antwort = new byte[1];
            using var frist = new CancellationTokenSource(warten);
            // Asynchron gelesen, damit die Frist greift; die Pipe ist asynchron geöffnet und braucht keinen Oberflächen-Thread.
            var gelesen = pipe.ReadAsync(antwort, frist.Token).AsTask().GetAwaiter().GetResult();
            return gelesen == 1 && antwort[0] == Gezeigt ? Antwort.Gezeigt : Antwort.NichtGezeigt;
        }
        catch (OperationCanceledException)
        {
            return Antwort.NichtGezeigt;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return Antwort.NichtErreicht;
        }
    }

    private async Task HorchenAsync(CancellationToken abbruch)
    {
        var puffer = new byte[ZeigenBefehl.Length];
        while (!abbruch.IsCancellationRequested)
        {
            try
            {
                var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await using (server.ConfigureAwait(false))
                {
                    await server.WaitForConnectionAsync(abbruch).ConfigureAwait(false);
                    using var frist = CancellationTokenSource.CreateLinkedTokenSource(abbruch);
                    frist.CancelAfter(TimeSpan.FromSeconds(5));
                    var gelesen = await server.ReadAtLeastAsync(puffer, puffer.Length, throwOnEndOfStream: false, frist.Token).ConfigureAwait(false);
                    if (gelesen == puffer.Length && puffer.AsSpan().SequenceEqual(ZeigenBefehl))
                    {
                        var antwort = Ausloesen() ? Gezeigt : NichtGezeigt;
                        await server.WriteAsync(new[] { antwort }, frist.Token).ConfigureAwait(false);
                        // Erst schließen, wenn der zweite Start die Antwort gelesen und seine Seite zugemacht hat.
                        _ = await server.ReadAsync(puffer.AsMemory(0, 1), frist.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (abbruch.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // Ein Aufrufer hat verbunden und nichts geschickt oder nichts gelesen; weiter horchen.
            }
            catch (IOException)
            {
                // Aufrufer weg, oder der Name ist kurz belegt: nicht im Kreis drehen.
                await Pause(abbruch).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Pause(abbruch).ConfigureAwait(false);
            }
        }
    }

    private static async Task Pause(CancellationToken abbruch)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), abbruch).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Beenden kommt über die Schleife.
        }
    }

    private bool Ausloesen()
    {
        try
        {
            return _zeigen();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ein Fehler beim Zeigen darf das Horchen nicht beenden.
            Debug.WriteLine("Einzelinstanz: zeigen gescheitert: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Läuft auf dem Horch-Thread: bittet den Oberflächen-Thread, das Hauptfenster zu zeigen, und wartet höchstens
    /// <see cref="OberflaechenFrist"/>. Falsch, wenn es kein Hauptfenster (mehr) gibt, die Anwendung schon endet (dann
    /// verwirft der Dispatcher den Auftrag) oder die Oberfläche so lange nicht dazu kommt. Der Auftrag bleibt dann stehen:
    /// Kommt die Oberfläche später dazu, zeigt sie das Fenster doch noch.
    /// </summary>
    private static bool AufOberflaecheZeigen(Application app)
    {
        try
        {
            var auftrag = app.Dispatcher.InvokeAsync(() => FensterZeigen(app.MainWindow));
            return auftrag.Wait(OberflaechenFrist) == DispatcherOperationStatus.Completed && auftrag.Result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Holt das Hauptfenster zurück, nur auf Bitte eines zweiten Starts: sichtbar machen, aus dem Minimieren wiederherstellen
    /// (SW_RESTORE, damit ein vorher maximiertes Fenster maximiert zurückkommt) und aktivieren. Wahr, wenn das Fenster danach
    /// sichtbar ist; falsch ohne Fenster oder wenn es schon geschlossen ist.
    /// </summary>
    internal static bool FensterZeigen(Window? fenster)
    {
        if (fenster is null) return false;
        try
        {
            if (!fenster.IsVisible) fenster.Show();
            if (fenster.WindowState == WindowState.Minimized)
            {
                var hwnd = new WindowInteropHelper(fenster).Handle;
                if (hwnd != IntPtr.Zero) _ = Nativ.FensterZeigen(hwnd, Nativ.SwRestore);
                else fenster.WindowState = WindowState.Normal;
            }
            // Ohne Vordergrundrecht blinkt es nur in der Taskleiste; sichtbar und wiederhergestellt ist es trotzdem.
            _ = fenster.Activate();
            return fenster.IsVisible;
        }
        catch (InvalidOperationException)
        {
            // Schon geschlossen: Die Anwendung endet gerade.
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _freigegeben, 1) != 0) return;
        _abbruch.Cancel();
        try
        {
            _horchen.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Das Horchen endet mit dem Prozess ohnehin.
        }
        _abbruch.Dispose();
        _marke?.Dispose();
        _mutex.Dispose();
    }

    private static partial class Nativ
    {
        public const int SwRestore = 9;

        [LibraryImport("kernel32.dll", EntryPoint = "GetNamedPipeServerProcessId", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PipeServerProzess(SafePipeHandle pipe, out uint prozess);

        [LibraryImport("user32.dll", EntryPoint = "AllowSetForegroundWindow", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool VordergrundErlauben(uint prozess);

        [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool FensterZeigen(nint fenster, int befehl);
    }
}
