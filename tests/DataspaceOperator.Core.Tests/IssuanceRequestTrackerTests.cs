using DataspaceOperator.Core.Protocol;
using Xunit;

namespace DataspaceOperator.Core.Tests;

/// <summary>
/// Covers the wait that lets the operator offer several credential types to one holder in sequence.
/// </summary>
public class IssuanceRequestTrackerTests
{
    private const string HolderDid = "did:web:alice.example";

    [Fact]
    public async Task WaitForSettled_ReturnsOnceTheRequestedTypeIsIssued()
    {
        var tracker = new IssuanceRequestTracker();
        var since = DateTimeOffset.UtcNow;
        var wait = tracker.WaitForSettledAsync(HolderDid, "BpnCredential", since, TimeSpan.FromSeconds(5));

        var pending = tracker.Create("holder-pid", HolderDid, "BpnCredential");
        Assert.False(wait.IsCompleted);
        pending.State = IssuanceRequestTracker.RequestState.Issued;

        Assert.Same(pending, await wait);
    }

    [Fact]
    public async Task WaitForSettled_IgnoresOtherTypesAndEarlierRequests()
    {
        var tracker = new IssuanceRequestTracker();
        tracker.Create("old", HolderDid, "BpnCredential").State = IssuanceRequestTracker.RequestState.Issued;
        var since = DateTimeOffset.UtcNow.AddMilliseconds(1);
        await Task.Delay(5);
        tracker.Create("other", HolderDid, "MembershipCredential").State = IssuanceRequestTracker.RequestState.Issued;

        var result = await tracker.WaitForSettledAsync(HolderDid, "BpnCredential", since, TimeSpan.FromMilliseconds(600));

        Assert.Null(result);
    }
}
