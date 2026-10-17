# haircolor.py IMAGE [ROWFRAC=0.6] : the hair's colour statistics (bluish, saturated pixels in the upper ROWFRAC of the image):
# luminance percentiles, share of pale (lum > 170) and dark (lum < 60) pixels, mean HSV saturation, median hue. References and lab captures alike.
import sys, numpy as np
from PIL import Image
path = sys.argv[1]; rowfrac = float(sys.argv[2]) if len(sys.argv) > 2 else 0.6
im = np.array(Image.open(path).convert('RGB')).astype(float); H = im.shape[0]; im = im[:int(H * rowfrac)]
r, g, b = im[..., 0], im[..., 1], im[..., 2]; mx = im.max(-1); mn = im.min(-1); sat = (mx - mn) / np.maximum(mx, 1)
hair = (b > r + 12) & (b > g + 4) & (sat > 0.18) & (mx > 30)
lum = 0.3 * r + 0.59 * g + 0.11 * b; L = lum[hair]; Sv = sat[hair]
hue = np.degrees(np.arctan2(np.sqrt(3) * (g - b), 2 * r - g - b))[hair] % 360
print(f"{path.split('/')[-1]:28s} hair px {int(hair.sum()):7d} | lum p5 {np.percentile(L,5):5.0f} p25 {np.percentile(L,25):5.0f} p50 {np.percentile(L,50):5.0f} p75 {np.percentile(L,75):5.0f} p95 {np.percentile(L,95):5.0f} | pale(>170) {float((L>170).mean()):.3f} dark(<60) {float((L<60).mean()):.3f} | sat mean {Sv.mean():.2f} | hue median {np.median(hue):.0f}")
