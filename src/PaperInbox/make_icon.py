from pathlib import Path
from PIL import Image,ImageDraw

root=Path(__file__).resolve().parent
scale=4
image=Image.new('RGBA',(256*scale,256*scale))
draw=ImageDraw.Draw(image)
def box(values):return tuple(round(x*scale) for x in values)
draw.rounded_rectangle(box((7,7,249,249)),radius=56*scale,fill='#0071EB')
draw.rounded_rectangle(box((64,44,191,214)),radius=18*scale,fill='white')
draw.polygon([box((155,44)),box((191,80)),box((155,80))],fill='#C9E3FF')
draw.line([box((155,45)),box((155,80)),box((189,80))],fill='#73AEF0',width=3*scale)
for y,end in [(113,167),(139,167),(165,142)]:draw.rounded_rectangle(box((88,y,end,y+7)),radius=3*scale,fill='#68A6EF')
image=image.resize((256,256),Image.Resampling.LANCZOS)
image.save(root/'App.ico',format='ICO',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print('App.ico generated from vector geometry.')
