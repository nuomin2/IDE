using System.Text.Json.Serialization;

namespace Pytools.Models.Simulation
{
    public class TrafficData
    {
        [JsonPropertyName("src")]
        public string Src { get; set; } = "";

        [JsonPropertyName("dst")]
        public string Dst { get; set; } = "";

        [JsonPropertyName("src_port")]
        public int SrcPort { get; set; }

        [JsonPropertyName("dst_port")]
        public int DstPort { get; set; }

        [JsonPropertyName("qos_level")]
        public int QosLevel { get; set; }

        [JsonPropertyName("flow_id")]
        public int FlowId { get; set; }

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
