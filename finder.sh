#!/usr/bin/env bash
# FINDER (Linux) — поиск файлов по имени, БЕЗ открытия самих файлов.
# Работает на SteamOS/Arch и любом Linux: используется стандартный `find`,
# который читает только имена в каталогах, содержимое файлов не трогается.
#
# Использование:
#   ./finder.sh                     — интерактивный режим (спросит что и где)
#   ./finder.sh <что> [где]         — например: ./finder.sh steam  ~/
#   ./finder.sh "*.log" /var/log    — по маске
#   ./finder.sh -e pdf ~/Documents  — по расширению
#   ./finder.sh -all steam          — искать по всей системе (от /)
#
# © 1565gfd

set -o pipefail

# ---- цвета ----
B=$'\e[38;2;122;162;247m'   # синий
L=$'\e[38;2;192;202;245m'   # светлый
M=$'\e[38;2;187;154;247m'   # сирень
C=$'\e[38;2;125;207;255m'   # циан
G=$'\e[38;2;158;227;161m'   # зелёный
R=$'\e[38;2;243;139;168m'   # красный
D=$'\e[38;2;105;112;134m'   # серый
Z=$'\e[0m'

banner() {
  echo
  echo "${B}   █████ █ █   █ ████  █████ ████ ${Z}"
  echo "${B}   █     █ ██  █ █   █ █     █   █${Z}"
  echo "${B}   ████  █ █ █ █ █   █ ████  ████ ${Z}"
  echo "${B}   █     █ █  ██ █   █ █     █  █ ${Z}"
  echo "${B}   █     █ █   █ ████  █████ █   █${Z}"
  echo "${D}   поиск файлов • Linux • © 1565gfd${Z}"
  echo
}

# ---- аргументы ----
MODE="name"      # name | ext
PATTERN=""
ROOT=""
ALL=0

while [ $# -gt 0 ]; do
  case "$1" in
    -e|--ext)   MODE="ext"; PATTERN="$2"; shift 2 ;;
    -all|--all) ALL=1; shift ;;
    -h|--help)  banner; sed -n '3,17p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) if [ -z "$PATTERN" ]; then PATTERN="$1"; elif [ -z "$ROOT" ]; then ROOT="$1"; fi; shift ;;
  esac
done

banner

# ---- интерактивно, если не задан шаблон ----
if [ -z "$PATTERN" ]; then
  printf "%s" "${M}> ${L}Что искать (имя, маска *.pdf): ${Z}"
  read -r PATTERN
  [ -z "$PATTERN" ] && { echo "${R}   Пусто.${Z}"; exit 1; }
  printf "%s" "${M}> ${L}Где (Enter = домашняя папка, / = вся система): ${Z}"
  read -r ROOT
fi

# ---- куда искать ----
if [ "$ALL" -eq 1 ]; then
  ROOT="/"
elif [ -z "$ROOT" ]; then
  ROOT="$HOME"
elif [ "$ROOT" = "/" ]; then
  ALL=1
fi

if [ ! -d "$ROOT" ]; then
  echo "${R}   Папка не найдена: $ROOT${Z}"
  exit 1
fi

# ---- шаблон find ----
if [ "$MODE" = "ext" ]; then
  NAME="*.${PATTERN#.}"
elif printf '%s' "$PATTERN" | grep -q '[*?]'; then
  NAME="$PATTERN"                 # уже маска
else
  NAME="*${PATTERN}*"             # часть имени
fi

echo "${D}   Ищу '${PATTERN}' в: ${ROOT}${Z}"
echo "${D}   (файлы НЕ открываются, читаются только имена)${Z}"
echo

start=$(date +%s.%N)
count=0

# псевдо-каталоги пропускаем, чтобы не зашумлять и не зависать
# -iname = без учёта регистра; вывод — только пути
while IFS= read -r path; do
  dir=$(dirname "$path")
  base=$(basename "$path")
  printf "   %s%s/%s%s%s\n" "$D" "$dir" "$L" "$base" "$Z"
  count=$((count+1))
done < <(find "$ROOT" \
            \( -path /proc -o -path /sys -o -path /dev -o -path /run \) -prune -o \
            -type f -iname "$NAME" -print 2>/dev/null)

end=$(date +%s.%N)
elapsed=$(awk "BEGIN{printf \"%.1f\", $end-$start}")

echo
if [ "$count" -gt 0 ]; then
  echo "${G}   ● Готово за ${elapsed} с. Найдено: ${count}${Z}"
else
  echo "${R}   ● Ничего не найдено (${elapsed} с).${Z}"
fi
