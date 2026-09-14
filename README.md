# Image Flasher

Image Flasher is a small Windows application for Counter-Strike 2 that shows a configurable visual flash and optional sound when a kill is detected through Game State Integration (GSI).

## Features

* Kill detection through CS2 GSI
* Flash only when CS2 is the active window
* Custom images
* Stretch or preserve image proportions
* Adaptive background for proportional mode
* Adjustable flash duration and hold time
* Adjustable flash opacity
* Optional sound on kill
* Supports WAV sounds
* Sound playback can overlap between rapid kills
* Optional debug mode
* Russian / English interface
* Custom accent color
* White-flash fallback when no valid image is available

## Requirements

* Windows 10 or later
* Counter-Strike 2

## Installation

Download the release ZIP, extract it, and run:

`ImageFlasher.exe`

Keep the files from the ZIP together.

The application creates these automatically when needed:

`settings.json`

`Images/`

`Sounds/`

## Images

Open **Settings → Open folder** and put your images into the `Images` folder.

Supported formats:

* PNG
* JPG / JPEG
* BMP
* GIF
* TIF / TIFF

Press **Refresh** after adding or removing images.

## Sounds

Open **Settings → Open folder** in the Sound section and put one sound file into the `Sounds` folder.

Supported formats:

* WAV

Only one sound is used. If multiple supported sound files are present, the first file by name is selected.

Use **Enable sound** to turn sound playback on or off.

Rapid kills can overlap the sound instead of interrupting the previous playback.

Press **Refresh** after adding, removing, or replacing the sound file.

## Settings

| Setting | Description |
|-|-|
| Enable flash | Enables or disables the flash |
| Flash only when CS2 is active | Shows kill flashes only when the CS2 window is active |
| Preserve proportions | Keeps the original image aspect ratio |
| Duration | Total flash duration |
| Hold | Full-brightness time before fading |
| Opacity | Maximum flash opacity |
| Enable sound | Enables or disables the kill sound |
| Show Debug | Shows the debug label |
| Accent | Changes the interface accent color |
| Language | Switches between Russian and English |

Test image can be used from Settings or the tray menu even when CS2 is not the active window.

## CS2 GSI

Image Flasher uses `CounterStrike2GSI` and starts a local GSI listener on port `3000`.

The required GSI configuration is generated automatically.

If kills are not detected, make sure CS2 and Image Flasher are running and check the connection status in Settings.

## Files

Image Flasher/
├── ImageFlasher.exe
├── settings.json
├── Images/
├── Sounds/
└── other application files

`settings.json` contains the application settings. `Images/` contains the flash images. `Sounds/` contains the optional kill sound.

## Troubleshooting

### No flash after a kill

Check that:

* **Enable flash** is enabled.
* CS2 is running and is the active window.
* The connection status shows CS2 as connected.
* The GSI listener started successfully.

### Images are not displayed

Make sure the files use a supported format and press **Refresh**.

Corrupted images are skipped automatically.

### The image is distorted

Enable **Preserve proportions**.

### No sound is played

Check that:

* **Enable sound** is enabled.
* A supported `.wav` file is present in `Sounds/`.
* **Refresh** was pressed after adding or replacing the sound file.

## VAC / CS2 Safety

Image Flasher does **not** modify CS2 files or inject code into the game. It uses the official **Game State Integration (GSI)** interface developed by Valve to receive game-state data.

GSI is used for external tools, statistics and broadcast systems, including competitive Counter-Strike events and Major tournament broadcasts.

Image Flasher only receives data through GSI and displays its own overlay, so it does not provide a gameplay advantage or alter the game.

There is no known reason for Image Flasher itself to cause a VAC or CS2 ban.

---

# Image Flasher

Image Flasher — небольшое приложение для Windows, которое показывает настраиваемую визуальную вспышку и дополнительный звук при обнаружении убийства в Counter-Strike 2 через Game State Integration (GSI).

## Возможности

* Обнаружение убийств через CS2 GSI
* Вспышка только при активном окне CS2
* Пользовательские изображения
* Растягивание или сохранение пропорций
* Адаптивный фон в режиме сохранения пропорций
* Настройка длительности и удержания вспышки
* Настройка прозрачности вспышки
* Дополнительный звук при убийстве
* Поддержка WAV
* Наложение звуков при быстрых убийствах без обрыва предыдущего звука
* Режим Debug
* Русский / английский интерфейс
* Настраиваемый акцентный цвет
* Белая вспышка, если нет подходящего изображения

## Требования

* Windows 10 или новее
* Counter-Strike 2

## Установка

Скачайте ZIP-архив релиза, распакуйте его и запустите:

`ImageFlasher.exe`

Файлы из ZIP должны оставаться вместе.

При необходимости приложение автоматически создаёт:

`settings.json`

`Images/`

`Sounds/`

## Изображения

Откройте **Настройки → Открыть папку** и поместите изображения в папку `Images`.

Поддерживаемые форматы:

* PNG
* JPG / JPEG
* BMP
* GIF
* TIF / TIFF

После добавления или удаления изображений нажмите **Обновить**.

## Звуки

Откройте **Настройки → Открыть папку** в разделе звука и поместите один звуковой файл в папку `Sounds`.

Поддерживаемые форматы:

* WAV

Используется только один звук. Если в папке несколько поддерживаемых файлов, выбирается первый файл по имени.

Параметр **Включить звук** позволяет включить или отключить воспроизведение звука.

При быстрых убийствах звуки могут накладываться друг на друга, не прерывая предыдущее воспроизведение.

После добавления, удаления или замены звука нажмите **Обновить**.

## Настройки

| Настройка | Описание |
|-|-|
| Включить вспышку | Включает или отключает вспышку |
| Вспышка только при активной CS2 | Показывает вспышку от убийства только когда окно CS2 активно |
| Сохранять пропорции | Сохраняет исходное соотношение сторон изображения |
| Длительность | Общая длительность вспышки |
| Удержание | Время полной яркости перед затуханием |
| Прозрачность | Максимальная прозрачность вспышки |
| Включить звук | Включает или отключает звук убийства |
| Показывать Debug | Показывает надпись Debug |
| Акцент | Изменяет акцентный цвет интерфейса |
| Язык | Переключает русский и английский |

Тест изображения можно запустить из настроек или через меню в трее даже когда CS2 не является активным окном.

## CS2 GSI

Image Flasher использует `CounterStrike2GSI` и запускает локальный GSI listener на порту `3000`.

Необходимая конфигурация GSI создаётся автоматически.

Если убийства не определяются, убедитесь, что CS2 и Image Flasher запущены, и проверьте статус подключения в настройках.

## Файлы

Image Flasher/
├── ImageFlasher.exe
├── settings.json
├── Images/
├── Sounds/
└── остальные файлы приложения

`settings.json` содержит настройки приложения. `Images/` содержит изображения для вспышки. `Sounds/` содержит дополнительный звук убийства.

## Решение проблем

### После убийства вспышка не появляется

Проверьте:

* включён параметр **Включить вспышку**;
* CS2 запущена и является активным окном;
* статус подключения показывает, что CS2 подключена;
* GSI listener запустился.

### Изображения не отображаются

Проверьте формат файлов и нажмите **Обновить**.

Повреждённые изображения автоматически пропускаются.

### Изображение искажено

Включите **Сохранять пропорции**.

### Звук не воспроизводится

Проверьте:

* включён параметр **Включить звук**;
* в `Sounds/` находится поддерживаемый файл `.wav`;
* после добавления или замены звука была нажата кнопка **Обновить**.

## Безопасность для VAC / CS2

Image Flasher **не изменяет файлы CS2 и не внедряет код в игру**. Приложение использует официальный интерфейс **Game State Integration (GSI)**, разработанный Valve, для получения данных о состоянии игры.

GSI используется внешними инструментами, статистикой и broadcast-системами, в том числе на крупных соревновательных мероприятиях и трансляциях Major.

Image Flasher только получает данные через GSI и отображает собственный overlay, не давая игрового преимущества и не изменяя игру.

Нет известных оснований считать, что сама Image Flasher приводит к VAC- или CS2-блокировке.
