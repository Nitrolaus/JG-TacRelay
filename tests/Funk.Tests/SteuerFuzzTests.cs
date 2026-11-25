// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Globalization;
using System.Text;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.SteuerHilfe;

namespace Funk.Tests;

/// <summary>
/// Kurzform von P-42 für den Steuerkanal: Mutationen gültiger Rahmen und Bodies durch Rahmenparser, JSON-Vorlauf,
/// Body-Prüfung und beide Kanalmodelle, ohne Ausnahme. Standard 200.000 Fälle; die Umgebungsvariable
/// <c>FUNK_FUZZ_FAELLE</c> setzt die Zahl (P-42 verlangt 10^7 je Parser).
/// </summary>
internal static class SteuerFuzzTests
{
    private static int Faelle =>
        int.TryParse(Environment.GetEnvironmentVariable("FUNK_FUZZ_FAELLE"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 200_000;

    private static readonly byte[][] Vorlagen =
    [
        Convert.FromHexString("0000001001000004000000077b226e72223a317d"),
        Convert.FromHexString("0000002b01010006000000087b22636f6465223a224b45494e5f5245434854222c2262657a7567547970223a35317d"),
        Convert.FromHexString("0000006201008002800000010000000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1fa0a1a2a3a4a5a6a7a8a9aaab202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3fb0b1b2b3b4b5b6b7b8b9babb"),
        Json(Nachrichtentyp.Hello, new Hello { Minor = 0, Client = new ClientAngabe { Name = "Funk ü", Version = "1.0.0-rc.1" }, SchluesselArt = SchluesselArt.Tpm, Opus = "1.6.1", Faehigkeiten = ["tunnel"] }),
        Json(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = "Jürgens PC" }, nr: 1),
        Json(Nachrichtentyp.Welcome, WelcomeFreigegeben()),
        Json(Nachrichtentyp.Verschieben, new VerschiebenAnfrage { BasisVersion = 5, Aenderungen = [new Verschiebung { KontoId = 1002, FelderPatch = [new FeldPatch { NetzId = 7 }], SendeFelder = [0, 1] }] }, nr: 2),
        Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, TvA),
    ];

    [Test]
    public static void P42_Kurz_RahmenJsonUndKanaele()
    {
        var zufall = new Random(4242);
        var faelle = Faelle;
        var puffer = new byte[8192];
        for (var i = 0; i < faelle; i++)
        {
            var vorlage = Vorlagen[zufall.Next(Vorlagen.Length)];
            var laenge = Mutieren(zufall, vorlage, puffer);
            var daten = puffer.AsSpan(0, laenge);
            try
            {
                Durchlaufen(daten, i);
            }
            catch (Exception e) when (e is not PruefungException)
            {
                throw new PruefungException($"Fall {i}: {e.GetType().Name}: {e.Message} bei {Convert.ToHexString(daten)}");
            }
        }
    }

    private static void Durchlaufen(ReadOnlySpan<byte> daten, int i)
    {
        var seq = new ReadOnlySequence<byte>(daten.ToArray());
        var grenze = (i % 3) switch { 0 => Rahmengrenzen.VorHello, 1 => Rahmengrenzen.RegistrierungUndWarteraum, _ => Rahmengrenzen.Aktiv };
        if (Rahmen.VersucheLesen(ref seq, grenze, out var kopf, out var body, out _) != RahmenStatus.Fertig) return;
        var zustand = (ServerZustand)(1 + i % 5);
        var server = ServerIn(zustand);
        server.Empfangen(kopf, body, 0);
        var client = ClientIn((ClientZustand)(1 + i % 7));
        client.Empfangen(kopf, body.ToArray(), 0);
        JsonVorpruefung.Pruefen(body.ToArray());
    }

    private static int Mutieren(Random zufall, byte[] vorlage, byte[] ziel)
    {
        vorlage.CopyTo(ziel, 0);
        var laenge = vorlage.Length;
        var schritte = 1 + zufall.Next(4);
        for (var s = 0; s < schritte; s++)
        {
            switch (zufall.Next(6))
            {
                case 0: // Bit kippen
                    ziel[zufall.Next(laenge)] ^= (byte)(1 << zufall.Next(8));
                    break;
                case 1: // Byte ersetzen, bevorzugt JSON-Sonderzeichen
                {
                    var sonder = "{}[]\",:\\0 -.eE\u0001"u8;
                    ziel[zufall.Next(laenge)] = sonder[zufall.Next(sonder.Length)];
                    break;
                }
                case 2: // kürzen
                    laenge = Math.Max(1, laenge - zufall.Next(1, 8));
                    break;
                case 3: // Byte einfügen
                    if (laenge < ziel.Length - 1)
                    {
                        var p = zufall.Next(laenge);
                        Array.Copy(ziel, p, ziel, p + 1, laenge - p);
                        ziel[p] = (byte)zufall.Next(256);
                        laenge++;
                    }
                    break;
                case 4: // Längenfeld passend zur neuen Länge setzen, damit der Body-Parser erreicht wird
                    if (laenge >= Rahmen.KopfLaenge)
                    {
                        var n = (uint)(laenge - 4);
                        ziel[0] = (byte)(n >> 24);
                        ziel[1] = (byte)(n >> 16);
                        ziel[2] = (byte)(n >> 8);
                        ziel[3] = (byte)n;
                    }
                    break;
                default: // Stück doppeln (doppelte Schlüssel, Tiefe)
                    if (laenge > Rahmen.KopfLaenge + 4 && laenge * 2 < ziel.Length)
                    {
                        var von = zufall.Next(Rahmen.KopfLaenge, laenge);
                        var anzahl = Math.Min(laenge - von, zufall.Next(1, 24));
                        Array.Copy(ziel, von, ziel, von + anzahl, laenge - von);
                        laenge += anzahl;
                    }
                    break;
            }
        }
        // Meist das Längenfeld stimmig halten, sonst prüft nur der Kopfparser.
        if (zufall.Next(4) != 0 && laenge >= Rahmen.KopfLaenge)
        {
            var n = (uint)(laenge - 4);
            ziel[0] = (byte)(n >> 24);
            ziel[1] = (byte)(n >> 16);
            ziel[2] = (byte)(n >> 8);
            ziel[3] = (byte)n;
        }
        return laenge;
    }

    [Test]
    public static void P42_Kurz_Zeichenketten()
    {
        var zufall = new Random(99);
        var sb = new StringBuilder();
        for (var i = 0; i < 20_000; i++)
        {
            sb.Clear();
            var n = zufall.Next(0, 40);
            for (var j = 0; j < n; j++) sb.Append((char)zufall.Next(0, 0x10000));
            var s = sb.ToString();
            _ = Textregeln.Pruefen(s);
            _ = Textregeln.Skalarwerte(s);
            try
            {
                var json = SteuerJson.Serialisieren(new RegistrierungAnfrage { Geraetename = s });
                // Was der eigene Schreiber erzeugt, ist wohlgeformt: höchstens UNGUELTIG oder ZU_GROSS.
                var e = SteuerJson.Lesen(json, SteuerJson.Info<RegistrierungAnfrage>());
                Pruefe.Wahr(e.Fehler is null or Fehlercode.Ungueltig or Fehlercode.ZuGross, $"Fall {i}: {e.Fehler}");
            }
            catch (System.Text.Json.JsonException)
            {
                // Nur ungültiges UTF-16 (einsames Surrogat) weist der Schreiber ab.
                Pruefe.Falsch(Textregeln.Pruefen(s), $"Fall {i}: Schreiber weist gültigen Text ab");
            }
        }
    }
}
