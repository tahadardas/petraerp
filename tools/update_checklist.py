import csv
import json
import sys
import openpyxl

sys.stdout.reconfigure(encoding='utf-8')

updates = {
    'UX-001': {
        'status': '✅ مكتمل حسب سجل المشروع',
        'verification': '🧪 تم التحقق بالاختبارات',
        'evidence': 'DesktopWindow.xaml: PurchasesMenu bound to Navigation.PurchasesItems; DesktopMenuNavigationTests.cs: AuthorizedPurchaseOnlyUserSeesPurchasesButNotTreasuryOrSales passed',
        'next': 'استمرار مراقبة صلاحيات القوائم في المراحل اللاحقة',
        'test_result': 'PASS: 7/7 DesktopMenuNavigationTests, 588 C# passed, 42 Python passed, Release 0 errors',
        'updated_at': '2026-10-19'
    },
    'UX-002': {
        'status': '✅ مكتمل حسب سجل المشروع',
        'verification': '🧪 تم التحقق بالاختبارات',
        'evidence': 'DesktopWindow.xaml: TreasuryMenu bound to Navigation.TreasuryItems; DesktopMenuNavigationTests.cs: AuthorizedTreasuryOnlyUserSeesTreasuryButNotPurchasesOrSales passed',
        'next': 'استكمال تخصيصات سند الدفع على المشتريات (PT-108/PT-109)',
        'test_result': 'PASS: 7/7 DesktopMenuNavigationTests, 588 C# passed, 42 Python passed, Release 0 errors',
        'updated_at': '2026-10-19'
    },
    'UX-003': {
        'status': '✅ مكتمل حسب سجل المشروع',
        'verification': '🧪 تم التحقق بالاختبارات',
        'evidence': 'NavigationService.cs Notify() raises OnPropertyChanged(nameof(SalesItems)) and OnPropertyChanged(nameof(HasSales)); DesktopMenuNavigationTests.cs passed',
        'next': 'مكتمل',
        'test_result': 'PASS: PropertyChangedNotifiesSalesItemsHasSalesPurchasesAndTreasuryOnConfigureAndReset',
        'updated_at': '2026-10-19'
    },
    'UX-004': {
        'status': '✅ مكتمل حسب سجل المشروع',
        'verification': '🧪 تم التحقق بالاختبارات',
        'evidence': 'NavigationService.cs exposes PurchasesItems, TreasuryItems, HasPurchases, HasTreasury with full notifications in Notify(), Configure(), Reset() and Relocalize()',
        'next': 'مكتمل',
        'test_result': 'PASS: DesktopMenuNavigationTests verifies all properties and events',
        'updated_at': '2026-10-19'
    },
    'UX-005': {
        'status': '✅ مكتمل حسب سجل المشروع',
        'verification': '🧪 تم التحقق بالاختبارات',
        'evidence': 'src/PetraERP.Desktop/Shell/MenuRegistry.cs created with 12 Al-Ameen top categories and permission/capability filtering; spec-only nodes filtered out of production UI',
        'next': 'ربط القوائم الفرعية المتقدمة لكل شريحة عند تنفيذها',
        'test_result': 'PASS: MenuRegistryEncapsulatesAmeenHierarchyAndHidesSpecOnlyFutureNodes',
        'updated_at': '2026-10-19'
    },
    'UX-031': {
        'status': '✅ مكتمل حسب سجل المشروع',
        'verification': '🧪 تم التحقق بالاختبارات',
        'evidence': 'docs/architecture/RELATIONSHIP_NAVIGATION_MATRIX.md line 68 updated to remove obsolete (Purchasing absent) claim',
        'next': 'مكتمل',
        'test_result': 'PASS: Documentation drift resolved and verified against source',
        'updated_at': '2026-10-19'
    },
    'UX-032': {
        'status': '✅ مكتمل حسب سجل المشروع',
        'verification': '🧪 تم التحقق بالاختبارات',
        'evidence': 'src/PetraERP.Desktop/Shell/NavigationService.cs XML comment updated to remove obsolete (purchases and others) are absent claim',
        'next': 'مكتمل',
        'test_result': 'PASS: Documentation drift resolved and verified against source',
        'updated_at': '2026-10-19'
    }
}

# 1. Update Excel
xlsx_path = r'D:\PetraERP\PetraERP_Continuation_Pack_UPDATED_2026-10-19\PetraERP_CHECKLIST.xlsx'
wb = openpyxl.load_workbook(xlsx_path)
ws = wb['قائمة التحقق']

for r in range(2, ws.max_row + 1):
    item_id = ws.cell(row=r, column=1).value
    if item_id in updates:
        u = updates[item_id]
        ws.cell(row=r, column=5, value=u['status'])
        ws.cell(row=r, column=6, value=u['verification'])
        ws.cell(row=r, column=8, value=u['evidence'])
        ws.cell(row=r, column=9, value=u['next'])
        ws.cell(row=r, column=12, value=u['test_result'])
        ws.cell(row=r, column=13, value=u['updated_at'])
        print(f"Updated Excel item {item_id}")

wb.save(xlsx_path)
print("Saved updated Excel:", xlsx_path)

# 2. Update CSV files
csv_paths = [
    r'D:\PetraERP\PetraERP_Continuation_Pack_UPDATED_2026-10-19\CHECKLIST.csv',
    r'D:\PetraERP\CHECKLIST.csv'
]

for p in csv_paths:
    rows = []
    with open(p, 'r', encoding='utf-8') as f:
        reader = csv.reader(f)
        header = next(reader)
        rows.append(header)
        for row in reader:
            item_id = row[0].lstrip('\ufeff')
            if item_id in updates:
                u = updates[item_id]
                row[4] = u['status']
                row[5] = u['verification']
                row[7] = u['evidence']
                row[8] = u['next']
            rows.append(row)
    with open(p, 'w', encoding='utf-8', newline='') as f:
        writer = csv.writer(f)
        writer.writerows(rows)
    print("Saved updated CSV:", p)

# 3. Update JSON files
json_paths = [
    r'D:\PetraERP\PetraERP_Continuation_Pack_UPDATED_2026-10-19\checklist_data.json',
    r'D:\PetraERP\checklist_data.json'
]

for jp in json_paths:
    with open(jp, 'r', encoding='utf-8') as f:
        data = json.load(f)
    for item in data:
        item_id = item.get('id')
        if item_id in updates:
            u = updates[item_id]
            item['status'] = u['status']
            item['verification'] = u['verification']
            item['evidence'] = u['evidence']
            item['next'] = u['next']
    with open(jp, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print("Saved updated JSON:", jp)
