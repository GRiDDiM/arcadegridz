// OFFENSE OF THE ALIENS v2 browser layer: renderer (original 310x180 screen drawn 4x, zooming out as the swarm grows),
// SNES-style menus (shared potato kit), campaign flow (NEW GAME / CONTINUE, levels, bonus stages, ending) and input.
// Controller: the d-pad hops the selection between aliens (no fake cursor), A opens the order menu, Y repeats the last
// order, X snatches loot, B sends the swarm rushing, L picks ONE / ROW / ALL, hold R + d-pad shifts the formation.
import{W,H,DT,CX,CMD,CMD_NAMES,CMD_POS,DIFF,LEVELS,createGame,step,snapshot,idleAliens,commandable,lockedCmd,fx0,fx1,bot,surfaceY}from"./engine.js?v=1102-v2a";
import{buildAtlas,buildPlanet,buildSky,buildStars}from"./sprites.js?v=1102-v2a";
import{createAudio}from"./audio.js?v=1013-oota1";
import{isPad,padDir,padLabel,anyPad,createBindings,createMenu,controlsScreen}from"/potato-kit.js?v=1101-pk2";
const GAME_ID="offense-of-the-aliens",S=4,CW=W*S,CH=H*S,RUN_KEY="run.v2";
const store={get(k,d){try{const v=localStorage.getItem("oota."+k);return v==null?d:JSON.parse(v)}catch{return d}},set(k,v){try{localStorage.setItem("oota."+k,JSON.stringify(v))}catch{}},del(k){try{localStorage.removeItem("oota."+k)}catch{}}};
const fmt=n=>Math.round(n).toLocaleString("en-US");
// radio chatter: the original's lines for Earth, Mars and Jupiter, new ones for the rest of the campaign
const DIALOGUE={earth:["HUMAN: Halt there!","YOU: AHAH HA. *SLURP!*","HUMAN: I warned you.","YOU: *Strange noises!*","HUMAN: Leave now!"],
 mars:["YOU: *slurp* *SLURLP!*","HUMAN: The aliens are here!","HUMAN: Mars is in danger!","YOU: *BLUUURP*","HUMAN: Die alien scum!"],
 venus:["HUMAN: Wingmen, form up!","YOU: *bubble bubble*","HUMAN: Watch the acid clouds!","YOU: *HISSSS*"],
 jupiter:["HUMAN: More aliens?!","YOU: *REHHZZZblblblbl*","HUMAN: Not Jupiter station!","HUMAN: You shall not pass!!!","HUMAN: Game over, man!"],
 saturn:["HUMAN: Carrier, launch everything!","YOU: *CLICK CLICK CLICK*","HUMAN: Hold the rings!"],
 neptune:["HUMAN: Medics, keep us flying!","HUMAN: Frigates, shields up!","YOU: *gurgle* *GURGLE*","HUMAN: They're diving under the domes!"],
 planetx:["ADMIRAL: So you found us.","ADMIRAL: Every ship. Every pilot. NOW!","YOU: *THE SWARM SINGS*","ADMIRAL: Planet X will not fall!"]};
const BOSS_LINES={earth:["HUMAN: Boss! Help me!","BOSS: WHAT THE...","BOSS: Why are you trying...","BOSS: to take on a...","BOSS: SWARM of aliens!","BOSS: ALL ALONE?!!!","HUMAN: It worked in the...","HUMAN: Video Games!!!"],
 mars:["BOSS: I will decimate you!"],jupiter:["BOSS: This is not over!!"],venus:["BOSS: You'll melt like the rest!"],saturn:["BOSS: Ring guard, engage!"],neptune:["BOSS: Down into the deep with you!"],planetx:["ADMIRAL: I'll finish this myself!"]};
const SONG=["earth","mars","menu","mars","jupiter","menu","jupiter","earth","menu","jupiter"];
const KEYCMD={Digit1:CMD.ATTACK,Digit2:CMD.DIVE,Digit3:CMD.BUGS,Digit4:CMD.TENTACLES,Digit5:CMD.RANDOM};
const ARROWS=[{x:77,y:156,dir:-1},{x:233,y:156,dir:1}];
const ORDER=[CMD.ATTACK,CMD.DIVE,CMD.BUGS,CMD.TENTACLES,CMD.RANDOM];
const ORDER_HINT={[CMD.ATTACK]:"SHOOT STRAIGHT DOWN",[CMD.DIVE]:"DIVEBOMB THE NEAREST SHIP",[CMD.BUGS]:"SPIT HOMING BUGS",[CMD.TENTACLES]:"LASH WHATEVER IS BELOW",[CMD.RANDOM]:"SURPRISE ME"};
// controller actions (pad buttons arrive from gamepad.js as synthetic key codes; SNES names shown by default)
const ACTIONS=[
 {id:"cursor",label:"SELECT ALIEN",def:"D-PAD",fixed:true,hint:"HOPS BETWEEN YOUR ALIENS"},
 {id:"order",label:"ORDER MENU",def:"Escape",hint:"PICK WHAT THE SELECTION DOES"},
 {id:"repeat",label:"REPEAT LAST ORDER",def:"KeyZ",hint:"GIVE THE SAME ORDER AGAIN"},
 {id:"snatch",label:"SNATCH LOOT",def:"KeyX",hint:"GRAB GREEN, BLOCK RED"},
 {id:"rush",label:"SWARM RUSH",def:"Space",hint:"EVERYONE DIVES AT THE SHIP"},
 {id:"scope",label:"GROUP: ONE / ROW / ALL",def:"ShiftLeft"},
 {id:"shift",label:"HOLD + D-PAD: SHIFT SWARM",def:"KeyE"},
 {id:"idle",label:"NEXT IDLE ALIEN",def:"KeyM"},
 {id:"pause",label:"PAUSE MENU",def:"KeyP",fixed:true}];
const SCOPES=["ONE","ROW","ALL"];
const potato=()=>document.documentElement.classList.contains("agbm-on")||document.documentElement.classList.contains("ag-winapp");
function fit(stage){const host=stage.parentElement;if(!host)return;
 if(potato()){const w=host.clientWidth||innerWidth,h=host.clientHeight||innerHeight,s=Math.min(w/CW,h/CH);stage.style.transform=`translateX(-50%) scale(${s})`;stage.style.top=`${Math.max(0,(h-CH*s)/2)}px`;host.style.height="100%";return}
 const avail=Math.max(240,innerHeight-150),s=Math.min(host.clientWidth/CW,avail/CH);stage.style.top="0";stage.style.transform=`translateX(-50%) scale(${s})`;host.style.height=`${CH*s}px`}
export function mountOota(root,{onExit}={}){
 root.innerHTML=`<section class="oa-shell"><div class="oa-stage"><canvas width="${CW}" height="${CH}" tabindex="0" aria-label="Offense of the Aliens"></canvas><div class="oa-scan"></div>
 <div class="oa-banner" data-oa-banner hidden></div>
 <div class="oa-controls"><button data-oa="menu">MENU</button><button data-oa="pause">PAUSE</button><button data-oa="audio">AUDIO</button><button data-oa="exit">× EXIT</button></div></div></section>`;
 document.body.classList.add("oa-playing");
 const stage=root.querySelector(".oa-stage"),canvas=root.querySelector("canvas"),ctx=canvas.getContext("2d"),bannerEl=root.querySelector("[data-oa-banner]");
 ctx.imageSmoothingEnabled=false;
 const atlas=buildAtlas(),planets=[],skies=[],planetOf=k=>planets[k]||(planets[k]=buildPlanet(k)),skyOf=k=>skies[k]||(skies[k]=buildSky(k)),stars=[buildStars(3),buildStars(9)],audio=createAudio(),reduceMotion=matchMedia?.("(prefers-reduced-motion: reduce)").matches;
 let opts={difficulty:store.get("difficulty",2),dial:store.get("dial",true),assist:store.get("assist",false),slowmo:store.get("slowmo",true),shake:store.get("shake",true)};
 let g=null,demo=true,paused=false,focusPaused=false,destroyed=false,raf=0,last=performance.now(),acc=0,publishT=0,submitted=false,endT=0,bannerTimer=0,tick=0,shake=0,flash=0,hitstop=0,result=null,run=null;
 let particles=[],pops=[],fx=[],taps=[],shiftHold={l:false,r:false},inpQ={},hoverX=CX,dial=null;
 const binds=createBindings("oota.padmap.v2",ACTIONS);
 const sel={on:anyPad(),anchor:0,scope:0,menu:null,pick:null,last:null,shift:false,ax:CX,ay:20,dirs:{up:0,down:0,left:0,right:0}};
 const menu=createMenu(stage,{sfx:n=>{if(n==="move")audio.sfx("pick");else if(n==="ok")audio.sfx("ok");else if(n==="back")audio.sfx("deny")}});
 const L=id=>padLabel(binds.get(id));
 const rumble=(ms,s)=>{try{if(!demo)window.ArcadeGridzPad?.rumble?.(ms,s)}catch{}};
 const live=()=>!demo&&g&&g.state==="play"&&!paused;
 function resetHolds(){shiftHold.l=shiftHold.r=false;sel.shift=false;for(const k in sel.dirs)sel.dirs[k]=0}
 // ---------- selection (controller + keyboard) ----------
 const selectable=a=>!a.kami&&!a.mergeInto&&!a.dead;
 const anchorAlien=()=>g?.aliens.find(a=>a.id===sel.anchor&&selectable(a))||null;
 function ensureAnchor(){if(!g)return null;let a=anchorAlien();if(a){sel.ax=a.x;sel.ay=a.y;return a}
  let best=null,bd=1e9;for(const o of g.aliens){if(!selectable(o)||o.y<-4)continue;const d=Math.hypot(o.x-sel.ax,o.y-sel.ay);if(d<bd){bd=d;best=o}}sel.anchor=best?.id||0;return best}
 function selIds(){const a=ensureAnchor();if(!a)return[];if(sel.scope===0)return[a.id];
  if(sel.scope===1)return g.aliens.filter(o=>selectable(o)&&Math.abs(o.mey-a.mey)<4).map(o=>o.id);return g.aliens.filter(selectable).map(o=>o.id)}
 function hop(dx,dy){const a=ensureAnchor();if(!a)return;let best=null,bc=1e9;
  for(const o of g.aliens){if(o===a||!selectable(o)||o.y<-4)continue;const vx=o.mex-a.mex,vy=o.mey-a.mey,proj=vx*dx+vy*dy;if(proj<3)continue;const perp=Math.abs(vx*dy-vy*dx),c=proj+perp*2.4;if(c<bc){bc=c;best=o}}
  if(best){sel.anchor=best.id;sel.ax=best.x;sel.ay=best.y;audio.sfx("pick")}}
 function nextIdle(){const a=ensureAnchor(),idle=idleAliens(g).filter(o=>o.y>-4);if(!idle.length){audio.sfx("deny");return}
  idle.sort((p,q)=>Math.hypot(p.x-(a?.x??CX),p.y-(a?.y??20))-Math.hypot(q.x-(a?.x??CX),q.y-(a?.y??20)));const n=idle.find(o=>o.id!==sel.anchor)||idle[0];sel.anchor=n.id;sel.scope=0;audio.sfx("pick")}
 function orderItems(){const a=anchorAlien(),items=ORDER.map(c=>({cmd:c,label:CMD_NAMES[c],off:lockedCmd(g,c),hint:lockedCmd(g,c)?"LOCKED: GRAB POWER-UPS":ORDER_HINT[c]}));
  if(sel.scope===0&&a&&a.sparkle>0&&a.species===1)items.push({merge:true,label:"MERGE",hint:"PICK A PARTNER TO FUSE WITH"});return items}
 function openOrders(){if(!live()||!ensureAnchor())return;const items=orderItems();let idx=Math.max(0,items.findIndex(i=>i.cmd===sel.last&&!i.off));if(items[idx].off)idx=0;sel.menu={idx,items};audio.sfx("pick")}
 function confirmOrder(){const m=sel.menu;if(!m)return;const it=m.items[m.idx];if(it.off){audio.sfx("deny");return}sel.menu=null;
  if(it.merge){sel.pick={src:sel.anchor};banner("PICK A PARTNER","HOP TO ANOTHER ALIEN, THEN PRESS "+L("order"));return}
  inpQ.command={cmd:it.cmd,ids:selIds()};sel.last=it.cmd}
 function repeatOrder(){if(sel.last==null){openOrders();return}if(lockedCmd(g,sel.last)){audio.sfx("deny");return}const ids=selIds();if(ids.length)inpQ.command={cmd:sel.last,ids}}
 function action(name,down,e){sel.on=true;
  if(name==="shift"){sel.shift=down;if(!down)shiftHold.l=shiftHold.r=false;return}
  if(name==="dir"){const d=e;sel.dirs[d]=down?1:0;
   if(sel.shift&&(d==="left"||d==="right")){shiftHold[d==="left"?"l":"r"]=down;return}
   if(!down||!live())return;
   if(sel.menu){const n=sel.menu.items.length;if(d==="up")sel.menu.idx=(sel.menu.idx-1+n)%n;else if(d==="down")sel.menu.idx=(sel.menu.idx+1)%n;audio.sfx("pick");return}
   hop(d==="left"?-1:d==="right"?1:0,d==="up"?-1:d==="down"?1:0);return}
  if(!down)return;
  if(name==="pause"){openPause();return}
  if(!live())return;audio.unlock();
  if(sel.menu){if(name==="order"||name==="repeat")confirmOrder();else if(name==="rush"){sel.menu=null;audio.sfx("deny")}return}
  if(sel.pick){if(name==="order"){if(sel.anchor!==sel.pick.src)inpQ.merge={src:sel.pick.src,dst:sel.anchor};else audio.sfx("deny");sel.pick=null}else if(name==="rush"){sel.pick=null;audio.sfx("deny")}return}
  if(name==="order")openOrders();else if(name==="repeat")repeatOrder();else if(name==="snatch")inpQ.snatch=true;else if(name==="rush")inpQ.rush=true;
  else if(name==="scope"){sel.scope=(sel.scope+1)%3;audio.sfx("pick")}else if(name==="idle")nextIdle()}
 // ---------- input ----------
 const KB={ArrowUp:["dir","up"],ArrowDown:["dir","down"],ArrowLeft:["dir","left"],ArrowRight:["dir","right"],KeyW:["dir","up"],KeyS:["dir","down"],KeyA:["dir","left"],KeyD:["dir","right"],
  KeyZ:["order"],Enter:["order"],KeyC:["repeat"],KeyX:["snatch"],Space:["rush"],KeyQ:["scope"],Tab:["idle"],ShiftLeft:["shift"],ShiftRight:["shift"],KeyP:["pause"],Escape:["pause"]};
 const onKey=e=>{if(e.target.closest?.("input,textarea,select"))return;
  if(menu.handleKey(e)){if(isPad(e))sel.on=true;e.preventDefault();return}
  const down=e.type==="keydown";
  if(isPad(e)){e.preventDefault();const d=padDir(e);if(d){action("dir",down,d);return}if(e.code==="KeyP"){action("pause",down);return}const a=binds.actionFor(e.code);if(a)action(a,down);return}
  if(down&&e.code==="KeyM"){toggleAudio();e.preventDefault();return}
  if(down&&KEYCMD[e.code]!=null&&live()){const ids=selIds();inpQ.command=ids.length&&sel.on?{cmd:KEYCMD[e.code],ids}:{cmd:KEYCMD[e.code],near:hoverX};sel.last=KEYCMD[e.code];e.preventDefault();return}
  const k=KB[e.code];if(!k)return;e.preventDefault();if(e.repeat&&k[0]!=="dir")return;if(k[0]==="dir")action("dir",down,k[1]);else action(k[0],down)};
 addEventListener("keydown",onKey);addEventListener("keyup",onKey);
 const view=()=>g?.z||1;
 const world=e=>{const r=canvas.getBoundingClientRect(),sx=(e.clientX-r.left)/r.width*W,sy=(e.clientY-r.top)/r.height*H,z=view();return{x:CX+(sx-CX)/z,y:sy/z}};
 let arrowPtr=null;
 canvas.addEventListener("pointermove",e=>{hoverX=world(e).x;if(e.pointerType==="mouse"&&(e.movementX||e.movementY))sel.on=false});
 canvas.addEventListener("pointerdown",e=>{if(!live())return;const p=world(e);e.preventDefault();canvas.focus?.({preventScroll:true});audio.unlock();sel.on=false;sel.menu=null;sel.pick=null;
  const r=canvas.getBoundingClientRect(),sx=(e.clientX-r.left)/r.width*W,sy=(e.clientY-r.top)/r.height*H;
  for(const a of ARROWS)if(Math.abs(sx-a.x)<14&&Math.abs(sy-a.y)<26){arrowPtr={id:e.pointerId,dir:a.dir};return}
  taps.push(p)});
 const up=e=>{if(arrowPtr&&arrowPtr.id===e.pointerId)arrowPtr=null};
 canvas.addEventListener("pointerup",up);canvas.addEventListener("pointercancel",up);
 function readInput(){const inp=inpQ;inpQ={};const sh=(shiftHold.r||arrowPtr?.dir===1?1:0)-(shiftHold.l||arrowPtr?.dir===-1?1:0);if(sh)inp.shift=sh*1.4;
  const t=taps.shift();if(t)inp.tap=t;return inp}
 // ---------- events / fx ----------
 function burst(x,y,c,n=10,sp=1.2,grav=0){if(reduceMotion)n=Math.min(n,4);for(let i=0;i<n;i++){const a=Math.random()*6.283,v=.3+Math.random()*sp;particles.push({x,y,vx:Math.cos(a)*v,vy:Math.sin(a)*v,life:10+Math.random()*14,c,grav,sz:grav?2:1})}}
 function banner(text,sub=""){bannerEl.hidden=false;bannerEl.innerHTML=`<b>${text}</b>${sub?`<span>${sub}</span>`:""}`;bannerEl.classList.remove("on");void bannerEl.offsetWidth;bannerEl.classList.add("on");clearTimeout(bannerTimer);bannerTimer=setTimeout(()=>{bannerEl.hidden=true},2300)}
 function say(lines){if(!opts.dial||demo||!lines)return;dial={lines,i:0,t:150}}
 const BUFF_NAMES={speed:"SPEED",spit:"FIRE RATE",bugs:"BUGS",spawn:"SPAWN RATE",armor:"ARMOR",tentacles:"TENTACLES",lasers:"LASERS",missiles:"MISSILES",plazma:"PLAZMA",shields:"SHIELDS",option:"DRONE OPTION"};
 const kick=n=>{if(opts.shake)shake=Math.max(shake,n)};
 const HAZARD_TEXT={acid:["ACID CLOUD","IT EATS ALIENS"],moon:["MOON RISING","IT SHIELDS THE SHIP"],storm:["NEPTUNE STORM","THE SWARM DRIFTS"]};
 function handleEvents(){for(const e of g.events){
  if(e.type==="sfx"){if(!demo)audio.sfx(e.name)}
  else if(e.type==="pop")pops.push({x:e.x,y:e.y,t:40,txt:e.txt,c:e.c});
  else if(e.type==="alienDie")burst(e.x,e.y,e.species===2?"#3fd4bd":"#5fd456",10);
  else if(e.type==="manDie"){burst(e.x,e.y,"#ffd93a",18,1.8);burst(e.x,e.y,"#ff5468",10);burst(e.x,e.y,"#b9c1cf",8,1.4,.06);kick(4);hitstop=3;rumble(180,.6)}
  else if(e.type==="shipDie"){burst(e.x,e.y,"#ffd93a",e.kind==="carrier"?34:16,1.8);burst(e.x,e.y,"#94a0b8",10,1.6,.06);kick(e.kind==="carrier"?8:4);hitstop=e.kind==="carrier"?5:2;rumble(200,.7)}
  else if(e.type==="spark")burst(e.x,e.y,"#ffe08a",3,.8);
  else if(e.type==="iceBreak"){burst(e.x,e.y,"#d2fbff",14,1.6,.05);burst(e.x,e.y,"#5ee7ff",6,1)}
  else if(e.type==="snatch")fx.push({k:"snatch",x0:e.x0,y0:e.y0,x1:e.x1,y1:e.y1,t:10});
  else if(e.type==="roll")fx.push({k:"roll",x:e.x,y:e.y,t:14});
  else if(e.type==="launch")burst(e.x,e.y-6,"#5ee7ff",6,.8);
  else if(e.type==="dome")burst(e.x,e.y,"#5ee7ff",2,.5);
  else if(e.type==="hazard"&&!demo&&HAZARD_TEXT[e.kind])banner(...HAZARD_TEXT[e.kind]);
  else if(e.type==="merged"){burst(e.x,e.y,"#ff6fb5",16,1.5);if(!demo){banner("MERGED!","A NEW SPECIES");rumble(120,.4)}}
  else if(e.type==="buff"&&!demo){if(e.side==="me")pops.push({x:CX,y:60,t:46,txt:`SWARM +${BUFF_NAMES[e.kind]}`,c:"#5fd456"});else pops.push({x:CX,y:120,t:46,txt:`SHIP +${BUFF_NAMES[e.kind]}`,c:"#ff9a2e"})}
  else if(e.type==="planetDestroyed"){flash=16;kick(10);hitstop=6;rumble(500,1);audio.hyper?.(true);if(!demo){banner(`${g.L.name} DESTROYED!`,g.L.final?"THE ADMIRAL ATTACKS":"HERE COMES THE BOSS");say(BOSS_LINES[g.L.id])}}
  else if(e.type==="boss"){burst(e.x,e.y,"#ffd93a",40,2.5);burst(e.x,e.y,"#ffffff",20,2);burst(e.x,e.y,"#94a0b8",14,2,.06);flash=22;kick(12);hitstop=8;rumble(600,1)}
  else if(e.type==="stage"&&!demo)banner(`STAGE ${e.stage}`,"THE SHIP GETS UPGRADES");
  else if(e.type==="descend"&&!demo&&g.aliens.some(a=>a.mey>bot(g)*.42))pops.push({x:CX,y:bot(g)*.55,t:30,txt:"THE SWARM DESCENDS",c:"#ff6fb5"});
  else if(e.type==="end")finish()}}
 // ---------- drawing (world units, scaled by S and the camera zoom) ----------
 function text(t,x,y,size,c,align="left",shadow="#05040a"){ctx.font=`900 ${size}px "Courier New",monospace`;ctx.textAlign=align;if(shadow){ctx.fillStyle=shadow;ctx.fillText(t,x*S+2,y*S+2)}ctx.fillStyle=c;ctx.fillText(t,x*S,y*S)}
 function spr(img,x,y,a=1,sc=1){if(!img)return;if(a<1)ctx.globalAlpha=a;const w=img.width*S*sc,h=img.height*S*sc;ctx.drawImage(img,Math.round(x*S-w/2),Math.round(y*S-h/2),w,h);if(a<1)ctx.globalAlpha=1}
 const ring=(x,y,r,c,lw=4)=>{ctx.strokeStyle=c;ctx.lineWidth=lw;ctx.beginPath();ctx.arc(x*S,y*S,r*S,0,6.283);ctx.stroke()};
 const SHIPSPR={fighter:"fighter",interceptor:"interceptor",carrier:"carrier",repair:"repair",frigate:"frigate"};
 function drawWorld(){const art=g.L.art,z=g.z,B=bot(g),x0=fx0(g),x1=fx1(g),fw=x1-x0;
  ctx.drawImage(skyOf(art),0,0,CW,CH);
  for(const[i,img]of stars.entries()){const off=((tick*(i?2:1)*(i?-1:1)/4)%H+H)%H;ctx.globalAlpha=i?.6:1;ctx.drawImage(img,0,off*S-CH,CW,CH);ctx.drawImage(img,0,off*S,CW,CH);ctx.globalAlpha=1}
  ctx.save();ctx.translate(CW/2,0);ctx.scale(z,z);ctx.translate(-CW/2,0);
  // planet / bonus bodies
  const pw=fw*S,ph=140*S*fw/196;
  if(g.phase==="planet")ctx.drawImage(planetOf(art),x0*S,Math.round(surfaceY(g))*S,pw,ph);
  else if(g.phase==="boss"){ctx.globalAlpha=.25;ctx.drawImage(planetOf(art),x0*S,(B-10)*S,pw,ph);ctx.globalAlpha=1}
  else if(g.L.bonus==="sun"){const gl=.5+.15*Math.sin(tick*.05);ctx.globalAlpha=gl;ctx.drawImage(planetOf(8),(x0-30)*S,(B-26)*S,(fw+60)*S,ph*1.3);ctx.globalAlpha=1}
  else if(g.L.bonus==="pluto")ctx.drawImage(planetOf(9),x0*S,(B-20)*S,pw,ph);
  else if(g.L.bonus==="nibiru"){ctx.globalAlpha=.85;ctx.drawImage(planetOf(10),(x0+fw*.15)*S,(B-14)*S,pw*.7,ph*.7);ctx.globalAlpha=1}
  for(const m of g.moons)spr(atlas.moon,m.x,m.y);
  for(const h of g.hazards){if(h.kind==="acid"){ctx.globalAlpha=.75;spr(atlas.cloud,h.x,h.y,1,h.r/16);ctx.globalAlpha=1}else if(h.kind==="rock")spr(atlas.rock[(h.x|0)&1],h.x,h.y)}
  if(g.hazards.some(h=>h.kind==="storm")){const h=g.hazards.find(h=>h.kind==="storm");ctx.fillStyle="rgba(190,230,255,.18)";for(let i=0;i<14;i++){const y=(i*37+tick*1.7)%B,x=((i*91+tick*6*h.dir)%fw+fw)%fw+x0;ctx.fillRect(x*S,y*S,26*S,S)}}
  for(const s of g.sats)spr(s.ufo?atlas.ufo:atlas.sat,s.x,s.y);
  for(const t of g.tentacles){const len=Math.min(52,(30-t.life)*6+8);ctx.fillStyle="#3fd4bd";ctx.fillRect((t.x-1.5)*S,t.y*S,3*S,len*S);ctx.fillStyle="#b8fff1";ctx.fillRect((t.x-.5)*S,t.y*S,1*S,len*S)}
  // bonus targets
  for(const i of g.ice)if(i.hp>0){const k=Math.min(3,Math.floor((1-i.hp/i.max)*4));ctx.drawImage(atlas.ice[k],(i.x-i.w/2)*S,(i.y-5)*S,i.w*S,10*S)}
  for(const u of g.ufos)if(!u.dead)spr(atlas.ufo,u.x,u.y);
  for(const f of g.flares){if(f.t<0){ctx.strokeStyle=`rgba(255,106,16,${.25+.2*Math.sin(tick*.6)})`;ctx.lineWidth=6;ctx.setLineDash([10,10]);ctx.beginPath();for(let k=0;k<=20;k++){const q=k/20,X=f.dir>0?x0-10+(fw+20)*q:x1+10-(fw+20)*q,Y=B+10-(B+10-f.peak)*Math.sin(q*Math.PI);k?ctx.lineTo(X*S,Y*S):ctx.moveTo(X*S,Y*S)}ctx.stroke();ctx.setLineDash([])}
   else{ctx.fillStyle="rgba(255,106,16,.35)";ctx.beginPath();ctx.arc(f.x*S,f.y*S,15*S,0,6.283);ctx.fill();ctx.fillStyle="#ffd24a";ctx.beginPath();ctx.arc(f.x*S,f.y*S,8*S,0,6.283);ctx.fill();ctx.fillStyle="#ffffff";ctx.beginPath();ctx.arc(f.x*S,f.y*S,4*S,0,6.283);ctx.fill()}}
  // the swarm
  const ids=sel.on&&live()?new Set(selIds()):null,anchor=sel.anchor;
  for(const a of g.aliens){const f=(tick>>3)&1;let img=a.species===2?atlas.big[f]:atlas.alien[a.variant][f];
   if(a.sparkle>0){ctx.globalAlpha=.5+.5*Math.sin(tick*.6);ctx.fillStyle=["#ff6fb5","#5ee7ff","#ffd93a"][(tick>>2)%3];ctx.beginPath();ctx.arc(a.x*S,a.y*S,11*S,0,6.283);ctx.fill();ctx.globalAlpha=1}
   if(g.merge?.id===a.id||sel.pick?.src===a.id)ring(a.x,a.y,12,"#ff6fb5");
   if(ids&&ids.has(a.id)){const main=a.id===anchor,r=(a.species===2?13:9.5)+Math.sin(tick*.3)*.6;ring(a.x,a.y,r,"#05040a",8);ring(a.x,a.y,r,main?"#ffd93a":"rgba(94,231,255,.85)",main?4:3)}
   spr(img,a.x,a.y,a.kami&&tick%6<3?.6:1);
   if(a.armor>0){ctx.fillStyle="#5ee7ff";ctx.fillRect((a.x-6)*S,(a.y+9)*S,Math.min(12,a.armor*3)*S,S)}
   if(!demo&&a.chosen&&a.cmd>=0&&a.okT<=0)spr(atlas.icon[a.cmd],a.x+7,a.y-8,.8,.55)}
  if(!demo)for(const a of g.aliens){if(a.okT>0)text("OK!",a.x,a.y-10,16,"#ffd93a","center");
   else if(!a.chosen&&!a.kami&&a.dive===0&&Math.abs(a.x-a.mex)<1.5&&Math.abs(a.y-a.mey)<1.5){if(sel.on){ctx.fillStyle="#05040a";ctx.fillRect((a.x-1.5)*S,(a.y-11.5)*S,3*S,3*S);ctx.fillStyle="#ffffff";ctx.fillRect((a.x-1)*S,(a.y-11)*S,2*S,2*S)}else text("GO",a.x,a.y-10,13,"#ffffff","center")}}
  for(const s of g.shots){ctx.fillStyle="#5fd456";ctx.fillRect((s.x-.5)*S,(s.y-3)*S,1*S,6*S)}
  for(const s of g.globs)spr(atlas.glob[(tick>>2)&1],s.x,s.y);
  for(const b of g.bugs)spr(atlas.bug[(tick>>1)&1],b.x,b.y);
  for(const l of g.loots){if(l.done){text(l.done===1?(l.orb?"":l.good?"POWER UP!":"BLOCKED!"):"",l.x,l.y-l.t*.2,13,"#ffffff","center");continue}
   if(l.orb)spr(atlas.orb[(tick>>3)&1],l.x,l.y,1,1+.12*Math.sin(tick*.4));else spr((l.good?atlas.good:atlas.bad)[(tick>>3)&1],l.x,l.y,1,1+.1*Math.sin(tick*.4))}
  // the humans
  for(const s of g.ships){const img=atlas[SHIPSPR[s.kind]];spr(img,s.x,s.y,s.inv>0&&tick%4<2?.5:1);
   if(s.kind==="frigate"&&s.dome>0){ctx.globalAlpha=.15+.08*s.dome;ring(s.x,s.y,24,"#5ee7ff",3);ctx.fillStyle="rgba(94,231,255,.08)";ctx.beginPath();ctx.arc(s.x*S,s.y*S,24*S,0,6.283);ctx.fill();ctx.globalAlpha=1}
   if(s.max>1){ctx.fillStyle="#05040a";ctx.fillRect((s.x-8)*S,(s.y+(s.kind==="carrier"?12:9))*S,16*S,S*1.2);ctx.fillStyle="#ff5468";ctx.fillRect((s.x-8)*S,(s.y+(s.kind==="carrier"?12:9))*S,16*S*s.hp/s.max,S*1.2)}}
  for(const b of g.beams){const t=b.hero?g.man:g.ships.find(s=>s.id===b.id);if(!t)continue;ctx.strokeStyle=`rgba(95,212,86,${b.t/20})`;ctx.lineWidth=5;ctx.beginPath();ctx.moveTo(b.x0*S,b.y0*S);ctx.lineTo(t.x*S,t.y*S);ctx.stroke()}
  const m=g.man;if(m){spr(m.shields>0?atlas.shipShield:atlas.ship,m.x,m.y,m.inv>0&&tick%4<2?.4:1);if(m.shields>0)ring(m.x,m.y,10,`rgba(94,231,255,${.5+.3*Math.sin(tick*.4)})`)}
  for(const o of g.options)spr(atlas.ship,o.x,o.y,.8,.5);
  if(g.boss){const Bs=g.boss;spr(atlas.boss[Bs.type-1],Bs.x,Bs.y);if(Bs.dome>0){ctx.globalAlpha=.4;ring(Bs.x,Bs.y,30,"#ffd93a",5);ctx.globalAlpha=1}}
  for(const s of g.manshots){ctx.fillStyle="#ff9a2e";ctx.fillRect((s.x-.5)*S,(s.y-3)*S,1*S,6*S)}
  for(const s of g.bossShots){ctx.fillStyle=s.kind==="jup"?"#ffd93a":"#a39aac";ctx.fillRect((s.x-2)*S,(s.y-2)*S,4*S,4*S)}
  for(const s of g.missiles)spr(atlas.missile,s.x,s.y);
  for(const s of g.plazmas){ctx.fillStyle=tick%4<2?"#ff6fb5":"#ffffff";ctx.fillRect((s.x-2)*S,(s.y-2)*S,4*S,4*S)}
  for(const e of fx){if(e.k==="snatch"){ctx.strokeStyle="#3fd4bd";ctx.lineWidth=8;ctx.beginPath();ctx.moveTo(e.x0*S,e.y0*S);ctx.lineTo(e.x1*S,e.y1*S);ctx.stroke();ctx.strokeStyle="#b8fff1";ctx.lineWidth=3;ctx.stroke()}
   else if(e.k==="roll"){ctx.globalAlpha=e.t/14;spr(atlas.shipShield,e.x-(14-e.t),e.y,.5);spr(atlas.shipShield,e.x+(14-e.t),e.y,.5);ctx.globalAlpha=1}}
  // mouse command menu (radial)
  if(g.menu){const a=g.aliens.find(a=>a.id===g.menu.id);if(a){ctx.fillStyle="rgba(5,4,10,.55)";ctx.beginPath();ctx.arc(a.x*S,a.y*S,26*S,0,6.283);ctx.fill();
   for(let i=0;i<5;i++){const[ox,oy]=CMD_POS[i];spr((lockedCmd(g,i)?atlas.iconLocked:atlas.icon)[i],a.x+ox,a.y+oy)}spr(atlas.alien[a.variant][0],a.x,a.y)}}
  for(const p of particles){ctx.globalAlpha=Math.min(1,p.life/10);ctx.fillStyle=p.c;ctx.fillRect((p.x-.5)*S,(p.y-.5)*S,S*p.sz,S*p.sz)}ctx.globalAlpha=1;
  for(const p of pops)text(p.txt,p.x,p.y-(46-p.t)*.25,15,p.c,"center");
  ctx.restore()}
 // where a world point lands on screen (for overlays drawn without the camera transform)
 const scr=(x,y)=>({x:CW/2+(x-CX)*S*g.z,y:y*S*g.z});
 function win(x,y,w,h){ctx.fillStyle="rgba(0,0,0,.45)";ctx.fillRect(x+6,y+6,w,h);const gr=ctx.createLinearGradient(0,y,0,y+h);gr.addColorStop(0,"#3a5cd8");gr.addColorStop(1,"#0c1660");ctx.fillStyle=gr;ctx.fillRect(x,y,w,h);ctx.strokeStyle="#ffffff";ctx.lineWidth=4;ctx.strokeRect(x+2,y+2,w-4,h-4);ctx.strokeStyle="#9aa6c8";ctx.lineWidth=2;ctx.strokeRect(x+7,y+7,w-14,h-14)}
 function drawOrderMenu(){const m=sel.menu,a=anchorAlien();if(!m||!a)return;const p=scr(a.x,a.y),w=330,rowH=34,h=26+m.items.length*rowH+40;
  let x=p.x+46,y=p.y-20;if(x+w>FX1px())x=p.x-46-w;x=Math.max(FX0px()+6,x);y=Math.max(8,Math.min(CH-h-8,y));win(x,y,w,h);
  ctx.font='800 15px "Courier New",monospace';ctx.textAlign="left";ctx.fillStyle="#ffd93a";ctx.fillText(`ORDER: ${SCOPES[sel.scope]}${sel.scope?` (${selIds().length})`:""}`,x+18,y+26);
  m.items.forEach((it,i)=>{const yy=y+30+i*rowH+22,on=i===m.idx;if(on){ctx.fillStyle="rgba(255,255,255,.12)";ctx.fillRect(x+10,yy-20,w-20,rowH-4);ctx.fillStyle="#ffffff";ctx.beginPath();const bx=x+20+(Math.floor(tick/8)%2)*3;ctx.moveTo(bx,yy-14);ctx.lineTo(bx+14,yy-7);ctx.lineTo(bx,yy);ctx.fill()}
   ctx.font='900 22px "Courier New",monospace';ctx.fillStyle=it.off?"#6a74a0":on?"#ffffff":"#c8d0f0";ctx.fillText(it.label,x+44,yy)});
  const it=m.items[m.idx];ctx.font='700 13px "Courier New",monospace';ctx.fillStyle="#9fe9ff";ctx.fillText(it.hint||"",x+18,y+h-16)}
 const FX0px=()=>57*S,FX1px=()=>253*S;
 function drawHud(){const s=snapshot(g);
  ctx.fillStyle="rgba(5,4,14,.78)";ctx.fillRect(0,0,57*S,CH);ctx.fillRect(253*S,0,CW-253*S,CH);
  const best=store.get(`best.${g.difficulty}`,0);
  const Lh=[["HI-SCORE",fmt(Math.max(best,demo?0:g.score))],["SCORE",fmt(g.score)],["COMBO",`x ${g.combo}`],["SWARM",`${s.aliens} (${s.idle} idle)`]];
  Lh.forEach(([k,v],i)=>{text(k,4,12+i*24,14,"#9488ad");text(v,4,22+i*24,18,i===2&&g.combo>1?"#ffd93a":"#ffffff")});
  text(g.L.name,4,112,18,"#5ee7ff");text(`LEVEL ${g.level+1}/${LEVELS.length}`,4,121,12,"#9488ad");text(DIFF[g.difficulty].name,4,129,13,"#ff6fb5");
  if(g.phase!=="bonus"){for(let i=0;i<Math.min(6,g.manlives);i++)spr(atlas.ship,8+i*12,141,1,.6);text("SHIP LIVES",4,151,11,"#9488ad")}
  if(g.phase==="bonus"){text("TIME",306,14,14,"#9488ad","right");text(String(s.timer),306,30,26,s.timer<=10?"#ff5468":"#ffffff","right");text("BONUS",306,48,14,"#9488ad","right");text(fmt(g.bonusPts),306,60,18,"#ffd93a","right")}
  else{const R=[["STAGE",g.stage],["KILLS",g.kills],["LOST",g.deaths],["TIME",s.time]];R.forEach(([k,v],i)=>{text(k,306,12+i*22,14,"#9488ad","right");text(String(v),306,22+i*22,18,"#ffffff","right")})}
  if(!demo){text(s.rush?"RUSH READY":"RUSH...",306,96,12,s.rush?"#5fd456":"#6a74a0","right");if(!s.rush){ctx.fillStyle="#252536";ctx.fillRect(262*S,98*S,44*S,2*S);ctx.fillStyle="#5fd456";ctx.fillRect(262*S,98*S,44*S*(1-g.rushCD/300),2*S)}
   if(sel.on){const y0=108;[[`${L("order")} ORDER`,`${L("repeat")} AGAIN`],[`${L("snatch")} SNATCH`,`${L("rush")} RUSH`],[`${L("scope")} ${SCOPES[sel.scope]}`,`${L("idle")} IDLE`],[`${L("shift")}+◀▶ SHIFT`,""]].forEach(([a,b],i)=>text(`${a}  ${b}`.trim(),306,y0+i*8,10,i===2?"#ffd93a":"#5ee7ff","right"));text(`${padLabel("KeyP")} = MENU`,306,y0+34,10,"#9488ad","right")}
   else{text("CLICK GO ALIENS",306,110,10,"#5ee7ff","right");text("1-5 ORDER  X SNATCH",306,118,10,"#5ee7ff","right");text("SPACE RUSH  ← → SHIFT",306,126,10,"#5ee7ff","right");text("ARROWS SELECT  Z MENU",306,134,10,"#5ee7ff","right");text("P / ESC = MENU",306,142,10,"#9488ad","right")}}
  // objective bar
  if(g.phase!=="bonus"){const hp=Math.max(0,g.health)/100;ctx.fillStyle="#05040a";ctx.fillRect(59*S,170*S,192*S,4*S);ctx.fillStyle=g.phase==="boss"?"#ff5468":"#5fd456";ctx.fillRect(59*S+2,170*S+2,(192*S-4)*hp,4*S-4);
   text(g.phase==="boss"?(g.L.final?"THE ADMIRAL":"BOSS"):`${g.L.name}${g.ships.length?`  +${g.ships.length} SHIPS`:""}`,155,168,12,"#d6cbe8","center")}
  if(!demo&&!sel.on)for(const a of ARROWS)spr(a.dir<0?atlas.arrowL:atlas.arrowR,a.x,a.y,arrowPtr?.dir===a.dir||(a.dir<0?shiftHold.l:shiftHold.r)?1:.55);
  if(sel.pick&&!demo)text("PICK A PARTNER",155,16,16,"#ff6fb5","center");
  if(dial){const l=dial.lines[dial.i],c=l.startsWith("YOU")?"#5fd456":/^(BOSS|ADMIRAL)/.test(l)?"#ff5468":"#ffffff",i=l.indexOf(": "),who=l.slice(0,i),say=l.slice(i+2);
   ctx.fillStyle="rgba(5,4,10,.92)";ctx.fillRect(2*S,156*S,53*S,22*S);ctx.strokeStyle=c;ctx.lineWidth=3;ctx.strokeRect(2*S+1,156*S+1,53*S-2,22*S-2);
   text(who,4,162,11,c,"left",null);const words=say.split(" ");let line="",y=169;ctx.font='900 11px "Courier New",monospace';
   for(const w of words){const t=line?line+" "+w:w;if(ctx.measureText(t).width>49*S&&line){text(line,4,y,11,"#ffffff","left",null);line=w;y+=7}else line=t}if(line&&y<180)text(line,4,y,11,"#ffffff","left",null)}}
 function draw(){tick++;const sx=shake>0&&!reduceMotion?(Math.random()-.5)*shake*S:0,sy=shake>0&&!reduceMotion?(Math.random()-.5)*shake*S:0;shake=Math.max(0,shake-.5);
  ctx.save();ctx.translate(sx,sy);drawWorld();ctx.restore();drawHud();if(!demo)drawOrderMenu();
  if(flash>0){ctx.fillStyle=`rgba(255,246,184,${flash/30})`;ctx.fillRect(0,0,CW,CH);flash--}
  if(paused){ctx.fillStyle="rgba(4,2,9,.6)";ctx.fillRect(0,0,CW,CH)}}
 function simFx(){for(const p of particles){p.x+=p.vx;p.y+=p.vy;p.vy+=p.grav;p.vx*=.93;if(!p.grav)p.vy*=.93;p.life--}particles=particles.filter(p=>p.life>0);if(particles.length>360)particles.splice(0,particles.length-360);for(const p of pops)p.t--;pops=pops.filter(p=>p.t>0);for(const e of fx)e.t--;fx=fx.filter(e=>e.t>0);
  if(dial&&--dial.t<=0){dial.i++;dial.t=150;if(dial.i>=dial.lines.length)dial=null}}
 // ---------- menus (SNES-style, shared potato kit) ----------
 const onoff=v=>v?"ON":"OFF";
 const savedRun=()=>{const r=store.get(RUN_KEY,null);return r&&LEVELS[r.level]?r:null};
 const LOGO=`<div class="oa-logo"><small>ARCADEGRIDZ // ORIGINAL 2014 SIGNAL, REBUILT</small><b>OFFENSE</b><span>of the aliens</span></div>`;
 function titleScreen(){return{hero:LOGO,clear:true,sub:()=>{const b=store.get(`best.${opts.difficulty}`,null);return b!=null?`BEST ${fmt(b)} ON ${DIFF[opts.difficulty].name}`:"YOU ARE THE SWARM"},start:()=>{savedRun()?continueRun():menu.push(difficultyScreen())},
  items:[{label:"NEW GAME",hint:"START AT EARTH",act:()=>{if(savedRun())menu.push({title:"START OVER?",sub:"YOUR SAVED INVASION WILL BE LOST",items:[{label:"NO",act:()=>menu.pop()},{label:"YES, NEW GAME",act:()=>{menu.pop();menu.push(difficultyScreen())}}]});else menu.push(difficultyScreen())}},
   {label:"CONTINUE",hint:()=>{const r=savedRun();return r?`LEVEL ${r.level+1}: ${LEVELS[r.level].name} // ${DIFF[r.difficulty].name} // ${fmt(r.score)}`:"NO INVASION IN PROGRESS"},off:()=>!savedRun(),act:continueRun},
   {label:"OPTIONS",act:()=>menu.push(optionsScreen())},
   {label:"CONTROLS",act:()=>menu.push(controls())},
   {label:"HOW TO PLAY",act:()=>menu.push(howTo())},
   {label:"EXIT GAME",hint:"BACK TO ARCADEGRIDZ",act:exitGame}]}}
 function difficultyScreen(){return{title:"DIFFICULTY",sub:"YOU CAN'T CHANGE IT MID-INVASION",items:[1,2,3].map(d=>({label:DIFF[d].name,hint:["","A GENTLE START. ROOKIE PILOTS.","THE INTENDED INVASION.","SHARP PILOTS. UPGRADES STICK."][d],act:()=>newRun(d)})).concat([{label:"BACK",act:()=>menu.pop()}]),
  idx:opts.difficulty-1}}
 function optionsScreen(){const tog=(k,v)=>{opts[k]=v;store.set(k,v)};return{title:"OPTIONS",items:[
  {label:"DIALOGUE",value:()=>onoff(opts.dial),left:()=>tog("dial",!opts.dial),right:()=>tog("dial",!opts.dial)},
  {label:"ASSIST",value:()=>onoff(opts.assist),hint:"IDLE ALIENS SOMETIMES ATTACK ON THEIR OWN",left:()=>tog("assist",!opts.assist),right:()=>tog("assist",!opts.assist)},
  {label:"ORDER MENU SLOW-MO",value:()=>onoff(opts.slowmo),hint:"TIME SLOWS WHILE YOU PICK AN ORDER",left:()=>tog("slowmo",!opts.slowmo),right:()=>tog("slowmo",!opts.slowmo)},
  {label:"SOUND",value:()=>onoff(!audio.muted),left:toggleAudio,right:toggleAudio},
  {label:"SCREEN SHAKE",value:()=>onoff(opts.shake),left:()=>tog("shake",!opts.shake),right:()=>tog("shake",!opts.shake)},
  {label:"VIBRATION",value:()=>onoff(window.ArcadeGridzPad?.settings?.().vibration!==false),off:()=>!window.ArcadeGridzPad,left:vib,right:vib},
  {label:"BACK",act:()=>menu.pop()}]}}
 function vib(){const P=window.ArcadeGridzPad;if(P){P.update({vibration:!P.settings().vibration});P.rumble?.(120,.5)}}
 function controls(){return controlsScreen(binds,{menu,extra:[{label:"KEYBOARD + MOUSE",act:()=>menu.push(kbScreen())}]})}
 function kbScreen(){return{title:"KEYBOARD + MOUSE",body:`<b>MOUSE</b> Click an alien that says GO, then a command icon. Click loot, missiles, satellites, or the ship (rush).<br><b>ARROWS</b> select an alien // <b>Z / ENTER</b> order menu // <b>C</b> repeat order // <b>1-5</b> attack, dive, bugs, tentacles, random<br><b>X</b> snatch loot // <b>SPACE</b> rush // <b>Q</b> one / row / all // <b>TAB</b> next idle alien // <b>SHIFT + ← →</b> shift the swarm<br><b>P / ESC</b> pause menu // <b>M</b> sound`,items:[{label:"BACK",act:()=>menu.pop()}]}}
 function howTo(){return{title:"HOW TO PLAY",body:()=>`You are the <b>swarm</b>. Wreck each planet, beat its defender, and don't let the humans wipe you out. Ten levels from Earth to Planet X, with bonus stages between.<br>
  <b>SELECT</b> The d-pad hops between your aliens. ${L("scope")} switches between ONE alien, its ROW, or ALL of them. ${L("idle")} jumps to an alien waiting for orders.<br>
  <b>ORDER</b> ${L("order")} opens the order menu: ATTACK shoots, DIVE divebombs, BUGS and TENTACLES unlock with power-ups. Aliens keep following their order. ${L("repeat")} gives the same order again.<br>
  <b>LOOT</b> ${L("snatch")} snatches the nearest power-up: green upgrades the swarm, red ones are the ship's (block them). <b>RUSH</b> ${L("rush")} sends everyone at the ship (it needs to recharge).<br>
  <b>FLEETS</b> Later planets bring wingmen, carriers, repair ships and shield frigates. Shields stop shots, not dives.<br>Hold <b>${padLabel("KeyM")} + ${padLabel("KeyP")}</b> to leave for the ArcadeGridz launcher.`,items:[{label:"BACK",act:()=>menu.pop()}]}}
 function confirm(title,yes,sub="PROGRESS SINCE THE LEVEL STARTED WILL BE LOST"){menu.push({title,sub,items:[{label:"NO, KEEP PLAYING",act:()=>menu.pop()},{label:"YES",act:yes}]})}
 function pauseScreen(){return{title:"PAUSED",sub:()=>`LEVEL ${g.level+1}: ${g.L.name} // ${DIFF[g.difficulty].name} // SCORE ${fmt(g.score)}`,back:resume,start:resume,items:[
  {label:"RESUME",act:resume},
  {label:"RESTART LEVEL",act:()=>confirm("RESTART LEVEL?",()=>startLevel(run.level))},
  {label:"OPTIONS",act:()=>menu.push(optionsScreen())},
  {label:"CONTROLS",act:()=>menu.push(controls())},
  {label:"HOW TO PLAY",act:()=>menu.push(howTo())},
  {label:"QUIT TO TITLE",hint:"YOUR INVASION IS SAVED AT THE START OF THIS LEVEL",act:()=>confirm("QUIT TO TITLE?",showMenu)},
  {label:"EXIT GAME",hint:"BACK TO ARCADEGRIDZ",act:()=>confirm("EXIT GAME?",exitGame)}]}}
 function clearScreen(){const t=g.tally,next=g.level+1;return{title:g.phase==="bonus"?"BONUS CLEAR":"LEVEL CLEAR",sub:`${g.L.name} // ${DIFF[g.difficulty].name}`,start:nextLevel,
  body:`<div class="oa-result"><b>SCORE ${fmt(g.score)}</b>${t?`<span>${t.perfect?"PERFECT! +"+fmt(t.perfectPts)+" // ":""}${t.survivors} SURVIVORS +${fmt(t.survivorPts)} // BONUS ${fmt(t.bonus)}</span>`:`<span>${g.kills} SHIPS DOWNED // ${g.deaths} ALIENS LOST // ${snapshot(g).time}</span>`}<small>NEXT: LEVEL ${next+1} // ${LEVELS[next].name}${LEVELS[next].kind==="bonus"?" (BONUS STAGE)":""}. Your swarm upgrades carry over.</small></div>`,
  items:[{label:"NEXT LEVEL",act:nextLevel},{label:"SAVE + QUIT TO TITLE",act:()=>{saveRun(next);showMenu()}},{label:"EXIT GAME",hint:"PROGRESS IS SAVED",act:()=>{saveRun(next);exitGame()}}]}}
 function overScreen(){return{title:"GAME OVER",sub:`${g.L.name} // ${DIFF[g.difficulty].name}`,start:()=>startLevel(run.level),
  body:`<div class="oa-result"><b>SCORE ${fmt(g.score)}</b>${result?.isBest?`<em>NEW LOCAL BEST</em>`:""}<span>${g.kills} SHIPS DOWNED // ${g.deaths} ALIENS LOST</span><small>Signed-in scores post to ArcadeGridz high scores as UNVERIFIED. CONTINUE retries this level with the score you started it with.</small></div>`,
  items:[{label:"CONTINUE",hint:`RETRY ${g.L.name}`,act:()=>startLevel(run.level)},{label:"TITLE",act:showMenu},{label:"EXIT GAME",hint:"BACK TO ARCADEGRIDZ",act:exitGame}]}}
 function endingScreen(){return{title:"THE SWARM WINS",sub:`FINAL SCORE ${fmt(g.score)} // ${DIFF[g.difficulty].name}`,
  body:`Planet X was the humans' last hideout, and the Admiral's flagship was the last thing standing between the swarm and the stars. Now the whole solar system hums with the sound of very happy aliens. <b>*SLUUURP*</b>${result?.isBest?"<br><b>NEW LOCAL BEST!</b>":""}`,
  items:[{label:"LOOP THE INVASION",hint:"START AGAIN AT EARTH, KEEP YOUR UPGRADES AND SCORE",act:()=>{run={difficulty:g.difficulty,level:0,score:g.score,me:{...g.me},seed:Date.now()>>>0};startLevel(0)}},{label:"TITLE",act:showMenu},{label:"EXIT GAME",act:exitGame}]}}
 // ---------- campaign flow ----------
 function saveRun(level){if(!g)return;run={difficulty:g.difficulty,level,score:g.score,me:{...g.me}};store.set(RUN_KEY,run)}
 function newRun(d){opts.difficulty=d;store.set("difficulty",d);run={difficulty:d,level:0,score:0,me:null};store.set(RUN_KEY,run);startLevel(0)}
 function continueRun(){const r=savedRun();if(!r)return;run={...r};startLevel(r.level)}
 function nextLevel(){const next=g.level+1;saveRun(next);startLevel(next)}
 function attract(){g=createGame({difficulty:1,level:[0,1,4,6][Math.floor(Math.random()*4)],seed:(Math.random()*2**32)>>>0,assist:true});demo=true}
 function demoInput(){const inp={};if(g.frame%20===0&&idleAliens(g).length)inp.command={cmd:[CMD.ATTACK,CMD.ATTACK,CMD.DIVE][g.frame/20%3]};if(g.frame%12===0)inp.snatch=true;return inp}
 function showMenu(){clearTimeout(endT);if(!demo){attract();audio.song("menu");audio.hyper?.(false);publish(true,true)}paused=false;focusPaused=false;dial=null;sel.menu=null;sel.pick=null;resetHolds();menu.open(titleScreen())}
 function startLevel(level){clearTimeout(endT);audio.unlock();const L0=LEVELS[level];
  g=createGame({difficulty:run.difficulty,level,seed:(Math.random()*2**32)>>>0,assist:opts.assist,carry:{me:run.me,score:run.score}});
  demo=false;paused=false;focusPaused=false;submitted=false;result=null;dial=null;particles=[];pops=[];fx=[];taps=[];inpQ={};resetHolds();menu.close();
  sel.anchor=0;sel.scope=0;sel.menu=null;sel.pick=null;sel.ax=CX;sel.ay=20;
  audio.song(SONG[level]||"earth");audio.hyper?.(false);audio.music(true);
  banner(`LEVEL ${level+1}: ${L0.name}`,L0.kind==="bonus"?`BONUS STAGE // ${L0.brief}`:L0.brief);say(DIALOGUE[L0.id]);publish(true);canvas.focus?.({preventScroll:true})}
 function postScore(){window.dispatchEvent(new CustomEvent("arcadegridz:score",{detail:{gameId:GAME_ID,score:g.score}}));let isBest=false;if(g.score>store.get(`best.${g.difficulty}`,-1)){store.set(`best.${g.difficulty}`,g.score);isBest=true}return isBest}
 function finish(){if(submitted||demo)return;submitted=true;audio.music(false);audio.hyper?.(false);resetHolds();sel.menu=null;sel.pick=null;
  if(g.state==="over"){result={isBest:postScore()};banner("GAME OVER","THE SWARM IS GONE");endT=setTimeout(()=>{if(!destroyed)menu.open(overScreen())},2200)}
  else if(g.state==="won"){result={isBest:postScore()};store.del(RUN_KEY);banner("INVASION COMPLETE","THE SOLAR SYSTEM IS YOURS");endT=setTimeout(()=>{if(!destroyed)menu.open(endingScreen())},2600)}
  else{saveRun(g.level+1);banner(g.phase==="bonus"?"BONUS CLEAR":"LEVEL CLEAR",`${g.L.name} IS YOURS`);endT=setTimeout(()=>{if(!destroyed)menu.open(clearScreen())},2200)}
  publish(true)}
 function openPause(){if(demo||!g||g.state!=="play"||menu.isOpen())return;paused=true;resetHolds();sel.menu=null;if(g.menu)g.menu=null;audio.music(false);menu.open(pauseScreen());publish(true)}
 function resume(){if(!paused)return;menu.close();paused=false;focusPaused=false;resetHolds();last=performance.now();acc=0;audio.resume();if(!demo&&g.state==="play")audio.music(true);publish(true)}
 function exitGame(){shutdown();onExit?.()}
 // ---------- lifecycle ----------
 function publish(force,inactive){const now=performance.now();if(!force&&now-publishT<200)return;publishT=now;if(demo&&!inactive)return;window.dispatchEvent(new CustomEvent("oota:state",{detail:demo?{active:true,menu:true,paused:false,muted:audio.muted}:{...snapshot(g),paused,muted:audio.muted}}))}
 function frame(now){if(destroyed)return;raf=requestAnimationFrame(frame);const dt=Math.min(.1,(now-last)/1000);last=now;
  if(!paused&&!focusPaused){if(hitstop>0){hitstop--}else{acc+=dt*(sel.menu&&opts.slowmo&&!demo?.35:1);let n=0;while(acc>=DT&&n<4){step(g,demo?demoInput():readInput());handleEvents();simFx();acc-=DT;n++;if(demo&&g.state!=="play")attract()}if(n===4)acc=0}}
  draw();publish()}
 function togglePause(){if(paused)resume();else openPause()}
 function toggleAudio(){audio.setMuted(!audio.muted);if(!audio.muted&&!demo&&!paused)audio.music(true);root.querySelector('[data-oa="audio"]')?.classList.toggle("active",audio.muted);publish(true)}
 function shutdown(){if(destroyed)return;destroyed=true;cancelAnimationFrame(raf);menu.close();removeEventListener("keydown",onKey);removeEventListener("keyup",onKey);removeEventListener("resize",onResize);ro.disconnect();clearTimeout(bannerTimer);clearTimeout(endT);audio.close();document.body.classList.remove("oa-playing");window.dispatchEvent(new CustomEvent("oota:state",{detail:{active:false}}))}
 root.querySelector(".oa-controls").addEventListener("click",e=>{const a=e.target.closest("button")?.dataset.oa;if(a==="pause")togglePause();else if(a==="audio")toggleAudio();else if(a==="menu"){if(!demo&&g.state==="play")openPause();else showMenu()}else if(a==="exit")exitGame()});
 const onResize=()=>fit(stage);addEventListener("resize",onResize);const ro=new ResizeObserver(onResize);ro.observe(root.querySelector(".oa-shell"));fit(stage);setTimeout(onResize,500);
 attract();menu.open(titleScreen());raf=requestAnimationFrame(frame);
 return{destroy:shutdown,
  // the arcade shell shows no overlay of its own: losing focus opens this game's pause menu (like 2DISO)
  ownsFocusPause:true,
  pauseForFocus(){if(destroyed)return;resetHolds();if(!demo&&g.state==="play")openPause();audio.music(false);audio.suspend();publish(true)},
  resumeFromFocus(){if(destroyed)return;last=performance.now();acc=0;audio.resume();if(!demo&&!paused&&g.state==="play")audio.music(true);publish(true)},
  control(action){if(action==="pause")togglePause();else if(action==="audio")toggleAudio();else if(action==="menu"){if(!demo&&g.state==="play")openPause();else showMenu()}else if(action==="restart"||action==="start"){if(run)startLevel(run.level);else newRun(opts.difficulty)}else if(action==="exit")exitGame()},
  getResult:()=>({score:g.score,state:g.state,result:g.result}),
  // test hook: the live game object (scores from this game are always UNVERIFIED)
  _debug:{game:()=>g,startLevel,newRun,opts:()=>opts,sel:()=>sel,selIds,menu:()=>menu,binds,run:()=>run}};
}
