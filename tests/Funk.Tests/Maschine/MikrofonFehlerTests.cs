// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Punkt 2 des Integrationsauftrags: Lässt sich das Mikrofon nicht öffnen (Datenschutz von Windows), läuft die Wiedergabe
/// trotzdem (A7: die Netze hören), der Grund steht im Gerätezustand und wird einmal gemeldet, und das Mikrofon lässt
/// sich später nachholen.
/// </summary>
internal static class MikrofonFehlerTests
{
    [Test]
    public static async Task Mikrofon_Gesperrt_WiedergabeLaeuft_UndWirdNachgeholt()
    {
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false);
        s.System.AufnahmeFehler = new UnauthorizedAccessException("Zugriff verweigert (0x80070005)");
        await s.StartenAsync();
        var z = s.Maschine.Geraete;
        Pruefe.Wahr(z.Gestartet && z.WiedergabeLaeuft && !z.AufnahmeLaeuft, "gestartet, Wiedergabe ja, Mikrofon nein: " + z);
        Pruefe.Wahr(z.AufnahmeFehler?.Contains("Datenschutz", StringComparison.Ordinal) == true, "Grund mit Hinweis auf den Datenschutz: " + z.AufnahmeFehler);
        Pruefe.Gleich(1, s.Fehler.Count, "einmal gemeldet");
        Pruefe.Wahr(s.Fehler[0].Message.StartsWith("Mikrofon:", StringComparison.Ordinal), "als Mikrofonfehler: " + s.Fehler[0].Message);

        // Die Netze sind zu hören: ein empfangener Spruch kommt im Ausgang an.
        s.Mitschreiben = true;
        var (paket, n) = EinPaket();
        for (uint f = 0; f < 20; f++)
        {
            _ = s.Maschine.SpracheEmpfangen(5, 0, 1, f, f == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine, paket.AsSpan(0, n), s.Uhr.Us);
            s.Stille(2);
        }
        s.Stille(20);
        PruefeWert.Mindestens(0.01, s.Ausgabe.Max(MathF.Abs), "Empfang hörbar trotz gesperrtem Mikrofon");

        s.Maschine.GeraeteWiederholen();
        Pruefe.Gleich(1, s.Fehler.Count, "gleicher Grund beim Wiederholen: nicht noch einmal gemeldet");
        s.System.AufnahmeFehler = null;
        s.Maschine.GeraeteWiederholen();
        z = s.Maschine.Geraete;
        Pruefe.Wahr(z.AufnahmeLaeuft && z.AufnahmeFehler is null, "nach dem Erlauben nachgeholt: " + z);
        s.Maschine.SprechtasteGedrueckt();
        s.Abspielen(AnbindungSignale.Sprache(48000 / 2, 4));
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(5);
        PruefeWert.Mindestens(20, s.Pakete.Count(p => p.Opus.Length > 0), "und es wird gesendet");
    }

    [Test]
    public static async Task Wiedergabe_Fehlt_MikrofonLaeuft()
    {
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false);
        s.System.Aus.Clear();
        await s.StartenAsync();
        var z = s.Maschine.Geraete;
        Pruefe.Wahr(z.AufnahmeLaeuft && !z.WiedergabeLaeuft, "Mikrofon ja, Wiedergabe nein");
        Pruefe.Gleich("Kein Wiedergabegerät gefunden.", z.WiedergabeFehler, "Grund");
    }

    [Test]
    public static async Task BeideGesperrt_StartenWirft()
    {
        var system = new AttrappenSystem
        {
            AufnahmeFehler = new InvalidOperationException("Mikrofon weg"),
            WiedergabeFehler = new InvalidOperationException("Lautsprecher weg"),
        };
        await using var m = new KlangMaschine(new KlangMaschinenOptionen { AudioSystem = system, EigeneThreads = false, MitApm = false, MitRnnoise = false });
        var e = await PruefeAsync(() => m.StartenAsync(null, null, CancellationToken.None));
        Pruefe.Wahr(e.Message.Contains("Mikrofon weg", StringComparison.Ordinal) && e.Message.Contains("Lautsprecher weg", StringComparison.Ordinal), "beide Gründe: " + e.Message);
        Pruefe.Falsch(m.Geraete.Gestartet, "nicht gestartet");
    }

    [Test]
    public static void Beschreiben_VerweigerterZugriff()
    {
        var com = new HResultFehler("x", unchecked((int)0x80070005));
        Pruefe.Wahr(KlangMaschine.Beschreiben(com, aufnahme: true).Contains("Datenschutz", StringComparison.Ordinal), "E_ACCESSDENIED beim Mikrofon");
        Pruefe.Wahr(KlangMaschine.Beschreiben(new UnauthorizedAccessException("y"), aufnahme: true).Contains("Datenschutz", StringComparison.Ordinal),
            "so meldet .NET E_ACCESSDENIED aus COM");
        Pruefe.Gleich("x", KlangMaschine.Beschreiben(com, aufnahme: false), "bei der Wiedergabe der Text selbst");
        Pruefe.Gleich("anders", KlangMaschine.Beschreiben(new InvalidOperationException("anders"), aufnahme: true), "anderer Fehler: Text");
    }

    private sealed class HResultFehler : Exception
    {
        public HResultFehler(string meldung, int hresult) : base(meldung) => HResult = hresult;
    }

    private static (byte[] Paket, int Laenge) EinPaket()
    {
        using var k = new Funk.Klang.Kern.OpusKodierer();
        var paket = new byte[400];
        var ton = new float[960];
        for (var i = 0; i < ton.Length; i++) ton[i] = 0.3f * MathF.Sin(2f * MathF.PI * 440f * i / 48000f);
        return (paket, k.Kodieren(ton, paket));
    }

    private static async Task<InvalidOperationException> PruefeAsync(Func<Task> aktion)
    {
        try
        {
            await aktion();
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
        throw new PruefungException("erwartet InvalidOperationException");
    }
}
