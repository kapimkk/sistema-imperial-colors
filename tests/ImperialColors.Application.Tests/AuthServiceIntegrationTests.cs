using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Security;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>Nunca lê .env ou credenciais reais; usa usuário fictício no banco local explicitamente autorizado.</summary>
public class AuthServiceIntegrationTests
{
    [Fact]
    public async Task LoginAsync_NoBancoLocalIsolado_CredenciaisFicticiasFuncionam()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var cs)) return;
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(cs);
        services.AddApplication();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        const string password = "Imperial-Local-Test-2026!";
        var (hash,salt) = PasswordHasher.HashPassword(password);
        var usuario = new Usuario
        {
            Username = "local_test_" + Guid.NewGuid().ToString("N"),
            NomeCompleto = "Usuário fictício local", Email = $"local-{Guid.NewGuid():N}@example.test",
            SenhaHash = hash, Salt = salt, Status = StatusUsuario.Aprovado, Permissao = PermissaoUsuario.Admin
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();
        try
        {
            var auth = provider.GetRequiredService<IAuthService>();
            var sessao = await auth.LoginAsync(new LoginDto {Username=usuario.Username,Senha=password});
            Assert.Equal(usuario.Id,sessao.Id);
            Assert.Equal(usuario.Username,sessao.Username);
        }
        finally
        {
            context.Usuarios.Remove(usuario);
            await context.SaveChangesAsync();
        }
    }
}