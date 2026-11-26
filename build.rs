fn main() {
    println!("cargo:rerun-if-changed=brand/navodka.ico");
    let mut res = winresource::WindowsResource::new();
    res.set_icon("brand/navodka.ico")
        .set("ProductName", "Наводка")
        .set("FileDescription", "Наводка — запись экрана");
    if let Ok(tool) = std::env::var("NAVODKA_WINDRES") {
        res.set_windres_path(&tool);
    }
    if let Ok(tool) = std::env::var("NAVODKA_AR") {
        res.set_ar_path(&tool);
    }
    res.compile().expect("Windows icon resource");
}
