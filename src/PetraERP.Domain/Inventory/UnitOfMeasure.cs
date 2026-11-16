using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Inventory;

public sealed class UnitOfMeasure
{
    private UnitOfMeasure() { }

    public UnitOfMeasure(
        Guid id,
        Guid companyId,
        EntityCode code,
        BilingualName name,
        string symbol,
        int decimalPlaces = 0,
        bool isActive = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("Unit ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("Unit symbol cannot be empty.", nameof(symbol));
        if (decimalPlaces is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(decimalPlaces), "Decimal places must be between 0 and 4.");

        Id = id;
        CompanyId = companyId;
        Code = code;
        Name = name;
        Symbol = symbol.Trim();
        DecimalPlaces = decimalPlaces;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public EntityCode Code { get; private set; } = null!;
    public BilingualName Name { get; private set; } = null!;
    public string Symbol { get; private set; } = string.Empty;
    public int DecimalPlaces { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(BilingualName name, string symbol, int decimalPlaces)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("Unit symbol cannot be empty.", nameof(symbol));
        if (decimalPlaces is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(decimalPlaces), "Decimal places must be between 0 and 4.");

        Name = name;
        Symbol = symbol.Trim();
        DecimalPlaces = decimalPlaces;
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
