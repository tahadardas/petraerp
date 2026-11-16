using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PetraERP.Infrastructure.Persistence;

public sealed class AccountingDesignTimeContextFactory : IDesignTimeDbContextFactory<AccountingDbContext>
{
    public AccountingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(sql => sql.UseCompatibilityLevel(160)).Options);
}
