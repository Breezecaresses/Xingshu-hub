//! Desktop host boundary. The native build has no Tauri, WebView, or HTTP UI.
#[cfg(feature = "web-ui")]
pub use tauri::{AppHandle, Emitter, State};
#[cfg(feature = "web-ui")]
pub use tauri::async_runtime::{block_on, spawn};

#[cfg(not(feature = "web-ui"))]
mod native {
    use serde::Serialize;
    use serde_json::Value;
    use std::{future::Future, ops::Deref, sync::{Arc, OnceLock}};

    pub struct State<'a, T: ?Sized> { inner: &'a T }
    impl<'a, T: ?Sized> State<'a, T> {
        pub fn new(inner: &'a T) -> Self { Self { inner } }
    }
    impl<T: ?Sized> Deref for State<'_, T> {
        type Target = T;
        fn deref(&self) -> &T { self.inner }
    }
    type EventSink = dyn Fn(&str, Value) + Send + Sync;
    #[derive(Clone)]
    pub struct AppHandle(Arc<EventSink>);
    impl AppHandle {
        pub fn new(sink: impl Fn(&str, Value) + Send + Sync + 'static) -> Self { Self(Arc::new(sink)) }
    }
    pub trait Emitter {
        fn emit<T: Serialize>(&self, event: &str, payload: T) -> Result<(), serde_json::Error>;
    }
    impl Emitter for AppHandle {
        fn emit<T: Serialize>(&self, event: &str, payload: T) -> Result<(), serde_json::Error> {
            (self.0)(event, serde_json::to_value(payload)?);
            Ok(())
        }
    }
    fn runtime() -> &'static tokio::runtime::Runtime {
        static RUNTIME: OnceLock<tokio::runtime::Runtime> = OnceLock::new();
        RUNTIME.get_or_init(|| tokio::runtime::Runtime::new().expect("native task runtime"))
    }
    pub fn spawn<F>(future: F) -> tokio::task::JoinHandle<F::Output>
    where F: Future + Send + 'static, F::Output: Send + 'static {
        match tokio::runtime::Handle::try_current() {
            Ok(handle) => handle.spawn(future),
            Err(_) => runtime().spawn(future),
        }
    }
    pub fn block_on<F: Future>(future: F) -> F::Output { runtime().block_on(future) }
}
#[cfg(not(feature = "web-ui"))]
pub use native::*;
