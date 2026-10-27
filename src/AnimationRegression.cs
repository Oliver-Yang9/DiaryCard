using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DailyDiaryCard;

internal static class AnimationRegression
{
    public static void Start(MainWindow window,CardConfig config,string output,Action<bool,string> assert,Action<Exception?> done)
    {
        var docking=window.Docking;docking.AutomaticPolling=false;docking.AnimationOverride=true;
        window.SetEdgeAutoHide(true);Keyboard.ClearFocus();
        var original=window.Session.LocalDocument();var edges=new[]{DockEdge.Left,DockEdge.Right,DockEdge.Top,DockEdge.Bottom};
        var frames=new List<object>();var elapsed=new Stopwatch();int index=0,phase=0,samples=0;bool midpoint=false;
        var timer=new DispatcherTimer(DispatcherPriority.Render) {Interval=TimeSpan.FromMilliseconds(20)};
        void Begin(){samples=0;midpoint=false;elapsed.Restart();}
        void Stop(Exception? error)
        {
            timer.Stop();docking.Detach();docking.AnimationOverride=null;
            DiarySession.AtomicText(Path.Combine(output,"animation-result.json"),JsonSerializer.Serialize(new{passed=error==null,systemAnimationEnabled=SystemParameters.ClientAreaAnimation,closeDurationMs=EdgeSlide.CloseDurationMs,openDurationMs=EdgeSlide.OpenDurationMs,frames,error=error?.ToString()},CardConfig.Json));done(error);
        }
        timer.Tick+=(_,_)=>
        {
            try
            {
                if(elapsed.IsRunning&&elapsed.ElapsedMilliseconds>3000)throw new Exception("动画没有在合理时间内结束。");
                switch(phase)
                {
                    case 0:
                        docking.Arm(edges[index]);Keyboard.ClearFocus();
                        assert(docking.Collapse()&&docking.IsAnimating&&!window.IsVisible,$"{edges[index]}：收起后先显示滑动动画，而非立即切到细条");
                        Begin();phase=1;break;
                    case 1:
                        if(docking.IsAnimating)
                        {
                            double p=docking.AnimationProgress;
                            if(p>0.03&&p<0.97)
                            {
                                midpoint=true;
                                var animation=docking.AnimationWindow!;var container=(Grid)animation.Content;var shift=(TranslateTransform)((Image)container.Children[0]).RenderTransform;
                                bool direction=edges[index] switch {DockEdge.Left=>shift.X<0,DockEdge.Right=>shift.X>0,DockEdge.Top=>shift.Y<0,_=>shift.Y>0};
                                if(samples==0)assert(direction&&container.ClipToBounds,$"{edges[index]}：动画方向正确且裁切在原卡片范围内");
                                if(samples<3&&p>(samples+1)*0.18)
                                {
                                    var file=$"16-{edges[index].ToString().ToLowerInvariant()}-close-{samples+1}.png";docking.CaptureAnimation(Path.Combine(output,file));frames.Add(new{edge=edges[index].ToString(),closing=true,progress=p,elapsedMs=elapsed.ElapsedMilliseconds,file,bounds=DockNative.Bounds(animation)});samples++;
                                }
                            }
                            break;
                        }
                        assert(midpoint&&docking.Collapsed&&docking.TabWindow is {IsVisible:true},$"{edges[index]}：真实动画时钟经过中间帧后自然完成收起");
                        docking.Expand();assert(docking.IsAnimating&&!docking.Collapsed,$"{edges[index]}：从入口反向滑入展开");
                        var source=(BitmapSource)((Image)((Grid)docking.AnimationWindow!.Content).Children[0]).Source;
                        var bytes=new byte[source.PixelHeight*source.PixelWidth*4];source.CopyPixels(bytes,source.PixelWidth*4,0);
                        bool visible=false;for(int i=3;i<bytes.Length;i+=400)if(bytes[i]>10){visible=true;break;}
                        assert(visible,$"{edges[index]}：隐藏窗口的展开画面包含实际内容，不是透明空帧");
                        Begin();phase=2;break;
                    case 2:
                        if(docking.IsAnimating)
                        {
                            double p=docking.AnimationProgress;
                            if(p>0.03&&p<0.97)
                            {
                                midpoint=true;
                                if(samples<2){var file=$"17-{edges[index].ToString().ToLowerInvariant()}-open-{samples+1}.png";docking.CaptureAnimation(Path.Combine(output,file));frames.Add(new{edge=edges[index].ToString(),closing=false,progress=p,elapsedMs=elapsed.ElapsedMilliseconds,file});samples++;}
                            }
                            break;
                        }
                        assert(midpoint&&window.IsVisible&&!docking.Collapsed&&docking.TabWindow is {IsVisible:false},$"{edges[index]}：展开动画自然完成后恢复真实编辑窗口");
                        index++;phase=index<edges.Length?0:3;elapsed.Reset();break;
                    case 3:
                        docking.Arm(DockEdge.Left);assert(docking.Collapse(),"返回反转检查：启动收起动画");Begin();phase=4;break;
                    case 4:
                        if(docking.IsAnimating&&docking.AnimationProgress>0.12&&docking.AnimationProgress<0.9)
                        {
                            double before=docking.AnimationProgress;var bounds=DockNative.Bounds(window);
                            docking.Poll(DateTime.UtcNow,bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
                            assert(docking.IsAnimating&&!docking.Collapsed&&Math.Abs(docking.AnimationProgress-before)<0.06,"收起途中鼠标返回时从当前位置平滑反转，不跳回起点");Begin();phase=5;
                        }
                        else if(!docking.IsAnimating)throw new Exception("没有捕获到反转检查的中间帧。");
                        break;
                    case 5:
                        if(docking.IsAnimating)break;
                        assert(window.IsVisible&&!docking.Collapsed,"中途反转后可继续编辑");
                        docking.Arm(DockEdge.Right);assert(docking.Collapse(),"手动恢复检查：启动收起动画");var transient=docking.AnimationWindow;window.ShowCard();
                        assert(window.IsVisible&&!docking.IsAnimating&&transient is {IsVisible:false},"动画途中手动恢复会清理临时画面并显示原窗口");
                        docking.Arm(DockEdge.Bottom);assert(docking.Collapse(),"托盘检查：启动收起动画");transient=docking.AnimationWindow;
                        assert(window.HideCard()&&!docking.IsAnimating&&!window.IsVisible&&transient is {IsVisible:false},"动画途中隐藏至托盘不会留下透明窗口或边缘入口");window.ShowCard();
                        docking.AnimationOverride=false;docking.Arm(DockEdge.Top);
                        assert(docking.Collapse()&&!docking.IsAnimating&&docking.Collapsed,"系统减少动画时保留直接切换回退路径");window.ShowCard();docking.AnimationOverride=true;
                        var child=new MainWindow(config,()=>window.Session.Date);child.Show();child.Docking.AutomaticPolling=false;child.Docking.AnimationOverride=true;child.Docking.Arm(DockEdge.Left);
                        assert(child.Docking.Collapse()&&child.Docking.IsAnimating,"退出检查：启动收起动画");var childAnimation=child.Docking.AnimationWindow;
                        assert(child.ExitCard()&&!child.Docking.IsAnimating&&childAnimation is {IsVisible:false},"动画途中正常退出会关闭临时画面并释放动画");
                        assert(window.Session.LocalDocument()==original,"所有动画及中途操作均不改变日记内容");elapsed.Reset();Stop(null);break;
                }
            }
            catch(Exception e){Stop(e);}
        };
        timer.Start();
    }
}
