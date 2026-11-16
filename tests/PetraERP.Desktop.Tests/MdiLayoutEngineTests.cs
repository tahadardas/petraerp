using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>Geometry of the classic MDI workspace: everything is computed from the container's own bounds, never from the screen.</summary>
public sealed class MdiLayoutEngineTests
{
    private static readonly MdiRect Container = new(0, 0, 1200, 700);

    private static void AssertInside(MdiRect rect, MdiRect bounds)
    {
        const double tolerance = 0.0001;
        Assert.True(rect.X >= bounds.X - tolerance, $"left {rect.X}");
        Assert.True(rect.Y >= bounds.Y - tolerance, $"top {rect.Y}");
        Assert.True(rect.Right <= bounds.Right + tolerance, $"right {rect.Right}");
        Assert.True(rect.Bottom <= bounds.Bottom + tolerance, $"bottom {rect.Bottom}");
    }

    [Fact]
    public void CascadeKeepsEveryWindowInsideTheContainerAndOffsetsThem()
    {
        var rects = MdiLayoutEngine.Cascade(5, Container);
        Assert.Equal(5, rects.Count);
        Assert.All(rects, rect => AssertInside(rect, Container));
        Assert.Equal(rects[0].X + MdiLayoutEngine.CascadeStep, rects[1].X, 3);
        Assert.Equal(rects[0].Y + MdiLayoutEngine.CascadeStep, rects[1].Y, 3);
        Assert.Single(rects.Select(rect => rect.Width).Distinct());
    }

    [Fact]
    public void CascadeWrapsInsteadOfLeavingTheContainerWhenManyWindowsAreOpen()
    {
        var rects = MdiLayoutEngine.Cascade(60, Container);
        Assert.Equal(60, rects.Count);
        Assert.All(rects, rect => AssertInside(rect, Container));
    }

    [Fact]
    public void TileHorizontalStacksFullWidthRowsWithoutOverlap()
    {
        var rects = MdiLayoutEngine.TileHorizontal(3, Container);
        Assert.Equal(3, rects.Count);
        Assert.All(rects, rect => { AssertInside(rect, Container); Assert.Equal(Container.Width, rect.Width, 3); });
        Assert.Equal(rects[0].Bottom, rects[1].Y, 3);
        Assert.Equal(rects[1].Bottom, rects[2].Y, 3);
        Assert.Equal(Container.Bottom, rects[2].Bottom, 3);
    }

    [Fact]
    public void TileVerticalPlacesFullHeightColumnsSideBySide()
    {
        var rects = MdiLayoutEngine.TileVertical(3, Container);
        Assert.Equal(3, rects.Count);
        Assert.All(rects, rect => { AssertInside(rect, Container); Assert.Equal(Container.Height, rect.Height, 3); });
        Assert.Equal(rects[0].Right, rects[1].X, 3);
        Assert.Equal(rects[1].Right, rects[2].X, 3);
        Assert.Equal(Container.Right, rects[2].Right, 3);
    }

    [Fact]
    public void TilingFallsBackToAGridWhenRowsOrColumnsWouldBeUnusablySmall()
    {
        foreach (var rects in new[] { MdiLayoutEngine.TileHorizontal(10, Container), MdiLayoutEngine.TileVertical(10, Container) })
        {
            Assert.Equal(10, rects.Count);
            Assert.All(rects, rect => AssertInside(rect, Container));
            Assert.All(rects, rect => Assert.True(rect.Height >= MdiLayoutEngine.MinimumHeight));
        }
    }

    [Fact]
    public void LayoutsFollowTheContainerOriginAndNotTheScreenOrigin()
    {
        var offset = new MdiRect(40, 90, 800, 500);
        Assert.All(MdiLayoutEngine.TileHorizontal(2, offset), rect => AssertInside(rect, offset));
        Assert.All(MdiLayoutEngine.TileVertical(2, offset), rect => AssertInside(rect, offset));
        Assert.All(MdiLayoutEngine.Cascade(4, offset), rect => AssertInside(rect, offset));
        Assert.Equal(40, MdiLayoutEngine.Cascade(1, offset)[0].X, 3);
        Assert.Equal(90, MdiLayoutEngine.Cascade(1, offset)[0].Y, 3);
    }

    [Fact]
    public void EmptyOrInvalidInputsProduceNoLayout()
    {
        Assert.Empty(MdiLayoutEngine.Cascade(0, Container));
        Assert.Empty(MdiLayoutEngine.TileHorizontal(2, new MdiRect(0, 0, 0, 0)));
        Assert.Empty(MdiLayoutEngine.TileVertical(2, new MdiRect(0, 0, double.NaN, 100)));
        Assert.Empty(MdiLayoutEngine.Grid(-1, Container));
    }

    [Fact]
    public void ClampMovesAWindowBackInsideAndRaisesItToTheMinimumSize()
    {
        AssertInside(MdiLayoutEngine.Clamp(new MdiRect(1100, 650, 400, 300), Container), Container);
        AssertInside(MdiLayoutEngine.Clamp(new MdiRect(-500, -500, 400, 300), Container), Container);
        var tiny = MdiLayoutEngine.Clamp(new MdiRect(10, 10, 20, 20), Container);
        Assert.Equal(MdiLayoutEngine.MinimumWidth, tiny.Width, 3);
        Assert.Equal(MdiLayoutEngine.MinimumHeight, tiny.Height, 3);
        var huge = MdiLayoutEngine.Clamp(new MdiRect(0, 0, 5000, 5000), Container);
        Assert.Equal(Container.Width, huge.Width, 3);
        Assert.Equal(Container.Height, huge.Height, 3);
    }

    [Fact]
    public void ClampReplacesNonFiniteRectanglesWithSafeOnes()
    {
        var rect = MdiLayoutEngine.Clamp(new MdiRect(double.NaN, double.PositiveInfinity, double.NaN, -1), Container);
        Assert.True(rect.IsValid);
        AssertInside(rect, Container);
    }

    [Fact]
    public void MinimizedChipsStartOnTheReadingSideAlongTheBottomEdgeAndWrapUpwards()
    {
        var first = MdiLayoutEngine.MinimizedSlot(0, Container, rightToLeft: true);
        var second = MdiLayoutEngine.MinimizedSlot(1, Container, rightToLeft: true);
        Assert.Equal(Container.Right, first.Right, 3);
        Assert.True(second.Right < first.X);
        Assert.Equal(Container.Bottom, first.Bottom, 3);
        Assert.Equal(0, MdiLayoutEngine.MinimizedSlot(0, Container, rightToLeft: false).X, 3);

        var narrow = new MdiRect(0, 0, MdiLayoutEngine.MinimizedWidth + 10, 400);
        var wrapped = MdiLayoutEngine.MinimizedSlot(1, narrow, rightToLeft: false);
        Assert.True(wrapped.Y < MdiLayoutEngine.MinimizedSlot(0, narrow, rightToLeft: false).Y);
        AssertInside(wrapped, narrow);
    }

    [Theory]
    [InlineData(0, 3, 1, 1)]
    [InlineData(2, 3, 1, 0)]
    [InlineData(0, 3, -1, 2)]
    [InlineData(1, 3, -1, 0)]
    [InlineData(-1, 3, 1, 0)]
    [InlineData(-1, 3, -1, 2)]
    [InlineData(0, 0, 1, -1)]
    public void NeighborWrapsAround(int index, int count, int direction, int expected) =>
        Assert.Equal(expected, MdiLayoutEngine.Neighbor(index, count, direction));
}
