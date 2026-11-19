import type { ReactNode } from "react";
import { hasPermission, useSession } from "@/app/permissions";

export interface PetraPermissionGateProps {
  permission: string | undefined;
  children: ReactNode;
  fallback?: ReactNode;
}

export function PetraPermissionGate({ permission, children, fallback = null }: PetraPermissionGateProps) {
  const permissions = useSession((s) => s.permissions);
  const allowed = hasPermission(permissions, permission);

  if (!allowed) {
    return <>{fallback}</>;
  }

  return <>{children}</>;
}
