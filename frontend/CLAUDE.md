# Админка доступа (frontend)

SPA на `/admin` сервиса входа: пользователи, роли и права приложений, журнал. Работает только через API сервиса
(`/auth/admin/*`), токены — в httpOnly-cookie сервиса (страница их не видит). Проверки в интерфейсе — только для
удобства: доступ решает сервер.

## Стек (не менять без ADR)

Vite + React 19 + TypeScript (strict) · react-router 7 (`basename: /admin`) · TanStack Query 5 · axios · react-hook-form +
Zod · Tailwind CSS 4 + компоненты в стиле shadcn/ui · sonner (уведомления) · Vitest + Testing Library · ESLint 9 +
Prettier. React Compiler включён: без `useMemo`/`useCallback`/`memo`, данные не мутировать.

Node 22 (`.nvmrc`), npm, `package-lock.json` в git. `sonner` закреплён точной версией: хэш его стилей прописан в CSP
сервиса (`src/AuthService.Api/Configuration/AdminUiExtensions.cs`) — обновили sonner, обновите хэш (подскажет консоль).

## Раскладка (Feature-Sliced Design)

```
src/app        — провайдеры, роутер, проверка сессии, плашка «доступ изменился»
src/pages      — страницы (обзор, «нет доступа», 404)
src/widgets    — каркас с меню (app-shell)
src/features   — действия пользователя (появятся с разделами)
src/entities   — данные: admin-session (GET /auth/admin/me)
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
npm run lint         # ESLint, 0 предупреждений
npm run type-check
npm test
npm run build        # dist/ — в образе сервиса копируется в wwwroot/admin
npm run format
```

Вход в dev: сервис должен пускать возврат на `http://localhost:5173` (`ReturnUrls:AllowedOrigins` — уже есть в
Development и Docker).
