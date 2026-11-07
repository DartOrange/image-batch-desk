"""Create original geometric demo images. Requires Pillow only for regeneration."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1] / "demo" / "windows-x64" / "samples"
root.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 110)
small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 36)

im = Image.new("RGB", (2400, 1600), "#e7dfca")
d = ImageDraw.Draw(im)
d.ellipse((1000, -300, 2700, 1400), fill="#db764a")
d.polygon([(0, 1600), (1100, 450), (2200, 1600)], fill="#28594e")
d.polygon([(800, 1600), (1700, 700), (2600, 1600)], fill="#7b9c7a")
d.text((150, 160), "FIELD NOTES", fill="#18372f", font=font)
d.text((155, 320), "01 / A study in quiet landscapes", fill="#35564c", font=small)
im.save(root / "field-notes.jpg", quality=95)

im = Image.new("RGB", (1400, 2100), "#193a4b")
d = ImageDraw.Draw(im)
for y in range(250, 1900, 280):
    for x in range(-150, 1450, 280):
        d.arc((x, y, x + 420, y + 420), 180, 355, fill="#90b8bd", width=36)
d.ellipse((860, 170, 1210, 520), fill="#efbd75")
d.rectangle((85, 1450, 880, 1920), fill="#193a4b")
d.text((120, 1510), "TIDELINE", font=font, fill="#f2e9d5")
d.text((125, 1670), "02 / Rhythm in blue", font=small, fill="#a2c4c8")
im.save(root / "tideline.jpg", quality=95)

im = Image.new("RGBA", (1800, 1800), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
d.rounded_rectangle((120, 120, 1680, 1680), radius=300, fill="#e5ad60")
d.ellipse((350, 350, 1450, 1450), fill="#204c42")
d.polygon([(900, 380), (1070, 740), (1450, 900), (1070, 1060), (900, 1420), (730, 1060), (350, 900), (730, 740)], fill="#f2e9d5")
im.save(root / "sunmark.png")
im.resize((320, 320)).save(root / "sunmark-small.png")
print("Created 4 original samples in", root)
