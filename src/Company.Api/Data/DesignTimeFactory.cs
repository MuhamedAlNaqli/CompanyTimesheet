using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Company.Api.Data;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>: the migrations are written for PostgreSQL, the production database, and
/// the tool must not start the application (which would connect to whatever database is configured).
/// </summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CompanyDbContext>
{
    public CompanyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CompanyDbContext>().UseNpgsql("Host=localhost;Database=design_time_only").Options);
}
