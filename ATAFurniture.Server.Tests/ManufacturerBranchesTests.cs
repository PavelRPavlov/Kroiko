using ATAFurniture.Server.Models;
using FluentAssertions;
using Kroiko.Domain.CellsExtracting;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// The domain knows three manufacturers and no emails (ADR-0004 §6); the Server keeps the order emails and
/// Suliver's Kuklensko branch. These values are also stored in <c>Users.LastSelectedCompany_*</c>, so they
/// must stay as they were.
/// </summary>
public sealed class ManufacturerBranchesTests
{
    [Fact]
    public void Kuklensko_is_a_Suliver_branch_with_its_own_order_email()
    {
        var kuklensko = ManufacturerBranches.SuliverKuklensko;

        kuklensko.Name.Should().Be(SupportedCompanies.Suliver.Name);
        kuklensko.Email.Should().Be("saliver_m1@abv.bg")
            .And.NotBe(ManufacturerBranches.Suliver.Email);
    }

    [Fact]
    public void Each_branch_keeps_its_label_and_order_email()
    {
        ManufacturerBranches.All.Should().Equal(
            new ManufacturerBranch("Lonira", "Лонира, гр.София", "office@lonyra.com"),
            new ManufacturerBranch("MegaTrading", "Мега Трейдинг, гр.София", "razkroi_mt@abv.bg"),
            new ManufacturerBranch("Suliver", "Съливер, гр.Пловдив (бул.Васил Априлов)", "saliver_zaiavki@abv.bg"),
            new ManufacturerBranch("Suliver", "Съливер, гр.Пловдив (бул.Кукленско Шосе)", "saliver_m1@abv.bg"));
    }
}
