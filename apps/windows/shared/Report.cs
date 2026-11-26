using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

internal static class Report
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly Dictionary<string,string[]> Labels = new Dictionary<string,string[]> {
        { "HASAGI    /    图像分析结果", new[] { "HASAGI / 图像分析结果", "HASAGI / 影像分析結果", "HASAGI / Image analysis results" } },
        { "神经可辨识度   ·   仅纯 SBNP 评分   /   0–100 分，越高越清晰", new[] { "神经可辨识度 · 仅纯 SBNP 评分 / 0–100 分，越高越清晰", "神經可辨識度 · 僅純 SBNP 評分 / 0–100 分，越高越清晰", "Nerve discernibility · Pure SBNP only / 0–100, higher is clearer" } },
        { "模型输出   /   概率取值 0–1，空白表示该项未得到结果", new[] { "模型输出 / 概率 0–1，空白表示无结果", "模型輸出 / 機率 0–1，空白表示無結果", "Model output / Probabilities 0–1; blank means unavailable" } },
        { "图像路径   /   Image files", new[] { "图像路径", "影像路徑", "Image files" } },
        { "组织层面   /   Layers", new[] { "组织层面", "組織層面", "Layers" } },
        { "可辨识度   /   Discernibility", new[] { "可辨识度", "可辨識度", "Discernibility" } },
        { "处理记录   /   Processing", new[] { "处理记录", "處理紀錄", "Processing" } },
        { "层面概率   /   Layer probabilities", new[] { "层面概率", "層面機率", "Layer probabilities" } },
        { "分级概率   /   Grade probabilities", new[] { "分级概率", "分級機率", "Grade probabilities" } },
        { "absolute_path", new[] { "绝对路径", "絕對路徑", "Absolute path" } },
        { "relative_path", new[] { "相对路径", "相對路徑", "Relative path" } },
        { "sbnp", new[] { "SBNP", "SBNP", "SBNP" } },
        { "epithelium", new[] { "上皮", "上皮", "Epithelium" } },
        { "stroma", new[] { "基质", "基質", "Stroma" } },
        { "other", new[] { "其他", "其他", "Other" } },
        { "discernibility_grade", new[] { "可辨识度等级", "可辨識度等級", "Discernibility grade" } },
        { "discernibility_score", new[] { "可辨识度评分", "可辨識度評分", "Discernibility score" } },
        { "status", new[] { "状态", "狀態", "Status" } },
        { "error_detail", new[] { "错误详情", "錯誤詳情", "Error detail" } },
        { "p_sbnp", new[] { "SBNP 概率", "SBNP 機率", "SBNP probability" } },
        { "p_epithelium", new[] { "上皮概率", "上皮機率", "Epithelium probability" } },
        { "p_stroma", new[] { "基质概率", "基質機率", "Stroma probability" } },
        { "p_other", new[] { "其他概率", "其他機率", "Other probability" } },
        { "p_poor", new[] { "差的概率", "差的機率", "Poor probability" } },
        { "p_fair", new[] { "一般的概率", "一般的機率", "Fair probability" } },
        { "p_good", new[] { "良好的概率", "良好的機率", "Good probability" } },
        { "p_excellent", new[] { "优秀的概率", "優秀的機率", "Excellent probability" } },
        { "poor", new[] { "差", "差", "Poor" } },
        { "fair", new[] { "一般", "一般", "Fair" } },
        { "good", new[] { "良好", "良好", "Good" } },
        { "excellent", new[] { "优秀", "優秀", "Excellent" } },
        { "not_applicable", new[] { "不适用", "不適用", "Not applicable" } },
        { "ok", new[] { "成功", "成功", "OK" } },
        { "read_error", new[] { "读取失败", "讀取失敗", "Read error" } },
        { "layer_error", new[] { "层面识别失败", "層面辨識失敗", "Layer error" } },
        { "grade_error", new[] { "分级失败", "分級失敗", "Grade error" } },
    };

    private static string Translate(string value,int language)
    {
        string[] labels;
        return Labels.TryGetValue(value,out labels) ? labels[language] : value;
    }

    private static string NumberOrText(object value)
    {
        if (value is double) return ((double)value).ToString("R", Invariant);
        return Convert.ToString(value, Invariant);
    }

    private static void CsvLine(TextWriter csv, IEnumerable<object> values)
    {
        csv.WriteLine(string.Join(",", values.Select(value => {
            var text = NumberOrText(value);
            return text.IndexOfAny(new[]{',','"','\r','\n'}) < 0 ? text : "\"" + text.Replace("\"","\"\"") + "\"";
        })));
    }

    private static string CellText(string text)
    {
        // Escape Excel's literal escape marker and XML control characters in paths and errors.
        return Regex.Replace(text, @"_(?=x[0-9A-Fa-f]{4}_)|[\x00-\x08\x0B\x0C\x0E-\x1F\uFFFE\uFFFF]",
            m => "_x" + ((int)m.Value[0]).ToString("X4",Invariant) + "_");
    }

    private static void DataRow(XmlWriter xml, int index, object[] values, string[] styles)
    {
        string r = index.ToString(Invariant);
        xml.WriteStartElement("row",Ns);
        xml.WriteAttributeString("r",r);
        // Leave wrapped rows eligible for Excel's automatic height calculation.
        for (int i=0;i<values.Length;i++) {
            xml.WriteStartElement("c",Ns);
            xml.WriteAttributeString("r",(char)('A'+i)+r);
            xml.WriteAttributeString("s",styles[i]);
            if (values[i] is string) {
                xml.WriteAttributeString("t","inlineStr");
                xml.WriteStartElement("is",Ns);
                xml.WriteStartElement("t",Ns);
                xml.WriteAttributeString("xml","space",null,"preserve");
                xml.WriteString(CellText((string)values[i]));
                xml.WriteEndElement();
                xml.WriteEndElement();
            } else if (values[i]!=null) xml.WriteElementString("v",Ns,NumberOrText(values[i]));
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static void WriteSheet(Stream target, Stream template, TextWriter csv, IEnumerable<object[]> rows, int language)
    {
        XNamespace ns=Ns;
        var sheet=XElement.Load(template);
        foreach (var value in sheet.Descendants(ns+"sheetData").Elements(ns+"row").Take(3).Descendants(ns+"v"))
            value.Value = Translate(value.Value, language);
        var view=sheet.Descendants(ns+"sheetView").Single();
        sheet.Element(ns+"sheetFormatPr").SetAttributeValue("defaultRowHeight","21");
        view.AddFirst(new XElement(ns+"pane",new XAttribute("xSplit",1),new XAttribute("ySplit",3),
            new XAttribute("topLeftCell","B4"),new XAttribute("activePane","bottomRight"),new XAttribute("state","frozen")));
        var data=sheet.Element(ns+"sheetData");
        var sample=data.Elements().Last();
        var styles=sample.Elements().Select(c=>(string)c.Attribute("s")).ToArray();
        sample.Remove();
        using (var xml=XmlWriter.Create(target,new XmlWriterSettings {Encoding=new UTF8Encoding(false)})) {
            xml.WriteStartElement("worksheet",Ns);
            foreach(var part in sheet.Elements()) {
                if(part!=data) {part.WriteTo(xml); continue;}
                xml.WriteStartElement("sheetData",Ns);
                foreach(var header in data.Elements()) header.WriteTo(xml);
                int index=3;
                foreach(var row in rows) {
                    if (++index>1048576) throw new IOException("Excel worksheet row limit exceeded.");
                    CsvLine(csv,row);
                    var excelRow = (object[])row.Clone();
                    foreach (int column in new[] {6,8})
                        if (row[column] is string) excelRow[column] = Translate((string)row[column],language);
                    DataRow(xml,index,excelRow,styles);
                }
                xml.WriteEndElement();
                xml.WriteStartElement("autoFilter",Ns);
                xml.WriteAttributeString("ref","A3:R"+index.ToString(Invariant));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
    }

    internal static string FreeStem(string stem)
    {
        string candidate=stem;
        for(int i=2; File.Exists(candidate+".csv") || File.Exists(candidate+".xlsx"); i++)
            candidate=stem+"_"+i.ToString(Invariant);
        return candidate;
    }

    internal static string SavePair(string stem, IEnumerable<object[]> rows, Func<bool> partial, int language)
    {
        string temporary=Path.Combine(Path.GetDirectoryName(stem),".hasagi-"+Guid.NewGuid().ToString("N"));
        string csvPath=temporary+".csv.tmp", bookPath=temporary+".xlsx.tmp";
        try {
            using(var csv=new StreamWriter(csvPath,false,new UTF8Encoding(true)))
            using(var output=ZipFile.Open(bookPath,ZipArchiveMode.Create))
            using(var resource=typeof(Report).Assembly.GetManifestResourceStream("report.xlsx"))
            using(var template=new ZipArchive(resource,ZipArchiveMode.Read)) {
                CsvLine(csv,Analysis.Fields);
                foreach(var source in template.Entries) {
                    var entry=output.CreateEntry(source.FullName,CompressionLevel.Fastest);
                    using(var input=source.Open())
                    using(var dest=entry.Open()) {
                        if(source.FullName=="xl/worksheets/sheet1.xml") WriteSheet(dest,input,csv,rows,language);
                        else if (source.FullName=="xl/workbook.xml") {
                            var book = XElement.Load(input);
                            book.Descendants(XName.Get("sheet",Ns)).Single().SetAttributeValue("name",new[]{"结果","結果","Results"}[language]);
                            book.Save(dest);
                        }
                        else input.CopyTo(dest);
                    }
                }
            }
            string final=FreeStem(stem+(partial()?"_partial":""));
            File.Move(csvPath,final+".csv");
            try { File.Move(bookPath,final+".xlsx"); }
            catch { File.Delete(final+".csv"); throw; }
            return final;
        } finally {
            File.Delete(csvPath);
            File.Delete(bookPath);
        }
    }
}
