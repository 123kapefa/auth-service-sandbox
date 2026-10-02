import {
  AxiosHeaders,
  type AxiosAdapter,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from "axios";
import { describe, expect, it, vi } from "vitest";
import { type AccessChanges, createApiClient, EnvelopeError } from "@/shared/api";

type Reply = { status: number; data?: unknown; headers?: Record<string, string> };

// Подставной «сервер»: ответы по очереди для каждого пути; вызовы записываются.
function fakeServer(routes: Record<string, Reply[]>) {
  const calls: string[] = [];
  const adapter: AxiosAdapter = async (config: InternalAxiosRequestConfig) => {
    const url = config.url ?? "";
    calls.push(url);
    const reply = routes[url]?.shift() ?? { status: 404, data: {} };
    const response: AxiosResponse = {
      data: reply.data,
      status: reply.status,
      statusText: String(reply.status),
      headers: new AxiosHeaders(reply.headers ?? {}),
      config,
    };
    if (reply.status >= 400) {
      const { AxiosError } = await import("axios");
      throw new AxiosError("fail", String(reply.status), config, undefined, response);
    }
    return response;
  };
  return { adapter, calls };
}

const ok = (result: unknown): Reply => ({
  status: 200,
  data: { result, error: null, isError: false },
});
const accessChanged: Reply = {
  status: 401,
  data: {
    result: null,
    error: { messages: [{ code: "auth.access_changed", message: "" }], type: "AUTHENTICATION" },
    isError: true,
  },
  headers: {
    "www-authenticate": 'Bearer error="invalid_token", error_description="access_changed"',
  },
};
const expired: Reply = { status: 401, data: "" };
const changes: AccessChanges = {
  addedPermissions: ["users.view"],
  removedPermissions: [],
  addedRoles: ["manager"],
  removedRoles: [],
  activeMode: "manager",
};

function setup(routes: Record<string, Reply[]>) {
  const server = fakeServer(routes);
  const handlers = { onAccessChanged: vi.fn(), redirectToLogin: vi.fn() };
  return { client: createApiClient(handlers, server.adapter), handlers, calls: server.calls };
}

describe("apiClient", () => {
  it("отдаёт ответ с Envelope как есть", async () => {
    const { client } = setup({ "/x": [ok({ a: 1 })] });

    const response = await client.get("/x");

    expect(response.data.result).toEqual({ a: 1 });
  });

  it("ошибку Envelope превращает в EnvelopeError с кодом", async () => {
    const { client } = setup({
      "/x": [
        {
          status: 409,
          data: {
            result: null,
            error: { messages: [{ code: "roles.builtin", message: "Нельзя" }], type: "CONFLICT" },
            isError: true,
          },
        },
      ],
    });

    const error = await client.get("/x").catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(EnvelopeError);
    expect((error as EnvelopeError).code).toBe("roles.builtin");
    expect((error as EnvelopeError).status).toBe(409);
  });

  it("access_changed: умный рефреш, плашка, повтор запроса", async () => {
    const { client, handlers, calls } = setup({
      "/x": [accessChanged, ok("после")],
      "/auth/refresh": [ok({ changes })],
    });

    const response = await client.get("/x");

    expect(response.data.result).toBe("после");
    expect(calls).toEqual(["/x", "/auth/refresh", "/x"]);
    expect(handlers.onAccessChanged).toHaveBeenCalledWith(changes);
    expect(handlers.redirectToLogin).not.toHaveBeenCalled();
  });

  it("истёк токен: refresh и повтор без плашки", async () => {
    const { client, handlers } = setup({
      "/x": [expired, ok("после")],
      "/auth/refresh": [ok({ changes: null })],
    });

    await client.get("/x");

    expect(handlers.onAccessChanged).not.toHaveBeenCalled();
  });

  // Запрос при уходе на вход не завершается: ждём, пока клиент попросит переход.
  const settlesBeforeRedirect = async (
    request: Promise<unknown>,
    redirect: ReturnType<typeof vi.fn>,
  ) => {
    let settled = false;
    void request.finally(() => {
      settled = true;
    });
    await vi.waitFor(() => expect(redirect).toHaveBeenCalledOnce());
    return settled;
  };

  it("refresh не принят — на вход, запрос не завершается", async () => {
    const { client, handlers } = setup({ "/x": [expired], "/auth/refresh": [expired] });

    expect(await settlesBeforeRedirect(client.get("/x"), handlers.redirectToLogin)).toBe(false);
  });

  it("после refresh снова 401 — на вход, без бесконечного цикла", async () => {
    const { client, handlers, calls } = setup({
      "/x": [expired, expired],
      "/auth/refresh": [ok({ changes: null })],
    });

    expect(await settlesBeforeRedirect(client.get("/x"), handlers.redirectToLogin)).toBe(false);
    expect(calls).toEqual(["/x", "/auth/refresh", "/x"]);
  });

  it("параллельные 401 — одно обновление на всех", async () => {
    const { client, calls } = setup({
      "/a": [expired, ok("a")],
      "/b": [expired, ok("b")],
      "/auth/refresh": [ok({ changes: null })],
    });

    await Promise.all([client.get("/a"), client.get("/b")]);

    expect(calls.filter((url) => url === "/auth/refresh")).toHaveLength(1);
  });
});
