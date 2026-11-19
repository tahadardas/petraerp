import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { applyPreferences, usePreferences } from "@/design-system/preferences";
import { useT } from "@/i18n/messages";
import { WorkspaceShell } from "@/workspace/WorkspaceShell";
import { useWorkspace } from "@/workspace/workspaceStore";
import { hasPermission, useSession } from "./permissions";
import { topMenus } from "./menu";

function createQueryClient(): QueryClient {
  // Reference data is cached per workspace by query keys; aborted requests are cancelled by React Query's signal.
  return new QueryClient({ defaultOptions: { queries: { staleTime: 60_000, retry: 1, refetchOnWindowFocus: false } } });
}

export function App() {
  const [queryClient] = useState(createQueryClient);
  const { theme, language, density, setTheme, setLanguage } = usePreferences();
  const t = useT();
  const permissions = useSession((s) => s.permissions);
  const workspaceId = useSession((s) => s.workspaceId);
  const open = useWorkspace((s) => s.open);

  useEffect(() => applyPreferences(document.documentElement, { theme, language, density }), [theme, language, density]);
  // Switching company/year must never show another workspace's cached data.
  useEffect(() => queryClient.clear(), [workspaceId, queryClient]);
  useEffect(() => {
    open({ key: "home", screen: "home", title: t("home.title") });
    // Opened once at start.
  }, []);

  return (
    <QueryClientProvider client={queryClient}>
      <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
        <header
          style={{
            display: "flex",
            alignItems: "center",
            gap: "var(--space-4)",
            padding: "0 var(--space-4)",
            background: "var(--bg-surface)",
            borderBottom: "1px solid var(--divider)",
            height: 44,
          }}
        >
          <strong>{t("app.title")}</strong>
          <nav aria-label="main" style={{ display: "flex", gap: "var(--space-3)", flex: 1 }}>
            {topMenus
              .filter((m) => hasPermission(permissions, m.permission))
              .map((m) => (
                <button
                  key={m.id}
                  type="button"
                  onClick={() => (m.screen ? open({ key: m.id, screen: m.screen, title: t(m.label) }) : undefined)}
                  style={{ background: "none", border: 0, color: "inherit", cursor: "pointer", padding: "var(--space-2)" }}
                >
                  {t(m.label)}
                </button>
              ))}
          </nav>
          {import.meta.env.DEV ? (
            <button type="button" onClick={() => open({ key: "gallery", screen: "gallery", title: "Components" })}>Components</button>
          ) : null}
          <label>
            {t("pref.theme")}{" "}
            <select value={theme} onChange={(e) => setTheme(e.target.value === "dark" ? "dark" : "light")}>
              <option value="light">{t("pref.light")}</option>
              <option value="dark">{t("pref.dark")}</option>
            </select>
          </label>
          <label>
            {t("pref.language")}{" "}
            <select value={language} onChange={(e) => setLanguage(e.target.value === "en" ? "en" : "ar")}>
              <option value="ar">{"العربية"}</option>
              <option value="en">English</option>
            </select>
          </label>
        </header>
        <main style={{ flex: 1, minHeight: 0 }}>
          <WorkspaceShell />
        </main>
      </div>
    </QueryClientProvider>
  );
}
