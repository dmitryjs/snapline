using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
namespace Navodka {
partial class Editor {
    sealed class Edit {
        public double Start,End,Zoom,X,Y;
        public ZoomSpan[] Tracks;
        public Annotation[] Masks;
    }
    readonly List<Edit> undo=new List<Edit>(),redo=new List<Edit>();
    Edit CaptureEdit(){
        return new Edit{Start=start,End=end,Zoom=zoom,X=manualX,Y=manualY,
            Tracks=position.Spans.Select(z=>z.Copy()).ToArray(),Masks=annotations.Select(a=>a.Copy()).ToArray()};
    }
    void HistoryButtons(){
        Get<Button>("Undo").IsEnabled=undo.Count>0;Get<Button>("Redo").IsEnabled=redo.Count>0;
        Get<Button>("DeleteSelection").IsEnabled=position.Selected>=0||position.ManualSelected||Get<ListBox>("FxList").SelectedIndex>=0;
    }
    void Remember(){
        if(!ready)return;
        if(undo.Count>=80)undo.RemoveAt(0);
        undo.Add(CaptureEdit());redo.Clear();HistoryButtons();
    }
    void Restore(List<Edit> from,List<Edit> to){
        if(from.Count==0)return;
        Pause();to.Add(CaptureEdit());var item=from[from.Count-1];from.RemoveAt(from.Count-1);
        start=item.Start;end=item.End;zoom=item.Zoom;manualX=item.X;manualY=item.Y;
        position.Spans.Clear();position.Spans.AddRange(item.Tracks);position.Selected=-1;position.ManualSelected=false;
        var list=Get<ListBox>("FxList");list.Items.Clear();annotations.Clear();
        foreach(var a in item.Masks){annotations.Add(a);list.Items.Add(a);}
        position.Value=Math.Max(start,Math.Min(position.Value,end));Range();Paint(position.Value);HistoryButtons();
    }
    void DeleteSelection(){
        if(position.Selected>=0){
            Remember();position.Spans.RemoveAt(position.Selected);position.Selected=-1;
        }else if(position.ManualSelected){
            Remember();zoom=-1;position.ManualSelected=false;
        }else{
            var list=Get<ListBox>("FxList");var a=list.SelectedItem as Annotation;if(a==null)return;
            Remember();annotations.Remove(a);list.Items.Remove(a);
        }
        Range();Paint(position.Value);HistoryButtons();
    }
    void InitTimelineEditing(){
        position.SelectionChanged=()=>{Pause();HistoryButtons();if(position.Selected>=0){var z=position.Spans[position.Selected];position.Value=(z.Start+z.End)/2;}else if(position.ManualSelected)position.Value=Math.Min(duration,zoom+1);};
        position.BeginEdit=()=>{Pause();Remember();};
        position.EndEdit=HistoryButtons;position.Changed=()=>Paint(position.Value);
        position.Trim=(left,right)=>{start=left;end=right;Range();};
        position.MoveManual=time=>{zoom=time;Range();Paint(position.Value);};
        Bind("Undo",()=>Restore(undo,redo));Bind("Redo",()=>Restore(redo,undo));Bind("DeleteSelection",DeleteSelection);
        win.PreviewKeyDown+=(s,e)=>{
            if(e.Key==Key.Space && Keyboard.Modifiers==ModifierKeys.None && !(e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase)
                && !(e.OriginalSource is ComboBox && ((ComboBox)e.OriginalSource).IsDropDownOpen)){
                e.Handled=true;if(!e.IsRepeat && ready && !exporting)Get<Button>("Play").RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            }else if(Keyboard.Modifiers==ModifierKeys.Control && (e.Key==Key.Z || e.Key==Key.Y)){
                if(e.Key==Key.Z)Restore(undo,redo);else Restore(redo,undo);e.Handled=true;
            }else if(e.Key==Key.Delete && position.IsKeyboardFocusWithin){DeleteSelection();e.Handled=true;}
        };
        HistoryButtons();
    }
}
}
