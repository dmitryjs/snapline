#![windows_subsystem = "windows"]
mod record;
use native_windows_gui as ui;
use record::{Failure, Progress};
use std::{
    cell::{Cell, RefCell},
    path::PathBuf,
    rc::Rc,
    sync::mpsc,
    thread,
};

fn main() {
    if let Err(e) = run() {
        ui::simple_message("Наводка", &e.to_string());
    }
}

fn run() -> Result<(), Failure> {
    ui::init()?;
    ui::Font::set_global_family("Segoe UI")?;
    let icon = ui::Icon::from_bin(include_bytes!("../brand/navodka.ico"))?;
    let mut window = ui::Window::default();
    ui::Window::builder()
        .title("Наводка")
        .size((512, 276))
        .center(true)
        .flags(ui::WindowFlags::WINDOW | ui::WindowFlags::VISIBLE | ui::WindowFlags::MINIMIZE_BOX)
        .icon(Some(&icon))
        .build(&mut window)?;
    let window = Rc::new(window);
    let label = |text: &str, x, y, w, h| -> Result<ui::Label, ui::NwgError> {
        let mut c = ui::Label::default();
        ui::Label::builder()
            .text(text)
            .position((x, y))
            .size((w, h))
            .parent(&*window)
            .build(&mut c)?;
        Ok(c)
    };
    let button = |text: &str, x, y, w| -> Result<ui::Button, ui::NwgError> {
        let mut c = ui::Button::default();
        ui::Button::builder()
            .text(text)
            .position((x, y))
            .size((w, 32))
            .parent(&*window)
            .build(&mut c)?;
        Ok(c)
    };
    let _heading = label("Источник записи", 20, 15, 465, 22)?;
    let mut picker = ui::ComboBox::<String>::default();
    ui::ComboBox::builder()
        .position((20, 42))
        .size((358, 200))
        .parent(&*window)
        .build(&mut picker)?;
    let refresh = button("Обновить", 388, 40, 102)?;
    let folder_button = button("Папка…", 20, 91, 102)?;
    let default_folder = std::env::var_os("USERPROFILE")
        .map(PathBuf::from)
        .ok_or("Не найдена папка пользователя")?
        .join("Videos")
        .join("Наводка");
    let directory = RefCell::new(default_folder);
    let folder_label = label(&directory.borrow().display().to_string(), 132, 97, 355, 35)?;
    let action = button("Записать", 20, 149, 214)?;
    let preview = button("Открыть последнюю запись", 244, 149, 245)?;
    preview.set_enabled(false);
    let status = label("Готово к записи экрана без звука", 20, 201, 470, 65)?;
    let mut dialog = ui::FileDialog::default();
    ui::FileDialog::builder()
        .action(ui::FileDialogAction::OpenDirectory)
        .title("Куда сохранять записи")
        .build(&mut dialog)?;
    let mut notice = ui::Notice::default();
    ui::Notice::builder().parent(&*window).build(&mut notice)?;
    let sources = RefCell::new(record::sources());
    picker.set_collection(sources.borrow().iter().map(|s| s.0.clone()).collect());
    picker.set_selection(Some(0));
    let (tx, rx) = mpsc::channel::<Progress>();
    let active = RefCell::new(None::<mpsc::Sender<()>>);
    let last = RefCell::new(None::<PathBuf>);
    let closing = Cell::new(false);
    let owner = window.clone();
    let handler = ui::full_bind_event_handler(&window.handle, move |event, data, handle| {
        use ui::Event::*;
        let busy = |value: bool| {
            picker.set_enabled(!value);
            refresh.set_enabled(!value);
            folder_button.set_enabled(!value);
        };
        let stop = || {
            if let Some(sender) = active.borrow().as_ref() {
                let _ = sender.send(());
            }
            action.set_enabled(false);
            status.set_text("Завершаем запись…");
        };
        match event {
            OnButtonClick if handle == refresh.handle => {
                *sources.borrow_mut() = record::sources();
                picker.set_collection(sources.borrow().iter().map(|s| s.0.clone()).collect());
                picker.set_selection(Some(0));
            }
            OnButtonClick if handle == folder_button.handle => {
                if dialog.run(Some(&*owner))
                    && let Ok(p) = dialog.get_selected_item()
                {
                    *directory.borrow_mut() = PathBuf::from(p);
                    folder_label.set_text(&directory.borrow().display().to_string());
                }
            }
            OnButtonClick if handle == preview.handle => {
                if let Some(path) = last.borrow().as_ref()
                    && let Err(e) = open::that(path)
                {
                    ui::modal_error_message(&*owner, "Просмотр", &e.to_string());
                }
            }
            OnButtonClick if handle == action.handle => {
                if active.borrow().is_some() {
                    stop();
                    return;
                }
                let Some(source) = picker
                    .selection()
                    .and_then(|i| sources.borrow().get(i).map(|s| s.1))
                else {
                    status.set_text("Выбери экран или окно");
                    return;
                };
                let (end, receiver) = mpsc::channel();
                *active.borrow_mut() = Some(end);
                busy(true);
                preview.set_enabled(false);
                action.set_text("Отменить");
                let folder = directory.borrow().clone();
                let tx = tx.clone();
                let notify = notice.sender();
                thread::spawn(move || {
                    let report = |event| {
                        let _ = tx.send(event);
                        notify.notice();
                    };
                    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                        record::record(source, &folder, &receiver, &report)
                    }))
                    .map_err(|_| "Сбой службы записи Windows".to_string())
                    .and_then(|r| r.map_err(|e| e.to_string()));
                    report(Progress::Done(result));
                });
            }
            OnNotice if handle == notice.handle => {
                for event in rx.try_iter() {
                    match event {
                        Progress::Countdown(n) => status.set_text(&format!("До записи: {n}")),
                        Progress::Recording => {
                            status.set_text("Идёт запись");
                            action.set_text("Остановить и сохранить");
                        }
                        Progress::Done(result) => {
                            active.borrow_mut().take();
                            busy(false);
                            action.set_enabled(true);
                            action.set_text("Записать");
                            match result {
                                Ok(Some(path)) => {
                                    status.set_text(&format!("Сохранено: {}", path.display()));
                                    *last.borrow_mut() = Some(path);
                                }
                                Ok(None) => status.set_text("Запись отменена"),
                                Err(e) => {
                                    status.set_text("Ошибка записи");
                                    ui::modal_error_message(&*owner, "Запись", &e);
                                }
                            }
                            preview.set_enabled(last.borrow().is_some());
                            if closing.get() {
                                ui::stop_thread_dispatch();
                            }
                        }
                    }
                }
            }
            OnWindowClose => {
                if active.borrow().is_some() {
                    if let ui::EventData::OnWindowClose(d) = data {
                        d.close(false);
                    }
                    closing.set(true);
                    stop();
                } else {
                    ui::stop_thread_dispatch();
                }
            }
            _ => {}
        }
    });
    ui::dispatch_thread_events();
    ui::unbind_event_handler(&handler);
    Ok(())
}
