using System.Globalization;

namespace Kroiko.Domain.TemplateBuilding.MegaTrading;

internal sealed class MegaTradingFileNameProvider: IFileNameProvider
{
    public string GetFileNameForSheet(ISheet sheet) =>
        string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd}_{{CompanyName}}.xlsx");
}