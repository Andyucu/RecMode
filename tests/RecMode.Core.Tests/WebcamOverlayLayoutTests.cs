using RecMode.Core.Recording;
using RecMode.Core.Settings;
using Xunit;

namespace RecMode.Core.Tests;

public class WebcamOverlayLayoutTests
{
    [Fact]
    public void BottomRight_SitsAtTheBottomRightCornerWithMargin()
    {
        (int x, int y, int w, int h) = WebcamOverlayLayout.ComputeRect(1920, 1080, 20, WebcamOverlayPosition.BottomRight);

        Assert.Equal(1920 * 20 / 100, w);
        Assert.Equal(w * 9 / 16, h);
        Assert.True(x + w < 1920);
        Assert.True(y + h < 1080);
        Assert.True(x > 1920 - w - w); // roughly hugging the right edge, not centered
    }

    [Fact]
    public void TopLeft_SitsAtTheOrigin()
    {
        (int x, int y, int _, int _) = WebcamOverlayLayout.ComputeRect(1920, 1080, 20, WebcamOverlayPosition.TopLeft);

        Assert.True(x > 0 && x < 100);
        Assert.True(y > 0 && y < 100);
    }

    [Theory]
    [InlineData(WebcamOverlayPosition.BottomLeft)]
    [InlineData(WebcamOverlayPosition.TopRight)]
    public void OtherCorners_StayWithinBounds(WebcamOverlayPosition position)
    {
        (int x, int y, int w, int h) = WebcamOverlayLayout.ComputeRect(1920, 1080, 25, position);

        Assert.True(x >= 0);
        Assert.True(y >= 0);
        Assert.True(x + w <= 1920);
        Assert.True(y + h <= 1080);
    }

    [Fact]
    public void SizePercent_IsClamped()
    {
        (int _, int _, int wTooSmall, int _) = WebcamOverlayLayout.ComputeRect(1920, 1080, 1, WebcamOverlayPosition.BottomRight);
        (int _, int _, int wTooBig, int _) = WebcamOverlayLayout.ComputeRect(1920, 1080, 99, WebcamOverlayPosition.BottomRight);

        Assert.Equal(1920 * 10 / 100, wTooSmall);
        Assert.Equal(1920 * 50 / 100, wTooBig);
    }

    [Fact]
    public void Dimensions_AreAlwaysEven()
    {
        (int _, int _, int w, int h) = WebcamOverlayLayout.ComputeRect(1921, 1081, 17, WebcamOverlayPosition.BottomRight);

        Assert.Equal(0, w % 2);
        Assert.Equal(0, h % 2);
    }
}
