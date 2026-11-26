<p align="center"><img src="assets/icon.png" width="112" alt="Иконка Snapline"></p>
<h1 align="center">Snapline</h1>
<p align="center">Запись экрана и монтаж скринкастов на Windows и Mac.</p>

<p align="center">
  <a href="https://raw.githubusercontent.com/dmitryjs/snapline/1aea5af5db12d345180041f377fee5551a0ddcfd/Snapline-Windows-0.9.1-x64.zip"><img alt="Скачать для Windows x64" src="https://img.shields.io/badge/Windows_x64-0.9.1-316CFF?style=for-the-badge"></a>
  <a href="https://raw.githubusercontent.com/dmitryjs/snapline/fb0b59705271315397d95f4218a9e3e772b16a32/Snapline-macOS-0.9.1-preview.zip"><img alt="Скачать для Mac (Intel и Apple Silicon)" src="https://img.shields.io/badge/macOS_Universal-0.9.1_preview-6357DB?style=for-the-badge"></a>
</p>
<p align="center"><a href="#install">Установка</a> · <a href="#editor">Редактор</a> · <a href="https://github.com/dmitryjs/snapline/releases">Все версии</a></p>

## Проверка VirusTotal

**26 ноября 2026 года.** Проверены основные исполняемые файлы из опубликованных ZIP. Windows — **3 из 62** (CrowdStrike Falcon, DrWeb, SecureAge). macOS — **1 из 62** (Microsoft, Trojan:Script/Wacatac.B!ml). Причины срабатываний пока не установлены. До окончания разбора рекомендуем отложить запуск. Отчёты относятся только к указанным файлам.

[Отчёт Windows — Snapline.exe](https://www.virustotal.com/gui/file/e68705fdbf74209dd279f551ffe27206817c5ed79062c842a9f84a78b140e286) · [Отчёт macOS — Snapline.app/Contents/MacOS/Snapline](https://www.virustotal.com/gui/file/34995906eb417cbe5c806e2e425ca61f9aec462e3227b16ab1cec1b2695f5f24)

<a id="install"></a>

## Установка

| Система | Как запустить |
| --- | --- |
| Windows 10 22H2 или Windows 11, x64 | Распакуйте весь ZIP в одну папку и откройте Snapline.exe. |
| macOS 10.15+ (Intel) или 11+ (Apple Silicon), предварительная версия | Распакуйте ZIP, перенесите Snapline.app в «Программы» и откройте. |

<a id="editor"></a>

## Редактор

В версии для Windows можно обрезать запись, перемещать зум на таймлайне и добавлять маски. При приближении фон с отступами остаётся на месте.

![Редактор Snapline для Windows с предпросмотром и таймлайном](assets/editor.png)

В предварительной сборке для Mac доступны запись экрана или области, микрофон, пауза записи, выбор разрешения до 2160p, автозум по кликам, ручной зум, маски на три секунды, градиенты, обрезка и экспорт MP4. Для повторного редактирования автозума храните файл .snapclicks рядом с оригинальным видео. На macOS 15 проверены запуск опубликованного приложения и экспорт тестового MP4 с автозумом и фиксированным фоном. Захват реального экрана с разрешениями macOS ещё требует проверки. [Результат теста](https://github.com/dmitryjs/snapline/actions/runs/45769644832). Расширенные настройки курсора и дорожки эффектов пока доступны в Windows.

