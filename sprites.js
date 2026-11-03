// OFFENSE OF THE ALIENS 16-bit sprite set (SD), painted at the original 310x180 scale and drawn 4x.
// New designs after the creator's 2014 originals; v2 adds the human fleet, more planets and bonus-stage art.
import{painter,R,hash,toCanvas}from"../2diso/sprites.js?v=1012-2diso1";
// the swarm: species 1 (two colour variants, 2 animation frames) and the merged species 2
function alien(variant,frame){const P=painter(16,16),body=variant?R.purple:R.green,eye=variant?R.yellow:R.pink;
 P.ellipse(8,7,6.5,5.5,body,{amb:.3});P.ellipse(8,5,3.5,2.5,R.white,{amb:.6});P.eye(6,7,1.8,eye);P.eye(10,7,1.8,eye);
 const leg=frame?[[3,12,1,15],[6,12,5,15],[10,12,11,15],[13,12,15,15]]:[[3,12,2,15],[6,12,6,15],[10,12,10,15],[13,12,14,15]];for(const[a,b,c,d]of leg)P.line(a,b,c,d,body[2]);
 P.line(5,1,4,0,body[3]);P.line(11,1,12,0,body[3]);P.set(4,0,body[4]);P.set(12,0,body[4]);return P.outline("#05040a")}
function bigAlien(frame){const P=painter(24,24);P.ellipse(12,11,10,8,R.teal,{amb:.3});P.ellipse(12,8,6,4,R.white,{amb:.5});
 for(const x of[7,12,17])P.eye(x,11,2.2,R.lava);for(let i=0;i<5;i++){const x=4+i*4;P.line(x,17,x+(frame?1:-1),23,R.teal[2])}P.disc(12,3,2,"#ffd93a");P.set(11,2,"#fff6b8");return P.outline("#05040a")}
// the humans
function ship(kind=0){const P=painter(16,16),hull=kind?R.cyan:R.white;P.poly([[8,0],[11,6],[15,12],[11,13],[8,11],[5,13],[1,12],[5,6]],hull);
 P.poly([[7,4],[9,4],[10,8],[6,8]],R.navy);P.set(8,5,"#7f9bff");P.line(2,12,4,10,R.red[3]);P.line(14,12,12,10,R.red[3]);P.box(6,13,4,3,R.orange,{bevel:0});return P.outline("#05040a")}
function fighter(){const P=painter(12,12);P.poly([[6,0],[8,5],[11,9],[8,10],[6,8],[4,10],[1,9],[4,5]],R.steel);P.poly([[5,3],[7,3],[7,6],[5,6]],R.navy);P.set(6,4,"#7f9bff");P.box(5,10,2,2,R.orange,{bevel:0});return P.outline("#05040a")}
function interceptor(){const P=painter(14,14);P.poly([[7,0],[9,4],[13,6],[13,10],[9,9],[7,13],[5,9],[1,10],[1,6],[5,4]],R.red);P.poly([[6,3],[8,3],[8,7],[6,7]],R.navy);P.set(7,4,"#ffb4bd");P.line(1,6,1,11,R.white[3]);P.line(13,6,13,11,R.white[3]);return P.outline("#05040a")}
function carrier(){const P=painter(44,20);P.poly([[4,8],[10,2],[34,2],[40,8],[43,14],[38,18],[6,18],[1,14]],R.steel);P.box(12,4,20,5,R.dark,{bevel:1});
 for(let i=0;i<5;i++)P.set(14+i*4,6,"#ffd93a");P.box(17,12,10,4,R.navy,{bevel:1});P.set(22,13,"#7f9bff");P.box(3,14,5,4,R.orange,{bevel:0});P.box(36,14,5,4,R.orange,{bevel:0});P.line(10,2,8,0,R.red[3]);return P.outline("#05040a")}
function repair(){const P=painter(16,14);P.ellipse(8,7,7,5.5,R.white,{amb:.3});P.box(7,3,2,8,R.green,{bevel:0});P.box(4,6,8,2,R.green,{bevel:0});P.box(5,11,6,3,R.orange,{bevel:0});return P.outline("#05040a")}
function frigate(){const P=painter(22,16);P.poly([[11,0],[15,5],[21,9],[19,14],[3,14],[1,9],[7,5]],R.navy);P.ellipse(11,7,4,3,R.cyan,{amb:.5});P.set(10,6,"#ffffff");P.box(4,12,4,3,R.orange,{bevel:0});P.box(14,12,4,3,R.orange,{bevel:0});return P.outline("#05040a")}
function boss(type){const P=painter(32,32),hull=[R.white,R.red,R.orange,R.purple][type-1],trim=[R.cyan,R.yellow,R.purple,R.gold][type-1];
 P.poly([[16,0],[22,8],[31,20],[28,28],[16,24],[4,28],[1,20],[10,8]],hull);P.poly([[13,6],[19,6],[21,14],[11,14]],R.navy);P.set(16,8,"#bcd2ff");
 P.box(3,18,6,10,trim,{bevel:1});P.box(23,18,6,10,trim,{bevel:1});P.box(12,24,8,6,R.orange,{bevel:1});if(type===3){P.ellipse(16,18,4,4,R.lava,{amb:.5})}if(type===2){P.line(8,12,24,12,R.yellow[3])}
 if(type===4){P.ellipse(16,18,5,4,R.gold,{amb:.5});P.set(16,17,"#ffffff");P.line(6,10,1,4,R.gold[3]);P.line(26,10,31,4,R.gold[3]);P.set(1,4,"#ff5468");P.set(31,4,"#ff5468")}return P.outline("#05040a")}
function sat(ufo){const P=painter(16,16);if(ufo){P.ellipse(8,9,7.5,3.5,R.steel,{amb:.3});P.ellipse(8,6,4,3,R.cyan,{amb:.5});for(const x of[3,8,13])P.set(x,10,"#ffd93a")}else{P.box(6,5,4,6,R.gold,{bevel:1});P.box(0,6,5,4,R.blue,{bevel:0});P.box(11,6,5,4,R.blue,{bevel:0});P.line(8,5,8,1,R.steel[3]);P.set(8,0,"#ff5468")}return P.outline("#05040a")}
function loot(good,frame){const P=painter(16,16),ramp=good?R.green:R.red,g=frame%2;P.ellipse(8,8,7,7,ramp,{amb:.35});P.ellipse(8,8,4.5,4.5,R.white,{amb:.7});
 if(good){P.line(8,5,8,11,ramp[2]);P.line(5,8,11,8,ramp[2])}else{P.line(5,5,11,11,ramp[2]);P.line(11,5,5,11,ramp[2])}if(g)P.set(5,4,"#ffffff");return P.outline("#05040a")}
function orb(frame){const P=painter(12,12);P.ellipse(6,6,5,5,R.yellow,{amb:.4});P.ellipse(6,6,2.5,2.5,R.white,{amb:.8});if(frame)P.set(3,3,"#ffffff");return P.outline("#3a1204")}
function bug(f){const P=painter(8,8);P.ellipse(4,4,3,2.5,R.lava,{amb:.4});P.set(f?1:0,2,"#d2fbff");P.set(f?6:7,2,"#d2fbff");P.set(3,3,"#05040a");P.set(5,3,"#05040a");return P.outline("#05040a")}
function glob(f){const P=painter(12,12);P.ellipse(6,6,5,5-f*.6,R.teal,{amb:.5});P.disc(4,4,1.2,"#ffffff");return P.outline("#032420")}
function missile(){const P=painter(6,10);P.box(1,1,4,7,R.steel,{bevel:0});P.poly([[1,1],[3,0],[5,1]],R.red);P.set(3,9,"#ff9a2e");P.set(2,9,"#ffd93a");return P.outline("#05040a")}
function rock(i){const P=painter(12,12);P.ellipse(6,6,5,4+i%2,R.stone,{amb:.25});P.disc(4,5,1.2,"#4a4452");P.disc(8,7,1,"#4a4452");return P.outline("#05040a")}
function iceBlock(cracks){const P=painter(18,10);P.box(0,0,18,10,R.cyan,{bevel:1});P.box(2,2,6,3,R.white,{bevel:0});
 if(cracks>0)P.line(5,1,9,8,"#0c5f72");if(cracks>1)P.line(12,0,10,9,"#0c5f72");if(cracks>2)P.line(2,6,16,4,"#0c5f72");return P.outline("#05040a")}
function icon(kind,locked){const P=painter(13,13),ramp=locked?R.dark:[R.navy,R.blue,R.cyan,R.teal][kind%4];P.ellipse(6.5,6.5,6.5,6.5,ramp,{amb:.4});const c=locked?"#3c3c55":"#ffffff";
 if(kind===0){P.line(4,4,8,3,c);P.line(8,3,9,5,c);P.line(9,5,7,7,c);P.set(7,8,c);P.set(7,10,c)}
 if(kind===1){P.ellipse(6.5,7,3,2.5,locked?R.dark:R.lava,{amb:.6});P.set(3,4,c);P.set(10,4,c)}
 if(kind===2){P.line(6,2,6,10,c);P.line(2,6,10,6,c);P.set(6,6,"#ff5468")}
 if(kind===3){P.line(6,2,6,9,c);P.line(3,7,6,10,c);P.line(9,7,6,10,c)}
 if(kind===4){for(const x of[4,6,8])P.line(x,3,x+(x===6?0:x<6?-1:1),10,c)}
 return P.outline("#05040a")}
function arrow(dir){const P=painter(24,48);P.poly(dir<0?[[2,24],[22,4],[22,44]]:[[22,24],[2,4],[2,44]],R.steel);P.poly(dir<0?[[8,24],[18,14],[18,34]]:[[16,24],[6,14],[6,34]],R.cyan,{flat:true});return P.outline("#05040a")}
function moon(){const P=painter(36,36);P.ellipse(18,18,17,17,R.stone,{amb:.2});for(let i=0;i<6;i++){const x=7+hash(i,1,7)*22,y=7+hash(i,2,7)*22,r=1.5+hash(i,3,7)*3.5;P.disc(x,y,r,"#4a4452");P.disc(x-.6,y-.6,r-1,"#26222c")}return P.outline("#05040a")}
function cloud(){const P=painter(40,24);for(let i=0;i<7;i++)P.ellipse(8+hash(i,1,3)*24,8+hash(i,2,3)*8,6+hash(i,3,3)*5,5+hash(i,4,3)*3,R.olive,{amb:.35,gloss:false});return toCanvas(P)}
// planet caps rising from the bottom (196 wide) and the bonus-stage bodies
const PL={1:{base:R.blue,land:R.green,pat:"land",rim:"#bcd2ff"},2:{base:R.red,land:R.lava,pat:"spots",rim:"#ffb4bd"},3:{base:R.orange,land:R.gold,pat:"bands",rim:"#ffe08a"},
 4:{base:R.yellow,land:R.orange,pat:"swirl",rim:"#fff6b8"},5:{base:R.gold,land:R.yellow,pat:"bands",rim:"#fff3c2",rings:true},6:{base:R.blue,land:R.cyan,pat:"swirl",rim:"#d2fbff"},
 7:{base:R.purple,land:R.red,pat:"cracks",rim:"#eab3ff"},8:{base:R.lava,land:R.yellow,pat:"swirl",rim:"#fff6b8"},9:{base:R.stone,land:R.white,pat:"spots",rim:"#d8d0de"},10:{base:R.dark,land:R.lava,pat:"cracks",rim:"#ff6a10"}};
export function buildPlanet(kind){const w=196,h=140,P=painter(w,h),cx=98,cy=160,r=158,s=kind*13,T=PL[kind]||PL[1];
 for(let y=0;y<h;y++)for(let x=0;x<w;x++){const dx=x+.5-cx,dy=y+.5-cy,d=Math.hypot(dx,dy);if(d>r)continue;const lit=.75-(d/r)*.35-(dx/r)*.25;
  let ramp=T.base;
  if(T.pat==="land"&&hash(x>>3,y>>3,s)+hash(x>>2,y>>2,s+1)*.5>1.05)ramp=T.land;
  if(T.pat==="spots"&&hash(x>>2,y>>2,s)>.86)ramp=T.land;
  if(T.pat==="bands"&&((Math.floor((y+Math.sin(x*.05)*4)/9))%2))ramp=T.land;
  if(T.pat==="swirl"&&Math.sin(x*.06+Math.sin(y*.09)*3+y*.05)>.55)ramp=T.land;
  if(T.pat==="cracks"&&Math.abs(Math.sin(x*.11+hash(x>>4,y>>4,s)*4)+Math.cos(y*.13))<.12)ramp=T.land;
  P.set(x,y,P.pick(ramp,lit,x,y))}
 for(let x=0;x<w;x++)for(let y=0;y<h;y++){const d=Math.hypot(x+.5-cx,y+.5-cy);if(d>r-1&&d<=r+1.5){P.set(x,y,T.rim);break}}
 if(T.rings)for(let x=0;x<w;x++){const y=Math.round(14+Math.pow((x-cx)/cx,2)*-6+6);for(let k=0;k<3;k++)P.set(x,y+k,k===1?"#fff3c2":"#b88912")}
 return toCanvas(P)}
export const SKY=[null,["#010208","#040a1e","#0a1638","#14285a"],["#060104","#18040c","#2e0a14","#4a1420"],["#060302","#1a0e04","#30200a","#4a3410"],
 ["#070502","#1e1404","#3a280a","#5a4212"],["#050402","#161004","#2a2008","#40300e"],["#010309","#04102a","#08204a","#10306a"],["#05010a","#14041e","#2a0a36","#40104e"],
 ["#0a0300","#2a0a00","#4a1804","#702a06"],["#030406","#0c1018","#182030","#283448"],["#070204","#1a0608","#300c10","#481418"]];
export function buildSky(kind){const P=painter(310,180),base=SKY[kind]||SKY[1],s=kind+40;
 const vn=(x,y,c,k)=>{const X=Math.floor(x/c),Y=Math.floor(y/c),fx=x/c-X,fy=y/c-Y,u=fx*fx*(3-2*fx),v=fy*fy*(3-2*fy),a=hash(X,Y,k),b=hash(X+1,Y,k),d=hash(X,Y+1,k),e=hash(X+1,Y+1,k);return a+(b-a)*u+(d-a)*v+(a-b-d+e)*u*v};
 for(let y=0;y<180;y++)for(let x=0;x<310;x++){const n=vn(x,y,70,s)*.6+vn(x,y,22,s+5)*.4;P.set(x,y,P.pick(base,.05+.7*n*n*n,x,y))}return toCanvas(P)}
export function buildStars(seed){const P=painter(310,180);for(let i=0;i<70;i++){P.set(hash(i,1,seed)*310,hash(i,2,seed)*180,["#ffffff","#bcd2ff","#ffd0e8","#fff6b8"][i%4])}return toCanvas(P)}
export function buildAtlas(){const C=toCanvas;return{
 alien:[0,1].map(v=>[0,1].map(f=>C(alien(v,f)))),big:[0,1].map(f=>C(bigAlien(f))),ship:C(ship()),shipShield:C(ship(1)),boss:[1,2,3,4].map(t=>C(boss(t))),
 fighter:C(fighter()),interceptor:C(interceptor()),carrier:C(carrier()),repair:C(repair()),frigate:C(frigate()),
 sat:C(sat(false)),ufo:C(sat(true)),good:[0,1].map(f=>C(loot(true,f))),bad:[0,1].map(f=>C(loot(false,f))),orb:[0,1].map(f=>C(orb(f))),bug:[0,1].map(f=>C(bug(f))),glob:[0,1].map(f=>C(glob(f))),
 missile:C(missile()),rock:[0,1].map(i=>C(rock(i))),ice:[0,1,2,3].map(k=>C(iceBlock(k))),cloud:cloud(),
 icon:[0,1,2,3,4].map(k=>C(icon(k,false))),iconLocked:[0,1,2,3,4].map(k=>C(icon(k,true))),arrowL:C(arrow(-1)),arrowR:C(arrow(1)),moon:C(moon())}}
