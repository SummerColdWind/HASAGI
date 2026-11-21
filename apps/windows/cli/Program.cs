using System;
using System.IO;
using System.Text;
using System.Threading;

internal sealed class Program
{
    private const string Help = @"Usage: HASAGI-cli.exe INPUT_FOLDER [-o OUTPUT_FOLDER]

Recursively analyze single-frame 384 x 384 PNG, BMP, JPEG and TIFF images
with both CPU ONNX models. Write HASAGI_results.csv and HASAGI_results.xlsx
to the input folder by default. Existing reports are preserved.

  -o, --output-dir DIR    Destination directory (created if needed)
  -h, --help              Print this help

stdout: absolute CSV and XLSX paths, one per line. Progress goes to stderr.
Ctrl+C: stop after the current image and save completed rows as *_partial.
Exit codes: 0 complete, 1 run failed, 2 invalid arguments, 3 image errors,
            4 no supported files, 130 interrupted.";
    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Usage: HASAGI-cli.exe INPUT_FOLDER [-o OUTPUT_FOLDER]");
        return 2;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h" || args[0] == "/?")) {
            Console.WriteLine(Help);
            return 0;
        }
        string input = null, output = null;
        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];
            if (arg == "-o" || arg == "--output-dir") {
                if (output != null || ++i == args.Length || args[i].StartsWith("-") || string.IsNullOrWhiteSpace(args[i]))
                    return UsageError("Expected one output folder after " + arg + ".");
                output = args[i];
            } else if (arg.StartsWith("-") || input != null || string.IsNullOrWhiteSpace(arg)) {
                return UsageError("Unexpected argument: " + arg);
            } else input = arg;
        }
        if (input == null) return UsageError("An input folder is required.");
        using (var cancellation = new CancellationTokenSource()) {
            ConsoleCancelEventHandler stop = (s,e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += stop;
            try {
                if (!Directory.Exists(input)) return UsageError("Input folder does not exist: " + input);
                var job = new BatchJob(n => Console.Error.WriteLine("Found {0} images.", n),
                    (n,total,errors) => Console.Error.WriteLine("Processed {0}/{1} ({2} errors)", n,total,errors));
                int code = job.Run(Path.GetFullPath(input), Path.GetFullPath(output ?? input), cancellation.Token);
                if (job.Saved != null) {
                    Console.WriteLine(job.Saved + ".csv");
                    Console.WriteLine(job.Saved + ".xlsx");
                }
                if (job.Partial) Console.Error.WriteLine("Stopped. Saved {0}/{1} rows.", job.Processed, job.Total);
                return code;
            } catch (OperationCanceledException) {
                Console.Error.WriteLine("Interrupted before processing.");
                return 130;
            } catch (Exception ex) {
                Console.Error.WriteLine("Analysis failed: " + ex.Message);
                return 1;
            } finally {
                Console.CancelKeyPress -= stop;
            }
        }
    }
}
