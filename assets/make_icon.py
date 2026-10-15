# 뉴탐색기 아이콘: 파란 둥근 사각형 + 2×2 칸마다 노란 폴더 (활성 칸 하나는 밝게)
from PIL import Image, ImageDraw
import os

def draw(size):
    S = 512
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((16, 16, S - 16, S - 16), radius=96, fill=(0, 92, 175, 255))
    d.rounded_rectangle((16, 16, S - 16, S // 2), radius=96, fill=(18, 112, 196, 255))
    d.rectangle((16, S // 2 - 96, S - 16, S // 2), fill=(18, 112, 196, 255))
    d.rounded_rectangle((16, 16, S - 16, S - 16), radius=96, outline=(0, 60, 120, 255), width=6)
    gap, pad = 26, 58
    cell = (S - pad * 2 - gap) // 2
    for r in range(2):
        for c in range(2):
            x0 = pad + c * (cell + gap)
            y0 = pad + r * (cell + gap)
            active = (r == 0 and c == 0)
            body = (255, 214, 92, 255) if active else (246, 190, 60, 255)
            back = (222, 160, 40, 255) if active else (205, 145, 30, 255)
            tab_w = cell * 0.42
            top = y0 + cell * 0.14
            # 폴더 뒷면 + 탭
            d.rounded_rectangle((x0, top, x0 + tab_w, top + cell * 0.2), radius=18, fill=back)
            d.rounded_rectangle((x0, top + cell * 0.1, x0 + cell, y0 + cell * 0.9), radius=22, fill=back)
            # 폴더 앞면
            d.rounded_rectangle((x0, top + cell * 0.24, x0 + cell, y0 + cell * 0.9), radius=22, fill=body)
            if active:
                d.rounded_rectangle((x0 - 8, y0 - 4, x0 + cell + 8, y0 + cell * 0.9 + 8), radius=28, outline=(255, 255, 255, 230), width=10)
    return im.resize((size, size), Image.LANCZOS)

here = os.path.dirname(os.path.abspath(__file__))
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
big = draw(256)
big.save(os.path.join(here, "app.ico"), sizes=[(s, s) for s in sizes], append_images=[draw(s) for s in sizes])
big.save(os.path.join(here, "app.png"))
print("ok")
