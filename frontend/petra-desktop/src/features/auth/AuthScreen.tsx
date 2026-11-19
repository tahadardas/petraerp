import { useState } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { PetraButton, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface AuthScreenProps {
  onSuccess?: () => void;
}

export default function AuthScreen({ onSuccess }: AuthScreenProps) {
  const api = useApi();
  const signIn = useSession((s) => s.signIn);
  const selectWorkspace = useSession((s) => s.selectWorkspace);
  const [userName, setUserName] = useState("admin");
  const [password, setPassword] = useState("Try!NsfRGISWtqD+8sTWjJmC");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!userName.trim() || !password) {
      setError("يرجى إدخال اسم المستخدم وكلمة المرور");
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const res = await api.POST("/api/v1/auth/login", {
        body: {
          userName: userName.trim(),
          password,
          clientKind: "Desktop",
          deviceName: "Petra Desktop Windows",
        },
      });

      if (res.error) {
        const errorDetail = (res.error as { title?: string; detail?: string })?.detail ?? "بيانات الدخول غير صحيحة";
        setError(errorDetail);
        toast.error("فشل تسجيل الدخول", errorDetail);
        setLoading(false);
        return;
      }

      const tokenData = res.data;
      if (!tokenData?.accessToken) {
        setError("لم يتم استلام رمز المصادقة من الخادم");
        setLoading(false);
        return;
      }

      // Temporarily store token in session to make authenticated calls
      useSession.getState().signIn({
        userName: userName.trim(),
        accessToken: tokenData.accessToken,
        permissions: [],
      });

      // Fetch user profile and permissions
      const meRes = await api.GET("/api/v1/me");
      const permissions = meRes.data?.permissions ?? [];

      // Fetch user's workspaces
      const wsRes = await api.GET("/api/v1/me/workspaces");
      const workspaces = wsRes.data?.items ?? [];
      const defaultWs = workspaces.find((w) => w.isDefault) ?? workspaces[0];

      let branchId: string | null = null;
      let branchName: string | null = null;

      if (defaultWs) {
        // Fetch branches for default company
        const branchRes = await api.GET("/api/v1/me/companies/{companyId}/branches", {
          params: { path: { companyId: defaultWs.companyId } },
        });
        const firstBranch = branchRes.data?.items?.[0];
        branchId = firstBranch?.id ?? null;
        branchName = firstBranch?.nameArabic ?? null;

        selectWorkspace(defaultWs.workspaceId);
      }

      signIn({
        userName: meRes.data?.displayNameArabic ?? userName.trim(),
        accessToken: tokenData.accessToken,
        permissions,
        ...(defaultWs
          ? {
              context: {
                companyId: defaultWs.companyId,
                companyName: defaultWs.companyNameArabic,
                fiscalYear: defaultWs.yearNumber.toString(),
                branchId,
                branchName,
              },
            }
          : {}),
      });

      toast.success(`مرحباً بك، ${meRes.data?.displayNameArabic ?? userName}`);
      onSuccess?.();
    } catch (err) {
      const msg = err instanceof Error ? err.message : "تعذر الاتصال بخادم Petra ERP";
      setError(msg);
      toast.error("خطأ في الاتصال", msg);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        background: "var(--bg-app)",
        padding: "var(--space-4)",
      }}
    >
      <div
        style={{
          width: 420,
          maxWidth: "100%",
          background: "var(--bg-surface)",
          border: "1px solid var(--border)",
          borderRadius: "var(--radius)",
          boxShadow: "0 8px 24px rgba(0, 0, 0, 0.12)",
          overflow: "hidden",
        }}
      >
        <header
          style={{
            background: "var(--header-bg)",
            color: "var(--header-text)",
            padding: "var(--space-4)",
            textAlign: "center",
          }}
        >
          <h2 style={{ margin: "0 0 var(--space-1) 0", fontSize: 20, fontWeight: 700 }}>Petra ERP</h2>
          <span style={{ fontSize: "var(--fs-caption)", opacity: 0.85 }}>
            تسجيل الدخول — نظام المحاسبة والمستودعات
          </span>
        </header>

        <form onSubmit={handleSubmit} style={{ padding: "var(--space-6)" }}>
          {error ? (
            <div
              role="alert"
              style={{
                padding: "var(--space-2) var(--space-3)",
                background: "rgba(192, 57, 43, 0.1)",
                border: "1px solid var(--danger)",
                color: "var(--danger)",
                borderRadius: "var(--radius)",
                fontSize: "var(--fs-caption)",
                marginBottom: "var(--space-4)",
              }}
            >
              {error}
            </div>
          ) : null}

          <PetraField label="اسم المستخدم" required>
            <input
              type="text"
              value={userName}
              onChange={(e) => setUserName(e.target.value)}
              disabled={loading}
              autoFocus
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
                fontFamily: "inherit",
                fontSize: "var(--fs-body)",
                outline: "none",
              }}
            />
          </PetraField>

          <PetraField label="كلمة المرور" required>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={loading}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
                fontFamily: "inherit",
                fontSize: "var(--fs-body)",
                outline: "none",
              }}
            />
          </PetraField>

          <div
            style={{
              padding: "var(--space-2)",
              background: "var(--bg-sunken)",
              borderRadius: "var(--radius)",
              fontSize: "var(--fs-caption)",
              color: "var(--text-muted)",
              marginTop: "var(--space-2)",
              lineHeight: 1.5,
            }}
          >
            💡 بيانات بيئة المعاينة التجريبية:
            <br />
            المستخدم: <strong style={{ color: "var(--text)" }}>admin</strong> &nbsp;|&nbsp; المرور: <strong style={{ color: "var(--text)" }}>Try!NsfRGISWtqD+8sTWjJmC</strong>
          </div>

          <div style={{ marginTop: "var(--space-6)" }}>
            <PetraButton
              type="submit"
              variant="primary"
              loading={loading}
              style={{ width: "100%", height: 38 }}
            >
              دخول النظام
            </PetraButton>
          </div>
        </form>
      </div>
    </div>
  );
}
