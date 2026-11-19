import { beforeEach, describe, expect, it } from "vitest";
import { useWorkspace } from "./workspaceStore";

const reset = () => useWorkspace.setState({ windows: [], activeKey: null });

describe("workspace store", () => {
  beforeEach(reset);

  it("opens several documents and activates the last one", () => {
    const s = useWorkspace.getState();
    s.open({ key: "invoice:1", screen: "sales-invoice", title: "فاتورة 1" });
    s.open({ key: "voucher:7", screen: "voucher", title: "سند 7" });
    expect(useWorkspace.getState().windows.map((w) => w.key)).toEqual(["invoice:1", "voucher:7"]);
    expect(useWorkspace.getState().activeKey).toBe("voucher:7");
  });

  it("opening the same document again focuses the existing window instead of duplicating it", () => {
    const s = useWorkspace.getState();
    s.open({ key: "invoice:1", screen: "sales-invoice", title: "فاتورة 1" });
    s.open({ key: "voucher:7", screen: "voucher", title: "سند 7" });
    s.open({ key: "invoice:1", screen: "sales-invoice", title: "فاتورة 1" });
    expect(useWorkspace.getState().windows).toHaveLength(2);
    expect(useWorkspace.getState().activeKey).toBe("invoice:1");
  });

  it("refuses to close a window with unsaved edits unless the caller forces it after asking the user", () => {
    const s = useWorkspace.getState();
    s.open({ key: "invoice:1", screen: "sales-invoice", title: "فاتورة" });
    s.setDirty("invoice:1", true);
    expect(useWorkspace.getState().close("invoice:1")).toBe(false);
    expect(useWorkspace.getState().windows).toHaveLength(1);
    expect(useWorkspace.getState().close("invoice:1", true)).toBe(true);
    expect(useWorkspace.getState().windows).toHaveLength(0);
    expect(useWorkspace.getState().activeKey).toBeNull();
  });

  it("closing the active window activates its neighbour", () => {
    const s = useWorkspace.getState();
    s.open({ key: "a", screen: "home", title: "A" });
    s.open({ key: "b", screen: "home", title: "B" });
    s.open({ key: "c", screen: "home", title: "C" });
    s.activate("b");
    s.close("b");
    expect(useWorkspace.getState().activeKey).toBe("c");
    s.close("c");
    expect(useWorkspace.getState().activeKey).toBe("a");
  });

  it("ignores activation of an unknown window and closing an unknown window", () => {
    const s = useWorkspace.getState();
    s.open({ key: "a", screen: "home", title: "A" });
    s.activate("missing");
    expect(useWorkspace.getState().activeKey).toBe("a");
    expect(useWorkspace.getState().close("missing")).toBe(false);
  });
});
