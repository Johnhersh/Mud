using Mud.Server.Hubs;
using Mud.Server.Services;
using Mud.Server.Components;
using Mud.Client.Services;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddSignalR()
    .AddMessagePackProtocol();

builder.Services.AddSingleton<GameLoopService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GameLoopService>());

// Register GameClient for Server-Side Rendering (SSR)
builder.Services.AddScoped(sp => 
{
    var navigationManager = sp.GetRequiredService<NavigationManager>();
    return new GameClient(navigationManager.BaseUri);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Mud.Client.Routes).Assembly);

app.MapHub<GameHub>("/gamehub");

app.Run();
