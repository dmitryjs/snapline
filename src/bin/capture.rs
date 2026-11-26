#[path = "../record.rs"]
#[allow(dead_code)]
mod record;
use std::{env, io, path::PathBuf, sync::mpsc, thread};
use windows_capture::{monitor::Monitor, window::Window};

fn main() {
    if let Err(error) = run() {
        eprintln!("{error}");
        std::process::exit(1);
    }
}
fn run() -> Result<(), record::Failure> {
    let mut args = env::args_os().skip(1);
    let mode = args.next().ok_or("Missing capture mode")?;
    let handle: usize = args.next().ok_or("Missing handle")?.to_str().ok_or("Invalid handle")?.parse()?;
    let path = PathBuf::from(args.next().ok_or("Missing output")?);
    let system_cursor = args.next().as_deref() != Some(std::ffi::OsStr::new("clean"));
    let bitrate: u32 = match args.next() {
        Some(value) => value.to_str().ok_or("Invalid bitrate")?.parse()?,
        None => 16_000_000,
    };
    if !(1_000_000..=160_000_000).contains(&bitrate) {
        return Err("Unsupported capture bitrate".into());
    }
    std::fs::File::create_new(&path)?;
    let (tx, stop) = mpsc::channel();
    thread::spawn(move || {
        let mut line = String::new();
        let _ = io::stdin().read_line(&mut line);
        let _ = tx.send(());
    });
    if mode == "monitor" {
        record::capture(Monitor::from_raw_hmonitor(handle as _), &path, &stop, &|_| {}, system_cursor, bitrate)?;
    } else if mode == "window" {
        record::capture(Window::from_raw_hwnd(handle as _), &path, &stop, &|_| {}, system_cursor, bitrate)?;
    } else { return Err("Unknown capture mode".into()); }
    Ok(())
}
