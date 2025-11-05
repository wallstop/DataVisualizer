# Testing Guide: Async Loading Implementation

This guide explains how to test the async loading features implemented in Data Visualizer.

## Quick Start Testing

### 1. Enable Debug Logging

Open `Editor/DataVisualizer/DataVisualizer.cs` and change:
```csharp
private static readonly bool EnableAsyncLoadDebugLog = false;
```
to:
```csharp
private static readonly bool EnableAsyncLoadDebugLog = true;
```

This will enable detailed logging in the Unity Console showing:
- When async loading starts
- How many objects are loaded in each batch
- Total time for each batch
- When loading completes

### 2. Test Scenarios

#### **Test A: Fast Initial Load (<250ms goal)**
**Setup:**
- Open a Unity project with 5,000+ ScriptableObjects
- Close Data Visualizer if already open

**Steps:**
1. Open Console window (Ctrl+Shift+C)
2. Clear console
3. Open Data Visualizer: `Tools → Wallstop Studios → Data Visualizer`
4. **Watch for:**
   - Window appears **immediately** (<250ms)
   - "Loading objects..." message appears briefly
   - First 100 objects appear quickly
   - Remaining objects populate in background

**Expected Results:**
- ✅ Window opens instantly (no blocking)
- ✅ Priority batch (100 objects) loads in <250ms
- ✅ UI is interactive immediately
- ✅ Objects continue appearing as batches load

**Debug Log Example:**
```
[DataVisualizer] OnEnable - Starting async initialization at 14:32:15.123
[DataVisualizer] LoadObjectTypesAsync START - Type: MyScriptableObject, Priority: False at 14:32:15.145
[DataVisualizer] Loading priority batch: 100 objects (Total: 5234, Remaining: 5134)
[DataVisualizer] Loaded batch: 100 objects in 45ms (Total loaded: 100)
[DataVisualizer] Queued 5134 objects for background loading
[DataVisualizer] Loaded batch: 100 objects in 52ms (Total loaded: 200)
...
```

#### **Test B: Progressive Loading**
**Steps:**
1. Open Data Visualizer with a type that has 500+ objects
2. Watch the Objects panel

**Expected Results:**
- ✅ First batch appears immediately
- ✅ Objects continue appearing every ~10ms as batches load
- ✅ Scrollbar updates as more objects are added
- ✅ No UI freezing or blocking

**Verification:**
- Count objects in the list - should grow: 100 → 200 → 300 → ...
- Check console logs for batch completion messages

#### **Test C: Type Switching Cancellation**
**Steps:**
1. Select a type with 1000+ objects
2. Wait for ~200 objects to load
3. **Quickly** switch to a different type before loading completes
4. Check console logs

**Expected Results:**
- ✅ Previous loading is cancelled
- ✅ New type starts loading immediately
- ✅ No errors or duplicate loading
- ✅ Only objects from new type appear

**Debug Log Example:**
```
[DataVisualizer] LoadObjectTypesAsync START - Type: TypeA, Priority: False
[DataVisualizer] Loading priority batch: 100 objects (Total: 1500, Remaining: 1400)
[DataVisualizer] Loaded batch: 100 objects in 48ms (Total loaded: 100)
[DataVisualizer] Loaded batch: 100 objects in 51ms (Total loaded: 200)
[DataVisualizer] Cancelling previous async load for TypeA
[DataVisualizer] LoadObjectTypesAsync START - Type: TypeB, Priority: False
[DataVisualizer] Loading priority batch: 50 objects (Total: 50, Remaining: 0)
```

#### **Test D: Search Cache Background Loading**
**Steps:**
1. Open Data Visualizer
2. Wait 1-2 seconds
3. Use the global search box (top of window)
4. Search for objects

**Expected Results:**
- ✅ Search works even while cache is still loading
- ✅ Results appear progressively as cache populates
- ✅ No blocking when searching

**Verification:**
- Search should work immediately (GUIDs collected fast)
- Search results may be incomplete initially but grow

#### **Test E: Window Close During Loading**
**Steps:**
1. Open Data Visualizer with a large dataset
2. Immediately close the window while objects are still loading
3. Check console for errors

**Expected Results:**
- ✅ No errors
- ✅ Loading stops cleanly
- ✅ Memory is freed properly

### 3. Performance Benchmarks

#### Before vs After Comparison

**Before (Synchronous Loading):**
- Projects with 5,000+ objects: **2-10+ seconds** blocking time
- UI frozen during load
- User must wait for everything to load

**After (Async Loading):**
- Projects with 5,000+ objects: **<250ms** to usable state
- UI remains responsive
- Progressive loading in background

#### Measurement Method

1. Enable debug logging
2. Open Data Visualizer
3. Check first log timestamp vs when priority batch completes:
   ```
   OnEnable at 14:32:15.123
   Priority batch loaded at 14:32:15.168
   Difference: 45ms ✅ (<250ms target)
   ```

### 4. Visual Indicators to Check

**What You Should See:**
- ✅ "Loading objects..." message when list is empty and loading
- ✅ Objects appear incrementally (not all at once)
- ✅ Scrollbar grows as more objects load
- ✅ No UI freezing
- ✅ Inspector works immediately on loaded objects

**What You Should NOT See:**
- ❌ Multi-second freeze when opening window
- ❌ All objects appearing at once after a delay
- ❌ UI unresponsive during loading
- ❌ Errors in console
- ❌ Objects from wrong type appearing

### 5. Edge Cases to Test

1. **Empty Types:**
   - Type with 0 objects → Should show "No objects" message instantly

2. **Small Types (<100 objects):**
   - Type with 50 objects → Should load immediately, no batching

3. **Very Large Types (5000+ objects):**
   - Type with 5000+ objects → Should show priority batch, continue loading

4. **Rapid Type Switching:**
   - Switch between types quickly → Should cancel and restart properly

5. **Project Refresh During Load:**
   - Trigger asset refresh (Ctrl+R) while loading → Should handle gracefully

### 6. Console Log Analysis

When debug logging is enabled, look for these patterns:

**Good Pattern (Fast Load):**
```
OnEnable at 14:32:15.123
LoadObjectTypesAsync START at 14:32:15.145  ← 22ms to start
Priority batch loaded in 45ms               ← 45ms total = 67ms ✅
Queued X objects for background
```

**Bad Pattern (Slow Load):**
```
OnEnable at 14:32:15.123
LoadObjectTypesAsync START at 14:32:15.800  ← 677ms to start ❌
Priority batch loaded in 1500ms              ← Too slow ❌
```

### 7. Disabling Debug Logs

After testing, change back to:
```csharp
private static readonly bool EnableAsyncLoadDebugLog = false;
```

This removes performance overhead from logging.

## Success Criteria

✅ **Window opens in <250ms** for projects with 5k+ objects  
✅ **Priority batch loads immediately** (<100ms)  
✅ **UI stays responsive** during background loading  
✅ **Objects appear progressively** as batches complete  
✅ **No errors** in console  
✅ **Type switching cancels** previous loading correctly  
✅ **Search works** while cache is still loading  

## Troubleshooting

**If window still takes >250ms to open:**
- Check if `LoadScriptableObjectTypes()` is taking too long
- Verify `PopulateSearchCacheAsync()` isn't blocking
- Check for other synchronous operations in `OnEnable()`

**If objects don't appear progressively:**
- Verify `ContinueLoadingObjects()` is being called
- Check if `_pendingObjectGuids` has items
- Ensure `BuildObjectsView()` is called after each batch

**If cancellation doesn't work:**
- Verify `_asyncLoadTask?.Pause()` is being called
- Check `_isLoadingObjectsAsync` flag is set correctly

## Advanced Testing

### Manual Batch Size Adjustment

For testing different batch sizes, modify:
```csharp
private const int AsyncLoadBatchSize = 100;  // Try 50, 200, etc.
private const int AsyncLoadPriorityBatchSize = 100;  // Try 50, 200, etc.
```

Smaller batches = more frequent updates, more overhead  
Larger batches = fewer updates, less overhead, more noticeable pauses

