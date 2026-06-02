
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{Duration, Instant};

use parking_lot::Mutex;

use crate::ipc::protocol::{ElementInfo, Rect};

/// How long a cached entry remains valid before being considered stale.
const CACHE_TTL: Duration = Duration::from_millis(200);

/// Maximum number of cached entries.
const CACHE_CAPACITY: usize = 64;

// ── Cache entry ───────────────────────────────────────────────────────────────

#[derive(Clone)]
struct CacheEntry {
    info: ElementInfo,
    inserted_at: Instant,
    /// The HWND this result was associated with (used as primary key).
    hwnd: isize,
}

impl CacheEntry {
    fn is_fresh(&self) -> bool {
        self.inserted_at.elapsed() < CACHE_TTL
    }
}


struct LastHitCache {
    x: i32,
    y: i32,
    result: ElementInfo,
    valid: bool,
    inserted_at: Instant,
}

impl LastHitCache {
    fn empty() -> Self {
        Self {
            x: i32::MIN,
            y: i32::MIN,
            result: ElementInfo {
                name: String::new(),
                control_type: String::new(),
                automation_id: String::new(),
                class_name: String::new(),
                process_id: 0,
                framework: String::new(),
                rect: Rect::default(),
                is_enabled: false,
                is_keyboard_focusable: false,
                hwnd: 0,
            },
            valid: false,
            inserted_at: Instant::now(),
        }
    }

    fn is_hit(&self, x: i32, y: i32) -> bool {
        self.valid
            && self.x == x
            && self.y == y
            && self.inserted_at.elapsed() < CACHE_TTL
    }
}

// ── HWND element cache ────────────────────────────────────────────────────────

struct HwndCache {
    entries: Vec<CacheEntry>,
}

impl HwndCache {
    fn new() -> Self {
        Self {
            entries: Vec::with_capacity(CACHE_CAPACITY),
        }
    }

    fn get(&self, hwnd: isize) -> Option<ElementInfo> {
        self.entries
            .iter()
            .rev() // most recently inserted is likely to match
            .find(|e| e.hwnd == hwnd && e.is_fresh())
            .map(|e| e.info.clone())
    }

    fn insert(&mut self, hwnd: isize, info: ElementInfo) {
        // Evict stale entries
        self.entries.retain(|e| e.is_fresh());

        // Evict oldest if at capacity
        if self.entries.len() >= CACHE_CAPACITY {
            self.entries.remove(0);
        }

        self.entries.push(CacheEntry {
            info,
            inserted_at: Instant::now(),
            hwnd,
        });
    }

    #[allow(dead_code)]
    fn invalidate(&mut self, hwnd: isize) {
        self.entries.retain(|e| e.hwnd != hwnd);
    }

    fn clear(&mut self) {
        self.entries.clear();
    }
}

// ── Public ElementCache ───────────────────────────────────────────────────────

pub struct ElementCache {
    hwnd_cache: Mutex<HwndCache>,
    last_hit: Mutex<LastHitCache>,
    // Counters
    hits: AtomicU64,
    misses: AtomicU64,
}

impl ElementCache {
    pub fn new() -> Self {
        Self {
            hwnd_cache: Mutex::new(HwndCache::new()),
            last_hit: Mutex::new(LastHitCache::empty()),
            hits: AtomicU64::new(0),
            misses: AtomicU64::new(0),
        }
    }

    /// Look up the element at an exact (x, y) point using last-hit cache.
    pub fn get_by_point(&self, x: i32, y: i32) -> Option<ElementInfo> {
        let last = self.last_hit.lock();
        if last.is_hit(x, y) {
            self.hits.fetch_add(1, Ordering::Relaxed);
            return Some(last.result.clone());
        }
        drop(last);
        self.misses.fetch_add(1, Ordering::Relaxed);
        None
    }

    /// Cache a point query result.
    pub fn put_by_point(&self, x: i32, y: i32, info: ElementInfo) {
        let mut last = self.last_hit.lock();
        last.x = x;
        last.y = y;
        last.result = info.clone();
        last.valid = true;
        last.inserted_at = Instant::now();
        drop(last);

        // Also store in hwnd cache
        if info.hwnd != 0 {
            self.hwnd_cache.lock().insert(info.hwnd, info);
        }
    }

    /// Look up by HWND (used when we already know the window).
    pub fn get_by_hwnd(&self, hwnd: isize) -> Option<ElementInfo> {
        let result = self.hwnd_cache.lock().get(hwnd);
        if result.is_some() {
            self.hits.fetch_add(1, Ordering::Relaxed);
        } else {
            self.misses.fetch_add(1, Ordering::Relaxed);
        }
        result
    }

    /// Insert or update a cached entry by HWND.
    pub fn put_by_hwnd(&self, hwnd: isize, info: ElementInfo) {
        self.hwnd_cache.lock().insert(hwnd, info);
    }

    /// Invalidate all cached data for a specific HWND.
    #[allow(dead_code)]
    pub fn invalidate_hwnd(&self, hwnd: isize) {
        self.hwnd_cache.lock().invalidate(hwnd);
        // Also invalidate last-hit if it belongs to this hwnd
        let mut last = self.last_hit.lock();
        if last.result.hwnd == hwnd {
            last.valid = false;
        }
    }

    /// Clear all cache data.
    pub fn clear(&self) {
        self.hwnd_cache.lock().clear();
        self.last_hit.lock().valid = false;
    }

    pub fn hit_count(&self) -> u64 {
        self.hits.load(Ordering::Relaxed)
    }

    pub fn miss_count(&self) -> u64 {
        self.misses.load(Ordering::Relaxed)
    }
}

impl Default for ElementCache {
    fn default() -> Self {
        Self::new()
    }
}
