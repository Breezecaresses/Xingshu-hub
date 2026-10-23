use crate::xing_shu::{
    bridge::server::BridgeServer,
    local_watcher::{LocalWatcher, LocalWatcherInfo},
};
use std::sync::Arc;
use crate::desktop_host::State;

#[cfg_attr(feature = "web-ui", tauri::command)]
pub fn managed_local_status(
    watcher: State<'_, LocalWatcher>,
    bridge: State<'_, Arc<BridgeServer>>,
    session_id: Option<String>,
) -> LocalWatcherInfo {
    watcher.status(&bridge, session_id.as_deref())
}

#[cfg_attr(feature = "web-ui", tauri::command)]
pub fn managed_local_allocate(
    watcher: State<'_, LocalWatcher>,
    bridge: State<'_, Arc<BridgeServer>>,
    room_id: Option<i64>,
    watch_code: Option<String>,
    session_id: Option<String>,
) -> Result<LocalWatcherInfo, String> {
    watcher.allocate(
        &bridge,
        room_id,
        watch_code.as_deref(),
        session_id.as_deref(),
    )
}

#[cfg_attr(feature = "web-ui", tauri::command)]
pub fn managed_local_release(
    watcher: State<'_, LocalWatcher>,
    session_id: String,
) -> Result<(), String> {
    watcher.release(&session_id)
}
