#!/usr/bin/env bash
# Проверка этапа 11.7 (docs/kubernetes.md): Helm-чарт deploy/helm/platform-auth в локальном кластере kind. Сервис и
# Keycloak — по две реплики, базы и Redis — внутри чарта (стенд), миграторы — Job-хуки. Проверки стенда
# (check-stand.sh, check-login.sh) идут против кластера, затем реплики Keycloak останавливаются по одной — вошедший
# пользователь остаётся в системе.
# Нужны Docker, kind, kubectl, helm и образы (scripts/package/build-images.sh). Порты 8080 и 8180 заняты кластером —
# стенд в Docker остановить (docker compose stop). Каждый запуск — с чистой установки. Запуск:
#   scripts/k8s/check-kind.sh            (KEEP=1 — оставить кластер; LOGS=<каталог> — при падении логи подов туда)
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
CLUSTER="${CLUSTER:-auth-kind}"
NS=auth-kind
RELEASE=auth
VERSION=$(sed -n 's#.*<VersionPrefix>\(.*\)</VersionPrefix>.*#\1#p' "$ROOT/Directory.Build.props" | head -1)
KCTX="kind-$CLUSTER"
k() { kubectl --context "$KCTX" -n "$NS" "$@"; }

# shellcheck source=../keycloak/lib.sh
source "$ROOT/scripts/keycloak/lib.sh"

for tool in docker kind kubectl helm; do
  command -v "$tool" > /dev/null || { echo "Нужен $tool"; exit 2; }
done

collect_logs() {
  [ -n "${LOGS:-}" ] || return 0
  mkdir -p "$LOGS"
  k get all,jobs,events -o wide > "$LOGS/resources.txt" 2>&1
  for pod in $(k get pods -o name); do
    k logs "$pod" --all-containers --timestamps > "$LOGS/${pod#pod/}.log" 2>&1
    k logs "$pod" --all-containers --timestamps --previous > "$LOGS/${pod#pod/}.previous.log" 2>/dev/null || true
  done
  echo "Логи подов: $LOGS"
  # Не готовые поды — сразу в вывод: в журнале CI видно без артефакта.
  k get pods -o wide
  for pod in $(k get pods -o jsonpath='{range .items[?(@.status.containerStatuses[0].ready!=true)]}{.metadata.name}{" "}{end}'); do
    echo "--- $pod"
    k describe pod "$pod" | sed -n '/^Events:/,$p' | tail -15
    k logs "$pod" --all-containers --tail 60 2>&1
  done
}

finish() {
  [ "$FAIL" -eq 0 ] || collect_logs
  if [ -z "${KEEP:-}" ]; then
    kind delete cluster --name "$CLUSTER" > /dev/null 2>&1
  else
    echo "Кластер оставлен: kubectl --context $KCTX -n $NS get pods; убрать — kind delete cluster --name $CLUSTER"
  fi
  summary
}

echo "1. Кластер и установка"
if ! kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
  kind create cluster --name "$CLUSTER" --config "$ROOT/scripts/k8s/kind.yaml" --wait 120s > /dev/null 2>&1 \
    || { bad "кластер kind не создан"; finish; exit 1; }
fi
kubectl --context "$KCTX" delete namespace "$NS" --ignore-not-found --wait > /dev/null
for image in platform-auth-service platform-auth-keycloak platform-auth-realm-migrator; do
  kind load docker-image --name "$CLUSTER" "$image:$VERSION" > /dev/null 2>&1 \
    || { bad "образ $image:$VERSION не загружен в kind (scripts/package/build-images.sh)"; finish; exit 1; }
done

# Секреты стенда — заглушки разработки, как в .env.example; сертификат DataProtection — новый. Файлом, не --set:
# значения не попадают в список процессов.
SECRETS="$WORK/secrets.yaml"
cat > "$SECRETS" <<YAML
secrets:
  create: true
  values:
    AUTH_DB_PASSWORD: development-only
    AUTH_KEYCLOAK_DB_PASSWORD: development-only
    AUTH_KEYCLOAK_ADMIN_PASSWORD: admin
    AUTH_BFF_CLIENT_SECRET: development-bff-secret
    LEGACY_IMPORT_CLIENT_SECRET: development-legacy-import-secret
    AUTH_DATA_PROTECTION_CERT: $(PFX_PASSWORD=development-pfx "$ROOT/scripts/secrets/new-pfx.sh")
    AUTH_DATA_PROTECTION_CERT_PASSWORD: development-pfx
YAML

# Том для копий — заранее, готовым PVC (backup.persistence.existingClaim): класс хранения kind привязывает том только к первому поду
# (WaitForFirstConsumer), а PVC самого чарта «helm --wait» ждёт привязанным и установка зависла бы. Так же — в облаках с таким классом.
kubectl --context "$KCTX" create namespace "$NS" --dry-run=client -o yaml | kubectl --context "$KCTX" apply -f - > /dev/null
kubectl --context "$KCTX" -n "$NS" apply -f - > /dev/null <<YAML
apiVersion: v1
kind: PersistentVolumeClaim
metadata: { name: kind-backups }
spec:
  accessModes: [ReadWriteOnce]
  resources: { requests: { storage: 1Gi } }
YAML

# --wait: Deployment готовы, затем хуки post-install (миграторы) отработали.
if helm upgrade --install "$RELEASE" "$ROOT/deploy/helm/platform-auth" --kube-context "$KCTX" -n "$NS" --create-namespace \
    -f "$ROOT/scripts/k8s/kind-values.yaml" -f "$SECRETS" --set "images.tag=$VERSION" \
    --set-file keycloak.realmImport="$ROOT/infra/keycloak/realm/sandbox-realm.json" \
    --wait --timeout 15m > "$WORK/helm.log" 2>&1; then
  ok "helm install: чарт установлен, поды готовы"
else
  bad "helm install: $(tail -3 "$WORK/helm.log" | tr '\n' ' ')"
  finish; exit 1
fi

ready() { k get deploy "$RELEASE-$1" -o jsonpath='{.status.readyReplicas}'; }
check "сервис: готовых реплик" "$(ready service)" 2
check "Keycloak: готовых реплик" "$(ready keycloak)" 2
check "мигратор базы сервиса отработал" "$(k get job "$RELEASE-migrator" -o jsonpath='{.status.succeeded}')" 1
check "мигратор настроек проектов отработал" "$(k get job "$RELEASE-realm-migrator" -o jsonpath='{.status.succeeded}')" 1
# Кластер Infinispan: каждый узел видит оба (обнаружение через базу, jdbc-ping).
cluster_size() {
  k logs "$1" | grep 'ISPN000094' | tail -1 | sed -n 's/.*|[0-9]*\] (\([0-9]*\)).*/\1/p'
}
for pod in $(k get pods -l app.kubernetes.io/name=keycloak -o name); do
  check "Keycloak ${pod#pod/}: узлов в кластере" "$(cluster_size "$pod")" 2
done

# Скрипты проверки стенда — против кластера: мастер-админка — изнутри пода Keycloak.
export KEYCLOAK_EXEC="kubectl --context $KCTX -n $NS exec -i deploy/$RELEASE-keycloak --"
export KEYCLOAK_ADMIN=admin KEYCLOAK_ADMIN_PASSWORD=admin SANDBOX_BFF_CLIENT_SECRET=development-bff-secret

echo "2. check-stand.sh"
if "$ROOT/scripts/keycloak/check-stand.sh" > "$WORK/stand.log" 2>&1; then ok "check-stand.sh: $(tail -1 "$WORK/stand.log")"
else bad "check-stand.sh"; sed 's/^/    /' "$WORK/stand.log"; fi

echo "3. check-login.sh"
# Две реплики сервиса: отзыв доходит до второй, когда истечёт её кэш проверки (2 с).
if REVOCATION_CACHE_SECONDS=2 "$ROOT/scripts/keycloak/check-login.sh" > "$WORK/login.log" 2>&1; then ok "check-login.sh: $(tail -1 "$WORK/login.log")"
else bad "check-login.sh"; sed 's/^/    /' "$WORK/login.log"; fi

echo "4. Остановка реплики Keycloak не разлогинивает"
JAR="$WORK/jar"; : > "$JAR"
check "вход" "$(login "$JAR" /me/ 0987654321)" /me/

# sso <jar> — сессия Keycloak жива: вход в приложение проходит без формы (Keycloak сразу возвращает в callback)
sso() {
  local h="$WORK/h"
  curl -s -o /dev/null -D "$h" -b "$1" -c "$1" "$API/auth/login?return_to=/me/"
  curl -s -o /dev/null -D "$h" -b "$1" -c "$1" "$(header "$h" location)"
  case "$(header "$h" location)" in */auth/callback*) echo без-формы ;; *) echo форма ;; esac
}
# still_signed_in <подпись> — несколько запросов подряд: NodePort раскидывает их по живым репликам
still_signed_in() {
  local i refresh="" me=""
  for i in 1 2 3 4; do
    refresh+="$(status "$JAR" POST /auth/refresh) "
    me+="$(status "$JAR" GET /me/) "
  done
  check "$1: обновление токена" "$refresh" "200 200 200 200 "
  check "$1: /me/" "$me" "200 200 200 200 "
  check "$1: сессия Keycloak (SSO)" "$(sso "$JAR")" без-формы
}

read -ra ORIGINAL <<< "$(k get pods -l app.kubernetes.io/name=keycloak -o name | tr '\n' ' ')"
# Первая — штатно (kubectl delete, как при обновлении узла): реплика выходит из кластера.
k delete "${ORIGINAL[0]}" --wait > /dev/null
still_signed_in "одна реплика осталась"
k rollout status "deploy/$RELEASE-keycloak" --timeout 300s > /dev/null
# Вторая — аварийно (без ожидания): сессии — у оставшихся реплик кластера и в базе Keycloak. Пока кластер не заметил
# пропажу узла (десятки секунд), запросы к кэшу, чья копия была на нём, отказывают; вход при этом не теряется.
k delete "${ORIGINAL[1]}" --grace-period=0 --force > /dev/null 2>&1
for _ in $(seq 60); do
  [ "$(status "$JAR" POST /auth/refresh)" = 200 ] && break
  sleep 2
done
still_signed_in "вторая упала"
k rollout status "deploy/$RELEASE-keycloak" --timeout 300s > /dev/null
check "Keycloak снова в двух репликах" "$(ready keycloak)" 2
still_signed_in "обе реплики — новые"
check "выход" "$(status "$JAR" POST /auth/logout)" 200

echo "4а. Резервные копии: CronJob чарта снимает копии обеих баз"
check "CronJob создан, расписание из values" "$(k get cronjob "$RELEASE-backup" -o jsonpath='{.spec.schedule} {.spec.concurrencyPolicy}')" "0 3 * * * Forbid"
# Не ждём трёх часов ночи: запуск того же CronJob вручную.
k delete job backup-check --ignore-not-found > /dev/null 2>&1
k create job backup-check --from="cronjob/$RELEASE-backup" > /dev/null 2>&1
k wait --for=condition=complete job/backup-check --timeout=240s > /dev/null 2>&1
check "копия снята: Job завершён" "$(k get job backup-check -o jsonpath='{.status.succeeded}')" 1
k logs job/backup-check > "$WORK/backup-job.log" 2>&1
check "в журнале: обе базы и «готово»" "$(grep -cE '^(auth-service|keycloak): /backups/|^готово: ' "$WORK/backup-job.log")" 3
check "в журнале нет пароля базы" "$(grep -c 'development-only' "$WORK/backup-job.log")" 0
# Содержимое тома смотрим подом с тем же PVC: файлы, суммы, права и что pg_restore читает копию.
CLAIM=$(k get cronjob "$RELEASE-backup" -o jsonpath='{.spec.jobTemplate.spec.template.spec.volumes[?(@.name=="backups")].persistentVolumeClaim.claimName}')
k delete pod backup-inspect --ignore-not-found > /dev/null 2>&1
k apply -f - > /dev/null 2>&1 <<YAML
apiVersion: v1
kind: Pod
metadata: { name: backup-inspect }
spec:
  restartPolicy: Never
  securityContext: { runAsNonRoot: true, runAsUser: 70, runAsGroup: 70, fsGroup: 70 }
  containers:
    - name: inspect
      image: postgres:17-alpine
      imagePullPolicy: IfNotPresent
      command: ["sh", "-c", "cd /backups && sha256sum -c *.sha256 | grep -c ': OK'; ls -l *.dump | cut -c1-10 | sort -u; for f in *.dump; do pg_restore --list \"\$f\" | grep -c 'TABLE DATA'; done; ls last-success"]
      volumeMounts: [{ name: backups, mountPath: /backups, readOnly: true }]
  volumes:
    - { name: backups, persistentVolumeClaim: { claimName: $CLAIM } }
YAML
k wait --for=jsonpath='{.status.phase}'=Succeeded pod/backup-inspect --timeout=120s > /dev/null 2>&1
k logs pod/backup-inspect > "$WORK/backup-inspect.log" 2>&1
check "суммы обеих копий совпадают" "$(sed -n 1p "$WORK/backup-inspect.log")" 2
check "права копий — только владелец" "$(sed -n 2p "$WORK/backup-inspect.log")" "-rw-------"
check "pg_restore читает обе копии (таблицы с данными есть)" \
  "$(sed -n '3,4p' "$WORK/backup-inspect.log" | awk '{ if ($1 > 0) n++ } END { print n }')" 2
check "last-success записан" "$(sed -n 5p "$WORK/backup-inspect.log")" last-success
k delete pod backup-inspect job/backup-check --ignore-not-found > /dev/null 2>&1

echo "5. Ingress: наружу — только realm проекта, realm master и мастер-админка закрыты"
# Настоящий контроллер (ingress-nginx, манифест для kind): рендер чарта и спецификация Kubernetes ничего не говорят о том,
# как контроллер сравнивает пути. Проверяется Ingress из самого чарта; хосты — тестовые, запросы идут на узел kind
# с заголовком Host. Матрица обходов — check-public-paths.sh, та же, что для прокси Compose.
INGRESS_NGINX="https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.12.1/deploy/static/provider/kind/deploy.yaml"
# Кластер с прошлого запуска (KEEP=1) мог быть создан по старому kind.yaml — без метки ingress-ready и портов 8088/8443:
# тогда контроллер не встанет; такой кластер удалить (kind delete cluster --name auth-kind).
INSTALLED=нет
for _ in 1 2 3; do
  kubectl --context "$KCTX" apply -f "$INGRESS_NGINX" > "$WORK/ingress-install.log" 2>&1 && { INSTALLED=да; break; }
  sleep 5
done
# wait сразу после apply падает, пока ReplicaSet не создал под: ждём появления пода, затем готовности.
if [ "$INSTALLED" = да ]; then
  for _ in $(seq 30); do
    [ -n "$(kubectl --context "$KCTX" -n ingress-nginx get pod --selector=app.kubernetes.io/component=controller -o name 2> /dev/null)" ] && break
    sleep 2
  done
  kubectl --context "$KCTX" -n ingress-nginx wait --for=condition=ready pod --selector=app.kubernetes.io/component=controller --timeout=240s >> "$WORK/ingress-install.log" 2>&1 || INSTALLED=нет
fi
if [ "$INSTALLED" = да ]; then
  ok "ingress-nginx установлен"
  # Admission-вебхук контроллера отвечает не сразу после готовности пода.
  APPLIED=нет
  for _ in $(seq 20); do
    if helm template "$RELEASE" "$ROOT/deploy/helm/platform-auth" -n "$NS" -f "$ROOT/scripts/k8s/kind-values.yaml" \
         --set ingress.enabled=true --set-string publicUrls.service=http://auth.test --set-string publicUrls.keycloak=http://id.test \
         --show-only templates/ingress.yaml 2> /dev/null | kubectl --context "$KCTX" -n "$NS" apply -f - > "$WORK/ingress-apply.log" 2>&1; then
      APPLIED=да; break
    fi
    sleep 3
  done
  check "Ingress чарта применён" "$APPLIED" да
  # Контроллеру нужно время, чтобы подхватить правила.
  for _ in $(seq 30); do
    [ "$(curl -sk -o /dev/null -w '%{http_code}' -H 'Host: id.test' https://127.0.0.1:8443/realms/sandbox/.well-known/openid-configuration)" = 200 ] && break
    sleep 2
  done
  if CURL_INSECURE=1 PUBLIC=https://127.0.0.1:8443 HOST_HEADER=id.test DIRECT=http://localhost:8180 REALM=sandbox \
       "$ROOT/scripts/keycloak/check-public-paths.sh" > "$WORK/paths.log" 2>&1; then
    ok "check-public-paths.sh: $(tail -1 "$WORK/paths.log")"
  else
    bad "check-public-paths.sh"; sed 's/^/    /' "$WORK/paths.log"
  fi
else
  bad "ingress-nginx не установлен: $(tail -3 "$WORK/ingress-install.log" | tr '\n' ' ')"
fi

finish
