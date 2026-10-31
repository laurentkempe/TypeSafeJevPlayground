using TypeSafe.Cli.IO;
using Xunit;

namespace TypeSafe.Cli.Tests;

public sealed class InputReaderTests
{
    [Fact]
    public async Task ReadStateAsync_DirectText_ReturnsProvidedText()
    {
        var text = "Hello world";
        var result = await InputReader.ReadStateAsync(text);
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task ReadStateAsync_FileReference_ReadsFileContent()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "File contents payload");
            var result = await InputReader.ReadStateAsync($"@{tempFile}");
            Assert.Equal("File contents payload", result);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ReadStateAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.json");
        await Assert.ThrowsAsync<FileNotFoundException>(() => InputReader.ReadStateAsync($"@{missingPath}"));
    }

    [Fact]
    public async Task ReadStateAsync_StdinHyphen_ReadsFromProvidedReader()
    {
        using var reader = new StringReader("Piped text from stream");
        var result = await InputReader.ReadStateAsync("-", stdin: reader);
        Assert.Equal("Piped text from stream", result);
    }

    [Fact]
    public async Task ReadStateAsync_OmittedArgumentWithRedirectedStdin_ReadsFromReader()
    {
        using var reader = new StringReader("Piped text without arg");
        var result = await InputReader.ReadStateAsync(null, stdin: reader, isInputRedirected: true);
        Assert.Equal("Piped text without arg", result);
    }

    [Fact]
    public async Task ReadStateAsync_EmptyStdin_ThrowsInvalidOperationException()
    {
        using var reader = new StringReader("   ");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InputReader.ReadStateAsync("-", stdin: reader));
    }

    [Fact]
    public async Task ReadStateAsync_NoArgAndNotRedirected_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            InputReader.ReadStateAsync(null, stdin: null, isInputRedirected: false));
    }
}
