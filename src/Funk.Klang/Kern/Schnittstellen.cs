// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Kern;

public interface IDekodierer : IDisposable {
  int Abtastrate { get; }                 // immer 48000
  int Dekodieren(ReadOnlySpan<byte> paket, Span<float> ziel);        // liefert Samples, ziel >= 960 fuer 20 ms
  int DekodierenFec(ReadOnlySpan<byte> folgepaket, Span<float> ziel, int fehlendeSamples); // LBRR aus dem Folgepaket
  int Verdecken(Span<float> ziel, int fehlendeSamples);             // PLC ohne Paket (Deep PLC ab Komplexitaet 5)
}
public interface IKodierer : IDisposable {
  int Kodieren(ReadOnlySpan<float> pcm20ms, Span<byte> ziel);        // 960 Samples mono 48 kHz -> Paketlaenge
  void SetzeVerlustHinweis(int prozent);
}
