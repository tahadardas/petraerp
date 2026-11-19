import { useEffect } from "react";

export interface ShortcutDefinition {
  id: string;
  key: string;
  ctrl?: boolean;
  alt?: boolean;
  shift?: boolean;
  description: string;
  category: "navigation" | "document" | "grid" | "global";
}

export const DEFAULT_SHORTCUTS: readonly ShortcutDefinition[] = [
  { id: "command-palette", key: "k", ctrl: true, description: "البحث الشامل والأوامر السريعة", category: "global" },
  { id: "close-tab", key: "w", ctrl: true, description: "إغلاق التبويب الحالي", category: "navigation" },
  { id: "cycle-tab", key: "Tab", ctrl: true, description: "التبديل بين التبويبات المفتوحة", category: "navigation" },
  { id: "save-document", key: "s", ctrl: true, description: "حفظ المستند الحالي", category: "document" },
  { id: "post-document", key: "F9", description: "ترحيل المستند المالي", category: "document" },
  { id: "new-document", key: "n", ctrl: true, description: "إنشاء مستند جديد", category: "document" },
  { id: "lookup-search", key: "F3", description: "فتح قائمة البحث في الحسابات والمواد والأطراف", category: "grid" },
  { id: "edit-cell", key: "F2", description: "تعديل محتوى الخلية", category: "grid" },
  { id: "safe-escape", key: "Escape", description: "إغلاق القوائم والنوافذ المنبثقة بأمان", category: "global" },
];

export function useGlobalShortcut(
  shortcut: { key: string; ctrl?: boolean; alt?: boolean; shift?: boolean },
  handler: (event: globalThis.KeyboardEvent) => void,
  enabled: boolean = true
) {
  useEffect(() => {
    if (!enabled) return;

    const onKeyDown = (e: globalThis.KeyboardEvent) => {
      const matchKey = e.key.toLowerCase() === shortcut.key.toLowerCase();
      const matchCtrl = !!shortcut.ctrl === (e.ctrlKey || e.metaKey);
      const matchAlt = !!shortcut.alt === e.altKey;
      const matchShift = !!shortcut.shift === e.shiftKey;

      if (matchKey && matchCtrl && matchAlt && matchShift) {
        handler(e);
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [shortcut, handler, enabled]);
}
