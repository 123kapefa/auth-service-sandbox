#!/usr/bin/env bash
# Проверка всего этапа 10 (docs/keycloak/10-login-methods.md) одной командой: лимиты и капча, звонок вместо SMS,
# способы входа человека и проекта, вход в два шага, смена номера, предложение ключа, настройки и бюджет кодов,
# кабинет. Скрипты шагов идут по очереди, в конце — итог по каждому и общий.
# Запуск: стенд поднят (docker compose up -d --build --wait; стенд старше этапа 10 —
# scripts/project/upgrade-10.py sandbox), затем
#   scripts/keycloak/check-10.sh
# Только часть: scripts/keycloak/check-10.sh check-code-limits check-phone-code
set -uo pipefail

DIR="$(dirname "$0")"
SCRIPTS=("$@")
[ ${#SCRIPTS[@]} -gt 0 ] || SCRIPTS=(check-code-limits check-phone-code check-sign-in-methods check-two-step-login check-phone-change
  check-passkey-offer check-code-settings check-project-sign-in check-budget-alerts check-account)
OUT=$(mktemp); trap 'rm -f "$OUT"' EXIT

TOTAL_PASS=0; TOTAL_FAIL=0; LINES=()
for name in "${SCRIPTS[@]}"; do
  name=${name%.sh}
  printf '\n\033[1m== %s ==\033[0m\n' "$name"
  "$DIR/$name.sh" 2>&1 | tee "$OUT"
  # «Итого: N ✓, M ✗» — последняя строка скрипта; нет её — скрипт оборвался.
  read -r pass fail < <(sed 's/\x1b\[[0-9;]*m//g' "$OUT" | sed -n 's/^Итого: \([0-9]*\) ✓, \([0-9]*\) ✗$/\1 \2/p' | tail -1)
  if [ -z "${pass:-}" ]; then
    LINES+=("$(printf '  \033[31m✗\033[0m %-20s оборвался без итога' "$name")"); TOTAL_FAIL=$((TOTAL_FAIL+1))
  else
    mark='\033[32m✓\033[0m'; [ "$fail" -eq 0 ] || mark='\033[31m✗\033[0m'
    LINES+=("$(printf "  $mark %-20s %s ✓, %s ✗" "$name" "$pass" "$fail")")
    TOTAL_PASS=$((TOTAL_PASS+pass)); TOTAL_FAIL=$((TOTAL_FAIL+fail))
  fi
  unset pass fail
done

printf '\n\033[1mЭтап 10\033[0m\n'
printf '%s\n' "${LINES[@]}"
echo
echo "Итого: $TOTAL_PASS ✓, $TOTAL_FAIL ✗"
[ "$TOTAL_FAIL" -eq 0 ]
