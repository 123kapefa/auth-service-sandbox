import { describe, expect, it } from "vitest";
import { actionNotice } from "../model/status";

describe("actionNotice", () => {
  it("успех — своё сообщение для действия", () => {
    expect(actionNotice("update-password", "success")).toEqual({
      kind: "success",
      text: "Пароль изменён",
    });
    expect(actionNotice("update-email", "success")?.text).toContain("после перехода по ссылке");
  });

  it("2FA, ключ, коды, удаление — свои сообщения", () => {
    expect(actionNotice("configure-totp", "success")?.text).toBe(
      "Приложение-аутентификатор подключено",
    );
    expect(actionNotice("register-passkey", "success")?.text).toBe("Ключ доступа добавлен");
    expect(actionNotice("configure-recovery-codes", "success")?.text).toContain(
      "Коды восстановления созданы",
    );
    expect(actionNotice("delete-credential", "success")?.text).toBe("Удалено");
  });

  it("неизвестное действие при успехе — просто «Готово»", () => {
    expect(actionNotice(null, "success")).toEqual({ kind: "success", text: "Готово" });
  });

  it("ошибка — предложить повторить; отмена и мусор — молча", () => {
    expect(actionNotice("update-password", "error")?.kind).toBe("error");
    expect(actionNotice("update-password", "cancelled")).toBeNull();
    expect(actionNotice("update-password", "<script>")).toBeNull();
  });
});
