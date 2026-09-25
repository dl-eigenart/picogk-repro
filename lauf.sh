#!/bin/bash
# Baut und startet die drei Reproduktionen auf agents-pc. Aufruf: ./lauf.sh
set -u
P=/home/eigenart/leap71/PicoGKRepro
cd "$P" && dotnet build -c Release 2>&1 | tail -3
cp "$P/../PicoGKRuntime/build/lib/picogk.so" "$P/bin/Release/net9.0/libpicogk.26.2.so"
OUT=/tmp/picogk_repro_ergebnis.txt; : > "$OUT"
for m in traegheit innen volumen; do
  case $m in traegheit) VS="5 2 1";; innen) VS="20 5 2 1 0.5";; volumen) VS="5 2 1";; esac
  for v in $VS; do
    echo "===== $m voxel $v =====" >> "$OUT"
    cd "$P" && timeout 900 xvfb-run -a dotnet run -c Release --no-build -- $m $v >> "$OUT" 2>&1
    echo "RC=$?" >> "$OUT"
  done
done
touch /tmp/picogk_repro.done
