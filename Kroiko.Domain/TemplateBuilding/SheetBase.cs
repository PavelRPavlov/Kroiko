namespace Kroiko.Domain.TemplateBuilding;

internal class SheetBase : ISheet
{
    public required List<int> ColumnWidths { get; set; }
    public required List<Cell> Cells { get; set; }
    
}