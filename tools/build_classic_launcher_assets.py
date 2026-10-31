"""Rebuild approved skin assets from the read-only launcher UI audit export (Pillow)."""
import argparse,hashlib,json,pathlib,shutil
from PIL import Image,ImageDraw,ImageOps

parser=argparse.ArgumentParser();parser.add_argument('--audit-root',required=True);args=parser.parse_args()
audit=pathlib.Path(args.audit_root).resolve();repo=pathlib.Path(__file__).resolve().parents[1]
dest=repo/'src/DFLegacy.Launcher/Assets/Skin';records=[]
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def source(number):return Image.open(audit/'images'/f'image-{number:04d}.png').convert('RGBA')
def save(name,image,origins,operation):
 path=dest/('classic-'+name+'.png');image.save(path,optimize=True)
 records.append({'file':path.relative_to(repo).as_posix(),'sha256':digest(path),'pixels':list(image.size),
  'sources':[{'file':p.relative_to(audit).as_posix(),'sha256':digest(p)} for p in origins],'operation':operation})
def copy(name,path,operation):save(name,Image.open(path).convert('RGBA'),[path],operation)

login=Image.open(audit/'preview/assets/login.png').convert('RGBA')
map_panel=source(106).crop((70,144,518,282)).resize((533,218),Image.Resampling.LANCZOS)
login.alpha_composite(map_panel,(17,128));draw=ImageDraw.Draw(login)
draw.rectangle((17,136,549,345),outline=(80,48,12,255),width=1)
draw.rectangle((18,137,337,344),outline=(242,188,74,255),width=1)
save('login-background',login,[audit/'preview/assets/login.png',audit/'images/image-0106.png'],'approved revision2 map/news backplate; all labels, buttons and fields remain live controls')
banner_sources=[(7,(17,113,550,316)),(7,(200,115,510,314)),(7,(38,175,370,345)),(7,(151,126,505,345)),(106,(70,144,518,282))]
for index,(number,rect) in enumerate(banner_sources):
 save('banner'+str(index),ImageOps.fit(source(number).crop(rect),(315,202),method=Image.Resampling.LANCZOS),[audit/'images'/f'image-{number:04d}.png'],'manual banner crop; first crop matches approved revision2 exactly')
copy('settings-panel',audit/'preview/revision-2/settings-panel.png','approved480x428 full panel, clean border and single non-cyclic stone field')
for name in ['crest','small0','small1','small2','confirm0','confirm1','confirm2']:
 copy(name,audit/'preview/assets'/(name+'.png'),'approved source-derived UI asset; original images preserved in audit')
# Original RT_BITMAP311 operator labels and status legend. Only the painted,
# nonfunctional full-screen checkbox is replaced by the prior clean map patch.
server=source(106)
server.putdata([(r,g,b,0 if (r,g,b)==(255,0,255) else a) for r,g,b,a in server.get_flattened_data()])
clean=Image.open(audit/'preview/assets/server.png').convert('RGBA')
server.paste(clean.crop((417,372,526,398)),(417,372))
save('server',server,[audit/'images/image-0106.png',audit/'preview/assets/server.png'],
     'RT_BITMAP311 labels/legend preserved; remove only painted nonfunctional full-screen checkbox using existing clean map patch')
for index,number in enumerate([94,95,93]):
 image=source(number)
 image.putdata([(r,g,b,0 if (r,g,b)==(255,0,255) else a) for r,g,b,a in image.get_flattened_data()])
 save('row'+str(index),image,[audit/'images'/f'image-{number:04d}.png'],
      'RT_BITMAP265 default / 266 hover / 264 selected; magenta colour key to transparency')
for index,number in enumerate([102,103,104]):
 image=source(number);pixels=image.load()
 for y in range(image.height):
  for x in range(image.width):
   if pixels[x,y][:3]==(255,0,255):pixels[x,y]=(0,0,0,0)
 save('offline'+str(index),image,[audit/'images'/f'image-{number:04d}.png'],'original gray server state; magenta colour key to transparency')
# Original HintMsg.xml uses RT_BITMAP314 at native 286x170 with an 80x29 yes button.
message=source(56)
message.putdata([(r,g,b,0 if (r,g,b)==(255,0,255) else a) for r,g,b,a in message.get_flattened_data()])
save('message-panel',message,[audit/'images/image-0056.png'],
     'original RT_BITMAP314 panel at native286x170; magenta colour key to transparency')
for index in range(3):
 copy('message-confirm'+str(index),audit/'loose/data/ui'/('button_yes0'+str(index)+'.png'),
      'original HintMsg.xml yes-button state; native80x29 pixels unchanged')
# Approved update surfaces stay at native size; strip stretches only along its fill axis.
for name,number,description in [('update-panel',63,'DNFchina.exe RT_BITMAP198 native567x512'),
                                ('install-panel',115,'LauncherUpdator.exe RT_BITMAP132 native286x104'),
                                ('update-progress',81,'DNFchina.exe RT_BITMAP249 original progress texture')]:
 image=source(number)
 image.putdata([(r,g,b,0 if (r,g,b)==(255,0,255) else a) for r,g,b,a in image.get_flattened_data()])
 save(name,image,[audit/'images'/f'image-{number:04d}.png'],description+'; magenta colour key to transparency')
out=repo/'docs/superpowers/reports/2026-10-31-launcher-classic-assets.json'
out.write_text(json.dumps({'approved':'User accepted revision2 login/settings, original server page and RT_BITMAP314 message panel in chat','build_dependency':'none; all PNG files checked in and embedded by WPF','assets':records},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'embedded_png_files':len(records),'manifest':str(out)}))
