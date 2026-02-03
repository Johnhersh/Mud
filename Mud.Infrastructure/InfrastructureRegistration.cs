using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mud.Core.Models;
using Mud.Core.Services;
using Mud.Infrastructure.Data;
using Mud.Infrastructure.Services;

namespace Mud.Infrastructure;

/// <summary>
/// Registers infrastructure services (DbContext, Identity, Persistence).
/// </summary>
public static class InfrastructureRegistration
{
    public static void AddInfrastructureServices(
        IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        // Database context
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<MudDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Identity
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            // Password settings
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 4;

            // User settings
            options.User.RequireUniqueEmail = false;

            // SignIn settings
            options.SignIn.RequireConfirmedAccount = false;
        })
        .AddEntityFrameworkStores<MudDbContext>()
        .AddDefaultTokenProviders();

        // Cookie configuration
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.LoginPath = "/Login";
            options.LogoutPath = "/Logout";
            options.AccessDeniedPath = "/Login";
            options.SlidingExpiration = true;
        });

        // Memory cache for character progression
        services.AddMemoryCache();

        // Persistence service (scoped, tied to DbContext lifetime)
        services.AddScoped<IPersistenceService, PersistenceService>();

        // Character cache (scoped, uses DbContext for cache misses)
        services.AddScoped<ICharacterCache, CharacterCache>();
    }
}
