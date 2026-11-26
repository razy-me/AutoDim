using Xunit;
using AutoDim.Core;

namespace AutoDim.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void AppName_IsAutoDim()
    {
        Assert.Equal("AutoDim", AutoDimInfo.AppName);
    }
}
