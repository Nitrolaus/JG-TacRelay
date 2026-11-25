// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Reflection;

namespace Funk.Tests;

/// <summary>Markiert eine statische Testmethode. Sie wirft bei Fehlschlag, sonst gilt sie als bestanden.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

/// <summary>Fehlschlag eines Tests mit lesbarer Meldung.</summary>
public sealed class PruefungException(string meldung) : Exception(meldung);

/// <summary>Kleine Prüfhilfen, damit kein Testframework nachgeladen werden muss.</summary>
public static class Pruefe
{
    public static void Wahr(bool bedingung, string was)
    {
        if (!bedingung) throw new PruefungException("Erwartet wahr: " + was);
    }

    public static void Falsch(bool bedingung, string was)
    {
        if (bedingung) throw new PruefungException("Erwartet falsch: " + was);
    }

    public static void Gleich<T>(T erwartet, T tatsaechlich, string was)
    {
        if (!EqualityComparer<T>.Default.Equals(erwartet, tatsaechlich))
            throw new PruefungException($"{was}: erwartet <{erwartet}>, war <{tatsaechlich}>");
    }

    public static void BytesGleich(ReadOnlySpan<byte> erwartet, ReadOnlySpan<byte> tatsaechlich, string was)
    {
        if (!erwartet.SequenceEqual(tatsaechlich))
            throw new PruefungException($"{was}: erwartet {Convert.ToHexString(erwartet)}, war {Convert.ToHexString(tatsaechlich)}");
    }

    public static TException Wirft<TException>(Action aktion, string was) where TException : Exception
    {
        try
        {
            aktion();
        }
        catch (TException e)
        {
            return e;
        }
        catch (Exception e)
        {
            throw new PruefungException($"{was}: erwartet {typeof(TException).Name}, bekam {e.GetType().Name}: {e.Message}");
        }
        throw new PruefungException($"{was}: erwartet {typeof(TException).Name}, nichts geworfen");
    }
}

/// <summary>Findet alle [Test]-Methoden per Reflection und führt sie aus. Optional Filter über Namensteil.</summary>
public static class Testlauf
{
    public static int Ausfuehren(string[] args)
    {
        // Nichts aus dem Testlauf darf die Einstellungen des Nutzers im Profil anlegen oder überschreiben; was den
        // Standardpfad des Clients erreicht, landet im Testverzeichnis.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TACRELAY_EINSTELLUNGEN")))
            Environment.SetEnvironmentVariable("TACRELAY_EINSTELLUNGEN", Path.Combine(Testserver.Wurzel(), "client-einstellungen.json"));
        // Ohne tools\dotnet-umgebung.ps1 hat die CLI vor dem Start Telemetrie gesendet und das Restore womöglich den
        // NuGet-Ordner im Profil genommen; verhindern lässt sich das von hier nicht mehr, nur sichtbar machen.
        if (Environment.GetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT") != "1")
            Console.WriteLine(@"HINWEIS ohne tools\dotnet-umgebung.ps1 gestartet (DOTNET_CLI_TELEMETRY_OPTOUT fehlt); Tests über tests\testlauf.ps1 aufrufen.");
        // Mehrere Namensteile: ein Test läuft, wenn einer davon passt (gezielte Läufe über mehrere Klassen).
        var filter = args.Where(a => !a.StartsWith('-')).ToArray();
        var tests = typeof(Testlauf).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
            .Where(m => filter.Length == 0 || filter.Any(f => $"{m.DeclaringType!.Name}.{m.Name}".Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.DeclaringType!.Name, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        int bestanden = 0, fehlgeschlagen = 0;
        var uhr = Stopwatch.StartNew();
        foreach (var m in tests)
        {
            var name = $"{m.DeclaringType!.Name}.{m.Name}";
            try
            {
                var ergebnis = m.Invoke(null, null);
                if (ergebnis is Task task) task.GetAwaiter().GetResult();
                bestanden++;
            }
            catch (Exception e)
            {
                var innen = e is TargetInvocationException { InnerException: { } i } ? i : e;
                fehlgeschlagen++;
                Console.WriteLine($"FEHLER {name}: {innen.GetType().Name}: {innen.Message}");
            }
        }
        Console.WriteLine(Summenzeile(bestanden, fehlgeschlagen, tests.Count, uhr.ElapsedMilliseconds));
        return fehlgeschlagen == 0 ? 0 : 1;
    }

    /// <summary>
    /// Die letzte Zeile jedes Laufs. tests\testlauf.ps1 sucht sie und schreibt sie in die Ergebnisdatei; wer das Format ändert,
    /// ändert dort das Muster mit (SicherungTests.Werkzeuge_TestlaufMeldetErgebnisUndCode prüft beides zusammen).
    /// </summary>
    internal static string Summenzeile(int bestanden, int fehlgeschlagen, int gesamt, long ms) =>
        $"{bestanden} bestanden, {fehlgeschlagen} fehlgeschlagen, {gesamt} gesamt, {ms} ms";
}
