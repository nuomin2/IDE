using System.Windows.Media.Imaging;

namespace Pytools.Models
{
    public class PlotModel
    {
        public BitmapImage ImageSource { get; set; } = new();
        public string Title { get; set; } = "";
        public byte[] RawImageData { get; set; } = Array.Empty<byte>();
    }
}
