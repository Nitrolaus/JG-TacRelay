# Huelle fuer webrtc_apm.dll (JG TacRelay).
#
# Das Bauskript native\skripte\webrtc-apm.ps1 kopiert diese Datei samt apm_c.* und
# rtc_export_statisch.h in native\bau\webrtc-apm\quelle\ und legt die unveraenderten
# Upstream-Quellen als subprojects\webrtc-audio-processing\ daneben. Die Upstream-Bibliothek
# wird so mit ihrem eigenen meson.build statisch gebaut und in die DLL gelinkt.

project('tacrelay-webrtc-apm', 'c', 'cpp',
  version : '1.0.0',
  meson_version : '>= 1.0',
  default_options : [
    # Upstream nennt c++17, nutzt aber designierte Initialisierer (gain_controller2.cc,
    # agc2/input_volume_stats_reporter.cc), die MSVC erst ab /std:c++20 annimmt (C7555).
    # Die Upstream-CI baut MSVC deshalb mit -Dcpp_std=c++20. Das Bauskript setzt c++20
    # zusaetzlich je Subprojekt, damit abseil denselben Standard bekommt wie webrtc.
    'cpp_std=c++20',
    'warning_level=1',
    'buildtype=release',
    'default_library=static',
    'b_vscrt=mt',
    'b_ndebug=true',
  ]
)

cpp = meson.get_compiler('cpp')
if cpp.get_id() != 'msvc'
  error('Dieses Bauskript ist fuer MSVC gedacht (gefunden: ' + cpp.get_id() + ')')
endif

# Muss vor dem ersten subproject() stehen, damit es fuer alle Ziele gilt.
# Begruendung in rtc_export_statisch.h.
export_sperre = '/FI' + (meson.current_source_dir() / 'rtc_export_statisch.h')
add_global_arguments(export_sperre, language : ['c', 'cpp'])

# Bitgleiche Ausgabe: ohne /Brepro schreiben cl und link Zeitstempel in .obj und DLL,
# dann aendert sich der SHA-256 bei jedem Bau trotz gleicher Quellen.
add_global_arguments('/Brepro', language : ['c', 'cpp'])
add_global_link_arguments('/Brepro', language : ['c', 'cpp'])

apm_proj = subproject('webrtc-audio-processing')
apm_dep = apm_proj.get_variable('audio_processing_dep')

huelle_args = [
  '-DAPM_C_BUILD',
  # Dieselben Makros, mit denen die Upstream-Bibliothek gebaut wird (common_cxxflags und apm_flags
  # aus deren meson.build, ohne WEBRTC_LIBRARY_IMPL/WEBRTC_ENABLE_SYMBOL_EXPORT, die nur RTC_EXPORT
  # betreffen). Seit 1.1.0 Pflicht: apm_c.cpp legt EchoCanceller3 selbst an und sieht dessen
  # Klassendefinition; mit anderen Makros koennte sie ein anderes Speicherlayout haben.
  '-DWEBRTC_WIN',
  '-D_WIN32',
  '-D__STDC_FORMAT_MACROS=1',
  '-DNOMINMAX',
  '-D_USE_MATH_DEFINES',
  '-DWEBRTC_ENABLE_AVX2',
  '-D_WINSOCKAPI_',
  '-DWEBRTC_APM_DEBUG_DUMP=0',
]

shared_library('webrtc_apm',
  'apm_c.cpp',
  cpp_args : huelle_args,
  dependencies : [apm_dep],
  name_prefix : '',
  install : false,
)
