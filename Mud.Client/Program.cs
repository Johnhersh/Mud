using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Mud.Client.Services;
using Mud.Client.Rendering;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new GameClient(builder.HostEnvironment.BaseAddress));
builder.Services.AddScoped<RenderCommandBuffer>();
builder.Services.AddScoped<ImmediateCommands>();
builder.Services.AddScoped<GameRenderer>();

await builder.Build().RunAsync();
