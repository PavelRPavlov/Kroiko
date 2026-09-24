using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Kroiko.Client.Blazor;
using Kroiko.Client.Blazor.Conversion;
using Kroiko.Client.Blazor.Updates;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddConfirmationDialog();
builder.Services.AddDeviceSettingsStore();
builder.Services.AddFileDownloader();
builder.Services.AddFolderPicker();
builder.Services.AddConverterState();
builder.Services.AddAppUpdates();

await builder.Build().RunAsync();
