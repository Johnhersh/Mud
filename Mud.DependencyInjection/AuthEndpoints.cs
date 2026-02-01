using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Mud.Core;
using Mud.Core.Services;
using Mud.Infrastructure.Data;

namespace Mud.DependencyInjection;

/// <summary>
/// Auth API endpoints for login/register.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (
            HttpContext context,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) =>
        {
            var form = await context.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Results.Redirect("/?ErrorMessage=Please+fill+in+all+fields");
            }

            var user = await userManager.FindByNameAsync(username);
            if (user == null)
            {
                return Results.Redirect("/?ErrorMessage=Invalid+username+or+password");
            }

            var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                return Results.Redirect("/Game");
            }

            return Results.Redirect("/?ErrorMessage=Invalid+username+or+password");
        });

        group.MapPost("/register", async (
            HttpContext context,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IPersistenceService persistenceService) =>
        {
            var form = await context.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var confirmPassword = form["confirmPassword"].ToString();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Results.Redirect("/?ErrorMessage=Please+fill+in+all+fields");
            }

            if (password != confirmPassword)
            {
                return Results.Redirect("/?ErrorMessage=Passwords+do+not+match");
            }

            var user = new ApplicationUser
            {
                UserName = username
            };

            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                // Create character for this account
                var accountId = new AccountId(user.Id);
                await persistenceService.CreateCharacterAsync(accountId, username);

                // Sign in and redirect to game
                await signInManager.SignInAsync(user, isPersistent: true);
                return Results.Redirect("/Game");
            }

            var errorMessage = Uri.EscapeDataString(string.Join(" ", result.Errors.Select(e => e.Description)));
            return Results.Redirect($"/?ErrorMessage={errorMessage}");
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/");
        });

        return app;
    }
}
