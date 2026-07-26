using ABCPClient.Domain.Models;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет разбор и сравнение версий приложения.
/// </summary>
public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.0.0", 1, 0, 0, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("2.10.0-beta.1", 2, 10, 0, "beta.1")]
    [InlineData("1.0.0+722f75a", 1, 0, 0, null)]
    [InlineData("1.0.0-rc.1+abc", 1, 0, 0, "rc.1")]
    [InlineData("1.0", 1, 0, 0, null)]
    public void Versions_are_parsed(string value, int major, int minor, int patch, string? prerelease)
    {
        Assert.True(AppVersion.TryParse(value, out AppVersion? version));

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(prerelease, version.Prerelease);
    }

    [Fact]
    public void Fourth_part_from_file_version_is_ignored()
    {
        // .NET подставляет четвёртую часть в FileVersion, а в тегах её нет.
        Assert.True(AppVersion.TryParse("1.0.0.0", out AppVersion? version));
        Assert.Equal("1.0.0", version.Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("не версия")]
    [InlineData("1")]
    [InlineData("1.x.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.2.3.4.5")]
    public void Rubbish_is_rejected(string? value) => Assert.False(AppVersion.TryParse(value, out _));

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.1.0", "1.0.9")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.0.0", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1")]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.9")]
    [InlineData("1.0.0-rc", "1.0.0-beta")]
    [InlineData("1.0.0-beta.1", "1.0.0-beta")]
    public void Newer_version_is_greater(string newer, string older)
    {
        AppVersion left = AppVersion.Parse(newer);
        AppVersion right = AppVersion.Parse(older);

        Assert.True(left > right, $"{newer} должна быть новее {older}");
        Assert.True(right < left);
        Assert.True(left != right);
    }

    [Fact]
    public void Build_metadata_does_not_affect_order()
    {
        // Хэш коммита, который дописывает .NET, версией не является.
        Assert.Equal(AppVersion.Parse("1.0.0"), AppVersion.Parse("1.0.0+722f75a"));
        Assert.False(AppVersion.Parse("1.0.0+aaa") > AppVersion.Parse("1.0.0+bbb"));
    }

    [Fact]
    public void Same_version_is_not_an_update()
    {
        AppVersion current = AppVersion.Parse("1.0.0");

        Assert.True(AppVersion.Parse("1.0.0") <= current);
        Assert.True(AppVersion.Parse("0.9.9") <= current);
        Assert.False(AppVersion.Parse("1.0.0-beta") > current);
    }

    [Fact]
    public void Prerelease_is_marked()
    {
        Assert.True(AppVersion.Parse("1.0.0-beta.1").IsPrerelease);
        Assert.False(AppVersion.Parse("1.0.0").IsPrerelease);
    }

    [Fact]
    public void Display_keeps_prerelease_and_drops_metadata()
    {
        Assert.Equal("1.2.3", AppVersion.Parse("v1.2.3+abcdef").Display);
        Assert.Equal("1.2.3-rc.1", AppVersion.Parse("1.2.3-rc.1+abcdef").Display);
    }
}
