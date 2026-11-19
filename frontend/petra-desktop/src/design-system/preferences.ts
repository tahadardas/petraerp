import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";

export type ThemeName = "light" | "dark";
export type Language = "ar" | "en";
export type Density = "compact" | "comfortable";

export interface Preferences {
  theme: ThemeName;
  language: Language;
  density: Density;
  setTheme: (theme: ThemeName) => void;
  setLanguage: (language: Language) => void;
  setDensity: (density: Density) => void;
}

/** Writes direction, language, theme and density to <html>; the CSS tokens and logical properties do the rest. */
export function applyPreferences(root: HTMLElement, p: Pick<Preferences, "theme" | "language" | "density">): void {
  root.setAttribute("lang", p.language);
  root.setAttribute("dir", p.language === "ar" ? "rtl" : "ltr");
  root.setAttribute("data-theme", p.theme);
  root.setAttribute("data-density", p.density);
}

const safeStorage = createJSONStorage<Pick<Preferences, "theme" | "language" | "density">>(() => {
  try {
    return window.localStorage;
  } catch {
    // Storage can be blocked: preferences then live for the session only.
    return { getItem: () => null, setItem: () => undefined, removeItem: () => undefined };
  }
});

/** Personal preferences (language, theme, density) are kept per user on this machine. Business values never live here. */
export const usePreferences = create<Preferences>()(
  persist(
    (set) => ({
      theme: "light",
      language: "ar",
      density: "compact",
      setTheme: (theme) => set({ theme }),
      setLanguage: (language) => set({ language }),
      setDensity: (density) => set({ density }),
    }),
    {
      name: "petra.preferences.v1",
      storage: safeStorage,
      partialize: (s) => ({ theme: s.theme, language: s.language, density: s.density }),
    },
  ),
);
