namespace Kroiko.Domain.CellsExtracting;

// The three manufacturers the domain can produce order files for (ADR-0004 §6). Order emails and
// Suliver's Kuklensko branch are the Server's concern (ATAFurniture.Server.Models.ManufacturerBranches).
public static class SupportedCompanies
{
    public static readonly SupportedCompany Lonira = new(nameof(Lonira), "Лонира, гр.София");
    public static readonly SupportedCompany MegaTrading = new(nameof(MegaTrading), "Мега Трейдинг, гр.София");
    public static readonly SupportedCompany Suliver = new(nameof(Suliver), "Съливер, гр.Пловдив (бул.Васил Априлов)");
}

public sealed record SupportedCompany(string Name, string Translation);
