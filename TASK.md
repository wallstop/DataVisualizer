# Task Progress

## Persist & restore object selection on re-open
- Status: Completed
- Notes: Guarded `ListView.itemsSource` resets so selection change events no longer clear the restored scriptable object; reopen should now retain persisted selection. Manual reasoning used because Unity tests unavailable in this environment.
