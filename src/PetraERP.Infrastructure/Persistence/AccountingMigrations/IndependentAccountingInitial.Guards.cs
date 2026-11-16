using Microsoft.EntityFrameworkCore.Migrations;

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;

public partial class IndependentAccountingInitial
{
    private static void InstallGuards(MigrationBuilder migrationBuilder)
    {
        // These protections are part of this baseline, independent of Legacy migration history.
        (string Schema, string Table, string Trigger)[] immutable =
        [
            ("audit", "ActorReferences", "TR_ActorReferences_Immutable"),
            ("ledger", "AccountAudit", "TR_AccountAudit_Immutable"),
            ("ledger", "AccountAuditStates", "TR_AccountAuditStates_Immutable"),
            ("ledger", "CostCenterAudit", "TR_CostCenterAudit_Immutable"),
            ("ledger", "CostCenterAuditStates", "TR_CostCenterAuditStates_Immutable"),
            ("ledger", "JournalAudit", "TR_JournalAudit_Immutable"),
            ("ledger", "PeriodAudit", "TR_PeriodAudit_Immutable"),
            ("inventory", "StockMovements", "TR_StockMovements_Immutable"),
            ("system", "ExchangeRates", "TR_ExchangeRates_AppendOnly"),
            ("system", "CurrencyAudit", "TR_CurrencyAudit_AppendOnly"),
            ("treasury", "SettlementApplications", "TR_SettlementApplications_Immutable"),
            ("sales", "CreditAudit", "TR_CreditAudit_Immutable"),
            ("parties", "PolicyAudit", "TR_PolicyAudit_Immutable"),
        ];
        foreach (var (schema, table, trigger) in immutable)
            migrationBuilder.Sql($"""
                EXEC(N'CREATE TRIGGER [{schema}].[{trigger}] ON [{schema}].[{table}]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                    THROW 51020, ''Accounting audit and posted movement history is immutable.'', 1;
                END;');
                """);

        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [system].[TR_CompanyAdditionalCurrencies_BaseCurrency]
            ON [system].[CompanyAdditionalCurrencies] AFTER INSERT, UPDATE AS
            BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted i JOIN [system].[Companies] c WITH (UPDLOCK,HOLDLOCK)
                    ON c.Id=i.CompanyId AND c.BaseCurrencyCode=i.CurrencyCode)
                    THROW 51001, ''Base currency cannot be an additional company currency.'', 1;
            END;');
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [system].[TR_Companies_BaseCurrency]
            ON [system].[Companies] AFTER UPDATE AS
            BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted i JOIN [system].[CompanyAdditionalCurrencies] a WITH (UPDLOCK,HOLDLOCK)
                    ON a.CompanyId=i.Id AND a.CurrencyCode=i.BaseCurrencyCode)
                    THROW 51001, ''Base currency cannot be an additional company currency.'', 1;
                IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id
                    WHERE i.BaseCurrencyCode<>d.BaseCurrencyCode AND (
                        EXISTS(SELECT 1 FROM ledger.JournalEntries x WHERE x.CompanyId=i.Id) OR
                        EXISTS(SELECT 1 FROM sales.Invoices x WHERE x.CompanyId=i.Id) OR
                        EXISTS(SELECT 1 FROM purchases.Invoices x WHERE x.CompanyId=i.Id) OR
                        EXISTS(SELECT 1 FROM treasury.Vouchers x WHERE x.CompanyId=i.Id) OR
                        EXISTS(SELECT 1 FROM inventory.Documents x WHERE x.CompanyId=i.Id) OR
                        EXISTS(SELECT 1 FROM inventory.StockMovements x WHERE x.CompanyId=i.Id) OR
                        EXISTS(SELECT 1 FROM inventory.ItemPrices x WHERE x.CompanyId=i.Id) OR
                        EXISTS(SELECT 1 FROM system.ExchangeRates x WHERE x.CompanyId=i.Id)))
                    THROW 51002, ''Base currency cannot change after financial activity.'', 1;
            END;');
            """);
    }
}
