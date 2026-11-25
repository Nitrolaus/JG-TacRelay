// JG TacRelay - Maintainer: Sarevoc
// apm_c.cpp - duenne C-Schicht ueber das WebRTC Audio Processing Module
// (webrtc-audio-processing 2.1) fuer JG TacRelay. Beschreibung der Funktionen in apm_c.h.
//
// Benutzt die oeffentliche C++-API aus webrtc/api/audio/audio_processing.h
// (AudioProcessingBuilder, AudioProcessing::Config, StreamConfig, GetStatistics) und fuer die
// eigene AEC3-Konfiguration EchoControlFactory samt EchoCanceller3 (seit 1.1.0, Modul aec3).
// Keine C++-Ausnahme verlaesst die DLL: jede Funktion faengt alles ab und meldet einen Code.

#include "apm_c.h"

#include <atomic>
#include <charconv>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <optional>
#include <shared_mutex>
#include <string>
#include <string_view>
#include <system_error>
#include <thread>
#include <type_traits>
#include <utility>

#include "api/audio/audio_processing.h"
#include "api/audio/echo_canceller3_config.h"
#include "api/audio/echo_control.h"
#include "api/scoped_refptr.h"
#include "modules/audio_processing/aec3/echo_canceller3.h"
#include "system_wrappers/include/field_trial.h"

namespace {

// Feldversuche von webrtc, prozessweit und fest (seit 1.1.0, Modul aec3). Die Bibliothek liest sie nur beim Anlegen
// ihrer Module (Konstruktoren, geprueft ueber alle field_trial::-Aufrufe der Quellen); gesetzt wird einmal vor dem
// ersten Anlegen, danach wird der Zeiger nur noch gelesen.
//   WebRTC-Aec3TransparentModeHmm: Transparentmodus (Headset erkannt, kaum Unterdrueckung) ueber das HMM statt ueber
//   den Legacy-Klassifikator. Legacy schaltet erst nach 6 s aktiver Wiedergabe ohne konvergiertes Filter um, bis dahin
//   ist die eigene Stimme bei laufendem Spielton um 44 bis 52 dB gedaempft; mit HMM war in der Messung nur noch die
//   Aeusserung bei 0,8 s gedaempft, die bei 3,0 s schon frei (docs\MODULBERICHT-aec3.md). Der Schalter steht nicht in
//   EchoCanceller3Config, gilt also auch fuer "voreinstellung=webrtc".
// APM_C_OHNE_FELDVERSUCHE (nur fuer Vergleichsmessungen, nie im Produkt): baut ohne diesen Feldversuch.
#ifndef APM_C_OHNE_FELDVERSUCHE
constexpr char kFeldversuche[] = "WebRTC-Aec3TransparentModeHmm/Enabled/";
std::once_flag g_feldversuche;
void FeldversucheSetzen() {
  std::call_once(g_feldversuche,
                 [] { webrtc::field_trial::InitFieldTrialsFromString(kFeldversuche); });
}
#else
void FeldversucheSetzen() {}
#endif

constexpr int kMaxKanaele = 2;
constexpr int kMinRateHz = 8000;
constexpr int kMaxRateHz = 384000;
constexpr int kMaxDelayMs = 500;  // Grenze aus AudioProcessingImpl::set_stream_delay_ms
constexpr float kMaxAgc2GainDb = 90.0f;
constexpr float kAgc2StandardStartDb = 15.0f;  // Vorgabe von AdaptiveDigital::initial_gain_db

// AEC3 schaetzt ERL und ERLE erst nach einer Anlaufphase von 2 s
// (aec3/aec_state.cc: erl_estimator_(2 * kNumBlocksPerSecond), ebenso erle_estimator_).
// Bis dahin liefert GetStatistics() Startwerte: ERL = -10*log10(kMaxErl = 1000) = -30 dB.
constexpr long long kAec3AnlaufRahmen = 200;  // 200 x 10 ms = 2 s
constexpr double kErlPlatzhalterDb = -29.9;   // ERL <= -29.9 dB heisst: noch keine Schaetzung

#define APM_C_STR2(x) #x
#define APM_C_STR(x) APM_C_STR2(x)

#ifdef APM_C_OHNE_FELDVERSUCHE
#define APM_C_TRANSPARENTMODUS "Transparentmodus Legacy (Messbau)"
#else
#define APM_C_TRANSPARENTMODUS "Transparentmodus HMM"
#endif

constexpr char kVersion[] =
    "JG TacRelay webrtc_apm 1.1.0 (webrtc-audio-processing 2.1, AEC3/NS/AGC2/HPF, "
    "AEC3-Konfiguration, " APM_C_TRANSPARENTMODUS ", MSVC " APM_C_STR(_MSC_FULL_VER) ", x64, /MT)";

using Aec3Config = webrtc::EchoCanceller3Config;

// ---------------------------------------------------------------- AEC3-Konfiguration als Text
//
// Jedes Feld heisst wie sein Pfad in EchoCanceller3Config (api/audio/echo_canceller3_config.h).
// Der Name entsteht per #pfad aus demselben Ausdruck, der das Feld adressiert: Ein Feld, das es in
// dieser Fassung nicht gibt, uebersetzt nicht.

enum class Art { Gleitkomma, Ganzzahl, Groesse, Schalter };

template <typename T>
constexpr Art ArtVon() {
  if constexpr (std::is_same_v<T, float>) {
    return Art::Gleitkomma;
  } else if constexpr (std::is_same_v<T, int>) {
    return Art::Ganzzahl;
  } else if constexpr (std::is_same_v<T, size_t>) {
    return Art::Groesse;
  } else {
    static_assert(std::is_same_v<T, bool>, "Feldtyp nicht vorgesehen");
    return Art::Schalter;
  }
}

struct Feld {
  const char* name;
  Art art;
  void* (*ort)(Aec3Config&);
};

#define APM_C_FELD(pfad)                                                         \
  Feld {                                                                         \
    #pfad, ArtVon<std::remove_cvref_t<decltype(std::declval<Aec3Config&>().pfad)>>(), \
        [](Aec3Config& c) -> void* { return &c.pfad; }                           \
  }

constexpr Feld kFelder[] = {
    APM_C_FELD(filter.config_change_duration_blocks),
    APM_C_FELD(filter.initial_state_seconds),
    APM_C_FELD(filter.conservative_initial_phase),
    APM_C_FELD(filter.enable_coarse_filter_output_usage),
    APM_C_FELD(filter.use_linear_filter),
    APM_C_FELD(filter.high_pass_filter_echo_reference),
    APM_C_FELD(delay.default_delay),
    APM_C_FELD(delay.down_sampling_factor),
    APM_C_FELD(delay.num_filters),
    APM_C_FELD(delay.delay_headroom_samples),
    APM_C_FELD(delay.hysteresis_limit_blocks),
    APM_C_FELD(delay.delay_estimate_smoothing),
    APM_C_FELD(delay.delay_estimate_smoothing_delay_found),
    APM_C_FELD(delay.delay_candidate_detection_threshold),
    APM_C_FELD(delay.detect_pre_echo),
    APM_C_FELD(erle.min),
    APM_C_FELD(erle.max_l),
    APM_C_FELD(erle.max_h),
    APM_C_FELD(erle.onset_detection),
    APM_C_FELD(erle.num_sections),
    APM_C_FELD(ep_strength.default_gain),
    APM_C_FELD(ep_strength.default_len),
    APM_C_FELD(ep_strength.nearend_len),
    APM_C_FELD(ep_strength.echo_can_saturate),
    APM_C_FELD(ep_strength.bounded_erl),
    APM_C_FELD(ep_strength.erle_onset_compensation_in_dominant_nearend),
    APM_C_FELD(ep_strength.use_conservative_tail_frequency_response),
    APM_C_FELD(echo_audibility.low_render_limit),
    APM_C_FELD(echo_audibility.normal_render_limit),
    APM_C_FELD(echo_audibility.floor_power),
    APM_C_FELD(echo_audibility.audibility_threshold_lf),
    APM_C_FELD(echo_audibility.audibility_threshold_mf),
    APM_C_FELD(echo_audibility.audibility_threshold_hf),
    APM_C_FELD(echo_audibility.use_stationarity_properties),
    APM_C_FELD(echo_audibility.use_stationarity_properties_at_init),
    APM_C_FELD(render_levels.active_render_limit),
    APM_C_FELD(render_levels.poor_excitation_render_limit),
    APM_C_FELD(echo_removal_control.has_clock_drift),
    APM_C_FELD(echo_removal_control.linear_and_stable_echo_path),
    APM_C_FELD(echo_model.noise_floor_hold),
    APM_C_FELD(echo_model.min_noise_floor_power),
    APM_C_FELD(echo_model.stationary_gate_slope),
    APM_C_FELD(echo_model.noise_gate_power),
    APM_C_FELD(echo_model.noise_gate_slope),
    APM_C_FELD(echo_model.model_reverb_in_nonlinear_mode),
    APM_C_FELD(comfort_noise.noise_floor_dbfs),
    APM_C_FELD(suppressor.nearend_average_blocks),
    APM_C_FELD(suppressor.normal_tuning.mask_lf.enr_transparent),
    APM_C_FELD(suppressor.normal_tuning.mask_lf.enr_suppress),
    APM_C_FELD(suppressor.normal_tuning.mask_lf.emr_transparent),
    APM_C_FELD(suppressor.normal_tuning.mask_hf.enr_transparent),
    APM_C_FELD(suppressor.normal_tuning.mask_hf.enr_suppress),
    APM_C_FELD(suppressor.normal_tuning.mask_hf.emr_transparent),
    APM_C_FELD(suppressor.normal_tuning.max_inc_factor),
    APM_C_FELD(suppressor.normal_tuning.max_dec_factor_lf),
    APM_C_FELD(suppressor.nearend_tuning.mask_lf.enr_transparent),
    APM_C_FELD(suppressor.nearend_tuning.mask_lf.enr_suppress),
    APM_C_FELD(suppressor.nearend_tuning.mask_lf.emr_transparent),
    APM_C_FELD(suppressor.nearend_tuning.mask_hf.enr_transparent),
    APM_C_FELD(suppressor.nearend_tuning.mask_hf.enr_suppress),
    APM_C_FELD(suppressor.nearend_tuning.mask_hf.emr_transparent),
    APM_C_FELD(suppressor.nearend_tuning.max_inc_factor),
    APM_C_FELD(suppressor.nearend_tuning.max_dec_factor_lf),
    APM_C_FELD(suppressor.lf_smoothing_during_initial_phase),
    APM_C_FELD(suppressor.last_permanent_lf_smoothing_band),
    APM_C_FELD(suppressor.last_lf_smoothing_band),
    APM_C_FELD(suppressor.last_lf_band),
    APM_C_FELD(suppressor.first_hf_band),
    APM_C_FELD(suppressor.dominant_nearend_detection.enr_threshold),
    APM_C_FELD(suppressor.dominant_nearend_detection.enr_exit_threshold),
    APM_C_FELD(suppressor.dominant_nearend_detection.snr_threshold),
    APM_C_FELD(suppressor.dominant_nearend_detection.hold_duration),
    APM_C_FELD(suppressor.dominant_nearend_detection.trigger_threshold),
    APM_C_FELD(suppressor.dominant_nearend_detection.use_during_initial_phase),
    APM_C_FELD(suppressor.dominant_nearend_detection.use_unbounded_echo_spectrum),
    APM_C_FELD(suppressor.subband_nearend_detection.nearend_average_blocks),
    APM_C_FELD(suppressor.subband_nearend_detection.subband1.low),
    APM_C_FELD(suppressor.subband_nearend_detection.subband1.high),
    APM_C_FELD(suppressor.subband_nearend_detection.subband2.low),
    APM_C_FELD(suppressor.subband_nearend_detection.subband2.high),
    APM_C_FELD(suppressor.subband_nearend_detection.nearend_threshold),
    APM_C_FELD(suppressor.subband_nearend_detection.snr_threshold),
    APM_C_FELD(suppressor.use_subband_nearend_detection),
    APM_C_FELD(suppressor.high_bands_suppression.enr_threshold),
    APM_C_FELD(suppressor.high_bands_suppression.max_gain_during_echo),
    APM_C_FELD(suppressor.high_bands_suppression.anti_howling_activation_threshold),
    APM_C_FELD(suppressor.high_bands_suppression.anti_howling_gain),
    APM_C_FELD(suppressor.floor_first_increase),
    APM_C_FELD(suppressor.conservative_hf_suppression),
};

#undef APM_C_FELD

// Obergrenze fuer size_t-Felder; darueber ist jeder Wert in EchoCanceller3Config sinnlos (die
// groessten Grenzen in Validate liegen bei 5000 bzw. 100000).
constexpr double kMaxGroesse = 1.0e6;

// Voreinstellung JG TacRelay (Modul aec3, 29.09.2026), als Aenderungen gegenueber der
// Upstream-Vorgabe von webrtc. Begruendung und Messwerte: docs\MODULBERICHT-aec3.md.
//   max_inc_factor: Um diesen Faktor darf die Unterdrueckungsverstaerkung (Leistung) je 4-ms-Block steigen.
//   Upstream 2 (3 dB je Block): Nach einer Phase mit Spielton in der Referenz braucht der Unterdruecker bis zu
//   70 ms, bis ein eigener Anlaut durchkommt, und RNNoise erkennt ihn dann oft nicht mehr als Sprache.
//   4 bzw. 8 (6 bzw. 9 dB je Block) halbieren die verpassten Anlaute im Headsetfall; das Restecho steigt dabei im
//   Mittel ueber zehn Echolaufzeiten um 2 bis 4 dB (verzerrender Lautsprecher 7,5 dB), die Echotests bleiben ohne
//   Fehlphase.
constexpr char kVoreinstellungTacRelay[] =
    "suppressor.normal_tuning.max_inc_factor=4;"
    "suppressor.nearend_tuning.max_inc_factor=8";

const Feld* FeldSuchen(std::string_view name) {
  for (const Feld& f : kFelder) {
    if (name == f.name) return &f;
  }
  return nullptr;
}

std::string_view Trimmen(std::string_view s) {
  while (!s.empty() && (s.front() == ' ' || s.front() == '\t')) s.remove_prefix(1);
  while (!s.empty() && (s.back() == ' ' || s.back() == '\t')) s.remove_suffix(1);
  return s;
}

// Setzt ein Feld aus Text. Falsch bei unbekanntem Namen, unlesbarer Zahl oder unpassendem Wert.
bool FeldSetzen(Aec3Config& c, std::string_view name, std::string_view text) {
  const Feld* f = FeldSuchen(name);
  if (!f || text.empty()) return false;
  double wert = 0.0;
  const auto [ende, fehler] = std::from_chars(text.data(), text.data() + text.size(), wert);
  if (fehler != std::errc() || ende != text.data() + text.size() || !std::isfinite(wert)) {
    return false;
  }
  void* ort = f->ort(c);
  switch (f->art) {
    case Art::Gleitkomma:
      if (std::fabs(wert) > static_cast<double>(std::numeric_limits<float>::max())) return false;
      *static_cast<float*>(ort) = static_cast<float>(wert);
      return true;
    case Art::Ganzzahl:
      if (wert != std::floor(wert) || wert < -1.0e6 || wert > 1.0e6) return false;
      *static_cast<int*>(ort) = static_cast<int>(wert);
      return true;
    case Art::Groesse:
      if (wert != std::floor(wert) || wert < 0.0 || wert > kMaxGroesse) return false;
      *static_cast<size_t*>(ort) = static_cast<size_t>(wert);
      return true;
    case Art::Schalter:
      if (wert != 0.0 && wert != 1.0) return false;
      *static_cast<bool*>(ort) = wert != 0.0;
      return true;
  }
  return false;
}

void FeldSchreiben(const Feld& f, Aec3Config& c, std::string& ziel) {
  char puffer[64];
  std::to_chars_result r{};
  void* ort = f.ort(c);
  switch (f.art) {
    case Art::Gleitkomma:  // kuerzeste Darstellung, die denselben float ergibt
      r = std::to_chars(puffer, puffer + sizeof(puffer), *static_cast<float*>(ort));
      break;
    case Art::Ganzzahl:
      r = std::to_chars(puffer, puffer + sizeof(puffer), *static_cast<int*>(ort));
      break;
    case Art::Groesse:
      r = std::to_chars(puffer, puffer + sizeof(puffer), *static_cast<size_t*>(ort));
      break;
    case Art::Schalter:
      puffer[0] = *static_cast<bool*>(ort) ? '1' : '0';
      r.ptr = puffer + 1;
      break;
  }
  ziel += f.name;
  ziel += '=';
  ziel.append(puffer, r.ptr);
  ziel += ';';
}

std::string Beschreiben(Aec3Config c) {
  std::string s;
  s.reserve(4096);
  for (const Feld& f : kFelder) FeldSchreiben(f, c, s);
  return s;
}

// Prueft eine fertige Konfiguration: EchoCanceller3Config::Validate darf nichts aendern, dazu
// die Bedingungen, die Upstream nur per RTC_DCHECK prueft (im Release-Bau also gar nicht; eine
// Verletzung teilt in SuppressionGain durch null).
bool Gueltig(const Aec3Config& c) {
  Aec3Config kopie = c;
  if (!Aec3Config::Validate(&kopie)) return false;
  const auto maske_gut = [](const Aec3Config::Suppressor::MaskingThresholds& m) {
    return m.enr_transparent < m.enr_suppress;
  };
  const auto& s = c.suppressor;
  if (!maske_gut(s.normal_tuning.mask_lf) || !maske_gut(s.normal_tuning.mask_hf) ||
      !maske_gut(s.nearend_tuning.mask_lf) || !maske_gut(s.nearend_tuning.mask_hf)) {
    return false;
  }
  if (!(s.dominant_nearend_detection.enr_exit_threshold >= 0.0f)) return false;
  if (!(c.echo_audibility.floor_power > 0.0f)) return false;
  for (float schwelle : {c.echo_audibility.audibility_threshold_lf,
                         c.echo_audibility.audibility_threshold_mf,
                         c.echo_audibility.audibility_threshold_hf}) {
    // WeightEchoForAudibility teilt durch floor_power * (schwelle - 1).
    if (!(schwelle > 1.0f)) return false;
  }
  return c.filter.config_change_duration_blocks > 0;  // RTC_DCHECK_LT(0, ...) in SuppressionGain
}

// Wendet "name=wert;name=wert" auf c an. "voreinstellung=webrtc|tacrelay" setzt c zurueck.
// Rueckgabe: -1 bei Erfolg, sonst die Position des fehlerhaften Eintrags im Text.
long long SpecAnwenden(Aec3Config& c, std::string_view spec);

Aec3Config Voreinstellung(bool tacrelay) {
  Aec3Config c;  // Upstream-Vorgabe
  if (tacrelay) {
    const long long fehler = SpecAnwenden(c, kVoreinstellungTacRelay);
    (void)fehler;  // im Rauchtest geprueft; eine kaputte Voreinstellung faellt dort sofort auf
  }
  return c;
}

long long SpecAnwenden(Aec3Config& c, std::string_view spec) {
  size_t pos = 0;
  while (pos <= spec.size()) {
    size_t ende = spec.find(';', pos);
    if (ende == std::string_view::npos) ende = spec.size();
    const std::string_view eintrag = Trimmen(spec.substr(pos, ende - pos));
    if (!eintrag.empty()) {
      const size_t gleich = eintrag.find('=');
      if (gleich == std::string_view::npos) return static_cast<long long>(pos);
      const std::string_view name = Trimmen(eintrag.substr(0, gleich));
      const std::string_view wert = Trimmen(eintrag.substr(gleich + 1));
      if (name == "voreinstellung") {
        if (wert == "webrtc") {
          c = Voreinstellung(false);
        } else if (wert == "tacrelay") {
          c = Voreinstellung(true);
        } else {
          return static_cast<long long>(pos);
        }
      } else if (!FeldSetzen(c, name, wert)) {
        return static_cast<long long>(pos);
      }
    }
    pos = ende + 1;
  }
  return -1;
}

// Was die Fabrik zuletzt erzeugt hat; apm_aec3_describe(..., wirksam = 1) liest es.
struct Aec3Protokoll {
  std::mutex sperre;
  std::optional<Aec3Config> wirksam;
  long long erzeugt = 0;
};

// Erzeugt EchoCanceller3 mit der Konfiguration der Instanz. Das APM ruft Create beim Aufbau und bei
// jeder Neuinitialisierung (etwa bei einem Formatwechsel); jedes Mal gilt dieselbe Konfiguration.
class Aec3Fabrik final : public webrtc::EchoControlFactory {
 public:
  Aec3Fabrik(const Aec3Config& config, Aec3Protokoll* protokoll)
      : config_(config), protokoll_(protokoll) {}

  std::unique_ptr<webrtc::EchoControl> Create(int sample_rate_hz,
                                              int num_render_channels,
                                              int num_capture_channels) override {
    // Wie AudioProcessingImpl::InitializeEchoController ohne Fabrik, nur mit eigener Konfiguration:
    // keine eigene Mehrkanal-Konfiguration (use_setup_specific_default_aec3_config_ ist aus).
    auto aec = std::make_unique<webrtc::EchoCanceller3>(
        config_, std::nullopt, sample_rate_hz, static_cast<size_t>(num_render_channels),
        static_cast<size_t>(num_capture_channels));
    std::lock_guard<std::mutex> l(protokoll_->sperre);
    protokoll_->wirksam = config_;
    ++protokoll_->erzeugt;
    return aec;
  }

 private:
  const Aec3Config config_;
  Aec3Protokoll* const protokoll_;
};

// Grundeinstellung: alle Stufen aus, nur die Kanalfuehrung passend zur Kanalzahl.
webrtc::AudioProcessing::Config GrundConfig(int channels) {
  webrtc::AudioProcessing::Config c;
  c.pipeline.maximum_internal_processing_rate = 48000;
  c.pipeline.multi_channel_capture = channels > 1;
  c.pipeline.multi_channel_render = channels > 1;
  c.echo_canceller.enabled = false;
  c.high_pass_filter.enabled = false;
  c.noise_suppression.enabled = false;
  c.gain_controller1.enabled = false;
  c.gain_controller2.enabled = false;
  return c;
}

}  // namespace

struct apm_handle {
  // Umbau (exklusiv, apm_configure und apm_aec3_configure) gegen Verarbeitung (geteilt, Capture
  // und Render duerfen weiter gleichzeitig laufen). Eine eigene AEC3-Konfiguration geht nur ueber
  // eine EchoControlFactory, und die legt das APM beim Bau fest; mit Fabrik laeuft der AEC immer
  // (AudioProcessingImpl::InitializeEchoController). Deshalb baut die Schicht das APM beim Ein- und
  // Ausschalten des AEC neu auf: aus = ohne Fabrik, genau wie bis 1.0.0.
  std::shared_mutex sperre;
  // Vorrang fuer den Umbau: std::shared_mutex (unter MSVC ein SRW-Lock) ist nicht fair. Nehmen Capture und Render die
  // geteilte Sperre dicht hintereinander, wartete ein Umbau gemessen bis zu 1,4 s. Solange ein Umbau wartet, lassen neue
  // Verarbeitungsaufrufe ihm den Vortritt; sie warten dann ohnehin, bis er fertig ist.
  std::atomic<int> umbau_wartet{0};
  Aec3Protokoll protokoll;  // vor apm: die Fabrik im APM zeigt hierher
  rtc::scoped_refptr<webrtc::AudioProcessing> apm;
  webrtc::AudioProcessing::Config config;  // zuletzt angewandt
  Aec3Config aec3 = Voreinstellung(true);  // fuer den naechsten Aufbau mit AEC
  webrtc::ProcessingConfig pc;
  webrtc::StreamConfig stream;  // Capture und Render gleich
  int frames = 0;               // Werte je Kanal und 10-ms-Rahmen
  int channels = 0;
  std::atomic<int> delay_ms{0};
  std::atomic<bool> delay_gesetzt{false};
  std::atomic<bool> aec_an{false};
  std::atomic<long long> aec_rahmen{0};  // Capture-Rahmen seit dem Einschalten des AEC

  // Stand der AEC-Kennwerte nach dem letzten Capture-Rahmen. Das APM reicht seine Statistik
  // ueber eine SwapQueue der Laenge 1 weiter (audio_processing_impl.cc, ApmStatsReporter):
  // Solange niemand abholt, werden neuere Werte verworfen, und GetStatistics() liefert spaeter
  // den Stand vom ersten Rahmen nach dem letzten Abruf. Deshalb holt apm_process_capture die
  // Werte nach jedem Rahmen ab, und apm_get_stats liest nur noch diese Kopie.
  std::atomic<float> st_erl{NAN};
  std::atomic<float> st_erle{NAN};
  std::atomic<int> st_delay{-1};

  void StatsLeeren() {
    st_erl.store(NAN, std::memory_order_relaxed);
    st_erle.store(NAN, std::memory_order_relaxed);
    st_delay.store(-1, std::memory_order_relaxed);
  }

  // Geteilte Sperre fuer Capture und Render; laesst einem wartenden Umbau den Vortritt.
  std::shared_lock<std::shared_mutex> Verarbeiten() {
    while (umbau_wartet.load(std::memory_order_acquire) != 0) {
      std::this_thread::yield();
    }
    return std::shared_lock<std::shared_mutex>(sperre);
  }

  // Exklusive Sperre fuer Umbau und Konfiguration.
  std::unique_lock<std::shared_mutex> Umbauen() {
    umbau_wartet.fetch_add(1, std::memory_order_acq_rel);
    std::unique_lock<std::shared_mutex> l(sperre);
    umbau_wartet.fetch_sub(1, std::memory_order_acq_rel);
    return l;
  }

  // Baut das APM mit Konfiguration c neu auf, mit AEC3-Fabrik genau dann, wenn c den AEC
  // einschaltet. Nur unter exklusiver Sperre. Scheitert der Aufbau, bleibt das alte APM.
  bool NeuAufbauen(const webrtc::AudioProcessing::Config& c) {
    webrtc::AudioProcessingBuilder b;
    b.SetConfig(c);
    if (c.echo_canceller.enabled) {
      b.SetEchoControlFactory(std::make_unique<Aec3Fabrik>(aec3, &protokoll));
    } else {
      std::lock_guard<std::mutex> l(protokoll.sperre);
      protokoll.wirksam.reset();
    }
    rtc::scoped_refptr<webrtc::AudioProcessing> neu = b.Create();
    if (!neu || neu->Initialize(pc) != webrtc::AudioProcessing::kNoError) return false;
    apm = neu;
    config = c;
    aec_an.store(c.echo_canceller.enabled, std::memory_order_relaxed);
    aec_rahmen.store(0, std::memory_order_relaxed);
    StatsLeeren();
    return true;
  }
};

extern "C" {

APM_API apm_handle* APM_CALL apm_create(int sample_rate_hz, int channels) {
  if (sample_rate_hz < kMinRateHz || sample_rate_hz > kMaxRateHz ||
      sample_rate_hz % 100 != 0) {
    return nullptr;
  }
  if (channels < 1 || channels > kMaxKanaele) {
    return nullptr;
  }
  try {
    FeldversucheSetzen();
    auto h = std::make_unique<apm_handle>();
    h->stream = webrtc::StreamConfig(sample_rate_hz, static_cast<size_t>(channels));
    h->frames = sample_rate_hz / 100;
    h->channels = channels;
    h->pc.input_stream() = h->stream;
    h->pc.output_stream() = h->stream;
    h->pc.reverse_input_stream() = h->stream;
    h->pc.reverse_output_stream() = h->stream;
    if (!h->NeuAufbauen(GrundConfig(channels))) {
      return nullptr;
    }
    return h.release();
  } catch (...) {
    return nullptr;
  }
}

APM_API void APM_CALL apm_destroy(apm_handle* handle) {
  try {
    delete handle;
  } catch (...) {
  }
}

APM_API int APM_CALL apm_configure(apm_handle* handle,
                                   int echo_cancel,
                                   int high_pass,
                                   int noise_suppression_level,
                                   int agc2_adaptive,
                                   float agc2_max_gain_db) {
  if (!handle) {
    return APM_ERR_NULLZEIGER;
  }
  if (noise_suppression_level < -1 || noise_suppression_level > 3) {
    return APM_ERR_PARAMETER;
  }
  if (agc2_adaptive && !(std::isfinite(agc2_max_gain_db) && agc2_max_gain_db > 0.0f &&
                         agc2_max_gain_db <= kMaxAgc2GainDb)) {
    return APM_ERR_PARAMETER;
  }
  try {
    webrtc::AudioProcessing::Config c = GrundConfig(handle->channels);

    c.echo_canceller.enabled = echo_cancel != 0;
    c.echo_canceller.mobile_mode = false;  // AEC3, nicht AECM

    c.high_pass_filter.enabled = high_pass != 0;

    if (noise_suppression_level >= 0) {
      c.noise_suppression.enabled = true;
      c.noise_suppression.level =
          static_cast<webrtc::AudioProcessing::Config::NoiseSuppression::Level>(
              noise_suppression_level);
    }

    if (agc2_adaptive) {
      c.gain_controller2.enabled = true;
      c.gain_controller2.adaptive_digital.enabled = true;
      c.gain_controller2.adaptive_digital.max_gain_db = agc2_max_gain_db;
      // Startwert nie ueber der Obergrenze, sonst setzt die AGC2 anfangs zu hoch ein.
      c.gain_controller2.adaptive_digital.initial_gain_db =
          agc2_max_gain_db < kAgc2StandardStartDb ? agc2_max_gain_db : kAgc2StandardStartDb;
    }

    auto l = handle->Umbauen();
    if (c.echo_canceller.enabled != handle->config.echo_canceller.enabled) {
      // AEC ein oder aus: neues APM (mit bzw. ohne Fabrik). Die Anlaufphase der Kennwerte
      // beginnt von vorn, ebenso Hochpass, NS und AGC2.
      return handle->NeuAufbauen(c) ? APM_OK : APM_ERR_UNBESTIMMT;
    }
    handle->apm->ApplyConfig(c);
    handle->config = c;
    return APM_OK;
  } catch (...) {
    return APM_ERR_UNBESTIMMT;
  }
}

APM_API int APM_CALL apm_aec3_configure(apm_handle* handle, const char* spec,
                                        int* fehler_position) {
  if (fehler_position) *fehler_position = -1;
  if (!handle || !spec) {
    return APM_ERR_NULLZEIGER;
  }
  try {
    auto l = handle->Umbauen();
    Aec3Config neu = handle->aec3;
    const long long fehler = SpecAnwenden(neu, spec);
    if (fehler >= 0 || !Gueltig(neu)) {
      if (fehler_position) {
        *fehler_position = fehler >= 0 ? static_cast<int>(fehler) : static_cast<int>(std::strlen(spec));
      }
      return APM_ERR_PARAMETER;
    }
    const Aec3Config alt = handle->aec3;
    handle->aec3 = neu;
    if (handle->config.echo_canceller.enabled && !handle->NeuAufbauen(handle->config)) {
      handle->aec3 = alt;
      return APM_ERR_UNBESTIMMT;
    }
    return APM_OK;
  } catch (...) {
    return APM_ERR_UNBESTIMMT;
  }
}

APM_API int APM_CALL apm_aec3_describe(apm_handle* handle, int wirksam, char* buffer,
                                       int buffer_len) {
  if (buffer && buffer_len > 0) buffer[0] = '\0';
  if (!handle || (!buffer && buffer_len != 0) || buffer_len < 0) {
    return APM_ERR_NULLZEIGER;
  }
  if (wirksam != 0 && wirksam != 1) {
    return APM_ERR_PARAMETER;
  }
  try {
    std::optional<Aec3Config> c;
    if (wirksam) {
      std::lock_guard<std::mutex> l(handle->protokoll.sperre);
      c = handle->protokoll.wirksam;
      if (!c) return APM_ERR_NICHT_AN;
    } else {
      std::shared_lock<std::shared_mutex> l(handle->sperre);
      c = handle->aec3;
    }
    const std::string text = Beschreiben(*c);
    const int noetig = static_cast<int>(text.size());
    if (buffer && buffer_len > noetig) {
      std::memcpy(buffer, text.c_str(), text.size() + 1);
    }
    return noetig;
  } catch (...) {
    return APM_ERR_UNBESTIMMT;
  }
}

APM_API int APM_CALL apm_set_stream_delay_ms(apm_handle* handle, int delay_ms) {
  if (!handle) {
    return APM_ERR_NULLZEIGER;
  }
  int rc = APM_OK;
  if (delay_ms < 0) {
    delay_ms = 0;
    rc = APM_WARN_BEGRENZT;
  } else if (delay_ms > kMaxDelayMs) {
    delay_ms = kMaxDelayMs;
    rc = APM_WARN_BEGRENZT;
  }
  handle->delay_ms.store(delay_ms, std::memory_order_relaxed);
  handle->delay_gesetzt.store(true, std::memory_order_release);
  return rc;
}

APM_API int APM_CALL apm_process_capture(apm_handle* handle, float* samples_10ms) {
  if (!handle || !samples_10ms) {
    return APM_ERR_NULLZEIGER;
  }
  try {
    auto l = handle->Verarbeiten();
    float* kanal[kMaxKanaele] = {};
    for (int c = 0; c < handle->channels; ++c) {
      kanal[c] = samples_10ms + static_cast<size_t>(c) * static_cast<size_t>(handle->frames);
    }
    if (handle->delay_gesetzt.load(std::memory_order_acquire)) {
      handle->apm->set_stream_delay_ms(handle->delay_ms.load(std::memory_order_relaxed));
    }
    const int rc = handle->apm->ProcessStream(kanal, handle->stream, handle->stream, kanal);
    if (rc == webrtc::AudioProcessing::kNoError && handle->aec_an.load(std::memory_order_relaxed)) {
      // Abholen haelt die SwapQueue leer, damit der naechste Rahmen seine Werte ablegen kann.
      const webrtc::AudioProcessingStats s = handle->apm->GetStatistics();
      handle->st_erl.store(s.echo_return_loss ? static_cast<float>(*s.echo_return_loss) : NAN,
                           std::memory_order_relaxed);
      handle->st_erle.store(s.echo_return_loss_enhancement
                                ? static_cast<float>(*s.echo_return_loss_enhancement)
                                : NAN,
                            std::memory_order_relaxed);
      handle->st_delay.store(s.delay_ms ? static_cast<int>(*s.delay_ms) : -1,
                             std::memory_order_relaxed);
      handle->aec_rahmen.fetch_add(1, std::memory_order_relaxed);
    }
    return rc;
  } catch (...) {
    return APM_ERR_UNBESTIMMT;
  }
}

APM_API int APM_CALL apm_process_render(apm_handle* handle, const float* samples_10ms) {
  if (!handle || !samples_10ms) {
    return APM_ERR_NULLZEIGER;
  }
  try {
    auto l = handle->Verarbeiten();
    const float* kanal[kMaxKanaele] = {};
    for (int c = 0; c < handle->channels; ++c) {
      kanal[c] = samples_10ms + static_cast<size_t>(c) * static_cast<size_t>(handle->frames);
    }
    // Nur Analyse: der Wiedergaberahmen bleibt unveraendert (const).
    return handle->apm->AnalyzeReverseStream(kanal, handle->stream);
  } catch (...) {
    return APM_ERR_UNBESTIMMT;
  }
}

APM_API int APM_CALL apm_get_stats(apm_handle* handle, float* erl, float* erle, int* delay_ms) {
  if (erl) *erl = NAN;
  if (erle) *erle = NAN;
  if (delay_ms) *delay_ms = -1;
  if (!handle) {
    return APM_ERR_NULLZEIGER;
  }
  if (!handle->aec_an.load(std::memory_order_relaxed)) {
    return APM_STATS_UNGUELTIG;
  }
  // Nur die Kopie vom letzten Capture-Rahmen lesen: keine Sperre im APM, jederzeit aufrufbar.
  const float l = handle->st_erl.load(std::memory_order_relaxed);
  const float le = handle->st_erle.load(std::memory_order_relaxed);
  const int d = handle->st_delay.load(std::memory_order_relaxed);
  if (erl) *erl = l;
  if (erle) *erle = le;
  if (delay_ms) *delay_ms = d;
  const bool alle = !std::isnan(l) && !std::isnan(le) && d >= 0;
  const bool anlauf_vorbei =
      handle->aec_rahmen.load(std::memory_order_relaxed) >= kAec3AnlaufRahmen;
  const bool erl_geschaetzt = alle && l > kErlPlatzhalterDb;
  return (alle && anlauf_vorbei && erl_geschaetzt) ? APM_OK : APM_STATS_UNGUELTIG;
}

APM_API const char* APM_CALL apm_version(void) {
  return kVersion;
}

}  // extern "C"
