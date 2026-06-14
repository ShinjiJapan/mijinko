using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class FileSearchInteractionTests
{
    [Fact]
    public void 未検索状態のEnterは検索開始()
    {
        Assert.Equal(FileSearchEnterAction.StartSearch,
            FileSearchInteraction.DecideEnterAction(searchStarted: false));
    }

    [Fact]
    public void 検索を始めた後のEnterは転送して閉じる()
    {
        Assert.Equal(FileSearchEnterAction.Transfer,
            FileSearchInteraction.DecideEnterAction(searchStarted: true));
    }
}
