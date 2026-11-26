using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Markup;
using System.Xml;
using Microsoft.Win32;

namespace Navodka {
static class Language {
    const string Key = @"HKEY_CURRENT_USER\Software\Snapline";
    internal static readonly bool English = ((string)Registry.GetValue(Key, "Language", null)
        ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) != "ru";
    internal static string Pick(string russian, string english) { return English ? english : russian; }
    internal static void Save(string language) { Registry.SetValue(Key, "Language", language); }

    static readonly Dictionary<string,string> labels = new Dictionary<string,string> {
        { "1 секунда", "1 second" },
        { "3 секунды", "3 seconds" },
        { "30 кадров/с", "30 fps" },
        { "5 секунд", "5 seconds" },
        { "Snapline · Запись", "Snapline · Recorder" },
        { "Snapline · Редактор", "Snapline · Editor" },
        { "Автозум", "Auto zoom" },
        { "Весь экран", "Full screen" },
        { "Включить или выключить микрофон", "Toggle microphone" },
        { "Внутренняя обрезка", "Inset" },
        { "Воспроизведение", "Playback" },
        { "Воспроизвести", "Play" },
        { "Выбери маску и обведи область на видео.", "Select a mask and draw its area on the video." },
        { "Выберите режим записи", "Choose a recording mode" },
        { "Выбор микрофона", "Select microphone" },
        { "Выбрать папку", "Choose folder" },
        { "Выключен", "Off" },
        { "Высота таймлайна", "Timeline height" },
        { "Длительность", "Duration" },
        { "До конца видео", "To end of video" },
        { "Добавить зум", "Add zoom" },
        { "Закрыть", "Close" },
        { "Записать", "Record" },
        { "Звук щелчка", "Click sound" },
        { "Источник записи", "Recording source" },
        { "Исходный", "Original" },
        { "Качество", "Quality" },
        { "Качество записи", "Recording quality" },
        { "Конец фрагмента здесь", "Set clip end here" },
        { "Курсор", "Cursor" },
        { "Максимальное разрешение записи", "Maximum recording resolution" },
        { "Маски", "Masks" },
        { "Микрофон", "Microphone" },
        { "Название", "Name" },
        { "Название файла", "File name" },
        { "Начало фрагмента здесь", "Set clip start here" },
        { "Область", "Area" },
        { "Обрезать по указателю", "Trim at playhead" },
        { "Обрезка", "Trim" },
        { "Окно", "Window" },
        { "Открываем запись…", "Opening recording…" },
        { "Открыть видео, выбрать папку или обновить устройства", "Open a video, choose a folder or refresh devices" },
        { "Открыть изображение…", "Open image…" },
        { "Отменить", "Undo" },
        { "Отступ", "Padding" },
        { "Папка назначения", "Destination folder" },
        { "Поворот при движении", "Rotation while moving" },
        { "Повторить", "Redo" },
        { "Подсветка кликов", "Click highlight" },
        { "Показать файл", "Show file" },
        { "Показывать курсор", "Show cursor" },
        { "Потянуть для изменения высоты таймлайна", "Drag to resize timeline" },
        { "Приближать по клику", "Zoom on click" },
        { "Развернуть или восстановить", "Maximize or restore" },
        { "Размер курсора", "Cursor size" },
        { "Размер холста", "Canvas size" },
        { "Размытие фона", "Background blur" },
        { "Разрешение", "Resolution" },
        { "Разрешение записи", "Recording resolution" },
        { "Сброс", "Reset" },
        { "Сбросить обрезку", "Reset trim" },
        { "Свернуть", "Minimize" },
        { "Свой размер…", "Custom size…" },
        { "Сглаживание", "Smoothing" },
        { "Сглаживать путь курсора", "Smooth cursor path" },
        { "Скругление", "Corner radius" },
        { "Скрывать неподвижный курсор", "Hide idle cursor" },
        { "Следовать за выделением", "Follow selection" },
        { "Сохранить в", "Save to" },
        { "Стиль", "Style" },
        { "Стиль курсора", "Cursor style" },
        { "Таймлайн", "Timeline" },
        { "Тень", "Shadow" },
        { "Убрать ручной", "Remove manual zoom" },
        { "Угол курсора", "Cursor angle" },
        { "Удалить выбранное", "Delete selection" },
        { "Удалить маску", "Remove mask" },
        { "Устройства и запись", "Devices and recording" },
        { "Файл", "File" },
        { "Фиксировать фон при зуме", "Keep background fixed during zoom" },
        { "Фон", "Background" },
        { "Формат", "Format" },
        { "Холст", "Canvas" },
        { "Цвет эффекта", "Effect color" },
        { "Частота кадров", "Frame rate" },
        { "Экспорт", "Export" },
        { "Экспорт видео", "Export video" },
        { "Эффект клика", "Click effect" },
    };
    internal static object LoadView(Stream stream) {
        if (!English) return XamlReader.Load(stream);
        var document = new XmlDocument { XmlResolver = null };
        document.Load(stream);
        foreach (XmlAttribute attribute in document.SelectNodes("//@*")) {
            string translation;
            if (labels.TryGetValue(attribute.Value, out translation)) attribute.Value = translation;
        }
        using (var reader = new XmlNodeReader(document)) return XamlReader.Load(reader);
    }
}
}
