import { describe, expect, it } from "vitest";
import { applyPreferences } from "./preferences";

describe("applyPreferences", () => {
  it("sets RTL for Arabic and LTR for English together with theme and density", () => {
    const root = document.createElement("html");
    applyPreferences(root, { language: "ar", theme: "dark", density: "comfortable" });
    expect(root.getAttribute("dir")).toBe("rtl");
    expect(root.getAttribute("lang")).toBe("ar");
    expect(root.getAttribute("data-theme")).toBe("dark");
    expect(root.getAttribute("data-density")).toBe("comfortable");
    applyPreferences(root, { language: "en", theme: "light", density: "compact" });
    expect(root.getAttribute("dir")).toBe("ltr");
    expect(root.getAttribute("data-theme")).toBe("light");
  });
});
