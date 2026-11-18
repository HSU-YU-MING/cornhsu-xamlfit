"""產生 icon.png（NuGet 套件圖示與 README 用）。

概念：一個文字位置（框），裡面兩行文字。上面那行裝得下；下面那行
伸出框外，伸出去的那一段用警告色 —— 這就是這個工具在回答的問題：
「翻譯之後，這個位置裝不裝得下」。

風格跟家族其他套件一致（XamlKeys、Parity、XamlContrast）：
圓角方塊、扁平、單一概念圖形。

重新產生：py -3 scripts/make-icon.py
（以 4 倍大小繪製再縮小，邊緣才會平滑。）
"""

from PIL import Image, ImageDraw

SIZE = 256          # 輸出尺寸
S = 4               # 超取樣倍率
N = SIZE * S

BG = (24, 27, 33, 255)          # 深炭灰（跟 XamlKeys 同一個底）
FRAME = (120, 190, 230, 255)    # 框：淡天藍
FIT = (235, 238, 242, 255)      # 裝得下的那行：近白
OVER = (255, 112, 84, 255)      # 溢出去的那段：珊瑚紅


def main():
    # 圓角方塊底
    out = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(out)
    d.rounded_rectangle([0, 0, N - 1, N - 1], radius=int(N * 0.19), fill=BG)

    # 文字框：偏左放，右邊留空間給溢出的那段
    fx0, fy0 = int(N * 0.14), int(N * 0.22)
    fx1, fy1 = int(N * 0.70), int(N * 0.78)
    stroke = int(N * 0.040)
    d.rounded_rectangle([fx0, fy0, fx1, fy1], radius=int(N * 0.06),
                        outline=FRAME, width=stroke)

    bar_h = int(N * 0.085)
    bar_x0 = fx0 + stroke + int(N * 0.07)
    inner_right = fx1 - stroke - int(N * 0.07)

    # 第一行：裝得下，停在框內
    y1 = int(N * 0.385)
    d.rounded_rectangle([bar_x0, y1 - bar_h // 2, inner_right - int(N * 0.08), y1 + bar_h // 2],
                        radius=bar_h // 2, fill=FIT)

    # 第二行：伸出框外。框內的一段近白，壓過右邊框線之後換成珊瑚紅
    y2 = int(N * 0.615)
    bar_end = int(N * 0.90)
    d.rounded_rectangle([bar_x0, y2 - bar_h // 2, bar_end, y2 + bar_h // 2],
                        radius=bar_h // 2, fill=OVER)
    # 用一個方角的近白段蓋回框內的部分，分界剛好落在框線內緣
    d.rectangle([bar_x0 + bar_h // 2, y2 - bar_h // 2, fx1 - stroke, y2 + bar_h // 2], fill=FIT)
    d.ellipse([bar_x0, y2 - bar_h // 2, bar_x0 + bar_h, y2 + bar_h // 2], fill=FIT)
    # 框線本身要畫在溢出段上面，讓紅色段看起來是「穿過去」
    d.rectangle([fx1 - stroke, y2 - bar_h // 2, fx1, y2 + bar_h // 2], fill=FRAME)

    out = out.resize((SIZE, SIZE), Image.LANCZOS)
    out.save("icon.png", optimize=True)
    print(f"wrote icon.png ({SIZE}x{SIZE})")


if __name__ == "__main__":
    main()
