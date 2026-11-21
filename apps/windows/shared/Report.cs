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

    private static void WriteSheet(Stream target, Stream template, TextWriter csv, IEnumerable<object[]> rows)
    {
        XNamespace ns=Ns;
        var sheet=XElement.Load(template);
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
                    DataRow(xml,index,row,styles);
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

    internal static string SavePair(string stem, IEnumerable<object[]> rows, Func<bool> partial)
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
                        if(source.FullName=="xl/worksheets/sheet1.xml") WriteSheet(dest,input,csv,rows);
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
