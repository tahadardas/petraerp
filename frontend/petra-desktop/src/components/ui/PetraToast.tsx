import { useToast, type ToastItem } from "./toastStore";

export function PetraToastContainer() {
  const { toasts, dismiss } = useToast();

  if (toasts.length === 0) return null;

  return (
    <div
      aria-live="polite"
      style={{
        position: "fixed",
        bottom: "calc(var(--space-4) + 28px)", // above status bar
        insetInlineEnd: "var(--space-4)",
        display: "flex",
        flexDirection: "column",
        gap: "var(--space-2)",
        zIndex: 9999,
        maxWidth: 420,
        pointerEvents: "none",
      }}
    >
      {toasts.map((t) => (
        <ToastCard key={t.id} toast={t} onDismiss={() => dismiss(t.id)} />
      ))}
    </div>
  );
}

function ToastCard({ toast, onDismiss }: { toast: ToastItem; onDismiss: () => void }) {
  const getColors = () => {
    switch (toast.type) {
      case "success": return { bg: "var(--bg-surface)", border: "var(--success)", icon: "✓", text: "var(--success)" };
      case "error": return { bg: "var(--bg-surface)", border: "var(--danger)", icon: "✕", text: "var(--danger)" };
      case "warning": return { bg: "var(--bg-surface)", border: "var(--warning)", icon: "⚠", text: "var(--warning)" };
      default: return { bg: "var(--bg-surface)", border: "var(--accent)", icon: "ℹ", text: "var(--accent)" };
    }
  };

  const colors = getColors();

  return (
    <div
      role="alert"
      style={{
        pointerEvents: "auto",
        display: "flex",
        alignItems: "flex-start",
        gap: "var(--space-2)",
        padding: "var(--space-3)",
        background: colors.bg,
        border: `1px solid var(--border)`,
        borderInlineStart: `4px solid ${colors.border}`,
        borderRadius: "var(--radius)",
        boxShadow: "0 4px 12px rgba(0, 0, 0, 0.15)",
        minWidth: 280,
      }}
    >
      <span style={{ color: colors.text, fontWeight: "bold", fontSize: 16 }}>{colors.icon}</span>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontWeight: 600, fontSize: "var(--fs-body)", color: "var(--text)" }}>{toast.message}</div>
        {toast.details ? (
          <div style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)", marginTop: 2, wordBreak: "break-word" }}>
            {toast.details}
          </div>
        ) : null}
      </div>
      <button
        type="button"
        onClick={onDismiss}
        aria-label="إغلاق التنبيه"
        style={{
          background: "none",
          border: 0,
          color: "var(--text-muted)",
          cursor: "pointer",
          padding: 2,
          fontSize: 14,
          lineHeight: 1,
        }}
      >
        ✕
      </button>
    </div>
  );
}
