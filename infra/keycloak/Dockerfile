# Keycloak сервиса входа: стандартный образ + плагины (вход по коду, BCrypt, роли приложения) + тема platform (Keycloakify).
# Java, Maven и Node на машине не нужны — всё собирается здесь. Контекст сборки — корень репозитория.

FROM maven:3.9-eclipse-temurin-21 AS build
WORKDIR /build
# Сначала только pom.xml: зависимости кэшируются и не скачиваются заново при правке кода.
COPY infra/keycloak/providers/code-login/pom.xml ./
RUN mvn -B -q dependency:go-offline
COPY infra/keycloak/providers/code-login/src ./src
# package запускает и юнит-тесты: упавший тест — упавшая сборка образа.
RUN mvn -B -ntp package

# Тема входа (login-theme/, React): Keycloakify собирает jar темы, для упаковки ему нужны Maven и Java.
FROM node:22-alpine AS theme
RUN apk add --no-cache openjdk17 maven
WORKDIR /theme
COPY login-theme/package.json login-theme/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-fund --no-audit
COPY login-theme/ ./
RUN npm run build-keycloak-theme

# Версия — та же, что keycloak.version в pom.xml: плагин собран под неё.
FROM quay.io/keycloak/keycloak:26.4.7
COPY --from=build /build/target/code-login.jar /opt/keycloak/providers/code-login.jar
# Тема platform (loginTheme и emailTheme realm) — jar в providers; Keycloak 26 — «все остальные версии» для Keycloakify.
COPY --from=theme /theme/dist_keycloak/platform-login-theme.jar /opt/keycloak/providers/platform-login-theme.jar
