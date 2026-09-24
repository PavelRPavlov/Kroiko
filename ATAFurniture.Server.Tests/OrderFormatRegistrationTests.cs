using ATAFurniture.Server.Models;
using FluentAssertions;
using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// The Server resolves each manufacturer's <see cref="IOrderFormat"/> with keyed DI, keyed by
/// <c>nameof(SupportedCompanies.X)</c> (ADR-0004 §1, ADR-0007 §8), as registered by <c>Startup</c>.
/// </summary>
public sealed class OrderFormatRegistrationTests
{
    private static ServiceProvider Services() =>
        new ServiceCollection().AddOrderFormats().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    [Theory]
    [InlineData(nameof(SupportedCompanies.Lonira))]
    [InlineData(nameof(SupportedCompanies.Suliver))]
    [InlineData(nameof(SupportedCompanies.MegaTrading))]
    public void Every_manufacturer_key_resolves_its_order_format(string key)
    {
        using var services = Services();

        services.GetRequiredKeyedService<IOrderFormat>(key).Company.Name.Should().Be(key);
    }

    [Fact]
    public void Every_branch_in_the_dropdown_resolves_an_order_format()
    {
        using var services = Services();

        ManufacturerBranches.All.Should().AllSatisfy(branch =>
            services.GetKeyedService<IOrderFormat>(branch.Name).Should().NotBeNull());
    }

    [Fact]
    public void Kuklensko_resolves_to_the_Suliver_format_with_its_own_order_email()
    {
        using var services = Services();

        var kuklensko = ManufacturerBranches.SuliverKuklensko;

        services.GetRequiredKeyedService<IOrderFormat>(kuklensko.Name)
            .Should().BeSameAs(OrderFormats.For(SupportedCompanies.Suliver));
        kuklensko.Email.Should().Be("saliver_m1@abv.bg").And.NotBe(ManufacturerBranches.Suliver.Email);
    }
}
