using ElasticMachines.Engine.Models;
using Xunit;

namespace ElasticMachines.Engine.Tests.Models;

public class ConfigurationTests
{
    public ConfigurationTests()
    {
    }

    [Fact]
    public void ValidName()
    {
        _ = new Configuration
        {
            Name = "abc-XYZ_123",
            Url = new("https://localhost"),
        };
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a.b")]
    public void InvalidName(string name)
    {
        Assert.Throws<Exception>(() => new Configuration
        {
            Name = name,
            Url = new("https://localhost"),
        });
    }

    [Fact]
    public void ConvertList()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new List<object> { 1, 2, 3 }
        };

        var with = configuration.WithAs<List<int>>();

        Assert.Equal(new List<int> { 1, 2, 3 }, with);
    }

    [Fact]
    public void ConvertDictionary()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object> { { "a", 1 }, { "b", 2 }, }
        };

        var with = configuration.WithAs<Dictionary<string, int>>();

        Assert.Equal(new Dictionary<string, int> { { "a", 1 }, { "b", 2 }, }, with);
    }

    [Fact]
    public void ConvertCustomType()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object>
            {
                ["boolProp"] = "true",
                ["intProp"] = "1",
                ["doubleProp"] = "1.2",
                ["stringProp"] = "foo",
                ["objectProp"] = "bar",
                ["listBoolProp"] = new List<object> { "true", "false" },
                ["listStringProp"] = new List<object> { "foo", "bar" },
                ["inceptionProp"] = new Dictionary<object, object>
                {
                    ["intProp"] = "2",
                    ["doubleProp"] = "2.3",
                },
            }
        };

        var with = configuration.WithAs<TestConfiguration>();

        Assert.Equal(true, with.BoolProp);
        Assert.Equal(1, with.IntProp);
        Assert.Equal(1.2, with.DoubleProp);
        Assert.Equal("foo", with.StringProp);
        Assert.Equal("bar", with.ObjectProp);
        Assert.Equal(new List<bool> { true, false }, with.ListBoolProp);
        Assert.Equal(new List<string> { "foo", "bar" }, with.ListStringProp);
        Assert.Equal(2, with.InceptionProp?.IntProp);
        Assert.Equal(2.3, with.InceptionProp?.DoubleProp);
    }

    [Fact]
    public void ConvertCustomTypeWithNullValues()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object?>
            {
                ["boolProp"] = null,
                ["intProp"] = null,
                ["doubleProp"] = null,
                ["stringProp"] = null,
                ["objectProp"] = null,
                ["listBoolProp"] = null,
                ["listStringProp"] = null,
                ["inceptionProp"] = null,
            }
        };

        var with = configuration.WithAs<TestConfiguration>();

        Assert.Null(with.BoolProp);
        Assert.Null(with.IntProp);
        Assert.Null(with.DoubleProp);
        Assert.Null(with.StringProp);
        Assert.Null(with.ObjectProp);
        Assert.Null(with.ListBoolProp);
        Assert.Null(with.ListStringProp);
        Assert.Null(with.InceptionProp);
    }

    [Fact]
    public void ConvertCustomTypeWithMissingValues()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object> { ["nonexistent"] = "foo" }
        };

        var with = configuration.WithAs<TestConfiguration>();

        Assert.Null(with.BoolProp);
        Assert.Null(with.IntProp);
        Assert.Null(with.DoubleProp);
        Assert.Null(with.StringProp);
        Assert.Null(with.ObjectProp);
        Assert.Null(with.ListBoolProp);
        Assert.Null(with.ListStringProp);
        Assert.Null(with.InceptionProp);
    }

    [Fact]
    public void ConvertWithPositionalMinimum()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object>
            {
                ["requiredProp"] = "required"
            }
        };

        var with = configuration.WithAs<TestPositionalConfiguration>();

        Assert.NotNull(with);
        Assert.Equal("required", with.RequiredProp);
        Assert.Null(with.OptionalProp);
        Assert.Equal("default_value", with.DefaultProp);
    }

    [Fact]
    public void ConvertWithPositionalFull()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object>
            {
                ["requiredProp"] = "required",
                ["defaultProp"] = "non_default_value",
                ["optionalProp"] = "optional_value",
            }
        };

        var with = configuration.WithAs<TestPositionalConfiguration>();

        Assert.NotNull(with);
        Assert.Equal("required", with.RequiredProp);
        Assert.Equal("optional_value", with.OptionalProp);
        Assert.Equal("non_default_value", with.DefaultProp);
    }

    [Fact]
    public void ConvertWithConstructorMinimum()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object>
            {
                ["requiredProp"] = "required"
            }
        };

        var with = configuration.WithAs<TestConstructorConfiguration>();

        Assert.NotNull(with);
        Assert.Equal("required", with.RequiredProp);
        Assert.Null(with.OptionalProp);
        Assert.Equal("default_value", with.DefaultProp);
    }

    [Fact]
    public void ConvertWithConstructorFull()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object>
            {
                ["requiredProp"] = "required",
                ["defaultProp"] = "non_default_value",
                ["optionalProp"] = "optional_value",
            }
        };

        var with = configuration.WithAs<TestConstructorConfiguration>();

        Assert.NotNull(with);
        Assert.Equal("required", with.RequiredProp);
        Assert.Equal("optional_value", with.OptionalProp);
        Assert.Equal("non_default_value", with.DefaultProp);
    }

    [Fact]
    public void ConvertWithConstructorCaseInsensitive()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object>
            {
                ["requiredPROP"] = "required",
                ["defaultPROP"] = "non_default_value",
                ["optionalPROP"] = "optional_value",
            }
        };

        var with = configuration.WithAs<TestConstructorConfiguration>();

        Assert.NotNull(with);
        Assert.Equal("required", with.RequiredProp);
        Assert.Equal("optional_value", with.OptionalProp);
        Assert.Equal("non_default_value", with.DefaultProp);
    }

    [Fact]
    public void ConvertWithConstructorMissingParameters()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object>()
        };

        Assert.Throws<Exception>(() => configuration.WithAs<TestConstructorConfiguration>());
    }

    [Fact]
    public void ConvertWithConstructorInvalidParameters()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            With = new Dictionary<object, object?>
            {
                ["requiredProp"] = null,
            }
        };

        Assert.Throws<ArgumentException>(() => configuration.WithAs<TestConstructorConfiguration>());
    }

    [Fact]
    public void CurrentScaleReturnsFirstMatchingProperties()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            Scale = new()
            {
                new()
                {
                    MinimumIdleMachines = 1,
                    MaximumIdleMachines = 1,
                    MaximumMachines = 1,
                    MinimumMachines = 1,
                },
                new()
                {
                    MinimumIdleMachines = 2,
                    MaximumIdleMachines = 2,
                    MaximumMachines = 2,
                    MinimumMachines = 2,
                },
            }
        };

        var currentScale = configuration.GetCurrentScale();

        Assert.Equal(1, currentScale.MinimumIdleMachines);
        Assert.Equal(1, currentScale.MaximumIdleMachines);
        Assert.Equal(1, currentScale.MaximumMachines);
        Assert.Equal(1, currentScale.MinimumMachines);
    }

    [Fact]
    public void CurrentScaleReturnsFirstDefinedProperties()
    {
        var configuration = new Configuration
        {
            Name = "test",
            Url = new("https://localhost"),
            Scale = new()
            {
                new(),
                new()
                {
                    MinimumIdleMachines = 2,
                    MaximumIdleMachines = 2,
                    MaximumMachines = 2,
                    MinimumMachines = 2,
                },
            }
        };

        var currentScale = configuration.GetCurrentScale();

        Assert.Equal(2, currentScale.MinimumIdleMachines);
        Assert.Equal(2, currentScale.MaximumIdleMachines);
        Assert.Equal(2, currentScale.MaximumMachines);
        Assert.Equal(2, currentScale.MinimumMachines);
    }

    private record TestConfiguration
    {
        public bool? BoolProp { get; set; }
        public int? IntProp { get; set; }
        public double? DoubleProp { get; set; }
        public string? StringProp { get; set; }
        public object? ObjectProp { get; set; }
        public List<bool>? ListBoolProp { get; set; }
        public List<string>? ListStringProp { get; set; }
        public TestConfiguration? InceptionProp { get; set; }
    }

    private record TestPositionalConfiguration(string RequiredProp)
    {
        public string DefaultProp { get; init; } = "default_value";
        public string? OptionalProp { get; set; }
    }

    private class TestConstructorConfiguration
    {
        public string RequiredProp { get; init; }
        public string DefaultProp { get; init; }
        public string? OptionalProp { get; init; }

        public TestConstructorConfiguration(string requiredProp, string defaultProp = "default_value", string? optionalProp = null)
        {
            RequiredProp = requiredProp;
            DefaultProp = defaultProp;
            OptionalProp = optionalProp;
        }
    }
}
