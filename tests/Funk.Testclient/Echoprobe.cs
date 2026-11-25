// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Globalization;
using Funk.ClientKern;
using Funk.ClientKern.Echo;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.Testclient;

/// <summary>Ein Paket aus dem Echo-Netz mit Empfangszeit.</summary>
internal sealed record Echopaket(uint Absender, ushort Phase, uint Frame, Kennzeichen Kennzeichen, byte[] Opus, bool Ende, long AnkunftMs);

/// <summary>
/// Befehl <c>echo-probe</c>: meldet sich an (Testzugang auf Loopback oder TLS mit Profil), spricht eine synthetische
/// Sprechphase ins Echo-Netz und prüft, dass sie vollständig, in derselben Reihenfolge, als eine Phase mit BEGINN und ENDE
/// und frühestens 500 ms nach dem eigenen ENDE zurückkommt. Kein Audiogerät, kein Fenster. Exitcode 0 heißt bestanden.
/// </summary>
internal sealed class Echoprobe : ISprachEmpfaenger
{
    /// <summary>Kennung im zweiten Byte der Opus-Nutzlast, damit die eigenen Frames im Echo erkennbar sind.</summary>
    private const byte Kennung = (byte)'E';

    private readonly ConcurrentQueue<Echopaket> _pakete = new();
    private volatile ushort _netz;

    public void Empfangen(in SprachEmpfang e)
    {
        var p = e.Paket;
        var netz = _netz;
        if (netz == 0 || p.Art != Spruchart.Netz) return;
        for (var i = 0; i < p.NetzAnzahl; i++)
        {
            if (p.Netz(i) != netz) continue;
            _pakete.Enqueue(new Echopaket(p.Absender, p.Sprechphase, p.FrameNr, p.Kennzeichen, p.Opus.ToArray(), p.IstEnde, Monouhr.MsAus(e.EmpfangenTakte)));
            return;
        }
    }

    public void ZeitlinieBeendet(uint absender, ushort sprechphase)
    {
    }

    /// <summary>Opus mit TOC 0xF8 (CELT FB 20 ms mono, 5.13), Kennung, Lauf und Nummer, Rest Füllbytes: 120 Byte wie echte Sprache.</summary>
    private static byte[] Rahmen(byte lauf, int nummer)
    {
        var o = new byte[120];
        o[0] = 0xF8;
        o[1] = Kennung;
        o[2] = lauf;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(o.AsSpan(3), nummer);
        o.AsSpan(7).Fill(0x3C);
        return o;
    }

    public static async Task<int> LaufenAsync(IVerbindungsaufbau aufbau, Geraeteidentitaet identitaet, string netzName, int frames, int preroll,
        TimeSpan freigabeFrist, TextWriter aus)
    {
        var probe = new Echoprobe();
        await using var client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = aufbau,
            Identitaet = identitaet,
            ClientName = "funk-testclient",
            ClientVersion = "1.0.0",
            Geraetename = "Echo-Probe",
            Rueckfrage = (_, _) => Task.FromResult(true),
            Empfaenger = probe,
        });
        client.CodeErhalten += (_, c) =>
        {
            // Die ganzen Befehle wie in HEIMSERVER-PROXMOX.md, Abschnitt 11, zum Kopieren; die Zahl muss eine andere sein als die
            // des Echo-Funkers, sonst landet die Probe in seinem Konto und verdrängt ihn.
            aus.WriteLine("Gerät unbekannt. Registrierungscode " + c.Code + " (5 Minuten gültig). In einer zweiten Shell:");
            aus.WriteLine("  dotnet /opt/funk/funk-admin.dll verknuepfen --code " + c.Code
                + " --discord <eigene Zahl, nicht die des Echo-Funkers> --benutzername echo-probe --name Probe --konfig /etc/funk/server.json");
            aus.WriteLine("  dotnet /opt/funk/funk-admin.dll freigeben " + identitaet.Fingerabdruck.Anzeige + " --konfig /etc/funk/server.json");
        };
        client.Starten();
        if (!await client.AktivAsync(freigabeFrist).ConfigureAwait(false))
        {
            aus.WriteLine("NICHT BESTANDEN: nicht aktiv (Zustand " + client.Zustand + ", " + client.LetztesEnde?.Grund + ")");
            return 1;
        }
        var lage = client.Lage;
        if (lage.NetzListe is not { } liste || EchoFunker.NetzAufloesen(liste, netzName) is not { } netz)
        {
            aus.WriteLine("NICHT BESTANDEN: Kanal '" + netzName + "' gibt es nicht.");
            return 1;
        }
        probe._netz = netz.Id;
        if (lage.FeldMitNetz(netz.Id) is null && netz.Offen == NetzOffen.HoerenSenden && lage.Mitgliedschaften is { } m)
        {
            var frei = Enumerable.Range(0, 16).Select(i => (byte)i).First(i => m.Felder.All(f => f.Feld != i));
            await client.FeldSetzenAsync(frei, netz.Id).ConfigureAwait(false);
            await client.WartenAufAsync(_ => client.Lage.FeldMitNetz(netz.Id) is not null, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        if (client.Lage.FeldMitNetz(netz.Id) is not { Hoeren: true, Senden: true })
        {
            aus.WriteLine("NICHT BESTANDEN: kein Feld mit hören und senden im Kanal '" + netz.Name + "'.");
            return 1;
        }
        var echos = lage.Praesenz.Values.Where(p => p.KontoId != lage.KontoId && p.Online).Select(p => p.Name).ToList();
        aus.WriteLine("Aktiv als Konto " + lage.KontoId.ToString(CultureInfo.InvariantCulture) + ", Kanal " + netz.Name + " (ID "
            + netz.Id.ToString(CultureInfo.InvariantCulture) + "), online: " + string.Join(", ", echos));

        var lauf = (byte)Random.Shared.Next(1, 256);
        preroll = Math.Clamp(preroll, 0, Math.Min(SprechphasenGeber.MaxPreroll, frames - 1));
        if (!client.Sprechen.Beginnen(Sendeziel.Netz(netz.Id), sprachaktivierung: true))
        {
            aus.WriteLine("NICHT BESTANDEN: Senden nicht möglich.");
            return 1;
        }
        for (var i = 0; i < preroll; i++) client.Sprechen.Senden(Rahmen(lauf, i), preroll: true);
        await new Taktgeber(TimeSpan.FromMilliseconds(20)).LaufenAsync(frames - preroll,
            (k, _) => client.Sprechen.Senden(Rahmen(lauf, preroll + k)), CancellationToken.None).ConfigureAwait(false);
        client.Sprechen.Beenden();
        var ende = Monouhr.Ms();
        aus.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Gesprochen: {frames} Frames ({frames * 20} ms), davon {preroll} Pre-roll, VA"));

        var frist = Monouhr.Ms() + frames * 20 + 5000;
        List<Echopaket> eigene = [];
        while (Monouhr.Ms() < frist)
        {
            eigene = [.. probe._pakete.Where(p => p.Ende || (p.Opus.Length > 2 && p.Opus[1] == Kennung && p.Opus[2] == lauf))];
            if (eigene.Any(p => !p.Ende) && eigene.Any(p => p.Ende && p.Absender == eigene.First(x => !x.Ende).Absender && p.Phase == eigene.First(x => !x.Ende).Phase)) break;
            await Task.Delay(20).ConfigureAwait(false);
        }
        var audio = eigene.Where(p => !p.Ende).ToList();
        if (audio.Count == 0)
        {
            aus.WriteLine("NICHT BESTANDEN: kein Echo binnen " + (frames * 20 + 5000).ToString(CultureInfo.InvariantCulture) + " ms. Läuft ein Echo-Funker im Kanal?");
            return 1;
        }
        var absender = audio[0].Absender;
        var phase = audio[0].Phase;
        var vollstaendig = audio.Count == frames;
        var reihenfolge = audio.Select((p, i) => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(p.Opus.AsSpan(3)) == i).All(x => x);
        var einePhase = audio.All(p => p.Absender == absender && p.Phase == phase);
        var beginn = (audio[0].Kennzeichen & Kennzeichen.Beginn) != 0;
        var mitEnde = eigene.Any(p => p.Ende && p.Absender == absender && p.Phase == phase);
        var pause = audio[0].AnkunftMs - ende;
        var dauer = audio[^1].AnkunftMs - audio[0].AnkunftMs;
        var name = lage.Praesenz.TryGetValue(absender, out var e) ? e.Name : "?";
        aus.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Echo von Konto {absender} ({name}): {audio.Count} von {frames} Frames, Reihenfolge {(reihenfolge ? "richtig" : "FALSCH")}, eine Phase {(einePhase ? "ja" : "NEIN")}, BEGINN {(beginn ? "ja" : "NEIN")}, ENDE {(mitEnde ? "ja" : "NEIN")}, Pause nach dem eigenen ENDE {pause} ms, Dauer {dauer} ms"));
        var ok = vollstaendig && reihenfolge && einePhase && beginn && mitEnde && pause >= 498;
        aus.WriteLine(ok ? "BESTANDEN" : "NICHT BESTANDEN");
        return ok ? 0 : 1;
    }
}
