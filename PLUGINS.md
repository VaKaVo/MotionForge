# Плагины MotionForge / MotionForge Plugins

**Русский** · [English](#english)

---

## Русский

MotionForge можно расширять плагинами: программа сама почти ничего не знает о вашем плагине —
она просто запускает его, отдаёт проект и забирает результат. Писать плагины можно на Python
(или на чём угодно ещё), не пересобирая программу.

### 1. Где лежат плагины

```
MotionForge.exe
Plugins\
  stars\        ← пример: «Звёздное небо»
    plugin.json
    stars.py
  outline\      ← пример: «Жирная обводка»
    plugin.json
    outline.py
  _template\    ← шаблон: скопируйте, переименуйте (уберите «_») и правьте
```

Папки, имя которых начинается с `_` или `.`, программа **не загружает** — это место для
шаблонов и экспериментов.

Открыть папку и перечитать список можно из меню **Плагины → Открыть папку плагинов /
Обновить список плагинов** или в **Настройки → Плагины**.

### 2. Файл `plugin.json`

```json
{
  "id": "stars",
  "name": "Звёздное небо",
  "version": "1.0",
  "author": "MotionForge",
  "description": "Добавляет случайные звёзды на текущий кадр.",
  "type": "python",
  "command": "stars.py",
  "arguments": [],
  "timeout": 30
}
```

| Поле          | Что значит                                                              |
|---------------|-------------------------------------------------------------------------|
| `id`          | Короткий идентификатор (латиницей). Попадает в команду `plugin.<id>`.    |
| `name`        | Название пункта меню (можно по-русски).                                 |
| `name_en`     | Необязательное английское название: показывается, когда интерфейс не русский. |
| `version`     | Версия плагина (для порядка).                                           |
| `author`      | Автор.                                                                  |
| `description` | Описание — видно в окне «Плагины…».                                     |
| `type`        | `python` — запустить через Python; `command` — запустить файл как есть. |
| `command`     | Файл относительно папки плагина (`stars.py`, `tool.exe`, `run.bat`).    |
| `arguments`   | Массив дополнительных аргументов командной строки.                      |
| `timeout`     | Сколько секунд ждать плагин, потом он будет остановлен.                 |

### 3. Как программа вызывает плагин

```
python stars.py --input <файл проекта> --output <файл результата> --frame <кадр>
```

* `--input` — JSON текущего проекта (ровно тот же формат, что и файл `.mforge`).
* `--output` — сюда плагин **должен** записать изменённый проект.
* `--frame` — номер текущего кадра (с нуля), чтобы плагин знал, что сейчас на сцене.

Правила:

* Если файл `--output` не создан — программа считает, что плагин ничего не изменил.
* Если плагин завершился с ошибкой (код ≠ 0) — проект остаётся прежним, а всё, что плагин
  напечатал в stdout/stderr, показывается в окне отчёта.
* Всё, что плагин печатает (например, `print("Готово")`), тоже попадает в отчёт.
* Изменения применяются как одна операция: их можно отменить через **Ctrl+Z**.
* Рабочая папка плагина — его собственная папка, поэтому рядом можно держать данные и модули.

Минимальный плагин:

```python
import argparse, json

parser = argparse.ArgumentParser()
parser.add_argument("--input", required=True)
parser.add_argument("--output", required=True)
parser.add_argument("--frame", type=int, default=0)
args = parser.parse_args()

with open(args.input, encoding="utf-8") as f:
    project = json.load(f)

# ... что-то меняем в project ...

with open(args.output, "w", encoding="utf-8") as f:
    json.dump(project, f, ensure_ascii=False, indent=2)

print("Готово")
```

### 4. Формат проекта

```jsonc
{
  "Version": 1,
  "Id": "prj_1a2b3c",
  "Name": "Без названия",
  "Stage":   { "Width": 960, "Height": 600, "Background": "#FFFFFF", "Fps": 24 },
  "Timeline": {
    "Id": "scn_...", "Name": "Сцена", "Background": "",
    "Layers": [{
      "Id": "lyr_...", "Name": "Слой 1", "Color": "#4A90D9",
      "Visible": true, "Locked": false, "Outline": false,
      "Frames": [{
        "Id": "kf_...", "Index": 0, "Duration": 1,
        "Elements": [{
          "kind": "shape",
          "Id": "el_...", "Name": "",
          "X": 480, "Y": 300,          // положение центра фигуры
          "Rotation": 0, "ScaleX": 1, "ScaleY": 1, "Alpha": 1,
          "Visible": true, "Locked": false,
          "Points": [ { "X": -20, "Y": -10 }, { "X": 20, "Y": 0 } ],
          "Closed": false,
          "Fill": "#000000", "Stroke": "#141826", "StrokeWidth": 4,
          "Smooth": true, "Filled": true, "Stroked": true,
          "Brush": "",     // стиль кисти: pencil/brush/marker/calligraphy/airbrush
          "BrushAngle": 45
        }]
      }]
    }]
  },
  "Library": { }
}
```

Что важно знать:

* **Кадры — это ключевые кадры.** `Index` — с какого кадра начинается, `Duration` — сколько
  кадров держится. Кадр N показывает тот ключевой кадр, для которого
  `Index ≤ N ≤ Index + Duration − 1`.
* **Точки локальные.** `Points` задаются относительно центра (`X`, `Y`), поэтому переносить
  фигуру нужно через `X`/`Y`, а не через точки.
* **`kind` обязателен**: `shape` (фигура), `text`, `group`, `image`, `instance`.
* Отсутствующие поля берут значения по умолчанию, но `kind` нужен всегда.
* Идентификаторы — просто строки: `el_…`, `kf_…`, `lyr_…`. Генерируйте свои уникальные
  (например, `uuid4().hex[:12]`).
* Символы (мультиклипы) лежат в `Library`, у каждого свой `Timeline` той же структуры —
  можно менять и их.
* `Tween` у ключевого кадра (`{ "Type": "classic", "Ease": 0 }`) включает плавную
  интерполяцию до следующего ключевого кадра.

### 5. Идеи для плагинов

* Генераторы: звёзды, дождь, снег, трава, случайный шум, сетка перспективы.
* Эффекты: дрожание «от руки», пульсация, волна, разлёт, зеркальное отражение.
* Работа с кадрами: автоматическое создание твинов, копирование кадра через N кадров,
  переименование слоёв по шаблону.
* Импорт/экспорт: разобрать чужой формат, собрать SVG/Aseprite-подобный файл, разложить
  спрайт-лист по файлам.
* Проверки: найти пустые слои, фигуры за пределами сцены, дубликаты, слишком мелкие детали.

### 6. Отладка

1. Сохраните проект (**Ctrl+S**) — файл `.mforge` это тот же JSON, удобно смотреть глазами.
2. Запустите плагин вручную в консоли:

   ```
   cd Plugins\stars
   python stars.py --input C:\путь\проект.mforge --output test-out.json --frame 0
   ```

3. Откройте `test-out.json` (или импортируйте его как проект) и посмотрите результат.
4. Ошибки Python целиком попадают в окно отчёта MotionForge: имя файла, номер строки, текст.

Рекомендации: не падайте молча — печатайте, что сделано (`print("Звёзд добавлено:", n)`);
ограничивайте объём (`--count`), чтобы большие проекты оставались быстрыми.

### 7. Безопасность

Плагин — обычная программа, она запускается с вашими правами и может делать что угодно.
Ставьте и пишите плагины только те, которым доверяете. MotionForge никогда не скачивает
плагины из интернета сам.

---

## English

MotionForge is extended with plugins. The host knows almost nothing about your plugin: it runs
it, hands over the project, and takes the result back. Plugins are usually written in Python,
and no rebuild of the application is needed.

### Where plugins live

```
MotionForge.exe
Plugins\
  stars\        ← sample: "Starry sky"
    plugin.json
    stars.py
  outline\      ← sample: "Bold outline"
  _template\    ← template: copy it, drop the leading "_", edit
```

Folders starting with `_` or `.` are ignored by the host. Use **Plugins → Open plugins folder /
Reload plugin list**, or **Settings → Plugins**.

### `plugin.json`

```json
{
  "id": "stars",
  "name": "Starry sky",
  "version": "1.0",
  "author": "You",
  "description": "Adds random stars to the current frame.",
  "type": "python",
  "command": "stars.py",
  "arguments": [],
  "timeout": 30
}
```

`type` is `python` (run through the Python interpreter) or `command` (run the file directly).
`command` is relative to the plugin folder; `timeout` is in seconds. The optional `name_en`
field is shown when the interface language is not Russian.

### The call

```
python stars.py --input <project file> --output <result file> --frame <frame>
```

* `--input` — JSON of the current project, identical to the `.mforge` file format.
* `--output` — the plugin must write the modified project here.
* `--frame` — current frame number (0-based).

No output file means "no changes". A non-zero exit code keeps the project untouched and shows
stdout/stderr in a report window. Everything the plugin prints is shown there as well. Applied
changes are a single undo step (**Ctrl+Z**). The working directory is the plugin folder.

### Project format

Same shape as shown in the Russian section above: `Stage`, `Timeline` → `Layers` → `Frames`
(keyframes with `Index` / `Duration`) → `Elements`; `Library` holds symbols with their own
timelines. Elements carry a mandatory `kind` discriminator (`shape`, `text`, `group`, `image`,
`instance`); shape points are local to the element's `X`/`Y` centre.

### Debugging

Save the project (it *is* JSON), then run the plugin by hand:

```
cd Plugins\stars
python stars.py --input C:\path\project.mforge --output test-out.json --frame 0
```

Python tracebacks are reported in full by MotionForge, with file name and line number.
Print something useful (`print("Stars added:", n)`) and keep parameters like `--count` bounded.

### Security

A plugin is an ordinary program running with your privileges. Only install and run plugins you
trust. MotionForge never downloads plugins by itself.
