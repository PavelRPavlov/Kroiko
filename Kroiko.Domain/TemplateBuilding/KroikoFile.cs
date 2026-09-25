namespace Kroiko.Domain.TemplateBuilding;
public class LoniraDetail : IKroikoDetail
{
    public string Material { get; set; }
    public string? LoniraEdges { get; set; }
    public string? Note { get; set; }
    public Guid Id { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Quantity { get; set; }
}
public class MegaTradingDetail : IKroikoDetail
{
    public string? Note { get; set; }
    public string Material { get; set; }
    public string? EdgeBandingMaterial { get; set; }
    public bool Rotated { get; set; }
    public string LeftEdge { get; set; }
    public string RightEdge { get; set; }
    public string BottomEdge { get; set; }
    public string TopEdge { get; set; }
    public Guid Id { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Thickness { get; set; }
    public int Quantity { get; set; }
}
public class SuliverDetail : IKroikoDetail
{
    public Guid Id { get; set; }
    public bool IsEdgeColorDifferent { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Quantity { get; set; }
    public string Material { get; set; }
    public string? Cabinet { get; set; }
    public double MaterialThickness { get; set; }
    public byte IsGrainDirectionReversed { get; set; }
    public string? LongEdge { get; set; }
    public string? LongEdge2 { get; set; }
    public string? ShortEdge { get; set; }
    public string? ShortEdge2 { get; set; }
    public string? Note { get; set; }

    public void AdjustHeightForFalc(bool result)
    {
        if (result)
        {
            Height += 4;
        }
    }

    public void AdjustWidthForFalc(bool result)
    {
        if (result)
        {
            Width += 4;
        }
    }
}

public interface IKroikoDetail
{
    public Guid Id { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public string Material { get; set; }
}

public class KroikoFile
{
    public required string FileName { get; set; }
    
    public required List<IKroikoDetail> Details { get; set; }
}