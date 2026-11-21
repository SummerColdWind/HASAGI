using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal sealed class CpuModel : IDisposable
{
    [DllImport("HasagiInference.dll", CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode, ExactSpelling=true)]
    private static extern IntPtr OpenModel(string path, [Out] byte[] error, UIntPtr size);
    [DllImport("HasagiInference.dll", CallingConvention=CallingConvention.Cdecl)]
    private static extern int Predict(IntPtr model, [In] float[] pixels, [Out] float[] logits, [Out] byte[] error, UIntPtr size);
    [DllImport("HasagiInference.dll", CallingConvention=CallingConvention.Cdecl)]
    private static extern void CloseModel(IntPtr model);
    private IntPtr handle;
    private readonly byte[] error = new byte[4096];

    internal CpuModel(string filename)
    {
        var path = Path.Combine(Path.GetDirectoryName(typeof(CpuModel).Assembly.Location), "models", filename);
        handle = OpenModel(path, error, (UIntPtr)error.Length);
        if (handle == IntPtr.Zero) throw Failure();
    }
    private Exception Failure() {
        return new InvalidOperationException(Encoding.UTF8.GetString(error).TrimEnd('\0').Split('\0')[0]);
    }
    internal double[] Run(float[] input)
    {
        var logits = new float[4];
        if (Predict(handle, input, logits, error, (UIntPtr)error.Length) != 0) throw Failure();
        if (logits.Any(x => float.IsNaN(x) || float.IsInfinity(x))) throw new InvalidDataException("Non-finite model output.");
        return logits.Select(x => (double)x).ToArray();
    }
    public void Dispose() { if (handle != IntPtr.Zero) { CloseModel(handle); handle = IntPtr.Zero; } }
}

internal static class Analysis
{
    internal static readonly string[] Fields = {
        "absolute_path", "relative_path", "sbnp", "epithelium", "stroma", "other",
        "discernibility_grade", "discernibility_score", "status", "error_detail",
        "p_sbnp", "p_epithelium", "p_stroma", "p_other", "p_poor", "p_fair", "p_good", "p_excellent"
    };
    private static readonly string[] Grades = { "poor", "fair", "good", "excellent" };
    private const int Side = 384;
    private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };
    private static readonly float[][] Channels = new[] { 0.485f, 0.456f, 0.406f }
        .Select((mean,c) => Enumerable.Range(0,256).Select(i => (i/255f - mean) / Std[c]).ToArray()).ToArray();

    private static float[] ReadInput(string path)
    {
        using (var file = File.OpenRead(path)) {
            var decoder = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            if (decoder.Frames.Count != 1) throw new InvalidDataException("Expected a single-frame image.");
            var frame = decoder.Frames[0];
            if (frame.PixelWidth != Side || frame.PixelHeight != Side)
                throw new InvalidDataException("Expected 384 x 384 pixels; received " + frame.PixelWidth + " x " + frame.PixelHeight + ".");
            const int n = Side*Side;
            bool gray = frame.Format == PixelFormats.Gray8;
            bool gray16 = frame.Format == PixelFormats.Gray16;
            var source = gray || gray16 ? (BitmapSource)frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = gray ? Side : (gray16 ? 2*Side : Side*4);
            var bytes = new byte[stride*Side];
            source.CopyPixels(bytes, stride, 0);
            var result = new float[3*n];
            for (int i=0; i<n; i++) {
                // Same fixed-point luminance coefficients as Pillow's RGB -> L.
                int g;
                if (gray) g=bytes[i];
                else if (gray16) g=Math.Min(255,bytes[2*i] | bytes[2*i+1]<<8);
                else g=(19595*bytes[4*i+2] + 38470*bytes[4*i+1] + 7471*bytes[4*i] + 32768) >> 16;
                for (int c=0;c<3;c++) result[c*n+i] = Channels[c][g];
            }
            return result;
        }
    }

    internal static object[] Process(string path, string rootPrefix, CpuModel layers, CpuModel grade)
    {
        var row = new object[Fields.Length];
        row[0] = path;
        row[1] = path.Substring(rootPrefix.Length);
        string status = "read_error";
        try {
            var pixels = ReadInput(path);
            status = "layer_error";
            var p = layers.Run(pixels).Select(x => 1.0/(1.0+Math.Exp(-x))).ToArray();
            var labels = p.Select(x => x >= 0.5 ? 1 : 0).ToArray();
            if (labels.Sum() == 0) labels[3] = 1;
            for (int i=0;i<4;i++) { row[2+i]=labels[i]; row[10+i]=p[i]; }
            if (labels[0] == 1 && labels.Sum() == 1) {
                status = "grade_error";
                var logits = grade.Run(pixels);
                double max = logits.Max();
                var weights = logits.Select(x => Math.Exp(x-max)).ToArray();
                double sum = weights.Sum();
                var probabilities = weights.Select(x => x/sum).ToArray();
                for (int i=0;i<4;i++) row[14+i] = probabilities[i];
                row[6] = Grades[Array.IndexOf(logits,max)];
                row[7] = 100.0*(probabilities[1]+2*probabilities[2]+3*probabilities[3])/3.0;
            } else row[6] = "not_applicable";
            row[8] = "ok";
        } catch (Exception ex) { row[8] = status; row[9] = ex.Message; }
        return row;
    }
}
