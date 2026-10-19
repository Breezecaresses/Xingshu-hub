use std::collections::HashMap;
use std::sync::Arc;
use crate::providers::provider::IDataProvider;

#[derive(Default, Clone)]
pub struct ProviderRegistry {
    providers: HashMap<String, Arc<dyn IDataProvider>>,
}

impl ProviderRegistry {
    pub fn new() -> Self {
        Self {
            providers: HashMap::new(),
        }
    }

    pub fn register(&mut self, provider: Arc<dyn IDataProvider>) {
        let id = provider.metadata().id;
        self.providers.insert(id, provider);
    }

    pub fn get(&self, id: &str) -> Option<Arc<dyn IDataProvider>> {
        self.providers.get(id).cloned()
    }

    pub fn list(&self) -> Vec<Arc<dyn IDataProvider>> {
        self.providers.values().cloned().collect()
    }
}
