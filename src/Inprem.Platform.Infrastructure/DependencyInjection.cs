using Inprem.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inprem.Platform.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the platform's infrastructure: <see cref="PlatformDbContext"/> for now.</summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PlatformDbContext>(options => PlatformDbContext.Configure(options, connectionString));
        return services;
    }
}
