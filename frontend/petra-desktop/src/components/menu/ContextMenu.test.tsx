import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { useContextMenu, type ContextMenuItem } from "./ContextMenu";

function Host({ run, locked }: { run: (id: string) => void; locked: boolean }) {
  const { onContextMenu, menu } = useContextMenu<string>((row) => {
    const items: ContextMenuItem[] = [{ id: "open", label: `فتح ${row}`, run: () => run("open") }];
    if (row === "posted") items.push({ id: "delete", label: "حذف", run: () => run("delete"), disabled: true, reason: "سند مرحَّل", separatorBefore: true });
    else if (!locked) items.push({ id: "delete", label: "حذف", run: () => run("delete"), separatorBefore: true });
    return items;
  });
  return (
    <div>
      <div data-testid="draft" onContextMenu={(e) => onContextMenu(e, "draft")}>draft</div>
      <div data-testid="posted" onContextMenu={(e) => onContextMenu(e, "posted")}>posted</div>
      {menu}
    </div>
  );
}

describe("context menu", () => {
  it("opens on right-click with the actions of THAT row and runs one by click", async () => {
    const run = vi.fn();
    render(<Host run={run} locked={false} />);
    fireEvent.contextMenu(screen.getByTestId("draft"));
    expect(screen.getByRole("menuitem", { name: "فتح draft" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("menuitem", { name: "حذف" }));
    expect(run).toHaveBeenCalledWith("delete");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  });

  it("omits hidden actions and disables unavailable ones with the reason", () => {
    render(<Host run={vi.fn()} locked />);
    fireEvent.contextMenu(screen.getByTestId("draft"));
    expect(screen.queryByRole("menuitem", { name: "حذف" })).not.toBeInTheDocument(); // hidden for this context
    fireEvent.contextMenu(screen.getByTestId("posted"));
    const del = screen.getByRole("menuitem", { name: "حذف" });
    expect(del).toHaveAttribute("aria-disabled", "true");
    expect(del).toHaveAttribute("title", "سند مرحَّل");
  });

  it("is keyboard-operable: arrows skip disabled items, Enter runs, Escape closes", async () => {
    const run = vi.fn();
    render(<Host run={run} locked={false} />);
    fireEvent.contextMenu(screen.getByTestId("posted"));
    await userEvent.keyboard("{ArrowDown}{Enter}"); // the only enabled item stays active
    expect(run).toHaveBeenCalledWith("open");
    expect(run).not.toHaveBeenCalledWith("delete");
    fireEvent.contextMenu(screen.getByTestId("draft"));
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  });
});
