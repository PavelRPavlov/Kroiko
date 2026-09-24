using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Domain.TemplateBuilding.MegaTrading;

internal sealed class MegaTradingTemplateBuilder(ITableRowProvider tableRowProvider) : TemplateBuilderBase
{
    public override IList<ISheet> BuildTemplate(ContactInfo contactInfo, IEnumerable<KroikoFile> files)
    {
        var sheet = ReadTemplate(nameof(SupportedCompanies.MegaTrading), TemplateJsonContext.Default.SheetBase);
        var tableStartCell = PopulateStaticInfo(sheet, contactInfo);
        
        // NOTE MegaTrading has a single file
        PopulateDetails(sheet, tableStartCell, files.First().Details, tableRowProvider);

        return [sheet];
    }
}
