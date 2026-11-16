param([string]$Out = '.artifacts\evidence\probe.txt')
Set-Location (Split-Path (Split-Path $PSScriptRoot))
$terms = @(
 'BranchAccountLink','ParentBranchId','class BranchRow','FifoLayer|CostLayer|InventoryCostLayer','CostMethod.Fifo|CostMethod\.FIFO','AllowedItemGroups','ParentWarehouse',
 'NumberingMaintenance|DocumentNumberMaintenance|RenumberDocuments','class BackupRestore|BackupRestore','VerifyRestore|RESTORE VERIFYONLY','RoundingMode','MinorUnit|SubUnit',
 'AgingBucket|Aging','ClosingSnapshot|ClosureSnapshot|PriorYearClosing','UNVERIFIED','AccountingMutationPolicy','DocumentRequestBindingRow','TransactionalOutbox|OutboxMessage',
 'InterBranch|IntercompanyMatch|CrossCompanyMatch','CurrencyBulletin|ExchangeRateBulletin|RateBulletin','StocktakingSchedule|Stocktaking|CycleCount','Reserved(Quantity)?',
 'SerialNumber|LotNumber','ReportCatalog|ReportDefinition','AddInterceptors|DbCommandInterceptor'
)
$lines = foreach ($t in $terms) {
  $hits = git grep -l -E $t -- src 2>$null
  "{0,-60} files={1}  {2}" -f $t, ($hits | Measure-Object).Count, (($hits | Select-Object -First 3) -join '; ')
}
$lines | Out-File -Encoding utf8 $Out
$lines
