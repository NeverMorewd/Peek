
use thiserror::Error;

#[derive(Debug, Error)]
pub enum WorkerError {
    #[error("UIA error: {0}")]
    Uia(#[from] uiautomation::Error),

    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),

    #[error("JSON error: {0}")]
    Json(#[from] serde_json::Error),

    #[error("Element not found at point ({x}, {y})")]
    ElementNotFound { x: i32, y: i32 },

    #[error("IPC pipe broken")]
    PipeBroken,

    #[error("Request timeout")]
    #[allow(dead_code)]
    Timeout,

    #[allow(dead_code)]
    #[error("Invalid request: {0}")]
    InvalidRequest(String),

    #[allow(dead_code)]
    #[error("Worker shutting down")]
    ShuttingDown,
}

pub type WorkerResult<T> = Result<T, WorkerError>;
