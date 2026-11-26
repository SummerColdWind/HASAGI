using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal sealed class Program
{
    private enum Focus { Input, Output, Start, Log, Language }
    private static readonly string[] Languages={"简体中文","繁體中文","English"};
    private readonly ConcurrentQueue<Action> updates=new ConcurrentQueue<Action>();
    private readonly List<Func<string>> history=new List<Func<string>>();
    private readonly Screen screen;
    private string input,output,saved;
    private const string DefaultName="HASAGI_results";
    private string reportName=DefaultName;
    private Focus focus=Focus.Input;
    private int language=AppLanguage.Load(), languageChoice=-1, scroll;
    private bool expanded,quit,exitAfterSave,confirmExit,lastProgress;
    private FolderPicker picker;
    private CancellationTokenSource stop;
    private Task worker;
    private bool Busy {get{return stop!=null;}}
    private string T(string simplified,string traditional,string english)
    {return language==0?simplified:language==1?traditional:english;}

    private Program(Screen screen,string initial)
    {
        this.screen=screen;
        if(initial==null) Log(()=>T("请选择输入目录。","請選擇輸入資料夾。","Select an input folder."));
        else SelectInput(initial);
    }
    private void Log(Func<string> message,bool progress=false)
    {
        if(progress && lastProgress && history.Count>0) history.RemoveAt(history.Count-1);
        history.Add(message); lastProgress=progress;
        if(history.Count>500) history.RemoveAt(0);
    }
    private void SelectInput(string path)
    {
        input=Path.GetFullPath(path); output=input; reportName=DefaultName; saved=null; focus=Focus.Start;
        string value=input;
        Log(()=>T("已选择目录：","已選擇資料夾：","Selected folder: ")+value);
    }
    private void Choose(bool isOutput)
    {
        if(Busy || (isOutput && input==null)) return;
        focus=isOutput?Focus.Output:Focus.Input;
        picker=new FolderPicker(isOutput,isOutput?output:input,T);
    }
    private void Cancel()
    {
        if(!Busy || stop.IsCancellationRequested) return;
        stop.Cancel(); Log(()=>T("正在停止并保存已完成的结果…","正在停止並儲存已完成的結果…","Stopping and saving completed rows…"));
    }
    private void Analyze()
    {
        if(Busy) {Cancel();return;}
        if(input==null) {Choose(false);return;}
        string src=input,dest=output,name=reportName;
        saved=null; focus=Focus.Start; scroll=0;
        stop=new CancellationTokenSource();
        var token=stop.Token;
        Log(()=>T("正在扫描图像…","正在掃描影像…","Scanning images…"));
        int reportLanguage=language;
        worker=Task.Run(()=> {
            var job=new BatchJob(
                n=>updates.Enqueue(()=>Log(()=>T("找到 "+n+" 张图像。","找到 "+n+" 張影像。","Found "+n+" images."))),
                (n,total,errors)=>updates.Enqueue(()=>Log(()=>T("已处理 ","已處理 ","Processed ")+n+" / "+total+
                    T("，失败 ","，失敗 ","; errors ")+errors,true)));
            try {
                int code=job.Run(src,dest,token,reportLanguage,name);
                updates.Enqueue(()=> {
                    if(job.Saved!=null) {
                        saved=job.Saved;
                        Log(()=> "CSV   "+job.Saved+".csv");
                        Log(()=> "Excel "+job.Saved+".xlsx");
                        string stats=job.Processed+" / "+job.Total;
                        Log(()=> (job.Partial?T("已停止，部分结果已保存。","已停止，部分結果已儲存。","Stopped. Partial results saved. "):
                                 T("分析完成。","分析完成。","Analysis complete. "))+stats+T("，失败 ","，失敗 ","; errors ")+job.Errors);
                    } else if(code==4) Log(()=>T("没有找到支持的图像。","沒有找到支援的影像。","No supported images found."));
                });
            } catch(OperationCanceledException) {updates.Enqueue(()=>Log(()=>T("已停止扫描。","已停止掃描。","Scan stopped.")));}
            catch(Exception ex) {string reason=ex.Message; updates.Enqueue(()=>Log(()=>T("分析失败：","分析失敗：","Analysis failed: ")+reason));}
            finally {updates.Enqueue(()=>{stop.Dispose();stop=null;confirmExit=false;if(exitAfterSave)quit=true;});}
        });
    }
    private Focus[] Controls()
    {
        if(Busy) return new[]{Focus.Start,Focus.Log,Focus.Language};
        if(input==null) return new[]{Focus.Input,Focus.Log,Focus.Language};
        return new[]{Focus.Input,Focus.Output,Focus.Start,Focus.Log,Focus.Language};
    }
    private void Activate()
    {
        switch(focus) {
            case Focus.Input: Choose(false);break;
            case Focus.Output: Choose(true);break;
            case Focus.Start: Analyze();break;
            case Focus.Log: expanded=!expanded;scroll=0;break;
            case Focus.Language: languageChoice=language;break;
        }
    }
    private void Handle(ConsoleKeyInfo k)
    {
        bool ctrl=(k.Modifiers&ConsoleModifiers.Control)!=0;
        if(ctrl && k.Key==ConsoleKey.C) {
            if(Busy) Cancel(); else quit=true;
            return;
        }
        if(confirmExit) {
            if(k.Key==ConsoleKey.Enter) {exitAfterSave=true;confirmExit=false;Cancel();}
            if(k.Key==ConsoleKey.Escape) confirmExit=false;
            return;
        }
        if(picker!=null) {
            picker.Handle(k);
            if(picker.Closed) {
                if(picker.Value!=null) {
                    if(focus==Focus.Input) SelectInput(picker.Value);
                    else {
                        output=picker.Value; reportName=picker.ReportName??reportName; saved=null; focus=Focus.Start;
                        string val=output; Log(()=>T("输出目录：","輸出資料夾：","Output folder: ")+val);
                    }
                }
                picker=null;
            }
            return;
        }
        if(languageChoice>=0) {
            switch(k.Key) {
                case ConsoleKey.Escape: case ConsoleKey.F4: languageChoice=-1;break;
                case ConsoleKey.UpArrow: languageChoice=(languageChoice+2)%3;break;
                case ConsoleKey.DownArrow: case ConsoleKey.Tab: languageChoice=(languageChoice+1)%3;break;
                case ConsoleKey.Enter: language=languageChoice;AppLanguage.Save(language);languageChoice=-1;break;
            }
            return;
        }
        switch(k.Key) {
            case ConsoleKey.Q: if(Busy) confirmExit=true;else quit=true;break;
            case ConsoleKey.F2: Choose(false);break;
            case ConsoleKey.F3: Choose(true);break;
            case ConsoleKey.F4: focus=Focus.Language;Activate();break;
            case ConsoleKey.F5: Analyze();break;
            case ConsoleKey.F6: focus=Focus.Log;Activate();break;
            case ConsoleKey.Enter: case ConsoleKey.Spacebar: Activate();break;
            case ConsoleKey.PageUp: case ConsoleKey.UpArrow: if(expanded) scroll+=k.Key==ConsoleKey.UpArrow?1:5;break;
            case ConsoleKey.PageDown: case ConsoleKey.DownArrow: if(expanded) scroll=Math.Max(0,scroll-(k.Key==ConsoleKey.DownArrow?1:5));break;
            case ConsoleKey.End: scroll=0;break;
            case ConsoleKey.Tab:
                var controls=Controls();
                int step=(k.Modifiers&ConsoleModifiers.Shift)==0?1:-1;
                focus=controls[(Array.IndexOf(controls,focus)+step+controls.Length)%controls.Length];break;
            default:
                if(input==null && (!char.IsControl(k.KeyChar) || (ctrl && k.Key==ConsoleKey.V))) {
                    Choose(false);picker.StartEdit("");picker.Handle(k);
                }
                break;
        }
    }
    private string Button(Focus target,string text)
    {
        return (focus==target?Screen.Selected:Screen.Normal)+text+Screen.Normal;
    }
    private void Field(List<string> lines,string label,string path,string buttons,int buttonWidth)
    {
        int space=screen.Width-4-Screen.Length(label)-buttonWidth;
        lines.Add(Screen.Dim+"│"+Screen.Normal+" "+label+Screen.Fit(path,space,true)+buttons+" "+Screen.Dim+"│");
    }
    private void MainScreen(List<string> lines)
    {
        int w=screen.Width;
        bool small=w<76;
        screen.Banner(lines);
        lines.Add(Screen.Dim+"┌"+new string('─',w-2)+"┐");
        if(input==null) {
            string label=T(" Enter  选择图像目录 "," Enter  選擇影像資料夾 "," Enter  Select image folder ");
            int rest=w-2-Screen.Length(label);
            lines.Add(Screen.Dim+"│"+Screen.Normal+new string(' ',rest/2)+Button(Focus.Input,label)+new string(' ',rest-rest/2)+Screen.Dim+"│");
            string hint=T("也可粘贴目录路径","也可貼上資料夾路徑","or paste a folder path");
            int margin=(w-2-Screen.Length(hint))/2;
            lines.Add(Screen.Dim+"│"+Screen.Fit(new string(' ',margin)+hint,w-2)+"│");
        } else {
            string pick=Busy?"":small?"[F2]":"[F2 "+T("更改目录","更改資料夾","Input")+"]";
            string dest=Busy?"":small?"[F3] ":"[F3 "+T("更改输出","更改輸出","Output")+"] ";
            string start="[F5 "+(Busy?T("停止","停止","Stop"):T("开始分析","開始分析","Analyze"))+"]";
            string stem=saved??Path.Combine(output,reportName);
            Field(lines,T("输入 ","輸入 ","Input "),input,Button(Focus.Input,pick),Screen.Length(pick));
            Field(lines,T("输出 ","輸出 ","Output "),stem+".{csv,xlsx}",Button(Focus.Output,dest)+Button(Focus.Start,start),Screen.Length(dest+start));
        }
        lines.Add(Screen.Dim+"└"+new string('─',w-2)+"┘");
        string lang=" 🌐 [F4 "+Languages[language]+" ▾]";
        string console=" "+(expanded?"▾":"▸")+" F6 ";
        lines.Add((focus==Focus.Log?Screen.Selected:Screen.Green)+console+Screen.Green+
                  Screen.Fit(expanded?T("控制台","主控台","Console"):history.Last()(),w-Screen.Length(lang+console))+
                  Button(Focus.Language,lang));
        if(expanded) {
            int reserve=languageChoice>=0?4:1;
            int capacity=Math.Max(1,Math.Min(8,screen.Height-lines.Count-reserve));
            var logs=history.SelectMany(f=>Screen.Wrap("> "+f(),w-2)).ToList();
            scroll=Math.Min(scroll,Math.Max(0,logs.Count-capacity));
            foreach(var item in logs.Skip(Math.Max(0,logs.Count-capacity-scroll)).Take(capacity))
                lines.Add(Screen.Green+Screen.Fit(" "+item,w));
        }
        if(languageChoice>=0) {
            for(int i=0;i<Languages.Length;i++) lines.Add((i==languageChoice?Screen.Selected:Screen.Normal)+Screen.Fit("  "+Languages[i],w));
        }
        string hintText=confirmExit?T("停止、保存部分结果并退出？ Enter 确认   Esc 返回","停止、儲存部分結果並退出？ Enter 確認   Esc 返回","Stop, save partial results and quit? Enter Yes   Esc Back"):
            languageChoice>=0?" ↑↓   Enter   Esc":
            T(" Tab 切换焦点   Enter 确认"," Tab 切換焦點   Enter 確認"," Tab Focus   Enter Select")+
              (expanded?T("   ↑↓ 日志","   ↑↓ 記錄","   ↑↓ Log"):"")+T("   Q 退出","   Q 退出","   Q Quit");
        lines.Add((confirmExit?Screen.Selected:Screen.Dim)+Screen.Fit(hintText,w));
    }
    private void Draw()
    {
        var lines=new List<string>();
        lines.Add(Screen.Normal+Screen.Fit(" HASAGI: High-throughput Automated Screening And Grading of IVCM Images",screen.Width));
        if(screen.Width<45 || screen.Height<13)
            lines.Add(Screen.Normal+Screen.Fit(T("请扩大终端至 45×13","請擴大終端至 45×13","Resize terminal to 45 x 13"),screen.Width));
        else if(picker!=null) picker.Draw(lines,screen.Width,screen.Height-1);
        else MainScreen(lines);
        screen.Draw(lines);
    }
    private void Loop()
    {
        bool dirty=true;
        try {
            while(!quit) {
                dirty|=screen.Resize();
                Action update;
                while(updates.TryDequeue(out update)) {update();dirty=true;}
                while(!quit && Console.KeyAvailable) {Handle(Console.ReadKey(true));dirty=true;}
                if(dirty && !quit) {Draw();dirty=false;}
                if(!quit) Thread.Sleep(35);
            }
        } finally {
            if(Busy) stop.Cancel();
            if(worker!=null) worker.GetAwaiter().GetResult();
            if(stop!=null) stop.Dispose();
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length==1 && (args[0]=="--help" || args[0]=="-h")) {
            Console.WriteLine("HASAGI-tui.exe [INPUT_FOLDER]\nF2 Input   F3 Output   F4 Language   F5 Start/Stop   F6 Console   Q Quit");return 0;
        }
        if(args.Length>1 || (args.Length==1 && !Directory.Exists(args[0]))) {
            Console.Error.WriteLine("Usage: HASAGI-tui.exe [existing input folder]"); return 2;
        }
        if(Console.IsInputRedirected || Console.IsOutputRedirected) {Console.Error.WriteLine("An interactive Windows console is required.");return 2;}
        try {using(var view=new Screen()) new Program(view,args.FirstOrDefault()).Loop(); return 0;}
        catch(Exception ex) {Console.Error.WriteLine(ex.Message);return 1;}
    }
}
