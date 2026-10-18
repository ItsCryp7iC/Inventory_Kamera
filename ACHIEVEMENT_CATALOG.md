# Canonical Achievement Catalog

Inventory Kamera packages a deterministic scanner-oriented snapshot at
`InventoryKamera/Resources/Data/achievements-scanner.json`. Its canonical source is:

```text
ItsCryp7iC/travelers-toolkit
src/data/achievements/achievements.json
```

The snapshot uses schema version 1, preserves Toolkit source order, and contains canonical ID,
name, description, category ID, hidden status, version, order, and complete stage metadata when
present. IDs are emitted as validated integers. The generator uses UTF-8 without a byte-order mark,
two-space indentation, LF line endings, fixed field order, and no timestamp.

## Updating the snapshot

1. Pull or otherwise update the separate `travelers-toolkit` checkout.
2. From the Inventory Kamera repository root, run:

   ```powershell
   dotnet run --project tools/InventoryKamera.AchievementCatalogGenerator -- `
     C:\path\to\travelers-toolkit\src\data\achievements\achievements.json `
     InventoryKamera\Resources\Data\achievements-scanner.json
   ```

3. Review the generator's total, hidden, stage-group, and duplicate-name validation output. A
   non-stage/cross-group duplicate is valid but must remain visible for later collision handling.
4. Run the focused catalog tests:

   ```powershell
   dotnet test tests/InventoryKamera.Tests/InventoryKamera.Tests.csproj `
     --filter FullyQualifiedName~AchievementCatalogTests
   ```

5. Inspect `git diff -- InventoryKamera/Resources/Data/achievements-scanner.json` and confirm that
   changes correspond to the Toolkit update.
6. Commit the reviewed generated snapshot with its related catalog changes. Do not hand-edit the
   generated JSON.

The application reads the embedded snapshot and never requires Traveler's Toolkit or network access
at runtime. Set `TRAVELERS_TOOLKIT_ACHIEVEMENTS_JSON` when running catalog tests against a Toolkit
checkout that is not a sibling of this repository.
