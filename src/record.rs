use std::{
    error::Error,
    fs,
    path::{Path, PathBuf},
    sync::mpsc::{Receiver, RecvTimeoutError},
    time::{Duration, SystemTime, UNIX_EPOCH},
};
use windows_capture::{
    capture::{Context, GraphicsCaptureApiHandler},
    encoder::{AudioSettingsBuilder, ContainerSettingsBuilder, VideoEncoder, VideoSettingsBuilder},
    frame::Frame,
    graphics_capture_api::InternalCaptureControl,
    monitor::Monitor,
    settings::{
        ColorFormat, CursorCaptureSettings, DirtyRegionSettings, DrawBorderSettings,
        GraphicsCaptureItemType, MinimumUpdateIntervalSettings, SecondaryWindowSettings, Settings,
    },
    window::Window,
};
pub type Failure = Box<dyn Error + Send + Sync>;

#[derive(Clone, Copy)]
pub enum Source {
    Screen(Monitor),
    App(Window),
}
pub fn sources() -> Vec<(String, Source)> {
    let mut list = Vec::new();
    for (i, m) in Monitor::enumerate()
        .unwrap_or_default()
        .into_iter()
        .enumerate()
    {
        list.push((format!("Экран {}", i + 1), Source::Screen(m)));
    }
    for w in Window::enumerate().unwrap_or_default() {
        if w.process_id().ok() == Some(std::process::id()) {
            continue;
        }
        if let Ok(title) = w.title()
            && !title.trim().is_empty()
        {
            list.push((title, Source::App(w)));
        }
    }
    list
}

struct Recorder {
    encoder: Option<VideoEncoder>,
    frames: u64,
}
impl GraphicsCaptureApiHandler for Recorder {
    type Flags = (PathBuf, u32, u32, u32);
    type Error = Failure;
    fn new(ctx: Context<Self::Flags>) -> Result<Self, Failure> {
        let (path, w, h, bitrate) = ctx.flags;
        let encoder = VideoEncoder::new(
            VideoSettingsBuilder::new(w, h)
                .frame_rate(60)
                .bitrate(bitrate),
            AudioSettingsBuilder::default().disabled(true),
            ContainerSettingsBuilder::default(),
            path,
        )?;
        Ok(Self {
            encoder: Some(encoder),
            frames: 0,
        })
    }
    fn on_frame_arrived(
        &mut self,
        frame: &mut Frame,
        _: InternalCaptureControl,
    ) -> Result<(), Failure> {
        if let Some(encoder) = self.encoder.as_mut() {
            if self.frames == 0 {
                let start = SystemTime::now().duration_since(UNIX_EPOCH)?.as_millis();
                eprintln!("NAVODKA_FIRST_MS={start}");
            }
            encoder.send_frame(frame)?;
            self.frames += 1;
        }
        Ok(())
    }
}

pub enum Progress {
    Countdown(u8),
    Recording,
    Done(Result<Option<PathBuf>, String>),
}
pub fn record(
    source: Source,
    folder: &Path,
    stop: &Receiver<()>,
    report: &impl Fn(Progress),
) -> Result<Option<PathBuf>, Failure> {
    for n in (1..=3).rev() {
        report(Progress::Countdown(n));
        if !matches!(
            stop.recv_timeout(Duration::from_secs(1)),
            Err(RecvTimeoutError::Timeout)
        ) {
            return Ok(None);
        }
    }
    fs::create_dir_all(folder)?;
    let stamp = SystemTime::now().duration_since(UNIX_EPOCH)?.as_millis();
    let stem = format!("Navodka-{stamp}-{}", std::process::id());
    let tmp = folder.join(format!("{stem}.partial.mp4"));
    let out = folder.join(format!("{stem}.mp4"));
    fs::File::create_new(&tmp)?;
    let result = match source {
        Source::Screen(m) => capture(m, &tmp, stop, report, true, 16_000_000),
        Source::App(w) => capture(w, &tmp, stop, report, true, 16_000_000),
    };
    if let Err(e) = result {
        if fs::metadata(&tmp).is_ok_and(|m| m.len() == 0) {
            let _ = fs::remove_file(&tmp);
        }
        return Err(e);
    }
    fs::rename(tmp, &out)?;
    Ok(Some(out))
}

pub(crate) fn capture<T>(
    item: T,
    path: &Path,
    stop: &Receiver<()>,
    report: &impl Fn(Progress),
    system_cursor: bool,
    bitrate: u32,
) -> Result<(), Failure>
where
    T: TryInto<GraphicsCaptureItemType> + Send + 'static + Dimensions,
{
    let (w, h) = item.dimensions()?;
    let settings = Settings::new(
        item,
        if system_cursor { CursorCaptureSettings::Default } else { CursorCaptureSettings::WithoutCursor },
        DrawBorderSettings::Default,
        SecondaryWindowSettings::Default,
        MinimumUpdateIntervalSettings::Default,
        DirtyRegionSettings::Default,
        ColorFormat::Bgra8,
        (path.to_owned(), (w + 1) & !1, (h + 1) & !1, bitrate),
    );
    let control = Recorder::start_free_threaded(settings)?;
    let callback = control.callback();
    report(Progress::Recording);
    while !control.is_finished() {
        if !matches!(
            stop.recv_timeout(Duration::from_millis(100)),
            Err(RecvTimeoutError::Timeout)
        ) {
            break;
        }
    }
    let stopped = control.stop();
    let mut recorder = callback.lock();
    if let Some(enc) = recorder.encoder.take() {
        enc.finish()?;
    }
    stopped?;
    if recorder.frames == 0 {
        return Err("Источник не передал кадров".into());
    }
    Ok(())
}
pub(crate) trait Dimensions {
    fn dimensions(&self) -> Result<(u32, u32), Failure>;
}
impl Dimensions for Monitor {
    fn dimensions(&self) -> Result<(u32, u32), Failure> {
        Ok((self.width()?, self.height()?))
    }
}
impl Dimensions for Window {
    fn dimensions(&self) -> Result<(u32, u32), Failure> {
        Ok((self.width()?.try_into()?, self.height()?.try_into()?))
    }
}
