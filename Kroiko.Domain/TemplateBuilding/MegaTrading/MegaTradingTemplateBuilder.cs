using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Domain.TemplateBuilding.MegaTrading;

public class MegaTradingTemplateBuilder(ITableRowProvider tableRowProvider) : TemplateBuilderBase
{
    public override Task<IList<ISheet>> BuildTemplateAsync(ContactInfo contactInfo, IEnumerable<KroikoFile> files)
    {
        var sheet = ReadTemplate(nameof(SupportedCompanies.MegaTrading), TemplateJsonContext.Default.SheetBase);
        var tableStartCell = PopulateStaticInfo(sheet, contactInfo);
        
        // NOTE MegaTrading has a single file
        PopulateDetails(sheet, tableStartCell, files.First().Details, tableRowProvider);

        return Task.FromResult<IList<ISheet>>(new List<ISheet> { sheet });
    }
}
