# أسماء الأنماط والتقنيات اللازمة لمضاهاة الأمين 10

الاسم أدناه اسم **نمط واجهة** أو اقتراح تنفيذ WPF؛ لا يعني بالضرورة أنه اسم المكتبة التي استخدمها الأمين.

| المفهوم المرئي والسلوكي | المصطلح الهندسي | أدوات التنفيذ داخل Petra ERP |
|---|---|---|
| الشريط العلوي | Ribbon UI / Tabbed Command Bar | TabControl + command groups + ICommand |
| سطح عمل متعدد النوافذ | MDI Workspace / Child Windows | MdiWorkspaceHost + MdiLayoutEngine الحالية |
| نافذة عائمة | Floating Child Window / Window Chrome | Thumb + resize + focus/z-index |
| الأدلة المثبتة | Dockable Tool Window | docking layout service |
| إخفاء الأدلة تلقائيًا | Auto-Hide Dock Panels | side tabs + activation + restore |
| جمع الأدلة في تبويبات | Tabbed Dock Group | dock tab container |
| نقاط إسقاط السحب | Docking Guides / Compass | adorners and hit testing |
| تقسيم الدليل | Split Pane / GridSplitter | Grid + GridSplitter + TreeView |
| شجرة الحسابات والمواد | Hierarchical TreeView / TreeGrid | TreeView with virtualization where valid |
| بطاقة الحساب والمادة | Record Form with Side Tabs | fixed header + category navigator + footer commands |
| التنقل في السجلات | Record Navigator | First / Previous / Next / Last |
| نافذة اختيار الحقول | Lookup Dialog / Search Selector | searchable dialog, Enter/F3/Esc handling |
| شبكة الفواتير | Editable DataGrid / Grid Editor | DataGrid + keyboard focus navigation |
| قائمة الزر الأيمن | ContextMenu | standard WPF ContextMenu |
| مرشحات التقرير | Report Parameters Dialog | parameters view before preview |
| نتائج التقرير | Report Preview Grid | grid / sort / totals / export actions |
| معلومات الملف المستخدم | Status Bar | WPF StatusBar |

## سياسات غير قابلة للتفاوض

- لا تغيير لـ .NET 10، أو WPF/XAML، أو API/SQL Server، أو الترحيل والأرصدة.
- لا تغيير أسلوب التصميم إلى Dashboard عصري، ولا إدخال مكتبات تجارية دون مبرر وترخيص.
- مرجع **التقرير الافتراضي** في الأمين 10 هو «قياسي» وأزراره السفلية أفقية، وفق مدونة الشركة؛ «أمين» العمودي خيار بديل مشابه للأمين 9.
- الرقم الغربي 0–9 وRTL إلزاميان في Petra ERP.
- لا يمكن استنتاج أبعاد نافذة WPF الدقيقة أو DPI من لقطة رسمية مضغوطة؛ يُعتمد التخطيط النسبي والنسب البصرية ثم تجربة تشغيل Windows.
