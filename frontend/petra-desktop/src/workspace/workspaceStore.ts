import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";

/** One open window/tab. `key` identifies the document (e.g. "sales-invoice:<id>"): opening it again focuses the existing one. */
export interface WorkspaceWindow {
  key: string;
  /** Registered screen id (lazy-loaded by the app), e.g. "home" or "sales-invoice". */
  screen: string;
  title: string;
  /** Opaque screen parameters (ids only; never financial values). */
  params?: Readonly<Record<string, string>>;
  /** True while the window holds unsaved edits: closing it must be confirmed. */
  dirty: boolean;
}

export interface WorkspaceState {
  windows: readonly WorkspaceWindow[];
  activeKey: string | null;
  /** Opens a window, or activates it when the same key is already open. Returns the key. */
  open: (window: Omit<WorkspaceWindow, "dirty"> & { dirty?: boolean }) => string;
  /** Closes a window. A dirty window is only closed with force = true (the caller asked the user first). Returns whether it closed. */
  close: (key: string, force?: boolean) => boolean;
  activate: (key: string) => void;
  setDirty: (key: string, dirty: boolean) => void;
  rename: (key: string, title: string) => void;
}

const storage = createJSONStorage<Pick<WorkspaceState, "windows" | "activeKey">>(() => {
  try {
    return window.sessionStorage;
  } catch {
    return { getItem: () => null, setItem: () => undefined, removeItem: () => undefined };
  }
});

/**
 * Multi-document workspace: several documents stay open at once, the layout survives a reload of the app window.
 * The layout is personal UI state; documents themselves are always re-read from the API.
 */
export const useWorkspace = create<WorkspaceState>()(
  persist(
    (set, get) => ({
      windows: [],
      activeKey: null,
      open: (window) => {
        const existing = get().windows.find((w) => w.key === window.key);
        if (existing) {
          set({ activeKey: existing.key });
          return existing.key;
        }
        set((s) => ({ windows: [...s.windows, { dirty: false, ...window }], activeKey: window.key }));
        return window.key;
      },
      close: (key, force = false) => {
        const target = get().windows.find((w) => w.key === key);
        if (!target) return false;
        if (target.dirty && !force) return false;
        set((s) => {
          const index = s.windows.findIndex((w) => w.key === key);
          const windows = s.windows.filter((w) => w.key !== key);
          // Closing the active window activates its neighbour, so the user keeps working.
          const neighbour = windows[Math.min(index, windows.length - 1)];
          return { windows, activeKey: s.activeKey === key ? (neighbour?.key ?? null) : s.activeKey };
        });
        return true;
      },
      activate: (key) => {
        if (get().windows.some((w) => w.key === key)) set({ activeKey: key });
      },
      setDirty: (key, dirty) => set((s) => ({ windows: s.windows.map((w) => (w.key === key ? { ...w, dirty } : w)) })),
      rename: (key, title) => set((s) => ({ windows: s.windows.map((w) => (w.key === key ? { ...w, title } : w)) })),
    }),
    { name: "petra.workspace.v1", storage, partialize: (s) => ({ windows: s.windows, activeKey: s.activeKey }) },
  ),
);
