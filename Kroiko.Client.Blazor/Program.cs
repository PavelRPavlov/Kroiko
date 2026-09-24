using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Kroiko.Client.Blazor;
using Kroiko.Client.Blazor.Conversion;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
// AddConverterState() follows once IConfirmation has its dialog (phase 04 step 3): the host validates the
// container in Development, so ConverterState cannot be registered before both of its interfaces.
builder.Services.AddDeviceSettingsStore();

await builder.Build().RunAsync();
