// OFFENSE OF THE ALIENS intro + title screen: a SNES-style cold open (warp-in, Earth rising, the swarm arriving,
// the logo slamming down) and a chunky pixel-font title menu drawn straight into the game canvas.
// ui.js owns game state; this module only draws and turns input into {act:"new"|"continue"|...} calls.
const G={A:".###.#...##...#######...##...##...#",B:"####.#...##...#####.#...##...#####.",C:".###.#...##....#....#....#...#.###.",D:"####.#...##...##...##...##...#####.",E:"######....#....####.#....#....#####",F:"######....#....####.#....#....#....",
 G:".###.#...##....#.####...##...#.####",H:"#...##...##...#######...##...##...#",I:"#####..#....#....#....#....#..#####",J:"..###...#....#....#.#..#.#..#..##..",K:"#...##..#.#.#..##...#.#..#..#.#...#",L:"#....#....#....#....#....#....#####",
 M:"#...###.###.#.##.#.##...##...##...#",N:"#...###..##.#.##..###...##...##...#",O:".###.#...##...##...##...##...#.###.",P:"####.#...##...#####.#....#....#....",Q:".###.#...##...##...##.#.##..#..##.#",R:"####.#...##...#####.#.#..#..#.#...#",
 S:".#####....#.....###.....#....#####.",T:"#####..#....#....#....#....#....#..",U:"#...##...##...##...##...##...#.###.",V:"#...##...##...##...##...#.#.#...#..",W:"#...##...##...##.#.##.#.###.###...#",X:"#...##...#.#.#...#...#.#.#...##...#",
 Y:"#...##...#.#.#...#....#....#....#..",Z:"#####....#...#...#...#...#....#####","0":".###.#...##..###.#.###..##...#.###.","1":"..#...##....#....#....#....#...###.","2":".###.#...#....#...#...#...#...#####","3":"####.....#....#.###.....#....#####.",
 "4":"...#...##..#.#.#..#.#####...#....#.","5":"######....####.....#....##...#.###.","6":".###.#....#....####.#...##...#.###.","7":"#####....#...#...#...#....#....#...","8":".###.#...##...#.###.#...##...#.###.","9":".###.#...##...#.####....#....#.###.",
 ".":"..................................#.",":":"......#..............#.......",
 "/":"....#....#...#...#...#...#....#....","-":"...............###.................","!":"..#....#....#....#....#.........#..","?":".###.#...#....#...#...#.........#..","+":"comments",",":"......................#....#...#...",
 "'":"..#....#...........................","*":"     #.#.#.###.#####.###.#.#.#     "," ":"..................................."};
G["+"]=".......#....#..#####..#....#.......";G[":"]="......#..................#.........";G["."]="...............................#...";G["*"]=".....#.#.#.###.#####.###.#.#.#.....";
const glyph=c=>{const s=G[c]||G[c.toUpperCase()]||G[" "];return i=>s[i]==="#"};
const width=(str,b)=>str.length*6*b-b;
// chunky bevelled block text (pre-rendered): gradient fill, black outline, top light / bottom dark bevel, drop shadow
function blockText(str,b,{top,stops=null,depth=0,hi="rgba(255,255,255,.55)",lo="rgba(0,0,0,.35)",outline="#05040a",shadow="rgba(0,0,0,.55)",drip=null}={}){
 const pad=Math.ceil(b*1.6)+depth*2,w=width(str,b)+pad*2,h=7*b+pad*2+(drip?b*5:0),c=document.createElement("canvas");c.width=w;c.height=h;const x=c.getContext("2d");
 const on=(gx,gy)=>{if(gy<0||gy>6)return false;const ci=Math.floor(gx/6),cx=gx%6;if(ci<0||ci>=str.length||cx>4)return false;return glyph(str[ci])(gy*5+cx)};
 const cells=[];for(let gy=0;gy<7;gy++)for(let gx=0;gx<str.length*6;gx++)if(on(gx,gy))cells.push([gx,gy]);
 const X=gx=>pad+gx*b,Y=gy=>pad+gy*b,o=Math.max(2,Math.round(b*.28));
 x.fillStyle=shadow;for(const[gx,gy]of cells)x.fillRect(X(gx)+o*1.6+depth,Y(gy)+o*2+depth*1.3,b,b);
 for(let d=depth;d>0;d--){x.fillStyle=d===depth?outline:`rgb(${8+d*2},${14+d*3},${60+d*9})`;for(const[gx,gy]of cells)x.fillRect(X(gx)-o+d,Y(gy)-o+d*1.3,b+o*2,b+o*2)}
 x.fillStyle=outline;for(const[gx,gy]of cells)x.fillRect(X(gx)-o,Y(gy)-o,b+o*2,b+o*2);
 const gr=x.createLinearGradient(0,pad,0,pad+7*b);if(stops)stops.forEach(([p,c])=>gr.addColorStop(p,c));else top.forEach((c,i)=>gr.addColorStop(i/(top.length-1),c));x.fillStyle=gr;for(const[gx,gy]of cells)x.fillRect(X(gx),Y(gy),b,b);
 const e=Math.max(1,Math.round(b*.22));
 for(const[gx,gy]of cells){if(!on(gx,gy-1)){x.fillStyle=hi;x.fillRect(X(gx),Y(gy),b,e)}if(!on(gx-1,gy)){x.fillStyle=hi;x.fillRect(X(gx),Y(gy),e,b)}if(!on(gx,gy+1)){x.fillStyle=lo;x.fillRect(X(gx),Y(gy)+b-e,b,e)}if(!on(gx+1,gy)){x.fillStyle=lo;x.fillRect(X(gx)+b-e,Y(gy),e,b)}}
 if(drip){let k=7;for(const[gx,gy]of cells)if(!on(gx,gy+1)&&((gx*31+gy*7+k)%5===0)){const len=b*(1+(gx*13)%4);x.fillStyle=outline;x.fillRect(X(gx)+b*.2-o,Y(gy)+b,b*.6+o*2,len+o);x.fillStyle=drip;x.fillRect(X(gx)+b*.2,Y(gy)+b,b*.6,len);x.beginPath();x.arc(X(gx)+b/2,Y(gy)+b+len,b*.42,0,6.283);x.fill()}}
 return{canvas:c,pad,w,h}}
const ease=t=>1-Math.pow(1-Math.min(1,Math.max(0,t)),3),back=t=>{t=Math.min(1,Math.max(0,t));const c=1.70158;return 1+(c+1)*Math.pow(t-1,3)+c*Math.pow(t-1,2)};
export function createTitle({ctx,CW,CH,atlas,planetOf,skyOf,stars,audio,rumble,act,items}){
 const LOGO=blockText("OFFENSE",19,{stops:[[0,"#ffffff"],[.3,"#b8f4ff"],[.49,"#2e8fd6"],[.5,"#0b1250"],[.53,"#fff3c2"],[.75,"#ff9a2e"],[1,"#7a2b06"]],depth:9,hi:"rgba(255,255,255,.7)"}),
  SUB=blockText("OF THE ALIENS",6,{top:["#c8ff9a","#5fd456","#22983a","#145a24"],drip:"#5fd456"}),
  PRESENTS=blockText("ARCADEGRIDZ PRESENTS",3,{top:["#ffffff","#9fb0d8"]}),
  PRESS=blockText("PRESS START",5,{top:["#fff6b8","#ffd93a","#ff9a2e"]}),
  CREDIT=blockText("ORIGINAL 2014 BY SYCX1 // REBUILT FOR ARCADEGRIDZ",2,{top:["#9fb0d8","#6c7699"],shadow:"rgba(0,0,0,0)"});
 const cache=new Map(),label=(s,sel,off)=>{const k=s+(sel?1:off?2:0);if(!cache.has(k))cache.set(k,blockText(s,sel?6:5,{top:sel?["#fff6b8","#ffd93a","#ff9a2e","#d0560c"]:off?["#4a4a66","#33334a"]:["#ffffff","#d6dbe8","#9fb0d8"]}));return cache.get(k)},
  hintText=s=>{const k="h:"+s;if(!cache.has(k))cache.set(k,blockText(s,2,{top:["#d2fbff","#5ee7ff"],shadow:"rgba(0,0,0,.6)"}));return cache.get(k)};
 const T={phase:"intro",t0:performance.now()/1000,idx:0,list:"main",pressT:0,swarm:[],shake:0,flash:0,slam:false,hit:[0]};
 for(let i=0;i<13;i++){const row=i<7?0:1,col=row?i-7:i;T.swarm.push({x:(row?-2.5:-3)+col,y:row,v:i%3?0:1,delay:i*.05})}
 const now=()=>performance.now()/1000-T.t0;
 function setPhase(p){T.phase=p;T.t0=performance.now()/1000;T.flash=0;if(p==="menu"){T.idx=Math.max(0,list().findIndex(i=>!i.off()))}}
 const list=()=>items(T.list);
 function move(d){const L=list();let i=T.idx;for(let n=0;n<L.length;n++){i=(i+d+L.length)%L.length;if(!L[i].off())break}if(i!==T.idx){T.idx=i;audio.sfx("pick")}}
 function choose(){const it=list()[T.idx];if(!it||it.off()){audio.sfx("deny");return}audio.sfx("ok");it.act()}
 // input: name = up|down|left|right|ok|back|start ; returns true when used
 function input(name){const p=T.phase;
  if(p==="intro"){if(name==="ok"||name==="start"||name==="back"){setPhase("press");T.slam=true}return true}
  if(p==="press"){if(name==="ok"||name==="start"){audio.sfx("ok");setPhase("menu");rumble?.(120,.5)}return true}
  if(name==="up")move(-1);else if(name==="down")move(1);else if(name==="ok"||name==="start")choose();else if(name==="back"){if(T.list!=="main"){const up=T.list==="play"?"main":"play";T.idx=up==="main"?0:T.list==="defense"?2:1;T.list=up;audio.sfx("deny")}else{setPhase("press")}}return true}
 function openList(name,idx=0){T.list=name;T.idx=idx;const L=list();if(L[T.idx]?.off())move(1)}
 // menu row geometry (canvas px) for mouse hit tests
 const rowY=i=>CH*.47+i*58;
 function click(x,y){if(T.phase!=="menu"){input("ok");return}const L=list();for(let i=0;i<L.length;i++)if(Math.abs(y-(rowY(i)+20))<26){if(T.idx===i)choose();else if(!L[i].off()){T.idx=i;audio.sfx("pick")}return}}
 function hover(x,y){if(T.phase!=="menu")return;const L=list();for(let i=0;i<L.length;i++)if(Math.abs(y-(rowY(i)+20))<26&&i!==T.idx&&!L[i].off()){T.idx=i}}
 function drawWarp(t){ctx.fillStyle="#020108";ctx.fillRect(0,0,CW,CH);const cx=CW/2,cy=CH*.42,sp=Math.min(1,t/1.4);
  for(let i=0;i<140;i++){const a=(i*137.5)%360*Math.PI/180,r0=((i*53)%100)/100,d=((r0+t*(0.25+sp*1.6))%1),r=Math.pow(d,2)*CW*.8,len=6+sp*sp*120*d;
   ctx.strokeStyle=["#ffffff","#bcd2ff","#ffd0e8","#5ee7ff"][i%4];ctx.globalAlpha=Math.min(1,d*2);ctx.lineWidth=1+d*3;ctx.beginPath();ctx.moveTo(cx+Math.cos(a)*r,cy+Math.sin(a)*r);ctx.lineTo(cx+Math.cos(a)*(r+len),cy+Math.sin(a)*(r+len));ctx.stroke()}ctx.globalAlpha=1}
 function drawScene(t,dimDemo){// Earth rising, the human ship on patrol, the swarm warping in
  if(!dimDemo){ctx.drawImage(skyOf(1),0,0,CW,CH);for(const[i,img]of stars.entries()){ctx.globalAlpha=i?.6:1;ctx.drawImage(img,0,0,CW,CH)}ctx.globalAlpha=1}
  const rise=ease((t-.1)/2.2),pw=CW*1.05,ph=pw*140/196;ctx.drawImage(planetOf(1),(CW-pw)/2,CH-ph*.32*rise-4,pw,ph);
  const sx=CW/2+Math.sin(t*1.3)*CW*.22,sy=CH*.8-ph*.32*rise*.15;if(!dimDemo){ctx.drawImage(atlas.ship,sx-32,sy-32,64,64)}
  for(const a of T.swarm){const k=ease((t-1.1-a.delay)/.55);if(k<=0)continue;const tx=CW/2+a.x*78,ty=CH*.6+a.y*70-Math.abs(a.x)*14,y=-80+(ty+80)*k,f=(Math.floor(t*4)+a.v)&1,img=atlas.alien[a.v][f];
   if(k<1){ctx.strokeStyle=a.v?"rgba(178,91,210,.6)":"rgba(95,212,86,.6)";ctx.lineWidth=10;ctx.beginPath();ctx.moveTo(tx,y-120*(1-k));ctx.lineTo(tx,y);ctx.stroke()}
   ctx.drawImage(img,tx-34,y-34+Math.sin(t*3+a.x)*4,68,68)}}
 function drawLogo(t,y0,slamAt){const k=Math.min(1,Math.max(0,(t-slamAt)/.28));if(k<=0)return;const sc=3-2*ease(k),w=LOGO.w*sc,h=LOGO.h*sc;
  if(k>=1&&!T.hit.includes(slamAt)){T.hit.push(slamAt);T.shake=14;T.flash=.8;audio.sfx("planetboom");rumble?.(300,.9)}
  ctx.globalAlpha=Math.min(1,k*2.5);ctx.drawImage(LOGO.canvas,(CW-w)/2,y0+LOGO.h/2-h/2,w,h);ctx.globalAlpha=1;
  // shine sweep
  if(k>=1){const s=((t-slamAt)%3.2)/1.1;if(s<1){ctx.save();ctx.globalCompositeOperation="source-atop";ctx.restore();const bx=(CW-LOGO.w)/2+(LOGO.w+200)*s-100;ctx.save();ctx.beginPath();ctx.rect((CW-LOGO.w)/2,y0,LOGO.w,LOGO.h);ctx.clip();ctx.globalAlpha=.35;ctx.fillStyle="#ffffff";ctx.beginPath();ctx.moveTo(bx,y0);ctx.lineTo(bx+40,y0);ctx.lineTo(bx-20,y0+LOGO.h);ctx.lineTo(bx-60,y0+LOGO.h);ctx.fill();ctx.restore()}}
  // OF THE ALIENS pops in letter by letter
  const st=t-slamAt-.35;if(st>0){const n=Math.min(13,Math.floor(st*18)+1),cw=6*6,x0=(CW-SUB.w)/2,yy=y0+LOGO.h-18;ctx.save();ctx.beginPath();ctx.rect(x0,yy,SUB.pad+n*cw,SUB.h);ctx.clip();const bob=Math.sin(t*2)*3;ctx.drawImage(SUB.canvas,x0,yy+bob);ctx.restore()}}
 function draw(dimDemo){const t=now(),p=T.phase;ctx.save();
  if(T.shake>0){ctx.translate((Math.random()-.5)*T.shake,(Math.random()-.5)*T.shake);T.shake*=.85;if(T.shake<.5)T.shake=0}
  if(p==="intro"){
   if(t<1.6){drawWarp(t);const a=Math.min(1,t*2)*Math.min(1,(1.6-t)*3);ctx.globalAlpha=a;ctx.drawImage(PRESENTS.canvas,(CW-PRESENTS.w)/2,CH*.42-PRESENTS.h/2);ctx.globalAlpha=1}
   else{const s=t-1.6;drawScene(s,false);drawLogo(s,CH*.08,2.2);if(s<.25){ctx.fillStyle=`rgba(255,255,255,${1-s*4})`;ctx.fillRect(0,0,CW,CH)}if(s>3.6)setPhase("press")}}
  else{drawScene(9+t,false);if(dimDemo){ctx.fillStyle="rgba(3,2,12,.35)";ctx.fillRect(0,0,CW,CH);const v=ctx.createRadialGradient(CW/2,CH/2,CH*.3,CW/2,CH/2,CW*.7);v.addColorStop(0,"rgba(0,0,0,0)");v.addColorStop(1,"rgba(0,0,0,.65)");ctx.fillStyle=v;ctx.fillRect(0,0,CW,CH)}
   const up=p==="menu"?ease(t/.35):0,ly=CH*.08-up*CH*.04;drawLogo(T.slam?99:t+10,ly,0);
   if(p==="press"){if(Math.floor(t*2.2)%2===0)ctx.drawImage(PRESS.canvas,(CW-PRESS.w)/2,CH*.68);ctx.drawImage(CREDIT.canvas,(CW-CREDIT.w)/2,CH-CREDIT.h-14)}
   else{const L=list(),top=rowY(0)-20,bot=rowY(L.length)+34,bg=ctx.createLinearGradient(0,top-40,0,bot+20);bg.addColorStop(0,"rgba(3,2,12,0)");bg.addColorStop(.15,"rgba(3,2,12,.82)");bg.addColorStop(.85,"rgba(3,2,12,.82)");bg.addColorStop(1,"rgba(3,2,12,0)");ctx.fillStyle=bg;ctx.fillRect(0,top-40,CW,bot-top+60);L.forEach((it,i)=>{const sel=i===T.idx,off=it.off(),lab=label(it.label,sel,off),slide=(1-ease((t-.08*i)/.3))*CW*.6,y=rowY(i)+(sel?-3:0),x=(CW-lab.w)/2+slide+(sel?Math.sin(t*6)*2:0);
     ctx.drawImage(lab.canvas,x,y);if(sel){const f=Math.floor(t*4)&1,ax=x-70+Math.sin(t*8)*5;ctx.drawImage(atlas.alien[0][f],ax,y+lab.pad-6,56,56);ctx.save();ctx.scale(-1,1);ctx.drawImage(atlas.alien[1][f],-(x+lab.w+70-Math.sin(t*8)*5),y+lab.pad-6,56,56);ctx.restore()}});
    const it=L[T.idx],h=it&&hintText(typeof it.hint==="function"?it.hint():it.hint||"");if(h&&h.w>8)ctx.drawImage(h.canvas,(CW-h.w)/2,rowY(L.length)+6)}}
  ctx.restore();if(T.flash>0){ctx.fillStyle=`rgba(255,255,255,${T.flash})`;ctx.fillRect(0,0,CW,CH);T.flash=Math.max(0,T.flash-.06)}}
 return{draw,input,click,hover,setPhase,openList,get phase(){return T.phase},get list(){return T.list}}}
