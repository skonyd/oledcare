"""assets/oledcare.ico dosyasını üretir (monitör + yeşil durum noktası)."""
from PIL import Image, ImageDraw

def draw(size: int) -> Image.Image:
    s = size / 32
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    frame, screen, dot = (235, 235, 235, 255), (20, 20, 20, 255), (46, 160, 67, 255)
    d.rectangle([2 * s, 4 * s, 30 * s - 1, 23 * s - 1], fill=frame)
    d.rectangle([4 * s, 6 * s, 28 * s - 1, 21 * s - 1], fill=screen)
    d.rectangle([13 * s, 23 * s, 19 * s - 1, 26 * s - 1], fill=frame)
    d.rectangle([9 * s, 26 * s, 23 * s - 1, 28 * s - 1], fill=frame)
    d.ellipse([17 * s, 15 * s, 31 * s - 1, 29 * s - 1], fill=dot, outline=(255, 255, 255, 255), width=max(1, round(1.5 * s)))
    return img

big = draw(256)
big.save("assets/oledcare.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("assets/oledcare.ico yazıldı")
