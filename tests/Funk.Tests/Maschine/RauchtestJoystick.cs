// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Funk.Klang.Eingabe;

namespace Funk.Tests.Maschine;

/// <summary>
/// Rauchtest der Joystick-Sprechtaste mit echten Spielgeräten, nur von Hand: <c>FUNK_RAUCHTEST=1</c>. Zählt die Geräte
/// über DirectInput auf (nicht-exklusiv, Hintergrund, nur lesen), liest drei Sekunden und prüft, dass nichts scheitert
/// und die Raw-Input-Anmeldung der Tastatur-Sprechtaste bleibt. Der eigene Code schreibt nichts in die Registry, DirectInput
/// selbst pflegt beim Aufzählen und Öffnen aber Werte unter HKCU (Kalibrier- und Instanzdaten je Gerät); die Freigabe
/// deckt das mit ab. Läuft ein Spiel oder eine Vollbildanwendung, wird nichts geöffnet, und startet eins während des
/// Tests, gibt er sofort frei (<see cref="Spielschutz"/>).
/// </summary>
internal static partial class RauchtestJoystick
{
    private const string Was = "Rauchtest Joystick (echte Spielgeräte; DirectInput pflegt dabei Werte unter HKCU)";

    [Test]
    public static async Task Rauchtest_DirectInput_DreiSekundenLesen()
    {
        if (!Spielschutz.RauchtestFreigegeben(Was)) return;

        using var tastatur = new RawInputSprechtaste();
        var vorher = RawInputAnmeldungen();
        var uhr = Stopwatch.StartNew();
        using var joystick = new DirectInputSprechtaste();
        var startMs = uhr.ElapsedMilliseconds;
        var fehler = new List<Exception>();
        joystick.Fehler += (_, e) =>
        {
            lock (fehler) fehler.Add(e);
        };
        if (!await Spielschutz.WartenAsync(() => joystick.Statistik.Aufzaehlungen > 0, TimeSpan.FromMilliseconds(5000), Was)) return;
        var aufgezaehltMs = uhr.ElapsedMilliseconds;
        var geraete = joystick.Geraete;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  Rauchtest Joystick: Start {startMs} ms, erstes Aufzählen fertig nach {aufgezaehltMs} ms (Suche {joystick.Statistik.LetzteSucheMikros / 1000.0:F1} ms im Suchthread), {geraete.Count} Spielgeräte"));
        foreach (var g in geraete)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"    {g.Kennung.Name}: {g.Knoepfe} Knöpfe, {g.Huete} Hüte, {g.Kennung.Vid:X4}:{g.Kennung.Pid:X4}{(g.MussAbfragen ? ", abgefragt" : "")}{(g.Kennung.IstVirtuell ? ", virtuell" : "")}, {Kuerzen(g.Kennung.Pfad)}"));
        }
        if (geraete.Count > 0)
        {
            // Nur um das Wiederfinden am echten Gerät zu üben; es sendet nichts, und ein gehaltener Knopf zählt nicht.
            var probebelegung = Joystickeingabe.Knopf(1, geraete[0].Kennung);
            joystick.Belegung = probebelegung;
            Console.WriteLine($"  Rauchtest Joystick: Probebelegung \"{DirectInputSprechtaste.Anzeigename(probebelegung)}\", angeschlossen: {joystick.IstAngeschlossen(probebelegung)}");
        }
        var davor = joystick.Statistik;
        if (!await Spielschutz.WartenAsync(() => false, TimeSpan.FromSeconds(3), Was)) return;
        var st = joystick.Statistik;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  Rauchtest Joystick: in 3 s {st.Ereignisse - davor.Ereignisse} Ereignisse, {st.Wechsel - davor.Wechsel} Knopf-/Hutwechsel, gedrückt {joystick.IstGedrueckt}; " +
            $"insgesamt {st.Ereignisse} Ereignisse, {st.Wechsel} Wechsel (die ersten sind die Platzhalter nach dem Öffnen), allokiert {st.AllokiertImTakt} B"));

        // Der heiße Pfad über COM am echten Gerät: Puffer und Stand lesen wie nach einem Ereignis, erst warm, dann gemessen.
        joystick.Probelesen(50);
        if (!await Spielschutz.WartenAsync(() => joystick.Statistik.Ereignisse >= st.Ereignisse + (50L * geraete.Count) || uhr.ElapsedMilliseconds >= 20000,
            TimeSpan.FromSeconds(20), Was)) return;
        var warm = joystick.Statistik;
        var probeUhr = Stopwatch.StartNew();
        joystick.Probelesen(1000);
        if (!await Spielschutz.WartenAsync(() => joystick.Statistik.Ereignisse >= warm.Ereignisse + (1000L * geraete.Count) || uhr.ElapsedMilliseconds >= 30000,
            TimeSpan.FromSeconds(30), Was)) return;
        var probe = joystick.Statistik;
        var lesungen = probe.Ereignisse - warm.Ereignisse;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  Rauchtest Joystick: Probelesen {lesungen} Mal (Puffer und Stand), {probeUhr.Elapsed.TotalMicroseconds / Math.Max(1, lesungen):F1} µs je Lesen, " +
            $"allokiert {probe.AllokiertImTakt - warm.AllokiertImTakt} B (im Aufwärmen {warm.AllokiertImTakt - st.AllokiertImTakt} B)"));
        st = probe;
        var nachher = RawInputAnmeldungen();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  Rauchtest Joystick: Aufzählungen {st.Aufzaehlungen}, Öffnungsfehler {st.Oeffnungsfehler}, verloren {st.Verloren}, Fehler {st.Fehler}"));
        if (st.LetzterFehler is not null) Console.WriteLine($"  Rauchtest Joystick: letzte Meldung: {st.LetzterFehler}");
        foreach (var e in fehler) Console.WriteLine($"  Rauchtest Joystick Fehler: {e.GetType().Name}: {e.Message}");
        Console.WriteLine($"  Rauchtest Joystick: Raw Input vorher {Beschreiben(vorher)}; nachher {Beschreiben(nachher)}");

        Pruefe.Gleich(0L, st.Fehler, "keine Fehler im Lesethread");
        Pruefe.Gleich(0L, st.Oeffnungsfehler, "jedes aufgezählte Gerät ließ sich nicht-exklusiv öffnen");
        Pruefe.Gleich(0L, st.Verloren, "kein Gerät verloren");
        Pruefe.Wahr(geraete.Count == 0 || lesungen == 1000L * geraete.Count, "alle Probelesungen gelaufen");
        Pruefe.Gleich(warm.AllokiertImTakt, probe.AllokiertImTakt, "keine Allokation im Takt (nach dem Aufwärmen)");
        var tastaturVorher = vorher.Find(a => a.Seite == 1 && a.Nutzung == 6);
        var tastaturNachher = nachher.Find(a => a.Seite == 1 && a.Nutzung == 6);
        Pruefe.Wahr(tastaturVorher.Ziel != 0, "Raw Input: Tastatur angemeldet");
        Pruefe.Gleich(tastaturVorher, tastaturNachher, "DirectInput nahm der Tastatur-Sprechtaste die Anmeldung nicht weg");
        Pruefe.Falsch(nachher.Exists(a => a.Seite == 1 && a.Nutzung == 2 && a.Ziel != tastaturVorher.Ziel), "keine fremde Mausanmeldung");
    }

    private static string Kuerzen(string pfad) => pfad.Length <= 44 ? pfad : pfad[..40] + "...";

    private static string Beschreiben(List<Anmeldung> liste) =>
        liste.Count == 0 ? "keine" : string.Join(", ", liste.Select(a => string.Create(CultureInfo.InvariantCulture, $"{a.Seite:X2}/{a.Nutzung:X2} Flags 0x{a.Flags:X}")));

    [StructLayout(LayoutKind.Sequential)]
    private struct Anmeldung
    {
        public ushort Seite;
        public ushort Nutzung;
        public uint Flags;
        public nint Ziel;
    }

    private static unsafe List<Anmeldung> RawInputAnmeldungen()
    {
        uint anzahl = 0;
        _ = GetRegisteredRawInputDevices(null, ref anzahl, (uint)sizeof(Anmeldung));
        var puffer = new Anmeldung[Math.Max(anzahl, 1)];
        fixed (Anmeldung* p = puffer)
        {
            var n = GetRegisteredRawInputDevices(p, ref anzahl, (uint)sizeof(Anmeldung));
            return n == uint.MaxValue ? [] : [.. puffer.AsSpan(0, (int)n).ToArray()];
        }
    }

    [LibraryImport("user32.dll")]
    private static unsafe partial uint GetRegisteredRawInputDevices(Anmeldung* geraete, ref uint anzahl, uint groesse);
}
