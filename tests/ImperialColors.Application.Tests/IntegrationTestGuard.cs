using Npgsql;

namespace ImperialColors.Application.Tests;

/// <summary>Integrações exigem opt-in e conexão local explícita. Nunca carrega o .env da loja.</summary>
internal static class IntegrationTestGuard
{
    public static bool TryObterConnectionString(out string connectionString)
    {
        connectionString = string.Empty;
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            return false;
        var valor = Environment.GetEnvironmentVariable("IMPERIAL_TEST_DATABASE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(valor))
            return false;
        var builder = new NpgsqlConnectionStringBuilder(valor);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1")
            || builder.Database is null || !builder.Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Integração exige host loopback e banco com sufixo _test.");
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        connectionString = builder.ConnectionString;
        return true;
    }
}