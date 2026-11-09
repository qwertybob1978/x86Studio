using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class DesignerControlTests
{
    [Fact]
    public void ControlFitsAtTheExactSurfaceBoundary()
    {
        var control = new DesignerControl("button-1", "OK", 10, 20, 80, 24);

        Assert.True(control.FitsWithin(90, 44));
    }

    [Fact]
    public void ControlOutsideSurfaceDoesNotFit()
    {
        var control = new DesignerControl("button-1", "OK", 10, 20, 80, 24);

        Assert.False(control.FitsWithin(89, 44));
        Assert.False(control.FitsWithin(90, 43));
    }

    [Fact]
    public void ControlRejectsNonFiniteOrNegativeGeometry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DesignerControl("button-1", "OK", double.NaN, 0, 80, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DesignerControl("button-1", "OK", 0, 0, double.PositiveInfinity, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DesignerControl("button-1", "OK", -1, 0, 80, 24));
    }

    [Theory]
    [InlineData(5, 10, 10)]
    [InlineData(4.9, 10, 0)]
    [InlineData(15, 10, 20)]
    public void SnapRoundsToTheNearestGridPoint(
        double coordinate,
        double spacing,
        double expected)
    {
        Assert.Equal(expected, DesignerGeometry.Snap(coordinate, spacing));
    }

    [Fact]
    public void SnapRejectsInvalidGridSpacing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DesignerGeometry.Snap(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DesignerGeometry.Snap(4, double.NaN));
    }

    [Theory]
    [InlineData(DesignerControlAlignment.Left, 10, 10, 20, 40)]
    [InlineData(DesignerControlAlignment.Right, 10, 70, 20, 40)]
    [InlineData(DesignerControlAlignment.Top, 10, 30, 20, 20)]
    [InlineData(DesignerControlAlignment.Bottom, 10, 30, 20, 40)]
    public void AlignUsesTheFirstControlAsTheAnchor(
        DesignerControlAlignment alignment,
        double expectedX1,
        double expectedX2,
        double expectedY1,
        double expectedY2)
    {
        var controls = new[]
        {
            new DesignerControl("first", "First", 10, 20, 80, 40),
            new DesignerControl("second", "Second", 30, 40, 20, 20),
        };

        var aligned = DesignerGeometry.Align(controls, alignment);

        Assert.Equal(expectedX1, aligned[0].X);
        Assert.Equal(expectedX2, aligned[1].X);
        Assert.Equal(expectedY1, aligned[0].Y);
        Assert.Equal(expectedY2, aligned[1].Y);
    }
}
