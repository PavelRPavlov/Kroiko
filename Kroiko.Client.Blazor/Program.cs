using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Kroiko.Client.Blazor;
using Kroiko.Client.Blazor.Conversion;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddConfirmationDialog();
builder.Services.AddDeviceSettingsStore();
builder.Services.AddConverterState();

await builder.Build().RunAsync();
