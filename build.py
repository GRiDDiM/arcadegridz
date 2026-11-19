import json, base64, subprocess, os, sys, numpy as np
SRC=sys.argv[1]; FPS=25
order=[("8BIT_ROCKETSHIPS.mp3","8-Bit Rocketship")]+[(f"8BIT_ROCKETSHIP{i}.mp3",f"Rocketship {r}") for i,r in zip(range(2,9),["II","III","IV","V","VI","VII","VIII"])]
tracks=[]
for n,(f,title) in enumerate(order,1):
    slug=f"{n:02d}-"+title.lower().replace(" ","-")
    out=f"out/{slug}.mp3"
    subprocess.run(["ffmpeg","-y","-loglevel","error","-i",os.path.join(SRC,f),"-map","0:a","-c","copy","-map_metadata","-1",
      "-metadata",f"title={title}","-metadata","album=8-BIT ROCKETSHIP","-metadata","artist=BDC","-metadata","date=2008",
      "-metadata",f"track={n}/{len(order)}","-metadata","genre=Chiptune",out],check=True)
    raw=subprocess.run(["ffmpeg","-v","quiet","-i",out,"-ac","2","-ar","44100","-f","f32le","-"],capture_output=True).stdout
    x=np.frombuffer(raw,dtype=np.float32).reshape(-1,2); sr=44100; hop=sr//FPS; k=len(x)//hop
    p=np.abs(x[:k*hop]).reshape(k,hop,2).max(1); db=20*np.log10(np.maximum(p,1e-5))
    b=(np.clip((db+48)/48,0,1)*255).round().astype(np.uint8)
    tracks.append({"n":n,"title":title,"file":os.path.basename(out),"duration":round(len(x)/sr,2),"fps":FPS,
      "peaksL":base64.b64encode(b[:,0].tobytes()).decode(),"peaksR":base64.b64encode(b[:,1].tobytes()).decode()})
    print(n,title,round(len(x)/sr,1),os.path.getsize(out)//1024,"KB")
album={"id":"8bit-rocketship","title":"8-BIT ROCKETSHIP","subtitle":"Chiptunes 2008","artist":"BDC","year":2008,
 "cover":"cover.png","base":"https://arcadegridz.com/music/8bit-rocketship/","tracks":tracks}
json.dump(album,open("out/album.json","w"))
