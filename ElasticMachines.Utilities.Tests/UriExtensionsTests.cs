using Xunit;

namespace ElasticMachines.Utilities.Tests;

public class UriExtensionsTests
{
    [Fact]
    public void UnmodifiedEmptyRelative()
    {
        var baseUri = new Uri("https://localhost/a");

        var result = baseUri.Combine("");

        Assert.Equal(baseUri, result);
    }

    [Fact]
    public void ReplaceFullPath()
    {
        var baseUri = new Uri("https://localhost/b");

        var result = baseUri.Combine("/a");

        Assert.Equal(new Uri("https://localhost/a"), result);
    }

    [Fact]
    public void AppendRelativePath()
    {
        var baseUri = new Uri("https://localhost/a");

        var result = baseUri.Combine("b");

        Assert.Equal(new Uri("https://localhost/a/b"), result);
    }

    [Fact]
    public void AppendRelativePathToTrailingSlash()
    {
        var baseUri = new Uri("https://localhost/a/");

        var result = baseUri.Combine("b");

        Assert.Equal(new Uri("https://localhost/a/b"), result);
    }

    [Fact]
    public void PreserveQuery()
    {
        var baseUri = new Uri("https://localhost?a=b");

        var result = baseUri.Combine("");

        Assert.Equal(new Uri("https://localhost?a=b"), result);
    }

    [Fact]
    public void AppendQuery()
    {
        var baseUri = new Uri("https://localhost");

        var result = baseUri.Combine("?a=b");

        Assert.Equal(new Uri("https://localhost?a=b"), result);
    }

    [Fact]
    public void ReplaceQuery()
    {
        var baseUri = new Uri("https://localhost?a=c");

        var result = baseUri.Combine("?a=b");

        Assert.Equal(new Uri("https://localhost?a=b"), result);
    }

    [Fact]
    public void PreserveFragment()
    {
        var baseUri = new Uri("https://localhost#a");

        var result = baseUri.Combine("");

        Assert.Equal(new Uri("https://localhost#a"), result);
    }

    [Fact]
    public void AppendFragment()
    {
        var baseUri = new Uri("https://localhost");

        var result = baseUri.Combine("#a");

        Assert.Equal(new Uri("https://localhost#a"), result);
    }

    [Fact]
    public void ReplaceFragment()
    {
        var baseUri = new Uri("https://localhost#b");

        var result = baseUri.Combine("#a");

        Assert.Equal(new Uri("https://localhost#a"), result);
    }
}
