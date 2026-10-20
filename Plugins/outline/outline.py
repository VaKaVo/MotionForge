"""Жирная обводка — второй пример плагина MotionForge.

Плагин проходит по всем фигурам текущего кадра и включает у них обводку,
чтобы рисунок выглядел в едином «мультяшном» стиле.

Полезно смотреть как пример работы с уже существующими элементами:
изменяем только нужные поля и возвращаем проект целиком.
"""

import argparse
import json

# Настройки плагина: их можно менять, а можно вынести в plugin.json → arguments
OUTLINE_COLOR = "#1B2233"
OUTLINE_WIDTH = 3.0
SKIP_ALREADY_OUTLINED = False


def keyframes_at(scene, frame):
    """Все ключевые кадры (по слоям), которые показываются в этом кадре."""
    found = []
    for layer in scene.get("Layers", []):
        for keyframe in layer.get("Frames", []):
            start = keyframe.get("Index", 0)
            end = start + max(1, keyframe.get("Duration", 1)) - 1
            if start <= frame <= end:
                found.append((layer, keyframe))
    return found


def main():
    parser = argparse.ArgumentParser(description="Обводка фигур для MotionForge")
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--frame", type=int, default=0)
    parser.add_argument("--color", default=OUTLINE_COLOR)
    parser.add_argument("--width", type=float, default=OUTLINE_WIDTH)
    args = parser.parse_args()

    with open(args.input, "r", encoding="utf-8-sig") as handle:
        project = json.load(handle)

    changed = 0
    for _layer, keyframe in keyframes_at(project.get("Timeline") or {}, args.frame):
        for element in keyframe.get("Elements", []):
            if element.get("kind") != "shape":
                continue
            if SKIP_ALREADY_OUTLINED and element.get("Stroked"):
                continue
            element["Stroked"] = True
            element["Stroke"] = args.color
            element["StrokeWidth"] = args.width
            changed += 1

    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(project, handle, ensure_ascii=False, indent=2)

    print("Фигур обведено: {}".format(changed))


if __name__ == "__main__":
    main()
