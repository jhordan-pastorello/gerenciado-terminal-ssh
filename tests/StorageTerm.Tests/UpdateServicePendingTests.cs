using System.IO;
using StorageTerm.Services;

namespace StorageTerm.Tests;

public sealed class UpdateServicePendingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "storageterm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void MakeZip(string tag)
    {
        var dir = Path.Combine(_root, tag);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "StorageTerm-win-x64.zip"), "x");
    }

    [Fact]
    public void ReturnsNullWhenDirectoryMissing()
    {
        Assert.Null(new UpdateService(_root).TryLoadPendingDownload());
    }

    [Fact]
    public void ReturnsNewestVersionAboveCurrent()
    {
        MakeZip("v0.0.1");
        MakeZip("v99.0.1");
        MakeZip("v99.0.2");
        Directory.CreateDirectory(Path.Combine(_root, "v99.1.0")); // sem zip

        var result = new UpdateService(_root).TryLoadPendingDownload();

        Assert.NotNull(result);
        Assert.Equal("v99.0.2", result!.Value.Info.Tag);
        Assert.EndsWith("StorageTerm-win-x64.zip", result.Value.ZipPath);
    }

    [Fact]
    public void IgnoresOlderAndPartialDownloads()
    {
        MakeZip("v0.0.1");
        var dir = Path.Combine(_root, "v99.0.0");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "StorageTerm-win-x64.zip.part"), "x");

        Assert.Null(new UpdateService(_root).TryLoadPendingDownload());
    }
}
