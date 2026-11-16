namespace PetraERP.Domain.Sales;

public sealed class SalesInvoice
{
    private readonly List<SalesInvoiceLine> lines = [];

    private SalesInvoice() { }

    public SalesInvoice(
        Guid id,
        Guid companyId,
        Guid branchId,
        Guid patternRevisionId,
        Guid customerId,
        Guid warehouseId,
        DateTimeOffset invoiceDateUtc,
        PaymentMethod paymentMethod = PaymentMethod.Credit,
        string currencyCode = "SYP",
        decimal exchangeRate = 1.0m,
        Guid? costCenterId = null,
        string? notes = null,
        Guid? createdByUserId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invoice ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID cannot be empty.", nameof(branchId));
        if (patternRevisionId == Guid.Empty) throw new ArgumentException("Pattern revision ID cannot be empty.", nameof(patternRevisionId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer ID cannot be empty.", nameof(customerId));
        if (warehouseId == Guid.Empty) throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        if (string.IsNullOrWhiteSpace(currencyCode)) throw new ArgumentException("Currency code cannot be empty.", nameof(currencyCode));
        if (exchangeRate <= 0m) throw new ArgumentOutOfRangeException(nameof(exchangeRate), "Exchange rate must be positive.");

        Id = id;
        CompanyId = companyId;
        BranchId = branchId;
        PatternRevisionId = patternRevisionId;
        CustomerId = customerId;
        WarehouseId = warehouseId;
        InvoiceDateUtc = invoiceDateUtc;
        PaymentMethod = paymentMethod;
        CurrencyCode = currencyCode!.Trim().ToUpperInvariant();
        ExchangeRate = exchangeRate;
        CostCenterId = costCenterId;
        Notes = notes?.Trim();
        Status = SalesInvoiceStatus.Draft;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid PatternRevisionId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public DateTimeOffset InvoiceDateUtc { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ExchangeRate { get; private set; } = 1.0m;
    public SalesInvoiceStatus Status { get; private set; } = SalesInvoiceStatus.Draft;
    public Guid? CostCenterId { get; private set; }
    public string? Notes { get; private set; }

    public decimal SubTotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal AdditionsTotal { get; private set; }
    public decimal GrandTotal { get; private set; }

    public Guid? JournalEntryId { get; private set; }
    public Guid? ReversedByInvoiceId { get; private set; }
    public Guid? ReversesInvoiceId { get; private set; }

    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public Guid? PostedByUserId { get; private set; }
    public DateTimeOffset? PostedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<SalesInvoiceLine> Lines => lines.AsReadOnly();

    public void AddLine(SalesInvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureCanModify();
        line.SalesInvoiceId = Id;
        lines.Add(line);
        RecalculateTotals();
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureCanModify();
        var line = lines.FirstOrDefault(l => l.Id == lineId);
        if (line is not null)
        {
            lines.Remove(line);
            RecalculateTotals();
        }
    }

    public void ClearLines()
    {
        EnsureCanModify();
        lines.Clear();
        RecalculateTotals();
    }

    public void UpdateHeader(
        Guid customerId,
        Guid warehouseId,
        DateTimeOffset invoiceDateUtc,
        PaymentMethod paymentMethod,
        string currencyCode,
        decimal exchangeRate,
        Guid? costCenterId,
        string? notes)
    {
        EnsureCanModify();
        if (customerId == Guid.Empty) throw new ArgumentException("Customer ID cannot be empty.", nameof(customerId));
        if (warehouseId == Guid.Empty) throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        if (string.IsNullOrWhiteSpace(currencyCode)) throw new ArgumentException("Currency code cannot be empty.", nameof(currencyCode));
        if (exchangeRate <= 0m) throw new ArgumentOutOfRangeException(nameof(exchangeRate), "Exchange rate must be positive.");

        CustomerId = customerId;
        WarehouseId = warehouseId;
        InvoiceDateUtc = invoiceDateUtc;
        PaymentMethod = paymentMethod;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        ExchangeRate = exchangeRate;
        CostCenterId = costCenterId;
        Notes = notes?.Trim();
    }

    public void RecalculateTotals()
    {
        decimal subTotal = 0m;
        decimal discountTotal = 0m;
        decimal taxTotal = 0m;
        decimal additionsTotal = 0m;

        foreach (var line in lines)
        {
            var baseAmount = Math.Round(line.Quantity * line.UnitPrice, 4, MidpointRounding.AwayFromZero);
            subTotal += baseAmount;
            discountTotal += line.DiscountAmount;
            taxTotal += line.TaxAmount;
            additionsTotal += (line.PumpCost + line.TransportCost);
        }

        SubTotal = subTotal;
        DiscountTotal = discountTotal;
        TaxTotal = taxTotal;
        AdditionsTotal = additionsTotal;
        GrandTotal = (subTotal - discountTotal) + taxTotal + additionsTotal;
    }

    public void Approve(Guid approvedByUserId)
    {
        if (Status != SalesInvoiceStatus.Draft)
        {
            throw new InvalidOperationException($"Cannot approve invoice in state '{Status}'. Only Draft invoices can be approved.");
        }
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Cannot approve an invoice with no lines.");
        }

        Status = SalesInvoiceStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Post(string invoiceNumber, Guid journalEntryId, Guid postedByUserId)
    {
        if (Status != SalesInvoiceStatus.Draft && Status != SalesInvoiceStatus.Approved)
        {
            throw new InvalidOperationException($"Cannot post invoice in state '{Status}'. Only Draft or Approved invoices can be posted.");
        }
        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            throw new ArgumentException("Invoice number cannot be empty when posting.", nameof(invoiceNumber));
        }
        if (journalEntryId == Guid.Empty)
        {
            throw new ArgumentException("Journal entry ID cannot be empty when posting.", nameof(journalEntryId));
        }
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Cannot post an invoice with no lines.");
        }

        InvoiceNumber = invoiceNumber;
        JournalEntryId = journalEntryId;
        Status = SalesInvoiceStatus.Posted;
        PostedByUserId = postedByUserId;
        PostedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkReversed(Guid reversalInvoiceId)
    {
        if (Status != SalesInvoiceStatus.Posted)
        {
            throw new InvalidOperationException($"Cannot reverse invoice in state '{Status}'. Only Posted invoices can be reversed.");
        }

        Status = SalesInvoiceStatus.Reversed;
        ReversedByInvoiceId = reversalInvoiceId;
    }

    public void LinkReverses(Guid originalInvoiceId)
    {
        ReversesInvoiceId = originalInvoiceId;
    }

    private void EnsureCanModify()
    {
        if (Status != SalesInvoiceStatus.Draft)
        {
            throw new InvalidOperationException($"Invoice cannot be modified because its current status is '{Status}'.");
        }
    }
}
