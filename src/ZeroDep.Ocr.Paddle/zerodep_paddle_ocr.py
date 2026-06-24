#!/usr/bin/env python3
"""ZeroDep <-> PaddleOCR bridge.

Reads one image and prints JSON to stdout:

    {"words": [{"text": "...", "conf": 0.0-1.0, "box": [x, y, w, h]}, ...]}

`box` is an axis-aligned bounding box in image pixels (origin top-left). Each PaddleOCR detection is a
text line; the C# adapter treats it as one unit (line for OcrResult, "word" for box-aligned scoring).

Usage:  python3 zerodep_paddle_ocr.py <image_path> <paddle_lang>
Requires:  pip install paddleocr   (tested with paddleocr 2.6-2.9)
"""
import json
import sys


def _bbox(points):
    xs = [float(p[0]) for p in points]
    ys = [float(p[1]) for p in points]
    x, y = min(xs), min(ys)
    return [int(round(x)), int(round(y)), int(round(max(xs) - x)), int(round(max(ys) - y))]


def _parse_legacy(result):
    """paddleocr 2.x: result = [ [ [box, (text, conf)], ... ] ] (per-image list)."""
    words = []
    page = result[0] if result and isinstance(result, list) else []
    for det in page or []:
        box, (text, conf) = det[0], det[1]
        if text:
            words.append({"text": text, "conf": float(conf), "box": _bbox(box)})
    return words


def _first_present(d, *keys):
    """Return the first key whose value is not None (avoids numpy-array truthiness on `or`)."""
    for k in keys:
        v = d.get(k)
        if v is not None:
            return v
    return []


def _parse_predict(result):
    """paddleocr 3.x: result = [ OCRResult ] (a dict subclass) with rec_texts / rec_scores / polys."""
    words = []
    for page in result or []:
        d = page if isinstance(page, dict) else getattr(page, "json", page)
        if isinstance(d, dict) and "res" in d:
            d = d["res"]
        texts = list(d.get("rec_texts", []))
        scores = list(d.get("rec_scores", []))
        polys = _first_present(d, "rec_polys", "dt_polys", "rec_boxes")
        for i, text in enumerate(texts):
            if not text:
                continue
            conf = float(scores[i]) if i < len(scores) else 0.0
            poly = polys[i] if i < len(polys) else [0, 0, 0, 0]
            pts = list(poly)
            # rec_boxes are flat [x1,y1,x2,y2]; polys are 4 points [[x,y],...].
            if len(pts) == 4 and not hasattr(pts[0], "__len__"):
                x1, y1, x2, y2 = (float(v) for v in pts)
                box = [int(round(x1)), int(round(y1)), int(round(x2 - x1)), int(round(y2 - y1))]
            else:
                box = _bbox(pts)
            words.append({"text": text, "conf": conf, "box": box})
    return words


def _recognize(ocr, image_path):
    """Run one image through whichever API this PaddleOCR version exposes."""
    if hasattr(ocr, "predict"):
        return _parse_predict(ocr.predict(image_path))   # 3.x pipeline API
    return _parse_legacy(ocr.ocr(image_path, cls=True))  # 2.x API


def _serve(lang):
    """Persistent worker: load the model once, then read one image path per stdin line and emit one
    JSON line per request on stdout. This amortizes PaddleOCR's heavy per-process init across a corpus.

    PaddleOCR logs freely to stdout, which would corrupt the protocol — so stdout is redirected to
    stderr for the duration and our JSON is written to the original stdout handle only."""
    from paddleocr import PaddleOCR

    real_out = sys.stdout
    sys.stdout = sys.stderr
    try:
        ocr = PaddleOCR(lang=lang)
    except Exception as ex:  # noqa: BLE001
        sys.stderr.write(f"paddleocr init failed: {type(ex).__name__}: {ex}\n")
        return 1

    real_out.write("READY\n")
    real_out.flush()

    while True:
        line = sys.stdin.readline()
        if not line:
            break
        path = line.strip()
        if not path:
            continue
        try:
            words = _recognize(ocr, path)
        except Exception as ex:  # noqa: BLE001
            sys.stderr.write(f"paddleocr failed on {path}: {type(ex).__name__}: {ex}\n")
            words = []
        real_out.write(json.dumps({"words": words or []}, ensure_ascii=False) + "\n")
        real_out.flush()

    return 0


def main():
    args = sys.argv[1:]

    # Persistent worker mode:  --serve <lang>
    if len(args) >= 2 and args[0] == "--serve":
        return _serve(args[1])

    # One-shot mode (handy for direct testing):  <image_path> <lang>
    if len(args) < 2:
        sys.stderr.write("usage: zerodep_paddle_ocr.py <image_path> <lang>  |  --serve <lang>\n")
        return 2

    image_path, lang = args[0], args[1]
    from paddleocr import PaddleOCR

    ocr = PaddleOCR(lang=lang)
    try:
        words = _recognize(ocr, image_path)
    except Exception as ex:  # noqa: BLE001
        sys.stderr.write(f"paddleocr failed: {type(ex).__name__}: {ex}\n")
        return 1

    json.dump({"words": words or []}, sys.stdout, ensure_ascii=False)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
