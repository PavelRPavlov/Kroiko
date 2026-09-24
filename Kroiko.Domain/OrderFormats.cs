using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.TemplateBuilding.Lonira;
using Kroiko.Domain.TemplateBuilding.MegaTrading;
using Kroiko.Domain.TemplateBuilding.Suliver;

namespace Kroiko.Domain;

/// <summary>The order format of each manufacturer the domain supports (ADR-0004 §1). The domain uses no DI.</summary>
public static class OrderFormats
{
    /// <summary>One format per <see cref="SupportedCompanies"/> manufacturer.</summary>
    public static IReadOnlyList<IOrderFormat> All { get; } =
    [
        new LoniraOrderFormat(),
        new SuliverOrderFormat(),
        new MegaTradingOrderFormat(),
    ];

    /// <summary>The format of <paramref name="company"/>, found by its <see cref="SupportedCompany.Name"/>.</summary>
    /// <exception cref="ArgumentException">The domain has no format for <paramref name="company"/>.</exception>
    public static IOrderFormat For(SupportedCompany company)
    {
        ArgumentNullException.ThrowIfNull(company);
        return All.FirstOrDefault(format => format.Company.Name == company.Name)
            ?? throw new ArgumentException($"There is no order format for '{company.Name}'.", nameof(company));
    }
}
