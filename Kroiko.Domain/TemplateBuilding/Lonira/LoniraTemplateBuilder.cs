using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Domain.TemplateBuilding.Lonira;

public class LoniraTemplateBuilder(ITableRowProvider tableRowProvider) : TemplateBuilderBase
{
    private const string MaterialNameCellFlag = "{MaterialName}";

    public override Task<IList<ISheet>> BuildTemplateAsync(ContactInfo contactInfo, IEnumerable<KroikoFile> files)
    {
        List<ISheet> sheets = new();
        foreach (var file in files)
        {
            // TODO only for Lonira the details are grouped by material beforehand
            var groupMaterial = file.FileName;
            var groupDetails = file.Details.Cast<LoniraDetail>();

            var sheet = ReadTemplate(nameof(SupportedCompanies.Lonira), TemplateJsonContext.Default.LoniraSheet);
            sheet.SheetMaterial = groupMaterial;

            var tableStartCell = PopulateStaticInfo(sheet, contactInfo);
            PopulateDetails(sheet, tableStartCell, groupDetails, tableRowProvider);
            PopulateMaterialName(sheet, groupMaterial);

            sheets.Add(sheet);
        }

        return Task.FromResult<IList<ISheet>>(sheets);
    }

    private void PopulateMaterialName(ISheet sheet, string materialName)
    {
        foreach (var cell in sheet.Cells.Where(cell => cell.Value?.ToString() == MaterialNameCellFlag))
        {
            cell.Value = materialName;
        }
    }
}
