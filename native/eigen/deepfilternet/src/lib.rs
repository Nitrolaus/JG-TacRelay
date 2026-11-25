// JG TacRelay - Maintainer: Sarevoc
//! deepfilternet.dll: eigene C-Schicht ueber libDF (DeepFilterNet 0.5.6, Rust, tract 0.19.16) mit fest einkompiliertem
//! DeepFilterNet3-Modell (`models/DeepFilterNet3_onnx.tar.gz` aus dem Tag v0.5.6). Beschreibung der Schnittstelle in
//! `deepfilternet.h`.
//!
//! Warum nicht die capi von libDF: Dort enden Fehler in `expect`, also in einer Panik, und seit Rust 1.81 bricht eine Panik
//! an der Grenze `extern "C"` den ganzen Prozess ab (JG TacRelay samt Funkverbindung). Hier faengt jede Funktion eine Panik,
//! meldet einen Fehlercode und sperrt den Zustand; der Aufrufer faellt auf RNNoise zurueck. Dazu: Modell aus der DLL statt
//! aus einem Dateipfad, Laengenangabe bei jedem Puffer, Version und Verzoegerung abfragbar.
//!
//! Signal: 48 kHz mono, float in [-1, 1], genau `dfn_frame_length` (480) Werte je Aufruf. Ein Zustand gehoert genau einem
//! Thread zur Zeit (keine Sperre); Anlegen und Freigeben duerfen in einem anderen Thread geschehen als das Rechnen.
//! tract legt beim Rechnen je Rahmen Speicher an (Tensoren); das ist hier nicht abstellbar und gemessen
//! (docs\MODULBERICHT-klang.md, Klangstufe B).

use std::ffi::c_char;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr::null_mut;

use df::tract::{DfParams, DfTract, RuntimeParams};
use ndarray::{ArrayView2, ArrayViewMut2};

pub const DFN_OK: i32 = 0;
/// Ein Zeiger ist NULL.
pub const DFN_FEHLER_ZEIGER: i32 = -1;
/// Falsche Anzahl Werte.
pub const DFN_FEHLER_LAENGE: i32 = -2;
/// Wert nicht endlich oder ausserhalb des Bereichs.
pub const DFN_FEHLER_WERT: i32 = -3;
/// Modell laesst sich nicht laden oder passt nicht (48 kHz, 480 Werte).
pub const DFN_FEHLER_MODELL: i32 = -4;
/// tract meldet einen Fehler beim Rechnen; der Zustand ist danach gesperrt.
pub const DFN_FEHLER_RECHNEN: i32 = -5;
/// Panik in libDF oder tract, gefangen; der Zustand ist danach gesperrt.
pub const DFN_FEHLER_PANIK: i32 = -6;
/// Der Zustand ist nach einem Fehler gesperrt; nur noch dfn_destroy.
pub const DFN_FEHLER_GESPERRT: i32 = -7;

const RATE: usize = 48_000;
const RAHMEN: usize = 480;

static VERSION: &str = concat!(
    "JG TacRelay deepfilternet ",
    env!("CARGO_PKG_VERSION"),
    " (DeepFilterNet 0.5.6, Modell DeepFilterNet3, tract 0.19.16)\0"
);

/// Zustand eines Filters; fuer C undurchsichtig.
pub struct DfnZustand {
    modell: DfTract,
    gesperrt: bool,
}

/// Versionszeichenkette (UTF-8, nullterminiert, statisch; nie freigeben).
#[no_mangle]
pub extern "C" fn dfn_version() -> *const c_char {
    VERSION.as_ptr() as *const c_char
}

/// Legt einen Filter an. `atten_lim_db`: hoechste Absenkung in dB (100 und mehr = unbegrenzt, wie libDF);
/// `post_filter_beta`: 0 = ohne Nachfilter, sonst 0 bis 0,05 laut libDF. `fehler` (darf NULL sein) bekommt DFN_OK oder den
/// Fehlercode. Rueckgabe NULL bei Fehler. Dauert (Modell laden, optimieren) einige hundert Millisekunden: nie im Audio-Thread.
#[no_mangle]
pub unsafe extern "C" fn dfn_create(atten_lim_db: f32, post_filter_beta: f32, fehler: *mut i32) -> *mut DfnZustand {
    let ergebnis = catch_unwind(|| -> Result<Box<DfnZustand>, i32> {
        if !atten_lim_db.is_finite() || !post_filter_beta.is_finite() || !(0.0..=1.0).contains(&post_filter_beta) {
            return Err(DFN_FEHLER_WERT);
        }
        let parameter = DfParams::default();
        let laufzeit = RuntimeParams::default_with_ch(1)
            .with_atten_lim(atten_lim_db)
            .with_post_filter(post_filter_beta);
        let modell = DfTract::new(parameter, &laufzeit).map_err(|_| DFN_FEHLER_MODELL)?;
        if modell.sr != RATE || modell.hop_size != RAHMEN || modell.ch != 1 {
            return Err(DFN_FEHLER_MODELL);
        }
        Ok(Box::new(DfnZustand { modell, gesperrt: false }))
    });
    let (zeiger, code) = match ergebnis {
        Ok(Ok(zustand)) => (Box::into_raw(zustand), DFN_OK),
        Ok(Err(code)) => (null_mut(), code),
        Err(_) => (null_mut(), DFN_FEHLER_PANIK),
    };
    if !fehler.is_null() {
        *fehler = code;
    }
    zeiger
}

/// Werte je Rahmen (480); Fehlercode bei NULL.
#[no_mangle]
pub unsafe extern "C" fn dfn_frame_length(zustand: *const DfnZustand) -> i32 {
    match zustand.as_ref() {
        Some(z) => z.modell.hop_size as i32,
        None => DFN_FEHLER_ZEIGER,
    }
}

/// Verzoegerung des Ausgangs gegen den Eingang in Werten: Ueberlappung der STFT (fft_size - hop_size) plus der Vorgriff
/// des Modells (lookahead Rahmen). DeepFilterNet3: 480 + 2 * 480 = 1440 (30 ms). Fehlercode bei NULL.
#[no_mangle]
pub unsafe extern "C" fn dfn_delay_samples(zustand: *const DfnZustand) -> i32 {
    match zustand.as_ref() {
        Some(z) => (z.modell.fft_size - z.modell.hop_size + z.modell.lookahead * z.modell.hop_size) as i32,
        None => DFN_FEHLER_ZEIGER,
    }
}

/// Filtert genau `anzahl` = dfn_frame_length Werte von `eingang` nach `ausgang` (duerfen sich nicht ueberlappen).
/// `lsnr` (darf NULL sein) bekommt das geschaetzte lokale Signal-Rausch-Verhaeltnis des Rahmens in dB.
/// Ein Rahmen mit mittlerer Leistung unter 1e-7 (etwa -70 dBFS) ergibt Nullen und laesst den Zustand stehen (libDF).
#[no_mangle]
pub unsafe extern "C" fn dfn_process(
    zustand: *mut DfnZustand,
    eingang: *const f32,
    ausgang: *mut f32,
    anzahl: i32,
    lsnr: *mut f32,
) -> i32 {
    if zustand.is_null() || eingang.is_null() || ausgang.is_null() {
        return DFN_FEHLER_ZEIGER;
    }
    let z = &mut *zustand;
    if z.gesperrt {
        return DFN_FEHLER_GESPERRT;
    }
    let n = z.modell.hop_size;
    if anzahl < 0 || anzahl as usize != n {
        return DFN_FEHLER_LAENGE;
    }
    let ergebnis = catch_unwind(AssertUnwindSafe(|| {
        let ein = ArrayView2::from_shape_ptr((1, n), eingang);
        let aus = ArrayViewMut2::from_shape_ptr((1, n), ausgang);
        z.modell.process(ein, aus)
    }));
    match ergebnis {
        Ok(Ok(snr)) => {
            if !lsnr.is_null() {
                *lsnr = snr;
            }
            DFN_OK
        }
        Ok(Err(_)) => {
            z.gesperrt = true;
            DFN_FEHLER_RECHNEN
        }
        Err(_) => {
            z.gesperrt = true;
            DFN_FEHLER_PANIK
        }
    }
}

/// Hoechste Absenkung in dB setzen (100 und mehr = unbegrenzt). Wirkt ab dem naechsten Rahmen.
#[no_mangle]
pub unsafe extern "C" fn dfn_set_atten_lim(zustand: *mut DfnZustand, db: f32) -> i32 {
    let Some(z) = zustand.as_mut() else { return DFN_FEHLER_ZEIGER };
    if z.gesperrt {
        return DFN_FEHLER_GESPERRT;
    }
    if !db.is_finite() {
        return DFN_FEHLER_WERT;
    }
    match catch_unwind(AssertUnwindSafe(|| z.modell.set_atten_lim(db))) {
        Ok(()) => DFN_OK,
        Err(_) => {
            z.gesperrt = true;
            DFN_FEHLER_PANIK
        }
    }
}

/// Nachfilter setzen: 0 = aus, sonst bis 0,05 laut libDF (hier hoechstens 1). Wirkt ab dem naechsten Rahmen.
#[no_mangle]
pub unsafe extern "C" fn dfn_set_post_filter_beta(zustand: *mut DfnZustand, beta: f32) -> i32 {
    let Some(z) = zustand.as_mut() else { return DFN_FEHLER_ZEIGER };
    if z.gesperrt {
        return DFN_FEHLER_GESPERRT;
    }
    if !beta.is_finite() || !(0.0..=1.0).contains(&beta) {
        return DFN_FEHLER_WERT;
    }
    match catch_unwind(AssertUnwindSafe(|| z.modell.set_pf_beta(beta))) {
        Ok(()) => DFN_OK,
        Err(_) => {
            z.gesperrt = true;
            DFN_FEHLER_PANIK
        }
    }
}

/// Schwellen der Rechenstufen nach dem lokalen SNR des Rahmens (libDF `apply_stages`): unter `min_db` gilt der Rahmen als
/// reines Rauschen und wird stumm, ueber `max_erb_db` bleibt er unbearbeitet, ueber `max_df_db` entfaellt die zweite Stufe
/// (Deep Filtering). libDF rechnet ab Werk mit -10 / 30 / 20 dB, das LADSPA-Plugin mit -15 / 35 / 35; -100 / 100 / 100
/// rechnet jeden Rahmen voll wie DeepFilterNet in Python. Verlangt min_db < max_df_db <= max_erb_db.
#[no_mangle]
pub unsafe extern "C" fn dfn_set_thresholds(zustand: *mut DfnZustand, min_db: f32, max_erb_db: f32, max_df_db: f32) -> i32 {
    let Some(z) = zustand.as_mut() else { return DFN_FEHLER_ZEIGER };
    if z.gesperrt {
        return DFN_FEHLER_GESPERRT;
    }
    if !(min_db.is_finite() && max_erb_db.is_finite() && max_df_db.is_finite()) || !(min_db < max_df_db && max_df_db <= max_erb_db) {
        return DFN_FEHLER_WERT;
    }
    z.modell.min_db_thresh = min_db;
    z.modell.max_db_erb_thresh = max_erb_db;
    z.modell.max_db_df_thresh = max_df_db;
    DFN_OK
}

/// Gibt einen Filter frei; NULL ist erlaubt.
#[no_mangle]
pub unsafe extern "C" fn dfn_destroy(zustand: *mut DfnZustand) {
    if zustand.is_null() {
        return;
    }
    let _ = catch_unwind(AssertUnwindSafe(|| drop(Box::from_raw(zustand))));
}
