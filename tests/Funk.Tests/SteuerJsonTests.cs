// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using System.Text.Json;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.SteuerHilfe;

namespace Funk.Tests;

/// <summary>Kodierung der JSON-Bodies (4.3), Prüfplan P-02 und P-44.</summary>
internal static class SteuerJsonTests
{
    private static DekodierErgebnis Lies<T>(string json) => SteuerJson.Lesen(Encoding.UTF8.GetBytes(json), SteuerJson.Info<T>());

    private static DekodierErgebnis Lies<T>(byte[] json) => SteuerJson.Lesen(json, SteuerJson.Info<T>());

    private static void Erwarte<T>(Fehlercode? erwartet, string json, string was)
    {
        var e = Lies<T>(json);
        Pruefe.Gleich(erwartet, e.Fehler, $"{was}: {json}");
    }

    // ---------------------------------------------------------------- P-02 doppelte Schlüssel

    [Test]
    public static void P02_DoppelteSchluessel_Protokoll()
    {
        Erwarte<PingAnfrage>(Fehlercode.Protokoll, "{\"nr\":1,\"nr\":2}", "bekannt");
        Erwarte<PingAnfrage>(Fehlercode.Protokoll, "{\"nr\":1,\"x\":1,\"x\":2}", "unbekannt");
        Erwarte<PingAnfrage>(Fehlercode.Protokoll, "{\"nr\":1,\"n\\u0072\":2}", "maskiert gleich");
        Erwarte<PingAnfrage>(Fehlercode.Protokoll, "{\"nr\":1,\"x\":{\"a\":1,\"a\":1}}", "verschachtelt");
        Erwarte<PingAnfrage>(Fehlercode.Protokoll, "{\"nr\":1,\"x\":[{\"a\":1},{\"b\":1,\"b\":2}]}", "in Liste");
        Erwarte<PingAnfrage>(null, "{\"nr\":1,\"x\":[{\"a\":1},{\"a\":2}],\"y\":{\"a\":1}}", "gleiche Namen in Geschwistern");
        Erwarte<PingAnfrage>(null, "{\"nr\":1,\"x\":{\"nr\":2}}", "gleicher Name auf anderer Ebene");
    }

    // ---------------------------------------------------------------- P-02 Tiefe

    [Test]
    public static void P02_Tiefe8Gilt_Tiefe9Protokoll()
    {
        // Wurzel (1) + daten (2) + 6 weitere Ebenen = 8.
        var acht = "{\"op\":\"LESEN\",\"bereich\":\"SAETZE\",\"daten\":{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{}}}}}}}}";
        var neun = "{\"op\":\"LESEN\",\"bereich\":\"SAETZE\",\"daten\":{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":{}}}}}}}}}";
        Erwarte<AblageAnfrage>(null, acht, "Tiefe 8");
        Erwarte<AblageAnfrage>(Fehlercode.Protokoll, neun, "Tiefe 9");
        Erwarte<PingAnfrage>(Fehlercode.Protokoll, "{\"nr\":1,\"x\":[[[[[[[[1]]]]]]]]}", "Tiefe 9 mit Listen");
        Erwarte<PingAnfrage>(null, "{\"nr\":1,\"x\":[[[[[[[1]]]]]]]}", "Tiefe 8 mit Listen");
    }

    // ---------------------------------------------------------------- P-02 Steuer- und Bidi-Zeichen

    [Test]
    public static void P02_SteuerUndBidiZeichen_Ungueltig()
    {
        foreach (var z in new[] { "\\u0000", "\\u0001", "\\u001f", "\\u007f", "\\u0085", "\\u009f", "\\u202a", "\\u202e", "\\u2066", "\\u2069", "\\n", "\\t" })
        {
            Erwarte<RegistrierungAnfrage>(Fehlercode.Ungueltig, "{\"geraetename\":\"PC" + z + "\"}", $"maskiert {z}");
        }
        foreach (var z in new[] { "\u007f", "\u0085", "\u202e", "\u2067" })
        {
            Erwarte<RegistrierungAnfrage>(Fehlercode.Ungueltig, "{\"geraetename\":\"PC" + z + "\"}", $"roh U+{(int)z[0]:X4}");
        }
        // Rohe C0-Zeichen sind schon kein gültiges JSON.
        Erwarte<RegistrierungAnfrage>(Fehlercode.Protokoll, "{\"geraetename\":\"PC\u0001\"}", "rohes U+0001");
        // Auch in unbekannten Feldern und in Schlüsseln.
        Erwarte<RegistrierungAnfrage>(Fehlercode.Ungueltig, "{\"geraetename\":\"PC\",\"x\":\"\\u202e\"}", "unbekanntes Feld");
        Erwarte<RegistrierungAnfrage>(Fehlercode.Ungueltig, "{\"geraetename\":\"PC\",\"x\\u0001\":1}", "Schlüssel");
        // Grenzfälle, die erlaubt sind.
        foreach (var z in new[] { "\\u0020", "\\u007e", "\\u00a0", "\\u2029", "\\u202f", "\\u2065", "\\u206a" })
        {
            Erwarte<RegistrierungAnfrage>(null, "{\"geraetename\":\"PC" + z + "\"}", $"erlaubt {z}");
        }
    }

    [Test]
    public static void P02_EinsamesSurrogatUndKaputtesUtf8()
    {
        Erwarte<RegistrierungAnfrage>(Fehlercode.Ungueltig, "{\"geraetename\":\"\\ud800\"}", "einsames Surrogat");
        Erwarte<RegistrierungAnfrage>(null, "{\"geraetename\":\"\\ud83d\\ude00\"}", "Surrogatpaar");
        var kaputt = Encoding.UTF8.GetBytes("{\"geraetename\":\"PCx\"}");
        kaputt[18] = 0xFF;
        Pruefe.Gleich(Fehlercode.Protokoll, Lies<RegistrierungAnfrage>(kaputt).Fehler, "0xFF im Text");
        var ueberlang = Encoding.UTF8.GetBytes("{\"geraetename\":\"PC__\"}");
        ueberlang[18] = 0xC0;
        ueberlang[19] = 0xAF;
        Pruefe.Gleich(Fehlercode.Protokoll, Lies<RegistrierungAnfrage>(ueberlang).Fehler, "überlanges UTF-8");
    }

    [Test]
    public static void P02_Nfc_SoweitDiePlattformEsKann()
    {
        // 4.3: Zeichenketten in NFC. Unter InvariantGlobalization prüft .NET das nicht (Bericht, Abweichungen).
        Erwarte<RegistrierungAnfrage>(null, "{\"geraetename\":\"J\u00fcrgen\"}", "vorkomponiert");
        var zerlegt = Lies<RegistrierungAnfrage>("{\"geraetename\":\"Mu\u0308ller\"}");
        if (Textregeln.NfcPruefbar)
        {
            Pruefe.Gleich(Fehlercode.Ungueltig, zerlegt.Fehler, "zerlegt, nicht NFC");
        }
        else
        {
            Pruefe.Gleich(null, zerlegt.Fehler, "zerlegt: im Invariant-Modus nicht prüfbar");
            Console.WriteLine("HINWEIS: NFC-Prüfung unwirksam (InvariantGlobalization); nur Kurzprüfung unter U+0300.");
        }
    }

    // ---------------------------------------------------------------- P-02 Bereichsgrenzen

    [Test]
    public static void P02_Bereichsgrenzen()
    {
        Erwarte<PingAnfrage>(null, "{\"nr\":4294967295}", "u32 max");
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{\"nr\":4294967296}", "u32 max + 1");
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{\"nr\":-1}", "u32 negativ");
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{\"nr\":1.0}", "Nachkommastelle");
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{\"nr\":1e2}", "Exponent");
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{\"nr\":\"1\"}", "Zahl als Text");
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{\"nr\":true}", "Wahrheitswert");

        Erwarte<ZustandsVersionBody>(null, "{\"zustandsVersion\":9007199254740991}", "2^53-1");
        Erwarte<ZustandsVersionBody>(Fehlercode.Ungueltig, "{\"zustandsVersion\":9007199254740992}", "2^53");
        Erwarte<ZustandsVersionBody>(Fehlercode.Ungueltig, "{\"zustandsVersion\":-1}", "negativ");
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{\"nr\":1,\"x\":9007199254740992}", "2^53 im unbekannten Feld");
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{\"nr\":1,\"x\":1e400}", "unendlich");

        Erwarte<HoerenSetzenAnfrage>(null, "{\"netzId\":1,\"hoeren\":true}", "Netz 1");
        Erwarte<HoerenSetzenAnfrage>(null, "{\"netzId\":65534,\"hoeren\":true}", "Netz 65534");
        Erwarte<HoerenSetzenAnfrage>(Fehlercode.Ungueltig, "{\"netzId\":0,\"hoeren\":true}", "Netz 0");
        Erwarte<HoerenSetzenAnfrage>(Fehlercode.Ungueltig, "{\"netzId\":65535,\"hoeren\":true}", "Netz 65535");
        Erwarte<HoerenSetzenAnfrage>(Fehlercode.Ungueltig, "{\"netzId\":65536,\"hoeren\":true}", "u16 max + 1");

        Erwarte<StummschaltenAnfrage>(null, "{\"kontoId\":1,\"netzId\":0,\"sekunden\":3600}", "3600 s");
        Erwarte<StummschaltenAnfrage>(Fehlercode.Ungueltig, "{\"kontoId\":1,\"netzId\":0,\"sekunden\":3601}", "3601 s");
        Erwarte<FeldSetzenAnfrage>(null, "{\"feld\":15}", "Feld 15");
        Erwarte<FeldSetzenAnfrage>(Fehlercode.Ungueltig, "{\"feld\":16}", "Feld 16");
        Erwarte<FeldSetzenAnfrage>(Fehlercode.Ungueltig, "{\"feld\":256}", "u8 max + 1");

        Erwarte<PegelSetzenAnfrage>(null, "{\"kontoId\":1,\"netzId\":0,\"db\":-60.0}", "-60 dB");
        Erwarte<PegelSetzenAnfrage>(null, "{\"kontoId\":1,\"netzId\":0,\"db\":12}", "+12 dB");
        Erwarte<PegelSetzenAnfrage>(null, "{\"kontoId\":1,\"netzId\":0,\"db\":-0.5}", "-0.5 dB");
        Erwarte<PegelSetzenAnfrage>(Fehlercode.Ungueltig, "{\"kontoId\":1,\"netzId\":0,\"db\":12.5}", "+12.5 dB");
        Erwarte<PegelSetzenAnfrage>(Fehlercode.Ungueltig, "{\"kontoId\":1,\"netzId\":0,\"db\":0.25}", "0.25 dB");

        Erwarte<VerlustHinweis>(null, "{\"prozent\":100}", "100 %");
        Erwarte<VerlustHinweis>(Fehlercode.Ungueltig, "{\"prozent\":101}", "101 %");

        Erwarte<SendeauswahlSetzenAnfrage>(null, "{\"felder\":[0,3,15],\"global\":false}", "aufsteigend");
        Erwarte<SendeauswahlSetzenAnfrage>(Fehlercode.Ungueltig, "{\"felder\":[3,0],\"global\":false}", "absteigend");
        Erwarte<SendeauswahlSetzenAnfrage>(Fehlercode.Ungueltig, "{\"felder\":[3,3],\"global\":false}", "doppelt");
        Erwarte<SendeauswahlSetzenAnfrage>(Fehlercode.ZuGross, "{\"felder\":[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16],\"global\":false}", "17 Felder");
    }

    [Test]
    public static void P02_Laengen_ZaehlenSkalarwerte_UndErgebenZuGross()
    {
        var vierzig = new string('x', 40);
        Erwarte<RegistrierungAnfrage>(null, "{\"geraetename\":\"" + vierzig + "\"}", "40 Zeichen");
        Erwarte<RegistrierungAnfrage>(Fehlercode.ZuGross, "{\"geraetename\":\"" + vierzig + "x\"}", "41 Zeichen");
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 40));
        Erwarte<RegistrierungAnfrage>(null, "{\"geraetename\":\"" + emoji + "\"}", "40 Emoji = 80 UTF-16-Zeichen");
        Erwarte<RegistrierungAnfrage>(Fehlercode.ZuGross, "{\"geraetename\":\"" + emoji + "\U0001F600\"}", "41 Emoji");
        Pruefe.Wahr(Fehlercodes.Trennt(Fehlercode.ZuGross), "ZU_GROSS trennt (4.9)");
        Erwarte<Fehler>(Fehlercode.ZuGross, "{\"code\":\"INTERN\",\"details\":{\"x\":\"" + new string('a', 4096) + "\"}}", "details über 4 KiB");
        Erwarte<Fehler>(null, "{\"code\":\"INTERN\",\"details\":{\"x\":\"" + new string('a', 4096 - 8) + "\"}}", "details genau 4 KiB");
    }

    // ---------------------------------------------------------------- P-02 Pflichtfelder, unbekannte Felder, null

    [Test]
    public static void P02_FehlendePflichtfelder_Ungueltig()
    {
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{}", "nr fehlt");
        Erwarte<Hello>(Fehlercode.Ungueltig, "{\"minor\":0,\"schluesselArt\":\"TPM\",\"opus\":\"1.6.1\",\"faehigkeiten\":[]}", "client fehlt");
        Erwarte<Hello>(Fehlercode.Ungueltig, "{\"minor\":0,\"client\":{\"name\":\"Funk\"},\"schluesselArt\":\"TPM\",\"opus\":\"1.6.1\",\"faehigkeiten\":[]}", "client.version fehlt");
        Erwarte<Welcome>(Fehlercode.Ungueltig, "{\"minor\":0,\"status\":\"UNBEKANNT\"}", "registrierung fehlt bei UNBEKANNT");
        Erwarte<Welcome>(Fehlercode.Ungueltig, "{\"minor\":0,\"status\":\"FREIGEGEBEN\",\"sitzungsId\":5}", "FREIGEGEBEN unvollständig");
        Pruefe.Wahr(!Fehlercodes.Trennt(Fehlercode.Ungueltig), "UNGUELTIG trennt nicht");
    }

    [Test]
    public static void P02_UnbekannteFelder_Uebergangen()
    {
        var e = Lies<PingAnfrage>("{\"zusatz\":{\"x\":[1,2,{\"y\":null}]},\"nr\":5,\"neu\":\"feld\"}");
        Pruefe.Gleich(null, e.Fehler, "Fehler");
        Pruefe.Gleich(5u, ((PingAnfrage)e.Body!).Nr, "nr");
        Erwarte<LeererBody>(null, "{\"was\":\"auch immer\"}", "leerer Body mit Zusatz");
    }

    [Test]
    public static void P02_NullWieFehlend()
    {
        var e = Lies<Fehler>("{\"code\":\"GRENZE\",\"text\":null,\"bezugTyp\":null,\"details\":null}");
        Pruefe.Gleich(null, e.Fehler, "optional null");
        var f = (Fehler)e.Body!;
        Pruefe.Gleich(null, f.Text, "text");
        Pruefe.Gleich(null, f.BezugTyp, "bezugTyp");
        Pruefe.Wahr(f.Details is null, "details");
        Erwarte<PingAnfrage>(Fehlercode.Ungueltig, "{\"nr\":null}", "Pflichtfeld null wie fehlend");
        Erwarte<RegistrierungAnfrage>(Fehlercode.Ungueltig, "{\"geraetename\":null}", "Pflichttext null");
        Erwarte<Hello>(Fehlercode.Ungueltig, "{\"minor\":0,\"client\":{\"name\":\"F\",\"version\":\"1.0.0\"},\"schluesselArt\":\"TPM\",\"opus\":\"1\",\"faehigkeiten\":[null]}", "null in Liste");
    }

    // ---------------------------------------------------------------- 4.3 Wohlgeformtheit

    [Test]
    public static void Json_NichtWohlgeformt_Protokoll()
    {
        foreach (var json in new[] { "", " ", "{", "{\"nr\":1,}", "{/*x*/\"nr\":1}", "{\"nr\":1}//x", "{\"nr\":1} {}", "[1]", "1", "\"x\"", "null", "{\"nr\":01}", "{'nr':1}", "{\"nr\":1]" })
        {
            Erwarte<PingAnfrage>(Fehlercode.Protokoll, json, "nicht wohlgeformt");
        }
        Pruefe.Gleich(Fehlercode.Protokoll, Lies<PingAnfrage>([0xEF, 0xBB, 0xBF, .. "{\"nr\":1}"u8]).Fehler, "BOM");
        Erwarte<PingAnfrage>(null, " \r\n\t{ \"nr\" : 1 }\n ", "Leerraum ist erlaubt");
    }

    [Test]
    public static void Json_Aufzaehlungen_Streng()
    {
        Erwarte<Bye>(null, "{\"grund\":\"BEENDET\"}", "gültig");
        Erwarte<Bye>(null, "{\"grund\":\"BEENDE\\u0054\"}", "maskiert gleich");
        Erwarte<Bye>(Fehlercode.Ungueltig, "{\"grund\":\"beendet\"}", "klein");
        Erwarte<Bye>(Fehlercode.Ungueltig, "{\"grund\":0}", "Zahl");
        Erwarte<Bye>(Fehlercode.Ungueltig, "{\"grund\":\"BEENDET, WARTUNG\"}", "Liste");
        Erwarte<Bye>(Fehlercode.Ungueltig, "{\"grund\":\"UNBEKANNT\"}", "fremder Wert");
    }

    // ---------------------------------------------------------------- 4.3 Sender: kompakt, Katalogreihenfolge, unmaskiert

    [Test]
    public static void Json_Sender_KompaktReihenfolgeUnmaskiert()
    {
        var hello = new Hello
        {
            Minor = 0,
            Client = new ClientAngabe { Name = "Jürgen 😀 <&>'", Version = "1.2.3-beta.1" },
            SchluesselArt = SchluesselArt.Tpm,
            Opus = "1.6.1",
            Faehigkeiten = ["tunnel"],
        };
        var json = Encoding.UTF8.GetString(SteuerJson.Serialisieren(hello));
        Pruefe.Gleich("{\"minor\":0,\"client\":{\"name\":\"Jürgen 😀 <&>'\",\"version\":\"1.2.3-beta.1\"},\"schluesselArt\":\"TPM\",\"opus\":\"1.6.1\",\"faehigkeiten\":[\"tunnel\"]}", json, "Hello");
        Pruefe.Gleich(null, Lies<Hello>(json).Fehler, "liest zurück");

        var sonder = Encoding.UTF8.GetString(SteuerJson.Serialisieren(new Fehler { Code = Fehlercode.Intern, Text = "a\"b\\c\u0001\u2028\uFEFF" }));
        Pruefe.Gleich("{\"code\":\"INTERN\",\"text\":\"a\\\"b\\\\c\\u0001\u2028\uFEFF\"}", sonder, "nur \" \\ und C0 maskiert");
    }

    [Test]
    public static void Json_Empfaenger_BeideSchreibweisen()
    {
        var roh = (RegistrierungAnfrage)Lies<RegistrierungAnfrage>("{\"geraetename\":\"Jürgen 😀\"}").Body!;
        var maskiert = (RegistrierungAnfrage)Lies<RegistrierungAnfrage>("{\"geraetename\":\"J\\u00fcrgen \\ud83d\\ude00\"}").Body!;
        Pruefe.Gleich(roh.Geraetename, maskiert.Geraetename, "gleich");
        Pruefe.Gleich("Jürgen 😀", roh.Geraetename, "Wert");
    }

    [Test]
    public static void Json_UnsafeRelaxed_MaskiertAusserhalbBmp_Messung()
    {
        // Messung zu Offener Punkt 3: JavaScriptEncoder.UnsafeRelaxedJsonEscaping maskiert Emoji.
        var opt = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        Pruefe.Gleich("\"\\uD83D\\uDE00\"", JsonSerializer.Serialize("\U0001F600", opt), "Emoji maskiert");
        Pruefe.Gleich("\"äöüß€\"", JsonSerializer.Serialize("äöüß€", opt), "BMP unmaskiert");
        // Der eigene Schreiber hält 4.3 ein: 32 Emoji = 128 Byte + 2 Anführungszeichen = 130 (Rechnung 4.10).
        var name = string.Concat(Enumerable.Repeat("\U0001F600", 32));
        Pruefe.Gleich(130 + "{\"version\":}".Length, SteuerJson.Serialisieren(new ServerAngabe { Version = name }).Length, "130 Byte je Name");
    }

    // ---------------------------------------------------------------- P-44 kein Leck vor der Freigabe

    [Test]
    public static void P44_WelcomeUnbekannt_NurRegistrierung()
    {
        var json = Encoding.UTF8.GetString(SteuerJson.Serialisieren(Welcome.Unbekannt(0)));
        Pruefe.Gleich("{\"minor\":0,\"status\":\"UNBEKANNT\",\"registrierung\":{\"codeLaenge\":12,\"gueltigSek\":300}}", json, "Welcome UNBEKANNT");
        Pruefe.Gleich(null, Lies<Welcome>(json).Fehler, "gültig");
    }

    [Test]
    public static void P44_WelcomeFreigegeben_Kanonisch()
    {
        var w = WelcomeFreigegeben();
        var json = Encoding.UTF8.GetString(SteuerJson.Serialisieren(w));
        Pruefe.Wahr(json.StartsWith("{\"minor\":0,\"status\":\"FREIGEGEBEN\",\"sitzungsId\":1584361601,\"konto\":", StringComparison.Ordinal), "Reihenfolge: " + json);
        Pruefe.Wahr(json.Contains("\"grenzen\":{\"rahmenMax\":1048576,\"opusMax\":400,\"zieleNetzMax\":16,\"zieleDurchsageMax\":64,\"pingMs\":2000,\"totMs\":6000,\"ruheMs\":10000,\"keepaliveMs\":2000}", StringComparison.Ordinal), "grenzen");
        Pruefe.Wahr(json.Contains("\"rechte\":[\"GLOBAL_SENDEN\",\"ZUWEISEN\"]", StringComparison.Ordinal), "rechte");
        Pruefe.Gleich(null, Lies<Welcome>(json).Fehler, "gültig");
    }

    // ---------------------------------------------------------------- 4.5 einzelne Regeln

    [Test]
    public static void Nachrichten_Einzelregeln()
    {
        Pruefe.Gleich(null, RolleLesen("\"netzId\":2").Fehler, "netzId allein");
        Pruefe.Gleich(null, RolleLesen("\"schiffsnetz\":\"CREW\"").Fehler, "schiffsnetz allein");
        Pruefe.Gleich(Fehlercode.Ungueltig, RolleLesen("\"netzId\":2,\"schiffsnetz\":\"CREW\"").Fehler, "beide");
        Pruefe.Gleich(Fehlercode.Ungueltig, RolleLesen("\"feld\":1").Fehler, "keines");
        Pruefe.Gleich(Fehlercode.Ungueltig, RolleLesen("\"schiffsnetz\":\"crew\"").Fehler, "Kennung klein");
        Pruefe.Gleich(Fehlercode.ZuGross, RolleLesen("\"schiffsnetz\":\"ABCDEFGHIJKLMNOPQ\"").Fehler, "Kennung 17 Zeichen");
        Erwarte<RaidHelperImportAnfrage>(null, "{\"eventId\":\"123456789012345678901234\"}", "24 Ziffern");
        Erwarte<RaidHelperImportAnfrage>(Fehlercode.Ungueltig, "{\"eventId\":\"1234567890123456789012345\"}", "25 Ziffern");
        Erwarte<RaidHelperImportAnfrage>(null, "{\"eventId\":\"https://discord.com/channels/1/2/3\"}", "Link");
        Erwarte<RaidHelperImportAnfrage>(Fehlercode.Ungueltig, "{\"eventId\":\"https://discord.com/channels/1/2\"}", "Link ohne Nachricht");
        Erwarte<RaidHelperImportAnfrage>(Fehlercode.Ungueltig, "{\"eventId\":\"http://discord.com/channels/1/2/3\"}", "falsches Schema");
        Pruefe.Gleich("3", RaidHelperEventId.NachrichtenId("https://discord.com/channels/1/2/3"), "Nachrichten-ID");

        Erwarte<AufstellungSpeichernAnfrage>(Fehlercode.Ungueltig, "{\"name\":\"A\",\"eintraege\":[{\"rolleId\":1}]}", "weder kontoId noch discordId");
        Erwarte<AufstellungSpeichernAnfrage>(Fehlercode.Ungueltig, "{\"name\":\"A\",\"eintraege\":[{\"kontoId\":1,\"discordId\":\"5\",\"rolleId\":1}]}", "beide");
        Erwarte<AufstellungSpeichernAnfrage>(null, "{\"name\":\"A\",\"eintraege\":[{\"discordId\":\"123456789012345678\",\"rolleId\":1}]}", "discordId");
        Erwarte<AufstellungSpeichernAnfrage>(Fehlercode.Ungueltig, "{\"name\":\"A\",\"eintraege\":[{\"discordId\":\"12a\",\"rolleId\":1}]}", "discordId mit Buchstabe");

        Erwarte<VerschiebenAnfrage>(Fehlercode.Ungueltig, "{\"aenderungen\":[]}", "0 Änderungen");
        Erwarte<VerschiebenAnfrage>(null, "{\"aenderungen\":[{\"kontoId\":5,\"felderPatch\":[{\"netzId\":7},{\"feld\":3,\"leeren\":true}]}]}", "Patch");
        Erwarte<VerschiebenAnfrage>(Fehlercode.Ungueltig, "{\"aenderungen\":[{\"kontoId\":5,\"felderPatch\":[{\"feld\":3}]}]}", "Patch ohne netzId");

        var patch = (VerschiebenAnfrage)Lies<VerschiebenAnfrage>("{\"aenderungen\":[{\"kontoId\":5,\"felderPatch\":[{\"netzId\":7}]}]}").Body!;
        var p = patch.Aenderungen[0].FelderPatch![0];
        Pruefe.Wahr(p.HoerenWirksam && !p.SendenWirksam && p.FestWirksam, "Standardwerte des Patches");

        Erwarte<Mitgliedschaften>(Fehlercode.Ungueltig, MitgliedschaftenJson("{\"feld\":0,\"netzId\":1,\"hoeren\":true,\"senden\":false,\"fest\":false}"), "Global in felder");
        Erwarte<Mitgliedschaften>(Fehlercode.Ungueltig, MitgliedschaftenJson("{\"feld\":0,\"netzId\":2,\"hoeren\":true,\"senden\":false,\"fest\":false},{\"feld\":0,\"netzId\":3,\"hoeren\":true,\"senden\":false,\"fest\":false}"), "Feld doppelt");
        Erwarte<Mitgliedschaften>(Fehlercode.Ungueltig, MitgliedschaftenJson("{\"feld\":0,\"netzId\":2,\"hoeren\":true,\"senden\":false,\"fest\":false},{\"feld\":1,\"netzId\":2,\"hoeren\":true,\"senden\":false,\"fest\":false}"), "Netz doppelt");
        Erwarte<Mitgliedschaften>(null, MitgliedschaftenJson("{\"feld\":0,\"netzId\":2,\"hoeren\":true,\"senden\":false,\"fest\":false}"), "gültig");

        Erwarte<GeraetEntfernenAntwort>(Fehlercode.Ungueltig, "{\"status\":\"WARTET\"}", "Status nicht erlaubt");
        Erwarte<GeraetStatus>(Fehlercode.Ungueltig, GeraetStatusJson("AAAA"), "Fingerabdruck nicht 32 Byte");
        Erwarte<GeraetStatus>(null, GeraetStatusJson("XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM="), "Fingerabdruck aus 8.5");
        Erwarte<AblageAnfrage>(Fehlercode.Ungueltig, "{\"op\":\"SCHREIBEN\",\"bereich\":\"SAETZE\"}", "SCHREIBEN ohne daten");
        Erwarte<AblageAnfrage>(Fehlercode.Ungueltig, "{\"op\":\"SCHREIBEN\",\"bereich\":\"SAETZE\",\"daten\":[1]}", "daten kein Objekt");
    }

    [Test]
    public static void Verwaltung_ObjektLesen()
    {
        var v = (VerwaltungAnfrage)Lies<VerwaltungAnfrage>("{\"op\":\"SELBSTWAHL\",\"objekt\":{\"an\":true},\"version\":3}").Body!;
        var e = v.ObjektLesen<AnAus>();
        Pruefe.Wahr(e.Ok && ((AnAus)e.Body!).An, "SELBSTWAHL {an}");
        var n = (VerwaltungAnfrage)Lies<VerwaltungAnfrage>("{\"op\":\"NETZ_SETZEN\",\"objekt\":{\"id\":7,\"name\":\"Crew\"},\"version\":3}").Body!;
        Pruefe.Gleich(Fehlercode.Ungueltig, n.ObjektLesen<Netz>().Fehler, "Netz unvollständig");
        Erwarte<VerwaltungAnfrage>(Fehlercode.Ungueltig, "{\"op\":\"SELBSTWAHL\",\"objekt\":5,\"version\":3}", "objekt kein Objekt");
    }

    /// <summary>Liest eine Rolle mit einem Feld, dessen Netzangabe <paramref name="netz"/> ist, über Verwaltung ROLLE_SETZEN.</summary>
    private static DekodierErgebnis RolleLesen(string netz)
    {
        var feld = netz.Contains("\"feld\"", StringComparison.Ordinal) ? netz : "\"feld\":0," + netz;
        var json = "{\"op\":\"ROLLE_SETZEN\",\"version\":1,\"objekt\":{\"id\":3,\"name\":\"CO\",\"reihenfolge\":1,\"selbstWaehlbar\":true,\"brauchtSchiff\":true,\"felder\":[{"
            + feld + ",\"senden\":true,\"hoeren\":true,\"sendenStandard\":true,\"fest\":false}]}}";
        var v = (VerwaltungAnfrage)Lies<VerwaltungAnfrage>(json).Body!;
        return v.ObjektLesen<Rolle>();
    }

    private static string MitgliedschaftenJson(string felder) =>
        "{\"zustandsVersion\":1,\"kontoVersion\":1,\"felder\":[" + felder + "],\"global\":{\"senden\":false},\"sendeauswahl\":{\"felder\":[],\"global\":false},\"stumm\":[],\"quelle\":\"STANDARD\"}";

    private static string GeraetStatusJson(string fingerabdruck) =>
        "{\"geraetId\":1,\"kontoId\":2,\"kontoName\":\"E\",\"benutzername\":\"e\",\"status\":\"WARTET\",\"anzeige\":\"5CD2-52FB-0CE8-9324\",\"fingerabdruck\":\"" + fingerabdruck + "\",\"schluesselArt\":\"SOFTWARE\",\"erstesGeraet\":true}";
}
