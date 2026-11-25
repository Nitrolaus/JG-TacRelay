// JG TacRelay - Maintainer: Sarevoc
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;
using Fehlercode = Funk.Protokoll.Steuerung.Fehlercode;
using SigniertesProfil = Funk.Protokoll.Identitaet.SigniertesProfil;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Fehlermeldungen für Funker (Sarevoc, Grilling Q5 und Q14): Sie beschreiben die Lage und tragen eine kurze Fehlernummer zum
/// Weitersagen, nennen aber nie Adresse, Hostname, Port oder Pfad und keine Admin-Themen (Portfreigabe, DynDNS). Das Technische
/// geht nicht verloren, sondern getrennt als Einzelheit mit (für "Details" im Admin-Setup). Gegenprobe mit einer Adresse
/// 203.0.113.7:8443 und einem Pfad C:\Users\Max\profil.json: beide nur in der Einzelheit.
/// </summary>
internal static class FehlertexteTests
{
    private const string Adresse = "203.0.113.7:8443";
    private const string Pfad = @"C:\Users\Max\profil.json";

    // IPv4, IPv6 in Klammern und ohne, Hostname mit Punkt (auch Dateinamen wie profil.json), Port hinter Doppelpunkt, Backslash.
    private static readonly Regex Technisch = new(
        @"\b\d{1,3}(?:\.\d{1,3}){3}\b|\[[0-9a-f:.]*:[0-9a-f:.]*\]|\b[0-9a-f]{1,4}(?::[0-9a-f]{0,4}){2,}|\b[a-z0-9-]+(?:\.[a-z0-9-]+)*\.[a-z]{2,}\b|:\d{1,5}\b|\\",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Admin-Themen gehören nicht in Funkertexte.
    private static readonly Regex Adminwort = new(@"\b(?:Port|Adress|Router|DynDNS|funk-admin)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Nummernende = new(@" \(Fehler (\d\d)\)$", RegexOptions.CultureInvariant);

    /// <summary>Ein Satz für Funker: nichts Technisches, keine Admin-Themen, nur einfache Zeichen.</summary>
    private static void Funkertext(string text, string was)
    {
        Pruefe.Wahr(text.Length > 0, was + ": nicht leer");
        Pruefe.Falsch(Technisch.IsMatch(text), was + ": ohne Adresse, Port und Pfad, war " + Technisch.Match(text).Value + " in: " + text);
        Pruefe.Falsch(Adminwort.IsMatch(text), was + ": ohne Admin-Themen, war " + Adminwort.Match(text).Value + " in: " + text);
        Pruefe.Falsch(text.Any(z => z is '\u2013' or '\u2014' or '\u2212' or '\u201E' or '\u201C' or '\u201D' or '\u2026' or '\u00A0'), was + ": nur einfache Zeichen: " + text);
    }

    /// <summary>Ein Fehler: Funkertext, am Ende genau eine Fehlernummer, und zwar die erwartete.</summary>
    private static void MitNummer(Verbindungsmeldung m, string nummer, string was)
    {
        Funkertext(m.Text, was);
        var treffer = Nummernende.Match(m.Text);
        Pruefe.Wahr(treffer.Success, was + ": endet mit \" (Fehler NN)\": " + m.Text);
        Pruefe.Gleich(nummer, treffer.Groups[1].Value, was + ": Fehlernummer in " + m.Text);
        Pruefe.Gleich(1, Regex.Count(m.Text, @"\(Fehler "), was + ": die Nummer steht einmal da");
        Pruefe.Wahr(FehlernummernTabelle.Alle.Any(f => f.Nummer == nummer), was + ": " + nummer + " steht in der Tabelle");
    }

    /// <summary>Ein normaler Zustand: Funkertext ohne Nummer.</summary>
    private static void OhneNummer(Verbindungsmeldung m, string was)
    {
        Funkertext(m.Text, was);
        Pruefe.Falsch(m.Text.Contains("(Fehler", StringComparison.Ordinal), was + ": normaler Zustand ohne Nummer: " + m.Text);
    }

    private static Sitzungsende Ende(string grund, ByeGrund? bye = null, Fehlercode? fehler = null) =>
        new(grund, bye, 10_000, fehler, 5_000, bye == ByeGrund.Verdraengt ? new ByeDurch { GeraetName = "Laptop", Anzeige = "1A2B-3C4D-5E6F-7A8B" } : null);

    [Test]
    public static void Fehlernummern_TabelleEindeutigUndInIhrenKreisen()
    {
        var nummern = FehlernummernTabelle.Alle.Select(f => f.Nummer).ToList();
        Pruefe.Gleich(nummern.Count, nummern.Distinct(StringComparer.Ordinal).Count(), "jede Nummer genau einmal: " + string.Join(",", nummern));
        Pruefe.Wahr(nummern.SequenceEqual(nummern.Order(StringComparer.Ordinal)), "aufsteigend");
        foreach (var f in FehlernummernTabelle.Alle)
        {
            Pruefe.Wahr(Regex.IsMatch(f.Nummer, "^[0-4][1-9]$"), f.Nummer + ": zwei Ziffern in 01-09, 11-19, 21-29, 31-39 oder 41-49");
            Pruefe.Wahr(f.Bedeutung.Length > 10, f.Nummer + ": Bedeutung");
            Pruefe.Falsch(f.Bedeutung.Any(z => z is '\u2013' or '\u2014' or '\u201E' or '\u201C' or '\u201D' or '\u2026'), f.Nummer + ": nur einfache Zeichen");
        }
        // Jede Konstante steht in der Tabelle und jede Zeile hat ihre Konstante.
        var konstanten = typeof(Fehlernummern).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Pruefe.Gleich(konstanten.Count, konstanten.Distinct(StringComparer.Ordinal).Count(), "keine Nummer doppelt vergeben");
        Pruefe.Wahr(konstanten.Order(StringComparer.Ordinal).SequenceEqual(nummern), "Konstanten und Tabelle gleich: " + string.Join(",", konstanten));
        Pruefe.Gleich("Satz. (Fehler 07)", Fehlernummern.Anhaengen("Satz.", "07"), "Form der Nummer");
        Pruefe.Gleich("Satz.", Fehlernummern.Anhaengen("Satz.", null), "ohne Nummer");
    }

    [Test]
    public static void Aufbau_JedeArt_OhneAdresseMitNummer_EinzelheitBleibt()
    {
        foreach (var einzelheit in new[] { Adresse + " antwortet nicht", "[2001:db8::7]:8443 ist nicht erreichbar", "funk.example.org hat keine IPv4-Adresse" })
        {
            foreach (var art in Enum.GetValues<AufbauFehler>())
            {
                foreach (var wieder in new[] { false, true })
                {
                    var was = art + (wieder ? " beim Wiederverbinden" : "") + " mit " + einzelheit;
                    var m = Verbindungstexte.Aufbau(art, einzelheit, wieder);
                    var nummer = art switch
                    {
                        AufbauFehler.Netz => Fehlernummern.ServerNichtErreichbar,
                        AufbauFehler.Tls => Fehlernummern.TlsGescheitert,
                        AufbauFehler.Alpn => Fehlernummern.KeinFunkServer,
                        AufbauFehler.Pin => Fehlernummern.FremderServer,
                        _ => wieder ? Fehlernummern.UnbekannterSchluessel : Fehlernummern.NichtVertrauenswuerdig,
                    };
                    MitNummer(m, nummer, was);
                    Pruefe.Gleich(einzelheit, m.Einzelheit, was + ": Einzelheit unverändert");
                    Pruefe.Gleich(m, Verbindungstexte.Ende(new Sitzungsende("Aufbau: " + art, null, null, null, null, null, art, einzelheit, wieder)),
                        was + ": über das Ende der Sitzung dasselbe");
                }
            }
        }
        Pruefe.Gleich(null, Verbindungstexte.Aufbau(AufbauFehler.Netz, " ").Einzelheit, "leere Einzelheit: null");
        Pruefe.Wahr(Verbindungstexte.Aufbau(AufbauFehler.Netz, Adresse).Text.StartsWith("Server nicht erreichbar", StringComparison.Ordinal), "Netz: sagt die Lage zuerst");
        Pruefe.Falsch(Verbindungstexte.Aufbau(AufbauFehler.PinAlarm, Adresse).Text.Contains(Verbindungstexte.NeuerVersuch, StringComparison.Ordinal),
            "Pin-Alarm bei der Erstverbindung: kein Neuversuch (4.1)");
        Pruefe.Wahr(Verbindungstexte.Aufbau(AufbauFehler.PinAlarm, Adresse, wiederverbindung: true).Text.Contains(Verbindungstexte.NeuerVersuch, StringComparison.Ordinal),
            "beim Wiederverbinden versucht der Client weiter");
    }

    /// <summary>
    /// Seit 0.7.0 löscht Ablehnen den Antrag, und der Server meldet das wartende Gerät mit Bye{ENTFERNT} ab (Sarevoc, 04.10.2026). Ohne
    /// AKTIV-Zeit war es ein Antrag: Der Satz sagt das und wie es weitergeht, nicht "Gerät abgemeldet". GERAET_GESPERRT kommt nur
    /// noch von einer bewussten Sperre und spricht nicht mehr von "abgelehnt".
    /// </summary>
    [Test]
    public static void Ende_EntferntImWarteraum_SagtAntragAbgelehnt()
    {
        var warteraum = Verbindungstexte.Ende(new Sitzungsende("Bye Entfernt", ByeGrund.Entfernt, null, null, null, null));
        Pruefe.Wahr(warteraum.Text.StartsWith("Dein Antrag ist nicht mehr offen", StringComparison.Ordinal)
            && warteraum.Text.Contains("abgelehnt", StringComparison.Ordinal) && warteraum.Text.Contains("Verbinden", StringComparison.Ordinal), warteraum.Text);
        MitNummer(warteraum, Fehlernummern.GeraetEntfernt, "Antrag fort");
        var aktiv = Verbindungstexte.Ende(new Sitzungsende("Bye Entfernt", ByeGrund.Entfernt, null, null, 60_000, null));
        Pruefe.Wahr(aktiv.Text.StartsWith("Dieses Gerät wurde vom Funk abgemeldet", StringComparison.Ordinal), aktiv.Text);
        var gesperrt = Verbindungstexte.Ende(new Sitzungsende("Fehler GERAET_GESPERRT", null, null, Fehlercode.GeraetGesperrt, null, null));
        Pruefe.Wahr(gesperrt.Text.StartsWith("Dieses Gerät ist gesperrt.", StringComparison.Ordinal)
            && !gesperrt.Text.Contains("abgelehnt", StringComparison.Ordinal), gesperrt.Text);
    }

    [Test]
    public static void Ende_JederByeGrundUndFehlercode_NummerNurBeiFehlern()
    {
        foreach (var bye in Enum.GetValues<ByeGrund>())
        {
            var grund = "Bye " + bye;
            var m = Verbindungstexte.Ende(Ende(grund, bye: bye));
            string? nummer = bye switch
            {
                ByeGrund.Wartung or ByeGrund.Verdraengt or ByeGrund.Ersetzt => null,
                ByeGrund.Gesperrt => Fehlernummern.KontoGesperrt,
                ByeGrund.Entzogen => Fehlernummern.GeraetGesperrt,
                ByeGrund.Entfernt => Fehlernummern.GeraetEntfernt,
                ByeGrund.Wartezeit => Fehlernummern.ServerVoll,
                ByeGrund.Protokoll => Fehlernummern.Protokollfehler,
                _ => Fehlernummern.ServerBeendet,
            };
            if (nummer is null)
            {
                OhneNummer(m, "Bye " + bye);
                // Verdrängt: das andere Gerät (Name, Kennung) nur als Einzelheit (Ende_Verdraengt_OhneGeraetenamen_...).
                if (bye != ByeGrund.Verdraengt) Pruefe.Gleich(null, m.Einzelheit, "Bye " + bye + ": normaler Zustand ohne Einzelheit");
            }
            else
            {
                MitNummer(m, nummer, "Bye " + bye);
                Pruefe.Gleich(grund, m.Einzelheit, "Bye " + bye + ": der Grund als Einzelheit");
            }
        }

        foreach (var code in Enum.GetValues<Fehlercode>())
        {
            var name = Fehlercodes.Drahtname(code);
            var m = Verbindungstexte.Ende(Ende("Fehler " + name, fehler: code));
            string? nummer = code switch
            {
                Fehlercode.Verdraengt => null,
                Fehlercode.VersionZuAlt => Fehlernummern.VersionZuAlt,
                Fehlercode.Version => Fehlernummern.VersionPasstNicht,
                Fehlercode.KontoGesperrt => Fehlernummern.KontoGesperrt,
                Fehlercode.GeraetGesperrt => Fehlernummern.GeraetGesperrt,
                Fehlercode.VerdraengenGesperrt => Fehlernummern.WechselGesperrt,
                Fehlercode.ServerVoll => Fehlernummern.ServerVoll,
                Fehlercode.Protokoll => Fehlernummern.Protokollfehler,
                _ => Fehlernummern.Abgelehnt,
            };
            if (nummer is null)
            {
                OhneNummer(m, name);
                continue;
            }
            MitNummer(m, nummer, name);
            Pruefe.Falsch(m.Text.Contains(name, StringComparison.Ordinal), name + ": kein Drahtname im Text: " + m.Text);
            Pruefe.Wahr(m.Einzelheit?.Contains(name, StringComparison.Ordinal) == true, name + ": Drahtname in der Einzelheit: " + m.Einzelheit);
        }
        // Erkennt der Client selbst einen Protokollfehler, steht sein Grund in der Einzelheit, mit dem Code.
        var rahmen = Verbindungstexte.Ende(Ende("Rahmenfehler", fehler: Fehlercode.ZuGross));
        MitNummer(rahmen, Fehlernummern.Abgelehnt, "Rahmenfehler");
        Pruefe.Wahr(rahmen.Einzelheit is { } r && r.Contains("Rahmenfehler", StringComparison.Ordinal) && r.Contains("ZU_GROSS", StringComparison.Ordinal),
            "Grund und Code: " + rahmen.Einzelheit);
    }

    /// <summary>
    /// Gegenprüfung Stufe 2 (Sarevoc, Vorgabe f und Q4): Der Windows-Gerätename steht nur als Zeile in der Antragskarte. Verdrängt ein
    /// anderes Gerät desselben Kontos die Verbindung, nennt der Satz für Funker weder dessen Namen noch dessen Kennung; beides geht
    /// als Einzelheit für Details im Admin-Setup mit.
    /// </summary>
    [Test]
    public static void Ende_Verdraengt_OhneGeraetenamen_NameUndKennungNurInDerEinzelheit()
    {
        var m = Verbindungstexte.Ende(Ende("Bye Verdraengt", bye: ByeGrund.Verdraengt));
        OhneNummer(m, "Verdrängt");
        Pruefe.Falsch(m.Text.Contains("Laptop", StringComparison.Ordinal) || m.Text.Contains("1A2B", StringComparison.Ordinal),
            "kein Gerätename und keine Kennung im Satz: " + m.Text);
        Pruefe.Wahr(m.Text.StartsWith("Dein Konto hat sich auf einem anderen Gerät angemeldet.", StringComparison.Ordinal), "die Lage: " + m.Text);
        Pruefe.Wahr(m.Einzelheit is { } e && e.Contains("Laptop", StringComparison.Ordinal) && e.Contains("1A2B-3C4D-5E6F-7A8B", StringComparison.Ordinal),
            "Name und Kennung in der Einzelheit: " + m.Einzelheit);
        var ohne = Verbindungstexte.Ende(new Sitzungsende("Bye Verdraengt", ByeGrund.Verdraengt, null, null, null, null));
        Pruefe.Gleich(m.Text, ohne.Text, "ohne Angabe des anderen Geräts derselbe Satz");
        Pruefe.Gleich(null, ohne.Einzelheit, "und keine Einzelheit");
    }

    [Test]
    public static void Ende_GruendeDesClients_UndGegenprobe()
    {
        foreach (var (grund, nummer) in new (string, string?)[]
        {
            ("Beendet", null),
            ("Nutzer verbindet neu", null),
            ("6 s ohne Ping-Antwort", Fehlernummern.KeineAntwort),
            ("Verbindungsverlust", Fehlernummern.VerbindungVerloren),
            ("Verbindung geschlossen", Fehlernummern.VerbindungVerloren),
            ("Geschlossen", Fehlernummern.VerbindungVerloren),
            ("Schreibfehler: IOException", Fehlernummern.VerbindungGetrennt),
            ("harte Grenze des hoch-Zählers", Fehlernummern.VerbindungGetrennt),
            ("Interner Fehler: " + Adresse + " " + Pfad, Fehlernummern.VerbindungGetrennt),
        })
        {
            var m = Verbindungstexte.Ende(new Sitzungsende(grund, null, null, null, null, null));
            if (nummer is null)
            {
                OhneNummer(m, grund);
                Pruefe.Gleich("Getrennt.", m.Text, grund + ": selbst getrennt");
                Pruefe.Gleich(null, m.Einzelheit, grund + ": ohne Einzelheit");
                continue;
            }
            MitNummer(m, nummer, grund);
            Pruefe.Gleich(grund, m.Einzelheit, grund + ": der Grund als Einzelheit");
        }
        // Eine Meldung, die versehentlich in einen Text gerät, bringt die Einzelheit nicht mit.
        var aufbau = Verbindungstexte.Aufbau(AufbauFehler.Tls, "TLS mit " + Adresse + " gescheitert: " + Pfad);
        Pruefe.Gleich(aufbau.Text, aufbau.ToString(), "ToString ist nur der Satz");
        Pruefe.Gleich(aufbau.Text, "" + aufbau, "auch beim Zusammensetzen");
    }

    [Test]
    public static void InterneFehler_OhneAusnahmetext_MitNummer()
    {
        var meldung = "Verbindungsschleife: " + Pfad + " an " + Adresse + " kaputt";
        foreach (var z in Enum.GetValues<ClientZustand>())
        {
            var m = FunkClientDienst.Fehleranzeige(z, meldung);
            var nummer = z switch
            {
                ClientZustand.Getrennt or ClientZustand.Verbinden => Fehlernummern.InternVerbinden,
                ClientZustand.Registrierung or ClientZustand.Warteraum or ClientZustand.Sync => Fehlernummern.InternAnmeldung,
                _ => Fehlernummern.InternSitzung,
            };
            MitNummer(m, nummer, z.ToString());
            Pruefe.Wahr(m.Text.StartsWith("Interner Fehler ", StringComparison.Ordinal), z + ": " + m.Text);
            Pruefe.Gleich(meldung, m.Einzelheit, z + ": Ausnahmetext als Einzelheit");
        }

        // Vor dem Verbinden (Verbindungsfabrik): eine unerwartete Ausnahme wird zu 44, eine vorbereitete Meldung geht durch.
        var io = new IOException("Zugriff verweigert: " + Pfad);
        var v = Verbindungstexte.Vorbereitung(io);
        MitNummer(v, Fehlernummern.InternVorbereitung, "unerwartete Ausnahme");
        Pruefe.Gleich(io.Message, v.Einzelheit, "Ausnahmetext als Einzelheit");
        var vorbereitet = new Verbindungsmeldung("Vorbereitet. (Fehler 17)", Adresse);
        Pruefe.Gleich(vorbereitet, Verbindungstexte.Vorbereitung(new VorbereitungException(vorbereitet)), "vorbereitete Meldung unverändert");
        Pruefe.Gleich(vorbereitet.Text, new VorbereitungException(vorbereitet).Message, "Message ist der Satz");
    }

    /// <summary>Ein Schlüsselspeicher, der wie ein gesperrter Speicher von Windows scheitert.</summary>
    private sealed class GesperrterSpeicher : IGeraeteschluesselSpeicher
    {
        public Geraeteschluessel? Laden() => throw new CryptographicException("Schlüsselspeicher " + Pfad + " gesperrt");

        public Geraeteschluessel Erzeugen() => throw new CryptographicException("nie erreicht");

        public void Loeschen()
        {
        }
    }

    [Test]
    public static void Verbindungsfabrik_ProfilEinstellungUndSchluessel_NurInDerEinzelheit()
    {
        using var o = new Testordner();
        using var k = new OberflaecheProfilTests.Schluessel();

        // Serverprofil fehlt: die Nummer aus der Profilprüfung im Text, der Pfad nur in der Einzelheit.
        var fehlt = Pruefe.Wirft<VorbereitungException>(() => Verbindungsfabrik.Bauen(new VerbindungsEinstellungen(), null, profil: new Profilquelle(Pfad, [k.Spki])),
            "Profil fehlt");
        MitNummer(fehlt.Meldung, Fehlernummern.ProfildateiFehlt, "Profil fehlt");
        Pruefe.Gleich(Pfad, fehlt.Meldung.Einzelheit, "Pfad als Einzelheit");
        Pruefe.Gleich(fehlt.Meldung.Text, fehlt.Message, "Message ist der Satz");

        // Ungültige Adresse in den Einstellungen.
        var profil = new Serverprofil("Meine Staffel", 1, 0, 1, [new ProfilHost("funk.example.org", true, false)], 8443,
            [Fingerabdruck.AusSpki([1]), Fingerabdruck.AusSpki([2])], []);
        var eingabe = "https://" + Adresse;
        var adresse = Pruefe.Wirft<VorbereitungException>(() => Verbindungsfabrik.AufbauOptionen(new VerbindungsEinstellungen { Adresse = eingabe }, profil), "Adresse mit https");
        MitNummer(adresse.Meldung, Fehlernummern.EinstellungUngueltig, "Adresse");
        Pruefe.Wahr(adresse.Meldung.Einzelheit is { } e && e.Contains(eingabe, StringComparison.Ordinal) && e.Contains("ohne http", StringComparison.Ordinal),
            "Eingabe und Grund in der Einzelheit: " + adresse.Meldung.Einzelheit);

        // Der Geräteschlüssel lässt sich nicht öffnen.
        var datei = o.Datei("profil.json");
        OberflaecheProfilTests.ProfilSchreiben(datei, k);
        var schluessel = Pruefe.Wirft<VorbereitungException>(() => Verbindungsfabrik.Bauen(new VerbindungsEinstellungen(), null, speicher: new GesperrterSpeicher(),
            profil: new Profilquelle(datei, [k.Spki])), "Schlüssel gesperrt");
        MitNummer(schluessel.Meldung, Fehlernummern.GeraeteschluesselFehlt, "Geräteschlüssel");
        Pruefe.Gleich("Schlüsselspeicher " + Pfad + " gesperrt", schluessel.Meldung.Einzelheit, "Meldung von Windows als Einzelheit");
        Pruefe.Wahr(schluessel.InnerException is CryptographicException, "die Ursache bleibt dran");
    }

    /// <summary>Die neun Profilbefunde mit ihren Nummern, wie die Profilprüfung sie liefert.</summary>
    private static List<(string Was, Profilbefund Befund, string Nummer, string? Einzelheit)> Profilbefunde(Testordner o, OberflaecheProfilTests.Schluessel k,
        Action<Action> gesperrt)
    {
        using var fremd = new OberflaecheProfilTests.Schluessel();
        var jetzt = DateTimeOffset.UtcNow;
        var profil = o.Datei("profil.json");
        OberflaecheProfilTests.ProfilSchreiben(profil, k);
        var gross = o.Datei("gross.json");
        File.WriteAllBytes(gross, new byte[Profilpruefung.MaxBytes + 1]);
        var daten = o.Datei("daten.json");
        File.WriteAllBytes(daten, new Serverprofil("Meine Staffel", 1, 0, 1, [new ProfilHost("funk.example.org", true, false)], 8443,
            [Fingerabdruck.AusSpki([1]), Fingerabdruck.AusSpki([2])], []).AlsJson());
        var muell = o.Datei("muell.json");
        File.WriteAllText(muell, "{\"daten\":\"AA==\"}");
        var inhalt = o.Datei("inhalt.json");
        File.WriteAllBytes(inhalt, SigniertesProfil.Signieren("{}"u8, k.Privat).AlsJson());

        var liste = new List<(string, Profilbefund, string, string?)>
        {
            ("ohne Datei", Profilpruefung.Pruefen(null, [k.Spki], jetzt), Fehlernummern.ProfilFehlt, null),
            ("Datei fehlt", Profilpruefung.Pruefen(Pfad, [k.Spki], jetzt), Fehlernummern.ProfildateiFehlt, Pfad),
            ("zu groß", Profilpruefung.Pruefen(gross, [k.Spki], jetzt), Fehlernummern.ProfilZuGross, gross),
            ("unsigniert", Profilpruefung.Pruefen(daten, [k.Spki], jetzt), Fehlernummern.ProfilUnsigniert, daten),
            ("kein Profil", Profilpruefung.Pruefen(muell, [k.Spki], jetzt), Fehlernummern.ProfilKeinProfil, muell),
            ("ohne Schlüssel", Profilpruefung.Pruefen(profil, [], jetzt), Fehlernummern.ProfilschluesselFehlt, null),
            ("Inhalt ungültig", Profilpruefung.Pruefen(inhalt, [k.Spki], jetzt), Fehlernummern.ProfilInhaltUngueltig, inhalt),
            ("fremd signiert", Profilpruefung.Pruefen(profil, [fremd.Spki], jetzt), Fehlernummern.ProfilFremd, profil),
        };
        // Gesperrt: Windows meldet das mit dem Pfad; der steht dann nur in der Einzelheit.
        gesperrt(() => liste.Add(("gesperrt", Profilpruefung.Pruefen(profil, [k.Spki], jetzt), Fehlernummern.ProfilUnlesbar, null)));
        return liste;
    }

    private static void Gesperrt(string datei, Action aktion)
    {
        using var sperre = new FileStream(datei, FileMode.Open, FileAccess.Read, FileShare.None);
        aktion();
    }

    [Test]
    public static void Profilpruefung_JederFehler_NummerImFeld_PfadInDerEinzelheit()
    {
        using var o = new Testordner();
        using var k = new OberflaecheProfilTests.Schluessel();
        var befunde = Profilbefunde(o, k, a => Gesperrt(o.Datei("profil.json"), a));
        Pruefe.Gleich(9, befunde.Count, "neun Fälle");
        foreach (var (was, b, nummer, einzelheit) in befunde)
        {
            Pruefe.Wahr(b.Fehler is not null && b.Profil is null, was + ": ein Fehler, kein Profil");
            Funkertext(b.Fehler!, was);
            Pruefe.Gleich(nummer, b.Nummer, was + ": Nummer im Feld");
            Pruefe.Falsch(b.Fehler!.Contains("(Fehler", StringComparison.Ordinal), was + ": die Nummer hängt die Oberfläche an, nicht der Text: " + b.Fehler);
            // Die Oberfläche zeigt Funkern nur den ersten Satz (bis ". "); der muss allein stehen können.
            var ende = b.Fehler.IndexOf(". ", StringComparison.Ordinal);
            var erster = ende < 0 ? b.Fehler : b.Fehler[..(ende + 1)];
            Pruefe.Wahr(erster.EndsWith('.') && erster.Length >= 25 && char.IsUpper(erster[0]), was + ": erster Satz vollständig: " + erster);
            if (was == "gesperrt")
            {
                Pruefe.Wahr(b.Einzelheit is { Length: > 0 } e && e.Contains(o.Datei("profil.json"), StringComparison.OrdinalIgnoreCase),
                    was + ": Meldung von Windows mit Pfad in der Einzelheit: " + b.Einzelheit);
            }
            else if (was == "zu groß")
            {
                Pruefe.Wahr(b.Einzelheit is { } e && e.StartsWith(einzelheit!, StringComparison.Ordinal), was + ": Pfad in der Einzelheit: " + b.Einzelheit);
            }
            else
            {
                Pruefe.Gleich(einzelheit, b.Einzelheit, was + ": Einzelheit");
            }
        }
        Pruefe.Gleich(9, befunde.Select(b => b.Nummer).Distinct(StringComparer.Ordinal).Count(), "neun verschiedene Nummern");
        Pruefe.Wahr(befunde.All(b => b.Nummer[0] == '0'), "alle im Kreis 01-09");

        var gut = Profilpruefung.Pruefen(o.Datei("profil.json"), [k.Spki], DateTimeOffset.UtcNow);
        Pruefe.Wahr(gut.Gueltig && gut.Nummer is null && gut.Einzelheit is null, "gültig: keine Nummer, keine Einzelheit");
    }

    [Test]
    public static async Task Dienst_SetztHinweisUndEinzelheitGetrennt()
    {
        // Unerwartete Ausnahme der Fabrik: Nummer 44, der Ausnahmetext nur in der Einzelheit.
        await using (var d = new FunkClientDienst(() => throw new IOException("Datei " + Pfad + " gesperrt")))
        {
            await d.VerbindenAsync();
            MitNummer(new Verbindungsmeldung(d.Lage.Hinweis!, d.Lage.HinweisEinzelheit), Fehlernummern.InternVorbereitung, "Fabrik wirft");
            Pruefe.Gleich("Datei " + Pfad + " gesperrt", d.Lage.HinweisEinzelheit, "Ausnahmetext als Einzelheit");
        }

        // Die echte Fabrik ohne Serverprofil: Nummer 02 im Hinweis, der Pfad nur in der Einzelheit.
        using var k = new OberflaecheProfilTests.Schluessel();
        await using (var d = new FunkClientDienst(() => Verbindungsfabrik.Bauen(new VerbindungsEinstellungen(), null, profil: new Profilquelle(Pfad, [k.Spki]))))
        {
            await d.VerbindenAsync();
            MitNummer(new Verbindungsmeldung(d.Lage.Hinweis!, d.Lage.HinweisEinzelheit), Fehlernummern.ProfildateiFehlt, "Profil fehlt");
            Pruefe.Gleich(Pfad, d.Lage.HinweisEinzelheit, "Pfad als Einzelheit");
        }

        // Aufbau scheitert: der Satz aus Verbindungstexte, Adresse:Port nur in der Einzelheit; Trennen räumt beides ab.
        using var id = Geraeteidentitaet.Fluechtig();
        await using var dienst = new FunkClientDienst(() => new FunkAnbindung(new FunkClient(new FunkClientOptionen
        {
            Aufbau = new DelegatAufbau((_, _) => Task.FromException<Steuerverbindung>(new AufbauException(AufbauFehler.Netz, Adresse + " antwortet nicht"))),
            Identitaet = id,
            Zufall = () => 0.0,
        }), null, "Meine Staffel", id.Fingerabdruck.Anzeige, "Software"));
        await dienst.VerbindenAsync();
        var bis = DateTime.UtcNow.AddSeconds(5);
        while (dienst.Lage.HinweisEinzelheit is null && DateTime.UtcNow < bis) await Task.Delay(10);
        MitNummer(new Verbindungsmeldung(dienst.Lage.Hinweis!, dienst.Lage.HinweisEinzelheit), Fehlernummern.ServerNichtErreichbar, "Aufbau");
        Pruefe.Gleich(Adresse + " antwortet nicht", dienst.Lage.HinweisEinzelheit, "Einzelheit des Aufbaus");
        await dienst.TrennenAsync();
        Pruefe.Gleich("Getrennt", dienst.Lage.Hinweis, "selbst getrennt");
        Pruefe.Gleich(null, dienst.Lage.HinweisEinzelheit, "ohne Einzelheit");
    }

    [Test]
    public static void Fehlernummern_JedeWirdBenutzt()
    {
        var benutzt = new HashSet<string>(StringComparer.Ordinal);
        void Merken(string text)
        {
            if (Nummernende.Match(text) is { Success: true } t) benutzt.Add(t.Groups[1].Value);
        }
        foreach (var art in Enum.GetValues<AufbauFehler>())
        {
            Merken(Verbindungstexte.Aufbau(art, Adresse).Text);
            Merken(Verbindungstexte.Aufbau(art, Adresse, wiederverbindung: true).Text);
        }
        foreach (var bye in Enum.GetValues<ByeGrund>()) Merken(Verbindungstexte.Ende(Ende("Bye " + bye, bye: bye)).Text);
        foreach (var code in Enum.GetValues<Fehlercode>()) Merken(Verbindungstexte.Ende(Ende("Fehler " + code, fehler: code)).Text);
        foreach (var grund in new[] { "6 s ohne Ping-Antwort", "Verbindungsverlust", "Schreibfehler: IOException" })
            Merken(Verbindungstexte.Ende(new Sitzungsende(grund, null, null, null, null, null)).Text);
        foreach (var z in Enum.GetValues<ClientZustand>()) Merken(FunkClientDienst.Fehleranzeige(z, "x").Text);
        Merken(Verbindungstexte.Vorbereitung(new IOException("x")).Text);
        var profil = new Serverprofil("Meine Staffel", 1, 0, 1, [new ProfilHost("funk.example.org", true, false)], 8443,
            [Fingerabdruck.AusSpki([1]), Fingerabdruck.AusSpki([2])], []);
        try
        {
            Verbindungsfabrik.AufbauOptionen(new VerbindungsEinstellungen { Adresse = "https://x" }, profil);
        }
        catch (InvalidOperationException e)
        {
            Merken(e.Message);
        }
        try
        {
            using var k = new OberflaecheProfilTests.Schluessel();
            using var o = new Testordner();
            var datei = o.Datei("profil.json");
            OberflaecheProfilTests.ProfilSchreiben(datei, k);
            _ = Verbindungsfabrik.Bauen(new VerbindungsEinstellungen(), null, speicher: new GesperrterSpeicher(), profil: new Profilquelle(datei, [k.Spki]));
        }
        catch (InvalidOperationException e)
        {
            Merken(e.Message);
        }
        using (var o = new Testordner())
        using (var k = new OberflaecheProfilTests.Schluessel())
        {
            foreach (var b in Profilbefunde(o, k, aktion => Gesperrt(o.Datei("profil.json"), aktion)))
            {
                if (b.Befund.Nummer is { } n) benutzt.Add(n);
            }
        }
        var tabelle = FehlernummernTabelle.Alle.Select(f => f.Nummer).ToHashSet(StringComparer.Ordinal);
        Pruefe.Wahr(tabelle.SetEquals(benutzt), "jede Nummer der Tabelle kommt vor, keine außerhalb: fehlt "
            + string.Join(",", tabelle.Except(benutzt).Order(StringComparer.Ordinal)) + ", fremd " + string.Join(",", benutzt.Except(tabelle).Order(StringComparer.Ordinal)));
    }
}
