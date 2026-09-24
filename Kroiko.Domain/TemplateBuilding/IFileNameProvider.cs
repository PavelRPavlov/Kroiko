namespace Kroiko.Domain.TemplateBuilding;

internal interface IFileNameProvider
{
    string GetFileNameForSheet(ISheet sheet);
}