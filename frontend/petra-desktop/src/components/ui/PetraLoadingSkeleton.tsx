export interface PetraLoadingSkeletonProps {
  rows?: number;
  height?: number;
}

export function PetraLoadingSkeleton({ rows = 5, height = 28 }: PetraLoadingSkeletonProps) {
  return (
    <div
      aria-label="جاري التحميل"
      role="progressbar"
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--space-2)",
        padding: "var(--space-4)",
        width: "100%",
        boxSizing: "border-box",
      }}
    >
      {Array.from({ length: rows }, (_, i) => (
        <div
          key={i}
          style={{
            height,
            width: i === 0 ? "40%" : i === rows - 1 ? "60%" : "100%",
            borderRadius: "var(--radius)",
            background: "linear-gradient(90deg, var(--bg-sunken) 25%, var(--bg-row-alt) 50%, var(--bg-sunken) 75%)",
            backgroundSize: "200% 100%",
            animation: "skeleton-shimmer 1.5s infinite linear",
            opacity: 0.8,
          }}
        />
      ))}
      <style>{`
        @keyframes skeleton-shimmer {
          0% { background-position: 200% 0; }
          100% { background-position: -200% 0; }
        }
      `}</style>
    </div>
  );
}
