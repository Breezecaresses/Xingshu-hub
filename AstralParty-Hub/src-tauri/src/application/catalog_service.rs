use crate::domain::card::Card;
use crate::domain::character::Character;
use crate::domain::error::HubResult;
use crate::storage::database::Database;
use crate::storage::repositories::CatalogRepository;

pub struct CatalogService {
    catalog_repo: CatalogRepository,
}

impl CatalogService {
    pub fn new(db: Database) -> Self {
        Self {
            catalog_repo: CatalogRepository::new(db),
        }
    }

    pub fn list_characters(&self) -> HubResult<Vec<Character>> {
        self.catalog_repo.list_characters()
    }

    pub fn list_cards(&self) -> HubResult<Vec<Card>> {
        self.catalog_repo.list_cards()
    }
}
