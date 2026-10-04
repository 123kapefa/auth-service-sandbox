# Интерфейсы сервиса входа (frontend)

Два SPA одного проекта (общие `shared`, `entities`, сборка):

- **админка доступа** на `/admin`: пользователи, роли и права приложений, журнал, сервисы и ключи, оформление страниц
  входа (`/auth/admin/*`);
- **личный кабинет** на `/account`: профиль, пароль и почта, безопасность (2FA, коды, ключи доступа), устройства и
  история входов — любому вошедшему (`/me/*`, `/security/*`). Пароль, почта, коды и ключи вводятся на страницах сервера входа
  (`/auth/login?action=…`), кабинет их не видит.

Работают только через API сервиса, токены — в httpOnly-cookie сервиса (страница их не видит). Проверки в интерфейсе —
только для удобства: доступ решает сервер. Какое приложение — режим Vite (`--mode account`; иначе админка).

## Стек (не менять без ADR)

Vite + React 19 + TypeScript (strict) · react-router 7 (`basename: /admin`) · TanStack Query 5 · axios · react-hook-form +
Zod · Tailwind CSS 4 + компоненты в стиле shadcn/ui · sonner (уведомления) · Vitest + Testing Library · ESLint 9 +
Prettier. React Compiler включён: без `useMemo`/`useCallback`/`memo`, данные не мутировать.

Node 22 (`.nvmrc`), npm, `package-lock.json` в git. `sonner` закреплён точной версией: хэш его стилей прописан в CSP
сервиса (`src/AuthService.Api/Configuration/FrontendExtensions.cs`) — обновили sonner, обновите хэш (подскажет консоль).

## Стиль

«Серый квадратный» (макеты — холст Claude Design «Личный кабинет»): только оттенки серого без чёрного, без скруглений,
жёсткие тени со сдвигом (`shadow-hard`), опасное и «закрыто» — пунктир и штриховка (`bg-hatch`), не красный; у карточки —
квадрат в углу (`corner-mark`). Цвета — токены в `src/app/styles.css`, светлая и тёмная тема: по системе или выбору
пользователя (`data-theme` на `<html>`, `shared/lib/theme.ts`, кнопка `ThemeToggle`). Шрифты — Golos Text и JetBrains
Mono из `@fontsource/*` (свои файлы: CSP пускает только `'self'`). Навигация — боковое меню (`shared/ui/sidebar.tsx`),
разделы приложения — под ним в меню. Метки (`Badge`): `solid` — включено, `warning` — закрыто/заблокировано.
Токены: `primary` — линии, рамки, уголки (всегда серые); `accent` — основная кнопка, активный пункт меню, фокус
(`ring`): в кабинете их подменяет акцент проекта или приложения (`applyBrandingAccent`, свой для тёмной темы);
`danger` и `danger-muted` — ошибки (`FieldError`, `ErrorState`, неудачный вход): красный текст на бледном фоне.
Новое «активное» — `accent`, новая ошибка — `danger`, остальное — `primary`.

## Раскладка (Feature-Sliced Design)

```
src/app        — админка: провайдеры, роутер, проверка сессии, плашка «доступ изменился»;
                 src/app/account — кабинет: провайдеры, роутер, проверка сессии
src/pages      — обзор, пользователи и карточка, журнал, сервисы и ключи, оформление, раздел приложения (пользователи, роли, журнал),
                 «нет доступа», 404; кабинет — account-profile, account-security, account-devices
src/widgets    — каркас с боковым меню (app-shell), раздел приложения (app-section; вкладки — в меню), журнал действий (audit-log);
                 каркас кабинета (account-shell)
src/features   — действия: блокировка, доступ к приложению, «выйти везде», пароль и 2FA, роли пользователя,
                 роль (создать/изменить), удалить роль, своё право (создать/удалить), ключ сервиса (выдать; права,
                 перевыпуск, отзыв), оформление проекта и приложения (branding-edit); кабинет — действие на странице сервера входа (account-action), выйти на
                 устройстве, на других, везде (account-sessions)
src/entities   — данные: admin-session («кто я»), user, app-user, app-catalog (роли и права), journal, service-key
                 (сервисы проекта и окно с ключом — секрет показывается один раз), account-profile (профиль кабинета),
                 account-security (2FA, коды, ключи доступа), account-devices (сессии, история входов), branding (оформление
                 проекта и приложения, страница входа в миниатюре; кабинет — /me/branding)
src/shared     — api (клиент, Envelope, ошибки), config, lib, ui
```

Импорт — только сверху вниз; фичи друг друга не импортируют (общее — в entities или shared). Это проверяет ESLint
(`eslint-plugin-boundaries`). Публичный API слайса — `index.ts`. Алиас `@/` → `src/`.

## API

`shared/api/client.ts`: тот же origin, cookie уходят сами. Ошибка в Envelope → `EnvelopeError` (`code`, `status`).
401 с `access_changed` — умный рефреш (`POST /auth/refresh`, плашка, повтор запроса); истёк токен — refresh и повтор;
refresh не принят — переход на `/auth/login?return_to=…`. Запросы сущности — в `entities/*/api.ts` с фабриками
`queryOptions`.

## Добавить раздел или действие

Типы (`entities/*/types.ts`) → API и `queryOptions` → страница или фича → пункт меню (`widgets/app-shell/model/nav.ts`)
→ тесты рядом (`__tests__/`). Мутации: `await queryClient.invalidateQueries(...)` в `onSuccess`, ошибки — через
`getErrorMessage`.

## Команды

```bash
npm ci
npm run dev          # http://localhost:5173/admin/, API — прокси на сервис (AUTH_API, по умолчанию :8080)
npm run dev:account  # http://localhost:5174/account/
npm run lint         # ESLint, 0 предупреждений
npm run type-check
npm test
npm run build        # dist/admin и dist/account — в образе сервиса копируются в wwwroot/admin и wwwroot/account
npm run format
```

Вход в dev: сервис должен пускать возврат на `http://localhost:5173` и `:5174` (`ReturnUrls:AllowedOrigins` — уже есть
в Development и Docker).
