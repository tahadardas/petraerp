import { PetraButton } from "./PetraButton";

export interface PetraErrorDetailsProps {
  status?: number | undefined;
  message?: string | undefined;
  traceId?: string | undefined;
  onRetry?: (() => void) | undefined;
}

export function PetraErrorDetails({
  status,
  message,
  traceId,
  onRetry,
}: PetraErrorDetailsProps) {
  const getStatusExplanation = () => {
    switch (status) {
      case 400: return "الطلب غير صالح أو البيانات المدخلة غير صحيحة.";
      case 401: return "انتهت الجلسة أو يجب تسجيل الدخول مجددًا.";
      case 403: return "ليس لديك الصلاحية الكافية للوصول إلى هذه العملية.";
      case 404: return "العنصر المطلوب غير موجود على الخادم.";
      case 409: return "تعارض في البيانات أو إصدار المستند (تعديل متزامن).";
      case 422: return "تعذر معالجة البيانات بسبب قيود محاسبية أو مالية.";
      case 500: return "حدث خطأ غير متوقع في الخادم.";
      default: return message ?? "حدث خطأ غير معروف أثناء الاتصال.";
    }
  };

  return (
    <div
      role="alert"
      style={{
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: "var(--space-6) var(--space-4)",
        background: "var(--bg-sunken)",
        border: "1px solid var(--danger)",
        borderRadius: "var(--radius)",
        margin: "var(--space-4)",
        textAlign: "center",
      }}
    >
      <div style={{ fontSize: 32, color: "var(--danger)", marginBottom: "var(--space-2)" }}>⚠</div>
      <h4 style={{ margin: "0 0 var(--space-1) 0", color: "var(--danger)" }}>
        {status ? `خطأ ${status}` : "تعذر إكمال العملية"}
      </h4>
      <p style={{ margin: "0 0 var(--space-3) 0", color: "var(--text)", maxWidth: 500 }}>
        {getStatusExplanation()}
      </p>
      {traceId ? (
        <code
          style={{
            fontSize: "var(--fs-caption)",
            background: "var(--bg-surface)",
            padding: "2px 8px",
            borderRadius: 4,
            marginBottom: "var(--space-3)",
            fontFamily: "var(--font-mono)",
          }}
        >
          Trace ID: {traceId}
        </code>
      ) : null}
      {onRetry ? (
        <PetraButton variant="secondary" onClick={onRetry}>
          إعادة المحاولة
        </PetraButton>
      ) : null}
    </div>
  );
}
