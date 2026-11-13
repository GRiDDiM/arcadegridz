# Generates the animated CityLife layer XAML for the launcher backdrop (1672x941 image space)
# plus a PIL preview so placement can be checked against the art.
import random, sys
random.seed(7)
X=[]; A=[]   # element xaml, animation xaml
def anim(target, prop, frames=None, frm=None, to=None, dur=None, rev=False, begin=0, ease=False):
    b=f' BeginTime="0:0:{begin:g}"' if begin else ''
    if frames is not None:
        ks=''.join(f'<{"Discrete" if d else "Linear"}DoubleKeyFrame KeyTime="0:0:{t:g}" Value="{v:g}"/>' for t,v,d in frames)
        A.append(f'<DoubleAnimationUsingKeyFrames Storyboard.TargetName="{target}" Storyboard.TargetProperty="{prop}" RepeatBehavior="Forever"{b}>{ks}</DoubleAnimationUsingKeyFrames>')
    else:
        e='<DoubleAnimation.EasingFunction><SineEase EasingMode="EaseInOut"/></DoubleAnimation.EasingFunction>' if ease else ''
        A.append(f'<DoubleAnimation Storyboard.TargetName="{target}" Storyboard.TargetProperty="{prop}" From="{frm:g}" To="{to:g}" Duration="0:0:{dur:g}" AutoReverse="{str(rev)}" RepeatBehavior="Forever"{b}>{e}</DoubleAnimation>' if e else
                 f'<DoubleAnimation Storyboard.TargetName="{target}" Storyboard.TargetProperty="{prop}" From="{frm:g}" To="{to:g}" Duration="0:0:{dur:g}" AutoReverse="{str(rev)}" RepeatBehavior="Forever"{b}/>')
TX='(UIElement.RenderTransform).(CompositeTransform.TranslateX)'
TY='(UIElement.RenderTransform).(CompositeTransform.TranslateY)'
ROT='(UIElement.RenderTransform).(CompositeTransform.Rotation)'
OP='(UIElement.Opacity)'
CT='<Canvas.RenderTransform><CompositeTransform/></Canvas.RenderTransform>'
preview=[]  # (kind, x, y, w, h, color)

# ---- moon: cycling rim glow (magenta -> cyan -> violet) + soft halo ----
mx,my,mr=1177,78,44
def ring(name,color,size,inner,peak,alpha):
    X.append(f'<Ellipse x:Name="{name}" Width="{size}" Height="{size}" Canvas.Left="{mx-size/2:g}" Canvas.Top="{my-size/2:g}" Opacity="0"><Ellipse.Fill><RadialGradientBrush>'
             f'<GradientStop Color="#00{color}" Offset="0"/><GradientStop Color="#18{color}" Offset="{inner-0.12:g}"/><GradientStop Color="#{alpha}{color}" Offset="{peak:g}"/><GradientStop Color="#00{color}" Offset="1"/></RadialGradientBrush></Ellipse.Fill></Ellipse>')
for i,(n,c) in enumerate([('MoonGlowA','FF3AA7'),('MoonGlowB','5EE7FF'),('MoonGlowC','B26BFF')]):
    ring(n,c,150,mr/75,mr/75+0.05,'C0')
    t0=i*6
    fr=[(0,0,False)]
    # each colour rises/falls in its own third of an 18 s cycle, overlapping its neighbours
    fr=[(0,1 if i==0 else 0,False),(3,1 if i==0 else 0,False)] if False else None
    keys=[]
    for t in range(0,19,3):
        phase=((t-t0)%18)
        v={0:1.0,3:0.75,6:0.0,9:0.0,12:0.0,15:0.35}[phase]
        keys.append((t,v,False))
    anim(n,OP,frames=keys)
    preview.append(('ring',mx,my,75,75,c))
X.append(f'<Ellipse x:Name="MoonHalo" Width="320" Height="320" Canvas.Left="{mx-160}" Canvas.Top="{my-160}" Opacity="0.35"><Ellipse.Fill><RadialGradientBrush><GradientStop Color="#40B26BFF" Offset="0.25"/><GradientStop Color="#00B26BFF" Offset="1"/></RadialGradientBrush></Ellipse.Fill></Ellipse>')
anim('MoonHalo',OP,frm=0.2,to=0.6,dur=9,rev=True,ease=True)

# ---- sun: breathing halo + reflection shimmer on the water ----
sx,sy=833,165
X.append(f'<Ellipse x:Name="SunHalo" Width="420" Height="420" Canvas.Left="{sx-210}" Canvas.Top="{sy-210}"><Ellipse.Fill><RadialGradientBrush><GradientStop Color="#00FF6A9B" Offset="0.5"/><GradientStop Color="#55FF6A9B" Offset="0.6"/><GradientStop Color="#00FF3AA7" Offset="1"/></RadialGradientBrush></Ellipse.Fill></Ellipse>')
anim('SunHalo',OP,frm=0.35,to=0.9,dur=5,rev=True,ease=True)
for i,(y,w) in enumerate([(256,70),(263,96),(271,60),(280,110),(290,80),(301,124),(312,90)]):
    n=f'SunGlint{i}'
    X.append(f'<Rectangle x:Name="{n}" Width="{w}" Height="2" Canvas.Left="{sx-w/2:g}" Canvas.Top="{y}" Fill="#FFFF8FB8" Opacity="0">{CT.replace("Canvas","Rectangle")}</Rectangle>')
    anim(n,OP,frm=0,to=0.8,dur=round(random.uniform(1.1,2.3),2),rev=True,begin=round(random.uniform(0,2),2))
    anim(n,TX,frm=-6,to=6,dur=round(random.uniform(2.5,4.5),2),rev=True,ease=True)
    preview.append(('rect',sx-w/2,y,w,2,'FF8FB8'))

# ---- synthwave grid pulses rolling from the horizon toward the railing ----
for i in range(2):
    n=f'GridPulse{i}'
    X.append(f'<Rectangle x:Name="{n}" Width="1100" Height="2" Canvas.Left="290" Canvas.Top="250" Opacity="0"><Rectangle.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="1,0"><GradientStop Color="#00FF3AA7" Offset="0"/><GradientStop Color="#FFFF3AA7" Offset="0.3"/><GradientStop Color="#FF5EE7FF" Offset="0.7"/><GradientStop Color="#005EE7FF" Offset="1"/></LinearGradientBrush></Rectangle.Fill>{CT.replace("Canvas","Rectangle")}</Rectangle>')
    anim(n,TY,frames=[(0,0,False),(4,68,False),(8,68,True)],begin=i*4)
    anim(n,OP,frames=[(0,0,False),(0.4,0.55,False),(4,0,False),(8,0,True)],begin=i*4)

# ---- stars twinkling ----
stars=[(330,40),(372,88),(455,22),(520,64),(612,30),(700,74),(752,18),(960,40),(1010,24),(1060,60),(1270,30),(1300,70),(1350,18),(400,130),(1365,110),(690,128),(985,108),(1130,20)]
for i,(x,y) in enumerate(stars):
    n=f'Star{i}'; s=random.choice([2,2,3,4])
    col=random.choice(['FFFFFFFF','FF9FF6FF','FFFFB3E6'])
    X.append(f'<Ellipse x:Name="{n}" Width="{s}" Height="{s}" Canvas.Left="{x}" Canvas.Top="{y}" Fill="#{col}" Opacity="0.2"/>')
    anim(n,OP,frm=0.1,to=1,dur=round(random.uniform(1.4,4.2),2),rev=True,begin=round(random.uniform(0,3),2))
    preview.append(('dot',x,y,s,s,col[2:]))
# occasional shooting star
X.append('<Rectangle x:Name="ShootingStar" Width="70" Height="2" Canvas.Left="420" Canvas.Top="30" Opacity="0" RenderTransformOrigin="1,0.5"><Rectangle.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="1,0"><GradientStop Color="#00FFFFFF" Offset="0"/><GradientStop Color="#FFFFFFFF" Offset="1"/></LinearGradientBrush></Rectangle.Fill><Rectangle.RenderTransform><CompositeTransform Rotation="14"/></Rectangle.RenderTransform></Rectangle>')
anim('ShootingStar',TX,frames=[(0,0,False),(0.9,260,False),(23,260,True)],begin=6)
anim('ShootingStar',TY,frames=[(0,0,False),(0.9,65,False),(23,65,True)],begin=6)
anim('ShootingStar',OP,frames=[(0,0,False),(0.15,1,False),(0.9,0,False),(23,0,True)],begin=6)

# ---- city: aircraft beacons, flickering windows, searchlights, needle ring ----
for i,(x,y) in enumerate([(1085,94),(1096,96),(1042,116),(1227,101),(1305,128),(1178,146)]):
    n=f'Beacon{i}'
    X.append(f'<Ellipse x:Name="{n}" Width="6" Height="6" Canvas.Left="{x-3}" Canvas.Top="{y-3}" Fill="#FFFF3355" Opacity="0"/>')
    p=round(random.uniform(2.0,3.0),2); o=round(random.uniform(0,p),2)
    anim(n,OP,frames=[(0,0,True),(0.05,1,True),(0.35,0,True),(p,0,True)],begin=o)
    preview.append(('dot',x-3,y-3,6,6,'FF3355'))
wins=[(1043,160),(1047,178),(1092,150),(1098,170),(1094,196),(1175,172),(1181,192),(1229,158),(1233,182),(1001,205),(1124,198),(1265,212),(1150,220),(960,226)]
for i,(x,y) in enumerate(wins):
    n=f'Win{i}'; col=random.choice(['FF7FF2FF','FFFF7AC8','FFFFE38A'])
    X.append(f'<Rectangle x:Name="{n}" Width="4" Height="3" Canvas.Left="{x}" Canvas.Top="{y}" Fill="#{col}" Opacity="0"/>')
    p=round(random.uniform(5,14),1); on=round(random.uniform(1,p-1),1)
    anim(n,OP,frames=[(0,0,True),(on,0.9,True),(p,0,True)],begin=round(random.uniform(0,6),1))
    preview.append(('rect',x,y,4,3,col[2:]))
for i,(x,a,b,d) in enumerate([(1066,-28,18,11),(1248,22,-24,13)]):
    n=f'Searchlight{i}'
    X.append(f'<Polygon x:Name="{n}" Points="0,0 -16,-240 16,-240" Canvas.Left="{x}" Canvas.Top="240" Opacity="0.22"><Polygon.Fill><LinearGradientBrush StartPoint="0.5,1" EndPoint="0.5,0"><GradientStop Color="#AA9FF6FF" Offset="0"/><GradientStop Color="#009FF6FF" Offset="1"/></LinearGradientBrush></Polygon.Fill><Polygon.RenderTransform><CompositeTransform Rotation="{a}"/></Polygon.RenderTransform></Polygon>')
    anim(n,ROT,frm=a,to=b,dur=d,rev=True,ease=True)
    preview.append(('beam',x,240,a,0,'9FF6FF'))
for i,dx in enumerate([-24,-12,0,12,24]):
    n=f'Needle{i}'
    X.append(f'<Ellipse x:Name="{n}" Width="4" Height="4" Canvas.Left="{1303+dx}" Canvas.Top="172" Fill="#FF5EE7FF" Opacity="0.15"/>')
    anim(n,OP,frames=[(0,0.15,True),(i*0.25,1,True),(i*0.25+0.25,0.15,True),(1.6,0.15,True)])
    preview.append(('dot',1303+dx,172,4,4,'5EE7FF'))

# ---- hovercars weaving through the skyline ----
def car(n,left,top,scale,rtl,body,glow):
    w=26*scale; h=6*scale
    front=0 if rtl else w-3*scale; back=w-3*scale if rtl else 0
    return (f'<Canvas x:Name="{n}" Canvas.Left="{left}" Canvas.Top="{top}">{CT}'
            f'<Ellipse Width="{w+12*scale:g}" Height="{7*scale:g}" Canvas.Left="{-6*scale:g}" Canvas.Top="{h-1*scale:g}"><Ellipse.Fill><RadialGradientBrush><GradientStop Color="#99{glow}" Offset="0"/><GradientStop Color="#00{glow}" Offset="1"/></RadialGradientBrush></Ellipse.Fill></Ellipse>'
            f'<Rectangle Width="{w:g}" Height="{h:g}" RadiusX="{3*scale:g}" RadiusY="{3*scale:g}" Fill="#FF1A1238" Stroke="#FF{body}" StrokeThickness="1"/>'
            f'<Rectangle Width="{w*0.45:g}" Height="{2*scale:g}" Canvas.Left="{(w*0.12 if rtl else w*0.43):g}" Canvas.Top="{1*scale:g}" Fill="#CC9FF6FF"/>'
            f'<Ellipse Width="{3*scale:g}" Height="{3*scale:g}" Canvas.Left="{front:g}" Canvas.Top="{1.5*scale:g}" Fill="#FFFFFFFF"/>'
            f'<Ellipse Width="{3*scale:g}" Height="{3*scale:g}" Canvas.Left="{back:g}" Canvas.Top="{1.5*scale:g}" Fill="#FFFF3355"/></Canvas>')
lanes=[(118,0.8,False,26,'FF3AA7','FF3AA7',0),(146,1.0,True,20,'5EE7FF','5EE7FF',3),(176,1.2,False,17,'B26BFF','5EE7FF',8),(204,0.9,True,23,'FF3AA7','FF3AA7',12),(226,1.4,False,15,'5EE7FF','FF3AA7',5),(96,0.7,True,31,'FFE38A','B26BFF',15),(190,1.0,False,21,'FF3AA7','5EE7FF',18)]
for i,(y,s,rtl,d,body,glow,b) in enumerate(lanes):
    n=f'Car{i}'
    start,end=(1400,240) if rtl else (240,1400)
    X.append(car(n,0,y,s,rtl,body,glow))
    anim(n,TX,frm=start,to=end,dur=d,begin=b)
    anim(n,TY,frm=-3,to=3,dur=round(random.uniform(1.6,2.8),2),rev=True,ease=True)
    preview.append(('car',700,y,26*s,6*s,body))

# ---- elevated mag-lev line across the water + the cybertrain ----
X.append('<Rectangle Width="1100" Height="2" Canvas.Left="290" Canvas.Top="243" Opacity="0.75"><Rectangle.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="1,0"><GradientStop Color="#665EE7FF" Offset="0"/><GradientStop Color="#CCFF3AA7" Offset="0.5"/><GradientStop Color="#665EE7FF" Offset="1"/></LinearGradientBrush></Rectangle.Fill></Rectangle>')
for x in range(300,1390,56):
    X.append(f'<Rectangle Width="2" Height="9" Canvas.Left="{x}" Canvas.Top="244" Fill="#665EE7FF"/>')
preview.append(('rect',290,243,1100,2,'FF3AA7'))
tc=['<Canvas x:Name="Train" Canvas.Left="0" Canvas.Top="235">'+CT]
tc.append('<Rectangle Width="226" Height="5" Canvas.Left="0" Canvas.Top="19" Opacity="0.35"><Rectangle.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FF5EE7FF" Offset="0"/><GradientStop Color="#005EE7FF" Offset="1"/></LinearGradientBrush></Rectangle.Fill></Rectangle>')
tc.append('<Polygon Points="0,4 -70,-2 -70,12" Opacity="0.45"><Polygon.Fill><LinearGradientBrush StartPoint="1,0" EndPoint="0,0"><GradientStop Color="#FFFFFFFF" Offset="0"/><GradientStop Color="#00FFFFFF" Offset="1"/></LinearGradientBrush></Polygon.Fill></Polygon>')
for c in range(4):
    x0=c*57
    if c==0:
        tc.append(f'<Path Data="M 10,0 L 54,0 L 54,8 L 0,8 Q 2,1 10,0 Z" Canvas.Left="{x0}" Fill="#FF140E2E" Stroke="#FF5EE7FF" StrokeThickness="1"/>')
        tc.append(f'<Rectangle Width="30" Height="2" Canvas.Left="{x0+18}" Canvas.Top="2" Fill="#FFB8F8FF"/>')
        tc.append(f'<Ellipse Width="3" Height="3" Canvas.Left="{x0+1}" Canvas.Top="4" Fill="#FFFFFFFF"/>')
    else:
        tc.append(f'<Rectangle Width="54" Height="8" RadiusX="2" RadiusY="2" Canvas.Left="{x0}" Fill="#FF140E2E" Stroke="#FF5EE7FF" StrokeThickness="1"/>')
        for k in range(5):
            tc.append(f'<Rectangle Width="7" Height="2" Canvas.Left="{x0+5+k*9.5:g}" Canvas.Top="2" Fill="{"#FFB8F8FF" if (c+k)%3 else "#FFFF8FD0"}"/>')
    tc.append(f'<Rectangle Width="54" Height="1" Canvas.Left="{x0}" Canvas.Top="7" Fill="#FFFF3AA7"/>')
tc.append('<Ellipse Width="3" Height="3" Canvas.Left="223" Canvas.Top="3" Fill="#FFFF3355"/>')
tc.append('</Canvas>')
X.append(''.join(tc))
anim('Train',TX,frames=[(0,1420,False),(11,40,False),(28,40,True)],begin=2)
preview.append(('rect',600,235,226,8,'5EE7FF'))

# ---- boats on the grid sea ----
def boat(n,top,s,rtl,col):
    w=40*s
    hull=f'M 0,{4*s:g} L {w:g},{4*s:g} L {w-6*s:g},{9*s:g} L {6*s:g},{9*s:g} Z'
    cab_x=w*0.25 if rtl else w*0.45
    wake_pts=(f'{w:g},{7*s:g} {w+60*s:g},{6*s:g} {w+60*s:g},{9*s:g}' if rtl else f'0,{7*s:g} {-60*s:g},{6*s:g} {-60*s:g},{9*s:g}')
    ws,we=('1,0','0,0') if not rtl else ('0,0','1,0')
    return (f'<Canvas x:Name="{n}" Canvas.Left="0" Canvas.Top="{top}">{CT}'
            f'<Polygon Points="{wake_pts}" Opacity="0.6"><Polygon.Fill><LinearGradientBrush StartPoint="{"0,0" if rtl else "1,0"}" EndPoint="{"1,0" if rtl else "0,0"}"><GradientStop Color="#FF{col}" Offset="0"/><GradientStop Color="#00{col}" Offset="1"/></LinearGradientBrush></Polygon.Fill></Polygon>'
            f'<Rectangle Width="{w*0.3:g}" Height="{4*s:g}" Canvas.Left="{cab_x:g}" Fill="#FF140E2E" Stroke="#FF{col}" StrokeThickness="1"/>'
            f'<Rectangle Width="{w*0.18:g}" Height="{1.5*s:g}" Canvas.Left="{cab_x+w*0.06:g}" Canvas.Top="{1.2*s:g}" Fill="#FFFFE38A"/>'
            f'<Path Data="{hull}" Fill="#FF100A24" Stroke="#FF{col}" StrokeThickness="1"/>'
            f'<Rectangle Width="{w*0.9:g}" Height="{3*s:g}" Canvas.Left="{w*0.05:g}" Canvas.Top="{10*s:g}" Opacity="0.3"><Rectangle.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FF{col}" Offset="0"/><GradientStop Color="#00{col}" Offset="1"/></LinearGradientBrush></Rectangle.Fill></Rectangle>'
            f'<Ellipse x:Name="{n}Mast" Width="{3*s:g}" Height="{3*s:g}" Canvas.Left="{cab_x+w*0.12:g}" Canvas.Top="{-3*s:g}" Fill="#FF5EE7FF"/></Canvas>')
for i,(y,s,rtl,d,col,b) in enumerate([(254,0.55,True,70,'5EE7FF',0),(270,0.8,False,58,'FF3AA7',20),(292,1.15,True,48,'B26BFF',9)]):
    n=f'Boat{i}'
    X.append(boat(n,y,s,rtl,col))
    start,end=(1420,200) if rtl else (200,1420)
    anim(n,TX,frm=start,to=end,dur=d,begin=b)
    anim(n,TY,frm=-1,to=1.2,dur=round(random.uniform(1.8,2.6),2),rev=True,ease=True)
    anim(n+'Mast',OP,frames=[(0,1,True),(0.6,0.2,True),(1.4,0.2,True)])
    preview.append(('rect',500+i*120,y,40*s,9*s,col))

xaml=('<Canvas x:Name="CityLife" Width="1672" Height="941" Opacity="0.85">\n'
      '<Canvas.Clip><RectangleGeometry Rect="290,0,1096,322"/></Canvas.Clip>\n'
      '<Canvas.Resources><Storyboard x:Key="CityLifeStory">\n'+'\n'.join(A)+'\n</Storyboard></Canvas.Resources>\n'
      +'\n'.join(X)+'\n</Canvas>')
open(sys.argv[1],'w').write(xaml)
print(len(A),'animations',len(X),'elements')

from PIL import Image,ImageDraw
import math
im=Image.open('/home/claude/hall.jpg').convert('RGB'); d=ImageDraw.Draw(im)
for k,x,y,w,h,c in preview:
    rgb='#'+c[-6:]
    if k=='ring': d.ellipse((x-w*0.6,y-h*0.6,x+w*0.6,y+h*0.6),outline=rgb,width=2)
    elif k=='beam':
        a=math.radians(w); d.line((x,y,x+240*math.sin(a),y-240*math.cos(a)),fill=rgb,width=2)
    else: d.rectangle((x,y,x+w,y+h),fill=rgb)
im.crop((280,0,1400,380)).save('/home/claude/preview.png')
