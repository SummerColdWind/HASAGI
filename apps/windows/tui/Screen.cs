using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class Screen : IDisposable
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle,out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle,uint mode);
    internal const string Normal="\x1b[38;2;34;36;38;48;2;242;242;242m";
    internal const string Selected="\x1b[38;2;255;255;255;48;2;0;90;158m";
    internal const string Green="\x1b[38;2;99;235;93;48;2;12;12;12m";
    internal const string Dim="\x1b[38;2;90;94;98;48;2;242;242;242m";
    private const string Reset="\x1b[0m";
    private readonly IntPtr handle=GetStdHandle(-11);
    private readonly uint mode;
    private readonly Encoding encoding=Console.OutputEncoding;
    private readonly bool ctrl=Console.TreatControlCAsInput;
    private readonly List<string[]> banner=new List<string[]>();
    private List<string> previous;
    internal int Width,Height;

    internal Screen()
    {
        using(var reader=new StreamReader(typeof(Screen).Assembly.GetManifestResourceStream("design.ans"))) {
            reader.ReadLine(); // Title precedes the nine rows of approved banner artwork.
            for(int y=0;y<9;y++) {
                string ink=Normal;
                var cells=new List<string>();
                foreach(Match token in Regex.Matches(reader.ReadLine(),@"\x1b\[[0-9;]*m|.")) {
                    if(token.Value[0]=='\x1b') ink=token.Value;
                    else cells.Add(ink+token.Value);
                }
                banner.Add(cells.ToArray());
            }
        }
        if(!GetConsoleMode(handle,out mode) || !SetConsoleMode(handle,mode|5))
            throw new IOException("Open HASAGI in Windows Terminal or a Windows console with VT support.");
        Console.OutputEncoding=new UTF8Encoding(false);
        Console.TreatControlCAsInput=true;
        Console.Write("\x1b[?1049h\x1b[?25l");
    }
    public void Dispose()
    {
        try { Console.Write(Reset+"\x1b[?25h\x1b[?1049l"); }
        finally {
            Console.TreatControlCAsInput=ctrl;
            Console.OutputEncoding=encoding;
            SetConsoleMode(handle,mode);
        }
    }
    internal bool Resize()
    {
        int w=Math.Min(112,Console.WindowWidth), h=Console.WindowHeight;
        if(Width==w && Height==h) return false;
        Width=w; Height=h; previous=null;
        return true;
    }
    internal void Banner(List<string> lines)
    {
        int n=Math.Max(1,Math.Min(Height-10,(int)Math.Round(9*Width/112.0)));
        n=Math.Min(9,n);
        for(int y=0;y<n;y++) {
            var row=banner[((2*y+1)*9)/(2*n)];
            var s=new StringBuilder();
            for(int x=0;x<Width;x++) s.Append(row[((2*x+1)*row.Length)/(2*Width)]);
            lines.Add(s.ToString());
        }
    }
    internal void Draw(List<string> lines)
    {
        var frame=new StringBuilder();
        if(previous==null) frame.Append(Reset+"\x1b[2J");
        int count=Math.Min(Height,Math.Max(lines.Count,previous==null?0:previous.Count));
        for(int i=0;i<count;i++) {
            string line=i<lines.Count?lines[i]:"";
            if(previous!=null && i<previous.Count && previous[i]==line) continue;
            frame.Append("\x1b["+(i+1)+";1H"+Reset+"\x1b[2K"+line+Reset);
        }
        Console.Write(frame.ToString());
        previous=lines;
    }
    internal static string Clean(string text)
    {
        return new string(text.Select(c=>char.IsControl(c)?' ':c).ToArray());
    }
    private static int Cells(string element)
    {
        int c=char.IsSurrogatePair(element,0)?char.ConvertToUtf32(element,0):element[0];
        return (c>=0x1100 && c<=0x115f) || (c>=0x2e80 && c<=0xa4cf) ||
            (c>=0xac00 && c<=0xd7a3) || (c>=0xf900 && c<=0xfaff) ||
            (c>=0xfe10 && c<=0xfe6f) || (c>=0xff01 && c<=0xff60) ||
            (c>=0x1f300 && c<=0x1faff) || c>=0x20000 ? 2:1;
    }
    internal static int Length(string text)
    {
        var chars=StringInfo.GetTextElementEnumerator(text);
        int n=0;
        while(chars.MoveNext()) n+=Cells(chars.GetTextElement());
        return n;
    }
    internal static string Fit(string text,int width,bool tail=false)
    {
        if(width<=0) return "";
        text=Clean(text);
        int len=Length(text);
        if(len<=width) return text+new string(' ',width-len);
        var chars=StringInfo.GetTextElementEnumerator(text);
        var result=new StringBuilder();
        int used=0;
        while(chars.MoveNext()) {
            string ch=chars.GetTextElement();
            int w=Cells(ch);
            if(tail && len-used>width-1) {used+=w;continue;}
            if(!tail && used+w>width-1) break;
            result.Append(ch); used+=w;
        }
        string cropped=tail?"…"+result:result+"…";
        return cropped+new string(' ',Math.Max(0,width-Length(cropped)));
    }
    internal static IEnumerable<string> Wrap(string text,int width)
    {
        var e=StringInfo.GetTextElementEnumerator(Clean(text));
        var part=new StringBuilder(); int n=0;
        while(e.MoveNext()) {
            string ch=e.GetTextElement(); int size=Cells(ch);
            if(n+size>width) {yield return part.ToString(); part.Clear(); n=0;}
            part.Append(ch); n+=size;
        }
        yield return part.ToString();
    }
}
