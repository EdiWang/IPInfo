using IPInfo.Services;
using Xunit;

namespace IPInfo.Tests;

public sealed class DbAvailabilityLogStateTests
{
    [Fact]
    public void TryMarkUnavailable_ReturnsTrueOnlyForFirstUnavailableRequestUntilAvailable()
    {
        var state = new DbAvailabilityLogState();

        Assert.True(state.TryMarkUnavailable());
        Assert.False(state.TryMarkUnavailable());

        state.MarkAvailable();

        Assert.True(state.TryMarkUnavailable());
    }
}
