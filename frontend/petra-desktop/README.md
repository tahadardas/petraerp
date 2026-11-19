# Petra Desktop — React + TypeScript + Tauri 2

الواجهة الرسمية لـPetra ERP (قرار المالك 19 نوفمبر 2026، `docs/adr/ADR-0003-wpf-to-react-tauri-desktop-migration.md`). مصدر البيانات والعمليات الوحيد: ASP.NET Core Web API. لا يُنسخ منطق المحاسبة إلى هنا: الأرصدة والمجاميع والترقيم والترحيل والموافقات من الخادم.

**الحالة: المرحلة 1 (Architecture Migration) — هيكل فقط.** لا شاشات محاسبية بعد؛ الشاشات تُبنى شاشة واحدة في كل خطوة فوق API الحقيقي.

## التشغيل
```bash
npm install
npm run dev          # Vite على http://127.0.0.1:1420
npm run typecheck    # TypeScript strict
npm test             # Vitest + Testing Library
npm run build        # typecheck ثم Vite
npm run api:generate # يولّد src/services/api/schema.d.ts من OpenAPI
npm run api:check    # يفشل إن انحرف المخطط المولَّد عن العقد
npm run tauri dev    # يتطلب Rust (انظر أدناه)
```

## الهيكل
| المجلد | المسؤولية |
|---|---|
| `src/app/` | التطبيق، القوائم العلوية، الصلاحيات، سجل الشاشات (تحميل كسول) |
| `src/workspace/` | النوافذ الداخلية والتبويبات (متعدد المستندات، تثبيت التخطيط، Ctrl+W / Ctrl+Tab) |
| `src/design-system/` | رموز «Petra Calm Enterprise» (فاتح/داكن، RTL/LTR، كثافة) وتفضيلات المستخدم |
| `src/services/api/` | عميل API مطبوع من OpenAPI (`openapi-fetch`)، يضيف `Authorization` و`X-Petra-Workspace`، ويمرر إشارة الإلغاء |
| `src/features/` | ميزة لكل مجال (المحاسبة، الفواتير، السندات، التقارير…) |
| `src/components/` | مكونات مشتركة (محرك الإدخال والجداول والقوائم السياقية — المرحلة التالية) |
| `src/i18n/` | نصوص الواجهة (عربي/إنجليزي) |
| `src-tauri/` | غلاف Windows (Tauri 2): CSP مقيّد، صلاحيات `core:default` فقط |

## قواعد
- TypeScript Strict + `noUncheckedIndexedAccess` + `exactOptionalPropertyTypes`.
- المبالغ نصوص عشرية من API؛ الواجهة تنسّق ولا تحسب.
- تبديل مساحة العمل (شركة/سنة) يمسح كاش الاستعلامات.
- لا خاصية نمط من الاسم أو اللون: تُقرأ من `GET patterns/{id}/effective`.
- الجداول: 50 سطراً جاهزاً حسب الإعداد، تحرير بلوحة المفاتيح، Virtualization (المرحلة التالية).

## متطلبات Tauri (غير متوفرة في بيئة التطوير الحالية)
Rust (rustup, toolchain stable `x86_64-pc-windows-msvc`)، Visual Studio Build Tools (C++)، WebView2. **لم يُبنَ غلاف Tauri بعد** (Rust غير مثبّت هنا)؛ ما تحقق: واجهة Vite تُبنى وتُختبر، وملفات `src-tauri/` مكتوبة دون ترجمة. الخادم المحلي بشهادة HTTPS ذاتية التوقيع (`https://localhost:17483`): قد يحتاج WebView2 إلى الوثوق بها، وقد تلزم سياسة شهادة للتطوير.

## الملكية
Antigravity = مالك الواجهة (هذا المجلد). Claude Code = مالك Backend/OpenAPI. فرع أو Worktree مستقل لكل وكيل؛ لا تعديل لملفات الآخر دون تنسيق.
