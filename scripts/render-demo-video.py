#!/usr/bin/env python3
"""Render a 60-second captioned slideshow of real demo screenshots. Requires Pillow, imageio-ffmpeg."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import imageio_ffmpeg,subprocess
root=Path(__file__).resolve().parents[1]
shots=root/'portfolio/screenshots'
out=root/'artifacts/pharmacy-pos-demo-60s.mp4';out.parent.mkdir(exist_ok=True)
scenes=[
 ('01-demo-login.jpg','Try the demo without a password','Choose admin or operator. Fictional data only.'),
 ('02-catalogue.jpg','Find and manage medicines','Catalogue, classifications and stock receiving.'),
 ('03-barcode.jpg','Scan the exact pack','Barcode lookup identifies a piece, strip or box; staff confirm the batch.'),
 ('04-cart.jpg','Build the customer cart','Pack quantities convert to stock units. Prices are checked at checkout.'),
 ('05-split-payment.jpg','Record split payments','Cash, Card, bKash and Nagad. Payments are completed externally.'),
 ('06-receipt.jpg','Save a printable receipt','Payment references and the 15-day return deadline stay on the receipt.'),
 ('07-returns.jpg','Track returns and refunds','Whole receipt lines; returned stock is held for admin resale approval.'),
 ('08-alerts.jpg','Watch expiry dates','Near-expiry alerts help staff review batches before they expire.'),
 ('09-reports.jpg','Review sales and payment totals','Admin reports and CSV exports support daily review.'),
 ('10-operator.jpg','Receive stock with role controls','Operator submissions need admin approval. Explore more in the demo.')]
fontpath='/System/Library/Fonts/Supplemental/Arial.ttf'
font=ImageFont.truetype(fontpath,30);small=ImageFont.truetype(fontpath,19)
proc=subprocess.Popen([imageio_ffmpeg.get_ffmpeg_exe(),'-y','-f','rawvideo','-vcodec','rawvideo','-pix_fmt','rgb24','-s','1280x800','-r','15','-i','-','-an','-c:v','libx264','-preset','fast','-crf','21','-pix_fmt','yuv420p','-movflags','+faststart',str(out)],stdin=subprocess.PIPE,stderr=subprocess.DEVNULL)
for index,(file,title,subtitle) in enumerate(scenes):
 frame=Image.new('RGB',(1280,800),'#102d28');draw=ImageDraw.Draw(frame)
 draw.text((28,15),'PHARMACY POS  /  SHAMEER AZMI',font=small,fill='#b5dccb')
 draw.text((28,43),title,font=font,fill='white')
 im=Image.open(shots/file).convert('RGB');im.thumbnail((1224,620),Image.Resampling.LANCZOS)
 frame.paste(im,((1280-im.width)//2,95+(620-im.height)//2))
 draw.text((28,735),subtitle,font=small,fill='white')
 draw.text((28,769),'Edited real-app walkthrough • Fictional demo • MIT license',font=small,fill='#b5dccb')
 draw.text((1160,769),f'{index+1:02}/10',font=small,fill='#b5dccb')
 if index==2:frame.save(root/'portfolio/demo-poster.jpg',quality=92)
 for _ in range(90):proc.stdin.write(frame.tobytes())
proc.stdin.close();assert proc.wait()==0
print(out)
