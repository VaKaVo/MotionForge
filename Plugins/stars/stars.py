"""Звёздное небо — пример плагина MotionForge.

Что делает плагин:
  * получает проект во входном JSON-файле (--input);
  * ищет ключевой кадр текущего кадра (--frame) в верхнем слое;
  * добавляет в него случайные звёзды (замкнутые фигуры с заливкой);
  * записывает изменённый проект в выходной файл (--output).

Запуск: меню «Плагины» → «Звёздное небо».

Формат обмена: обычный JSON проекта MotionForge (те же имена полей, что и в .mforge).
"""

import argparse
import json
import math
import random
import uuid


def new_id(prefix):
    """Идентификаторы в проекте — строки, начинающиеся с префикса типа объекта."""
    return prefix + "_" + uuid.uuid4().hex[:12]


def star_points(cx, cy, outer, inner, points=5):
    """Координаты звёздочки (локальные, относительно её центра)."""
    result = []
    for i in range(points * 2):
        radius = outer if i % 2 == 0 else inner
        angle = -math.pi / 2 + i * math.pi / points
        result.append({"X": cx + math.cos(angle) * radius,
                       "Y": cy + math.sin(angle) * radius})
    return result


def keyframe_for(scene, frame):
    """Ключевой кадр, который сейчас показывается (или None)."""
    for layer in scene.get("Layers", []):
        for keyframe in layer.get("Frames", []):
            start = keyframe.get("Index", 0)
            end = start + max(1, keyframe.get("Duration", 1)) - 1
            if start <= frame <= end:
                return layer, keyframe
    return None, None


def main():
    parser = argparse.ArgumentParser(description="Звёздное небо для MotionForge")
    parser.add_argument("--input", required=True, help="файл проекта до изменения")
    parser.add_argument("--output", required=True, help="куда записать изменённый проект")
    parser.add_argument("--frame", type=int, default=0, help="текущий кадр")
    parser.add_argument("--count", type=int, default=40, help="сколько звёзд добавить")
    parser.add_argument("--color", default="#FFD166", help="цвет звёзд")
    args = parser.parse_args()

    with open(args.input, "r", encoding="utf-8-sig") as handle:
        project = json.load(handle)

    scene = project.get("Timeline") or {}
    stage = project.get("Stage") or {}
    width = float(stage.get("Width", 960))
    height = float(stage.get("Height", 600))

    layer, keyframe = keyframe_for(scene, args.frame)
    if keyframe is None:
        if not scene.get("Layers"):
            scene["Layers"] = [{"Id": new_id("lyr"), "Name": "Слой 1", "Color": "#4A90D9",
                                "Visible": True, "Locked": False, "Outline": False, "Frames": []}]
        layer = scene["Layers"][0]
        layer.setdefault("Frames", [])
        keyframe = {"Id": new_id("kf"), "Index": args.frame, "Duration": 1,
                    "Elements": []}
        layer["Frames"].append(keyframe)

    elements = keyframe.setdefault("Elements", [])
    random.seed(args.frame * 7919 + len(elements))

    for _ in range(max(1, args.count)):
        x = random.uniform(width * 0.04, width * 0.96)
        y = random.uniform(height * 0.04, height * 0.5)
        size = random.uniform(3.0, 9.0)
        elements.append({
            "kind": "shape",
            "Id": new_id("el"),
            "Name": "Звезда",
            "X": x,
            "Y": y,
            "Rotation": random.uniform(0, 360),
            "ScaleX": 1,
            "ScaleY": 1,
            "Alpha": random.uniform(0.55, 1.0),
            "Visible": True,
            "Locked": False,
            "Points": star_points(0, 0, size, size * 0.42),
            "Closed": True,
            "Fill": args.color,
            "Stroke": args.color,
            "StrokeWidth": 1,
            "Smooth": False,
            "Filled": True,
            "Stroked": False,
        })

    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(project, handle, ensure_ascii=False, indent=2)

    print("Звёзд добавлено: {}".format(max(1, args.count)))
    print("Кадр: {}".format(args.frame))


if __name__ == "__main__":
    main()
