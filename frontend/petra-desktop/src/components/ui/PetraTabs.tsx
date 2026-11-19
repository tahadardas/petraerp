export interface PetraTabItem {
  id: string;
  label: string;
  count?: number;
  disabled?: boolean;
}

export interface PetraTabsProps {
  tabs: readonly PetraTabItem[];
  activeId: string;
  onChange: (id: string) => void;
}

export function PetraTabs({ tabs, activeId, onChange }: PetraTabsProps) {
  return (
    <div
      role="tablist"
      style={{
        display: "flex",
        gap: "var(--space-1)",
        borderBottom: "1px solid var(--border)",
        background: "var(--bg-sunken)",
        padding: "var(--space-1) var(--space-2) 0",
      }}
    >
      {tabs.map((tab) => {
        const isActive = tab.id === activeId;
        return (
          <button
            key={tab.id}
            role="tab"
            aria-selected={isActive}
            disabled={tab.disabled}
            type="button"
            onClick={() => onChange(tab.id)}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "var(--space-2)",
              padding: "var(--space-2) var(--space-4)",
              background: isActive ? "var(--bg-surface)" : "transparent",
              border: "1px solid transparent",
              borderBottom: isActive ? "2px solid var(--primary)" : "2px solid transparent",
              borderTopLeftRadius: "var(--radius)",
              borderTopRightRadius: "var(--radius)",
              color: isActive ? "var(--primary)" : "var(--text-muted)",
              fontWeight: isActive ? 600 : 500,
              fontSize: "var(--fs-body)",
              cursor: tab.disabled ? "not-allowed" : "pointer",
              transition: "all 0.15s ease",
            }}
          >
            <span>{tab.label}</span>
            {tab.count !== undefined ? (
              <span
                style={{
                  fontSize: "var(--fs-caption)",
                  padding: "1px 6px",
                  borderRadius: 10,
                  background: isActive ? "rgba(8, 126, 135, 0.15)" : "var(--bg-sunken)",
                  color: isActive ? "var(--primary)" : "var(--text-muted)",
                }}
              >
                {tab.count}
              </span>
            ) : null}
          </button>
        );
      })}
    </div>
  );
}
