"""Draw the original desktop application mark. Requires Pillow for regeneration."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1] / "assets"
root.mkdir(exist_ok=True)
image = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((4, 4, 252, 252), radius=62, fill="#146d5e")
draw.rounded_rectangle((55, 60, 170, 153), radius=12, outline="#ffffff", width=10)
draw.rounded_rectangle((86, 93, 201, 186), radius=12, outline="#bde4a9", width=10)
image.save(root / "desk.ico", sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
