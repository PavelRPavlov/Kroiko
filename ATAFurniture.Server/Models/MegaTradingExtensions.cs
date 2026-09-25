using Kroiko.Domain.TemplateBuilding;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ATAFurniture.Server.Models;

// The MegaTrading tab edits MegaTradingViewModel copies of the domain's MegaTradingDetails (the mapping
// from Details lives in the domain's MegaTrading order format).
public static class MegaTradingExtensions {
    public static ObservableCollection<MegaTradingViewModel> ToMegaTradingViewModel(this IEnumerable<IKroikoDetail> genericDetails)
    {
        var dtos = genericDetails.Cast<MegaTradingDetail>();
        var vms = dtos.Select(x =>
            new MegaTradingViewModel(x.Rotated, x.EdgeBandingMaterial, x.LeftEdge, x.RightEdge, x.BottomEdge, x.TopEdge, x.Id, x.Width, x.Height, x.Thickness, x.Quantity, x.Material, x.Note));
        return new ObservableCollection<MegaTradingViewModel>(vms);

    }

    public static List<IKroikoDetail> ToKroikoDetails(this ObservableCollection<MegaTradingViewModel> megaTradingViewModels) =>
        megaTradingViewModels.Select(x => new MegaTradingDetail
        {
            Id = x.Id,
            Width = x.Width,
            Height = x.Height,
            Quantity = x.Quantity,
            Material = x.Material,
            BottomEdge = x.BottomEdge,
            LeftEdge = x.LeftEdge,
            RightEdge = x.RightEdge,
            TopEdge = x.TopEdge,
            Note = x.Note,
            Thickness = x.Thickness,
            EdgeBandingMaterial = x.EdgeBandingMaterial,
            Rotated = x.Rotated
        }).ToList<IKroikoDetail>();
}