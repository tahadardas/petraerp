import { useEffect, useState } from "react";
import { useSession } from "@/app/permissions";

export interface PetraStatusBarProps {
  onOpenContextSwitch?: () => void;
}

export function PetraStatusBar({ onOpenContextSwitch }: PetraStatusBarProps) {
  const userName = useSession((s) => s.userName);
  const companyName = useSession((s) => s.companyName ?? "—");
  const fiscalYear = useSession((s) => s.fiscalYear ?? "—");
  const branchName = useSession((s) => s.branchName ?? "—");
  const isOnline = useSession((s) => s.isOnline);
  const pingMs = useSession((s) => s.pingMs);

  const [time, setTime] = useState(() => new Date().toLocaleTimeString("ar-EG"));

  useEffect(() => {
    const timer = setInterval(() => {
      setTime(new Date().toLocaleTimeString("ar-EG"));
    }, 1000);
    return () => clearInterval(timer);
  }, []);

  return (
    <footer
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "0 var(--space-3)",
        height: 26,
        background: "var(--bg-sunken)",
        borderTop: "1px solid var(--border)",
        fontSize: "var(--fs-caption)",
        color: "var(--text-muted)",
        userSelect: "none",
        zIndex: 10,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: "var(--space-4)" }}>
        <button
          type="button"
          onClick={onOpenContextSwitch}
          title="تبديل الشركة أو السنة أو الفرع"
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: "var(--space-1)",
            background: "none",
            border: 0,
            color: "inherit",
            cursor: onOpenContextSwitch ? "pointer" : "default",
            font: "inherit",
            padding: 0,
          }}
        >
          <span style={{ fontWeight: 600, color: "var(--text)" }}>🏢 {companyName}</span>
          <span>/</span>
          <span>📅 {fiscalYear}</span>
          <span>/</span>
          <span>📍 {branchName}</span>
        </button>

        <span style={{ borderInlineStart: "1px solid var(--divider)", height: 14 }} />

        <span>👤 {userName ?? "غير مسجل"}</span>
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: "var(--space-3)" }}>
        <span
          title={isOnline ? `متصل بالخادم (${pingMs ?? 0}ms)` : "غير متصل بالخادم"}
          style={{ display: "inline-flex", alignItems: "center", gap: 4 }}
        >
          <span
            style={{
              width: 8,
              height: 8,
              borderRadius: "50%",
              background: isOnline ? "var(--success)" : "var(--danger)",
              display: "inline-block",
            }}
          />
          <span>{isOnline ? "متصل" : "غير متصل"}</span>
          {pingMs !== null ? <span className="numeric">{pingMs}ms</span> : null}
        </span>

        <span style={{ borderInlineStart: "1px solid var(--divider)", height: 14 }} />

        <span className="numeric">{time}</span>
      </div>
    </footer>
  );
}
