# capthrough.py FLAT_IMAGE : in a solo.py render with flat=cards:1,0,1 flat=cap:1,0.5,0, the share of the hair's silhouette
# (cards + cap) where the cap shows: holes in the hair and cap fragments (owner: "trasiga lockar, random trianglar").
import sys, numpy as np
from PIL import Image
a = np.asarray(Image.open(sys.argv[1]).convert('RGB')).astype(int); r, g, b = a[..., 0], a[..., 1], a[..., 2]
cards = (r > 200) & (g < 90) & (b > 200)
cap = (r > 200) & (g > 150) & (b < 90)
print(f"{sys.argv[1].split('/')[-1]:24s} hair px {int(cards.sum()):7d} cap px {int(cap.sum()):6d} cap share {cap.sum() / max(cards.sum() + cap.sum(), 1):.4f}")
