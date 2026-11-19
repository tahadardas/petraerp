// Petra desktop shell. The shell hosts the React UI and nothing else: no accounting logic, no direct database access,
// no native commands beyond what the window needs. Business data and operations come from the ASP.NET Core API only.
#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .run(tauri::generate_context!())
        .expect("error while running the Petra desktop application");
}
