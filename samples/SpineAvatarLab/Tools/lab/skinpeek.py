# skinpeek.py CAPTURE.png X0 Y0 X1 Y1 : skin-coloured pixels enclosed by hair in a box (the skull peeking between strands)
import sys, numpy as np
from PIL import Image
from scipy import ndimage
path = sys.argv[1]; x0, y0, x1, y1 = (int(v) for v in sys.argv[2:6])
im = np.array(Image.open(path).convert('RGB')).astype(int)[y0:y1, x0:x1]
r, g, b = im[..., 0], im[..., 1], im[..., 2]
skin = (r > g) & (g > b) & (r > 150) & (r - b > 40); hair = (b > r + 10) & (b > g)
# skin pixels inside the hair's filled silhouette (holes of the hair mask), not the open face
hair_filled = ndimage.binary_fill_holes(ndimage.binary_closing(hair, iterations=3))
enclosed = skin & hair_filled & ~ndimage.binary_dilation(~hair_filled, iterations=12)
print(f'{path.split("/")[-1]}: skin px enclosed by hair {int(enclosed.sum())} (of {int(skin.sum())} skin px in the box)')
