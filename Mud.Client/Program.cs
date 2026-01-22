using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Mud.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new GameClient(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();
