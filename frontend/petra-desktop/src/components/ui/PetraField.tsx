import type { ReactNode } from "react";

export interface PetraFieldProps {
  label: string;
  required?: boolean;
  error?: string;
  helperText?: string;
  horizontal?: boolean;
  labelWidth?: number | string;
  children: ReactNode;
}

export function PetraField({
  label,
  required,
  error,
  helperText,
  horizontal = false,
  labelWidth = 120,
  children,
}: PetraFieldProps) {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: horizontal ? "row" : "column",
        alignItems: horizontal ? "center" : "stretch",
        gap: horizontal ? "var(--space-2)" : "var(--space-1)",
        marginBottom: "var(--space-2)",
      }}
    >
      <label
        style={{
          width: horizontal ? labelWidth : undefined,
          flexShrink: 0,
          fontSize: "var(--fs-caption)",
          fontWeight: 600,
          color: error ? "var(--danger)" : "var(--text-muted)",
        }}
      >
        {label}
        {required ? <span style={{ color: "var(--danger)", marginInlineStart: 2 }}>*</span> : null}
      </label>
      <div style={{ flex: 1, display: "flex", flexDirection: "column" }}>
        {children}
        {error ? (
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--danger)", marginTop: 2 }}>
            {error}
          </span>
        ) : helperText ? (
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)", marginTop: 2 }}>
            {helperText}
          </span>
        ) : null}
      </div>
    </div>
  );
}
