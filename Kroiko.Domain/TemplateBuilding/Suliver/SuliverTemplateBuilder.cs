using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Domain.TemplateBuilding.Suliver;

public class SuliverTemplateBuilder(ITableRowProvider tableRowProvider) : TemplateBuilderBase
{
    public override Task<IList<ISheet>> BuildTemplateAsync(ContactInfo contactInfo, IEnumerable<KroikoFile> files)
    {
        var sheet = ReadTemplate(nameof(SupportedCompanies.Suliver), TemplateJsonContext.Default.SheetBase);
        var tableStartCell = PopulateStaticInfo(sheet, contactInfo);
        
        // NOTE Suliver has a single file
        PopulateDetails(sheet, tableStartCell, files.First().Details, tableRowProvider);

        return Task.FromResult<IList<ISheet>>(new List<ISheet> { sheet });
    }
}
