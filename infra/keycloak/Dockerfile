# Keycloak песочницы: стандартный образ + плагин «вход по коду» + тема входа sandbox.
# Java и Maven на машине не нужны — плагин собирается и тестируется здесь.

FROM maven:3.9-eclipse-temurin-21 AS build
WORKDIR /build
# Сначала только pom.xml: зависимости кэшируются и не скачиваются заново при правке кода.
COPY providers/code-login/pom.xml ./
RUN mvn -B -q dependency:go-offline
COPY providers/code-login/src ./src
# package запускает и юнит-тесты: упавший тест — упавшая сборка образа.
RUN mvn -B -ntp package

# Версия — та же, что keycloak.version в pom.xml: плагин собран под неё.
FROM quay.io/keycloak/keycloak:26.4.7
COPY --from=build /build/target/code-login.jar /opt/keycloak/providers/code-login.jar
# Тема внутри образа — так она попадёт и в прод. На стенде compose поверх монтирует папку для правок без пересборки.
COPY themes/sandbox /opt/keycloak/themes/sandbox
