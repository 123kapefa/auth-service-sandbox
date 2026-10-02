# Тема входа (login-theme)

Страницы входа Keycloak на React — Keycloakify 11. Собирается в jar темы `sandbox` (loginTheme realm) при сборке
образа Keycloak (`infra/keycloak/Dockerfile`: Node + Maven + Java). Оформление — как у админки (`frontend/`): Tailwind,
те же цвета, светлая и тёмная тема по системе.

## Как устроено

```
src/main.tsx           — точка входа: в Keycloak — window.kcContext, в npm run dev — макет (?page=login.ftl)
src/kc.gen.tsx         — создаёт Keycloakify при сборке (не править)
src/login/KcContext.ts — свои страницы плагина и их атрибуты (sandbox-code-login.ftl: codeChannel, codeTarget…)
src/login/KcPage.tsx   — страница по pageId: свои — pages/, остальные — стандартные Keycloakify в нашем каркасе
src/login/Template.tsx — каркас: название, карточка, сообщение Keycloak, «другой способ», языки
src/login/classes.ts   — классы Keycloak → Tailwind (так стандартные страницы выглядят как наши)
src/login/i18n.ts      — свои тексты и тексты realm для страниц входа (только литералы: читаются при сборке)
src/login/pages/       — Login (телефон или почта, «Войти по коду», ключ доступа), CodeLogin (экран кода)
src/login/lib/phone.ts — «+» перед номером, логин цифрами
```

- **Своя страница плагина**: ключ в `KcContextExtensionPerPage` (имя шаблона из `createForm(...)`), атрибуты
  аутентификатора (`form.setAttribute`) приходят в `kcContext` сами — нужны только типы; `case` в `KcPage.tsx`.
- **Тексты**: ключи сервера (`form.setError("key")`) — тоже в `i18n.ts`: Keycloakify кладёт их в сообщения темы.
  Во всех языках — одинаковый набор ключей. Тексты из `localizationTexts` realm странице в браузере не видны —
  нужные продублированы.
- **Поля форм**: имена — как читает сервер (`username`, `password`, `code`, `codeLogin`, `resend`…).

## Команды

```bash
npm ci
npm run dev          # http://localhost:5174/?page=login.ftl — страницы на макетах, без Keycloak
npm run lint
npm run type-check
npm test
npm run build        # только фронтенд; jar темы — npm run build-keycloak-theme (нужны Maven и Java) или образ Keycloak
```

Проверка на стенде: `docker compose build keycloak && docker compose up -d --force-recreate --wait keycloak`, затем
`scripts/keycloak/check-*.sh` — они читают страницы через kcContext (`scripts/keycloak/kc_context.py`).
