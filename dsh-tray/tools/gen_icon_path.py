import re
import sys
import pathlib

SVG = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])

text = SVG.read_text(encoding="utf-8")
m = re.search(r'<path[^>]*\sd="([^"]+)"', text)
if not m:
    raise SystemExit("no path d attribute found in " + str(SVG))

d = m.group(1).strip()

vb = re.search(r'viewBox="([\d.\s-]+)"', text)
if not vb:
    raise SystemExit("no viewBox found")
_, _, vw, vh = [float(x) for x in vb.group(1).split()]

OUT.parent.mkdir(parents=True, exist_ok=True)

chunks = [d[i:i + 100] for i in range(0, len(d), 100)]
body = "\n".join('            "%s" +' % c for c in chunks)
body = body.rstrip(" +")

OUT.write_text(
    "namespace DshTray\n"
    "{\n"
    "    internal static class BrandGlyph\n"
    "    {\n"
    "        internal const float ViewBoxWidth = %sf;\n"
    "        internal const float ViewBoxHeight = %sf;\n"
    "\n"
    "        internal const string PathData =\n"
    "%s;\n"
    "    }\n"
    "}\n" % (vw, vh, body),
    encoding="utf-8",
)

print("commands used:", sorted(set(re.findall(r"[A-Za-z]", d))))
print("path length:", len(d))
print("viewBox:", vw, vh)
print("written:", OUT)
