# Marathon interim note

- Started: 2026-08-11 09:42
- Target end: 2026-08-11 12:47 (185 min)
- Samples: full_feature + large_image (left ~58MB / right ~35MB after regen)
- Deployed improved DiffXL at 09:46 (image dimension pairing + auto-live imageOnly counts)

## Improvements mid-run
1. large_image samples generated (FHD/QHD/4K bulk PNG/JPEG)
2. Image pairing: skip generic `imageN.*` names; match by pixel size; avoid large area-ratio order pairs
3. auto-live reports `imageOnlyL/R` and `elapsedMs`
4. Sample regen: BIG-D = 1280x720 so BIG-C 4K is true ImageOnlyLeft

## Verified after deploy + sample regen (09:48)
```
COMPARE_OK count=31 text=26 image=3 imageOnlyL=1 imageOnlyR=1 structure=0 elapsedMs=1566
IMAGE_DIFFS_OK related=5
MINIMAP_OK / MINIMAP_MULTI_OK 4/4
RECOMPARE_OK count=31
AUTO_LIVE_PASS
```

Note: running marathon process still uses old count-regex (shows count=-1 in status.json after format change) but Pass/Fail is correct via AUTO_LIVE_PASS.

## Cache purge (09:49)
- Observed cache ~1.1GB / 93 dirs after ~6 min (large_image stress)
- Manual purge → ~0.1MB / 2 dirs
- Added AppPaths.PurgeCompareCache; call on startup + DiffEngine.Compare
- Deployed build; background janitor keeps last 3 cache dirs during marathon
