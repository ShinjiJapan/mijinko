using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class ImageDownscaleTests
{
    [Fact]
    public void NoHalving_WhenSourceFitsTarget()
    {
        // 元が表示寸以下なら縮小不要。
        Assert.Equal(0, ImageDownscale.HalvingSteps(800, 600, 1920, 1080));
        Assert.Equal(0, ImageDownscale.HalvingSteps(1920, 1080, 1920, 1080));
    }

    [Fact]
    public void HalvesUntilJustAboveTarget()
    {
        // 4000幅を1000幅へ: /2=2000(>=1000)→ もう一度 /2=1000(>=1000)→ さらに/2=500(<1000で停止)。計2回。
        Assert.Equal(2, ImageDownscale.HalvingSteps(4000, 4000, 1000, 1000));
    }

    [Fact]
    public void StopsBeforeFallingBelowTarget_OnEitherAxis()
    {
        // 縦横どちらかが表示寸を下回るならそこで止める(横長を狭い枠へ収める想定)。
        // 4000x1000 を 1000x1000 へ: /2=2000x500。高さ500<1000 なので 0 回で止まる。
        Assert.Equal(0, ImageDownscale.HalvingSteps(4000, 1000, 1000, 1000));
    }

    [Fact]
    public void ZeroOrNegativeTarget_ReturnsZero()
    {
        // 表示寸が未確定(レイアウト前=0)なら縮小しない(原寸のまま描画側へ委ねる)。
        Assert.Equal(0, ImageDownscale.HalvingSteps(4000, 4000, 0, 0));
        Assert.Equal(0, ImageDownscale.HalvingSteps(4000, 4000, -1, 500));
    }

    [Fact]
    public void LargeReduction_TakesManySteps()
    {
        // 8000幅を250幅へ: 4000,2000,1000,500,250 で5回(250>=250)、次は125<250で停止。
        Assert.Equal(5, ImageDownscale.HalvingSteps(8000, 8000, 250, 250));
    }
}
