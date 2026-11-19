import { create } from "zustand";

/** Context identifying the active company, branch, and fiscal year. */
export interface WorkspaceContext {
  companyId: string | null;
  branchId: string | null;
  companyName: string | null;
  fiscalYear: string | null;
  branchName: string | null;
}

/** Permission codes come from the server (e.g. "Ledger.Reports.View"); the UI only hides what the server would refuse anyway. */
export interface SessionState extends WorkspaceContext {
  userName: string | null;
  accessToken: string | null;
  workspaceId: string | null;
  permissions: ReadonlySet<string>;
  isOnline: boolean;
  pingMs: number | null;
  signIn: (s: {
    userName: string;
    accessToken: string;
    permissions: Iterable<string>;
    context?: Partial<WorkspaceContext>;
  }) => void;
  selectWorkspace: (workspaceId: string | null) => void;
  setContext: (ctx: Partial<WorkspaceContext>) => void;
  setConnectionStatus: (isOnline: boolean, pingMs?: number | null) => void;
  signOut: () => void;
}

export const useSession = create<SessionState>()((set) => ({
  userName: null,
  accessToken: null,
  workspaceId: null,
  permissions: new Set<string>(),
  companyId: null,
  branchId: null,
  companyName: null,
  fiscalYear: null,
  branchName: null,
  isOnline: true,
  pingMs: null,
  signIn: ({ userName, accessToken, permissions, context }) =>
    set({
      userName,
      accessToken,
      permissions: new Set(permissions),
      ...(context ?? {}),
    }),
  // A different workspace is a different company/year database: nothing cached for the previous one may be reused.
  selectWorkspace: (workspaceId) => set({ workspaceId }),
  setContext: (ctx) => set((s) => ({ ...s, ...ctx })),
  setConnectionStatus: (isOnline, pingMs = null) => set({ isOnline, pingMs }),
  signOut: () =>
    set({
      userName: null,
      accessToken: null,
      workspaceId: null,
      permissions: new Set<string>(),
      companyId: null,
      branchId: null,
      companyName: null,
      fiscalYear: null,
      branchName: null,
      isOnline: true,
      pingMs: null,
    }),
}));

/** Authorization is enforced by the API. This only decides what to show; an empty requirement is always allowed. */
export function hasPermission(permissions: ReadonlySet<string>, required: string | undefined): boolean {
  return required === undefined || permissions.has(required);
}
