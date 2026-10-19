use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt, EnvFilter};

pub struct HubLogger;

impl HubLogger {
    pub fn init() {
        let filter = EnvFilter::try_from_default_env()
            .unwrap_or_else(|_| EnvFilter::new("info,astralparty_hub=debug"));

        let fmt_layer = tracing_subscriber::fmt::layer()
            .with_target(false)
            .compact();

        let _ = tracing_subscriber::registry()
            .with(filter)
            .with(fmt_layer)
            .try_init();
    }
}
