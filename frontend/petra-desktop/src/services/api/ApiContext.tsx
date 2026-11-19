import { createContext, useContext, useMemo, type ReactNode } from "react";
import { createApi, type PetraApi } from "./client";
import { useSession } from "@/app/permissions";

const ApiContext = createContext<PetraApi | null>(null);

export interface ApiProviderProps {
  baseUrl?: string;
  children: ReactNode;
  fetch?: typeof fetch;
}

export function ApiProvider({ baseUrl = "http://localhost:5000", children, fetch: fetchImpl }: ApiProviderProps) {
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
