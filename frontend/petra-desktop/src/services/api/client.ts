import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./schema";

/** Where the session and the selected fiscal workspace come from. The UI never decides company/branch scope from request data. */
export interface ApiSession {
  /** Bearer token of the signed-in user, or null before sign-in. */
  getAccessToken: () => string | null;
  /** The registered fiscal workspace (company + fiscal year database) selected by the user, sent as X-Petra-Workspace. */
  getWorkspaceId: () => string | null;
}

export interface ApiOptions {
  baseUrl: string;
  session: ApiSession;
  /** Injectable for tests; defaults to the global fetch. */
  fetch?: typeof fetch;
}

export const WORKSPACE_HEADER = "X-Petra-Workspace";

/** Adds the bearer token and the workspace header to every request. Aborting a request aborts the HTTP call (cancellable queries). */
export function sessionMiddleware(session: ApiSession): Middleware {
  return {
    onRequest({ request }) {
      const token = session.getAccessToken();
      if (token) request.headers.set("Authorization", `Bearer ${token}`);
      const workspace = session.getWorkspaceId();
      if (workspace) request.headers.set(WORKSPACE_HEADER, workspace);
      return request;
    },
  };
}

/**
 * The typed Petra API client. Every path, parameter and response is checked against the generated OpenAPI schema
 * (`npm run api:generate`). Amounts travel as decimal strings; the UI formats them, it never does accounting arithmetic.
 */
export function createApi({ baseUrl, session, fetch: fetchImpl }: ApiOptions) {
  const client = createClient<paths>({ baseUrl, ...(fetchImpl ? { fetch: fetchImpl } : {}) });
  client.use(sessionMiddleware(session));
  return client;
}

export type PetraApi = ReturnType<typeof createApi>;
