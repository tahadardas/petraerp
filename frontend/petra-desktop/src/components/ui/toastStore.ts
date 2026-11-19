import { create } from "zustand";

export interface ToastItem {
  id: string;
  type: "success" | "error" | "warning" | "info";
  message: string;
  details?: string | undefined;
  duration?: number | undefined;
}

interface ToastState {
  toasts: readonly ToastItem[];
  show: (toast: Omit<ToastItem, "id">) => string;
  dismiss: (id: string) => void;
}

export const useToast = create<ToastState>()((set) => ({
  toasts: [],
  show: (toast) => {
    const id = `toast-${Date.now()}-${Math.random().toString(36).substring(2, 7)}`;
    const duration = toast.duration ?? (toast.type === "error" ? 8000 : 4000);
    const item: ToastItem = { ...toast, id, duration };

    set((s) => ({ toasts: [...s.toasts, item] }));

    if (duration > 0) {
      setTimeout(() => {
        set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }));
      }, duration);
    }
    return id;
  },
  dismiss: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}));

export const toast = {
  success: (message: string, details?: string) => useToast.getState().show({ type: "success", message, details }),
  error: (message: string, details?: string) => useToast.getState().show({ type: "error", message, details }),
  warning: (message: string, details?: string) => useToast.getState().show({ type: "warning", message, details }),
  info: (message: string, details?: string) => useToast.getState().show({ type: "info", message, details }),
};
