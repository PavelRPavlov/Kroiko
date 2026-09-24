namespace Kroiko.Domain.TemplateBuilding.Lonira;

internal sealed class LoniraFileNameProvider : IFileNameProvider
{
    public string GetFileNameForSheet(ISheet sheet)
    { 
        // TODO legacy ???
        // var name = $"{DateTime.Now:yyyy-MM-dd}_{{CompanyName}}_{{Material}}.xlsx";
        
        var name = "{Material}.xlsx";
        if (sheet is LoniraSheet loniraSheet)
        {
            name = name.Replace("{Material}", loniraSheet.SheetMaterial);
        }

        return name;
    }
}