import { useState, useMemo, useEffect, useRef } from "react";
import { useWorkspace } from "@/workspace/workspaceStore";
import { useGlobalShortcut } from "./shortcuts";

export interface CommandItem {
  id: string;
  title: string;
  category: string;
  screen?: string;
  params?: Record<string, string>;
  action?: () => void;
  shortcut?: string;
}

export interface PetraCommandPaletteProps {
  items: readonly CommandItem[];
}

export function PetraCommandPalette({ items }: PetraCommandPaletteProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  useGlobalShortcut({ key: "k", ctrl: true }, (e) => {
    e.preventDefault();
    setOpen((prev) => !prev);
  });

  useEffect(() => {
    if (open) {
      setQuery("");
      setSelectedIndex(0);
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [open]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return items.slice(0, 20);
    return items.filter(
      (item) =>
        item.title.toLowerCase().includes(q) ||
        item.category.toLowerCase().includes(q)
    ).slice(0, 20);
  }, [items, query]);

  const executeItem = (item: CommandItem) => {
    setOpen(false);
    if (item.action) {
      item.action();
    } else if (item.screen) {
      openWorkspaceWindow({
        key: item.id,
        screen: item.screen,
        title: item.title,
        ...(item.params ? { params: item.params } : {}),
      });
    }
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setSelectedIndex((i) => (filtered.length ? (i + 1) % filtered.length : 0));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((i) => (filtered.length ? (i - 1 + filtered.length) % filtered.length : 0));
    } else if (e.key === "Enter" && filtered[selectedIndex]) {
      e.preventDefault();
      executeItem(filtered[selectedIndex]);
    } else if (e.key === "Escape") {
      e.preventDefault();
      setOpen(false);
    }
  };

  if (!open) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="لوحة الأوامر"
      onClick={() => setOpen(false)}
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0, 0, 0, 0.45)",
        zIndex: 10000,
        display: "flex",
        alignItems: "flex-start",
        justifyContent: "center",
        paddingTop: "12vh",
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: 580,
          maxWidth: "92vw",
          background: "var(--bg-surface)",
          borderRadius: "var(--radius)",
          border: "1px solid var(--border)",
          boxShadow: "0 12px 36px rgba(0, 0, 0, 0.28)",
          overflow: "hidden",
          display: "flex",
          flexDirection: "column",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            padding: "var(--space-3)",
            borderBottom: "1px solid var(--border)",
            gap: "var(--space-2)",
          }}
        >
          <span style={{ fontSize: 18, color: "var(--text-muted)" }}>🔍</span>
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setSelectedIndex(0);
            }}
            onKeyDown={onKeyDown}
            placeholder="ابحث عن شاشة، تقرير، أو أمر سريع... (Esc للإلغاء)"
            style={{
              flex: 1,
              border: 0,
              background: "transparent",
              color: "var(--text)",
              fontSize: "var(--fs-body)",
              outline: "none",
              fontFamily: "inherit",
            }}
          />
          <kbd
            style={{
              fontSize: 10,
              padding: "2px 5px",
              borderRadius: 3,
              background: "var(--bg-sunken)",
              color: "var(--text-muted)",
              border: "1px solid var(--border)",
            }}
          >
            ESC
          </kbd>
        </div>

        <div style={{ maxHeight: 360, overflowY: "auto", padding: "var(--space-1) 0" }}>
          {filtered.length === 0 ? (
            <div style={{ padding: "var(--space-4)", textAlign: "center", color: "var(--text-muted)" }}>
              لم يتم العثور على نتائج مطابقة
            </div>
          ) : (
            filtered.map((item, index) => {
              const isSelected = index === selectedIndex;
              return (
                <div
                  key={item.id}
                  onClick={() => executeItem(item)}
                  onMouseEnter={() => setSelectedIndex(index)}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "var(--space-2) var(--space-4)",
                    background: isSelected ? "var(--bg-sunken)" : "transparent",
                    borderInlineStart: isSelected ? "3px solid var(--primary)" : "3px solid transparent",
                    cursor: "pointer",
                  }}
                >
                  <div style={{ display: "flex", flexDirection: "column" }}>
                    <span style={{ fontWeight: isSelected ? 600 : 400, color: "var(--text)" }}>
                      {item.title}
                    </span>
                    <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
                      {item.category}
                    </span>
                  </div>
                  {item.shortcut ? (
                    <kbd
                      style={{
                        fontSize: 10,
                        padding: "1px 5px",
                        borderRadius: 3,
                        background: "var(--bg-surface)",
                        border: "1px solid var(--border)",
                        color: "var(--text-muted)",
                      }}
                    >
                      {item.shortcut}
                    </kbd>
                  ) : null}
                </div>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}
