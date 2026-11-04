using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qraft.Application;

namespace Qraft.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddQraftPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var path = Path.GetFullPath(configuration["Data:DatabasePath"] ?? "data/qraft.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        services.AddDbContext<QualityDbContext>(options => options.UseSqlite(
            $"Data Source={path};Default Timeout=30;Foreign Keys=True"));
        services.AddScoped<IQualityStore, EfQualityStore>();
        return services;
    }

    public static async Task InitializeQraftDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<QualityDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }
}
