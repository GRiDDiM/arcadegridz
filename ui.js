// OFFENSE OF THE ALIENS v2 browser layer: renderer (original 310x180 screen drawn 4x, zooming out as the swarm grows),
// SNES-style menus (shared potato kit), campaign flow (NEW GAME / CONTINUE, levels, bonus stages, ending) and input.
// Controller: the d-pad hops the selection between aliens (no fake cursor), A opens the order menu, Y repeats the last
// order, X snatches loot, B sends the swarm rushing, L picks ONE / ROW / ALL, hold R + d-pad shifts the formation.
import{autoInput,W,H,DT,CX,CMD,CMD_NAMES,CMD_POS,DIFF,LEVELS,CLASSES,SUPER_NAMES,UNIQUE,KIND_NAMES,createGame,step,snapshot,idleAliens,commandable,lockedCmd,fx0,fx1,bot,surfaceY}from"./engine.js?v=1102-v8";
import{buildAtlas,buildPlanet,buildSky,buildStars}from"./sprites.js?v=1102-v8";
import{createAudio}from"./audio.js?v=1013-oota1";
import{createTitle}from"./title.js?v=1102-v7";
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
 planetx:["HUMAN: Outpost X, we have contact!","YOU: *bloop*","HUMAN: Call the fleet!"],
 earth:["ADMIRAL: So you came all this way.","ADMIRAL: Every ship. Every pilot. NOW!","YOU: *THE SWARM SINGS*","ADMIRAL: Earth will not fall!"]};
const BOSS_LINES={planetx:["HUMAN: Boss! Help me!","BOSS: WHAT THE...","BOSS: Why are you trying...","BOSS: to take on a...","BOSS: SWARM of aliens!","BOSS: ALL ALONE?!!!","HUMAN: It worked in the...","HUMAN: Video Games!!!"],
 mars:["BOSS: I will decimate you!"],jupiter:["BOSS: This is not over!!"],venus:["BOSS: You'll melt like the rest!"],saturn:["BOSS: Ring guard, engage!"],neptune:["BOSS: Down into the deep with you!"],earth:["ADMIRAL: I'll finish this myself!"]};
const SONG=["mars","earth","menu","jupiter","jupiter","menu","mars","mars","menu","earth"];
const KEYCMD={Digit1:CMD.ATTACK,Digit2:CMD.DIVE,Digit3:CMD.BUGS,Digit4:CMD.TENTACLES,Digit5:CMD.RANDOM};
const ARROWS=[{x:77,y:156,dir:-1},{x:233,y:156,dir:1}];
const ORDER=[CMD.ATTACK,CMD.DIVE,CMD.BUGS,CMD.TENTACLES,CMD.RANDOM];
const ORDER_HINT={[CMD.ZAP]:"CHAIN LIGHTNING THROUGH SHIPS AND MISSILES",[CMD.EMP]:"STUN THE NEAREST SHIP",[CMD.SHIELD]:"BUBBLE THAT BLOCKS SHOTS FOR NEARBY ALIENS",[CMD.RAM]:"HEAVY DIVE: BIG DAMAGE, SURVIVES",[CMD.TAUNT]:"THE HUMANS AIM AT ME. I GET TOUGHER",[CMD.MEND]:"REPAIR ARMOUR OF ALIENS NEARBY",[CMD.HATCH]:"LAY EGGS THAT HATCH NEW GRUNTS",[CMD.SANCT]:"SAFE ZONE: NO MISSILES, SURVIVES NUKES",[CMD.ATTACK]:"SHOOT STRAIGHT DOWN",[CMD.DIVE]:"DIVEBOMB THE NEAREST SHIP",[CMD.BUGS]:"SPIT HOMING BUGS",[CMD.TENTACLES]:"LASH WHATEVER IS BELOW",[CMD.RANDOM]:"SURPRISE ME",[CMD.PHASE]:"BLINK SIDEWAYS, GHOSTED: SHOTS PASS THROUGH",[CMD.FREEZE]:"ICE SHOTS THAT SLOW THE SHIP DOWN",[CMD.HURL]:"HURL A RING ROCK AT THE NEAREST SHIP",[CMD.NAPALM]:"FIRE BLOBS THAT BURN ON THE SHIP'S LANE",[CMD.SPORES]:"SPORE PODS THAT BURST INTO BUGS",[CMD.BROOD]:"BIRTH TWO DRONES AT A TIME",[CMD.RAGE]:"NEARBY ALIENS ATTACK TWICE AS FAST"};
const KCOL={zapper:"#5ee7ff",tank:"#ff9a2e",healer:"#ff6fb5",phantom:"#b25bd2",froster:"#d2fbff",rocker:"#ffd93a",firebug:"#ff6a10",spore:"#c8ff9a",brood:"#ff6fb5"};
// controller actions (pad buttons arrive from gamepad.js as synthetic key codes; SNES names shown by default)
const ACTIONS=[
 {id:"cursor",label:"SELECT ALIEN",def:"D-PAD",fixed:true,hint:"HOPS BETWEEN YOUR ALIENS"},
 {id:"order",label:"ORDER MENU",def:"Escape",hint:"PICK WHAT THE SELECTION DOES"},
 {id:"repeat",label:"REPEAT LAST ORDER",def:"KeyZ",hint:"GIVE THE SAME ORDER AGAIN"},
 {id:"snatch",label:"SNATCH LOOT",def:"KeyX",hint:"GRAB GREEN, BLOCK RED"},
 {id:"rush",label:"SWARM RUSH",def:"Space",hint:"EVERYONE DIVES AT THE SHIP"},
 {id:"left",label:"MOVE SWARM LEFT",def:"ShiftLeft",hint:"HOLD"},
 {id:"right",label:"MOVE SWARM RIGHT",def:"KeyE",hint:"HOLD"},
 {id:"scope",label:"GROUP: ONE / ROW / ALL",def:"KeyM",hint:"ORDER A WHOLE ROW OR EVERYONE"},
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
 try{document.fonts?.load('16px "Press Start 2P"');document.fonts?.load("16px Bungee")}catch{}
 const atlas=buildAtlas(),planets=[],skies=[],planetOf=k=>planets[k]||(planets[k]=buildPlanet(k)),skyOf=k=>skies[k]||(skies[k]=buildSky(k)),stars=[buildStars(3),buildStars(9)],audio=createAudio(),reduceMotion=matchMedia?.("(prefers-reduced-motion: reduce)").matches;
 let opts={difficulty:store.get("difficulty",2),dial:store.get("dial",true),assist:store.get("assist",false),slowmo:store.get("slowmo",true),shake:store.get("shake",true)};
 let g=null,demo=true,paused=false,focusPaused=false,destroyed=false,raf=0,last=performance.now(),acc=0,publishT=0,submitted=false,endT=0,bannerTimer=0,tick=0,shake=0,flash=0,hitstop=0,result=null,run=null;
 let bombFlash=0,particles=[],pops=[],fx=[],taps=[],shiftHold={l:false,r:false},inpQ={},hoverX=CX,hoverY=0,dial=null;
 const binds=createBindings("oota.padmap.v3",ACTIONS);
 const sel={on:anyPad(),anchor:0,scope:0,menu:null,pick:null,last:null,shift:false,ax:CX,ay:20,dirs:{up:0,down:0,left:0,right:0}};
 const menu=createMenu(stage,{sfx:n=>{if(n==="move")audio.sfx("pick");else if(n==="ok")audio.sfx("ok");else if(n==="back")audio.sfx("deny")}});
 const L=id=>padLabel(binds.get(id));
 const glowC=new Map(),glow=c=>{if(!glowC.has(c)){const k=document.createElement("canvas");k.width=k.height=64;const x=k.getContext("2d"),gr=x.createRadialGradient(32,32,0,32,32,32);gr.addColorStop(0,c);gr.addColorStop(.35,c+"88");gr.addColorStop(1,c+"00");x.fillStyle=gr;x.fillRect(0,0,64,64);glowC.set(c,k)}return glowC.get(c)};
 const halo=(x,y,r,c,a=1)=>{ctx.globalAlpha=a;ctx.drawImage(glow(c),(x-r)*S,(y-r)*S,r*2*S,r*2*S);ctx.globalAlpha=1};
 const GC=34,GR=22,grid=[];for(let j=0;j<GR;j++)for(let i=0;i<GC;i++)grid.push({ox:0,oy:0,vx:0,vy:0});
 function shock(x,y,f){const sx=CW/2+(x-CX)*S*(g?.z||1),sy=y*S*(g?.z||1);for(let j=0;j<GR;j++)for(let i=0;i<GC;i++){const p=grid[j*GC+i],px=i*CW/(GC-1),py=j*CH/(GR-1),dx=px-sx,dy=py-sy,d=Math.hypot(dx,dy)+30;if(d<420){const k=f*900/(d*d)*30;p.vx+=dx/d*k;p.vy+=dy/d*k}}}
 function drawGrid(){const col=["#5ee7ff","#ff6fb5","#b25bd2","#5fd456"][g.level%4];for(const p of grid){p.vx+=-p.ox*.06;p.vy+=-p.oy*.06;p.vx*=.86;p.vy*=.86;p.ox+=p.vx;p.oy+=p.vy}
  ctx.save();ctx.globalCompositeOperation="lighter";ctx.strokeStyle=col;ctx.globalAlpha=.3;ctx.lineWidth=2.5;ctx.beginPath();
  for(let j=0;j<GR;j++)for(let i=0;i<GC;i++){const p=grid[j*GC+i],x=i*CW/(GC-1)+p.ox,y=j*CH/(GR-1)+p.oy;if(i){const q=grid[j*GC+i-1];ctx.moveTo((i-1)*CW/(GC-1)+q.ox,j*CH/(GR-1)+q.oy);ctx.lineTo(x,y)}if(j){const q=grid[(j-1)*GC+i];ctx.moveTo(i*CW/(GC-1)+q.ox,(j-1)*CH/(GR-1)+q.oy);ctx.lineTo(x,y)}}
  ctx.stroke();ctx.restore()}
 let titleOn=true,idle=false,fireHeld=false,cut=null;
 const fromTitle=sc=>()=>menu.open({...sc,back:()=>menu.close()});
 const title=createTitle({ctx,CW,CH,atlas,planetOf,skyOf,stars,audio,rumble:(ms,s)=>{try{window.ArcadeGridzPad?.rumble?.(ms,s)}catch{}},items:name=>{
  if(name==="diff")return[1,2,3].map(d=>({label:DIFF[d].name,hint:["","A GENTLE START. ROOKIE PILOTS.","THE INTENDED INVASION.","SHARP PILOTS. NO MERCY."][d],off:()=>false,act:()=>newRun(d)})).concat([{label:"BACK",hint:"",off:()=>false,act:()=>title.openList("play",1)}]);
  if(name==="hdiff")return[1,2,3].map(d=>({label:DIFF[d].name,hint:["","A GENTLE START. FEWER ALIENS.","THE INTENDED DEFENSE.","A HUNGRY SWARM. NO MERCY."][d],off:()=>false,act:()=>startHero(d)})).concat([{label:"BACK",hint:"",off:()=>false,act:()=>title.openList("play",2)}]);
  if(name==="over")return[{label:"START OVER",hint:"YOUR SAVED INVASION WILL BE LOST",off:()=>false,act:()=>title.openList("diff",opts.difficulty-1)},{label:"KEEP MY SAVE",hint:"",off:()=>false,act:()=>title.openList("play",0)}];
  if(name==="play")return[{label:"CONTINUE",hint:()=>{const r=savedRun();return r?`LEVEL ${r.level+1} ${LEVELS[r.level].name} // ${DIFF[r.difficulty].name} // ${fmt(r.score)}`:"NO INVASION IN PROGRESS"},off:()=>!savedRun(),act:continueRun},
   {label:"NEW GAME",hint:"INVADE THE SOLAR SYSTEM, EARTH LAST",off:()=>false,act:()=>savedRun()?title.openList("over"):title.openList("diff",opts.difficulty-1)},
   {label:"HERO MODE",hint:"FLY THE HUMAN SHIP FROM EARTH OUT TO PLANET X",off:()=>false,act:()=>title.openList("hdiff",opts.difficulty-1)},
   {label:"BACK",hint:"",off:()=>false,act:()=>title.openList("main")}];
  return[{label:"PLAY GAME",hint:()=>savedRun()?"CONTINUE, NEW GAME OR HERO MODE":"NEW GAME OR HERO MODE",off:()=>false,act:()=>title.openList("play",savedRun()?0:1)},
   {label:"OPTIONS",hint:"SOUND, CONTROLS, DIALOGUE, ASSIST",off:()=>false,act:fromTitle(optionsScreen())},
   {label:"HOW TO PLAY",hint:"",off:()=>false,act:fromTitle(howTo())},
   {label:"EXIT GAME",hint:"BACK TO ARCADEGRIDZ",off:()=>false,act:exitGame}]}});
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
// ring order menu (controller/keyboard): icons around the selected alien, d-pad aims, tooltip explains
 const RING=[0,1,2,3,4];
 const canMerge=()=>{const a=anchorAlien();return sel.scope===0&&a&&a.sparkle>0&&a.species===1&&a.kind==="grunt"};
 const ringOf=a=>(CLASSES[a?.kind]||CLASSES.grunt).ring;
 const ringSlot=(i,a=anchorAlien())=>i===0&&canMerge()?"merge":ringOf(a)[i];
 function openOrders(){const a=ensureAnchor();if(!live()||!a)return;const r=ringOf(a);let idx=sel.last==null?-1:r.indexOf(sel.last);if(idx<0||lockedCmd(g,sel.last))idx=r.findIndex(c=>c!=null&&c!==CMD.RANDOM&&!lockedCmd(g,c));if(canMerge())idx=0;sel.menu={idx:Math.max(0,idx)};audio.sfx("pick")}
 function aimRing(){const dx=sel.dirs.right-sel.dirs.left,dy=sel.dirs.down-sel.dirs.up;if(!dx&&!dy)return;const n=Math.hypot(dx,dy);let bi=sel.menu.idx,bs=-2;
  for(const i of RING){if(ringSlot(i)==null)continue;const[ox,oy]=CMD_POS[i],m=Math.hypot(ox,oy),sc=(ox*dx+oy*dy)/(m*n);if(sc>bs){bs=sc;bi=i}}if(bi!==sel.menu.idx){sel.menu.idx=bi;audio.sfx("pick")}}
 function confirmOrder(){const m=sel.menu;if(!m)return;const slot=ringSlot(m.idx);if(slot==null)return;if(slot!=="merge"&&lockedCmd(g,slot)){audio.sfx("deny");return}sel.menu=null;
  if(slot==="merge"){sel.pick={src:sel.anchor};banner("PICK A PARTNER","HOP TO ANOTHER ALIEN, THEN PRESS "+L("order"));return}
  inpQ.command={cmd:slot,ids:selIds()};sel.last=slot}
 function repeatOrder(){if(sel.last==null){openOrders();return}if(lockedCmd(g,sel.last)){audio.sfx("deny");return}const ids=selIds();if(ids.length)inpQ.command={cmd:sel.last,ids}}
 function action(name,down,e){sel.on=true;
  if(idle&&down&&name!=="pause"&&live()){setIdle(false);return}
  if(g&&g.mode==="defense"&&!demo){if(name==="pause"){if(down)openPause();return}
   if(name==="dir"&&(e==="left"||e==="right")||name==="left"||name==="right"){shiftHold[(name==="dir"?e:name)==="left"?"l":"r"]=down;return}
   if(name==="order"||name==="repeat"){fireHeld=down;return}if((name==="rush"||name==="snatch")&&down)inpQ.bomb=true;return}
  if(name==="left"||name==="right"){shiftHold[name==="left"?"l":"r"]=down;if(down&&live())tutor("move");return}
  if(name==="dir"){const d=e;sel.dirs[d]=down?1:0;
   if(!down||!live())return;
   if(sel.menu){aimRing();return}
   hop(d==="left"?-1:d==="right"?1:0,d==="up"?-1:d==="down"?1:0);tutor("hop");return}
  if(!down)return;
  if(name==="pause"){openPause();return}
  if(!live())return;audio.unlock();
  if(sel.menu){if(name==="order"||name==="repeat"){confirmOrder();tutor("order")}else if(name==="rush"){sel.menu=null;audio.sfx("deny")}return}
  if(sel.pick){if(name==="order"){if(sel.anchor!==sel.pick.src)inpQ.merge={src:sel.pick.src,dst:sel.anchor};else audio.sfx("deny");sel.pick=null}else if(name==="rush"){sel.pick=null;audio.sfx("deny")}return}
  if(name==="order")openOrders();else if(name==="repeat")repeatOrder();else if(name==="snatch"){inpQ.snatch=true;tutor("snatch")}else if(name==="rush"){inpQ.rush=true;tutor("rush")}
  else if(name==="scope"){sel.scope=(sel.scope+1)%3;audio.sfx("pick");banner(`ORDERS GO TO: ${["ONE ALIEN","THE WHOLE ROW","THE WHOLE SWARM"][sel.scope]}`,"");tutor("scope")}else if(name==="idle")nextIdle()}
 // first-invasion tutorial: one tip at a time, each cleared by doing it
 const TUT=[["hop","PICK AN ALIEN","D-PAD","ARROWS"],["order","GIVE IT AN ORDER","order","Z"],["move","MOVE THE WHOLE SWARM","left+right","Q / E"],["snatch","SNATCH THE GREEN POWER-UPS","snatch","X"],["scope","ORDER A ROW OR EVERYONE","scope","TAB"],["rush","RUSH THE SHIP (RECHARGES)","rush","SPACE"]];
 let tut=store.get("tut",0);
 function tutor(k){if(tut<TUT.length&&TUT[tut][0]===k){tut++;store.set("tut",tut);audio.sfx("powerup")}}
 function drawTutorial(){if(demo||tut>=TUT.length||!g||g.level!==0||paused||g.mode==="defense")return;const[k,msg,pad,kb]=TUT[tut],btn=sel.on?(pad==="D-PAD"?"D-PAD":pad.split("+").map(id=>L(id)).join(" / ")):kb,
  w=640,h=74,x=(CW-w)/2,y=CH*.44;ctx.globalAlpha=.88+.12*Math.sin(tick*.15);ctx.fillStyle="rgba(5,4,10,.86)";ctx.fillRect(x,y,w,h);ctx.strokeStyle="#5fd456";ctx.lineWidth=3;ctx.strokeRect(x+1.5,y+1.5,w-3,h-3);
  ctx.textAlign="center";ctx.font='10px "Press Start 2P",monospace';ctx.fillStyle="#9fe9ff";ctx.fillText(`TIP ${tut+1}/${TUT.length}`,CW/2,y+22);
  ctx.font='17px "Press Start 2P",monospace';const t=`${btn}  ${msg}`,tw=ctx.measureText(t).width;ctx.fillStyle="#05040a";ctx.fillText(t,CW/2+2,y+56);ctx.fillStyle="#c8ff9a";ctx.fillText(t,CW/2,y+54);
  const bw=ctx.measureText(btn).width;ctx.fillStyle="#ffd93a";ctx.textAlign="left";ctx.fillText(btn,CW/2-tw/2,y+54);ctx.globalAlpha=1}
 // ---------- input ----------
 const KB={ArrowUp:["dir","up"],ArrowDown:["dir","down"],ArrowLeft:["dir","left"],ArrowRight:["dir","right"],KeyW:["dir","up"],KeyS:["dir","down"],KeyA:["dir","left"],KeyD:["dir","right"],
  KeyZ:["order"],Enter:["order"],KeyC:["repeat"],KeyX:["snatch"],Space:["rush"],KeyQ:["left"],KeyE:["right"],Tab:["scope"],KeyP:["pause"],Escape:["pause"]};
 const onKey=e=>{if(e.target.closest?.("input,textarea,select"))return;
  if(cut&&!demo&&!menu.isOpen()){e.preventDefault();if(e.type==="keydown"&&!e.repeat&&(Date.now()-cut.t0>400))endCut();return}
  if(e.type==="keydown"&&e.code==="KeyI"&&live()){setIdle(!idle);e.preventDefault();return}
  if(menu.handleKey(e)){if(isPad(e))sel.on=true;e.preventDefault();return}
  const down=e.type==="keydown";
  if(titleOn&&demo){e.preventDefault();if(!down||e.repeat&&!/Arrow|Key[WS]/.test(e.code))return;sel.on=isPad(e)||sel.on;const pad=isPad(e),d=pad?padDir(e):({ArrowUp:"up",ArrowDown:"down",ArrowLeft:"left",ArrowRight:"right",KeyW:"up",KeyS:"down"})[e.code];
   const name=d||(pad?(e.code==="Escape"||e.code==="Enter"?"ok":e.code==="Space"?"back":e.code==="KeyP"?"start":null):(e.code==="Enter"||e.code==="Space"||e.code==="KeyZ"?"ok":e.code==="Escape"||e.code==="Backspace"?"back":e.code==="KeyP"?"start":null));
   if(name){audio.unlock();title.input(name)}return}
  if(isPad(e)){e.preventDefault();const d=padDir(e);if(d){action("dir",down,d);return}if(e.code==="KeyP"){action("pause",down);return}const a=binds.actionFor(e.code);if(a)action(a,down);return}
  if(down&&e.code==="KeyM"){toggleAudio();e.preventDefault();return}
  if(down&&KEYCMD[e.code]!=null&&live()){const ids=selIds();inpQ.command=ids.length&&sel.on?{cmd:KEYCMD[e.code],ids}:{cmd:KEYCMD[e.code],near:hoverX};sel.last=KEYCMD[e.code];e.preventDefault();return}
  const k=KB[e.code];if(!k)return;e.preventDefault();if(e.repeat&&k[0]!=="dir")return;if(k[0]==="dir")action("dir",down,k[1]);else action(k[0],down)};
 addEventListener("keydown",onKey);addEventListener("keyup",onKey);
 const view=()=>g?.z||1;
 const world=e=>{const r=canvas.getBoundingClientRect(),sx=(e.clientX-r.left)/r.width*W,sy=(e.clientY-r.top)/r.height*H,z=view();return{x:CX+(sx-CX)/z,y:sy/z}};
 let arrowPtr=null;
 const cpx=e=>{const r=canvas.getBoundingClientRect();return{x:(e.clientX-r.left)/r.width*CW,y:(e.clientY-r.top)/r.height*CH}};
 canvas.addEventListener("pointermove",e=>{if(titleOn&&demo){const p=cpx(e);title.hover(p.x,p.y)}const wp=world(e);hoverX=wp.x;hoverY=wp.y;if(e.pointerType==="mouse"&&(e.movementX||e.movementY))sel.on=false});
 canvas.addEventListener("pointerdown",e=>{if(cut&&!demo){endCut();return}if(titleOn&&demo&&!menu.isOpen()){e.preventDefault();audio.unlock();const p=cpx(e);title.click(p.x,p.y);return}if(!live())return;const p=world(e);e.preventDefault();canvas.focus?.({preventScroll:true});audio.unlock();sel.on=false;sel.menu=null;sel.pick=null;
  const r=canvas.getBoundingClientRect(),sx=(e.clientX-r.left)/r.width*W,sy=(e.clientY-r.top)/r.height*H;
  for(const a of ARROWS)if(Math.abs(sx-a.x)<14&&Math.abs(sy-a.y)<26){arrowPtr={id:e.pointerId,dir:a.dir};return}
  if(g.mode==="defense"){fireHeld=true;return}taps.push(p)});
 const up=e=>{if(arrowPtr&&arrowPtr.id===e.pointerId)arrowPtr=null;else if(g?.mode==="defense"&&e.pointerType!=="mouse"||g?.mode==="defense"&&e.type==="pointerup")fireHeld=false};
 canvas.addEventListener("pointerup",up);canvas.addEventListener("pointercancel",up);
 function readInput(){if(g.mode==="defense"){const inp=idle?defBot():{move:(shiftHold.r?1:0)-(shiftHold.l?1:0),fire:fireHeld||!sel.on&&false};if(inpQ.bomb)inp.bomb=true;inpQ={};if(arrowPtr)inp.move=arrowPtr.dir;return inp}
  if(idle){inpQ={};return autoInput(g)}
  const inp=inpQ;inpQ={};const sh=(shiftHold.r||arrowPtr?.dir===1?1:0)-(shiftHold.l||arrowPtr?.dir===-1?1:0);if(sh)inp.shift=sh*1.4;
  const t=taps.shift();if(t)inp.tap=t;return inp}
 // ---------- events / fx ----------
 function burst(x,y,c,n=10,sp=1.2,grav=0){if(reduceMotion)n=Math.min(n,4);for(let i=0;i<n;i++){const a=Math.random()*6.283,v=.3+Math.random()*sp;particles.push({x,y,vx:Math.cos(a)*v,vy:Math.sin(a)*v,life:10+Math.random()*14,c,grav,sz:grav?2:1})}}
 function banner(text,sub=""){bannerEl.hidden=false;bannerEl.innerHTML=`<b>${text}</b>${sub?`<span>${sub}</span>`:""}`;bannerEl.classList.remove("on");void bannerEl.offsetWidth;bannerEl.classList.add("on");clearTimeout(bannerTimer);bannerTimer=setTimeout(()=>{bannerEl.hidden=true},2300)}
 function say(lines){if(!opts.dial||demo||!lines)return;dial={lines,i:0,t:150}}
 const BUFF_NAMES={speed:"SPEED",spit:"FIRE RATE",bugs:"BUGS",spawn:"SPAWN RATE",armor:"ARMOR",tentacles:"TENTACLES",lasers:"LASERS",missiles:"MISSILES",plazma:"PLAZMA",shields:"SHIELDS",option:"DRONE OPTION"};
 const kick=n=>{if(opts.shake)shake=Math.max(shake,n)};
 const HAZARD_TEXT={nuke:["NUKE POD INCOMING!","SHOOT IT BEFORE THE SHIP GRABS IT"],acid:["ACID CLOUD","IT EATS ALIENS"],moon:["MOON RISING","IT SHIELDS THE SHIP"],storm:["NEPTUNE STORM","THE SWARM DRIFTS"]};
 function handleEvents(){for(const e of g.events){
  if(e.type==="sfx"){if(!demo)audio.sfx(e.name)}
  else if(e.type==="pop")pops.push({x:e.x,y:e.y,t:40,txt:e.txt,c:e.c});
  else if(e.type==="alienDie"){shock(e.x,e.y,.6);burst(e.x,e.y,"#ffffff",3,.8)}
  if(e.type==="alienDie")burst(e.x,e.y,e.species===2?"#3fd4bd":"#5fd456",10);
  else if(e.type==="manHurt"){burst(e.x,e.y,"#ff9a2e",10,1.4);shock(e.x,e.y,1.4);kick(3)}
  else if(e.type==="nova"){fx.push({k:"nova",x:e.x,y:e.y,t:40});shock(e.x,e.y,8);flash=18;kick(14);rumble(500,1)}
  else if(e.type==="manDie"){shock(e.x,e.y,3);burst(e.x,e.y,"#ffd93a",18,1.8);burst(e.x,e.y,"#ff5468",10);burst(e.x,e.y,"#b9c1cf",8,1.4,.06);kick(4);rumble(180,.6)}
  else if(e.type==="shipDie"){shock(e.x,e.y,e.kind==="carrier"?6:2.5);burst(e.x,e.y,"#ffd93a",e.kind==="carrier"?34:16,1.8);burst(e.x,e.y,"#94a0b8",10,1.6,.06);kick(e.kind==="carrier"?8:4);if(e.kind==="carrier")hitstop=3;rumble(200,.7)}
  else if(e.type==="spark")burst(e.x,e.y,"#ffe08a",3,.8);
  else if(e.type==="iceBreak"){burst(e.x,e.y,"#d2fbff",14,1.6,.05);burst(e.x,e.y,"#5ee7ff",6,1)}
  else if(e.type==="zap")fx.push({k:"zap",pts:e.pts,t:6});
  else if(e.type==="phase"){burst(e.x0,e.y,"#b25bd2",8,1);burst(e.x1,e.y,"#b25bd2",8,1)}
  else if(e.type==="freeze")burst(e.x,e.y,"#d2fbff",10,1.4);
  else if(e.type==="rage")burst(e.x,e.y,"#ff5468",12,1.6);
  else if(e.type==="bombGet")bombFlash=40;
  else if(e.type==="emp")fx.push({k:"emp",x1:e.x1,y1:e.y1,t:14});
  else if(e.type==="mend")burst(e.x,e.y,"#5fd456",6,1);
  else if(e.type==="hatch"){burst(e.x,e.y,"#c8ff9a",8,1);pops.push({x:e.x,y:e.y-8,t:30,txt:"HATCH!",c:"#c8ff9a"})}
  else if(e.type==="super"&&!demo){banner(`SUPER: ${SUPER_NAMES[e.kind]}!`,"THE SWARM GOES BONKERS");flash=10;kick(6);rumble(250,.8);burst(e.x,e.y,"#ffd93a",24,2)}
  else if(e.type==="nukeArmed"&&!demo){banner("NUKE ARMED!","KILL THE SHIP BEFORE IT BLOWS");rumble(300,.8)}
  else if(e.type==="nukeBoom"){shock(CX,90,14);flash=30;kick(16);hitstop=6;rumble(900,1);if(!demo)setTimeout(()=>banner("THE SWARM WAS NUKED",`${e.lost} LOST // CEASEFIRE: REBUILD!`),300)}
  else if(e.type==="defused"&&!demo){banner("NUKE DEFUSED!","+1000");burst(e.x,e.y,"#5fd456",20,2)}
  else if(e.type==="podBoom"){burst(e.x,e.y,"#ffd93a",20,2);burst(e.x,e.y,"#ff5468",10,1.4)}
  else if(e.type==="snatch")fx.push({k:"snatch",x0:e.x0,y0:e.y0,x1:e.x1,y1:e.y1,t:10});
  else if(e.type==="roll")fx.push({k:"roll",x:e.x,y:e.y,t:14});
  else if(e.type==="launch")burst(e.x,e.y-6,"#5ee7ff",6,.8);
  else if(e.type==="dome")burst(e.x,e.y,"#5ee7ff",2,.5);
  else if(e.type==="hazard"&&!demo&&HAZARD_TEXT[e.kind])banner(...HAZARD_TEXT[e.kind]);
  else if(e.type==="merged"){burst(e.x,e.y,"#ff6fb5",16,1.5);if(!demo){banner("MERGED!","A NEW SPECIES");rumble(120,.4)}}
  else if(e.type==="buff"&&!demo){if(e.side==="me")pops.push({x:CX,y:60,t:46,txt:`SWARM +${BUFF_NAMES[e.kind]}`,c:"#5fd456"});else pops.push({x:CX,y:120,t:46,txt:`SHIP +${BUFF_NAMES[e.kind]}`,c:"#ff9a2e"})}
  else if(e.type==="planetDestroyed"){shock(CX,150,10);flash=16;kick(10);hitstop=4;rumble(500,1);audio.hyper?.(true);if(!demo){banner(`${g.L.name} DESTROYED!`,g.L.final?"THE ADMIRAL ATTACKS":"HERE COMES THE BOSS");say(BOSS_LINES[g.L.id])}}
  else if(e.type==="boss"){shock(e.x,e.y,10);burst(e.x,e.y,"#ffd93a",40,2.5);burst(e.x,e.y,"#ffffff",20,2);burst(e.x,e.y,"#94a0b8",14,2,.06);flash=22;kick(12);hitstop=8;rumble(600,1)}
  else if(e.type==="stage"&&!demo)banner(`STAGE ${e.stage}`,"THE SHIP GETS UPGRADES");
  else if(e.type==="descend"&&!demo&&g.aliens.some(a=>a.mey>bot(g)*.42))pops.push({x:CX,y:bot(g)*.55,t:30,txt:"THE SWARM DESCENDS",c:"#ff6fb5"});
  else if(e.type==="end")finish()}}
 // ---------- drawing (world units, scaled by S and the camera zoom) ----------
 function text(t,x,y,size,c,align="left",shadow="#05040a"){ctx.font=`${Math.round(size*.78)}px "Press Start 2P",monospace`;ctx.textAlign=align;if(shadow){ctx.fillStyle=shadow;ctx.fillText(t,x*S+2,y*S+2)}ctx.fillStyle=c;ctx.fillText(t,x*S,y*S)}
 function spr(img,x,y,a=1,sc=1){if(!img)return;if(a<1)ctx.globalAlpha=a;const w=img.width*S*sc,h=img.height*S*sc;ctx.drawImage(img,Math.round(x*S-w/2),Math.round(y*S-h/2),w,h);if(a<1)ctx.globalAlpha=1}
 const ring=(x,y,r,c,lw=4)=>{ctx.strokeStyle=c;ctx.lineWidth=lw;ctx.beginPath();ctx.arc(x*S,y*S,r*S,0,6.283);ctx.stroke()};
 const SHIPSPR={fighter:"fighter",interceptor:"interceptor",carrier:"carrier",repair:"repair",frigate:"frigate"};
 function drawWorld(){const art=g.L.art,z=g.z,B=bot(g),x0=fx0(g),x1=fx1(g),fw=x1-x0;
  ctx.drawImage(skyOf(art),0,0,CW,CH);ctx.fillStyle="rgba(2,1,10,.5)";ctx.fillRect(0,0,CW,CH);drawGrid();
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
  for(const t of g.tentacles){const len=Math.min(56,(30-t.life)*6+8),pts=[];for(let i=0;i<=10;i++){const u=i/10,w=Math.sin((t.ph||0)*1.4+u*6)*3*u,x=(1-u)*(1-u)*t.x+2*(1-u)*u*t.x+u*u*(t.tx??t.x)+w,y=t.y+4+u*len;pts.push([x,y,(1-u)*3.2+.9])}
   for(const[pass,col]of[[0,"#05040a"],[1,"#b0306a"]]){ctx.strokeStyle=col;ctx.lineCap="round";for(let i=1;i<pts.length;i++){ctx.lineWidth=(pts[i][2]+pts[i-1][2])*S*.5+(pass?0:5);ctx.beginPath();ctx.moveTo(pts[i-1][0]*S,pts[i-1][1]*S);ctx.lineTo(pts[i][0]*S,pts[i][1]*S);ctx.stroke()}}
   ctx.fillStyle="#ffc0e0";for(let i=1;i<pts.length-1;i+=2){const[x,y,r]=pts[i];ctx.beginPath();ctx.arc((x+r*.3)*S,y*S,r*.35*S,0,6.283);ctx.fill()}ctx.globalCompositeOperation="lighter";halo(pts[10][0],pts[10][1],6,"#ff6fb5",.6);ctx.globalCompositeOperation="source-over"}
  // bonus targets
  for(const i of g.ice)if(i.hp>0){const k=Math.min(3,Math.floor((1-i.hp/i.max)*4));ctx.save();ctx.translate(i.x*S,i.y*S);ctx.rotate(i.ang||0);ctx.drawImage(atlas.ice[k],-i.w/2*S,-5*S,i.w*S,10*S);ctx.restore()}
  for(const u of g.ufos)if(!u.dead)spr(atlas.ufo,u.x,u.y);
  for(const f of g.flares){if(f.t<0){ctx.strokeStyle=`rgba(255,106,16,${.25+.2*Math.sin(tick*.6)})`;ctx.lineWidth=6;ctx.setLineDash([10,10]);ctx.beginPath();for(let k=0;k<=20;k++){const q=k/20,X=f.dir>0?x0-10+(fw+20)*q:x1+10-(fw+20)*q,Y=B+10-(B+10-f.peak)*Math.sin(q*Math.PI);k?ctx.lineTo(X*S,Y*S):ctx.moveTo(X*S,Y*S)}ctx.stroke();ctx.setLineDash([])}
   else{ctx.fillStyle="rgba(255,106,16,.35)";ctx.beginPath();ctx.arc(f.x*S,f.y*S,15*S,0,6.283);ctx.fill();ctx.fillStyle="#ffd24a";ctx.beginPath();ctx.arc(f.x*S,f.y*S,8*S,0,6.283);ctx.fill();ctx.fillStyle="#ffffff";ctx.beginPath();ctx.arc(f.x*S,f.y*S,4*S,0,6.283);ctx.fill()}}
  // the swarm
  ctx.globalCompositeOperation="lighter";for(const a of g.aliens)halo(a.x,a.y,a.species===2?18:12,a.rage>0?"#ff5468":KCOL[a.kind]||(a.variant?"#b25bd2":"#5fd456"),a.ghost>0?.2:.6);for(const s of g.ships)halo(s.x,s.y,s.kind==="carrier"?28:14,"#5ee7ff",.3);if(g.man)halo(g.man.x,g.man.y,16,"#ffffff",.35);if(g.boss)halo(g.boss.x,g.boss.y,30,"#ff5468",.4);ctx.globalCompositeOperation="source-over";
  const ids=sel.on&&live()&&g.mode!=="defense"?new Set(selIds()):null,anchor=sel.anchor;
  for(const a of g.aliens){const f=(tick>>3)&1;let img=a.species===2?atlas.big[f]:a.kind!=="grunt"&&atlas[a.kind]?atlas[a.kind][f]:atlas.alien[a.variant][f];
   if(a.chosen&&a.cmd===CMD.SHIELD&&a.dome>0){ctx.globalAlpha=.25+a.dome*.08;ring(a.x,a.y,22,"#5ee7ff",4);ctx.fillStyle="rgba(94,231,255,.1)";ctx.beginPath();ctx.arc(a.x*S,a.y*S,22*S,0,6.283);ctx.fill();ctx.globalAlpha=1}
   if(a.chosen&&a.cmd===CMD.SANCT){ctx.setLineDash([12,10]);ctx.globalAlpha=.6;ring(a.x,a.y,26,"#ffd93a",3);ctx.setLineDash([]);ctx.fillStyle="rgba(255,217,58,.07)";ctx.beginPath();ctx.arc(a.x*S,a.y*S,26*S,0,6.283);ctx.fill();ctx.globalAlpha=1}
   if(a.chosen&&a.cmd===CMD.TAUNT&&tick%40<20)text("!",a.x,a.y-12,22,"#ff5468","center");
   if(g.sup.laser>0&&a.chosen&&a.cmd===CMD.ATTACK&&!a.kami){const w=(3+Math.sin(tick*.8))*S;ctx.fillStyle="rgba(95,212,86,.35)";ctx.fillRect(a.x*S-w*1.6,a.y*S+6*S,w*3.2,(bot(g)-a.y)*S);ctx.fillStyle="#d2ffc0";ctx.fillRect(a.x*S-w/2,a.y*S+6*S,w,(bot(g)-a.y)*S)}
   if(a.sparkle>0){ctx.globalAlpha=.5+.5*Math.sin(tick*.6);ctx.fillStyle=["#ff6fb5","#5ee7ff","#ffd93a"][(tick>>2)%3];ctx.beginPath();ctx.arc(a.x*S,a.y*S,11*S,0,6.283);ctx.fill();ctx.globalAlpha=1}
   if(g.merge?.id===a.id||sel.pick?.src===a.id)ring(a.x,a.y,12,"#ff6fb5");
   if(ids&&ids.has(a.id)){const main=a.id===anchor,r=(a.species===2?13:9.5)+Math.sin(tick*.3)*.6;ring(a.x,a.y,r,"#05040a",8);ring(a.x,a.y,r,main?"#ffd93a":"rgba(94,231,255,.85)",main?4:3)}
   spr(img,a.x,a.y,a.ghost>0?.3+.15*Math.sin(tick*.9):a.kami&&tick%6<3?.6:1);
   if(a.armor>0){ctx.fillStyle="#5ee7ff";ctx.fillRect((a.x-6)*S,(a.y+9)*S,Math.min(12,a.armor*3)*S,S)}
   if(!demo&&a.chosen&&a.cmd>=0&&a.okT<=0)spr(atlas.icon[a.cmd],a.x+7,a.y-8,.8,.55)}
  if(!demo)for(const a of g.aliens){if(a.okT>0)text("OK!",a.x,a.y-10,16,"#ffd93a","center");
   else if(!a.chosen&&!a.kami&&a.dive===0&&Math.abs(a.x-a.mex)<1.5&&Math.abs(a.y-a.mey)<1.5){if(sel.on){ctx.fillStyle="#05040a";ctx.fillRect((a.x-1.5)*S,(a.y-11.5)*S,3*S,3*S);ctx.fillStyle="#ffffff";ctx.fillRect((a.x-1)*S,(a.y-11)*S,2*S,2*S)}else text("GO",a.x,a.y-10,13,"#ffffff","center")}}
  ctx.globalCompositeOperation="lighter";for(const s of g.shots){halo(s.x,s.y,s.ice?10:8,s.ice?"#5ee7ff":"#5fd456",1);ctx.fillStyle=s.ice?"#ffffff":"#d2ffc0";if(s.ice)ctx.fillRect((s.x-1.5)*S,(s.y-1.5)*S,3*S,3*S);else ctx.fillRect((s.x-.5)*S,(s.y-3)*S,1*S,6*S)}
  for(const f of g.fires){const big=f.t>0,r=big?6+Math.sin(tick*.7+f.x)*1.5:3;halo(f.x,f.y,big?16:9,"#ff6a10",big?Math.min(1,f.t/40):1);ctx.fillStyle=tick%4<2?"#ffd24a":"#ff9a2e";ctx.beginPath();ctx.ellipse(f.x*S,f.y*S,r*S,(big?3:r)*S,0,0,6.283);ctx.fill()}
  for(const p of g.spores){halo(p.x,p.y,9,"#c8ff9a",.8);ctx.fillStyle=p.t<15&&tick%4<2?"#ffffff":"#e6ff8a";ctx.beginPath();ctx.arc(p.x*S,p.y*S,(2.5+(p.t<15?1:0))*S,0,6.283);ctx.fill()}for(const s of g.globs)halo(s.x,s.y,8,"#3fd4bd",.7);for(const b of g.bugs)halo(b.x,b.y,6,"#ff6a10",.7);ctx.globalCompositeOperation="source-over";
  for(const s of g.globs)spr(atlas.glob[(tick>>2)&1],s.x,s.y);
  for(const b of g.bugs)spr(atlas.bug[(tick>>1)&1],b.x,b.y);
  for(const l of g.loots){if(l.done){text(l.done===1?(l.orb?"":l.good?"POWER UP!":"BLOCKED!"):"",l.x,l.y-l.t*.2,13,"#ffffff","center");continue}
   if(l.super)spr(atlas.super[(tick>>2)&3],l.x,l.y,1,1.15+.15*Math.sin(tick*.5));else if(l.orb)spr(atlas.orb[(tick>>3)&1],l.x,l.y,1,1+.12*Math.sin(tick*.4));else if(l.bomb){ctx.globalCompositeOperation="lighter";halo(l.x,l.y,14,"#ffd93a",.6+.3*Math.sin(tick*.4));ctx.globalCompositeOperation="source-over";spr(atlas.bombLoot[(tick>>3)&1],l.x,l.y,1,1.2);text("BOMB",l.x,l.y-11,12,tick%20<10?"#ffd93a":"#ffffff","center")}
   else spr((l.good?atlas.good:atlas.bad)[(tick>>3)&1],l.x,l.y,1,1+.1*Math.sin(tick*.4))}
  // the humans
  for(const s of g.ships){const img=atlas[SHIPSPR[s.kind]];spr(img,s.x,s.y,s.inv>0&&tick%4<2?.5:1);
   if(s.kind==="frigate"&&s.dome>0){ctx.globalAlpha=.15+.08*s.dome;ring(s.x,s.y,24,"#5ee7ff",3);ctx.fillStyle="rgba(94,231,255,.08)";ctx.beginPath();ctx.arc(s.x*S,s.y*S,24*S,0,6.283);ctx.fill();ctx.globalAlpha=1}
   if(s.max>1){ctx.fillStyle="#05040a";ctx.fillRect((s.x-8)*S,(s.y+(s.kind==="carrier"?12:9))*S,16*S,S*1.2);ctx.fillStyle="#ff5468";ctx.fillRect((s.x-8)*S,(s.y+(s.kind==="carrier"?12:9))*S,16*S*s.hp/s.max,S*1.2)}}
  for(const b of g.beams){const t=b.hero?g.man:g.ships.find(s=>s.id===b.id);if(!t)continue;ctx.strokeStyle=`rgba(95,212,86,${b.t/20})`;ctx.lineWidth=5;ctx.beginPath();ctx.moveTo(b.x0*S,b.y0*S);ctx.lineTo(t.x*S,t.y*S);ctx.stroke()}
  if(g.motherX!=null){const bx=g.motherX,w=(14+Math.sin(tick*.7)*3)*S;ctx.globalCompositeOperation="lighter";ctx.fillStyle="rgba(255,111,181,.25)";ctx.fillRect(bx*S-w*1.8,0,w*3.6,B*S);ctx.fillStyle="rgba(255,240,250,.85)";ctx.fillRect(bx*S-w/2,0,w,B*S);ctx.globalCompositeOperation="source-over";spr(atlas.big[(tick>>3)&1],bx,8,1,2)}
  for(const r of g.rocks){ctx.globalCompositeOperation="lighter";halo(r.x,r.y,10,"#ff6a10",.9);ctx.fillStyle="#ffd24a";ctx.fillRect((r.x-r.vx*3-1)*S,(r.y-r.vy*3-1)*S,2*S,2*S);ctx.globalCompositeOperation="source-over";spr(atlas.rock[0],r.x,r.y)}
  if(g.pod){spr(atlas.pod[(tick>>3)&1],g.pod.x,g.pod.y,1,1.2);text("NUKE",g.pod.x,g.pod.y-12,13,tick%20<10?"#ff5468":"#ffd93a","center")}
  if(g.hole){const h=g.hole;for(let k=0;k<4;k++){ctx.globalAlpha=.25+k*.12;ring(h.x,h.y,30-k*6+Math.sin(tick*.3+k)*2,["#7a2a9a","#b25bd2","#eab3ff","#ffffff"][k],4)}ctx.globalAlpha=1;ctx.fillStyle="#05040a";ctx.beginPath();ctx.arc(h.x*S,h.y*S,9*S,0,6.283);ctx.fill()}
  const m=g.man;if(m&&g.nuke){ctx.globalAlpha=.5+.5*Math.sin(tick*.6);ring(m.x,m.y,14,"#ff5468",5);ctx.globalAlpha=1;text(`NUKE ${Math.ceil(g.nuke.t/30)}`,m.x,m.y-16,16,"#ff5468","center")}
  if(m&&m.stun>0)text("ZZT",m.x,m.y-12,12,"#5ee7ff","center");
  if(m&&m.max>1){ctx.fillStyle="#05040a";ctx.fillRect((m.x-9)*S,(m.y+10)*S,18*S,1.4*S);ctx.fillStyle=m.hp/m.max>.5?"#5fd456":"#ff5468";ctx.fillRect((m.x-9)*S,(m.y+10)*S,18*S*m.hp/m.max,1.4*S)}
  if(m){spr(m.shields>0?atlas.shipShield:atlas.ship,m.x,m.y,m.inv>0&&tick%4<2?.4:1);if(m.shields>0)ring(m.x,m.y,10,`rgba(94,231,255,${.5+.3*Math.sin(tick*.4)})`)}
  for(const o of g.options)spr(atlas.ship,o.x,o.y,.8,.5);
  if(g.boss){const Bs=g.boss;spr(atlas.boss[Bs.type-1],Bs.x,Bs.y);if(Bs.dome>0){ctx.globalAlpha=.4;ring(Bs.x,Bs.y,30,"#ffd93a",5);ctx.globalAlpha=1}}
  ctx.globalCompositeOperation="lighter";for(const s of g.manshots){halo(s.x,s.y,8,"#ff9a2e",1);ctx.fillStyle="#ffe08a";ctx.fillRect((s.x-.5)*S,(s.y-3)*S,1*S,6*S)}for(const s of g.missiles)halo(s.x,s.y,7,"#ff5468",.7);for(const s of g.plazmas)halo(s.x,s.y,7,"#ff6fb5",.8);for(const s of g.bossShots)halo(s.x,s.y,7,"#ffd93a",.7);ctx.globalCompositeOperation="source-over";
  for(const s of g.bossShots){ctx.fillStyle=s.kind==="jup"?"#ffd93a":"#a39aac";ctx.fillRect((s.x-2)*S,(s.y-2)*S,4*S,4*S)}
  for(const s of g.missiles)spr(atlas.missile,s.x,s.y);
  for(const s of g.plazmas){ctx.fillStyle=tick%4<2?"#ff6fb5":"#ffffff";ctx.fillRect((s.x-2)*S,(s.y-2)*S,4*S,4*S)}
  for(const e of fx){if(e.k==="nova"){const r=(40-e.t)*7;ctx.globalCompositeOperation="lighter";ctx.globalAlpha=e.t/40;ring(e.x,e.y,r,"#ffffff",12);ring(e.x,e.y,r*.8,"#ffd93a",8);ctx.globalAlpha=1;ctx.globalCompositeOperation="source-over"}
   else if(e.k==="zap"){ctx.strokeStyle="#ffffff";ctx.lineWidth=5;ctx.beginPath();e.pts.forEach(([x,y],i)=>{const jx=i&&i<e.pts.length?(Math.random()-.5)*6:0;i?ctx.lineTo((x+jx)*S,y*S):ctx.moveTo(x*S,y*S)});ctx.stroke();ctx.strokeStyle="#5ee7ff";ctx.lineWidth=2;ctx.stroke()}
   else if(e.k==="emp"){ctx.globalAlpha=e.t/14;ring(e.x1,e.y1,16-e.t,"#5ee7ff",4);ctx.globalAlpha=1}
   else if(e.k==="snatch"){ctx.strokeStyle="#3fd4bd";ctx.lineWidth=8;ctx.beginPath();ctx.moveTo(e.x0*S,e.y0*S);ctx.lineTo(e.x1*S,e.y1*S);ctx.stroke();ctx.strokeStyle="#b8fff1";ctx.lineWidth=3;ctx.stroke()}
   else if(e.k==="roll"){ctx.globalAlpha=e.t/14;spr(atlas.shipShield,e.x-(14-e.t),e.y,.5);spr(atlas.shipShield,e.x+(14-e.t),e.y,.5);ctx.globalAlpha=1}}
  // mouse command menu (radial)
  if(g.menu){const a=g.aliens.find(a=>a.id===g.menu.id);if(a){ctx.fillStyle="rgba(5,4,10,.55)";ctx.beginPath();ctx.arc(a.x*S,a.y*S,26*S,0,6.283);ctx.fill();
   for(let i=0;i<5;i++){const[ox,oy]=CMD_POS[i],c=ringOf(a)[i];if(c!=null)spr((lockedCmd(g,c)?atlas.iconLocked:atlas.icon)[c],a.x+ox,a.y+oy)}spr(atlas.alien[a.variant][0],a.x,a.y)}}
  ctx.globalCompositeOperation="lighter";for(const p of particles){ctx.globalAlpha=Math.min(1,p.life/10);ctx.fillStyle=p.c;ctx.fillRect((p.x-.5)*S,(p.y-.5)*S,S*p.sz*1.4,S*p.sz*1.4);ctx.fillRect((p.x-p.vx*2-.3)*S,(p.y-p.vy*2-.3)*S,S*.7,S*.7)}ctx.globalAlpha=1;ctx.globalCompositeOperation="source-over";
  for(const p of pops)text(p.txt,p.x,p.y-(46-p.t)*.25,15,p.c,"center");
  ctx.restore()}
 // where a world point lands on screen (for overlays drawn without the camera transform)
 const scr=(x,y)=>({x:CW/2+(x-CX)*S*g.z,y:y*S*g.z});
 function win(x,y,w,h){ctx.fillStyle="rgba(0,0,0,.45)";ctx.fillRect(x+6,y+6,w,h);const gr=ctx.createLinearGradient(0,y,0,y+h);gr.addColorStop(0,"#3a5cd8");gr.addColorStop(1,"#0c1660");ctx.fillStyle=gr;ctx.fillRect(x,y,w,h);ctx.strokeStyle="#ffffff";ctx.lineWidth=4;ctx.strokeRect(x+2,y+2,w-4,h-4);ctx.strokeStyle="#9aa6c8";ctx.lineWidth=2;ctx.strokeRect(x+7,y+7,w-14,h-14)}
 const TIP={merge:["MERGE","FUSE WITH A PARTNER INTO A BIGGER SPECIES"]};
 function tooltip(px,py,name,hint,locked){ctx.font='15px "Press Start 2P",monospace';const w1=ctx.measureText(name).width;ctx.font='9px "Press Start 2P",monospace';const w2=ctx.measureText(hint).width;
  const w=Math.max(w1,w2)+36,h=62;let x=px+56,y=py-h/2;if(x+w>253*S)x=px-56-w;x=Math.max(57*S+6,x);y=Math.max(6,Math.min(CH-h-6,y));
  ctx.fillStyle="rgba(5,4,10,.9)";ctx.fillRect(x,y,w,h);ctx.strokeStyle=locked?"#6a6a8a":"#5fd456";ctx.lineWidth=3;ctx.strokeRect(x+1.5,y+1.5,w-3,h-3);ctx.fillStyle=locked?"#3c3c55":"#145a24";ctx.fillRect(x+3,y+3,6,h-6);
  ctx.textAlign="left";ctx.font='15px "Press Start 2P",monospace';ctx.fillStyle="#05040a";ctx.fillText(name,x+20,y+28);ctx.fillStyle=locked?"#8d95ad":"#c8ff9a";ctx.fillText(name,x+18,y+26);
  ctx.font='9px "Press Start 2P",monospace';ctx.fillStyle=locked?"#8d95ad":"#9fe9ff";ctx.fillText(hint,x+18,y+48)}
 function ringInfo(slot){if(slot==="merge")return[...TIP.merge,false];const lk=lockedCmd(g,slot);return[CMD_NAMES[slot],lk?"LOCKED: SNATCH GREEN POWER-UPS":ORDER_HINT[slot],lk]}
 function drawRing(a,active){ctx.save();ctx.translate(CW/2,0);ctx.scale(g.z,g.z);ctx.translate(-CW/2,0);
  ctx.fillStyle="rgba(5,4,10,.62)";ctx.beginPath();ctx.arc(a.x*S,a.y*S,27*S,0,6.283);ctx.fill();ctx.strokeStyle="rgba(95,212,86,.5)";ctx.lineWidth=3;ctx.stroke();
  for(const i of RING){const[ox,oy]=CMD_POS[i],slot=ringSlot(i,a),on=active===i;if(slot==null)continue;if(slot==="merge"){ctx.fillStyle=`hsl(${tick*8%360},90%,65%)`;ctx.beginPath();ctx.arc((a.x+ox)*S,(a.y+oy)*S,6.2*S,0,6.283);ctx.fill();text("M",a.x+ox,a.y+oy+2.4,22,"#05040a","center",null)}
   else spr((lockedCmd(g,slot)?atlas.iconLocked:atlas.icon)[slot],a.x+ox,a.y+oy,1,on?1.25:1);
   if(on){ring(a.x+ox,a.y+oy,8.2+Math.sin(tick*.5)*.5,"#05040a",8);ring(a.x+ox,a.y+oy,8.2+Math.sin(tick*.5)*.5,"#ffd93a",4)}}
  ctx.restore()}
 function drawOrderMenu(){const m=sel.menu,a=anchorAlien();
  if(m&&a){drawRing(a,m.idx);const[ox,oy]=CMD_POS[m.idx],p=scr(a.x+ox,a.y+oy),[n,h,lk]=ringInfo(ringSlot(m.idx));tooltip(p.x,p.y,sel.scope?`${n} x${selIds().length}`:n,h,lk);return}
  // mouse radial: tooltip for the icon under the pointer
  if(g.menu){const b=g.aliens.find(o=>o.id===g.menu.id);if(!b)return;for(const i of RING){const[ox,oy]=CMD_POS[i],c=ringOf(b)[i];if(c==null)continue;if(Math.hypot(hoverX-(b.x+ox),hoverY-(b.y+oy))<8){const p=scr(b.x+ox,b.y+oy),[n,h,lk]=ringInfo(c);tooltip(p.x,p.y,n,h,lk)}}}}
 const FX0px=()=>57*S,FX1px=()=>253*S;
 function drawHud(){const s=snapshot(g);
  ctx.fillStyle="rgba(5,4,14,.78)";ctx.fillRect(0,0,57*S,CH);ctx.fillRect(253*S,0,CW-253*S,CH);
  const best=store.get(`best.${g.difficulty}`,0);
  const Lh=[["HI-SCORE",fmt(Math.max(best,demo?0:g.score))],["SCORE",fmt(g.score)],["COMBO",`x ${g.combo}`],["SWARM",`${s.aliens} (${s.idle} idle)`]];
  Lh.forEach(([k,v],i)=>{text(k,4,12+i*24,14,"#9488ad");text(v,4,22+i*24,18,i===2&&g.combo>1?"#ffd93a":"#ffffff")});
  text(g.L.name,4,112,18,"#5ee7ff");text(`LEVEL ${g.level+1}/${LEVELS.length}`,4,121,12,"#9488ad");text(DIFF[g.difficulty].name,4,129,13,"#ff6fb5");
  if(g.phase!=="bonus"){for(let i=0;i<Math.min(6,g.manlives);i++)spr(atlas.ship,8+i*12,141,1,.6);text("SHIP LIVES",4,151,11,"#9488ad");
}
  if(g.phase==="bonus"){text("TIME",306,14,14,"#9488ad","right");text(String(s.timer),306,30,26,s.timer<=10?"#ff5468":"#ffffff","right");text("BONUS",306,48,14,"#9488ad","right");text(fmt(g.bonusPts),306,60,18,"#ffd93a","right")}
  else{const R=[["STAGE",g.stage],["KILLS",g.kills],["LOST",g.deaths],["TIME",s.time]];R.forEach(([k,v],i)=>{text(k,306,12+i*22,14,"#9488ad","right");text(String(v),306,22+i*22,18,"#ffffff","right")})}
  if(!demo){text(s.rush?"RUSH READY":"RUSH...",306,96,12,s.rush?"#5fd456":"#6a74a0","right");if(!s.rush){ctx.fillStyle="#252536";ctx.fillRect(262*S,98*S,44*S,2*S);ctx.fillStyle="#5fd456";ctx.fillRect(262*S,98*S,44*S*(1-g.rushCD/300),2*S)}
   const rows=g.mode==="defense"?[[sel.on?"D-PAD":"← →","MOVE"],[sel.on?L("order"):"Z","FIRE (HOLD)"],[sel.on?L("rush"):"SPACE",`BOMB (${g.man?.bombs??0} LEFT)`],["","GRAB THE NUKE POD"],["","TO NUKE THE SWARM"],[sel.on?padLabel("KeyP"):"P","MENU"]]:sel.on?[["D-PAD","PICK ALIEN"],[`${L("order")}`,"ORDER"],[`${L("repeat")}`,"SAME AGAIN"],[`${L("snatch")}`,"SNATCH LOOT"],[`${L("rush")}`,"RUSH"],[`${L("left")}/${L("right")}`,"MOVE SWARM"],[`${L("scope")}`,`GROUP: ${SCOPES[sel.scope]}`],[padLabel("KeyP"),"MENU"]]
    :[["ARROWS","PICK ALIEN"],["Z","ORDER"],["C","SAME AGAIN"],["X","SNATCH LOOT"],["SPACE","RUSH"],["Q / E","MOVE SWARM"],["TAB",`GROUP: ${SCOPES[sel.scope]}`],["P","MENU"]];
   rows.forEach(([b,t],i)=>{const y=106+i*8.4;text(t,306,y,11,i===6?"#ffd93a":"#d6dbe8","right");text(b,262,y,11,"#5ee7ff","left")});if(!sel.on)text("OR CLICK A GO ALIEN",306,106+8*8.4,9,"#9488ad","right")}
  // hero mode: bomb stock, bottom-left of the playfield where the eye already is
  if(g.mode==="defense"&&g.phase!=="bonus"){const nb=g.man?.bombs??0,fl=bombFlash>0;if(bombFlash>0)bombFlash--;
   ctx.fillStyle="rgba(5,4,14,.6)";ctx.fillRect(59*S,151*S,62*S,17*S);text("BOMBS",61,157,11,nb?fl&&tick%4<2?"#ffffff":"#ffd93a":"#ff5468");
   for(let i=0;i<5;i++)spr(atlas.bombLoot[0],65+i*11.5,163,i<nb?1:.2,i<nb?(fl&&i===nb-1?1+.3*Math.sin(tick*.8):.8):.6)}
  // objective bar
  if(g.phase!=="bonus"){const hp=Math.max(0,g.health)/100;ctx.fillStyle="#05040a";ctx.fillRect(59*S,170*S,192*S,4*S);ctx.fillStyle=g.phase==="boss"?"#ff5468":"#5fd456";ctx.fillRect(59*S+2,170*S+2,(192*S-4)*hp,4*S-4);
   text(g.mode==="defense"?`PROTECT ${g.L.name} // ${s.quota} ALIENS LEFT`:g.phase==="boss"?(g.L.final?"THE ADMIRAL":"BOSS"):`${g.L.name}${g.ships.length?`  +${g.ships.length} SHIPS`:""}`,155,168,12,"#d6cbe8","center")}
  if(idle&&!demo&&tick%60<45)text("IDLE MODE // PRESS ANY BUTTON",155,16,12,"#ffd93a","center");
  if(!demo&&!sel.on)for(const a of ARROWS)spr(a.dir<0?atlas.arrowL:atlas.arrowR,a.x,a.y,arrowPtr?.dir===a.dir||(a.dir<0?shiftHold.l:shiftHold.r)?1:.55);
  if(sel.pick&&!demo)text("PICK A PARTNER",155,16,16,"#ff6fb5","center");
  if(!demo){const sp=Object.entries(g.sup).find(([k,v])=>v>0)||(g.hole?["hole",g.hole.t]:null);if(sp){const nm=SUPER_NAMES[sp[0]]||sp[0];text(nm,155,8,12,`hsl(${tick*6%360},90%,70%)`,"center")}
   if(g.calm>0&&tick%40<30)text(`CEASEFIRE ${Math.ceil(g.calm/30)} // REBUILD THE SWARM`,155,162,11,"#5fd456","center")}
  if(dial){const l=dial.lines[dial.i],c=l.startsWith("YOU")?"#5fd456":/^(BOSS|ADMIRAL)/.test(l)?"#ff5468":"#ffffff",i=l.indexOf(": "),who=l.slice(0,i),say=l.slice(i+2);
   ctx.fillStyle="rgba(5,4,10,.92)";ctx.fillRect(2*S,156*S,53*S,22*S);ctx.strokeStyle=c;ctx.lineWidth=3;ctx.strokeRect(2*S+1,156*S+1,53*S-2,22*S-2);
   text(who,4,162,11,c,"left",null);const words=say.split(" ");let line="",y=169;ctx.font='7px "Press Start 2P",monospace';
   for(const w of words){const t=line?line+" "+w:w;if(ctx.measureText(t).width>49*S&&line){text(line,4,y,11,"#ffffff","left",null);line=w;y+=7}else line=t}if(line&&y<180)text(line,4,y,11,"#ffffff","left",null)}}
 function draw(){tick++;
  if(titleOn&&demo){title.draw(title.phase!=="intro");return}
  const sx=shake>0&&!reduceMotion?(Math.random()-.5)*shake*S:0,sy=shake>0&&!reduceMotion?(Math.random()-.5)*shake*S:0;shake=Math.max(0,shake-.5);
  ctx.save();ctx.translate(sx,sy);drawWorld();ctx.restore();drawHud();if(!demo){drawOrderMenu();drawTutorial()}
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
  {label:"CONTROLS",hint:"REMAP YOUR CONTROLLER",act:()=>menu.push(controls())},
  {label:"BACK",act:()=>menu.pop()}]}}
 function vib(){const P=window.ArcadeGridzPad;if(P){P.update({vibration:!P.settings().vibration});P.rumble?.(120,.5)}}
 function controls(){return controlsScreen(binds,{menu,extra:[{label:"KEYBOARD + MOUSE",act:()=>menu.push(kbScreen())}]})}
 function kbScreen(){return{title:"KEYBOARD + MOUSE",body:`<b>MOUSE</b> Click an alien that says GO, then a command icon. Click loot, missiles, satellites, or the ship (rush).<br><b>ARROWS</b> select an alien // <b>Z / ENTER</b> order menu // <b>C</b> repeat order // <b>1-5</b> attack, dive, bugs, tentacles, random<br><b>X</b> snatch loot // <b>SPACE</b> rush // <b>Q</b> one / row / all // <b>TAB</b> next idle alien // <b>SHIFT + ← →</b> shift the swarm<br><b>P / ESC</b> pause menu // <b>M</b> sound`,items:[{label:"BACK",act:()=>menu.pop()}]}}
 function howTo(){return{title:"HOW TO PLAY",body:()=>`You are the <b>swarm</b>. Wreck each planet, beat its defender, and don't let the humans wipe you out. Ten levels from Earth to Planet X, with bonus stages between.<br>
  <b>SELECT</b> The d-pad hops between your aliens. ${L("scope")} switches between ordering ONE alien, its whole ROW, or ALL of them. ${L("left")} / ${L("right")} move the whole swarm left and right.<br>
  <b>ORDER</b> ${L("order")} opens the order menu: ATTACK shoots, DIVE divebombs, BUGS and TENTACLES unlock with power-ups. Aliens keep following their order. ${L("repeat")} gives the same order again.<br>
  <b>LOOT</b> ${L("snatch")} snatches the nearest power-up: green upgrades the swarm, red ones are the ship's (block them). <b>RUSH</b> ${L("rush")} sends everyone at the ship (it needs to recharge).<br>
  <b>CLASSES</b> Grunts attack. Zappers chain-lightning and EMP ships. Tanks shield, taunt and ram. Healers mend, hatch eggs and make a sanctuary.<br><b>SUPERS</b> Rainbow stars set off FRENZY, MEGA LASERS, HIVE STORM, GOO RAIN, CLONE BURST or a BLACK HOLE.<br><b>HERO MODE</b> Fly the human ship from Earth out to Planet X: move, hold fire, smart bombs. Grab the nuke pod to nuke the swarm.<br><b>NUKES</b> Shoot the NUKE pod before the ship grabs it. If it's armed, kill the ship. If it blows, a ceasefire lets you rebuild.<br><b>FLEETS</b> Later planets bring carriers, repair ships and shield frigates. Shields stop shots, not dives.<br>Hold <b>${padLabel("KeyM")} + ${padLabel("KeyP")}</b> to leave for the ArcadeGridz launcher.`,items:[{label:"BACK",act:()=>menu.pop()}]}}
 function confirm(title,yes,sub="PROGRESS SINCE THE LEVEL STARTED WILL BE LOST"){menu.push({title,sub,items:[{label:"NO, KEEP PLAYING",act:()=>menu.pop()},{label:"YES",act:yes}]})}
 function pauseScreen(){return{title:"PAUSED",sub:()=>`LEVEL ${g.level+1}: ${g.L.name} // ${DIFF[g.difficulty].name} // SCORE ${fmt(g.score)}`,back:resume,start:resume,items:[
  {label:"RESUME",act:resume},
  {label:"IDLE MODE",value:()=>idle?"ON":"OFF",hint:"THE GAME PLAYS ITSELF (SCORES COUNT AS ASSISTED)",act:()=>{setIdle(!idle);resume()},left:()=>setIdle(!idle),right:()=>setIdle(!idle)},
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
 function saveRun(level){if(!g||g.mode==="defense")return;run={difficulty:g.difficulty,level,score:g.score,me:{...g.me}};store.set(RUN_KEY,run)}
 function newRun(d){opts.difficulty=d;store.set("difficulty",d);run={difficulty:d,level:0,score:0,me:null};store.set(RUN_KEY,run);startLevel(0)}
 function continueRun(){const r=savedRun();if(!r)return;run={...r};startLevel(r.level)}
 function nextLevel(){const next=g.level+1;saveRun(next);startLevel(next)}
 function attract(){g=createGame({difficulty:1,level:[0,1,4,6][Math.floor(Math.random()*4)],seed:(Math.random()*2**32)>>>0,assist:true});demo=true}
 function demoInput(){const inp={};if(g.frame%20===0&&idleAliens(g).length)inp.command={cmd:[CMD.ATTACK,CMD.ATTACK,CMD.DIVE][g.frame/20%3]};if(g.frame%12===0)inp.snatch=true;return inp}
 function showMenu(){clearTimeout(endT);if(!demo){attract();audio.song("menu");audio.hyper?.(false);publish(true,true)}paused=false;focusPaused=false;dial=null;sel.menu=null;sel.pick=null;resetHolds();menu.close();titleOn=true;title.openList("main");title.setPhase("menu")}
 function startLevel(level){clearTimeout(endT);audio.unlock();const L0=LEVELS[level];
  g=createGame({difficulty:run.difficulty,level,seed:(Math.random()*2**32)>>>0,assist:opts.assist,carry:run.mode==="defense"?null:{me:run.me,score:run.score},mode:run.mode||"invade"});if(run.mode==="defense"&&run.score)g.score=run.score;idle=false;fireHeld=false;cut={t0:Date.now()};
  titleOn=false;demo=false;paused=false;focusPaused=false;submitted=false;result=null;dial=null;particles=[];pops=[];fx=[];taps=[];inpQ={};resetHolds();menu.close();
  sel.anchor=0;sel.scope=0;sel.menu=null;sel.pick=null;sel.ax=CX;sel.ay=20;
  audio.song(SONG[level]||"earth");audio.hyper?.(false);audio.music(true);
  publish(true);canvas.focus?.({preventScroll:true})}
 function setIdle(v){if(demo||!g)return;idle=v;if(v){run.assisted=true;banner("IDLE MODE","PRESS ANY BUTTON TO TAKE OVER")}else banner("YOU'RE IN CONTROL","");publish(true)}
 function defBot(){const m=g.man,inp={fire:true};if(!m)return inp;const t=g.pod||g.aliens.filter(a=>a.y>0).sort((a,b)=>Math.abs(a.x-m.x)-Math.abs(b.x-m.x))[0];
  const danger=[...g.shots,...g.globs].some(s=>Math.abs(s.x-m.x)<8&&s.y<m.y&&s.y>m.y-40);inp.move=danger?(m.x<CX?1:-1):t?Math.sign(t.x-m.x)*(Math.abs(t.x-m.x)>3?1:0):0;if(g.aliens.length>28&&m.bombs>0&&g.frame%240===0)inp.bomb=true;return inp}
 const heroNext=i=>{for(let k=i-1;k>=0;k--)if(LEVELS[k].kind!=="bonus")return k;return -1};
 function startHero(d){opts.difficulty=d;store.set("difficulty",d);const first=LEVELS.length-1-[...LEVELS].reverse().findIndex(L=>L.kind!=="bonus");run={mode:"defense",difficulty:d,level:first,score:0,me:null};startLevel(first)}
 const BOSSN=["","CRUISER","METEOR CANNON","STORM LANCER","THE ADMIRAL"],SKILL=["ROOKIE","CADET","VETERAN","ACE","LEGEND"];
 function endCut(){cut=null;last=performance.now();acc=0;banner(g.mode==="defense"?`DEFEND ${g.L.name}`:`LEVEL ${g.level+1}: ${g.L.name}`,g.mode==="defense"?"HOLD A TO FIRE // B = SMART BOMB":g.L.kind==="bonus"?`BONUS STAGE // ${g.L.brief}`:g.L.brief);say(DIALOGUE[g.L.id])}
 function drawCut(){const t=(Date.now()-cut.t0)/1000,L0=g.L,e=k=>1-Math.pow(1-Math.min(1,Math.max(0,k)),3);if(t>4.6){endCut();return}
  ctx.drawImage(skyOf(L0.art),0,0,CW,CH);for(const[i,img]of stars.entries()){const off=(t*(i?60:30))%CH;ctx.globalAlpha=i?.6:1;ctx.drawImage(img,0,off-CH,CW,CH);ctx.drawImage(img,0,off,CW,CH)}ctx.globalAlpha=1;
  const z=.35+e(t/2.6)*.9,pw=CW*z,ph=pw*140/196;ctx.drawImage(planetOf(L0.art),(CW-pw)/2,CH-ph*.55*e(t/1.6),pw,ph);
  ctx.fillStyle="rgba(3,2,12,.55)";ctx.fillRect(0,CH*.05,CW,CH*.25);ctx.textAlign="center";
  ctx.font='16px "Press Start 2P",monospace';ctx.fillStyle="#9fe9ff";ctx.fillText(g.mode==="defense"?"HERO MODE":L0.kind==="bonus"?"BONUS STAGE":`LEVEL ${g.level+1} OF ${LEVELS.length}`,CW/2,CH*.1+8);
  const sc=1+(1-e(t/.5))*1.6;ctx.save();ctx.translate(CW/2,CH*.2);ctx.scale(sc,sc);ctx.font='64px Bungee,sans-serif';ctx.fillStyle="#05040a";ctx.fillText(L0.name,4,22);ctx.fillStyle=`hsl(${185+Math.sin(t*3)*20},95%,65%)`;ctx.shadowColor="#5ee7ff";ctx.shadowBlur=24;ctx.fillText(L0.name,0,18);ctx.restore();ctx.shadowBlur=0;
  // the line-up of what's defending it (or what's coming, in defense mode)
  let roster;if(g.mode==="defense"){const u=UNIQUE[L0.id];roster=[[atlas.alien[0][(Date.now()>>8)&1],`${g.quota} ALIENS`],[atlas.tank[0],"TANKS"],[atlas.healer[0],"HEALERS"],...(u?[[atlas[u][0],KIND_NAMES[u]+"S"]]:[])]}
  else if(L0.kind==="bonus")roster=[[L0.bonus==="sun"?atlas.orb[0]:L0.bonus==="pluto"?atlas.ice[0]:atlas.ufo,L0.bonus==="sun"?"ENERGY ORBS":L0.bonus==="pluto"?"ICE":"UFO WAVES"]];
  else roster=[[atlas.ship,`${SKILL[g.skill]} PILOT`],...L0.fleet.map(k=>[atlas[k],k.toUpperCase()]),[atlas.boss[L0.boss-1],BOSSN[L0.boss]]];
  const n=roster.length,gap=Math.min(190,CW*.8/n);roster.forEach(([img,lab],i)=>{const k=e((t-.7-i*.22)/.5);if(k<=0)return;const x=CW/2+(i-(n-1)/2)*gap,y=CH*.64+(1-k)*CH*.5,sz=img.width>20?2.4:3.4;
   ctx.globalCompositeOperation="lighter";ctx.globalAlpha=.6;ctx.drawImage(glow(i===n-1&&L0.kind!=="bonus"?"#ff5468":"#5ee7ff"),x-60,y-60,120,120);ctx.globalAlpha=1;ctx.globalCompositeOperation="source-over";
   ctx.drawImage(img,x-img.width*sz/2,y-img.height*sz/2,img.width*sz,img.height*sz);ctx.font='11px "Press Start 2P",monospace';ctx.fillStyle="#05040a";ctx.fillText(lab,x+2,y+62);ctx.fillStyle=i===n-1&&L0.kind!=="bonus"&&g.mode!=="defense"?"#ff5468":"#ffffff";ctx.fillText(lab,x,y+60)});
  ctx.font='12px "Press Start 2P",monospace';ctx.fillStyle="#c8ff9a";ctx.fillText(g.mode==="defense"?"PROTECT THE PLANET":L0.brief.toUpperCase(),CW/2,CH*.9);
  if(UNIQUE[L0.id]&&L0.kind!=="bonus"){ctx.fillStyle="#ffd93a";ctx.font='10px "Press Start 2P",monospace';ctx.fillText(g.mode==="defense"?`ONLY HERE: ${KIND_NAMES[UNIQUE[L0.id]]}S`:`NATIVE SPECIES JOINS YOUR SWARM: ${KIND_NAMES[UNIQUE[L0.id]]}`,CW/2,CH*.85)}
  if(Math.floor(t*2)%2)ctx.fillText("PRESS ANY BUTTON",CW/2,CH*.96)}
 function postScore(){window.dispatchEvent(new CustomEvent("arcadegridz:score",{detail:{gameId:GAME_ID,score:g.score,mode:run?.assisted?"assisted":"solo"}}));let isBest=false;if(g.score>store.get(`best.${g.difficulty}`,-1)){store.set(`best.${g.difficulty}`,g.score);isBest=true}return isBest}
 function finish(){if(submitted||demo)return;submitted=true;audio.music(false);audio.hyper?.(false);resetHolds();sel.menu=null;sel.pick=null;idle=false;
  if(g.mode==="defense"){const won=g.state==="clear";if(!won)result={isBest:postScore()};banner(won?`${g.L.name} SAVED!`:`${g.L.name} HAS FALLEN`,won?"THE SWARM IS BROKEN":"THE HUMANS LOSE THIS ONE");
   endT=setTimeout(()=>{if(destroyed)return;const nx=heroNext(g.level),left=LEVELS.filter((L,i)=>i<g.level&&L.kind!=="bonus").length;if(won&&nx<0){result={isBest:postScore()}}menu.open({title:won?(nx<0?"THE SOLAR SYSTEM IS SAFE":"PLANET SAVED"):"PLANET LOST",sub:`HERO MODE // ${g.L.name} // ${DIFF[g.difficulty].name}`,body:`<div class="oa-result"><b>SCORE ${fmt(g.score)}</b><span>${g.deaths} ALIENS DESTROYED${won&&nx>=0?` // ${left} PLANET${left===1?"":"S"} TO GO`:""}</span>${won&&nx<0?"<small>Every planet held. The swarm retreats into deep space.</small>":""}</div>`,
    items:[...(won&&nx>=0?[{label:`NEXT: DEFEND ${LEVELS[nx].name}`,act:()=>{run={...run,level:nx,score:g.score};startLevel(nx)}}]:[]),...(!won?[{label:"RETRY",hint:`DEFEND ${g.L.name} AGAIN`,act:()=>startLevel(g.level)}]:[]),...(won&&nx<0?[{label:"PLAY AGAIN",act:()=>startHero(g.difficulty)}]:[]),{label:"TITLE",act:()=>{if(won&&nx>=0)postScore();showMenu()}},{label:"EXIT GAME",act:()=>{if(won&&nx>=0)postScore();exitGame()}}]})},2200);publish(true);return}
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
  if(cut&&!demo){drawCut();publish();return}
  if(!paused&&!focusPaused){if(hitstop>0){hitstop--}else{acc+=dt*(sel.menu&&opts.slowmo&&!demo?.35:1);let n=0;while(acc>=DT&&n<4){step(g,demo?demoInput():readInput());handleEvents();simFx();acc-=DT;n++;if(demo&&g.state!=="play")attract()}if(n===4)acc=0}}
  draw();publish()}
 function togglePause(){if(paused)resume();else openPause()}
 function toggleAudio(){audio.setMuted(!audio.muted);if(!audio.muted&&!demo&&!paused)audio.music(true);root.querySelector('[data-oa="audio"]')?.classList.toggle("active",audio.muted);publish(true)}
 function shutdown(){if(destroyed)return;destroyed=true;cancelAnimationFrame(raf);menu.close();removeEventListener("keydown",onKey);removeEventListener("keyup",onKey);removeEventListener("resize",onResize);ro.disconnect();clearTimeout(bannerTimer);clearTimeout(endT);audio.close();document.body.classList.remove("oa-playing");window.dispatchEvent(new CustomEvent("oota:state",{detail:{active:false}}))}
 root.querySelector(".oa-controls").addEventListener("click",e=>{const a=e.target.closest("button")?.dataset.oa;if(a==="pause")togglePause();else if(a==="audio")toggleAudio();else if(a==="menu"){if(!demo&&g.state==="play")openPause();else showMenu()}else if(a==="exit")exitGame()});
 const onResize=()=>fit(stage);addEventListener("resize",onResize);const ro=new ResizeObserver(onResize);ro.observe(root.querySelector(".oa-shell"));fit(stage);setTimeout(onResize,500);
 attract();audio.song("menu");raf=requestAnimationFrame(frame);
 return{destroy:shutdown,
  // the arcade shell shows no overlay of its own: losing focus opens this game's pause menu (like 2DISO)
  ownsFocusPause:true,
  pauseForFocus(){if(destroyed)return;resetHolds();if(!demo&&g.state==="play")openPause();audio.music(false);audio.suspend();publish(true)},
  resumeFromFocus(){if(destroyed)return;last=performance.now();acc=0;audio.resume();if(!demo&&!paused&&g.state==="play")audio.music(true);publish(true)},
  control(action){if(action==="idle"){if(paused)resume();setIdle(!idle);return}if(action==="pause")togglePause();else if(action==="audio"||action==="sound")toggleAudio();else if(action==="menu"){if(!demo&&g.state==="play")openPause();else showMenu()}else if(action==="restart"||action==="start"){if(run)startLevel(run.level);else newRun(opts.difficulty)}else if(action==="exit")exitGame()},
  getResult:()=>({score:g.score,state:g.state,result:g.result}),
  // test hook: the live game object (scores from this game are always UNVERIFIED)
  _debug:{game:()=>g,startLevel,newRun,opts:()=>opts,sel:()=>sel,selIds,menu:()=>menu,binds,run:()=>run}};
}
