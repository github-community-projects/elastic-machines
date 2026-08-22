using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace ElasticMachines.Monolith.Models;

[JsonConverter(typeof(JsonStringEnumMemberConverter))]
public enum InstallationAction
{
    [EnumMember(Value = "created")]
    Created,
    [EnumMember(Value = "deleted")]
    Deleted,
    [EnumMember(Value = "suspend")]
    Suspend,
    [EnumMember(Value = "unsuspend")]
    Unsuspend,
    [EnumMember(Value = "new_permissions_accepted")]
    NewPermissionsAccepted,
}
