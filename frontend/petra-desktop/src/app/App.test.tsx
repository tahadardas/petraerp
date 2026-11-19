import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import { usePreferences } from "@/design-system/preferences";
import { useWorkspace } from "@/workspace/workspaceStore";
import { App } from "./App";
import { hasPermission, useSession } from "./permissions";

describe("application shell", () => {
  beforeEach(() => {
    useWorkspace.setState({ windows: [], activeKey: null });
    usePreferences.setState({ language: "ar", theme: "light", density: "compact" });
    useSession.getState().signOut();
  });

  it("shows the owner's top menus and opens the home window in Arabic RTL", async () => {
    render(<App />);
    for (const label of ["المحاسبة", "الفواتير", "السندات", "المستودعات", "التقارير", "الصيانة", "الإعدادات"])
      expect(screen.getByRole("button", { name: label })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "الصفحة الرئيسية" })).toBeInTheDocument();
    expect(document.documentElement.getAttribute("dir")).toBe("rtl");
  });

  it("switches to English LTR and dark theme from the header", async () => {
    render(<App />);
    await userEvent.selectOptions(screen.getByLabelText(/اللغة/), "en");
    expect(document.documentElement.getAttribute("dir")).toBe("ltr");
    expect(screen.getByRole("button", { name: "Accounting" })).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText(/Theme/), "dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });

  it("closes the active window with Ctrl+W when it is clean", async () => {
    render(<App />);
    await screen.findByRole("heading", { name: "الصفحة الرئيسية" });
    await userEvent.click(screen.getByRole("tab"));
    await userEvent.keyboard("{Control>}w{/Control}");
    expect(useWorkspace.getState().windows).toHaveLength(0);
  });
});

describe("permissions", () => {
  it("allows an item with no requirement and enforces a requirement", () => {
    const granted = new Set(["Ledger.Reports.View"]);
    expect(hasPermission(granted, undefined)).toBe(true);
    expect(hasPermission(granted, "Ledger.Reports.View")).toBe(true);
    expect(hasPermission(granted, "Ledger.Periods.Manage")).toBe(false);
  });
});
