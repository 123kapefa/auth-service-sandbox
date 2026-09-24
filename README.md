# Auth Service

Отдельный IAM-микросервис на .NET 10: ASP.NET Core Identity, PostgreSQL и OpenIddict. Прикладной код организован vertical slices в `Core/Features`; HTTP/UI — тонкий адаптер над ними.

## Локальный запуск

```bash
cp .env.example .env
docker compose up --build
```

- Auth service: <http://localhost:8080>
- Mailpit: <http://localhost:8025>
- Discovery: <http://localhost:8080/.well-known/openid-configuration>

Bootstrap-администратор создаётся только при `BOOTSTRAP_ENABLED=true`, получает роль `platform_admin`, но должен подтвердить email и включить TOTP перед использованием `/api/admin`. Development-клиенты: `dev-spa` (code + PKCE) и `dev-service` (client credentials; secret в `.env`).

## Контракт авторизации

- `GET|POST /connect/authorize` — authorization code с PKCE S256.
- `POST /connect/token` — authorization code, refresh token или client credentials.
- `GET|POST /connect/userinfo`, `GET|POST /connect/logout`, `POST /connect/revocation`.
- Discovery и JWKS публикуются OpenIddict; ресурсные сервисы валидируют `iss`, `aud`, подпись, срок действия и нужный scope через discovery, без общей БД.

Access token действует 15 минут и подписывается асимметричным ключом. Refresh token хранится сервером и ротируется. Не включены password/implicit/device grants, dynamic client registration и introspection.

## Администрирование

Bearer access token с `platform_admin`, `auth.admin` и `amr=mfa` открывает:

- `GET|POST /api/admin/clients`, `POST /api/admin/clients/{clientId}/rotate-secret`;
- `GET|POST /api/admin/scopes`;
- назначение платформенной роли и отзыв refresh-сессий пользователя.

Client secret возвращается только при создании или ротации. Redirect URI принимаются только как точные HTTPS URI или loopback HTTP URI для локальной разработки.

## Production

Перед запуском production обязательно передайте `Oidc:Keys:SigningCertificatePath` и `Oidc:Keys:EncryptionCertificatePath` через secret mounts, установите HTTPS issuer, SMTP и защищённое хранилище PostgreSQL/Data Protection keys. При ротации добавьте прежний ключ через `Oidc:Keys:PreviousSigningCertificatePath` на срок, превышающий TTL access token. Не используйте `.env.example` как production-секрет.
