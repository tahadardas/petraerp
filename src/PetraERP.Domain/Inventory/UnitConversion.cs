using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Inventory;

public sealed class UnitConversion
{
    private UnitConversion() { }

    public UnitConversion(
        Guid id,
        Guid companyId,
        Guid fromUnitId,
        Guid toUnitId,
        decimal factor,
        Guid? itemId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Conversion ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        if (fromUnitId == Guid.Empty) throw new ArgumentException("FromUnit ID cannot be empty.", nameof(fromUnitId));
        if (toUnitId == Guid.Empty) throw new ArgumentException("ToUnit ID cannot be empty.", nameof(toUnitId));
        if (fromUnitId == toUnitId)
            throw RuleViolation.Create(RuleViolation.UnitConversionInvalid, "FromUnit and ToUnit cannot be the same unit.");
        if (factor <= 0)
            throw RuleViolation.Create(RuleViolation.UnitConversionInvalid, "Conversion factor must be greater than zero.");

        Id = id;
        CompanyId = companyId;
        ItemId = itemId;
        FromUnitId = fromUnitId;
        ToUnitId = toUnitId;
        Factor = factor;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid FromUnitId { get; private set; }
    public Guid ToUnitId { get; private set; }
    public decimal Factor { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void UpdateFactor(decimal factor)
    {
        if (factor <= 0)
            throw RuleViolation.Create(RuleViolation.UnitConversionInvalid, "Conversion factor must be greater than zero.");
        Factor = factor;
    }
}
