namespace Kroiko.Domain.TemplateBuilding;

internal interface ITemplateBuilder
{
    /// <summary>Fills a fresh copy of the manufacturer's template for each sheet <paramref name="files"/> make.</summary>
    IList<ISheet> BuildTemplate(ContactInfo contactInfo, IEnumerable<KroikoFile> files);
}
