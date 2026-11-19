import { createContext, useContext, useMemo, type ReactNode } from "react";
import { createApi, type PetraApi } from "./client";
import { useSession } from "@/app/permissions";

const ApiContext = createContext<PetraApi | null>(null);

export interface ApiProviderProps {
  baseUrl?: string;
  children: ReactNode;
  fetch?: typeof fetch;
}

const DEFAULT_BASE_URL = (import.meta.env.VITE_API_URL as string | undefined) || "https://localhost:17483";

export function ApiProvider({ baseUrl = DEFAULT_BASE_URL, children, fetch: fetchImpl }: ApiProviderProps) {
  const api = useMemo(() => {
    return createApi({
      baseUrl,
      session: {
        getAccessToken: () => useSession.getState().accessToken,
        getWorkspaceId: () => useSession.getState().workspaceId,
      },
      ...(fetchImpl ? { fetch: fetchImpl } : {}),
    });
  }, [baseUrl, fetchImpl]);

  return <ApiContext.Provider value={api}>{children}</ApiContext.Provider>;
}

export function useApi(): PetraApi {
  const api = useContext(ApiContext);
  if (!api) {
    throw new Error("useApi must be used within an ApiProvider");
  }
  return api;
}
