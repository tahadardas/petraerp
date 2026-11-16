using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PetraERP.Infrastructure.Persistence;

/// <summary>Builds migration metadata without a connection or automatic database changes.</summary>
public sealed class PetraDesignTimeContextFactory : IDesignTimeDbContextFactory<PetraDbContext>
{
    public PetraDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PetraDbContext>()
            .UseSqlServer(static sql => sql.UseCompatibilityLevel(160)).Options);
}
