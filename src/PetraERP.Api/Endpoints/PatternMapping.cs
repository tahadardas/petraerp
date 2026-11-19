using PetraERP.Application.Patterns;
using PetraERP.Contracts.Patterns;
using DomainPattern = PetraERP.Domain.Patterns;

namespace PetraERP.Api.Endpoints;

internal static class PatternMapping
{
    public static DocumentPatternSummaryResponse ToResponse(this DocumentPatternSummaryDto dto) =>
        new()
        {
            Id = dto.Id,
            OperationKind = (BusinessOperationKind)dto.OperationKind,
            Code = dto.Code,
            NameArabic = dto.NameArabic,
            NameEnglish = dto.NameEnglish,
            ShortCode = dto.ShortCode,
            Icon = dto.Icon,
            Group = dto.Group,
            IsActive = dto.IsActive,
            IsDefault = dto.IsDefault,
            IsRunnable = dto.IsRunnable,
            ActiveRevisionNumber = dto.ActiveRevisionNumber,
            Version = dto.Version.ToBase64(),
        };

    public static DocumentPatternResponse ToResponse(this DocumentPatternDetailsDto dto) =>
        new()
        {
            Id = dto.Id,
            CompanyId = dto.CompanyId,
            OperationKind = (BusinessOperationKind)dto.OperationKind,
            Code = dto.Code,
            NameArabic = dto.NameArabic,
            NameEnglish = dto.NameEnglish,
            ShortCode = dto.ShortCode,
            Icon = dto.Icon,
            Group = dto.Group,
            IsActive = dto.IsActive,
            IsDefault = dto.IsDefault,
            IsRunnable = dto.IsRunnable,
            Version = dto.Version.ToBase64(),
            BranchIds = dto.BranchIds,
            ActiveRevision = dto.ActiveRevision?.ToResponse(),
            Revisions = dto.Revisions.Select(ToResponse).ToList(),
        };

    public static PatternRevisionResponse ToResponse(this PatternRevisionDto dto) =>
        new()
        {
            Id = dto.Id,
            PatternId = dto.PatternId,
            RevisionNumber = dto.RevisionNumber,
            State = (PatternRevisionState)dto.State,
            EffectiveFromUtc = dto.EffectiveFromUtc,
            EffectiveToUtc = dto.EffectiveToUtc,
            Configuration = dto.Configuration.ToDto(),
            ActorUserId = dto.CreatedByUserId,
            Reason = dto.Reason,
            CreatedAtUtc = dto.CreatedAtUtc,
        };

    public static PatternConfigurationDto ToDto(this DomainPattern.PatternConfiguration config) =>
        new()
        {
            SchemaVersion = config.SchemaVersion,
            Voucher = config.Voucher is null ? null : new PatternVoucherPolicyDto
            {
                FormKind = (VoucherFormKind)config.Voucher.FormKind,
                CaptionMode = (VoucherCaptionMode)config.Voucher.CaptionMode,
                Lookup = new PatternLookupPolicyDto
                {
                    EligibleAccountRootIds = [.. config.Voucher.Lookup?.EligibleAccountRootIds ?? []],
                    EligibleCostCenterIds = [.. config.Voucher.Lookup?.EligibleCostCenterIds ?? []],
                },
                DenyMutationsOnOrBefore = config.Voucher.DenyMutationsOnOrBefore,
                PostingMode = (VoucherPostingMode)config.Voucher.PostingMode,
                PerLineJournal = config.Voucher.PerLineJournal,
                AutoPost = config.Voucher.AutoPost,
            },
            DefaultWarehouseId = config.DefaultWarehouseId,
            DefaultCurrencyCode = config.DefaultCurrencyCode,
            DefaultCostCenterId = config.DefaultCostCenterId,
            AccountMappings = config.AccountMappings.Select(static m => new PatternAccountMappingDto
            {
                Role = m.Role,
                AccountId = m.AccountId,
                IsRequired = m.IsRequired,
                AllowOverride = m.AllowOverride,
            }).ToList(),
            AllowedPaymentMethods = config.AllowedPaymentMethods,
            AllowedWarehouseIds = config.AllowedWarehouseIds,
            LineFields = config.LineFields.Select(static f => new PatternFieldRuleDto
            {
                FieldName = f.FieldName,
                CaptionArabic = f.CaptionArabic,
                IsVisible = f.IsVisible,
                IsRequired = f.IsRequired,
                DisplayOrder = f.DisplayOrder,
            }).ToList(),
            OptionOverridesWhitelist = config.OptionOverridesWhitelist,
            NumberingPolicy = new PatternNumberingPolicyDto
            {
                InitialSequence = config.NumberingPolicy.InitialSequence,
                ResetPolicy = (PatternNumberResetPolicy)config.NumberingPolicy.ResetPolicy,
                Prefix = config.NumberingPolicy.Prefix,
                Suffix = config.NumberingPolicy.Suffix,
                MinimumDigits = config.NumberingPolicy.MinimumDigits,
                Scope = (PatternNumberingScope)config.NumberingPolicy.Scope,
                AllowManualNumbering = config.NumberingPolicy.AllowManualNumbering,
            },
            ApprovalPolicy = new PatternApprovalPolicyDto
            {
                RequiresApproval = config.ApprovalPolicy.RequiresApproval,
                ThresholdAmount = config.ApprovalPolicy.ThresholdAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            EntryPolicy = new PatternEntryPolicyDto { LockDocumentDate = config.EntryPolicy.LockDocumentDate },
            PricingPolicy = new PatternPricingPolicyDto
            {
                AllowManualPriceOverride = config.PricingPolicy.AllowManualPriceOverride,
                MaxDiscountPercentage = config.PricingPolicy.MaxDiscountPercentage?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RequirePriceQuote = config.PricingPolicy.RequirePriceQuote,
            },
        };

    public static DomainPattern.PatternConfiguration ToDomain(this PatternConfigurationDto dto) =>
        new()
        {
            SchemaVersion = dto.SchemaVersion,
            Voucher = dto.Voucher is null ? null : new DomainPattern.PatternVoucherPolicy
            {
                FormKind = (DomainPattern.VoucherFormKind)dto.Voucher.FormKind,
                CaptionMode = (DomainPattern.VoucherCaptionMode)dto.Voucher.CaptionMode,
                Lookup = new DomainPattern.PatternLookupPolicy
                {
                    EligibleAccountRootIds = [.. dto.Voucher.Lookup?.EligibleAccountRootIds ?? []],
                    EligibleCostCenterIds = [.. dto.Voucher.Lookup?.EligibleCostCenterIds ?? []],
                },
                DenyMutationsOnOrBefore = dto.Voucher.DenyMutationsOnOrBefore,
                PostingMode = (DomainPattern.VoucherPostingMode)dto.Voucher.PostingMode,
                PerLineJournal = dto.Voucher.PerLineJournal,
                AutoPost = dto.Voucher.AutoPost,
            },
            DefaultWarehouseId = dto.DefaultWarehouseId,
            DefaultCurrencyCode = dto.DefaultCurrencyCode,
            DefaultCostCenterId = dto.DefaultCostCenterId,
            AccountMappings = dto.AccountMappings.Select(static m => new DomainPattern.PatternAccountMapping
            {
                Role = m.Role,
                AccountId = m.AccountId,
                IsRequired = m.IsRequired,
                AllowOverride = m.AllowOverride,
            }).ToList(),
            AllowedPaymentMethods = dto.AllowedPaymentMethods,
            AllowedWarehouseIds = dto.AllowedWarehouseIds ?? [],
            LineFields = dto.LineFields.Select(static f => new DomainPattern.PatternFieldRule
            {
                FieldName = f.FieldName,
                CaptionArabic = (f.CaptionArabic ?? string.Empty).Trim(),
                IsVisible = f.IsVisible,
                IsRequired = f.IsRequired,
                DisplayOrder = f.DisplayOrder,
            }).ToList(),
            OptionOverridesWhitelist = dto.OptionOverridesWhitelist,
            NumberingPolicy = new DomainPattern.PatternNumberingPolicy
            {
                InitialSequence = dto.NumberingPolicy.InitialSequence,
                ResetPolicy = (DomainPattern.PatternNumberResetPolicy)dto.NumberingPolicy.ResetPolicy,
                Prefix = dto.NumberingPolicy.Prefix,
                Suffix = dto.NumberingPolicy.Suffix,
                MinimumDigits = dto.NumberingPolicy.MinimumDigits,
                Scope = (DomainPattern.PatternNumberingScope)dto.NumberingPolicy.Scope,
                AllowManualNumbering = dto.NumberingPolicy.AllowManualNumbering,
            },
            ApprovalPolicy = new DomainPattern.PatternApprovalPolicy
            {
                RequiresApproval = dto.ApprovalPolicy.RequiresApproval,
                ThresholdAmount = decimal.TryParse(dto.ApprovalPolicy.ThresholdAmount, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null,
            },
            EntryPolicy = new DomainPattern.PatternEntryPolicy { LockDocumentDate = dto.EntryPolicy?.LockDocumentDate ?? false },
            PricingPolicy = dto.PricingPolicy is null ? new() : new DomainPattern.PatternPricingPolicy
            {
                AllowManualPriceOverride = dto.PricingPolicy.AllowManualPriceOverride,
                MaxDiscountPercentage = decimal.TryParse(dto.PricingPolicy.MaxDiscountPercentage, System.Globalization.CultureInfo.InvariantCulture, out var md) ? md : null,
                RequirePriceQuote = dto.PricingPolicy.RequirePriceQuote,
            },
        };
}
