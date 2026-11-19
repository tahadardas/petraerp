use std::io::Write;

fn log(msg: &str) {
    if let Ok(local) = std::env::var("LOCALAPPDATA") {
        let p = std::path::PathBuf::from(local)
            .join("com.petraerp.desktop")
            .join("tauri_app.log");
        if let Some(parent) = p.parent() {
            let _ = std::fs::create_dir_all(parent);
        }
        if let Ok(mut f) = std::fs::OpenOptions::new().create(true).append(true).open(&p) {
            let _ = writeln!(f, "[{:?}] {}", std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_secs(), msg);
        }
    }
}

// Petra desktop shell. The shell hosts the React UI and nothing else: no accounting logic, no direct database access,
// no native commands beyond what the window needs. Business data and operations come from the ASP.NET Core API only.
#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    std::panic::set_hook(Box::new(|info| {
        log(&format!("PANIC: {}", info));
    }));

    log("Tauri run() starting");

    let result = tauri::Builder::default()
        .setup(|_app| {
            log("Tauri setup callback succeeded");
            Ok(())
        })
        .on_window_event(|window, event| {
            log(&format!("Window event on {:?}: {:?}", window.label(), event));
        })
        .run(tauri::generate_context!());

    match result {
        Ok(_) => log("Tauri exited with Ok"),
        Err(e) => log(&format!("Tauri exited with Err: {:?}", e)),
    }
}
