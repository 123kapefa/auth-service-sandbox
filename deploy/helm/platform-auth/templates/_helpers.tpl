{{/* Имена — от имени релиза: релиз auth → auth-service, auth-keycloak (как в deploy/compose.yaml). */}}
{{- define "auth.name" -}}{{ .Release.Name | trunc 40 | trimSuffix "-" }}{{- end -}}

{{- define "auth.labels" -}}
app.kubernetes.io/part-of: platform-auth
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{- end -}}

{{/* auth.selector <компонент> */}}
{{- define "auth.selector" -}}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/name: {{ .component }}
{{- end -}}

{{/* auth.image <имя образа> */}}
{{- define "auth.image" -}}
{{ .root.Values.images.registry }}{{ .image }}:{{ .root.Values.images.tag | default .root.Chart.AppVersion }}
{{- end -}}

{{- define "auth.secretName" -}}{{ .Values.secrets.name | default (printf "%s-secrets" (include "auth.name" .)) }}{{- end -}}
{{- define "auth.dataProtectionSecretName" -}}{{ .Values.dataProtection.secretName | default (printf "%s-data-protection" (include "auth.name" .)) }}{{- end -}}

{{/* Адреса баз и Redis: на стенде — внутри чарта, в бою — обязательны в values. */}}
{{- define "auth.dbHost" -}}
{{- if .Values.stand.enabled }}{{ include "auth.name" . }}-db{{ else }}{{ required "нужен database.host — Postgres сервиса входа" .Values.database.host }}{{ end -}}
{{- end -}}
{{- define "auth.keycloakDbHost" -}}
{{- if .Values.stand.enabled }}{{ include "auth.name" . }}-keycloak-db{{ else }}{{ required "нужен keycloakDatabase.host — Postgres Keycloak" .Values.keycloakDatabase.host }}{{ end -}}
{{- end -}}
{{- define "auth.redis" -}}
{{- if .Values.stand.enabled }}{{ include "auth.name" . }}-redis:6379{{ else }}{{ required "нужен redis — Redis проекта (host:port)" .Values.redis }}{{ end -}}
{{- end -}}

{{/* Строка подключения к базе сервиса; пароль — переменная AUTH_DB_PASSWORD, объявленная раньше в env. */}}
{{- define "auth.dbConnection" -}}
Host={{ include "auth.dbHost" . }};Port={{ .Values.database.port }};Database={{ .Values.database.name }};Username={{ .Values.database.user }};Password=$(AUTH_DB_PASSWORD){{ with .Values.database.options }};{{ . }}{{ end }}
{{- end -}}

{{/* secretEnv <имя переменной> <ключ Secret> [optional] */}}
{{- define "auth.secretEnv" -}}
- name: {{ .name }}
  valueFrom:
    secretKeyRef:
      name: {{ include "auth.secretName" .root }}
      key: {{ .key }}
      {{- if .optional }}
      optional: true
      {{- end }}
{{- end -}}

{{- define "auth.keycloakInternalUrl" -}}http://{{ include "auth.name" . }}-keycloak:8180{{- end -}}
