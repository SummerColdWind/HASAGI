using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

internal sealed class BatchJob
{
    private static readonly HashSet<string> Extensions = new HashSet<string>(
        new[] { ".png", ".bmp", ".tif", ".tiff", ".jpeg", ".jpg" }, StringComparer.OrdinalIgnoreCase);
    internal int Processed { get; private set; }
    internal int Errors { get; private set; }
    internal int Total { get; private set; }
    internal string Saved { get; private set; }
    internal bool Partial { get { return Saved != null && Processed < Total; } }
    private readonly Action<int> found;
    private readonly Action<int,int,int> progress;
    internal BatchJob(Action<int> found, Action<int,int,int> progress)
    {
        this.found = found;
        this.progress = progress;
    }

    private static IEnumerable<string> FindImages(string root, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0) {
            var folder = pending.Pop();
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos()) {
                token.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.Directory) != 0) {
                    // Skip nested directory links to avoid cycles and traversing outside the batch.
                    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) pending.Push(entry.FullName);
                } else if (Extensions.Contains(entry.Extension)) yield return entry.FullName;
            }
        }
    }

    private IEnumerable<object[]> Rows(string[] files, string prefix, CpuModel layers, CpuModel grades, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        foreach (var file in files) {
            if (token.IsCancellationRequested) break;
            var row = Analysis.Process(file, prefix, layers, grades);
            if ((string)row[8] != "ok") Errors++;
            Processed++;
            yield return row;
            if (timer.ElapsedMilliseconds >= 1000 || Processed == files.Length) {
                progress(Processed, Total, Errors);
                timer.Restart();
            }
        }
    }

    internal int Run(string input, string output, CancellationToken token, string reportName = "HASAGI_results")
    {
        var files = FindImages(input, token).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        token.ThrowIfCancellationRequested();
        Total = files.Length;
        found(Total);
        if (files.Length == 0) return 4;
        Directory.CreateDirectory(output);
        var stem = Path.Combine(output, reportName);
        var prefix = input.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (var layers = new CpuModel("layers_fp32.onnx"))
        using (var grades = new CpuModel("discernibility_fp32.onnx")) {
            token.ThrowIfCancellationRequested();
            Saved = Report.SavePair(stem, Rows(files, prefix, layers, grades, token), () => Processed < Total);
        }
        if (Partial) return 130;
        return Errors == 0 ? 0 : 3;
    }
}
