// JG TacRelay - Maintainer: Sarevoc
/* Rauchtest fuer speexdsp_resampler.dll (JG TacRelay)

   Laedt die DLL zur Laufzeit ueber LoadLibraryExW und sucht jeden
   Einsprungpunkt per GetProcAddress ueber den Namen - so, wie LibraryImport
   in .NET es spaeter tut. Gegen die Importbibliothek wird nicht gelinkt.

   Aufruf:   rauchtest.exe <Pfad zur speexdsp_resampler.dll>
   Exitcode: 0 = alle Pruefungen bestanden, 1 = Pruefung fehlgeschlagen,
             2 = DLL oder Einsprungpunkte fehlen.

   Ausgabe bewusst in ASCII, damit sie in jeder Konsole lesbar bleibt. */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

typedef unsigned int u32;
typedef struct SpeexResamplerState_ SpeexResamplerState;

/* Signaturen wie in include\speex\speex_resampler.h (spx_uint32_t = unsigned int). */
typedef SpeexResamplerState *(*fn_init)(u32 nb_channels, u32 in_rate, u32 out_rate, int quality, int *err);
typedef void (*fn_destroy)(SpeexResamplerState *st);
typedef int (*fn_process_float)(SpeexResamplerState *st, u32 channel_index, const float *in, u32 *in_len, float *out, u32 *out_len);
typedef int (*fn_process_interleaved_float)(SpeexResamplerState *st, const float *in, u32 *in_len, float *out, u32 *out_len);
typedef int (*fn_set_rate_frac)(SpeexResamplerState *st, u32 ratio_num, u32 ratio_den, u32 in_rate, u32 out_rate);
typedef int (*fn_st_int)(SpeexResamplerState *st);
typedef const char *(*fn_strerror)(int err);

static struct {
   fn_init init;
   fn_destroy destroy;
   fn_process_float process_float;
   fn_process_interleaved_float process_interleaved_float;
   fn_set_rate_frac set_rate_frac;
   fn_st_int get_input_latency;
   fn_st_int get_output_latency;
   fn_st_int reset_mem;
   fn_st_int skip_zeros;
   fn_strerror strerror_;
} R;

static int g_pruefungen = 0;
static int g_fehler = 0;

static void pruefe(int ok, const char *was)
{
   g_pruefungen++;
   if (!ok) {
      g_fehler++;
      printf("    FEHLER: %s\n", was);
   }
}

/* ------------------------------------------------------------------ Laden */

static FARPROC hole(HMODULE h, const char *name, int *fehlend)
{
   FARPROC p = GetProcAddress(h, name);
   if (!p) {
      printf("  fehlt: %s\n", name);
      (*fehlend)++;
   }
   return p;
}

static int laden(const wchar_t *pfad)
{
   int fehlend = 0;
   wchar_t geladen[MAX_PATH];
   HMODULE h = LoadLibraryExW(pfad, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
   if (!h) {
      printf("LoadLibraryExW fehlgeschlagen, Windows-Fehler %lu\n", GetLastError());
      return 0;
   }
   GetModuleFileNameW(h, geladen, MAX_PATH);
   printf("DLL geladen: %ls\n", geladen);

   R.init                      = (fn_init)                     hole(h, "speex_resampler_init", &fehlend);
   R.destroy                   = (fn_destroy)                  hole(h, "speex_resampler_destroy", &fehlend);
   R.process_float             = (fn_process_float)            hole(h, "speex_resampler_process_float", &fehlend);
   R.process_interleaved_float = (fn_process_interleaved_float)hole(h, "speex_resampler_process_interleaved_float", &fehlend);
   R.set_rate_frac             = (fn_set_rate_frac)            hole(h, "speex_resampler_set_rate_frac", &fehlend);
   R.get_input_latency         = (fn_st_int)                   hole(h, "speex_resampler_get_input_latency", &fehlend);
   R.get_output_latency        = (fn_st_int)                   hole(h, "speex_resampler_get_output_latency", &fehlend);
   R.reset_mem                 = (fn_st_int)                   hole(h, "speex_resampler_reset_mem", &fehlend);
   R.skip_zeros                = (fn_st_int)                   hole(h, "speex_resampler_skip_zeros", &fehlend);
   R.strerror_                 = (fn_strerror)                 hole(h, "speex_resampler_strerror", &fehlend);

   printf("Einsprungpunkte per Name gefunden: %d von 10\n", 10 - fehlend);
   return fehlend == 0;
}

/* ---------------------------------------------------------------- Signale */

static void sinus(float *x, u32 n, u32 stride, double f, double fs, double amp)
{
   u32 i;
   for (i = 0; i < n; i++)
      x[(size_t)i * stride] = (float)(amp * sin(2.0 * M_PI * f * (double)i / fs));
}

/* Kleinste Quadrate: y[i] ~ a*sin(w i) + b*cos(w i) fuer i in [von, bis).
   Liefert die Amplitude und den Abstand zwischen angepasstem Sinus und Rest
   in dB. Die Phase ist frei, die Latenz des Resamplers stoert also nicht. */
static void sinus_anpassen(const float *y, u32 stride, u32 von, u32 bis, double f, double fs,
                           double *amp, double *abstand_db)
{
   double w = 2.0 * M_PI * f / fs;
   double sss = 0, scc = 0, ssc = 0, sys = 0, syc = 0, det, a, b, sig = 0, rest = 0;
   u32 i;
   for (i = von; i < bis; i++) {
      double s = sin(w * i), c = cos(w * i), v = y[(size_t)i * stride];
      sss += s * s; scc += c * c; ssc += s * c; sys += v * s; syc += v * c;
   }
   det = sss * scc - ssc * ssc;
   a = (sys * scc - syc * ssc) / det;
   b = (syc * sss - sys * ssc) / det;
   for (i = von; i < bis; i++) {
      double m = a * sin(w * i) + b * cos(w * i), v = y[(size_t)i * stride];
      sig += m * m;
      rest += (v - m) * (v - m);
   }
   *amp = sqrt(a * a + b * b);
   *abstand_db = rest > 0 ? 10.0 * log10(sig / rest) : 300.0;
}

static u32 spitze(const float *y, u32 n)
{
   u32 i, best = 0;
   for (i = 1; i < n; i++)
      if (fabsf(y[i]) > fabsf(y[best])) best = i;
   return best;
}

/* Gibt n Eingangswerte in Bloecken zu `block` Werten an process_float
   (Kanal 0) und haengt die Ausgabe an `aus` an. Liefert die Zahl der
   erzeugten Werte oder -1 bei Fehler. */
static long durchschieben(SpeexResamplerState *st, const float *ein, u32 n, u32 block, float *aus, u32 platz)
{
   u32 pos = 0, erzeugt = 0;
   while (pos < n) {
      u32 ilen = (n - pos < block) ? n - pos : block;
      u32 olen = platz - erzeugt;
      int e = R.process_float(st, 0, ein + pos, &ilen, aus + erzeugt, &olen);
      if (e != 0) {
         printf("    process_float: %s\n", R.strerror_(e));
         return -1;
      }
      if (ilen == 0 && olen == 0) {
         printf("    process_float: kein Fortschritt\n");
         return -1;
      }
      pos += ilen;
      erzeugt += olen;
   }
   return (long)erzeugt;
}

static double sekunden(LARGE_INTEGER t0, LARGE_INTEGER t1)
{
   LARGE_INTEGER f;
   QueryPerformanceFrequency(&f);
   return (double)(t1.QuadPart - t0.QuadPart) / (double)f.QuadPart;
}

/* Mindestabstand Sinus zu Rest, abhaengig von der Qualitaet. */
static double schwelle_db(int q)
{
   return q >= 10 ? 90.0 : 60.0;
}

/* ------------------------------------------------------------ Einzelfall */

/* Impuls bei 10 ms, Flush mit Nullen; liefert die Position der Spitze. */
static long impuls_spitze(SpeexResamplerState *st, u32 ein_rate, float *ein, float *null, float *aus, u32 platz)
{
   u32 n = ein_rate / 10, k0 = ein_rate / 100, block = ein_rate / 100;
   long a, b;
   memset(ein, 0, sizeof(float) * n);
   ein[k0] = 1.0f;
   a = durchschieben(st, ein, n, block, aus, platz);
   if (a < 0) return -1;
   b = durchschieben(st, null, (u32)R.get_input_latency(st), block, aus + a, platz - (u32)a);
   if (b < 0) return -1;
   return (long)spitze(aus, (u32)(a + b));
}

static void fall(u32 ein_rate, u32 aus_rate, int q)
{
   int err = -1;
   SpeexResamplerState *st;
   u32 n = ein_rate, block = ein_rate / 100, platz = aus_rate * 2 + 8192;
   float *ein, *null, *aus;
   long signal, nachlauf, p;
   int lat_ein, lat_aus;
   double soll, soll_p, amp, abstand, dauer;
   LARGE_INTEGER t0, t1;

   printf("\n[%u -> %u Hz, Qualitaet %d, mono]\n", ein_rate, aus_rate, q);
   st = R.init(1, ein_rate, aus_rate, q, &err);
   pruefe(st != NULL && err == 0, "speex_resampler_init");
   if (!st) return;

   ein = calloc(n, sizeof(float));
   null = calloc(n, sizeof(float));
   aus = calloc(platz, sizeof(float));

   lat_ein = R.get_input_latency(st);
   lat_aus = R.get_output_latency(st);
   printf("  Latenz: Eingang %d Werte, Ausgang %d Werte (%.3f ms)\n",
          lat_ein, lat_aus, 1000.0 * lat_aus / aus_rate);

   /* 1 s Sinus 1 kHz, Amplitude 0.5, in 10-ms-Bloecken, danach Flush. */
   sinus(ein, n, 1, 1000.0, ein_rate, 0.5);
   QueryPerformanceCounter(&t0);
   signal = durchschieben(st, ein, n, block, aus, platz);
   QueryPerformanceCounter(&t1);
   dauer = sekunden(t0, t1);
   nachlauf = signal >= 0 ? durchschieben(st, null, (u32)lat_ein, block, aus + signal, platz - (u32)signal) : -1;
   pruefe(signal >= 0 && nachlauf >= 0, "process_float lieferte Fehler");
   if (signal < 0 || nachlauf < 0) goto ende;

   soll = (double)n * aus_rate / ein_rate;
   printf("  Ausgabelaenge: %ld Werte aus %u (Soll %.1f), Nachlauf +%ld, zusammen %ld\n",
          signal, n, soll, nachlauf, signal + nachlauf);
   pruefe(fabs((double)signal - soll) <= 1.0, "Ausgabelaenge weicht vom Soll ab");
   pruefe(labs(nachlauf - lat_aus) <= 1, "Nachlauf passt nicht zur Ausgangslatenz");

   sinus_anpassen(aus, 1, (u32)(2 * lat_aus + 32), (u32)signal, 1000.0, aus_rate, &amp, &abstand);
   printf("  1-kHz-Sinus: Amplitude %.5f (Soll 0.50000), Abstand zum Rest %.1f dB (Schwelle %.0f)\n",
          amp, abstand, schwelle_db(q));
   pruefe(fabs(amp - 0.5) < 0.005, "Amplitude ausserhalb 0.5 +- 1 %");
   pruefe(abstand > schwelle_db(q), "Abstand Sinus zu Rest zu klein");
   printf("  Tempo: %.3f ms fuer 1 s Audio (%.0f-fach Echtzeit)\n", dauer * 1000.0, 1.0 / dauer);

   /* reset_mem: Spitze eines Impulses liegt um die Ausgangslatenz spaeter. */
   pruefe(R.reset_mem(st) == 0, "speex_resampler_reset_mem");
   p = impuls_spitze(st, ein_rate, ein, null, aus, platz);
   soll_p = (double)(ein_rate / 100) * aus_rate / ein_rate + lat_aus;
   printf("  Impuls nach reset_mem: Spitze bei %ld (Soll %.1f = Impulsort + Ausgangslatenz)\n", p, soll_p);
   pruefe(p >= 0 && fabs((double)p - soll_p) <= 1.0, "Impulsspitze nach reset_mem verschoben");

   /* skip_zeros: die fuehrenden Nullen entfallen, die Spitze liegt am Impulsort. */
   pruefe(R.reset_mem(st) == 0, "speex_resampler_reset_mem");
   pruefe(R.skip_zeros(st) == 0, "speex_resampler_skip_zeros");
   p = impuls_spitze(st, ein_rate, ein, null, aus, platz);
   soll_p = (double)(ein_rate / 100) * aus_rate / ein_rate;
   printf("  Impuls nach skip_zeros: Spitze bei %ld (Soll %.1f = Impulsort)\n", p, soll_p);
   pruefe(p >= 0 && fabs((double)p - soll_p) <= 1.0, "Impulsspitze nach skip_zeros verschoben");

ende:
   R.destroy(st);
   free(ein);
   free(null);
   free(aus);
}

/* set_rate_frac: Zustand fuer 48000 -> 16000 auf 44100 -> 48000 umstellen. */
static void fall_set_rate_frac(void)
{
   int err = -1, q = 5;
   SpeexResamplerState *st, *frisch;
   u32 n = 44100, platz = 48000 * 2 + 8192;
   float *ein, *aus;
   long signal;
   double amp, abstand;

   printf("\n[set_rate_frac: 48000 -> 16000 umgestellt auf 44100 -> 48000, Qualitaet %d]\n", q);
   st = R.init(1, 48000, 16000, q, &err);
   frisch = R.init(1, 44100, 48000, q, &err);
   pruefe(st != NULL && frisch != NULL, "speex_resampler_init");
   if (!st || !frisch) return;

   printf("  vorher: Latenz Ausgang %d\n", R.get_output_latency(st));
   pruefe(R.set_rate_frac(st, 44100, 48000, 44100, 48000) == 0, "speex_resampler_set_rate_frac");
   printf("  nachher: Latenz Eingang %d / Ausgang %d, frischer Zustand: %d / %d\n",
          R.get_input_latency(st), R.get_output_latency(st),
          R.get_input_latency(frisch), R.get_output_latency(frisch));
   pruefe(R.get_input_latency(st) == R.get_input_latency(frisch)
       && R.get_output_latency(st) == R.get_output_latency(frisch),
          "Latenzen nach set_rate_frac weichen vom frischen Zustand ab");

   ein = calloc(n, sizeof(float));
   aus = calloc(platz, sizeof(float));
   R.reset_mem(st);
   sinus(ein, n, 1, 1000.0, 44100, 0.5);
   signal = durchschieben(st, ein, n, 441, aus, platz);
   printf("  Ausgabelaenge: %ld Werte aus %u (Soll 48000.0)\n", signal, n);
   pruefe(signal >= 0 && labs(signal - 48000) <= 1, "Ausgabelaenge nach set_rate_frac");
   if (signal > 0) {
      sinus_anpassen(aus, 1, (u32)(2 * R.get_output_latency(st) + 32), (u32)signal, 1000.0, 48000, &amp, &abstand);
      printf("  1-kHz-Sinus: Amplitude %.5f, Abstand zum Rest %.1f dB\n", amp, abstand);
      pruefe(fabs(amp - 0.5) < 0.005 && abstand > schwelle_db(q), "Signal nach set_rate_frac");
   }

   R.destroy(st);
   R.destroy(frisch);
   free(ein);
   free(aus);
}

/* process_interleaved_float: Stereo, links 1 kHz, rechts 440 Hz. */
static void fall_interleaved(u32 ein_rate, u32 aus_rate, int q)
{
   int err = -1;
   SpeexResamplerState *st;
   u32 n = ein_rate, block = ein_rate / 100, platz = aus_rate * 2 + 8192;
   u32 pos = 0, erzeugt = 0;
   float *ein, *aus;
   double soll = (double)n * aus_rate / ein_rate, amp_l, amp_r, ab_l, ab_r;
   int lat_aus;

   printf("\n[process_interleaved_float: %u -> %u Hz, Qualitaet %d, 2 Kanaele]\n", ein_rate, aus_rate, q);
   st = R.init(2, ein_rate, aus_rate, q, &err);
   pruefe(st != NULL && err == 0, "speex_resampler_init (2 Kanaele)");
   if (!st) return;
   lat_aus = R.get_output_latency(st);

   ein = calloc((size_t)n * 2, sizeof(float));
   aus = calloc((size_t)platz * 2, sizeof(float));
   sinus(ein, n, 2, 1000.0, ein_rate, 0.5);
   sinus(ein + 1, n, 2, 440.0, ein_rate, 0.25);

   while (pos < n) {
      u32 ilen = (n - pos < block) ? n - pos : block;
      u32 olen = platz - erzeugt;
      int e = R.process_interleaved_float(st, ein + (size_t)pos * 2, &ilen, aus + (size_t)erzeugt * 2, &olen);
      if (e != 0 || (ilen == 0 && olen == 0)) {
         printf("    process_interleaved_float: %s\n", e ? R.strerror_(e) : "kein Fortschritt");
         pruefe(0, "process_interleaved_float");
         break;
      }
      pos += ilen;
      erzeugt += olen;
   }
   printf("  Ausgabelaenge: %u Rahmen aus %u (Soll %.1f)\n", erzeugt, n, soll);
   pruefe(fabs((double)erzeugt - soll) <= 1.0, "Ausgabelaenge interleaved");

   sinus_anpassen(aus, 2, (u32)(2 * lat_aus + 32), erzeugt, 1000.0, aus_rate, &amp_l, &ab_l);
   sinus_anpassen(aus + 1, 2, (u32)(2 * lat_aus + 32), erzeugt, 440.0, aus_rate, &amp_r, &ab_r);
   printf("  links 1 kHz: Amplitude %.5f (Soll 0.50000), %.1f dB | rechts 440 Hz: Amplitude %.5f (Soll 0.25000), %.1f dB\n",
          amp_l, ab_l, amp_r, ab_r);
   pruefe(fabs(amp_l - 0.5) < 0.005 && fabs(amp_r - 0.25) < 0.0025, "Amplituden interleaved");
   pruefe(ab_l > schwelle_db(q) && ab_r > schwelle_db(q), "Kanaltrennung/Abstand interleaved");

   R.destroy(st);
   free(ein);
   free(aus);
}

/* Ungueltige Qualitaet: init muss NULL und RESAMPLER_ERR_INVALID_ARG liefern. */
static void fall_fehlerpfad(void)
{
   int err = -1;
   SpeexResamplerState *st = R.init(1, 48000, 16000, 11, &err);
   printf("\n[Fehlerpfad: Qualitaet 11]\n  init -> %s, err %d (%s)\n",
          st ? "Zustand" : "NULL", err, R.strerror_(err));
   pruefe(st == NULL && err == 3, "ungueltige Qualitaet nicht abgewiesen");
   if (st) R.destroy(st);
}

int wmain(int argc, wchar_t **argv)
{
   static const u32 raten[][2] = { { 48000, 16000 }, { 44100, 48000 } };
   static const int qualitaeten[] = { 5, 10 };
   int i, j;

   if (argc < 2) {
      printf("Aufruf: rauchtest.exe <Pfad zur speexdsp_resampler.dll>\n");
      return 2;
   }
   if (!laden(argv[1]))
      return 2;

   for (i = 0; i < 2; i++)
      for (j = 0; j < 2; j++)
         fall(raten[i][0], raten[i][1], qualitaeten[j]);

   fall_set_rate_frac();
   fall_interleaved(48000, 16000, 10);
   fall_fehlerpfad();

   printf("\nERGEBNIS: %s (%d Pruefungen, %d Fehler)\n",
          g_fehler ? "NICHT BESTANDEN" : "BESTANDEN", g_pruefungen, g_fehler);
   return g_fehler ? 1 : 0;
}
