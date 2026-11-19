import { useState, useEffect, useCallback } from "react";
import { useApi } from "@/services/api/ApiContext";
import {
  PetraButton,
  PetraField,
  PetraDialog,
  PetraConfirm,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

interface UserItem {
  id: string;
  userName: string;
  displayNameArabic: string;
  displayNameEnglish?: string | null;
  isActive: boolean;
  isLockedOut: boolean;
  mustChangePassword: boolean;
}

export default function UsersScreen() {
  const api = useApi();

  const [users, setUsers] = useState<UserItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);

  // New User Dialog
  const [isNewDialogOpen, setIsNewDialogOpen] = useState(false);
  const [newUserName, setNewUserName] = useState("");
  const [newDisplayNameAr, setNewDisplayNameAr] = useState("");
  const [newDisplayNameEn, setNewDisplayNameEn] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [creating, setCreating] = useState(false);

  // Status toggle
  const [selectedUser, setSelectedUser] = useState<UserItem | null>(null);
  const [isStatusDialogOpen, setIsStatusDialogOpen] = useState(false);

  const loadUsers = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/admin/users", {
        params: {
          query: {
            page,
            pageSize: 50,
            ...(search.trim() ? { search: search.trim() } : {}),
          },
        },
      });
      if (res.error) {
        setError(JSON.stringify(res.error));
      } else if (res.data) {
        setUsers(res.data.items || []);
        setTotalCount(res.data.totalCount || 0);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, page, search]);

  useEffect(() => {
    loadUsers();
  }, [loadUsers]);

  const handleCreateUser = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newUserName.trim() || !newDisplayNameAr.trim() || !newPassword) {
      toast.error("اسم المستخدم والاسم العربي وكلمة المرور مطلوبة");
      return;
    }

    setCreating(true);
    try {
      const res = await api.POST("/api/v1/admin/users", {
        body: {
          userName: newUserName.trim(),
          displayNameArabic: newDisplayNameAr.trim(),
          displayNameEnglish: newDisplayNameEn.trim() || null,
          initialPassword: newPassword,
        },
      });

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم إنشاء المستخدم بنجاح");
        setIsNewDialogOpen(false);
        setNewUserName("");
        setNewDisplayNameAr("");
        setNewDisplayNameEn("");
        setNewPassword("");
        loadUsers();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setCreating(false);
    }
  };

  const handleToggleStatus = async () => {
    if (!selectedUser) return;
    try {
      const res = await api.PUT("/api/v1/admin/users/{userId}/status", {
        params: { path: { userId: selectedUser.id } },
        body: {
          isActive: !selectedUser.isActive,
          reason: "تعديل الحالة من واجهة إدارة المستخدمين",
          version: "1",
        },
      });

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success(
          selectedUser.isActive ? "تم تعطيل المستخدم بنجاح" : "تم تفعيل المستخدم بنجاح"
        );
        setIsStatusDialogOpen(false);
        setSelectedUser(null);
        loadUsers();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Header Bar */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            إدارة المستخدمين (ADM-01)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            إجمالي: {totalCount}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <input
            type="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            placeholder="بحث عن مستخدم..."
            className="border border-[#CBD8E5] rounded px-3 py-1 text-sm bg-[#EEF3F8] focus:bg-white focus:border-[#087E87] outline-none w-56"
          />
          <PetraButton
            variant="secondary"
            onClick={() => loadUsers()}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsNewDialogOpen(true)}
            shortcut="F2"
          >
            + مستخدم جديد
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadUsers} />
        ) : users.length === 0 ? (
          <PetraEmptyState
            title="لا يوجد مستخدمون مطابقون"
            description="يمكنك إضافة مستخدمين جدد وتعيين أدوارهم وصلاحياتهم."
            actionLabel="+ إضافة مستخدم"
            onAction={() => setIsNewDialogOpen(true)}
          />
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm border-collapse text-right">
              <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                <tr>
                  <th className="p-2 border-l border-slate-700 w-36">اسم الدخول</th>
                  <th className="p-2 border-l border-slate-700">الاسم العربي</th>
                  <th className="p-2 border-l border-slate-700">الاسم الأجنبي</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-center">الحالة</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-center">القفل</th>
                  <th className="p-2 w-36 text-center">إجراءات</th>
                </tr>
              </thead>
              <tbody>
                {users.map((u, idx) => (
                  <tr
                    key={u.id}
                    className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] ${
                      idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                    }`}
                  >
                    <td className="p-2 font-mono font-bold text-[#087E87]">
                      {u.userName}
                    </td>
                    <td className="p-2 font-medium text-[#253447]">
                      {u.displayNameArabic}
                    </td>
                    <td className="p-2 text-slate-500 font-mono text-xs">
                      {u.displayNameEnglish || "—"}
                    </td>
                    <td className="p-2 text-center">
                      <span
                        className={`text-xs px-2 py-0.5 rounded font-bold ${
                          u.isActive
                            ? "bg-green-100 text-green-800"
                            : "bg-red-100 text-red-800"
                        }`}
                      >
                        {u.isActive ? "نشط" : "معطل"}
                      </span>
                    </td>
                    <td className="p-2 text-center">
                      <span
                        className={`text-xs px-2 py-0.5 rounded font-bold ${
                          u.isLockedOut
                            ? "bg-red-100 text-red-800"
                            : "bg-slate-100 text-slate-700"
                        }`}
                      >
                        {u.isLockedOut ? "مقفل" : "مفتوح"}
                      </span>
                    </td>
                    <td className="p-2 text-center">
                      <PetraButton
                        variant={u.isActive ? "danger" : "secondary"}
                        size="sm"
                        onClick={() => {
                          setSelectedUser(u);
                          setIsStatusDialogOpen(true);
                        }}
                      >
                        {u.isActive ? "تعطيل" : "تفعيل"}
                      </PetraButton>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        <div className="bg-[#EEF3F8] border-t border-[#CBD8E5] p-2 flex items-center justify-between text-xs text-slate-600">
          <span>صفحة {page}</span>
          <div className="flex gap-1">
            <PetraButton
              variant="secondary"
              size="sm"
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              السابق
            </PetraButton>
            <PetraButton
              variant="secondary"
              size="sm"
              disabled={users.length < 50}
              onClick={() => setPage((p) => p + 1)}
            >
              التالي
            </PetraButton>
          </div>
        </div>
      </div>

      {/* New User Dialog */}
      <PetraDialog
        open={isNewDialogOpen}
        onClose={() => setIsNewDialogOpen(false)}
        title="إضافة مستخدم جديد (ADM-01)"
      >
        <form onSubmit={handleCreateUser} className="space-y-3">
          <PetraField label="اسم الدخول (Username)" required>
            <input
              type="text"
              value={newUserName}
              onChange={(e) => setNewUserName(e.target.value)}
              placeholder="مثال: accountant1"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="الاسم العربي المعروض" required>
            <input
              type="text"
              value={newDisplayNameAr}
              onChange={(e) => setNewDisplayNameAr(e.target.value)}
              placeholder="مثال: أحمد المحاسب"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <PetraField label="الاسم الأجنبي">
            <input
              type="text"
              value={newDisplayNameEn}
              onChange={(e) => setNewDisplayNameEn(e.target.value)}
              placeholder="Ahmed Accountant"
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="كلمة المرور الأولية" required>
            <input
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder="••••••••"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <div className="flex justify-end gap-2 pt-3 border-t border-[#CBD8E5]">
            <PetraButton
              variant="secondary"
              onClick={() => setIsNewDialogOpen(false)}
              disabled={creating}
            >
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" type="submit" loading={creating}>
              إنشاء المستخدم
            </PetraButton>
          </div>
        </form>
      </PetraDialog>

      {/* Status Toggle Modal */}
      <PetraConfirm
        open={isStatusDialogOpen}
        onCancel={() => setIsStatusDialogOpen(false)}
        onConfirm={handleToggleStatus}
        title={selectedUser?.isActive ? "تعطيل حساب مستخدم" : "تفعيل حساب مستخدم"}
        message={`هل أنت متأكد من تغيير حالة المستخدم ${selectedUser?.displayNameArabic} (${selectedUser?.userName})؟`}
        confirmLabel={selectedUser?.isActive ? "تأكيد التعطيل" : "تأكيد التفعيل"}
        destructive={Boolean(selectedUser?.isActive)}
      />
    </div>
  );
}
