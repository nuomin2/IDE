using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pytools.Models.Simulation
{
    public class SimulationRequest
    {
        [JsonPropertyName("simulation_time")]
        public int SimulationTime { get; set; }

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        [JsonPropertyName("summary_only")]
        public bool SummaryOnly { get; set; }

        [JsonPropertyName("nodes")]
        public List<NodeData> Nodes { get; set; } = new();

        [JsonPropertyName("links")]
        public List<LinkData> Links { get; set; } = new();

        [JsonPropertyName("target_queue_link")]
        public string? TargetQueueLink { get; set; }

        [JsonPropertyName("traffic")]
        public List<TrafficData> Traffic { get; set; } = new();
    }
}
