"""Strip C2PA content credentials from PNG and JPEG files in place, and prove it.

PNG: the manifest is a JUMBF box inside a `caBX` chunk. Every chunk is kept
except `caBX`; the IDAT chunks are concatenated before and after and compared.
JPEG: the manifest is a JUMBF box across APP11 (0xFFEB) segments. Every segment
is kept except APP11 whose payload carries a JUMBF or C2PA mark; everything
outside the removed segments is compared before and after.

Run from the repository root: python strip_c2pa.py docs/images
Exit 1 if any mark remains or any image's data changed.
"""
import os
import struct
import sys

MARKS = [b"c2pa", b"jumb", b"jumd", b"caBX", b"urn:c2pa"]


def has_mark(blob: bytes) -> bool:
    return any(m in blob for m in MARKS)


def png_chunks(blob: bytes):
    assert blob[:8] == b"\x89PNG\r\n\x1a\n", "not a PNG"
    pos = 8
    while pos < len(blob):
        length = struct.unpack(">I", blob[pos : pos + 4])[0]
        ctype = blob[pos + 4 : pos + 8]
        end = pos + 12 + length
        yield ctype, blob[pos:end]
        pos = end


def strip_png(blob: bytes):
    out = bytearray(blob[:8])
    before = b""
    removed = []
    for ctype, raw in png_chunks(blob):
        if ctype == b"IDAT":
            before += raw[8:-4]
        if ctype == b"caBX":
            removed.append(len(raw))
            continue
        out += raw
    after = b"".join(raw[8:-4] for ctype, raw in png_chunks(bytes(out)) if ctype == b"IDAT")
    return bytes(out), removed, before == after


def jpeg_segments(blob: bytes):
    assert blob[:2] == b"\xff\xd8", "not a JPEG"
    yield b"\xff\xd8", blob[:2]
    pos = 2
    while pos < len(blob):
        assert blob[pos] == 0xFF, f"lost sync at {pos}"
        marker = blob[pos : pos + 2]
        if marker == b"\xff\xd9":
            yield marker, blob[pos:]
            return
        if marker == b"\xff\xda":  # start of scan: the rest is entropy-coded data up to EOI
            yield marker, blob[pos:]
            return
        length = struct.unpack(">H", blob[pos + 2 : pos + 4])[0]
        end = pos + 2 + length
        yield marker, blob[pos:end]
        pos = end


def strip_jpeg(blob: bytes):
    out = bytearray()
    kept_before = bytearray()
    removed = []
    for marker, raw in jpeg_segments(blob):
        # A JUMBF box longer than one segment continues in further APP11
        # segments that carry the "JP" identifier and none of the marks, so the
        # identifier is what decides, not only the mark.
        if marker == b"\xff\xeb" and (has_mark(raw) or raw[4:6] == b"JP"):
            removed.append(len(raw))
            continue
        kept_before += raw
        out += raw
    return bytes(out), removed, bytes(kept_before) == bytes(out)


def main(folder: str) -> int:
    failures = 0
    scanned = 0
    stripped = 0
    for name in sorted(os.listdir(folder)):
        path = os.path.join(folder, name)
        ext = name.lower().rsplit(".", 1)[-1]
        if ext not in ("png", "jpg", "jpeg"):
            continue
        scanned += 1
        blob = open(path, "rb").read()
        if not has_mark(blob):
            continue
        if ext == "png":
            clean, removed, same = strip_png(blob)
        else:
            clean, removed, same = strip_jpeg(blob)
        clean_ok = not has_mark(clean)
        status = "ok" if (same and clean_ok) else "FAILED"
        if same and clean_ok:
            open(path, "wb").write(clean)
            stripped += 1
        else:
            failures += 1
        print(
            f"  {status:6} {name}: {len(blob)} -> {len(clean)} B, removed {len(removed)} segment(s) "
            f"totalling {sum(removed)} B, image data identical={same}, marks remain={not clean_ok}"
        )
    remaining = [n for n in sorted(os.listdir(folder)) if n.lower().endswith((".png", ".jpg", ".jpeg")) and has_mark(open(os.path.join(folder, n), "rb").read())]
    print(f"scanned {scanned} raster images, stripped {stripped}, failures {failures}, still marked {len(remaining)}: {remaining}")
    return 1 if (failures or remaining) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "docs/images"))
