using System.Text.Json.Serialization;

namespace Pytools.Models.Simulation
{
    public class DistributionData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("value")]
        public double Value { get; set; }

        [JsonPropertyName("mean")]
        public double Mean { get; set; }

        [JsonPropertyName("min_val")]
        public double MinVal { get; set; }

        [JsonPropertyName("max_val")]
        public double MaxVal { get; set; }
    }

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

        [JsonPropertyName("interval_dist")]
        public DistributionData IntervalDist { get; set; } = new();

        [JsonPropertyName("payload_dist")]
        public DistributionData PayloadDist { get; set; } = new();
    }
}
