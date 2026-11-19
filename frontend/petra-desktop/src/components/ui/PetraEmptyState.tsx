import type { ReactNode } from "react";
import { PetraButton } from "./PetraButton";

export interface PetraEmptyStateProps {
  icon?: string;
  title: string;
  description?: string;
  actionLabel?: string;
  onAction?: () => void;
  children?: ReactNode;
}

export function PetraEmptyState({
  icon = "📂",
  title,
  description,
  actionLabel,
  onAction,
  children,
}: PetraEmptyStateProps) {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: "var(--space-6) var(--space-4)",
        textAlign: "center",
        color: "var(--text-muted)",
        minHeight: 200,
      }}
    >
      <div style={{ fontSize: 40, marginBottom: "var(--space-2)" }}>{icon}</div>
      <h4 style={{ margin: "0 0 var(--space-1) 0", fontSize: "var(--fs-title)", color: "var(--text)" }}>
        {title}
      </h4>
      {description ? (
        <p style={{ margin: "0 0 var(--space-4) 0", maxWidth: 400, fontSize: "var(--fs-body)" }}>
          {description}
        </p>
      ) : null}
      {actionLabel && onAction ? (
        <PetraButton variant="primary" onClick={onAction}>
          {actionLabel}
        </PetraButton>
      ) : null}
      {children}
    </div>
  );
}
