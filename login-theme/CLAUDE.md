# Тема входа (login-theme)

Страницы входа Keycloak на React — Keycloakify 11. Собирается в jar темы `platform` (loginTheme realm) при сборке
образа Keycloak (`infra/keycloak/Dockerfile`: Node + Maven + Java). Оформление — как у кабинета и админки (`frontend/`,
раздел «Стиль» в его CLAUDE.md): «серый квадратный» стиль, те же токены и шрифты (`@fontsource/*`, файлами темы),
светлая и тёмная тема по системе.

## Как устроено

```
src/main.tsx           — точка входа: в Keycloak — window.kcContext, в npm run dev — макет (?page=login.ftl)
src/kc.gen.tsx         — создаёт Keycloakify при сборке (не править)
src/login/KcContext.ts — свои страницы плагина и их атрибуты (platform-code-login.ftl: codeChannel, codeTarget…)
src/login/KcPage.tsx   — страница по pageId: свои — pages/, остальные — стандартные Keycloakify в нашем каркасе
src/login/Template.tsx — каркас: логотип и название, карточка, сообщение Keycloak, «другой способ», языки
src/login/branding.ts  — оформление проекта (плагин platform-branding): логотип, цвет, тема — читается до показа
src/login/classes.ts   — классы Keycloak → Tailwind (так стандартные страницы выглядят как наши)
src/login/i18n.ts      — свои тексты и тексты realm для страниц входа (только литералы: читаются при сборке)
src/login/pages/       — Login (телефон или почта, «Войти по коду», ключ доступа, «Или войдите через»: Госуслуги,
                         Яндекс ID; при привязке к аккаунту — только пароль), CodeLogin (экран кода), UpdatePhone
                         (смена номера), PasskeyRegister (добавление ключа доступа: из кабинета и «Входите без кода»)
src/login/lib/phone.ts — «+» перед номером, логин цифрами
src/email/            — тема писем (FreeMarker, без React): каркас html/template.ftl, сброс пароля, ссылка из админки,
                         подтверждение почты, код входа плагина (platform-login-code.ftl); тексты — messages/*.properties
```

- **Своя страница плагина**: ключ в `KcContextExtensionPerPage` (имя шаблона из `createForm(...)`), атрибуты
  аутентификатора (`form.setAttribute`) приходят в `kcContext` сами — нужны только типы; `case` в `KcPage.tsx`.
- **Тексты**: ключи сервера (`form.setError("key")`) — тоже в `i18n.ts`: Keycloakify кладёт их в сообщения темы.
  Во всех языках — одинаковый набор ключей. Тексты из `localizationTexts` realm странице в браузере не видны —
  нужные продублированы.
- **Письма** (`emailTheme: platform`): серый квадратный стиль; `branding` (название, цвет, текст на цвете, `logoUrl`) —
  от плагина (`BrandingEmailTemplateProvider`), без него — серый и `realmName`. Стили — в атрибутах (почтовые программы не читают `<style>`), к каждому HTML —
  текстовый вариант в `text/`. Остальные письма Keycloak — его шаблоны в нашем каркасе. Письмо с кодом рисует плагин
  (`EmailCodeSender`) этим же шаблоном: без темы писем код на почту не уйдёт. Посмотреть — Mailpit, `localhost:8025`.
- **Оформление проекта и приложения** (админка, «Оформление» проекта и вкладка приложения): название, логотип, акцент
  и тема — `GET …/realms/{realm}/platform-branding?client_id=…&tab_id=…` в `main.tsx` до первого показа (не дождались
  за 1,5 с — серый стиль, название — `realm.displayName`). По клиенту и сессии входа плагин находит приложение.
  Тема — `data-theme` на `<html>`, акцент (`color`, для тёмной темы — `colorDark`) — таблица стилей через CSSOM (`adoptedStyleSheets`)
  с токенами `--color-accent`, `--color-accent-foreground`, `--color-ring` для обеих тем. Акцент — только основная
  кнопка, квадрат у названия и фокус; линии и рамки — `primary` (серые), ошибки — `danger` (красный), не цвет проекта.
  В `npm run dev` (макеты) плагина нет — страницы серые.
- **Галочки** (`input[type=checkbox]`) — общий стиль в `styles.css` по переключателю из набора компонентов (Kit):
  квадрат с рамкой, включённая залита. Своих классов на галочки не вешать.
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
