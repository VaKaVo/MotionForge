"""Шаблон плагина MotionForge.

Как пользоваться
----------------
1. Скопируйте папку `_template` рядом с программой (каталог Plugins) под новым именем.
2. Уберите знак «_» в начале имени папки — тогда программа увидит плагин.
3. Отредактируйте plugin.json (название, описание, command) и этот файл.
4. Меню «Плагины» → «Обновить список плагинов».

Как программа общается с плагином
---------------------------------
MotionForge запускает:  python plugin.py --input <файл> --output <файл> --frame <кадр>
  --input   готовый JSON текущего проекта (тот же формат, что и файл .mforge);
  --output  сюда плагин должен записать изменённый проект;
  --frame   номер текущего кадра (с 0), чтобы знать, что сейчас показывает сцена.

Если файл --output не создан, программа считает, что плагин ничего не изменил.
Если плагин завершился с ненулевым кодом, его вывод (stdout/stderr) покажется в окне
отчёта, а проект останется прежним. Всё, что печатает плагин, тоже видно в отчёте.
"""

import argparse
import json
import uuid


def new_id(prefix):
    """Свежий идентификатор: буквы префикса задают тип объекта (el, kf, lyr...)."""
    return prefix + "_" + uuid.uuid4().hex[:12]


def keyframe_at(scene, frame):
    """Ключевой кадр слоя, который показывается в кадре `frame`."""
    for layer in scene.get("Layers", []):
        for keyframe in layer.get("Frames", []):
            start = keyframe.get("Index", 0)
            end = start + max(1, keyframe.get("Duration", 1)) - 1
            if start <= frame <= end:
                return keyframe
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--frame", type=int, default=0)
    args = parser.parse_args()

    with open(args.input, "r", encoding="utf-8-sig") as handle:
        project = json.load(handle)

    # --- здесь ваша логика -------------------------------------------------
    # Пример: сдвинуть все фигуры текущего кадра на 10 пикселей вправо.
    keyframe = keyframe_at(project.get("Timeline") or {}, args.frame)
    if keyframe:
        for element in keyframe.get("Elements", []):
            element["X"] = element.get("X", 0) + 10
    # ----------------------------------------------------------------------

    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(project, handle, ensure_ascii=False, indent=2)

    print("Плагин выполнен, кадр {}".format(args.frame))


if __name__ == "__main__":
    main()
