using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace ClaudeMeter {
public static class SkinOptions {
    public static Skin Next(IList<Skin> skins,string previous,Random random){var choices=skins.Where(s=>s.Id!=previous).ToList();return choices.Count==0?skins[0]:choices[random.Next(choices.Count)];}
    static Brush Color(string value){return (Brush)new BrushConverter().ConvertFromString(value);}
    public static FrameworkElement Language(string code,string name){
        var group=new DrawingGroup();group.ClipGeometry=new RectangleGeometry(new Rect(0,0,30,20));
        using(var dc=group.Open()){
            if(code=="en"){
                dc.DrawRectangle(Color("#183A80"),null,new Rect(0,0,30,20));
                foreach(var pair in new[]{new[]{new Point(0,0),new Point(30,20)},new[]{new Point(0,20),new Point(30,0)}}){dc.DrawLine(new Pen(Brushes.White,5),pair[0],pair[1]);dc.DrawLine(new Pen(Color("#C92435"),2),pair[0],pair[1]);}
                dc.DrawRectangle(Brushes.White,null,new Rect(11,0,8,20));dc.DrawRectangle(Brushes.White,null,new Rect(0,6,30,8));dc.DrawRectangle(Color("#C92435"),null,new Rect(13,0,4,20));dc.DrawRectangle(Color("#C92435"),null,new Rect(0,8,30,4));
            }else if(code=="fr"||code=="it"){
                dc.DrawRectangle(Color(code=="fr"?"#163D89":"#15945B"),null,new Rect(0,0,10,20));dc.DrawRectangle(Brushes.White,null,new Rect(10,0,10,20));dc.DrawRectangle(Color("#D5303C"),null,new Rect(20,0,10,20));
            }else if(code=="pt"){
                dc.DrawRectangle(Color("#138245"),null,new Rect(0,0,12,20));dc.DrawRectangle(Color("#D52A3B"),null,new Rect(12,0,18,20));dc.DrawEllipse(Color("#F3D050"),null,new Point(12,10),4,4);dc.DrawRectangle(Brushes.White,null,new Rect(10.7,7.7,2.6,4.5));
            }else{
                string[] colors=code=="ru"?new[]{"#FFFFFF","#2455B7","#D72F3F"}:code=="de"?new[]{"#161616","#CB2831","#E7BF39"}:code=="sr"?new[]{"#C82C3B","#25498B","#FFFFFF"}:new[]{"#B32034","#F1C739","#B32034"};
                if(code=="es"){dc.DrawRectangle(Color(colors[0]),null,new Rect(0,0,30,5));dc.DrawRectangle(Color(colors[1]),null,new Rect(0,5,30,10));dc.DrawRectangle(Color(colors[2]),null,new Rect(0,15,30,5));dc.DrawRectangle(Color("#BD3442"),null,new Rect(8,8,3,5));}
                else{for(int i=0;i<3;i++)dc.DrawRectangle(Color(colors[i]),null,new Rect(0,i*20.0/3,30,20.0/3));if(code=="sr"){dc.DrawRectangle(Brushes.White,null,new Rect(9,6,5,8));dc.DrawRectangle(Color("#C82C3B"),null,new Rect(10,7,3,5));}}
            }
            dc.DrawRectangle(null,new Pen(Color("#40303030"),.7),new Rect(.35,.35,29.3,19.3));
        }
        group.Freeze();var panel=new StackPanel{Orientation=Orientation.Horizontal};panel.Children.Add(new Image{Source=new DrawingImage(group),Width=27,Height=18,Margin=new Thickness(0,0,10,0)});panel.Children.Add(new TextBlock{Text=name,VerticalAlignment=VerticalAlignment.Center});System.Windows.Automation.AutomationProperties.SetName(panel,name);return panel;
    }
}
}
