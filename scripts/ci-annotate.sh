#!/usr/bin/env bash
# Выводит строки лога одной аннотацией ::error — их видно на странице сборки
# и через Checks API, даже без доступа к полным логам.
#   ci-annotate.sh <заголовок> <файл-лога> [regex для отбора строк]
title="$1"
file="$2"
pattern="${3:-}"
[ -f "$file" ] || exit 0

lines=""
if [ -n "$pattern" ]; then
  lines="$(grep -E "$pattern" "$file" | sed "s#${GITHUB_WORKSPACE:-@@}/##g" | awk '!seen[$0]++' | head -n 40)"
fi
[ -n "$lines" ] || lines="$(tail -n 60 "$file" | sed "s#${GITHUB_WORKSPACE:-@@}/##g")"

msg="$(printf '%s' "$lines" | python3 -c 'import sys; s=sys.stdin.read(); print(s.replace("%","%25").replace("\r","%0D").replace("\n","%0A"), end="")')"
echo "::error title=${title}::${msg}"
