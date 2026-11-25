// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;

namespace Funk.Testclient;

/// <summary>Werte eines Empfängers in einem Szenario.</summary>
internal sealed record Empfaengerwert(int Index, long Erwartet, long Empfangen, double VerlustProzent, double LaufzeitP50Ms, double LaufzeitP99Ms,
    double JitterMs, double PaketeJeS, double KbitJeS, long Tunnel);

/// <summary>Ergebnis eines Szenarios (Loopback: misst Server und Clients, nicht die Leitung).</summary>
internal sealed record Szenarioergebnis(
    char Szenario,
    string Beschreibung,
    int Funker,
    int Sprecher,
    int Stroeme,
    double Sekunden,
    int OpusBytes,
    int Sendegruppen,
    long Gesendet,
    long Erwartet,
    long Empfangen,
    double VerlustProzent,
    double VerlustMaxEmpfaengerProzent,
    double LaufzeitP50Ms,
    double LaufzeitP95Ms,
    double LaufzeitP99Ms,
    double LaufzeitMaxMs,
    double JitterMittelMs,
    double JitterMaxMs,
    double PaketeJeSEmpfaengerMittel,
    double PaketeJeSEmpfaengerMax,
    double KbitJeSEmpfaengerMittel,
    double KbitJeSEmpfaengerMax,
    double AusgangPaketeJeS,
    double AusgangMbitNutzlast,
    double AusgangMbitLeitung,
    double EingangMbitLeitung,
    double CpuMittelProzent,
    double CpuMaxProzent,
    double ArbeitsspeicherMaxMb,
    double PrivatspeicherMaxMb,
    double RelaisP50MittelMs,
    double RelaisP99MaxMs,
    long RelaisVerworfen,
    long AusgangAltersverwurf,
    int LaststufeMax,
    double TaktVerspaetungP99Ms,
    long TunnelPakete,
    IReadOnlyList<Empfaengerwert> JeEmpfaenger)
{
    /// <summary>Kurzzeile für die Konsole.</summary>
    public string Zeile() => string.Create(CultureInfo.InvariantCulture,
        $"{Szenario}: {Stroeme} Ströme, gesendet {Gesendet}, erwartet {Erwartet}, empfangen {Empfangen}, Verlust {VerlustProzent:F3} % (max {VerlustMaxEmpfaengerProzent:F2} %), "
        + $"Laufzeit p50/p95/p99/max {LaufzeitP50Ms:F2}/{LaufzeitP95Ms:F2}/{LaufzeitP99Ms:F2}/{LaufzeitMaxMs:F1} ms, Jitter {JitterMittelMs:F2}/{JitterMaxMs:F2} ms, "
        + $"Ausgang {AusgangPaketeJeS:F0} Pakete/s {AusgangMbitLeitung:F2} Mbit/s Leitung, Server CPU {CpuMittelProzent:F0} % (max {CpuMaxProzent:F0} %), RAM {ArbeitsspeicherMaxMb:F0} MB, "
        + $"Relais p50 {RelaisP50MittelMs:F3} p99 {RelaisP99MaxMs:F2} ms, Laststufe {LaststufeMax}");

    /// <summary>Markdown-Tabelle über alle Szenarien, deutsch mit Komma.</summary>
    public static string Tabelle(IReadOnlyList<Szenarioergebnis> liste)
    {
        static string D(double w, int stellen) => w.ToString("N" + stellen.ToString(CultureInfo.InvariantCulture), CultureInfo.GetCultureInfo("de-DE"));
        var sb = new StringBuilder();
        sb.AppendLine("| Wert | " + string.Join(" | ", liste.Select(e => e.Szenario.ToString())) + " |");
        sb.AppendLine("| --- |" + string.Concat(liste.Select(_ => " ---: |")));
        void Zeile(string name, Func<Szenarioergebnis, string> wert) => sb.AppendLine("| " + name + " | " + string.Join(" | ", liste.Select(wert)) + " |");
        Zeile("Szenario", e => e.Beschreibung);
        Zeile("Funker / Sprecher / Ströme", e => $"{e.Funker} / {e.Sprecher} / {e.Stroeme}");
        Zeile("Dauer (s)", e => D(e.Sekunden, 1));
        Zeile("Opus-Nutzlast (Byte)", e => e.OpusBytes.ToString(CultureInfo.InvariantCulture));
        Zeile("Sendegruppen je 20 ms (1 = alle Sprecher im selben Takt)", e => e.Sendegruppen.ToString(CultureInfo.InvariantCulture));
        Zeile("Frames gesendet (hoch)", e => D(e.Gesendet, 0));
        Zeile("Pakete erwartet / empfangen (runter)", e => D(e.Erwartet, 0) + " / " + D(e.Empfangen, 0));
        Zeile("Verlust gesamt / schlechtester Empfänger", e => D(e.VerlustProzent, 3) + " % / " + D(e.VerlustMaxEmpfaengerProzent, 2) + " %");
        Zeile("Laufzeit Mund-Server-Ohr p50 / p95 / p99 (ms)", e => $"{D(e.LaufzeitP50Ms, 2)} / {D(e.LaufzeitP95Ms, 2)} / {D(e.LaufzeitP99Ms, 2)}");
        Zeile("Laufzeit max (ms)", e => D(e.LaufzeitMaxMs, 1));
        Zeile("Jitter RFC 3550 je Strom, Mittel / max (ms)", e => D(e.JitterMittelMs, 2) + " / " + D(e.JitterMaxMs, 2));
        Zeile("Pakete/s je Empfänger, Mittel / max", e => D(e.PaketeJeSEmpfaengerMittel, 0) + " / " + D(e.PaketeJeSEmpfaengerMax, 0));
        Zeile("kbit/s je Empfänger (UDP-Nutzlast), Mittel / max", e => D(e.KbitJeSEmpfaengerMittel, 0) + " / " + D(e.KbitJeSEmpfaengerMax, 0));
        Zeile("Server-Ausgang Pakete/s", e => D(e.AusgangPaketeJeS, 0));
        Zeile("Server-Ausgang Mbit/s (UDP-Nutzlast)", e => D(e.AusgangMbitNutzlast, 2));
        Zeile("**Server-Ausgang auf der Leitung Mbit/s** (+28 IPv4/UDP, +26 PPPoE/Ethernet; ohne Verwurf = Upload-Bedarf)", e => "**" + D(e.AusgangMbitLeitung, 2) + "**");
        Zeile("Anteil an 50 Mbit/s brutto / an 35 Mbit/s Pacer", e => D(e.AusgangMbitLeitung / 50 * 100, 0) + " % / " + D(e.AusgangMbitLeitung / 35 * 100, 0) + " %");
        Zeile("Download am Server (Sprecher hoch) Mbit/s", e => D(e.EingangMbitLeitung, 2));
        Zeile("Server-CPU Mittel / max (% eines Kerns)", e => D(e.CpuMittelProzent, 0) + " / " + D(e.CpuMaxProzent, 0));
        Zeile("Server-Arbeitsspeicher max (MB) / privat max (MB)", e => D(e.ArbeitsspeicherMaxMb, 0) + " / " + D(e.PrivatspeicherMaxMb, 0));
        Zeile("Relais-Verarbeitung p50 Mittel / p99 max (ms)", e => D(e.RelaisP50MittelMs, 3) + " / " + D(e.RelaisP99MaxMs, 2));
        Zeile("Relais verworfen / Pacer-Altersverwurf", e => D(e.RelaisVerworfen, 0) + " / " + D(e.AusgangAltersverwurf, 0));
        Zeile("Laststufe max (7.4)", e => e.LaststufeMax.ToString(CultureInfo.InvariantCulture));
        Zeile("Taktgeber-Verspätung p99 (ms)", e => D(e.TaktVerspaetungP99Ms, 2));
        return sb.ToString();
    }

    /// <summary>Quantil einer aufsteigend sortierten Liste.</summary>
    public static double Quantil(float[] sortiert, double q)
    {
        if (sortiert.Length == 0) return 0;
        var i = (int)Math.Ceiling(q * sortiert.Length) - 1;
        return sortiert[Math.Clamp(i, 0, sortiert.Length - 1)];
    }
}
