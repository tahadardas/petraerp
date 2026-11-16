using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Currencies;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Currencies;

/// <summary>
/// Company currencies and the exchange-rate register. No currency is seeded or assumed: the operator defines and enables
/// each currency (for example SYP, USD) and registers rates with an effective date, source and reason. Register entries
/// are append-only; documents copy the rate they used, so later entries never change history.
/// </summary>
public sealed class SqlCurrencyService(FinancialDbContext context, TimeProvider? clock = null) : ICurrencyService
{
    private TimeProvider Clock => clock ?? TimeProvider.System;

    public async Task<CompanyCurrencySettingsResult> GetSettingsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var company = await CompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var enabled = await context.CompanyAdditionalCurrencies.AsNoTracking().Where(c => c.CompanyId == companyId).Select(c => c.CurrencyCode)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        enabled.Add(company.BaseCurrencyCode);
        var catalog = await context.Currencies.AsNoTracking().Where(c => enabled.Contains(c.Code)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var lockReason = await BaseLockReasonAsync(companyId, cancellationToken).ConfigureAwait(false);
        return new CompanyCurrencySettingsResult(companyId, company.BaseCurrencyCode, lockReason is not null, lockReason,
            catalog.OrderByDescending(c => c.Code == company.BaseCurrencyCode).ThenBy(c => c.Code, StringComparer.Ordinal)
                .Select(c => new CompanyCurrencyResult(c.Code, c.NameArabic, c.NameEnglish, c.IsActive, c.Code == company.BaseCurrencyCode,
                    c.Symbol, c.MinorUnitNameArabic, c.MinorUnitNameEnglish, c.DecimalPlaces, (CurrencyRoundingMode)c.RoundingMode)).ToList());
    }

    public async Task<CompanyCurrencySettingsResult> EnableCurrencyAsync(Guid companyId, Guid userId, EnableCurrencyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var code = Code(command.Code);
        var reason = Reason(command.Reason);
        var company = await CompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        if (code == company.BaseCurrencyCode)
            throw new BusinessRuleException("currency.already_base", "This currency is already the company base currency.", "code");

        var now = Clock.GetUtcNow();
        var currency = await context.Currencies.SingleOrDefaultAsync(c => c.Code == code, cancellationToken).ConfigureAwait(false);
        if (currency is null)
        {
            var arabic = command.NameArabic?.Trim();
            if (string.IsNullOrEmpty(arabic) || arabic.Length > 200)
                throw new BusinessRuleException("currency.name_required", "اسم العملة الجديدة بالعربية مطلوب (حتى 200 محرف) / A new currency needs its Arabic name (at most 200 characters).", "nameArabic");
            context.Currencies.Add(new CurrencyRow
            {
                Code = code, NameArabic = arabic, NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim(),
                IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now,
                DecimalPlaces = (byte)(PetraERP.Application.Currencies.StandardCurrencyCatalog.Find(code)?.DecimalPlaces ?? 2),
            });
        }
        else if (!currency.IsActive)
        {
            throw new BusinessRuleException("currency.inactive", "The currency exists in the catalog but is inactive.", "code");
        }

        if (await context.CompanyAdditionalCurrencies.AnyAsync(c => c.CompanyId == companyId && c.CurrencyCode == code, cancellationToken).ConfigureAwait(false))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return await GetSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);
        }
        context.CompanyAdditionalCurrencies.Add(new CompanyAdditionalCurrencyRow { CompanyId = companyId, CurrencyCode = code, CreatedAtUtc = now, UpdatedAtUtc = now });
        context.CurrencyAudit.Add(new CurrencyAuditRow
        {
            Id = Guid.CreateVersion7(now), CompanyId = companyId, Action = CurrencyAuditRow.CurrencyEnabled, CurrencyCode = code,
            Reason = reason, ActorUserId = userId, OccurredAtUtc = now,
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompanyCurrencySettingsResult> UpdateCardAsync(Guid companyId, Guid userId, UpdateCurrencyCardCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var code = Code(command.Code);
        var reason = Reason(command.Reason);
        if (!CurrencyRounding.IsValidPlaces(command.DecimalPlaces))
            throw new BusinessRuleException("currency.decimal_places_invalid", "عدد الكسور بين 0 و4 / Decimal places must be between 0 and 4.", "decimalPlaces");
        if (!Enum.IsDefined(command.RoundingMode))
            throw new BusinessRuleException("currency.rounding_mode_invalid", "نمط التقريب غير معروف / Unknown rounding mode.", "roundingMode");
        var symbol = Trimmed(command.Symbol, 8, "symbol");
        var minorAr = Trimmed(command.MinorUnitNameArabic, 100, "minorUnitNameArabic");
        var minorEn = Trimmed(command.MinorUnitNameEnglish, 100, "minorUnitNameEnglish");

        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var company = await CompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        if (code != company.BaseCurrencyCode)
            await CompanyCurrencies.RequireEnabledAsync(context, companyId, company.BaseCurrencyCode, code, "code", cancellationToken).ConfigureAwait(false);
        var currency = await context.Currencies.SingleOrDefaultAsync(c => c.Code == code, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Currency");

        var before = $"symbol={currency.Symbol ?? "-"}; minorAr={currency.MinorUnitNameArabic ?? "-"}; minorEn={currency.MinorUnitNameEnglish ?? "-"}; places={currency.DecimalPlaces}; rounding={(CurrencyRoundingMode)currency.RoundingMode}";
        var after = $"symbol={symbol ?? "-"}; minorAr={minorAr ?? "-"}; minorEn={minorEn ?? "-"}; places={command.DecimalPlaces}; rounding={command.RoundingMode}";
        if (before == after)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return await GetSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);
        }
        var now = Clock.GetUtcNow();
        currency.Symbol = symbol;
        currency.MinorUnitNameArabic = minorAr;
        currency.MinorUnitNameEnglish = minorEn;
        currency.DecimalPlaces = (byte)command.DecimalPlaces;
        currency.RoundingMode = (byte)command.RoundingMode;
        currency.UpdatedAtUtc = now;
        context.CurrencyAudit.Add(new CurrencyAuditRow
        {
            Id = Guid.CreateVersion7(now), CompanyId = companyId, Action = CurrencyAuditRow.CurrencyCardChanged, CurrencyCode = code,
            Reason = reason, Details = $"{before} => {after}", ActorUserId = userId, OccurredAtUtc = now,
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);
    }

    private static string? Trimmed(string? value, int max, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > max)
            throw new BusinessRuleException("currency.card_text_too_long", $"الحد الأقصى {max} محرفاً / At most {max} characters.", field);
        return text;
    }

    public async Task<CompanyCurrencySettingsResult> ChangeBaseCurrencyAsync(Guid companyId, Guid userId, ChangeBaseCurrencyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var code = Code(command.Code);
        var reason = Reason(command.Reason);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // Lock the company row so no document can be created between the emptiness check and the change.
            var company = await context.Companies.FromSqlInterpolated($"SELECT * FROM [system].[Companies] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {companyId}")
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("Company");
            if (company.BaseCurrencyCode == code)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return await GetSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);
            }
            if (await BaseLockReasonAsync(companyId, cancellationToken).ConfigureAwait(false) is { } lockReason)
                throw new BusinessRuleException(DocumentCurrencyPolicy.BaseLocked,
                    $"العملة الأساسية مقفلة لوجود بيانات؛ يلزم إجراء تحويل محاسبي معتمد / The base currency is locked ({lockReason}); changing it needs an approved conversion procedure, which is not available.", "code");
            if (!await context.Currencies.AnyAsync(c => c.Code == code && c.IsActive, cancellationToken).ConfigureAwait(false)
                || !await context.CompanyAdditionalCurrencies.AnyAsync(c => c.CompanyId == companyId && c.CurrencyCode == code, cancellationToken).ConfigureAwait(false))
                throw new BusinessRuleException("currency.not_defined", "Define and enable the currency before making it the base currency.", "code");

            var now = Clock.GetUtcNow();
            var previous = company.BaseCurrencyCode;
            // The new base cannot also be an additional currency (database trigger); the previous base stays usable as one.
            var additional = await context.CompanyAdditionalCurrencies.SingleOrDefaultAsync(c => c.CompanyId == companyId && c.CurrencyCode == code, cancellationToken)
                .ConfigureAwait(false);
            if (additional is not null)
            {
                context.CompanyAdditionalCurrencies.Remove(additional);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            company.BaseCurrencyCode = code;
            company.UpdatedAtUtc = now;
            // Saved before the previous base is re-added as an additional currency, so the trigger never sees it as the base.
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.CompanyAdditionalCurrencies.Add(new CompanyAdditionalCurrencyRow { CompanyId = companyId, CurrencyCode = previous, CreatedAtUtc = now, UpdatedAtUtc = now });
            context.CurrencyAudit.Add(new CurrencyAuditRow
            {
                Id = Guid.CreateVersion7(now), CompanyId = companyId, Action = CurrencyAuditRow.BaseCurrencyChanged, CurrencyCode = code,
                PreviousCurrencyCode = previous, Reason = reason, ActorUserId = userId, OccurredAtUtc = now,
            });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        context.ChangeTracker.Clear();
        return await GetSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExchangeRatePageResult> ListRatesAsync(Guid companyId, ExchangeRateQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await CompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var page = Math.Clamp(query.Page, 1, 1_000_000);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var rows = context.ExchangeRates.AsNoTracking().Where(r => r.CompanyId == companyId);
        if (DocumentCurrencyPolicy.Normalize(query.CurrencyCode) is { } currency) rows = rows.Where(r => r.CurrencyCode == currency);
        if (query.From is { } from) rows = rows.Where(r => r.EffectiveDate >= from);
        if (query.To is { } to) rows = rows.Where(r => r.EffectiveDate <= to);
        var total = await rows.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await rows.OrderByDescending(r => r.EffectiveDate).ThenByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ExchangeRatePageResult(items.Select(ToResult).ToList(), page, size, total);
    }

    public async Task<ExchangeRateResult> RegisterRateAsync(Guid companyId, Guid userId, RegisterExchangeRateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var code = Code(command.CurrencyCode);
        var reason = Reason(command.Reason);
        var source = command.Source?.Trim();
        if (command.EffectiveDate == default)
            throw new BusinessRuleException("currency.query_invalid", "تاريخ سريان السعر مطلوب / An effective rate date is required.", "effectiveDate");
        if (string.IsNullOrEmpty(source) || source.Length > 64)
            throw new BusinessRuleException("currency.rate_source_required", "The rate source is required (at most 64 characters), for example a bulletin or bank.", "source");
        try { DocumentCurrencyPolicy.RequireRate(command.Rate); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } ruleCode)
        {
            throw new BusinessRuleException(ruleCode, exception.Message, "rate");
        }
        var company = await CompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        if (code == company.BaseCurrencyCode)
            throw new BusinessRuleException(DocumentCurrencyPolicy.RateInvalid, "The base currency has no register rate; it is always 1.", "currencyCode");
        await CompanyCurrencies.RequireEnabledAsync(context, companyId, company.BaseCurrencyCode, code, "currency", cancellationToken).ConfigureAwait(false);

        var now = Clock.GetUtcNow();
        var row = new ExchangeRateRow
        {
            Id = Guid.CreateVersion7(now), CompanyId = companyId, CurrencyCode = code, BaseCurrencyCode = company.BaseCurrencyCode, Rate = command.Rate,
            EffectiveDate = command.EffectiveDate, Source = source, Reason = reason, CreatedByUserId = userId, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        context.ExchangeRates.Add(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToResult(row);
    }

    public async Task<IReadOnlyList<ExchangeRateResult>> RegisterRateBulletinAsync(Guid companyId, Guid userId, RegisterRateBulletinCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = Reason(command.Reason);
        var source = command.Source?.Trim();
        if (command.EffectiveDate == default)
            throw new BusinessRuleException("currency.query_invalid", "تاريخ سريان النشرة مطلوب / A bulletin date is required.", "effectiveDate");
        if (string.IsNullOrEmpty(source) || source.Length > 64)
            throw new BusinessRuleException("currency.rate_source_required", "The rate source is required (at most 64 characters), for example a bulletin or bank.", "source");
        if (command.Rates is null || command.Rates.Count == 0 || command.Rates.Count > 50)
            throw new BusinessRuleException("currency.bulletin_size", "النشرة تحتوي من 1 إلى 50 عملة / A bulletin holds 1 to 50 currencies.", "rates");

        var lines = new List<(string Code, decimal Rate)>();
        foreach (var line in command.Rates)
        {
            var code = Code(line.CurrencyCode);
            try { DocumentCurrencyPolicy.RequireRate(line.Rate); }
            catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } ruleCode)
            {
                throw new BusinessRuleException(ruleCode, exception.Message, "rate");
            }
            if (lines.Any(l => l.Code == code))
                throw new BusinessRuleException("currency.bulletin_duplicate", $"العملة {code} مكررة في النشرة / Currency {code} appears twice in the bulletin.", "rates");
            lines.Add((code, line.Rate));
        }

        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var company = await CompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var now = Clock.GetUtcNow();
        var rows = new List<ExchangeRateRow>();
        foreach (var (code, rate) in lines)
        {
            if (code == company.BaseCurrencyCode)
                throw new BusinessRuleException(DocumentCurrencyPolicy.RateInvalid, "The base currency has no register rate; it is always 1.", "rates");
            await CompanyCurrencies.RequireEnabledAsync(context, companyId, company.BaseCurrencyCode, code, "rates", cancellationToken).ConfigureAwait(false);
            rows.Add(new ExchangeRateRow
            {
                Id = Guid.CreateVersion7(now), CompanyId = companyId, CurrencyCode = code, BaseCurrencyCode = company.BaseCurrencyCode, Rate = rate,
                EffectiveDate = command.EffectiveDate, Source = source, Reason = reason, CreatedByUserId = userId, CreatedAtUtc = now, UpdatedAtUtc = now,
            });
        }
        context.ExchangeRates.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToResult).ToList();
    }

    public async Task<EffectiveExchangeRateResult> GetEffectiveRateAsync(Guid companyId, string currencyCode, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var code = Code(currencyCode);
        var company = await CompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        if (code == company.BaseCurrencyCode)
            return new EffectiveExchangeRateResult(code, company.BaseCurrencyCode, asOfDate, true, 1m, null, null, null);
        var row = await CompanyCurrencies.EffectiveRateAsync(context, companyId, company.BaseCurrencyCode, code, asOfDate, cancellationToken).ConfigureAwait(false);
        return row is null
            ? new EffectiveExchangeRateResult(code, company.BaseCurrencyCode, asOfDate, false, null, null, null, null)
            : new EffectiveExchangeRateResult(code, company.BaseCurrencyCode, asOfDate, true, row.Rate, row.Id, row.EffectiveDate, row.Source);
    }

    /// <summary>Why the base currency can no longer change, or null while the company has no accounting or commercial data.</summary>
    private async Task<string?> BaseLockReasonAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (await context.JournalEntries.AnyAsync(j => j.CompanyId == companyId, cancellationToken).ConfigureAwait(false)) return "journals exist";
        if (await context.SalesInvoices.AnyAsync(i => i.CompanyId == companyId, cancellationToken).ConfigureAwait(false)) return "sales invoices exist";
        if (await context.PurchaseInvoices.AnyAsync(i => i.CompanyId == companyId, cancellationToken).ConfigureAwait(false)) return "purchase invoices exist";
        if (await context.Vouchers.AnyAsync(v => v.CompanyId == companyId, cancellationToken).ConfigureAwait(false)) return "vouchers exist";
        if (await context.InventoryDocuments.AnyAsync(d => d.CompanyId == companyId, cancellationToken).ConfigureAwait(false)) return "inventory documents exist";
        if (await context.StockMovements.AnyAsync(m => m.CompanyId == companyId, cancellationToken).ConfigureAwait(false)) return "stock movements exist";
        if (await context.ExchangeRates.AnyAsync(r => r.CompanyId == companyId, cancellationToken).ConfigureAwait(false)) return "exchange rates are registered";
        if (await context.ItemPrices.AnyAsync(p => p.CompanyId == companyId, cancellationToken).ConfigureAwait(false)) return "item prices exist";
        return null;
    }

    private async Task<CompanyRow> CompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        await context.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken).ConfigureAwait(false)
        ?? throw new ResourceNotFoundException("Company");

    private static string Code(string? code)
    {
        try
        {
            return DocumentCurrencyPolicy.Normalize(code) ?? throw RuleViolation.Create(DocumentCurrencyPolicy.CurrencyInvalid, "A currency code is required.");
        }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } ruleCode)
        {
            throw new BusinessRuleException(ruleCode, exception.Message, "code");
        }
    }

    private static string Reason(string? reason)
    {
        var value = reason?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 500)
            throw new BusinessRuleException("currency.reason_required", "A reason of at most 500 characters is required.", "reason");
        return value;
    }

    private static ExchangeRateResult ToResult(ExchangeRateRow r) =>
        new(r.Id, r.CurrencyCode, r.BaseCurrencyCode, r.Rate, r.EffectiveDate, r.Source, r.Reason, r.CreatedByUserId, r.CreatedAtUtc);
}
