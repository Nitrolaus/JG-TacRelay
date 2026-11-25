// JG TacRelay - Maintainer: Sarevoc
/*
 * rauchtest.c - Rauchtest fuer webrtc_apm.dll (JG TacRelay).
 *
 * Laedt die DLL zur Laufzeit per LoadLibrary/GetProcAddress (so wie spaeter LibraryImport
 * aus C#), prueft die zehn Exporte, die Fehlerpfade und fuehrt einen Echotest aus:
 *   48 kHz mono, weisses Rauschen als Wiedergabe (Render), das Aufnahmesignal (Capture)
 *   ist dasselbe Rauschen 40 ms verzoegert und um 6 dB abgeschwaecht plus leises
 *   Grundrauschen. Gemessen werden ERLE (eigene Energiemessung und APM-Kennwert) je
 *   100 ms und die Rechenzeit je 10-ms-Rahmen (Render + Capture zusammen).
 *
 * Aufruf: rauchtest.exe <pfad\webrtc_apm.dll> [sekunden, Vorgabe 1]
 * Exitcode 0 = alle Pruefungen bestanden.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "apm_c.h"

typedef apm_handle*(APM_CALL* fn_create)(int, int);
typedef void(APM_CALL* fn_destroy)(apm_handle*);
typedef int(APM_CALL* fn_configure)(apm_handle*, int, int, int, int, float);
typedef int(APM_CALL* fn_delay)(apm_handle*, int);
typedef int(APM_CALL* fn_capture)(apm_handle*, float*);
typedef int(APM_CALL* fn_render)(apm_handle*, const float*);
typedef int(APM_CALL* fn_stats)(apm_handle*, float*, float*, int*);
typedef const char*(APM_CALL* fn_version)(void);
typedef int(APM_CALL* fn_aec3_configure)(apm_handle*, const char*, int*);
typedef int(APM_CALL* fn_aec3_describe)(apm_handle*, int, char*, int);

static struct {
  fn_create create;
  fn_destroy destroy;
  fn_configure configure;
  fn_delay set_delay;
  fn_capture capture;
  fn_render render;
  fn_stats stats;
  fn_version version;
  fn_aec3_configure aec3_configure;
  fn_aec3_describe aec3_describe;
} api;

static int g_fehler = 0;

static void pruefe(int bedingung, const char* text) {
  printf("  %-6s %s\n", bedingung ? "ok" : "FEHLER", text);
  if (!bedingung) g_fehler++;
}

static unsigned int g_zufall = 12345u;
static float zufall(void) { /* gleichverteilt in [-0.5, 0.5) */
  g_zufall = g_zufall * 1664525u + 1013904223u;
  return (float)(g_zufall >> 8) / 16777216.0f - 0.5f;
}

static int vergleiche_double(const void* a, const void* b) {
  double x = *(const double*)a, y = *(const double*)b;
  return (x > y) - (x < y);
}

static double db(double zaehler, double nenner) {
  if (nenner <= 0.0) nenner = 1e-20;
  if (zaehler <= 0.0) zaehler = 1e-20;
  return 10.0 * log10(zaehler / nenner);
}

typedef struct {
  double erle_eigen_ende; /* eigene Messung ueber die letzten 30 % */
  float erle_apm, erl_apm;
  int delay_apm, stats_rc;
  double t_mittel, t_median, t_p99, t_max;
  int rc_fehler, nan_werte;
} ergebnis;

/* Ein Echolauf. ns: -1..3, agc2: 0/1. tabelle_ms: Zeilenabstand der Tabelle (0 = keine). */
static int echo_lauf(double sekunden, int ns, int agc2, int tabelle_ms, ergebnis* e) {
  const int rate = 48000, n = 480;
  const int rahmen = (int)(sekunden * 100.0 + 0.5);
  const int verzoegerung_ms = 40;
  const int verzoegerung = verzoegerung_ms * rate / 1000;
  const float daempfung = 0.5f; /* -6 dB */
  const size_t gesamt = (size_t)rahmen * (size_t)n;
  float* wiedergabe = (float*)malloc(gesamt * sizeof(float));
  float* aufnahme = (float*)malloc(gesamt * sizeof(float));
  double* zeiten = (double*)malloc((size_t)rahmen * sizeof(double));
  float puffer[480];
  LARGE_INTEGER f, t0, t1;
  apm_handle* h;
  size_t i;
  int k, start_ende = rahmen - (rahmen * 3) / 10;
  double e_in_blk = 0, e_out_blk = 0, e_in_ende = 0, e_out_ende = 0, summe = 0;

  memset(e, 0, sizeof(*e));
  if (!wiedergabe || !aufnahme || !zeiten) return -1;
  g_zufall = 12345u;
  for (i = 0; i < gesamt; ++i) wiedergabe[i] = 0.6f * zufall(); /* ca. -15 dBFS */
  for (i = 0; i < gesamt; ++i) {
    float echo = (i >= (size_t)verzoegerung) ? daempfung * wiedergabe[i - verzoegerung] : 0.0f;
    aufnahme[i] = echo + 0.001f * zufall(); /* Grundrauschen ca. -71 dBFS */
  }

  h = api.create(rate, 1);
  if (!h) { free(wiedergabe); free(aufnahme); free(zeiten); return -2; }
  if (api.configure(h, 1, 1, ns, agc2, 30.0f) != 0) { api.destroy(h); return -3; }
  api.set_delay(h, verzoegerung_ms);

  QueryPerformanceFrequency(&f);
  if (tabelle_ms) printf("      Zeit  ERLE eigen  ERLE APM  ERL APM  Delay APM  Stats\n");
  for (k = 0; k < rahmen; ++k) {
    const float* w = wiedergabe + (size_t)k * n;
    const float* a = aufnahme + (size_t)k * n;
    double ein = 0, aus = 0;
    int j, rc1, rc2;
    for (j = 0; j < n; ++j) ein += (double)a[j] * a[j];
    memcpy(puffer, a, sizeof(puffer));

    QueryPerformanceCounter(&t0);
    rc1 = api.render(h, w);
    rc2 = api.capture(h, puffer);
    QueryPerformanceCounter(&t1);

    zeiten[k] = (double)(t1.QuadPart - t0.QuadPart) * 1000.0 / (double)f.QuadPart;
    if (rc1 != 0 || rc2 != 0) e->rc_fehler++;
    for (j = 0; j < n; ++j) {
      if (!isfinite(puffer[j])) e->nan_werte++;
      aus += (double)puffer[j] * puffer[j];
    }
    e_in_blk += ein; e_out_blk += aus;
    if (k >= start_ende) { e_in_ende += ein; e_out_ende += aus; }

    if (tabelle_ms && (k + 1) % (tabelle_ms / 10) == 0) {
      float erl = NAN, erle = NAN; int d = -1, src;
      src = api.stats(h, &erl, &erle, &d);
      printf("    %5d ms  %7.1f dB  %6.1f dB  %5.1f dB  %5d ms  %5d\n", (k + 1) * 10,
             db(e_in_blk, e_out_blk), erle, erl, d, src);
      e_in_blk = e_out_blk = 0;
    }
  }

  e->erle_eigen_ende = db(e_in_ende, e_out_ende);
  e->erl_apm = NAN; e->erle_apm = NAN; e->delay_apm = -1;
  e->stats_rc = api.stats(h, &e->erl_apm, &e->erle_apm, &e->delay_apm);
  api.destroy(h);

  for (k = 0; k < rahmen; ++k) summe += zeiten[k];
  e->t_mittel = summe / rahmen;
  qsort(zeiten, (size_t)rahmen, sizeof(double), vergleiche_double);
  e->t_median = zeiten[rahmen / 2];
  e->t_p99 = zeiten[(int)((rahmen - 1) * 0.99)];
  e->t_max = zeiten[rahmen - 1];

  free(wiedergabe); free(aufnahme); free(zeiten);
  return 0;
}

/* Liest die AEC3-Konfiguration als Text (malloc, vom Aufrufer freizugeben); NULL bei Fehler. */
static char* beschreibung(apm_handle* h, int wirksam, int* rc) {
  char* text;
  int n = api.aec3_describe(h, wirksam, NULL, 0);
  *rc = n;
  if (n < 0) return NULL;
  text = (char*)malloc((size_t)n + 1);
  if (!text) return NULL;
  *rc = api.aec3_describe(h, wirksam, text, n + 1);
  if (*rc != n) { free(text); return NULL; }
  return text;
}

static int enthaelt(const char* text, const char* teil) { return text && strstr(text, teil) != NULL; }

typedef struct {
  apm_handle* h;
  volatile LONG stop;
  LONG fehler, rahmen;
} last_arg;

/* Verarbeitet Render und Capture im Kreis, bis stop gesetzt ist (zweiter Thread). */
static DWORD WINAPI last_thread(LPVOID p) {
  last_arg* a = (last_arg*)p;
  float w[480], c[480];
  unsigned int z = 777u; /* eigener Zufall: g_zufall gehoert dem Hauptthread */
  int j;
  while (!a->stop) {
    for (j = 0; j < 480; ++j) {
      z = z * 1664525u + 1013904223u;
      w[j] = 0.3f * ((float)(z >> 8) / 16777216.0f - 0.5f);
      c[j] = 0.2f * w[j];
    }
    if (api.render(a->h, w) != 0) InterlockedIncrement(&a->fehler);
    if (api.capture(a->h, c) != 0) InterlockedIncrement(&a->fehler);
    for (j = 0; j < 480; ++j) if (!isfinite(c[j])) { InterlockedIncrement(&a->fehler); break; }
    InterlockedIncrement(&a->rahmen);
  }
  return 0;
}

static void aec3_pruefen(void) {
  apm_handle* h = api.create(48000, 1);
  apm_handle* frisch = api.create(48000, 1);
  char *vorher, *nachher, *wirksam, *vorgabe;
  int rc, pos, k;
  last_arg last;
  HANDLE thread;

  pruefe(h != NULL && frisch != NULL, "apm_create fuer die AEC3-Pruefung");
  if (!h || !frisch) return;
  vorgabe = beschreibung(frisch, 0, &rc);
  pruefe(vorgabe != NULL && rc > 1000, "apm_aec3_describe(0) liefert die ganze Konfiguration");
  printf("    Laenge %d Zeichen\n", rc);
  pruefe(enthaelt(vorgabe, "suppressor.normal_tuning.max_inc_factor=") &&
             enthaelt(vorgabe, "ep_strength.default_gain=") &&
             enthaelt(vorgabe, "filter.initial_state_seconds="),
         "Beschreibung enthaelt die Felder mit ihren Upstream-Namen");
  pruefe(enthaelt(vorgabe, "suppressor.normal_tuning.max_inc_factor=4;") &&
             enthaelt(vorgabe, "suppressor.nearend_tuning.max_inc_factor=8;") &&
             enthaelt(vorgabe, "ep_strength.default_gain=1;"),
         "frische Instanz: Voreinstellung tacrelay (max_inc_factor 4/8, sonst Upstream)");
  pruefe(strstr(api.version(), "Transparentmodus HMM") != NULL, "Version nennt den Transparentmodus HMM");
  pruefe(api.aec3_describe(h, 1, NULL, 0) == APM_ERR_NICHT_AN, "wirksam ohne AEC -> -12");
  pruefe(api.aec3_describe(h, 2, NULL, 0) == APM_ERR_PARAMETER, "wirksam = 2 -> -6");
  pruefe(api.aec3_describe(NULL, 0, NULL, 0) == APM_ERR_NULLZEIGER, "describe(NULL) -> -5");
  pruefe(api.aec3_configure(NULL, "", NULL) == APM_ERR_NULLZEIGER, "configure(NULL) -> -5");
  pruefe(api.aec3_configure(h, NULL, NULL) == APM_ERR_NULLZEIGER, "configure(h, NULL) -> -5");

  pruefe(api.aec3_configure(h, "voreinstellung=webrtc", &pos) == 0 && pos == -1,
         "voreinstellung=webrtc wird angenommen (Upstream-Vorgabe besteht Validate)");
  vorher = beschreibung(h, 0, &rc);
  pruefe(enthaelt(vorher, "suppressor.normal_tuning.max_inc_factor=2;") &&
             enthaelt(vorher, "ep_strength.default_gain=1;") &&
             enthaelt(vorher, "filter.initial_state_seconds=2.5;") &&
             enthaelt(vorher, "delay.down_sampling_factor=4;") &&
             enthaelt(vorher, "suppressor.dominant_nearend_detection.trigger_threshold=12;"),
         "Upstream-Werte wie in echo_canceller3_config.h");

  {
    static const struct { const char* spec; int pos; const char* text; } falsch[] = {
        {"gibt.es.nicht=1", 0, "unbekannter Name"},
        {"ep_strength.default_gain=1;ep_strength.default_gain=abc", 27, "unlesbarer Wert, Position des Eintrags"},
        {"ep_strength.default_gain", 0, "ohne Gleichheitszeichen"},
        {"ep_strength.default_gain=nan", 0, "NaN"},
        {"ep_strength.default_gain=inf", 0, "Unendlich"},
        {"filter.conservative_initial_phase=2", 0, "Schalter nur 0/1"},
        {"delay.default_delay=1.5", 0, "ganzzahliges Feld mit Bruch"},
        {"delay.default_delay=-1", 0, "size_t negativ"},
        {"voreinstellung=gibtsnicht", 0, "unbekannte Voreinstellung"},
        {"delay.down_sampling_factor=5", 28, "Validate wuerde anpassen (nur 4 oder 8)"},
        {"suppressor.normal_tuning.mask_lf.enr_transparent=0.5", 52, "Maske enr_transparent >= enr_suppress"},
        {"echo_audibility.audibility_threshold_lf=1", 41, "Hoerbarkeitsschwelle <= 1 (Division durch null)"},
    };
    size_t i;
    for (i = 0; i < sizeof(falsch) / sizeof(falsch[0]); ++i) {
      char text[160];
      rc = api.aec3_configure(h, falsch[i].spec, &pos);
      snprintf(text, sizeof(text), "abgelehnt (-6, Position %d): %s", falsch[i].pos, falsch[i].text);
      pruefe(rc == APM_ERR_PARAMETER && pos == falsch[i].pos, text);
    }
  }
  nachher = beschreibung(h, 0, &rc);
  pruefe(vorher && nachher && strcmp(vorher, nachher) == 0, "nach Ablehnungen unveraendert (alles oder nichts)");
  free(nachher);

  pruefe(api.aec3_configure(h, " ep_strength.default_gain = 0.5 ;suppressor.normal_tuning.max_inc_factor=4;", &pos) == 0,
         "zwei Werte mit Leerzeichen und abschliessendem ; angenommen");
  nachher = beschreibung(h, 0, &rc);
  pruefe(enthaelt(nachher, "ep_strength.default_gain=0.5;") &&
             enthaelt(nachher, "suppressor.normal_tuning.max_inc_factor=4;"),
         "Beschreibung zeigt die neuen Werte");
  pruefe(api.configure(h, 1, 1, -1, 1, 30.0f) == 0, "AEC einschalten");
  wirksam = beschreibung(h, 1, &rc);
  pruefe(wirksam && nachher && strcmp(wirksam, nachher) == 0,
         "wirksam (vom laufenden EchoCanceller3) = eingestellt");
  free(wirksam); free(nachher);
  pruefe(api.aec3_configure(h, "filter.initial_state_seconds=0.5", &pos) == 0, "Aenderung bei laufendem AEC");
  wirksam = beschreibung(h, 1, &rc);
  pruefe(enthaelt(wirksam, "filter.initial_state_seconds=0.5;"), "sofort neu aufgebaut und wirksam");
  free(wirksam);
  pruefe(api.aec3_configure(h, "voreinstellung=tacrelay", &pos) == 0, "voreinstellung=tacrelay");
  wirksam = beschreibung(h, 1, &rc);
  pruefe(wirksam && vorgabe && strcmp(wirksam, vorgabe) == 0,
         "entspricht der Konfiguration einer frischen Instanz (Voreinstellung besteht Validate)");
  free(wirksam);
  pruefe(api.configure(h, 0, 1, -1, 1, 30.0f) == 0 && api.aec3_describe(h, 1, NULL, 0) == APM_ERR_NICHT_AN,
         "AEC aus: kein laufender EchoCanceller3 mehr");

  printf("\n[8] Umbau bei laufender Verarbeitung (zweiter Thread: Render und Capture im Kreis)\n");
  memset(&last, 0, sizeof(last));
  last.h = h;
  thread = CreateThread(NULL, 0, last_thread, &last, 0, NULL);
  pruefe(thread != NULL, "Lastthread gestartet");
  rc = 0;
  {
    LARGE_INTEGER f, t0, t1;
    double laengste = 0.0;
    QueryPerformanceFrequency(&f);
    for (k = 0; k < 40 && thread; ++k) {
      double ms;
      QueryPerformanceCounter(&t0);
      rc |= api.configure(h, k % 2 == 0, 1, -1, 1, 30.0f);
      rc |= api.aec3_configure(h, k % 3 == 0 ? "filter.initial_state_seconds=1" : "voreinstellung=tacrelay", NULL);
      QueryPerformanceCounter(&t1);
      ms = (double)(t1.QuadPart - t0.QuadPart) * 1000.0 / (double)f.QuadPart;
      if (ms > laengste) laengste = ms;
      Sleep(5);
    }
    printf("    laengste Umschaltung (AEC ein/aus plus AEC3-Konfiguration): %.2f ms\n", laengste);
    /* Ohne Vorrang fuer den Umbau wartete er hier bis zu 1,4 s auf die geteilte Sperre. */
    pruefe(laengste < 100.0, "Umbau wartet nicht auf einen Strom von Verarbeitungsaufrufen (unter 100 ms)");
  }
  if (thread) {
    last.stop = 1;
    WaitForSingleObject(thread, INFINITE);
    CloseHandle(thread);
  }
  printf("    %ld Rahmen im Lastthread, 40 Umschaltungen\n", last.rahmen);
  pruefe(rc == 0, "alle Umschaltungen -> 0");
  pruefe(last.fehler == 0 && last.rahmen > 0, "Lastthread ohne Fehlercode und ohne NaN");

  free(vorher); free(vorgabe);
  api.destroy(h);
  api.destroy(frisch);
}

static int kurzlauf(int rate, int kanaele, int rahmen) {
  const int n = rate / 100 * kanaele;
  float* w = (float*)calloc((size_t)n, sizeof(float));
  float* a = (float*)calloc((size_t)n, sizeof(float));
  apm_handle* h = api.create(rate, kanaele);
  int k, j, fehler = 0;
  if (!h || !w || !a) { free(w); free(a); if (h) api.destroy(h); return -1; }
  if (api.configure(h, 1, 1, 2, 1, 30.0f) != 0) fehler++;
  api.set_delay(h, 20);
  for (k = 0; k < rahmen; ++k) {
    for (j = 0; j < n; ++j) { w[j] = 0.3f * zufall(); a[j] = 0.5f * w[j] + 0.01f * zufall(); }
    if (api.render(h, w) != 0) fehler++;
    if (api.capture(h, a) != 0) fehler++;
    for (j = 0; j < n; ++j) if (!isfinite(a[j])) { fehler++; break; }
  }
  api.destroy(h);
  free(w); free(a);
  return fehler;
}

int main(int argc, char** argv) {
  const char* pfad = argc > 1 ? argv[1] : "webrtc_apm.dll";
  double sekunden = argc > 2 ? atof(argv[2]) : 1.0;
  HMODULE dll;
  ergebnis e;
  apm_handle* h;
  float buf[480], erl, erle;
  int d, rc;

  if (sekunden < 0.3) sekunden = 0.3;
  printf("Rauchtest webrtc_apm.dll\n  DLL: %s\n", pfad);
  dll = LoadLibraryExA(pfad, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
  if (!dll) {
    printf("  FEHLER DLL nicht ladbar (GetLastError %lu)\n", GetLastError());
    return 2;
  }

  printf("\n[1] Exporte\n");
#define HOLE(feld, name)                            \
  *(FARPROC*)&api.feld = GetProcAddress(dll, name); \
  pruefe(api.feld != NULL, name);
  HOLE(create, "apm_create")
  HOLE(destroy, "apm_destroy")
  HOLE(configure, "apm_configure")
  HOLE(set_delay, "apm_set_stream_delay_ms")
  HOLE(capture, "apm_process_capture")
  HOLE(render, "apm_process_render")
  HOLE(stats, "apm_get_stats")
  HOLE(version, "apm_version")
  HOLE(aec3_configure, "apm_aec3_configure")
  HOLE(aec3_describe, "apm_aec3_describe")
#undef HOLE
  if (g_fehler) { printf("\nAbbruch: Exporte fehlen.\n"); return 1; }
  printf("  Version: %s\n", api.version());

  printf("\n[2] Fehlerpfade\n");
  pruefe(api.create(22050, 1) == NULL, "apm_create(22050, 1) -> NULL (nicht durch 100 teilbar)");
  pruefe(api.create(48000, 3) == NULL, "apm_create(48000, 3) -> NULL (zu viele Kanaele)");
  pruefe(api.create(0, 1) == NULL, "apm_create(0, 1) -> NULL");
  h = api.create(48000, 1);
  pruefe(h != NULL, "apm_create(48000, 1) -> Handle");
  if (!h) return 1;
  pruefe(api.configure(h, 1, 1, 4, 0, 0.0f) == -6, "apm_configure mit NS-Stufe 4 -> -6");
  pruefe(api.configure(h, 1, 1, -2, 0, 0.0f) == -6, "apm_configure mit NS-Stufe -2 -> -6");
  pruefe(api.configure(h, 0, 0, -1, 1, 0.0f) == -6, "apm_configure AGC2 mit 0 dB -> -6");
  pruefe(api.configure(h, 0, 0, -1, 1, NAN) == -6, "apm_configure AGC2 mit NaN -> -6");
  pruefe(api.configure(h, 1, 1, 1, 1, 20.0f) == 0, "apm_configure gueltig -> 0");
  pruefe(api.configure(NULL, 1, 1, 1, 1, 20.0f) == -5, "apm_configure(NULL) -> -5");
  pruefe(api.capture(NULL, buf) == -5, "apm_process_capture(NULL, ...) -> -5");
  pruefe(api.capture(h, NULL) == -5, "apm_process_capture(h, NULL) -> -5");
  pruefe(api.render(h, NULL) == -5, "apm_process_render(h, NULL) -> -5");
  pruefe(api.set_delay(h, -5) == -13, "apm_set_stream_delay_ms(-5) -> -13 (begrenzt)");
  pruefe(api.set_delay(h, 900) == -13, "apm_set_stream_delay_ms(900) -> -13 (begrenzt)");
  pruefe(api.set_delay(h, 40) == 0, "apm_set_stream_delay_ms(40) -> 0");
  rc = api.stats(h, &erl, &erle, &d);
  pruefe(rc == 1 && isnan(erle) && d == -1, "apm_get_stats vor dem ersten Rahmen -> 1, NaN, -1");
  pruefe(api.stats(NULL, NULL, NULL, NULL) == -5, "apm_get_stats(NULL) -> -5");
  api.destroy(h);
  api.destroy(NULL);
  pruefe(1, "apm_destroy(NULL) ohne Absturz");

  printf("\n[3] Echotest 48 kHz mono, %.1f s, AEC3 + Hochpass, NS aus, AGC2 aus\n", sekunden);
  printf("    Render: weisses Rauschen ca. -15 dBFS; Capture: Echo 40 ms spaeter, -6 dB, plus -71 dBFS Grundrauschen\n");
  printf("    ERLE eigen = 10*log10(Energie Capture-Eingang / Energie Ausgang) je Zeile\n");
  rc = echo_lauf(sekunden, -1, 0, 100, &e);
  pruefe(rc == 0, "Echolauf ausgefuehrt");
  pruefe(e.rc_fehler == 0, "alle Render- und Capture-Aufrufe -> 0");
  pruefe(e.nan_werte == 0, "keine NaN/Inf im Ausgang");
  printf("    ERLE nach der Konvergenz (letzte 30 %%, eigene Messung): %.1f dB\n", e.erle_eigen_ende);
  printf("    APM-Kennwerte am Ende: ERLE %.1f dB, ERL %.1f dB, Delay %d ms (Rueckgabe %d)\n",
         e.erle_apm, e.erl_apm, e.delay_apm, e.stats_rc);
  printf("    Rechenzeit je 10-ms-Rahmen (Render+Capture): Mittel %.3f ms, Median %.3f ms, "
         "P99 %.3f ms, Max %.3f ms (%.1f %% Echtzeit)\n",
         e.t_mittel, e.t_median, e.t_p99, e.t_max, e.t_mittel * 10.0);
  pruefe(e.erle_eigen_ende >= 10.0, "Echo nach der Konvergenz um mindestens 10 dB gedaempft");
  pruefe(e.t_mittel < 10.0, "mittlere Rechenzeit unter 10 ms (Echtzeit)");
  if (sekunden < 2.5) {
    pruefe(e.stats_rc == 1, "apm_get_stats -> 1: AEC3 schaetzt ERL/ERLE erst nach 2 s Anlauf");
  } else {
    pruefe(e.stats_rc == 0, "apm_get_stats -> 0 (gueltig)");
  }

  printf("\n[4] Konvergenzlauf 48 kHz mono, 6 s, gleiche Signale, APM-Kennwerte nach der Anlaufphase\n");
  rc = echo_lauf(6.0, -1, 0, 500, &e);
  pruefe(rc == 0 && e.rc_fehler == 0 && e.nan_werte == 0, "alle Aufrufe -> 0, keine NaN/Inf");
  printf("    ERLE eigen (letzte 30 %%): %.1f dB; APM am Ende: ERLE %.1f dB, ERL %.1f dB, Delay %d ms\n",
         e.erle_eigen_ende, e.erle_apm, e.erl_apm, e.delay_apm);
  pruefe(e.stats_rc == 0, "apm_get_stats nach 6 s -> 0 (gueltig)");
  /* Echopfad im Test: -6 dB. AEC3 schaetzt ERL nach Minimumstatistik (aec3/erl_estimator.cc,
     "maximum statistics manner"): es folgt nur fallenden Blockverhaeltnissen und liest deshalb
     hoeher als die mittlere Pfaddaempfung, nie deutlich niedriger. */
  pruefe(e.erl_apm >= 3.0f && e.erl_apm <= 20.0f,
         "ERL des APM zwischen 3 und 20 dB (Pfad 6 dB, Schaetzer liest hoeher)");
  pruefe(e.delay_apm >= 32 && e.delay_apm <= 48,
         "Delay des APM 40 ms +-8 ms (AEC3 rechnet in 4-ms-Bloecken)");
  pruefe(e.erle_apm > 0.0f, "ERLE des APM positiv");

  printf("\n[5] Volle Kette 48 kHz mono, 6 s: AEC3 + Hochpass + NS hoch + AGC2 (max 30 dB)\n");
  printf("    apm_get_stats wird hier erst am Ende aufgerufen (Pruefung auf veraltete Kennwerte)\n");
  rc = echo_lauf(6.0, 2, 1, 0, &e);
  pruefe(rc == 0 && e.rc_fehler == 0 && e.nan_werte == 0, "alle Aufrufe -> 0, keine NaN/Inf");
  pruefe(e.stats_rc == 0 && e.delay_apm >= 32 && e.delay_apm <= 48,
         "Kennwerte ohne Zwischenabruf frisch: gueltig, Delay 40 ms +-8 ms");
  printf("    ERLE eigen (letzte 30 %%): %.1f dB; APM: ERLE %.1f dB, ERL %.1f dB, Delay %d ms\n",
         e.erle_eigen_ende, e.erle_apm, e.erl_apm, e.delay_apm);
  printf("    Rechenzeit je 10-ms-Rahmen: Mittel %.3f ms, Median %.3f ms, P99 %.3f ms, Max %.3f ms "
         "(%.1f %% Echtzeit)\n",
         e.t_mittel, e.t_median, e.t_p99, e.t_max, e.t_mittel * 10.0);

  printf("\n[6] Weitere Formate (alle Stufen an, 100 Rahmen)\n");
  pruefe(kurzlauf(48000, 2, 100) == 0, "48 kHz stereo (planar)");
  pruefe(kurzlauf(16000, 1, 100) == 0, "16 kHz mono");
  pruefe(kurzlauf(44100, 1, 100) == 0, "44,1 kHz mono");

  printf("\n[7] AEC3-Konfiguration (apm_aec3_configure, apm_aec3_describe)\n");
  aec3_pruefen();

  printf("\nErgebnis: %s (%d Fehler)\n", g_fehler ? "FEHLER" : "BESTANDEN", g_fehler);
  FreeLibrary(dll);
  return g_fehler ? 1 : 0;
}
