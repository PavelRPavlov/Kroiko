using ATAFurniture.Server.Models;
using FluentAssertions;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// The domain knows three manufacturers and no emails (ADR-0004 §6); the Server keeps the order emails and
/// Suliver's Kuklensko branch, which resolves to the Suliver format (<see cref="OrderFormatRegistrationTests"/>).
/// These values are also stored in <c>Users.LastSelectedCompany_*</c>, so they must stay as they were.
/// </summary>
public sealed class ManufacturerBranchesTests
{
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
