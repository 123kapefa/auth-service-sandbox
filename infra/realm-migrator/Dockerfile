# Мигратор настроек проектов (realm) Keycloak: scripts/project/migrate-realms.py и миграции realm-migrations/.
# Запускается при каждом развёртывании после Keycloak и до сервиса, применяет недостающие миграции ко всем проектам и
# завершается. Только стандартная библиотека Python. Контекст сборки — корень репозитория.
FROM python:3.13-alpine
WORKDIR /app
COPY scripts/project/ scripts/project/
# Токен мастер-админки — по паролю, прямо у Keycloak (KC — его адрес в сети Docker, KEYCLOAK_ADMIN_PASSWORD).
ENV KC_ADMIN_DIRECT=1 PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
USER nobody
ENTRYPOINT ["python3", "scripts/project/migrate-realms.py"]
