using System.Globalization;
using Kroiko.Domain.TemplateBuilding;
using LargeXlsx;

namespace Kroiko.Domain.ExcelFilesGeneration.XlsxWrapper;

/// <summary>Writes each sheet as an <c>.xlsx</c> file named by the manufacturer's file-name provider.</summary>
internal static class ExcelFileGenerator
{
    public static List<FileSaveContext> GenerateExcelFiles(IEnumerable<ISheet> sheets, IFileNameProvider fileNameProvider)
    {
        var result = new List<FileSaveContext>();
        
        foreach (var sheet in sheets)
        {
            using var str = new MemoryStream();
            using var writer = new XlsxWriter(str);
            var lastFilledRow = 0;
            var sheetColumns = sheet.ColumnWidths.ToColumnStyle();
            writer.BeginWorksheet("Sheet1", columns: sheetColumns);
            var groupedCellsByRow = sheet.Cells.GroupBy(c => c.Row);
            foreach (var row in groupedCellsByRow)
            {
                lastFilledRow = CreateAllRows(lastFilledRow, row, writer);
            }
            writer.Dispose();
            var fileName = fileNameProvider.GetFileNameForSheet(sheet);
            result.Add(new FileSaveContext(fileName, str.ToArray()));
        }

        return result;
    }

    private static int CreateAllRows(int lastFilledRow, IGrouping<int, Cell> row, XlsxWriter writer)
    {
        lastFilledRow++;

        while (lastFilledRow < row.Key)
        {
            writer.SkipRows(1);
            lastFilledRow++;
        }
        writer.BeginRow();
                
        foreach (var cell in row)
        {
            CreateSingleRow(writer, cell);
        }

        return lastFilledRow;
    }

    private static void CreateSingleRow(XlsxWriter writer, Cell cell)
    {
        if (cell.ContentAlignment == 0)
        {
            writer.Write(cell.Value);
        }
        else
        {
            if ( string.IsNullOrEmpty(cell.Value))
            {
                writer.SkipColumns(1);
                return;
            }

            // Invariant, as the row providers write numbers (ADR-0004 §4): "609.5" is a number on any host.
            if (double.TryParse(cell.Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var val))
            {
                writer.Write(
                    val,
                    XlsxStyle.Default.With(
                        alignment: new XlsxAlignment(XlsxAlignment.Horizontal.Center)));
            }
            else
            {
                writer.Write(
                    cell.Value,
                    XlsxStyle.Default.With(
                        alignment: new XlsxAlignment(XlsxAlignment.Horizontal.Center)));
            }
        }
    }
}