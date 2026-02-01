using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mud.Infrastructure;

namespace Mud.DependencyInjection;

/// <summary>
/// Extension methods for registering Mud services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Mud services (database, identity, persistence, session management).
    /// </summary>
    public static IServiceCollection AddMudServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        InfrastructureRegistration.AddInfrastructureServices(services, configuration, isDevelopment);
        return services;
    }
}
