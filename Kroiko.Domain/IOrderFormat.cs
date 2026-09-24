using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.TemplateBuilding;

namespace Kroiko.Domain;

/// <summary>
/// Everything one manufacturer needs to turn parsed Details into order files (ADR-0004 §1). Get one
/// from <see cref="OrderFormats"/>; it is the only way to reach the manufacturer's templates, table
/// rows and file names. Implementations are stateless and safe to share.
/// </summary>
public interface IOrderFormat
{
    /// <summary>The manufacturer this format writes order files for.</summary>
    SupportedCompany Company { get; }

    /// <summary>
    /// Maps <paramref name="details"/> to this manufacturer's details and groups them into the files
    /// the operator reviews and edits before generating (Lonira: one per material; the others: one).
    /// No details make no files.
    /// </summary>
    IReadOnlyList<KroikoFile> CreateFiles(IReadOnlyList<Detail> details);

    /// <summary>
    /// The reasons this format refuses to generate <paramref name="files"/> as they are now, after the
    /// operator's edits (ADR-0006 §3); empty when they can be generated. Only MegaTrading has any: more
    /// materials than its <c>.cut_mt</c> header can list. <see cref="Generate"/> does not call it.
    /// </summary>
    IReadOnlyList<OrderProblem> Check(IReadOnlyList<KroikoFile> files);

    /// <summary>
    /// Writes the order files for <paramref name="files"/>: one <c>.xlsx</c> per file for Lonira, one
    /// <c>.xlsx</c> for Suliver, and a <c>.cut_mt</c> then an <c>.xlsx</c> for MegaTrading.
    /// <paramref name="differentEdgeColor"/> fills the template's "different edge colour" cell, if it has one.
    /// </summary>
    IReadOnlyList<FileSaveContext> Generate(ContactInfo contact, IReadOnlyList<KroikoFile> files, string? differentEdgeColor);
}
