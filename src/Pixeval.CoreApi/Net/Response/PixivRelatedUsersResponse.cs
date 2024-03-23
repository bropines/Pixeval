using System.Text.Json.Serialization;
using Pixeval.CoreApi.Model;

namespace Pixeval.CoreApi.Net.Response;

[Factory]
public partial record PixivRelatedUsersResponse
{
    [JsonPropertyName("user_previews")]
    public required User[] Users { get; set; } = [];
}
