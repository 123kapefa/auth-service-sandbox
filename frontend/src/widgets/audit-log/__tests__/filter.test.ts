import { describe, expect, it } from "vitest";
import { filterFromParams, paramsFromFilter } from "../model/filter";

describe("фильтр журнала в адресной строке", () => {
  it("туда и обратно; пустые поля и первая страница в адрес не попадают; чужие параметры сохраняются", () => {
    const keep = new URLSearchParams("tab=actions");
    const filter = {
      action: "UserRoleChanged",
      app: "manager-1c",
      from: "",
      to: "2026-10-02",
      objectId: "",
      page: 1,
    };

    const params = paramsFromFilter(filter, keep);

    expect(params.toString()).toBe(
      "tab=actions&action=UserRoleChanged&app=manager-1c&to=2026-10-02",
    );
    expect(filterFromParams(params)).toEqual(filter);
  });
});
