using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.ExcelFilesGeneration.XlsxWrapper;
using Kroiko.Domain.TemplateBuilding;

namespace Kroiko.Domain;

/// <summary>
/// An order format that fills its manufacturer's template and writes it as <c>.xlsx</c> files. Replaces the
/// caller-wired <c>FileGeneratorService</c>: the builder and file-name provider are always there.
/// </summary>
internal abstract class OrderFormatBase(ITemplateBuilder templateBuilder, IFileNameProvider fileNameProvider) : IOrderFormat
{
    public abstract SupportedCompany Company { get; }

    public abstract IReadOnlyList<KroikoFile> CreateFiles(IReadOnlyList<Detail> details);

    /// <summary>One file named <paramref name="fileName"/> holding every detail, or none for no details.</summary>
    protected static IReadOnlyList<KroikoFile> OneFile(
        string fileName, IReadOnlyList<Detail> details, Func<Detail, IKroikoDetail> toManufacturerDetail) =>
        details.Count == 0
            ? []
            : [new KroikoFile { FileName = fileName, Details = details.Select(toManufacturerDetail).ToList() }];

    public virtual IReadOnlyList<FileSaveContext> Generate(ContactInfo contact, IReadOnlyList<KroikoFile> files, string? differentEdgeColor)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(files);

        var sheets = templateBuilder.BuildTemplate(contact, files);
        foreach (var sheet in sheets)
        {
            var differentEdgeColorCell = sheet.Cells.FirstOrDefault(c => c.Value == TemplateBuilderBase.DifferentEdgeColorCellFlag);
            if (differentEdgeColorCell is not null)
            {
                differentEdgeColorCell.Value = differentEdgeColor;
            }
        }

        var result = ExcelFileGenerator.GenerateExcelFiles(sheets, fileNameProvider);
        foreach (var file in result)
        {
            file.FileName = file.FileName.Replace(TemplateBuilderBase.CompanyNameCellFlag, contact.CompanyName);
        }

        return result;
    }
}
