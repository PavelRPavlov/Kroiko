#nullable enable
using System.Collections.Generic;
using Kroiko.Domain.CellsExtracting;

namespace ATAFurniture.Server.Models;

// A manufacturer branch the Server sends orders to: the domain manufacturer's Name (which picks the order
// format), the label shown in the dropdown and the branch's order email. Suliver has two branches that
// share the Suliver format. Stored as Users.LastSelectedCompany_{Name,Translation,Email}; the values must
// not change, or a user's stored selection no longer matches any branch.
public sealed record ManufacturerBranch(string Name, string Translation, string Email);

public static class ManufacturerBranches
{
    public static readonly ManufacturerBranch Lonira =
        new(SupportedCompanies.Lonira.Name, SupportedCompanies.Lonira.Translation, "office@lonyra.com");

    public static readonly ManufacturerBranch MegaTrading =
        new(SupportedCompanies.MegaTrading.Name, SupportedCompanies.MegaTrading.Translation, "razkroi_mt@abv.bg");

    public static readonly ManufacturerBranch Suliver =
        new(SupportedCompanies.Suliver.Name, SupportedCompanies.Suliver.Translation, "saliver_zaiavki@abv.bg");

    public static readonly ManufacturerBranch SuliverKuklensko =
        new(SupportedCompanies.Suliver.Name, "Съливер, гр.Пловдив (бул.Кукленско Шосе)", "saliver_m1@abv.bg");

    // In the order the target-company dropdown lists them.
    public static readonly IReadOnlyList<ManufacturerBranch> All = [Lonira, MegaTrading, Suliver, SuliverKuklensko];
}
