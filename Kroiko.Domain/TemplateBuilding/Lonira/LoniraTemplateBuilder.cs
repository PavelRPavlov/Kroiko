using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Domain.TemplateBuilding.Lonira;

internal sealed class LoniraTemplateBuilder(ITableRowProvider tableRowProvider) : TemplateBuilderBase
{
    private const string MaterialNameCellFlag = "{MaterialName}";

    public override IList<ISheet> BuildTemplate(ContactInfo contactInfo, IEnumerable<KroikoFile> files)
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

        return sheets;
    }

    private static void PopulateMaterialName(ISheet sheet, string materialName)
    {
        foreach (var cell in sheet.Cells.Where(cell => cell.Value == MaterialNameCellFlag))
        {
            cell.Value = materialName;
        }
    }
}
