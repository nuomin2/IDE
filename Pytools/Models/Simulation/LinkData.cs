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
    }
}
