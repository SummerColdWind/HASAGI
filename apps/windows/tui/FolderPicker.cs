using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class FolderPicker
{
    private readonly bool output;
    private readonly Func<string,string,string,string> tr;
    private string directory, edit, error="";
    private string[] folders;
    private int index, caret;
    private bool editing, selectAll;
    internal bool Closed;
    internal string Value;
    internal string ReportName;

    internal FolderPicker(bool output,string initial,Func<string,string,string,string> tr)
    {
        this.output=output; this.tr=tr;
        Load(Directory.Exists(initial)?initial:Environment.CurrentDirectory);
        if(!string.IsNullOrEmpty(initial) && initial!=directory) StartEdit(initial);
    }
    private void Load(string path)
    {
        try {
            string current=Path.GetFullPath(path);
            var children=Directory.GetDirectories(current).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();
            directory=current; folders=children; index=0; editing=false; edit=current; error="";
        } catch(Exception ex) {
            if(directory==null) {directory=Path.GetFullPath(path); folders=new string[0]; StartEdit(directory);}
            error=ex.Message;
        }
    }
    internal void StartEdit(string initial)
    {
        editing=true; edit=initial??""; caret=edit.Length; selectAll=true; error="";
    }
    private void Insert(string text)
    {
        if(selectAll) {edit="";caret=0;selectAll=false;}
        text=text.TrimEnd('\r','\n').Replace("\r","").Replace("\n","");
        edit=edit.Insert(caret,text); caret+=text.Length;
    }
    internal void Handle(ConsoleKeyInfo k)
    {
        if(k.Key==ConsoleKey.Escape) {Closed=true;return;}
        if(k.Key==ConsoleKey.Tab) {
            if(editing) {editing=false;error="";} else StartEdit(directory);
            return;
        }
        if(k.Key==ConsoleKey.V && (k.Modifiers&ConsoleModifiers.Control)!=0) {
            try {
                if(!editing) StartEdit("");
                var drop=Clipboard.GetFileDropList();
                Insert(drop.Count==1?drop[0]:Clipboard.GetText());
            } catch(Exception ex) {error=ex.Message;}
            return;
        }
        if(!editing) {
            if(k.Key==ConsoleKey.UpArrow) index=Math.Max(0,index-1);
            else if(k.Key==ConsoleKey.DownArrow) index=Math.Min(folders.Length+1,index+1);
            else if(k.Key==ConsoleKey.Home) index=0;
            else if(k.Key==ConsoleKey.End) index=folders.Length+1;
            else if(k.Key==ConsoleKey.Enter || k.Key==ConsoleKey.Backspace) {
                if(k.Key==ConsoleKey.Backspace || index==1) {
                    var parent=Directory.GetParent(directory);
                    if(parent!=null) Load(parent.FullName);
                    else StartEdit(directory);
                } else if(index==0) {Value=directory;Closed=true;}
                else Load(folders[index-2]);
            } else if(!char.IsControl(k.KeyChar)) {StartEdit("");Insert(k.KeyChar.ToString());}
            return;
        }
        switch(k.Key) {
            case ConsoleKey.Enter:
                try {
                    string raw=Environment.ExpandEnvironmentVariables(edit.Trim().Trim('"'));
                    if(raw.Length==0) throw new IOException(tr("请输入目录。","請輸入資料夾。","Enter a directory."));
                    string path=Path.GetFullPath(Path.Combine(directory,raw));
                    var ext=Path.GetExtension(path);
                    if(output && !Directory.Exists(path) &&
                       (ext.Equals(".csv",StringComparison.OrdinalIgnoreCase) || ext.Equals(".xlsx",StringComparison.OrdinalIgnoreCase))) {
                        var name=Path.GetFileNameWithoutExtension(path);
                        if(string.IsNullOrWhiteSpace(name)) throw new IOException(tr("请输入文件名。","請輸入檔名。","Enter a file name."));
                        ReportName=name;
                        path=Path.GetDirectoryName(path);
                    }
                    if(File.Exists(path) || (!output && !Directory.Exists(path)))
                        throw new IOException(tr("目录不存在或指向文件。","資料夾不存在或指向檔案。","Directory does not exist or is a file."));
                    Value=path; Closed=true;
                } catch(Exception ex) {error=ex.Message;}
                break;
            case ConsoleKey.A:
                if((k.Modifiers&ConsoleModifiers.Control)!=0) {selectAll=true;break;}
                goto default;
            case ConsoleKey.LeftArrow: caret=Prev(); selectAll=false; break;
            case ConsoleKey.RightArrow: caret=Next(); selectAll=false; break;
            case ConsoleKey.Home: caret=0;selectAll=false;break;
            case ConsoleKey.End: caret=edit.Length;selectAll=false;break;
            case ConsoleKey.Delete:
            case ConsoleKey.Backspace:
                if(selectAll) {edit="";caret=0;selectAll=false;}
                else if(k.Key==ConsoleKey.Delete) edit=edit.Remove(caret,Next()-caret);
                else {int p=Prev();edit=edit.Remove(p,caret-p);caret=p;}
                break;
            default: if(!char.IsControl(k.KeyChar)) Insert(k.KeyChar.ToString()); break;
        }
    }
    private int Prev() {return Math.Max(0,caret-(caret>1 && char.IsLowSurrogate(edit[caret-1])?2:1));}
    private int Next() {return Math.Min(edit.Length,caret+(caret<edit.Length && char.IsHighSurrogate(edit[caret])?2:1));}
    internal void Draw(List<string> lines,int width,int height)
    {
        string title=output?tr("输出目录   ·   也可输入 .csv 或 .xlsx 文件路径","輸出資料夾   ·   也可輸入 .csv 或 .xlsx 檔案路徑","Output folder   ·   or enter a .csv / .xlsx file path"):tr("输入目录","輸入資料夾","Input folder");
        lines.Add(Screen.Normal+Screen.Fit(" "+title,width));
        string text=editing ? edit.Insert(caret,"▏") : directory;
        if(editing && Screen.Length(text)>width-3 && Screen.Length(edit.Substring(0,caret))>width-5)
            text=Screen.Fit(edit.Substring(0,caret)+"▏",width-3,true);
        lines.Add((editing?Screen.Selected:Screen.Normal)+Screen.Fit(" > "+text,width));
        var names=new []{tr("[选用当前目录]","[選用目前資料夾]","[Use this folder]"),"[..]"};
        int count=folders.Length+2;
        int visible=Math.Max(1,Math.Min(count,height-5));
        int start=Math.Min(Math.Max(0,index-visible+1),count-visible);
        for(int i=start;i<start+visible;i++){
            string label=i<2?names[i]:Path.GetFileName(folders[i-2])+Path.DirectorySeparatorChar;
            bool active=!editing && index==i;
            lines.Add((active?Screen.Selected:Screen.Normal)+Screen.Fit((active?" > ":"   ")+label,width));
        }
        if(error.Length>0) lines.Add(Screen.Green+Screen.Fit(" "+error,width));
        lines.Add(Screen.Dim+Screen.Fit(tr(" ↑↓ 浏览  Tab 输入/粘贴路径  Enter 确认  Esc 返回"," ↑↓ 瀏覽  Tab 輸入/貼上路徑  Enter 確認  Esc 返回"," ↑↓ Browse   Tab Edit path   Enter Select   Esc Back"),width));
    }
}
