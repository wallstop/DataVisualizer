# Task Progress

## Persist & restore object selection on re-open
- Status: Completed
- Notes: Guarded `ListView.itemsSource` resets so selection change events no longer clear the restored scriptable object; reopen should now retain persisted selection. Manual reasoning used because Unity tests unavailable in this environment.

## Highlight Reset State warning confirmation
- Status: Completed
- Notes: Styled the reset button with warning colors and re-used the standard confirmation overlay with a bold "DANGER" action label.
