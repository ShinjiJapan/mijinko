using Filer.Core;
using static Filer.Core.SearchIpcProtocol;

namespace Filer.Core.Tests;

public sealed class SearchIpcProtocolTests
{
    // ---- フレーム往復 ----

    [Fact]
    public async Task Frame_RoundTrips_TypeAndPayload()
    {
        using var ms = new MemoryStream();
        await WriteMessageAsync(ms, MessageType.Request, new byte[] { 1, 2, 3 });
        await WriteMessageAsync(ms, MessageType.Cancel, ReadOnlyMemory<byte>.Empty);
        ms.Position = 0;

        var a = await ReadMessageAsync(ms);
        var b = await ReadMessageAsync(ms);

        Assert.NotNull(a);
        Assert.Equal(MessageType.Request, a!.Type);
        Assert.Equal(new byte[] { 1, 2, 3 }, a.Payload);
        Assert.NotNull(b);
        Assert.Equal(MessageType.Cancel, b!.Type);
        Assert.Empty(b.Payload);
    }

    [Fact]
    public async Task ReadMessage_ReturnsNull_AtEndOfStream()
    {
        using var ms = new MemoryStream();
        Assert.Null(await ReadMessageAsync(ms));
    }

    [Fact]
    public void SyncWrite_IsReadableByAsyncRead()
    {
        using var ms = new MemoryStream();
        WriteMessage(ms, MessageType.Batch, new byte[] { 9 });
        ms.Position = 0;
        var msg = ReadMessageAsync(ms).GetAwaiter().GetResult();
        Assert.Equal(MessageType.Batch, msg!.Type);
        Assert.Equal(new byte[] { 9 }, msg.Payload);
    }

    // ---- DTO 往復 ----

    [Fact]
    public void RequestDto_RoundTrips_Json()
    {
        var dto = new SearchRequestDto("*.md", @"C:\work", UseRegex: true,
            IncludeFiles: true, IncludeDirectories: false, SearchArchives: true);
        var back = FromJsonUtf8<SearchRequestDto>(ToJsonUtf8(dto));
        Assert.Equal(dto, back);
    }

    [Fact]
    public void FileEntryDtoArray_RoundTrips_Json()
    {
        var dtos = new[]
        {
            new FileEntryDto("a.txt", @"C:\a.txt", false, 12, new DateTime(2024, 1, 2, 3, 4, 5), false),
            new FileEntryDto("z.zip", @"C:\z.zip", false, 99, new DateTime(2023, 6, 7, 8, 9, 10), true),
        };
        var back = FromJsonUtf8<FileEntryDto[]>(ToJsonUtf8(dtos));
        Assert.Equal(dtos, back);
    }

    // ---- DTO ↔ ドメイン型 ----

    [Fact]
    public void Options_To_RequestDto_And_Back_PreservesCarriedFields_AndPrefersMft()
    {
        var options = new FileSearchOptions("rep", @"D:\src")
        {
            UseRegex = false,
            IncludeFiles = true,
            IncludeDirectories = true,
            SearchArchives = false,
            PreferMft = false,   // 転送されず、ヘルパー側では常に true になる
        };

        var restored = SearchRequestDto.FromOptions(options).ToOptions();

        Assert.Equal(options.Pattern, restored.Pattern);
        Assert.Equal(options.BaseDirectory, restored.BaseDirectory);
        Assert.Equal(options.UseRegex, restored.UseRegex);
        Assert.Equal(options.IncludeFiles, restored.IncludeFiles);
        Assert.Equal(options.IncludeDirectories, restored.IncludeDirectories);
        Assert.Equal(options.SearchArchives, restored.SearchArchives);
        Assert.True(restored.PreferMft);   // ヘルパーは管理者権限なので MFT 優先
    }

    [Fact]
    public void FileEntry_To_Dto_And_Back_PreservesAllFields()
    {
        var entry = new FileEntry("docs\\g.zip", @"C:\base\docs\g.zip", false, 42,
            new DateTime(2025, 3, 4, 5, 6, 7)) { IsArchive = true };

        var back = FileEntryDto.FromEntry(entry).ToEntry();

        Assert.Equal(entry, back);
        Assert.True(back.IsArchive);
    }
}
