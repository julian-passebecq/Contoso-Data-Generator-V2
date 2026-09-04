using DatabaseGenerator.Forge;
using DatabaseGenerator.Forge.Runtime;

namespace DatabaseGenerator.Tests;

public class ForgeEvidenceCommandTests
{
    [Fact]
    public async Task EvidenceImportRequiresExplicitInputAndOutputPaths()
    {
        Assert.Equal(2, await ForgeCommand.RunAsync(new[] { "evidence", "import", "--root", "missing" }));
    }

    [Fact]
    public async Task MissingEvidenceCannotStartVerifierOrCreateReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-evidence-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "report.json");
        await Assert.ThrowsAsync<ArgumentException>(() => ForgeEvidenceCommand.ImportAsync(root,
            Path.Combine(root, "order.json"), Path.Combine(root, "result.json"), output, "nonexistent-python"));
        Assert.False(File.Exists(output));
    }
}
