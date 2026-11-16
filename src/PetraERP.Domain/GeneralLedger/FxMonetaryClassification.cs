namespace PetraERP.Domain.GeneralLedger;

/// <summary>
/// Authoritative classification for FX revaluation eligibility.
/// Prevents improper revaluation of revenues, expenses, or nonmonetary assets.
/// </summary>
public enum FxMonetaryClassification : byte
{
    /// <summary>Monetary balance (foreign cash, bank, receivables, payables) revalued at closing rates.</summary>
    MonetaryEligible = 1,

    /// <summary>Nonmonetary item (revenues, expenses, inventory, fixed assets) carried at historical cost.</summary>
    NonMonetary = 2,

    /// <summary>Internal, memorandum or statistical accounts not subject to currency revaluation.</summary>
    NotApplicable = 3,

    /// <summary>Default state for ambiguous accounts; requires explicit accountant review before FX revaluation.</summary>
    RequiresReview = 4
}
