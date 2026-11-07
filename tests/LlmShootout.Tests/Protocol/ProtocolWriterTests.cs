using LlmShootout.Arena.Protocol;

namespace LlmShootout.Tests.Protocol;

public sealed class ProtocolWriterTests
{
    [Fact]
    public async Task Protocol_writer_appends_lines_that_survive_reopening()
    {
        var dir = TempDirectory.Create("tests");
        try
        {
            await using (var writer = new ProtocolWriter(dir))
            {
                await writer.AppendAsync(new MatchAbortedEvent(DateTimeOffset.UnixEpoch, "r", "d"), TestContext.Current.CancellationToken);
            }

            await using (var writer = new ProtocolWriter(dir))
            {
                await writer.AppendAsync(new MatchAbortedEvent(DateTimeOffset.UnixEpoch, "r2", "d2"), TestContext.Current.CancellationToken);
            }

            Assert.Equal(["r", "r2"], ProtocolReader.ReadAll(dir).OfType<MatchAbortedEvent>().Select(e => e.Reason));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
