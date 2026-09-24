using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Kroiko.Domain.TemplateBuilding;

internal abstract class TemplateBuilderBase : ITemplateBuilder
{
    public const string CompanyNameCellFlag = "{CompanyName}";
    public const string MobileNumberCellFlag = "{MobileNumber}";
    public const string TableStartCellFlag = "{TableStart}";
    public const string DifferentEdgeColorCellFlag = "{DifferentEdgeColor}";

    public abstract IList<ISheet> BuildTemplate(ContactInfo contactInfo, IEnumerable<KroikoFile> files);

    protected Cell PopulateStaticInfo(ISheet sheet, ContactInfo contactInfo)
    {
        var tableStartCell = Cell.Empty;
        foreach (var cell in sheet.Cells)
        {
            switch (cell.Value)
            {
                case CompanyNameCellFlag:
                    cell.Value = contactInfo.CompanyName;
                    break;
                case MobileNumberCellFlag:
                    cell.Value = contactInfo.MobileNumber;
                    break;
                case TableStartCellFlag:
                    tableStartCell = cell;
                    cell.Value = null;
                    break;
                default:
                    break;
            }
        }

        if (tableStartCell != Cell.Empty)
        {
            sheet.Cells.Remove(tableStartCell);
        }

        return tableStartCell;
    }
    
    /// <summary>
    /// A fresh copy of <paramref name="manufacturer"/>'s template, deserialised from the <c>template.json</c>
    /// embedded in this assembly. Each call returns a new sheet, as the builders fill it in place.
    /// </summary>
    protected static T ReadTemplate<T>(string manufacturer, JsonTypeInfo<T> typeInfo) where T : ISheet
    {
        var resourceName = $"{typeof(TemplateBuilderBase).Namespace}.{manufacturer}.template.json";
        using var stream = typeof(TemplateBuilderBase).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"'{resourceName}' is not embedded in {typeof(TemplateBuilderBase).Assembly.GetName().Name}.");
        return JsonSerializer.Deserialize(stream, typeInfo)
            ?? throw new InvalidOperationException($"'{resourceName}' holds no template.");
    }
    
    protected void PopulateDetails(ISheet sheet, Cell tableStartCell, IEnumerable<IKroikoDetail> details, ITableRowProvider tableRowProvider)
    {
        (int currentRow, int currentColumn) = Cell.GetRowAndColumn(tableStartCell);
        foreach (var detail in details)
        {
            var row = tableRowProvider.GetTableRow(detail, currentRow, currentColumn);
            sheet.Cells.AddRange(row);
            currentRow++;
        }
    }
}