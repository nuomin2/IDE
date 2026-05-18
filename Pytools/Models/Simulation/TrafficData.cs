using System.Text.Json.Serialization;

namespace Pytools.Models.Simulation
{
    public class TrafficData
    {
        [JsonPropertyName("src")]
        public string Src { get; set; } = "";

        [JsonPropertyName("dst")]
        public string Dst { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("interval_mean")]
        public double IntervalMean { get; set; }

        [JsonPropertyName("payload_mean")]
        public int PayloadMean { get; set; }

        [JsonPropertyName("payload_variance")]
        public double PayloadVariance { get; set; }
    }
}
