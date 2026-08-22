namespace ElasticMachines.Engine;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class CloudProviderAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
