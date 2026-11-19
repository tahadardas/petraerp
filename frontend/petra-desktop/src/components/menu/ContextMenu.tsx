import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from "react";

export interface ContextMenuItem {
  id: string;
  label: string;
  run: () => void;
  /** Disabled for a real reason (permission, document state); the reason is shown as the tooltip. */
  disabled?: boolean;
  reason?: string;
  separatorBefore?: boolean;
}

interface Open<T> {
  x: number;
  y: number;
  target: T;
}

/**
 * Right-click menus. `buildItems(target)` returns the actions for THAT cell/row/field (not one generic list): hidden actions are
 * simply absent, unavailable ones are disabled with a reason. Keyboard: ArrowUp/Down, Enter, Escape; also the Menu key / Shift+F10
 * via `openAt`.
 */
export function useContextMenu<T>(buildItems: (target: T) => readonly ContextMenuItem[]): {
  onContextMenu: (event: MouseEvent, target: T) => void;
  openAt: (element: HTMLElement, target: T) => void;
  menu: ReactNode;
  close: () => void;
} {
  const [state, setState] = useState<Open<T> | null>(null);
  const close = useCallback(() => setState(null), []);

  const onContextMenu = useCallback((event: MouseEvent, target: T) => {
    event.preventDefault();
    setState({ x: event.clientX, y: event.clientY, target });
  }, []);

  const openAt = useCallback((element: HTMLElement, target: T) => {
    const box = element.getBoundingClientRect();
    setState({ x: box.left, y: box.bottom, target });
  }, []);

  const items = state ? buildItems(state.target) : [];
  const menu = state && items.length > 0 ? <MenuSurface x={state.x} y={state.y} items={items} onClose={close} /> : null;
  return { onContextMenu, openAt, menu, close };
}

function MenuSurface({ x, y, items, onClose }: { x: number; y: number; items: readonly ContextMenuItem[]; onClose: () => void }) {
  const [active, setActive] = useState(() => Math.max(items.findIndex((i) => !i.disabled), 0));
  const ref = useRef<HTMLUListElement>(null);

  useEffect(() => {
    ref.current?.focus();
    const away = (e: globalThis.MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) onClose();
    };
    document.addEventListener("mousedown", away);
    return () => document.removeEventListener("mousedown", away);
  }, [onClose]);

  const step = (delta: number) => {
    for (let n = 1; n <= items.length; n++) {
      const index = (active + delta * n + items.length * n) % items.length;
      if (!items[index]?.disabled) return setActive(index);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLUListElement>) => {
    if (event.key === "ArrowDown") { event.preventDefault(); step(1); }
    else if (event.key === "ArrowUp") { event.preventDefault(); step(-1); }
    else if (event.key === "Escape") { event.preventDefault(); onClose(); }
    else if (event.key === "Enter") {
      event.preventDefault();
      const item = items[active];
      if (item && !item.disabled) { onClose(); item.run(); }
    }
  };

  return (
    <ul
      ref={ref}
      role="menu"
      tabIndex={-1}
      onKeyDown={onKeyDown}
      style={{ position: "fixed", insetInlineStart: x, top: y, zIndex: 50, margin: 0, padding: "var(--space-1) 0", listStyle: "none", minWidth: 180, background: "var(--bg-input)", border: "1px solid var(--border)", borderRadius: "var(--radius)", boxShadow: "0 6px 20px rgba(0,0,0,.18)", outline: "none" }}
    >
      {items.map((item, index) => (
        <li
          key={item.id}
          role="menuitem"
          aria-disabled={item.disabled === true}
          title={item.disabled ? item.reason : undefined}
          onMouseEnter={() => !item.disabled && setActive(index)}
          onClick={() => { if (!item.disabled) { onClose(); item.run(); } }}
          style={{
            padding: "var(--space-1) var(--space-4)",
            cursor: item.disabled ? "not-allowed" : "pointer",
            color: item.disabled ? "var(--text-muted)" : "var(--text)",
            background: index === active ? "var(--bg-sunken)" : "transparent",
            borderTop: item.separatorBefore ? "1px solid var(--divider)" : undefined,
          }}
        >
          {item.label}
        </li>
      ))}
    </ul>
  );
}
