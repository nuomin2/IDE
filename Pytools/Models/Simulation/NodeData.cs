using System.Text.Json.Serialization;

namespace Pytools.Models.Simulation
{
    public class NodeData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";
    }
}
