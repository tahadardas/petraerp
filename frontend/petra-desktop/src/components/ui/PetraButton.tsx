import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";

export interface PetraButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: "primary" | "secondary" | "danger" | "ghost";
  size?: "sm" | "md";
  shortcut?: string;
  icon?: ReactNode;
  loading?: boolean;
}

export const PetraButton = forwardRef<HTMLButtonElement, PetraButtonProps>(function PetraButton(
  { variant = "secondary", size = "md", shortcut, icon, loading, disabled, children, style, ...rest },
  ref
) {
  const getBackground = () => {
    if (disabled || loading) return "var(--bg-sunken)";
    switch (variant) {
      case "primary": return "var(--primary)";
      case "danger": return "var(--danger)";
      case "ghost": return "transparent";
      default: return "var(--bg-surface)";
    }
  };

  const getColor = () => {
    if (disabled || loading) return "var(--text-muted)";
    switch (variant) {
      case "primary": return "var(--primary-contrast)";
      case "danger": return "#FFFFFF";
      case "ghost": return "var(--text)";
      default: return "var(--text)";
    }
  };

  const getBorder = () => {
    if (variant === "ghost" || variant === "primary" || variant === "danger") return "none";
    return "1px solid var(--border)";
  };

  return (
    <button
      ref={ref}
      disabled={disabled || loading}
      style={{
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        gap: "var(--space-2)",
        height: size === "sm" ? 28 : "var(--control-height)",
        padding: size === "sm" ? "0 var(--space-2)" : "0 var(--space-3)",
        fontSize: size === "sm" ? "var(--fs-caption)" : "var(--fs-body)",
        fontWeight: variant === "primary" ? 600 : 500,
        borderRadius: "var(--radius)",
        border: getBorder(),
        background: getBackground(),
        color: getColor(),
        cursor: disabled || loading ? "not-allowed" : "pointer",
        transition: "background 0.15s ease, border-color 0.15s ease",
        fontFamily: "inherit",
        whiteSpace: "nowrap",
        ...style,
      }}
      {...rest}
    >
      {loading ? <span className="petra-spinner" style={{ animation: "spin 1s linear infinite" }}>⏳</span> : icon}
      <span>{children}</span>
      {shortcut ? (
        <kbd
          style={{
            fontSize: "10px",
            padding: "1px 4px",
            borderRadius: 3,
            background: "rgba(0, 0, 0, 0.1)",
            color: "inherit",
            border: "1px solid rgba(0, 0, 0, 0.15)",
            fontFamily: "var(--font-mono)",
            marginInlineStart: "var(--space-1)",
          }}
        >
          {shortcut}
        </kbd>
      ) : null}
    </button>
  );
});
