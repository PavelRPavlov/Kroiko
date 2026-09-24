namespace Kroiko.Domain.TemplateBuilding;

internal interface ISheet
{
    public List<int> ColumnWidths { get; set; }
    public List<Cell> Cells { get; set; }
}