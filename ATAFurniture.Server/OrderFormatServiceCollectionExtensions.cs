#nullable enable
using Kroiko.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace ATAFurniture.Server;

public static class OrderFormatServiceCollectionExtensions
{
    /// <summary>
    /// Registers each manufacturer's <see cref="IOrderFormat"/> keyed by its name, i.e.
    /// <c>nameof(SupportedCompanies.X)</c>, which is also the <c>ManufacturerBranch.Name</c> of its branches.
    /// The formats are stateless, so one instance serves every circuit.
    /// </summary>
    public static IServiceCollection AddOrderFormats(this IServiceCollection services)
    {
        foreach (var format in OrderFormats.All)
        {
            services.AddKeyedSingleton(format.Company.Name, format);
        }

        return services;
    }
}
