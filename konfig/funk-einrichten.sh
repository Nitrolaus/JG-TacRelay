#!/usr/bin/env bash
# JG TacRelay - Maintainer: Sarevoc
# JG-TacRelay-Server einrichten, beliebig oft (EINRICHTEN.md). Im entpackten Ordner linux des Serverpakets:
#   sudo bash funk-einrichten.sh                         erster Lauf, endet mit WARTET AUF PROFIL
#   sudo bash funk-einrichten.sh --profil profil.json    mit dem Profil vom Eigentümer: prüfen, einspielen, starten
#   sudo bash funk-einrichten.sh --umzug <paket> [--mit-serverschluessel]    Umzugspaket einspielen (UMZUG.md)
# Jede Zeile beginnt mit OK, NEU, BLEIBT oder FEHLT; bei FEHLT steht die Abhilfe dabei. Firewall und Router werden
# nur angezeigt; angefasst wird dort nichts ohne dein ausdrückliches ja.
#
# Hier steht nur, was vor .NET laufen muss: root, systemd, apt, libicu, .NET-Laufzeit 10 (Wege A und B aus
# HEIMSERVER-PROXMOX.md, Abschnitt 3) und die Programme nach /opt/funk. Alles Weitere macht "funk-admin einrichten".
# Falle: Mit pipefail scheitert "programm | grep -q" zufällig (grep endet früh, das Programm bekommt SIGPIPE).
# Deshalb hier nie "| grep -q"; erst in eine Variable, dann vergleichen.
set -euo pipefail

ziel=/opt/funk
paket=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
weiter=("$@")

melde() { printf '%-7s%s\n' "$1" "$2"; }
fehlt() { melde FEHLT "$1"; exit "${2:-1}"; }
frage() { # Eingabetaste heißt ja; Ende der Eingabe heißt nein
  local a
  read -r -p "$1 (j/n) [j]: " a || return 1
  case "${a,,}" in "" | j | ja) return 0 ;; *) return 1 ;; esac
}

while [ $# -gt 0 ]; do
  case "$1" in
    -h | --hilfe) sed -n '2,7s/^# \{0,1\}//p' "${BASH_SOURCE[0]}"; exit 0 ;;
    --profil | --umzug) [ $# -ge 2 ] || fehlt "$1 braucht eine Datei. Nichts geändert." 2; shift 2 ;;
    --mit-serverschluessel) shift ;;
    *) fehlt "Unbekannte Angabe $1. Erlaubt: --profil <datei>, --umzug <paket>, --mit-serverschluessel. Nichts geändert." 2 ;;
  esac
done

# ---- root, systemd, Rechner ----
[ "$(id -u)" -eq 0 ] || fehlt "Nicht als root. Abhilfe: sudo bash $0 ${weiter[*]-}"
[ -d /run/systemd/system ] || fehlt "Kein systemd. Abhilfe: Der Server läuft als systemd-Dienst; einen Rechner mit systemd nehmen (Debian, Ubuntu)."
[ "$(uname -m)" = x86_64 ] || fehlt "Rechner $(uname -m): Die Programme sind für x86_64 gebaut und nur dort geprüft. Abhilfe: einen x86_64-Rechner nehmen."
[ -r /etc/os-release ] || fehlt "/etc/os-release fehlt. Abhilfe: Debian oder Ubuntu nehmen."
os_id=$(. /etc/os-release && echo "${ID:-}")
os_like=$(. /etc/os-release && echo "${ID_LIKE:-}")
os_version=$(. /etc/os-release && echo "${VERSION_ID:-}")
os_name=$(. /etc/os-release && echo "${PRETTY_NAME:-${ID:-unbekannt}}")
case " $os_id $os_like " in *" debian "* | *" ubuntu "*) apt=ja ;; *) apt=nein ;; esac

icu_da() { local l; l=$(ldconfig -p 2>/dev/null || true); [[ $l == *libicuuc.so* ]]; }
dotnet_da() {
  [ -x /usr/bin/dotnet ] || return 1
  local r; r=$(/usr/bin/dotnet --list-runtimes 2>/dev/null || true)
  [[ $r == *"Microsoft.NETCore.App 10."* ]]
}
holen() { # holen <url> <datei>
  if command -v curl >/dev/null; then curl -fsSL -o "$2" "$1"
  elif command -v wget >/dev/null; then wget -q -O "$2" "$1"
  else installieren ca-certificates wget && wget -q -O "$2" "$1"; fi
}
apt_frisch=nein
apt_holen() {
  [ "$apt_frisch" = ja ] && return 0
  DEBIAN_FRONTEND=noninteractive apt-get update -qq || fehlt "apt-get update scheitert. Abhilfe: Netz und Paketquellen prüfen; apt-get update zeigt den Grund."
  apt_frisch=ja
}
installieren() {
  apt_holen
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq "$@" >/dev/null || fehlt "apt-get install $* scheitert. Abhilfe: apt-get install $* von Hand zeigt den Grund."
}
kandidat() { { LC_ALL=C apt-cache policy "$1" 2>/dev/null || true; } | sed -n 's/^ *Candidate: *//p'; }
kandidat_da() { local k; k=$(kandidat dotnet-runtime-10.0); [ -n "$k" ] && [ "$k" != "(none)" ]; }

if [ "$apt" = nein ]; then
  luecken=""
  icu_da || luecken="libicu (ICU)"
  dotnet_da || luecken="${luecken:+$luecken, }.NET-Laufzeit 10 (Microsoft.NETCore.App) unter /usr/bin/dotnet"
  [ -z "$luecken" ] || fehlt "$os_name ist nicht Debian oder Ubuntu, und es fehlt: $luecken. Abhilfe: von Hand nachinstallieren und noch einmal aufrufen."
  melde OK "$os_name: libicu und .NET-Laufzeit 10 sind da"
else
  melde OK "$os_name, root, systemd"
  if icu_da; then
    melde OK "libicu ist da"
  else
    apt_holen
    icu=$({ apt-cache search --names-only '^libicu[0-9]+$' 2>/dev/null || true; } | cut -d' ' -f1 | sort -V | tail -n 1)
    [ -n "$icu" ] || fehlt "Kein Paket libicu<zahl> in den Paketquellen. Abhilfe: apt-cache search libicu zeigt, wie es hier heißt; dann apt install <paket>."
    installieren "$icu"
    melde NEU "$icu installiert"
  fi
fi

# ---- .NET-Laufzeit 10: aus den Paketquellen, sonst Weg A (Paketquelle von Microsoft), sonst Weg B (Skript) ----
if [ ! -e /usr/bin/dotnet ] && [ -x /usr/share/dotnet/dotnet ]; then
  r=$(/usr/share/dotnet/dotnet --list-runtimes 2>/dev/null || true)
  if [[ $r == *"Microsoft.NETCore.App 10."* ]]; then
    ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
    melde NEU "Verweis /usr/bin/dotnet auf /usr/share/dotnet/dotnet"
  fi
fi
if dotnet_da; then
  melde OK ".NET-Laufzeit 10 ist da"
elif [ -e /usr/bin/dotnet ]; then
  # Ein .NET ohne Laufzeit 10 gehört womöglich einem anderen Dienst; apt oder das Skript könnten /usr/bin/dotnet umbiegen.
  fehlt "/usr/bin/dotnet gibt es schon, aber ohne Laufzeit 10; es gehört womöglich einem anderen Dienst, und einrichten fasst es nicht an. Abhilfe: die Laufzeit 10 auf demselben Weg dazulegen, auf dem die vorhandene kam (apt install dotnet-runtime-10.0 oder dotnet-install.sh in ihren Ordner), dann noch einmal aufrufen."
elif [ "$apt" = ja ]; then
  apt_holen
  if ! kandidat_da && [ -n "$os_version" ] && { [ "$os_id" = debian ] || [ "$os_id" = ubuntu ]; }; then
    url="https://packages.microsoft.com/config/$os_id/$os_version/packages-microsoft-prod.deb"
    if frage "Die .NET-Laufzeit 10 fehlt in den Paketquellen. Paketquelle von Microsoft eintragen ($url)?"; then
      t=$(mktemp -d)
      if holen "$url" "$t/ms.deb" && dpkg -i "$t/ms.deb" >/dev/null; then
        apt_frisch=nein
        apt_holen
        melde NEU "Paketquelle von Microsoft eingetragen"
      else
        melde FEHLT "Für $os_id $os_version gibt es dort keine Paketquelle. Abhilfe: Weg B, das Installationsskript (gleich die Frage)."
      fi
      rm -rf -- "$t"
    fi
  fi
  if kandidat_da; then
    installieren dotnet-runtime-10.0
    melde NEU "dotnet-runtime-10.0 installiert (Updates kommen mit apt)"
  elif frage "Laufzeit mit dem Installationsskript von Microsoft nach /usr/share/dotnet legen (https://dot.net/v1/dotnet-install.sh)?"; then
    t=$(mktemp -d)
    holen https://dot.net/v1/dotnet-install.sh "$t/dotnet-install.sh" || fehlt "dotnet-install.sh nicht zu laden. Abhilfe: Netz prüfen, dann noch einmal."
    bash "$t/dotnet-install.sh" --channel 10.0 --runtime dotnet --install-dir /usr/share/dotnet >/dev/null || fehlt "dotnet-install.sh scheitert. Abhilfe: es von Hand aufrufen, dann zeigt es den Grund."
    rm -rf -- "$t"
    ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
    melde NEU ".NET-Laufzeit 10 nach /usr/share/dotnet (Installationsskript), Verweis /usr/bin/dotnet; Updates mit demselben Skript"
  fi
fi
dotnet_da || fehlt ".NET-Laufzeit 10 unter /usr/bin/dotnet. Abhilfe: HEIMSERVER-PROXMOX.md, Abschnitt 3, Weg A oder B von Hand."

# ---- Programme nach /opt/funk ----
for d in funk-server.dll funk-admin.dll funk-echo.dll funk-server.deps.json konfig/funk-server.service konfig/funk-echo.service konfig/profilschluessel.txt; do
  [ -e "$paket/$d" ] || fehlt "$paket ist nicht der Ordner linux des Serverpakets ($d fehlt). Abhilfe: im entpackten Ordner linux aufrufen."
done
fassung() { sed -n '/^ *"funk-server\/[0-9]/{s/^ *"funk-server\/\([0-9][0-9.]*\)".*/\1/p;q;}' "$1/funk-server.deps.json" 2>/dev/null || true; }
# Was zum Programmpaket gehört. Nur das wird verglichen und kopiert: Was du daneben in den Ordner legst (profil.json, umzug.tru,
# auf dem Stick profil.key und reserve.key), kommt nie nach /opt/funk und hält nie den Dienst an. Seit 0.3.0 gehören dazu
# HEIMSERVER-HAERTUNG.md (*.md) und die fail2ban-Beispiele für den Proxmox-Host unter konfig/fail2ban (nur zum Nachlesen; in
# Kraft setzt sie niemand von hier, HEIMSERVER-HAERTUNG.md Punkt 7.2).
programmdateien() ( # programmdateien <ordner>: Pfade relativ zum Ordner, je Zeile einer, sortiert
  cd -- "$1" 2>/dev/null || exit 0
  shopt -s nullglob
  for f in *.dll *.deps.json *.runtimeconfig.json *.md funk-einrichten.sh "Profil erneuern.cmd" konfig/* konfig/fail2ban/*/*.conf; do
    if [ -f "$f" ] && [ ! -L "$f" ]; then printf '%s\n' "$f"; fi
  done | LC_ALL=C sort
)
gleich() { # gleich <paket> <ziel>: dieselben Programmdateien, Byte für Byte
  local a b f
  a=$(programmdateien "$1")
  b=$(programmdateien "$2")
  [ "$a" = "$b" ] || return 1
  while IFS= read -r f; do
    if [ -n "$f" ] && ! cmp -s -- "$1/$f" "$2/$f"; then return 1; fi
  done <<< "$a"
  return 0
}
neu=$(fassung "$paket")
if [ "$paket" = "$ziel" ]; then
  melde OK "Programme $neu in $ziel (Aufruf von dort)"
elif [ -e "$ziel" ] && gleich "$paket" "$ziel"; then
  melde OK "Programme $neu in $ziel, unverändert"
else
  if [ -e "$ziel" ]; then
    [ -e "$ziel/funk-server.dll" ] || fehlt "$ziel gibt es schon, aber ohne funk-server.dll; das ist nicht unser Ordner. Abhilfe: nachsehen, was darin liegt, und ihn selbst woanders hinlegen."
    alt=$(fassung "$ziel")
    beiseite="$ziel.${alt:-alt}"
    [ ! -e "$beiseite" ] || beiseite="$beiseite-$(date +%Y%m%d-%H%M%S)"
    if systemctl is-active --quiet funk-server; then
      frage "funk-server läuft mit ${alt:-dem alten Stand}. Für die Programme $neu kurz anhalten?" \
        || fehlt "Programme nicht ersetzt, der Dienst läuft weiter. Abhilfe: später noch einmal aufrufen."
      systemctl stop funk-echo 2>/dev/null || true
      systemctl stop funk-server
      melde NEU "funk-server angehalten; einrichten startet ihn am Ende wieder"
    fi
    mv -- "$ziel" "$beiseite"
    melde NEU "alter Stand ${alt:-ohne Fassung} nach $beiseite"
  fi
  install -d -m 0755 "$ziel" "$ziel/konfig"
  while IFS= read -r f; do
    if [ -n "$f" ]; then
      install -d -m 0755 "$ziel/$(dirname -- "$f")"
      cp -- "$paket/$f" "$ziel/$f"
    fi
  done <<< "$(programmdateien "$paket")"
  melde NEU "Programme $neu nach $ziel"
fi
# Ein tgz aus Windows trägt 0666 und 0777 (CODEMAP, Fallen); deshalb immer, auch bei unveränderten Programmen.
chown -R root:root "$ziel"
chmod -R go-w "$ziel"

exec /usr/bin/dotnet "$ziel/funk-admin.dll" einrichten ${weiter[@]+"${weiter[@]}"}
