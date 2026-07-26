using ABCPClient.Infrastructure.Api;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет, как кэш изображений определяет адрес загрузки и имя файла.
/// </summary>
/// <remarks>
/// Источников два: API отдаёт имя файла на CDN платформы, а выгрузка каталога —
/// полный адрес на другом хосте.
/// </remarks>
public sealed class ProductImageCacheTests
{
    [Fact]
    public void Api_image_name_is_taken_from_platform_cdn()
    {
        Assert.True(ProductImageCache.TryResolve("09a73cde.jpeg", out string address, out string fileName));

        Assert.Equal(ProductImageCache.ImageBaseUrl + "09a73cde.jpeg", address);
        Assert.Equal("09a73cde.jpeg", fileName);
    }

    [Fact]
    public void Absolute_address_from_catalog_is_downloaded_as_is()
    {
        const string url = "https://pubimg.nodacdn.net/images/09a73cde2806ead97565.jpeg";

        Assert.True(ProductImageCache.TryResolve(url, out string address, out string fileName));

        Assert.Equal(url, address);

        // Имя файла берётся из хэша адреса: имена на разных хостах совпадают,
        // и в кэше они не должны затирать друг друга.
        Assert.EndsWith(".jpeg", fileName, StringComparison.Ordinal);
        Assert.DoesNotContain("nodacdn", fileName, StringComparison.Ordinal);
        Assert.Equal(Path.GetFileName(fileName), fileName);
    }

    [Fact]
    public void Same_address_always_maps_to_the_same_file()
    {
        const string url = "https://pubimg.nodacdn.net/images/one.JPEG";

        ProductImageCache.TryResolve(url, out _, out string first);
        ProductImageCache.TryResolve(url, out _, out string second);

        Assert.Equal(first, second);

        ProductImageCache.TryResolve("https://pubimg.nodacdn.net/images/two.JPEG", out _, out string other);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Path_traversal_in_api_name_stays_inside_cache()
    {
        Assert.True(ProductImageCache.TryResolve(@"..\..\windows\system32\evil.dll", out _, out string fileName));

        Assert.Equal("evil.dll", fileName);
    }

    [Theory]
    [InlineData("file:///C:/windows/system32/evil.dll")]
    [InlineData("ftp://example.com/pic.jpg")]
    public void Foreign_schemes_are_rejected(string value)
    {
        Assert.False(ProductImageCache.TryResolve(value, out _, out _));
    }
}
