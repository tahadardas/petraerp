import { create } from "zustand";

/** Permission codes come from the server (e.g. "Ledger.Reports.View"); the UI only hides what the server would refuse anyway. */
export interface SessionState {
  userName: string | null;
  accessToken: string | null;
  workspaceId: string | null;
  permissions: ReadonlySet<string>;
  signIn: (s: { userName: string; accessToken: string; permissions: Iterable<string> }) => void;
  selectWorkspace: (workspaceId: string | null) => void;
  signOut: () => void;
}

export const useSession = create<SessionState>()((set) => ({
  userName: null,
  accessToken: null,
  workspaceId: null,
  permissions: new Set<string>(),
  signIn: ({ userName, accessToken, permissions }) => set({ userName, accessToken, permissions: new Set(permissions) }),
  // A different workspace is a different company/year database: nothing cached for the previous one may be reused.
  selectWorkspace: (workspaceId) => set({ workspaceId }),
  signOut: () => set({ userName: null, accessToken: null, workspaceId: null, permissions: new Set<string>() }),
}));

/** Authorization is enforced by the API. This only decides what to show; an empty requirement is always allowed. */
export function hasPermission(permissions: ReadonlySet<string>, required: string | undefined): boolean {
  return required === undefined || permissions.has(required);
}
