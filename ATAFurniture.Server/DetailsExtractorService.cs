using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kroiko.Domain.CellsExtracting;
using Microsoft.Extensions.Logging;

namespace ATAFurniture.Server;

public interface IDetailsExtractorService
{
    Task<List<Detail>> ExtractDetails(MemoryStream stream);
}

/// <summary>
/// The Server's adapter over the domain's <see cref="PolyboardParser"/>. It keeps today's behaviour
/// (ADR-0004 §3): any bad line is logged and the whole file yields no Details.
/// </summary>
public class DetailsExtractorService(ILogger<DetailsExtractorService> logger) : IDetailsExtractorService
{
    public Task<List<Detail>> ExtractDetails(MemoryStream stream)
    {
        var result = PolyboardParser.Parse(stream.ToArray());
        if (result.Errors.Count == 0)
        {
            return Task.FromResult(result.Details.ToList());
        }

        foreach (var error in result.Errors)
        {
            logger.LogError(
                "Polyboard line {LineNumber} did not match the required format: {Kind} (field count {FieldCount}, field {Field})",
                error.LineNumber, error.Kind, error.FieldCount, error.Field);
        }

        return Task.FromResult(new List<Detail>());
    }
}