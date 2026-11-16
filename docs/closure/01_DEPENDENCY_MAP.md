# خريطة الاعتماديات (Gate A) — مستخرجة من ملفات csproj في 2026-11-15

## اعتماديات المشاريع (مباشرة)
| المشروع | يعتمد على |
|---|---|
| PetraERP.Domain | — |
| PetraERP.Application | Domain |
| PetraERP.Contracts | — |
| PetraERP.Infrastructure | Application, Modules.Abstractions, Modules.Host, Modules.Packaging |
| PetraERP.Api | Application, Contracts, Infrastructure |
| PetraERP.ApiClient | — (مُولَّد من عقد OpenAPI) |
| PetraERP.Desktop | ApiClient, Reporting.OpenSource |
| PetraERP.Modules.Abstractions | — |
| PetraERP.Modules.Packaging | Modules.Abstractions |
| PetraERP.Modules.Host | Modules.Abstractions, Modules.Packaging |
| PetraERP.Modules.Sample | Modules.Abstractions |
| PetraERP.Reporting.OpenSource | — |

## ما يعنيه ذلك (وما لا يعنيه)
- **حدّ نظيف:** Desktop لا يرى Domain ولا Application ولا Infrastructure؛ يتكلم مع الخادم عبر العميل المُولَّد فقط. أي منطق مالي في Desktop خطأ معماري.
- **Contracts وApiClient بلا اعتمادية على Domain**: العقد نصوص وأرقام (المبالغ كنص ثابت)، لا أنواع النطاق.
- **Infrastructure يحمل منصة الوحدات** (Host وPackaging) لأن تشغيل الوحدات يحتاج الوصول للقاعدة؛ الموديولات الخارجية ترى Abstractions فقط.
- **Api لا يعتمد على Domain مباشرة** (يصل إليه عبر Application).
- لا دورات في الرسم أعلاه.

## ما لم يُنجَز في Gate A
- اعتماديات على مستوى النوع/الدالة (من يستدعي من) لم تُرسم؛ الخريطة أعلاه على مستوى المشروع فقط.
- خريطة DI التفصيلية (كل تسجيل → تنفيذ) لم تُكتب؛ مصدرها `PetraServiceRegistration.cs` و`DesktopComposition.cs`.
- مطابقة المسارات مع الاستدعاءات (Route↔Client) موجودة جزئيًا في `API_ROUTE_MAPPING.csv`.
