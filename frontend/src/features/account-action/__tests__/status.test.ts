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

  it("неизвестное действие при успехе — просто «Готово»", () => {
    expect(actionNotice(null, "success")).toEqual({ kind: "success", text: "Готово" });
  });

  it("ошибка — предложить повторить; отмена и мусор — молча", () => {
    expect(actionNotice("update-password", "error")?.kind).toBe("error");
    expect(actionNotice("update-password", "cancelled")).toBeNull();
    expect(actionNotice("update-password", "<script>")).toBeNull();
  });
});
