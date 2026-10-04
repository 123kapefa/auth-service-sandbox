import axios, {
  type AxiosError,
  type AxiosAdapter,
  type AxiosInstance,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from "axios";
import { loginUrl } from "@/shared/config/routes";
import { type Envelope } from "./envelope";
import { EnvelopeError } from "./errors";

/** Что изменилось в доступе после умного рефреша (ответ POST /auth/refresh). */
export interface AccessChanges {
  addedPermissions: string[];
  removedPermissions: string[];
  addedRoles: string[];
  removedRoles: string[];
  activeMode: string | null;
}

export interface AuthHandlers {
  /** Доступ изменился, токен обновлён: показать, что стало доступно и что пропало. */
  onAccessChanged: (changes: AccessChanges) => void;
  /** Сессии нет или она кончилась: на вход, с возвратом сюда. */
  redirectToLogin: () => void;
}

interface RetryConfig extends InternalAxiosRequestConfig {
  _isRetry?: boolean;
}

const REFRESH_PATH = "/auth/refresh";

const isAccessChanged = (error: AxiosError) =>
  String(error.response?.headers["www-authenticate"] ?? "").includes("access_changed");

/**
 * Клиент API сервиса входа. Токены — в httpOnly-cookie сервиса (страница их не видит), поэтому запросы идут на тот же
 * origin. 401:
 * - доступ изменился (`access_changed`) — умный рефреш: новые токены без перелогина, плашка, повтор запроса;
 * - токен истёк — одна попытка refresh и повтор;
 * - refresh не принят — на вход.
 * Ошибка в Envelope превращается в EnvelopeError с кодом.
 */
export function createApiClient(handlers: AuthHandlers, adapter?: AxiosAdapter): AxiosInstance {
  const client = axios.create({ headers: { Accept: "application/json" }, adapter });
  let refreshing: Promise<AccessChanges | null> | null = null;

  // Уходим на вход: запрос не завершается — экран не успевает показать ошибку до перехода.
  const leaveForLogin = () => {
    handlers.redirectToLogin();
    return new Promise<never>(() => undefined);
  };

  // Одно обновление на все параллельные запросы, получившие 401.
  const refresh = () => {
    refreshing ??= client
      .post<Envelope<{ changes: AccessChanges | null }>>(REFRESH_PATH, undefined, {
        _isRetry: true,
      } as RetryConfig)
      .then((response) => response.data.result?.changes ?? null)
      .finally(() => {
        refreshing = null;
      });
    return refreshing;
  };

  client.interceptors.response.use(
    (response: AxiosResponse<Envelope<unknown>>) => {
      if (response.data?.isError && response.data.error) {
        throw new EnvelopeError(response.data.error, response.status);
      }

      return response;
    },
    async (error: AxiosError<Envelope<unknown>>) => {
      const config = error.config as RetryConfig | undefined;
      if (error.response?.status === 401 && config && !config._isRetry) {
        config._isRetry = true;
        try {
          const changes = await refresh();
          if (isAccessChanged(error) && changes) {
            handlers.onAccessChanged(changes);
          }
        } catch {
          return leaveForLogin();
        }

        return client(config);
      }

      if (error.response?.status === 401 && config?.url === REFRESH_PATH) {
        throw error; // refresh не принят — решает вызвавший refresh()
      }

      if (error.response?.status === 401) {
        return leaveForLogin(); // и после обновления 401 — сессию отозвали
      }

      if (error.response?.data?.error) {
        throw new EnvelopeError(error.response.data.error, error.response.status);
      }

      throw error;
    },
  );

  return client;
}

/** Обработчики по умолчанию: вход — переходом, плашку подключает приложение (setAccessChangedListener). */
let accessChangedListener: AuthHandlers["onAccessChanged"] = () => undefined;

export function setAccessChangedListener(listener: AuthHandlers["onAccessChanged"]): void {
  accessChangedListener = listener;
}

export const apiClient = createApiClient({
  onAccessChanged: (changes) => accessChangedListener(changes),
  redirectToLogin: () =>
    window.location.assign(loginUrl(window.location.pathname + window.location.search)),
});

/** Результат из Envelope (ошибки уже брошены интерсептором). */
export function unwrap<T>(response: AxiosResponse<Envelope<T>>): T {
  return response.data.result as T;
}
