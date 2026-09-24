using System.Text.Json.Serialization;
using Kroiko.Domain.TemplateBuilding.Lonira;

namespace Kroiko.Domain.TemplateBuilding;

/// <summary>
/// Source-generated (trim-safe) metadata for reading the embedded <c>template.json</c> files
/// (ADR-0004 §2). Default options, as the reflection-based read had: case-sensitive property names.
/// </summary>
[JsonSerializable(typeof(SheetBase))]
[JsonSerializable(typeof(LoniraSheet))]
internal sealed partial class TemplateJsonContext : JsonSerializerContext;
