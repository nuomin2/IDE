using System.Text.Json.Serialization;

namespace Pytools.Models.Simulation
{
    public class LinkData
    {
        [JsonPropertyName("src")]
        public string Src { get; set; } = "";

        [JsonPropertyName("dst")]
        public string Dst { get; set; } = "";

        [JsonPropertyName("bw")]
        public int Bw { get; set; }

        [JsonPropertyName("delay")]
        public double Delay { get; set; }

        [JsonPropertyName("drop_rate")]
        public double DropRate { get; set; } = 0.0;

        [JsonPropertyName("max_queue_bytes")]
        public int MaxQueueBytes { get; set; } = 15360;

        [JsonPropertyName("max_timeout_ms")]
        public double MaxTimeoutMs { get; set; } = 50.0;
    }
}
