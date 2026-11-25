// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using Funk.Klang.Eingabe;

namespace Funk.Tests.Maschine;

/// <summary>
/// Sprechtaste (A3): Auswertung der Raw-Input-Daten ohne Fenster, Zustand (Wechsel, Wiederholung, Belegen), Verbund
/// als Naht für DirectInput, und ein Rauchtest des Raw-Input-Threads (nur mit FUNK_RAUCHTEST=1 und ohne laufendes Spiel).
/// </summary>
internal static class EingabeTests
{
    private static byte[] Tastatur(ushort makeCode, ushort flags, ushort vkey)
    {
        var b = new byte[RawInputAuswertung.TastaturBytes];
        BinaryPrimitives.WriteInt32LittleEndian(b, RawInputAuswertung.TypTastatur);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), b.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(24), makeCode);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(26), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(30), vkey);
        return b;
    }

    private static byte[] Maus(ushort knopfFlags)
    {
        var b = new byte[RawInputAuswertung.MausBytes];
        BinaryPrimitives.WriteInt32LittleEndian(b, RawInputAuswertung.TypMaus);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), b.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(28), knopfFlags);
        return b;
    }

    private static Tastenereignis[] Auswerten(byte[] daten)
    {
        var ziel = new Tastenereignis[5];
        var n = RawInputAuswertung.Auswerten(daten, ziel);
        return ziel[..n];
    }

    [Test]
    public static void Tastatur_LinksUndRechtsGetrennt_MitPraefix()
    {
        var links = Auswerten(Tastatur(0x1D, 0, 0x11));
        var rechts = Auswerten(Tastatur(0x1D, 0x02, 0x11));
        var rechtsLos = Auswerten(Tastatur(0x1D, 0x03, 0x11));
        Pruefe.Gleich(new Tastenereignis(Tastenbelegung.Taste(0x001D), true), links.Single(), "linke Strg gedrückt");
        Pruefe.Gleich(new Tastenereignis(Tastenbelegung.Taste(0xE01D), true), rechts.Single(), "rechte Strg gedrückt (E0)");
        Pruefe.Gleich(new Tastenereignis(Tastenbelegung.Taste(0xE01D), false), rechtsLos.Single(), "rechte Strg losgelassen");
        Pruefe.Gleich(Tastenbelegung.Taste(0xE11D), Auswerten(Tastatur(0x1D, 0x04, 0x13)).Single().Taste, "Pause mit E1");
    }

    [Test]
    public static void Tastatur_UnechteTastenUndUeberlauf_ZaehlenNicht()
    {
        Pruefe.Gleich(0, Auswerten(Tastatur(0x2A, 0x02, 0xFF)).Length, "Schein-Shift vor Pfeiltasten (VKey 0xFF)");
        Pruefe.Gleich(0, Auswerten(Tastatur(0xFF, 0, 0x41)).Length, "Überlauf");
        Pruefe.Gleich(0, RawInputAuswertung.Auswerten(new byte[10], new Tastenereignis[5]), "zu kurz");
        var hid = Tastatur(0x1E, 0, 0x41);
        BinaryPrimitives.WriteInt32LittleEndian(hid, 2);
        Pruefe.Gleich(0, Auswerten(hid).Length, "HID-Geräte liefern nichts");
    }

    [Test]
    public static void Maus_KnoepfeEinsBisFuenf()
    {
        Pruefe.Gleich(new Tastenereignis(Tastenbelegung.Maus(Tastenbelegung.MausX1), true), Auswerten(Maus(0x0040)).Single(), "X1 gedrückt");
        Pruefe.Gleich(new Tastenereignis(Tastenbelegung.Maus(Tastenbelegung.MausX2), false), Auswerten(Maus(0x0200)).Single(), "X2 losgelassen");
        var zwei = Auswerten(Maus(0x0001 | 0x0020));
        Pruefe.Gleich(2, zwei.Length, "zwei Wechsel in einer Nachricht");
        Pruefe.Wahr(zwei.Contains(new Tastenereignis(Tastenbelegung.Maus(1), true)) && zwei.Contains(new Tastenereignis(Tastenbelegung.Maus(3), false)), "links runter, Mitte hoch");
        Pruefe.Gleich(0, Auswerten(Maus(0)).Length, "Bewegung allein liefert nichts");
    }

    [Test]
    public static void Zustand_NurWechselDerBelegtenTaste()
    {
        var z = new SprechtastenZustand();
        _ = z.Belegen(Tastenbelegung.Taste(0xE01D));
        var folge = new[]
        {
            new Tastenereignis(Tastenbelegung.Taste(0x001D), true), // linke Strg: fremd
            new Tastenereignis(Tastenbelegung.Taste(0xE01D), true),
            new Tastenereignis(Tastenbelegung.Taste(0xE01D), true), // Wiederholung beim Halten
            new Tastenereignis(Tastenbelegung.Taste(0xE01D), true),
            new Tastenereignis(Tastenbelegung.Maus(1), true), // fremd
            new Tastenereignis(Tastenbelegung.Taste(0xE01D), false),
            new Tastenereignis(Tastenbelegung.Taste(0xE01D), false),
        };
        var wechsel = new List<int>();
        foreach (var e in folge)
        {
            z.Verarbeiten(e, out var w, out var gelernt);
            Pruefe.Gleich(null, gelernt, "nichts gelernt");
            if (w != 0) wechsel.Add(w);
        }
        Pruefe.Wahr(wechsel.SequenceEqual([1, -1]), "genau einmal gedrückt und einmal losgelassen");
    }

    [Test]
    public static void Zustand_BelegenLerntNaechsteTaste_UndNeueBelegungLaesstLos()
    {
        var z = new SprechtastenZustand { Lernt = true };
        z.Verarbeiten(new Tastenereignis(Tastenbelegung.Taste(0x0039), false), out _, out var nichts);
        Pruefe.Gleich(null, nichts, "Loslassen wird nicht gelernt");
        z.Verarbeiten(new Tastenereignis(Tastenbelegung.Maus(4), true), out var w, out var gelernt);
        Pruefe.Gleich(Tastenbelegung.Maus(4), gelernt, "X1 gelernt");
        Pruefe.Gleich(0, w, "Lernen sendet nicht");
        Pruefe.Falsch(z.Lernt, "danach nicht mehr im Lernmodus");

        _ = z.Belegen(Tastenbelegung.Maus(4));
        z.Verarbeiten(new Tastenereignis(Tastenbelegung.Maus(4), true), out var runter, out _);
        Pruefe.Gleich(1, runter, "gedrückt");
        Pruefe.Wahr(z.Belegen(Tastenbelegung.Taste(0xE01D)), "Umbelegen bei gehaltener Taste meldet Loslassen");
        Pruefe.Falsch(z.IstGedrueckt, "nicht mehr gedrückt");
    }

    [Test]
    public static void Tastenbelegung_Kurzform()
    {
        Pruefe.Gleich("Taste 0xE01D", Tastenbelegung.Taste(0xE01D).ToString(), "Tastatur");
        Pruefe.Gleich("Maus 5", Tastenbelegung.Maus(5).ToString(), "Maus");
    }

    private sealed class JoystickAttrappe : ISprechtasteQuelle
    {
        public Tastenbelegung? Belegung { get; set; }

        public bool IstGedrueckt { get; private set; }

        public event Action? Gedrueckt;

        public event Action? Losgelassen;

        public bool Unterstuetzt(EingabeArt art) => art == EingabeArt.Joystick;

        public Task<Tastenbelegung> NaechsteTasteAsync(CancellationToken ct) => Task.FromResult(new Tastenbelegung(EingabeArt.Joystick, 7, "HID#VID_3344&PID_0001#1"));

        public void Druecken(bool unten)
        {
            IstGedrueckt = unten;
            if (unten) Gedrueckt?.Invoke();
            else Losgelassen?.Invoke();
        }

        public void Dispose()
        {
        }
    }

    private sealed class TastaturAttrappe : ISprechtasteQuelle
    {
        public Tastenbelegung? Belegung { get; set; }

        public bool IstGedrueckt => false;

        public event Action? Gedrueckt
        {
            add { }
            remove { }
        }

        public event Action? Losgelassen
        {
            add { }
            remove { }
        }

        public bool Unterstuetzt(EingabeArt art) => art is EingabeArt.Tastatur or EingabeArt.Maus;

        public Task<Tastenbelegung> NaechsteTasteAsync(CancellationToken ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => Tastenbelegung.Taste(1), TaskScheduler.Default);

        public void Dispose()
        {
        }
    }

    [Test]
    public static async Task Verbund_LeitetBelegungWeiter_UndMeldetJoystick()
    {
        var joystick = new JoystickAttrappe();
        var tastatur = new TastaturAttrappe();
        using var verbund = new SprechtastenVerbund(tastatur, joystick);
        var stick = new Tastenbelegung(EingabeArt.Joystick, 7, "HID#VID_3344&PID_0001#1");
        verbund.Belegung = stick;
        Pruefe.Gleich(stick, joystick.Belegung, "Joystick-Belegung an die Joystick-Quelle");
        Pruefe.Gleich(null, tastatur.Belegung, "Tastatur-Quelle geleert");
        var gedrueckt = 0;
        var losgelassen = 0;
        verbund.Gedrueckt += () => gedrueckt++;
        verbund.Losgelassen += () => losgelassen++;
        joystick.Druecken(true);
        joystick.Druecken(false);
        Pruefe.Gleich((1, 1), (gedrueckt, losgelassen), "Ereignisse kommen durch");
        Pruefe.Gleich(stick, await verbund.NaechsteTasteAsync(CancellationToken.None), "Belegen: erste Quelle mit Taste gewinnt");

        using var nurTastatur = new SprechtastenVerbund(new TastaturAttrappe());
        _ = Pruefe.Wirft<NotSupportedException>(() => nurTastatur.Belegung = stick, "ohne DirectInput-Quelle kein Joystick");
    }

    [Test]
    public static void RawInput_Rauchtest_StartenBelegenBeenden()
    {
        // Öffnet ein unsichtbares Nachrichtenfenster (kein Fokus, keine Anzeige), meldet aber Tastatur und Maus mit
        // RIDEV_INPUTSINK an: Der Testprozess bekäme jede Eingabe des Systems, auch die für andere Programme. Deshalb nur
        // mit FUNK_RAUCHTEST=1 und ohne laufendes Spiel. Kein Belegen am echten Gerät, es übernähme die nächste echte
        // Taste des Nutzers; die Lernlogik prüft Zustand_BelegenLerntNaechsteTaste_UndNeueBelegungLaesstLos ohne Gerät.
        if (!Spielschutz.RauchtestFreigegeben("Raw-Input-Rauchtest")) return;
        using var taste = new RawInputSprechtaste();
        Pruefe.Wahr(taste.Unterstuetzt(EingabeArt.Tastatur) && taste.Unterstuetzt(EingabeArt.Maus), "Tastatur und Maus");
        Pruefe.Falsch(taste.Unterstuetzt(EingabeArt.Joystick), "Joystick nicht über Raw Input");
        taste.Belegung = Tastenbelegung.Maus(Tastenbelegung.MausX2); // meldet die Maus an
        taste.Belegung = Tastenbelegung.Taste(0xE01D); // und wieder ab
        _ = Pruefe.Wirft<NotSupportedException>(() => taste.Belegung = new Tastenbelegung(EingabeArt.Joystick, 1), "Joystick abgelehnt");
        Pruefe.Falsch(taste.IstGedrueckt, "nichts gedrückt");
        var name = RawInputSprechtaste.Tastenname(Tastenbelegung.Taste(0x0039));
        Console.WriteLine($"  Raw Input: Leertaste heißt \"{name}\"");
        Pruefe.Wahr(name.Length > 0, "Tastenname aus dem Layout");
    }
}
