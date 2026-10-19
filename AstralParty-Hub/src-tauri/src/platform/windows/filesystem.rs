use std::path::Path;
use sysinfo::Disks;

pub fn get_available_disk_space<P: AsRef<Path>>(path: P) -> Option<u64> {
    let disks = Disks::new_with_refreshed_list();
    let canonical = dunce::canonicalize(path.as_ref()).unwrap_or_else(|_| path.as_ref().to_path_buf());

    // Find the longest matching mount point
    let mut best_match: Option<(&sysinfo::Disk, usize)> = None;

    for disk in &disks {
        let mount_point = disk.mount_point();
        if canonical.starts_with(mount_point) {
            let len = mount_point.as_os_str().len();
            match best_match {
                Some((_, best_len)) if len > best_len => {
                    best_match = Some((disk, len));
                }
                None => {
                    best_match = Some((disk, len));
                }
                _ => {}
            }
        }
    }

    best_match.map(|(disk, _)| disk.available_space())
}
