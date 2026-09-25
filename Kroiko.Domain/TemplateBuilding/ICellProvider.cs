namespace Kroiko.Domain.TemplateBuilding;

internal interface ITableRowProvider
{
    IEnumerable<Cell> GetTableRow(IKroikoDetail detail, int rowNumber, int startColumnNumber);
}