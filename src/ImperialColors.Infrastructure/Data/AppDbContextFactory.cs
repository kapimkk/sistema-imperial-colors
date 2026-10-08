using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ImperialColors.Infrastructure.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        // Geração/teste de migration pode usar conexão explicitamente isolada sem carregar .env.
        var isolada = Environment.GetEnvironmentVariable("IMPERIAL_DESIGN_TIME_CONNECTION_STRING");
        optionsBuilder.UseNpgsql(string.IsNullOrWhiteSpace(isolada)
            ? DesignTimeConnectionHelper.ObterConnectionString()
            : isolada);
        return new AppDbContext(optionsBuilder.Options);
    }
}
