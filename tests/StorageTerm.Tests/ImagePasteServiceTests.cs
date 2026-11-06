using System.IO;
using StorageTerm.Services;

namespace StorageTerm.Tests;

public class ImagePasteServiceTests
{
    [Fact]
    public void FilterImages_KeepsOnlyExistingImageFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "StorageTermTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var png = Path.Combine(dir, "a.png");
            var jpg = Path.Combine(dir, "b.JPG");
            var text = Path.Combine(dir, "c.txt");
            var missing = Path.Combine(dir, "d.png");

            File.WriteAllBytes(png, new byte[] { 1 });
            File.WriteAllBytes(jpg, new byte[] { 2 });
            File.WriteAllText(text, "x");

            var result = ImagePasteService.FilterImages(new[] { png, jpg, text, missing });

            Assert.Equal(new[] { png, jpg }, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("foto.png", true)]
    [InlineData("foto.JPEG", true)]
    [InlineData("foto.webp", true)]
    [InlineData("foto.txt", false)]
    [InlineData("foto", false)]
    public void IsImageFile_ChecksExtensionAndExistence(string name, bool expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "StorageTermTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var path = Path.Combine(dir, name);
            if (Path.HasExtension(name)) File.WriteAllBytes(path, new byte[] { 1 });

            Assert.Equal(expected, ImagePasteService.IsImageFile(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
