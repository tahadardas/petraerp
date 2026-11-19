import { describe, expect, it } from "vitest";
import { createApi, WORKSPACE_HEADER } from "./client";

describe("typed API client", () => {
  it("sends the bearer token and the workspace header on every request, and passes the abort signal through", async () => {
    let captured: Request | undefined;
    const fakeFetch: typeof fetch = async (input) => {
      captured = input as Request;
      return new Response(JSON.stringify([]), { status: 200, headers: { "Content-Type": "application/json" } });
    };
    const api = createApi({
      baseUrl: "https://localhost:17483",
      session: { getAccessToken: () => "token-123", getWorkspaceId: () => "11111111-1111-1111-1111-111111111111" },
      fetch: fakeFetch,
    });
    const controller = new AbortController();

    const result = await api.GET("/api/v1/me/workspaces", { signal: controller.signal });

    expect(result.response.status).toBe(200);
    expect(captured?.headers.get("Authorization")).toBe("Bearer token-123");
    expect(captured?.headers.get(WORKSPACE_HEADER)).toBe("11111111-1111-1111-1111-111111111111");
    expect(captured?.url).toBe("https://localhost:17483/api/v1/me/workspaces");
    expect(captured?.signal).toBeDefined();
  });

  it("sends no auth headers before sign-in", async () => {
    let captured: Request | undefined;
    const fakeFetch: typeof fetch = async (input) => {
      captured = input as Request;
      return new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } });
    };
    const api = createApi({ baseUrl: "https://localhost:17483", session: { getAccessToken: () => null, getWorkspaceId: () => null }, fetch: fakeFetch });
    await api.GET("/api/v1/me/workspaces");
    expect(captured?.headers.get("Authorization")).toBeNull();
    expect(captured?.headers.get(WORKSPACE_HEADER)).toBeNull();
  });
});
