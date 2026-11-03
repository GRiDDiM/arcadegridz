// OFFENSE OF THE ALIENS v2 rules: DOM-free simulation of the creator's 2014 reverse fixed shooter, rebuilt as a
// campaign. You command the alien swarm at the top; the human ship (and later a whole fleet) fights back.
// One step = one original 30 fps frame. World units match the original 310x180 screen at zoom 1; as the swarm
// grows the camera zooms out (to ZMIN) and the field widens/deepens around the top centre.
export const W=310,H=180,DT=1/30,CX=155,FX0=57,FX1=253,SLOT_Y=26,ZMIN=.5,ZBASE=.92;
export const CMD={RANDOM:0,BUGS:1,ATTACK:2,DIVE:3,TENTACLES:4,ZAP:5,EMP:6,SHIELD:7,RAM:8,TAUNT:9,MEND:10,HATCH:11,SANCT:12};
export const CMD_NAMES=["RANDOM","BUGS","ATTACK","DIVE","TENTACLES","ZAP","EMP","SHIELD","RAM","TAUNT","MEND","HATCH","SANCTUARY"];
// alien classes: each has its own ring of orders (ring slots: top, left, bottom-left, bottom-right, right)
export const CLASSES={grunt:{ring:[0,1,2,3,4],armor:0},zapper:{ring:[6,null,5,3,null],armor:0},tank:{ring:[9,7,8,null,null],armor:3},healer:{ring:[12,10,null,null,11],armor:0}};
export const canDo=(a,c)=>(CLASSES[a.kind]||CLASSES.grunt).ring.includes(c);
export const SUPERS=["frenzy","laser","hive","goo","clones","hole","nova","mother","meteors"];
export const SUPER_NAMES={frenzy:"FRENZY",laser:"MEGA LASERS",hive:"HIVE STORM",goo:"GOO RAIN",clones:"CLONE BURST",hole:"BLACK HOLE",nova:"SUPERNOVA",mother:"MOTHERSHIP BEAM",meteors:"METEOR STORM"};
// command icon offsets around the alien (original: random on top, bugs left, attack/dive below, tentacles right)
export const CMD_POS=[[0,-17],[-18,0],[-9,15],[9,15],[18,0]];
export const ME_BUFFS=["speed","spit","bugs","spawn","armor","tentacles"],MAN_BUFFS=["speed","lasers","missiles","plazma","shields","option"];
export const DIFF=[null,
 {name:"EASY",planetdmg:1,bossdmg:1,goodloot:1,badloot:10,badfire:90,goodspeed:1,badspeed:.1,stopper:0,stages:false,ai:-1,ice:10},
 {name:"NORMAL",planetdmg:.5,bossdmg:1,goodloot:2,badloot:4,badfire:70,goodspeed:.5,badspeed:.5,stopper:0,stages:true,ai:0,ice:12},
 {name:"HARD",planetdmg:.5,bossdmg:.5,goodloot:2,badloot:2,badfire:45,goodspeed:.25,badspeed:1,stopper:1,stages:true,ai:1,ice:15}];
// the campaign: planet levels (wreck the planet, beat its boss) and bonus stages
// the campaign flies in from the edge of the solar system: Earth is last and the most heavily defended
export const LEVELS=[
 {id:"planetx",name:"PLANET X",kind:"planet",art:7,rise:.03,boss:1,ai:0,hp:2,fleet:[],brief:"A lonely human outpost. Wreck it."},
 {id:"neptune",name:"NEPTUNE",kind:"planet",art:6,rise:.04,boss:3,ai:1,hp:2.2,fleet:["fighter"],hazard:"storm",brief:"Storms push the swarm around. A wingman joins the ship."},
 {id:"nibiru",name:"NIBIRU",kind:"bonus",bonus:"nibiru",art:10,time:40,brief:"Pop the UFO waves. Quick chains multiply."},
 {id:"saturn",name:"SATURN",kind:"planet",art:5,rise:.05,boss:2,ai:1,hp:2.4,fleet:["carrier","fighter"],hazard:"rings",brief:"The carrier keeps launching fighters. Sink it."},
 {id:"jupiter",name:"JUPITER",kind:"planet",art:3,rise:.06,boss:3,ai:2,hp:2.6,fleet:["interceptor","fighter"],hazard:"moons",brief:"Interceptors hunt your divers. Moons shield the ship."},
 {id:"pluto",name:"PLUTO",kind:"bonus",bonus:"pluto",art:9,time:40,brief:"Smash all the ice off Pluto before time runs out."},
 {id:"mars",name:"MARS",kind:"planet",art:2,rise:.06,boss:2,ai:2,hp:2.8,fleet:["repair","fighter","fighter"],brief:"Repair ships keep the fleet flying. Take them out first."},
 {id:"venus",name:"VENUS",kind:"planet",art:4,rise:.07,boss:1,ai:2,hp:2.8,fleet:["frigate","interceptor","fighter"],hazard:"acid",brief:"Acid clouds. Shield frigates block shots, not dives."},
 {id:"sun",name:"THE SUN",kind:"bonus",bonus:"sun",art:8,time:45,brief:"Shift the swarm away from the solar flares. Snatch the energy orbs."},
 {id:"earth",name:"EARTH",kind:"planet",art:1,rise:.08,boss:4,ai:3,hp:3.4,fleet:["carrier","repair","frigate","interceptor","fighter"],final:true,brief:"Humanity's home, defended by everything it has left."}];
// hero / fleet pilot skill (0 rookie .. 4 ace)
const AI=[{replan:22,horizon:8,jitter:14,dodge:.35,aim:10,greed:.3,roll:0},{replan:16,horizon:14,jitter:10,dodge:.55,aim:8,greed:.5,roll:0},
 {replan:11,horizon:20,jitter:6,dodge:.75,aim:6,greed:.8,roll:0},{replan:7,horizon:26,jitter:3,dodge:.88,aim:5,greed:1,roll:1},{replan:4,horizon:32,jitter:1,dodge:.96,aim:4,greed:1,roll:1}];
const SHIP={fighter:{hp:1,sp:2.2,fire:70,dy:-14},interceptor:{hp:2,sp:3,fire:40,dy:-20},carrier:{hp:8,sp:.6,fire:0,dy:8},repair:{hp:3,sp:1.4,fire:0,dy:4},frigate:{hp:5,sp:1,fire:90,dy:-6}};
export function rng(seed=1){let a=seed>>>0||1;return()=>{a=a+0x6D2B79F5|0;let t=Math.imul(a^a>>>15,1|a);t=t+Math.imul(t^t>>>7,61|t)^t;return((t^t>>>14)>>>0)/4294967296}}
const ev=(g,e)=>g.events.push(e),sfx=(g,n)=>ev(g,{type:"sfx",name:n});
const d2=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y);
const dice=(g,n)=>n<=1||g.rand()<1/n;
const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
function toward(o,tx,ty,sp){const dx=tx-o.x,dy=ty-o.y,d=Math.hypot(dx,dy);if(d<=sp){o.x=tx;o.y=ty;return true}o.x+=dx/d*sp;o.y+=dy/d*sp;return false}
function aim(o,tx,ty,sp){const dx=tx-o.x,dy=ty-o.y,d=Math.hypot(dx,dy)||1;o.vx=dx/d*sp;o.vy=dy/d*sp}
// view geometry (depends on zoom)
export const fx0=g=>CX-98/g.z,fx1=g=>CX+98/g.z,bot=g=>H/g.z,manY=g=>bot(g)-40,kamiY=g=>bot(g)*.53,surfaceY=g=>bot(g)-g.prise;
export function createGame({difficulty=2,level=0,seed=Date.now(),assist=false,carry=null,mode="invade"}={}){
 const D=DIFF[difficulty],L=LEVELS[level],r=rng(seed);
 const g={difficulty,level,D,L,assist,seed:seed>>>0,rand:r,frame:0,state:"play",phase:L.kind==="bonus"?"bonus":"planet",events:[],uid:1,z:ZBASE,march:{dir:1,last:0},
  spawner:{x:61,t:60},aliens:[],sup:{frenzy:0,laser:0,goo:0,mother:0,meteors:0},rocks:[],hole:null,pod:null,nuke:null,calm:0,nukeT:0,me:carry?.me?{...carry.me}:{speed:0,spit:0,bugs:0,spawn:0,armor:0,tentacles:0},man:null,manb:{speed:0,lasers:0,missiles:0,plazma:0,shields:0,option:0},manlives:3,
  skill:clamp(L.ai??0+0,0,4),ships:[],options:[],shots:[],manshots:[],missiles:[],plazmas:[],bugs:[],tentacles:[],globs:[],loots:[],sats:[],moons:[],bossShots:[],hazards:[],flares:[],ice:[],ufos:[],beams:[],
  prise:2,health:100,boss:null,menu:null,bigdive:0,rushCD:0,merge:null,timer:(L.time||0)*30,chain:0,chainT:0,bonusPts:0,
  score:carry?.score||0,startScore:carry?.score||0,combo:0,level0:level,lvl:0,stage:0,kills:0,deaths:0,moonT:420,hazT:200,fleetT:1200,result:null};
 g.mode=mode;g.skill=clamp((L.ai||0)+D.ai,0,4);g.nukeT=nukeEvery(g);if(mode==="defense"){g.quota=26+level*5+difficulty*4;g.spawned=0;g.manlives=5;g.cmdT=60}
 if(g.phase==="bonus")setupBonus(g);
 else{spawnMan(g);g.man.y=bot(g)+70;g.man.inv=60;for(const k of L.fleet)spawnShip(g,k);for(let i=0;i<8;i++){const a=spawnAlien(g,CX-56+i*16);a.y=-20-i*6}g.spawner.x=CX+72}
 return g}
const nukeEvery=g=>Math.round(Math.max(1100,2600-g.level*160)*(g.difficulty===1?1.4:1));
function pickKind(g){const r=g.rand(),lv=g.level,z=lv>=1?.12:0,t=.1+lv*.008,h=.1;return r<z?"zapper":r<z+t?"tank":r<z+t+h?"healer":"grunt"}
function spawnAlien(g,mex,mey=SLOT_Y,kind=pickKind(g)){if(g.mode==="defense")g.spawned=(g.spawned||0)+1;const a={id:g.uid++,kind,x:mex,y:-32,mex,mey,species:1,cmd:-1,chosen:false,okT:0,dive:0,vx:0,vy:0,kami:false,armor:g.me.armor+CLASSES[kind].armor,
  t0:0,t2:0,t3:0,t4:0,t8:60,evolveT:300,sparkle:0,assistT:300,variant:g.rand()<1/3?1:0,hitMan:false};g.aliens.push(a);sfx(g,"warpin");return a}
const heroHp=g=>g.mode==="defense"?4:2+Math.floor(g.level/3);
function spawnMan(g){g.man={kind:"hero",x:CX,y:bot(g)+24,gox:CX,think:10,fireT:60,missT:15,plazT:20,shields:g.manb.shields,inv:24,rollCD:0,hp:heroHp(g),max:heroHp(g),bombs:3};sfx(g,"manwarp")}
function spawnShip(g,kind,x){const s=SHIP[kind];g.ships.push({id:g.uid++,kind,x:x??fx0(g)+20+g.rand()*(fx1(g)-fx0(g)-40),y:bot(g)+30,gox:CX,think:0,fireT:s.fire||0,hp:s.hp,max:s.hp,launchT:240,healT:120,dome:kind==="frigate"?6:0,domeT:0,inv:20,mine:[]})}
// ---------------- input ----------------
// inp: {tap:{x,y}, shift:-1|0|1, command:{cmd, ids?:[...], near?:x}, rush:true, snatch:true, merge:{src,dst}}
export function step(g,inp={}){g.events.length=0;g.frame++;if(g.state!=="play")return g;g.pin=inp;if(g.mode==="defense"&&inp.bomb)bomb(g);
 if(inp.shift)for(const a of g.aliens)a.mex=clamp(a.mex+inp.shift,fx0(g)+20,fx1(g)-20);
 if(inp.tap)tap(g,inp.tap.x,inp.tap.y);
 if(inp.command)orders(g,inp.command);
 if(inp.rush)rush(g);
 if(inp.snatch)snatch(g);
 if(inp.merge)mergePair(g,inp.merge.src,inp.merge.dst);
 if(g.phase==="bonus")tickBonus(g);else tick(g);return g}
const atSlot=a=>!a.kami&&a.dive===0&&Math.abs(a.x-a.mex)<1.5&&Math.abs(a.y-a.mey)<1.5;
export const commandable=a=>!a.kami&&!a.mergeInto&&a.okT<=0;
export function idleAliens(g){return g.aliens.filter(a=>!a.chosen&&a.okT<=0&&!a.kami&&a.dive===0&&!a.mergeInto)}
export const lockedCmd=(g,c)=>c===CMD.BUGS&&g.me.bugs<=0||c===CMD.TENTACLES&&g.me.tentacles<=0;
function orders(g,{cmd,ids,near}){if(lockedCmd(g,cmd))return ev(g,{type:"deny",why:"locked"});
 let list;if(ids)list=g.aliens.filter(a=>ids.includes(a.id)&&commandable(a));else{list=idleAliens(g).filter(a=>canDo(a,cmd));const nx=near??CX;list.sort((a,b)=>Math.abs(a.x-nx)-Math.abs(b.x-nx));list=list.slice(0,1)}
 list=list.filter(a=>cmd===CMD.RANDOM?a.kind==="grunt":canDo(a,cmd));if(!list.length)return ev(g,{type:"deny"});for(const a of list)giveCommand(g,a,cmd,list.length>1)}
function giveCommand(g,a,cmd,quiet){if(lockedCmd(g,cmd))return ev(g,{type:"deny",why:"locked"});
 if(cmd===CMD.RANDOM){cmd=1+Math.floor(g.rand()*4);if(lockedCmd(g,cmd))cmd=g.rand()<.5?CMD.ATTACK:CMD.DIVE}
 if(!canDo(a,cmd))return;if(a.dive===1||a.dive===3){a.dive=0;a.y=Math.min(a.y,-8)}a.chosen=false;a.cmd=cmd;a.dome=cmd===CMD.SHIELD?6:0;a.ram=false;a.okT=8;g.menu=null;if(!quiet)sfx(g,"ok");ev(g,{type:"command",id:a.id,cmd})}
function rush(g){if(g.rushCD>0||g.bigdive>0||g.phase==="bonus"&&g.L.bonus!=="pluto")return ev(g,{type:"deny",why:"rush"});
 g.bigdive=45;g.rushCD=300;for(const a of g.aliens)if(!a.kami&&!a.mergeInto){a.dive=2;a.hitMan=false}sfx(g,"dive");ev(g,{type:"bigdive"})}
function endRush(g){for(const a of g.aliens)if(a.dive===2){a.dive=0;a.vx=a.vy=0}}
// a tentacle from the nearest alien snatches the nearest loot (good = power up, bad = blocked) or orb / UFO in bonus stages
function snatch(g){const pool=[...g.loots.filter(l=>!l.done),...g.ufos.filter(u=>!u.dead)];let best=null,bd=1e9,src=null;
 for(const l of pool)for(const a of g.aliens){if(a.kami)continue;const d=d2(a,l);if(d<74&&d<bd){bd=d;best=l;src=a}}
 if(!best)return ev(g,{type:"deny",why:"snatch"});ev(g,{type:"snatch",x0:src.x,y0:src.y,x1:best.x,y1:best.y});
 if(best.ufo)return popUfo(g,best);grabLoot(g,best)}
function grabLoot(g,l){l.done=1;l.t=40;g.combo++;if(l.super){g.score+=100;superPower(g,l);comboBonus(g,l);return}if(l.nuke)return;if(l.orb){g.score+=100;g.bonusPts+=100;sfx(g,"powerup");pop(g,l.x,l.y,"+100","#ffd93a");return}
 if(l.good){g.lvl++;g.score+=25;buffSwarm(g);sfx(g,"powerup");pop(g,l.x,l.y,"POWER UP!","#5fd456")}else{g.score+=50;sfx(g,"blocked");pop(g,l.x,l.y,"BLOCKED!","#5ee7ff")}comboBonus(g,l)}
function mergePair(g,srcId,dstId){const src=g.aliens.find(a=>a.id===srcId),dst=g.aliens.find(a=>a.id===dstId);
 if(!src||!dst||src===dst||src.sparkle<=0||src.species!==1||dst.species!==1||dst.kami||src.kind!=="grunt"||dst.kind!=="grunt")return ev(g,{type:"deny",why:"merge"});dst.mergeInto=src.id;dst.dive=0;g.merge=null;sfx(g,"morph")}
function tap(g,x,y){const p={x,y};
 if(g.menu){const a=g.aliens.find(a=>a.id===g.menu.id);if(a)for(let i=0;i<5;i++){const[ox,oy]=CMD_POS[i],c=CLASSES[a.kind].ring[i];if(c==null)continue;if(Math.hypot(x-(a.x+ox),y-(a.y+oy))<8){if(lockedCmd(g,c))continue;giveCommand(g,a,c);return}}g.menu=null}
 for(const l of g.loots)if(!l.done&&d2(l,p)<12){grabLoot(g,l);return}
 for(const u of g.ufos)if(!u.dead&&d2(u,p)<11){popUfo(g,u);return}
 for(const m of g.missiles)if(d2(m,p)<9){m.dead=true;g.score+=10;sfx(g,"pop");pop(g,m.x,m.y,"+10","#ffd93a");return}
 const hit=g.aliens.filter(a=>d2(a,p)<(a.species===2?13:10)).sort((a,b)=>d2(a,p)-d2(b,p))[0];
 if(hit){
  if(hit.sparkle>0&&hit.species===1&&!g.merge){g.merge={id:hit.id,t:150};sfx(g,"pick");pop(g,hit.x,hit.y-10,"PICK A PARTNER","#ff6fb5");return}
  if(g.merge&&hit.id!==g.merge.id&&hit.species===1){mergePair(g,g.merge.id,hit.id);return}
  if(commandable(hit)&&!hit.dive){g.menu={id:hit.id};sfx(g,"pick");return}}
 for(const s of g.sats)if(d2(s,p)<10){for(const a of g.aliens)if(!a.kami){a.dive=3;a.tx=s.x;a.ty=s.y;a.diveT=60}sfx(g,"dive");return}
 if(g.man&&d2(g.man,p)<12||g.boss&&d2(g.boss,p)<18)rush(g)}
function pop(g,x,y,txt,c){ev(g,{type:"pop",x,y,txt,c})}
function comboBonus(g,o){if(g.combo>1){const b=g.combo*Math.max(1,g.lvl);g.score+=b;pop(g,o.x,o.y+12,`COMBO! ${b}`,"#ffd93a")}}
function buffSwarm(g){const k=ME_BUFFS[Math.floor(g.rand()*6)];g.me[k]+=k==="speed"?g.D.goodspeed:1;if(k==="armor")for(const a of g.aliens)a.armor+=1;ev(g,{type:"buff",side:"me",kind:k})}
function buffMan(g,n=1){for(let i=0;i<n;i++){const k=MAN_BUFFS[Math.floor(g.rand()*6)];g.manb[k]+=k==="speed"?g.D.badspeed:1;if(k==="shields"&&g.man)g.man.shields++;if(k==="option"&&g.options.length<2)g.options.push({x:g.man?.x??CX,y:manY(g),fireT:90});ev(g,{type:"buff",side:"man",kind:k})}}
// ---------------- the pilots: plan a lane every few frames, dodge what is coming, line up on the scariest alien ----------------
function threats(g){const T=[];
 for(const s of g.shots)T.push({x:s.x,y:s.y,vx:0,vy:s.vy,r:3});
 for(const s of g.globs)T.push({x:s.x,y:s.y,vx:0,vy:3,r:6});
 for(const a of g.aliens)if(a.kami||a.dive===1)T.push({x:a.x,y:a.y,vx:a.vx,vy:a.vy,r:8});else if(a.dive===2)T.push({x:a.x,y:a.y,home:true,r:8});
 for(const b of g.bugs)T.push({x:b.x,y:b.y,home:true,r:5,slow:true});
 for(const t of g.tentacles)T.push({x:t.x,y:t.y,col:true,life:t.life,r:6});
 for(const h of g.hazards)if(h.kind==="acid")T.push({x:h.x,y:h.y,vx:h.vx,vy:0,r:h.r*.6});
 return T}
function danger(T,x,y,horizon){let d=0;for(const t of T){
  if(t.col){if(Math.abs(t.x-x)<t.r+7&&t.y<y)d+=1.2;continue}
  if(t.home){const dd=Math.hypot(t.x-x,t.y-y);if(dd<(t.slow?30:60))d+=t.slow?.4:1.4*(1-dd/60);continue}
  if(t.vy<=0)continue;const k=(y-t.y)/t.vy;if(k<0||k>horizon)continue;const px=t.x+(t.vx||0)*k;if(Math.abs(px-x)<t.r+6)d+=1.5-k/horizon}return d}
function priority(a){return(a.dive>0||a.kami?4:0)+(a.chosen&&a.cmd===CMD.ATTACK?3:0)+(a.species===2?2:0)+(a.sparkle>0||a.mergeInto?2:0)+1}
function pickTarget(g,u){const tn=g.aliens.find(a=>a.chosen&&a.cmd===CMD.TAUNT&&a.y>0);if(tn)return tn;let best=null,bs=-1;for(const a of g.aliens){if(a.y<0)continue;const s=priority(a)-Math.abs(a.x-u.x)/60;if(s>bs){bs=s;best=a}}return best}
function pilot(g,u,sp,skill,{hunts=false,greedy=true,lane=null}={}){const S=AI[clamp(skill,0,4)],T=threats(g),x0=fx0(g)+10,x1=fx1(g)-10;
 if(u.inv>0)u.inv--;
 if(u.stun>0){u.stun--;u.fireT=Math.max(u.fireT||0,8);return{aligned:false,tgt:null}}
 const now=danger(T,u.x,u.y,6);
 if(--u.think<=0||now>1.2&&g.rand()<S.dodge*.25){u.think=S.replan;
  const tgt=hunts?g.aliens.find(a=>a.dive>0||a.kami)||pickTarget(g,u):pickTarget(g,u);u.tgt=tgt?.id||0;
  const bad=greedy?(g.pod||g.loots.find(l=>!l.good&&!l.done&&!l.orb)):null;let bx=u.x,bs=1e9;
  for(let x=x0;x<=x1;x+=6){let s=danger(T,x,u.y,S.horizon)*(.3+S.dodge)+Math.abs(x-u.x)*.008;
   if(tgt&&Math.abs(x-tgt.x)<6)s-=1.1;if(bad&&Math.abs(x-bad.x)<10&&bad.y<u.y)s-=1.4*S.greed;if(lane!=null)s+=Math.abs(x-lane)*.012;
   if(s<bs){bs=s;bx=x}}
  u.gox=clamp(bx+(g.rand()-.5)*S.jitter,x0,x1)}
 // aces barrel-roll through shots they can't escape
 if(S.roll&&now>2&&u.rollCD<=0&&u.inv<=0&&u.kind==="hero"){u.inv=14;u.rollCD=240;sfx(g,"shield");ev(g,{type:"roll",x:u.x,y:u.y})}
 if(u.rollCD>0)u.rollCD--;
 u.x+=clamp(u.gox-u.x,-sp,sp);
 const tgt=g.aliens.find(a=>a.id===u.tgt);return{aligned:!!tgt&&Math.abs(tgt.x-u.x)<S.aim,tgt}}
// ---------------- planet levels ----------------
function tick(g){const f=g.frame,D=g.D,me=g.me,mb=g.manb,L=g.L;
 // camera: zoom out as the swarm grows (capped), ease back in when it shrinks
 const zt=clamp(ZBASE-(g.aliens.length-10)*.013,ZMIN,ZBASE);g.z+=(zt-g.z)*.02;
 march(g);
 if(g.rushCD>0)g.rushCD--;if(g.bigdive>0&&--g.bigdive===0)endRush(g);
 // the hand of god: formation spawner sweeping the top row
 if(--g.spawner.t<=0){const s=g.spawner;if(s.x>fx1(g)-24)s.x=fx0(g)+4;s.x+=16;if(g.aliens.length<56&&!(g.mode==="defense"&&g.spawned>=g.quota)&&!g.aliens.some(a=>Math.abs(a.mey-SLOT_Y)<10&&Math.abs(a.mex-s.x)<12))spawnAlien(g,s.x);
  s.t=g.calm>0&&g.aliens.length<18?5:f<300||g.aliens.length<8?14:Math.max(22,80-me.spawn*3);
  if(f>300&&!g.sats.length&&g.rand()<.05){const dir=g.rand()<.5?1:-1;g.sats.push({x:dir>0?fx0(g)+16:fx1(g)-16,y:bot(g)*.45,dir,ufo:g.rand()<.5})}}
 if(D.stages&&f%1800===0){const mins=f/1800;g.stage=mins+1;buffMan(g,1);ev(g,{type:"stage",stage:g.stage})}
 // planet rises / boss
 if(g.phase==="planet")g.prise=Math.min(32,g.prise+L.rise*.5);
 hazards(g);
 // aliens
 for(const a of g.aliens){
  if(a.mergeInto){const src=g.aliens.find(s=>s.id===a.mergeInto);if(!src){a.mergeInto=0}else if(toward(a,src.x,src.y,6)){a.dead=true;a.noLoot=true;src.species=2;src.armor=me.armor*2+1;src.sparkle=0;src.t8=60;g.score+=50;sfx(g,"morphok");ev(g,{type:"merged",x:src.x,y:src.y})}continue}
  if(a.okT>0&&--a.okT===0){a.chosen=true;a.t0=1;a.t2=1;a.t3=1;a.t4=Math.max(10,90-me.tentacles)}
  if(a.sparkle>0)a.sparkle--;
  if(--a.evolveT<=0){a.evolveT=300;if(a.species===1&&a.kind==="grunt"&&!a.sparkle&&g.rand()<.012&&!g.aliens.some(o=>o.sparkle>0)){a.sparkle=150;sfx(g,"morphbegin")}}
  if(g.assist&&!a.chosen&&a.okT<=0&&--a.assistT<=0){a.assistT=300;if(g.rand()<.5)giveCommand(g,a,CMD.ATTACK,true)}
  const spd=1+me.speed+(g.sup.frenzy>0?1.5:0),M=g.man||g.boss;
  if(a.kami){a.x+=a.vx;a.y+=a.vy;if(a.y>bot(g)+20||a.x<fx0(g)-20||a.x>fx1(g)+20){a.dead=true;a.noLoot=true}}
  else if(a.dive===2&&M){toward(a,M.x,M.y,4+me.speed);if(a.hitMan)a.dive=0}
  else if(a.dive===2){a.dive=0}
  else if(a.dive===3){toward(a,a.tx,a.ty,4);if(--a.diveT<=0)a.dive=0}
  else if(a.dive===1){a.x+=a.vx;a.y+=a.vy;if(a.y>bot(g)+8||a.x<fx0(g)-8||a.x>fx1(g)+8){a.dive=0;a.x=clamp(a.x,fx0(g),fx1(g));a.y=-16}}
  else{toward(a,a.mex,a.mey,spd);if(a.mey>kamiY(g)&&atSlot(a)&&M){a.kami=true;aim(a,M.x,M.y,3+me.speed);sfx(g,"kamikaze")}}
  if(a.chosen&&!a.kami){const tgt=nearestFoe(g,a),fr=g.sup.frenzy>0?3:1;
   if(a.cmd===CMD.ATTACK&&(a.t2-=fr)<=0){a.t2=Math.max(8,60-me.spit*3);const n=me.spit>=6?3:me.spit>=3?2:1;for(let k=0;k<n;k++)g.shots.push({x:a.x,y:a.y+6,vy:4,vx:n===1?0:(k-(n-1)/2)*.7});g.score+=1;sfx(g,"shoot")}
   if(a.cmd===CMD.ZAP&&(a.t2-=fr)<=0){a.t2=Math.max(28,80-me.spit*3);zap(g,a)}
   if(a.cmd===CMD.EMP&&--a.t3<=0){a.t3=240;const t=nearestFoe(g,a);if(t){t.stun=75;ev(g,{type:"emp",x:a.x,y:a.y,x1:t.x,y1:t.y});sfx(g,"shield")}}
   if(a.cmd===CMD.SHIELD&&g.frame%40===0&&a.dome<6)a.dome++;
   if(a.cmd===CMD.RAM&&a.dive===0&&--a.t0<=0&&tgt){a.t0=150;a.dive=1;a.ram=true;aim(a,tgt.x,tgt.y,1.7+me.speed*.4);sfx(g,"dive")}
   if(a.cmd===CMD.TAUNT&&g.frame%90===0&&a.armor<6)a.armor++;
   if(a.cmd===CMD.MEND&&--a.t4<=0){a.t4=90;for(const o of g.aliens)if(o!==a&&Math.hypot(o.x-a.x,o.y-a.y)<34&&o.armor<3+CLASSES[o.kind].armor)o.armor++;ev(g,{type:"mend",x:a.x,y:a.y})}
   if(a.cmd===CMD.HATCH&&--a.t3<=0){a.t3=300;if(g.aliens.length<56){const e=spawnAlien(g,clamp(a.mex+(g.rand()<.5?-8:8),fx0(g)+20,fx1(g)-20),a.mey,"grunt");e.x=a.x;e.y=a.y;ev(g,{type:"hatch",x:a.x,y:a.y})}}
   if(a.cmd===CMD.DIVE&&a.dive===0&&(a.t0-=fr)<=0&&tgt){a.t0=Math.max(24,90-me.speed*4);a.dive=1;aim(a,tgt.x,tgt.y,2+me.speed);g.score+=1;sfx(g,"dive")}
   if(a.cmd===CMD.BUGS&&--a.t3<=0){a.t3=Math.max(30,180-me.bugs*10);g.bugs.push({x:a.x,y:a.y,life:240});sfx(g,"bug");g.score+=1}
   if(a.cmd===CMD.TENTACLES&&--a.t4<=0){a.t4=Math.max(15,90-me.tentacles*4);g.tentacles.push({x:a.x-8+g.rand()*16,y:a.y,life:30,src:a.id,hit:new Set(),ph:g.rand()*6,tx:a.x});g.score+=1;sfx(g,"tentacle")}}
  if(a.species===2&&--a.t8<=0){a.t8=60;g.globs.push({x:a.x,y:a.y+8,hit:false})}
  if(g.sup.goo>0&&(g.frame+a.id)%18===0)g.globs.push({x:a.x,y:a.y+8,hit:false})}
 g.aliens=g.aliens.filter(a=>!a.dead);
 supers(g);nukes(g);
 if(g.merge&&--g.merge.t<=0)g.merge=null;
 if(g.menu&&!g.aliens.some(a=>a.id===g.menu.id))g.menu=null;
 // the human
 const m=g.man;
 if(m){if(m.y>manY(g))m.y=Math.max(manY(g),m.y-2);else m.y=manY(g);
  const P=g.mode==="defense"?player(g,m,g.pin||{}):pilot(g,m,2+mb.speed,g.skill);
  if(g.mode==="defense"){if(--m.fireT<=0&&(g.pin||{}).fire){m.fireT=Math.max(5,14-mb.lasers);const n=mb.lasers>=4?3:mb.lasers>=2?2:1;for(let k=0;k<n;k++)g.manshots.push({x:m.x+(k-(n-1)/2)*5,y:m.y-6,vy:-5});sfx(g,"manshoot")}}
  else if(g.calm<=0&&g.aliens.length&&--m.fireT<=0&&(P.aligned||m.fireT<-45)){m.fireT=Math.max(8,D.badfire-mb.lasers*4);g.manshots.push({x:m.x,y:m.y-6,vy:-4});sfx(g,"manshoot")}
  if(mb.missiles>0&&g.aliens.length&&--m.missT<=0){m.missT=Math.max(30,120-mb.missiles*6);g.missiles.push({x:m.x,y:m.y-6,life:300,tgt:P.tgt?.id||0});sfx(g,"missile")}
  if(mb.plazma>0&&g.aliens.length&&--m.plazT<=0){m.plazT=Math.max(30,120-mb.plazma*6);g.plazmas.push({x:m.x,y:m.y-6,life:150})}}
 for(const o of g.options){if(!m)break;toward(o,m.x+(o===g.options[0]?-14:14),manY(g)+4,2+mb.speed);if(--o.fireT<=0&&g.aliens.length){o.fireT=Math.max(20,90-mb.lasers*4);g.manshots.push({x:o.x,y:o.y-4,vy:-4})}}
 fleet(g);
 // the boss (planet destroyed)
 const B=g.boss;
 if(B){if(B.y>manY(g)-6)B.y-=1.2;const P=pilot(g,B,1.6+mb.speed*.5,g.skill,{greedy:false});
  if(g.aliens.length&&--B.fireT<=0&&(P.aligned||B.fireT<-30)){B.fireT=Math.max(12,56-mb.lasers*3);g.manshots.push({x:B.x-12,y:B.y-8,vy:-4},{x:B.x+12,y:B.y-8,vy:-4});sfx(g,"manshoot")}
  if(g.aliens.length&&--B.specialT<=0){B.specialT=40+Math.floor(g.rand()*30);
   if(B.type===2)g.bossShots.push({x:B.x,y:B.y-10,vx:2,vy:-3,zig:16,kind:"meteor"});
   if(B.type===3)g.bossShots.push({x:B.x,y:B.y-10,vx:4,vy:-2.5,zig:16,kind:"jup"});
   if(B.type===4){g.bossShots.push({x:B.x-10,y:B.y-10,vx:-2,vy:-3,zig:14,kind:"jup"},{x:B.x+10,y:B.y-10,vx:2,vy:-3,zig:14,kind:"jup"});if(g.ships.length<3&&g.rand()<.5)spawnShip(g,"fighter",B.x)}
   if(B.type>1)sfx(g,"bossshoot")}
  if(B.type===4){if(--B.domeT<=0){B.domeT=360;B.dome=90}if(B.dome>0)B.dome--}
  if(--B.missT<=0){B.missT=Math.max(30,120-mb.missiles*6);if(g.aliens.length)g.missiles.push({x:B.x,y:B.y-10,life:300,tgt:P.tgt?.id||0})}}
 projectiles(g);
 // objective
 if(g.mode!=="defense"&&g.phase==="planet"&&g.health<1){g.phase="boss";g.health=100;g.boss={kind:"boss",x:CX,y:bot(g)+40,gox:CX,think:0,fireT:90,specialT:60,missT:30,type:L.boss,inv:0,dome:0,domeT:200};
  for(const s of g.ships)if(!L.final){s.leaving=true}sfx(g,"planetboom");ev(g,{type:"planetDestroyed"})}
 else if(g.phase==="boss"&&g.health<1&&g.state==="play"){const B=g.boss;g.score+=2000*g.difficulty*(g.level+1);sfx(g,"win");ev(g,{type:"boss",x:B.x,y:B.y});
  g.state=L.final?"won":"clear";g.result="win";g.boss=null;ev(g,{type:"end",result:"win"})}
 if(g.mode==="defense"){alienCommander(g);
  if(g.state==="play"&&g.spawned>=g.quota&&g.aliens.length===0){g.state="clear";g.result="win";g.score+=1500*g.difficulty;sfx(g,"win");ev(g,{type:"end",result:"win"})}
  else if(g.state==="play"&&(g.health<1||g.manlives<=0&&!g.man)){g.state="over";g.result="lose";sfx(g,"over");ev(g,{type:"end",result:"lose"})}}
 else if(g.state==="play"&&g.aliens.length===0&&g.frame>60&&g.calm<=0){g.state="over";g.result="lose";sfx(g,"over");ev(g,{type:"end",result:"lose"})}}
// Galaxian / Invaders march: the formation drifts side to side and steps down a notch at the edges
function march(g){const m=g.march;let lo=1e9,hi=-1e9;for(const a of g.aliens)if(!a.kami&&!a.mergeInto){lo=Math.min(lo,a.mex);hi=Math.max(hi,a.mex)}
 if(lo>hi)return;const sp=.15+g.level*.008,L=fx0(g)+12,R=fx1(g)-12;if(hi-lo>R-L-8)return;
 if(m.dir>0&&hi+sp>R||m.dir<0&&lo-sp<L){m.dir=-m.dir;if(g.frame-m.last>360){m.last=g.frame;for(const a of g.aliens)if(!a.kami)a.mey+=5;ev(g,{type:"descend"})}}
 else for(const a of g.aliens)if(!a.kami&&!a.mergeInto)a.mex+=m.dir*sp}
const inSanct=(g,o)=>g.aliens.some(h=>h.chosen&&h.cmd===CMD.SANCT&&Math.hypot(h.x-o.x,h.y-o.y)<26);
// chain lightning: nearest human thing in reach, then up to two more jumps
function zap(g,a){const hit=new Set(),pts=[[a.x,a.y]];let from=a;
 for(let j=0;j<3;j++){let best=null,bd=j?60:120;const cand=[...g.ships.filter(s=>!s.dead&&!s.leaving),g.man,g.boss,...g.missiles,g.pod].filter(Boolean);
  for(const c of cand){if(hit.has(c))continue;const d=Math.hypot(c.x-from.x,c.y-from.y);if(d<bd){bd=d;best=c}}if(!best)break;hit.add(best);pts.push([best.x,best.y]);
  if(best===g.man)manHit(g);else if(best===g.boss){g.health-=1/(g.L.hp||1);g.score+=10}else if(best===g.pod)podHit(g);else if(g.missiles.includes(best))best.dead=true;else shipHit(g,best,1);from=best}
 if(pts.length>1){ev(g,{type:"zap",pts});sfx(g,"tentacle")}}
function superPower(g,l){const k=SUPERS[Math.floor(g.rand()*SUPERS.length)];ev(g,{type:"super",kind:k,x:l.x,y:l.y});sfx(g,"morphok");
 if(k==="frenzy")g.sup.frenzy=330;else if(k==="laser")g.sup.laser=270;else if(k==="goo")g.sup.goo=270;else if(k==="mother")g.sup.mother=240;else if(k==="meteors")g.sup.meteors=240;
 else if(k==="nova"){const HP=g.L.hp||1;ev(g,{type:"nova",x:CX,y:bot(g)*.5});for(const s of g.ships)shipHit(g,s,3);if(g.man){g.man.inv=0;g.man.shields=0;manHit(g);if(g.man){g.man.inv=0;manHit(g)}}if(g.boss)g.health-=8/HP;else if(g.phase==="planet")g.health-=5/HP;for(const k2 of["manshots","missiles","plazmas","bossShots"])g[k2]=[];g.pod&&podHit(g,9)}
 else if(k==="hive"){for(let i=0;i<24;i++){const a=g.aliens[Math.floor(g.rand()*g.aliens.length)];if(a)g.bugs.push({x:a.x,y:a.y,life:240})}}
 else if(k==="clones"){for(let i=0;i<6&&g.aliens.length<60;i++){const a=g.aliens[Math.floor(g.rand()*g.aliens.length)];if(!a)break;const c=spawnAlien(g,clamp(a.mex+(i%2?8:-8),fx0(g)+20,fx1(g)-20),a.mey,a.kind);c.x=a.x;c.y=a.y}}
 else if(k==="hole")g.hole={x:CX,y:bot(g)*.62,t:270}}
function supers(g){const S=g.sup,HP=g.L.hp||1;for(const k in S)if(S[k]>0)S[k]--;
 if(S.laser>0&&g.frame%8===0)for(const a of g.aliens)if(a.chosen&&a.cmd===CMD.ATTACK&&!a.kami){
  for(const s of g.ships)if(!s.dead&&Math.abs(s.x-a.x)<(s.kind==="carrier"?20:8)&&s.y>a.y)shipHit(g,s,1);
  if(g.man&&Math.abs(g.man.x-a.x)<8)manHit(g);if(g.boss&&Math.abs(g.boss.x-a.x)<16){g.health-=.4/HP;g.score+=5}else if(g.phase==="planet"){g.health-=.15/HP;g.score+=2}}
 if(S.mother>0){const bx=CX+Math.sin(g.frame*.03)*(fx1(g)-fx0(g))*.38;g.motherX=bx;if(g.frame%6===0){for(const s of g.ships)if(Math.abs(s.x-bx)<20)shipHit(g,s,1);if(g.man&&Math.abs(g.man.x-bx)<16)manHit(g);if(g.boss&&Math.abs(g.boss.x-bx)<26){g.health-=.6/HP;g.score+=5}else if(g.phase==="planet"&&g.frame%12===0){g.health-=.3/HP;g.score+=3}if(g.pod&&Math.abs(g.pod.x-bx)<16)podHit(g)}}else g.motherX=null;
 if(S.meteors>0&&g.frame%7===0)g.rocks.push({x:fx0(g)+g.rand()*(fx1(g)-fx0(g)),y:-10,vx:(g.rand()-.5)*1.2,vy:3+g.rand()*2});
 for(const r of g.rocks){r.x+=r.vx;r.y+=r.vy;if(r.y>bot(g)+10){r.dead=true;continue}for(const s of g.ships)if(!s.dead&&Math.abs(s.x-r.x)<12&&Math.abs(s.y-r.y)<10){shipHit(g,s,2);r.dead=true}
  if(!r.dead&&g.man&&Math.abs(g.man.x-r.x)<10&&Math.abs(g.man.y-r.y)<10){manHit(g);r.dead=true}if(!r.dead&&g.boss&&Math.abs(g.boss.x-r.x)<18&&Math.abs(g.boss.y-r.y)<16){g.health-=1.2/HP;r.dead=true;ev(g,{type:"spark",x:r.x,y:r.y})}
  if(!r.dead&&g.phase==="planet"&&r.y>surfaceY(g)){g.health-=.8/HP;g.score+=8;r.dead=true;ev(g,{type:"spark",x:r.x,y:r.y})}}g.rocks=g.rocks.filter(r=>!r.dead);
 const h=g.hole;if(h){if(--h.t<=0){g.hole=null;return}
  for(const k of["manshots","missiles","plazmas","bossShots"])for(const o of g[k]){const d=Math.hypot(h.x-o.x,h.y-o.y);if(d<100){toward(o,h.x,h.y,2.5);if(d<8)o.dead=true}}
  for(const s of[...g.ships,g.man].filter(Boolean)){const d=Math.hypot(h.x-s.x,h.y-s.y);if(d<70)s.x+=Math.sign(h.x-s.x)*.8;if(d<26&&g.frame%30===0){if(s===g.man)manHit(g);else shipHit(g,s,1)}}}}
// nukes: now and then a bomb pod drifts in; if the ship catches it, kill the ship before it blows or the swarm is wiped
function nukes(g){if(g.calm>0)g.calm--;
 if(!g.pod&&!g.nuke&&g.calm<=0&&--g.nukeT<=0){g.nukeT=nukeEvery(g);const dir=g.rand()<.5?1:-1;g.pod={x:dir>0?fx0(g)-10:fx1(g)+10,y:bot(g)-66,vx:.55*dir,hp:6};ev(g,{type:"hazard",kind:"nuke"});sfx(g,"missile")}
 const p=g.pod;if(p){p.x+=p.vx;const m=g.man;
  if(m&&Math.abs(p.x-m.x)<12&&Math.abs(p.y-m.y)<30){g.pod=null;g.nuke={t:150};ev(g,{type:"nukeArmed"});sfx(g,"powerupbad")}
  else if(p.x<fx0(g)-20||p.x>fx1(g)+20)g.pod=null}
 if(g.nuke&&!g.man)g.nuke=null;
 if(g.nuke&&--g.nuke.t<=0){g.nuke=null;let lost=0;
  for(const a of g.aliens){if(inSanct(g,a))continue;if(a.kind==="tank"){a.armor=0;continue}a.dead=true;a.noLoot=true;lost++}
  g.deaths+=lost;g.aliens=g.aliens.filter(a=>!a.dead);for(const k of["shots","globs","bugs","tentacles"])g[k]=[];
  g.calm=270;g.spawner.t=1;ev(g,{type:"nukeBoom",lost});sfx(g,"planetboom")}}
function podHit(g,dmg=1){const p=g.pod;if(!p)return;p.hp-=dmg;ev(g,{type:"spark",x:p.x,y:p.y});if(p.hp<=0){g.pod=null;g.score+=500;pop(g,p.x,p.y-10,"NUKE DESTROYED! +500","#5fd456");ev(g,{type:"podBoom",x:p.x,y:p.y});sfx(g,"satpop")}}
 // the player's ship in DEFENSE mode
function player(g,m,inp){if(m.inv>0)m.inv--;if(m.stun>0){m.stun--;return{aligned:false}}const sp=2.6+g.manb.speed;m.x=clamp(m.x+(inp.move||0)*sp,fx0(g)+8,fx1(g)-8);return{aligned:false,tgt:null}}
function bomb(g){const m=g.man;if(!m||m.bombs<=0)return ev(g,{type:"deny"});m.bombs--;ev(g,{type:"nova",x:m.x,y:m.y});sfx(g,"planetboom");
 for(const k of["shots","globs","bugs","tentacles"])g[k]=[];for(const a of g.aliens)if(Math.hypot(a.x-m.x,(a.y-m.y)*.6)<110)alienHit(g,a,2)}
// the alien commander (DEFENSE mode) and the idle autopilot (INVADE mode) share the same playbook
const PLAY={grunt:[CMD.ATTACK,CMD.ATTACK,CMD.DIVE,CMD.BUGS,CMD.TENTACLES],zapper:[CMD.ZAP,CMD.ZAP,CMD.EMP],tank:[CMD.SHIELD,CMD.TAUNT,CMD.RAM],healer:[CMD.MEND,CMD.SANCT,CMD.HATCH]};
function playbook(g,a){const L=PLAY[a.kind].filter(c=>!lockedCmd(g,c));return L[Math.floor(g.rand()*L.length)]??CMD.ATTACK}
function alienCommander(g){if(--g.cmdT>0)return;g.cmdT=Math.max(10,40-g.difficulty*8);const idle=idleAliens(g);for(const a of idle.slice(0,2))giveCommand(g,a,playbook(g,a),true);
 if(g.rand()<.25)snatch(g);if(g.man&&g.rushCD<=0&&g.aliens.length>12&&g.rand()<.04)rush(g);if(g.rand()<.04)for(const a of g.aliens)if(a.sparkle>0&&a.kind==="grunt"){const p=g.aliens.find(o=>o!==a&&o.kind==="grunt"&&o.species===1);if(p)mergePair(g,a.id,p.id);break}}
export function autoInput(g){const inp={};if(g.frame%14===0){const idle=idleAliens(g);if(idle.length){const a=idle[0],c=playbook(g,a);inp.command={cmd:c,ids:idle.filter(o=>o.kind===a.kind).slice(0,4).map(o=>o.id)}}}
 if(g.frame%9===0&&(g.loots.some(l=>!l.done)||g.ufos.length))inp.snatch=true;
 const M=g.man||g.boss;if(M&&g.rushCD<=0&&g.frame%30===0&&(g.nuke||g.rand()<.25))inp.rush=true;
 if(g.L.bonus==="sun"){const f=g.flares.find(f=>f.t>-40&&f.t<12);if(f)inp.shift=f.dir>0?1.4:-1.4}
 else if(M&&g.phase!=="bonus"){let cx=0,n=0;for(const a of g.aliens)if(a.chosen&&a.cmd===CMD.ATTACK){cx+=a.x;n++}if(n){const d=M.x-cx/n;if(Math.abs(d)>12)inp.shift=Math.sign(d)*.8}}
 for(const a of g.aliens)if(a.sparkle>0&&a.kind==="grunt"){const p=g.aliens.find(o=>o!==a&&o.kind==="grunt"&&o.species===1&&!o.kami);if(p)inp.merge={src:a.id,dst:p.id};break}
 return inp}
function nearestFoe(g,a){let best=g.man||g.boss,bd=best?Math.abs(best.x-a.x):1e9;for(const s of g.ships){const d=Math.abs(s.x-a.x);if(d<bd){bd=d;best=s}}return best}
function hazards(g){const L=g.L;
 if(L.hazard==="moons"&&--g.moonT<=0){g.moonT=420;if(!g.moons.length){g.moons.push({x:fx0(g)+20+g.rand()*(fx1(g)-fx0(g)-40),y:bot(g)+30,r:18,hit:new Set()});ev(g,{type:"hazard",kind:"moon"})}}
 for(const m of g.moons){m.y-=.35;if(m.y<-40)m.dead=true}
 if(L.hazard==="acid"&&--g.hazT<=0){g.hazT=330;const dir=g.rand()<.5?1:-1;g.hazards.push({kind:"acid",x:dir>0?fx0(g)-20:fx1(g)+20,y:30+g.rand()*50,vx:.45*dir,r:16,life:900});ev(g,{type:"hazard",kind:"acid"})}
 if(L.hazard==="rings"&&--g.hazT<=0){g.hazT=50;g.hazards.push({kind:"rock",x:fx0(g)-10,y:bot(g)*.6+g.rand()*8-4,vx:.9+g.rand()*.4,r:5,life:600,hp:2})}
 if(L.hazard==="storm"&&--g.hazT<=0){g.hazT=420;g.hazards.push({kind:"storm",dir:g.rand()<.5?1:-1,life:150});ev(g,{type:"hazard",kind:"storm"})}
 for(const h of g.hazards){h.life--;if(h.vx)h.x+=h.vx;if(h.life<=0||h.x<fx0(g)-40||h.x>fx1(g)+40)h.dead=true;
  if(h.kind==="acid")for(const a of g.aliens)if(!a.dead&&Math.hypot(a.x-h.x,(a.y-h.y)*1.4)<h.r&&g.frame%20===0)alienHit(g,a,1);
  if(h.kind==="storm"&&g.frame%3===0)for(const a of g.aliens)if(!a.kami)a.mex=clamp(a.mex+h.dir*.5,fx0(g)+20,fx1(g)-20)}
 g.hazards=g.hazards.filter(h=>!h.dead)}
// ---------------- the fleet ----------------
function fleet(g){const mb=g.manb;
 if(!g.L.final&&g.phase==="planet"&&--g.fleetT<=0){g.fleetT=1200;const want=g.L.fleet.filter(k=>k!=="carrier"&&!g.ships.some(s=>s.kind===k));if(want.length&&g.difficulty>2)spawnShip(g,want[0])}
 for(const s of g.ships){const T=SHIP[s.kind];
  if(s.leaving){s.y+=1;if(s.y>bot(g)+30)s.dead=true;continue}
  const row=manY(g)+T.dy;if(s.y>row)s.y=Math.max(row,s.y-1.5);else s.y=row;
  const lane=s.kind==="carrier"||s.kind==="repair"?CX+(s.id%2?-40:40):null;
  const P=pilot(g,s,T.sp+mb.speed*.3,Math.max(0,g.skill-1),{hunts:s.kind==="interceptor",greedy:false,lane});
  if(g.calm<=0&&T.fire&&g.aliens.length&&--s.fireT<=0&&(P.aligned||s.fireT<-40)){s.fireT=T.fire*(g.difficulty===1?1.6:1);g.manshots.push({x:s.x,y:s.y-6,vy:s.kind==="interceptor"?-5:-4});sfx(g,"manshoot")}
  if(s.kind==="carrier"&&--s.launchT<=0){s.launchT=360;if(g.ships.filter(o=>o.kind==="fighter").length<4){spawnShip(g,"fighter",s.x);ev(g,{type:"launch",x:s.x,y:s.y})}}
  if(s.kind==="repair"&&--s.healT<=0){s.healT=120;const hurt=g.ships.filter(o=>o!==s&&o.hp<o.max).sort((a,b)=>a.hp/a.max-b.hp/b.max)[0];
   if(hurt){hurt.hp++;g.beams.push({x0:s.x,y0:s.y,id:hurt.id,t:20});sfx(g,"shield")}else if(g.man&&g.man.shields<2){g.man.shields++;g.beams.push({x0:s.x,y0:s.y,hero:true,t:20})}}
  if(s.kind==="frigate"){if(s.dome<=0){if(++s.domeT>120){s.dome=6;s.domeT=0}}else if(g.frame%60===0&&s.dome<6)s.dome++}}
 for(const b of g.beams)b.t--;g.beams=g.beams.filter(b=>b.t>0);
 g.ships=g.ships.filter(s=>!s.dead)}
function domeAt(g,x,y){for(const s of g.ships)if(s.kind==="frigate"&&s.dome>0&&Math.hypot(x-s.x,y-s.y)<24)return s;const B=g.boss;if(B&&B.dome>0&&Math.hypot(x-B.x,y-B.y)<30)return B;return null}
function shipHit(g,s,dmg){if(s.inv>0)return;s.hp-=dmg;s.inv=6;sfx(g,"bosshit");ev(g,{type:"spark",x:s.x,y:s.y});
 if(s.hp<=0){s.dead=true;g.kills++;g.score+=150+(s.kind==="carrier"?350:0);sfx(g,"mandie");ev(g,{type:"shipDie",x:s.x,y:s.y,kind:s.kind});pop(g,s.x,s.y-10,`${s.kind.toUpperCase()} DOWN`,"#ffd93a");
  if(s.kind==="carrier"||g.rand()<.05)g.loots.push({x:s.x,y:s.y-4,good:true,super:true,vy:-1.6,id:g.uid++});else if(dice(g,3))g.loots.push({x:s.x,y:s.y-4,good:true,vy:-2,id:g.uid++})}}
// ---------------- projectiles and collisions ----------------
function projectiles(g){const D=g.D,me=g.me,mb=g.manb,m=g.man,B=g.boss;
 const hitMan=(x,y,r)=>{if(!m||m.inv>0)return false;if(Math.abs(x-m.x)<r+6&&Math.abs(y-m.y)<r+6){manHit(g);return true}return false};
 const hitShip=(x,y,r,dmg)=>{for(const s of g.ships)if(!s.dead&&!s.leaving&&Math.abs(x-s.x)<r+(s.kind==="carrier"?18:7)&&Math.abs(y-s.y)<r+(s.kind==="carrier"?8:7)){shipHit(g,s,dmg);return true}return false};
 const HP=(g.L.hp||1)*(g.mode==="defense"?5:1);const hitBoss=(x,y,r,dmg)=>{if(!B)return false;if(Math.abs(x-B.x)<r+14&&Math.abs(y-B.y)<r+14){g.health-=dmg/HP;g.score+=10;sfx(g,"bosshit");ev(g,{type:"spark",x,y});return true}return false};
 const hitPlanet=(x,y,dmg)=>{if(g.phase!=="planet"||y<surfaceY(g)+2||y>bot(g))return false;g.health-=dmg/HP;g.score+=10;sfx(g,"planethit");ev(g,{type:"spark",x,y});return true};
 const hitRock=(x,y)=>{for(const h of g.hazards)if(h.kind==="rock"&&Math.abs(x-h.x)<h.r+2&&Math.abs(y-h.y)<h.r+2){if(--h.hp<=0){h.dead=true;g.score+=5;ev(g,{type:"spark",x:h.x,y:h.y})}return true}return false};
 const hitPod=(x,y)=>{const p=g.pod;if(!p||Math.abs(x-p.x)>9||Math.abs(y-p.y)>9)return false;podHit(g);return true};
 const guarded=s=>{const sh=g.aliens.find(o=>o.chosen&&o.cmd===CMD.SHIELD&&o.dome>0&&Math.hypot(o.x-s.x,o.y-s.y)<22);if(sh){sh.dome--;ev(g,{type:"dome",x:s.x,y:s.y});return true}return inSanct(g,s)};
 const absorbed=(x,y)=>{for(const o of g.moons)if(Math.hypot(x-o.x,y-o.y)<o.r)return true;const d=domeAt(g,x,y);if(!d)return false;d.dome--;ev(g,{type:"dome",x,y});return true};
 for(const s of g.shots){s.y+=s.vy;if(s.vx)s.x+=s.vx;if(s.y>bot(g)+6){s.dead=true;continue}
  if(hitPod(s.x,s.y)){s.dead=true;continue}
  if(hitRock(s.x,s.y)||absorbed(s.x,s.y)||hitShip(s.x,s.y,1,1)||hitMan(s.x,s.y,1)||hitBoss(s.x,s.y,1,D.bossdmg)||hitPlanet(s.x,s.y,D.planetdmg)){s.dead=true;continue}
  for(const t of g.sats)if(Math.abs(t.x-s.x)<8&&Math.abs(t.y-s.y)<8){t.dead=true;s.dead=true;g.score+=10;g.loots.push({x:t.x,y:t.y,good:true,vy:-2,id:g.uid++});sfx(g,"satpop")}}
 for(const s of g.globs){s.y+=3;if(s.y>bot(g)+8){s.dead=true;continue}if(!s.hit&&(hitPod(s.x,s.y)||hitRock(s.x,s.y)||absorbed(s.x,s.y)||hitShip(s.x,s.y,5,2)||hitMan(s.x,s.y,5)||hitBoss(s.x,s.y,5,D.bossdmg)||hitPlanet(s.x,s.y,D.planetdmg))){s.hit=true;s.dead=true}}
 for(const b of g.bugs){const tgt=B||m;if(tgt)toward(b,tgt.x,tgt.y,1+me.bugs*.3);else b.y+=1;if(--b.life<=0){b.dead=true;continue}
  if(absorbed(b.x,b.y)){b.dead=true;continue}
  if(m&&Math.abs(b.x-m.x)<8&&Math.abs(b.y-m.y)<8){if(m.shields>0){m.shields--;sfx(g,"shield")}else if(g.rand()<.35)manHit(g);b.dead=true;g.score+=1;continue}
  if(hitShip(b.x,b.y,3,1)||hitBoss(b.x,b.y,3,D.bossdmg)||hitPlanet(b.x,b.y,D.planetdmg))b.dead=true}
 for(const t of g.tentacles){if(--t.life<=0){t.dead=true;continue}const src=g.aliens.find(a=>a.id===t.src);if(src){t.x=src.x+(t.x-src.x)*.9;t.y=src.y}
  t.ph=(t.ph||0)+.28;t.tx=t.x+Math.sin(t.ph)*14*Math.min(1,(30-t.life)/10);const hx=(t.x+t.tx)/2;
  if(m&&!t.hit.has("m")&&m.inv<=0&&Math.abs(hx-m.x)<11&&m.y>t.y&&m.y<t.y+56){t.hit.add("m");manHit(g)}
  for(const s of g.ships)if(!t.hit.has(s.id)&&Math.abs(hx-s.x)<12&&s.y>t.y&&s.y<t.y+56){t.hit.add(s.id);shipHit(g,s,1)}
  if(B&&!t.hit.has("b")&&Math.abs(t.x-B.x)<16&&B.y>t.y&&B.y-14<t.y+52){t.hit.add("b");hitBoss(B.x,B.y,1,D.bossdmg)}
  if(g.pod&&!t.hit.has("pod")&&Math.abs(t.x-g.pod.x)<9&&g.pod.y>t.y&&g.pod.y<t.y+56){t.hit.add("pod");podHit(g)}
  if(!t.hit.has("p")&&t.y+52>=surfaceY(g)&&g.phase==="planet"){t.hit.add("p");hitPlanet(t.x,t.y+52,D.planetdmg)}}
 for(const s of g.manshots){s.y+=s.vy;if(s.y<-6){s.dead=true;continue}
  if(guarded(s)){s.dead=true;continue}
  for(const a of g.aliens)if(!a.dead&&Math.abs(a.x-s.x)<(a.species===2?10:7)&&Math.abs(a.y-s.y)<(a.species===2?10:8)){s.dead=true;alienHit(g,a,1);break}
  if(s.dead)continue;for(const l of g.loots)if(l.good&&!l.done&&!l.orb&&Math.abs(l.x-s.x)<8&&Math.abs(l.y-s.y)<8){l.done=2;l.t=40;g.combo=0;s.dead=true;pop(g,l.x,l.y,"INTERCEPTED!","#ff5468");sfx(g,"intercept")}
  if(s.dead)continue;for(const t of g.sats)if(Math.abs(t.x-s.x)<8&&Math.abs(t.y-s.y)<8){t.dead=true;s.dead=true;g.loots.push({x:t.x,y:t.y,good:false,vy:2,id:g.uid++});sfx(g,"satpop")}}
 for(const s of g.bossShots){s.x+=s.vx;s.y+=s.vy;if(--s.zig<=0){s.zig=16;s.vx=-s.vx}if(s.y<-10||s.x<fx0(g)-20||s.x>fx1(g)+20){s.dead=true;continue}
  if(guarded(s)){s.dead=true;continue}
  for(const a of g.aliens)if(!a.dead&&Math.abs(a.x-s.x)<9&&Math.abs(a.y-s.y)<9){s.dead=true;alienHit(g,a,1);break}}
 for(const s of g.missiles){if(--s.life<=0||guarded(s)){s.dead=true;continue}const tgt=g.aliens.find(a=>a.id===s.tgt)||g.aliens[0];if(tgt){s.tgt=tgt.id;toward(s,tgt.x,tgt.y,.75)}else s.y-=.7;
  for(const a of g.aliens)if(!a.dead&&Math.abs(a.x-s.x)<8&&Math.abs(a.y-s.y)<8){s.dead=true;alienHit(g,a,99);break}}
 for(const s of g.plazmas){if(--s.life<=0||guarded(s)){s.dead=true;continue}const tgt=g.aliens[g.aliens.length-1];if(tgt)toward(s,tgt.x,tgt.y,1+mb.plazma*.15);for(const a of g.aliens)if(!a.dead&&Math.abs(a.x-s.x)<7&&Math.abs(a.y-s.y)<7){s.dead=true;alienHit(g,a,1);break}}
 // dives into ships, satellites, moons, rocks
 for(const a of g.aliens){if(a.dead)continue;const diving=a.dive>0||a.kami;
  if(diving&&g.pod&&Math.abs(a.x-g.pod.x)<10&&Math.abs(a.y-g.pod.y)<10){podHit(g,3)}
  if(a.ram&&a.dive===1&&g.phase==="planet"&&a.y>=surfaceY(g)){g.health-=D.planetdmg*4/HP;g.score+=30;sfx(g,"planethit");ev(g,{type:"spark",x:a.x,y:a.y});a.dive=0;a.y=-16;a.ram=false}
  if(m&&m.inv<=0&&diving&&Math.abs(a.x-m.x)<10&&Math.abs(a.y-m.y)<10){if(a.dive===2)a.hitMan=true;if(a.dive===1)a.dive=0,a.y=-16;manHit(g);if(a.kami){a.dead=true;a.noLoot=true}}
  if(diving)for(const s of g.ships)if(!s.dead&&!s.leaving&&Math.abs(a.x-s.x)<(s.kind==="carrier"?20:10)&&Math.abs(a.y-s.y)<10){shipHit(g,s,a.ram?4:2);if(a.kami){a.dead=true;a.noLoot=true}else if(a.dive===2)a.hitMan=true;else{a.dive=0;a.y=-16}break}
  if(B&&diving&&Math.abs(a.x-B.x)<16&&Math.abs(a.y-B.y)<16){if(B.dome>0){B.dome=Math.max(0,B.dome-20)}g.health-=D.bossdmg*(a.ram?5:2)/HP;g.score+=10;sfx(g,"bosshit");if(a.kami){a.dead=true;a.noLoot=true}else{a.dive=0;a.y=-16;a.hitMan=true}}
  for(const t of g.sats)if(!t.dead&&a.dive>0&&Math.abs(t.x-a.x)<10&&Math.abs(t.y-a.y)<10){t.dead=true;g.score+=10;g.loots.push({x:t.x,y:t.y,good:true,vy:-2,id:g.uid++});sfx(g,"satpop")}
  for(const o of g.moons)if(!o.hit.has(a.id)&&Math.hypot(o.x-a.x,o.y-a.y)<o.r+6){o.hit.add(a.id);a.noLoot=true;alienHit(g,a,1)}
  for(const h of g.hazards)if(h.kind==="rock"&&!h.dead&&Math.abs(a.x-h.x)<h.r+5&&Math.abs(a.y-h.y)<h.r+5){h.dead=true;if(!diving)alienHit(g,a,1)}}
 g.aliens=g.aliens.filter(a=>!a.dead);
 for(const t of g.sats){t.x+=t.dir*.8;if(t.x<fx0(g)-10||t.x>fx1(g)+10)t.dead=true}
 // loot drifts: good rises toward the swarm, bad falls toward the ship
 for(const l of g.loots){if(l.done){if(--l.t<=0)l.dead=true;continue}l.y+=l.vy;
  const catcher=!l.good&&[m,...g.ships].find(u=>u&&Math.abs(l.x-u.x)<10&&Math.abs(l.y-u.y)<10);
  if(catcher){l.done=2;l.t=40;g.combo=0;g.stage++;buffMan(g);pop(g,l.x,l.y-8,"INTERFERENCE!","#ff9a2e");sfx(g,"powerupbad");continue}
  if(l.y<-10||l.y>bot(g)+10){l.dead=true;if(!l.orb){g.combo=0;pop(g,clamp(l.x,fx0(g)+12,fx1(g)-12),l.good?12:bot(g)-24,"MISS!","#9488ad")}}}
 for(const k of["shots","globs","bugs","tentacles","manshots","bossShots","missiles","plazmas","sats","loots","moons","ships"])g[k]=g[k].filter(o=>!o.dead)}
function alienHit(g,a,dmg){if(a.dead)return;if(a.armor>0&&dmg<99){a.armor-=dmg;ev(g,{type:"armor",x:a.x,y:a.y});return}a.dead=true;g.deaths++;if(g.mode==="defense")g.score+=a.kind==="tank"?60:a.species===2?80:30;sfx(g,"aliendie");ev(g,{type:"alienDie",x:a.x,y:a.y,species:a.species});
 if(g.menu?.id===a.id)g.menu=null;if(g.phase!=="bonus"&&!a.kami&&!a.noLoot&&dice(g,g.D.badloot))g.loots.push({x:a.x,y:a.y,good:false,vy:2,id:g.uid++})}
function manHit(g){const m=g.man;if(!m||m.inv>0)return;if(m.shields>0){m.shields--;m.inv=12;sfx(g,"shield");return}
 if(--m.hp>0){m.inv=24;sfx(g,"bosshit");ev(g,{type:"manHurt",x:m.x,y:m.y});return}
 if(g.mode!=="defense"){g.kills++;g.score+=100}sfx(g,"mandie");ev(g,{type:"manDie",x:m.x,y:m.y});
 if(g.nuke){g.nuke=null;g.score+=1000;pop(g,m.x,m.y-16,"NUKE DEFUSED! +1000","#5fd456");ev(g,{type:"defused",x:m.x,y:m.y})}
 if(g.rand()<.09)g.loots.push({x:m.x,y:m.y-4,good:true,super:true,vy:-1.6,id:g.uid++});else if(dice(g,g.D.goodloot))g.loots.push({x:m.x,y:m.y-4,good:true,vy:-2,id:g.uid++});
 if(g.phase==="planet"){g.health-=3*g.D.planetdmg*2/(g.L.hp||1);g.prise=Math.max(-16,g.prise-2)}
 g.combo+=4-Math.max(0,g.manlives);g.manlives--;
 if(g.mode==="defense"){if(g.manlives<=0){g.man=null;return}}
 if(g.manlives<=0){g.manlives=3;ev(g,{type:"manWave"});if(!g.D.stopper){g.stage=0;for(const k of MAN_BUFFS)g.manb[k]=0;g.options=[]}}
 spawnMan(g)}
// ---------------- bonus stages ----------------
function setupBonus(g){const b=g.L.bonus,x0=fx0(g)+20,x1=fx1(g)-20;
 for(let r=0;r<3;r++)for(let i=0;i<7;i++){const a=spawnAlien(g,x0+i*(x1-x0)/6,SLOT_Y+r*16,"grunt");a.y=-20-r*12-i*4}
 if(b==="pluto"){const fw=fx1(g)-fx0(g),k=fw/196,cx=fx0(g)+98*k,cy=bot(g)-20+160*k,R=158*k,cols=11,span=.5;
  for(let r=0;r<2;r++)for(let c=0;c<cols;c++){const ang=-Math.PI/2+(c-(cols-1)/2)*(span/(cols-1))*2,rr=R+3+r*11;g.ice.push({x:cx+Math.cos(ang)*rr,y:cy+Math.sin(ang)*rr,ang:ang+Math.PI/2,w:R*span*2/(cols-1)-1,hp:g.D.ice,max:g.D.ice})}}
 g.flareT=90;g.waveT=40;g.orbT=50}
function tickBonus(g){const b=g.L.bonus,me=g.me;if(g.rushCD>0)g.rushCD--;if(g.bigdive>0&&--g.bigdive===0)endRush(g);
 if(g.chainT>0&&--g.chainT===0)g.chain=0;
 // aliens keep formation and run their orders
 for(const a of g.aliens){
  if(a.mergeInto){const src=g.aliens.find(s=>s.id===a.mergeInto);if(!src)a.mergeInto=0;else if(toward(a,src.x,src.y,6)){a.dead=true;src.species=2;src.sparkle=0;src.t8=60;ev(g,{type:"merged",x:src.x,y:src.y})}continue}
  if(a.okT>0&&--a.okT===0){a.chosen=true;a.t0=1;a.t2=1;a.t3=1;a.t4=20}
  const tgt=b==="pluto"?g.ice.find(i=>i.hp>0&&Math.abs(i.x-a.x)<40)||g.ice.find(i=>i.hp>0):b==="nibiru"?g.ufos.filter(u=>!u.dead).sort((p,q)=>Math.abs(p.x-a.x)-Math.abs(q.x-a.x))[0]:null;
  if(a.dive===1){a.x+=a.vx;a.y+=a.vy;if(a.y>bot(g)+8||a.x<fx0(g)-8||a.x>fx1(g)+8){a.dive=0;a.x=clamp(a.x,fx0(g),fx1(g));a.y=-16}}
  else if(a.dive===2){const t2=b==="pluto"?g.ice.find(i=>i.hp>0&&Math.abs(i.x-a.x)<60):null;if(t2)toward(a,t2.x,t2.y,4);else a.dive=0}
  else toward(a,a.mex,a.mey,1+me.speed);
  if(a.chosen){if(a.cmd===CMD.ATTACK&&--a.t2<=0){a.t2=Math.max(8,50-me.spit*3);g.shots.push({x:a.x,y:a.y+6,vy:4});sfx(g,"shoot")}
   if(a.cmd===CMD.DIVE&&a.dive===0&&--a.t0<=0&&tgt){a.t0=60;a.dive=1;aim(a,tgt.x,tgt.y,3);sfx(g,"dive")}
   if(a.cmd===CMD.BUGS&&--a.t3<=0){a.t3=90;g.bugs.push({x:a.x,y:a.y,life:200});sfx(g,"bug")}
   if(a.cmd===CMD.TENTACLES&&--a.t4<=0){a.t4=40;g.tentacles.push({x:a.x-6+g.rand()*12,y:a.y,life:30,src:a.id,hit:new Set()});sfx(g,"tentacle")}}
  if(a.species===2&&--a.t8<=0){a.t8=50;g.globs.push({x:a.x,y:a.y+8,hit:false})}}
 if(b==="sun"){
  if(--g.flareT<=0){g.flareT=Math.max(55,110-g.frame/30);const dir=g.rand()<.5?1:-1,peak=SLOT_Y+8+g.rand()*40;g.flares.push({dir,peak,t:-40,len:70,x:0,y:0})}
  for(const f of g.flares){f.t++;if(f.t>f.len){f.dead=true;continue}if(f.t<0)continue;const k=f.t/f.len,x0=fx0(g)-10,x1=fx1(g)+10;
   f.x=f.dir>0?x0+(x1-x0)*k:x1-(x1-x0)*k;f.y=bot(g)+10-(bot(g)+10-f.peak)*Math.sin(k*Math.PI);
   for(const a of g.aliens)if(!a.dead&&Math.hypot(a.x-f.x,a.y-f.y)<11){a.dead=true;g.deaths++;sfx(g,"aliendie");ev(g,{type:"alienDie",x:a.x,y:a.y,species:a.species})}}
  if(--g.orbT<=0){g.orbT=34;g.loots.push({x:fx0(g)+16+g.rand()*(fx1(g)-fx0(g)-32),y:bot(g)+6,good:true,orb:true,vy:-.7,id:g.uid++})}}
 if(b==="nibiru"&&--g.waveT<=0){g.waveT=110;const n=5,dir=g.rand()<.5?1:-1,y=bot(g)*.45+g.rand()*bot(g)*.3,shape=Math.floor(g.rand()*3);
  for(let i=0;i<n;i++)g.ufos.push({ufo:true,x:(dir>0?fx0(g)-14:fx1(g)+14)-dir*i*16,y:y+(shape===1?Math.sin(i)*10:shape===2?i*6-12:0),vx:dir*(1.2+g.rand()*.4),ph:i,dead:false})}
 for(const u of g.ufos){if(u.dead)continue;u.x+=u.vx;u.y+=Math.sin((g.frame+u.ph*9)*.08)*.4;if(u.vx>0&&u.x>fx1(g)+30||u.vx<0&&u.x<fx0(g)-30){u.dead=true;u.gone=true}}
 // shots / dives / bugs / tentacles against bonus targets
 const hitTarget=(x,y,r,dmg)=>{if(b==="pluto"){for(const i of g.ice)if(i.hp>0&&Math.abs(x-i.x)<i.w/2+r&&Math.abs(y-i.y)<6+r){iceHit(g,i,dmg);return true}}
  if(b==="nibiru"){for(const u of g.ufos)if(!u.dead&&Math.abs(x-u.x)<8+r&&Math.abs(y-u.y)<7+r){popUfo(g,u);return true}}return false};
 for(const s of g.shots){s.y+=s.vy;if(s.y>bot(g)+6||hitTarget(s.x,s.y,1,1))s.dead=true}
 for(const s of g.globs){s.y+=3;if(s.y>bot(g)+8||hitTarget(s.x,s.y,4,2))s.dead=true}
 for(const bg of g.bugs){const tgt=b==="pluto"?g.ice.find(i=>i.hp>0):g.ufos.find(u=>!u.dead);if(tgt)toward(bg,tgt.x,tgt.y,1.4);else bg.y+=1;if(--bg.life<=0||hitTarget(bg.x,bg.y,2,1))bg.dead=true}
 for(const t of g.tentacles){if(--t.life<=0){t.dead=true;continue}const src=g.aliens.find(a=>a.id===t.src);if(src){t.x=src.x+(t.x-src.x)*.9;t.y=src.y}if(t.life===18)for(let y=t.y+8;y<t.y+56;y+=8)if(hitTarget(t.x,y,3,1))break}
 for(const a of g.aliens)if(a.dive>0&&hitTarget(a.x,a.y,4,2)){a.dive=0;a.y=-16}
 for(const l of g.loots){if(l.done){if(--l.t<=0)l.dead=true;continue}l.y+=l.vy;if(l.y<-10)l.dead=true}
 for(const k of["shots","globs","bugs","tentacles","loots","flares"])g[k]=g[k].filter(o=>!o.dead);g.ufos=g.ufos.filter(u=>!u.gone&&!(u.dead&&(u.t=(u.t||20)-1)<=0));
 g.aliens=g.aliens.filter(a=>!a.dead);
 // end of stage
 const done=b==="pluto"&&g.ice.every(i=>i.hp<=0);
 if(--g.timer<=0||done||!g.aliens.length){const surv=g.aliens.length*50,perfect=done?5000*g.difficulty:0;g.bonusPts+=surv+perfect;g.score+=surv+perfect;
  g.state="clear";g.result="win";g.tally={survivors:g.aliens.length,survivorPts:surv,perfect:!!done,perfectPts:perfect,bonus:g.bonusPts};sfx(g,"win");ev(g,{type:"end",result:"win",bonus:true})}}
function iceHit(g,i,dmg){i.hp=Math.max(0,i.hp-dmg);g.score+=20;g.bonusPts+=20;sfx(g,"planethit");ev(g,{type:"spark",x:i.x,y:i.y});if(i.hp<=0){g.score+=200;g.bonusPts+=200;pop(g,i.x,i.y-8,"+200","#d2fbff");ev(g,{type:"iceBreak",x:i.x,y:i.y})}}
function popUfo(g,u){if(u.dead)return;u.dead=true;u.t=20;g.chain++;g.chainT=45;const pts=150*Math.min(5,g.chain);g.score+=pts;g.bonusPts+=pts;sfx(g,"satpop");pop(g,u.x,u.y-8,g.chain>1?`x${Math.min(5,g.chain)} ${pts}`:`+${pts}`,"#ffd93a");ev(g,{type:"spark",x:u.x,y:u.y})}
export function snapshot(g){const t=Math.floor(g.frame/30);
 return{active:true,state:g.state,result:g.result,difficulty:DIFF[g.difficulty].name,planet:g.L.name,level:g.level+1,levels:LEVELS.length,kind:g.L.kind,phase:g.phase,health:Math.max(0,Math.round(g.health)),
  aliens:g.aliens.length,idle:idleAliens(g).length,score:g.score,combo:g.combo,lvl:g.lvl,stage:g.stage,kills:g.kills,deaths:g.deaths,manlives:g.manlives,ships:g.ships.length,
  rush:g.rushCD<=0&&g.bigdive<=0,mode:g.mode,quota:g.mode==="defense"?Math.max(0,g.quota-g.spawned)+g.aliens.length:null,hp:g.man?g.man.hp:0,bombs:g.man?g.man.bombs:0,nuke:g.nuke?Math.ceil(g.nuke.t/30):null,calm:g.calm>0,timer:g.phase==="bonus"?Math.ceil(g.timer/30):null,
  time:`${Math.floor(t/60)}:${String(t%60).padStart(2,"0")}`,buffs:{...g.me},man:{...g.manb}}}
