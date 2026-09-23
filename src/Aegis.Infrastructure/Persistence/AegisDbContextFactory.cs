namespace Aegis.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class AegisDbContextFactory : IDesignTimeDbContextFactory<AegisDbContext>
{
    public AegisDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AegisDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=aegis;Username=aegis;Password=aegis_dev_password")
            .Options;
        return new AegisDbContext(options);
    }
}
