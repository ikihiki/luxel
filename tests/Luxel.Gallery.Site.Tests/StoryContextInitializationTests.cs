using Luxel.Gallery;

namespace Luxel.Gallery.Site.Tests;

public sealed class StoryContextInitializationTests
{
    [Fact]
    public async Task ReadyWaitsForEveryRegisteredInitialization()
    {
        using var context = new StoryContext();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Initialize(first.Task);
        context.Initialize(second.Task);

        Task ready = context.Ready;
        first.SetResult();
        Assert.False(ready.IsCompleted);

        second.SetResult();
        await ready;
    }

    [Fact]
    public void InitializeRejectsRegistrationAfterReadyWasObserved()
    {
        using var context = new StoryContext();
        Assert.True(context.Ready.IsCompletedSuccessfully);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => context.Initialize(Task.CompletedTask));
        Assert.Contains("after Ready", error.Message, StringComparison.Ordinal);
    }
}
