namespace LoopbackTranscriber.Tests.Infrastructure;

public static class Eventually
{
    public static async Task Until(Func<bool> condition, string because, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting until " + because);
            }

            await Task.Delay(10);
        }
    }
}
