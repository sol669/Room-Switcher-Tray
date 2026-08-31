# RoomSwitcher

[Русский](#русский) · [English](#english)

---

## Русский

**RoomSwitcher** — лёгкая утилита для Windows 10/11, которая переключает готовые сценарии рабочего места одним нажатием в системном трее или горячей клавишей.

Сценарий объединяет подключённые экраны, аудиоустройство и, при необходимости, стартовую громкость. Например: «Кабинет» с двумя мониторами и аудиокартой, «Гостиная» с телевизором или отдельный сценарий для ноутбука.

### Возможности

- До четырёх экранов в одном сценарии.
- Выбор аудиоустройства и необязательная установка громкости при переключении.
- Пользовательские имена экранов и аудиоустройств.
- Палитра из 16 иконок и вариант с собственными литерами.
- Быстрое переключение через меню в трее или `Ctrl + Space`.
- Автозапуск и выбор сценария при входе в Windows.
- Светлая, тёмная или системная тема; русский и английский языки.
- Отдельный безопасный режим для удалённого сеанса RDP.

### Умное поведение при отключённых устройствах

RoomSwitcher не считает звук препятствием для переключения сценария: если аудиоустройство временно отсутствует, доступная конфигурация экранов всё равно применяется.

При этом утилита не подменяет отключённый аудиовыход случайным другим устройством Windows. В меню трея остаётся видно, что именно из сценария сейчас не подключено.

### Скачать

Актуальная версия: [RoomSwitcher 1.0.1](https://github.com/sol669/Room-Switcher-Tray/releases/latest)

Для большинства пользователей рекомендуется:

- `Room-Switcher-Tray-Setup-Self-Contained-v1.0.1.exe` — установщик со всеми необходимыми компонентами; подходит для установки без интернета.

Также доступны:

- `Room-Switcher-Tray-Setup-v1.0.1.exe` — компактный установщик; при необходимости скачает официальные Microsoft .NET 8 и Windows App Runtime.
- `Room-Switcher-Tray-Portable-Self-Contained-win-x64.zip` — переносная версия со всеми зависимостями.
- `Room-Switcher-Tray-Portable-win-x64.zip` — компактная переносная версия; требует установленный .NET 8 и Windows App Runtime 2.3.

Перед обновлением завершите RoomSwitcher через пункт «Выход» в меню трея. Сценарии и настройки сохраняются отдельно и не удаляются.

### Что RoomSwitcher не меняет

Утилита не управляет расположением экранов, разрешением, масштабом, ориентацией и частотой обновления — за это по-прежнему отвечают параметры Windows.

### Требования

- Windows 10 версии 1809 или новее;
- 64-битная система;
- Для компактных версий: Microsoft .NET 8 Runtime и Windows App Runtime 2.3.

---

## English

**RoomSwitcher** is a lightweight Windows 10/11 utility for switching complete workspace scenarios from the system tray or with a hotkey.

A scenario combines connected displays, an audio output, and optionally a startup volume level. For example: an “Office” setup with two monitors and an audio interface, a “Living room” setup with a TV, or a laptop-only scenario.

### Features

- Up to four displays per scenario.
- Audio-output selection and optional volume setting when a scenario is applied.
- Custom names for displays and audio devices.
- A palette of 16 icons, plus a custom initials option.
- Fast switching from the tray menu or with `Ctrl + Space`.
- Windows startup support and a configurable startup scenario.
- Light, dark, or system theme; Russian and English UI.
- A separate safe mode for Remote Desktop sessions.

### Safe behaviour when hardware changes

RoomSwitcher does not treat a missing audio device as a reason to block an otherwise available display scenario.

At the same time, it never silently replaces an unavailable audio output with an unrelated Windows device. The tray menu clearly shows which device from the selected scenario is currently disconnected.

### Download

Latest version: [RoomSwitcher 1.0.1](https://github.com/sol669/Room-Switcher-Tray/releases/latest)

Recommended for most users:

- `Room-Switcher-Tray-Setup-Self-Contained-v1.0.1.exe` — installer with all required components included; suitable for offline installation.

Other options:

- `Room-Switcher-Tray-Setup-v1.0.1.exe` — compact installer; downloads official Microsoft .NET 8 and Windows App Runtime if needed.
- `Room-Switcher-Tray-Portable-Self-Contained-win-x64.zip` — portable build with all dependencies included.
- `Room-Switcher-Tray-Portable-win-x64.zip` — compact portable build; requires .NET 8 and Windows App Runtime 2.3.

Before updating, exit RoomSwitcher from the tray menu. Your scenarios and settings are stored separately and are not removed during an update.

### What RoomSwitcher does not control

RoomSwitcher does not change display layout, resolution, scaling, orientation, or refresh rate. Those settings remain under Windows control.

### Requirements

- Windows 10 version 1809 or later;
- 64-bit system;
- Microsoft .NET 8 Runtime and Windows App Runtime 2.3 for compact builds.
