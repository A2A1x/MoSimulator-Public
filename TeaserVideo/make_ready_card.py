# Builds ReadyCard.png: the robot shot (ReadyRobot.png) with the Coming Soon card's logos, with "READY TO TEST".
# Run from the repo root: python TeaserVideo/make_ready_card.py
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

# Background: the card's clean right edge (x 1570+, no text or glow), mirror-tiled leftward.
strip = np.asarray(Image.open("TeaserVideo/SoonCard.png").convert("RGB"))[:, 1570:]
tiles, width = [], 0
while width < 1920:
    tiles.insert(0, strip if len(tiles) % 2 == 0 else strip[:, ::-1])
    width += strip.shape[1]
bg = np.concatenate(tiles, 1)[:, -1920:]

out = Image.fromarray(bg.astype(np.uint8)).convert("RGBA")

# Robot shot on the right, faded into the background on its left edge.
robot = Image.open("TeaserVideo/ReadyRobot.png").convert("RGBA")
robot = robot.resize((round(robot.width * 1080 / robot.height), 1080), Image.LANCZOS)
robot = robot.crop((45, 0, robot.width, 1080))  # drop the blue field pillar on the photo's left edge
fade = np.clip(np.arange(robot.width) / 300, 0, 1)
robot.putalpha(Image.fromarray((np.tile(fade, (1080, 1)) * 255).astype(np.uint8)))
out.alpha_composite(robot, (1920 - robot.width, 0))  # flush right

# Reuse the card's logo, title and MoSim logo (crops padded for their glow), stacked in a left column.
overlay = Image.open("TeaserVideo/SoonCard_transparent.png")
CX = 390

def paste(box, scale, cy, cx=CX):
    piece = overlay.crop(box)
    piece = piece.resize((round(piece.width * scale), round(piece.height * scale)), Image.LANCZOS)
    out.alpha_composite(piece, (round(cx - piece.width / 2), round(cy - piece.height / 2)))

paste((636, 45, 1296, 358), 0.75, 225)
paste((384, 339, 1572, 588), 0.6, 465)
paste((822, 715, 1205, 975), 0.62, 805, CX + 40)

font_path = "Assets/Imports/Fonts/nasalization rg(1).ttf"

def glow_text(img, text, cx, cy, size, tracking, fill=(232, 220, 255)):
    font = ImageFont.truetype(font_path, size)
    widths = [font.getlength(ch) for ch in text]
    x = cx - (sum(widths) + tracking * (len(text) - 1)) / 2
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for ch, cw in zip(text, widths):
        d.text((x, cy), ch, font=font, fill=fill + (255,), anchor="lm")
        x += cw + tracking
    glow = Image.new("RGBA", img.size, (150, 70, 255, 0))
    glow.putalpha(layer.getchannel("A").filter(ImageFilter.GaussianBlur(14)).point(lambda v: min(255, v * 2)))
    img.alpha_composite(glow)
    img.alpha_composite(layer)

glow_text(out, "READY TO TEST", CX, 612, 44, 20)
glow_text(out, "IN", CX - 132, 810, 28, 2)

out.convert("RGB").save("TeaserVideo/ReadyCard.png")
print("wrote TeaserVideo/ReadyCard.png")
