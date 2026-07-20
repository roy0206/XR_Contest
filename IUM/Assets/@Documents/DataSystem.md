# IUM Data System

## Storage

- Static JSON: `Assets/@AddressableAssets/Data/Static` (`TextAsset` Addressables)
- UserData at runtime: `Application.persistentDataPath/UserData/user.json`
- UserData backup: `user.json.backup`
- Corrupt saves are preserved as `user.corrupt-YYYYMMDD-HHMMSS.json`

Static data is loaded through `AddressableDataProvider`. UserData is never placed in Addressables.

## Manifest

Register every shipped data file in `Assets/@AddressableAssets/Data/manifest.json` and add the asset to Addressables.

```json
{
  "key": "items",
  "address": "ium/data/static/items",
  "format": "json",
  "category": "static",
  "required": true
}
```

The current manifest contains static JSON entries only.

The manifest address is `ium/data/manifest`. Data assets use the `ium-data` label. Keep addresses stable even if the physical asset path changes.

For remote delivery later, move the data entries to a remote group and enable remote catalog/content update settings. Runtime services do not need to change because they depend on `IDataTextProvider`, not asset paths.

## Runtime API

```csharp
await DataManager.Instance.InitializeAsync();

var table = DataManager.Instance.Static.Get<MyTable>("items");

DataManager.Instance.User.Update(data => data.Progress.CurrentChapter = "chapter_02");
await DataManager.Instance.SaveUserAsync();
```

## UserData schema changes

Increase `UserDataMigrator.CurrentSchemaVersion`, add a migration step for the previous version, and keep old fields readable until the migration has completed. Unknown JSON fields are retained through `JsonExtensionData`.
