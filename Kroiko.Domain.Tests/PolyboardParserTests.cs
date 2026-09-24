using System.Globalization;
using System.Text;
using FluentAssertions;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Testing;
using Xunit;

namespace Kroiko.Domain.Tests;

/// <summary>
/// <see cref="PolyboardParser.Parse"/>: both Polyboard field formats, and every
/// <see cref="ParseErrorKind"/> it reports instead of discarding (ADR-0004 §3).
/// </summary>
public sealed class PolyboardParserTests
{
    private static ParseResult Parse(string text) => PolyboardParser.Parse(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void An_11_field_line_becomes_a_detail_with_the_latest_format_fields_zero_or_empty()
    {
        var result = Parse("350.5;150.0;2;Basic white W908 ST2;1;1;0;1;0;Model[0];3\r\n");

        result.Errors.Should().BeEmpty();
        result.Details.Should().Equal(new Detail(
            Height: 350.5,
            Width: 150.0,
            Quantity: 2,
            Material: "Basic white W908 ST2",
            IsGrainDirectionReversed: true,
            HasTopEdge: true,
            HasBottomEdge: false,
            HasRightEdge: true,
            HasLeftEdge: false,
            Cabinet: "Model[0]",
            CuttingNumber: 3,
            MaterialThickness: 0,
            TopEdgeThickness: 0,
            BottomEdgeThickness: 0,
            RightEdgeThickness: 0,
            LeftEdgeThickness: 0,
            Reference: "",
            TopEdgeMaterial: "",
            BottomEdgeMaterial: "",
            RightEdgeMaterial: "",
            LeftEdgeMaterial: "",
            OversizingHeight: 0,
            OversizingWidth: 0));
    }

    [Fact]
    public void A_23_field_line_becomes_a_detail_with_every_field()
    {
        var result = Parse("243.00;200.00;1;MELA_BL;0;1;1;0;0;Cabinet1;4;19.00;2.0;0.8;;1;Top;ABS white;ABS oak;;PVC;1.50;2.00\r\n");

        result.Errors.Should().BeEmpty();
        result.Details.Should().Equal(new Detail(
            Height: 243,
            Width: 200,
            Quantity: 1,
            Material: "MELA_BL",
            IsGrainDirectionReversed: false,
            HasTopEdge: true,
            HasBottomEdge: true,
            HasRightEdge: false,
            HasLeftEdge: false,
            Cabinet: "Cabinet1",
            CuttingNumber: 4,
            MaterialThickness: 19,
            TopEdgeThickness: 2,
            BottomEdgeThickness: 0.8,
            RightEdgeThickness: 0, // an empty numeric field is 0
            LeftEdgeThickness: 1,
            Reference: "Top",
            TopEdgeMaterial: "ABS white",
            BottomEdgeMaterial: "ABS oak",
            RightEdgeMaterial: "",
            LeftEdgeMaterial: "PVC",
            OversizingHeight: 1.5,
            OversizingWidth: 2));
    }

    [Fact]
    public void A_line_with_neither_11_nor_23_fields_is_a_FieldCount_error_and_the_other_lines_are_kept()
    {
        // Line 10 of the fixture has 9 fields; its other 19 lines are good 11-field lines.
        var result = ParseFixture("bad-field-count");

        result.Errors.Should().Equal(new ParseError(10, ParseErrorKind.FieldCount, FieldCount: 9));
        result.Details.Should().HaveCount(19);
    }

    [Theory]
    [InlineData("350.0;abc;2;Basic white;0;1;1;1;1;Model[0];1", nameof(Detail.Width))]
    [InlineData("350.0;150.0;1.5;Basic white;0;1;1;1;1;Model[0];1", nameof(Detail.Quantity))]
    [InlineData("350.0;150.0;2;Basic white;0;x;1;1;1;Model[0];1", nameof(Detail.HasTopEdge))]
    [InlineData("243.00;200.00;1;MELA_BL;0;0;0;0;0;Cabinet1;1;19.00;;;;;Top;;;;;0.00;2.5mm", nameof(Detail.OversizingWidth))]
    // Only the first bad field of a line is reported.
    [InlineData("350.0.0;abc;2;Basic white;0;1;1;1;1;Model[0];1", nameof(Detail.Height))]
    public void A_field_that_is_not_an_invariant_number_is_an_InvalidNumber_error_naming_the_field(string badLine, string field)
    {
        var good = "350.0;150.0;2;Basic white;0;1;1;1;1;Model[0];1";

        var result = Parse($"{good}\r\n{badLine}\r\n{good}\r\n");

        result.Errors.Should().Equal(new ParseError(2, ParseErrorKind.InvalidNumber, Field: field));
        result.Details.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("wardrobes-4-materials", 76, 4)] // 11-field
    [InlineData("cabinet-23-field", 5, 2)]
    public void A_fixture_in_either_field_format_parses_without_errors(string fixture, int details, int materials)
    {
        var result = ParseFixture(fixture);

        result.Errors.Should().BeEmpty();
        result.Details.Should().HaveCount(details);
        result.Details.Select(d => d.Material).Distinct().Should().HaveCount(materials);
    }

    [Fact]
    public void Every_line_of_an_unsupported_16_field_file_is_a_FieldCount_error()
    {
        var result = ParseFixture("unsupported-16-field");

        result.Details.Should().BeEmpty();
        result.Errors.Should().Equal(
            Enumerable.Range(1, 5).Select(line => new ParseError(line, ParseErrorKind.FieldCount, FieldCount: 16)));
    }

    [Fact]
    public void A_comma_is_a_thousands_separator_as_in_todays_Server()
    {
        // Characterization: Polyboard writes '.' decimals; a ',' is read as a group separator, not rejected.
        var result = Parse("1,350.5;150,0;2;Basic white;0;1;1;1;1;Model[0];1\r\n");

        result.Errors.Should().BeEmpty();
        result.Details.Single().Should().Match<Detail>(d => d.Height == 1350.5 && d.Width == 1500);
    }

    [Fact]
    public void The_host_culture_does_not_change_the_result()
    {
        var invariant = ParseFixture("cabinet-23-field");

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("bg-BG");
        try
        {
            var bulgarian = ParseFixture("cabinet-23-field");

            bulgarian.Details.Should().Equal(invariant.Details);
            bulgarian.Errors.Should().BeEmpty();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static ParseResult ParseFixture(string fixture) =>
        PolyboardParser.Parse(File.ReadAllBytes(TestData.Polyboard(fixture)));
}
