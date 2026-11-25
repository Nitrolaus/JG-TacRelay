// JG TacRelay - Maintainer: Sarevoc
/*
 * deepfilternet.h - Schnittstelle von deepfilternet.dll (eigene C-Schicht in Rust ueber libDF, DeepFilterNet 0.5.6, mit
 * fest einkompiliertem Modell DeepFilterNet3). Quelle der Schicht: src/lib.rs, Bau: native\skripte\deepfilternet.ps1.
 *
 * Signal: 48 kHz mono, float in [-1, 1], genau dfn_frame_length() = 480 Werte (10 ms) je Aufruf.
 * Verzoegerung Ausgang gegen Eingang: dfn_delay_samples() = 1440 Werte (30 ms; STFT-Ueberlappung 10 ms plus 2 Rahmen
 * Vorgriff des Modells). Die Autoren nennen 40 ms algorithmische Latenz; darin steckt zusaetzlich das Sammeln des
 * 10-ms-Rahmens, das jede Kette ohnehin hat.
 *
 * Fehler: Keine Funktion bricht den Prozess ab. Jede Panik in libDF oder tract wird gefangen und als DFN_FEHLER_PANIK
 * gemeldet; nach einem Rechenfehler ist der Zustand gesperrt (DFN_FEHLER_GESPERRT) und gehoert freigegeben.
 *
 * Threads: Ein Zustand gehoert genau einem Thread zur Zeit; keine Sperre in der Schicht. dfn_create dauert einige hundert
 * Millisekunden und gehoert nie in einen Audio-Thread. tract legt beim Rechnen je Rahmen Speicher an.
 *
 * Eigenheit von libDF: Ein Rahmen mit mittlerer Leistung unter 1e-7 (etwa -70 dBFS) ergibt Nullen, und der Zustand
 * rueckt nicht weiter.
 */
#ifndef TACRELAY_DEEPFILTERNET_H
#define TACRELAY_DEEPFILTERNET_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define DFN_OK                0
#define DFN_FEHLER_ZEIGER    -1 /* ein Zeiger ist NULL */
#define DFN_FEHLER_LAENGE    -2 /* falsche Anzahl Werte */
#define DFN_FEHLER_WERT      -3 /* Wert nicht endlich oder ausserhalb des Bereichs */
#define DFN_FEHLER_MODELL    -4 /* Modell nicht ladbar oder nicht 48 kHz / 480 Werte */
#define DFN_FEHLER_RECHNEN   -5 /* tract meldet einen Fehler; Zustand danach gesperrt */
#define DFN_FEHLER_PANIK     -6 /* Panik gefangen; Zustand danach gesperrt */
#define DFN_FEHLER_GESPERRT  -7 /* Zustand nach einem Fehler gesperrt */

typedef struct DfnZustand DfnZustand;

/* Versionszeichenkette, UTF-8, statisch (nie freigeben). */
const char *dfn_version(void);

/* atten_lim_db: hoechste Absenkung in dB (100 und mehr = unbegrenzt); post_filter_beta: 0 = aus, libDF empfiehlt bis
 * 0,05 (hier 0 bis 1 erlaubt). fehler darf NULL sein. Rueckgabe NULL bei Fehler. */
DfnZustand *dfn_create(float atten_lim_db, float post_filter_beta, int32_t *fehler);

int32_t dfn_frame_length(const DfnZustand *zustand);
int32_t dfn_delay_samples(const DfnZustand *zustand);

/* eingang und ausgang je anzahl Werte, duerfen sich nicht ueberlappen; lsnr darf NULL sein (lokales SNR in dB). */
int32_t dfn_process(DfnZustand *zustand, const float *eingang, float *ausgang, int32_t anzahl, float *lsnr);

int32_t dfn_set_atten_lim(DfnZustand *zustand, float db);
int32_t dfn_set_post_filter_beta(DfnZustand *zustand, float beta);

/* Schwellen nach dem lokalen SNR: unter min_db stumm, ueber max_erb_db unbearbeitet, ueber max_df_db ohne Deep Filtering.
 * libDF ab Werk -10 / 30 / 20, LADSPA-Plugin -15 / 35 / 35, -100 / 100 / 100 = jeder Rahmen voll (wie Python).
 * Verlangt min_db < max_df_db <= max_erb_db, sonst DFN_FEHLER_WERT. */
int32_t dfn_set_thresholds(DfnZustand *zustand, float min_db, float max_erb_db, float max_df_db);

/* NULL erlaubt. */
void dfn_destroy(DfnZustand *zustand);

#ifdef __cplusplus
}
#endif

#endif
