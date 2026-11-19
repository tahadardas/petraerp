import { useT } from "@/i18n/messages";

export default function HomeScreen() {
  const t = useT();
  return (
    <section style={{ padding: "var(--space-6)" }}>
      <h1 style={{ fontSize: "var(--fs-title)", margin: 0 }}>{t("home.title")}</h1>
      <p style={{ color: "var(--text-muted)" }}>{t("home.body")}</p>
    </section>
  );
}
