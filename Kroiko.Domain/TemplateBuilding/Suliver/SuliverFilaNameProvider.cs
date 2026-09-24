using System.Globalization;

namespace Kroiko.Domain.TemplateBuilding.Suliver;

public class SuliverFileNameProvider : IFileNameProvider
{
    public string GetFileNameForSheet(ISheet sheet)
    { 
        return string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd}_{{CompanyName}}.xlsx");
    }
}