import { Suspense, type KeyboardEvent } from "react";
import { screens } from "@/app/screens";
import { useT } from "@/i18n/messages";
import { useWorkspace } from "./workspaceStore";

/**
 * The tabbed multi-document area. Windows live in the store (their documents are re-read from the API when reactivated),
 * Ctrl+W closes the active window (only when clean), Ctrl+Tab / Ctrl+Shift+Tab cycle.
 */
export function WorkspaceShell() {
  const t = useT();
  const { windows, activeKey, activate, close } = useWorkspace();
  const active = windows.find((w) => w.key === activeKey);
  const Screen = active ? screens[active.screen] : undefined;

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.ctrlKey && event.key.toLowerCase() === "w" && active) {
      event.preventDefault();
      close(active.key);
    } else if (event.ctrlKey && event.key === "Tab" && windows.length > 1) {
      event.preventDefault();
      const index = windows.findIndex((w) => w.key === activeKey);
      const next = windows[(index + (event.shiftKey ? -1 : 1) + windows.length) % windows.length];
      if (next) activate(next.key);
    }
  };

  return (
    <div onKeyDown={onKeyDown} style={{ display: "flex", flexDirection: "column", height: "100%", minHeight: 0 }}>
      <div role="tablist" style={{ display: "flex", gap: 2, background: "var(--bg-sunken)", padding: "var(--space-1) var(--space-2) 0" }}>
        {windows.map((w) => (
          <div
            key={w.key}
            role="tab"
            aria-selected={w.key === activeKey}
            tabIndex={w.key === activeKey ? 0 : -1}
            onClick={() => activate(w.key)}
            style={{
              display: "flex",
              alignItems: "center",
              gap: "var(--space-2)",
              padding: "var(--space-1) var(--space-3)",
              cursor: "pointer",
              background: w.key === activeKey ? "var(--bg-surface)" : "transparent",
              borderInline: "1px solid var(--divider)",
              borderTop: w.key === activeKey ? "2px solid var(--accent)" : "2px solid transparent",
            }}
          >
            <span>{w.title}</span>
            {w.dirty ? <span title={t("workspace.unsaved")} aria-label={t("workspace.unsaved")}>{"●"}</span> : null}
            <button
              type="button"
              aria-label={t("workspace.close")}
              onClick={(e) => {
                e.stopPropagation();
                close(w.key);
              }}
              style={{ background: "none", border: 0, color: "inherit", cursor: "pointer" }}
            >
              {"✕"}
            </button>
          </div>
        ))}
      </div>
      <div role="tabpanel" style={{ flex: 1, minHeight: 0, overflow: "auto", background: "var(--bg-surface)" }}>
        {Screen && active ? (
          <Suspense fallback={null}>
            <Screen params={active.params} />
          </Suspense>
        ) : (
          <p style={{ padding: "var(--space-6)", color: "var(--text-muted)" }}>{t("workspace.empty")}</p>
        )}
      </div>
    </div>
  );
}
