namespace Pytools.Models.Simulation
{
    public class SimEvent
    {
        public string Type { get; set; } = "";
        public string SrcId { get; set; } = "";
        public string DstId { get; set; } = "";
        public string CurrentNodeId { get; set; } = "";
        public int PayloadSize { get; set; }
        public double IntervalMean { get; set; }
        public double PayloadMean { get; set; }
        public double PayloadVariance { get; set; }
        public double CreationTime { get; set; }
        public string FromNodeId { get; set; } = "";
    }
}
